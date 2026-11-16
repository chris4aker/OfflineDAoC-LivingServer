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
    /// A trade window between a real player and an autonomous crafter gamebot (Crafting 2.0 WP8). The client's own trade
    /// window: the bot's side shows the real pieces it hands over (from its bag) and the price it asks; the player puts the
    /// coin in and accepts. The bot accepts by itself once the coin covers the price, and nothing moves until the player
    /// accepts too: then the coin goes from the player's purse to the bot's (EconomyFlowKind.PlayerTrade), the pieces from
    /// the bot's bag to the player's, and <see cref="Completed"/> closes the sale (a stock sale or a pay-on-delivery order).
    /// Items the player puts in are not taken (a bot buys nothing here). Only the player has a window: the bot is no
    /// GamePlayer, so <see cref="Partner"/> is null and the packet names the bot through <see cref="IBotTradeWindow"/>.
    /// </summary>
    public sealed class BotTradeWindow : ITradeWindow, IBotTradeWindow
    {
        private readonly GamePlayer _owner;
        private readonly GameBot _bot;
        private readonly ArrayList _botItems;
        private ArrayList _playerItems = new(10);
        private long _money;
        private readonly Lock _lock = new();
        private bool _done;

        /// <summary>What the trade is for (the order ids handed over, or the stock piece sold), for the completion.</summary>
        public string Purpose { get; }
        public long Price { get; }
        public IReadOnlyList<DbInventoryItem> Pieces { get; }

        /// <summary>Called once, after a completed trade (coin and pieces moved), on the game loop.</summary>
        public Action<BotTradeWindow> Completed { get; init; }

        /// <summary>Moves the coin and the pieces; replaceable by tests. True when everything moved (or nothing did).</summary>
        public static Func<BotTradeWindow, bool> Execute = DefaultExecute;

        public static void ResetForTests() => Execute = DefaultExecute;

        public BotTradeWindow(GamePlayer owner, GameBot bot, IEnumerable<DbInventoryItem> pieces, long price, string purpose)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Pieces = (pieces ?? []).Where(p => p != null).Take(10).ToArray();
            _botItems = new ArrayList(Pieces.ToArray());
            Price = Math.Max(0, price);
            Purpose = purpose ?? string.Empty;
        }

        public GameBot Bot => _bot;
        public string BotName => _bot.Name;

        public ArrayList TradeItems
        {
            get => _playerItems;
            set => _playerItems = value ?? new ArrayList(10);
        }

        public ArrayList PartnerTradeItems => _botItems;
        public long TradeMoney
        {
            get => _money;
            set => _money = Math.Max(0, value);
        }
        public long PartnerTradeMoney => 0;
        public GamePlayer Owner => _owner;
        public GamePlayer Partner => null;
        public int ItemsCount => _playerItems.Count;
        public int PartnerItemsCount => _botItems.Count;
        public Lock Lock => _lock;

        public bool Repairing
        {
            get => false;
            set { }
        }

        public bool Combine
        {
            get => false;
            set { }
        }

        /// <summary>The bot has accepted: the coin in the window covers its price.</summary>
        public bool BotAccepts => _money >= Price;

        public bool AddItemToTrade(DbInventoryItem itemForTrade) => false;

        public void RemoveItemToTrade(DbInventoryItem itemToRemove)
        {
            lock (_lock)
                _playerItems.Remove(itemToRemove);
        }

        public void AddMoneyToTrade(long money)
        {
            lock (_lock)
                _money += Math.Max(0, money);
            TradeUpdate();
        }

        public void TradeUpdate()
        {
            lock (_lock)
                _owner.Out?.SendTradeWindow();
            if (BotAccepts && _owner.Out != null)
                _owner.Out.SendMessage($"{_bot.Name} has accepted the trade.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        public bool AcceptTrade()
        {
            lock (_lock)
            {
                if (_done)
                    return false;
                if (!BotAccepts)
                {
                    _owner.Out?.SendMessage($"{_bot.Name} wants {Money.GetString(Price)} for that.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                    return false;
                }
                _done = true;
            }
            bool moved = false;
            try
            {
                moved = Execute(this);
            }
            finally
            {
                CloseTrade();
            }
            if (moved)
                Completed?.Invoke(this);
            return moved;
        }

        public void CloseTrade()
        {
            lock (_lock)
                _owner.Out?.SendCloseTradeWindow();
            lock (_owner)
                if (ReferenceEquals(_owner.TradeWindow, this))
                    _owner.TradeWindow = null;
        }

        /// <summary>Opens the window on the player's screen (the player must have no other trade open).</summary>
        public static BotTradeWindow Open(GamePlayer player, GameBot bot, IEnumerable<DbInventoryItem> pieces, long price, string purpose,
            Action<BotTradeWindow> completed)
        {
            if (player == null || bot == null)
                return null;
            lock (player)
            {
                if (player.TradeWindow != null)
                    return null;
                var window = new BotTradeWindow(player, bot, pieces, price, purpose) { Completed = completed };
                if (window.Pieces.Count == 0)
                    return null;
                player.TradeWindow = window;
                window.TradeUpdate();
                return window;
            }
        }

        /// <summary>
        /// The trade itself, on the game loop: the player's coin first (the whole price, never more), then each piece from
        /// the bot's bag to the player's (saved with its new owner), then the bot's purse. A piece that cannot move puts the
        /// moved ones back and returns the coin, so a trade happens whole or not at all.
        /// </summary>
        private static bool DefaultExecute(BotTradeWindow window)
        {
            GamePlayer player = window._owner;
            GameBot bot = window._bot;
            if (player.Inventory == null || bot.Inventory == null || !player.Inventory.IsSlotsFree(window.Pieces.Count,
                    eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
            {
                player.Out?.SendMessage("You don't have enough space in your inventory.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (window.Pieces.Any(piece => !bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Contains(piece)))
            {
                player.Out?.SendMessage($"{bot.Name} no longer has that.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (window.Price > 0 && !player.RemoveMoney(window.Price))
            {
                player.Out?.SendMessage("You don't have enough money.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                return false;
            }
            var moved = new List<(DbInventoryItem Piece, int Slot)>();
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
            {
                foreach (DbInventoryItem piece in window.Pieces)
                {
                    int from = piece.SlotPosition;
                    eInventorySlot to = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (to != eInventorySlot.Invalid && bot.Inventory.RemoveItemWithoutDbDeletion(piece))
                    {
                        if (player.Inventory.AddItemWithoutDbAddition(to, piece))
                        {
                            moved.Add((piece, from));
                            continue;
                        }
                        bot.Inventory.AddItem((eInventorySlot)from, piece);
                    }
                    foreach ((DbInventoryItem back, int slot) in moved)
                    {
                        player.Inventory.RemoveItemWithoutDbDeletion(back);
                        bot.Inventory.AddItem((eInventorySlot)slot, back);
                    }
                    if (window.Price > 0)
                        player.AddMoney(window.Price);
                    player.Out?.SendMessage("The trade could not be completed; nothing changed hands.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                    return false;
                }
                foreach ((DbInventoryItem piece, _) in moved)
                    GameServer.Database.SaveObject(piece);
            }
            if (window.Price > 0)
                AutonomousBotEconomy.AddMoney(bot.DatabaseID, window.Price, EconomyFlowKind.PlayerTrade);
            InventoryLogging.LogInventoryAction(player, bot, eInventoryActionType.Trade, window.Price);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            string names = string.Join(", ", window.Pieces.Select(p => p.Name));
            player.Out?.SendMessage(window.Price > 0 ? $"You bought {names} from {bot.Name} for {Money.GetString(window.Price)}."
                : $"{bot.Name} hands you {names}.", eChatType.CT_Merchant, eChatLoc.CL_ChatWindow);
            return true;
        }
    }

    /// <summary>A trade window whose partner is a gamebot (the packet names it: "Trading with ...").</summary>
    public interface IBotTradeWindow
    {
        string BotName { get; }
    }
}
