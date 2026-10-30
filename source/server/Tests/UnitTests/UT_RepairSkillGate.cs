using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 2026-10-30: Repair.IsAllowedToBeginWork gated the skill with (percentNeeded / 100) * craftLevel, integer division,
    /// so the 50 percent BeginWork asks for became 0 and anyone could start a repair. The rule is: at least percentNeeded
    /// percent of the item's craft level.
    /// </summary>
    [TestFixture]
    public class UT_RepairSkillGate
    {
        // skill, the item's craft level, percent needed
        [TestCase(0, 735, 50, false)]        // the old gate let this through
        [TestCase(367, 735, 50, false)]      // 367.5 needed
        [TestCase(368, 735, 50, true)]
        [TestCase(735, 735, 50, true)]
        [TestCase(734, 735, 100, false)]     // the trade window asks for the full craft level
        [TestCase(735, 735, 100, true)]
        [TestCase(1, 1, 100, true)]
        [TestCase(0, 0, 50, true)]           // nothing to reach
        public void TheSkillMustReachTheAskedShareOfTheItemsCraftLevel(int skill, int craftLevel, int percentNeeded, bool allowed) =>
            Assert.That(Repair.HasSkillToRepair(skill, craftLevel, percentNeeded), Is.EqualTo(allowed));

        [Test]
        public void HalfTheCraftLevelIsTheRepairGateAndItIsNotZero()
        {
            // A level-50 piece of leg armor: 15 + 50 * 20 + 50 = 1,065; a repair needs 533 skill, not 0.
            int craftLevel = 15 + 50 * 20 + 50;
            Assert.Multiple(() =>
            {
                Assert.That(Repair.HasSkillToRepair(532, craftLevel, 50), Is.False);
                Assert.That(Repair.HasSkillToRepair(533, craftLevel, 50), Is.True);
            });
        }
    }
}
