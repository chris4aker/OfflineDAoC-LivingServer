using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The raid-route blockers that still kept raids from their big bosses in the owner's PC logs (build e3b8238 of
    /// 2026-11-14; Krondon from build e90bf21 of 2026-11-11). Tests only: none of this has been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidRouteBlockers12
    {
        // ---- 1. Gatekeeper Dommel, Darkness Falls -------------------------------------------------------------------

        [Test]
        public void DommelStaysDownWhileTheRaidIsInDarknessFalls()
        {
            // His row: DOL.GS.GameEpicNPC, RespawnInterval 2400 s, aggro 500, region 249 at 39793,32004,16493. He came back
            // after 08:12 at 10:04, 11:09, 12:06 and 12:47, each time behind the Hibernian raid.
            Assert.That(typeof(IGameEpicNpc).IsAssignableFrom(typeof(GameEpicNPC)), Is.True, "an epic: its respawn is held");
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, 2_400 * 1000, true), Is.True);
            Assert.That(AutonomousRealmRaid.EpicHoldRegionsOf(249, 249), Is.EqualTo(new ushort[] { 249 }),
                "Darkness Falls is entered from open ground: its own region");
        }

        [Test]
        public void TheRaidTrailThroughTheCrossingPassesDommel()
        {
            // The lead raider's walk across the central crossing (points 400 apart) and Dommel at his spawn.
            var dommel = new Vector3(39793, 32004, 16493);
            Vector3[] trail = [new(37989, 31963, 16497), new(38389, 31970, 16497), new(38789, 31980, 16497), new(39189, 31990, 16494),
                new(39589, 32000, 16494), new(39989, 32010, 16494)];
            Assert.That(RealmRaidRouteGate.Blocks(trail, dommel, 500), Is.True);
            Vector3[] otherFloor = trail.Select(p => p with { Z = p.Z + 1_500 }).ToArray();
            Assert.That(RealmRaidRouteGate.Blocks(otherFloor, dommel, 500), Is.False, "a trail on another floor");
            Assert.That(RealmRaidDungeonRoute.TrailGateCheckMilliseconds, Is.EqualTo(60_000));
        }

        // ---- 2. Summoner's Hall, Midgard: the walk back through Dodens Gruva ----------------------------------------------

        [Test]
        public void APullerInSummonersHallIsAwayForTheMembersInDodensGruva()
        {
            // AUTONOMOUS_DUNGEON_HOLD_LONG Brett region=246 unbroken_s=908 puller="Dawnblade" pullerRegion=248.
            Assert.That(AutonomousDungeonPolicy.PullerAway(true, true, 248, 246), Is.True);
            Assert.That(AutonomousDungeonPolicy.PullerAway(true, false, 246, 246), Is.True, "a dead puller");
            Assert.That(AutonomousDungeonPolicy.PullerAway(false, false, 0, 246), Is.True, "no puller");
            Assert.That(AutonomousDungeonPolicy.PullerAway(true, true, 246, 246), Is.False, "the party's puller is here");
        }

        [Test]
        public void AHeldRaidClearsOrdinaryMonstersOnItsWalk()
        {
            // raidHold=True: now the approach dungeon's ordinary monsters count as the raid's walk (raidTrash), as its own
            // dungeon's did; an epic there still waits for the raid's order (a route gate).
            Assert.That(AutonomousDungeonPolicy.ExpeditionMayClearCorridor(true, false, true, true), Is.True);
            Assert.That(AutonomousDungeonPolicy.ExpeditionMayClearCorridor(true, false, true, false), Is.False);
            Assert.That(AutonomousDungeonPolicy.PullsOnRoute(true, false, false), Is.False, "Aidon in Hall of the Corrupt");
        }

        // ---- 3. Krondon: Scurceol Hyrde's orbs ---------------------------------------------------------------------------

        [Test]
        public void TheOrbWaitsForTheRaidNotForAParty()
        {
            // 11 Nov: near=11, 8 and 6 of about 110 inside when each orb was skipped.
            Assert.That(RealmRaidDungeonRoute.RequiredAddMusterShort(11, 114), Is.True);
            Assert.That(RealmRaidDungeonRoute.RequiredAddMusterShort(8, 114), Is.True);
            Assert.That(RealmRaidDungeonRoute.RequiredAddMusterShort(38, 114), Is.False, "a third of the raid is at it");
            Assert.That(RealmRaidDungeonRoute.RequiredAddMusterShort(8, 12), Is.False, "a small raid: a party will do");
            Assert.That(RealmRaidDungeonRoute.RequiredAddMusterShort(7, 12), Is.True);
        }

        [Test]
        public void AStalledOrbSetsTheEncounterAsideInsteadOfMovingToTheNext()
        {
            Assert.That(EpicEncounterTactics.For("ScurceolHyrde")?.AddsInOrder, Is.True, "Scurceol's orbs die in order (#220)");
            Assert.That(RealmRaidDungeonRoute.RequiredAddStays(true), Is.True);
            Assert.That(RealmRaidDungeonRoute.RequiredAddStays(false), Is.False, "other targets are skipped as before");
        }

        [Test]
        public void ARaiderStuckInTheOgreCampJoinsTheRaid()
        {
            var stuck = new Vector3(31533, 35487, 15184);
            var front = new Vector3(52859, 21541, 17163);
            Vector3[] raiders = [new(33013, 31379, 15698), new(52700, 21400, 17160), new(52000, 20000, 17200)];
            Assert.That(RealmRaidStuckRecovery.Choose(stuck, front, raiders, out Vector3 spot, out int index), Is.True);
            Assert.That(index, Is.EqualTo(1), "the raider nearest the front");
            Assert.That(spot, Is.EqualTo(raiders[1]));
            Assert.That(RealmRaidStuckRecovery.Choose(stuck, front, [new(33013, 31379, 15698)], out spot, out index), Is.True);
            Assert.That((index, spot), Is.EqualTo((-1, front)), "nobody at the front: the front");
            Assert.That(RealmRaidStuckRecovery.Choose(front, front, raiders, out _, out _), Is.False, "already with the raid");
            Assert.That(RealmRaidStuckRecovery.Choose(stuck, default, [], out _, out _), Is.False, "no front");
        }

        // ---- 4. Snowdonia Station ---------------------------------------------------------------------------------------

        [Test]
        public void AReleaseWithoutAStagingGoesToThePartysPost()
        {
            var centre = new Vector3(523562, 408847, 4204);
            var post = new Vector3(524100, 409300, 4180);
            Assert.That(RealmRaidMuster.ReleasePoint(centre, post), Is.EqualTo(post));
            Assert.That(RealmRaidMuster.ReleasePoint(centre, default), Is.EqualTo(centre), "no post: the centre, as before");
        }

        // ---- 5. Hall of the Corrupt on Albion's way to Summoner's Hall -----------------------------------------------------

        [Test]
        public void AlbionsSummonersHallRouteGoesThroughHallOfTheCorruptAndGatesOnAidon()
        {
            // The world's zone points (opendaoc.sqlite3.db): 88 Snowdonia -> Hall of the Corrupt, 90 Hall of the Corrupt ->
            // Summoner's Hall, and the other realms' approaches (Dodens Gruva 93/92, Marfach Caverns 96).
            DbZonePoint[] crossings =
            [
                new() { Id = 88, SourceRegion = 1, TargetRegion = 277, Realm = 1, TargetX = 32395, TargetY = 31847, TargetZ = 16158 },
                new() { Id = 90, SourceRegion = 277, TargetRegion = 248, Realm = 1, SourceX = 26191, SourceY = 37697, SourceZ = 13476 },
                new() { Id = 93, SourceRegion = 100, TargetRegion = 246, Realm = 2 },
                new() { Id = 92, SourceRegion = 246, TargetRegion = 248, Realm = 2 },
                new() { Id = 96, SourceRegion = 276, TargetRegion = 248, Realm = 3 },
            ];
            var ways = RealmRaidNeutralEvents.Entrances(crossings, eRealm.Albion, 1, 248).ToArray();
            Assert.That(ways.Select(w => (w.Outer.Id, w.Inner.Id)), Is.EqualTo(new[] { (88, 90) }), "the only way in");
            Assert.That(ways[0].Outer.TargetRegion, Is.EqualTo(277).And.Not.EqualTo(ways[0].Inner.TargetRegion),
                "a dungeon on the way in: TryCreateGate looks for gates there");
            // Aidon is a gate candidate (an epic boss with aggro), and his room is on the walk to the way out.
            Assert.That(typeof(AidonTheArchwizard).IsSubclassOf(typeof(GameEpicBoss)), Is.True);
            var aidon = new Vector3(31353, 37634, 14873);
            Vector3[] walk = [new(32395, 31847, 16158), new(32242, 35521, 15369), new(31353, 37300, 14880), new(26191, 37697, 13476)];
            Assert.That(RealmRaidRouteGate.Blocks(walk, aidon, 600), Is.True);
            Assert.That(RealmRaidRouteGate.On, Is.True);
            // While the raid is in, Hall of the Corrupt's epics stay down too (Aidon came back at 10:42 on 14 Nov).
            Assert.That(AutonomousRealmRaid.EpicHoldRegionsOf(248, 277), Is.EqualTo(new ushort[] { 248, 277 }));
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, 3_600 * 1000, true), Is.True, "Aidon: 3600 s");
        }
    }
}
