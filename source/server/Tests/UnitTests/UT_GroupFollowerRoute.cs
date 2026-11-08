using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Nikse77's follower-stutter guard (b0fcd555, via rambodolphin13/DividedRealms 0996fff6ea): a group follower no
    /// longer takes its own faster route from the follow step every turn.
    /// </summary>
    [TestFixture]
    public sealed class UT_GroupFollowerRoute
    {
        [Test]
        public void OnlyTheLeaderOfAPartyThatIsNotAFormedPveGroupTakesItsOwnRoute() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.RvR, isLeader: true), Is.True);
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.Battleground, isLeader: true), Is.True);
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.RvR, isLeader: false), Is.False, "a warband follower keeps its formation follow");
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.SoloPve, isLeader: false), Is.False);
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.GroupPve, isLeader: true), Is.False, "a formed PvE party walks together");
            Assert.That(AutonomousWorldBotController.MayTakeIndependentRouteWhileFollowing(
                eAutonomousObjectiveKind.GroupPve, isLeader: false), Is.False);
        });
    }
}
