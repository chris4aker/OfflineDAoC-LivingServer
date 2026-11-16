using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// What a crafter gamebot remembers of a real player who bought from it (Crafting 2.0 WP8): WP0's table
    /// offline_crafter_customers, one row per crafter and player (deals, pieces not paid for, coin spent, a refund still
    /// owed). It decides the per-pair discount (an idea from mod-playerbots, no code taken), whether the crafter wants the
    /// coin up front, and whether it deals at all. Saved progress that stays in its world; release builds empty it.
    /// </summary>
    public static class CrafterCustomers
    {
        internal static LivingServerTable<string, OfflineCrafterCustomerRecord> Table =
            new("crafter customers", row => KeyOf(row.CrafterBotId, row.CustomerId), StringComparer.Ordinal);

        public static string KeyOf(long crafterId, string customerId) => crafterId.ToString(CultureInfo.InvariantCulture) + "|" + customerId;

        /// <summary>A real player's customer id: "player:" and the character's id (never a gamebot's "offlinebot:" id).</summary>
        public static string CustomerIdOf(string characterId) => "player:" + (characterId ?? string.Empty);

        public static bool IsPlayerCustomer(string customerId) => customerId?.StartsWith("player:", StringComparison.Ordinal) == true;

        public static string CharacterIdOf(string customerId) => IsPlayerCustomer(customerId) ? customerId["player:".Length..] : null;

        public static OfflineCrafterCustomerRecord Get(long crafterId, string customerId) =>
            Table.TryGet(KeyOf(crafterId, customerId), out OfflineCrafterCustomerRecord row) ? row : null;

        public static OfflineCrafterCustomerRecord[] Of(string customerId) => Table.Where(row => row.CustomerId == customerId);

        public static OfflineCrafterCustomerRecord[] OfCrafter(long crafterId) => Table.Where(row => row.CrafterBotId == crafterId);

        /// <summary>A delivered and paid piece.</summary>
        public static void NoteDeal(long crafterId, string customerId, string name, long copper, DateTime utcNow) =>
            Change(crafterId, customerId, name, row =>
            {
                row.Deals++;
                row.SpentCopper += Math.Max(0, copper);
                row.LastUtc = utcNow.ToString("O");
            });

        /// <summary>A piece made on a pay-on-delivery order and never paid for.</summary>
        public static void NoteStiffed(long crafterId, string customerId, string name, DateTime utcNow) =>
            Change(crafterId, customerId, name, row =>
            {
                row.Stiffed++;
                row.LastUtc = utcNow.ToString("O");
            });

        public static void AddOwed(long crafterId, string customerId, string name, long copper) =>
            Change(crafterId, customerId, name, row => row.OwedCopper += Math.Max(0, copper));

        /// <summary>Takes every refund owed to the customer (they are back): the total, and the rows are cleared.</summary>
        public static long TakeOwed(string customerId)
        {
            long total = 0;
            foreach (OfflineCrafterCustomerRecord row in Of(customerId).Where(row => row.OwedCopper > 0))
                Table.Change(KeyOf(row.CrafterBotId, customerId), () => row, r =>
                {
                    total += r.OwedCopper;
                    r.OwedCopper = 0;
                });
            return total;
        }

        private static void Change(long crafterId, string customerId, string name, Action<OfflineCrafterCustomerRecord> change)
        {
            if (crafterId <= 0 || string.IsNullOrEmpty(customerId))
                return;
            Table.Change(KeyOf(crafterId, customerId),
                () => new OfflineCrafterCustomerRecord { CrafterBotId = crafterId, CustomerId = customerId, Name = name ?? string.Empty },
                row =>
                {
                    if (!string.IsNullOrEmpty(name))
                        row.Name = name.Length > 64 ? name[..64] : name;
                    change(row);
                });
        }

        public static void Load() => Table.Load(GameServer.Database.SelectAllObjects<OfflineCrafterCustomerRecord>());

        public static void Flush(bool wait) => Table.Flush(wait);

        public static void ResetForTests() =>
            Table = new LivingServerTable<string, OfflineCrafterCustomerRecord>("crafter customers",
                row => KeyOf(row.CrafterBotId, row.CustomerId), StringComparer.Ordinal, database: () => null);

        #region Rules (pure)

        /// <summary>How a crafter stands with a customer.</summary>
        public enum Standing { Stranger, Regular, Friend, Stiffed, Refused }

        /// <summary>
        /// The standing: refused after two pieces never paid for or an insult (the bot's memory is cool towards the player);
        /// stiffed after one; a friend with three deals or more (or a friend in the bot's memory); a regular with one. Pure.
        /// </summary>
        public static Standing StandingOf(int deals, int stiffed, bool insulted, bool friend)
        {
            if (insulted || stiffed >= 2)
                return Standing.Refused;
            if (stiffed == 1)
                return Standing.Stiffed;
            if (deals >= 3 || friend)
                return Standing.Friend;
            return deals >= 1 ? Standing.Regular : Standing.Stranger;
        }

        /// <summary>The cut of the crafter's fee: 5% for a regular, 10% for a friend, 15% once 10 gold was spent with it. Pure.</summary>
        public static int DiscountPercent(Standing standing, long spentCopper) => standing switch
        {
            Standing.Regular => spentCopper >= 100_000 ? 15 : 5,
            Standing.Friend => spentCopper >= 100_000 ? 15 : 10,
            _ => 0,
        };

        /// <summary>
        /// Whether the crafter wants the coin up front (into the order's escrow) or on delivery (in the trade window when it
        /// hands the piece over): up front from a stranger it is not patient with (patience under 50) and from anyone who
        /// stiffed it, on delivery from regulars, friends and guildmates. Pure.
        /// </summary>
        public static bool WantsUpFront(Standing standing, int patience, bool guildmate) => standing switch
        {
            Standing.Stiffed => true,
            Standing.Stranger => !guildmate && patience < 50,
            _ => false,
        };

        #endregion
    }
}
