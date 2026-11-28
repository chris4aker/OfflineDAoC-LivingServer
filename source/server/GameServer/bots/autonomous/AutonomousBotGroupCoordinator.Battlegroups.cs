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

    /// <summary>
    /// One battlegroup pulse. <paramref name="force"/>: run now (the RvR fight rehearsal, #520). #599 step 3: each realm's
    /// free frontier gamebots are counted for its zerg's size (<see cref="FreeFrontierBots"/>);
    /// <paramref name="freeFrontierBotsStandIn"/> replaces the count (the rehearsal, which places no full roster).
    /// </summary>
    public static List<AutonomousRvrEventLayer.BattlegroupChange> PulseBattlegroups(long now, bool force = false,
        IReadOnlyDictionary<eRealm, int> freeFrontierBotsStandIn = null)
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
        IReadOnlyDictionary<eRealm, int> free = freeFrontierBotsStandIn ?? FreeFrontierBots(candidates);
        bool scheduleOn = AutonomousBotSchedule.CurrentMode != AutonomousBotSchedule.Mode.Off;
        RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
        ushort[] frontiers = catalog?.Frontiers.Select(frontier => frontier.Region).ToArray();
        List<AutonomousRvrEventLayer.BattlegroupChange> changes =
            AutonomousRvrEventLayer.PulseBattlegroups(candidates, now, free, scheduleOn, frontiers is { Length: > 0 } ? frontiers : null);
        Volatile.Write(ref _battlegroupGroups, groups);
        return changes;
    }

    /// <summary>
    /// #599 step 3: each realm's free frontier gamebots online, the zerg's base (owner 50A): the heads of the warbands in no
    /// event and with no player, plus the solo RvR gamebots that are alive, spoken for by no player and in no event.
    /// Autonomous gamebots only (the registry holds no players and no companions).
    /// </summary>
    private static Dictionary<eRealm, int> FreeFrontierBots(IReadOnlyList<AutonomousRvrEventLayer.BattlegroupCandidate> warbands)
    {
        var free = new Dictionary<eRealm, int>();
        foreach (AutonomousRvrEventLayer.BattlegroupCandidate warband in warbands)
            if (!warband.InEvent && !warband.HostsPlayer)
                free[warband.Realm] = free.GetValueOrDefault(warband.Realm) + warband.Heads;
        Dictionary<string, string> committed = AutonomousRvrEventLayer.ForceTargets();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            if (bot.Group == null && bot.IsAlive && !bot.IsTemporaryGroupHelper &&
                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) && !BotDutyLeases.IsSpokenForByPlayer(bot) &&
                !committed.ContainsKey($"rvr-{bot.DatabaseID}"))
                free[bot.Realm] = free.GetValueOrDefault(bot.Realm) + 1;
        return free;
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
