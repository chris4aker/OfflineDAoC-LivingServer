using System;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private long _expeditionRouteRetry;
    private long _expeditionOrdinaryUntil;
    private int _expeditionRouteFailures;
    private Vector3[] _expeditionSeams;
    private int _expeditionLeg;
    private string _expeditionEvent;
    private ushort _expeditionRegion;
    private Vector3 _expeditionGoal;
    private Vector3? _expeditionLastPosition;
    private Vector3? _expeditionOrdinaryFrom;
    private int _expeditionOrdinaryStill;
    private RvrPlanningNavigation _dragonRallyPlanning;
    private Vector3[] _dragonRallyRoute;
    private Vector3 _dragonRallyOrigin, _dragonRallyGoal;
    private int _dragonRallyLeg;
    private string _dragonRallyEvent;
    private long _dragonRallyRetry;
    private bool TravelRealmExpedition(GameBot bot, AutonomousRealmRaid.View order)
    {
        using var aiProfile = AiProfile.Scope(AiSystem.ControllerRaidTravel); // AI_PROFILE: measures only
        if (GameLoop.GameLoopTime < _expeditionRouteRetry) return true;
        var c = order.Camp;
        if (_camp == null || _camp.RegionId != c.RegionId || _camp.X != c.X || _camp.Y != c.Y || _camp.Z != c.Z)
            _camp = FromSharedCamp(c);
        if (AutonomousRealmRaid.TryPendingDragonRally(bot,out var home) && bot.CurrentRegionID == c.RegionId &&
            Vector2.DistanceSquared(new(bot.X,bot.Y),new(home.X,home.Y)) <= DragonRallyRoute.ApproachRadius*DragonRallyRoute.ApproachRadius &&
            Vector2.DistanceSquared(new(c.X,c.Y),new(home.X,home.Y)) <= DragonRallyRoute.ApproachRadius*DragonRallyRoute.ApproachRadius)
            return TravelDragonRally(bot,order,home);
        _dragonRallyPlanning=null; _dragonRallyRoute=null; _dragonRallyEvent=null;
        // After repeated exterior-corridor failures from one spot (a stable landing whose
        // corridor never proves, Vindsaul Faste 2026-10-06) the ordinary region travel below
        // takes over for a while; it has the usual route and pocket recovery.
        if (GameLoop.GameLoopTime >= _expeditionOrdinaryUntil &&
            AutonomousRealmRaid.TryIndependentApproach(bot, out var exterior, out var via))
        {
            Vector3 position = new(bot.X, bot.Y, bot.Z);
            if (_expeditionEvent != order.EventId || _expeditionRegion != bot.CurrentRegionID ||
                Vector3.DistanceSquared(exterior, _expeditionGoal) > 512 * 512 ||
                _expeditionLastPosition.HasValue && Vector3.DistanceSquared(position, _expeditionLastPosition.Value) > 2048 * 2048)
            {
                _expeditionSeams = null;
                _expeditionEvent = order.EventId; _expeditionRegion = bot.CurrentRegionID; _expeditionGoal = exterior;
            }
            _expeditionLastPosition = position;
            if (_expeditionSeams == null)
            {
                if (!RealmRaidMuster.TryRoute(bot.CurrentRegion, PathfindingProvider.Instance, bot.Realm,
                    new(bot.X, bot.Y, bot.Z), exterior, out _expeditionSeams, via))
                {
                    _expeditionSeams = null;
                    if (++_expeditionRouteFailures >= 4)
                    {
                        _expeditionRouteFailures = 0;
                        _expeditionOrdinaryUntil = GameLoop.GameLoopTime + 120_000;
                        // Live run 2026-10-22: 23 Hibernian raiders stood at one Cruachan Gorge spot for 33 minutes logging
                        // this every three minutes, and nothing said why (the same route proves offline). The reason, the
                        // goal and how many windows in a row the bot has not moved say where to look next time.
                        _expeditionOrdinaryStill = _expeditionOrdinaryFrom.HasValue &&
                            Vector3.DistanceSquared(position, _expeditionOrdinaryFrom.Value) < 64 * 64 ? _expeditionOrdinaryStill + 1 : 0;
                        _expeditionOrdinaryFrom = position;
                        Log.Warn($"REALM_EXPEDITION_ORDINARY_TRAVEL bot={bot.Name} id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                            $"position={bot.X},{bot.Y},{bot.Z} event={order.EventId} goal={(int)exterior.X},{(int)exterior.Y},{(int)exterior.Z} " +
                            $"via={(via.HasValue ? $"{(int)via.Value.X},{(int)via.Value.Y}" : "-")} stillWindows={_expeditionOrdinaryStill} " +
                            $"reason=\"{RealmRaidMuster.LastRouteFailure}\" status=\"{bot.PersistentRecord?.Activity}: {bot.PersistentRecord?.ObjectiveProgress}\"");
                        return true;
                    }
                    _expeditionRouteRetry = GameLoop.GameLoopTime + 15_000;
                    bot.StopMovingOnPath(); bot.StopMoving();
                    SetStatus(bot, "Expedition route retry", c.MonsterName, "No connected exterior corridor; retaining assignment and retrying safely");
                    return true;
                }
                _expeditionRouteFailures = 0;
                _expeditionLeg = 0;
            }
            while (_expeditionLeg < _expeditionSeams.Length &&
                Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), _expeditionSeams[_expeditionLeg]) <= 64 * 64)
                _expeditionLeg++;
            if (_expeditionLeg < _expeditionSeams.Length)
            {
                GuardExpeditionDanger(bot, _expeditionSeams[_expeditionLeg]);
                if (!IssuePath(bot, _expeditionSeams[_expeditionLeg], preciseArrival: true))
                { _expeditionSeams = null; _expeditionRouteRetry = GameLoop.GameLoopTime + 15_000; }
                SetStatus(bot, "Traveling to expedition", c.MonsterName, "Following the validated exterior corridor; no town attendance required");
                return true;
            }
        }
        // Released/late members own their trip to the encounter. No follower
        // gate, all-eight meetup or dead leader can hold them in another region.
        if (bot.CurrentRegionID != c.RegionId) return TravelAcrossRegions(bot);
        Vector3 post = new(c.X,c.Y,c.Z);
        int arrival = order.Crossing ? 32 : 175;
        if (Vector3.DistanceSquared(new(bot.X,bot.Y,bot.Z),post) > arrival * arrival)
        {
            if (FollowDarknessFallsRaidChain(bot, post, c.MonsterName)) return true;
            if (!TryBeginFasterStableRoute(bot,post,c.ZoneName))
            {
                GuardExpeditionDanger(bot, post);
                IssuePath(bot,post,preciseArrival:order.Crossing);
            }
            SetStatus(bot,order.State,c.MonsterName,"Traveling to the expedition; late members rejoin without town staging");
            return true;
        }
        if (!order.Hold) return false;
        bot.StopMovingOnPath();
        bot.StopMoving();
        SetStatus(bot,order.State,c.MonsterName,"Present at expedition staging; waiting for the expedition order");
        return true;
    }

    /// <summary>
    /// A raider walking to its expedition on its own (late, or back from a release) bends around monsters far
    /// above it and remembered death spots, as a solo bot does on any road. Caer Sidi rallies, 2026-10-19/20:
    /// 124 level-50 raiders died on the walk to the staging area, most to dread liches (57-61) and ancient
    /// zombies (60), and released 110,000 units back at Fort Gwyntell each time; 25-37 were still on the
    /// corridor when both rallies ran out of time.
    /// </summary>
    private void GuardExpeditionDanger(GameBot bot, Vector3 destination)
    {
        if (!AutonomousRouteThreatPolicy.Enabled || bot.CurrentZone == null || bot.CurrentZone.IsDungeon ||
            bot.CurrentRegion?.IsDungeon == true || bot.InCombat || _routeRecoveryWaypoint.HasValue ||
            bot.Brain is not BotBrain { HasAggro: false })
            return;
        long now = GameLoop.GameLoopTime;
        if (now < _nextRouteThreatScanTick) return;
        _nextRouteThreatScanTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds + bot.ObjectID % 500;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return;
        Span<Vector3> route = stackalloc Vector3[257];
        route[0] = new(bot.X, bot.Y, bot.Z);
        int count = bot.movementComponent.CopyUpcomingPath(destination, route[1..]) + 1;
        if (count < 2) { route[1] = destination; count = 2; }
        TryDangerBypass(bot, nav, bot.CurrentZone, route[..count]);
    }

    private bool TravelDragonRally(GameBot bot, AutonomousRealmRaid.View order, Vector3 home)
    {
        long now=GameLoop.GameLoopTime;
        Vector3 current=new(bot.X,bot.Y,bot.Z), goal=new(order.Camp.X,order.Camp.Y,order.Camp.Z);
        if (_dragonRallyEvent!=order.EventId || _dragonRallyGoal!=goal ||
            _dragonRallyPlanning!=null && Vector3.DistanceSquared(current,_dragonRallyOrigin)>96*96)
        {
            _dragonRallyEvent=order.EventId; _dragonRallyGoal=goal;
            _dragonRallyPlanning=null; _dragonRallyRoute=null; _dragonRallyRetry=0;
        }
        if(now<_dragonRallyRetry) return true;
        if(_dragonRallyRoute==null)
        {
            if(_dragonRallyPlanning==null)
            {
                bot.StopMovingOnPath();bot.StopMoving();
                _dragonRallyOrigin=current;
                _dragonRallyPlanning=new RvrPlanningNavigation(PathfindingProvider.Instance);
            }
            var work=_dragonRallyPlanning; work.BeginSlice();
            bool found=false;
            try
            {
                found=DragonRallyRoute.TryBuild(work,bot.CurrentZone,_dragonRallyOrigin,goal,home,out var route);
                if(found) _dragonRallyRoute=route;
            }
            catch(RvrPlanningNavigation.Yield)
            { _dragonRallyRetry=now+250; SetStatus(bot,"Planning safe dragon rally route",order.Camp.MonsterName,"Routing around the dragon; normal defense remains active"); return true; }
            catch(RvrPlanningNavigation.Limit) { }
            finally { work.EndSlice(); }
            _dragonRallyPlanning=null;
            if(!found)
            {
                _dragonRallyRoute=null;_dragonRallyRetry=now+30_000;
                SetStatus(bot,"Dragon rally route retry",order.Camp.MonsterName,"No verified approach outside dragon aggro; not charging through the boss");
                Log.Warn($"DRAGON_RALLY_ROUTE_RETRY bot={bot.Name} event={order.EventId} from={current} goal={goal} queries={work.Queries}");
                return true;
            }
            _dragonRallyLeg=0;
        }
        while(_dragonRallyLeg<_dragonRallyRoute.Length && Vector3.DistanceSquared(current,_dragonRallyRoute[_dragonRallyLeg])<=80*80)
            _dragonRallyLeg++;
        if(_dragonRallyLeg==_dragonRallyRoute.Length)
        {
            bot.StopMovingOnPath();bot.StopMoving();
            SetStatus(bot,order.State,order.Camp.MonsterName,"At safe dragon staging; waiting for the expedition order");
            return true;
        }
        if(!IssuePath(bot,_dragonRallyRoute[_dragonRallyLeg],preciseArrival:true))
        { _dragonRallyRoute=null;_dragonRallyRetry=now+15_000;bot.StopMovingOnPath();bot.StopMoving(); }
        else SetStatus(bot,"Traveling to dragon rally",order.Camp.MonsterName,"Following a checked route outside the dragon's aggro area");
        return true;
    }

    // Consecutive expedition route failures from (nearly) the same floor point.
    // A member on a disconnected navmesh island (a ledge, a fall, a bad respawn
    // landing) cannot path to any route stage; it used to retry every 30 seconds
    // for the whole four-hour expedition.
    public const int StrandedExpeditionFailureLimit = 3;
    public const float StrandedExpeditionRadius = 400;
    private int _strandedExpeditionFailures;
    private Vector3 _strandedExpeditionPosition;

    public static bool ShouldRegroupStrandedMember(int failures, bool alive, bool inCombat) =>
        alive && !inCombat && failures >= StrandedExpeditionFailureLimit;

    private void TryRegroupStrandedExpeditionMember(GameBot bot)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        _strandedExpeditionFailures = Vector2.DistanceSquared(new(here.X, here.Y),
            new(_strandedExpeditionPosition.X, _strandedExpeditionPosition.Y)) <= StrandedExpeditionRadius * StrandedExpeditionRadius
            ? _strandedExpeditionFailures + 1 : 1;
        _strandedExpeditionPosition = here;
        if (!ShouldRegroupStrandedMember(_strandedExpeditionFailures, bot.IsAlive, bot.InCombat) ||
            !AutonomousRealmRaid.TryDungeonRegroupPoint(bot, out ushort region, out Vector3 front) ||
            Vector3.DistanceSquared(here, front) <= 300 * 300)
            return;
        _strandedExpeditionFailures = 0;
        if (!bot.MoveInRegion(region, (int)front.X, (int)front.Y, (int)front.Z, bot.Heading, true))
            return;
        _expeditionRouteRetry = 0;
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Recovery);
        Log.Warn($"REALM_EXPEDITION_STRANDED_REGROUP bot={bot.Name} id={bot.DatabaseID} region={region} " +
                 $"from={(int)here.X},{(int)here.Y},{(int)here.Z} to={(int)front.X},{(int)front.Y},{(int)front.Z}");
    }
}
