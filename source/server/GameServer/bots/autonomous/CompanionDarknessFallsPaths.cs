using System.Collections.Concurrent;
using System.Numerics;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// #571 step 3: which Darkness Falls path rules a bot moves by.
///
/// The gamebot rules (Pathfinder's certified-corridor gate, certified falls only with the full certificate and only on a
/// complete path, a re-order during a fall ignored unless it keeps the same goal, the stair climb on the full certificate's
/// links) are for an autonomous gamebot that no player leads: it picks its own DF goals and walks them unseen. A bot that
/// follows a real player there (a hired companion, a gamebot in the player's group, a bot with a player owner) moves by the
/// companion rules that /spawn and /bot companions already use (#438): the ordinary Detour path, the installed mesh's stair
/// falls on a complete or partial path, a re-order during a drop finishing the fall first, the climb on the installed
/// links. Before this, a hired companion or befriended gamebot got the gamebot gate, NoPathFound on every path that was not
/// a complete certified corridor, and stood still until COMPANION_STUCK_165 or the stuck watchdog moved it.
/// Unled gamebots (solo, bot-led groups, raids, the DF raid AI) are unchanged; Darkness Falls stays open to solo bots.
/// </summary>
public static class CompanionDarknessFallsPaths
{
    internal static readonly Logger Log = LoggerManager.Create(typeof(CompanionDarknessFallsPaths));

    /// <summary>At most one COMPANION_PATH_DF row per bot in this interval.</summary>
    public const long LogIntervalMilliseconds = 30_000;

    private static readonly ConcurrentDictionary<long, long> NextLog = new();

    /// <summary>Pure: the gamebot DF rules apply to an autonomous bot that no player leads.</summary>
    public static bool UsesGamebotRules(bool autonomousWorldBot, bool playerLed) => autonomousWorldBot && !playerLed;

    /// <summary>A bot a real player leads: in a player's group, with a player owner (/spawn, /bot), or a hired companion.</summary>
    public static bool IsPlayerLed(GameBot bot) =>
        bot != null && (bot.IsPlayerLedGroup || bot.Owner != null || HiredCompanions165.IsHired(bot));

    /// <summary>This bot moves by the gamebot DF rules (see the class summary). False for a null bot.</summary>
    public static bool UsesGamebotRules(GameBot bot) =>
        bot != null && UsesGamebotRules(bot.IsAutonomousWorldBot, IsPlayerLed(bot));

    /// <summary>"gamebot", "hired", "grouped" (a gamebot in a player's group) or "companion" (/spawn, /bot).</summary>
    public static string KindOf(GameBot bot) =>
        bot == null ? "none" :
        !bot.IsAutonomousWorldBot ? "companion" :
        HiredCompanions165.IsHired(bot) ? "hired" :
        IsPlayerLed(bot) ? "grouped" : "gamebot";

    /// <summary>Pure: a row is due for <paramref name="key"/> at <paramref name="now"/>.</summary>
    public static bool LogDue(ConcurrentDictionary<long, long> next, long key, long now) =>
        !next.TryGetValue(key, out long due) || now >= due;

    /// <summary>Pure: claims the row slot for <paramref name="key"/> (true at most once per <paramref name="interval"/>).
    /// A bot's path work runs on its own tick, so one key is not claimed from two threads at once; a rare duplicate row is
    /// harmless. Each claim first drops every expired slot, so the map holds only bots with a row in the last interval
    /// (no per-bot entry outlives its slot, #731).</summary>
    public static bool TryClaimLogSlot(ConcurrentDictionary<long, long> next, long key, long now, long interval)
    {
        if (!LogDue(next, key, now))
            return false;
        PruneExpired(next, now);
        next[key] = now + interval;
        return true;
    }

    /// <summary>Pure: removes the slots that are due again (their bot may log at once, so the entry carries nothing).</summary>
    public static void PruneExpired(ConcurrentDictionary<long, long> next, long now)
    {
        foreach (var slot in next)
            if (now >= slot.Value)
                next.TryRemove(slot);
    }

    private static long KeyOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : -bot.ObjectID;

    /// <summary>The old gate's check is worth running for this player-led bot now (its row slot is free).</summary>
    public static bool ShouldCheckOldGate(GameBot bot) =>
        bot != null && Log.IsInfoEnabled && LogDue(NextLog, KeyOf(bot), GameLoop.GameLoopTime);

    /// <summary>
    /// COMPANION_PATH_DF: a player-led bot's Darkness Falls path that the gamebot gate would have turned into NoPathFound
    /// (the bot then stood still). It now takes the ordinary path, as a /spawn companion does. At most one per bot per 30 s.
    /// </summary>
    public static void NoteUngatedPath(GameBot bot, PathfindingStatus status, int nodeCount, Vector3 from, Vector3 to)
    {
        if (bot == null || !Log.IsInfoEnabled || !TryClaimLogSlot(NextLog, KeyOf(bot), GameLoop.GameLoopTime, LogIntervalMilliseconds))
            return;
        // COMPANION_PATH_DF: a player-led bot's Darkness Falls path that the gamebot gate would have refused (it now takes the ordinary path).
        Log.Info($"COMPANION_PATH_DF bot=\"{bot.Name}\" id={bot.DatabaseID} kind={KindOf(bot)} region={bot.CurrentRegionID} " +
                 $"status={status} nodes={nodeCount} from={(int) from.X},{(int) from.Y},{(int) from.Z} " +
                 $"to={(int) to.X},{(int) to.Y},{(int) to.Z} old_gate=reject");
    }
}
