using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private string _keepTravelKey;
    private long _keepTravelGeometry, _keepTravelRetry;
    private int _keepTravelFailures, _keepTravelIndex;
    private Vector3 _keepPlanningOrigin;
    private Vector3? _keepTravelLastPosition;
    private Vector3[] _keepTravelPoints;
    private RvrPlanningNavigation _keepPlanning;
    private Vector3? _keepHop;
    private Vector3 _keepHopFor, _keepStallPosition, _keepStallAnchor;
    private long _keepStallSince;
    private int _keepTravelStalls;
    // A lane this bot's keep walk follows (RvR march or, from work package 6, a roam), and the lane that failed to join.
    private RvrLaneTravel _laneTravel;
    private string _laneTravelFailed;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> LaneJoinLogs = new(StringComparer.Ordinal);

    /// <summary>
    /// Work package 6's hook (roaming groups between catalog objectives): the keep walk to <c>travel.DestinationId</c>
    /// follows this lane (see <see cref="RvrLaneChoice.ForRoam"/>). Null returns the walk to today's route.
    /// </summary>
    private void UseLaneTravel(RvrLaneTravel travel)
    {
        if (travel?.Tag == _laneTravel?.Tag) return;
        _laneTravel = travel;
        _laneTravelFailed = null;
    }

    /// <summary>The lane for this destination, if the walk should follow one: same destination and region, not failed.</summary>
    private RvrLaneTravel ActiveLaneTravel(GameBot bot, CampDestination destination) =>
        _laneTravel is { } lane && lane.DestinationId == destination.Id && lane.Region == bot.CurrentRegionID &&
        lane.Region == destination.RegionId && _laneTravelFailed != lane.Tag ? lane : null;

    /// <summary>
    /// The keep walk along the active lane: the planner's own route onto the lane's first walk point ahead of the bot, the
    /// lane's validated walk, then the planner's route from the lane's end to the keep approach. False keeps today's route
    /// (RvrKeepRoute.TryBuild), and a lane that cannot be joined is not tried again for this walk (RVR_LANE_FALLBACK).
    /// A planner yield passes through to the caller, which retries the same work on its next slice.
    /// </summary>
    private bool TryBuildLaneTravel(GameBot bot, CampDestination destination, IPathfindingMgr nav, Vector3 origin, Vector3 endpoint,
        out Vector3[] steps)
    {
        steps = null;
        RvrLaneTravel lane = ActiveLaneTravel(bot, destination);
        if (lane == null) return false;
        RvrLaneRoute route = lane.Route;
        int join = RvrLaneChoice.JoinIndex(route, origin, _laneWalked, out float lateral);
        string failure = null;
        Vector3[] head = null, tail = [];
        if (join < 0) failure = $"{lateral:0} units from the lane";
        else if (!RvrKeepRoute.TryBuild(bot.CurrentRegion, nav, bot.Realm, origin, route.Walk[join], out head))
            failure = "no planner route onto the lane";
        else if (Vector3.DistanceSquared(route.Walk[^1], endpoint) > 80 * 80 &&
                 !RvrKeepRoute.TryBuild(bot.CurrentRegion, nav, bot.Realm, route.Walk[^1], endpoint, out tail))
            failure = "no planner route from the lane's end to the keep";
        if (failure != null)
        {
            _laneTravelFailed = lane.Tag;
            long now = GameLoop.GameLoopTime;
            // One line a minute per lane, not one per marcher.
            if (!LaneJoinLogs.TryGetValue(lane.Tag, out long last) || now - last >= 60_000)
            {
                LaneJoinLogs[lane.Tag] = now;
                if (LaneJoinLogs.Count > 512) LaneJoinLogs.Clear();
                Log.Info($"RVR_LANE_FALLBACK force=\"{destination.Id}\" bot=\"{bot.Name}\" region={bot.CurrentRegionID} " +
                         $"realm={GlobalConstants.RealmToName(bot.Realm)} lane={route.LaneIndex} reason={RvrLaneFallback.Join} " +
                         $"detail=\"{failure}\" purpose={lane.Purpose} from=\"{route.FromRef}\" to=\"{route.ToRef}\"");
            }
            return false;
        }
        var all = new System.Collections.Generic.List<Vector3>(head.Length + route.Walk.Length + tail.Length);
        all.AddRange(head);
        for (int i = join + 1; i < route.Walk.Length; i++) all.Add(route.Walk[i]);
        all.AddRange(tail);
        steps = all.ToArray();
        return true;
    }

    private static long KeepTravelGeometry(Region region, Zone destination, string targetId)
    {
        // A distant border door opening must not restart every defender's job.
        // Intermediate cached queries carry their own zone revision instead.
        long stamp = NavigationGeometryRevision.Read(destination);
        // Ownership can change permission to traverse a closed friendly gate.
        foreach (var keep in GameServer.KeepManager.GetKeepsOfRegion(region.ID))
        {
            stamp = unchecked(stamp * 31 + keep.KeepID * 7 + (int)keep.Realm);
            if ($"rvr-keep-{keep.KeepID}" != targetId) continue;
            foreach (var door in keep.Doors.Values)
                stamp = unchecked(stamp * 31 + (int)door.State * 7 + (door.IsAlive ? 1 : 0));
        }
        return stamp;
    }

    private string KeepTravelKey(GameBot bot, CampDestination destination) =>
        $"{bot.PersistentRecord?.ObjectiveAssignmentId}:{bot.CurrentRegionID}:{bot.Realm}:{destination.Id}:{destination.X}:{destination.Y}:{destination.Z}" +
        $":{ActiveLaneTravel(bot, destination)?.Tag}";

    /// <returns>True while planning/travelling/waiting. False only at the proved
    /// endpoint, so existing combat and patrol decisions retain control there.</returns>
    private bool FollowKeepTravel(GameBot bot, CampDestination destination)
    {
        long now = GameLoop.GameLoopTime;
        Vector3 current = new(bot.X, bot.Y, bot.Z);
        string key = KeepTravelKey(bot, destination);
        long geometry = KeepTravelGeometry(bot.CurrentRegion, bot.CurrentRegion.GetZone(destination.X,destination.Y),destination.Id);
        if (_keepTravelKey != key || _keepTravelGeometry != geometry ||
            _keepTravelLastPosition.HasValue && Vector3.DistanceSquared(current, _keepTravelLastPosition.Value) > 8000 * 8000 ||
            _keepTravelPoints != null && _keepTravelIndex >= _keepTravelPoints.Length && Vector3.DistanceSquared(current, _keepTravelPoints[^1]) > 650 * 650 ||
            _keepPlanning != null && Vector3.DistanceSquared(current, _keepPlanningOrigin) > 96 * 96)
        {
            _keepTravelKey = key; _keepTravelGeometry = geometry;
            _keepTravelPoints = null; _keepPlanning = null; _keepTravelIndex = 0;
            _keepTravelRetry = 0; _keepTravelFailures = 0;
            _rvrApproachDestination = null;
            _rvrTravelWaypoint = null; _patrolDestination = null;
        }
        _keepTravelLastPosition = current;
        if (_keepTravelPoints == null)
        {
            if (now < _keepTravelRetry) return true;
            if (_keepPlanning == null)
            {
                // Cancel only the obsolete travel order. Otherwise a redirected
                // roamer moves the search origin every slice and never finishes.
                bot.StopMovingOnPath(); bot.StopMoving();
                _keepPlanningOrigin = current;
                _keepPlanning = new RvrPlanningNavigation(AutonomousKeepApproachNavigation.ForRealm(
                    PathfindingProvider.Instance, bot.CurrentRegion, bot.Realm));
            }
            _keepPlanning.BeginSlice();
            var planningWork = _keepPlanning;
            string failure = "No connected exterior route";
            try
            {
                // A march or roam lane (work package 5) is walked as these same keep-travel points; without one, or when
                // the lane cannot be joined, today's route.
                if (TryResolveKeepTravelApproach(bot, destination, _keepPlanning, _keepPlanningOrigin, out var endpoint) &&
                    (TryBuildLaneTravel(bot, destination, _keepPlanning, _keepPlanningOrigin, endpoint, out var steps) ||
                     RvrKeepRoute.TryBuild(bot.CurrentRegion, _keepPlanning, bot.Realm, _keepPlanningOrigin, endpoint, out steps)))
                {
                    _keepTravelPoints = steps; _keepTravelIndex = 0;
                    _rvrApproachDestination = endpoint;
                    _keepPlanning = null; _keepTravelFailures = 0;
                    // A lane that could not be joined fell back to today's route just now: keep that route.
                    _keepTravelKey = KeepTravelKey(bot, destination);
                }
            }
            catch (RvrPlanningNavigation.Yield)
            {
                _keepTravelRetry = now + 250;
                if (bot.Brain is BotBrain brain) brain.ThinkInterval = 250;
                SetRvrStatus(bot, "Planning keep route", destination.MonsterName,
                    "Continuing a bounded route search; combat remains responsive");
                return true;
            }
            catch (RvrPlanningNavigation.Limit) { failure = "Bounded route-work limit reached"; }
            finally
            {
                // Charge only work actually spent planning. Combat, casting,
                // transport and background AI cadence must not consume the
                // search's time allowance and discard a valid partial route.
                planningWork.EndSlice();
            }
            if (_keepTravelPoints == null)
            {
                int queries = _keepPlanning.Queries;
                _keepPlanning = null;
                _keepTravelFailures = Math.Min(4, _keepTravelFailures + 1);
                if (failure == "No connected exterior route" && TryRestartKeepTravelAtArrival(bot, destination, current, queries))
                    return true;
                _keepTravelRetry = now + _keepTravelFailures * 30_000 + Math.Abs(bot.DatabaseID % 3000);
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Keep route retry", destination.MonsterName,
                    $"{failure}; retaining the siege and backing off before retrying");
                Log.Warn($"RVR_KEEP_ROUTE_FAILED bot=\"{bot.Name}\" id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                    $"target=\"{destination.Id}\" region={bot.CurrentRegionID} from={current} queries={queries} " +
                    $"reason=\"{failure}\" retryMs={_keepTravelRetry-now}");
                return true;
            }
        }
        // Retain the existing attacker's optional real stable journey. The
        // horse still owns movement exclusively; replan from its real arrival.
        if (_keepTravelIndex == 0 && _rvrIntent != AutonomousRvrEventLayer.Intent.DefendEvent &&
            IsInFrontier(bot) && Vector2.Distance(new(current.X,current.Y),
                new(_keepTravelPoints[^1].X,_keepTravelPoints[^1].Y)) > 650 &&
            TryBeginFasterStableRoute(bot,_keepTravelPoints[^1],destination.ZoneName))
        {
            _keepTravelKey = null; _keepTravelPoints = null;
            return true;
        }
        while (_keepTravelIndex < _keepTravelPoints.Length &&
            Vector3.DistanceSquared(current, _keepTravelPoints[_keepTravelIndex]) <= 80 * 80)
            _keepTravelIndex++;
        if (_keepTravelIndex >= _keepTravelPoints.Length)
        {
            _rvrApproachDestination = _keepTravelPoints[^1];
            return false;
        }
        Vector3 next = _keepTravelPoints[_keepTravelIndex];
        if (HandleKeepTravelStall(bot, destination, current, next, now))
            return true;
        Vector3 leg = KeepTravelLeg(bot, current, next);
        var zone = bot.CurrentRegion.GetZone((int)next.X, (int)next.Y);
        if (zone != bot.CurrentZone && _keepTravelIndex > 0 &&
            Vector3.DistanceSquared(current, next) <= 256 * 256)
        {
            // Only the explicit inside/outside pair of an already-proved seam.
            // No teleport, wall shortcut, or fresh cross-region chord.
            if (!bot.IsMoving) bot.WalkTo(next, bot.MaxSpeed);
        }
        // The next leg starts at once: a moving bot kept its old order, so it walked into the waypoint it had just
        // reached, stopped there and waited for its next AI turn (2.6-4.2 s) at every waypoint of the march (run 22:
        // 255 kept orders in five minutes).
        else if (!IssuePath(bot, leg, preciseArrival: leg == next, retargetWhileMoving: true))
        {
            _keepHop = null;
            _keepTravelPoints = null; _keepPlanning = null;
            _keepTravelRetry = now + 30_000;
            return true;
        }
        SetRvrStatus(bot, "Traveling to keep", destination.MonsterName,
            $"Following validated road segment {_keepTravelIndex+1}/{_keepTravelPoints.Length}" +
            (leg == next ? string.Empty : " in a shorter leg"));
        return true;
    }

    /// <summary>
    /// A keep route that keeps failing from a spot by the realm's arrival point starts again from that point, the porter's
    /// drop and the muster centre (AutonomousRvrMusterPost.TryRestartPoint), instead of waiting there for the 15-minute
    /// watchdog to send the bot to its capital. Logs RVR_KEEP_ROUTE_RESTART.
    /// </summary>
    private bool TryRestartKeepTravelAtArrival(GameBot bot, CampDestination destination, Vector3 current, int queries)
    {
        if (!AutonomousRvrMusterPost.TryRestartPoint(bot.Realm, bot.CurrentRegionID, current, _keepTravelFailures, out Vector3 arrival))
            return false;
        Zone zone = bot.CurrentRegion?.GetZone((int)arrival.X, (int)arrival.Y);
        var nav = PathfindingProvider.Instance;
        Vector3 floor = zone != null && nav.IsAvailable && nav.HasNavmesh(zone)
            ? nav.GetClosestPoint(zone, arrival, 160, 160, 800, nav.DefaultFilters) ?? arrival : arrival;
        // #532 step 1a: not in sight of a player; the ordinary retry runs meanwhile and asks again at its next failure.
        if (AutonomousRvrUnseenMove.Holds(bot, floor, "keep_route_restart"))
            return false;
        if (!bot.MoveInRegion(bot.CurrentRegionID, (int)Math.Round(floor.X), (int)Math.Round(floor.Y), (int)Math.Round(floor.Z),
                bot.Heading, true))
            return false;
        Log.Warn($"RVR_KEEP_ROUTE_RESTART bot=\"{bot.Name}\" id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                 $"target=\"{destination.Id}\" region={bot.CurrentRegionID} from={(int)current.X},{(int)current.Y},{(int)current.Z} " +
                 $"to={(int)floor.X},{(int)floor.Y},{(int)floor.Z} failures={_keepTravelFailures} queries={queries}");
        _keepTravelKey = null; _keepTravelPoints = null; _keepPlanning = null;
        _keepTravelRetry = 0; _keepTravelFailures = 0; _keepHop = null;
        ResetRouteOrderState();
        bot.MarkAutonomousStateDirty();
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Recovery);
        SetRvrStatus(bot, "Keep route restart", destination.MonsterName,
            "No route from that spot; starting the keep route again from the realm's arrival point");
        return true;
    }

    /// <summary>Very long same-zone legs are walked in navmesh-proven chunks.</summary>
    private Vector3 KeepTravelLeg(GameBot bot, Vector3 current, Vector3 next)
    {
        if (_keepHop.HasValue && (_keepHopFor != next || Vector3.DistanceSquared(current, _keepHop.Value) <= 300 * 300))
            _keepHop = null;
        if (_keepHop.HasValue)
            return _keepHop.Value;
        Zone zone = bot.CurrentZone;
        if (!KeepTravelStallPolicy.NeedsShorterLeg(current, next) || zone == null ||
            bot.CurrentRegion.GetZone((int)next.X, (int)next.Y) != zone ||
            !KeepTravelStallPolicy.TryShorterLeg(PathfindingProvider.Instance, zone, current, next, out Vector3 hop))
            return next;
        _keepHop = hop;
        _keepHopFor = next;
        return hop;
    }

    /// <summary>
    /// A keep traveller with no progress for a minute replans from where it stands
    /// in a shorter leg; three stalls at one spot release the siege job and back off
    /// instead of idling until the 15-minute watchdog sends the bot home.
    /// </summary>
    private bool HandleKeepTravelStall(GameBot bot, CampDestination destination, Vector3 current, Vector3 next, long now)
    {
        if (_keepStallSince == 0 || Vector3.DistanceSquared(current, _keepStallPosition) >
                KeepTravelStallPolicy.StallRadius * KeepTravelStallPolicy.StallRadius)
        {
            _keepStallPosition = current;
            _keepStallSince = now;
            return false;
        }
        if (!KeepTravelStallPolicy.IsStalled(now - _keepStallSince, bot.InCombat || bot.IsAttacking || bot.IsCasting,
                bot.IsOnStableMasterRoute))
            return false;

        _keepTravelStalls = _keepTravelStalls > 0 && KeepTravelStallPolicy.SameSpot(_keepStallAnchor, current)
            ? _keepTravelStalls + 1 : 1;
        if (_keepTravelStalls == 1)
            _keepStallAnchor = current;
        _keepStallSince = now;
        _keepHop = null;
        Log.Warn($"RVR_KEEP_TRAVEL_STALL bot=\"{bot.Name}\" id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{destination.Id}\" " +
                 $"region={bot.CurrentRegionID} from={(int)current.X},{(int)current.Y},{(int)current.Z} " +
                 $"next={(int)next.X},{(int)next.Y},{(int)next.Z} segment={_keepTravelIndex + 1}/{_keepTravelPoints?.Length ?? 0} " +
                 $"moving={bot.IsMoving} stalls={_keepTravelStalls}");
        bot.StopMovingOnPath();
        bot.StopMoving();
        bot.ForcePathReplot();
        ResetRouteOrderState();
        if (_keepTravelStalls >= KeepTravelStallPolicy.StallsBeforeBackoff)
        {
            _keepTravelStalls = 0;
            _keepTravelPoints = null;
            _keepPlanning = null;
            _keepTravelRetry = now + KeepTravelStallPolicy.BackoffMilliseconds;
            if (_siegeJobKeep != null)
                ReleaseSiegeJob(bot);
            SetRvrStatus(bot, "Keep route stalled", destination.MonsterName,
                "No progress at the same spot three times; replanning after a short back-off");
            return true;
        }
        Zone zone = bot.CurrentZone;
        if (zone != null && bot.CurrentRegion.GetZone((int)next.X, (int)next.Y) == zone &&
            KeepTravelStallPolicy.TryShorterLeg(PathfindingProvider.Instance, zone, current, next, out Vector3 hop))
        {
            _keepHop = hop;
            _keepHopFor = next;
        }
        return false;
    }
}
