using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>The four kinds of lane in the catalog (docs/research/rvr-field-1.2/LANES-CATALOG.md).</summary>
public enum RvrLaneKind
{
    /// <summary>A portal keep to its milegate: the one road out of the portal valley, lane 0 only.</summary>
    Trunk,
    /// <summary>A milegate to a keep: the fan, up to three lanes.</summary>
    Gate,
    /// <summary>A realm's border keep to one of its own keeps.</summary>
    Entry,
    /// <summary>A keep to a keep within 60,000 units.</summary>
    Hop,
}

public enum RvrLaneObjectiveKind { PortalKeep, Keep, RelicKeep, Milegate, Entry }

/// <summary>A catalog objective: a keep, a milegate or a border keep ("entry"), at its catalog point.</summary>
public sealed record RvrLaneObjective(string Ref, RvrLaneObjectiveKind Kind, ushort Region, int KeepId, string Name,
    Vector3 At, int PortalKeepId);

/// <summary>
/// One catalog lane as shipped: geometry only (start, via points, goal) and the lengths the survey walked. Lane 0 is the
/// planner's own route (no via points). <see cref="Id"/> is the lane's position in the catalog.
/// </summary>
public sealed record RvrLane(int Id, ushort Region, RvrLaneKind Kind, string From, string To, int Index,
    Vector3 Start, Vector3 Goal, ImmutableArray<Vector3> Via, float Length, float BackLength, string Proof)
{
    /// <summary>Start, via points, goal.</summary>
    public ImmutableArray<Vector3> Points => [Start, .. Via, Goal];
}

/// <summary>The survey's acceptance criteria, as the catalog states them.</summary>
public sealed record RvrLaneCriteria(float MaxLengthRatio, float MinSeparation, float KeepClearance, float MaxSwim,
    float MonsterCampRadius, float HopRadius)
{
    public static readonly RvrLaneCriteria Default = new(1.35f, 2_500, 4_000, 1_000, 1_500, 60_000);
}

public enum RvrLaneStatus { Unchecked, Validated, Rejected }

/// <summary>
/// What the server's own walk of a lane found. <see cref="Walk"/> is what the keep walker follows (the planner's zone
/// seams and leg ends, RvrKeepRoute.TryBuild); <see cref="Shape"/> is the Detour corridor between them, which every walk
/// point is also on, used for lengths, progress, keep clearance and hazards. Only a validated lane is ever used.
/// </summary>
public sealed record RvrLaneProof(RvrLaneStatus Status, ImmutableArray<Vector3> Walk, ImmutableArray<Vector3> Shape,
    float Length, string Reason)
{
    public static readonly RvrLaneProof Unchecked = new(RvrLaneStatus.Unchecked, [], [], 0, "not checked yet");
    public static RvrLaneProof Rejected(string reason) => new(RvrLaneStatus.Rejected, [], [], 0, reason);
    public bool Usable => Status == RvrLaneStatus.Validated && Walk.Length > 0 && Shape.Length > 1;
}

/// <summary>The parsed catalog (immutable). <see cref="Problem"/> is null when it loaded cleanly.</summary>
public sealed class RvrLaneData
{
    public static readonly RvrLaneData Empty = new(0, "", RvrLaneCriteria.Default, [], [], "empty catalog");

    public int Version { get; }
    public string Tool { get; }
    public RvrLaneCriteria Criteria { get; }
    public ImmutableArray<RvrLaneObjective> Objectives { get; }
    public ImmutableArray<RvrLane> Lanes { get; }
    public string Problem { get; }

    private readonly Dictionary<string, RvrLaneObjective> _objectives = new(StringComparer.Ordinal);
    private readonly Dictionary<(ushort, string, string), ImmutableArray<RvrLane>> _trips = new();

    public RvrLaneData(int version, string tool, RvrLaneCriteria criteria, ImmutableArray<RvrLaneObjective> objectives,
        ImmutableArray<RvrLane> lanes, string problem = null)
    {
        Version = version; Tool = tool ?? ""; Criteria = criteria ?? RvrLaneCriteria.Default;
        Objectives = objectives.IsDefault ? [] : objectives;
        Lanes = lanes.IsDefault ? [] : lanes;
        Problem = problem;
        foreach (RvrLaneObjective objective in Objectives)
            _objectives[objective.Ref] = objective;
        foreach (var trip in Lanes.GroupBy(lane => (lane.Region, lane.From, lane.To)))
            _trips[trip.Key] = [.. trip.OrderBy(lane => lane.Index)];
    }

    public int TripCount => _trips.Count;

    /// <summary>Every trip's lanes, lane 0 first.</summary>
    public IEnumerable<ImmutableArray<RvrLane>> Trips => _trips.Values;

    public RvrLaneObjective Objective(string reference) =>
        reference != null && _objectives.TryGetValue(reference, out RvrLaneObjective objective) ? objective : null;

    /// <summary>The lanes from one objective to another as catalogued (one direction), lane 0 first; empty if none.</summary>
    public ImmutableArray<RvrLane> Trip(ushort region, string from, string to) =>
        from != null && to != null && _trips.TryGetValue((region, from, to), out var lanes) ? lanes : [];

    /// <summary>The trunk out of a portal keep's valley, or null.</summary>
    public RvrLane TrunkFrom(ushort region, string portalKeepRef) =>
        Lanes.FirstOrDefault(lane => lane.Kind == RvrLaneKind.Trunk && lane.Region == region && lane.From == portalKeepRef);

    /// <summary>The catalog objective nearest a point in a region, within a radius (2-D), or null.</summary>
    public RvrLaneObjective Nearest(ushort region, Vector3 point, float radius, Func<RvrLaneObjective, bool> filter = null)
    {
        RvrLaneObjective best = null;
        float bestDistance = radius * radius;
        foreach (RvrLaneObjective objective in Objectives)
        {
            if (objective.Region != region || filter != null && !filter(objective)) continue;
            float distance = Vector2.DistanceSquared(new(point.X, point.Y), new(objective.At.X, objective.At.Y));
            if (distance <= bestDistance) { best = objective; bestDistance = distance; }
        }
        return best;
    }

    public static string KeepRef(int keepId) => $"keep:{keepId}";
}

/// <summary>One leg of a lane's walk: append the walked points from <paramref name="from"/> (excluded) to
/// <paramref name="to"/> (included) to both lists. False, with a reason, when the planner cannot walk it.</summary>
public delegate bool RvrLegWalker(Vector3 from, Vector3 to, List<Vector3> walk, List<Vector3> shape, out string failure);

/// <summary>
/// The open-field RvR lane catalog (1.2 work package 5; docs/RVR-FIELD-1.2.md, "Feet"): the embedded
/// bots/autonomous/data/rvr-lanes.json, loaded lazily as the other embedded catalogs are, and a background validation that
/// re-walks every lane on the live navmesh with the server's own planner (RvrKeepRoute.TryBuild under a bounded
/// RvrPlanningNavigation, the keep walker's proof), a few milliseconds at a time with pauses between, so it never holds
/// the game loop. A lane that fails is turned off (RVR_LANE_REJECTED); until a lane is validated it is never used.
/// RVR_LANE_LOADED reports the counts per region and kind once the walk is done. Tests only until seen in the owner's log.
/// </summary>
public static class RvrLaneCatalog
{
    private static readonly Logger Log = LoggerManager.Create(typeof(RvrLaneCatalog));

    public const string ResourceSuffix = "rvr-lanes.json";
    /// <summary>A validated walk may differ from the survey's walked length by this share (Detour chose another corridor
    /// after a mesh change when it is more).</summary>
    public const float LengthTolerance = 0.25f;
    /// <summary>Alternates of one trip must still lie this share of the catalog's separation apart over their middle half.</summary>
    public const float SeparationTolerance = 0.8f;
    /// <summary>Validation's bounded work: one planner slice (RvrPlanningNavigation.SliceMilliseconds), then this pause.</summary>
    public const int PauseMilliseconds = 50;
    public const int StartDelayMilliseconds = 20_000;
    public const int NavigationWaitMilliseconds = 10 * 60_000;

    private static readonly Lazy<RvrLaneData> Loaded = new(() => Load(typeof(RvrLaneCatalog).Assembly, ResourceSuffix));
    private static RvrLaneProof[] _forward = [], _back = [];
    private static int _validationStarted;
    private static volatile bool _validationDone;

    /// <summary>The embedded catalog (parsed on first use).</summary>
    public static RvrLaneData Data => Loaded.Value;

    public static bool ValidationDone => _validationDone;

    /// <summary>This lane's validation result in the direction asked (only hops are walked back). Unchecked until the
    /// background walk reached it.</summary>
    public static RvrLaneProof ProofOf(RvrLane lane, bool reverse)
    {
        RvrLaneProof[] proofs = reverse ? Volatile.Read(ref _back) : Volatile.Read(ref _forward);
        return lane != null && lane.Id >= 0 && lane.Id < proofs.Length ? proofs[lane.Id] ?? RvrLaneProof.Unchecked : RvrLaneProof.Unchecked;
    }

    /// <summary>Reads the catalog from an assembly resource; a missing or broken resource gives an empty catalog with
    /// the problem named (the march then takes today's route, RVR_LANE_FALLBACK reason=NoCatalog).</summary>
    public static RvrLaneData Load(Assembly assembly, string resourceSuffix)
    {
        try
        {
            string resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                return Report(new RvrLaneData(0, "", RvrLaneCriteria.Default, [], [], $"resource '{resourceSuffix}' is missing"));
            using Stream stream = assembly.GetManifestResourceStream(resource);
            return Report(Parse(stream));
        }
        catch (Exception exception)
        {
            return Report(new RvrLaneData(0, "", RvrLaneCriteria.Default, [], [], $"unreadable: {exception.Message}"));
        }

        static RvrLaneData Report(RvrLaneData data)
        {
            if (data.Problem != null) TryLog(true, $"RVR_LANE_LOADED lanes=0 trips=0 problem=\"{data.Problem}\"; marches take today's route");
            return data;
        }
    }

    public static RvrLaneData Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? ""));
        return Parse(stream);
    }

    /// <summary>Parses the catalog JSON. Pure. A malformed lane is skipped and counted in <see cref="RvrLaneData.Problem"/>.</summary>
    public static RvrLaneData Parse(Stream stream)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(stream); }
        catch (Exception exception) { return new(0, "", RvrLaneCriteria.Default, [], [], $"not JSON: {exception.Message}"); }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("lanes", out JsonElement lanesJson) ||
                lanesJson.ValueKind != JsonValueKind.Array)
                return new(0, "", RvrLaneCriteria.Default, [], [], "no lanes array");
            int version = root.TryGetProperty("version", out JsonElement v) && v.TryGetInt32(out int number) ? number : 0;
            string tool = root.TryGetProperty("tool", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "";
            RvrLaneCriteria criteria = RvrLaneCriteria.Default;
            if (root.TryGetProperty("criteria", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
                criteria = new(Number(c, "max_length_ratio", 1.35f), Number(c, "min_separation_middle_half", 2_500),
                    Number(c, "keep_clearance", 4_000), Number(c, "max_swim", 1_000), Number(c, "monster_camp_radius", 1_500),
                    Number(c, "hop_radius", 60_000));
            var objectives = ImmutableArray.CreateBuilder<RvrLaneObjective>();
            int skipped = 0;
            if (root.TryGetProperty("objectives", out JsonElement objectivesJson) && objectivesJson.ValueKind == JsonValueKind.Array)
                foreach (JsonElement o in objectivesJson.EnumerateArray())
                {
                    string reference = Text(o, "ref");
                    RvrLaneObjectiveKind? kind = Text(o, "kind") switch
                    {
                        "portal keep" => RvrLaneObjectiveKind.PortalKeep, "keep" => RvrLaneObjectiveKind.Keep,
                        "relic keep" => RvrLaneObjectiveKind.RelicKeep, "milegate" => RvrLaneObjectiveKind.Milegate,
                        "entry" => RvrLaneObjectiveKind.Entry, _ => null,
                    };
                    if (reference == null || kind == null || !TryPoint(o, "at", out Vector3 at)) { skipped++; continue; }
                    objectives.Add(new(reference, kind.Value, (ushort)Integer(o, "region"), Integer(o, "keep_id"), Text(o, "name") ?? reference,
                        at, Integer(o, "portal_keep")));
                }
            var lanes = ImmutableArray.CreateBuilder<RvrLane>();
            foreach (JsonElement l in lanesJson.EnumerateArray())
            {
                RvrLaneKind? kind = Text(l, "kind") switch
                {
                    "trunk" => RvrLaneKind.Trunk, "gate" => RvrLaneKind.Gate, "entry" => RvrLaneKind.Entry, "hop" => RvrLaneKind.Hop,
                    _ => null,
                };
                string from = l.TryGetProperty("from", out JsonElement f) ? Text(f, "ref") : null;
                string to = l.TryGetProperty("to", out JsonElement g) ? Text(g, "ref") : null;
                if (kind == null || from == null || to == null || !TryPoint(l, "start", out Vector3 start) ||
                    !TryPoint(l, "goal", out Vector3 goal) || !l.TryGetProperty("lane", out JsonElement index) ||
                    !index.TryGetInt32(out int laneIndex) || laneIndex is < 0 or > 2)
                { skipped++; continue; }
                var via = ImmutableArray.CreateBuilder<Vector3>();
                bool viaOk = true;
                if (l.TryGetProperty("via", out JsonElement viaJson) && viaJson.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement p in viaJson.EnumerateArray())
                        if (TryArrayPoint(p, out Vector3 point)) via.Add(point); else viaOk = false;
                if (!viaOk) { skipped++; continue; }
                float forward = 0, back = 0;
                if (l.TryGetProperty("length", out JsonElement length) && length.ValueKind == JsonValueKind.Object)
                { forward = Number(length, "forward", 0); back = Number(length, "back", forward); }
                lanes.Add(new(lanes.Count, (ushort)Integer(l, "region"), kind.Value, from, to, laneIndex, start, goal,
                    via.ToImmutable(), forward, back, Text(l, "proof") ?? ""));
            }
            return new(version, tool, criteria, objectives.ToImmutable(), lanes.ToImmutable(),
                skipped > 0 ? $"{skipped} malformed entries skipped" : null);
        }

        static string Text(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        static int Integer(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int i) ? i : 0;
        static float Number(JsonElement e, string name, float fallback) =>
            e.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double d) ? (float)d : fallback;
        static bool TryPoint(JsonElement e, string name, out Vector3 point)
        {
            point = default;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement value) && TryArrayPoint(value, out point);
        }
    }

    private static bool TryArrayPoint(JsonElement e, out Vector3 point)
    {
        point = default;
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3) return false;
        var values = new float[3];
        int i = 0;
        foreach (JsonElement item in e.EnumerateArray())
        {
            if (!item.TryGetDouble(out double d)) return false;
            values[i++] = (float)d;
        }
        point = new(values[0], values[1], values[2]);
        return true;
    }

    /// <summary>
    /// The realm whose walk proves a lane: a trunk or a fan from a milegate is walked by its portal keep's realm, an entry
    /// or a hop by the frontier's home realm (other realms' zone limits are checked when a lane is chosen). Pure.
    /// </summary>
    public static eRealm NaturalRealm(RvrLaneData data, RvrLane lane, Func<int, eRealm?> keepRealm = null)
    {
        if (lane == null) return eRealm.None;
        if (lane.Kind is RvrLaneKind.Trunk or RvrLaneKind.Gate)
        {
            RvrLaneObjective from = data?.Objective(lane.From);
            int portalKeep = from?.Kind == RvrLaneObjectiveKind.PortalKeep ? from.KeepId : from?.PortalKeepId ?? 0;
            if (portalKeep > 0 && keepRealm?.Invoke(portalKeep) is eRealm live and not eRealm.None) return live;
            RvrLaneObjective portal = data?.Objectives.FirstOrDefault(o => o.Kind == RvrLaneObjectiveKind.PortalKeep &&
                o.Region == lane.Region && o.KeepId == portalKeep);
            string name = portal?.Name ?? "";
            if (name.StartsWith("Albion", StringComparison.OrdinalIgnoreCase)) return eRealm.Albion;
            if (name.StartsWith("Midgard", StringComparison.OrdinalIgnoreCase)) return eRealm.Midgard;
            if (name.StartsWith("Hibernia", StringComparison.OrdinalIgnoreCase)) return eRealm.Hibernia;
        }
        return HomeRealm(lane.Region);
    }

    /// <summary>The realm whose frontier a region is.</summary>
    public static eRealm HomeRealm(ushort region) => region switch
    {
        1 => eRealm.Albion,
        100 => eRealm.Midgard,
        200 => eRealm.Hibernia,
        _ => eRealm.None,
    };

    /// <summary>
    /// Walks one lane, leg by leg (start, each via point, goal; reversed for a back walk), with the given walker and
    /// checks the walked length against the survey's. Pure apart from the walker.
    /// </summary>
    public static RvrLaneProof Prove(RvrLane lane, bool reverse, RvrLegWalker walker, float lengthTolerance = LengthTolerance)
    {
        if (lane == null || walker == null) return RvrLaneProof.Rejected("nothing to walk");
        ImmutableArray<Vector3> points = reverse ? [.. Enumerable.Reverse(lane.Points)] : lane.Points;
        var walk = new List<Vector3> { points[0] };
        var shape = new List<Vector3> { points[0] };
        for (int leg = 1; leg < points.Length; leg++)
        {
            string failure;
            bool walked;
            try { walked = walker(points[leg - 1], points[leg], walk, shape, out failure); }
            catch (Exception exception) { walked = false; failure = exception.GetType().Name; }
            if (!walked) return RvrLaneProof.Rejected($"leg {leg}/{points.Length - 1}: {failure ?? "no walk"}");
        }
        float length = RvrLaneGeometry.Length(shape);
        float proved = reverse ? lane.BackLength : lane.Length;
        if (proved > 0 && MathF.Abs(length - proved) > proved * lengthTolerance)
            return RvrLaneProof.Rejected($"walked {length:0} units, the survey proved {proved:0} (another corridor)");
        return new(RvrLaneStatus.Validated, [.. walk], [.. shape], length, null);
    }

    /// <summary>
    /// Why an alternate no longer keeps apart from a lower lane of its trip (the least distance from the middle half of its
    /// walk to the other walk is under <paramref name="minimum"/>), or null when it still does. Pure.
    /// </summary>
    public static string SeparationFailure(RvrLaneProof alternate, RvrLaneProof other, float minimum)
    {
        if (alternate?.Usable != true || other?.Usable != true) return null;
        float[] along = RvrLaneGeometry.Cumulative(alternate.Shape);
        float total = along[^1];
        float least = float.MaxValue;
        for (int i = 0; i < alternate.Shape.Length; i++)
        {
            if (along[i] < total * 0.25f || along[i] > total * 0.75f) continue;
            least = MathF.Min(least, RvrLaneGeometry.DistanceToPath(alternate.Shape[i], other.Shape, other.Shape.Length));
        }
        return least < minimum ? $"only {least:0} units from a lower lane over its middle half (needs {minimum:0})" : null;
    }

    /// <summary>The RVR_LANE_LOADED line: counts per region and kind, validated / rejected (and unchecked if any). Pure.</summary>
    public static string Summary(RvrLaneData data, IReadOnlyList<RvrLaneProof> forward, IReadOnlyList<RvrLaneProof> back, double seconds)
    {
        int Count(IReadOnlyList<RvrLaneProof> proofs, RvrLane lane, RvrLaneStatus status) =>
            (lane.Id < proofs.Count ? proofs[lane.Id]?.Status ?? RvrLaneStatus.Unchecked : RvrLaneStatus.Unchecked) == status ? 1 : 0;
        var text = new StringBuilder();
        int validated = data.Lanes.Sum(lane => Count(forward, lane, RvrLaneStatus.Validated));
        int rejected = data.Lanes.Sum(lane => Count(forward, lane, RvrLaneStatus.Rejected));
        RvrLane[] hops = data.Lanes.Where(lane => lane.Kind == RvrLaneKind.Hop).ToArray();
        text.Append(FormattableString.Invariant(
            $"RVR_LANE_LOADED lanes={data.Lanes.Length} trips={data.TripCount} validated={validated} rejected={rejected} "));
        int unchecked_ = data.Lanes.Length - validated - rejected;
        if (unchecked_ > 0) text.Append($"unchecked={unchecked_} ");
        text.Append($"hopsBack={hops.Sum(lane => Count(back, lane, RvrLaneStatus.Validated))}/{hops.Length} ");
        text.Append(FormattableString.Invariant($"seconds={seconds:0} byRegion=\""));
        text.Append(string.Join(";", data.Lanes.GroupBy(lane => lane.Region).OrderBy(group => group.Key).Select(region =>
            $"{region.Key}:" + string.Join(",", region.GroupBy(lane => lane.Kind).OrderBy(group => group.Key).Select(kind =>
                $"{kind.Key.ToString().ToLowerInvariant()} {kind.Sum(lane => Count(forward, lane, RvrLaneStatus.Validated))}/" +
                $"{kind.Sum(lane => Count(forward, lane, RvrLaneStatus.Rejected))}")))));
        text.Append('"');
        return text.ToString();
    }

    /// <summary>Starts the background validation once (server start, and the first march that wants lanes if the
    /// switches were off at start).</summary>
    public static void StartValidation()
    {
        if (Interlocked.Exchange(ref _validationStarted, 1) != 0) return;
        var thread = new Thread(ValidateAll) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "RvrLaneValidation" };
        thread.Start();
    }

    private static void ValidateAll()
    {
        try
        {
            Thread.Sleep(StartDelayMilliseconds);
            RvrLaneData data = Data;
            var forward = new RvrLaneProof[data.Lanes.Length];
            var back = new RvrLaneProof[data.Lanes.Length];
            Volatile.Write(ref _forward, forward);
            Volatile.Write(ref _back, back);
            if (data.Lanes.Length == 0) return;
            var waited = Stopwatch.StartNew();
            while (!(PathfindingProvider.Instance?.IsAvailable ?? false) && waited.ElapsedMilliseconds < NavigationWaitMilliseconds)
                Thread.Sleep(5_000);
            var started = Stopwatch.StartNew();
            foreach (ImmutableArray<RvrLane> trip in data.Trips.ToArray())
            {
                // A trip's lanes are published together, after their separation check, so a march never takes an
                // alternate whose spacing is not proven yet.
                var tripForward = trip.Select(lane => ProveLive(data, lane, false)).ToArray();
                var tripBack = trip.Select(lane => lane.Kind == RvrLaneKind.Hop ? ProveLive(data, lane, true) : null).ToArray();
                for (int k = 1; k < trip.Length; k++)
                    for (int j = 0; j < k; j++)
                    {
                        string failure = SeparationFailure(tripForward[k], tripForward[j], data.Criteria.MinSeparation * SeparationTolerance);
                        if (failure == null) continue;
                        tripForward[k] = RvrLaneProof.Rejected($"lane {trip[j].Index}: {failure}");
                        tripBack[k] = tripBack[k] == null ? null : tripForward[k];
                        break;
                    }
                for (int i = 0; i < trip.Length; i++)
                {
                    forward[trip[i].Id] = tripForward[i];
                    back[trip[i].Id] = tripBack[i];
                    foreach ((RvrLaneProof proof, string direction) in new[] { (tripForward[i], "out"), (tripBack[i], "back") })
                        if (proof?.Status == RvrLaneStatus.Rejected)
                            TryLog(false, $"RVR_LANE_REJECTED region={trip[i].Region} kind={trip[i].Kind.ToString().ToLowerInvariant()} " +
                                $"from=\"{trip[i].From}\" to=\"{trip[i].To}\" lane={trip[i].Index} walk={direction} reason=\"{proof.Reason}\"");
                }
            }
            TryLog(false, Summary(data, forward, back, started.Elapsed.TotalSeconds));
        }
        catch (Exception exception)
        {
            TryLog(true, $"RVR_LANE_LOADED problem=\"validation stopped: {exception.GetType().Name} {exception.Message}\"; unvalidated lanes stay off");
        }
        finally
        {
            _validationDone = true;
        }
    }

    private static RvrLaneProof ProveLive(RvrLaneData data, RvrLane lane, bool reverse)
    {
        Region region = WorldMgr.GetRegion(lane.Region);
        IPathfindingMgr raw = PathfindingProvider.Instance;
        if (region == null) return RvrLaneProof.Rejected("region not loaded");
        if (raw == null || !raw.IsAvailable) return RvrLaneProof.Rejected("navigation unavailable");
        eRealm realm = NaturalRealm(data, lane, id => GameServer.KeepManager?.GetKeepByID(id)?.Realm);
        var planning = new RvrPlanningNavigation(AutonomousKeepApproachNavigation.ForRealm(raw, region, realm));
        var nodes = new WrappedPathfindingNode[1024];
        return Prove(lane, reverse, Walk);

        bool Walk(Vector3 from, Vector3 to, List<Vector3> walk, List<Vector3> shape, out string failure)
        {
            failure = null;
            Zone start = region.GetZone((int)from.X, (int)from.Y), end = region.GetZone((int)to.X, (int)to.Y);
            if (start == null || end == null) { failure = "point outside the region's zones"; return false; }
            if (!raw.HasNavmesh(start) || !raw.HasNavmesh(end)) { failure = "no navmesh"; return false; }
            Vector3[] steps;
            while (true)
            {
                planning.BeginSlice();
                try
                {
                    if (!RvrKeepRoute.TryBuild(region, planning, realm, from, to, out steps))
                    { failure = "no planner route"; return false; }
                    break;
                }
                catch (RvrPlanningNavigation.Yield) { }
                catch (RvrPlanningNavigation.Limit) { failure = "route-work limit"; return false; }
                finally { planning.EndSlice(); }
                Thread.Sleep(PauseMilliseconds);
            }
            Vector3 previous = from;
            long sliceStart = Environment.TickCount64;
            foreach (Vector3 step in steps)
            {
                Zone zone = region.GetZone((int)previous.X, (int)previous.Y);
                if (zone != null && zone == region.GetZone((int)step.X, (int)step.Y) && raw.HasNavmesh(zone))
                {
                    PathfindingResult path = raw.GetPathStraight(zone, previous, step, raw.DefaultFilters, nodes);
                    for (int i = 0; i < path.NodeCount && i < nodes.Length; i++)
                        if (Vector3.DistanceSquared(nodes[i].Position, shape[^1]) > 1) shape.Add(nodes[i].Position);
                    if (Environment.TickCount64 - sliceStart >= RvrPlanningNavigation.SliceMilliseconds)
                    {
                        Thread.Sleep(PauseMilliseconds);
                        sliceStart = Environment.TickCount64;
                    }
                }
                if (Vector3.DistanceSquared(step, shape[^1]) > 1) shape.Add(step);
                walk.Add(step);
                previous = step;
            }
            Thread.Sleep(PauseMilliseconds);
            return true;
        }
    }

    // Unit tests do not start the server logger.
    private static void TryLog(bool warn, string message)
    {
        try
        {
            if (warn) { if (Log.IsWarnEnabled) Log.Warn(message); }
            else if (Log.IsInfoEnabled) Log.Info(message);
        }
        catch { }
    }
}

/// <summary>Plain polyline geometry for lanes (2-D, in the ground plane). Pure.</summary>
public static class RvrLaneGeometry
{
    public static float Length(IReadOnlyList<Vector3> path)
    {
        float length = 0;
        for (int i = 1; i < path.Count; i++) length += Flat(path[i - 1], path[i]);
        return length;
    }

    /// <summary>Walked distance at each point.</summary>
    public static float[] Cumulative(IReadOnlyList<Vector3> path)
    {
        var along = new float[Math.Max(1, path.Count)];
        for (int i = 1; i < path.Count; i++) along[i] = along[i - 1] + Flat(path[i - 1], path[i]);
        return along;
    }

    public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

    /// <summary>The least ground distance from a point to the first <paramref name="count"/> points of a path.</summary>
    public static float DistanceToPath(Vector3 point, IReadOnlyList<Vector3> path, int count)
    {
        count = Math.Min(count, path.Count);
        if (count == 0) return float.MaxValue;
        if (count == 1) return Flat(point, path[0]);
        float least = float.MaxValue;
        for (int i = 1; i < count; i++)
            least = MathF.Min(least, Segment(point, path[i - 1], path[i], out _));
        return least;
    }

    /// <summary>Ground distance from a point to segment a-b, and how far along it (0-1) the nearest point lies.</summary>
    public static float Segment(Vector3 point, Vector3 a, Vector3 b, out float t)
    {
        Vector2 p = new(point.X, point.Y), s = new(a.X, a.Y), e = new(b.X, b.Y);
        Vector2 d = e - s;
        float lengthSquared = d.LengthSquared();
        t = lengthSquared < 1e-3f ? 0 : Math.Clamp(Vector2.Dot(p - s, d) / lengthSquared, 0, 1);
        return Vector2.Distance(p, s + d * t);
    }

    /// <summary>
    /// The walked distance along a path to the point nearest <paramref name="point"/>, and how far off the path the point
    /// is. With a hint (the last known progress, or -1), a nearer match far from the hint loses to one close to it, so a
    /// lane that bends back near itself does not make progress jump.
    /// </summary>
    public static float Project(IReadOnlyList<Vector3> path, IReadOnlyList<float> along, Vector3 point, float hint, out float lateral)
    {
        lateral = float.MaxValue;
        if (path.Count == 0) return 0;
        if (path.Count == 1) { lateral = Flat(point, path[0]); return 0; }
        float best = float.MaxValue, walked = 0;
        for (int i = 1; i < path.Count; i++)
        {
            float distance = Segment(point, path[i - 1], path[i], out float t);
            float at = along[i - 1] + (along[i] - along[i - 1]) * t;
            float score = hint < 0 ? distance : distance + 0.25f * MathF.Abs(at - hint);
            if (score < best) { best = score; walked = at; lateral = distance; }
        }
        return walked;
    }

    /// <summary>The point at a walked distance along a path.</summary>
    public static Vector3 PointAt(IReadOnlyList<Vector3> path, IReadOnlyList<float> along, float walked)
    {
        if (path.Count == 0) return default;
        if (walked <= 0) return path[0];
        for (int i = 1; i < path.Count; i++)
            if (along[i] >= walked)
            {
                float span = along[i] - along[i - 1];
                return span <= 0 ? path[i] : Vector3.Lerp(path[i - 1], path[i], (walked - along[i - 1]) / span);
            }
        return path[^1];
    }
}
