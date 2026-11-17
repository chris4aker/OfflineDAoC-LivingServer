using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Darkness Falls Albion, owner's PC, 2026-11-16 (build ccba364, /epictest sweep): Chthonic Knight Ukobat, on the raid's
    /// trail, was killed about nine times. Each time he came back the route still held the attempt of his last life open and
    /// scored his fresh 100% against that life's low: REALM_EXPEDITION_ENCOUNTER_RESET at 22:37:13 (engaged=58 fallen=0
    /// lowest=18) and 22:46:23 (engaged=51 fallen=0 lowest=66), then REALM_RAID_WIPE failures=1 living=64 engaged=54 fallen=0
    /// (22:55:28, lowest=14) and failures=2 living=87 engaged=68 fallen=2 (23:07:42, lowest=68): "Defeated at Chthonic Knight
    /// Ukobat". Every one came 10-11 s after the route turned to his new life, 3-4 minutes after a
    /// REALM_EXPEDITION_TARGET_DEFEATED. Raid-wide deaths outside an attempt (the other way a wipe is scored) now count once per
    /// fight. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidRespawnIsNoWipe
    {
        private static long At(int hour, int minute, int second) => ((hour * 60L + minute) * 60 + second) * 1000;

        [Test]
        public void UkobatKilledAndBackSinceThePullEndedThatAttemptAsAWin()
        {
            Assert.Multiple(() =>
            {
                // 22:51:25 FINAL_ENGAGE, 22:51:56 defeated, back by 22:55:17 (the route's switch to him), 22:55:28 scored a wipe.
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(22, 51, 25), At(22, 55, 17)), Is.True);
                // 23:03:59 FINAL_ENGAGE, 23:04:19 defeated, back by 23:07:31, 23:07:42 scored the second wipe.
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(23, 3, 59), At(23, 7, 31)), Is.True);
                // 22:32:39 early pull, 22:34:00 defeated, back by 22:37:03, 22:37:13 scored a free reset.
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(22, 32, 39), At(22, 37, 3)), Is.True);
            });
        }

        [Test]
        public void TheSameLifeKeepsItsAttempt()
        {
            Assert.Multiple(() =>
            {
                // A boss on the life he was pulled on (spawned before the pull, or still on his first life: SpawnTick 0).
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(22, 51, 25), At(22, 20, 0)), Is.False);
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(22, 51, 25), 0), Is.False);
                // No attempt yet.
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(0, At(22, 55, 17)), Is.False);
            });
        }

        [Test]
        public void TheRulesThatScoredUkobatStillScoreARealWipeOrReset()
        {
            Assert.Multiple(() =>
            {
                // What the stale attempt was scored with: the reset rule (healed to 95%+ after 70% or lower). These verdicts are
                // unchanged; they are no longer reached for a new life.
                Assert.That(RealmRaidDungeonRoute.IsWipe(54, 0, 100, 14), Is.True);
                Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(54, 0, 100, 14), Is.True);
                Assert.That(RealmRaidDungeonRoute.IsWipe(68, 2, 100, 68, 87), Is.True);
                // A real wipe on the life he was pulled on still counts: Trollheim, 2026-11-17 00:12:12, Nosdoden
                // engaged=62 fallen=51 (living raid 64 at the pull).
                Assert.That(RealmRaidDungeonRoute.KilledSinceAttempt(At(0, 9, 51), At(0, 5, 0)), Is.False);
                Assert.That(RealmRaidDungeonRoute.IsWipe(62, 51, 100, 82, 64), Is.True);
            });
        }

        // Tuscaran Glacier, owner's PC, 2026-11-16 (build ccba364): one unplanned fight with Queen Kula and King Tuscar during a
        // regroup was scored twice in 111 s as "raid-wide deaths outside an attempt" at King Tuscar: 20:39:59 engaged=129
        // fallen=65 living=88, then 20:41:50 engaged=76 fallen=38 living=85, and the second ended the raid.
        [Test]
        public void ARouteWipeIsCountedOncePerFight()
        {
            Assert.Multiple(() =>
            {
                // The first count stands: 65 of the 129 inside fell.
                Assert.That(RealmRaidDungeonRoute.CountsRouteWipe(65, 64, 88, fightAlreadyCounted: false), Is.True);
                // The same fight 111 s later, King Tuscar never out of combat: no second wipe.
                Assert.That(RealmRaidDungeonRoute.CountsRouteWipe(38, 38, 85, fightAlreadyCounted: true), Is.False);
                // After he left combat a new raid-wide loss is a new fight, and counts.
                Assert.That(RealmRaidDungeonRoute.CountsRouteWipe(38, 38, 85, fightAlreadyCounted: false), Is.True);
            });
        }

        [Test]
        public void ARouteWipeStillNeedsHalfOfTheRaidersInsideToFall()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.CountsRouteWipe(30, 90, 120, false), Is.False);
                // Icelord Agmundr, 2026-11-14 (the case the rule was made for): 80 fell in two minutes, a handful stood.
                Assert.That(RealmRaidDungeonRoute.CountsRouteWipe(80, 10, 20, false), Is.True);
            });
        }
    }
}
