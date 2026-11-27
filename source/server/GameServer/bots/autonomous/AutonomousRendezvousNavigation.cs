using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

// Called only when choosing an assembly point, never in a bot's movement tick.
// A valid nav polygon may still be an isolated prop top, not a usable town floor.
public static class AutonomousRendezvousNavigation
{
    // Open lower-city ground: complete corridors to the bank/exit and unobstructed
    // formation rings verified on zone120. Do not randomize onto nearby steps.
    public static Vector3 JordheimMeetingPoint => new(34100, 34600, 8008);

    public static bool TryChooseFixedPoint(IPathfindingMgr nav, Zone zone, Vector3 anchor, out Vector3 point)
    {
        point = default;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? floor = nav.GetClosestPoint(zone, anchor, 48, 48, 96, nav.DefaultFilters);
        if (!floor.HasValue || !HasLocalExit(nav, zone, floor.Value)) return false;
        point = floor.Value;
        return true;
    }

    public static bool CanReachFrom(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 rendezvous)
    {
        AutonomousNavigationSurface.TryFloor(nav, zone, start, out start);
        return AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start, rendezvous);
    }

    /// <summary>
    /// Resolves one deterministic formation offset without allowing it to land
    /// on a nearby prop, shelf, or disconnected polygon. This is called while a
    /// party is formed (and when its membership changes), never every movement
    /// tick.
    /// </summary>
    public static bool TryResolveFormationSlot(
        IPathfindingMgr nav,
        Zone zone,
        Vector3 center,
        Vector3 desired,
        out Vector3 slot)
    {
        slot = default;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return false;

        Vector3? moved = AutonomousNavigationSurface.MoveAlongGround(nav, zone, center, desired);
        if (!moved.HasValue || Vector2.Distance(new(moved.Value.X, moved.Value.Y), new(desired.X, desired.Y)) > 48 ||
            MathF.Abs(moved.Value.Z - center.Z) > 160 || !ConnectedBothWays(nav, zone, center, moved.Value))
            return false;

        slot = moved.Value;
        return true;
    }

    public static bool CanReachFormationSlot(
        IPathfindingMgr nav,
        Region region,
        Zone memberZone,
        Zone slotZone,
        Vector3 memberPosition,
        Vector3 slot)
    {
        if (region == null || memberZone == null || slotZone == null)
            return false;
        if (memberZone == slotZone)
            return CanReachFrom(nav, slotZone, memberPosition, slot);
        return AutonomousZoneItinerary.TryNextStep(region, memberZone, slotZone,
            memberPosition, slot, nav, out _);
    }

    private static readonly Vector2[] ExitOffsets = [new(512, 0), new(0, 512), new(-512, 0), new(0, -512)];

    public static bool TryChoosePoint(IPathfindingMgr nav, Zone zone, Vector3 anchor, out Vector3 point)
    {
        point = default;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? floor = nav.GetClosestPoint(zone, anchor, 48, 48, 96, nav.DefaultFilters);
        if (!floor.HasValue || !HasLocalExit(nav, zone, floor.Value)) return false;
        var choices = new List<Vector3> { floor.Value };
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector3? candidate = nav.GetRandomPoint(zone, floor.Value, 240, nav.DefaultFilters);
            if (candidate.HasValue && Vector3.DistanceSquared(candidate.Value, floor.Value) <= 300 * 300 &&
                ConnectedBothWays(nav, zone, floor.Value, candidate.Value) && HasLocalExit(nav, zone, candidate.Value))
                choices.Add(candidate.Value);
        }
        // A connected lower town floor is preferable to a reachable wall,
        // shelf, or raised service platform.  Keep the projected anchor when
        // candidates are on the same elevation so ordinary street rendezvous
        // remain stable and do not drift between sessions.
        point = choices.OrderBy(candidate => candidate.Z)
            .ThenBy(candidate => Vector3.DistanceSquared(candidate, floor.Value))
            .First();
        return true;
    }

    // #569 step 4 (True165 78a70c0): two rings, farther first; the first walkable, connected spot wins, so a meetup
    // sits as far from the NPC as the band allows. 2 x 6 projections at most, once per party formation.
    private static readonly float[] ClearRings = [420f, 340f];
    private const int ClearRingAngles = 6;

    /// <summary>
    /// #569 step 4: a meeting point BESIDE a service NPC, never on it. <see cref="TryChoosePoint"/> keeps the projected
    /// anchor (the NPC's own spot) on flat ground, so a party waited minutes in a small ring around a stable master or
    /// merchant that the player then could not click. This takes a walkable spot
    /// <see cref="AutonomousServiceNpcClearance.MeetClearRadius"/>-<see cref="AutonomousServiceNpcClearance.MeetOuterRadius"/>
    /// from <paramref name="anchor"/>, connected both ways to the floor beside the NPC and with a local exit, preferring
    /// the farther ring. <paramref name="accept"/> can reject a spot (one near another service NPC, or outside the
    /// town). The start angle comes from <paramref name="seed"/>, so two parties at one NPC do not share a spot.
    /// Called while a party forms, never in a movement tick.
    /// </summary>
    public static bool TryChooseClearPoint(IPathfindingMgr nav, Zone zone, Vector3 anchor, int seed,
        Func<Vector3, bool> accept, out Vector3 point)
    {
        point = default;
        if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? floor = nav.GetClosestPoint(zone, anchor, 48, 48, 96, nav.DefaultFilters);
        if (!floor.HasValue) return false;
        double start = (seed & 0x7fffffff) % 360 * Math.PI / 180;
        foreach (float ring in ClearRings)
            for (int k = 0; k < ClearRingAngles; k++)
            {
                double angle = start + k * 2 * Math.PI / ClearRingAngles;
                Vector3 desired = new(anchor.X + (float)(Math.Cos(angle) * ring), anchor.Y + (float)(Math.Sin(angle) * ring), floor.Value.Z);
                if (desired.X < zone.XOffset || desired.X >= zone.XOffset + zone.Width ||
                    desired.Y < zone.YOffset || desired.Y >= zone.YOffset + zone.Height) continue;
                Vector3? candidate = nav.GetClosestPoint(zone, desired, 48, 48, 160, nav.DefaultFilters);
                if (!candidate.HasValue || !IsInClearBand(anchor, candidate.Value) ||
                    MathF.Abs(candidate.Value.Z - floor.Value.Z) > 160 ||
                    accept != null && !accept(candidate.Value) ||
                    !ConnectedBothWays(nav, zone, floor.Value, candidate.Value) || !HasLocalExit(nav, zone, candidate.Value))
                    continue;
                point = candidate.Value;
                return true;
            }
        return false;
    }

    /// <summary>Pure: a meetup centre beside a service NPC lies in the clear band around it (across the ground).</summary>
    public static bool IsInClearBand(Vector3 anchor, Vector3 point)
    {
        float distance = Vector2.Distance(new(anchor.X, anchor.Y), new(point.X, point.Y));
        return distance >= AutonomousServiceNpcClearance.MeetClearRadius &&
               distance <= AutonomousServiceNpcClearance.MeetOuterRadius;
    }

    public static bool HasLocalExit(IPathfindingMgr nav, Zone zone, Vector3 center)
    {
        foreach (Vector2 offset in ExitOffsets)
        {
            Vector3 target = center + new Vector3(offset, 0);
            if (target.X < zone.XOffset || target.X >= zone.XOffset + zone.Width ||
                target.Y < zone.YOffset || target.Y >= zone.YOffset + zone.Height) continue;
            Vector3? exit = nav.GetClosestPoint(zone, target, 64, 64, 256, nav.DefaultFilters);
            if (exit.HasValue && ConnectedBothWays(nav, zone, center, exit.Value)) return true;
        }
        return false;
    }

    private static bool ConnectedBothWays(IPathfindingMgr nav, Zone zone, Vector3 a, Vector3 b) =>
        AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, a, b) &&
        AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, b, a);
}
