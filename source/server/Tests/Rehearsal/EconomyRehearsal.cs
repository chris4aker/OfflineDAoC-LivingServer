using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal
{
    public enum GateStatus { Pass, Fail, NotApplicable }

    public sealed record RehearsalGate(string Name, GateStatus Status, string Detail);

    /// <summary>What a run produced: the hourly rows, the gates, the metrics, the log lines, and what was not built yet.</summary>
    public sealed class RehearsalReport
    {
        public RehearsalScenario Scenario { get; init; }
        public string Build { get; init; }
        public IReadOnlyList<IRehearsalModule> Modules { get; init; }
        public IReadOnlyList<RehearsalHourRow> Rows { get; init; }
        public List<RehearsalGate> Gates { get; } = new();
        /// <summary>Reported, not asserted (crafter wealth against hunters, realized rates against M0, module results).</summary>
        public Dictionary<string, string> Metrics { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<string> Lines { get; init; }
        /// <summary>"setting=value (WPn)" for each changed setting no running module honours.</summary>
        public List<string> NotBuilt { get; } = new();
        public long[] StartSupply { get; init; }
        public long[] EndSupply { get; init; }
        public TimeSpan Elapsed { get; set; }
        public IReadOnlyDictionary<string, long> Counters { get; init; }

        public bool GatesPass => Gates.All(gate => gate.Status != GateStatus.Fail);

        public RehearsalGate Gate(string name) => Gates.Single(gate => gate.Name == name);

        public void Metric(string key, object value) => Metrics[key] = EconomyLog.Format(value).Trim('"');
    }

    /// <summary>
    /// The economy rehearsal (Crafting 2.0 WP10, design facet U; the planner's plan 3.2): plays a realm's gamebot economy
    /// in 1-minute steps (10,080 for a week) on the packages' pure code, writes a ledger in the server's CRAFT_*/ECON_*
    /// line format and checks the plan's gates. <b>A rehearsal is a test, not an in-game result</b>; every report names its
    /// build. What runs today is the economy as it is (M0's calibration, today's listing, vendor and upgrade rules, the
    /// WP0 seams' defaults and the registered price index) plus each package's adapter found in Adapters/.
    /// </summary>
    /// <example>
    /// <code>RehearsalReport report = EconomyRehearsal.Run(RehearsalScenario.Today(realms: 1, bots: 200, minutes: RehearsalScenario.Day));
    /// RehearsalLedgerWriter.Write(report, "out/");</code>
    /// </example>
    public static class EconomyRehearsal
    {
        public const string NoNegativePurse = "no negative purse";
        public const string FlowsEqualSupply = "flows equal the change in supply";
        public const string BidBelowAsk = "Bid < Ask in every band";
        public const string BoundedSupplyGrowth = "supply growth within its band";
        public const string NoUndress = "0 undress events";
        public const string OrderWait = "median order wait under the limit";

        public static RehearsalReport Run(RehearsalScenario scenario, IEnumerable<IRehearsalModule> modules = null, string build = null,
            Action<RehearsalWorld> configure = null)
        {
            var clock = Stopwatch.StartNew();
            var world = new RehearsalWorld(scenario);
            world.Modules = RehearsalModules.For(scenario, modules);
            configure?.Invoke(world);
            foreach (IRehearsalModule module in world.Modules)
                module.Attach(world);
            RehearsalBaseline.Populate(world);
            world.OpenLedger();

            for (world.Minute = 0; world.Minute < scenario.Minutes; world.Minute++)
            {
                foreach (RehearsalAgent agent in world.Agents)
                {
                    Random random = world.RandomFor(agent.Realm);
                    if (agent.Now == RehearsalAgent.Phase.Hunting)
                        RehearsalBaseline.HuntMinute(world, agent, random);
                    if (world.Minute + 1 < agent.PhaseEndsMinute)
                        continue;
                    if (agent.Now == RehearsalAgent.Phase.Hunting)
                        RehearsalBaseline.TaskBreak(world, agent, random);
                    else
                        RehearsalBaseline.StartTask(world, agent, random);
                }
                foreach (IRehearsalModule module in world.Modules)
                    module.OnMinute(world);
                if (world.Minute % 60 == 59)
                {
                    world.ExpireListings();
                    foreach (IRehearsalModule module in world.Modules)
                        module.OnHour(world);
                    world.CloseHour();
                }
            }

            var report = new RehearsalReport
            {
                Scenario = scenario,
                Build = build ?? DefaultBuild(),
                Modules = world.Modules,
                Rows = world.Rows,
                Lines = world.Lines,
                StartSupply = world.StartSupply,
                EndSupply = world.Realms.Select(realm => world.InRealm(realm).Sum(agent => agent.Purse)).ToArray(),
                Counters = world.Counters,
            };
            foreach (IRehearsalModule module in world.Modules)
                module.Finish(world, report);
            NotBuilt(world, report);
            Evaluate(world, report);
            Summarize(world, report);
            clock.Stop();
            report.Elapsed = clock.Elapsed;
            report.Metric("elapsedSeconds", Math.Round(clock.Elapsed.TotalSeconds, 2));
            foreach (RehearsalGate gate in report.Gates)
                world.Log(EconomyLog.Line(EconomyLog.EconRehearsal, ("scenario", scenario.Name), ("gate", gate.Name),
                    ("status", gate.Status.ToString().ToLowerInvariant()), ("detail", gate.Detail), ("build", report.Build)));
            world.Log(EconomyLog.Line(EconomyLog.EconRehearsal, new[]
                {
                    ("scenario", (object)scenario.Name), ("summary", true), ("build", report.Build), ("realms", scenario.Realms),
                    ("botsPerRealm", scenario.BotsPerRealm), ("minutes", scenario.Minutes), ("seed", scenario.Seed),
                    ("gatesPass", report.GatesPass),
                }.Concat(report.Metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => (pair.Key, (object)pair.Value))).ToArray()));
            return report;
        }

        /// <summary>The build a report names: ECON_REHEARSAL_BUILD (e.g. a commit) when set, and the fork's build name.</summary>
        public static string DefaultBuild()
        {
            string commit = Environment.GetEnvironmentVariable("ECON_REHEARSAL_BUILD");
            return string.IsNullOrWhiteSpace(commit) ? ForkBuildInfo.BuildName + " (commit not given)" : $"{ForkBuildInfo.BuildName} {commit.Trim()}";
        }

        private static void NotBuilt(RehearsalWorld world, RehearsalReport report)
        {
            var honoured = new HashSet<string>(world.Modules.SelectMany(module => module.ReadsSettings ?? Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase);
            foreach (string key in world.Scenario.ChangedSettings())
                if (!honoured.Contains(key))
                {
                    string package = RehearsalScenario.Known.TryGetValue(key, out var known) ? known.Package : "no package";
                    report.NotBuilt.Add($"{key}={world.Scenario.Text(key)} ({package}: not built yet; the run used today's behaviour)");
                }
        }

        /// <summary>The plan's gates (3.2): asserted by the week scenarios; the smoke test asserts them on a day.</summary>
        public static void Evaluate(RehearsalWorld world, RehearsalReport report)
        {
            var gates = world.Calibration.Gates;
            long minPurse = world.Agents.Count == 0 ? 0 : world.Agents.Min(agent => agent.Purse);
            report.Gates.Add(new RehearsalGate(NoNegativePurse,
                world.NegativePurseEvents == 0 && minPurse >= 0 ? GateStatus.Pass : GateStatus.Fail,
                $"{world.NegativePurseEvents} negative moves; lowest purse {minPurse} copper"));

            RehearsalHourRow[] off = world.Rows.Where(row => row.ResidualCopper != 0).ToArray();
            report.Gates.Add(new RehearsalGate(FlowsEqualSupply, off.Length == 0 ? GateStatus.Pass : GateStatus.Fail,
                off.Length == 0 ? $"{world.Rows.Count} hourly rows, residual 0 in every one"
                    : $"{off.Length} of {world.Rows.Count} rows off; first: hour {off[0].Hour} {off[0].Realm} residual {off[0].ResidualCopper}"));

            report.Gates.Add(new RehearsalGate(BidBelowAsk,
                world.BandQuotes.Count == 0 ? GateStatus.NotApplicable : world.BidAskViolations == 0 ? GateStatus.Pass : GateStatus.Fail,
                world.BandQuotes.Count == 0 ? "no listing was made"
                    : $"{world.BandQuotes.Values.Sum(q => q.Quotes)} listings in {world.BandQuotes.Count} bands; {world.BidAskViolations} at or under the index's bid"));

            long start = report.StartSupply.Sum(), end = report.EndSupply.Sum();
            double botHours = world.Agents.Count * (world.Scenario.Minutes / 60.0);
            if (world.Scenario.Aged && start > 0)
            {
                double weekPercent = (end - start) * 100.0 / start * (RehearsalScenario.Week / (double)Math.Max(1, world.Scenario.Minutes));
                report.Gates.Add(new RehearsalGate(BoundedSupplyGrowth,
                    weekPercent <= gates.SupplyGrowthWeekPercentMax && end >= 0 ? GateStatus.Pass : GateStatus.Fail,
                    $"{Gold(start)} -> {Gold(end)} gold: {weekPercent:0.#}% a week (limit {gates.SupplyGrowthWeekPercentMax:0.#}%)"));
            }
            else
            {
                double perBotHourGold = botHours <= 0 ? 0 : (end - start) / 10_000.0 / botHours;
                report.Gates.Add(new RehearsalGate(BoundedSupplyGrowth,
                    perBotHourGold <= gates.SupplyGrowthPerBotHourGoldMax ? GateStatus.Pass : GateStatus.Fail,
                    $"{Gold(start)} -> {Gold(end)} gold: {perBotHourGold:0.##} gold a bot-hour (limit {gates.SupplyGrowthPerBotHourGoldMax:0.#})"));
            }

            report.Gates.Add(new RehearsalGate(NoUndress, world.UndressEvents == 0 ? GateStatus.Pass : GateStatus.Fail,
                $"{world.UndressEvents} undress events in {world.Counters.GetValueOrDefault("equipped") + world.Counters.GetValueOrDefault("equipped_crafted")} equips"));

            if (world.OrderWaitMinutes.Count == 0)
                report.Gates.Add(new RehearsalGate(OrderWait, GateStatus.NotApplicable,
                    world.OrdersPosted == 0 ? "no order board ran (WP5 not built yet)" : $"{world.OrdersPosted} orders posted, none collected"));
            else
            {
                double median = RehearsalWorld.MedianOf(world.OrderWaitMinutes);
                report.Gates.Add(new RehearsalGate(OrderWait, median < gates.OrderWaitMedianMinutesMax ? GateStatus.Pass : GateStatus.Fail,
                    $"median {median:0} minutes over {world.OrderWaitMinutes.Count} orders (limit {gates.OrderWaitMedianMinutesMax:0}); {world.OrdersNeverCollected} never collected"));
            }
        }

        /// <summary>Reported, not asserted: wealth by type, realized rates against M0, faucets and sinks.</summary>
        private static void Summarize(RehearsalWorld world, RehearsalReport report)
        {
            long[] crafters = world.Agents.Where(a => a.IsCrafter).Select(a => a.Purse).OrderBy(p => p).ToArray();
            long[] hunters = world.Agents.Where(a => !a.IsCrafter).Select(a => a.Purse).OrderBy(p => p).ToArray();
            report.Metric("supplyStartCopper", report.StartSupply.Sum());
            report.Metric("supplyEndCopper", report.EndSupply.Sum());
            report.Metric("wealthCrafterMedian", RehearsalWorld.Median(crafters));
            report.Metric("wealthHunterMedian", RehearsalWorld.Median(hunters));
            report.Metric("wealthCrafterTopFifth", RehearsalWorld.TopFifth(crafters));
            report.Metric("wealthHunterTopFifth", RehearsalWorld.TopFifth(hunters));
            report.Metric("crafters", crafters.Length);
            report.Metric("bots", world.Agents.Count);
            double botHours = Math.Max(1e-9, world.Agents.Count * world.Scenario.Minutes / 60.0);
            report.Metric("lootUpgradesPerBotHour", Math.Round(world.Counters.GetValueOrDefault("loot_upgrades") / botHours, 4));
            report.Metric("lootUpgradesPerBotHourObserved", world.Calibration.Observed.LootUpgradesPerBotHour);
            report.Metric("exchangeBuysPerBotHour", Math.Round(world.Counters.GetValueOrDefault("exchange_sales") / botHours, 4));
            report.Metric("vendorBuysPerBotHour", Math.Round(world.Counters.GetValueOrDefault("vendor_buys") / botHours, 4));
            var faucets = new Dictionary<EconomyFlowKind, long>();
            var sinks = new Dictionary<EconomyFlowKind, long>();
            foreach (RehearsalHourRow row in world.Rows)
                foreach ((EconomyFlowKind kind, FlowTotals totals) in row.Flows)
                {
                    if (RehearsalWorld.IsTransfer(kind))
                        continue;
                    faucets[kind] = faucets.GetValueOrDefault(kind) + totals.In;
                    sinks[kind] = sinks.GetValueOrDefault(kind) + totals.Out;
                }
            long faucetTotal = faucets.Values.Sum();
            report.Metric("faucetsCopper", faucetTotal);
            report.Metric("sinksCopper", sinks.Values.Sum());
            report.Metric("lootCoinShareOfFaucets", faucetTotal == 0 ? 0 : Math.Round(faucets.GetValueOrDefault(EconomyFlowKind.LootCoin) / (double)faucetTotal, 3));
            report.Metric("ordersPosted", world.OrdersPosted);
            report.Metric("ordersCollected", world.OrderWaitMinutes.Count);
            for (int band = 0; band < 5; band++)
            {
                int levelMin = band switch { 0 => 1, 1 => 21, 2 => 31, 3 => 41, _ => 50 };
                RehearsalAgent[] inBand = world.Agents.Where(a => RehearsalCalibration.BandIndex(a.Level) == band).ToArray();
                report.Metric($"bots_{RehearsalCalibration.BandNames[band]}", inBand.Length);
                if (inBand.Length > 0)
                    report.Metric($"lootGoldPerHour_{RehearsalCalibration.BandNames[band]}",
                        Math.Round(GrossLootGoldPerHour(world, levelMin), 1));
            }
        }

        /// <summary>The calibration's expected gross solo coin an hour at a level (to set beside M0's 9.5 / 33 / 94 / 74 gold).</summary>
        public static double GrossLootGoldPerHour(RehearsalWorld world, int level)
        {
            var c = world.Calibration;
            int representative = level switch { 21 => 25, 31 => 35, 41 => 45, 50 => 50, _ => 10 };
            return c.KillsPerHour(representative, false) * c.CoinPerKillBase(c.MonsterLevel(representative)) *
                   (1 + c.Coin.ExtraUpTo / 2) / 10_000.0;
        }

        public static string Gold(long copper) => (copper / 10_000.0).ToString("#,0.#", CultureInfo.InvariantCulture);
    }
}
