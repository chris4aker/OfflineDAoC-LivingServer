using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Fixes found while reviewing other Offline DAoC forks on 2026-10-27, and issue #25.
    /// </summary>
    [TestFixture]
    public class UT_ForkPicksOct27
    {
        // Found by stefanrows/OfflineDAoC 0.345.0: before the first pass, now - long.MinValue overflowed and the live clock
        // always returned early.
        [Test]
        public void TheFirstLiveSiegeExpiryPassRuns()
        {
            Assert.That(AutonomousRvrEventLayer.ExpireThrottled(1_000_000, long.MinValue), Is.False);
            Assert.That(AutonomousRvrEventLayer.ExpireThrottled(0, long.MinValue), Is.False);
        }

        [Test]
        public void LaterSiegeExpiryPassesAreThrottledToTwiceASecond()
        {
            long interval = AutonomousRvrEventLayer.ExpireIntervalMilliseconds;
            Assert.That(AutonomousRvrEventLayer.ExpireThrottled(1_000_100, 1_000_000), Is.True);
            Assert.That(AutonomousRvrEventLayer.ExpireThrottled(1_000_000 + interval, 1_000_000), Is.False);
            Assert.That(AutonomousRvrEventLayer.ExpireThrottled(999_000, 1_000_000), Is.False, "a clock behind the last pass runs");
        }

        // From stefanrows/OfflineDAoC 714a9e20 via rambodolphin13 88e7414: a cure mezz on a still-mezzed mate was stopped
        // and restarted every turn.
        [Test]
        public void AHealerLetsTheSameSpellFinishInsteadOfRestartingIt()
        {
            object mate = new(), other = new();
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(true, 4321, 4321, mate, mate), Is.True);
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(true, 1234, 4321, mate, mate), Is.False, "a different spell may replace it");
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(true, 4321, 4321, mate, other), Is.False, "the same heal for someone else replaces it");
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(false, 4321, 4321, mate, mate), Is.False);
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(true, null, 4321, mate, mate), Is.False);
            Assert.That(DOL.AI.Brain.BotBrain.IsAlreadyCastingSpell(true, 4321, 4321, null, null), Is.False);
        }

        // From rambodolphin13/OfflineDAoC 092701e: with nothing to sell the bot held at the medallion merchant until the
        // fifteen-minute watchdog.
        [Test]
        public void AFullBackpackWaitsAtTheMedallionMerchantOnlyWhileASaleMadeRoom()
        {
            Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: true, soldTrash: true), Is.True);
            Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: true, soldTrash: false), Is.False);
            Assert.That(AutonomousFrontierTransport.KeepWaitingForMedallionRoom(backpackFull: false, soldTrash: false), Is.False);
        }

        // From rambodolphin13/OfflineDAoC 7a8ae69: an ended effect left in the service pulsed forever.
        [Test]
        public void EndedEffectsLeaveTheServiceUnlessRestarting()
        {
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: false, enabling: false), Is.True);
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: true, enabling: false), Is.False);
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: false, enabling: true), Is.False);
            Assert.That(EffectService.IsStaleEndedEffect(ended: false, starting: false, enabling: false), Is.False);
        }
    }
}
