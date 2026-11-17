using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace DOL.GS;

/// <summary>A keep as the lane choice sees it: who holds it now, and where it stands.</summary>
public readonly record struct RvrLiveKeep(int KeepId, eRealm Realm, Vector2 At);

/// <summary>
/// One walkable lane for one trip: the trunk out of a portal valley (if the trip starts at a portal keep) followed by the
/// lane from the milegate, as validated walks. <see cref="Walk"/> is what the keep walker follows; <see cref="Shape"/>
/// (the Detour corridor, every walk point on it) measures progress. <see cref="MergeLength"/> is where the lane enters
/// the siege camp ring (<see cref="RvrLaneChoice.MergeRadius"/> from the goal): the march ends there, as today, and the
/// column re-merges at the camp.
/// </summary>
public sealed record RvrLaneRoute(int Slot, int LaneIndex, ushort Region, string FromRef, string ToRef, RvrLane Lane,
    RvrLane Trunk, bool Reversed, ImmutableArray<Vector3> Walk, ImmutableArray<float> WalkProgress,
    ImmutableArray<Vector3> Shape, ImmutableArray<float> Along, float TrunkLength, float MergeLength)
{
    public float Length => Along.IsDefaultOrEmpty ? 0 : Along[^1];
    public string Tag => $"{Region}:{FromRef}>{ToRef}#{LaneIndex}";

    /// <summary>How far along this route a point is (see <see cref="RvrLaneGeometry.Project"/>).</summary>
    public float Project(Vector3 point, float hint, out float lateral) => RvrLaneGeometry.Project(Shape, Along, point, hint, out lateral);

    /// <summary>Joins a trunk walk and a lane walk into one route. Pure.</summary>
    public static RvrLaneRoute Build(int slot, RvrLane lane, RvrLaneProof proof, RvrLane trunk, RvrLaneProof trunkProof,
        bool reversed, string fromRef, string toRef, Vector2 goal)
    {
        var walk = new List<Vector3>();
        var shape = new List<Vector3>();
        if (trunk != null && trunkProof?.Usable == true)
        {
            walk.AddRange(trunkProof.Walk);
            shape.AddRange(trunkProof.Shape);
        }
        float trunkLength = RvrLaneGeometry.Length(shape);
        Append(walk, proof.Walk);
        Append(shape, proof.Shape);
        float[] along = RvrLaneGeometry.Cumulative(shape);
        var progress = new float[walk.Count];
        float hint = 0;
        for (int i = 0; i < walk.Count; i++)
        {
            hint = MathF.Max(hint, RvrLaneGeometry.Project(shape, along, walk[i], hint, out _));
            progress[i] = hint;
        }
        return new(slot, lane.Index, lane.Region, fromRef, toRef, lane, trunk, reversed, [.. walk], [.. progress], [.. shape],
            [.. along], trunkLength, MergeAt(shape, along, trunkLength, goal));

        static void Append(List<Vector3> into, ImmutableArray<Vector3> points)
        {
            foreach (Vector3 point in points)
                if (into.Count == 0 || Vector3.DistanceSquared(into[^1], point) > 1) into.Add(point);
        }
    }

    /// <summary>Where the route (past its trunk) first comes within the merge radius of the goal; its end if never.</summary>
    public static float MergeAt(IReadOnlyList<Vector3> shape, IReadOnlyList<float> along, float trunkLength, Vector2 goal)
    {
        float radius = RvrLaneChoice.MergeRadius;
        for (int i = 1; i < shape.Count; i++)
        {
            if (along[i] < trunkLength) continue;
            Vector2 a = new(shape[i - 1].X, shape[i - 1].Y), b = new(shape[i].X, shape[i].Y);
            if (Vector2.Distance(b, goal) > radius) continue;
            if (Vector2.Distance(a, goal) <= radius) return MathF.Max(trunkLength, along[i - 1]);
            float low = 0, high = 1;
            for (int step = 0; step < 24; step++)
            {
                float mid = (low + high) / 2;
                if (Vector2.Distance(Vector2.Lerp(a, b, mid), goal) <= radius) high = mid; else low = mid;
            }
            return MathF.Max(trunkLength, along[i - 1] + (along[i] - along[i - 1]) * high);
        }
        return along.Count == 0 ? 0 : along[^1];
    }
}

/// <summary>Why a march or roam took today's route instead of lanes (RVR_LANE_FALLBACK reason=).</summary>
public enum RvrLaneFallback { None, Off, NoCatalog, NoOrigin, NoTrip, Unproved, NotAllowed, KeepHeld, Hazard, Join }

/// <summary>The usable lanes for one trip, lane 0 first (at most three), or why there are none.</summary>
public sealed record RvrLaneOptions(ImmutableArray<RvrLaneRoute> Routes, RvrLaneFallback Fallback, string Detail,
    string FromRef, string ToRef)
{
    public bool UsesLanes => !Routes.IsDefaultOrEmpty;
    public static RvrLaneOptions None(RvrLaneFallback reason, string detail, string from = null, string to = null) =>
        new([], reason, detail, from, to);
}

/// <summary>A lane the keep walker follows for one destination (march or roam).</summary>
public sealed record RvrLaneTravel(string DestinationId, ushort Region, RvrLaneRoute Route, string Purpose)
{
    public string Tag => $"{Purpose}|{Route.Tag}";
}

/// <summary>
/// Which lanes a marching force or a roaming group takes between two catalog objectives (1.2 work package 5;
/// docs/RVR-FIELD-1.2.md, "Feet"). Pure: the live keep owners, hazards and lane proofs come in as arguments.
/// <list type="bullet">
/// <item>Trunk, then fan: from a portal keep everyone walks the trunk to its milegate, then the lanes from the gate.</item>
/// <item>A lane is left out when it is not validated, enters a zone closed to the realm, passes within
/// <see cref="KeepClearance"/> of a keep an enemy of the marching realm holds now (other than the goal), or crosses one
/// of today's <see cref="AutonomousArmyDetour"/> hazards before the siege camp.</item>
/// <item>The groups of a march split 50/25/25 over three lanes, 67/33 over two (<see cref="Weights"/>), by head count;
/// with none left, everyone takes today's route (RvrKeepRoute.TryBuild and the leader's detour).</item>
/// </list>
/// </summary>
public static class RvrLaneChoice
{
    /// <summary>A march or roam starts at the catalog objective within this distance of where it sets off.</summary>
    public const float OriginRadius = 8_000;
    /// <summary>Live-ownership keep filter (the catalog's own filter rejected every keep, whoever held it).</summary>
    public const float KeepClearance = 4_000;
    /// <summary>A bot farther than this from its lane walks today's route instead of crossing country to it.</summary>
    public const float JoinDistance = 6_000;
    public const int MaxLanes = 3;
    /// <summary>Where the march ends today (TryMarchWithArmy: the camp radius plus 2,500) and the column re-merges.</summary>
    public static float MergeRadius => AutonomousRvrRally.CampRadius + 2_500;

    /// <summary>Head-count weights of the lanes in use: 2/1/1 (50/25/25), 2/1 (67/33), or 1.</summary>
    public static ImmutableArray<int> Weights(int lanes) => lanes switch { <= 1 => [1], 2 => [2, 1], _ => [2, 1, 1] };

    /// <summary>
    /// The lanes from the catalog objective nearest <paramref name="origin"/> to the goal. Pure apart from the delegates.
    /// </summary>
    public static RvrLaneOptions Options(RvrLaneData data, Func<RvrLane, bool, RvrLaneProof> proof, ushort region, eRealm realm,
        Vector3 origin, string goalRef, Vector2 goalCenter, int goalKeepId, IReadOnlyList<RvrLiveKeep> keeps,
        IReadOnlyList<AutonomousArmyDetour.Hazard> hazards, Func<Vector3, bool> allowed = null)
    {
        if (data == null || data.Lanes.Length == 0)
            return RvrLaneOptions.None(RvrLaneFallback.NoCatalog, data?.Problem ?? "no catalog", null, goalRef);
        RvrLaneObjective from = data.Nearest(region, origin, OriginRadius, objective => objective.Ref != goalRef);
        if (from == null)
            return RvrLaneOptions.None(RvrLaneFallback.NoOrigin,
                $"no catalog objective within {OriginRadius:0} of {(int)origin.X},{(int)origin.Y}", null, goalRef);
        return Between(data, proof, region, realm, from.Ref, goalRef, goalCenter, goalKeepId, keeps, hazards, allowed);
    }

    /// <summary>The lanes between two named catalog objectives (trunk first when <paramref name="fromRef"/> is a portal keep).</summary>
    public static RvrLaneOptions Between(RvrLaneData data, Func<RvrLane, bool, RvrLaneProof> proof, ushort region, eRealm realm,
        string fromRef, string toRef, Vector2 goalCenter, int goalKeepId, IReadOnlyList<RvrLiveKeep> keeps,
        IReadOnlyList<AutonomousArmyDetour.Hazard> hazards, Func<Vector3, bool> allowed = null)
    {
        RvrLaneObjective from = data.Objective(fromRef);
        RvrLane trunk = null;
        RvrLaneProof trunkProof = null;
        string tripFrom = fromRef;
        if (from?.Kind == RvrLaneObjectiveKind.PortalKeep)
        {
            trunk = data.TrunkFrom(region, fromRef);
            if (trunk == null) return RvrLaneOptions.None(RvrLaneFallback.NoTrip, "no trunk out of the portal valley", fromRef, toRef);
            trunkProof = proof(trunk, false);
            if (trunkProof?.Usable != true)
                return RvrLaneOptions.None(RvrLaneFallback.Unproved, $"trunk: {trunkProof?.Reason ?? "unchecked"}", fromRef, toRef);
            tripFrom = trunk.To;
        }
        bool reversed = false;
        ImmutableArray<RvrLane> trip = data.Trip(region, tripFrom, toRef);
        if (trip.IsEmpty)
        {
            trip = data.Trip(region, toRef, tripFrom);
            reversed = !trip.IsEmpty;
        }
        if (trip.IsEmpty) return RvrLaneOptions.None(RvrLaneFallback.NoTrip, $"no catalog lane {tripFrom} > {toRef}", fromRef, toRef);

        var routes = ImmutableArray.CreateBuilder<RvrLaneRoute>();
        var rejections = new List<string>();
        RvrLaneFallback first = RvrLaneFallback.None;
        foreach (RvrLane lane in trip)
        {
            if (routes.Count >= MaxLanes) break;
            (RvrLaneFallback reason, string why) = Check(lane, out RvrLaneRoute route);
            if (reason == RvrLaneFallback.None) { routes.Add(route); continue; }
            if (first == RvrLaneFallback.None) first = reason;
            rejections.Add($"lane {lane.Index}: {why}");
        }
        string detail = rejections.Count == 0 ? null : string.Join("; ", rejections);
        return routes.Count == 0
            ? RvrLaneOptions.None(first, detail, fromRef, toRef)
            : new(routes.ToImmutable(), RvrLaneFallback.None, detail, fromRef, toRef);

        (RvrLaneFallback, string) Check(RvrLane lane, out RvrLaneRoute route)
        {
            route = null;
            RvrLaneProof walked = proof(lane, reversed);
            if (walked?.Usable != true) return (RvrLaneFallback.Unproved, $"unproved ({walked?.Reason ?? "unchecked"})");
            route = RvrLaneRoute.Build(routes.Count, lane, walked, trunk, trunkProof, reversed, fromRef, toRef, goalCenter);
            if (allowed != null && route.Walk.Any(point => !allowed(point)))
                return (RvrLaneFallback.NotAllowed, "enters a zone closed to the realm");
            string keep = KeepFailure(route, realm, goalKeepId, keeps);
            if (keep != null) return (RvrLaneFallback.KeepHeld, keep);
            string hazard = HazardFailure(route, goalCenter, hazards);
            return hazard != null ? (RvrLaneFallback.Hazard, hazard) : (RvrLaneFallback.None, null);
        }
    }

    /// <summary>
    /// Why a lane passes too close to a keep an enemy of <paramref name="realm"/> holds now (not the goal, and not one at
    /// the lane's own start), up to the siege camp; null when it does not. Pure.
    /// </summary>
    public static string KeepFailure(RvrLaneRoute route, eRealm realm, int goalKeepId, IReadOnlyList<RvrLiveKeep> keeps)
    {
        if (keeps == null || route.Shape.IsDefaultOrEmpty) return null;
        int count = PointsBeforeMerge(route);
        foreach (RvrLiveKeep keep in keeps)
        {
            if (keep.KeepId == goalKeepId || keep.Realm == realm || keep.Realm == eRealm.None) continue;
            Vector3 at = new(keep.At, 0);
            if (RvrLaneGeometry.Flat(route.Shape[0], at) <= KeepClearance) continue;
            float distance = RvrLaneGeometry.DistanceToPath(at, route.Shape, count);
            if (distance < KeepClearance)
                return $"passes {distance:0} from keep {keep.KeepId}, held by {GlobalConstants.RealmToName(keep.Realm)}";
        }
        return null;
    }

    /// <summary>
    /// Why a lane crosses one of today's march hazards (AutonomousArmyDetour.HazardsFor: enemy keeps at 6,000, the
    /// defenders' rally keep at 12,000) before the siege camp, ignoring hazards around its start or the goal (as
    /// <see cref="AutonomousArmyDetour.FirstDetour"/> does); null when clear. Pure.
    /// </summary>
    public static string HazardFailure(RvrLaneRoute route, Vector2 goalCenter, IReadOnlyList<AutonomousArmyDetour.Hazard> hazards)
    {
        if (hazards == null || route.Shape.IsDefaultOrEmpty) return null;
        int count = PointsBeforeMerge(route);
        Vector2 start = new(route.Shape[0].X, route.Shape[0].Y);
        foreach (AutonomousArmyDetour.Hazard hazard in hazards)
        {
            if (Vector2.Distance(start, hazard.Center) <= hazard.Radius || Vector2.Distance(goalCenter, hazard.Center) <= hazard.Radius)
                continue;
            float distance = RvrLaneGeometry.DistanceToPath(new(hazard.Center, 0), route.Shape, count);
            if (distance < hazard.Radius)
                return $"crosses a march hazard at {(int)hazard.Center.X},{(int)hazard.Center.Y} ({distance:0} of {hazard.Radius:0})";
        }
        return null;
    }

    private static int PointsBeforeMerge(RvrLaneRoute route)
    {
        int count = 0;
        while (count < route.Shape.Length && route.Along[count] <= route.MergeLength) count++;
        return Math.Min(route.Shape.Length, count + 1);
    }

    /// <summary>
    /// The lane slot for a group joining the march: the march leader's group takes slot 0 (the pace-keeper's lane);
    /// every other group the slot furthest below its share of heads (ties to the lower slot). Pure.
    /// </summary>
    public static int AssignSlot(IReadOnlyList<int> heads, int size, bool leaderForce)
    {
        if (leaderForce || heads == null || heads.Count <= 1) return 0;
        ImmutableArray<int> weights = Weights(heads.Count);
        float total = heads.Sum() + Math.Max(1, size), weightSum = weights.Sum();
        int best = 0;
        float bestDeficit = float.MinValue;
        for (int slot = 0; slot < heads.Count; slot++)
        {
            float deficit = weights[slot] * total / weightSum - heads[slot];
            if (deficit > bestDeficit + 1e-3f) { best = slot; bestDeficit = deficit; }
        }
        return best;
    }

    /// <summary>Share of groups (percent) that ignore their flank lane and stay with the main body: the tier's failure rate.</summary>
    public static int TagAlongPercent(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 15,
        RvrSkillTier.Average => 8,
        _ => 3,
    };

    /// <summary>Whether this group, sent to a flank lane, stays with the main body instead (fixed by its key). Pure.</summary>
    public static bool TagsAlong(RvrSkillTier tier, string forceKey) =>
        (int)((ulong)RvrGroupSkill.Key((forceKey ?? "") + ":lane") % 100) < TagAlongPercent(tier);

    /// <summary>The walk point a bot at <paramref name="from"/> joins the route at (the first one ahead of it), or -1 when
    /// it is farther than <see cref="JoinDistance"/> from the lane. Pure.</summary>
    public static int JoinIndex(RvrLaneRoute route, Vector3 from, float hint, out float lateral)
    {
        lateral = float.MaxValue;
        if (route == null || route.Walk.IsDefaultOrEmpty) return -1;
        float walked = route.Project(from, hint, out lateral);
        if (lateral > JoinDistance) return -1;
        for (int i = 0; i < route.Walk.Length; i++)
            if (route.WalkProgress[i] > walked + 100) return i;
        return route.Walk.Length - 1;
    }

    /// <summary>
    /// Hook for work package 6 (roaming groups): the lane a roaming group takes from where it stands to a catalog
    /// objective (<paramref name="toRef"/>, for example "keep:52" or a milegate ref), or null to keep today's route.
    /// The group's lane is fixed by its key (50/25/25 over three lanes). Use it only when the trip is between catalog
    /// objectives; then hand the route to the walker with
    /// <c>UseLaneTravel(new RvrLaneTravel(destination.Id, region, route, "roam"))</c> before FollowKeepTravel.
    /// </summary>
    public static RvrLaneRoute ForRoam(RvrTacticsSwitches switches, RvrLaneData data, Func<RvrLane, bool, RvrLaneProof> proof,
        ushort region, eRealm realm, Vector3 from, string toRef, string groupKey, IReadOnlyList<RvrLiveKeep> keeps,
        IReadOnlyList<AutonomousArmyDetour.Hazard> hazards, Func<Vector3, bool> allowed, out RvrLaneOptions options)
    {
        options = null;
        if (!switches.RoamLanes || data == null) return null;
        RvrLaneObjective target = data.Objective(toRef);
        if (target == null || target.Region != region) return null;
        options = Options(data, proof, region, realm, from, toRef, new(target.At.X, target.At.Y), target.KeepId, keeps, hazards, allowed);
        if (!options.UsesLanes) return null;
        ImmutableArray<int> weights = Weights(options.Routes.Length);
        int roll = (int)((ulong)RvrGroupSkill.Key((groupKey ?? "") + ":roam") % (ulong)weights.Sum());
        for (int slot = 0; slot < weights.Length; slot++)
        {
            if (roll < weights[slot]) return options.Routes[slot];
            roll -= weights[slot];
        }
        return options.Routes[0];
    }
}

public enum RvrLanePace
{
    /// <summary>Level with the leader: walk on while he walks, wait while he waits.</summary>
    Level,
    /// <summary>Ahead of the leader by more than the margin: wait.</summary>
    Hold,
    /// <summary>Behind by more than the margin: walk on, even while the leader waits.</summary>
    Hurry,
}

/// <summary>How tight a group keeps level with the march leader, and how fast it reacts (its skill tier).</summary>
public readonly record struct RvrLaneMargins(float HoldAhead, float HurryBehind, int ReactMilliseconds);

/// <summary>
/// Keeping the lanes of one march level (pure). The march leader is the pace-keeper on his lane; a group on another lane
/// compares the share of its own lane walked with the leader's share of his, past the shared trunk. Ahead by more than
/// its margin it waits; behind by more it walks on while the leader waits; and the leader waits for a flank lane that
/// fell behind by more than <see cref="LaneWaitBehind"/> (at most <see cref="LaneWaitMilliseconds"/> at a time). The
/// march still ends at the siege camp ring, where the leader's existing arrival hold gathers the column.
/// </summary>
public static class RvrLanePacing
{
    public const float LaneWaitBehind = 3_000;
    public const float LaneCaughtUp = 1_000;
    public const long LaneWaitMilliseconds = 45_000;
    public const long LaneWaitGapMilliseconds = 45_000;
    /// <summary>A member's reported progress is used for this long.</summary>
    public const long ProgressFreshMilliseconds = 15_000;
    /// <summary>The stated bound (tests): lanes spread the column's arrival at the camp ring by at most this much more
    /// than one column on one route does, at running speed.</summary>
    public const long ArrivalSpreadMarginMilliseconds = 60_000;

    public static RvrLaneMargins Margins(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => new(1_000, 1_000, 1_000),
        RvrSkillTier.Average => new(1_500, 1_500, 2_000),
        _ => new(2_500, 2_500, 4_000),
    };

    /// <summary>
    /// How far ahead (positive) or behind (negative) a member is of the leader, in units of the member's own route. On a
    /// shared trunk it is the plain distance; past it, the leader's share of his fan maps onto the member's fan.
    /// </summary>
    public static float Gap(float memberWalked, RvrLaneRoute member, float leaderWalked, RvrLaneRoute leader)
    {
        float equivalent;
        if (leaderWalked <= leader.TrunkLength) equivalent = leaderWalked;
        else
        {
            float leaderFan = MathF.Max(1, leader.MergeLength - leader.TrunkLength);
            float share = Math.Clamp((leaderWalked - leader.TrunkLength) / leaderFan, 0, 1);
            equivalent = member.TrunkLength + share * (member.MergeLength - member.TrunkLength);
        }
        return MathF.Min(memberWalked, member.MergeLength) - equivalent;
    }

    public static RvrLanePace Pace(float gap, RvrLaneMargins margins) =>
        gap > margins.HoldAhead ? RvrLanePace.Hold : gap < -margins.HurryBehind ? RvrLanePace.Hurry : RvrLanePace.Level;

    /// <summary>
    /// The pace with the tier's reaction delay: a change of pace takes effect only after it has been wanted for the
    /// tier's reaction time (<paramref name="pendingSince"/> is 0 while no change is pending).
    /// </summary>
    public static RvrLanePace Next(RvrLanePace previous, long pendingSince, float gap, RvrLaneMargins margins, long now,
        out long nextPendingSince)
    {
        RvrLanePace wanted = Pace(gap, margins);
        nextPendingSince = 0;
        if (wanted == previous) return previous;
        if (pendingSince == 0) { nextPendingSince = Math.Max(1, now); return previous; }
        if (now - pendingSince < margins.ReactMilliseconds) { nextPendingSince = pendingSince; return previous; }
        return wanted;
    }

    /// <summary>
    /// Whether a member on another lane stands this turn: ahead of the leader (its pace says hold), or level and not
    /// behind him while he stands. Behind, it walks on until level, also while he waits for it.
    /// </summary>
    public static bool Waits(RvrLanePace pace, float gap, bool leaderMoving) =>
        pace == RvrLanePace.Hold || !leaderMoving && pace == RvrLanePace.Level && gap >= 0;

    /// <summary>A member on another lane counts as "with the leader" for his regroup when level within the assist radius.</summary>
    public static bool Level(float gap) => MathF.Abs(gap) <= AutonomousWorldBotController.ArmyAssistRadius;

    /// <summary>Whether the leader waits for the flank lane furthest behind (<paramref name="worstGap"/>, negative = behind).</summary>
    public static bool LeaderWaits(float worstGap, long now, ref long waitUntil, ref long nextWait)
    {
        if (now < waitUntil)
        {
            if (worstGap < -LaneCaughtUp) return true;
            waitUntil = 0;
            return false;
        }
        if (now < nextWait || worstGap >= -LaneWaitBehind) return false;
        waitUntil = now + LaneWaitMilliseconds;
        nextWait = now + LaneWaitMilliseconds + LaneWaitGapMilliseconds;
        return true;
    }

    /// <summary>The median of a lane's gaps (its main body, not its last straggler).</summary>
    public static float Median(List<float> gaps)
    {
        if (gaps.Count == 0) return 0;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }
}

/// <summary>One march's lane plan: its usable lanes, each group's lane (fixed for the whole march) and progress.</summary>
public sealed class RvrMarchLanePlan
{
    public readonly record struct ForceLane(int Slot, int Intended, RvrSkillTier Tier, bool TagAlong, int Heads);

    public string TargetId { get; }
    public ushort Region { get; }
    public eRealm Realm { get; }
    public RvrLaneOptions Options { get; }
    public long CreatedAt { get; }
    internal long TouchedAt;

    private readonly object _sync = new();
    private readonly Dictionary<string, ForceLane> _forces = new(StringComparer.Ordinal);
    private readonly int[] _heads;
    private readonly ConcurrentDictionary<long, (int Slot, float Walked, long Tick)> _progress = new();
    private int _fallbackLogged;

    public RvrMarchLanePlan(string targetId, ushort region, eRealm realm, RvrLaneOptions options, long now)
    {
        TargetId = targetId; Region = region; Realm = realm; Options = options; CreatedAt = TouchedAt = now;
        _heads = new int[Math.Max(1, options.Routes.IsDefaultOrEmpty ? 1 : options.Routes.Length)];
    }

    /// <summary>The group's lane, chosen the first time any member asks and never changed for this march.</summary>
    public ForceLane Assign(string force, int size, bool leaderForce, RvrSkillTier tier, out bool assignedNow)
    {
        lock (_sync)
        {
            assignedNow = false;
            if (_forces.TryGetValue(force, out ForceLane known)) return known;
            int heads = Math.Max(1, size);
            int intended = RvrLaneChoice.AssignSlot(_heads, heads, leaderForce);
            bool tagAlong = intended > 0 && RvrLaneChoice.TagsAlong(tier, force);
            _heads[intended] += heads;
            assignedNow = true;
            return _forces[force] = new(tagAlong ? 0 : intended, intended, tier, tagAlong, heads);
        }
    }

    public bool TryGetForce(string force, out ForceLane lane)
    {
        lock (_sync) return _forces.TryGetValue(force, out lane);
    }

    /// <summary>Lanes groups actually walk (the before/after measure: today 1).</summary>
    public int DistinctLanes
    {
        get { lock (_sync) return _forces.Values.Select(lane => lane.Slot).Distinct().Count(); }
    }

    public void Report(long botId, int slot, float walked, long now) => _progress[botId] = (slot, walked, now);

    public bool TryProgress(long botId, long now, out int slot, out float walked)
    {
        slot = 0; walked = 0;
        if (!_progress.TryGetValue(botId, out var entry) || now - entry.Tick > RvrLanePacing.ProgressFreshMilliseconds) return false;
        slot = entry.Slot; walked = entry.Walked;
        return true;
    }

    /// <summary>True once per march: the RVR_LANE_FALLBACK line for the whole march.</summary>
    public bool TakeFallbackLog() => Interlocked.Exchange(ref _fallbackLogged, 1) == 0;
}

/// <summary>The lane plans of the marches under way, by target (dropped after five idle minutes).</summary>
public static class RvrMarchLanes
{
    public const long IdleMilliseconds = 5 * 60_000;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, RvrMarchLanePlan> Plans = new(StringComparer.Ordinal);

    /// <summary>This march's plan; <paramref name="build"/> runs once, when the first marcher asks.</summary>
    public static RvrMarchLanePlan Get(string targetId, ushort region, eRealm realm, long now, Func<RvrLaneOptions> build)
    {
        lock (Sync)
        {
            foreach (string stale in Plans.Where(pair => now - pair.Value.TouchedAt > IdleMilliseconds).Select(pair => pair.Key).ToArray())
                Plans.Remove(stale);
            if (Plans.TryGetValue(targetId, out RvrMarchLanePlan known) && known.Region == region && known.Realm == realm)
            {
                known.TouchedAt = now;
                return known;
            }
        }
        RvrLaneOptions options = build() ?? RvrLaneOptions.None(RvrLaneFallback.NoCatalog, "no options");
        lock (Sync)
        {
            if (Plans.TryGetValue(targetId, out RvrMarchLanePlan raced) && raced.Region == region && raced.Realm == realm)
                return raced;
            return Plans[targetId] = new RvrMarchLanePlan(targetId, region, realm, options, now);
        }
    }

    public static void Forget(string targetId)
    {
        lock (Sync) Plans.Remove(targetId);
    }
}
