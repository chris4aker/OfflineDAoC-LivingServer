using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q12: every companion option read rebuilt the "key=value;..." table (42+ call sites).
    /// It is now parsed once per Options string; a changed string is parsed again.
    /// </summary>
    [TestFixture]
    public class UT_VeteranCompanionOptionsCache
    {
        [Test]
        public void ParseReadsPairsCaseInsensitivelyAndSkipsJunk() => Assert.Multiple(() =>
        {
            var map = CompanionOptions165.Parse("Role=2;hood=1;;=x;bad;role=3");
            Assert.That(map["ROLE"], Is.EqualTo("3"), "later keys win, any case");
            Assert.That(map["hood"], Is.EqualTo("1"));
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(CompanionOptions165.Parse(null), Is.Empty);
        });

        [Test]
        public void AChangedOptionsStringIsReadAgain()
        {
            var hired = new OfflineHiredCompanionRecord { Options = "hood=1" };
            Assert.That(CompanionOptions165.HoodUp(hired), Is.True);
            Assert.That(CompanionOptions165.HoodUp(hired), Is.True, "cached");
            hired.Options = "hood=0";
            Assert.That(CompanionOptions165.HoodUp(hired), Is.False, "a new string is parsed again");
            hired.Options = null;
            Assert.That(CompanionOptions165.HoodUp(hired), Is.False);
            Assert.That(CompanionOptions165.HoodUp(null), Is.False);
        }
    }
}
