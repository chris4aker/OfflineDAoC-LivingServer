using System.Linq;

namespace DOL.GS.Commands
{
    /// <summary>
    /// #667 step 1: <c>/order &lt;word&gt;</c>, the player's fight order to his group (PlayerGroupOrders). One word per order,
    /// so a client macro holds it (<c>/macro Atk /order attack</c>); <c>/order</c> alone lists them.
    /// </summary>
    [CmdAttribute("&order", ePrivLevel.Player, "Order your group in a fight: attack, assist, hold or regroup", "/order <attack|assist|hold|regroup>")]
    public class GroupOrderCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null) return;
            if (args.Length < 2)
            {
                DisplayMessage(client, "Order your group (your helpers, companions and the gamebots in a group you lead). " +
                    "Every order, hold too, lasts until your next order or the end of the fight. You can also say the word alone in /group.");
                foreach ((string word, string does) in PlayerGroupOrders.Help)
                    DisplayMessage(client, $"  /order {word} - {does}");
                return;
            }
            if (IsSpammingCommand(player, "order", 250))
                return;
            GroupOrder order = PlayerGroupOrders.Parse(string.Join(" ", args.Skip(1)));
            if (order == GroupOrder.None)
            {
                DisplayMessage(client, "Unknown order. Use /order attack, /order assist, /order hold or /order regroup.");
                return;
            }
            string reply = PlayerGroupOrders.Give(player, order, "command", out _);
            if (!string.IsNullOrEmpty(reply))
                DisplayMessage(client, reply);
        }
    }
}
