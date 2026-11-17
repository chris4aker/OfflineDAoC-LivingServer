using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people for 1.2, work package 5: the /stick line. Who follows whom, the gaps and the line's length, the lost
    /// stick and where a member aims. The same for all tiers.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotStickLine
    {
        [Test]
        public void Each_member_follows_the_one_above_it_and_the_first_follows_the_leader() => Assert.Multiple(() =>
        {
            Assert.That(BotStickLine.Order([true, true, true, true], 0), Is.EqualTo(new[] { -1, 0, 1, 2 }));
            Assert.That(BotStickLine.Order([true, false, true, true], 0), Is.EqualTo(new[] { -1, -1, 0, 2 }),
                "a member off the line is skipped: the next one follows the one above it on the line");
            Assert.That(BotStickLine.Order([true, true, true, true], 2), Is.EqualTo(new[] { 2, 0, -1, 1 }),
                "the leader heads the line wherever it stands in the window");
            Assert.That(BotStickLine.Order([true, true], -1), Is.EqualTo(new[] { -1, -1 }), "no leader, no line");
            Assert.That(BotStickLine.Followers(BotStickLine.Order([true, true, false, true], 0)), Is.EqualTo(2));
        });

        [Test]
        public void Gaps_are_eighty_to_two_hundred_and_the_whole_line_fits_the_pace_rule() => Assert.Multiple(() =>
        {
            var gaps = Enumerable.Range(1, 10_000).Select(id => BotStickLine.Gap(id, 3)).ToArray();
            Assert.That(gaps.Min(), Is.GreaterThanOrEqualTo(BotStickLine.MinimumGap));
            Assert.That(gaps.Max(), Is.LessThanOrEqualTo(BotStickLine.MaximumGap));
            Assert.That(gaps.Average(), Is.EqualTo(140).Within(3));
            Assert.That(BotStickLine.Gap(99, 3), Is.EqualTo(BotStickLine.Gap(99, 3)), "the same member, the same gap");
            // Seven followers: the line stays inside 900, well inside AutonomousGroupPace's 1,500.
            for (long first = 1; first < 2_000; first += 7)
            {
                float line = Enumerable.Range(0, 7).Sum(i => BotStickLine.Gap(first + i, 7));
                Assert.That(line, Is.LessThanOrEqualTo(BotStickLine.LineCap + 0.01f));
                Assert.That(line, Is.LessThan(AutonomousGroupPace.TrailRadius));
            }
            Assert.That(Enumerable.Range(1, 500).Select(id => BotStickLine.Gap(id, 20)).Min(), Is.EqualTo(BotStickLine.MinimumGap),
                "never under eighty");
        });

        [Test]
        public void One_leg_in_fifty_loses_stick_for_three_to_ten_seconds() => Assert.Multiple(() =>
        {
            int lost = Enumerable.Range(0, 100_000).Count(i => BotStickLine.LoseStick(i / 100_000.0));
            Assert.That(lost / 100_000.0, Is.EqualTo(1.0 / 50).Within(0.0001));
            Assert.That(BotStickLine.LostMilliseconds(0), Is.EqualTo(3_000));
            Assert.That(BotStickLine.LostMilliseconds(0.999999), Is.EqualTo(10_000).Within(1));
            Assert.That(BotStickLine.LegMilliseconds, Is.EqualTo(10_000), "a roll each 10 s of travel: a member about every 8 minutes");
        });

        [Test]
        public void A_standing_member_waits_until_the_one_ahead_is_past_its_gap() => Assert.Multiple(() =>
        {
            Vector2 self = new(0, 0), forward = new(0, 1);
            var inside = BotStickLine.AimFor(self, new(0, 100), forward, 200, 120, running: false);
            Assert.That(inside.Run, Is.False);
            Assert.That(inside.Point, Is.EqualTo(self), "it stands where it is");
            var past = BotStickLine.AimFor(self, new(0, 130), forward, 200, 120, running: false);
            Assert.That(past.Run, Is.True, "a /stick: it runs once the distance is past its gap");
            Assert.That(past.Point.Y, Is.EqualTo(130 + 200 * BotStickLine.LookaheadSeconds).Within(0.01),
                "aimed ahead of the one it follows, so it never arrives and stands between checks");
            Assert.That(past.Behind, Is.EqualTo(10).Within(0.01));
        });

        [Test]
        public void A_running_member_stops_only_well_inside_its_gap() => Assert.Multiple(() =>
        {
            Vector2 self = new(0, 0), forward = new(0, 1);
            Assert.That(BotStickLine.AimFor(self, new(0, 100), forward, 200, 120, running: true).Run, Is.True, "no stop-go at the gap");
            Assert.That(BotStickLine.AimFor(self, new(0, 71), forward, 200, 120, running: true).Run, Is.False, "closer than 60%");
            var standing = BotStickLine.AimFor(self, new(0, 500), Vector2.Zero, 0, 120, running: true);
            Assert.That(standing.Point, Is.EqualTo(new Vector2(0, 500)), "the one ahead stands: aim at it");
            Assert.That(standing.Behind, Is.EqualTo(380).Within(0.01), "the catch-up is measured from the gap");
        });
    }
}
