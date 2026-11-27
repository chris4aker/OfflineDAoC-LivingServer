using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 (#569 step 3): the wiring of the True165 14dcaa5 meetup and travel-budget rules into Living Server's
    /// coordinator and camp planner. The rules themselves are pinned by the ported UT_AutonomousMeetupPlanPolicy,
    /// UT_AutonomousMeetupWindow and UT_AutonomousGroupTravelBudget.
    /// </summary>
    public class UT_MeetupTravel569
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void TheMeetupMovesOnlyToATownEveryMemberReachesAndItsWaitIsSized()
        {
            string coordinator = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int start = coordinator.IndexOf("public static bool PlanMeetupNearCamp(GameBot leader, SharedCamp camp)");
            int end = coordinator.IndexOf("private static double[] MeetupTravelMinutes(", start);
            Assert.That(start, Is.GreaterThan(0));
            Assert.That(end, Is.GreaterThan(start));
            string body = coordinator[start..end];
            int choose = body.IndexOf("TryChooseTownNear(leader, members, campRegion, campPoint, out Vector3 point, out string name, reachable)");
            int rule = body.IndexOf("AutonomousMeetupPlanPolicy.ShouldMoveRendezvous(toNew, toCurrent)");
            int window = body.IndexOf("AutonomousMeetupPlanPolicy.MeetupWindowMs(moved ? farthestNew : farthestCurrent)");
            int rebase = body.IndexOf("RebaseAttendance(session, members, windowMs: windowMs);");
            Assert.That(rule, Is.GreaterThan(0), "each candidate town is checked against every member's estimate");
            Assert.That(choose, Is.GreaterThan(rule));
            Assert.That(window, Is.GreaterThan(choose));
            Assert.That(rebase, Is.GreaterThan(window), "the sized wait is the attendance window");
            // the accept predicate runs after the formation-slot check, so a town must pass both
            Assert.That(coordinator, Does.Contain("if (!valid || accept != null && !accept(chosen)) continue;"));
        }

        [Test]
        public void APartyCampPickIsCutToItsTravelBudgetAfterTheQuietFilter()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            int quiet = controller.IndexOf("legalCells = AutonomousCampCompetition.PreferQuiet(legalCells, camp => camp.Id, camp => camp.LiveMobCount,");
            int budget = controller.IndexOf("legalCells = BudgetGroupCamps(bot, legalCells, destinations);");
            Assert.That(quiet, Is.GreaterThan(0));
            Assert.That(budget, Is.GreaterThan(quiet));
            Assert.That(controller.Substring(quiet, budget - quiet), Does.Contain("sharedGroup && AutonomousGroupTravelBudget.Enabled"),
                "solo bots keep their own travel weighting");
        }

        [Test]
        public void TheDirectiveCarriesTheTaskClock()
        {
            var running = new AutonomousBotGroupCoordinator.Directive("g", "Traveling", "", "", eAutonomousObjectiveKind.GroupPve,
                null, default, null, 4, 20, 0, 3, true) { TaskDeadlineTick = 100_000, TaskPausedRemainingMilliseconds = 5 };
            Assert.That(running.TaskRemainingMilliseconds(40_000), Is.EqualTo(60_000));
            Assert.That(running.TaskRemainingMilliseconds(200_000), Is.EqualTo(0));
            var paused = running with { TaskDeadlineTick = null, TaskPausedRemainingMilliseconds = 30 * 60_000L };
            Assert.That(paused.TaskRemainingMilliseconds(40_000), Is.EqualTo(30 * 60_000L));
        }

        [Test]
        public void TheSwitchesDefaultOnAndShipOn()
        {
            Assert.That(AutonomousMeetupPlanPolicy.ReachableTownEnabled, Is.True);
            Assert.That(AutonomousMeetupPlanPolicy.TravelWindowEnabled, Is.True);
            Assert.That(AutonomousGroupTravelBudget.Enabled, Is.True);
            string settings = Source("..", "..", "tools", "release-server-settings.json");
            foreach (string key in new[] { "bot_group_camp_travel_budget", "bot_group_meetup_reachable_town", "bot_group_meetup_window_by_travel" })
                Assert.That(settings, Does.Contain($"\"{key}\": \"True\""), key);
        }
    }
}
