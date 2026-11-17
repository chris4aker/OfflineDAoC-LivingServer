using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Era builds 1.2, WP2: the server's generator draws the same era class mix as the launcher's batches when
    /// bots/bot_era_class_shares is on (the default), evenly when it is off; never the custom class.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_EraClassDraw
    {
        private bool _original;

        [SetUp]
        public void Remember() => _original = Properties.BOT_ERA_CLASS_SHARES;

        [TearDown]
        public void Restore() => Properties.BOT_ERA_CLASS_SHARES = _original;

        [Test]
        public void OnByDefault() => Assert.That(_original, Is.True);

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void OnTheGeneratorFollowsTheEraShares(eRealm realm)
        {
            Properties.BOT_ERA_CLASS_SHARES = true;
            Dictionary<eCharacterClass, int> counts = Roll(realm, 10_000);
            foreach (EraClassShare share in EraBuildCatalog.Default.Shares((int)realm))
                Assert.That(counts.GetValueOrDefault((eCharacterClass)share.ClassId) / 100.0, Is.EqualTo(share.Share * 100).Within(1.5),
                    share.ClassName);
            Assert.That(counts.Keys, Does.Not.Contain(eCharacterClass.Sluaghbinder));
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Hibernia)]
        public void OffEveryClassIsEquallyLikely(eRealm realm)
        {
            Properties.BOT_ERA_CLASS_SHARES = false;
            Dictionary<eCharacterClass, int> counts = Roll(realm, 10_000);
            int classes = AutonomousBotIdentityGenerator.GetEraClasses(realm).Count;
            Assert.That(counts.Keys, Has.Count.EqualTo(classes));
            foreach (int count in counts.Values)
                Assert.That(count / 100.0, Is.EqualTo(100.0 / classes).Within(1.5));
        }

        private static Dictionary<eCharacterClass, int> Roll(eRealm realm, int count)
        {
            var random = new Random(2003 + (int)realm);
            var counts = new Dictionary<eCharacterClass, int>();
            for (int i = 0; i < count; i++)
            {
                // No reserved-name set: only the class is under test, and names may repeat.
                eCharacterClass cls = AutonomousBotIdentityGenerator.Generate(realm, eGender.Male, null, random).CharacterClass;
                counts[cls] = counts.GetValueOrDefault(cls) + 1;
            }
            return counts;
        }
    }
}
