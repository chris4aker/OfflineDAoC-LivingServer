using System;
using System.Linq;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Crafting 2.0 WP6 in the economy rehearsal (WP10): the set score as the upgrade rule, the gear check and the gap
    /// purchases, with every gate still passing and no set score falling. Rehearsal results are tests, not game results.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_WP6Rehearsal
    {
        [Test]
        public void TheModuleRunsOnlyWithBotCrafters()
        {
            Assert.That(RehearsalModules.For(RehearsalScenario.Today()).Select(m => m.Id), Does.Not.Contain(WP6Demand.ModuleId));
            Assert.That(RehearsalModules.For(RehearsalScenario.FreshStart()).Select(m => m.Id), Does.Contain(WP6Demand.ModuleId));
        }

        [Test]
        public void ADayWithWP6_KeepsEveryGateAndNoSetScoreFalls()
        {
            RehearsalReport report = EconomyRehearsal.Run(
                RehearsalScenario.FreshStart(realms: 1, bots: 200, minutes: RehearsalScenario.Day), build: "wp6");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine,
                report.Metrics.Where(m => m.Key.StartsWith("wp6.")).Select(m => $"{m.Key}={m.Value}")));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Gates.Select(g => $"{g.Name}: {g.Status} {g.Detail}")));
            Assert.That(report.Modules.Select(m => m.Id), Does.Contain(WP6Demand.ModuleId));
            Assert.That(report.GatesPass, string.Join("; ", report.Gates.Select(g => $"{g.Name}: {g.Status} {g.Detail}")));
            Assert.That(report.Metrics["wp6.setScoreDrops"], Is.EqualTo("0"));
            Assert.That(long.Parse(report.Metrics["wp6.checks"]), Is.GreaterThan(0));
            Assert.That(long.Parse(report.Metrics["wp6.withGaps"]), Is.GreaterThan(0), "fresh bots start without armour");
        }
    }
}
