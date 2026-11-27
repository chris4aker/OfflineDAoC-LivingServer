using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Battleground tours (the fourth bot goal). A bot only ever goes to the battleground of its own level
    /// bracket, by the realm teleporters' [Battlegrounds] choice exactly as a player would, arriving at its
    /// realm's portal keep. Inside, enemies are found by the normal frontier threat scan; between fights the
    /// bot assaults the central keep when another realm holds it (guards, gates, then the lord through the
    /// normal capture pipeline) and otherwise defends or roams around it. Leaving uses the same exit players
    /// take, back to the realm's border keep.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        // Teleporter trips into a battleground have no walking alternative.
        private const double BattlegroundNoWalkSeconds = 3600;
        private const int BattlegroundExitRadius = 400;
        private long _nextBattlegroundRoamTick;
        private Vector3? _battlegroundRoamPoint;

        private bool ExecuteBattleground(BotBrain brain, GameBot bot)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.ControllerBattleground); // AI_PROFILE: measures only
            _battlegroundRallyHold = null;
            _battlegroundStorming = false;
            _battlegroundStormLord = null;
            _battlegroundGatherStaging = null;
            BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForLevel(bot.Level);
            if (bracket == null)
            {
                // Out-levelled: the tour ends here; leaving the battleground happens on the next goal's travel.
                AutonomousObjectiveAssignments.EndBattlegroundTour(bot, $"Outgrew the battleground brackets at level {bot.Level}");
                _nextPlanTick = 0;
                return true;
            }
            if (bot.CurrentRegionID != bracket.RegionId)
            {
                // #598 C: on a group's battleground trip a member walks with its leader, who takes the party through.
                if (!BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) && TripLeaderBeside(bot) is { } tripLeader)
                    return FollowTripLeader(bot, bracket, tripLeader, "Following {0} to the battleground");
                GameLocation entry = BattlegroundBrackets.BotArrival(bracket, bot.Realm);
                return TravelToBattleground(bot, bracket, new(entry.X, entry.Y, entry.Z));
            }

            AbstractGameKeep central = GameServer.KeepManager.GetKeepsOfRegion(bracket.RegionId)
                .FirstOrDefault(keep => !keep.IsPortalKeep); // starts neutral (realm none); taken from any realm
            bool assault = central != null && central.Realm != bot.Realm;
            if (central != null)
            {
                // Rebuilt only when the target changes (no per-turn allocation).
                if (_rvrDestination?.RegionId != bracket.RegionId || _rvrDestination.Id != $"rvr-keep-{central.KeepID}")
                {
                    _rvrApproachDestination = null;
                    _patrolDestination = null;
                    _rvrDestination = new($"rvr-keep-{central.KeepID}", central.Name, bracket.Name, bracket.RegionId,
                        central.X, central.Y, central.Z, 1, false, true);
                }
            }
            else
            {
                _rvrDestination = null;
            }
            // Too few of the realm here for a wave: fight in the field instead of standing at the staging point or
            // feeding single bots to the keep's guards (Thidranki, 2026-10-17).
            bool tooFew = assault && AutonomousBattlegroundWave.TooFewForAssault(bot.Realm, bracket);
            // A battleground trip's group takes its leader's part (defend or roam) together.
            long partId = _groupDirective?.BattlegroundTrip == true && _groupDirective.Leader != null
                ? _groupDirective.Leader.DatabaseID : bot.DatabaseID;
            // #530 step 5a: the event layer decides the battleground order (the keep as its objective, the wave as its
            // muster) and the bot proposes it, as a force of its own ("bg:" ids, so a frontier follower never reads it). No
            // event here: not shared, and the TempProperties stay as they were. The muster is the wave's as it stands now
            // (AutonomousBattlegroundWave.IsGathering, a read), decided before this first proposal: a siege crew, which
            // returns below before the wave's turn, is recorded as the wave's muster while it gathers, and a gatherer's
            // first turn logs one change, not an attack and then a muster. The intent is the keep's attack either way, so
            // a siege crew works its job.
            bool mustering = assault && !tooFew && AutonomousBattlegroundWave.IsGathering(bracket.RegionId, bot.Realm, out _);
            AutonomousRvrEventLayer.BattlegroundOrder order = AutonomousRvrEventLayer.DecideBattleground(
                central?.KeepID, central != null && !assault, tooFew, mustering, partId);
            ProposeBattlegroundOrder(bot, order);
            if (assault && !order.Attacks)
                return RoamBattleground(bot, bracket, central);

            if (assault)
            {
                // A siege crew works its job (rams and engines) while the realm gathers: the only damage any gate took on
                // the owner's PC (build 3d67b6f, 07:48-07:53, targetHp 98 -> 92) was a ram's.
                if (TryRunSiegeJob(bot)) return true;
                // The realm assaults as one wave (AutonomousBattlegroundWave). Decided first, in a fight or not: a bot whose
                // combat timer still ran after a fight (InCombat lasts 10 s, and a hurt bot comes here to recover) skipped
                // the gathering and went for the nearest keep guard up to 5,000 away on its own. On the owner's PC (build
                // 3d67b6f, Thidranki 2026-11-15) 63 Midgard and Hibernian bots died to the keep's guards in 17 minutes,
                // one at a time, while no wave ever went in.
                Vector3 staging = default;
                string gathered = string.Empty;
                bool gathering = central != null &&
                    AutonomousBattlegroundWave.ShouldGather(bot, bracket, central, out staging, out gathered);
                // The wave's turn moved the muster (it went in, or it is spent and gathers again): the order follows at once.
                if (BattlegroundMusterMoved(mustering, gathering))
                    ProposeBattlegroundOrder(bot, AutonomousRvrEventLayer.DecideBattleground(central.KeepID, false, false, gathering, partId));
                if (gathering)
                {
                    _battlegroundGatherStaging = staging;
                    if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), staging) > 250 * 250)
                        IssuePath(bot, staging);
                    else
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                    }
                    SetBattlegroundStatus(bot, bracket, "Gathering for the assault", $"{central.Name}: {gathered}");
                    return true;
                }
                GameKeepGuard lord = central?.Guards.Values.FirstOrDefault(guard => guard is GuardLord && guard.IsAlive);
                bool lordHere = lord != null && lord.CurrentRegionID == bot.CurrentRegionID;
                // The lord is exposed: the realm rallies outside the breach and goes up together (AutonomousBattlegroundKeepRally).
                // Every assaulting bot of the realm takes the rally, wherever it stands: on the owner's PC (build 3b808b4,
                // Thidranki 2026-11-15 10:12-10:58) only those within 4,000 of the lord did, while 17-33 others "marched" on
                // the keep's centre on their own and fought whatever they met.
                AutonomousBattlegroundKeepRally.Order rally = lordHere && lord is GuardLord { ShieldedFromBots: false }
                    ? AutonomousBattlegroundKeepRally.Decide(bot, bracket, central, lord)
                    : default;
                _battlegroundRallyHold = rally.Hold ? rally.Point : null;
                _battlegroundStorming = rally.Step == AutonomousBattlegroundKeepRally.Step.Storm;
                _battlegroundStormLord = _battlegroundStorming ? lord : null;
                // A storming healer heals the stormers before anything else (BotBrain's battleground heal): the controller's
                // storm turn returned before the brain's own heal check, and ungrouped it healed only itself.
                if (_battlegroundStorming && AutonomousGroupSupply.IsHealer(bot) && brain.CheckHeals()) return true;
                bool nearLord = lordHere && (bot.IsWithinRadius(lord, BattlegroundLordReach) || rally.Step != AutonomousBattlegroundKeepRally.Step.None);
                // Away from the lord (a new life after a release, or chased out) the bot comes in through a gate again. The
                // old reset sat inside the near-lord branch where it could never be true, so a bot that once passed the gate
                // walked straight at the lord ever after (playtest 2026-11-04: 12 of the 14 "lord" storm samples stood
                // 1,100-2,900 from the gate, outside the walls). The rally stands outside the breach, so a storm from it goes
                // through the gate first too.
                _battlegroundGatePassed = KeepsGatePassed(_battlegroundGatePassed, nearLord, _battlegroundGateKeep == central.KeepID);
                _battlegroundGateKeep = central.KeepID;
                // A new attempt comes in through the gate again unless the bot already stands within the walls: on 4d48f42 bots
                // that reached the rally as it went up (or came back from a release) kept the last attempt's gatePassed=True and
                // took the walk to the foot straight from the rally (Crandira 06:13:58, Svaetiah 06:17:56, Ivargeildrik 06:26:38).
                if (_battlegroundStorming && rally.Attempt != _battlegroundGateAttempt)
                {
                    _battlegroundGateAttempt = rally.Attempt;
                    _battlegroundGatePassed = GatePassedAtNewAttempt(_battlegroundGatePassed,
                        AutonomousKeepGateGoals.IsWithinWalls(central, bot.X, bot.Y));
                }
                GameLiving target = FindRvrTarget(bot);
                // While the gates stand the wave marches on the gate together and fights the guards that come at it,
                // instead of each bot running at its own nearest guard (AutonomousBattlegroundWave.WaveTakesGuard).
                bool gatesShut = central.Doors.Values.Any(door => door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed);
                // ...and the guard that comes at one of them is fought by the bots around that one, together. On the owner's
                // PC (build ea882e6, Thidranki 2026-11-15 08:57-08:59) 16 Hibernians died to the keep's level-33 guards while
                // their wave of 15 went, most with one attacker on them, one at a time: each bot's own pick among the guards near it
                // (SelectDistributedRvrTarget) was not the guard hitting its neighbour, and was dropped above.
                // The same once the doors are down, while the realm rallies and storms the lord: on 3b808b4 every bot took any
                // guard in sight up to 5,000 away, alone (103 deaths "Fighting Armsman", 70 "Fighting Armswoman").
                // A stormer helps against the guards only once it stands in the lord's fight; on the way up it fights only
                // what is on it (AutonomousBattlegroundKeepRally.StormerMayEngage, build ef76db3).
                // In the lord's fight casters, archers and healers strike him and melee the guard on them, else him
                // (AutonomousBattlegroundKeepRally.StrikesLordFirst; owner's PC, build 95af6fd: on her floor everyone took the
                // guards hitting a realm-mate, and Lady Thidranki never went below 97%).
                bool inLordFight = _battlegroundStorming && AutonomousBattlegroundKeepRally.InLordFight(bot, lord);
                // The storm marches on the gate as a pack and clears the gate's guards together before it pours in (owner,
                // 2026-11-16). Build 4d48f42: every one of the 140 stormer fight samples was one bot against a GuardFighter or
                // GuardCommander at the gate (218-798 from it), and the march samples of seconds 0-49 stood 1,514-2,697 out.
                if (_battlegroundStorming && lordHere && !inLordFight && RunGateClear(brain, bot, bracket, central, lord, rally) is { } cleared)
                    return cleared;
                if (inLordFight && LordFightTarget(bot, central, lord, target) is { } pick)
                    target = pick;
                else if (AutonomousBattlegroundKeepRally.AssistsAgainstGuards(gatesShut, rally.Step, inLordFight) &&
                    WaveAssistGuard(bot, central) is { } hitting)
                    target = hitting;
                else if (target is GameKeepGuard guard && (_battlegroundStorming
                        ? !StormerMayEngage(bot, guard)
                        : !AutonomousBattlegroundWave.WaveTakesGuard(
                            AutonomousBattlegroundKeepRally.DisciplinedKeepFight(gatesShut, rally.Step), guard is GuardLord,
                            bot.GetDistanceTo(guard), FiringOnRealm(bot, guard))))
                    target = null;
                // A stormer fights from where it stands only what is on its floor: the lord above it in his tower, or a
                // scout on the stair over it, is walked toward instead (owner's PC, build c165c90: Conyff stood on the stair
                // 539 under Lady Thidranki with her as its target, attacking=False and moving=False; StrikesFromHere).
                if (_battlegroundStorming && target != null && !StrikesFromHere(bot, target))
                    target = null;
                // #617 / #600 (FarmVibes): never a mezzed or breakably rooted enemy while a free one is in the fight, and no
                // aggro pushed onto it (BotBrain.CrowdControlRespect): the bot takes the free one instead, never the gate
                // while someone is free (FarmVibes' review 2).
                GameLiving pickedBeforeControl = target; // #625: BG_TARGET_PICK notes a controlled pick passed over.
                target = BattlegroundPickPastControl(bot, target);
                bool skippedControlled = pickedBeforeControl != null && !ReferenceEquals(pickedBeforeControl, target);
                target ??= FindReachableEnemyDoor(bot, central);
                // A caster, archer or healer in the lord's fight takes its formation spot behind the melee first (1.2.2).
                if (target is GuardLord formationLord && TakeLordFormation(bot, formationLord))
                {
                    bot.TargetObject = target;
                    return true;
                }
                if (target != null)
                {
                    // #625: BG_TARGET_PICK for a new keep-assault pick (sampled; logs only).
                    if (!ReferenceEquals(bot.TargetObject, target))
                        BgTargetPickRows.Keep(bot, target, skippedControlled);
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    bot.TargetObject = target;
                    brain.AddToAggroList(target, Math.Max(100, target.EffectiveLevel * 12));
                    brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    SetBattlegroundStatus(bot, bracket, target is GameKeepDoor ? "Battering the keep gate" : "Assaulting the keep",
                        $"{central.Name} is held by {Holder(central)}", target.Name);
                    if (_battlegroundStorming && lordHere) TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "target");
                    return false;
                }
                // At the keep the assault goes for the lord. Run 17 (2026-10-17): in all four battlegrounds the gates fell and
                // guards died for four hours while every lord stayed at 100% with no bot beside him; the assault walked to the
                // keep's centre and only took targets in reach there, and the lord stands upstairs. A closed gate on the way
                // is battered above (FindReachableEnemyDoor) as soon as the bot stands at it.
                if (nearLord)
                {
                    // #416: from near enough that the hits count (GuardLord takes nothing from beyond 400); farther, walk on to him.
                    // #617: a controlled lord with a free enemy in the fight: that enemy, not the lord.
                    if (!rally.Hold && SkipsControlledPick(bot, lord, out GameLiving freeNearLord) &&
                        GameServer.ServerRules.IsAllowedToAttack(bot, freeNearLord, true))
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        bot.TargetObject = freeNearLord;
                        brain.AddToAggroList(freeNearLord, Math.Max(100, freeNearLord.EffectiveLevel * 12));
                        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                        SetBattlegroundStatus(bot, bracket, "Assaulting the keep", $"{central.Name} is held by {Holder(central)}", freeNearLord.Name);
                        return false;
                    }
                    if (!rally.Hold && !SkipsControlledPick(bot, lord) &&
                        bot.IsWithinRadius(lord, (int)AutonomousBattlegroundKeepRally.LordStrikeReach(lord,
                            RvrLordFormation.StandsBack(LordFormationRole(bot)))) &&
                        Math.Abs(bot.Z - lord.Z) <= AutonomousBattlegroundKeepRally.StrikeFloorHeight &&
                        GameServer.ServerRules.IsAllowedToAttack(bot, lord, true))
                    {
                        if (lord is GuardLord strikeLord && TakeLordFormation(bot, strikeLord))
                        {
                            bot.TargetObject = lord;
                            return true;
                        }
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        bot.TargetObject = lord;
                        brain.AddToAggroList(lord, Math.Max(100, lord.EffectiveLevel * 12));
                        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                        SetBattlegroundStatus(bot, bracket, "Assaulting the keep lord", $"{central.Name} is held by {Holder(central)}", lord.Name);
                        if (_battlegroundStorming) TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "strike");
                        return false;
                    }
                    // In through a broken main gate, as players went: run 19 (2026-10-17) showed the stormers standing at a
                    // closed postern (never batterable) because the shortest path to the lord runs through it.
                    GameKeepDoor gate = BrokenMainGate(bot, central);
                    // A main door still shut (the lord is shielded while one stands): march on the door that is the way
                    // in, in keep order, and batter it (FindReachableEnemyDoor above takes it once in reach). Pathing to the
                    // lord through the closed keep found no path: 90 stormers of Dun Abermenai gave up their route that way
                    // in run 21 (2026-10-17). The door is the outer gate first, then the keep door behind it, never simply
                    // the nearest: on the owner's PC (build 3d67b6f, Thidranki 2026-11-15) 18 of 19 storm samples marched
                    // from outside the walls on Thidranki Faste's keep door (252000302, behind the outer gate) and stood
                    // at the south wall with no path. And with the outer gate broken and the keep door shut (build
                    // ea882e6) a bot through the gate walked at the lord, whose path the shut keep door refuses
                    // (Pathfinder's closed-door hold), so it held just inside the gate, 1,700 from the door it had to hit.
                    if (StormDoor(bot, central) is { } shut)
                    {
                        // Each storm step turns the bot at once (a moving bot otherwise finished its previous walk first).
                        // The walk ends on the attackers' side of the door, not on the door's own (blocked) floor; the
                        // navmesh takes it in through a broken gate when the door stands inside the walls.
                        if (IssuePath(bot, DoorFront(bot, central, shut), retargetWhileMoving: true))
                        {
                            TraceStorm(this, bot, central, lord, shut, gate == null ? "closed-gate" : "keep-door");
                            SetBattlegroundStatus(bot, bracket, "Marching on the keep gate",
                                gate == null ? $"{central.Name}: the gates are shut" : $"{central.Name}: through the broken gate to the keep door", shut.Name);
                            return true;
                        }
                        TraceStorm(this, bot, central, lord, shut, "closed-gate-path-refused");
                    }
                    if (rally.Hold && HoldBattlegroundRally(bot, bracket, central, lord, gate, rally))
                        return true;
                    if (gate != null && !_battlegroundGatePassed)
                    {
                        if (bot.IsWithinRadius(gate, BattlegroundGateReached))
                            _battlegroundGatePassed = true;
                        else if (IssuePath(bot, new Vector3(gate.X, gate.Y, gate.Z), retargetWhileMoving: true))
                        {
                            TraceStorm(this, bot, central, lord, gate, "gate");
                            SetBattlegroundStatus(bot, bracket, "Storming the keep", $"{central.Name}: through the broken gate", lord.Name);
                            return true;
                        }
                        else TraceStorm(this, bot, central, lord, gate, "gate-path-refused");
                    }
                    // The wave gathers at the foot of his tower and goes up his stair together (owner's PC, build 95af6fd: they
                    // reached Lady Thidranki's floor one or two at a time, 30-60 s into the attempt, and died there;
                    // AutonomousBattlegroundKeepRally.ClimbGoes). The climb waits only for the stormers inside the walls, 12 s
                    // at most, and stays open while it works (4d48f42: it waited on everyone standing and never went).
                    if (_battlegroundStorming && _battlegroundGatePassed && rally.Foot is { } foot && HoldsAtFoot(bot, lord, foot, rally))
                    {
                        if (Vector2.Distance(new(bot.X, bot.Y), new(foot.X, foot.Y)) > FootHoldRadius ||
                            Math.Abs(bot.Z - foot.Z) > AutonomousBattlegroundKeepRally.FootRise)
                        {
                            if (IssuePath(bot, foot, retargetWhileMoving: true))
                            {
                                TraceStorm(this, bot, central, lord, gate, "to-foot");
                                SetBattlegroundStatus(bot, bracket, "Storming the keep", $"{central.Name}: to the foot of {lord.Name}'s tower", lord.Name);
                                return true;
                            }
                            TraceStorm(this, bot, central, lord, gate, "foot-path-refused");
                        }
                        else
                        {
                            bot.StopMovingOnPath();
                            bot.StopMoving();
                            TraceStorm(this, bot, central, lord, gate, "foot-hold");
                            SetBattlegroundStatus(bot, bracket, "Storming the keep", $"{central.Name}: at the foot of the tower, waiting for the climb", lord.Name);
                            return true;
                        }
                    }
                    // Up his tower to his floor (the navmesh point on it, so a failed walk is never settled a floor below him).
                    Vector3 lordWalk = LordWalkTarget(bot, lord, out string lordWalkKind);
                    if (_battlegroundStorming)
                    {
                        // Once per attempt each: the first walk to the lord from inside the gate, and the first stormer in his
                        // tower below his floor (c165c90: nothing said where the walk ended; the nearest stood 539 under him).
                        bool inTower = Vector2.Distance(new(bot.X, bot.Y), new(lord.X, lord.Y)) <= AutonomousBattlegroundKeepRally.TowerRadius &&
                                       lord.Z - bot.Z > AutonomousBattlegroundKeepRally.StrikeFloorHeight;
                        if (inTower && AutonomousBattlegroundKeepRally.ClaimLordPathLog(bot.CurrentRegionID, bot.Realm, tower: true))
                            LogLordPath(bot, central, lord, lordWalk, lordWalkKind, "tower");
                        else if (_battlegroundGatePassed && AutonomousBattlegroundKeepRally.ClaimLordPathLog(bot.CurrentRegionID, bot.Realm, tower: false))
                            LogLordPath(bot, central, lord, lordWalk, lordWalkKind, "lord");
                    }
                    if (IssuePath(bot, lordWalk, retargetWhileMoving: true))
                    {
                        TraceStorm(this, bot, central, lord, gate, "lord");
                        SetBattlegroundStatus(bot, bracket, "Storming the keep", $"{central.Name}: heading for {lord.Name}", lord.Name);
                        return true;
                    }
                    TraceStorm(this, bot, central, lord, gate, "lord-path-refused");
                }
                if (FollowKeepTravel(bot, _rvrDestination))
                {
                    SetBattlegroundStatus(bot, bracket, $"Marching on {central.Name}", $"{Holder(central)} holds the central keep");
                    return true;
                }
                // With every main gate shut the keep's centre has no path (209 route give-ups in Abermenai, run 21): the
                // approach is then the front of the door that is the way in (StormDoor).
                Vector3 approach = _rvrApproachDestination ?? (StormDoor(bot, central) is { } closedGate
                        ? DoorFront(bot, central, closedGate)
                        : new Vector3(central.X, central.Y, central.Z));
                if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), approach) > 120 * 120)
                    IssuePath(bot, approach);
                else
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                }
                SetBattlegroundStatus(bot, bracket, "At the keep assault approach", $"{central.Name} is held by {Holder(central)}");
                return true;
            }

            // Own (or no) central keep: a third of the realm's bots hold its walls, the rest roam the field (the event layer's
            // order: AutonomousRvrEventLayer.DecideBattleground).
            if (central != null && order.Step == AutonomousRvrEventLayer.BattlegroundStep.Hold)
            {
                if (bot.GetDistanceTo(new Point3D(central.X, central.Y, central.Z)) > 3500)
                    return TravelToDefensivePost(bot, central, new(central.X, central.Y, central.Z));
                SetBattlegroundStatus(bot, bracket, $"Defending {central.Name}", "Holding the realm's central keep");
                return HoldDefensiveKeepPost(bot, central);
            }
            return RoamBattleground(bot, bracket, central);
        }

        /// <summary>
        /// Walks to the realm's rally outside the breach and holds there (fighting only what comes at it, through the normal
        /// combat turn: BattlegroundGatherHold) until the rally goes up. False only when the rally point can't be walked to.
        /// </summary>
        private bool HoldBattlegroundRally(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central,
            GameKeepGuard lord, GameKeepDoor gate, AutonomousBattlegroundKeepRally.Order rally)
        {
            string detail = $"{central.Name}: {rally.Progress}";
            if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), rally.Point) <=
                AutonomousBattlegroundKeepRally.RallyHoldRadius * AutonomousBattlegroundKeepRally.RallyHoldRadius)
            {
                // The rally stands outside the breach: going up, the bot comes in through the gate.
                _battlegroundGatePassed = false;
                bot.StopMovingOnPath();
                bot.StopMoving();
                SetBattlegroundStatus(bot, bracket, "Rallying for the keep lord", detail, lord.Name);
                return true;
            }
            if (IssuePath(bot, rally.Point, retargetWhileMoving: true))
            {
                TraceStorm(this, bot, central, lord, gate, "rally");
                SetBattlegroundStatus(bot, bracket, "Rallying for the keep lord", detail, lord.Name);
                return true;
            }
            TraceStorm(this, bot, central, lord, gate, "rally-path-refused");
            return false;
        }

        /// <summary>
        /// The storm's march and its clear of the gate (AutonomousBattlegroundKeepRally.MarchHolds, GateClear, ClearAction): on
        /// the march a stormer ahead of its pack waits for it; at the gate (ClearZoneRadius) every stormer fights the one gate
        /// guard the storm has called, melee at it, casters and archers from range, healers standing back to heal; once the
        /// gate's guards are down (or the clear stalls) the storm goes in as before. Null when none of it applies (the storm's
        /// own steps then run); otherwise ExecuteBattleground's return value.
        /// </summary>
        private bool? RunGateClear(BotBrain brain, GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central,
            GameKeepGuard lord, AutonomousBattlegroundKeepRally.Order rally)
        {
            if (rally.Gate is not { } gatePoint || _battlegroundGatePassed) return null;
            float toGate = Vector2.Distance(new(bot.X, bot.Y), new(gatePoint.X, gatePoint.Y));
            if (!AutonomousBattlegroundKeepRally.InClearZone(toGate, _battlegroundGatePassed))
            {
                if (!AutonomousBattlegroundKeepRally.MarchHolds(toGate, rally.PackToGate, bot.InCombat || brain.HasAggro,
                        GameLoop.GameLoopTime - rally.Attempt))
                    return null;
                bot.StopMovingOnPath();
                bot.StopMoving();
                AutonomousBattlegroundKeepRally.NoteMarchHold(bot.CurrentRegionID, bot.Realm);
                TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "march-hold");
                SetBattlegroundStatus(bot, bracket, "Storming the keep", $"{central.Name}: waiting for the others on the march", lord.Name);
                return true;
            }
            if (!AutonomousBattlegroundKeepRally.GateClear(bot.CurrentRegionID, bot.Realm, central, out GameKeepGuard called) ||
                !GameServer.ServerRules.IsAllowedToAttack(bot, called, true))
                return null;
            // #617: the called guard mezzed or rooted while someone is free: the free one, here at the gate.
            if (SkipsControlledPick(bot, called, out GameLiving freeAtGate))
            {
                bot.TargetObject = freeAtGate;
                brain.AddToAggroList(freeAtGate, Math.Max(100, freeAtGate.EffectiveLevel * 12));
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "clear-free");
                SetBattlegroundStatus(bot, bracket, "Clearing the keep gate", $"{central.Name}: {called.Name} is held", freeAtGate.Name);
                return true;
            }
            bool healer = AutonomousGroupSupply.IsHealer(bot);
            bool ranged = AutonomousRvrDefense.IsRangedDefender(bot);
            switch (AutonomousBattlegroundKeepRally.ClearAction(healer, ranged, bot.GetDistanceTo(called)))
            {
                case AutonomousBattlegroundKeepRally.ClearStep.Approach:
                    if (!IssuePath(bot, new Vector3(called.X, called.Y, called.Z), retargetWhileMoving: true)) return null;
                    TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "clear-approach");
                    SetBattlegroundStatus(bot, bracket, "Clearing the keep gate", $"{central.Name}: closing on {called.Name}", called.Name);
                    return true;
                case AutonomousBattlegroundKeepRally.ClearStep.StandBack:
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "clear-heal");
                    SetBattlegroundStatus(bot, bracket, "Clearing the keep gate", $"{central.Name}: healing the storm at the gate", called.Name);
                    return true;
                default:
                    if (!ranged || bot.IsWithinRadius(called, (int)AutonomousBattlegroundKeepRally.ClearRangedReach))
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                    }
                    bot.TargetObject = called;
                    brain.AddToAggroList(called, Math.Max(100, called.EffectiveLevel * 12));
                    brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    TraceStorm(this, bot, central, lord, BrokenMainGate(bot, central), "clear");
                    SetBattlegroundStatus(bot, bracket, "Clearing the keep gate", $"{central.Name}: everyone on {called.Name}", called.Name);
                    return false;
            }
        }

        /// <summary>Roam points near an enemy keep stay this far (less the 900 random spread) from its centre.</summary>
        public const float BattlegroundRoamKeepDistance = 2_500f;

        /// <summary>The roam anchor by an enemy-held keep: outside its walls, on the side of the bot's own arrival. Pure.</summary>
        public static Vector3 BattlegroundRoamAnchorOutside(Vector3 keep, Vector3 home)
        {
            Vector2 toward = new(home.X - keep.X, home.Y - keep.Y);
            float length = toward.Length();
            Vector2 direction = length < 1 ? Vector2.UnitX : toward / length;
            float distance = length < 1 ? BattlegroundRoamKeepDistance : Math.Min(BattlegroundRoamKeepDistance, length);
            return new Vector3(keep.X + direction.X * distance, keep.Y + direction.Y * distance, keep.Z);
        }

        private bool RoamBattleground(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central)
        {
            // #598 C: a battleground trip's members roam with their leader.
            if (TripLeaderBeside(bot) is { } tripLeader && bot.IsWithinRadius(tripLeader, TripRoamWithLeaderRadius))
                return FollowTripLeader(bot, bracket, tripLeader, "Roaming the battleground with {0}");
            long now = GameLoop.GameLoopTime;
            Vector3 current = new(bot.X, bot.Y, bot.Z);
            if (!_battlegroundRoamPoint.HasValue || now >= _nextBattlegroundRoamTick ||
                Vector3.DistanceSquared(current, _battlegroundRoamPoint.Value) < 200 * 200)
            {
                // Roam between the field's landmarks: the central keep and the other realms' portal approaches.
                eRealm[] others = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
                // An enemy-held keep is roamed around, outside its walls. Playtest 2026-11-04: every
                // AUTONOMOUS_ROUTE_RECOVERY of the Thidranki fight was a roamer sent to a random point within 1,050 of
                // the shut keep's centre (courtyard and wall tops), which has no path while the gates stand.
                GameLocation home = BattlegroundBrackets.BotArrival(bracket, bot.Realm);
                Vector3 keepAnchor = central == null ? current
                    : central.Realm != bot.Realm && home != null
                        ? BattlegroundRoamAnchorOutside(new(central.X, central.Y, central.Z), new(home.X, home.Y, home.Z))
                        : new Vector3(central.X, central.Y, central.Z);
                Vector3[] anchors = others.Where(realm => realm != bot.Realm)
                    .Select(realm => BattlegroundBrackets.BotArrival(bracket, realm))
                    .Select(entry => Vector3.Lerp(new(entry.X, entry.Y, entry.Z),
                        central != null ? new(central.X, central.Y, central.Z) : new(entry.X, entry.Y, entry.Z), 0.6f))
                    .Append(keepAnchor)
                    .ToArray();
                Vector3 anchor = anchors[Random.Shared.Next(anchors.Length)];
                Zone zone = bot.CurrentRegion?.GetZone((int)anchor.X, (int)anchor.Y) ?? bot.CurrentZone;
                IPathfindingMgr nav = PathfindingProvider.Instance;
                if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone))
                {
                    // Landmark heights come from stored coordinates; settle them on the mesh first.
                    anchor = nav.GetClosestPoint(zone, anchor, 700, 700, 6000, nav.DefaultFilters) ?? anchor;
                    anchor = nav.GetRandomPoint(zone, anchor, 900, nav.DefaultFilters) ?? anchor;
                }
                _battlegroundRoamPoint = anchor;
                _nextBattlegroundRoamTick = now + 120_000 + bot.ObjectID % 30_000;
            }
            IssueVariedRvrPath(bot, _battlegroundRoamPoint.Value);
            SetBattlegroundStatus(bot, bracket, "Roaming the battleground",
                central != null ? $"{central.Name} is held by our realm" : "Hunting enemy realms on the field");
            return true;
        }

        // Stormers stop 1,600-2,500 from the lord with the gates down (runs 18-21), though the mesh reaches him from the
        // gates (navmesh probe 2026-10-17). One line every 20 s for each realm says where a stormer stands and what it walks
        // to (was one line server-wide: build ea882e6 wrote four in two waves, all Hibernian).
        // The committed stormers have their own sample every 10 s per realm (build ef76db3: all 145 of 146 samples were
        // step=rally, so nothing said what a stormer did after BG_RALLY_STORM): step=storm, with phase= the storm step
        // (gate, lord, target, strike, fight, march-under-fire...), its target and the target's kind, and the attempt.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(eRealm Realm, bool Stormer), long> NextStormTraceTick = new();
        public const long StormTraceMilliseconds = 20_000, StormerTraceMilliseconds = 10_000;
        private static void TraceStorm(AutonomousWorldBotController controller, GameBot bot, AbstractGameKeep keep, GameKeepGuard lord,
            GameKeepDoor gate, string step)
        {
            if (keep == null || lord == null) return;
            long now = GameLoop.GameLoopTime;
            bool stormer = controller.BattlegroundStormHold(bot);
            var key = (bot.Realm, stormer);
            long next = NextStormTraceTick.GetOrAdd(key, 0);
            if (now < next || !NextStormTraceTick.TryUpdate(key, now + (stormer ? StormerTraceMilliseconds : StormTraceMilliseconds), next))
                return;
            var mover = bot.movementComponent;
            long waveMs = AutonomousBattlegroundWave.GoingMilliseconds(bot.CurrentRegionID, bot.Realm, now);
            GameLiving target = bot.TargetObject as GameLiving;
            string stormerPart = !stormer ? string.Empty :
                $"phase={step} attempt={AutonomousBattlegroundKeepRally.AttemptState(bot.CurrentRegionID, bot.Realm)} " +
                $"heightToLord={(int)(bot.Z - lord.Z)} inLordFight={AutonomousBattlegroundKeepRally.InLordFight(bot, lord)} " +
                $"gatePassed={controller._battlegroundGatePassed} targetKind={StormTargetKind(target, keep, bot)} " +
                $"toTarget={(target == null ? -1 : (int)bot.GetDistanceTo(target))} " +
                $"targetHeight={(target == null ? 0 : (int)(target.Z - bot.Z))} " +
                $"targetOnMe={target?.TargetObject == bot} " +
                $"mayFight={(target == null || target.Realm == bot.Realm ? "-" : controller.StormerMayEngage(bot, target).ToString())} " +
                $"hasAggro={(bot.Brain as BotBrain)?.HasAggro} attacking={bot.IsAttacking} casting={bot.IsCasting} health={bot.HealthPercent}% " +
                // Why an order it was given does not move it (4d48f42: Rormur, Weeturnip, Oisoinnan and Ivargeildrik stood 10 s
                // and more under fire, re-ordered every 1.5 s, path=PathFound): mesmerized or stunned, or a speed of 0 (rooted).
                $"cc={(bot.IsMezzed ? "mez" : bot.IsStunned ? "stun" : "-")} speed={bot.MaxSpeed} " +
                // The clear of the gate: whether the target is the guard the storm called, the bot's role in it.
                $"calledTarget={(target is GameKeepGuard && AutonomousBattlegroundKeepRally.IsCalledGuard(bot.CurrentRegionID, bot.Realm, target.ObjectID))} " +
                $"role={(AutonomousGroupSupply.IsHealer(bot) ? "healer" : AutonomousRvrDefense.IsRangedDefender(bot) ? "ranged" : "melee")} ";
            // pending=True: the order was given this turn and the mover has not taken it yet, so moving, path and dest are
            // still those of the previous order (build ea882e6: "dest" next to the bot was the staging hold, not the route).
            Log.Info($"BG_STORM_TRACE bot={bot.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" " +
                     $"step={(stormer ? "storm" : step)} {stormerPart}" +
                     $"wave={(waveMs < 0 ? "-" : $"{waveMs / 1000}s")} pending={mover?.HasPendingMoveRequest} at={bot.X},{bot.Y},{bot.Z} " +
                     $"toLord={(int)bot.GetDistanceTo(lord)} lordAt={lord.X},{lord.Y},{lord.Z} " +
                     $"gate={(gate == null ? "none" : $"{gate.DoorId}@{gate.X},{gate.Y},{gate.Z} alive={gate.IsAlive} state={gate.State} toGate={(int)bot.GetDistanceTo(gate)}")} " +
                     $"moving={bot.IsMoving} path={mover?.LastPathfindingStatus} dest={(mover == null ? "-" : $"{mover.Destination.X:0},{mover.Destination.Y:0},{mover.Destination.Z:0}")} " +
                     $"inCombat={bot.InCombat} target={bot.TargetObject?.Name} " +
                     // Why a stormer stands between turns (run 22: 55% of the samples at the gate step stood 3,300 from it).
                     $"issued={Format(controller._issuedRouteDestination)} recovery={Format(controller._routeRecoveryWaypoint)} " +
                     $"orderIn={Math.Max(0, controller._nextMoveOrderTick - now)} stallReplans={controller._routeStallReplans}");

            static string Format(Vector3? point) => point is { } p ? $"{p.X:0},{p.Y:0},{p.Z:0}" : "-";
        }

        /// <summary>What a stormer has targeted, for BG_STORM_TRACE: lord, guard:&lt;type&gt;, door, holder- or attacker- bot,
        /// player or pet (the holder's realm or the other attacking realm), friend, monster, or "-".</summary>
        private static string StormTargetKind(GameLiving target, AbstractGameKeep keep, GameBot bot)
        {
            if (target == null) return "-";
            if (target.Realm == bot.Realm) return "friend";
            string side = target.Realm == keep.Realm ? "holder" : "attacker";
            return target switch
            {
                GuardLord => "lord",
                GameKeepGuard guard => $"guard:{guard.GetType().Name}",
                GameKeepDoor => "door",
                GamePlayer => $"{side}-player",
                GameBot => $"{side}-bot",
                GameNPC { Brain: IControlledBrain } => $"{side}-pet",
                _ when target.Realm == eRealm.None => "monster",
                _ => "other",
            };
        }

        /// <summary>Whether this stormer fights <paramref name="target"/> from where it stands (a door always), or walks on up
        /// (AutonomousBattlegroundKeepRally.StrikesFromHere).</summary>
        private static bool StrikesFromHere(GameBot bot, GameLiving target) =>
            target is GameKeepDoor ||
            AutonomousBattlegroundKeepRally.StrikesFromHere(target is GuardLord, bot.GetDistanceTo(target), Math.Abs(target.Z - bot.Z),
                target is not GuardLord towerOrKeep || AutonomousBattlegroundKeepRally.IsKeepLord(towerOrKeep),
                RvrLordFormation.StandsBack(LordFormationRole(bot)));

        // Each lord's walk target, worked out at most every 30 s or when he moves (a navmesh query per stormer turn otherwise).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (Vector3 Lord, Vector3 Walk, string Kind, long Until)> LordWalks = new();
        public const long LordWalkMilliseconds = 30_000;
        /// <summary>The flat half-extents searched, in order, for a navmesh floor at the lord's own height.</summary>
        private static readonly float[] LordFloorSearch = [96f, 256f];

        /// <summary>
        /// Where a stormer walks to reach the lord: the navmesh point on his floor (within 96, then 256, of him and
        /// AutonomousBattlegroundKeepRally.StrikeFloorHeight of his height, kind "lordFloor"), or his own position (kind
        /// "lord", or "lord-offmesh" with no floor there: then the mover's goal snap may settle the walk on a floor below
        /// him, which BG_LORD_PATH shows). Without a navmesh his position.
        /// </summary>
        private static Vector3 LordWalkTarget(GameBot bot, GameKeepGuard lord, out string kind)
        {
            Vector3 at = new(lord.X, lord.Y, lord.Z);
            long now = GameLoop.GameLoopTime;
            if (LordWalks.TryGetValue(lord.ObjectID, out var cached) && now < cached.Until &&
                Vector3.DistanceSquared(cached.Lord, at) <= 16 * 16)
            {
                kind = cached.Kind;
                return cached.Walk;
            }
            Zone zone = bot.CurrentRegion?.GetZone(lord.X, lord.Y);
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Vector3 walk = at;
            kind = "lord";
            if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone))
            {
                if (AutonomousGoalSnap.IsOnMesh(nav, zone, at))
                    kind = "lord";
                else
                {
                    kind = "lord-offmesh";
                    // The goal snap's own boxes reach 512-1,024 down (AutonomousGoalSnap.WideBoxes): only a floor at his
                    // height is taken here, first right beside him, then across his room.
                    foreach (float horizontal in LordFloorSearch)
                        if (nav.GetClosestPoint(zone, at, horizontal, horizontal, AutonomousBattlegroundKeepRally.StrikeFloorHeight, nav.DefaultFilters) is { } floor &&
                            Math.Abs(floor.Z - at.Z) <= AutonomousBattlegroundKeepRally.StrikeFloorHeight)
                        {
                            walk = floor;
                            kind = "lordFloor";
                            break;
                        }
                }
            }
            LordWalks[lord.ObjectID] = (at, walk, kind, now + LordWalkMilliseconds);
            return walk;
        }

        /// <summary>
        /// BG_LORD_PATH: the walk a stormer is given to the lord, queried as the mover queries it, and where it ends against
        /// his height: status, nodes, the last node and its height under him, the highest node, whether his own position is
        /// on the navmesh and the nearest floor to it (the widest box the goal snap uses), and the bot's own goal snap, mover
        /// destination and state. Names the blocker on the owner's next run: a walk that ends under him (lastZMinusLord well
        /// below 0 with status PathFound: his floor is not on the mesh), one cut short (PartialPathFound), or a complete walk
        /// the stormers do not finish (look at the tower samples' moving= and phase=).
        /// </summary>
        private void LogLordPath(GameBot bot, AbstractGameKeep keep, GameKeepGuard lord, Vector3 walk, string kind, string sample)
        {
            Zone zone = bot.CurrentRegion?.GetZone(lord.X, lord.Y);
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Vector3 lordAt = new(lord.X, lord.Y, lord.Z), start = new(bot.X, bot.Y, bot.Z);
            bool meshed = zone != null && nav.IsAvailable && nav.HasNavmesh(zone);
            string query = !meshed ? "query=no-navmesh" : "query=other-zone";
            if (meshed && bot.CurrentZone == zone)
            {
                var nodes = new WrappedPathfindingNode[512];
                PathfindingResult result = nav.GetPathStraight(zone, start, walk, nav.DefaultFilters, nodes);
                int count = Math.Min(result.NodeCount, nodes.Length);
                Vector3? last = count > 0 ? nodes[count - 1].Position : null;
                float highest = count > 0 ? nodes.Take(count).Max(node => node.Position.Z) : float.NaN;
                Vector3? near = nav.GetClosestPoint(zone, lordAt, AutonomousGoalSnap.WideBoxes[^1].Horizontal, AutonomousGoalSnap.WideBoxes[^1].Horizontal,
                    AutonomousGoalSnap.WideBoxes[^1].Vertical, nav.DefaultFilters);
                query = $"status={result.Status} nodes={result.NodeCount} " +
                        $"last={(last is { } l ? $"{l.X:0},{l.Y:0},{l.Z:0}" : "-")} " +
                        $"lastZMinusLord={(last is { } m ? $"{m.Z - lord.Z:0}" : "-")} " +
                        $"lastToLord={(last is { } n ? $"{Vector3.Distance(n, lordAt):0}" : "-")} " +
                        $"highestZ={(float.IsNaN(highest) ? "-" : $"{highest:0}")} " +
                        $"lordOnMesh={AutonomousGoalSnap.IsOnMesh(nav, zone, lordAt)} " +
                        $"floorNearLord={(near is { } f ? $"{f.X:0},{f.Y:0},{f.Z:0}" : "-")}";
            }
            var mover = bot.movementComponent;
            Log.Info($"BG_LORD_PATH bot={bot.Name} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{keep.Name}\" sample={sample} " +
                     $"at={bot.X},{bot.Y},{bot.Z} lordAt={lord.X},{lord.Y},{lord.Z} heightToLord={(int)(bot.Z - lord.Z)} " +
                     $"walkTo={walk.X:0},{walk.Y:0},{walk.Z:0} walkKind={kind} {query} " +
                     $"goalSnap={(_goalSnapActive ? $"{_goalSnapTo.X:0},{_goalSnapTo.Y:0},{_goalSnapTo.Z:0}" : "-")} " +
                     $"moving={bot.IsMoving} path={mover?.LastPathfindingStatus} " +
                     $"dest={(mover == null ? "-" : $"{mover.Destination.X:0},{mover.Destination.Y:0},{mover.Destination.Z:0}")} " +
                     $"inCombat={bot.InCombat} target={bot.TargetObject?.Name} attempt={AutonomousBattlegroundKeepRally.AttemptState(bot.CurrentRegionID, bot.Realm)}");
        }

        /// <summary>The keep's nearest broken (or open) main door, never a postern; null with none.</summary>
        private static GameKeepDoor BrokenMainGate(GameBot bot, AbstractGameKeep keep) =>
            keep?.Doors.Values
                .Where(door => door.IsAttackableDoor && !door.IsPostern && (!door.IsAlive || door.State == eDoorState.Open))
                .OrderBy(door => bot.GetDistanceTo(door)).FirstOrDefault();

        public const int BattlegroundLordReach = 4_000;
        public const int BattlegroundGateReached = 250;
        private int _battlegroundGateKeep;
        private bool _battlegroundGatePassed;
        // Set while this bot gathers for its realm's wave (AutonomousBattlegroundWave): it fights only what comes at it.
        private Vector3? _battlegroundGatherStaging;

        /// <summary>
        /// Proposes the event layer's battleground order as the bot's intent (#530 step 5a): one writer, the event layer,
        /// for the battleground's force record, as for every frontier intent. A battleground party (a trip, or a muster party
        /// from step 5b) is one force, <c>bg:&lt;its id&gt;</c>, written by its leader; a solo bot is its own.
        /// </summary>
        private void ProposeBattlegroundOrder(GameBot bot, AutonomousRvrEventLayer.BattlegroundOrder order)
        {
            bool party = _groupDirective?.BattlegroundTrip == true && !string.IsNullOrEmpty(_groupDirective.GroupId);
            ProposeRvrIntent(bot, order.Proposal, mirrorIntent: false, mirrorKeep: false,
                forceId: "bg:" + (party ? _groupDirective.GroupId : RvrForceId(bot)),
                writer: party ? _groupDirective.Leader == null || _groupDirective.Leader == bot : null);
        }

        /// <summary>
        /// Whether the wave's own turn (ShouldGather) moved the muster from what this turn's first order said: the wave went
        /// in (muster to attacker) or is spent and gathers again. Then the order is proposed again at once (#530 step 5a).
        /// </summary>
        public static bool BattlegroundMusterMoved(bool orderedMuster, bool waveGathers) => orderedMuster != waveGathers;

        /// <summary>What holds this battleground attacker back from a fight now, and where its muster or rally stands.</summary>
        private AutonomousRvrEventLayer.MusterHold BattlegroundMusterHold(GameBot bot, out Vector3 point)
        {
            if (BattlegroundGatherHold(bot, out point, out bool atRally))
                return atRally ? AutonomousRvrEventLayer.MusterHold.Rally : AutonomousRvrEventLayer.MusterHold.Gather;
            point = default;
            return BattlegroundStormHold(bot) ? AutonomousRvrEventLayer.MusterHold.Storm : AutonomousRvrEventLayer.MusterHold.None;
        }

        /// <summary>
        /// The one gather-hold rule (AutonomousRvrEventLayer.MusterAllowsFight) for this bot and target, used by the threat
        /// scan and the combat drop (answering a fight: a lord that comes at the bot is fought back) and by the keep's guard
        /// pick (<paramref name="picking"/>: a new target, never the lord). A rally's posted contact is looked up only when
        /// the target does not come at the muster itself.
        /// </summary>
        private bool MusterAllowsFight(GameBot bot, AutonomousRvrEventLayer.MusterHold hold, Vector3 point, GameLiving target,
            bool picking = false)
        {
            if (hold == AutonomousRvrEventLayer.MusterHold.None)
                return true;
            bool isLord = target is GuardLord;
            if (hold == AutonomousRvrEventLayer.MusterHold.Storm)
                return AutonomousRvrEventLayer.MusterAllowsFight(hold, isLord, false, false, StormerMayEngage(bot, target), picking);
            if (isLord && picking)
                return false;
            bool comes = GathererMayEngage(bot, target, point);
            bool posted = !comes &&
                AutonomousBattlegroundRallyContact.Has(bot.CurrentRegionID, bot.Realm, point, target, GameLoop.GameLoopTime);
            return AutonomousRvrEventLayer.MusterAllowsFight(hold, isLord, comes, posted, false, picking);
        }

        /// <summary>This bot is gathering for its realm's battleground wave, at <paramref name="staging"/>.</summary>
        private bool BattlegroundGatherHold(GameBot bot, out Vector3 staging) => BattlegroundGatherHold(bot, out staging, out _);

        /// <summary>
        /// This bot is gathering for its realm's battleground wave, or holding at its realm's keep rally
        /// (<paramref name="atRally"/>), at <paramref name="staging"/>: either way it fights only what comes at it.
        /// </summary>
        private bool BattlegroundGatherHold(GameBot bot, out Vector3 staging, out bool atRally)
        {
            staging = default;
            atRally = false;
            if (!BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) ||
                !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground))
                return false;
            if (_battlegroundGatherStaging.HasValue && AutonomousBattlegroundWave.IsGathering(bot.CurrentRegionID, bot.Realm, out staging))
                return true;
            atRally = _battlegroundRallyHold.HasValue &&
                      AutonomousBattlegroundKeepRally.IsRallying(bot.CurrentRegionID, bot.Realm, bot.DatabaseID, out staging);
            return atRally;
        }

        /// <summary>This bot is one of its realm's stormers going up to the keep lord (AutonomousBattlegroundKeepRally).</summary>
        private bool BattlegroundStormHold(GameBot bot) =>
            _battlegroundStorming && BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) &&
            AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground) &&
            AutonomousBattlegroundKeepRally.IsStorming(bot.CurrentRegionID, bot.Realm, bot.DatabaseID);

        /// <summary>Whether a stormer takes a fight with this enemy (AutonomousBattlegroundKeepRally.StormerMayEngage): the lord,
        /// anything in the lord's fight once it stands beside him, and on the way only what is on it, near and on its floor.</summary>
        private bool StormerMayEngage(GameBot bot, GameLiving target)
        {
            GameKeepGuard lord = _battlegroundStormLord;
            return AutonomousBattlegroundKeepRally.StormerMayEngage(target is GuardLord,
                lord != null && lord.IsAlive && AutonomousBattlegroundKeepRally.InLordFight(bot, lord),
                target.TargetObject == bot, // it has this bot targeted (a caster's IsCasting drops between casts)
                bot.GetDistanceTo(target), Math.Abs(target.Z - bot.Z),
                IsOtherAttacker(bot, target, lord?.Realm ?? eRealm.None),
                // The gate guard the storm called is fought by everyone (AutonomousBattlegroundKeepRally.GateClear).
                target is GameKeepGuard && AutonomousBattlegroundKeepRally.IsCalledGuard(bot.CurrentRegionID, bot.Realm, target.ObjectID));
        }

        /// <summary>An enemy of neither this bot's realm nor the keep holder's (the other attacking realm's bot, player or pet);
        /// never a keep guard or door. Pure.</summary>
        public static bool IsOtherAttacker(eRealm mine, eRealm target, eRealm holder, bool keepPiece) =>
            !keepPiece && target != mine && target != holder && target != eRealm.None;

        private static bool IsOtherAttacker(GameBot bot, GameLiving target, eRealm holder) =>
            IsOtherAttacker(bot.Realm, target.Realm, holder, target is GameKeepGuard or GameKeepDoor);

        /// <summary>
        /// A stormer in its fight turn with nothing it may fight (only its ten-second combat timer, or the hits of a wall
        /// caster or a passing enemy it leaves alone) goes on up instead of standing where it was hit: its aggro on enemies
        /// it may not fight is dropped and the storm's own step runs (build ef76db3: 106 of the 110 attacking-realm deaths
        /// during the attempts were "Fighting ..."). A bot casting finishes its cast.
        /// </summary>
        private bool StormerMarchesOn(BotBrain brain, GameBot bot)
        {
            if (!BattlegroundStormHold(bot) || bot.IsCasting) return false;
            // In the lord's fight the stormer's pick holds in its combat turns too (the brain otherwise takes its highest aggro:
            // the guard or holder that hit it first).
            if (_battlegroundStormLord is { IsAlive: true } stormLord && AutonomousBattlegroundKeepRally.InLordFight(bot, stormLord) &&
                LordFightTarget(bot, StormKeep(), stormLord, bot.TargetObject as GameLiving) is { } pick &&
                // #416: the lord only from near enough that the hits count; farther, the bot walks on to him (not a cast from 900).
                !ReferenceEquals(bot.TargetObject, pick) && StrikesFromHere(bot, pick) && !SkipsControlledPick(bot, pick))
            {
                bot.TargetObject = pick;
                brain.AddToAggroList(pick, Math.Max(100, pick.EffectiveLevel * 12));
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                TraceStorm(this, bot, StormKeep(), stormLord, BrokenMainGate(bot, StormKeep()), "lord-fight-pick");
                return false;
            }
            if (bot.TargetObject is GameLiving target && target.IsAlive && target.Realm != bot.Realm &&
                (target is GameKeepDoor || StormerMayEngage(bot, target) && StrikesFromHere(bot, target)))
            {
                TraceStorm(this, bot, StormKeep(), _battlegroundStormLord, BrokenMainGate(bot, StormKeep()), "fight");
                return false;
            }
            // An enemy off its floor (the lord above it in his tower) stays out of this fight until the bot is up beside it.
            int dropped = brain.RemoveFromAggroListWhere(enemy => enemy.Realm != bot.Realm && enemy is not GameKeepDoor &&
                (!StormerMayEngage(bot, enemy) || !StrikesFromHere(bot, enemy)));
            if (brain.HasAggro) return false; // something it may fight is on its list: the fight goes on with that
            if (bot.TargetObject is GameLiving held && held.Realm != bot.Realm)
            {
                bot.StopAttack();
                bot.TargetObject = null;
            }
            if (brain.FSM.GetCurrentState()?.StateType == eFSMStateType.AGGRO)
                brain.FSM.SetCurrentState(eFSMStateType.IDLE);
            if (dropped > 0) NoteBattlegroundHeldBack(bot, rally: true, dropped: true);
            TraceStorm(this, bot, StormKeep(), _battlegroundStormLord, BrokenMainGate(bot, StormKeep()), "march-under-fire");
            return true;
        }

        /// <summary>
        /// #617 / #600: a battleground controller pick past crowd control. A mezzed or breakably rooted pick with a free enemy
        /// in the fight becomes that free enemy (AutonomousBattlegroundTargets.ReplaceControlledPick), so the bot neither
        /// breaks the mez nor walks off to the gate; otherwise the pick stands. Public for the fight-harness test.
        /// </summary>
        public static GameLiving BattlegroundPickPastControl(GameBot bot, GameLiving pick)
        {
            bool controlled = SkipsControlledPick(bot, pick, out GameLiving free);
            return AutonomousBattlegroundTargets.ReplaceControlledPick(pick, controlled, free);
        }

        /// <summary>The keep this stormer storms (its lord's).</summary>
        private AbstractGameKeep StormKeep() => _battlegroundStormLord?.Component?.Keep;

        /// <summary>Counts a fight a gatherer, rally holder or stormer left alone (dropped: broke off).</summary>
        private static void NoteBattlegroundHeldBack(GameBot bot, bool rally, bool dropped)
        {
            if (rally) AutonomousBattlegroundKeepRally.NoteHeldBack(bot.CurrentRegionID, bot.Realm, dropped);
            else AutonomousBattlegroundWave.NoteHeldBack(bot.CurrentRegionID, bot.Realm, dropped);
        }

        /// <summary>Whether a gathering bot takes a fight with this target (AutonomousBattlegroundWave.GathererMayEngage).</summary>
        private static bool GathererMayEngage(GameBot bot, GameLiving target, Vector3 staging) =>
            AutonomousBattlegroundWave.GathererMayEngage(FiringOnRealm(bot, target),
                Vector3.Distance(new(target.X, target.Y, target.Z), staging), bot.GetDistanceTo(target));

        /// <summary>
        /// A gatherer (or rally holder) in a fight drops a target that is no longer coming at it and has left the staging point
        /// (a fleeing enemy, or one it was pulled toward), and walks back instead of chasing it under the keep's walls. A
        /// stormer drops an enemy it may not fight (StormerMayEngage: not the lord, not on it) and goes on up. A friend it
        /// targets (a heal) is never dropped here.
        /// </summary>
        private bool DropBattlegroundGatherChase(BotBrain brain, GameBot bot)
        {
            if (bot.TargetObject is not GameLiving target || target is GameKeepDoor || !target.IsAlive || target.Realm == bot.Realm)
                return false;
            // The one gather-hold rule (#530 step 5a). #617: a contact the rally posted is the whole rally's fight, wherever
            // it was first seen. A stormer counts with the rally.
            AutonomousRvrEventLayer.MusterHold hold = BattlegroundMusterHold(bot, out Vector3 staging);
            if (MusterAllowsFight(bot, hold, staging, target)) return false;
            bool atRally = hold != AutonomousRvrEventLayer.MusterHold.Gather;
            brain.RemoveFromAggroList(target);
            bot.StopAttack();
            bot.TargetObject = null;
            if (bot.FollowTarget == target)
            {
                bot.StopFollowing();
                bot.StopMovingOnPath();
                bot.StopMoving();
            }
            if (!brain.HasAggro && brain.FSM.GetCurrentState()?.StateType == eFSMStateType.AGGRO)
                brain.FSM.SetCurrentState(eFSMStateType.IDLE);
            NoteBattlegroundHeldBack(bot, atRally, dropped: true);
            return true;
        }

        // Set while this bot holds at its realm's rally (AutonomousBattlegroundKeepRally): it fights what comes to the rally
        // (GathererMayEngage), not the lord and not guards elsewhere on the walls.
        private Vector3? _battlegroundRallyHold;
        // Set while this bot is one of its realm's stormers going up to the lord, and that lord.
        private bool _battlegroundStorming;
        private GameKeepGuard _battlegroundStormLord;
        // The attempt (its start) in which this stormer went up the lord's stair: it climbs on, whatever the foot's climb.
        private long _battlegroundClimbAttempt;
        public const int BattlegroundLordStrike = 900;
        /// <summary>A stormer this near the foot of the lord's tower (flat) stops there and waits for the climb.</summary>
        public const float FootHoldRadius = 150f;

        /// <summary>
        /// Whether this stormer waits at the foot of the lord's tower: not once it went up in this attempt, and not while the
        /// climb is open (it goes up now) or when it already stands up the tower (resurrected, or up before the foot was set).
        /// </summary>
        private bool HoldsAtFoot(GameBot bot, GameKeepGuard lord, Vector3 foot, AutonomousBattlegroundKeepRally.Order rally)
        {
            if (_battlegroundClimbAttempt == rally.Attempt) return false;
            bool upAlready = bot.Z > foot.Z + AutonomousBattlegroundKeepRally.FootRise &&
                             Vector2.Distance(new(bot.X, bot.Y), new(lord.X, lord.Y)) <= AutonomousBattlegroundKeepRally.InTheFightRadius;
            if (!rally.ClimbOpen && !upAlready) return true;
            _battlegroundClimbAttempt = rally.Attempt;
            return false;
        }

        /// <summary>
        /// Whom a stormer in the lord's fight strikes (AutonomousBattlegroundKeepRally.StrikesLordFirst): the lord, within the
        /// strike radius on his floor, for casters, archers and healers; for a melee bot the guard or defender on it, else the
        /// lord. Null when the lord can't be struck from here (the old rule then stands: the guard hitting a realm-mate).
        /// </summary>
        private static GameLiving LordFightTarget(GameBot bot, AbstractGameKeep keep, GameKeepGuard lord, GameLiving candidate)
        {
            if (lord == null || !lord.IsAlive || !bot.IsWithinRadius(lord, BattlegroundLordStrike) ||
                Math.Abs(bot.Z - lord.Z) > AutonomousBattlegroundKeepRally.StrikeFloorHeight ||
                !GameServer.ServerRules.IsAllowedToAttack(bot, lord, true))
                return null;
            bool rangedOrHealer = AutonomousRvrDefense.IsRangedDefender(bot) || AutonomousGroupSupply.IsHealer(bot);
            GameLiving onMe = rangedOrHealer ? null : EnemyOnMe(bot, keep, candidate);
            return AutonomousBattlegroundKeepRally.StrikesLordFirst(rangedOrHealer, onMe != null) ? lord : onMe;
        }

        /// <summary>The nearest enemy on this bot, close and on its floor (StormerMayEngage's hit-back reach): a keep guard
        /// other than the lord attacking it, or <paramref name="candidate"/> when that one has it targeted.</summary>
        private static GameLiving EnemyOnMe(GameBot bot, AbstractGameKeep keep, GameLiving candidate)
        {
            bool OnMe(GameLiving enemy) => enemy != null && enemy is not GuardLord && enemy.IsAlive && enemy.Realm != bot.Realm &&
                enemy.TargetObject == bot && bot.IsWithinRadius(enemy, (int)AutonomousBattlegroundKeepRally.StormerHitBackRadius) &&
                Math.Abs(enemy.Z - bot.Z) <= AutonomousBattlegroundKeepRally.StormerHitBackHeight &&
                GameServer.ServerRules.IsAllowedToAttack(bot, enemy, true);
            GameLiving best = OnMe(candidate) ? candidate : null;
            if (keep != null)
                foreach (GameKeepGuard guard in keep.Guards.Values)
                    if (OnMe(guard) && (guard.IsAttacking || guard.IsCasting) &&
                        (best == null || bot.GetDistanceTo(guard) < bot.GetDistanceTo(best)))
                        best = guard;
            return best;
        }

        /// <summary>A stormer keeps "through the gate" only while it stays by the same keep's lord. Pure.</summary>
        public static bool KeepsGatePassed(bool passed, bool nearLord, bool sameKeep) => passed && nearLord && sameKeep;

        // The attempt (its start) this stormer's gatePassed belongs to.
        private long _battlegroundGateAttempt;

        /// <summary>At the start of an attempt a stormer keeps "through the gate" only when it stands within the keep's walls. Pure.</summary>
        public static bool GatePassedAtNewAttempt(bool passed, bool withinWalls) => passed && withinWalls;

        private static string Holder(AbstractGameKeep keep) =>
            keep.Realm == eRealm.None ? "its neutral guards" : GlobalConstants.RealmToName(keep.Realm);

        /// <summary>
        /// The main door a storm marches on while one stands closed: the way in, in keep order (StormDoorIndex), or null.
        /// </summary>
        private static GameKeepDoor StormDoor(GameBot bot, AbstractGameKeep keep)
        {
            if (keep == null) return null;
            GameKeepDoor[] doors = keep.Doors.Values.ToArray();
            var facts = new (int Number, float FromCentre, float FromBot, bool PosternLike, bool ClosedAndStanding)[doors.Length];
            for (int i = 0; i < doors.Length; i++)
            {
                GameKeepDoor door = doors[i];
                facts[i] = (SiegeDoorNumber(door), Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y)), bot.GetDistanceTo(door),
                    PosternLike(keep, door), door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed);
            }
            int index = StormDoorIndex(facts);
            return index < 0 ? null : doors[index];
        }

        /// <summary>
        /// Which closed main door a storm goes for: posterns never (they are not battered and lead nowhere a wave goes),
        /// then keep order as the siege crews use it (SiegeDoor: the outer gate, door 01, before the keep door behind it),
        /// then the door farther from the keep's centre, then the nearer to the bot. -1 when no main door stands closed.
        /// Thidranki Faste: gate 252000301 (1,244 from the centre, east) before keep door 252000302 (435, by the lord). Pure.
        /// </summary>
        public static int StormDoorIndex(
            System.Collections.Generic.IReadOnlyList<(int Number, float FromCentre, float FromBot, bool PosternLike, bool ClosedAndStanding)> doors)
        {
            int best = -1;
            for (int i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                if (!door.ClosedAndStanding || door.PosternLike) continue;
                if (best < 0) { best = i; continue; }
                var held = doors[best];
                if (door.Number != held.Number ? door.Number < held.Number
                    : door.FromCentre != held.FromCentre ? door.FromCentre > held.FromCentre
                    : door.FromBot < held.FromBot)
                    best = i;
            }
            return best;
        }

        /// <summary>How far in front of a door a storm's walk ends: inside FindReachableEnemyDoor's 450, off the door's floor.</summary>
        public const float DoorFrontDistance = 230f;

        /// <summary>
        /// The spot in front of a door on the attackers' side (SiegePlacement.OutwardNormal: away from the keep's centre for
        /// the outer gate, toward the outer gate for the keep door behind it). Pure.
        /// </summary>
        public static Vector3 DoorFrontPoint(Vector3 door, ushort heading, Vector2 keepCentre, Vector2? approach)
        {
            Vector2 outward = SiegePlacement.OutwardNormal(new(door.X, door.Y), heading, keepCentre, approach);
            return new(door.X + outward.X * DoorFrontDistance, door.Y + outward.Y * DoorFrontDistance, door.Z);
        }

        /// <summary><see cref="DoorFrontPoint"/> settled on the navmesh (the door's own position when there is no mesh).</summary>
        private static Vector3 DoorFront(GameBot bot, AbstractGameKeep keep, GameKeepDoor door)
        {
            // A door behind the outer gate (nearer the centre than door 01) faces the gate; any other door faces out.
            Vector2 centre = new(keep.X, keep.Y);
            Vector2? approach = InnerDoorApproach(keep, door) is { } gate &&
                Vector2.Distance(gate, centre) > Vector2.Distance(new(door.X, door.Y), centre) ? gate : null;
            Vector3 front = DoorFrontPoint(new(door.X, door.Y, door.Z), door.Heading, centre, approach);
            Zone zone = bot.CurrentRegion?.GetZone((int)front.X, (int)front.Y);
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return new(door.X, door.Y, door.Z);
            return nav.GetClosestPoint(zone, front, 120, 120, 256, nav.DefaultFilters) ?? front;
        }

        /// <summary>
        /// The keep guard (never the lord) that is hitting one of the bot's realm near it, for a going wave to fight together:
        /// the one whose victim is nearest the bot (AutonomousBattlegroundWave.WaveAssists). Null when none.
        /// </summary>
        private static GameKeepGuard WaveAssistGuard(GameBot bot, AbstractGameKeep keep)
        {
            if (keep == null) return null;
            bool ranged = AutonomousRvrDefense.IsRangedDefender(bot);
            GameKeepGuard best = null;
            float bestVictim = float.MaxValue;
            // The guard it already fights stays its target while it still qualifies (no switching between two guards).
            if (bot.TargetObject is GameKeepGuard current && current.Component?.Keep == keep && Assists(current, out _))
                return current;
            foreach (GameKeepGuard guard in keep.Guards.Values)
            {
                if (!Assists(guard, out float victimToBot) || victimToBot >= bestVictim) continue;
                best = guard;
                bestVictim = victimToBot;
            }
            return best;

            bool Assists(GameKeepGuard guard, out float victimToBot)
            {
                victimToBot = float.MaxValue;
                if (guard is GuardLord || !guard.IsAlive || guard.CurrentRegionID != bot.CurrentRegionID ||
                    !(guard.IsAttacking || guard.IsCasting) || guard.TargetObject is not GameLiving victim ||
                    victim.Realm != bot.Realm || !victim.IsAlive || victim.CurrentRegionID != bot.CurrentRegionID)
                    return false;
                victimToBot = bot.GetDistanceTo(victim);
                return AutonomousBattlegroundWave.WaveAssists(ranged, victimToBot, guard.GetDistanceTo(victim)) &&
                       GameServer.ServerRules.IsAllowedToAttack(bot, guard, true);
            }
        }

        /// <summary>A closed gate of the enemy-held keep that this bot can stand at and hit, like players did.</summary>
        private static GameKeepDoor FindReachableEnemyDoor(GameBot bot, AbstractGameKeep keep)
        {
            if (keep == null || keep.Realm == bot.Realm) return null;
            return keep.Doors.Values
                .Where(door => door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed &&
                               bot.IsWithinRadius(door, 450) && GameServer.ServerRules.IsAllowedToAttack(bot, door, true))
                .OrderBy(bot.GetDistanceTo)
                .FirstOrDefault();
        }

        /// <summary>A trip member farther than this from its leader in the battleground roams on its own until they meet again.</summary>
        public const int TripRoamWithLeaderRadius = 6_000;

        /// <summary>
        /// #598 C: the bot's battleground-trip leader when the bot is a member (not the leader) and the leader is alive in the
        /// same region; otherwise null (the bot then travels or fights on its own, as any battleground bot).
        /// </summary>
        private GameBot TripLeaderBeside(GameBot bot) =>
            _groupDirective?.BattlegroundTrip == true && _groupDirective.Leader is { IsAlive: true } leader && leader != bot &&
            leader.Group == bot.Group && leader.CurrentRegionID == bot.CurrentRegionID
                ? leader : null;

        /// <summary>Walks in the trip leader's formation, as a party member follows its leader.</summary>
        private bool FollowTripLeader(GameBot bot, BattlegroundBrackets.Bracket bracket, GameBot leader, string activity)
        {
            Vector3 center = leader.IsMoving ? AutonomousGroupMotion.PredictLeader(leader) : new(leader.X, leader.Y, leader.Z);
            Vector3 formation = AutonomousBotGroupCoordinator.FormationPoint(bot, center, false);
            if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), formation) < FormationFollowReach * FormationFollowReach)
            {
                AutonomousGroupPace.CatchUp(bot, Vector3.Distance(new(bot.X, bot.Y, bot.Z), formation), GameLoop.GameLoopTime);
                AutonomousGroupFormationFollow.Ensure(bot, leader, center, formation, false);
            }
            else
                IssuePath(bot, formation, retargetWhileMoving: true);
            SetBattlegroundStatus(bot, bracket, string.Format(activity, leader.Name), $"The group goes as one, led by {leader.Name}");
            return true;
        }

        /// <summary>Teleporter trip into the bracket's battleground (walking to a realm teleporter first when needed).</summary>
        private bool TravelToBattleground(GameBot bot, BattlegroundBrackets.Bracket bracket, Vector3 goal)
        {
            if (BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID))
                return LeaveBattleground(bot);
            if (!BattlegroundBrackets.Allows(bracket, bot.Level)) return false;
            if (TryTownTeleport(bot, bracket.RegionId, goal, BattlegroundNoWalkSeconds)) return true;

            GameNPC porter = NearestRealmTeleporter(bot);
            if (porter == null)
            {
                AutonomousObjectiveAssignments.EndBattlegroundTour(bot, "No realm teleporter could be reached for the battlegrounds");
                return true;
            }
            SetStatus(bot, $"Heading to {porter.Name}", $"Battleground: {bracket.Name}",
                $"Walking to the teleporter for {bracket.Name}", porter.Name, porter.CurrentZone?.Description ?? string.Empty);
            if (bot.CurrentRegionID == porter.CurrentRegionID)
                return IssuePath(bot, AutonomousTownTeleporters.StandOff(porter, bot));
            CampDestination previous = _camp;
            _camp = new("battleground-teleporter", porter.Name, porter.CurrentZone?.Description ?? string.Empty,
                porter.CurrentRegionID, porter.X, porter.Y, porter.Z, 1, false, false);
            try { return TravelAcrossRegions(bot); }
            finally { _camp = previous; }
        }

        /// <summary>The bot's own-realm teleporter: one in this region first, otherwise one in the realm's capital.</summary>
        private static GameNPC NearestRealmTeleporter(GameBot bot)
        {
            GameNPC local = AutonomousTownTeleporters.Porters(bot.CurrentRegion)
                .Where(porter => porter.Realm == bot.Realm)
                .OrderBy(porter => Vector2.DistanceSquared(new(bot.X, bot.Y), new(porter.X, porter.Y)))
                .FirstOrDefault();
            if (local != null) return local;
            ushort capital = bot.Realm switch { eRealm.Albion => 10, eRealm.Midgard => 101, eRealm.Hibernia => 201, _ => (ushort)0 };
            return capital == 0 ? null : AutonomousTownTeleporters.Porters(WorldMgr.GetRegion(capital))
                .FirstOrDefault(porter => porter.Realm == bot.Realm);
        }

        /// <summary>
        /// The players' way out: walk back to the realm's portal-keep arrival point and take the exit to the
        /// realm's border keep (the same destinations KeepManager.ExitBattleground uses).
        /// </summary>
        private bool LeaveBattleground(GameBot bot)
        {
            BattlegroundBrackets.Bracket here = BattlegroundBrackets.ForRegion(bot.CurrentRegionID);
            DbTeleport exit = BattlegroundExit(bot.Realm);
            if (here == null || exit == null) return false;
            GameLocation entry = BattlegroundBrackets.BotArrival(here, bot.Realm);
            // Walk to the mesh floor at the realm's arrival point (the exit portal is there).
            Vector3 exitSpot = AutonomousTownTeleporters.Arrival(new DbTeleport { RegionID = here.RegionId, X = entry.X, Y = entry.Y, Z = entry.Z }, 0);
            if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(exitSpot.X, exitSpot.Y)) > BattlegroundExitRadius * BattlegroundExitRadius)
            {
                IssuePath(bot, exitSpot);
                SetStatus(bot, "Leaving the battleground", GoalText(), $"Walking to the {here.Name} exit portal");
                return true;
            }
            if (bot.InCombat) return false;
            Vector3 arrival = AutonomousTownTeleporters.Arrival(exit, 0);
            bot.StopMovingOnPath();
            bot.StopMoving();
            if (!bot.MoveTo((ushort)exit.RegionID, (int)arrival.X, (int)arrival.Y, (int)arrival.Z, (ushort)exit.Heading))
                return false;
            Log.Info($"AUTONOMOUS_BATTLEGROUND_EXIT bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                     $"battleground={here.Name} destination=\"{exit.TeleportID}\"");
            bot.ForcePathReplot();
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Movement);
            AutonomousBotStatusPersistence.Queue(bot, true);
            ResetRouteOrderState();
            _nextPlanTick = 0;
            return true;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<eRealm, DbTeleport> BattlegroundExits = new();

        private static DbTeleport BattlegroundExit(eRealm realm)
        {
            string location = realm switch
            {
                eRealm.Albion => "Castle Sauvage",
                eRealm.Midgard => "Svasud Faste",
                eRealm.Hibernia => "Druim Ligen",
                _ => null,
            };
            if (location == null) return null;
            return BattlegroundExits.GetOrAdd(realm, _ =>
                DOLDB<DbTeleport>.SelectObject(DB.Column("TeleportID").IsEqualTo(location)));
        }

        private void SetBattlegroundStatus(GameBot bot, BattlegroundBrackets.Bracket bracket, string activity, string detail, string target = "")
        {
            if (bot.PersistentRecord != null)
                bot.PersistentRecord.ObjectivePhase = activity;
            SetStatus(bot, activity, BattlegroundGoal(bracket), detail, target, bracket.Name);
        }

        public static string BattlegroundGoal(BattlegroundBrackets.Bracket bracket) =>
            $"Battleground: {bracket.Name} (levels {bracket.MinLevel}-{bracket.MaxLevel})";

        /// <summary>
        /// The goal a fight is reported under. A battleground bot's fights kept the PvE "Find a reachable level-appropriate
        /// XP camp" (GoalText with no camp): 647 of the 654 Thidranki deaths of 2026-11-04 were logged under it.
        /// </summary>
        private string CombatGoalText(GameBot bot) =>
            _groupDirective?.IsDynamic != true && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground) &&
            BattlegroundBrackets.ForLevel(bot.Level) is { } bracket
                ? BattlegroundGoal(bracket)
                : GoalText();
    }
}
