using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Names what a gamebot coin move was for (Crafting 2.0 WP2) without editing any call site: a caller's explicit
    /// <see cref="EconomyFlowKind"/> wins; otherwise the compiler-filled caller (file and member, from WP0's
    /// [CallerFilePath]/[CallerMemberName] on AutonomousBotEconomy.AddMoney/TrySpend) is looked up in
    /// <see cref="KnownCallers"/>, then by member alone, then by a few file-name hints. A refund in the same member
    /// (TrySpend, then AddMoney when the item could not be delivered) gets the member's kind, as in True165's
    /// BotMoneyLedger165 (a8c793e), so a kind's net is what really left or arrived.
    /// UT_EconomyFlowClassifier scans the source tree: every AddMoney/TrySpend call site must classify to a named kind,
    /// so a new call site either passes its kind or adds a row here.
    /// </summary>
    public static class EconomyFlowClassifier
    {
        /// <summary>Every call site of today's tree, by bare file name and member (the caller's method name).</summary>
        public static readonly IReadOnlyDictionary<(string File, string Member), EconomyFlowKind> KnownCallers =
            new Dictionary<(string, string), EconomyFlowKind>(new FileMemberComparer())
            {
                // Coin a gamebot picks up: its share after guild dues (the dues go to the guild bank, see GuildBanks).
                [("GameBot.cs", "TryAutoPickUpMoney")] = EconomyFlowKind.LootCoin,
                // Group-split coin, the share after dues.
                [("AutonomousBotGuildEconomy.cs", "PayLootShare")] = EconomyFlowKind.LootShare,
                [("AutonomousBotEconomy.cs", "TrySellToVendor")] = EconomyFlowKind.VendorSale,
                [("AutonomousBotEconomy.cs", "TryBuyUsefulVendorUpgrade")] = EconomyFlowKind.VendorGear,
                // Stablemaster routes (the ticket, refunded when the ride does not start).
                [("GameBot.cs", "ContinueReturnJourney")] = EconomyFlowKind.StableTicket,
                [("AutonomousWorldBotController.cs", "HandleRealmRaidStableTravel")] = EconomyFlowKind.StableTicket,
                [("AutonomousWorldBotController.cs", "TryBeginFasterStableRoute")] = EconomyFlowKind.StableTicket,
                [("AutonomousWorldBotController.FrontierTransport.cs", "TryFrontierTransport")] = EconomyFlowKind.Medallion,
                [("AutonomousBotCosmetics.cs", "TryRestyle")] = EconomyFlowKind.Dye,
                // Beggars: a bot's gift to a beggar bot (both sides) and a player's coin dragged onto one.
                [("AutonomousBeggars.cs", "TryBegBot")] = EconomyFlowKind.BeggarGift,
                [("AutonomousBeggars.cs", "TryReceiveMoney")] = EconomyFlowKind.BeggarGift,
                // An Exchange sale credited through AddMoney (today's purchases pay the seller's record directly and
                // are counted from EconomySeams.SaleCommitted instead).
                [("RealmExchangeBroker.cs", "CreditSeller")] = EconomyFlowKind.ExchangeSale,
            };

        /// <summary>Members whose name says what they are for, wherever they live (a partial class split moves files).</summary>
        private static readonly Dictionary<string, EconomyFlowKind> ByMember = new(StringComparer.Ordinal)
        {
            ["TryAutoPickUpMoney"] = EconomyFlowKind.LootCoin,
            ["PayLootShare"] = EconomyFlowKind.LootShare,
            ["TrySellToVendor"] = EconomyFlowKind.VendorSale,
            ["TryBuyUsefulVendorUpgrade"] = EconomyFlowKind.VendorGear,
            ["ContinueReturnJourney"] = EconomyFlowKind.StableTicket,
            ["HandleRealmRaidStableTravel"] = EconomyFlowKind.StableTicket,
            ["TryBeginFasterStableRoute"] = EconomyFlowKind.StableTicket,
            ["TryFrontierTransport"] = EconomyFlowKind.Medallion,
            ["TryRestockRepairKits"] = EconomyFlowKind.RepairKit,
            ["SupplySiegeItem"] = EconomyFlowKind.Siege,
            ["TryRestyle"] = EconomyFlowKind.Dye,
            ["TryBegBot"] = EconomyFlowKind.BeggarGift,
            ["CreditSeller"] = EconomyFlowKind.ExchangeSale,
        };

        /// <summary>File-name hints, in order, for a caller not listed above (a later package's new file).</summary>
        private static readonly (string Part, EconomyFlowKind Kind)[] ByFile =
        [
            ("Beggar", EconomyFlowKind.BeggarGift),
            ("KillTask", EconomyFlowKind.KillTask),
            ("Siege", EconomyFlowKind.Siege),
            ("Cosmetic", EconomyFlowKind.Dye),
            ("Dye", EconomyFlowKind.Dye),
            ("Poison", EconomyFlowKind.Poison),
            ("Potion", EconomyFlowKind.Potion),
            ("Healer", EconomyFlowKind.Healer),
            ("Repair", EconomyFlowKind.Repair),
            ("Enchant", EconomyFlowKind.Enchant),
            ("Commission", EconomyFlowKind.Commission),
            ("CraftOrder", EconomyFlowKind.CraftOrder),
            ("Luxury", EconomyFlowKind.Luxury),
            ("Stable", EconomyFlowKind.StableTicket),
            ("RealmExchange", EconomyFlowKind.ExchangeSale),
            ("GuildEconomy", EconomyFlowKind.Guild),
            ("Guilds", EconomyFlowKind.Guild),
        ];

        // Classification is by (file, member) only, so it is cached: one dictionary read per coin move.
        private static readonly ConcurrentDictionary<(string, string), EconomyFlowKind> Cache = new(new FileMemberComparer());

        /// <summary>The kind of a move: the caller's explicit kind, else the caller's file and member, else Unattributed.</summary>
        public static EconomyFlowKind Classify(EconomyFlowKind explicitKind, string callerFile, string callerMember)
        {
            if (explicitKind != EconomyFlowKind.Unattributed)
                return explicitKind;
            string file = EconomyFlows.FileName(callerFile);
            string member = callerMember ?? string.Empty;
            if (file.Length == 0 && member.Length == 0)
                return EconomyFlowKind.Unattributed;
            return Cache.GetOrAdd((file, member), static key => Lookup(key.Item1, key.Item2));
        }

        private static EconomyFlowKind Lookup(string file, string member)
        {
            if (KnownCallers.TryGetValue((file, member), out EconomyFlowKind kind))
                return kind;
            if (ByMember.TryGetValue(member, out kind))
                return kind;
            foreach ((string part, EconomyFlowKind hinted) in ByFile)
                if (file.Contains(part, StringComparison.OrdinalIgnoreCase))
                    return hinted;
            return EconomyFlowKind.Unattributed;
        }

        /// <summary>
        /// A transfer moves coin between a gamebot's purse and another part of the money supply (a player's wallet, a
        /// guild bank, the Exchange's seller), so it changes the bots' coin but not the realm's supply. Bot-to-bot moves
        /// need no flag: both sides are recorded and cancel in the kind's net. Every other kind is a faucet or a sink
        /// by the sign of its net.
        /// </summary>
        public static bool IsTransfer(EconomyFlowKind kind) =>
            kind is EconomyFlowKind.ExchangeSale or EconomyFlowKind.BeggarGift or EconomyFlowKind.Guild;

        /// <summary>The kind's name in a line: lower case, as True165 wrote it (in_lootcoin, out_repair).</summary>
        public static string KeyName(EconomyFlowKind kind) => KindNames[Math.Clamp((int)kind, 0, KindNames.Length - 1)];

        // KindCount first: static fields initialise in declaration order.
        public static readonly int KindCount = MaxKind() + 1;

        private static readonly string[] KindNames = BuildKindNames();

        private static string[] BuildKindNames()
        {
            var names = new string[MaxKind() + 1];
            foreach (EconomyFlowKind kind in Enum.GetValues<EconomyFlowKind>())
                names[(int)kind] = kind.ToString().ToLowerInvariant();
            return names;
        }

        private static int MaxKind()
        {
            int max = 0;
            foreach (EconomyFlowKind kind in Enum.GetValues<EconomyFlowKind>())
                max = Math.Max(max, (int)kind);
            return max;
        }

        private sealed class FileMemberComparer : IEqualityComparer<(string File, string Member)>
        {
            public bool Equals((string File, string Member) a, (string File, string Member) b) =>
                string.Equals(a.File, b.File, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Member, b.Member, StringComparison.Ordinal);

            public int GetHashCode((string File, string Member) key) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.File ?? string.Empty), StringComparer.Ordinal.GetHashCode(key.Member ?? string.Empty));
        }
    }
}
