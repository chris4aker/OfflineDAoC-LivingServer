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
        /// The player's target is taken: it is a live target the bot may fight (CanAggroTarget: /defensive's range, the
        /// attack rules, the grey-con rule with its exemption for the player's own fight), it is not a target a groupmate
        /// holds with a mesmerize or root (hitting it breaks the hold), and it is within the bot's aggro distance.
        /// </summary>
        public static bool Takes(bool live, bool mayFight, bool breakablyControlled, bool inReach) =>
            live && mayFight && !breakablyControlled && inReach;

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
    }
}
