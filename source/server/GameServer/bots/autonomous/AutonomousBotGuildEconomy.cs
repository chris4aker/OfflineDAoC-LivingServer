using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Guild economy for autonomous gamebots (docs/BOT-GUILDS.md, phase 5), following the player rules:
/// - dues: a guilded bot pays its guild's dues share of the real coin it loots into the guild bank,
///   exactly as a player does (GamePlayer.ApplyGuildDues); nothing is created;
/// - merit points: a bot's level-up and realm rank earn its guild the same merit points a player's do;
/// - buffs: a bot guild's leader spends 1,000 merit points on a 24-hour guild buff that suits the
///   charter, the same price and effect as /gc buff.
/// Bot guilds charge dues by charter from founding; a player's guild keeps the dues its leader set.
/// Since Living Server 1.1.2 (owner 2026-11-03, from a r/daoc report) a gamebot a player invited into his guild
/// (AutonomousBotGuilds, which keeps that membership off the bot's Guild) pays that guild's dues and earns it merit too,
/// under bot_player_guild_economy, whether bot guilds are on or not. Guild realm points, keep claims and guild buffs still
/// never count a bot in a player's guild. Online members see at most one merit line per guild every five minutes.
/// </summary>
public static class AutonomousBotGuildEconomy
{
    public const long BuffMeritCost = 1000;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool Enabled => AutonomousBotRealmGuilds.Enabled && Properties.BOT_GUILD_ECONOMY;

    /// <summary>Gamebots in a player's guild pay its dues and earn it merit (bot_player_guild_economy).</summary>
    public static bool PlayerGuildEnabled => Properties.BOT_PLAYER_GUILD_ECONOMY;

    #region Pure rules (tested)

    /// <summary>Splits looted coin into the guild's dues and the bot's share (the player formula).</summary>
    public static (long ToGuild, long ToBot) SplitDues(long copper, bool duesOn, long percent)
    {
        if (copper <= 0 || !duesOn || percent <= 0)
            return (0, Math.Max(0, copper));
        long toGuild = copper * Math.Min(100, percent) / 100;
        return (toGuild, copper - toGuild);
    }

    /// <summary>Merit points for reaching a level (GuildEvents.LevelUp's formula).</summary>
    public static int MeritForLevel(int level) => level <= 0 ? 0 : (int)(level * (3.0 + level / 25.0));

    /// <summary>Merit points for going from one level to another, one award per level gained.</summary>
    public static long MeritForLevels(int fromLevel, int toLevel)
    {
        long total = 0;
        for (int level = Math.Max(1, fromLevel + 1); level <= toLevel; level++)
            total += MeritForLevel(level);
        return total;
    }

    /// <summary>
    /// Merit points for reaching a realm level (GuildEvents.RealmRankUp's formula, realm level = 10 x (rank - 1) + level):
    /// (3 x (rank - 1))^2 for each realm level above RR2L0 except the round ranks RR2-RR5 (L0), which earn none.
    /// </summary>
    public static int MeritForRealmLevel(int realmLevel)
    {
        int rankAboveOne = realmLevel / 10;
        bool counts = realmLevel % 10 == 0 ? realmLevel > 45 : realmLevel > 10;
        return counts ? 9 * rankAboveOne * rankAboveOne : 0;
    }

    /// <summary>Merit points for going from one realm level to another, one award per realm level gained.</summary>
    public static long MeritForRealmLevels(int fromRealmLevel, int toRealmLevel)
    {
        long total = 0;
        for (int realmLevel = Math.Max(1, fromRealmLevel + 1); realmLevel <= toRealmLevel; realmLevel++)
            total += MeritForRealmLevel(realmLevel);
        return total;
    }

    /// <summary>
    /// The guild a gamebot's dues and merit go to: the player's guild it was invited into while bot_player_guild_economy
    /// is on, otherwise its bot guild while bot_guild_economy is on. A bot in a player's guild never feeds a bot guild.
    /// </summary>
    public static Guild ResolveEconomyGuild(Guild playerGuild, Guild botGuild, bool playerGuildEconomy, bool botGuildEconomy) =>
        playerGuild != null ? (playerGuildEconomy ? playerGuild : null) : botGuildEconomy ? botGuild : null;

    /// <summary>
    /// Takes the dues out of looted coin into the guild bank (under the guild's lock); returns what the looter keeps. No
    /// guild, no dues, or a full bank leaves the whole coin with the looter, so nothing is created or lost.
    /// </summary>
    public static long KeepAfterDues(Guild guild, long copper, out long deposited)
    {
        deposited = 0;
        if (guild == null || copper <= 0)
            return Math.Max(0, copper);
        lock (guild)
        {
            (long toGuild, long toBot) = SplitDues(copper, guild.IsGuildDuesOn(), guild.GetGuildDuesPercent());
            if (toGuild <= 0 || !guild.AddToBank(toGuild, false))
                return copper;
            deposited = toGuild;
            return toBot;
        }
    }

    /// <summary>Adds merit points to a guild under its lock (bot XP and realm point awards can run in parallel).</summary>
    public static bool AwardMerit(Guild guild, long merit)
    {
        if (guild == null || merit <= 0)
            return false;
        lock (guild)
            guild.GainMeritPoints(merit);
        return true;
    }

    /// <summary>Dues a bot guild charges, by charter (social guilds charge none).</summary>
    public static int DefaultDuesPercent(BotGuildCharter charter) => charter switch
    {
        BotGuildCharter.Rvr or BotGuildCharter.Raiding => 5,
        BotGuildCharter.Crafting => 3,
        BotGuildCharter.Leveling => 2,
        _ => 0,
    };

    /// <summary>The buff a bot guild buys: by charter, falling back to whatever the server offers.</summary>
    public static Guild.eBonusType BuffFor(BotGuildCharter charter, bool xp, bool rp, bool crafting)
    {
        Guild.eBonusType wanted = charter switch
        {
            BotGuildCharter.Rvr when rp => Guild.eBonusType.RealmPoints,
            BotGuildCharter.Crafting when crafting => Guild.eBonusType.CraftingHaste,
            _ when xp => Guild.eBonusType.Experience,
            _ => Guild.eBonusType.None,
        };
        if (wanted != Guild.eBonusType.None)
            return wanted;
        return xp ? Guild.eBonusType.Experience : rp ? Guild.eBonusType.RealmPoints : crafting ? Guild.eBonusType.CraftingHaste : Guild.eBonusType.None;
    }

    #endregion

    #region Runtime

    /// <summary>The guild a live gamebot's dues and merit go to (<see cref="ResolveEconomyGuild"/>), or null.</summary>
    public static Guild EconomyGuildOf(GameBot bot)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsEpicSpecialForce || bot.DatabaseID <= 0)
            return null;
        return ResolveEconomyGuild(AutonomousBotGuilds.GuildOf(bot.DatabaseID), bot.Guild, PlayerGuildEnabled, Enabled);
    }

    /// <summary>
    /// The bot's share of looted coin after its guild's dues; <paramref name="dues"/> is the rest and
    /// <paramref name="guild"/> the guild owed them (pass both to <see cref="DepositDues"/>).
    /// </summary>
    public static long ShareAfterDues(GameBot bot, long copper, out long dues, out Guild guild)
    {
        dues = 0;
        guild = EconomyGuildOf(bot);
        if (guild == null || copper <= 0)
            return copper;
        (long toGuild, long toBot) = SplitDues(copper, guild.IsGuildDuesOn(), guild.GetGuildDuesPercent());
        dues = toGuild;
        return toBot;
    }

    /// <summary>
    /// Deposits dues into the guild bank. False when there is no guild or the bank is full;
    /// the caller then gives the coin to the bot, so nothing is lost or created.
    /// </summary>
    public static bool DepositDues(Guild guild, long dues)
    {
        if (guild == null || dues <= 0)
            return dues <= 0;
        lock (guild)
            return guild.AddToBank(dues, false);
    }

    /// <summary>
    /// Pays a gamebot its share of group-split coin less its guild's dues, in the solo pickup's order: the bot's part
    /// first, then the dues to the bank, or back to the bot if the bank is full. False when the bot's part didn't fit.
    /// </summary>
    public static bool PayLootShare(GameBot bot, long copper)
    {
        if (bot == null || copper <= 0)
            return false;
        long share = ShareAfterDues(bot, copper, out long dues, out Guild guild);
        if (!AutonomousBotEconomy.AddMoney(bot.DatabaseID, share))
            return false;
        if (dues > 0 && !DepositDues(guild, dues))
            AutonomousBotEconomy.AddMoney(bot.DatabaseID, dues);
        return true;
    }

    /// <summary>Called by GameBot after a level-up; earns the guild merit points for each level gained.</summary>
    public static void OnBotLevelsGained(GameBot bot, int previousLevel)
    {
        Guild guild = EconomyGuildOf(bot);
        long merit = guild == null ? 0 : MeritForLevels(previousLevel, bot.Level);
        if (AwardMerit(guild, merit))
            Notices.Add(guild.GuildID, bot.Name, merit);
    }

    /// <summary>Called by GameBot when its realm level rises; earns the guild the realm rank merit points of each level gained.</summary>
    public static void OnBotRealmLevelsGained(GameBot bot, int previousRealmLevel)
    {
        Guild guild = EconomyGuildOf(bot);
        long merit = guild == null ? 0 : MeritForRealmLevels(previousRealmLevel, bot.RealmLevel);
        if (AwardMerit(guild, merit))
            Notices.Add(guild.GuildID, bot.Name, merit);
    }

    /// <summary>Gives a new bot guild its charter's dues.</summary>
    public static void SetCharterDues(Guild guild, BotGuildCharter charter)
    {
        if (guild == null)
            return;
        int percent = DefaultDuesPercent(charter);
        guild.SetGuildDues(percent > 0);
        guild.SetGuildDuesPercent(percent);
    }

    /// <summary>
    /// A bot guild's leader buys a guild buff when the guild can afford one and has none. True when
    /// bought. Runs from the leader's ambient chat check (game loop).
    /// </summary>
    public static bool TryBuyBuff(GameBot leader, BotGuildCharter charter)
    {
        Guild guild = leader?.Guild;
        if (!Enabled || guild == null || leader.GuildRank?.RankLevel != 0 || !AutonomousBotRealmGuilds.IsBotGuild(guild))
            return false;
        lock (guild)
        {
            if (guild.BonusType != Guild.eBonusType.None || guild.MeritPoints < BuffMeritCost)
                return false;
            Guild.eBonusType buff = BuffFor(charter, Properties.GUILD_BUFF_XP > 0, Properties.GUILD_BUFF_RP > 0, Properties.GUILD_BUFF_CRAFTING > 0);
            if (buff == Guild.eBonusType.None)
                return false;
            guild.BonusType = buff;
            guild.RemoveMeritPoints(BuffMeritCost);
            guild.BonusStartTime = DateTime.Now;
            guild.TryStartGuildBuffTimer();
        }
        string buffName = Guild.BonusTypeToName(guild.BonusType);
        foreach (GamePlayer member in guild.GetListOfOnlineMembers())
            member.Out.SendMessage($"[Guild Buff] {leader.Name} activated a bonus to {buffName} for 24 hours!", eChatType.CT_Guild, eChatLoc.CL_SystemWindow);
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_GUILD_BUFF guild=\"{guild.Name}\" buff={guild.BonusType} leader=\"{leader.Name}\"");
        return true;
    }

    #endregion

    #region Merit lines

    /// <summary>At most one merit line per guild in this time (game loop milliseconds).</summary>
    public const long MeritNoticeIntervalMs = 300_000;

    internal static readonly MeritNotices Notices = new();
    private static System.Threading.Timer _noticeTimer;

    /// <summary>
    /// Bot merit is told to a guild's online members as one summed line at most every five minutes, so a guild of levelling
    /// bots doesn't flood the system window (a player's own merit keeps GuildEvents' line). Nobody online: nothing is told.
    /// </summary>
    public sealed class MeritNotices
    {
        private sealed class Pending
        {
            public long Merit;
            public readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
            public string LastName;
            public long LastSent = long.MinValue;
        }

        private readonly ConcurrentDictionary<string, Pending> _pending = new();

        public void Add(string guildId, string botName, long merit)
        {
            if (string.IsNullOrEmpty(guildId) || merit <= 0)
                return;
            Pending pending = _pending.GetOrAdd(guildId, _ => new Pending());
            lock (pending)
            {
                pending.Merit += merit;
                if (!string.IsNullOrEmpty(botName))
                {
                    pending.Names.Add(botName);
                    pending.LastName = botName;
                }
            }
        }

        /// <summary>The lines due now, one per guild whose last line is at least the interval old. Guilds nobody hears are cleared.</summary>
        public List<(string GuildId, string Line)> TakeDue(long now, Func<string, bool> heard, long interval = MeritNoticeIntervalMs)
        {
            var due = new List<(string, string)>();
            foreach (var (guildId, pending) in _pending)
            {
                lock (pending)
                {
                    if (pending.Merit <= 0)
                        continue;
                    if (!heard(guildId))
                    {
                        Clear(pending);
                        continue;
                    }
                    if (pending.LastSent != long.MinValue && now - pending.LastSent < interval)
                        continue;
                    due.Add((guildId, Line(pending.Merit, pending.Names.Count, pending.LastName)));
                    pending.LastSent = now;
                    Clear(pending);
                }
            }
            return due;
        }

        private static void Clear(Pending pending)
        {
            pending.Merit = 0;
            pending.Names.Clear();
            pending.LastName = null;
        }

        public static string Line(long merit, int bots, string name) => bots <= 1 && !string.IsNullOrEmpty(name)
            ? $"{name} has earned {merit} merit points for your guild!"
            : $"{bots} guildmates have earned {merit} merit points for your guild!";
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        _noticeTimer?.Dispose();
        _noticeTimer = new System.Threading.Timer(_ => NpcService.Instance.Post<int>(_ => SendMeritNotices(), 0), null, 60_000, 60_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _noticeTimer?.Dispose();
        _noticeTimer = null;
    }

    private static void SendMeritNotices()
    {
        try
        {
            foreach (var (guildId, line) in Notices.TakeDue(GameLoop.GameLoopTime,
                         id => GuildMgr.GetGuildByGuildID(id)?.GetListOfOnlineMembers().Count > 0))
                GuildMgr.GetGuildByGuildID(guildId)?.SendMessageToGuildMembers(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        }
        catch (Exception exception)
        {
            Log.Warn("Bot guild merit lines failed", exception);
        }
    }

    #endregion
}
