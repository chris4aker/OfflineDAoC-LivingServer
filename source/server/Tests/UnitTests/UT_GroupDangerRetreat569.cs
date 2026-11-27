using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using Death = DOL.GS.AutonomousGroupDangerRetreat.Death;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 (#569 step 2): a gamebot party that keeps losing members to monsters far above it leaves the place. The rule
    /// tests are ported from zomboden/OfflineDAoC-True165 fb7487a (UT_GroupDangerRetreatAndCorpseWalk, its item 16; the
    /// corpse-walk half is not ported: Living Server has no party corpse walk); the wiring tests are Living Server's.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_GroupDangerRetreat569
    {
        private const long Minute = 60_000;

        [SetUp]
        public void Reset() => AutonomousGroupDangerRetreat.ResetForTests();

        private static Death D(long bot, int x, int y, int botLevel, int killerLevel, long tick, ushort region = 200) =>
            new(bot, region, x, y, botLevel, killerLevel, tick);

        [Test]
        public void TheOvernightGlimmerLoopEndsOnTheSecondMember()
        {
            // Dairininlan (13) to a level-43 glimmer ward, then Saoeaea (12) to a level-45 glimmer ardent 3.5 minutes later
            List<Death> deaths = [D(186174, 367787, 653381, 13, 43, 0)];
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(deaths, 0, false, out _), Is.False);
            deaths.Add(D(185498, 367502, 654270, 12, 45, 3 * Minute + 21_000));
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(deaths, 3 * Minute + 21_000, false, out Death anchor), Is.True);
            Assert.That(anchor.KillerLevel, Is.EqualTo(45));
            int band = AutonomousGroupDangerRetreat.BlockedBand(14,
                AutonomousGroupDangerRetreat.HighestKiller(deaths, anchor, 3 * Minute + 21_000));
            Assert.That(band, Is.EqualTo(41));
        }

        [TestCase(43, 13, 14, 2, ExpectedResult = true)]    // glimmer ward vs the overnight level-14 party
        [TestCase(24, 20, 20, 10, ExpectedResult = false)]  // an eight-man party pulls up to average + 10 on purpose
        [TestCase(34, 20, 20, 10, ExpectedResult = true)]
        [TestCase(18, 14, 16, 2, ExpectedResult = false)]   // the low member of a mixed party at an ordinary camp
        [TestCase(9, 5, 5, 1, ExpectedResult = false)]      // average + 3 is the planner's own floor
        [TestCase(12, 5, 5, 1, ExpectedResult = true)]
        public bool FarAboveMeansBeyondWhatThePartyPlans(int killer, int victim, int average, int bonus) =>
            AutonomousGroupDangerRetreat.IsFarAboveParty(killer, victim, average, bonus);

        [Test]
        public void OneMemberDyingTwiceIsNotEnoughButThreeTimesIs()
        {
            List<Death> deaths = [D(1, 1000, 1000, 10, 20, 0), D(1, 1200, 1000, 10, 20, Minute)];
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(deaths, Minute, false, out _), Is.False);
            deaths.Add(D(1, 1100, 900, 10, 20, 2 * Minute));
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(deaths, 2 * Minute, false, out _), Is.True);
        }

        [Test]
        public void FairFightsDistantDeathsAndOldDeathsDoNotCount()
        {
            // killer only 3 above: an ordinary lost pull, handled by the wipe penalty
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(
                [D(1, 0, 0, 10, 13, 0), D(2, 0, 0, 10, 13, 0)], 0, false, out _), Is.False);
            // two places far apart
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(
                [D(1, 0, 0, 10, 20, 0), D(2, 20_000, 0, 10, 20, Minute)], Minute, false, out _), Is.False);
            // the first death left the ten-minute window
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat(
                [D(1, 0, 0, 10, 20, 0), D(2, 0, 0, 10, 20, 11 * Minute)], 11 * Minute, false, out _), Is.False);
        }

        [Test]
        public void AWipeWithAFarAboveKillerRetreatsAtOnce()
        {
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat([D(1, 0, 0, 8, 12, 0)], 0, true, out _), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.ShouldRetreat([D(1, 0, 0, 8, 10, 0)], 0, true, out _), Is.False);
        }

        [Test]
        public void TheCampIsBenchedForTheBandOnlyAndForTwoHours()
        {
            const long now = 1_000_000;
            AutonomousGroupDangerRetreat.BenchCamp("irewood sapling", 41, now);
            Assert.That(AutonomousGroupDangerRetreat.IsCampBenchedFor("irewood sapling", 15, now + Minute), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.IsCampBenchedFor("irewood sapling", 41, now + Minute), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.IsCampBenchedFor("irewood sapling", 42, now + Minute), Is.False,
                "a higher-level party is never blocked");
            Assert.That(AutonomousGroupDangerRetreat.IsCampBenchedFor("irewood sapling", 15,
                now + AutonomousGroupDangerRetreat.BenchMilliseconds), Is.False);
            Assert.That(AutonomousGroupDangerRetreat.IsCampBenchedFor("other camp", 15, now + Minute), Is.False);
        }

        [Test]
        public void PartyCampsBesideADangerSpotAreSkippedByTheBand()
        {
            const long now = 5_000_000;
            AutonomousGroupDangerRetreat.MarkSpot(200, 367787, 653381, 41, now);
            Assert.That(AutonomousGroupDangerRetreat.AllowsPartyCamp("near", 200, 369758, 651032, 15, now), Is.False);
            Assert.That(AutonomousGroupDangerRetreat.AllowsPartyCamp("near", 200, 369758, 651032, 45, now), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.AllowsPartyCamp("far", 200, 355452, 576268, 15, now), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.AllowsPartyCamp("near", 201, 369758, 651032, 15, now), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.AllowsPartyCamp("near", 200, 369758, 651032, 15,
                now + AutonomousGroupDangerRetreat.BenchMilliseconds), Is.True);
        }

        [Test]
        public void RepeatedSpotsMergeAndKeepTheHighestBand()
        {
            AutonomousGroupDangerRetreat.MarkSpot(200, 0, 0, 20, 0);
            AutonomousGroupDangerRetreat.MarkSpot(200, 1000, 0, 30, Minute);
            Assert.That(AutonomousGroupDangerRetreat.NearSpot(200, 0, 0, 30, 2 * Minute), Is.True);
            Assert.That(AutonomousGroupDangerRetreat.NearSpot(200, 0, 0, 31, 2 * Minute), Is.False);
        }

        [Test]
        public void PruneKeepsTheListShortAndFresh()
        {
            List<Death> deaths = [];
            for (int i = 0; i < 30; i++)
                deaths.Add(D(i, 0, 0, 10, 20, i * 1000));
            AutonomousGroupDangerRetreat.Prune(deaths, 30_000);
            Assert.That(deaths.Count, Is.EqualTo(16));
            AutonomousGroupDangerRetreat.Prune(deaths, 30_000 + AutonomousGroupDangerRetreat.WindowMilliseconds);
            Assert.That(deaths, Is.Empty);
        }

        [Test]
        public void OnlyAutonomousPveGamebotPartiesCountTheDeaths() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, false, false, false, false), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(false, false, true, false, false, false, false), Is.False,
                "RvR warbands");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, true, true, false, false, false, false), Is.False,
                "already ending");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, false, false, false, false, false), Is.False,
                "deaths on the way to the first meetup are the meetup's business");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, true, false, false, false), Is.False,
                "realm expeditions");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, false, true, false, false), Is.False,
                "a party hosting a real player is never ended by it");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, false, false, true, false), Is.False,
                "nor a party formed from a player's group");
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, false, false, false, true), Is.False,
                "nor a party hunting a dungeon boss: six or more take a red boss on purpose");
        });

        [Test]
        public void ARedDungeonBossThatAPartyChoseCanMeetTheFarAboveFloor()
        {
            // Why the boss exemption is needed: a level 45 party of six (camp bonus 3) may take a level 53 boss, red to it
            // (AutonomousDungeonBossGoals.Qualifies), and that boss meets the far-above floor (45 + 3 + 4 = 52).
            Assert.That(AutonomousDungeonBossGoals.Qualifies(45, 6, 53, 0, true), Is.True, "red for a party of six");
            Assert.That(AutonomousDungeonBossGoals.Qualifies(45, 5, 53, 0, true), Is.False, "five take at most orange");
            Assert.That(AutonomousGroupDangerRetreat.IsFarAboveParty(53, 45, 45, 3), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.CountsPartyDangerDeath(true, false, true, false, false, false, true), Is.False);
        }

        [Test]
        public void TheRetreatHasItsOwnGroupEndedReason()
        {
            Assert.That(GroupEndedLog.Classify("Lost members to glimmer ardent (level 45) far above them; the place is avoided " +
                "by parties up to level 41"), Is.EqualTo(("danger", "far_above")));
            Assert.That(GroupEndedLog.Classify("anything", "danger/far_above"), Is.EqualTo(("danger", "far_above")));
        }

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void ADeathIsNotedAndThePartyRetreatsOnItsNextPulse()
        {
            string deaths = Source("bots", "autonomous", "AutonomousDeathDiagnostics.cs");
            int monster = deaths.IndexOf("if (string.Equals(KillerKind(killer), \"monster\", StringComparison.Ordinal))");
            int note = deaths.IndexOf("AutonomousBotGroupCoordinator.NoteMemberDeath(bot, partyKiller);");
            Assert.That(monster, Is.GreaterThan(0));
            Assert.That(note, Is.GreaterThan(monster), "only a monster's kill counts, never a player's or an enemy bot's");
            string retreat = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.DangerRetreat.cs");
            Assert.That(retreat, Does.Contain("session.DungeonBoss != null))"), "the boss hunt is passed to the rule");

            string coordinator = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int start = coordinator.IndexOf("private static bool UpdateSession(Session session, GameBot[] members)");
            int retreat = coordinator.IndexOf("if (raidView == null && TryRetreatFromDanger(session, members))\n            return false;", start);
            int expired = coordinator.IndexOf("if (session.TaskClock.HasExpired(GameLoop.GameLoopTime) &&", start);
            int regroup = coordinator.IndexOf("session.Phase = \"Regrouping\";", start);
            Assert.That(retreat, Is.GreaterThan(start));
            Assert.That(expired, Is.GreaterThan(retreat));
            Assert.That(regroup, Is.GreaterThan(retreat), "it is decided before the party regroups beside the killers");
        }

        [Test]
        public void PartyCampPlanningSkipsBenchedCampsAndDangerSpots()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            Assert.That(controller, Does.Contain(
                "(!sharedGroup || AutonomousGroupDangerRetreat.AllowsPartyCamp(cell.Id, cell.RegionId,\n" +
                "                                            cell.X, cell.Y, planningLevel, GameLoop.GameLoopTime)) &&"));
        }
    }
}
