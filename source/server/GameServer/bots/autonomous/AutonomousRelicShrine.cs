using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The relic shrine of a relic keep as the bots' siege objective (relic audit 2026-10-31, B1/B2/L9). Relic keeps have no
/// lord, so after the breach the attackers used to hold at the keep centre: 650-680 units and 832 below the pad at four
/// of the six keeps, out of the 600 pickup scan and the 500 "eight on the pad" count. The pad is now the approach target,
/// reached as soon as a walk to its floor exists (not only once every door of the keep is down).
/// </summary>
public static class AutonomousRelicShrine
{
    /// <summary>A pad belongs to the relic keep it stands in (the pads are 80-700 units from their keep's centre).</summary>
    public const int PadKeepRadius = 3_000;

    /// <summary>A path corner or segment this close to a closed door (2D, within <see cref="DoorHeight"/>) runs through it.</summary>
    public const float DoorRadius = 140;
    public const float DoorHeight = 260;

    private static readonly ConcurrentDictionary<GameRelicPad, Vector3> Floors = new();

    public static GameRelicPad PadOf(AbstractGameKeep keep)
    {
        if (keep == null || !keep.IsRelic) return null;
        return RelicMgr.GetPadsSnapshot()
            .Where(pad => pad.CurrentRegionID == keep.Region && Math.Abs(pad.X - keep.X) <= PadKeepRadius && Math.Abs(pad.Y - keep.Y) <= PadKeepRadius)
            .OrderBy(pad => (long)(pad.X - keep.X) * (pad.X - keep.X) + (long)(pad.Y - keep.Y) * (pad.Y - keep.Y))
            .FirstOrDefault();
    }

    public static AbstractGameKeep RelicKeepOf(GameRelicPad pad)
    {
        if (pad == null || GameServer.KeepManager == null) return null;
        return GameServer.KeepManager.GetKeepsOfRegion(pad.CurrentRegionID)
            .Where(keep => keep.IsRelic && Math.Abs(pad.X - keep.X) <= PadKeepRadius && Math.Abs(pad.Y - keep.Y) <= PadKeepRadius)
            .OrderBy(keep => (long)(pad.X - keep.X) * (pad.X - keep.X) + (long)(pad.Y - keep.Y) * (pad.Y - keep.Y))
            .FirstOrDefault();
    }

    /// <summary>The relic keep a mounted relic stands in (null for a carried or dropped relic).</summary>
    public static AbstractGameKeep RelicKeepOf(GameRelic relic) =>
        relic?.CurrentRelicPad is { } pad ? RelicKeepOf(pad) : null;

    /// <summary>The walkable floor at a relic pad (the relic stands on it).</summary>
    public static Vector3? ShrineFloor(IPathfindingMgr nav, Zone zone, Vector3 pad)
    {
        if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return null;
        return nav.GetClosestPoint(zone, pad, 64, 64, 256, nav.DefaultFilters) ??
               nav.GetClosestPoint(zone, pad, 160, 160, 640, nav.DefaultFilters);
    }

    /// <summary>The shrine floor of a live pad, cached (pads and their floors do not move).</summary>
    public static Vector3? ShrineFloor(IPathfindingMgr nav, GameRelicPad pad)
    {
        if (pad == null) return null;
        if (Floors.TryGetValue(pad, out Vector3 cached)) return cached;
        Zone zone = pad.CurrentRegion?.GetZone(pad.X, pad.Y);
        Vector3? floor = ShrineFloor(nav, zone, new(pad.X, pad.Y, pad.Z));
        if (floor.HasValue) Floors[pad] = floor.Value;
        return floor;
    }

    /// <summary>Whether a walk (its corners in order) passes through a door position.</summary>
    public static bool WalkPassesDoor(IReadOnlyList<Vector3> walk, Vector3 door, float radius = DoorRadius, float height = DoorHeight)
    {
        if (walk == null || walk.Count == 0) return false;
        Vector2 point = new(door.X, door.Y);
        for (int i = 0; i < walk.Count; i++)
        {
            Vector3 a = walk[i], b = i + 1 < walk.Count ? walk[i + 1] : walk[i];
            Vector2 a2 = new(a.X, a.Y), b2 = new(b.X, b.Y), ab = b2 - a2;
            float t = ab.LengthSquared() < 1 ? 0 : Math.Clamp(Vector2.Dot(point - a2, ab) / ab.LengthSquared(), 0, 1);
            Vector2 closest = a2 + ab * t;
            float z = a.Z + (b.Z - a.Z) * t;
            if (Vector2.DistanceSquared(closest, point) <= radius * radius && Math.Abs(z - door.Z) <= height)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the walk from start to end runs through one of the closed doors. The six relic gates (door 02 of each
    /// relic keep) are not registered in the navmesh ("Failed to register door in pathfinding provider ... Relic Gate"
    /// at every start), so the mesh alone does not know whether they are shut.
    /// </summary>
    public static bool ClosedDoorOnWalk(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end, IReadOnlyCollection<Vector3> closedDoors)
    {
        if (closedDoors == null || closedDoors.Count == 0 || nav == null || zone == null) return false;
        var nodes = new WrappedPathfindingNode[256];
        var result = nav.GetPathStraight(zone, start, end, nav.DefaultFilters, nodes);
        var walk = new List<Vector3>(Math.Min(result.NodeCount, nodes.Length) + 1) { start };
        for (int i = 0; i < Math.Min(result.NodeCount, nodes.Length); i++) walk.Add(nodes[i].Position);
        foreach (Vector3 door in closedDoors)
            if (WalkPassesDoor(walk, door)) return true;
        return false;
    }

    /// <summary>
    /// The relic of the attacker's target relic keep that may be lifted now: mounted on that keep's pad, of another realm,
    /// and one the attacker's realm may take (it holds its own relic of that type).
    /// </summary>
    public static GameRelic TargetRelic(AbstractGameKeep keep, GameLiving attacker)
    {
        GameRelicPad pad = PadOf(keep);
        if (pad == null || attacker == null) return null;
        return pad.MountedRelics.FirstOrDefault(relic => relic.ObjectState == GameObject.eObjectState.Active &&
            relic.IsMounted && relic.Realm != attacker.Realm && RelicMgr.CanPickupRelicFromShrine(attacker, relic));
    }
}
