using DOL.GS.PacketHandler;

// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0. Ported for a later world update: no Innkeeper165 row is
// placed in Living Server yet (the inns come with a base-game world update), and its "The Traveler's Inn" quest marker
// (InnQuestMarkers165) is not ported.

namespace DOL.GS
{
    /// <summary>
    /// The innkeeper of a traveler's inn (True 1.65, owner 2026-10-30): talking to them opens the companion hiring window
    /// (Hire165), where companions are hired, summoned, dismissed and released. Living Server places none yet; until a
    /// world update adds the inns, /hire beside a healer opens the same window.
    /// </summary>
    public class Innkeeper165 : GameNPC
    {
        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 5000);
            if (!HiredCompanionsMode.IsOn)
                return true;
            player.Out.SendMessage($"{GetName(0, true)} says, \"Welcome, {player.Name}. Looking for company on the road? " +
                "Pick who you'd like, and I'll send them along with you.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            Hire165.OpenInnkeeper(player, this);
            return true;
        }
    }
}
