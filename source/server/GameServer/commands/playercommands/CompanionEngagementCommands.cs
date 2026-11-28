using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS.Commands
{
    [CmdAttribute("&defensive", ePrivLevel.Player, "Your helpers and companions wait for enemies to approach you", "/defensive")]
    public class CompanionDefensiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, true);
            if (client.Player.Group != null)
                foreach (GameBot bot in client.Player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (CompanionEngagementMode.DefensiveLeader(bot) == client.Player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            DisplayMessage(client, "Helpers and companions: DEFENSIVE. Pull enemies within 350 units of you; they and their pets will engage nearby threats. Use /aggressive to restore normal assisting.");
        }
    }
    [CmdAttribute("&hold", ePrivLevel.Player, "Your helpers, companions and the bots in your group only defend themselves", "/hold")]
    public class CompanionHoldCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            // #664 step 2 (owner answer 3): for sneaking past a fight or waiting at a keep.
            CompanionEngagementMode.SetHold(client.Player);
            if (client.Player.Group != null)
                foreach (GameBot bot in client.Player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (CompanionRvrEngage.GroupLeader(bot) == client.Player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            DisplayMessage(client, "Your group: HOLD. Your helpers, companions and the bots in your group won't start a fight, assist your attacks or answer a pull. Each one only fights back what attacks it, and healers keep healing. Use /aggressive or /defensive to end it.");
        }
    }
    [CmdAttribute("&aggressive", ePrivLevel.Player, "Your helpers and companions assist your attacks again (default)", "/aggressive")]
    public class CompanionAggressiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, false);
            DisplayMessage(client, "Helpers and companions: AGGRESSIVE (default). They assist your attacks normally again.");
        }
    }
}
