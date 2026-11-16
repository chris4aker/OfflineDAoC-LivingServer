using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md): the group coordinator's hook point.
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>
    /// Hook: the RvR branch of group formation (TryFormGroups), after the pet cap picked <paramref name="picked"/> from
    /// <paramref name="pool"/> for a group of <paramref name="size"/> with <paramref name="leader"/>. Returns the
    /// members the tactics want instead (a role-built roster from the same pool), or null for no opinion. It runs
    /// under the coordinator's lock: read a fight board only with RvrFightBoards.Peek, never Get. A group formed here
    /// is bot-only; groups a player leads or joins never come through this branch.
    /// Reserved for work package 6 (rvr_roaming_parties).
    /// </summary>
    public static GameBot[] RvrTacticsRoster(GameBot leader, GameBot[] pool, GameBot[] picked, int size)
    {
        if (!Properties.RVR_ROAMING_PARTIES || !Properties.RVR_BOT_FIELD_TACTICS || !RvrTacticsScope.Allows(leader))
            return null;
        return null;
    }
}
