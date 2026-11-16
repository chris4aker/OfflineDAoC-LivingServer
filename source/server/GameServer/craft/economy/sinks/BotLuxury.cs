using System;
using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting 2.0 WP7: the luxury sink (ours; design facet L, "the inflation sink"). At a natural task break a rich
    /// gamebot (coin above 20 times its cosmetics reserve, so 100 gold a level) spends a share of its surplus: at most 2%
    /// of what is above the threshold, times the governor's intensity for its realm (<see cref="SinkGovernor"/>, 0-1), at
    /// least 1 gold or nothing. It spends it first on real goods it uses (the dearest draughts of healing and of power or
    /// endurance a merchant near it sells, up to <see cref="ExtraDraughts"/> more than the upkeep keeps), then the rest on
    /// the town's services (board, drink and a bard: the coin leaves the world, no item). Every coin move is
    /// <see cref="EconomyFlowKind.Luxury"/>. Needs <c>bot_luxury_spending</c> and <c>bot_crafters</c>; autonomous gamebots
    /// only, never beggars. Hourly ECON_LUXURY per realm; one line per spend with <c>econ_trade_lines</c>.
    /// </summary>
    public static class BotLuxury
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotLuxury));

        /// <summary>Rich: coin above this multiple of the cosmetics reserve (AutonomousBotCosmetics.Reserve, 5 gold a level).</summary>
        public const int RichReserveMultiple = 20;
        /// <summary>The share of the surplus a visit may spend at full intensity.</summary>
        public const double MaxShareOfSurplus = 0.02;
        /// <summary>Less than this is not spent.</summary>
        public const long MinimumSpend = 10_000;
        /// <summary>Draughts of a kind a rich bot keeps beyond what the upkeep's potion step buys (heal: 2).</summary>
        public const int ExtraDraughts = 2;
        /// <summary>What the town's services are called in the lines.</summary>
        public const string Services = "board, drink and a bard";

        internal static readonly BotSinks.HourlySums Spent = new();
        internal static readonly BotSinks.HourlySums Goods = new();
        private static readonly ConcurrentDictionary<long, string> SpentAtBreak = new();

        /// <summary>The coin above which a bot of this level is rich.</summary>
        public static long Threshold(int level) => AutonomousBotCosmetics.Reserve(level) * RichReserveMultiple;

        /// <summary>Pure: what a visit may spend at this coin, level and intensity (0 when below the minimum).</summary>
        public static long Budget(long coin, int level, double intensity)
        {
            long surplus = coin - Threshold(level);
            if (surplus <= 0 || !(intensity > 0))
                return 0;
            long budget = (long)Math.Floor(surplus * MaxShareOfSurplus * Math.Clamp(intensity, 0, 1));
            return budget >= MinimumSpend ? budget : 0;
        }

        /// <summary>Pure: how many draughts of a kind to buy as luxury when carrying <paramref name="carried"/>.</summary>
        public static int DraughtsToBuy(int carried, int keptByUpkeep) => Math.Max(0, keptByUpkeep + ExtraDraughts - Math.Max(0, carried));

        /// <summary>The luxury spend as a merchant step (once a break; after the repairs, potions and poisons).</summary>
        public sealed class Step : IBotServiceStep
        {
            public static readonly Step Instance = new();
            public string Name => "luxury (WP7)";
            public int Order => 200;
            public eWorldServiceKind Kind => eWorldServiceKind.Vendor;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                if (!BotSinks.LuxuryOn || !BotSinks.IsGamebot(bot) || AutonomousBeggars.Is(bot) || service is not GameMerchant merchant ||
                    bot.Inventory == null)
                    return false;
                string breakId = BotSinks.BreakOf(bot);
                if (SpentAtBreak.TryGetValue(bot.DatabaseID, out string done) && done == breakId)
                    return false;
                SpentAtBreak[bot.DatabaseID] = breakId;
                double intensity = SinkGovernor.Instance.Intensity(bot.Realm);
                long budget = Budget(BotSinks.CoinOf(bot.DatabaseID), bot.Level, intensity);
                if (budget <= 0)
                    return false;
                long spent = Spend(bot, merchant, budget, out int draughts);
                if (spent <= 0)
                    return false;
                result = new BotServiceResult($"Spent {spent} copper in town", draughts > 0
                        ? $"Bought {draughts} fine draughts and paid for {Services}"
                        : $"Paid for {Services} from its surplus", 3_000, eAutonomousProgressKind.Money);
                return true;
            }
        }

        /// <summary>Spends up to the budget: the dearest draughts first, then the services. Returns the copper spent.</summary>
        public static long Spend(GameBot bot, GameMerchant merchant, long budget, out int draughts)
        {
            draughts = 0;
            long left = budget;
            bool usesPower = bot.MaxMana > 0 && bot.CharacterClass?.ManaStat is not null and not eStat.UNDEFINED;
            foreach ((BotPotions.Kind kind, int kept) in new[] { (BotPotions.Kind.Heal, 2), (usesPower ? BotPotions.Kind.Power : BotPotions.Kind.Endurance, 0) })
            {
                int want = DraughtsToBuy(BotPotions.Carried(bot, kind), kept);
                if (want == 0)
                    continue;
                DbItemTemplate fine = BotPotions.DraughtsOnSale(bot, merchant, kind).Where(t => t.Price <= left)
                    .OrderByDescending(t => t.Price).FirstOrDefault();
                if (fine == null)
                    continue;
                int count = (int)Math.Min(want, left / fine.Price);
                int bought = count <= 0 ? 0 : BotPotions.Buy(bot, fine, count, EconomyFlowKind.Luxury);
                draughts += bought;
                left -= bought * fine.Price;
            }
            long goods = budget - left;
            if (draughts > 0)
                Goods.Add(bot.Realm, goods, draughts);
            // The rest on the town's services (nothing when the purse moved under it: they wait for the next break).
            long services = left > 0 && BotSinks.Spend(bot.DatabaseID, left, EconomyFlowKind.Luxury) ? left : 0;
            long spent = goods + services;
            if (spent > 0)
                Spent.Add(bot.Realm, spent);
            if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconLuxury, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("level", bot.Level), ("budget", budget), ("draughts", draughts), ("copper", spent),
                    ("intensity", Math.Round(SinkGovernor.Instance.Intensity(bot.Realm), 3))));
            return spent;
        }

        /// <summary>Tests: forget the break marks and the sums.</summary>
        public static void ResetForTests()
        {
            SpentAtBreak.Clear();
            Spent.Take();
            Goods.Take();
        }
    }
}
