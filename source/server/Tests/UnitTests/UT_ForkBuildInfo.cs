using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_ForkBuildInfo
    {
        [TestCase("1.0.0+6276541abcdef0123456789", "6276541abcde")]
        [TestCase("1.0.0+abc123", "abc123")]
        [TestCase("1.0.0", "local")]
        [TestCase("1.0.0+", "local")]
        [TestCase(null, "local")]
        public void CommitFrom_TakesTheShortShaAfterThePlus(string informationalVersion, string expected)
        {
            Assert.That(ForkBuildInfo.CommitFrom(informationalVersion), Is.EqualTo(expected));
        }

        [Test]
        public void BuildName_IsAForkBuildOfThisRelease()
        {
            Assert.That(ForkBuildInfo.BuildName, Does.Match(@"^\d+\.\d+(-beta(\.\d+)?)?$"));
            Assert.That(ForkBuildInfo.BaseRelease, Does.Match(@"^0\.\d+(,0\.\d+)*$"));
        }
    }
}
