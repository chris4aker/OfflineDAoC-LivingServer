using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool (RvR lane survey): batch walks of multi-zone trips on the real meshes, as UT_TripProbe / UT_EventRouteAudit
    /// walk them (zone steps from AutonomousZoneItinerary.ProbeNextStep, Detour inside a zone continued from the end of
    /// a partial path, the zone road route for a far-short partial), but with every path node written out so a lane's
    /// separation from another lane, from keeps and from monster camps can be measured. Read only.
    /// Env: OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_ZONES ("id:x:y:w:h;..."), OFFLINE_DAOC_REGION as UT_TripProbe;
    /// LANE_JOBS = a text file with one job per line, LANE_OUT = the result file (one line per job, same order).
    /// Jobs:
    ///   snap NAME x,y,z RADIUS          nearest mesh floor within RADIUS (and 1024 in height), whether it has a local
    ///                                   way out, and its road-graph ground (area id)
    ///   trip NAME x,y,z>x,y,z>...       start, then each target in turn (via points, then the goal)
    /// Result lines:
    ///   snap NAME floor=0|1 at=x,y,z exit=0|1 area=N
    ///   trip NAME ok=0|1 len=N legs=a,b,.. jumps=N swim=N doors=N direct=0|1,.. cross=from>to@x,y,z;.. fail=text pts=x,y,z,flags ...
    /// (direct: per leg, whether the last zone's walk is a complete Detour corridor without the road graph, which is what
    /// RvrKeepRoute.TryBuild asks of the final leg today.)
    /// The first result line is "version UT_RvrLaneSurvey/N"; the lane catalog's proof hashes include it
    /// (source/tools/rvr_lanes/build_rvr_lanes.py builds bots/autonomous/data/rvr-lanes.json with this tool).
    /// </summary>
    [TestFixture, Explicit("RvR lane survey tool"), NonParallelizable]
    public class UT_RvrLaneSurvey
    {
        /// <summary>Raise when the walk changes, so catalog proofs made with an older walk no longer match.</summary>
        public const string ToolVersion = "UT_RvrLaneSurvey/2";

        [Test]
        public void Survey()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string zoneSpec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_ZONES");
            string jobs = Environment.GetEnvironmentVariable("LANE_JOBS");
            string output = Environment.GetEnvironmentVariable("LANE_OUT");
            Assume.That(root != null && zoneSpec != null && jobs != null && output != null);
            ushort region = ushort.Parse(Environment.GetEnvironmentVariable("OFFLINE_DAOC_REGION") ?? "0");
            static Vector3 Point(string s) { float[] v = s.Split(',').Select(float.Parse).ToArray(); return new Vector3(v[0], v[1], v[2]); }
            static string P(Vector3 p) => $"{(int)MathF.Round(p.X)},{(int)MathF.Round(p.Y)},{(int)MathF.Round(p.Z)}";
            string[] jobLines = File.ReadAllLines(jobs);
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string> { "version " + ToolVersion };
            var zones = zoneSpec.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(item =>
            {
                int[] v = item.Split(':').Select(int.Parse).ToArray();
                return new Zone(null, (ushort)v[0], $"zone{v[0]}", v[1], v[2], v[3], v[4], (ushort)v[0], false, 0, false, 0, 0, 0, 0, 0);
            }).ToList();
            Zone At(Vector3 p) => zones.FirstOrDefault(z => p.X >= z.XOffset && p.X < z.XOffset + z.Width && p.Y >= z.YOffset && p.Y < z.YOffset + z.Height);
            var nodes = new WrappedPathfindingNode[4096];
            int complete = 0, failed = 0;
            try
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.LoadNavMesh(zone);
                foreach (string job in jobLines)
                {
                    string[] f = job.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 3 || f[0].StartsWith('#')) continue;
                    if (f[0] == "snap")
                    {
                        Vector3 raw = Point(f[2]);
                        float radius = f.Length > 3 ? float.Parse(f[3]) : 256;
                        Zone zone = At(raw);
                        Vector3? floor = zone == null ? null : nav.GetClosestPoint(zone, raw, radius, radius, 1024, nav.DefaultFilters);
                        bool exit = floor.HasValue && AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value);
                        lines.Add($"snap {f[1]} floor={(floor.HasValue ? 1 : 0)} at={(floor.HasValue ? P(floor.Value) : "-")} exit={(exit ? 1 : 0)} " +
                                  $"area={(floor.HasValue ? AutonomousZoneRoads.AreaAt(zone, floor.Value) ?? -1 : -1)}");
                        continue;
                    }
                    if (f[0] != "trip") continue;
                    Vector3[] points = f[2].Split('>').Select(Point).ToArray();
                    Vector3 position = points[0];
                    var walked = new List<(Vector3 Point, int Flags)> { (position, 0) };
                    var legLengths = new List<float>();
                    var direct = new List<int>();
                    var crossings = new List<string>();
                    int jumps = 0, swim = 0, doors = 0;
                    bool ok = true;
                    string fail = "";
                    // One zone's walk, as the mover: Detour, the road route for a far-short partial path, and on from the end
                    // of a partial path (UT_EventRouteAudit's walk).
                    bool Walk(Zone zone, Vector3 to, out float length)
                    {
                        length = 0;
                        for (int segment = 0; segment < 24; segment++)
                        {
                            AutonomousNavigationSurface.TryFloor(nav, zone, position, out Vector3 from);
                            PathfindingResult path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
                            if (Pathfinder.TryRoadPath(nav, zone, from, to, nav.DefaultFilters, path, nodes, nodes.Length, out var road)) path = road;
                            if (path.NodeCount < 1 || path.NodeCount > nodes.Length) return false;
                            Vector3 last = from;
                            for (int i = 0; i < path.NodeCount; i++)
                            {
                                length += Vector3.Distance(last, nodes[i].Position);
                                last = nodes[i].Position;
                                walked.Add((last, (int)nodes[i].Flags));
                                if ((nodes[i].Flags & EDtPolyFlags.Jump) != 0) jumps++;
                                if ((nodes[i].Flags & EDtPolyFlags.Swim) != 0) swim++;
                                if ((nodes[i].Flags & EDtPolyFlags.AnyDoor) != 0) doors++;
                            }
                            bool progressed = Vector3.Distance(last, from) > 32;
                            position = last;
                            if (path.Status == PathfindingStatus.PathFound || Vector3.Distance(last, to) <= 64) return true;
                            if (path.Status != PathfindingStatus.PartialPathFound || !progressed) return false;
                        }
                        return false;
                    }
                    for (int leg = 1; leg < points.Length && ok; leg++)
                    {
                        Vector3 goal = points[leg];
                        Zone target = At(goal);
                        if (target == null || At(position) == null) { fail = $"leg {leg}: point outside the loaded zones"; ok = false; break; }
                        float legLength = 0;
                        int steps = 0;
                        // Zones already crossed on this leg are not entered again (RvrKeepRoute, RealmRaidMuster.TryRoute).
                        var visited = new HashSet<ushort>();
                        while (ok && At(position) != target && steps < 16)
                        {
                            Zone from = At(position);
                            visited.Add(from.ID);
                            if (!AutonomousZoneItinerary.ProbeNextStep(zones, region, from, target, position, goal, nav, out var step, new List<string>(),
                                    z => !visited.Contains(z.ID)))
                            { fail = $"leg {leg}: no zone step {from.ID}->{target.ID} from {P(position)}"; ok = false; break; }
                            if (!Walk(from, step.Inside, out float inside))
                            { fail = $"leg {leg}: no path in zone {from.ID} to crossing {P(step.Inside)}, stopped at {P(position)}"; ok = false; break; }
                            legLength += inside + Vector3.Distance(step.Inside, step.Outside);
                            crossings.Add($"{from.ID}>{At(step.Outside)?.ID}@{P(step.Outside)}");
                            position = step.Outside;
                            walked.Add((position, 0));
                            steps++;
                        }
                        if (!ok) break;
                        Vector3 legStart = position;
                        AutonomousNavigationSurface.TryFloor(nav, target, legStart, out Vector3 legFloor);
                        direct.Add(AutonomousZoneItinerary.HasDirectCorridor(nav, target, legFloor, goal) ? 1 : 0);
                        if (!Walk(target, goal, out float final))
                        { fail = $"leg {leg}: no path in zone {target.ID} to {P(goal)}, stopped at {P(position)} ({Vector3.Distance(position, goal):0} short)"; ok = false; break; }
                        legLength += final;
                        legLengths.Add(legLength);
                    }
                    if (ok) complete++; else failed++;
                    var text = new StringBuilder();
                    text.Append($"trip {f[1]} ok={(ok ? 1 : 0)} len={legLengths.Sum():0} legs={string.Join(",", legLengths.Select(l => $"{l:0}"))} jumps={jumps} swim={swim} doors={doors} ");
                    text.Append($"direct={string.Join(",", direct)} cross={string.Join(";", crossings)} fail={fail.Replace(' ', '_')} pts=");
                    text.Append(string.Join(" ", walked.Select(w => $"{P(w.Point)},{w.Flags}")));
                    lines.Add(text.ToString());
                }
            }
            finally
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine($"lane survey: complete={complete} failed={failed} jobs={jobLines.Length}");
        }
    }
}
