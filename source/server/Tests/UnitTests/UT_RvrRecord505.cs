using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The RvR record (issue #505): logging only. The record's files draw no random number and write no game state; the
    /// pure parts of each row (fight ledger and outcome, siege watcher, the player's contact memory, teleport sight,
    /// force clusters, the decision trace's change-only memory and rule codes, the counted budget). Tests only: nothing
    /// here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrRecord505
    {
        private static string GameServerRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return Path.Combine(dir, "source", "server", "GameServer");
        }

        // ---- behaviour unchanged ---------------------------------------------------------------------------------------

        [Test]
        public void RecordFilesDrawNoRandomness()
        {
            string root = GameServerRoot();
            string[] files = Directory.GetFiles(Path.Combine(root, "bots", "autonomous"), "RvrRecord*.cs")
                .Append(Path.Combine(root, "commands", "playercommands", "RvrOddCommand.cs")).ToArray();
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(5));
            var random = new Regex(@"\bRandom\b|NextDouble|Util\.(Random|Chance)|RandomNumberGenerator");
            // Game state a log row must never write: targets, aggro, paths, movement, state machines.
            var writes = new Regex(@"\.TargetObject\s*=[^=]|AddToAggroList|RemoveFromAggroList|SetCurrentState|PathTo\(|" +
                                   @"WalkTo\(|StopMoving|\.MoveTo\(|MoveInRegion\(|SetProperty\(|RemoveProperty\(");
            foreach (string file in files)
            {
                string[] code = File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//")).ToArray();
                Assert.Multiple(() =>
                {
                    foreach (string line in code)
                    {
                        if (line.TrimStart().StartsWith("///"))
                            continue;
                        Assert.That(random.IsMatch(line), Is.False, $"{Path.GetFileName(file)}: {line.Trim()}");
                        Assert.That(writes.IsMatch(line), Is.False, $"{Path.GetFileName(file)}: {line.Trim()}");
                    }
                });
            }
        }

        [Test]
        public void The_hooks_marked_505_only_log()
        {
            // Every line a #505 hook added outside the record's files calls the record or builds a log string.
            string root = GameServerRoot();
            var hookCalls = new Regex(@"RvrDecide\.|RvrFights\.|RvrPlayerRecord\.|RvrRecordRows\.|RvrTeleports\.|RvrRecordPulse\.|" +
                                      @"RvrSieges\.|_rvrRuleHint|_rvrLastStatusRule");
            int hooks = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(root, "bots"), "*.cs", SearchOption.AllDirectories)
                         .Where(f => !Path.GetFileName(f).StartsWith("RvrRecord", StringComparison.Ordinal)))
                foreach (string line in File.ReadAllLines(file))
                    if (hookCalls.IsMatch(line))
                    {
                        hooks++;
                        Assert.That(line, Does.Not.Match(@"\bRandom\b|NextDouble"), $"{Path.GetFileName(file)}: {line.Trim()}");
                    }
            Assert.That(hooks, Is.GreaterThanOrEqualTo(20), "the hooks are in place");
        }

        // ---- RVR_DECIDE ---------------------------------------------------------------------------------------------------

        [Test]
        public void Decisions_are_change_only()
        {
            var memory = new RvrDecisionMemory();
            Assert.That(memory.Change(1, "intent", "Traveling", out string from), Is.True);
            Assert.That(from, Is.EqualTo("-"));
            Assert.That(memory.Change(1, "intent", "Traveling", out _), Is.False, "the same twice writes nothing");
            Assert.That(memory.Change(1, "target", "Traveling", out _), Is.True, "another what is its own");
            Assert.That(memory.Change(1, "intent", "Fighting", out from), Is.True);
            Assert.That(from, Is.EqualTo("Traveling"));
            Assert.That(memory.Last(1, "intent"), Is.EqualTo("Fighting"));
        }

        [Test]
        public void The_row_and_its_rule_codes()
        {
            string row = RvrDecide.Format(5, "Bot One", 9, eRealm.Albion, "rvr-9", "controller", "intent", "-", "Traveling to keep",
                "exec.ExecuteRvrTurn.traveling.to", "Stage safely");
            Assert.That(row, Is.EqualTo("RVR_DECIDE t=5 bot=\"Bot One\" id=9 realm=Albion force=rvr-9 layer=controller what=intent " +
                                        "from=- to=\"Traveling to keep\" rule=exec.ExecuteRvrTurn.traveling.to why=\"Stage safely\""));
            Assert.That(AutonomousWorldBotController.StatusRule("ExecuteRvrTurn", "Traveling to Castle Sauvage"),
                Is.EqualTo("exec.ExecuteRvrTurn.traveling.to"));
            Assert.That(AutonomousWorldBotController.StatusRule("TryKeepTravel", "Keep route stalled"),
                Is.EqualTo("exec.TryKeepTravel.keep.route"));
            foreach (string rule in new[]
                     {
                         AutonomousWorldBotController.StatusRule("ExecuteRvrTurn", "Engaging keep attackers"), "scan.noticed",
                         "flee.soloBail", "roam.override", "chat.promise", "brain.called", "plan.assaultKeep",
                     })
                Assert.That(RvrDecide.IsRuleCode(rule), Is.True, rule);
            Assert.That(RvrDecide.IsRuleCode("Exec.x"), Is.False);
            Assert.That(RvrDecide.IsRuleCode("exec"), Is.False);
            Assert.That(RvrDecide.IsRuleCode("exec.has space"), Is.False);
        }

        [Test]
        public void The_budget_counts_what_it_drops()
        {
            RvrRecordBudget.Reset();
            int written = Enumerable.Range(0, 10).Count(_ => RvrRecordBudget.Take("T", 60_000, 4));
            Assert.That(written, Is.EqualTo(4));
            Assert.That(RvrRecordBudget.Take("T", 120_000, 4), Is.True, "a new minute");
            Assert.That(RvrRecordBudget.TakeDropped(), Is.EqualTo("T:6"));
            Assert.That(RvrRecordBudget.TakeDropped(), Is.EqualTo("0"));
        }

        // ---- the fight ledger -------------------------------------------------------------------------------------------

        private static RvrFightActor A(long key, string group) => new(key, group, eRealm.Albion, false);
        private static RvrFightActor M(long key, string group) => new(key, group, eRealm.Midgard, false);

        [Test]
        public void A_fight_opens_joins_and_ends_with_its_outcome()
        {
            var ledger = new RvrFightLedger();
            long id = ledger.Touch(1, 1000, 1000, 0, "engage", A(1, "a"), M(2, "m"), 500, out string open);
            Assert.That(id, Is.EqualTo(1));
            Assert.That(open, Does.StartWith("RVR_FIGHT_OPEN fight=1 region=1 x=1000 y=1000 by=engage realms=Albion,Midgard"));
            Assert.That(ledger.Touch(1, 2500, 1000, 5_000, "board", A(3, "a2"), M(4, "m"), -1, out open), Is.EqualTo(1),
                "the group's fight, and within reach");
            Assert.That(open, Is.Null);
            ledger.CrowdControl("a", 6_000);
            Assert.That(ledger.Death(1, 1000, 1000, 10_000, M(2, "m"), A(1, "a"), 300, out _), Is.EqualTo(1));
            Assert.That(ledger.Touch(1, 9000, 9000, 11_000, "engage", A(5, "x"), M(6, "y"), -1, out _), Is.EqualTo(2),
                "far away: a second fight");
            Assert.That(ledger.IdOf("a", 12_000), Is.EqualTo(1));
            Assert.That(ledger.InView(1, 1000, 1000, 12_000, 6000), Is.EqualTo(new List<long> { 1 }));
            Assert.That(ledger.Sweep(20_000), Is.Empty, "still live");
            List<string> ended = ledger.Sweep(40_001);
            Assert.That(ended.Count, Is.EqualTo(1));
            Assert.That(ended[0], Does.Contain("fight=1 ").And.Contain("end=quiet").And.Contain("sizes=Albion:2,Midgard:2")
                .And.Contain("deaths=Albion:0,Midgard:1").And.Contain("firstDeathS=10").And.Contain("firstCcS=6")
                .And.Contain("winner=Albion").And.Contain("held=Albion").And.Contain("playerNearest=300"));
            Assert.That(ledger.IdOf("a", 40_001), Is.EqualTo(0));
            Assert.That(ledger.EndAll(41_000).Count, Is.EqualTo(1));
        }

        [Test]
        public void Same_realm_or_unknown_actors_open_nothing()
        {
            var ledger = new RvrFightLedger();
            Assert.That(ledger.Touch(1, 0, 0, 0, "engage", A(1, "a"), A(2, "b"), -1, out _), Is.EqualTo(0));
            Assert.That(ledger.Touch(1, 0, 0, 0, "engage", A(1, "a"), default, -1, out _), Is.EqualTo(0));
            Assert.That(ledger.OpenCount, Is.EqualTo(0));
        }

        [Test]
        public void Outcome_by_deaths_and_who_held_the_field()
        {
            var sizes = new Dictionary<eRealm, int> { [eRealm.Albion] = 3, [eRealm.Midgard] = 2 };
            Assert.That(RvrFightLedger.Outcome(sizes, new Dictionary<eRealm, int>(), eRealm.Albion), Is.EqualTo(("none", "Albion")));
            Assert.That(RvrFightLedger.Outcome(sizes, new Dictionary<eRealm, int> { [eRealm.Albion] = 1, [eRealm.Midgard] = 1 },
                eRealm.Midgard), Is.EqualTo(("even", "Midgard")));
            Assert.That(RvrFightLedger.Outcome(sizes, new Dictionary<eRealm, int> { [eRealm.Albion] = 1, [eRealm.Midgard] = 2 },
                eRealm.Midgard), Is.EqualTo(("Albion", "Albion")), "the last to act was wiped: the survivors held");
        }

        // ---- sieges ---------------------------------------------------------------------------------------------------------

        private static RvrSieges.Sample Keep(int doorsUp = 2, int doorHealth = 100, int lord = 100, bool lordAlive = true,
            int attackers = 8, int atDoor = 0, int engines = 0, eRealm holder = eRealm.Midgard) =>
            new(doorsUp, 2, doorHealth, lord, lordAlive, 10, 10, attackers, atDoor, 0, engines, holder);

        [Test]
        public void A_siege_begins_stalls_progresses_and_is_taken()
        {
            var state = new RvrSieges.State();
            Assert.That(RvrSieges.Step(state, Keep(attackers: 0), 0), Is.Empty, "quiet");
            Assert.That(RvrSieges.Step(state, Keep(), 10_000).Single(), Does.StartWith("RVR_SIEGE_BEGIN"));
            Assert.That(RvrSieges.Step(state, Keep(), 200_000), Is.Empty);
            string stall = RvrSieges.Step(state, Keep(), 310_000).Single();
            Assert.That(stall, Does.StartWith("RVR_SIEGE_STALL minute=5 stalledMinutes=5 why=noAttackersAtDoor"));
            Assert.That(RvrSieges.Step(state, Keep(), 400_000), Is.Empty, "a stall row every five minutes, not every sample");
            Assert.That(RvrSieges.Step(state, Keep(atDoor: 3, engines: 1, doorHealth: 60), 410_000), Is.Empty, "door damage is progress");
            Assert.That(RvrSieges.Step(state, Keep(doorsUp: 1, doorHealth: 0, atDoor: 3), 420_000).Single(), Does.StartWith("RVR_SIEGE_DOOR"));
            Assert.That(RvrSieges.Step(state, Keep(doorsUp: 1, doorHealth: 0, lord: 90), 430_000).Single(), Does.Contain("stage=hit"));
            Assert.That(RvrSieges.Step(state, Keep(doorsUp: 1, doorHealth: 0, lord: 40), 440_000).Single(), Does.Contain("stage=half"));
            List<string> end = RvrSieges.Step(state, Keep(doorsUp: 1, doorHealth: 0, lord: 0, lordAlive: false, holder: eRealm.Albion), 450_000);
            Assert.That(end, Has.Some.Contains("stage=dead"));
            Assert.That(end.Last(), Does.StartWith("RVR_SIEGE_END minute=7 result=taken from=Midgard stalls=1"));
        }

        [Test]
        public void A_siege_whose_attackers_leave_is_cleared()
        {
            var state = new RvrSieges.State();
            RvrSieges.Step(state, Keep(), 0);
            Assert.That(RvrSieges.Step(state, Keep(attackers: 0), 60_000), Is.Empty);
            Assert.That(RvrSieges.Step(state, Keep(attackers: 0), 180_000).Single(), Does.StartWith("RVR_SIEGE_END minute=3 result=cleared"));
        }

        [Test]
        public void Stall_reasons()
        {
            Assert.That(RvrSieges.StallReason(Keep(atDoor: 2)), Is.EqualTo("doorHoldingNoEngine"));
            Assert.That(RvrSieges.StallReason(Keep(atDoor: 2, engines: 1)), Is.EqualTo("doorHolding"));
            Assert.That(RvrSieges.StallReason(Keep(doorsUp: 0)), Is.EqualTo("lordUnreachedGuardsUp"));
            Assert.That(RvrSieges.StallReason(Keep(doorsUp: 0, lord: 70)), Is.EqualTo("lordHealing"));
        }

        // ---- the player -----------------------------------------------------------------------------------------------------

        [Test]
        public void A_group_notices_the_player_once_per_contact()
        {
            RvrPlayerRecord.Reset();
            Assert.That(RvrPlayerRecord.NoticeIsNew("g", "p", 0), Is.True);
            Assert.That(RvrPlayerRecord.NoticeIsNew("g", "p", 60_000), Is.False);
            Assert.That(RvrPlayerRecord.NoticeIsNew("h", "p", 60_000), Is.True, "another group");
            Assert.That(RvrPlayerRecord.NoticedAt("g", "p", 100_000), Is.EqualTo(0));
            Assert.That(RvrPlayerRecord.NoticeIsNew("g", "p", 60_000 + RvrPlayerRecord.ContactGapMilliseconds), Is.True,
                "a new contact after the gap");
            Assert.That(RvrPlayerRecord.NoticedLine(1, "P", "Looker", "g", eRealm.Hibernia, 1800, 2500, false, "player", 3, 253),
                Is.EqualTo("RVR_PLAYER_NOTICED t=1 player=P looker=Looker group=g realm=Hibernia distance=1800 " +
                           "time_since_first_los_ms=2500 called=false seen=player fight=3 region=253"));
            RvrPlayerRecord.Reset();
        }

        [Test]
        public void View_bands_and_the_seat_row()
        {
            Assert.That(new[] { 0, 1, 2, 3, 4, 7, 8, 16, 40 }.Select(RvrPlayerRecord.Bucket), Is.EqualTo(new[] { 0, 1, 2, 2, 3, 3, 4, 5, 5 }));
            Assert.That(RvrPlayerRecord.SeatLine(1, "P", 50, 12, 4, 3, 1, 1, 0), Does.StartWith(
                "RVR_PLAYER_SEAT t=1 player=P minutesInWar=50 minutesEnemyWithin3000=12 mostEnemiesWithin3000=4 timesAttacked=3"));
            Assert.That(RvrOdd.Line(1, 2, "P", eRealm.Albion, 1, 5, 6, 3, "rvr-keep-50", true, "they walked past"),
                Is.EqualTo("RVR_ODD seq=1 t=2 player=P realm=Albion region=1 at=5,6 inWar=true fight=3 siege=rvr-keep-50 " +
                           "note=\"they walked past\""));
        }

        // ---- teleports and clusters -----------------------------------------------------------------------------------------

        [Test]
        public void Whether_the_player_saw_a_snap()
        {
            Assert.That(RvrTeleports.Seen(-1, -1), Is.EqualTo("none"));
            Assert.That(RvrTeleports.Seen(500, 9000), Is.EqualTo("from"));
            Assert.That(RvrTeleports.Seen(9000, 500), Is.EqualTo("to"));
            Assert.That(RvrTeleports.Seen(100, 200), Is.EqualTo("both"));
            Assert.That(RvrTeleports.Line(1, "B", 2, eRealm.Albion, "moveTo", "AutonomousStuckWatchdog.Rescue", 1, 0, 0, 1, 900, 0, 900,
                    100, 1000, true, false),
                Does.Contain("by=AutonomousStuckWatchdog.Rescue from=1:0,0 to=1:900,0 distance=900 playerFrom=100 playerTo=1000 playerSaw=both"));
        }

        [Test]
        public void Force_clusters()
        {
            var parties = new List<RvrRecordPulse.ClusterParty>
            {
                new(1, 0, 0, 8), new(1, 2000, 0, 8), new(1, 4000, 0, 8), new(1, 20000, 0, 2), new(100, 0, 0, 8),
            };
            Assert.That(RvrRecordPulse.Clusters(parties, 3000), Is.EqualTo(new List<int> { 24, 8, 2 }), "chained within 3,000");
            Assert.That(RvrRecordPulse.PartyKind(1), Is.EqualTo("solo"));
            Assert.That(RvrRecordPulse.PartyKind(5), Is.EqualTo("small"));
            Assert.That(RvrRecordPulse.PartyKind(8), Is.EqualTo("eight"));
        }

        [Test]
        public void The_plan_row()
        {
            Assert.That(RvrRecordRows.PlanLine(1, "B", 2, eRealm.Midgard, "rvr-keep-5", "AssaultKeep", "plan.assaultKeep", 30, 4, "best"),
                Is.EqualTo("RVR_PLAN t=1 bot=B id=2 realm=Midgard chosen=rvr-keep-5 intent=AssaultKeep rule=plan.assaultKeep " +
                           "choices=30 rejected=4 reason=best"));
        }
    }
}
