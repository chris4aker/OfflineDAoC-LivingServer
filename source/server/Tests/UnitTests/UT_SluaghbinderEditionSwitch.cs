using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The Sluaghbinder is discontinued (shadowofze 2026-10-30; Living Server 1.2, owner 2026-11-01): whatever
    /// classes/enable_sluaghbinder says, no gamebot is made as one. Old characters still load.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_SluaghbinderEditionSwitch
    {
        private bool _original;

        [SetUp]
        public void Remember() => _original = Properties.ENABLE_SLUAGHBINDER;

        [TearDown]
        public void Restore() => Properties.ENABLE_SLUAGHBINDER = _original;

        // Owner 2026-11-01: Living Server follows the base and discontinues the Sluaghbinder (shadowofze, 2026-10-30).
        // With the switch on or off, no gamebot is made as one and it is never offered.
        [Test]
        public void DiscontinuedEvenWithTheSwitchOn()
        {
            Properties.ENABLE_SLUAGHBINDER = true;
            Assert.That(Roll(new Random(4242)), Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia), Does.Not.Contain(eCharacterClass.Sluaghbinder));
        }

        [Test]
        public void DisabledNeverRollsListsOrHelpsWithTheClass()
        {
            Properties.ENABLE_SLUAGHBINDER = false;
            Assert.That(Roll(new Random(4242)), Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia), Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia), Has.Count.EqualTo(13));
            Assert.That(AutonomousBotIdentityGenerator.IsClassAvailable(eCharacterClass.Sluaghbinder), Is.False);
            Assert.That(AutonomousBotIdentityGenerator.IsClassAvailable(eCharacterClass.Druid), Is.True);
            Assert.Throws<ArgumentException>(() =>
                AutonomousBotIdentityGenerator.GenerateForClass(eRealm.Hibernia, eGender.Male, eCharacterClass.Sluaghbinder));
        }

        [Test]
        public void OtherRealmsAreUnaffected()
        {
            Properties.ENABLE_SLUAGHBINDER = true;
            var albion = AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Albion).ToArray();
            var midgard = AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Midgard).ToArray();
            Properties.ENABLE_SLUAGHBINDER = false;
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Albion), Is.EqualTo(albion));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Midgard), Is.EqualTo(midgard));
        }

        private static List<eCharacterClass> Roll(Random random)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return Enumerable.Range(0, 700)
                .Select(index => AutonomousBotIdentityGenerator.Generate(eRealm.Hibernia,
                    index % 2 == 0 ? eGender.Male : eGender.Female, names, random).CharacterClass)
                .ToList();
        }
    }
}
