using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using static DOL.UnitTests.UT_RvrPeelPlan;

namespace DOL.UnitTests
{
    /// <summary>
    /// #531 step 2f: PBAoE bombers on a mezzed pile (RvrBombPlan; owner answer 53A: groups with 2 or more bombers bomb at
    /// once, a group with one bomber walks in and waits). Pure, with world-less gamebots as the living objects. The wiring
    /// (BotBrain.RvrBomb) is pinned by the #520 rehearsal (UT_RvrFightRehearsal: the bomb= values of Scenario1 and its
    /// two-bomber case). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrBombPlan531 : DutyLeaseFixture
    {
        private const long Long = 20_000;

        [Test]
        public void Two_bombers_bomb_the_mezzed_pile_at_once() => Assert.Multiple(() =>
        {
            Assert.That(RvrBombPlan.Decide(2, 3, 0, Long), Is.EqualTo(RvrBombAction.Bomb));
            Assert.That(RvrBombPlan.Decide(3, 2, 1, Long), Is.EqualTo(RvrBombAction.Bomb), "free ones in reach too");
            Assert.That(RvrBombPlan.Decide(2, 2, 0, 100), Is.EqualTo(RvrBombAction.Bomb));
        });

        [Test]
        public void One_bomber_walks_in_and_waits_until_the_mez_is_about_to_wear_off() => Assert.Multiple(() =>
        {
            Assert.That(RvrBombPlan.Decide(1, 3, 0, Long), Is.EqualTo(RvrBombAction.Wait));
            Assert.That(RvrBombPlan.Decide(1, 3, 0, RvrBombPlan.WakeWindowMilliseconds + 1), Is.EqualTo(RvrBombAction.Wait));
            Assert.That(RvrBombPlan.Decide(1, 3, 0, RvrBombPlan.WakeWindowMilliseconds), Is.EqualTo(RvrBombAction.Bomb), "2 s left: bomb");
            Assert.That(RvrBombPlan.Decide(1, 2, 2, 0), Is.EqualTo(RvrBombAction.Bomb));
        });

        [Test]
        public void No_mezzed_pile_keeps_todays_rules() => Assert.Multiple(() =>
        {
            Assert.That(RvrBombPlan.Decide(2, 1, 3, Long), Is.EqualTo(RvrBombAction.None), "one mezzed is no pile");
            Assert.That(RvrBombPlan.Decide(2, 0, 4, long.MaxValue), Is.EqualTo(RvrBombAction.None), "no mez landed: no walk in");
            Assert.That(RvrBombPlan.Decide(0, 3, 0, Long), Is.EqualTo(RvrBombAction.None), "no bomber");
            Assert.That(RvrBombPlan.Decide(1, 0, 0, long.MaxValue), Is.EqualTo(RvrBombAction.None));
        });

        [Test]
        public void A_lone_bomber_waits_out_the_mez_it_finds() => Assert.Multiple(() =>
        {
            Assert.That(RvrBombPlan.WaitFor(60_000), Is.EqualTo(59_000), "a 60 s mez: until 2 s are left, plus the 1 s margin");
            Assert.That(RvrBombPlan.WaitFor(30_000), Is.EqualTo(29_000));
            Assert.That(RvrBombPlan.WaitFor(2_500), Is.EqualTo(1_500));
            Assert.That(RvrBombPlan.WaitFor(1_000), Is.EqualTo(RvrBombPlan.WaitMarginMilliseconds), "at least the margin");
            Assert.That(RvrBombPlan.WaitFor(600_000), Is.EqualTo(RvrBombPlan.MaxWaitMilliseconds), "capped");
            Assert.That(RvrBombPlan.WaitFor(long.MaxValue), Is.EqualTo(RvrBombPlan.MaxWaitMilliseconds));
            Assert.That(RvrBombPlan.MaxWaitMilliseconds, Is.GreaterThan(60_000 - RvrBombPlan.WakeWindowMilliseconds), "a 60 s mez is waited out");
        });

        [Test]
        public void Two_bombers_bomb_together_the_first_holds_a_moment_for_the_other() => Assert.Multiple(() =>
        {
            Assert.That(RvrBombPlan.Hold(1, 0), Is.True, "just arrived, a mate still walking in: hold");
            Assert.That(RvrBombPlan.Hold(2, RvrBombPlan.SyncHoldMilliseconds - 1), Is.True);
            Assert.That(RvrBombPlan.Hold(1, RvrBombPlan.SyncHoldMilliseconds), Is.False, "the hold ran out: bomb");
            Assert.That(RvrBombPlan.Hold(0, 0), Is.False, "everyone in place: bomb");
            Assert.That(RvrBombPlan.SyncHoldMilliseconds, Is.InRange(2_000L, 4_000L), "about one bomb's cast time");
        });

        private static RvrCombatant Enemy(int x, int y, bool mezzed, bool pet = false) =>
            C(Gamebot(realm: eRealm.Midgard), pet ? eCharacterClass.Hunter : eCharacterClass.Healer, x, y, realm: eRealm.Midgard,
                control: mezzed ? RvrControlState.Mezzed : RvrControlState.None, pet: pet);

        [Test]
        public void The_pile_is_the_mezzed_enemy_whose_bomb_reaches_the_most_mezzed()
        {
            var lone = Enemy(400, 0, true);
            var pileA = Enemy(1_000, 0, true);
            var pileB = Enemy(1_100, 50, true);
            var pileC = Enemy(1_050, -80, true);
            var freeNear = Enemy(1_000, 150, false);
            var tooFar = Enemy(1_700, 0, true);
            var list = new List<RvrCombatant> { lone, pileA, pileB, pileC, freeNear, tooFar };
            RvrBombPlan.Pile pile = RvrBombPlan.Choose(list, Vector2.Zero, 350);
            Assert.Multiple(() =>
            {
                Assert.That(pile.Centre, Is.AnyOf(pileA, pileB, pileC), "the three asleep together, not the lone one");
                Assert.That(pile.Mezzed, Is.EqualTo(3));
                Assert.That(pile.Free, Is.EqualTo(1), "the free one beside them is in reach too");
                Assert.That(RvrBombPlan.Choose(new List<RvrCombatant> { lone, freeNear }, Vector2.Zero, 350).Centre, Is.Null,
                    "fewer than two mezzed: no pile");
                Assert.That(RvrBombPlan.Choose(new List<RvrCombatant> { tooFar, Enemy(1_750, 0, true) }, Vector2.Zero, 350).Centre,
                    Is.Null, "beyond the 1,500 walk");
                Assert.That(RvrBombPlan.Choose(null, Vector2.Zero, 350).Centre, Is.Null);
            });
        }

        [Test]
        public void A_pile_that_woke_ends_the_walk() => Assert.Multiple(() =>
        {
            Vector2 centre = new(1_000, 0);
            var asleep = new List<RvrCombatant> { Enemy(1_000, 0, true), Enemy(1_100, 50, true), Enemy(1_050, -80, true) };
            Assert.That(RvrBombPlan.StillAsleep(asleep, centre, 350), Is.True, "three asleep: walk on");
            var oneWoke = new List<RvrCombatant> { Enemy(1_000, 0, true), Enemy(1_100, 50, true), Enemy(1_050, -80, false) };
            Assert.That(RvrBombPlan.StillAsleep(oneWoke, centre, 350), Is.True, "two still asleep: still a pile");
            var woke = new List<RvrCombatant> { Enemy(1_000, 0, true), Enemy(1_100, 50, false), Enemy(1_050, -80, false) };
            Assert.That(RvrBombPlan.StillAsleep(woke, centre, 350), Is.False, "one asleep: the walk ends");
            var allWoke = new List<RvrCombatant> { Enemy(1_000, 0, false), Enemy(1_100, 50, false), Enemy(1_050, -80, false) };
            Assert.That(RvrBombPlan.StillAsleep(allWoke, centre, 350), Is.False, "the first bomb woke them all: the walk ends");
            var wandered = new List<RvrCombatant> { Enemy(1_000, 0, true), Enemy(1_600, 0, true) };
            Assert.That(RvrBombPlan.StillAsleep(wandered, centre, 350), Is.False, "the second sleeper is out of the bomb's reach");
            Assert.That(RvrBombPlan.StillAsleep(null, centre, 350), Is.False);
            // the same count as the pick: a pile Choose would walk to is asleep, and Decide says None once it is not
            Assert.That(RvrBombPlan.Choose(woke, Vector2.Zero, 350).Centre, Is.Null);
            (int mezzed, int free) = RvrBombPlan.Reach(woke, centre, 350);
            Assert.That(RvrBombPlan.Decide(2, mezzed, free, Long), Is.EqualTo(RvrBombAction.None), "two bombers: no bomb on it either");
        });

        [Test]
        public void Reach_counts_inside_the_edge_margin_and_counts_pets()
        {
            var list = new List<RvrCombatant>
            {
                Enemy(0, 0, true), Enemy(300, 0, true), Enemy(0, 305, false, pet: true), Enemy(320, 0, false),
            };
            (int mezzed, int free) = RvrBombPlan.Reach(list, Vector2.Zero, 350);
            Assert.Multiple(() =>
            {
                Assert.That(mezzed, Is.EqualTo(2), "310 reach: 0 and 300");
                Assert.That(free, Is.EqualTo(1), "the pet at 305 is hit; the one at 320 is past the margin");
            });
        }
    }
}
