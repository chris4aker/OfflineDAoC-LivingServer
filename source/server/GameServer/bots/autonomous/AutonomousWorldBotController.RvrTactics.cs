using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the world controller's hook points. Each is called by
// one line at its site and is a stub here: it asks the brain's gate (BotBrain.RvrTacticsActive: the master switch
// rvr_bot_field_tactics, a battle region and RvrTacticsScope) and its own switch, and then has no opinion, so the code
// at the site runs exactly as before. Work packages 1 and 4 fill the threat gate, 4 the travel hold, 5 and 6 the roam
// destination. A hook that does real work wraps it in AiProfile.Scope(AiSystem.RvrTactics).
public sealed partial class AutonomousWorldBotController
{
    /// <summary>
    /// Hook: TryEngageFrontierThreat, between perception (and the battleground gather and storm filters) and the target
    /// pick. Returns the targets this bot may pick from now: the same array for no opinion, fewer or none when the
    /// group's plan holds the bot back (it is not a looker; the group is watching or sizing up). A bot under attack
    /// never comes here: the scan runs only for a bot that is not fighting.
    /// Reserved for work packages 1 (rvr_bot_group_lookers) and 4 (rvr_bot_size_up). Work package 6: a soloer, duo or
    /// stealther opens only on what the odds allow (<see cref="RvrSoloGate"/>).
    /// </summary>
    private GameLiving[] RvrTacticsGateThreats(BotBrain brain, GameBot bot, GameLiving[] visible)
    {
        if (visible.Length == 0 || brain?.RvrTacticsActive() != true)
            return visible;
        visible = RvrSoloGate(bot, visible);
        if (!(Properties.RVR_BOT_GROUP_LOOKERS || Properties.RVR_BOT_SIZE_UP))
            return visible;
        return visible;
    }

    /// <summary>
    /// Hook: the RvR turn, before the group's hold for a new pull (HoldBeforeNewGroupPull). True when the tactics own
    /// this turn (the group holds for stragglers, or regroups at its rally point after a retreat or a wipe); false for
    /// no opinion. Work package 4 (rvr_bot_size_up, rvr_bot_regroup): AutonomousWorldBotController.RvrEngagement.cs.
    /// </summary>
    private bool RvrTacticsTravelHold(BotBrain brain, GameBot bot)
    {
        if (brain?.RvrTacticsActive() != true || !(Properties.RVR_BOT_REGROUP || Properties.RVR_BOT_SIZE_UP))
            return false;
        return RvrStanceTravelHold(brain, bot);
    }

    /// <summary>
    /// Hook: ChooseRvrDestination, after the event layer chose (its plan is in _rvrPlanView, #530; a siege or a
    /// defense call comes first). <paramref name="chosen"/> is today's destination, null when there is none. Returns
    /// the destination the tactics want instead (the next check spot of the group's circuit, a lane's end), or null
    /// for no opinion. Work packages 5 (rvr_roam_lanes) and 6 (rvr_roaming_parties, rvr_roam_checks:
    /// <see cref="RvrRoamersDestination"/>).
    /// </summary>
    private CampDestination RvrTacticsRoamDestination(GameBot bot, CampDestination chosen)
    {
        if (!(Properties.RVR_ROAM_LANES || Properties.RVR_ROAMING_PARTIES || Properties.RVR_ROAM_CHECKS) ||
            !BotBrain.RvrTacticsApplyTo(bot))
            return null;
        return RvrRoamersDestination(bot, chosen, _rvrPlanView ?? new(_rvrIntent, _rvrSharedEvent));
    }

    /// <summary>The plan the event layer just chose, while ChooseRvrDestination asks the roam override for its proposal (#530).</summary>
    private AutonomousRvrEventLayer.IntentView? _rvrPlanView;

    // ---- work package 6: roaming eights, soloers, duos and stealthers ---------------------------------------------------

    /// <summary>Where one walker (a group, or a bot alone) is on its circuit or at its spot. Locked by its owner.</summary>
    private sealed class RvrRoamBox
    {
        public RvrRoamWalk Walk;
        public RvrPartyKind Kind;
        public bool Stealther;
        public RvrSkillTier Tier;
        public string Walker = string.Empty;
        public long NextLookAt;
        public int PreySeen;
        public int RoamersSeen;
        public readonly HashSet<GameBot> Tracked = new();
        public GameBot Hunting;
        public long HuntLoggedFor;
    }

    private static readonly ConditionalWeakTable<object, RvrRoamBox> RvrRoamBoxes = new();
    // Stealthers camping each spot, per realm (loose packs): bot id -> until.
    private static readonly ConcurrentDictionary<(string Spot, eRealm Realm), ConcurrentDictionary<long, long>> RvrStealthCamps = new();
    private const long RvrLookMilliseconds = 3_000;
    private const int RvrHuntApproach = 700;

    /// <summary>A roaming eight's leader (or member) does not open sieges: <c>siegeReady</c> is false for it.</summary>
    private static bool RvrTacticsIsRoamingEight(GameBot bot) =>
        AutonomousBotGroupCoordinator.RvrRoamSplitActive && Properties.RVR_ROAM_CHECKS &&
        AutonomousBotGroupCoordinator.RvrPartyKindOf(bot) == RvrPartyKind.Eight;

    private static bool IsRvrRoamDestination(string id) => id != null &&
        (id.StartsWith("rvr-spot-", StringComparison.Ordinal) || id.StartsWith("rvr-prey-", StringComparison.Ordinal));

    private static bool CampingStealther(GameBot bot) => bot?.CharacterClass != null &&
        RvrStealthOpener.IsCampingStealther((eCharacterClass)bot.CharacterClass.ID) &&
        (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0;

    private string RvrWalkerId(GameBot bot) => bot.Group != null && _groupDirective?.GroupId is { Length: > 0 } id ? id : $"rvr-{bot.DatabaseID}";

    private static CampDestination RvrSpotDestination(RvrFrontierPoi spot)
    {
        string zone = WorldMgr.GetRegion(spot.Region)?.GetZone((int)spot.Stand.X, (int)spot.Stand.Y)?.Description ?? spot.Label;
        return new CampDestination("rvr-spot-" + spot.Id, spot.Label, zone, spot.Region, (int)spot.Stand.X, (int)spot.Stand.Y,
            (int)spot.Stand.Z, 1, false, true);
    }

    /// <summary>
    /// Work package 6's roam destination. An eight's leader walks a circuit of its realm, chosen by the live count of
    /// enemy levelling bots on it; a soloer or duo takes a chokepoint or camp, a stealther a chokepoint where its realm's
    /// stealthers camp. Sieges, defence calls, relic hunts and warbands are left as chosen. Null for no opinion.
    /// </summary>
    private CampDestination RvrRoamersDestination(GameBot bot, CampDestination chosen, AutonomousRvrEventLayer.IntentView plan)
    {
        if (!Properties.RVR_ROAM_CHECKS || !AutonomousBotGroupCoordinator.RvrRoamSplitActive)
            return null;
        // #530: the event layer's rule for a roam proposal (sieges, defence calls, events and non-force hunts stay as chosen).
        if (!AutonomousRvrEventLayer.AcceptsRoamOverride(plan.Intent, plan.Shared, chosen?.Id))
            return null;
        // The old frontiers only: a bot in a battleground or a battle dungeon, with no frontier goal, keeps its own.
        if (bot.CurrentRegionID is not (1 or 100 or 200) && chosen?.RegionId is not (1 or 100 or 200))
            return null;
        RvrPartyKind kind = AutonomousBotGroupCoordinator.RvrPartyKindOf(bot);
        if (kind == RvrPartyKind.Warband || bot.Group != null && _groupDirective?.Leader != bot)
            return null;
        // #417: an eight goes after the live enemy force the planner found near it (its circuit waits); before, the
        // circuit replaced every such hunt and the eights walked past the fights.
        if (kind == RvrPartyKind.Eight && plan.Intent == AutonomousRvrEventLayer.Intent.HuntEnemy &&
            chosen?.Id.StartsWith("rvr-enemy-", StringComparison.Ordinal) == true &&
            RvrRoamCircuits.EightFollowsHunt(chosen.RegionId == bot.CurrentRegionID, Vector2.Distance(new(bot.X, bot.Y), new(chosen.X, chosen.Y))))
        {
            long at = GameLoop.GameLoopTime;
            if (RvrLogBudget.Take("RVR_ROAM_CHASE", at, 60))
                Log.Info($"RVR_ROAM_CHASE group={RvrWalkerId(bot)} realm={GlobalConstants.RealmToName(bot.Realm)} target={chosen.Id} " +
                         $"name=\"{chosen.MonsterName}\" distance={(int)Vector2.Distance(new(bot.X, bot.Y), new(chosen.X, chosen.Y))} " +
                         $"limit={(int)RvrRoamCircuits.ChaseRadius} region={bot.CurrentRegionID}");
            return null;
        }
        RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
        if (catalog == null)
            return null;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrCheckSpots.EnsureValidation();
        long now = GameLoop.GameLoopTime;
        RvrRoamPrey.Refresh(catalog, now);
        RvrRoamBox box = RvrRoamBoxes.GetValue(RvrFightBoards.KeyOf(bot), _ => new RvrRoamBox());
        lock (box)
        {
            if (box.Walk == null || box.Walk.Done || box.Kind != kind || !UsableWalk(catalog, box.Walk, bot.Realm))
            {
                RvrRoamWalk walk = kind == RvrPartyKind.Eight ? ChooseCircuitWalk(bot, catalog, now, box) : ChooseSoloWalk(bot, catalog, now, box);
                if (walk == null)
                    return null;
                box.Walk = walk;
                box.Kind = kind;
                box.Tracked.Clear();
                box.Hunting = null;
                box.PreySeen = 0;
                box.RoamersSeen = 0;
            }
            RvrFrontierPoi spot = catalog.Spot(box.Walk.CurrentSpot);
            return spot == null ? null : RvrSpotDestination(spot);
        }
    }

    private static bool UsableWalk(RvrCheckSpotCatalog catalog, RvrRoamWalk walk, eRealm realm) =>
        walk.Route.All(id => catalog.Spot(id) is { } spot && catalog.Usable(spot)) &&
        (walk.Id.StartsWith("spot:", StringComparison.Ordinal) ? catalog.Usable(catalog.Spot(walk.Route[^1]), realm)
            : catalog.Frontiers.SelectMany(f => f.Circuits).FirstOrDefault(c => c.Id == walk.Id) is { } circuit && catalog.Usable(circuit));

    /// <summary>The frontiers this bot can work: the one it is in and those a porter reaches, or the one it is committed to.</summary>
    private HashSet<ushort> RvrRoamRegions(GameBot bot, Func<ushort, bool> hasWork)
    {
        HashSet<ushort> regions = ReachableRegions(bot.Realm, bot.CurrentRegionID);
        foreach (ushort frontier in AutonomousFrontierTransport.PorterFrontiers(bot.Realm, bot.CurrentRegionID))
            regions.Add(frontier);
        regions.RemoveWhere(region => region is not (1 or 100 or 200) || !hasWork(region));
        if (GameLoop.GameLoopTime < _rvrFrontierCommittedUntil && regions.Contains(_rvrFrontierRegion))
            return [_rvrFrontierRegion];
        return regions;
    }

    private RvrRoamWalk ChooseCircuitWalk(GameBot bot, RvrCheckSpotCatalog catalog, long now, RvrRoamBox box)
    {
        bool HasCircuits(ushort region) => catalog.Frontier(region)?.Circuits.Any(c => c.Realm == bot.Realm && catalog.Usable(c)) == true;
        HashSet<ushort> regions = RvrRoamRegions(bot, HasCircuits);
        RvrRoamCircuit[] circuits = catalog.Frontiers.Where(f => regions.Contains(f.Region)).SelectMany(f => f.Circuits)
            .Where(c => c.Realm == bot.Realm && catalog.Usable(c)).ToArray();
        if (circuits.Length == 0)
            return null;
        RvrSpotPrey Prey(RvrFrontierPoi spot) => RvrRoamPrey.For(spot, bot.Realm);
        bool reserve = RvrRoamCircuits.ReserveMode(circuits.SelectMany(c => c.Spots).Distinct().Select(catalog.Spot).Where(s => s != null), Prey, now);
        var weighted = circuits.Select(c => (c, RvrRoamCircuits.CircuitWeight(c, catalog, Prey, now, reserve) *
            (c.Region == bot.CurrentRegionID ? 1.5 : 1.0))).ToArray();
        RvrRoamCircuit circuit = RvrRoamCircuits.Pick(weighted, Random.Shared.NextDouble());
        if (circuit == null)
            return null;
        // #417: which circuit, and how many enemy RvR bots it and the others had at the last sample.
        if (RvrLogBudget.Take("RVR_ROAM_CIRCUIT", now, 60))
        {
            int enemies = RvrRoamCircuits.CircuitRoamers(circuit, catalog, Prey, reserve);
            int best = circuits.Max(c => RvrRoamCircuits.CircuitRoamers(c, catalog, Prey, reserve));
            Log.Info($"RVR_ROAM_CIRCUIT group={RvrWalkerId(bot)} realm={GlobalConstants.RealmToName(bot.Realm)} circuit={circuit.Id} " +
                     $"region={circuit.Region} enemiesNear={enemies} bestEnemiesNear={best} circuits={circuits.Length} reserve={reserve} " +
                     $"sampleAgeSeconds={(now - RvrRoamPrey.SampledAt) / 1000}");
        }
        box.Tier = RvrFightBoards.DefaultSkillTier(bot);
        box.Walker = RvrWalkerId(bot);
        box.Stealther = false;
        long moveOn = RvrRoamCircuits.MoveOnMilliseconds(Properties.RVR_ROAM_MOVE_ON_MINUTES, box.Tier, Random.Shared.NextDouble());
        RvrRoamWalk walk = RvrRoamCircuits.Begin(circuit, catalog, box.Walker, reserve, moveOn, now, bot.CurrentRegionID, new(bot.X, bot.Y, bot.Z));
        return walk.Route.IsEmpty ? null : walk;
    }

    private RvrRoamWalk ChooseSoloWalk(GameBot bot, RvrCheckSpotCatalog catalog, long now, RvrRoamBox box)
    {
        bool stealther = CampingStealther(bot);
        bool Fits(RvrFrontierPoi spot) => (stealther ? spot.ForStealthers : spot.ForSoloers) && catalog.Usable(spot, bot.Realm);
        HashSet<ushort> regions = RvrRoamRegions(bot, region => catalog.Frontier(region)?.Spots.Any(Fits) == true);
        RvrFrontierPoi[] spots = catalog.Frontiers.Where(f => regions.Contains(f.Region)).SelectMany(f => f.Spots).Where(Fits).ToArray();
        if (spots.Length == 0)
            return null;
        RvrSpotPrey Prey(RvrFrontierPoi spot) => RvrRoamPrey.For(spot, bot.Realm);
        bool reserve = RvrRoamCircuits.ReserveMode(spots, Prey, now);
        var weighted = spots.Where(s => reserve || !s.ReserveZone)
            .Select(s => (s, RvrRoamCircuits.SoloSpotWeight(Prey(s), now, stealther, StealthCampers(s.Id, bot.Realm, now)) *
                (s.Region == bot.CurrentRegionID ? 1.5 : 1.0))).ToArray();
        RvrFrontierPoi spot = RvrRoamCircuits.Pick(weighted, Random.Shared.NextDouble());
        if (spot == null)
            return null;
        box.Tier = RvrFightBoards.DefaultSkillTier(bot);
        box.Walker = RvrWalkerId(bot);
        box.Stealther = stealther;
        long moveOn = RvrRoamCircuits.MoveOnMilliseconds(Properties.RVR_ROAM_MOVE_ON_MINUTES, box.Tier, Random.Shared.NextDouble());
        RvrRoamWalk walk = RvrRoamCircuits.BeginSolo(spot, catalog, bot.Realm, box.Walker, stealther, moveOn, now);
        if (stealther)
            RvrStealthCamps.GetOrAdd((spot.Id, bot.Realm), _ => new())[bot.DatabaseID] = now + walk.DwellMaximumMilliseconds + 600_000;
        return walk;
    }

    private static int StealthCampers(string spot, eRealm realm, long now)
    {
        if (!RvrStealthCamps.TryGetValue((spot, realm), out var campers))
            return 0;
        foreach ((long id, long until) in campers)
            if (until <= now) campers.TryRemove(id, out _);
        return campers.Count;
    }

    /// <summary>
    /// The prey the walker can see inside the spot's radius: enemy levelling gamebots (a PvE goal) for every walker, and
    /// for a roaming eight also enemy RvR gamebots whose numbers it would take on (RvrRoamCircuits.MayHuntAtSpot;
    /// fix/rvr-quiet-frontier). Only what is not hidden (stealth detection is work package 1's). <paramref name="roamers"/>
    /// is how many of the prey are not levellers.
    /// </summary>
    private static GameBot[] RvrPreyAt(GameBot bot, RvrFrontierPoi spot, RvrPartyKind kind, RvrSkillTier tier, out int roamers)
    {
        roamers = 0;
        ushort radius = (ushort)Math.Min(6_000, spot.Radius + 1_500);
        float reach = spot.Radius + 500f;
        static bool Leveller(GameBot prey) => AutonomousObjectiveAssignments.Is(prey, eAutonomousObjectiveKind.SoloPve) ||
                                              AutonomousObjectiveAssignments.Is(prey, eAutonomousObjectiveKind.GroupPve);
        GameBot[] seen = bot.GetNPCsInRadius(radius).OfType<GameBot>()
            .Where(prey => prey.IsAlive && prey.IsAutonomousWorldBot && prey.Realm != bot.Realm && prey.Realm != eRealm.None &&
                           !prey.IsStealthed && prey.ObjectState == GameObject.eObjectState.Active)
            .ToArray();
        GameBot[] inReach = seen.Where(prey => Vector2.Distance(new(prey.X, prey.Y), new(spot.Position.X, spot.Position.Y)) <= reach).ToArray();
        if (kind != RvrPartyKind.Eight)
            return inReach.Where(Leveller).ToArray();
        int ownHeads = 1 + (bot.Group?.GetMembersInTheGroup().Count(member => member != bot && member.IsAlive &&
            member.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(member, 3_000)) ?? 0);
        GamePlayer[] players = bot.GetPlayersInRadius(radius).Where(player => player.IsAlive && player.Realm != bot.Realm &&
            player.Realm != eRealm.None && !player.IsStealthed).ToArray();
        var prey = new List<GameBot>(inReach.Length);
        foreach (GameBot target in inReach)
        {
            bool leveller = Leveller(target);
            if (!leveller)
            {
                int near = seen.Count(other => other.IsWithinRadius(target, RvrRoamCircuits.PreyPartyRadius)) +
                           players.Count(player => player.IsWithinRadius(target, RvrRoamCircuits.PreyPartyRadius));
                if (!RvrRoamCircuits.MayHuntAtSpot(false, ownHeads, near, tier))
                    continue;
                roamers++;
            }
            prey.Add(target);
        }
        return prey.ToArray();
    }

    /// <summary>
    /// Work package 6's turn in the RvR turn (ExecuteRvr, before a roamer's arrival re-plan): a walker at its check spot
    /// stands at the standing point and looks for 30-60 s (a soloer 2-4 min, a stealther 4-8, hidden), hunts the enemy
    /// levelling bots it notices there, leaves within rvr_roam_move_on_minutes of a kill, and walks on to the next spot.
    /// True when it used the turn; false lets the RvR turn go on (travel to the spot, or a new plan).
    /// </summary>
    private bool RvrTacticsRoamTurn(BotBrain brain, GameBot bot)
    {
        if (_rvrDestination == null || !IsRvrRoamDestination(_rvrDestination.Id))
            return false;
        RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
        if (_rvrSharedEvent || catalog == null || !Properties.RVR_ROAM_CHECKS || brain?.RvrTacticsActive() != true ||
            !RvrRoamBoxes.TryGetValue(RvrFightBoards.KeyOf(bot), out RvrRoamBox box))
        {
            _rvrDestination = null; // the walk is over (switched off, out of scope): the RvR turn plans again
            return false;
        }
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        long now = GameLoop.GameLoopTime;
        lock (box)
        {
            RvrRoamWalk walk = box.Walk;
            RvrFrontierPoi spot = catalog.Spot(walk?.CurrentSpot);
            if (walk == null || spot == null || !catalog.Usable(spot))
            {
                box.Walk = walk == null ? null : RvrRoamCircuits.Advance(walk);
                _rvrRuleHint = "roam.advance";
                _rvrDestination = box.Walk is { Done: false } next && catalog.Spot(next.CurrentSpot) is { } nextSpot ? RvrSpotDestination(nextSpot) : null;
                return false;
            }
            if (bot.CurrentRegionID != spot.Region)
                return false;
            Vector2 at = new(bot.X, bot.Y);
            bool inArea = Vector2.Distance(at, new(spot.Stand.X, spot.Stand.Y)) <= CampArrivalRadius ||
                          Vector2.Distance(at, new(spot.Position.X, spot.Position.Y)) <= spot.Radius;
            bool passing = walk.Index < walk.LoopStart && walk.Id.StartsWith("spot:", StringComparison.Ordinal);

            // Look for prey every few seconds, never every think; a casual group misses some looks.
            GameBot[] prey = null;
            if (inArea && !passing && now >= box.NextLookAt)
            {
                box.NextLookAt = now + RvrLookMilliseconds;
                prey = RvrPreyAt(bot, spot, box.Kind, box.Tier, out int roamers);
                foreach (GameBot dead in box.Tracked.Where(tracked => !tracked.IsAlive).ToArray())
                {
                    box.Tracked.Remove(dead);
                    walk = RvrRoamCircuits.RecordKill(walk, now);
                }
                box.Tracked.RemoveWhere(tracked => tracked.ObjectState != GameObject.eObjectState.Active ||
                    tracked.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(tracked, spot.Radius + 3_000));
                if (box.Kind == RvrPartyKind.Small)
                    prey = prey.Where(target => SoloMayHunt(bot, target, prey, box.Tier)).ToArray();
                if (prey.Length > 0 && Random.Shared.NextDouble() >= RvrRoamCircuits.NoticeChance(box.Tier))
                    prey = [];
                foreach (GameBot target in prey)
                    box.Tracked.Add(target);
                box.PreySeen = Math.Max(box.PreySeen, prey.Length);
                box.RoamersSeen = Math.Max(box.RoamersSeen, prey.Length == 0 ? 0 : roamers);
            }
            bool preyInView = box.Tracked.Any(tracked => tracked.IsAlive);
            RvrRoamStep step = RvrRoamCircuits.Step(walk, now, inArea, preyInView);
            box.Walk = step.Walk;
            switch (step.Action)
            {
                case RvrRoamAction.Travel:
                    return false;
                case RvrRoamAction.Hunt:
                    GameBot quarry = box.Tracked.Where(tracked => tracked.IsAlive).OrderBy(tracked => bot.GetDistanceTo(tracked)).First();
                    if (box.Hunting != quarry && box.HuntLoggedFor != quarry.ObjectID && RvrLogBudget.Take("RVR_ROAM_HUNT", now, 120))
                    {
                        box.HuntLoggedFor = quarry.ObjectID;
                        Log.Info($"RVR_ROAM_HUNT group={box.Walker} realm={GlobalConstants.RealmToName(bot.Realm)} kind={box.Kind} walk={walk.Id} " +
                                 $"spot={spot.Id} prey=\"{quarry.Name}\" preyRealm={GlobalConstants.RealmToName(quarry.Realm)} preyLevel={quarry.Level} " +
                                 $"seen={box.Tracked.Count} distance={(int)bot.GetDistanceTo(quarry)} tier={box.Tier} region={bot.CurrentRegionID}");
                    }
                    box.Hunting = quarry;
                    if (!bot.IsWithinRadius(quarry, RvrHuntApproach))
                        IssuePath(bot, new Vector3(quarry.X, quarry.Y, quarry.Z), retargetWhileMoving: true);
                    else
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                    }
                    SetRvrStatus(bot, $"Hunting near {spot.Label}", "Check frontier spots for enemy levellers", quarry.Name, quarry.Name);
                    return true;
                case RvrRoamAction.Check:
                    box.Hunting = null;
                    if (passing)
                    {
                        // A via spot of a soloer's route: walk through it.
                        box.Walk = RvrRoamCircuits.Advance(step.Walk);
                        return NextRoamSpot(bot, catalog, box);
                    }
                    if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), spot.Stand) > 100 * 100)
                        IssuePath(bot, spot.Stand);
                    else
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        if (box.Stealther && !bot.IsStealthed && !bot.InCombat)
                            bot.Stealth(true);
                    }
                    SetRvrStatus(bot, $"Checking {spot.Label}", "Check frontier spots for enemy levellers",
                        box.Stealther ? "Camping the spot in stealth" : "Watching the spot for enemy levellers", spot.Label);
                    return true;
                default:
                    if (!passing)
                        LogRoamCheck(bot, box, walk, spot, step.Action, now); // a soloer's via spot is passed, not checked
                    box.Tracked.Clear();
                    box.Hunting = null;
                    box.PreySeen = 0;
                    box.RoamersSeen = 0;
                    // #522: a stealther walks on hidden (BotStealthTravel owns its stealth while it moves).
                    if (bot.IsStealthed && !bot.InCombat && !BotStealthTravel.HoldsStealth(bot))
                        bot.Stealth(false);
                    if (box.Stealther && RvrStealthCamps.TryGetValue((spot.Id, bot.Realm), out var campers))
                        campers.TryRemove(bot.DatabaseID, out _);
                    if (step.Action == RvrRoamAction.Done)
                    {
                        box.Walk = null;
                        _rvrDestination = null;
                        return false;
                    }
                    return NextRoamSpot(bot, catalog, box);
            }
        }
    }

    private bool NextRoamSpot(GameBot bot, RvrCheckSpotCatalog catalog, RvrRoamBox box)
    {
        if (box.Walk is { Done: false } walk && catalog.Spot(walk.CurrentSpot) is { } next)
        {
            _rvrRuleHint = "roam.next";
            _rvrDestination = RvrSpotDestination(next);
            _rvrApproachDestination = null;
            _patrolDestination = null;
            _campStartedTick = GameLoop.GameLoopTime;
        }
        else
        {
            box.Walk = null;
            _rvrDestination = null;
        }
        return false;
    }

    private static void LogRoamCheck(GameBot bot, RvrRoamBox box, RvrRoamWalk walk, RvrFrontierPoi spot, RvrRoamAction action, long now)
    {
        string result = walk.KillsHere > 0 ? "kill" : box.PreySeen > 0 ? "hunted" : "clear";
        long stayed = walk.ArrivedAt > 0 ? now - walk.ArrivedAt : 0;
        if (RvrLogBudget.Take("RVR_ROAM_CHECK", now, 120))
            Log.Info($"RVR_ROAM_CHECK group={box.Walker} realm={GlobalConstants.RealmToName(bot.Realm)} kind={box.Kind} walk={walk.Id} " +
                     $"spot={spot.Id} type={spot.Kind} region={spot.Region} preySeen={box.PreySeen} roamersSeen={box.RoamersSeen} kills={walk.KillsHere} result={result} " +
                     $"seconds={stayed / 1000} lap={walk.Laps + 1}/{walk.MaxLaps} reserve={walk.ReserveMode} tier={box.Tier}");
        if (action == RvrRoamAction.MoveOn && walk.KillAt > 0 && RvrLogBudget.Take("RVR_ROAM_MOVE_ON", now, 120))
            Log.Info($"RVR_ROAM_MOVE_ON group={box.Walker} realm={GlobalConstants.RealmToName(bot.Realm)} kind={box.Kind} spot={spot.Id} " +
                     $"kills={walk.KillsHere} secondsAfterKill={(now - walk.KillAt) / 1000} limitSeconds={walk.MoveOnMilliseconds / 1000} tier={box.Tier}");
    }

    // ---- work package 6: a soloer opens only when the odds allow --------------------------------------------------------

    private static bool MainHealer(GameLiving living)
    {
        int? id = living switch
        {
            GameBot bot => bot.CharacterClass?.ID,
            GamePlayer player => player.CharacterClass?.ID,
            _ => null,
        };
        return id is int classId && RvrRoles.For((eCharacterClass)classId) == RvrRole.Healer;
    }

    private static RvrSoloFacts SoloFacts(GameBot bot, GameLiving target, IReadOnlyCollection<GameLiving> seen)
    {
        int allies = 1 + (bot.Group?.GetMembersInTheGroup().Count(member => member != bot && member.IsAlive &&
            member.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(member, RvrSoloEngage.GroupRadius)) ?? 0);
        GameLiving[] near = seen.Where(other => other is GamePlayer or GameBot && other.IsAlive &&
            other.CurrentRegionID == target.CurrentRegionID && target.IsWithinRadius(other, RvrSoloEngage.GroupRadius)).ToArray();
        return new RvrSoloFacts
        {
            OwnLevel = bot.Level,
            OwnHealthPercent = bot.HealthPercent,
            UsesPower = bot.MaxMana > 0,
            OwnPowerPercent = bot.ManaPercent,
            Allies = allies,
            Stealthed = bot.IsStealthed,
            TargetLevel = target.Level,
            TargetHealthPercent = target.HealthPercent,
            EnemiesNearTarget = Math.Max(1, near.Length),
            HealersNearTarget = near.Count(MainHealer),
            TargetBusy = target.InCombat && target.TargetObject is GameLiving other && other != bot && other.Realm != bot.Realm,
            TargetAttackingUs = FiringOnRealm(bot, target),
        };
    }

    /// <summary>The same roll for a bot, a target and a minute, so the decision does not flicker scan to scan.</summary>
    private static double SoloRoll(GameBot bot, GameLiving target) =>
        RvrRoamCircuits.Roll($"solo|{bot.DatabaseID}", target.ObjectID * 1_000L + GameLoop.GameLoopTime / 60_000);

    private static bool SoloMayHunt(GameBot bot, GameLiving target, GameLiving[] seen, RvrSkillTier tier) =>
        RvrSoloEngage.MayOpen(SoloFacts(bot, target, seen), tier, SoloRoll(bot, target));

    /// <summary>
    /// For a soloer, duo or stealther (rvr_bot_stealth_openers): the perceived targets it may open on (RvrSoloEngage). A
    /// group, or the switch off: the same array.
    /// </summary>
    private GameLiving[] RvrSoloGate(GameBot bot, GameLiving[] visible)
    {
        if (!Properties.RVR_BOT_STEALTH_OPENERS || !AutonomousBotGroupCoordinator.RvrRoamSplitActive ||
            bot.CurrentRegionID is not (1 or 100 or 200) || AutonomousBotGroupCoordinator.RvrPartyKindOf(bot) != RvrPartyKind.Small)
            return visible;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrSkillTier tier = RvrFightBoards.DefaultSkillTier(bot);
        GameLiving[] allowed = visible.Where(target => target is not (GamePlayer or GameBot) && FiringOnRealm(bot, target) ||
            target is GamePlayer or GameBot && RvrSoloEngage.MayOpen(SoloFacts(bot, target, visible), tier, SoloRoll(bot, target))).ToArray();
        return allowed.Length == visible.Length ? visible : allowed;
    }
}
