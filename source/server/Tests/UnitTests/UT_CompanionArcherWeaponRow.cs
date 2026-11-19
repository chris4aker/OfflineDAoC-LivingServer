using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G6 (2026-11-11): Scouts, Hunters and Rangers fight with the bow or in melee by their
    /// training (Automatic) or the player's Weapon row choice; a spear Hunter melees.</summary>
    [TestFixture]
    public class UT_CompanionArcherWeaponRow
    {
        [Test]
        public void AutomaticFollowsTraining() => Assert.Multiple(() =>
        {
            Assert.That(ArcherFightsWithBow(WeaponAutomatic, 25, 50), Is.False, "spear Hunter: 50 Spear, 25 Composite Bow");
            Assert.That(ArcherFightsWithBow(WeaponAutomatic, 50, 30), Is.True, "bow Scout");
            Assert.That(ArcherFightsWithBow(WeaponAutomatic, 35, 35), Is.True, "a tie keeps the bow");
            Assert.That(ArcherFightsWithBow(WeaponAutomatic, 0, 0), Is.True, "nothing trained: as before");
        });

        [Test]
        public void TheChoiceWins() => Assert.Multiple(() =>
        {
            Assert.That(ArcherFightsWithBow(WeaponBow, 1, 50), Is.True);
            Assert.That(ArcherFightsWithBow(WeaponMelee, 50, 1), Is.False);
        });

        [Test]
        public void OnlyTheThreeArchersHaveAnArcheryLine() => Assert.Multiple(() =>
        {
            Assert.That(ArcheryLine(eCharacterClass.Scout), Is.EqualTo(Specs.Longbow));
            Assert.That(ArcheryLine(eCharacterClass.Hunter), Is.EqualTo(Specs.CompositeBow));
            Assert.That(ArcheryLine(eCharacterClass.Ranger), Is.EqualTo(Specs.RecurveBow));
            Assert.That(ArcheryLine(eCharacterClass.Armsman), Is.Null);
        });
    }
}
