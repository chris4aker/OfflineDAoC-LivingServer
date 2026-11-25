using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public sealed class UT_AutonomousGoalDiagnostics
    {
        private static AutonomousGoalAttempt Attempt() => new()
        {
            CampId = "hib:large-frog:1", Target = "large frog", Region = 200,
            X = 10000, Y = 10000, Z = 5000,
            StartedUtc = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc),
            ExpiresUtc = new DateTime(2026, 8, 30, 1, 0, 0, DateTimeKind.Utc)
        };

        [Test]
        public void TargetMatchIncludesRegionCampAndVerticalLayer()
        {
            var attempt = Attempt();
            Assert.Multiple(() =>
            {
                Assert.That(attempt.MatchesTarget("Large Frog", 200, 10100, 10100, 5100), Is.True);
                Assert.That(attempt.MatchesTarget("large frog", 181, 10100, 10100, 5100), Is.False);
                Assert.That(attempt.MatchesTarget("large frog", 200, 20000, 10000, 5000), Is.False);
                Assert.That(attempt.MatchesTarget("large frog", 200, 10000, 10000, 7000), Is.False);
                Assert.That(attempt.MatchesTarget("bull frog", 200, 10000, 10000, 5000), Is.False);
            });
        }

        [Test]
        public void UnrelatedXpCannotMakeAnUnreachableGoalLookProductive()
        {
            var attempt = Attempt();
            attempt.Reward(false, 100);
            Assert.Multiple(() =>
            {
                Assert.That(attempt.Kills, Is.EqualTo(1));
                Assert.That(attempt.Experience, Is.EqualTo(100));
                Assert.That(attempt.TargetKills, Is.Zero);
                Assert.That(AutonomousGoalAttempt.Classify(GoalAttemptEnd.NoProgress45m, attempt.TargetKills), Is.EqualTo("failed"));
            });
        }

        [TestCase(GoalAttemptEnd.Logout)]
        [TestCase(GoalAttemptEnd.GroupChanged)]
        [TestCase(GoalAttemptEnd.ServiceDetour)]
        [TestCase(GoalAttemptEnd.Reassigned)]
        public void InterruptedAttemptsDoNotCountAsFailures(GoalAttemptEnd reason)
        {
            Assert.That(AutonomousGoalAttempt.Classify(reason, 0), Is.EqualTo("interrupted"));
        }

        [Test]
        public void FinishIsExactlyOnceAndIgnoresLateEvents()
        {
            var attempt = Attempt();
            attempt.Arrive(attempt.StartedUtc.AddMinutes(2));
            attempt.Reward(true, 55);
            attempt.Died();
            attempt.RouteFailed("NoPathFound");
            object result = attempt.Finish(GoalAttemptEnd.Defeated, "test", attempt.StartedUtc.AddMinutes(10), 200, 1, 2, 3);
            attempt.Reward(true, 100);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(attempt.TargetKills, Is.EqualTo(1));
                Assert.That(attempt.TargetExperience, Is.EqualTo(55));
                Assert.That(attempt.RecoveryAttempts, Is.EqualTo(1));
                Assert.That(attempt.Deaths, Is.EqualTo(1));
                Assert.That(attempt.Finish(GoalAttemptEnd.Logout, "duplicate", DateTime.UtcNow, 0, 0, 0, 0), Is.Null);
            });
        }

        [Test]
        public void ExpiryWithNoAssignedKillsIsNotHiddenByReassignment()
        {
            var attempt = Attempt();
            object result = attempt.Finish(GoalAttemptEnd.Reassigned, "expiry", attempt.ExpiresUtc.AddSeconds(1), 200, 1, 2, 3);
            using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Multiple(() =>
            {
                Assert.That(json.RootElement.GetProperty("reason").GetString(), Is.EqualTo("Expired"));
                Assert.That(json.RootElement.GetProperty("outcome").GetString(), Is.EqualTo("failed"));
            });
        }

        [Test]
        public void SuccessfulRecoveryIsNotReportedAsTerminalRouteFailure()
        {
            var attempt = Attempt();
            attempt.RouteFailed("NoPathFound");
            attempt.Reward(true, 50);
            object result = attempt.Finish(GoalAttemptEnd.Reassigned, "productive", attempt.StartedUtc.AddMinutes(5), 200, 10, 20, 30);
            using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Multiple(() =>
            {
                Assert.That(json.RootElement.GetProperty("recoveryAttempts").GetInt32(), Is.EqualTo(1));
                Assert.That(json.RootElement.GetProperty("terminalRouteFailures").GetInt32(), Is.Zero);
                Assert.That(json.RootElement.GetProperty("endX").GetInt32(), Is.EqualTo(10));
                Assert.That(json.RootElement.GetProperty("endY").GetInt32(), Is.EqualTo(20));
                Assert.That(json.RootElement.GetProperty("endZ").GetInt32(), Is.EqualTo(30));
            });
        }

        [Test]
        public void TheAttemptLogsTheCampTargetLevel()
        {
            var attempt = new AutonomousGoalAttempt
            {
                CampId = "alb:bandit:1", Target = "bandit", TargetLevel = 23, Level = 21, Region = 1,
                StartedUtc = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc)
            };
            object result = attempt.Finish(GoalAttemptEnd.Reassigned, "t", attempt.StartedUtc.AddMinutes(3), 1, 0, 0, 0);
            using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Multiple(() =>
            {
                Assert.That(json.RootElement.GetProperty("targetLevel").GetInt32(), Is.EqualTo(23));
                Assert.That(json.RootElement.GetProperty("level").GetInt32(), Is.EqualTo(21));
                Assert.That(json.RootElement.GetProperty("schema").GetInt32(), Is.EqualTo(1), "old readers keep working");
            });
        }

        // GitHub #544: group attempts ended with arrivedUtc null although AUTONOMOUS_GROUP_ARRIVED was logged.
        [Test]
        public void GroupArrivalMarksEveryMembersAttemptAtThatCamp()
        {
            DateTime arrived = new(2026, 11, 25, 0, 4, 0, DateTimeKind.Utc);
            AutonomousGoalAttempt leader = Attempt(), follower = Attempt(), late = Attempt();
            var elsewhere = new AutonomousGoalAttempt { CampId = "hib:other:2", Target = "other", StartedUtc = arrived };
            leader.Arrive(arrived.AddMinutes(-1));
            late.Finish(GoalAttemptEnd.GroupChanged, "left", arrived.AddMinutes(-2), 200, 0, 0, 0);
            int marked = AutonomousGoalDiagnostics.MarkArrived([leader, follower, elsewhere, late, null], "hib:large-frog:1", arrived);
            Assert.Multiple(() =>
            {
                Assert.That(marked, Is.EqualTo(1), "only the follower still needed the arrival");
                Assert.That(follower.ArrivedUtc, Is.EqualTo(arrived));
                Assert.That(leader.ArrivedUtc, Is.EqualTo(arrived.AddMinutes(-1)), "an earlier arrival is kept");
                Assert.That(elsewhere.ArrivedUtc, Is.Null, "an attempt at another camp is not touched");
                Assert.That(late.ArrivedUtc, Is.Null, "a finished attempt is not touched");
                Assert.That(AutonomousGoalDiagnostics.MarkArrived([Attempt()], "", arrived), Is.Zero);
                Assert.That(AutonomousGoalDiagnostics.GroupArrived(null, "x", arrived), Is.Zero);
                Assert.That(AutonomousGoalDiagnostics.GroupArrived([null], "x", arrived), Is.Zero);
            });
        }

        [Test]
        public void TheGroupArrivalLogLineStampsTheMembersAttempts()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            string code = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous",
                "AutonomousBotGroupCoordinator.cs"));
            int arrivedLog = code.IndexOf("AUTONOMOUS_GROUP_ARRIVED group=", StringComparison.Ordinal);
            int guard = code.LastIndexOf("session.ArrivalLoggedCampId = session.Camp.Id;", arrivedLog, StringComparison.Ordinal);
            Match stamp = new Regex(@"AutonomousGoalDiagnostics\.GroupArrived\(party, session\.Camp\.Id, DateTime\.UtcNow\)")
                .Match(code, Math.Max(0, arrivedLog));
            Assert.Multiple(() =>
            {
                Assert.That(arrivedLog, Is.GreaterThan(0));
                Assert.That(guard, Is.GreaterThan(0).And.LessThan(arrivedLog), "once per camp, with the arrival line");
                Assert.That(stamp.Success, Is.True, "the members' attempts take the arrival time");
                Assert.That(code.IndexOf("WriteSessionMetadata(session, BotMembers(bot.Group));", arrivedLog, StringComparison.Ordinal),
                    Is.GreaterThan(stamp.Index), "inside MarkGrinding, right after the arrival line");
            });
        }

        [Test]
        public void DiagnosticHooksIgnoreMissingActors()
        {
            Assert.DoesNotThrow(() => AutonomousGoalDiagnostics.Begin(null, "x", "y", "z", 200, 1, 2, 3, "", 1, 1, ""));
            Assert.DoesNotThrow(() => AutonomousGoalDiagnostics.Reward(null, null, 0));
            Assert.DoesNotThrow(() => AutonomousGoalDiagnostics.End(null, GoalAttemptEnd.Logout, "test"));
        }
    }
}
