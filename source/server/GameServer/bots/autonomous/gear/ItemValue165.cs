using System;
using System.Collections.Generic;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// From shadowofze (32a88e0, 145bdee), via True165, GPL-3.0. Crafting 2.0 WP6 ports it unchanged except that the
    /// two Exchange165 factors it read (Exchange165 is part of True165's 1.65 mode, which is not ported) are copied in
    /// below as <see cref="SlotFactor"/> and <see cref="QualityFactor"/>; nothing here reads a 1.65 mode switch. It is
    /// registered as the IPriceIndex's floor/fair/ask by <see cref="ItemValuePriceIndex"/> (bot_crafters on).
    ///
    /// One value score per item for the Realm Exchange (2026-11-11 exchange plan, section 2.1). It replaces three valuers
    /// that disagreed (AutonomousAuctionValuation for stacks, Exchange165.Price for gear, EquipmentValue for buying) for
    /// every listing and buying decision. Pure functions, no DB after the merchant-stock set is built once, no LINQ.
    /// - Floor: what a merchant pays for it (the ordinary merchant appraisal).
    /// - Shelf: what a merchant sells it for, when a merchant stocks it (a set of MerchantItem template ids, built once).
    /// - Utility: bonus points weighted by type, as a period player valued them (skills 5, resists and power 2, stats,
    ///   focus and AF 1, hits 1/4). The Bonus% field is left out.
    /// - Fair: stacks, ammunition, poisons and other non-gear 0.9 x Shelf (2 x Floor when no merchant sells it); gear the
    ///   owner's 1.65 level/slot/quality curve (Exchange165) x up to +50% for utility, +25% for a proc or charges, kept
    ///   within the table's cap, at least 2 x Floor and at most 0.9 x Shelf; crafted at least 1.1 x its materials.
    /// - Gain = Fair - Floor: the copper a bot earns by listing instead of selling to a merchant. Gain drives every
    ///   listing decision.
    /// </summary>
    public static class ItemValue165
    {
        private const long Gold = 10_000;
        /// <summary>The least extra copper over a merchant that makes a listing worth a slot (or a quarter of the merchant
        /// value, when that is more).</summary>
        public const long MinimumGainCopper = 300;
        /// <summary>Items more than this many levels below the bot are grey loot to it: not listed unless their gain is high.</summary>
        public const int GreyLevels = 12;
        /// <summary>A seller's mood on top of the fair price: -5% to +15% (the old -35% to +50% listed one tunic anywhere
        /// from 1,244 to 4,538 copper).</summary>
        public const double MoodLow = -0.05, MoodHigh = 0.15;

        private static readonly float[] Weights = BuildWeights();

        private static float[] BuildWeights()
        {
            var w = new float[256];
            Array.Fill(w, 1f);
            for (int p = (int)eProperty.Stat_First; p <= (int)eProperty.Stat_Last; p++)
                w[p] = 1f;
            w[(int)eProperty.Acuity] = 1f;
            w[(int)eProperty.MaxHealth] = 0.25f;
            w[(int)eProperty.MaxMana] = 2f;
            w[(int)eProperty.PowerPool] = 2f;
            for (int p = (int)eProperty.Resist_First; p <= (int)eProperty.Resist_Last; p++)
                w[p] = 2f;
            for (int p = (int)eProperty.Skill_First; p <= (int)eProperty.Skill_Last; p++)
                w[p] = 5f;
            foreach (eProperty p in new[] { eProperty.AllMagicSkills, eProperty.AllMeleeWeaponSkills, eProperty.AllSkills,
                         eProperty.AllDualWieldingSkills, eProperty.AllArcherySkills })
                w[(int)p] = 5f;
            for (int p = (int)eProperty.Focus_Darkness; p <= (int)eProperty.Focus_Arboreal; p++)
                w[p] = 1f;
            w[(int)eProperty.AllFocusLevels] = 1f;
            w[(int)eProperty.ArmorFactor] = 1f;
            return w;
        }

        /// <summary>The weight of one point of a bonus type.</summary>
        public static double Weight(int propertyType) => propertyType is >= 0 and < 256 ? Weights[propertyType] : 1d;

        /// <summary>The item's bonus points weighted by type (Bonus1-10 and ExtraBonus; not the Bonus% field).</summary>
        public static double Utility(DbInventoryItem item)
        {
            if (item == null)
                return 0;
            return Weight(item.Bonus1Type) * Math.Abs(item.Bonus1) + Weight(item.Bonus2Type) * Math.Abs(item.Bonus2) +
                   Weight(item.Bonus3Type) * Math.Abs(item.Bonus3) + Weight(item.Bonus4Type) * Math.Abs(item.Bonus4) +
                   Weight(item.Bonus5Type) * Math.Abs(item.Bonus5) + Weight(item.Bonus6Type) * Math.Abs(item.Bonus6) +
                   Weight(item.Bonus7Type) * Math.Abs(item.Bonus7) + Weight(item.Bonus8Type) * Math.Abs(item.Bonus8) +
                   Weight(item.Bonus9Type) * Math.Abs(item.Bonus9) + Weight(item.Bonus10Type) * Math.Abs(item.Bonus10) +
                   Weight(item.ExtraBonusType) * Math.Abs(item.ExtraBonus);
        }

        /// <summary>What a merchant pays for it.</summary>
        public static long Floor(DbInventoryItem item) => Math.Max(0, AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item));

        /// <summary>What a merchant sells it for (the same quantity), or 0 when no merchant stocks it.</summary>
        public static long Shelf(DbInventoryItem item)
        {
            if (item == null || item.Price <= 0 || !IsMerchantStock(item.Id_nb))
                return 0;
            return item.Price * (long)Math.Max(1, item.Count) / Math.Max(1, item.PackSize);
        }

        /// <summary>Gear: weapons, armour, shields, jewellery and instruments (valued by the 1.65 curve).</summary>
        public static bool IsGear(DbInventoryItem item) => item != null && item.MaxCount <= 1 && item.Count <= 1 &&
            (eObjectType)item.Object_Type is >= eObjectType.GenericWeapon and <= eObjectType._LastWeapon or
                >= eObjectType._FirstArmor and <= eObjectType._LastArmor or eObjectType.Magical or eObjectType.Shield or
                eObjectType.Instrument &&
            (eObjectType)item.Object_Type is not (eObjectType.Arrow or eObjectType.Bolt or eObjectType.Poison);

        /// <summary>The fair exchange price in copper (see the class summary). <paramref name="materialCost"/> is a
        /// crafter's own material cost, when known.</summary>
        public static long Fair(DbInventoryItem item, long materialCost = 0)
        {
            if (item == null)
                return 0;
            long floor = Floor(item);
            long shelf = Shelf(item);
            long fair;
            if (!IsGear(item))
                fair = shelf > 0 ? shelf * 9 / 10 : Math.Max(2 * floor, floor + 1);
            else
            {
                int level = Math.Clamp(item.Level, 1, 50);
                double curve = Math.Pow(level / 50d, 3);
                double baseGold = 200 * curve * SlotFactor(item);
                double quality = QualityFactor(item.Quality);
                double uCap = Math.Max(4, 0.8 * level);
                double utility = 1 + 0.5 * Math.Min(Utility(item), uCap) / uCap;
                double proc = HasProcOrCharges(item) ? 1.25 : 1;
                double condition = 0.45 + 0.55 * Math.Clamp((int)item.ConditionPercent, 1, 100) / 100d;
                double price = baseGold * quality * utility * proc * condition * Gold;
                double cap = Math.Max(1, 800 * curve) * Gold;
                fair = (long)Math.Round(Math.Clamp(price, 1, cap));
                fair = Math.Max(fair, 2 * floor);
                if (shelf > 0)
                    fair = Math.Min(fair, shelf * 9 / 10);
            }
            if (item.IsCrafted && materialCost > 0)
                fair = Math.Max(fair, materialCost * 11 / 10);
            return Math.Max(1, fair);
        }

        /// <summary>Copper gained by listing it instead of selling it to a merchant.</summary>
        public static long Gain(DbInventoryItem item, long materialCost = 0) => Math.Max(0, Fair(item, materialCost) - Floor(item));

        /// <summary>The least gain worth a listing slot: 300 copper, or a quarter of the merchant value when more.</summary>
        public static long MinimumGain(long floor) => Math.Max(MinimumGainCopper, floor / 4);

        /// <summary>A gain high enough to list even grey loot: what a bot of this level would care about.</summary>
        public static long HighGain(int botLevel) => Math.Max(MinimumGainCopper * 4, (long)botLevel * botLevel * 20);

        /// <summary>The lowest ask a bot posts: a quarter over the merchant value (and above it).</summary>
        public static long AskFloor(DbInventoryItem item)
        {
            long floor = Floor(item);
            return Math.Max(floor + 1, floor * 5 / 4);
        }

        /// <summary>The highest ask: under 90% of the merchant's own price for merchant stock; otherwise none.</summary>
        public static long AskCeiling(DbInventoryItem item)
        {
            long shelf = Shelf(item);
            return shelf > 0 ? shelf * 9 / 10 - 1 : long.MaxValue;
        }

        /// <summary>
        /// Whether a bot of this level would list it at all: its gain is worth a slot, merchant stock can be asked under
        /// 90% of the merchant's price and above the floor, and it is not grey loot to the bot unless its gain is high.
        /// </summary>
        public static bool WorthListing(DbInventoryItem item, int botLevel, long materialCost = 0)
        {
            if (item == null)
                return false;
            long floor = Floor(item);
            long gain = Gain(item, materialCost);
            if (gain < MinimumGain(floor) || AskFloor(item) > AskCeiling(item))
                return false;
            return item.Level >= botLevel - GreyLevels || gain >= HighGain(botLevel);
        }

        /// <summary>A seller's mood roll: -5% to +15%.</summary>
        public static double RollMood() => MoodLow + Random.Shared.NextDouble() * (MoodHigh - MoodLow);

        /// <summary>
        /// The ask: Fair x (1 + mood), kept between <see cref="AskFloor"/> and <see cref="AskCeiling"/>. When other copies
        /// are listed (<paramref name="cheapestOther"/> above 0) it undercuts the cheapest by 3-5%, never below the floor.
        /// A crafter's product (<paramref name="materialCost"/> above 0) never asks under <see cref="CraftedFloor"/>.
        /// </summary>
        public static long Ask(DbInventoryItem item, double mood, long cheapestOther = 0, long materialCost = 0)
        {
            if (item == null)
                return 0;
            long low = AskFloor(item);
            long high = AskCeiling(item);
            // crafted at least 1.1 x its materials (an enchanted piece's include the enchant), still under the ceiling
            // (review 2026-11-11: the undercut could take it under its materials)
            if (materialCost > 0)
                low = Math.Max(low, Math.Min(high, CraftedFloor(materialCost)));
            double price = Fair(item, materialCost) * (1 + Math.Clamp(mood, -0.5, 1.0));
            if (cheapestOther > 0)
                price = Math.Min(price, cheapestOther * (0.95 + 0.02 * Math.Clamp(mood - MoodLow, 0, MoodHigh - MoodLow) / (MoodHigh - MoodLow)));
            long ask = (long)Math.Round(price);
            if (ask > high)
                ask = high;
            if (ask < low)
                ask = low;
            return Math.Clamp(ask, 1, int.MaxValue);
        }

        /// <summary>The least a crafter asks for its product: 1.1 x its materials.</summary>
        public static long CraftedFloor(long materialCost) => materialCost > 0 ? materialCost * 11 / 10 : 0;

        /// <summary>True165's Exchange165.QualityFactor (the owner's quality steps: 96% is the reference, masterpieces ask most).</summary>
        public static double QualityFactor(int quality) => quality switch
        {
            >= 100 => 2.475, 99 => 1.6, 98 => 1.35, 97 => 1.15, 96 => 1.0, 95 => 0.85, 94 => 0.75, _ => 0.6,
        };

        /// <summary>True165's Exchange165.SlotFactor: the body piece is the reference; other armour, shields and jewellery
        /// ask less, two-handers more.</summary>
        public static double SlotFactor(DbInventoryItem item) => (eInventorySlot)item.Item_Type switch
        {
            eInventorySlot.TorsoArmor => 1.0,
            eInventorySlot.LegsArmor => 0.85,
            eInventorySlot.HeadArmor or eInventorySlot.ArmsArmor => 0.7,
            eInventorySlot.HandsArmor or eInventorySlot.FeetArmor => 0.6,
            eInventorySlot.TwoHandWeapon => 1.1,
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.DistanceWeapon => 0.9,
            eInventorySlot.Cloak => 0.5,
            _ => 0.6,   // jewellery, belts, bracers, rings, necks
        };

        public static bool HasProcOrCharges(DbInventoryItem item) =>
            item != null && (item.SpellID != 0 || item.SpellID1 != 0 || item.ProcSpellID != 0 || item.ProcSpellID1 != 0);

        // ---- merchant stock

        private static volatile HashSet<string> _testMerchantStock;
        private static volatile HashSet<string> _merchantStock;
        private static readonly HashSet<string> NoStock = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object StockGate = new();
        private static long _nextStockRetryTick;

        /// <summary>Whether some merchant sells this template.</summary>
        public static bool IsMerchantStock(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return false;
            HashSet<string> stock = _testMerchantStock ?? MerchantStock();
            return stock.Contains(templateId);
        }

        /// <summary>Startup, before the market cache is built and outside its lock: the merchant-stock set is read now, not
        /// by the first listing's MarketBook.Add under the index write lock (review 2026-11-11).</summary>
        public static void WarmMerchantStock() => MerchantStock();

        /// <summary>The MerchantItem template ids, read once. A failed read is not kept (every Fair price would have lost
        /// its shelf price for the life of the process): it is tried again a minute later, with no stock known meanwhile.</summary>
        private static HashSet<string> MerchantStock()
        {
            HashSet<string> stock = _merchantStock;
            if (stock != null)
                return stock;
            lock (StockGate)
            {
                if (_merchantStock != null)
                    return _merchantStock;
                if (Environment.TickCount64 < Interlocked.Read(ref _nextStockRetryTick))
                    return NoStock;
                try
                {
                    IObjectDatabase database = GameServer.Database;
                    if (database == null)
                        return NoStock;   // no server (tests): no stock known, nothing kept
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DbMerchantItem row in database.SelectAllObjects<DbMerchantItem>() ?? [])
                        if (!string.IsNullOrEmpty(row?.ItemTemplateID))
                            ids.Add(row.ItemTemplateID);
                    _merchantStock = ids;
                    return ids;
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _nextStockRetryTick, Environment.TickCount64 + 60_000);
                    return NoStock;
                }
            }
        }

        /// <summary>Tests only: the merchant stock to use instead of the database (null restores it).</summary>
        public static void SetMerchantStockForTests(IEnumerable<string> templateIds) =>
            _testMerchantStock = templateIds == null ? null : new HashSet<string>(templateIds, StringComparer.OrdinalIgnoreCase);
    }
}
