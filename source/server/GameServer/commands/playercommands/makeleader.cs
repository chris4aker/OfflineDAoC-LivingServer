using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute("&makeleader",
         new string[] { "&m" },
         ePrivLevel.Player,
         "Set a new group leader (can be used by current leader).",
         "/m <playerName>")]

    public class MakeLeaderCommandHandler : ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client.Player.Group == null || client.Player.Group.MemberCount < 2)
            {
                client.Out.SendMessage("You are not part of a group.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                return;
            }

            // Bot-led groups (docs/BOT-LED-GROUPS.md, B2): in a group a gamebot leads, /lead takes over.
            if (BotLedGroups.BotLeadsMessageFor(client.Player) is { } botLeads)
            {
                client.Out.SendMessage(botLeads, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if(client.Player.Group.Leader != client.Player)
            {
                client.Out.SendMessage("You are not the leader of your group.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                return;
            }

            GamePlayer target;

            if (args.Length < 2) // Setting by target
            {
                if (client.Player.TargetObject == null || client.Player.TargetObject == client.Player)
                {
                    client.Out.SendMessage("You have not selected a valid player as your target.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                    return;
                }

                // A gamebot in your group: it takes the lead and the bots run the group (as /lead bot).
                if (client.Player.TargetObject is GameBot targetedBot && targetedBot.Group == client.Player.Group)
                {
                    client.Out.SendMessage(BotLedGroups.HandBack(client.Player, targetedBot), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }

                if(client.Player.TargetObject is not GamePlayer)
                {
                    client.Out.SendMessage("You have not selected a valid player as your target.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                    return;
                }

                target = (GamePlayer) client.Player.TargetObject;

                if(client.Player.Group != target.Group)
                {
                    client.Out.SendMessage("You have not selected a valid player as your target.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                    return;
                }
            }
            else //Setting by name
            {
                string targetName = args[1];
                if (client.Player.Group.GetMembersInTheGroup().OfType<GameBot>()
                        .FirstOrDefault(bot => string.Equals(bot.Name, targetName, System.StringComparison.OrdinalIgnoreCase)) is { } namedBot)
                {
                    client.Out.SendMessage(BotLedGroups.HandBack(client.Player, namedBot), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                target = ClientService.Instance.GetPlayerByPartialName(targetName, out _);

                if(target==null || client.Player.Group != target.Group)
                { // Invalid target
                    client.Out.SendMessage("No players in group with that name.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                    return;
                }

                if(target==client.Player)
                {
                    client.Out.SendMessage("You are the group leader already.",eChatType.CT_System,eChatLoc.CL_SystemWindow);
                    return;
                }

            }

            client.Player.Group.MakeLeader(target);
        }
    }
}
