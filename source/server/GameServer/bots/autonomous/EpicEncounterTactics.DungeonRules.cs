using System;
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
    /// around the boss's target, a jail the raid sends a team to open). Each dungeon keeps its rules in its own partial file
    /// (EpicEncounterTactics.&lt;Dungeon&gt;.cs) in a static method marked <see cref="DungeonRulesAttribute"/>; they are found by
    /// reflection, so dungeons never share a list (and never conflict when merged).
    /// <para>A rule runs in <see cref="Decide"/> before the catalog's generic rules: the first rule of the raider's target that
    /// gives an action wins. A rule can also forbid damage types on its target (<see cref="ForbidsSpell"/>, for every bot),
    /// act on the bot each turn (<see cref="DungeonRule.Act"/>), and name the party's call for its decision
    /// (<see cref="Advice.Call"/>, said in /group where the real player is).</para>
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

        /// <summary>One encounter's code rule. <paramref name="Targets"/>: the class names of the NPCs it applies to as the
        /// raider's target (the boss, its partner, its adds).</summary>
        public sealed record DungeonRule(string Encounter, string[] Targets, Func<RuleContext, Advice> Decide)
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
        }

        private static readonly object DungeonRuleLock = new();
        private static readonly List<DungeonRule> ExtraDungeonRules = new();
        private static Dictionary<string, DungeonRule[]> _dungeonRules;

        private static Dictionary<string, DungeonRule[]> DungeonRuleMap
        {
            get
            {
                lock (DungeonRuleLock)
                {
                    if (_dungeonRules != null) return _dungeonRules;
                    var all = new List<DungeonRule>();
                    foreach (MethodInfo m in typeof(EpicEncounterTactics).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        if (m.IsDefined(typeof(DungeonRulesAttribute), false) && m.GetParameters().Length == 0 &&
                            m.Invoke(null, null) is IEnumerable<DungeonRule> rules)
                            all.AddRange(rules.Where(r => r != null));
                    all.AddRange(ExtraDungeonRules);
                    return _dungeonRules = all.SelectMany(r => r.Targets.Select(t => (t, r)))
                        .GroupBy(p => p.t, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Select(p => p.r).ToArray(), StringComparer.Ordinal);
                }
            }
        }

        /// <summary>Every dungeon rule (each once).</summary>
        public static IReadOnlyList<DungeonRule> AllDungeonRules => DungeonRuleMap.Values.SelectMany(r => r).Distinct().ToArray();

        /// <summary>Adds a rule (tests).</summary>
        public static void RegisterDungeonRule(DungeonRule rule)
        {
            lock (DungeonRuleLock) { ExtraDungeonRules.Add(rule); _dungeonRules = null; }
        }

        public static void UnregisterDungeonRule(DungeonRule rule)
        {
            lock (DungeonRuleLock) { ExtraDungeonRules.Remove(rule); _dungeonRules = null; }
        }

        public static DungeonRule[] DungeonRulesFor(GameNPC npc) =>
            npc != null && DungeonRuleMap.TryGetValue(npc.GetType().Name, out DungeonRule[] rules) ? rules : [];

        public static bool HasDungeonRule(GameNPC npc) => DungeonRulesFor(npc).Length > 0;

        /// <summary>The first dungeon rule of the target that gives an action, or none.</summary>
        private static Advice DungeonAdvice(in RuleContext c)
        {
            foreach (DungeonRule rule in DungeonRulesFor(c.Target))
            {
                Advice a;
                try { a = rule.Decide(c); }
                catch (Exception ex)
                {
                    Log.Warn($"EPIC_TACTIC dungeon rule {rule.Encounter} failed: {ex.Message}");
                    continue;
                }
                if (a.Action != BotAction.None) return a;
            }
            return default;
        }

        /// <summary>The rules' per-turn acts for this bot and target.</summary>
        private static void DungeonActs(GameBot bot, GameNPC target, IReadOnlyList<GameNPC> live)
        {
            foreach (DungeonRule rule in DungeonRulesFor(target))
            {
                if (rule.Act == null) continue;
                try { rule.Act(bot, target, live); }
                catch (Exception ex) { Log.Warn($"EPIC_TACTIC dungeon act {rule.Encounter} failed: {ex.Message}"); }
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

        /// <summary>The raider is in the same party as <paramref name="anchor"/> (or is it).</summary>
        private static bool SameParty(GameLiving raider, GameLiving anchor) =>
            anchor != null && (anchor == raider || anchor.Group != null && anchor.Group == raider.Group);
    }
}
