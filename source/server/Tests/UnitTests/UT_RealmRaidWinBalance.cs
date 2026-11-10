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
        private static readonly RaidResult Helped = new(Win, WithSpecialForces: true);
        /// <summary>Outcomes as results the Special Forces did not take part in.</summary>
        private static RaidResult[] R(IEnumerable<string> outcomes) => outcomes.Select(o => (RaidResult)o).ToArray();

        [SetUp]
        public void Reset()
        {
            AutonomousEpicSpecialForces.ResetForTests();
            SiegeDefenseBalance.Reset();
        }

        [Test]
        public void TheDefaultsStartAtAHundredAndTwentyFiveAimAtTwoInThreeAndMoveSeventyToTwoHundred()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.TargetWinPercentSetting, Is.EqualTo(67));
                Assert.That(RealmRaidRecruitmentPolicy.SmallestSizePercentSetting, Is.EqualTo(70));
                Assert.That(RealmRaidRecruitmentPolicy.LargestSizePercentSetting, Is.EqualTo(200));
                Assert.That(RealmRaidRecruitmentPolicy.WinTrimPercentSetting, Is.EqualTo(10));
                Assert.That(RealmRaidRecruitmentPolicy.StartSizePercentSetting, Is.EqualTo(125));
                Assert.That(RealmRaidRecruitmentPolicy.RulesFromSettings(), Is.EqualTo(Rules));
                Assert.That(Rules.StartPercent, Is.EqualTo(125));
                Assert.That(Rules.LossAddPercent, Is.EqualTo(10 * 67 / 33.0).Within(1e-9), "a loss adds about twice what a win trims");
            });
        }

        // The period sizes (docs/RAID-SIZES.md, 2026-11-04): the comfortable 2002-2004 raid, not the minimum.
        [TestCase("dragon-albion", 70)]
        [TestCase("dragon-midgard", 70)]
        [TestCase("dragon-hibernia", 70)]
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
        public void NoRecordStartsAtAHundredAndTwentyFivePercentOfThePeriodSize()
        {
            // Owner 2026-11-05, after a night of 1 win in 7: bots play well below 2003 players.
            var sizing = RealmRaidRecruitmentPolicy.Size(100, [], Rules);
            Assert.Multiple(() =>
            {
                Assert.That(sizing.Percent, Is.EqualTo(125));
                Assert.That((sizing.Required, sizing.RecruitCap), Is.EqualTo((125, 160)), "Caer Sidi: a quarter above, in whole parties");
                foreach (var (period, start, cap) in new[] { (70, 88, 112), (64, 80, 104), (120, 150, 192), (48, 60, 80), (40, 50, 72) })
                {
                    var first = RealmRaidRecruitmentPolicy.Size(period, [], Rules);
                    Assert.That((first.Required, first.RecruitCap), Is.EqualTo((start, cap)), $"period size {period}");
                }
                Assert.That(sizing.SpecialForcesOnWipe, Is.EqualTo(3), "the full 24 after a wipe");
                Assert.That(RealmRaidRecruitmentPolicy.Size(70, [], Rules).SpecialForcesOnWipe, Is.EqualTo(3), "for a dragon raid too");
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
                Assert.That(sizes.Take(7), Is.EqualTo(new double[] { 125, 115, 105, 95, 85, 75, 70 }));
                Assert.That(sizes.Skip(6), Is.All.EqualTo(70), "never under the smallest size");
                Assert.That(RealmRaidRecruitmentPolicy.Size(100, [Win], Rules).Required, Is.EqualTo(115));
                Assert.That(RealmRaidRecruitmentPolicy.Size(70, R(Enumerable.Repeat(Win, 6)), Rules).Required, Is.EqualTo(49), "a dragon raid at 70%");
                Assert.That(RealmRaidRecruitmentPolicy.Size(48, R(Enumerable.Repeat(Win, 6)), Rules).Required, Is.EqualTo(34));
                Assert.That(RealmRaidRecruitmentPolicy.Size(40, R(Enumerable.Repeat(Win, 6)), Rules).Required, Is.EqualTo(28),
                    "70% of the smallest raid, above the three-party minimum");
            });
        }

        [Test]
        public void LossesGrowARaidUpToTwiceItsPeriodSize()
        {
            Assert.Multiple(() =>
            {
                double afterFour = RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win], Rules);
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, Loss], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Win, Win, TimedOut], Rules),
                    Is.EqualTo(afterFour + Rules.LossAddPercent).Within(1e-9), "a time-out is a loss");
                // Bots that play worse than 2003 players: a raid that keeps losing grows above its period size.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Loss], Rules), Is.EqualTo(125 + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Loss, Loss, Loss], Rules), Is.EqualTo(125 + 3 * Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Loss, Loss, Loss, Loss], Rules), Is.EqualTo(200), "never above 200%");
                Assert.That(RealmRaidRecruitmentPolicy.Size(100, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(200));
                Assert.That(RealmRaidRecruitmentPolicy.Size(70, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(140), "a dragon raid at 200%");
                Assert.That(RealmRaidRecruitmentPolicy.Size(64, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(128), "Darkness Falls at 200%");
                Assert.That(RealmRaidRecruitmentPolicy.Size(120, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(240));
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(300), "never past 300");
                // Two wins and a loss, the target's own mix, leave the size about where it was.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Loss], Rules), Is.EqualTo(105 + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, Win, Loss], Rules), Is.InRange(124.9, 125.4));
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
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([Win, notAResult, notAResult, Win], Rules), Is.EqualTo(105));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent([notAResult, notAResult], Rules), Is.EqualTo(125));
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
                var largest = RealmRaidRecruitmentPolicy.Size(100, R(Enumerable.Repeat(Loss, 8)), Rules);
                Assert.That((largest.Required, largest.RecruitCap), Is.EqualTo((200, 256)));
                foreach (var (period, most, cap) in new[] { (64, 128, 160), (70, 140, 176), (120, 240, 300), (48, 96, 128), (40, 80, 112) })
                {
                    var top = RealmRaidRecruitmentPolicy.Size(period, R(Enumerable.Repeat(Loss, 8)), Rules);
                    Assert.That((top.Required, top.RecruitCap), Is.EqualTo((most, cap)), $"period size {period} at its largest");
                }
                var smallest = RealmRaidRecruitmentPolicy.Size(100, R(Enumerable.Repeat(Win, 10)), Rules);
                Assert.That((smallest.Required, smallest.RecruitCap), Is.EqualTo((70, 96)));
                var dragon = RealmRaidRecruitmentPolicy.Size(70, R(Enumerable.Repeat(Win, 10)), Rules);
                Assert.That((dragon.Required, dragon.RecruitCap), Is.EqualTo((49, 64)));
                // A big raid scales its 300 cap, and never goes past 300.
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, R(Enumerable.Repeat(Win, 6)), Rules).RecruitCap, Is.EqualTo(216),
                    "70% of 300, not RecruitCap(140) = 176");
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, [], Rules).Required, Is.EqualTo(250));
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, [], Rules).RecruitCap, Is.EqualTo(300));
                Assert.That(RealmRaidRecruitmentPolicy.Size(200, R(Enumerable.Repeat(Loss, 8)), Rules).Required, Is.EqualTo(300));
                foreach (int full in new[] { 40, 48, 64, 70, 100, 120, 200 })
                for (int wins = 0; wins <= 8; wins++)
                for (int losses = 0; losses <= 6; losses++)
                {
                    string[] record = [.. Enumerable.Repeat(Loss, losses), .. Enumerable.Repeat(Win, wins)];
                    var sizing = RealmRaidRecruitmentPolicy.Size(full, R(record), Rules);
                    Assert.That(sizing.RecruitCap, Is.GreaterThanOrEqualTo(sizing.Required).And.LessThanOrEqualTo(300));
                    Assert.That(sizing.RecruitCap % 8 == 0 || sizing.RecruitCap == 300, Is.True, $"{full} after {losses} losses and {wins} wins");
                    Assert.That(sizing.Required, Is.InRange((int)Math.Round(full * 0.7), Math.Min(300, full * 2)));
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
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 35, 70), Is.True, "a dragon raid: half of 70");
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 34, 70), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 25, 49), Is.True, "trimmed to 49: half, not 40");
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 24, 49), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 24, 34), Is.True, "three parties at least");
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 23, 34), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(false, 50, 100), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(true, 60, 60), Is.False, "forced raids never go in short");
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 30, patience, 60), Is.True, "the hub leaves with half too");
                Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 29, patience, 60), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.LateStartMinimum(60), Is.EqualTo(42), "the late start keeps its 70%");
            });
        }

        [Test]
        public void TheSpecialForcesStayTwentyFourWhateverTheRaidsSize()
        {
            // Owner 2026-11-04: they come with 24, not scaled by the raid; a win they helped is what makes it 16.
            foreach (int full in new[] { 40, 48, 64, 70, 100, 120 })
            {
                Assert.That(RealmRaidRecruitmentPolicy.Size(full, [], Rules).SpecialForcesOnWipe, Is.EqualTo(3), $"a raid of {full}");
                Assert.That(RealmRaidRecruitmentPolicy.Size(full, R(Enumerable.Repeat(Win, 8)), Rules).SpecialForcesOnWipe, Is.EqualTo(3),
                    "wins without them: still 24, however small the raid got");
                Assert.That(RealmRaidRecruitmentPolicy.Size(full, [Helped], Rules).SpecialForcesOnWipe, Is.EqualTo(2));
            }
        }

        [Test]
        public void ARaidWithTheOwnersSizeOrNoSizingKeepsItsFullSize()
        {
            RaidResult[] wins = R(Enumerable.Repeat(Win, 6));
            Assert.Multiple(() =>
            {
                var fixedSize = RealmRaidRecruitmentPolicy.Size(150, wins, Rules, fixedSize: true);
                Assert.That((fixedSize.Required, fixedSize.Percent), Is.EqualTo((150, 100.0)));
                Assert.That(fixedSize.Why, Does.Contain("realm_raid_minimum_bots"));
                var off = RealmRaidRecruitmentPolicy.Size(100, wins, Rules with { TargetWinPercent = 0 });
                Assert.That((off.Required, off.RecruitCap), Is.EqualTo((125, 160)), "sizing off: the starting size");
                var loading = RealmRaidRecruitmentPolicy.Size(100, wins, Rules, recordLoaded: false);
                Assert.That(loading.Required, Is.EqualTo(125), "the ledger still loading: the starting size");
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
                var huge = RealmRaidRecruitmentPolicy.Size(120, R(Enumerable.Repeat(Loss, 30)), Rules with { LargestPercent = 300 });
                Assert.That((huge.Required, huge.RecruitCap), Is.EqualTo((300, 300)), "never past the realm's 300");
            });
        }

        [Test]
        public void OneRecordGivesTheSizeAndTheSpecialForces()
        {
            // Caer Sidi won six times, twice after a wipe with the Special Forces: 70 to stage, 8 after a wipe.
            RaidResult[] six = [Win, Helped, Win, Win, Helped, Win];
            var winning = RealmRaidRecruitmentPolicy.Size(100, six, Rules);
            // Then two losses in a row: 40.6 points bigger again, a party more for each, and from the start.
            var losing = RealmRaidRecruitmentPolicy.Size(100, [.. six, Loss, TimedOut], Rules);
            Assert.Multiple(() =>
            {
                Assert.That((winning.Required, winning.SpecialForcesOnWipe, winning.SpecialForcesFromStart, winning.Streak),
                    Is.EqualTo((70, 1, false, 6)));
                Assert.That(losing.Percent, Is.EqualTo(70 + 2 * Rules.LossAddPercent).Within(1e-9));
                Assert.That((losing.Required, losing.SpecialForcesOnWipe, losing.SpecialForcesFromStart, losing.Streak),
                    Is.EqualTo((111, 3, true, -2)));
                Assert.That(losing.Why, Does.Contain("6 won and 2 lost here, the last 2 lost"));
            });
        }

        [Test]
        public void TheSpecialForcesFollowTheWholeRecordNotTheLastSixRows()
        {
            // Restarts and failed rallies change nothing; nor do later wins without them (the old last-six window would
            // have let six plain wins bring all 24 back).
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([Helped, Helped, "Interrupted", "Interrupted", "Interrupted", "Failed rally", Loss]),
                    Is.EqualTo(2), "3 - 2 helped wins + 1 loss");
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([Helped, Helped, .. R(Enumerable.Repeat(Win, 6))]), Is.EqualTo(1));
                Assert.That(AutonomousEpicSpecialForces.CallUpParties([.. R(Enumerable.Repeat(Loss, 6)), Helped, Helped]), Is.EqualTo(1));
            });
        }

        [Test]
        public void AWinTheSpecialForcesJoinedIsRecordedAsTheirs()
        {
            AutonomousEpicSpecialForces.JoinedForTests("epic-midgard", 24);
            AutonomousEpicSpecialForces.RaidEnded("epic-midgard", Win);
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecord.GetResults("epic-midgard"), Is.EqualTo(new[] { Helped }));
                Assert.That(AutonomousEpicSpecialForces.Joined("epic-midgard"), Is.Zero, "counted afresh for the next raid");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize(RealmRaidRecord.GetResults("epic-midgard")), Is.EqualTo(16));
            });
            AutonomousEpicSpecialForces.RaidEnded("epic-midgard", Win);
            Assert.That(RealmRaidRecord.GetResults("epic-midgard").Last(), Is.EqualTo((RaidResult)Win), "the next win was without them");
            AutonomousEpicSpecialForces.Request("epic-midgard", "Tuscaran Glacier", eRealm.Midgard, "a wipe at King Tuscar");
            Assert.That(AutonomousEpicSpecialForces.NextCallMembersForTests, Is.EqualTo(16), "the next call-up there is 16");
            // A launcher raid they joined is still not part of the record.
            AutonomousEpicSpecialForces.JoinedForTests("epic-albion", 8);
            AutonomousEpicSpecialForces.RaidEnded("epic-albion", Win, forced: true);
            Assert.That(RealmRaidRecord.GetResults("epic-albion"), Is.Empty);
        }

        [Test]
        public void TheLedgerSaysWhenTheSpecialForcesJoinedAndOldRowsCountAsWithoutThem()
        {
            Assert.Multiple(() =>
            {
                string details = RealmEventRecordStore.WithSpecialForces("The Caer Sidi expedition defeated the final encounter.", 16);
                Assert.That(details, Is.EqualTo("The Caer Sidi expedition defeated the final encounter. Epic Special Forces joined: 16."));
                Assert.That(RealmEventRecordStore.SpecialForcesIn(details), Is.EqualTo(16));
                Assert.That(RealmEventRecordStore.WithSpecialForces("x", 0), Is.EqualTo("x"), "nothing added when none joined");
                Assert.That(RealmEventRecordStore.SpecialForcesIn("Defeated at Apocalypse after 2 failed attempts."), Is.Zero);
                Assert.That(RealmEventRecordStore.SpecialForcesIn(null), Is.Zero);
                Assert.That(RealmEventRecordStore.SpecialForcesIn("x Epic Special Forces joined: ."), Is.Zero);
                Assert.That(RealmEventRecordStore.SpecialForcesIn(RealmEventRecordStore.ForcedRaidPrefix + details), Is.EqualTo(16));
            });
            string folder = Path.Combine(Path.GetTempPath(), "ut-sf-ledger-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "dragon-albion", "Golestandt", "Dragon", "Albion",
                        $"2026-11-04T10:{n:00}:00.0000000Z", $"2026-11-04T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 70, 60);
                RealmEventRecordStore.Save(path, [
                    Row(Win, "The dragon encounter has ended; checking actual respawn before the next expedition."),   // old row
                    Row(Win, RealmEventRecordStore.WithSpecialForces("The dragon encounter has ended.", 24)),
                    Row(Loss, RealmEventRecordStore.WithSpecialForces("Defeated at Golestandt after 2 failed attempts.", 24)),
                    Row(Win, RealmEventRecordStore.WithSpecialForces(RealmEventRecordStore.ForcedRaidPrefix + "The dragon encounter has ended.", 8)),
                    Row("Interrupted", "Server stopped before the event reached a final outcome."),
                ]);
                var rows = RealmEventRecordStore.Results(path, "dragon-albion", 10, automaticOnly: true);
                Assert.Multiple(() =>
                {
                    Assert.That(rows.Select(r => r.SpecialForces), Is.EqualTo(new[] { 0, 24, 24, 0 }));
                    Assert.That(RealmRaidRecord.FromLedger(rows), Is.EqualTo(new RaidResult[] { Win, Helped, new(Loss, true) }));
                    Assert.That(AutonomousEpicSpecialForces.CallUpSize(RealmRaidRecord.FromLedger(rows)), Is.EqualTo(24), "24 -8 +8");
                    Assert.That(RealmEventRecordStore.Results(path, "dragon-albion", 10), Has.Count.EqualTo(5));
                    Assert.That(RealmEventRecordStore.Results(Path.Combine(folder, "missing.sqlite3"), "dragon-albion", 10), Is.Empty);
                });
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
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
            Assert.That(line, Is.EqualTo("REALM_RAID_SIZED event=summoners-albion realm=Albion full=64 size=95% required=61 recruitCap=80 " +
                "won=3 lost=0 specialForcesOnWipe=24 specialForcesFromStart=False " +
                "why=\"3 won and 0 lost here, the last 3 won: each win trims 10 points, each loss adds 20.3, aiming at 67% wins\" " +
                "rules=\"start=125% range=70-200% win=-10 loss=+20.3 target=67%\""));
            // The live values: a world still on the old 5-point step says so.
            var old = RealmRaidRecruitmentPolicy.Size(64, [Win], Rules with { WinTrimPercent = 5, LargestPercent = 150 });
            Assert.That(RealmRaidRecruitmentPolicy.SizedLine("x", "Albion", old),
                Does.Contain("each win trims 5 points, each loss adds 10.2").And.Contain("range=70-150% win=-5 loss=+10.2"));
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
            var ledger = new List<RaidResult>();
            var sizes = new List<double>();
            int counted = 0, won = 0;
            double sum = 0;
            for (int raid = 0; raid < raids; raid++)
            {
                // As the server does it: the record is the ledger's last counted results, at most RealmRaidRecord.Depth, each
                // with the size its raid ran at.
                var sizing = RealmRaidRecruitmentPolicy.Size(100, RealmRaidRecord.CountedResults(ledger), Rules);
                double percent = sizing.Percent;
                sizes.Add(percent);
                if (random.NextDouble() < interruptedShare) { ledger.Add("Interrupted"); continue; }
                bool win = random.NextDouble() < chance(percent);
                ledger.Add(new RaidResult(win ? Win : random.Next(2) == 0 ? Loss : TimedOut, false, sizing.RecordedPercent));
                if (raid >= raids / 4) { counted++; won += win ? 1 : 0; sum += percent; } // after a settling-in quarter
            }
            return (won / (double)counted, sum / counted, sizes);
        }

        // seed, the model's coin-flip size and steepness, and the size it wins 2 in 3 at (% of the period size)
        [TestCase(1, 75, 0.0693, 85)]     // bots as good as 2003 players: the raid comes down well below its start
        [TestCase(2, 90, 0.0693, 100)]    // just right at the period size
        [TestCase(3, 115, 0.0693, 125)]   // right at the start (125%)
        [TestCase(4, 115, 0.0693, 125)]
        [TestCase(5, 115, 0.0693, 125)]
        [TestCase(6, 120, 0.0693, 130)]   // bots that play much worse
        [TestCase(9, 130, 0.0693, 140)]   // the overnight losses (2026-11-05): about 140% needed
        [TestCase(7, 110, 0.15, 114.6)]   // a steep encounter: a little smaller and it falls apart
        [TestCase(8, 70, 0.03, 93.1)]     // a forgiving one: size matters less
        public void ARaidSettlesNearTwoInThree(int seed, double half, double steep, double twoInThreeAt)
        {
            // 2,000 raids, three in ten of them interrupted (not counted): the record fills past its 400 and slides.
            var (rate, mean, sizes) = Simulate(p => WinChance(p, half, steep), 2000, seed, interruptedShare: 0.3);
            TestContext.WriteLine($"seed {seed}: won {rate:P1} at {mean:0.#}% of the period size on average");
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.InRange(0.62, 0.72), "about two in three");
                // A flat curve settles a little above its 2-in-3 point (a loss adds twice what a win trims).
                Assert.That(mean, Is.InRange(twoInThreeAt - 8, twoInThreeAt + 8), "around the size the model wins 2 in 3 at");
                Assert.That(sizes, Is.All.InRange(70.0, 200.0));
                // Smooth: one raid to the next moves by one step at most, also once the 400-result record is full (each
                // result carries the size its raid ran at, so dropping old ones changes nothing).
                double[] moves = sizes.Zip(sizes.Skip(1), (a, b) => Math.Abs(a - b)).ToArray();
                Assert.That(moves.Max(), Is.LessThanOrEqualTo(Rules.LossAddPercent + 1e-9));
            });
        }

        // ---- With raid discipline (2026-11-08): the realm learns the boss ----

        /// <summary>
        /// The size learner with the realm learning the boss (RaidDiscipline.Familiarity from the same record): an unlearned
        /// raid needs <paramref name="unlearnedExtra"/> more points of its period size for the same chance (more of it dies
        /// to the stomps), shrinking as familiarity grows. A model, not a measurement.
        /// </summary>
        private static (List<double> Sizes, List<bool> Wins, List<double> Familiarity) SimulateLearning(int raids, int seed,
            double learnedHalf, double unlearnedExtra, double interruptedShare = 0)
        {
            var random = new Random(seed);
            var ledger = new List<RaidResult>();
            var sizes = new List<double>();
            var wins = new List<bool>();
            var familiarity = new List<double>();
            for (int raid = 0; raid < raids; raid++)
            {
                List<RaidResult> record = RealmRaidRecord.CountedResults(ledger);
                var sizing = RealmRaidRecruitmentPolicy.Size(100, record, Rules);
                double f = RaidDiscipline.Familiarity(record);
                if (random.NextDouble() < interruptedShare) { ledger.Add("Interrupted"); continue; }
                bool win = random.NextDouble() < WinChance(sizing.Percent, learnedHalf + unlearnedExtra * (1 - f));
                ledger.Add(new RaidResult(win ? Win : Loss, false, sizing.RecordedPercent));
                sizes.Add(sizing.Percent);
                wins.Add(win);
                familiarity.Add(f);
            }
            return (sizes, wins, familiarity);
        }

        // the size it wins 2 in 3 at once learned, and how much more an unlearned raid needs
        [TestCase(1, 105, 30)]
        [TestCase(2, 105, 60)]
        [TestCase(3, 120, 45)]
        [TestCase(4, 85, 45)]
        public void WithTheRealmLearningTheBossItStillSettlesNearTwoInThreeWithoutSwinging(int seed, double learnedHalf, double unlearnedExtra)
        {
            var (sizes, wins, familiarity) = SimulateLearning(2000, seed, learnedHalf, unlearnedExtra, interruptedShare: 0.3);
            int settled = sizes.Count / 4;
            double rate = wins.Skip(settled).Count(w => w) / (double)(wins.Count - settled);
            double mean = sizes.Skip(settled).Average();
            double spread = Math.Sqrt(sizes.Skip(settled).Average(s => (s - mean) * (s - mean)));
            var (plainSizes, plainWins) = (SimulateLearning(2000, seed, learnedHalf, 0, 0.3).Sizes, SimulateLearning(2000, seed, learnedHalf, 0, 0.3).Wins);
            double plainMean = plainSizes.Skip(settled).Average();
            double plainSpread = Math.Sqrt(plainSizes.Skip(settled).Average(s => (s - plainMean) * (s - plainMean)));
            TestContext.WriteLine($"seed {seed}: won {rate:P1} at {mean:0.#}% (spread {spread:0.#}) once settled; without learning " +
                                  $"{plainWins.Skip(settled).Count(w => w) / (double)(plainWins.Count - settled):P1} at {plainMean:0.#}% (spread {plainSpread:0.#}); " +
                                  $"familiarity {familiarity[0]:0.00} -> {familiarity[4]:0.00} after 4 -> {familiarity[19]:0.00} after 20");
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.InRange(0.62, 0.72), "about two in three");
                Assert.That(mean, Is.EqualTo(plainMean).Within(5), "learned, it settles where the plain learner does");
                Assert.That(spread, Is.LessThan(plainSpread + 3), "and swings no more than it");
                double[] moves = sizes.Zip(sizes.Skip(1), (a, b) => Math.Abs(a - b)).ToArray();
                Assert.That(moves.Max(), Is.LessThanOrEqualTo(Rules.LossAddPercent + 1e-9), "one step at most between raids");
                Assert.That(familiarity[0], Is.EqualTo(0), "a fresh world is unlearned");
                Assert.That(familiarity.Zip(familiarity.Skip(1), (a, b) => b >= a).All(x => x), Is.True, "it never unlearns");
            });
        }

        [Test]
        public void EarlyRaidsLoseAndGrowAndLaterOnesWinWithFewerBots()
        {
            // The owner's arc, over 300 fresh worlds: the first raids at a boss lose more and grow the raid, the later ones
            // win more with fewer bots. Learned, it wins 2 in 3 at 115%; unlearned it needs 45 points more.
            const int worlds = 300, raids = 30;
            var size = new double[raids];
            var won = new double[raids];
            for (int world = 0; world < worlds; world++)
            {
                var (sizes, wins, _) = SimulateLearning(raids, 1000 + world, 105, 45);
                for (int i = 0; i < raids; i++) { size[i] += sizes[i] / worlds; won[i] += (wins[i] ? 1.0 : 0) / worlds; }
            }
            double firstWins = won.Take(3).Average(), laterWins = won.Skip(15).Average();
            double peak = size.Take(10).Max(), later = size.Skip(20).Average();
            TestContext.WriteLine($"won {firstWins:P0} of the first three, {laterWins:P0} from the 16th; size {size[0]:0}% -> peak " +
                                  $"{peak:0}% (raid {Array.IndexOf(size, peak) + 1}) -> {later:0}% from the 21st");
            Assert.Multiple(() =>
            {
                Assert.That(firstWins, Is.LessThan(0.5), "early raids lose more");
                Assert.That(peak, Is.GreaterThan(size[0] + 5), "and grow");
                Assert.That(laterWins, Is.InRange(0.6, 0.75), "later raids win about two in three");
                Assert.That(later, Is.LessThan(peak - 10), "with fewer bots");
            });
        }

        [Test]
        public void TheFirstWinsTrimAtOnceAndTheSizeComesDownWithinTwentyRaids()
        {
            var (_, _, sizes) = Simulate(p => WinChance(p, half: 60), 40, 7);
            Assert.That(sizes[0], Is.EqualTo(125));
            Assert.That(sizes.Skip(10).Take(30).Average(), Is.LessThan(100), "an encounter that wins nine in ten comes down");
        }

        [Test]
        public void NearTheLargestSizeItFallsShortOfTwoInThree()
        {
            // A model that wins 2 in 3 only at 190%: the 200% ceiling cuts the size's swing, so it wins less (with 150% this
            // was the 140% model, which won about 6 in 10 until the owner raised the ceiling on 2026-11-05). The ceiling is
            // a limit; past it the Special Forces are the help.
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 180), 2000, 6, interruptedShare: 0.3);
            TestContext.WriteLine($"near the ceiling: won {rate:P1} at {mean:0.#}%");
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.InRange(0.5, 0.67));
                Assert.That(mean, Is.GreaterThan(180));
            });
        }

        [Test]
        public void ALosingRaidReachesTheLargestSizeInFourLosses()
        {
            // 2026-11-05: a raid that keeps losing (as five did overnight) reaches twice its period size in four raids.
            var (_, _, sizes) = Simulate(_ => 0, 5, 1);
            Assert.That(sizes, Is.EqualTo(new[] { 125, 125 + Rules.LossAddPercent, 125 + 2 * Rules.LossAddPercent,
                125 + 3 * Rules.LossAddPercent, 200 }).Within(1e-9));
        }

        [Test]
        public void NoJumpsOnceTheRecordIsFull()
        {
            // Once 400 results are kept, the oldest drops out. A size replayed from them alone moved by more than one step
            // on a quarter of the raids with the 200% ceiling; carried on from the size each raid ran at, never.
            var (_, _, sizes) = Simulate(p => WinChance(p, half: 130), 6000, 21);
            double[] moves = sizes.Skip(RealmRaidRecord.Depth).Zip(sizes.Skip(RealmRaidRecord.Depth + 1), (a, b) => Math.Abs(a - b)).ToArray();
            TestContext.WriteLine($"largest move once the record is full: {moves.Max():0.#}");
            Assert.That(moves.Max(), Is.LessThanOrEqualTo(Rules.LossAddPercent + 1e-9));
        }

        [Test]
        public void TheNextRaidCarriesOnFromTheSizeTheLastOneRanAt()
        {
            Assert.Multiple(() =>
            {
                // Results that say their size: the next is that size and one step.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercentOf([new RaidResult(Loss, false, 160)], Rules),
                    Is.EqualTo(160 + Rules.LossAddPercent).Within(1e-9));
                Assert.That(RealmRaidRecruitmentPolicy.SizePercentOf([Win, Win, new RaidResult(Win, false, 180), Loss], Rules),
                    Is.EqualTo(170 + Rules.LossAddPercent).Within(1e-9), "older rows without a size are replayed up to it");
                // Rows from before 2026-11-05 have no size: replayed from the start as before.
                Assert.That(RealmRaidRecruitmentPolicy.SizePercentOf(R([Win, Loss]), Rules), Is.EqualTo(115 + Rules.LossAddPercent).Within(1e-9));
                // The ledger says it.
                string details = RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithSpecialForces("Defeated at Legion after 2 failed attempts.", 24), 145.303);
                Assert.That(details, Is.EqualTo("Defeated at Legion after 2 failed attempts. Epic Special Forces joined: 24. Raid size: 145.303%."));
                Assert.That(RealmEventRecordStore.RaidSizeIn(details), Is.EqualTo(145.303).Within(1e-9));
                Assert.That(RealmEventRecordStore.SpecialForcesIn(details), Is.EqualTo(24));
                Assert.That(RealmEventRecordStore.RaidSizeIn("Defeated at Legion after 2 failed attempts."), Is.Null);
                Assert.That(RealmEventRecordStore.RaidSizeIn("x Raid size: 12"), Is.Null, "unfinished");
                Assert.That(RealmEventRecordStore.WithRaidSize("x", null), Is.EqualTo("x"), "an unsized raid writes nothing");
                // Sized by results, the raid's size is recorded; a fixed size or sizing off is not.
                Assert.That(RealmRaidRecruitmentPolicy.Size(64, [], Rules).RecordedPercent, Is.EqualTo(125));
                Assert.That(RealmRaidRecruitmentPolicy.Size(64, [], Rules, fixedSize: true).RecordedPercent, Is.Null);
                Assert.That(RealmRaidRecruitmentPolicy.Size(64, [], Rules with { TargetWinPercent = 0 }).RecordedPercent, Is.Null);
                // And a raid that ends hands it to the record.
                AutonomousEpicSpecialForces.RaidEnded("darkness-albion", Loss, sizePercent: 145.3);
                Assert.That(RealmRaidRecord.GetResults("darkness-albion"), Is.EqualTo(new[] { new RaidResult(Loss, false, 145.3) }));
            });
        }

        [Test]
        public void AnEncounterThatStillWinsAtTheSmallestSizeStaysThere()
        {
            // A model where even 70% wins about nine in ten: the size stays at the floor (a rare loss lifts it a step for
            // a raid or two), and the bosses are untouched.
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 40), 1000, 11);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.LessThan(80));
                Assert.That(rate, Is.GreaterThan(0.85), "the floor is a limit, not a promise of 2 in 3");
            });
        }

        [Test]
        public void AnEncounterThatLosesEvenAtTheLargestSizeStaysThere()
        {
            // A model that wins only half the time at 200%: the size stays up (the Special Forces are the help there).
            var (rate, mean, _) = Simulate(p => WinChance(p, half: 200), 1000, 13);
            Assert.Multiple(() =>
            {
                Assert.That(mean, Is.GreaterThan(185));
                Assert.That(rate, Is.InRange(0.4, 0.6));
            });
        }

        // ---- After a restart ----

        [Test]
        public void AutomaticRaidsWaitForTheStartupRamp()
        {
            // 2026-11-04 20:48: Tuscaran Glacier opened two minutes after a restart with full=58, sized from the few level
            // 50s already logged in. Nothing opens until the roster is in (or ten minutes after the ramp's end).
            TimeSpan Minutes(double m) => TimeSpan.FromMinutes(m);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPopulationRamp.Finished(true, 15, Minutes(2), true), Is.False, "two minutes in");
                Assert.That(AutonomousPopulationRamp.Finished(true, 15, Minutes(14.9), true), Is.False, "the ramp is still on");
                Assert.That(AutonomousPopulationRamp.Finished(true, 15, Minutes(15), false), Is.False, "the roster is not all in yet");
                Assert.That(AutonomousPopulationRamp.Finished(true, 15, Minutes(15), true), Is.True);
                Assert.That(AutonomousPopulationRamp.Finished(true, 15, Minutes(25), false), Is.True, "ten minutes after the ramp, regardless");
                Assert.That(AutonomousPopulationRamp.Finished(false, 15, Minutes(0), false), Is.True, "no population: nothing to wait for");
                Assert.That(AutonomousPopulationRamp.Finished(true, 0, Minutes(0), true), Is.True, "no ramp");
                Assert.That(AutonomousPopulationRamp.MinutesLeft(15, Minutes(2)), Is.EqualTo(23));
                Assert.That(AutonomousPopulationRamp.MinutesLeft(15, Minutes(40)), Is.Zero);
                // With the controller not started (tests, tools) nothing waits.
                Assert.That(AutonomousPopulationController.StartupRampFinished, Is.True);
                // Once in, the full size is the period size, not half of a part-logged-in realm.
                Assert.That(RealmRaidRecruitmentPolicy.RequiredBots(116, 100, 0), Is.EqualTo(58), "what 20:48 gave");
                Assert.That(RealmRaidRecruitmentPolicy.RequiredBots(600, 100, 0), Is.EqualTo(100));
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
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.MaximumShift + SiegeDefenseBalance.MaximumAttackerTrim),
                    "6 of 7 captured: defenders at +0.25, and the attackers trimmed both steps (four or more of six captured adds defenders)");
                Assert.That(SiegeDefenseBalance.Ratio(0.75), Is.EqualTo(1.0));
                Assert.That(SiegeDefenseBalance.AttackerCap(128), Is.EqualTo(96));
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
                Is.EqualTo("SIEGE_DEFENSE_BALANCE event=\"captured\" shift=+0.125->+0.25 share=0.75->1 attackerCap=128->128 window=\"CCDC\""));

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
