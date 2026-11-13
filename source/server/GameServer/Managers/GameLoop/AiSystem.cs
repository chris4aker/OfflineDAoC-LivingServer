using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DOL.GS
{
    // Living Server 1.2, AI_PROFILE ("measure before adding", audit 2026-11-03 §8.4). The list of what the profiler
    // times. The idea of a fixed list of named phases, timed with two timestamps each, comes from stefanrows's
    // BotThinkProfiler (BotThinkPhase, refs/forks/stefanrows/main, GPL-3.0); the split of brain turns by the kind of
    // bot comes from lometur's HearthDAoC bot CPU meter (BotCpuKind, #137/#148, GPL-3.0).

    /// <summary>
    /// A system the AI profiler times. Each one has a short dotted name (<see cref="AiSystemNames.Of"/>) used in the
    /// AI_PROFILE log line, the CSV file and <c>/lsprofile</c>. New members go at the end of their group; nothing is
    /// stored by number, so the order only decides the table layout. Code that is not on this list can still be timed
    /// with <see cref="AiProfile.Scope(string)"/>, which gives the name a free slot the first time it is seen.
    /// </summary>
    public enum AiSystem
    {
        // Brain turns (NpcService), by the kind of body. Everything a turn does that no other scope claims stays here.
        ThinkGamebot,
        ThinkCompanion,
        ThinkPet,
        ThinkNpc,

        // Serial planning on the game loop before the brains run (NpcService.Tick).
        SerialBrainRegistry,
        SerialRvrPlanning,
        SerialCoordinator,
        SerialCampPlanning,

        // The pulses inside AutonomousBotGroupCoordinator.PrepareCoordinatorTick, in the order they run.
        PulseRealmEventNotices,
        PulseLivingServer,
        PulseRealmEventControls,
        PulseRealmRaid,
        PulseRaidForcedParty,
        PulseEpicSpecialForces,
        PulseRvrDefense,
        PulseWarbandSupport,
        PulseObjectiveAssignments,
        PulseBrokenSessions,
        PulseFormSiegeParties,
        PulseTryFormGroups,

        // The autonomous world controller and its biggest branches.
        ControllerTick,
        ControllerStableTravel,
        ControllerRvr,
        ControllerTravelRegions,
        ControllerGroupCombat,
        ControllerRendezvous,
        ControllerFollowLeader,
        ControllerSelectCamp,
        ControllerWorkCamp,
        ControllerIssuePath,
        ControllerRouteThreats,
        ControllerRaidTravel,
        ControllerBattleground,

        // The group coordinator's per-bot pulse.
        CoordinatorPulse,

        // Route planning.
        RouteStableFindBest,
        RouteStablePortal,
        RouteRaidDungeon,
        RouteDarknessFallsChain,
        RouteZoneStep,
        RouteCorridor,
        RouteRoad,
        RouteRvrRally,

        // Single navmesh queries (the native Detour calls NavQueryProfile already counts).
        NavPath,
        NavClosestPoint,
        NavOther,

        // The frontier threat scan of an RvR bot.
        FrontierThreatScan,

        // Combat AI in BotBrain.
        CombatSpells,
        CombatHeals,
        CombatStyle,
        CombatMovement,

        // Pets.
        PetsMaintain,
        PetsSync,

        // Persistence of bot status and inventories.
        DbStatusBatch,
        DbInventoryBatch,

        // Chat.
        ChatBotTurn,
        ChatAmbient,
        ChatPlayerReply,
        ChatNews,

        // Economy and town visits.
        EconomyMerchant,
        EconomyExchange,
        EconomyValuation,
        TownIdle,
        TownService,
        TownHangout,

        // Housekeeping.
        SweepDepartedReferences,

        // Time spent waiting to take a shared lock (only the waits; an uncontended lock costs nothing here).
        LockCoordinator,
        LockRaid,
        LockRvrEvents,
        LockDatabaseWrite,

        /// <summary>Not a system: the number of built-in systems. Names given to <see cref="AiProfile.Scope(string)"/>
        /// take the slots from here up to <see cref="AiProfile.MaxSystems"/>.</summary>
        BuiltInCount,
    }

    /// <summary>The dotted names of the systems, and the free slots handed to names that are not built in.</summary>
    public static class AiSystemNames
    {
        // Slot number to name. Built-in names are filled at start; the rest are filled once, by Register, under the lock.
        private static readonly string[] Names = BuildNames();
        private static readonly ConcurrentDictionary<string, int> ByName = BuildIndex();
        private static readonly Lock RegisterLock = new();
        private static int _next = (int)AiSystem.BuiltInCount;

        /// <summary>The slot that a name that does not fit any more is counted in.</summary>
        public const string OverflowName = "other";

        /// <summary>The name of a slot, or null for a free slot.</summary>
        public static string Of(int system) => (uint)system < (uint)Names.Length ? Volatile.Read(ref Names[system]) : null;

        public static string Of(AiSystem system) => Of((int)system);

        /// <summary>True for the lock-wait systems, which the AI_PROFILE line adds up as locks_ms.</summary>
        public static bool IsLockWait(int system) => system is >= (int)AiSystem.LockCoordinator and <= (int)AiSystem.LockDatabaseWrite;

        /// <summary>
        /// The slot of a name: a built-in name gives its own system, a new name the next free slot. When every slot is
        /// taken, new names share the last slot, named <see cref="OverflowName"/>. Costs one dictionary lookup.
        /// </summary>
        public static int Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = OverflowName;
            if (ByName.TryGetValue(name, out int known))
                return known;
            lock (RegisterLock)
            {
                if (ByName.TryGetValue(name, out known))
                    return known;
                int slot;
                if (_next < AiProfile.MaxSystems - 1)
                    slot = _next++;
                else
                {
                    slot = AiProfile.MaxSystems - 1;
                    name = OverflowName;
                }
                Volatile.Write(ref Names[slot], name);
                ByName[name] = slot;
                return slot;
            }
        }

        private static ConcurrentDictionary<string, int> BuildIndex()
        {
            var index = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < (int)AiSystem.BuiltInCount; i++)
                index[Names[i]] = i;
            return index;
        }

        private static string[] BuildNames()
        {
            var names = new string[AiProfile.MaxSystems];
            for (int i = 0; i < (int)AiSystem.BuiltInCount; i++)
                names[i] = BuiltInName((AiSystem)i);
            return names;
        }

        private static string BuiltInName(AiSystem system) => system switch
        {
            AiSystem.ThinkGamebot => "think.gamebot",
            AiSystem.ThinkCompanion => "think.companion",
            AiSystem.ThinkPet => "think.pet",
            AiSystem.ThinkNpc => "think.npc",
            AiSystem.SerialBrainRegistry => "serial.brain_registry",
            AiSystem.SerialRvrPlanning => "serial.rvr_planning",
            AiSystem.SerialCoordinator => "serial.coordinator",
            AiSystem.SerialCampPlanning => "serial.camp_planning",
            AiSystem.PulseRealmEventNotices => "pulse.realm_event_notices",
            AiSystem.PulseLivingServer => "pulse.living_server",
            AiSystem.PulseRealmEventControls => "pulse.realm_event_controls",
            AiSystem.PulseRealmRaid => "pulse.realm_raid",
            AiSystem.PulseRaidForcedParty => "pulse.raid_forced_party",
            AiSystem.PulseEpicSpecialForces => "pulse.epic_special_forces",
            AiSystem.PulseRvrDefense => "pulse.rvr_defense",
            AiSystem.PulseWarbandSupport => "pulse.warband_support",
            AiSystem.PulseObjectiveAssignments => "pulse.objective_assignments",
            AiSystem.PulseBrokenSessions => "pulse.broken_sessions",
            AiSystem.PulseFormSiegeParties => "pulse.form_siege_parties",
            AiSystem.PulseTryFormGroups => "pulse.try_form_groups",
            AiSystem.ControllerTick => "controller.tick",
            AiSystem.ControllerStableTravel => "controller.stable_travel",
            AiSystem.ControllerRvr => "controller.rvr",
            AiSystem.ControllerTravelRegions => "controller.travel_regions",
            AiSystem.ControllerGroupCombat => "controller.group_combat",
            AiSystem.ControllerRendezvous => "controller.rendezvous",
            AiSystem.ControllerFollowLeader => "controller.follow_leader",
            AiSystem.ControllerSelectCamp => "controller.select_camp",
            AiSystem.ControllerWorkCamp => "controller.work_camp",
            AiSystem.ControllerIssuePath => "controller.issue_path",
            AiSystem.ControllerRouteThreats => "controller.route_threats",
            AiSystem.ControllerRaidTravel => "controller.raid_travel",
            AiSystem.ControllerBattleground => "controller.battleground",
            AiSystem.CoordinatorPulse => "coord.pulse",
            AiSystem.RouteStableFindBest => "route.stable_find_best",
            AiSystem.RouteStablePortal => "route.stable_portal",
            AiSystem.RouteRaidDungeon => "route.raid_dungeon",
            AiSystem.RouteDarknessFallsChain => "route.df_chain",
            AiSystem.RouteZoneStep => "route.zone_step",
            AiSystem.RouteCorridor => "route.corridor",
            AiSystem.RouteRoad => "route.road",
            AiSystem.RouteRvrRally => "route.rvr_rally_check",
            AiSystem.NavPath => "nav.path",
            AiSystem.NavClosestPoint => "nav.closest_point",
            AiSystem.NavOther => "nav.other",
            AiSystem.FrontierThreatScan => "frontier.threat_scan",
            AiSystem.CombatSpells => "combat.spells",
            AiSystem.CombatHeals => "combat.heals",
            AiSystem.CombatStyle => "combat.style",
            AiSystem.CombatMovement => "combat.movement",
            AiSystem.PetsMaintain => "pets.maintain",
            AiSystem.PetsSync => "pets.sync",
            AiSystem.DbStatusBatch => "db.status_batch",
            AiSystem.DbInventoryBatch => "db.inventory_batch",
            AiSystem.ChatBotTurn => "chat.bot_turn",
            AiSystem.ChatAmbient => "chat.ambient",
            AiSystem.ChatPlayerReply => "chat.player_reply",
            AiSystem.ChatNews => "chat.news",
            AiSystem.EconomyMerchant => "economy.merchant",
            AiSystem.EconomyExchange => "economy.exchange",
            AiSystem.EconomyValuation => "economy.valuation",
            AiSystem.TownIdle => "town.idle",
            AiSystem.TownService => "town.service",
            AiSystem.TownHangout => "town.hangout",
            AiSystem.SweepDepartedReferences => "sweep.departed_refs",
            AiSystem.LockCoordinator => "lock.coordinator",
            AiSystem.LockRaid => "lock.raid",
            AiSystem.LockRvrEvents => "lock.rvr_events",
            AiSystem.LockDatabaseWrite => "lock.db_write",
            _ => throw new ArgumentOutOfRangeException(nameof(system), system, "Every built-in AI system needs a name."),
        };
    }
}
