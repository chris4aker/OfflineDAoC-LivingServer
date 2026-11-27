using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bot guilds and keeps (docs/BOT-GUILDS.md, phase 4). After a realm takes a keep or tower, a bot
/// guild with enough members inside it claims it under the same rules a player guild meets: the
/// keep's realm, not under attack, not a relic or portal keep, the server's claim limit, and the
/// group size (claim_num, half for towers). A real player of that realm inside the keep always
/// gets the first chance, so bots never claim while one is there. Defense of a claimed keep is a
/// bias in the existing defense response (AutonomousRvrEventLayer.Defense).
/// #523 step 1 (h): the count is the realm's bot-guild members of any guild inside the keep area (the
/// capturing army, which holds the keep for the claim: AutonomousRvrEventLayer.ClaimHold), as a player's
/// claim counts the members of the claimer's group whatever their guild; the guild with the most of them
/// there claims. A claim that never comes is logged once, with why (BOT_GUILD_CLAIM_SKIPPED).
/// </summary>
public static class AutonomousBotGuildKeeps
{
    public const int AttemptIntervalMs = 20_000;
    public const int MaximumAttempts = 8;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool Enabled => AutonomousBotRealmGuilds.Enabled && Properties.BOT_GUILD_CLAIMS_KEEPS;

    #region Pure rules (tested)

    public readonly record struct ClaimCheck(bool SameRealm, bool AlreadyClaimed, bool InCombat, bool ClaimableType,
        bool PlayerPresent, int GuildClaims, int ClaimLimit, int MembersPresent, int Needed);

    public static int NeededMembers(int claimNum, bool tower) => Math.Max(2, tower ? claimNum / 2 : claimNum);

    public static bool CanClaim(ClaimCheck check) => SkipReason(check) == null;

    /// <summary>
    /// Why a claim can't be made now, or null when it can: the first rule of <see cref="ClaimCheck"/> that fails
    /// (BOT_GUILD_CLAIM_SKIPPED reason=). Pure.
    /// </summary>
    public static string SkipReason(ClaimCheck check) =>
        check.AlreadyClaimed ? "claimed" :
        !check.ClaimableType ? "not-claimable" :
        check.ClaimLimit <= 0 ? "claims-off" :
        !check.SameRealm ? "other-realm" :
        check.PlayerPresent ? "player-present" :
        check.InCombat ? "under-attack" :
        check.GuildClaims >= check.ClaimLimit ? "guild-at-limit" :
        check.MembersPresent < check.Needed ? "too-few" : null;

    public readonly record struct Claimant(string GuildId, BotGuildCharter Charter, int Present, bool AtClaimLimit = false);

    /// <summary>
    /// The guild with the most members present; ties go to RvR, then Raiding, then the lowest id. A guild that already holds
    /// as many keeps as guilds_claim_limit allows is passed over (1.2 play-check prep, 2026-11-15: it was chosen anyway and
    /// then failed the limit, so the realm's biggest RvR guild, once it held a keep, stopped every other guild claiming).
    /// </summary>
    public static string ChooseClaimant(IEnumerable<Claimant> claimants) =>
        (claimants ?? Enumerable.Empty<Claimant>())
            .Where(claimant => claimant.Present > 0 && !claimant.AtClaimLimit && !string.IsNullOrEmpty(claimant.GuildId))
            .OrderByDescending(claimant => claimant.Present)
            .ThenBy(claimant => claimant.Charter switch { BotGuildCharter.Rvr => 0, BotGuildCharter.Raiding => 1, _ => 2 })
            .ThenBy(claimant => claimant.GuildId, StringComparer.Ordinal)
            .Select(claimant => claimant.GuildId)
            .FirstOrDefault();

    /// <summary>
    /// 0 when at least half of a responding force belongs to the guild that claimed the attacked
    /// keep (it answers first and is never held in reserve), else 1. Off or unclaimed: always 1.
    /// </summary>
    public static int ClaimDefenseRank(bool enabled, string claimGuildId, IReadOnlyCollection<string> memberGuildIds)
    {
        if (!enabled || string.IsNullOrEmpty(claimGuildId) || memberGuildIds == null || memberGuildIds.Count == 0)
            return 1;
        int own = memberGuildIds.Count(id => string.Equals(id, claimGuildId, StringComparison.Ordinal));
        return own * 2 >= memberGuildIds.Count ? 0 : 1;
    }

    #endregion

    #region Runtime

    [ScriptLoadedEvent]
    public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.AddHandler(KeepEvent.KeepTaken, new DOLEventHandler(OnKeepTaken));

    [ScriptUnloadedEvent]
    public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.RemoveHandler(KeepEvent.KeepTaken, new DOLEventHandler(OnKeepTaken));

    private static void OnKeepTaken(DOLEvent e, object sender, EventArgs args)
    {
        if (!Enabled || args is not KeepEventArgs keepArgs || keepArgs.Keep is not AbstractGameKeep keep)
            return;
        GameKeepComponent owner = keep.KeepComponents?.FirstOrDefault();
        if (owner == null)
            return;
        int attempts = 0;
        _ = new ECSGameTimer(owner, timer =>
        {
            attempts++;
            try
            {
                if (TryClaim(keep, out string reason, out int present, out int needed))
                    return 0;
                if (attempts >= MaximumAttempts || keep.Guild != null)
                {
                    LogSkipped(keep, keep.Guild != null ? "claimed" : reason, present, needed, attempts);
                    return 0;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"Bot guilds: claim attempt on {keep.Name} failed", exception);
                return 0;
            }
            return AttemptIntervalMs;
        }, AttemptIntervalMs);
    }

    /// <summary>The guild id of a keep's claim, looked up by region and name (the RvR event target).</summary>
    public static string ClaimGuildIdOf(ushort regionId, string keepName)
    {
        if (!Enabled || string.IsNullOrEmpty(keepName))
            return null;
        AbstractGameKeep keep = GameServer.KeepManager.GetKeepsOfRegion(regionId)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, keepName, StringComparison.Ordinal));
        return keep?.Guild?.GuildID;
    }

    /// <summary>Claim rank of a defense force for <see cref="AutonomousRvrEventLayer"/>.</summary>
    public static int ClaimDefenseRank(string claimGuildId, IEnumerable<GameBot> members) =>
        ClaimDefenseRank(Enabled, claimGuildId, members?.Select(member => member?.Guild?.GuildID).ToArray());

    private static void LogSkipped(AbstractGameKeep keep, string reason, int present, int needed, int attempts)
    {
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_GUILD_CLAIM_SKIPPED keep=\"{keep.Name}\" realm={GlobalConstants.RealmToName(keep.Realm)} reason={reason ?? "unknown"} " +
                     $"present={present} needed={needed} attempts={attempts}");
    }

    private static bool TryClaim(AbstractGameKeep keep, out string reason, out int membersPresent, out int needed)
    {
        bool tower = keep is GameKeepTower;
        membersPresent = 0;
        needed = NeededMembers(Properties.CLAIM_NUM, tower);
        if (keep.Guild != null) { reason = "claimed"; return false; }
        if (keep.Area == null) { reason = "no-keep-area"; return false; }
        if (keep.Realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)) { reason = "no-realm"; return false; }
        ushort region = keep.Region;
        bool claimableType = keep is not RelicGameKeep && !keep.IsPortalKeep &&
            (keep.DBKeep?.BaseLevel == 50 || Properties.ALLOW_BG_CLAIM);
        bool playerPresent = ClientService.Instance.GetPlayersOfRegion(keep.CurrentRegion)
            .Any(player => player.Realm == keep.Realm && player.IsAlive && player.CurrentAreas.Contains(keep.Area));

        GameBot[] present = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.IsAutonomousWorldBot && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active &&
                          bot.Realm == keep.Realm && bot.CurrentRegionID == region && bot.Guild != null &&
                          AutonomousBotRealmGuilds.IsBotGuild(bot.Guild) && bot.CurrentAreas.Contains(keep.Area))
            .ToArray();
        var byGuild = present.GroupBy(bot => bot.Guild).ToArray();
        // #523 (h): every realm bot-guild member inside counts, whichever guild (a player's claim counts the group).
        membersPresent = present.Length;
        string chosenId = ChooseClaimant(byGuild.Select(group =>
        {
            AutonomousBotRealmGuilds.TryGetBotGuild(group.Key.GuildID, out AutonomousBotRealmGuilds.BotGuildInfo info);
            return new Claimant(group.Key.GuildID, info?.Charter ?? BotGuildCharter.Social, group.Count(),
                group.Key.ClaimedKeeps.Count >= Properties.GUILDS_CLAIM_LIMIT);
        }));
        if (chosenId == null)
        {
            reason = !claimableType ? "not-claimable" : Properties.GUILDS_CLAIM_LIMIT <= 0 ? "claims-off" : playerPresent ? "player-present" :
                present.Length == 0 ? "no-guild-present" : "guilds-at-limit";
            return false;
        }
        Guild guild = byGuild.First(group => group.Key.GuildID == chosenId).Key;

        var check = new ClaimCheck(guild.Realm == keep.Realm, keep.Guild != null, keep.InCombat, claimableType, playerPresent,
            guild.ClaimedKeeps.Count, Properties.GUILDS_CLAIM_LIMIT, membersPresent, needed);
        reason = SkipReason(check);
        if (reason != null)
            return false;

        keep.ClaimForGuild(guild);
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_GUILD_CLAIM keep=\"{keep.Name}\" guild=\"{guild.Name}\" realm={GlobalConstants.RealmToName(keep.Realm)} present={membersPresent}");
        GameBot speaker = byGuild.First(group => group.Key.GuildID == chosenId)
            .OrderBy(bot => bot.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank).First();
        AutonomousBotGuildSocial.OnKeepClaimed(speaker, guild, keep.Name);
        return true;
    }

    #endregion
}
