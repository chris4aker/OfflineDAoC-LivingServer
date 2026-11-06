using System;
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
/// </summary>
public static class AutonomousCampCompetition
{
    private const long RefreshMilliseconds = 30_000;
    /// <summary>Same-monster camps this close count together (a bot at a camp looks for targets up to 5,000 away).</summary>
    public const int NeighbourRadius = 5_000;
    private static readonly object Sync = new();
    private static Dictionary<string, int> _occupancy = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextRefresh;

    /// <summary>A camp's same-monster neighbours (itself included) and their live monsters together.</summary>
    public sealed record Neighbourhood(string[] Ids, int LiveMobs);

    private static volatile Dictionary<string, Neighbourhood> _neighbourhoods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records which catalog camps share spawns: outdoor camps of the same monster name in the same region within
    /// <see cref="NeighbourRadius"/> of each other (2026-11-06: four Shannon Estuary camps of the same 7 level-1 haunted
    /// driftwood were each counted alone, so up to 18 bots worked 7 monsters). Called when a new catalog is published.
    /// </summary>
    public static void PublishCatalog(IEnumerable<(string Id, ushort Region, int X, int Y, string Monster, int LiveMobs, bool Dungeon)> cells)
    {
        var result = new Dictionary<string, Neighbourhood>(StringComparer.OrdinalIgnoreCase);
        long reach = (long)NeighbourRadius * NeighbourRadius;
        foreach (var group in cells
                     .Where(cell => !cell.Dungeon && !string.IsNullOrEmpty(cell.Id) && !string.IsNullOrEmpty(cell.Monster))
                     .GroupBy(cell => (cell.Region, Name: cell.Monster.Trim().ToLowerInvariant())))
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
    }

    /// <summary>The camp's neighbourhood, or null when no other camp shares its spawns.</summary>
    public static Neighbourhood NeighbourhoodOf(string campId) =>
        string.IsNullOrEmpty(campId) ? null : _neighbourhoods.GetValueOrDefault(campId);

    /// <summary>True when the two camps are the same or share spawns.</summary>
    public static bool SameSpawns(string campId, string otherId)
    {
        if (string.IsNullOrEmpty(campId) || string.IsNullOrEmpty(otherId))
            return false;
        if (string.Equals(campId, otherId, StringComparison.OrdinalIgnoreCase))
            return true;
        return NeighbourhoodOf(campId)?.Ids.Contains(otherId, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>The live monsters a camp's bots compete for: its neighbourhood's, or its own.</summary>
    public static int SharedLiveMobs(string campId, int ownLiveMobs) =>
        NeighbourhoodOf(campId)?.LiveMobs is int shared ? Math.Max(shared, ownLiveMobs) : ownLiveMobs;

    /// <summary>Pure rule: more bots than half the camp's live monsters (two always fit).</summary>
    public static bool Crowded(int bots, int liveMobs) => bots > Math.Max(2, liveMobs / 2);

    /// <summary>How crowded a camp is: bots per live monster.</summary>
    public static double Pressure(int bots, int liveMobs) => bots / (double)Math.Max(1, liveMobs);

    /// <summary>Bots at the camp and at the camps that share its spawns.</summary>
    public static int Occupancy(string campId)
    {
        if (string.IsNullOrEmpty(campId))
            return 0;
        Refresh();
        Neighbourhood shared = NeighbourhoodOf(campId);
        lock (Sync)
        {
            if (shared == null)
                return _occupancy.GetValueOrDefault(campId);
            int bots = 0;
            foreach (string id in shared.Ids)
                bots += _occupancy.GetValueOrDefault(id);
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
        lock (Sync)
            _occupancy = counts;   // a new dictionary: NoteAssigned writes to the current one under the same lock
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
            _occupancy[campId] = _occupancy.GetValueOrDefault(campId) + bots;
    }

    /// <summary>
    /// The camps to draw from: the uncrowded ones, or when every camp is crowded the least crowded third (at least one).
    /// <paramref name="own"/> is the bot's own current camp, which does not count itself as competition.
    /// </summary>
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
