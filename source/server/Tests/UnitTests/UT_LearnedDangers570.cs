using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 2: the learned danger spots (AutonomousPveBossDanger) steer town and horse trips too, and landings where
    /// bots keep dying soon after they get off a horse are remembered (port of True165 AutonomousRoadHotspots a24f34987 /
    /// a97fd32 and AutonomousStableLandingWatch feecfac1d / 3aaeae2). Pure rules; tests only until a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_LearnedDangers570
    {
        private const ushort Region = 200;
        private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        // a level 19 monster killed level 8 bots here
        private const int SpotX = 500_000, SpotY = 500_000;

        [SetUp]
        public void SetUp()
        {
            AutonomousPveBossDanger.Clear();
            AutonomousStableLandingWatch.Clear();
            AutonomousLearnedDangerTrips.ClearSamples();
            for (int i = 0; i < 3; i++)
                AutonomousPveBossDanger.Record(Region, SpotX + i * 100, SpotY, 19, Now.AddMinutes(i), "lunantishee");
        }

        [TearDown]
        public void TearDown()
        {
            AutonomousPveBossDanger.Clear();
            AutonomousStableLandingWatch.Clear();
            AutonomousLearnedDangerTrips.ClearSamples();
        }

        private static bool Service(bool broker, int fromX, int fromY, int level, int toX, int toY) =>
            AutonomousLearnedDangerTrips.ServiceDangerous(broker, Region, fromX, fromY, level, Region, toX, toY, Now);

        [Test]
        public void AServiceBesideOrStraightPastASpotIsDangerousToABotBelowTheKiller()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Service(false, 490_000, 500_000, 9, 501_000, 500_000), Is.True, "a merchant beside the spot");
                Assert.That(Service(false, 490_000, 500_000, 9, 510_000, 500_000), Is.True, "a merchant straight past it");
                Assert.That(Service(false, 490_000, 500_000, 9, 490_000, 520_000), Is.False, "a merchant the other way");
                Assert.That(Service(false, 490_000, 500_000, 20, 501_000, 500_000), Is.False,
                    "a level 19 killer is no danger to a level 20 bot");
                Assert.That(Service(true, 490_000, 500_000, 9, 510_000, 500_000), Is.False,
                    "the realm's one broker is judged by its own spot, not the straight line");
                Assert.That(Service(true, 490_000, 500_000, 9, 501_000, 500_000), Is.True, "a broker inside a spot");
                Assert.That(Service(false, 500_500, 500_000, 9, 480_000, 520_000), Is.False,
                    "a bot already inside the spot can still walk out to a safe merchant");
                Assert.That(AutonomousLearnedDangerTrips.ServiceDangerous(false, 100, 490_000, 500_000, 9, Region, 490_000,
                    520_000, Now), Is.False, "another region: only the service's own spot counts");
            });
        }

        [Test]
        public void TheNearestSafeServiceWinsAndADangerousOneOnlyWhenNoOtherIsLeft()
        {
            string near = "near", far = "far", farther = "farther";
            var choice = new AutonomousLearnedDangerTrips.SafestNearest<string>();
            choice.Consider(near, 1, dangerous: true);
            choice.Consider(farther, 9, dangerous: false);
            choice.Consider(far, 5, dangerous: false);
            var onlyDangerous = new AutonomousLearnedDangerTrips.SafestNearest<string>();
            onlyDangerous.Consider(far, 5, dangerous: true);
            onlyDangerous.Consider(near, 1, dangerous: true);
            var nearestSafe = new AutonomousLearnedDangerTrips.SafestNearest<string>();
            nearestSafe.Consider(near, 1, dangerous: false);
            nearestSafe.Consider(far, 5, dangerous: true);
            Assert.Multiple(() =>
            {
                Assert.That(choice.Choice, Is.EqualTo(far));
                Assert.That(choice.Skipped, Is.True);
                Assert.That(choice.Dangerous, Is.EqualTo(1));
                Assert.That(onlyDangerous.Choice, Is.EqualTo(near), "never left with nowhere to sell");
                Assert.That(onlyDangerous.Skipped, Is.False);
                Assert.That(nearestSafe.Choice, Is.EqualTo(near));
                Assert.That(nearestSafe.Skipped, Is.False);
            });
        }

        [Test]
        public void LandingsAndFinishingWalksPastASpot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLearnedDangerTrips.LandingDangerous(Region, 502_000, 500_000, 20_000, 9, Now), Is.True,
                    "a landing beside the spot, passed through");
                Assert.That(AutonomousLearnedDangerTrips.LandingDangerous(Region, 502_000, 500_000, 1_000, 9, Now), Is.False,
                    "the bot is going there");
                Assert.That(AutonomousLearnedDangerTrips.LandingDangerous(Region, 502_000, 500_000, 20_000, 20, Now), Is.False);
                Assert.That(AutonomousLearnedDangerTrips.FinishWalkDangerous(Region, 490_000, 500_000, 510_000, 500_000, 9, Now),
                    Is.True, "the walk from the landing to the goal crosses the spot");
                Assert.That(AutonomousLearnedDangerTrips.FinishWalkDangerous(Region, 490_000, 500_000, 501_500, 500_000, 9, Now),
                    Is.False, "a goal at the spot: the bot was sent there (its camp choice judged it)");
                Assert.That(AutonomousLearnedDangerTrips.FinishWalkDangerous(Region, 490_000, 510_000, 510_000, 510_000, 9, Now),
                    Is.False, "a walk 10,000 away");
            });
        }

        // start (0,0), goal (100000,0), 100 units a second: walking all the way takes 1,000 s
        private static AutonomousStableRoutePlanner.RouteDecision? Choose(AutonomousStableRoutePlanner.LegMetric[] legs,
            int[] unusable = null, int[] finishUnsafe = null, bool directUnsafe = false) =>
            AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 100_000, 0, 100, 1_000, legs, null,
                unusable == null ? null : new System.Collections.Generic.HashSet<int>(unusable),
                finishUnsafe == null ? null : new System.Collections.Generic.HashSet<int>(finishUnsafe), directUnsafe);

        [Test]
        public void AnUnsafeFinishCostsTenMinutesSoASaferLandingWinsUpToThat()
        {
            // leg 0 lands 10,000 short (100 s ride + 100 s walk = 200 s); leg 1 lands on the goal
            var close = new[]
            {
                new AutonomousStableRoutePlanner.LegMetric(0, 0, 0, 90_000, 0, 100, 0),
                new AutonomousStableRoutePlanner.LegMetric(1, 0, 0, 100_000, 0, 650, 0),
            };
            var slow = new[]
            {
                close[0],
                new AutonomousStableRoutePlanner.LegMetric(1, 0, 0, 100_000, 0, 900, 0),
            };
            Assert.Multiple(() =>
            {
                Assert.That(Choose(close)?.FirstLegIndex, Is.EqualTo(0), "no danger: the faster landing");
                Assert.That(Choose(close, finishUnsafe: [0])?.FirstLegIndex, Is.EqualTo(1), "450 s slower and safe wins");
                Assert.That(Choose(close, finishUnsafe: [0])?.EstimatedSeconds, Is.EqualTo(650).Within(0.01),
                    "the estimate stays the real travel time");
                Assert.That(Choose(slow, finishUnsafe: [0])?.FirstLegIndex, Is.EqualTo(0), "700 s slower loses");
                Assert.That(Choose(slow, unusable: [0])?.FirstLegIndex, Is.EqualTo(1), "an unusable landing is never ridden");
            });
        }

        [Test]
        public void AnUnusableLandingIsNotRiddenAsALaterLegEither()
        {
            var legs = new[]
            {
                new AutonomousStableRoutePlanner.LegMetric(0, 0, 0, 50_000, 0, 100, 0),
                new AutonomousStableRoutePlanner.LegMetric(1, 50_000, 0, 100_000, 0, 10, 0),
            };
            var ride = new[] { new AutonomousStableRoutePlanner.LegMetric(0, 0, 0, 100_000, 0, 1_200, 0) };
            Assert.Multiple(() =>
            {
                Assert.That(Choose(legs)?.HopCount, Is.EqualTo(2));
                Assert.That(Choose(legs, unusable: [1])?.HopCount, Is.EqualTo(1), "the second horse lands beside a spot");
                Assert.That(Choose(legs, unusable: [1])?.EstimatedSeconds, Is.EqualTo(600).Within(0.01));
                Assert.That(Choose(ride), Is.Null, "walking (1,000 s) beats a 1,200 s ride");
                Assert.That(Choose(ride, directWalkUnsafe: true)?.FirstLegIndex, Is.EqualTo(0),
                    "a walk straight through a spot costs ten minutes more, so the horse wins");
            });
        }

        [Test]
        public void ALandingTurnsDeadlyAtTheSixthDifferentBotOfOneBand()
        {
            const long landed = 1_000_000;
            string made = null;
            for (int bot = 1; bot <= 5; bot++)
            {
                AutonomousStableLandingWatch.Arrived(bot, Region, "Howth", landed);
                made ??= AutonomousStableLandingWatch.Died(bot, 12, landed + 60_000);
            }
            AutonomousStableLandingWatch.Arrived(1, Region, "Howth", landed + 120_000);
            string again = AutonomousStableLandingWatch.Died(1, 13, landed + 180_000);
            bool afterFiveSameBot = AutonomousStableLandingWatch.IsDeadlyLanding(Region, "Howth", 12, landed + 180_000);
            AutonomousStableLandingWatch.Arrived(6, Region, "Howth", landed);
            string late = AutonomousStableLandingWatch.Died(6, 12, landed + AutonomousStableLandingWatch.WatchMilliseconds + 1);
            AutonomousStableLandingWatch.Arrived(7, Region, "Howth", landed + 200_000);
            string sixth = AutonomousStableLandingWatch.Died(7, 14, landed + 250_000);
            Assert.Multiple(() =>
            {
                Assert.That(made, Is.Null, "five bots are not enough");
                Assert.That(again, Is.Null);
                Assert.That(afterFiveSameBot, Is.False, "the same bot twice counts once");
                Assert.That(late, Is.Null, "a death more than five minutes after landing doesn't count");
                Assert.That(sixth, Is.EqualTo("Howth"), "the sixth different bot of the band makes it deadly");
                Assert.That(AutonomousStableLandingWatch.IsDeadlyLanding(Region, "Howth", 10, landed + 250_000), Is.True,
                    "levels 10-14 are one band");
                Assert.That(AutonomousStableLandingWatch.IsDeadlyLanding(Region, "Howth", 15, landed + 250_000), Is.False,
                    "another band");
                Assert.That(AutonomousStableLandingWatch.IsDeadlyLanding(Region, "Eilwen", 12, landed + 250_000), Is.False);
                Assert.That(AutonomousStableLandingWatch.IsDeadlyLanding(Region, "Howth", 12,
                    landed + 60_000 + AutonomousStableLandingWatch.DeadlyLandingWindowMilliseconds + 1), Is.False,
                    "the first deaths age out after an hour");
                Assert.That(AutonomousStableLandingWatch.CountsAsLandingDeath(0, 10), Is.False, "never landed");
            });
        }
    }
}
