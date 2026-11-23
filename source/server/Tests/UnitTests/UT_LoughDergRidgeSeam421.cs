using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Issue #421: AUTONOMOUS_ZONE_SEAM_CYCLE at the Lough Derg / Valley of Bri Leith seam (200:384960,486688 and
/// 385088,483424). On the installed meshes (zone200/206.nav, the 1.2 download) Lough Derg has a ridge along its Connacht
/// border (seam file ground 6081, 1,379 polygons) that joins no other Lough Derg ground. It meets the Valley's main
/// ground (61486) at y 486,624-488,352 and the Valley shelf (10136) at y 483,424-486,496, and Connacht only on a ledge
/// (69404). Lough Derg's main ground (69676) meets the Valley's from y 488,480. The seam lines below are those of the
/// installed navmesh/seams.json for these borders (fewer points); the fake mesh has the same pieces of ground.
/// Tests only: not yet seen on the owner's PC.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_LoughDergRidgeSeam421
{
    [TearDown]
    public void TearDown() => AutonomousZoneSeams.SetForTests(null);

    private static Zone Z(ushort id, int x, int y) => new(null, id, $"zone{id}", x, y, 65536, 65536, id, false, 0, false, 0, 0, 0, 0, 0);

    private static readonly Zone LoughDerg = Z(200, 319488, 483328);
    private static readonly Zone Valley = Z(206, 385024, 483328);
    private static readonly Zone Connacht = Z(207, 286720, 417792);
    private static readonly Zone Cruachan = Z(211, 319488, 352256);
    private static readonly Zone[] Hibernia = [LoughDerg, Valley, Connacht, Cruachan];

    private const string SeamFile = """
        {"version": 3, "step": 64, "main": {"200": {"200": 69676, "206": 61486, "207": 69397, "211": 21922}},
         "regions": {"200": [
          {"from": 200, "to": 206, "points": [[483424, 7264, 7262, 6081, 10136], [483488, 7264, 7262, 6081, 10136],
                                             [486624, 5490, 5520, 6081, 61486], [486688, 5480, 5518, 6081, 61486],
                                             [488480, 5200, 5208, 69676, 61486], [488544, 5200, 5208, 69676, 61486]]},
          {"from": 206, "to": 200, "points": [[483424, 7262, 7264, 10136, 6081], [483488, 7262, 7264, 10136, 6081],
                                             [486624, 5520, 5490, 61486, 6081], [486688, 5518, 5480, 61486, 6081],
                                             [488480, 5208, 5200, 61486, 69676], [488544, 5208, 5200, 61486, 69676]]},
          {"from": 200, "to": 207, "points": [[340000, 5203, 5203, 69676, 69397], [340064, 5203, 5203, 69676, 69397],
                                             [350432, 5500, 5510, 6081, 69404], [350496, 5500, 5510, 6081, 69404],
                                             [352032, 5500, 5510, 6081, 69404], [352096, 5500, 5510, 6081, 69404]]},
          {"from": 207, "to": 200, "points": [[340000, 5203, 5203, 69397, 69676], [340064, 5203, 5203, 69397, 69676],
                                             [350432, 5510, 5500, 69404, 6081], [350496, 5510, 5500, 69404, 6081],
                                             [352032, 5510, 5500, 69404, 6081], [352096, 5510, 5500, 69404, 6081]]},
          {"from": 207, "to": 211, "points": [[334880, 6561, 6527, 69397, 21922], [334944, 6563, 6530, 69397, 21922],
                                             [351000, 8000, 8000, 69404, 64207], [351064, 8000, 8000, 69404, 64207]]},
          {"from": 211, "to": 207, "points": [[334880, 6527, 6561, 21922, 69397], [334944, 6530, 6563, 21922, 69397],
                                             [351000, 8000, 8000, 64207, 69404], [351064, 8000, 8000, 64207, 69404]]}
         ]}}
        """;

    /// <summary>The ground a point stands on, as the installed meshes split it (dfprobe reach on zone200/206/207.nav).</summary>
    private static string Ground(Zone zone, Vector3 p) => zone.ID switch
    {
        200 => p.X >= 350300 && p.Y < 488416 ? "ridge" : "main",
        206 => p.Y < 486560 ? "shelf" : "main",
        207 => p.X >= 350000 ? "ledge" : "main",
        211 => p.X >= 350000 ? "ledge" : "main",
        _ => "main",
    };

    /// <summary>Every point is floor; a walk reaches its end only on the same ground (else it stops where it is).</summary>
    private sealed class GroundNav : IPathfindingMgr
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

    private static readonly Vector3 ValleySide = new(385088, 486688, 5518); // the Valley end of the 384960,486688 crossing
    private static readonly Vector3 RidgeSide = new(384960, 486688, 5480);  // AUTONOMOUS_ZONE_SEAM_CYCLE zone=200 position
    private static readonly Vector3 DfPortal = new(325269, 433985, 6297);   // Hibernia's Darkness Falls portal (Connacht)
    private static readonly Vector3 GorgeCamp = new(335000, 400000, 6500);  // Cruachan Gorge main ground

    private static Zone At(Vector3 p) => Hibernia.First(zone => p.X >= zone.XOffset && p.X < zone.XOffset + zone.Width &&
        p.Y >= zone.YOffset && p.Y < zone.YOffset + zone.Height);

    [Test]
    public void FromTheValleyTowardConnachtTheStepEntersLoughDergsMainGroundNotTheRidge()
    {
        AutonomousZoneSeams.SetForTests(SeamFile);
        var trace = new List<string>();
        bool ok = AutonomousZoneItinerary.ProbeNextStep(Hibernia, 200, Valley, Connacht, ValleySide, DfPortal, new GroundNav(),
            out var step, trace);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, string.Join("\n", trace));
            Assert.That(At(step.Outside), Is.EqualTo(LoughDerg));
            // 1.2.1: both picks of the Valley's main ground were ridge crossings (nearer Connacht), so nothing was left
            // out and the bot stepped onto the ridge at 384960,486688; the ridge sent it straight back.
            Assert.That(Ground(LoughDerg, step.Outside), Is.EqualTo("main"), $"stepped to {step.Outside}\n" + string.Join("\n", trace));
            Assert.That(step.Outside.Y, Is.GreaterThanOrEqualTo(488416));
        });
    }

    [Test]
    public void FromTheRidgeTheBotLeavesThroughTheValleyNotOntoTheConnachtLedge()
    {
        AutonomousZoneSeams.SetForTests(SeamFile);
        var trace = new List<string>();
        bool ok = AutonomousZoneItinerary.ProbeNextStep(Hibernia, 200, LoughDerg, Cruachan, RidgeSide, GorgeCamp, new GroundNav(),
            out var step, trace);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, string.Join("\n", trace));
            // 1.2.1: the direct border crossing (unlisted, on ground 69404) passed because the ledge continues into a
            // Cruachan ledge, and the bot walked onto the Connacht ledge and back.
            Assert.That(At(step.Outside), Is.EqualTo(Valley), $"stepped to {step.Outside}\n" + string.Join("\n", trace));
            Assert.That(Ground(Valley, step.Outside), Is.EqualTo("main"), "onto the Valley's main ground, not its shelf");
        });
    }

    [Test]
    public void AGoalOnTheRidgeItselfIsStillReachedFromTheValley()
    {
        // On the last border the walk on to the goal is proved itself, so the main-ground rule stays out of it: a goal on
        // the ridge keeps its ridge crossing.
        AutonomousZoneSeams.SetForTests(SeamFile);
        var trace = new List<string>();
        var onTheRidge = new Vector3(384700, 486700, 5480);
        bool ok = AutonomousZoneItinerary.ProbeNextStep(Hibernia, 200, Valley, LoughDerg, ValleySide, onTheRidge,
            new GroundNav(), out var step, trace);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, string.Join("\n", trace));
            Assert.That(At(step.Outside), Is.EqualTo(LoughDerg));
            Assert.That(Ground(LoughDerg, step.Outside), Is.EqualTo("ridge"), $"stepped to {step.Outside}");
        });
    }
}
