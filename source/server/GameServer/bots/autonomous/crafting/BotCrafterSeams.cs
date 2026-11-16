using System;
using DOL.Database;
using DOL.Events;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;

namespace DOL.GS;

/// <summary>
/// What Crafting 2.0 WP4 registers in WP0's seams, so no shared file is edited for the crafting itself:
/// <list type="bullet">
/// <item><see cref="ItemPolicy"/> (IBotItemPolicy): DividedRealms' item rules of ceac061a - only a bot's own crafts'
/// materials are material; its own work waits in the bag for an Exchange post, is worth a post whatever it fetches, and a
/// craft trip posts it first - with True165's "only the best of a session" (the seconds go to a vendor).</item>
/// <item><see cref="TripActivity"/> (IBotBreakActivity 'C'): DividedRealms' craft trip at a natural task break.</item>
/// <item><see cref="StockNeed"/> (IBotTownVisitNeed): a crafter carrying unsold work asks for the inventory stop.</item>
/// <item><see cref="BotCrafterRoster"/> as the roster and the store of offline_bot_crafters and offline_bot_types.</item>
/// </list>
/// Every member answers neutrally while bot_crafters is off (and draws no random number), so with the switch off the
/// server behaves as before. Called from parallel brain turns: no locks beyond the roster's short in-memory ones, no
/// database work.
/// </summary>
public static class BotCrafterSeams
{
    public const char TripFlag = 'C';

    private static int _registered;

    [ScriptLoadedEvent]
    public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

    /// <summary>Registers every WP4 implementation once (script load, before the Living Server loads its stores).</summary>
    public static void Register()
    {
        if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
            return;
        BotCrafters.Roster = BotCrafterRoster.Instance;
        EconomyLifecycle.Register(BotCrafterRoster.Instance);
        EconomySeams.Register(ItemPolicy.Instance);
        BotServiceSeams.Register(TripActivity.Instance);
        BotServiceSeams.Register(StockNeed.Instance);
    }

    /// <summary>Tests only: register again after the seams were reset.</summary>
    public static void RegisterForTests()
    {
        System.Threading.Interlocked.Exchange(ref _registered, 0);
        Register();
    }

    public sealed class ItemPolicy : IBotItemPolicy
    {
        public static readonly ItemPolicy Instance = new();
        public string Name => "bot crafters (WP4)";

        /// <summary>DR: only the bot's own crafts' materials (null for a bot with no craft: today's rule).</summary>
        public bool? IsMaterialFor(GameBot bot, DbInventoryItem item) => AutonomousBotCrafting.IsMaterialFor(bot, item);

        /// <summary>DR: the bot's own work waits for a free Exchange post; only a nearly full bag sends it to a vendor.
        /// True165: a session's seconds are not kept.</summary>
        public bool KeepFromVendor(GameBot bot, DbInventoryItem item) =>
            AutonomousBotCrafting.Enabled && AutonomousBotCrafting.IsListableWork(bot, item) && !AutonomousBotEconomy.IsBackpackNearlyFull(bot);

        /// <summary>DR: a crafter's own piece is worth a post whatever it fetches.</summary>
        public bool AlwaysWorthListing(GameBot bot, DbInventoryItem item) =>
            AutonomousBotCrafting.Enabled && AutonomousBotCrafting.IsListableWork(bot, item);

        /// <summary>True165: only the best piece of a session is posted; the rest are sold to a vendor.</summary>
        public bool BlocksListing(GameBot bot, DbInventoryItem item) =>
            AutonomousBotCrafting.Enabled && AutonomousBotCrafting.IsSecond(bot, item);

        /// <summary>DR: a craft trip ends by posting what it made, not the dearest loot in the bag.</summary>
        public int ListingPriority(GameBot bot, DbInventoryItem item) =>
            AutonomousBotCrafting.Enabled && AutonomousObjectiveAssignments.HasBetweenTaskFlag(bot?.PersistentRecord, TripFlag) &&
            AutonomousBotCrafting.IsListableWork(bot, item) ? 1 : 0;
    }

    public sealed class TripActivity : IBotBreakActivity
    {
        public static readonly TripActivity Instance = new();
        public string Name => "craft trip (WP4)";
        public char Flag => TripFlag;
        /// <summary>DR: a craft trip ends at the usual inventory stop, where what it made is posted.</summary>
        public bool ImpliesInventoryStop => true;
        public string Describe => "make a trip to craft; ";

        /// <summary>A Crafter gamebot at a natural task break (the caller has checked it is ungrouped). The switch and the
        /// type are checked before the roll, so a bot that never crafts draws no random number.</summary>
        public bool Wants(GameBot bot, Random random)
        {
            if (!AutonomousBotCrafting.IsCrafter(bot) || bot.Level < BotCraftingRules.MinimumBotLevel || !RecipeCatalogs.Current.IsLoaded)
                return false;
            bool trained = AutonomousBotCrafting.IsTrained(bot);
            return BotCraftingRules.WantsTrip(trained, bot.Level, trained ? AutonomousBotCrafting.MaterialStacks(bot) : 0,
                (random ?? Random.Shared).NextDouble());
        }

        public bool IsDone(GameBot bot) => !AutonomousBotCrafting.Enabled || AutonomousBotCrafting.TripDone(bot);
    }

    public sealed class StockNeed : IBotTownVisitNeed
    {
        public static readonly StockNeed Instance = new();
        public string Name => "crafter stock (WP4)";

        /// <summary>A crafter carrying a listable piece of its own work that it can post now asks for the inventory stop.</summary>
        public bool WantsVisit(GameBot bot)
        {
            if (!AutonomousBotCrafting.IsCrafter(bot) || bot.Inventory == null)
                return false;
            foreach (DbInventoryItem item in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                if (AutonomousBotCrafting.IsListableWork(bot, item) && !EconomySeams.IsReserved(bot, item) &&
                    AutonomousBotEconomy.CanListNow(bot, item))
                    return true;
            return false;
        }

        /// <summary>The stock is found by the service choice's own listing search (AlwaysWorthListing), so nothing more here.</summary>
        public eWorldServiceKind? NeededService(GameBot bot) => null;
    }
}
