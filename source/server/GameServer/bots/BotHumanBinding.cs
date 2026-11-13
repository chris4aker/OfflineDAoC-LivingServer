using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Play with friends (1.2, co-op audit 2026-11-03, rule 4): which human a bot in a player's group answers to.
    /// <list type="bullet">
    /// <item>A <b>companion</b> (a bot with an Owner: /spawn, /raid, a saved /bot companion) follows its Owner, always.
    /// No other human's command moves it.</item>
    /// <item>An <b>autonomous gamebot</b> invited into a player's group (no Owner) follows the group's leader when the
    /// leader is a human (<see cref="Group.Leader"/>). A gamebot that leads (bot-led groups) follows nobody.</item>
    /// </list>
    /// With one human in the group every bot in it is that human's, so single-player decisions are what they were.
    /// </summary>
    public static class BotHumanBinding
    {
        /// <summary>A player's companion: a bot with an owner. Autonomous gamebots have none.</summary>
        public static bool IsCompanion(GameBot bot) => bot?.Owner != null;

        /// <summary>The human this bot should follow now: its owner, or for a gamebot the group's human leader (null when none).</summary>
        public static GamePlayer RightfulLeader(GameBot bot) => bot == null ? null : bot.Owner ?? bot.Group?.Leader;

        /// <summary>
        /// Whether <paramref name="player"/>'s order (/pull, the attack companions join, the /bot pilot's party) may move
        /// <paramref name="bot"/> in <paramref name="group"/>: always the player's own companion, never another human's; a
        /// gamebot when the player leads the group, or when the player is the only human in it (as before co-op).
        /// </summary>
        public static bool Commands(GamePlayer player, GameBot bot, Group group)
        {
            if (player == null || bot == null)
                return false;
            if (bot.Owner != null)
                return bot.Owner == player;
            if (group == null)
                return false;
            if (!HasOtherHuman(group, player))
                return true;
            GamePlayer leader = group.Leader;
            return leader == player || leader == null && bot.PlayerGroupLeader == player;
        }

        /// <summary>The pure form of <see cref="Commands"/>, for tests and logs.</summary>
        public static bool CommandsRule(bool botHasOwner, bool ownedByPlayer, bool otherHumanInGroup, bool playerLeads, bool groupHasHumanLeader,
            bool followsPlayer) =>
            botHasOwner ? ownedByPlayer
            : !otherHumanInGroup || playerLeads || !groupHasHumanLeader && followsPlayer;

        /// <summary>Another real player is in the group besides <paramref name="player"/>.</summary>
        public static bool HasOtherHuman(Group group, GamePlayer player) =>
            group != null && group.GetPlayersInTheGroup().Any(member => member != player);
    }
}
