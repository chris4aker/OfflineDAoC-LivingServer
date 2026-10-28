using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Bot-to-bot PvE matchmaking (2026-10-27 run: 153 parties formed, 790 waits ran out): patience that relaxes with the
    /// wait, mid-fight recruits, four-man parties for long waits, and the bonds bots keep with each other.
    /// </summary>
    [TestFixture]
    public class UT_GroupMatchmaking
    {
        private static long Minutes(double m) => (long)(m * 60_000);

        [Test]
        public void PatienceGrowsWithTheWait()
        {
            Assert.That(AutonomousGroupMatchmakingPatience.Stage(0), Is.EqualTo(0));
            Assert.That(AutonomousGroupMatchmakingPatience.Stage(Minutes(5.9)), Is.EqualTo(0));
            Assert.That(AutonomousGroupMatchmakingPatience.Stage(Minutes(6)), Is.EqualTo(1));
            Assert.That(AutonomousGroupMatchmakingPatience.Stage(Minutes(12)), Is.EqualTo(2));
            Assert.That(AutonomousGroupMatchmakingPatience.Stage(Minutes(19)), Is.EqualTo(2));
            Assert.That(AutonomousGroupMatchmakingPatience.EagerAfter, Is.LessThan(AutonomousObjectiveAssignments.GroupMatchmakingTimeout),
                "The most patient stage comes well before the 20-minute timeout.");
        }

        [Test]
        public void APatientBotWalksFurtherAndTakesASmallerParty()
        {
            Assert.That(AutonomousGroupMatchmakingPatience.TravelMinutes(0), Is.EqualTo(AutonomousBotGroupCoordinator.MaximumPveRecruitmentMinutes));
            Assert.That(AutonomousGroupMatchmakingPatience.TravelMinutes(1), Is.GreaterThan(AutonomousGroupMatchmakingPatience.TravelMinutes(0)));
            Assert.That(AutonomousGroupMatchmakingPatience.TravelMinutes(2), Is.GreaterThan(AutonomousGroupMatchmakingPatience.TravelMinutes(1)));
            Assert.That(AutonomousGroupMatchmakingPatience.MinimumPartySize(0), Is.EqualTo(AutonomousBotGroupCoordinator.MinimumPveFormationSize));
            Assert.That(AutonomousGroupMatchmakingPatience.MinimumPartySize(1), Is.EqualTo(AutonomousBotGroupCoordinator.MinimumPveFormationSize));
            Assert.That(AutonomousGroupMatchmakingPatience.MinimumPartySize(2), Is.EqualTo(AutonomousBotGroupCoordinator.SmallestPveFormationSize));
            Assert.That(AutonomousGroupMatchmakingPatience.PairStage(0, 2), Is.EqualTo(2), "a long-waiting recruit walks to a fresh leader");
            Assert.That(AutonomousGroupMatchmakingPatience.PairStage(2, 0), Is.EqualTo(2));
        }

        [Test]
        public void FormationTakesABotMidFightButNotOnAHorseOrACorpseRun()
        {
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, true, true, true, 3, midFightAllowed: true), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, true, false, false, 3), Is.False,
                "Without the formation flag the old rule stands.");
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(true, false, false, false, false, 3, midFightAllowed: true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, true, false, false, false, 3, midFightAllowed: true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, false, false, false, 13, 14, true), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, false, false, false, 13, 8, true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.CanRecruitPveMember(false, false, false, false, false, 9999, 14, true), Is.False, "no route");
        }

        [Test]
        public void AFourManNeedsATankAHealerAndALongWait()
        {
            var cls = new[] { eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Scout };
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Armsman, cls), Is.EqualTo(0),
                "Four bots never made a party before, and still don't for a fresh leader.");
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSizeAtLeast(4, eCharacterClass.Armsman, cls), Is.EqualTo(4));
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSizeAtLeast(4, eCharacterClass.Wizard,
                eCharacterClass.Theurgist, eCharacterClass.Scout, eCharacterClass.Cleric), Is.EqualTo(0), "no tank");
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSizeAtLeast(4, eCharacterClass.Hero,
                eCharacterClass.Eldritch, eCharacterClass.Ranger, eCharacterClass.Enchanter), Is.EqualTo(0), "no healer");
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSizeAtLeast(4, eCharacterClass.Thane,
                eCharacterClass.Healer, eCharacterClass.Runemaster, eCharacterClass.Hunter, eCharacterClass.Savage), Is.EqualTo(5),
                "Five is still preferred to four.");
        }

        [Test]
        public void GoodRunsMakeFriendsAndFlakesMakeGrudges()
        {
            Assert.That(AutonomousBotBonds.Score(0, 0, 0, 0), Is.EqualTo(0));
            Assert.That(AutonomousBotBonds.IsFriend(AutonomousBotBonds.Score(1, 1, 0, 0)), Is.False, "one good run is a start");
            Assert.That(AutonomousBotBonds.IsFriend(AutonomousBotBonds.Score(2, 2, 0, 0)), Is.True, "two good runs make a friend");
            Assert.That(AutonomousBotBonds.IsGrudge(AutonomousBotBonds.Score(0, 0, 0, 1)), Is.False, "one no-show can be a bad route");
            Assert.That(AutonomousBotBonds.IsGrudge(AutonomousBotBonds.Score(0, 0, 0, 2)), Is.True, "two is a habit");
            Assert.That(AutonomousBotBonds.IsGrudge(AutonomousBotBonds.Score(1, 0, 2, 0)), Is.False, "one rough night isn't a grudge");
            Assert.That(AutonomousBotBonds.IsFriend(AutonomousBotBonds.Score(20, 0, 0, 0)), Is.True, "grouping a lot counts, up to a cap");
            Assert.That(AutonomousBotBonds.Score(50, 0, 0, 0), Is.EqualTo(AutonomousBotBonds.Score(5, 0, 0, 0)));
            Assert.That(AutonomousBotBonds.IsGrudge(AutonomousBotBonds.Score(5, 3, 0, 1)), Is.False, "an old friend survives one flake");
        }

        [Test]
        public void APartyIsRememberedByHowItWent()
        {
            Assert.That(AutonomousBotBonds.OutcomeOf(false, false, 0), Is.EqualTo(AutonomousBotBonds.Outcome.NotStarted));
            Assert.That(AutonomousBotBonds.OutcomeOf(true, true, 0), Is.EqualTo(AutonomousBotBonds.Outcome.Good));
            Assert.That(AutonomousBotBonds.OutcomeOf(true, true, 1), Is.EqualTo(AutonomousBotBonds.Outcome.Good));
            Assert.That(AutonomousBotBonds.OutcomeOf(true, true, 2), Is.EqualTo(AutonomousBotBonds.Outcome.Rough));
            Assert.That(AutonomousBotBonds.OutcomeOf(true, false, 0), Is.EqualTo(AutonomousBotBonds.Outcome.Plain), "ended early");
        }

        [Test]
        public void LeadersPickFriendsFirstAndGrudgesWaitForALongWait()
        {
            Assert.That(AutonomousBotBonds.FriendRank((int?)null), Is.EqualTo(2));
            Assert.That(AutonomousBotBonds.FriendRank(1), Is.EqualTo(1));
            Assert.That(AutonomousBotBonds.FriendRank(AutonomousBotBonds.FriendScore), Is.EqualTo(0));
            Assert.That(AutonomousBotBonds.WontGroup((int?)null, null, 0), Is.False);
            Assert.That(AutonomousBotBonds.WontGroup(AutonomousBotBonds.GrudgeScore, null, 0), Is.True);
            Assert.That(AutonomousBotBonds.WontGroup(null, AutonomousBotBonds.GrudgeScore, 1), Is.True, "a grudge either way");
            Assert.That(AutonomousBotBonds.WontGroup(AutonomousBotBonds.GrudgeScore, null, 2), Is.False, "a long wait takes anyone");
        }

        [Test]
        public void ABotForgetsTheLeastMemorableFirst()
        {
            var rows = new[] { ("a", 0, "2026-10-01"), ("b", 6, "2026-09-01"), ("c", -5, "2026-09-02"), ("d", 0, "2026-10-02"), ("e", 1, "2026-08-01") };
            string[] forgotten = AutonomousBotBonds.ToForget(rows, row => row.Item2, row => row.Item3, 3).Select(row => row.Item1).ToArray();
            Assert.That(forgotten, Is.EqualTo(new[] { "a", "d" }), "lowest |score| first, the older of a tie before the newer");
            Assert.That(AutonomousBotBonds.ToForget(rows, row => row.Item2, row => row.Item3, 10), Is.Empty);
        }

        [Test]
        public void NonHealersQueueOnlyWhileHealersCanCarryThem()
        {
            Assert.That(AutonomousGroupSupply.RoomForNonHealer(0, 0), Is.True, "a queue can start before a healer comes");
            Assert.That(AutonomousGroupSupply.RoomForNonHealer(0, AutonomousGroupSupply.Slack), Is.False);
            Assert.That(AutonomousGroupSupply.RoomForNonHealer(2, 14), Is.True);
            Assert.That(AutonomousGroupSupply.RoomForNonHealer(2, 15), Is.False, "two healers carry ten others plus the slack");
            Assert.That(AutonomousGroupSupply.Bucket(50), Is.EqualTo(50));
            Assert.That(AutonomousGroupSupply.Bucket(49), Is.EqualTo(40));
            Assert.That(AutonomousGroupSupply.Bucket(7), Is.EqualTo(0));
            Assert.That(AutonomousGroupSupply.ShouldQueue(null), Is.True);
        }

        [Test]
        public void BondTalkRestsPerRealmAndTopic()
        {
            long now = 1_000_000_000_000;
            Assert.That(AutonomousBotBonds.TryReserveTalk(eRealm.Midgard, "flake", now), Is.True);
            Assert.That(AutonomousBotBonds.TryReserveTalk(eRealm.Midgard, "flake", now + 60_000), Is.False, "one flake grumble per realm per rest");
            Assert.That(AutonomousBotBonds.TryReserveTalk(eRealm.Midgard, "friends", now + 60_000), Is.True, "another topic");
            Assert.That(AutonomousBotBonds.TryReserveTalk(eRealm.Hibernia, "flake", now + 60_000), Is.True, "another realm");
            Assert.That(AutonomousBotBonds.TryReserveTalk(eRealm.Midgard, "flake",
                now + (long)AutonomousBotBonds.TalkRest.TotalMilliseconds), Is.True, "after the rest");
        }

        [Test]
        public void BondsLoadIntoAPerBotIndex()
        {
            AutonomousBotBonds.Load(new[]
            {
                new OfflineBotBondRecord { BotId = 900_001, OtherId = 900_002, Grouped = 3, Good = 2 },
                new OfflineBotBondRecord { BotId = 900_001, OtherId = 900_003, Grouped = 1 },
                new OfflineBotBondRecord { BotId = 900_002, OtherId = 900_001, Flaked = 2 },
            });
            try
            {
                Assert.That(AutonomousBotBonds.KnownCount(900_001), Is.EqualTo(2));
                Assert.That(AutonomousBotBonds.KnownCount(900_002), Is.EqualTo(1));
                Assert.That(AutonomousBotBonds.KnownCount(900_004), Is.EqualTo(0));
                Assert.That(AutonomousBotBonds.Friends(900_001).Select(row => row.OtherId), Is.EqualTo(new long[] { 900_002 }));
            }
            finally
            {
                AutonomousBotBonds.Load(Array.Empty<OfflineBotBondRecord>());
            }
        }
    }
}
