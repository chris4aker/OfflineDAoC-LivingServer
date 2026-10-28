using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

// Route give-up fixes ported from stefanrows/OfflineDAoC (0.314-0.328, docs/NAVMESH_HOTSPOTS.md): most give-ups
// there were bad goals (off the mesh, or behind a gate the bot may not pass), not bad meshes.

/// <summary>
/// A goal outside the server's 32/32/64 Detour pick box is moved to the nearest floor of its own zone, only after a
/// path to it failed. A goal on the mesh is never touched.
/// </summary>
public static class AutonomousGoalSnap
{
    public enum Outcome
    {
        /// <summary>Inside the normal pick box: the goal stays as it is.</summary>
        OnMesh,
        /// <summary>No floor within the widest box (or no navmesh).</summary>
        NoFloor,
        /// <summary>The goal moved to the nearest floor of its own zone.</summary>
        Snapped,
        /// <summary>The floor lies far above or below a nearby goal and the bot cannot walk there.</summary>
        WrongFloor,
    }

    /// <summary>A snap moving the goal more than this in height, near the bot, must be walkable from the bot.</summary>
    public const float UncheckedHeight = 256f;
    /// <summary>Only a goal this close (2D) to the bot gets the walk check (a bounded corridor query).</summary>
    public const float ConnectionCheckRange = 3_000f;
    /// <summary>The path query's own pick box (LocalPathfindingMgr's default half extents).</summary>
    public const float NormalHorizontal = 32f;
    public const float NormalVertical = 64f;
    /// <summary>Wider boxes, in order. Never larger: 512/512/2048 crashed the Detour host in stefanrows' probe.</summary>
    public static readonly (float Horizontal, float Vertical)[] WideBoxes = [(96f, 512f), (256f, 1024f)];

    public static bool NeedsConnectionCheck(Vector3 goal, Vector3 snapped, Vector3 bot) =>
        MathF.Abs(snapped.Z - goal.Z) > UncheckedHeight &&
        Vector2.DistanceSquared(new(bot.X, bot.Y), new(snapped.X, snapped.Y)) <=
        ConnectionCheckRange * ConnectionCheckRange;

    /// <summary><see cref="TrySnap"/> plus the height guard; the refused floor stays in <paramref name="snapped"/> for the log.</summary>
    public static Outcome TrySnapGuarded(IPathfindingMgr nav, Zone zone, Vector3 goal, Predicate<Vector3> inOwnZone,
        Vector3 bot, Func<Vector3, bool> reachable, out Vector3 snapped)
    {
        Outcome outcome = TrySnap(nav, zone, goal, inOwnZone, out snapped);
        if (outcome != Outcome.Snapped || !NeedsConnectionCheck(goal, snapped, bot) || reachable == null ||
            reachable(snapped))
            return outcome;
        return Outcome.WrongFloor;
    }

    /// <summary>The height guard's walk check from the bot's floor; a bot in another zone (null) is not checked.</summary>
    public static bool IsReachableFrom(IPathfindingMgr nav, Zone zone, Vector3 bot, Vector3 floor)
    {
        if (zone == null)
            return true;
        Vector3? start = nav.GetClosestPoint(zone, bot, NormalHorizontal, NormalHorizontal, NormalVertical,
            nav.DefaultFilters);
        return !start.HasValue || AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start.Value, floor);
    }

    public static bool IsOnMesh(IPathfindingMgr nav, Zone zone, Vector3 point) =>
        nav.GetClosestPoint(zone, point, NormalHorizontal, NormalHorizontal, NormalVertical, nav.DefaultFilters).HasValue;

    public static Outcome TrySnap(IPathfindingMgr nav, Zone zone, Vector3 goal, Predicate<Vector3> inOwnZone,
        out Vector3 snapped)
    {
        snapped = goal;
        if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return Outcome.NoFloor;
        if (IsOnMesh(nav, zone, goal))
            return Outcome.OnMesh;
        foreach ((float horizontal, float vertical) in WideBoxes)
        {
            Vector3? floor = nav.GetClosestPoint(zone, goal, horizontal, horizontal, vertical, nav.DefaultFilters);
            if (floor is not { } p || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) ||
                inOwnZone != null && !inOwnZone(p))
                continue;
            snapped = p;
            return Outcome.Snapped;
        }
        return Outcome.NoFloor;
    }
}

/// <summary>
/// Limits on the existing floor step (a bot standing off the mesh is put on the nearest floor that leads on): an
/// idle traveller only, one step per 45 s, two per destination, never into a keep the bot is gated out of. Repeated
/// floor searches from the same off-mesh spot were one of the costly per-turn queries.
/// </summary>
public static class AutonomousStartSnap
{
    public const long CooldownMilliseconds = 45_000;
    public const int MaximumPerDestination = 2;
    /// <summary>The controller's "destination changed" distance.</summary>
    public const float SameDestination = 96f;

    public static bool MayMove(bool casting, bool attacking, bool inCombat, bool hasAggro, bool onStableRoute) =>
        !casting && !attacking && !inCombat && !hasAggro && !onStableRoute;

    /// <summary>Steps already spent on <paramref name="destination"/> (0 when the destination changed).</summary>
    public static int SnapsFor(Vector3 destination, Vector3 lastDestination, int lastCount) =>
        Vector3.DistanceSquared(destination, lastDestination) <= SameDestination * SameDestination ? lastCount : 0;

    public static bool BudgetAllows(long now, long nextAllowedTick, int snapsForDestination) =>
        now >= nextAllowedTick && snapsForDestination < MaximumPerDestination;

    /// <summary>A floor inside a keep the bot is gated out of is refused, unless the bot already stands in one.</summary>
    public static bool EntersGatedKeep(bool startGated, bool floorGated) => floorGated && !startGated;
}

/// <summary>
/// A NoPathFound well away (over 96 units) from the route's goal and continuation came from another mover (a
/// threat detour, a retreat point). When its point hangs off the mesh it is set aside once and the route is planned
/// again, instead of ending a route the bot was never asked to walk.
/// </summary>
public static class AutonomousForeignRouteFailure
{
    public const float SamePoint = 16f;
    /// <summary>AppliesToCurrentDestination's radius: a drifting formation slot or moving target belongs to the route.</summary>
    public const float RouteRadius = 96f;

    public static bool IsRoutePoint(Vector3 failed, Vector3 destination, Vector3? continuation) =>
        Vector3.DistanceSquared(destination, failed) <= SamePoint * SamePoint ||
        continuation.HasValue && Vector3.DistanceSquared(continuation.Value, failed) <= SamePoint * SamePoint;

    public static bool IsForeign(Vector3 failed, Vector3 destination, Vector3? continuation) =>
        Vector3.DistanceSquared(destination, failed) > RouteRadius * RouteRadius &&
        (!continuation.HasValue || Vector3.DistanceSquared(continuation.Value, failed) > RouteRadius * RouteRadius);

    public static bool SetAside(bool noPathFound, bool foreign, bool failedOffMesh, bool alreadySetAside) =>
        noPathFound && foreign && failedOffMesh && !alreadySetAside;
}

/// <summary>
/// After a route give-up the bot (and its warband) leaves that spot alone for 6 minutes: pickers skip it, so a
/// failed goal is not re-picked every few seconds. A give-up writes one entry; nothing is recorded per tick.
/// </summary>
public static class AutonomousFailedGoalMemory
{
    /// <summary>About one warband roam cycle; a door, siege or blocked route has changed by then.</summary>
    public const long CooldownMilliseconds = 6 * 60_000;
    /// <summary>Close enough that a re-pick would walk the same failed route.</summary>
    public const float Radius = 200f;
    private const int SlotsPerOwner = 4;
    private const int SoftOwnerCap = 512;

    private struct Entry
    {
        public ushort Region;
        public float X, Y;
        public long Until;
    }

    private static readonly ConcurrentDictionary<long, Entry[]> Owners = new();

    /// <summary>A warband as one owner (bit 62 keeps it apart from bot ids).</summary>
    public static long ForceKey(string forceId) =>
        string.IsNullOrEmpty(forceId) ? 0 : (1L << 62) | (uint)StringComparer.Ordinal.GetHashCode(forceId);

    public static void Note(long ownerKey, ushort region, float x, float y, long now)
    {
        if (ownerKey == 0 || region == 0)
            return;
        if (Owners.Count > SoftOwnerCap)
            Prune(now);
        Entry[] entries = Owners.GetOrAdd(ownerKey, _ => new Entry[SlotsPerOwner]);
        lock (entries)
        {
            int slot = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Until > now && entries[i].Region == region && Near(entries[i].X, entries[i].Y, x, y))
                {
                    slot = i; // the same spot again: refresh it
                    break;
                }
                if (entries[i].Until < entries[slot].Until)
                    slot = i; // else replace the oldest
            }
            entries[slot] = new Entry { Region = region, X = x, Y = y, Until = now + CooldownMilliseconds };
        }
    }

    public static bool IsBlocked(long ownerKey, ushort region, float x, float y, long now)
    {
        if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
            return false;
        lock (entries)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Until > now && entries[i].Region == region && Near(entries[i].X, entries[i].Y, x, y))
                    return true;
        }
        return false;
    }

    /// <summary>Whether the owner has any live entry: lets a picker skip its per-candidate checks.</summary>
    public static bool HasAny(long ownerKey, long now)
    {
        if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
            return false;
        lock (entries)
        {
            foreach (Entry entry in entries)
                if (entry.Until > now)
                    return true;
        }
        return false;
    }

    /// <summary>A zone point whose approach the owner just gave up on.</summary>
    public static bool IsBlockedCrossing(long ownerKey, DbZonePoint point, long now) =>
        point != null && IsBlocked(ownerKey, point.SourceRegion, point.SourceX, point.SourceY, now);

    public enum CrossingSteer
    {
        /// <summary>The first crossing was not a give-up spot (or there was none).</summary>
        Unchanged,
        /// <summary>It was; another route was taken.</summary>
        Steered,
        /// <summary>It was the only route, so it was kept: the memory never strands a bot.</summary>
        Fallback,
    }

    /// <summary>
    /// Runs <paramref name="search"/> (its argument is an extra "avoid this crossing" filter, null for none); when its
    /// crossing is a give-up spot, searches again avoiding give-up spots and keeps the first when nothing else exists.
    /// </summary>
    public static DbZonePoint SteerCrossing(Func<Func<DbZonePoint, bool>, DbZonePoint> search,
        Func<DbZonePoint, bool> isGivenUp, out CrossingSteer steer)
    {
        steer = CrossingSteer.Unchanged;
        DbZonePoint first = search(null);
        if (first == null || isGivenUp == null || !isGivenUp(first))
            return first;
        DbZonePoint other = search(isGivenUp);
        steer = other == null ? CrossingSteer.Fallback : CrossingSteer.Steered;
        return other ?? first;
    }

    private static bool Near(float ax, float ay, float bx, float by) =>
        (ax - bx) * (ax - bx) + (ay - by) * (ay - by) <= Radius * Radius;

    private static void Prune(long now)
    {
        foreach ((long key, Entry[] entries) in Owners)
        {
            bool live = false;
            lock (entries)
                foreach (Entry entry in entries)
                    live |= entry.Until > now;
            if (!live)
                Owners.TryRemove(key, out _);
        }
    }

    /// <summary>Test seam: forget everything.</summary>
    public static void Clear() => Owners.Clear();
}

/// <summary>
/// The capital relocation after repeated failures helps a bot stuck on a cut-off patch of mesh. It does not help
/// when the bot stands on connected ground and the goal is off the mesh (six bots shuttled Connacht - Tir na Nog
/// on one such goal in stefanrows' log).
/// </summary>
public static class AutonomousPocketEscapeRule
{
    public static bool SkipRelocation(bool startHasLocalExit, bool goalOffMesh) => startHasLocalExit && goalOffMesh;
}

/// <summary>One AUTONOMOUS_ROUTE_GOAL_FIX line a minute, only when something happened.</summary>
public static class AutonomousRouteGoalFixMetrics
{
    public const long ReportIntervalMilliseconds = 60_000;
    public const int MoveLinesPerWindow = 30;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousRouteGoalFixMetrics));
    private static long _snapped, _snapFailed, _snapDzRejected, _snapRefailed, _foreignSkipped, _gatedSkipped,
        _repickBlocked, _crossingSteered, _crossingFallback, _campRepickBlocked, _pocketEscapeSkipped,
        _floorStepBudget, _floorStepGated;
    private static long _nextReportTick, _moveLines, _nextMoveWindowTick;

    public static void Snapped() => Interlocked.Increment(ref _snapped);
    public static void SnapFailed() => Interlocked.Increment(ref _snapFailed);
    public static void SnapDzRejected() => Interlocked.Increment(ref _snapDzRejected);
    public static void SnapRefailed() => Interlocked.Increment(ref _snapRefailed);
    public static void ForeignFailureSkipped() => Interlocked.Increment(ref _foreignSkipped);
    public static void GatedSkipped() => Interlocked.Increment(ref _gatedSkipped);
    public static void RepickBlocked() => Interlocked.Increment(ref _repickBlocked);
    public static void CrossingSteered() => Interlocked.Increment(ref _crossingSteered);
    public static void CrossingFallback() => Interlocked.Increment(ref _crossingFallback);
    public static void CampRepickBlocked() => Interlocked.Increment(ref _campRepickBlocked);
    public static void PocketEscapeSkipped() => Interlocked.Increment(ref _pocketEscapeSkipped);
    /// <summary>A floor step was skipped by its budget, its miss memo or because the bot was busy.</summary>
    public static void FloorStepHeld() => Interlocked.Increment(ref _floorStepBudget);
    public static void FloorStepGated() => Interlocked.Increment(ref _floorStepGated);

    public static void LogSnap(GameBot bot, Vector3 from, Vector3 to) => LogMove("AUTONOMOUS_GOAL_SNAP", bot, from, to);

    public static void LogSnapRejected(GameBot bot, Vector3 from, Vector3 to) =>
        LogMove("AUTONOMOUS_GOAL_SNAP_REJECTED", bot, from, to);

    private static void LogMove(string marker, GameBot bot, Vector3 from, Vector3 to)
    {
        long now = GameLoop.GameLoopTime;
        long window = Interlocked.Read(ref _nextMoveWindowTick);
        if (now >= window && Interlocked.CompareExchange(ref _nextMoveWindowTick, now + ReportIntervalMilliseconds, window) == window)
            Interlocked.Exchange(ref _moveLines, 0);
        if (Interlocked.Increment(ref _moveLines) > MoveLinesPerWindow)
            return;
        Log.Info($"{marker} bot={bot.Name} id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                 $"from={from.X:0},{from.Y:0},{from.Z:0} to={to.X:0},{to.Y:0},{to.Z:0} " +
                 $"moved={Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y)):0} dz={to.Z - from.Z:0}");
    }

    public static void ReportIfDue(long now)
    {
        long next = Interlocked.Read(ref _nextReportTick);
        if (next == 0)
        {
            Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, 0);
            return;
        }
        if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, next) != next)
            return;
        long snapped = Interlocked.Exchange(ref _snapped, 0), snapFailed = Interlocked.Exchange(ref _snapFailed, 0),
             dz = Interlocked.Exchange(ref _snapDzRejected, 0), refailed = Interlocked.Exchange(ref _snapRefailed, 0),
             foreign = Interlocked.Exchange(ref _foreignSkipped, 0), gated = Interlocked.Exchange(ref _gatedSkipped, 0),
             repick = Interlocked.Exchange(ref _repickBlocked, 0), steered = Interlocked.Exchange(ref _crossingSteered, 0),
             fallback = Interlocked.Exchange(ref _crossingFallback, 0), camp = Interlocked.Exchange(ref _campRepickBlocked, 0),
             pocket = Interlocked.Exchange(ref _pocketEscapeSkipped, 0), held = Interlocked.Exchange(ref _floorStepBudget, 0),
             floorGated = Interlocked.Exchange(ref _floorStepGated, 0);
        if (snapped + snapFailed + dz + refailed + foreign + gated + repick + steered + fallback + camp + pocket + held +
            floorGated == 0)
            return;
        Log.Info($"AUTONOMOUS_ROUTE_GOAL_FIX window_s={ReportIntervalMilliseconds / 1000} snapped={snapped} " +
                 $"snapFailed={snapFailed} snapDzRejected={dz} snapRefailed={refailed} foreignFailureSkipped={foreign} " +
                 $"gatedSkipped={gated} repickBlocked={repick} crossingSteered={steered} crossingFallback={fallback} " +
                 $"campRepickBlocked={camp} pocketEscapeSkipped={pocket} floorStepHeld={held} floorStepGated={floorGated}");
    }
}
