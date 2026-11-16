using System;
using System.Threading;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting 2.0 WP7 (sinks, milestone M5): who pays and which switch turns each sink on. Every switch defaults off
    /// (CRAFTING-2.0-BUILD-PLAN 2.4 and 5.7, owner): <c>bot_upkeep</c> (gear wear, repairs, potions, paid poisons),
    /// <c>bot_luxury_spending</c> (the luxury sink and the money-supply governor that drives it) and
    /// <c>bot_siege_supply</c> (siegecrafter gamebots supply siege kits below the merchant price). Each, as its setting
    /// says, needs <c>bot_crafters</c> too. Only autonomous gamebots ever pay: companions (/bot), Epic Special Force
    /// visitors and real players are never touched by this package (players keep WP3's player_upkeep rule).
    /// </summary>
    public static class BotSinks
    {
        /// <summary>bot_crafters and bot_upkeep.</summary>
        public static bool UpkeepOn => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_UPKEEP;

        /// <summary>bot_crafters and bot_luxury_spending (also runs the governor).</summary>
        public static bool LuxuryOn => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_LUXURY_SPENDING;

        /// <summary>bot_crafters and bot_siege_supply.</summary>
        public static bool SiegeSupplyOn => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_SIEGE_SUPPLY;

        /// <summary>An autonomous gamebot with a saved record: never a companion, an Epic Special Force visitor or a player.</summary>
        public static bool IsGamebot(GameBot bot) =>
            bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && !bot.IsEpicSpecialForce &&
            bot.DatabaseID > 0 && bot.PersistentRecord != null;

        /// <summary>This bot's gear wears and it pays its upkeep.</summary>
        public static bool Upkeep(GameBot bot) => UpkeepOn && IsGamebot(bot);

        /// <summary>The between-task assignment the bot is in (a step that runs once a break keys on it).</summary>
        public static string BreakOf(GameBot bot) => bot?.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;

        // ---- coin (every move names its kind; tests replace these) ----

        /// <summary>A gamebot's coin.</summary>
        public static Func<long, long> CoinOf = GameCoin;

        /// <summary>Takes coin from a gamebot (false when short), recorded in the ledger under the kind.</summary>
        public static Func<long, long, EconomyFlowKind, bool> Spend = GameSpend;

        /// <summary>Gives coin to a gamebot, recorded in the ledger under the kind.</summary>
        public static Func<long, long, EconomyFlowKind, bool> Pay = GamePay;

        private static long GameCoin(long botId) => AutonomousBotEconomy.GetMoney(botId);

        // Every WP7 caller names its kind; one left unnamed is a Consumable, never Unattributed.
        private static bool GameSpend(long botId, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.TrySpend(botId, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.Consumable : kind);

        private static bool GamePay(long botId, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.AddMoney(botId, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.Consumable : kind);

        /// <summary>Tests: the coin delegates back to the game's.</summary>
        public static void ResetCoinForTests()
        {
            CoinOf = GameCoin;
            Spend = GameSpend;
            Pay = GamePay;
        }

        /// <summary>Realm slot 0-3 for the hourly sums (0 = none).</summary>
        public static int Slot(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? (int)realm : 0;

        /// <summary>Hourly sums per realm: a count and a copper total per kind of event, summed lock-free.</summary>
        public sealed class HourlySums
        {
            private long[] _count = new long[4];
            private long[] _copper = new long[4];

            public void Add(eRealm realm, long copper, long count = 1)
            {
                int slot = Slot(realm);
                Interlocked.Add(ref Volatile.Read(ref _count)[slot], count);
                Interlocked.Add(ref Volatile.Read(ref _copper)[slot], copper);
            }

            /// <summary>Takes the hour's sums (count, copper per realm slot) and starts a new hour.</summary>
            public (long[] Count, long[] Copper) Take() =>
                (Interlocked.Exchange(ref _count, new long[4]), Interlocked.Exchange(ref _copper, new long[4]));

            public long Count(eRealm realm) => Volatile.Read(ref _count)[Slot(realm)];
            public long Copper(eRealm realm) => Volatile.Read(ref _copper)[Slot(realm)];
        }
    }
}
