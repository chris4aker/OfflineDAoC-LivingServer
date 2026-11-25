using System;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// The stealth rows (#522; stealth.md "Measuring tools worth building"; #505's record). Logging only: reads the world,
/// writes rows, draws no random number.
/// <list type="bullet">
/// <item><c>RVR_STEALTH_UNCOVER</c>: the uncover timer (StealthRules.Look) uncovering the player or an autonomous gamebot
/// stealther: who was uncovered, by what kind (a guard or a monster; never a gamebot or a pet of one since #522), the
/// distance, the sense and whether a keep was within <see cref="KeepRadius"/>.</item>
/// <item><c>RVR_STEALTH_ARCHER_OPEN</c>: a bot archer's opening shot from stealth (BotStealthArcher), the twin of
/// RVR_STEALTH_OPEN: the distance, whether it was a Critical Shot and whether the bot stayed hidden on the nock.</item>
/// <item><c>RVR_STEALTH_TRAVEL</c>: a gamebot stealther hiding or unhiding on the move (BotStealthTravel): the reason, and
/// the nearest enemy player's distance.</item>
/// </list>
/// Player names go in a <c>player=</c> field, which the tools mask.
/// </summary>
public static class RvrStealthRecord
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrStealthRecord));

    public const int KeepRadius = 2_500;
    public const int LinesPerMinute = 120;

    /// <summary>The uncoverer's kind for the row: Guard or Npc (a monster). Pure on the type.</summary>
    public static string UncovererKind(GameNPC by) => by is Keeps.GameKeepGuard ? "Guard" : RvrRecordText.Kind(by);

    /// <summary>An uncover: writes RVR_STEALTH_UNCOVER.</summary>
    public static void Uncovered(GameLiving hider, GameNPC by, double distance, UncoverSense sense)
    {
        if (hider == null || by == null || !Log.IsInfoEnabled)
            return;
        long now = GameLoop.GameLoopTime;
        if (!RvrRecordBudget.Take("RVR_STEALTH_UNCOVER", now, LinesPerMinute))
            return;
        Log.Info(UncoverLine(now, hider is GamePlayer, RvrRecordText.Field(hider.Name), hider.Realm, UncovererKind(by),
            RvrRecordText.Field(by.Name), by.Level, (int)distance, sense, NearKeep(hider), hider.CurrentRegionID));
    }

    /// <summary>The row. Pure.</summary>
    public static string UncoverLine(long t, bool player, string hider, eRealm realm, string byKind, string by, int byLevel,
        int distance, UncoverSense sense, bool atKeep, ushort region) =>
        $"RVR_STEALTH_UNCOVER t={t} hider={(player ? "player" : "bot")} {(player ? "player" : "bot")}={hider} " +
        $"realm={RvrRecordText.Realm(realm)} byKind={byKind} by={by} byLevel={byLevel} distance={distance} " +
        $"sense={sense.ToString().ToLowerInvariant()} atKeep={(atKeep ? "true" : "false")} region={region}";

    /// <summary>A bot archer's opening shot from stealth: writes RVR_STEALTH_ARCHER_OPEN.</summary>
    public static void ArcherOpened(GameBot bot, GameLiving target, int distance, bool critical, bool stayedHidden)
    {
        if (bot == null || target == null || !Log.IsInfoEnabled)
            return;
        long now = GameLoop.GameLoopTime;
        if (!RvrRecordBudget.Take("RVR_STEALTH_ARCHER_OPEN", now, LinesPerMinute))
            return;
        Log.Info(ArcherLine(now, bot.Name, bot.DatabaseID, bot.Realm, (eCharacterClass)(bot.CharacterClass?.ID ?? 0),
            target is GamePlayer ? "player" : RvrRecordText.Field(target.Name), RvrRecordText.Kind(target), distance, critical,
            stayedHidden, bot.CurrentRegionID));
    }

    /// <summary>The row. Pure.</summary>
    public static string ArcherLine(long t, string bot, long id, eRealm realm, eCharacterClass characterClass, string target,
        string targetKind, int distance, bool critical, bool stayedHidden, ushort region) =>
        $"RVR_STEALTH_ARCHER_OPEN t={t} bot={RvrRecordText.Field(bot)} id={id} realm={RvrRecordText.Realm(realm)} " +
        $"class={characterClass} target={target} targetKind={targetKind} distance={distance} " +
        $"shot={(critical ? "critical" : "normal")} stayedHidden={(stayedHidden ? "true" : "false")} region={region}";

    /// <summary>A gamebot stealther hiding or unhiding on the move: writes RVR_STEALTH_TRAVEL.</summary>
    public static void Travel(GameBot bot, bool hidden, string reason)
    {
        if (bot == null || !Log.IsInfoEnabled)
            return;
        long now = GameLoop.GameLoopTime;
        if (!RvrRecordBudget.Take("RVR_STEALTH_TRAVEL", now, LinesPerMinute))
            return;
        int nearest = -1;
        foreach (GamePlayer player in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            if (player.Realm != bot.Realm && player.IsAlive)
            {
                int d = (int)bot.GetDistanceTo(player);
                if (nearest < 0 || d < nearest)
                    nearest = d;
            }
        Log.Info(TravelLine(now, bot.Name, bot.DatabaseID, bot.Realm, hidden, reason, nearest, bot.CurrentRegionID));
    }

    /// <summary>The row. Pure.</summary>
    public static string TravelLine(long t, string bot, long id, eRealm realm, bool hidden, string reason, int nearestPlayer,
        ushort region) =>
        $"RVR_STEALTH_TRAVEL t={t} bot={RvrRecordText.Field(bot)} id={id} realm={RvrRecordText.Realm(realm)} " +
        $"state={(hidden ? "hidden" : "visible")} reason={reason} nearestPlayer={(nearestPlayer < 0 ? "-" : nearestPlayer.ToString())} " +
        $"region={region}";

    private static bool NearKeep(GameLiving living)
    {
        try
        {
            var keeps = GameServer.KeepManager?.GetKeepsOfRegion(living.CurrentRegionID);
            if (keeps == null)
                return false;
            foreach (Keeps.AbstractGameKeep keep in keeps)
                if (keep != null && Math.Abs(keep.X - living.X) < KeepRadius && Math.Abs(keep.Y - living.Y) < KeepRadius &&
                    Math.Sqrt((double)(keep.X - living.X) * (keep.X - living.X) + (double)(keep.Y - living.Y) * (keep.Y - living.Y)) <= KeepRadius)
                    return true;
        }
        catch (InvalidOperationException)
        {
            // A keep list changing under us: the row says false rather than throw from a timer.
        }
        return false;
    }
}
