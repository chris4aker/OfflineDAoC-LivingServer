using System;
using DOL.Database;
using System.Threading;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// How many Realm Exchange posts an owner may hold. The default is today's rule: 3 for every gamebot item
    /// (AutonomousBotEconomy.MaxBotRealmExchangeListings). WP5 replaces it with DividedRealms' rule (3 more for a crafter's
    /// own work, ceac061a), sized by bot_crafter_exchange_listings.
    /// </summary>
    public interface IExchangeListingPolicy
    {
        /// <summary>An upper bound over every item: an owner at this many posts can list nothing more.</summary>
        int MaxListings(string ownerId);
        /// <summary>The cap that applies to this item (at most <see cref="MaxListings"/>). <paramref name="item"/> may be null.</summary>
        int ListingCap(string ownerId, DbInventoryItem item);
    }

    /// <summary>
    /// A package's say over what a gamebot does with one of its items (WP4, WP5 and WP6 register these instead of editing
    /// AutonomousBotEconomy). Every member has a neutral default, so a policy implements only what it needs. Called from
    /// parallel brain turns: implementations must be thread-safe and must not take locks or touch the database.
    /// The members map onto DividedRealms' edits of AutonomousBotEconomy (ceac061a, 702ef108) one for one.
    /// </summary>
    public interface IBotItemPolicy
    {
        string Name { get; }
        /// <summary>Never worn, listed or sold (DR: IsHeldForOrder, a piece made for another bot; IsHeldForQuest).</summary>
        bool IsReserved(GameBot bot, DbInventoryItem item) => false;
        /// <summary>Kept from vendors (DR: the bot's own work while the bag has room).</summary>
        bool KeepFromVendor(GameBot bot, DbInventoryItem item) => false;
        /// <summary>
        /// Whether this stackable is crafting material the bot wants: true or false decide; null leaves it to the next
        /// policy and finally to today's rule ("a bot with any SerializedCraftingSkills wants every stack"). DR: IsMaterialFor.
        /// </summary>
        bool? IsMaterialFor(GameBot bot, DbInventoryItem item) => null;
        /// <summary>Not posted now (DR: RealmExchangeSaturation, 25 bot posts of the kind already); a vendor may take it.</summary>
        bool BlocksListing(GameBot bot, DbInventoryItem item) => false;
        /// <summary>Worth a post whatever it fetches (DR: a crafted, non-stackable piece).</summary>
        bool AlwaysWorthListing(GameBot bot, DbInventoryItem item) => false;
        /// <summary>Higher posts first (DR: a craft trip posts what it made before the dearest loot).</summary>
        int ListingPriority(GameBot bot, DbInventoryItem item) => 0;
        /// <summary>After a successful post (DR: RealmExchangeSaturation.NoteListed).</summary>
        void OnListed(GameBot bot, DbInventoryItem item) { }
    }

    /// <summary>
    /// The Realm Exchange and item seams Crafting 2.0's WP0 put into the shared files (AutonomousBotEconomy,
    /// RealmExchangeExpiry, RealmExchangeTransactions, RealmExchangeSaleLedger), so later packages never edit those
    /// files. Every default leaves today's behaviour exactly as it was (UT_EconomySeamsDefaults). Lock order when a
    /// seam is called: RealmExchangeBroker.TransactionLock, then typeof(AutonomousBotEconomy), then DatabaseWriteLock,
    /// then a package's own lock.
    /// </summary>
    public static class EconomySeams
    {
        /// <summary>Today's cap: 3 posts per gamebot, whatever the item.</summary>
        public sealed class DefaultListingPolicy : IExchangeListingPolicy
        {
            public static readonly DefaultListingPolicy Instance = new();
            public int MaxListings(string ownerId) => AutonomousBotEconomy.MaxBotRealmExchangeListings;
            public int ListingCap(string ownerId, DbInventoryItem item) => AutonomousBotEconomy.MaxBotRealmExchangeListings;
        }

        private static volatile IExchangeListingPolicy _listings = DefaultListingPolicy.Instance;
        private static IBotItemPolicy[] _itemPolicies = Array.Empty<IBotItemPolicy>();

        public static IExchangeListingPolicy Listings
        {
            get => _listings;
            set => _listings = value ?? DefaultListingPolicy.Instance;
        }

        /// <summary>
        /// An expired gamebot listing, before it is deleted (the 24-hour sink). Return true when the seam took the item
        /// back (WP5: unsold crafted stock returns to its crafter, DR/our plan; or True165's Exchange165 relist and payout
        /// cycle) so the sweep leaves it alone; return true only once the item is no longer an expired listing (moved to
        /// a bag, or relisted with a new time), or the sweep meets it again a minute later. Called under TransactionLock
        /// and DatabaseWriteLock; must not block.
        /// </summary>
        public static Func<DbInventoryItem, long, bool> TryReclaimExpiredListing = (_, _) => false;

        /// <summary>
        /// What a seller receives from a sale: (price, realm, sellerId, buyerId) =&gt; proceeds. The default is the whole
        /// price (no tax, as today). WP5's tax is <c>price - round(price x exchange_tax_percent / 100)</c>. The result is
        /// kept within 0..price; the difference is the sale's tax, a sink.
        /// </summary>
        public static Func<long, eRealm, string, string, long> SellerProceeds = (price, _, _, _) => price;

        /// <summary>
        /// Raised after a sale is committed to the database (RealmExchangeSaleLedger.CommitSale), with the price, the tax
        /// and whether the item was crafted in the sale record. Subscribers (WP2's ledger, WP5's index) run under the
        /// Exchange's locks: keep them to counters, never block or throw (an exception is caught and dropped).
        /// </summary>
        public static event Action<RealmExchangeSaleLedger.Sale> SaleCommitted;

        public static void Register(IBotItemPolicy policy)
        {
            if (policy == null)
                return;
            IBotItemPolicy[] current, next;
            do
            {
                current = Volatile.Read(ref _itemPolicies);
                if (Array.IndexOf(current, policy) >= 0)
                    return;
                next = new IBotItemPolicy[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = policy;
            }
            while (Interlocked.CompareExchange(ref _itemPolicies, next, current) != current);
        }

        /// <summary>Tests: back to every default.</summary>
        public static void ResetForTests()
        {
            Listings = DefaultListingPolicy.Instance;
            Volatile.Write(ref _itemPolicies, Array.Empty<IBotItemPolicy>());
            TryReclaimExpiredListing = (_, _) => false;
            SellerProceeds = (price, _, _, _) => price;
            SaleCommitted = null;
        }

        public static int ItemPolicyCount => Volatile.Read(ref _itemPolicies).Length;

        public static bool IsReserved(GameBot bot, DbInventoryItem item)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                if (policy.IsReserved(bot, item))
                    return true;
            return false;
        }

        public static bool KeepFromVendor(GameBot bot, DbInventoryItem item)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                if (policy.KeepFromVendor(bot, item))
                    return true;
            return false;
        }

        /// <summary>The first policy's answer, else today's rule (<paramref name="legacyTrainedCrafter"/>).</summary>
        public static bool IsMaterialFor(GameBot bot, DbInventoryItem item, bool legacyTrainedCrafter)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                if (policy.IsMaterialFor(bot, item) is bool decided)
                    return decided;
            return legacyTrainedCrafter;
        }

        public static bool BlocksListing(GameBot bot, DbInventoryItem item)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                if (policy.BlocksListing(bot, item))
                    return true;
            return false;
        }

        public static bool AlwaysWorthListing(GameBot bot, DbInventoryItem item)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                if (policy.AlwaysWorthListing(bot, item))
                    return true;
            return false;
        }

        public static int ListingPriority(GameBot bot, DbInventoryItem item)
        {
            int priority = 0;
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
                priority += policy.ListingPriority(bot, item);
            return priority;
        }

        public static void NotifyListed(GameBot bot, DbInventoryItem item)
        {
            foreach (IBotItemPolicy policy in Volatile.Read(ref _itemPolicies))
            {
                try { policy.OnListed(bot, item); }
                catch { } // a package's bookkeeping never undoes a listing
            }
        }

        /// <summary>The seller's proceeds of a sale, kept within 0..price.</summary>
        public static long ProceedsOf(long price, eRealm realm, string sellerId, string buyerId)
        {
            if (price <= 0)
                return price;
            long proceeds;
            try { proceeds = SellerProceeds?.Invoke(price, realm, sellerId, buyerId) ?? price; }
            catch { proceeds = price; }
            return Math.Clamp(proceeds, 0, price);
        }

        public static bool Reclaim(DbInventoryItem item, long botId)
        {
            try { return TryReclaimExpiredListing?.Invoke(item, botId) == true; }
            catch { return false; }
        }

        public static void RaiseSaleCommitted(RealmExchangeSaleLedger.Sale sale)
        {
            Action<RealmExchangeSaleLedger.Sale> handlers = SaleCommitted;
            if (handlers == null)
                return;
            foreach (Action<RealmExchangeSaleLedger.Sale> handler in handlers.GetInvocationList())
            {
                try { handler(sale); }
                catch { } // a subscriber's failure never undoes a committed sale
            }
        }
    }
}
