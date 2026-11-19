using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// Writes a run's readable ledger: <c>&lt;scenario&gt;-ledger.md</c> (gates, settings, modules, daily totals per realm,
    /// wealth, levels, crafted gear, orders, the index's quotes), <c>&lt;scenario&gt;-ledger.csv</c> (one row per realm and
    /// hour) and <c>&lt;scenario&gt;-ledger.log</c> (the CRAFT_*/ECON_* lines in the server's format, which
    /// source/tools/econ_timeline_report.py reads as it reads a server log).
    /// </summary>
    public static class RehearsalLedgerWriter
    {
        public static readonly string[] CsvColumns =
        [
            "tag", "hour", "hourUtc", "realm", "supplyCopper", "deltaCopper", "flowsCopper", "residualCopper", "faucetsCopper",
            "sinksCopper", "transfersCopper", "bots", "crafters", "crafterPurseMedian", "hunterPurseMedian", "crafterPurseTopFifth",
            "hunterPurseTopFifth", "meanLevel", "level1_20", "level21_30", "level31_40", "level41_49", "level50", "listings",
            "listedValue", "craftedWorn1_20", "craftedWorn21_30", "craftedWorn31_40", "craftedWorn41_49", "craftedWorn50",
            "orderWaitMedianMinutes", "metrics",
        ];

        /// <summary>Writes the three files into a folder; returns their paths (md, csv, log).</summary>
        public static string[] Write(RehearsalReport report, string folder)
        {
            Directory.CreateDirectory(folder);
            string stem = Path.Combine(folder, Safe(report.Scenario.Name) + "-ledger");
            File.WriteAllText(stem + ".md", Markdown(report), new UTF8Encoding(false));
            File.WriteAllText(stem + ".csv", Csv(report), new UTF8Encoding(false));
            File.WriteAllLines(stem + ".log", report.Lines, new UTF8Encoding(false));
            return [stem + ".md", stem + ".csv", stem + ".log"];
        }

        public static string Safe(string name) =>
            new string((name ?? "scenario").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());

        public static string Csv(RehearsalReport report)
        {
            var text = new StringBuilder();
            text.AppendLine(string.Join(",", CsvColumns));
            foreach (RehearsalHourRow row in report.Rows)
            {
                var cells = new List<string>
                {
                    EconomyLog.EconSupply, I(row.Hour), row.HourUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    RehearsalWorld.RealmName(row.Realm), I(row.SupplyCopper), I(row.DeltaCopper), I(row.FlowsCopper), I(row.ResidualCopper),
                    I(row.FaucetsCopper), I(row.SinksCopper), I(row.TransfersCopper), I(row.Bots), I(row.Crafters),
                    I(row.CrafterPurseMedian), I(row.HunterPurseMedian), I(row.CrafterPurseTopFifth), I(row.HunterPurseTopFifth),
                    D(row.MeanLevel),
                };
                cells.AddRange(row.LevelBands.Select(n => I(n)));
                cells.Add(I(row.Listings));
                cells.Add(I(row.ListedValue));
                cells.AddRange(row.CraftedWornShareByBand.Select(D));
                cells.Add(row.OrderWaitMedianMinutes is double wait ? D(wait) : "");
                cells.Add(Quote(string.Join(";", row.Metrics.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={D(p.Value)}"))));
                text.AppendLine(string.Join(",", cells));
            }
            return text.ToString();
        }

        public static string Markdown(RehearsalReport report)
        {
            RehearsalScenario scenario = report.Scenario;
            var md = new StringBuilder();
            md.AppendLine($"# Economy rehearsal: {scenario.Name}");
            md.AppendLine();
            md.AppendLine($"**Tests only.** A rehearsal on the packages' pure code, not an in-game result. Build: {report.Build}. " +
                          $"{scenario.Realms} realm(s) x {scenario.BotsPerRealm} gamebots, {scenario.Minutes:#,0} one-minute steps " +
                          $"({scenario.Minutes / 1440.0:0.#} days), seed {scenario.Seed}, {(scenario.Aged ? "aged world" : "fresh start")}; " +
                          $"ran in {report.Elapsed.TotalSeconds:0.0} s.");
            md.AppendLine();
            md.AppendLine("## Gates");
            md.AppendLine();
            md.AppendLine("| Gate | Result | Detail |");
            md.AppendLine("|---|---|---|");
            foreach (RehearsalGate gate in report.Gates)
                md.AppendLine($"| {gate.Name} | {gate.Status switch { GateStatus.Pass => "pass", GateStatus.Fail => "**FAIL**", _ => "n/a" }} | {gate.Detail} |");
            md.AppendLine();
            md.AppendLine("## Settings and modules");
            md.AppendLine();
            string changed = string.Join(", ", scenario.ChangedSettings().Select(key => $"`{key}={scenario.Text(key)}`"));
            md.AppendLine($"- Settings changed from the plan's defaults: {(changed.Length == 0 ? "none" : changed)}.");
            foreach (string missing in report.NotBuilt)
                md.AppendLine($"- Not built yet: `{missing}`.");
            md.AppendLine(report.Modules.Count == 0 ? "- Modules: none (today's economy only)." : "- Modules:");
            foreach (IRehearsalModule module in report.Modules)
                md.AppendLine($"  - {module.Package} `{module.Id}`: {module.Describe}");
            md.AppendLine();

            md.AppendLine("## Money supply by realm and day");
            md.AppendLine();
            md.AppendLine("Gold. Faucets and sinks exclude transfers between bots (Exchange sales, orders), which net to zero but for any tax.");
            md.AppendLine();
            md.AppendLine("| Realm | Day | Supply at end | Change | Faucets | Sinks | Largest faucets | Largest sinks |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var realmRows in report.Rows.GroupBy(row => row.Realm))
            {
                int index = Array.IndexOf(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }, realmRows.Key);
                long previous = index >= 0 && index < report.StartSupply.Length ? report.StartSupply[index] : 0;
                foreach (var day in realmRows.GroupBy(row => row.Hour / 24))
                {
                    RehearsalHourRow last = day.Last();
                    var flows = Sum(day);
                    string topIn = string.Join(", ", flows.Where(f => !RehearsalWorld.IsTransfer(f.Key) && f.Value.In > 0)
                        .OrderByDescending(f => f.Value.In).Take(3).Select(f => $"{f.Key} {EconomyRehearsal.Gold(f.Value.In)}"));
                    string topOut = string.Join(", ", flows.Where(f => !RehearsalWorld.IsTransfer(f.Key) && f.Value.Out > 0)
                        .OrderByDescending(f => f.Value.Out).Take(3).Select(f => $"{f.Key} {EconomyRehearsal.Gold(f.Value.Out)}"));
                    md.AppendLine($"| {RehearsalWorld.RealmName(realmRows.Key)} | {day.Key + 1} | {EconomyRehearsal.Gold(last.SupplyCopper)} | " +
                                  $"{EconomyRehearsal.Gold(last.SupplyCopper - previous)} | {EconomyRehearsal.Gold(day.Sum(r => r.FaucetsCopper))} | " +
                                  $"{EconomyRehearsal.Gold(day.Sum(r => r.SinksCopper))} | {topIn} | {topOut} |");
                    previous = last.SupplyCopper;
                }
            }
            md.AppendLine();

            md.AppendLine("## Bots at the end");
            md.AppendLine();
            md.AppendLine("| Realm | Bots | Crafters | Mean level | Levels 1-20 / 21-30 / 31-40 / 41-49 / 50 | Crafter purse median / top fifth | Hunter purse median / top fifth | Crafted share of worn gear by band | Listings (value) |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (RehearsalHourRow row in report.Rows.GroupBy(r => r.Realm).Select(g => g.Last()))
                md.AppendLine($"| {RehearsalWorld.RealmName(row.Realm)} | {row.Bots} | {row.Crafters} | {row.MeanLevel:0.0} | {string.Join(" / ", row.LevelBands)} | " +
                              $"{EconomyRehearsal.Gold(row.CrafterPurseMedian)} / {EconomyRehearsal.Gold(row.CrafterPurseTopFifth)} g | " +
                              $"{EconomyRehearsal.Gold(row.HunterPurseMedian)} / {EconomyRehearsal.Gold(row.HunterPurseTopFifth)} g | " +
                              $"{string.Join(" / ", row.CraftedWornShareByBand.Select(s => (s * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"))} | " +
                              $"{row.Listings} ({EconomyRehearsal.Gold(row.ListedValue)} g) |");
            md.AppendLine();

            md.AppendLine("## Reported, not asserted");
            md.AppendLine();
            md.AppendLine("| Metric | Value |");
            md.AppendLine("|---|---|");
            foreach ((string key, string value) in report.Metrics.OrderBy(p => p.Key, StringComparer.Ordinal))
                md.AppendLine($"| {key} | {value} |");
            md.AppendLine();
            md.AppendLine("Counters: " + string.Join(", ", report.Counters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value:#,0}")) + ".");
            md.AppendLine();
            md.AppendLine($"Calibration: {RehearsalFixtures.Calibration.Source}. Recipes: {RehearsalFixtures.Recipes.Source} " +
                          $"({RehearsalFixtures.Recipes.All.Count:#,0} recipes, {RehearsalFixtures.Recipes.HiddenCount} hidden). " +
                          "The log beside this file has every ECON_SUPPLY, ECON_FLOWS, CRAFT_* and ECON_REHEARSAL line; " +
                          "`python source/tools/econ_timeline_report.py <it>` summarizes it as it does a server log.");
            return md.ToString();
        }

        private static Dictionary<EconomyFlowKind, FlowTotals> Sum(IEnumerable<RehearsalHourRow> rows)
        {
            var sum = new Dictionary<EconomyFlowKind, FlowTotals>();
            foreach (RehearsalHourRow row in rows)
                foreach ((EconomyFlowKind kind, FlowTotals totals) in row.Flows)
                {
                    if (!sum.TryGetValue(kind, out FlowTotals into))
                        sum[kind] = into = new FlowTotals();
                    into.In += totals.In;
                    into.Out += totals.Out;
                    into.Count += totals.Count;
                }
            return sum;
        }

        private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string D(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
