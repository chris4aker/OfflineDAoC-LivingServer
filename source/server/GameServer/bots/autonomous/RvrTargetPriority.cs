using System;

namespace DOL.GS;

/// <summary>
/// Where an enemy class stands in the 2003 kill order (docs/RVR-FIELD-1.2.md, "Mind"; ERA-RESEARCH.md section 2):
/// "casters, healers, PBT casters, CC classes, tanks last" ("anything that wears a dress and moves their arms about").
/// </summary>
public enum RvrKillRole : byte
{
    /// <summary>A cloth caster (RvrRole.Caster), whatever its line.</summary>
    Caster,
    /// <summary>A main healer: Cleric, Druid, Healer.</summary>
    Healer,
    /// <summary>
    /// A bladeturn caster that is not already a cloth caster. In this server's spell data the only such class at level 50
    /// or lower is the Warden (the cloth casters with a bladeturn rank as casters).
    /// </summary>
    Bladeturn,
    /// <summary>A hybrid whose job is crowd control: Minstrel, Bard, Skald, Shaman (mez, stun or root in the spell data).</summary>
    CrowdControl,
    /// <summary>Other hybrid support (Friar).</summary>
    Support,
    Archer,
    Stealther,
    Melee,
    /// <summary>Last: "tanks last".</summary>
    Tank,
    Pet,
    Unknown,
}

/// <summary>
/// The kill order as numbers (work package 2, pure): a base score per kill role, the realm variants of the playbooks,
/// the switch triggers (a caster seen using Mastery of Concentration, an enemy just resurrected), and the human errors
/// of the caller: scores wobble by about 20% (by skill tier) and 5-15% of calls land on a tank. The order is sourced
/// (FH Jan/Aug 2003, Uthgard 2011); every number here is our design value, as no source gives weights.
/// </summary>
public static class RvrTargetPriority
{
    /// <summary>What a rising enemy (Mastery of Concentration, just resurrected) adds: it tops any kill order.</summary>
    public const double RisingBonus = 200;
    /// <summary>What an enemy that is fighting the group adds (it is in the fight, not just near it).</summary>
    public const double EngagedBonus = 5;
    /// <summary>Per point of missing health: a wounded enemy is a little more attractive (at most +15).</summary>
    public const double WoundBonusPerPoint = 0.15;

    public static RvrKillRole KillRole(eCharacterClass characterClass, RvrRole role)
    {
        switch (characterClass)
        {
            case eCharacterClass.Warden:
                return RvrKillRole.Bladeturn;
            case eCharacterClass.Minstrel or eCharacterClass.Bard or eCharacterClass.Skald or eCharacterClass.Shaman:
                return RvrKillRole.CrowdControl;
        }
        return role switch
        {
            RvrRole.Caster => RvrKillRole.Caster,
            RvrRole.Healer => RvrKillRole.Healer,
            RvrRole.Support => RvrKillRole.Support,
            RvrRole.Archer => RvrKillRole.Archer,
            RvrRole.Stealther => RvrKillRole.Stealther,
            RvrRole.Melee => RvrKillRole.Melee,
            RvrRole.Tank => RvrKillRole.Tank,
            RvrRole.Pet => RvrKillRole.Pet,
            _ => RvrKillRole.Unknown,
        };
    }

    public static RvrKillRole KillRole(RvrCombatant enemy) =>
        enemy == null ? RvrKillRole.Unknown : enemy.IsPet ? RvrKillRole.Pet : KillRole(enemy.Class, enemy.Role);

    /// <summary>The general order (versus Albion, and for every class a realm variant does not name).</summary>
    public static double BaseScore(RvrKillRole role) => role switch
    {
        RvrKillRole.Caster => 100,
        RvrKillRole.Healer => 90,
        RvrKillRole.Bladeturn => 80,
        RvrKillRole.CrowdControl => 70,
        RvrKillRole.Support => 60,
        RvrKillRole.Archer => 55,
        RvrKillRole.Stealther => 50,
        RvrKillRole.Melee => 40,
        RvrKillRole.Unknown => 35,
        RvrKillRole.Tank => 20,
        _ => 5,
    };

    /// <summary>
    /// The score of a class by the realm it belongs to (the playbooks, "How its enemies killed it"):
    /// versus Midgard "mezz the healers, kill the shaman and then kill the tanks" (era-dated): the Shaman first and the
    /// Healers below the casters (they are the crowd controllers' work); versus Hibernia the Bard first, then the
    /// Eldritch, then the Druids (retrospective); versus Albion no order was found, so the general one applies.
    /// </summary>
    public static double RealmScore(eCharacterClass characterClass, RvrKillRole role, eRealm enemyRealm)
    {
        switch (enemyRealm)
        {
            case eRealm.Midgard when characterClass == eCharacterClass.Shaman:
                return 130;
            case eRealm.Midgard when characterClass == eCharacterClass.Healer:
                return 85;
            case eRealm.Hibernia when characterClass == eCharacterClass.Bard:
                return 130;
            case eRealm.Hibernia when characterClass == eCharacterClass.Eldritch:
                return 120;
            case eRealm.Hibernia when characterClass == eCharacterClass.Druid:
                return 110;
        }
        return BaseScore(role);
    }

    /// <summary>The score of an enemy before the caller's wobble: kill order, realm variant, rising, fighting us, wounds. Pure.</summary>
    public static double Score(RvrCombatant enemy, in RvrEnemyFacts facts)
    {
        if (enemy == null)
            return 0;
        RvrKillRole role = KillRole(enemy);
        double score = enemy.IsPet ? BaseScore(RvrKillRole.Pet) : RealmScore(enemy.Class, role, enemy.Realm);
        if (facts.Rising)
            score += RisingBonus;
        if (enemy.TargetsGroup)
            score += EngagedBonus;
        score += Math.Clamp(100 - enemy.HealthPercent, 0, 100) * WoundBonusPerPoint;
        return score;
    }

    /// <summary>How far a caller's scores wobble either way (about 20%, by tier): Veteran 12%, Average 20%, Casual 28%.</summary>
    public static double Wobble(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => 0.12,
        RvrSkillTier.Casual => 0.28,
        _ => 0.20,
    };

    /// <summary>The share of calls that land on a tank when one is in reach: Veteran 5%, Average 10%, Casual 15%.</summary>
    public static double WrongCallShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => 0.05,
        RvrSkillTier.Casual => 0.15,
        _ => 0.10,
    };

    /// <summary>The wobble factor (1 ± <see cref="Wobble"/>) for one enemy in one call, from a 0-1 roll. Pure.</summary>
    public static double Jitter(RvrSkillTier tier, double roll) => 1 + Wobble(tier) * (2 * Math.Clamp(roll, 0, 1) - 1);

    /// <summary>
    /// A class that casts for its living, so an interrupt stops something: the cloth casters, the main healers, and the
    /// hybrids that cast more than they swing (Shaman, Bard, Friar, Warden). Pure.
    /// </summary>
    public static bool IsSpellcaster(eCharacterClass characterClass, RvrRole role) =>
        role is RvrRole.Caster or RvrRole.Healer ||
        characterClass is eCharacterClass.Shaman or eCharacterClass.Bard or eCharacterClass.Friar or eCharacterClass.Warden;

    /// <summary>
    /// Whom a mezzer or stunner holds first (not the kill target): the healers ("mezz the healers"), then the casters,
    /// the crowd controllers and support, then archers and melee, tanks last; an enemy fighting the group comes a little
    /// earlier. Versus Midgard the Healers rank highest of all. Pure.
    /// </summary>
    public static double HoldScore(RvrCombatant enemy)
    {
        if (enemy == null)
            return 0;
        double score = KillRole(enemy) switch
        {
            RvrKillRole.Healer => enemy.Realm == eRealm.Midgard ? 120 : 100,
            RvrKillRole.Caster => 90,
            RvrKillRole.CrowdControl => 85,
            RvrKillRole.Bladeturn => 80,
            RvrKillRole.Support => 75,
            RvrKillRole.Archer => 60,
            RvrKillRole.Stealther => 55,
            RvrKillRole.Melee => 50,
            RvrKillRole.Tank => 40,
            RvrKillRole.Unknown => 30,
            _ => 5,
        };
        return enemy.TargetsGroup ? score + 10 : score;
    }

    /// <summary>Whom a rooter holds first: the melee coming at the group, tanks, stealthers; casters last. Pure.</summary>
    public static double RootScore(RvrCombatant enemy)
    {
        if (enemy == null)
            return 0;
        double score = KillRole(enemy) switch
        {
            RvrKillRole.Melee => 100,
            RvrKillRole.Tank => 90,
            RvrKillRole.Stealther => 85,
            RvrKillRole.Support or RvrKillRole.CrowdControl or RvrKillRole.Bladeturn => 60,
            RvrKillRole.Archer => 50,
            RvrKillRole.Pet => 5,
            _ => 40,
        };
        return enemy.TargetsGroup ? score + 20 : score;
    }
}
