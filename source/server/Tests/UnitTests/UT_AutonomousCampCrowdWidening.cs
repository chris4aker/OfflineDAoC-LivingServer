using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-10 "camps": busy-sibling memory and the one-step crowd widening for solo bots.</summary>
    [TestFixture]
    public class UT_AutonomousCampCrowdWidening
    {
        private static readonly string[] NoBusy = Array.Empty<string>();

        [Test]
        public void WidenedCeilingStepsOnceAndNeverReachesTheConItDiedTo()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.GREEN, ConColor.YELLOW, null), Is.EqualTo(ConColor.BLUE));
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.BLUE, ConColor.YELLOW, null), Is.EqualTo(ConColor.YELLOW));
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.YELLOW, ConColor.YELLOW, null), Is.Null, "at the natural ceiling");
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.GREEN, ConColor.YELLOW, ConColor.BLUE), Is.Null, "died to blue");
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.GREEN, ConColor.YELLOW, ConColor.GREEN), Is.Null, "died to green");
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.GREEN, ConColor.YELLOW, ConColor.YELLOW), Is.EqualTo(ConColor.BLUE));
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.BLUE, ConColor.YELLOW, ConColor.YELLOW), Is.Null);
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.GREEN, ConColor.BLUE, null), Is.EqualTo(ConColor.BLUE), "Forester");
                Assert.That(AutonomousCampCrowdWidening.WidenedCeiling(ConColor.BLUE, ConColor.BLUE, null), Is.Null, "Forester at its ceiling");
            });
        }

        [Test]
        public void OnlyACrowdedSoloOutdoorBotOfLevelEightUnderADeathLimitWidens()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, 5, 0, ConColor.GREEN, ConColor.YELLOW), Is.True);
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 7, 5, 0, ConColor.GREEN, ConColor.YELLOW), Is.False, "level 7");
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(false, true, 10, 5, 0, ConColor.GREEN, ConColor.YELLOW), Is.False, "grouped");
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, false, 10, 5, 0, ConColor.GREEN, ConColor.YELLOW), Is.False, "dungeon");
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, 5, 1, ConColor.GREEN, ConColor.YELLOW), Is.False, "a quiet camp");
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, 5, 0, ConColor.YELLOW, ConColor.YELLOW), Is.False, "no death limit");
                Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, 0, 0, ConColor.GREEN, ConColor.YELLOW), Is.False,
                    "no allowed camp at all: the death fallback handles it");
                Assert.That(AutonomousCampCrowdWidening.MinimumLevel, Is.EqualTo(8));
            });
        }

        [Test]
        public void PickWidenedTakesOnlyQuietWiderCampsAndOnlyWhenNoStrictCampIsQuiet()
        {
            // a camp is "quiet" here when its name ends in q
            Func<string[], string[]> quiet = set => set.Where(id => id.EndsWith("q")).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampCrowdWidening.PickWidened(new[] { "g1", "g2q" }, new[] { "b1q" }, quiet), Is.Null, "a quiet green camp");
                Assert.That(AutonomousCampCrowdWidening.PickWidened(new[] { "g1", "g2" }, new[] { "b1", "b2q", "b3", "b4q", "b5" }, quiet),
                    Is.EqualTo(new[] { "b2q", "b4q" }));
                Assert.That(AutonomousCampCrowdWidening.PickWidened(new[] { "g1", "g2" }, new[] { "b1", "b2" }, quiet), Is.Null,
                    "all crowded: today's least-crowded fallback");
                Assert.That(AutonomousCampCrowdWidening.PickWidened(new[] { "g1" }, Array.Empty<string>(), quiet), Is.Null);
            });
        }

        [Test]
        public void BusySiblingsAreSkippedButTheListIsNeverEmptied()
        {
            Func<string, string, bool> same = (a, b) => a.Split(':')[0] == b.Split(':')[0];
            string[] camps = { "wolf:1", "wolf:2", "badger:1" };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampCrowdWidening.AvoidBusySiblings(camps, new[] { "wolf:9" }, c => c, same),
                    Is.EqualTo(new[] { "badger:1" }));
                string[] allWolves = { "wolf:1", "wolf:2" };
                Assert.That(AutonomousCampCrowdWidening.AvoidBusySiblings(allWolves, new[] { "wolf:9" }, c => c, same),
                    Is.SameAs(allWolves), "every camp is a sibling: keep them all");
                Assert.That(AutonomousCampCrowdWidening.AvoidBusySiblings(camps, NoBusy, c => c, same), Is.SameAs(camps));
            });
        }

        [Test]
        public void BusyMemoryExpiresAfterTenMinutesAndHoldsAtMostSix()
        {
            var busy = new List<(string CampId, long Until)>();
            for (int i = 0; i < 8; i++)
                AutonomousCampCrowdWidening.RememberBusy(busy, "camp" + i, 1_000);
            Assert.That(busy, Has.Count.EqualTo(AutonomousCampCrowdWidening.MaxBusyCamps));
            Assert.That(AutonomousCampCrowdWidening.ActiveBusy(busy, 1_000), Does.Contain("camp7").And.Not.Contain("camp0"));
            AutonomousCampCrowdWidening.RememberBusy(busy, "camp7", 2_000);
            Assert.That(busy.Count(entry => entry.CampId == "camp7"), Is.EqualTo(1), "re-remembering refreshes, never duplicates");
            Assert.That(AutonomousCampCrowdWidening.ActiveBusy(busy, 1_000 + AutonomousCampCrowdWidening.BusyMilliseconds),
                Is.EqualTo(new[] { "camp7" }), "the rest expired");
            Assert.That(AutonomousCampCrowdWidening.ActiveBusy(busy, 3_000 + AutonomousCampCrowdWidening.BusyMilliseconds), Is.Empty);
        }

        [Test]
        public void CrowdedGreenCampsWidenToTheQuietBlueCampOnly()
        {
            const string green1 = "t1110:widen:200:lough wolf:1", green2 = "t1110:widen:200:young badger:1",
                blue = "t1110:widen:200:badger:1";
            var camps = new (string Id, int Mobs)[] { (green1, 8), (green2, 8), (blue, 8) };
            try
            {
                AutonomousCampCompetition.PublishCatalogWithPools([
                    (green1, 200, 100_000, 100_000, "lough wolf", 8, false, 8),
                    (green2, 200, 200_000, 100_000, "young badger", 8, false, 8),
                    (blue, 200, 300_000, 100_000, "badger", 8, false, 8)]);
                AutonomousCampCompetition.NoteAssigned(green1, 6);
                AutonomousCampCompetition.NoteAssigned(green2, 6);
                Func<(string Id, int Mobs)[], (string Id, int Mobs)[]> quiet =
                    set => AutonomousCampCompetition.Quiet(set, c => c.Id, c => c.Mobs);
                (string Id, int Mobs)[] strict = camps.Take(2).ToArray();
                (string Id, int Mobs)[] wider = camps.Skip(2).ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(quiet(strict), Is.Empty, "both green camps crowded");
                    Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, strict.Length, quiet(strict).Length,
                        ConColor.GREEN, ConColor.YELLOW), Is.True);
                    Assert.That(AutonomousCampCrowdWidening.PickWidened(strict, wider, quiet)?.Select(c => c.Id),
                        Is.EqualTo(new[] { blue }));
                    Assert.That(AutonomousCampCrowdWidening.ShouldWiden(true, true, 10, strict.Length, quiet(strict).Length,
                        ConColor.YELLOW, ConColor.YELLOW), Is.False, "no death steps: no widening");
                });
            }
            finally
            {
                AutonomousCampCompetition.PublishCatalog([]);
            }
        }

        [Test]
        public void TheSummaryLineComesAtMostOnceEveryTenMinutes()
        {
            AutonomousCampCrowdWidening.ResetForTests();
            try
            {
                Assert.That(AutonomousCampCrowdWidening.TryTakeSummary(1_000, out _), Is.False, "the first call arms the timer");
                AutonomousCampCrowdWidening.CountWidened(eRealm.Hibernia);
                AutonomousCampCrowdWidening.CountBlockedDiedTo(eRealm.Albion);
                Assert.That(AutonomousCampCrowdWidening.TryTakeSummary(1_000 + 5 * 60_000, out _), Is.False);
                Assert.That(AutonomousCampCrowdWidening.TryTakeSummary(1_000 + 10 * 60_000, out string line), Is.True);
                Assert.That(line, Does.Contain("hib: strict_quiet=0 widened=1").And.Contain("alb: strict_quiet=0 widened=0 crowded_fallback=0 blocked_died_to=1"));
                Assert.That(AutonomousCampCrowdWidening.TryTakeSummary(1_000 + 11 * 60_000, out _), Is.False);
                Assert.That(AutonomousCampCrowdWidening.TryTakeSummary(1_000 + 20 * 60_000, out line), Is.True);
                Assert.That(line, Does.Contain("hib: strict_quiet=0 widened=0"), "counts reset after each summary");
            }
            finally
            {
                AutonomousCampCrowdWidening.ResetForTests();
            }
        }

        [Test]
        public void BothSwitchesDefaultOn()
        {
            Assert.That(AutonomousCampCrowdWidening.BusySiblingMemory, Is.True);
            Assert.That(AutonomousCampCrowdWidening.WidenWhenCrowded, Is.True);
        }
    }
}
