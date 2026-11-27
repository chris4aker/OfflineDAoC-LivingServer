using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private readonly AutonomousFrontierThreatPolicy _frontierThreat = new();

    // PvP stalemate (run 15: Hibernian bots in Darkness Falls stood "fighting" one Albion raider for 15
    // minutes, 117 watchdog relocations): an enemy-realm target that has not lost health for 90 s while
    // this bot has not moved is dropped and ignored for 5 minutes, like a player giving up on a target
    // they cannot reach or hurt.
    private const long PvpStalemateMilliseconds = 90_000;
    private const long PvpIgnoreMilliseconds = 5 * 60_000;
    private GameLiving _pvpWatch;
    private long _pvpWatchSince;
    private int _pvpWatchHealth;
    private Vector3 _pvpWatchPosition;
    private readonly Dictionary<GameLiving, long> _ignoredPvpTargets = new();

    private bool BreakPvpStalemate(BotBrain brain, GameBot bot)
    {
        if (bot.TargetObject is not GameLiving target || target is not (GamePlayer or GameBot) || !target.IsAlive ||
            target.Realm == bot.Realm || target.Realm == eRealm.None)
        {
            _pvpWatch = null;
            return false;
        }
        long now = GameLoop.GameLoopTime;
        Vector3 position = new(bot.X, bot.Y, bot.Z);
        if (_pvpWatch != target || target.Health < _pvpWatchHealth ||
            Vector3.DistanceSquared(position, _pvpWatchPosition) > 150f * 150f)
        {
            _pvpWatch = target;
            _pvpWatchSince = now;
            _pvpWatchHealth = target.Health;
            _pvpWatchPosition = position;
            return false;
        }
        if (now - _pvpWatchSince < PvpStalemateMilliseconds) return false;
        if (_ignoredPvpTargets.Count > 32)
            foreach (GameLiving expired in _ignoredPvpTargets.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                _ignoredPvpTargets.Remove(expired);
        _ignoredPvpTargets[target] = now + PvpIgnoreMilliseconds;
        brain.RemoveFromAggroList(target);
        bot.StopAttack();
        bot.TargetObject = null;
        _pvpWatch = null;
        RvrDecide.Event(bot, "scan", "flee", "-", "flee.stalemate", $"target={target.Name}");
        Log.Info($"AUTONOMOUS_PVP_STALEMATE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{target.Name}\" " +
                 $"targetRealm={GlobalConstants.RealmToName(target.Realm)} region={bot.CurrentRegionID} position={bot.X},{bot.Y},{bot.Z} seconds={(now - _pvpWatchSince) / 1000}");
        return true;
    }

    private bool IsIgnoredPvpTarget(GameLiving target) =>
        _ignoredPvpTargets.TryGetValue(target, out long until) && until > GameLoop.GameLoopTime;

    /// <summary>A target this bot dropped as unreachable or stalemated; its hits do not pull the bot back in.</summary>
    // A fleeing relic carrier takes no new aggro (relic audit 2026-10-31, L4: hits used to pull it back into the fight).
    internal bool IgnoresTarget(GameLiving target) => target != null && (_relicCarrierFleeing || IsIgnoredPvpTarget(target));

    /// <summary>
    /// Aggro this bot does not take (BotBrain.ForceAddToAggroList): a target it ignores (<see cref="IgnoresTarget"/>), and
    /// for a battleground stormer on its way up an enemy it may not fight (AutonomousBattlegroundKeepRally.StormerMayEngage):
    /// a hit from a wall wizard or a passing enemy's arrow no longer turns the storm round (owner's PC, build ef76db3).
    /// </summary>
    internal bool IgnoresAggro(GameLiving target, GameBot bot) => IgnoresTarget(target) ||
        target != null && bot != null && target.Realm != bot.Realm && target is not DOL.GS.Keeps.GameKeepDoor &&
        BattlegroundStormHold(bot) && !StormerMayEngage(bot, target);

    // Owner 2026-10-07: a melee bot shot at from a keep wall (an archer guard, or an enemy bot up on the wall) must
    // not run around below it for ever. A target with no walkable route (closed enemy doors block the way) is dropped
    // for two minutes after two failed checks four seconds apart, and left to the ranged bots, who shoot back at
    // anything firing on their realm (TryEngageFrontierThreat).
    private const long UnreachableCheckMilliseconds = 4_000;
    private const long UnreachableIgnoreMilliseconds = 2 * 60_000;
    private GameLiving _reachWatch;
    private int _reachFailures;
    private long _nextReachCheck;

    private bool BreakUnreachableTarget(BotBrain brain, GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        if (now < _nextReachCheck) return false;
        _nextReachCheck = now + UnreachableCheckMilliseconds;
        if (AutonomousRvrDefense.IsRangedDefender(bot) || bot.TargetObject is not GameLiving target || !target.IsAlive ||
            target.Realm == bot.Realm || target is GameSiegeWeapon or GameKeepDoor || bot.IsWithinRadius(target, 250) ||
            // Only a target up on a wall or cliff (run 20 also dropped level-ground PvP targets 2-72 units apart).
            Math.Abs(target.Z - bot.Z) < 150)
        {
            _reachWatch = null;
            return false;
        }
        var nav = AutonomousKeepApproachNavigation.ForRealm(PathfindingProvider.Instance, bot.CurrentRegion, bot.Realm);
        if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return false;
        Vector3? floor = nav.GetClosestPoint(bot.CurrentZone, new(target.X, target.Y, target.Z), 64, 64, 96, nav.DefaultFilters);
        bool reachable = floor.HasValue && Math.Abs(floor.Value.Z - target.Z) <= 96 &&
            AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, new(bot.X, bot.Y, bot.Z), floor.Value);
        if (reachable || _reachWatch != target)
        {
            _reachWatch = reachable ? null : target;
            _reachFailures = reachable ? 0 : 1;
            return false;
        }
        if (++_reachFailures < 2) return false;
        _ignoredPvpTargets[target] = now + UnreachableIgnoreMilliseconds;
        brain.RemoveFromAggroList(target);
        bot.StopAttack();
        bot.StopMovingOnPath();
        bot.TargetObject = null;
        _reachWatch = null;
        RvrDecide.Event(bot, "scan", "flee", "-", "flee.unreachable", $"target={target.Name}");
        Log.Info($"AUTONOMOUS_UNREACHABLE_TARGET bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{target.Name}\" " +
                 $"targetRealm={GlobalConstants.RealmToName(target.Realm)} heightAbove={target.Z - bot.Z} region={bot.CurrentRegionID} position={bot.X},{bot.Y},{bot.Z}");
        return true;
    }

    // #617: until when this battleground bot counts an enemy person as in sight (set by the scan).
    private long _battlegroundEnemySeenUntil;

    /// <summary>#617: this bot saw an enemy person in a battleground within the last few seconds (BotBrain holds its
    /// maintenance and self-buff pulse then).</summary>
    public bool BattlegroundEnemyInSight => GameLoop.GameLoopTime < _battlegroundEnemySeenUntil;

    /// <summary>
    /// #617 / #600: an enemy the bots leave alone while a free one is in the fight: mesmerized, rooted by a root damage
    /// breaks (Value >= 99; BotBrain.IsBreakablyControlled), or held for a realm-mate's crowd control
    /// (BotPvpCrowdControl.Protected).
    /// </summary>
    public static bool HeldByCrowdControl(GameBot bot, GameLiving target) =>
        target is not (GameSiegeWeapon or GameKeepDoor) &&
        (BotBrain.IsBreakablyControlled(target) || bot != null && BotPvpCrowdControl.Protected(bot, target));

    /// <summary>
    /// #617 / #600: whether a controller pick in a battleground passes over <paramref name="target"/>: it is crowd-controlled
    /// and an uncontrolled enemy (player, gamebot, pet or keep guard) is in the fight near this bot.
    /// </summary>
    private static bool SkipsControlledPick(GameBot bot, GameLiving target) => SkipsControlledPick(bot, target, out _);

    /// <summary>As above, with the nearest free enemy that made the pick pass over (null when it does not).</summary>
    private static bool SkipsControlledPick(GameBot bot, GameLiving target, out GameLiving freeEnemy)
    {
        freeEnemy = null;
        if (target == null || !BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) || !HeldByCrowdControl(bot, target))
            return false;
        freeEnemy = NearestFreeEnemy(bot, target);
        return AutonomousBattlegroundTargets.SkipsControlledPick(true, freeEnemy != null);
    }

    /// <summary>The nearest enemy in the fight (within FreeEnemyRadius) that is not crowd-controlled and may be attacked.</summary>
    public static GameLiving NearestFreeEnemy(GameBot bot, GameLiving except)
    {
        bool Free(GameLiving enemy) => enemy != except && enemy.IsAlive && !enemy.IsStealthed &&
            bot.IsWithinRadius(enemy, AutonomousBattlegroundTargets.FreeEnemyRadius) &&
            !BotBrain.IsBreakablyControlled(enemy) && BotSiegeRuntime.LegalEnemy(bot, enemy);
        return bot.GetPlayersInRadius(AutonomousBattlegroundTargets.FreeEnemyRadius).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius(AutonomousBattlegroundTargets.FreeEnemyRadius)
                .Where(npc => BotPvpCrowdControl.PlayerLike(npc) || npc is GameKeepGuard))
            .Where(Free).OrderBy(bot.GetDistanceTo).FirstOrDefault();
    }

    /// <summary>
    /// #617: the rally's contacts. Posts each enemy person in <paramref name="seen"/> that heads for the rally or fights a
    /// realm-mate near it (AutonomousBattlegroundRallyContact.IsContact), then returns every live contact of this rally
    /// that <paramref name="usable"/> accepts (eligible and in reach), the ones rally-mates posted included.
    /// </summary>
    private static GameLiving[] RallyContacts(GameBot bot, Vector3 rally, GameLiving[] seen, Func<GameLiving, bool> usable, long now)
    {
        foreach (GameLiving target in seen)
        {
            if (!BotPvpCrowdControl.PlayerLike(target))
                continue;
            float toRally = Vector3.Distance(new(target.X, target.Y, target.Z), rally);
            bool approaching = AutonomousBattlegroundRallyContact.Approaching(target.IsMoving, target.Heading,
                target.GetHeading((int)rally.X, (int)rally.Y));
            bool fighting = (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
                victim.Realm == bot.Realm;
            float victimToRally = fighting && target.TargetObject is GameLiving hit
                ? Vector3.Distance(new(hit.X, hit.Y, hit.Z), rally) : float.MaxValue;
            if (!AutonomousBattlegroundRallyContact.IsContact(true, toRally, approaching, fighting, victimToRally) ||
                !AutonomousBattlegroundRallyContact.Post(bot.CurrentRegionID, bot.Realm, rally, target, now))
                continue;
            // #625: BG_RALLY_RESPONSE times each rally-mate's first engage from here (logs only).
            BgRallyResponseRows.Post(bot.CurrentRegionID, bot.Realm, target, now);
            if (Log.IsInfoEnabled)
                Log.Info($"BG_RALLY_CONTACT bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                         $"target=\"{target.Name}\" toRally={(int)toRally} approaching={approaching} fight={fighting} region={bot.CurrentRegionID}");
        }
        return AutonomousBattlegroundRallyContact.Contacts(bot.CurrentRegionID, bot.Realm, rally, now).Where(usable).ToArray();
    }

    /// <summary>How close an enemy must be to pull a siege operator off its engine.</summary>
    public const int SiegeOperatorEngageRadius = 450;

    /// <summary>
    /// #525: whether a bot holding a siege job may take this enemy as a fight target. A bot with no siege job may take
    /// any. An operator takes only a person within 450 (never an enemy engine: its own engine answers those); a
    /// defending operator also answers an enemy attacking the engine it works. Before, a defending operator dropped its
    /// engine for any enemy it saw (defense.md finding 4).
    /// </summary>
    public static bool SiegeOperatorMayEngage(bool assigned, bool defending, bool withinRadius, bool isEngine, bool attacksOperatedEngine) =>
        !assigned || !isEngine && (withinRadius || defending && attacksOperatedEngine);

    private static bool AttacksOperatedEngine(GameBot bot, GameLiving target) =>
        target.IsAttacking && target.TargetObject is GameSiegeWeapon engine && engine.Owner == bot;

    /// <summary>An enemy attacking this bot or a friend near it: it has a line of fire, so it can be fired back at
    /// (wall archers stand where the navmesh ray cannot reach).</summary>
    private static bool FiringOnRealm(GameBot bot, GameLiving target) =>
        (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
        victim.Realm == bot.Realm && (victim == bot || bot.IsWithinRadius(victim, 1000));

    // The frontier, plus the neutral raid dungeons (Darkness Falls, Summoner's Hall and the
    // dungeons leading to it), where realms meet and fight on sight.
    internal static bool IsBattleground(GameLiving living) =>
        IsInFrontier(living) || living != null && (RealmRaidNeutralEvents.IsBattleRegion(living.CurrentRegionID) ||
            BattlegroundBrackets.IsBattlegroundRegion(living.CurrentRegionID));

    // Called before optional upkeep and the world controller's rendezvous,
    // recovery and follow returns. Assignment/level do not restrict defense.
    // It starts real combat without replacing the bot's durable task or event.
    public bool TryEngageFrontierThreat(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        // L4: a relic carrier does not go looking for fights; Tick decides whether it is cornered.
        if (GameRelic.IsPlayerCarryingRelic(bot)) return false;
        if (bot?.IsAutonomousWorldBot == true && IsBattleground(bot) && (BreakPvpStalemate(brain, bot) || BreakUnreachableTarget(brain, bot))) return false;
        bool defending = AutonomousRvrDefense.IsCommittedDefender(bot);
        bool siegeFighter = AutonomousRvrDefense.IsCommittedSiegeFighter(bot);
        GameLiving previousEngine = defending ? bot.TargetObject as GameSiegeWeapon ?? _siegeWeapon?.TargetObject as GameSiegeWeapon : null;
        // #617 (FarmVibes' review 1): a battleground attacker already hitting an enemy engine scans on, as a defender on an
        // engine does, so an enemy person walking up takes it off the engine (the brain's own pick is only highest aggro).
        bool battleground = bot != null && BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID);
        GameSiegeWeapon engineInHand = AutonomousBattlegroundTargets.EngineAttackerScans(battleground, defending,
            bot?.TargetObject is GameSiegeWeapon onEngine && onEngine.Realm != bot.Realm && onEngine.IsAlive)
            ? (GameSiegeWeapon)bot.TargetObject : null;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
            !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active || bot.IsReturningAfterRelease ||
            bot.IsOnStableMasterRoute || !IsBattleground(bot) ||
            (brain.HasAggro || bot.InCombat || bot.IsAttacking) && previousEngine == null && engineInHand == null ||
            brain.FSM.GetCurrentState()?.StateType == eFSMStateType.PASSIVE ||
            !_frontierThreat.Due(GameLoop.GameLoopTime, bot.DatabaseID) || IsSafeArea(bot)) return false;
        // AI_PROFILE: only the scan itself is timed, not the cheap checks above that skip it.
        using var aiProfile = AiProfile.Scope(AiSystem.FrontierThreatScan);

        // 1.2 work package 1 ("Eyes"): where the bot is (at its objective only near it), which field rules apply, and
        // whether this bot looks this turn (two lookers per group; the others are woken by a call or a fight).
        long scanNow = GameLoop.GameLoopTime;
        RvrScanContext rvr = RvrContext(brain, bot, defending || siegeFighter);
        RvrScanRole scanRole = RvrScanTurn(brain, bot, rvr, scanNow);
        RvrScanBudget.Add(bot.Realm, scanRole);
        LogRvrScanBudget(scanNow);
        // #617 (FarmVibes' review 4): a bot holding at a rally whose board holds a contact scans even when it is not one of
        // its group's lookers this turn, so the whole rally engages within one scan of the post.
        if (scanRole == RvrScanRole.Idle && battleground && !defending && !siegeFighter &&
            BattlegroundGatherHold(bot, out Vector3 heldRally, out _) &&
            AutonomousBattlegroundRallyContact.Contacts(bot.CurrentRegionID, bot.Realm, heldRally, scanNow).Count > 0)
            scanRole = RvrScanRole.Woken;
        if (!RvrScanRoster.Scans(scanRole)) return false;

        var nav = PathfindingProvider.Instance;
        if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return false;
        RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Scans);
        // Away from its objective a bot looks only to where noticing stops (never less than 2,600), not 5,000.
        int searchRadius = AutonomousRvrPerception.ScanRadius(rvr.Profile, rvr.Rules.FieldRadius, TargetSearchRadius);
        // A stealthed enemy is a candidate only close up, for the stealth rule (RvrStealthNotice); never otherwise.
        // #522: at an objective too (RvrScanContext.StealthNotice), now that gamebots no longer uncover stealthers like monsters.
        bool StealthOk(GameLiving target) => !target.IsStealthed ||
            rvr.StealthNotice && bot.IsWithinRadius(target, RvrStealthNotice.MaxRange);
        // Cheap rejections first (most nearby livings are friendly bots); the area lookup last
        // (IsSafeArea/GetAreasOfZone were ~64 MB per 40 s of allocations in run 11).
        bool Eligible(GameLiving target) => target != bot && StealthOk(target) && !IsIgnoredPvpTarget(target) &&
            target.ObjectState == GameObject.eObjectState.Active && target.IsAlive &&
            target.Realm != bot.Realm && target.Realm != eRealm.None && target.CurrentRegionID == bot.CurrentRegionID &&
            AutonomousRvrTargetPolicy.IsEligible(bot.Realm, target.Realm, target.IsAlive,
                target.CurrentRegionID == bot.CurrentRegionID, IsBattleground(target), IsSafeArea(target),
                GameServer.ServerRules.IsAllowedToAttack(bot, target, true));

        // #617: battleground scans only (field RvR keeps its picks): people before engines, an attacking crew answers an
        // enemy person within fight range, and crowd-controlled enemies wait while a free one is in the fight.
        bool crew = BotSiegeRuntime.Assigned(bot) || _siegeJobKeep != null;
        GameLiving[] nearby = bot.GetPlayersInRadius((ushort)searchRadius).Where(Eligible).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius((ushort)searchRadius)
                .Where(npc => (npc is GameBot or GameSiegeWeapon ||
                    npc is GameKeepGuard && AutonomousRvrDefense.IsRangedDefender(bot) && FiringOnRealm(bot, npc) ||
                    (siegeFighter ? AutonomousRvrDefense.IsCombatant(npc) :
                        npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is IGamePlayer)) && Eligible(npc)))
            .Where(target => bot.GetDistanceTo(target) <= searchRadius)
            // #525: a siege operator, attacking or defending, keeps its engine unless the enemy is within 450 or is
            // attacking its engine (defense.md finding 4: defenders dropped their engines at the first sighting).
            .Where(target => SiegeOperatorMayEngage(BotSiegeRuntime.Assigned(bot), defending, bot.IsWithinRadius(target, SiegeOperatorEngageRadius),
                target is GameSiegeWeapon, AttacksOperatedEngine(bot, target)) ||
                AutonomousBattlegroundTargets.SiegeCrewReleases(battleground, crew, defending,
                    BotPvpCrowdControl.PlayerLike(target), bot.GetDistanceTo(target)))
            .OrderBy(target => target.TargetObject is GameBot friendly && friendly.Realm == bot.Realm &&
                bot.IsWithinRadius(friendly, 1000) && BotSiegeRuntime.HoldingPosition(friendly) ? 0 : 1)
            // #617: in a battleground the engines come after the people (the visibility budget looks at people first).
            .ThenBy(target => AutonomousBattlegroundTargets.Rank(battleground, target is GameSiegeWeapon))
            .ThenBy(bot.GetDistanceTo).ToArray();
        bool Ray(bool seen)
        {
            RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Rays);
            return seen;
        }
        bool Visible(GameLiving target) => FiringOnRealm(bot, target) && !target.IsStealthed ||
            !RvrSkipsLineOfSight(bot, target, rvr, scanNow) && Ray(nav.HasLineOfSight(bot.CurrentZone,
            new(bot.X, bot.Y, bot.Z + 48), new(target.X, target.Y, target.Z + 48), nav.BlockingDoorAvoidanceFilters));
        var visible = siegeFighter
            ? _frontierThreat.VisiblePriority(nearby.Where(AutonomousRvrDefense.IsCombatant).ToArray(),
                nearby.OfType<GameSiegeWeapon>().Cast<GameLiving>().ToArray(), Visible)
            : _frontierThreat.Visible(nearby, target => FiringOnRealm(bot, target) && !target.IsStealthed ||
                // RvR perception: no ray for a target this bot couldn't notice on this scan anyway (observer cap, range).
                !RvrSkipsLineOfSight(bot, target, rvr, scanNow) && Ray(nav.HasLineOfSight(bot.CurrentZone,
                new(bot.X, bot.Y, bot.Z), new(target.X, target.Y, target.Z), nav.DefaultFilters)));
        var visibleTargets=visible.ToArray();
        // RvR perception (owner 2026-11-01; enemy gamebots and stealth since 1.2 work package 1): perceived targets only
        // once noticed and after a reaction delay. At its objective (near the keep it holds or besieges) as before.
        visibleTargets = PerceiveRvrTargets(bot, visibleTargets, rvr);
        // #529 step 2 ("Also, now"): the /faction alarm and the news board hear what this bot knows (its fresh contacts:
        // noticed, told or hit), not everyone within the scan radius before the ray and the notice (structure.md finding 1,
        // "a fourth leak"). Nobody calls out a stealther.
        AutonomousLivingServer.ObserveEnemies(bot, RvrAlarmHears(bot, scanNow));
        // A bot gathering for its realm's battleground wave fights only what comes at it (owner's PC, build 3d67b6f: the
        // gatherers saw the keep's Albion defenders and other realms within 5,000 and were pulled into the guards).
        // The same for a bot holding at its realm's keep rally once the doors are down, and a stormer going up to the lord
        // fights only what is upon it or firing on the realm near it (owner's PC, build 3b808b4, Thidranki 10:12-10:58: with
        // the waves bypassed nothing held the realm back, and 33-57 of the battleground's bots were "Fighting" at every sample).
        // #617: the rally reacts as a group. A bot holding at the rally posts what it sees heading for the rally (or fighting
        // a realm-mate near it), and takes every contact a rally-mate posted, even one it has not seen itself.
        Vector3 rallyPoint = default;
        bool rallyHold = !defending && !siegeFighter && battleground && BattlegroundGatherHold(bot, out rallyPoint, out _);
        GameLiving[] rallyContacts = rallyHold
            ? RallyContacts(bot, rallyPoint, visibleTargets, target => Eligible(target) && bot.GetDistanceTo(target) <= searchRadius, scanNow)
            : [];
        if (rallyContacts.Length > 0 && visibleTargets.Length == 0)
            visibleTargets = rallyContacts;
        if (!defending && !siegeFighter && visibleTargets.Length > 0)
        {
            // The one gather-hold rule (#530 step 5a, AutonomousRvrEventLayer.MusterAllowsFight), as in the combat drop and
            // the keep's guard pick.
            AutonomousRvrEventLayer.MusterHold hold = BattlegroundMusterHold(bot, out Vector3 gatherStaging);
            bool atRally = hold != AutonomousRvrEventLayer.MusterHold.Gather;
            if (hold != AutonomousRvrEventLayer.MusterHold.None)
            {
                GameLiving[] allowed = visibleTargets.Where(target => MusterAllowsFight(bot, hold, gatherStaging, target))
                    .Concat(rallyContacts).Distinct().ToArray();
                if (allowed.Length == 0)
                {
                    NoteBattlegroundHeldBack(bot, atRally, dropped: false);
                    return false;
                }
                visibleTargets = allowed;
            }
        }
        // #617 (FarmVibes' review 3): only an enemy person this bot may engage (after the gather and storm filters) holds its
        // maintenance and self-buff pulse; a gatherer with the keep's distant defenders in sight still buffs.
        if (battleground && visibleTargets.Any(BotPvpCrowdControl.PlayerLike))
            _battlegroundEnemySeenUntil = scanNow + AutonomousBattlegroundTargets.EnemyInSightMilliseconds;
        // #662: a solo or duo bot sizes the fight up on sight and opens or backs off (a committed defender or a siege crew
        // keeps its job; RvrSightSizeUp).
        if (!defending && !siegeFighter && !crew)
            visibleTargets = SightSizeUpGate(brain, bot, visibleTargets, scanNow);
        visibleTargets = RvrTacticsGateThreats(brain, bot, RvrStanceGateThreats(brain, bot, visibleTargets));
        GameLiving[] beforeArrange = visibleTargets; // #625: BG_TARGET_PICK counts what the arrangement passed over.
        // #617 / #600 (FarmVibes): in a battleground, people before engines, then mezzed or breakably rooted enemies passed
        // over while a free one is in the fight. Outside a battleground the array is the same one.
        visibleTargets = AutonomousBattlegroundTargets.Arrange(battleground, visibleTargets, target => target is GameSiegeWeapon,
            target => HeldByCrowdControl(bot, target));
        var operatorThreats=visibleTargets.Where(target=>target.IsAttacking && target.TargetObject is GameBot friendly &&
            friendly.Realm==bot.Realm && bot.IsWithinRadius(friendly,1000) && BotSiegeRuntime.HoldingPosition(friendly)).ToArray();
        GameLiving enemy = SelectDistributedRvrTarget(bot, operatorThreats.Length>0 ? operatorThreats : visibleTargets);
        if (enemy == null || !Eligible(enemy)) return false;
        if (previousEngine != null && enemy is GameSiegeWeapon) return false;
        // #617: on an engine already, the bot stays on it unless the pick is a person; then it leaves the engine for him.
        if (engineInHand != null)
        {
            if (enemy is GameSiegeWeapon) return false;
            bot.StopAttack();
            brain.RemoveFromAggroList(engineInHand);
            RvrDecide.Event(bot, "scan", "switch", RvrRecordText.Who(enemy), "scan.engine-to-person", $"engine={engineInHand.Name}");
        }
        // #617: an attacking crew leaves its engine for an enemy person within fight range, and takes no job for 10 s.
        if (AutonomousBattlegroundTargets.SiegeCrewReleases(battleground, crew, defending, BotPvpCrowdControl.PlayerLike(enemy),
                bot.GetDistanceTo(enemy)))
        {
            ReleaseSiegeJob(bot);
            _siegeNextAttempt = scanNow + AutonomousBattlegroundTargets.CrewRetakeMilliseconds;
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_SIEGE action=crew_released bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                         $"target=\"{enemy.Name}\" distance={bot.GetDistanceTo(enemy)} region={bot.CurrentRegionID}");
        }

        if (defending)
        {
            // Combat wins over buying siege supplies. #525: an operator (only an enemy within 450 or on its engine gets
            // here, SiegeOperatorMayEngage) keeps its job and engine as attackers do, and goes back to it after the
            // fight; a bot with no engine yet releases its job without deleting anything, and reclamation stays open.
            if ((BotSiegeRuntime.Assigned(bot) || _siegeJobKeep != null) && !BotSiegeRuntime.HoldingPosition(bot)) ReleaseSiegeJob(bot);
            _siegeNextAttempt = GameLoop.GameLoopTime + 10_000;
            if (previousEngine != null)
            {
                bot.StopAttack();
                brain.RemoveFromAggroList(previousEngine);
            }
            SetRvrStatus(bot, "Engaging keep attackers", _rvrDestination?.MonsterName ?? "Keep defense", enemy.Name);
        }

        bot.StopMovingOnPath();
        bot.StopMoving();
        bot.WakeRecoveryRest();
        bot.TargetObject = enemy;
        brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
        if (!brain.HasAggro) return false;
        AutonomousDefensivePull.OnThreat(bot, enemy);
        AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        // #505: which list the target came from, the fight and the decision row (logging only).
        string list = operatorThreats.Length > 0 ? "operator" : defending ? "defend" : siegeFighter ? "siege" :
            Array.IndexOf(rallyContacts, enemy) >= 0 ? "rally" :
            FiringOnRealm(bot, enemy) ? "firing" : rvr.AtObjective ? "objective" : "noticed";
        long fight = RvrFights.Touch(bot, enemy, "engage");
        RvrRecordRows.Engage(bot, enemy, list, visibleTargets.Length, fight);
        RvrDecide.Event(bot, "scan", "engage", RvrRecordText.Who(enemy), "scan." + list, $"visible={visibleTargets.Length}",
            enemy is GamePlayer engagedPlayer ? engagedPlayer.Name : null);
        // #625: BG_TARGET_PICK (sampled) and BG_RALLY_RESPONSE (a rallied bot's first engage of a posted contact); logs only.
        if (battleground)
        {
            bool fromRally = Array.IndexOf(rallyContacts, enemy) >= 0;
            BgTargetPickRows.Scan(bot, enemy, beforeArrange, visibleTargets, fromRally, list);
            if (fromRally)
                BgRallyResponseRows.Engaged(bot.CurrentRegionID, bot.Realm, bot.DatabaseID, enemy, scanNow);
        }
        return true;
    }
}
