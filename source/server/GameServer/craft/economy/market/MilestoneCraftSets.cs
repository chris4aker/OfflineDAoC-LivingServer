using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Milestone crafted sets, the board's second source of the bots' own orders (Crafting 2.0 WP5).
/// <para>Ported from shadowofze's True165 (CraftOrders165, f6dbe48), GPL-3.0: a veteran bought a crafted 94-99% set from a
/// realm crafter around levels 20, 30 and 40 (FreddysHouse: "Get saving for your next SC"). A gamebot that has reached a
/// milestone works out once (cached) the crafted set it would wear there - its heaviest usable armour for each body slot
/// and its main weapon - leaves out slots whose worn piece is already as good as a 97% crafted copy, and posts one order
/// per slot once its coin covers the whole set above its reserve.</para>
/// <para>Changes in the port: outside True165's 1.65 Mode, behind bot_crafters and bot_craft_orders (both off by default);
/// the orders go on DividedRealms' board with the coin held in escrow, each to a crafter picked as DR picks one (an able
/// crafter at a craft break soon), and are made on its craft trip and handed over at its task break, instead of
/// True165's queue filled by a crafter on its town errand; the price is the board's (DR's materials + 25%, plan §5
/// decision 2) rather than True165's materials x 1.75; the upgrade test is our TryGetEquipmentUpgrade; only at a natural
/// task break. A slot with no able crafter is left out of the set; the buyer may try again after <see cref="Retry"/>.</para>
/// </summary>
public static class MilestoneCraftSets
{
    private static readonly Logger Log = LoggerManager.Create(typeof(MilestoneCraftSets));

    public static readonly int[] Milestones = [20, 30, 40];
    /// <summary>True165: an order nobody fills in two hours lapses and the buyer may post again (ours: the board's lifetime).</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromHours(2);
    /// <summary>The quality a crafted piece is judged at when deciding whether the slot wants it (True165).</summary>
    public const int JudgedQuality = 97;

    public static readonly eInventorySlot[] BodySlots =
    [
        eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor,
        eInventorySlot.HandsArmor, eInventorySlot.FeetArmor,
    ];

    /// <summary>One slot of a set: the recipe that makes it and what the buyer pays.</summary>
    public sealed record Piece(CraftRecipeInfo Recipe, eInventorySlot Slot, long Price);

    private sealed class BuyerState
    {
        public int Milestone;
        public Piece[] Plan;
        public DateTime LastTryUtc = DateTime.MinValue;
    }

    private static readonly ConcurrentDictionary<long, BuyerState> Buyers = new();

    /// <summary>The highest milestone at or below the level, 0 below the first (True165).</summary>
    public static int MilestoneFor(int level)
    {
        int found = 0;
        foreach (int milestone in Milestones)
            if (level >= milestone)
                found = milestone;
        return found;
    }

    /// <summary>Whether a buyer with this coin and reserve can pay for the whole set (True165).</summary>
    public static bool CanAfford(long coin, long reserve, IEnumerable<Piece> plan) =>
        plan != null && coin - reserve >= plan.Sum(piece => piece.Price);

    /// <summary>A recipe's product fits a milestone set: from two levels below the milestone to one above, never above the buyer (True165).</summary>
    public static bool FitsMilestone(int productLevel, int milestone, int buyerLevel) =>
        productLevel >= milestone - 2 && productLevel <= Math.Min(milestone + 1, buyerLevel);

    /// <summary>
    /// Picks per slot (True165): the heaviest armour (absorb), then the level nearest the milestone, then the higher armour
    /// factor or damage. <paramref name="candidates"/> are the recipes the buyer could wear, with their slot. Pure.
    /// </summary>
    public static Piece[] PickPlan(IEnumerable<(CraftRecipeInfo Recipe, DbItemTemplate Product, eInventorySlot Slot)> candidates, int milestone) =>
        (candidates ?? [])
            .GroupBy(candidate => candidate.Slot)
            .Select(group => group
                .OrderByDescending(candidate => GlobalConstants.IsArmor(candidate.Product.Object_Type) ? candidate.Product.SPD_ABS : 0)
                .ThenBy(candidate => Math.Abs(candidate.Recipe.ProductLevel - milestone))
                .ThenByDescending(candidate => candidate.Product.DPS_AF)
                .ThenBy(candidate => candidate.Recipe.RecipeId, StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => (int)candidate.Slot)
            .Select(candidate => new Piece(candidate.Recipe, candidate.Slot, AutonomousBotCraftOrders.Quote(candidate.Recipe)))
            .ToArray();

    private static DbInventoryItem Probe(CraftRecipeInfo recipe)
    {
        DbItemTemplate template = recipe?.Recipe?.Product;
        if (template == null || template.IsStackable)
            return null;
        DbInventoryItem probe = GameInventoryItem.Create(template);
        if (probe != null && recipe.Unique)
            probe.Quality = JudgedQuality;
        return probe;
    }

    /// <summary>The set this bot would wear at the milestone: a body slot's armour, or the main-hand weapon of its build,
    /// where it is an upgrade now.</summary>
    public static Piece[] BuildPlan(GameBot bot, int milestone)
    {
        var candidates = new List<(CraftRecipeInfo, DbItemTemplate, eInventorySlot)>();
        try
        {
            foreach (CraftRecipeInfo recipe in AutonomousBotCraftOrders.GearRecipes())
            {
                if (recipe == null || recipe.Stackable || !FitsMilestone(recipe.ProductLevel, milestone, bot.Level) ||
                    !(recipe.Realm == 0 || recipe.Realm == (int)bot.Realm) || Probe(recipe) is not DbInventoryItem probe ||
                    !AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, probe, out eInventorySlot slot))
                    continue; // not wearable, or the worn piece is already as good (True165 leaves such a slot out)
                bool body = Array.IndexOf(BodySlots, slot) >= 0;
                bool weapon = slot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon &&
                              BotWeaponStats.IsMeleeWeapon((eObjectType)probe.Object_Type);
                if (body || weapon)
                    candidates.Add((recipe, recipe.Recipe.Product, slot));
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Milestone sets: plan failed for {bot?.Name}", exception);
        }
        return PickPlan(candidates, milestone);
    }

    /// <summary>Whether the bot still wants the piece: an upgrade over what it wears in the slot (True165's WantsPiece).</summary>
    public static bool WantsPiece(GameBot bot, Piece piece) =>
        Probe(piece?.Recipe) is DbInventoryItem probe && AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, probe, out _);

    /// <summary>
    /// At a task break: a gamebot at a milestone, with no order of its own open, posts its set's orders once it can pay
    /// for all of them (each to a crafter who can make it). Returns the orders posted.
    /// </summary>
    public static int TryPost(GameBot bot, DateTime? utcNow = null)
    {
        if (!AutonomousBotCraftOrders.Enabled || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
            bot.Inventory == null || bot.DatabaseID <= 0)
            return 0;
        int milestone = MilestoneFor(bot.Level);
        if (milestone == 0 || AutonomousBotCraftOrders.OfBuyer(AutonomousBotEconomy.GetOwnerId(bot.DatabaseID)).Count > 0)
            return 0;
        DateTime now = utcNow ?? DateTime.UtcNow;
        BuyerState state = Buyers.GetOrAdd(bot.DatabaseID, _ => new BuyerState());
        Piece[] plan;
        lock (state) // one bot's turns never overlap; the lock only keeps the plan whole
        {
            if (state.Milestone == milestone && now - state.LastTryUtc < Retry)
                return 0;
            if (state.Milestone != milestone || state.Plan == null)
            {
                state.Milestone = milestone;
                state.Plan = BuildPlan(bot, milestone);
            }
            state.LastTryUtc = now;
            plan = state.Plan;
        }
        GameBot[] crafters = AutonomousBotCraftOrders.CraftersOf(bot.Realm).ToArray();
        var wanted = new List<(Piece Piece, GameBot Crafter)>();
        var busy = new Dictionary<long, int>();
        foreach (Piece piece in plan)
        {
            if (!WantsPiece(bot, piece) || !AutonomousBotCraftOrders.OnSale(piece.Recipe, bot.Realm))
                continue;
            GameBot crafter = AutonomousBotCraftOrders.PickCrafter(crafters.Where(c =>
                busy.GetValueOrDefault(c.DatabaseID) + AutonomousBotCraftOrders.OfCrafter(c.DatabaseID).Count <
                AutonomousBotCraftOrders.MaximumOpenPerCrafter), piece.Recipe, bot);
            if (crafter == null)
                continue;
            busy[crafter.DatabaseID] = busy.GetValueOrDefault(crafter.DatabaseID) + 1;
            wanted.Add((piece, crafter));
        }
        if (wanted.Count == 0)
            return 0;
        long coin = AutonomousBotCraftOrders.CoinOf(bot.DatabaseID);
        if (!CanAfford(coin, AutonomousBotCosmetics.Reserve(bot.Level), wanted.Select(w => w.Piece)))
            return 0;
        int posted = 0;
        foreach ((Piece piece, GameBot crafter) in wanted)
            if (AutonomousBotCraftOrders.Place(bot, crafter, piece.Recipe, piece.Price, "milestone") != null)
            {
                posted++;
                MarketPriceIndex.Instance.ObserveDemand(MarketPriceIndex.BandOf(bot.Realm, piece.Recipe));
            }
        if (posted > 0 && Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconOrder, ("outcome", "set"), ("source", "milestone"), ("realm", bot.Realm),
                ("buyer", bot.Name), ("buyerId", bot.DatabaseID), ("level", (int)bot.Level), ("milestone", milestone),
                ("pieces", posted), ("total", wanted.Take(posted).Sum(w => w.Piece.Price)), ("coin", coin)));
        return posted;
    }

    /// <summary>The bot left the world: its plan goes (its orders lapse on their own).</summary>
    public static void Forget(long botId) => Buyers.TryRemove(botId, out _);

    /// <summary>Tests: forget every plan.</summary>
    public static void ResetForTests() => Buyers.Clear();
}
