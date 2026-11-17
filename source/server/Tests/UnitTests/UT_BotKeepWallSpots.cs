using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people for 1.2, work package 5: sitting on keep walls (owner decision 11, 2026-11-17). Only at a keep the realm
    /// holds, with no siege there and no enemy noticed; committed defenders and siege fighters keep today's rules; the spot
    /// is a guard archer's floor on the wall near where the bot waits. The same for all tiers.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotKeepWallSpots
    {
        private static BotKeepWallSpots.Candidate Archer(float x, float y, float rise = 200) => new(new(x, y, 500 + rise), true, rise);

        [Test]
        public void Walls_only_at_a_held_keep_with_no_siege_and_no_enemy_noticed() => Assert.Multiple(() =>
        {
            Assert.That(BotKeepWallSpots.Allowed(true, false, false, false, false), Is.True);
            Assert.That(BotKeepWallSpots.Allowed(false, false, false, false, false), Is.False, "an enemy keep");
            Assert.That(BotKeepWallSpots.Allowed(true, true, false, false, false), Is.False, "a siege is on there");
            Assert.That(BotKeepWallSpots.Allowed(true, false, true, false, false), Is.False, "an enemy is noticed");
            Assert.That(BotKeepWallSpots.Allowed(true, false, false, true, false), Is.False, "a committed defender keeps today's rules");
            Assert.That(BotKeepWallSpots.Allowed(true, false, false, false, true), Is.False, "a siege fighter keeps today's rules");
        });

        [Test]
        public void A_wall_spot_is_a_guard_archers_floor_above_the_gate_near_the_bot() => Assert.Multiple(() =>
        {
            Vector2 anchor = new(0, 0);
            var none = new List<Vector2>();
            Assert.That(BotKeepWallSpots.Choose([Archer(100, 0)], anchor, none, 0.5), Is.EqualTo(0));
            Assert.That(BotKeepWallSpots.Choose([new(new(100, 0, 700), false, 200)], anchor, none, 0.5), Is.EqualTo(-1),
                "a lord or a fighter guard stands in the courtyard or at the gate, not on the wall");
            Assert.That(BotKeepWallSpots.Choose([Archer(100, 0, rise: 40)], anchor, none, 0.5), Is.EqualTo(-1),
                "an archer on the ground is not on the wall");
            Assert.That(BotKeepWallSpots.Choose([Archer(450, 0)], anchor, none, 0.5), Is.EqualTo(-1),
                "too far from where it waits: the group stays together");
            Assert.That(BotKeepWallSpots.Choose([Archer(100, 0)], anchor, [new Vector2(120, 10)], 0.5), Is.EqualTo(-1),
                "someone already sits there");
            Assert.That(BotKeepWallSpots.Choose([], anchor, none, 0.5), Is.EqualTo(-1));
            Assert.That(BotKeepWallSpots.Choose(null, anchor, none, 0.5), Is.EqualTo(-1));
        });

        [Test]
        public void Bots_spread_along_the_wall()
        {
            var candidates = new[] { Archer(100, 0), new(new(0, 50, 700), false, 200), Archer(0, 150), Archer(-200, 0), Archer(900, 0) };
            var picks = Enumerable.Range(0, 100).Select(i => BotKeepWallSpots.Choose(candidates, Vector2.Zero, [], i / 100.0)).ToArray();
            Assert.That(picks.Distinct().OrderBy(i => i), Is.EqualTo(new[] { 0, 2, 3 }), "every usable spot, nothing else");
            Assert.That(picks.Count(i => i == 0), Is.EqualTo(34).Within(1));
        }

        [Test]
        public void The_rise_is_measured_from_the_lowest_gate_or_the_keep() => Assert.Multiple(() =>
        {
            Assert.That(BotKeepWallSpots.Rise(800, 600, 500), Is.EqualTo(200));
            Assert.That(BotKeepWallSpots.Rise(800, null, 500), Is.EqualTo(300), "a keep with no gate");
        });

        [Test]
        public void A_bot_on_the_wall_stands_within_a_second_of_noticing_an_enemy() => Assert.Multiple(() =>
        {
            Assert.That(BotKeepWallSpots.StandDelay(0), Is.EqualTo(300));
            Assert.That(BotKeepWallSpots.StandDelay(0.999999), Is.EqualTo(1_000).Within(1));
            Assert.That(BotKeepWallSpots.StandDelay(5), Is.LessThanOrEqualTo(1_000));
            Assert.That(BotKeepWallSpots.WatchRadius, Is.GreaterThanOrEqualTo(2_000), "it notices well before an enemy reaches the wall");
            Assert.That(BotKeepWallSpots.MaxFromAnchor, Is.LessThan(500), "inside the group's cohesion radius");
        });
    }
}
