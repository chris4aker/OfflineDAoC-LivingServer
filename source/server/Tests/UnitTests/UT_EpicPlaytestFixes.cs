using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Fixes from the 1.2 epic encounter play test (container runs, 2026-11-07).</summary>
    [TestFixture]
    public class UT_EpicPlaytestFixes
    {
        // Easmarach (Galladoria, run duo1): at 30% he climbs the waterfall and takes back an eighth of his health, so his
        // health ran 30% -> 42% -> 41, 40, 39, 37, 36, 35% over five minutes of the raid's casters shooting him from the
        // falls stands, never below the earlier 30%. The route counted only a new lowest as damage and skipped him at 35%
        // "no damage for five minutes"; out of combat he healed to full.
        private const int Max = 100_000;

        [Test]
        public void ASelfHealingBossWornDownAgainIsProgress()
        {
            int lowest = 30_000, last = 42_000;
            foreach (int health in new[] { 41_000, 40_000, 39_000, 37_000, 36_000, 35_000 })
            {
                Assert.That(RealmRaidDungeonRoute.TargetTookDamage(health, lowest, last, Max), Is.True, $"{health}");
                last = health;
            }
        }

        [Test]
        public void ANewLowestIsProgress() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(29_000, 30_000, 31_000, Max), Is.True);

        [Test]
        public void NoDamageIsNoProgress()
        {
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(100_000, 100_000, 100_000, Max), Is.False, "untouched");
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(45_000, 30_000, 44_000, Max), Is.False, "healing back up");
        }

        [Test]
        public void ABossThatOutHealsTheRaidTowardFullIsNotProgress() =>
            // 30% lowest, now 70% and nicked to 69%: more than a quarter above the lowest, the old stall rule stands.
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(69_000, 30_000, 70_000, Max), Is.False);

        [Test]
        public void TheFirstLookIsNoProgressByItself() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(50_000, 50_000, 0, Max), Is.False);
    }
}
