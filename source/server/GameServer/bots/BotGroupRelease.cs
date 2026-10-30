using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// /allrelease and the way back to a player's group after a release. Kept out of upstream's GameBot, which only calls
/// in here, so it stays easy to merge.
/// - /allrelease runs the release the death timer would have run, at once, for every dead bot of the player's group.
/// - Battlegrounds have no bind points, so a gamebot in a player's group released to its capital and stood there for good.
///   It now releases to its realm's portal keep, whatever bg_release_to_portal_keep says for players. Solo gamebots and
///   bot-only groups release as upstream has them: their own controllers already take them back.
/// - A gamebot that is still in another region than the player who leads it (no portal keep, or the player moved on)
///   joins that player after a short wait, about as long as the capital's teleporter takes.
/// </summary>
public static class BotGroupRelease
{
    /// <summary>A released gamebot's wait in another region before it joins the player who leads it.</summary>
    public const long ReturnToLeaderDelayMilliseconds = 20_000;

    private const string ReturnWaitKey = "BotGroupRelease.ReturnWait";
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotGroupRelease));

    private sealed record ReturnWait(long DeathTick, long SinceTick);

    /// <summary>
    /// Pure rule: only a gamebot on its way back to a player's group, and as for a player, not above the battleground's
    /// level cap.
    /// </summary>
    public static bool PortalKeepApplies(bool playerLed, bool returnToParty, bool isBattleground, int level, int maxLevel) =>
        playerLed && returnToParty && isBattleground && level <= maxLevel;

    /// <summary>Pure rule: a gamebot joins the living player who leads it, in the same group and realm, only.</summary>
    public static bool CanJoinLeader(bool autonomous, bool temporary, bool sameGroup, bool sameRealm,
        bool leaderAlive, bool leaderActive) =>
        autonomous && !temporary && sameGroup && sameRealm && leaderAlive && leaderActive;

    public static bool ReturnDelayOver(long sinceTick, long nowTick) =>
        nowTick - sinceTick >= ReturnToLeaderDelayMilliseconds;

    /// <summary>What /allrelease tells the player.</summary>
    public static IReadOnlyList<string> Report(IReadOnlyCollection<string> released, IReadOnlyCollection<string> waiting)
    {
        if (released.Count == 0 && waiting.Count == 0)
            return ["There are no dead bots in your group."];
        List<string> lines = [];
        if (released.Count > 0)
            lines.Add($"Released: {string.Join(", ", released)}.");
        if (waiting.Count > 0)
            lines.Add($"Could not release yet: {string.Join(", ", waiting)}. Companions wait while you are dead or a fight is near you.");
        return lines;
    }

    /// <summary>The nearest thing a battleground has to a bind point for this gamebot: its realm's portal keep.</summary>
    public static bool TryPortalKeep(GameBot bot, ushort region, bool returnToParty, out Point3D point)
    {
        point = null;
        DbBattleground battleground = GameServer.KeepManager?.GetBattleground(region);
        if (battleground == null ||
            !PortalKeepApplies(bot.IsPlayerLedGroup, returnToParty, true, bot.Level, battleground.MaxLevel))
            return false;
        foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(region))
        {
            if (!keep.IsPortalKeep || keep.OriginalRealm != bot.Realm)
                continue;
            Zone zone = WorldMgr.GetRegion(region)?.GetZone(keep.X, keep.Y);
            point = BotReleaseBindPoints.Resolve(PathfindingProvider.Instance, zone, new Vector3(keep.X, keep.Y, keep.Z));
            return point != null;
        }
        return false;
    }

    /// <summary>Releases every dead bot of the group at once. Returns the names released and those that have to wait.</summary>
    public static (List<string> Released, List<string> Waiting) ReleaseDeadBots(Group group)
    {
        List<string> released = [];
        List<string> waiting = [];
        foreach (GameBot bot in group.GetMembersInTheGroup().OfType<GameBot>().Where(bot => !bot.IsAlive))
        {
            bool done;
            try
            {
                done = bot.ReleaseNow();
            }
            catch (Exception exception)
            {
                // The bot's own death timer goes on and retries.
                Log.Error($"BOT_GROUP_RELEASE_FAILED bot=\"{bot.Name}\"", exception);
                done = false;
            }
            (done ? released : waiting).Add(bot.Name);
        }
        return (released, waiting);
    }

    /// <summary>
    /// One step of a released gamebot's return while it is in another region than its corpse and its leader.
    /// Returns true while the return goes on.
    /// </summary>
    public static bool TryReturnToLeader(GameBot bot, long deathTick)
    {
        GamePlayer leader = bot.PlayerGroupLeader;
        if (leader == null || !CanJoinLeader(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper,
                bot.Group != null && bot.Group == leader.Group, bot.Realm == leader.Realm, leader.IsAlive,
                leader.ObjectState == GameObject.eObjectState.Active && leader.CurrentZone != null))
            return true;

        ReturnWait wait = bot.TempProperties.GetProperty<ReturnWait>(ReturnWaitKey);
        if (wait == null || wait.DeathTick != deathTick)
        {
            wait = new ReturnWait(deathTick, GameLoop.GameLoopTime);
            bot.TempProperties.SetProperty(ReturnWaitKey, wait);
        }
        if (!ReturnDelayOver(wait.SinceTick, GameLoop.GameLoopTime))
            return true;
        bot.TempProperties.RemoveProperty(ReturnWaitKey);

        ushort oldRegion = bot.CurrentRegionID;
        Vector3 spot = TemporaryGroupStableTravel.FormationPoint(leader, bot.ObjectID % 6);
        bot.CompleteStableMasterRoute();
        bot.StopFollowing();
        bot.StopMovingOnPath();
        bot.StopMoving();
        if (!bot.MoveTo(leader.CurrentRegionID, (int)Math.Round(spot.X), (int)Math.Round(spot.Y), (int)Math.Round(spot.Z), leader.Heading))
            return true;
        bot.EndReturnJourney();
        if (bot.Brain is BotBrain brain)
            brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
        if (bot.PersistentRecord != null)
        {
            bot.PersistentRecord.Activity = "Following the player's group";
            bot.MarkAutonomousStateDirty();
        }
        Log.Info($"BOT_GROUP_RELEASE_RETURN bot=\"{bot.Name}\" id={bot.DatabaseID} from={oldRegion} " +
                 $"to=player:{leader.Name} {leader.CurrentRegionID}:{leader.X},{leader.Y},{leader.Z}");
        return false;
    }

    /// <summary>Only a release in a player's group: every gamebot's release was 2,951 lines in three hours (GameBot.LogDeadMove).</summary>
    public static void LogRelease(GameBot bot, ushort region, Point3D point, bool returnToParty)
    {
        if (bot.Group?.GetPlayersInTheGroup().Count > 0)
            Log.Info($"BOT_GROUP_RELEASE bot=\"{bot.Name}\" id={bot.DatabaseID} to={region}:{point.X},{point.Y},{point.Z} returning={returnToParty}");
    }
}
