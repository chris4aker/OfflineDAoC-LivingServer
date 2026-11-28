using System.Diagnostics;
using System.Linq;
using DOL.GS;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The RvR fight rehearsal (#520; docs/rvr-audit-1.2.3/evidence.md F2, F6, 5.2-5.3): three scenarios on a real Region
    /// with real gamebots, their own brains and the server's own handlers, for 120 simulated seconds each. The rows are a
    /// report, printed to the CI log; none is a gate until an owner's PC log agrees with it. What these tests assert is the
    /// harness itself: the fight boards were built from live GameLivings, the services threw nothing, and scenario 3 (its
    /// rolls all the rehearsal's) repeats by seed. Scenarios 1 and 2 are run twice and both rows printed with whether they
    /// matched: the server's own fight code still draws from Random.Shared and from process-wide serials, so a fight
    /// repeats closely but not yet exactly (see the PR for #520). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable, Category("Rehearsal")]
    public sealed class UT_RvrFightRehearsal
    {
        private const int Seed = 520;

        private static void Print(string row, System.Collections.Generic.IEnumerable<string> detail, Stopwatch clock)
        {
            foreach (string line in detail)
                TestContext.Out.WriteLine("  " + line);
            TestContext.Out.WriteLine(row);
            TestContext.Out.WriteLine($"  (wall time {clock.Elapsed.TotalSeconds:0.0}s)");
            TestContext.Progress.WriteLine(row);
        }

        private static void Repeat(string first, string again)
        {
            TestContext.Out.WriteLine(again == first ? "  repeat (same seed): same row" : "  repeat (same seed): differs:");
            if (again != first)
                TestContext.Out.WriteLine(again);
            TestContext.Progress.WriteLine(again == first ? "  repeat: same row" : "  repeat: " + again);
        }

        [Test]
        public void Scenario1_TwoEightsMeetInTheOpen()
        {
            var clock = Stopwatch.StartNew();
            long boards = RvrFightBoards.Builds;
            RvrFightRehearsal.OpenFieldResult first = RvrFightRehearsal.OpenField(Seed);
            Print(first.Row, first.Detail, clock);
            Assert.That(RvrFightBoards.Builds, Is.GreaterThan(boards), "BuildBoard ran on live GameLivings");
            // #531 step 2c-1: the realm-ability row (Speed of Sound, MoC, Bunker of Faith per side) is printed
            string actives = first.Detail.FirstOrDefault(l => l.StartsWith("RVR_REHEARSAL open_field_realm_abilities", System.StringComparison.Ordinal));
            Assert.That(actives, Is.Not.Null, "the realm-ability row");
            // #531 step 2e: the row says how deep each side's casters and healers stood behind their tanks
            Assert.That(first.Row, Does.Contain(" depth=Alb:casters="), "the row has the back line's depth");
            // ... and the layout placed Albion's casters (the Sorcerer and the Theurgist) by their spell reach, deeper than the old
            // flat back-line depth (the reach fill is wired). Their sampled depth is not asserted: a caster walks in to cast
            // (seed 520: mean 151, deepest 1311), kites and peels.
            System.Collections.Generic.IReadOnlyList<float> casterReach = first.Depth.Reaches(eRealm.Albion, false);
            Assert.That(casterReach, Is.Not.Empty, "Albion's casters were sampled: " + first.Row);
            Assert.That(casterReach.Min(), Is.GreaterThan(0), "every sample had a reach: " + first.Row);
            Assert.That(RvrPositioning.DepthFor(false, casterReach.Average(), 0, RvrPositioning.MezSpread),
                Is.GreaterThan(RvrPositioning.MaxBackDepth), "at that reach a caster stands deeper than the old 700: " + first.Row);
            TestContext.Progress.WriteLine(actives);
            RvrFightRehearsal.OpenFieldResult again = RvrFightRehearsal.OpenField(Seed);
            Repeat(first.Row, again.Row);
            Assert.That(first.Errors + again.Errors, Is.Zero, "no service threw");
            // #531 step 1: each eight's opening came from the plan's first pick (not the spread), and the plan opened on no more
            // enemies than the spread pick would have from the same scans.
            Assert.Multiple(() =>
            {
                foreach ((string side, RvrFightRehearsal.RvrOpening opening) in new[] { ("Albion", first.Albion), ("Midgard", first.Midgard) })
                {
                    Assert.That(opening, Is.Not.Null, $"{side} engaged by scan: {first.Row}");
                    if (opening == null)
                        continue;
                    Assert.That(opening.FirstRule, Is.AnyOf("order", "mates", "called"), $"{side}'s first pick is the plan's: {first.Row}");
                    Assert.That(opening.OpenedOn, Is.LessThanOrEqualTo(opening.SpreadOpenedOn), $"{side} opened on fewer: {first.Row}");
                    // Only the kill order rules a tank out: a called pick can be a tank by design (WrongCallShare), and so can
                    // a pick where only a tank was seen.
                    if (opening.FirstRule == "order")
                        Assert.That(RvrRoles.For(RvrFightRehearsal.ClassOf(opening.FirstTarget)), Is.Not.EqualTo(RvrRole.Tank),
                            $"{side} opened on the kill order, not a tank: {first.Row}");
                }
            });
            TestContext.Progress.WriteLine($"  #531 opening: Albion {first.Albion} / Midgard {first.Midgard}");
        }

        [Test]
        public void Scenario2_TwentyFourBotStormOnABattlegroundLord()
        {
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.StormResult first = RvrFightRehearsal.Storm(Seed);
            Print(first.Row, first.Detail, clock);
            RvrFightRehearsal.StormResult again = RvrFightRehearsal.Storm(Seed);
            Repeat(first.Row, again.Row);
            Assert.That(first.Errors + again.Errors, Is.Zero, "no service threw");
        }

        [Test]
        public void Scenario3_APlayerInRangeOfABotGroup()
        {
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.NoticeResult first = RvrFightRehearsal.PlayerNotice(Seed);
            Print(first.Row, first.Detail, clock);
            RvrFightRehearsal.NoticeResult again = RvrFightRehearsal.PlayerNotice(Seed);
            Assert.Multiple(() =>
            {
                Assert.That(again.Row, Is.EqualTo(first.Row), "same seed, same row");
                Assert.That(first.Errors, Is.Zero, "no service threw");
            });
        }
    }
}
