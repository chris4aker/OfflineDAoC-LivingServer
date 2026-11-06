using System;

namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The power regen rate calculator
    /// 
    /// BuffBonusCategory1 is used for all buffs
    /// BuffBonusCategory2 is used for all debuffs (positive values expected here)
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.PowerRegenerationAmount)]
    public class PowerRegenerationAmountCalculator : PropertyCalculator
    {
        public PowerRegenerationAmountCalculator() { }

        public override int CalcValue(GameLiving living, eProperty property)
        {
            /* PATCH 1.87 COMBAT AND REGENERATION
              - While in combat, health and power regeneration ticks will happen twice as often.
              - Each tick of health and power is now twice as effective.
              - All health and power regeneration aids are now twice as effective.
             */

            // Reverted 1.87 changes.
            // From DoL's `5 + (living.Level / 2.75)`.
            // 12 power per tick at level 50 instead of 23.18.
            double regen = 2.5 + living.Level * 0.2;
            int debuff = living.SpecBuffBonusCategory[property];

            if (debuff < 0)
                debuff = -debuff;

            regen += living.BaseBuffBonusCategory[property] + living.AbilityBonus[property] + living.ItemBonus[property] - debuff;

            // One rule for real players and GameBots (autonomous gamebots and /spawn companions), owner 2026-11-03: a list
            // caster below half power regenerates at half speed only while mana_regen_amount_halved_below_50_percent is on
            // (off by default in the Living Server). Bots used to be exempt while players were halved.
            bool listCasterBelowHalf = living switch
            {
                GamePlayer player => IsListCasterBelowHalf(player.CharacterClass, player.ManaPercent),
                GameBot bot => IsListCasterBelowHalf(bot.CharacterClass, bot.ManaPercent),
                _ => false,
            };
            if (ServerProperties.Properties.MANA_REGEN_AMOUNT_HALVED_BELOW_50_PERCENT && listCasterBelowHalf)
            {
                regen /= 2;
            }

            regen *= ServerProperties.Properties.MANA_REGEN_AMOUNT_MODIFIER;
            return Math.Max(1, (int) regen);
        }

        /// <summary>The classic penalty's condition, the same for a player and a bot: a list caster below 50% power.</summary>
        public static bool IsListCasterBelowHalf(ICharacterClass characterClass, int manaPercent) =>
            characterClass?.ClassType is eClassType.ListCaster && manaPercent < 50;
    }
}
