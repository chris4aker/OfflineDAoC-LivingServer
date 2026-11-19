using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>One realm's money supply at a moment (Crafting 2.0 WP2): coin in bot purses, player wallets, Exchange
    /// proceeds waiting for a player, guild banks; and, apart from the coin, what the Exchange has on offer.</summary>
    public sealed class RealmSupply
    {
        public long BotCoin { get; internal set; }
        /// <summary>AccountXMoney per account and realm (an online player's live purse instead of the saved row).</summary>
        public long PlayerWallets { get; internal set; }
        /// <summary>Exchange sale proceeds credited to a player and not yet claimed at a broker.</summary>
        public long PlayerProceeds { get; internal set; }
        public long GuildBanks { get; internal set; }
        /// <summary>Asking prices of the realm's Exchange listings (goods, not coin: not part of <see cref="Total"/>).</summary>
        public long ListedValue { get; internal set; }
        public int Listings { get; internal set; }
        public int BotListings { get; internal set; }
        public int Bots { get; internal set; }
        public int Online { get; internal set; }
        public int Crafters { get; internal set; }
        public long MedianBotCoin { get; internal set; }
        public readonly int[] BandBots = new int[6];
        public readonly long[] BandCoin = new long[6];
        public readonly long[] BandMedian = new long[6];

        public long PlayerCoin => PlayerWallets + PlayerProceeds;
        /// <summary>The money supply: every coin a realm's bots, players and guilds hold.</summary>
        public long Total => BotCoin + PlayerWallets + PlayerProceeds + GuildBanks;
    }

    /// <summary>A whole snapshot: the realms (slot 0 unused), and every bot's purse for the roster comparison.</summary>
    public sealed class SupplySnapshot
    {
        public DateTime TakenUtc { get; init; }
        public RealmSupply[] Realms { get; } = Enumerable.Range(0, EconomyFlowLedger.RealmSlots).Select(_ => new RealmSupply()).ToArray();
        /// <summary>BotId to (realm, level, coin) of every gamebot row.</summary>
        public IReadOnlyDictionary<long, (int Realm, int Level, long Coin)> Purses { get; init; } =
            new Dictionary<long, (int, int, long)>();
    }

    /// <summary>Builds a <see cref="SupplySnapshot"/> from rows (pure; the server glue and the tests feed it).</summary>
    public sealed class SupplyBuilder
    {
        private readonly Dictionary<long, (int Realm, int Level, long Coin)> _purses = new();
        private readonly HashSet<long> _online = new();
        private readonly long[] _wallets = new long[EconomyFlowLedger.RealmSlots];
        private readonly long[] _proceeds = new long[EconomyFlowLedger.RealmSlots];
        private readonly long[] _guilds = new long[EconomyFlowLedger.RealmSlots];
        private readonly long[] _listed = new long[EconomyFlowLedger.RealmSlots];
        private readonly int[] _listings = new int[EconomyFlowLedger.RealmSlots];
        private readonly int[] _botListings = new int[EconomyFlowLedger.RealmSlots];
        private readonly int[] _crafters = new int[EconomyFlowLedger.RealmSlots];

        private static int Slot(int realm) => realm is >= 1 and <= 3 ? realm : 0;

        /// <summary>A gamebot's purse; a later call for the same bot replaces the earlier (the live record wins).</summary>
        public SupplyBuilder Bot(long botId, int realm, int level, long coin, bool online = false)
        {
            _purses[botId] = (Slot(realm), level, Math.Max(0, coin));
            if (online)
                _online.Add(botId);
            else
                _online.Remove(botId);
            return this;
        }

        public SupplyBuilder Wallet(int realm, long copper) { _wallets[Slot(realm)] += Math.Max(0, copper); return this; }
        public SupplyBuilder Proceeds(int realm, long copper) { _proceeds[Slot(realm)] += Math.Max(0, copper); return this; }
        public SupplyBuilder Guild(int realm, long bank) { _guilds[Slot(realm)] += Math.Max(0, bank); return this; }
        public SupplyBuilder Crafters(int realm, int count) { _crafters[Slot(realm)] = count; return this; }

        public SupplyBuilder Listing(int realm, long price, bool byBot)
        {
            int slot = Slot(realm);
            _listed[slot] += Math.Max(0, price);
            _listings[slot]++;
            if (byBot)
                _botListings[slot]++;
            return this;
        }

        public SupplySnapshot Build(DateTime takenUtc)
        {
            var snapshot = new SupplySnapshot { TakenUtc = takenUtc, Purses = new Dictionary<long, (int, int, long)>(_purses) };
            var coins = new List<long>[EconomyFlowLedger.RealmSlots];
            var bandCoins = new List<long>[EconomyFlowLedger.RealmSlots, 6];
            for (int realm = 0; realm < EconomyFlowLedger.RealmSlots; realm++)
            {
                coins[realm] = new List<long>();
                for (int band = 0; band < 6; band++)
                    bandCoins[realm, band] = new List<long>();
            }
            foreach ((long botId, (int realm, int level, long coin)) in _purses)
            {
                RealmSupply supply = snapshot.Realms[realm];
                int band = EconomyFlowLedger.BandOf(level);
                supply.BotCoin += coin;
                supply.Bots++;
                supply.BandBots[band]++;
                supply.BandCoin[band] += coin;
                if (_online.Contains(botId))
                    supply.Online++;
                coins[realm].Add(coin);
                bandCoins[realm, band].Add(coin);
            }
            for (int realm = 0; realm < EconomyFlowLedger.RealmSlots; realm++)
            {
                RealmSupply supply = snapshot.Realms[realm];
                supply.PlayerWallets = _wallets[realm];
                supply.PlayerProceeds = _proceeds[realm];
                supply.GuildBanks = _guilds[realm];
                supply.ListedValue = _listed[realm];
                supply.Listings = _listings[realm];
                supply.BotListings = _botListings[realm];
                supply.Crafters = _crafters[realm];
                supply.MedianBotCoin = Median(coins[realm]);
                for (int band = 0; band < 6; band++)
                    supply.BandMedian[band] = Median(bandCoins[realm, band]);
            }
            return snapshot;
        }

        /// <summary>The median (the lower middle of an even count), 0 for none.</summary>
        public static long Median(List<long> values)
        {
            if (values == null || values.Count == 0)
                return 0;
            values.Sort();
            return values[(values.Count - 1) / 2];
        }
    }
}
