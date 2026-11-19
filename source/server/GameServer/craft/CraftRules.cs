using System;
using System.Collections.Generic;

namespace DOL.GS.Crafting
{
    /// <summary>A recipe's colour to a crafter of some skill (AbstractCraftingSkill.GetItemCon's -3 to 3).</summary>
    public enum CraftCon
    {
        Grey = -3,
        Green = -2,
        Blue = -1,
        Yellow = 0,
        Orange = 1,
        Red = 2,
        Purple = 3,
    }

    /// <summary>
    /// What a craft's clock and skill gain depend on besides the recipe, so the same rules serve a player and a gamebot.
    /// A player's caller fills it from <c>GamePlayer</c> (CraftingSpeed, CraftingSkillBonus, CurrentRegion.IsCapitalCity,
    /// its realm's relics and keep bonuses, max_craft_time); a gamebot's caller from its own record.
    /// </summary>
    /// <param name="Skill">The crafter's value in the recipe's skill.</param>
    /// <param name="RecipeLevel">The recipe's crafting level.</param>
    /// <param name="InCapital">The crafter stands in a capital city (the 1.86 gain bonus applies).</param>
    /// <param name="CapitalGainBonus">The points added to the gain chance in a capital (GamePlayer.CraftingSkillBonus).</param>
    /// <param name="SpeedMultiplier">The crafting speed the time is divided by (GamePlayer.CraftingSpeed; 1 = none).</param>
    /// <param name="RealmRelics">Relics the crafter's realm holds (RelicMgr.GetRelicCount): -5% time each.</param>
    /// <param name="KeepTimerDivisor">1.05 or 1.03 with the realm's keep crafting bonus (KeepBonusMgr), else 1.</param>
    /// <param name="MaxCraftTimeSeconds">The max_craft_time server property; 0 = no cap.</param>
    /// <param name="InstantGm">A GM crafts instantly (GetCraftingTime returns 0).</param>
    public readonly record struct CraftContext(int Skill, int RecipeLevel, bool InCapital = false, int CapitalGainBonus = 0,
        double SpeedMultiplier = 1.0, int RealmRelics = 0, double KeepTimerDivisor = 1.0, int MaxCraftTimeSeconds = 0,
        bool InstantGm = false);

    /// <summary>
    /// The craft's numbers as pure functions of a skill value, so players and gamebots roll by the same rules (design
    /// docs/CRAFTING-AND-ECONOMY.md facet C). WP0 (Crafting 2.0 foundation) copied each body <b>verbatim</b> from
    /// <c>AbstractCraftingSkill</c> (and the professions' secondary-skill rules); UT_CraftRulesParity checks them. WP1:
    /// AbstractCraftingSkill, the professions, CraftAction, Salvage and Repair now delegate every number here, and the
    /// optional rules live here too (classic quality, classic losses, the player pace). The same split as
    /// rambodolphin13's DividedRealms refactor (ChanceToMakeAt, BaseChanceToGainAt, QualityAt, CraftingTimeAt in
    /// 93c25b69), here as static functions so the rehearsal can run them without a server. Thread-safe: no state.
    /// </summary>
    public static class CraftRules
    {
        /// <summary>Secondary skills stop here (AbstractCraftingSkill.subSkillCap).</summary>
        public const int SubSkillCap = 1300;

        /// <summary>The recipe's colour to this skill (AbstractCraftingSkill.GetItemCon, verbatim).</summary>
        public static CraftCon Con(int crafterSkill, int itemCraftingLevel)
        {
            int diff = itemCraftingLevel - crafterSkill;
            if (diff <= -50)
                return CraftCon.Grey;
            else if (diff <= -31)
                return CraftCon.Green;
            else if (diff <= -11)
                return CraftCon.Blue;
            else if (diff <= 0)
                return CraftCon.Yellow;
            else if (diff <= 19)
                return CraftCon.Orange;
            else if (diff <= 49)
                return CraftCon.Red;
            else
                return CraftCon.Purple;
        }

        /// <summary>Percent chance to make the item (AbstractCraftingSkill.CalculateChanceToMakeItem): 100/100/100/92/84/68/0.</summary>
        public static int ChanceToMake(int skill, int craftingLevel)
        {
            int con = (int)Con(skill, craftingLevel);
            if (con < -3)
                con = -3;
            if (con > 3)
                con = 3;

            switch (con)
            {
                // Chance to MAKE ! (100 - chance to fail)
                case -3:
                    return 100;
                case -2:
                    return 100;
                case -1:
                    return 100;
                case 0:
                    return 100 - 8;
                case 1:
                    return 100 - 16;
                case 2:
                    return 100 - 32;
                case 3:
                    return 0;
                default:
                    return 0;
            }
        }

        /// <summary>Percent chance to gain a point before a capital's bonus: 0/15/30/45/55/65/0 (DR's BaseChanceToGainAt).</summary>
        public static int BaseChanceToGainPoint(int skill, int recipeLevel)
        {
            int con = (int)Con(skill, recipeLevel);
            if (con < -3)
                con = -3;
            if (con > 3)
                con = 3;

            switch (con)
            {
                case -3:
                    return 0;
                case -2:
                    return 15;
                case -1:
                    return 30;
                case 0:
                    return 45;
                case 1:
                    return 55;
                case 2:
                    return 65;
                case 3:
                    return 0;
                default:
                    return 0;
            }
        }

        /// <summary>Percent chance to gain a point (AbstractCraftingSkill.CalculateChanceToGainPoint): the base chance, plus
        /// the capital bonus in a capital city (patch 1.86), kept within 0-100. Grey and purple give nothing anywhere.</summary>
        public static int ChanceToGainPoint(in CraftContext context)
        {
            int chance = BaseChanceToGainPoint(context.Skill, context.RecipeLevel);
            if (chance == 0)
                return 0;

            // In capital cities bonuses to crafting apply (patch 1.86)
            if (context.InCapital)
            {
                chance += context.CapitalGainBonus;

                if (chance < 0)
                    chance = 0;
                if (chance > 100)
                    chance = 100;
            }

            return chance;
        }

        /// <summary>
        /// Seconds one craft takes (AbstractCraftingSkill.GetCraftingTime, verbatim, every integer truncation in the same
        /// place): (floor(level / 100) + 1) x materials / 4, / speed, -5% per relic, / the keep bonus, x the con factor
        /// (grey 0.4, green 0.6, blue 0.8), at least 1 and at most max_craft_time when set; 0 for a GM.
        /// </summary>
        /// <param name="ingredientCounts">Each ingredient's count, in recipe order (a count over 100 counts a tenth).</param>
        public static int CraftingTimeSeconds(in CraftContext context, IReadOnlyList<int> ingredientCounts)
        {
            if (context.InstantGm)
                return 0;

            double baseMultiplier = (context.RecipeLevel / 100) + 1;
            if (baseMultiplier < 1) baseMultiplier = 1;

            ushort materialsCount = 0;
            if (ingredientCounts != null)
                foreach (int count in ingredientCounts)
                {
                    var countMod = count;
                    if (countMod > 100) countMod /= 10;
                    materialsCount += (ushort)countMod;
                }

            var divisorMod = 4;
            int craftingTime = (int)(baseMultiplier * materialsCount / divisorMod);

            // Player does check for capital city bonus as well
            craftingTime = (int)(craftingTime / context.SpeedMultiplier);

            craftingTime = (int)(craftingTime * (1 - (.05 * context.RealmRelics)));

            //keep bonuses reduction in crafting time
            if (context.KeepTimerDivisor > 1.0)
                craftingTime = (int)(craftingTime / context.KeepTimerDivisor);

            int con = (int)Con(context.Skill, context.RecipeLevel);
            double mod = 1.0;
            switch (con)
            {
                case -3:
                    mod = 0.4;
                    break;
                case -2:
                    mod = 0.6;
                    break;
                case -1:
                    mod = 0.8;
                    break;
                case 0:
                    mod = 1.0;
                    break;
                case 1:
                    mod = 1.0;
                    break;
                case 2:
                    mod = 1.0;
                    break;
                case 3:
                    mod = 1.0;
                    break;
            }

            craftingTime = (int)(craftingTime * mod);

            var maxCraftingTime = context.MaxCraftTimeSeconds;

            if (craftingTime < 1)
                craftingTime = 1;
            else if (maxCraftingTime > 0 && craftingTime > maxCraftingTime)
                craftingTime = maxCraftingTime;

            return craftingTime;
        }

        /// <summary>The keep bonus divisor the time formula uses: 1.05 with Craft_Timers_5, 1.03 with Craft_Timers_3, else 1.</summary>
        public static double KeepTimerDivisor(bool craftTimers5, bool craftTimers3) =>
            craftTimers5 ? 1.05 : craftTimers3 ? 1.03 : 1.0;

        /// <summary>
        /// The crafted item's quality (AbstractCraftingSkill.GetQuality, verbatim, the 1.87 rules): at skill 1000 or on a
        /// grey recipe 96-99 with a 2% masterpiece (100); otherwise a roulette of 96-99 leaning to 96 as the recipe gets
        /// harder, never a masterpiece. <paramref name="random"/> is drawn exactly as Util.Chance and Util.Random draw
        /// (Chance(p) = p &gt; Next(100); Random(max) = Next(0, max + 1)), so a seeded Random gives a repeatable roll.
        /// </summary>
        public static int RollQuality(int skill, int recipeLevel, Random random)
        {
            random ??= Random.Shared;
            bool Chance(int percent) => percent > random.Next(100);
            int Roll(int max) => random.Next(0, max + 1);

            // 2% chance to get masterpiece, 1:6 chance to get 94-99%, if legendary or if grey con
            // otherwise moving the most load towards 94%, the higher the item con to the crafter skill
            //1.87 patch raises min quality to 96%

            // legendary
            if (skill >= 1000)
            {
                if (Chance(2))
                {
                    return 100; // 2% chance for master piece
                }
                return 96 + Roll(3);
            }

            int delta = (int)Con(skill, recipeLevel);
            if (delta < -2)
            {
                if (Chance(2))
                    return 100; // grey items get 2% chance to be master piece
                return 96 + Roll(3); // handle grey items like legendary
            }

            // this is a type of roulette selection, imagine a roulette wheel where all chances get different sized
            // fields where the ball can drop, the bigger the field, the bigger the chance
            // field size is modulated by item con and can be also 0

            delta = delta * 100;

            int[] chancePart = new int[4]; // index ranges from 96%(0) to 99%(5)
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                chancePart[i] = Math.Max((4 - i) * 100 - delta, 0); // 0 minimum
                sum += chancePart[i];
            }

            // selection
            int rand = Roll(sum);
            for (int i = 3; i >= 0; i--)
            {
                if (rand < chancePart[i])
                    return 96 + i;
                rand -= chancePart[i];
            }

            return 96;
        }

        /// <summary>The masterpiece chance of <see cref="RollClassicQuality"/> below skill 1000 on a coloured recipe.</summary>
        public const int ClassicMasterpiecePercent = 1;

        /// <summary>
        /// The quality roll the server is set to: <see cref="RollClassicQuality"/> with crafting_classic_quality on,
        /// otherwise <see cref="RollQuality(int, int, Random)"/> (the 1.87 rules, the default).
        /// </summary>
        public static int RollQuality(int skill, int recipeLevel, Random random, bool classic) =>
            classic ? RollClassicQuality(skill, recipeLevel, random) : RollQuality(skill, recipeLevel, random);

        /// <summary>
        /// The optional classic quality (crafting_classic_quality, off by default; WP1): 94-99% as before patch 1.87, with
        /// a small masterpiece chance at every skill. At skill 1000 or on a grey recipe: 2% masterpiece, otherwise 94-99
        /// evenly (the "1:6 chance to get 94-99%" the 1.87 code's comment keeps). Otherwise: <see
        /// cref="ClassicMasterpiecePercent"/>% masterpiece, then the same roulette as the 1.87 rule over six fields
        /// 94-99, so a harder recipe leans to 94. A purple recipe cannot be made, so it never rolls. The era's exact odds
        /// are from memory of the live game, not a dated source (design doc 3.1).
        /// </summary>
        public static int RollClassicQuality(int skill, int recipeLevel, Random random)
        {
            random ??= Random.Shared;
            bool Chance(int percent) => percent > random.Next(100);
            int Roll(int max) => random.Next(0, max + 1);

            int delta = (int)Con(skill, recipeLevel);
            if (skill >= 1000 || delta < -2)
            {
                if (Chance(2))
                    return 100;
                return 94 + Roll(5);
            }

            if (Chance(ClassicMasterpiecePercent))
                return 100;

            delta = delta * 100;

            int[] chancePart = new int[6]; // index 0 = 94% to 5 = 99%
            int sum = 0;
            for (int i = 0; i < 6; i++)
            {
                chancePart[i] = Math.Max((6 - i) * 100 - delta, 0);
                sum += chancePart[i];
            }

            int rand = Roll(sum);
            for (int i = 5; i >= 0; i--)
            {
                if (rand < chancePart[i])
                    return 94 + i;
                rand -= chancePart[i];
            }

            return 94;
        }

        /// <summary>
        /// Whether a failed craft costs its materials: never by default (the 1.87 rule, "you lose no materials");
        /// all of them with crafting_classic_losses on (WP1, the era's rule).
        /// </summary>
        public static bool FailureCostsMaterials(bool classicLosses) => classicLosses;

        /// <summary>
        /// Whether a spellcraft explosion destroys the item: no by default (the Luhz 1.87 rule, only the gems are lost);
        /// yes with crafting_classic_losses on (WP1, the era's rule). The gems are lost either way.
        /// </summary>
        public static bool ExplosionDestroysItem(bool classicLosses) => classicLosses;

        // ---- Player pace (True165's CraftingRate165, 00f9fbf) ----

        /// <summary>
        /// The points one won skill gain adds to a skill at <paramref name="current"/> at a pace of
        /// <paramref name="rate"/> points per gain, never past <paramref name="cap"/> (CraftingRate165.Points; the
        /// gain chance, the grey rule and the caps are unchanged).
        /// </summary>
        public static int PointsPerGain(int current, double rate, int cap = int.MaxValue) => CraftingRate165.Points(current, rate, cap);

        // ---- Salvage and repair (moved from Salvage.cs and Repair.cs; bots repair and salvage by the same rules) ----

        /// <summary>
        /// The secondary skill as a percent of an item's craft level for salvage (Salvage.GetYieldPenalty), 100 from 99%
        /// up. 2026-11-15 (WP1): a craft level of 0 or less (an item type with no craft level) divided by zero; it counts
        /// as 100%. A skill below 0 (untrained) is 0%.
        /// </summary>
        public static int SalvageSkillPercent(int skill, int craftLevel)
        {
            if (craftLevel <= 0)
                return 100;
            int percent = (int)Math.Clamp((long)skill * 100 / craftLevel, 0, 100);
            return percent > 99 ? 100 : percent;
        }

        /// <summary>
        /// The materials a salvage gives after the skill penalty: the count times the skill percent, at least 1
        /// (OpenDAoC 5302c79e9's GetYieldPenalty; the old dead multiplier gave the same results).
        /// </summary>
        public static int SalvageYield(int skill, int craftLevel, int salvageCount) =>
            Math.Max(1, salvageCount * SalvageSkillPercent(skill, craftLevel) / 100);

        /// <summary>Whether a crafting skill is enough to repair an item (Repair.HasSkillToRepair, 2026-10-30).</summary>
        public static bool HasSkillToRepair(int skill, int craftLevel, int percentNeeded) =>
            (long)skill * 100 >= (long)craftLevel * percentNeeded;

        /// <summary>The chance to repair an item (Repair.RepairChancePercent, M0 2026-11-15): 10% at half its craft level,
        /// 100% at its craft level; a craft level of 0 or less is 100%, an untrained skill 0%.</summary>
        public static int RepairChancePercent(int skill, int craftLevel)
        {
            if (craftLevel <= 0)
                return 100;

            if (skill <= 0)
                return 0;

            double chance = 90.0 / (craftLevel * 0.5) * skill - 80; // 50% = 10% chance, 100% = 100% chance
            return (int)Math.Clamp(chance, 0, 100);
        }

        /// <summary>The chance to repair a siege weapon (Repair.SiegeRepairChancePercent, M0): 90 - 50 / woodworking,
        /// integer division, a woodworking below 1 counting as 1.</summary>
        public static int SiegeRepairChancePercent(int woodworking)
        {
            int skill = Math.Max(1, woodworking);
            return Math.Clamp(90 - 50 / skill, 0, 100);
        }

        /// <summary>
        /// The secondary skill a material of this model is worked with (the switch in
        /// AbstractCraftingSkill.CheckSecondCraftingSkillRequirement and DR's SecondarySkillForMaterial), or NoCrafting.
        /// </summary>
        public static eCraftingSkill SecondarySkillForMaterial(int model) => model switch
        {
            522 or 537 => eCraftingSkill.ClothWorking, // cloth square, heavy thread
            521 => eCraftingSkill.LeatherCrafting,     // leather square
            519 => eCraftingSkill.MetalWorking,        // metal bars
            520 => eCraftingSkill.WoodWorking,         // wooden boards
            _ => eCraftingSkill.NoCrafting,
        };

        /// <summary>
        /// The secondary-skill level a recipe asks for (GetSecondaryCraftingSkillMinimumLevel of ArmorCrafting,
        /// WeaponCrafting, Tailoring and Fletching, verbatim); 0 = none. Every other craft asks for none.
        /// </summary>
        public static int SecondaryMinimumLevel(eCraftingSkill skill, int productObjectType, int recipeLevel)
        {
            switch (skill)
            {
                case eCraftingSkill.ArmorCrafting:
                    switch (productObjectType)
                    {
                        case (int)eObjectType.Studded:
                        case (int)eObjectType.Chain:
                        case (int)eObjectType.Plate:
                        case (int)eObjectType.Reinforced:
                        case (int)eObjectType.Scale:
                            return recipeLevel - 60;
                    }
                    return 0;
                case eCraftingSkill.WeaponCrafting:
                    switch (productObjectType)
                    {
                        case (int)eObjectType.CrushingWeapon:
                        case (int)eObjectType.SlashingWeapon:
                        case (int)eObjectType.ThrustWeapon:
                        case (int)eObjectType.TwoHandedWeapon:
                        case (int)eObjectType.PolearmWeapon:
                        case (int)eObjectType.Flexible:
                        case (int)eObjectType.Sword:
                        case (int)eObjectType.Hammer:
                        case (int)eObjectType.Axe:
                        case (int)eObjectType.Spear:
                        case (int)eObjectType.HandToHand:
                        case (int)eObjectType.Blades:
                        case (int)eObjectType.Blunt:
                        case (int)eObjectType.Piercing:
                        case (int)eObjectType.LargeWeapons:
                        case (int)eObjectType.CelticSpear:
                        case (int)eObjectType.Scythe:
                            return recipeLevel - 60;
                    }
                    return 0;
                case eCraftingSkill.Tailoring:
                    switch (productObjectType)
                    {
                        case (int)eObjectType.Cloth:
                        case (int)eObjectType.Leather:
                        case (int)eObjectType.Studded:
                            return recipeLevel - 30;
                    }
                    return 0;
                case eCraftingSkill.Fletching:
                    switch (productObjectType)
                    {
                        case (int)eObjectType.Fired:
                        case (int)eObjectType.Longbow:
                        case (int)eObjectType.Crossbow:
                        case (int)eObjectType.Instrument:
                        case (int)eObjectType.RecurvedBow:
                        case (int)eObjectType.CompositeBow:
                            return recipeLevel - 20;

                        case (int)eObjectType.Arrow:
                        case (int)eObjectType.Bolt:
                        case (int)eObjectType.Thrown:
                            return recipeLevel - 15;

                        case (int)eObjectType.Staff:
                            return recipeLevel - 35;
                    }
                    return 0;
                default:
                    return 0;
            }
        }

        /// <summary>The ingredient counts of a recipe in its order, for <see cref="CraftingTimeSeconds"/>.</summary>
        public static int[] IngredientCounts(Recipe recipe)
        {
            if (recipe == null)
                return Array.Empty<int>();
            List<Ingredient> ingredients = recipe.Ingredients;
            var counts = new int[ingredients.Count];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = ingredients[i].Count;
            return counts;
        }
    }
}
