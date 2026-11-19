using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The relic raid's bot side (relic audit 2026-10-31): lifting the relic from the enemy shrine (B1/B2/L3/L10/M4), the
/// carrier running it home into its own shrine (B3/L4), ranged bots stepping in on relic sentinels (owner ruling 2) and
/// bots battering keep doors by hand (owner ruling 1).
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    /// <summary>Bots this close to a relic they may lift go for it before taking a new guard target (L3).</summary>
    internal const int RelicFirstRadius = 400;
    /// <summary>A mounted relic counts as reached from the shrine floor: 600 around it (the old scan) and this much height.</summary>
    internal const int ShrineFloorHeight = 300;
    /// <summary>A relic ready to lift (enough raiders on the pad) waits this long for its chosen carrier, then anyone takes it.</summary>
    internal const long CarrierChoiceMilliseconds = 20_000;
    /// <summary>Sentinels (GuardFighterRK) take no damage from beyond 800; a bot closes to this range before it attacks.</summary>
    internal const int SentinelStrikeRange = 720;
    /// <summary>Bots at a gate approach (400-900 from the gate) batter the gate within this reach.</summary>
    internal const int DoorBatterReach = 1_200;
    /// <summary>A carrier that made no headway for this long with an enemy this close is cornered and fights back (L4).</summary>
    internal const long CarrierCorneredMilliseconds = 20_000;
    internal const int CarrierCorneredRadius = 350;
    /// <summary>A carrier without headway for this long is reported (RELIC_CARRIER_STUCK), once per period.</summary>
    internal const long CarrierStuckMilliseconds = 3 * 60_000;

    private static readonly ConcurrentDictionary<GameRelic, long> RelicReadySince = new();

    private bool _relicCarrierFleeing;
    private Vector3 _carrierProgressPosition;
    private ushort _carrierProgressRegion;
    private long _carrierProgressTick, _carrierStuckLoggedTick;
    private GameRelic _shrineWalkRelic;
    private Vector3 _shrineWalkFrom;
    private long _shrineWalkCheckedTick;
    private bool _shrineWalkOpen;

    // ------------------------------------------------------------------ carrier (L4)

    /// <summary>
    /// A relic carrier runs: it drops its fights and loot walks and goes straight on with the relic (ExecuteRvr). Only a
    /// crowd-controlled carrier, or one cornered (no headway for 20 s with an enemy within 350), fights back. Escorts fight.
    /// Returns true when the caller should run ExecuteRvr now.
    /// </summary>
    private bool RelicCarrierRuns(BotBrain brain, GameBot bot, long now)
    {
        if (!GameRelic.IsPlayerCarryingRelic(bot))
        {
            _relicCarrierFleeing = false;
            _carrierProgressTick = 0;
            return false;
        }
        ObserveCarrierProgress(bot, now);
        bool held = bot.IsStunned || bot.IsMezzed;
        bool cornered = !held && now - _carrierProgressTick >= CarrierCorneredMilliseconds && NearestEnemyWithin(bot, CarrierCorneredRadius) != null;
        if (held || cornered)
        {
            if (_relicCarrierFleeing && Log.IsInfoEnabled)
                Log.Info($"RELIC_CARRIER_CORNERED bot=\"{bot.Name}\" id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                    $"reason={(held ? "crowd-controlled" : "blocked")} region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z}");
            _relicCarrierFleeing = false;
            return false;
        }
        _relicCarrierFleeing = true;
        if (brain.HasAggro || bot.IsAttacking || bot.IsCasting || bot.TargetObject is GameLiving)
        {
            brain.ClearAggroList();
            if (bot.IsAttacking) bot.StopAttack();
            if (bot.IsCasting) bot.StopCurrentSpellcast();
            bot.TargetObject = null;
        }
        return true;
    }

    private void ObserveCarrierProgress(GameBot bot, long now)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        if (_carrierProgressTick == 0 || bot.CurrentRegionID != _carrierProgressRegion ||
            Vector3.DistanceSquared(here, _carrierProgressPosition) >= 120 * 120)
        {
            _carrierProgressTick = now;
            _carrierProgressPosition = here;
            _carrierProgressRegion = bot.CurrentRegionID;
        }
    }

    private static GameLiving NearestEnemyWithin(GameBot bot, int radius) =>
        bot.GetPlayersInRadius((ushort)radius).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius((ushort)radius).Where(npc => npc is GameBot or GameKeepGuard))
            .Where(living => living != bot && living.IsAlive && living.Realm != bot.Realm && living.Realm != eRealm.None &&
                GameServer.ServerRules.IsAllowedToAttack(living, bot, true))
            .OrderBy(bot.GetDistanceTo).FirstOrDefault();

    // ------------------------------------------------------------------ carrier route home (B3)

    /// <summary>The carrier's turn: capture at its realm's matching shrine, else the walk home and into the home shrine.</summary>
    private bool RelicCarrierTurn(GameBot bot, GameRelic carried)
    {
        long now = GameLoop.GameLoopTime;
        ObserveCarrierProgress(bot, now);
        GameRelicPad home = RelicMgr.GetPadsSnapshot().Where(pad =>
                pad.Realm == bot.Realm && pad.PadType == carried.RelicType && pad.MountedRelics.Count < 3)
            .OrderBy(pad => pad.CurrentRegionID == bot.CurrentRegionID ? 0 : 1).FirstOrDefault();
        if (home == null)
        {
            SetRvrStatus(bot, "Relic return blocked", "Protect the carried relic", "No matching home shrine is available");
            ReportCarrierStuck(bot, carried, now, "no home shrine with room");
            return true;
        }
        if (bot.CurrentRegionID == home.CurrentRegionID && bot.IsWithinRadius(home, 250) && carried.RelicPadTakesOver(home, false))
        {
            _rvrDestination = null;
            if (Log.IsInfoEnabled)
                Log.Info($"RELIC_CAPTURE relic=\"{carried.Name}\" carrier={bot.Name} realm={GlobalConstants.RealmToName(bot.Realm)} pad=\"{home.Name}\" path=controller");
            SetRvrStatus(bot, "Relic captured", "Relic delivered to the home shrine", carried.Name);
            return true;
        }
        if (now - _carrierProgressTick >= CarrierStuckMilliseconds)
            ReportCarrierStuck(bot, carried, now, $"no headway for {(now - _carrierProgressTick) / 60_000} minutes");
        AbstractGameKeep homeKeep = AutonomousRelicShrine.RelicKeepOf(home);
        if (homeKeep != null && bot.CurrentRegionID == home.CurrentRegionID &&
            Vector2.DistanceSquared(new(bot.X, bot.Y), new(homeKeep.X, homeKeep.Y)) <= 3500f * 3500f)
            return CarrierEntersHomeShrine(bot, home, homeKeep, carried);
        SetRvrStatus(bot, "Returning relic", "Escort the real relic to its home shrine", home.Name);
        // In the home frontier the walk goes to the home relic keep (the keep route); elsewhere across the zone points
        // (no porter takes a carrier).
        CampDestination destination = homeKeep != null && bot.CurrentRegionID == home.CurrentRegionID
            ? new CampDestination($"rvr-keep-{homeKeep.KeepID}", homeKeep.Name, homeKeep.Name, homeKeep.Region, homeKeep.X, homeKeep.Y, homeKeep.Z, 50, false, true)
            : new CampDestination("relic-home", home.Name, home.CurrentZone?.Description ?? "home shrine", home.CurrentRegionID, home.X, home.Y, home.Z, 50, false, true);
        return TravelRvrObjective(bot, destination);
    }

    /// <summary>
    /// The home pad stands inside the realm's relic keep behind its own shut gates, which split the navmesh. As
    /// TravelToDefensivePost does for defenders: walk to the doorway and use the native door, then on to the pad.
    /// </summary>
    private bool CarrierEntersHomeShrine(GameBot bot, GameRelicPad home, AbstractGameKeep keep, GameRelic carried)
    {
        var nav = PathfindingProvider.Instance;
        Vector3 post = AutonomousRelicShrine.ShrineFloor(nav, home) ?? new Vector3(home.X, home.Y, home.Z);
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Zone zone = bot.CurrentRegion?.GetZone((int)post.X, (int)post.Y);
        if (zone != null && bot.CurrentZone == zone && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, here, post))
        {
            SetRvrStatus(bot, "Returning relic", "Into the home shrine", home.Name);
            return IssuePath(bot, post);
        }
        if (bot.TempProperties.GetProperty<long>("RvrDoorPassUntil") > GameLoop.GameLoopTime) return true;
        float toPost = Vector2.Distance(new(here.X, here.Y), new(post.X, post.Y));
        foreach (GameKeepDoor door in keep.Doors.Values.OrderBy(bot.GetDistanceTo))
        {
            if (door.Realm != bot.Realm || Math.Abs(door.Z - bot.Z) > 500) continue;
            // Only a door that leads on toward the shrine: never back out through the one just passed.
            if (Vector2.Distance(new(door.X, door.Y), new(post.X, post.Y)) + 40 >= toPost) continue;
            if (bot.IsWithinRadius(door, WorldMgr.INTERACT_DISTANCE))
            {
                if (!door.TryTraverse(bot)) continue;
                bot.TempProperties.SetProperty("RvrDoorPassUntil", GameLoop.GameLoopTime + 2000);
                if (Log.IsInfoEnabled)
                    Log.Info($"RELIC_CARRIER_DOOR bot=\"{bot.Name}\" relic=\"{carried.Name}\" keep=\"{keep.Name}\" door={door.DoorId} " +
                        $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z}");
                SetRvrStatus(bot, "Returning relic", "Through the home gate", door.Name);
                return true;
            }
            if (door.CurrentZone == bot.CurrentZone && AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, here,
                    new(door.X, door.Y, door.Z)))
            {
                SetRvrStatus(bot, "Returning relic", "To the home gate", door.Name);
                return IssuePath(bot, new(door.X, door.Y, door.Z));
            }
        }
        // Outside the walls with no door walk found: the keep route to the keep itself, then try again.
        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(keep.X, keep.Y)) > 1200f * 1200f)
        {
            SetRvrStatus(bot, "Returning relic", "Escort the real relic to its home shrine", keep.Name);
            FollowKeepTravel(bot, new($"rvr-keep-{keep.KeepID}", keep.Name, keep.Name, keep.Region, keep.X, keep.Y, keep.Z, 50, false, true));
            return true;
        }
        bot.StopMovingOnPath();
        bot.StopMoving();
        SetRvrStatus(bot, "Relic return blocked", "Into the home shrine", "No walk to the shrine or its gate from here");
        ReportCarrierStuck(bot, carried, GameLoop.GameLoopTime, "no walk to the home shrine or a home gate");
        return true;
    }

    private void ReportCarrierStuck(GameBot bot, GameRelic carried, long now, string reason)
    {
        if (now - _carrierStuckLoggedTick < 120_000 && now >= _carrierStuckLoggedTick) return;
        _carrierStuckLoggedTick = now;
        if (Log.IsWarnEnabled)
            Log.Warn($"RELIC_CARRIER_STUCK bot=\"{bot.Name}\" id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} relic=\"{carried.Name}\" " +
                $"reason=\"{reason}\" status=\"{bot.PersistentRecord?.Activity}\" region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z}");
    }

    // ------------------------------------------------------------------ pickup (B1/B2/L3/L10/M4)

    /// <summary>The bot's target keep when it is an enemy relic keep.</summary>
    private AbstractGameKeep TargetRelicKeep(GameBot bot) =>
        _rvrDestination?.Id.StartsWith("rvr-keep-", StringComparison.Ordinal) == true
            ? GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).FirstOrDefault(keep =>
                keep.IsRelic && keep.Realm != bot.Realm && $"rvr-keep-{keep.KeepID}" == _rvrDestination.Id)
            : null;

    /// <summary>
    /// A relic this bot may go for: one on the ground nearby, or a mounted enemy relic on the shrine floor around the bot
    /// (600 across, 300 up or down) with a walk to it that no shut door blocks. B2: the pickup no longer waits for every
    /// door of the keep to fall, posterns and side doors included.
    /// </summary>
    private GameRelic FindInteractableRelic(GameBot bot)
    {
        foreach (GameRelic relic in RelicMgr.GetRelics())
        {
            if (relic.ObjectState is not GameObject.eObjectState.Active || relic.CurrentRegionID != bot.CurrentRegionID ||
                Distance(bot.X, bot.Y, relic.X, relic.Y) > 600)
                continue;
            if (!relic.IsMounted)
            {
                if (Math.Abs(relic.Z - bot.Z) <= 400) return relic;
                continue;
            }
            if (relic.Realm == bot.Realm || Math.Abs(relic.Z - bot.Z) > ShrineFloorHeight ||
                !RelicMgr.CanPickupRelicFromShrine(bot, relic))
                continue;
            if (HasShrineWalk(bot, relic)) return relic;
        }
        return null;
    }

    /// <summary>A walk from the bot onto the relic's floor that no shut enemy door blocks (checked every 3 s per bot).</summary>
    private bool HasShrineWalk(GameBot bot, GameRelic relic)
    {
        long now = GameLoop.GameLoopTime;
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        if (_shrineWalkRelic == relic && now - _shrineWalkCheckedTick < 3_000 && now >= _shrineWalkCheckedTick &&
            Vector3.DistanceSquared(here, _shrineWalkFrom) < 150 * 150)
            return _shrineWalkOpen;
        var nav = PathfindingProvider.Instance;
        Zone zone = bot.CurrentRegion?.GetZone(relic.X, relic.Y);
        Vector3 floor = (relic.CurrentRelicPad != null ? AutonomousRelicShrine.ShrineFloor(nav, relic.CurrentRelicPad) : null) ??
            new Vector3(relic.X, relic.Y, relic.Z);
        bool open = zone != null && bot.CurrentZone == zone &&
            (bot.IsWithinRadius(relic, WorldMgr.INTERACT_DISTANCE) ||
             AutonomousZoneItinerary.HasCompleteCorridor(AutonomousKeepApproachNavigation.ForRealm(nav, bot.CurrentRegion, bot.Realm), zone, here, floor));
        _shrineWalkRelic = relic;
        _shrineWalkFrom = here;
        _shrineWalkCheckedTick = now;
        _shrineWalkOpen = open;
        return open;
    }

    /// <summary>Walks onto the relic and lifts it, or holds the pad (counting toward the eight) while the chosen carrier does.</summary>
    private bool RelicPickupTurn(GameBot bot, GameRelic relic)
    {
        // M4: a hidden raider does not count on the pad.
        if (bot.IsStealthed) bot.Stealth(false);
        if (!bot.IsWithinRadius(relic, WorldMgr.INTERACT_DISTANCE - 24))
        {
            IssuePath(bot, new(relic.X, relic.Y, relic.Z));
            SetRvrStatus(bot, relic.IsMounted ? "Storming the relic shrine" : "Recovering dropped relic", relic.Name,
                relic.IsMounted ? "Onto the shrine floor: the relic needs raiders on the pad" : "Walking to the dropped relic");
            return true;
        }
        bot.StopMovingOnPath();
        bot.StopMoving();
        string choice = "dropped";
        if (relic.IsMounted && !IsChosenCarrier(bot, relic, out choice))
        {
            SetRvrStatus(bot, "Holding the relic shrine", relic.Name,
                $"{relic.CurrentRelicPad?.GetEnemiesOnPad() ?? 0}/{DOL.GS.ServerProperties.Properties.RELIC_PLAYERS_REQUIRED_ON_PAD} raiders on the pad; another raider carries");
            return true;
        }
        bool mounted = relic.IsMounted;
        bool pickedUp = relic.TryPickup(bot, out string refusal);
        if (pickedUp)
        {
            RelicReadySince.TryRemove(relic, out _);
            if (Log.IsInfoEnabled)
                Log.Info($"RELIC_CARRIER_CHOSEN relic=\"{relic.Name}\" bot=\"{bot.Name}\" id={bot.DatabaseID} class={bot.CharacterClass?.Name} " +
                    $"realm={GlobalConstants.RealmToName(bot.Realm)} health={bot.Health}/{bot.MaxHealth} choice={choice} fromShrine={mounted}");
        }
        SetRvrStatus(bot, pickedUp ? "Returning relic" : "Rallying at relic", relic.Name,
            pickedUp ? "Real relic secured; escorts follow the carrier home" : $"Not yet: {refusal}");
        return true;
    }

    /// <summary>
    /// M4: the raider that carries is chosen, not whoever reaches the relic first: the healthiest living, visible raider of
    /// the realm on the pad with a free bag slot. Once the relic has been ready for 20 s anyone may take it.
    /// </summary>
    private static bool IsChosenCarrier(GameBot bot, GameRelic relic, out string choice)
    {
        choice = "best";
        GameRelicPad pad = relic.CurrentRelicPad;
        if (pad == null) return true;
        long now = GameLoop.GameLoopTime;
        if (pad.GetEnemiesOnPad() < DOL.GS.ServerProperties.Properties.RELIC_PLAYERS_REQUIRED_ON_PAD)
        {
            RelicReadySince.TryRemove(relic, out _);
            choice = "too-few";
            return true;   // the pickup attempt itself reports the refusal (RELIC_PICKUP_REFUSED too-few-on-pad)
        }
        long since = RelicReadySince.GetOrAdd(relic, now);
        if (now - since >= CarrierChoiceMilliseconds) { choice = "fallback"; return true; }
        GameBot best = pad.GetNPCsInRadius(500).OfType<GameBot>()
            .Where(candidate => candidate.IsAutonomousWorldBot && candidate.Realm == bot.Realm && candidate.IsAlive &&
                candidate.ObjectState == GameObject.eObjectState.Active && !candidate.IsStealthed &&
                !GameRelic.IsPlayerCarryingRelic(candidate) &&
                candidate.Inventory?.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) is not (null or eInventorySlot.Invalid))
            .OrderByDescending(candidate => candidate.Health)
            .ThenBy(candidate => candidate.DatabaseID)
            .FirstOrDefault();
        return best == null || best == bot;
    }

    // ------------------------------------------------------------------ sentinels (owner ruling 2)

    /// <summary>
    /// A relic sentinel (GuardFighterRK) takes no damage from beyond 800. A bot targeting one from farther closes in first
    /// instead of wasting shots; with no walk to it, it gives the sentinel up for another target. True while closing in.
    /// </summary>
    private bool StepInOnSentinel(BotBrain brain, GameBot bot, GameLiving target)
    {
        if (target is not GuardFighterRK sentinel || !sentinel.IsAlive || bot.IsWithinRadius(sentinel, SentinelStrikeRange) ||
            bot.IsStunned || bot.IsMezzed)
            return false;
        if (bot.IsCasting) bot.StopCurrentSpellcast();
        if (bot.IsAttacking) bot.StopAttack();
        if (!IssuePath(bot, new(sentinel.X, sentinel.Y, sentinel.Z), retargetWhileMoving: true))
        {
            brain.RemoveFromAggroList(sentinel);
            if (bot.TargetObject == sentinel) bot.TargetObject = null;
            return false;
        }
        SetRvrStatus(bot, "Closing on sentinel", sentinel.Name, "Sentinels take no damage from beyond 800");
        return true;
    }

    // ------------------------------------------------------------------ doors by hand (owner ruling 1)

    /// <summary>
    /// A sieging bot with nothing else to fight batters the target keep's nearest shut gate by hand (melee, arrows or
    /// spells, as players did), whatever the normal combat rules allow it. Rams stay with the siegecrafters.
    /// </summary>
    private GameKeepDoor FindDoorToBatter(GameBot bot)
    {
        if (_rvrIntent is not (AutonomousRvrEventLayer.Intent.AssaultKeep or AutonomousRvrEventLayer.Intent.AssaultRelicKeep) ||
            _rvrDestination?.Id.StartsWith("rvr-keep-", StringComparison.Ordinal) != true)
            return null;
        AbstractGameKeep keep = GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID)
            .FirstOrDefault(k => $"rvr-keep-{k.KeepID}" == _rvrDestination.Id);
        if (keep == null || keep.Realm == bot.Realm || !AutonomousRvrKeepPolicy.IsSiegeObjective(keep)) return null;
        return keep.Doors.Values
            .Where(door => door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed &&
                bot.IsWithinRadius(door, DoorBatterReach) && GameServer.ServerRules.IsAllowedToAttack(bot, door, true))
            .OrderBy(bot.GetDistanceTo)
            .FirstOrDefault();
    }
}
