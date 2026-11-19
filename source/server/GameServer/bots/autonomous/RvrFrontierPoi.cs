using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What kind of place a check spot is (rvr-checkspots.json "type").</summary>
public enum RvrSpotKind : byte
{
    Camp,
    Chokepoint,
    Fringe,
    Named,
    DungeonDoor,
}

/// <summary>
/// One check spot of the old frontiers (docs/RVR-FIELD-1.2.md, "Feet"; data: bots/autonomous/data/rvr-checkspots.json,
/// proven on the navmeshes by UT_RvrCheckSpotCatalog, tests only): a milegate, hastener or road crossing, a levelling
/// camp, a dungeon door, a named monster or a fringe camp. A group stands at <see cref="Stand"/> (floor with a local
/// exit, out of aggro range of the spot's own monsters) and looks over <see cref="Radius"/> around
/// <see cref="Position"/>. <see cref="Via"/> lists, per realm, the spots to walk through when the direct walk from that
/// realm's portal keep failed. Immutable.
/// </summary>
public sealed record RvrFrontierPoi
{
    public string Id { get; init; } = string.Empty;
    public RvrSpotKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public ushort Region { get; init; }
    /// <summary>The frontier zone the spot lies in (from the catalog's zone rectangles); 0 when outside them all.</summary>
    public int ZoneId { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Stand { get; init; }
    public int Radius { get; init; }
    public int MinLevel { get; init; }
    public int MaxLevel { get; init; }
    /// <summary>In Forest Sauvage, Snowdonia, Mount Collory or Cruachan Gorge: used only when nothing else has prey.</summary>
    public bool ReserveZone { get; init; }
    /// <summary>On at least one patrol circuit.</summary>
    public bool OnCircuit { get; init; }
    public ImmutableDictionary<eRealm, ImmutableArray<string>> Via { get; init; } = ImmutableDictionary<eRealm, ImmutableArray<string>>.Empty;

    /// <summary>The spots a soloer or duo works: chokepoints and camps (owner design, "Soloers and stealthers").</summary>
    public bool ForSoloers => Kind is RvrSpotKind.Chokepoint or RvrSpotKind.Camp;

    /// <summary>The spots stealthers camp in loose packs: milegates, hasteners and road crossings.</summary>
    public bool ForStealthers => Kind == RvrSpotKind.Chokepoint;

    /// <summary>The via spots a bot of <paramref name="realm"/> walks through to reach this spot (empty: walk direct).</summary>
    public ImmutableArray<string> ViaFor(eRealm realm) => Via.TryGetValue(realm, out ImmutableArray<string> via) ? via : ImmutableArray<string>.Empty;
}

/// <summary>One old frontier of the catalog: its zones, the enemy realms' portal-keep entries, spots and circuits.</summary>
public sealed record RvrFrontier
{
    public ushort Region { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImmutableArray<(int Id, int X, int Y, int Width, int Height)> Zones { get; init; } = [];
    public ImmutableDictionary<eRealm, Vector3> Entries { get; init; } = ImmutableDictionary<eRealm, Vector3>.Empty;
    public ImmutableArray<RvrFrontierPoi> Spots { get; init; } = [];
    public ImmutableArray<RvrRoamCircuit> Circuits { get; init; } = [];

    /// <summary>The catalog zone that holds a point, or 0.</summary>
    public int ZoneAt(float x, float y)
    {
        foreach ((int id, int zx, int zy, int width, int height) in Zones)
            if (x >= zx && x < zx + width && y >= zy && y < zy + height)
                return id;
        return 0;
    }
}

/// <summary>
/// The check-spot and circuit catalog, parsed once (pure: <see cref="Parse"/> reads a JSON text), plus what the
/// start-up validation turned off. A rejected spot is never used; a circuit with a rejected leg, approach or spot is
/// never walked; a spot a realm's walk could not reach is not used by that realm. Rejections only grow and are
/// published atomically, so readers on any thread see a consistent set.
/// </summary>
public sealed class RvrCheckSpotCatalog
{
    /// <summary>Forest Sauvage, Snowdonia, Mount Collory, Cruachan Gorge: nobody to hunt in the 2026-11-16 survey.</summary>
    public static readonly ImmutableHashSet<int> ReserveZones = [11, 12, 210, 211];

    public int Version { get; init; }
    public string ProofHash { get; init; } = string.Empty;
    public ImmutableArray<RvrFrontier> Frontiers { get; init; } = [];
    /// <summary>Entries dropped while parsing (an unknown type, a circuit naming an unknown spot), one line each.</summary>
    public ImmutableArray<string> ParseRejections { get; init; } = [];

    private ImmutableDictionary<string, RvrFrontierPoi> _spots;
    private ImmutableHashSet<string> _rejected = ImmutableHashSet<string>.Empty;

    public int SpotCount => Frontiers.Sum(frontier => frontier.Spots.Length);
    public int CircuitCount => Frontiers.Sum(frontier => frontier.Circuits.Length);
    public int RejectedCount => Volatile.Read(ref _rejected).Count;

    public RvrFrontier Frontier(ushort region) => Frontiers.FirstOrDefault(frontier => frontier.Region == region);

    public RvrFrontierPoi Spot(string id)
    {
        _spots ??= Frontiers.SelectMany(frontier => frontier.Spots).GroupBy(spot => spot.Id)
            .ToImmutableDictionary(group => group.Key, group => group.First());
        return id != null && _spots.TryGetValue(id, out RvrFrontierPoi spot) ? spot : null;
    }

    public static string SpotKey(string id) => "spot:" + id;
    public static string CircuitKey(string id) => "circuit:" + id;
    public static string ReachKey(string spotId, eRealm realm) => $"reach:{spotId}@{realm}";

    /// <summary>Turns a spot, circuit or a realm's reach off for good. True when it was not off before.</summary>
    public bool Reject(string key)
    {
        while (true)
        {
            ImmutableHashSet<string> before = Volatile.Read(ref _rejected);
            if (before.Contains(key))
                return false;
            if (Interlocked.CompareExchange(ref _rejected, before.Add(key), before) == before)
                return true;
        }
    }

    public bool IsRejected(string key) => Volatile.Read(ref _rejected).Contains(key);

    /// <summary>A spot nobody's check turned off (and, with a realm, that realm can reach).</summary>
    public bool Usable(RvrFrontierPoi spot, eRealm realm = eRealm.None) => spot != null && !IsRejected(SpotKey(spot.Id)) &&
        (realm == eRealm.None || !IsRejected(ReachKey(spot.Id, realm))) &&
        spot.ViaFor(realm).All(via => Spot(via) is { } step && !IsRejected(SpotKey(step.Id)));

    /// <summary>A circuit whose approach, legs and spots all passed (or are not checked yet).</summary>
    public bool Usable(RvrRoamCircuit circuit) => circuit != null && !IsRejected(CircuitKey(circuit.Id)) &&
        circuit.Spots.All(id => Spot(id) is { } spot && !IsRejected(SpotKey(spot.Id))) &&
        circuit.Approach.All(id => Spot(id) is { } spot && !IsRejected(SpotKey(spot.Id)));

    private static eRealm ParseRealm(string name) => name?.Trim() switch
    {
        "Albion" => eRealm.Albion,
        "Midgard" => eRealm.Midgard,
        "Hibernia" => eRealm.Hibernia,
        _ => eRealm.None,
    };

    public static bool TryParseKind(string type, out RvrSpotKind kind)
    {
        kind = type?.Trim().ToLowerInvariant() switch
        {
            "camp" => RvrSpotKind.Camp,
            "chokepoint" => RvrSpotKind.Chokepoint,
            "fringe" => RvrSpotKind.Fringe,
            "named" => RvrSpotKind.Named,
            "dungeon door" or "dungeon" => RvrSpotKind.DungeonDoor,
            _ => (RvrSpotKind)255,
        };
        return (byte)kind != 255;
    }

    private static Vector3 Point(JsonElement element) =>
        new(element.GetProperty("x").GetSingle(), element.GetProperty("y").GetSingle(), element.GetProperty("z").GetSingle());

    /// <summary>
    /// Reads rvr-checkspots.json (schema rvr-checkspots/1). Pure. Throws on text that is not the catalog at all; drops,
    /// with a line in <see cref="ParseRejections"/>, a spot of an unknown type and a circuit that names an unknown spot or
    /// realm, so one bad entry does not cost the rest.
    /// </summary>
    public static RvrCheckSpotCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var rejections = ImmutableArray.CreateBuilder<string>();
        var frontiers = ImmutableArray.CreateBuilder<RvrFrontier>();
        foreach (JsonElement frontier in root.GetProperty("frontiers").EnumerateArray())
        {
            ushort region = (ushort)frontier.GetProperty("region").GetInt32();
            var zones = frontier.GetProperty("zones").EnumerateArray().Select(zone => (zone.GetProperty("id").GetInt32(),
                zone.GetProperty("x").GetInt32(), zone.GetProperty("y").GetInt32(), zone.GetProperty("width").GetInt32(),
                zone.GetProperty("height").GetInt32())).ToImmutableArray();
            var shell = new RvrFrontier { Region = region, Zones = zones };
            var entries = ImmutableDictionary.CreateBuilder<eRealm, Vector3>();
            foreach (JsonElement entry in frontier.GetProperty("entries").EnumerateArray())
                if (ParseRealm(entry.GetProperty("realm").GetString()) is var realm and not eRealm.None)
                    entries[realm] = Point(entry);

            var circuitSpots = new HashSet<string>(StringComparer.Ordinal);
            if (frontier.TryGetProperty("circuits", out JsonElement circuitList))
                foreach (JsonElement circuit in circuitList.EnumerateArray())
                    foreach (JsonElement id in circuit.GetProperty("spots").EnumerateArray())
                        circuitSpots.Add(id.GetString());

            var spots = ImmutableArray.CreateBuilder<RvrFrontierPoi>();
            foreach (JsonElement spot in frontier.GetProperty("spots").EnumerateArray())
            {
                string id = spot.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id) || !TryParseKind(spot.GetProperty("type").GetString(), out RvrSpotKind kind))
                {
                    rejections.Add($"spot {id} unknown type");
                    continue;
                }
                Vector3 position = Point(spot);
                int min = 0, max = 0;
                if (spot.TryGetProperty("levels", out JsonElement levels) && levels.GetArrayLength() == 2)
                {
                    min = levels[0].GetInt32();
                    max = levels[1].GetInt32();
                }
                var via = ImmutableDictionary.CreateBuilder<eRealm, ImmutableArray<string>>();
                if (spot.TryGetProperty("via", out JsonElement viaElement))
                    foreach (JsonProperty realmVia in viaElement.EnumerateObject())
                        if (ParseRealm(realmVia.Name) is var realm and not eRealm.None)
                            via[realm] = realmVia.Value.EnumerateArray().Select(step => step.GetString()).ToImmutableArray();
                int zone = shell.ZoneAt(position.X, position.Y);
                spots.Add(new RvrFrontierPoi
                {
                    Id = id,
                    Kind = kind,
                    Label = spot.TryGetProperty("label", out JsonElement label) ? label.GetString() ?? id : id,
                    Region = region,
                    ZoneId = zone,
                    Position = position,
                    Stand = spot.TryGetProperty("stand", out JsonElement stand) ? Point(stand) : position,
                    Radius = spot.TryGetProperty("radius", out JsonElement radius) ? radius.GetInt32() : 800,
                    MinLevel = min,
                    MaxLevel = max,
                    ReserveZone = ReserveZones.Contains(zone),
                    OnCircuit = circuitSpots.Contains(id),
                    Via = via.ToImmutable(),
                });
            }
            var known = spots.Select(spot => spot.Id).ToHashSet(StringComparer.Ordinal);
            var circuits = ImmutableArray.CreateBuilder<RvrRoamCircuit>();
            if (frontier.TryGetProperty("circuits", out circuitList))
                foreach (JsonElement circuit in circuitList.EnumerateArray())
                {
                    string id = circuit.GetProperty("id").GetString();
                    eRealm realm = ParseRealm(circuit.GetProperty("realm").GetString());
                    var loop = circuit.GetProperty("spots").EnumerateArray().Select(step => step.GetString()).ToImmutableArray();
                    var approach = circuit.TryGetProperty("approach", out JsonElement steps)
                        ? steps.EnumerateArray().Select(step => step.GetString()).ToImmutableArray()
                        : ImmutableArray<string>.Empty;
                    string unknown = loop.Concat(approach).FirstOrDefault(step => !known.Contains(step));
                    if (realm == eRealm.None || loop.Length < 2 || unknown != null)
                    {
                        rejections.Add($"circuit {id} {(realm == eRealm.None ? "unknown realm" : unknown != null ? "unknown spot " + unknown : "too short")}");
                        continue;
                    }
                    circuits.Add(new RvrRoamCircuit
                    {
                        Id = id,
                        Region = region,
                        Realm = realm,
                        Start = circuit.TryGetProperty("start", out JsonElement start) ? start.GetString() ?? string.Empty : string.Empty,
                        Approach = approach,
                        Spots = loop,
                    });
                }
            frontiers.Add(shell with
            {
                Name = frontier.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
                Entries = entries.ToImmutable(),
                Spots = spots.ToImmutable(),
                Circuits = circuits.ToImmutable(),
            });
        }
        return new RvrCheckSpotCatalog
        {
            Version = root.TryGetProperty("version", out JsonElement version) ? version.GetInt32() : 0,
            ProofHash = root.TryGetProperty("proof_hash", out JsonElement hash) ? hash.GetString() ?? string.Empty : string.Empty,
            Frontiers = frontiers.ToImmutable(),
            ParseRejections = rejections.ToImmutable(),
        };
    }
}

/// <summary>What one start-up check walks (<see cref="RvrCheckSpotValidation"/>).</summary>
public enum RvrCheckSpotCheckKind : byte
{
    /// <summary>The standing point is floor with a local exit.</summary>
    Stand,
    /// <summary>A circuit's approach: the walking realm's portal-keep entry, the approach spots, the first spot.</summary>
    Approach,
    /// <summary>One leg of a circuit, from a spot to the next (the last back to the first), in walking order.</summary>
    Leg,
    /// <summary>A spot from an enemy realm's portal-keep entry, through the spot's via list for that realm.</summary>
    Reach,
}

/// <summary>One start-up check: what it turns off when it fails (<see cref="RejectKey"/>) and the points it walks.</summary>
public sealed record RvrCheckSpotCheck(RvrCheckSpotCheckKind Kind, ushort Region, string Subject, string Detail,
    eRealm Realm, ImmutableArray<Vector3> Points, ImmutableArray<string> SpotsUsed)
{
    public string RejectKey => Kind switch
    {
        RvrCheckSpotCheckKind.Stand => RvrCheckSpotCatalog.SpotKey(Subject),
        RvrCheckSpotCheckKind.Reach => RvrCheckSpotCatalog.ReachKey(Subject, Realm),
        _ => RvrCheckSpotCatalog.CircuitKey(Subject),
    };
}

/// <summary>
/// The start-up validation of the catalog (WP6), as pure steps: <see cref="Plan"/> lists every check (stands first, then
/// each circuit's approach and legs, then each spot's reach from both enemy entries), <see cref="Skip"/> says when a
/// check is moot because what it would turn off is already off (or it walks through a spot that is), and
/// <see cref="Apply"/> records a result. The live runner (<see cref="RvrCheckSpots"/>) walks one check at a time in the
/// background with a pause between, so the server never stalls on it.
/// </summary>
public static class RvrCheckSpotValidation
{
    public static ImmutableArray<RvrCheckSpotCheck> Plan(RvrCheckSpotCatalog catalog)
    {
        var checks = ImmutableArray.CreateBuilder<RvrCheckSpotCheck>();
        foreach (RvrFrontier frontier in catalog.Frontiers)
            foreach (RvrFrontierPoi spot in frontier.Spots)
                checks.Add(new(RvrCheckSpotCheckKind.Stand, frontier.Region, spot.Id, "stand", eRealm.None, [spot.Stand], [spot.Id]));
        foreach (RvrFrontier frontier in catalog.Frontiers)
            foreach (RvrRoamCircuit circuit in frontier.Circuits)
            {
                if (frontier.Entries.TryGetValue(circuit.Realm, out Vector3 entry))
                {
                    var approachSpots = circuit.Approach.Add(circuit.Spots[0]);
                    checks.Add(new(RvrCheckSpotCheckKind.Approach, frontier.Region, circuit.Id, "approach", circuit.Realm,
                        approachSpots.Select(id => catalog.Spot(id).Stand).Prepend(entry).ToImmutableArray(), approachSpots));
                }
                for (int k = 0; k < circuit.Spots.Length; k++)
                {
                    string from = circuit.Spots[k], to = circuit.Spots[(k + 1) % circuit.Spots.Length];
                    checks.Add(new(RvrCheckSpotCheckKind.Leg, frontier.Region, circuit.Id, $"leg {from} > {to}", circuit.Realm,
                        [catalog.Spot(from).Stand, catalog.Spot(to).Stand], [from, to]));
                }
            }
        foreach (RvrFrontier frontier in catalog.Frontiers)
            foreach (RvrFrontierPoi spot in frontier.Spots)
                foreach ((eRealm realm, Vector3 entry) in frontier.Entries.OrderBy(pair => pair.Key))
                {
                    var via = spot.ViaFor(realm).Where(id => catalog.Spot(id) != null).ToImmutableArray();
                    checks.Add(new(RvrCheckSpotCheckKind.Reach, frontier.Region, spot.Id, $"reach via {via.Length}", realm,
                        via.Select(id => catalog.Spot(id).Stand).Prepend(entry).Append(spot.Stand).ToImmutableArray(), via.Add(spot.Id)));
                }
        return checks.ToImmutable();
    }

    /// <summary>A check that cannot change anything: its subject is already off, or it walks through a spot that is.</summary>
    public static bool Skip(RvrCheckSpotCatalog catalog, RvrCheckSpotCheck check) =>
        catalog.IsRejected(check.RejectKey) ||
        check.Kind != RvrCheckSpotCheckKind.Stand && check.SpotsUsed.Any(id => catalog.IsRejected(RvrCheckSpotCatalog.SpotKey(id)));

    /// <summary>Records a result. Returns the RVR_CHECKSPOT_REJECTED line for a failure that turned something off, else null.</summary>
    public static string Apply(RvrCheckSpotCatalog catalog, RvrCheckSpotCheck check, bool passed, string why = null)
    {
        if (passed || !catalog.Reject(check.RejectKey))
            return null;
        string what = check.Kind switch
        {
            RvrCheckSpotCheckKind.Stand => "spot",
            RvrCheckSpotCheckKind.Reach => "reach",
            _ => "circuit",
        };
        return $"RVR_CHECKSPOT_REJECTED kind={check.Kind} off={what} id={check.Subject} region={check.Region} " +
               (check.Realm != eRealm.None ? $"realm={check.Realm} " : "") + $"check=\"{check.Detail}\" reason={why ?? "walk failed"}";
    }
}

/// <summary>
/// The embedded catalog, loaded lazily on first use (as AutonomousCapnBryGoalCatalog and AutonomousDarknessFallsNavigation
/// read their embedded data), and its start-up validation: started once, from the first RvR formation pass, on a
/// background thread that walks one check on the live navmesh every <see cref="CheckPauseMilliseconds"/> (about ten
/// minutes for the whole catalog) and turns off any spot, reach or circuit that fails. Until a check has run, its
/// subject counts as usable: the catalog was proven on these meshes when it was built (tests only).
/// </summary>
public static class RvrCheckSpots
{
    private static readonly Logger Log = LoggerManager.Create(typeof(RvrCheckSpots));
    private const string ResourceSuffix = "rvr-checkspots.json";
    public const int CheckPauseMilliseconds = 1_500;

    private static readonly Lazy<RvrCheckSpotCatalog> Data = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _validationStarted;
    private static volatile bool _validated;

    /// <summary>The catalog, or null when the embedded file is missing or unreadable (then nothing roams by it).</summary>
    public static RvrCheckSpotCatalog Catalog => Data.Value;

    public static bool Validated => _validated;

    /// <summary>Writes a line; a logger that is not set up (a test run) never costs the catalog its load.</summary>
    private static void Note(bool warn, string line)
    {
        try
        {
            if (warn) Log.Warn(line);
            else Log.Info(line);
        }
        catch (NullReferenceException) { }
    }

    private static RvrCheckSpotCatalog Load()
    {
        try
        {
            var assembly = typeof(RvrCheckSpots).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(resource => resource.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            if (name == null)
            {
                Note(true, "RVR_CHECKSPOT_LOADED ok=False reason=\"embedded rvr-checkspots.json missing\"");
                return null;
            }
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
            RvrCheckSpotCatalog catalog = RvrCheckSpotCatalog.Parse(reader.ReadToEnd());
            foreach (string rejection in catalog.ParseRejections)
                Note(true, $"RVR_CHECKSPOT_REJECTED kind=Parse {rejection}");
            Note(false, $"RVR_CHECKSPOT_LOADED ok=True version={catalog.Version} frontiers={catalog.Frontiers.Length} " +
                     $"spots={catalog.SpotCount} circuits={catalog.CircuitCount} " +
                     $"reserve={catalog.Frontiers.Sum(f => f.Spots.Count(s => s.ReserveZone))} parseRejected={catalog.ParseRejections.Length} " +
                     $"proof={catalog.ProofHash}");
            return catalog;
        }
        catch (Exception exception)
        {
            Note(true, $"RVR_CHECKSPOT_LOADED ok=False reason=\"{exception.GetType().Name}: {exception.Message}\"");
            return null;
        }
    }

    /// <summary>Starts the background validation once, when the navmeshes are up. Cheap to call often.</summary>
    public static void EnsureValidation()
    {
        if (Volatile.Read(ref _validationStarted) != 0 || !PathfindingProvider.Instance.IsAvailable)
            return;
        RvrCheckSpotCatalog catalog = Catalog;
        if (catalog == null || Interlocked.Exchange(ref _validationStarted, 1) != 0)
            return;
        var thread = new Thread(() => Validate(catalog)) { IsBackground = true, Name = "rvr-checkspot-validation", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private static void Validate(RvrCheckSpotCatalog catalog)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        int walked = 0, failed = 0;
        try
        {
            var nodes = new WrappedPathfindingNode[4096];
            foreach (RvrCheckSpotCheck check in RvrCheckSpotValidation.Plan(catalog))
            {
                if (RvrCheckSpotValidation.Skip(catalog, check))
                    continue;
                bool passed;
                string why = null;
                try { passed = RvrCheckSpotWalker.Passes(check, nodes, out why); }
                catch (Exception exception)
                {
                    passed = false;
                    why = exception.GetType().Name;
                }
                walked++;
                if (RvrCheckSpotValidation.Apply(catalog, check, passed, why) is { } line)
                {
                    failed++;
                    Note(true, line);
                }
                Thread.Sleep(CheckPauseMilliseconds);
            }
        }
        catch (Exception exception)
        {
            Note(true, $"RVR_CHECKSPOT_VALIDATED ok=False reason=\"{exception.GetType().Name}: {exception.Message}\" walked={walked}");
            return;
        }
        _validated = true;
        Note(false, $"RVR_CHECKSPOT_VALIDATED ok=True walked={walked} failed={failed} off={catalog.RejectedCount} " +
                 $"usableCircuits={catalog.Frontiers.Sum(f => f.Circuits.Count(catalog.Usable))}/{catalog.CircuitCount} " +
                 $"usableSpots={catalog.Frontiers.Sum(f => f.Spots.Count(s => catalog.Usable(s)))}/{catalog.SpotCount} " +
                 $"seconds={(int)started.Elapsed.TotalSeconds}");
    }
}

/// <summary>
/// The live walk behind one start-up check, as UT_RvrCheckSpotCatalog walks it on the shipped meshes: a stand must be
/// floor with a local exit; a walk goes zone by zone (AutonomousZoneItinerary.ProbeNextStep between zones, Detour and
/// then the zone road route inside one). Needs the world (regions, zones, navmeshes).
/// </summary>
internal static class RvrCheckSpotWalker
{
    public static bool Passes(RvrCheckSpotCheck check, WrappedPathfindingNode[] nodes, out string why)
    {
        why = null;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Region region = WorldMgr.GetRegion(check.Region);
        if (region == null || !nav.IsAvailable)
        {
            why = "no region or navmesh";
            return true; // nothing to check against: keep it (the frontier is not loaded on this server)
        }
        if (check.Kind == RvrCheckSpotCheckKind.Stand)
        {
            Vector3 stand = check.Points[0];
            Zone zone = region.GetZone((int)stand.X, (int)stand.Y);
            bool ok = zone != null && nav.HasNavmesh(zone) && AutonomousNavigationSurface.TryFloor(nav, zone, stand, out Vector3 floor) &&
                      AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor);
            if (!ok) why = "no floor or local exit";
            return ok;
        }
        bool walked = Walk(nav, region, check.Points, nodes);
        if (!walked) why = "walk failed";
        return walked;
    }

    private static bool Walk(IPathfindingMgr nav, Region region, ImmutableArray<Vector3> points, WrappedPathfindingNode[] nodes)
    {
        List<Zone> zones = region.Zones;
        Vector3 position = points[0];
        bool Leg(Zone zone, Vector3 to)
        {
            AutonomousNavigationSurface.TryFloor(nav, zone, position, out Vector3 from);
            PathfindingResult path = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            if (Pathfinder.TryRoadPath(nav, zone, from, to, nav.DefaultFilters, path, nodes, nodes.Length, out var road)) path = road;
            if (path.NodeCount < 1) return false;
            position = nodes[path.NodeCount - 1].Position;
            return path.Status == PathfindingStatus.PathFound || Vector3.Distance(position, to) <= 64;
        }
        for (int k = 1; k < points.Length; k++)
        {
            Zone target = region.GetZone((int)points[k].X, (int)points[k].Y);
            for (int crossings = 0; region.GetZone((int)position.X, (int)position.Y) != target; crossings++)
            {
                Zone from = region.GetZone((int)position.X, (int)position.Y);
                if (crossings >= 16 || from == null || target == null ||
                    !AutonomousZoneItinerary.ProbeNextStep(zones, region.ID, from, target, position, points[k], nav, out var step, new List<string>()) ||
                    !Leg(from, step.Inside)) return false;
                position = step.Outside;
            }
            if (target == null || !Leg(target, points[k])) return false;
        }
        return true;
    }
}
