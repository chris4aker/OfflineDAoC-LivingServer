using NUnit.Framework;

namespace DOL.GS.Tests
{
    // The silent pause between a player's whispers (owner 2026-10-30): 1.5 s to a player as before, none to an NPC, so
    // two quick clicks on a quest NPC's [keyword] links both arrive.
    [TestFixture]
    public sealed class UT_WhisperThrottle
    {
        [Test]
        public void AWhisperToAPlayerKeepsTheOldPause()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WhisperThrottle.WindowMs(targetIsPlayer: true), Is.EqualTo(1500));
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: true, sinceLastWhisperMs: 0), Is.True);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: true, sinceLastWhisperMs: 1499), Is.True);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: true, sinceLastWhisperMs: 1500), Is.False);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: true, sinceLastWhisperMs: 60_000), Is.False);
            });
        }

        [Test]
        public void AWhisperToAnNpcIsNeverDroppedInSilence()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WhisperThrottle.WindowMs(targetIsPlayer: false), Is.Zero);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: false, sinceLastWhisperMs: 0), Is.False);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: false, sinceLastWhisperMs: 700), Is.False);
                Assert.That(WhisperThrottle.Drops(targetIsPlayer: false, sinceLastWhisperMs: 1499), Is.False);
            });
        }
    }
}
