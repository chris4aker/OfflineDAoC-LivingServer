using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Offline DAoC 0.33 editions share one server. classes/enable_sluaghbinder is on for 0.33b
    /// (and by default) and off for the 0.33 "no custom class" edition.
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

        [Test]
        public void EnabledByDefault()
        {
            Assert.That(_original, Is.True);
        }

        // Owner 2026-11-17: "the custom class, that should never be played by bots". The edition switch is for players only:
        // with it on (0.37b) or off (0.37), no gamebot roll, /spawn or /raid helper, buffbot or Epic Special Force is ever
        // the Sluaghbinder.
        [TestCase(true)]
        [TestCase(false)]
        public void BotsNeverRollListOrHelpWithTheClassInEitherEdition(bool edition)
        {
            Properties.ENABLE_SLUAGHBINDER = edition;
            Assert.That(Roll(new Random(4242)), Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia), Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia), Has.Count.EqualTo(13));
            Assert.That(AutonomousBotIdentityGenerator.IsClassAvailable(eCharacterClass.Sluaghbinder), Is.False);
            Assert.That(AutonomousBotIdentityGenerator.IsClassAvailable(eCharacterClass.Druid), Is.True);
            Assert.Throws<ArgumentException>(() =>
                AutonomousBotIdentityGenerator.GenerateForClass(eRealm.Hibernia, eGender.Male, eCharacterClass.Sluaghbinder));
            Assert.That(Commands.TemporaryGroupClassCatalog.ForRealm(eRealm.Hibernia).Select(entry => entry.CharacterClass),
                Does.Not.Contain(eCharacterClass.Sluaghbinder));
            Assert.That(Commands.TemporaryGroupClassCatalog.TryResolve(eRealm.Hibernia, "Sluaghbinder", out _), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RollsAreRepeatableInEitherEdition(bool edition)
        {
            Properties.ENABLE_SLUAGHBINDER = edition;
            Assert.That(Roll(new Random(4242)), Is.EqualTo(Roll(new Random(4242))));
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
