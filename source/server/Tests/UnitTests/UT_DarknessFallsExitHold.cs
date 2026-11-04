using NUnit.Framework;

namespace DOL.GS.Tests;

// Nikse77's Darkness Falls exit hold (e9572404, via rambodolphin13): four quick probes, then one a minute, and a bot
// still without a certified exit route after three minutes leaves for its realm's safe capital.
[TestFixture]
public sealed class UT_DarknessFallsExitHold
{
    [TestCase(0, 15_000)]
    [TestCase(3, 15_000)]
    [TestCase(4, 60_000)]
    [TestCase(20, 60_000)]
    public void Probes_SlowDownAfterFourEmptyOnes(int emptyProbes, long expected) =>
        Assert.That(AutonomousWorldBotController.NextExitProbeDelay(emptyProbes), Is.EqualTo(expected));

    [Test]
    public void Hold_EndsAfterThreeMinutes()
    {
        long since = 10_000;
        Assert.That(AutonomousWorldBotController.ExitHoldTooLong(since, since + 3 * 60_000 - 1), Is.False);
        Assert.That(AutonomousWorldBotController.ExitHoldTooLong(since, since + 3 * 60_000), Is.True);
    }

    [Test]
    public void Hold_NeverStarted_NeverMoves() =>
        Assert.That(AutonomousWorldBotController.ExitHoldTooLong(0, long.MaxValue / 2), Is.False);
}
