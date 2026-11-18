using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute("&raid", ePrivLevel.Player, "Open a level-50 helper raid (40 or 80 total)", "/raid 40", "/raid 80")]
    public sealed class CompanionRaidCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length != 2 || !int.TryParse(args[1], out int capacity) || capacity is not (40 or 80))
            {
                DisplayMessage(client, "Use /raid 40 or /raid 80. The total includes your character.");
                return;
            }
            if (client.Player.Level != 50)
            {
                DisplayMessage(client, "Helper raids are available at level 50.");
                return;
            }
            DisplayMessage(client, CompanionRaid.Open(client.Player, capacity)
                ? $"Raid active: {capacity} total including you. Use /spawn to add helpers, /defensive or /aggressive for engagement, and /disband to close the raid."
                : "You must lead a group containing only your own /spawn helpers. Remove excess members before switching to /raid 40.");
        }
    }
}
