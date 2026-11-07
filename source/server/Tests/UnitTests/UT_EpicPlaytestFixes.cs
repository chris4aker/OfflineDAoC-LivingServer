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
        [Test]
        public void ASelfHealingBossWornDownAgainIsProgress()
        {
            int lowest = 30_000, last = 42_000;
            foreach (int health in new[] { 41_000, 40_000, 39_000, 37_000, 36_000, 35_000 })
            {
                Assert.That(RealmRaidDungeonRoute.TargetTookDamage(health, lowest, last), Is.True, $"{health}");
                last = health;
            }
        }

        [Test]
        public void ANewLowestIsProgress() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(29_000, 30_000, 31_000), Is.True);

        [Test]
        public void NoDamageIsNoProgress()
        {
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(100_000, 100_000, 100_000), Is.False, "untouched");
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(45_000, 30_000, 44_000), Is.False, "healing back up");
        }

        [Test]
        public void AFullHealThenDamageIsProgress()
        {
            // Govannon turns demon at 50% and heals to full; the raid's damage from 100% down is progress at once (the old
            // rule waited for health below 50% again and would have skipped him for five minutes of it).
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(99_000, 50_000, 100_000), Is.True);
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(100_000, 50_000, 99_000), Is.False, "the heal itself is not damage");
        }

        // Easmarach (Galladoria, run duo2): with the stall fix the raid fought on, but he stayed on the waterfall top (no walked
        // path down) and only ranged hits reached him there: 42% to 34% in ten minutes, and each fall to 30% healed an eighth.
        [TestCase(30, false, 0, false, true)]
        [TestCase(31, false, 0, false, false)]
        [TestCase(30, true, 0, false, false)]
        [TestCase(25, false, 1, false, false)]
        [TestCase(25, false, 1, true, true)]
        public void EasmarachHealsOnTheFallsOnceAFightUnlessEndless(int health, bool resting, int heals, bool endless, bool retreats) =>
            Assert.That(EpicEncounterMechanics.EasmarachRetreats(health, resting, heals, endless), Is.EqualTo(retreats));

        [Test]
        public void TheEndlessFallsHealIsAnOwnerSettingOffByDefault()
        {
            Assert.That(EpicEncounterMechanics.RestoreSwitches, Does.Contain(nameof(EpicEncounterMechanics.AllowEasmarachEndlessFallsHeal)));
            Assert.That(DOL.GS.ServerProperties.Properties.EPIC_EASMARACH_ENDLESS_FALLS_HEAL, Is.False);
        }

        [Test]
        public void TheFirstLookIsNoProgressByItself() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(50_000, 50_000, 0), Is.False);
    }
}
