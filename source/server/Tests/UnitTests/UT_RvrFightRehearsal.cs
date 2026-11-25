using System.Diagnostics;
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
            RvrFightRehearsal.OpenFieldResult again = RvrFightRehearsal.OpenField(Seed);
            Repeat(first.Row, again.Row);
            Assert.That(first.Errors + again.Errors, Is.Zero, "no service threw");
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
