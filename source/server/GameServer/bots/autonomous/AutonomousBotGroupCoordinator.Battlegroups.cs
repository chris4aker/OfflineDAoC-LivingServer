using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// #599 step 1: feeds the event layer's battlegroup record (AutonomousRvrEventLayer.PulseBattlegroups) from the frontier
/// RvR warbands, every ten seconds, and keeps the force id to group map the controller and RealmWarbandSupport read.
/// The sessions are read under the coordinator's lock; the event layer is asked outside it. Autonomous gamebots only: a
/// warband that hosts a player is passed as such and never links.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private static long _nextBattlegroupTick;
    private static Dictionary<string, Group> _battlegroupGroups = new();

    /// <summary>One battlegroup pulse. <paramref name="force"/>: run now (the RvR fight rehearsal, #520).</summary>
    public static List<AutonomousRvrEventLayer.BattlegroupChange> PulseBattlegroups(long now, bool force = false)
    {
        if (!force && now < Interlocked.Read(ref _nextBattlegroupTick))
            return null;
        Interlocked.Exchange(ref _nextBattlegroupTick, now + 10_000);
        var warbands = new List<(string Id, Group Group, GameBot Leader, bool Hosted)>();
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            foreach (Session session in Sessions.Values)
                if (session.ObjectiveKind == eAutonomousObjectiveKind.RvR && !session.Ending && session.Leader != null &&
                    session.Group != null && !string.IsNullOrEmpty(session.Id))
                    warbands.Add((session.Id, session.Group, session.Leader, session.HostedPlayer != null));
        var candidates = new List<AutonomousRvrEventLayer.BattlegroupCandidate>(warbands.Count);
        var groups = new Dictionary<string, Group>(warbands.Count);
        foreach (var warband in warbands)
        {
            List<GameLiving> members = warband.Group.GetMembersInTheGroup();
            candidates.Add(new(warband.Id, warband.Leader.Realm, warband.Leader.CurrentRegionID, warband.Leader.X, warband.Leader.Y,
                members.Count, AutonomousRvrEventLayer.IsForceCommitted(warband.Id, now),
                warband.Hosted || members.Any(member => member is GamePlayer)));
            groups[warband.Id] = warband.Group;
        }
        List<AutonomousRvrEventLayer.BattlegroupChange> changes = AutonomousRvrEventLayer.PulseBattlegroups(candidates, now);
        Volatile.Write(ref _battlegroupGroups, groups);
        return changes;
    }

    /// <summary>The group behind a warband's force id, as of the last battlegroup pulse (null when it was not a warband).</summary>
    public static Group BattlegroupGroup(string forceId) =>
        forceId != null && Volatile.Read(ref _battlegroupGroups).TryGetValue(forceId, out Group group) ? group : null;

    /// <summary>The leader of the warband behind <paramref name="forceId"/> (the lead group a follower trails), or null.</summary>
    public static GameBot BattlegroupLeader(string forceId)
    {
        Group group = BattlegroupGroup(forceId);
        if (group == null) return null;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            return TryGetSession(group, out Session session) && session.Id == forceId ? session.Leader : null;
    }

    /// <summary>Tests.</summary>
    public static void ResetBattlegroups()
    {
        Interlocked.Exchange(ref _nextBattlegroupTick, 0);
        Volatile.Write(ref _battlegroupGroups, new Dictionary<string, Group>());
        AutonomousRvrEventLayer.ResetBattlegroups();
    }
}
