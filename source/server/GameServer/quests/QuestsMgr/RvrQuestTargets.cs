namespace DOL.GS.Quests
{
    /// <summary>
    /// Who an RvR or battleground kill quest counts as an enemy player (1.2, 2026-11-15). The Atlas quests (Pazz's
    /// battleground dailies in Thidranki and Caledonia, the realm dailies, weeklies and monthlies, the Darkness Falls
    /// weekly and the hardcore "kill a player") counted a kill only when the victim was a GamePlayer. Offline DAoC is
    /// single-player: the enemy realms' players are autonomous gamebots (GameBot : GameNPC, IGamePlayer), so none of
    /// those quests could ever be finished. A gamebot now counts as the player it stands in for.
    ///
    /// <para>The rule: the victim is of a realm (not None) other than the quest player's, and it is either a real
    /// GamePlayer (as before) or a GameBot that is dying. Who gets the kill is unchanged: GameLiving.ProcessDeath
    /// credits a bot's death exactly as a player's (QuestKillCredit.PlayersCredited: attackers, pet and companion
    /// owners, an invited gamebot's leader, and their groups in range), so a group or companion kill counts for a
    /// gamebot victim exactly where it would for a player victim.</para>
    ///
    /// <para>Pets don't count. A bot's pet (a necromancer's shade, a spirit master's or an animist's pet, a charmed
    /// monster) is a GameNPC, as a player's pet is, and the originals never counted a player's pet.</para>
    ///
    /// <para>A gamebot counts only while it is dying (GameLiving.IsBeingHandledByReaperService, set by Die and held
    /// through ProcessDeath). GameLiving.RemoveFromWorld also tells every attacker "EnemyKilled" when a living leaves the
    /// world, and gamebots leave it far more often than a person zones: a stablemaster route, a port home, a region
    /// change or the population manager's despawn. Without the guard, a bot that rode off mid-fight would be a kill. A
    /// real GamePlayer keeps the original rule.</para>
    /// </summary>
    public static class RvrQuestTargets
    {
        /// <summary>True when killing <paramref name="target"/> is an enemy player kill for <paramref name="questPlayer"/>'s quest.</summary>
        public static bool IsEnemyPlayer(GameLiving target, GamePlayer questPlayer)
        {
            if (target == null || questPlayer == null)
                return false;
            if (target.Realm == eRealm.None || target.Realm == questPlayer.Realm)
                return false;
            return target switch
            {
                GamePlayer => true,
                GameBot bot => bot.IsBeingHandledByReaperService,
                _ => false,
            };
        }
    }
}
