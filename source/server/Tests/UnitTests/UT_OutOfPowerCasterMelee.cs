using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Casters with no power for any attack spell fight with their staff.</summary>
    [TestFixture]
    public class UT_OutOfPowerCasterMelee
    {
        [Test]
        public void OutOfPowerMeansNoAttackSpellIsAffordable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BrainOutOfPower(new[] { 20, 35 }, 10), Is.True, "nothing affordable");
                Assert.That(BrainOutOfPower(new[] { 20, 35 }, 20), Is.False, "the cheap nuke is affordable");
                Assert.That(BrainOutOfPower(new[] { 0, 35 }, 0), Is.False, "a free spell can still be cast");
                Assert.That(BrainOutOfPower(new int[0], 0), Is.False, "no attack spells known");
            });
        }

        [Test]
        public void TemporaryCompanionHealersHoldTheirPositionDuringAFight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CompanionHealerCombatHold.Holds(true, false, true, true, true), Is.True,
                    "a /spawn healer stays put while the group fights");
                Assert.That(CompanionHealerCombatHold.Holds(true, false, true, false, true), Is.False,
                    "it follows again when the fight is over");
                Assert.That(CompanionHealerCombatHold.Holds(true, false, true, true, false), Is.False,
                    "it follows a leader beyond the leash");
                Assert.That(CompanionHealerCombatHold.Holds(true, false, false, true, true), Is.False,
                    "other companions keep their formation");
                Assert.That(CompanionHealerCombatHold.Holds(false, true, true, true, true), Is.False,
                    "autonomous healers are unchanged");
            });
        }

        [Test]
        public void TemporaryCompanionsDoNotLeaveThePlayerToSupportAFarMember()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.LeavesPlayerToSupport(true, false, true, 2_000), Is.True, "the member is far from the player");
                Assert.That(BotBrain.LeavesPlayerToSupport(true, false, true, 1_000), Is.False, "the member is near the player");
                Assert.That(BotBrain.LeavesPlayerToSupport(true, false, false, 2_000), Is.False, "the player is not here");
                Assert.That(BotBrain.LeavesPlayerToSupport(false, true, true, 2_000), Is.False, "autonomous gamebots are unchanged");
            });
        }

        private static bool BrainOutOfPower(int[] costs, int mana) => BotBrain.OutOfPower(costs, mana);
    }
}
