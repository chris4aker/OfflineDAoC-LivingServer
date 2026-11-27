using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #571 step 1, leader focus: the damage dealers of a real player's group fight the player's target before their own
    /// attacker or their top threat. Port of rambodolphin13/DividedRealms <c>ad8e087c</c> (bots/BotBrain.LeaderFocus.cs,
    /// GPL-3.0), widened to the player's whole group (owner, #663 answer 1: /spawn and /raid helpers, hired companions,
    /// /bot companions and befriended gamebots). DetShaft/OfflineDAoC-LivingServer <c>77e74f670</c> puts the player's
    /// target first in its group fight mode the same way.
    ///
    /// Period sources (docs/rvr-audit-1.2.3/reference.md §3.4; docs/research/rvr-field-1.2/ERA-RESEARCH.md §2): one main
    /// assist, and "Everyone then makes an /assist macro" (FH Nov 2003); nukers and melee assist strictly and never split
    /// damage; supports and peel tanks are not on the train. In the player's group the player is the main assist.
    ///
    /// The player's target is the fight the player is in (PlayerLedPullCoordinator.FindLeaderTarget: he attacks it or casts
    /// harmful magic at it, or his pet fights it), never one he has only selected (DetShaft owner decision #170).
    ///
    /// Where: the #663 places only (frontier, battlegrounds, Darkness Falls and the neutral battle dungeons); PvE waits on
    /// the owner (<see cref="InPve"/>, #571 needs-owner, after "PvE stays as it is" on #663).
    /// </summary>
    public static class CompanionLeaderFocus
    {
        public const string PropertyKey = "bot_leader_focus";

        public static bool On => Properties.BOT_LEADER_FOCUS;

        /// <summary>
        /// Leader focus runs for this bot: the switch is on, the bot is in a real player's group, and it is a damage dealer.
        /// A tank keeps its own choice (it holds its fight and peels the enemy on a groupmate, BotBrain.FindProtectionTarget);
        /// a support heals and buffs (it never reaches the target choice in a group with a fighter in it).
        /// </summary>
        public static bool AppliesTo(bool on, bool playerLedGroup, bool tank, bool support) =>
            on && playerLedGroup && !tank && !support;

        /// <summary>
        /// Leader focus in PvE too. Off until the owner decides (#571, `needs-owner`): on #663 he answered "PvE stays as it
        /// is", so for now it runs only where realms fight (<see cref="InPlace"/>). The one line to change if he says yes.
        /// </summary>
        public const bool InPve = false;

        /// <summary>
        /// Where leader focus runs: the frontier, the battlegrounds and Darkness Falls and the other neutral battle dungeons
        /// (the #663 places, AutonomousWorldBotController.IsBattleground), and in PvE only with <paramref name="inPve"/>.
        /// </summary>
        public static bool InPlace(bool warZone, bool inPve = InPve) => warZone || inPve;

        /// <summary>
        /// The player's target is taken: it is a live target the bot may fight (CanAggroTarget: /defensive's range, the
        /// attack rules, the grey-con rule with its exemption for the player's own fight), it is not a target a groupmate
        /// holds with a mesmerize or root (hitting it breaks the hold), it is within the bot's aggro distance, and no epic
        /// rule forbids the bot to hit it (<paramref name="mustNotHit"/>: EpicEncounterTactics.MustNotHit, which covers
        /// GameBot.RefusesAttackOn: Behemoth, Funus during his puzzle, an add the raid leaves alive). Otherwise the bot would
        /// idle on a target it may not hit while its own attacker hits it.
        /// </summary>
        public static bool Takes(bool live, bool mayFight, bool breakablyControlled, bool inReach, bool mustNotHit) =>
            live && mayFight && !breakablyControlled && inReach && !mustNotHit;

        /// <summary>
        /// The target the choice ends on: a tank's protection target first, then the player's target, then the bot's own
        /// attacker, then its pet's fight, then its top threat. <paramref name="leaderFocus"/> is null when leader focus
        /// does not apply or does not take the target, which leaves the choice as it was before.
        /// </summary>
        public static T Choose<T>(T protection, T leaderFocus, T directAttacker, T petTarget, T threat) where T : class =>
            protection ?? leaderFocus ?? directAttacker ?? petTarget ?? threat;

        /// <summary>
        /// Log the decision only when leader focus changed the outcome: the bot would otherwise have fought another target
        /// (<paramref name="without"/>), and the bot has not already logged this target.
        /// </summary>
        public static bool Logs<T>(T chosen, T without, T lastLogged) where T : class =>
            chosen != null && !ReferenceEquals(chosen, without) && !ReferenceEquals(chosen, lastLogged);

        /// <summary>
        /// The log row's why=: what leader focus took the bot off. <paramref name="directAttacker"/> is the attacker the
        /// target choice saw (after BotBrain.KillOrderAttacker), <paramref name="petTarget"/> its pet's fight.
        /// </summary>
        public static string Why<T>(T without, T directAttacker, T petTarget) where T : class =>
            without == null ? "idle" :
            ReferenceEquals(without, directAttacker) ? "attacker" :
            ReferenceEquals(without, petTarget) ? "pet" : "threat";
    }
}
