using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Safety gates for the active-only autonomous population. Defaults guarantee
    /// that installing or starting the server cannot create a bot on its own.
    /// </summary>
    public static class AutonomousPopulationProperties
    {
        [ServerProperty("autonomous_population", "population_enabled", "Master switch. False means no autonomous profiles are created or spawned. Once enabled, live bot activity does not require a human player online.", false)]
        public static bool POPULATION_ENABLED;

        [ServerProperty("autonomous_population", "active_target", "Legacy dashboard mirror of the non-retired roster size. The server always targets the complete roster.", 0)]
        public static int ACTIVE_TARGET;

        [ServerProperty("autonomous_population", "hard_active_cap", "Legacy compatibility value only. It does not limit the autonomous population.", 0)]
        public static int HARD_ACTIVE_CAP;

        [ServerProperty("autonomous_population", "initial_cohort_size", "Recommended first observed cohort when population creation is explicitly enabled.", 3)]
        public static int INITIAL_COHORT_SIZE;

        [ServerProperty("autonomous_population", "active_only", "Reject time-skipped or offline simulated bot progression.", true)]
        public static bool ACTIVE_ONLY;

        [ServerProperty("autonomous_population", "startup_ramp_minutes", "Startup ramp duration. The first 33 percent stagger through five minutes and the remaining roster through the next ten minutes.", 15)]
        public static int STARTUP_RAMP_MINUTES;

        [ServerProperty("autonomous_population", "allow_character_deletion", "Owner-only destructive gate. Keep false so shutdowns, restarts, and population reductions can only park bot characters.", false)]
        public static bool ALLOW_CHARACTER_DELETION;

        // Not read by the server yet (audit 2026-11-03 §1.2): the deferral queue that will honour it is decided after the
        // L300/L600 baselines (docs/PERFORMANCE.md). AiProfile.ElapsedThisTickMs and AutonomousAiBudget are its seam.
        [ServerProperty("autonomous_population", "ai_budget_ms", "Planned per-tick budget in ms for autonomous planning work. Not enforced yet: changing it changes nothing today. AI_PROFILE (ai_profile) measures the real cost; see docs/PERFORMANCE.md.", 4.0)]
        public static double AI_BUDGET_MS;

        [ServerProperty("autonomous_population", "ai_profile", "AI cost profiler. Once a minute the server log gets one AI_PROFILE line (game-loop tick p50/p95/p99/max, serial planning and parallel brain time, lock waits, the ten costliest AI systems) and logs/ai-profile.csv gets one row per system; /lsprofile shows the last minute. It only measures; about 50-100 ns per timed call.", true)]
        public static bool AI_PROFILE;

        [ServerProperty("autonomous_population", "world_actor_only", "Require every progressing bot to remain a real loaded world actor.", true)]
        public static bool WORLD_ACTOR_ONLY;
    }
}
