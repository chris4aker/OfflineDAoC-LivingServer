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
                // #599 step 2: the follower group's main assist took the lead group's call (one shared target). The same target alone
                // could match by kill order; CallFromBattlegroup says the follower held it because the lead called it.
                Assert.That(result.SharedCallSeconds, Is.GreaterThan(0), $"the two groups held one shared call: {result.Row}");
                Assert.That(result.BattlegroupCallSeconds, Is.GreaterThan(0), $"the follower took the lead's call: {result.Row}");
                Assert.That(result.Errors, Is.Zero, "no service threw");
            });
        }

        [Test]
        public void Scenario6_ThreeEightsFormTheRealmsZergAndMeetAnEight()
        {
            // #599 step 3: with 72 free Albion gamebots (a third is 24), three eights form one zerg, the far one (12,000 off,
            // beyond the battlegroup's link) included, with one support roster of 24; the Midgard eight does not join. With 26
            // free (a third is 8, under the floor) a battlegroup forms instead. The fight's outcome is printed, not asserted.
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.ZergResult result = RvrFightRehearsal.Zerg(Seed);
            Print(result.Row, result.Detail, clock);
            Assert.Multiple(() =>
            {
                Assert.That(result.ZergId, Does.StartWith("zerg-albion-"), result.Row);
                Assert.That(result.Members, Has.Count.EqualTo(3), "three groups");
                Assert.That(result.LeadForce, Is.EqualTo(result.Members[0]));
                Assert.That(result.FarJoined, Is.True, "recruited beyond the battlegroup's 3,000 link");
                Assert.That(result.MidgardJoined, Is.False, "the lone Midgard eight (8 free) has no zerg and no battlegroup");
                Assert.That(result.RosterSizes, Is.EqualTo(new[] { 24 }), "every Albion bot of the three eights shares one roster of 24");
                Assert.That(result.MidgardInRosters, Is.Zero);
                Assert.That(result.FewerId, Does.StartWith("bgrp-albion-"), "26 free: no zerg, the two near eights make a battlegroup");
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
