using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #50 item 11: a companion's items move between its own bag and its paper doll ("/companionui move"), with the
    // same wear rules as "equip" from the player's backpack.
    [TestFixture]
    public class UT_CompanionOwnMove
    {
        private const int Bag1 = 40, Bag2 = 41, Bag10 = 49;
        private const int RightHand = 10, LeftHand = 11, TwoHand = 12, Head = 21, Torso = 25, LeftRing = 35, RightRing = 36;

        private static CompanionWindows165.OwnMove Plan(int from, int to, IEnumerable<int> held, bool canWear = true,
            int wearSlot = Torso, IEnumerable<int> fits = null, bool occupantFits = true, bool alive = true)
        {
            var occupied = new HashSet<int>(held);
            var fitting = new HashSet<int>(fits ?? [wearSlot]);
            return CompanionWindows165.PlanOwnMove(from, to, alive, occupied.Contains, canWear, wearSlot, fitting.Contains,
                _ => occupantFits, "Osbert", "Studded Jerkin");
        }

        [Test]
        public void TheMoveVerbIsRouted() => Assert.That(CompanionWindows165.Verbs, Does.Contain("move"));

        [Test]
        public void BagItemGoesOnTheDroppedSlot() => Assert.Multiple(() =>
        {
            Assert.That(Plan(Bag1, Torso, [Bag1]), Is.EqualTo(new CompanionWindows165.OwnMove(null, Torso, false)));
            Assert.That(Plan(Bag1, Torso, [Bag1, Torso]), Is.EqualTo(new CompanionWindows165.OwnMove(null, Torso, true)),
                "what it wore there swaps into the bag slot it left");
            Assert.That(Plan(Bag1, CompanionWindows165.MoveToWear, [Bag1]).Target, Is.EqualTo(Torso), "a drop on the figure");
        });

        [Test]
        public void BagItemOnTheWrongSlotGoesWhereItBelongsAsEquipDoes()
        {
            CompanionWindows165.OwnMove plan = Plan(Bag1, Head, [Bag1, Torso]);
            Assert.That(plan, Is.EqualTo(new CompanionWindows165.OwnMove(null, Torso, true)));
            // a ring dropped on the other hand's ring slot stays there (FitsChosenSlot allows either side)
            Assert.That(Plan(Bag1, RightRing, [Bag1], wearSlot: LeftRing, fits: [LeftRing, RightRing]).Target, Is.EqualTo(RightRing));
        }

        [Test]
        public void WhatItCannotUseStaysInTheBag() => Assert.Multiple(() =>
        {
            Assert.That(Plan(Bag1, Torso, [Bag1], canWear: false).Problem, Is.EqualTo("Osbert can't use Studded Jerkin."));
            Assert.That(Plan(Bag1, CompanionWindows165.MoveToWear, [Bag1], canWear: false).Target, Is.LessThan(0));
            // an item that can't go into a bag row is still free to move between bag rows
            Assert.That(Plan(Bag1, Bag2, [Bag1], canWear: false), Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag2, false)));
        });

        [Test]
        public void WornItemGoesIntoTheBag() => Assert.Multiple(() =>
        {
            Assert.That(Plan(Torso, Bag2, [Torso]), Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag2, false)));
            Assert.That(Plan(Torso, CompanionWindows165.MoveToBag, [Torso, Bag1]),
                Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag2, false)), "the bag panel: the first free slot");
            Assert.That(Plan(Torso, Bag1, [Torso, Bag1]), Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag1, true)),
                "onto a bag row with a jerkin it can wear: they swap");
            Assert.That(Plan(Torso, Bag1, [Torso, Bag1], occupantFits: false),
                Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag2, false)), "onto arrows: into the first free slot instead");
        });

        [Test]
        public void AFullBagRefusesAnUnequip()
        {
            int[] full = [Torso, .. Enumerable.Range(Bag1, 10)];
            Assert.Multiple(() =>
            {
                Assert.That(Plan(Torso, CompanionWindows165.MoveToBag, full).Problem, Is.EqualTo("Osbert's bag is full (10 slots)."));
                Assert.That(Plan(Torso, Bag10, full, occupantFits: false).Problem, Is.EqualTo("Osbert's bag is full (10 slots)."));
                Assert.That(Plan(Torso, Bag10, full).Swap, Is.True, "a swap needs no free slot");
                Assert.That(Plan(Bag1, Torso, full).Swap, Is.True, "nor does putting one on");
            });
        }

        [Test]
        public void BagRowsSwap() =>
            Assert.That(Plan(Bag1, Bag10, [Bag1, Bag10]), Is.EqualTo(new CompanionWindows165.OwnMove(null, Bag10, true)));

        [Test]
        public void HandToHandFollowsTheSlotRules() => Assert.Multiple(() =>
        {
            Assert.That(Plan(RightHand, LeftHand, [RightHand], wearSlot: RightHand, fits: [RightHand, LeftHand]),
                Is.EqualTo(new CompanionWindows165.OwnMove(null, LeftHand, false)), "a dual wielder's weapon to the left hand");
            Assert.That(Plan(RightHand, LeftHand, [RightHand], wearSlot: RightHand, fits: [RightHand]).Problem,
                Is.EqualTo("Studded Jerkin doesn't go there."), "a class with no left-hand weapon");
            Assert.That(Plan(RightHand, TwoHand, [RightHand], wearSlot: RightHand, fits: [RightHand]).Problem,
                Is.EqualTo("Studded Jerkin doesn't go there."), "a one-hander is not a two-hander");
            Assert.That(Plan(LeftRing, RightRing, [LeftRing, RightRing], wearSlot: LeftRing, fits: [LeftRing, RightRing]),
                Is.EqualTo(new CompanionWindows165.OwnMove(null, RightRing, true)), "rings trade sides");
            Assert.That(Plan(RightHand, LeftHand, [RightHand, LeftHand], wearSlot: RightHand, fits: [RightHand, LeftHand],
                occupantFits: false).Problem, Is.EqualTo("Those two can't trade places."), "a shield can't take the main hand");
        });

        [Test]
        public void NothingToDoSendsNothingBack() => Assert.Multiple(() =>
        {
            foreach (CompanionWindows165.OwnMove plan in new[]
                     {
                         Plan(Bag1, Bag1, [Bag1]), Plan(Torso, Torso, [Torso]),
                         Plan(Bag1, CompanionWindows165.MoveToBag, [Bag1]), Plan(Torso, CompanionWindows165.MoveToWear, [Torso]),
                     })
            {
                Assert.That(plan.Problem, Is.Null);
                Assert.That(plan.Target, Is.LessThan(0));
            }
        });

        [Test]
        public void RefusedMoves() => Assert.Multiple(() =>
        {
            Assert.That(Plan(Bag1, Torso, [Bag1], alive: false).Problem, Is.EqualTo("Revive Osbert first."));
            Assert.That(Plan(Bag2, Torso, [Bag1]).Problem, Is.EqualTo("Nothing there."));
            Assert.That(Plan(60, Torso, [60]).Problem, Is.EqualTo("Drag one of Osbert's items."), "not its 10-slot bag");
            Assert.That(Plan(14, Bag1, [14]).Problem, Is.EqualTo("Drag one of Osbert's items."), "a quiver: not on the doll");
            Assert.That(Plan(Bag1, 60, [Bag1]).Problem, Is.EqualTo("Drop it on Osbert's gear or bag."));
            Assert.That(Plan(Bag1, int.MinValue, [Bag1]).Problem, Is.EqualTo("Drop it on Osbert's gear or bag."));
            Assert.That(Plan(Bag1, Torso, [Bag1], wearSlot: 0, fits: []).Problem, Is.EqualTo("Osbert can't use Studded Jerkin."));
        });
    }
}
