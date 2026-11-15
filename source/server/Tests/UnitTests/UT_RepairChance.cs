using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 2026-11-15, crafting M0: the repair chances divided by the item's craft level (in doubles: infinity or a negative
    /// slope at 0 or less) and by the woodworking skill (in integers: DivideByZeroException at 0, inside the repair
    /// timer). The skill gate's integer division was fixed on 2026-10-30 (UT_RepairSkillGate).
    /// </summary>
    [TestFixture]
    public class UT_RepairChance
    {
        // skill, craft level, chance: 10% at half the craft level, 100% at the craft level, as before
        [TestCase(500, 1000, 10)]
        [TestCase(1000, 1000, 100)]
        [TestCase(750, 1000, 55)]
        [TestCase(444, 1000, 0)]
        [TestCase(0, 1000, 0)]
        [TestCase(-1, 1000, 0)]      // untrained
        [TestCase(2000, 1000, 100)]
        [TestCase(10, 0, 100)]       // no craft level: used to be the cast of infinity
        [TestCase(0, 0, 100)]        // used to be the cast of NaN
        [TestCase(10, -5, 100)]      // a level-0 weapon: 15 + (0 - 1) * 20; used to be a negative slope, 0%
        public void TheRepairChanceIsAPercentForEveryInput(int skill, int craftLevel, int expected) =>
            Assert.That(Repair.RepairChancePercent(skill, craftLevel), Is.EqualTo(expected));

        [TestCase(1000, 1000)]
        [TestCase(600, 1065)]
        [TestCase(533, 1065)]
        public void TheFormulaIsUnchangedForRealItems(int skill, int craftLevel)
        {
            int old = (int)(90 / (craftLevel * 0.5) * skill) - 80;
            old = old > 100 ? 100 : old < 0 ? 0 : old;
            Assert.That(Repair.RepairChancePercent(skill, craftLevel), Is.EqualTo(old));
        }

        [TestCase(1, 40)]
        [TestCase(2, 65)]
        [TestCase(50, 89)]
        [TestCase(51, 90)]
        [TestCase(1000, 90)]
        [TestCase(0, 40)]            // used to throw DivideByZeroException
        [TestCase(-1, 40)]           // untrained: used to be 140, clamped to 100
        public void TheSiegeRepairChanceNeverDividesByZero(int woodworking, int expected) =>
            Assert.That(Repair.SiegeRepairChancePercent(woodworking), Is.EqualTo(expected));
    }
}
