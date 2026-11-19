using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.CraftingEconomy;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Crafting 2.0 WP7 in WP10's rehearsal: the sinks on WP7's pure code, the plan's gates, and the governor. Tests only:
    /// a rehearsal is not an in-game result.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_SinksRehearsal
    {
        /// <summary>An aged realm of 300 bots for a day, crafters on (the plan's upkeep scenario at small scale).</summary>
        private static RehearsalScenario Day(RehearsalScenario scenario) =>
            scenario with { Realms = 1, BotsPerRealm = 300, Minutes = RehearsalScenario.Day, Seed = 17 };

        private static RehearsalScenario AllSinks() =>
            RehearsalScenario.Upkeep(true).With("bot_luxury_spending", "True").With("bot_siege_supply", "True").Named("sinks-on");

        [TearDown]
        public void TearDown()
        {
            MarketSeams.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
        }

        private static long Flow(RehearsalReport report, EconomyFlowKind kind) =>
            report.Rows.Sum(row => row.Flows.TryGetValue(kind, out FlowTotals t) ? t.Out - t.In : 0);

        private static void Print(RehearsalReport report)
        {
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Gates.Select(g => $"{g.Name}: {g.Status} ({g.Detail})")));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Metrics.Where(m => m.Key.StartsWith("wp7.") ||
                m.Key is "supplyStartCopper" or "supplyEndCopper" or "faucetsCopper" or "sinksCopper").Select(m => $"{m.Key}={m.Value}")));
        }

        [Test]
        public void TheSinksRunOnlyWithCraftersAndASwitch()
        {
            Assert.That(RehearsalModules.For(RehearsalScenario.Today()).Select(m => m.Id), Does.Not.Contain(WP7Sinks.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.AgedLegacy()).Select(m => m.Id), Does.Contain(WP7Sinks.ModuleId),
                "every switch at its default: crafters and every sink on (owner 2026-11-16)");
            Assert.That(RehearsalModules.For(RehearsalScenario.Upkeep(false)).Select(m => m.Id), Does.Not.Contain(WP7Sinks.ModuleId),
                "crafters on, every sink off");
            Assert.That(RehearsalModules.For(RehearsalScenario.Upkeep(true)).Select(m => m.Id), Does.Contain(WP7Sinks.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.Today().With("bot_upkeep", "True")).Select(m => m.Id), Does.Not.Contain(WP7Sinks.ModuleId),
                "bot_upkeep needs bot_crafters");
        }

        [Test]
        public void ADayWithEverySinkOnPassesTheGatesAndNamesEveryKind()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(AllSinks()), build: "smoke");
            Print(report);
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.NoNegativePurse).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.BoundedSupplyGrowth).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.NoUndress).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.NotBuilt, Has.None.StartsWith("bot_upkeep").And.None.StartsWith("bot_luxury_spending").And.None.StartsWith("bot_siege_supply"));
            Assert.That(long.Parse(report.Metrics["wp7.wearEvents"]), Is.GreaterThan(0), "gear wears");
            Assert.That(Flow(report, EconomyFlowKind.Potion), Is.EqualTo(long.Parse(report.Metrics["wp7.draughtCopper"])), "draughts are a Potion sink");
            Assert.That(Flow(report, EconomyFlowKind.Poison), Is.EqualTo(long.Parse(report.Metrics["wp7.vialCopper"])), "vials are a Poison sink");
            Assert.That(Flow(report, EconomyFlowKind.Repair), Is.EqualTo(long.Parse(report.Metrics["wp7.repairCopper"])), "repairs are a Repair sink");
            Assert.That(Flow(report, EconomyFlowKind.Luxury), Is.EqualTo(long.Parse(report.Metrics["wp7.luxuryCopper"])), "luxury is a Luxury sink");
            Assert.That(long.Parse(report.Metrics["wp7.vialCopper"]), Is.GreaterThan(0), "assassins pay for their coats");
            Assert.That(report.Metrics["wp7.siegeSupply"], Does.StartWith("not rehearsed"));
        }

        [Test]
        public void WithTodaysWideBandTheGovernorHoldsLuxuryBack()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(AllSinks()) with { Minutes = 12 * 60 }, build: "smoke");
            Print(report);
            double ratioMax = report.Rows.Where(r => r.Metrics.ContainsKey("wp7.growthRatio")).Select(r => r.Metrics["wp7.growthRatio"]).DefaultIfEmpty(0).Max();
            Assert.That(ratioMax, Is.LessThan(SinkGovernor.Target), "an aged world grows well inside the gates' band");
            Assert.That(double.Parse(report.Metrics["wp7.luxuryIntensityEnd"], System.Globalization.CultureInfo.InvariantCulture), Is.Zero,
                "inside the band's target the governor spends nothing on luxury");
            Assert.That(report.Metrics["wp7.luxuryCopper"], Is.EqualTo("0"), "no luxury before or after the governor's first hour");
        }

        [Test]
        public void WithATightBandTheGovernorSpendsTheSurplusAndSlowsTheGrowth()
        {
            RehearsalScenario scenario = Day(RehearsalScenario.Upkeep(true).With("bot_luxury_spending", "True"));
            var tight = new SinkGovernor.Band(2, 0.5);
            IEnumerable<IRehearsalModule> Modules(SinkGovernor.Band band) =>
                RehearsalModules.CreateAll().Select(m => m is WP7Sinks ? new WP7Sinks { Band = band } : m);
            RehearsalReport wide = EconomyRehearsal.Run(scenario, Modules(SinkGovernor.Gates), build: "smoke");
            RehearsalReport governed = EconomyRehearsal.Run(scenario, Modules(tight), build: "smoke");
            Print(governed);
            Assert.That(long.Parse(governed.Metrics["wp7.luxuryCopper"]), Is.GreaterThan(long.Parse(wide.Metrics["wp7.luxuryCopper"])));
            Assert.That(governed.EndSupply.Sum() - governed.StartSupply.Sum(), Is.LessThan(wide.EndSupply.Sum() - wide.StartSupply.Sum()),
                "the luxury sink slows the growth");
            Assert.That(governed.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass), "every luxury move is in the ledger");
            Assert.That(governed.Gate(EconomyRehearsal.NoNegativePurse).Status, Is.EqualTo(GateStatus.Pass));
        }

        [Test]
        public void TheOrderWaitGateIsTheSameWithTheSinksOff()
        {
            // WP5's order wait is not WP7's: the same day with every WP7 switch off, for comparison (printed).
            RehearsalReport off = EconomyRehearsal.Run(Day(RehearsalScenario.AgedLegacy()), build: "smoke");
            TestContext.Out.WriteLine("sinks off: " + off.Gate(EconomyRehearsal.OrderWait).Detail);
            RehearsalReport on = EconomyRehearsal.Run(Day(AllSinks()), build: "smoke");
            TestContext.Out.WriteLine("sinks on: " + on.Gate(EconomyRehearsal.OrderWait).Detail);
            Assert.Pass();
        }

        [Test]
        public void UpkeepOffMeansNoSinkOfWP7()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Upkeep(false)) with { Minutes = 360 }, build: "smoke");
            foreach (EconomyFlowKind kind in new[] { EconomyFlowKind.Repair, EconomyFlowKind.Potion, EconomyFlowKind.Poison, EconomyFlowKind.Luxury })
                Assert.That(report.Rows.Any(row => row.Flows.ContainsKey(kind)), Is.False, kind.ToString());
        }

        /// <summary>The plan's upkeep scenario at full size (3 realms, 1,000 bots each, a week). Explicit: minutes to run.</summary>
        [Test, Explicit("a week of 3,000 bots")]
        public void AWeekOfEverySinkOn()
        {
            RehearsalReport off = EconomyRehearsal.Run(RehearsalScenario.Upkeep(false), build: "explicit");
            TestContext.Out.WriteLine("---- upkeep-off (the same aged world, crafters on, every WP7 switch off)");
            Print(off);
            RehearsalReport report = EconomyRehearsal.Run(AllSinks(), build: "explicit");
            TestContext.Out.WriteLine("---- sinks-on (bot_upkeep, bot_luxury_spending, bot_siege_supply)");
            Print(report);
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.BoundedSupplyGrowth).Status, Is.EqualTo(GateStatus.Pass));
        }
    }
}
