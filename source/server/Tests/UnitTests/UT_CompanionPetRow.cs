using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G9 (2026-11-11): the companion "Pet" row - Automatic, Guard me, Take adds, Attack my
    /// target - for pet classes, the Druid and the Spiritmaster in groups included.</summary>
    [TestFixture]
    public class UT_CompanionPetRow
    {
        [Test]
        public void OfferedToThePetClasses() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass petClass in new[] { eCharacterClass.Cabalist, eCharacterClass.Enchanter,
                         eCharacterClass.Spiritmaster, eCharacterClass.Bonedancer, eCharacterClass.Hunter, eCharacterClass.Druid,
                         eCharacterClass.Sorcerer, eCharacterClass.Minstrel })
                Assert.That(PetRowClass(petClass), Is.True, petClass.ToString());
            foreach (eCharacterClass other in new[] { eCharacterClass.Necromancer, eCharacterClass.Theurgist,
                         eCharacterClass.Armsman, eCharacterClass.Cleric })
                Assert.That(PetRowClass(other), Is.False, other.ToString());
        });

        [Test]
        public void AutomaticKeepsTheOldRules() => Assert.Multiple(() =>
        {
            Assert.That(PetDefendsOnly(PetAutomatic, true), Is.True, "Regrowth Druid");
            Assert.That(PetDefendsOnly(PetAutomatic, false), Is.False);
            Assert.That(BrainHoldsPetOffTarget(PetAutomatic, true), Is.True);
            Assert.That(PetTakesAdds(PetAutomatic, true), Is.True, "grouped Spiritmaster with a tank (Q25)");
            Assert.That(PetTakesAdds(PetAutomatic, false), Is.False);
        });

        [Test]
        public void GuardMeDefendsTakeAddsGoesForAddsAttackGoesIn() => Assert.Multiple(() =>
        {
            Assert.That(PetDefendsOnly(PetGuardMe, false), Is.True);
            Assert.That(BrainHoldsPetOffTarget(PetGuardMe, false), Is.True);
            Assert.That(PetDefendsOnly(PetTakeAdds, true), Is.False, "the service sends it at adds");
            Assert.That(BrainHoldsPetOffTarget(PetTakeAdds, false), Is.True, "never at the owner's own target");
            Assert.That(PetTakesAdds(PetTakeAdds, false), Is.True);
            Assert.That(PetDefendsOnly(PetAttack, true), Is.False, "a Regrowth Druid's pet goes in");
            Assert.That(BrainHoldsPetOffTarget(PetAttack, true), Is.False);
            Assert.That(PetTakesAdds(PetAttack, true), Is.False, "a grouped Spiritmaster's pet goes on the kill");
        });

        [Test]
        public void GamebotsReadAutomatic() => Assert.That(PetJob(null), Is.EqualTo(PetAutomatic));
    }
}
