using System;
using System.Reflection;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Guild economy for autonomous gamebots (docs/BOT-GUILDS.md, phase 5), following the player rules:
/// - dues: a guilded bot pays its guild's dues share of the real coin it loots into the guild bank,
///   exactly as a player does (GamePlayer.ApplyGuildDues); nothing is created;
/// - merit points: a bot's level-up earns its guild the same merit points a player's does;
/// - buffs: a bot guild's leader spends 1,000 merit points on a 24-hour guild buff that suits the
///   charter, the same price and effect as /gc buff.
/// Bot guilds charge dues by charter from founding; a player's guild keeps the dues its leader set.
/// </summary>
public static class AutonomousBotGuildEconomy
{
    public const long BuffMeritCost = 1000;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool Enabled => AutonomousBotGuilds.Enabled && Properties.BOT_GUILD_ECONOMY;

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

    /// <summary>The bot's share of looted coin after its guild's dues; <paramref name="dues"/> is the rest.</summary>
    public static long ShareAfterDues(GameBot bot, long copper, out long dues)
    {
        dues = 0;
        Guild guild = bot?.Guild;
        if (!Enabled || guild == null || copper <= 0)
            return copper;
        (long toGuild, long toBot) = SplitDues(copper, guild.IsGuildDuesOn(), guild.GetGuildDuesPercent());
        dues = toGuild;
        return toBot;
    }

    /// <summary>
    /// Deposits dues into the bot's guild bank. False when there is no guild or the bank is full;
    /// the caller then gives the coin to the bot, so nothing is lost or created.
    /// </summary>
    public static bool DepositDues(GameBot bot, long dues)
    {
        Guild guild = bot?.Guild;
        if (guild == null || dues <= 0)
            return dues <= 0;
        lock (guild)
            return guild.AddToBank(dues, false);
    }

    /// <summary>Called by GameBot after a level-up; earns the guild merit points for each level gained.</summary>
    public static void OnBotLevelsGained(GameBot bot, int previousLevel)
    {
        Guild guild = bot?.Guild;
        if (!Enabled || guild == null)
            return;
        long merit = MeritForLevels(previousLevel, bot.Level);
        if (merit <= 0)
            return;
        // Bot XP awards can run in parallel; GainMeritPoints is a plain add.
        lock (guild)
            guild.GainMeritPoints(merit);
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
        if (!Enabled || guild == null || leader.GuildRank?.RankLevel != 0 || !AutonomousBotGuilds.IsBotGuild(guild))
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
}
