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
        public void TheDefaultsAreTwoInThreeBetweenSixtyAndAHundredPercent()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.TargetWinPercentSetting, Is.EqualTo(67));
                Assert.That(RealmRaidRecruitmentPolicy.SmallestSizePercentSetting, Is.EqualTo(60));
                Assert.That(RealmRaidRecruitmentPolicy.LargestSizePercentSetting, Is.EqualTo(100));
                Assert.That(RealmRaidRecruitmentPolicy.WinTrimPercentSetting, Is.EqualTo(5));
                Assert.That(RealmRaidRecruitmentPolicy.RulesFromSettings(), Is.EqualTo(Rules));
                Assert.That(Rules.LossAddPercent, Is.EqualTo(5 * 67 / 33.0).Within(1e-9), "a loss adds about twice what a win trims");
            });
        }

        [Test]
        public void NoRecordIsTodaysSize()
        {
            var sizing = RealmRaidRecruitmentPolicy.Size(200, [], Rules);
            Assert.Multiple(() =>
            {
                Assert.That(sizing.Percent, Is.EqualTo(100));
                Assert.That(sizing.Required, Is.EqualTo(200));
                Assert.That(sizing.RecruitCap, Is.EqualTo(300), "a full 200 raid keeps its 300 cap");
                Assert.That(RealmRaidRecruitmentPolicy.Size(120, [], Rules).RecruitCap, Is.EqualTo(RealmRaidRecruitmentPolicy.RecruitCap(120)));
                Assert.That(sizing.SpecialForcesOnWipe, Is.EqualTo(AutonomousEpicSpecialForces.MaximumParties), "and the full guild after a wipe");
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
                Assert.That(sizes.Take(9), Is.EqualTo(new double[] { 100, 95, 90, 85, 80, 75, 70, 65, 60 }));
                Assert.That(sizes.Skip(8), Is.All.EqualTo(60), "never under the smallest size");
                // 2026-11-04 on the owner's test copy: Avalon City (120) won once, Summoner's Hall (200) once.
                Assert.That(RealmRaidRecruitmentPolicy.Size(120, [Win], Rules).Required, Is.EqualTo(114));
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, [Win], Rules).Required, Is.EqualTo(190));
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, [Win, Win, Win, Win], Rules).Required, Is.EqualTo(160));
            });
        }

        [Test]
        public void LossesAddItBackAndNeverPastTodaysSize()
        {
            Assert.Multiple(() =>
            {
                double afterFour = RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win], Rules);
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, Loss], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, TimedOut], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9), "a time-out is a loss");
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Loss, Loss, Loss], Rules), Is.EqualTo(100), "never above today's size");
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Loss], Rules), Is.EqualTo(100));
                // Two wins and a loss, the target's own mix, leave the size where it was.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, Win, Win, Win, Loss], Rules),
                    Is.EqualTo(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, Win], Rules) + 5 * (67 / 33.0 - 2)).Within(1e-9));
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
        public void TheRecruitCapShrinksWithTheSizeInWholeParties()
        {
            Assert.Multiple(() =>
            {
                var at95 = RealmRaidRecruitmentPolicy.Size(200, [Win], Rules);
                Assert.That(at95.RecruitCap, Is.EqualTo(288), "95% of 300 in whole parties, not RecruitCap(190) = 240");
                var smallest = RealmRaidRecruitmentPolicy.Size(200, Enumerable.Repeat(Win, 10).ToArray(), Rules);
                Assert.That((smallest.Required, smallest.RecruitCap), Is.EqualTo((120, 184)));
                var dungeon = RealmRaidRecruitmentPolicy.Size(120, Enumerable.Repeat(Win, 10).ToArray(), Rules);
                Assert.That((dungeon.Required, dungeon.RecruitCap), Is.EqualTo((72, 96)));
                for (int wins = 0; wins <= 10; wins++)
                foreach (int full in new[] { 40, 64, 96, 120, 137, 160, 200 })
                {
                    var sizing = RealmRaidRecruitmentPolicy.Size(full, Enumerable.Repeat(Win, wins).ToArray(), Rules);
                    Assert.That(sizing.RecruitCap, Is.GreaterThanOrEqualTo(sizing.Required).And.LessThanOrEqualTo(300));
                    Assert.That(sizing.RecruitCap % 8 == 0 || sizing.RecruitCap == 300, Is.True, $"{full} after {wins} wins");
                    Assert.That(sizing.Required, Is.GreaterThanOrEqualTo(Math.Min(40, full)), "never under 40");
                    // The short start and late start stay shares of what the sized raid needs.
                    Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, (sizing.Required + 1) / 2, sizing.Required),
                        Is.EqualTo((sizing.Required + 1) / 2 >= 40));
                }
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
                var off = RealmRaidRecruitmentPolicy.Size(200, wins, Rules with { TargetWinPercent = 0 });
                Assert.That((off.Required, off.RecruitCap), Is.EqualTo((200, 300)));
                var loading = RealmRaidRecruitmentPolicy.Size(200, wins, Rules, recordLoaded: false);
                Assert.That(loading.Required, Is.EqualTo(200), "the ledger still loading: full size");
                Assert.That(loading.Why, Does.Contain("loading"));
            });
        }

        [Test]
        public void TheRulesAreMadeSafe()
        {
            var safe = new RealmRaidRecruitmentPolicy.SizingRules(99, 5, 500, 0).Normalized();
            Assert.That(safe, Is.EqualTo(new RealmRaidRecruitmentPolicy.SizingRules(90, 25, 150, 1)));
            var inverted = new RealmRaidRecruitmentPolicy.SizingRules(67, 80, 50, 5).Normalized();
            Assert.That(inverted.LargestPercent, Is.EqualTo(80), "the largest is never under the smallest");
            Assert.That(new RealmRaidRecruitmentPolicy.SizingRules(-1, 60, 100, 5).Normalized().On, Is.False);
            // A largest size above 100% may grow a losing raid, but never past 300.
            var big = RealmRaidRecruitmentPolicy.Size(200, [Loss], Rules with { LargestPercent = 150 });
            Assert.That((big.Required, big.RecruitCap), Is.EqualTo((300, 300)));
        }

        [Test]
        public void OneRecordGivesTheSizeAndTheSpecialForces()
        {
            // Six wins: two-thirds size, and nobody comes after a wipe.
            var winning = RealmRaidRecruitmentPolicy.Size(200, Enumerable.Repeat(Win, 6).ToArray(), Rules);
            // Then two losses in a row: bigger again, and the Special Forces come from the start.
            var losing = RealmRaidRecruitmentPolicy.Size(200, [.. Enumerable.Repeat(Win, 6), Loss, TimedOut], Rules);
            Assert.Multiple(() =>
            {
                Assert.That((winning.Required, winning.SpecialForcesOnWipe, winning.SpecialForcesFromStart, winning.Streak),
                    Is.EqualTo((140, 0, false, 6)));
                Assert.That(losing.Percent, Is.EqualTo(70 + 2 * Rules.LossAddPercent).Within(1e-9));
                Assert.That((losing.SpecialForcesOnWipe, losing.SpecialForcesFromStart, losing.Streak), Is.EqualTo((2, true, -2)));
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
            var sizing = RealmRaidRecruitmentPolicy.Size(200, [Win, Win, Win], Rules);
            string line = RealmRaidRecruitmentPolicy.SizedLine("summoners-albion", "Albion", sizing);
            Assert.That(line, Is.EqualTo("REALM_RAID_SIZED event=summoners-albion realm=Albion full=200 size=85% required=170 recruitCap=256 " +
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
        /// A simple model of a raid's chance to win by its size (share of today's): a logistic curve. Fitted loosely to
        /// 2026-11-04 (four automatic raids at full size, four wins, one of them after a wipe): about 92% at full size,
        /// 2/3 at 75%, a coin flip at 65%. It is a model, not a measurement.
        /// </summary>
        private static double WinChance(double percent, double half = 65, double steep = 0.0693) =>
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
                double percent = RealmRaidRecruitmentPolicy.Size(200, RealmRaidRecord.CountedOf(ledger), Rules).Percent;
                sizes.Add(percent);
                if (random.NextDouble() < interruptedShare) { ledger.Add("Interrupted"); continue; }
                bool win = random.NextDouble() < chance(percent);
                ledger.Add(win ? Win : random.Next(2) == 0 ? Loss : TimedOut);
                if (raid >= raids / 4) { counted++; won += win ? 1 : 0; sum += percent; } // after a settling-in quarter
            }
            return (won / (double)counted, sum / counted, sizes);
        }

        // seed, the model's coin-flip size and steepness, and the size it wins 2 in 3 at
        [TestCase(1, 65, 0.0693, 75)]
        [TestCase(2, 65, 0.0693, 75)]
        [TestCase(3, 65, 0.0693, 75)]
        [TestCase(4, 65, 0.0693, 75)]
        [TestCase(5, 65, 0.0693, 75)]
        [TestCase(6, 80, 0.15, 84.6)]   // a steep encounter: a little smaller and it falls apart
        [TestCase(7, 50, 0.03, 73.1)]   // a forgiving one: size matters less
        public void ARaidThatWinsTooEasilySettlesNearTwoInThree(int seed, double half, double steep, double twoInThreeAt)
        {
            // 2,000 raids, three in ten of them interrupted (not counted): the record fills past its 400 and slides.
            var (rate, mean, sizes) = Simulate(p => WinChance(p, half, steep), 2000, seed, interruptedShare: 0.3);
            TestContext.WriteLine($"seed {seed}: won {rate:P1} at {mean:0.#}% of full size on average");
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.InRange(0.62, 0.72), "about two in three");
                Assert.That(mean, Is.InRange(twoInThreeAt - 6, twoInThreeAt + 6), "around the size the model wins 2 in 3 at");
                Assert.That(sizes, Is.All.InRange(60.0, 100.0));
                // Smooth: one raid to the next moves by one step at most.
                Assert.That(sizes.Zip(sizes.Skip(1), (a, b) => Math.Abs(a - b)).Max(), Is.LessThanOrEqualTo(Rules.LossAddPercent + 1e-9));
            });
        }

        [Test]
        public void TheFirstWinsTrimAtOnceAndTheSizeComesDownWithinTwentyRaids()
        {
            var (_, _, sizes) = Simulate(p => WinChance(p), 40, 7);
            Assert.That(sizes[0], Is.EqualTo(100));
            Assert.That(sizes.Skip(10).Take(30).Average(), Is.LessThan(90), "an encounter that wins nine in ten comes down");
        }

        [Test]
        public void AnEncounterThatStillWinsAtTheSmallestSizeStaysThere()
        {
            // A model where even 60% wins about nine in ten: the size stays at the floor (a rare loss lifts it a step for
            // a raid or two), and the bosses are untouched.
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 30), 1000, 11);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.LessThan(65));
                Assert.That(rate, Is.GreaterThan(0.85), "the floor is a limit, not a promise of 2 in 3");
            });
        }

        [Test]
        public void AnEncounterThatLosesAtFullSizeKeepsItsFullSize()
        {
            // A model that wins only half the time at full size: the size stays up (the Special Forces are the help there).
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 100), 1000, 13);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.GreaterThan(95));
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
