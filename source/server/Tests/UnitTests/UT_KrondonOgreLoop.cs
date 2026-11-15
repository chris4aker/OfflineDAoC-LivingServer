using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Krondon, owner's PC, 2026-11-15 (build 1dc66b6, play-test sweep raid from 03:23:22): after Drihten Elreden died at
    /// 03:35:35 the raid fought only thrawn ogre bocere in his rooms (42 bocere spawns at 36,587-38,023 x 64,921-69,076 in
    /// the world database, RespawnInterval 180 s) until the log ended at 03:48, with 101 kills in region 61 in those 12
    /// minutes. The first-hour sweep took the walk-nearest monster every time. Owner's decision (2026-11-15): the sweep leaves
    /// out monsters that came back since the route began, except those on (or within their aggro of) the walk from the way
    /// in to the current target; the ten-minute raid respawn hold stays for neutral raids only. Tests only: none of this has
    /// been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_KrondonOgreLoop
    {
        private const long RouteStart = 5_000_000;

        [Test]
        public void TheSweepNeverClearsAMonsterThatCameBackSinceTheRouteBegan()
        {
            // A bocere on its first life (SpawnTick 0) or back before the raid came in is swept.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(RouteStart + 60_000, RouteStart, 0), Is.True);
            Assert.That(RealmRaidDungeonRoute.SweepTarget(RouteStart + 60_000, RouteStart, RouteStart - 1), Is.True);
            // One the raid killed at 03:31 and that came back 180 s later is not (no walk back known): fought only if it attacks.
            Assert.That(RealmRaidDungeonRoute.RespawnedSinceRouteStart(RouteStart + 480_000, RouteStart), Is.True);
            Assert.That(RealmRaidDungeonRoute.SweepTarget(RouteStart + 720_000, RouteStart, RouteStart + 480_000), Is.False);
            // After the first hour the sweep is over for every monster, as before.
            long late = RouteStart + RealmRaidDungeonRoute.EpicTrashPhaseMilliseconds;
            Assert.That(RealmRaidDungeonRoute.SweepTarget(late, RouteStart, 0), Is.False);
            // No route start yet: nothing counts as respawned.
            Assert.That(RealmRaidDungeonRoute.RespawnedSinceRouteStart(RouteStart, 0), Is.False);
        }

        // The walk back from the way in (32,000, 64,000) east to the front by Drihten's rooms, round one corner, on one floor.
        private static readonly Vector3[] Walk = RealmRaidDungeonRoute.WalkBackOf(new(32000, 64000, 14600), new(37500, 67000, 14600),
            [new(35000, 64000, 14600)]);

        [Test]
        public void ARespawnOnTheWalkBackIsClearedAgain() => Assert.Multiple(() =>
        {
            long now = RouteStart + 720_000, back = RouteStart + 480_000;
            Assert.That(Walk, Has.Length.EqualTo(3), "way in, corner, front");
            // A bocere back on the corridor the raiders walk in by after a wipe: cleared again.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(33500, 64100, 14600), 0), Is.True,
                "on the walk");
            // One 450 off the walk that aggroes at 300: it would pull the raiders walking by, so it is cleared too.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(33500, 64450, 14600), 300), Is.True,
                "within its aggro of the walk");
            // The same spot without aggro is off the walk (only Margin counts): left out.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(33500, 64450, 14600), 0), Is.False,
                "near the walk but no aggro");
            // A bocere back in a side room 2,000 off the walk: left out, as the sweep does today.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(33500, 66000, 14600), 300), Is.False,
                "off the walk");
            // Right under the walk on the floor 1,500 below: not on it.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(33500, 64000, 13100), 300), Is.False,
                "another floor");
            // On the last leg, from the corner to the front.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(now, RouteStart, back, Walk, new(36250, 65500, 14600), 0), Is.True,
                "on the leg to the front");
            // The first hour is still the limit, on the walk or not.
            Assert.That(RealmRaidDungeonRoute.SweepTarget(RouteStart + RealmRaidDungeonRoute.EpicTrashPhaseMilliseconds, RouteStart, back,
                Walk, new(33500, 64100, 14600), 300), Is.False, "after the first hour");
        });

        [Test]
        public void WithoutAPathTheWalkBackIsTheStraightLine()
        {
            Vector3[] walk = RealmRaidDungeonRoute.WalkBackOf(new(32000, 64000, 14600), new(37000, 64000, 14600), null);
            Assert.That(walk, Is.EqualTo(new Vector3[] { new(32000, 64000, 14600), new(37000, 64000, 14600) }));
            Assert.That(RealmRaidDungeonRoute.OnWalkBack(walk, new(34500, 64150, 14600), 0), Is.True);
            // A path that ends at the front adds no second front; one that stops short is completed to it.
            Assert.That(RealmRaidDungeonRoute.WalkBackOf(new(0, 0, 0), new(1000, 0, 0), [new(1000, 0, 0)]), Has.Length.EqualTo(2));
            Assert.That(RealmRaidDungeonRoute.WalkBackOf(new(0, 0, 0), new(1000, 0, 0), [new(500, 0, 0)]), Has.Length.EqualTo(3));
            Assert.That(RealmRaidDungeonRoute.OnWalkBack([], new(0, 0, 0), 300), Is.False, "no walk: nothing on it");
        }

        [Test]
        public void TheRaidRespawnHoldStaysForNeutralRaidsOnly() => Assert.Multiple(() =>
        {
            // Owner, 2026-11-15: Darkness Falls and Summoner's Hall only; Krondon's bocere keep their own 180 s.
            Assert.That(AutonomousRealmRaid.SlowRespawnRegions, Is.EquivalentTo(new ushort[]
                { RealmRaidNeutralEvents.DarknessFalls, RealmRaidNeutralEvents.SummonersHall }));
            Assert.That(AutonomousRealmRaid.SlowRespawnRegions, Does.Not.Contain((ushort)61), "Krondon");
            Assert.That(AutonomousRealmRaid.RaidDungeons.Select(d => d.Region).Intersect(AutonomousRealmRaid.SlowRespawnRegions), Is.Empty);
        });
    }
}
