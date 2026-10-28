using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace DOL.GS;

/// <summary>
/// Long-walk fallback for one zone: a graph of short legs that the real Detour corridor check
/// proved (navmesh/roads.json, built by tools/navmesh_splice/roadgraph.py + UT_ZoneRoadBuild).
/// Detour's own search stops at a dead end when the only way to a far point first leads away from
/// it: the Sheeroe Hills plateau reaches its Bog of Cullen crossing only through the western valley
/// (104,000 units for a 54,000-unit chord), so 1,638 Hibernian camp trips failed with "no safe
/// connected zone seam" in one night (2026-10-12) and bots stalled at the same dead end.
/// Used only after a direct corridor check failed, for points at least MinimumDistance apart:
/// short routes and every zone without data behave exactly as before.
/// </summary>
public static class AutonomousZoneRoads
{
    public const float MinimumDistance = 1_024;
    // Proven legs from the actor onto the graph and off it to the goal (nearest anchors first).
    public const int EndpointChecks = 4;
    public const long RouteMemoryMilliseconds = 300_000;
    public const int RouteMemoryCapacity = 20_000;

    private sealed class ZoneRoads
    {
        public int Cell;
        public Vector3[] Anchors;
        public int[] Areas;
        public int[][] Next;
        /// <summary>Walked length of each leg in <see cref="Next"/> (the Detour corridor), not the chord: two anchors
        /// across a ravine can be 3,800 apart and 37,000 to walk (Sheeroe Hills, 2026-10-13).</summary>
        public float[][] Cost;
        public Dictionary<long, List<int>> Cells;
    }

    private static readonly Lazy<Dictionary<ushort, ZoneRoads>> Data = new(Load);
    private static readonly ConcurrentDictionary<(ushort, int, int, int, int, int, int, long), (Vector3[] Via, long Until)> Routes = new();

    public static string FilePath => File.Exists(Path.Combine(AppContext.BaseDirectory, "navmesh", "roads.json"))
        ? Path.Combine(AppContext.BaseDirectory, "navmesh", "roads.json")
        : Path.Combine(Environment.CurrentDirectory, "navmesh", "roads.json");

    public static bool Covers(Zone zone) => zone != null && Data.Value.ContainsKey(zone.ID);

    public static int ZoneCount => Data.Value.Count;
    public static int RememberedRoutes => Routes.Count + EndpointMemory.Count;

    private static long _solves, _found, _solveTicks;

    /// <summary>Road searches since the last call: "searches/found/ms" (SERVER_MEMORY), then starts counting again.</summary>
    public static string TakeStats()
    {
        long solves = System.Threading.Interlocked.Exchange(ref _solves, 0);
        long found = System.Threading.Interlocked.Exchange(ref _found, 0);
        long ticks = System.Threading.Interlocked.Exchange(ref _solveTicks, 0);
        return $"{solves}/{found}/{ticks * 1000 / System.Diagnostics.Stopwatch.Frequency}ms";
    }

    /// <summary>Reads navmesh/roads.json now (about half a second), off the game loop, instead of on the first bot turn that needs it.</summary>
    public static void Preload() => System.Threading.ThreadPool.QueueUserWorkItem(_ => _ = Data.Value.Count);

    /// <summary>
    /// The road anchors to walk through from start to end inside one zone (each consecutive leg,
    /// including start to the first anchor and the last anchor to end, is a proven corridor).
    /// False when the zone has no road data, the points are close, or no proven route joins them.
    /// </summary>
    public static bool TryRoute(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end, out Vector3[] via)
    {
        via = null;
        if (nav == null || zone == null || Vector3.DistanceSquared(start, end) < MinimumDistance * MinimumDistance ||
            !Data.Value.TryGetValue(zone.ID, out ZoneRoads roads))
            return false;
        long now = GameLoop.GameLoopTime;
        var key = (zone.ID, (int)start.X >> 7, (int)start.Y >> 7, (int)start.Z >> 7, (int)end.X >> 7, (int)end.Y >> 7,
            (int)end.Z >> 7, NavigationGeometryRevision.Read(zone));
        if (Routes.TryGetValue(key, out var known) && now < known.Until)
        {
            via = known.Via;
            return via != null;
        }
        long profiled = NavQueryProfile.Start();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { via = Solve(nav, zone, roads, start, end); }
        finally
        {
            NavQueryProfile.Stop(NavQueryProfile.Kind.Road, profiled);
            System.Threading.Interlocked.Add(ref _solveTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
            System.Threading.Interlocked.Increment(ref _solves);
            if (via != null) System.Threading.Interlocked.Increment(ref _found);
        }
        if (Routes.Count > RouteMemoryCapacity) Routes.Clear();
        Routes[key] = (via, now + RouteMemoryMilliseconds);
        return via != null;
    }

    private static Vector3[] Solve(IPathfindingMgr nav, Zone zone, ZoneRoads roads, Vector3 start, Vector3 end)
    {
        List<int> near = Nearby(roads, start), far = Nearby(roads, end);
        // Different connected ground at both ends: no corridor queries at all.
        var endAreas = new HashSet<int>();
        foreach (int anchor in far) endAreas.Add(roads.Areas[anchor]);
        near.RemoveAll(anchor => !endAreas.Contains(roads.Areas[anchor]));
        if (near.Count == 0) return null;
        var startAreas = new HashSet<int>();
        foreach (int anchor in near) startAreas.Add(roads.Areas[anchor]);
        far.RemoveAll(anchor => !startAreas.Contains(roads.Areas[anchor]));

        var entries = new List<int>();
        foreach (int area in startAreas)
            if (endAreas.Contains(area)) entries.AddRange(Endpoints(nav, zone, roads, start, near, area, true));
        // An entry reached only back through a door, and no nearer the goal than the start, is the way the bot just
        // came out: run 24 (2026-10-18), 47 Hibernian bots stepped out of their portal keep in Odin's Gate through
        // door 115000902, the route led back in to the yard's road anchor 143, and the yard led out again (292 give-ups).
        float startToEnd = Vector3.DistanceSquared(start, end);
        entries.RemoveAll(entry => Vector3.DistanceSquared(roads.Anchors[entry], end) >= startToEnd &&
            CrossesDoor(nav, zone, start, roads.Anchors[entry]));
        if (entries.Count == 0) return null;
        var exits = new HashSet<int>();
        foreach (int area in startAreas)
            if (endAreas.Contains(area)) exits.UnionWith(Endpoints(nav, zone, roads, end, far, area, false));
        if (exits.Count == 0) return null;

        // A* over the proven legs from either entry to either exit.
        int count = roads.Anchors.Length;
        var cost = new float[count];
        var previous = new int[count];
        Array.Fill(cost, float.MaxValue);
        Array.Fill(previous, -1);
        var open = new PriorityQueue<int, float>();
        foreach (int entry in entries)
        {
            cost[entry] = Vector3.Distance(start, roads.Anchors[entry]);
            open.Enqueue(entry, cost[entry] + Vector3.Distance(roads.Anchors[entry], end));
        }
        int reached = -1;
        while (open.TryDequeue(out int current, out float priority))
        {
            if (priority - Vector3.Distance(roads.Anchors[current], end) > cost[current] + 0.5f) continue;
            if (exits.Contains(current)) { reached = current; break; }
            int[] legs = roads.Next[current];
            for (int leg = 0; leg < legs.Length; leg++)
            {
                int next = legs[leg];
                float candidate = cost[current] + roads.Cost[current][leg];
                if (candidate >= cost[next]) continue;
                cost[next] = candidate;
                previous[next] = current;
                open.Enqueue(next, candidate + Vector3.Distance(roads.Anchors[next], end));
            }
        }
        if (reached < 0) return null;
        var path = new List<Vector3>();
        for (int anchor = reached; anchor >= 0; anchor = previous[anchor]) path.Add(roads.Anchors[anchor]);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>The Detour path between the two points passes a door polygon.</summary>
    public static bool CrossesDoor(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to)
    {
        WrappedPathfindingNode[] nodes = System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Rent(512);
        try
        {
            PathfindingResult path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            for (int i = 0; i < Math.Min(path.NodeCount, 512); i++)
                if ((nodes[i].Flags & EDtPolyFlags.AnyDoor) != 0) return true;
            return false;
        }
        finally { System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes); }
    }

    private static readonly ConcurrentDictionary<(ushort, int, int, int, int, bool, long), (int[] Anchors, long Until)> EndpointMemory = new();

    /// <summary>
    /// Up to two anchors of one area among the point's nearest that a proven corridor joins to the point
    /// (point to anchor when entering, anchor to point when leaving). Remembered per 32-unit spot: many
    /// bots plan toward the same border crossing, and proving an unreachable anchor is the costliest query.
    /// </summary>
    private static int[] Endpoints(IPathfindingMgr nav, Zone zone, ZoneRoads roads, Vector3 point, List<int> nearby, int area, bool entering)
    {
        long now = GameLoop.GameLoopTime;
        var key = (zone.ID, (int)point.X >> 5, (int)point.Y >> 5, (int)point.Z >> 5, area, entering, NavigationGeometryRevision.Read(zone));
        if (EndpointMemory.TryGetValue(key, out var known) && now < known.Until) return known.Anchors;
        var proven = new List<int>(2);
        int tested = 0;
        foreach (int anchor in nearby)
        {
            if (roads.Areas[anchor] != area) continue;
            if (proven.Count == 2 || tested++ >= EndpointChecks) break;
            if (entering ? AutonomousZoneItinerary.HasDirectCorridor(nav, zone, point, roads.Anchors[anchor])
                         : AutonomousZoneItinerary.HasDirectCorridor(nav, zone, roads.Anchors[anchor], point))
                proven.Add(anchor);
        }
        int[] result = proven.ToArray();
        if (EndpointMemory.Count > RouteMemoryCapacity) EndpointMemory.Clear();
        EndpointMemory[key] = (result, now + RouteMemoryMilliseconds);
        return result;
    }

    /// <summary>
    /// The walkable area id (same ids as navmesh/seams.json) of the road anchor nearest the point, or
    /// null without road data or anchors nearby. An estimate: callers still prove every leg.
    /// </summary>
    public static int? AreaAt(Zone zone, Vector3 point)
    {
        if (zone == null || !Data.Value.TryGetValue(zone.ID, out ZoneRoads roads)) return null;
        List<int> near = Nearby(roads, point);
        return near.Count == 0 ? null : roads.Areas[near[0]];
    }

    /// <summary>Anchors in the point's cell and the eight around it, nearest first (height counts double).</summary>
    private static List<int> Nearby(ZoneRoads roads, Vector3 point)
    {
        var result = new List<int>();
        int cx = (int)MathF.Floor(point.X / roads.Cell), cy = (int)MathF.Floor(point.Y / roads.Cell);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (roads.Cells.TryGetValue(CellKey(cx + dx, cy + dy), out List<int> anchors))
                    result.AddRange(anchors);
        result.Sort((a, b) => Score(roads.Anchors[a], point).CompareTo(Score(roads.Anchors[b], point)));
        return result;

        static float Score(Vector3 anchor, Vector3 point)
        {
            float dz = (anchor.Z - point.Z) * 2;
            return Vector2.DistanceSquared(new(anchor.X, anchor.Y), new(point.X, point.Y)) + dz * dz;
        }
    }

    private static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;

    private static Dictionary<ushort, ZoneRoads> Load()
    {
        var result = new Dictionary<ushort, ZoneRoads>();
        try
        {
            if (!File.Exists(FilePath)) return result;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath));
            int cell = document.RootElement.GetProperty("cell").GetInt32();
            foreach (JsonProperty zone in document.RootElement.GetProperty("zones").EnumerateObject())
            {
                var anchors = new List<Vector3>();
                var areas = new List<int>();
                foreach (JsonElement anchor in zone.Value.GetProperty("anchors").EnumerateArray())
                {
                    anchors.Add(new(anchor[0].GetSingle(), anchor[1].GetSingle(), anchor[2].GetSingle()));
                    areas.Add(anchor[3].GetInt32());
                }
                var next = new List<int>[anchors.Count];
                var costs = new List<float>[anchors.Count];
                for (int i = 0; i < next.Length; i++) { next[i] = new(); costs[i] = new(); }
                foreach (JsonElement edge in zone.Value.GetProperty("edges").EnumerateArray())
                {
                    int from = edge[0].GetInt32(), to = edge[1].GetInt32();
                    float chord = Vector3.Distance(anchors[from], anchors[to]);
                    // [from, to, walked length]; never below the chord, so the A* estimate stays a lower bound.
                    next[from].Add(to);
                    costs[from].Add(edge.GetArrayLength() > 2 ? Math.Max(chord, edge[2].GetSingle()) : chord);
                }
                var roads = new ZoneRoads
                {
                    Cell = cell,
                    Anchors = anchors.ToArray(),
                    Areas = areas.ToArray(),
                    Next = Array.ConvertAll(next, list => list.ToArray()),
                    Cost = Array.ConvertAll(costs, list => list.ToArray()),
                    Cells = new()
                };
                for (int i = 0; i < roads.Anchors.Length; i++)
                {
                    long key = CellKey((int)MathF.Floor(roads.Anchors[i].X / cell), (int)MathF.Floor(roads.Anchors[i].Y / cell));
                    if (!roads.Cells.TryGetValue(key, out List<int> list)) roads.Cells[key] = list = new();
                    list.Add(i);
                }
                result[ushort.Parse(zone.Name)] = roads;
            }
        }
        catch (Exception exception)
        {
            DOL.Logging.LoggerManager.Create(typeof(AutonomousZoneRoads)).Warn("navmesh/roads.json could not be read; long walks use Detour only", exception);
            result.Clear();
        }
        return result;
    }
}
