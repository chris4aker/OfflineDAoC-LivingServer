using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// #598 option C (owner, 2026-11-24): a whole bot group goes to a battleground together, as real groups did. Until 1.2.3 a
/// bot in a bot group was never drawn for a battleground (AutonomousObjectiveAssignments: group-owned bots are kept out of
/// both draw pools), so a bracket's bots spent every group task in PvE.
/// - When: only at the group's natural break, the end of its shared PvE task (never mid-task), and only for a party of
///   gamebots alone (no player, not hosted, not in a raid), every member alive, out of combat, with room in its bags (the
///   owner's rule: a gamebot sells at a natural task break, so a nearly full bag goes to sell instead), and every member in
///   the same battleground bracket with the battleground goal allowed at its level.
/// - How many: the group's members count against the band's battleground share. It goes only when the share's open slots
///   hold the whole group, with the odds a single bot gets (AutonomousObjectiveAssignments.GroupTakesBattleground); the
///   next one-minute pass counts its members as battleground goals, as it counts any group's members.
/// - The trip: one task for the group (45-120 minutes, like any task). The leader leads: the members follow it to the
///   realm teleporter and it takes the gathered party through in one go (the town teleporter's party code). Inside, every
///   member runs the battleground turn, so the realm's wave, rally and storm count them like any battleground bot; while
///   the realm roams, the members keep with their leader. At the end (after the fight in hand, five minutes at most) the
///   group breaks up and each member starts its next task, the between-task services (selling, training) first.
/// Groups do not survive a server restart: after one, each member keeps its battleground task alone.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    public const string SharedTaskExpiredReason = "Shared group task expired";
    /// <summary>An ending trip waits this long at most for a fight in hand to end.</summary>
    public const long BattlegroundTripEndGraceMilliseconds = 5 * 60_000L;
    public const string BattlegroundTripPhase = "Battleground trip";

    private sealed class BattlegroundTrip
    {
        public required string Id { get; init; }
        public required GameBot Leader { get; set; }
        public required ushort BracketRegion { get; init; }
        public required string BracketName { get; init; }
        public required long ExpiresTick { get; init; }
    }

    private static readonly Dictionary<Group, BattlegroundTrip> Trips = new();
    private static int _nextTripNumber;

    /// <summary>
    /// Pure: may a bot group go to a battleground together at its task's end? Two to eight members, gamebots alone, none
    /// dead, fighting or with nearly full bags, every level in one bracket and allowed the battleground goal.
    /// </summary>
    public static bool CanGoToBattlegroundTogether(IReadOnlyList<int> levels, bool onlyGamebots, bool hostsPlayer, bool inRaid,
        bool anyDead, bool anyInCombat, bool anyBagsNearlyFull, Func<int, bool> battlegroundAllowed)
    {
        if (levels == null || levels.Count < 2 || levels.Count > 8 || !onlyGamebots || hostsPlayer || inRaid || anyDead ||
            anyInCombat || anyBagsNearlyFull)
            return false;
        BattlegroundBrackets.Bracket first = BattlegroundBrackets.ForLevel(levels[0]);
        return first != null && levels.All(level => BattlegroundBrackets.ForLevel(level)?.Name == first.Name &&
                                                    (battlegroundAllowed?.Invoke(level) ?? true));
    }

    /// <summary>Under <see cref="Sync"/>, from <see cref="FinishGroupTask"/> at the shared task's end. True when the group goes.</summary>
    private static bool TryStartBattlegroundTrip(Session session, string reason)
    {
        if (reason != SharedTaskExpiredReason || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
            !session.TaskClock.HasExpired(GameLoop.GameLoopTime))
            return false;
        Group group = session.Group;
        GameBot[] members = AllBotMembers(group);
        var everyone = group.GetMembersInTheGroup();
        bool onlyGamebots = everyone.Count == members.Length && members.All(member => member.PersistentRecord != null);
        if (!CanGoToBattlegroundTogether(members.Select(member => (int)member.Level).ToArray(), onlyGamebots,
                session.HostedPlayer != null || session.FromPlayersGroup || BotLedGroups.IsHostedGroup(group),
                AutonomousRealmRaid.GetView(group) != null, members.Any(member => !member.IsAlive),
                members.Any(member => member.InCombat || member.IsAttacking),
                members.Any(AutonomousBotEconomy.IsBackpackNearlyFull),
                level => AutonomousBotGoalPolicy.Allows(level, eAutonomousObjectiveKind.Battleground)))
            return false;
        GameBot leader = ChooseLeader(session, members) ?? members[0];
        BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForLevel(leader.Level);
        if (bracket == null || !AutonomousObjectiveAssignments.TryReserveGroupBattleground(leader.Realm,
                AutonomousObjectiveAssignments.BandOf(leader.Level), members.Length, Random.Shared.NextDouble()))
            return false;

        // The PvE task ends as at any natural break, but the group stays together.
        session.Ending = true;
        BotGroupRetreat.Clear(group);
        AutonomousDefensivePull.Cancel(group);
        Log.Info($"AUTONOMOUS_GROUP_TASK_ENDED group={session.Id} objective={session.ObjectiveKind} size={members.Length} " +
                 $"members=\"{string.Join(",", members.Select(member => member.Name))}\" reason=\"{reason}; the group goes to {bracket.Name} together\"");
        AutonomousBotBonds.OnPartyFinished(members, session.TaskClock.HasStarted, true, session.Wipes,
            session.Camp?.ZoneName ?? session.RendezvousName);
        LogGroupEnded(session, members, reason, "finished/battleground");
        Sessions.Remove(group);

        long durationMs = Random.Shared.NextInt64((long)AutonomousObjectiveAssignments.MinimumPveTenure.TotalMilliseconds,
            (long)AutonomousObjectiveAssignments.MaximumPveTenure.TotalMilliseconds + 1);
        var trip = new BattlegroundTrip
        {
            Id = $"bgtrip-{GlobalConstants.RealmToName(leader.Realm).ToLowerInvariant()}-{RuntimeGroupToken}-{++_nextTripNumber:000}",
            Leader = leader, BracketRegion = bracket.RegionId, BracketName = bracket.Name,
            ExpiresTick = GameLoop.GameLoopTime + durationMs,
        };
        DateTime expiresUtc = DateTime.UtcNow.AddMilliseconds(durationMs);
        foreach (GameBot member in members)
        {
            BotDutyLeases.Release(member, eDutyLeaseKind.BotGroupSession, session.Id);
            AutonomousObjectiveAssignments.MarkPveTaskCompleted(member);
            AutonomousObjectiveAssignments.AssignGroupBattleground(member, trip.Id, expiresUtc, leader.Name, bracket.Name);
            if (BotDutyLeases.WouldGrant(member, eDutyLeaseKind.BotGroupSession, trip.Id))
                BotDutyLeases.Claim(member, eDutyLeaseKind.BotGroupSession, trip.Id, DutyLeaseReason.GroupSession,
                    "Battleground party", group);
        }
        Trips[group] = trip;
        WriteTripMetadata(trip, members);
        Log.Info($"AUTONOMOUS_GROUP_BATTLEGROUND_TRIP group={trip.Id} from={session.Id} realm={GlobalConstants.RealmToName(leader.Realm)} " +
                 $"battleground={bracket.Name} size={members.Length} leader=\"{leader.Name}\" " +
                 $"levels=\"{string.Join(",", members.Select(member => member.Level))}\" minutes={durationMs / 60_000}");
        return true;
    }

    /// <summary>
    /// Under <see cref="Sync"/>, from <see cref="Pulse"/>: runs a battleground trip's group. True when the group is a trip's
    /// (or a battleground group left with no trip, which breaks up); <paramref name="directive"/> is then the pulse's answer.
    /// </summary>
    private static bool TryPulseBattlegroundTrip(Group group, GameBot[] members, out Directive directive)
    {
        directive = null;
        bool hasPlayer = group.GetMembersInTheGroup().Any(member => member is GamePlayer);
        if (!Trips.TryGetValue(group, out BattlegroundTrip trip))
        {
            // A group of battleground goals with no trip (none is ever formed but by a trip): each member goes on alone.
            if (hasPlayer || Sessions.ContainsKey(group) ||
                !members.All(member => AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.Battleground)))
                return false;
            group.DisbandGroup();
            return true;
        }
        if (hasPlayer)
        {
            // A real player joined: the party is the player's now. Each member keeps its battleground task.
            EndBattlegroundTrip(group, trip, members, "A player joined the battleground party", newGoals: false, disband: false);
            return false;
        }
        // A member who outgrew the bracket leaves the party and ends its own tour.
        foreach (GameBot outgrown in members.Where(member => BattlegroundBrackets.ForLevel(member.Level)?.RegionId != trip.BracketRegion).ToArray())
        {
            group.RemoveMember(outgrown);
            AutonomousObjectiveAssignments.EndBattlegroundTour(outgrown, $"Outgrew {trip.BracketName} at level {outgrown.Level}");
        }
        members = BotMembers(group);
        if (members.Length < 2)
        {
            // The last member keeps its battleground task alone.
            EndBattlegroundTrip(group, trip, members, "Fewer than two members remain", newGoals: false, disband: true);
            return true;
        }
        long now = GameLoop.GameLoopTime;
        if (now >= trip.ExpiresTick &&
            (now - trip.ExpiresTick >= BattlegroundTripEndGraceMilliseconds || !members.Any(member => member.InCombat || member.IsAttacking)))
        {
            EndBattlegroundTrip(group, trip, members, "Battleground trip ended", newGoals: true, disband: true);
            return true;
        }
        if (trip.Leader == null || !trip.Leader.IsAlive || trip.Leader.Group != group)
        {
            GameBot next = ChooseLeader(group, members);
            if (next != null && next != trip.Leader)
            {
                Log.Info($"AUTONOMOUS_GROUP_BATTLEGROUND_TRIP_LEADER group={trip.Id} leader=\"{next.Name}\" was=\"{trip.Leader?.Name}\"");
                trip.Leader = next;
                WriteTripMetadata(trip, members);
            }
        }
        GameBot leader = trip.Leader;
        directive = new Directive(trip.Id, BattlegroundTripPhase, TripGoal(trip), $"Led by {leader.Name}",
            eAutonomousObjectiveKind.Battleground, leader, new Vector3(leader.X, leader.Y, leader.Z), null, members.Length,
            (int)Math.Round(members.Average(member => member.Level)), 0, 0, false)
        {
            LeaderName = leader.Name,
            BattlegroundTrip = true,
            RendezvousRegion = trip.BracketRegion,
        };
        return true;
    }

    private static string TripGoal(BattlegroundTrip trip) => $"Battleground: {trip.BracketName}, as a group";

    private static void WriteTripMetadata(BattlegroundTrip trip, GameBot[] members)
    {
        foreach (GameBot member in members)
            SetMetadata(member, trip.Id, BattlegroundTripPhase, TripGoal(trip), $"Led by {trip.Leader.Name}",
                new StoredMetadata { LeaderName = trip.Leader.Name, RendezvousName = trip.BracketName });
    }

    private static void EndBattlegroundTrip(Group group, BattlegroundTrip trip, GameBot[] members, string reason, bool newGoals, bool disband)
    {
        Trips.Remove(group);
        foreach (GameBot member in AllBotMembers(group))
        {
            BotDutyLeases.Release(member, eDutyLeaseKind.BotGroupSession, trip.Id);
            ClearMetadata(member, true);
        }
        Log.Info($"AUTONOMOUS_GROUP_BATTLEGROUND_TRIP_ENDED group={trip.Id} battleground={trip.BracketName} size={members.Length} " +
                 $"members=\"{string.Join(",", members.Select(member => member.Name))}\" reason=\"{reason}\"");
        if (disband) group.DisbandGroup();
        if (!newGoals) return;
        foreach (GameBot member in members)
            AutonomousObjectiveAssignments.BeginSoloAfterGroupTask(member, reason + "; choosing independent work");
    }

    /// <summary>The leader of the bot's battleground trip, or null when it is on none.</summary>
    public static GameBot BattlegroundTripLeader(GameBot bot)
    {
        if (bot?.Group == null) return null;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            return Trips.TryGetValue(bot.Group, out BattlegroundTrip trip) ? trip.Leader : null;
    }

    /// <summary>Test view: the number of battleground trips running.</summary>
    public static int BattlegroundTripCount
    {
        get { using (AiProfile.Lock(Sync, AiSystem.LockCoordinator)) return Trips.Count; }
    }
}
