using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.EncounterDisplacementRecovery;

namespace DOL.UnitTests
{
    /// <summary>1.2 epic "wow" pass: the displacement API, the per-encounter switches, the bots' recovery, the dragon throw.
    /// Automated only: nothing here was played in game.</summary>
    [TestFixture, NonParallelizable]
    public class UT_EncounterDisplacement
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => EmptyDatabase; }
        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            EpicEncounterMechanics.ResetPerEncounter();
            R.ForgetAll();
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterMechanics.ResetPerEncounter();
            foreach (string name in EpicEncounterMechanics.EncounterSwitches)
                typeof(EpicEncounterMechanics).GetProperty(name).SetValue(null, false);
            R.ForgetAll();
            PathfindingProvider.SetPathfindingMgr(null);
            GameServer.LoadTestDouble(_previous);
        }

        // ---- Switches --------------------------------------------------------------------------------------------------

        [Test]
        public void TheFiveEncounterSwitchesStayOffGloballyAndAreSettable()
        {
            string[] five = ["AllowDisplacement", "AllowPositionPuzzle", "AllowInvulnerableHazards", "AllowAddShield", "AllowGlacierGriffonPatrol"];
            foreach (string name in five)
            {
                PropertyInfo p = typeof(EpicEncounterMechanics).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(p, Is.Not.Null, name);
                Assert.That(p.CanWrite, Is.True, name + " is settable");
                Assert.That(p.GetValue(null), Is.False, name + " default is today's behaviour (off)");
                Assert.That(EpicEncounterMechanics.EncounterSwitches, Does.Contain(name));
            }
            Assert.That(EpicEncounterMechanics.EncounterSwitches, Does.Contain("AllowBotDisplacement"));
            Assert.That(EpicEncounterMechanics.EncounterSwitches, Does.Contain("AllowDragonThrow"));
            Assert.That(EpicEncounterMechanics.EncounterSwitches, Does.Contain("AllowDragonMessengers"));
            Assert.That(EpicEncounterMechanics.AllowBotDisplacement, Is.False);
            Assert.That(EpicEncounterMechanics.AllowDragonThrow, Is.False);
            Assert.That(EpicEncounterMechanics.AllowDragonMessengers, Is.False);
            // The 17 restore switches are unchanged.
            Assert.That(EpicEncounterMechanics.RestoreSwitches, Has.Length.EqualTo(18));
        }

        [Test]
        public void AnEncounterTakesItsOwnValueElseTheGlobalOne()
        {
            var olcasgean = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            Assert.That(EpicEncounterMechanics.AllowDisplacementFor(olcasgean), Is.False);
            EpicEncounterMechanics.SetFor("AllowDisplacement", "GameNPC", true);
            Assert.That(EpicEncounterMechanics.AllowDisplacementFor(olcasgean), Is.True, "per-encounter on");
            Assert.That(EpicEncounterMechanics.AllowDisplacement, Is.False, "the global value does not move");
            Assert.That(EpicEncounterMechanics.On("AllowDisplacement", "Conservator"), Is.False, "another encounter keeps the global");
            EpicEncounterMechanics.AllowDisplacement = true;
            Assert.That(EpicEncounterMechanics.On("AllowDisplacement", "Conservator"), Is.True, "global on");
            EpicEncounterMechanics.SetFor("AllowDisplacement", "Conservator", false);
            Assert.That(EpicEncounterMechanics.On("AllowDisplacement", "Conservator"), Is.False, "an encounter can be turned off alone");
            EpicEncounterMechanics.SetFor("AllowDisplacement", "Conservator", null);
            Assert.That(EpicEncounterMechanics.ValueFor("AllowDisplacement", "Conservator"), Is.Null);
            Assert.That(EpicEncounterMechanics.On("NoSuchSwitch", "Conservator"), Is.False);
            Assert.That(EpicEncounterMechanics.On(null, "Conservator"), Is.False);
        }

        [Test]
        public void WhatShipsOnIsWhatTheRaidPlays()
        {
            string[] shipped = EpicEncounterMechanics.PerEncounterValues();
            // Wave 2 ships more per dungeon (UT_EpicWaveTwo, UT_EpicWave2MidHib, UT_EpicWave2Albion); these are the foundation's.
            Assert.That(shipped, Is.SupersetOf(new[]
            {
                "AllowBotDisplacement@AlbGolestandt=true", "AllowBotDisplacement@HibCuuldurach=true",
                "AllowBotDisplacement@MidGjalpinulva=true", "AllowBotDisplacement@Xanxicar=true",
                "AllowDragonThrow@AlbGolestandt=true", "AllowDragonThrow@HibCuuldurach=true", "AllowDragonThrow@MidGjalpinulva=true",
                // Epic wave 2 (UT_EpicWaveTwo): the messengers, Olcasgean and Galladoria's teleports and close-range rules.
                "AllowDragonMessengers@AlbGolestandt=true", "AllowDragonMessengers@HibCuuldurach=true",
                "AllowDragonMessengers@MidGjalpinulva=true",
                "AllowPositionPuzzle@Olcasgean=true", "AllowDisplacement@Olcasgean=true", "AllowBotDisplacement@Olcasgean=true",
                "AllowDisplacement@WaterfallAntipass=true", "AllowBotDisplacement@WaterfallAntipass=true",
                "AllowDisplacement@WaterPrimal=true", "AllowBotDisplacement@WaterPrimal=true",
                "AllowDisplacement@SpindlerBroodmother=true", "AllowBotDisplacement@SpindlerBroodmother=true",
                "AllowPositionPuzzle@Conservator=true", "AllowPositionPuzzle@Easmarach=true",
            }));
            foreach (Type dragon in new[] { typeof(AlbGolestandt), typeof(MidGjalpinulva), typeof(HibCuuldurach) })
            {
                var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(dragon);
                Assert.That(EpicEncounterMechanics.AllowDragonThrowFor(npc), Is.True, dragon.Name);
                Assert.That(EpicEncounterMechanics.AllowBotDisplacementFor(npc), Is.True, dragon.Name);
                Assert.That(EpicEncounterMechanics.AllowDragonMessengersFor(npc), Is.True, dragon.Name + " messengers (wave 2)");
                Assert.That(EpicEncounterMechanics.AllowDisplacementFor(npc), Is.False);
            }
            var xanxicar = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar));
            Assert.That(EpicEncounterMechanics.AllowBotDisplacementFor(xanxicar), Is.True);
            // Epic wave 3: Legion's throw takes bots (they come back down by the recall); still no Darkness Falls raid AI.
            Assert.That(EpicEncounterMechanics.On("AllowBotDisplacement", "Legion"), Is.True, "wave 3: bots thrown, recalled down");
            EpicEncounterMechanics.SetFor("AllowDragonThrow", "AlbGolestandt", false);
            EpicEncounterMechanics.ResetPerEncounter();
            Assert.That(EpicEncounterMechanics.On("AllowDragonThrow", "AlbGolestandt"), Is.True);
        }

        // ---- Falls and landings ----------------------------------------------------------------------------------------

        [TestCase(0, 0, 0)]
        [TestCase(100, 0, 0)]
        [TestCase(180, 0, 0)]     // 509 units/s at impact: 0%
        [TestCase(400, 0, 17)]
        [TestCase(712, 0, 34)]    // the fork's half lair throw
        [TestCase(1424, 0, 62)]   // Golestandt's first spot above the lair floor
        [TestCase(1424, 3, 42)]   // Safe Fall 3
        [TestCase(20000, 0, 99)]
        public void FallDamageFollowsThePlayersRule(int height, int safeFall, int expected)
        {
            Assert.That(EncounterDisplacement.FallDamagePercent(height, safeFall), Is.EqualTo(expected));
        }

        [Test]
        public void FallTimeFollowsGravity()
        {
            Assert.That(EncounterDisplacement.FallMilliseconds(0), Is.Zero);
            Assert.That(EncounterDisplacement.FallMilliseconds(1424), Is.InRange(1950, 2040));
        }

        [Test]
        public void KindsSetTheExpectedRecovery()
        {
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Throw, 1000, false), Is.EqualTo(DisplacementRecovery.Falls));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Throw, 0, false), Is.EqualTo(DisplacementRecovery.WalkBack));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Teleport, 10, false), Is.EqualTo(DisplacementRecovery.WalkBack));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Jail, 0, false), Is.EqualTo(DisplacementRecovery.Wait));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Jail, 0, true), Is.EqualTo(DisplacementRecovery.NeedsRescue));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Pull, 0, false), Is.EqualTo(DisplacementRecovery.None));
            Assert.That(EncounterDisplacement.Expect(DisplacementKind.Barrier, 0, false), Is.EqualTo(DisplacementRecovery.None));
            Assert.That(R.JailNeedsRescue("Jailer"), Is.True, "Vifil's jail ends by killing the tunnel guardians");
            Assert.That(R.JailNeedsRescue("Xanxicar"), Is.False);
        }

        private sealed class FakeNav : IPathfindingMgr
        {
            public Vector3? Floor, Closest;
            public bool Available = true;
            public bool Init() => true;
            public void Stop() { }
            public bool RegisterDoor(GameDoorBase door) => true;
            public bool UpdateDoorFlags(GameDoorBase door) => true;
            public PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination) => default;
            public Vector3? GetMoveAlongSurface(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters) => null;
            public Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters) => null;
            public Vector3? GetClosestPoint(Zone zone, Vector3 position, EDtPolyFlags[] filters) => Closest;
            public Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange, float zRange, EDtPolyFlags[] filters) => Closest;
            public Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters) => null;
            public Vector3? GetRoofAbove(Zone zone, Vector3 position, float maxHeight, EDtPolyFlags[] filters) => null;
            public Vector3? GetFloorBeneath(Zone zone, Vector3 position, float maxDepth, EDtPolyFlags[] filters) => Floor;
            public bool TrySnapToMesh(Zone zone, ref Vector3 position, float range) => false;
            public bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) => true;
            public bool HasNavmesh(Zone zone) => Available;
            public bool IsAvailable => Available;
            public EDtPolyFlags[] DefaultFilters => [];
            public EDtPolyFlags[] BlockingDoorAvoidanceFilters => [];
        }

        private static Zone TestZone() => new(null, 200, "test", 0, 0, 65536, 65536, 200, false, 0, false, 0, 0, 0, 0, 0);

        [Test]
        public void ABotLandsOnTheFloorUnderTheSpotOrTheNearestFloor()
        {
            var nav = new FakeNav { Floor = new Vector3(391348, 755751, 391) };
            var spot = new Vector3(391348, 755751, 1815);
            Assert.That(EncounterDisplacement.TryLanding(nav, TestZone(), spot, out Vector3 landing), Is.True);
            Assert.That(landing.Z, Is.EqualTo(391));

            nav = new FakeNav { Floor = null, Closest = new Vector3(391500, 755751, 400) };
            Assert.That(EncounterDisplacement.TryLanding(nav, TestZone(), spot, out landing), Is.True, "no floor straight under: nearest");
            Assert.That(landing.X, Is.EqualTo(391500));

            nav = new FakeNav { Floor = new Vector3(395000, 755751, 391), Closest = null };
            Assert.That(EncounterDisplacement.TryLanding(nav, TestZone(), spot, out _), Is.False, "a 'floor' far to the side is not under the spot");

            nav = new FakeNav { Floor = null, Closest = new Vector3(391348, 755751, 2600) };
            Assert.That(EncounterDisplacement.TryLanding(nav, TestZone(), spot, out _), Is.False, "nothing well above the spot");

            Assert.That(EncounterDisplacement.TryLanding(new FakeNav { Available = false }, TestZone(), spot, out _), Is.False);
            Assert.That(EncounterDisplacement.TryLanding(nav, null, spot, out _), Is.False);
        }

        // ---- The recovery (pure state machine) ---------------------------------------------------------------------------

        private static R.Observation See(long now, Vector3 at, Vector3 home, bool attacked = false, bool released = false,
            bool atFight = false, bool corridor = true, bool alive = true) =>
            new(now, alive, at, home, attacked, released, atFight, (_, _) => corridor);

        /// <summary>Walks a simulated bot from its landing towards home at <paramref name="speed"/> units/s, one turn a second,
        /// and returns the orders it was given.</summary>
        private static List<R.Order> Simulate(R.Plan plan, long start, Vector3 home, float speed, int seconds,
            Func<long, bool> attacked = null, bool corridor = true)
        {
            var orders = new List<R.Order>();
            Vector3 at = plan.Landing;
            for (int s = 0; s <= seconds; s++)
            {
                long now = start + s * 1_000L;
                R.Order o = R.Step(plan, See(now, at, home, attacked?.Invoke(now) == true, corridor: corridor));
                orders.Add(o);
                if (o.Kind is R.OrderKind.Done or R.OrderKind.Recall) break;
                if (o.Kind == R.OrderKind.Walk)
                {
                    Vector3 d = home - at;
                    float len = d.Length();
                    at = len <= speed ? home : at + d / len * speed;
                }
            }
            return orders;
        }

        [Test]
        public void AThrownBotFallsTakesTheDamageAndWalksBackToItsParty()
        {
            var origin = new Vector3(391326, 755351, 391);
            var landing = new Vector3(398605, 754458, 300);
            var plan = R.Plan.For(DisplacementKind.Throw, 10_000, landing, fallHeight: 1104, safeFallLevel: 0, onMesh: true);
            Assert.That(plan.FallPercent, Is.EqualTo(EncounterDisplacement.FallDamagePercent(1104, 0)).And.GreaterThan(0));

            R.Order first = R.Step(plan, See(10_000, landing, origin));
            Assert.That(first.Kind, Is.EqualTo(R.OrderKind.Hold), "in the air for the fall's time");
            R.Order land = R.Step(plan, See(10_000 + EncounterDisplacement.FallMilliseconds(1104), landing, origin));
            Assert.That(land.Kind, Is.EqualTo(R.OrderKind.Land));
            Assert.That(land.Point, Is.EqualTo(landing));
            Assert.That(land.FallPercent, Is.EqualTo(plan.FallPercent));

            List<R.Order> orders = Simulate(plan, 12_000, origin, 180, 120);
            Assert.That(orders[0].Kind, Is.EqualTo(R.OrderKind.Walk));
            Assert.That(orders[0].Point, Is.EqualTo(origin));
            Assert.That(orders.Last().Kind, Is.EqualTo(R.OrderKind.Done));
            Assert.That(orders.Last().Reason, Is.EqualTo("back with the raid"));
            Assert.That(orders.Count(o => o.Kind == R.OrderKind.Recall), Is.Zero, "no fallback needed");
            Assert.That(orders.Count, Is.LessThan(60), "about 7,000 units at 180 a second");
        }

        [Test]
        public void ABotThatReachesTheBossIsBackEvenBeforeItsPost()
        {
            var plan = R.Plan.For(DisplacementKind.Teleport, 0, new Vector3(0, 0, 0), 0, 0, true);
            Assert.That(R.Step(plan, See(0, Vector3.Zero, new Vector3(5000, 0, 0))).Kind, Is.EqualTo(R.OrderKind.Land));
            Assert.That(R.Step(plan, See(1_000, Vector3.Zero, new Vector3(5000, 0, 0))).Kind, Is.EqualTo(R.OrderKind.Walk));
            Assert.That(R.Step(plan, See(2_000, new Vector3(200, 0, 0), new Vector3(5000, 0, 0), atFight: true)).Kind, Is.EqualTo(R.OrderKind.Done));
        }

        [Test]
        public void NoPathBackGivesTheShortRecall()
        {
            var home = new Vector3(48000, 49000, 20000);
            var plan = R.Plan.For(DisplacementKind.Teleport, 0, new Vector3(48200, 49566, 20833), 0, 0, true);
            R.Step(plan, See(0, plan.Landing, home));
            R.Order o = R.Step(plan, See(500, plan.Landing, home, corridor: false));
            Assert.That(o.Kind, Is.EqualTo(R.OrderKind.Hold));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Recalling));
            Assert.That(plan.Reason, Is.EqualTo("no path back"));
            Assert.That(R.Step(plan, See(500 + R.RecallMilliseconds - 1, plan.Landing, home)).Kind, Is.EqualTo(R.OrderKind.Hold), "the recall takes its time");
            R.Order recall = R.Step(plan, See(500 + R.RecallMilliseconds, plan.Landing, home));
            Assert.That(recall.Kind, Is.EqualTo(R.OrderKind.Recall));
            Assert.That(recall.Point, Is.EqualTo(home));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Done));
        }

        [Test]
        public void AnOffMeshLandingGivesTheRecall()
        {
            var plan = R.Plan.For(DisplacementKind.Throw, 0, new Vector3(100, 0, 0), 0, 0, onMesh: false);
            R.Step(plan, See(0, plan.Landing, new Vector3(3000, 0, 0)));
            R.Step(plan, See(100, plan.Landing, new Vector3(3000, 0, 0)));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Recalling));
            Assert.That(plan.Reason, Is.EqualTo("landed off the navmesh"));
        }

        [Test]
        public void ABotThatMakesNoProgressIsRecalled()
        {
            var home = new Vector3(5000, 0, 0);
            var plan = R.Plan.For(DisplacementKind.Throw, 0, Vector3.Zero, 0, 0, true);
            List<R.Order> orders = Simulate(plan, 0, home, speed: 0, seconds: 60); // walks into a wall
            Assert.That(orders.Last().Kind, Is.EqualTo(R.OrderKind.Recall));
            Assert.That(orders.Last().Point, Is.EqualTo(home));
            Assert.That(plan.Reason, Is.EqualTo("no progress walking back"));
            int start = orders.FindIndex(o => o.Kind == R.OrderKind.Hold);
            Assert.That(start, Is.InRange(10, 12), "found within the progress window");
            Assert.That(orders.Count - 1 - start, Is.EqualTo(R.RecallMilliseconds / 1_000), "then the recall's channel");
        }

        [Test]
        public void ABotTooSlowForItsBudgetIsRecalled()
        {
            var home = new Vector3(4000, 0, 0);
            var plan = R.Plan.For(DisplacementKind.Throw, 0, Vector3.Zero, 0, 0, true);
            // 16 units a second: progress each window, but far past twice the walk time.
            List<R.Order> orders = Simulate(plan, 0, home, speed: 16, seconds: 200);
            Assert.That(orders.Last().Kind, Is.EqualTo(R.OrderKind.Recall));
            Assert.That(plan.Reason, Is.EqualTo("walk back took too long"));
            Assert.That(orders.Count, Is.InRange(55, 62));
            Assert.That(R.WalkBudget(4000), Is.EqualTo(54_444));
            Assert.That(R.WalkBudget(10), Is.EqualTo(R.MinimumWalkMilliseconds));
            Assert.That(R.WalkBudget(1_000_000), Is.EqualTo(R.MaximumWalkMilliseconds));
        }

        [Test]
        public void ABotAttackedOnTheWayFightsAndIsNotCountedStuck()
        {
            var home = new Vector3(3000, 0, 0);
            var plan = R.Plan.For(DisplacementKind.Throw, 0, Vector3.Zero, 0, 0, true);
            // Attacked from 3 s to 40 s (it stands and fights), then walks on.
            List<R.Order> orders = Simulate(plan, 0, home, 180, 120, attacked: now => now >= 3_000 && now < 40_000);
            Assert.That(orders.Count(o => o.Kind == R.OrderKind.Fight), Is.GreaterThan(30));
            Assert.That(orders.Last().Kind, Is.EqualTo(R.OrderKind.Done));
            Assert.That(orders.Any(o => o.Kind == R.OrderKind.Recall), Is.False);
        }

        [Test]
        public void AJailedBotWaitsFightsItsGuardsAndWalksOutWhenFreed()
        {
            var jail = new Vector3(16631, 58683, 10858);
            var home = new Vector3(27086, 61284, 10349);
            var plan = R.Plan.For(DisplacementKind.Jail, 0, jail, 0, 0, true);
            Assert.That(R.Step(plan, See(0, jail, home)).Kind, Is.EqualTo(R.OrderKind.Land));
            Assert.That(R.Step(plan, See(1_000, jail, home)).Kind, Is.EqualTo(R.OrderKind.Hold));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Jailed));
            Assert.That(R.Step(plan, See(2_000, jail, home, attacked: true)).Kind, Is.EqualTo(R.OrderKind.Fight), "fights the guardians");
            Assert.That(R.Step(plan, See(30_000, jail, home)).Kind, Is.EqualTo(R.OrderKind.Hold), "still jailed");
            R.Order freed = R.Step(plan, See(60_000, jail, home, released: true));
            Assert.That(freed.Kind, Is.EqualTo(R.OrderKind.Walk));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Walking));
        }

        [Test]
        public void AJailNeverHoldsABotForever()
        {
            var plan = R.Plan.For(DisplacementKind.Jail, 0, Vector3.Zero, 0, 0, true);
            R.Step(plan, See(0, Vector3.Zero, new Vector3(9000, 0, 0)));
            R.Step(plan, See(1_000, Vector3.Zero, new Vector3(9000, 0, 0)));
            R.Step(plan, See(1_000 + R.JailMaxMilliseconds, Vector3.Zero, new Vector3(9000, 0, 0)));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Recalling));
            Assert.That(plan.Reason, Is.EqualTo("held in the jail too long"));
        }

        [Test]
        public void AFreedJailWithNoPathOutRecalls()
        {
            var plan = R.Plan.For(DisplacementKind.Jail, 0, Vector3.Zero, 0, 0, true);
            R.Step(plan, See(0, Vector3.Zero, new Vector3(9000, 0, 0)));
            R.Step(plan, See(1_000, Vector3.Zero, new Vector3(9000, 0, 0)));
            R.Step(plan, See(2_000, Vector3.Zero, new Vector3(9000, 0, 0), released: true, corridor: false));
            Assert.That(plan.Stage, Is.EqualTo(R.Stage.Recalling));
        }

        [Test]
        public void PullsBarriersAndDeathEndTheRecovery()
        {
            foreach (DisplacementKind kind in new[] { DisplacementKind.Pull, DisplacementKind.Barrier })
            {
                var plan = R.Plan.For(kind, 0, Vector3.Zero, 0, 0, true);
                Assert.That(R.Step(plan, See(0, Vector3.Zero, new Vector3(9000, 0, 0))).Kind, Is.EqualTo(R.OrderKind.Land));
                Assert.That(R.Step(plan, See(1, Vector3.Zero, new Vector3(9000, 0, 0))).Kind, Is.EqualTo(R.OrderKind.Done), kind.ToString());
            }
            var thrown = R.Plan.For(DisplacementKind.Throw, 0, Vector3.Zero, 2000, 0, true);
            Assert.That(R.Step(thrown, See(0, Vector3.Zero, Vector3.One, alive: false)).Kind, Is.EqualTo(R.OrderKind.Done));
            Assert.That(thrown.Reason, Is.EqualTo("died"));
        }

        // ---- Healers ----------------------------------------------------------------------------------------------------

        [TestCase(true, 70, false, 75, true)]    // displaced first
        [TestCase(true, 70, false, null, true)]
        [TestCase(true, 95, false, 75, false)]   // a displaced raider at 95% needs no special care
        [TestCase(false, 20, false, 75, false)]  // not displaced: ordinary triage
        [TestCase(true, 70, false, 30, false)]   // an emergency elsewhere, lower, still comes first
        [TestCase(true, 25, false, 30, true)]    // ...unless the displaced one is lower
        [TestCase(true, 50, true, 60, true)]     // two displaced: the lower
        [TestCase(true, 60, true, 50, false)]
        public void HealersPreferDisplacedRaiders(bool candidateDisplaced, int candidate, bool currentDisplaced, int? current, bool expected)
        {
            Assert.That(R.PreferPatient(candidateDisplaced, candidate, currentDisplaced, current), Is.EqualTo(expected));
        }

        // ---- Players move exactly as before ----------------------------------------------------------------------------

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public bool SawDisplacement;
            public Point3D Destination;
            public int Moves;
            public override bool IsAlive => true;
            public override bool MoveTo(ushort region, int x, int y, int z, ushort heading)
            {
                SawDisplacement = DragonCombatGeometry.IsDisplacing(this);
                Destination = new Point3D(x, y, z);
                Moves++;
                return true;
            }
        }

        private static T Make<T>() where T : GameLiving
        {
            var living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(living, new PropertyCollection());
            return living;
        }

        [Test]
        public void APlayerIsMovedToTheScriptsSpotAsBeforeButNotAsAPartyPortal()
        {
            var player = Make<Player>();
            var xanxicar = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar));
            DisplacementEvent seen = null;
            void Handler(DisplacementEvent e) => seen = e;
            EncounterDisplacement.Displaced += Handler;
            try
            {
                Assert.That(EncounterDisplacement.Move(player, 62, 32338, 32387, 16539, 1830, DisplacementKind.Throw, xanxicar), Is.True);
            }
            finally { EncounterDisplacement.Displaced -= Handler; }
            Assert.That(player.Moves, Is.EqualTo(1));
            Assert.That(player.Destination.X, Is.EqualTo(32338));
            Assert.That(player.Destination.Y, Is.EqualTo(32387));
            Assert.That(player.Destination.Z, Is.EqualTo(16539), "players are put exactly where the script said; the client falls");
            Assert.That(player.SawDisplacement, Is.True, "companions and pet are not carried along");
            Assert.That(DragonCombatGeometry.IsDisplacing(player), Is.False, "later genuine portals work normally");
            Assert.That(DragonCombatGeometry.IsRecoveringFromThrow(player), Is.True);
            Assert.That(EncounterDisplacement.IsRecovering(player), Is.True);
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.IsBot, Is.False);
            Assert.That(seen.Encounter, Is.EqualTo("Xanxicar"));
            Assert.That(R.WasDisplaced(player, GameLoop.GameLoopTime), Is.True, "healers see it");
        }

        [Test]
        public void APlayerIsMovedEvenWhereBotDisplacementIsOff()
        {
            var player = Make<Player>();
            var legion = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Scripts.Legion));
            Assert.That(EncounterDisplacement.Move(player, 249, 48200, 49566, 20833, 1028, DisplacementKind.Teleport, legion), Is.True);
            Assert.That(player.Moves, Is.EqualTo(1));
        }

        // ---- Bots ---------------------------------------------------------------------------------------------------------

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public ushort Region;
            public int InRegionMoves;
            public Point3D Last;
            public override bool IsAlive => true;
            public override ushort CurrentRegionID { get => Region; set => Region = value; }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
            public override void StopMoving() { }
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public int Px, Py, Pz;
            public override int X => Px;
            public override int Y => Py;
            public override int Z => Pz;
            public override bool MoveInRegion(ushort regionID, int x, int y, int z, ushort heading, bool forceMove)
            {
                InRegionMoves++;
                Last = new Point3D(x, y, z);
                Px = x; Py = y; Pz = z;
                return true;
            }
        }

        [Test]
        public void ABotStaysWhereItsEncountersBotDisplacementIsOff()
        {
            var bot = Make<Bot>();
            bot.Region = 249;
            var legion = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Scripts.Legion));
            EpicEncounterMechanics.SetFor("AllowBotDisplacement", "Legion", false); // on since wave 3; the owner can turn it off
            Assert.That(EncounterDisplacement.Move(bot, 249, 48200, 49566, 20833, 1028, DisplacementKind.Teleport, legion), Is.False);
            Assert.That(bot.InRegionMoves, Is.Zero);
            Assert.That(R.IsRecovering(bot), Is.False);
        }

        [Test]
        public void ABotXanxicarThrowsIsMovedRaisesTheEventAndIsBroughtBack()
        {
            // No navmesh in a unit test (no region): the landing is off the mesh, so the bot gets the fair fallback, the
            // recall to where it stood. The walk-back with a navmesh is the state machine's simulation above.
            var bot = Make<Bot>();
            bot.Region = 62;
            bot.Px = 31000; bot.Py = 31000; bot.Pz = 16000;
            var xanxicar = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar));
            DisplacementEvent seen = null;
            void Handler(DisplacementEvent e) => seen = e;
            EncounterDisplacement.Displaced += Handler;
            try
            {
                Assert.That(EncounterDisplacement.Move(bot, 62, 32338, 32387, 16539, 1830, DisplacementKind.Throw, xanxicar), Is.True);
            }
            finally { EncounterDisplacement.Displaced -= Handler; }
            Assert.That(bot.InRegionMoves, Is.EqualTo(1));
            Assert.That(bot.Last.X, Is.EqualTo(32338));
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.IsBot, Is.True);
            Assert.That(seen.OnMesh, Is.False);
            Assert.That(seen.From, Is.EqualTo(new Vector3(31000, 31000, 16000)));
            Assert.That(R.IsRecovering(bot), Is.True);
            Assert.That(R.StageOf(bot), Is.EqualTo(R.Stage.Airborne));
            Assert.That(EncounterDisplacement.IsRecovering(bot), Is.True);

            // The brain's hook: it lands, finds no floor to walk on, and starts the short recall instead of being stuck.
            Assert.That(R.Think(bot), Is.True, "lands");
            Assert.That(R.StageOf(bot), Is.EqualTo(R.Stage.Landed));
            Assert.That(R.Think(bot), Is.True, "recall channel");
            Assert.That(R.StageOf(bot), Is.EqualTo(R.Stage.Recalling));
        }

        // ---- Lich Lord Sanguis: finished by spells ------------------------------------------------------------------------

        [Test]
        public void LichLordSanguisLastFivePercentIsFinishedBySpells()
        {
            const GameNPC.eFlags Hidden = GameNPC.eFlags.GHOST | GameNPC.eFlags.CANTTARGET;
            Assert.That(EpicEncounterTactics.FinishableUntargetable("LichLordSanguis", true, Hidden, true), Is.True);
            Assert.That(EpicEncounterTactics.FinishableUntargetable("LichLordSanguis", true, GameNPC.eFlags.GHOST, true), Is.False,
                "targetable: nothing special");
            Assert.That(EpicEncounterTactics.FinishableUntargetable("LichLordSanguis", true, Hidden, false), Is.False,
                "out of the fight: left alone (it resets)");
            Assert.That(EpicEncounterTactics.FinishableUntargetable("LichLordSanguis", false, Hidden, true), Is.False);
            Assert.That(EpicEncounterTactics.FinishableUntargetable("FirePatch", true, Hidden, true), Is.False,
                "other untargetable NPCs (the fire primal's trail) stay ignored");
            Assert.That(EpicEncounterTactics.MayFinishUntargetable(null, null), Is.False);
        }

        // ---- Dragons --------------------------------------------------------------------------------------------------------

        [Test]
        public void EachDragonHasItsFiveLairSpotsFromTheOriginal()
        {
            Assert.That(DragonEncounterThrow.SpotsFor((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(AlbGolestandt))), Has.Length.EqualTo(5));
            Assert.That(DragonEncounterThrow.SpotsFor((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(MidGjalpinulva))), Has.Length.EqualTo(5));
            Assert.That(DragonEncounterThrow.SpotsFor((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(HibCuuldurach))), Has.Length.EqualTo(5));
            Assert.That(DragonEncounterThrow.Spots["AlbGolestandt"][0], Is.EqualTo(new DragonEncounterThrow.Spot(391348, 755751, 1815, 2069)));
            Assert.That(DragonEncounterThrow.Spots["MidGjalpinulva"][0], Is.EqualTo(new DragonEncounterThrow.Spot(708632, 1021688, 3721, 2499)));
            Assert.That(DragonEncounterThrow.Spots["HibCuuldurach"][0], Is.EqualTo(new DragonEncounterThrow.Spot(408807, 706640, 4315, 1588)));
            Assert.That(DragonEncounterThrow.SpotsFor((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Xanxicar))), Is.Empty);
        }

        [Test]
        public void TheGustPicksTwoToFiveAndAPlayerAmongBotsIsPickedLessThanAlone()
        {
            var random = new Random(7);
            var player = Make<Player>();
            List<GameLiving> alone = [player];
            List<GameLiving> crowd = [player, .. Enumerable.Range(0, 11).Select(_ => (GameLiving)Make<Bot>())];
            int pickedAlone = 0, pickedInCrowd = 0;
            const int Trials = 4_000;
            for (int i = 0; i < Trials; i++)
            {
                List<GameLiving> a = DragonEncounterThrow.Pick(alone, random);
                Assert.That(a, Has.Count.EqualTo(1));
                if (a.Contains(player)) pickedAlone++;
                List<GameLiving> c = DragonEncounterThrow.Pick(crowd, random);
                Assert.That(c.Count, Is.InRange(2, 5));
                Assert.That(c.Distinct().Count(), Is.EqualTo(c.Count));
                if (c.Contains(player)) pickedInCrowd++;
            }
            Assert.That(pickedAlone, Is.EqualTo(Trials), "alone, as in the original: every gust");
            // Expected share with 12 raiders: 3.5 picks of 12, about 29%.
            Assert.That(pickedInCrowd / (double)Trials, Is.InRange(0.24, 0.34));
        }

        [Test]
        public void TheGustDoesNothingWithItsSwitchOff()
        {
            var dragon = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(AlbGolestandt));
            EpicEncounterMechanics.SetFor("AllowDragonThrow", "AlbGolestandt", false);
            Assert.That(DragonEncounterThrow.Throw(dragon, []), Is.Zero);
        }

        [Test]
        public void TheLandingAuditListsEveryFixedSpot()
        {
            var spots = UT_DisplacementLandingAudit.Spots;
            Assert.That(spots.Count(s => s.Encounter == "AlbGolestandt"), Is.EqualTo(5));
            Assert.That(spots.Count(s => s.Encounter == "MidGjalpinulva"), Is.EqualTo(5));
            Assert.That(spots.Count(s => s.Encounter == "HibCuuldurach"), Is.EqualTo(5));
            Assert.That(spots.Count(s => s.Encounter == "Xanxicar"), Is.EqualTo(1));
            Assert.That(spots.Select(s => (s.Region, s.X, s.Y, s.Z)).Distinct().Count(), Is.EqualTo(spots.Length));
        }

        [TestCase(typeof(DOL.AI.Brain.AlbGolestandtBrain))]
        [TestCase(typeof(DOL.AI.Brain.MidGjalpinulvaBrain))]
        [TestCase(typeof(DOL.AI.Brain.HibCuuldurachBrain))]
        public void TheRestoredCallbacksExistBehindTheirSwitches(Type type)
        {
            Assert.That(type.GetMethod("ThrowRaiders", BindingFlags.NonPublic | BindingFlags.Instance), Is.Not.Null);
            Assert.That(type.GetMethod("SpawnMssengers", BindingFlags.NonPublic | BindingFlags.Instance), Is.Not.Null);
        }
    }
}
