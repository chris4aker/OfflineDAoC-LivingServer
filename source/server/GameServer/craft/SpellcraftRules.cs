using System;
using System.Collections.Generic;

namespace DOL.GS.Crafting
{
    /// <summary>
    /// Spellcraft's numbers as pure functions (design facet F), so a bot spellcrafter and the template-aware valuation
    /// use the player's rules. WP0: each body is copied <b>verbatim</b> from today's <c>SpellCrafting</c> (the Luhz 1.87
    /// rules); UT_SpellcraftRulesParity checks them against the originals. Nothing calls these yet; WP1 rewires
    /// SpellCrafting to delegate here. True165's BotEnchant165 (ported in WP4) uses these instead of its own copy.
    /// </summary>
    public static class SpellcraftRules
    {
        private static readonly int[] OCStartPercentages = { 0, 10, 20, 30, 50, 70 };
        private static readonly int[] ItemQualOCModifiers = { 0, 0, 6, 8, 10, 18, 26 };

        /// <summary>The imbue points an item of this level and quality holds (SpellCrafting.GetItemMaxImbuePoints): at
        /// level 51, 100% = 32, 99% = 28, 96% = 18.</summary>
        public static int MaxImbuePoints(int itemLevel, int quality)
        {
            if (itemLevel < 1) return 0;
            // Luhz Crafting Update:
            // All items have MP level imbue points

            if (quality == 100)
                return (int)Math.Round(0.625 * itemLevel);
            if (quality == 99)
                return (int)Math.Round(0.55 * itemLevel);
            if (quality == 98)
                return (int)Math.Round(0.475 * itemLevel);
            if (quality == 97)
                return (int)Math.Round(0.42 * itemLevel);
            if (quality == 96)
                return (int)Math.Round(0.353 * itemLevel);
            if (quality == 95)
                return (int)Math.Round(0.295 * itemLevel);

            return (int)Math.Round(0.2 * itemLevel);
        }

        /// <summary>The imbue points one bonus costs (SpellCrafting.GetGemImbuePoints): stats, mana, hits, resists, skills,
        /// focus; at least 1.</summary>
        public static int GemImbuePoints(int bonusType, int bonusValue)
        {
            int gemBonus;
            if (bonusType <= (int)eProperty.Stat_Last) gemBonus = (int)(((bonusValue - 1) * 2 / 3) + 1); //stat
            else if (bonusType == (int)eProperty.MaxMana) gemBonus = (int)((bonusValue * 2) - 2); //mana
            else if (bonusType == (int)eProperty.MaxHealth) gemBonus = (int)(bonusValue / 4); //HP
            else if (bonusType <= (int)eProperty.Resist_Last) gemBonus = (int)((bonusValue * 2) - 2);//resist
            else if (bonusType <= (int)eProperty.Skill_Last) gemBonus = (int)((bonusValue - 1) * 5);//skill
            else gemBonus = 1;// focus
            if (gemBonus < 1) gemBonus = 1;

            return gemBonus;
        }

        /// <summary>
        /// The imbue points an item's existing bonuses plus the gems use (SpellCrafting.GetTotalImbuePoints): the sum of
        /// every bonus's points plus the dearest one again, halved. <paramref name="existing"/> is the item's Bonus1-3 in
        /// order; counting stops at the first with type 0, as the original's nesting does. Each gem counts its Bonus1.
        /// </summary>
        public static int TotalImbuePoints(IEnumerable<(int Type, int Value)> existing, IEnumerable<(int Type, int Value)> gems)
        {
            int totalGemBonus = 0;
            int biggerGemCharge = 0;

            if (existing != null)
            {
                int counted = 0;
                foreach ((int type, int value) in existing)
                {
                    if (type == 0 || counted == 3)
                        break;
                    int currentBonusImbuePoint = GemImbuePoints(type, value);
                    totalGemBonus += currentBonusImbuePoint;
                    if (currentBonusImbuePoint > biggerGemCharge) biggerGemCharge = currentBonusImbuePoint;
                    counted++;
                }
            }

            if (gems != null)
                foreach ((int type, int value) in gems)
                {
                    int currentGemCharge = GemImbuePoints(type, value);
                    if (currentGemCharge > biggerGemCharge) biggerGemCharge = currentGemCharge;

                    totalGemBonus += currentGemCharge;
                }
            totalGemBonus += biggerGemCharge;
            totalGemBonus /= 2;

            return (int)Math.Floor((double)totalGemBonus);
        }

        /// <summary>
        /// Percent chance an imbue at <paramref name="bonus"/> points on an item that holds <paramref name="maxBonus"/>
        /// succeeds (SpellCrafting.CalculateChanceToOverchargeItem, Kort's 1.87 calculator): 100 when not overcharged, 0
        /// beyond +5, otherwise by item quality, the overcharge and the spellcraft skill. A quality outside 94-100 gets
        /// 100, as the original's caught array exception does.
        /// </summary>
        public static int ChanceToOvercharge(int itemQuality, int spellcraftSkill, int maxBonus, int bonus)
        {
            if (bonus - maxBonus > 5) return 0;
            if (bonus - maxBonus < 0) return 100;

            int qualityIndex = itemQuality - 94;
            if (qualityIndex < 0 || qualityIndex >= ItemQualOCModifiers.Length)
                return 100;
            int success = 34 + ItemQualOCModifiers[qualityIndex];
            success -= OCStartPercentages[bonus - maxBonus];
            int skillbonus = (spellcraftSkill / 10);
            if (skillbonus > 100)
                skillbonus = 100;
            success += skillbonus;
            int fudgefactor = (int)(100.0 * ((skillbonus / 100.0 - 1.0) * (OCStartPercentages[bonus - maxBonus] / 200.0)));
            success += fudgefactor;
            if (success > 100)
                success = 100;
            return success;
        }

        /// <summary>The overcharge penalty in percent (SpellCrafting.GetOverchargePenality): -10/-20/-30/-50, else -70.</summary>
        public static int OverchargePenalty(int maxBonus, int bonus)
        {
            int diff = bonus - maxBonus;
            if (diff == 1) return -10;
            else if (diff == 2) return -20;
            else if (diff == 3) return -30;
            else if (diff == 4) return -50;
            else return -70;
        }

        /// <summary>
        /// Percent chance a failed overcharge keeps the item (SpellCrafting.CalculateChanceToPreserveItem): the skill's
        /// step (-50 below 50 up to +50 at 1000) plus 1.8 per gem quality point above 92, scaled by the item's quality.
        /// </summary>
        public static int ChanceToPreserve(int itemQuality, IEnumerable<int> gemQualities, int spellcraftSkill)
        {
            int baseChance = 0;

            double gemModifier = 0;
            if (gemQualities != null)
                foreach (int gemQuality in gemQualities)
                    gemModifier += (gemQuality - 92) * 1.8;

            gemModifier = gemModifier * itemQuality / 100;

            int intSkill = spellcraftSkill;
            int intSkillMod;
            if (intSkill < 50) { intSkillMod = -50; }
            else if (intSkill < 100) { intSkillMod = -45; }
            else if (intSkill < 150) { intSkillMod = -40; }
            else if (intSkill < 200) { intSkillMod = -35; }
            else if (intSkill < 250) { intSkillMod = -30; }
            else if (intSkill < 300) { intSkillMod = -25; }
            else if (intSkill < 350) { intSkillMod = -20; }
            else if (intSkill < 400) { intSkillMod = -15; }
            else if (intSkill < 450) { intSkillMod = -10; }
            else if (intSkill < 500) { intSkillMod = -5; }
            else if (intSkill < 550) { intSkillMod = 0; }
            else if (intSkill < 600) { intSkillMod = 5; }
            else if (intSkill < 650) { intSkillMod = 10; }
            else if (intSkill < 700) { intSkillMod = 15; }
            else if (intSkill < 750) { intSkillMod = 20; }
            else if (intSkill < 800) { intSkillMod = 25; }
            else if (intSkill < 850) { intSkillMod = 30; }
            else if (intSkill < 900) { intSkillMod = 35; }
            else if (intSkill < 950) { intSkillMod = 40; }
            else if (intSkill < 1000) { intSkillMod = 45; }
            else { intSkillMod = 50; }

            int finalChances = (int)(intSkillMod + gemModifier + baseChance);
            if (finalChances > 100) finalChances = 100;
            else if (finalChances < 0) finalChances = 0;

            return finalChances;
        }
    }
}
