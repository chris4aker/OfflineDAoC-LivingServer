using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>A gamebot party moves on when its camp bonus runs dry (from Offline DAoC True 1.65, shadowofze).</summary>
    [TestFixture]
    public class UT_GroupDryCampBonus
    {
        [Test]
        public void APartyMovesOnAfterSeveralDryCampBonusKills() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.IsDryCampKill(0.98), Is.False, "a fresh camp");
            Assert.That(AutonomousBotGroupCoordinator.IsDryCampKill(0.30), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.IsDryCampKill(0.20), Is.True, "less than a quarter of the bonus left");
            int streak = 0;
            foreach (double paid in new[] { 0.2, 0.2, 0.9, 0.2, 0.2, 0.2, 0.2 })
                streak = AutonomousBotGroupCoordinator.NextDryCampStreak(streak, AutonomousBotGroupCoordinator.IsDryCampKill(paid));
            Assert.That(streak, Is.EqualTo(4), "a kill with bonus left resets the streak");
            Assert.That(AutonomousBotGroupCoordinator.MovesOnFromDryCamp(streak), Is.False);
            streak = AutonomousBotGroupCoordinator.NextDryCampStreak(streak, true);
            Assert.That(AutonomousBotGroupCoordinator.MovesOnFromDryCamp(streak), Is.True, "five dry kills in a row");
        });
    }
}
