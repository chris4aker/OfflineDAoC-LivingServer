using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 1: the seam reversal guard (port of True165 d1320e6, last 2f0f143) on our reversal test. Checked against
    /// #555: on 11/23 (build 1.2.2 868a82d) Hobnobber and Roiselna, level 7-8 Hibernian gamebots in Cruachan Gorge (211),
    /// stepped 334880,417728 &gt; 334880,417856 into Connacht (207) and back on the way to the orchard nipper camp at
    /// 355817,484592 (Lough Derg, 200) until AUTONOMOUS_ZONE_SEAM_CYCLE at the third reversal.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_SeamReversalGuard570
    {
        private static readonly Vector3 Gorge = new(334880, 417728, 6528);
        private static readonly Vector3 Connacht = new(334880, 417856, 6528);
        private static readonly Vector3 OrchardNipperCamp = new(355817, 484592, 6728);
        private static readonly AutonomousZoneBoundaryRouting.Step IntoConnacht = new(Gorge, Connacht);
        private static readonly AutonomousZoneBoundaryRouting.Step BackToTheGorge = new(Connacht, Gorge);

        [Test]
        public void The555StepPairStopsAtTheSecondTurnNotBefore()
        {
            var state = default(AutonomousSeamReversalGuard.State);
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, IntoConnacht, OrchardNipperCamp, 1_000), Is.False, "211 > 207");
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, BackToTheGorge, OrchardNipperCamp, 3_000), Is.False,
                "207 > 211: the first turn is allowed");
            Assert.That(state.Reversals, Is.EqualTo(1));
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, IntoConnacht, OrchardNipperCamp, 5_000), Is.True,
                "211 > 207 again: the second turn ends the trip");
            Assert.That(AutonomousWorldBotController.MaximumSeamReversals, Is.EqualTo(AutonomousSeamReversalGuard.TriggerReversals));
        }

        [Test]
        public void AGoalOnTheBorderMayBeCrossedBackAndForth()
        {
            var state = default(AutonomousSeamReversalGuard.State);
            Vector3 onTheBorder = new(335200, 417800, 6528);
            AutonomousSeamReversalGuard.ShouldFail(ref state, IntoConnacht, onTheBorder, 0);
            AutonomousSeamReversalGuard.ShouldFail(ref state, BackToTheGorge, onTheBorder, 1_000);
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, IntoConnacht, onTheBorder, 2_000), Is.False);
            Assert.That(state.Reversals, Is.EqualTo(2), "counted, but exempt");
            Assert.That(AutonomousSeamReversalGuard.Exempt(IntoConnacht, onTheBorder), Is.True);
            Assert.That(AutonomousSeamReversalGuard.Exempt(IntoConnacht, OrchardNipperCamp), Is.False);
        }

        [Test]
        public void ANewGoalAnOldStepOrAStepThatGoesOnStartsTheCountAgain()
        {
            var state = default(AutonomousSeamReversalGuard.State);
            AutonomousSeamReversalGuard.Note(ref state, IntoConnacht, OrchardNipperCamp, 0);
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, BackToTheGorge, OrchardNipperCamp + new Vector3(600, 0, 0), 1_000),
                Is.EqualTo(0), "the goal moved 600: a new trip");
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, IntoConnacht, OrchardNipperCamp + new Vector3(600, 0, 0),
                1_000 + AutonomousSeamReversalGuard.WindowMilliseconds), Is.EqualTo(0), "older than the window");
            var onward = new AutonomousZoneBoundaryRouting.Step(new(340000, 430000, 6528), new(340000, 430140, 6528));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, onward, OrchardNipperCamp + new Vector3(600, 0, 0), 2_000_000),
                Is.EqualTo(0), "the next border of the chain");
        }

        [Test]
        public void OnlyANonLeaderMemberOfADynamicGroupHolds()
        {
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(dynamicGroup: true, isLeader: false, inRealmRaid: false), Is.True);
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(true, true, false), Is.False, "the leader gives the camp up");
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(false, false, false), Is.False, "a solo bot gives the camp up");
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(true, false, true), Is.False, "a realm raid retries its own way");
            Assert.That(AutonomousSeamReversalGuard.MemberHoldMilliseconds,
                Is.LessThan(AutonomousWorldBotController.RouteStallReplotMilliseconds), "a held member never becomes a route stall");
        }

        [Test]
        public void AForgottenStepIsPlannedAgainNotHandedOut()
        {
            var region = new Region(new RegionData { Id = 200, Name = "seam test", Description = "seam test", Mobs = [] });
            var gorge = new Zone(region, 211, "gorge", 327680, 393216, 65536, 65536, 211, false, 0, false, 0, 0, 0, 0, 0);
            var lough = new Zone(region, 200, "lough", 327680, 458752, 65536, 65536, 200, false, 0, false, 0, 0, 0, 0, 0);
            region.Zones.Add(gorge);
            region.Zones.Add(lough);
            AutonomousZoneItinerary.RememberSolvedForTests(region, gorge, lough, Gorge, OrchardNipperCamp, IntoConnacht);
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, gorge, lough, Gorge, OrchardNipperCamp), Is.EqualTo("solved"));
            AutonomousZoneItinerary.ForgetStep(region, gorge, lough, Gorge, OrchardNipperCamp);
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, gorge, lough, Gorge, OrchardNipperCamp), Is.EqualTo("failed"),
                "remembered as failed for the next bot from the same spot");
        }

        [Test]
        public void AHeldMembersStepIsForgottenButNotRememberedAsFailed()
        {
            var region = new Region(new RegionData { Id = 201, Name = "seam test", Description = "seam test", Mobs = [] });
            var gorge = new Zone(region, 211, "gorge", 327680, 393216, 65536, 65536, 211, false, 0, false, 0, 0, 0, 0, 0);
            var lough = new Zone(region, 200, "lough", 327680, 458752, 65536, 65536, 200, false, 0, false, 0, 0, 0, 0, 0);
            region.Zones.Add(gorge);
            region.Zones.Add(lough);
            AutonomousZoneItinerary.RememberSolvedForTests(region, gorge, lough, Gorge, OrchardNipperCamp, IntoConnacht);
            AutonomousZoneItinerary.ForgetStep(region, gorge, lough, Gorge, OrchardNipperCamp, rememberFailed: false);
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, gorge, lough, Gorge, OrchardNipperCamp), Is.Null,
                "planned afresh from the same spot after the hold, never failed outright");
        }

        [Test]
        public void AMemberHoldsAtMostTwiceInTheWindowThenGivesUp()
        {
            var state = default(AutonomousSeamReversalGuard.State);
            Assert.That(AutonomousSeamReversalGuard.TakeMemberHold(ref state, 1_000), Is.True);
            Assert.That(AutonomousSeamReversalGuard.TakeMemberHold(ref state, 30_000), Is.True);
            Assert.That(AutonomousSeamReversalGuard.TakeMemberHold(ref state, 60_000), Is.False, "the third catch gives up");
            Assert.That(AutonomousSeamReversalGuard.TakeMemberHold(ref state, 1_000 + AutonomousSeamReversalGuard.WindowMilliseconds),
                Is.True, "a fresh window");
            Assert.That(state.MemberHolds, Is.EqualTo(1));
            // Clearing the reversal count keeps the holds.
            AutonomousSeamReversalGuard.Note(ref state, IntoConnacht, OrchardNipperCamp, 400_000);
            AutonomousSeamReversalGuard.Note(ref state, BackToTheGorge, OrchardNipperCamp, 401_000);
            AutonomousSeamReversalGuard.ClearReversals(ref state);
            Assert.That((state.Last, state.Reversals, state.MemberHolds), Is.EqualTo(((AutonomousZoneBoundaryRouting.Step?)null, 0, 1)));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, IntoConnacht, OrchardNipperCamp, 402_000), Is.EqualTo(0),
                "the count starts again after a catch");
        }

        private static string Controller()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            string[] parts = { "GameServer", "bots", "autonomous", "AutonomousWorldBotController.cs" };
            while (dir != null && !File.Exists(Path.Combine(new[] { dir }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void TheControllerFailsTheStepForgetsBothLegsAndHoldsAMember()
        {
            string code = Controller();
            int guard = code.IndexOf("AutonomousSeamReversalGuard.ShouldFail(ref _seamReversal, resolved, destination, seamNow)");
            Assert.That(guard, Is.GreaterThan(0));
            string caught = code.Substring(guard, code.IndexOf("_lastSeamPlanZone = currentZone;", guard) - guard);
            Assert.That(caught, Does.Contain("AutonomousZoneItinerary.ForgetStep(bot.CurrentRegion, currentZone, destinationZone, current, destination,\n                        allowedZones, rememberFailed: !memberHolds)"));
            Assert.That(caught, Does.Contain("_lastSeamPlanStart, destination, allowedZones, rememberFailed: !memberHolds)"));
            Assert.That(caught, Does.Contain("reversals={reversals}"), "the counted reversals, not the constant");
            int member = caught.IndexOf("if (memberHolds)");
            int abandon = caught.IndexOf("AbandonCamp(bot, \"A zone seam route that turns back on itself\")");
            Assert.That(member, Is.GreaterThan(0).And.LessThan(abandon), "a held member returns before the camp is given up");
            Assert.That(caught.Substring(member, abandon - member), Does.Not.Contain("AbandonCamp("));
            // The hold is checked before any stall recovery and cleared with the route state.
            int hold = code.IndexOf("if (HoldAfterSeamReversal(current, now, destinationChanged))");
            int stall = code.IndexOf("AutonomousRouteRecoveryPolicy.ShouldRecoverActiveRouteStall(");
            Assert.That(hold, Is.GreaterThan(0).And.LessThan(stall));
            int reset = code.IndexOf("private void ResetRouteOrderState()");
            Assert.That(code.Substring(reset, 400), Does.Contain("_seamMemberHoldUntilTick = 0;"));
        }

        // ---- Movement harness: the controller's own zone-step code, a real movement component, a bot that moves ----------
        // The itinerary's answers are seeded in its shared memory (RememberSolvedForTests): this drives the controller's
        // guard, hold and give-up, not the planner (the planner's #555 route is UT_SeamCycle555, navmesh job).

        private sealed class Walker : GameBot
        {
            public Walker() : base((OfflineWorldBotRecord)null) { }
            public override int X { get; set; }
            public override int Y { get; set; }
            public override int Z { get; set; }
            public override ushort Heading { get; set; }
            public override bool IsAlive => true;
            public override bool InCombat => false;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override bool IsCrowdControlled => false;
            public override eRealm Realm { get; set; }
            public override int GetModified(eProperty property) => property == eProperty.MaxSpeed ? 200 : 0;
        }

        private sealed class Mesh : PathfindingMgrBase
        {
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override bool HasLineOfSight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters) => true;
            public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
            {
                nodes[0] = new(from, EDtPolyFlags.Walk);
                nodes[1] = new(to, EDtPolyFlags.Walk);
                return new(PathfindingStatus.PathFound, 2);
            }
        }

        private sealed class Server : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Empty;
        }
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private IPathfindingMgr _previousNav;
        private GameServer _previousServer;
        private PetTestLanguageScope _language;

        [SetUp]
        public void Setup()
        {
            _previousNav = PathfindingProvider.Instance;
            _previousServer = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            PathfindingProvider.SetPathfindingMgr(new Mesh());
        }

        [TearDown]
        public void Cleanup()
        {
            PathfindingProvider.SetPathfindingMgr(_previousNav);
            GameServer.LoadTestDouble(_previousServer);
            _language.Dispose();
        }

        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        private static Walker Bot(Region region, Vector3 at, string campId)
        {
            var bot = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
            bot.Realm = eRealm.Albion;
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden).SetValue(bot, new PropertyCollection());
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { ObjectiveAssignmentId = "grind", CurrentCampId = campId });
            typeof(GameNPC).GetField("m_brains", Hidden).SetValue(bot, new ArrayList());
            typeof(GameNPC).GetField("m_ownBrain", Hidden).SetValue(bot, new BotBrain());
            bot.CurrentRegion = region;
            var mover = new NpcMovementComponent(bot) { FixedSpeed = true, MaxSpeedBase = 200 };
            bot.movementComponent = mover;
            // GameLiving keeps its own field (GameNPC's is a `new` one); MaxSpeed and IsMoving read that one.
            ((GameLiving)bot).movementComponent = mover;
            bot.X = (int)at.X; bot.Y = (int)at.Y; bot.Z = (int)at.Z;
            return bot;
        }

        // Three zones in a row: the bot's (A), the one it keeps stepping into (B), the camp's (C).
        private static (Region Region, Zone A, Zone B, Zone C) World(ushort id)
        {
            var region = new Region(new RegionData { Id = id, Name = "seam walk", Description = "seam walk", Mobs = [] });
            var a = new Zone(region, 1, "a", 0, 0, 65536, 65536, 1, false, 0, false, 0, 0, 0, 0, 0) { IsPathfindingEnabled = true };
            var b = new Zone(region, 2, "b", 65536, 0, 65536, 65536, 2, false, 0, false, 0, 0, 0, 0, 0) { IsPathfindingEnabled = true };
            var c = new Zone(region, 3, "c", 131072, 0, 65536, 65536, 3, false, 0, false, 0, 0, 0, 0, 0) { IsPathfindingEnabled = true };
            region.Zones.Add(a);
            region.Zones.Add(b);
            region.Zones.Add(c);
            return (region, a, b, c);
        }

        private static readonly Vector3 SideA = new(65400, 30000, 0), SideB = new(65660, 30000, 0), Camp = new(150000, 30000, 0);

        private static bool Step(AutonomousWorldBotController controller, Walker bot, Zone from, Zone to) =>
            (bool)typeof(AutonomousWorldBotController).GetMethod("StepAcrossZoneBorder", Hidden)!
                .Invoke(controller, new object[] { bot, new Vector3(bot.X, bot.Y, bot.Z), Camp, from, to });

        private static bool Holding(AutonomousWorldBotController controller, Walker bot, long now, bool destinationChanged) =>
            (bool)typeof(AutonomousWorldBotController).GetMethod("HoldAfterSeamReversal", Hidden)!
                .Invoke(controller, new object[] { new Vector3(bot.X, bot.Y, bot.Z), now, destinationChanged });

        private static void MoveTo(Walker bot, Vector3 point)
        {
            bot.X = (int)point.X;
            bot.Y = (int)point.Y;
        }

        private static Func<Zone, bool> Allowed(Walker bot) =>
            zone => AutonomousRealmBoundary.Allows(bot.Realm, bot.CurrentRegionID, zone.ID);

        /// <summary>Seeds the turn-back: A sends the bot into B, B sends it straight back.</summary>
        private static void SeedTurnBack(Region region, Zone a, Zone b, Zone c, Walker bot)
        {
            AutonomousZoneItinerary.RememberSolvedForTests(region, a, c, SideA, Camp, new(SideA, SideB), Allowed(bot));
            AutonomousZoneItinerary.RememberSolvedForTests(region, b, c, SideB, Camp, new(SideB, SideA), Allowed(bot));
        }

        [Test]
        public void AMemberHitsTheGuardHoldsThenMovesOnAndTheGroupKeepsItsCamp()
        {
            var (region, a, b, c) = World(9570);
            Walker member = Bot(region, SideA, "shared-camp");
            Walker leader = Bot(region, Camp, "shared-camp");
            member.Group = new Group(leader);
            var controller = new AutonomousWorldBotController();
            var camp = new AutonomousBotGroupCoordinator.SharedCamp("shared-camp", "orchard nipper", "c", region.ID,
                (int)Camp.X, (int)Camp.Y, (int)Camp.Z, false, false);
            typeof(AutonomousWorldBotController).GetField("_groupDirective", Hidden)!.SetValue(controller,
                new AutonomousBotGroupCoordinator.Directive("group-570", "grind", "Grind at orchard nipper", "", eAutonomousObjectiveKind.GroupPve,
                    leader, Camp, camp, 2, 8, 0, 0, true));
            SeedTurnBack(region, a, b, c, member);

            Assert.That(Step(controller, member, a, c), Is.True, "A > B");
            MoveTo(member, SideB);
            Assert.That(Step(controller, member, b, c), Is.True, "B > A: the first turn");
            MoveTo(member, SideA);
            Assert.That(Step(controller, member, a, c), Is.False, "A > B again: the second turn is caught");

            // Held, not given up: the group's camp stands and nothing is remembered as failed.
            Assert.That(member.PersistentRecord.CurrentCampId, Is.EqualTo("shared-camp"), "no AbandonCamp");
            Assert.That(typeof(AutonomousWorldBotController).GetField("_nextPlanTick", Hidden)!.GetValue(controller), Is.EqualTo(0L));
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, a, c, SideA, Camp, Allowed(member)), Is.Null);
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, b, c, SideB, Camp, Allowed(member)), Is.Null);
            long now = GameLoop.GameLoopTime;
            Assert.That(Holding(controller, member, now, destinationChanged: false), Is.True, "waits out the hold");
            Assert.That(Holding(controller, member, now, destinationChanged: true), Is.False, "the leader walked on: follows at once");
            Assert.That(Holding(controller, member, now, destinationChanged: false), Is.False, "and the hold is over");

            // It plans again from the same spot (the fresh plan finds the way on) and walks into C.
            Vector3 intoC = new(131200, 30000, 0);
            AutonomousZoneItinerary.RememberSolvedForTests(region, a, c, SideA, Camp, new(SideA, SideB), Allowed(member));
            AutonomousZoneItinerary.RememberSolvedForTests(region, b, c, SideB, Camp, new(new(131000, 30000, 0), intoC), Allowed(member));
            Assert.That(Step(controller, member, a, c), Is.True, "A > B");
            MoveTo(member, SideB);
            Assert.That(Step(controller, member, b, c), Is.True, "B > C: onward, not a turn");
            MoveTo(member, intoC);
            Assert.That(region.GetZone(member.X, member.Y), Is.SameAs(c), "in the camp's zone");
            Assert.That(member.PersistentRecord.CurrentCampId, Is.EqualTo("shared-camp"), "the group kept its camp throughout");
        }

        [Test]
        public void ASoloBotAtTheSameTurnGivesTheCampUp()
        {
            var (region, a, b, c) = World(9571);
            Walker solo = Bot(region, SideA, "solo-camp");
            var controller = new AutonomousWorldBotController();
            SeedTurnBack(region, a, b, c, solo);
            Assert.That(Step(controller, solo, a, c), Is.True);
            MoveTo(solo, SideB);
            Assert.That(Step(controller, solo, b, c), Is.True);
            MoveTo(solo, SideA);
            Assert.That(Step(controller, solo, a, c), Is.False);
            Assert.That(solo.PersistentRecord.CurrentCampId, Is.Empty, "AbandonCamp ran");
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, a, c, SideA, Camp, Allowed(solo)), Is.EqualTo("failed"));
            Assert.That(AutonomousZoneItinerary.MemoryForTests(region, b, c, SideB, Camp, Allowed(solo)), Is.EqualTo("failed"));
        }
    }
}
