using System;
using System.Collections.Generic;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Optional experience rates by level range, read from the server settings
    /// <c>xp_rate_by_level</c> (real players) and <c>bot_xp_rate_by_level</c> (autonomous gamebots).
    /// A setting looks like <c>1-19:3;20-34:5;35-50:12</c>. Inside a listed range its rate
    /// replaces the flat rate (<c>xp_rate</c> or <c>bot_xp_rate</c>); levels outside every range,
    /// an empty setting and an invalid setting all keep the flat rate, so the feature is off by default.
    /// </summary>
    public static partial class LevelRangeXpRates
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(LevelRangeXpRates));

        private sealed class Parsed
        {
            public Parsed(string spec, LevelRange[] ranges, string error)
            {
                Spec = spec;
                Ranges = ranges;
                Error = error;
            }

            public string Spec { get; }
            public LevelRange[] Ranges { get; }
            public string Error { get; }
        }

        // One cache entry per setting; settings change rarely, kills happen constantly.
        private static Parsed _playerCache;
        private static Parsed _botCache;

        /// <summary>The rate for a real player character of this level.</summary>
        public static double PlayerRate(int level)
        {
            return EffectiveRate(ServerProperties.Properties.XP_RATE_BY_LEVEL, level, ServerProperties.Properties.XP_RATE,
                ref _playerCache, "xp_rate_by_level");
        }

        /// <summary>
        /// The kill rate for a real player character of this level: <see cref="PlayerRate(int)"/>, times
        /// <c>rvr_zones_xp_rate</c> in RvR regions and zones (battlegrounds, frontiers, Darkness Falls), as gamebots get.
        /// </summary>
        public static double PlayerRate(int level, bool isRvR)
        {
            double rate = PlayerRate(level);
            if (isRvR)
                rate *= ServerProperties.Properties.RvR_XP_RATE;
            return rate;
        }

        /// <summary>The rate for an autonomous gamebot of this level (before the RvR-zone multiplier).</summary>
        public static double BotRate(int level)
        {
            return EffectiveRate(ServerProperties.Properties.BOT_XP_RATE_BY_LEVEL, level, ServerProperties.Properties.BOT_XP_RATE,
                ref _botCache, "bot_xp_rate_by_level");
        }

        /// <summary>
        /// The rate for <paramref name="level"/>: the matching range's rate, or <paramref name="flatRate"/>
        /// when the setting is empty or invalid or no range contains the level.
        /// </summary>
        public static double EffectiveRate(string spec, int level, double flatRate)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return flatRate;
            return TryParse(spec, out LevelRange[] ranges, out _) ? Lookup(ranges, level, flatRate) : flatRate;
        }

        private static double EffectiveRate(string spec, int level, double flatRate, ref Parsed cache, string settingName)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return flatRate;

            Parsed parsed = cache;
            if (parsed == null || !string.Equals(parsed.Spec, spec, StringComparison.Ordinal))
            {
                bool ok = TryParse(spec, out LevelRange[] ranges, out string error);
                parsed = new Parsed(spec, ok ? ranges : null, error);
                cache = parsed;
                // Check the level first, as the rest of the server does: before logging starts
                // (for example in unit tests) the logger has no queue to write to.
                if (ok)
                {
                    if (Log.IsInfoEnabled)
                        Log.Info($"{settingName} in use: {spec}");
                }
                else if (Log.IsWarnEnabled)
                    Log.Warn($"{settingName} is invalid and is ignored ({error}); the flat rate stays in use. Value: \"{spec}\"");
            }

            return parsed.Ranges == null ? flatRate : Lookup(parsed.Ranges, level, flatRate);
        }

        private static double Lookup(LevelRange[] ranges, int level, double flatRate)
        {
            foreach (LevelRange range in ranges)
            {
                if (level >= range.FirstLevel && level <= range.LastLevel)
                    return range.Rate;
            }

            return flatRate;
        }
    }
}
