using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the world controller's hook points. Each is called by
// one line at its site and is a stub here: it asks the brain's gate (BotBrain.RvrTacticsActive: the master switch
// rvr_bot_field_tactics, a battle region and RvrTacticsScope) and its own switch, and then has no opinion, so the code
// at the site runs exactly as before. Work packages 1 and 4 fill the threat gate, 4 the travel hold, 5 and 6 the roam
// destination. A hook that does real work wraps it in AiProfile.Scope(AiSystem.RvrTactics).
public sealed partial class AutonomousWorldBotController
{
    /// <summary>
    /// Hook: TryEngageFrontierThreat, between perception (and the battleground gather and storm filters) and the target
    /// pick. Returns the targets this bot may pick from now: the same array for no opinion, fewer or none when the
    /// group's plan holds the bot back (it is not a looker; the group is watching or sizing up). A bot under attack
    /// never comes here: the scan runs only for a bot that is not fighting.
    /// Reserved for work packages 1 (rvr_bot_group_lookers) and 4 (rvr_bot_size_up).
    /// </summary>
    private GameLiving[] RvrTacticsGateThreats(BotBrain brain, GameBot bot, GameLiving[] visible)
    {
        if (visible.Length == 0 || brain?.RvrTacticsActive() != true || !(Properties.RVR_BOT_GROUP_LOOKERS || Properties.RVR_BOT_SIZE_UP))
            return visible;
        return visible;
    }

    /// <summary>
    /// Hook: the RvR turn, before the group's hold for a new pull (HoldBeforeNewGroupPull). True when the tactics own
    /// this turn (the group holds for stragglers, or regroups at its rally point after a retreat or a wipe); false for
    /// no opinion. Work package 4 (rvr_bot_size_up, rvr_bot_regroup): AutonomousWorldBotController.RvrEngagement.cs.
    /// </summary>
    private bool RvrTacticsTravelHold(BotBrain brain, GameBot bot)
    {
        if (brain?.RvrTacticsActive() != true || !(Properties.RVR_BOT_REGROUP || Properties.RVR_BOT_SIZE_UP))
            return false;
        return RvrStanceTravelHold(brain, bot);
    }

    /// <summary>
    /// Hook: ChooseRvrDestination, after the event layer chose (_rvrIntent and _rvrSharedEvent are set; a siege or a
    /// defense call comes first). <paramref name="chosen"/> is today's destination, null when there is none. Returns
    /// the destination the tactics want instead (the next check spot of the group's circuit, a lane's end), or null
    /// for no opinion. Reserved for work packages 5 (rvr_roam_lanes) and 6 (rvr_roaming_parties, rvr_roam_checks).
    /// </summary>
    private CampDestination RvrTacticsRoamDestination(GameBot bot, CampDestination chosen)
    {
        if (!(Properties.RVR_ROAM_LANES || Properties.RVR_ROAMING_PARTIES || Properties.RVR_ROAM_CHECKS) ||
            !BotBrain.RvrTacticsApplyTo(bot))
            return null;
        return null;
    }
}
