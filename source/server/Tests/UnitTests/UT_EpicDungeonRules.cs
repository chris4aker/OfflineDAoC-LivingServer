using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// The one per-dungeon rule mechanism (EpicEncounterTactics.DungeonRules.cs) after the wave-2 merge: #103's
    /// [DungeonRules] lists and #105's [EncounterRule] methods became one. These tests pin what both used to promise: every
    /// dungeon's rules are found, null means no opinion and any advice is final, a subclass asks its boss's rules, a failing
    /// rule is skipped, the forbids, must-not-hit and route parts come from the rules, the break-off speaks before them, and
    /// the rules' calls and the reason tags' calls share one booking. No world tick, combat or pathing runs.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicDungeonRules
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;
        private readonly List<T.DungeonRule> _registered = new();

        public class Probe : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class ProbeChild : Probe { }
        public sealed class Bystander : GameNPC
        {
            public override bool IsAlive => true;
        }

        private static readonly int Warrior = (int)eCharacterClass.Warrior;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 65, Name = "Dungeon rules test", Description = "Dungeon rules test", Mobs = [] });
            EncounterMechanicCalls.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (T.DungeonRule rule in _registered) T.UnregisterDungeonRule(rule);
            _registered.Clear();
            EncounterMechanicCalls.Reset();
            _server.Dispose();
        }

        private T.DungeonRule Register(T.DungeonRule rule)
        {
            T.RegisterDungeonRule(rule);
            _registered.Add(rule);
            return rule;
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)?.SetValue(npc, new PropertyCollection());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private static T.RuleContext Context(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live) =>
            new(raider, target, live, 0, false, 1_000, Warrior);

        // ---- one mechanism ---------------------------------------------------------------------------------------------

        [Test]
        public void EveryDungeonsRulesAreFoundInTheOneMechanism()
        {
            string[] encounters = T.AllDungeonRules.Select(r => r.Encounter).ToArray();
            Assert.That(encounters, Is.SupersetOf(new[]
            {
                // Galladoria (#104), Tuscaran Glacier, Summoner's Hall, Fomor and Marfach (#103), Albion (#105).
                "FirePrimal",
                "KingTuscar", "Kvasir", "Jailer", "Fornfrusenen", "Vagn", "SummonerLossren", "Krackenschtein", "Avartack",
                "Funus", "SkeletalSacristan", "SoulReckoner", "CryptLord", "LichLordSanguis", "Orshom", "DraargusMighty",
                "PrinceAsmoien",
            }));
            Assert.That(T.AllDungeonRules, Is.Unique, "each rule once");
            foreach (T.DungeonRule rule in T.AllDungeonRules)
            {
                Assert.That(rule.Targets, Is.Not.Null, rule.Encounter);
                Assert.That(rule.Targets.Length > 0 || rule.Route != null, Is.True, rule.Encounter + ": a rule with no target routes");
                Assert.That(rule.Decide != null || rule.Act != null || rule.Route != null || rule.Forbids.Length > 0 || rule.MustNotHit != null,
                    Is.True, rule.Encounter + ": does something");
                Assert.That(rule.Summary, Is.Not.Empty, rule.Encounter);
            }
            Type[] types = typeof(GameNPC).Assembly.GetTypes();
            Assert.That(types.Any(t => t.Name is "EncounterRuleAttribute" or "EncounterRouteRuleAttribute"), Is.False,
                "#105's [EncounterRule] mechanism is folded into the dungeon rules");
        }

        [Test]
        public void NullHasNoOpinionAndAnyAdviceIsFinalNoneIncluded()
        {
            var probe = Npc<Probe>(1000, 1000, 0);
            var raider = Npc<Bystander>(1100, 1000, 0);
            Register(new("ProbeSilent", [nameof(Probe)], _ => null) { Summary = "no opinion" });
            Register(new("ProbeFinal", [nameof(Probe)], c => new T.Advice(T.BotAction.None, c.Target, null, "probe-final")) { Summary = "final" });
            Register(new("ProbeNever", [nameof(Probe)], c => new T.Advice(T.BotAction.Retarget, c.Target, null, "probe-never")) { Summary = "never asked" });
            var live = new List<GameNPC> { probe };
            T.Advice? a = T.DungeonAdvice(Context(raider, probe, live));
            Assert.That(a, Is.Not.Null);
            Assert.That((a.Value.Action, a.Value.Reason), Is.EqualTo((T.BotAction.None, "probe-final")), "the first advice wins, None too");
            Assert.That(T.Decide(raider, probe, live, 0, false, 1_000, Warrior).Reason, Is.EqualTo("probe-final"));
            Assert.That(T.DungeonAdvice(Context(raider, Npc<Bystander>(0, 0, 0), live)), Is.Null, "no rule: no opinion");
        }

        [Test]
        public void TheBreakOffSpeaksBeforeTheDungeonRules()
        {
            var probe = Npc<Probe>(1000, 1000, 0);
            var raider = Npc<Bystander>(1100, 1000, 0);
            Register(new("ProbeFinal", [nameof(Probe)], c => new T.Advice(T.BotAction.None, c.Target, null, "probe-final")) { Summary = "final" });
            var live = new List<GameNPC> { probe };
            T.BreakOff(probe, new Vector3(5000, 5000, 0), 10_000);
            try
            {
                T.Advice a = T.Decide(raider, probe, live, 0, false, 1_000, Warrior);
                Assert.That(a.Action, Is.EqualTo(T.BotAction.StandOff));
                Assert.That(a.Reason, Does.StartWith("break-off"));
            }
            finally
            {
                T.EndBreakOff(probe);
            }
            Assert.That(T.Decide(raider, probe, live, 0, false, 1_000, Warrior).Reason, Is.EqualTo("probe-final"));
        }

        [Test]
        public void ARuleNamesItsClassAndEverySubclassOfIt()
        {
            var rule = Register(new("ProbeFinal", [nameof(Probe)], c => new T.Advice(T.BotAction.None, c.Target, null, "probe-final")) { Summary = "final" });
            var child = Npc<ProbeChild>(0, 0, 0);
            Assert.That(T.HasDungeonRule(child), Is.True, "a subclass of the target class asks its rules (#105's typeof matching)");
            Assert.That(T.DungeonRulesFor(child), Does.Contain(rule));
            Assert.That(T.HasDungeonRule(Npc<Bystander>(0, 0, 0)), Is.False);
            Assert.That(T.HasDungeonRule(null), Is.False);
        }

        [Test]
        public void AFailingRuleIsSkippedAndTheNextOneSpeaks()
        {
            var probe = Npc<Probe>(0, 0, 0);
            Register(new("ProbeThrows", [nameof(Probe)], _ => throw new InvalidOperationException("test")) { Summary = "throws" });
            Register(new("ProbeFinal", [nameof(Probe)], c => new T.Advice(T.BotAction.None, c.Target, null, "probe-final")) { Summary = "final" });
            Assert.That(T.DungeonAdvice(Context(Npc<Bystander>(0, 0, 0), probe, [probe]))?.Reason, Is.EqualTo("probe-final"));
        }

        [Test]
        public void ForbidsMustNotHitAndRoutesComeFromTheRules()
        {
            var probe = Npc<Probe>(0, 0, 0);
            var court = Npc<Bystander>(100, 0, 0);
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            Register(new("ProbeNoHit", [nameof(Probe)], null) { Summary = "no hit", MustNotHit = (_, npc) => npc is Probe });
            Register(new("ProbeCourt", [], null)
            {
                Summary = "court",
                Route = (GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason) =>
                {
                    reason = "probe court";
                    return chosen is Bystander ? live.OfType<Probe>().FirstOrDefault() : null;
                },
            });
            Assert.That(T.MustNotHit(bot, probe), Is.True);
            Assert.That(T.MustNotHit(bot, court), Is.False, "only its targets");
            Assert.That(T.MustNotHit(null, probe), Is.False, "a real player or nobody: never");
            Assert.That(T.DungeonRoute(court, [probe, court], out string why), Is.SameAs(probe));
            Assert.That(why, Is.EqualTo("probe court"));
            Assert.That(T.DungeonRoute(probe, [probe, court], out string none), Is.Null);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void PointAwayAlwaysGivesThePointWhileAwayFromIsNullWhenAlreadyClear()
        {
            // #105's rules stand at a point (PointAway); #103's step out only when inside (AwayFrom). Both kept, two names.
            Assert.That(T.PointAway(new(100, 0, 0), Vector3.Zero, 1_400), Is.EqualTo(new Vector3(1_400, 0, 0)));
            Assert.That(T.PointAway(new(0, 0, 7), Vector3.Zero, 500), Is.EqualTo(new Vector3(500, 0, 7)), "on the spot: any side");
            Assert.That(T.AwayFrom(new(2_000, 0, 0), Vector3.Zero, 1_400), Is.Null);
            Assert.That(T.AwayFrom(new(100, 0, 0), Vector3.Zero, 1_400), Is.Not.Null);
        }

        // ---- the party's calls ---------------------------------------------------------------------------------------

        [Test]
        public void TheRulesCallsAndTheReasonTagsShareOneBooking()
        {
            Assert.That(EncounterMechanicCalls.CallFor(new T.Advice(T.BotAction.Retarget, null, null, "twin heal", "epic_twin_heal")),
                Is.EqualTo(("epic_twin_heal", (long)T.MechanicCallGapMilliseconds)), "a rule's own call");
            Assert.That(EncounterMechanicCalls.CallFor(new T.Advice(T.BotAction.StandOff, null, null, "funus-puzzle: stand clear")),
                Is.EqualTo(("mech_funus", EncounterMechanicCalls.TopicGapMilliseconds)), "a reason's tag");
            Assert.That(EncounterMechanicCalls.CallFor(new T.Advice(T.BotAction.Retarget, null, null, "dies-with \"x\"")), Is.Null);
            Assert.That(EncounterMechanicCalls.Book(7, "epic_twin_heal", 0, T.MechanicCallGapMilliseconds), Is.True);
            Assert.That(EncounterMechanicCalls.Book(7, "mech_funus", 1_000), Is.False, "one party hears one call at a time (8 s)");
            Assert.That(EncounterMechanicCalls.Book(7, "epic_twin_heal", 46_000, T.MechanicCallGapMilliseconds), Is.True, "a rule's call: 45 s");
            Assert.That(EncounterMechanicCalls.Book(7, "mech_funus", 60_000), Is.True);
            Assert.That(EncounterMechanicCalls.Book(7, "mech_funus", 120_000), Is.False, "a tag's call: 120 s");
        }
    }
}
