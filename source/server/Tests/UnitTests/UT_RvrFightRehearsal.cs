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
            // #531 step 2f: the row says what each side's bombers did with a mezzed pile
            Assert.That(first.Row, Does.Contain(" bomb=Alb:["), "the row has the bombers");
            // (seed 520's eights hold no Wizard, Runemaster or Spiritmaster: the bomb cases below give Albion its bombers)
            Assert.That(BombOf(first.Row, "Alb").Bombers, Is.Zero, "no period bomber in Albion's eight: " + first.Row);
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

        /// <summary>One side's bomb= part of the open_field row.</summary>
        private sealed record BombText(int Bombers, string Waited, int Bombs, int OnMezzed);

        private static BombText BombOf(string row, string side)
        {
            var m = System.Text.RegularExpressions.Regex.Match(row,
                $@"\b{side}:\[bombers=(\d+)(?: first=\S+ waited=(\S+) bombs=(\d+) on_mezzed=(\d+))?\]");
            Assert.That(m.Success, Is.True, $"{side}'s bomb= part: {row}");
            return new BombText(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? m.Groups[2].Value : "no",
                m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0, m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        }

        [Test]
        public void Scenario1_ALoneBomberWaitsOutTheMezThenBombsThePile()
        {
            // #531 step 2f, owner answer 53A: a group with one bomber walks in and waits beside the mezzed pile, and bombs as
            // the first mez there is about to wear off. Albion fights with one bomber beside its Sorcerer's area mez. This pins
            // the plan's walk-in (BotBrain.TryRvrBomb) and its lift of the mez veto.
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.OpenFieldResult result = RvrFightRehearsal.OpenField(Seed, albionBombers: 1);
            Print(result.Row, result.Detail, clock);
            BombText alb = BombOf(result.Row, "Alb");
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Zero, "no service threw");
                Assert.That(alb.Bombers, Is.EqualTo(1), "Albion has one bomber: " + result.Row);
                Assert.That(alb.Waited, Is.Not.EqualTo("no"), "the lone bomber waited beside a mezzed pile: " + result.Row);
                Assert.That(alb.OnMezzed, Is.GreaterThanOrEqualTo(1), "and bombed it: " + result.Row);
            });
        }

        [Test]
        public void Scenario1_TwoBombersBombAMezzedPileTogether()
        {
            // #531 step 2f, owner answer 53A: a group with two or more bombers bombs the mezzed pile at once. Albion fights
            // with two bombers beside its Sorcerer's area mez.
            var clock = Stopwatch.StartNew();
            RvrFightRehearsal.OpenFieldResult result = RvrFightRehearsal.OpenField(Seed, albionBombers: 2);
            Print(result.Row, result.Detail, clock);
            BombText alb = BombOf(result.Row, "Alb");
            var albion = result.Bombs.Where(b => b.Caster.Realm == eRealm.Albion).ToList();
            RvrBombCast pile = albion.FirstOrDefault(b => b.Mezzed >= RvrBombPlan.MinimumMezzed);
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Zero, "no service threw");
                Assert.That(alb.Bombers, Is.EqualTo(2), "Albion has two bombers: " + result.Row);
                Assert.That(alb.Waited, Is.EqualTo("no"), "two bombers never wait for the mez to wear off: " + result.Row);
                Assert.That(pile, Is.Not.Null, "Albion bombed a mezzed pile: " + result.Row);
                if (pile == null)
                    return;
                // the other bomber's bomb starts with it: the first to arrive held for it (a moment at most)
                long window = RvrBombPlan.SyncHoldMilliseconds + 1_000;
                Assert.That(albion.Any(b => b.Caster != pile.Caster && System.Math.Abs(b.At - pile.At) <= window), Is.True,
                    $"both bombers bombed together (within {window} ms): " +
                    string.Join(", ", albion.Select(b => $"{b.At - albion[0].At}:{b.Caster.Name}x{b.Mezzed}m{b.Free}f")) + " " + result.Row);
            });
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
