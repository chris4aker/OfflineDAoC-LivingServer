using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private string _physicalRallyKey;
    private Vector3? _physicalRallyPost;
    private long _nextRallyProjection;

    private string _musterKey;
    private Vector3? _musterPost;

    /// <summary>
    /// Staged assault, first stage: an attacker gathers at a reserved post around its realm's portal
    /// outpost in the target frontier (else its border keep) until the army's march order. Warbands used to walk
    /// into the enemy frontier one at a time and were picked off before the siege camp filled.
    /// </summary>
    private bool HandleSiegeMuster(GameBot bot, string forceId, AutonomousRvrEventLayer.RallyOrder order)
    {
        AutonomousRvrStaging.BorderKeep home;
        if (bot.Realm == order.Attacker ? !AutonomousRvrStaging.TryGetSiegeMuster(bot.Realm, _rvrDestination.RegionId, out home)
                : !TryGetDefenseMuster(bot, order.TargetId, out home)) return false;
        var members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().OrderBy(member => member.DatabaseID).ToArray() ?? [bot];
        int memberIndex = Array.IndexOf(members, bot);
        if (memberIndex < 0 || memberIndex >= order.Slots.Length) return true;
        int slot = order.Slots[memberIndex];
        string key = $"{order.TargetId}:{forceId}:{slot}";
        if (_musterKey != key) { _musterKey = key; _musterPost = null; }
        Region region = WorldMgr.GetRegion(home.RegionId);
        if (!_musterPost.HasValue && region != null)
        {
            // A sunflower spread around the keep: every slot its own spot, 120 units per ring. A spot with no walk from
            // the muster centre takes the nearest one round its ring that has one (AutonomousRvrMusterPost).
            _musterPost = AutonomousRvrMusterPost.Choose(PathfindingProvider.Instance,
                point => region.GetZone((int)point.X, (int)point.Y), home.Position, slot);
        }
        Vector3 post = _musterPost ?? home.Position;
        long now = GameLoop.GameLoopTime;
        bool atPost = bot.CurrentRegionID == home.RegionId &&
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), post) <= 160 * 160;
        AutonomousRvrEventLayer.ReportMusterAttendance(order.TargetId, forceId, bot.DatabaseID,
            atPost && bot.IsAlive && !bot.InCombat && (bot.Brain as BotBrain)?.HasAggro != true, now, bot, post);
        if (atPost)
        {
            // Holding the post (in a fight or not) is the progress the straggler sweep looks for (RVR_RALLY_DELAY).
            AutonomousRvrEventLayer.ReportTravel(order.TargetId, forceId, bot.DatabaseID, double.PositiveInfinity, true, now, bot);
            bot.StopMovingOnPath();
            bot.StopMoving();
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, $"Mustering at {home.Name}", order.TargetId,
                "Gathering with the realm's army; it marches on the keep together once enough have assembled");
            return true;
        }
        // A plain point, not the target keep's id: with "rvr-keep-N" the walk took the keep-assault
        // approach planner (gates of a keep in another region) and nobody reached the muster (run 10).
        TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-muster-{home.RegionId}", RegionId = home.RegionId,
            X = (int)post.X, Y = (int)post.Y, Z = (int)post.Z });
        // Abroad, the frontier transport's own status (medallion, boarding) stays visible.
        if (bot.CurrentRegionID == home.RegionId)
            SetRvrStatus(bot, $"Joining the muster at {home.Name}", order.TargetId,
                "Walking to the army's muster at the border keep before the march on the siege target");
        return true;
    }

    /// <summary>
    /// The defense rally gathers at the friendly keep or tower nearest the attacked keep (at least 3,000 units
    /// away, same region), else the realm's border keep, then marches in together.
    /// </summary>
    private bool TryGetDefenseMuster(GameBot bot, string targetId, out AutonomousRvrStaging.BorderKeep muster)
    {
        muster = default;
        var keeps = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId);
        var target = keeps.FirstOrDefault(keep => $"rvr-keep-{keep.KeepID}" == targetId);
        if (target == null) return false;
        var friendly = keeps.Where(keep => keep != target && keep.Realm == bot.Realm &&
                Vector2.DistanceSquared(new(keep.X, keep.Y), new(target.X, target.Y)) >= 3_000f * 3_000f)
            .OrderBy(keep => Vector2.DistanceSquared(new(keep.X, keep.Y), new(target.X, target.Y))).FirstOrDefault();
        if (friendly != null)
        {
            muster = new(friendly.Region, new(friendly.X, friendly.Y, friendly.Z), friendly.Name);
            return true;
        }
        return AutonomousRvrStaging.TryGetBorderKeepNear(bot.Realm, target.Region, new(target.X, target.Y), out muster);
    }

    private bool HandleSiegeRally(GameBot bot, string forceId, AutonomousRvrEventLayer.RallyOrder order)
    {
        // The muster is the realm's portal outpost inside the target's frontier, so roamers already
        // there gather with the army too (see AutonomousRvrStaging.TryGetSiegeMuster).
        // Attackers muster at their portal outpost; defenders gather as one quick rally at a nearby friendly keep.
        if (order.HomeMuster && HandleSiegeMuster(bot, forceId, order)) return true;
        var keep = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId)
            .FirstOrDefault(candidate => $"rvr-keep-{candidate.KeepID}" == order.TargetId);
        if (keep == null) return true;
        if (bot.Realm == order.Attacker && TryMarchWithArmy(bot, keep, order.TargetId)) return true;
        // A distant responder must use the same bounded, retained keep route.
        // Do not run a fresh full-country reachability test for every rally slot.
        if (bot.CurrentRegionID == keep.Region &&
            Vector2.DistanceSquared(new(bot.X,bot.Y),new(keep.X,keep.Y)) > 11_500 * 11_500)
        {
            FollowKeepTravel(bot, _rvrDestination);
            return true;
        }
        bool operatorBot = _groupDirective?.IsDynamic != true || _groupDirective.Leader == bot;
        // Equipment procurement belongs to the bounded per-keep job pool.
        // Do not send every arriving leader (and their followers) shopping.
        bot.TempProperties.RemoveProperty("RvrSupplying");
        if (operatorBot && bot.CurrentRegionID != keep.Region && TryFrontierTransport(bot,_rvrDestination)) return true;
        GameBot leader = _groupDirective?.Leader;
        if (leader != null && leader != bot && leader.IsAlive)
        {
            if (bot.CurrentRegionID != keep.Region && TryFrontierTransport(bot,_rvrDestination)) return true;
            if (leader.CurrentRegionID != keep.Region ||
                Vector2.Distance(new(leader.X,leader.Y),new(keep.X,keep.Y)) > AutonomousRvrRally.CampRadius+2500)
                return FollowDynamicGroupLeader(bot,_groupDirective);
        }
        if (operatorBot && _groupDirective?.IsDynamic == true &&
            (bot.CurrentRegionID != keep.Region || Vector2.Distance(new(bot.X,bot.Y),new(keep.X,keep.Y)) > AutonomousRvrRally.CampRadius+2500) &&
            !AutonomousBotGroupCoordinator.IsCohesive(_groupDirective))
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            SetRvrStatus(bot,"Regrouping en route to rally",keep.Name,"Waiting for nearby warband members before continuing together");
            return true;
        }
        var members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().OrderBy(member => member.DatabaseID).ToArray() ?? [bot];
        int memberIndex = Array.IndexOf(members, bot);
        if (memberIndex < 0 || memberIndex >= order.Slots.Length) return true;
        string key = $"{order.TargetId}:{forceId}:{order.Slots[memberIndex]}";
        if (_physicalRallyKey != key)
        {
            _physicalRallyKey = key;
            _physicalRallyPost = null;
            _nextRallyProjection = 0;
        }
        long now = GameLoop.GameLoopTime;
        if (!_physicalRallyPost.HasValue && now >= _nextRallyProjection)
        {
            _nextRallyProjection = now + 15_000;
            if (AutonomousRvrRally.TryPost(bot, keep, order, memberIndex, out Vector3 point))
                _physicalRallyPost = point;
        }
        if (!_physicalRallyPost.HasValue)
        {
            // Run 19: 27 of 64 Hibernian attackers stood still at Odin's Gate, beside the marching column on the same
            // navmesh, because the full corridor check to a post 10-11k away failed. Keep closing in on the keep's
            // retained route (as distant responders do) and retry the post every 15 s; never stand idle.
            FollowKeepTravel(bot, _rvrDestination);
            SetRvrStatus(bot, "Closing on the rally", keep.Name,
                "No validated rally post yet; walking the keep route and retrying (not counted as present)");
            return true;
        }
        Vector3 post = _physicalRallyPost.Value;
        bool atPost = bot.CurrentRegionID == keep.Region &&
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), post) <= AutonomousRvrRally.ArrivalRadius * AutonomousRvrRally.ArrivalRadius;
        // Different regions have unrelated coordinates; use a large sentinel
        // until the actual approach starts instead of rewarding a wrong map.
        AutonomousRvrEventLayer.ReportTravel(order.TargetId, forceId, bot.DatabaseID,
            bot.CurrentRegionID == keep.Region ? Vector3.Distance(new(bot.X, bot.Y, bot.Z), post) : double.PositiveInfinity,
            atPost, now, bot);
        if (atPost)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        AutonomousRvrEventLayer.ReportAttendance(order.TargetId, forceId, bot.Realm, bot.DatabaseID,
            atPost && bot.IsAlive && !bot.InCombat && (bot.Brain as BotBrain)?.HasAggro != true, now, bot, post);
        if (atPost)
        {
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, "Holding siege rally", keep.Name,
                $"{(bot.Realm == order.Defender ? "Holding an interior defensive post" : "Holding the realm's separated defensive camp")}; " +
                $"attackers advance when their recruited force is ready after at least three minutes; no enemy attendance requirement; " +
                $"preparation deadline in {Math.Max(0, (int)Math.Ceiling(TimeSpan.FromMilliseconds(order.RemainingMilliseconds).TotalMinutes))}m " +
                $"({(keep.IsRelic ? 192 : 128)} cap per realm; actual fighting can start the battle earlier)");
            return true;
        }

        var destination = _rvrDestination with { X = (int)post.X, Y = (int)post.Y, Z = (int)post.Z };
        if (bot.CurrentRegionID != keep.Region)
            TravelRvrObjective(bot, destination);
        else if (bot.Realm == order.Defender)
            TravelToDefensivePost(bot, keep, post);
        else
        {
            // Keep the outbound warband around its leader until close to the
            // siege camp, then let each member settle into its reserved post.
            if (leader != null && leader != bot && leader.IsAlive && leader.CurrentRegionID == bot.CurrentRegionID &&
                Vector3.DistanceSquared(new(leader.X, leader.Y, leader.Z), post) > 2200 * 2200)
                FollowDynamicGroupLeader(bot, _groupDirective);
            else IssueVariedRvrPath(bot, post);
        }
        SetRvrStatus(bot, "Traveling to siege rally", keep.Name,
            bot.Realm == order.Defender ? "Entering the friendly keep to take a defensive post" :
                "Joining this realm's separate rally camp outside the keep");
        return true;
    }

    private long _armyRegroupUntil;
    private long _nextArmyRegroup;
    public const int ArmyAssistRadius = 2500;
    public const int ArmyCombatHoldRadius = 8000;
    private long _armyCombatHoldUntil;
    private long _nextArmyCombatHold;
    public const int ArmyKeepUpDistance = 900;
    public const int ArmyArrivalRadius = 3000;
    // Run 10 (2026-10-15): Dun Dagda's siege opened with 11 of the army present after five minutes; the leader now waits
    // up to twelve minutes at the camp edge for the column.
    public const long ArmyArrivalHoldMilliseconds = 12 * 60_000;
    private long _armyArrivalHoldSince;
    private Vector3? _marchDetour;
    private const string ArmyDetourProperty = "ArmyMarchDetour";
    private const string ArmyPushProperty = "ArmyMarchPush";
    private AutonomousArmyPush.State _armyPush;
    private long _marchDetourUntil, _nextMarchDetourCheck;

    /// <summary>The march leader's way around the first enemy keep ahead (AutonomousArmyDetour). True when it walks the detour.</summary>
    private bool TryArmyDetour(GameBot bot, AbstractGameKeep keep, string targetId)
    {
        long now = GameLoop.GameLoopTime;
        Vector2 here = new(bot.X, bot.Y);
        if (_marchDetour is Vector3 detour)
        {
            if (Vector2.Distance(here, new(detour.X, detour.Y)) <= AutonomousArmyDetour.ReachedRadius || now > _marchDetourUntil)
            {
                _marchDetour = null;
                _nextMarchDetourCheck = 0;
            }
            else
            {
                TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-detour-{targetId}", X = (int)detour.X, Y = (int)detour.Y, Z = (int)detour.Z });
                return true;
            }
        }
        if (now < _nextMarchDetourCheck) return false;
        _nextMarchDetourCheck = now + 20_000;
        Vector2? point = AutonomousArmyDetour.FirstDetour(here, new(keep.X, keep.Y),
            AutonomousArmyDetour.HazardsFor(bot.CurrentRegionID, bot.Realm, keep));
        if (point == null) return false;
        Zone zone = bot.CurrentRegion?.GetZone((int)point.Value.X, (int)point.Value.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? floor = nav.GetClosestPoint(zone, new(point.Value.X, point.Value.Y, bot.Z), 2_000, 2_000, 4_000, nav.DefaultFilters);
        if (floor == null) return false;
        _marchDetour = floor;
        _marchDetourUntil = now + 10 * 60_000;
        Log.Info($"RVR_MARCH_DETOUR target={targetId} leader={bot.Name} from={(int)bot.X},{(int)bot.Y} " +
                 $"via={(int)floor.Value.X},{(int)floor.Value.Y},{(int)floor.Value.Z}");
        TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-detour-{targetId}", X = (int)floor.Value.X, Y = (int)floor.Value.Y, Z = (int)floor.Value.Z });
        return true;
    }

    /// <summary>
    /// The march from the muster to the siege camp as one column: the leader walks the keep route and holds briefly
    /// when most of the army has fallen behind; everyone else keeps within reach of the leader; a marcher who is not
    /// fighting joins the fight of any army member under attack nearby. Run 17: Midgard's 64 on Caer Sursbrooke and
    /// Albion's 64 on Blendrake Faste each walked 50,000+ units one by one and lost half their strength to roaming
    /// enemies before either reached its camp. Only for the long march; inside the camp radius each bot takes its post.
    /// </summary>
    private bool TryMarchWithArmy(GameBot bot, AbstractGameKeep keep, string targetId)
    {
        // Any march turn counts as leading, also while the leader is still travelling to the keep's region (run 3:
        // reporting only inside that region replaced every leader of a cross-realm march every 30 seconds).
        AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
        if (bot.CurrentRegionID != keep.Region) return false;
        Vector2 here = new(bot.X, bot.Y);
        if (Vector2.Distance(here, new(keep.X, keep.Y)) <= AutonomousRvrRally.CampRadius + 2500)
        {
            // Run 19: the march ended here and handed over to rally posts the bots could not validate, so the army
            // stood still 11k from Bledmeer. The army's arrival opens the battle (owner: start once the threshold is
            // met, no waiting at the keep); the bots then go straight to the assault and their siege jobs.
            if (AutonomousRvrEventLayer.MarchLeader(targetId) == bot)
            {
                AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
                // Run 2026-10-13: the leader reached Dun Lamfhota alone and opened the relic siege with one attacker
                // present while its column trailed 6,000-30,000 units behind. The leader now waits at the camp edge
                // (at most five minutes) until most of the column has closed up, then the army attacks together.
                GameBot[] column = AutonomousRvrEventLayer.ArmyMembers(targetId)
                    .Where(member => member.CurrentRegionID == bot.CurrentRegionID).ToArray();
                int closed = column.Count(member => bot.IsWithinRadius(member, ArmyArrivalRadius) ||
                    Vector2.Distance(new(member.X, member.Y), new(keep.X, keep.Y)) <= AutonomousRvrRally.CampRadius + 2500);
                long tick = GameLoop.GameLoopTime;
                if (_armyArrivalHoldSince == 0) _armyArrivalHoldSince = tick;
                if (column.Length >= 8 && closed < column.Length * 6 / 10 && tick - _armyArrivalHoldSince < ArmyArrivalHoldMilliseconds)
                {
                    bot.StopMovingOnPath(); bot.StopMoving();
                    SetRvrStatus(bot, "Army closing up before the assault", keep.Name, $"{closed} of {column.Length} at the camp");
                    return true;
                }
                _armyArrivalHoldSince = 0;
                AutonomousRvrEventLayer.ReportArmyArrived(targetId, tick);
            }
            return false;
        }
        GameBot leader = AutonomousRvrEventLayer.MarchLeader(targetId);
        if (leader == null || leader.CurrentRegionID != bot.CurrentRegionID) return false;
        GameBot[] army = AutonomousRvrEventLayer.ArmyMembers(targetId);
        long now = GameLoop.GameLoopTime;
        // Work package 5: the groups of the column on up to three lanes (null: today's one route).
        MarchLaneTurn? lanes = MarchLanes(bot, leader, keep, targetId, army, now);

        // A column that has stood in one place too long pushes on (AutonomousArmyPush).
        if (leader == bot)
        {
            bool push = AutonomousArmyPush.Update(ref _armyPush, here, now, out bool started);
            bot.TempProperties.SetProperty(ArmyPushProperty, push);
            if (started)
                Log.Info($"RVR_MARCH_PUSH target={targetId} leader={bot.Name} at={(int)bot.X},{(int)bot.Y} " +
                         $"stalledSeconds={AutonomousArmyPush.StallMilliseconds / 1000} column={army.Length}");
        }
        bool pushing = leader.TempProperties.GetProperty<bool>(ArmyPushProperty);
        if (pushing && bot.TargetObject is GameLiving engaged && engaged.Realm != bot.Realm &&
            (leader == bot || engaged.TargetObject != bot))
        {
            if (bot.Brain is BotBrain pushBrain) pushBrain.ClearAggroList();
            bot.StopAttack();
            bot.TargetObject = null;
        }

        // Help a member under attack nearby before anything else (not while pushing on, and not against an enemy this
        // bot already gave up on as unreachable).
        GameBot fighting = pushing ? null : army.FirstOrDefault(member => member != bot && member.CurrentRegionID == bot.CurrentRegionID &&
            (member.InCombat || member.IsAttacking) && member.TargetObject is GameLiving foe && foe.IsAlive &&
            foe.Realm != bot.Realm && !IgnoresTarget(foe) && bot.IsWithinRadius(member, ArmyAssistRadius));
        if (fighting?.TargetObject is GameLiving enemy && bot.Brain is BotBrain brain &&
            GameServer.ServerRules.IsAllowedToAttack(bot, enemy, true))
        {
            bot.TargetObject = enemy;
            brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
            brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
            SetRvrStatus(bot, "Defending the army column", keep.Name, $"Helping {fighting.Name} against {enemy.Name}");
            return true;
        }

        if (leader == bot)
        {
            AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
            // A group level with the leader on its own lane is with the column, though thousands of units to the side.
            int near = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID &&
                (bot.IsWithinRadius(member, ArmyAssistRadius) || LevelOnOtherLane(lanes, member, now)));
            int marching = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID); // the fallen who released home don't hold the column
            if (pushing)
            {
                _armyRegroupUntil = 0;
                _armyCombatHoldUntil = 0;
            }
            if (now < _armyRegroupUntil && near < marching * 6 / 10)
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army regrouping", keep.Name, $"{near} of {marching} with the leader; waiting for the column");
                return true;
            }
            // Run 18: 13 of 50 marchers fought a hunter's avatar 10k behind while the leader walked on, and
            // the column stretched to 10k before they caught up. The leader holds while a quarter of the
            // column is fighting nearby (60 seconds at most, then two minutes before it holds again).
            int fightingBehind = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID &&
                (member.InCombat || member.IsAttacking) && bot.IsWithinRadius(member, ArmyCombatHoldRadius));
            if (!pushing && marching >= 8 && fightingBehind >= Math.Max(3, marching / 4) &&
                (now < _armyCombatHoldUntil || now >= _nextArmyCombatHold))
            {
                if (now >= _armyCombatHoldUntil)
                {
                    _armyCombatHoldUntil = now + 60_000;
                    _nextArmyCombatHold = now + 180_000;
                }
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army holding for the column", keep.Name, $"{fightingBehind} of {marching} fighting behind the leader");
                return true;
            }
            if (!pushing && now >= _nextArmyRegroup && marching >= 8 && near < marching / 2)
            {
                // At most 45 seconds of waiting, then two minutes of marching before the next wait.
                _armyRegroupUntil = now + 45_000;
                _nextArmyRegroup = now + 165_000;
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army regrouping", keep.Name, $"{near} of {marching} with the leader; waiting for the column");
                return true;
            }
            // The leader waits for a flank lane that fell behind (at most 45 seconds, then 45 seconds of marching).
            if (lanes is { } paced && !pushing &&
                RvrLanePacing.LeaderWaits(WorstLaneGap(paced, army, now), now, ref _laneWaitUntil, ref _nextLaneWait))
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army waiting for the flank lanes", keep.Name,
                    $"{near} of {marching} with the leader; a lane is {(int)-WorstLaneGap(paced, army, now)} units behind");
                return true;
            }
            // Lanes passed the same hazards when they were chosen; the leader's detour is for today's route only.
            if (lanes != null) _marchDetour = null;
            bool detouring = lanes == null && TryArmyDetour(bot, keep, targetId);
            bot.TempProperties.SetProperty(ArmyDetourProperty, detouring);
            if (detouring)
            {
                SetRvrStatus(bot, "Leading the army around an enemy keep", keep.Name, $"{near} of {marching} marching with the leader");
                return true;
            }
            FollowKeepTravel(bot, _rvrDestination);
            SetRvrStatus(bot, "Leading the army", keep.Name, $"{near} of {marching} marching with the leader");
            return true;
        }

        // A group on another lane keeps level with the leader by progress along its own lane, not by closing on him.
        if (lanes is { } lane && lane.Slot != lane.LeaderSlot)
        {
            float gap = RvrLanePacing.Gap(lane.Walked, lane.Route, lane.LeaderWalked, lane.LeaderRoute);
            _lanePace = RvrLanePacing.Next(_lanePace, _lanePaceSince, gap, RvrLanePacing.Margins(lane.Tier), now, out _lanePaceSince);
            if (RvrLanePacing.Waits(_lanePace, gap, leader.IsMoving))
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Keeping level with the army", keep.Name,
                    $"On lane {lane.Route.LaneIndex}, {(int)gap} units {(gap >= 0 ? "ahead of" : "behind")} {leader.Name}");
                return true;
            }
            FollowKeepTravel(bot, _rvrDestination);
            SetRvrStatus(bot, "Marching with the army", keep.Name, _lanePace == RvrLanePace.Hurry
                ? $"Catching up on lane {lane.Route.LaneIndex}, {(int)-gap} units behind {leader.Name}"
                : $"On lane {lane.Route.LaneIndex}, level with {leader.Name}");
            return true;
        }
        if (!bot.IsWithinRadius(leader, ArmyKeepUpDistance))
        {
            IssuePath(bot, new Vector3(leader.X, leader.Y, leader.Z));
            SetRvrStatus(bot, "Marching with the army", keep.Name, $"Catching up with {leader.Name}");
            return true;
        }
        if (!leader.IsMoving)
        {
            // The leader is holding (regroup or a fight behind); walking on past it splits the column.
            bot.StopMovingOnPath(); bot.StopMoving();
            SetRvrStatus(bot, "Waiting with the army", keep.Name, $"Holding with {leader.Name}");
            return true;
        }
        // During the leader's detour around an enemy keep the column walks with the leader, not its own keep route.
        if (leader.TempProperties.GetProperty<bool>(ArmyDetourProperty))
        {
            IssuePath(bot, new Vector3(leader.X, leader.Y, leader.Z));
            SetRvrStatus(bot, "Marching with the army", keep.Name, $"Around an enemy keep with {leader.Name}");
            return true;
        }
        FollowKeepTravel(bot, _rvrDestination);
        SetRvrStatus(bot, "Marching with the army", keep.Name, $"In the column with {leader.Name}");
        return true;
    }

    // ---- Work package 5 (docs/RVR-FIELD-1.2.md, "Feet"): the march on lanes. The march leader keeps the pace on his
    // group's lane (lane 0: his group takes it); the other groups walk their own lane's validated walk (FollowKeepTravel)
    // and keep level with him by the share of their lane walked. Fights, pushes, regroups, the combat hold and the arrival
    // hold at the camp are today's. Tests only until seen in the owner's PC log.
    private float _laneWalked = -1;
    private string _laneWalkedTag;
    private RvrLanePace _lanePace;
    private long _lanePaceSince;
    private long _laneWaitUntil, _nextLaneWait;

    private readonly record struct MarchLaneTurn(RvrMarchLanePlan Plan, int Slot, RvrLaneRoute Route, float Walked,
        int LeaderSlot, RvrLaneRoute LeaderRoute, float LeaderWalked, RvrSkillTier Tier);

    private static string MarchForceOf(GameBot bot) =>
        bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";

    /// <summary>Lanes are for autonomous gamebots in bot-only groups; a group a player leads or is in marches as today.</summary>
    private static bool LaneEligible(GameBot bot)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup) return false;
        Group group = bot.Group;
        return group == null || group.Leader == null && group.GetPlayersInTheGroup().Count == 0 && AutonomousRealmRaid.GetView(group) == null;
    }

    private void DropMarchLane()
    {
        if (_laneTravel?.Purpose == "march") UseLaneTravel(null);
    }

    /// <summary>This marcher's lane and the leader's, or null for today's one-route march (switch off, a player's group,
    /// no proven lane: RVR_LANE_FALLBACK once per march).</summary>
    private MarchLaneTurn? MarchLanes(GameBot bot, GameBot leader, AbstractGameKeep keep, string targetId, GameBot[] army, long now)
    {
        if (!RvrTacticsSwitches.Current.MarchLanes || !LaneEligible(bot))
        {
            DropMarchLane();
            return null;
        }
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrLaneCatalog.StartValidation();
        RvrMarchLanePlan plan = RvrMarchLanes.Get(targetId, keep.Region, bot.Realm, now, () => MarchLaneOptions(leader, keep));
        if (!plan.Options.UsesLanes)
        {
            if (plan.TakeFallbackLog())
                Log.Info($"RVR_LANE_FALLBACK force=\"{targetId}\" region={keep.Region} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                         $"reason={plan.Options.Fallback} detail=\"{plan.Options.Detail}\" from=\"{plan.Options.FromRef}\" " +
                         $"to=\"{plan.Options.ToRef}\" column={army.Length} lanes=1");
            DropMarchLane();
            return null;
        }
        string force = MarchForceOf(bot), leaderForce = MarchForceOf(leader);
        RvrMarchLanePlan.ForceLane own = AssignMarchLane(plan, force, force == leaderForce, bot, army);
        RvrMarchLanePlan.ForceLane led = force == leaderForce ? own : AssignMarchLane(plan, leaderForce, true, leader, army);
        RvrLaneRoute route = plan.Options.Routes[own.Slot];
        if (_laneWalkedTag != route.Tag) { _laneWalkedTag = route.Tag; _laneWalked = -1; }
        _laneWalked = route.Project(new(bot.X, bot.Y, bot.Z), _laneWalked, out _);
        plan.Report(bot.DatabaseID, own.Slot, _laneWalked, now);
        UseLaneTravel(new RvrLaneTravel(_rvrDestination.Id, keep.Region, route, "march"));
        RvrLaneRoute leaderRoute = plan.Options.Routes[led.Slot];
        float leaderWalked = leader == bot ? _laneWalked
            : plan.TryProgress(leader.DatabaseID, now, out int slot, out float walked) && slot == led.Slot ? walked
            : leaderRoute.Project(new(leader.X, leader.Y, leader.Z), -1, out _);
        return new(plan, own.Slot, route, _laneWalked, led.Slot, leaderRoute, leaderWalked, own.Tier);
    }

    private static RvrMarchLanePlan.ForceLane AssignMarchLane(RvrMarchLanePlan plan, string force, bool leaderForce, GameBot member,
        GameBot[] army)
    {
        if (plan.TryGetForce(force, out RvrMarchLanePlan.ForceLane known)) return known;
        int size = army.Count(other => MarchForceOf(other) == force);
        RvrMarchLanePlan.ForceLane lane = plan.Assign(force, size, leaderForce, RvrFightBoards.DefaultSkillTier(member), out bool assigned);
        if (assigned)
        {
            RvrLaneRoute route = plan.Options.Routes[lane.Slot];
            Log.Info(FormattableString.Invariant(
                $"RVR_LANE_ASSIGN force=\"{plan.TargetId}\" group=\"{force}\" lane={route.LaneIndex} slot={lane.Slot} lanes={plan.Options.Routes.Length} ") +
                FormattableString.Invariant($"length={route.MergeLength:0} trunk={route.TrunkLength:0} heads={lane.Heads} tier={lane.Tier} ") +
                $"tagAlong={(lane.TagAlong ? 1 : 0)} leader={(leaderForce ? 1 : 0)} distinct={plan.DistinctLanes} region={plan.Region} " +
                $"realm={GlobalConstants.RealmToName(plan.Realm)} from=\"{plan.Options.FromRef}\" to=\"{plan.Options.ToRef}\"");
        }
        return lane;
    }

    private static RvrLaneOptions MarchLaneOptions(GameBot leader, AbstractGameKeep keep)
    {
        RvrLiveKeep[] keeps = GameServer.KeepManager.GetKeepsOfRegion(keep.Region)
            .Select(other => new RvrLiveKeep(other.KeepID, other.Realm, new(other.X, other.Y))).ToArray();
        Region region = WorldMgr.GetRegion(keep.Region);
        eRealm realm = leader.Realm;
        return RvrLaneChoice.Options(RvrLaneCatalog.Data, RvrLaneCatalog.ProofOf, keep.Region, realm,
            new(leader.X, leader.Y, leader.Z), RvrLaneData.KeepRef(keep.KeepID), new(keep.X, keep.Y), keep.KeepID, keeps,
            AutonomousArmyDetour.HazardsFor(keep.Region, realm, keep),
            point => region?.GetZone((int)point.X, (int)point.Y) is Zone zone && AutonomousRealmBoundary.Allows(realm, keep.Region, zone.ID));
    }

    private static bool LevelOnOtherLane(MarchLaneTurn? lanes, GameBot member, long now) =>
        lanes is { } turn && turn.Plan.TryProgress(member.DatabaseID, now, out int slot, out float walked) && slot != turn.LeaderSlot &&
        RvrLanePacing.Level(RvrLanePacing.Gap(walked, turn.Plan.Options.Routes[slot], turn.LeaderWalked, turn.LeaderRoute));

    /// <summary>The gap of the flank lane furthest behind the leader (the median of its marchers; 0 when none report).</summary>
    private static float WorstLaneGap(MarchLaneTurn turn, GameBot[] army, long now)
    {
        var bySlot = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<float>>();
        foreach (GameBot member in army)
        {
            if (!turn.Plan.TryProgress(member.DatabaseID, now, out int slot, out float walked) || slot == turn.LeaderSlot) continue;
            if (!bySlot.TryGetValue(slot, out var gaps)) bySlot[slot] = gaps = new();
            gaps.Add(RvrLanePacing.Gap(walked, turn.Plan.Options.Routes[slot], turn.LeaderWalked, turn.LeaderRoute));
        }
        float worst = 0;
        foreach (var gaps in bySlot.Values) worst = MathF.Min(worst, RvrLanePacing.Median(gaps));
        return worst;
    }

    private bool TravelToDefensivePost(GameBot bot, AbstractGameKeep keep, Vector3 post)
    {
        var nav = PathfindingProvider.Instance;
        // Do not route across a whole frontier to an interior wall component.
        // Reach the connected exterior first, then use the native friendly door.
        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(keep.X, keep.Y)) > 3500 * 3500)
        {
            FollowKeepTravel(bot, _rvrDestination with { X = keep.X, Y = keep.Y, Z = keep.Z });
            return true;
        }
        Zone zone = bot.CurrentRegion.GetZone((int)post.X, (int)post.Y);
        if (bot.CurrentZone == zone && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone,
                new(bot.X, bot.Y, bot.Z), post))
            return IssuePath(bot, post);
        // Closed friendly doors split mesh components. Walk to the actual
        // doorway and use the native door operation, never a wall shortcut.
        foreach (var door in keep.Doors.Values.OrderBy(bot.GetDistanceTo))
        {
            if (door.Realm != bot.Realm || Math.Abs(door.Z - bot.Z) > 500) continue;
            if (bot.IsWithinRadius(door, WorldMgr.INTERACT_DISTANCE))
            {
                if (AutonomousRvrTravel.TraverseFriendlyDoor(bot, post)) return true;
                continue;
            }
            if (door.CurrentZone == bot.CurrentZone && AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone,
                    new(bot.X, bot.Y, bot.Z), new(door.X, door.Y, door.Z)))
                return IssuePath(bot, new(door.X, door.Y, door.Z));
        }
        // A local floor is not proof of a usable interior route. Hold outside
        // and retry instead of issuing a known-disconnected wall/rampart goal.
        _rvrApproachDestination=null;
        bot.StopMovingOnPath();bot.StopMoving();
        SetRvrStatus(bot,"Defending outside keep",keep.Name,"No usable interior corridor yet; watching for enemies outside the gate");
        return true;
    }
}
