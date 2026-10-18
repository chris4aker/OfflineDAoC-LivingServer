#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DOL.GS
{
    // The parsing half of LevelRangeXpRates. It has no server dependencies, so the launcher compiles this
    // same file (its Social Expansion tab checks a setting with exactly the server's rules).
    public static partial class LevelRangeXpRates
    {
        public const double MaximumRate = 1000.0;

        /// <summary>One level range and its rate.</summary>
        public readonly record struct LevelRange(int FirstLevel, int LastLevel, double Rate);

        /// <summary>
        /// Parses <c>first-last:rate</c> entries separated by semicolons. A single level may be written
        /// without a dash (<c>50:2</c>). Spaces are allowed; the decimal point is always a dot.
        /// Ranges must not overlap; rates must be greater than 0 and at most <see cref="MaximumRate"/>.
        /// </summary>
        public static bool TryParse(string spec, out LevelRange[] ranges, out string error)
        {
            ranges = Array.Empty<LevelRange>();
            error = null;

            if (string.IsNullOrWhiteSpace(spec))
            {
                error = "the setting is empty";
                return false;
            }

            var result = new List<LevelRange>();
            foreach (string rawEntry in spec.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                int colon = entry.IndexOf(':');
                if (colon <= 0 || colon != entry.LastIndexOf(':'))
                {
                    error = $"\"{entry}\" is not in the form first-last:rate";
                    return false;
                }

                string levels = entry[..colon].Trim();
                string rateText = entry[(colon + 1)..].Trim();

                int first, last;
                int dash = levels.IndexOf('-');
                if (dash < 0)
                {
                    if (!TryLevel(levels, out first))
                    {
                        error = $"\"{levels}\" is not a level";
                        return false;
                    }

                    last = first;
                }
                else if (!TryLevel(levels[..dash].Trim(), out first) || !TryLevel(levels[(dash + 1)..].Trim(), out last))
                {
                    error = $"\"{levels}\" is not a level range";
                    return false;
                }

                if (last < first)
                {
                    error = $"\"{levels}\" ends before it starts";
                    return false;
                }

                if (!double.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ||
                    double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0 || rate > MaximumRate)
                {
                    error = $"\"{rateText}\" is not a rate above 0 and at most {MaximumRate.ToString(CultureInfo.InvariantCulture)}";
                    return false;
                }

                foreach (LevelRange existing in result)
                {
                    if (first <= existing.LastLevel && last >= existing.FirstLevel)
                    {
                        error = $"{first}-{last} overlaps {existing.FirstLevel}-{existing.LastLevel}";
                        return false;
                    }
                }

                result.Add(new LevelRange(first, last, rate));
            }

            if (result.Count == 0)
            {
                error = "no ranges were given";
                return false;
            }

            ranges = result.ToArray();
            return true;
        }

        private static bool TryLevel(string text, out int level)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out level) && level >= 1 && level <= 255;
        }
    }
}
