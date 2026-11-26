using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// #644 parts 2-3: RAID_FIGHT_SAMPLE (every 15 s of a live attempt, from the attempt's own raiders) and RAID_FUNNEL (at a
    /// raid's start and end). The formatters and counts are pure; the budgets are 4 a minute per attempt and 30 a minute overall
    /// through LivingRows.Take; no row carries a name. Tests only: not yet seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidFightSampleRows644 : DutyLeaseFixture
    {
        private readonly List<string> _rows = [];
        private long _oldTime;

        [SetUp]
        public void Capture()
        {
            _oldTime = GameLoop.GameLoopTime;
            LivingRowsTestKit.SetTime(5_000_000);
            RvrRecordBudget.Reset();
            RaidAttemptRows.ResetForTests();
            _rows.Clear();
            LivingRows.TestSink = row => { lock (_rows) _rows.Add(row); };
        }

        [TearDown]
        public void Release()
        {
            LivingRows.TestSink = null;
            RaidAttemptRows.ResetForTests();
            RvrRecordBudget.Reset();
            LivingRowsTestKit.SetTime(_oldTime);
        }

        private List<string> Rows(string tag)
        {
            lock (_rows)
                return _rows.Where(row => row.StartsWith(tag + " ", StringComparison.Ordinal)).ToList();
        }

        private static string F(string row, string name) => LivingRowsTestKit.Field(row, name);

        private static RaidFightSampleRows.Raider R(string role, int dist, bool combat = true, int mana = -1, bool alive = true,
            bool inRegion = true, bool mezzed = false, bool stunned = false) =>
            new() { Role = role, Dist = dist, InCombat = combat, Mana = mana, Alive = alive, InRegion = inRegion, Mezzed = mezzed, Stunned = stunned };

        // ---- the pure formatters -----------------------------------------------------------------------------------

        [Test]
        public void TheSampleRowHasItsFieldsInOrderAndDashesForTheUnknown()
        {
            var counts = new RaidFightSampleRows.Counts("4:12:2:30", "3:10:1:22", 61, 2, 640, 1, 3);
            string row = RaidFightSampleRows.FormatSample(new RaidFightSampleRows.SampleFacts("dungeon-albion-krondon.3", 45, 72, null, -1,
                counts, "tank"));
            Assert.That(row, Is.EqualTo("RAID_FIGHT_SAMPLE attempt=dungeon-albion-krondon.3 t=45 bossPct=72 phase=- adds=- alive=4:12:2:30 " +
                "engaged=3:10:1:22 healerPow=61 healersLow=2 onTank=1 tgtKind=tank medDist=640 mezzed=1 stunned=3"));
            Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                { "attempt", "t", "bossPct", "phase", "adds", "alive", "engaged", "healerPow", "healersLow", "onTank", "tgtKind", "medDist",
                  "mezzed", "stunned" }));
            string none = RaidFightSampleRows.FormatSample(new RaidFightSampleRows.SampleFacts("e.1", -3, -1, null, -1,
                new RaidFightSampleRows.Counts("0:0:0:0", "0:0:0:0", -1, 0, -1, 0, 0), "none"));
            Assert.Multiple(() =>
            {
                Assert.That(F(none, "t"), Is.EqualTo("0"));
                Assert.That(F(none, "bossPct"), Is.EqualTo("-"));
                Assert.That(F(none, "healerPow"), Is.EqualTo("-"), "no living healer: no mean");
                Assert.That(F(none, "medDist"), Is.EqualTo("-"));
                Assert.That(F(none, "onTank"), Is.EqualTo("-"), "no target: neither on the tank nor off it");
                Assert.That(F(none, "tgtKind"), Is.EqualTo("none"));
            });
        }

        [Test]
        public void TheRoleSplitCountsOnlyLivingRaidersInTheRegionAndEngagedMeansInCombatWithin1500()
        {
            RaidFightSampleRows.Raider[] raiders =
            [
                R("tank", 200), R("tank", 1_600),                        // a tank out of reach: alive, not engaged
                R("healer", 900, mana: 80), R("healer", 1_400, combat: false, mana: 5),   // not in combat: alive, not engaged
                R("buffer", 1_500, mana: 40),                            // 1,500 is within
                R("attacker", 300), R("attacker", 50, alive: false),     // dead: nowhere
                R("attacker", 100, inRegion: false),                     // walking back in another region: nowhere
                R("-", 700),                                             // no class: in no slot, still in medDist
            ];
            int[] scratch = new int[raiders.Length];
            RaidFightSampleRows.Counts c = RaidFightSampleRows.Count(raiders, raiders.Length, scratch);
            Assert.Multiple(() =>
            {
                Assert.That(c.Alive, Is.EqualTo("2:2:1:1"));
                Assert.That(c.Engaged, Is.EqualTo("1:1:1:1"));
                Assert.That(c.HealerPow, Is.EqualTo(43), "the two living healers' mean power: (80 + 5) / 2 rounded");
                Assert.That(c.HealersLow, Is.EqualTo(1), "under 10%");
                Assert.That(c.MedDist, Is.EqualTo(900), "200, 300, 700, 900, 1400, 1500, 1600: the middle");
            });
        }

        [Test]
        public void HealerPowerCountsHealersOnlyAndOnlyKnownPower()
        {
            RaidFightSampleRows.Raider[] raiders = [R("healer", 10, mana: 9), R("healer", 10, mana: -1), R("attacker", 10, mana: 0),
                R("buffer", 10, mana: 3)];
            RaidFightSampleRows.Counts c = RaidFightSampleRows.Count(raiders, raiders.Length, new int[4]);
            Assert.That((c.HealerPow, c.HealersLow), Is.EqualTo((9, 1)), "an attacker's or buffer's power is not the healers'");
            Assert.That(RaidFightSampleRows.Count([R("attacker", 10)], 1, new int[1]).HealerPow, Is.EqualTo(-1));
            Assert.That(RaidFightSampleRows.MeanPercent(201, 2), Is.EqualTo(101), "100.5 rounds away from zero");
        }

        [Test]
        public void TheMedianIsTheLowerMiddleAndCountsOnlyTheFilledPart()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidFightSampleRows.Median([30, 10, 20], 3), Is.EqualTo(20));
                Assert.That(RaidFightSampleRows.Median([40, 10, 30, 20], 4), Is.EqualTo(20));
                Assert.That(RaidFightSampleRows.Median([5, 1, 999, 999], 2), Is.EqualTo(1), "only the first two are this sample's");
                Assert.That(RaidFightSampleRows.Median([], 0), Is.EqualTo(-1));
                Assert.That(RaidFightSampleRows.Median(null, 3), Is.EqualTo(-1));
            });
        }

        [Test]
        public void MezzedAndStunnedCountLivingRaidersInTheRegion()
        {
            RaidFightSampleRows.Raider[] raiders = [R("attacker", 10, mezzed: true), R("healer", 10, mezzed: true, stunned: true),
                R("tank", 10, stunned: true, alive: false), R("tank", 10, mezzed: true, inRegion: false)];
            RaidFightSampleRows.Counts c = RaidFightSampleRows.Count(raiders, raiders.Length, new int[4]);
            Assert.That((c.Mezzed, c.Stunned), Is.EqualTo((2, 1)));
        }

        [Test]
        public void TheBossTargetKindAndOnTank()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidFightSampleRows.TargetKind(false, false, null, false), Is.EqualTo("none"));
                Assert.That(RaidFightSampleRows.TargetKind(true, true, "tank", false), Is.EqualTo("tank"));
                Assert.That(RaidFightSampleRows.TargetKind(true, true, "healer", false), Is.EqualTo("healer"));
                Assert.That(RaidFightSampleRows.TargetKind(true, true, "-", false), Is.EqualTo("raider"));
                Assert.That(RaidFightSampleRows.TargetKind(true, false, null, true), Is.EqualTo("pet"));
                Assert.That(RaidFightSampleRows.TargetKind(true, false, null, false), Is.EqualTo("other"));
                Assert.That(RaidFightSampleRows.OnTank("tank"), Is.EqualTo("1"));
                Assert.That(RaidFightSampleRows.OnTank("healer"), Is.EqualTo("0"));
                Assert.That(RaidFightSampleRows.OnTank("pet"), Is.EqualTo("0"), "a pet holding the boss is not the tank");
                Assert.That(RaidFightSampleRows.OnTank("other"), Is.EqualTo("0"));
                Assert.That(RaidFightSampleRows.OnTank("none"), Is.EqualTo("-"));
            });
        }

        // ---- the budgets and the cadence ------------------------------------------------------------------------------

        [Test]
        public void TheBudgetIsFourAMinutePerAttemptAndThirtyAMinuteOverall()
        {
            int[] taken = new int[10];
            for (int round = 0; round < 6; round++)
                for (int a = 0; a < taken.Length; a++)
                    if (RaidFightSampleRows.TakeBudget(RaidFightSampleRows.AttemptBudgetTag($"e{a}.1"))) taken[a]++;
            Assert.Multiple(() =>
            {
                Assert.That(taken.Max(), Is.LessThanOrEqualTo(4), "no attempt more than 4 in a minute");
                Assert.That(taken.Sum(), Is.EqualTo(30), "30 in a minute over all attempts");
                Assert.That(RvrRecordBudget.TakeDropped(), Does.Contain("RAID_FIGHT_SAMPLE:10").And.Contain("RAID_FIGHT_SAMPLE.e0.1:2"),
                    "the drops are counted under the budget that refused them");
            });
            LivingRowsTestKit.SetTime(5_000_000 + 60_000);
            Assert.That(RaidFightSampleRows.TakeBudget(RaidFightSampleRows.AttemptBudgetTag("e9.1")), Is.True, "the next minute");
        }

        [Test]
        public void ASampleIsDueAtThePullThenEvery15SecondsAndALateOneResetsTheClock()
        {
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            RaidAttempt attempt = tracker.Begin(null, 1, [], [], 100_000, -1);
            Assert.Multiple(() =>
            {
                Assert.That(attempt.SampleDue(100_000, 15_000), Is.True, "t=0");
                Assert.That(attempt.SampleDue(105_000, 15_000), Is.False);
                Assert.That(attempt.SampleDue(110_000, 15_000), Is.False);
                Assert.That(attempt.SampleDue(115_000, 15_000), Is.True, "t=15");
                Assert.That(attempt.SampleDue(130_000, 15_000), Is.True, "t=30");
                Assert.That(attempt.SampleDue(177_000, 15_000), Is.True, "a slow pulse: t=77");
                Assert.That(attempt.SampleDue(190_000, 15_000), Is.False, "the next is 15 s after the late one, not at the old slot");
                Assert.That(attempt.SampleDue(192_000, 15_000), Is.True);
            });
            tracker.End("wipe", 0, 0, 0, 0, 0, 0, 200_000);
            Assert.That(attempt.SampleDue(500_000, 15_000), Is.False, "a closed attempt has no samples");
        }

        [Test]
        public void ALiveAttemptWritesItsSamplesEvery15SecondsAndNoneAfterItEnds()
        {
            Bot[] raid = Enumerable.Range(0, 6).Select(_ => Gamebot()).ToArray();
            raid[5].Alive = false;
            raid[4].Region = 2;
            var tracker = new RaidAttemptTracker("dragon-albion", "dragon", 120);
            tracker.Begin(null, 1, raid, raid, 5_000_000, -1);
            for (long t = 0; t <= 60_000; t += RaidFightSampleRows.CheckMilliseconds)
            {
                LivingRowsTestKit.SetTime(5_000_000 + t);
                tracker.Sample(5_000_000 + t);
            }
            List<string> samples = Rows(RaidFightSampleRows.SampleMarker);
            Assert.Multiple(() =>
            {
                Assert.That(samples.Select(s => F(s, "t")), Is.EqualTo(new[] { "0", "15", "30", "45", "60" }));
                Assert.That(samples.Select(s => F(s, "attempt")).Distinct(), Is.EqualTo(new[] { "dragon-albion.1" }));
                Assert.That(F(samples[0], "bossPct"), Is.EqualTo("-"), "no boss object in this test");
                Assert.That(F(samples[0], "tgtKind"), Is.EqualTo("none"));
                Assert.That(F(samples[0], "phase"), Is.EqualTo("-"));
                Assert.That(F(samples[0], "adds"), Is.EqualTo("-"));
                foreach (string s in samples)
                    Assert.That(LivingRowsTestKit.Keys(s), Does.Not.Contain("name").And.Not.Contain("bot").And.Not.Contain("account"), s);
            });
            tracker.End("wipe", 6, 6, 0, 6, 50, 40, 5_070_000);
            LivingRowsTestKit.SetTime(5_090_000);
            tracker.Sample(5_090_000);
            Assert.That(Rows(RaidFightSampleRows.SampleMarker), Has.Count.EqualTo(5), "no sample once the attempt ended");
        }

        [Test]
        public void TheSampleBuffersAreTheAttemptsOwnAndGrowOnlyWhenRaidersJoin()
        {
            Bot[] raid = Enumerable.Range(0, 3).Select(_ => Gamebot()).ToArray();
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            RaidAttempt attempt = tracker.Begin(null, 1, raid, raid, 0, -1);
            var first = attempt.SampleBuffer(out GameLiving[] members, out int[] dists);
            var second = attempt.SampleBuffer(out GameLiving[] again, out int[] dists2);
            Assert.That(ReferenceEquals(first, second) && ReferenceEquals(members, again) && ReferenceEquals(dists, dists2), Is.True,
                "no allocation per sample");
            attempt.Join([Gamebot()]);
            attempt.SampleBuffer(out GameLiving[] grown, out int[] grownDists);
            Assert.That((grown.Length, grownDists.Length >= 4), Is.EqualTo((4, true)));
            tracker.End("wipe", 0, 0, 0, 0, 0, 0, 1_000);
        }

        // ---- the funnel ----------------------------------------------------------------------------------------------

        [Test]
        public void TheFunnelRowHasItsFieldsInOrder()
        {
            string row = RaidFightSampleRows.FormatFunnel(new RaidFightSampleRows.FunnelFacts("dungeon-albion-krondon", "end", 96, 112, 14, 90,
                88, 84, new RaidFunnel.Losses(3, 1, 2, 4, 1, 6), 612, 1_430, 95));
            Assert.That(row, Is.EqualTo("RAID_FUNNEL event=dungeon-albion-krondon stage=end asked=96 recruited=112 parties=14 atHub=90 " +
                "staged=88 atBoss=84 lost=route:3,travel:1,died:2,logout:4,released:1,other:6 hubS=612 stageS=1430 bossS=95"));
            Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                { "event", "stage", "asked", "recruited", "parties", "atHub", "staged", "atBoss", "lost", "hubS", "stageS", "bossS" }));
            string start = RaidFightSampleRows.FormatFunnel(new RaidFightSampleRows.FunnelFacts("e", "start", 64, 70, 9, 60, 58, -1,
                default, 300, 900, -1));
            Assert.That((F(start, "atBoss"), F(start, "bossS"), F(start, "lost")),
                Is.EqualTo(("-", "-", "route:0,travel:0,died:0,logout:0,released:0,other:0")));
        }

        [Test]
        public void TheFunnelCountsDistinctRaidersPerReason()
        {
            var funnel = new RaidFunnel();
            funnel.RouteFailed(1);
            funnel.RouteFailed(1);
            funnel.RouteFailed(2);
            funnel.TravelCancelled(3);
            funnel.TravelCancelled(3);
            funnel.PartyLeft([(4, true), (5, false), (6, false)]);
            funnel.PartyLeft([(4, true)]);
            Assert.That(funnel.Lost(2), Is.EqualTo(new RaidFunnel.Losses(2, 1, 0, 1, 2, 2)));
            Assert.That(funnel.Lost(-1).Released, Is.EqualTo(0));
        }

        [Test]
        public void ADeathOutsideAPullCountsOnceAndADeathInAPullNot()
        {
            var funnel = new RaidFunnel();
            funnel.ObserveLife(1, dead: true, inLiveAttempt: false);   // died on the way in
            funnel.ObserveLife(1, dead: true, inLiveAttempt: false);   // the same corpse at the next pulse
            funnel.ObserveLife(2, dead: true, inLiveAttempt: true);    // died in a pull: RAID_DEATH's
            funnel.ObserveLife(2, dead: true, inLiveAttempt: false);   // still dead after the attempt ended: not again
            funnel.ObserveLife(1, dead: false, inLiveAttempt: false);  // rezzed or released
            funnel.ObserveLife(1, dead: true, inLiveAttempt: false);   // died again between pulls: the same raider
            funnel.ObserveLife(3, dead: false, inLiveAttempt: false);
            Assert.That(funnel.Lost(0).Died, Is.EqualTo(1), "distinct raiders who died outside a pull");
        }

        [Test]
        public void TheFunnelKeepsTheMostPartiesAndTheMostAtTheHubBeforeDeparture()
        {
            var funnel = new RaidFunnel();
            funnel.Observe(8, 40, true);
            funnel.Observe(12, 70, true);
            funnel.Observe(10, 90, false);   // after the departure: not "at the hub"
            Assert.That((funnel.MostParties, funnel.MostAtHub), Is.EqualTo((12, 70)));
        }

        [Test]
        public void TheFunnelLegsInSeconds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidFightSampleRows.Legs(1_000, 601_000, 1_501_000, 1_596_000, 2_000_000), Is.EqualTo((600L, 900L, 95L)));
                Assert.That(RaidFightSampleRows.Legs(1_000, 601_000, 0, -1, 901_000), Is.EqualTo((600L, 300L, -1L)),
                    "never started: staging until now, no boss leg");
                Assert.That(RaidFightSampleRows.Legs(1_000, 0, 0, -1, 901_000), Is.EqualTo((-1L, -1L, -1L)), "never left the hub");
                Assert.That(RaidFightSampleRows.Legs(1_000, 601_000, 1_501_000, -1, 2_000_000).BossS, Is.EqualTo(-1L), "no pull yet");
            });
        }

        [Test]
        public void ATrackerKnowsItsFirstPullAndItsLargestAttempt()
        {
            Bot[] raid = Enumerable.Range(0, 5).Select(_ => Gamebot()).ToArray();
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            Assert.That((tracker.FirstBegin, tracker.MostMembers), Is.EqualTo((-1L, 0)));
            tracker.Begin(null, 1, raid, raid, 10_000, -1);
            Assert.That(tracker.MostMembers, Is.EqualTo(5), "the live attempt counts");
            tracker.End("wipe", 5, 5, 5, 0, 30, 20, 50_000);
            tracker.Begin(null, 1, raid.Take(2), raid, 90_000, -1);
            Assert.That((tracker.FirstBegin, tracker.MostMembers), Is.EqualTo((10_000L, 5)));
            tracker.End("killed", 2, 0, 0, 0, 0, 0, 120_000);
        }
    }
}
