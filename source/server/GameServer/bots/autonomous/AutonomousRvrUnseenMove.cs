using System;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// RvR work 14, step 1a (issue #532; pathing.md 3): an autonomous gamebot in a war zone makes a non-urgent move within its
/// region (a keep route restart at the arrival point, a route-pocket short hop, a floor step, a listed-pocket nudge) only
/// when no real player could see either end of it. A person in the frontier never blinks; a bot that did was seen
/// vanishing and standing up to 4,000 units away. While a player is within <see cref="WorldMgr.VISIBILITY_DISTANCE"/> of
/// the start or the landing, the move waits and is asked again on a later turn (about <see cref="HoldMilliseconds"/>);
/// each wait is logged as RVR_TELEPORT_HELD under a counted budget. Region changes (capital, raid front, porter), door
/// passes and every bot outside the war zones are unchanged. The member-to-leader move has its own watcher check
/// (<see cref="AutonomousGroupMemberStall.MayMoveUnseen"/>, #538).
/// </summary>
public static class AutonomousRvrUnseenMove
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(AutonomousRvrUnseenMove));

    /// <summary>A shorter move is a foot shuffle, not a blink: it never waits.</summary>
    public const int MinimumDistance = 64;
    /// <summary>How long a bot whose move waited holds before it asks again.</summary>
    public const long HoldMilliseconds = 10_000;
    public const int LinesPerMinute = 60;

    /// <summary>Whether the player at that distance (-1: none in the region) could see the spot. Pure.</summary>
    public static bool Seen(int playerDistance) => playerDistance >= 0 && playerDistance <= WorldMgr.VISIBILITY_DISTANCE;

    /// <summary>
    /// The rule: a move waits when the bot is in a war zone, stays in its region, goes at least
    /// <see cref="MinimumDistance"/>, and the player could see the start or the landing. Pure.
    /// </summary>
    public static bool MustWait(bool inWar, bool sameRegion, double distance, int playerFrom, int playerTo) =>
        inWar && sameRegion && distance >= MinimumDistance && (Seen(playerFrom) || Seen(playerTo));

    /// <summary>
    /// True when this autonomous gamebot's move to <paramref name="to"/> (same region) must wait because a player could
    /// see it; logs RVR_TELEPORT_HELD. False (the move may go ahead) for every other bot, place and move.
    /// </summary>
    public static bool Holds(GameBot bot, Vector3 to, string how)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || bot.ObjectState != GameObject.eObjectState.Active ||
            !AutonomousRvrRecordScope.InWar(bot))
            return false;
        double distance = Math.Sqrt((double)(bot.X - to.X) * (bot.X - to.X) + (double)(bot.Y - to.Y) * (bot.Y - to.Y) +
                                    (double)(bot.Z - to.Z) * (bot.Z - to.Z));
        if (distance < MinimumDistance)
            return false;
        int playerFrom = RvrFights.PlayerDistance(bot.CurrentRegionID, bot.X, bot.Y);
        int playerTo = RvrFights.PlayerDistance(bot.CurrentRegionID, (int)to.X, (int)to.Y);
        if (!MustWait(true, true, distance, playerFrom, playerTo))
            return false;
        long now = GameLoop.GameLoopTime;
        if (Log.IsInfoEnabled && RvrRecordBudget.Take("RVR_TELEPORT_HELD", now, LinesPerMinute))
            Log.Info(Line(now, bot.Name, bot.DatabaseID, bot.Realm, how, bot.CurrentRegionID, bot.X, bot.Y, (int)to.X, (int)to.Y,
                (int)distance, playerFrom, playerTo));
        return true;
    }

    /// <summary>The row: one move that waited because the player could see it. Pure.</summary>
    public static string Line(long t, string bot, long id, eRealm realm, string how, ushort region, int fromX, int fromY,
        int toX, int toY, int distance, int playerFrom, int playerTo) =>
        $"RVR_TELEPORT_HELD t={t} bot={RvrRecordText.Field(bot)} id={id} realm={RvrRecordText.Realm(realm)} how={how} " +
        $"from={region}:{fromX},{fromY} to={region}:{toX},{toY} distance={distance} playerFrom={playerFrom} " +
        $"playerTo={playerTo} playerSaw={RvrTeleports.Seen(playerFrom, playerTo)} holdMs={HoldMilliseconds}";
}
