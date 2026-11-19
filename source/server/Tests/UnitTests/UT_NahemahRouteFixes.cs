using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Princess Nahemah's route (1.1.2 test copy, 2026-11-07 10:59-11:14): after Prince Ba'alorien died the Hibernian raid's
    /// path to her muster came back NoPathFound for every raider, 37 gave up the goal and the raid's own event camp was
    /// quarantined for two hours. On the installed zone249 mesh (UT_DarknessFallsRouteProbe) the destination is on the
    /// floor; Detour cut the 20,000-unit corridor at its 256-polygon limit (PartialPathFound, 239 nodes, ending at
    /// 25380,25567,18128) and DF's strict validator rejects every partial path. Both halves are complete strict segments.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_NahemahRouteFixes
    {
        [SetUp]
        public void Reset() => AutonomousCampRouteQuarantine.ResetForTests();

        [TearDown]
        public void Clean() => AutonomousCampRouteQuarantine.ResetForTests();

        [Test]
        public void ARealmRaidsOwnEventCampIsNeverQuarantined()
        {
            var now = new DateTime(2026, 11, 7, 16, 0, 1, DateTimeKind.Utc);
            const string camp = "realm-event-darkness-hibernia";
            foreach (long bot in new long[] { 2401, 702, 2251, 2298, 2221 })
                Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, bot, now), Is.False);
            Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, now), Is.False);
            Assert.That(AutonomousCampRouteQuarantine.IsRealmRaidCamp("Realm-Event-epic-albion"), Is.True);
        }

        [Test]
        public void AnOrdinaryCampIsStillBenchedByThreeBots()
        {
            var now = new DateTime(2026, 11, 7, 16, 0, 1, DateTimeKind.Utc);
            const string camp = "df-live:8806ee02:249:plated fiend";
            Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 1, now), Is.False);
            Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 2, now), Is.False);
            Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 3, now), Is.True);
            Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, now), Is.True);
        }

        private static readonly Vector3 Room = new(36668, 15524, 16064), Muster = new(19939, 24961, 17010);

        private static WrappedPathfindingNode[] Path(params Vector3[] points)
        {
            var nodes = new WrappedPathfindingNode[points.Length];
            for (int i = 0; i < points.Length; i++) nodes[i] = new(points[i], EDtPolyFlags.Walk);
            return nodes;
        }

        [Test]
        public void ACorridorCutShortOnALongWalkIsATruncatedCorridor()
        {
            WrappedPathfindingNode[] nodes = Path(Room, new(30000, 20000, 16500), new(25380, 25567, 18128));
            Assert.That(AutonomousDarknessFallsNavigation.IsTruncatedCorridor(
                new(PathfindingStatus.PartialPathFound, nodes.Length), nodes, Room, Muster), Is.True);
        }

        [Test]
        public void OnlyAPartialPathWithRealProgressShortOfTheGoalIsTruncated()
        {
            WrappedPathfindingNode[] reached = Path(Room, new(30000, 20000, 16500), Muster);
            Assert.That(AutonomousDarknessFallsNavigation.IsTruncatedCorridor(
                new(PathfindingStatus.PathFound, reached.Length), reached, Room, Muster), Is.False, "a complete path");
            Assert.That(AutonomousDarknessFallsNavigation.IsTruncatedCorridor(
                new(PathfindingStatus.PartialPathFound, reached.Length), reached, Room, Muster), Is.False, "ends at the goal");
            WrappedPathfindingNode[] stuck = Path(Room, Room + new Vector3(100, 0, 0));
            Assert.That(AutonomousDarknessFallsNavigation.IsTruncatedCorridor(
                new(PathfindingStatus.PartialPathFound, stuck.Length), stuck, Room, Muster), Is.False, "no real progress");
            Assert.That(AutonomousDarknessFallsNavigation.IsTruncatedCorridor(
                new(PathfindingStatus.NoPathFound, 0), stuck, Room, Muster), Is.False);
        }

        [Test]
        public void TheCutCorridorsEndIsStillWalkedOnlyAsAStrictSegment()
        {
            // The prefix to the cut point is re-queried and must pass the strict validator: a partial prefix (Detour's fake
            // endpoint after a Z leap) or a 900-unit vertical step is refused like before.
            Vector3 cut = new(25380, 25567, 18128);
            WrappedPathfindingNode[] leap = Path(Room, new(Room.X + 50, Room.Y, Room.Z + 900), cut);
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(new(PathfindingStatus.PathFound, leap.Length), leap, Room, cut),
                Is.False);
            WrappedPathfindingNode[] partial = Path(Room, new(30000, 20000, 16500));
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(new(PathfindingStatus.PartialPathFound, partial.Length),
                partial, Room, cut), Is.False);
        }
    }
}
