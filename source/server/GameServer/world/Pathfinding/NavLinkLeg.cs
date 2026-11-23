using System;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// 1.2.2 (#438 generalized): a path leg that crosses an off-mesh link (a ladder, a tower or wall climb, a ledge drop,
    /// a gap or a lift) has no polygon in its middle within the server's pick box (game 32, 32, 64). tools/navmesh/navscan
    /// found 1,262 such link directions in 39 of the 103 shipped navmeshes (docs/NAVMESH-DROPS.md). A bot that replots from
    /// inside one gets NoPathFound, and a companion with a formation order then stands there for good, as Darkness Falls'
    /// entrance stairs showed (issue #438). These are the pure rules the mover uses outside Darkness Falls, whose
    /// certified stair falls (AutonomousDarknessFallsNavigation) stay as they are.
    /// </summary>
    public static class NavLinkLeg
    {
        /// <summary>The server's pick box half height (LocalPathfindingMgr._defaultHalfExtents, game Z).</summary>
        public const float PickHalfHeight = 64;
        /// <summary>The longest leg treated as a link (the longest stuck-prone link in the scan runs 316, a Galladoria lift).</summary>
        public const float MaxRun = 320;
        /// <summary>How far (XY) from a leg a bot still counts as being on it.</summary>
        public const float LegWidth = 40;

        /// <summary>Darkness Falls (zone and region 249) keeps its certified stair falls and its own landing rule.</summary>
        public static bool AppliesIn(ushort zoneId) => zoneId != AutonomousDarknessFallsPolicy.RegionId;

        /// <summary>A leg no walkable surface can make: it rises or drops at least the pick box's half height and at least
        /// as far as it runs (steeper than 45 degrees), over a short run. A bot walking one does not replot until it
        /// reaches the far end; a new order only changes where it goes from there.</summary>
        public static bool IsCommitLeg(Vector3 from, Vector3 to)
        {
            if (!Finite(from) || !Finite(to)) return false;
            float rise = Math.Abs(to.Z - from.Z);
            float run = Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y));
            return run <= MaxRun && rise >= PickHalfHeight && rise >= run;
        }

        /// <summary>A bot at <paramref name="actor"/> is inside the leg <paramref name="from"/> to <paramref name="to"/>:
        /// within <see cref="LegWidth"/> (XY) of the segment, between its two heights (a little slack), and not already at
        /// the far end. Only short legs (run up to <see cref="MaxRun"/>) count, so the set-down never carries a bot far.</summary>
        public static bool IsOnLeg(Vector3 actor, Vector3 from, Vector3 to)
        {
            if (!Finite(actor) || !Finite(from) || !Finite(to)) return false;
            Vector2 a = new(from.X, from.Y), b = new(to.X, to.Y), p = new(actor.X, actor.Y);
            Vector2 ab = b - a;
            if (ab.Length() > MaxRun) return false;
            if (Vector3.Distance(actor, to) <= 8) return false;
            float low = Math.Min(from.Z, to.Z) - 32, high = Math.Max(from.Z, to.Z) + 32;
            if (actor.Z < low || actor.Z > high) return false;
            float t = ab.LengthSquared() < 1e-3f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(p, a + ab * t) <= LegWidth;
        }

        /// <summary>Pure: where a bot whose path failed (NoPathFound) is set down. Only a bot inside its last leg
        /// (<see cref="IsOnLeg"/>), on no polygon within the pick box (<paramref name="floorHere"/> null: the #438 stuck
        /// condition), whose leg end has a floor close by (<paramref name="floorAtEnd"/>, within 32): that floor.</summary>
        public static bool TryResolveLanding(Vector3 actor, Vector3 from, Vector3 to, Vector3? floorHere, Vector3? floorAtEnd,
            out Vector3 landing)
        {
            landing = default;
            if (floorHere.HasValue || !floorAtEnd.HasValue || !IsOnLeg(actor, from, to) ||
                Vector3.Distance(floorAtEnd.Value, to) > 32) return false;
            landing = floorAtEnd.Value;
            return true;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
