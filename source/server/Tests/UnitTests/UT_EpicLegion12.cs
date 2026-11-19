using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;
using R = DOL.GS.EncounterDisplacementRecovery;

namespace DOL.UnitTests
{
    /// <summary>
    /// Legion's raid plan (1.2, EpicEncounterTactics.Legion.cs): the main tank and the taunts, his target's tank healers, his
    /// lair and the ranged line, the soul waves sized to the wave, the way down across Behemoth's hall (the recovery's way
    /// out) and Behemoth left alone. Pure rules and the recovery's state machine, plus a test region with Legion and bots;
    /// no world tick, combat, spell or pathing runs. Tests only: nothing here was played in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicLegion12
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 2_600;
        private EpicTestServerScope _server;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            // Made without a constructor (GetUninitializedObject): no field initializers run, so the defaults live here.
            private ICharacterClass _class;
            public byte? Hp;
            public ICharacterClass Class { get => _class ??= new ClassHero(); set => _class = value; }
            public override bool IsAlive => true;
            public override byte HealthPercent => Hp ?? 100;
            public override eRealm Realm { get => eRealm.Hibernia; set { } }
            public override ICharacterClass CharacterClass => Class;
            public override void StopMoving() { }
            public override bool IsCasting => false;
        }

        public sealed class TestLegion : DOL.GS.Scripts.Legion
        {
            public bool Idle;
            public override bool IsAlive => true;
            public override bool InCombat => !Idle;
        }

        public sealed class TestBehemoth : Behemoth { public override bool IsAlive => true; }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 249, Name = "Legion test", Description = "Legion test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, 2490, "Legion test", 0, 0, 65536, 65536, 2490, false, 0, false, 0, 0, 0, 0, 0));
            T.ResetLegionForTests();
            R.ForgetAll();
        }

        [TearDown]
        public void TearDown()
        {
            T.ResetLegionForTests();
            R.ForgetAll();
            _server.Dispose();
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            var caches = typeof(GameObject).GetField("_objectsInRadiusCaches", Hidden)!;
            caches.SetValue(npc, Activator.CreateInstance(caches.FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(npc, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(npc, new PropertyCollection());
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
            npc.effectListComponent = EffectListComponent.Create(npc);
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            npc.SubZoneObject = new SubZoneObject(npc);
            Zone zone = _region.Zones[0];
            Assert.That(zone.AddObject(npc), Is.True);
            SubZoneObject sub = npc.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
            return npc;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the scripts are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray()));
        }

        /// <summary>The nearest a walk along the legs comes to <paramref name="p"/> (flat).</summary>
        private static float ClosestApproach(Vector3 from, IEnumerable<Vector3> legs, Vector3 p)
        {
            float best = float.MaxValue;
            Vector2 c = new(p.X, p.Y), a = new(from.X, from.Y);
            foreach (Vector3 leg in legs)
            {
                Vector2 b = new(leg.X, leg.Y), ab = b - a;
                float t = ab.LengthSquared() < 1 ? 0 : Math.Clamp(Vector2.Dot(c - a, ab) / ab.LengthSquared(), 0, 1);
                best = Math.Min(best, Vector2.Distance(c, a + ab * t));
                a = b;
            }
            return best;
        }

        // ---- the data --------------------------------------------------------------------------------------------------

        [Test]
        public void TheLayoutIsTheScriptsAndTheWorlds()
        {
            string legion = Source("scripts", "namedmobs", "DarknessFalls", "Legion.cs");
            Assert.That(legion, Does.Contain("EncounterDisplacement.Move(player, 249, 48200, 49566, 20833"), "the throw's landing");
            Assert.That(T.LegionThrowLanding, Is.EqualTo(new Vector3(48200, 49566, 20833)));
            Assert.That(legion, Does.Contain("new Area.Circle(\"Legion's Lair\", 45000, 51700, 15468, radius)"));
            Assert.That(T.LegionLairCenter, Is.EqualTo(new Vector3(45000, 51700, 15468)));
            Assert.That(legion, Does.Contain("sBrain.AggroRange = 500;"), "Behemoth's aggro range, which the way down keeps clear of");
            Assert.That(legion, Does.Contain("int distance = 1400;"), "his close range, which the ranged line stands inside");
            Assert.That(T.AttackRules.Single(r => r.Boss == "Legion").Radius, Is.EqualTo(1_400));
            Assert.That(T.LegionRangedMax, Is.LessThanOrEqualTo(1_400 - T.AttackMargin), "the line never fights the close-range step-in");
            Assert.That(T.For("Legion").RangedAdds, Is.EqualTo(new[] { "LegionAdd" }), "casters and archers on the souls (catalog)");
            // His room first (period: clear it, then pull him before a respawn): the Chamberlain 792 from his spawn, Famuel and
            // Earl Amagin in the corridor about 2,150-2,200 off (world database). Behemoth, 2,372 off in his hall, is no part of it.
            T.Tactic row = T.For("Legion");
            Assert.That(row.ClearFirst, Is.EqualTo(new[] { "Chamberlain", "Inquisitor Famuel", "Earl Amagin" }));
            Assert.That(T.IsClearFirst(row, "Chamberlain", 792), Is.True);
            Assert.That(T.IsClearFirst(row, "Inquisitor Famuel", 2_174), Is.True);
            Assert.That(T.IsClearFirst(row, "Earl Amagin", 2_150), Is.True);
            Assert.That(T.IsClearFirst(row, "Behemoth", 2_372), Is.False);
            Assert.That(T.IsClearFirst(row, "Chamberlain", 4_000), Is.False, "only in his room");
        }

        // ---- his lair and the ranged line ---------------------------------------------------------------------------------

        [Test]
        public void HisTankWalksHimBackOntoHisLairWhenHeIsDrawnOff()
        {
            Vector3 spawn = new(45027, 51899, 15468);
            // Hibernia's first try (PC log e3b8238): he fought 1,835 off his spawn, in its muster.
            Vector3 drawn = new(45190, 50078, 15311);
            var hold = T.LegionHoldAdvice(new Vector3(45190, 50000, 15311), drawn, spawn, isTank: true);
            Assert.That(hold?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(hold.Value.StandAt, Is.EqualTo(spawn), "to his spawn: he follows it, keeping it as his target");
            Assert.That(T.LegionHoldAdvice(new Vector3(45190, 50000, 15311), drawn, spawn, isTank: false), Is.Null,
                "anyone else he fights stands: running would drag him through the raid");
            // Midgard's kill: 1,126 off is close enough.
            Assert.That(T.LegionHoldAdvice(new Vector3(45268, 50700, 15307), new Vector3(45268, 50810, 15307), spawn, true), Is.Null);
            Assert.That(T.LegionHoldAdvice(new Vector3(45030, 51890, 15468), drawn, spawn, true), Is.Null, "already there");
        }

        [Test]
        public void TheRangedLineStandsInsideHisReachOnTheRaidsSideClearOfHisTank()
        {
            Vector3 legion = new(45027, 51750, 15468);   // he follows his tank to his spawn
            Vector3 tank = new(45027, 51899, 15468);
            var spots = Enumerable.Range(0, T.LegionRangedSlots).Select(id => T.LegionRangedSpot(legion, T.LegionRaidSide, id, 15311)).ToArray();
            Assert.That(spots.Distinct().Count(), Is.EqualTo(T.LegionRangedSlots), "spread across the corridor");
            foreach (Vector3 s in spots)
            {
                Assert.That(Flat(s, legion), Is.EqualTo(T.LegionRangedDistance).Within(1), "his close range counts it");
                Assert.That(s.Y, Is.LessThan(legion.Y - 1_000), "south of him, toward the raid's muster");
                Assert.That(Flat(s, tank), Is.GreaterThan(1_000), "out of the soul blast (1,000 around his target)");
                Assert.That(Math.Abs(s.X - 45000), Is.LessThan(500), "within the corridor raiders stood in (PC log)");
            }
            // In the band: fights where it stands. Too close or too far: walks to its spot.
            Assert.That(T.LegionRangedAdvice(new Vector3(45027, 50600, 15311), legion, T.LegionRaidSide, 7), Is.Null);
            var close = T.LegionRangedAdvice(new Vector3(45027, 51300, 15311), legion, T.LegionRaidSide, 7);
            Assert.That(close?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(close.Value.StandAt.Value, legion), Is.InRange(T.LegionRangedMin, T.LegionRangedMax));
            var far = T.LegionRangedAdvice(new Vector3(45027, 49800, 15311), legion, T.LegionRaidSide, 7);
            Assert.That(far?.StandAt, Is.Not.Null);
            Assert.That(T.LegionRangedAdvice(close.Value.StandAt.Value, legion, T.LegionRaidSide, 7), Is.Null, "there: it fights");
            // Near the muster himself: the line still goes south (the lair-to-muster direction).
            Vector3 atMuster = T.LegionRangedSpot(T.LegionRaidSide, T.LegionRaidSide, 2, 15311);
            Assert.That(atMuster.Y, Is.LessThan(T.LegionRaidSide.Y - 1_000));
        }

        [Test]
        public void TheRulePlacesHisTankAndTheHealersAndLeavesMeleeAlone()
        {
            var legion = Npc<TestLegion>(45190, 50078, 15311);
            legion.SpawnPoint = new Point3D(45027, 51899, 15468);
            var tank = Npc<Bot>(45190, 50000, 15311);
            var druid = Npc<Bot>(45100, 50300, 15311);
            druid.Class = new ClassDruid();
            var melee = Npc<Bot>(45150, 50100, 15311);
            melee.Class = new ClassBlademaster();
            var live = new List<GameNPC> { legion, tank, druid, melee };
            legion.TargetObject = tank;
            T.Advice? Ask(Bot b, bool atRange) => T.DungeonAdvice(new T.RuleContext(b, legion, live, 0, atRange, 0, b.Class.ID));
            Assert.That(Ask(tank, false)?.Reason, Does.StartWith("legion-hold"));
            Assert.That(Ask(tank, false)?.Target, Is.SameAs(legion));
            var line = Ask(druid, false);
            Assert.That(line?.Reason, Does.StartWith("legion-ranged"), "healers stand in the line (they count as at range)");
            Assert.That(Flat(line.Value.StandAt.Value, At(legion)), Is.InRange(T.LegionRangedMin, T.LegionRangedMax));
            Assert.That(Ask(melee, false), Is.Null, "melee fight him as usual");
            druid.Class = new ClassPaladin();
            Assert.That(Ask(druid, false), Is.Null, "a paladin heals but tanks: it stays in melee");
            legion.Idle = true;
            Assert.That(Ask(tank, false), Is.Null, "out of combat the plan has no opinion");
        }

        // ---- main tank ----------------------------------------------------------------------------------------------------

        [Test]
        public void TanksTauntHimBackOntoATankAndSwapWhenTheirTankIsLow()
        {
            var healer = Npc<Bot>(0, 0, 0);
            var near = Npc<Bot>(10, 0, 0);
            var far = Npc<Bot>(20, 0, 0);
            var low = Npc<Bot>(5, 0, 0);
            (GameLiving, float, int)[] tanks = [(far, 400, 100), (near, 150, 90), (low, 100, 40)];
            Assert.That(T.LegionTaunter(healer, false, 100, tanks), Is.SameAs(near), "he hits a healer: the nearest healthy tank");
            Assert.That(T.LegionTaunter(near, true, 70, tanks), Is.Null, "his tank keeps him while it stands");
            Assert.That(T.LegionTaunter(near, true, 30, tanks), Is.SameAs(far), "a low tank hands him to a fresh one (not itself)");
            Assert.That(T.LegionTaunter(near, true, 30, [(low, 100, 70)]), Is.Null, "no fresh tank: it holds on");
            Assert.That(T.LegionTaunter(null, false, 100, tanks), Is.Null);
            Assert.That(T.LegionTaunter(healer, false, 100, [(low, 100, 40)]), Is.Null, "a tank under 60% would only feed him");
            // The rule's act asks the tanks (autonomous gamebots, by BotBrain's tactic turn).
            Assert.That(T.AllDungeonRules.Single(r => r.Encounter == "Legion" && r.Act != null).Targets, Is.EqualTo(new[] { "Legion" }));
        }

        // ---- tank healers ----------------------------------------------------------------------------------------------

        [Test]
        public void TheNearestSixHealersKeepHisTargetUpWhateverItsParty()
        {
            var bots = Enumerable.Range(0, 9).Select(i => Npc<Bot>(i * 10, 0, 0)).ToArray();
            var team = T.PickTankHealers(bots.Select((b, i) => ((GameLiving)b, (float)(900 - i * 100))), T.LegionTankHealerCount);
            Assert.That(team.Count, Is.EqualTo(6));
            Assert.That(team, Is.EquivalentTo(bots.Skip(3)), "the nearest six to his target");
            Assert.That(T.PickTankHealers([], 6), Is.Empty);
            // They heal it first under 85% (SignalPatient's mark), unless their own pick is a lower emergency.
            Assert.That(T.PreferMarked(50, T.LegionTankHealBelow, 70), Is.True);
            Assert.That(T.PreferMarked(90, T.LegionTankHealBelow, 70), Is.False);
            Assert.That(T.PreferMarked(50, T.LegionTankHealBelow, 30), Is.False, "a lower emergency first");
            string signals = Source("bots", "autonomous", "EpicEncounterTactics.Signals.cs");
            Assert.That(signals, Does.Contain("Consider(LegionTankPatient(healer, now), LegionTankHealBelow);"));
        }

        [Test]
        public void AHealerOfTheTeamGetsHisTargetAsItsPatient()
        {
            var legion = Npc<TestLegion>(20_000, 20_000, 1_000);
            var tank = Npc<Bot>(20_100, 20_000, 1_000);
            var healers = Enumerable.Range(0, 8).Select(i => Npc<Bot>(20_300 + i * 150, 20_000, 1_000)).ToArray();
            foreach (Bot h in healers) h.Class = new ClassDruid();
            legion.TargetObject = tank;
            Assert.That(T.LegionTankPatient(healers[0], 0), Is.SameAs(tank), "the nearest healer is in the team");
            Assert.That(T.LegionTankPatient(healers[7], 0), Is.Null, "the eighth heals its party as usual");
            legion.Idle = true;
            T.ResetLegionForTests();
            Assert.That(T.LegionTankPatient(healers[0], 0), Is.Null, "not while he is out of combat");
        }

        // ---- soul waves ---------------------------------------------------------------------------------------------------

        [Test]
        public void SoulWavesTakeMeleePartiesSizedToTheWaveNeverTheTanks()
        {
            Assert.That(T.LegionSoulPartyCount(3, 12), Is.Zero, "a small wave is the casters' and archers'");
            Assert.That(T.LegionSoulPartyCount(4, 12), Is.Zero);
            Assert.That(T.LegionSoulPartyCount(5, 12), Is.EqualTo(1));
            Assert.That(T.LegionSoulPartyCount(16, 12), Is.EqualTo(3), "a full wave (64 counted, 16 souls)");
            Assert.That(T.LegionSoulPartyCount(48, 12), Is.EqualTo(4), "three waves at once (Hibernia 13:27): a third of the raid");
            Assert.That(T.LegionSoulPartyCount(16, 1), Is.Zero, "one party stays on him");
            bool[] healer = [true, true, false, true, true];
            Assert.That(T.LegionSoulPartyPicks(healer, 4, 2), Is.EqualTo(new[] { 3, 1 }), "from the end, with a healer, not the tank's");
            Assert.That(T.LegionSoulPartyPicks(healer, -1, 5), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(T.LegionSoulPartyPicks(healer, 0, 9), Does.Not.Contain(0));
        }

        // ---- the way down ------------------------------------------------------------------------------------------------

        [Test]
        public void TheWayDownCrossesBehemothsHallOutOfHisAggroToThePortal()
        {
            R.ExitRoute route = R.ExitRouteFor("Legion");
            Assert.That(route, Is.SameAs(T.LegionWayDown));
            Assert.That(route.Legs.Last(), Is.EqualTo(T.PurgatoryPortal), "the last leg is the zone point");
            Assert.That(route.PortalTarget, Is.EqualTo(new Vector3(45050, 48892, 15460)), "DF LEGION PURGATORY's target");
            Assert.That(route.Avoid, Is.EqualTo(new[] { "Behemoth" }));
            float pass = ClosestApproach(T.LegionThrowLanding, route.Legs, T.BehemothSpawn);
            Assert.That(pass, Is.GreaterThan(500 + R.ExitAvoidMargin), "planned clear of Behemoth's 500 aggro and the margin");
            Assert.That(route.MaxMilliseconds, Is.InRange(50_000, 80_000), "bounded: about a minute");
            Assert.That(Flat(route.PortalTarget, T.LegionLairCenter), Is.LessThan(3_000), "the portal puts it back by his lair");
            Assert.That(R.AboutToBeNoticed(new(0, 0, 0), new(510, 0, 0), 500, false), Is.True, "inside the margin");
            Assert.That(R.AboutToBeNoticed(new(0, 0, 0), new(560, 0, 0), 500, false), Is.False);
            Assert.That(R.AboutToBeNoticed(new(0, 0, 0), new(5_000, 0, 0), 500, true), Is.True, "he fights it");
        }

        private static R.Plan Thrown()
        {
            var plan = R.Plan.For(DisplacementKind.Teleport, 0, T.LegionThrowLanding, 0, 0, true);
            plan.Exit = T.LegionWayDown;
            return plan;
        }

        private static readonly Vector3 Party = new(45000, 50800, 15311);

        private static R.Observation Seen(long now, Vector3 at, bool pathHome = false, bool danger = false, bool held = false) =>
            new(now, true, at, Party, false, false, false, (from, to) => pathHome || Flat(to, Party) > 100, held, null, danger);

        [Test]
        public void AThrownBotWalksTheLegsThroughThePortalAndBackToItsParty()
        {
            R.Plan plan = Thrown();
            Assert.That(R.Step(plan, Seen(0, T.LegionThrowLanding)).Kind, Is.EqualTo(R.OrderKind.Land));
            long now = 0;
            Vector3 at = T.LegionThrowLanding;
            for (int i = 0; i < T.LegionWayDownLegs.Length; i++)
            {
                R.Order walk = R.Step(plan, Seen(now += 500, at));
                Assert.That(walk.Kind, Is.EqualTo(R.OrderKind.Walk), $"leg {i}");
                Assert.That(walk.Point, Is.EqualTo(T.LegionWayDownLegs[i]));
                at = T.LegionWayDownLegs[i];
                now += 8_000;
            }
            R.Order portal = R.Step(plan, Seen(now += 500, at));
            Assert.That(portal.Kind, Is.EqualTo(R.OrderKind.Portal));
            Assert.That(portal.Point, Is.EqualTo(T.PurgatoryPortalTarget));
            Assert.That(now, Is.LessThan(T.LegionWayDown.MaxMilliseconds), "the legs fit the time bound");
            // Put by his lair: the usual walk to its party (there is a path down there).
            R.Order back = R.Step(plan, Seen(now += 500, T.PurgatoryPortalTarget, pathHome: true));
            Assert.That(back.Kind, Is.EqualTo(R.OrderKind.Walk));
            Assert.That(back.Point, Is.EqualTo(Party));
            Assert.That(R.Step(plan, Seen(now += 500, Party, pathHome: true)).Kind, Is.EqualTo(R.OrderKind.Done));
        }

        [Test]
        public void TheWayDownFallsBackToTheRecallWhenItCannotBeWalked()
        {
            // No path to the first leg: the usual 5 s recall.
            R.Plan noPath = Thrown();
            R.Step(noPath, Seen(0, T.LegionThrowLanding));
            var blocked = Seen(100, T.LegionThrowLanding) with { Corridor = (_, _) => false };
            Assert.That(R.Step(noPath, blocked).Kind, Is.EqualTo(R.OrderKind.Hold));
            Assert.That(noPath.Reason, Is.EqualTo("no path to the way out"));
            Assert.That(R.Step(noPath, blocked with { Now = 100 + R.RecallMilliseconds }).Kind, Is.EqualTo(R.OrderKind.Recall));

            // Behemoth about to notice it: recalled at once.
            R.Plan danger = Thrown();
            R.Step(danger, Seen(0, T.LegionThrowLanding));
            R.Step(danger, Seen(100, T.LegionThrowLanding));
            R.Order now = R.Step(danger, Seen(600, new Vector3(45000, 49100, 20833), danger: true));
            Assert.That(now.Kind, Is.EqualTo(R.OrderKind.Recall), "no 5 s standing still beside him");
            Assert.That(now.Point, Is.EqualTo(Party));

            // No progress: recalled.
            R.Plan stuck = Thrown();
            R.Step(stuck, Seen(0, T.LegionThrowLanding));
            R.Step(stuck, Seen(100, T.LegionThrowLanding));
            Assert.That(R.Step(stuck, Seen(100 + R.ProgressWindowMilliseconds, T.LegionThrowLanding)).Kind, Is.EqualTo(R.OrderKind.Hold));
            Assert.That(stuck.Reason, Is.EqualTo("no progress on the way out"));

            // Held (rooted) past the bound: the bound is not stretched.
            R.Plan rooted = Thrown();
            R.Step(rooted, Seen(0, T.LegionThrowLanding));
            R.Step(rooted, Seen(100, T.LegionThrowLanding));
            Assert.That(R.Step(rooted, Seen(5_000, T.LegionThrowLanding, held: true)).Kind, Is.EqualTo(R.OrderKind.Hold));
            R.Step(rooted, Seen(T.LegionWayDown.MaxMilliseconds + 200, T.LegionThrowLanding, held: true));
            Assert.That(rooted.Reason, Is.EqualTo("the way out took too long"));

            // A path straight back after all (a navmesh with a link): the usual walk, no way out.
            R.Plan direct = Thrown();
            R.Step(direct, Seen(0, T.LegionThrowLanding));
            R.Order walk = R.Step(direct, Seen(100, T.LegionThrowLanding, pathHome: true));
            Assert.That((walk.Kind, walk.Point), Is.EqualTo((R.OrderKind.Walk, Party)));
            Assert.That(direct.Stage, Is.EqualTo(R.Stage.Walking));
        }

        [Test]
        public void OnlyTeleportsAndThrowsOfAnEncounterWithAWayOutUseIt()
        {
            Assert.That(R.ExitRouteFor("Xanxicar"), Is.Null);
            Assert.That(R.ExitRouteFor(null), Is.Null);
            var plan = R.Plan.For(DisplacementKind.Teleport, 0, T.LegionThrowLanding, 0, 0, true);
            Assert.That(plan.Exit, Is.Null, "a plan without one walks back as before");
            R.Step(plan, Seen(0, T.LegionThrowLanding));
            var noPath = Seen(100, T.LegionThrowLanding) with { Corridor = (_, _) => false };
            R.Step(plan, noPath);
            Assert.That(plan.Reason, Is.EqualTo("no path back"), "the old recall");
            string recovery = Source("bots", "EncounterDisplacementRecovery.cs");
            Assert.That(recovery, Does.Contain("if (e.Kind is DisplacementKind.Teleport or DisplacementKind.Throw) plan.Exit = ExitRouteFor(e.Encounter);"));
        }

        // ---- Behemoth ---------------------------------------------------------------------------------------------------

        [Test]
        public void NoBotFightsBehemoth()
        {
            var behemoth = Npc<TestBehemoth>(44490, 49588, 20833);
            var bot = Npc<Bot>(44700, 49588, 20833);
            T.DungeonRule rule = T.AllDungeonRules.Single(r => r.Encounter == "Behemoth");
            Assert.That(rule.MustNotHit(bot, behemoth), Is.True, "not even an area spell");
            Assert.That(rule.NoPets(behemoth), Is.True);
            T.Advice a = T.BehemothStandOffAdvice(At(bot), behemoth);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(Flat(a.StandAt.Value, At(behemoth)), Is.EqualTo(T.BehemothStandOff).Within(1));
            Assert.That(T.BehemothStandOffAdvice(new Vector3(46000, 49588, 20833), behemoth).StandAt, Is.Null, "already clear");
        }
    }
}
