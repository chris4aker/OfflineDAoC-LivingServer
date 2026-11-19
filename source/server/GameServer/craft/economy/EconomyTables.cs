using System;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The saved-progress tables of Crafting 2.0, declared by WP0 so the release tools, the progress import, the
    /// Passport and the launcher's purge (WP9) know them before any package writes a row. The server creates them empty
    /// at start-up; nothing writes them in WP0. Each is in progress-policy.json (OptionalProgressTables, so
    /// build_release_037.py empties it) and in PassportTables.Decisions (StaysInWorld): UT_EconomyTablesRegistered.
    /// No column is added to offline_world_bots: the launcher's explicit INSERT would break on a new NOT NULL column.
    /// </summary>
    public static class EconomyTables
    {
        public const string BotTypes = "offline_bot_types";
        public const string BotCrafters = "offline_bot_crafters";
        public const string CraftOrders = "offline_bot_craft_orders";
        public const string ExchangeIndex = "offline_exchange_index";
        public const string EconomyLedger = "offline_economy_ledger";
        public const string CrafterCustomers = "offline_crafter_customers";

        public static readonly string[] All = [BotTypes, BotCrafters, CraftOrders, ExchangeIndex, EconomyLedger, CrafterCustomers];

        public static readonly Type[] RecordTypes =
        [
            typeof(OfflineBotTypeRecord), typeof(OfflineBotCrafterRecord), typeof(OfflineBotCraftOrderRecord),
            typeof(OfflineExchangeIndexRecord), typeof(OfflineEconomyLedgerRecord), typeof(OfflineCrafterCustomerRecord),
        ];
    }

    /// <summary>
    /// Every gamebot's personality type, stored the first time WP4 sees it, so changing the type shares (12% Crafters
    /// today, 7% for new bots) never re-types anyone: legacy bots keep their hash-bucket type, beggars and voices too.
    /// </summary>
    [DataTable(TableName = EconomyTables.BotTypes)]
    public sealed class OfflineBotTypeRecord : DataObject, ILivingServerRow
    {
        public DataObject CopyForSave() => (OfflineBotTypeRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Unique = true)] public long BotId { get; set; }
        /// <summary>(int)BotPersonalityType.</summary>
        [DataElement(AllowDbNull = false)] public int PersonalityType { get; set; }
        /// <summary>(int)CrafterCohort.</summary>
        [DataElement(AllowDbNull = false)] public int Cohort { get; set; }
        [DataElement(AllowDbNull = false)] public string StoredUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// A crafter gamebot (WP4). Its skills stay in offline_world_bots.SerializedCraftingSkills in BotCraftingSkills'
    /// format (DividedRealms'), primary first; this row holds the order and the crafter's book-keeping.
    /// </summary>
    [DataTable(TableName = EconomyTables.BotCrafters)]
    public sealed class OfflineBotCrafterRecord : DataObject, ILivingServerRow
    {
        public DataObject CopyForSave() => (OfflineBotCrafterRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Unique = true)] public long BotId { get; set; }
        [DataElement(AllowDbNull = false)] public int Realm { get; set; }
        /// <summary>(int)eCraftingSkill of the order the crafter joined.</summary>
        [DataElement(AllowDbNull = false)] public int PrimaryCraft { get; set; }
        [DataElement(AllowDbNull = false)] public string PolicyJson { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false, Varchar = 64)] public string LastTitle { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int CraftsMade { get; set; }
        [DataElement(AllowDbNull = false)] public int Masterpieces { get; set; }
        [DataElement(AllowDbNull = false)] public long CoinEarned { get; set; }
        [DataElement(AllowDbNull = false)] public long CoinSpent { get; set; }
        [DataElement(AllowDbNull = false)] public string LastSessionUtc { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string CreatedUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// A craft order with its escrowed coin. The table name and the first columns are DividedRealms'
    /// (OfflineBotCraftOrderRecord, ceac061a), so its AutonomousBotCraftOrders ports onto this record unchanged (WP5);
    /// the columns after ExpiresUtc let the same board carry the player's and the quartermaster's orders.
    /// </summary>
    [DataTable(TableName = EconomyTables.CraftOrders)]
    public sealed class OfflineBotCraftOrderRecord : DataObject, ILivingServerRow
    {
        public const string OpenState = "open";
        public const string MadeState = "made";

        public DataObject CopyForSave() => (OfflineBotCraftOrderRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Unique = true, Varchar = 64)] public string OrderId { get; set; } = string.Empty;
        /// <summary>The buying gamebot; 0 for a player's or the quartermaster's order (then see BuyerId).</summary>
        [DataElement(AllowDbNull = false, Index = true)] public long BuyerBotId { get; set; }
        [DataElement(AllowDbNull = false, Index = true)] public long CrafterBotId { get; set; }
        [DataElement(AllowDbNull = false)] public int Realm { get; set; }
        [DataElement(AllowDbNull = false)] public int CraftingSkill { get; set; }
        [DataElement(AllowDbNull = false, Varchar = 255)] public string ProductId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false, Varchar = 255)] public string ProductName { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int RecipeLevel { get; set; }
        /// <summary>The buyer's coin, held by the order.</summary>
        [DataElement(AllowDbNull = false)] public long Copper { get; set; }
        /// <summary>"open" until the piece is made, then "made" until it is delivered (CraftOrders.StateText for the rest).</summary>
        [DataElement(AllowDbNull = false, Varchar = 16)] public string State { get; set; } = OpenState;
        /// <summary>The made piece in the crafter's bag, while it waits for delivery.</summary>
        [DataElement(AllowDbNull = false, Varchar = 255)] public string ItemObjectId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string CreatedUtc { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string ExpiresUtc { get; set; } = string.Empty;
        // Ours (Crafting 2.0 WP0)
        /// <summary>(int)CraftOrderSource; 0 = a gamebot's order, as every DividedRealms row is.</summary>
        [DataElement(AllowDbNull = false)] public int Source { get; set; }
        /// <summary>"offlinebot:{id}" for a gamebot, the character id for a player, "quartermaster:{realm}".</summary>
        [DataElement(AllowDbNull = false, Varchar = 64)] public string BuyerId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false, Varchar = 64)] public string BuyerName { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int MinQuality { get; set; }
        [DataElement(AllowDbNull = false)] public int Count { get; set; } = 1;
        [DataElement(AllowDbNull = false)] public bool PayUpFront { get; set; } = true;
    }

    /// <summary>The price index per band (MarketBand.Key), written hourly by WP5.</summary>
    [DataTable(TableName = EconomyTables.ExchangeIndex)]
    public sealed class OfflineExchangeIndexRecord : DataObject, ILivingServerRow
    {
        public DataObject CopyForSave() => (OfflineExchangeIndexRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Unique = true, Varchar = 64)] public string BandKey { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Realm { get; set; }
        [DataElement(AllowDbNull = false)] public int Count { get; set; }
        [DataElement(AllowDbNull = false)] public int Demand { get; set; }
        [DataElement(AllowDbNull = false)] public long IndexCopper { get; set; }
        [DataElement(AllowDbNull = false)] public long FloorCopper { get; set; }
        [DataElement(AllowDbNull = false)] public long CeilingCopper { get; set; }
        [DataElement(AllowDbNull = false)] public int DailyPlayerSales { get; set; }
        [DataElement(AllowDbNull = false, Varchar = 16)] public string DailyDate { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
    }

    /// <summary>One hour of one realm's money supply and flows (WP2), for the Herald and the launcher's economy page.</summary>
    [DataTable(TableName = EconomyTables.EconomyLedger)]
    public sealed class OfflineEconomyLedgerRecord : DataObject, ILivingServerRow
    {
        public DataObject CopyForSave() => (OfflineEconomyLedgerRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Index = true, Varchar = 32)] public string HourUtc { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Realm { get; set; }
        [DataElement(AllowDbNull = false)] public long BotCoin { get; set; }
        [DataElement(AllowDbNull = false)] public long PlayerCoin { get; set; }
        [DataElement(AllowDbNull = false)] public long GuildBanks { get; set; }
        [DataElement(AllowDbNull = false)] public long ListedValue { get; set; }
        [DataElement(AllowDbNull = false)] public string FaucetsJson { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string SinksJson { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public long ResidualCopper { get; set; }
        [DataElement(AllowDbNull = false)] public int Bots { get; set; }
        [DataElement(AllowDbNull = false)] public int Crafters { get; set; }
    }

    /// <summary>A crafter gamebot's memory of a customer (WP8): repeat deals get a greeting and a rounded-down price.</summary>
    [DataTable(TableName = EconomyTables.CrafterCustomers)]
    public sealed class OfflineCrafterCustomerRecord : DataObject, ILivingServerRow
    {
        public DataObject CopyForSave() => (OfflineCrafterCustomerRecord)MemberwiseClone();

        [DataElement(AllowDbNull = false, Index = true)] public long CrafterBotId { get; set; }
        [DataElement(AllowDbNull = false, Varchar = 64)] public string CustomerId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false, Varchar = 64)] public string Name { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Deals { get; set; }
        [DataElement(AllowDbNull = false)] public int Stiffed { get; set; }
        [DataElement(AllowDbNull = false)] public string LastUtc { get; set; } = string.Empty;
        // WP8: what the customer has paid this crafter in all, and a refund owed to a player who was offline when an
        // order lapsed (paid at their next login; outside the money supply until then, as escrow is).
        [DataElement(AllowDbNull = false)] public long SpentCopper { get; set; }
        [DataElement(AllowDbNull = false)] public long OwedCopper { get; set; }
    }
}
