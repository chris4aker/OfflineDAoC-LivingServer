using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The economy rehearsal's week scenarios (the planner's plan 3.2): a week of 1-3 realms in minutes, each written as a
    /// readable ledger (markdown, CSV and the CRAFT_*/ECON_* log) into ECON_REHEARSAL_OUT, and the plan's gates asserted.
    /// The UT_ChatMonth pattern: [Explicit], ignored unless ECON_REHEARSAL_OUT is set. Optional: ECON_REHEARSAL_BUILD (the
    /// commit, named in every report), ECON_REHEARSAL_SCENARIOS (comma-separated names, e.g. "today-aged,fresh-7pct"),
    /// ECON_REHEARSAL_REALMS (3), ECON_REHEARSAL_BOTS (1000 a realm), ECON_REHEARSAL_DAYS (7), ECON_REHEARSAL_SEED (1).
    /// Tests only: a rehearsal is not an in-game result.
    /// </summary>
    [TestFixture, Explicit("rehearsal: set ECON_REHEARSAL_OUT"), NonParallelizable]
    public class UT_EconomyRehearsalWeek
    {
        private static int Env(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
                ? value : fallback;

        public static RehearsalScenario[] Scenarios()
        {
            int realms = Env("ECON_REHEARSAL_REALMS", 3), bots = Env("ECON_REHEARSAL_BOTS", 1000);
            int minutes = Env("ECON_REHEARSAL_DAYS", 7) * RehearsalScenario.Day, seed = Env("ECON_REHEARSAL_SEED", 1);
            string[] only = (Environment.GetEnvironmentVariable("ECON_REHEARSAL_SCENARIOS") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return RehearsalScenario.PlanScenarios(realms, bots, minutes, seed)
                .Where(s => only.Length == 0 || only.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        }

        [Test]
        public void AWeekOfEachScenario()
        {
            string output = Environment.GetEnvironmentVariable("ECON_REHEARSAL_OUT");
            if (string.IsNullOrEmpty(output))
                Assert.Ignore("set ECON_REHEARSAL_OUT");
            var failures = new System.Collections.Generic.List<string>();
            foreach (RehearsalScenario scenario in Scenarios())
            {
                RehearsalReport report = EconomyRehearsal.Run(scenario);
                string[] files = RehearsalLedgerWriter.Write(report, output);
                TestContext.Out.WriteLine($"{scenario.Name}: {report.Elapsed.TotalSeconds:0.0} s, gates {(report.GatesPass ? "pass" : "FAIL")} -> {files[0]}");
                foreach (RehearsalGate gate in report.Gates)
                {
                    TestContext.Out.WriteLine($"  {gate.Name}: {gate.Status} ({gate.Detail})");
                    if (gate.Status == GateStatus.Fail)
                        failures.Add($"{scenario.Name}: {gate.Name} ({gate.Detail})");
                }
                foreach (string missing in report.NotBuilt)
                    TestContext.Out.WriteLine($"  not built yet: {missing}");
            }
            Assert.That(failures, Is.Empty);
        }
    }
}
