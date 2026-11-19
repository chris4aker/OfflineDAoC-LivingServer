using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The way a realm's Darkness Falls raid walked (1.2, DF raid wave): where its lead raider stood, in order, from the
/// realm's entrance to the raid's front, with the loops taken out. A late member (back from a release, a replacement, the
/// Special Forces) that has no certified chain to its post walks this trail instead
/// (AutonomousWorldBotController.FollowDarknessFallsRaidChain).
/// <para>Owner's PC, build e3b8238, 2026-11-14: the certified chains are built from the ordinary grind spawns, and the
/// High Lords' and Legion's floor has none (no proof within 8,000 of Legion's muster, none on High Lord Baln's floor), so a
/// late member's chain search failed and it pathed straight from the entrance floors: 391 route recoveries in Darkness
/// Falls (NoPathFound, "selecting a new goal") at the realm entrances (Midgard 18,798,18,667,22,895) and on the
/// Hibernian upper corridor (29,000-37,700 by 35,398-38,000, Z 19,824). The Hibernian raid mustered 0-3 of 88-106 for
/// Legion for 100 minutes with 120-135 raiders inside. The lead raider's walk is ground the raid has just crossed, so
/// its legs are short native paths (one-way drops included, forward only).</para>
/// Autonomous raids only; solo gamebots in Darkness Falls never see a trail.
/// </summary>
public sealed class AutonomousDarknessFallsRaidTrail
{
    /// <summary>A new point is kept once the lead raider stands this far from the last one.</summary>
    public const float StepDistance = 400f;
    /// <summary>Coming back within this of an earlier point (on the same floor) cuts the loop out.</summary>
    public const float LoopRadius = 300f;
    public const float LoopMaxHeight = 120f;
    /// <summary>The lead raider is the living raider nearest the front, within this of it.</summary>
    public const float LeaderRadius = 1_500f;
    public const long SampleMilliseconds = 3_000;
    public const int MaximumPoints = 1_024;

    private readonly List<Vector3> _points = new();
    private long _nextSample;

    public IReadOnlyList<Vector3> Points => _points;

    /// <summary>
    /// Adds where the lead raider stands (pure): nothing while it is within <see cref="StepDistance"/> of the last point; a
    /// point within <see cref="LoopRadius"/> (and <see cref="LoopMaxHeight"/>) of an earlier one cuts the trail back to
    /// that one, so the trail is the raid's way from the entrance without its detours. True when the trail changed.
    /// </summary>
    public bool Record(Vector3 at)
    {
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z)) return false;
        if (_points.Count > 0 && Vector3.Distance(_points[^1], at) < StepDistance) return false;
        for (int i = 0; i < _points.Count - 1; i++)
        {
            Vector3 p = _points[i];
            if (Vector2.Distance(new(p.X, p.Y), new(at.X, at.Y)) <= LoopRadius && Math.Abs(p.Z - at.Z) <= LoopMaxHeight)
            {
                _points.RemoveRange(i + 1, _points.Count - i - 1);
                return true;
            }
        }
        if (_points.Count >= MaximumPoints) return false;
        _points.Add(at);
        return true;
    }

    /// <summary>The trail, then the post: the chain a late member walks (null with fewer than two trail points).</summary>
    public Vector3[] ChainTo(Vector3 post) => _points.Count < 2 ? null : _points.Append(post).ToArray();

    /// <summary>The lead raider's position (pure): the living one nearest the front within <see cref="LeaderRadius"/>.</summary>
    public static Vector3? Leader(IEnumerable<Vector3> livingInside, Vector3 front)
    {
        Vector3? best = null;
        float bestDistance = LeaderRadius;
        foreach (Vector3 at in livingInside ?? [])
        {
            float d = Vector3.Distance(at, front);
            if (d <= bestDistance) { best = at; bestDistance = d; }
        }
        return best;
    }

    // ---- one trail per realm's running Darkness Falls raid -----------------------------------------------------------

    private static readonly ConcurrentDictionary<eRealm, (object Owner, AutonomousDarknessFallsRaidTrail Trail)> Trails = new();

    /// <summary>The raid route's turn: samples its lead raider every <see cref="SampleMilliseconds"/>. A new route (a new
    /// raid) starts a new trail.</summary>
    public static void Observe(object route, eRealm realm, IEnumerable<GameLiving> participants, Vector3 front, long now)
    {
        if (route == null || realm == eRealm.None) return;
        var entry = Trails.AddOrUpdate(realm, _ => (route, new AutonomousDarknessFallsRaidTrail()),
            (_, old) => ReferenceEquals(old.Owner, route) ? old : (route, new AutonomousDarknessFallsRaidTrail()));
        AutonomousDarknessFallsRaidTrail trail = entry.Trail;
        lock (trail._points)
        {
            if (now < trail._nextSample) return;
            trail._nextSample = now + SampleMilliseconds;
            Vector3? lead = Leader((participants ?? []).Where(p => p != null && p.IsAlive &&
                    p.CurrentRegionID == AutonomousDarknessFallsPolicy.RegionId).Select(p => new Vector3(p.X, p.Y, p.Z)), front);
            if (lead is Vector3 at) trail.Record(at);
        }
    }

    /// <summary>The realm's raid trail to <paramref name="post"/>, or false (no raid, or too short a trail).</summary>
    public static bool TryChainTo(eRealm realm, Vector3 post, out Vector3[] chain)
    {
        chain = null;
        if (!Trails.TryGetValue(realm, out var entry)) return false;
        lock (entry.Trail._points) chain = entry.Trail.ChainTo(post);
        return chain != null;
    }

    /// <summary>The realm's raid trail so far (a copy), or false with fewer than two points.</summary>
    public static bool TryPoints(eRealm realm, out Vector3[] points)
    {
        points = null;
        if (!Trails.TryGetValue(realm, out var entry)) return false;
        lock (entry.Trail._points) points = entry.Trail._points.Count < 2 ? null : entry.Trail._points.ToArray();
        return points != null;
    }

    /// <summary>The route ended: its trail goes.</summary>
    public static void Forget(object route, eRealm realm)
    {
        if (Trails.TryGetValue(realm, out var entry) && ReferenceEquals(entry.Owner, route)) Trails.TryRemove(realm, out _);
    }

    /// <summary>Tests.</summary>
    public static void ForgetAll() => Trails.Clear();
}

/// <summary>Darkness Falls raid route rules (1.2, DF raid wave), pure.</summary>
public static class AutonomousDarknessFallsRaidRoute
{
    /// <summary>After the final's muster began, an objective out of combat further than this (by the route's walk estimate)
    /// from the front is left.</summary>
    public const float LateObjectiveReach = 6_000f;

    /// <summary>
    /// Whether the raid leaves this objective for its final (pure): only once the final's muster has begun, never one that
    /// fights the raid (it is answered) and never one near the front. Darkness Falls' objectives are no keys to Legion (only
    /// Beliathan waits for the princes), and each walk from Legion's room up to a prince's took the raid 20-40 minutes.
    /// </summary>
    public static bool LeavesForFinal(bool finalMusterBegun, bool inCombat, float walkFromFront) =>
        finalMusterBegun && !inCombat && walkFromFront > LateObjectiveReach;
}
