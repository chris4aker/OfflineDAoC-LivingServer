using DOL.GS;
using NUnit.Framework;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// From zomboden/OfflineDAoC-True165 (AutonomousRealmRaid.Reinforcements.cs), adapted (Living Server 1.2, tests only):
    /// dragon raiders walking back after a release gather at their staging post on the ring and go back in as a wave, as
    /// dungeon raiders already did (RealmRaidReturnWave).
    /// </summary>
    [TestFixture]
    public class UT_DragonReturnWave
    {
        [Test]
        public void ADragonRaiderOnItsWayBackIsHeldForTheWave()
        {
            Assert.That(W.DragonReturnDistance, Is.GreaterThan(6_100), "beyond the staging ring (3,600-6,100 from the lair)");
            // On the way in, never held.
            Assert.That(W.ClassifyDragonRaider(alive: true, beenWithRaid: false, returning: false, distanceToLair: 20_000),
                Is.EqualTo(W.DragonRaider.NotYet));
            Assert.That(W.ClassifyDragonRaider(true, false, false, 4_000), Is.EqualTo(W.DragonRaider.WithTheRaid));
            // Fell in the fight: down, then on its way back from its bind, also once it is near the ring again.
            Assert.That(W.ClassifyDragonRaider(false, true, false, 1_000), Is.EqualTo(W.DragonRaider.Fallen));
            Assert.That(W.ClassifyDragonRaider(true, true, true, 15_000), Is.EqualTo(W.DragonRaider.Returning));
            Assert.That(W.ClassifyDragonRaider(true, true, true, 4_500), Is.EqualTo(W.DragonRaider.Returning), "waiting at its staging post");
            // Wandered off beyond the ring (or out of the region): on its way back too.
            Assert.That(W.ClassifyDragonRaider(true, true, false, float.MaxValue), Is.EqualTo(W.DragonRaider.Returning));
            Assert.That(W.ClassifyDragonRaider(false, false, false, 1_000), Is.EqualTo(W.DragonRaider.NotYet), "never reached the raid");
        }

        [Test]
        public void ADragonWaveGoesByTheDungeonWavesRules()
        {
            Assert.That(W.Next(outside: 4, fallen: 2, waiting: 3, waitedMilliseconds: 0, open: false), Is.EqualTo(W.Step.Open), "too few: straight in");
            Assert.That(W.Next(10, 4, 5, 0, false), Is.EqualTo(W.Step.Gather));
            Assert.That(W.Next(10, 4, 9, 0, false), Is.EqualTo(W.Step.Go), "60% of the 14 on their way back");
            Assert.That(W.Next(10, 4, 8, W.PatienceMilliseconds, false), Is.EqualTo(W.Step.Go));
        }
    }
}
