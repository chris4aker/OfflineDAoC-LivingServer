using DOL.GS.ServerProperties;

namespace DOL.GS
{
    public enum PullStallAction { None, Log, End }

    /// <summary>
    /// The party pull-stall monitor (1.2.3, #569). Port of zomboden/OfflineDAoC-True165 0e0a397 (GPL-3.0, shadowofze;
    /// AutonomousPartyPullCamp's stall half): a grinding gamebot party that has had no group fight and no experience
    /// for <see cref="StallLogMilliseconds"/> logs AUTONOMOUS_GROUP_PULL_STALL naming the pull gate that holds it, and at
    /// <see cref="StallEndMilliseconds"/> ends its task cleanly (AUTONOMOUS_GROUP_PULL_STALL_END) instead of standing until
    /// the 15-minute stuck watchdog teleports every member home.
    /// Living Server already has the other half of that commit: the pre-camp ranged-pull check has its own throttle
    /// (6d41a77) and a ranged pull with no firing point is benched after three tries (#539). This catches whatever is
    /// left, and names it so the next fix has evidence.
    /// Pure rules; the coordinator (AutonomousBotGroupCoordinator.PullStall) runs them under its lock.
    /// </summary>
    public static class AutonomousPartyPullStall
    {
        [ServerProperty("bots", "bot_party_pull_stall_log",
            "Log AUTONOMOUS_GROUP_PULL_STALL, naming the pull gate, when a grinding gamebot party has had no fight or experience for 6 minutes.", true)]
        public static bool StallLog = true;

        [ServerProperty("bots", "bot_party_pull_stall_end",
            "End a grinding gamebot party's task cleanly when it has had no fight or experience for 11 minutes, instead of waiting for the 15-minute stuck watchdog teleport. Parties hosting a player are never ended.", true)]
        public static bool StallEnd = true;

        public const long StallLogMilliseconds = 6 * 60_000L;
        public const long StallEndMilliseconds = 11 * 60_000L;
        /// <summary>A pulse gap longer than this means the stall clock was not watched; start it again.</summary>
        public const long StallObserveGapMilliseconds = 30_000;
        /// <summary>How often the party's live experience is sampled.</summary>
        public const long ExperienceSampleMilliseconds = 5_000;

        /// <summary>Gate names, in CanInitiateNewPull's order. Constants, so the pull gate allocates nothing.</summary>
        public const string GateOpen = "open", GateNoSession = "no-session", GateExpeditionHold = "expedition-hold",
            GateExpeditionPuller = "expedition-puller", GateNoLeader = "no-leader", GateAssembly = "assembly",
            GateRegrouping = "regrouping", GateNotPuller = "not-puller", GateCasualty = "casualty",
            GateRecovering = "recovering-between-pulls", GateResources = "resources", GateHostedPlayer = "hosted-player",
            GateComposition = "composition", GateSize = "size", GatePlacement = "placement", GateNoPuller = "no-puller";

        public static PullStallAction PullStallStep(long sinceProgressMilliseconds) =>
            sinceProgressMilliseconds >= StallEndMilliseconds ? PullStallAction.End
            : sinceProgressMilliseconds >= StallLogMilliseconds ? PullStallAction.Log
            : PullStallAction.None;

        /// <summary>
        /// The stall clock (0 = stopped). It runs while the party grinds, and keeps running through a camp move
        /// ("Traveling" with the same camp, as TryRepositionEmptyCamp does), which is not progress. Combat, experience,
        /// a new camp or an unwatched gap start it again; any other phase stops it.
        /// </summary>
        public static long NextStallClock(long since, string phase, bool sameCamp, bool progress, long gapMilliseconds, long now)
        {
            bool grinding = phase == "Grinding";
            bool movingSameCamp = phase == "Traveling" && since > 0 && sameCamp;
            if (!grinding && !movingSameCamp) return 0;
            if (since == 0 || !sameCamp || progress || gapMilliseconds > StallObserveGapMilliseconds) return now;
            return since;
        }

        /// <summary>
        /// What the monitor does at this step, given the switches and whether this stall was already logged: a log
        /// line once per stall, then the end; with the end switched off, only the log line.
        /// </summary>
        public static PullStallAction Decide(PullStallAction step, bool alreadyLogged, bool stallLog, bool stallEnd, bool mayEnd)
        {
            if (step == PullStallAction.End && (!stallEnd || !mayEnd))
                step = PullStallAction.Log;
            if (step == PullStallAction.Log && (alreadyLogged || !stallLog))
                return PullStallAction.None;
            return step;
        }

        /// <summary>The pull gate's resource check for one member, for the log (no name: the log row ships); null when ready.</summary>
        public static string ResourceGate(int health, int power, int endurance, bool usesPower) =>
            AutonomousBotGroupCoordinator.ReadyForGroupPull(health, power, endurance, usesPower)
                ? null
                : $"{health}/{(usesPower ? power.ToString() : "-")}/{endurance}";

        /// <summary>The pull gate's placement check for one member, for the log (no name); null when it passes.</summary>
        public static string PlacementGate(bool riding, bool sameRegion, int distanceToLeader, int cohesionRadius) =>
            riding ? "riding"
            : !sameRegion ? "region"
            : distanceToLeader > cohesionRadius ? $"cohesion {distanceToLeader}"
            : null;
    }
}
