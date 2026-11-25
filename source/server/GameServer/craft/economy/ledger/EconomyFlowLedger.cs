using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The coin-flow half of the economy ledger (Crafting 2.0 WP2), ported from shadowofze's BotMoneyLedger165
    /// (a8c793e, df86f9f), via True165, GPL-3.0: every gamebot AddMoney / TrySpend is summed by kind, in and out with a
    /// count, at the cost of a few Interlocked adds. What changed in the port: it is WP0's <see cref="IEconomyFlowSink"/>
    /// (so no call site names a kind; <see cref="EconomyFlowClassifier"/> does), the sums are per realm and per level
    /// band, the Exchange's committed sales are counted from <see cref="EconomySeams.SaleCommitted"/> (transfers and the
    /// tax), callers that could not be classified are listed by name, and the hourly line is written by
    /// <see cref="EconomyLedger"/> together with the money-supply snapshot instead of by the first move after the hour.
    /// Thread-safe and lock-free: brain turns call it in parallel.
    /// </summary>
    public sealed class EconomyFlowLedger : IEconomyFlowSink
    {
        /// <summary>Realm slots: 0 = not known (no live bot and not seen in a snapshot yet), 1-3 = Albion, Midgard, Hibernia.</summary>
        public const int RealmSlots = 4;

        /// <summary>Level bands (M0's 21-30, 31-40, 41-49, 50, plus 1-20); slot 0 = level not known.</summary>
        public static readonly string[] BandNames = ["unknown", "1_20", "21_30", "31_40", "41_49", "50"];

        public static int BandOf(int level) => level switch
        {
            <= 0 => 0,
            <= 20 => 1,
            <= 30 => 2,
            <= 40 => 3,
            <= 49 => 4,
            _ => 5,
        };

        /// <summary>One window's sums. Writers add to the current one; <see cref="TakeWindow"/> swaps in a fresh one.</summary>
        public sealed class Counters
        {
            internal readonly long[] In = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            internal readonly long[] Out = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            internal readonly long[] InCount = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            internal readonly long[] OutCount = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            // Faucet/sink kinds only (no transfers), per realm and band: gross in and signed net.
            internal readonly long[] BandIn = new long[RealmSlots * 6];
            internal readonly long[] BandNet = new long[RealmSlots * 6];
            // The Exchange, per realm.
            internal readonly long[] Sales = new long[RealmSlots];
            internal readonly long[] SalesCopper = new long[RealmSlots];
            internal readonly long[] SalesTax = new long[RealmSlots];
            internal readonly long[] SalesCrafted = new long[RealmSlots];
            // Issue #625 (ECONOMY_PULSE): sale prices per realm and slot band, at most MaxSalePrices each (counted beyond).
            internal readonly List<long>[] SaleBandPrices = Enumerable.Range(0, RealmSlots * EconomyPulse.SlotBandCount)
                .Select(_ => new List<long>()).ToArray();
            internal readonly long[] SaleBandCount = new long[RealmSlots * EconomyPulse.SlotBandCount];
            internal readonly long[] BotPaid = new long[RealmSlots];
            internal readonly long[] BotReceived = new long[RealmSlots];
            internal readonly long[] PlayerPaid = new long[RealmSlots];
            internal readonly long[] PlayerReceived = new long[RealmSlots];
            // WP8: players' coin into (negative) and out of (positive) craft-order escrow, per realm.
            internal readonly long[] PlayerEscrow = new long[RealmSlots];
            // Hired companions: players' named coin to and from the world (a hire, a companion's repairs), per realm and kind.
            internal readonly long[] PlayerIn = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            internal readonly long[] PlayerOut = new long[RealmSlots * EconomyFlowClassifier.KindCount];
            internal readonly ConcurrentDictionary<string, long[]> Unattributed = new(StringComparer.Ordinal);
            // Each bot's net (every kind and its Exchange trades), so a bot met mid-hour brings only the purse it had.
            internal readonly ConcurrentDictionary<long, long> BotNet = new();
        }

        /// <summary>A closed window, read-only: what the hourly lines and rows are made from.</summary>
        public sealed class Window
        {
            public Window(Counters counters) => Sums = counters ?? new Counters();
            internal Counters Sums { get; }

            public long In(int realm, EconomyFlowKind kind) => Sums.In[Index(realm, kind)];
            public long Out(int realm, EconomyFlowKind kind) => Sums.Out[Index(realm, kind)];
            public long InCount(int realm, EconomyFlowKind kind) => Sums.InCount[Index(realm, kind)];
            public long OutCount(int realm, EconomyFlowKind kind) => Sums.OutCount[Index(realm, kind)];
            public long Net(int realm, EconomyFlowKind kind) => In(realm, kind) - Out(realm, kind);
            public long BandIn(int realm, int band) => Sums.BandIn[realm * 6 + band];
            public long BandNet(int realm, int band) => Sums.BandNet[realm * 6 + band];
            public long Sales(int realm) => Sums.Sales[realm];
            public long SalesCopper(int realm) => Sums.SalesCopper[realm];
            public long SalesTax(int realm) => Sums.SalesTax[realm];
            public long SalesCrafted(int realm) => Sums.SalesCrafted[realm];
            /// <summary>Issue #625: sales in a slot band (EconomyPulse.SlotBands).</summary>
            public long SaleBandCount(int realm, int band) => Sums.SaleBandCount[realm * EconomyPulse.SlotBandCount + band];
            /// <summary>Issue #625: the median price of a slot band's sales (of the first MaxSalePrices), 0 for none.</summary>
            public long SaleBandMedian(int realm, int band)
            {
                List<long> prices = Sums.SaleBandPrices[realm * EconomyPulse.SlotBandCount + band];
                lock (prices)
                    return SupplyBuilder.Median(new List<long>(prices));
            }
            public long BotPaid(int realm) => Sums.BotPaid[realm];
            public long BotReceived(int realm) => Sums.BotReceived[realm];
            public long PlayerPaid(int realm) => Sums.PlayerPaid[realm];
            public long PlayerReceived(int realm) => Sums.PlayerReceived[realm];
            /// <summary>WP8: the net of players' coin into and out of craft-order escrow (outside the supply).</summary>
            public long PlayerEscrow(int realm) => Sums.PlayerEscrow[realm];
            public long PlayerIn(int realm, EconomyFlowKind kind) => Sums.PlayerIn[Index(realm, kind)];
            public long PlayerOut(int realm, EconomyFlowKind kind) => Sums.PlayerOut[Index(realm, kind)];
            /// <summary>Players' named coin for one kind (a hire, a companion's repairs): in minus out.</summary>
            public long PlayerNet(int realm, EconomyFlowKind kind) => PlayerIn(realm, kind) - PlayerOut(realm, kind);

            public bool HasPlayerFlows(int realm)
            {
                for (int kind = 0; kind < EconomyFlowClassifier.KindCount; kind++)
                    if (PlayerIn(realm, (EconomyFlowKind)kind) != 0 || PlayerOut(realm, (EconomyFlowKind)kind) != 0)
                        return true;
                return false;
            }
            /// <summary>What a bot's purse gained (or lost) through recorded moves and Exchange trades in the window.</summary>
            public long BotNet(long botId) => Sums.BotNet.TryGetValue(botId, out long net) ? net : 0;

            /// <summary>Unclassified callers ("File.cs:Member") with their in, out and count, largest first.</summary>
            public IReadOnlyList<(string Caller, long In, long Out, long Count)> Unattributed(int top) =>
                Sums.Unattributed.Select(pair => (pair.Key, pair.Value[0], pair.Value[1], pair.Value[2]))
                    .OrderByDescending(entry => entry.Item2 + entry.Item3).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                    .Take(top).ToArray();

            /// <summary>Net of every kind that is not a transfer: what came into (or left) the money supply.</summary>
            public long ExternalNet(int realm)
            {
                long net = 0;
                for (int kind = 0; kind < EconomyFlowClassifier.KindCount; kind++)
                    if (!EconomyFlowClassifier.IsTransfer((EconomyFlowKind)kind))
                        net += Net(realm, (EconomyFlowKind)kind);
                return net;
            }

            /// <summary>Net of every kind, transfers included: what AddMoney/TrySpend moved in and out of bot purses.</summary>
            public long PurseNet(int realm)
            {
                long net = 0;
                for (int kind = 0; kind < EconomyFlowClassifier.KindCount; kind++)
                    net += Net(realm, (EconomyFlowKind)kind);
                return net;
            }

            public long Moves(int realm)
            {
                long moves = 0;
                for (int kind = 0; kind < EconomyFlowClassifier.KindCount; kind++)
                    moves += InCount(realm, (EconomyFlowKind)kind) + OutCount(realm, (EconomyFlowKind)kind);
                return moves;
            }

            public bool IsEmpty(int realm) => Moves(realm) == 0 && Sales(realm) == 0 && PlayerEscrow(realm) == 0 && !HasPlayerFlows(realm);
        }

        private static int Index(int realm, EconomyFlowKind kind)
        {
            int k = (int)kind;
            if ((uint)k >= (uint)EconomyFlowClassifier.KindCount)
                k = 0;
            return Math.Clamp(realm, 0, RealmSlots - 1) * EconomyFlowClassifier.KindCount + k;
        }

        private Counters _current = new();

        /// <summary>Realm and level of a bot (a live bot first, else what the last snapshot saw). Replaceable by tests.</summary>
        public Func<long, (int Realm, int Level)> BotInfo { get; set; } = _ => (0, 0);

        /// <summary>False: nothing is counted (economy_ledger_enabled off). Replaceable by tests.</summary>
        public Func<bool> Enabled { get; set; } = () => true;

        public void Record(long botId, long signedCopper, EconomyFlowKind kind, string callerFile, string callerMember)
        {
            if (signedCopper == 0 || !Enabled())
                return;
            EconomyFlowKind named = EconomyFlowClassifier.Classify(kind, callerFile, callerMember);
            (int realm, int level) = BotInfo(botId);
            realm = Math.Clamp(realm, 0, RealmSlots - 1);
            Counters sums = Volatile.Read(ref _current);
            int index = Index(realm, named);
            if (signedCopper > 0)
            {
                Interlocked.Add(ref sums.In[index], signedCopper);
                Interlocked.Increment(ref sums.InCount[index]);
            }
            else
            {
                Interlocked.Add(ref sums.Out[index], -signedCopper);
                Interlocked.Increment(ref sums.OutCount[index]);
            }
            AddBotNet(sums, botId, signedCopper);
            if (!EconomyFlowClassifier.IsTransfer(named))
            {
                int band = realm * 6 + BandOf(level);
                if (signedCopper > 0)
                    Interlocked.Add(ref sums.BandIn[band], signedCopper);
                Interlocked.Add(ref sums.BandNet[band], signedCopper);
            }
            if (named == EconomyFlowKind.Unattributed)
            {
                string caller = $"{EconomyFlows.FileName(callerFile)}:{callerMember}";
                long[] entry = sums.Unattributed.GetOrAdd(caller, static _ => new long[3]);
                Interlocked.Add(ref entry[signedCopper > 0 ? 0 : 1], Math.Abs(signedCopper));
                Interlocked.Increment(ref entry[2]);
            }
        }

        /// <summary>
        /// A committed Exchange sale (EconomySeams.SaleCommitted, under the Exchange's locks: counters only). The buyer
        /// paid the price; the seller got the price less the tax; the tax left the money supply.
        /// </summary>
        public void OnSaleCommitted(RealmExchangeSaleLedger.Sale sale)
        {
            if (sale == null || sale.PriceCopper <= 0 || !Enabled())
                return;
            int realm = Math.Clamp(sale.Realm, 0, RealmSlots - 1);
            long tax = Math.Clamp(sale.TaxCopper, 0, sale.PriceCopper);
            long proceeds = sale.PriceCopper - tax;
            Counters sums = Volatile.Read(ref _current);
            Interlocked.Increment(ref sums.Sales[realm]);
            Interlocked.Add(ref sums.SalesCopper[realm], sale.PriceCopper);
            Interlocked.Add(ref sums.SalesTax[realm], tax);
            if (sale.Crafted)
                Interlocked.Increment(ref sums.SalesCrafted[realm]);
            int saleBand = realm * EconomyPulse.SlotBandCount + EconomyPulse.SlotBand(sale.ItemType);
            Interlocked.Increment(ref sums.SaleBandCount[saleBand]);
            List<long> prices = sums.SaleBandPrices[saleBand];
            lock (prices)
                if (prices.Count < EconomyPulse.MaxSalePrices)
                    prices.Add(sale.PriceCopper);
            if (AutonomousBotEconomy.TryParseOwnerId(sale.BuyerId, out long buyerBot))
            {
                Interlocked.Add(ref sums.BotPaid[realm], sale.PriceCopper);
                AddBotNet(sums, buyerBot, -sale.PriceCopper);
            }
            else
                Interlocked.Add(ref sums.PlayerPaid[realm], sale.PriceCopper);
            if (AutonomousBotEconomy.TryParseOwnerId(sale.SellerId, out long sellerBot))
            {
                Interlocked.Add(ref sums.BotReceived[realm], proceeds);
                AddBotNet(sums, sellerBot, proceeds);
            }
            else
                Interlocked.Add(ref sums.PlayerReceived[realm], proceeds);
        }

        /// <summary>WP8: a player's coin into (negative) or out of (positive) a craft order's escrow.</summary>
        public void RecordPlayerEscrow(int realm, long signedCopper)
        {
            if (signedCopper == 0 || !Enabled())
                return;
            Interlocked.Add(ref Volatile.Read(ref _current).PlayerEscrow[Math.Clamp(realm, 0, RealmSlots - 1)], signedCopper);
        }

        /// <summary>Hired companions: a player's named coin to (negative) or from (positive) the world.</summary>
        public void RecordPlayer(int realm, long signedCopper, EconomyFlowKind kind)
        {
            if (signedCopper == 0 || !Enabled())
                return;
            Counters sums = Volatile.Read(ref _current);
            int index = Index(Math.Clamp(realm, 0, RealmSlots - 1), kind);
            if (signedCopper > 0)
                Interlocked.Add(ref sums.PlayerIn[index], signedCopper);
            else
                Interlocked.Add(ref sums.PlayerOut[index], -signedCopper);
        }

        private static void AddBotNet(Counters sums, long botId, long copper) =>
            sums.BotNet.AddOrUpdate(botId, static (_, add) => add, static (_, net, add) => net + add, copper);

        /// <summary>Closes the window: returns its sums and starts a new one (a move racing the swap may land in either).</summary>
        public Window TakeWindow() => new(Interlocked.Exchange(ref _current, new Counters()));

        /// <summary>Tests: clears the sums.</summary>
        public void Reset() => Interlocked.Exchange(ref _current, new Counters());
    }
}
