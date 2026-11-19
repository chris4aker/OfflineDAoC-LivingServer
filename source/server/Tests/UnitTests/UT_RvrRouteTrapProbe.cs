using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool: RvR muster posts and keep-route starts on the real meshes. Read only.
    /// Env: OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_PROOF_OUT, OFFLINE_DAOC_ZONES and OFFLINE_DAOC_REGION as UT_ZoneStepProbe;
    /// OFFLINE_DAOC_MUSTERS = "x,y,z;..." (muster centres: every slot's post as HandleSiegeMuster places it, raw and with
    /// AutonomousRvrMusterPost), OFFLINE_DAOC_ROUTES = "x,y,z>x,y,z;..." (the in-zone corridor and path).
    /// </summary>
    [TestFixture, Explicit("RvR route trap probe tool"), NonParallelizable]
    public class UT_RvrRouteTrapProbe
    {
        [Test]
        public void ProbeRvrRouteTraps()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            string zoneSpec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_ZONES");
            Assume.That(root != null && output != null && zoneSpec != null);
            ushort regionId = ushort.Parse(Environment.GetEnvironmentVariable("OFFLINE_DAOC_REGION") ?? "0");
            static Vector3 Point(string s) { float[] v = s.Split(',').Select(float.Parse).ToArray(); return new Vector3(v[0], v[1], v[2]); }
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string>();
            var zones = zoneSpec.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(item =>
            {
                int[] v = item.Split(':').Select(int.Parse).ToArray();
                return new Zone(null, (ushort)v[0], $"zone{v[0]}", v[1], v[2], v[3], v[4], (ushort)v[0], false, 0, false, 0, 0, 0, 0, 0);
            }).ToList();
            Zone At(Vector3 p) => zones.FirstOrDefault(z => p.X >= z.XOffset && p.X < z.XOffset + z.Width && p.Y >= z.YOffset && p.Y < z.YOffset + z.Height);
            string Path(Zone zone, Vector3 a, Vector3 b)
            {
                var nodes = ArrayPool<WrappedPathfindingNode>.Shared.Rent(512);
                try
                {
                    var result = nav.GetPathStraight(zone, a, b, nav.DefaultFilters, nodes);
                    Vector3 last = result.NodeCount > 0 ? nodes[result.NodeCount - 1].Position : a;
                    return $"{result.Status} nodes={result.NodeCount} last={(int)last.X},{(int)last.Y},{(int)last.Z} short={(int)Vector3.Distance(last, b)}";
                }
                finally { ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes); }
            }
            try
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.LoadNavMesh(zone);
                foreach (string item in (Environment.GetEnvironmentVariable("OFFLINE_DAOC_MUSTERS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    Vector3 home = Point(item);
                    Zone zone = At(home);
                    Vector3? homeFloor = nav.GetClosestPoint(zone, home, 160, 160, 800, nav.DefaultFilters);
                    lines.Add($"muster {item} floor={homeFloor}");
                    int bad = 0, fixedBad = 0;
                    for (int slot = 0; slot < 80; slot++)
                    {
                        Vector3 raw = AutonomousRvrMusterPost.RawPost(home, slot);
                        Vector3? floor = nav.GetClosestPoint(At(raw), raw, 160, 160, 800, nav.DefaultFilters);
                        Vector3 post = floor ?? home;
                        bool reach = AutonomousRvrMusterPost.Reachable(nav, At(post), home, post);
                        Vector3 chosen = AutonomousRvrMusterPost.Choose(nav, At, home, slot);
                        bool chosenReach = AutonomousRvrMusterPost.Reachable(nav, At(chosen), home, chosen);
                        if (!reach) bad++;
                        if (!chosenReach) fixedBad++;
                        lines.Add($"  slot={slot} post={(int)post.X},{(int)post.Y},{(int)post.Z} reach={reach} " +
                                  $"path={Path(At(post), homeFloor ?? home, post)} chosen={(int)chosen.X},{(int)chosen.Y},{(int)chosen.Z} chosenReach={chosenReach}");
                    }
                    lines.Add($"  unreachable raw={bad}/80 chosen={fixedBad}/80");
                }
                foreach (string item in (Environment.GetEnvironmentVariable("OFFLINE_DAOC_ROUTES") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    Vector3[] ends = item.Split('>').Select(Point).ToArray();
                    Zone zone = At(ends[0]);
                    bool corridor = At(ends[1]) == zone && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, ends[0], ends[1], true);
                    lines.Add($"route {item} sameZoneCorridor={corridor} path={Path(zone, ends[0], ends[1])}");
                }
            }
            finally
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine(lines.FirstOrDefault() ?? "nothing probed");
        }
    }
}
