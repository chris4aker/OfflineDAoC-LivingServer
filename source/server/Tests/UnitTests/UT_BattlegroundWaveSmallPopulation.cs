using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Wave = DOL.GS.AutonomousBattlegroundWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Battleground waves with a realistic small population (owner's PC, build 3d67b6f, Thidranki 2026-11-15: 11-21 bots
    /// of a realm inside, no wave for 22 minutes, then one wave of 8 that never reached the gate). Only the pure rules are
    /// tested here; whether a wave goes in and reaches the gate needs a run on the owner's PC (BG_WAVE_STATUS).
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundWaveSmallPopulation
    {
        private const long Minute = 60_000;
        // Thidranki Faste (keep table) and the Midgard field arrival (BattlegroundBrackets.BotArrival).
        private static readonly Vector3 ThidrankiKeep = new(33089, 38271, 3720);
        private static readonly Vector3 MidgardArrival = new(53150, 26093, 4263);
        private static readonly Vector3 HiberniaArrival = new(19714, 19085, 4094);

        [TestCase(13, 7)]
        [TestCase(14, 7)]
        [TestCase(11, 6)]
        [TestCase(8, 4)]
        [TestCase(21, 11)]
        [TestCase(3, 4)]
        [TestCase(0, 4)]
        public void PatienceWaveIsHalfTheRealmInside(int inside, int wave) =>
            Assert.That(Wave.PatienceWaveSize(inside), Is.EqualTo(wave));

        [Test]
        public void ARealmOfThirteenGoesWithSevenAfterThePatienceTime()
        {
            Assert.Multiple(() =>
            {
                // The 3d67b6f rule needed eight here however long they waited; 13 inside makes WaveSize 8.
                Assert.That(Wave.WaveSize(13), Is.EqualTo(8));
                Assert.That(Wave.WaveGoes(7, 13, 3 * Minute), Is.False, "before the patience time most of the realm is needed");
                Assert.That(Wave.WaveGoes(7, 13, 5 * Minute), Is.True);
                Assert.That(Wave.WaveGoes(6, 13, 30 * Minute), Is.False, "never fewer than half of those inside");
                Assert.That(Wave.WaveGoes(8, 13, 0), Is.True, "most of the realm gathered goes at once");
            });
        }

        [Test]
        public void AnotherRealmsWaveGoingInIsTheCueToGoWithHalf()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Wave.WaveGoes(7, 13, Minute, otherRealmGoing: false), Is.False);
                Assert.That(Wave.WaveGoes(7, 13, Minute, otherRealmGoing: true), Is.True);
                Assert.That(Wave.WaveGoes(6, 13, Minute, otherRealmGoing: true), Is.False);
            });
        }

        [Test]
        public void TheHibernianWaveOfEightOutOfTwentyOneNowWaitsForEleven()
        {
            // BG_WAVE_ASSAULT realm=Hibernia gathered=8 needed=12 inside=21 minutes=22.0, spent three minutes later
            // with nobody near the keep: half of the realm (11) now goes, not a fixed eight.
            Assert.Multiple(() =>
            {
                Assert.That(Wave.WaveGoes(8, 21, 22 * Minute), Is.False);
                Assert.That(Wave.WaveGoes(11, 21, 22 * Minute), Is.True);
                Assert.That(Wave.WaveSize(21), Is.EqualTo(12));
            });
        }

        [TestCase(false, 3_500f, 3_400f, false, TestName = "Albion defender on the wall, not firing on us: left alone")]
        [TestCase(false, 600f, 500f, true, TestName = "Enemy walked into the gathered realm: fought")]
        [TestCase(true, 1_200f, 1_300f, true, TestName = "Enemy firing on the realm near the staging point: fought back")]
        [TestCase(true, 2_000f, 800f, true, TestName = "Attacked on the way to the staging point: fought back")]
        [TestCase(true, 2_000f, 1_400f, false, TestName = "Archer firing from far off the staging point: walk on")]
        [TestCase(false, 1_200f, 300f, false, TestName = "Enemy that broke off and left the staging point: not chased")]
        public void AGathererFightsOnlyWhatComesAtIt(bool firingOnRealm, float toStaging, float toBot, bool engages) =>
            Assert.That(Wave.GathererMayEngage(firingOnRealm, toStaging, toBot), Is.EqualTo(engages));

        [TestCase(true, false, 3_000f, false, false, TestName = "Gates shut, a guard 3,000 away: march on the gate")]
        [TestCase(true, false, 1_000f, false, true, TestName = "Gates shut, a guard in reach: fight it")]
        [TestCase(true, false, 3_000f, true, true, TestName = "Gates shut, a guard firing on the realm: fight back")]
        [TestCase(true, true, 3_000f, false, true, TestName = "The lord is never filtered here")]
        [TestCase(false, false, 4_000f, false, true, TestName = "Gates down: the ordinary assault")]
        public void AGoingWaveMarchesOnTheGateBeforeTheGuards(bool gatesShut, bool lord, float distance, bool firing, bool takes) =>
            Assert.That(Wave.WaveTakesGuard(gatesShut, lord, distance, firing), Is.EqualTo(takes));

        [Test]
        public void TheStagingPointIsOutsideTheKeepsReach()
        {
            Vector3 midgard = Wave.StagingRaw(ThidrankiKeep, MidgardArrival)!.Value;
            Vector3 hibernia = Wave.StagingRaw(ThidrankiKeep, HiberniaArrival)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(Vector3.Distance(midgard, ThidrankiKeep), Is.EqualTo(Wave.StagingDistance).Within(0.5));
                // Toward Midgard's arrival.
                Assert.That(Vector3.Distance(midgard, MidgardArrival),
                    Is.EqualTo(Vector3.Distance(ThidrankiKeep, MidgardArrival) - Wave.StagingDistance).Within(0.5));
                // 3d67b6f: the keep's guards killed Midgard bots up to 3,109 from the keep (median 2,230).
                Assert.That(Wave.StagingDistance, Is.GreaterThan(3_109f));
                Assert.That(Vector3.Distance(hibernia, ThidrankiKeep), Is.EqualTo(Wave.StagingDistance).Within(0.5));
            });
        }

        [Test]
        public void ANearArrivalStagesAtFourFifthsOfTheWay()
        {
            Vector3 keep = new(0, 0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Wave.StagingRaw(keep, new Vector3(1_000, 0, 0)), Is.EqualTo(new Vector3(800, 0, 0)));
                Assert.That(Wave.StagingRaw(keep, keep), Is.Null);
            });
        }
    }
}
