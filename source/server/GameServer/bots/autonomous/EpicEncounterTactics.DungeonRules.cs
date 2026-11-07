using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Dungeon rules (1.2, epic wave 2): an encounter's own raid rule written in code, for a mechanic the catalog's generic
    /// fields can't say (a twin that heals the other, a damage type that heals, a boss hurt only from close by, a timed scream
    /// around the boss's target, a jail the raid sends a team to open, a class and weapon puzzle, a patrol to ambush). Each
    /// dungeon keeps its rules in its own partial file (EpicEncounterTactics.&lt;Dungeon&gt;.cs) in a static method marked
    /// <see cref="DungeonRulesAttribute"/> that returns them; they are found by reflection, so dungeons never share a list
    /// (and never conflict when merged). This is the one per-dungeon rule mechanism: wave 2's Midgard/Hibernia rules
    /// (#103) and Albion's (#105, which had its own [EncounterRule] methods) were unified into it when merged.
    /// <para>A rule applies to a raider whose target is one of its <see cref="DungeonRule.Targets"/> (class names; a subclass
    /// of one counts too). It runs in <see cref="Decide"/> after the break-off and the announced mechanics
    /// (EpicEncounterTactics.Signals.cs) and before the catalog's generic rules, hazards included: the first rule of the
    /// target that gives an advice wins. A rule returns null when it has no opinion; any advice it returns is final, Action
    /// None included (fight the target as usual and skip the catalog's rules: Funus's clerics, the Sacristan's ambush).
    /// A rule can also forbid damage types on its target (<see cref="ForbidsSpell"/>, for every bot), forbid some bots to
    /// hit it at all, area spells included (<see cref="DungeonRule.MustNotHit"/>), act on the bot each turn
    /// (<see cref="DungeonRule.Act"/>), name the party's call for its decision (<see cref="Advice.Call"/>, said in /group
    /// where the real player is, EncounterMechanicCalls), and change what the raid route fights (<see cref="DungeonRule.Route"/>).</para>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>Marks a static method returning <c>IEnumerable&lt;DungeonRule&gt;</c>: one dungeon's rules.</summary>
        [AttributeUsage(AttributeTargets.Method)]
        internal sealed class DungeonRulesAttribute : Attribute { }

        /// <summary>What a dungeon rule sees: one raider about to attack <see cref="Target"/>.</summary>
        public readonly record struct RuleContext(GameLiving Raider, GameNPC Target, IReadOnlyList<GameNPC> Live, int PartyIndex,
            bool CanShootFlyers, long Now, int ClassId)
        {
            public Vector3 At => new(Raider.X, Raider.Y, Raider.Z);
            /// <summary>The raider is the one the target fights, or in its party.</summary>
            public bool Holds => EpicEncounterTactics.Holds(Raider, Target);
            /// <summary>The raider fights from range (caster, archer) or heals.</summary>
            public bool AtRange => CanShootFlyers || ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)ClassId);
            /// <summary>The living NPC of this class near the target (the target itself when it is one), or null.</summary>
            public GameNPC Find(string type) => FindNear(Target, Live, type);
            /// <summary>Every living NPC of this class near the target, nearest the raider first.</summary>
            public GameNPC[] All(string type)
            {
                GameNPC target = Target;
                GameLiving raider = Raider;
                return (Live ?? []).Where(n => Present(n) && n.GetType().Name == type && n.CurrentRegionID == target.CurrentRegionID &&
                                               target.IsWithinRadius(n, AddSearchRadius))
                    .OrderBy(n => raider.GetDistanceTo(n)).ThenBy(n => n.ObjectID).ToArray();
            }
        }

        /// <summary>What a rule says about the monster the raid route chose (null: nothing); <paramref name="reason"/> for the log.</summary>
        public delegate GameNPC RouteRule(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason);

        /// <summary>One encounter's code rule. <paramref name="Targets"/>: the class names of the NPCs it applies to as the
        /// raider's target (the boss, its partner, its adds; a subclass counts too). <paramref name="Decide"/>: null when it has
        /// no opinion (null itself: the rule only acts, forbids or routes).</summary>
        public sealed record DungeonRule(string Encounter, string[] Targets, Func<RuleContext, Advice?> Decide)
        {
            /// <summary>What the rule does, for logs and the docs.</summary>
            public string Summary { get; init; }
            /// <summary>Damage types the rule's targets must not take from bots (Kvasir: cold heals him), while
            /// <see cref="ForbidsWhile"/> says so (null: always).</summary>
            public eDamageType[] Forbids { get; init; } = [];
            public Func<GameNPC, bool> ForbidsWhile { get; init; }
            /// <summary>Done for the bot on each of its tactic turns before the decision (face the boss, start a team's
            /// errand). Autonomous gamebots only, like the decision.</summary>
            public Action<GameBot, GameNPC, IReadOnlyList<GameNPC>> Act { get; init; }
            /// <summary>This bot must not hit the target at all, not even with an area spell aimed at another monster (Funus
            /// while his puzzle is on: the wrong class heals him to full). Every bot, companions too (BotBrain).</summary>
            public Func<GameBot, GameNPC, bool> MustNotHit { get; init; }
            /// <summary>What the raid route fights instead of the monster it chose (the Crypt Lord for one of his court), asked
            /// for every chosen monster whatever <see cref="Targets"/> says.</summary>
            public RouteRule Route { get; init; }
            /// <summary>This bot fights the target with its distance weapon only (Funus: a scout's longbow, a mercenary's short
            /// bow, an armsman's or infiltrator's crossbow): BotBrain draws it whatever its class's usual stance, never falls
            /// back to melee on him, and waits for endurance rather than swing (epic wave 3, 1.2). Every bot, companions too.</summary>
            public Func<GameBot, GameNPC, bool> FightsAtRange { get; init; }
            /// <summary>While true, no bot's pet attacks the target, defends its owner against it or takes it from its owner's
            /// orders (Funus: a pet's hit heals him to full). ControlledMobBrain asks <see cref="PetMustNotHit"/>.</summary>
            public Func<GameNPC, bool> NoPets { get; init; }
        }

        private sealed record RuleSet(DungeonRule[] All, Dictionary<string, DungeonRule[]> ByTarget, DungeonRule[] Routes);

        private static readonly object DungeonRuleLock = new();
        private static readonly List<DungeonRule> ExtraDungeonRules = new();
        private static RuleSet _dungeonRules;
        /// <summary>The rules of each NPC class (its own name's and its base classes' up to GameNPC), built once per class.</summary>
        private static readonly ConcurrentDictionary<Type, DungeonRule[]> RulesByType = new();

        private static RuleSet DungeonRuleSet
        {
            get
            {
                lock (DungeonRuleLock)
                {
                    if (_dungeonRules != null) return _dungeonRules;
                    var all = new List<DungeonRule>();
                    // In method name order, so the order of two dungeons' rules for one class never depends on reflection.
                    foreach (MethodInfo m in typeof(EpicEncounterTactics).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                 .Where(m => m.IsDefined(typeof(DungeonRulesAttribute), false) && m.GetParameters().Length == 0)
                                 .OrderBy(m => m.Name, StringComparer.Ordinal))
                        if (m.Invoke(null, null) is IEnumerable<DungeonRule> rules)
                            all.AddRange(rules.Where(r => r != null));
                    all.AddRange(ExtraDungeonRules);
                    var byTarget = all.SelectMany(r => (r.Targets ?? []).Select(t => (t, r)))
                        .GroupBy(p => p.t, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Select(p => p.r).Distinct().ToArray(), StringComparer.Ordinal);
                    RulesByType.Clear();
                    return _dungeonRules = new RuleSet(all.ToArray(), byTarget, all.Where(r => r.Route != null).ToArray());
                }
            }
        }

        /// <summary>Every dungeon rule (each once).</summary>
        public static IReadOnlyList<DungeonRule> AllDungeonRules => DungeonRuleSet.All;

        /// <summary>Adds a rule (tests).</summary>
        public static void RegisterDungeonRule(DungeonRule rule)
        {
            lock (DungeonRuleLock) { ExtraDungeonRules.Add(rule); _dungeonRules = null; RulesByType.Clear(); }
        }

        public static void UnregisterDungeonRule(DungeonRule rule)
        {
            lock (DungeonRuleLock) { ExtraDungeonRules.Remove(rule); _dungeonRules = null; RulesByType.Clear(); }
        }

        /// <summary>The rules for this NPC as a raider's target: those naming its class or one of its base classes (a test's
        /// or a variant's subclass of a boss asks the boss's rules), the class's own first.</summary>
        public static DungeonRule[] DungeonRulesFor(GameNPC npc)
        {
            if (npc == null) return [];
            RuleSet set = DungeonRuleSet;
            return RulesByType.GetOrAdd(npc.GetType(), type =>
            {
                var found = new List<DungeonRule>();
                for (Type t = type; t != null && t != typeof(GameNPC) && t != typeof(object); t = t.BaseType)
                    if (set.ByTarget.TryGetValue(t.Name, out DungeonRule[] rules))
                        found.AddRange(rules.Where(r => !found.Contains(r)));
                return found.ToArray();
            });
        }

        public static bool HasDungeonRule(GameNPC npc) => DungeonRulesFor(npc).Length > 0;

        /// <summary>The first dungeon rule of the target that gives an advice, or null (no rule has an opinion).</summary>
        public static Advice? DungeonAdvice(in RuleContext c)
        {
            if (c.Target == null) return null;
            foreach (DungeonRule rule in DungeonRulesFor(c.Target))
            {
                if (rule.Decide == null) continue;
                try
                {
                    if (rule.Decide(c) is Advice a) return a;
                }
                catch (Exception ex)
                {
                    RuleWarn($"EPIC_TACTIC dungeon rule {rule.Encounter} failed: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>A failing rule's warning; a logger that isn't up (the tests) never turns the skipped rule into a failed turn.</summary>
        private static void RuleWarn(string message)
        {
            try { Log.Warn(message); }
            catch (Exception) { }
        }

        /// <summary>What the raid route fights instead of <paramref name="chosen"/> by a dungeon rule's route (null: none).</summary>
        public static GameNPC DungeonRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || live == null) return null;
            foreach (DungeonRule rule in DungeonRuleSet.Routes)
            {
                try
                {
                    if (rule.Route(chosen, live, out reason) is { } other && other != chosen) return other;
                }
                catch (Exception ex)
                {
                    RuleWarn($"EPIC_TACTIC dungeon route {rule.Encounter} failed: {ex.Message}");
                }
            }
            reason = null;
            return null;
        }

        /// <summary>This bot must not hit <paramref name="living"/> at all, not even with an area spell aimed at another
        /// monster (BotBrain refuses the cast or the blast): a dungeon rule of it says so (Funus while his puzzle is on and
        /// the bot isn't one he takes damage from).</summary>
        public static bool MustNotHit(GameBot bot, GameLiving living)
        {
            if (bot == null || living is not GameNPC npc) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
                if (rule.MustNotHit != null && rule.MustNotHit(bot, npc))
                    return true;
            return false;
        }

        /// <summary>This bot fights <paramref name="living"/> with its distance weapon only (a dungeon rule's
        /// <see cref="DungeonRule.FightsAtRange"/>; BotBrain's weapon choice and the native melee fallbacks ask it).</summary>
        public static bool FightsAtRange(GameBot bot, GameLiving living)
        {
            if (bot == null || living is not GameNPC npc) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
            {
                if (rule.FightsAtRange == null) continue;
                try
                {
                    if (rule.FightsAtRange(bot, npc)) return true;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon range rule {rule.Encounter} failed: {ex.Message}"); }
            }
            return false;
        }

        /// <summary>
        /// A bot's pet (companion bots and autonomous gamebots alike) must not hit <paramref name="living"/>: a dungeon rule of it
        /// says no pet may (<see cref="DungeonRule.NoPets"/>). A real player's own pet is the player's to command and is never
        /// held back here. Asked by ControlledMobBrain before a pet takes an attack order, picks a target or defends its owner.
        /// </summary>
        public static bool PetMustNotHit(GameNPC pet, GameLiving living)
        {
            if (pet == null || pet is GameBot || living is not GameNPC npc) return false;
            if ((pet.Brain as IControlledBrain)?.GetLivingOwner() is not GameBot) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
            {
                if (rule.NoPets == null) continue;
                try
                {
                    if (rule.NoPets(npc)) return true;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon pet rule {rule.Encounter} failed: {ex.Message}"); }
            }
            return false;
        }

        /// <summary>The rules' per-turn acts for this bot and target.</summary>
        private static void DungeonActs(GameBot bot, GameNPC target, IReadOnlyList<GameNPC> live)
        {
            foreach (DungeonRule rule in DungeonRulesFor(target))
            {
                if (rule.Act == null) continue;
                try { rule.Act(bot, target, live); }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon act {rule.Encounter} failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// A bot must not cast <paramref name="spell"/> on <paramref name="target"/>: a dungeon rule forbids its damage type there
        /// (Icelord Kvasir while cold heals him). Asked by every bot's offensive casts (BotBrain), companion bots included: a
        /// spell that heals the boss is never an order worth following.
        /// </summary>
        public static bool ForbidsSpell(GameLiving caster, GameLiving target, Spell spell)
        {
            if (caster is not GameBot || target is not GameNPC npc || spell == null || !spell.IsHarmful) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
                if (rule.Forbids is { Length: > 0 } && Array.IndexOf(rule.Forbids, (eDamageType)spell.DamageType) >= 0 &&
                    (rule.ForbidsWhile == null || rule.ForbidsWhile(npc)))
                    return true;
            return false;
        }

        // ---- shared pieces ------------------------------------------------------------------------------------------

        private static GameNPC FindNear(GameNPC near, IReadOnlyList<GameNPC> live, string type)
        {
            if (near == null) return null;
            if (near.GetType().Name == type && Present(near)) return near;
            return live?.Where(n => Present(n) && n.GetType().Name == type && n.CurrentRegionID == near.CurrentRegionID &&
                                    near.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => near.GetDistanceTo(n)).FirstOrDefault();
        }

        private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
        private static Vector3 PosOf(GameObject o) => new(o.X, o.Y, o.Z);

        /// <summary>A raider this far inside an allowed radius is close enough; one further out moves this far inside it.</summary>
        public const int CloseInSlack = 20, CloseInInset = 80;

        /// <summary>
        /// Where a raider at <paramref name="raider"/> stands to be within <paramref name="radius"/> of <paramref name="center"/>
        /// (a boss that takes damage only from close attackers: Fornfrusenen 200, Krackenschtein 300), on its own side; null
        /// when it already is.
        /// </summary>
        public static Vector3? CloseIn(Vector3 raider, Vector3 center, int radius)
        {
            float d = Vector2.Distance(Flat(raider), Flat(center));
            if (d <= radius - CloseInSlack) return null;
            Vector2 dir = d < 1 ? new Vector2(1, 0) : Vector2.Normalize(Flat(raider) - Flat(center));
            Vector2 at = Flat(center) + dir * Math.Max(1, radius - CloseInInset);
            return new Vector3(at.X, at.Y, center.Z);
        }

        /// <summary>A raider within <paramref name="radius"/> plus <see cref="SpreadMargin"/> of <paramref name="anchor"/> steps
        /// out to <paramref name="radius"/> plus <see cref="SpreadStepOut"/>, away from it; null when it is clear.</summary>
        public static Vector3? AwayFrom(Vector3 raider, Vector3 anchor, int radius)
        {
            float d = Vector2.Distance(Flat(raider), Flat(anchor));
            if (d > radius + SpreadMargin) return null;
            Vector2 dir = d < 1 ? new Vector2(1, 0) : Vector2.Normalize(Flat(raider) - Flat(anchor));
            Vector2 at = Flat(anchor) + dir * (radius + SpreadStepOut);
            return new Vector3(at.X, at.Y, raider.Z);
        }

        /// <summary>
        /// The raider's damage is magic (a list caster: its spells are what it hurts with), else physical (melee, archers,
        /// hybrids, healers who swing). Used where a boss resists one kind 90% (Council Vagn and Nokkvi).
        /// </summary>
        public static bool IsMagicDealer(int classId) => (eCharacterClass)classId is
            eCharacterClass.Wizard or eCharacterClass.Sorcerer or eCharacterClass.Theurgist or eCharacterClass.Cabalist or
            eCharacterClass.Necromancer or eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer or
            eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist or eCharacterClass.Animist;

        /// <summary>The encounter's switch is on (its own value, else the global one).</summary>
        private static bool On(string name, string encounter) => EpicEncounterMechanics.On(name, encounter);

        /// <summary>A raider's position.</summary>
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        /// <summary>The nearest living NPC of type <typeparamref name="T"/> (or a subclass) among <paramref name="live"/> in the
        /// region of <paramref name="near"/> within <paramref name="radius"/>, or null.</summary>
        private static T NearestLive<T>(GameObject near, IEnumerable<GameNPC> live, int radius) where T : GameNPC =>
            live?.OfType<T>().Where(n => Present(n) && n.CurrentRegionID == near.CurrentRegionID && near.IsWithinRadius(n, radius))
                .OrderBy(n => near.GetDistanceTo(n)).FirstOrDefault();

        /// <summary>The point <paramref name="distance"/> from <paramref name="from"/> on the raider's side of it (away from it),
        /// wherever the raider stands now (unlike <see cref="AwayFrom"/>, which says null when it is already clear).</summary>
        public static Vector3 PointAway(Vector3 raider, Vector3 from, float distance)
        {
            var d = new Vector2(raider.X - from.X, raider.Y - from.Y);
            Vector2 dir = d.Length() < 1 ? new Vector2(1, 0) : Vector2.Normalize(d);
            Vector2 p = new Vector2(from.X, from.Y) + dir * distance;
            return new Vector3(p.X, p.Y, raider.Z);
        }

        /// <summary>The distance between two points on the floor (X, Y).</summary>
        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        /// <summary>The raider is in the same party as <paramref name="anchor"/> (or is it).</summary>
        private static bool SameParty(GameLiving raider, GameLiving anchor) =>
            anchor != null && (anchor == raider || anchor.Group != null && anchor.Group == raider.Group);
    }
}
