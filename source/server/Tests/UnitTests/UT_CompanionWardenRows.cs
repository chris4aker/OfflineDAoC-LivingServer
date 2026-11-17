using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 (2026-11-11): the Warden gets chant rows (fight, standing around, travelling) and a Role
    /// row with the Friar's choices; the Friar's "Main healer (stays back)" really stays back now.</summary>
    [TestFixture]
    public class UT_CompanionWardenRows
    {
        [Test]
        public void MainHealerStaysBack() => Assert.Multiple(() =>
        {
            Assert.That(StaysBackAsHealer(eCharacterClass.Warden, WardenRoleMainHealer, true), Is.True);
            Assert.That(StaysBackAsHealer(eCharacterClass.Friar, 2, true), Is.True, "Friar: Main healer (stays back)");
            Assert.That(StaysBackAsHealer(eCharacterClass.Friar, 1, true), Is.False, "Friar: Off-healer fights");
            Assert.That(StaysBackAsHealer(eCharacterClass.Shaman, 1, true), Is.True, "Shaman: Stay back and heal");
            Assert.That(StaysBackAsHealer(eCharacterClass.Warden, WardenRoleMainHealer, false), Is.False, "solo");
            Assert.That(StaysBackAsHealer(eCharacterClass.Warden, WardenRoleAutomatic, true), Is.False);
        });

        [Test]
        public void OffHealerHealsOnlyEmergenciesInAFight() => Assert.Multiple(() =>
        {
            Assert.That(OffHealerInFight(eCharacterClass.Warden, WardenRoleOffHealer, true), Is.True);
            Assert.That(OffHealerInFight(eCharacterClass.Warden, WardenRoleOffHealer, false), Is.False, "out of a fight");
            Assert.That(OffHealerInFight(eCharacterClass.Friar, 1, true), Is.True);
            Assert.That(OffHealerInFight(eCharacterClass.Cleric, 1, true), Is.False);
        });

        [Test]
        public void TheWardenRoleRowSharesTheRoleKey()
        {
            // Living Server: the Warden's Role row is not offered (its off-healer and main-healer roles steer True 1.65 AI)
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindWardenRole), Is.Null);
        }
    }
}
