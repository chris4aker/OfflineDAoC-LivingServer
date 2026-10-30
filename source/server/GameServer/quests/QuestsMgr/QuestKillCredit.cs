namespace DOL.GS.Quests
{
    /// <summary>
    /// Whose quests a kill counts for. Owner 2026-10-30: a player who healed, nuked late or only tagged while the
    /// autonomous gamebots in the group (/invite) made the kill got no quest credit and no classic step drop. Such a
    /// gamebot has no Owner, only a PlayerGroupLeader, and GameLiving.ProcessDeath credited pets through their owner
    /// only. The leader is credited like a companion's owner, and then the leader's group in range as for any player
    /// attacker. XP and loot keep their own rules: nothing here changes who the killer is.
    /// </summary>
    public static class QuestKillCredit
    {
        /// <summary>The group's kill-credit range, the one GameLiving.ProcessDeath uses for groupmates.</summary>
        public const int Range = WorldMgr.MAX_EXPFORKILL_DISTANCE;

        /// <summary>
        /// The pure rule: the leader of a player-led group is credited for an ownerless gamebot's kill when the bot
        /// still fights in the leader's group and the leader stands in the kill's region within <see cref="Range"/>.
        /// A bot with an owner (a companion) is credited through that owner as before.
        /// </summary>
        public static bool LeaderIsCredited(bool botHasOwner, bool leaderInBotsGroup, bool leaderInKillsRegion, int leaderDistance) =>
            !botHasOwner && leaderInBotsGroup && leaderInKillsRegion && leaderDistance <= Range;

        /// <summary>The real player an ownerless gamebot's kill counts for, or null (a bot on its own, or a leader too far away).</summary>
        public static GamePlayer GroupLeaderFor(GameBot bot, GameObject killed)
        {
            if (bot == null || killed == null)
                return null;

            GamePlayer leader = bot.PlayerGroupLeader;
            if (leader == null || !LeaderIsCredited(bot.Owner != null, bot.Group != null && bot.Group == leader.Group,
                leader.CurrentRegionID == killed.CurrentRegionID, leader.GetDistanceTo(killed)))
                return null;

            return leader;
        }
    }
}
