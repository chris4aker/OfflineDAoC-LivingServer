using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Realm commissions (Crafting 2.0 WP5, written new; design §5 "Realm commissions", decision 8): each realm's
/// quartermaster keeps its guards in armor and weapons by ordering them from the realm's crafter gamebots. The owner's
/// "doubled commissions" is the setting realm_commissions: classic (30 orders a realm an hour, the simulator's rate),
/// doubled (60, the default) or off. An order is <b>sized to the crafter's skill</b>: the hardest recipe of its own order
/// it still makes at orange or better, so an Apprentice gets apprentice work and a Legendary smith legendary work; and it
/// is <b>paid for the time</b>: the materials back and a wage for the crafting time at the crafter's skill, at least the
/// simulator's three times the materials (<see cref="Pay"/>).
/// <para>The order goes on the same board as the bots' own orders (source Quartermaster, buyer "quartermaster:{realm}"),
/// so the crafter makes it on its next craft trip and hands it over at the end of that break, at a natural task break as
/// everything else. The piece goes to the realm's guards: it leaves the world (a goods sink), and the realm pays the
/// crafter, which is new coin: the pay is recorded as a faucet of kind Commission in the economy ledger
/// (<see cref="EconomyFlowKind.Commission"/>). An order not made in time lapses; nobody paid for it, so nothing is
/// refunded, and a piece already made stays with its maker as its own work.</para>
/// <para><see cref="GovernorMultiplier"/> scales the rate for WP7's money-supply governor (1 until WP7 sets it). Nothing
/// runs while bot_crafters is off; companions and players are never asked.</para>
/// </summary>
public static class QuartermasterCommissions
{
    private static readonly Logger Log = LoggerManager.Create(typeof(QuartermasterCommissions));

    public enum Mode { Off, Classic, Doubled }

    /// <summary>The simulator's NPC orders a realm an hour (persona_sim.py: 30, doubled to 60).</summary>
    public const int ClassicPerHour = 30;
    /// <summary>Open to crafters above this skill (the simulator's rule).</summary>
    public const int MinimumSkill = 50;
    /// <summary>The simulator paid three times the materials.</summary>
    public const int MaterialMultiple = 3;
    /// <summary>Orders a realm may owe at once after a quiet spell (no burst of hundreds after a restart).</summary>
    public const double MaxCredit = 5;
    public const long PostPassMilliseconds = 60_000;
    /// <summary>A crafter asked within this time waits behind one that was not (the work goes round).</summary>
    public static readonly TimeSpan CrafterRest = TimeSpan.FromMinutes(30);

    private static double _governor = 1.0;

    /// <summary>
    /// WP7's money-supply governor: the commission rate is multiplied by this (kept within 0-2; 1 until WP7 sets it).
    /// Read on every posting pass, so a change takes effect within a minute.
    /// </summary>
    public static double GovernorMultiplier
    {
        get => Volatile.Read(ref _governor);
        set => Volatile.Write(ref _governor, double.IsNaN(value) ? 1.0 : Math.Clamp(value, 0.0, 2.0));
    }

    private static readonly double[] Credit = new double[4];
    private static readonly ConcurrentDictionary<long, DateTime> LastAsked = new();
    private static long _nextPass;

    /// <summary>The hour's counts per realm, for the hourly ECON_COMMISSION line.</summary>
    private sealed class Tally
    {
        public int Posted, Delivered, Expired;
        public long Paid;
    }

    private static readonly Tally[] Hour = Enumerable.Range(0, 4).Select(_ => new Tally()).ToArray();
    private static DateTime _hourStartUtc = DateTime.UtcNow;

    // Tests replace these.
    public static Func<long, long, EconomyFlowKind, bool> PayCrafter = DefaultPay;
    public static Func<eRealm, IEnumerable<GameBot>> CraftersOf = realm => AutonomousBotCraftOrders.CraftersOf(realm);

    private static bool DefaultPay(long botId, long copper, EconomyFlowKind kind) => AutonomousBotEconomy.AddMoney(botId, copper, kind);

    #region Rules (pure)

    public static Mode ParseMode(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" or "none" or "0" => Mode.Off,
        "classic" => Mode.Classic,
        _ => Mode.Doubled, // the default, also for an unreadable value
    };

    /// <summary>Orders a realm an hour: classic 30, doubled 60, off 0; times the governor (0-2).</summary>
    public static double PerHour(Mode mode, double governor) =>
        (mode switch { Mode.Classic => ClassicPerHour, Mode.Doubled => 2 * ClassicPerHour, _ => 0 }) * Math.Clamp(governor, 0, 2);

    /// <summary>The copper per second of crafting time a crafter of this skill earns: 1 at the start, 11 at Legendary.</summary>
    public static long WagePerSecond(int skill) => 1 + Math.Clamp(skill, 0, 1100) / 100;

    /// <summary>
    /// What the realm pays for a piece: the materials and a wage for the crafting time at the crafter's skill, at least
    /// <see cref="MaterialMultiple"/> times the materials and the board's minimum fee above them.
    /// </summary>
    public static long Pay(long materialCopper, int craftSeconds, int skill)
    {
        long materials = Math.Max(0, materialCopper);
        long wage = Math.Max(1, craftSeconds) * WagePerSecond(skill);
        return Math.Max(materials * MaterialMultiple, materials + Math.Max(AutonomousBotCraftOrders.MinimumFee, wage));
    }

    /// <summary>
    /// The work sized to the crafter: of its primary craft, a worn piece (never a stack) its realm may make, with the
    /// materials on sale and within its coin, that it makes at orange or better: the hardest such recipe, then the
    /// cheapest, then the id. Null when nothing fits (a crafter below <see cref="MinimumSkill"/> gets none).
    /// </summary>
    public static CraftRecipeInfo ChooseWork(IEnumerable<CraftRecipeInfo> recipes, string skills, eRealm realm, long coin,
        Func<CraftRecipeInfo, bool> onSale, bool allowNoRealm = false)
    {
        eCraftingSkill primary = BotCraftingSkills.Primary(skills);
        if (primary == eCraftingSkill.NoCrafting || BotCraftingSkills.Value(skills, primary) < MinimumSkill)
            return null;
        return (recipes ?? [])
            .Where(recipe => recipe != null && recipe.Skill == primary && !recipe.Stackable &&
                             BotCraftingRules.RealmAllows(recipe.Realm, realm, allowNoRealm) && recipe.MaterialCostCopper <= coin &&
                             AutonomousBotCraftOrders.CanFill(skills, recipe) && (onSale == null || onSale(recipe)))
            .OrderByDescending(recipe => recipe.Level)
            .ThenBy(recipe => recipe.MaterialCostCopper)
            .ThenBy(recipe => recipe.RecipeId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The crafters to ask, in order: able and at a craft break soon, the longest unasked first, then the least busy.</summary>
    public static IEnumerable<GameBot> Candidates(IEnumerable<GameBot> crafters, eRealm realm, DateTime utcNow,
        Func<long, DateTime> lastAsked)
    {
        DateTime latest = utcNow + AutonomousBotCraftOrders.BreakWindow;
        return (crafters ?? [])
            .Where(crafter => AutonomousBotCraftOrders.IsAbleCrafter(crafter) && crafter.Realm == realm &&
                              BotCraftingSkills.Value(crafter.PersistentRecord.SerializedCraftingSkills,
                                  BotCraftingSkills.Primary(crafter.PersistentRecord.SerializedCraftingSkills)) >= MinimumSkill &&
                              AutonomousBotCraftOrders.OfCrafter(crafter.DatabaseID).Count < AutonomousBotCraftOrders.MaximumOpenPerCrafter &&
                              AutonomousBotCraftOrders.NextCraftBreak(crafter) <= latest)
            .OrderBy(crafter => lastAsked(crafter.DatabaseID))
            .ThenBy(crafter => AutonomousBotCraftOrders.OfCrafter(crafter.DatabaseID).Count)
            .ThenBy(crafter => crafter.DatabaseID);
    }

    /// <summary>The buyer id of a realm's quartermaster on the board.</summary>
    public static string BuyerId(eRealm realm) => $"quartermaster:{(int)realm}";

    public static string BuyerName(eRealm realm) => $"Quartermaster of {EconomyLog.RealmName(realm)}";

    #endregion

    #region Posting

    public static bool On => CraftingEconomyProperties.BOT_CRAFTERS && ParseMode(CraftingEconomyProperties.REALM_COMMISSIONS) != Mode.Off;

    /// <summary>Once a minute from the Living Server pulse: each realm earns its minute's share of orders and posts those
    /// it has crafters for. Posting moves no coin; the pay comes at delivery. Returns the orders posted.</summary>
    public static int PostIfDue(long now, DateTime utcNow)
    {
        if (now < Volatile.Read(ref _nextPass))
            return 0;
        Volatile.Write(ref _nextPass, now + PostPassMilliseconds);
        if (!On)
            return 0;
        double perMinute = PerHour(ParseMode(CraftingEconomyProperties.REALM_COMMISSIONS), GovernorMultiplier) / 60.0;
        int posted = 0;
        using var aiProfile = AiProfile.Scope(AiSystem.EconomyMarket); // AI_PROFILE: measures only
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            posted += PostForRealm(realm, perMinute, utcNow);
        return posted;
    }

    /// <summary>Adds the minute's share to the realm's credit and posts whole orders while crafters can take them.</summary>
    public static int PostForRealm(eRealm realm, double perMinute, DateTime utcNow)
    {
        int slot = (int)realm;
        Credit[slot] = Math.Min(MaxCredit, Credit[slot] + Math.Max(0, perMinute));
        if (Credit[slot] < 1)
            return 0;
        int posted = 0;
        foreach (GameBot crafter in Candidates(CraftersOf(realm).ToArray(), realm, utcNow, id => LastAsked.GetValueOrDefault(id, DateTime.MinValue)).ToArray())
        {
            if (Credit[slot] < 1)
                break;
            if (Post(crafter, utcNow) == null)
                continue;
            Credit[slot] -= 1;
            posted++;
        }
        return posted;
    }

    /// <summary>Posts one commission sized to this crafter, or null when nothing fits it.</summary>
    public static CraftOrder Post(GameBot crafter, DateTime utcNow)
    {
        string skills = crafter?.PersistentRecord?.SerializedCraftingSkills;
        eCraftingSkill primary = BotCraftingSkills.Primary(skills);
        if (crafter == null || primary == eCraftingSkill.NoCrafting)
            return null;
        long coin = AutonomousBotCraftOrders.CoinOf(crafter.DatabaseID);
        CraftRecipeInfo work = ChooseWork(AutonomousBotCrafting.RecipesOf(primary), skills, crafter.Realm, coin,
            recipe => AutonomousBotCraftOrders.OnSale(recipe, crafter.Realm), DOL.GS.ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS);
        if (work == null)
            return null;
        int skill = BotCraftingSkills.Value(skills, primary);
        int seconds = SecondsFor(crafter, work);
        long pay = Pay(work.MaterialCostCopper, seconds, skill);
        var order = new CraftOrder(Guid.NewGuid().ToString("N"), crafter.Realm, CraftOrderSource.Quartermaster, 0, BuyerId(crafter.Realm),
            BuyerName(crafter.Realm), crafter.DatabaseID, work.Skill, work.ProductId ?? string.Empty, work.ProductName ?? string.Empty,
            work.Level, 0, 1, pay, false, CraftOrderState.Open, string.Empty, utcNow, utcNow + AutonomousBotCraftOrders.Lifetime);
        if (!AutonomousBotCraftOrders.TryAdd(order))
            return null;
        LastAsked[crafter.DatabaseID] = utcNow;
        lock (Hour)
            Hour[(int)crafter.Realm].Posted++;
        if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconCommission, ("outcome", "posted"), ("order", order.OrderId), ("realm", crafter.Realm),
                ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("craft", work.Skill), ("skill", skill),
                ("recipeLevel", work.Level), ("item", work.ProductName), ("materials", work.MaterialCostCopper), ("seconds", seconds),
                ("pay", pay)));
        return order;
    }

    private static int SecondsFor(GameBot crafter, CraftRecipeInfo work)
    {
        try { return AutonomousBotCrafting.WorkSeconds(crafter, new BotCraftingRules.Session(work, work.Skill, 1)); }
        catch { return 1; } // tests and a world without keeps or relics
    }

    #endregion

    #region Delivery (from the board at the crafter's task break)

    /// <summary>The made piece goes to the realm's guards (it leaves the world) and the realm pays the crafter (a faucet).</summary>
    internal static bool Deliver(GameBot crafter, CraftOrder order, DbInventoryItem piece)
    {
        using (AutonomousBotStatusPersistence.TryEnterForAiTurn(crafter, "commission", out bool entered))
        {
            if (!entered || !crafter.Inventory.RemoveItem(piece))
                return false;
        }
        AutonomousBotCraftOrders.Remove(order);
        bool paid = PayCrafter(crafter.DatabaseID, order.EscrowCopper, EconomyFlowKind.Commission);
        BotCrafterRoster.Instance.NoteEarned(crafter.DatabaseID, order.EscrowCopper);
        AutonomousBotEconomy.MarkInventoryChanged(crafter);
        crafter.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(crafter, true);
        lock (Hour)
        {
            Tally tally = Hour[Math.Clamp((int)crafter.Realm, 0, 3)];
            tally.Delivered++;
            if (paid)
                tally.Paid += order.EscrowCopper;
        }
        if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.EconCommission, ("outcome", "delivered"), ("order", order.OrderId), ("realm", crafter.Realm),
                ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("item", piece.Name), ("quality", piece.Quality),
                ("pay", order.EscrowCopper), ("paid", paid)));
        return true;
    }

    internal static void NoteExpired(CraftOrder order)
    {
        lock (Hour)
            Hour[Math.Clamp((int)order.Realm, 0, 3)].Expired++;
    }

    /// <summary>The hourly ECON_COMMISSION line per realm (when anything happened). Returns the lines written.</summary>
    public static IReadOnlyList<string> HourLines(DateTime utcNow, bool reset = true)
    {
        var lines = new List<string>();
        lock (Hour)
        {
            if (utcNow - _hourStartUtc < TimeSpan.FromHours(1) && reset)
                return lines;
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                Tally tally = Hour[(int)realm];
                if (tally.Posted + tally.Delivered + tally.Expired == 0)
                    continue;
                lines.Add(EconomyLog.Line(EconomyLog.EconCommission, ("outcome", "hour"), ("realm", realm),
                    ("mode", ParseMode(CraftingEconomyProperties.REALM_COMMISSIONS).ToString().ToLowerInvariant()),
                    ("governor", GovernorMultiplier), ("posted", tally.Posted), ("delivered", tally.Delivered),
                    ("expired", tally.Expired), ("paid", tally.Paid)));
                if (reset)
                    Hour[(int)realm] = new Tally();
            }
            if (reset)
                _hourStartUtc = utcNow;
        }
        if (reset && Log.IsInfoEnabled)
            foreach (string line in lines)
                Log.Info(line);
        return lines;
    }

    /// <summary>What /lscraft commission says (registered as its WP10 /lscraft part).</summary>
    public static string[] ReportLines()
    {
        Mode mode = ParseMode(CraftingEconomyProperties.REALM_COMMISSIONS);
        var lines = new List<string>
        {
            $"Realm commissions: {mode.ToString().ToLowerInvariant()} ({PerHour(mode, GovernorMultiplier):0.#} orders a realm an hour, " +
            $"governor x{GovernorMultiplier:0.##}); bot_crafters {(CraftingEconomyProperties.BOT_CRAFTERS ? "on" : "off")}, " +
            $"bot_craft_orders {(CraftingEconomyProperties.BOT_CRAFT_ORDERS ? "on" : "off")}; Exchange tax {ExchangeTax.Percent}%.",
        };
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            CraftOrder[] orders = AutonomousBotCraftOrders.All().Where(order => order.Realm == realm).ToArray();
            int quartermaster = orders.Count(order => order.Source == CraftOrderSource.Quartermaster);
            lines.Add($"  {EconomyLog.RealmName(realm)}: {orders.Length} open orders ({quartermaster} the quartermaster's, " +
                      $"{orders.Count(order => order.State == CraftOrderState.Made)} made and waiting), escrow " +
                      $"{orders.Where(order => order.PayUpFront).Sum(order => order.EscrowCopper)}c.");
        }
        foreach (string line in HourLines(DateTime.UtcNow, reset: false))
            lines.Add("  this hour: " + line);
        return lines.ToArray();
    }

    #endregion

    /// <summary>Tests only.</summary>
    public static void ResetForTests()
    {
        Array.Clear(Credit);
        LastAsked.Clear();
        _nextPass = 0;
        GovernorMultiplier = 1.0;
        lock (Hour)
            for (int i = 0; i < Hour.Length; i++)
                Hour[i] = new Tally();
        _hourStartUtc = DateTime.UtcNow;
        PayCrafter = DefaultPay;
        CraftersOf = realm => AutonomousBotCraftOrders.CraftersOf(realm);
    }
}
