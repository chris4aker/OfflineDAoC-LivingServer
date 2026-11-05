using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-10-19: raids "mostly successful... high stakes but not a guarantee", about 2 in 3, and "if it keeps
    /// winning easily, it needs fewer bots next time, so the stakes stay up". Bosses are never weakened: the lever is the
    /// raid's own size (RealmRaidRecruitmentPolicy.Size) beside the Epic Special Forces' help after a wipe.
    /// </summary>
    [TestFixture]
    public class UT_RealmRaidWinBalance
    {
        private const string Win = "Boss defeated", Loss = "Defeated", TimedOut = "Timed out";
        private static readonly RealmRaidRecruitmentPolicy.SizingRules Rules = RealmRaidRecruitmentPolicy.SizingRules.Default;

        [SetUp]
        public void Reset()
        {
            AutonomousEpicSpecialForces.ResetForTests();
            SiegeDefenseBalance.Reset();
        }

        [Test]
        public void TheDefaultsAreTwoInThreeBetweenSeventyAndAHundredAndFiftyPercent()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.TargetWinPercentSetting, Is.EqualTo(67));
                Assert.That(RealmRaidRecruitmentPolicy.SmallestSizePercentSetting, Is.EqualTo(70));
                Assert.That(RealmRaidRecruitmentPolicy.LargestSizePercentSetting, Is.EqualTo(150));
                Assert.That(RealmRaidRecruitmentPolicy.WinTrimPercentSetting, Is.EqualTo(5));
                Assert.That(RealmRaidRecruitmentPolicy.RulesFromSettings(), Is.EqualTo(Rules));
                Assert.That(Rules.LossAddPercent, Is.EqualTo(5 * 67 / 33.0).Within(1e-9), "a loss adds about twice what a win trims");
            });
        }

        // The period sizes (docs/RAID-SIZES.md, 2026-11-04): the comfortable 2002-2004 raid, not the minimum.
        [TestCase("dragon-albion", 60)]
        [TestCase("dragon-midgard", 60)]
        [TestCase("dragon-hibernia", 60)]
        [TestCase("epic-albion", 100)]
        [TestCase("epic-midgard", 100)]
        [TestCase("epic-hibernia", 100)]
        [TestCase("summoners-albion", 64)]
        [TestCase("darkness-hibernia", 64)]
        [TestCase("dungeon-albion-krondon", 64)]
        [TestCase("dungeon-albion-avalon", 64)]
        [TestCase("dungeon-albion-crystal", 120)]
        [TestCase("dungeon-albion-corrupt", 48)]
        [TestCase("dungeon-midgard-trollheim", 64)]
        [TestCase("dungeon-midgard-iarnvidiur", 64)]
        [TestCase("dungeon-midgard-dodens", 40)]
        [TestCase("dungeon-hibernia-tursuil", 40)]
        [TestCase("dungeon-hibernia-marfach", 64)]
        public void EachRaidsFullSizeIsItsPeriodSize(string id, int size)
        {
            var definition = AutonomousRealmRaid.Definitions.Single(d => d.Id == id);
            Assert.Multiple(() =>
            {
                Assert.That(definition.SuggestedBots, Is.EqualTo(size));
                // A big roster (600 per realm on the owner's copy) gives the period size; a small one never under 40.
                Assert.That(RealmRaidRecruitmentPolicy.RequiredBots(300, size, 0), Is.EqualTo(size));
                Assert.That(RealmRaidRecruitmentPolicy.RequiredBots(60, size, 0), Is.EqualTo(40));
            });
        }

        [Test]
        public void NoRecordIsThePeriodSize()
        {
            var sizing = RealmRaidRecruitmentPolicy.Size(100, [], Rules);
            Assert.Multiple(() =>
            {
                Assert.That(sizing.Percent, Is.EqualTo(100));
                Assert.That(sizing.Required, Is.EqualTo(100));
                Assert.That(sizing.RecruitCap, Is.EqualTo(128), "a quarter above, in whole parties");
                Assert.That(RealmRaidRecruitmentPolicy.Size(60, [], Rules).RecruitCap, Is.EqualTo(RealmRaidRecruitmentPolicy.RecruitCap(60)));
                Assert.That(sizing.SpecialForcesOnWipe, Is.EqualTo(2), "two parties after a wipe for a raid of 100");
                Assert.That(RealmRaidRecruitmentPolicy.Size(60, [], Rules).SpecialForcesOnWipe, Is.EqualTo(1), "one for a dragon raid of 60");
                Assert.That(sizing.Why, Does.Contain("no wins or losses"));
            });
        }

        [Test]
        public void ARunOfWinsTrimsStepByStepDownToTheSmallestSize()
        {
            var sizes = Enumerable.Range(0, 12)
                .Select(n => RealmRaidRecruitmentPolicy.SizePercent(Enumerable.Repeat(Win, n), Rules)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(sizes.Take(7), Is.EqualTo(new double[] { 100, 95, 90, 85, 80, 75, 70 }));
                Assert.That(sizes.Skip(6), Is.All.EqualTo(70), "never under the smallest size");
                Assert.That(RealmRaidRecruitmentPolicy.Size(100, [Win], Rules).Required, Is.EqualTo(95));
                Assert.That(RealmRaidRecruitmentPolicy.Size(60, Enumerable.Repeat(Win, 6).ToArray(), Rules).Required, Is.EqualTo(42));
                Assert.That(RealmRaidRecruitmentPolicy.Size(48, Enumerable.Repeat(Win, 6).ToArray(), Rules).Required, Is.EqualTo(34));
                Assert.That(RealmRaidRecruitmentPolicy.Size(40, Enumerable.Repeat(Win, 6).ToArray(), Rules).Required, Is.EqualTo(28),
                    "70% of the smallest raid, above the three-party minimum");
            });
        }

        [Test]
        public void LossesGrowARaidUpToHalfAgainItsPeriodSize()
        {
            Assert.Multiple(() =>
            {
                double afterFour = RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win], Rules);
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, Loss], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, TimedOut], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9), "a time-out is a loss");
                // Bots that play worse than 2003 players: a raid that keeps losing grows above its period size.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Loss], Rules), Is.EqualTo(100 + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent(Enumerable.Repeat(Loss, 5), Rules), Is.EqualTo(150), "never above 150%");
                Assert.That(RealmRaidRecruitmentPolicy.Size(100, Enumerable.Repeat(Loss, 8).ToArray(), Rules).Required, Is.EqualTo(150));
                Assert.That(RealmRaidRecruitmentPolicy.Size(60, Enumerable.Repeat(Loss, 8).ToArray(), Rules).Required, Is.EqualTo(90));
                Assert.That(RealmRaidRecruitmentPolicy.Size(120, Enumerable.Repeat(Loss, 8).ToArray(), Rules).Required, Is.EqualTo(180));
                // Two wins and a loss, the target's own mix, leave the size about where it was.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Loss], Rules), Is.EqualTo(90 + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Loss], Rules), Is.InRange(99.9, 100.2));
            });
        }

        [TestCase("Interrupted")]
        [TestCase("Beaten to it")]
        [TestCase("Failed rally")]
        [TestCase("Route blocked")]
        [TestCase("Stopped by player")]
        [TestCase("Encounter unavailable")]
        [TestCase("Ended (unconfirmed)")]
        public void OnlyWinsAndLossesCount(string notAResult)
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, notAResult, notAResult, Win], Rules), Is.EqualTo(90));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([notAResult, notAResult], Rules), Is.EqualTo(100));
                // The record keeps only results, so restarts and failed rallies never push a real result out of it.
                RealmRaidRecord.SetForTests("epic-albion", Win, notAResult, Loss, notAResult);
                Assert.That(RealmRaidRecord.Get("epic-albion"), Is.EqualTo(new[] { Win, Loss }));
                RealmRaidRecord.Add("epic-albion", notAResult);
                Assert.That(RealmRaidRecord.Get("epic-albion"), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void TheRecruitCapFollowsTheSizeInWholeParties()
        {
            Assert.Multiple(() =>
            {
                var largest = RealmRaidRecruitmentPolicy.Size(100, Enumerable.Repeat(Loss, 8).ToArray(), Rules);
                Assert.That((largest.Required, largest.RecruitCap), Is.EqualTo((150, 192)));
                var smallest = RealmRaidRecruitmentPolicy.Size(100, Enumerable.Repeat(Win, 10).ToArray(), Rules);
                Assert.That((smallest.Required, smallest.RecruitCap), Is.EqualTo((70, 96)));
                var dragon = RealmRaidRecruitmentPolicy.Size(60, Enumerable.Repeat(Win, 10).ToArray(), Rules);
                Assert.That((dragon.Required, dragon.RecruitCap), Is.EqualTo((42, 56)));
                // A 200 raid set by realm_raid_minimum_bots still keeps its 300 cap and never goes past 300.
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, [Win], Rules).RecruitCap, Is.EqualTo(288), "95% of 300, not RecruitCap(190) = 240");
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, Enumerable.Repeat(Loss, 8).ToArray(), Rules).Required, Is.EqualTo(300));
                foreach (int full in new[] { 40, 48, 60, 64, 100, 120, 200 })
                for (int wins = 0; wins <= 8; wins++)
                for (int losses = 0; losses <= 6; losses++)
                {
                    string[] record = [.. Enumerable.Repeat(Loss, losses), .. Enumerable.Repeat(Win, wins)];
                    var sizing = RealmRaidRecruitmentPolicy.Size(full, record, Rules);
                    Assert.That(sizing.RecruitCap, Is.GreaterThanOrEqualTo(sizing.Required).And.LessThanOrEqualTo(300));
                    Assert.That(sizing.RecruitCap % 8 == 0 || sizing.RecruitCap == 300, Is.True, $"{full} after {losses} losses and {wins} wins");
                    Assert.That(sizing.Required, Is.InRange((int)Math.Round(full * 0.7), Math.Min(300, (int)Math.Round(full * 1.5))));
                    // The short start stays half of what the sized raid needs, never under three parties.
                    Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, (sizing.Required + 1) / 2, sizing.Required),
                        Is.EqualTo((sizing.Required + 1) / 2 >= RealmRaidRecruitmentPolicy.MinimumRaidBots));
                }
            });
        }

        [Test]
        public void ASmallRaidGoesInShortWithHalfAndAtLeastThreeParties()
        {
            const long patience = RealmRaidRecruitmentPolicy.HubPatienceMilliseconds;
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.MinimumRaidBots, Is.EqualTo(24));
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 30, 60), Is.True, "a dragon raid: half of 60");
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 29, 60), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 24, 42), Is.True, "trimmed to 42: three parties, not 40");
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 23, 42), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 50, 100), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(true, 60, 60), Is.False, "forced raids never go in short");
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 30, patience, 60), Is.True, "the hub leaves with half too");
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 29, patience, 60), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.LateStartMinimum(60), Is.EqualTo(42), "the late start keeps its 70%");
            });
        }

        [Test]
        public void TheSpecialForcesScaleWithTheRaid()
        {
            Assert.Multiple(() =>
            {
                // One party per 64 raiders, rounded, one to three: about the 12% that 24 were of a 200 raid.
                foreach (var (size, parties) in new[] { (0, 3), (28, 1), (42, 1), (60, 1), (95, 1), (96, 2), (100, 2), (150, 2), (160, 3), (300, 3) })
                    Assert.That(AutonomousEpicSpecialForces.PartiesFor(size), Is.EqualTo(parties), $"a raid of {size}");
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([], 1), Is.EqualTo(1));
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([Loss, Loss, Loss], 1), Is.EqualTo(1), "never more than the raid's share");
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([Win], 1), Is.Zero);
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([Win, Loss], 2), Is.EqualTo(2));
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([], 2), Is.EqualTo(16));
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([]), Is.EqualTo(24), "no size given: the full guild");
            });
        }

        [Test]
        public void ARaidWithTheOwnersSizeOrNoSizingKeepsItsFullSize()
        {
            string[] wins = Enumerable.Repeat(Win, 6).ToArray();
            Assert.Multiple(() =>
            {
                var fixedSize = RealmRaidRecruitmentPolicy.Size(150, wins, Rules, fixedSize: true);
                Assert.That((fixedSize.Required, fixedSize.Percent), Is.EqualTo((150, 100.0)));
                Assert.That(fixedSize.Why, Does.Contain("realm_raid_minimum_bots"));
                var off = RealmRaidRecruitmentPolicy.Size(100, wins, Rules with { TargetWinPercent = 0 });
                Assert.That((off.Required, off.RecruitCap), Is.EqualTo((100, 128)));
                var loading = RealmRaidRecruitmentPolicy.Size(100, wins, Rules, recordLoaded: false);
                Assert.That(loading.Required, Is.EqualTo(100), "the ledger still loading: full size");
                Assert.That(loading.Why, Does.Contain("loading"));
            });
        }

        [Test]
        public void TheRulesAreMadeSafe()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new RealmRaidRecruitmentPolicy.SizingRules(99, 5, 500, 0).Normalized(),
                    Is.EqualTo(new RealmRaidRecruitmentPolicy.SizingRules(90, 25, 300, 1)));
                Assert.That(new RealmRaidRecruitmentPolicy.SizingRules(67, 80, 50, 5).Normalized().LargestPercent, Is.EqualTo(100),
                    "the largest is never under 100% (every raid starts there)");
                Assert.That(new RealmRaidRecruitmentPolicy.SizingRules(67, 120, 50, 5).Normalized(),
                    Is.EqualTo(new RealmRaidRecruitmentPolicy.SizingRules(67, 100, 100, 5)), "the smallest is never above 100%");
                Assert.That(new RealmRaidRecruitmentPolicy.SizingRules(-1, 60, 100, 5).Normalized().On, Is.False);
                var huge = RealmRaidRecruitmentPolicy.Size(120, Enumerable.Repeat(Loss, 30).ToArray(), Rules with { LargestPercent = 300 });
                Assert.That((huge.Required, huge.RecruitCap), Is.EqualTo((300, 300)), "never past the realm's 300");
            });
        }

        [Test]
        public void OneRecordGivesTheSizeAndTheSpecialForces()
        {
            // Caer Sidi won six times: 70 to stage, and nobody comes after a wipe.
            var winning = RealmRaidRecruitmentPolicy.Size(100, Enumerable.Repeat(Win, 6).ToArray(), Rules);
            // Then two losses in a row: bigger again, and the Special Forces come from the start (one party at 90).
            var losing = RealmRaidRecruitmentPolicy.Size(100, [.. Enumerable.Repeat(Win, 6), Loss, TimedOut], Rules);
            Assert.Multiple(() =>
            {
                Assert.That((winning.Required, winning.SpecialForcesOnWipe, winning.SpecialForcesFromStart, winning.Streak),
                    Is.EqualTo((70, 0, false, 6)));
                Assert.That(losing.Percent, Is.EqualTo(70 + 2 * Rules.LossAddPercent).Within(1e-9));
                Assert.That((losing.Required, losing.SpecialForcesOnWipe, losing.SpecialForcesFromStart, losing.Streak),
                    Is.EqualTo((90, 1, true, -2)));
                Assert.That(losing.Why, Does.Contain("6 won and 2 lost here, the last 2 lost"));
            });
        }

        [Test]
        public void TheSpecialForcesCountTheLastSixResultsNotTheLastSixRows()
        {
            // Before 2026-11-04 three restarts after two wins and a loss left only the loss in the last six rows.
            string[] rows = [Win, Win, Win, "Interrupted", "Interrupted", "Interrupted", "Failed rally", Loss];
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousEpicSpecialForces.CallUpParties(rows), Is.EqualTo(1), "3 - 3 wins + 1 loss");
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([.. Enumerable.Repeat(Loss, 6), .. Enumerable.Repeat(Win, 6)]), Is.Zero,
                    "only the last six results");
            });
        }

        [Test]
        public void ARaidThatEndsWhileTheRecordLoadsIsKept()
        {
            RealmRaidRecord.UnloadForTests();
            Assert.That(RealmRaidRecord.Loaded, Is.False);
            AutonomousEpicSpecialForces.RaidEnded("dragon-albion", Win);
            Assert.That(RealmRaidRecord.Get("dragon-albion"), Is.Empty, "not loaded yet");
            RealmRaidRecord.ResetForTests();
            RealmRaidRecord.Add("dragon-albion", Win);
            RealmRaidRecord.Add("dragon-albion", Loss);
            Assert.That(RealmRaidRecord.Get("dragon-albion"), Is.EqualTo(new[] { Win, Loss }), "each encounter, dragons too, has its own record");
            Assert.That(RealmRaidRecord.Get("dragon-midgard"), Is.Empty);
        }

        [Test]
        public void ALauncherRaidsResultIsNotPartOfTheRecord()
        {
            // Owner 2026-11-04: a raid the player started from the launcher counts for neither the size nor the Special Forces.
            RealmRaidRecord.SetForTests("epic-hibernia", Win);
            AutonomousEpicSpecialForces.RaidEnded("epic-hibernia", Win, forced: true);
            AutonomousEpicSpecialForces.RaidEnded("epic-hibernia", Loss, forced: true);
            Assert.That(RealmRaidRecord.Get("epic-hibernia"), Is.EqualTo(new[] { Win }));
            AutonomousEpicSpecialForces.RaidEnded("epic-hibernia", Loss);
            Assert.That(RealmRaidRecord.Get("epic-hibernia"), Is.EqualTo(new[] { Win, Loss }), "an automatic raid's result still counts");
            // Nor while the record is still loading.
            RealmRaidRecord.UnloadForTests();
            RealmRaidRecord.Add("epic-hibernia", Win, forced: true);
            RealmRaidRecord.ResetForTests();
            Assert.That(RealmRaidRecord.Get("epic-hibernia"), Is.Empty);
        }

        [Test]
        public void TheLedgerLeavesOutLauncherRaidsAndCountsUnmarkedOldRowsAsAutomatic()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ut-raid-ledger-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "epic-albion", "Caer Sidi", "Epic dungeon", "Albion",
                        $"2026-11-04T10:{n:00}:00.0000000Z", $"2026-11-04T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 200, 180);
                RealmEventRecordStore.Save(path, [
                    Row(Win, "The Caer Sidi expedition defeated the final encounter."),       // an old row: no marker, automatic
                    Row(Win, RealmEventRecordStore.ForcedRaidPrefix + "The Caer Sidi expedition defeated the final encounter."),
                    Row(Loss, "Defeated at Apocalypse after 2 failed attempts."),
                    Row(Loss, RealmEventRecordStore.ForcedRaidPrefix + "Defeated at Apocalypse after 2 failed attempts."),
                    Row("Interrupted", "Server stopped before the event reached a final outcome."),
                ]);
                Assert.Multiple(() =>
                {
                    Assert.That(RealmEventRecordStore.Outcomes(path, "epic-albion", 10, automaticOnly: true),
                        Is.EqualTo(new[] { Win, Loss, "Interrupted" }));
                    Assert.That(RealmRaidRecord.CountedOf(RealmEventRecordStore.Outcomes(path, "epic-albion", 10, automaticOnly: true)),
                        Is.EqualTo(new[] { Win, Loss }));
                    Assert.That(RealmEventRecordStore.Outcomes(path, "epic-albion", 10), Has.Count.EqualTo(5), "the Herald's own read is unchanged");
                });
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void TheRecordKeepsItsLastFourHundredResults()
        {
            Assert.That(RealmRaidRecord.Depth, Is.EqualTo(400));
            RealmRaidRecord.SetForTests("epic-midgard", [.. Enumerable.Repeat(Loss, 50), .. Enumerable.Repeat(Win, RealmRaidRecord.Depth)]);
            Assert.That(RealmRaidRecord.Get("epic-midgard"), Has.Count.EqualTo(RealmRaidRecord.Depth).And.All.EqualTo(Win));
            RealmRaidRecord.Add("epic-midgard", Loss);
            Assert.That(RealmRaidRecord.Get("epic-midgard").Last(), Is.EqualTo(Loss));
            Assert.That(RealmRaidRecord.Get("epic-midgard"), Has.Count.EqualTo(RealmRaidRecord.Depth));
        }

        [Test]
        public void TheSizedLineSaysWhy()
        {
            var sizing = RealmRaidRecruitmentPolicy.Size(64, [Win, Win, Win], Rules);
            string line = RealmRaidRecruitmentPolicy.SizedLine("summoners-albion", "Albion", sizing);
            Assert.That(line, Is.EqualTo("REALM_RAID_SIZED event=summoners-albion realm=Albion full=64 size=85% required=54 recruitCap=72 " +
                "won=3 lost=0 specialForcesOnWipe=0 specialForcesFromStart=False " +
                "why=\"3 won and 0 lost here, the last 3 won: each win trims 5 points, each loss adds 10.2, aiming at 67% wins\""));
        }

        [Test]
        public void AnAutomaticRaidTakesPartiesOnlyUpToItsRecruitCap()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, 20, 180, 0, 8, recruitCap: 184), Does.Contain("188 of its 184"));
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, 20, 176, 0, 8, recruitCap: 184), Is.Null);
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, 20, 296, 0, 8, recruitCap: 400), Does.Contain("300"), "never past 300");
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(true, 20, 184, 0, 8, recruitCap: 184), Is.Null,
                    "the Special Forces come on top of the cap");
            });
        }

        // ---- The simulation: does the policy settle near 2 in 3? ----

        /// <summary>
        /// A simple model of a raid's chance to win by its size (as a share of its period size): a logistic curve. The
        /// default puts 2 in 3 at 85% of the period size (bots as good as 2003 players: Apocalypse "tough" at 80-90 of a
        /// comfortable 100), about 85% wins at the period size. The test cases move that point from 80% to 120% (bots
        /// that play worse). It is a model, not a measurement.
        /// </summary>
        private static double WinChance(double percent, double half = 75, double steep = 0.0693) =>
            1 / (1 + Math.Exp(-steep * (percent - half)));

        private static (double WinRate, double MeanPercent, List<double> Sizes) Simulate(Func<double, double> chance, int raids, int seed,
            double interruptedShare = 0)
        {
            var random = new Random(seed);
            var ledger = new List<string>();
            var sizes = new List<double>();
            int counted = 0, won = 0;
            double sum = 0;
            for (int raid = 0; raid < raids; raid++)
            {
                // As the server does it: the record is the ledger's last counted results, at most RealmRaidRecord.Depth.
                double percent = RealmRaidRecruitmentPolicy.Size(100, RealmRaidRecord.CountedOf(ledger), Rules).Percent;
                sizes.Add(percent);
                if (random.NextDouble() < interruptedShare) { ledger.Add("Interrupted"); continue; }
                bool win = random.NextDouble() < chance(percent);
                ledger.Add(win ? Win : random.Next(2) == 0 ? Loss : TimedOut);
                if (raid >= raids / 4) { counted++; won += win ? 1 : 0; sum += percent; } // after a settling-in quarter
            }
            return (won / (double)counted, sum / counted, sizes);
        }

        // seed, the model's coin-flip size and steepness, and the size it wins 2 in 3 at (% of the period size)
        [TestCase(1, 75, 0.0693, 85)]     // bots as good as 2003 players: the raid comes down below its period size
        [TestCase(2, 75, 0.0693, 85)]
        [TestCase(3, 75, 0.0693, 85)]
        [TestCase(4, 90, 0.0693, 100)]    // just right at the period size
        [TestCase(5, 110, 0.0693, 120)]   // bots that play worse: the raid grows above its period size
        [TestCase(6, 95, 0.15, 99.6)]     // a steep encounter: a little smaller and it falls apart
        [TestCase(7, 60, 0.03, 83.1)]     // a forgiving one: size matters less
        public void ARaidSettlesNearTwoInThree(int seed, double half, double steep, double twoInThreeAt)
        {
            // 2,000 raids, three in ten of them interrupted (not counted): the record fills past its 400 and slides.
            var (rate, mean, sizes) = Simulate(p => WinChance(p, half, steep), 2000, seed, interruptedShare: 0.3);
            TestContext.WriteLine($"seed {seed}: won {rate:P1} at {mean:0.#}% of the period size on average");
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.InRange(0.62, 0.72), "about two in three");
                // A flat curve settles a little above its 2-in-3 point (a loss adds twice what a win trims).
                Assert.That(mean, Is.InRange(twoInThreeAt - 7, twoInThreeAt + 7), "around the size the model wins 2 in 3 at");
                Assert.That(sizes, Is.All.InRange(70.0, 150.0));
                // Smooth: one raid to the next moves by one step, and once the 400-result record is full by at most one
                // more (the oldest result drops out of the replay: a new loss and a dropped old win is 10.2 + 5).
                Assert.That(sizes.Zip(sizes.Skip(1), (a, b) => Math.Abs(a - b)).Max(),
                    Is.LessThanOrEqualTo(Rules.LossAddPercent + Rules.WinTrimPercent + 1e-9));
                Assert.That(sizes.Take(RealmRaidRecord.Depth).Zip(sizes.Skip(1), (a, b) => Math.Abs(a - b)).Max(),
                    Is.LessThanOrEqualTo(Rules.LossAddPercent + 1e-9), "one step while the record fills");
            });
        }

        [Test]
        public void TheFirstWinsTrimAtOnceAndTheSizeComesDownWithinTwentyRaids()
        {
            var (_, _, sizes) = Simulate(p => WinChance(p, half: 60), 40, 7);
            Assert.That(sizes[0], Is.EqualTo(100));
            Assert.That(sizes.Skip(10).Take(30).Average(), Is.LessThan(90), "an encounter that wins nine in ten comes down");
        }

        [Test]
        public void AnEncounterThatStillWinsAtTheSmallestSizeStaysThere()
        {
            // A model where even 70% wins about nine in ten: the size stays at the floor (a rare loss lifts it a step for
            // a raid or two), and the bosses are untouched.
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 40), 1000, 11);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.LessThan(75));
                Assert.That(rate, Is.GreaterThan(0.85), "the floor is a limit, not a promise of 2 in 3");
            });
        }

        [Test]
        public void AnEncounterThatLosesEvenAtTheLargestSizeStaysThere()
        {
            // A model that wins only half the time at 150%: the size stays up (the Special Forces are the help there).
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 150), 1000, 13);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.GreaterThan(140));
                Assert.That(rate, Is.InRange(0.4, 0.6));
            });
        }

        // ---- Sieges ----

        [Test]
        public void TheSiegeBalanceIsRebuiltFromTheLedgerAfterARestart()
        {
            bool[] sixOfSeven = [true, true, true, false, true, true, true];
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Load(sixOfSeven);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter(sixOfSeven)));
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.MaximumShift), "6 of 7 captured: defenders at +0.25");
                Assert.That(SiegeDefenseBalance.Ratio(0.75), Is.EqualTo(1.0));
            });
        }

        [Test]
        public void ASiegeThatEndsBeforeTheLedgerIsReadIsReplayedAfterIt()
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Record(true);
            SiegeDefenseBalance.Load([true, true]);
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step), "three captures in a row");
            SiegeDefenseBalance.Load([]);
            Assert.That(SiegeDefenseBalance.Shift, Is.Zero, "a new ledger replaces the record");
        }

        [Test]
        public void TheSiegeShiftReplaysTheWayItWasRecorded()
        {
            var random = new Random(5);
            bool[] results = Enumerable.Range(0, 30).Select(_ => random.Next(3) != 0).ToArray();
            foreach (bool result in results) SiegeDefenseBalance.Record(result);
            Assert.That(SiegeDefenseBalance.ShiftAfter(results), Is.EqualTo(SiegeDefenseBalance.Shift));
        }

        [Test]
        public void TheSiegeLineShowsTheShiftAndTheWindow() =>
            Assert.That(SiegeDefenseBalance.Line("captured", 0.125, 0.25, [true, true, false, true]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE event=\"captured\" shift=+0.125->+0.25 share=0.75->1 window=\"CCDC\""));

        [Test]
        public void TheLedgerGivesBackOnlyBotSieges()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ut-siege-ledger-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string kind, string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "keep-" + n, "Keep " + n, kind, "Albion",
                        $"2026-11-04T10:{n:00}:00.0000000Z", $"2026-11-04T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 50, 40);
                RealmEventRecordStore.Save(path, [
                    Row("Keep", "Captured", "Keep captured: ownership changed"),
                    Row("Keep", "Defended", "Siege defended: no attacker reached the keep for 10 minutes"),
                    Row("Keep", "Failed rally", "Rally failed: insufficient physical attackers at the one-hour deadline"),
                    Row("Keep", "Captured", RealmEventRecordStore.PlayerAttackPrefix + "Keep captured: ownership changed"),
                    Row("Relic keep", "Defended (timeout)", "Siege defended: four-hour battle timer expired"),
                    Row("Epic dungeon", "Boss defeated", "The final dungeon encounter has been defeated."),
                    Row("Keep", "Interrupted", "Server stopped before the event reached a final outcome."),
                    Row("Keep", "Captured", "Keep captured: siege ended"),
                ]);
                Assert.That(RealmEventRecordStore.SiegeResults(path, 24), Is.EqualTo(new[] { true, false, false, true }));
                Assert.That(RealmEventRecordStore.SiegeResults(path, 2), Is.EqualTo(new[] { false, true }), "the last ones, oldest first");
                Assert.That(RealmEventRecordStore.SiegeResults(Path.Combine(folder, "missing.sqlite3"), 24), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
