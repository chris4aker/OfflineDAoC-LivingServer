using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 (#569): the party pull-stall monitor, ported from zomboden/OfflineDAoC-True165 0e0a397 (its UT_PartyPullCamp
    /// stall tests, adapted). A grinding gamebot party with no fight and no experience is logged at 6 minutes, naming the
    /// pull gate, and ended cleanly at 11, before the 15-minute stuck watchdog teleport.
    /// </summary>
    public class UT_PartyPullStall569
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [TestCase(0L, PullStallAction.None)]
        [TestCase(5 * 60_000L + 59_000, PullStallAction.None)]
        [TestCase(6 * 60_000L, PullStallAction.Log)]
        [TestCase(10 * 60_000L + 59_000, PullStallAction.Log)]
        [TestCase(11 * 60_000L, PullStallAction.End)]
        public void TheStallIsLoggedAtSixMinutesAndEndedAtEleven(long ms, PullStallAction expected) =>
            Assert.That(AutonomousPartyPullStall.PullStallStep(ms), Is.EqualTo(expected));

        [Test]
        public void TheEndComesBeforeTheStuckWatchdogTeleport() =>
            Assert.That(AutonomousPartyPullStall.StallEndMilliseconds,
                Is.LessThan((long)AutonomousStuckWatchdog.StuckThreshold.TotalMilliseconds));

        [Test]
        public void TheStallClockRunsWhileGrindingAndThroughACampMove() => Assert.Multiple(() =>
        {
            const long t0 = 100_000;
            long since = AutonomousPartyPullStall.NextStallClock(0, "Grinding", false, false, 0, t0);
            Assert.That(since, Is.EqualTo(t0), "starts when the party grinds");
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, "Grinding", true, false, 1_000, t0 + 1_000), Is.EqualTo(t0));
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, "Traveling", true, false, 1_000, t0 + 2_000), Is.EqualTo(t0),
                "moving to another spawn point of the same camp is not progress");
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, "Grinding", true, true, 1_000, t0 + 3_000), Is.EqualTo(t0 + 3_000),
                "a fight or experience starts it again");
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, "Grinding", false, false, 1_000, t0 + 4_000), Is.EqualTo(t0 + 4_000),
                "a new camp starts it again");
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, "Grinding", true, false, 31_000, t0 + 40_000), Is.EqualTo(t0 + 40_000),
                "an unwatched gap starts it again");
            Assert.That(AutonomousPartyPullStall.NextStallClock(0, "Traveling", true, false, 1_000, t0), Is.EqualTo(0),
                "travel to a first camp never starts it");
        });

        [TestCase("Meeting up", 50_000L)]
        [TestCase("Choosing group target", 50_000L)]
        [TestCase("Regrouping", 50_000L)]
        [TestCase("Waiting for resurrection", 50_000L)]
        [TestCase("Leader staging", 50_000L)]
        [TestCase("", 50_000L)]
        public void TheStallClockStopsOutsideGrinding(string phase, long since) =>
            Assert.That(AutonomousPartyPullStall.NextStallClock(since, phase, true, false, 1_000, 200_000), Is.EqualTo(0));

        [Test]
        public void EachStallIsLoggedOnceThenEndedAndTheSwitchesAreHonoured() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.Log, false, true, true, true), Is.EqualTo(PullStallAction.Log));
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.Log, true, true, true, true), Is.EqualTo(PullStallAction.None),
                "one log line per stall");
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.End, true, true, true, true), Is.EqualTo(PullStallAction.End));
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.End, false, true, false, true), Is.EqualTo(PullStallAction.Log),
                "with the end off, the stall is still logged");
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.End, true, true, false, true), Is.EqualTo(PullStallAction.None));
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.Log, false, false, true, true), Is.EqualTo(PullStallAction.None),
                "with the log off, nothing at 6 minutes");
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.End, false, false, true, true), Is.EqualTo(PullStallAction.End),
                "with the log off, the end still comes");
            Assert.That(AutonomousPartyPullStall.Decide(PullStallAction.None, false, true, true, true), Is.EqualTo(PullStallAction.None));
        });

        [Test]
        public void OnlyAutonomousPveGamebotPartiesAreWatched() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, false, 4, false, false, false), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(false, false, 4, false, false, false), Is.False, "RvR warbands");
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, true, 4, false, false, false), Is.False, "already ending");
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, false, 0, false, false, false), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, false, 4, true, false, false), Is.False,
                "realm expeditions hold on their own staging orders");
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, false, 4, false, true, false), Is.False,
                "a party hosting a real player is never ended by it");
            Assert.That(AutonomousBotGroupCoordinator.WatchesPullStall(true, false, 4, false, false, true), Is.False,
                "nor a party formed from a player's group");
        });

        [Test]
        public void TheLogNamesTheMemberThatHoldsThePull()
        {
            string stall = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.PullStall.cs");
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPartyPullStall.ResourceGate(85, 40, 100, true), Is.EqualTo("85/40/100"));
                Assert.That(AutonomousPartyPullStall.ResourceGate(50, 0, 100, false), Is.EqualTo("50/-/100"));
                Assert.That(AutonomousPartyPullStall.ResourceGate(95, 90, 90, true), Is.Null);
                const int cohesion = 500;
                Assert.That(AutonomousPartyPullStall.PlacementGate(true, true, 10, cohesion), Is.EqualTo("riding"));
                Assert.That(AutonomousPartyPullStall.PlacementGate(false, false, int.MaxValue, cohesion), Is.EqualTo("region"));
                Assert.That(AutonomousPartyPullStall.PlacementGate(false, true, 900, cohesion), Is.EqualTo("cohesion 900"));
                Assert.That(AutonomousPartyPullStall.PlacementGate(false, true, 400, cohesion), Is.Null);
                // the row ships: bot names only under the keys the pipeline drops
                Assert.That(stall, Does.Contain("bot = puller?.Name ?? string.Empty,"));
                Assert.That(stall, Does.Contain("name = holder,"));
                Assert.That(stall, Does.Not.Contain("puller = puller"));
            });
        }

        [Test]
        public void BothSwitchesDefaultOnAndShipOn()
        {
            Assert.That(AutonomousPartyPullStall.StallLog, Is.True);
            Assert.That(AutonomousPartyPullStall.StallEnd, Is.True);
            string settings = Source("..", "..", "tools", "release-server-settings.json");
            Assert.That(settings, Does.Contain("\"bot_party_pull_stall_end\": \"True\""));
            Assert.That(settings, Does.Contain("\"bot_party_pull_stall_log\": \"True\""));
        }

        [Test]
        public void ThePullGateIsOneMethodAndTheBoolOverloadWrapsIt()
        {
            string coordinator = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            Assert.That(coordinator, Does.Contain(
                "public static bool CanInitiateNewPull(GameBot bot, bool corridorBlocker = false, bool raidDungeonTrash = false) =>\n" +
                "        CanInitiateNewPull(bot, corridorBlocker, raidDungeonTrash, out _);"));
            int start = coordinator.IndexOf("public static bool CanInitiateNewPull(GameBot bot, bool corridorBlocker, bool raidDungeonTrash, out string gate)");
            int end = coordinator.IndexOf("public static int PullCohesionRadius(", start);
            Assert.That(start, Is.GreaterThan(0));
            string body = coordinator[start..end];
            Assert.That(body, Does.Not.Contain("$\""), "gate names are constants: the pull path allocates no strings");
            // the gates in their old order
            string[] order =
            {
                "GateNoSession", "GateExpeditionHold", "GateExpeditionPuller", "GateNoLeader", "GateAssembly", "GateRegrouping",
                "GateNotPuller", "GateCasualty", "GateRecovering", "GateResources", "GateHostedPlayer", "GateComposition",
                "GateSize", "GatePlacement"
            };
            int at = 0;
            foreach (string name in order)
            {
                int next = body.IndexOf("AutonomousPartyPullStall." + name, at);
                Assert.That(next, Is.GreaterThan(at), name);
                at = next;
            }
        }

        [Test]
        public void TheMonitorRunsInEveryPartyPulseAfterTheCombatCheck()
        {
            string coordinator = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int start = coordinator.IndexOf("private static bool UpdateSession(Session session, GameBot[] members)");
            int combat = coordinator.IndexOf("bool groupCombatActive = AutonomousGroupCombat.AnyHoldsGroup(members, combatLeader", start);
            int monitor = coordinator.IndexOf("if (!ObservePartyPullStall(session, members, groupCombatActive))\n            return false;", start);
            int meetup = coordinator.IndexOf("if (session.Phase == \"Meeting up\"", combat);
            Assert.That(combat, Is.GreaterThan(start));
            Assert.That(monitor, Is.GreaterThan(combat));
            Assert.That(meetup, Is.GreaterThan(monitor));
            string stall = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.PullStall.cs");
            Assert.That(stall, Does.Contain("Log.Info(\"AUTONOMOUS_GROUP_PULL_STALL \" + JsonSerializer.Serialize(fields));"));
            Assert.That(stall, Does.Contain("Log.Warn(\"AUTONOMOUS_GROUP_PULL_STALL_END \" + JsonSerializer.Serialize(fields));"));
            Assert.That(stall, Does.Contain("FinishGroupTask(session, $\"Pull stalled {minutes} minutes at {campId}: {gate}\");"));
            Assert.That(GroupEndedLog.Classify("Pull stalled 11 minutes at camp-1: resources"), Is.EqualTo(("stalled", "pull")),
                "GROUP_ENDED gives the stall its own reason");
        }
    }
}
