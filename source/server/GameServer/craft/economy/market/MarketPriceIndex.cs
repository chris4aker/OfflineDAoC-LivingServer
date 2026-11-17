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
/// The pure arithmetic of the price index (Crafting 2.0 WP5, written new; design §5 "Three ledgers", §4 L). A band's price
/// comes from today's valuation (AutonomousBotEconomy.RecommendListingPrice, the fair price before a seller's mood),
/// moved by the band's stock against its demand and, once it has sales, toward what it sold for; bounded below by what a
/// merchant pays and above by a multiple of the material cost. The quotes keep the anti-exploit rule of design §5: a
/// bot's bid is below every ask by at least the vendor spread and never below what a merchant pays, so no buy-and-sell
/// loop through the index makes coin (UT_PriceIndex).
/// </summary>
public static class PriceIndexMath
{
    /// <summary>The seller's mood range of today's listings (AutonomousBotEconomy.RollListingPrice: -0.35..+0.50).</summary>
    public const double MinDisposition = -0.35;
    public const double MaxDisposition = 0.50;
    /// <summary>The stock and demand a band is assumed to have before any is seen, so one listing does not swing it.</summary>
    public const int Smoothing = 5;
    public const double MinFactor = 0.8;
    public const double MaxFactor = 1.25;
    /// <summary>A crafted piece never asks more than this many times its materials (design §5: "a multiple of material cost").</summary>
    public const int CeilingMaterialMultiple = 3;
    /// <summary>Sales a band needs before its sale price moves the index.</summary>
    public const int MinSalesForHistory = 3;

    /// <summary>What the index knows of a band: listings on offer, demand seen, sales and their running price.</summary>
    public readonly record struct BandStats(int Stock, int Demand, int Sales, long SaleEmaCopper);

    public readonly record struct Quote(long Floor, long Fair, long Ask, long Bid, long Spread);

    /// <summary>Stock against demand: 1 when they balance, down to 0.8 for a flooded band, up to 1.25 for a starved one.</summary>
    public static double Pressure(int stock, int demand) =>
        Math.Clamp(Math.Pow((Math.Max(0, demand) + (double)Smoothing) / (Math.Max(0, stock) + (double)Smoothing), 0.25), MinFactor, MaxFactor);

    /// <summary>The vendor spread: what a merchant asks above what it pays (at least 1 copper).</summary>
    public static long Spread(long floor, long merchantPrice) =>
        Math.Max(1, merchantPrice > floor ? merchantPrice - floor : floor);

    /// <summary>The highest price the index allows: a multiple of the materials when they are known, else none.</summary>
    public static long Ceiling(long floor, long materialCopper) =>
        materialCopper > 0 ? Math.Max(floor, materialCopper * CeilingMaterialMultiple) : long.MaxValue;

    /// <summary>The fair price of an item in its band.</summary>
    public static long Fair(long baseCopper, long floor, long materialCopper, BandStats stats)
    {
        floor = Math.Max(0, floor);
        double basis = Math.Max(1, baseCopper);
        if (stats.Sales >= MinSalesForHistory && stats.SaleEmaCopper > 0)
            basis = (basis + Math.Clamp(stats.SaleEmaCopper, basis / 2, basis * 2)) / 2;
        long fair = (long)Math.Round(basis * Pressure(stats.Stock, stats.Demand), MidpointRounding.AwayFromZero);
        return Math.Clamp(fair, floor, Math.Max(floor, Ceiling(floor, materialCopper)));
    }

    /// <summary>The bid before a buyer's mood: the cheapest ask less the spread, never below the floor.</summary>
    public static long BidBase(long fair, long floor, long spread) =>
        Math.Max(floor, (long)Math.Floor(fair * (1 + MinDisposition)) - spread);

    /// <summary>The seller's ask at its mood: never below the bid base plus the spread.</summary>
    public static long Ask(long fair, long floor, long spread, double disposition) =>
        Math.Max((long)Math.Round(fair * (1 + Math.Clamp(disposition, MinDisposition, MaxDisposition)), MidpointRounding.AwayFromZero),
            BidBase(fair, floor, spread) + spread);

    /// <summary>The most a bot pays at its mood: a stingy one less, never more than the bid base, never below the floor.</summary>
    public static long Bid(long fair, long floor, long spread, double disposition)
    {
        long bidBase = BidBase(fair, floor, spread);
        double mood = Math.Min(0, Math.Clamp(disposition, MinDisposition, MaxDisposition));
        return Math.Clamp((long)Math.Floor(bidBase * (1 + mood)), floor, bidBase);
    }

    public static Quote QuoteOf(long baseCopper, long floor, long merchantPrice, long materialCopper, BandStats stats,
        double sellerDisposition = 0, double buyerDisposition = 0)
    {
        long spread = Spread(floor, merchantPrice);
        long fair = Fair(baseCopper, floor, materialCopper, stats);
        return new Quote(floor, fair, Ask(fair, floor, spread, sellerDisposition), Bid(fair, floor, spread, buyerDisposition), spread);
    }

    /// <summary>The running sale price: a quarter of the way toward each sale.</summary>
    public static long NextEma(long ema, long price) => ema <= 0 ? Math.Max(0, price) : ema + (Math.Max(0, price) - ema) / 4;
}

/// <summary>
/// The price index per band (Crafting 2.0 WP5, written new), registered as WP0's <see cref="PriceIndexes.Current"/>. It
/// adds stock and demand per band (realm, object type, slot, five-level band, quality tier; <see cref="MarketBand"/>) to
/// today's valuation; WP6 ports True165's ItemValue165 as the base of the floor and fair price on top of it. It hears of
/// listings (WP0's IBotItemPolicy.OnListed), sales (EconomySeams.SaleCommitted), demand (an order placed for a band) and
/// recounts the whole Exchange hourly. Nothing in WP5 prices by it yet: the index is read by /lscraft index and by WP6;
/// its answers change no amount until a package uses them. Its rows (offline_exchange_index) and ECON_INDEX lines are
/// written, and its hourly recount run, only while bot_crafters is on. Reads are lock-free (parallel brain turns).
/// </summary>
public sealed class MarketPriceIndex : IPriceIndex, IEconomyStore
{
    private static readonly Logger Log = LoggerManager.Create(typeof(MarketPriceIndex));

    public static readonly MarketPriceIndex Instance = new();

    public string Name => "price index";

    private sealed class BandState
    {
        public int Stock;
        public int Demand;
        public int Sales;
        public long SaleEma;
        public long LastFloor;
        public long LastCeiling;
        public PriceIndexMath.BandStats Stats => new(Volatile.Read(ref Stock), Volatile.Read(ref Demand), Volatile.Read(ref Sales),
            Interlocked.Read(ref SaleEma));
    }

    private readonly ConcurrentDictionary<MarketBand, BandState> _bands = new();
    /// <summary>The band of an item name per realm, from listings, so a sale (which carries only the name) finds its band.</summary>
    private readonly ConcurrentDictionary<(int Realm, string Name), MarketBand> _names = new();
    /// <summary>Crafted pieces gamebots posted since the last hourly line, by realm (PC log 2026-11-17: the night showed only
    /// crafted sales, exchange_crafted, so whether crafters posted at all could not be read).</summary>
    private readonly int[] _craftedPosted = new int[4];
    private const int MaxNames = 50_000;
    private long _nextHourTick;
    private const long HourMilliseconds = 60 * 60_000;

    internal LivingServerTable<string, OfflineExchangeIndexRecord> Rows = new("exchange index", row => row.BandKey, StringComparer.Ordinal);

    // Today's numbers, replaceable in tests.
    public Func<DbInventoryItem, long> FloorOf = AutonomousBotEconomy.CalculateStandardVendorSaleCopper;
    public Func<DbInventoryItem, long> BaseOf = item => AutonomousBotEconomy.RecommendListingPrice(item);
    public Func<DbInventoryItem, long> MerchantPriceOf = DefaultMerchantPrice;
    public Func<IEnumerable<DbInventoryItem>> Market = () => MarketCache.SearchItems(new ItemQuery());

    private static long DefaultMerchantPrice(DbInventoryItem item) =>
        item == null ? 0 : item.Price * (long)Math.Max(1, item.Count) / Math.Max(1, item.PackSize);

    public static bool Writes => CraftingEconomyProperties.BOT_CRAFTERS;

    #region Bands

    /// <summary>The realm an item belongs to here: its Exchange lot when listed, else its own realm (None when any).</summary>
    public static eRealm RealmOf(DbInventoryItem item)
    {
        if (item == null)
            return eRealm.None;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            if (item.OwnerLot != 0 && item.OwnerLot == RealmExchangeBroker.GetOwnerLot(realm))
                return realm;
        return item.Realm is >= 1 and <= 3 ? (eRealm)item.Realm : eRealm.None;
    }

    public static MarketBand BandOf(DbInventoryItem item) => MarketBand.Of(RealmOf(item), item);

    /// <summary>The band a recipe's product falls in (a crafted piece before its roll: tier 0).</summary>
    public static MarketBand BandOf(eRealm realm, CraftRecipeInfo recipe) => recipe == null
        ? default
        : new MarketBand(realm, recipe.ObjectType, recipe.ItemType, MarketBand.BandOf(recipe.ProductLevel), 0);

    private static MarketBand AllRealms(MarketBand band) => band with { Realm = eRealm.None };

    private BandState State(MarketBand band) => _bands.GetOrAdd(band, _ => new BandState());

    /// <summary>A band's stats; a band of no known realm reads the realms together.</summary>
    public PriceIndexMath.BandStats StatsOf(MarketBand band) =>
        _bands.TryGetValue(band, out BandState state) ? state.Stats : default;

    #endregion

    #region IPriceIndex

    public long Floor(DbInventoryItem item) => item == null ? 0 : Math.Max(0, FloorOf(item));

    public PriceIndexMath.Quote QuoteOf(DbInventoryItem item, long materialCostCopper = 0, double sellerDisposition = 0, double buyerDisposition = 0)
    {
        if (item == null)
            return default;
        long floor = Floor(item);
        MarketBand band = BandOf(item);
        if (_bands.TryGetValue(band, out BandState state))
        {
            Interlocked.Exchange(ref state.LastFloor, floor);
            Interlocked.Exchange(ref state.LastCeiling, PriceIndexMath.Ceiling(floor, materialCostCopper) == long.MaxValue ? 0 : PriceIndexMath.Ceiling(floor, materialCostCopper));
        }
        return PriceIndexMath.QuoteOf(BaseOf(item), floor, MerchantPriceOf(item), materialCostCopper, StatsOf(band),
            sellerDisposition, buyerDisposition);
    }

    public long Fair(DbInventoryItem item, long materialCostCopper = 0) => QuoteOf(item, materialCostCopper).Fair;

    public long Ask(DbInventoryItem item, double disposition, long materialCostCopper = 0) =>
        QuoteOf(item, materialCostCopper, disposition).Ask;

    public long Bid(DbInventoryItem item, double disposition) => QuoteOf(item, 0, 0, disposition).Bid;

    public void ObserveListing(MarketBand band, long priceCopper)
    {
        foreach (MarketBand each in new[] { band, AllRealms(band) }.Distinct())
            Interlocked.Increment(ref State(each).Stock);
    }

    public void ObserveSale(MarketBand band, long priceCopper)
    {
        foreach (MarketBand each in new[] { band, AllRealms(band) }.Distinct())
        {
            BandState state = State(each);
            Interlocked.Increment(ref state.Sales);
            // A sale takes one off the shelf.
            int stock;
            do stock = Volatile.Read(ref state.Stock);
            while (stock > 0 && Interlocked.CompareExchange(ref state.Stock, stock - 1, stock) != stock);
            long ema;
            do ema = Interlocked.Read(ref state.SaleEma);
            while (Interlocked.CompareExchange(ref state.SaleEma, PriceIndexMath.NextEma(ema, priceCopper), ema) != ema);
        }
    }

    public void ObserveDemand(MarketBand band)
    {
        foreach (MarketBand each in new[] { band, AllRealms(band) }.Distinct())
            Interlocked.Increment(ref State(each).Demand);
    }

    /// <summary>A listing that was posted (WP0's OnListed): its band and, for later sales, its name.</summary>
    public void NoteListed(DbInventoryItem item)
    {
        if (item == null)
            return;
        MarketBand band = BandOf(item);
        RememberName(band, item.Name);
        ObserveListing(band, item.SellPrice);
        if (item.IsCrafted && AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _))
            Interlocked.Increment(ref _craftedPosted[Math.Clamp((int)band.Realm, 0, 3)]);
    }

    /// <summary>A committed Exchange sale: its band by the item's name, when a listing of it was seen.</summary>
    public void NoteSale(RealmExchangeSaleLedger.Sale sale)
    {
        if (sale == null || string.IsNullOrEmpty(sale.ItemName) || sale.Quantity <= 0)
            return;
        if (_names.TryGetValue((sale.Realm, sale.ItemName), out MarketBand band))
            ObserveSale(band, sale.PriceCopper / Math.Max(1, sale.Quantity));
    }

    private void RememberName(MarketBand band, string name)
    {
        if (string.IsNullOrEmpty(name) || band.Realm == eRealm.None)
            return;
        if (_names.Count >= MaxNames)
            _names.Clear();
        _names[((int)band.Realm, name)] = band;
    }

    #endregion

    #region Hourly recount and store

    /// <summary>
    /// The hourly pass (flush timer, off the game loop): every band's stock from the whole Exchange (players' listings
    /// too: they are stock a buyer sees), demand halved (it fades in a few hours), then the rows and ECON_INDEX lines.
    /// Returns the lines (also logged while bot_crafters is on).
    /// </summary>
    public IReadOnlyList<string> Recount(DateTime utcNow)
    {
        var stock = new Dictionary<MarketBand, int>();
        var craftedListed = new int[4];
        foreach (DbInventoryItem item in Market() ?? [])
        {
            if (item == null || item.SellPrice <= 0 || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
                RealmExchangeExpiry.IsExpired(item, utcNow))
                continue;
            MarketBand band = BandOf(item);
            if (item.IsCrafted && AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _))
                craftedListed[Math.Clamp((int)band.Realm, 0, 3)]++;
            RememberName(band, item.Name);
            stock[band] = stock.GetValueOrDefault(band) + 1;
            stock[AllRealms(band)] = stock.GetValueOrDefault(AllRealms(band)) + 1;
        }
        ApplyHour(stock);

        var lines = new List<string>();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            var bands = _bands.Where(pair => pair.Key.Realm == realm).ToArray();
            if (bands.Length == 0)
                continue;
            double[] factors = bands.Select(pair => PriceIndexMath.Pressure(pair.Value.Stock, pair.Value.Demand)).OrderBy(f => f).ToArray();
            lines.Add(EconomyLog.Line(EconomyLog.EconIndex, ("realm", realm), ("bands", bands.Length),
                ("stock", bands.Sum(pair => pair.Value.Stock)), ("demand", bands.Sum(pair => pair.Value.Demand)),
                ("sales", bands.Sum(pair => pair.Value.Sales)), ("medianFactor", factors[factors.Length / 2]),
                ("flooded", factors.Count(f => f <= PriceIndexMath.MinFactor)), ("starved", factors.Count(f => f >= PriceIndexMath.MaxFactor)),
                ("craftedListed", craftedListed[(int)realm]), ("craftedPosted", Interlocked.Exchange(ref _craftedPosted[(int)realm], 0))));
        }
        if (Writes)
        {
            string updated = utcNow.ToString("O");
            foreach ((MarketBand band, BandState state) in _bands.Where(pair => pair.Key.Realm != eRealm.None))
            {
                PriceIndexMath.BandStats stats = state.Stats;
                if (stats.Stock + stats.Demand + stats.Sales == 0)
                    continue;
                Rows.Change(band.Key, () => new OfflineExchangeIndexRecord { BandKey = band.Key }, row =>
                {
                    row.Realm = (int)band.Realm;
                    row.Count = stats.Stock;
                    row.Demand = stats.Demand;
                    row.IndexCopper = stats.SaleEmaCopper;
                    row.FloorCopper = state.LastFloor;
                    row.CeilingCopper = state.LastCeiling;
                    row.UpdatedUtc = updated;
                });
            }
            if (Log.IsInfoEnabled)
                foreach (string line in lines)
                    Log.Info(line);
        }
        return lines;
    }

    /// <summary>
    /// An hour's turn of the index: every band's stock as counted (a band not counted has none; a band of no known realm
    /// holds the realms together), and its demand halved. The recount's second half; the rehearsal calls it on its own
    /// listings.
    /// </summary>
    public void ApplyHour(IReadOnlyDictionary<MarketBand, int> stock)
    {
        stock ??= new Dictionary<MarketBand, int>();
        foreach ((MarketBand band, BandState state) in _bands)
        {
            Volatile.Write(ref state.Stock, stock.GetValueOrDefault(band));
            int demand;
            do demand = Volatile.Read(ref state.Demand);
            while (Interlocked.CompareExchange(ref state.Demand, demand / 2, demand) != demand);
        }
        foreach ((MarketBand band, int count) in stock)
            if (!_bands.ContainsKey(band))
                Volatile.Write(ref State(band).Stock, count);
    }

    public void Load()
    {
        _bands.Clear();
        OfflineExchangeIndexRecord[] rows = GameServer.Database.SelectAllObjects<OfflineExchangeIndexRecord>().ToArray();
        Rows.Load(rows);
        foreach (OfflineExchangeIndexRecord row in rows)
            if (TryParseKey(row.BandKey, out MarketBand band))
            {
                BandState state = State(band);
                state.Stock = Math.Max(0, row.Count);
                state.Demand = Math.Max(0, row.Demand);
                state.SaleEma = Math.Max(0, row.IndexCopper);
                state.Sales = row.IndexCopper > 0 ? PriceIndexMath.MinSalesForHistory : 0;
            }
    }

    public void Pulse(long now) { }

    public void Flush(bool wait)
    {
        long now = Environment.TickCount64;
        if (!wait && now >= Volatile.Read(ref _nextHourTick))
        {
            bool first = Volatile.Read(ref _nextHourTick) == 0;
            Volatile.Write(ref _nextHourTick, now + HourMilliseconds);
            // The first pass waits an hour after start-up (the Exchange is still loading then); with bot_crafters off
            // there is no pass at all, so the server does exactly what it did before.
            if (!first && Writes)
            {
                try { Recount(DateTime.UtcNow); }
                catch (Exception exception) { Log.Warn("Price index: hourly recount failed", exception); }
            }
            QuartermasterCommissions.HourLines(DateTime.UtcNow);
        }
        Rows.Flush(wait);
    }

    public static bool TryParseKey(string key, out MarketBand band)
    {
        band = default;
        string[] parts = key?.Split('|');
        if (parts is not { Length: 5 } || !int.TryParse(parts[0], out int realm) || !int.TryParse(parts[1], out int objectType) ||
            !int.TryParse(parts[2], out int itemType) || !int.TryParse(parts[3], out int levelBand) || !int.TryParse(parts[4], out int tier))
            return false;
        band = new MarketBand((eRealm)realm, objectType, itemType, levelBand, tier);
        return true;
    }

    #endregion

    /// <summary>What /lscraft index says of an item (registered as its WP10 /lscraft part).</summary>
    public string[] ReportLines(DbInventoryItem item)
    {
        if (item == null)
            return ["Put an item in your first backpack slots, then /lscraft index."];
        MarketBand band = BandOf(item);
        PriceIndexMath.BandStats stats = StatsOf(band);
        PriceIndexMath.Quote quote = QuoteOf(item);
        return
        [
            $"{item.Name} (level {item.Level}, quality {item.Quality}{(item.IsCrafted ? ", crafted" : "")}), band {band.Key}: " +
            $"stock {stats.Stock}, demand {stats.Demand}, sales {stats.Sales} (running price {stats.SaleEmaCopper}c), " +
            $"pressure x{PriceIndexMath.Pressure(stats.Stock, stats.Demand):0.##}.",
            $"  floor {quote.Floor}c, fair {quote.Fair}c, ask {quote.Ask}c (a seller's mood of 0), bid {quote.Bid}c, spread {quote.Spread}c.",
        ];
    }

    /// <summary>Tests only: an empty index whose rows are written nowhere, and today's numbers again.</summary>
    public void ResetForTests()
    {
        _bands.Clear();
        _names.Clear();
        Array.Clear(_craftedPosted);
        _nextHourTick = 0;
        Rows = new LivingServerTable<string, OfflineExchangeIndexRecord>("exchange index", row => row.BandKey, StringComparer.Ordinal,
            database: () => null);
        FloorOf = AutonomousBotEconomy.CalculateStandardVendorSaleCopper;
        BaseOf = item => AutonomousBotEconomy.RecommendListingPrice(item);
        MerchantPriceOf = DefaultMerchantPrice;
        Market = () => MarketCache.SearchItems(new ItemQuery());
    }
}
