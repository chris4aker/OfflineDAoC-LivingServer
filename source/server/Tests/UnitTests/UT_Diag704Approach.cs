// DIAGNOSTIC ONLY (#704, Account #2): not for merge. Why does AutonomousZonePointApproach.TryResolve say no for
// portal 165 and blodfelag when a complete corridor exists? Mirrors TryResolve step by step and prints each step.
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable, Explicit("diagnostic #704")]
public class UT_Diag704Approach
{
    [Test]
    public void PrintApproach()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var report = new StringBuilder();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory = root;
            AutonomousZoneSeams.SetForTests(null);
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 100, loaded);
            IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 portal165;
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "select SourceX,SourceY,SourceZ from ZonePoint where Id=165 and SourceRegion=100";
                using var row = cmd.ExecuteReader();
                Assert.That(row.Read(), Is.True);
                portal165 = new(row.GetInt32(0), row.GetInt32(1), row.GetInt32(2));
            }
            var cases = new (string Name, Vector3 Start, Vector3 Goal, int Radius)[]
            {
                ("portal 165", new(808662,724912,4883), portal165, 174),
                ("mularn stable -> 165 (passes?)", new(803743,722129,4684), portal165, 174),
                ("blodfelag", new(707427,773530,4624), new(708401,772052,4553), 112),
            };
            foreach (var c in cases)
            {
                Zone zone = region.GetZone((int)c.Start.X, (int)c.Start.Y);
                Zone goalZone = region.GetZone((int)c.Goal.X, (int)c.Goal.Y);
                bool resolved = AutonomousZonePointApproach.TryResolve(nav, zone, c.Start, c.Goal, c.Radius, out Vector3 ap);
                report.AppendLine($"DIAG704A {c.Name}: zone={zone?.ID} goalZone={goalZone?.ID} start={c.Start} goal={c.Goal} radius={c.Radius} TryResolve={resolved} approach={ap}");
                bool tf = AutonomousNavigationSurface.TryFloor(nav, zone, c.Start, out Vector3 tfloor);
                bool pf = AutonomousNavigationSurface.TryPlanningFloor(nav, zone, c.Start, out Vector3 start);
                Vector3? wide = nav.GetClosestPoint(zone, c.Start, 64, 64, 128, nav.DefaultFilters);
                report.AppendLine($"  TryFloor={tf} {tfloor} TryPlanningFloor={pf} {start} wide64={wide}");
                if (!pf) start = wide ?? c.Start;
                Vector3? goalFloor = nav.GetClosestPoint(zone, c.Goal, 48, 48, 192, nav.DefaultFilters);
                Vector3? goalWide = nav.GetClosestPoint(zone, c.Goal, 96, 96, 512, nav.DefaultFilters);
                report.AppendLine($"  goalFloor48={goalFloor} goalWide96={goalWide}");
                report.AppendLine($"  corridor start->goal={AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start, c.Goal)} " +
                    $"start->goalFloor={(goalFloor.HasValue ? AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start, goalFloor.Value) : false)} " +
                    $"wide->goal={(wide.HasValue ? AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, wide.Value, c.Goal) : false)} " +
                    $"roads start->goal={AutonomousZoneRoads.TryRoute(nav, zone, start, c.Goal, out _)}");
                var raws = new List<(string, Vector3, int, int)> { ("portal", c.Goal, 48, 192) };
                int[] radii = [Math.Max(32, c.Radius / 3), Math.Max(48, c.Radius * 2 / 3), Math.Max(64, c.Radius - 20)];
                foreach (int r in radii.Distinct())
                for (int angle = 0; angle < 360; angle += 30)
                {
                    float rad = angle * MathF.PI / 180f;
                    raws.Add(($"r{r}a{angle}", new(c.Goal.X + MathF.Cos(rad) * r, c.Goal.Y + MathF.Sin(rad) * r, c.Goal.Z), 36, 128));
                }
                int kept = 0;
                foreach (var (label, raw, xy, z) in raws)
                {
                    Vector3? fl = nav.GetClosestPoint(zone, raw, xy, xy, z, nav.DefaultFilters);
                    string why = "kept";
                    if (!fl.HasValue) why = "nofloor";
                    else if (Vector2.Distance(new(fl.Value.X, fl.Value.Y), new(c.Goal.X, c.Goal.Y)) > c.Radius) why = "far";
                    else if (MathF.Abs(fl.Value.Z - c.Goal.Z) > 256) why = $"dz={fl.Value.Z - c.Goal.Z:0}";
                    bool corr = why == "kept" && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start, fl.Value);
                    if (why == "kept") kept++;
                    if (label == "portal" || why != "nofloor")
                        report.AppendLine($"  cand {label}: floor={fl} {why} corridor={corr}" +
                            (why == "kept" && !corr ? $" pathStatus={PathStatus(nav, zone, start, fl.Value)}" : ""));
                }
                report.AppendLine($"  candidates kept={kept}");
            }
        }
        finally
        {
            AutonomousZoneSeams.SetForTests(null);
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
        }
        Assert.Fail("DIAGNOSTIC REPORT\n" + report);
    }

    [Test]
    public void PrintFrontier()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var report = new StringBuilder();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory = root;
            AutonomousZoneSeams.SetForTests(null);
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 1, loaded);
            IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 from = new(655198, 292757, 4859), to = new(605390, 293864, 4839);
            Zone zone = region.GetZone((int)from.X, (int)from.Y);
            Zone toZone = region.GetZone((int)to.X, (int)to.Y);
            report.AppendLine($"DIAG704F zone={zone?.ID} {zone?.Description} toZone={toZone?.ID} {toZone?.Description} dist={Vector3.Distance(from, to):0}");
            Vector3? sf = nav.GetClosestPoint(zone, from, 96, 96, 512, nav.DefaultFilters);
            Vector3? tf = nav.GetClosestPoint(zone, to, 96, 96, 512, nav.DefaultFilters);
            report.AppendLine($"  startFloor={sf} targetFloor={tf} planning={AutonomousNavigationSurface.TryPlanningFloor(nav, zone, from, out Vector3 ps)} {ps}");
            foreach (int size in new[] { 256, 1024, 4096 })
            {
                var buf = new WrappedPathfindingNode[size];
                var path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, buf);
                string last = path.NodeCount > 0 ? buf[Math.Min(path.NodeCount, size) - 1].Position.ToString() : "-";
                float walked = 0; for (int i = 1; i < Math.Min(path.NodeCount, size); i++) walked += Vector3.Distance(buf[i - 1].Position, buf[i].Position);
                report.AppendLine($"  buffer {size}: {path.Status} nodes={path.NodeCount} last={last} lastToTarget={(path.NodeCount > 0 ? Vector3.Distance(buf[Math.Min(path.NodeCount, size) - 1].Position, to) : -1):0} walked={walked:0}");
            }
            report.AppendLine($"  HasCompleteCorridor={AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, to)} roads={AutonomousZoneRoads.TryRoute(nav, zone, from, to, out var roadRoute)} roadPoints={roadRoute?.Length}");
            // Walk the partial path in legs: from the last reached node, does a second query reach the target?
            var b = new WrappedPathfindingNode[4096];
            Vector3 cur = from;
            for (int leg = 0; leg < 6; leg++)
            {
                var p2 = nav.GetPathStraight(zone, cur, to, nav.DefaultFilters, b);
                int n = Math.Min(p2.NodeCount, b.Length);
                Vector3 end = n > 0 ? b[n - 1].Position : cur;
                report.AppendLine($"  leg {leg}: from {cur} {p2.Status} nodes={p2.NodeCount} end={end} endToTarget={Vector3.Distance(end, to):0}");
                if (p2.Status == PathfindingStatus.PathFound || n == 0 || Vector3.Distance(end, cur) < 50) break;
                cur = end;
            }
        }
        finally
        {
            AutonomousZoneSeams.SetForTests(null);
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
        }
        Assert.Fail("DIAGNOSTIC REPORT\n" + report);
    }

    private static string PathStatus(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to)
    {
        Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
        var path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
        string last = path.NodeCount > 0 ? nodes[path.NodeCount - 1].Position.ToString() : "-";
        return $"{path.Status}/{path.NodeCount} last={last}";
    }
}
