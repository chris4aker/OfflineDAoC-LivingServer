using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3, the owner's 1.2.2 game: two group damage adds from two casters ("Berermund casts a spell!", "Squishywizard
    /// casts a spell!") printed "XxEvilxX's weapon returns to normal." for every member, twice for one, while each still had
    /// a damage add on. A buff handed over to another of its kind on the same owner is quiet; only the last one going says so.
    /// </summary>
    [TestFixture]
    public class UT_QuietBuffHandover123
    {
        [Test]
        public void AnotherBuffOfTheSameFamilyKeepsTheEndQuiet()
        {
            Assert.Multiple(() =>
            {
                Assert.That(EffectListComponent.KeptHandover(otherDisabled: false, otherIsConcentration: false, concentrationOutOfRange: false, sameFamily: true),
                    Is.True, "a stronger damage add from another caster is on");
                Assert.That(EffectListComponent.KeptHandover(otherDisabled: true, otherIsConcentration: false, concentrationOutOfRange: false, sameFamily: true),
                    Is.True, "a weaker one waits disabled and will be enabled");
                Assert.That(EffectListComponent.KeptHandover(otherDisabled: true, otherIsConcentration: true, concentrationOutOfRange: false, sameFamily: true),
                    Is.True, "a disabled concentration buff whose caster is in range will be enabled");
            });
        }

        [Test]
        public void TheLastBuffOrAnotherFamilyStillSaysItEnded()
        {
            Assert.Multiple(() =>
            {
                Assert.That(EffectListComponent.KeptHandover(otherDisabled: false, otherIsConcentration: false, concentrationOutOfRange: false, sameFamily: false),
                    Is.False, "another family doesn't keep this buff's effect");
                Assert.That(EffectListComponent.KeptHandover(otherDisabled: true, otherIsConcentration: true, concentrationOutOfRange: true, sameFamily: true),
                    Is.False, "a disabled concentration buff out of its caster's range won't come back, so the weapon really returns to normal");
            });
        }

        [Test]
        public void EveryStartAndStopMessageChecksTheHandover()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "source", "server", "GameServer", "ECS-Effects", "ECSGameEffect.cs"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(source, @"if \(!IsBeingReplaced && !IsQuietHandover\)").Count, Is.EqualTo(2));
        }
    }
}
