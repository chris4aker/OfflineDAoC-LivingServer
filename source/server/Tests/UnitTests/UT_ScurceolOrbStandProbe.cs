using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool: on the real Krondon navmesh (zone 61), every floor spot around Scurceol Hyrde's four Lyft Miht orbs with its
    /// distance to the orb, whether the raid can walk to it from Scurceol's floor, and what the mesh ray says about sight.
    /// Read only. Env: OFFLINE_DAOC_NAV_ROOT (folder with navmesh/ and lib/libDetour.so or lib/Detour.dll),
    /// OFFLINE_DAOC_DB (world db, read only), OFFLINE_DAOC_PROOF_OUT (tsv written).
    /// </summary>
    [TestFixture, Explicit("Krondon orb stand probe tool"), NonParallelizable]
    public class UT_ScurceolOrbStandProbe
    {
        internal static readonly Vector3 Scurceol = new(51941, 20068, 17174);
        internal static readonly Vector3[] Orbs = [new(51236, 20849, 17669), new(51316, 19228, 17408), new(52702, 19214, 17751), new(52713, 20841, 17511)];

        internal static string NativeDetour(string root) =>
            new[] { Path.Combine(root, "lib", "libDetour.so"), Path.Combine(root, "lib", "Detour.dll") }.FirstOrDefault(File.Exists);

        /// <summary>Every mesh surface at (x, y) from 18,500 down to 15,500, highest first.</summary>
        internal static List<float> Layers(IPathfindingMgr nav, Zone zone, float x, float y)
        {
            var found = new List<float>();
            float top = 18_500;
            while (top > 15_500)
            {
                Vector3? p = nav.GetClosestPointInBounds(zone, new Vector3(x, y, top), new Vector3(-4, -4, 15_500 - top), new Vector3(4, 4, 0), nav.DefaultFilters);
                if (p is not Vector3 v) break;
                found.Add(v.Z);
                top = v.Z - 24;
            }
            return found;
        }

        /// <summary>The 1.1.2 search, kept here to show on the real mesh why it found nothing: four floor spots, and sight as the
        /// floor ray to the floor under the orb.</summary>
        internal static bool LegacyTryRangedStand(IPathfindingMgr nav, Zone zone, Vector3 add, Vector3 boss, Func<Vector3, Vector3?> reach,
            out Vector3 stand, out string why)
        {
            stand = default;
            Vector3? under = nav.GetClosestPoint(zone, add, 128, 128, RealmRaidDungeonRoute.RangedFlyerReach, nav.DefaultFilters);
            var floors = new List<Vector3>();
            void Add(Vector3? p) { if (p is Vector3 v && !floors.Any(f => Vector3.DistanceSquared(f, v) < 48 * 48)) floors.Add(v); }
            Add(under);
            Add(nav.GetClosestPoint(zone, boss, 64, 64, 256, nav.DefaultFilters));
            var level = new Vector3(add.X, add.Y, boss.Z);
            foreach (float t in new[] { 0.67f, 0.33f })
                Add(nav.GetClosestPoint(zone, Vector3.Lerp(boss, level, t), 96, 96, 256, nav.DefaultFilters));
            int reached = 0, inRange = 0;
            foreach (Vector3 floor in floors)
            {
                if (reach(floor) is not Vector3 approach) continue;
                reached++;
                if (Vector3.Distance(approach, add) > RealmRaidDungeonRoute.RequiredAddRangedReach) continue;
                inRange++;
                Vector3 sight = under ?? new Vector3(add.X, add.Y, approach.Z);
                if (Vector3.DistanceSquared(approach, sight) > 1 && !nav.HasLineOfSight(zone, approach, sight, nav.DefaultFilters)) continue;
                stand = approach; why = "found"; return true;
            }
            why = $"floors={floors.Count} reached={reached} inRange={inRange}: " + (reached == 0 ? "none reachable" : inRange == 0 ? "none within 1400" : "no floor ray to the floor under it");
            return false;
        }

        /// <summary>
        /// On the installed Krondon mesh with the real Detour library and the route's own walk check (TryResolve from Scurceol's
        /// floor): every orb gets a stand in range, well off Scurceol, with a clear line; the 1.1.2 search gets none.
        /// </summary>
        [Test]
        public void InstalledMeshGivesEveryOrbAStand()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string dbPath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB");
            Assume.That(root != null && dbPath != null);
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            try
            {
                string native = NativeDetour(root);
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, asm, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                Environment.CurrentDirectory = root;
                using var db = new SQLiteConnection($"Data Source={dbPath};Read Only=True;Pooling=False;");
                db.Open();
                Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 61, loaded);
                IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                Zone zone = region.GetZone((int)Scurceol.X, (int)Scurceol.Y);
                Vector3 anchor = nav.GetClosestPoint(zone, Scurceol, 64, 64, 256, nav.DefaultFilters) ?? Scurceol;
                Vector3? Reach(Vector3 floor, Func<Vector3, bool> accept) =>
                    AutonomousZonePointApproach.TryResolve(nav, zone, anchor, floor, 220, out Vector3 a, accept) ? a : null;
                foreach (Vector3 orb in Orbs)
                {
                    Assert.That(LegacyTryRangedStand(nav, zone, orb, Scurceol, f => Reach(f, null), out _, out string legacy), Is.False, legacy);
                    Assert.That(RealmRaidDungeonRoute.TryRangedStand(nav, zone, orb, Scurceol, Reach, out Vector3 stand, out string why), Is.True, why);
                    TestContext.Progress.WriteLine($"orb {orb}: stand {stand}: {why}");
                    Assert.That(Vector3.Distance(stand, orb), Is.LessThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddRangedReach));
                    Assert.That(Vector3.Distance(stand, Scurceol), Is.GreaterThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddStandBossClearance));
                    Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(nav, zone, stand, orb));
                    Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchor, stand), "the raid walks there from Scurceol's floor");
                }
            }
            finally
            {
                foreach (Zone z in loaded) LocalPathfindingMgr.UnloadNavMesh(z);
                Environment.CurrentDirectory = previous;
            }
        }

        /// <summary>The recorded fixture's area and cell (UnitTests/Data/krondon_scurceol_mesh.txt.gz): every floor within 1,400 of an orb.</summary>
        internal const int FixtureMinX = 49_800, FixtureMaxX = 54_150, FixtureMinY = 17_800, FixtureMaxY = 22_300, FixtureCell = 25;

        /// <summary>
        /// Records the fixture (gzip the file written): on a 25 grid over the orbs' room, every mesh surface (highest first) and, for those within 300 of
        /// Scurceol's floor height, whether the raid walks there from his floor (the real corridor check). A "w" after a height
        /// marks a walkable one. Env as above; OFFLINE_DAOC_PROOF_OUT is the fixture file written.
        /// </summary>
        [Test]
        public void RecordFixture()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string dbPath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && dbPath != null && output != null);
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            var lines = new List<string> { "# Krondon (zone 61) navmesh around Scurceol Hyrde's orbs, recorded by UT_ScurceolOrbStandProbe.RecordFixture",
                "# from the 0.37b world's zone061.nav on a 25 grid. Line: X, Y, then every walkable surface there (highest first);",
                "# \"w\": the raid walks there from Scurceol's floor (real corridor check; only surfaces within 300 of his floor were checked)." };
            try
            {
                string native = NativeDetour(root);
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, asm, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                Environment.CurrentDirectory = root;
                using var db = new SQLiteConnection($"Data Source={dbPath};Read Only=True;Pooling=False;");
                db.Open();
                Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 61, loaded);
                IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                Zone zone = region.GetZone((int)Scurceol.X, (int)Scurceol.Y);
                Vector3 anchor = nav.GetClosestPoint(zone, Scurceol, 64, 64, 256, nav.DefaultFilters) ?? Scurceol;
                for (int x = FixtureMinX; x <= FixtureMaxX; x += FixtureCell)
                for (int y = FixtureMinY; y <= FixtureMaxY; y += FixtureCell)
                {
                    List<float> layers = Layers(nav, zone, x, y);
                    if (layers.Count == 0) continue;
                    lines.Add($"{x} {y} " + string.Join(" ", layers.Select(z =>
                        $"{(int)MathF.Round(z)}{(MathF.Abs(z - anchor.Z) <= 300 && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchor, new Vector3(x, y, z)) ? "w" : "")}")));
                }
            }
            finally
            {
                foreach (Zone z in loaded) LocalPathfindingMgr.UnloadNavMesh(z);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
        }

        [Test]
        public void ProbeOrbStands()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string dbPath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && dbPath != null && output != null);
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            var lines = new List<string>();
            try
            {
                string native = NativeDetour(root);
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, asm, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                Environment.CurrentDirectory = root;
                using var db = new SQLiteConnection($"Data Source={dbPath};Read Only=True;Pooling=False;");
                db.Open();
                Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 61, loaded);
                IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                Zone zone = region.GetZone((int)Scurceol.X, (int)Scurceol.Y);
                Vector3? bossFloor = nav.GetClosestPoint(zone, Scurceol, 64, 64, 256, nav.DefaultFilters);
                lines.Add($"BOSS\t{Scurceol}\tfloor\t{bossFloor}");
                Vector3 anchor = bossFloor ?? Scurceol;
                Vector3? Reach(Vector3 floor, Func<Vector3, bool> accept) =>
                    AutonomousZonePointApproach.TryResolve(nav, zone, anchor, floor, 220, out Vector3 a, accept) ? a : null;
                for (int i = 0; i < Orbs.Length; i++)
                {
                    Vector3 orb = Orbs[i];
                    Vector3? under = nav.GetClosestPoint(zone, orb, 128, 128, RealmRaidDungeonRoute.RangedFlyerReach, nav.DefaultFilters);
                    Vector3? beneath = nav.GetFloorBeneath(zone, orb, 2000, nav.DefaultFilters);
                    bool legacy = LegacyTryRangedStand(nav, zone, orb, Scurceol, f => Reach(f, null), out _, out string legacyWhy);
                    lines.Add($"ORB{i + 1}\t1.1.2\t{legacy}\t{legacyWhy}");
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    bool found = RealmRaidDungeonRoute.TryRangedStand(nav, zone, orb, Scurceol, Reach, out Vector3 stand, out string why);
                    lines.Add($"ORB{i + 1}\t{orb}\tunder\t{under}\tbeneath\t{beneath}\tTryRangedStand\t{found}\t{stand}\t{why}\tms={watch.ElapsedMilliseconds}");
                    {
                        // The ring candidates in the search's order: the first sighted ones, with their walk from Scurceol's floor.
                        var ring = new List<Vector3>();
                        for (int radius = 200; radius <= 1400; radius += 50)
                        for (int degrees = 0; degrees < 360; degrees += 10)
                        {
                            float a = degrees * MathF.PI / 180f;
                            if (nav.GetClosestPoint(zone, new Vector3(orb.X + MathF.Cos(a) * radius, orb.Y + MathF.Sin(a) * radius, anchor.Z), 48, 48, 320, nav.DefaultFilters) is Vector3 f &&
                                !ring.Any(r => Vector3.DistanceSquared(r, f) < 48 * 48)) ring.Add(f);
                        }
                        int shown = 0;
                        foreach (Vector3 f in ring.Where(f => Vector3.Distance(f, orb) <= 1400 && Vector3.Distance(f, Scurceol) >= 400 && MathF.Abs(f.Z - anchor.Z) <= RealmRaidDungeonRoute.RequiredAddStandFloorBand)
                                     .OrderBy(f => Vector3.Distance(f, Scurceol) < 700 ? 1 : 0).ThenBy(f => Vector3.DistanceSquared(f, orb)))
                        {
                            if (!RealmRaidDungeonRoute.HasSightToAirborne(nav, zone, f, orb)) continue;
                            lines.Add($"ORB{i + 1}\tRING\t{(int)f.X},{(int)f.Y},{(int)f.Z}\td3={(int)Vector3.Distance(f, orb)}\tboss={(int)Vector3.Distance(f, Scurceol)}\t" +
                                      $"walk={AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchor, f)}");
                            if (++shown >= 30) break;
                        }
                        lines.Add($"ORB{i + 1}\tRINGCOUNT\t{ring.Count}");
                    }
                    foreach (int aimUp in new[] { 0, 10, 20, 30, 40, 60 })
                    {
                        int clear = 0, all = 0;
                        for (int dx = -1400; dx <= 1400; dx += 50)
                        for (int dy = -1400; dy <= 1400; dy += 50)
                        {
                            if (nav.GetClosestPoint(zone, new Vector3(orb.X + dx, orb.Y + dy, anchor.Z), 24, 24, RealmRaidDungeonRoute.RequiredAddStandFloorBand, nav.DefaultFilters) is not Vector3 f ||
                                Vector3.Distance(f, orb) > RealmRaidDungeonRoute.RequiredAddRangedReach || Vector3.Distance(f, Scurceol) < 700) continue;
                            all++;
                            if (RealmRaidDungeonRoute.HasSightToAirborne(nav, zone, f, orb + new Vector3(0, 0, aimUp - RealmRaidDungeonRoute.SightAimHeight))) clear++;
                        }
                        lines.Add($"ORB{i + 1}\tAIM\t+{aimUp}\tclear={clear}\tof={all}");
                    }
                    if (under is Vector3 u)
                        lines.Add($"ORB{i + 1}\tunder-walk-from-boss\t{AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchor, u)}\t" +
                                  $"under-ray-from-boss\t{nav.HasLineOfSight(zone, anchor, u, nav.DefaultFilters)}");
                    // Mesh layers (every walkable surface, top down) along the line from Scurceol's floor to the orb.
                    for (int step = 0; step <= 40; step++)
                    {
                        Vector3 at = Vector3.Lerp(anchor, orb, step / 40f);
                        lines.Add($"ORB{i + 1}\tPROFILE\t{step}\t{(int)at.X}\t{(int)at.Y}\tline={(int)at.Z}\t" +
                                  string.Join(",", Layers(nav, zone, at.X, at.Y).Select(z => (int)z)));
                    }
                    // Every floor within 1,500 across of the orb, on a 100 grid, at any height from 1,500 below it to 200 above.
                    for (int dx = -1500; dx <= 1500; dx += 100)
                    for (int dy = -1500; dy <= 1500; dy += 100)
                    {
                        if (dx * dx + dy * dy > 1500 * 1500) continue;
                        var seen = new List<Vector3>();
                        foreach (float z in new[] { orb.Z + 100, orb.Z - 300, orb.Z - 700, orb.Z - 1100, orb.Z - 1500 })
                        {
                            Vector3? f = nav.GetClosestPoint(zone, new Vector3(orb.X + dx, orb.Y + dy, z), 40, 40, 200, nav.DefaultFilters);
                            if (f is not Vector3 floor || seen.Any(s => Vector3.DistanceSquared(s, floor) < 32 * 32)) continue;
                            seen.Add(floor);
                            float d3 = Vector3.Distance(floor, orb);
                            bool walk = AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchor, floor);
                            bool rayUnder = under is Vector3 uu && nav.HasLineOfSight(zone, floor, uu, nav.DefaultFilters);
                            var level = new Vector3(orb.X, orb.Y, floor.Z);
                            bool rayLevel = nav.HasLineOfSight(zone, floor, level, nav.DefaultFilters);
                            string sight = "";
                            if (walk && d3 <= 1500)
                            {
                                // The 3-D line from the stand's eye (60 up) to the orb's middle (20 up): the worst surface above it,
                                // away from the orb (more than 300 across from it) and near it (its own perch).
                                Vector3 eye = floor + new Vector3(0, 0, 60), aim = orb + new Vector3(0, 0, 20);
                                float far = float.NegativeInfinity, near = float.NegativeInfinity;
                                int n = (int)(Vector2.Distance(new(eye.X, eye.Y), new(aim.X, aim.Y)) / 25);
                                for (int k = 1; k < n; k++)
                                {
                                    Vector3 at = Vector3.Lerp(eye, aim, k / (float)n);
                                    float over = Layers(nav, zone, at.X, at.Y).Where(z => z > at.Z && z <= at.Z + 400).Select(z => z - at.Z).DefaultIfEmpty(float.NegativeInfinity).Max();
                                    if (Vector2.Distance(new(at.X, at.Y), new(orb.X, orb.Y)) > 300) far = Math.Max(far, over); else near = Math.Max(near, over);
                                }
                                sight = $"\tfarOver={(float.IsNegativeInfinity(far) ? "clear" : ((int)far).ToString())}\tperchOver={(float.IsNegativeInfinity(near) ? "clear" : ((int)near).ToString())}";
                            }
                            if (walk && d3 <= RealmRaidDungeonRoute.RequiredAddRangedReach)
                                sight += $"\tairSight={RealmRaidDungeonRoute.HasSightToAirborne(nav, zone, floor, orb)}";
                            lines.Add($"ORB{i + 1}\tGRID\t{(int)floor.X}\t{(int)floor.Y}\t{(int)floor.Z}\td3={(int)d3}\tdz={(int)(orb.Z - floor.Z)}\t" +
                                      $"walk={walk}\trayUnder={rayUnder}\trayLevel={rayLevel}{sight}");
                        }
                    }
                }
            }
            finally
            {
                foreach (Zone z in loaded) LocalPathfindingMgr.UnloadNavMesh(z);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Progress.WriteLine(string.Join("\n", lines.Where(l => !l.Contains("\tGRID\t"))));
        }
    }
}
