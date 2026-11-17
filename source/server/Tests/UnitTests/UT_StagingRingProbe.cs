using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool: the staging ring of a dragon or open-world raid (RealmRaidStaging.TryDragonPost) on the real meshes, with the
    /// check each candidate post failed. Read only.
    /// Env: OFFLINE_DAOC_NAV_ROOT (folder with navmesh/ and lib/), OFFLINE_DAOC_PROOF_OUT,
    /// OFFLINE_DAOC_ZONES = "id:offsetX:offsetY:width:height;..." (world units),
    /// OFFLINE_DAOC_HOMES = "name@x,y,z;..." (the boss's spawn), OFFLINE_DAOC_SLOTS = posts to place (default 8).
    /// </summary>
    [TestFixture, Explicit("Staging ring probe tool"), NonParallelizable]
    public class UT_StagingRingProbe
    {
        [Test]
        public void ProbeStagingRings()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            string zoneSpec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_ZONES");
            string homeSpec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_HOMES");
            Assume.That(root != null && output != null && zoneSpec != null && homeSpec != null);
            int slots = int.Parse(Environment.GetEnvironmentVariable("OFFLINE_DAOC_SLOTS") ?? "8");
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string>();
            var zones = zoneSpec.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(item =>
            {
                int[] v = item.Split(':').Select(int.Parse).ToArray();
                return new Zone(null, (ushort)v[0], $"zone{v[0]}", v[1], v[2], v[3], v[4], (ushort)v[0], false, 0, false, 0, 0, 0, 0, 0);
            }).ToList();
            try
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.LoadNavMesh(zone);
                Zone At(Vector3 p) => zones.FirstOrDefault(z => p.X >= z.XOffset && p.X < z.XOffset + z.Width && p.Y >= z.YOffset && p.Y < z.YOffset + z.Height);
                foreach (string item in homeSpec.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = item.Split('@')[0];
                    float[] v = item.Split('@')[1].Split(',').Select(float.Parse).ToArray();
                    var home = new Vector3(v[0], v[1], v[2]);
                    Zone zone = At(home);
                    var homeFloor = zone == null ? null : nav.GetClosestPoint(zone, home, 96, 96, 4096, nav.DefaultFilters);
                    lines.Add($"{name} home={home} zone={zone?.ID} navmesh={zone != null && nav.HasNavmesh(zone)} homeFloor={homeFloor} " +
                              $"area={(zone == null ? null : AutonomousZoneRoads.AreaAt(zone, home))} foot={(zone == null ? null : RealmRaidStaging.FootOf(nav, zone, home))}");
                    var occupied = new List<Vector3>();
                    for (int slot = 0; slot < slots; slot++)
                    {
                        bool ok = RealmRaidStaging.TryDragonPost(nav, zone, home, slot, occupied, out Vector3 post);
                        if (ok) occupied.Add(post);
                        lines.Add($"  slot {slot}: {(ok ? $"OK {post} ({Vector3.Distance(post, home):0} out)" : "FAILED")}");
                        if (ok || zone == null) continue;
                        foreach (string why in Diagnose(nav, zone, home, slot, occupied)) lines.Add("    " + why);
                    }
                }
            }
            finally
            {
                foreach (Zone zone in zones) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine(string.Join(Environment.NewLine, lines.Where(l => !l.StartsWith("    "))));
        }

        // The checks of RealmRaidStaging.TryDragonPost, one candidate at a time: the first one each candidate fails.
        private static IEnumerable<string> Diagnose(IPathfindingMgr nav, Zone zone, Vector3 home, int slot, IReadOnlyCollection<Vector3> occupied)
        {
            int? lairArea = AutonomousZoneRoads.AreaAt(zone, home);
            for (int attempt = 0; attempt < 36; attempt++)
            {
                double angle = (attempt * 47.5 + slot * 137.5) * Math.PI / 180;
                float radius = 3600 + attempt % 6 * 500;
                var raw = home + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
                var floor = nav.GetClosestPoint(zone, raw, 96, 96, 4096, nav.DefaultFilters);
                string why =
                    !floor.HasValue ? "no floor" :
                    Math.Abs(floor.Value.Z - home.Z) > 768 ? $"height {floor.Value.Z - home.Z:0}" :
                    lairArea.HasValue && AutonomousZoneRoads.AreaAt(zone, floor.Value) is int area && area != lairArea.Value ? $"area {area} not {lairArea}" :
                    occupied.Any(p => Vector3.DistanceSquared(p, floor.Value) < 600 * 600) ? "occupied" :
                    !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ? "no local exit" :
                    !AutonomousZonePointApproach.TryResolve(nav, zone, floor.Value, home, 300, out _) ? "no walk to the home" :
                    !RealmRaidStaging.HasOpenPartySpace(nav, zone, floor.Value) ? "no party space" : "ok";
                yield return $"attempt {attempt} r={radius} raw={(int)raw.X},{(int)raw.Y} floor={(floor.HasValue ? $"{(int)floor.Value.X},{(int)floor.Value.Y},{(int)floor.Value.Z}" : "-")}: {why}";
            }
        }
    }
}
