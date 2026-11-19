using System;
using System.Linq;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>
/// True165's BOT_MONEY_LEDGER_165 test (shadowofze, a8c793e and df86f9f, via True165, GPL-3.0), adapted to the port
/// (Crafting 2.0 WP2): the sums are EconomyFlowLedger's, per realm, fed through WP0's flow sink, and the hourly line is
/// ECON_FLOWS with True165's in_&lt;kind&gt;=copper/count keys. The hour itself is the ledger's (UT_EconomyLedgerSnapshot).
/// </summary>
[TestFixture, NonParallelizable]
public class UT_BotMoneyLedger165
{
    private EconomyFlowLedger _ledger;

    [SetUp]
    public void SetUp()
    {
        _ledger = new EconomyFlowLedger { BotInfo = _ => (1, 25) };
    }

    [TearDown]
    public void TearDown() => EconomyFlows.Sink = null;

    private static string FlowsLine(EconomyFlowLedger.Window window, int realm = 1)
    {
        var before = new SupplyBuilder().Build(new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc));
        var after = new SupplyBuilder().Build(new DateTime(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc));
        var hours = EconomyLedgerSnapshot.Compute(before, after, window, after.TakenUtc);
        return EconomyLedgerSnapshot.FlowsLine(hours.Single(hour => hour.Realm == realm), window);
    }

    [Test]
    public void LedgerSumsKindsAndTotalsIntoTheHourlyLine()
    {
        _ledger.Record(1, 300, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(1, 200, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(1, -120, EconomyFlowKind.Repair, "", "");
        _ledger.Record(1, -80, EconomyFlowKind.Healer, "", "");
        _ledger.Record(1, 40, EconomyFlowKind.VendorSale, "", "");
        _ledger.Record(1, -10, EconomyFlowKind.Dye, "", "");

        string line = FlowsLine(_ledger.TakeWindow());
        Assert.That(line, Does.StartWith("ECON_FLOWS hour=2026-11-15T21:00:00Z realm=Albion window=3600 faucets=540 sinks=210 net=330"));
        Assert.That(line, Does.Contain(" in_lootcoin=500/2"));
        Assert.That(line, Does.Contain(" out_repair=120/1"));
        Assert.That(line, Does.Contain(" out_healer=80/1"));
        Assert.That(line, Does.Contain(" in_vendorsale=40/1"));
        Assert.That(line, Does.Contain(" out_dye=10/1"));
        Assert.That(line, Does.Contain(" moves=6"));
        Assert.That(line, Does.Contain(" earned_21_30=540 net_21_30=330"), "the level band of the bot");
    }

    [Test]
    public void True165sOwnLineIsKeptAsAnAlias()
    {
        _ledger.Record(1, 300, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(1, 200, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(1, -120, EconomyFlowKind.Repair, "", "");
        _ledger.Record(1, -80, EconomyFlowKind.Healer, "", "");
        _ledger.Record(1, 40, EconomyFlowKind.VendorSale, "", "");
        _ledger.Record(1, -10, EconomyFlowKind.Dye, "", "");
        EconomyLedgerSnapshot.RealmHour all = AllHour(_ledger.TakeWindow(), out EconomyFlowLedger.Window window);
        string line = EconomyLedgerSnapshot.True165Line(all, window);
        Assert.That(line, Does.StartWith("BOT_MONEY_LEDGER_165 window=3600s in_total=540 out_total=210 net=330"));
        Assert.That(line, Does.Contain(" in_lootcoin=500/2"));
        Assert.That(line, Does.Contain(" out_repair=120/1"));
        Assert.That(line, Does.Contain(" out_dye=10/1"));
        Assert.That(EconomyLedgerSnapshot.True165Line(AllHour(_ledger.TakeWindow(), out window), window), Is.Null, "an idle hour writes nothing");
    }

    private static EconomyLedgerSnapshot.RealmHour AllHour(EconomyFlowLedger.Window taken, out EconomyFlowLedger.Window window)
    {
        window = taken;
        var before = new SupplyBuilder().Build(new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc));
        var after = new SupplyBuilder().Build(new DateTime(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc));
        return EconomyLedgerSnapshot.Compute(before, after, window, after.TakenUtc).Single(hour => hour.Realm == EconomyLedgerSnapshot.AllRealms);
    }

    [Test]
    public void LedgerIgnoresZeroAmountsAndAnIdleHourHasNoKinds()
    {
        _ledger.Record(1, 0, EconomyFlowKind.LootCoin, "", "");
        string line = FlowsLine(_ledger.TakeWindow());
        Assert.That(line, Does.Contain(" faucets=0 sinks=0 net=0"));
        Assert.That(line, Does.Not.Contain(" in_"));
        Assert.That(line, Does.Not.Contain(" out_"));
    }

    [Test]
    public void KillTaskRewardsHaveTheirOwnKind()
    {
        _ledger.Record(1, 120, EconomyFlowKind.KillTask, "", "");
        Assert.That(FlowsLine(_ledger.TakeWindow()), Does.Contain(" in_killtask=120/1"), "not mixed into Unattributed");
    }

    [Test]
    public void TakingTheWindowResetsTheSums()
    {
        _ledger.Record(1, 75, EconomyFlowKind.CraftSale, "", "");
        Assert.That(FlowsLine(_ledger.TakeWindow()), Does.Contain(" in_craftsale=75/1"));
        Assert.That(FlowsLine(_ledger.TakeWindow()), Does.Not.Contain("craftsale"));
    }

    [Test]
    public void ARefundInTheSameMemberCancelsInTheKindsNet()
    {
        // TrySpend then AddMoney when the ticket could not be used (True165 filed both under Travel; ours is StableTicket).
        _ledger.Record(1, -500, EconomyFlowKind.Unattributed, @"C:\src\GameBot.cs", "ContinueReturnJourney");
        _ledger.Record(1, 500, EconomyFlowKind.Unattributed, @"C:\src\GameBot.cs", "ContinueReturnJourney");
        EconomyFlowLedger.Window window = _ledger.TakeWindow();
        Assert.That(window.Net(1, EconomyFlowKind.StableTicket), Is.Zero);
        string line = FlowsLine(window);
        Assert.That(line, Does.Contain(" faucets=0 sinks=0 net=0"));
        Assert.That(line, Does.Contain(" in_stableticket=500/1 out_stableticket=500/1"), "both moves still shown");
    }

    [Test]
    public void FlowsArriveThroughTheWp0SinkWithTheirCaller()
    {
        EconomyFlows.Sink = _ledger;
        EconomyFlows.Record(7, 250, EconomyFlowKind.Unattributed, "/build/GameServer/bots/GameBot.cs", "TryAutoPickUpMoney");
        EconomyFlows.Record(7, -90, EconomyFlowKind.Unattributed, "/build/bots/autonomous/AutonomousBotCosmetics.cs", "TryRestyle");
        EconomyFlowLedger.Window window = _ledger.TakeWindow();
        Assert.That(window.In(1, EconomyFlowKind.LootCoin), Is.EqualTo(250));
        Assert.That(window.Out(1, EconomyFlowKind.Dye), Is.EqualTo(90));
    }

    [Test]
    public void SumsArePerRealm_AndAnUnknownBotIsCountedApart()
    {
        _ledger.BotInfo = id => id switch { 1 => (1, 10), 2 => (3, 50), _ => (0, 0) };
        _ledger.Record(1, 100, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(2, 200, EconomyFlowKind.LootCoin, "", "");
        _ledger.Record(9, 300, EconomyFlowKind.LootCoin, "", "");
        EconomyFlowLedger.Window window = _ledger.TakeWindow();
        Assert.That(window.In(1, EconomyFlowKind.LootCoin), Is.EqualTo(100));
        Assert.That(window.In(3, EconomyFlowKind.LootCoin), Is.EqualTo(200));
        Assert.That(window.In(0, EconomyFlowKind.LootCoin), Is.EqualTo(300));
        Assert.That(window.BandIn(3, EconomyFlowLedger.BandOf(50)), Is.EqualTo(200));
    }

    [Test]
    public void ADisabledLedgerCountsNothing()
    {
        _ledger.Enabled = () => false;
        _ledger.Record(1, 100, EconomyFlowKind.LootCoin, "", "");
        _ledger.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(1, "tunic", 1, 400, "offlinebot:1", "A", "offlinebot:2", "B", DateTime.UtcNow));
        EconomyFlowLedger.Window window = _ledger.TakeWindow();
        Assert.That(window.IsEmpty(1), Is.True);
    }

    [Test]
    public void ParallelMovesAreAllCounted()
    {
        System.Threading.Tasks.Parallel.For(0, 10_000, i => _ledger.Record(1, i % 2 == 0 ? 3 : -1, EconomyFlowKind.LootCoin, "", ""));
        EconomyFlowLedger.Window window = _ledger.TakeWindow();
        Assert.That(window.In(1, EconomyFlowKind.LootCoin), Is.EqualTo(15_000));
        Assert.That(window.Out(1, EconomyFlowKind.LootCoin), Is.EqualTo(5_000));
        Assert.That(window.InCount(1, EconomyFlowKind.LootCoin) + window.OutCount(1, EconomyFlowKind.LootCoin), Is.EqualTo(10_000));
    }
}
