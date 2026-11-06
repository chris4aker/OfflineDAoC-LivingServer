using DOL.GS;
using NUnit.Framework;
using State = DOL.GS.GameObject.eObjectState;

namespace DOL.UnitTests
{
    /// <summary>
    /// Fixes taken from chris4aker/OfflineDAoC-LivingServer v1.1-beta (2026-10-29). These tests are lifted from that fork's
    /// UT_ForkPicksOct27, UT_EffectOrphans and UT_PlayTestFixes, keeping only the parts for patches 01, 02 and 05.
    /// </summary>
    [TestFixture]
    public class UT_ForkFixes1029
    {
        // 01: before the first pass, now - long.MinValue overflowed and the live clock always returned early (found by
        // stefanrows/OfflineDAoC 0.345.0).
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

        // 02: an ended effect left in the service pulsed forever (from rambodolphin13/OfflineDAoC 7a8ae69).
        [Test]
        public void EndedEffectsLeaveTheServiceUnlessRestarting()
        {
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: false, enabling: false), Is.True);
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: true, enabling: false), Is.False);
            Assert.That(EffectService.IsStaleEndedEffect(ended: true, starting: false, enabling: true), Is.False);
            Assert.That(EffectService.IsStaleEndedEffect(ended: false, starting: false, enabling: false), Is.False);
        }

        // 02: effects outlived the NPCs that held or cast them.
        [Test]
        public void EffectsOfDeletedNpcsEnd() => Assert.Multiple(() =>
        {
            Assert.That(EffectService.OutlivedItsLiving(State.Deleted, true, false, null, false), Is.True, "a snare on a deleted monster");
            Assert.That(EffectService.OutlivedItsLiving(State.Active, true, true, State.Deleted, true), Is.True, "a deleted pet's pulse");
            Assert.That(EffectService.OutlivedItsLiving(State.Active, true, false, null, false), Is.False, "a live monster");
            Assert.That(EffectService.OutlivedItsLiving(State.Inactive, true, false, null, false), Is.False, "a bot between regions");
            Assert.That(EffectService.OutlivedItsLiving(State.Deleted, false, false, null, false), Is.False, "a player's saved effects");
            Assert.That(EffectService.OutlivedItsLiving(State.Active, false, true, State.Inactive, true), Is.False, "a pet between regions");
        });

        // 05: issue #25, interaction spots are searched for only near the merchant.
        [Test]
        public void MerchantSpotsAreSearchedOnlyNearTheMerchant()
        {
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(150), Is.True);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(AutonomousSupplyMerchantSpace.SpotSearchRange), Is.True);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(AutonomousSupplyMerchantSpace.SpotSearchRange + 1), Is.False);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(110_000), Is.False);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(int.MaxValue), Is.False, "another region");
        }
    }
}
