using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Places where a monster far above the victim's level killed an autonomous bot. Players learn to keep away
/// from such a spot; so do the bots: no XP camp within <see cref="AvoidRadius"/> of a remembered spot for
/// <see cref="Lifetime"/>, and a levelling bot counts a straight walk past one as a long trip, so it usually
/// picks another camp. In memory only: a restart forgets.
///
/// Ported from stefanrows/OfflineDAoC (AutonomousPveBossDanger, GPL-3.0), where two named bosses killed
/// level-50 bots about 5,500 times in 19 hours. Here (2026-10-19, one hour): a merucha camp (levels 30-33)
/// on the Connacht road killed 18 bots of levels 10-17 on their way to Muire Tomb and Silvermine camps, and
/// 159 bots under level 20 died to red or purple monsters, 126 of them before reaching their camp.
///
/// 1.2.3 (#537): the spot is marked by con, not only by ten levels. In the 1.2.2 log (11/24) 5,777 of 14,166 deaths
/// were to purple monsters, 97% of them before the camp, and a level 24-26 howling maiden killed 120 different
/// level 10-14 bots at one spot in five hours: at those levels purple starts 4-5 levels up, so the 10-level gap
/// marked it only sometimes. Now a monster that cons purple to its victim marks its spot, and red does too when the
/// victim was alone; the spot keeps the killer's name, and a spot warns every bot the killer still cons red or
/// purple to. Deaths at one place (within <see cref="MergeRadius"/>) refresh one spot, so a hot spot stays one entry.
/// </summary>
public static class AutonomousPveBossDanger
{
    /// <summary>A killer this many levels above its victim marks the spot.</summary>
    public const int LevelGap = 10;
    /// <summary>A new death this close to a remembered spot in the same region refreshes that spot.</summary>
    public const float MergeRadius = 1_000;
    public const float AvoidRadius = 3_000;
    /// <summary>A straight walk that passes this close to a spot counts as passing it.</summary>
    public const float RouteCorridor = 1_500;
    /// <summary>Minutes a levelling bot adds to a camp whose route passes a spot (TravelWeightPercent: about 10%).</summary>
    public const double RoutePenaltyMinutes = 30;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);
    private const int MaximumSpots = 512;

    // The killer's level: a spot only warns bots it is still far above (a level-30 camp that kills level-12
    // bots is an ordinary camp for a level-28 bot).
    private readonly record struct Spot(ushort RegionId, int X, int Y, int KillerLevel, string KillerName, DateTime Utc);

    /// <summary>The con of a monster of <paramref name="killerLevel"/> to a bot of <paramref name="botLevel"/>.</summary>
    public static ConColor ConTo(int botLevel, int killerLevel) =>
        ConLevels.GetConColor(ConLevels.GetConLevel(Math.Max(0, botLevel), Math.Max(0, killerLevel)));

    /// <summary>A spot warns a bot its killer still cons red or purple to (or is ten levels above).</summary>
    public static bool Warns(int killerLevel, int botLevel) =>
        killerLevel >= botLevel + LevelGap || ConTo(botLevel, killerLevel) >= ConColor.RED;

    private static readonly object Sync = new();
    // Deaths publish a new bounded snapshot; camp planners never hold a shared
    // lock while checking thousands of candidate cells.
    private static Spot[] _spots = [];

    /// <summary>
    /// Whether a death marks its place: a monster (not a player, bot or pet) that conned purple to the victim (or was ten
    /// levels above it), or red to a victim that was alone. A group that dies to a red camp target chose that fight.
    /// </summary>
    public static bool Marks(string killerKind, int killerLevel, int victimLevel, bool victimAlone = true)
    {
        if (!string.Equals(killerKind, "monster", StringComparison.Ordinal))
            return false;
        if (killerLevel >= victimLevel + LevelGap)
            return true;
        ConColor con = ConTo(victimLevel, killerLevel);
        return con >= ConColor.PURPLE || victimAlone && con >= ConColor.RED;
    }

    public static int Count => Volatile.Read(ref _spots).Length;

    /// <summary>
    /// Remembers a killer and its spot. Returns true when this is a new spot, false when it refreshed one within
    /// <see cref="MergeRadius"/> (that spot then keeps the lower killer level, so it warns every bot either killer would).
    /// </summary>
    public static bool Record(ushort regionId, int x, int y, int killerLevel, DateTime utc, string killerName = null)
    {
        lock (Sync)
        {
            var spots = new List<Spot>(_spots.Length + 1);
            bool merged = false;
            foreach (Spot spot in _spots)
            {
                if (utc - spot.Utc > Lifetime)
                    continue;
                double dx = (double)spot.X - x, dy = (double)spot.Y - y;
                if (!merged && spot.RegionId == regionId && dx * dx + dy * dy <= (double)MergeRadius * MergeRadius)
                {
                    merged = true;
                    // the refreshed spot moves to the end: the oldest spots are dropped first
                    continue;
                }
                spots.Add(spot);
            }
            Spot existing = merged ? Array.Find(_spots, spot => spot.RegionId == regionId &&
                ((double)spot.X - x) * ((double)spot.X - x) + ((double)spot.Y - y) * ((double)spot.Y - y) <=
                (double)MergeRadius * MergeRadius && utc - spot.Utc <= Lifetime) : default;
            if (spots.Count >= MaximumSpots)
                spots.RemoveAt(0);
            spots.Add(merged
                ? existing with { KillerLevel = Math.Min(existing.KillerLevel, killerLevel), KillerName = killerName ?? existing.KillerName, Utc = utc }
                : new Spot(regionId, x, y, killerLevel, killerName, utc));
            Volatile.Write(ref _spots, spots.ToArray());
            return !merged;
        }
    }

    public static bool IsNear(ushort regionId, int x, int y, int botLevel, DateTime utc)
    {
        foreach (Spot spot in Volatile.Read(ref _spots))
        {
            if (spot.RegionId != regionId || utc - spot.Utc > Lifetime || !Warns(spot.KillerLevel, botLevel))
                continue;
            double dx = (double)spot.X - x, dy = (double)spot.Y - y;
            if (dx * dx + dy * dy <= (double)AvoidRadius * AvoidRadius)
                return true;
        }
        return false;
    }

    /// <summary>Remembered spots within <paramref name="radius"/> that warn a bot of this level (for route bends).</summary>
    public static List<(int X, int Y, int KillerLevel)> SpotsNear(ushort regionId, int x, int y, int botLevel, DateTime utc, float radius)
    {
        var near = new List<(int, int, int)>();
        foreach (Spot spot in Volatile.Read(ref _spots))
        {
            if (spot.RegionId != regionId || utc - spot.Utc > Lifetime || !Warns(spot.KillerLevel, botLevel))
                continue;
            double dx = (double)spot.X - x, dy = (double)spot.Y - y;
            if (dx * dx + dy * dy <= (double)radius * radius)
                near.Add((spot.X, spot.Y, spot.KillerLevel));
        }
        return near;
    }

    /// <summary>Whether the straight walk from one point to another in the same region passes a remembered spot.</summary>
    public static bool RouteCrosses(ushort regionId, int fromX, int fromY, int toX, int toY, int botLevel, DateTime utc)
    {
        foreach (Spot spot in Volatile.Read(ref _spots))
        {
            if (spot.RegionId != regionId || utc - spot.Utc > Lifetime || !Warns(spot.KillerLevel, botLevel))
                continue;
            if (DistanceToSegment(spot.X, spot.Y, fromX, fromY, toX, toY) <= RouteCorridor)
                return true;
        }
        return false;
    }

    /// <summary>
    /// #570 step 2 (town and horse trips, after True165 AutonomousRoadHotspots a24f34987): whether the straight walk from
    /// one point to another in the same region passes a remembered spot that warns this bot. A spot the walk starts in
    /// (within <see cref="RouteCorridor"/>) doesn't count, so a bot inside one can still choose its way out; with
    /// <paramref name="goalExempt"/> a spot within <see cref="AvoidRadius"/> of the goal doesn't count either (the bot is
    /// sent there on purpose, and the goal's own spot is judged apart).
    /// </summary>
    public static bool WalkPasses(ushort regionId, int fromX, int fromY, int toX, int toY, int botLevel, DateTime utc,
        bool goalExempt = false)
    {
        foreach (Spot spot in Volatile.Read(ref _spots))
        {
            if (spot.RegionId != regionId || utc - spot.Utc > Lifetime || !Warns(spot.KillerLevel, botLevel))
                continue;
            if (Within(spot, fromX, fromY, RouteCorridor) || goalExempt && Within(spot, toX, toY, AvoidRadius))
                continue;
            if (DistanceToSegment(spot.X, spot.Y, fromX, fromY, toX, toY) <= RouteCorridor)
                return true;
        }
        return false;
    }

    private static bool Within(Spot spot, int x, int y, float radius)
    {
        double dx = (double)spot.X - x, dy = (double)spot.Y - y;
        return dx * dx + dy * dy <= (double)radius * radius;
    }

    public static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared <= 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        double cx = ax + t * dx - px, cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>Tests only.</summary>
    public static void Clear()
    {
        lock (Sync)
            Volatile.Write(ref _spots, []);
    }
}
