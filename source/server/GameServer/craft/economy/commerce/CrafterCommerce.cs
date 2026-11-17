using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Crafting;
using DOL.GS.PacketHandler;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// A real player and crafter gamebots (Crafting 2.0 WP8, milestone M4; design doc §8), behind bot_crafter_commerce and
    /// bot_crafters (both off by default):
    /// <list type="bullet">
    /// <item>By /tell a player asks a crafter what it makes or sells, asks for a piece ("wtb ...", "can you make ..."), or a
    /// set; the crafter answers from its real skill, the recipes it makes surely and the crafted stock in its bag
    /// (<see cref="CrafterTalk"/>). A gamebot that does not craft says so and names one of the realm that can.</item>
    /// <item>A player's WTB in /trade is answered by tell by the realm's best crafter gamebot that can make it.</item>
    /// <item>A short haggle (<see cref="CrafterHaggle"/>), the customer's standing (<see cref="CrafterCustomers"/>).</item>
    /// <item>A "deal" places the piece on WP5's craft-order board (<see cref="AutonomousBotCraftOrders"/>, source Player):
    /// up front, the coin is dragged onto the crafter and held in the order's escrow; on delivery, it is paid in the trade
    /// window at the hand-over. The crafter makes it on its next craft trip like any order.</item>
    /// <item>At the crafter's natural task break (never during its task) a made piece is handed over to the player standing
    /// near it: given straight away when paid up front (the escrow then pays the crafter), in a trade window
    /// (<see cref="BotTradeWindow"/>) otherwise. Stock is sold in the trade window the same way.</item>
    /// <item>A lapsed order refunds an up-front player (at their next login when offline); a made pay-on-delivery piece
    /// never collected stays the crafter's own work and counts against the player.</item>
    /// </list>
    /// Only real players start this, only with autonomous gamebots (never a /bot companion). Every coin move names its kind:
    /// the player's escrow (EconomyFlows.RecordPlayerEscrow), the escrow to the crafter (CraftOrder, the board's), coin in a
    /// trade window (PlayerTrade, a transfer), the guild bank's fee (Guild). Log lines: ECON_PLAYER_ORDER,
    /// ECON_PLAYER_DELIVERY, ECON_HAGGLE (and ECON_GUILD_SPEND from the guild economy).
    /// </summary>
    /// <remarks>Written for this project: neither fork has player commerce with gamebots.</remarks>
    public static class CrafterCommerce
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(CrafterCommerce));

        /// <summary>bot_crafters and bot_crafter_commerce.</summary>
        public static bool Enabled => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_CRAFTER_COMMERCE;

        /// <summary>How long a player's order may wait on the board (a player is on a few hours at a time): 6 hours.</summary>
        public static readonly TimeSpan PlayerOrderLifetime = TimeSpan.FromHours(6);

        /// <summary>Within this distance a crafter at its break hands a piece over (about a /say's reach).</summary>
        public const int HandOverDistance = 512;

        /// <summary>A player's /trade WTB draws at most one crafter's tell in this time.</summary>
        public const long TradePostRestMs = 120_000;

        // ---- seams for tests (the defaults are the live game) ----
        public static Func<string, GamePlayer> FindPlayer = DefaultFindPlayer;
        public static Func<GamePlayer, long, bool> TakeCoin = (player, copper) => copper <= 0 || player.RemoveMoney(copper);
        public static Action<GamePlayer, long> GiveCoin = (player, copper) => player.AddMoney(copper);
        public static Action<GameBot, GamePlayer, string> SendTell = (bot, player, text) => AutonomousLivingServer.Tell(bot, player, text);
        public static Action<GameObject, int, Action> Schedule = (owner, delay, action) => AutonomousLivingServer.Later(owner, delay, null, action);
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        public static Func<GameBot, bool> AtBreak = bot => AutonomousObjectiveAssignments.IsBetweenPveTasks(bot);
        public static Func<GameBot, GamePlayer, bool> IsNear = DefaultIsNear;
        public static Func<GameBot, GamePlayer, bool> IsGuildmate = AutonomousBotGuilds.IsGuildmate;
        public static Func<GamePlayer, string> CustomerIdOf = player => CrafterCustomers.CustomerIdOf(player?.InternalID);

        private static readonly ConcurrentDictionary<(long Bot, string Customer), CrafterTalk.Deal> Deals = new();
        private static readonly ConcurrentDictionary<(long Bot, string Customer), CrafterTalk.Deal> AwaitingDeposit = new();
        private static readonly ConcurrentDictionary<string, string> ReadyTold = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, byte> HandingOver = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long> LastTradePost = new(StringComparer.OrdinalIgnoreCase);
        private static int _registered;

        private static GamePlayer DefaultFindPlayer(string characterId) =>
            string.IsNullOrEmpty(characterId) ? null
            : ClientService.Instance.GetPlayers().FirstOrDefault(player => player?.InternalID == characterId &&
                                                                           player.ObjectState == GameObject.eObjectState.Active);

        private static bool DefaultIsNear(GameBot bot, GamePlayer player) =>
            bot?.CurrentRegion != null && player?.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(player, HandOverDistance);

        #region Registration

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

        /// <summary>Hooks WP8 into the seams: the board's player orders, drag-and-drop onto a bot, the customers' table, logins.</summary>
        public static void Register()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
                return;
            RegisterSeams();
            BotTrades.Register();
            EconomyLifecycle.Register(CustomerStore.Instance);
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        }

        /// <summary>The seams alone (tests call this after resetting them).</summary>
        public static void RegisterSeams()
        {
            AutonomousBotCraftOrders.DeliverToPlayer = DeliverToPlayer;
            AutonomousBotCraftOrders.PlayerOrderClosed = OnPlayerOrderClosed;
            // Coin dropped on a bot: a deposit for a deal first (WP8), else a trade window with the coin in (the full bot trade).
            BotCommerceSeams.TryReceiveMoney = (bot, player, copper) =>
                TryReceiveMoney(bot, player, copper) || BotTrades.TryReceiveMoney(bot, player, copper);
            // An item dropped on a bot: a trade window with it in the player's side (sell, swap, a repair or spellcraft job).
            BotCommerceSeams.TryReceiveItem = BotTrades.TryReceiveItem;
        }

        private sealed class CustomerStore : IEconomyStore
        {
            public static readonly CustomerStore Instance = new();
            public string Name => "crafter customers";
            public void Load() => CrafterCustomers.Load();
            public void Pulse(long now)
            {
                if (Enabled)
                    PayOwedOnline();
            }

            public void Flush(bool wait) => CrafterCustomers.Flush(wait);
        }

        public static void ResetForTests()
        {
            Deals.Clear();
            AwaitingDeposit.Clear();
            ReadyTold.Clear();
            HandingOver.Clear();
            LastTradePost.Clear();
            FindPlayer = DefaultFindPlayer;
            TakeCoin = (player, copper) => copper <= 0 || player.RemoveMoney(copper);
            GiveCoin = (player, copper) => player.AddMoney(copper);
            SendTell = (bot, player, text) => AutonomousLivingServer.Tell(bot, player, text);
            Schedule = (owner, delay, action) => AutonomousLivingServer.Later(owner, delay, null, action);
            UtcNow = () => DateTime.UtcNow;
            AtBreak = bot => AutonomousObjectiveAssignments.IsBetweenPveTasks(bot);
            IsNear = DefaultIsNear;
            IsGuildmate = AutonomousBotGuilds.IsGuildmate;
            CustomerIdOf = player => CrafterCustomers.CustomerIdOf(player?.InternalID);
            OnlinePlayers = () => ClientService.Instance.GetPlayers();
            _owedPending = true;
            BotTradeWindow.ResetForTests();
        }

        #endregion

        #region Who and what (live facts)

        /// <summary>An autonomous gamebot (never a /bot companion or a hired companion) the player may trade with: same realm,
        /// in the game.</summary>
        public static bool IsCommerceBot(GameBot bot, GamePlayer player) =>
            bot != null && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsHiredCompanion && bot.DatabaseID > 0 && player != null &&
            bot.Realm == player.Realm;

        /// <summary>The recipes this crafter makes surely, for its realm, with materials on sale there.</summary>
        public static IReadOnlyList<CraftRecipeInfo> MakeableOf(GameBot bot)
        {
            string skills = bot?.PersistentRecord?.SerializedCraftingSkills;
            if (string.IsNullOrWhiteSpace(skills))
                return [];
            return BotCraftingSkills.Parse(skills).Select(skill => skill.Key)
                .Where(skill => Array.IndexOf(AutonomousBotCraftOrders.GearCrafts, skill) >= 0)
                .SelectMany(skill => CrafterQuotes.Makeable(AutonomousBotCrafting.RecipesOf(skill), skills, bot.Realm,
                    ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS))
                .Where(recipe => AutonomousBotCraftOrders.OnSale(recipe, bot.Realm))
                .ToArray();
        }

        /// <summary>The crafted pieces it holds and would sell: its own work, not held for an order.</summary>
        public static IReadOnlyList<CrafterTalk.StockPiece> StockOf(GameBot bot) =>
            bot?.Inventory == null ? []
            : bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .Where(item => item != null && AutonomousBotCrafting.IsOwnWork(bot, item) && !AutonomousBotCraftOrders.IsHeldForOrder(item) &&
                               !EconomySeams.IsReserved(bot, item))
                .Select(item => new CrafterTalk.StockPiece(item.ObjectId, item.Name, item.Quality, item.Level,
                    Math.Max(1, AutonomousBotEconomy.RecommendListingPrice(item)), item.Object_Type, item.Item_Type))
                .ToArray();

        public static CrafterTalk.CrafterFacts FactsOf(GameBot bot, DateTime utcNow)
        {
            string skills = bot.PersistentRecord?.SerializedCraftingSkills ?? string.Empty;
            CraftingDrama.CraftFacts facts = CraftingDrama.FactsOf(skills);
            IReadOnlyList<CraftRecipeInfo> makeable = MakeableOf(bot);
            bool atBreak = AtBreak(bot);
            double toBreak = atBreak ? 0 : MinutesToBreak(bot.PersistentRecord, bot.Group != null, utcNow);
            CraftRecipeInfo sample = makeable.OrderByDescending(r => r.ProductLevel).FirstOrDefault();
            int seconds = 60;
            try
            {
                if (sample != null)
                    seconds = AutonomousBotCrafting.WorkSeconds(bot, new BotCraftingRules.Session(sample, sample.Skill, 1));
            }
            catch { } // tests without a world: a minute a piece
            return new CrafterTalk.CrafterFacts(bot.Name, bot.Realm, skills, facts.Primary, facts.Skill, facts.Title, makeable, StockOf(bot),
                toBreak, atBreak, seconds, AutonomousLivingServer.PlaceOf(bot), AutonomousBotPersonality.For(bot).Traits.Patience,
                FreeToTrade: FreeToTrade(bot));
        }

        /// <summary>Minutes to the end of the bot's task (its next natural break); an hour while grouped or unknown. Pure.</summary>
        public static double MinutesToBreak(OfflineWorldBotRecord record, bool grouped, DateTime utcNow)
        {
            if (record == null || grouped)
                return 60;
            return DateTime.TryParse(record.ObjectiveExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime end)
                ? Math.Clamp((end.ToUniversalTime() - utcNow).TotalMinutes, 0, 180)
                : 60;
        }

        public static CrafterTalk.CustomerFacts CustomerOf(GameBot bot, GamePlayer player)
        {
            string id = CustomerIdOf(player);
            OfflineCrafterCustomerRecord row = CrafterCustomers.Get(bot.DatabaseID, id);
            string mood = null;
            try
            {
                if (AutonomousLivingServer.TryRecall(bot, player, out OfflineBotMemoryRecord memory))
                    mood = AutonomousLivingServer.MoodOf(memory);
            }
            catch { } // no memory store (tests)
            CrafterCustomers.Standing standing = CrafterCustomers.StandingOf(row?.Deals ?? 0, row?.Stiffed ?? 0, mood == "cool", mood == "friendly");
            bool guildmate = CraftingDrama.On && IsGuildmate(bot, player);
            return new CrafterTalk.CustomerFacts(id, player.Name, standing, CrafterCustomers.DiscountPercent(standing, row?.SpentCopper ?? 0),
                guildmate, IsNear(bot, player), row?.SpentCopper ?? 0);
        }

        public static IReadOnlyList<CrafterTalk.OrderView> OrdersOf(GameBot crafter, string customerId) =>
            AutonomousBotCraftOrders.OfBuyer(customerId).Where(order => order.CrafterBotId == crafter.DatabaseID &&
                                                                        order.Source == CraftOrderSource.Player)
                .Select(order => new CrafterTalk.OrderView(order.OrderId, order.ProductName, order.State, order.EscrowCopper, order.PayUpFront))
                .ToArray();

        #endregion

        #region Tells and /trade

        /// <summary>
        /// A real player's tell to a gamebot (AutonomousLivingServer.OnCommerceTell, from the chat coordinator after the
        /// beggars). True when it was about trade with the bot and an answer is on its way; false leaves the tell to the
        /// ordinary reply.
        /// </summary>
        public static bool OnPlayerTell(GamePlayer player, GameBot bot, string message)
        {
            if (!Enabled || player?.Client == null || !IsCommerceBot(bot, player) || string.IsNullOrWhiteSpace(message))
                return false;
            try
            {
                using var aiProfile = AiProfile.Scope(AiSystem.CraftCommerce); // AI_PROFILE: measures only
                // The full bot trade: any gamebot answers "what are you selling", "trade me", "buy 2" (BotTrades.OnTell).
                if (!AutonomousBotCrafting.IsCrafter(bot) || !AutonomousBotCrafting.IsTrained(bot))
                    return BotTrades.OnTell(player, bot, message) || Refer(player, bot, message);
                CrafterTalk.Reply reply = Handle(player, bot, message);
                if (reply == null)
                    return BotTrades.OnTell(player, bot, message);
                Say(bot, player, reply, 1_500 + Math.Min(4_000, message.Length * 30));
                return true;
            }
            catch (Exception exception)
            {
                Log.Warn($"Crafter commerce: tell from {player.Name} to {bot.Name} failed", exception);
                return false;
            }
        }

        /// <summary>Works out the crafter's answer and does what it says (the deal, the order, the trade). Game loop.</summary>
        public static CrafterTalk.Reply Handle(GamePlayer player, GameBot bot, string message)
        {
            DateTime now = UtcNow();
            CrafterTalk.CustomerFacts customer = CustomerOf(bot, player);
            var key = (bot.DatabaseID, customer.Id);
            Deals.TryGetValue(key, out CrafterTalk.Deal open);
            IReadOnlyList<CrafterTalk.OrderView> orders = OrdersOf(bot, customer.Id);
            CrafterTalk.CrafterFacts facts = FactsOf(bot, now);
            CrafterTalk.Reply reply = CrafterTalk.Answer(facts, customer, message, open, orders, now);
            if (reply == null)
                return null;
            switch (reply.Act)
            {
                case CrafterTalk.Act.Quote:
                    Deals[key] = reply.Deal;
                    break;
                case CrafterTalk.Act.Haggle:
                    Deals[key] = reply.Deal;
                    if (Log.IsInfoEnabled)
                        Log.Info(EconomyLog.Line(EconomyLog.EconHaggle, ("crafter", bot.Name), ("crafterId", bot.DatabaseID), ("realm", bot.Realm),
                            ("player", player.Name), ("item", open?.ItemText ?? string.Empty), ("asked", open?.Ask ?? 0), ("now", reply.Deal?.Ask ?? 0),
                            ("round", reply.Deal?.Round ?? 0), ("result", reply.Topic.Replace("craft_haggle_", string.Empty)),
                            ("standing", customer.Standing)));
                    break;
                case CrafterTalk.Act.PlaceOrder:
                    Deals.TryRemove(key, out _);
                    if (Place(bot, player, customer, reply.Deal, upFront: false) == null)
                        return reply with { Text = "cant take that on right now, try me later", Act = CrafterTalk.Act.Cannot, Topic = "craft_full" };
                    break;
                case CrafterTalk.Act.AwaitDeposit:
                    Deals.TryRemove(key, out _);
                    AwaitingDeposit[key] = reply.Deal with { UntilUtc = now + CrafterTalk.DealLifetime };
                    break;
                case CrafterTalk.Act.OpenTrade:
                    Deals.TryRemove(key, out _);
                    CrafterTalk.Deal sale = reply.Deal;
                    Schedule(bot, 2_500, () => OpenStockTrade(bot, player, sale));
                    break;
                case CrafterTalk.Act.Status when reply.Topic == "craft_ready_here":
                    Schedule(bot, 2_500, () => HandOver(bot, player));
                    break;
                case CrafterTalk.Act.Cancel:
                    CrafterTalk.OrderView cancel = orders.FirstOrDefault(o => o.State == CraftOrderState.Open);
                    if (cancel == null || !AutonomousBotCraftOrders.TryCancelOpen(cancel.OrderId, "the player cancelled"))
                        return reply with { Text = "cant cancel that one now", Act = CrafterTalk.Act.Status, Topic = "craft_status" };
                    break;
                case CrafterTalk.Act.Declined:
                case CrafterTalk.Act.Refuse:
                    Deals.TryRemove(key, out _);
                    AwaitingDeposit.TryRemove(key, out _);
                    break;
            }
            return reply;
        }

        /// <summary>A gamebot that does not craft, asked for a piece: it says so and names a crafter of the realm that makes it.</summary>
        private static bool Refer(GamePlayer player, GameBot bot, string message)
        {
            if (!CrafterTalk.AsksForPiece(message) && !CrafterTalk.AsksWhatYouMake(message))
                return false;
            CrafterQuotes.Request request = CrafterQuotes.Parse(message);
            GameBot other = request.Names ? BestCrafterFor(player, request, exclude: bot.DatabaseID) : null;
            string text = other != null
                ? $"not a crafter, sorry. try {other.Name}, {CraftingDrama.FactsOf(other.PersistentRecord?.SerializedCraftingSkills).Title.ToLowerInvariant()}"
                : Random.Shared.Next(2) == 0 ? "not a crafter, sorry" : "i dont craft, ask around in /trade";
            var reply = new CrafterTalk.Reply(text, CrafterTalk.Act.Cannot, null, other != null ? "craft_refer" : "craft_not_crafter",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = player.Name, ["other"] = other?.Name ?? string.Empty });
            Say(bot, player, reply, 2_000 + Random.Shared.Next(2_000));
            return true;
        }

        /// <summary>The realm's best crafter gamebot in the game that can make the request (stock first, then skill).</summary>
        public static GameBot BestCrafterFor(GamePlayer player, CrafterQuotes.Request request, long exclude = 0) =>
            AutonomousBotRegistry.Snapshot()
                .Where(bot => bot.DatabaseID != exclude && IsCommerceBot(bot, player) && AutonomousBotCrafting.IsCrafter(bot) &&
                              AutonomousBotCrafting.IsTrained(bot) && AutonomousLivingServer.CanTalk(bot))
                .Select(bot => (Bot: bot, Stock: StockOf(bot).Any(piece => CrafterQuotes.Score(request, StockAsRecipe(piece)) > 0),
                    Recipe: CrafterQuotes.Best(request, MakeableOf(bot))))
                .Where(c => c.Stock || c.Recipe != null)
                .OrderByDescending(c => c.Stock)
                .ThenByDescending(c => CraftingDrama.FactsOf(c.Bot.PersistentRecord?.SerializedCraftingSkills).Skill)
                .Select(c => c.Bot)
                .FirstOrDefault();

        private static CraftRecipeInfo StockAsRecipe(CrafterTalk.StockPiece piece) =>
            new(piece.ObjectId, eCraftingSkill.NoCrafting, 0, piece.ObjectId, piece.Name, piece.ObjectType, piece.ItemType, piece.Level, 0, true,
                false, Array.Empty<CraftMaterial>());

        /// <summary>
        /// A player's /trade post: a WTB the realm's crafters can fill is answered by tell from the best one (stock first),
        /// a few seconds later, as a quote the player can take ("deal") or haggle. Once per player in two minutes.
        /// </summary>
        public static void OnPlayerTradePost(GamePlayer player, string message)
        {
            if (!Enabled || player?.Client == null || string.IsNullOrWhiteSpace(message) || !BuysInTrade(message))
                return;
            try
            {
                long now = GameLoop.GameLoopTime;
                if (LastTradePost.TryGetValue(player.Name, out long last) && now - last < TradePostRestMs)
                    return;
                CrafterQuotes.Request request = CrafterQuotes.Parse(message);
                if (!request.Names)
                    return;
                GameBot crafter = BestCrafterFor(player, request);
                if (crafter == null)
                    return;
                LastTradePost[player.Name] = now;
                Schedule(crafter, 5_000 + Random.Shared.Next(10_000), () =>
                {
                    if (!AutonomousLivingServer.CanTalk(crafter) || player.ObjectState != GameObject.eObjectState.Active)
                        return;
                    CrafterTalk.Reply reply = Handle(player, crafter, "can you make " + message);
                    if (reply != null)
                        Say(crafter, player, reply, 0);
                });
            }
            catch (Exception exception)
            {
                Log.Warn($"Crafter commerce: /trade post of {player.Name} failed", exception);
            }
        }

        /// <summary>A /trade line that buys ("wtb", "lf", "need", "anyone make"), not one that sells. Pure.</summary>
        public static bool BuysInTrade(string message)
        {
            string t = (message ?? string.Empty).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(wtb|lf|lfm|need|needs|looking for|anyone (make|craft|got|have)|who (makes|can make|crafts)|buying)\b") &&
                   !System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(wts|selling|for sale)\b");
        }

        /// <summary>Says a reply by tell: a corpus line of its topic with its slots (bot voices on), else the fixed text.</summary>
        private static void Say(GameBot bot, GamePlayer player, CrafterTalk.Reply reply, int delayMs)
        {
            string text = Voiced(bot, reply);
            if (string.IsNullOrWhiteSpace(text))
                return;
            Schedule(bot, Math.Max(1, delayMs), () =>
            {
                if (player.ObjectState == GameObject.eObjectState.Active)
                    SendTell(bot, player, text);
            });
        }

        public static string Voiced(GameBot bot, CrafterTalk.Reply reply)
        {
            try
            {
                if (reply.Topic != null && AutonomousBotVoices.TryLine(bot, "craft_reply", out VC.Picked picked, topic: reply.Topic, slots: reply.Slots,
                        requireNeed: null) && !string.IsNullOrWhiteSpace(picked.Text))
                    return picked.Text;
                return AutonomousBotVoices.StyleFor(bot, reply.Text);
            }
            catch
            {
                return reply.Text;
            }
        }

        #endregion

        #region Orders on the board

        /// <summary>
        /// Puts the deal's pieces on the craft-order board as the player's orders (source Player, one order a piece). Up front,
        /// the whole price is taken from the player first and held in the orders' escrow (outside the money supply; the
        /// ledger sees the player's side through RecordPlayerEscrow); a piece the board refuses is refunded. Returns the
        /// orders placed, or null when none was.
        /// </summary>
        public static IReadOnlyList<CraftOrder> Place(GameBot crafter, GamePlayer player, CrafterTalk.CustomerFacts customer, CrafterTalk.Deal deal,
            bool upFront)
        {
            if (deal == null || deal.Kind == CrafterTalk.DealKind.Stock || deal.Recipes.Count == 0 || crafter == null || player == null)
                return null;
            long total = deal.Ask;
            if (upFront)
            {
                if (!TakeCoin(player, total))
                    return null;
                EconomyFlows.RecordPlayerEscrow(player.Realm, -total);
            }
            long[] shares = Shares(deal, total);
            DateTime now = UtcNow();
            var placed = new List<CraftOrder>();
            long refused = 0;
            for (int i = 0; i < deal.Recipes.Count; i++)
            {
                CraftRecipeInfo recipe = deal.Recipes[i];
                var order = new CraftOrder(Guid.NewGuid().ToString("N"), crafter.Realm, CraftOrderSource.Player, 0, customer.Id, player.Name,
                    crafter.DatabaseID, recipe.Skill, recipe.ProductId ?? string.Empty, recipe.ProductName ?? string.Empty, recipe.Level, 0, 1,
                    shares[i], upFront, CraftOrderState.Open, string.Empty, now, now + PlayerOrderLifetime);
                if (AutonomousBotCraftOrders.TryAdd(order))
                    placed.Add(order);
                else
                    refused += shares[i];
            }
            if (upFront && refused > 0)
            {
                GiveCoin(player, refused);
                EconomyFlows.RecordPlayerEscrow(player.Realm, refused);
            }
            if (placed.Count == 0)
                return null;
            crafter.MarkAutonomousStateDirty();
            if (Log.IsInfoEnabled)
                foreach (CraftOrder order in placed)
                    Log.Info(EconomyLog.Line(EconomyLog.EconPlayerOrder, ("outcome", "placed"), ("order", order.OrderId), ("realm", crafter.Realm),
                        ("player", player.Name), ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("craft", order.Skill),
                        ("recipeLevel", order.RecipeLevel), ("item", order.ProductName), ("copper", order.EscrowCopper),
                        ("pay", upFront ? "upfront" : "delivery"), ("guildmate", customer.Guildmate), ("standing", customer.Standing)));
            return placed;
        }

        /// <summary>The price split over the pieces by their own prices, the remainder on the last. Pure.</summary>
        public static long[] Shares(CrafterTalk.Deal deal, long total)
        {
            int n = deal.Recipes.Count;
            var shares = new long[n];
            long[] weights = deal.Recipes.Select(r => Math.Max(1, AutonomousBotCraftOrders.Quote(r))).ToArray();
            long sum = weights.Sum(), given = 0;
            for (int i = 0; i < n - 1; i++)
            {
                shares[i] = total * weights[i] / sum;
                given += shares[i];
            }
            shares[n - 1] = total - given;
            return shares;
        }

        /// <summary>
        /// Coin dragged onto a crafter (BotCommerceSeams.TryReceiveMoney): the up-front payment of a deal the player took.
        /// Only the price is taken, never more; short coin is not taken and the crafter says what it costs.
        /// </summary>
        public static bool TryReceiveMoney(GameBot bot, GamePlayer player, long copper)
        {
            if (!Enabled || !IsCommerceBot(bot, player) || player.Client == null)
                return false;
            var key = (bot.DatabaseID, CustomerIdOf(player));
            if (!AwaitingDeposit.TryGetValue(key, out CrafterTalk.Deal deal) || deal.UntilUtc <= UtcNow())
                return false;
            CrafterTalk.CustomerFacts customer = CustomerOf(bot, player);
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["price"] = CrafterTalk.PriceText(deal.Ask), ["item"] = deal.ItemText };
            if (copper < deal.Ask)
            {
                Say(bot, player, new CrafterTalk.Reply($"thats short, its {slots["price"]}", CrafterTalk.Act.None, deal, "craft_short", slots), 1_200);
                return true;
            }
            if (!AwaitingDeposit.TryRemove(key, out _))
                return true;
            IReadOnlyList<CraftOrder> placed = Place(bot, player, customer, deal, upFront: true);
            if (placed == null)
            {
                Say(bot, player, new CrafterTalk.Reply("cant take that on right now, keep your coin", CrafterTalk.Act.Cannot, null, "craft_full", slots), 1_200);
                return true;
            }
            player.Out?.SendMessage($"You give {Money.GetString(deal.Ask)} to {bot.Name}, held for your order.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            int minutes = CrafterQuotes.MinutesUntilReady(MinutesToBreak(bot.PersistentRecord, bot.Group != null, UtcNow()), 60, placed.Count);
            slots["when"] = CrafterQuotes.TimeText(minutes);
            Say(bot, player, new CrafterTalk.Reply($"got it, the {slots["item"]} will be ready in {slots["when"]}", CrafterTalk.Act.PlaceOrder, null,
                "craft_paid", slots), 1_500);
            return true;
        }

        #endregion

        #region Delivery

        /// <summary>
        /// The board's hand-over of a made player piece (AutonomousBotCraftOrders.DeliverToPlayer, at the crafter's task
        /// break, from its brain turn): with the player in the game and near, the hand-over is scheduled on the game loop;
        /// otherwise the crafter tells the player once per break that it is done and where it is. Always false here: the
        /// order closes when the hand-over completes.
        /// </summary>
        public static bool DeliverToPlayer(GameBot crafter, CraftOrder order, DbInventoryItem piece)
        {
            if (!Enabled || crafter == null || order == null || !AtBreak(crafter))
                return false;
            // A brain turn runs in parallel: the player is looked up and the piece moved on the game loop, once per buyer.
            string key = crafter.DatabaseID + "|" + order.BuyerId;
            if (HandingOver.TryAdd(key, 0))
                Schedule(crafter, 1_000, () =>
                {
                    try { DeliverOnLoop(crafter, order); }
                    finally { HandingOver.TryRemove(key, out _); }
                });
            return false;
        }

        private static void DeliverOnLoop(GameBot crafter, CraftOrder order)
        {
            GamePlayer player = FindPlayer(CrafterCustomers.CharacterIdOf(order.BuyerId));
            if (player == null || player.Realm != crafter.Realm || !AtBreak(crafter))
                return;
            if (IsNear(crafter, player))
            {
                HandOver(crafter, player);
                return;
            }
            string breakId = crafter.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;
            if (ReadyTold.TryGetValue(order.OrderId, out string told) && told == breakId)
                return;
            ReadyTold[order.OrderId] = breakId;
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["item"] = order.ProductName.ToLowerInvariant(), ["zone"] = AutonomousLivingServer.PlaceOf(crafter), ["customer"] = player.Name,
            };
            Say(crafter, player, new CrafterTalk.Reply($"your {slots["item"]} is done, im in {slots["zone"]} for a bit, come by and say ready",
                CrafterTalk.Act.Status, null, "craft_ready_tell", slots), 1_000);
        }

        /// <summary>
        /// The hand-over, on the game loop, at the crafter's break with the player near: the made pieces paid up front are
        /// given at once (the escrow then pays the crafter, CraftOrder); those to pay on delivery go into one trade window
        /// at their price. Returns how many pieces were given at once.
        /// </summary>
        public static int HandOver(GameBot crafter, GamePlayer player)
        {
            if (!Enabled || crafter?.Inventory == null || player == null || !AtBreak(crafter) || !IsNear(crafter, player))
                return 0;
            string customerId = CustomerIdOf(player);
            CraftOrder[] made = AutonomousBotCraftOrders.OfBuyer(customerId)
                .Where(order => order.CrafterBotId == crafter.DatabaseID && order.Source == CraftOrderSource.Player && order.State == CraftOrderState.Made)
                .ToArray();
            if (made.Length == 0)
                return 0;
            DbInventoryItem[] bag = crafter.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Where(i => i != null).ToArray();
            DbInventoryItem PieceOf(CraftOrder order) => bag.FirstOrDefault(item => item.ObjectId == order.ItemObjectId);
            int given = 0;
            foreach (CraftOrder order in made.Where(o => o.PayUpFront))
            {
                DbInventoryItem piece = PieceOf(order);
                if (piece == null)
                    continue;
                var gift = new BotTradeWindow(player, crafter, [piece], 0, order.OrderId);
                if (!BotTradeWindow.Execute(gift))
                    continue;
                AutonomousBotCraftOrders.Remove(order);
                bool paid = AutonomousBotCraftOrders.PayCoin(crafter.DatabaseID, order.EscrowCopper, EconomyFlowKind.CraftOrder);
                Delivered(crafter, player, order, piece, order.EscrowCopper, paid, "upfront");
                given++;
            }
            CraftOrder[] unpaid = made.Where(o => !o.PayUpFront && PieceOf(o) != null).Take(10).ToArray();
            if (unpaid.Length > 0)
            {
                long price = unpaid.Sum(o => o.EscrowCopper);
                BotTradeWindow window = BotTradeWindow.Open(player, crafter, unpaid.Select(PieceOf), price, string.Join(",", unpaid.Select(o => o.OrderId)),
                    completed => OnDeliveryTraded(crafter, player, unpaid, completed));
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["item"] = unpaid.Length == 1 ? unpaid[0].ProductName.ToLowerInvariant() : $"{unpaid.Length} pieces", ["price"] = CrafterTalk.PriceText(price),
                };
                Say(crafter, player, window != null
                    ? new CrafterTalk.Reply($"here's your {slots["item"]}, {slots["price"]} in the trade window", CrafterTalk.Act.OpenTrade, null, "craft_handover_trade", slots)
                    : new CrafterTalk.Reply("close your trade window and say ready", CrafterTalk.Act.Status, null, "craft_trade_busy", slots), 300);
            }
            if (given > 0)
            {
                AutonomousBotEconomy.MarkInventoryChanged(crafter);
                crafter.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(crafter, true);
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["item"] = given == 1 ? made.First(o => o.PayUpFront).ProductName.ToLowerInvariant() : $"{given} pieces" };
                Say(crafter, player, new CrafterTalk.Reply($"here you go, your {slots["item"]}. enjoy", CrafterTalk.Act.Status, null, "craft_delivered", slots), 300);
            }
            return given;
        }

        private static void OnDeliveryTraded(GameBot crafter, GamePlayer player, IReadOnlyList<CraftOrder> orders, BotTradeWindow window)
        {
            foreach (CraftOrder order in orders)
            {
                AutonomousBotCraftOrders.Remove(order);
                DbInventoryItem piece = window.Pieces.FirstOrDefault(p => p.ObjectId == order.ItemObjectId);
                Delivered(crafter, player, order, piece, order.EscrowCopper, true, "delivery");
            }
        }

        private static void Delivered(GameBot crafter, GamePlayer player, CraftOrder order, DbInventoryItem piece, long copper, bool paid, string pay)
        {
            string customerId = order.BuyerId;
            CrafterCustomers.NoteDeal(crafter.DatabaseID, customerId, player.Name, copper, UtcNow());
            BotCrafterRoster.Instance.NoteEarned(crafter.DatabaseID, copper);
            ReadyTold.TryRemove(order.OrderId, out _);
            long bankFee = 0;
            if (CraftingDrama.On && IsGuildmate(crafter, player))
                bankFee = AutonomousBotGuildEconomy.PayGuildCrafterFee(player.Guild, crafter, order.EscrowCopper, player.Name);
            if (Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconPlayerDelivery, ("source", "order"), ("order", order.OrderId), ("realm", crafter.Realm),
                    ("player", player.Name), ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("item", piece?.Name ?? order.ProductName),
                    ("quality", piece?.Quality ?? 0), ("copper", copper), ("pay", pay), ("paid", paid), ("guildFee", bankFee)));
        }

        /// <summary>
        /// The full bot trade's busy rule (BotTrades): a crafter not fighting, casting, dead or on raid duty may trade a
        /// piece of its stock with a player who asks, mid-task (the player's action); its own selling stays at its breaks.
        /// </summary>
        public static bool FreeToTrade(GameBot bot)
        {
            try { return BotTrades.BusyOf(bot) == BotTrades.Busy.Free; }
            catch { return false; }
        }

        /// <summary>A stock piece the player agreed to buy: a trade window with the piece at its price (the player near, the bot free).</summary>
        public static BotTradeWindow OpenStockTrade(GameBot crafter, GamePlayer player, CrafterTalk.Deal deal)
        {
            if (!Enabled || deal?.Piece == null || crafter?.Inventory == null || !AtBreak(crafter) && !FreeToTrade(crafter) || !IsNear(crafter, player))
                return null;
            DbInventoryItem piece = crafter.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .FirstOrDefault(item => item != null && item.ObjectId == deal.Piece.ObjectId);
            if (piece == null || AutonomousBotCraftOrders.IsHeldForOrder(piece))
                return null;
            return BotTradeWindow.Open(player, crafter, [piece], deal.Ask, "stock:" + piece.ObjectId, window =>
            {
                CrafterCustomers.NoteDeal(crafter.DatabaseID, CustomerIdOf(player), player.Name, deal.Ask, UtcNow());
                BotCrafterRoster.Instance.NoteEarned(crafter.DatabaseID, deal.Ask);
                if (Log.IsInfoEnabled)
                    Log.Info(EconomyLog.Line(EconomyLog.EconPlayerDelivery, ("source", "stock"), ("realm", crafter.Realm), ("player", player.Name),
                        ("crafter", crafter.Name), ("crafterId", crafter.DatabaseID), ("item", piece.Name), ("quality", piece.Quality),
                        ("copper", deal.Ask), ("pay", "trade"), ("paid", true)));
            });
        }

        #endregion

        #region Lapsed orders and refunds

        /// <summary>
        /// A player's order left the board without a delivery (AutonomousBotCraftOrders.PlayerOrderClosed): the escrow back to
        /// the player (in the game now, else at their next login: CrafterCustomers' OwedCopper, still outside the supply), and a
        /// made pay-on-delivery piece that was never collected counts against the player. The piece stays its maker's work.
        /// </summary>
        public static void OnPlayerOrderClosed(CraftOrder order, bool refund, string outcome)
        {
            if (order == null)
                return;
            try
            {
                string result = outcome;
                if (refund && order.PayUpFront && order.EscrowCopper > 0)
                {
                    // The expiry pass runs off the game loop: the refund is owed (saved, still outside the supply) and paid on
                    // the game loop, within a second to a player in the game (PayOwedOnline) or at the next login.
                    CrafterCustomers.AddOwed(order.CrafterBotId, order.BuyerId, order.BuyerName, order.EscrowCopper);
                    _owedPending = true;
                    result = outcome + "_refund";
                }
                else if (!order.PayUpFront && order.State == CraftOrderState.Made && outcome == "expired")
                {
                    CrafterCustomers.NoteStiffed(order.CrafterBotId, order.BuyerId, order.BuyerName, UtcNow());
                    result = "uncollected";
                }
                ReadyTold.TryRemove(order.OrderId, out _);
                if (Log.IsInfoEnabled)
                    Log.Info(EconomyLog.Line(EconomyLog.EconPlayerOrder, ("outcome", result), ("order", order.OrderId), ("realm", order.Realm),
                        ("player", order.BuyerName), ("crafterId", order.CrafterBotId), ("item", order.ProductName),
                        ("state", CraftOrders.StateText(order.State)), ("copper", order.EscrowCopper), ("pay", order.PayUpFront ? "upfront" : "delivery")));
            }
            catch (Exception exception)
            {
                Log.Warn($"Crafter commerce: closing player order {order.OrderId} failed", exception);
            }
        }

        private static volatile bool _owedPending = true;

        /// <summary>The players in the game (replaceable by tests).</summary>
        public static Func<IEnumerable<GamePlayer>> OnlinePlayers = () => ClientService.Instance.GetPlayers();

        /// <summary>Game loop (the stores' pulse): pays the refunds owed to players in the game. Returns the copper paid.</summary>
        public static long PayOwedOnline()
        {
            if (!_owedPending)
                return 0;
            _owedPending = false;
            long paid = 0;
            foreach (GamePlayer player in OnlinePlayers() ?? [])
                paid += PayOwed(player);
            return paid;
        }

        private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is GamePlayer player)
                PayOwed(player);
        }

        /// <summary>Pays a returning player every refund owed to them (orders that lapsed while they were away).</summary>
        public static long PayOwed(GamePlayer player)
        {
            if (player == null)
                return 0;
            string id = CustomerIdOf(player);
            if (CrafterCustomers.Of(id).All(row => row.OwedCopper <= 0))
                return 0;
            long owed = CrafterCustomers.TakeOwed(id);
            if (owed <= 0)
                return 0;
            GiveCoin(player, owed);
            EconomyFlows.RecordPlayerEscrow(player.Realm, owed);
            player.Out?.SendMessage($"A crafter's order of yours was not delivered (lapsed or cancelled): {Money.GetString(owed)} returned.",
                eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
            if (Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconPlayerOrder, ("outcome", "owed_paid"), ("realm", player.Realm), ("player", player.Name),
                    ("copper", owed)));
            return owed;
        }

        #endregion

        #region Guild crafters (8b)

        /// <summary>The guild's best real crafter among these members, and its answer to "anyone craft?" (null when none crafts).</summary>
        public static (GameBot Bot, string Text)? GuildCrafterReply(IEnumerable<GameBot> members, Guild guild, Random random)
        {
            GameBot crafter = (members ?? []).Where(bot => AutonomousBotCrafting.IsCrafter(bot) && AutonomousBotCrafting.IsTrained(bot))
                .OrderByDescending(bot => CraftingDrama.FactsOf(bot.PersistentRecord?.SerializedCraftingSkills).Skill)
                .FirstOrDefault();
            if (crafter == null)
                return null;
            CraftingDrama.CraftFacts facts = CraftingDrama.FactsOf(crafter.PersistentRecord?.SerializedCraftingSkills);
            if (!facts.Trained)
                return null;
            int highest = CrafterQuotes.Range(MakeableOf(crafter)).Highest;
            string text = CraftingDrama.GuildCrafterAnswer(facts, highest, AutonomousBotGuildEconomy.IsCraftingBotGuild(guild), random);
            return (crafter, AutonomousBotVoices.StyleFor(crafter, text));
        }

        #endregion
    }
}
