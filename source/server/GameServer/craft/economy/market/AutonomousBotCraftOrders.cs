using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Craft orders (Crafting 2.0 WP5): the order board of WP0's <see cref="ICraftOrderBoard"/> and the rules that fill it.
/// <para>Ported from rambodolphin13's DividedRealms (AutonomousBotCraftOrders, ceac061a → e37f8810 → 24f8a2e7), GPL-3.0:
/// at a natural task break a gamebot with coin that a crafted piece would upgrade pays a crafter of its realm whose skill
/// makes the recipe orange or easier. The coin leaves the buyer's purse at once and is held by the saved order
/// (offline_bot_craft_orders, DR's table and columns). The crafter makes the piece first on its next craft trip, with
/// materials it buys itself, and at the end of that break hands the real item into the buyer's backpack and takes the
/// coin. An order not delivered in <see cref="Lifetime"/> refunds the buyer; a piece already made stays with its maker.</para>
/// <para>Changes in the port: the board also carries the realm quartermaster's commissions
/// (<see cref="QuartermasterCommissions"/>) and True165's milestone sets (<see cref="MilestoneCraftSets"/>); recipes are
/// WP1's catalog (<see cref="CraftRecipeInfo"/>) and the chances CraftRules; only Crafter-type gamebots fill orders and only
/// while bot_crafters and (for the bots' own orders) bot_craft_orders are on, both off by default; reads from parallel
/// brain turns go through an immutable snapshot (no lock); coin moves outside the board's lock (WP0's lock order) and
/// each names its kind for the economy ledger; a delivery waits for the database writer only briefly
/// (<see cref="AutonomousBotStatusPersistence.TryEnterForAiTurn"/>); ECON_ORDER lines replace DR's AUTONOMOUS_CRAFT_ORDER_*.</para>
/// Only autonomous gamebots order or fill orders; companion bots and players never reach this code.
/// </summary>
public static class AutonomousBotCraftOrders
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotCraftOrders));

    /// <summary>The bots' own orders: bot_crafters and DR's bot_craft_orders (both off by default).</summary>
    public static bool Enabled => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_CRAFT_ORDERS;

    /// <summary>Any order on the board is worked (bot_crafters): the quartermaster's need no bot_craft_orders.</summary>
    public static bool BoardOn => CraftingEconomyProperties.BOT_CRAFTERS;

    public const string OpenState = OfflineBotCraftOrderRecord.OpenState;
    public const string MadeState = OfflineBotCraftOrderRecord.MadeState;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);
    /// <summary>DR (e37f8810): a crafter makes orders only at a task break (45-120 minute tasks), so it is asked only when
    /// its current task ends within this window.</summary>
    public static readonly TimeSpan BreakWindow = TimeSpan.FromMinutes(45);
    /// <summary>Chance, at a task break, that a bot with no order looks for one (DR).</summary>
    public const double CommissionChance = 0.25;
    /// <summary>A crafter fills orders it makes at this chance or better: orange, yellow or easier (DR).</summary>
    public const int MinimumMakeChance = 84;
    public const int MaximumOpenPerCrafter = 2;
    /// <summary>A buyer spends at most this share of its coin on one order (DR).</summary>
    public const double BuyerShareOfCoin = 0.5;
    /// <summary>The crafter's fee on top of the materials: a share of their cost, at least <see cref="MinimumFee"/> (DR's
    /// price; True165's materials x 1.75 is the other option, plan §5 decision 2).</summary>
    public const double FeeShare = 0.25;
    public const long MinimumFee = 100;
    public const long ExpiryPassMilliseconds = 60_000;
    /// <summary>DR (24f8a2e7): an order may be up to this many levels below its buyer.</summary>
    public const int CommissionLevelWindow = 20;

    /// <summary>The crafts whose products are worn (the bots' own orders and the milestone sets).</summary>
    public static readonly eCraftingSkill[] GearCrafts =
        [eCraftingSkill.ArmorCrafting, eCraftingSkill.WeaponCrafting, eCraftingSkill.Tailoring, eCraftingSkill.Fletching];

    internal static LivingServerTable<string, OfflineBotCraftOrderRecord> Orders =
        new("craft order", row => row.OrderId, StringComparer.Ordinal);

    /// <summary>Made pieces waiting for delivery, by item id: never worn, sold or listed by their maker (DR).</summary>
    private static readonly ConcurrentDictionary<string, string> Held = new(StringComparer.Ordinal);
    /// <summary>Orders a delivery or the expiry pass is working on, so the two never both settle one.</summary>
    private static readonly ConcurrentDictionary<string, byte> Claimed = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static long _nextExpiryPass;

    /// <summary>Every order, by crafter and by buyer, as of the last change: what brain turns read, without a lock.</summary>
    private sealed record Index(IReadOnlyDictionary<long, CraftOrder[]> ByCrafter, IReadOnlyDictionary<string, CraftOrder[]> ByBuyer, int Count)
    {
        public static readonly Index Empty = new(new Dictionary<long, CraftOrder[]>(), new Dictionary<string, CraftOrder[]>(), 0);
    }

    private static volatile Index _index = Index.Empty;

    // The coin and roster this works with; tests replace them (ResetForTests puts them back).
    public static Func<long, long, EconomyFlowKind, bool> SpendCoin = DefaultSpend;
    public static Func<long, long, EconomyFlowKind, bool> PayCoin = DefaultPay;
    public static Func<long, GameBot> FindLive = DefaultFindLive;
    public static Func<eRealm, IEnumerable<GameBot>> CraftersOf = DefaultCraftersOf;
    /// <summary>A live gamebot's purse (AutonomousBotEconomy.GetMoney; tests replace it).</summary>
    public static Func<long, long> CoinOf = AutonomousBotEconomy.GetMoney;

    /// <summary>
    /// WP8's seam for a real player's order (<see cref="CraftOrderSource.Player"/>): the hand-over at the crafter's break
    /// (true when the piece reached the player and the order is closed), and the player's escrow when an order ends
    /// without a delivery (refund, outcome). The defaults keep a player's order on the board until it lapses, unpaid.
    /// </summary>
    public static Func<GameBot, CraftOrder, DbInventoryItem, bool> DeliverToPlayer = (_, _, _) => false;
    public static Action<CraftOrder, bool, string> PlayerOrderClosed = (_, _, _) => { };
    /// <summary>WP8b (bot-to-bot guild orders): a crafter the buyer prefers (its guildmate). Default: none.</summary>
    public static Func<GameBot, long, bool> PreferCrafter = (_, _) => false;
    /// <summary>WP8b: the price a buyer pays this crafter (a guildmate: the materials only); null for the board's quote.</summary>
    public static Func<GameBot, GameBot, CraftRecipeInfo, long?> GuildPrice = (_, _, _) => null;
    /// <summary>WP8b: a bot's order handed over and paid (a Crafting guild's bank then pays the waived fee).</summary>
    public static Action<GameBot, GameBot, CraftOrder> BotOrderDelivered = (_, _, _) => { };

    /// <summary>WP8: a player's open order (nothing made yet) is withdrawn; a made one stays to be handed over or to lapse.</summary>
    public static bool TryCancelOpen(string orderId, string reason)
    {
        if (!Orders.TryGet(orderId, out OfflineBotCraftOrderRecord row) || row.State != OpenState || !Claimed.TryAdd(orderId, 0))
            return false;
        try
        {
            Close(ToOrder(row), refund: true, reason, "cancelled");
            return true;
        }
        finally
        {
            Claimed.TryRemove(orderId, out _);
        }
    }
    /// <summary>Whether a recipe's materials are on sale to this realm (AutonomousBotCrafting.OnSale; tests replace it).</summary>
    public static Func<CraftRecipeInfo, eRealm, bool> OnSale = AutonomousBotCrafting.OnSale;

    // Every coin move names its kind (the economy ledger's source scan): escrow, refund and payout are one kind, so the
    // kind's net is the coin the open orders hold.
    private static bool DefaultSpend(long botId, long copper, EconomyFlowKind kind) => AutonomousBotEconomy.TrySpend(botId, copper, kind);
    private static bool DefaultPay(long botId, long copper, EconomyFlowKind kind) => AutonomousBotEconomy.AddMoney(botId, copper, kind);

    private static GameBot DefaultFindLive(long botId) => AutonomousBotRegistry.TryGet(botId, out GameBot bot) ? bot : null;

    private static IEnumerable<GameBot> DefaultCraftersOf(eRealm realm) =>
        AutonomousBotRegistry.Snapshot().Where(bot => bot != null && bot.Realm == realm && AutonomousBotCrafting.IsCrafter(bot) &&
                                                      AutonomousBotCrafting.IsTrained(bot));

    #region State

    /// <summary>Fills the in-memory state from the saved rows (start-up, and tests).</summary>
    public static void Load(IEnumerable<OfflineBotCraftOrderRecord> rows)
    {
        OfflineBotCraftOrderRecord[] all = (rows ?? []).Where(row => row != null && !string.IsNullOrEmpty(row.OrderId)).ToArray();
        lock (Gate)
        {
            Orders.Load(all);
            Held.Clear();
            Claimed.Clear();
            foreach (OfflineBotCraftOrderRecord row in all.Where(row => row.State == MadeState && !string.IsNullOrEmpty(row.ItemObjectId)))
                Held[row.ItemObjectId] = row.OrderId;
            Rebuild();
        }
    }

    // Caller holds Gate.
    private static void Rebuild()
    {
        CraftOrder[] all = Orders.Where(_ => true).Select(ToOrder).ToArray();
        _index = new Index(
            all.GroupBy(order => order.CrafterBotId).ToDictionary(group => group.Key, group => group.OrderBy(o => o.CreatedUtc).ToArray()),
            all.GroupBy(order => order.BuyerId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal),
            all.Length);
    }

    public static int Count => _index.Count;

    public static IReadOnlyList<CraftOrder> OfCrafter(long crafterId) =>
        _index.ByCrafter.TryGetValue(crafterId, out CraftOrder[] orders) ? orders : Array.Empty<CraftOrder>();

    public static IReadOnlyList<CraftOrder> OfBuyer(string buyerId) =>
        buyerId != null && _index.ByBuyer.TryGetValue(buyerId, out CraftOrder[] orders) ? orders : Array.Empty<CraftOrder>();

    /// <summary>Every order a bot is part of, as buyer or crafter (DR's OrdersOf).</summary>
    public static CraftOrder[] OrdersOf(long botId) =>
        OfCrafter(botId).Concat(OfBuyer(AutonomousBotEconomy.GetOwnerId(botId))).Distinct().ToArray();

    public static IEnumerable<CraftOrder> All() => _index.ByCrafter.Values.SelectMany(orders => orders);

    public static bool IsHeldForOrder(DbInventoryItem item) =>
        item != null && !string.IsNullOrEmpty(item.ObjectId) && Held.ContainsKey(item.ObjectId);

    private static int OpenFor(long crafterId) => OfCrafter(crafterId).Count;

    /// <summary>A crafter with an order still to make goes on a craft trip at its next task break (DR).</summary>
    public static bool HasOpenWork(GameBot crafter) =>
        BoardOn && crafter != null && OfCrafter(crafter.DatabaseID).Any(order => order.State == CraftOrderState.Open);

    public static CraftOrder ToOrder(OfflineBotCraftOrderRecord row) => new(row.OrderId, (eRealm)row.Realm,
        Enum.IsDefined(typeof(CraftOrderSource), row.Source) ? (CraftOrderSource)row.Source : CraftOrderSource.Bot,
        row.BuyerBotId, string.IsNullOrEmpty(row.BuyerId) && row.BuyerBotId > 0 ? AutonomousBotEconomy.GetOwnerId(row.BuyerBotId) : row.BuyerId ?? string.Empty,
        row.BuyerName ?? string.Empty, row.CrafterBotId, (eCraftingSkill)row.CraftingSkill, row.ProductId ?? string.Empty,
        row.ProductName ?? string.Empty, row.RecipeLevel, row.MinQuality, Math.Max(1, row.Count), row.Copper, row.PayUpFront,
        CraftOrders.ParseState(row.State), row.ItemObjectId ?? string.Empty, ParseTime(row.CreatedUtc), ParseTime(row.ExpiresUtc));

    public static OfflineBotCraftOrderRecord ToRecord(CraftOrder order) => new()
    {
        OrderId = order.OrderId,
        BuyerBotId = order.BuyerBotId,
        CrafterBotId = order.CrafterBotId,
        Realm = (int)order.Realm,
        CraftingSkill = (int)order.Skill,
        ProductId = order.ProductId ?? string.Empty,
        ProductName = order.ProductName ?? string.Empty,
        RecipeLevel = order.RecipeLevel,
        Copper = order.EscrowCopper,
        State = CraftOrders.StateText(order.State),
        ItemObjectId = order.ItemObjectId ?? string.Empty,
        CreatedUtc = order.CreatedUtc.ToUniversalTime().ToString("O"),
        ExpiresUtc = order.ExpiresUtc.ToUniversalTime().ToString("O"),
        Source = (int)order.Source,
        BuyerId = order.BuyerId ?? string.Empty,
        BuyerName = order.BuyerName ?? string.Empty,
        MinQuality = order.MinQuality,
        Count = Math.Max(1, order.Count),
        PayUpFront = order.PayUpFront,
    };

    private static DateTime ParseTime(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time) ? time.ToUniversalTime() : DateTime.MinValue;

    #endregion

    #region Rules (pure)

    /// <summary>What a piece costs its buyer: the materials at the merchant's price and the crafter's fee (DR).</summary>
    public static long Quote(CraftRecipeInfo recipe) => Quote(recipe?.MaterialCostCopper ?? 0);

    public static long Quote(long materialCopper) =>
        Math.Max(0, materialCopper) + Math.Max(MinimumFee, (long)Math.Ceiling(Math.Max(0, materialCopper) * FeeShare));

    /// <summary>A crafter can take the order: it knows the craft and makes the recipe at <see cref="MinimumMakeChance"/> (DR).</summary>
    public static bool CanFill(string skills, CraftRecipeInfo recipe)
    {
        if (recipe == null)
            return false;
        int skill = BotCraftingSkills.Value(skills, recipe.Skill);
        return skill > 0 && CraftRules.ChanceToMake(skill, recipe.Level) >= MinimumMakeChance &&
               BotCraftingRules.MeetsSecondaryRequirement(recipe, skills);
    }

    /// <summary>
    /// When the crafter next reaches a task break where it can craft: now while on a craft trip, the end of its task
    /// otherwise (now with no task). Never while grouped (group bots get no break) or on a break that is not a craft
    /// trip (DR, e37f8810).
    /// </summary>
    public static DateTime NextCraftBreak(GameBot crafter)
    {
        OfflineWorldBotRecord record = crafter?.PersistentRecord;
        if (record == null || crafter.Group != null)
            return DateTime.MaxValue;
        if (AutonomousObjectiveAssignments.IsBetweenPveTasks(record))
            return AutonomousObjectiveAssignments.HasBetweenTaskFlag(record, BotCrafterSeams.TripFlag) ? DateTime.MinValue : DateTime.MaxValue;
        return DateTime.TryParse(record.ObjectiveExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime end)
            ? end.ToUniversalTime()
            : DateTime.MinValue;
    }

    /// <summary>Whether this gamebot may fill orders at all: a Crafter-type autonomous gamebot (never a companion) of the level.</summary>
    public static bool IsAbleCrafter(GameBot crafter) =>
        crafter != null && crafter.DatabaseID > 0 && crafter.IsAutonomousWorldBot && !crafter.IsTemporaryGroupHelper &&
        crafter.Level >= BotCraftingRules.MinimumBotLevel && crafter.PersistentRecord != null;

    /// <summary>The crafter of the buyer's realm to ask: able to fill it and at a craft break within
    /// <see cref="BreakWindow"/>; least busy first, then the soonest break, then the most skilled (DR).</summary>
    public static GameBot PickCrafter(IEnumerable<GameBot> crafters, CraftRecipeInfo recipe, GameBot buyer) =>
        PickCrafter(crafters, recipe, buyer?.Realm ?? eRealm.None, buyer?.DatabaseID ?? 0, DateTime.UtcNow);

    public static GameBot PickCrafter(IEnumerable<GameBot> crafters, CraftRecipeInfo recipe, eRealm realm, long buyerBotId, DateTime utcNow)
    {
        if (recipe == null || realm == eRealm.None)
            return null;
        DateTime latest = utcNow + BreakWindow;
        return (crafters ?? [])
            .Where(crafter => IsAbleCrafter(crafter) && crafter.DatabaseID != buyerBotId && crafter.Realm == realm &&
                              CanFill(crafter.PersistentRecord.SerializedCraftingSkills, recipe) &&
                              OpenFor(crafter.DatabaseID) < MaximumOpenPerCrafter && NextCraftBreak(crafter) <= latest)
            // WP8b: a crafter of the buyer's own guild first (bot_crafting_social; nobody is preferred otherwise).
            .OrderByDescending(crafter => PreferCrafter(crafter, buyerBotId))
            .ThenBy(crafter => OpenFor(crafter.DatabaseID))
            .ThenBy(crafter => NextCraftBreak(crafter))
            .ThenByDescending(crafter => BotCraftingSkills.Value(crafter.PersistentRecord.SerializedCraftingSkills, recipe.Skill))
            .ThenBy(crafter => crafter.DatabaseID)
            .FirstOrDefault();
    }

    /// <summary>
    /// The piece to order (DR): of the buyer's realm and level window, within its share of coin, an upgrade to what it
    /// wears and with a crafter to make it. The biggest upgrade first, the cheaper among equals.
    /// </summary>
    public static CraftRecipeInfo ChooseCommission(IEnumerable<CraftRecipeInfo> recipes, eRealm realm, int buyerLevel, long coin,
        Func<CraftRecipeInfo, int> upgradeGain, Func<CraftRecipeInfo, bool> hasCrafter)
    {
        long budget = (long)(coin * BuyerShareOfCoin);
        return (recipes ?? [])
            .Where(recipe => recipe != null && !recipe.Stackable && (recipe.Realm == 0 || recipe.Realm == (int)realm) &&
                             FitsCommissionLevel(recipe.ProductLevel, buyerLevel) && Quote(recipe) <= budget)
            .Select(recipe => (Recipe: recipe, Gain: upgradeGain(recipe)))
            .Where(candidate => candidate.Gain > 0)
            .OrderByDescending(candidate => candidate.Gain)
            .ThenBy(candidate => Quote(candidate.Recipe))
            .ThenBy(candidate => candidate.Recipe.RecipeId, StringComparer.Ordinal)
            .Select(candidate => candidate.Recipe)
            .FirstOrDefault(hasCrafter);
    }

    /// <summary>
    /// DR's night of 2026-11-04: 2 craft orders against 88 the night before. Almost no bot was under level 20 any more and
    /// the crafters' skill ended at a median of 8, so with a 10-level window no crafter could make anything a buyer would
    /// take. Gamebots start without armor and earn it, so a lower piece is still a real upgrade to many of them.
    /// </summary>
    public static bool FitsCommissionLevel(int productLevel, int buyerLevel) =>
        productLevel <= buyerLevel && productLevel + CommissionLevelWindow >= buyerLevel;

    /// <summary>How much the product would raise what the buyer wears in its slot, by the equipment upgrade rules; 0 if
    /// it is no upgrade (DR).</summary>
    public static int UpgradeGain(GameBot buyer, CraftRecipeInfo recipe, int quality = 94)
    {
        try
        {
            DbItemTemplate product = recipe?.Recipe?.Product;
            if (buyer?.Inventory == null || product == null)
                return 0;
            DbInventoryItem piece = GameInventoryItem.Create(product);
            if (recipe.Unique)
                piece.Quality = Math.Max(piece.Quality, quality);
            if (!AutonomousBotEconomy.TryGetEquipmentUpgrade(buyer, piece, out eInventorySlot slot))
                return 0;
            DbInventoryItem worn = buyer.Inventory.GetItem(slot);
            return Math.Max(1, AutonomousBotEconomy.EquipmentValue(piece) - AutonomousBotEconomy.EquipmentValue(worn));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Unreadable times count as due, so a damaged row cannot hold a buyer's coin for ever (DR).</summary>
    public static bool IsDue(string expiresUtc, DateTime utcNow) =>
        !DateTime.TryParse(expiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime due) ||
        due.ToUniversalTime() <= utcNow;

    #endregion

    #region Placing

    /// <summary>
    /// At a task break (WP0's IBotTaskBreakHook): deliver what this bot made for others, then perhaps order for itself
    /// (True165's milestone set first, else DR's single upgrade at <see cref="CommissionChance"/>). At the end of a
    /// break (<paramref name="commission"/> false) it only delivers what the craft trip made (DR e37f8810).
    /// </summary>
    public static void AtTaskBreak(GameBot bot, bool commission = true, Random random = null)
    {
        if (!BoardOn || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0)
            return;
        try
        {
            using var aiProfile = AiProfile.Scope(AiSystem.EconomyMarket); // AI_PROFILE: measures only
            Deliver(bot);
            if (!commission || !Enabled)
                return;
            if (MilestoneCraftSets.TryPost(bot) > 0)
                return;
            if ((random ?? Random.Shared).NextDouble() < CommissionChance)
                TryCommission(bot);
        }
        catch (Exception exception)
        {
            Log.Warn($"Craft orders: task break of {bot.Name} failed", exception);
        }
    }

    /// <summary>The gear recipes of the catalog (empty while it loads).</summary>
    public static IEnumerable<CraftRecipeInfo> GearRecipes() => GearCrafts.SelectMany(AutonomousBotCrafting.RecipesOf);

    /// <summary>Orders the best upgrade a crafter of the realm can make, if the buyer has the coin (DR). Returns the order.</summary>
    public static CraftOrder TryCommission(GameBot buyer)
    {
        if (!Enabled || buyer?.IsAutonomousWorldBot != true || buyer.IsTemporaryGroupHelper || buyer.DatabaseID <= 0 ||
            buyer.Inventory == null || OfBuyer(AutonomousBotEconomy.GetOwnerId(buyer.DatabaseID)).Count > 0)
            return null;
        GameBot[] crafters = CraftersOf(buyer.Realm).ToArray();
        if (crafters.Length == 0)
            return null;
        long coin = CoinOf(buyer.DatabaseID);
        CraftRecipeInfo recipe = ChooseCommission(GearRecipes(), buyer.Realm, buyer.Level, coin, candidate => UpgradeGain(buyer, candidate),
            candidate => OnSale(candidate, buyer.Realm) && PickCrafter(crafters, candidate, buyer) != null);
        GameBot crafter = recipe == null ? null : PickCrafter(crafters, recipe, buyer);
        if (crafter == null)
            return null;
        MarketPriceIndex.Instance.ObserveDemand(MarketPriceIndex.BandOf(buyer.Realm, recipe));
        // WP8b: a guildmate crafter is paid the materials only.
        long? guildPrice = GuildPrice(buyer, crafter, recipe);
        return Place(buyer, crafter, recipe, guildPrice, guildPrice != null ? "guild" : "bot");
    }

    /// <summary>Takes the price from the buyer's purse into a new order for the crafter (DR). Null when the buyer cannot pay.</summary>
    public static CraftOrder Place(GameBot buyer, GameBot crafter, CraftRecipeInfo recipe, long? price = null, string source = "bot")
    {
        if (buyer == null || crafter == null || recipe == null)
            return null;
        long copper = price ?? Quote(recipe);
        // The coin moves before the board's lock is taken (lock order: the money lock, then a package's own).
        if (!SpendCoin(buyer.DatabaseID, copper, EconomyFlowKind.CraftOrder))
            return null;
        DateTime now = DateTime.UtcNow;
        var order = new CraftOrder(Guid.NewGuid().ToString("N"), buyer.Realm, CraftOrderSource.Bot, buyer.DatabaseID,
            AutonomousBotEconomy.GetOwnerId(buyer.DatabaseID), buyer.Name ?? string.Empty, crafter.DatabaseID, recipe.Skill,
            recipe.ProductId ?? string.Empty, recipe.ProductName ?? string.Empty, recipe.Level, 0, 1, copper, true,
            CraftOrderState.Open, string.Empty, now, now + Lifetime);
        if (!TryAdd(order))
        {
            PayCoin(buyer.DatabaseID, copper, EconomyFlowKind.CraftOrder);
            return null;
        }
        buyer.MarkAutonomousStateDirty();
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconOrder, ("outcome", "placed"), ("source", source), ("order", order.OrderId),
                ("realm", buyer.Realm), ("buyer", buyer.Name), ("buyerId", buyer.DatabaseID), ("level", (int)buyer.Level),
                ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("craft", recipe.Skill), ("recipeLevel", recipe.Level),
                ("item", order.ProductName), ("itemId", order.ProductId), ("copper", copper)));
        return order;
    }

    /// <summary>Adds an order to the board (any source); false for a duplicate id or a missing crafter.</summary>
    public static bool TryAdd(CraftOrder order)
    {
        if (order == null || string.IsNullOrEmpty(order.OrderId) || order.CrafterBotId <= 0)
            return false;
        lock (Gate)
        {
            if (!Orders.TryAdd(order.OrderId, ToRecord(order)))
                return false;
            Rebuild();
        }
        return true;
    }

    #endregion

    #region Making

    /// <summary>The order this crafter makes on its trip, oldest first, if it knows the recipe and can pay the materials (DR).</summary>
    public static BotCraftingRules.Session PlanOrder(GameBot crafter, long coin)
    {
        if (!BoardOn || crafter == null)
            return null;
        string skills = crafter.PersistentRecord?.SerializedCraftingSkills;
        foreach (CraftOrder order in OfCrafter(crafter.DatabaseID).Where(order => order.State == CraftOrderState.Open))
        {
            CraftRecipeInfo recipe = AutonomousBotCrafting.RecipesOf(order.Skill)
                .FirstOrDefault(candidate => string.Equals(candidate.ProductId, order.ProductId, StringComparison.OrdinalIgnoreCase) &&
                                             candidate.Level == order.RecipeLevel)
                ?? AutonomousBotCrafting.RecipesOf(order.Skill)
                    .FirstOrDefault(candidate => string.Equals(candidate.ProductId, order.ProductId, StringComparison.OrdinalIgnoreCase));
            // An order the crafter cannot make now stays open and lapses when it expires (DR).
            if (recipe == null || CraftRules.ChanceToMake(BotCraftingSkills.Value(skills, order.Skill), recipe.Level) <= 0 ||
                recipe.MaterialCostCopper > coin || !OnSale(recipe, crafter.Realm))
                continue;
            return new BotCraftingRules.Session(recipe, order.Skill, 1, order.OrderId);
        }
        return null;
    }

    /// <summary>The ordered piece is made: it waits in the crafter's bag for delivery (DR). True when an open order took it.</summary>
    public static bool OnMade(GameBot crafter, string orderId, DbInventoryItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.ObjectId) || string.IsNullOrEmpty(orderId))
            return false;
        OfflineBotCraftOrderRecord row;
        lock (Gate)
        {
            if (!Orders.TryGet(orderId, out row) || row.State != OpenState)
                return false;
            Orders.Change(orderId, () => row, order =>
            {
                order.State = MadeState;
                order.ItemObjectId = item.ObjectId;
            });
            Held[item.ObjectId] = orderId;
            Rebuild();
        }
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconOrder, ("outcome", "made"), ("source", SourceText((CraftOrderSource)row.Source)),
                ("order", orderId), ("realm", (eRealm)row.Realm), ("crafter", crafter?.Name ?? string.Empty),
                ("crafterId", crafter?.DatabaseID ?? row.CrafterBotId), ("item", item.Name), ("quality", item.Quality)));
        return true;
    }

    #endregion

    #region Delivering

    /// <summary>
    /// The crafter's task break (DR): each made piece goes straight from its bag into the buyer's backpack and the
    /// order's coin to the crafter; a quartermaster's piece goes to the realm (it leaves the world) and the realm pays the
    /// commission. A buyer who is not in the world or has a full bag is tried again at the next break. A piece the
    /// crafter no longer has refunds the buyer. Returns how many were delivered.
    /// </summary>
    public static int Deliver(GameBot crafter)
    {
        if (crafter?.Inventory == null || crafter.DatabaseID <= 0)
            return 0;
        int delivered = 0;
        foreach (CraftOrder order in OfCrafter(crafter.DatabaseID).Where(order => order.State == CraftOrderState.Made).ToArray())
        {
            if (!Claimed.TryAdd(order.OrderId, 0))
                continue;
            try
            {
                if (TryDeliverClaimed(crafter, order))
                    delivered++;
            }
            finally
            {
                Claimed.TryRemove(order.OrderId, out _);
            }
        }
        return delivered;
    }

    private static bool TryDeliverClaimed(GameBot crafter, CraftOrder order)
    {
        if (!Orders.TryGet(order.OrderId, out OfflineBotCraftOrderRecord row) || row.State != MadeState)
            return false;
        DbInventoryItem piece = crafter.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
            .FirstOrDefault(item => item != null && item.ObjectId == order.ItemObjectId);
        if (piece == null)
        {
            Close(order, refund: true, "the piece is no longer in the crafter's bag", "refunded");
            return false;
        }
        if (order.Source == CraftOrderSource.Quartermaster)
            return QuartermasterCommissions.Deliver(crafter, order, piece);
        // A real player's order (WP8): handed over in person at the crafter's break, never into an absent player's bag.
        if (order.Source == CraftOrderSource.Player)
            return DeliverToPlayer?.Invoke(crafter, order, piece) == true;
        GameBot buyer = FindLive(order.BuyerBotId);
        if (buyer?.Inventory == null || buyer.Realm != crafter.Realm)
            return false;
        if (!Hand(crafter, buyer, piece, out bool deferred))
            return false;
        Remove(order);
        bool paid = PayCoin(crafter.DatabaseID, order.EscrowCopper, EconomyFlowKind.CraftOrder);
        BotCrafterRoster.Instance.NoteEarned(crafter.DatabaseID, order.EscrowCopper);
        foreach (GameBot bot in new[] { crafter, buyer })
        {
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
        }
        bool worn = AutonomousBotEconomy.TryEquipOwnedUpgrade(buyer, piece, "craft-order");
        try { BotOrderDelivered(crafter, buyer, order); }
        catch { } // another package's failure never stops a hand-over
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconOrder, ("outcome", "delivered"), ("source", SourceText(order.Source)),
                ("order", order.OrderId), ("realm", crafter.Realm), ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID),
                ("buyer", buyer.Name), ("buyerId", buyer.DatabaseID), ("item", piece.Name), ("quality", piece.Quality),
                ("copper", order.EscrowCopper), ("paid", paid), ("worn", worn)));
        return true;
    }

    /// <summary>Moves the real item between the two bags, as the Realm Exchange moves a sold item; nothing is copied (DR).
    /// The database writer is waited for only briefly (<paramref name="deferred"/>: try at the next break).</summary>
    internal static bool Hand(GameBot crafter, GameBot buyer, DbInventoryItem piece, out bool deferred)
    {
        deferred = false;
        using (AutonomousBotStatusPersistence.TryEnterForAiTurn(crafter, "craft-order", out bool entered))
        {
            if (!entered)
            {
                deferred = true;
                return false;
            }
            eInventorySlot slot = buyer.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            int originalSlot = piece.SlotPosition;
            if (slot == eInventorySlot.Invalid || !crafter.Inventory.RemoveItemWithoutDbDeletion(piece))
                return false;
            if (buyer.Inventory.AddItem(slot, piece))
                return true;
            crafter.Inventory.AddItem((eInventorySlot)originalSlot, piece);
            return false;
        }
    }

    /// <summary>Takes an order off the board (delivered).</summary>
    internal static void Remove(CraftOrder order)
    {
        lock (Gate)
        {
            Orders.Remove(order.OrderId);
            if (!string.IsNullOrEmpty(order.ItemObjectId))
                Held.TryRemove(order.ItemObjectId, out _);
            Rebuild();
        }
    }

    /// <summary>Ends an order; with <paramref name="refund"/> a paying buyer gets its coin back (DR). The coin moves after the lock.</summary>
    private static void Close(CraftOrder order, bool refund, string reason, string outcome)
    {
        lock (Gate)
        {
            if (!Orders.Remove(order.OrderId))
                return;
            if (!string.IsNullOrEmpty(order.ItemObjectId))
                Held.TryRemove(order.ItemObjectId, out _);
            Rebuild();
        }
        bool escrowed = order.PayUpFront && order.BuyerBotId > 0 && order.EscrowCopper > 0;
        bool refunded = refund && escrowed && PayCoin(order.BuyerBotId, order.EscrowCopper, EconomyFlowKind.CraftOrder);
        if (order.Source == CraftOrderSource.Quartermaster)
            QuartermasterCommissions.NoteExpired(order);
        // A real player's escrow goes back to the player (WP8; BuyerBotId is 0, so the refund above never pays it).
        if (order.Source == CraftOrderSource.Player)
            PlayerOrderClosed?.Invoke(order, refund, outcome);
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconOrder, ("outcome", outcome), ("source", SourceText(order.Source)),
                ("order", order.OrderId), ("realm", order.Realm), ("buyer", order.BuyerName), ("buyerId", order.BuyerId),
                ("crafterId", order.CrafterBotId), ("item", order.ProductName), ("state", CraftOrders.StateText(order.State)),
                ("copper", order.EscrowCopper), ("refunded", refunded), ("reason", reason)));
        if (refund && escrowed && !refunded)
            Log.Warn($"Craft orders: could not refund {order.EscrowCopper} copper to bot {order.BuyerBotId} (order {order.OrderId})");
    }

    /// <summary>Refunds every order past its time (DR). A made piece stays with its maker as its own work. Returns how many.</summary>
    public static int ExpireDue(DateTime utcNow)
    {
        int expired = 0;
        foreach (CraftOrder order in All().Where(order => order.ExpiresUtc <= utcNow).ToArray())
        {
            if (!Claimed.TryAdd(order.OrderId, 0))
                continue;
            try
            {
                if (!Orders.TryGet(order.OrderId, out _))
                    continue;
                Close(order, refund: true, "expired", "expired");
                expired++;
            }
            finally
            {
                Claimed.TryRemove(order.OrderId, out _);
            }
        }
        return expired;
    }

    /// <summary>The flush timer (off the game loop): the expiry pass, once a minute (DR).</summary>
    public static void ExpireIfDue()
    {
        long now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextExpiryPass))
            return;
        Volatile.Write(ref _nextExpiryPass, now + ExpiryPassMilliseconds);
        try
        {
            ExpireDue(DateTime.UtcNow);
        }
        catch (Exception exception)
        {
            Log.Warn("Craft orders: expiry pass failed", exception);
        }
    }

    public static string SourceText(CraftOrderSource source) => source switch
    {
        CraftOrderSource.Quartermaster => "quartermaster",
        CraftOrderSource.Player => "player",
        _ => "bot",
    };

    #endregion

    /// <summary>WP0's ICraftOrderBoard over this board, and its store (load, expiry, flush).</summary>
    public sealed class Board : ICraftOrderBoard, IEconomyStore
    {
        public static readonly Board Instance = new();
        public string Name => "craft orders";

        public bool Post(CraftOrder order) => order != null && TryAdd(order);

        public IReadOnlyList<CraftOrder> Open(eRealm realm, eCraftingSkill skill, int maxRecipeLevel) =>
            All().Where(order => order.Realm == realm && order.Skill == skill && order.RecipeLevel <= maxRecipeLevel &&
                                 order.State == CraftOrderState.Open).ToArray();

        public IReadOnlyList<CraftOrder> ForCrafter(long crafterBotId) => OfCrafter(crafterBotId);
        public IReadOnlyList<CraftOrder> ForBuyer(string buyerId) => OfBuyer(buyerId);
        public bool IsHeldForOrder(DbInventoryItem item) => AutonomousBotCraftOrders.IsHeldForOrder(item);

        public bool TryMarkMade(string orderId, DbInventoryItem item)
        {
            CraftOrder order = All().FirstOrDefault(candidate => candidate.OrderId == orderId);
            return order != null && OnMade(FindLive(order.CrafterBotId), orderId, item);
        }

        public bool TryDeliver(string orderId)
        {
            CraftOrder order = All().FirstOrDefault(candidate => candidate.OrderId == orderId);
            GameBot crafter = order == null ? null : FindLive(order.CrafterBotId);
            if (crafter == null || order.State != CraftOrderState.Made || !Claimed.TryAdd(orderId, 0))
                return false;
            try { return TryDeliverClaimed(crafter, order); }
            finally { Claimed.TryRemove(orderId, out _); }
        }

        public int ExpireDue(DateTime utcNow) => AutonomousBotCraftOrders.ExpireDue(utcNow);

        public void Load() => AutonomousBotCraftOrders.Load(GameServer.Database.SelectAllObjects<OfflineBotCraftOrderRecord>());

        public void Pulse(long now) { }

        public void Flush(bool wait)
        {
            ExpireIfDue();
            Orders.Flush(wait);
        }
    }

    /// <summary>Tests only: empty state, rows written nowhere, and the real coin and roster again.</summary>
    public static void ResetForTests()
    {
        Orders = new LivingServerTable<string, OfflineBotCraftOrderRecord>("craft order", row => row.OrderId, StringComparer.Ordinal,
            database: () => null);
        Load([]);
        _nextExpiryPass = 0;
        SpendCoin = DefaultSpend;
        PayCoin = DefaultPay;
        FindLive = DefaultFindLive;
        CraftersOf = DefaultCraftersOf;
        CoinOf = AutonomousBotEconomy.GetMoney;
        OnSale = AutonomousBotCrafting.OnSale;
        DeliverToPlayer = (_, _, _) => false;
        PlayerOrderClosed = (_, _, _) => { };
        PreferCrafter = (_, _) => false;
        GuildPrice = (_, _, _) => null;
        BotOrderDelivered = (_, _, _) => { };
    }
}
