using System;
using DOL.Database;
using System.Collections.Generic;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>Who placed a craft order: a gamebot (DividedRealms' commissions, True165's milestone sets), the realm's
    /// quartermaster, or the player.</summary>
    public enum CraftOrderSource { Bot, Quartermaster, Player }

    /// <summary>
    /// An order's state. "open" and "made" are DividedRealms' two states (AutonomousBotCraftOrders.OpenState/MadeState);
    /// the rest are ours. The stored text is <see cref="CraftOrders.StateText"/>.
    /// </summary>
    public enum CraftOrderState { Open, Made, Delivered, Expired, Cancelled }

    /// <summary>
    /// One craft order, the record form of <see cref="OfflineBotCraftOrderRecord"/>. The first fields are DividedRealms'
    /// (ceac061a): the buyer and crafter, the product, the escrowed coin, the item made and the deadline; the rest let the
    /// same board carry the player's and the quartermaster's orders. A bot buyer has <c>BuyerId = "offlinebot:{id}"</c>.
    /// </summary>
    public sealed record CraftOrder(string OrderId, eRealm Realm, CraftOrderSource Source, long BuyerBotId, string BuyerId,
        string BuyerName, long CrafterBotId, eCraftingSkill Skill, string ProductId, string ProductName, int RecipeLevel,
        int MinQuality, int Count, long EscrowCopper, bool PayUpFront, CraftOrderState State, string ItemObjectId,
        DateTime CreatedUtc, DateTime ExpiresUtc);

    /// <summary>The order board (WP5 implements it by porting DividedRealms' AutonomousBotCraftOrders).</summary>
    public interface ICraftOrderBoard
    {
        /// <summary>Posts an order; false when refused (no crafter, no coin, a duplicate id).</summary>
        bool Post(CraftOrder order);
        IReadOnlyList<CraftOrder> Open(eRealm realm, eCraftingSkill skill, int maxRecipeLevel);
        IReadOnlyList<CraftOrder> ForCrafter(long crafterBotId);
        IReadOnlyList<CraftOrder> ForBuyer(string buyerId);
        /// <summary>Whether this item is held for an order (never worn, listed or sold by its maker).</summary>
        bool IsHeldForOrder(DbInventoryItem item);
        bool TryMarkMade(string orderId, DbInventoryItem item);
        bool TryDeliver(string orderId);
        int ExpireDue(DateTime utcNow);
    }

    public static class CraftOrders
    {
        private static volatile ICraftOrderBoard _board = NullBoard.Instance;

        /// <summary>The registered board; empty until WP5.</summary>
        public static ICraftOrderBoard Board
        {
            get => _board;
            set => _board = value ?? NullBoard.Instance;
        }

        /// <summary>The stored text of a state ("open", "made" as in DividedRealms; then "delivered", "expired", "cancelled").</summary>
        public static string StateText(CraftOrderState state) => state.ToString().ToLowerInvariant();

        public static CraftOrderState ParseState(string text) =>
            Enum.TryParse(text, true, out CraftOrderState state) ? state : CraftOrderState.Open;

        private sealed class NullBoard : ICraftOrderBoard
        {
            internal static readonly NullBoard Instance = new();
            public bool Post(CraftOrder order) => false;
            public IReadOnlyList<CraftOrder> Open(eRealm realm, eCraftingSkill skill, int maxRecipeLevel) => Array.Empty<CraftOrder>();
            public IReadOnlyList<CraftOrder> ForCrafter(long crafterBotId) => Array.Empty<CraftOrder>();
            public IReadOnlyList<CraftOrder> ForBuyer(string buyerId) => Array.Empty<CraftOrder>();
            public bool IsHeldForOrder(DbInventoryItem item) => false;
            public bool TryMarkMade(string orderId, DbInventoryItem item) => false;
            public bool TryDeliver(string orderId) => false;
            public int ExpireDue(DateTime utcNow) => 0;
        }
    }

    /// <summary>
    /// A price band: realm, object type and slot, a five-level band (DividedRealms' RealmExchangeSaturation.Kind uses the
    /// same band) and a quality tier (0 = 98% or less, 1 = 99%, 2 = 100%).
    /// </summary>
    public readonly record struct MarketBand(eRealm Realm, int ObjectType, int ItemType, int LevelBand, int QualityTier)
    {
        public const int LevelsPerBand = 5;

        public static int BandOf(int level) => Math.Max(0, level) / LevelsPerBand;

        public static int TierOf(int quality) => quality >= 100 ? 2 : quality == 99 ? 1 : 0;

        public static MarketBand Of(eRealm realm, DbInventoryItem item) => item == null
            ? default
            : new MarketBand(realm, item.Object_Type, item.Item_Type, BandOf(item.Level), TierOf(item.Quality));

        /// <summary>The stored key of offline_exchange_index ("realm|objectType|itemType|band|tier").</summary>
        public string Key => $"{(int)Realm}|{ObjectType}|{ItemType}|{LevelBand}|{QualityTier}";
    }

    /// <summary>
    /// What an item is worth to bots (WP5/WP6). Its first form is True165's ItemValue165 (floor, shelf, fair; 32a88e0),
    /// ported by WP6; WP5 adds demand per band. Ask is never below Bid plus the vendor spread, so no buy-and-sell loop
    /// makes coin.
    /// </summary>
    public interface IPriceIndex
    {
        /// <summary>What a merchant pays (the floor no price goes under).</summary>
        long Floor(DbInventoryItem item);
        /// <summary>The fair Exchange price of the item, before a seller's mood.</summary>
        long Fair(DbInventoryItem item, long materialCostCopper = 0);
        /// <summary>The seller's ask with its disposition (-0.35..+0.50 today; True165 narrows it to -0.05..+0.15).</summary>
        long Ask(DbInventoryItem item, double disposition, long materialCostCopper = 0);
        /// <summary>The most a bot pays for it.</summary>
        long Bid(DbInventoryItem item, double disposition);
        void ObserveListing(MarketBand band, long priceCopper);
        void ObserveSale(MarketBand band, long priceCopper);
        void ObserveDemand(MarketBand band);
    }

    public static class PriceIndexes
    {
        private static volatile IPriceIndex _current = VendorValueIndex.Instance;

        /// <summary>The registered index; until WP5/WP6 a vendor-value fallback that prices as today's code does.</summary>
        public static IPriceIndex Current
        {
            get => _current;
            set => _current = value ?? VendorValueIndex.Instance;
        }

        /// <summary>Today's numbers: the merchant appraisal as floor and bid, today's listing valuation as fair and ask.</summary>
        private sealed class VendorValueIndex : IPriceIndex
        {
            internal static readonly VendorValueIndex Instance = new();
            public long Floor(DbInventoryItem item) => AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item);
            public long Fair(DbInventoryItem item, long materialCostCopper = 0) => AutonomousBotEconomy.RecommendListingPrice(item);
            public long Ask(DbInventoryItem item, double disposition, long materialCostCopper = 0) =>
                AutonomousBotEconomy.RecommendListingPrice(item, disposition);
            public long Bid(DbInventoryItem item, double disposition) => Floor(item);
            public void ObserveListing(MarketBand band, long priceCopper) { }
            public void ObserveSale(MarketBand band, long priceCopper) { }
            public void ObserveDemand(MarketBand band) { }
        }
    }
}
