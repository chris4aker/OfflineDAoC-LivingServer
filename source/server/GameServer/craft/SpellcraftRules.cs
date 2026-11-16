using System;
using System.Collections.Generic;

namespace DOL.GS.Crafting
{
    /// <summary>Why a spellcraft imbue is refused (SpellCrafting.IsAllowedToCombine's messages, in the order it checks).</summary>
    public enum ImbueRefusal
    {
        None = 0,
        /// <summary>The item already has four bonuses (AlreadyImbued).</summary>
        AlreadyImbued,
        /// <summary>Something in the window is not a spellcraft gem (FalseItem).</summary>
        NotAGem,
        /// <summary>A focus gem on anything but a staff (NotStaff; upstream aa8b5232b).</summary>
        FocusNotOnStaff,
        /// <summary>The same bonus twice (NoSameBonus).</summary>
        SameBonus,
        /// <summary>More than four bonuses in all (DifferentTypes; upstream b62885a86 made the check count right).</summary>
        TooManyBonuses,
        /// <summary>The imbue points exceed the spellcraft skill / 20 (NotEnoughSkill).</summary>
        NotEnoughSkill,
        /// <summary>More than 6 points over the item's capacity (NoMoreLevels).</summary>
        OverchargeTooFar,
    }

    /// <summary>One bonus slot of an item (Bonus1Type/Bonus1 ...), or a gem's Bonus1.</summary>
    public readonly record struct ImbueBonus(int Type, int Value);

    /// <summary>A gem offered to an imbue: its object type, its Bonus1, its quality, and whether Bonus1 is a focus
    /// (SkillBase.CheckPropertyType(..., ePropertyType.Focus), filled in by the caller).</summary>
    public readonly record struct ImbueGem(int ObjectType, int BonusType, int BonusValue, int Quality, bool IsFocus);

    /// <summary>
    /// Spellcraft's numbers as pure functions (design facet F), so a bot spellcrafter and the template-aware valuation
    /// use the player's rules. WP0 copied each body <b>verbatim</b> from <c>SpellCrafting</c> (the Luhz 1.87 rules);
    /// UT_CraftRulesParity checks them. WP1: SpellCrafting delegates every number here, and the combine check is here
    /// as <see cref="CheckImbue"/> with two upstream OpenDAoC fixes by Baptiste Marie (b62885a86: a fifth bonus was
    /// accepted; aa8b5232b: focus gems were not limited to staves), via rambodolphin13's DividedRealms (a3f3e4b8,
    /// 60d3a4fb), GPL-3.0. True165's BotEnchant165 (ported in WP4) uses these instead of its own copy.
    /// </summary>
    public static class SpellcraftRules
    {
        /// <summary>An item holds at most this many bonuses.</summary>
        public const int MaxBonuses = 4;

        /// <summary>Points past the item's capacity the window still accepts (the success chance is 0 beyond +5).</summary>
        public const int MaxOverchargeAccepted = 6;

        /// <summary>
        /// Whether a spellcrafter of <paramref name="spellcraftSkill"/> may imbue these gems into an item
        /// (SpellCrafting.IsAllowedToCombine, gem branch, in its order). <paramref name="itemBonuses"/> is the item's
        /// Bonus1-4 in order; as in the original, a bonus counts only while every earlier one is set.
        /// </summary>
        public static ImbueRefusal CheckImbue(IReadOnlyList<ImbueBonus> itemBonuses, int itemLevel, int itemQuality, bool itemIsStaff,
            IReadOnlyList<ImbueGem> gems, int spellcraftSkill)
        {
            var bonusTypes = new List<int>(8);
            var existing = new List<(int Type, int Value)>(3);
            if (itemBonuses != null)
                foreach (ImbueBonus bonus in itemBonuses)
                {
                    if (bonus.Type == 0)
                        break;
                    if (bonusTypes.Count == MaxBonuses - 1)
                        return ImbueRefusal.AlreadyImbued; // a fourth bonus is set
                    bonusTypes.Add(bonus.Type);
                    existing.Add((bonus.Type, bonus.Value));
                }

            if (gems != null)
                foreach (ImbueGem gem in gems)
                {
                    if (gem.ObjectType != (int)eObjectType.SpellcraftGem)
                        return ImbueRefusal.NotAGem;
                    if (gem.IsFocus && !itemIsStaff)
                        return ImbueRefusal.FocusNotOnStaff;
                    if (bonusTypes.Contains(gem.BonusType))
                        return ImbueRefusal.SameBonus;
                    if (bonusTypes.Count >= MaxBonuses)
                        return ImbueRefusal.TooManyBonuses;
                    bonusTypes.Add(gem.BonusType);
                }

            int bonusLevel = TotalImbuePoints(existing, GemBonuses(gems));
            if (bonusLevel > spellcraftSkill / 20)
                return ImbueRefusal.NotEnoughSkill;

            if (bonusLevel - MaxImbuePoints(itemLevel, itemQuality) > MaxOverchargeAccepted)
                return ImbueRefusal.OverchargeTooFar;

            return ImbueRefusal.None;
        }

        /// <summary>The gems' Bonus1 as TotalImbuePoints takes them.</summary>
        public static IEnumerable<(int Type, int Value)> GemBonuses(IEnumerable<ImbueGem> gems)
        {
            if (gems == null)
                yield break;
            foreach (ImbueGem gem in gems)
                yield return (gem.BonusType, gem.BonusValue);
        }

        /// <summary>The language key SpellCrafting answers a refusal with.</summary>
        public static string RefusalKey(ImbueRefusal refusal) => refusal switch
        {
            ImbueRefusal.AlreadyImbued => "SpellCrafting.IsAllowedToCombine.AlreadyImbued",
            ImbueRefusal.NotAGem => "SpellCrafting.IsAllowedToCombine.FalseItem",
            ImbueRefusal.FocusNotOnStaff => "SpellCrafting.IsAllowedToCombine.NotStaff",
            ImbueRefusal.SameBonus => "SpellCrafting.IsAllowedToCombine.NoSameBonus",
            ImbueRefusal.TooManyBonuses => "SpellCrafting.IsAllowedToCombine.DifferentTypes",
            ImbueRefusal.NotEnoughSkill => "SpellCrafting.IsAllowedToCombine.NotEnoughSkill",
            ImbueRefusal.OverchargeTooFar => "SpellCrafting.IsAllowedToCombine.NoMoreLevels",
            _ => null,
        };

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
