using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Trading with gamebots (Crafting 2.0 WP8, the full bot trade; bot_crafters and bot_crafter_commerce). With a gamebot
    /// of your realm targeted, /tradewindow opens a trade window with it (as dropping an item or coin on it does) and lists
    /// what it sells; the rest works the window from the chat line, which is how the new client's trade window acts (its
    /// network client cannot send the original client's trade packet). See docs/CRAFTING.md, "Trading with gamebots".
    /// </summary>
    [CmdAttribute(
        "&tradewindow",
        ePrivLevel.Player,
        "Trade with a gamebot of your realm (target it first)",
        "/tradewindow - open a trade with your target",
        "/tradewindow list - what it sells",
        "/tradewindow buy <number> - put one of its goods on its side",
        "/tradewindow put|take <backpack slot>, coin <copper>, repair on|off, combine on|off, accept, close")]
    public class TradeWindowCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;
            foreach (string line in BotTrades.Command(client.Player, args))
                client.Out.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
