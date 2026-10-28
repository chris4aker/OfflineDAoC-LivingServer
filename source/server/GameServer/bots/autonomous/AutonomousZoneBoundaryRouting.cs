using System;
using System.Numerics;

namespace DOL.GS
{
    public static class AutonomousZoneBoundaryRouting
    {
        public readonly record struct Step(Vector3 Inside, Vector3 Outside);

        public static bool TryResolveHeights(Vector3 inside, Vector3 outside,
            Func<Vector3, Vector3?> snapInside, Func<Vector3, Vector3?> snapOutside, out Step step)
        {
            step = default;
            Vector3? a = snapInside(inside);
            Vector3? b = snapOutside(outside);
            if (!a.HasValue || !b.HasValue || !IsFinite(a.Value) || !IsFinite(b.Value))
                return false;
            // A short seam crossing may not jump to another floor or bridge a
            // cliff/hole. Snap vertically to terrain, but keep XY near the seam.
            if (Vector2.Distance(new(a.Value.X, a.Value.Y), new(inside.X, inside.Y)) > 96 ||
                Vector2.Distance(new(b.Value.X, b.Value.Y), new(outside.X, outside.Y)) > 96 ||
                Vector3.Distance(a.Value, b.Value) > 240 || Math.Abs(a.Value.Z - b.Value.Z) > 128)
                return false;
            step = new(a.Value, b.Value);
            return true;
        }

        /// <summary>
        /// Whether a seam step is the one before it taken back: it crosses the border the opposite way (the two directions point
        /// against each other) at a place within the tolerance of the first. Steps are short (the heights check allows 240 units),
        /// so nearness alone would also take the same step twice for a reversal. A bot on a patch of ground that joins neither
        /// zone's main ground gets such steps for ever (Vindsaul Faste, 2026-10-28: West Svealand to Yggdra Forest and back,
        /// 130 units each way, five bots).
        /// </summary>
        public static bool IsReversal(Step previous, Step next, float tolerance = 300)
        {
            static Vector2 Plane(Vector3 point) => new(point.X, point.Y);
            Vector2 first = Plane(previous.Outside) - Plane(previous.Inside);
            Vector2 second = Plane(next.Outside) - Plane(next.Inside);
            if (first.LengthSquared() < 1 || second.LengthSquared() < 1)
                return false;
            if (Vector2.Dot(first, second) / (first.Length() * second.Length()) > -0.7f)
                return false;
            Vector2 firstMiddle = (Plane(previous.Inside) + Plane(previous.Outside)) / 2;
            Vector2 secondMiddle = (Plane(next.Inside) + Plane(next.Outside)) / 2;
            return Vector2.Distance(firstMiddle, secondMiddle) <= tolerance;
        }

        private static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    }
}
