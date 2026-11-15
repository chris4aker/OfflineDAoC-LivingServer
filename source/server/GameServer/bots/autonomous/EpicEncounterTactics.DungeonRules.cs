using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Styles;

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
            /// <see cref="ForbidsWhile"/> says so (null: always). Asked for a bot's spells (sub spells too) and the area
            /// spells that would reach the target (<see cref="ForbidsSpell"/>), its styles' procs (<see cref="ForbidsStyle"/>),
            /// its melee weapons (<see cref="MeleeForbidden"/>) and its armor's reactive procs (<see cref="ArmorProcForbidden"/>).</summary>
            public eDamageType[] Forbids { get; init; } = [];
            public Func<GameNPC, bool> ForbidsWhile { get; init; }
            /// <summary>Done for the bot on each of its tactic turns before the decision (face the boss, start a team's
            /// errand). Autonomous gamebots only, like the decision.</summary>
            public Action<GameBot, GameNPC, IReadOnlyList<GameNPC>> Act { get; init; }
            /// <summary>Done for the bot on each of its tactic turns before the decision, as <see cref="Act"/>; true when the bot
            /// spent the turn on it, and does nothing else this turn (Summoner Lossren's kept soul: the raid's one
            /// crowd-control bot keeps it mesmerized). Autonomous gamebots only.</summary>
            public Func<GameBot, GameNPC, IReadOnlyList<GameNPC>, bool> Control { get; init; }
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
            /// <summary>While true the raid leaves this monster alive (Summoner Lossren's kept soul: her next wave of tortured
            /// souls comes only once none of hers lives). No bot hits it, area spells included (as <see cref="MustNotHit"/>),
            /// no bot's pet takes it (as <see cref="NoPets"/>), the catalog's ranged-add and clump lists skip it, and a bot
            /// told to leave it drops it from its aggro list. Asked for the rule's targets; null: never (<see cref="LeftAlive"/>).</summary>
            public Func<GameNPC, bool> LeaveAlive { get; init; }
            /// <summary>The <see cref="Route"/> also redirects a monster that is in combat (the horsemen's barred room). By
            /// default a route rule leaves a fighting monster alone: a monster fighting the raid is answered, never mapped onto
            /// the raid's current target (PC log e3b8238: "Crypt Lord -&gt; Crypt Lord" 22 times and "Easmarach -&gt;
            /// Easmarach" 208 times, while a dark light and the Earth primal it was mapped from killed the raid).</summary>
            public bool RoutesInCombat { get; init; }
            /// <summary>The living boss whose fight this monster would join, which waits until it is dead (the Crypt Lord's
            /// court, the Host's and Icelord Agmundr's linked monsters, Lich Lord Ilron's dark lights and eyes): the route kills
            /// it first, like a catalog <see cref="Tactic.ClearFirst"/> name. Asked for every monster whatever
            /// <see cref="Targets"/> says; null: not one.</summary>
            public Func<GameNPC, IReadOnlyList<GameNPC>, GameNPC> ClearFirstOf { get; init; }
            /// <summary>Why the raid must not pull this monster now (null: it may): the Crypt Lord's patrol would pass its
            /// fight, the Host is not at his ambush yet. Asked for every monster whatever <see cref="Targets"/> says; the route
            /// asks before it picks a target and before a muster goes in (<see cref="PullWait"/>).</summary>
            public Func<GameNPC, IReadOnlyList<GameNPC>, long, string> PullWait { get; init; }
            /// <summary>A fixed point the raid musters for this encounter at instead of where it stands (the Host's ambush on
            /// his patrol: the muster chased him every 10 s); null: none.</summary>
            public Func<GameNPC, Vector3?> MusterAnchor { get; init; }
            /// <summary>Spots no muster of the raid goes near while this NPC is out of combat, each with its radius (the Crypt
            /// Lord's patrol loop, where a fight near him pulls him and his court; his court monsters' aggro). Asked for every
            /// living NPC of the dungeon, the encounter mustered for included; null or empty: none
            /// (<see cref="MusterDangersOf"/>, RealmRaidDungeonRoute.GuardMuster).</summary>
            public Func<GameNPC, IReadOnlyList<GameNPC>, IEnumerable<(Vector3 At, float Radius)>> MusterDangers { get; init; }
            /// <summary>This boss is never pulled while a monster that would join its fight (<see cref="ClearFirstOf"/>) lives:
            /// when the raid musters for it again (after a skirmish or a wipe) with one alive, the route clears it first, as it
            /// does in Darkness Falls. Asked for the raid's target.</summary>
            public bool ClearsAgainBeforePull { get; init; }
        }

        private sealed record RuleSet(DungeonRule[] All, Dictionary<string, DungeonRule[]> ByTarget, DungeonRule[] Routes)
        {
            public DungeonRule[] ClearFirsts { get; } = All.Where(r => r.ClearFirstOf != null).ToArray();
            public DungeonRule[] PullWaits { get; } = All.Where(r => r.PullWait != null).ToArray();
            public DungeonRule[] Anchors { get; } = All.Where(r => r.MusterAnchor != null).ToArray();
            public DungeonRule[] Dangers { get; } = All.Where(r => r.MusterDangers != null).ToArray();
        }

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
                // A monster fighting the raid is fought (RoutesInCombat: unless the rule says otherwise).
                if (chosen.InCombat && !rule.RoutesInCombat) continue;
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

        /// <summary>The living boss a dungeon rule says <paramref name="npc"/> is killed before (null: none).</summary>
        public static GameNPC RuleClearFirstOwner(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc == null || live == null) return null;
            foreach (DungeonRule rule in DungeonRuleSet.ClearFirsts)
            {
                try
                {
                    if (rule.ClearFirstOf(npc, live) is { } boss && boss != npc && Present(boss)) return boss;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon clear-first {rule.Encounter} failed: {ex.Message}"); }
            }
            return null;
        }

        /// <summary>
        /// Why the raid must not pull <paramref name="target"/> now, or null when it may (a dungeon rule's
        /// <see cref="DungeonRule.PullWait"/>; the first that objects). The raid route asks before it takes a target out of
        /// combat and before a mustered raid goes in, and waits (holding at the muster) while one objects. A monster in combat
        /// is never held: the fight is on.
        /// </summary>
        public static string PullWait(GameNPC target, IReadOnlyList<GameNPC> live, long now)
        {
            if (target == null || target.InCombat || live == null) return null;
            foreach (DungeonRule rule in DungeonRuleSet.PullWaits)
            {
                try
                {
                    if (rule.PullWait(target, live, now) is string why) return why;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon pull-wait {rule.Encounter} failed: {ex.Message}"); }
            }
            return null;
        }

        /// <summary>The fixed point the raid musters for <paramref name="npc"/> at (a dungeon rule's
        /// <see cref="DungeonRule.MusterAnchor"/>), or null: where it stands, as before.</summary>
        public static Vector3? MusterAnchor(GameNPC npc)
        {
            if (npc == null) return null;
            foreach (DungeonRule rule in DungeonRuleSet.Anchors)
            {
                try
                {
                    if (rule.MusterAnchor(npc) is Vector3 at) return at;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon muster anchor {rule.Encounter} failed: {ex.Message}"); }
            }
            return null;
        }

        /// <summary>The spots (and radii) no muster goes near because of <paramref name="npc"/> (a dungeon rule's
        /// <see cref="DungeonRule.MusterDangers"/>), while it is out of combat; empty: none.</summary>
        public static List<(Vector3 At, float Radius)> MusterDangersOf(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            var found = new List<(Vector3 At, float Radius)>();
            if (!Present(npc) || npc.InCombat) return found;
            foreach (DungeonRule rule in DungeonRuleSet.Dangers)
            {
                try
                {
                    if (rule.MusterDangers(npc, live) is { } spots) found.AddRange(spots);
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon muster danger {rule.Encounter} failed: {ex.Message}"); }
            }
            return found;
        }

        /// <summary>A dungeon rule of <paramref name="boss"/> says it is never pulled while a monster that would join its fight
        /// lives (<see cref="DungeonRule.ClearsAgainBeforePull"/>).</summary>
        public static bool ClearsAgainBeforePull(GameNPC boss) => DungeonRulesFor(boss).Any(r => r.ClearsAgainBeforePull);

        /// <summary>This bot must not hit <paramref name="living"/> at all, not even with an area spell aimed at another
        /// monster (BotBrain refuses the cast or the blast): a dungeon rule of it says so (Funus while his puzzle is on and
        /// the bot isn't one he takes damage from).</summary>
        public static bool MustNotHit(GameBot bot, GameLiving living)
        {
            if (bot == null || living is not GameNPC npc) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
                if (rule.MustNotHit != null && rule.MustNotHit(bot, npc))
                    return true;
            return LeftAlive(npc);
        }

        /// <summary>A dungeon rule of <paramref name="npc"/> says the raid leaves it alive now (<see cref="DungeonRule.LeaveAlive"/>).</summary>
        public static bool LeftAlive(GameNPC npc)
        {
            if (npc == null) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
            {
                if (rule.LeaveAlive == null) continue;
                try
                {
                    if (rule.LeaveAlive(npc)) return true;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon leave-alive rule {rule.Encounter} failed: {ex.Message}"); }
            }
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
            if (LeftAlive(npc)) return true;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
            {
                if (rule.NoPets == null) continue;
                try
                {
                    if (rule.NoPets(npc)) return true;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon pet rule {rule.Encounter} failed: {ex.Message}"); }
            }
            // 1.2, Kvasir (owner's PC, build 1dc66b6, 2026-11-15: "aahhhh thank you spirit champion for healing me !" five
            // times in 30 s): a pet that deals a damage type the target's rule forbids stays off it like a NoPets target.
            return HasForbids(npc) && PetDamageTypes(pet).Any(type => ForbidsDamage(npc, type));
        }

        /// <summary>
        /// Every damage type a pet can deal: its melee (a Bonedancer commander's weapon, any other pet's MeleeDamageType), its
        /// harmful spells and their sub spells, and the spells its offensive procs cast (the Spiritmaster's spirit swordsman,
        /// warrior and champion buff themselves with Pet-Cold-DD-Proc, a 70 cold DD on their hits; the Sluaghbinder's zombie
        /// magician casts Rotting Gloom Blast, cold). Kept per pet until its spell list changes.
        /// </summary>
        public static IReadOnlyCollection<eDamageType> PetDamageTypes(GameNPC pet)
        {
            if (pet == null) return [];
            List<Spell> spells = pet.Spells;
            if (PetTypes.TryGetValue(pet, out PetTypeCache known) && ReferenceEquals(known.Spells, spells) && known.Count == (spells?.Count ?? 0))
                return known.Types;
            var types = new HashSet<eDamageType>();
            if (pet is CommanderPet && MeleeDamageOf(pet.ActiveWeapon) is eDamageType weapon) types.Add(weapon);
            else types.Add(pet.MeleeDamageType);
            foreach (Spell spell in spells ?? [])
                AddSpellDamageTypes(spell, types);
            PetTypes.AddOrUpdate(pet, new PetTypeCache(spells, spells?.Count ?? 0, types));
            return types;
        }

        /// <summary>The damage types <paramref name="spell"/> lands (pure but for the sub spell lookup): a harmful spell's own and
        /// its sub spell's; an offensive or defensive proc's, the proc spell's (its Value names it).</summary>
        public static void AddSpellDamageTypes(Spell spell, ISet<eDamageType> types)
        {
            if (spell == null) return;
            if (spell.SpellType is eSpellType.OffensiveProc or eSpellType.DefensiveProc)
            {
                if ((int)spell.Value is int procId && procId > 0 && procId != spell.ID && ItemSpell(procId) is { } proc && proc.IsHarmful)
                    types.Add(proc.DamageType);
                return;
            }
            if (!spell.IsHarmful) return;
            types.Add(spell.DamageType);
            if (spell.SubSpellID > 0 && spell.SubSpellID != spell.ID && ItemSpell(spell.SubSpellID) is { } sub && sub.IsHarmful)
                types.Add(sub.DamageType);
        }

        private sealed record PetTypeCache(List<Spell> Spells, int Count, HashSet<eDamageType> Types);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameNPC, PetTypeCache> PetTypes = new();

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

        /// <summary>The <see cref="DungeonRule.Control"/>s of <paramref name="target"/>'s rules: true when one used the bot's turn.</summary>
        private static bool DungeonControls(GameBot bot, GameNPC target, IReadOnlyList<GameNPC> live)
        {
            foreach (DungeonRule rule in DungeonRulesFor(target))
            {
                if (rule.Control == null) continue;
                try
                {
                    if (rule.Control(bot, target, live)) return true;
                }
                catch (Exception ex) { RuleWarn($"EPIC_TACTIC dungeon control {rule.Encounter} failed: {ex.Message}"); }
            }
            return false;
        }

        /// <summary>
        /// A bot must not cast <paramref name="spell"/> on <paramref name="target"/>: a dungeon rule forbids its damage type there
        /// (Icelord Kvasir while cold heals him). Asked by every bot's offensive casts (BotBrain), companion bots included: a
        /// spell that heals the boss is never an order worth following.
        /// </summary>
        public static bool ForbidsSpell(GameLiving caster, GameLiving target, Spell spell)
        {
            if (caster is not GameBot || target is not GameNPC npc || spell == null || !spell.IsHarmful) return false;
            if (ForbidsDamage(npc, spell.DamageType)) return true;
            // A spell whose sub spell lands the forbidden type (1.2, Kvasir: the filter read only the outer spell).
            return spell.SubSpellID > 0 && spell.SubSpellID != spell.ID && ItemSpell(spell.SubSpellID) is { } sub && sub.IsHarmful &&
                   ForbidsDamage(npc, sub.DamageType);
        }

        /// <summary>A dungeon rule of <paramref name="npc"/> forbids <paramref name="type"/> on it now (Kvasir: cold while
        /// <see cref="EpicEncounterMechanics.AllowKvasirColdHeal"/> is on).</summary>
        public static bool ForbidsDamage(GameNPC npc, eDamageType type)
        {
            if (npc == null) return false;
            foreach (DungeonRule rule in DungeonRulesFor(npc))
                if (rule.Forbids is { Length: > 0 } && Array.IndexOf(rule.Forbids, type) >= 0 &&
                    (rule.ForbidsWhile == null || rule.ForbidsWhile(npc)))
                    return true;
            return false;
        }

        /// <summary>The spell an item or a spell names by id (a proc, a sub spell). Settable for tests.</summary>
        public static Func<int, Spell> ItemSpell = id => id > 0 ? SkillBase.GetSpellByID(id) : null;

        /// <summary>A harmful spell id whose damage type is forbidden on <paramref name="npc"/> (an item's proc or poison).</summary>
        private static bool ForbidsSpellId(GameNPC npc, int spellId) =>
            spellId > 0 && ItemSpell(spellId) is { } spell && spell.IsHarmful &&
            (ForbidsDamage(npc, spell.DamageType) ||
             spell.SubSpellID > 0 && spell.SubSpellID != spell.ID && ItemSpell(spell.SubSpellID) is { } sub && sub.IsHarmful &&
             ForbidsDamage(npc, sub.DamageType));

        /// <summary>
        /// A bot must not use <paramref name="style"/> on <paramref name="target"/>: one of the style's procs lands a damage type
        /// a dungeon rule forbids there (1.2, Kvasir: a berserker's Aurora Borealis procs a 95 cold DD at 100%, and cold
        /// heals him a fifth of his health). Asked by BotMeleeStylePolicy.Usable, so the bot picks another style.
        /// </summary>
        public static bool ForbidsStyle(GameLiving attacker, GameLiving target, Style style)
        {
            if (attacker is not GameBot bot || target is not GameNPC npc || style?.Procs is not { Count: > 0 } procs) return false;
            int classId = bot.CharacterClass?.ID ?? 0;
            foreach (StyleProcInfo proc in procs)
                if (proc?.Spell != null && (proc.ClassId == 0 || proc.ClassId == style.ClassID || proc.ClassId == classId) &&
                    proc.Spell.IsHarmful && (ForbidsDamage(npc, proc.Spell.DamageType) || ForbidsSpellId(npc, proc.Spell.SubSpellID)))
                    return true;
            return false;
        }

        /// <summary>
        /// The melee damage type of <paramref name="weapon"/> for a bot (AttackComponent.AttackDamageType: a shield hits
        /// crush, any other weapon its Type_Damage), or null for a bow or no weapon.
        /// </summary>
        public static eDamageType? MeleeDamageOf(DbInventoryItem weapon) => weapon == null ? null : (eObjectType)weapon.Object_Type switch
        {
            eObjectType.Shield => eDamageType.Crush,
            eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Fired or eObjectType.Crossbow => null,
            _ => GlobalConstants.IsWeapon(weapon.Object_Type) ? (eDamageType)weapon.Type_Damage : null
        };

        /// <summary>
        /// A melee swing of <paramref name="weapon"/> lands a forbidden damage type on <paramref name="npc"/>: its own damage
        /// type (real loot: 125 Midgard weapon templates deal cold) or its poison. Weapon procs are not asked: they go off for
        /// a GamePlayer only (GameLiving.CheckWeaponMagicalEffect), never for a bot.
        /// </summary>
        public static bool WeaponForbidden(GameNPC npc, DbInventoryItem weapon) =>
            weapon != null && npc != null &&
            (MeleeDamageOf(weapon) is eDamageType type && ForbidsDamage(npc, type) || ForbidsSpellId(npc, weapon.PoisonSpellID));

        /// <summary>
        /// The bot's melee on <paramref name="target"/> would land a forbidden damage type (its active weapon, or in the standard
        /// slot its off-hand weapon, which swings too): NpcAttackAction swings no more, and the dungeon rule sends an autonomous
        /// gamebot to the adds. A distance weapon is not melee.
        /// </summary>
        public static bool MeleeForbidden(GameBot bot, GameLiving target)
        {
            if (bot == null || target is not GameNPC npc || bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || !HasForbids(npc)) return false;
            return MeleeForbidden(npc, bot.ActiveWeaponSlot, bot.ActiveWeapon, bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon));
        }

        /// <summary>The melee of a weapon set lands a forbidden damage type on <paramref name="npc"/> (pure but for the rule
        /// lookup): the active weapon, and in the standard slot an off-hand weapon (never a shield, which only blocks).</summary>
        public static bool MeleeForbidden(GameNPC npc, eActiveWeaponSlot slot, DbInventoryItem active, DbInventoryItem left) =>
            slot != eActiveWeaponSlot.Distance &&
            (WeaponForbidden(npc, active) ||
             slot == eActiveWeaponSlot.Standard && left != null && left.Object_Type != (int)eObjectType.Shield && WeaponForbidden(npc, left));

        /// <summary>
        /// One of the bot's worn armor pieces (or its shield) carries a reactive proc that lands a forbidden damage type on the
        /// monster that hits it (GameLiving.CheckArmorMagicalEffect: Tuscaran loot such as the Tuscarian Monitor and
        /// Dragon-shadow pieces cast a 95 cold DD at their attacker). Each of Kvasir's swings on such a bot could heal him a fifth.
        /// </summary>
        public static bool ArmorProcForbidden(GameBot bot, GameNPC npc)
        {
            if (bot?.Inventory == null || npc == null || !HasForbids(npc)) return false;
            return ArmorProcForbidden(npc, ReactiveSlots.Select(slot => bot.Inventory.GetItem(slot)));
        }

        /// <summary>One of <paramref name="worn"/> carries a reactive proc forbidden on <paramref name="npc"/> (pure but for the
        /// rule and spell lookups).</summary>
        public static bool ArmorProcForbidden(GameNPC npc, IEnumerable<DbInventoryItem> worn)
        {
            foreach (DbInventoryItem item in worn ?? [])
                if (item != null && (ForbidsSpellId(npc, item.ProcSpellID) || ForbidsSpellId(npc, item.ProcSpellID1)))
                    return true;
            return false;
        }

        /// <summary>Where a struck bot's reactive proc can come from: its armor (ArmorHitLocation) and its shield (a block).</summary>
        private static readonly eInventorySlot[] ReactiveSlots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.TorsoArmor,
            eInventorySlot.ArmsArmor, eInventorySlot.LegsArmor, eInventorySlot.LeftHandWeapon
        ];

        /// <summary>Any dungeon rule of <paramref name="npc"/> forbids a damage type now (a cheap first check).</summary>
        private static bool HasForbids(GameNPC npc)
        {
            foreach (DungeonRule rule in DungeonRulesFor(npc))
                if (rule.Forbids is { Length: > 0 } && (rule.ForbidsWhile == null || rule.ForbidsWhile(npc)))
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
