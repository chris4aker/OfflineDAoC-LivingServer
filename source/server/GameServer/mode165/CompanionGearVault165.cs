using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The original client's companion gear window (True 1.65 Mode, plan 17h): the client's own house-vault item window,
    /// whose slots are one companion's worn gear in a fixed order, then its 10-slot bag. The player drags from his backpack
    /// onto it to equip the companion, and off it to take an item back, with the client's normal drag and drop; the
    /// moves arrive as ordinary item moves and become equip / unequip here (no client code needed; tested in the original
    /// client 2026-10-30). Set as the player's ActiveInventoryObject while open.
    /// </summary>
    public sealed class CompanionGearVault165 : IGameInventoryObject
    {
        /// <summary>The client slot order: weapons, armour, jewellery (the slot meaning shows in the item's tooltip).</summary>
        public static readonly eInventorySlot[] Order =
        [
            eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon,
            eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.ArmsArmor, eInventorySlot.HandsArmor,
            eInventorySlot.LegsArmor, eInventorySlot.FeetArmor, eInventorySlot.Cloak, eInventorySlot.Neck,
            eInventorySlot.Jewelry, eInventorySlot.Waist, eInventorySlot.LeftBracer, eInventorySlot.RightBracer,
            eInventorySlot.LeftRing, eInventorySlot.RightRing,
        ];

        private readonly int _row;
        private readonly GameBot _bot;

        public CompanionGearVault165(int row, GameBot bot)
        {
            _row = row;
            _bot = bot;
        }

        public static void Open(GamePlayer player, int row, GameBot bot)
        {
            player.ActiveInventoryObject?.RemoveObserver(player);
            var window = new CompanionGearVault165(row, bot);
            player.ActiveInventoryObject = window;
            player.Out.SendInventoryItemsUpdate(window.GetClientInventory(player), eInventoryWindowType.HouseVault);
        }

        public eInventorySlot FirstClientSlot => eInventorySlot.HousingInventory_First;
        public eInventorySlot LastClientSlot => eInventorySlot.HousingInventory_First + Order.Length + (HiredCompanions165.BagSlots - 1);
        public int FirstDbSlot => (int)eInventorySlot.MinEquipable;
        public int LastDbSlot => (int)eInventorySlot.LastBackpack;
        public Lock Lock { get; } = new();

        public string GetOwner(GamePlayer player) => AutonomousBotEconomy.GetOwnerId(_bot.DatabaseID);

        public IEnumerable<DbInventoryItem> GetDbItems(GamePlayer player) => _bot.Inventory.AllItems;

        /// <summary>Client slot of a companion slot, or -1.</summary>
        private static int ClientSlot(int companionSlot)
        {
            int i = System.Array.IndexOf(Order, (eInventorySlot)companionSlot);
            if (i >= 0)
                return (int)eInventorySlot.HousingInventory_First + i;
            if (HiredCompanions165.IsBagSlot(companionSlot))
                return (int)eInventorySlot.HousingInventory_First + Order.Length + companionSlot - (int)HiredCompanions165.FirstBagSlot;
            return -1;
        }

        private static int CompanionSlot(eInventorySlot clientSlot)
        {
            int i = clientSlot - eInventorySlot.HousingInventory_First;
            if (i < 0)
                return -1;
            if (i < Order.Length)
                return (int)Order[i];
            int bag = (int)HiredCompanions165.FirstBagSlot + i - Order.Length;
            return HiredCompanions165.IsBagSlot(bag) ? bag : -1;
        }

        public Dictionary<int, DbInventoryItem> GetClientInventory(GamePlayer player)
        {
            var result = new Dictionary<int, DbInventoryItem>();
            foreach (DbInventoryItem item in _bot.Inventory.AllItems)
            {
                int client = item == null ? -1 : ClientSlot(item.SlotPosition);
                if (client >= 0)
                    result[client] = item;
            }
            return result;
        }

        private bool Ours(eInventorySlot slot) => slot >= FirstClientSlot && slot <= LastClientSlot;

        public bool CanHandleMove(GamePlayer player, eInventorySlot fromClientSlot, eInventorySlot toClientSlot) =>
            Ours(fromClientSlot) || Ours(toClientSlot);

        public bool MoveItem(GamePlayer player, eInventorySlot fromClientSlot, eInventorySlot toClientSlot, ushort itemCount)
        {
            if (!CanHandleMove(player, fromClientSlot, toClientSlot))
                return false;     // the player's own moves go on as normal
            if (_bot.ObjectState != GameObject.eObjectState.Active || HiredCompanions165.LiveBot(_bot.DatabaseID) != _bot)
            {
                player.Out.SendMessage($"{_bot.Name} is no longer with you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }
            string problem = null;
            bool fromBag = fromClientSlot is >= eInventorySlot.FirstBackpack and <= eInventorySlot.LastBackpack;
            bool toBag = toClientSlot is >= eInventorySlot.FirstBackpack and <= eInventorySlot.LastBackpack;
            if (fromBag && Ours(toClientSlot))
                problem = HiredCompanions165.IsBagSlot(CompanionSlot(toClientSlot))
                    ? CompanionWindows165.PutInBag(player, _bot, (int)fromClientSlot, CompanionSlot(toClientSlot))   // onto its bag
                    : CompanionWindows165.Equip(player, _bot, (int)fromClientSlot, CompanionSlot(toClientSlot));      // onto its gear
            else if (Ours(fromClientSlot) && toBag)
                problem = CompanionWindows165.Unequip(player, _bot, CompanionSlot(fromClientSlot), (int)toClientSlot);
            // moves inside the window: the companion wears each piece where it belongs, nothing to do
            if (problem != null)
                player.Out.SendMessage(problem, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            player.Out.SendInventoryItemsUpdate(GetClientInventory(player), eInventoryWindowType.HouseVault);
            CompanionWindows165.SendGear(player, _row, _bot);
            return true;
        }

        public bool OnAddItem(GamePlayer player, DbInventoryItem item, int previousSlot) => false;
        public bool OnRemoveItem(GamePlayer player, DbInventoryItem item, int previousSlot) => false;
        public bool OnMoveItem(GamePlayer player, DbInventoryItem firstItem, int previousFirstSlot, DbInventoryItem secondItem, int previousSecondSlot) => false;
        public void OnItemManipulationError(GamePlayer player) => player.Out.SendInventoryItemsUpdate(GetClientInventory(player), eInventoryWindowType.HouseVault);
        public bool SetSellPrice(GamePlayer player, eInventorySlot clientSlot, uint sellPrice) => false;
        public bool SearchInventory(GamePlayer player, MarketSearch.SearchData searchData) => false;
        public void AddObserver(GamePlayer player) { }
        public void RemoveObserver(GamePlayer player) { }
    }
}
