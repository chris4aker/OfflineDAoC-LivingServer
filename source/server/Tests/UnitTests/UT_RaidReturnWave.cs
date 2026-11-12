using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, 2026-11-12 (build 3e64140): after a wipe the released raiders walked back into the dungeon in ones and
    /// twos and nobody reached the muster for 20 minutes (Trollheim, Tuscaran, Krondon, Darkness Falls). They now gather
    /// at the way in and go back in together.
    /// </summary>
    [TestFixture]
    public class UT_RaidReturnWave
    {
        private const long Minute = 60_000;

        [Test]
        public void AFewStragglersWalkStraightIn()
        {
            Assert.That(RealmRaidReturnWave.Next(7, 0, 0, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Open));
            Assert.That(RealmRaidReturnWave.Next(3, 4, 3, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Open));
        }

        [Test]
        public void AWipedRaidGathersAtTheWayIn()
        {
            // Trollheim, Nosdoden: 60 of 80 fell. The first ones back wait, also while most are still down inside.
            Assert.That(RealmRaidReturnWave.Next(0, 60, 0, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather));
            Assert.That(RealmRaidReturnWave.Next(5, 55, 5, Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather));
            Assert.That(RealmRaidReturnWave.Next(60, 0, 35, 2 * Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather));
        }

        [Test]
        public void MostOfThemThereGoesInTogether()
        {
            Assert.That(RealmRaidReturnWave.Next(60, 0, 36, 2 * Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Go));
            // The whole raid at its staging when the raid starts: it goes in at once, as before.
            Assert.That(RealmRaidReturnWave.Next(80, 0, 80, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Go));
        }

        [Test]
        public void NobodyWaitsLong()
        {
            Assert.That(RealmRaidReturnWave.Next(60, 0, 8, 4 * Minute - 1, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather));
            Assert.That(RealmRaidReturnWave.Next(60, 0, 8, 4 * Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Go), "a party's worth after four minutes");
            Assert.That(RealmRaidReturnWave.Next(60, 0, 7, 4 * Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather));
            Assert.That(RealmRaidReturnWave.Next(60, 0, 2, 8 * Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Go), "whoever is there after eight");
            Assert.That(RealmRaidReturnWave.Next(60, 0, 0, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather), "nobody there yet: nothing to send");
        }

        [Test]
        public void AnEncounterThatResetsWhileTheRaidStandsIsNotTheRaidsWipe()
        {
            // Darkness Falls, 2026-11-12: High Lord Oro at 59% reset to 100% with 3 of 80 raiders down.
            Assert.That(RealmRaidDungeonRoute.IsWipe(80, 3, 100, 59), Is.True, "the old verdict");
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(80, 3, 100, 59), Is.True);
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(80, 20, 100, 59), Is.False, "a quarter fell: the raid lost that fight");
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(80, 3, 80, 59), Is.False, "it did not reset");
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(80, 3, 100, 84), Is.False, "never worn down: not an attempt at all");
            Assert.That(RealmRaidDungeonRoute.FreeEncounterResets, Is.EqualTo(2));
        }

        [Test]
        public void LateArrivalsFollowTheWaveThatJustWent()
        {
            Assert.That(RealmRaidReturnWave.Next(30, 0, 3, 0, true), Is.EqualTo(RealmRaidReturnWave.Step.Open));
        }
    }
}
