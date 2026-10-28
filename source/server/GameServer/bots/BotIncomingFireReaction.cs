using System;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>What kind of hit drew an autonomous gamebot into a fight.</summary>
public enum BotIncomingFireKind
{
    /// <summary>No reaction rule: not an autonomous gamebot, not a player-shaped attacker, or no hit.</summary>
    None,
    /// <summary>Somebody is swinging at the bot in melee.</summary>
    Melee,
    /// <summary>A single-target spell or arrow aimed at the bot, or an area spell cast from right next to it (a bomber).</summary>
    Direct,
    /// <summary>An area spell from a distance that happened to catch the bot or a mate.</summary>
    Area,
    /// <summary>A group mate was hit by a single-target attack, not the bot itself.</summary>
    Mate,
}

/// <summary>
/// How an autonomous gamebot reacts to a hit from a player-shaped fighter (a player, a bot, or their pets) the way a
/// person does, not a mob: it takes a moment to notice (healers heal and cure meanwhile), an area splash from a
/// distance is threat but not a personal call, and a far attacker does not pull it off another player it is fighting.
/// Hits from mobs keep the immediate reaction. Companions and bots in a player's group follow the player, so they are
/// excluded. Ported from stefanrows/OfflineDAoC 0.271 (BotIncomingFireReaction); the delay is scaled by Living Server's
/// personality Risk trait instead of stefanrows' Aggression, and their group-doctrine "wait for the caller" is left out.
/// </summary>
public static class BotIncomingFireReaction
{
    public const int MaxHoldMilliseconds = 2_500;
    /// <summary>An attacker within the bot's melee range plus this is "on" the bot.</summary>
    public const int MeleePressureReach = 200;
    /// <summary>A point-blank area spell cast within its radius plus this of the bot is a bomber in the group.</summary>
    public const int BomberReach = 100;
    /// <summary>An attacker the bot was not facing takes this much longer to find.</summary>
    public const int UnseenMinMilliseconds = 500;
    public const int UnseenMaxMilliseconds = 1_000;
    public const int FrontArcDegrees = 180;
    /// <summary>With this many dotted group members in reach, a healer casts the group cure first.</summary>
    public const int GroupCureMembers = 3;

    public const int MeleeMinMilliseconds = 300;
    public const int MeleeMaxMilliseconds = 800;
    public const int DirectMinMilliseconds = 500;
    public const int DirectMaxMilliseconds = 1_200;
    public const int AreaMinMilliseconds = 900;
    public const int AreaMaxMilliseconds = 2_200;
    public const int MateMinMilliseconds = 800;
    public const int MateMaxMilliseconds = 1_800;

    // ---- pure rules

    /// <summary>
    /// An autonomous gamebot that no player leads: not a /spawn companion (never IsAutonomousWorldBot), not one hired
    /// into a player's group, not a temporary helper or an epic special force escort.
    /// </summary>
    public static bool AppliesTo(bool enabled, bool autonomous, bool playerLed, bool temporaryHelper, bool epicSpecialForce) =>
        enabled && autonomous && !playerLed && !temporaryHelper && !epicSpecialForce;

    public static bool Applies(GameBot bot) =>
        bot != null && AppliesTo(Properties.BOT_INCOMING_FIRE_REACTION, bot.IsAutonomousWorldBot, bot.IsPlayerLedGroup,
            bot.IsTemporaryGroupHelper, bot.IsEpicSpecialForce);

    /// <summary>A melee swing is melee whatever its proc's radius; an area spell from a bomber in reach is direct.</summary>
    public static BotIncomingFireKind KindOf(AttackData.eAttackType attackType, bool area, bool areaFromReach = false)
    {
        if (attackType == AttackData.eAttackType.Spell)
            return area && !areaFromReach ? BotIncomingFireKind.Area : BotIncomingFireKind.Direct;
        if (attackType == AttackData.eAttackType.Ranged)
            return BotIncomingFireKind.Direct;
        return BotIncomingFireKind.Melee;
    }

    public static bool AreaFromReach(bool pointBlank, double distance, int radius, int meleeReach) =>
        distance <= meleeReach || pointBlank && distance <= radius + BomberReach;

    /// <summary>A mate's area hit probably caught the bot too; any other hit is a mate's.</summary>
    public static BotIncomingFireKind MateKindOf(bool area) => area ? BotIncomingFireKind.Area : BotIncomingFireKind.Mate;

    public static (int Min, int Max) DelayRange(BotIncomingFireKind kind) => kind switch
    {
        BotIncomingFireKind.Melee => (MeleeMinMilliseconds, MeleeMaxMilliseconds),
        BotIncomingFireKind.Direct => (DirectMinMilliseconds, DirectMaxMilliseconds),
        BotIncomingFireKind.Area => (AreaMinMilliseconds, AreaMaxMilliseconds),
        BotIncomingFireKind.Mate => (MateMinMilliseconds, MateMaxMilliseconds),
        _ => (0, 0),
    };

    /// <summary>Classes that only cast: they take a beat longer to turn (stefanrows' "pure caster").</summary>
    public static bool IsPureCaster(eCharacterClass characterClass) => characterClass is
        eCharacterClass.Wizard or eCharacterClass.Theurgist or eCharacterClass.Cabalist or eCharacterClass.Sorcerer or
        eCharacterClass.Necromancer or eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or
        eCharacterClass.Bonedancer or eCharacterClass.Warlock or eCharacterClass.Eldritch or eCharacterClass.Enchanter or
        eCharacterClass.Animist or eCharacterClass.Bainshee or eCharacterClass.Heretic;

    /// <summary>A tank turns first (×0.8), a pure caster takes longer (×1.15), everyone else ×1.</summary>
    public static double RoleFactor(eCharacterClass characterClass) =>
        BotPartyRoles.For(characterClass) == BotPartyRole.Tank ? 0.8 : IsPureCaster(characterClass) ? 1.15 : 1.0;

    /// <summary>
    /// Personality Risk 0 reacts at ×1.3 of the drawn delay, Risk 9 at ×0.75, a straight line between (stefanrows'
    /// Aggression 15-85 scale). Null (personalities off, or none) is ×1.
    /// </summary>
    public static double PersonalityFactor(int? risk) =>
        risk is { } r ? 1.3 - Math.Clamp(r, 0, 9) / 9d * 0.55 : 1.0;

    /// <summary>A draw in the kind's range (<paramref name="roll"/> in [0,1)) scaled by role and personality, plus time to find an unseen attacker, capped.</summary>
    public static int DelayMilliseconds(BotIncomingFireKind kind, double roleFactor, double personalityFactor, double roll,
        bool unseen = false)
    {
        (int min, int max) = DelayRange(kind);
        if (max <= 0)
            return 0;
        double fraction = Math.Clamp(roll, 0, 0.999_999);
        double drawn = (min + fraction * (max - min)) * roleFactor * personalityFactor;
        if (unseen)
            drawn += UnseenMinMilliseconds + fraction * (UnseenMaxMilliseconds - UnseenMinMilliseconds);
        return (int)Math.Min(MaxHoldMilliseconds, Math.Round(drawn));
    }

    /// <summary>A far area splash is never a personal call; every other kind (including None) is.</summary>
    public static bool IsPersonalCall(BotIncomingFireKind kind) => kind != BotIncomingFireKind.Area;

    /// <summary>A direct attacker overrides the kill order only without another player-shaped target, or in melee reach.</summary>
    public static bool OverridesKillOrder(bool hasOtherPlayerTarget, bool attackerInMeleeReach) =>
        !hasOtherPlayerTarget || attackerInMeleeReach;

    /// <summary>The hold runs until <paramref name="holdUntil"/>, unless an attacker is in melee reach.</summary>
    public static bool Holds(long now, long holdUntil, bool attackerInMeleeReach) =>
        holdUntil > 0 && !attackerInMeleeReach && now < holdUntil;

    /// <summary>A healer casts the group cure first when enough members carry a DoT.</summary>
    public static bool PrefersGroupCure(int dottedMembersInReach) => dottedMembersInReach >= GroupCureMembers;

    // ---- live reads

    /// <summary>The player or bot behind a living (a pet resolves to its owner); null for a mob.</summary>
    public static GameLiving Fighter(GameLiving living)
    {
        for (int depth = 0; living != null && depth < 8; depth++)
        {
            if (living is GamePlayer or GameBot)
                return living;
            if (living is not GameNPC { Brain: IControlledBrain pet } || pet.GetLivingOwner() is not GameLiving owner ||
                owner == living)
                return null;
            living = owner;
        }
        return null;
    }

    public static bool IsAreaEffect(AttackData ad) => ad?.SpellHandler?.Spell is { Radius: > 0 };

    public static BotIncomingFireKind KindOf(GameBot bot, AttackData ad)
    {
        if (!Applies(bot) || ad?.Attacker is not GameLiving attacker || attacker == bot ||
            Fighter(attacker) is not { } fighter || fighter == bot)
            return BotIncomingFireKind.None;
        bool area = IsAreaEffect(ad);
        bool fromReach = false;
        if (area && attacker.CurrentRegionID == bot.CurrentRegionID)
        {
            Spell spell = ad.SpellHandler.Spell;
            fromReach = AreaFromReach(spell.Range <= 0 || spell.IsPBAoE, bot.GetDistanceTo(attacker), spell.Radius,
                bot.MeleeAttackRange + MeleePressureReach);
        }
        return KindOf(ad.AttackType, area, fromReach);
    }

    public static BotIncomingFireKind MateKindOf(GameBot bot, GameLiving attacker, bool area) =>
        Applies(bot) && attacker != null && attacker != bot && Fighter(attacker) is { } fighter && fighter != bot
            ? MateKindOf(area) : BotIncomingFireKind.None;

    public static int? PersonalityRisk(GameBot bot) =>
        AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot).Traits.Risk : null;

    public static int Draw(GameBot bot, BotIncomingFireKind kind, GameLiving attacker)
    {
        if (kind == BotIncomingFireKind.None || bot?.CharacterClass == null)
            return 0;
        bool unseen = attacker != null && attacker.CurrentRegionID == bot.CurrentRegionID &&
                      !bot.IsObjectInFront(attacker, FrontArcDegrees);
        return DelayMilliseconds(kind, RoleFactor((eCharacterClass)bot.CharacterClass.ID),
            PersonalityFactor(PersonalityRisk(bot)), Util.RandomDouble(), unseen);
    }

    public static bool InMeleeReach(GameLiving bot, GameLiving attacker) =>
        bot != null && attacker != null && attacker.CurrentRegionID == bot.CurrentRegionID &&
        bot.IsWithinRadius(attacker, bot.MeleeAttackRange + MeleePressureReach);
}
