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
    /// online and free frontier gamebots are counted for its zerg (<see cref="FrontierBots"/>): the size is a third of the
    /// online ones, the floor needs 16 free. <paramref name="freeFrontierBotsStandIn"/> and
    /// <paramref name="onlineFrontierBotsStandIn"/> replace the counts (the rehearsal, which places no full roster); with only
    /// the free stand-in given, the online count is the same.
    /// </summary>
    public static List<AutonomousRvrEventLayer.BattlegroupChange> PulseBattlegroups(long now, bool force = false,
        IReadOnlyDictionary<eRealm, int> freeFrontierBotsStandIn = null, IReadOnlyDictionary<eRealm, int> onlineFrontierBotsStandIn = null)
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
        // #599 step 3 review: the zerg's followers go on the roster of the event its lead took it to, under the cap, before
        // the candidates are read (a follower left out leaves the zerg in this pulse: why=cap, event, defense or level).
        var warbandGroups = new Dictionary<string, Group>(warbands.Count);
        foreach (var warband in warbands)
            warbandGroups.TryAdd(warband.Id, warband.Group);
        AutonomousRvrEventLayer.EnrolZergFollowers(id => warbandGroups.TryGetValue(id, out Group group) ? ZergFollowerForce(id, group) : null, now);
        var candidates = new List<AutonomousRvrEventLayer.BattlegroupCandidate>(warbands.Count);
        var groups = new Dictionary<string, Group>(warbands.Count);
        var warbandBots = new Dictionary<string, int>(warbands.Count);
        // The force each committed warband is in (the zerg's own attack keeps it, AutonomousRvrEventLayer.ZergLeadLeaveReason).
        Dictionary<string, string> committed = AutonomousRvrEventLayer.ForceTargets();
        foreach (var warband in warbands)
        {
            List<GameLiving> members = warband.Group.GetMembersInTheGroup();
            string eventTarget = committed.GetValueOrDefault(warband.Id);
            candidates.Add(new(warband.Id, warband.Leader.Realm, warband.Leader.CurrentRegionID, warband.Leader.X, warband.Leader.Y,
                members.Count, AutonomousRvrEventLayer.IsForceCommitted(warband.Id, now),
                warband.Hosted || members.Any(member => member is GamePlayer), eventTarget));
            groups[warband.Id] = warband.Group;
            warbandBots[warband.Id] = members.Count(member => member is GameBot);
        }
        IReadOnlyDictionary<eRealm, int> free = freeFrontierBotsStandIn, online = onlineFrontierBotsStandIn ?? freeFrontierBotsStandIn;
        if (free == null || online == null)
        {
            (Dictionary<eRealm, int> countedOnline, Dictionary<eRealm, int> countedFree) = FrontierBots(candidates, warbandBots, committed);
            free ??= countedFree;
            online ??= countedOnline;
        }
        bool scheduleOn = AutonomousBotSchedule.CurrentMode != AutonomousBotSchedule.Mode.Off;
        RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
        ushort[] frontiers = catalog?.Frontiers.Select(frontier => frontier.Region).ToArray();
        List<AutonomousRvrEventLayer.BattlegroupChange> changes =
            AutonomousRvrEventLayer.PulseBattlegroups(candidates, now, free, scheduleOn, frontiers is { Length: > 0 } ? frontiers : null, online);
        Volatile.Write(ref _battlegroupGroups, groups);
        return changes;
    }

    /// <summary>
    /// #599 step 3: each realm's frontier gamebots for its zerg (owner 50A). Online: every gamebot of the realm's RvR warbands
    /// and every solo RvR gamebot, those in a keep or relic event included; the zerg's size is a third of these. Free: the
    /// heads of the warbands in no event and with no player, plus the solo RvR gamebots that are alive, spoken for by no
    /// player and in no event; the floor of 16 needs these. Autonomous gamebots only (the registry holds no players and no
    /// companions; a player in a warband is not counted online).
    /// </summary>
    private static (Dictionary<eRealm, int> Online, Dictionary<eRealm, int> Free) FrontierBots(
        IReadOnlyList<AutonomousRvrEventLayer.BattlegroupCandidate> warbands, IReadOnlyDictionary<string, int> warbandBots,
        IReadOnlyDictionary<string, string> committed)
    {
        var online = new Dictionary<eRealm, int>();
        var free = new Dictionary<eRealm, int>();
        foreach (AutonomousRvrEventLayer.BattlegroupCandidate warband in warbands)
        {
            online[warband.Realm] = online.GetValueOrDefault(warband.Realm) + warbandBots.GetValueOrDefault(warband.ForceId);
            if (!warband.InEvent && !warband.HostsPlayer)
                free[warband.Realm] = free.GetValueOrDefault(warband.Realm) + warband.Heads;
        }
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            if (bot.Group != null || bot.IsTemporaryGroupHelper || !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR))
                continue;
            online[bot.Realm] = online.GetValueOrDefault(bot.Realm) + 1;
            if (bot.IsAlive && !BotDutyLeases.IsSpokenForByPlayer(bot) && !committed.ContainsKey($"rvr-{bot.DatabaseID}"))
                free[bot.Realm] = free.GetValueOrDefault(bot.Realm) + 1;
        }
        return (online, free);
    }

    /// <summary>
    /// A zerg follower's force for the event's roster, counted as the warband's own review counts it (its RvR gamebots,
    /// ForceStrength.Of), so the roster holds the same heads either way.
    /// </summary>
    private static AutonomousRvrEventLayer.Force ZergFollowerForce(string forceId, Group group)
    {
        GameBot[] bots = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(member => member.IsAutonomousWorldBot && AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.RvR)).ToArray();
        if (bots.Length == 0) return null;
        ForceStrength strength = ForceStrength.Of(bots);
        return new AutonomousRvrEventLayer.Force(forceId, bots[0].Realm, strength.Members, strength.AverageLevel, strength.Healers,
            MemberIds: bots.Select(member => member.DatabaseID).ToArray(), MinimumMemberLevel: strength.MinimumLevel);
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
