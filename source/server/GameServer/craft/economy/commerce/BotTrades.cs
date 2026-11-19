using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.PacketHandler;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Trade windows between a real player and autonomous gamebots, in both directions (Crafting 2.0 WP8, built out on the
    /// owner's word of 2026-11-16: "It should be built out fully, look pretty and have functionality to the fullest"). Off
    /// with WP8's switches (bot_crafters and bot_crafter_commerce).
    /// <list type="bullet">
    /// <item><b>Buy:</b> what the bot holds and would part with: a crafter's own crafted stock, loot and spare gear in its
    /// bag, and its own Realm Exchange listings (taken back off the Exchange into its bag for the player). Asked at the
    /// price index's ask (WP5/WP6); a crafter's stock at the price it quotes by tell (WP8).</item>
    /// <item><b>Sell:</b> a piece the bot wants: one that fills a gap or beats what it wears by its set score (WP6
    /// BotGearSet through AutonomousBotEconomy.TryGetEquipmentUpgrade, the same test as every purchase), or a piece one of
    /// its own open craft orders asks for (the order is called off and its escrow refunded, so that coin pays the player).
    /// Bid at the price index's bid, never more than its purse share (the gear pipeline's Exchange budget, 80%).</item>
    /// <item><b>Swap:</b> both at once; the coin settles the difference either way.</item>
    /// <item><b>Jobs:</b> the Repair box with one piece of the player's (a crafter with the material skill at the item's
    /// craft level, as a player in the window), or the Combine box with the player's piece and spellcraft gems (a bot
    /// spellcrafter, the player's rules: no overcharge, so nothing explodes).</item>
    /// <item><b>Commissions</b> (WP8): a paid-up piece handed over, or one paid on delivery.</item>
    /// </list>
    /// <para>
    /// Who: only autonomous gamebots (never a /bot companion); only real players start a trade. A busy bot (decided here,
    /// owner rule "selling only at natural task breaks" kept for the bot's own selling trips): dead, fighting, casting or
    /// its group fighting, or on a raid, siege or battleground duty, it says so and no window opens; anything else (a hunt
    /// between pulls, travel, a break) is the player's to interrupt: the bot stops where it stands, faces the player and
    /// trades, for at most <see cref="HoldMilliseconds"/>. A fight closes the window at once. Worn gear never leaves a bot
    /// (only its backpack is offered), and what it buys it puts on through the no-undress guard.
    /// </para>
    /// <para>
    /// Every coin move names its kind: player and bot coin in the window is PlayerTrade (a transfer within the supply); an
    /// open order's escrow refunded to the bot is the board's CraftOrder. Logs: ECON_PLAYER_TRADE (open, refused, closed,
    /// done), ECON_PLAYER_SELL (a piece a bot bought), ECON_PLAYER_DELIVERY (a piece a bot sold, as WP8), ECON_SC_JOB
    /// (job=repair or spellcraft).
    /// </para>
    /// </summary>
    public static class BotTrades
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotTrades));

        public static bool Enabled => CrafterCommerce.Enabled;

        /// <summary>How far the player may stand from the bot while trading (twice the give-item distance: a bot settles a step).</summary>
        public const int TradeReach = WorldMgr.GIVE_ITEM_DISTANCE * 2;

        /// <summary>The longest a trade holds a gamebot still (then it closes the window and moves on).</summary>
        public const long HoldMilliseconds = 120_000;

        /// <summary>The share of its purse a gamebot pays for a piece (WP6's Exchange budget for a gear gap).</summary>
        public static double PurseShare => BotGearPipeline.ExchangeBudgetShare;

        /// <summary>A spellcraft job's fee: 10% of the gems' value (WP4's enchanting fee), at least a silver a gem.</summary>
        public const int SpellcraftFeePercent = BotEnchanting.FeePercent;
        public const long SpellcraftMinimumFeePerGem = 100;

        /// <summary>A repair job's fee: half what the smith asks for it now (the Repair upkeep setting decides the smith's price).</summary>
        public const int RepairFeePercentOfSmith = 50;

        public enum Busy { Free, Dead, Fighting, Duty, Away, NotTrading }

        /// <summary>A piece the bot would sell, numbered as its list in chat says ("1. ... 2. ...").</summary>
        public sealed record Good(int Number, DbInventoryItem Item, long Ask, BotTradeTerms.Source Source);

        #region Seams for tests (the defaults are the live game)

        public static Func<GameBot, long> PurseOf = bot => AutonomousBotEconomy.GetMoney(bot.DatabaseID);
        public static Func<GameBot, GamePlayer, bool> InReach = DefaultInReach;
        public static Func<GameBot, Busy> BusyOf = DefaultBusy;
        public static Func<GameBot, IReadOnlyList<Good>> GoodsOf = DefaultGoods;
        public static Func<GameBot, DbInventoryItem, IReadOnlyList<DbInventoryItem>, BotTradeTerms.PlayerPiece> Appraise = DefaultAppraise;
        public static Action<GameBot, GamePlayer, string> SayTo = DefaultSay;
        public static Func<GamePlayer, long> WalletOf = player => player.GetCurrentMoney();
        public static Func<int, bool> Roll = percent => Util.Chance(percent);
        public static Func<long> Now = SafeNow;
        public static Action<GamePlayer, string> TellPlayer = DefaultTellPlayer;

        private static void DefaultTellPlayer(GamePlayer player, string text) =>
            player?.Out?.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        public static Func<long, long, EconomyFlowKind, bool> BotSpend = DefaultBotSpend;
        public static Func<long, long, EconomyFlowKind, bool> BotEarn = DefaultBotEarn;
        public static Func<GameBot, DbInventoryItem, bool> Equip = (bot, item) => AutonomousBotEconomy.TryEquipOwnedUpgrade(bot, item, "player-trade");

        private static bool DefaultBotSpend(long botId, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.TrySpend(botId, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.PlayerTrade : kind);
        private static bool DefaultBotEarn(long botId, long copper, EconomyFlowKind kind) =>
            AutonomousBotEconomy.AddMoney(botId, copper, kind == EconomyFlowKind.Unattributed ? EconomyFlowKind.PlayerTrade : kind);

        private static long SafeNow()
        {
            try { return GameLoop.GameLoopTime; }
            catch { return Environment.TickCount64; }
        }

        public static void ResetForTests()
        {
            PurseOf = bot => AutonomousBotEconomy.GetMoney(bot.DatabaseID);
            InReach = DefaultInReach;
            BusyOf = DefaultBusy;
            GoodsOf = DefaultGoods;
            Appraise = DefaultAppraise;
            SayTo = DefaultSay;
            WalletOf = player => player.GetCurrentMoney();
            Roll = percent => Util.Chance(percent);
            Now = SafeNow;
            TellPlayer = DefaultTellPlayer;
            LastAdvert.Clear();
            BotSpend = DefaultBotSpend;
            BotEarn = DefaultBotEarn;
            Equip = (bot, item) => AutonomousBotEconomy.TryEquipOwnedUpgrade(bot, item, "player-trade");
            Holds.Clear();
            LastGoods.Clear();
            BotTradeWindow.LiveExecute = ExecuteTrade;
        }

        private static bool DefaultInReach(GameBot bot, GamePlayer player) =>
            bot?.CurrentRegion != null && player?.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(player, TradeReach);

        private static void DefaultSay(GameBot bot, GamePlayer player, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (!AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Say, text))
                CrafterCommerce.SendTell(bot, player, text);
        }

        #endregion

        #region Who may trade, and the hold

        /// <summary>An autonomous gamebot of the player's realm (never a /bot companion), with the switches on.</summary>
        public static bool CanTradeWith(GameBot bot, GamePlayer player) => Enabled && CrafterCommerce.IsCommerceBot(bot, player);

        /// <summary>The busy rule (see the class): what keeps a gamebot from a trade the player starts.</summary>
        public static Busy DefaultBusy(GameBot bot)
        {
            if (bot == null || bot.ObjectState != GameObject.eObjectState.Active)
                return Busy.Away;
            if (!bot.IsAlive)
                return Busy.Dead;
            if (bot.InCombat || bot.IsCasting || bot.Group?.GetMembersInTheGroup().Any(member => member is GameBot && member.InCombat) == true)
                return Busy.Fighting;
            eDutyLeaseKind duty = BotDutyLeases.KindOf(bot);
            if (duty is eDutyLeaseKind.RealmRaid or eDutyLeaseKind.RvrEvent or eDutyLeaseKind.BattlegroundWave)
                return Busy.Duty;
            return Busy.Free;
        }

        /// <summary>A busy bot's answer (fixed words; the corpus may say it, topic trade_busy_*).</summary>
        public static (string Topic, string Text) BusyAnswer(Busy busy) => busy switch
        {
            Busy.Fighting => ("trade_busy_fight", "in a fight, give me a min"),
            Busy.Duty => ("trade_busy_duty", "on a raid right now, catch me after"),
            Busy.NotTrading => ("trade_busy_trading", "one trade at a time, finish yours first"),
            _ => (null, null),
        };

        private sealed record Hold(GamePlayer Player, long Until, BotTradeWindow Window);

        private static readonly ConcurrentDictionary<long, Hold> Holds = new();

        /// <summary>
        /// BotBrain, every think: a gamebot trading with a player stands still and faces the player (true: the brain does
        /// nothing else this turn). The hold ends when the window closes, after <see cref="HoldMilliseconds"/>, or at once
        /// in a fight (the window is closed then).
        /// </summary>
        public static bool HoldsStill(GameBot bot)
        {
            if (bot == null || Holds.IsEmpty || !Holds.TryGetValue(bot.DatabaseID, out Hold hold))
                return false;
            BotTradeWindow window = hold.Window;
            long now = Now();
            Busy busy = BusyOf(bot);
            string end = window == null || window.IsClosed ? "closed"
                : now >= hold.Until ? "too long"
                : busy is Busy.Fighting or Busy.Dead or Busy.Away ? "fight"
                : !InReach(bot, hold.Player) ? "walked away"
                : null;
            if (end != null)
            {
                Holds.TryRemove(bot.DatabaseID, out _);
                // A brain turn runs in parallel: the window is closed (packets, the player's state) on the game loop.
                if (window != null && !window.IsClosed)
                    CrafterCommerce.Schedule(bot, 1, () => EndHold(bot, hold, window, end));
                return false;
            }
            try
            {
                if (bot.IsMoving)
                    bot.StopMoving();
                if (hold.Player != null && !bot.IsMoving)
                {
                    int off = Math.Abs(bot.GetHeading(hold.Player) - bot.Heading) % 4096;
                    if (Math.Min(off, 4096 - off) > 4096 / 12)
                        bot.TurnTo(hold.Player);
                }
            }
            catch { } // tests: no world
            return true;
        }

        private static void EndHold(GameBot bot, Hold hold, BotTradeWindow window, string end)
        {
            if (window.IsClosed)
                return;
            window.Tell?.Invoke(hold.Player, end switch
            {
                "fight" => $"{bot.Name} is attacked and closes the trade.",
                "walked away" => $"You are too far away to trade with {bot.Name}.",
                _ => $"{bot.Name} has to go and closes the trade.",
            });
            if (end == "too long")
                SayTo(bot, hold.Player, Voiced(bot, "trade_timeout", "sorry, gotta run. trade me again later", null));
            window.CloseTrade();
        }

        public static bool IsHeld(GameBot bot) => bot != null && Holds.ContainsKey(bot.DatabaseID);

        #endregion

        #region Opening

        /// <summary>
        /// Fills a window's behaviour (valuer, close, the bot's hold): every bot trade window, WP8's included, goes through it.
        /// </summary>
        public static void Decorate(BotTradeWindow window)
        {
            if (window == null)
                return;
            window.Valuer ??= OfferOf;
            window.Tell = (player, text) => TellPlayer(player, text);
            Action<BotTradeWindow, bool> closed = window.Closed;
            window.Closed = (w, completed) =>
            {
                closed?.Invoke(w, completed);
                OnClosed(w, completed);
            };
            Holds[window.Bot.DatabaseID] = new Hold(window.Owner, Now() + HoldMilliseconds, window);
        }

        /// <summary>
        /// A trade the player starts (/tradewindow, an item or coin dropped on the bot, "buy 2" by tell). Null with the
        /// reason when it cannot open; the busy bot says why itself.
        /// </summary>
        public static BotTradeWindow TryOpen(GamePlayer player, GameBot bot, string how, out string why)
        {
            why = null;
            if (player == null || bot == null)
            {
                why = "Select a gamebot to trade with.";
                return null;
            }
            if (!CanTradeWith(bot, player))
            {
                why = bot.IsTemporaryGroupHelper ? $"{bot.Name} is your companion: give it gear with /bot." :
                    bot.IsHiredCompanion ? $"{bot.Name} is a hired companion: hand them gear, or right-click them for their gear window." :
                    $"{bot.Name} does not trade.";
                return null;
            }
            if (player.TradeWindow is BotTradeWindow open && open.Bot == bot)
                return open;
            if (player.TradeWindow != null)
            {
                why = "You are already trading.";
                return null;
            }
            if (!InReach(bot, player))
            {
                why = $"You are too far away to trade with {bot.Name}.";
                return null;
            }
            Busy busy = BusyOf(bot);
            if (busy == Busy.Free && Holds.TryGetValue(bot.DatabaseID, out Hold other) && other.Window is { IsClosed: false } && other.Player != player)
                busy = Busy.NotTrading;
            if (busy != Busy.Free)
            {
                why = busy switch
                {
                    Busy.Dead => $"{bot.Name} is dead.",
                    Busy.Away => $"{bot.Name} is not here.",
                    Busy.Fighting => $"{bot.Name} is fighting and cannot trade now.",
                    Busy.Duty => $"{bot.Name} is on a raid and cannot trade now.",
                    _ => $"{bot.Name} is busy trading with someone else.",
                };
                (string topic, string text) = BusyAnswer(busy);
                if (text != null)
                    SayTo(bot, player, Voiced(bot, topic, text, Slots(bot, player)));
                LogTrade("refused", bot, player, how, ("why", busy));
                return null;
            }
            var window = new BotTradeWindow(player, bot) { Purpose = "trade:" + how };
            Decorate(window);
            if (!BotTradeWindow.Show(player, window))
            {
                Holds.TryRemove(bot.DatabaseID, out _);
                why = "You are already trading.";
                return null;
            }
            try
            {
                if (bot.IsMoving)
                    bot.StopMoving();
                bot.TurnTo(player);
            }
            catch { } // tests: no world
            LogTrade("open", bot, player, how);
            IReadOnlyList<Good> goods = GoodsOf(bot);
            LastGoods[(bot.DatabaseID, player.Name)] = goods;
            foreach (string line in GoodsLines(bot, goods))
                window.Tell?.Invoke(player, line);
            return window;
        }

        private static void OnClosed(BotTradeWindow window, bool completed)
        {
            if (window?.Bot != null && Holds.TryGetValue(window.Bot.DatabaseID, out Hold hold) && ReferenceEquals(hold.Window, window))
                Holds.TryRemove(window.Bot.DatabaseID, out _);
            if (!completed)
                LogTrade("closed", window.Bot, window.Owner, window.Purpose);
        }

        #endregion

        #region What the bot sells

        private static readonly ConcurrentDictionary<(long Bot, string Player), IReadOnlyList<Good>> LastGoods = new();

        /// <summary>
        /// What the bot would sell a player, numbered: its crafted stock first (a crafter's own work, at its quote), then its
        /// bag's spare goods (AutonomousBotEconomy.FindPlayerTradeGoods, at the price index's ask), then its Realm Exchange
        /// listings at their posted price. Never anything it wears.
        /// </summary>
        public static IReadOnlyList<Good> DefaultGoods(GameBot bot)
        {
            var goods = new List<Good>();
            if (bot?.Inventory == null)
                return goods;
            var seen = new HashSet<DbInventoryItem>(ReferenceEqualityComparer.Instance);
            DbInventoryItem[] bag = bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Where(i => i != null).ToArray();
            foreach (CrafterTalk.StockPiece stock in SafeStock(bot))
            {
                DbInventoryItem item = bag.FirstOrDefault(i => i.ObjectId == stock.ObjectId);
                if (item != null && seen.Add(item))
                    goods.Add(new Good(goods.Count + 1, item, Math.Max(1, stock.Price), BotTradeTerms.Source.Stock));
            }
            foreach (DbInventoryItem item in AutonomousBotEconomy.FindPlayerTradeGoods(bot))
                if (seen.Add(item))
                    goods.Add(new Good(goods.Count + 1, item, AskFor(bot, item), BotTradeTerms.Source.Bag));
            foreach (DbInventoryItem item in ListingsOf(bot))
                if (seen.Add(item))
                    goods.Add(new Good(goods.Count + 1, item, Math.Max(1, item.SellPrice), BotTradeTerms.Source.Listing));
            return goods.Take(20).ToArray();
        }

        private static IReadOnlyList<CrafterTalk.StockPiece> SafeStock(GameBot bot)
        {
            try { return AutonomousBotCrafting.IsCrafter(bot) ? CrafterCommerce.StockOf(bot) : []; }
            catch { return []; }
        }

        /// <summary>The bot's ask for a piece of its bag: the registered price index (WP5/WP6) at a fair mood, at least 1 copper.</summary>
        public static long AskFor(GameBot bot, DbInventoryItem item)
        {
            try { return Math.Max(1, PriceIndexes.Current.Ask(item, 0)); }
            catch { return Math.Max(1, AutonomousBotEconomy.RecommendListingPrice(item)); }
        }

        /// <summary>The bot's bid for a player's piece: the registered price index's bid (WP5/WP6), keener for an empty slot.</summary>
        public static long BidFor(DbInventoryItem item, bool eager)
        {
            try { return Math.Max(0, PriceIndexes.Current.Bid(item, eager ? ItemValue165.MoodHigh : 0)); }
            catch { return Math.Max(0, AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item)); }
        }

        /// <summary>The bot's own Realm Exchange listings (in the Exchange's cache).</summary>
        public static IEnumerable<DbInventoryItem> ListingsOf(GameBot bot)
        {
            if (bot == null || bot.DatabaseID <= 0)
                return [];
            try
            {
                string owner = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
                return MarketCache.SearchItems(new ItemQuery { Owner = owner })
                    .Where(item => item != null && RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) && item.SellPrice > 0)
                    .OrderByDescending(item => item.SellPrice).ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>The bot's list in the system window: "[Aldric sells] 1. Studded Vest (96%, level 23): 1 gold, 20 silver".</summary>
        public static string[] GoodsLines(GameBot bot, IReadOnlyList<Good> goods)
        {
            if (bot == null)
                return [];
            if (goods == null || goods.Count == 0)
                return [$"[{bot.Name} sells] nothing right now. Put in what you would sell it, or tick Repair or Combine for a job."];
            var lines = new List<string> { $"[{bot.Name} sells] {goods.Count} piece{(goods.Count == 1 ? string.Empty : "s")}: say \"buy <number>\" to {bot.Name}, or /tradewindow buy <number>." };
            lines.AddRange(goods.Select(good =>
                $"[{bot.Name} sells] {good.Number}. {BotTradeTerms.PieceText(good.Item.Name, QualityOf(good.Item), good.Item.Level)}: " +
                $"{BotTradeTerms.PriceText(good.Ask)}{(good.Source == BotTradeTerms.Source.Listing ? " (on the Realm Exchange)" : string.Empty)}"));
            return lines.ToArray();
        }

        private static int QualityOf(DbInventoryItem item) => item == null || item.IsStackable ? 0 : item.Quality;

        /// <summary>
        /// The player picks a piece of the bot's list by its number: it goes on the bot's side of the open window (a listing is
        /// taken back off the Exchange into the bot's bag first). False with the reason.
        /// </summary>
        public static bool Buy(GamePlayer player, int number, out string why)
        {
            why = null;
            if (player?.TradeWindow is not BotTradeWindow window || window.IsClosed)
            {
                why = "Open a trade with the gamebot first (target it and type /tradewindow).";
                return false;
            }
            GameBot bot = window.Bot;
            if (!LastGoods.TryGetValue((bot.DatabaseID, player.Name), out IReadOnlyList<Good> goods))
                LastGoods[(bot.DatabaseID, player.Name)] = goods = GoodsOf(bot);
            Good good = goods.FirstOrDefault(g => g.Number == number);
            if (good == null)
            {
                why = $"{bot.Name} has no piece number {number}.";
                return false;
            }
            DbInventoryItem item = good.Item;
            if (good.Source == BotTradeTerms.Source.Listing && !WithdrawListing(bot, item))
            {
                why = $"{bot.Name} could not take its {item.Name} off the Realm Exchange (sold, or no room in its bag).";
                return false;
            }
            if (!window.AddBotItem(item, BotTradeWindow.PieceOf(item, good.Ask, good.Source)))
            {
                why = window.HasBotItem(item) ? $"{item.Name} is in the trade already." : "The window is full.";
                return false;
            }
            window.TradeUpdate();
            return true;
        }

        /// <summary>
        /// One of the bot's listings back into its bag (under the Exchange's lock), so the player can buy it in the window.
        /// The bot lists it again at a later break if the trade does not happen.
        /// </summary>
        public static bool WithdrawListing(GameBot bot, DbInventoryItem item)
        {
            if (bot?.Inventory == null || item == null)
                return false;
            lock (RealmExchangeBroker.TransactionLock)
            {
                if (!RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) || item.OwnerID != AutonomousBotEconomy.GetOwnerId(bot.DatabaseID))
                    return false;
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid)
                    return false;
                ushort lot = item.OwnerLot;
                int price = (int)Math.Min(int.MaxValue, item.SellPrice);
                int listedSlot = item.SlotPosition;
                string listedUtc = item.RealmExchangeListedUtc;
                MarketCache.RemoveItem(item);
                item.OwnerLot = 0;
                item.SellPrice = 0;
                item.RealmExchangeListedUtc = string.Empty;
                bool saved;
                using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                    saved = bot.Inventory.AddItem(slot, item) && GameServer.Database.SaveObject(item);
                if (!saved)
                {
                    bot.Inventory.RemoveItemWithoutDbDeletion(item);
                    item.OwnerLot = lot;
                    item.SellPrice = price;
                    item.SlotPosition = listedSlot;
                    item.RealmExchangeListedUtc = listedUtc;
                    MarketCache.AddItem(item);
                    return false;
                }
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            if (Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconReclaim, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("item", item.Name), ("quality", item.Quality), ("outcome", "taken back for a player's trade")));
            return true;
        }

        #endregion

        #region What the bot wants, and jobs

        /// <summary>
        /// The bot's verdict on a piece the player put in (with the other pieces coming in, so two chest pieces do not both
        /// count): wanted at a bid when it fills a gap or beats what it wears (TryGetEquipmentUpgrade: the set score with WP6),
        /// or for one of its own open orders; refused otherwise, with the reason.
        /// </summary>
        public static BotTradeTerms.PlayerPiece DefaultAppraise(GameBot bot, DbInventoryItem item, IReadOnlyList<DbInventoryItem> incoming)
        {
            string id = item?.ObjectId ?? string.Empty;
            BotTradeTerms.PlayerPiece Refuse(string reason) =>
                new(id, item?.Name ?? "it", QualityOf(item), item?.Level ?? 0, 0, BotTradeTerms.Verdict.Refused, reason);
            if (bot == null || item == null)
                return Refuse("no use for it");
            if (!item.IsTradable || !item.IsDropable)
                return Refuse("it cannot be traded");
            if (!AutonomousBotEconomy.CanUseEquipment(bot, item))
                return Refuse(item.LevelRequirement > bot.Level ? $"it is level {bot.Level}, too low for it" : "it cannot use it");
            if (!AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out eInventorySlot slot))
                return Refuse("no better than what it wears");
            // Only the best of several for one slot.
            DbInventoryItem better = (incoming ?? []).Where(other => other != null && !ReferenceEquals(other, item))
                .FirstOrDefault(other => AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, other, out eInventorySlot otherSlot) && otherSlot == slot &&
                                         AutonomousBotEconomy.EquipmentValue(other) > AutonomousBotEconomy.EquipmentValue(item));
            if (better != null)
                return Refuse($"it only needs one for that slot, and your {better.Name} is better");
            bool empty = bot.Inventory?.GetItem(slot) == null;
            long bid = BidFor(item, eager: empty);
            CraftOrder order = OpenOrderFor(bot, item, slot);
            if (order != null)
                return new BotTradeTerms.PlayerPiece(id, item.Name, QualityOf(item), item.Level, Math.Min(bid, order.EscrowCopper) > 0
                    ? Math.Min(Math.Max(bid, order.EscrowCopper / 2), order.EscrowCopper) : bid, BotTradeTerms.Verdict.ForOrder, null, order.OrderId);
            return new BotTradeTerms.PlayerPiece(id, item.Name, QualityOf(item), item.Level, bid, BotTradeTerms.Verdict.Wanted);
        }

        /// <summary>The bot's own open order (it is the buyer, the piece not made yet) this piece answers: same slot and kind.</summary>
        public static CraftOrder OpenOrderFor(GameBot bot, DbInventoryItem item, eInventorySlot slot)
        {
            if (bot == null || item == null)
                return null;
            try
            {
                string buyer = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
                return AutonomousBotCraftOrders.OfBuyer(buyer).Where(order => order.State == CraftOrderState.Open && order.BuyerBotId == bot.DatabaseID)
                    .FirstOrDefault(order => RecipeFits(order, item, slot));
            }
            catch
            {
                return null;
            }
        }

        private static bool RecipeFits(CraftOrder order, DbInventoryItem item, eInventorySlot slot)
        {
            if (string.Equals(order.ProductId, item.Id_nb, StringComparison.OrdinalIgnoreCase))
                return true;
            CraftRecipeInfo recipe = AutonomousBotCrafting.RecipesOf(order.Skill)
                .FirstOrDefault(r => string.Equals(r.ProductId, order.ProductId, StringComparison.OrdinalIgnoreCase));
            return recipe != null && recipe.ObjectType == item.Object_Type && recipe.ItemType == item.Item_Type &&
                   item.Level >= recipe.ProductLevel - 2 && (int)slot == item.Item_Type;
        }

        /// <summary>The job the window's boxes ask for, priced (or refused with the reason), or null for none.</summary>
        public static BotTradeTerms.JobQuote JobOf(BotTradeWindow window, string skills)
        {
            if (window == null || !window.Repairing && !window.Combine)
                return null;
            DbInventoryItem[] theirs = window.PlayerPieces.ToArray();
            if (window.Repairing)
                return RepairQuote(theirs, window.BotItems.Count, skills);
            return SpellcraftQuote(theirs, window.BotItems.Count, skills);
        }

        /// <summary>A repair (pure but for the item's own numbers): one piece, the bot's side empty, the material skill at 100% of the item's craft level.</summary>
        public static BotTradeTerms.JobQuote RepairQuote(IReadOnlyList<DbInventoryItem> theirs, int botPieces, string skills)
        {
            DbInventoryItem item = theirs?.Count == 1 ? theirs[0] : null;
            string name = item?.Name ?? "piece";
            if (theirs == null || theirs.Count != 1)
                return new(BotTradeTerms.Job.Repair, name, 0, "put in exactly one piece to repair");
            if (botPieces > 0)
                return new(BotTradeTerms.Job.Repair, name, 0, "a repair is a trade of its own");
            if (item.IsNotLosingDur)
                return new(BotTradeTerms.Job.Repair, name, 0, "it cannot be repaired");
            if (item.Condition >= item.MaxCondition)
                return new(BotTradeTerms.Job.Repair, name, 0, "it is fully repaired already");
            eCraftingSkill skill = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
            if (skill == eCraftingSkill.NoCrafting)
                return new(BotTradeTerms.Job.Repair, name, 0, "it cannot be repaired");
            int value = BotCraftingSkills.Value(skills, skill);
            int craftLevel = CraftingMgr.GetItemCraftLevel(item);
            if (!CraftRules.HasSkillToRepair(value, craftLevel, 100))
                return new(BotTradeTerms.Job.Repair, name, 0, value <= 0 ? $"it has no {SkillName(skill)}" : $"its {SkillName(skill)} ({value}) is too low for it");
            int chance = CraftRules.RepairChancePercent(value, craftLevel);
            return new(BotTradeTerms.Job.Repair, name, RepairFee(item), null, $"{chance}% to succeed, no charge if it fails");
        }

        public static long RepairFee(DbInventoryItem item)
        {
            long smith;
            try { smith = PlayerCraftingRules.SmithCost(item.RepairCost); }
            catch { smith = Math.Max(0, item?.RepairCost ?? 0); }
            return smith <= 0 ? 0 : Math.Max(1, (long)Math.Ceiling(smith * RepairFeePercentOfSmith / 100.0));
        }

        /// <summary>
        /// A spellcraft imbue (the player's rules: SpellcraftRules.CheckImbue with the bot's skill), the player's piece and
        /// gems in the window, no overcharge: a bot never risks the player's item.
        /// </summary>
        public static BotTradeTerms.JobQuote SpellcraftQuote(IReadOnlyList<DbInventoryItem> theirs, int botPieces, string skills)
        {
            DbInventoryItem[] gems = (theirs ?? []).Where(i => i.Object_Type == (int)eObjectType.SpellcraftGem).ToArray();
            DbInventoryItem[] targets = (theirs ?? []).Where(i => i.Object_Type != (int)eObjectType.SpellcraftGem).ToArray();
            string name = targets.Length == 1 ? targets[0].Name : "piece";
            if (targets.Length != 1 || gems.Length == 0)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, "put in one piece and the gems for it");
            if (botPieces > 0)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, "spellcraft is a trade of its own");
            DbInventoryItem item = targets[0];
            int skill = BotCraftingSkills.Value(skills, eCraftingSkill.SpellCrafting);
            if (skill <= 0)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, "it is no spellcrafter");
            if (!item.IsCrafted)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, "only crafted pieces take gems");
            ImbueGem[] imbue = gems.Select(GemOf).ToArray();
            ImbueRefusal refusal = SpellcraftRules.CheckImbue(BonusesOf(item), item.Level, item.Quality, item.Object_Type == (int)eObjectType.Staff,
                imbue, skill);
            if (refusal != ImbueRefusal.None)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, RefusalText(refusal, skill));
            int max = SpellcraftRules.MaxImbuePoints(item.Level, item.Quality);
            int points = SpellcraftRules.TotalImbuePoints(ExistingOf(item), SpellcraftRules.GemBonuses(imbue));
            if (points > max)
                return new(BotTradeTerms.Job.Spellcraft, name, 0, $"that is {points} of {max} imbue points, it will not overcharge your piece");
            return new(BotTradeTerms.Job.Spellcraft, name, SpellcraftFee(gems), null,
                $"{gems.Length} gem{(gems.Length == 1 ? string.Empty : "s")}, {points} of {max} imbue points");
        }

        public static long SpellcraftFee(IReadOnlyList<DbInventoryItem> gems) =>
            Math.Max(SpellcraftMinimumFeePerGem * gems.Count, gems.Sum(g => Math.Max(0L, g.Price)) * SpellcraftFeePercent / 100);

        private static string RefusalText(ImbueRefusal refusal, int skill) => refusal switch
        {
            ImbueRefusal.AlreadyImbued => "it is imbued already",
            ImbueRefusal.NotAGem => "only spellcraft gems go in",
            ImbueRefusal.FocusNotOnStaff => "focus gems go on staves only",
            ImbueRefusal.SameBonus => "two gems of the same bonus",
            ImbueRefusal.TooManyBonuses => "more than four bonuses",
            ImbueRefusal.NotEnoughSkill => $"its spellcraft ({skill}) is too low for those gems",
            ImbueRefusal.OverchargeTooFar => "far too many imbue points for that piece",
            _ => "it cannot do that",
        };

        public static ImbueGem GemOf(DbInventoryItem gem)
        {
            bool focus = false;
            try { focus = SkillBase.CheckPropertyType((eProperty)gem.Bonus1Type, ePropertyType.Focus); }
            catch { } // tests: no skill tables
            return new ImbueGem(gem.Object_Type, gem.Bonus1Type, gem.Bonus1, gem.Quality, focus);
        }

        public static ImbueBonus[] BonusesOf(DbInventoryItem item) =>
        [
            new(item.Bonus1Type, item.Bonus1), new(item.Bonus2Type, item.Bonus2), new(item.Bonus3Type, item.Bonus3), new(item.Bonus4Type, item.Bonus4),
        ];

        private static IEnumerable<(int Type, int Value)> ExistingOf(DbInventoryItem item) =>
            BonusesOf(item).TakeWhile(b => b.Type != 0).Select(b => (b.Type, b.Value));

        private static string SkillName(eCraftingSkill skill) => skill switch
        {
            eCraftingSkill.MetalWorking => "metalworking",
            eCraftingSkill.LeatherCrafting => "leatherworking",
            eCraftingSkill.ClothWorking => "clothworking",
            eCraftingSkill.WoodWorking => "woodworking",
            eCraftingSkill.GemCutting => "gemcutting",
            _ => skill.ToString().ToLowerInvariant(),
        };

        /// <summary>The bot's view of the window now (the window's valuer).</summary>
        public static BotTradeTerms.Offer OfferOf(BotTradeWindow window)
        {
            GameBot bot = window.Bot;
            string skills = bot.PersistentRecord?.SerializedCraftingSkills ?? string.Empty;
            BotTradeTerms.JobQuote job = JobOf(window, skills);
            DbInventoryItem[] theirs = window.PlayerPieces.ToArray();
            BotTradeTerms.PlayerPiece[] pieces;
            if (job != null)
                pieces = theirs.Select(item => new BotTradeTerms.PlayerPiece(item.ObjectId, item.Name, QualityOf(item), item.Level, 0,
                    item.Object_Type == (int)eObjectType.SpellcraftGem ? BotTradeTerms.Verdict.JobGem : BotTradeTerms.Verdict.JobItem)).ToArray();
            else
                pieces = theirs.Select(item => Appraise(bot, item, theirs)).ToArray();
            long purse = 0;
            try { purse = PurseOf(bot); }
            catch { } // tests: no saved purse
            return new BotTradeTerms.Offer(bot.Name, window.BotItems.Select(b => b.Piece).ToArray(), pieces, window.TradeMoney,
                BotTradeTerms.PurseCapOf(purse, PurseShare), job);
        }

        #endregion

        #region The trade itself

        /// <summary>
        /// Moves everything at once, on the game loop, or nothing: the pieces both ways (the bot's from its backpack, the
        /// player's from theirs, each saved with its new owner), then the coin (the player's owed to the bot, the bot's to the
        /// player, an order's escrow refunded to the bot first), then a job on the player's piece. A step that fails puts back
        /// what moved.
        /// </summary>
        public static bool ExecuteTrade(BotTradeWindow window)
        {
            GamePlayer player = window.Owner;
            GameBot bot = window.Bot;
            BotTradeTerms.Quote quote = window.Evaluate();
            BotTradeTerms.Offer offer = OfferOf(window);
            void Say(string text) => window.Tell?.Invoke(player, text);
            if (!quote.Accepts)
                return false;
            // WP8's hand-over reach (about a /say's) counts too: a commission given at the break without a window.
            if (!InReach(bot, player) && !CrafterCommerce.IsNear(bot, player) || BusyOf(bot) != Busy.Free)
            {
                Say($"{bot.Name} cannot trade now.");
                return false;
            }
            if (player.Inventory == null || bot.Inventory == null)
                return false;
            BotTradeWindow.BotItem[] mine = window.BotItems.ToArray();
            DbInventoryItem[] theirs = window.PlayerPieces.ToArray();
            BotTradeTerms.PlayerPiece[] verdicts = offer.PlayerPieces.ToArray();
            bool job = offer.Job != null && !offer.Job.Refused;
            DbInventoryItem[] bought = job ? [] : theirs.Where(item => verdicts.Any(v => v.ObjectId == item.ObjectId &&
                v.Verdict is BotTradeTerms.Verdict.Wanted or BotTradeTerms.Verdict.ForOrder)).ToArray();

            // Still where they were?
            DbInventoryItem[] botBag = bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Where(i => i != null).ToArray();
            if (mine.Any(b => !botBag.Contains(b.Item)))
            {
                Say($"{bot.Name} no longer has that.");
                return false;
            }
            DbInventoryItem[] playerBag = player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Where(i => i != null).ToArray();
            if (theirs.Any(item => !playerBag.Contains(item)))
            {
                Say("You no longer have everything you put in.");
                return false;
            }
            if (quote.Owed > 0 && WalletOf(player) < quote.Owed)
            {
                Say("You don't have enough money.");
                return false;
            }
            int playerFree = FreeSlots(player.Inventory) + bought.Length;
            int botFree = FreeSlots(bot.Inventory) + mine.Length;
            if (playerFree < mine.Length)
            {
                Say("You don't have enough space in your inventory.");
                return false;
            }
            if (botFree < bought.Length)
            {
                Say($"{bot.Name} has no room for that in its bags.");
                return false;
            }

            // The orders the player filled for the bot: called off first, the escrow back in the bot's purse (CraftOrder).
            long purseBefore = 0;
            try { purseBefore = PurseOf(bot); } catch { }
            foreach (BotTradeTerms.PlayerPiece filled in verdicts.Where(v => v.Verdict == BotTradeTerms.Verdict.ForOrder && v.OrderId != null))
                if (!job && bought.Any(b => b.ObjectId == filled.ObjectId))
                    AutonomousBotCraftOrders.TryCancelOpen(filled.OrderId, "a player sold the bot the piece");
            if (quote.BotPays > 0)
            {
                long purse = 0;
                try { purse = PurseOf(bot); } catch { }
                if (purse < quote.BotPays)
                {
                    Say($"{bot.Name} is short of coin.");
                    return false;
                }
            }

            // The pieces.
            var movedToPlayer = new List<(DbInventoryItem Item, int From)>();
            var movedToBot = new List<(DbInventoryItem Item, int From)>();
            bool itemsMoved = true;
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
            {
                foreach (DbInventoryItem item in bought)
                {
                    int from = item.SlotPosition;
                    if (!player.Inventory.RemoveItemWithoutDbDeletion(item))
                    {
                        itemsMoved = false;
                        break;
                    }
                    movedToBot.Add((item, from));
                }
                if (itemsMoved)
                    foreach (BotTradeWindow.BotItem b in mine)
                    {
                        int from = b.Item.SlotPosition;
                        if (!bot.Inventory.RemoveItemWithoutDbDeletion(b.Item))
                        {
                            itemsMoved = false;
                            break;
                        }
                        movedToPlayer.Add((b.Item, from));
                    }
                var landedPlayer = new List<DbInventoryItem>();
                var landedBot = new List<DbInventoryItem>();
                if (itemsMoved)
                    foreach ((DbInventoryItem item, _) in movedToPlayer)
                    {
                        eInventorySlot to = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                        if (to == eInventorySlot.Invalid || !player.Inventory.AddItemWithoutDbAddition(to, item))
                        {
                            itemsMoved = false;
                            break;
                        }
                        landedPlayer.Add(item);
                    }
                if (itemsMoved)
                    foreach ((DbInventoryItem item, _) in movedToBot)
                    {
                        eInventorySlot to = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                        if (to == eInventorySlot.Invalid || !bot.Inventory.AddItem(to, item))
                        {
                            itemsMoved = false;
                            break;
                        }
                        item.OwnerID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
                        landedBot.Add(item);
                    }
                if (!itemsMoved)
                {
                    foreach (DbInventoryItem item in landedPlayer)
                        player.Inventory.RemoveItemWithoutDbDeletion(item);
                    foreach (DbInventoryItem item in landedBot)
                        bot.Inventory.RemoveItemWithoutDbDeletion(item);
                    foreach ((DbInventoryItem item, int from) in movedToPlayer)
                        bot.Inventory.AddItem((eInventorySlot)from, item);
                    foreach ((DbInventoryItem item, int from) in movedToBot)
                    {
                        player.Inventory.AddItemWithoutDbAddition((eInventorySlot)from, item);
                        item.OwnerID = player.InternalID;
                    }
                }
                else
                {
                    foreach (DbInventoryItem item in landedPlayer.Concat(landedBot))
                        SaveItem(item);
                }
            }
            if (!itemsMoved)
            {
                Say("The trade could not be completed; nothing changed hands.");
                return false;
            }

            // The coin.
            if (quote.Owed > 0)
            {
                if (!CrafterCommerce.TakeCoin(player, quote.Owed))
                {
                    UndoItems(player, bot, movedToPlayer, movedToBot);
                    Say("You don't have enough money.");
                    return false;
                }
                BotEarn(bot.DatabaseID, quote.Owed, EconomyFlowKind.PlayerTrade);
            }
            if (quote.BotPays > 0)
            {
                if (!BotSpend(bot.DatabaseID, quote.BotPays, EconomyFlowKind.PlayerTrade))
                {
                    if (quote.Owed > 0 && BotSpend(bot.DatabaseID, quote.Owed, EconomyFlowKind.PlayerTrade))
                        CrafterCommerce.GiveCoin(player, quote.Owed);
                    UndoItems(player, bot, movedToPlayer, movedToBot);
                    Say($"{bot.Name} is short of coin; nothing changed hands.");
                    return false;
                }
                CrafterCommerce.GiveCoin(player, quote.BotPays);
            }

            // A job on the player's piece.
            string jobResult = null;
            if (job)
                jobResult = DoJob(window, offer.Job, bot, player);

            try { InventoryLogging.LogInventoryAction(player, bot, eInventoryActionType.Trade, quote.Owed - quote.BotPays); }
            catch { } // tests: no inventory log
            foreach (DbInventoryItem item in bought)
                Equip(bot, item);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            Report(window, quote, offer, mine, bought, jobResult);
            return true;
        }

        private static int FreeSlots(IGameInventory inventory)
        {
            int free = 0;
            for (var slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
                if (inventory.GetItem(slot) == null)
                    free++;
            return free;
        }

        private static void SaveItem(DbInventoryItem item)
        {
            try { GameServer.Database.SaveObject(item); }
            catch { } // tests: no database
        }

        private static void UndoItems(GamePlayer player, GameBot bot, List<(DbInventoryItem Item, int From)> toPlayer, List<(DbInventoryItem Item, int From)> toBot)
        {
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
            {
                foreach ((DbInventoryItem item, int from) in toPlayer)
                {
                    player.Inventory.RemoveItemWithoutDbDeletion(item);
                    bot.Inventory.AddItem((eInventorySlot)from, item);
                    item.OwnerID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
                    SaveItem(item);
                }
                foreach ((DbInventoryItem item, int from) in toBot)
                {
                    bot.Inventory.RemoveItemWithoutDbDeletion(item);
                    player.Inventory.AddItemWithoutDbAddition((eInventorySlot)from, item);
                    item.OwnerID = player.InternalID;
                    SaveItem(item);
                }
            }
        }

        /// <summary>The job, done on the player's piece in the player's bag. Returns what happened, for the line and the log.</summary>
        private static string DoJob(BotTradeWindow window, BotTradeTerms.JobQuote quote, GameBot bot, GamePlayer player)
        {
            DbInventoryItem[] theirs = window.PlayerPieces.ToArray();
            if (quote.Kind == BotTradeTerms.Job.Repair)
            {
                DbInventoryItem item = theirs.Single();
                eCraftingSkill skill = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
                int value = BotCraftingSkills.Value(bot.PersistentRecord?.SerializedCraftingSkills, skill);
                int chance = CraftRules.RepairChancePercent(value, CraftingMgr.GetItemCraftLevel(item));
                if (!Roll(chance))
                {
                    if (quote.Fee > 0 && BotSpend(bot.DatabaseID, quote.Fee, EconomyFlowKind.PlayerTrade))
                        CrafterCommerce.GiveCoin(player, quote.Fee);
                    window.Tell?.Invoke(player, $"{bot.Name} fails to improve the condition of your {item.Name}." +
                        (quote.Fee > 0 ? $" Your {BotTradeTerms.PriceText(quote.Fee)} is returned." : string.Empty));
                    return "failed";
                }
                Repair.ModifyConditionAndDurability(item);
                SaveItem(item);
                player.Out?.SendInventorySlotsUpdate([(eInventorySlot)item.SlotPosition]);
                window.Tell?.Invoke(player, $"{bot.Name} repairs your {item.Name} ({item.ConditionPercent}% condition).");
                return "repaired";
            }
            DbInventoryItem target = theirs.Single(i => i.Object_Type != (int)eObjectType.SpellcraftGem);
            DbInventoryItem[] gems = theirs.Where(i => i.Object_Type == (int)eObjectType.SpellcraftGem).ToArray();
            ApplyGems(target, gems, player);
            window.Tell?.Invoke(player, $"{bot.Name} imbues your {target.Name}: {string.Join(", ", gems.Select(g => g.Name))}.");
            return "imbued";
        }

        /// <summary>The gems into the piece's free bonus slots, as SpellCrafting's success (each gem used up), the piece saved.</summary>
        public static void ApplyGems(DbInventoryItem item, IReadOnlyList<DbInventoryItem> gems, GamePlayer owner)
        {
            owner?.Inventory?.BeginChanges();
            try
            {
                foreach (DbInventoryItem gem in gems)
                {
                    if (item.Bonus1Type == 0) { item.Bonus1Type = gem.Bonus1Type; item.Bonus1 = gem.Bonus1; }
                    else if (item.Bonus2Type == 0) { item.Bonus2Type = gem.Bonus1Type; item.Bonus2 = gem.Bonus1; }
                    else if (item.Bonus3Type == 0) { item.Bonus3Type = gem.Bonus1Type; item.Bonus3 = gem.Bonus1; }
                    else if (item.Bonus4Type == 0) { item.Bonus4Type = gem.Bonus1Type; item.Bonus4 = gem.Bonus1; }
                    owner?.Inventory?.RemoveCountFromStack(gem, 1);
                    if (owner != null)
                        InventoryLogging.LogInventoryAction(owner, "(craft)", eInventoryActionType.Craft, gem.Template);
                }
            }
            finally
            {
                owner?.Inventory?.CommitChanges();
            }
            try
            {
                if (item.Template is DbItemUnique unique)
                {
                    GameInventoryObjectExtensions.SaveItem(item);
                    GameServer.Database.SaveObject(unique);
                }
            }
            catch { } // tests: no database
        }

        private static void Report(BotTradeWindow window, BotTradeTerms.Quote quote, BotTradeTerms.Offer offer, BotTradeWindow.BotItem[] mine,
            DbInventoryItem[] bought, string jobResult)
        {
            GamePlayer player = window.Owner;
            GameBot bot = window.Bot;
            void Say(string text) => window.Tell?.Invoke(player, text);
            // WP8's own windows (a commission handed over, a stock sale agreed by tell) log, remember and talk themselves.
            bool commissionOnly = mine.Length > 0 && bought.Length == 0 && offer.Job == null &&
                                  mine.All(b => b.Piece.Source is BotTradeTerms.Source.Delivery or BotTradeTerms.Source.Prepaid);
            bool wp8 = window.Completed != null || commissionOnly;
            foreach (BotTradeWindow.BotItem b in mine.Where(b => b.Piece.Source is BotTradeTerms.Source.Delivery or BotTradeTerms.Source.Prepaid))
                Say(b.Piece.Source == BotTradeTerms.Source.Prepaid ? $"{bot.Name} hands you {b.Item.Name}."
                    : $"{bot.Name} hands you {b.Item.Name} for {BotTradeTerms.PriceText(b.Piece.Ask)}.");
            foreach (BotTradeWindow.BotItem b in mine.Where(b => b.Piece.Source != BotTradeTerms.Source.Delivery && b.Piece.Source != BotTradeTerms.Source.Prepaid))
            {
                Say($"You buy {b.Item.Name} from {bot.Name} for {BotTradeTerms.PriceText(b.Piece.Ask)}.");
                if (!wp8 && Log.IsInfoEnabled)
                    Log.Info(EconomyLog.Line(EconomyLog.EconPlayerDelivery, ("source", b.Piece.Source.ToString().ToLowerInvariant()), ("realm", bot.Realm),
                        ("player", player.Name), ("crafter", bot.Name), ("crafterId", bot.DatabaseID), ("item", b.Item.Name), ("quality", b.Item.Quality),
                        ("copper", b.Piece.Ask), ("pay", "trade"), ("paid", true)));
            }
            foreach (DbInventoryItem item in bought)
            {
                BotTradeTerms.PlayerPiece verdict = offer.PlayerPieces.FirstOrDefault(v => v.ObjectId == item.ObjectId);
                Say(quote.CappedByPurse ? $"You sell {item.Name} to {bot.Name}."
                    : $"You sell {item.Name} to {bot.Name} for {BotTradeTerms.PriceText(verdict?.Bid ?? 0)}.");
                if (Log.IsInfoEnabled)
                    Log.Info(EconomyLog.Line(EconomyLog.EconPlayerSell, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm), ("level", bot.Level),
                        ("player", player.Name), ("item", item.Name), ("quality", item.Quality), ("itemLevel", item.Level), ("copper", verdict?.Bid ?? 0),
                        ("order", verdict?.OrderId ?? string.Empty), ("worn", item.SlotPosition < (int)eInventorySlot.FirstBackpack)));
            }
            if (offer.Job != null && jobResult != null && Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconScJob, ("job", offer.Job.Kind.ToString().ToLowerInvariant()), ("bot", bot.Name), ("id", bot.DatabaseID),
                    ("realm", bot.Realm), ("player", player.Name), ("item", offer.Job.ItemName), ("copper", jobResult == "failed" ? 0 : offer.Job.Fee),
                    ("result", jobResult)));
            if (quote.Owed > 0)
                Say($"You pay {bot.Name} {BotTradeTerms.PriceText(quote.Owed)}.");
            else if (quote.BotPays > 0)
                Say($"{bot.Name} pays you {BotTradeTerms.PriceText(quote.BotPays)}.");
            LogTrade("done", bot, player, window.Purpose, ("owed", quote.Owed), ("paid", quote.BotPays), ("given", mine.Length), ("bought", bought.Length),
                ("job", offer.Job?.Kind.ToString().ToLowerInvariant() ?? "none"));
            if (!wp8 && quote.Owed > 0 && SafeIsCrafter(bot))
            {
                CrafterCustomers.NoteDeal(bot.DatabaseID, CrafterCommerce.CustomerIdOf(player), player.Name, quote.Owed, CrafterCommerce.UtcNow());
                BotCrafterRoster.Instance.NoteEarned(bot.DatabaseID, quote.Owed);
            }
            string topic = bought.Length > 0 && mine.Length > 0 ? "trade_done_swap" : bought.Length > 0 ? "trade_done_bought"
                : offer.Job != null ? (jobResult == "failed" ? "trade_job_failed" : "trade_done_job") : "trade_done_sold";
            string text = topic switch
            {
                "trade_done_swap" => "fair swap, ty",
                "trade_done_bought" => "ty, just what i needed",
                "trade_done_job" => "there ya go",
                "trade_job_failed" => "sorry, didnt take. no charge",
                _ => "enjoy, ty",
            };
            if (!commissionOnly)
                SayTo(bot, player, Voiced(bot, topic, text, Slots(bot, player)));
        }

        private static bool SafeIsCrafter(GameBot bot)
        {
            try { return AutonomousBotCrafting.IsCrafter(bot); }
            catch { return false; }
        }

        private static void LogTrade(string outcome, GameBot bot, GamePlayer player, string how, params (string Key, object Value)[] more)
        {
            if (!Log.IsInfoEnabled || bot == null)
                return;
            var fields = new List<(string, object)>
            {
                ("outcome", outcome), ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm), ("player", player?.Name ?? string.Empty),
                ("how", how ?? string.Empty),
            };
            fields.AddRange(more);
            Log.Info(EconomyLog.Line(EconomyLog.EconPlayerTrade, fields.ToArray()));
        }

        #endregion

        #region Drag and drop, tells, the command

        /// <summary>
        /// A player drops a piece of theirs on a gamebot (BotCommerceSeams.TryReceiveItem): a trade opens with it in the
        /// player's side (the client's own way to start a trade with a player), or it joins the open one.
        /// </summary>
        public static bool TryReceiveItem(GameBot bot, GamePlayer player, DbInventoryItem item)
        {
            if (!CanTradeWith(bot, player) || item == null)
                return false;
            BotTradeWindow window = player.TradeWindow as BotTradeWindow;
            if (window == null || window.Bot != bot)
            {
                window = TryOpen(player, bot, "drop", out string why);
                if (window == null)
                {
                    if (why != null)
                        TellPlayer(player, why);
                    return true; // handled: nothing moves
                }
            }
            if (!window.AddItemToTrade(item))
                TellPlayer(player, $"You can't trade {item.Name}.");
            return true;
        }

        /// <summary>Coin dropped on a gamebot that waits for no deposit: a trade opens with the coin in (or it joins the open one).</summary>
        public static bool TryReceiveMoney(GameBot bot, GamePlayer player, long copper)
        {
            if (!CanTradeWith(bot, player) || copper <= 0)
                return false;
            BotTradeWindow window = player.TradeWindow as BotTradeWindow;
            if (window == null || window.Bot != bot)
            {
                window = TryOpen(player, bot, "coin", out string why);
                if (window == null)
                {
                    if (why != null)
                        TellPlayer(player, why);
                    return true;
                }
            }
            window.AddMoneyToTrade(copper);
            return true;
        }

        private static readonly Regex SellListRegex = new(
            @"\b(wts|what (are|r) (you|u) selling|(you|u) selling anything|anything (for sale|to sell)|what('?s| is) for sale|selling anything|whatcha selling|what (do|d) (you|u) (have|got) for sale|(show|list) (me )?(your|ur) (stuff|goods|wares|stock))\b",
            RegexOptions.Compiled);
        private static readonly Regex BuyNumberRegex = new(@"^(?:i'?ll take |buy |want |gimme |give me )?(?:#|no\.? ?|number )?(\d{1,2})\W*$|^(?:buy|want|take)\s+(?:#|no\.? ?|number )?(\d{1,2})\b",
            RegexOptions.Compiled);
        private static readonly Regex OpenTradeRegex = new(@"\b(trade me|open (a )?trade|lets trade|let'?s trade|wanna trade|want to trade|can we trade|trade\?)\b",
            RegexOptions.Compiled);
        private static readonly Regex SellToBotRegex = new(
            @"\b((want to|wanna) buy (my|this)|buy (my|this)|i'?ll sell (you|u)|selling (you|u)|(are|r) (you|u) buying|(you|u) buying)\b",
            RegexOptions.Compiled);

        private static string Norm(string text) => Regex.Replace((text ?? string.Empty).ToLowerInvariant().Replace('’', '\''), @"\s+", " ").Trim();

        /// <summary>
        /// A tell about trading with any gamebot (after WP8's crafter talk): "what are you selling", "buy 2" (with a window
        /// open or near enough to open one), "trade me", "want my sword?". True when answered.
        /// </summary>
        public static bool OnTell(GamePlayer player, GameBot bot, string message)
        {
            if (!CanTradeWith(bot, player) || string.IsNullOrWhiteSpace(message))
                return false;
            string t = Norm(message);
            Match number = BuyNumberRegex.Match(t);
            if (number.Success)
            {
                int n = int.Parse(number.Groups[1].Success ? number.Groups[1].Value : number.Groups[2].Value, CultureInfo.InvariantCulture);
                if (player.TradeWindow is not BotTradeWindow open || open.Bot != bot)
                {
                    if (TryOpen(player, bot, "tell", out string why) == null)
                    {
                        if (why != null && !why.Contains("fighting") && !why.Contains("raid"))
                            SayTo(bot, player, Voiced(bot, "trade_come_closer", "come closer and ill open a trade", Slots(bot, player)));
                        return true;
                    }
                }
                if (!Buy(player, n, out string reason))
                    TellPlayer(player, reason);
                return true;
            }
            if (SellListRegex.IsMatch(t))
            {
                IReadOnlyList<Good> goods = GoodsOf(bot);
                LastGoods[(bot.DatabaseID, player.Name)] = goods;
                Dictionary<string, string> slots = Slots(bot, player);
                if (goods.Count == 0)
                {
                    SayTo(bot, player, Voiced(bot, "trade_nothing_for_sale", "nothing for sale atm, sorry", slots));
                    return true;
                }
                Good first = goods[0];
                slots["item"] = first.Item.Name.ToLowerInvariant();
                slots["price"] = BotCrafterVoice.PriceText(first.Ask);
                slots["count"] = goods.Count.ToString(CultureInfo.InvariantCulture);
                CrafterCommerce.SendTell(bot, player, Voiced(bot, "trade_for_sale",
                    goods.Count == 1 ? $"just a {slots["item"]}, {slots["price"]}. trade me if you want it"
                        : $"got {goods.Count} things, {slots["item"]} for {slots["price"]} and more. trade me and have a look", slots));
                foreach (string line in GoodsLines(bot, goods))
                    TellPlayer(player, line);
                return true;
            }
            if (OpenTradeRegex.IsMatch(t))
            {
                if (TryOpen(player, bot, "tell", out string why) == null && why != null)
                {
                    if (why.StartsWith("You are too far", StringComparison.Ordinal))
                        SayTo(bot, player, Voiced(bot, "trade_come_closer", "come closer and ill open a trade", Slots(bot, player)));
                    else
                        TellPlayer(player, why);
                }
                return true;
            }
            if (SellToBotRegex.IsMatch(t) && !CrafterTalk.AsksForPiece(t))
            {
                Dictionary<string, string> slots = Slots(bot, player);
                BotGearPipeline.Gap[] gaps = Array.Empty<BotGearPipeline.Gap>();
                try { gaps = BotGearPipeline.FindGaps(bot.Level, BotGearSet.Worn(bot)); } catch { }
                if (gaps.Length > 0)
                {
                    slots["item"] = SlotWord(gaps[0].Slot);
                    SayTo(bot, player, Voiced(bot, "trade_wants_gap", $"always looking for a better {slots["item"]}. trade me and show me", slots));
                }
                else
                    SayTo(bot, player, Voiced(bot, "trade_wants_maybe", "maybe, put it in a trade and ill tell you what its worth to me", slots));
                return true;
            }
            return false;
        }

        public static string SlotWord(eInventorySlot slot) => slot switch
        {
            eInventorySlot.TorsoArmor => "chest piece",
            eInventorySlot.LegsArmor => "pair of legs",
            eInventorySlot.ArmsArmor => "pair of sleeves",
            eInventorySlot.HeadArmor => "helm",
            eInventorySlot.HandsArmor => "pair of gloves",
            eInventorySlot.FeetArmor => "pair of boots",
            eInventorySlot.RightHandWeapon => "weapon",
            eInventorySlot.LeftHandWeapon => "offhand",
            eInventorySlot.TwoHandWeapon => "two-hander",
            eInventorySlot.DistanceWeapon => "bow",
            eInventorySlot.Cloak => "cloak",
            _ => "piece",
        };

        /// <summary>
        /// /tradewindow: open (with the bot targeted), list, buy &lt;n&gt;, put &lt;slot&gt;, take &lt;slot&gt;, coin &lt;copper&gt;,
        /// repair on|off, combine on|off, accept, close. The original client has its own window for all but open, list and
        /// buy; the new client's trade window sends these. Returns the lines to show.
        /// </summary>
        public static string[] Command(GamePlayer player, string[] args)
        {
            if (player == null)
                return [];
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "open";
            BotTradeWindow window = player.TradeWindow as BotTradeWindow;
            string Arg(int i) => args.Length > i ? args[i] : string.Empty;
            switch (verb)
            {
                case "open":
                {
                    if (!Enabled)
                        return ["Trading with gamebots is off on this server (bot_crafters and bot_crafter_commerce)."];
                    if (player.TargetObject is not GameBot bot)
                        return ["Target a gamebot of your realm and type /tradewindow to trade with it."];
                    return TryOpen(player, bot, "command", out string why) == null ? [why ?? "No trade."] : [];
                }
                case "list":
                {
                    GameBot bot = window?.Bot ?? player.TargetObject as GameBot;
                    if (bot == null || !CanTradeWith(bot, player))
                        return ["Target a gamebot of your realm first."];
                    IReadOnlyList<Good> goods = GoodsOf(bot);
                    LastGoods[(bot.DatabaseID, player.Name)] = goods;
                    return GoodsLines(bot, goods);
                }
                case "buy":
                    return int.TryParse(Arg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && Buy(player, n, out string whyNot) ? []
                        : [n <= 0 ? "Usage: /tradewindow buy <number from the list>" : "Not added."];
                case "put":
                case "take":
                {
                    if (window == null)
                        return ["No trade with a gamebot is open."];
                    if (!int.TryParse(Arg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                        return [$"Usage: /tradewindow {verb} <backpack slot>"];
                    DbInventoryItem item = player.Inventory?.GetItem((eInventorySlot)slot);
                    if (item == null || slot < (int)eInventorySlot.FirstBackpack || slot > (int)eInventorySlot.LastBackpack)
                        return ["Only items in your backpack can be traded."];
                    if (verb == "put")
                        return window.AddItemToTrade(item) ? [] : [$"You can't trade {item.Name}."];
                    window.RemoveItemToTrade(item);
                    return [];
                }
                case "coin":
                {
                    if (window == null)
                        return ["No trade with a gamebot is open."];
                    if (!long.TryParse(Arg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out long copper) || copper < 0)
                        return ["Usage: /tradewindow coin <copper>"];
                    window.TradeMoney = Math.Min(copper, WalletOf(player));
                    window.TradeUpdate();
                    return [];
                }
                case "repair":
                case "combine":
                {
                    if (window == null)
                        return ["No trade with a gamebot is open."];
                    bool on = !string.Equals(Arg(2), "off", StringComparison.OrdinalIgnoreCase);
                    if (verb == "repair")
                        window.Repairing = on;
                    else
                        window.Combine = on;
                    window.TradeUpdate();
                    return [];
                }
                case "accept":
                    if (window == null)
                        return ["No trade with a gamebot is open."];
                    window.AcceptTrade();
                    return [];
                case "close":
                case "cancel":
                    window?.CloseTrade();
                    return [];
                default:
                    return ["Usage: /tradewindow [open|list|buy <n>|put <slot>|take <slot>|coin <copper>|repair on|off|combine on|off|accept|close]"];
            }
        }

        #endregion

        #region Registration and the want-to-buy adverts

        private static int _registered;

        /// <summary>Hooks the full trade into the seams (WP8's Register calls it): drops on a bot, the want-to-buy adverts.</summary>
        public static void Register()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
                return;
            BotServiceSeams.Register(WantToBuyAdvert.Instance);
        }

        /// <summary>One gap advert per realm in this many milliseconds; a share of task breaks with a gap, at chat rate 1.</summary>
        public const long RealmAdvertRestMs = 8 * 60_000;
        public const double AdvertChance = 0.15;

        private static readonly ConcurrentDictionary<eRealm, long> LastAdvert = new();

        /// <summary>
        /// A gamebot's want-to-buy in /trade (its WP6 gear gap, said as a player would: "wtb chain chest piece lvl 25, pst"):
        /// at a task break now and then, with a real player in the realm to read it. {item} is the real piece it lacks,
        /// {budget} the most it would pay (its purse share). Off with the commerce switches; bot_crafter_chat_rate scales it.
        /// </summary>
        public sealed class WantToBuyAdvert : IBotTaskBreakHook
        {
            public static readonly WantToBuyAdvert Instance = new();
            public string Name => "want-to-buy advert (WP8 trade)";

            public void AtTaskBreak(GameBot bot, TaskBreakPoint point)
            {
                if (point != TaskBreakPoint.Begin || !Enabled || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
                    bot.IsHiredCompanion || bot.DatabaseID <= 0 || BotCrafterVoice.ChatRate <= 0 || Random.Shared.NextDouble() >= AdvertChance * BotCrafterVoice.ChatRate)
                    return;
                try
                {
                    BotGearPipeline.Gap[] gaps = BotGearPipeline.FindGaps(bot.Level, BotGearSet.Worn(bot));
                    if (gaps.Length == 0 || !AutonomousLivingServer.RealmHasPlayer(bot.Realm))
                        return;
                    string item = WantedText(bot, gaps[0]);
                    long budget = BotTradeTerms.PurseCapOf(PurseOf(bot), PurseShare);
                    if (item == null || budget <= 0)
                        return;
                    AutonomousLivingServer.Later(bot, 3_000 + Random.Shared.Next(9_000), () => AutonomousLivingServer.CanTalk(bot), () =>
                    {
                        long now = Now();
                        if (LastAdvert.TryGetValue(bot.Realm, out long last) && now - last < RealmAdvertRestMs)
                            return;
                        LastAdvert[bot.Realm] = now;
                        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                            { ["item"] = item, ["budget"] = BotCrafterVoice.PriceText(budget) };
                        string text = AutonomousBotVoices.TryLine(bot, "trade", out VC.Picked picked, flags: ["wants_gear"], requireNeed: "wants_gear",
                            slots: slots) ? picked.Text : $"wtb {item}, pst";
                        BotCrafterVoice.SendTrade(bot, text);
                    });
                }
                catch (Exception exception)
                {
                    Log.Warn($"Bot trade: the want-to-buy advert of {bot.Name} failed", exception);
                }
            }
        }

        /// <summary>"chain chest piece lvl 25": the bot's armour type, the slot of its first gap, its level. Null for an unknown armour.</summary>
        public static string WantedText(GameBot bot, BotGearPipeline.Gap gap)
        {
            eObjectType armor = BotGearPipeline.ArmorTypeFor(bot);
            string kind = armor switch
            {
                eObjectType.Cloth => "cloth",
                eObjectType.Leather => "leather",
                eObjectType.Studded => "studded",
                eObjectType.Chain => "chain",
                eObjectType.Plate => "plate",
                eObjectType.Reinforced => "reinforced",
                eObjectType.Scale => "scale",
                _ => null,
            };
            if (kind == null)
                return null;
            string quality = gap.Reason == BotGearPipeline.GapReason.Template ? " 99%+" : string.Empty;
            return $"{kind} {SlotWord(gap.Slot)} lvl {bot.Level}{quality}";
        }

        #endregion

        #region Voice

        private static Dictionary<string, string> Slots(GameBot bot, GamePlayer player) => new(StringComparer.Ordinal)
        {
            ["customer"] = player?.Name ?? string.Empty,
            ["zone"] = AutonomousLivingServer.PlaceOf(bot),
        };

        /// <summary>A trade line in the bot's own voice: a corpus line of the topic (kind trade_reply) with its slots, else the fixed words styled.</summary>
        public static string Voiced(GameBot bot, string topic, string text, IReadOnlyDictionary<string, string> slots)
        {
            try
            {
                if (topic != null && AutonomousBotVoices.TryLine(bot, "trade_reply", out VC.Picked picked, topic: topic, slots: slots, requireNeed: null) &&
                    !string.IsNullOrWhiteSpace(picked.Text))
                    return picked.Text;
                return AutonomousBotVoices.StyleFor(bot, text);
            }
            catch
            {
                return text;
            }
        }

        #endregion
    }
}
