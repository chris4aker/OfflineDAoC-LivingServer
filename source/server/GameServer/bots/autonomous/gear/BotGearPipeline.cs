using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Crafting 2.0 WP6 (M3), demand and the gear pipeline: the design's second shopping trigger (CRAFTING-AND-ECONOMY.md
    /// §7.2, facet M). At every natural task break a gamebot checks its six armour slots (<see cref="FindGaps"/>): a slot
    /// empty, a piece a full level band (5 levels) or more below the bot, or, at 50, a slot without its template piece
    /// (crafted, 99% or better). With a gap and a piece on its realm's Exchange that fills it, raises its set score
    /// (<see cref="BotGearSet"/>) and fits 80% of its purse, it asks for the town stop and buys it there (the Exchange step
    /// runs after the listing and before the ordinary purchase); with none, it posts a want-to-buy order for a crafted piece
    /// on WP0's <see cref="ICraftOrderBoard"/> (WP5's board; with no board registered it posts nothing and counts it).
    /// The existing full-bag visit is unchanged.
    /// <para>
    /// Owner rules: everything here is a purchase from the bot's own purse (the Exchange's TryBuy; an order's escrow is
    /// taken by the board, as DividedRealms' Place does), so gamebots still start without armour and earn it; nothing runs
    /// except at a task break; the level 20 and 50 starter gear is not touched. Off by default: with bot_crafters off every
    /// member returns at once and draws no random number; want-to-buy orders also need bot_craft_orders. Called from
    /// parallel brain turns: no database work, no lock beyond the Exchange purchase's own.
    /// </para>
    /// </summary>
    public static class BotGearPipeline
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotGearPipeline));

        /// <summary>The share of its purse a bot spends on one Exchange piece for a gap (the rest is travel coin).</summary>
        public const double ExchangeBudgetShare = 0.8;
        /// <summary>DividedRealms' rule for a bot's order (AutonomousBotCraftOrders, ceac061a): at most half the purse, the
        /// materials at the merchant's price plus a fee of a quarter of them, at least 100 copper; three hours to make it.</summary>
        public const double OrderShareOfCoin = 0.5;
        public const double OrderFeeShare = 0.25;
        public const long OrderMinimumFee = 100;
        public static readonly TimeSpan OrderLifetime = TimeSpan.FromHours(3);
        /// <summary>A bot that could not post an order (no recipe, no coin, refused) tries again after this.</summary>
        public static readonly TimeSpan OrderRetry = TimeSpan.FromMinutes(30);
        /// <summary>How far below its level a bot looks for a recipe to order (recipes come in steps).</summary>
        public const int OrderLevelWindow = 10;

        public enum GapReason { Empty, Template, BelowBand }

        public readonly record struct Gap(eInventorySlot Slot, GapReason Reason);

        public sealed record Fill(DbInventoryItem Item, eInventorySlot Slot, long Gain, Gap Gap);

        public static bool Enabled => CraftingEconomyProperties.BOT_CRAFTERS;

        public static bool OrdersEnabled => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_CRAFT_ORDERS;

        /// <summary>The gamebots the pipeline is for (AutonomousBotEconomy.UsesGearValuation: saved autonomous gamebots).</summary>
        public static bool Applies(GameBot bot) => Enabled && bot?.DatabaseID > 0 && AutonomousBotEconomy.UsesGearValuation(bot);

        #region The gear check (pure)

        /// <summary>A piece at least one full five-level band below the bot (MarketBand.LevelsPerBand).</summary>
        public static bool IsBelowBand(int botLevel, int itemLevel) => itemLevel + MarketBand.LevelsPerBand <= botLevel;

        /// <summary>
        /// The gaps of a loadout, in the order they are filled: empty slots, then (at 50) slots without a template piece,
        /// then pieces below the band; within each, body, legs, arms, head, hands, feet.
        /// </summary>
        public static Gap[] FindGaps(int botLevel, DbInventoryItem[] worn)
        {
            var empty = new List<Gap>();
            var template = new List<Gap>();
            var below = new List<Gap>();
            foreach (eInventorySlot slot in BotGearSet.TemplateSlots)
            {
                DbInventoryItem item = worn != null && (int)slot < worn.Length ? worn[(int)slot] : null;
                if (item == null)
                    empty.Add(new Gap(slot, GapReason.Empty));
                else if (botLevel >= BotGearSet.TemplateLevel && !BotGearSet.IsTemplatePiece(item))
                    template.Add(new Gap(slot, GapReason.Template));
                else if (IsBelowBand(botLevel, item.Level))
                    below.Add(new Gap(slot, GapReason.BelowBand));
            }
            return [.. empty, .. template, .. below];
        }

        /// <summary>The quality a want-to-buy order asks for: 99 for a template piece, none otherwise.</summary>
        public static int MinQualityFor(Gap gap) => gap.Reason == GapReason.Template ? BotGearSet.TemplateQuality : 0;

        /// <summary>DividedRealms' order price: the materials and a fee of a quarter of them, at least 100 copper.</summary>
        public static long Quote(long materialCostCopper) =>
            Math.Max(0, materialCostCopper) + Math.Max(OrderMinimumFee, (long)Math.Ceiling(Math.Max(0, materialCostCopper) * OrderFeeShare));

        /// <summary>The order a bot can pay for: its quote within half the purse.</summary>
        public static bool CanAffordOrder(long quote, long purse) => quote > 0 && quote <= (long)(purse * OrderShareOfCoin);

        /// <summary>
        /// Ranks pieces that fill a gap: a template piece first where the gap is the template, then crafted pieces, then
        /// the bigger rise of the set score, then the cheaper.
        /// </summary>
        public static Fill Best(IEnumerable<Fill> fills) => fills?
            .Where(fill => fill?.Item != null && fill.Gain > 0)
            .OrderByDescending(fill => fill.Gap.Reason == GapReason.Template && BotGearSet.IsTemplatePiece(fill.Item))
            .ThenByDescending(fill => fill.Item.IsCrafted)
            .ThenByDescending(fill => fill.Gain)
            .ThenBy(fill => fill.Item.SellPrice)
            .FirstOrDefault();

        #endregion

        #region Per-bot state and the hourly counts

        private sealed class BotState
        {
            public volatile Gap[] Gaps = [];
            public volatile bool HasFill;
            public long NextOrderTicksUtc;
        }

        private static readonly ConcurrentDictionary<long, BotState> States = new();

        private static long _checks, _withGaps, _empty, _template, _below, _routed, _bought, _templateBought, _boughtCopper,
            _wtbPosted, _wtbRefused, _wtbOpen, _wtbUnaffordable, _wtbNoRecipe, _wtbNoBoard;

        public readonly record struct HourCounts(long Checks, long WithGaps, long Empty, long Template, long Below, long Routed,
            long Bought, long TemplateBought, long BoughtCopper, long WtbPosted, long WtbRefused, long WtbOpen,
            long WtbUnaffordable, long WtbNoRecipe, long WtbNoBoard);

        public static HourCounts Peek() => new(Interlocked.Read(ref _checks), Interlocked.Read(ref _withGaps),
            Interlocked.Read(ref _empty), Interlocked.Read(ref _template), Interlocked.Read(ref _below),
            Interlocked.Read(ref _routed), Interlocked.Read(ref _bought), Interlocked.Read(ref _templateBought),
            Interlocked.Read(ref _boughtCopper), Interlocked.Read(ref _wtbPosted), Interlocked.Read(ref _wtbRefused),
            Interlocked.Read(ref _wtbOpen), Interlocked.Read(ref _wtbUnaffordable), Interlocked.Read(ref _wtbNoRecipe),
            Interlocked.Read(ref _wtbNoBoard));

        public static HourCounts TakeHour() => new(Interlocked.Exchange(ref _checks, 0), Interlocked.Exchange(ref _withGaps, 0),
            Interlocked.Exchange(ref _empty, 0), Interlocked.Exchange(ref _template, 0), Interlocked.Exchange(ref _below, 0),
            Interlocked.Exchange(ref _routed, 0), Interlocked.Exchange(ref _bought, 0), Interlocked.Exchange(ref _templateBought, 0),
            Interlocked.Exchange(ref _boughtCopper, 0), Interlocked.Exchange(ref _wtbPosted, 0), Interlocked.Exchange(ref _wtbRefused, 0),
            Interlocked.Exchange(ref _wtbOpen, 0), Interlocked.Exchange(ref _wtbUnaffordable, 0), Interlocked.Exchange(ref _wtbNoRecipe, 0),
            Interlocked.Exchange(ref _wtbNoBoard, 0));

        public static void ResetForTests()
        {
            States.Clear();
            TakeHour();
            BotGearGuard.ResetForTests();
        }

        /// <summary>The gaps found at this bot's current task break (empty when none or not checked).</summary>
        public static Gap[] GapsOf(GameBot bot) => bot != null && States.TryGetValue(bot.DatabaseID, out BotState state) ? state.Gaps : [];

        #endregion

        #region At a task break

        /// <summary>
        /// The gear check (the task-break hook's Begin): finds the gaps, looks for an Exchange piece that fills one, and,
        /// when there is none, posts a want-to-buy order. Grouped bots are checked too (an order needs no trip); only an
        /// ungrouped bot makes the town stop.
        /// </summary>
        public static void Check(GameBot bot)
        {
            if (!Applies(bot))
                return;
            BotState state = States.GetOrAdd(bot.DatabaseID, _ => new BotState());
            DbInventoryItem[] worn = BotGearSet.Worn(bot);
            Gap[] gaps = FindGaps(bot.Level, worn);
            Interlocked.Increment(ref _checks);
            bool hadFill = state.HasFill;
            state.Gaps = gaps;
            if (gaps.Length == 0)
            {
                state.HasFill = false;
                if (hadFill)
                    AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
                return;
            }
            Interlocked.Increment(ref _withGaps);
            foreach (Gap gap in gaps)
            {
                if (gap.Reason == GapReason.Empty)
                    Interlocked.Increment(ref _empty);
                else if (gap.Reason == GapReason.Template)
                    Interlocked.Increment(ref _template);
                else
                    Interlocked.Increment(ref _below);
            }

            bool hasFill = FindGapFill(bot, gaps) != null;
            state.HasFill = hasFill;
            if (hasFill != hadFill)
                AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
            if (!hasFill)
                TryPostWantToBuy(bot, gaps, worn, state);
        }

        /// <summary>The break is closing: what was found is no reason for the next one's stop.</summary>
        public static void EndBreak(GameBot bot)
        {
            if (bot != null && States.TryGetValue(bot.DatabaseID, out BotState state))
            {
                state.HasFill = false;
                state.Gaps = [];
            }
        }

        /// <summary>The best Exchange piece of the bot's realm that fills one of these gaps within its budget, or null.</summary>
        public static Fill FindGapFill(GameBot bot, Gap[] gaps)
        {
            if (!Applies(bot) || gaps == null || gaps.Length == 0)
                return null;
            long budget = (long)(AutonomousBotEconomy.GetMoney(bot.DatabaseID) * ExchangeBudgetShare);
            if (budget <= 0)
                return null;
            BotGearSet.Context context = BotGearSet.For(bot);
            DbInventoryItem[] worn = BotGearSet.Worn(bot);
            var fills = new List<Fill>();
            foreach (DbInventoryItem item in AutonomousBotEconomy.FindAffordableExchangeItems(bot, int.MaxValue))
            {
                if (item.SellPrice > budget || item.SellPrice > ItemValuePriceIndex.Instance.Bid(item, ItemValue165.MoodHigh) ||
                    !AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out eInventorySlot slot))
                    continue;
                int index = Array.FindIndex(gaps, gap => gap.Slot == slot);
                if (index < 0 || gaps[index].Reason == GapReason.Template && !BotGearSet.IsTemplatePiece(item))
                    continue;
                long gain = BotGearSet.Delta(context, worn, slot, item);
                if (gain > 0)
                    fills.Add(new Fill(item, slot, gain, gaps[index]));
            }
            return Best(fills);
        }

        private static void TryPostWantToBuy(GameBot bot, Gap[] gaps, DbInventoryItem[] worn, BotState state)
        {
            if (!OrdersEnabled)
                return;
            ICraftOrderBoard board = CraftOrders.Board;
            if (IsNullBoard(board))
            {
                Interlocked.Increment(ref _wtbNoBoard);
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (now.Ticks < Interlocked.Read(ref state.NextOrderTicksUtc))
                return;
            string buyerId = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            if (board.ForBuyer(buyerId).Any(order => order.State is CraftOrderState.Open or CraftOrderState.Made))
            {
                Interlocked.Increment(ref _wtbOpen);
                return;
            }
            Interlocked.Exchange(ref state.NextOrderTicksUtc, now.Add(OrderRetry).Ticks);

            IRecipeCatalog catalog = RecipeCatalogs.Current;
            (CraftRecipeInfo recipe, Gap gap) = catalog.IsLoaded ? ChooseOrder(bot, gaps, worn, catalog) : default;
            if (recipe == null)
            {
                Interlocked.Increment(ref _wtbNoRecipe);
                return;
            }
            long quote = Quote(recipe.MaterialCostCopper);
            long purse = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
            if (!CanAffordOrder(quote, purse))
            {
                Interlocked.Increment(ref _wtbUnaffordable);
                return;
            }
            var order = new CraftOrder(Guid.NewGuid().ToString("N"), bot.Realm, CraftOrderSource.Bot, bot.DatabaseID, buyerId,
                bot.Name ?? string.Empty, 0, recipe.Skill, recipe.ProductId, recipe.ProductName ?? string.Empty, recipe.Level,
                MinQualityFor(gap), 1, quote, true, CraftOrderState.Open, string.Empty, now, now.Add(OrderLifetime));
            bool posted;
            try { posted = board.Post(order); }
            catch { posted = false; } // another package's failure never stops a bot's break
            if (posted)
                Interlocked.Increment(ref _wtbPosted);
            else
                Interlocked.Increment(ref _wtbRefused);
            if (posted && EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconGearCheck, ("event", "wtb"), ("bot", bot.Name), ("id", bot.DatabaseID),
                    ("realm", bot.Realm), ("level", bot.Level), ("slot", gap.Slot), ("reason", gap.Reason), ("item", recipe.ProductName),
                    ("craft", recipe.Skill), ("minQuality", MinQualityFor(gap)), ("copper", quote), ("order", order.OrderId)));
        }

        /// <summary>WP0's NullBoard: no board is registered (WP5 not merged or not loaded).</summary>
        public static bool IsNullBoard(ICraftOrderBoard board) => board == null || board.GetType().DeclaringType == typeof(CraftOrders);

        /// <summary>
        /// The recipe to order for the first gap one can be found for: the class's armour type at its level
        /// (GeneratedUniqueItem's own table), the recipe nearest the bot's level and not above it, whose piece (at the
        /// order's quality) the bot can wear and that raises its set score.
        /// </summary>
        public static (CraftRecipeInfo Recipe, Gap Gap) ChooseOrder(GameBot bot, Gap[] gaps, DbInventoryItem[] worn, IRecipeCatalog catalog)
        {
            eObjectType armor = ArmorTypeFor(bot);
            if (armor == eObjectType.GenericItem || catalog == null)
                return default;
            BotGearSet.Context context = BotGearSet.For(bot);
            foreach (Gap gap in gaps)
            {
                for (int target = bot.Level; target >= Math.Max(1, bot.Level - OrderLevelWindow); target -= 2)
                {
                    CraftRecipeInfo recipe = catalog.BestFor((int)armor, (int)gap.Slot, target, bot.Realm);
                    if (recipe == null)
                        break;
                    if (recipe.Stackable || recipe.ProductLevel > bot.Level)
                        continue;
                    DbItemTemplate product = recipe.Recipe?.Product;
                    DbInventoryItem piece = product == null ? null : GameInventoryItem.Create(product);
                    if (piece == null)
                        continue;
                    piece.Quality = Math.Max(MinQualityFor(gap), Math.Max(piece.Quality, 96));
                    if (!AutonomousBotEconomy.CanUseEquipment(bot, piece) || BotGearSet.Delta(context, worn, gap.Slot, piece) <= 0)
                        continue;
                    return (recipe, gap);
                }
            }
            return default;
        }

        /// <summary>The armour type the bot's class wears at its level (GeneratedUniqueItem's tables), GenericItem when unknown.</summary>
        public static eObjectType ArmorTypeFor(GameBot bot)
        {
            if (bot?.CharacterClass == null)
                return eObjectType.GenericItem;
            try
            {
                var cls = (eCharacterClass)bot.CharacterClass.ID;
                byte level = (byte)Math.Clamp((int)bot.Level, 1, 50);
                return bot.Realm switch
                {
                    eRealm.Albion => GeneratedUniqueItem.GetAlbionArmorType(cls, level),
                    eRealm.Midgard => GeneratedUniqueItem.GetMidgardArmorType(cls, level),
                    eRealm.Hibernia => GeneratedUniqueItem.GetHiberniaArmorType(cls, level),
                    _ => eObjectType.GenericItem,
                };
            }
            catch
            {
                return eObjectType.GenericItem;
            }
        }

        #endregion

        #region At the Realm Exchange

        /// <summary>
        /// Buys the gap's piece at the broker and puts it on (the no-undress guard checks the move). False when there is
        /// nothing to buy (the ordinary purchase then runs).
        /// </summary>
        public static bool TryBuyGapFill(GameBot bot, long now, out BotServiceResult result)
        {
            result = default;
            if (!Applies(bot) || AutonomousBotEconomy.IsBackpackFull(bot))
                return false;
            Gap[] gaps = FindGaps(bot.Level, BotGearSet.Worn(bot));
            Fill fill = FindGapFill(bot, gaps);
            if (fill == null)
                return false;
            DbInventoryItem item = fill.Item;
            int price = item.SellPrice;
            string seller = item.OwnerID;
            long scoreBefore = BotGearSet.Score(BotGearSet.For(bot), BotGearSet.Worn(bot));
            if (!AutonomousBotEconomy.TryBuy(bot, item, out bool deferred))
            {
                if (!deferred)
                {
                    AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
                    return false;
                }
                result = new BotServiceResult($"Waiting to buy {item.Name}", "The database writer is busy; trying again shortly",
                    2_000, eAutonomousProgressKind.ServiceInteraction);
                return true;
            }
            bool equipped = AutonomousBotEconomy.TryEquipPurchasedUpgrade(bot,
                new AutonomousBotEconomy.PurchaseCandidate(item, 1_000, fill.Slot, true, AutonomousBotEconomy.IsPlayerListing(seller)));
            Interlocked.Increment(ref _bought);
            Interlocked.Add(ref _boughtCopper, price);
            bool template = bot.Level >= BotGearSet.TemplateLevel && BotGearSet.IsTemplatePiece(item);
            if (template)
            {
                Interlocked.Increment(ref _templateBought);
                try
                {
                    DbInventoryItem[] worn = BotGearSet.Worn(bot);
                    if (Log.IsInfoEnabled)
                        Log.Info(EconomyLog.Line(EconomyLog.EconTemplateBuy, ("bot", bot.Name), ("id", bot.DatabaseID),
                            ("realm", bot.Realm), ("level", bot.Level), ("slot", fill.Slot), ("item", item.Name),
                            ("quality", item.Quality), ("crafted", item.IsCrafted), ("creator", item.Creator ?? string.Empty),
                            ("copper", price), ("seller", seller), ("equipped", equipped), ("scoreBefore", scoreBefore),
                            ("scoreAfter", BotGearSet.Score(BotGearSet.For(bot), worn)),
                            ("template", $"{BotGearSet.TemplatePieces(worn)}/{BotGearSet.TemplateSlots.Length}")));
                }
                catch { }
            }
            AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
            result = new BotServiceResult(
                equipped ? $"Bought and equipped {item.Name}" : $"Bought {item.Name} from the Realm Exchange",
                $"Gear check ({fill.Gap.Reason} {fill.Slot}): paid {price} copper from real saved coin",
                3_000, eAutonomousProgressKind.Money);
            return true;
        }

        #endregion

        #region The hourly lines and /lscraft wtb

        /// <summary>The hourly ECON_GEAR_CHECK line (null when nothing was checked).</summary>
        public static string GearCheckLine(HourCounts counts) => counts.Checks == 0 ? null : EconomyLog.Line(EconomyLog.EconGearCheck,
            ("event", "hour"), ("checks", counts.Checks), ("gaps", counts.WithGaps), ("empty", counts.Empty),
            ("template", counts.Template), ("below", counts.Below), ("routed", counts.Routed), ("bought", counts.Bought),
            ("template_bought", counts.TemplateBought), ("copper", counts.BoughtCopper), ("wtb_posted", counts.WtbPosted),
            ("wtb_refused", counts.WtbRefused), ("wtb_open", counts.WtbOpen), ("wtb_unaffordable", counts.WtbUnaffordable),
            ("wtb_no_recipe", counts.WtbNoRecipe), ("wtb_no_board", counts.WtbNoBoard));

        /// <summary>The hourly ECON_UNDRESS_GUARD line: blocked must be 0 (null when no equip was checked).</summary>
        public static string GuardLine(long checkedEquips, long blocked) => checkedEquips == 0 && blocked == 0 ? null :
            EconomyLog.Line(EconomyLog.EconUndressGuard, ("event", "hour"), ("checked", checkedEquips), ("blocked", blocked),
                ("blocked_since_start", BotGearGuard.BlockedSinceStart));

        /// <summary>
        /// What /lscraft wtb shows (registered as WP10's CraftEconomyTestHooks handler for "wtb"): the switches, the board,
        /// this hour's counts and the guard.
        /// </summary>
        public static string[] WtbLines()
        {
            HourCounts counts = Peek();
            return
            [
                $"Gear pipeline (WP6): bot_crafters {(Enabled ? "on" : "off")}, want-to-buy orders {(OrdersEnabled ? "on" : "off (bot_craft_orders)")}, " +
                $"order board {(IsNullBoard(CraftOrders.Board) ? "not registered (WP5)" : "registered")}.",
                $"This hour: {counts.Checks} gear checks, {counts.WithGaps} with gaps (empty {counts.Empty}, template {counts.Template}, " +
                $"below band {counts.Below}); {counts.Routed} sent to the Exchange, {counts.Bought} bought ({counts.TemplateBought} template), " +
                $"{counts.BoughtCopper} copper.",
                $"Want-to-buy: {counts.WtbPosted} posted, {counts.WtbRefused} refused, {counts.WtbOpen} already open, " +
                $"{counts.WtbUnaffordable} unaffordable, {counts.WtbNoRecipe} without a recipe, {counts.WtbNoBoard} without a board.",
                $"No-undress guard: {BotGearGuard.Checked} equips checked this hour, {BotGearGuard.Blocked} blocked " +
                $"({BotGearGuard.BlockedSinceStart} since start; must be 0).",
            ];
        }

        #endregion

        #region Registration

        private static int _registered;

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

        /// <summary>Registers WP6's seams once (script load).</summary>
        public static void Register()
        {
            if (Interlocked.Exchange(ref _registered, 1) != 0)
                return;
            BotServiceSeams.Register(TaskBreakHook.Instance);
            BotServiceSeams.Register(GearNeed.Instance);
            BotServiceSeams.Register(ExchangeStep.Instance);
            EconomyLifecycle.Register(Store.Instance);
            ItemValuePriceIndex.RegisterIfUnset();
            // /lscraft wtb (WP10's GM window) answers from here.
            DOL.GS.Commands.CraftEconomyTestHooks.Register("wtb", (_, _) => WtbLines());
        }

        /// <summary>Tests only: register again after the seams were reset.</summary>
        public static void RegisterForTests()
        {
            Interlocked.Exchange(ref _registered, 0);
            Register();
        }

        /// <summary>The gear check at every natural task break (Begin), its state cleared when the break closes (End).</summary>
        public sealed class TaskBreakHook : IBotTaskBreakHook
        {
            public static readonly TaskBreakHook Instance = new();
            public string Name => "gear check (WP6)";

            public void AtTaskBreak(GameBot bot, TaskBreakPoint point)
            {
                if (!Enabled)
                    return;
                if (point == TaskBreakPoint.Begin)
                    Check(bot);
                else
                    EndBreak(bot);
            }
        }

        /// <summary>The second shopping trigger: a gap the Exchange can fill asks for the town stop, whatever the bag.</summary>
        public sealed class GearNeed : IBotTownVisitNeed
        {
            public static readonly GearNeed Instance = new();
            public string Name => "gear gap (WP6)";

            /// <summary>Asked right after the check of the same task break.</summary>
            public bool WantsVisit(GameBot bot)
            {
                if (!Applies(bot) || !States.TryGetValue(bot.DatabaseID, out BotState state) || !state.HasFill)
                    return false;
                Interlocked.Increment(ref _routed);
                return true;
            }

            /// <summary>The Exchange while a gap's piece is still there and affordable (the purchase may have been bought by
            /// someone else meanwhile, or the bag filled).</summary>
            public eWorldServiceKind? NeededService(GameBot bot)
            {
                if (!Applies(bot) || !States.TryGetValue(bot.DatabaseID, out BotState state) || state.Gaps.Length == 0 ||
                    AutonomousBotEconomy.IsBackpackFull(bot))
                    return null;
                return FindGapFill(bot, FindGaps(bot.Level, BotGearSet.Worn(bot))) != null ? eWorldServiceKind.RealmExchange : null;
            }
        }

        /// <summary>At the broker, after the listing and before the ordinary purchase: buy the gap's piece.</summary>
        public sealed class ExchangeStep : IBotServiceStep
        {
            public static readonly ExchangeStep Instance = new();
            public string Name => "gear gap purchase (WP6)";
            public int Order => 600;
            public eWorldServiceKind Kind => eWorldServiceKind.RealmExchange;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                return Enabled && TryBuyGapFill(bot, now, out result);
            }
        }

        /// <summary>The hourly ECON_GEAR_CHECK and ECON_UNDRESS_GUARD lines; the merchant-stock set read at start.</summary>
        public sealed class Store : IEconomyStore
        {
            public static readonly Store Instance = new();
            private const long HourMs = 3_600_000;
            private long _nextLineTick;

            public string Name => "gear pipeline (WP6)";

            public void Load()
            {
                // True165 (f905931): read the merchant stock at start, not in the first bot's turn.
                if (Enabled)
                    ItemValue165.WarmMerchantStock();
            }

            public void Pulse(long now)
            {
                long next = Interlocked.Read(ref _nextLineTick);
                if (next == 0)
                {
                    Interlocked.CompareExchange(ref _nextLineTick, now + HourMs, 0);
                    return;
                }
                if (now < next || Interlocked.CompareExchange(ref _nextLineTick, now + HourMs, next) != next)
                    return;
                HourCounts counts = TakeHour();
                (long checkedEquips, long blocked) = BotGearGuard.TakeHour();
                if (!Enabled || !Log.IsInfoEnabled)
                    return;
                string gear = GearCheckLine(counts);
                if (gear != null)
                    Log.Info(gear);
                string guard = GuardLine(checkedEquips, blocked);
                if (guard != null)
                {
                    if (blocked > 0)
                        Log.Warn(guard);
                    else
                        Log.Info(guard);
                }
            }

            public void Flush(bool wait) { }
        }

        #endregion
    }
}
