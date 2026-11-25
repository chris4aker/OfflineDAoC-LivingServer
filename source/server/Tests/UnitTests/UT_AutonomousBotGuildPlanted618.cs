using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>THROWAWAY (#618): a failing feature test (its name is in REPORT_TEST_CLASSES' default). Reverted next commit.</summary>
    [TestFixture]
    public class UT_AutonomousBotGuildPlanted618
    {
        [Test]
        public void APlantedFeatureFailure() => Assert.Fail("planted by #618 to prove the feature-test gate");
    }
}
