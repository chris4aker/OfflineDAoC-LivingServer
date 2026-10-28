using System;

namespace DOL.GS
{
    /// <summary>
    /// How choosy a gamebot waiting for a PvE party is. Like a player with /lfg on, the longer it has waited the
    /// further it will travel to a group and the smaller a group it will take.
    ///
    /// The 2026-10-27 run (11:17-16:06, 5 hours) formed 153 PvE parties while 790 waits ran out after 20 minutes: 447
    /// below level 50 (most at 16-30) and 343 at 50. Formation only looked at bots that were not fighting, within 8
    /// minutes of the leader, and never relaxed either rule. Fighting no longer blocks a pick (the bot finishes its
    /// fight, then travels), and the travel limit and the smallest party grow with the wait. The level rule does not:
    /// a wider spread would put the lowest member against purple camps.
    /// </summary>
    public static class AutonomousGroupMatchmakingPatience
    {
        public static readonly TimeSpan RelaxedAfter = TimeSpan.FromMinutes(6);
        public static readonly TimeSpan EagerAfter = TimeSpan.FromMinutes(12);

        /// <summary>0 = fresh, 1 = relaxed, 2 = eager (takes almost any party).</summary>
        public static int Stage(long waitedMilliseconds) =>
            waitedMilliseconds >= EagerAfter.TotalMilliseconds ? 2 :
            waitedMilliseconds >= RelaxedAfter.TotalMilliseconds ? 1 : 0;

        /// <summary>The longest walk to the leader a bot at this stage accepts.</summary>
        public static double TravelMinutes(int stage) => stage switch
        {
            >= 2 => 14,
            1 => 11,
            _ => 8,
        };

        /// <summary>A four-man (tank, healer and two more) is taken only by a leader that has waited a long time.</summary>
        public static int MinimumPartySize(int stage) => stage >= 2 ? 4 : 5;

        /// <summary>
        /// A recruit and a leader settle on the more patient of the two: a bot that has waited long will walk to a
        /// fresh leader, and a fresh bot will join a long-waiting leader that came looking for it.
        /// </summary>
        public static int PairStage(int leaderStage, int candidateStage) => Math.Max(leaderStage, candidateStage);
    }
}
