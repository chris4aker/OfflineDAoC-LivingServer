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
        {
            PayGamebots(killedBot, victimRealmPointValue, killedBot.RealmLevel, hostileContributors, totalDamage);
            PayHiredCompanionExperience(killedBot,
                GetPlayerEquivalentExperienceValue(GameServer.ServerRules.GetExperienceForLiving(killedBot.Level)),
                hostileContributors, totalDamage);
        }

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
                    realmPointsEarned = CalculateRealmPointReward(victimValue, killedBot.RealmLevel,
                        player.RealmPointsValue, player.RealmLevel, contributorCount,
                        groupContributorCount, damagePercent, applyRankAdjustment);

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

    /// <summary>
    /// #571 (True165 de83551a, owner 2026-11-27: "yes, as True165 de83551a does; under the owner's 'only add' rule the
    /// player's own share is never lowered by it"): a hired companion earns experience from a PvP kill (an enemy player or
    /// gamebot), by the player-kill rule, including a hire below the battleground's level bracket. Hired companions only:
    /// other gamebots, /spawn helpers and /bot companions are unchanged. The share is the hire's group's: the players and
    /// hired companions of its group in reach of the victim, their damage over the total, the capped base split by their
    /// number (so a hire beside a player who did all the damage still learns, and the other way round). This pays the
    /// hires only; it never reads or writes a player's reward, which the player paths work out as before.
    /// </summary>
    public static void PayHiredCompanionExperience(GameLiving victim, long victimExperienceValue,
        IReadOnlyDictionary<GameLiving, double> hostileContributors, double totalDamage) =>
        PayHiredCompanionExperience(victim, victimExperienceValue, hostileContributors, totalDamage,
            bot => bot.IsHiredCompanion,
            member => member.Realm != eRealm.None && member.Realm != victim.Realm &&
                      member.ObjectState == GameObject.eObjectState.Active &&
                      member.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE),
            group => group.GetMembersInTheGroup().ToArray(),
            level => GetPlayerEquivalentExperienceValue(GameServer.ServerRules.GetExperienceForLiving(level)),
            Properties.XP_PVP_CAP_PERCENT);

    /// <summary>The testable core of <see cref="PayHiredCompanionExperience(GameLiving, long, IReadOnlyDictionary{GameLiving, double}, double)"/>.
    /// Returns what each hire was offered (before its own level cap in GameBot.GainExperience).</summary>
    public static Dictionary<GameBot, long> PayHiredCompanionExperience(GameLiving victim, long victimExperienceValue,
        IReadOnlyDictionary<GameLiving, double> hostileContributors, double totalDamage, Func<GameBot, bool> isHired,
        Func<GameLiving, bool> inReach, Func<Group, IReadOnlyList<GameLiving>> rosterOf, Func<byte, long> experienceValueOf,
        int pvpCapPercent)
    {
        var paid = new Dictionary<GameBot, long>();
        if (victim == null || victimExperienceValue <= 0 || totalDamage <= 0 || hostileContributors == null)
            return paid;

        // The hires to pay: hired contributors, plus hired members of a contributor's group (a hire in reach is
        // normally in XPGainers already, at 0 damage if it only healed).
        var hires = new List<GameBot>();
        var groups = new HashSet<Group>();
        foreach (GameLiving contributor in hostileContributors.Keys)
        {
            if (contributor is GameBot hire && IsPayableHire(hire, isHired) && !hires.Contains(hire))
                hires.Add(hire);
            if (contributor?.Group is Group group && groups.Add(group))
            {
                foreach (GameLiving member in rosterOf(group))
                    if (member is GameBot groupHire && IsPayableHire(groupHire, isHired) && !hires.Contains(groupHire))
                        hires.Add(groupHire);
            }
        }

        foreach (GameBot hire in hires)
        {
            if (!inReach(hire))
                continue;
            (int members, double share) = HiredCompanionShare(hire, hire.Group == null ? null : rosterOf(hire.Group),
                hostileContributors, totalDamage, isHired, inReach);
            long experience = (long)(CalculateCappedExperienceShare(victimExperienceValue, experienceValueOf(hire.Level),
                members, pvpCapPercent) * share);
            if (experience <= 0)
                continue;
            paid[hire] = experience;
            // Like a player kill, not multiplied by the XP rates; the hire's own cap (its player's level) still holds.
            hire.GainExperience(eXPSource.Player, experience);
        }

        return paid;
    }

    private static bool IsPayableHire(GameBot bot, Func<GameBot, bool> isHired) =>
        bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } && isHired(bot);

    /// <summary>
    /// A hired companion's share of a PvP kill (#571): the players and hired companions of its group in reach, their
    /// damage over the total damage. A hire with no group is counted alone. Other gamebots and helpers don't count.
    /// </summary>
    public static (int Members, double DamageShare) HiredCompanionShare(GameBot hire, IReadOnlyList<GameLiving> roster,
        IReadOnlyDictionary<GameLiving, double> hostileContributors, double totalDamage, Func<GameBot, bool> isHired,
        Func<GameLiving, bool> inReach)
    {
        if (hire == null || totalDamage <= 0)
            return (0, 0);
        IEnumerable<GameLiving> counted = roster == null
            ? new GameLiving[] { hire }
            : roster.Where(member => ReferenceEquals(member, hire) ||
                                     (member is GamePlayer || member is GameBot bot && IsPayableHire(bot, isHired)) &&
                                     inReach(member));
        GameLiving[] members = counted.Distinct().ToArray();
        double damage = members.Sum(member => hostileContributors.GetValueOrDefault(member));
        return (Math.Max(1, members.Length), Math.Min(1.0, Math.Max(0, damage) / totalDamage));
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
        double total = hostile.Sum(pair => pair.Value);
        PayGamebots(killedPlayer, killedPlayer.RealmPointsValue, killedPlayer.RealmLevel, hostile, total);
        PayHiredCompanionExperience(killedPlayer, killedPlayer.ExperienceValue, hostile, total);
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
