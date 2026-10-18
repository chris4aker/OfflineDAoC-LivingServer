using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bot guild alliances (docs/BOT-GUILDS.md, phase 6). In each realm the RvR and raiding bot guilds
/// form one alliance of up to four guilds, led by the oldest RvR guild, using the same Alliance
/// objects and table as /gc ainvite. A player in one of those guilds hears its /as chatter.
/// Alliances that include a player's guild are never touched.
/// </summary>
public static class AutonomousBotGuildAlliances
{
    public const int MaximumGuildsPerAlliance = 4;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool Enabled => AutonomousBotGuilds.Enabled && Properties.BOT_GUILD_ALLIANCES;

    #region Pure rules (tested)

    public readonly record struct Candidate(string GuildId, BotGuildCharter Charter, int Ordinal, bool InAlliance);

    public static bool IsAllianceCharter(BotGuildCharter charter) => charter is BotGuildCharter.Rvr or BotGuildCharter.Raiding;

    /// <summary>
    /// The realm's alliance plan: with no bot alliance yet, at least two free RvR/raiding guilds
    /// found one (leader: the oldest RvR guild, else the oldest); an existing one takes free guilds,
    /// oldest first, up to the maximum.
    /// </summary>
    public static (string LeaderId, List<string> ToAdd) Plan(IReadOnlyList<Candidate> realmGuilds, string existingLeaderId, int existingCount)
    {
        List<Candidate> free = (realmGuilds ?? Array.Empty<Candidate>())
            .Where(guild => IsAllianceCharter(guild.Charter) && !guild.InAlliance && !string.IsNullOrEmpty(guild.GuildId))
            .OrderBy(guild => guild.Ordinal).ThenBy(guild => guild.GuildId, StringComparer.Ordinal)
            .ToList();
        if (existingLeaderId == null)
        {
            if (free.Count < 2)
                return (null, new List<string>());
            Candidate leader = free.FirstOrDefault(guild => guild.Charter == BotGuildCharter.Rvr);
            if (leader.GuildId == null)
                leader = free[0];
            return (leader.GuildId, free.Where(guild => guild.GuildId != leader.GuildId)
                .Take(MaximumGuildsPerAlliance - 1).Select(guild => guild.GuildId).ToList());
        }
        int room = MaximumGuildsPerAlliance - existingCount;
        return (existingLeaderId, room <= 0 ? new List<string>() : free.Take(room).Select(guild => guild.GuildId).ToList());
    }

    #endregion

    #region Runtime (game loop)

    /// <summary>Forms or grows each realm's bot alliance. Posted to the game loop by bot guild reconcile.</summary>
    public static void ReconcileAll(int unused)
    {
        if (!Enabled)
            return;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            try
            {
                ReconcileRealm(realm);
            }
            catch (Exception exception)
            {
                Log.Error($"Bot guilds: alliance reconcile failed for {realm}", exception);
            }
        }
    }

    private static void ReconcileRealm(eRealm realm)
    {
        List<(Guild Guild, AutonomousBotGuilds.BotGuildInfo Info)> botGuilds = GuildMgr.GetGuilds()
            .Where(guild => guild.Realm == realm && AutonomousBotGuilds.TryGetBotGuild(guild.GuildID, out _))
            .Select(guild =>
            {
                AutonomousBotGuilds.TryGetBotGuild(guild.GuildID, out AutonomousBotGuilds.BotGuildInfo info);
                return (guild, info);
            })
            .ToList();

        // The realm's bot alliance: one led by a bot guild whose guilds are all bot guilds.
        Alliance existing = botGuilds.Select(entry => entry.Guild.alliance)
            .FirstOrDefault(alliance => alliance?.DbAlliance != null &&
                                        alliance.Guilds.All(AutonomousBotGuilds.IsBotGuild) &&
                                        AutonomousBotGuilds.IsBotGuild(GuildMgr.GetGuildByGuildID(alliance.DbAlliance.LeaderGuildID)));
        var candidates = botGuilds.Select(entry => new Candidate(entry.Guild.GuildID, entry.Info.Charter, entry.Info.Ordinal,
            entry.Guild.alliance != null)).ToList();
        (string leaderId, List<string> toAdd) = Plan(candidates, existing?.DbAlliance.LeaderGuildID, existing?.Guilds.Count ?? 0);
        if (leaderId == null || toAdd.Count == 0)
            return;

        Guild leader = GuildMgr.GetGuildByGuildID(leaderId);
        if (leader == null)
            return;
        Alliance alliance = existing;
        if (alliance == null)
        {
            if (leader.alliance != null)
                return;
            // As /gc ainvite does: the leader's alliance object first, persisted when the first guild joins.
            alliance = new Alliance();
            var row = new DbGuildAlliance { AllianceName = leader.Name, LeaderGuildID = leader.GuildID, DBguildleader = null, Motd = string.Empty };
            alliance.DbAlliance = row;
            alliance.AddGuildOnLoad(leader);
            leader.alliance = alliance;
            leader.AllianceId = row.ObjectId;
            leader.SaveIntoDatabase();
        }
        foreach (string guildId in toAdd)
        {
            Guild guild = GuildMgr.GetGuildByGuildID(guildId);
            if (guild == null || guild.alliance != null || guild.Realm != leader.Realm)
                continue;
            alliance.AddGuild(guild);
            if (Log.IsInfoEnabled)
                Log.Info($"Bot guilds: \"{guild.Name}\" joined the alliance of \"{alliance.DbAlliance.AllianceName}\" ({realm})");
        }
    }

    /// <summary>True when a real player in any guild of the alliance can hear alliance chat.</summary>
    public static bool HasListener(Alliance alliance) => alliance != null && alliance.Guilds.Any(guild =>
        guild.GetListOfOnlineMembers().Any(player => guild.HasRank(player, Guild.eRank.AcHear)));

    public static void SendLine(Alliance alliance, string speaker, string text)
    {
        string safe = (text ?? string.Empty).Trim();
        if (alliance == null || safe.Length == 0)
            return;
        if (safe.Length > AutonomousBotGuildChatLines.MaxLength)
            safe = safe[..AutonomousBotGuildChatLines.MaxLength];
        string message = $"[Alliance] {speaker}: \"{safe}\"";
        foreach (Guild guild in alliance.Guilds)
        {
            foreach (GamePlayer player in guild.GetListOfOnlineMembers())
            {
                if (guild.HasRank(player, Guild.eRank.AcHear))
                    player.Out.SendMessage(message, PacketHandler.eChatType.CT_Alliance, PacketHandler.eChatLoc.CL_ChatWindow);
            }
        }
    }

    #endregion
}
