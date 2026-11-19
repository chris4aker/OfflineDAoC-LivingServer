using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS.CraftingEconomy;
using DOL.GS.Tests.Rehearsal;
using DOL.GS.Tests.Rehearsal.Adapters;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The economy rehearsal's CI check (Crafting 2.0 WP10): one simulated day, small, with the plan's gates. The week
    /// scenarios are UT_EconomyRehearsalWeek ([Explicit]). Tests only: a rehearsal is not an in-game result.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EconomyRehearsalSmoke
    {
        private static RehearsalScenario Day(RehearsalScenario scenario) =>
            scenario with { Realms = 3, BotsPerRealm = 120, Minutes = RehearsalScenario.Day, Seed = 7 };

        [Test]
        public void TheFixturesLoad()
        {
            RehearsalRecipes recipes = RehearsalFixtures.Recipes;
            Assert.That(recipes.All, Has.Count.EqualTo(8_479), "the 0.37b world's CraftedItem rows (M0 1.1)");
            Assert.That(recipes.HiddenCount, Is.EqualTo(40), "M0 1.2: 2 without ingredients, 38 statless products");
            Assert.That(recipes.All.Count(r => r.Hidden == "NoIngredients"), Is.EqualTo(2));
            Assert.That(recipes.For(DOL.GS.eCraftingSkill.WeaponCrafting, eRealm.Albion), Is.Not.Empty);
            Assert.That(recipes.MerchantGearPriceByLevel, Is.Not.Empty);

            RehearsalCalibration calibration = RehearsalFixtures.Calibration;
            Assert.That(calibration.Bands, Has.Count.EqualTo(5));
            Assert.That(calibration.KillsPerHour(25, grouped: false), Is.EqualTo(34.7));
            Assert.That(calibration.KillsPerHour(50, grouped: true), Is.EqualTo(25.4));
            // M0 4: mean coin per kill at monster level 25 is about 27 silver (2,700 copper).
            Assert.That(calibration.CoinPerKillBase(25) * 1.25, Is.EqualTo(2_700).Within(100));
        }

        [Test]
        public void ModulesAreFoundByReflectionAndChosenByScenario()
        {
            Assert.That(RehearsalModules.Discover(), Does.Contain(typeof(WP0CraftRulesStandIn)));
            Assert.That(RehearsalModules.For(RehearsalScenario.Today()).Select(m => m.Id), Does.Not.Contain(WP0CraftRulesStandIn.StandInId),
                "bot_crafters is off today");
            Assert.That(RehearsalModules.For(RehearsalScenario.FreshStart()).Select(m => m.Id), Does.Contain(WP0CraftRulesStandIn.StandInId));

            // A package's real code replaces a stand-in by naming it; the order is by package number.
            var replacement = new FakeModule("WP4.Crafting", "WP4", supersedes: WP0CraftRulesStandIn.StandInId);
            var ledger = new FakeModule("WP2.Ledger", "WP2");
            IReadOnlyList<IRehearsalModule> chosen = RehearsalModules.For(RehearsalScenario.FreshStart(),
                new IRehearsalModule[] { replacement, new WP0CraftRulesStandIn(), ledger });
            Assert.That(chosen.Select(m => m.Id), Is.EqualTo(new[] { "WP2.Ledger", "WP4.Crafting" }));
            Assert.That(RehearsalModules.PackageNumber("WP10"), Is.EqualTo(10));
        }

        [Test]
        public void ADayOfTodaysEconomyPassesTheGates()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Today()), build: "smoke");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Gates.Select(g => $"{g.Name}: {g.Status} ({g.Detail})")));
            Assert.That(report.Rows, Has.Count.EqualTo(24 * 3));
            Assert.That(report.Rows.All(row => row.ResidualCopper == 0), "the flows equal the change in supply, every hour");
            Assert.That(report.Gate(EconomyRehearsal.NoNegativePurse).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.BidBelowAsk).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.BoundedSupplyGrowth).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.NoUndress).Status, Is.EqualTo(GateStatus.Pass));
            Assert.That(report.Gate(EconomyRehearsal.OrderWait).Status, Is.EqualTo(GateStatus.NotApplicable), "no order board before WP5");
            Assert.That(report.Modules, Is.Empty);
            Assert.That(report.Counters["task_breaks"], Is.GreaterThan(1_000));
            Assert.That(report.Counters.GetValueOrDefault("listed"), Is.GreaterThan(0));
            Assert.That(report.Counters.GetValueOrDefault("exchange_sales"), Is.GreaterThan(0));
            Assert.That(report.EndSupply.Sum(), Is.GreaterThan(report.StartSupply.Sum()), "hunting is a faucet");
            Assert.That(report.Elapsed, Is.LessThan(TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void ARunRepeatsWithItsSeed()
        {
            RehearsalReport first = EconomyRehearsal.Run(Day(RehearsalScenario.FreshStart()) with { BotsPerRealm = 60 }, build: "smoke");
            RehearsalReport second = EconomyRehearsal.Run(Day(RehearsalScenario.FreshStart()) with { BotsPerRealm = 60 }, build: "smoke");
            Assert.That(second.EndSupply, Is.EqualTo(first.EndSupply));
            Assert.That(second.Lines.Where(l => !l.StartsWith(EconomyLog.EconRehearsal)), Is.EqualTo(first.Lines.Where(l => !l.StartsWith(EconomyLog.EconRehearsal))));
        }

        [Test]
        public void ADayWithTheCrafterStandInCraftsByTheRules()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.FreshStart()), build: "smoke");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, report.Metrics.Where(m => m.Key.StartsWith("wp0.")).Select(m => $"{m.Key}={m.Value}")));
            // WP5's market and WP6's demand modules run beside the stand-in when bot_crafters is on.
            Assert.That(report.Modules.Select(m => m.Id), Does.Contain(WP0CraftRulesStandIn.StandInId)); // WP6: other packages' modules run too
            Assert.That(report.GatesPass, string.Join("; ", report.Gates.Select(g => $"{g.Name}: {g.Status} {g.Detail}")));
            int crafters = int.Parse(report.Metrics["crafters"]);
            Assert.That(crafters, Is.InRange(3 * 120 * 3 / 100, 3 * 120 * 12 / 100), "about 7% of new bots");
            Assert.That(int.Parse(report.Metrics["wp0.sessions"]), Is.GreaterThan(0));
            Assert.That(int.Parse(report.Metrics["wp0.points"]), Is.GreaterThan(0));
            Assert.That(report.Lines.Any(l => l.StartsWith(EconomyLog.CraftSessionStart + " ")));
            Assert.That(report.Lines.Any(l => l.StartsWith(EconomyLog.CraftSessionEnd + " ")));
            Assert.That(report.NotBuilt, Has.None.StartsWith("bot_crafters"), "bot_crafters is read by the stand-in");
            // Every gamebot switch is on by default (owner 2026-11-16); the rehearsal has no module for WP8b's crafting social
            // life, and says so.
            Assert.That(report.NotBuilt, Is.EqualTo(new[] { "bot_crafting_social=True (WP8: on, but no rehearsal module plays it; the run is without it)" }));
        }

        [Test]
        public void EveryCoinMoveReachesTheFlowObservers()
        {
            var sink = new SumSink();
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Today()) with { Realms = 1, BotsPerRealm = 80 },
                build: "smoke", configure: world => world.FlowObservers.Add(sink));
            Assert.That(sink.Sum, Is.EqualTo(report.EndSupply.Sum() - report.StartSupply.Sum()));
            Assert.That(sink.Kinds, Does.Contain(EconomyFlowKind.LootCoin));
            Assert.That(sink.Kinds, Does.Contain(EconomyFlowKind.VendorSale));
        }

        [Test]
        public void ATaxThroughTheProceedsHookIsASinkAndTheBooksStillBalance()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.Today()) with { Realms = 1, BotsPerRealm = 200 }, build: "smoke",
                configure: world => world.SellerProceeds = (price, _, _, _) => price * 95 / 100);
            long tax = report.Rows.Sum(row => row.Flows.TryGetValue(EconomyFlowKind.ExchangeTax, out FlowTotals t) ? t.Out : 0);
            long sales = report.Rows.Sum(row => row.Flows.TryGetValue(EconomyFlowKind.ExchangeSale, out FlowTotals t) ? t.In - t.Out : 0);
            Assert.That(tax, Is.GreaterThan(0));
            Assert.That(sales, Is.EqualTo(0), "the transfer itself nets to zero; the tax is its own sink");
            Assert.That(report.Gate(EconomyRehearsal.FlowsEqualSupply).Status, Is.EqualTo(GateStatus.Pass));
        }

        [Test]
        public void ASettingNoModuleReadsIsReportedAsNotBuiltYet()
        {
            // WP5 reads exchange_tax_percent and WP7 bot_upkeep now (their adapters); WP9's launcher_level50_kit is read by no module.
            RehearsalScenario scenario = Day(RehearsalScenario.Upkeep(true).With("launcher_level50_kit", "False")) with { Realms = 1, BotsPerRealm = 20, Minutes = 120 };
            RehearsalReport report = EconomyRehearsal.Run(scenario, build: "smoke");
            Assert.That(report.NotBuilt, Has.Some.StartsWith("launcher_level50_kit=False (WP9"));
            Assert.That(report.NotBuilt, Has.None.StartsWith("bot_upkeep"));
            Assert.That(report.NotBuilt, Has.None.StartsWith("exchange_tax_percent"));
            Assert.That(RehearsalScenario.PlanScenarios().Select(s => s.Name).Distinct().Count(), Is.EqualTo(10));
        }

        [Test]
        public void TheLedgerIsWrittenInTheServersLineFormat()
        {
            RehearsalReport report = EconomyRehearsal.Run(Day(RehearsalScenario.FreshStart()) with { Realms = 2, BotsPerRealm = 50 }, build: "smoke");
            string folder = Path.Combine(Path.GetTempPath(), "econ-rehearsal-" + Guid.NewGuid().ToString("N"));
            try
            {
                string[] files = RehearsalLedgerWriter.Write(report, folder);
                Assert.That(files.Select(Path.GetFileName), Is.EqualTo(new[] { "fresh-7pct-ledger.md", "fresh-7pct-ledger.csv", "fresh-7pct-ledger.log" }));
                string[] csv = File.ReadAllLines(files[1]);
                Assert.That(csv, Has.Length.EqualTo(1 + 24 * 2));
                Assert.That(csv[0].Split(','), Is.EqualTo(RehearsalLedgerWriter.CsvColumns));
                Assert.That(csv.Skip(1).All(line => line.StartsWith(EconomyLog.EconSupply + ",")));
                string[] log = File.ReadAllLines(files[2]);
                Assert.That(log.Select(line => line.Split(' ')[0]).Distinct(), Is.SubsetOf(EconomyLog.AllTags));
                Assert.That(log.Count(line => line.StartsWith(EconomyLog.EconSupply + " ")), Is.EqualTo(24 * 2));
                Assert.That(log.Concat(csv), Has.None.Contains("_FirstPlayerRealm"), "Albion by its name, not eRealm's alias");
                Assert.That(csv.Skip(1).Count(line => line.Contains(",Albion,")), Is.EqualTo(24));
                Assert.That(log.Last(), Does.StartWith(EconomyLog.EconRehearsal + " scenario=\"fresh-7pct\" summary=1"));
                string md = File.ReadAllText(files[0]);
                Assert.That(md, Does.Contain("**Tests only.**").And.Contain("Build: smoke").And.Contain("## Gates"));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
        }

        private sealed class SumSink : IEconomyFlowSink
        {
            public long Sum;
            public readonly HashSet<EconomyFlowKind> Kinds = new();
            public void Record(long botId, long signedCopper, EconomyFlowKind kind, string callerFile, string callerMember)
            {
                Sum += signedCopper;
                Kinds.Add(kind);
            }
        }

        private sealed class FakeModule : RehearsalModule
        {
            private readonly string _id, _package;
            private readonly string[] _supersedes;
            public FakeModule(string id, string package, params string[] supersedes) => (_id, _package, _supersedes) = (id, package, supersedes);
            public override string Id => _id;
            public override string Package => _package;
            public override string Describe => "test";
            public override IReadOnlyCollection<string> Supersedes => _supersedes;
        }
    }
}
