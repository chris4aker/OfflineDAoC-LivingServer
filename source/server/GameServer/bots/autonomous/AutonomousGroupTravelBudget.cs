using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    // 1.2.3 (#569 step 3): port of zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze). "Owner" below is True165's.
    /// <summary>
    /// A formed PvE party picks camps it can reach within its task time (owner 2026-11-10). Logs 11/10: 97 of 596 PvE
    /// tasks never reached any camp; most had re-picked a camp mid-task with no travel limit, often in another region.
    /// Applied after the quiet-camp filter; never empties a non-empty list.
    /// </summary>
    public static class AutonomousGroupTravelBudget
    {
        [ServerProperty("bots", "bot_group_camp_travel_budget",
            "Does a PvE party only pick camps it can reach within about a third of its remaining task time (nearest camps when none fit)? Off = no travel limit, as before.", true)]
        public static bool Enabled = true;

        /// <summary>Real travel is 2.0x the straight-line estimate at p50 and 2.8x at p75 (logs 11/10).</summary>
        public const double SafetyFactor = 2.5;
        public const double MaxTravelShare = 0.35;
        public const double MinimumBudgetMinutes = 6;
        public const double DungeonSurchargeMinutes = 10;
        /// <summary>When nothing fits, camps up to this many budgets away before the nearest few.</summary>
        public const double WideBudgetFactor = 2;
        public const int KeepNearest = 3;

        public enum Tier { AllFit, Fit, Wide, Nearest }

        public static double BudgetMinutes(long remainingMs) =>
            Math.Max(MinimumBudgetMinutes, Math.Max(0, remainingMs) / 60_000d * MaxTravelShare);

        public static double EffectiveEstimate(double estimateMinutes, bool dungeon) =>
            estimateMinutes + (dungeon ? DungeonSurchargeMinutes : 0);

        /// <summary>
        /// Owner 2026-11-10 (round-3 review): the surcharge only weighs a dungeon camp against outdoor ones, and never
        /// once the leader is in that dungeon's region. An all-dungeon list (SelectCamp's environment roll) gets none,
        /// so dungeon parties are no longer all cut to the same nearest few camps.
        /// </summary>
        public static bool AppliesDungeonSurcharge(bool campIsDungeon, bool mixedCandidates, bool leaderInCampRegion) =>
            campIsDungeon && mixedCandidates && !leaderInCampRegion;

        /// <summary>
        /// The candidates whose effective estimate x 2.5 fits the budget; else those within twice the budget; else the
        /// <paramref name="keepNearest"/> nearest. Never empty for a non-empty input; returns the input itself when all fit.
        /// </summary>
        public static T[] Filter<T>(T[] candidates, Func<T, double> effectiveEstimate, long remainingMs, out Tier tier,
            int keepNearest = KeepNearest)
        {
            tier = Tier.AllFit;
            if (candidates == null || candidates.Length == 0)
                return candidates ?? [];
            double budget = BudgetMinutes(remainingMs);
            double[] cost = new double[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                double value = effectiveEstimate(candidates[i]);
                cost[i] = double.IsNaN(value) ? double.MaxValue : value * SafetyFactor;
            }
            T[] Within(double limit) => candidates.Where((_, i) => cost[i] <= limit).ToArray();
            T[] fit = Within(budget);
            if (fit.Length == candidates.Length)
                return candidates;
            if (fit.Length > 0)
            {
                tier = Tier.Fit;
                return fit;
            }
            T[] wide = Within(budget * WideBudgetFactor);
            if (wide.Length > 0)
            {
                tier = Tier.Wide;
                return wide;
            }
            tier = Tier.Nearest;
            return Enumerable.Range(0, candidates.Length).OrderBy(i => cost[i])
                .Take(Math.Max(1, keepNearest)).Select(i => candidates[i]).ToArray();
        }
    }

    /// <summary>
    /// The next region crossing cached per (realm, from region, to region). The older per-(realm, from) cache gave every
    /// later target region the first target's crossing (critic 2026-11-10); use this one when targets span regions.
    /// </summary>
    public static class AutonomousTravelCrossingCache
    {
        public static T GetOrResolve<T>(Dictionary<(eRealm Realm, ushort From, ushort To), T> cache, eRealm realm,
            ushort from, ushort to, Func<T> resolve)
        {
            var key = (realm, from, to);
            if (!cache.TryGetValue(key, out T value))
                cache[key] = value = resolve();
            return value;
        }
    }
}
