using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS
{
    /// <summary>
    /// Crafting 2.0 WP6: True165's ItemValue165 (shadowofze, 32a88e0, 145bdee, via True165, GPL-3.0) as the
    /// <see cref="IPriceIndex"/> floor, fair and ask, outside True165's 1.65 mode switch. With bot_crafters off it answers
    /// exactly as WP0's fallback does (today's merchant appraisal and listing valuation), so registering it changes
    /// nothing until the switch is on. WP5 adds demand per band (the Observe calls) by wrapping or replacing it; it is
    /// registered only while no other package has registered an index.
    /// <list type="bullet">
    /// <item>Floor: what a merchant pays (ItemValue165.Floor).</item>
    /// <item>Fair: ItemValue165.Fair (the 1.65 level/slot/quality curve for gear, 0.9 x shelf for merchant stock, crafted at
    /// least 1.1 x its materials).</item>
    /// <item>Ask: ItemValue165.Ask with the disposition as the seller's mood, between its ask floor and ceiling.</item>
    /// <item>Bid, the most a bot pays: Fair x (1 + mood), the mood kept to True165's -5%..+15%, never under Floor.</item>
    /// </list>
    /// </summary>
    public sealed class ItemValuePriceIndex : IPriceIndex
    {
        public static readonly ItemValuePriceIndex Instance = new();

        private static bool On => CraftingEconomyProperties.BOT_CRAFTERS;

        public long Floor(DbInventoryItem item) =>
            On ? ItemValue165.Floor(item) : AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item);

        public long Fair(DbInventoryItem item, long materialCostCopper = 0) =>
            On ? ItemValue165.Fair(item, materialCostCopper) : AutonomousBotEconomy.RecommendListingPrice(item);

        public long Ask(DbInventoryItem item, double disposition, long materialCostCopper = 0) =>
            On ? ItemValue165.Ask(item, disposition, 0, materialCostCopper) : AutonomousBotEconomy.RecommendListingPrice(item, disposition);

        public long Bid(DbInventoryItem item, double disposition)
        {
            if (!On)
                return AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item);
            if (item == null)
                return 0;
            double mood = System.Math.Clamp(disposition, ItemValue165.MoodLow, ItemValue165.MoodHigh);
            long bid = (long)System.Math.Round(ItemValue165.Fair(item) * (1 + mood));
            return System.Math.Max(ItemValue165.Floor(item), bid);
        }

        public void ObserveListing(MarketBand band, long priceCopper) { }
        public void ObserveSale(MarketBand band, long priceCopper) { }
        public void ObserveDemand(MarketBand band) { }

        /// <summary>Whether the registered index is still WP0's own fallback (no package has registered one).</summary>
        public static bool IsFallback(IPriceIndex index) => index != null && index.GetType().DeclaringType == typeof(PriceIndexes);

        /// <summary>Registers this index unless another package already has one.</summary>
        public static void RegisterIfUnset()
        {
            if (IsFallback(PriceIndexes.Current))
                PriceIndexes.Current = Instance;
        }
    }
}
