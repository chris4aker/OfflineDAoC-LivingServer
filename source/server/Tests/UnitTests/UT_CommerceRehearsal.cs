using System;
using System.Linq;
using DOL.GS.CraftingEconomy;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Crafting 2.0 WP8 in WP10's rehearsal: a day of player commissions keeps the ledger's gates. Tests only.</summary>
    [TestFixture, NonParallelizable]
    public class UT_CommerceRehearsal
    {
        [TearDown]
        public void TearDown()
        {
            MarketSeams.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
        }

        [Test]
        public void CommerceRunsOnlyWithBothSwitches()
        {
            Assert.That(RehearsalModules.For(RehearsalScenario.AgedLegacy()).Select(m => m.Id), Does.Not.Contain(WP8Commerce.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.Today().With("bot_crafter_commerce", "True")).Select(m => m.Id),
                Does.Not.Contain(WP8Commerce.ModuleId), "needs bot_crafters");
            Assert.That(RehearsalModules.For(RehearsalScenario.AgedLegacy().With("bot_crafter_commerce", "True")).Select(m => m.Id),
                Does.Contain(WP8Commerce.ModuleId));
        }

        [Test]
        public void ADayOfPlayerCommissionsKeepsTheResidualAtZero()
        {
            RehearsalScenario scenario = RehearsalScenario.AgedLegacy().With("bot_crafter_commerce", "True").Named("commerce") with
            {
                Realms = 1, BotsPerRealm = 300, Minutes = RehearsalScenario.Day, Seed = 8,
            };
            RehearsalReport report = EconomyRehearsal.Run(scenario, build: "smoke");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Metrics.Where(m => m.Key.StartsWith("wp8.")).Select(m => $"{m.Key}={m.Value}")));
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.NoNegativePurse).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(int.Parse(report.Metrics["wp8.playerOrdersPlaced"]), Is.GreaterThan(0), "an aged world's crafters take orders");
            Assert.That(int.Parse(report.Metrics["wp8.playerOrdersDelivered"]), Is.GreaterThan(0));
            Assert.That(report.NotBuilt, Has.None.StartsWith("bot_crafter_commerce"));
            // The full bot trade: players sell gamebots pieces for empty slots and buy loot from their bags, at the index.
            Assert.That(int.Parse(report.Metrics["wp8.tradeSoldToBots"]), Is.GreaterThan(0));
            Assert.That(int.Parse(report.Metrics["wp8.tradeBoughtFromBags"]), Is.GreaterThan(0));
        }
    }
}
