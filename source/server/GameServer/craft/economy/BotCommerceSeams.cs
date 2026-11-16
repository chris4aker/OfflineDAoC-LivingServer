using System;
using DOL.Database;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Drag-and-drop onto a gamebot (GameBot.ReceiveItem / ReceiveMoney), after the beggars: WP8's crafter commerce
    /// (a spellcraft or repair job, a sale to a bot that asked for it) takes the item or coin here. The defaults take
    /// nothing, so the drop falls through to GameNPC's own handling as today. Only real players' drops reach it.
    /// </summary>
    public static class BotCommerceSeams
    {
        public static Func<GameBot, GamePlayer, DbInventoryItem, bool> TryReceiveItem = (_, _, _) => false;
        public static Func<GameBot, GamePlayer, long, bool> TryReceiveMoney = (_, _, _) => false;

        public static bool ReceiveItem(GameBot bot, GamePlayer player, DbInventoryItem item)
        {
            if (player == null || item == null)
                return false;
            try { return TryReceiveItem?.Invoke(bot, player, item) == true; }
            catch { return false; }
        }

        public static bool ReceiveMoney(GameBot bot, GamePlayer player, long copper)
        {
            if (player == null || copper <= 0)
                return false;
            try { return TryReceiveMoney?.Invoke(bot, player, copper) == true; }
            catch { return false; }
        }

        public static void ResetForTests()
        {
            TryReceiveItem = (_, _, _) => false;
            TryReceiveMoney = (_, _, _) => false;
        }
    }
}
