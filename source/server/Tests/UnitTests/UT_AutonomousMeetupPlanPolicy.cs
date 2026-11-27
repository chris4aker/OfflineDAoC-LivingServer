using System;
using DOL.GS;
using NUnit.Framework;

// 1.2.3 (#569 step 3): ported from zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze).
namespace DOL.UnitTests
{
    /// <summary>A forming party meets at a town every member can reach in time; the wait is 15-20 min (owner 2026-11-10).</summary>
    [TestFixture]
    public class UT_AutonomousMeetupPlanPolicy
    {
        [Test]
        public void EveryMemberWithinThreeMinutesMoves() =>
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([1, 2, 3], [10]), Is.True);

        [Test]
        public void SlightlyFartherThanTheCurrentTownMoves() =>
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([6, 1], [5, 1]), Is.True, "6 <= 5 + 2 slack");

        [Test]
        public void MuchFartherThanTheCurrentTownKeeps() =>
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([12, 1], [2, 1]), Is.False);

        [Test]
        public void AnyUnreachableMemberKeeps()
        {
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([1, 9999], [20]), Is.False);
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([1, double.NaN], [20]), Is.False);
        }

        [Test]
        public void NoEstimatesKeeps()
        {
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([], [1]), Is.False);
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous(null, [1]), Is.False);
        }

        [Test]
        public void WithoutCurrentEstimatesOnlyTheThreeMinuteCapApplies()
        {
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([2.5], Array.Empty<double>()), Is.True);
            Assert.That(AutonomousMeetupPlanPolicy.ShouldMoveRendezvous([4], Array.Empty<double>()), Is.False);
        }

        [Test]
        public void FarthestIsTheLargestOrMinusOne()
        {
            Assert.That(AutonomousMeetupPlanPolicy.Farthest([3, 7, 1]), Is.EqualTo(7));
            Assert.That(AutonomousMeetupPlanPolicy.Farthest(Array.Empty<double>()), Is.EqualTo(-1));
            Assert.That(AutonomousMeetupPlanPolicy.Farthest(null), Is.EqualTo(-1));
        }

        [TestCase(0, 15)]
        [TestCase(-1, 15)]
        [TestCase(4, 15)]
        [TestCase(5, 18)]
        [TestCase(6, 20)]
        [TestCase(60, 20)]
        [TestCase(9999, 20)]
        public void MeetupWindowIsFifteenToTwentyMinutes(double farthestMinutes, int expectedMinutes) =>
            Assert.That(AutonomousMeetupPlanPolicy.MeetupWindowMs(farthestMinutes), Is.EqualTo(expectedMinutes * 60_000L));

        [Test]
        public void NaNWindowIsFifteenMinutes() =>
            Assert.That(AutonomousMeetupPlanPolicy.MeetupWindowMs(double.NaN), Is.EqualTo(15 * 60_000L));
    }
}
