using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The September 13 installed route samples, asserted as a bot walks them (#704): from the floor-step start, by the
    /// direct corridor or the zone road route, every leg complete. Every sample is checked in one run (Assert.Multiple).
    /// The known cut in the direct mesh query (region 1 frontier) is pinned on its own in <see cref="FrontierDirectQueryCut"/>.
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Read-only installed route probes")]
    public class UT_SeptemberThirteenInstalledRoutes
    {
        private static readonly Vector3 FrontierStart = new(655198, 292757, 4859);
        private static readonly Vector3 FrontierTarget = new(605390, 293864, 4839);
        /// <summary>Where the single direct query from <see cref="FrontierStart"/> stops on navmesh-v1 (#704, 2026-11-27).</summary>
        private static readonly Vector3 FrontierCut = new(635744, 298086, 4307);

        private static readonly (int Region, Vector3 From, Vector3 To, string Name, InstalledBotRoute.Walk Walk)[] Samples =
        [
            (200, new(378702, 497483, 5434), new(377976, 495314, 5198), "dergan", InstalledBotRoute.Walk.Direct),
            (181, new(400589, 420259, 4598), new(403982, 421754, 4406), "sporite", InstalledBotRoute.Walk.Direct),
            (100, new(707427, 773530, 4624), new(708401, 772052, 4553), "blodfelag", InstalledBotRoute.Walk.Direct),
            (1, new(535933, 608077, 2962), new(533198, 611484, 2272), "spider", InstalledBotRoute.Walk.Direct),
            // About 49,800 apart: a bot goes by the zone roads (the direct query stops at the cut, pinned below).
            (1, FrontierStart, FrontierTarget, "frontier", InstalledBotRoute.Walk.Road),
            (200, new(474010, 318918, 5786), new(476053, 343166, 4105), "frontier", InstalledBotRoute.Walk.None),
            (128, new(31644, 32996, 16015), Vector3.Zero, "cave spider", InstalledBotRoute.Walk.None),
            (222, new(32450, 32804, 14962), Vector3.Zero, "spraggonix", InstalledBotRoute.Walk.None),
            (223, new(27280, 32217, 17271), Vector3.Zero, "pelagian crab", InstalledBotRoute.Walk.None),
        ];

        [Test]
        public void Probe()
        {
            WithRegions((db, regions, nav) => Assert.Multiple(() =>
            {
                foreach (var sample in Samples)
                {
                    Region region = regions(sample.Region);
                    Zone zone = region.GetZone((int)sample.From.X, (int)sample.From.Y);
                    if (sample.Region == 200 && sample.Name == "frontier")
                    {
                        // The Emain hotspot: unchanged since September. The immediate escape is close and connected.
                        bool escaped = AutonomousRouteHotspotRepair.TryGetImmediateEscape(nav, region, 200, sample.From, out var escape);
                        {
                            Vector3? f = nav.GetClosestPoint(zone, sample.From, 64, 64, 128, nav.DefaultFilters);
                            Vector3? e = nav.GetClosestPoint(zone, new(474044, 318952, 5838), 96, 96, 512, nav.DefaultFilters);
                            Vector3? w = nav.GetClosestPoint(zone, sample.To, 96, 96, 512, nav.DefaultFilters);
                            TestContext.WriteLine($"EMAIN escaped={escaped} floor={f} planning={AutonomousNavigationSurface.TryPlanningFloor(nav, zone, sample.From, out _)} " +
                                $"escapeFloor={e} witness={w} escapeExit={e.HasValue && AutonomousRendezvousNavigation.HasLocalExit(nav, zone, e.Value)} " +
                                $"escape->witness={e.HasValue && w.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, e.Value, w.Value)} " +
                                $"source->escape={e.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, sample.From, e.Value)} " +
                                $"sourceWalk={InstalledBotRoute.SameZone(nav, zone, f ?? sample.From, sample.To, out string emainWalk)} ({emainWalk})");
                        }
                        Assert.That(escaped, Is.True, "Emain frontier: immediate escape");
                        if (escaped)
                        {
                            Assert.That(Vector3.Distance(escape, sample.From), Is.LessThan(100), "Emain frontier: escape distance");
                            Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, escape, sample.To), Is.True,
                                "Emain frontier: corridor from the escape");
                        }
                        Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(nav, region, 200, sample.From + new Vector3(0, 0, 200), out _),
                            Is.False, "Emain frontier: no escape 200 above");
                        continue;
                    }
                    foreach (Vector3 target in Targets(db, sample))
                    {
                        // Plan from where the floor step puts the bot (blodfelag's capture is 3 units off the 1.2 mesh edge).
                        Vector3 start = PlanningStart(nav, zone, sample.From);
                        bool approach = AutonomousZonePointApproach.TryResolve(nav, zone, start, target, 112, out var ap);
                        Assert.That(approach, Is.True, $"Approach {sample.Name} {target}");
                        if (sample.To == Vector3.Zero)
                        {
                            TestContext.WriteLine($"AUDIT {sample.Region} {sample.Name} from={start} target={target} approach={approach}:{ap}");
                            continue;
                        }
                        var walk = InstalledBotRoute.SameZone(nav, zone, start, target, out string detail, out Vector3[] legEnds);
                        Assert.That(walk, Is.EqualTo(sample.Walk), $"{sample.Name} {target}: the bot's walk ({detail})");
                        if (sample.Walk == InstalledBotRoute.Walk.Direct)
                        {
                            var nodes = new WrappedPathfindingNode[256];
                            var path = nav.GetPathStraight(zone, start, target, nav.DefaultFilters, nodes);
                            Assert.That(path.Status, Is.EqualTo(PathfindingStatus.PathFound), $"{sample.Name} {target}: direct path");
                        }
                        // Corner recovery follows the leg the bot is on: the target itself on a direct walk, the first road
                        // anchor on a road walk (it needs a direct path to its destination, which the cut denies; see the pin).
                        Vector3 leg = legEnds.Length > 0 ? legEnds[0] : target;
                        bool recovery = AutonomousCorridorRecovery.TryNextCorner(nav, zone, start, leg, out var corner);
                        Assert.That(recovery, Is.True, $"Corner {sample.Name} toward {leg}");
                        TestContext.WriteLine($"AUDIT {sample.Region} {sample.Name} from={start} target={target} walk={walk} ({detail}) " +
                            $"approach={approach}:{ap} corner={recovery}:{corner}");
                    }
                }
            }));
        }

        /// <summary>
        /// The known cut (#704): the single direct query from the region 1 frontier start stops at 635744,298086, about
        /// 30,650 short, and nothing nearer the target is reachable from there. Bots take the road route (<see cref="Probe"/>).
        /// Pinned so the cut stays visible: when a mesh change joins it, this fails and the pin is updated.
        /// </summary>
        [Test]
        public void FrontierDirectQueryCut()
        {
            WithRegions((db, regions, nav) =>
            {
                Zone zone = regions(1).GetZone((int)FrontierStart.X, (int)FrontierStart.Y);
                var nodes = new WrappedPathfindingNode[1024];
                var path = nav.GetPathStraight(zone, FrontierStart, FrontierTarget, nav.DefaultFilters, nodes);
                Vector3 end = path.NodeCount > 0 ? nodes[Math.Min(path.NodeCount, nodes.Length) - 1].Position : FrontierStart;
                TestContext.WriteLine($"FRONTIER_CUT status={path.Status} nodes={path.NodeCount} end={end}");
                Assert.Multiple(() =>
                {
                    Assert.That(path.Status, Is.EqualTo(PathfindingStatus.PartialPathFound), "the direct query is partial");
                    Assert.That(Vector3.Distance(end, FrontierCut), Is.LessThan(64), $"the direct query stops at the cut (ends at {end})");
                    Assert.That(AutonomousZoneItinerary.HasDirectCorridor(nav, zone, FrontierStart, FrontierTarget), Is.False,
                        "no direct corridor across the cut");
                    Assert.That(AutonomousCorridorRecovery.TryNextCorner(nav, zone, FrontierStart, FrontierTarget, out _), Is.False,
                        "corner recovery toward the far target needs the direct path the cut denies");
                    var onward = nav.GetPathStraight(zone, end, FrontierTarget, nav.DefaultFilters, nodes);
                    Vector3 onwardEnd = onward.NodeCount > 0 ? nodes[Math.Min(onward.NodeCount, nodes.Length) - 1].Position : end;
                    Assert.That(Vector3.Distance(onwardEnd, FrontierTarget), Is.GreaterThan(Vector3.Distance(end, FrontierTarget) - 64),
                        $"nothing nearer the target is reachable from the cut (a second query ends at {onwardEnd})");
                });
            });
        }

        private static IEnumerable<Vector3> Targets(SQLiteConnection db, (int Region, Vector3 From, Vector3 To, string Name, InstalledBotRoute.Walk Walk) sample)
        {
            if (sample.To != Vector3.Zero)
                return [sample.To];
            var raw = new List<Vector3>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "select X,Y,Z from Mob where Region=@r and Name=@n";
                cmd.Parameters.AddWithValue("@r", sample.Region);
                cmd.Parameters.AddWithValue("@n", sample.Name);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) raw.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
            }
            // Test installed bot goals, not legacy inaccessible NPC rows which are already excluded from the goal catalog.
            var verified = AutonomousDungeonGoalCatalog.VerifiedPointsForRegion((ushort)sample.Region);
            var targets = raw.Select(r => verified.FirstOrDefault(p => p.Name == sample.Name &&
                    Vector3.DistanceSquared(r, new(p.Spawn[0], p.Spawn[1], p.Spawn[2])) <= 4))
                .Where(p => p != null).Select(p => p.Position).ToList();
            Assert.That(targets, Is.Not.Empty, $"{sample.Name}: verified goal points in region {sample.Region}");
            return targets;
        }

        private static void WithRegions(Action<SQLiteConnection, Func<int, Region>, IPathfindingMgr> body)
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            var regions = new Dictionary<int, Region>();
            try
            {
                InstalledDetour.Use(root);
                Environment.CurrentDirectory = root;
                using var db = new SQLiteConnection($"Data Source={Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"))};Read Only=True;Pooling=False;");
                db.Open();
                Region Get(int id)
                {
                    if (!regions.TryGetValue(id, out Region region))
                        regions[id] = region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, id, loaded);
                    return region;
                }
                body(db, Get, PathfindingProvider.LocalPathfindingMgr);
            }
            finally
            {
                foreach (var zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
        }

        /// <summary>
        /// Where a bot at <paramref name="capture"/> plans from: the capture itself when it stands on floor, otherwise the floor
        /// the bot's floor step moves it to (TryNearestWalkableFloor, 300 units, as AutonomousWorldBotController.TryFloorStep).
        /// Asserts that floor exists and is close: a capture far from any floor is a real failure, not an off-edge capture.
        /// </summary>
        internal static Vector3 PlanningStart(IPathfindingMgr nav, Zone zone, Vector3 capture)
        {
            if (AutonomousNavigationSurface.TryPlanningFloor(nav, zone, capture, out _))
                return capture;
            Assert.That(AutonomousNavigationSurface.TryNearestWalkableFloor(nav, zone, capture, 300, out Vector3 floor), Is.True,
                $"no walkable floor near the capture {capture}");
            Assert.That(Vector2.Distance(new(floor.X, floor.Y), new(capture.X, capture.Y)), Is.LessThan(32),
                $"the floor step from {capture} lands at {floor}: too far to call the capture just off the edge");
            TestContext.WriteLine($"OFF_MESH_CAPTURE {capture} floor={floor}");
            return floor;
        }
    }
}
