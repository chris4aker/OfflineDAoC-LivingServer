using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using static DOL.GS.ServerRules.IServerRules;

namespace DOL.GS;

/// <summary>
/// Treats a persistent autonomous gamebot as a player only for RvR realm-point
/// credit. Ordinary NPCs, controlled pets, and temporary /spawn companions
/// remain ineligible victims.
/// </summary>
public static class AutonomousBotRealmPointRewards
{
    internal const string LastRealmPointDeathTickProperty = "autonomous.rvr.last.realm.point.death.tick";

    public static bool IsEligibleVictim(GameNPC npc) =>
        npc is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false };

    // #571 step 2 (harvest F11; idea from zomboden/OfflineDAoC-True165 74334692 and de83551a, GPL-3.0, rewritten on our
    // reward code). Owner, 2026-11-27 (#571): "Kill share (#755): only add. A hired companion or befriended gamebot in your
    // group may raise your share of XP, bounty points, coins and RP, but never lower it below what you'd get without it."
    // A player's share of a PvP kill is worked out twice: as before (his group's players only) and with the persistent
    // gamebots of his group counted as groupmates (their damage the group's, their number in the split). The second is
    // used only when it gives him at least as much of every reward (RP, bounty points, XP, coins) and more of one
    // (UseMixedShare). So a player who healed while his hired companion landed every blow, who had 0, gets the group share,
    // and a player who did the damage keeps his whole reward. The gamebots' own realm points are paid on their side
    // (PayGamebots, unchanged) and never come out of the player's. /spawn helpers and /bot companions are unchanged: their
    // damage is already their owner's (BotBrain.GetLivingOwner).

    /// <summary>A persistent gamebot (not a /spawn helper) in a group with a real player in it.</summary>
    public static bool IsPlayerGroupBot(GameLiving member) =>
        member is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } bot && HasPlayer(bot.Group);

    /// <summary>A player-group gamebot that may count in its group's share of this kill: of a hostile realm, in the world and
    /// within reward range, as a player must be.</summary>
    public static bool CountsWithPlayerGroup(GameLiving member, GameLiving victim) =>
        victim != null && IsPlayerGroupBot(member) && member.Realm != eRealm.None && member.Realm != victim.Realm &&
        member.ObjectState == GameObject.eObjectState.Active && member.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE);

    private static bool CountsAsPlayer(GameLiving member, GameLiving victim) =>
        member is GamePlayer player && player.ObjectState == GameObject.eObjectState.Active &&
        player.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE);

    /// <summary>A group with a real player and at least one persistent gamebot in it.</summary>
    public static bool IsMixedGroup(Group group)
    {
        if (group == null) return false;
        bool player = false, bot = false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
        {
            player |= member is GamePlayer;
            bot |= member is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false };
        }
        return player && bot;
    }

    private static bool HasPlayer(Group group)
    {
        if (group == null) return false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
            if (member is GamePlayer) return true;
        return false;
    }

    /// <summary>
    /// Each mixed group's count and damage with its gamebots counted: its players and player-group gamebots among the
    /// contributors (GameLiving.TakeDamage enrols every group member in range, so a healer is there with 0 damage).
    /// </summary>
    public static Dictionary<Group, EntityCountTotalDamagePair> MixedGroupContributions(
        IEnumerable<KeyValuePair<GameLiving, double>> contributors, GameLiving victim)
    {
        var groups = new Dictionary<Group, EntityCountTotalDamagePair>();
        foreach (KeyValuePair<GameLiving, double> pair in contributors)
        {
            GameLiving member = pair.Key;
            if (member?.Group is not Group group || !CountsAsPlayer(member, victim) && !CountsWithPlayerGroup(member, victim))
                continue;
            if (!groups.ContainsKey(group) && !IsMixedGroup(group))
                continue;
            if (groups.TryGetValue(group, out EntityCountTotalDamagePair value))
            {
                value.Count++;
                value.Damage += pair.Value;
                value.Track(member);
            }
            else
                groups[group] = new EntityCountTotalDamagePair(1, pair.Value, member);
        }
        return groups;
    }

    /// <summary>What a player-kill share is worth to its awarder (the player rule's own inputs).</summary>
    public readonly record struct PvpKillValues(
        int VictimRealmPoints, int VictimRealmLevel, int AwarderRealmPoints, int AwarderRealmLevel, bool RankAdjust,
        long VictimExperience, long AwarderExperience, int ExperienceCapPercent,
        int VictimBountyPoints, int AwarderBountyPoints, bool BountyWorthless,
        long VictimMoney, long AwarderMoney);

    /// <summary>
    /// The player rule's rewards for one share (AbstractServerRules.AwardPlayerOnPlayerKill, and Award for a gamebot
    /// victim): [realm points, bounty points, experience, coins]. <paramref name="battlegroundFloor"/> applies
    /// <see cref="KeepBattlegroundShare"/> to the realm points.
    /// </summary>
    public static long[] PvpKillRewards(in PvpKillValues v, int count, bool grouped, double share, Func<long, long> experienceBonus,
        bool battlegroundFloor = false)
    {
        count = Math.Max(1, count);
        share = Math.Min(1.0, Math.Max(0, share));
        int realmPoints = CalculateRealmPointReward(v.VictimRealmPoints, v.VictimRealmLevel, v.AwarderRealmPoints,
            v.AwarderRealmLevel, count, grouped ? count : 1, share, v.RankAdjust);
        if (battlegroundFloor)
            realmPoints = KeepBattlegroundShare(realmPoints, true, true, share, v.VictimRealmPoints);
        int bounty = CalculateBountyPointReward(v.VictimBountyPoints, v.AwarderBountyPoints, count, share, v.BountyWorthless, false);
        long capped = CalculateCappedExperienceShare(v.VictimExperience, v.AwarderExperience, count, v.ExperienceCapPercent);
        long experience = (long)(capped * share) + (capped > 0 ? experienceBonus?.Invoke(capped) ?? 0 : 0);
        long money = v.VictimMoney <= 0 ? 0 : (long)(Math.Min(v.VictimMoney / count, v.AwarderMoney * 2) * share);
        return [realmPoints, bounty, experience, money];
    }

    /// <summary>
    /// The owner's "only add" (#571, 2026-11-27): the share with the group's gamebots counted is used only when it gives
    /// the player at least as much of every reward and more of one; otherwise his share stays as it was.
    /// </summary>
    public static bool UseMixedShare(IReadOnlyList<long> withoutBots, IReadOnlyList<long> withBots)
    {
        bool more = false;
        for (int i = 0; i < withoutBots.Count; i++)
        {
            if (withBots[i] < withoutBots[i]) return false;
            more |= withBots[i] > withoutBots[i];
        }
        return more;
    }

    /// <summary>
    /// A battleground victim worth a few realm points (a level 21 is worth 1) split over a player and his gamebots rounds
    /// to 0 each: with a real share the mixed share keeps at least 1 RP. Used only inside the mixed share, so it only adds.
    /// </summary>
    public static int KeepBattlegroundShare(int realmPoints, bool mixedGroup, bool battleground, double damagePercent,
        int victimRealmPointValue) =>
        realmPoints <= 0 && mixedGroup && battleground && damagePercent > 0 && victimRealmPointValue > 0 ? 1 : realmPoints;

    /// <summary>The victim stands in a battleground (its zone, or one of the battleground regions).</summary>
    public static bool InBattleground(GameLiving victim) =>
        victim != null && (victim.CurrentZone?.IsBG == true || BattlegroundBrackets.IsBattlegroundRegion(victim.CurrentRegionID));

    public static int GetPlayerEquivalentRealmPointValue(byte level, int realmLevel)
    {
        // This is the pre-1.81 player formula used by GamePlayer.RealmPointsValue.
        int modifiedLevel = level - 20;
        return Math.Max(1, modifiedLevel * modifiedLevel) + realmLevel;
    }

    public static int CalculateRealmPointReward(int victimRealmPointValue, int victimRealmLevel,
        int awarderRealmPointValue, int awarderRealmLevel, int participantCount,
        int groupContributorCount, double damagePercent, bool applyRealmRankAdjustment)
    {
        if (victimRealmPointValue <= 0 || awarderRealmPointValue <= 0 ||
            participantCount <= 0 || damagePercent <= 0)
        {
            return 0;
        }

        int baseRealmPoints = victimRealmPointValue / participantCount;
        baseRealmPoints = Math.Min(baseRealmPoints, awarderRealmPointValue * 2);
        int realmPoints = (int)(baseRealmPoints * Math.Min(1.0, damagePercent));

        if (applyRealmRankAdjustment)
        {
            realmPoints = (int)(realmPoints *
                (1.0 + 2.0 * (victimRealmLevel - awarderRealmLevel) / 900.0));
        }

        if (groupContributorCount > 1)
            realmPoints += (int)(realmPoints * (groupContributorCount - 1) * 0.125);

        return Math.Max(0, realmPoints);
    }

    /// <summary>
    /// What a gamebot is worth in experience to the player who kills it: a player of its level
    /// (GamePlayer.ExperienceValue is four times the monster value). Issue #396: gamebots paid realm
    /// points only, so a hundred kills in Thidranki gave no experience.
    /// </summary>
    public static long GetPlayerEquivalentExperienceValue(long monsterExperienceValueForLevel) =>
        Math.Max(0, monsterExperienceValueForLevel) * 4;

    /// <summary>The bounty point value of a player of this level (GamePlayer.BountyPointsValue).</summary>
    public static int GetPlayerEquivalentBountyPointValue(byte level) => (int)(1 + level * 0.6);

    /// <summary>
    /// The player-kill experience rule (AbstractServerRules.AwardPlayerOnPlayerKill) before the damage share:
    /// the victim's value split between the contributors, capped at XP_PVP_Cap_Percent of the awarder's own value.
    /// </summary>
    public static long CalculateCappedExperienceShare(long victimExperienceValue, long awarderExperienceValue,
        int participantCount, int pvpCapPercent)
    {
        if (victimExperienceValue <= 0 || awarderExperienceValue <= 0 || participantCount <= 0 || pvpCapPercent <= 0)
            return 0;

        return Math.Min(victimExperienceValue / participantCount, awarderExperienceValue * pvpCapPercent / 100);
    }

    /// <summary>
    /// The player-kill bounty point rule: the victim's value split between the contributors, capped at twice
    /// the awarder's own value, then by damage share. Nothing in a battleground unless allow_bps_in_bgs is on.
    /// </summary>
    public static int CalculateBountyPointReward(int victimBountyPointValue, int awarderBountyPointValue,
        int participantCount, double damagePercent, bool victimInBattleground, bool allowInBattlegrounds)
    {
        if (victimBountyPointValue <= 0 || awarderBountyPointValue <= 0 || participantCount <= 0 ||
            damagePercent <= 0 || (victimInBattleground && !allowInBattlegrounds))
        {
            return 0;
        }

        int baseBountyPoints = Math.Min(victimBountyPointValue / participantCount, awarderBountyPointValue * 2);
        return Math.Max(0, (int)(baseBountyPoints * Math.Min(1.0, damagePercent)));
    }

    private static readonly DOL.Logging.Logger RvrLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousBotRealmPointRewards));

    /// <summary>
    /// RVR_BOT_KILLED: every gamebot killed by another realm (bot, player, pet or guard), with its siege event
    /// force and where it fell. Siege armies were losing half their strength on the march with no record of it
    /// (run 17, Caer Sursbrooke).
    /// </summary>
    private static void LogRvrDeath(GameBot victim, GameObject killer)
    {
        if (victim?.IsAutonomousWorldBot != true || killer is not GameLiving living || !RvrLog.IsInfoEnabled) return;
        GameLiving owner = ResolveRootRewardOwner(living) ?? living;
        if (owner.Realm == eRealm.None || owner.Realm == victim.Realm) return;
        string force = victim.TempProperties.GetProperty<string>("RvrEventForce") ?? "";
        // #505: the fight it fell in, both groups, its role and whether it was the killers' called target.
        long fight = RvrFights.Death(victim, owner);
        RvrRecordPulse.CountDeath(victim.Realm, owner.Realm);
        if (owner is GamePlayer playerKiller)
            RvrPlayerRecord.Killed(playerKiller);
        bool called = ReferenceEquals(RvrFightBoards.Peek(owner)?.Plan?.CalledTarget, victim);
        RvrLog.Info($"RVR_BOT_KILLED victim={victim.Name} id={victim.DatabaseID} realm={GlobalConstants.RealmToName(victim.Realm)} " +
                    $"goal={AutonomousObjectiveAssignments.KindFor(victim)} force={force} killer=\"{owner.Name}\" killerRealm={GlobalConstants.RealmToName(owner.Realm)} " +
                    $"killerType={owner.GetType().Name} region={victim.CurrentRegionID} zone=\"{victim.CurrentZone?.Description}\" " +
                    $"at={victim.X},{victim.Y},{victim.Z} fight={fight} victimGroup={RvrFights.GroupOf(victim) ?? "-"} " +
                    $"victimRole={RvrRoles.For((eCharacterClass)(victim.CharacterClass?.ID ?? 0))} " +
                    $"victimClass={RvrRecordText.Field(victim.CharacterClass?.Name ?? "-")} killerGroup={RvrFights.GroupOf(owner) ?? "-"} " +
                    $"by={RvrRecordText.Kind(living)} calledTarget={(called ? "true" : "false")}" +
                    (owner is GamePlayer named ? $" player={RvrRecordText.Field(named.Name)}" : string.Empty));
    }

    public static void Award(GameBot killedBot, GameObject killer)
    {
        LogRvrDeath(killedBot, killer);
        if (!IsEligibleVictim(killedBot))
            return;

        long now = GameLoop.GameLoopTime;
        long previousDeath = killedBot.TempProperties.GetProperty<long>(
            LastRealmPointDeathTickProperty, -1);
        long worthInterval = Math.Max(0, Properties.RP_WORTH_SECONDS) * 1000L;
        bool isWorthRealmPoints = previousDeath < 0 || now - previousDeath >= worthInterval;

        // Every death resets the same repeat-kill window used for a real player,
        // including a death caused by an NPC or by somebody who receives no RP.
        killedBot.TempProperties.SetProperty(LastRealmPointDeathTickProperty, now);

        KeyValuePair<GameLiving, double>[] rawContributors;
        lock (killedBot.XpGainersLock)
            rawContributors = killedBot.XPGainers.ToArray();

        Dictionary<GameLiving, double> hostileContributors = new();
        foreach (KeyValuePair<GameLiving, double> pair in rawContributors)
        {
            GameLiving credited = ResolveRootRewardOwner(pair.Key);
            if (credited == null || credited.Realm == eRealm.None || credited.Realm == killedBot.Realm)
                continue;

            hostileContributors[credited] = hostileContributors.TryGetValue(credited, out double existing)
                ? existing + pair.Value
                : pair.Value;
        }

        double totalDamage = hostileContributors.Sum(pair => pair.Value);
        if (totalDamage <= 0)
            return;

        int victimRealmPointValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);
        if (isWorthRealmPoints)
            PayGamebots(killedBot, victimRealmPointValue, killedBot.RealmLevel, hostileContributors, totalDamage);

        Dictionary<GamePlayer, EntityCountTotalDamagePair> playerContributions = new();
        Dictionary<Group, EntityCountTotalDamagePair> groupContributions = new();

        foreach (KeyValuePair<GameLiving, double> pair in hostileContributors)
        {
            // Persistent gamebots remain part of the damage denominator, just
            // like another real participant, but this path only pays connected
            // players. Temporary companions have already resolved to the owner.
            if (pair.Key is not GamePlayer player ||
                player.ObjectState is not GameObject.eObjectState.Active ||
                !player.IsWithinRadius(killedBot, WorldMgr.MAX_EXPFORKILL_DISTANCE))
            {
                continue;
            }

            AddContribution(player, pair.Value, player, playerContributions);
            if (player.Group != null)
                AddContribution(player, pair.Value, player.Group, groupContributions);
        }

        if (playerContributions.Count == 0)
            return;

        // #571 step 2: the same groups with their gamebots counted, for the "only add" choice below.
        Dictionary<Group, EntityCountTotalDamagePair> mixedContributions = MixedGroupContributions(hostileContributors, killedBot);

        GameLiving creditedKiller = ResolveRootRewardOwner(killer as GameLiving);
        int victimValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);

        foreach (KeyValuePair<GamePlayer, EntityCountTotalDamagePair> pair in playerContributions)
        {
            GamePlayer player = pair.Key;
            lock (player.AwardLock)
            {
                EntityCountTotalDamagePair contribution = pair.Value;
                if (player.Group != null && groupContributions.TryGetValue(player.Group, out EntityCountTotalDamagePair group))
                    contribution = group;

                double damagePercent = Math.Min(1.0, contribution.Damage / totalDamage);
                int contributorCount = Math.Max(1, contribution.Count);
                int groupContributorCount = player.Group == null ? 1 : contributorCount;
                int realmPointsEarned = 0;

                if (isWorthRealmPoints)
                {
                    DbBattleground battleground = GameServer.KeepManager.GetBattleground(player.CurrentRegionID);
                    bool applyRankAdjustment = battleground == null || player.RealmLevel < battleground.MaxRealmLevel;
                    bool battlegroundFloor = false;
                    // #571 step 2, "only add": the group's gamebots count only when that raises every reward of his.
                    if (player.Group != null && mixedContributions.TryGetValue(player.Group, out EntityCountTotalDamagePair mixed))
                    {
                        var values = new PvpKillValues(victimValue, killedBot.RealmLevel, player.RealmPointsValue, player.RealmLevel,
                            applyRankAdjustment,
                            GetPlayerEquivalentExperienceValue(GameServer.ServerRules.GetExperienceForLiving(killedBot.Level)),
                            player.ExperienceValue, Properties.XP_PVP_CAP_PERCENT,
                            GetPlayerEquivalentBountyPointValue(killedBot.Level), player.BountyPointsValue,
                            killedBot.CurrentZone?.IsBG == true && !Properties.ALLOW_BPS_IN_BGS, 0, 0);
                        GamePlayer awarded = player;
                        long Bonus(long b) => DOL.GS.ServerRules.AbstractServerRules.CalculateOutpostExperienceBonus(awarded, b);
                        bool floor = battleground != null || InBattleground(killedBot);
                        double mixedPercent = Math.Min(1.0, mixed.Damage / totalDamage);
                        if (UseMixedShare(PvpKillRewards(values, contributorCount, true, damagePercent, Bonus),
                                PvpKillRewards(values, mixed.Count, true, mixedPercent, Bonus, floor)))
                        {
                            contribution = mixed;
                            damagePercent = mixedPercent;
                            contributorCount = groupContributorCount = Math.Max(1, mixed.Count);
                            battlegroundFloor = floor;
                        }
                    }
                    realmPointsEarned = CalculateRealmPointReward(victimValue, killedBot.RealmLevel,
                        player.RealmPointsValue, player.RealmLevel, contributorCount,
                        groupContributorCount, damagePercent, applyRankAdjustment);
                    if (battlegroundFloor)
                        realmPointsEarned = KeepBattlegroundShare(realmPointsEarned, true, true, damagePercent, victimValue);

                    if (realmPointsEarned > 0)
                        player.GainRealmPoints(realmPointsEarned, true);

                    // Issue #396: a gamebot stands in for an enemy player, so its killer earns the bounty points
                    // and experience of a player kill too (AbstractServerRules.AwardPlayerOnPlayerKill), not only
                    // realm points. Like a player kill the experience is not multiplied by the XP rates.
                    int bountyPoints = CalculateBountyPointReward(
                        GetPlayerEquivalentBountyPointValue(killedBot.Level), player.BountyPointsValue,
                        contributorCount, damagePercent, killedBot.CurrentZone?.IsBG == true,
                        Properties.ALLOW_BPS_IN_BGS);
                    if (bountyPoints > 0)
                    {
                        if (DOL.GS.Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Bounty_Points_5, player.Realm))
                            bountyPoints += (int)(bountyPoints / 100.0 * 5);
                        else if (DOL.GS.Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Bounty_Points_3, player.Realm))
                            bountyPoints += (int)(bountyPoints / 100.0 * 3);
                        player.GainBountyPoints(bountyPoints);
                    }

                    long cappedExperience = CalculateCappedExperienceShare(
                        GetPlayerEquivalentExperienceValue(GameServer.ServerRules.GetExperienceForLiving(killedBot.Level)),
                        player.ExperienceValue, contributorCount, Properties.XP_PVP_CAP_PERCENT);
                    long experience = (long)(cappedExperience * damagePercent);
                    if (cappedExperience > 0)
                        experience += DOL.GS.ServerRules.AbstractServerRules.CalculateOutpostExperienceBonus(player, cappedExperience);
                    if (experience > 0)
                        player.GainExperience(eXPSource.Player, experience);
                }
                else
                {
                    string victimName = AutonomousNameMask.NameFor(player, killedBot, 0, true);
                    player.Out.SendMessage($"{victimName} has been killed recently and is worth no realm points!",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    player.Out.SendMessage($"{victimName} has been killed recently and is worth no experience!",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                }

                bool deathBlow = ReferenceEquals(player, creditedKiller);
                bool soloKill = damagePercent >= 1.0 && contributorCount == 1;
                player.UpdateKillStatsOnPlayerKill(killedBot.Realm, deathBlow, soloKill, realmPointsEarned);
            }
        }
    }

    /// <summary>
    /// Gamebots earn realm points for PvP kills like players: a share of the victim's value by
    /// their part of the damage, with the same group bonus. Only persistent gamebots of a hostile
    /// realm within reward range (pets resolve to their bot).
    /// </summary>
    public static void PayGamebots(GameLiving victim, int victimRealmPointValue, int victimRealmLevel,
        IReadOnlyDictionary<GameLiving, double> hostileContributors, double totalDamage)
    {
        if (victim == null || victimRealmPointValue <= 0 || totalDamage <= 0) return;
        var bots = hostileContributors
            .Where(pair => pair.Key is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } bot &&
                bot.Realm != eRealm.None && bot.Realm != victim.Realm && bot.ObjectState == GameObject.eObjectState.Active &&
                bot.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE))
            .Select(pair => (Bot: (GameBot)pair.Key, Damage: pair.Value)).ToArray();
        foreach (var (bot, damage) in bots)
        {
            int groupCount = bot.Group == null ? 1 : Math.Max(1, bots.Count(other => other.Bot.Group == bot.Group));
            double groupDamage = bot.Group == null ? damage : bots.Where(other => other.Bot.Group == bot.Group).Sum(other => other.Damage);
            int points = CalculateRealmPointReward(victimRealmPointValue, victimRealmLevel,
                GetPlayerEquivalentRealmPointValue(bot.Level, bot.RealmLevel), bot.RealmLevel,
                groupCount, groupCount, Math.Min(1.0, groupDamage / totalDamage), true);
            if (points > 0) bot.GainRealmPoints(points, true);
        }
    }

    /// <summary>A player killed in PvP pays the gamebots that fought them (players are paid by the rules), only past the
    /// player's repeat-kill window (#521: RP_WORTH_SECONDS, as AwardPlayerOnPlayerKill does for players). Called before
    /// GamePlayer.Die stamps this death, so DeathTime is still the previous death. No grey floor: the player path has none
    /// either (the realm point value already scales with the victim's level), and the rule is parity.</summary>
    public static void PayGamebotsForPlayerKill(GamePlayer killedPlayer)
    {
        if (killedPlayer == null) return;
        if (!DOL.GS.ServerRules.PvpRules.WorthRealmPoints(killedPlayer.DeathTime, killedPlayer.PlayedTime, Properties.RP_WORTH_SECONDS))
            return;
        KeyValuePair<GameLiving, double>[] raw;
        lock (killedPlayer.XpGainersLock)
            raw = killedPlayer.XPGainers.ToArray();
        var hostile = new Dictionary<GameLiving, double>();
        foreach (var pair in raw)
        {
            GameLiving credited = ResolveRootRewardOwner(pair.Key);
            if (credited == null || credited.Realm == eRealm.None || credited.Realm == killedPlayer.Realm) continue;
            hostile[credited] = hostile.GetValueOrDefault(credited) + pair.Value;
        }
        PayGamebots(killedPlayer, killedPlayer.RealmPointsValue, killedPlayer.RealmLevel, hostile, hostile.Sum(pair => pair.Value));
    }

    /// <summary>Realm level for a realm point total, from the player table (RR1L0 = 0 ... RR5L3 = 43).</summary>
    public static int RealmLevelFor(long realmPoints)
    {
        if (realmPoints <= 0) return 0;
        long[] table = GamePlayer.REALMPOINTS_FOR_LEVEL;
        for (int level = table.Length - 1; level > 0; level--)
            if (table[level] <= realmPoints) return level;
        return 0;
    }

    public static GameLiving ResolveRootRewardOwner(GameLiving source)
    {
        GameLiving current = source;
        for (int depth = 0; depth < 16 && current is GameNPC npc &&
             npc.Brain is IControlledBrain controlled &&
             controlled.GetLivingOwner() is GameLiving owner; depth++)
        {
            current = owner;
        }

        return current;
    }

    private static void AddContribution<T>(GameLiving participant, double damage, T entity,
        Dictionary<T, EntityCountTotalDamagePair> contributions) where T : class, IGameStaticItemOwner
    {
        if (contributions.TryGetValue(entity, out EntityCountTotalDamagePair value))
        {
            value.Count++;
            value.Damage += damage;
            value.Track(participant);
        }
        else
        {
            contributions[entity] = new EntityCountTotalDamagePair(1, damage, participant);
        }
    }
}
