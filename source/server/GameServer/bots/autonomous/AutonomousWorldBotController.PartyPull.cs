using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The rest of the party puller's camp turn (1.2.3, #569 step 9; True165 0e0a397). See AutonomousPartyPullCamp for the
/// rules and the switch. Formation movement is not touched: the leader's hold and the followers' formation follow are
/// unchanged. Autonomous gamebot parties only (a ranged-pull party: UsesDefensivePullGroup), outdoors only.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private long _partyTooFarSinceTick;
    private string _partyTooFarCampId = string.Empty;
    private int _partyTooFarSkips;
    private long _partyTooFarLastSkipTick;
    private long _partyTooFarSkipPendingUntil;
    private long _partyTooFarLogTick;
    private int _partyTooFarSkipsSinceLog;
    private long _partyAdvanceRetryTick;
    private string _pullerArrivedLoggedCampId = string.Empty;
    private long _pullerEmptyHoldLoggedSince = -1;

    /// <summary>A non-leader puller beside a leader inside the arrival ring counts as arrived (outdoors).</summary>
    private bool PartyPullerArrivedWithLeader(GameBot bot, int campDistance, bool dungeonApproach, bool certifiedDarknessFalls)
    {
        if (!AutonomousPartyPullCamp.CampTurn || certifiedDarknessFalls || dungeonApproach || _camp == null ||
            campDistance <= CampArrivalRadius || _groupDirective?.IsDynamic != true ||
            _groupDirective.Puller != bot || _groupDirective.Leader is not { } leader || leader == bot ||
            leader.CurrentRegionID != bot.CurrentRegionID || bot.CurrentZone?.IsDungeon == true ||
            !AutonomousBotGroupCoordinator.UsesDefensivePullGroup(bot.Group))
            return false;
        int leaderCampDistance = Distance(leader.X, leader.Y, _camp.X, _camp.Y);
        int pullerToLeader = bot.GetDistanceTo(leader);
        if (!AutonomousPartyPullCamp.PartyPullerHasArrived(campDistance, leaderCampDistance, pullerToLeader,
                CampArrivalRadius, AutonomousBotGroupCoordinator.PartyCohesionRadius))
            return false;
        if (_pullerArrivedLoggedCampId != _camp.Id)
        {
            _pullerArrivedLoggedCampId = _camp.Id;
            Log.Info($"AUTONOMOUS_PARTY_PULLER_ARRIVED_WITH_LEADER bot={bot.Name} leader={leader.Name} camp=\"{_camp.Id}\" " +
                     $"campDistance={campDistance} leaderCampDistance={leaderCampDistance} toLeader={pullerToLeader}");
        }
        return true;
    }

    /// <summary>Any outcome but a too-far skip (pull, waiting, unreachable) ends the too-far episode.</summary>
    private void NotePartyPullOutcome(PartyPullFallback next)
    {
        if (next == PartyPullFallback.SkipTooFar) return;
        _partyTooFarSinceTick = 0;
        _partyTooFarSkips = 0;
    }

    /// <summary>
    /// A target too far for the short approach to a firing point: skip it for 60 s and look for a reachable one, with the
    /// empty-camp clock running. After about 45 s of only too-far targets, move the party's camp point onto the target's
    /// floor (bounded by the coordinator). Never the ordinary pull's walk into the pack (owner, 2026-10-06).
    /// </summary>
    private bool SkipTooFarPartyTarget(GameBot bot, GameNPC target, long emptySinceBefore, string reportedEmptyBefore)
    {
        long now = GameLoop.GameLoopTime;
        string campId = _camp?.Id ?? string.Empty;
        if (!AutonomousPartyPullCamp.TooFarEpisodeContinues(_partyTooFarSinceTick, _partyTooFarCampId == campId,
                _partyTooFarLastSkipTick, LastGroupCombatTick(bot), now))
        {
            _partyTooFarSinceTick = now;
            _partyTooFarCampId = campId;
            _partyTooFarSkips = 0;
        }
        _partyTooFarLastSkipTick = now;
        _partyTooFarSkips++;
        long tooFarFor = now - _partyTooFarSinceTick;
        if (_camp != null && !_camp.IsDungeon && AutonomousPartyPullCamp.ShouldAdvanceAfterTooFar(tooFarFor) &&
            now >= _partyAdvanceRetryTick && AutonomousBotGroupCoordinator.PartyCampAdvanceAllowed(bot, campId))
        {
            _partyAdvanceRetryTick = now + 15_000;
            if (TryResolvePartyAdvancePoint(bot, target, out Vector3 point) &&
                AutonomousBotGroupCoordinator.TryAdvanceCampTowardTarget(bot, campId, point, target.Name, tooFarFor,
                    out int advances))
            {
                _partyTooFarSinceTick = 0;
                _partyTooFarSkips = 0;
                _emptyCampSinceTick = 0;
                _reportedEmptySharedCampId = string.Empty;
                _patrolDestination = null;
                ResetRouteOrderState();
                _groupDirective = AutonomousBotGroupCoordinator.Pulse(bot);
                if (_groupDirective?.Camp != null)
                    _camp = FromSharedCamp(_groupDirective.Camp);
                if (_camp == null) return true;
                SetStatus(bot, $"Moving the party toward {target.Name}", GoalText(),
                    $"Only targets beyond pull range were found; the party walks in together (move {advances} at this camp)",
                    target.Name, _camp.ZoneName);
                return true;
            }
        }
        _failedSavageMeleePullTargets[target.ObjectID] = now + AutonomousPartyPullCamp.TooFarSkipMilliseconds;
        _partyTooFarSkipPendingUntil = now + AutonomousPartyPullCamp.TooFarSkipMilliseconds;
        // a camp whose only targets are too far counts as empty (the 75 s wait and the 180 s move still happen)
        if (_camp != null && AutonomousBotGroupCoordinator.ShouldHoldCampThroughRespawn(_groupDirective))
        {
            _emptyCampSinceTick = AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(emptySinceBefore, now);
            _reportedEmptySharedCampId = reportedEmptyBefore ?? string.Empty;
        }
        _partyTooFarSkipsSinceLog++;
        if (AutonomousPartyPullCamp.ShouldLogTooFar(_partyTooFarLogTick, now))
        {
            Log.Info($"AUTONOMOUS_PARTY_PULL_TOO_FAR bot={bot.Name} target=\"{target.Name}\" camp=\"{campId}\" " +
                     $"distance={bot.GetDistanceTo(target)} skips={_partyTooFarSkips} skipsSinceLastLog={_partyTooFarSkipsSinceLog} " +
                     $"tooFarSeconds={tooFarFor / 1000}");
            _partyTooFarLogTick = now;
            _partyTooFarSkipsSinceLog = 0;
        }
        SetStatus(bot, "Looking for a target in pull range", GoalText(),
            $"{target.Name} is beyond pull range; the party holds formation and looks for a nearer one",
            target.Name, _camp?.ZoneName ?? string.Empty);
        return true;
    }

    /// <summary>The latest PvE combat tick of any party member (O(members), only on a too-far skip).</summary>
    private static long LastGroupCombatTick(GameBot bot)
    {
        long last = bot.LastCombatTickPvE;
        if (bot.Group == null) return last;
        foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            if (member != null && member.LastCombatTickPvE > last)
                last = member.LastCombatTickPvE;
        return last;
    }

    /// <summary>A WorkCamp turn with no target at all: once no too-far skip is pending, the episode is over.</summary>
    private void NotePartyEmptyCampTurn()
    {
        if (_partyTooFarSinceTick != 0 &&
            AutonomousPartyPullCamp.EmptyTurnEndsTooFarEpisode(_partyTooFarSkipPendingUntil, GameLoop.GameLoopTime))
        {
            _partyTooFarSinceTick = 0;
            _partyTooFarSkips = 0;
        }
    }

    /// <summary>The connected floor at the target, with a complete corridor from the puller.</summary>
    private static bool TryResolvePartyAdvancePoint(GameBot bot, GameNPC target, out Vector3 point)
    {
        point = default;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Zone zone = bot.CurrentZone;
        if (zone == null || nav?.IsAvailable != true || !nav.HasNavmesh(zone) || target.CurrentZone != zone)
            return false;
        Vector3 from = new(bot.X, bot.Y, bot.Z);
        return AutonomousZonePointApproach.TryResolve(nav, zone, from, new(target.X, target.Y, target.Z), 112,
                   out point) &&
               AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, point);
    }

    /// <summary>A non-leader party puller waits beside the party at an empty outdoor camp instead of patrolling away.</summary>
    private bool HoldPartyPullerAtEmptyCamp(GameBot bot)
    {
        if (!AutonomousPartyPullCamp.CampTurn || _camp == null || _camp.IsDungeon ||
            _groupDirective?.IsDynamic != true || _groupDirective.Puller != bot || _groupDirective.Leader == bot ||
            !AutonomousBotGroupCoordinator.UsesDefensivePullGroup(bot.Group))
            return false;
        _patrolDestination = null;
        bot.StopMovingOnPath();
        bot.StopMoving();
        if (_pullerEmptyHoldLoggedSince != _emptyCampSinceTick)
        {
            _pullerEmptyHoldLoggedSince = _emptyCampSinceTick;
            Log.Info($"AUTONOMOUS_PARTY_PULLER_EMPTY_CAMP_HOLD bot={bot.Name} camp=\"{_camp.Id}\" target=\"{_camp.MonsterName}\" " +
                     $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z}");
        }
        SetStatus(bot, $"Waiting for {_camp.MonsterName} to respawn", GoalText(),
            "The puller holds with the party; the party moves together if the camp stays empty",
            _camp.MonsterName, _camp.ZoneName);
        return true;
    }
}
