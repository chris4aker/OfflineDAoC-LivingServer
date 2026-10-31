using System;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Quest experience at the player's pace (owner 2026-10-30). A quest pays a share of the player's level
    /// (ClassicQuests.ScaleXP, since 0.36) or its fixed amount, and a bounty pays bulbs of its assigned level. When the
    /// server setting <c>quest_xp_by_level</c> is on and an <c>xp_rate_by_level</c> range covers the player's level, that
    /// amount is multiplied by the level's rate, as a kill's base XP is, so quests keep up with grinding at 12x, and one
    /// payment pays at most one level's worth of experience. The setting is a kill switch (owner 2026-10-31, after
    /// review): off, there is no rate and no cap, and a quest pays exactly what it paid before 1.1.1. These are pure
    /// functions, covered by UT_QuestExperience; GamePlayer.GainQuestExperience applies them, and the offer window
    /// shows their result.
    /// </summary>
    public static class QuestExperience
    {
        /// <summary>
        /// The multiplier for a quest payout to a player of <paramref name="level"/>: the rate of the <paramref name="rangeSpec"/>
        /// range that covers the level while <paramref name="settingOn"/>, else <paramref name="flatRate"/>, which is what
        /// the payout used before this setting (1 for quests, which never used xp_rate; xp_rate for bounties, which did).
        /// So the setting being off, an empty or invalid range setting and a level outside every range all leave the
        /// payout as it was. Kill XP is not touched here.
        /// </summary>
        public static double Rate(int level, bool settingOn, string rangeSpec, double flatRate = 1.0)
        {
            if (!settingOn)
                return flatRate;
            return LevelRangeXpRates.EffectiveRate(rangeSpec, level, flatRate);
        }

        /// <summary>The rate from the live server settings (the cached parse, which logs the setting once).</summary>
        public static double Rate(int level, double flatRate = 1.0)
        {
            if (!Properties.QUEST_XP_BY_LEVEL)
                return flatRate;
            return LevelRangeXpRates.PlayerRate(level, flatRate);
        }

        /// <summary>
        /// <paramref name="baseXp"/> at <paramref name="rate"/>, rounded down as kill XP is. Nothing for a base of zero or
        /// less, nothing for a rate of zero or less, and a rate of 1 changes nothing.
        /// </summary>
        public static long Scaled(long baseXp, double rate)
        {
            if (baseXp <= 0 || double.IsNaN(rate) || rate <= 0)
                return 0;
            if (rate == 1.0)
                return baseXp;
            decimal amount = baseXp * (decimal) Math.Min(rate, LevelRangeXpRates.MaximumRate);
            return (long) Math.Min(long.MaxValue, Math.Floor(amount));
        }

        /// <summary>
        /// The cap: one payment pays at most one level's worth, <paramref name="levelSpan"/> being the experience from the
        /// start of the player's level to the start of the next. A span of zero or less leaves the amount alone. The cap
        /// is part of the setting: <see cref="Payout(long, double, long, bool)"/> applies it only while the setting is on.
        /// </summary>
        public static long Capped(long xp, long levelSpan)
        {
            if (xp <= 0)
                return 0;
            return levelSpan > 0 ? Math.Min(xp, levelSpan) : xp;
        }

        /// <summary>
        /// What one payment of <paramref name="baseXp"/> pays at <paramref name="rate"/>: the amount, capped at one level
        /// only while <paramref name="settingOn"/>. With the setting off the amount is paid whole, however large, as it
        /// was before 1.1.1 (owner 2026-10-31, after review: the setting is a kill switch, so the cap goes with it).
        /// </summary>
        public static long Payout(long baseXp, double rate, long levelSpan, bool settingOn)
        {
            long scaled = Scaled(baseXp, rate);
            return settingOn ? Capped(scaled, levelSpan) : scaled;
        }

        /// <summary>
        /// What one payment of <paramref name="baseXp"/> pays a player of this level under the live settings: the rate,
        /// then the cap while <c>quest_xp_by_level</c> is on; the amount at <paramref name="flatRate"/> when it is off.
        /// </summary>
        public static long Payout(long baseXp, int level, long levelSpan, double flatRate = 1.0)
        {
            return Payout(baseXp, Rate(level, flatRate), levelSpan, Properties.QUEST_XP_BY_LEVEL);
        }

        /// <summary>
        /// What the offer and reward windows show: the payout as a whole percentage of the player's level, 0 to 100.
        /// The packet carries one byte, so a payment of more than a level (possible with the setting off) shows as a
        /// whole level; before 1.1.1 such a value wrapped around.
        /// </summary>
        public static int Percent(long paidXp, long levelSpan)
        {
            if (paidXp <= 0 || levelSpan <= 0)
                return 0;
            return (int) Math.Min(100, paidXp * 100 / levelSpan);
        }
    }
}
