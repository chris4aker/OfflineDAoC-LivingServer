namespace DOL.GS
{
    /// <summary>
    /// The silent pause between a player's whispers (GameLiving.WhisperReceive). Owner 2026-10-30: it was 1.5 s
    /// whatever the target, and a quest NPC's [keyword] links are whispers too, so the second of two quick clicks did
    /// nothing and said nothing. A whisper to a player keeps the 1.5 s pause against whisper spam; a whisper to an
    /// NPC has no silent pause (the /whisper command still refuses a repeat within half a second, and says so).
    /// </summary>
    public static class WhisperThrottle
    {
        public const int PlayerWindowMs = 1500;

        public static int WindowMs(bool targetIsPlayer) => targetIsPlayer ? PlayerWindowMs : 0;

        /// <summary>Whether a whisper sent this long after the player's previous whisper is dropped.</summary>
        public static bool Drops(bool targetIsPlayer, long sinceLastWhisperMs) => sinceLastWhisperMs < WindowMs(targetIsPlayer);
    }
}
