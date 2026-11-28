using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// What a bot actually walks on the installed meshes, for the installed-route probes (#704). The September probes asked
    /// one raw Detour query from a raw capture; a bot instead plans from the floor its floor step puts it on, walks a
    /// same-zone trip by the direct corridor or, when that stops short, by the zone road route (Pathfinder.TryRoadPath),
    /// and crosses zones by the itinerary's seam steps (AutonomousZoneItinerary.TryNextStep). Every leg of such a route is
    /// asserted to be a complete corridor; nothing here is looser than what the mover itself requires.
    /// </summary>
    internal static class InstalledBotRoute
    {
        internal enum Walk { None, Direct, Road }

        /// <summary>
        /// How a bot walks from <paramref name="start"/> to <paramref name="end"/> inside one zone: Direct when the Detour
        /// corridor alone is complete, Road when the zone road route joins them and each of its legs (start to the first
        /// anchor, anchor to anchor, the last anchor to the end) is a complete direct corridor, None otherwise.
        /// </summary>
        internal static Walk SameZone(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end, out string detail) =>
            SameZone(nav, zone, start, end, out detail, out _);

        /// <param name="legEnds">The end of each leg in order (the road anchors, then <paramref name="end"/>); one entry for a
        /// direct walk, empty for none.</param>
        internal static Walk SameZone(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end, out string detail, out Vector3[] legEnds)
        {
            legEnds = [];
            if (AutonomousZoneItinerary.HasDirectCorridor(nav, zone, start, end))
            {
                detail = "direct";
                legEnds = [end];
                return Walk.Direct;
            }
            if (!AutonomousZoneRoads.TryRoute(nav, zone, start, end, out Vector3[] via) || via == null)
            {
                detail = "no direct corridor and no road route";
                return Walk.None;
            }
            Vector3 from = start;
            for (int i = 0; i <= via.Length; i++)
            {
                Vector3 to = i < via.Length ? via[i] : end;
                if (!AutonomousZoneItinerary.HasDirectCorridor(nav, zone, from, to))
                {
                    detail = $"road route of {via.Length} anchors, leg {i} {from} -> {to} has no complete corridor";
                    return Walk.None;
                }
                from = to;
            }
            detail = $"road route, {via.Length + 1} complete legs";
            legEnds = via.Append(end).ToArray();
            return Walk.Road;
        }

        /// <summary>
        /// The bot's trip from <paramref name="start"/> to <paramref name="goal"/> in one region: the itinerary's seam steps
        /// (each walk to a seam a <see cref="SameZone"/> walk), then the last walk in the goal zone. False, with the stop in
        /// <paramref name="trace"/>, when a step is missing, a leg is not complete, or the trip enters a zone twice.
        /// </summary>
        internal static bool Itinerary(Region region, IPathfindingMgr nav, Vector3 start, Vector3 goal, out string trace,
            Func<Zone, bool> allowed = null)
        {
            var log = new List<string>();
            Zone from = region.GetZone((int)start.X, (int)start.Y);
            Zone to = region.GetZone((int)goal.X, (int)goal.Y);
            if (from == null || to == null)
            {
                trace = $"start {start} or goal {goal} outside every zone";
                return false;
            }
            var visited = new HashSet<ushort> { from.ID };
            Vector3 cursor = start;
            while (from != to)
            {
                if (!AutonomousZoneItinerary.TryNextStep(region, from, to, cursor, goal, nav, out var step, allowed))
                {
                    log.Add($"no seam step from zone {from.ID} at {cursor} toward zone {to.ID}");
                    trace = string.Join("; ", log);
                    return false;
                }
                Walk walk = SameZone(nav, from, cursor, step.Inside, out string detail);
                log.Add($"zone {from.ID} {cursor} -> seam {step.Inside} ({detail})");
                if (walk == Walk.None)
                {
                    trace = string.Join("; ", log);
                    return false;
                }
                cursor = step.Outside;
                from = region.GetZone((int)cursor.X, (int)cursor.Y);
                if (from == null || !visited.Add(from.ID))
                {
                    log.Add($"the step to {cursor} enters zone {from?.ID} again: the trip turns back");
                    trace = string.Join("; ", log);
                    return false;
                }
            }
            Walk last = SameZone(nav, to, cursor, goal, out string end);
            log.Add($"zone {to.ID} {cursor} -> goal {goal} ({end})");
            trace = string.Join("; ", log);
            return last != Walk.None;
        }

        /// <summary>Where a bot at <paramref name="capture"/> plans from (the floor step; see UT_SeptemberThirteenInstalledRoutes.PlanningStart).</summary>
        internal static Vector3 Start(IPathfindingMgr nav, Zone zone, Vector3 capture) =>
            UT_SeptemberThirteenInstalledRoutes.PlanningStart(nav, zone, capture);

        /// <summary>The ground (road/seam area) a point stands on, and its zone's main ground, for the side-ground pins.</summary>
        internal static (int? Area, int? Main) Ground(ushort regionId, Zone zone, Vector3 point) =>
            (AutonomousZoneRoads.AreaAt(zone, point), AutonomousZoneSeams.MainArea(regionId, zone.ID));

        internal static string Describe(IEnumerable<Vector3> points) => string.Join(" ", points.Select(p => $"({p.X:0},{p.Y:0},{p.Z:0})"));
    }
}
