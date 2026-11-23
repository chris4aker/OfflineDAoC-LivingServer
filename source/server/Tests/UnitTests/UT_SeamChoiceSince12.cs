using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.2 regression guard (audit item 7, #421): the zone itinerary's seam choice changed in 1.2.2 for every border (no
/// crossing onto ground that never reaches the destination's main ground while the border lists one that does). These
/// tests pin, on the real seam data, the crossings chosen for two walks seen on the owner's PC on 1.2 builds:
/// - the Hibernian siege column through Cruachan Gorge, walked single file on <c>aa537ce</c> (Druim Ligen, Connacht, to
///   Dun nGed in Breifine: 207 > 211 > 212);
/// - the walk to Druim Ligen (the medallion vendor) from both sides of the Cruachan Gorge / Breifine seam
///   (200:384960,385504), the 43c43aa loop that 1.2 fixed.
/// The seam data is the 1.2 / v0.36b download's navmesh/seams.json (sha256 70774af9...), its region 200 lines and main
/// grounds unchanged (tools/navmesh/seams-region200-v036b.json.gz). The expected crossings are 1.2's choice:
/// tools/navmesh/seam_route_sim.py with --rule old (the 1.2 / 1.2.1 pick rule) over the 1.2 meshes (zone207/211/212.nav,
/// with that download's roads.json), and --rule new (1.2.2) picks the same ones. The fake mesh below joins two points
/// exactly when the seam file gives them the same ground id (the real connected pieces of each zone's mesh); the start and
/// goal grounds are the ones dfprobe reach and roads.json give (Druim Ligen 69397 and Dun nGed 62732: each zone's main
/// ground). Tests only.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_SeamChoiceSince12
{
    private static Zone Z(ushort id, int x, int y) => new(null, id, $"zone{id}", x, y, 65536, 65536, id, false, 0, false, 0, 0, 0, 0, 0);

    // Region 200's zones (Zones.xml).
    private static readonly Zone[] Hibernia =
    [
        Z(200, 319488, 483328), Z(201, 319488, 548864), Z(202, 286720, 614400), Z(203, 221184, 417792),
        Z(204, 286720, 679936), Z(205, 352256, 712704), Z(206, 385024, 483328), Z(207, 286720, 417792),
        Z(208, 450560, 483328), Z(210, 385024, 417792), Z(211, 319488, 352256), Z(212, 385024, 352256),
        Z(214, 417792, 286720), Z(216, 352256, 647168),
    ];
    private static Zone ById(ushort id) => Hibernia.Single(z => z.ID == id);
    private static Zone Connacht => ById(207);
    private static Zone CruachanGorge => ById(211);
    private static Zone Breifine => ById(212);

    private static readonly Vector3 DruimLigen = new(333231, 420812, 5184);  // goal=333231,420812,5184 (medallion vendor)
    private static readonly Vector3 DunNGed = new(397098, 399736, 4376);     // keep 103 (rvr-lanes.json)
    private static readonly Vector3 GorgeSide = new(384960, 385504, 6699);   // the 43c43aa cycle spot, zone 211
    private static readonly Vector3 BreifineSide = new(385088, 385504, 6713); // the same line's zone 212 position

    private static string _seams;
    private static List<(ushort Zone, Vector3 At, int Area)> _grounds;

    private static string FindUp(params string[] relative)
    {
        for (DirectoryInfo at = new(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
        {
            string candidate = Path.Combine(new[] { at.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [OneTimeSetUp]
    public void Load()
    {
        string path = FindUp("tools", "navmesh", "seams-region200-v036b.json.gz");
        Assert.That(path, Is.Not.Null, "tools/navmesh/seams-region200-v036b.json.gz not found");
        using (var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        using (var reader = new StreamReader(gz))
            _seams = reader.ReadToEnd();
        // Every listed crossing's two ends as the itinerary places them (64 either side of the border), with their ground.
        _grounds = new();
        using JsonDocument doc = JsonDocument.Parse(_seams);
        foreach (JsonElement line in doc.RootElement.GetProperty("regions").GetProperty("200").EnumerateArray())
        {
            Zone a = ById(line.GetProperty("from").GetUInt16()), b = ById(line.GetProperty("to").GetUInt16());
            bool vertical; float border; int dir;
            if (a.XOffset + a.Width == b.XOffset) (vertical, border, dir) = (true, b.XOffset, 1);
            else if (b.XOffset + b.Width == a.XOffset) (vertical, border, dir) = (true, a.XOffset, -1);
            else if (a.YOffset + a.Height == b.YOffset) (vertical, border, dir) = (false, b.YOffset, 1);
            else (vertical, border, dir) = (false, a.YOffset, -1);
            foreach (JsonElement p in line.GetProperty("points").EnumerateArray())
            {
                float along = p[0].GetSingle();
                Vector3 inside = vertical ? new(border - dir * 64, along, p[1].GetSingle()) : new(along, border - dir * 64, p[1].GetSingle());
                Vector3 outside = vertical ? new(border + dir * 64, along, p[2].GetSingle()) : new(along, border + dir * 64, p[2].GetSingle());
                _grounds.Add((a.ID, inside, p[3].GetInt32()));
                _grounds.Add((b.ID, outside, p[4].GetInt32()));
            }
        }
    }

    [SetUp]
    public void UseTheRealSeams() => AutonomousZoneSeams.SetForTests(_seams);

    [TearDown]
    public void TearDown() => AutonomousZoneSeams.SetForTests(null);

    /// <summary>The ground of a point: the declared start and goal grounds, else that of the nearest listed crossing end
    /// within the itinerary's own reach (96 across or along, 160 in height); else a ground of its own.</summary>
    private static int Ground(Zone zone, Vector3 p)
    {
        if (zone.ID == 207 && Vector3.Distance(p, DruimLigen) < 2) return 69397;
        if (zone.ID == 212 && Vector3.Distance(p, DunNGed) < 2) return 62732;
        int best = int.MinValue; float bestScore = float.MaxValue;
        foreach (var g in _grounds)
        {
            if (g.Zone != zone.ID || MathF.Abs(g.At.X - p.X) > 96 || MathF.Abs(g.At.Y - p.Y) > 96 || MathF.Abs(g.At.Z - p.Z) > 160) continue;
            float dz = (g.At.Z - p.Z) * 2, score = Vector2.DistanceSquared(new(g.At.X, g.At.Y), new(p.X, p.Y)) + dz * dz;
            if (score < bestScore) { bestScore = score; best = g.Area; }
        }
        return best != int.MinValue ? best : HashCode.Combine(zone.ID, (int)p.X, (int)p.Y, (int)p.Z) | 1 << 30;
    }

    private sealed class SeamGroundNav : IPathfindingMgr
    {
        public bool Init() => true;
        public void Stop() { }
        public bool RegisterDoor(GameDoorBase door) => true;
        public bool UpdateDoorFlags(GameDoorBase door) => true;
        public PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
        {
            if (Ground(zone, start) == Ground(zone, end))
            {
                destination[0] = new WrappedPathfindingNode(end, default);
                return new PathfindingResult(PathfindingStatus.PathFound, 1);
            }
            destination[0] = new WrappedPathfindingNode(start, default);
            return new PathfindingResult(PathfindingStatus.PartialPathFound, 1);
        }
        public Vector3? GetMoveAlongSurface(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters) => null;
        public Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters) => null;
        public Vector3? GetClosestPoint(Zone zone, Vector3 position, EDtPolyFlags[] filters) => position;
        public Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange, float zRange, EDtPolyFlags[] filters) => position;
        public Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters) => null;
        public Vector3? GetRoofAbove(Zone zone, Vector3 position, float maxHeight, EDtPolyFlags[] filters) => null;
        public Vector3? GetFloorBeneath(Zone zone, Vector3 position, float maxDepth, EDtPolyFlags[] filters) => position;
        public bool TrySnapToMesh(Zone zone, ref Vector3 position, float range) => true;
        public bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) => true;
        public bool HasNavmesh(Zone zone) => true;
        public bool IsAvailable => true;
        public EDtPolyFlags[] DefaultFilters => [];
        public EDtPolyFlags[] BlockingDoorAvoidanceFilters => [];
    }

    // The fake mesh keeps every height as given, so a step's heights are the seam file's own (the simulator above prints
    // them snapped to the floor: 6562 / 6528 at 334880, 6713 at 385088,385504).
    private static void AssertStep(Zone from, Zone to, Vector3 start, Vector3 goal, Vector3 inside, Vector3 outside)
    {
        var trace = new List<string>();
        bool ok = AutonomousZoneItinerary.ProbeNextStep(Hibernia, 200, from, to, start, goal, new SeamGroundNav(), out var step, trace);
        string log = $"stepped {step.Inside} > {step.Outside}\n" + string.Join("\n", trace);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, log);
            Assert.That(Vector3.Distance(step.Inside, inside), Is.LessThan(1f), "1.2's crossing (inside): " + log);
            Assert.That(Vector3.Distance(step.Outside, outside), Is.LessThan(1f), "1.2's crossing (outside): " + log);
        });
    }

    [Test]
    public void TheSeamDataIsThe12Download()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousZoneSeams.MainArea(200, 207), Is.EqualTo(69397));
            Assert.That(AutonomousZoneSeams.MainArea(200, 211), Is.EqualTo(21922));
            Assert.That(AutonomousZoneSeams.MainArea(200, 212), Is.EqualTo(62732));
            Assert.That(AutonomousZoneSeams.For(200, 207, 211), Has.Length.EqualTo(507));
            Assert.That(AutonomousZoneSeams.For(200, 211, 212), Has.Length.EqualTo(1020));
            Assert.That(_grounds, Has.Count.EqualTo(2 * 22832));
        });
    }

    // seam_route_sim.py --rule old and --rule new, 333231,420812,5184 to 397098,399736,4376:
    //   hop 0: zone 207 route 207>211>212: 334880,417856,6562 > 334880,417728,6528
    //   hop 1: zone 211 route 211>212: 384960,385504,6699 > 385088,385504,6713
    [Test]
    public void TheGorgeColumn_FromDruimLigenToDunNGed_KeepsIts12Crossings()
    {
        AssertStep(Connacht, Breifine, DruimLigen, DunNGed, new(334880, 417856, 6572), new(334880, 417728, 6552));
        AssertStep(CruachanGorge, Breifine, new(334880, 417728, 6552), DunNGed, new(384960, 385504, 6699), new(385088, 385504, 6707));
    }

    // seam_route_sim.py --rule old and --rule new, 385088,385504,6713 to 333231,420812,5184:
    //   hop 0: zone 212 route 212>211>207: 385088,385504,6713 > 384960,385504,6699
    //   hop 1: zone 211 route 211>207: 334880,417728,6528 > 334880,417856,6562
    [Test]
    public void TheWalkToDruimLigen_FromBreifine_KeepsIts12Crossings()
    {
        AssertStep(Breifine, Connacht, BreifineSide, DruimLigen, new(385088, 385504, 6707), new(384960, 385504, 6699));
        AssertStep(CruachanGorge, Connacht, GorgeSide, DruimLigen, new(334880, 417728, 6552), new(334880, 417856, 6572));
    }
}
