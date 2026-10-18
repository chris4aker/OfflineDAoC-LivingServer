using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Marks a guild as run by autonomous gamebots.</summary>
[DataTable(TableName = "offline_bot_guilds")]
public sealed class OfflineBotGuildRecord : DataObject
{
    [DataElement(AllowDbNull = false, Unique = true, Varchar = 64)] public string GuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 32)] public string Charter { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Ordinal { get; set; }
    [DataElement(AllowDbNull = false)] public long FounderBotId { get; set; }
    [DataElement(AllowDbNull = false)] public string CreatedUtc { get; set; } = string.Empty;
}

/// <summary>One row per guilded autonomous gamebot, in a bot guild or a player's guild.</summary>
[DataTable(TableName = "offline_bot_guild_members")]
public sealed class OfflineBotGuildMemberRecord : DataObject
{
    /// <summary>A copy for a detached save: the copy is written while the live row may keep changing.</summary>
    public OfflineBotGuildMemberRecord CopyForSave() => (OfflineBotGuildMemberRecord)MemberwiseClone();

    [PrimaryKey] public long BotId { get; set; }
    [DataElement(AllowDbNull = false, Index = true, Varchar = 64)] public string GuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RankLevel { get; set; } = AutonomousBotGuildPlanner.LowestRank;
    [DataElement(AllowDbNull = false)] public string JoinedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Autonomous gamebots in guilds (docs/BOT-GUILDS.md). Forms realm bot guilds, binds members as
/// they log in, lists them in the guild window and handles /ginvite and /gc remove on bots.
/// Writes only its own two tables plus new Guild/GuildRank rows, never the bots' own records.
/// </summary>
public static class AutonomousBotGuilds
{
    public sealed record Membership(long BotId, string GuildId, int RankLevel);

    public sealed record BotGuildInfo(string GuildId, eRealm Realm, BotGuildCharter Charter, int Ordinal);

    public readonly record struct BotSummary(string Name, int Level, int ClassId, eRealm Realm);

    public const int FirstReconcileDelayMs = 90_000;
    public const int ReconcileIntervalMs = 600_000;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly object Sync = new();
    private static readonly object BindSync = new();
    private static readonly ConcurrentDictionary<long, Membership> Members = new();
    private static readonly ConcurrentDictionary<string, BotGuildInfo> BotGuilds = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<long, BotSummary> Summaries = new();
    private static readonly Dictionary<long, OfflineBotGuildMemberRecord> MemberRows = new();
    private static readonly ConcurrentDictionary<(eRealm Realm, int Ordinal), byte> PendingCreations = new();
    private static Timer _timer;
    private static int _reconciling;
    private static int _followUpRequested;

    /// <summary>Delay of the pass that fills guilds founded by the previous pass.</summary>
    public const int FollowUpReconcileDelayMs = 30_000;

    public static bool Enabled => Properties.BOT_GUILDS_ENABLED;

    public static bool IsBotGuild(Guild guild) => guild != null && BotGuilds.ContainsKey(guild.GuildID);

    public static bool TryGetBotGuild(string guildId, out BotGuildInfo info) =>
        BotGuilds.TryGetValue(guildId ?? string.Empty, out info);

    public static bool TryGetMembership(long botId, out Membership membership) => Members.TryGetValue(botId, out membership);

    /// <summary>0 when the candidate is in the leader's guild (bot guilds on), else 1. Used to order group candidates.</summary>
    public static int GuildmateRank(GameBot leader, GameBot candidate) =>
        GuildmateRank(Enabled, leader?.Guild?.GuildID, candidate?.Guild?.GuildID);

    public static int GuildmateRank(bool enabled, string leaderGuildId, string candidateGuildId) =>
        enabled && !string.IsNullOrEmpty(leaderGuildId) && string.Equals(leaderGuildId, candidateGuildId, StringComparison.Ordinal) ? 0 : 1;

    #region Server lifecycle

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            LoadState();
        }
        catch (Exception exception)
        {
            Log.Error("Bot guilds: could not load saved guilds and memberships", exception);
        }
        _timer = new Timer(OnTimer, null, FirstReconcileDelayMs, ReconcileIntervalMs);
        _flushTimer = new Timer(_ => FlushWrites(), null, FlushIntervalMs, FlushIntervalMs);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        _flushTimer?.Dispose();
        _flushTimer = null;
        FlushWrites(wait: true);
    }

    #region Membership writes (batched, off the game loop; audit 2026-10-16)

    public const int FlushIntervalMs = 5_000;
    private const int WriteBatch = 16;
    private static Timer _flushTimer;
    // Rows to insert or update, and rows to delete, written by FlushWrites in batches under the shared
    // database write lock, as the bot status writer does. Callers of SetMembership/DeleteMembership hold Sync.
    private static readonly Dictionary<long, OfflineBotGuildMemberRecord> PendingWrites = new();
    private static readonly List<OfflineBotGuildMemberRecord> PendingDeletes = new();
    private static int _flushing;

    /// <summary>Writes queued membership changes. A failed batch stays queued and is tried again.</summary>
    public static void FlushWrites(bool wait = false)
    {
        while (Interlocked.Exchange(ref _flushing, 1) != 0)
        {
            if (!wait)
                return;
            Thread.Sleep(20);
        }
        try
        {
            // Copies are written, not the live rows, so a rank change made while a save runs is never marked
            // as saved: the live row only takes the copy's state as its baseline (review 2026-10-16).
            (OfflineBotGuildMemberRecord Live, OfflineBotGuildMemberRecord Copy)[] writes;
            OfflineBotGuildMemberRecord[] deletes;
            lock (Sync)
            {
                writes = PendingWrites.Values.Select(row => (row, row.CopyForSave())).ToArray();
                foreach (var write in writes)
                    write.Item2.Dirty = true;
                deletes = PendingDeletes.ToArray();
                PendingWrites.Clear();
                PendingDeletes.Clear();
            }
            if (writes.Length == 0 && deletes.Length == 0)
                return;
            var failedWrites = new List<OfflineBotGuildMemberRecord>();
            var failedDeletes = new List<OfflineBotGuildMemberRecord>();
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                foreach (var chunk in writes.Where(write => !write.Copy.IsPersisted).Chunk(WriteBatch))
                    if (!TryWrite(() => GameServer.Database.AddObject(chunk.Select(write => (DataObject)write.Copy))))
                        failedWrites.AddRange(chunk.Where(write => !write.Copy.IsPersisted).Select(write => write.Live));
                foreach (var chunk in writes.Where(write => write.Copy.IsPersisted).Chunk(WriteBatch))
                    if (!TryWrite(() => GameServer.Database.SaveObject(chunk.Select(write => (DataObject)write.Copy))))
                        failedWrites.AddRange(chunk.Where(write => write.Copy.Dirty).Select(write => write.Live));
                foreach (OfflineBotGuildMemberRecord[] chunk in deletes.Where(row => row.IsPersisted).Chunk(WriteBatch))
                    if (!TryWrite(() => GameServer.Database.DeleteObject(chunk.Cast<DataObject>())))
                        failedDeletes.AddRange(chunk.Where(row => row.IsPersisted));
            }
            lock (Sync)
            {
                foreach (var write in writes)
                {
                    if (!write.Copy.IsPersisted || write.Copy.Dirty)
                        continue;
                    write.Live.IsPersisted = true;
                    try
                    {
                        write.Live.AcceptPersistenceBaseline(write.Copy);
                    }
                    catch (ArgumentException)
                    {
                        write.Live.Dirty = true;   // no baseline: the next save writes every column
                    }
                }
                // A row changed again since the snapshot is already queued in its newer state.
                foreach (OfflineBotGuildMemberRecord row in failedWrites)
                    if (MemberRows.TryGetValue(row.BotId, out var current) && ReferenceEquals(current, row))
                        PendingWrites.TryAdd(row.BotId, row);
                PendingDeletes.AddRange(failedDeletes);
            }
            if (failedWrites.Count > 0 || failedDeletes.Count > 0)
                Log.Warn($"Bot guilds: {failedWrites.Count} membership write(s) and {failedDeletes.Count} delete(s) failed; retrying");
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }

    private static bool TryWrite(Func<bool> write)
    {
        try
        {
            return write();
        }
        catch (Exception exception)
        {
            Log.Error("Bot guilds: membership write failed", exception);
            return false;
        }
    }

    #endregion

    private static void LoadState()
    {
        lock (Sync)
        {
            BotGuilds.Clear();
            Members.Clear();
            MemberRows.Clear();
            foreach (OfflineBotGuildRecord row in GameServer.Database.SelectAllObjects<OfflineBotGuildRecord>())
            {
                if (GuildMgr.GetGuildByGuildID(row.GuildId) == null)
                    continue;
                BotGuildCharter charter = Enum.TryParse(row.Charter, out BotGuildCharter parsed) ? parsed : BotGuildCharter.Leveling;
                BotGuilds[row.GuildId] = new BotGuildInfo(row.GuildId, (eRealm)row.Realm, charter, row.Ordinal);
                ApplyStartupRewrites(GuildMgr.GetGuildByGuildID(row.GuildId), charter, Enabled);
            }
            foreach (OfflineBotGuildMemberRecord row in GameServer.Database.SelectAllObjects<OfflineBotGuildMemberRecord>())
            {
                MemberRows[row.BotId] = row;
                Members[row.BotId] = new Membership(row.BotId, row.GuildId, row.RankLevel);
            }
        }
        if (Log.IsInfoEnabled)
            Log.Info($"Bot guilds: loaded {BotGuilds.Count} bot guilds and {Members.Count} memberships (enabled: {Enabled}).");
    }

    /// <summary>
    /// The startup rewrites of a saved bot guild: its member ranks' chat rights and, for guilds founded
    /// before dues existed, its charter's dues once. Only while bot guilds are on: with them off (or
    /// after a rollback) the saved guilds are left exactly as they are (audit 2026-10-17). True when applied.
    /// </summary>
    public static bool ApplyStartupRewrites(Guild guild, BotGuildCharter charter, bool enabled)
    {
        if (!enabled || guild == null)
            return false;
        ApplyMemberRankRights(guild);
        if (!guild.IsGuildDuesOn() && AutonomousBotGuildEconomy.DefaultDuesPercent(charter) > 0)
            AutonomousBotGuildEconomy.SetCharterDues(guild, charter);
        return true;
    }

    private static void OnTimer(object state)
    {
        if (!Enabled || Interlocked.Exchange(ref _reconciling, 1) != 0)
            return;
        try
        {
            Reconcile(AutonomousPopulationController.CachedRoster);
        }
        catch (Exception exception)
        {
            Log.Error("Bot guilds: reconcile failed", exception);
        }
        finally
        {
            Volatile.Write(ref _reconciling, 0);
        }
        if (Interlocked.Exchange(ref _followUpRequested, 0) != 0)
            _timer?.Change(FollowUpReconcileDelayMs, ReconcileIntervalMs);
    }

    #endregion

    #region Formation

    /// <summary>
    /// Forms guilds and places unguilded bots for every realm, then posts binding of changed live
    /// bots to the game loop. <paramref name="roster"/> is the non-retired roster.
    /// </summary>
    public static void Reconcile(IReadOnlyList<OfflineWorldBotRecord> roster)
    {
        if (roster == null || roster.Count == 0)
            return;

        double share = Properties.BOT_GUILD_SHARE;
        int minLevel = Math.Max(1, Properties.BOT_GUILD_MIN_LEVEL);
        var changed = new HashSet<long>();
        var nowUtc = DateTime.UtcNow.ToString("O");

        foreach (OfflineWorldBotRecord record in roster)
            Summaries[record.BotId] = new BotSummary(record.Name, record.Level, record.ClassId, (eRealm)record.Realm);

        lock (Sync)
        {
            // Drop memberships of bots that are gone or retired, and of guilds that no longer exist.
            var live = roster.Select(record => record.BotId).ToHashSet();
            foreach (Membership membership in Members.Values.ToArray())
            {
                if (live.Contains(membership.BotId) && GuildMgr.GetGuildByGuildID(membership.GuildId) != null)
                    continue;
                DeleteMembership(membership.BotId);
                if (!live.Contains(membership.BotId))
                    Summaries.TryRemove(membership.BotId, out _);
                changed.Add(membership.BotId);
            }

            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                ReconcileRealm(realm, roster, share, minLevel, nowUtc, changed);
        }

        FlushWrites();
        if (changed.Count > 0)
            NpcService.Instance.Post(RebindLiveBots, changed.ToArray());
        if (AutonomousBotGuildAlliances.Enabled)
            NpcService.Instance.Post(AutonomousBotGuildAlliances.ReconcileAll, 0);
    }

    private static void ReconcileRealm(eRealm realm, IReadOnlyList<OfflineWorldBotRecord> roster, double share, int minLevel,
        string nowUtc, HashSet<long> changed)
    {
        // Bots in a player's guild are the player's business; formation never touches them.
        List<OfflineWorldBotRecord> eligible = roster
            .Where(record => (eRealm)record.Realm == realm && !record.IsRetired && record.Level >= minLevel)
            .Where(record => !Members.TryGetValue(record.BotId, out Membership m) || BotGuilds.ContainsKey(m.GuildId))
            .ToList();
        int memberTarget = AutonomousBotGuildPlanner.MemberTarget(eligible.Count, share);
        int desired = AutonomousBotGuildPlanner.DesiredGuildCount(memberTarget);

        List<BotGuildInfo> guilds = BotGuilds.Values.Where(info => info.Realm == realm).OrderBy(info => info.Ordinal).ToList();
        int nextOrdinal = guilds.Count == 0 ? 0 : guilds.Max(info => info.Ordinal) + 1;
        // New guilds are founded on the game loop (GuildMgr, Guild timers); this pass places members only
        // in guilds that already exist, and a follow-up pass 30 seconds later fills the new ones.
        for (int missing = desired - guilds.Count; missing > 0; missing--)
        {
            int ordinal = nextOrdinal++;
            if (PendingCreations.TryAdd((realm, ordinal), 0))
            {
                NpcService.Instance.Post(CreateBotGuildOnGameLoop, (realm, ordinal, nowUtc));
                Interlocked.Exchange(ref _followUpRequested, 1);
            }
        }
        if (guilds.Count == 0)
            return;

        var realmGuildIds = guilds.Select(info => info.GuildId).ToHashSet(StringComparer.Ordinal);
        var counts = Members.Values.Where(m => realmGuildIds.Contains(m.GuildId))
            .GroupBy(m => m.GuildId).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int[] targets = AutonomousBotGuildPlanner.TargetSizes(guilds.Count, Math.Max(memberTarget, counts.Values.Sum()));
        var slots = guilds.Select((info, index) => new AutonomousBotGuildPlanner.GuildSlot(
            info.GuildId, info.Charter, info.Ordinal, targets[index], counts.GetValueOrDefault(info.GuildId))).ToList();

        int placedNow = slots.Sum(slot => slot.Count);
        var unplaced = eligible.Where(record => !Members.ContainsKey(record.BotId))
            .Select(record => new AutonomousBotGuildPlanner.Candidate(record.BotId, record.Level));
        foreach (AutonomousBotGuildPlanner.Placement placement in AutonomousBotGuildPlanner.Place(unplaced, slots, memberTarget - placedNow))
        {
            SetMembership(placement.BotId, placement.GuildId, AutonomousBotGuildPlanner.LowestRank, nowUtc);
            changed.Add(placement.BotId);
        }

        var levels = roster.ToDictionary(record => record.BotId, record => record.Level);
        foreach (BotGuildInfo guild in guilds)
        {
            var members = Members.Values.Where(m => m.GuildId == guild.GuildId)
                .Select(m => new AutonomousBotGuildPlanner.Member(m.BotId, levels.GetValueOrDefault(m.BotId), m.RankLevel)).ToList();
            foreach ((long botId, int rank) in AutonomousBotGuildPlanner.RankChanges(members))
            {
                SetMembership(botId, guild.GuildId, rank, null);
                changed.Add(botId);
            }
        }
    }

    private static void CreateBotGuildOnGameLoop((eRealm Realm, int Ordinal, string NowUtc) request)
    {
        try
        {
            CreateBotGuild(request.Realm, request.Ordinal, request.NowUtc);
        }
        catch (Exception exception)
        {
            Log.Error($"Bot guilds: could not found guild {request.Ordinal} for {request.Realm}", exception);
        }
        finally
        {
            PendingCreations.TryRemove((request.Realm, request.Ordinal), out _);
        }
    }

    private static BotGuildInfo CreateBotGuild(eRealm realm, int ordinal, string nowUtc)
    {
        BotGuildCharter charter = AutonomousBotGuildPlanner.CharterFor(ordinal);
        string name = AutonomousBotGuildPlanner.NameFor(realm, charter, ordinal, GuildMgr.DoesGuildExist);
        Guild guild = GuildMgr.CreateGuild(realm, name);
        if (guild == null)
        {
            Log.Warn($"Bot guilds: could not create guild \"{name}\" for {realm}");
            return null;
        }

        string[] titles = AutonomousBotGuildPlanner.RankTitles(charter);
        var retitled = new List<DataObject>();
        for (int rank = 0; rank < titles.Length; rank++)
        {
            DbGuildRank dbRank = guild.GetRankByID(rank);
            if (dbRank == null)
                continue;
            dbRank.Title = titles[rank];
            dbRank.Dirty = true;
            retitled.Add(dbRank);
        }
        // Audit 2026-10-17: these writes ran outside the shared database write lock that the batched bot writers
        // hold, so they could interleave with a status or membership batch. The rank titles are one batched save.
        if (retitled.Count > 0)
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                GameServer.Database.SaveObject(retitled);

        AutonomousBotGuildEconomy.SetCharterDues(guild, charter);
        ApplyMemberRankRights(guild);

        var row = new OfflineBotGuildRecord
        {
            GuildId = guild.GuildID, Realm = (int)realm, Charter = charter.ToString(), Ordinal = ordinal, CreatedUtc = nowUtc,
        };
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            GameServer.Database.AddObject(row);
        var info = new BotGuildInfo(guild.GuildID, realm, charter, ordinal);
        BotGuilds[guild.GuildID] = info;
        if (Log.IsInfoEnabled)
            Log.Info($"Bot guilds: founded {charter} guild \"{name}\" in {realm}");
        return info;
    }

    /// <summary>The rank a player joins a bot guild at: 8, the lowest rank that may speak in guild chat.</summary>
    public const int PlayerJoinRank = 8;

    /// <summary>
    /// Ranks 7 and 8 of a bot guild also hear alliance chat (by default only ranks 0-6 do), so a
    /// player who joins hears the realm alliance. Saves only what changed.
    /// </summary>
    public static void ApplyMemberRankRights(Guild guild)
    {
        if (guild == null)
            return;
        for (int level = 7; level <= PlayerJoinRank; level++)
        {
            DbGuildRank rank = guild.GetRankByID(level);
            if (rank == null || rank.AcHear && rank.GcHear && rank.GcSpeak)
                continue;
            rank.AcHear = true;
            rank.GcHear = true;
            rank.GcSpeak = true;
            rank.Dirty = true;
            GameServer.Database.SaveObject(rank);
        }
    }

    /// <summary>A guild was deleted (/gc purge): its bot memberships and bot-guild marker go with it.</summary>
    public static void ForgetGuild(string guildId)
    {
        if (string.IsNullOrEmpty(guildId))
            return;
        lock (Sync)
        {
            foreach (Membership membership in Members.Values.Where(m => m.GuildId == guildId).ToArray())
                DeleteMembership(membership.BotId);
            BotGuilds.TryRemove(guildId, out _);
        }
    }

    // Callers hold Sync.
    private static void SetMembership(long botId, string guildId, int rank, string joinedUtc)
    {
        if (!MemberRows.TryGetValue(botId, out OfflineBotGuildMemberRecord row))
        {
            row = new OfflineBotGuildMemberRecord { BotId = botId, GuildId = guildId, RankLevel = rank, JoinedUtc = joinedUtc ?? DateTime.UtcNow.ToString("O") };
            MemberRows[botId] = row;
        }
        else
        {
            if (row.GuildId != guildId)
                row.JoinedUtc = joinedUtc ?? DateTime.UtcNow.ToString("O");
            row.GuildId = guildId;
            row.RankLevel = rank;
            row.Dirty = true;
        }
        // Written in batches by FlushWrites (a failed insert stays queued instead of being forgotten).
        PendingWrites[botId] = row;
        Members[botId] = new Membership(botId, guildId, rank);
    }

    // Callers hold Sync.
    private static void DeleteMembership(long botId)
    {
        if (MemberRows.Remove(botId, out OfflineBotGuildMemberRecord row))
        {
            PendingWrites.Remove(botId);
            // Queued even before its first insert is written: FlushWrites deletes it once it exists, so a
            // membership dropped mid-flush can't come back after a restart (review 2026-10-16).
            PendingDeletes.Add(row);
        }
        Members.TryRemove(botId, out _);
    }

    private static void RebindLiveBots(long[] botIds)
    {
        foreach (long botId in botIds)
        {
            if (AutonomousBotRegistry.TryGet(botId, out GameBot bot) && bot.ObjectState == GameObject.eObjectState.Active)
                Bind(bot);
        }
    }

    #endregion

    #region Live bots

    public static void OnBotOnline(GameBot bot)
    {
        if (bot == null || !Enabled)
            return;
        Bind(bot);
    }

    public static void OnBotOffline(GameBot bot)
    {
        if (bot?.Guild != null)
            bot.Guild.RemoveBotMember(bot);
    }

    /// <summary>Makes a live bot's guild fields match its saved membership (or no guild).</summary>
    public static void Bind(GameBot bot)
    {
        if (bot == null)
            return;
        Guild target = null;
        DbGuildRank rank = null;
        if (Enabled && Members.TryGetValue(bot.DatabaseID, out Membership membership))
        {
            target = GuildMgr.GetGuildByGuildID(membership.GuildId);
            rank = target?.GetRankByID(membership.RankLevel) ?? target?.GetRankByID(AutonomousBotGuildPlanner.LowestRank);
        }

        // Posted game-loop actions run in parallel, so a login and a rebind of the same bot can overlap.
        lock (BindSync)
        {
            if (bot.Guild != null && (bot.Guild != target || target == null))
                bot.Guild.RemoveBotMember(bot);
            if (target != null && rank != null)
                target.AddBotMember(bot, rank);
        }
    }

    #endregion

    #region Player commands

    /// <summary>
    /// /ginvite on an autonomous gamebot. Returns false when bots can't be invited (setting off, or
    /// not an autonomous bot), so the caller shows its usual "can't invite" message.
    /// </summary>
    public static bool TryInvite(GamePlayer inviter, GameBot bot)
    {
        if (!Enabled || inviter?.Guild == null || bot == null || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0)
            return false;

        Guild guild = inviter.Guild;
        Members.TryGetValue(bot.DatabaseID, out Membership current);
        bool inBotGuild = current != null && BotGuilds.ContainsKey(current.GuildId);
        if (current != null && current.GuildId == guild.GuildID)
        {
            inviter.Out.SendMessage($"{bot.Name} is already in {guild.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }

        AutonomousBotGuildPlanner.InviteAnswer answer = AutonomousBotGuildPlanner.AnswerInvite(
            bot.Realm == guild.Realm, bot.IsAlive, bot.InCombat, current != null && !inBotGuild,
            inBotGuild ? current.RankLevel : -1, bot.Level, inviter.Level);

        string botGuildName = inBotGuild ? GuildMgr.GetGuildByGuildID(current.GuildId)?.Name : null;
        switch (answer)
        {
            case AutonomousBotGuildPlanner.InviteAnswer.WrongRealm:
                return false;
            case AutonomousBotGuildPlanner.InviteAnswer.Dead:
                inviter.Out.SendMessage($"{bot.Name} is dead and can't answer.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            case AutonomousBotGuildPlanner.InviteAnswer.Busy:
                bot.Say("not now, kinda busy here");
                return true;
            case AutonomousBotGuildPlanner.InviteAnswer.AlreadyInPlayerGuild:
                inviter.Out.SendMessage($"{bot.Name} is already in a guild.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            case AutonomousBotGuildPlanner.InviteAnswer.LeadsOwnGuild:
                bot.Say(current.RankLevel == 0
                    ? $"thanks {inviter.Name}, but i run {botGuildName}, cant leave them"
                    : $"ty for the offer, but im an officer in {botGuildName}");
                return true;
            case AutonomousBotGuildPlanner.InviteAnswer.LevelGap:
                bot.Say(bot.Level > inviter.Level
                    ? "appreciate it, but id be out of your range for groups"
                    : "ty but im a bit low for you guys, maybe later");
                return true;
        }

        DbGuildRank rank = guild.GetRankByID(AutonomousBotGuildPlanner.LowestRank);
        if (rank == null)
            return false;
        lock (Sync)
            SetMembership(bot.DatabaseID, guild.GuildID, rank.RankLevel, null);
        Summaries[bot.DatabaseID] = new BotSummary(bot.Name, bot.Level, bot.CharacterClass?.ID ?? 0, bot.Realm);
        Bind(bot);

        bot.Say(inBotGuild ? $"sure, im in. sorry {botGuildName}, see you around" : $"sure, glad to join {guild.Name}!");
        guild.SendMessageToGuildMembers($"{bot.Name} has joined the guild!", eChatType.CT_Group, eChatLoc.CL_SystemWindow);
        return true;
    }

    /// <summary>/gc remove for a bot member of the remover's guild. False when no bot member has that name.</summary>
    public static bool TryRemove(GamePlayer remover, string name)
    {
        if (!Enabled || remover?.Guild == null || string.IsNullOrWhiteSpace(name))
            return false;

        Guild guild = remover.Guild;
        Membership target = Members.Values.FirstOrDefault(m => m.GuildId == guild.GuildID &&
            string.Equals(NameOf(m.BotId), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return false;

        string botName = NameOf(target.BotId);
        lock (Sync)
            DeleteMembership(target.BotId);
        if (AutonomousBotRegistry.TryGet(target.BotId, out GameBot bot))
            Bind(bot);
        guild.SendMessageToGuildMembers($"{remover.Name} removed {botName} from the guild.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        return true;
    }

    /// <summary>Adds the guild's bot members to a guild window (social window) member list.</summary>
    public static void AppendMemberViews(Guild guild, Dictionary<string, GuildMgr.GuildMemberView> views)
    {
        if (!Enabled || guild == null || views == null)
            return;

        foreach (Membership membership in Members.Values)
        {
            if (membership.GuildId != guild.GuildID)
                continue;
            string id = AutonomousBotEconomy.GetOwnerId(membership.BotId);
            if (AutonomousBotRegistry.TryGet(membership.BotId, out GameBot bot) && bot.ObjectState == GameObject.eObjectState.Active)
            {
                views[id] = new GuildMgr.GuildMemberView(id, bot.Name, bot.Level.ToString(), (bot.CharacterClass?.ID ?? 0).ToString(),
                    membership.RankLevel.ToString(), bot.Group != null ? bot.Group.MemberCount.ToString() : "1",
                    bot.CurrentZone?.Description ?? "Online", string.Empty, guild.Name);
            }
            else if (Summaries.TryGetValue(membership.BotId, out BotSummary summary))
            {
                views[id] = new GuildMgr.GuildMemberView(id, summary.Name, summary.Level.ToString(), summary.ClassId.ToString(),
                    membership.RankLevel.ToString(), "0", "Offline", string.Empty, guild.Name);
            }
        }
    }

    /// <summary>Lines for /gc who: the guild's online bot members.</summary>
    public static List<string> WhoLines(Guild guild)
    {
        var lines = new List<string>();
        if (!Enabled || guild == null)
            return lines;
        foreach (GameBot bot in guild.GetOnlineBotMembers().OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            string rank = bot.GuildRank?.Title ?? bot.GuildRank?.RankLevel.ToString() ?? "?";
            lines.Add($"{bot.Name} <{rank}> the Level {bot.Level} {bot.CharacterClass?.Name ?? "adventurer"} in {bot.CurrentZone?.Description ?? "(null)"}");
        }
        return lines;
    }

    private static string NameOf(long botId)
    {
        if (AutonomousBotRegistry.TryGet(botId, out GameBot bot))
            return bot.Name;
        return Summaries.TryGetValue(botId, out BotSummary summary) ? summary.Name : string.Empty;
    }

    #endregion
}
