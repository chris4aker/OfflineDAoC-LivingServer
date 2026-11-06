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
/// </summary>
public static class AutonomousCampCompetition
{
    private const long RefreshMilliseconds = 30_000;
    private static readonly object Sync = new();
    private static Dictionary<string, int> _occupancy = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextRefresh;

    /// <summary>Pure rule: more bots than half the camp's live monsters (two always fit).</summary>
    public static bool Crowded(int bots, int liveMobs) => bots > Math.Max(2, liveMobs / 2);

    /// <summary>How crowded a camp is: bots per live monster.</summary>
    public static double Pressure(int bots, int liveMobs) => bots / (double)Math.Max(1, liveMobs);

    public static int Occupancy(string campId)
    {
        if (string.IsNullOrEmpty(campId))
            return 0;
        Refresh();
        lock (Sync)
            return _occupancy.GetValueOrDefault(campId);
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
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            string camp = bot?.PersistentRecord?.CurrentCampId;
            if (bot is { IsAlive: true } && !string.IsNullOrEmpty(camp))
                counts[camp] = counts.GetValueOrDefault(camp) + 1;
        }
        lock (Sync)
            _occupancy = counts;
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
            if (own != null && string.Equals(id(camp), own, StringComparison.OrdinalIgnoreCase))
                count = Math.Max(0, count - partySize);
            return count + partySize;
        }
        T[] quiet = camps.Where(camp => !Crowded(Bots(camp), liveMobs(camp))).ToArray();
        if (quiet.Length > 0)
            return quiet;
        return camps.OrderBy(camp => Pressure(Bots(camp), liveMobs(camp)))
            .Take(Math.Max(1, camps.Length / 3)).ToArray();
    }
}
