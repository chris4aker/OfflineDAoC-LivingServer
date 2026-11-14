using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Bots spread over a zone's camps instead of piling into one (owner 2026-10-29: 84 level 19-23 bots shared four brown
/// bear and moor wolf camps in Cornwall while other camps stood empty). The camp draw was weighted only by live spawns,
/// blind to how many bots were already there. Now a camp counts as crowded when its bots outnumber half its live
/// monsters (at least two bots allowed); the draw takes only uncrowded camps while any is left, otherwise the least
/// crowded ones. No monster is added or moved, and nothing else about the choice changes.
/// Camps of the same monster close together share their bots and monsters (<see cref="PublishCatalog"/>): a bot at
/// one hunts within <see cref="NeighbourRadius"/> of itself, so they compete for the same spawns.
/// Owner 2026-11-08: bots are counted by WHERE their camp is, not only by its id. Live-cluster camp ids change as their
/// monsters die and respawn (524 hill toad camp ids in one night for 192 toads), and a bot holding an id that left the
/// catalog was invisible to every other bot: 16-25 bots worked the same Silvermine toads at once. Every camp id seen in
/// the last <see cref="KnownSpotMilliseconds"/> keeps its spot, and a camp's bots are all bots camped on the same
/// monster within <see cref="NeighbourRadius"/>. The catalog may also hand in each camp's pool: the distinct live
/// monsters of its name within that radius, so overlapping camps no longer add up the same spawns twice.
/// </summary>
public static class AutonomousCampCompetition
{
    private const long RefreshMilliseconds = 30_000;
    /// <summary>Same-monster camps this close count together (a bot at a camp looks for targets up to 5,000 away).</summary>
    public const int NeighbourRadius = 5_000;
    /// <summary>How long a camp id that left the catalog still locates the bots that hold it (owner 2026-11-08).</summary>
    public const long KnownSpotMilliseconds = 3 * 60 * 60_000;
    private const int MaxKnownSpots = 60_000;
    private static readonly object Sync = new();
    private static Dictionary<string, int> _occupancy = new(StringComparer.OrdinalIgnoreCase);
    // bots per (region, monster) at each occupied outdoor spot, rebuilt with _occupancy; read under Sync
    private static Dictionary<(ushort Region, string Name), List<(int X, int Y, int Bots)>> _spotOccupancy = new();
    private static readonly Dictionary<string, int> OccupancyCache = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextRefresh;

    /// <summary>Where a camp is: region, anchor and monster (lower case).</summary>
    public readonly record struct CampSpot(ushort Region, int X, int Y, string Name, bool Dungeon);

    private static readonly ConcurrentDictionary<string, (CampSpot Spot, long SeenTick)> KnownSpots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A camp's same-monster neighbours (itself included) and their live monsters together.</summary>
    public sealed record Neighbourhood(string[] Ids, int LiveMobs);

    private static volatile Dictionary<string, Neighbourhood> _neighbourhoods = new(StringComparer.OrdinalIgnoreCase);
    private static volatile Dictionary<string, int> _pools = new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeName(string monster) => (monster ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Records which catalog camps share spawns: outdoor camps of the same monster name in the same region within
    /// <see cref="NeighbourRadius"/> of each other (2026-11-06: four Shannon Estuary camps of the same 7 level-1 haunted
    /// driftwood were each counted alone, so up to 18 bots worked 7 monsters). Called when a new catalog is published.
    /// </summary>
    public static void PublishCatalog(IEnumerable<(string Id, ushort Region, int X, int Y, string Monster, int LiveMobs, bool Dungeon)> cells) =>
        PublishCatalogWithPools(cells.Select(cell => (cell.Id, cell.Region, cell.X, cell.Y, cell.Monster, cell.LiveMobs, cell.Dungeon, PoolMobs: -1)));

    /// <summary>The same, with each camp's pool (distinct live monsters of its name within <see cref="NeighbourRadius"/>;
    /// -1 when unknown, which falls back to adding up the neighbours' own counts).</summary>
    public static void PublishCatalogWithPools(IEnumerable<(string Id, ushort Region, int X, int Y, string Monster, int LiveMobs, bool Dungeon, int PoolMobs)> cells)
    {
        var all = cells.ToArray();
        long now = GameLoop.GameLoopTime;
        var pools = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in all)
        {
            if (string.IsNullOrEmpty(cell.Id))
                continue;
            KnownSpots[cell.Id] = (new CampSpot(cell.Region, cell.X, cell.Y, NormalizeName(cell.Monster), cell.Dungeon), now);
            if (cell.PoolMobs >= 0)
                pools[cell.Id] = cell.PoolMobs;
        }
        if (all.Length == 0)
            KnownSpots.Clear();
        else
        {
            // once per catalog: old ids expire; above the cap the oldest retired ids go first, never one in this catalog
            int count = KnownSpots.Count;
            if (count > MaxKnownSpots / 2)
            {
                var current = new HashSet<string>(all.Select(cell => cell.Id).Where(id => !string.IsNullOrEmpty(id)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var pair in KnownSpots)
                    if (!current.Contains(pair.Key) && now - pair.Value.SeenTick > KnownSpotMilliseconds &&
                        KnownSpots.TryRemove(pair.Key, out _))
                        count--;
                if (count > MaxKnownSpots)
                    foreach (var pair in KnownSpots.Where(pair => !current.Contains(pair.Key))
                                 .OrderBy(pair => pair.Value.SeenTick).Take(count - MaxKnownSpots).ToArray())
                        KnownSpots.TryRemove(pair.Key, out _);
            }
        }

        var result = new Dictionary<string, Neighbourhood>(StringComparer.OrdinalIgnoreCase);
        long reach = (long)NeighbourRadius * NeighbourRadius;
        foreach (var group in all
                     .Where(cell => !cell.Dungeon && !string.IsNullOrEmpty(cell.Id) && !string.IsNullOrEmpty(cell.Monster))
                     .GroupBy(cell => (cell.Region, Name: NormalizeName(cell.Monster))))
        {
            var members = group.ToArray();
            if (members.Length < 2)
                continue;
            foreach (var cell in members)
            {
                List<string> ids = null;
                int mobs = 0;
                foreach (var other in members)
                {
                    long dx = (long)cell.X - other.X, dy = (long)cell.Y - other.Y;
                    if (dx * dx + dy * dy > reach)
                        continue;
                    (ids ??= new()).Add(other.Id);
                    mobs += Math.Max(0, other.LiveMobs);
                }
                if (ids is { Count: > 1 })
                    result[cell.Id] = new Neighbourhood(ids.ToArray(), mobs);
            }
        }
        _neighbourhoods = result;
        _pools = pools;
        lock (Sync)
        {
            OccupancyCache.Clear();
            _nextRefresh = 0;   // the next count resolves bots against the new spots
        }
    }

    /// <summary>The spot of a camp id from this or a recent catalog.</summary>
    public static bool TryGetSpot(string campId, out CampSpot spot)
    {
        spot = default;
        if (string.IsNullOrEmpty(campId) || !KnownSpots.TryGetValue(campId, out var known))
            return false;
        spot = known.Spot;
        return true;
    }

    /// <summary>The camp's neighbourhood, or null when no other camp shares its spawns.</summary>
    public static Neighbourhood NeighbourhoodOf(string campId) =>
        string.IsNullOrEmpty(campId) ? null : _neighbourhoods.GetValueOrDefault(campId);

    /// <summary>True when the two camps are the same or share spawns (same monster within <see cref="NeighbourRadius"/>).</summary>
    public static bool SameSpawns(string campId, string otherId)
    {
        if (string.IsNullOrEmpty(campId) || string.IsNullOrEmpty(otherId))
            return false;
        if (string.Equals(campId, otherId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (NeighbourhoodOf(campId)?.Ids.Contains(otherId, StringComparer.OrdinalIgnoreCase) == true)
            return true;
        // the bot's own camp may have left the catalog since it was chosen: compare the spots
        return TryGetSpot(campId, out CampSpot a) && TryGetSpot(otherId, out CampSpot b) && !a.Dungeon && !b.Dungeon &&
               Shares(a, b.Region, b.X, b.Y, b.Name);
    }

    private static bool Shares(CampSpot spot, ushort region, int x, int y, string name)
    {
        if (spot.Region != region || !string.Equals(spot.Name, name, StringComparison.Ordinal))
            return false;
        long dx = (long)spot.X - x, dy = (long)spot.Y - y;
        return dx * dx + dy * dy <= (long)NeighbourRadius * NeighbourRadius;
    }

    /// <summary>The live monsters a camp's bots compete for: its pool when the catalog gave one, otherwise its
    /// neighbourhood's, or its own.</summary>
    public static int SharedLiveMobs(string campId, int ownLiveMobs)
    {
        if (!string.IsNullOrEmpty(campId) && _pools.TryGetValue(campId, out int pool))
            return Math.Max(pool, ownLiveMobs);
        return NeighbourhoodOf(campId)?.LiveMobs is int shared ? Math.Max(shared, ownLiveMobs) : ownLiveMobs;
    }

    /// <summary>Pure rule: more bots than half the camp's live monsters (two always fit).</summary>
    public static bool Crowded(int bots, int liveMobs) => bots > Math.Max(2, liveMobs / 2);

    /// <summary>How crowded a camp is: bots per live monster.</summary>
    public static double Pressure(int bots, int liveMobs) => bots / (double)Math.Max(1, liveMobs);

    /// <summary>
    /// Bots at the camp and at every camp (current or recently retired id) of the same monster within
    /// <see cref="NeighbourRadius"/>. Cached per camp until the next count, so the planner's repeated calls are lookups.
    /// </summary>
    public static int Occupancy(string campId)
    {
        if (string.IsNullOrEmpty(campId))
            return 0;
        Refresh();
        bool located = TryGetSpot(campId, out CampSpot spot) && !spot.Dungeon;
        lock (Sync)
        {
            if (OccupancyCache.TryGetValue(campId, out int cached))
                return cached;
            int bots;
            if (!located)
                bots = _occupancy.GetValueOrDefault(campId);
            else
            {
                bots = 0;
                if (_spotOccupancy.TryGetValue((spot.Region, spot.Name), out var spots))
                    foreach (var (x, y, count) in spots)
                    {
                        long dx = (long)spot.X - x, dy = (long)spot.Y - y;
                        if (dx * dx + dy * dy <= (long)NeighbourRadius * NeighbourRadius)
                            bots += count;
                    }
                // a bot whose camp id is unknown (no spot) still counts on its own id
                bots = Math.Max(bots, _occupancy.GetValueOrDefault(campId));
            }
            OccupancyCache[campId] = bots;
            return bots;
        }
    }

    private static void Refresh()
    {
        long now = GameLoop.GameLoopTime;
        lock (Sync)
        {
            if (now < _nextRefresh)
                return;
            _nextRefresh = now + RefreshMilliseconds;
        }
        // A party counts as all its members (owner 2026-11-05: only the leader carries the camp id, so a party of 8
        // counted as one bot and solos piled in beside it); a bot on a corpse run keeps its place.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<Group>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            string camp = bot?.PersistentRecord?.CurrentCampId;
            if (bot == null || string.IsNullOrEmpty(camp))
                continue;
            int weight = bot.Group is { } group ? (groups.Add(group) ? Math.Max(1, (int)group.MemberCount) : 0) : 1;
            if (weight > 0)
                counts[camp] = counts.GetValueOrDefault(camp) + weight;
        }
        var spots = new Dictionary<(ushort Region, string Name), List<(int X, int Y, int Bots)>>();
        foreach (var (camp, bots) in counts)
            AddSpot(spots, camp, bots);
        lock (Sync)
        {
            _occupancy = counts;   // a new dictionary: NoteAssigned writes to the current one under the same lock
            _spotOccupancy = spots;
            OccupancyCache.Clear();
        }
    }

    private static void AddSpot(Dictionary<(ushort Region, string Name), List<(int X, int Y, int Bots)>> spots, string camp, int bots)
    {
        if (!TryGetSpot(camp, out CampSpot spot) || spot.Dungeon)
            return;
        if (!spots.TryGetValue((spot.Region, spot.Name), out var list))
            spots[(spot.Region, spot.Name)] = list = new();
        list.Add((spot.X, spot.Y, bots));
    }

    /// <summary>
    /// A camp just chosen counts at once (owner 2026-11-05, reset-day S36: 47 bots on a 9-highwayman camp - the counts
    /// were 30 s old, so a wave of bots planning together all saw the camp empty).
    /// </summary>
    public static void NoteAssigned(string campId, int bots = 1)
    {
        if (string.IsNullOrEmpty(campId) || bots <= 0)
            return;
        Refresh();
        lock (Sync)
        {
            _occupancy[campId] = _occupancy.GetValueOrDefault(campId) + bots;
            AddSpot(_spotOccupancy, campId, bots);
            OccupancyCache.Clear();
        }
    }

    /// <summary>
    /// The camps to draw from: the uncrowded ones, or when every camp is crowded the least crowded third (at least one).
    /// <paramref name="own"/> is the bot's own current camp, which does not count itself as competition.
    /// </summary>
    /// <summary>Only the uncrowded camps (none when every one is crowded).</summary>
    public static T[] Quiet<T>(T[] camps, Func<T, string> id, Func<T, int> liveMobs, string own = null, int partySize = 1)
    {
        int Bots(T camp)
        {
            int count = Occupancy(id(camp));
            if (own != null && SameSpawns(id(camp), own))
                count = Math.Max(0, count - partySize);
            return count + partySize;
        }
        int Mobs(T camp) => SharedLiveMobs(id(camp), liveMobs(camp));
        return camps.Where(camp => !Crowded(Bots(camp), Mobs(camp))).ToArray();
    }

    public static T[] PreferQuiet<T>(T[] camps, Func<T, string> id, Func<T, int> liveMobs, string own = null, int partySize = 1)
    {
        if (camps.Length <= 1)
            return camps;
        int Bots(T camp)
        {
            int count = Occupancy(id(camp));
            if (own != null && SameSpawns(id(camp), own))
                count = Math.Max(0, count - partySize);
            return count + partySize;
        }
        int Mobs(T camp) => SharedLiveMobs(id(camp), liveMobs(camp));
        T[] quiet = camps.Where(camp => !Crowded(Bots(camp), Mobs(camp))).ToArray();
        if (quiet.Length > 0)
            return quiet;
        return camps.OrderBy(camp => Pressure(Bots(camp), Mobs(camp)))
            .Take(Math.Max(1, camps.Length / 3)).ToArray();
    }
}
