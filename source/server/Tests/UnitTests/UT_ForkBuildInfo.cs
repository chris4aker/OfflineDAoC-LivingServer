using DOL.GS.ServerProperties;
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
            Assert.That(ForkBuildInfo.BuildName, Does.Match(@"^\d+\.\d+(\.\d+)?(-beta(\.\d+)?)?$"));
            Assert.That(ForkBuildInfo.BaseRelease, Does.Match(@"^0\.\d+(,0\.\d+)*$"));
        }

        [Test]
        public void TheStartupLineNamesTheRaidSurpriseNeutrally()
        {
            // Players paste the FORK_BUILD line into public bug reports: it must not give the gag away.
            bool was = Properties.BOT_LIVING_LEEROY;
            try
            {
                Properties.BOT_LIVING_LEEROY = true;
                string on = ForkBuildInfo.Describe();
                Assert.That(on, Does.Not.Contain("leeroy").IgnoreCase);
                Assert.That(on, Does.Match(@"living=(\S*,)?extras(,|\s|$)"));
                Properties.BOT_LIVING_LEEROY = false;
                Assert.That(ForkBuildInfo.Describe(), Does.Not.Contain("extras"));
            }
            finally
            {
                Properties.BOT_LIVING_LEEROY = was;
            }
        }
    }
}
