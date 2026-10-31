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
        /// How long after a counted kill a notification about the same monster is still that kill. The repeats are
        /// synchronous, inside the one Die(); no monster object dies again within two seconds, respawn included.
        /// </summary>
        public const int RepeatWindowMs = 2000;

        /// <summary>
        /// The pure rule: the leader of a player-led group is credited for an ownerless gamebot's kill when the bot
        /// still fights in the leader's group and the leader stands in the kill's region within <see cref="Range"/>.
        /// A bot with an owner (a companion) is credited through that owner as before.
        /// </summary>
        public static bool LeaderIsCredited(bool botHasOwner, bool leaderInBotsGroup, bool leaderInKillsRegion, int leaderDistance) =>
            !botHasOwner && leaderInBotsGroup && leaderInKillsRegion && leaderDistance <= Range;

        /// <summary>
        /// The real player an ownerless gamebot's kill counts for, or null (a bot on its own, a leader too far away,
        /// or a leader who is dead or no longer in the world). Owner 2026-10-31: the leader must be alive and active,
        /// as every other reader of PlayerGroupLeader requires (CompanionFollowPolicy, TemporaryGroupStableTravel,
        /// AutonomousStuckWatchdog); a dead leader lying in range while the bots fight on is not credited.
        /// </summary>
        public static GamePlayer GroupLeaderFor(GameBot bot, GameObject killed)
        {
            if (bot == null || killed == null)
                return null;

            GamePlayer leader = bot.PlayerGroupLeader;
            if (leader is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active })
                return null;

            if (!LeaderIsCredited(bot.Owner != null, bot.Group != null && bot.Group == leader.Group,
                leader.CurrentRegionID == killed.CurrentRegionID, leader.GetDistanceTo(killed)))
                return null;

            return leader;
        }

        /// <summary>
        /// A kill counts for a quest once. GamePlayer.EnemyKilled tells the killer's quests about the kill once more
        /// for each groupmate in range who didn't attack (with that groupmate as the sender), so a data quest with
        /// consecutive kill steps advanced several steps on one kill. Owner 2026-10-31: the kill is told apart by the
        /// monster, not by the sender. The first fix counted only the notification sent by the quest's own player,
        /// which also silenced the nine named mobs (the three dragons, Legion, Nosdoden, Olcasgean, Myrddraxis,
        /// Xanxicar, King Tuscar) that force credit for every player in sight with the killer as the sender. A
        /// notification is a repeat when it names the monster whose kill this quest last counted, within
        /// <see cref="RepeatWindowMs"/>; the same monster object dying again later (a respawn) is a new kill.
        /// </summary>
        public static bool IsRepeat(GameLiving killed, GameLiving lastCounted, long lastCountedAt, long now) =>
            killed != null && lastCounted != null && ReferenceEquals(killed, lastCounted) && now - lastCountedAt < RepeatWindowMs;
    }
}
