using System.Numerics;

namespace DOL.GS.Commands
{
    [CmdAttribute("&stuck",
        ePrivLevel.Player,
        "Move the player to the last recorded safe position",
        "/stuck")]
    public class StuckCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (IsSpammingCommand(client.Player, "stuck"))
                return;

            GamePlayer player = client.Player;
            PlayerMovementComponent movementComponent = player.movementComponent;

            // Early exit if the currently set safe position cannot be used.
            if (!player.movementComponent.TryGetSafePosition(out Vector3 _))
            {
                DisplayMessage(client, "No safe position could be found. Please use your bind stone instead.");
                return;
            }

            // Port of OpenDAoC-Core 74587b664: flip UseSafePosition first in case the player may quit at once, say what will
            // happen, and clear it again when the quit is refused (it is also reset if the quit timer is interrupted).
            // Before, a refused /stuck left it set, so a later ordinary quit still moved the player.
            movementComponent.UseSafePosition = true;
            DisplayMessage(client, "Your position will be adjusted to your last known safe point when you exit the game.");

            if (!player.Quit(false))
            {
                movementComponent.UseSafePosition = false;
                return;
            }
        }
    }
}
