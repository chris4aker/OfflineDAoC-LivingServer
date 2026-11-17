using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// One patrol circuit of the check-spot catalog (docs/RVR-FIELD-1.2.md, "Roaming eights"): the realm that walks it,
/// the spots to walk through first from its portal keep (<see cref="Approach"/>, where the direct walk failed), and the
/// ordered loop of check spots (the last leads back to the first). Immutable.
/// </summary>
public sealed record RvrRoamCircuit
{
    public string Id { get; init; } = string.Empty;
    public ushort Region { get; init; }
    public eRealm Realm { get; init; }
    public string Start { get; init; } = string.Empty;
    public ImmutableArray<string> Approach { get; init; } = [];
    public ImmutableArray<string> Spots { get; init; } = [];
}

/// <summary>What a roaming group does at this turn of its walk (<see cref="RvrRoamCircuits.Step"/>).</summary>
public enum RvrRoamAction : byte
{
    /// <summary>On the way to the current spot.</summary>
    Travel,
    /// <summary>At the spot, looking (the check runs for its dwell).</summary>
    Check,
    /// <summary>Enemy levelling bots near the spot: go for them.</summary>
    Hunt,
    /// <summary>The check is over with no kill (or the kills were long enough ago): on to the next spot.</summary>
    Advance,
    /// <summary>The move-on rule: a kill at this spot, and its time is up. No corpse camping.</summary>
    MoveOn,
    /// <summary>The walk is over (its laps are walked): choose again.</summary>
    Done,
}

/// <summary>
/// Where a roaming eight (or a soloer, duo or stealther at its one spot) is on its walk. <see cref="Route"/> is the
/// approach spots, then the loop from <see cref="LoopStart"/>; a soloer's route is its via spots and then its spot.
/// Immutable; <see cref="RvrRoamCircuits"/> moves it on.
/// </summary>
public sealed record RvrRoamWalk
{
    /// <summary>The circuit id, or "spot:&lt;id&gt;" for a soloer's single spot.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Who walks it (the group's session id or the bot's id): the seed of its rolls.</summary>
    public string Walker { get; init; } = string.Empty;
    public ushort Region { get; init; }
    public ImmutableArray<string> Route { get; init; } = [];
    public int LoopStart { get; init; }
    public int Index { get; init; }
    public int Laps { get; init; }
    public int MaxLaps { get; init; } = 1;
    public long StartedAt { get; init; }
    /// <summary>When the group reached the current spot; 0 while it is on its way.</summary>
    public long ArrivedAt { get; init; }
    public long DwellMilliseconds { get; init; }
    public int DwellMinimumMilliseconds { get; init; } = RvrRoamCircuits.DwellMinimumMilliseconds;
    public int DwellMaximumMilliseconds { get; init; } = RvrRoamCircuits.DwellMaximumMilliseconds;
    /// <summary>The first kill at the current spot; 0 for none.</summary>
    public long KillAt { get; init; }
    public int KillsHere { get; init; }
    public int Kills { get; init; }
    /// <summary>How soon after a kill at a spot this walker leaves (the move-on rule, spread by skill tier).</summary>
    public long MoveOnMilliseconds { get; init; } = RvrRoamCircuits.DefaultMoveOnMinutes * 60_000L;
    /// <summary>Walked with the reserve zones in, because nothing else had prey.</summary>
    public bool ReserveMode { get; init; }

    public string CurrentSpot => Index >= 0 && Index < Route.Length ? Route[Index] : null;
    public bool Done => Route.IsDefaultOrEmpty || Laps >= MaxLaps;
    public bool InApproach => Index < LoopStart;
}

/// <summary>One step of a walk: the walk after it, what to do, and the spot just left (for the log), if any.</summary>
public readonly record struct RvrRoamStep(RvrRoamWalk Walk, RvrRoamAction Action, string LeftSpot = null);

/// <summary>
/// Live prey at a spot for one walking realm: enemy levelling bots (a PvE goal) inside the spot's radius at the last
/// sample, and when any were last seen there.
/// </summary>
public readonly record struct RvrSpotPrey(int Now, long LastSeenAt);

/// <summary>
/// The pure rules of circuits and check spots (work package 6): a walk's route, its dwell at each spot (30-60 s), the
/// hunt and the move-on rule (after a kill at a check spot the group leaves within rvr_roam_move_on_minutes; how soon
/// depends on its skill tier), and the weights that send groups toward where enemy levelling bots are by live count.
/// No game world, no clock, no random number: times and rolls come in.
/// </summary>
public static class RvrRoamCircuits
{
    public const int DwellMinimumMilliseconds = 30_000;
    public const int DwellMaximumMilliseconds = 60_000;
    /// <summary>A soloer or duo works one chokepoint or camp for 2-4 minutes, a stealther camps one for 4-8.</summary>
    public const int SoloDwellMinimumMilliseconds = 120_000, SoloDwellMaximumMilliseconds = 240_000;
    public const int StealthDwellMinimumMilliseconds = 240_000, StealthDwellMaximumMilliseconds = 480_000;
    public const int DefaultMoveOnMinutes = 3;
    /// <summary>However long the prey keeps coming, a group leaves a spot after this (a hunt that never lands a kill too).</summary>
    public const long MaximumStayMilliseconds = 6 * 60_000;
    /// <summary>A walk is this many laps of its loop; then the group weighs the circuits again.</summary>
    public const int CircuitLaps = 2;
    /// <summary>A group already this close to a loop spot joins the loop there instead of walking back to its start.</summary>
    public const float JoinRadius = 4_000;

    public const double PreyWeight = 4.0;
    public const double RecentBonus = 1.0;
    public const double BaseWeight = 0.15;
    /// <summary>"Spots with nobody recently get low weight": prey seen in the last 20 minutes still counts a little.</summary>
    public const long RecentMilliseconds = 20 * 60_000;
    /// <summary>A stealther camp is worth more with friends there (loose packs), up to three, and little past six.</summary>
    public const double PackBonus = 1.5;
    public const int PackFull = 6;

    private const ulong DwellSalt = 0x44574c4c; // "DWLL"

    /// <summary>A deterministic roll in [0, 1) from a text key and a number.</summary>
    public static double Roll(string key, long salt)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in key ?? string.Empty)
            hash = (hash ^ c) * 1099511628211UL;
        return AutonomousBotPersonality.Mix(unchecked((long)hash) ^ salt, DwellSalt) % 1_000_000 / 1_000_000.0;
    }

    /// <summary>How long the walker checks the spot at <paramref name="index"/> on lap <paramref name="lap"/>.</summary>
    public static long DwellFor(RvrRoamWalk walk, int index, int lap)
    {
        int min = Math.Min(walk.DwellMinimumMilliseconds, walk.DwellMaximumMilliseconds);
        int max = Math.Max(walk.DwellMinimumMilliseconds, walk.DwellMaximumMilliseconds);
        return min + (long)(Roll(walk.Walker + "|" + walk.Id, index * 131L + lap) * (max - min));
    }

    /// <summary>
    /// The move-on time for a tier: the setting is the most a group stays after a kill; a veteran group leaves after
    /// 40-70% of it, an average one after 60-90%, a casual one after 80-100%. Never more than the setting.
    /// </summary>
    public static long MoveOnMilliseconds(int minutes, RvrSkillTier tier, double roll)
    {
        if (minutes <= 0)
            return 0;
        (double low, double high) = tier switch
        {
            RvrSkillTier.Veteran => (0.4, 0.7),
            RvrSkillTier.Average => (0.6, 0.9),
            _ => (0.8, 1.0),
        };
        double share = low + Math.Clamp(roll, 0, 1) * (high - low);
        return (long)(Math.Min(1.0, share) * minutes * 60_000L);
    }

    /// <summary>The chance that a group notices enemy levelling bots at its spot on one look (casual groups miss more).</summary>
    public static double NoticeChance(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => 0.95,
        RvrSkillTier.Average => 0.85,
        _ => 0.7,
    };

    /// <summary>
    /// A circuit's route: the approach spots and then the loop. Spots the catalog turned off are left out; so are spots in
    /// the reserve zones unless the walk is in reserve mode (nothing else had prey).
    /// </summary>
    public static ImmutableArray<string> RouteFor(RvrRoamCircuit circuit, RvrCheckSpotCatalog catalog, bool reserveMode, out int loopStart)
    {
        bool Keep(string id) => catalog.Spot(id) is { } spot && catalog.Usable(spot) && (reserveMode || !spot.ReserveZone);
        var approach = circuit.Approach.Where(Keep).ToImmutableArray();
        var loop = circuit.Spots.Where(Keep).ToImmutableArray();
        loopStart = approach.Length;
        return loop.Length == 0 ? ImmutableArray<string>.Empty : approach.AddRange(loop);
    }

    /// <summary>
    /// Starts a walk on a circuit: at the start of its route (through the approach), or, for a group already within
    /// <see cref="JoinRadius"/> of a loop spot (in the circuit's region), at the nearest one.
    /// </summary>
    public static RvrRoamWalk Begin(RvrRoamCircuit circuit, RvrCheckSpotCatalog catalog, string walker, bool reserveMode,
        long moveOnMilliseconds, long now, ushort region, Vector3 position, int maxLaps = CircuitLaps)
    {
        ImmutableArray<string> route = RouteFor(circuit, catalog, reserveMode, out int loopStart);
        int index = 0;
        if (region == circuit.Region && !route.IsEmpty)
        {
            float best = JoinRadius;
            for (int k = loopStart; k < route.Length; k++)
            {
                float distance = Vector2.Distance(new(position.X, position.Y), new(catalog.Spot(route[k]).Stand.X, catalog.Spot(route[k]).Stand.Y));
                if (distance < best)
                {
                    best = distance;
                    index = k;
                }
            }
        }
        var walk = new RvrRoamWalk
        {
            Id = circuit.Id,
            Walker = walker,
            Region = circuit.Region,
            Route = route,
            LoopStart = loopStart,
            Index = index,
            MaxLaps = Math.Max(1, maxLaps),
            StartedAt = now,
            MoveOnMilliseconds = moveOnMilliseconds,
            ReserveMode = reserveMode,
        };
        return walk with { DwellMilliseconds = DwellFor(walk, index, 0) };
    }

    /// <summary>A soloer's, duo's or stealther's walk to one spot: its via spots for the realm, then the spot, once.</summary>
    public static RvrRoamWalk BeginSolo(RvrFrontierPoi spot, RvrCheckSpotCatalog catalog, eRealm realm, string walker, bool stealther,
        long moveOnMilliseconds, long now)
    {
        var via = spot.ViaFor(realm).Where(id => catalog.Spot(id) != null).ToImmutableArray();
        var walk = new RvrRoamWalk
        {
            Id = "spot:" + spot.Id,
            Walker = walker,
            Region = spot.Region,
            Route = via.Add(spot.Id),
            LoopStart = via.Length,
            Index = 0,
            MaxLaps = 1,
            StartedAt = now,
            MoveOnMilliseconds = moveOnMilliseconds,
            DwellMinimumMilliseconds = stealther ? StealthDwellMinimumMilliseconds : SoloDwellMinimumMilliseconds,
            DwellMaximumMilliseconds = stealther ? StealthDwellMaximumMilliseconds : SoloDwellMaximumMilliseconds,
        };
        return walk with { DwellMilliseconds = via.Length > 0 ? 0 : DwellFor(walk, 0, 0) };
    }

    /// <summary>A via spot of a soloer's route is walked through, not checked.</summary>
    private static bool PassesThrough(RvrRoamWalk walk) => walk.Id.StartsWith("spot:", StringComparison.Ordinal) && walk.Index < walk.LoopStart;

    /// <summary>On to the next spot (the loop wraps; after the last lap the walk is done).</summary>
    public static RvrRoamWalk Advance(RvrRoamWalk walk)
    {
        int index = walk.Index + 1, laps = walk.Laps;
        if (index >= walk.Route.Length)
        {
            index = walk.LoopStart;
            laps++;
        }
        RvrRoamWalk next = walk with { Index = index, Laps = laps, ArrivedAt = 0, KillAt = 0, KillsHere = 0 };
        return next with { DwellMilliseconds = PassesThrough(next) ? 0 : DwellFor(next, index, laps) };
    }

    /// <summary>A kill at the current spot (an enemy the group hunted there died). The first one starts the move-on clock.</summary>
    public static RvrRoamWalk RecordKill(RvrRoamWalk walk, long now) =>
        walk with { KillAt = walk.KillAt > 0 ? walk.KillAt : now, KillsHere = walk.KillsHere + 1, Kills = walk.Kills + 1 };

    /// <summary>
    /// One turn of the walk. <paramref name="inArea"/>: the walker is at the current spot (at its standing point or
    /// hunting inside its radius); <paramref name="preyInView"/>: enemy levelling bots the group noticed there.
    /// Order: travel until there; the move-on rule first (a kill and its time is up: leave, whatever else is about);
    /// then hunt what is in view (never past <see cref="MaximumStayMilliseconds"/>); then the dwell; else keep checking.
    /// </summary>
    public static RvrRoamStep Step(RvrRoamWalk walk, long now, bool inArea, bool preyInView)
    {
        if (walk == null || walk.Done)
            return new(walk, RvrRoamAction.Done);
        if (walk.ArrivedAt == 0)
        {
            if (!inArea)
                return new(walk, RvrRoamAction.Travel);
            walk = walk with { ArrivedAt = Math.Max(1, now) }; // 0 means "on the way"
        }
        if (walk.KillAt > 0 && now >= walk.KillAt + walk.MoveOnMilliseconds)
            return Leave(walk, RvrRoamAction.MoveOn);
        if (preyInView && !PassesThrough(walk) && now - walk.ArrivedAt < MaximumStayMilliseconds)
            return new(walk, RvrRoamAction.Hunt);
        if (now - walk.ArrivedAt >= walk.DwellMilliseconds)
            return Leave(walk, walk.KillAt > 0 ? RvrRoamAction.MoveOn : RvrRoamAction.Advance);
        return new(walk, RvrRoamAction.Check);
    }

    private static RvrRoamStep Leave(RvrRoamWalk walk, RvrRoamAction action)
    {
        RvrRoamWalk next = Advance(walk);
        return new(next, next.Done ? RvrRoamAction.Done : action, walk.CurrentSpot);
    }

    // ---- live-count weighting -----------------------------------------------------------------------------------------

    /// <summary>How much a spot draws a walker: live prey weighs most, prey seen there recently a little, nobody hardly.</summary>
    public static double SpotWeight(RvrSpotPrey prey, long now) =>
        BaseWeight + Math.Max(0, prey.Now) * PreyWeight +
        (prey.LastSeenAt > 0 && now - prey.LastSeenAt <= RecentMilliseconds ? RecentBonus : 0);

    /// <summary>Whether a spot has prey now or had some recently (what "something else has prey" means).</summary>
    public static bool HasPrey(RvrSpotPrey prey, long now) =>
        prey.Now > 0 || prey.LastSeenAt > 0 && now - prey.LastSeenAt <= RecentMilliseconds;

    /// <summary>
    /// Reserve mode for a set of candidate spots: true only when no spot outside the reserve zones has prey (now or
    /// recently). Then, and only then, the reserve zones are walked.
    /// </summary>
    public static bool ReserveMode(IEnumerable<RvrFrontierPoi> candidates, Func<RvrFrontierPoi, RvrSpotPrey> prey, long now) =>
        !candidates.Any(spot => !spot.ReserveZone && HasPrey(prey(spot), now));

    /// <summary>A circuit's weight: the sum of its route's spot weights (reserve spots count only in reserve mode).</summary>
    public static double CircuitWeight(RvrRoamCircuit circuit, RvrCheckSpotCatalog catalog, Func<RvrFrontierPoi, RvrSpotPrey> prey,
        long now, bool reserveMode)
    {
        double weight = 0;
        foreach (string id in RouteFor(circuit, catalog, reserveMode, out _))
            weight += SpotWeight(prey(catalog.Spot(id)), now);
        return weight;
    }

    /// <summary>
    /// A soloer's or stealther's weight for a spot. Soloers go where the prey is (chokepoints and camps); stealthers
    /// camp chokepoints and like company: each realm-mate already camping there adds <see cref="PackBonus"/> (up to
    /// three), and a camp of <see cref="PackFull"/> or more is nearly full.
    /// </summary>
    public static double SoloSpotWeight(RvrSpotPrey prey, long now, bool stealther, int packMates)
    {
        if (!stealther)
            return SpotWeight(prey, now);
        if (packMates >= PackFull)
            return BaseWeight;
        return 1.0 + PackBonus * Math.Min(3, Math.Max(0, packMates)) + Math.Max(0, prey.Now);
    }

    /// <summary>A weighted pick: <paramref name="roll"/> in [0, 1). Null for an empty list or all weights zero.</summary>
    public static T Pick<T>(IReadOnlyList<(T Item, double Weight)> weighted, double roll) where T : class
    {
        double total = 0;
        foreach ((T _, double weight) in weighted)
            total += Math.Max(0, weight);
        if (total <= 0)
            return null;
        double at = Math.Clamp(roll, 0, Math.BitDecrement(1d)) * total;
        foreach ((T item, double weight) in weighted)
        {
            at -= Math.Max(0, weight);
            if (at < 0)
                return item;
        }
        return weighted[^1].Item;
    }

    /// <summary>
    /// The live count of one sample: for each spot, how many enemy levelling bots of each realm stand inside its radius.
    /// <paramref name="prey"/> lists the levelling bots in the frontiers (region, position, realm). Pure.
    /// </summary>
    public static Dictionary<string, int[]> Tally(RvrCheckSpotCatalog catalog, IEnumerable<(ushort Region, float X, float Y, eRealm Realm)> prey)
    {
        var counts = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var byRegion = catalog.Frontiers.ToDictionary(frontier => frontier.Region);
        foreach ((ushort region, float x, float y, eRealm realm) in prey)
        {
            if (realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) || !byRegion.TryGetValue(region, out RvrFrontier frontier))
                continue;
            foreach (RvrFrontierPoi spot in frontier.Spots)
            {
                float dx = x - spot.Position.X, dy = y - spot.Position.Y;
                if (dx * dx + dy * dy > (float)spot.Radius * spot.Radius)
                    continue;
                if (!counts.TryGetValue(spot.Id, out int[] byRealm))
                    counts[spot.Id] = byRealm = new int[4];
                byRealm[(int)realm]++;
            }
        }
        return counts;
    }
}

/// <summary>
/// The live sample behind the weights: every <see cref="SampleMilliseconds"/> (never per tick), the first walker to ask
/// counts the levelling gamebots (a PvE goal) in the three frontiers against every spot's radius. Thread-safe: the
/// snapshot is replaced as a whole.
/// </summary>
public static class RvrRoamPrey
{
    public const long SampleMilliseconds = 3 * 60_000;

    private sealed record Snapshot(long At, Dictionary<string, int[]> Now, Dictionary<string, long[]> LastSeen);

    private static Snapshot _snapshot = new(long.MinValue, new(), new());
    private static int _sampling;

    public static long SampledAt => Volatile.Read(ref _snapshot).At;

    /// <summary>Prey at a spot for a walker of <paramref name="walker"/>'s realm (every other realm's levelling bots).</summary>
    public static RvrSpotPrey For(RvrFrontierPoi spot, eRealm walker)
    {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        int now = 0;
        long last = 0;
        if (spot != null && snapshot.Now.TryGetValue(spot.Id, out int[] counts))
            for (int realm = 1; realm <= 3; realm++)
                if (realm != (int)walker) now += counts[realm];
        if (spot != null && snapshot.LastSeen.TryGetValue(spot.Id, out long[] seen))
            for (int realm = 1; realm <= 3; realm++)
                if (realm != (int)walker) last = Math.Max(last, seen[realm]);
        return new(now, last);
    }

    /// <summary>Takes a new sample when the last is older than <see cref="SampleMilliseconds"/>. Cheap otherwise.</summary>
    public static void Refresh(RvrCheckSpotCatalog catalog, long now)
    {
        Snapshot before = Volatile.Read(ref _snapshot);
        if (catalog == null || before.At != long.MinValue && now - before.At < SampleMilliseconds ||
            Interlocked.Exchange(ref _sampling, 1) != 0)
            return;
        try
        {
            using var profile = AiProfile.Scope(AiSystem.RvrTactics);
            var prey = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot != null && bot.IsAlive && bot.CurrentRegionID is 1 or 100 or 200 &&
                              (AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.SoloPve) ||
                               AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve)))
                .Select(bot => (bot.CurrentRegionID, (float)bot.X, (float)bot.Y, bot.Realm))
                .ToArray();
            Dictionary<string, int[]> counts = RvrRoamCircuits.Tally(catalog, prey);
            var lastSeen = before.LastSeen.ToDictionary(pair => pair.Key, pair => (long[])pair.Value.Clone(), StringComparer.Ordinal);
            foreach ((string spot, int[] byRealm) in counts)
            {
                if (!lastSeen.TryGetValue(spot, out long[] seen))
                    lastSeen[spot] = seen = new long[4];
                for (int realm = 1; realm <= 3; realm++)
                    if (byRealm[realm] > 0) seen[realm] = now;
            }
            Volatile.Write(ref _snapshot, new Snapshot(now, counts, lastSeen));
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }

    /// <summary>For tests: replaces the sample.</summary>
    public static void Set(long at, Dictionary<string, int[]> now, Dictionary<string, long[]> lastSeen) =>
        Volatile.Write(ref _snapshot, new Snapshot(at, now ?? new(), lastSeen ?? new()));
}

/// <summary>
/// A per-tag line budget for the work package 6 log tags (RVR_PARTY_FORMED, RVR_ROAM_*, RVR_STEALTH_OPEN): at most so
/// many lines a minute per tag, so a busy frontier cannot flood the owner's log. Thread-safe.
/// </summary>
public static class RvrLogBudget
{
    private sealed class Window
    {
        public long Minute;
        public int Count;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Window> Windows = new(StringComparer.Ordinal);

    /// <summary>True when a line of <paramref name="tag"/> may be written now.</summary>
    public static bool Take(string tag, long now, int perMinute)
    {
        Window window = Windows.GetOrAdd(tag, _ => new Window());
        lock (window)
        {
            long minute = now / 60_000;
            if (window.Minute != minute)
            {
                window.Minute = minute;
                window.Count = 0;
            }
            if (window.Count >= perMinute)
                return false;
            window.Count++;
            return true;
        }
    }
}
