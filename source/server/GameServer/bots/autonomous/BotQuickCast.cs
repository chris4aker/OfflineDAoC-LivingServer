using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.SkillHandler;

namespace DOL.GS;

/// <summary>
/// #531 step 2d: Quickcast for autonomous gamebots in the open field, on one key cast under interrupt (reference.md 3.5:
/// "one important cast (a mez or a big heal) through interrupts"). A key cast is a mez or the bot's big heal. Quickcast is
/// used only when an enemy's interrupt would cost that cast, with the player's rules: the cast takes two seconds and
/// can't be interrupted, it costs double power, Quickcast can't be used in melee, and it waits 30 seconds after a use
/// (<see cref="QuickCastAbilityHandler.DISABLE_DURATION"/>). Every other bot cast, and every companion, keeps Quickcast
/// off as before (GameBot.CastSpell ends a Quickcast it did not arm).
/// </summary>
public static class BotQuickCast
{
    /// <summary>Pure: the spell is a key cast (a mez, or the bot's big heal).</summary>
    public static bool IsKeyCast(bool mez, bool bigHeal) => mez || bigHeal;

    /// <summary>
    /// Pure: Quickcast carries this cast. The bot may (an open-field gamebot with the Quickcast ability), it is a key cast
    /// with a cast time, an enemy's interrupt would cost it, the bot is not swinging, Quickcast's 30 s wait is over, and
    /// the bot has the double power.
    /// </summary>
    public static bool Carries(bool mayQuickCast, bool keyCast, bool hasCastTime, bool interruptWouldCostIt, bool inMeleeAttack,
        long sinceLastQuickCastMs, int mana, int doublePowerCost) =>
        mayQuickCast && keyCast && hasCastTime && interruptWouldCostIt && !inMeleeAttack &&
        sinceLastQuickCastMs >= QuickCastAbilityHandler.DISABLE_DURATION && mana >= doublePowerCost;

    private sealed class Armed
    {
        public int SpellId = -1;
    }

    private static readonly ConditionalWeakTable<GameBot, Armed> ArmedFor = new();

    private static bool MayQuickCast(GameBot bot) =>
        bot != null && bot.IsAlive && BotBrain.RvrTacticsApplyTo(bot) && bot.HasAbility(Abilities.Quickcast) &&
        RvrFightBoards.Peek(bot) != null;

    private static bool IsKeyCast(GameBot bot, Spell spell) =>
        IsKeyCast(spell.SpellType == eSpellType.Mesmerize, bot.HealBig != null && bot.HealBig.ID == spell.ID);

    private static long SinceLast(GameBot bot)
    {
        long last = bot.TempProperties.GetProperty<long>(GamePlayer.QUICK_CAST_CHANGE_TICK);
        return last <= 0 || bot.CurrentRegion == null ? long.MaxValue : bot.CurrentRegion.Time - last;
    }

    /// <summary>Whether Quickcast would carry this cast now (reads the game; no change).</summary>
    public static bool WouldCarry(GameBot bot, Spell spell)
    {
        if (spell == null || spell.IsInstantCast || spell.CastTime <= 0 || !MayQuickCast(bot) || !IsKeyCast(bot, spell))
            return false;
        int castTime = BotInterruptPolicy.EstimatedCastTime(bot, spell);
        return Carries(true, true, true, bot.InterruptWouldStopCast(castTime), bot.attackComponent?.AttackState == true,
            SinceLast(bot), bot.Mana, 2 * bot.PowerCost(spell));
    }

    /// <summary>
    /// Called by GameBot.CastSpell before the cast: when Quickcast carries it, the bot's Quickcast effect is started and
    /// armed for this spell, and true is returned. Otherwise false (and the caller ends any Quickcast as before).
    /// </summary>
    public static bool TryArm(GameBot bot, Spell spell)
    {
        Armed armed = ArmedFor.GetOrCreateValue(bot);
        armed.SpellId = -1;
        if (!WouldCarry(bot, spell))
            return false;
        if (EffectListService.GetAbilityEffectOnTarget(bot, eEffect.QuickCast) == null)
            ECSGameEffectFactory.Create(new(bot, QuickCastECSGameEffect.DURATION, 1), static (in ECSGameEffectInitParams i) => new QuickCastECSGameEffect(i));
        armed.SpellId = spell.ID;
        return true;
    }

    /// <summary>Tests only: arm this spell as <see cref="TryArm"/> does once Quickcast carries it (the effect is the caller's).</summary>
    public static void ArmForTest(GameBot bot, Spell spell) => ArmedFor.GetOrCreateValue(bot).SpellId = spell.ID;

    /// <summary>Whether the spell handler may take the bot's Quickcast for this spell (armed by <see cref="TryArm"/>).</summary>
    public static bool IsArmed(GameBot bot, Spell spell) =>
        bot != null && spell != null && ArmedFor.TryGetValue(bot, out Armed armed) && armed.SpellId == spell.ID;

    /// <summary>After a Quickcast cast: the 30 s wait starts, as for a player (logged as BOT_REALM_ABILITY_USED ability=QuickCast).</summary>
    public static void Spent(GameBot bot)
    {
        bot.TempProperties.SetProperty(GamePlayer.QUICK_CAST_CHANGE_TICK, bot.CurrentRegion?.Time ?? 0);
        if (bot.GetAbility(Abilities.Quickcast) is { } ability)
            bot.DisableSkill(ability, QuickCastAbilityHandler.DISABLE_DURATION);
        if (ArmedFor.TryGetValue(bot, out Armed armed))
            armed.SpellId = -1;
        if (log.IsInfoEnabled)
            log.Info($"BOT_REALM_ABILITY_USED bot={bot.Name} ability=QuickCast health={bot.HealthPercent} endurance={bot.EndurancePercent} " +
                $"region={bot.CurrentRegionID}");
    }

    private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(BotQuickCast));
}
