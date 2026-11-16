using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP2: the hourly snapshot. residual = Δsupply − Σflows, per realm and for All; transfers (beggar gifts,
    /// Exchange sales) move coin without changing the supply; the Exchange tax is a sink; bots that appear or go bring or
    /// take their purse; the bots' own residual is zero when every purse change went through AddMoney/TrySpend or the
    /// Exchange. Then the ledger's hour: a baseline at start, one line pair per realm and a row per realm each UTC hour,
    /// a failed read that keeps the window open, a partial hour at shutdown, and the setting.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EconomyLedgerSnapshot
    {
        private static readonly DateTime H20 = new(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime H21 = H20.AddHours(1);

        private static EconomyFlowLedger Ledger(Dictionary<long, (int, int)> bots) =>
            new() { BotInfo = id => bots.TryGetValue(id, out var info) ? info : (0, 0) };

        private static EconomyLedgerSnapshot.RealmHour Realm(IEnumerable<EconomyLedgerSnapshot.RealmHour> hours, int realm) =>
            hours.Single(hour => hour.Realm == realm);

        [Test]
        public void ResidualIsTheChangeInSupplyLessTheFlows()
        {
            var bots = new Dictionary<long, (int, int)> { [1] = (1, 25), [2] = (1, 50) };
            var before = new SupplyBuilder().Bot(1, 1, 25, 1_000).Bot(2, 1, 50, 5_000).Wallet(1, 700).Guild(1, 300).Build(H20);
            EconomyFlowLedger flows = Ledger(bots);
            flows.Record(1, 400, EconomyFlowKind.LootCoin, "", "");     // faucet
            flows.Record(2, -150, EconomyFlowKind.VendorGear, "", "");  // sink
            // Every purse change is a recorded flow; the player found 55 copper the ledger cannot see.
            var after = new SupplyBuilder().Bot(1, 1, 25, 1_400).Bot(2, 1, 50, 4_850).Wallet(1, 755).Guild(1, 300).Build(H21);

            EconomyLedgerSnapshot.RealmHour albion = Realm(EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), H21), 1);
            Assert.That(albion.DeltaSupply, Is.EqualTo(305));
            Assert.That(albion.Flows, Is.EqualTo(250));
            Assert.That(albion.Residual, Is.EqualTo(55), "Δsupply − Σflows: the player's unseen coin");
            Assert.That(albion.Residual, Is.EqualTo(albion.DeltaSupply - albion.Flows));
            Assert.That(albion.BotResidual, Is.Zero, "every bot purse change was seen");
            Assert.That(albion.DeltaPlayers, Is.EqualTo(55));
            Assert.That(albion.Faucets, Is.EqualTo(400));
            Assert.That(albion.Sinks, Is.EqualTo(150));
            Assert.That(albion.WindowSeconds, Is.EqualTo(3600));
        }

        [Test]
        public void TransfersMoveCoinWithoutChangingTheSupply()
        {
            var bots = new Dictionary<long, (int, int)> { [1] = (2, 30), [2] = (2, 30) };
            var before = new SupplyBuilder().Bot(1, 2, 30, 1_000).Bot(2, 2, 30, 0).Wallet(2, 500).Build(H20);
            EconomyFlowLedger flows = Ledger(bots);
            // A bot gives a beggar bot 100; the player drags 50 onto the beggar.
            flows.Record(1, -100, EconomyFlowKind.Unattributed, "AutonomousBeggars.cs", "TryBegBot");
            flows.Record(2, 100, EconomyFlowKind.Unattributed, "AutonomousBeggars.cs", "TryBegBot");
            flows.Record(2, 50, EconomyFlowKind.Unattributed, "AutonomousBeggars.cs", "TryReceiveMoney");
            var after = new SupplyBuilder().Bot(1, 2, 30, 900).Bot(2, 2, 30, 150).Wallet(2, 450).Build(H21);

            EconomyLedgerSnapshot.RealmHour midgard = Realm(EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), H21), 2);
            Assert.That(midgard.DeltaSupply, Is.Zero);
            Assert.That(midgard.Flows, Is.Zero);
            Assert.That(midgard.Residual, Is.Zero);
            Assert.That(midgard.DeltaBots, Is.EqualTo(50));
            Assert.That(midgard.BotFlows, Is.EqualTo(50), "the bots received the player's gift");
            Assert.That(midgard.BotResidual, Is.Zero);
            Assert.That(midgard.TransfersIn, Is.EqualTo(150));
            Assert.That(midgard.TransfersOut, Is.EqualTo(100));
            Assert.That(midgard.Faucets + midgard.Sinks, Is.Zero, "a gift is neither faucet nor sink");
        }

        [Test]
        public void AnExchangeSaleIsATransferAndItsTaxASink()
        {
            var bots = new Dictionary<long, (int, int)> { [1] = (3, 50), [2] = (3, 50) };
            var before = new SupplyBuilder().Bot(1, 3, 50, 10_000).Bot(2, 3, 50, 0).Proceeds(3, 0).Wallet(3, 2_000).Build(H20);
            EconomyFlowLedger flows = Ledger(bots);
            // Bot 1 buys bot 2's piece for 1,000 with a 5% tax; the player buys bot 2's other piece for 400 (no tax);
            // bot 1 buys the player's listing for 600 (proceeds wait at the broker).
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(3, "tunic", 1, 1_000, "offlinebot:2", "B", "offlinebot:1", "A", H20, 50, true));
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(3, "cap", 1, 400, "offlinebot:2", "B", "char-1", "Me", H20));
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(3, "boots", 1, 600, "char-1", "Me", "offlinebot:1", "A", H20));
            var after = new SupplyBuilder().Bot(1, 3, 50, 8_400).Bot(2, 3, 50, 1_350).Proceeds(3, 600).Wallet(3, 1_600).Build(H21);

            EconomyFlowLedger.Window window = flows.TakeWindow();
            EconomyLedgerSnapshot.RealmHour hibernia = Realm(EconomyLedgerSnapshot.Compute(before, after, window, H21), 3);
            Assert.That(hibernia.DeltaSupply, Is.EqualTo(-50), "only the tax left");
            Assert.That(hibernia.Tax, Is.EqualTo(50));
            Assert.That(hibernia.Flows, Is.EqualTo(-50));
            Assert.That(hibernia.Residual, Is.Zero);
            Assert.That(hibernia.BotResidual, Is.Zero, "the bots' Exchange purchases and proceeds are counted");
            Assert.That(hibernia.SinkKinds["exchangetax"], Is.EqualTo(50));
            string line = EconomyLedgerSnapshot.FlowsLine(hibernia, window);
            Assert.That(line, Does.Contain(" exchange_sales=3 exchange_copper=2000 exchange_tax=50 exchange_crafted=1"));
            Assert.That(line, Does.Contain(" exchange_bots_paid=1600 exchange_bots_received=1350 exchange_players_paid=400 exchange_players_received=600"));
        }

        [Test]
        public void NewAndRemovedBotsBringAndTakeTheirPurse()
        {
            var before = new SupplyBuilder().Bot(1, 1, 50, 1_000).Bot(2, 1, 50, 300).Build(H20);
            // Bot 2 was deleted; bot 3 arrived from the launcher with 10,000 gold.
            var after = new SupplyBuilder().Bot(1, 1, 50, 1_000).Bot(3, 1, 50, 100_000_000).Build(H21);
            EconomyLedgerSnapshot.RealmHour albion = Realm(EconomyLedgerSnapshot.Compute(before, after, new EconomyFlowLedger.Window(null), H21), 1);
            Assert.That(albion.RosterIn, Is.EqualTo(100_000_000));
            Assert.That(albion.RosterOut, Is.EqualTo(300));
            Assert.That(albion.NewBots, Is.EqualTo(1));
            Assert.That(albion.GoneBots, Is.EqualTo(1));
            Assert.That(albion.Residual, Is.Zero);
            Assert.That(albion.BotResidual, Is.Zero);
        }

        [Test]
        public void CoinThatNoFlowRecordsShowsInTheBotsResidual()
        {
            // RealmRaidLootOwner pays raid shares straight into the purses: the ledger sees it only as residual.
            var before = new SupplyBuilder().Bot(1, 2, 50, 0).Build(H20);
            var after = new SupplyBuilder().Bot(1, 2, 50, 777).Build(H21);
            EconomyLedgerSnapshot.RealmHour midgard = Realm(EconomyLedgerSnapshot.Compute(before, after, new EconomyFlowLedger.Window(null), H21), 2);
            Assert.That(midgard.Residual, Is.EqualTo(777));
            Assert.That(midgard.BotResidual, Is.EqualTo(777));
            Assert.That(midgard.UnexplainedBots, Is.EqualTo(1));
            Assert.That(EconomyLedgerSnapshot.SupplyLine(midgard), Does.Contain(" bot_residual=777 ").And.Contain(" bots_unexplained=1 "));
        }

        [Test]
        public void AllSumsTheRealms_AndAnUnknownRealmsFlowsCountOnlyThere()
        {
            var bots = new Dictionary<long, (int, int)> { [1] = (1, 20), [2] = (2, 20) };
            var before = new SupplyBuilder().Bot(1, 1, 20, 100).Bot(2, 2, 20, 100).Build(H20);
            EconomyFlowLedger flows = Ledger(bots);
            flows.Record(1, 10, EconomyFlowKind.LootCoin, "", "");
            flows.Record(2, 20, EconomyFlowKind.LootCoin, "", "");
            flows.Record(99, 5, EconomyFlowKind.LootCoin, "", "");   // a bot the ledger has not placed yet
            var after = new SupplyBuilder().Bot(1, 1, 20, 110).Bot(2, 2, 20, 120).Bot(99, 2, 20, 5).Build(H21);
            var hours = EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), H21);
            Assert.That(hours.Select(hour => EconomyLedgerSnapshot.RealmName(hour.Realm)), Is.EqualTo(new[] { "Albion", "Midgard", "Hibernia", "None", "All" }));
            EconomyLedgerSnapshot.RealmHour all = Realm(hours, EconomyLedgerSnapshot.AllRealms);
            Assert.That(all.DeltaSupply, Is.EqualTo(35));
            Assert.That(all.Flows, Is.EqualTo(35), "30 recorded in the realms + 5 recorded in no realm");
            Assert.That(all.NewBots, Is.EqualTo(1));
            Assert.That(all.RosterIn, Is.Zero, "the new bot brought nothing: its 5 is its recorded loot, not counted twice");
            Assert.That(all.Residual, Is.Zero);
            Assert.That(Realm(hours, 2).Residual, Is.EqualTo(5), "per realm, a move the ledger could not place shows as residual");
            Assert.That(Realm(hours, 0).Flows, Is.EqualTo(5));
        }

        [Test]
        public void TheBaselineHasNoResidual_AndWritesNoRow()
        {
            var now = new SupplyBuilder().Bot(1, 1, 30, 9_000).Wallet(1, 50).Build(H20);
            var hours = EconomyLedgerSnapshot.Compute(null, now, new EconomyFlowLedger.Window(null), H20);
            EconomyLedgerSnapshot.RealmHour albion = Realm(hours, 1);
            Assert.That(albion.Baseline, Is.True);
            string supply = EconomyLedgerSnapshot.SupplyLine(albion);
            Assert.That(supply, Does.StartWith("ECON_SUPPLY hour=2026-11-15T20:00:00Z realm=Albion window=0 baseline=1 supply=9050 bot_coin=9000"));
            Assert.That(supply, Does.Not.Contain("residual"));
            Assert.That(EconomyLedgerSnapshot.FlowsLine(albion, new EconomyFlowLedger.Window(null)), Is.Null);
            Assert.That(EconomyLedgerSnapshot.Rows(hours), Is.Empty);
        }

        [Test]
        public void TheSupplyLineIsCompleteAndInTheLogFormat()
        {
            var before = new SupplyBuilder().Bot(1, 1, 25, 100).Bot(2, 1, 25, 300).Bot(3, 1, 50, 2_000).Build(H20);
            var after = new SupplyBuilder()
                .Bot(1, 1, 25, 100, online: true).Bot(2, 1, 25, 300, online: true).Bot(3, 1, 50, 2_000)
                .Wallet(1, 40).Proceeds(1, 60).Guild(1, 1_000).Listing(1, 500, byBot: true).Listing(1, 250, byBot: false)
                .Crafters(1, 2).Build(H21);
            EconomyLedgerSnapshot.RealmHour albion = Realm(EconomyLedgerSnapshot.Compute(before, after, new EconomyFlowLedger.Window(null), H21), 1);
            Assert.That(EconomyLedgerSnapshot.SupplyLine(albion), Is.EqualTo(
                "ECON_SUPPLY hour=2026-11-15T21:00:00Z realm=Albion window=3600 supply=3500 d_supply=1100 flows=0 residual=1100 " +
                "bot_coin=2400 player_wallets=40 player_proceeds=60 guild_banks=1000 d_bots=0 d_players=100 d_guilds=1000 " +
                "bot_flows=0 bot_residual=0 roster_in=0 roster_out=0 new_bots=0 gone_bots=0 bots_unexplained=0 bots=3 online=2 crafters=2 median=300 " +
                "listed=750 listings=2 bot_listings=1 bots_21_30=2 coin_21_30=400 median_21_30=100 bots_50=1 coin_50=2000 median_50=2000"));
        }

        [Test]
        public void RowsCarryTheHourPerRealm()
        {
            var bots = new Dictionary<long, (int, int)> { [1] = (2, 45) };
            var before = new SupplyBuilder().Bot(1, 2, 45, 1_000).Build(H20);
            EconomyFlowLedger flows = Ledger(bots);
            flows.Record(1, 500, EconomyFlowKind.LootCoin, "", "");
            flows.Record(1, -200, EconomyFlowKind.StableTicket, "", "");
            var after = new SupplyBuilder().Bot(1, 2, 45, 1_300).Wallet(2, 9).Guild(2, 4).Listing(2, 70, true).Build(H21);
            var rows = EconomyLedgerSnapshot.Rows(EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), H21));
            Assert.That(rows.Select(row => row.Realm), Is.EqualTo(new[] { 1, 2, 3 }));
            OfflineEconomyLedgerRecord midgard = rows.Single(row => row.Realm == 2);
            Assert.That(midgard.HourUtc, Is.EqualTo("2026-11-15T21:00:00Z"));
            Assert.That(midgard.BotCoin, Is.EqualTo(1_300));
            Assert.That(midgard.PlayerCoin, Is.EqualTo(9));
            Assert.That(midgard.GuildBanks, Is.EqualTo(4));
            Assert.That(midgard.ListedValue, Is.EqualTo(70));
            Assert.That(midgard.ResidualCopper, Is.EqualTo(13));
            Assert.That(midgard.Bots, Is.EqualTo(1));
            Assert.That(JsonSerializer.Deserialize<Dictionary<string, long>>(midgard.FaucetsJson), Is.EqualTo(new Dictionary<string, long> { ["lootcoin"] = 500 }));
            Assert.That(JsonSerializer.Deserialize<Dictionary<string, long>>(midgard.SinksJson), Is.EqualTo(new Dictionary<string, long> { ["stableticket"] = 200 }));
        }

        [Test]
        public void MedianIsTheLowerMiddle()
        {
            Assert.That(SupplyBuilder.Median(new List<long>()), Is.Zero);
            Assert.That(SupplyBuilder.Median(new List<long> { 5 }), Is.EqualTo(5));
            Assert.That(SupplyBuilder.Median(new List<long> { 9, 1, 5, 3 }), Is.EqualTo(3));
            Assert.That(ServerSupplySource.PendingCopper("1250|0|0"), Is.EqualTo(1250));
            Assert.That(ServerSupplySource.PendingCopper("bad"), Is.Zero);
            Assert.That(EconomyLedger.NextHour(new DateTime(2026, 11, 15, 20, 59, 59, DateTimeKind.Utc)), Is.EqualTo(H21));
            Assert.That(EconomyLedger.NextHour(H21), Is.EqualTo(H21.AddHours(1)));
        }

        // ---- the ledger's hour ----

        private sealed class FakeSource : IEconomySupplySource
        {
            public Func<SupplyBuilder, SupplyBuilder> Supply = builder => builder;
            public bool FailBeforeWindow;
            public readonly List<OfflineEconomyLedgerRecord> Rows = new();

            public void Read(SupplyBuilder builder, Action closeWindow)
            {
                if (FailBeforeWindow)
                    throw new InvalidOperationException("database busy");
                closeWindow();
                Supply(builder);
            }

            public void WriteRows(IReadOnlyList<OfflineEconomyLedgerRecord> rows) => Rows.AddRange(rows);
        }

        private EconomyLedger _ledger;
        private FakeSource _source;
        private List<string> _lines;
        private DateTime _now;
        private bool _enabled;

        [SetUp]
        public void SetUp()
        {
            _now = H20.AddMinutes(30);
            _enabled = true;
            _lines = new List<string>();
            _source = new FakeSource();
            _ledger = EconomyLedger.CreateForTests();
            _ledger.Clock = () => _now;
            _ledger.Write = _lines.Add;
            _ledger.Enabled = () => _enabled;
            _ledger.Source = _source;
        }

        [TearDown]
        public void TearDown()
        {
            _ledger.ResetForTests();
            EconomyFlows.Sink = null;
            EconomySeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
        }

        [Test]
        public void TheLedgerTakesABaselineThenWritesEachUtcHour()
        {
            long purse = 1_000;
            _source.Supply = builder => builder.Bot(1, 1, 30, purse);
            _ledger.Load();
            _ledger.WaitForSnapshots();
            Assert.That(_lines.Count(line => line.StartsWith("ECON_SUPPLY")), Is.EqualTo(4), "three realms and All");
            Assert.That(_lines[0], Does.Contain(" baseline=1 "));
            Assert.That(_ledger.NextHourForTests, Is.EqualTo(H21));
            _lines.Clear();

            // A coin move through WP0's sink; the bot is placed by the baseline snapshot.
            EconomyLedger.Install(_ledger);
            EconomyFlows.Record(1, 250, EconomyFlowKind.Unattributed, @"C:\src\GameBot.cs", "TryAutoPickUpMoney");
            purse = 1_250;
            _ledger.Pulse(1_000);
            _ledger.WaitForSnapshots();
            Assert.That(_lines, Is.Empty, "not before the hour");

            _now = H21.AddSeconds(1);
            _ledger.Pulse(2_000);
            _ledger.WaitForSnapshots();
            string supply = _lines.Single(line => line.StartsWith("ECON_SUPPLY") && line.Contains("realm=Albion"));
            string flows = _lines.Single(line => line.StartsWith("ECON_FLOWS") && line.Contains("realm=Albion"));
            Assert.That(supply, Does.StartWith("ECON_SUPPLY hour=2026-11-15T21:00:00Z realm=Albion window=1801 supply=1250 d_supply=250 flows=250 residual=0"));
            Assert.That(flows, Does.Contain(" faucets=250 sinks=0 net=250"));
            Assert.That(flows, Does.Contain(" in_lootcoin=250/1"));
            Assert.That(_lines.Count(line => line.StartsWith("ECON_FLOWS")), Is.EqualTo(4), "Albion, Midgard, Hibernia, All");
            Assert.That(_source.Rows.Select(row => (row.HourUtc, row.Realm)),
                Is.EqualTo(new[] { ("2026-11-15T21:00:00Z", 1), ("2026-11-15T21:00:00Z", 2), ("2026-11-15T21:00:00Z", 3) }));
            Assert.That(_ledger.NextHourForTests, Is.EqualTo(H21.AddHours(1)));
        }

        [Test]
        public void AFailedReadKeepsTheWindowOpenForTheNextHour()
        {
            _source.Supply = builder => builder.Bot(1, 1, 30, 0);
            _ledger.Load();
            _ledger.WaitForSnapshots();
            _ledger.Flows.Record(1, 100, EconomyFlowKind.LootCoin, "", "");
            _source.FailBeforeWindow = true;
            _now = H21;
            _ledger.Pulse(1_000);
            _ledger.WaitForSnapshots();
            Assert.That(_source.Rows, Is.Empty);

            _source.FailBeforeWindow = false;
            _source.Supply = builder => builder.Bot(1, 1, 30, 100);
            _now = H21.AddHours(1);
            _ledger.Pulse(2_000);
            _ledger.WaitForSnapshots();
            string supply = _lines.Last(line => line.StartsWith("ECON_SUPPLY") && line.Contains("realm=Albion"));
            Assert.That(supply, Does.Contain(" window=5400 "), "two hours' window (from the baseline at 20:30)");
            Assert.That(supply, Does.Contain(" flows=100 residual=0 "), "the first hour's move was kept");
        }

        [Test]
        public void ShutdownLogsThePartialHourWithoutARow()
        {
            _source.Supply = builder => builder.Bot(1, 3, 50, 10);
            _ledger.Load();
            _ledger.WaitForSnapshots();
            _lines.Clear();
            _now = H20.AddMinutes(40);
            _ledger.Flush(wait: false);
            Assert.That(_lines, Is.Empty, "the 5-second flush timer writes nothing");
            _ledger.Flush(wait: true);
            Assert.That(_lines.First(), Does.StartWith("ECON_SUPPLY hour=2026-11-15T20:40:00Z realm=Albion window=600 partial=1 "));
            Assert.That(_lines.Any(line => line.StartsWith("ECON_FLOWS") && line.Contains(" partial=1 ")), Is.True);
            Assert.That(_source.Rows, Is.Empty);
        }

        [Test]
        public void TheSettingOffCountsAndWritesNothing_AndOnAgainStartsANewBaseline()
        {
            _enabled = false;
            _ledger.Load();
            _ledger.WaitForSnapshots();
            _ledger.Flows.Record(1, 100, EconomyFlowKind.LootCoin, "", "");
            _now = H21.AddHours(3);
            _ledger.Pulse(1_000);
            _ledger.WaitForSnapshots();
            Assert.That(_lines, Is.Empty);
            Assert.That(_source.Rows, Is.Empty);
            Assert.That(_ledger.Flows.TakeWindow().IsEmpty(0), Is.True, "nothing was counted");

            _enabled = true;
            _ledger.Pulse(2_000);
            _ledger.WaitForSnapshots();
            Assert.That(_lines.First(), Does.Contain(" baseline=1 "));
        }

        [Test]
        public void InstallWiresTheWp0Seams()
        {
            EconomyLedger.Install(_ledger);
            EconomyLedger.Install(_ledger);
            Assert.That(EconomyFlows.Sink, Is.SameAs(_ledger.Flows));
            Assert.That(EconomyLifecycle.Count, Is.EqualTo(1));
            _ledger.StartForTests(_now);
            EconomySeams.RaiseSaleCommitted(new RealmExchangeSaleLedger.Sale(1, "tunic", 1, 400, "offlinebot:1", "A", "offlinebot:2", "B", _now));
            EconomyFlowLedger.Window window = _ledger.Flows.TakeWindow();
            Assert.That(window.Sales(1), Is.EqualTo(1), "subscribed once, however often installed");
            Assert.That(window.SalesCopper(1), Is.EqualTo(400));
        }
    }
}
