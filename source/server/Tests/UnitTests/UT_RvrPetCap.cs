using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Pet caps in the RvR groups gamebots form (AutonomousRvrPetCap; audit 2026-11-03, priority 7). Pure rules; the
    /// group formation that calls them (TryFormGroups for RvR warbands, FormSiegeParties) is not run here.
    /// </summary>
    [TestFixture]
    public class UT_RvrPetCap
    {
        [Test]
        public void The_pet_classes_are_those_that_fight_through_a_pet()
        {
            foreach (eCharacterClass pet in new[] { eCharacterClass.Cabalist, eCharacterClass.Theurgist, eCharacterClass.Necromancer,
                         eCharacterClass.Spiritmaster, eCharacterClass.Bonedancer, eCharacterClass.Enchanter, eCharacterClass.Animist,
                         eCharacterClass.Sluaghbinder })
                Assert.That(AutonomousRvrPetCap.IsPetClass(pet), Is.True, pet.ToString());
            foreach (eCharacterClass other in new[] { eCharacterClass.Druid, eCharacterClass.Hunter, eCharacterClass.Armsman,
                         eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Healer, eCharacterClass.Minstrel })
                Assert.That(AutonomousRvrPetCap.IsPetClass(other), Is.False, other.ToString());
        }

        [Test]
        public void The_cap_is_the_setting_for_five_or_more_and_half_of_it_for_small_groups()
        {
            Assert.That(AutonomousRvrPetCap.CapFor(8, 2), Is.EqualTo(2));
            Assert.That(AutonomousRvrPetCap.CapFor(5, 2), Is.EqualTo(2));
            Assert.That(AutonomousRvrPetCap.CapFor(4, 2), Is.EqualTo(1));
            Assert.That(AutonomousRvrPetCap.CapFor(2, 2), Is.EqualTo(1));
            Assert.That(AutonomousRvrPetCap.CapFor(3, 3), Is.EqualTo(2));
            Assert.That(AutonomousRvrPetCap.CapFor(8, 0), Is.EqualTo(int.MaxValue), "0: off");
        }

        [Test]
        public void A_group_takes_candidates_in_order_and_skips_pets_over_the_cap()
        {
            // P = a pet class. The pool is in the formation's own order (guildmates, level, distance).
            string[] pool = { "P1", "a", "P2", "P3", "b", "P4", "c", "d", "e", "f" };
            bool IsPet(string name) => name.StartsWith("P");
            Assert.That(AutonomousRvrPetCap.Pick(pool, 7, 0, 2, IsPet), Is.EqualTo(new[] { "P1", "a", "P2", "b", "c", "d", "e" }));
            Assert.That(AutonomousRvrPetCap.Pick(pool, 7, 1, 2, IsPet), Is.EqualTo(new[] { "P1", "a", "b", "c", "d", "e", "f" }),
                "a pet-class leader counts");
            Assert.That(AutonomousRvrPetCap.Pick(pool, 3, 0, int.MaxValue, IsPet), Is.EqualTo(new[] { "P1", "a", "P2" }), "no cap: as before");
            Assert.That(AutonomousRvrPetCap.Pick(new[] { "P1", "P2", "P3" }, 7, 0, 2, IsPet), Is.EqualTo(new[] { "P1", "P2" }),
                "a pool of pets gives a short group, which the formation's own minimum then accepts or not");
        }
    }
}
