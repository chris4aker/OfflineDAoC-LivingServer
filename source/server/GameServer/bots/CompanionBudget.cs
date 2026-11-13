using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Play with friends (1.2, co-op audit §4 and rule 7): companions are the heaviest bots (a /raid 80 is 79 of them at
    /// the 300 ms player-led cadence), and their caps were per group only. While more than one real player is online a
    /// new /spawn (or /raid) companion needs room in two budgets: the server total (<c>companion_budget_total</c>, 160)
    /// and the player's own share (<c>companion_budget_per_player</c>, 79 = one full /raid 80). With one player online
    /// neither applies, so single-player play is exactly as before.
    /// </summary>
    public static class CompanionBudget
    {
        /// <summary>What the budget looks at. Pure data.</summary>
        public readonly record struct Counts(int HumansOnline, int Own, int ServerTotal);

        /// <summary>
        /// Why one more companion can't come out now, or null when it can: "per_player" (the player's share is full) or
        /// "server_total" (the server's is). Nothing applies with one human online; a limit of 0 or less is off.
        /// </summary>
        public static string Blocks(in Counts counts, int total, int perPlayer) =>
            counts.HumansOnline <= 1 ? null
            : perPlayer > 0 && counts.Own >= perPlayer ? "per_player"
            : total > 0 && counts.ServerTotal >= total ? "server_total"
            : null;

        /// <summary>What the player is told when the budget refuses a companion.</summary>
        public static string Message(string reason, in Counts counts, int total, int perPlayer) => reason switch
        {
            "per_player" => $"You have {counts.Own} companions out, your share while {counts.HumansOnline} players are online " +
                            $"(companion_budget_per_player = {perPlayer}).",
            "server_total" => $"The server has {counts.ServerTotal} companions out for its {counts.HumansOnline} players, its " +
                              $"limit while friends play (companion_budget_total = {total}). Ask a friend to dismiss some.",
            _ => "No more companions can come out right now.",
        };

        /// <summary>The player's own /spawn and /raid companions (temporary helpers they own, in their group).</summary>
        public static int CompanionsOf(GamePlayer player) =>
            player?.Group?.GetMembersInTheGroup().OfType<GameBot>().Count(bot => bot.IsTemporaryGroupHelper && bot.Owner == player) ?? 0;

        /// <summary>The counts for <paramref name="player"/> among the real players <paramref name="online"/>.</summary>
        public static Counts CountsFor(GamePlayer player, IEnumerable<GamePlayer> online)
        {
            int humans = 0, total = 0;
            bool counted = false;
            foreach (GamePlayer human in online ?? Enumerable.Empty<GamePlayer>())
            {
                if (human == null)
                    continue;
                humans++;
                total += CompanionsOf(human);
                counted |= human == player;
            }
            int own = CompanionsOf(player);
            if (!counted && player != null)
            {
                humans++;
                total += own;
            }
            return new Counts(humans, own, total);
        }

        /// <summary>Why <paramref name="player"/> can't have one more companion now (with the message), or null.</summary>
        public static string BlocksSpawn(GamePlayer player, IEnumerable<GamePlayer> online, out string message)
        {
            Counts counts = CountsFor(player, online);
            int total = Properties.COMPANION_BUDGET_TOTAL, perPlayer = Properties.COMPANION_BUDGET_PER_PLAYER;
            string reason = Blocks(counts, total, perPlayer);
            message = reason == null ? null : Message(reason, counts, total, perPlayer);
            return reason;
        }
    }
}
