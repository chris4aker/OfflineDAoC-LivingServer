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
        public void Scenario4_SoloBotsAtABattlegroundMusterFormGroups()
        {
            // #530 step 5b: the muster's solo gamebots are put into parties, and each party's fight board lists the party.
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.MusterResult result = RvrFightRehearsal.MusterGroups(Seed);
            Print(result.Row, result.Detail, clock);
            Assert.Multiple(() =>
            {
                Assert.That(result.Placed, Is.EqualTo(14), "every free solo at the muster placed");
                Assert.That(result.PartySizes, Is.EqualTo(new[] { 7, 7 }), "fourteen: two parties of seven, never over eight");
                Assert.That(result.OutsidersTaken, Is.Zero, "no other realm, no grouped bot, no other bracket, no tour about to end");
                Assert.That(result.Solos, Is.Zero);
                Assert.That(result.BoardMembersBefore, Is.LessThanOrEqualTo(1), "before: a solo bot's board");
                Assert.That(result.BoardMembersAfter, Is.EqualTo(new[] { 7, 7 }), "after: the party's board");
                Assert.That(result.Errors, Is.Zero, "no service threw");
            });
        }

        [Test]
        public void Scenario5_TwoEightsLinkIntoABattlegroupAndMeetAnEight()
        {
            // #599 step 1: two nearby Albion eights link (16 heads, one support roster); a pair, a far eight and the other
            // realm do not. The fight's outcome is printed, not asserted.
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.BattlegroupResult result = RvrFightRehearsal.Battlegroup(Seed);
            Print(result.Row, result.Detail, clock);
            Assert.Multiple(() =>
            {
                Assert.That(result.BattlegroupId, Does.StartWith("bgrp-albion-"), "the two eights linked");
                Assert.That(result.Linked, Has.Count.EqualTo(2), "two groups");
                Assert.That(result.LeadForce, Is.EqualTo(result.Linked[0]));
                Assert.That(result.PairLinked, Is.False, "a duo stays a duo");
                Assert.That(result.FarLinked, Is.False, "an eight 9,000 away is not linked");
                Assert.That(result.MidgardLinked, Is.False, "the lone Midgard eight has no battlegroup");
                Assert.That(result.RosterSizes, Is.EqualTo(new[] { 16 }), "every Albion bot of the two eights shares one roster of 16");
                Assert.That(result.MidgardInRosters, Is.Zero);
                Assert.That(result.Errors, Is.Zero, "no service threw");
            });
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
