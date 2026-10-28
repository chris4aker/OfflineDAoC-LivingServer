using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// How a bot's death looks to the players around it (owner 2026-10-28: "when my group mate dies they didn't have an
    /// animation or anything"). A monster's death animation comes with its removal (SendObjectRemove with a dead NPC), but
    /// a bot keeps its corpse to be raised, so nothing told the client it died: it stood at 0 health. Now it falls as a
    /// player does (the player-death packet, sent for the bot), a player who comes into range sees it lying there, and when
    /// it is raised or released it is created again for everyone who sees it, standing.
    /// </summary>
    public static class BotCorpseVisuals
    {
        /// <summary>The bot fell: everyone who sees it watches it die.</summary>
        public static void OnDied(GameBot bot, GameObject killer)
        {
            if (bot == null || bot.ObjectState != GameObject.eObjectState.Active)
                return;
            foreach (GamePlayer viewer in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                if (Sees(viewer, bot))
                    viewer.Out.SendPlayerDied(bot, killer);
        }

        /// <summary>The client was just sent the bot: a dead bot lies on the ground for it.</summary>
        public static void OnCreatedFor(GamePlayer viewer, GameNPC npc)
        {
            if (npc is GameBot bot && ShowsCorpseOnCreate(bot.IsAlive, bot.ObjectState == GameObject.eObjectState.Active,
                    bot.IsMovingCorpseForRevival))
                viewer.Out.SendPlayerDied(bot, null);
        }

        /// <summary>
        /// The bot is alive again (raised, or released and revived at its bind): it is created again for everyone who
        /// sees it, so their clients stand it up rather than leave a walking corpse.
        /// </summary>
        public static void OnRevived(GameBot bot)
        {
            if (bot == null || bot.ObjectState != GameObject.eObjectState.Active)
                return;
            foreach (GamePlayer viewer in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (!Sees(viewer, bot))
                    continue;
                viewer.Out.SendObjectRemove(bot);
                ClientService.CreateObjectForPlayer(viewer, bot);
            }
        }

        /// <summary>Whether a newly created bot should be shown lying dead. Pure.</summary>
        public static bool ShowsCorpseOnCreate(bool alive, bool active, bool movingCorpseForRevival) =>
            !alive && active && !movingCorpseForRevival;

        /// <summary>Whether a bot that was at <paramref name="oldHealth"/> and is now at <paramref name="newHealth"/> came back to life. Pure.</summary>
        public static bool CameBackToLife(int oldHealth, int newHealth) => oldHealth <= 0 && newHealth > 0;

        private static bool Sees(GamePlayer viewer, GameBot bot) =>
            viewer?.Client?.ClientState == GameClient.eClientState.Playing && viewer.CanDetect(bot);
    }
}
