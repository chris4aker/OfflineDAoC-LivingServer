using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Probe (sweep 7c, C-1): Curengkur's staging posts (RealmRaidStaging.TryDragonPost, ring inner 5,600 as on the owner's PC at
    /// 04:32:18, build 9a173f4) against the way the raid enters zone 187 from the hub's side: the corridor inside zone 187
    /// from the zone 185/187 seam (navmesh/seams.json) to each post, as RealmRaidMuster.TryRouteThrough checks it
    /// (HasCompleteCorridor, no zone roads), and with the zone roads for comparison. Read only; no navmesh is rebuilt.
    /// Env: OFFLINE_DAOC_NAV_ROOT (folder with navmesh/ and lib/), OFFLINE_DAOC_PROOF_OUT (optional output file).
    /// </summary>
    [TestFixture, Explicit("Reads installed navigation meshes only"), NonParallelizable]
    public class UT_CurengkurEntryProbe
    {
        private static readonly Vector3 Home = new(376086, 172803, 3452);
        private const ushort ZoneId = 187;
        private const int ZoneX = 42 * 8192, ZoneY = 20 * 8192;

        [Test]
        public void ProbeHubSideEntryToStagingPosts()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assume.That(root != null && File.Exists(Path.Combine(root, "navmesh", $"zone{ZoneId:D3}.nav")), "zone187.nav");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var zone = new Zone(null, ZoneId, "zone187", ZoneX, ZoneY, 65536, 65536, ZoneId, false, 0, false, 0, 0, 0, 0, 0);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string>();
            try
            {
                LocalPathfindingMgr.LoadNavMesh(zone);
                Assume.That(nav.HasNavmesh(zone));
                // The 185 -> 187 border crossings in the seam file (x, then the floor heights on each side), every 4 km.
                List<Vector3> entries = SeamEntries(Path.Combine("navmesh", "seams.json"))
                    .Select(p => nav.GetClosestPoint(zone, p, 128, 128, 1024, nav.DefaultFilters))
                    .Where(p => p.HasValue).Select(p => p.Value).ToList();
                lines.Add($"entries={entries.Count} [{string.Join(" ", entries.Select(e => $"{(int)e.X},{(int)e.Y},{(int)e.Z}"))}]");
                var occupied = new List<Vector3>();
                int withEntry = 0, withRoadEntry = 0, posts = 0;
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!RealmRaidStaging.TryDragonPost(nav, zone, Home, slot, occupied, out Vector3 post, 5600f))
                    {
                        lines.Add($"slot {slot}: no post");
                        continue;
                    }
                    posts++;
                    occupied.Add(post);
                    int direct = entries.Count(e => AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, e, post));
                    int roads = entries.Count(e => AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, e, post, true));
                    if (direct > 0) withEntry++;
                    if (roads > 0) withRoadEntry++;
                    lines.Add($"slot {slot}: post={(int)post.X},{(int)post.Y},{(int)post.Z} fromSpawn={(int)Vector3.Distance(post, Home)} " +
                              $"entriesWithCorridor={direct}/{entries.Count} withRoads={roads}/{entries.Count} " +
                              $"direct=[{string.Join(" ", entries.Where(e => AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, e, post)).Select(e => (int)e.X))}]");
                }
                lines.Add($"posts={posts} withEntryCorridor={withEntry} withRoadCorridor={withRoadEntry}");
            }
            finally
            {
                LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            if (output != null) File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine(string.Join(Environment.NewLine, lines));
        }

        private static IEnumerable<Vector3> SeamEntries(string path)
        {
            if (!File.Exists(path)) yield break;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.GetProperty("regions").TryGetProperty("181", out JsonElement seams)) yield break;
            foreach (JsonElement seam in seams.EnumerateArray())
            {
                if (seam.GetProperty("from").GetInt32() != 185 || seam.GetProperty("to").GetInt32() != ZoneId) continue;
                int border = seam.GetProperty("border").GetInt32();
                int i = 0;
                foreach (JsonElement point in seam.GetProperty("points").EnumerateArray())
                {
                    if (i++ % 64 != 0) continue;
                    // Just inside zone 187 (south of the border), at the 187 side's floor height.
                    yield return new Vector3(point[0].GetInt32(), border - 96, point[2].GetInt32());
                }
            }
        }
    }
}
