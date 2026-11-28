using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// SERVER_COLLECTION_CENSUS (issue #731, owner answer A, 2026-11-28): the size of every long-lived static collection
    /// and cache in <see cref="Registry"/>, with the server's threads, timers and GC generation sizes. The heap snapshot
    /// pair of the rc.2 run could not say what holds the ~35 MB an hour of live growth (dotnet-gcdump's 10-million-object
    /// cap and lost type names), so this row names the stores instead: in a long run each one's growth per hour can be
    /// fitted like liveMB's, and the thread and timer counts against the handle growth (~530 an hour).
    /// <list type="bullet">
    /// <item>Written from the SERVER_MEMORY report (<see cref="ServerMemoryDiagnostics"/>, every five minutes): on its first
    /// report and then on every <see cref="EveryMemoryReports"/>th, so every 30 minutes. On in every session.</item>
    /// <item>Counting runs on a thread-pool thread, never on the game loop: it reads each collection's Count (a field read
    /// for the plain collections; a ConcurrentDictionary takes its own locks for a moment) and never enumerates one. The
    /// few weak tables (<see cref="Weak"/>) have no Count and are walked; they are keyed by groups, players or monsters,
    /// and their time is weakMs=.</item>
    /// <item>Counts only: the names are code names (type.field), never a player's, a bot's or a guild's.</item>
    /// <item>One row in numbered parts: part=1/N carries the header and every part carries counts="name=count,...", each
    /// line at most <see cref="MaxLineChars"/> characters. The logger has no line limit of its own; this keeps every
    /// line readable in a text editor and in Grafana.</item>
    /// </list>
    /// Row: <c>SERVER_COLLECTION_CENSUS part= collections= unresolved= threads= poolThreads= poolPending= timers= gen0MB= gen1MB= gen2MB= lohMB= pohMB= gen2Collections= ms= weakMs= counts=</c>
    /// </summary>
    public static class ServerCollectionCensus
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>SERVER_MEMORY reports between two census rows (6 x 5 minutes = 30 minutes).</summary>
        public const int EveryMemoryReports = 6;

        /// <summary>The longest line a part may be; a longer row is split into numbered parts.</summary>
        public const int MaxLineChars = 4_000;

        public const string Marker = "SERVER_COLLECTION_CENSUS";

        /// <summary>One counted store: its name in the row and how to count it (-1 when it can't be read).</summary>
        public sealed class Entry
        {
            public string Name { get; }
            /// <summary>The declaring type's full name and the static field, for a reflected entry (null for a custom one).</summary>
            public string TypeName { get; }
            public string FieldName { get; }
            public bool IsWeak { get; }
            internal Func<long> Counter { get; }

            internal Entry(string name, string typeName, string fieldName, bool weak, Func<long> counter)
            {
                Name = name;
                TypeName = typeName;
                FieldName = fieldName;
                IsWeak = weak;
                Counter = counter;
            }

            /// <summary>The static field behind a reflected entry, or null (custom entries, or a name that no longer resolves).</summary>
            public FieldInfo Resolve()
            {
                if (TypeName == null) return null;
                Type type = typeof(GameServer).Assembly.GetType(TypeName, throwOnError: false);
                return type?.GetField(FieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            public long Count()
            {
                try { return Counter(); }
                catch { return -1; }
            }
        }

        private static readonly Dictionary<Type, PropertyInfo> CountProperties = new();
        private static readonly Dictionary<Type, PropertyInfo> ValueProperties = new();

        /// <summary>A static collection field, counted by its Count (one line per store: add new ones here).</summary>
        public static Entry Field(string typeName, string fieldName)
        {
            string name = $"{ShortTypeName(typeName)}.{fieldName}";
            Entry entry = null;
            FieldInfo field = null;
            entry = new Entry(name, typeName, fieldName, false, () =>
            {
                field ??= entry.Resolve();
                return field == null ? -1 : CountOf(field.GetValue(null));
            });
            return entry;
        }

        /// <summary>A static ConditionalWeakTable (no Count): its live entries, walked off the game loop.</summary>
        public static Entry Weak(string typeName, string fieldName)
        {
            string name = $"{ShortTypeName(typeName)}.{fieldName}";
            Entry entry = null;
            FieldInfo field = null;
            entry = new Entry(name, typeName, fieldName, true, () =>
            {
                field ??= entry.Resolve();
                if (field == null) return -1;
                if (field.GetValue(null) is not IEnumerable table) return 0;
                long count = 0;
                foreach (object _ in table) count++;
                return count;
            });
            return entry;
        }

        /// <summary>A static ConditionalWeakTable whose values are collections: the sum of their Counts (e.g. boards per region).</summary>
        public static Entry WeakValues(string name, string typeName, string fieldName)
        {
            Entry entry = null;
            FieldInfo field = null;
            entry = new Entry(name, typeName, fieldName, true, () =>
            {
                field ??= entry.Resolve();
                if (field == null) return -1;
                if (field.GetValue(null) is not IEnumerable table) return 0;
                long count = 0;
                foreach (object pair in table)
                {
                    PropertyInfo value = PropertyOf(ValueProperties, pair.GetType(), "Value");
                    long size = value == null ? -1 : CountOf(value.GetValue(pair));
                    if (size > 0) count += size;
                }
                return count;
            });
            return entry;
        }

        /// <summary>A store counted by code (an instance's own Count, or a weak table's nested holders).</summary>
        public static Entry Custom(string name, Func<long> count, bool weak = false) => new(name, null, null, weak, count);

        private static string ShortTypeName(string typeName)
        {
            int cut = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
            return cut < 0 ? typeName : typeName[(cut + 1)..];
        }

        private static PropertyInfo PropertyOf(Dictionary<Type, PropertyInfo> cache, Type type, string property)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(type, out PropertyInfo found))
                    cache[type] = found = type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
                return found;
            }
        }

        /// <summary>A collection's Count without enumerating it: 0 for null, -1 when it has no Count.</summary>
        public static long CountOf(object value)
        {
            if (value == null) return 0;
            if (value is ICollection collection) return collection.Count;
            PropertyInfo count = PropertyOf(CountProperties, value.GetType(), "Count");
            if (count == null || (count.PropertyType != typeof(int) && count.PropertyType != typeof(long))) return -1;
            return Convert.ToInt64(count.GetValue(value));
        }

        /// <summary>
        /// Every store the census counts, in row order. The ones issue #731 named come first: the RvR fight boards and
        /// their holders, the RvR contact table, the force intents, the battlegroup records, the chat conversations, the
        /// claim and hold tables, the group coordinator's sessions and formations, the NextLog throttles and the duty
        /// leases. Then every other static collection of the bot code (bots/) whose keys grow with bots, groups,
        /// monsters, players or names, and the ones elsewhere keyed by a game object or group (found by grep of the
        /// static Dictionary, ConcurrentDictionary, HashSet, List and Queue fields, 2026-11-28). Collections keyed by
        /// realm, region, class or another bounded set are left out, as are lookup tables filled at start-up.
        /// UT_Issue731CollectionCensus checks that every entry resolves.
        /// </summary>
        public static readonly IReadOnlyList<Entry> Registry =
        [
            // RvR fight boards and their per-region holders.
            Weak("DOL.GS.RvrFightBoards", "Groups"),
            Custom("RvrFightBoards.holders", RvrFightBoards.HolderCount, weak: true),
            Weak("DOL.AI.Brain.BotBrain", "FightBoards"),
            WeakValues("BotBrain.fightBoardHolders", "DOL.AI.Brain.BotBrain", "FightBoards"),
            // The RvR contact table: bots holding a contact, and the realm union's rows (never its per-bot walk).
            Custom("RvrContactTable.owners", () => AutonomousWorldBotController.RvrContactTable.Owners),
            Custom("RvrContactTable.realmRows", () => AutonomousWorldBotController.RvrContactTable.RealmRows),
            // Force intents, battlegroup records, claim holds, coordinator sessions, NextLog throttles, duty leases.
            Field("DOL.GS.AutonomousRvrEventLayer", "ForceIntents"),
            Field("DOL.GS.AutonomousRvrEventLayer", "Battlegroups"),
            Field("DOL.GS.AutonomousRvrEventLayer", "BattlegroupOfForce"),
            Field("DOL.GS.AutonomousRvrEventLayer", "BattlegroupRelinkAfter"),
            Field("DOL.GS.AutonomousRvrEventLayer", "BattlegroupStuckReports"),
            Field("DOL.GS.AutonomousRvrEventLayer", "ZergEnrolmentLeaves"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "_battlegroupGroups"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "_battlegroundBattlegroupGroups"),
            Field("DOL.GS.AutonomousRvrEventLayer", "ClaimHolds"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "Sessions"),
            Field("DOL.GS.CompanionDarknessFallsPaths", "NextLog"),
            Field("DOL.GS.BotDutyLeases", "Held"),
            Field("DOL.GS.BotDutyLeases", "LastRefusal"),
            Custom("ChatConversations.Live", () => ChatConversations.Live.Count),
            // Formations and claims kept in weak tables.
            Weak("DOL.GS.AutonomousBotGroupCoordinator", "RoleFormations"),
            Weak("DOL.GS.CompanionRvrFormation", "Holders"),
            Weak("DOL.GS.AutonomousMobClaims", "Claims"),
            Weak("DOL.GS.BotPvpCrowdControl", "Claims"),
            // Every other static store of the bot code and the game-object-keyed ones elsewhere.
            Field("DOL.AI.Brain.BotBrain", "PlayerRezClaims"),
            Field("DOL.AI.Brain.BotBrain", "PlayerRezOffered"),
            Field("DOL.GS.BotGroupInvite", "Considering"),
            Field("DOL.GS.BotInviteDecision", "Rolls"),
            Field("DOL.GS.BotManager", "ActiveBots"),
            Field("DOL.GS.CompanionRaidSiege", "Sessions"),
            Field("DOL.GS.CompanionRaidSiege", "AssaultRequests"),
            Field("DOL.GS.CompanionRaidSiege", "Unreachable"),
            Field("DOL.GS.CompanionRaidSiege", "ReachWatch"),
            Field("DOL.GS.EncounterCalls", "Last"),
            Field("DOL.GS.EncounterDisplacementRecovery", "Active"),
            Field("DOL.GS.EncounterDisplacementRecovery", "Recent"),
            Field("DOL.GS.EncounterDisplacementRecovery", "Bounces"),
            Field("DOL.GS.EncounterDisplacementRecovery", "LastCall"),
            Field("DOL.GS.GameBot+SpecRefundLog", "ByClass"),
            Field("DOL.GS.TemporaryGroupStableTravel", "ActiveRoutes"),
            Field("DOL.GS.TemporaryGroupStableTravel", "PendingPlayerTransfers"),
            Field("DOL.GS.TemporaryGroupStableTravel", "TransferOrigins"),
            Field("DOL.GS.TemporaryGroupStableTravel", "EnemyRealmNoFollowLogged"),
            Field("DOL.GS.AutonomousBattlegroundExit", "Watches"),
            Field("DOL.GS.AutonomousBattlegroundExit", "ReentryBlockedUntil"),
            Field("DOL.GS.AutonomousBattlegroundExit", "Sightings"),
            Field("DOL.GS.AutonomousBeggars", "States"),
            Field("DOL.GS.AutonomousBeggars", "Online"),
            Field("DOL.GS.AutonomousBeggars", "Marked"),
            Field("DOL.GS.AutonomousBotBonds", "Known"),
            Field("DOL.GS.AutonomousBotChatCoordinator", "LastLocalPlayerResponse"),
            Field("DOL.GS.AutonomousBotChatCoordinator", "LastTellResponse"),
            Field("DOL.GS.AutonomousBotChatCoordinator", "LastAnswered"),
            Field("DOL.GS.AutonomousBotEconomy", "ServiceDecisions"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "Trips"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "RvrPendingKind"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "RvrPendingRoster"),
            Field("DOL.GS.AutonomousBotGroupCoordinator", "RvrShortEightFallback"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastIdleByGuild"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastReplyByGuild"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastDeathByGuild"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastGreetingByPlayer"),
            Field("DOL.GS.AutonomousBotGuildSocial", "DeclinedUntil"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastNearbyOffer"),
            Field("DOL.GS.AutonomousBotGuildSocial", "LastTooLowAnswer"),
            Field("DOL.GS.AutonomousBotGuildSocial", "RecentByGuild"),
            Field("DOL.GS.AutonomousBotGuildSocial", "RecentField"),
            Field("DOL.GS.AutonomousBotGuilds", "Members"),
            Field("DOL.GS.AutonomousBotGuilds", "LastGuildChat"),
            Field("DOL.GS.AutonomousBotGuilds", "LastGuildReply"),
            Field("DOL.GS.AutonomousBotLinesHeard", "Known"),
            Field("DOL.GS.AutonomousBotLinesHeard", "Saved"),
            Field("DOL.GS.AutonomousBotMapMarkers", "VisibleByPlayer"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "Members"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "BotGuilds"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "Summaries"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "MemberRows"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "PendingWrites"),
            Field("DOL.GS.AutonomousBotRealmGuilds", "PendingDeletes"),
            Field("DOL.GS.AutonomousBotRegistry", "Active"),
            Field("DOL.GS.AutonomousBotRegistry", "SpecialForces"),
            Field("DOL.GS.AutonomousBotRegistry", "BuffBots"),
            Field("DOL.GS.AutonomousBotRegistry", "Hired"),
            Field("DOL.GS.AutonomousBotSchedule", "BreakUntil"),
            Field("DOL.GS.AutonomousBotSchedule", "ReturningFromBreak"),
            Field("DOL.GS.AutonomousBotSchedule", "RageQuitUntil"),
            Field("DOL.GS.AutonomousBotSchedule", "ReturningFromRageQuit"),
            Field("DOL.GS.AutonomousBotSchedule", "LeavingNow"),
            Field("DOL.GS.AutonomousBotSchedule", "OverstaySince"),
            Field("DOL.GS.AutonomousBotSchedule", "Quitting"),
            Field("DOL.GS.AutonomousBotSchedule", "SaidGoodbyeAt"),
            Field("DOL.GS.AutonomousBotSchedule", "Frozen"),
            Field("DOL.GS.AutonomousBotSchedule", "LastLinkdead"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "Pending"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "PendingOrder"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "InPendingOrder"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "PendingPriority"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "PendingPriorityOrder"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "PendingInventory"),
            Field("DOL.GS.AutonomousBotStatusPersistence", "PendingInventoryOrder"),
            Field("DOL.GS.AutonomousBotVoices", "LeveledAt"),
            Field("DOL.GS.AutonomousBotVoices", "DiedAt"),
            Field("DOL.GS.AutonomousBotVoices", "Deaths"),
            Field("DOL.GS.AutonomousCampCompetition", "_occupancy"),
            Field("DOL.GS.AutonomousCampCompetition", "_spotOccupancy"),
            Field("DOL.GS.AutonomousCampCompetition", "OccupancyCache"),
            Field("DOL.GS.AutonomousCampCompetition", "KnownSpots"),
            Field("DOL.GS.AutonomousCampDangerBench", "Records"),
            Field("DOL.GS.AutonomousCampEmptyMemory", "Entries"),
            Field("DOL.GS.AutonomousCampRouteQuarantine", "Records"),
            Field("DOL.GS.AutonomousChatBroadcast", "Running"),
            Field("DOL.GS.AutonomousChatBudget", "GuildPressure"),
            Field("DOL.GS.AutonomousChatEngagement", "AwaySince"),
            Field("DOL.GS.AutonomousChatExchange", "Lanes"),
            Field("DOL.GS.AutonomousChatFieldTalk", "GuildMemories"),
            Field("DOL.GS.AutonomousChatFiller", "GenericUsed"),
            Field("DOL.GS.AutonomousChatNews", "ToVoice"),
            Field("DOL.GS.AutonomousChatPlaces", "Patterns"),
            Field("DOL.GS.AutonomousDoorRepair", "Claims"),
            Field("DOL.GS.AutonomousDungeonBossGoals", "OwnDrops"),
            Field("DOL.GS.AutonomousDungeonBossGoals", "DropLookups"),
            Field("DOL.GS.AutonomousDungeonPopulationPolicy", "_campRegions"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "Calls"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "Dismissals"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "Answered"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "Members"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "SeenOutOfWorld"),
            Field("DOL.GS.AutonomousEpicSpecialForces", "JoinedCount"),
            Field("DOL.GS.AutonomousGroupDangerRetreat", "Camps"),
            Field("DOL.GS.AutonomousHuntingGround", "States"),
            Field("DOL.GS.AutonomousStableLandingWatch", "Landings"),
            Field("DOL.GS.AutonomousStableLandingWatch", "LandingDeaths"),
            Field("DOL.GS.AutonomousLivingServer", "Watched"),
            Field("DOL.GS.AutonomousLivingServer", "NextBit"),
            Field("DOL.GS.AutonomousLivingServer", "RezOfferedFor"),
            Field("DOL.GS.AutonomousLivingServer", "LastGratsFor"),
            Field("DOL.GS.AutonomousLivingServer", "NextNemesisLine"),
            Field("DOL.GS.AutonomousLivingServer", "AskedAtDeath"),
            Field("DOL.GS.AutonomousLivingServer", "GreetSaidAt"),
            Field("DOL.GS.AutonomousLivingServer", "GreetPendingUntil"),
            Field("DOL.GS.AutonomousLivingServer", "LastGroupedNote"),
            Field("DOL.GS.AutonomousLivingServer", "LastGreeted"),
            Field("DOL.GS.AutonomousLivingServer", "LastGreetOfPlayer"),
            Field("DOL.GS.AutonomousLivingServer", "NextTellTo"),
            Field("DOL.GS.AutonomousLivingServer", "LastTellPair"),
            Field("DOL.GS.AutonomousLivingServer", "LastStartedTell"),
            Field("DOL.GS.AutonomousObjectiveAssignments", "GroupWaits"),
            Field("DOL.GS.AutonomousPetSupport", "PendingCharms"),
            Field("DOL.GS.AutonomousPetSupport", "ActiveSyntheticCharms"),
            Field("DOL.GS.AutonomousPetSupport", "FieldTurrets"),
            Field("DOL.GS.AutonomousPlayerDeeds", "KillTimes"),
            Field("DOL.GS.AutonomousPlayerDeeds", "LastTold"),
            Field("DOL.GS.AutonomousPlayerPilot", "Pilots"),
            Field("DOL.GS.AutonomousPopulationController", "PendingSpawns"),
            Field("DOL.GS.AutonomousPopulationController", "PendingCommands"),
            Field("DOL.GS.AutonomousPopulationController", "OrphanChecked"),
            Field("DOL.GS.AutonomousRamCrews", "RamsByKeep"),
            Field("DOL.GS.AutonomousRealmRaid", "InvitedPlayers"),
            Field("DOL.GS.AutonomousRealmRaid", "PlayerSeatTargets"),
            Field("DOL.GS.AutonomousRealmRaid", "OpenRaidBoxes"),
            Field("DOL.GS.AutonomousRealmRaid", "Raids"),
            Field("DOL.GS.AutonomousRealmRaid", "RecentDestinations"),
            Field("DOL.GS.AutonomousRealmRaid", "HeldRespawns"),
            Field("DOL.GS.AutonomousRealmRaid", "KilledOnTheWay"),
            Field("DOL.GS.AutonomousRealmRaid", "EndedHolds"),
            Field("DOL.GS.AutonomousRealmRaid", "Membership"),
            Field("DOL.GS.AutonomousRealmRaid", "DefenseGroups"),
            Field("DOL.GS.AutonomousRealmRaid", "Released"),
            Field("DOL.GS.AutonomousRealmRaid", "Cooldowns"),
            Field("DOL.GS.AutonomousRealmRaid", "Bosses"),
            Field("DOL.GS.AutonomousRealmRaid", "CompletedLoot"),
            Field("DOL.GS.AutonomousRepeatDeathSpots", "Deaths"),
            Field("DOL.GS.AutonomousFailedGoalMemory", "Owners"),
            Field("DOL.GS.AutonomousRumours", "LastAbout"),
            Field("DOL.GS.AutonomousRvrEventLayer", "BattlegroundBattlegroups"),
            Field("DOL.GS.AutonomousRvrEventLayer", "BattlegroundBattlegroupOfParty"),
            Field("DOL.GS.AutonomousRvrEventLayer", "DefenseAlarms"),
            Field("DOL.GS.AutonomousRvrEventLayer", "KeepCombatPressure"),
            Field("DOL.GS.AutonomousRvrEventLayer", "DefenseWarnings"),
            Field("DOL.GS.AutonomousRvrEventLayer", "CarrierOrigins"),
            Field("DOL.GS.AutonomousRvrEventLayer", "CarrierMountedSeen"),
            Field("DOL.GS.AutonomousRvrEventLayer", "Events"),
            Field("DOL.GS.AutonomousRvrEventLayer", "CarrierEvents"),
            Field("DOL.GS.AutonomousRvrEventLayer", "CarrierTargets"),
            Field("DOL.GS.AutonomousRvrEventLayer", "Cooldowns"),
            Field("DOL.GS.AutonomousRvrEventLayer", "ReleasedForces"),
            Field("DOL.GS.AutonomousRvrEventLayer", "SelectedKeeps"),
            Field("DOL.GS.AutonomousRvrEventLayer", "SelectedRelics"),
            Field("DOL.GS.AutonomousRvrEventLayer", "Campaigns"),
            Field("DOL.GS.AutonomousRvrMusterPost", "Posts"),
            Field("DOL.GS.AutonomousRvrStaging", "AnchorConnections"),
            Field("DOL.GS.AutonomousServiceReachability", "BotSkips"),
            Field("DOL.GS.AutonomousServiceReachability", "FirstFailures"),
            Field("DOL.GS.AutonomousServiceReachability", "SharedSkips"),
            Field("DOL.GS.AutonomousSiegeBuild", "Sieges"),
            Field("DOL.GS.AutonomousSiegeJobs", "Jobs"),
            Field("DOL.GS.AutonomousSiegeResurrection", "Claims"),
            Field("DOL.GS.AutonomousStableRoutePlanner", "NetworkCache"),
            Field("DOL.GS.AutonomousStuckWatchdog", "Observations"),
            Field("DOL.GS.AutonomousStuckWatchdog", "GoalObservations"),
            Field("DOL.GS.AutonomousStuckWatchdog", "LastPositionValidation"),
            Field("DOL.GS.AutonomousStuckWatchdog", "GroupHoldCache"),
            Field("DOL.GS.AutonomousTales", "FiresideQuietSince"),
            Field("DOL.GS.AutonomousTales", "LastFireside"),
            Field("DOL.GS.AutonomousTales", "Firesides"),
            Field("DOL.GS.AutonomousTales", "PlacedLast"),
            Field("DOL.GS.AutonomousTales", "PlacedNextCheck"),
            Field("DOL.GS.AutonomousTales", "PlacedHeard"),
            Field("DOL.GS.AutonomousTetherReset", "StrandedSince"),
            Field("DOL.GS.AutonomousTetherReset", "OutOfTetherSince"),
            Field("DOL.GS.AutonomousTownTeleporters", "ReachedStandOffs"),
            Field("DOL.GS.AutonomousTownTeleporters", "WalkLengths"),
            Field("DOL.GS.AutonomousWorldBotController", "LaneJoinLogs"),
            Field("DOL.GS.AutonomousWorldBotController", "RelicReadySince"),
            Field("DOL.GS.AutonomousWorldBotController", "RvrStealthCamps"),
            Field("DOL.GS.AutonomousWorldBotController", "FailedZonePointUntil"),
            Field("DOL.GS.AutonomousWorldBotController", "_campSafeSpots"),
            Field("DOL.GS.AutonomousWorldBotController", "KeptOrders"),
            Field("DOL.GS.AutonomousZoneRoads", "Routes"),
            Field("DOL.GS.AutonomousZoneRoads", "EndpointMemory"),
            Field("DOL.GS.AutonomousZoneSeams", "MainAreas"),
            Field("DOL.GS.BotLedGroups", "LastBotOffers"),
            Field("DOL.GS.BotLedGroups", "Hosted"),
            Field("DOL.GS.BotLedGroups", "Memories"),
            Field("DOL.GS.BotLedGroups", "LastSeen"),
            Field("DOL.GS.BotMilestoneLife", "Counts"),
            Field("DOL.GS.BotMilestoneLife", "NotBefore"),
            Field("DOL.GS.BotMilestoneLife", "Pending"),
            Field("DOL.GS.BotPeopleTest", "Handlers"),
            Field("DOL.GS.BotPeopleWhy", "Providers"),
            Field("DOL.GS.BotPeopleSummary", "Sources"),
            Field("DOL.GS.BotPlaceKnowledge", "Watches"),
            Field("DOL.GS.BotPlaceKnowledge", "Recent"),
            Field("DOL.GS.BotSayGate", "LastInRoom"),
            Field("DOL.GS.BotSayGate", "LastHeardBy"),
            Field("DOL.GS.BotSayGate", "LastHeardIn"),
            Field("DOL.GS.BotSayGate", "Recent"),
            Field("DOL.GS.BotSkill", "Bots"),
            Field("DOL.GS.BotSkillHands", "Bots"),
            Field("DOL.GS.BotWayFinding", "Travellers"),
            Field("DOL.GS.BotWayFinding", "ForcedWrongRoads"),
            Field("DOL.GS.BotWayFinding", "LastTrips"),
            Field("DOL.GS.BotWayFinding", "Refused"),
            Field("DOL.GS.CharmCreatureRoles", "Cache"),
            Field("DOL.GS.ChatPromises", "RecentRefusals"),
            Field("DOL.GS.EncounterMechanicCalls", "LastTopic"),
            Field("DOL.GS.EncounterMechanicCalls", "LastAny"),
            Field("DOL.GS.EncounterMeleeStations", "Orders"),
            Field("DOL.GS.EncounterMeleeStations", "RingAngles"),
            Field("DOL.GS.EncounterMeleeStations", "PartyOrders"),
            Field("DOL.GS.EpicEncounterTactics", "AddPartyCache"),
            Field("DOL.GS.EpicEncounterTactics", "LastRangedAddsLog"),
            Field("DOL.GS.EpicEncounterTactics", "CourtHeldSince"),
            Field("DOL.GS.EpicEncounterTactics", "SilencerSlots"),
            Field("DOL.GS.EpicEncounterTactics", "LegionSteppedOut"),
            Field("DOL.GS.EpicEncounterTactics", "ExtraDungeonRules"),
            Field("DOL.GS.EpicEncounterTactics", "Errands"),
            Field("DOL.GS.EpicEncounterTactics", "LastGreenKnightTreeLog"),
            Field("DOL.GS.EpicEncounterTactics", "GreenKnightPlans"),
            Field("DOL.GS.EpicEncounterTactics", "GreenKnightActive"),
            Field("DOL.GS.EpicEncounterTactics", "GreenKnightTankTeams"),
            Field("DOL.GS.EpicEncounterTactics", "GreenKnightAreaCasters"),
            Field("DOL.GS.EpicEncounterTactics", "GildasTauntPicks"),
            Field("DOL.GS.EpicEncounterTactics", "SoulPartyCache"),
            Field("DOL.GS.EpicEncounterTactics", "LegionTaunts"),
            Field("DOL.GS.EpicEncounterTactics", "LegionTankHealers"),
            Field("DOL.GS.EpicEncounterTactics", "LastLeechExposureLog"),
            Field("DOL.GS.EpicEncounterTactics", "PostWatch"),
            Field("DOL.GS.EpicEncounterTactics", "PostRings"),
            Field("DOL.GS.EpicEncounterTactics", "SpreadRings"),
            Field("DOL.GS.EpicEncounterTactics", "_bossLookup"),
            Field("DOL.GS.EpicEncounterTactics", "PerchFloors"),
            Field("DOL.GS.EpicEncounterTactics", "BlastCalls"),
            Field("DOL.GS.EpicEncounterTactics", "SpreadTargets"),
            Field("DOL.GS.EpicEncounterTactics", "SpreadCastWatch"),
            Field("DOL.GS.EpicEncounterTactics", "LastTauntLog"),
            Field("DOL.GS.EpicEncounterTactics", "KeptSouls"),
            Field("DOL.GS.EpicEncounterTactics", "KeptSoulHolders"),
            Field("DOL.GS.EpicEncounterTactics", "GuardianCache"),
            Field("DOL.GS.EpicEncounterTactics", "Rescues"),
            Field("DOL.GS.EpicEncounterTactics", "Extra"),
            Field("DOL.GS.EpicEncounterTactics", "SeenHazards"),
            Field("DOL.GS.EpicEncounterTactics", "TauntPicks"),
            Field("DOL.GS.EpicEncounterTactics", "Switches"),
            Field("DOL.GS.EpicEncounterTactics", "Stands"),
            Field("DOL.GS.EpicEncounterTactics", "BreakOffs"),
            Field("DOL.GS.EpicEncounterTactics", "BlastClocks"),
            Field("DOL.GS.EpicEncounterTactics", "BlastRates"),
            Field("DOL.GS.EpicEncounterTactics", "BlastPaths"),
            Field("DOL.GS.EpicEncounterTactics", "BlastSpotCache"),
            Field("DOL.GS.EpicEncounterTactics", "StandOffHolds"),
            Field("DOL.GS.EpicEncounterTactics", "CastWatch"),
            Field("DOL.GS.EpicEncounterTactics", "SentOutOfCast"),
            Field("DOL.GS.EpicEncounterTactics", "HomeWatch"),
            Field("DOL.GS.EpicEncounterTactics", "LeadHomeRepath"),
            Field("DOL.GS.EpicEncounterTactics", "NearbyCache"),
            Field("DOL.GS.EpicEncounterTactics", "LastDecision"),
            Field("DOL.GS.EpicFightLedger", "Rows"),
            Field("DOL.GS.EpicPlaytestSweep", "Lanes"),
            Field("DOL.GS.EpicPlaytestSweep", "Results"),
            Field("DOL.GS.GameWideAnnouncements", "Pending"),
            Field("DOL.GS.GroupEndedLog", "GroupChanged"),
            Field("DOL.GS.BgRallyResponseRows", "Contacts"),
            Field("DOL.GS.PlayerLineOutcomes", "Lines"),
            Field("DOL.GS.PlayerLineOutcomes", "LastDecision"),
            Field("DOL.GS.RaidAttemptRows", "ByRaider"),
            Field("DOL.GS.RaidAttemptRows", "Sequence"),
            Field("DOL.GS.RaidBossMechanics", "Pending"),
            Field("DOL.GS.RaidAttemptRows", "ByBoss"),
            Field("DOL.GS.LoreAtmosphere", "Players"),
            Field("DOL.GS.LoreAtmosphere", "Offers"),
            Field("DOL.GS.LoreAtmosphere", "Forget"),
            Field("DOL.GS.LoreAtmosphere", "LastRealmLine"),
            Field("DOL.GS.LoreBossWatch", "Fights"),
            Field("DOL.GS.LoreBossWatch", "Mechanics"),
            Field("DOL.GS.LoreChat", "RecentlySaid"),
            Field("DOL.GS.NoLiferBuffBots", "States"),
            Field("DOL.GS.NoLiferBuffBots", "LastLineTo"),
            Field("DOL.GS.RaidCureClaims", "Claims"),
            Field("DOL.GS.RaidLearning", "Raids"),
            Field("DOL.GS.RealmEventControls", "Pending"),
            Field("DOL.GS.RealmEventNotices", "Pending"),
            Field("DOL.GS.RealmEventNotices", "Told"),
            Field("DOL.GS.RealmEventNotices", "PendingScreen"),
            Field("DOL.GS.RealmRaidRecord", "EndedBeforeLoad"),
            Field("DOL.GS.RealmWarbandSupport", "_groups"),
            Field("DOL.GS.RealmWarbandSupport", "_leads"),
            Field("DOL.GS.RealmWarbandSupport", "_scopes"),
            Field("DOL.GS.RvrMarchLanes", "Plans"),
            Field("DOL.GS.RvrPlayerRecord", "Notices"),
            Field("DOL.GS.RvrPlayerRecord", "Seats"),
            Field("DOL.GS.RvrRecordBudget", "Windows"),
            Field("DOL.GS.RvrRecordBudget", "Dropped"),
            Field("DOL.GS.RvrDecide", "RuleCounts"),
            Field("DOL.GS.RvrLogBudget", "Windows"),
            Field("DOL.GS.SiegeDefenseBalance", "Results"),
            Field("DOL.GS.SiegeDefenseBalance", "EndedBeforeLoad"),
            Field("DOL.GS.AutonomousBotCrafting", "FinishedTrips"),
            Field("DOL.GS.AutonomousBotCrafting", "Seconds"),
            Field("DOL.GS.BotCrafterTypes", "Stored"),
            Field("DOL.GS.BotGearPipeline", "States"),
            Field("DOL.GS.ItemValue165", "NoStock"),
            Field("DOL.GS.Commands.SnoopManager", "_activeObserver"),
            Field("DOL.GS.Commands.PlayerMobNavigator", "Travels"),
            Field("DOL.GS.Commands.PlayerCompanionGrind", "Sessions"),
            Field("DOL.GS.GroupMgr", "m_groups"),
            Field("DOL.GS.GroupMgr", "m_lfgPlayers"),
            Field("DOL.GS.PacketHandler.Client.v168.WorldInitRequestHandler", "Entering"),
            Field("DOL.GS.BountyMapMarkers", "Targets"),
            Field("DOL.GS.Quests.ClassicQuests", "Shown"),
            Field("DOL.GS.Quests.ClassicQuests", "EventSpawns"),
            Field("DOL.GS.Quests.ClassicQuests", "EventCooldown"),
            Field("DOL.GS.Quests.ClassicQuests", "GiveCooldown"),
            Field("DOL.GS.Quests.ClassicQuests", "Conversations"),
            Field("DOL.GS.Quests.ClassicQuests", "ConversationTicks"),
            Field("DOL.GS.Quests.QuestGuide", "Cursors"),
            Field("DOL.GS.Silencer", "attackers"),
            Field("DOL.AI.Brain.AlbGolestandtBrain", "DragonAdds"),
            Field("DOL.GS.DragonDiagnostics", "NextHealthLog"),
            Field("DOL.AI.Brain.HibCuuldurachBrain", "DragonAdds"),
            Field("DOL.AI.Brain.MidGjalpinulvaBrain", "DragonAdds"),
            Field("DOL.GS.EncounterSignals", "LastMoment"),
            Field("DOL.GS.EncounterSignals", "Live"),
            Field("DOL.AI.Brain.EyesWatchingYouInitBrain", "PlayersInGalla"),
            Field("DOL.GS.Quests.Hibernia.SluaghbinderQuestMapMarkers", "Targets"),
            Field("DOL.GS.ReputationMapMarkers", "Targets"),
            Field("DOL.GS.ShroudedIslesPortals", "LastUse"),
        ];

        private static int _reports;
        private static int _running;

        /// <summary>
        /// Called on every SERVER_MEMORY report (the game loop's five-minute report): starts a census on a thread-pool
        /// thread on the first report and every <see cref="EveryMemoryReports"/>th after it. Never blocks the caller.
        /// </summary>
        public static void OnMemoryReport(GCMemoryInfo info)
        {
            if (!IsDue(Interlocked.Increment(ref _reports))) return;
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                try { RunNow(info); }
                catch (Exception exception) { Log.Warn("SERVER_COLLECTION_CENSUS failed", exception); }
                finally { Volatile.Write(ref _running, 0); }
            }, null);
        }

        /// <summary>Whether the census runs on this SERVER_MEMORY report (1-based): the first, then every sixth. Pure.</summary>
        public static bool IsDue(int report) => report >= 1 && (report - 1) % EveryMemoryReports == 0;

        private static void RunNow(GCMemoryInfo info)
        {
            long started = Stopwatch.GetTimestamp();
            double weakMs = 0;
            var counts = new List<(string Name, long Count)>(Registry.Count);
            foreach (Entry entry in Registry)
            {
                long before = Stopwatch.GetTimestamp();
                counts.Add((entry.Name, entry.Count()));
                if (entry.IsWeak) weakMs += Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            }
            Header header = Header.Now(info);
            header.Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            header.WeakMilliseconds = weakMs;
            foreach (string line in Lines(header, counts))
                Log.Info(line);
        }

        /// <summary>The thread, timer and GC figures of part 1.</summary>
        public sealed class Header
        {
            public int Threads = -1, PoolThreads, Unresolved;
            public long PoolPending, Timers, Gen2Collections;
            public long[] GenerationMegabytes = [-1, -1, -1, -1, -1];
            public double Milliseconds, WeakMilliseconds;

            public static Header Now(GCMemoryInfo info)
            {
                var header = new Header
                {
                    PoolThreads = ThreadPool.ThreadCount,
                    PoolPending = ThreadPool.PendingWorkItemCount,
                    Timers = System.Threading.Timer.ActiveCount,
                    Gen2Collections = GC.CollectionCount(2)
                };
                try
                {
                    using Process process = Process.GetCurrentProcess();
                    header.Threads = process.Threads.Count;
                }
                catch { }
                ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
                for (int i = 0; i < header.GenerationMegabytes.Length; i++)
                    header.GenerationMegabytes[i] = i < generations.Length ? generations[i].SizeAfterBytes / 1048576 : -1;
                return header;
            }
        }

        /// <summary>
        /// The row's lines (pure, for tests): part 1 the header, then counts="name=count,..." filling each line up to
        /// <see cref="MaxLineChars"/> and going on in part 2, 3... A store that couldn't be read is written name=?.
        /// </summary>
        public static List<string> Lines(Header header, IReadOnlyList<(string Name, long Count)> counts, int maxChars = MaxLineChars)
        {
            header.Unresolved = counts.Count(c => c.Count < 0);
            long[] gen = header.GenerationMegabytes;
            string head = FormattableString.Invariant(
                $"collections={counts.Count} unresolved={header.Unresolved} threads={header.Threads} poolThreads={header.PoolThreads} poolPending={header.PoolPending} timers={header.Timers} ") +
                FormattableString.Invariant(
                $"gen0MB={gen[0]} gen1MB={gen[1]} gen2MB={gen[2]} lohMB={gen[3]} pohMB={gen[4]} gen2Collections={header.Gen2Collections} ms={header.Milliseconds:F0} weakMs={header.WeakMilliseconds:F0}");
            var bodies = new List<StringBuilder>();
            // Room for the marker, " part=NN/NN ", the header (part 1 only) and counts="...".
            int Room(int part) => maxChars - Marker.Length - 16 - (part == 0 ? head.Length + 1 : 0) - "counts=\"\"".Length;
            var current = new StringBuilder();
            bodies.Add(current);
            foreach (var (name, count) in counts)
            {
                string pair = count < 0 ? $"{name}=?" : FormattableString.Invariant($"{name}={count}");
                int extra = (current.Length == 0 ? 0 : 1) + pair.Length;
                if (current.Length > 0 && current.Length + extra > Room(bodies.Count - 1))
                {
                    current = new StringBuilder();
                    bodies.Add(current);
                    extra = pair.Length;
                }
                if (current.Length > 0) current.Append(',');
                current.Append(pair);
            }
            var lines = new List<string>(bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                // One row every 30 minutes, in numbered parts, of the sizes of the server's long-lived static collections and caches with its thread, timer and GC generation figures (#731).
                lines.Add($"SERVER_COLLECTION_CENSUS part={i + 1}/{bodies.Count} " + (i == 0 ? head + " " : "") + $"counts=\"{bodies[i]}\"");
            }
            return lines;
        }
    }
}
