using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Haggling with a crafter gamebot (Crafting 2.0 WP8, design doc §8: "a short bounded exchange; a bot that likes you
    /// rounds down; one you insulted doesn't deal"), pure. The floor is what the piece costs the crafter: its materials
    /// plus half its fee (the board's minimum fee for a stranger), so no haggle makes a crafter work at a loss. At most
    /// <see cref="MaxRounds"/> counters; a third lowball ends the talk.
    /// </summary>
    /// <remarks>Written for this project; no fork had player haggling.</remarks>
    public static class CrafterHaggle
    {
        public const int MaxRounds = 3;

        public enum Verdict { Accept, Counter, Refuse, Walk }

        /// <summary>What the crafter says to an offer: accept at the offer, counter at a price, refuse (stays at its ask), or walk.</summary>
        public readonly record struct Result(Verdict Verdict, long Price, int Round);

        /// <summary>The least the crafter takes: materials plus half the fee (all of it from a customer it is cool towards).</summary>
        public static long Floor(long materials, long fee, bool cool) =>
            Math.Max(0, materials) + (cool ? Math.Max(0, fee) : Math.Max(0, fee) / 2);

        /// <summary>
        /// The crafter's answer to an offer on its ask. <paramref name="friendly"/> (a regular or a friend) splits the
        /// difference generously and rounds down; <paramref name="patience"/> (0-100, the personality trait) decides how far it
        /// comes down. Rounds after <see cref="MaxRounds"/> end the haggle (Walk). Pure.
        /// </summary>
        public static Result Answer(long ask, long offer, long floor, int round, bool friendly, int patience)
        {
            round = Math.Max(1, round);
            if (offer >= ask)
                return new Result(Verdict.Accept, ask, round);
            if (round > MaxRounds)
                return new Result(Verdict.Walk, ask, round);
            if (offer >= floor && (friendly || offer * 100 >= ask * 90))
                return new Result(Verdict.Accept, offer, round);
            if (offer * 100 < floor * 60)
                return new Result(round >= MaxRounds ? Verdict.Walk : Verdict.Refuse, ask, round);
            // Come part of the way down: a friendly crafter halfway, others a quarter to a third by patience.
            double share = friendly ? 0.5 : 0.2 + Math.Clamp(patience, 0, 100) / 100.0 * 0.15;
            long counter = Math.Max(floor, ask - (long)Math.Round((ask - Math.Max(offer, floor)) * share));
            counter = friendly ? RoundDown(counter, floor) : RoundUp(counter, ask);
            if (counter >= ask)
                return new Result(Verdict.Refuse, ask, round);
            return new Result(Verdict.Counter, counter, round);
        }

        /// <summary>Rounds a price down to ten copper (a silver from 1 gold, ten silver from 10 gold), never below the floor.</summary>
        public static long RoundDown(long copper, long floor)
        {
            long step = copper >= 100_000 ? 1_000 : copper >= 10_000 ? 100 : 10;
            return Math.Max(floor, copper / step * step);
        }

        /// <summary>Rounds a price up the same way, never above the ask.</summary>
        public static long RoundUp(long copper, long ask)
        {
            long step = copper >= 100_000 ? 1_000 : copper >= 10_000 ? 100 : 10;
            return Math.Min(ask, (copper + step - 1) / step * step);
        }

        private static readonly Regex Amount = new(
            @"(\d+(?:\.\d+)?)\s*(p|plat|platinum|g|gold|gc|s|silver|c|copper)\b", RegexOptions.Compiled);

        private static readonly Regex BareOffer = new(
            @"\b(?:how about|what about|would you take|would u take|will you take|will u take|ill give you|ill give u|i'll give you|ill do|i can do|i can pay|can you do|can u do|do it for|for)\s+(\d+(?:\.\d+)?)\b(?!\s*%)",
            RegexOptions.Compiled);

        /// <summary>
        /// The coin a line offers ("1g 50s", "how about 80s", "2.5g", "150 silver", "would you take 90" in the ask's unit),
        /// or null. <paramref name="askCopper"/> picks the unit of a bare number (gold above 1 gold, else silver). Pure.
        /// </summary>
        public static long? ParseCopper(string line, long askCopper = 0)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;
            string t = line.ToLowerInvariant().Replace(",", " ");
            long total = 0;
            bool any = false;
            foreach (Match match in Amount.Matches(t))
            {
                if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                    continue;
                long unit = match.Groups[2].Value switch
                {
                    "p" or "plat" or "platinum" => 10_000_000,
                    "g" or "gold" or "gc" => 10_000,
                    "s" or "silver" => 100,
                    _ => 1,
                };
                total += (long)Math.Round(value * unit);
                any = true;
            }
            if (any)
                return total;
            Match bare = BareOffer.Match(t);
            if (bare.Success && double.TryParse(bare.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain) && plain > 0)
                return (long)Math.Round(plain * (askCopper >= 10_000 ? 10_000 : 100));
            return null;
        }
    }
}
