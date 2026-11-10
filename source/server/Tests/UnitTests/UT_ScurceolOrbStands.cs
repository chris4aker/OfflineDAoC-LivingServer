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
    /// A navmesh as layers of walkable surfaces on a square grid: the recorded Krondon fixture, or geometry built in a test.
    /// It answers the queries the ranged-stand search makes the way Detour does: nearest surface in a box, and the floor ray
    /// (a walk along connected floor that stops where the floor ends or steps more than <see cref="Climb"/>).
    /// </summary>
    internal sealed class LayeredMesh : PathfindingMgrBase
    {
        public const float Climb = 40;
        public readonly int Cell;
        private readonly Dictionary<(int, int), List<(float Z, bool Walk)>> _cells = new();
        public LayeredMesh(int cell) { Cell = cell; }
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;

        private (int, int) Key(float x, float y) => ((int)MathF.Round(x / Cell), (int)MathF.Round(y / Cell));

        public void Add(float x, float y, float z, bool walk)
        {
            var key = Key(x, y);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new();
            list.Add((z, walk));
        }

        /// <summary>A flat surface over every cell whose centre is within <paramref name="outer"/> of (x, y) and not within <paramref name="inner"/>.</summary>
        public LayeredMesh Disk(float x, float y, float outer, float z, bool walk, float inner = -1)
        {
            for (int kx = (int)MathF.Floor((x - outer) / Cell); kx <= (int)MathF.Ceiling((x + outer) / Cell); kx++)
            for (int ky = (int)MathF.Floor((y - outer) / Cell); ky <= (int)MathF.Ceiling((y + outer) / Cell); ky++)
            {
                float d = Vector2.Distance(new(kx * Cell, ky * Cell), new(x, y));
                if (d <= outer && d > inner) Add(kx * Cell, ky * Cell, z, walk);
            }
            return this;
        }

        public bool IsWalkable(Vector3 p) =>
            _cells.TryGetValue(Key(p.X, p.Y), out var list) && list.Any(l => l.Walk && MathF.Abs(l.Z - p.Z) <= 1);

        public IEnumerable<Vector3> Walkable() =>
            _cells.SelectMany(c => c.Value.Where(l => l.Walk).Select(l => new Vector3(c.Key.Item1 * Cell, c.Key.Item2 * Cell, l.Z)));

        private Vector3? Nearest(Vector3 reference, Vector3 centre, float xr, float yr, float zLow, float zHigh)
        {
            Vector3? best = null;
            float bestD = float.MaxValue;
            var own = Key(centre.X, centre.Y);
            for (int kx = (int)MathF.Round((centre.X - xr) / Cell); kx <= (int)MathF.Round((centre.X + xr) / Cell); kx++)
            for (int ky = (int)MathF.Round((centre.Y - yr) / Cell); ky <= (int)MathF.Round((centre.Y + yr) / Cell); ky++)
            {
                if (!_cells.TryGetValue((kx, ky), out var list)) continue;
                foreach (var (z, _) in list)
                {
                    if (z < zLow || z > zHigh) continue;
                    // Over the surface itself Detour returns the point straight below or above; beside it, its edge (here the cell centre).
                    var p = (kx, ky) == own ? new Vector3(centre.X, centre.Y, z) : new Vector3(kx * Cell, ky * Cell, z);
                    float d = Vector3.DistanceSquared(p, reference);
                    if (d < bestD) { bestD = d; best = p; }
                }
            }
            return best;
        }

        public override Vector3? GetClosestPoint(Zone zone, Vector3 p, float x, float y, float z, EDtPolyFlags[] filters) =>
            Nearest(p, p, x, y, p.Z - z, p.Z + z);

        public override Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 min, Vector3 max, EDtPolyFlags[] filters) =>
            Nearest(origin, origin + (min + max) / 2, (max.X - min.X) / 2, (max.Y - min.Y) / 2, origin.Z + min.Z, origin.Z + max.Z);

        public override bool HasLineOfSight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters)
        {
            float? Step(float x, float y, float z) =>
                _cells.TryGetValue(Key(x, y), out var list) && list.Where(l => MathF.Abs(l.Z - z) <= Climb).ToArray() is { Length: > 0 } near
                    ? near.OrderBy(l => MathF.Abs(l.Z - z)).First().Z : null;
            if (Step(from.X, from.Y, from.Z) is not float current) return false;
            float across = Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y));
            int steps = Math.Max(1, (int)MathF.Ceiling(across / 10));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, i / (float)steps);
                if (Step(at.X, at.Y, current) is not float next) return false;
                current = next;
            }
            return true;
        }

        /// <summary>The recorded real mesh (UnitTests/Data/krondon_scurceol_mesh.txt.gz, from UT_ScurceolOrbStandProbe.RecordFixture).</summary>
        public static LayeredMesh Krondon()
        {
            var assembly = typeof(LayeredMesh).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("krondon_scurceol_mesh.txt.gz", StringComparison.Ordinal));
            using var reader = new StreamReader(new System.IO.Compression.GZipStream(assembly.GetManifestResourceStream(name),
                System.IO.Compression.CompressionMode.Decompress));
            var mesh = new LayeredMesh(UT_ScurceolOrbStandProbe.FixtureCell);
            while (reader.ReadLine() is string line)
            {
                if (line.StartsWith('#') || line.Length == 0) continue;
                string[] parts = line.Split(' ');
                int x = int.Parse(parts[0]), y = int.Parse(parts[1]);
                foreach (string layer in parts.Skip(2))
                    mesh.Add(x, y, float.Parse(layer.TrimEnd('w')), layer.EndsWith('w'));
            }
            return mesh;
        }

        /// <summary>The route's walk for a recorded mesh, as AutonomousZonePointApproach.TryResolve does with the real corridor
        /// check: the floor point itself when it lies on walkable floor, else the nearest walkable surface within 220 across (and
        /// 256 up or down) of it; only spots the filter accepts.</summary>
        public Vector3? Reach(Vector3 floor, Func<Vector3, bool> accept)
        {
            bool onWalk = _cells.TryGetValue(Key(floor.X, floor.Y), out var own) && own.Any(l => l.Walk && MathF.Abs(l.Z - floor.Z) <= Climb);
            return (onWalk ? new[] { floor } : [])
                .Concat(Walkable().Where(p => Vector2.Distance(new(p.X, p.Y), new(floor.X, floor.Y)) <= 220 && MathF.Abs(p.Z - floor.Z) <= 256)
                    .OrderBy(p => Vector3.DistanceSquared(p, floor)))
                .Cast<Vector3?>().FirstOrDefault(p => accept == null || accept(p.Value));
        }

        /// <summary>On walkable floor: a recorded walkable surface in its cell within <see cref="Climb"/> of its height.</summary>
        public bool OnWalkable(Vector3 p) =>
            _cells.TryGetValue(Key(p.X, p.Y), out var list) && list.Any(l => l.Walk && MathF.Abs(l.Z - p.Z) <= Climb);
    }

    [TestFixture]
    public class UT_ScurceolOrbStands
    {
        private static readonly Vector3 Scurceol = UT_ScurceolOrbStandProbe.Scurceol;
        private static readonly Vector3[] Orbs = UT_ScurceolOrbStandProbe.Orbs;
        private static readonly Lazy<LayeredMesh> Krondon = new(LayeredMesh.Krondon);
        private static Zone Zone => UT_RealmRaidFormation.Zone();

        // ---- the real Krondon mesh (recorded) ------------------------------------------------------------------------

        [Test]
        public void EveryOrbHasAStandTheRaidCanWalkToAndShootFrom()
        {
            LayeredMesh mesh = Krondon.Value;
            for (int i = 0; i < Orbs.Length; i++)
            {
                Vector3 orb = Orbs[i];
                Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, orb, Scurceol, mesh.Reach, out Vector3 stand, out string why), Is.True,
                    $"orb {i + 1}: {why}");
                TestContext.WriteLine($"orb {i + 1} {orb}: stand {stand}, {why}");
                Assert.That(mesh.OnWalkable(stand), $"orb {i + 1}: the stand is on the floor the raid walks from Scurceol's");
                Assert.That(Vector3.Distance(stand, orb), Is.LessThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddRangedReach), $"orb {i + 1}: in range");
                Assert.That(Vector3.Distance(stand, Scurceol), Is.GreaterThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddStandBossClearance),
                    $"orb {i + 1}: well off Scurceol");
                Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(mesh, Zone, stand, orb), $"orb {i + 1}: a clear line");
            }
        }

        [Test]
        public void The112SearchFoundNoStandOnTheRealMesh()
        {
            // What the owner's PC soak logged three times: "no reachable floor spot within 1400 has line of sight to it". The
            // fixture reproduces it, so the test above fails with the 1.1.2 search.
            LayeredMesh mesh = Krondon.Value;
            foreach (Vector3 orb in Orbs)
            {
                Assert.That(UT_ScurceolOrbStandProbe.LegacyTryRangedStand(mesh, Zone, orb, Scurceol, f => mesh.Reach(f, null), out _, out string why),
                    Is.False, why);
                Assert.That(why, Does.Contain("no floor ray to the floor under it"));
            }
        }

        [Test]
        public void TheFloorRayNeverCrossesThePitToAnOrbsPerch()
        {
            // The root cause: each orb hangs over its own perch, across a pit from the raid's floor. Hundreds of reachable spots are
            // in range of each orb, and the floor ray from none of them gets under it.
            LayeredMesh mesh = Krondon.Value;
            foreach (Vector3 orb in Orbs)
            {
                Vector3 perch = mesh.GetClosestPoint(Zone, orb, 128, 128, RealmRaidDungeonRoute.RangedFlyerReach, null).Value;
                Assert.That(orb.Z - perch.Z, Is.InRange(40, 70), "the orb floats just over its perch");
                Assert.That(mesh.IsWalkable(perch), Is.False, "nobody walks onto the perch");
                Vector3[] inRange = mesh.Walkable().Where(p => Vector3.Distance(p, orb) <= RealmRaidDungeonRoute.RequiredAddRangedReach).ToArray();
                Assert.That(inRange.Length, Is.GreaterThan(200));
                Assert.That(inRange.Count(p => mesh.HasLineOfSight(Zone, p, perch, null)), Is.Zero);
                Assert.That(inRange.Count(p => RealmRaidDungeonRoute.HasSightToAirborne(mesh, Zone, p, orb)), Is.GreaterThan(0));
            }
        }

        // ---- the search on built geometry ----------------------------------------------------------------------------

        // A boss on a platform (radius 900 at height 1,000), a pit floor 900 lower around it, and an orb 200 over a small perch.
        private static readonly Vector3 Boss = new(0, 0, 1000), Orb = new(1300, 0, 1500);

        private static LayeredMesh Room(float perchRadius = 100, float perchZ = 1300, bool pit = true)
        {
            var mesh = new LayeredMesh(50).Disk(0, 0, 900, 1000, true);
            if (pit) mesh.Disk(0, 0, 3000, 100, false, inner: 900);
            return perchRadius > 0 ? mesh.Disk(Orb.X, Orb.Y, perchRadius, perchZ, false) : mesh;
        }

        private static Vector3? WalkOnPlatform(Vector3 p, Func<Vector3, bool> accept) =>
            MathF.Abs(p.Z - 1000) < 1 && (accept == null || accept(p)) ? p : null;

        [Test]
        public void AnOrbOverAPitIsShotFromThePlatformEdgeWellOffTheBoss()
        {
            LayeredMesh mesh = Room();
            Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, Orb, Boss, WalkOnPlatform, out Vector3 stand, out string why), Is.True, why);
            Assert.That(stand.Z, Is.EqualTo(1000), "on the platform, not the perch or the pit");
            Assert.That(Vector3.Distance(stand, Boss), Is.GreaterThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddStandBossClearance));
            Assert.That(Vector3.Distance(stand, Orb), Is.LessThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddRangedReach));
            Assert.That(why, Does.StartWith("ranged from the floor"));
        }

        [Test]
        public void APerchTooWideForTheAngleHidesTheOrbFromTheFloor()
        {
            // The orb only 50 over a perch 400 wide: every line from the platform runs into the perch's top.
            LayeredMesh mesh = Room(perchRadius: 400, perchZ: 1450);
            Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, Orb, Boss, WalkOnPlatform, out _, out string why), Is.False);
            Assert.That(why, Does.Contain("has a clear line to it"));
        }

        [Test]
        public void ARockTopOverTheLineBlocksItAndOneUnderItDoesNot()
        {
            var stand = new Vector3(850, 0, 1000);
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(Room(), Zone, stand, Orb), Is.True);
            // Halfway across the pit the line is at about 1,290: a rock top at 1,380 is in the way, one at 1,200 is under it.
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(Room().Disk(1075, 0, 30, 1380, false), Zone, stand, Orb), Is.False);
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(Room().Disk(1075, 0, 30, 1200, false), Zone, stand, Orb), Is.True);
        }

        [Test]
        public void AFloorThatEndsAtAWallIsNoDrop()
        {
            var stand = new Vector3(850, 0, 1000);
            // No ground beyond the platform's edge (solid rock up to the ceiling has no surface): the floor ends at a wall.
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(Room(pit: false), Zone, stand, Orb), Is.False);
            // Floor beyond the edge at the platform's own height that the floor ray can't reach: a wall between two floors.
            LayeredMesh walled = new LayeredMesh(50).Disk(0, 0, 900, 1000, true).Disk(0, 0, 3000, 1000, false, inner: 950)
                .Disk(Orb.X, Orb.Y, 100, 1300, false);
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(walled, Zone, stand, Orb), Is.False);
            // Ground far below it: a drop, and the line is open.
            Assert.That(RealmRaidDungeonRoute.HasSightToAirborne(Room(), Zone, stand, Orb), Is.True);
        }

        [Test]
        public void ANearerSpotOnTheBossIsNotTakenOverOneOffHim()
        {
            // Two floors in range of the orb: a ring beside the boss (within 700 of him) and a patch well off him.
            var orb = new Vector3(0, 900, 1500);
            var mesh = new LayeredMesh(50).Disk(0, 0, 650, 1000, true).Disk(0, 0, 3000, 100, false, inner: 650)
                .Disk(900, 1300, 150, 1000, true);
            Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, orb, Boss, WalkOnPlatform, out Vector3 stand, out string why), Is.True, why);
            Assert.That(Vector3.Distance(stand, Boss), Is.GreaterThanOrEqualTo(RealmRaidDungeonRoute.RequiredAddStandBossClearance), "off him");
            // With only the ring left, a spot beside him (but never in his melee) is still taken.
            mesh = new LayeredMesh(50).Disk(0, 0, 650, 1000, true).Disk(0, 0, 3000, 100, false, inner: 650);
            Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, orb, Boss, WalkOnPlatform, out stand, out why), Is.True, why);
            Assert.That(Vector3.Distance(stand, Boss), Is.InRange(RealmRaidDungeonRoute.RequiredAddStandMinimumBossDistance,
                RealmRaidDungeonRoute.RequiredAddStandBossClearance));
        }

        [TestCase("no floor", "no floor")]
        [TestCase("too far", "within 1400")]
        [TestCase("unreachable", "reachable from the raid")]
        public void AnOrbNobodyCanShootSaysWhy(string trouble, string reason)
        {
            LayeredMesh mesh = trouble == "no floor" ? new LayeredMesh(50) : Room();
            Vector3 orb = trouble == "too far" ? Orb + new Vector3(0, 0, 2_000) : Orb;
            Func<Vector3, Func<Vector3, bool>, Vector3?> reach = trouble == "unreachable" ? (_, _) => null : WalkOnPlatform;
            Assert.That(RealmRaidDungeonRoute.TryRangedStand(mesh, Zone, orb, Boss, reach, out _, out string why), Is.False);
            Assert.That(why, Does.Contain(reason));
        }
    }
}
