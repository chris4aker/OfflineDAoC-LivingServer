using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, B2): /invites on|off turns unasked invites from gamebot leaders and raid
    /// leaders on or off for this character. /lfg, or a tell to a leader, still gets you one.
    /// </summary>
    [CmdAttribute(
        "&invites",
        ePrivLevel.Player,
        "Turn gamebot leaders' unasked group and raid invites on or off (/lfg still asks for one)",
        "/invites on|off")]
    public class InvitesCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;
            bool? on = args.Length >= 2 ? BotLedGroups.ParseToggle(args[1]) : null;
            if (args.Length >= 2 && on == null)
            {
                DisplaySyntax(client);
                return;
            }
            string text = on is bool value ? BotLedGroups.SetPlayerInvites(client.Player, value) : BotLedGroups.InvitesStatus(client.Player);
            client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
