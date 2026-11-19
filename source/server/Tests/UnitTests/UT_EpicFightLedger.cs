using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>EpicFightLedger's sums (1.2 diagnostics: where a raid's damage goes). Nothing here is a played check.</summary>
    [TestFixture]
    public class UT_EpicFightLedger
    {
        [SetUp]
        public void Reset() => EpicFightLedger.ResetForTests();

        [Test]
        public void SumsByMonsterAndStartsAgain()
        {
            EpicFightLedger.Add(191, false, "Spindler Broodmother", 1, 100);
            EpicFightLedger.Add(191, false, "Spindler Broodmother", 2, 50);
            EpicFightLedger.Add(191, false, "Newly-born spindler", 2, 400);
            EpicFightLedger.Add(191, true, "Newly-born spindler", 7, 900);
            EpicFightLedger.Add(190, false, "Somebody else", 1, 5);
            EpicFightLedger.Add(191, false, "Nothing", 1, 0);
            (string dealt, string took) = EpicFightLedger.Flush(191);
            Assert.That(dealt, Is.EqualTo("Newly-born spindler:400/1/1,Spindler Broodmother:150/2/2"));
            Assert.That(took, Is.EqualTo("Newly-born spindler:900/1/1"));
            Assert.That(EpicFightLedger.Flush(191), Is.EqualTo((string.Empty, string.Empty)));
            Assert.That(EpicFightLedger.Flush(190).Dealt, Is.EqualTo("Somebody else:5/1/1"), "another region's sums are its own");
        }

        [Test]
        public void ARegionIsWatchedOnlyForAWhile()
        {
            Assert.That(EpicFightLedger.IsWatched(191, 0), Is.False);
            EpicFightLedger.Watch(191, 1_000);
            Assert.That(EpicFightLedger.IsWatched(191, 1_000 + EpicFightLedger.WatchMs), Is.True);
            Assert.That(EpicFightLedger.IsWatched(191, 1_001 + EpicFightLedger.WatchMs), Is.False);
            Assert.That(EpicFightLedger.IsWatched(190, 1_000), Is.False);
        }
    }
}
