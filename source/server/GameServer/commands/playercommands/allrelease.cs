namespace DOL.GS.Commands
{
    [Cmd(
        "&allrelease", ["&grouprelease"],
        ePrivLevel.Player,
        "Every dead bot in your group releases at once and makes its way back to you.",
        "/allrelease")]
    public class AllReleaseCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (player == null || IsSpammingCommand(player, "allrelease"))
                return;

            if (player.Group == null)
            {
                ChatUtil.SendSystemMessage(player, "You are not in a group.");
                return;
            }

            var (released, waiting) = BotGroupRelease.ReleaseDeadBots(player.Group);
            foreach (string line in BotGroupRelease.Report(released, waiting))
                ChatUtil.SendSystemMessage(player, line);
        }
    }
}
