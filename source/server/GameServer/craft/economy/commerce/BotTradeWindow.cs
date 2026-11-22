using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// A trade window between a real player and an autonomous gamebot, in both directions (Crafting 2.0 WP8, built out
    /// fully on the owner's word of 2026-11-16). It is the client's own trade window: the bot's side shows the real pieces
    /// it gives (from its bag) and the coin it pays; the player's side is whatever the player drags in, the coin the player
    /// puts in, and the Repair and Combine boxes.
    /// <para>
    /// The bot weighs every change (<see cref="Valuer"/>, set by <see cref="BotTrades"/>; the terms are
    /// <see cref="BotTradeTerms"/>): what it asks for its pieces, what it bids for the player's, a repair or spellcraft job,
    /// the coin it pays (escrow first, then its purse within its purse share). It accepts by itself once the terms hold,
    /// and says "&lt;bot&gt; has accepted the trade." as a player would; any change takes the acceptance back. Nothing moves
    /// until the player accepts too: then <see cref="Execute"/> moves everything at once or nothing at all.
    /// </para>
    /// <para>
    /// Only the player has a window: the bot is no GamePlayer, so <see cref="Partner"/> is null and the packet names the
    /// bot through <see cref="IBotTradeWindow"/> ("Trading with &lt;bot&gt;"). The WP8 constructor (fixed pieces at a fixed
    /// price) still works: a commission handed over or a stock piece sold, with no other valuer.
    /// </para>
    /// </summary>
    public sealed class BotTradeWindow : ITradeWindow, IBotTradeWindow
    {
        /// <summary>Most pieces on either side (the client's window has ten places a side).</summary>
        public const int MaxItems = 10;

        /// <summary>A piece on the bot's side: the real item and the terms it is offered on.</summary>
        public sealed record BotItem(DbInventoryItem Item, BotTradeTerms.BotPiece Piece);

        private readonly GamePlayer _owner;
        private readonly GameBot _bot;
        private readonly List<BotItem> _botItems = new();
        private ArrayList _partnerItems = new(MaxItems);
        private ArrayList _playerItems = new(MaxItems);
        private long _money;
        private bool _repair, _combine;
        private readonly Lock _lock = new();
        private bool _done, _closed;
        private BotTradeTerms.Quote _quote;
        private bool _announcedAccept;
        private HashSet<string> _saidLines = new(StringComparer.Ordinal);

        /// <summary>What the trade is for (the order ids handed over, "stock:&lt;id&gt;", or "trade" for a player's own).</summary>
        public string Purpose { get; set; }

        /// <summary>When the window opened (game-loop milliseconds), for the bot's hold while it trades.</summary>
        public long OpenedAt { get; } = SafeNow();

        /// <summary>Called once, after a completed trade (coin and pieces moved), on the game loop.</summary>
        public Action<BotTradeWindow> Completed { get; set; }

        /// <summary>Called once when the window closes, completed or not (the bot's hold ends; the trade is logged).</summary>
        public Action<BotTradeWindow, bool> Closed { get; set; }

        /// <summary>
        /// Builds the bot's view of the trade from the window as it stands (BotTrades.OfferOf). Null: the WP8 default, the
        /// bot's fixed pieces at their price, every piece the player puts in refused.
        /// </summary>
        public Func<BotTradeWindow, BotTradeTerms.Offer> Valuer { get; set; }

        /// <summary>A system line to the player (replaceable by tests).</summary>
        public Action<GamePlayer, string> Tell { get; set; } = DefaultTell;

        /// <summary>Moves the coin and the pieces; replaceable by tests. True when everything moved (or nothing did).</summary>
        public static Func<BotTradeWindow, bool> Execute = DefaultExecute;

        /// <summary>The live trade (BotTrades.ExecuteTrade); a static default so WP8's tests can replace <see cref="Execute"/>.</summary>
        public static Func<BotTradeWindow, bool> LiveExecute = window => BotTrades.ExecuteTrade(window);

        public static void ResetForTests() => Execute = DefaultExecute;

        private static bool DefaultExecute(BotTradeWindow window) => LiveExecute(window);

        private static long SafeNow()
        {
            try { return GameLoop.GameLoopTime; }
            catch { return Environment.TickCount64; }
        }

        private static void DefaultTell(GamePlayer player, string text) =>
            player?.Out?.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        /// <summary>An empty window (BotTrades fills it).</summary>
        public BotTradeWindow(GamePlayer owner, GameBot bot)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Purpose = "trade";
        }

        /// <summary>
        /// WP8's window: these pieces for this price (split over them by their own value), a commission paid up front when the
        /// price is 0. <paramref name="purpose"/> "stock:..." marks a stock sale.
        /// </summary>
        public BotTradeWindow(GamePlayer owner, GameBot bot, IEnumerable<DbInventoryItem> pieces, long price, string purpose)
            : this(owner, bot)
        {
            Purpose = purpose ?? string.Empty;
            DbInventoryItem[] items = (pieces ?? []).Where(p => p != null).Take(MaxItems).ToArray();
            long total = Math.Max(0, price);
            BotTradeTerms.Source source = total == 0 ? BotTradeTerms.Source.Prepaid
                : Purpose.StartsWith("stock:", StringComparison.Ordinal) ? BotTradeTerms.Source.Stock : BotTradeTerms.Source.Delivery;
            long given = 0;
            for (int i = 0; i < items.Length; i++)
            {
                long share = i == items.Length - 1 ? total - given : total / items.Length;
                given += share;
                _botItems.Add(new BotItem(items[i], PieceOf(items[i], share, source)));
            }
            RebuildPartnerItems();
        }

        public static BotTradeTerms.BotPiece PieceOf(DbInventoryItem item, long ask, BotTradeTerms.Source source, string orderId = null) =>
            new(item?.ObjectId ?? string.Empty, item?.Name ?? "it", item?.IsCrafted == true || item?.Quality > 0 ? item.Quality : 0, item?.Level ?? 0,
                Math.Max(0, ask), source, orderId);

        public GameBot Bot => _bot;
        public string BotName => _bot.Name;

        /// <summary>The window's title in the client: "Trading with &lt;bot&gt;" (the client's own words for a player partner).</summary>
        public string Title => "Trading with " + _bot.Name;

        /// <summary>The bot's side: the real items and their terms.</summary>
        public IReadOnlyList<BotItem> BotItems
        {
            get
            {
                lock (_lock)
                    return _botItems.ToArray();
            }
        }

        /// <summary>The bot's pieces (WP8's name).</summary>
        public IReadOnlyList<DbInventoryItem> Pieces => BotItems.Select(b => b.Item).ToArray();

        /// <summary>The player's pieces in the window.</summary>
        public IReadOnlyList<DbInventoryItem> PlayerPieces
        {
            get
            {
                lock (_lock)
                    return _playerItems.OfType<DbInventoryItem>().ToArray();
            }
        }

        public ArrayList TradeItems
        {
            get => _playerItems;
            set
            {
                lock (_lock)
                    _playerItems = value == null ? new ArrayList(MaxItems) : new ArrayList(value.OfType<DbInventoryItem>().Distinct().Take(MaxItems).ToArray());
            }
        }

        public ArrayList PartnerTradeItems => _partnerItems;

        public long TradeMoney
        {
            get => _money;
            set => _money = Math.Max(0, value);
        }

        /// <summary>The coin the bot pays in the terms now (escrow and purse).</summary>
        public long PartnerTradeMoney => _quote?.BotPays ?? 0;

        public GamePlayer Owner => _owner;
        public GamePlayer Partner => null;
        public int ItemsCount => _playerItems.Count;
        public int PartnerItemsCount => _partnerItems.Count;
        public Lock Lock => _lock;
        public bool IsClosed => _closed;

        /// <summary>The player asks the bot for a repair of the one piece the player put in.</summary>
        public bool Repairing
        {
            get => _repair;
            set
            {
                _repair = value;
                if (value)
                    _combine = false;
            }
        }

        /// <summary>The player asks the bot to imbue the gems the player put in into the player's piece.</summary>
        public bool Combine
        {
            get => _combine;
            set
            {
                _combine = value;
                if (value)
                    _repair = false;
            }
        }

        /// <summary>The terms now, worked out afresh.</summary>
        public BotTradeTerms.Quote Evaluate()
        {
            BotTradeTerms.Offer offer;
            try
            {
                offer = Valuer?.Invoke(this) ?? DefaultOffer();
            }
            catch
            {
                offer = DefaultOffer();
            }
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(offer);
            _quote = quote;
            return quote;
        }

        /// <summary>The last terms worked out (null before the first).</summary>
        public BotTradeTerms.Quote Quote => _quote;

        /// <summary>WP8's default: the fixed pieces at their price; anything the player puts in is refused (a bot buys nothing then).</summary>
        public BotTradeTerms.Offer DefaultOffer()
        {
            BotTradeTerms.BotPiece[] mine = BotItems.Select(b => b.Piece).ToArray();
            BotTradeTerms.PlayerPiece[] theirs = PlayerPieces.Select(item => new BotTradeTerms.PlayerPiece(item.ObjectId, item.Name, item.Quality,
                item.Level, 0, BotTradeTerms.Verdict.Refused, "not buying anything right now")).ToArray();
            return new BotTradeTerms.Offer(_bot.Name, mine, theirs, _money, 0);
        }

        /// <summary>What the player owes in the terms now (WP8's Price).</summary>
        public long Price => Evaluate().Owed;

        /// <summary>The bot has accepted: the terms hold (enough coin in, nothing refused).</summary>
        public bool BotAccepts => Evaluate().Accepts;

        #region The bot's side

        private void RebuildPartnerItems()
        {
            _partnerItems = new ArrayList(_botItems.Select(b => b.Item).ToArray());
        }

        /// <summary>Puts one of the bot's pieces on its side (at most ten). False when full or already there.</summary>
        public bool AddBotItem(DbInventoryItem item, BotTradeTerms.BotPiece piece)
        {
            if (item == null || piece == null)
                return false;
            lock (_lock)
            {
                if (_botItems.Count >= MaxItems || _botItems.Any(b => ReferenceEquals(b.Item, item)))
                    return false;
                _botItems.Add(new BotItem(item, piece));
                RebuildPartnerItems();
            }
            return true;
        }

        /// <summary>Takes one of the bot's pieces off its side.</summary>
        public bool RemoveBotItem(DbInventoryItem item)
        {
            lock (_lock)
            {
                int removed = _botItems.RemoveAll(b => ReferenceEquals(b.Item, item));
                if (removed > 0)
                    RebuildPartnerItems();
                return removed > 0;
            }
        }

        public bool HasBotItem(DbInventoryItem item)
        {
            lock (_lock)
                return _botItems.Any(b => ReferenceEquals(b.Item, item));
        }

        #endregion

        #region The player's side

        /// <summary>A piece the player drags in (or drops on the bot): as PlayerTradeWindow, at most ten, tradable only.</summary>
        public bool AddItemToTrade(DbInventoryItem itemForTrade)
        {
            if (itemForTrade == null)
                return false;
            lock (_lock)
            {
                if (!itemForTrade.IsDropable || !itemForTrade.IsPickable || itemForTrade.IsNotLosingDur || !itemForTrade.IsTradable)
                    return false;
                if (_playerItems.Contains(itemForTrade) || _playerItems.Count >= MaxItems)
                    return false;
                _playerItems.Add(itemForTrade);
            }
            TradeUpdate();
            return true;
        }

        public void RemoveItemToTrade(DbInventoryItem itemToRemove)
        {
            if (itemToRemove == null)
                return;
            bool removed;
            lock (_lock)
            {
                removed = _playerItems.Contains(itemToRemove);
                _playerItems.Remove(itemToRemove);
            }
            if (removed && !_done)
                TradeUpdate();
        }

        public void AddMoneyToTrade(long money)
        {
            lock (_lock)
                _money += Math.Max(0, money);
            TradeUpdate();
        }

        #endregion

        /// <summary>
        /// The window changed (the player's side, the coin, a box, or the bot's own side): the bot weighs it again, the
        /// window is sent, and the player reads what changed in the system window.
        /// </summary>
        public void TradeUpdate()
        {
            if (_closed)
                return;
            BotTradeTerms.Quote previous = _quote;
            BotTradeTerms.Quote quote = Evaluate();
            lock (_lock)
                _owner.Out?.SendTradeWindow();
            Announce(previous, quote);
        }

        private void Announce(BotTradeTerms.Quote previous, BotTradeTerms.Quote quote)
        {
            if (quote == null || previous != null && BotTradeTerms.SameTerms(previous, quote))
                return;
            var now = new HashSet<string>(quote.Lines, StringComparer.Ordinal);
            foreach (string line in quote.Lines.Where(line => !_saidLines.Contains(line)))
                Tell?.Invoke(_owner, line);
            _saidLines = now;
            if (quote.Accepts && !_announcedAccept)
                Tell?.Invoke(_owner, $"{_bot.Name} has accepted the trade.");
            _announcedAccept = quote.Accepts;
        }

        /// <summary>The player pressed Accept: with the bot's terms holding, the trade happens whole or not at all.</summary>
        public bool AcceptTrade()
        {
            BotTradeTerms.Quote quote;
            lock (_lock)
            {
                if (_done || _closed)
                    return false;
                quote = Evaluate();
                if (!quote.Accepts)
                {
                    Tell?.Invoke(_owner, $"{_bot.Name} has not accepted: {quote.Blocker ?? "the terms changed"}.");
                    return false;
                }
                _done = true;
            }
            bool moved = false;
            try
            {
                moved = Execute(this);
            }
            catch (Exception)
            {
                moved = false;
            }
            finally
            {
                Finish(moved);
            }
            if (moved)
                Completed?.Invoke(this);
            return moved;
        }

        public void CloseTrade() => Finish(false);

        private void Finish(bool completed)
        {
            lock (_lock)
            {
                if (_closed)
                    return;
                _closed = true;
                _owner.Out?.SendCloseTradeWindow();
            }
            lock (_owner)
                if (ReferenceEquals(_owner.TradeWindow, this))
                    _owner.TradeWindow = null;
            try { Closed?.Invoke(this, completed); }
            catch { } // a listener never keeps a window open
        }

        /// <summary>Opens the window on the player's screen (the player must have no other trade open).</summary>
        public static BotTradeWindow Open(GamePlayer player, GameBot bot, IEnumerable<DbInventoryItem> pieces, long price, string purpose,
            Action<BotTradeWindow> completed)
        {
            // A player already trading gets no window, and the bot takes no trade hold for one never shown (the hold's timeout
            // would otherwise close the player's other window with "has to go and closes the trade").
            if (player == null || bot == null || player.TradeWindow != null)
                return null;
            var window = new BotTradeWindow(player, bot, pieces, price, purpose) { Completed = completed };
            if (window.BotItems.Count == 0)
                return null;
            BotTrades.Decorate(window);
            return Show(player, window) ? window : null;
        }

        /// <summary>Puts a built window on the player's screen. False when the player already trades.</summary>
        public static bool Show(GamePlayer player, BotTradeWindow window)
        {
            if (player == null || window == null)
                return false;
            lock (player)
            {
                if (player.TradeWindow != null)
                    return false;
                player.TradeWindow = window;
            }
            window.TradeUpdate();
            return true;
        }
    }

    /// <summary>A trade window whose partner is a gamebot (the packet names it: "Trading with ...").</summary>
    public interface IBotTradeWindow
    {
        string BotName { get; }
    }
}
