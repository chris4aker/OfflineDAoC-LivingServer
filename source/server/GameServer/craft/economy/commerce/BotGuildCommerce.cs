using System;
using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Crafting;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The guild side of crafting commerce (Crafting 2.0 WP8b, built out: what WP8 left as "not built"), behind
    /// bot_crafters and bot_crafting_social (both off by default):
    /// <list type="bullet">
    /// <item><b>Bot-to-bot guild orders.</b> A gamebot that commissions a piece (WP5's board) asks a crafter of its own guild
    /// first, and pays its guildmate the materials only (the fee waived, as WP8 does for a player guildmate); a
    /// Crafting-charter bot guild's bank pays its crafter that fee at the hand-over (WP8's PayGuildCrafterFee).</item>
    /// <item><b>The guild bank buys materials.</b> A Crafting-charter bot guild's bank pays for its crafters' materials at the
    /// merchant (the coin goes bank to crafter as a Guild transfer, then to the merchant as CraftMaterials), while the bank
    /// holds ten times the purchase.</item>
    /// <item><b>The guild bank buys raid potions.</b> A Raiding-charter bot guild's bank buys a heal draught for each of its
    /// raiders (level 40+) at a merchant visit of a task break, up to two carried (needs WP7's bot_upkeep, under which
    /// gamebots drink draughts at all): the coin goes bank to member (Guild) and member to merchant (Potion).</item>
    /// <item><b>Crafting guilds take crafters first</b> (AutonomousBotGuildPlanner.Place, Candidate.Crafter): unplaced crafter
    /// gamebots are placed before the others, and into a Crafting-charter guild that has room.</item>
    /// </list>
    /// A player's guild bank is never spent; companions are never in any of it. Log: ECON_GUILD_SPEND (what=crafter_fee,
    /// materials, raid_potion).
    /// </summary>
    public static class BotGuildCommerce
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotGuildCommerce));

        public static bool On => CraftingDrama.On;

        /// <summary>A bank pays for materials or a draught only while it holds this many times the price.</summary>
        public const int BankReserveMultiple = 10;

        /// <summary>The level from which a Raiding guild's member counts as a raider (the realm raids' own floor).</summary>
        public const int RaiderLevel = 40;

        /// <summary>A raider carries at most this many heal draughts from the bank.</summary>
        public const int RaidDraughts = 2;

        // ---- seams for tests ----
        public static Func<GameBot, Guild> GuildOf = AutonomousBotGuilds.CraftingGuildOf;
        public static Func<Guild, BotGuildCharter?> CharterOf = DefaultCharter;
        public static Func<long, long, EconomyFlowKind, bool> BotEarn = DefaultBotEarn;
        public static Func<long, long, EconomyFlowKind, bool> BotSpend = DefaultBotSpend;

        // The bank's coin is always a Guild transfer (the caller names it; never left unattributed).
        private static bool DefaultBotEarn(long id, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.AddMoney(id, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.Guild : kind);

        private static bool DefaultBotSpend(long id, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.TrySpend(id, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.Guild : kind);

        private static readonly ConcurrentDictionary<long, string> PotionBreak = new();
        private static int _registered;

        public static void ResetForTests()
        {
            GuildOf = AutonomousBotGuilds.CraftingGuildOf;
            CharterOf = DefaultCharter;
            BotEarn = DefaultBotEarn;
            BotSpend = DefaultBotSpend;
            PaidMaterialsOnly = DefaultPaidMaterialsOnly;
            PotionBreak.Clear();
        }

        private static BotGuildCharter? DefaultCharter(Guild guild) =>
            guild != null && AutonomousBotRealmGuilds.TryGetBotGuild(guild.GuildID, out AutonomousBotRealmGuilds.BotGuildInfo info) ? info.Charter : null;

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

        /// <summary>The seams in WP5's board and the raid-potion merchant step.</summary>
        public static void Register()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
                return;
            RegisterSeams();
            BotServiceSeams.Register(RaidPotionStep.Instance);
        }

        public static void RegisterSeams()
        {
            AutonomousBotCraftOrders.PreferCrafter = IsGuildmate;
            AutonomousBotCraftOrders.GuildPrice = GuildPrice;
            AutonomousBotCraftOrders.BotOrderDelivered = (crafter, buyer, order) => OnBotOrderDelivered(crafter, buyer, order);
        }

        #region Who

        /// <summary>Two autonomous gamebots of one guild (a bot guild, or a player's guild both were invited into).</summary>
        public static bool AreGuildmates(GameBot a, GameBot b)
        {
            if (!On || a == null || b == null || a == b)
                return false;
            Guild guild = GuildOf(a);
            return guild != null && ReferenceEquals(guild, GuildOf(b));
        }

        /// <summary>WP5's crafter preference: a crafter of the buyer's own guild first.</summary>
        public static bool IsGuildmate(GameBot crafter, long buyerBotId) =>
            On && crafter != null && buyerBotId > 0 && AutonomousBotCraftOrders.FindLive(buyerBotId) is { } buyer && AreGuildmates(crafter, buyer);

        public static bool IsCharter(Guild guild, BotGuildCharter charter) => guild != null && CharterOf(guild) == charter;

        #endregion

        #region Bot-to-bot guild orders

        /// <summary>WP5's price for a guildmate's order: the materials only (null for any other order: the board's quote).</summary>
        public static long? GuildPrice(GameBot buyer, GameBot crafter, CraftRecipeInfo recipe) =>
            recipe != null && AreGuildmates(buyer, crafter) ? Math.Max(1, recipe.MaterialCostCopper) : null;

        /// <summary>
        /// A bot's order delivered (WP5): when buyer and crafter share a Crafting-charter bot guild and the buyer paid the
        /// materials only (a guildmate's price, <see cref="GuildPrice"/>), the guild bank pays the crafter the fee it waived
        /// (WP8's CraftingDrama.BankFee: the board's fee, while the bank holds twice that). Returns the copper paid.
        /// </summary>
        public static long OnBotOrderDelivered(GameBot crafter, GameBot buyer, CraftOrder order)
        {
            if (!On || crafter == null || buyer == null || order == null || !AreGuildmates(crafter, buyer))
                return 0;
            Guild guild = GuildOf(crafter);
            if (!IsCharter(guild, BotGuildCharter.Crafting) || !PaidMaterialsOnly(order))
                return 0;
            try
            {
                long fee = CraftingDrama.BankFee(order.EscrowCopper, guild.GetGuildBank(), true);
                return fee <= 0 ? 0 : BankToMember(guild, crafter, fee, "crafter_fee", 2, buyer.Name);
            }
            catch (Exception exception)
            {
                Log.Warn($"Guild commerce: the fee for {crafter.Name}'s piece for {buyer.Name} failed", exception);
                return 0;
            }
        }

        /// <summary>Whether an order's escrow is its recipe's materials and no more (the guildmate's price).</summary>
        public static Func<CraftOrder, bool> PaidMaterialsOnly = DefaultPaidMaterialsOnly;

        private static bool DefaultPaidMaterialsOnly(CraftOrder order)
        {
            CraftRecipeInfo recipe = AutonomousBotCrafting.RecipesOf(order.Skill)
                .FirstOrDefault(r => string.Equals(r.ProductId, order.ProductId, StringComparison.OrdinalIgnoreCase) && r.Level == order.RecipeLevel);
            return recipe != null && order.EscrowCopper <= Math.Max(1, recipe.MaterialCostCopper);
        }

        #endregion

        #region The guild bank pays

        /// <summary>
        /// Takes <paramref name="copper"/> out of a bot guild's bank (keeping <see cref="BankReserveMultiple"/> times it) into
        /// a member's purse (Guild, a transfer within the supply). Returns the copper moved, 0 when the bank is short.
        /// </summary>
        public static long BankToMember(Guild guild, GameBot member, long copper, string what, int reserveMultiple = BankReserveMultiple,
            string forWhom = null)
        {
            if (guild == null || member == null || member.DatabaseID <= 0 || copper <= 0)
                return 0;
            lock (guild)
            {
                if (guild.GetGuildBank() < (double)copper * Math.Max(1, reserveMultiple) || !guild.AddToBank(-copper, false))
                    return 0;
            }
            if (!BotEarn(member.DatabaseID, copper, EconomyFlowKind.Guild))
            {
                lock (guild)
                    guild.AddToBank(copper, false);
                return 0;
            }
            if (Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconGuildSpend, ("guild", guild.Name), ("realm", member.Realm), ("what", what),
                    ("member", member.Name), ("memberId", member.DatabaseID), ("for", forWhom ?? member.Name), ("copper", copper),
                    ("bank", (long)guild.GetGuildBank())));
            return copper;
        }

        /// <summary>Back into the bank (a purchase that did not happen).</summary>
        public static void MemberToBank(Guild guild, GameBot member, long copper)
        {
            if (guild == null || member == null || copper <= 0)
                return;
            if (BotSpend(member.DatabaseID, copper, EconomyFlowKind.Guild))
                lock (guild)
                    guild.AddToBank(copper, false);
        }

        /// <summary>
        /// WP4's material purchase (AutonomousBotCrafting.TryBuy), before the crafter pays the merchant: a Crafting-charter
        /// bot guild's bank gives the crafter the material's price. Returns the copper the bank gave.
        /// </summary>
        public static long FundMaterials(GameBot crafter, long copper)
        {
            if (!On || crafter?.IsAutonomousWorldBot != true || crafter.IsTemporaryGroupHelper || crafter.IsHiredCompanion || copper <= 0)
                return 0;
            Guild guild = GuildOf(crafter);
            return IsCharter(guild, BotGuildCharter.Crafting) ? BankToMember(guild, crafter, copper, "materials") : 0;
        }

        /// <summary>
        /// A Raiding-charter guild's raider at a merchant visit of its task break: the bank buys it a heal draught, up to
        /// <see cref="RaidDraughts"/> carried, once a break.
        /// </summary>
        public sealed class RaidPotionStep : IBotServiceStep
        {
            public static readonly RaidPotionStep Instance = new();
            public string Name => "guild raid potions (WP8b)";
            public int Order => 105;
            public eWorldServiceKind Kind => eWorldServiceKind.Vendor;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                if (!On || !BotSinks.Upkeep(bot) || service is not GameMerchant merchant || bot.Level < RaiderLevel || bot.Inventory == null)
                    return false;
                Guild guild = GuildOf(bot);
                if (!IsCharter(guild, BotGuildCharter.Raiding))
                    return false;
                string breakId = BotSinks.BreakOf(bot);
                if (PotionBreak.TryGetValue(bot.DatabaseID, out string done) && done == breakId)
                    return false;
                PotionBreak[bot.DatabaseID] = breakId;
                if (BotPotions.Carried(bot, BotPotions.Kind.Heal) >= RaidDraughts)
                    return false;
                DbItemTemplate draught = BotPotions.DraughtsOnSale(bot, merchant, BotPotions.Kind.Heal).FirstOrDefault();
                if (draught == null || BankToMember(guild, bot, draught.Price, "raid_potion") <= 0)
                    return false;
                if (BotPotions.Buy(bot, draught, 1, EconomyFlowKind.Potion) <= 0)
                {
                    MemberToBank(guild, bot, draught.Price);
                    return false;
                }
                result = new BotServiceResult($"Got a {draught.Name} from the guild bank", $"{guild.Name} pays its raiders' heal draughts",
                    2_000, eAutonomousProgressKind.Money);
                return true;
            }
        }

        #endregion
    }
}
