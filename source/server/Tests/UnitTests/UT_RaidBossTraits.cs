using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Raid-boss damage range, from Offline DAoC True 1.65 (shadowofze).</summary>
    [TestFixture]
    public class UT_RaidBossTraits
    {
        [TestCase(1775, 1400, 1300)]   // a bolt caster at Legion
        [TestCase(1660, 1400, 1300)]   // an archer at Legion
        [TestCase(1500, 1400, 1300)]   // a 1,500 nuke
        [TestCase(1000, 1400, 1000)]   // an Animist's shrooms already stand closer
        [TestCase(1775, 0, 1775)]      // no limit
        [TestCase(0, 1400, 0)]         // no ranged attack
        public void StandOffNeverBeyondTheDamageRange(int own, int limit, int expected) =>
            Assert.That(RaidBossTraits.CapStandOff(own, limit), Is.EqualTo(expected));

        [TestCase(1400, 1340, false)]
        [TestCase(1400, 1360, true)]
        [TestCase(1400, 1775, true)]
        [TestCase(0, 5000, false)]
        public void BeyondDamageRangeStopsTheAttackSpells(int limit, double distance, bool beyond) =>
            Assert.That(RaidBossTraits.BeyondDamageRange(limit, distance), Is.EqualTo(beyond));

        [Test]
        public void ACappedBotStopsCastingOnlyFartherOutThanItWalksTo()
        {
            // the bot walks to the cap less 100 and keeps casting until it is past the cap less 50: no walk/cast flapping
            int cap = RaidBossTraits.CapStandOff(1775, 1400);
            Assert.That(RaidBossTraits.BeyondDamageRange(1400, cap), Is.False);
            Assert.That(RaidBossTraits.BeyondDamageRange(1400, cap - 100), Is.False);
        }

        [Test]
        public void LegionStatesItsDamageRange() =>
            Assert.That(typeof(IRaidBossTraits).IsAssignableFrom(typeof(DOL.GS.Scripts.Legion)), Is.True);
    }
}
