using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool: proves the RvR check-spot catalog (GameServer/bots/autonomous/data/rvr-checkspots.json) on the real meshes.
    /// Read only. Validate (default): every standing point is floor with a local exit and a straight walkable line (a
    /// navmesh ray, not a client sight check) to its spot; every spot is walked from each listed entry, directly or
    /// through its "via" spots; every circuit's approach is walked, and every leg in both directions, between standing
    /// points, as UT_TripProbe walks (AutonomousZoneItinerary.ProbeNextStep between zones, Detour then the zone road
    /// route inside one). The report's SHA-256 is the catalog's proof_hash; a changed mesh or catalog changes it.
    /// Search (rebuild step): OFFLINE_DAOC_CHECKSPOT_SEARCH = a JSON file of spots with their own and other monsters
    /// ([x,y,z,clearance] lists) and zone rectangles; writes the nearest standing point clear of the spot's own monsters.
    /// Env: OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_PROOF_OUT, optional OFFLINE_DAOC_CHECKSPOTS (a catalog file instead of the
    /// embedded one), optional OFFLINE_DAOC_CHECKSPOT_STRICT=1 (fail on any failed check or a proof_hash mismatch).
    /// </summary>
    [TestFixture, Explicit("Check-spot catalog proof tool"), NonParallelizable]
    public class UT_RvrCheckSpotCatalog
    {
        private static Vector3 P(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
        private static string F(Vector3 p) => $"{(int)p.X},{(int)p.Y},{(int)p.Z}";

        [Test]
        public void ProveCatalog()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && output != null);
            string search = Environment.GetEnvironmentVariable("OFFLINE_DAOC_CHECKSPOT_SEARCH");
            string catalogFile = Environment.GetEnvironmentVariable("OFFLINE_DAOC_CHECKSPOTS");
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var lines = new List<string>();
            bool ok = true;
            try
            {
                if (search != null) Search(JsonDocument.Parse(File.ReadAllText(search)).RootElement, lines);
                else ok = Validate(JsonDocument.Parse(catalogFile != null ? File.ReadAllText(catalogFile) : Embedded()).RootElement, lines);
            }
            finally { Environment.CurrentDirectory = previous; }
            File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine(lines.LastOrDefault() ?? "nothing");
            if (Environment.GetEnvironmentVariable("OFFLINE_DAOC_CHECKSPOT_STRICT") == "1") Assert.That(ok, Is.True, lines.LastOrDefault());
        }

        private static string Embedded()
        {
            var assembly = typeof(AutonomousZoneItinerary).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("rvr-checkspots.json", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
            return reader.ReadToEnd();
        }

        private static List<Zone> Zones(JsonElement list) => list.EnumerateArray().Select(z => new Zone(null,
            (ushort)z.GetProperty("id").GetInt32(), $"zone{z.GetProperty("id").GetInt32()}", z.GetProperty("x").GetInt32(), z.GetProperty("y").GetInt32(),
            z.GetProperty("width").GetInt32(), z.GetProperty("height").GetInt32(), (ushort)z.GetProperty("id").GetInt32(), false, 0, false, 0, 0, 0, 0, 0)).ToList();

        private static Zone At(List<Zone> zones, Vector3 p) =>
            zones.FirstOrDefault(z => p.X >= z.XOffset && p.X < z.XOffset + z.Width && p.Y >= z.YOffset && p.Y < z.YOffset + z.Height);

        /// <summary>A walk through the given points as UT_TripProbe does it; the walked length, or -1 when a step fails.</summary>
        private static int Walk(IPathfindingMgr nav, List<Zone> zones, ushort region, IReadOnlyList<Vector3> points, WrappedPathfindingNode[] nodes)
        {
            Vector3 position = points[0];
            float total = 0;
            bool Leg(Zone zone, Vector3 to, out float length)
            {
                length = 0;
                AutonomousNavigationSurface.TryFloor(nav, zone, position, out Vector3 from);
                PathfindingResult path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
                if (Pathfinder.TryRoadPath(nav, zone, from, to, nav.DefaultFilters, path, nodes, nodes.Length, out var road)) path = road;
                if (path.NodeCount < 1) return false;
                Vector3 last = from;
                for (int i = 0; i < path.NodeCount; i++) { length += Vector3.Distance(last, nodes[i].Position); last = nodes[i].Position; }
                position = last;
                return path.Status == PathfindingStatus.PathFound || Vector3.Distance(last, to) <= 64;
            }
            for (int k = 1; k < points.Count; k++)
            {
                Zone target = At(zones, points[k]);
                for (int crossings = 0; At(zones, position) != target; crossings++)
                {
                    Zone from = At(zones, position);
                    if (crossings >= 16 || from == null || target == null ||
                        !AutonomousZoneItinerary.ProbeNextStep(zones, region, from, target, position, points[k], nav, out var step, new List<string>()) ||
                        !Leg(from, step.Inside, out float inside)) return -1;
                    total += inside + Vector3.Distance(step.Inside, step.Outside);
                    position = step.Outside;
                }
                if (!Leg(target, points[k], out float final)) return -1;
                total += final;
            }
            return (int)total;
        }

        private static bool Validate(JsonElement catalog, List<string> lines)
        {
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var nodes = new WrappedPathfindingNode[4096];
            int failures = 0;
            void Check(bool passed, string line) { lines.Add((passed ? "ok   " : "FAIL ") + line); if (!passed) failures++; }
            lines.Add($"rvr-checkspots version {catalog.GetProperty("version").GetInt32()}");
            foreach (JsonElement frontier in catalog.GetProperty("frontiers").EnumerateArray())
            {
                ushort region = (ushort)frontier.GetProperty("region").GetInt32();
                List<Zone> zones = Zones(frontier.GetProperty("zones"));
                foreach (Zone zone in zones) LocalPathfindingMgr.LoadNavMesh(zone);
                try
                {
                    var stands = new Dictionary<string, Vector3>();
                    var entries = frontier.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("realm").GetString(), P);
                    lines.Add($"region {region}: {frontier.GetProperty("spots").GetArrayLength()} spots, {frontier.GetProperty("circuits").GetArrayLength()} circuits");
                    foreach (JsonElement spot in frontier.GetProperty("spots").EnumerateArray())
                    {
                        string id = spot.GetProperty("id").GetString();
                        Vector3 centre = P(spot), stand = P(spot.GetProperty("stand"));
                        stands[id] = stand;
                        Zone zone = At(zones, stand);
                        Vector3? spotFloor = zone == null ? null : nav.GetClosestPoint(zone, centre, 64, 64, 384, nav.DefaultFilters);
                        Check(zone != null && AutonomousNavigationSurface.TryFloor(nav, zone, stand, out _) &&
                              AutonomousRendezvousNavigation.HasLocalExit(nav, zone, stand) && spotFloor.HasValue &&
                              nav.HasLineOfSight(zone, stand, spotFloor.Value, nav.DefaultFilters),
                            $"stand {id} {F(stand)} floor, exit, line to spot");
                    }
                    foreach (JsonElement spot in frontier.GetProperty("spots").EnumerateArray())
                    {
                        string id = spot.GetProperty("id").GetString();
                        foreach ((string realm, Vector3 entry) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                        {
                            var points = new List<Vector3> { entry };
                            if (spot.TryGetProperty("via", out JsonElement via) && via.TryGetProperty(realm, out JsonElement list))
                                points.AddRange(list.EnumerateArray().Select(v => stands[v.GetString()]));
                            points.Add(stands[id]);
                            int walked = Walk(nav, zones, region, points, nodes);
                            Check(walked >= 0, $"reach {realm} {id} via {points.Count - 2}: {walked}");
                        }
                    }
                    foreach (JsonElement circuit in frontier.GetProperty("circuits").EnumerateArray())
                    {
                        string id = circuit.GetProperty("id").GetString(), realm = circuit.GetProperty("realm").GetString();
                        string[] spots = circuit.GetProperty("spots").EnumerateArray().Select(s => s.GetString()).ToArray();
                        var approach = new List<Vector3> { entries[realm] };
                        approach.AddRange(circuit.GetProperty("approach").EnumerateArray().Select(s => stands[s.GetString()]));
                        approach.Add(stands[spots[0]]);
                        int walked = Walk(nav, zones, region, approach, nodes);
                        Check(walked >= 0, $"circuit {id} {realm} approach: {walked}");
                        int loop = 0;
                        for (int k = 0; k < spots.Length; k++)
                        {
                            string a = spots[k], b = spots[(k + 1) % spots.Length];
                            int there = Walk(nav, zones, region, [stands[a], stands[b]], nodes);
                            int back = Walk(nav, zones, region, [stands[b], stands[a]], nodes);
                            Check(there >= 0 && back >= 0, $"circuit {id} leg {a} > {b}: {there} back {back}");
                            loop += Math.Max(there, 0);
                        }
                        lines.Add($"     circuit {id} loop {loop}");
                    }
                }
                finally { foreach (Zone zone in zones) LocalPathfindingMgr.UnloadNavMesh(zone); }
            }
            string hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).ToLowerInvariant();
            string recorded = catalog.TryGetProperty("proof_hash", out JsonElement h) ? h.GetString() : "";
            lines.Add($"proof_hash {hash} recorded {recorded}: {(hash == recorded ? "match" : "MISMATCH")}; {failures} failed checks");
            return failures == 0 && hash == recorded;
        }

        private static void Search(JsonElement request, List<string> lines)
        {
            var nav = PathfindingProvider.LocalPathfindingMgr;
            foreach (JsonElement spot in request.GetProperty("spots").EnumerateArray())
            {
                JsonElement zr = spot.GetProperty("zone");
                var zone = new Zone(null, (ushort)zr[0].GetInt32(), "zone", zr[1].GetInt32(), zr[2].GetInt32(), zr[3].GetInt32(), zr[4].GetInt32(),
                    (ushort)zr[0].GetInt32(), false, 0, false, 0, 0, 0, 0, 0);
                if (!nav.HasNavmesh(zone)) LocalPathfindingMgr.LoadNavMesh(zone);
                Vector3 centre = P(spot);
                float radius = spot.GetProperty("radius").GetSingle();
                static (Vector2 At, float Clear)[] Monsters(JsonElement list) =>
                    list.EnumerateArray().Select(m => (new Vector2(m[0].GetSingle(), m[1].GetSingle()), m[3].GetSingle())).ToArray();
                var own = Monsters(spot.GetProperty("own"));
                var other = Monsters(spot.GetProperty("other"));
                Vector3? spotFloor = nav.GetClosestPoint(zone, centre, 64, 64, 384, nav.DefaultFilters);
                (Vector3 Point, float Score, int Others, float Ring)? best = null;
                for (float ring = 0; ring <= radius + 1600 && spotFloor.HasValue; ring += 200)
                    for (int k = 0; k < (ring == 0 ? 1 : 24); k++)
                    {
                        double angle = k * Math.PI / 12 + ring / 997.0;
                        var desired = new Vector3(centre.X + (float)(Math.Cos(angle) * ring), centre.Y + (float)(Math.Sin(angle) * ring), centre.Z);
                        Vector3? floor = nav.GetClosestPoint(zone, desired, 48, 48, 512, nav.DefaultFilters);
                        if (!floor.HasValue || Vector2.Distance(new(floor.Value.X, floor.Value.Y), new(desired.X, desired.Y)) > 120) continue;
                        Vector2 at = new(floor.Value.X, floor.Value.Y);
                        if (own.Any(m => Vector2.Distance(at, m.At) < m.Clear) ||
                            AutonomousZonePockets.Contains(zone.ID, floor.Value.X, floor.Value.Y, floor.Value.Z) ||
                            !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ||
                            !nav.HasLineOfSight(zone, floor.Value, spotFloor.Value, nav.DefaultFilters)) continue;
                        int others = other.Count(m => Vector2.Distance(at, m.At) < m.Clear);
                        float score = others * 400 + ring;
                        if (best == null || score < best.Value.Score) best = (floor.Value, score, others, ring);
                    }
                lines.Add(best == null
                    ? $"stand {spot.GetProperty("id").GetString()} NONE"
                    : $"stand {spot.GetProperty("id").GetString()} {F(best.Value.Point)} ring={best.Value.Ring} others={best.Value.Others}");
            }
            lines.Add("search done");
        }
    }
}
