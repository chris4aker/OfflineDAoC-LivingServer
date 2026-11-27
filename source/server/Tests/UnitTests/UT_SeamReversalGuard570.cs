using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 1: the seam reversal guard (port of True165 d1320e6, last 2f0f143) on our reversal test. Checked against
    /// #555: on 11/23 (build 1.2.2 868a82d) Hobnobber and Roiselna, level 7-8 Hibernian gamebots in Cruachan Gorge (211),
    /// stepped 334880,417728 &gt; 334880,417856 into Connacht (207) and back on the way to the orchard nipper camp at
    /// 355817,484592 (Lough Derg, 200) until AUTONOMOUS_ZONE_SEAM_CYCLE at the third reversal.
    /// </summary>
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
            Assert.That(caught, Does.Contain("AutonomousZoneItinerary.ForgetStep(bot.CurrentRegion, currentZone, destinationZone, current, destination, allowedZones)"));
            Assert.That(caught, Does.Contain("AutonomousZoneItinerary.ForgetStep(bot.CurrentRegion, _lastSeamPlanZone, destinationZone,"));
            int member = caught.IndexOf("if (memberOnly)");
            int abandon = caught.IndexOf("AbandonCamp(bot, \"A zone seam route that turns back on itself\")");
            Assert.That(member, Is.GreaterThan(0).And.LessThan(abandon), "a held member returns before the camp is given up");
            Assert.That(caught.Substring(member, abandon - member), Does.Not.Contain("AbandonCamp("));
            // The hold is checked before any stall recovery and cleared with the route state.
            int hold = code.IndexOf("if (now < _seamMemberHoldUntilTick && !destinationChanged)");
            int stall = code.IndexOf("AutonomousRouteRecoveryPolicy.ShouldRecoverActiveRouteStall(");
            Assert.That(hold, Is.GreaterThan(0).And.LessThan(stall));
            int reset = code.IndexOf("private void ResetRouteOrderState()");
            Assert.That(code.Substring(reset, 400), Does.Contain("_seamMemberHoldUntilTick = 0;"));
        }
    }
}
