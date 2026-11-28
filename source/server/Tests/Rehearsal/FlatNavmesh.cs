using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// The rehearsal's stand-in navmesh (#520): one flat, open plane at Z 0 under every zone of the world, with no walls,
    /// doors or water. A path is the straight line, every point has floor beneath it and line of sight is always clear.
    /// A scenario may stand <see cref="Walls"/> on it: thin, infinitely tall walls that block line of sight (and nothing
    /// else; paths still go straight through). It exists because an autonomous gamebot never moves without a navmesh (NpcMovementComponent pauses a persistent
    /// bot's path when the zone has none) and the frontier scan needs one; the real zone meshes are not in CI. Random
    /// points come from the world's seed.
    /// </summary>
    public sealed class FlatNavmesh : IPathfindingMgr
    {
        private readonly Random _random;

        public FlatNavmesh(int seed) => _random = new Random(seed * 31 + 5);

        /// <summary>A wall from (X1, Y1) to (X2, Y2) that blocks line of sight (#529: the "behind a wall" case).</summary>
        public readonly record struct Wall(float X1, float Y1, float X2, float Y2);

        /// <summary>The walls standing on the plane; none by default, so line of sight is clear everywhere.</summary>
        public List<Wall> Walls { get; } = new();

        /// <summary>Whether the 2D segment from a to b properly crosses a wall (only touching it does not). Pure.</summary>
        public static bool Crosses(Wall wall, Vector3 a, Vector3 b)
        {
            static float Cross(float ax, float ay, float bx, float by, float cx, float cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            float d1 = Cross(wall.X1, wall.Y1, wall.X2, wall.Y2, a.X, a.Y), d2 = Cross(wall.X1, wall.Y1, wall.X2, wall.Y2, b.X, b.Y);
            float d3 = Cross(a.X, a.Y, b.X, b.Y, wall.X1, wall.Y1), d4 = Cross(a.X, a.Y, b.X, b.Y, wall.X2, wall.Y2);
            return (d1 > 0) != (d2 > 0) && d1 != 0 && d2 != 0 && (d3 > 0) != (d4 > 0) && d3 != 0 && d4 != 0;
        }

        private static Vector3 Floor(Vector3 point) => new(point.X, point.Y, 0);

        public bool Init() => true;
        public void Stop() { }
        public bool RegisterDoor(GameDoorBase door) => true;
        public bool UpdateDoorFlags(GameDoorBase door) => true;

        public PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
        {
            if (destination.Length < 2)
                return new PathfindingResult(PathfindingStatus.BufferTooSmall, 0);
            destination[0] = new WrappedPathfindingNode(Floor(start), EDtPolyFlags.Walk);
            destination[1] = new WrappedPathfindingNode(Floor(end), EDtPolyFlags.Walk);
            return new PathfindingResult(PathfindingStatus.PathFound, 2);
        }

        public Vector3? GetMoveAlongSurface(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters) => Floor(end);

        public Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters)
        {
            double angle = _random.NextDouble() * Math.PI * 2, distance = Math.Sqrt(_random.NextDouble()) * radius;
            return new Vector3(position.X + (float)(Math.Cos(angle) * distance), position.Y + (float)(Math.Sin(angle) * distance), 0);
        }

        public Vector3? GetClosestPoint(Zone zone, Vector3 position, EDtPolyFlags[] filters) => Floor(position);
        public Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange, float zRange, EDtPolyFlags[] filters) => Floor(position);
        public Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters) => Floor(origin);
        public Vector3? GetRoofAbove(Zone zone, Vector3 position, float maxHeight, EDtPolyFlags[] filters) => null;
        public Vector3? GetFloorBeneath(Zone zone, Vector3 position, float maxDepth, EDtPolyFlags[] filters) => position.Z >= 0 ? Floor(position) : null;

        public bool TrySnapToMesh(Zone zone, ref Vector3 position, float range)
        {
            position = Floor(position);
            return true;
        }

        public bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters)
        {
            foreach (Wall wall in Walls)
                if (Crosses(wall, position, target))
                    return false;
            return true;
        }
        public bool HasNavmesh(Zone zone) => zone != null;
        public bool IsAvailable => true;
        public EDtPolyFlags[] DefaultFilters { get; } = [EDtPolyFlags.All ^ EDtPolyFlags.Disabled, 0];
        public EDtPolyFlags[] BlockingDoorAvoidanceFilters { get; } = [EDtPolyFlags.All ^ EDtPolyFlags.Disabled, 0];
    }
}
