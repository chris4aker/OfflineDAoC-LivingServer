using System;
using System.Linq;
using DOL.GS.Commands;
using DOL.GS.CraftingEconomy;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Crafting 2.0 WP5 in WP10's rehearsal (small days, the plan's gates) and its /lscraft parts. Tests only: a rehearsal
    /// is not an in-game result.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_MarketRehearsal
    {
        /// <summary>An aged world (crafters with skill already), one realm, a day.</summary>
        private static RehearsalScenario Day(RehearsalScenario scenario) =>
            scenario with { Realms = 1, BotsPerRealm = 300, Minutes = RehearsalScenario.Day, Seed = 11 };

        [TearDown]
        public void TearDown()
        {
            MarketSeams.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
        }

        [Test]
        public void TheMarketRunsOnlyWithCraftersOrATax()
        {
            Assert.That(RehearsalModules.For(RehearsalScenario.Today()).Select(m => m.Id), Does.Not.Contain(WP5Market.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.AgedLegacy()).Select(m => m.Id), Does.Contain(WP5Market.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.Today().With("exchange_tax_percent", "5")).Select(m => m.Id), Does.Contain(WP5Market.ModuleId));
        }

        [Test]
        public void ADayOfDoubledCommissionsPassesTheGatesAndPaysAFaucet()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Commissions("doubled")), build: "smoke");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Gates.Select(g => $"{g.Name}: {g.Status} ({g.Detail})")));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Metrics.Where(m => m.Key.StartsWith("wp5.")).Select(m => $"{m.Key}={m.Value}")));
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.NoNegativePurse).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.BidBelowAsk).Status, Is.Not.EqualTo(GateStatus.Fail), "the index keeps every bid below its asks");
            Assert.That(int.Parse(report.Metrics["wp5.commissionsPosted"]), Is.GreaterThan(0), "an aged world has crafters above 50");
            long commission = report.Rows.Sum(row => row.Flows.TryGetValue(EconomyFlowKind.Commission, out FlowTotals t) ? t.In - t.Out : 0);
            Assert.That(commission, Is.EqualTo(long.Parse(report.Metrics["wp5.commissionPaidCopper"])), "the realm's pay is a Commission faucet");
            Assert.That(report.NotBuilt, Has.None.StartsWith("realm_commissions"));
        }

        [Test]
        public void OffMeansNoCommissions()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Commissions("off")) with { Minutes = 360 }, build: "smoke");
            Assert.That(report.Metrics["wp5.commissionsPosted"], Is.EqualTo("0"));
            Assert.That(report.Rows.Any(row => row.Flows.ContainsKey(EconomyFlowKind.Commission)), Is.False);
        }

        [Test]
        public void AFivePercentTaxIsASinkFromTheSellersProceeds()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Tax(5)) with { Minutes = 600 }, build: "smoke");
            long tax = report.Rows.Sum(row => row.Flows.TryGetValue(EconomyFlowKind.ExchangeTax, out FlowTotals t) ? t.Out : 0);
            long sales = report.Rows.Sum(row => row.Flows.TryGetValue(EconomyFlowKind.ExchangeSale, out FlowTotals t) ? t.In : 0);
            Assert.That(report.Metrics["wp5.taxPercent"], Is.EqualTo("5"));
            Assert.That(tax, Is.GreaterThan(0));
            Assert.That(tax, Is.EqualTo(sales * 5 / 95).Within(Math.Max(5, sales / 1000)), "about 5% of what sellers took");
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
        }

        [Test]
        public void TheLscraftPartsAreRegistered()
        {
            CraftEconomyTestHooks.ResetForTests();
            MarketSeams.RegisterForTests();
            Assert.That(CraftEconomyTestHooks.TryGet("commission", out CraftEconomyTestHooks.Handler commission), Is.True);
            Assert.That(commission(null, ["commission"]).First(), Does.StartWith("Realm commissions: "));
            Assert.That(CraftEconomyTestHooks.TryGet("index", out CraftEconomyTestHooks.Handler index), Is.True);
            Assert.That(index(null, ["index"]).Single(), Does.StartWith("Put an item"));
            MarketSeams.ResetForTests();
            Assert.That(CraftEconomyTestHooks.TryGet("commission", out _), Is.False);
        }
    }
}
