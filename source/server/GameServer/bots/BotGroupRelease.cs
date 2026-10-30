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
/// - A gamebot on its way back to a player walks a planned route (GameBot.WalkReturnStep). Thidranki, 2026-10-30: three
///   stood at the wall of Midgard's portal keep, whose way out is a door. While that player is dead it waits where it is,
///   and once there it takes up the group as after an invitation.
/// - For a while after its release a gamebot that makes no headway far from that player joins it, whatever holds it up.
/// </summary>
public static class BotGroupRelease
{
    /// <summary>A released gamebot's wait in another region before it joins the player who leads it.</summary>
    public const long ReturnToLeaderDelayMilliseconds = 20_000;

    /// <summary>How long after its release a gamebot is watched for making no headway.</summary>
    public const long ReleaseWatchMilliseconds = 300_000;

    /// <summary>How long a watched gamebot may stay within <see cref="ReturnHeadwayDistance"/> before it joins its leader.</summary>
    public const long ReturnStallMilliseconds = 15_000;

    public const int ReturnHeadwayDistance = 150;

    /// <summary>Nearer to its leader than this a gamebot is not watched: it is with the group.</summary>
    public const int ReleaseWatchLeaderDistance = 1_000;

    private const string ReturnWaitKey = "BotGroupRelease.ReturnWait";
    private const string ReleaseTickKey = "BotGroupRelease.ReleaseTick";
    private const string ReturnHeadwayKey = "BotGroupRelease.ReturnHeadway";
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotGroupRelease));

    private sealed record ReturnWait(long DeathTick, long SinceTick);
    private sealed record ReturnHeadway(Point3D Position, long SinceTick);

    /// <summary>Solo gamebots and bot-only groups keep upstream's walk: their own controllers take them back.</summary>
    public static bool IsGroupReturn(GameBot bot) => bot.IsAutonomousWorldBot && bot.IsPlayerLedGroup;

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

    /// <summary>Pure rule: only soon after a release, out of combat, and far from the leader.</summary>
    public static bool ReleaseWatchApplies(long releaseTick, long nowTick, bool inCombat, int distanceToLeader) =>
        releaseTick > 0 && nowTick - releaseTick <= ReleaseWatchMilliseconds && !inCombat &&
        distanceToLeader > ReleaseWatchLeaderDistance;

    public static bool ReturnStalled(long sinceTick, long nowTick) =>
        nowTick - sinceTick >= ReturnStallMilliseconds;

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
        if (!CanJoin(bot, leader))
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
        return !JoinLeader(bot, leader, "BOT_GROUP_RELEASE_RETURN");
    }

    /// <summary>While the player who leads it is dead, a gamebot on its way back stands where it is.</summary>
    public static bool WaitsForLeader(GameBot bot)
    {
        if (!IsGroupReturn(bot) || bot.PlayerGroupLeader.IsAlive)
            return false;
        if (bot.IsMoving)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        return true;
    }

    /// <summary>The end of a gamebot's way back: what an invitation does (BotGroupInvite).</summary>
    public static void FinishReturn(GameBot bot)
    {
        GamePlayer leader = bot.PlayerGroupLeader;
        if (!IsGroupReturn(bot) || !CanJoin(bot, leader))
            return;
        bot.WakeRecoveryRest();
        if (bot.EnterPlayerLedGroup(leader))
            bot.PathTo(leader, bot.MaxSpeed);
    }

    /// <summary>
    /// One look at a gamebot in a player's group, at the top of its brain's Think, so nothing there can keep this from
    /// running. Returns true when it made no headway for <see cref="ReturnStallMilliseconds"/> and joined that player.
    /// </summary>
    public static bool TryUnstick(GameBot bot)
    {
        if (bot == null || !IsGroupReturn(bot) || !bot.IsAlive || bot.IsOnStableMasterRoute)
            return false;

        long now = GameLoop.GameLoopTime;
        GamePlayer leader = bot.PlayerGroupLeader;
        if (!CanJoin(bot, leader) || leader.CurrentRegionID != bot.CurrentRegionID ||
            !ReleaseWatchApplies(bot.TempProperties.GetProperty<long>(ReleaseTickKey), now, bot.InCombat, bot.GetDistanceTo(leader)))
        {
            bot.TempProperties.RemoveProperty(ReturnHeadwayKey);
            return false;
        }

        ReturnHeadway headway = bot.TempProperties.GetProperty<ReturnHeadway>(ReturnHeadwayKey);
        if (headway == null || bot.GetDistanceTo(headway.Position) >= ReturnHeadwayDistance)
        {
            bot.TempProperties.SetProperty(ReturnHeadwayKey, new ReturnHeadway(new Point3D(bot.X, bot.Y, bot.Z), now));
            return false;
        }
        if (!ReturnStalled(headway.SinceTick, now))
            return false;
        bot.TempProperties.RemoveProperty(ReturnHeadwayKey);
        return JoinLeader(bot, leader, "BOT_GROUP_RELEASE_UNSTUCK", StuckDetail(bot));
    }

    /// <summary>What held the bot up, read before the move clears it.</summary>
    private static string StuckDetail(GameBot bot)
    {
        BotBrain brain = bot.Brain as BotBrain;
        return $" state={brain?.FSM.GetCurrentState()?.StateType} returning={bot.IsReturningAfterRelease} resting={bot.IsRecoveryResting} " +
               $"casting={bot.IsCasting} moving={bot.IsMoving} aggro={brain?.HasAggro} orderedPull=\"{brain?.ActiveOrderedPullTarget?.Name}\" " +
               $"health={bot.HealthPercent} mana={bot.ManaPercent} pet={bot.ControlledBrain?.Body?.IsAlive == true}";
    }

    private static bool CanJoin(GameBot bot, GamePlayer leader) =>
        leader != null && CanJoinLeader(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper,
            bot.Group != null && bot.Group == leader.Group, bot.Realm == leader.Realm, leader.IsAlive,
            leader.ObjectState == GameObject.eObjectState.Active && leader.CurrentZone != null);

    private static bool JoinLeader(GameBot bot, GamePlayer leader, string logMarker, string detail = "")
    {
        ushort oldRegion = bot.CurrentRegionID;
        int oldX = bot.X, oldY = bot.Y, oldZ = bot.Z;
        Vector3 spot = TemporaryGroupStableTravel.FormationPoint(leader, bot.ObjectID % 6);
        bot.CompleteStableMasterRoute();
        bot.StopFollowing();
        bot.StopMovingOnPath();
        bot.StopMoving();
        if (!bot.MoveTo(leader.CurrentRegionID, (int)Math.Round(spot.X), (int)Math.Round(spot.Y), (int)Math.Round(spot.Z), leader.Heading))
            return false;
        bot.EndReturnJourney();
        bot.WakeRecoveryRest();
        (bot.Brain as BotBrain)?.Disengage();
        if (bot.PersistentRecord != null)
        {
            bot.PersistentRecord.Activity = "Following the player's group";
            bot.MarkAutonomousStateDirty();
        }
        Log.Info($"{logMarker} bot=\"{bot.Name}\" id={bot.DatabaseID} from={oldRegion}:{oldX},{oldY},{oldZ} " +
                 $"to=player:{leader.Name} {leader.CurrentRegionID}:{leader.X},{leader.Y},{leader.Z}{detail}");
        return true;
    }

    /// <summary>Only a release in a player's group: every gamebot's release was 2,951 lines in three hours (GameBot.LogDeadMove).</summary>
    public static void NoteRelease(GameBot bot, ushort region, Point3D point, bool returnToParty)
    {
        bot.TempProperties?.SetProperty(ReleaseTickKey, GameLoop.GameLoopTime);
        bot.TempProperties?.RemoveProperty(ReturnHeadwayKey);
        if (bot.Group?.GetPlayersInTheGroup().Count > 0)
            Log.Info($"BOT_GROUP_RELEASE bot=\"{bot.Name}\" id={bot.DatabaseID} to={region}:{point.X},{point.Y},{point.Z} returning={returnToParty}");
    }
}
