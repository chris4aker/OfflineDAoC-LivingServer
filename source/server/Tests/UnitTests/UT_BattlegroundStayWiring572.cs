using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #572 51B (review of #807): the pure stay rules in UT_BattlegroundExit572 pass even when a caller stops using them,
    /// so the call sites that keep an out-levelled gamebot in its battleground are pinned in the source text. Reverting
    /// any of them fails here. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_BattlegroundStayWiring572
    {
        private const string Autonomous = "source/server/GameServer/bots/autonomous/";

        private static string Source(string relative)
        {
            string root = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 10 && root != null && !File.Exists(Path.Combine(root, relative)); i++)
                root = Path.GetDirectoryName(root);
            if (root == null || !File.Exists(Path.Combine(root, relative)))
                Assert.Fail($"{relative} not found above the test folder.");
            return Regex.Replace(File.ReadAllText(Path.Combine(root, relative)), @"\s+", " ");
        }

        private static string Between(string text, string start, string end)
        {
            int from = text.IndexOf(start, StringComparison.Ordinal);
            Assert.That(from, Is.GreaterThanOrEqualTo(0), start);
            int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
            Assert.That(to, Is.GreaterThan(from), end);
            return text.Substring(from, to - from);
        }

        [Test]
        public void TheTourWorksTheBattlegroundItStaysIn()
        {
            string battleground = Source(Autonomous + "AutonomousWorldBotController.Battleground.cs");
            // The tour's bracket comes from the stay, not from the level alone (ForLevel ends an out-levelled bot's tour).
            string tourStart = Between(battleground, "_battlegroundGatherStaging = null;",
                "AutonomousObjectiveAssignments.EndBattlegroundTour(bot, $\"Outgrew the battleground brackets");
            Assert.That(tourStart, Does.Contain("BattlegroundBrackets.Bracket bracket = WorkingBattleground(bot); if (bracket == null)"));
            Assert.That(tourStart, Does.Not.Contain("BattlegroundBrackets.ForLevel(bot.Level)"));
        }

        [Test]
        public void TheTripKeepsAMemberStayingInside()
        {
            string trip = Source(Autonomous + "AutonomousBotGroupCoordinator.BattlegroundTrip.cs");
            Assert.That(trip, Does.Contain(
                "BattlegroundBrackets.ForLevel(member.Level)?.RegionId != trip.BracketRegion && " +
                "!(member.CurrentRegionID == trip.BracketRegion && AutonomousWorldBotController.StaysOutlevelled(member))"));
        }

        [Test]
        public void TheCampPlannerKeepsTheStayersBattlegroundCamps()
        {
            string controller = Source(Autonomous + "AutonomousWorldBotController.cs");
            Assert.Multiple(() =>
            {
                Assert.That(controller, Does.Contain(
                    "BattlegroundBrackets.Bracket campBattleground = AutonomousTownTeleporters.IsEnabled && " +
                    "!AutonomousBattlegroundExit.ReentryBlocked(bot.DatabaseID) ? WorkingBattleground(bot) : null;"));
                Assert.That(controller, Does.Contain(
                    "AutonomousBattlegroundExit.MayBeIn(campBattleground, member.Level, member is GameBot stayer && " +
                    "stayer.CurrentRegionID == campBattleground.RegionId && StaysOutlevelled(stayer))"));
            });
        }

        [Test]
        public void TheWatchdogKeepsTheSightingFreshDuringBrainHolds()
        {
            string watchdog = Source(Autonomous + "AutonomousStuckWatchdog.cs");
            string observe = Between(watchdog, "public static bool Observe(GameBot bot, DateTime? nowUtc = null)",
                "if (bot.IsProtectedStableMasterTravel(now))");
            Assert.That(observe, Does.Contain(
                "if (BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID)) AutonomousWorldBotController.StaysOutlevelled(bot);"));
        }
    }
}
