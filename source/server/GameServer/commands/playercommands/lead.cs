using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, B2): /lead takes the lead of the gamebot party you are in (the bots follow
    /// you, as bots you /invite do); /lead bot [name] hands the lead to a gamebot (the bots run the group and you follow).
    /// </summary>
    [CmdAttribute(
        "&lead",
        ePrivLevel.Player,
        "Take the lead of a group a gamebot leads, or hand your group's lead to a gamebot",
        "/lead (take the lead)",
        "/lead bot [name] (a gamebot leads; the highest level one, or the one named or targeted)")]
    public class LeadCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null || IsSpammingCommand(client.Player, "lead"))
                return;
            if (args.Length >= 2 && args[1].Equals("bot", System.StringComparison.OrdinalIgnoreCase))
            {
                string name = args.Length >= 3 ? string.Join(" ", args, 2, args.Length - 2) : null;
                GameBot targeted = name == null && client.Player.TargetObject is GameBot bot && bot.Group == client.Player.Group ? bot : null;
                client.Out.SendMessage(BotLedGroups.HandBack(client.Player, targeted, name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            if (args.Length >= 2)
            {
                DisplaySyntax(client);
                return;
            }
            client.Out.SendMessage(BotLedGroups.TakeLead(client.Player), eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
