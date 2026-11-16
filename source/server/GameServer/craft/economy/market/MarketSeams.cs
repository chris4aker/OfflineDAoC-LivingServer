using System;
using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// What Crafting 2.0 WP5 (the market) registers in WP0's seams, so no shared file is edited for it:
/// <list type="bullet">
/// <item><see cref="AutonomousBotCraftOrders.Board"/> as CraftOrders.Board and as a store (load, the expiry pass, flush).</item>
/// <item><see cref="TaskBreak"/> (IBotTaskBreakHook): DividedRealms' AtTaskBreak - deliver, then perhaps order.</item>
/// <item><see cref="ItemPolicy"/> (IBotItemPolicy): DR's IsHeldForOrder as IsReserved; DR's saturation as BlocksListing and
/// OnListed; a reclaimed piece goes to a vendor next.</item>
/// <item><see cref="ListingPolicy"/> (IExchangeListingPolicy): DR's extra posts for a crafter's own work,
/// bot_crafter_exchange_listings in all (6 = DR's 3 + 3).</item>
/// <item><see cref="ExchangeTax.SellerProceeds"/> as EconomySeams.SellerProceeds (exchange_tax_percent, 0 by default).</item>
/// <item><see cref="OnSaleCommitted"/> on EconomySeams.SaleCommitted: DR's AUTONOMOUS_EXCHANGE_SOLD ... crafted= line, the
/// index's sale, the crafter's earnings.</item>
/// <item><see cref="ReclaimExpired"/> as EconomySeams.TryReclaimExpiredListing: unsold crafted work returns to its crafter.</item>
/// <item><see cref="MarketPriceIndex"/> as PriceIndexes.Current and as a store (the hourly recount).</item>
/// <item><see cref="Commissions"/> (a store): the quartermaster's posting pass.</item>
/// <item>WP10's /lscraft commission and /lscraft index (CraftEconomyTestHooks).</item>
/// </list>
/// With bot_crafters off (the default) every member answers as today's code: no order, no extra post, no saturation, no
/// reclaim, no sale line; the tax is 0. Companions and players never reach the bot-side members.
/// </summary>
public static class MarketSeams
{
    private static readonly Logger Log = LoggerManager.Create(typeof(MarketSeams));
    private static int _registered;

    /// <summary>A gamebot in the world by id (the registry; tests replace it).</summary>
    public static Func<long, GameBot> FindLive = DefaultFindLive;

    private static GameBot DefaultFindLive(long botId) => AutonomousBotRegistry.TryGet(botId, out GameBot bot) ? bot : null;

    [ScriptLoadedEvent]
    public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

    /// <summary>Registers every WP5 implementation once (script load, before the Living Server loads its stores).</summary>
    public static void Register()
    {
        if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
            return;
        CraftOrders.Board = AutonomousBotCraftOrders.Board.Instance;
        PriceIndexes.Current = MarketPriceIndex.Instance;
        EconomyLifecycle.Register(AutonomousBotCraftOrders.Board.Instance);
        EconomyLifecycle.Register(MarketPriceIndex.Instance);
        EconomyLifecycle.Register(Commissions.Instance);
        EconomySeams.Listings = ListingPolicy.Instance;
        EconomySeams.Register(ItemPolicy.Instance);
        EconomySeams.SellerProceeds = ExchangeTax.SellerProceeds;
        EconomySeams.TryReclaimExpiredListing = ReclaimExpired;
        EconomySeams.SaleCommitted -= OnSaleCommitted;
        EconomySeams.SaleCommitted += OnSaleCommitted;
        BotServiceSeams.Register(TaskBreak.Instance);
        // WP10's /lscraft parts this package answers.
        DOL.GS.Commands.CraftEconomyTestHooks.Register("commission", (_, _) => CommissionLines());
        DOL.GS.Commands.CraftEconomyTestHooks.Register("index", (gm, _) => IndexLines(FirstBackpackItem(gm)));
    }

    private static DbInventoryItem FirstBackpackItem(GamePlayer gm) =>
        gm?.Inventory?.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)?
            .Where(item => item != null).OrderBy(item => item.SlotPosition).FirstOrDefault();

    /// <summary>Tests only: register again after the seams were reset.</summary>
    public static void RegisterForTests()
    {
        System.Threading.Interlocked.Exchange(ref _registered, 0);
        Register();
    }

    /// <summary>Tests only: every WP5 holder back to WP0's default and every WP5 state empty.</summary>
    public static void ResetForTests()
    {
        System.Threading.Interlocked.Exchange(ref _registered, 0);
        CraftOrders.Board = null;
        PriceIndexes.Current = null;
        AutonomousBotCraftOrders.ResetForTests();
        QuartermasterCommissions.ResetForTests();
        MilestoneCraftSets.ResetForTests();
        MarketPriceIndex.Instance.ResetForTests();
        RealmExchangeSaturation.Reset();
        Reclaimed.Clear();
        FindLive = DefaultFindLive;
        DOL.GS.Commands.CraftEconomyTestHooks.ResetForTests();
    }

    /// <summary>DR's AtTaskBreak at both ends of a natural task break: at the start deliver and perhaps order; at the end
    /// (the craft trip is over) deliver what it made (DR e37f8810).</summary>
    public sealed class TaskBreak : IBotTaskBreakHook
    {
        public static readonly TaskBreak Instance = new();
        public string Name => "craft orders (WP5)";

        public void AtTaskBreak(GameBot bot, TaskBreakPoint point) =>
            AutonomousBotCraftOrders.AtTaskBreak(bot, commission: point == TaskBreakPoint.Begin);
    }

    /// <summary>The quartermaster's posting pass (once a minute from the pulse) as a store.</summary>
    public sealed class Commissions : IEconomyStore
    {
        public static readonly Commissions Instance = new();
        public string Name => "realm commissions";
        public void Load() { }
        public void Pulse(long now) => QuartermasterCommissions.PostIfDue(now, DateTime.UtcNow);
        public void Flush(bool wait) { }
    }

    /// <summary>Crafted pieces taken back from an expired listing: they go to a vendor next, not back to the Exchange.</summary>
    private static readonly ConcurrentDictionary<string, byte> Reclaimed = new(StringComparer.Ordinal);

    public static bool WasReclaimed(DbInventoryItem item) =>
        item?.ObjectId != null && Reclaimed.ContainsKey(item.ObjectId);

    public sealed class ItemPolicy : IBotItemPolicy
    {
        public static readonly ItemPolicy Instance = new();
        public string Name => "market (WP5)";

        /// <summary>DR: a piece made for another bot's order (or the quartermaster's) is never worn, listed or sold by its maker.</summary>
        public bool IsReserved(GameBot bot, DbInventoryItem item) => AutonomousBotCraftOrders.IsHeldForOrder(item);

        /// <summary>DR: no post when the realm already has 25 bot listings of the kind (a vendor may take it); a piece that
        /// came back unsold from the Exchange goes to a vendor.</summary>
        public bool BlocksListing(GameBot bot, DbInventoryItem item) =>
            RealmExchangeSaturation.On && bot != null && (RealmExchangeSaturation.IsSaturated(bot.Realm, item) || WasReclaimed(item));

        /// <summary>DR: count the post at once; the index hears of it; the ECON_LIST line when asked for.</summary>
        public void OnListed(GameBot bot, DbInventoryItem item)
        {
            if (RealmExchangeSaturation.On)
                RealmExchangeSaturation.NoteListed(item);
            MarketPriceIndex.Instance.NoteListed(item);
            if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled && bot != null && item != null)
                Log.Info(EconomyLog.Line(EconomyLog.EconList, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("item", item.Name), ("itemId", item.Id_nb), ("level", item.Level), ("quality", item.Quality),
                    ("crafted", item.IsCrafted), ("price", item.SellPrice), ("band", MarketPriceIndex.BandOf(item).Key)));
        }
    }

    /// <summary>DR (ceac061a): a crafter's own work has more posts than the 3 every bot has; bot_crafter_exchange_listings in all.</summary>
    public sealed class ListingPolicy : IExchangeListingPolicy
    {
        public static readonly ListingPolicy Instance = new();

        /// <summary>bot_crafter_exchange_listings, never below the 3 every bot has (Consignment slots bound it above).</summary>
        public static int CrafterCap => Math.Clamp(CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS,
            AutonomousBotEconomy.MaxBotRealmExchangeListings,
            eInventorySlot.Consignment_Last - eInventorySlot.Consignment_First + 1);

        private static GameBot Owner(string ownerId) =>
            AutonomousBotEconomy.TryParseOwnerId(ownerId, out long botId) ? FindLive(botId) : null;

        public int MaxListings(string ownerId) =>
            AutonomousBotCrafting.Enabled && Owner(ownerId) is GameBot bot && AutonomousBotCrafting.IsCrafter(bot)
                ? CrafterCap
                : AutonomousBotEconomy.MaxBotRealmExchangeListings;

        public int ListingCap(string ownerId, DbInventoryItem item) =>
            AutonomousBotCrafting.Enabled && item != null && Owner(ownerId) is GameBot bot && AutonomousBotCrafting.IsOwnWork(bot, item)
                ? CrafterCap
                : AutonomousBotEconomy.MaxBotRealmExchangeListings;
    }

    /// <summary>
    /// A committed sale (under the Exchange's locks; counters only): DR's sale line with crafted=1|0 and the tax, the index's
    /// sale, and a crafter's earnings from its own work. The line is written while bot_crafters or econ_trade_lines is on.
    /// </summary>
    public static void OnSaleCommitted(RealmExchangeSaleLedger.Sale sale)
    {
        if (sale == null)
            return;
        MarketPriceIndex.Instance.NoteSale(sale);
        if (sale.Crafted && AutonomousBotCrafting.Enabled && AutonomousBotEconomy.TryParseOwnerId(sale.SellerId, out long sellerBotId))
            BotCrafterRoster.Instance.NoteEarned(sellerBotId, Math.Max(0, sale.PriceCopper - sale.TaxCopper));
        if ((CraftingEconomyProperties.BOT_CRAFTERS || EconomyDiagnostics.TradeLines) && Log.IsInfoEnabled)
            Log.Info(SaleLine(sale));
    }

    /// <summary>DividedRealms' AUTONOMOUS_EXCHANGE_SOLD line (ceac061a), its keys in its order (realm as its number), and the tax.</summary>
    public static string SaleLine(RealmExchangeSaleLedger.Sale sale) =>
        EconomyLog.Line(EconomyLog.DividedRealmsExchangeSold, ("realm", sale.Realm), ("item", sale.ItemName), ("quantity", sale.Quantity),
            ("price", sale.PriceCopper), ("seller", sale.SellerName), ("sellerId", sale.SellerId), ("buyer", sale.BuyerName),
            ("buyerId", sale.BuyerId), ("crafted", sale.Crafted), ("tax", sale.TaxCopper));

    /// <summary>
    /// An expired gamebot listing (the Exchange's sweep; under its locks): a crafter's own work goes back into its bag
    /// when it is in the world with room (design §4 K; plan §5 decision 3, proposed), and to a vendor at its next break.
    /// Anything else, or with no room, is deleted as before. bot_crafters only.
    /// </summary>
    public static bool ReclaimExpired(DbInventoryItem item, long botId)
    {
        GameBot bot = AutonomousBotCrafting.Enabled && item != null && botId > 0 ? FindLive(botId) : null;
        if (bot?.Inventory == null || !AutonomousBotCrafting.IsCrafter(bot) || !AutonomousBotCrafting.IsOwnWork(bot, item))
            return false;
        eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
        if (slot == eInventorySlot.Invalid)
            return false;
        ushort lot = item.OwnerLot;
        int price = (int)Math.Min(int.MaxValue, item.SellPrice);
        int listedSlot = item.SlotPosition;
        string listedUtc = item.RealmExchangeListedUtc;
        MarketCache.RemoveItem(item);
        item.OwnerLot = 0;
        item.SellPrice = 0;
        item.RealmExchangeListedUtc = string.Empty;
        if (!bot.Inventory.AddItem(slot, item) || !GameServer.Database.SaveObject(item))
        {
            // Put the listing back as it was: the sweep deletes it as before.
            bot.Inventory.RemoveItemWithoutDbDeletion(item);
            item.OwnerLot = lot;
            item.SellPrice = price;
            item.SlotPosition = listedSlot;
            item.RealmExchangeListedUtc = listedUtc;
            MarketCache.AddItem(item);
            return false;
        }
        if (item.ObjectId != null)
            Reclaimed[item.ObjectId] = 0;
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconReclaim, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                ("item", item.Name), ("quality", item.Quality), ("price", price), ("outcome", "back to the crafter")));
        return true;
    }

    /// <summary>/lscraft commission (registered with WP10's CraftEconomyTestHooks).</summary>
    public static string[] CommissionLines() => QuartermasterCommissions.ReportLines();

    /// <summary>/lscraft index (registered with WP10's CraftEconomyTestHooks).</summary>
    public static string[] IndexLines(DbInventoryItem item) => MarketPriceIndex.Instance.ReportLines(item);
}
