using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The terms of a trade window between a real player and an autonomous gamebot, pure (the full bot trade, owner
    /// 2026-11-16: "built out fully, look pretty and have functionality to the fullest"). The glue
    /// (<see cref="BotTrades"/>) values each piece with the existing valuation and passes the numbers here; this works out
    /// who owes whom, whether the bot accepts, and the lines the player reads in the system window. Every number in a line
    /// comes from the inputs.
    /// <list type="bullet">
    /// <item>The bot's side: pieces from its bag (its own crafted stock, loot it would sell, a listing it took back from the
    /// Exchange) at their asking price, or a commission it hands over (paid up front: asks nothing; on delivery: its price).</item>
    /// <item>The player's side: pieces the bot wants (a gear gap or an upgrade by its set score, or a piece for its own
    /// want-to-buy order) at its bid, and pieces it refuses, with the reason. A refused piece must come out before the bot
    /// accepts: nothing is ever taken for free.</item>
    /// <item>A job instead of a sale: a repair of the one piece the player put in, or a spellcraft imbue of the player's gems
    /// into the player's piece; the item stays the player's, the fee is owed.</item>
    /// </list>
    /// The balance: what the bot gives (its pieces and the job) less what the player gives (wanted pieces). Positive: the
    /// player owes it (exactly that much is taken, never more, whatever the player put in). Negative: the bot pays it, first
    /// from the escrow of its own want-to-buy orders the player filled, the rest from its purse but never more than its
    /// purse share (<see cref="Offer.PurseCap"/>); short of that it offers what it can spare and says so.
    /// </summary>
    public static class BotTradeTerms
    {
        /// <summary>Where a piece on the bot's side comes from.</summary>
        public enum Source { Stock, Bag, Listing, Delivery, Prepaid }

        /// <summary>What the bot thinks of a piece the player put in.</summary>
        public enum Verdict { Wanted, ForOrder, Refused, JobItem, JobGem }

        public enum Job { None, Repair, Spellcraft }

        public sealed record BotPiece(string ObjectId, string Name, int Quality, int Level, long Ask, Source Source, string OrderId = null);

        public sealed record PlayerPiece(string ObjectId, string Name, int Quality, int Level, long Bid, Verdict Verdict, string Reason = null,
            string OrderId = null);

        /// <summary>A job the bot was asked for (the window's Repair or Combine box): the fee, or why not.</summary>
        public sealed record JobQuote(Job Kind, string ItemName, long Fee, string Refusal = null, string Detail = null)
        {
            public bool Refused => !string.IsNullOrEmpty(Refusal);
        }

        /// <summary>Everything the bot weighs: both sides, the coin the player put in, the most its purse may pay.</summary>
        public sealed record Offer(string BotName, IReadOnlyList<BotPiece> BotPieces, IReadOnlyList<PlayerPiece> PlayerPieces, long PlayerCoin,
            long PurseCap, JobQuote Job = null);

        /// <summary>The terms now: owed by the player, paid by the bot (escrow first), whether it accepts, and the lines.</summary>
        public sealed record Quote(long Asked, long Bid, long JobFee, long Owed, long BotPays, long FromEscrow, long FromPurse, bool CappedByPurse,
            bool Accepts, string Blocker, IReadOnlyList<string> Lines)
        {
            /// <summary>The net the player pays (positive) or gets (negative).</summary>
            public long Net => Owed - BotPays;
            public bool Empty => Asked == 0 && Bid == 0 && JobFee == 0 && Blocker == null;
        }

        /// <summary>Coin as the system window writes it, exact: "1 gold, 20 silver, 5 copper" ("nothing" for none).</summary>
        public static string PriceText(long copper)
        {
            if (copper <= 0)
                return "nothing";
            long platinum = copper / 10_000_000, gold = copper / 10_000 % 1000, silver = copper / 100 % 100, rest = copper % 100;
            var parts = new List<string>(4);
            if (platinum > 0) parts.Add($"{platinum} platinum");
            if (gold > 0) parts.Add($"{gold} gold");
            if (silver > 0) parts.Add($"{silver} silver");
            if (rest > 0) parts.Add($"{rest} copper");
            return string.Join(", ", parts);
        }

        /// <summary>"Studded Vest (96%, level 23)" or "Studded Vest (level 23)".</summary>
        public static string PieceText(string name, int quality, int level) =>
            quality > 0 && level > 0 ? $"{name} ({quality}%, level {level})"
            : level > 0 ? $"{name} (level {level})"
            : name ?? "it";

        /// <summary>Works out the terms (pure).</summary>
        public static Quote Evaluate(Offer offer)
        {
            if (offer == null)
                return new Quote(0, 0, 0, 0, 0, 0, 0, false, false, "nothing to trade", []);
            string bot = string.IsNullOrWhiteSpace(offer.BotName) ? "The trader" : offer.BotName;
            IReadOnlyList<BotPiece> mine = offer.BotPieces ?? [];
            IReadOnlyList<PlayerPiece> theirs = offer.PlayerPieces ?? [];
            var lines = new List<string>();
            string blocker = null;

            long asked = 0;
            foreach (BotPiece piece in mine)
            {
                long ask = piece.Source == Source.Prepaid ? 0 : Math.Max(0, piece.Ask);
                asked += ask;
                string what = PieceText(piece.Name, piece.Quality, piece.Level);
                lines.Add(piece.Source switch
                {
                    Source.Prepaid => $"{bot} hands over your {what}: already paid.",
                    Source.Delivery => $"{bot} hands over your {what} for {PriceText(ask)}.",
                    Source.Listing => $"{bot} offers {what} for {PriceText(ask)} (taken off the Realm Exchange for you).",
                    _ => $"{bot} offers {what} for {PriceText(ask)}.",
                });
            }

            long bidPurse = 0, bidEscrow = 0;
            foreach (PlayerPiece piece in theirs)
            {
                string what = PieceText(piece.Name, piece.Quality, piece.Level);
                switch (piece.Verdict)
                {
                    case Verdict.Wanted:
                        bidPurse += Math.Max(0, piece.Bid);
                        lines.Add($"{bot} will give {PriceText(piece.Bid)} for your {what}.");
                        break;
                    case Verdict.ForOrder:
                        bidEscrow += Math.Max(0, piece.Bid);
                        lines.Add($"{bot} will give {PriceText(piece.Bid)} for your {what}, for its own order.");
                        break;
                    case Verdict.JobItem:
                    case Verdict.JobGem:
                        break;
                    default:
                        lines.Add($"{bot} does not want your {what}: {piece.Reason ?? "no use for it"}.");
                        blocker ??= $"Take your {piece.Name} out of the trade";
                        break;
                }
            }

            long jobFee = 0;
            JobQuote job = offer.Job;
            if (job != null && job.Kind != Job.None)
            {
                if (job.Refused)
                {
                    lines.Add(job.Kind == Job.Repair
                        ? $"{bot} cannot repair your {job.ItemName}: {job.Refusal}."
                        : $"{bot} cannot imbue your {job.ItemName}: {job.Refusal}.");
                    blocker ??= job.Kind == Job.Repair ? "Untick Repair" : "Untick Combine";
                }
                else
                {
                    jobFee = Math.Max(0, job.Fee);
                    string fee = jobFee > 0 ? $"for {PriceText(jobFee)}" : "free of charge";
                    lines.Add(job.Kind == Job.Repair
                        ? $"{bot} will repair your {job.ItemName} {fee}{Detail(job)}."
                        : $"{bot} will imbue your {job.ItemName} {fee}{Detail(job)}.");
                }
            }

            long bid = bidPurse + bidEscrow;
            long net = asked + jobFee - bid;
            long owed = 0, pays = 0, fromEscrow = 0, fromPurse = 0;
            bool capped = false;
            if (net > 0)
                owed = net;
            else if (net < 0)
            {
                long due = -net;
                fromEscrow = Math.Min(due, bidEscrow);
                long rest = due - fromEscrow;
                long cap = Math.Max(0, offer.PurseCap);
                fromPurse = Math.Min(rest, cap);
                capped = fromPurse < rest;
                pays = fromEscrow + fromPurse;
            }

            bool somethingMoves = mine.Count > 0 || bid > 0 || jobFee > 0 || job is { Kind: not Job.None, Refused: false };
            if (blocker == null && !somethingMoves)
                blocker = "Nothing to trade yet";
            if (blocker == null && owed > 0 && offer.PlayerCoin < owed)
                blocker = $"Put {PriceText(owed - Math.Max(0, offer.PlayerCoin))} more in the window";

            if (owed > 0)
                lines.Add($"You pay {PriceText(owed)}." + (offer.PlayerCoin > owed ? $" ({PriceText(offer.PlayerCoin - owed)} of your coin stays with you.)" : string.Empty));
            else if (pays > 0)
                lines.Add(capped ? $"{bot} pays you {PriceText(pays)}, all it can spare (worth {PriceText(-net)} to it)."
                    : $"{bot} pays you {PriceText(pays)}.");
            else if (somethingMoves && blocker == null)
                lines.Add(mine.Count > 0 && bid > 0 ? "An even swap: no coin changes hands." : "No coin changes hands.");

            bool accepts = blocker == null;
            // An empty window says nothing yet (the bot's list of goods is enough on opening).
            if (!accepts && !string.IsNullOrEmpty(blocker) && (somethingMoves || theirs.Count > 0 || job != null))
                lines.Add($"{bot} waits: {blocker}.");
            return new Quote(asked, bid, jobFee, owed, pays, fromEscrow, fromPurse, capped, accepts, blocker, lines);
        }

        private static string Detail(JobQuote job) => string.IsNullOrEmpty(job.Detail) ? string.Empty : $" ({job.Detail})";

        /// <summary>The share of its purse a bot may pay a player for gear: the gear pipeline's Exchange budget (WP6).</summary>
        public static long PurseCapOf(long purse, double share) => purse <= 0 ? 0 : (long)Math.Floor(purse * Math.Clamp(share, 0, 1));

        /// <summary>
        /// Whether two quotes say the same to the player (the system lines are sent only when the terms changed, so a
        /// client that sends the window again and again does not flood the chat).
        /// </summary>
        public static bool SameTerms(Quote a, Quote b) =>
            a != null && b != null && a.Owed == b.Owed && a.BotPays == b.BotPays && a.Accepts == b.Accepts &&
            a.Lines.SequenceEqual(b.Lines, StringComparer.Ordinal);
    }
}
