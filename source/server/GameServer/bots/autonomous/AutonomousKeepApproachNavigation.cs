using System;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>Planning-only view that honors the mover's closed-door restrictions.
/// Does not change meshes, doors or the shared pathfinding provider.</summary>
public sealed class AutonomousKeepApproachNavigation : PathfindingMgrBase
{
    private readonly IPathfindingMgr _nav;
    private readonly Vector3[] _friendlyDoors;
    private readonly Vector3[] _unregisteredClosedDoors;
    public AutonomousKeepApproachNavigation(IPathfindingMgr nav, Vector3[] friendlyDoors, Vector3[] unregisteredClosedDoors = null)
    { _nav = nav; _friendlyDoors = friendlyDoors; _unregisteredClosedDoors = unregisteredClosedDoors ?? []; }

    public static IPathfindingMgr ForRealm(IPathfindingMgr nav, Region region, eRealm realm)
    {
        if (nav == null || nav is AutonomousKeepApproachNavigation) return nav;
        if (realm==eRealm.None || region==null) return new AutonomousKeepApproachNavigation(nav,[]);
        // Owner tests 2026-10-07 (Nottmoor Faste, Caer Erasleigh): once the outer gate was broken, every ram spot at
        // the keep door read "unconnected" because the broken gate still counted as a closed enemy door. A door that
        // is broken or standing open is passable like a friendly one; the mover walks through it.
        // A postern has no health (it cannot be battered), so "not alive" made every shut enemy postern passable too and
        // planned walks ran through it (relic audit 2026-10-31, B2; battleground run 19: stormers stuck at a postern).
        var doors = GameServer.KeepManager?.GetKeepsOfRegion(region.ID)
            .SelectMany(k => k.Doors.Values.Where(d => k.Realm == realm || d.IsAttackableDoor && !d.IsAlive || d.State == eDoorState.Open))
            .Select(d => new Vector3(d.X,d.Y,d.Z)).ToArray() ?? [];
        // Relic audit 2026-10-31 (B3): the navmesh has no door polygons at the six relic gates (door 02 of each relic
        // keep), so a shut relic gate of an enemy keep did not block a planned walk at all (UT_RelicShrineReach: it lies
        // on the open walk to every shrine). Such doors are checked against the planned walk below.
        var unregistered = DoorMgr.UnregisteredInNavmesh
            .OfType<DOL.GS.Keeps.GameKeepDoor>()
            .Where(d => d.CurrentRegionID == region.ID && d.IsAlive && d.State == eDoorState.Closed &&
                        d.Component?.Keep is { } keep && keep.Realm != realm)
            .Select(d => new Vector3(d.X,d.Y,d.Z)).ToArray();
        return new AutonomousKeepApproachNavigation(nav, doors, unregistered);
    }

    public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end,
        EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
    {
        var result = _nav.GetPathStraight(zone,start,end,filters,destination);
        for (int i = 0; i < Math.Min(result.NodeCount,destination.Length); i++)
        {
            var node = destination[i];
            if ((node.Flags & EDtPolyFlags.BlockingDoor) == 0) continue;
            bool friendly = false;
            foreach (var door in _friendlyDoors)
                if (Vector3.DistanceSquared(door,node.Position) <= Pathfinder.DOOR_SEARCH_DISTANCE * Pathfinder.DOOR_SEARCH_DISTANCE)
                { friendly = true; break; }
            if (!friendly)
            {
                var alternative=_nav.GetPathStraight(zone,start,end,_nav.BlockingDoorAvoidanceFilters,destination);
                // Match Pathfinder.TryApplyAlternativePath exactly: a partial
                // route ending at the outside of the wall is NOT permission
                // to aim for an interior endpoint, even when only 48 units away.
                return alternative.Status==PathfindingStatus.PathFound && !CrossesUnregisteredDoor(start,alternative,destination)
                    ? alternative : new(PathfindingStatus.NoPathFound,0);
            }
        }
        return CrossesUnregisteredDoor(start,result,destination) ? new(PathfindingStatus.NoPathFound,0) : result;
    }

    /// <summary>Whether a found walk runs through a shut enemy door the navmesh does not know (DoorMgr.UnregisteredInNavmesh).</summary>
    private bool CrossesUnregisteredDoor(Vector3 start, PathfindingResult result, Span<WrappedPathfindingNode> nodes)
    {
        if (_unregisteredClosedDoors.Length == 0 || result.NodeCount <= 0) return false;
        var walk = new System.Collections.Generic.List<Vector3>(result.NodeCount + 1) { start };
        for (int i = 0; i < Math.Min(result.NodeCount, nodes.Length); i++) walk.Add(nodes[i].Position);
        foreach (var door in _unregisteredClosedDoors)
            if (AutonomousRelicShrine.WalkPassesDoor(walk, door)) return true;
        return false;
    }
    public override bool IsAvailable => _nav.IsAvailable;
    public override bool HasNavmesh(Zone zone) => _nav.HasNavmesh(zone);
    public override EDtPolyFlags[] DefaultFilters => _nav.DefaultFilters;
    public override EDtPolyFlags[] BlockingDoorAvoidanceFilters => _nav.BlockingDoorAvoidanceFilters;
    public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters)
        => _nav.GetClosestPoint(zone,point,x,y,z,filters);
    public override Vector3? GetClosestPoint(Zone zone, Vector3 point, EDtPolyFlags[] filters)
        => _nav.GetClosestPoint(zone,point,filters);
    public override bool HasLineOfSight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters)
        => _nav.HasLineOfSight(zone,start,end,filters);
}
