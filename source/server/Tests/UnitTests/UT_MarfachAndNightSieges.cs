using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Overnight run 2026-11-04/05 (build 616190a): the Marfach Caverns raid timed out after 91 pulls on an Ancient Black
    /// Oak that never entered combat, and the night's sieges were logged without the army they could draw on
    /// (docs/NIGHT-SIEGES-AND-MARFACH.md).
    /// </summary>
    [TestFixture]
    public class UT_MarfachAndNightSieges
    {
        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(91, true)]
        public void ThreePullsThatReachNobodySetTheEncounterAside(int pulls, bool setAside) =>
            Assert.That(RealmRaidDungeonRoute.PullsNeverReached(pulls), Is.EqualTo(setAside));

        [Test]
        public void TheMarfachLoopEndsOnTheThirdEmptyPull()
        {
            // The run: every pull went out, waited the two-minute grace, and ended with the Oak out of combat and no wipe.
            var pulls = new RealmRaidDungeonRoute.PullContact();
            int setAsideAt = 0;
            for (int pull = 1; pull <= 91 && setAsideAt == 0; pull++)
            {
                pulls.Begin();
                if (pulls.Ended(wiped: false)) setAsideAt = pull;
            }
            Assert.That(setAsideAt, Is.EqualTo(RealmRaidDungeonRoute.MaximumPullsWithoutContact));
        }

        [Test]
        public void APullThatReachesTheEncounterStartsTheCountAgain() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(2));
            // The third pull gets a fight going and the raid breaks off: an ordinary attempt, not an unreachable encounter.
            pulls.Begin();
            pulls.SawCombat();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False, "one empty pull after a fight is not three in a row");
        });

        [Test]
        public void AWipeIsAFailedAttemptNotAnUnreachableEncounter() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            for (int i = 0; i < 5; i++)
            {
                pulls.Begin();
                Assert.That(pulls.Ended(wiped: true), Is.False);
            }
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
        });

        [Test]
        public void ANewEncounterStartsWithNoEmptyPulls()
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin(); pulls.Ended(false);
            pulls.Begin(); pulls.Ended(false);
            pulls.Reset();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(1));
        }

        [Test]
        public void TheSiegePoolCountsTheAttackersLevelFiftiesAndTheirRvrDuty()
        {
            var bots = new (eRealm, int, bool)[]
            {
                (eRealm.Albion, 50, true), (eRealm.Albion, 50, false), (eRealm.Albion, 49, true),
                (eRealm.Midgard, 50, true), (eRealm.Albion, 50, true),
            };
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Albion), Is.EqualTo((3, 2)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Hibernia), Is.EqualTo((0, 0)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(null, eRealm.Albion), Is.EqualTo((0, 0)));
        }

        [Test]
        public void TheMarchLineNamesTheArmyAndItsPool() =>
            Assert.That(AutonomousRvrEventLayer.SiegeArmyFields(72, 594, 180, 16, 0.5),
                Is.EqualTo("assigned=72 realmLevel50=594 realmRvr=180 inOtherSieges=16 defenseShare=0.5"));
    }
}
