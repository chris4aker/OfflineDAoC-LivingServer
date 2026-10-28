using System;

namespace DOL.GS
{
    /// <summary>
    /// A bot assigned group PvE grinds solo while it waits for a party, and party formation only takes bots that
    /// are not fighting. In the 2026-10-18 play logs 98% of tasks were solo: 244 waits timed out after 20 minutes
    /// and only 23 PvE parties formed all day, because a waiting bot was almost always mid-fight when the
    /// formation pass looked. Like a player sitting at a camp with /lfg on, a waiting bot now often stands
    /// between pulls for a while, so the formation pass can find it free. Level 50s (who get no experience from
    /// soloing) wait more often than levelling bots.
    /// Since 2026-10-27 formation also takes a bot mid-fight (AutonomousGroupMatchmakingPatience), so the pause is
    /// mostly for show: levelling bots pause less (0.35 to 0.15) and keep the experience.
    /// </summary>
    public static class AutonomousGroupMatchmakingPause
    {
        public const long MinimumPauseMs = 45_000;
        public const long MaximumPauseMs = 90_000;
        /// <summary>After a pause (or a roll that skipped one), at least this long of grinding before the next roll.</summary>
        public const long GrindBetweenRollsMs = 60_000;

        public static double PauseChance(int level) => level >= 50 ? 0.6 : 0.15;

        /// <summary>
        /// Whether a bot that is ready for its next pull holds instead. <paramref name="pauseUntil"/> and
        /// <paramref name="nextRoll"/> carry the bot's state between calls; rolls are in [0, 1).
        /// </summary>
        public static bool ShouldHold(bool awaitingGroup, int level, long now, ref long pauseUntil, ref long nextRoll,
            double chanceRoll, double lengthRoll)
        {
            if (!awaitingGroup)
            {
                pauseUntil = 0;
                return false;
            }
            if (now < pauseUntil)
                return true;
            if (now < nextRoll)
                return false;
            if (Math.Clamp(chanceRoll, 0, 0.999999) < PauseChance(level))
            {
                pauseUntil = now + MinimumPauseMs + (long)(Math.Clamp(lengthRoll, 0, 0.999999) * (MaximumPauseMs - MinimumPauseMs));
                nextRoll = pauseUntil + GrindBetweenRollsMs;
                return true;
            }
            nextRoll = now + GrindBetweenRollsMs;
            return false;
        }
    }
}
