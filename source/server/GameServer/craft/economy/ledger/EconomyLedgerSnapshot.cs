using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The hourly sums of the economy ledger (Crafting 2.0 WP2), pure: two supply snapshots and the flow window between
    /// them give, per realm, the change in the money supply, the flows that explain it, and the residual:
    /// <code>
    /// supply   = bot purses + player wallets + unclaimed Exchange proceeds + guild banks
    /// flows    = net of every faucet and sink kind (AddMoney/TrySpend) - Exchange tax + coin of new bots - coin of removed bots
    ///            + players' coin out of craft-order escrow - players' coin into it (WP8)
    /// residual = (supply now - supply before) - flows
    /// </code>
    /// The residual is what the ledger does not see. On a normal server it is not zero: every player-side coin move
    /// (a player's loot, merchants, trainers, quests), guild dues taken from loot (they go to the bank directly) and raid
    /// loot shares (RealmRaidLootOwner pays purses directly) land in it. The bots' own residual,
    /// <c>bot_residual = (bot coin now - before) - (every AddMoney/TrySpend + Exchange proceeds - Exchange purchases + roster)</c>,
    /// is the part of it in bot purses; it is the raid loot (and moves racing the snapshot), so it shows how complete
    /// the instrumentation is.
    /// </summary>
    public static class EconomyLedgerSnapshot
    {
        public const int AllRealms = -1;
        private static readonly string[] RealmNames = ["None", "Albion", "Midgard", "Hibernia"];

        public static string RealmName(int realm) => realm == AllRealms ? "All" : realm is >= 0 and <= 3 ? RealmNames[realm] : "None";

        /// <summary>One realm's hour (or <see cref="AllRealms"/>).</summary>
        public sealed class RealmHour
        {
            public int Realm { get; init; }
            public DateTime EndUtc { get; init; }
            public long WindowSeconds { get; init; }
            /// <summary>The first snapshot after start-up: the supply only, nothing to compare with.</summary>
            public bool Baseline { get; init; }
            /// <summary>A window cut short by the shutdown (logged, no table row).</summary>
            public bool Partial { get; init; }
            public RealmSupply Supply { get; init; }
            public RealmSupply Before { get; init; }

            public long DeltaSupply { get; init; }
            public long DeltaBots { get; init; }
            public long DeltaPlayers { get; init; }
            public long DeltaGuilds { get; init; }
            public long Faucets { get; init; }
            public long Sinks { get; init; }
            /// <summary>Faucets - sinks of the AddMoney/TrySpend kinds (transfers excluded).</summary>
            public long FlowNet { get; init; }
            public long TransfersIn { get; init; }
            public long TransfersOut { get; init; }
            public long Moves { get; init; }
            public long Tax { get; init; }
            public long RosterIn { get; init; }
            public long RosterOut { get; init; }
            public int NewBots { get; init; }
            public int GoneBots { get; init; }
            /// <summary>Bots in both snapshots whose purse changed by more or less than their recorded moves.</summary>
            public int UnexplainedBots { get; init; }
            /// <summary>flows = FlowNet - Tax + RosterIn - RosterOut + the players' escrow net (WP8).</summary>
            public long Flows { get; init; }
            public long Residual { get; init; }
            public long BotFlows { get; init; }
            public long BotResidual { get; init; }
            /// <summary>Per kind (lower-case name): net when it is not a transfer, split by sign.</summary>
            public IReadOnlyDictionary<string, long> FaucetKinds { get; init; } = new Dictionary<string, long>();
            public IReadOnlyDictionary<string, long> SinkKinds { get; init; } = new Dictionary<string, long>();
        }

        /// <summary>The realms' hours and the All total, in the order Albion, Midgard, Hibernia, None (only with flows), All.</summary>
        public static IReadOnlyList<RealmHour> Compute(SupplySnapshot before, SupplySnapshot now, EconomyFlowLedger.Window window,
            DateTime endUtc, bool partial = false)
        {
            window ??= new EconomyFlowLedger.Window(null);
            bool baseline = before == null;
            long seconds = baseline ? 0 : Math.Max(0, (long)Math.Round((now.TakenUtc - before.TakenUtc).TotalSeconds));

            // Roster: bots that appeared or went since the last snapshot bring or take their purse.
            // A new bot brings the purse it had before its recorded moves; a removed one takes its last purse plus its
            // moves since. A bot in both whose change its moves do not explain is counted (raid loot, a racing move).
            var rosterIn = new long[EconomyFlowLedger.RealmSlots];
            var rosterOut = new long[EconomyFlowLedger.RealmSlots];
            var newBots = new int[EconomyFlowLedger.RealmSlots];
            var goneBots = new int[EconomyFlowLedger.RealmSlots];
            var unexplained = new int[EconomyFlowLedger.RealmSlots];
            if (!baseline)
            {
                foreach ((long botId, (int realm, _, long coin)) in now.Purses)
                {
                    if (!before.Purses.TryGetValue(botId, out var prior))
                    {
                        rosterIn[realm] += coin - window.BotNet(botId);
                        newBots[realm]++;
                    }
                    else if (coin - prior.Coin != window.BotNet(botId))
                        unexplained[realm]++;
                }
                foreach ((long botId, (int realm, _, long coin)) in before.Purses)
                    if (!now.Purses.ContainsKey(botId))
                    {
                        rosterOut[realm] += coin + window.BotNet(botId);
                        goneBots[realm]++;
                    }
            }

            var hours = new List<RealmHour>();
            int[] order = [1, 2, 3, 0];
            foreach (int realm in order)
            {
                RealmSupply supply = now.Realms[realm];
                RealmSupply prior = baseline ? null : before.Realms[realm];
                if (realm == 0 && window.IsEmpty(0) && supply.Total == 0 && (prior?.Total ?? 0) == 0)
                    continue;
                hours.Add(Hour(realm, endUtc, seconds, baseline, partial, supply, prior, window, [realm],
                    rosterIn[realm], rosterOut[realm], newBots[realm], goneBots[realm], unexplained[realm]));
            }
            // All: the realms' supplies summed, every realm's flows (the unknown slot included).
            RealmSupply all = Sum(now.Realms);
            RealmSupply allBefore = baseline ? null : Sum(before.Realms);
            hours.Add(Hour(AllRealms, endUtc, seconds, baseline, partial, all, allBefore, window, [0, 1, 2, 3],
                rosterIn.Sum(), rosterOut.Sum(), newBots.Sum(), goneBots.Sum(), unexplained.Sum()));
            return hours;
        }

        private static RealmHour Hour(int realm, DateTime endUtc, long seconds, bool baseline, bool partial, RealmSupply supply,
            RealmSupply prior, EconomyFlowLedger.Window window, int[] slots, long rosterIn, long rosterOut, int newBots, int goneBots,
            int unexplained)
        {
            var faucets = new Dictionary<string, long>();
            var sinks = new Dictionary<string, long>();
            long transfersIn = 0, transfersOut = 0, purseNet = 0, moves = 0;
            for (int k = 0; k < EconomyFlowClassifier.KindCount; k++)
            {
                var kind = (EconomyFlowKind)k;
                long net = 0, gin = 0, gout = 0;
                foreach (int slot in slots)
                {
                    net += window.Net(slot, kind);
                    gin += window.In(slot, kind);
                    gout += window.Out(slot, kind);
                    moves += window.InCount(slot, kind) + window.OutCount(slot, kind);
                }
                purseNet += net;
                // Hired companions: a player's named coin to the world (a hire, a companion's repairs) is a faucet or a
                // sink of the supply like a bot's, but no bot purse moved, so it stays out of purseNet (the bots' flows).
                long playerNet = 0;
                foreach (int slot in slots)
                    playerNet += window.PlayerNet(slot, kind);
                if (EconomyFlowClassifier.IsTransfer(kind))
                {
                    transfersIn += gin;
                    transfersOut += gout;
                    net = playerNet;
                }
                else
                    net += playerNet;
                if (net > 0)
                    faucets[EconomyFlowClassifier.KeyName(kind)] = net;
                else if (net < 0)
                    sinks[EconomyFlowClassifier.KeyName(kind)] = -net;
            }
            long faucetSum = faucets.Values.Sum();
            long sinkSum = sinks.Values.Sum();
            long flowNet = faucetSum - sinkSum;
            // The Exchange tax is a sink of the supply that no AddMoney/TrySpend records (it is kept from the proceeds).
            long tax = slots.Sum(window.SalesTax);
            if (tax > 0)
            {
                string taxKey = EconomyFlowClassifier.KeyName(EconomyFlowKind.ExchangeTax);
                sinks[taxKey] = sinks.GetValueOrDefault(taxKey) + tax;
            }
            // WP8: a player's coin into a craft order's escrow leaves the supply (and comes back on a refund); no bot moved it.
            long escrow = slots.Sum(window.PlayerEscrow);
            long flows = flowNet - tax + rosterIn - rosterOut + escrow;
            long botFlows = purseNet + slots.Sum(window.BotReceived) - slots.Sum(window.BotPaid) + rosterIn - rosterOut;

            long deltaSupply = prior == null ? 0 : supply.Total - prior.Total;
            long deltaBots = prior == null ? 0 : supply.BotCoin - prior.BotCoin;
            return new RealmHour
            {
                Realm = realm, EndUtc = endUtc, WindowSeconds = seconds, Baseline = prior == null, Partial = partial,
                Supply = supply, Before = prior,
                DeltaSupply = deltaSupply,
                DeltaBots = deltaBots,
                DeltaPlayers = prior == null ? 0 : supply.PlayerCoin - prior.PlayerCoin,
                DeltaGuilds = prior == null ? 0 : supply.GuildBanks - prior.GuildBanks,
                Faucets = faucetSum, Sinks = sinkSum + tax, FlowNet = flowNet,
                TransfersIn = transfersIn, TransfersOut = transfersOut, Moves = moves, Tax = tax,
                RosterIn = rosterIn, RosterOut = rosterOut, NewBots = newBots, GoneBots = goneBots, UnexplainedBots = unexplained,
                Flows = flows,
                Residual = prior == null ? 0 : deltaSupply - flows,
                BotFlows = botFlows,
                BotResidual = prior == null ? 0 : deltaBots - botFlows,
                FaucetKinds = faucets.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                SinkKinds = sinks.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            };
        }

        private static RealmSupply Sum(RealmSupply[] realms)
        {
            var sum = new RealmSupply();
            var medians = new List<long>();
            foreach (RealmSupply realm in realms)
            {
                sum.BotCoin += realm.BotCoin;
                sum.PlayerWallets += realm.PlayerWallets;
                sum.PlayerProceeds += realm.PlayerProceeds;
                sum.GuildBanks += realm.GuildBanks;
                sum.ListedValue += realm.ListedValue;
                sum.Listings += realm.Listings;
                sum.BotListings += realm.BotListings;
                sum.Bots += realm.Bots;
                sum.Online += realm.Online;
                sum.Crafters += realm.Crafters;
                for (int band = 0; band < 6; band++)
                {
                    sum.BandBots[band] += realm.BandBots[band];
                    sum.BandCoin[band] += realm.BandCoin[band];
                }
            }
            // Medians do not sum; All keeps none (each realm's line has its own).
            return sum;
        }

        // ---- lines ----

        /// <summary>The ECON_SUPPLY line of one realm's hour.</summary>
        public static string SupplyLine(RealmHour hour)
        {
            RealmSupply s = hour.Supply;
            var pairs = new List<(string, object)>
            {
                ("hour", hour.EndUtc), ("realm", new Word(RealmName(hour.Realm))), ("window", hour.WindowSeconds),
            };
            if (hour.Baseline) pairs.Add(("baseline", true));
            if (hour.Partial) pairs.Add(("partial", true));
            pairs.Add(("supply", s.Total));
            if (!hour.Baseline)
            {
                pairs.Add(("d_supply", hour.DeltaSupply));
                pairs.Add(("flows", hour.Flows));
                pairs.Add(("residual", hour.Residual));
            }
            pairs.Add(("bot_coin", s.BotCoin));
            pairs.Add(("player_wallets", s.PlayerWallets));
            pairs.Add(("player_proceeds", s.PlayerProceeds));
            pairs.Add(("guild_banks", s.GuildBanks));
            if (!hour.Baseline)
            {
                pairs.Add(("d_bots", hour.DeltaBots));
                pairs.Add(("d_players", hour.DeltaPlayers));
                pairs.Add(("d_guilds", hour.DeltaGuilds));
                pairs.Add(("bot_flows", hour.BotFlows));
                pairs.Add(("bot_residual", hour.BotResidual));
                pairs.Add(("roster_in", hour.RosterIn));
                pairs.Add(("roster_out", hour.RosterOut));
                pairs.Add(("new_bots", hour.NewBots));
                pairs.Add(("gone_bots", hour.GoneBots));
                pairs.Add(("bots_unexplained", hour.UnexplainedBots));
            }
            pairs.Add(("bots", s.Bots));
            pairs.Add(("online", s.Online));
            pairs.Add(("crafters", s.Crafters));
            if (hour.Realm != AllRealms)
                pairs.Add(("median", s.MedianBotCoin));
            pairs.Add(("listed", s.ListedValue));
            pairs.Add(("listings", s.Listings));
            pairs.Add(("bot_listings", s.BotListings));
            for (int band = 1; band < 6; band++)
            {
                if (s.BandBots[band] == 0)
                    continue;
                string name = EconomyFlowLedger.BandNames[band];
                pairs.Add(($"bots_{name}", s.BandBots[band]));
                pairs.Add(($"coin_{name}", s.BandCoin[band]));
                if (hour.Realm != AllRealms)
                    pairs.Add(($"median_{name}", s.BandMedian[band]));
            }
            return EconomyLog.Line(EconomyLog.EconSupply, pairs.ToArray());
        }

        /// <summary>The ECON_FLOWS line of one realm's hour; null for a baseline (no window yet).</summary>
        public static string FlowsLine(RealmHour hour, EconomyFlowLedger.Window window, int topUnattributed = 5)
        {
            if (hour.Baseline)
                return null;
            int[] slots = hour.Realm == AllRealms ? [0, 1, 2, 3] : [hour.Realm];
            var pairs = new List<(string, object)>
            {
                ("hour", hour.EndUtc), ("realm", new Word(RealmName(hour.Realm))), ("window", hour.WindowSeconds),
            };
            if (hour.Partial) pairs.Add(("partial", true));
            pairs.Add(("faucets", hour.Faucets));
            pairs.Add(("sinks", hour.Sinks));
            pairs.Add(("net", hour.Faucets - hour.Sinks));
            pairs.Add(("per_online_bot_hour", PerBotHour(hour.Faucets - hour.Sinks, hour.Supply.Online, hour.WindowSeconds)));
            pairs.Add(("transfers_in", hour.TransfersIn));
            pairs.Add(("transfers_out", hour.TransfersOut));
            pairs.Add(("moves", hour.Moves));
            pairs.Add(("exchange_sales", slots.Sum(window.Sales)));
            pairs.Add(("exchange_copper", slots.Sum(window.SalesCopper)));
            pairs.Add(("exchange_tax", hour.Tax));
            pairs.Add(("exchange_crafted", slots.Sum(window.SalesCrafted)));
            pairs.Add(("exchange_bots_paid", slots.Sum(window.BotPaid)));
            pairs.Add(("exchange_bots_received", slots.Sum(window.BotReceived)));
            pairs.Add(("exchange_players_paid", slots.Sum(window.PlayerPaid)));
            pairs.Add(("exchange_players_received", slots.Sum(window.PlayerReceived)));
            // True165's per-kind keys: in_<kind>=copper/count, out_<kind>=copper/count.
            for (int k = 0; k < EconomyFlowClassifier.KindCount; k++)
            {
                var kind = (EconomyFlowKind)k;
                long gin = slots.Sum(slot => window.In(slot, kind)), gout = slots.Sum(slot => window.Out(slot, kind));
                long cin = slots.Sum(slot => window.InCount(slot, kind)), cout = slots.Sum(slot => window.OutCount(slot, kind));
                string name = EconomyFlowClassifier.KeyName(kind);
                if (gin > 0) pairs.Add(($"in_{name}", new CopperCount(gin, cin)));
                if (gout > 0) pairs.Add(($"out_{name}", new CopperCount(gout, cout)));
                // hired companions: what players paid or got for this kind (player_in_companionhire=copper)
                long pin = slots.Sum(slot => window.PlayerIn(slot, kind)), pout = slots.Sum(slot => window.PlayerOut(slot, kind));
                if (pin > 0) pairs.Add(($"player_in_{name}", pin));
                if (pout > 0) pairs.Add(($"player_out_{name}", pout));
            }
            for (int band = 0; band < 6; band++)
            {
                long earned = slots.Sum(slot => window.BandIn(slot, band));
                long net = slots.Sum(slot => window.BandNet(slot, band));
                if (earned == 0 && net == 0)
                    continue;
                string name = EconomyFlowLedger.BandNames[band];
                pairs.Add(($"earned_{name}", earned));
                pairs.Add(($"net_{name}", net));
            }
            // The callers the classifier could not name (all realms together, on the All line): a new call site to name.
            IReadOnlyList<(string Caller, long In, long Out, long Count)> unattributed =
                hour.Realm == AllRealms ? window.Unattributed(topUnattributed) : [];
            if (unattributed.Count > 0)
                pairs.Add(("unattributed", string.Join(", ", unattributed
                    .Select(entry => $"{entry.Caller} in {entry.In} out {entry.Out} x{entry.Count}"))));
            return EconomyLog.Line(EconomyLog.EconFlows, pairs.ToArray());
        }

        /// <summary>
        /// True165's own line, kept as an alias (plan §2.5) so a tool written for True165's logs still reads ours:
        /// <c>BOT_MONEY_LEDGER_165 window=3600s in_total=… out_total=… net=… in_&lt;kind&gt;=copper/count …</c>, every realm
        /// together, AddMoney/TrySpend moves only (as True165 counted them). Null for a baseline or an hour with no move,
        /// as True165's Flush.
        /// </summary>
        public static string True165Line(RealmHour all, EconomyFlowLedger.Window window)
        {
            if (all == null || all.Baseline || window == null)
                return null;
            int[] slots = [0, 1, 2, 3];
            long totalIn = 0, totalOut = 0;
            var parts = new System.Text.StringBuilder();
            for (int k = 0; k < EconomyFlowClassifier.KindCount; k++)
            {
                var kind = (EconomyFlowKind)k;
                long gin = slots.Sum(slot => window.In(slot, kind)), gout = slots.Sum(slot => window.Out(slot, kind));
                long cin = slots.Sum(slot => window.InCount(slot, kind)), cout = slots.Sum(slot => window.OutCount(slot, kind));
                string name = EconomyFlowClassifier.KeyName(kind);
                if (gin > 0)
                    parts.Append(" in_").Append(name).Append('=').Append(new CopperCount(gin, cin));
                if (gout > 0)
                    parts.Append(" out_").Append(name).Append('=').Append(new CopperCount(gout, cout));
                totalIn += gin;
                totalOut += gout;
            }
            if (totalIn == 0 && totalOut == 0)
                return null;
            return string.Create(CultureInfo.InvariantCulture,
                $"{EconomyLog.True165MoneyLedger} window={all.WindowSeconds}s in_total={totalIn} out_total={totalOut} net={totalIn - totalOut}{parts}");
        }

        private static long PerBotHour(long net, int online, long seconds) =>
            online <= 0 || seconds <= 0 ? 0 : (long)Math.Round(net * 3600.0 / seconds / online);

        public static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // ---- rows ----

        /// <summary>The table rows of an hour: one per realm (Albion, Midgard, Hibernia); none for a baseline or a partial hour.</summary>
        public static IReadOnlyList<OfflineEconomyLedgerRecord> Rows(IEnumerable<RealmHour> hours) =>
            hours.Where(hour => !hour.Baseline && !hour.Partial && hour.Realm is >= 1 and <= 3)
                .Select(hour => new OfflineEconomyLedgerRecord
                {
                    HourUtc = Stamp(hour.EndUtc),
                    Realm = hour.Realm,
                    BotCoin = hour.Supply.BotCoin,
                    PlayerCoin = hour.Supply.PlayerCoin,
                    GuildBanks = hour.Supply.GuildBanks,
                    ListedValue = hour.Supply.ListedValue,
                    FaucetsJson = JsonSerializer.Serialize(hour.FaucetKinds),
                    SinksJson = JsonSerializer.Serialize(hour.SinkKinds),
                    ResidualCopper = hour.Residual,
                    Bots = hour.Supply.Bots,
                    Crafters = hour.Supply.Crafters,
                }).ToArray();

        /// <summary>
        /// A bare word in a line. EconomyLog quotes every string; a realm name is a token, written unquoted as the
        /// AUTONOMOUS_* lines write it (realm=Albion), so a line reads and greps the same way.
        /// </summary>
        public readonly struct Word : IFormattable
        {
            private readonly string _text;
            public Word(string text) => _text = text ?? string.Empty;
            public string ToString(string format, IFormatProvider provider) => _text;
            public override string ToString() => _text;
        }

        /// <summary>A copper sum and how many moves made it, written as copper/count (True165's format).</summary>
        public readonly struct CopperCount : IFormattable
        {
            public CopperCount(long copper, long count) { Copper = copper; Count = count; }
            public long Copper { get; }
            public long Count { get; }
            public string ToString(string format, IFormatProvider provider) =>
                Copper.ToString(CultureInfo.InvariantCulture) + "/" + Count.ToString(CultureInfo.InvariantCulture);
            public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
        }
    }
}
