using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    // 1.2.3 (#569 step 3): port of zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze). "Owner" below is True165's.
    /// <summary>
    /// Where a forming PvE party meets (owner 2026-11-10): at a town every member can reach, often the leader's own,
    /// then the party walks to the camp together. Logs 11/10: a rendezvous moved 20k+ in its region had a 35-37% no-show
    /// rate against 7% for a kept one, all no-shows having waited the full fixed 15 minutes.
    /// Pure rules; the coordinator feeds them per-member travel estimates in minutes.
    /// </summary>
    public static class AutonomousMeetupPlanPolicy
    {
        [ServerProperty("bots", "bot_group_meetup_reachable_town",
            "Does a forming PvE party move its meetup next to the planned camp only to a town every member can reach in time (else it keeps its current town)? Off = the nearest town to the camp, as before.", true)]
        public static bool ReachableTownEnabled = true;

        [ServerProperty("bots", "bot_group_meetup_window_by_travel",
            "Is a PvE party's meetup wait sized from its farthest member's travel time (15 to 20 minutes)? Off = always 15 minutes.", true)]
        public static bool TravelWindowEnabled = true;

        /// <summary>About 34k straight-line at run speed: any town this close for everyone is fine.</summary>
        public const double FarthestMemberCapMinutes = 3.0;
        /// <summary>A new town may be this much farther for the farthest member than the current one.</summary>
        public const double SlackMinutes = 2.0;
        /// <summary>The travel estimate's "no route" value.</summary>
        public const double Unreachable = 9999;

        public const long MinimumWindowMilliseconds = AutonomousRendezvousAttendance.TimeoutMilliseconds;
        public const long MaximumWindowMilliseconds = 20 * 60_000L;
        /// <summary>Real travel is about 2-5x the straight-line estimate (logs 11/10).</summary>
        public const double WindowTravelFactor = 2.5;
        public const long WindowSlackMilliseconds = 5 * 60_000L;

        /// <summary>The largest estimate, or -1 for none.</summary>
        public static double Farthest(IReadOnlyList<double> minutes)
        {
            double farthest = -1;
            if (minutes == null) return farthest;
            foreach (double value in minutes)
                if (value > farthest) farthest = value;
            return farthest;
        }

        /// <summary>
        /// May the meetup move to a town with these per-member estimates (toNew) instead of the current one (toCurrent)?
        /// Only when every member can reach it and the farthest one needs at most 3 minutes, or no more than 2 minutes
        /// longer than to the current town.
        /// </summary>
        public static bool ShouldMoveRendezvous(IReadOnlyList<double> toNew, IReadOnlyList<double> toCurrent)
        {
            if (toNew == null || toNew.Count == 0)
                return false;
            foreach (double value in toNew)
                if (double.IsNaN(value) || value >= Unreachable)
                    return false;
            double farthestCurrent = Math.Max(0, Farthest(toCurrent));
            return Farthest(toNew) <= Math.Max(FarthestMemberCapMinutes, farthestCurrent + SlackMinutes);
        }

        /// <summary>The meetup wait for a farthest-member estimate: 2.5x the estimate plus 5 minutes, 15 to 20 minutes
        /// (owner 2026-11-10 approved the 20-minute cap).</summary>
        public static long MeetupWindowMs(double farthestMinutes)
        {
            if (double.IsNaN(farthestMinutes) || farthestMinutes <= 0)
                return MinimumWindowMilliseconds;
            double wanted = Math.Ceiling(Math.Min(farthestMinutes, 1_000) * WindowTravelFactor) * 60_000d + WindowSlackMilliseconds;
            return Math.Clamp((long)wanted, MinimumWindowMilliseconds, MaximumWindowMilliseconds);
        }
    }
}
