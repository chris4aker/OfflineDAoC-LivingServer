using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.Commands;
using DOL.GS.GameEvents;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Loads persistent launcher-created characters as real world actors according
/// to the configured startup ramp. It never invents offline progress and never
/// creates characters on its own.
/// </summary>
public static class AutonomousPopulationController
{
    public const string TeleportToBotCommandType = "TeleportToBot";
    public const int MaximumSpawnEnqueuePerPoll = 16;
    private const float MaximumStartupGroundDelta = 96f;
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly ConcurrentDictionary<long, byte> PendingSpawns = new();
    private static readonly ConcurrentDictionary<long, byte> PendingCommands = new();
    private static System.Threading.Timer _timer;
    private static DateTime _startedUtc;
    private static DateTime _nextRosterRefreshUtc;
    private static DateTime _nextCommandPollUtc;
    private static DateTime _nextOrphanRepairUtc;
    // Retired bots whose command history the orphan repair has already looked at this server run. The repair re-read
    // every retired bot's commands once a minute (thousands after test-roster rollbacks: the top database allocator
    // in run 22's allocation trace); a retired bot's history only gains a pending delete, which the command poll runs.
    private static readonly HashSet<long> OrphanChecked = new();
    private static volatile OfflineWorldBotRecord[] _cachedRoster = Array.Empty<OfflineWorldBotRecord>();
    private static bool _cachedEnabled;
    private static int _cachedRampMinutes = 15;
    private static int _polling;
    private static int _loginGeneration;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        Interlocked.Increment(ref _loginGeneration);
        _startedUtc = DateTime.UtcNow;
        AutonomousBotSchedule.NoteServerStarted(_startedUtc);
        OfflineWorldBotRecord[] previouslyOnline = DOLDB<OfflineWorldBotRecord>
            .SelectObjects(DB.Column("IsOnline").IsEqualTo(true))
            .ToArray();
        foreach (OfflineWorldBotRecord record in previouslyOnline)
        {
            record.IsOnline = false;
            record.Activity = record.IsRetired ? "Deletion requested" : "Queued for staggered login";
            record.Dirty = true;
        }
        if (previouslyOnline.Length > 0)
        {
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                GameServer.Database.SaveObject(previouslyOnline.Cast<DataObject>());
        }
        RefreshControlPlane(force: true);
        _nextCommandPollUtc = DateTime.MinValue;
        _nextOrphanRepairUtc = DateTime.MinValue;
        lock (OrphanChecked) OrphanChecked.Clear();
        _timer = new System.Threading.Timer(Poll, null, 500, 1000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        Interlocked.Increment(ref _loginGeneration);
        _timer?.Dispose();
        _timer = null;
        // Persist the coalesced launcher/status batch here. Inventories are
        // event-driven and included only when loot, equipment, buying, selling,
        // or training actually changed them; there is no shutdown-wide rewrite.
        AutonomousBotStatusPersistence.FlushAll();
        PendingSpawns.Clear();
        PendingCommands.Clear();
        _cachedRoster = Array.Empty<OfflineWorldBotRecord>();
    }

    private static void Poll(object state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            RefreshControlPlane(force: false);
            if (nowUtc >= _nextCommandPollUtc)
            {
                _nextCommandPollUtc = nowUtc.AddSeconds(3);
                bool repairOrphans = nowUtc >= _nextOrphanRepairUtc;
                if (repairOrphans)
                    _nextOrphanRepairUtc = nowUtc.AddMinutes(10);
                QueueOwnerCommands(repairOrphans);
            }

            OfflineWorldBotRecord[] roster = _cachedRoster;
            int desired = AutonomousPopulationRamp.DesiredActiveCount(_cachedEnabled, roster.Length, _cachedRampMinutes, nowUtc - _startedUtc);
            int online = AutonomousBotRegistry.Count;
            // Living Server schedules (off by default): only bots scheduled online are logged in, and the
            // count is of those, so bots still finishing a fight before logging out don't block the rest.
            bool scheduled = AutonomousBotSchedule.Active;
            if (scheduled)
            {
                OfflineWorldBotRecord[] wanted = roster.Where(record => AutonomousBotSchedule.WantsOnline(record, nowUtc)).ToArray();
                desired = Math.Min(desired, wanted.Length);
                online = wanted.Count(record => AutonomousBotRegistry.Contains(record.BotId));
            }
            int missing = desired - online - PendingSpawns.Count;
            // Scheduled-off bots still finishing something (often a group) stay online. For their first 15 minutes
            // they count, so logins wait for them (10% slack); after that they no longer hold the schedule back.
            if (scheduled)
            {
                missing = Math.Min(missing, desired + desired / 10 - online - AutonomousBotSchedule.RecentOverstays - PendingSpawns.Count);
                // Hard ceiling: never more than a quarter above the schedule's number in total.
                missing = Math.Min(missing, desired + desired / 4 - AutonomousBotRegistry.Count - PendingSpawns.Count);
            }
            _populationSatisfied = missing <= 0;
            if (missing <= 0)
                return;

            var realmLoad = new Dictionary<eRealm, int>(AutonomousBotRegistry.CountByRealm());
            foreach (OfflineWorldBotRecord pending in roster.Where(record => PendingSpawns.ContainsKey(record.BotId)))
            {
                eRealm realm = (eRealm)pending.Realm;
                realmLoad[realm] = realmLoad.TryGetValue(realm, out int count) ? count + 1 : 1;
            }

            List<OfflineWorldBotRecord> available = roster
                .Where(record => !record.IsOnline && !AutonomousBotRegistry.Contains(record.BotId) && !PendingSpawns.ContainsKey(record.BotId))
                .Where(record => !scheduled || AutonomousBotSchedule.WantsOnline(record, nowUtc))
                // A rage quitter stays away even with schedules off.
                .Where(record => !AutonomousBotSchedule.RageQuitting(record.BotId, nowUtc))
                .ToList();

            // Ten thousand actors require about eleven scheduled logins per
            // second to reach 33% at five minutes and 100% at fifteen. Keep
            // every pulse bounded while retaining modest catch-up headroom.
            int enqueueCount = Math.Min(Math.Min(missing, available.Count), MaximumSpawnEnqueuePerPoll);
            for (int i = 0; i < enqueueCount; i++)
            {
                long? nextId = AutonomousRealmLoginBalancer.SelectNext(
                    available.Select(record => new AutonomousRealmLoginBalancer.Candidate(record.BotId, (eRealm)record.Realm)),
                    realmLoad);
                OfflineWorldBotRecord next = nextId.HasValue
                    ? available.FirstOrDefault(record => record.BotId == nextId.Value)
                    : null;
                if (next == null)
                    break;
                available.Remove(next);
                if (!PendingSpawns.TryAdd(next.BotId, 0))
                    continue;

                eRealm realm = (eRealm)next.Realm;
                realmLoad[realm] = realmLoad.TryGetValue(realm, out int count) ? count + 1 : 1;
                try
                {
                    // Fetch the exact real inventory on this existing background
                    // poller. Applying slots/bonuses and actor creation still belong
                    // to the game loop; it must never spin waiting for SQLite here.
                    int generation = Volatile.Read(ref _loginGeneration);
                    string ownerId = AutonomousBotEconomy.GetOwnerId(next.BotId);
                    var inventory = new BotInventory(ownerId);
                    var items = inventory.SelectPersonalItems(ownerId);
                    if (generation != Volatile.Read(ref _loginGeneration) || _timer == null)
                    {
                        PendingSpawns.TryRemove(next.BotId, out _);
                        return;
                    }
                    NpcService.Instance.Post(SpawnOnGameLoop, new PreparedSpawn(next.BotId, items, generation));
                }
                catch
                {
                    PendingSpawns.TryRemove(next.BotId, out _);
                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error("Autonomous population poll failed", exception);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private sealed record PreparedSpawn(long BotId, System.Collections.IList Inventory, int Generation);

    private static void SpawnOnGameLoop(PreparedSpawn prepared)
    {
        long botId = prepared.BotId;
        try
        {
            if (prepared.Generation != Volatile.Read(ref _loginGeneration) || _timer == null) return;
            OfflineWorldBotRecord record = _cachedRoster.FirstOrDefault(candidate => candidate.BotId == botId);
            if (record == null || record.IsRetired || record.IsOnline || AutonomousBotRegistry.Contains(botId))
                return;

            GameBot bot = new(record, prepared.Inventory);
            if (IsFreshUnplacedLevelOneRecord(record))
            {
                if (!TryPlaceFreshBotAtAuthoritativeStart(bot, record))
                    PlaceAtCapitalFallback(bot, record);
            }
            else if (record.RegionId == 0)
            {
                // Preserve legacy handling for existing records with no usable
                // stored position. Recovery and bind paths intentionally remain
                // capital-based and are not part of new-character placement.
                PlaceAtCapitalFallback(bot, record);
            }

            // Owner 2026-10-29: after a restart 16 to 29 solo gamebots a time died within minutes in Darkness Falls, where they were
            // saved: they came back standing in the middle of a pack, no camp, no party (13-23 monsters around, 3-5 attacking).
            // A bot saved inside the dungeon returns at its realm's entrance of Darkness Falls, inside the dungeon.
            if (record.RegionId == AutonomousDarknessFallsPolicy.RegionId &&
                AutonomousDarknessFallsNavigation.TryGetRealmEntrance((eRealm)record.Realm, out Vector3 dungeonEntrance))
            {
                Log.Info($"AUTONOMOUS_DF_LOGIN_AT_ENTRANCE bot={record.Name} id={record.BotId} realm={(eRealm)record.Realm} " +
                         $"saved={record.X},{record.Y},{record.Z} entrance={(int)dungeonEntrance.X},{(int)dungeonEntrance.Y},{(int)dungeonEntrance.Z}");
                bot.CurrentRegionID = AutonomousDarknessFallsPolicy.RegionId;
                bot.X = (int)dungeonEntrance.X;
                bot.Y = (int)dungeonEntrance.Y;
                bot.Z = (int)dungeonEntrance.Z;
            }

            // GameNPC keeps a separate interpolated movement position. Before
            // AddToWorld it still contains the constructor default (0,0,0),
            // even after the authoritative saved/start coordinates were set.
            // Synchronize it before validation so a valid Classic/SI start is
            // not mistaken for an invalid origin and replaced by a capital.
            bot.SynchronizePositionForLoginValidation();
            AutonomousStuckWatchdog.RepairInvalidLoginPosition(bot);
            if (LogsInBesideABoss(record))
            {
                // Morning 2026-10-19: level 50s saved inside the Hall of the Corrupt logged in beside Aidon the
                // Archwizard and his illusions and died within seconds, 41 times in 41 minutes. A player who logged
                // out there would recall first; the bot starts in its capital instead.
                Log.Info($"AUTONOMOUS_LOGIN_MOVED_FROM_BOSS bot=\"{record.Name}\" region={record.RegionId} at={record.X},{record.Y},{record.Z}");
                PlaceAtCapitalFallback(bot, record);
                bot.SynchronizePositionForLoginValidation();
            }
            // A beggar (bot_living_beggars) always logs in in its own capital.
            if (AutonomousBeggars.PlaceAtLogin(bot, record))
                bot.SynchronizePositionForLoginValidation();
            if (string.IsNullOrWhiteSpace(record.LastSavedUtc) && record.Experience == 0)
            {
                bot.Health = bot.MaxHealth;
                bot.Mana = bot.MaxMana;
                bot.Endurance = bot.MaxEndurance;
            }

            if (!bot.AddToWorld())
            {
                record.IsOnline = false;
                record.Activity = "Login attempt failed; waiting to retry";
                record.LastUpdateUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                AutonomousBotStatusPersistence.Queue(bot);
                return;
            }

            record.IsOnline = true;
            record.Activity = "Choosing first live goal";
            record.CurrentGoal = "Find a reachable level-appropriate XP camp";
            record.ObjectiveProgress = "Entered world through staggered login queue";
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.QueueLogin(bot);
            AutonomousBotSchedule.OnLoggedIn(bot);
            Log.Info($"Autonomous character {record.Name} entered the live world from the staggered queue.");
        }
        catch (Exception exception)
        {
            Log.Error($"Could not load autonomous bot {botId}", exception);
        }
        finally
        {
            PendingSpawns.TryRemove(botId, out _);
        }
    }

    /// <summary>Only a never-saved, zero-position level-one record may enter fresh-start placement.</summary>
    public static bool IsFreshUnplacedLevelOneRecord(OfflineWorldBotRecord record) =>
        record != null && record.Level == 1 && record.Experience == 0 && record.RealmPoints == 0 &&
        record.RegionId == 0 && string.IsNullOrWhiteSpace(record.LastSavedUtc);

    /// <summary>How close to a boss (an epic or a scripted named monster with its own tether) a saved spot may be.</summary>
    public const int LoginBossClearance = 2_500;

    private static bool LogsInBesideABoss(OfflineWorldBotRecord record)
    {
        Region region = record.RegionId == 0 ? null : WorldMgr.GetRegion((ushort)record.RegionId);
        if (region == null)
            return false;
        var npcs = new List<GameNPC>();
        region.GetInRadius(new Point3D(record.X, record.Y, record.Z), eGameObjectType.NPC, LoginBossClearance, npcs);
        return npcs.Any(npc => npc.IsAlive && npc.Realm == eRealm.None && AutonomousTetherReset.HasEncounterTether(npc));
    }

    private static void PlaceAtCapitalFallback(GameBot bot, OfflineWorldBotRecord record)
    {
        AutonomousStuckWatchdog.CapitalLocation capital = AutonomousStuckWatchdog.SafeCapitalFor((eRealm)record.Realm);
        bot.CurrentRegionID = capital.RegionId;
        bot.X = capital.X;
        bot.Y = capital.Y;
        bot.Z = capital.Z;
    }

    /// <summary>
    /// New level-one autonomous actors use the server's normal StartupLocation
    /// data, after validating the live map and navmesh. Existing records never
    /// enter this path, so watchdog recovery stays capital-only.
    /// </summary>
    private static bool TryPlaceFreshBotAtAuthoritativeStart(GameBot bot, OfflineWorldBotRecord record)
    {
        StartupLocation[] candidates = StartupLocations
            .GetClassicSiLocationsForAutonomous((eRealm)record.Realm, record.RaceId, record.ClassId)
            .ToArray();
        if (candidates.Length == 0)
            return false;

        var valid = new List<(StartupLocation Location, Vector3 Position)>();
        foreach (StartupLocation candidate in candidates)
        {
            Region region = WorldMgr.GetRegion((ushort)candidate.Region);
            if (region == null || region.IsCapitalCity)
                continue;

            Zone zone = region.GetZone(candidate.XPos, candidate.YPos);
            if (zone == null)
                continue;

            Vector3 snappedPosition = new(candidate.XPos, candidate.YPos, candidate.ZPos);
            if (PathfindingProvider.Instance.IsAvailable && PathfindingProvider.Instance.HasNavmesh(zone) &&
                !PathfindingProvider.Instance.TrySnapToMesh(zone, ref snappedPosition, 768f))
            {
                continue;
            }
            if (Math.Abs(snappedPosition.Z - candidate.ZPos) > MaximumStartupGroundDelta)
                continue;
            // A point can be on a valid polygon yet isolated on a prop/ledge.
            // Only fresh-character placement is filtered; no live bot is moved
            // and no collision filter is relaxed.
            if (PathfindingProvider.Instance.IsAvailable && PathfindingProvider.Instance.HasNavmesh(zone) &&
                !AutonomousRendezvousNavigation.HasLocalExit(PathfindingProvider.Instance, zone, snappedPosition))
                continue;

            valid.Add((candidate, snappedPosition));
        }

        if (valid.Count == 0)
            return false;

        // The only fresh-character placement draw: all validated, applicable
        // Classic+SI start locations are equally likely. Capital is reached
        // only through the explicit no-valid-start fallback above or watchdog
        // recovery, never through a location preference here.
        StartupLocation chosenLocation = StartupLocations.ChooseUniformAutonomousLocation(
            valid.Select(candidate => candidate.Location), Random.Shared);
        (StartupLocation location, Vector3 chosenPosition) = valid.First(candidate => ReferenceEquals(candidate.Location, chosenLocation));
        bot.CurrentRegionID = (ushort)location.Region;
        bot.X = (int)Math.Round(chosenPosition.X);
        bot.Y = (int)Math.Round(chosenPosition.Y);
        bot.Z = (int)Math.Round(chosenPosition.Z);
        bot.Heading = (ushort)Math.Clamp(location.Heading, 0, ushort.MaxValue);
        return true;
    }

    private static void RefreshControlPlane(bool force)
    {
        DateTime nowUtc = DateTime.UtcNow;
        if (!force && nowUtc < _nextRosterRefreshUtc)
            return;
        // Refresh quickly while actors are still being admitted. Once every
        // requested actor is live, the full 6,000-row roster read only needs to
        // notice launcher-side roster edits, so a slower cadence is enough.
        _nextRosterRefreshUtc = nowUtc.AddSeconds(force ? 10 : _populationSatisfied ? 30 : 8);

        // Pure reads: SQLite WAL readers never block the writer, so this no
        // longer holds the shared write lock that game-loop transactions wait on.
        _cachedEnabled = ReadBool("population_enabled", false);
        _cachedRampMinutes = ReadInt("startup_ramp_minutes", 15);
        _cachedRoster = DOLDB<OfflineWorldBotRecord>
            .SelectObjects(DB.Column("IsRetired").IsEqualTo(false))
            .OrderBy(record => record.BotId)
            .ToArray();
        // A gamebot taken off the roster (launcher removal, retirement, test-roster rollback) leaves its guild.
        long[] rosterIds = _cachedRoster.Select(record => record.BotId).ToArray();
        NpcService.Instance.Post<long[]>(ids => AutonomousBotGuilds.Reconcile(ids), rosterIds);
    }

    private static volatile bool _populationSatisfied;

    /// <summary>The non-retired roster as last read by the control plane (read-only; empty before the first read).</summary>
    internal static OfflineWorldBotRecord[] CachedRoster => _cachedRoster;

    private static void QueueOwnerCommands(bool repairOrphans)
    {
        bool lockTaken = false;
        try
        {
            // A status flush or server-wide save owns SQLite. A population poll
            // is optional and must never block the game loop or shutdown behind
            // SQLite's busy timeout.
            Monitor.TryEnter(AutonomousBotStatusPersistence.DatabaseWriteLock, 0, ref lockTaken);
            if (!lockTaken)
                return;

        // Repair the narrow orphan case where an older server build marked a
        // delete command Completed without proving the durable row disappeared.
        // Only a bot whose latest command is Completed is retried; Failed
        // commands are left visible for diagnosis instead of spinning forever.
        foreach (OfflineWorldBotRecord retired in repairOrphans
                     ? DOLDB<OfflineWorldBotRecord>.SelectObjects(DB.Column("IsRetired").IsEqualTo(true))
                     : Array.Empty<OfflineWorldBotRecord>())
        {
            lock (OrphanChecked)
                if (!OrphanChecked.Add(retired.BotId))
                    continue;
            OfflineBotCommandRecord latest = DOLDB<OfflineBotCommandRecord>
                .SelectObjects(DB.Column("BotId").IsEqualTo(retired.BotId))
                .OrderByDescending(entry => entry.CommandId)
                .FirstOrDefault();
            if (latest != null && string.Equals(latest.CommandType, "Delete", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(latest.State, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                GameServer.Database.AddObject(new OfflineBotCommandRecord
                {
                    BotId = retired.BotId,
                    CommandType = "Delete",
                    State = "Pending",
                    RequestedUtc = DateTime.UtcNow.ToString("O"),
                    CompletedUtc = string.Empty,
                    Error = "Retrying an orphaned completed deletion",
                });
            }
        }

        foreach (OfflineBotCommandRecord command in DOLDB<OfflineBotCommandRecord>
                     .SelectObjects(DB.Column("State").IsEqualTo("Pending"))
                     .OrderBy(command => command.CommandId))
        {
            if (!PendingCommands.TryAdd(command.CommandId, 0))
                continue;
            NpcService.Instance.Post(ProcessOwnerCommandOnGameLoop, command.CommandId);
        }
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
        }
    }

    private static void ProcessOwnerCommandOnGameLoop(long commandId)
    {
        OfflineBotCommandRecord command = null;
        try
        {
            command = DOLDB<OfflineBotCommandRecord>.SelectObject(DB.Column("CommandId").IsEqualTo(commandId));
            if (command == null || !string.Equals(command.State, "Pending", StringComparison.OrdinalIgnoreCase))
                return;

            command.State = "Processing";
            command.Dirty = true;
            GameServer.Database.SaveObject(command);
            if (string.Equals(command.CommandType, TeleportToBotCommandType, StringComparison.OrdinalIgnoreCase))
            {
                TeleportOwnerToBot(command);
            }
            else if (string.Equals(command.CommandType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                DeleteBot(command.BotId);
            }
            else
            {
                throw new InvalidOperationException($"Unknown autonomous bot command: {command.CommandType}");
            }

            command.State = "Completed";
            command.CompletedUtc = DateTime.UtcNow.ToString("O");
            command.Error = string.Empty;
            command.Dirty = true;
            GameServer.Database.SaveObject(command);
        }
        catch (Exception exception)
        {
            Log.Error($"Autonomous owner command {commandId} failed", exception);
            if (command != null)
            {
                command.State = "Failed";
                command.CompletedUtc = DateTime.UtcNow.ToString("O");
                command.Error = exception.Message;
                command.Dirty = true;
                GameServer.Database.SaveObject(command);
            }
        }
        finally
        {
            PendingCommands.TryRemove(commandId, out _);
        }
    }

    private static void TeleportOwnerToBot(OfflineBotCommandRecord command)
    {
        if (string.IsNullOrWhiteSpace(command.RequestedByAccount))
            throw new InvalidOperationException("The launcher teleport request did not identify its local account.");

        GameClient client = ClientService.Instance.GetClientFromAccountName(command.RequestedByAccount.Trim());
        if (client?.ClientState != GameClient.eClientState.Playing ||
            client.Player?.ObjectState != GameObject.eObjectState.Active)
            throw new InvalidOperationException($"Account '{command.RequestedByAccount}' has no character currently in the game.");

        if (!AutonomousBotRegistry.TryGet(command.BotId, out GameBot bot))
            throw new InvalidOperationException($"Playerbot {command.BotId} is no longer online.");

        if (!PlayerBotTeleport.TryTeleport(client.Player, bot, out string message))
            throw new InvalidOperationException(message);

        client.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        Log.Info($"Launcher teleported account {command.RequestedByAccount} to autonomous character {command.BotId} ({bot.Name}).");
    }

    private static void DeleteBot(long botId)
    {
        AutonomousBotGuilds.Remove(botId, null);
        OfflineWorldBotRecord record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));
        if (AutonomousBotRegistry.TryGet(botId, out GameBot liveBot))
            liveBot.Delete();

        string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
        DbInventoryItem[] items = DOLDB<DbInventoryItem>.SelectObjects(DB.Column("OwnerID").IsEqualTo(ownerId)).ToArray();
        foreach (DbInventoryItem item in items)
        {
            if (RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
                MarketCache.RemoveItem(item);
        }
        if (items.Length > 0)
        {
            foreach (DbInventoryItem item in items)
                item.AllowDelete = true;
            if (!GameServer.Database.DeleteObject(items))
                throw new InvalidOperationException($"Could not delete inventory owned by autonomous character {botId}");
        }
        if (record != null)
        {
            record.AllowDelete = true;
            if (!GameServer.Database.DeleteObject(record))
                throw new InvalidOperationException($"Could not delete autonomous character record {botId}");
            if (DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId)) != null)
                throw new InvalidOperationException($"Autonomous character record {botId} still exists after deletion");
        }

        Log.Warn($"Owner permanently deleted autonomous character {botId} after safe world removal.");
    }

    private static int ReadInt(string key, int fallback) =>
        int.TryParse(ReadProperty(key), out int value) ? value : fallback;

    private static bool ReadBool(string key, bool fallback) =>
        bool.TryParse(ReadProperty(key), out bool value) ? value : fallback;

    private static string ReadProperty(string key) =>
        DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(key))?.Value;
}

[DataTable(TableName = "offline_bot_commands")]
public sealed class OfflineBotCommandRecord : DataObject
{
    [PrimaryKey(AutoIncrement = true)] public long CommandId { get; set; }
    [DataElement(AllowDbNull = false)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public string CommandType { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string RequestedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string State { get; set; } = "Pending";
    [DataElement(AllowDbNull = true)] public string RequestedByAccount { get; set; }
    [DataElement(AllowDbNull = true)] public string CompletedUtc { get; set; }
    [DataElement(AllowDbNull = true)] public string Error { get; set; }
}
