using System.Linq;
namespace DOL.GS.Commands
{
    /// <summary>
    /// The hiring window's buttons (True 1.65 Mode, Hire165): every button in both clients sends one of these.
    /// /companionui test (GM) opens the window with sample data and a private preview figure.
    /// </summary>
    [CmdAttribute("&companionui", ePrivLevel.Player, "True 1.65 Mode companion hiring window",
        "/companionui race-|race+|class-|class+|gender|face-|face+|hair-|hair+|look|name [name]|hire|tab <n>|close",
        "/companionui summon|dismiss|release|revive <row>")]
    public sealed class CompanionUiCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private static readonly DOL.Logging.Logger log =
            DOL.Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;
            // One line per button press, so a client developer can see which clicks reach the server.
            if (log.IsInfoEnabled)
                log.Info($"COMPANIONUI_CMD player={player.Name} args=\"{string.Join(' ', args.Skip(1))}\"");
            if (!HiredCompanionsMode.IsOn)
            {
                DisplayMessage(client, "Hired companions are switched off on this server (hired_companions).");
                return;
            }
            if (args.Length > 1 && args[1].Equals("test", System.StringComparison.OrdinalIgnoreCase))
            {
                if (client.Account.PrivLevel < (uint)ePrivLevel.GM)
                {
                    DisplayMessage(client, "That is a GM test command.");
                    return;
                }
                Hire165.OpenTest(player);
                return;
            }
            if (args.Length > 1 && CompanionWindows165.Verbs.Contains(args[1].ToLowerInvariant()))
                CompanionWindows165.Handle(player, args);   // gear, stats, repair, select, spec: any time, the companion with you
            else
                Hire165.Handle(player, args);
        }
    }
}
