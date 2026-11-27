using System;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DOL.GS;

/// <summary>
/// The party pull-stall monitor (1.2.3, #569). Port of zomboden/OfflineDAoC-True165 0e0a397 (GPL-3.0, shadowofze):
/// AutonomousPartyPullStall has the rules and switches. Autonomous gamebot PvE parties only: realm expeditions hold on
/// their own staging orders, and a party hosting a real player (bot-led groups) or formed from a player's group is
/// never watched here, so the player's own group is never ended by it.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private sealed class PartyPullStallState
    {
        public long StallSince;
        public string StallCampId = string.Empty;
        public bool StallLogged;
        public long LastObservedTick;
        public long XpSampleTick;
        public long Xp;
    }

    private static readonly ConditionalWeakTable<Session, PartyPullStallState> PartyPullStalls = new();

    /// <summary>Whether the stall monitor watches this party at all (pure; the coordinator passes the session's facts).</summary>
    public static bool WatchesPullStall(bool groupPve, bool ending, int members, bool expedition, bool hostsPlayer,
        bool fromPlayersGroup) =>
        groupPve && !ending && members > 0 && !expedition && !hostsPlayer && !fromPlayersGroup;

    /// <summary>
    /// Caller holds Sync (UpdateSession). O(members), the experience sum at most every 5 s; no database or world scans.
    /// Returns false when it ended the party.
    /// </summary>
    private static bool ObservePartyPullStall(Session session, GameBot[] members, bool groupCombatActive)
    {
        if (!AutonomousPartyPullStall.StallLog && !AutonomousPartyPullStall.StallEnd)
            return true;
        bool hostsPlayer = session.HostedPlayer != null || BotLedGroups.IsHostedGroup(session.Group);
        if (!WatchesPullStall(session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve, session.Ending, members.Length,
                AutonomousRealmRaid.GetView(session.Group) != null, hostsPlayer, session.FromPlayersGroup))
        {
            if (PartyPullStalls.TryGetValue(session, out PartyPullStallState held)) held.StallSince = 0;
            return true;
        }
        PartyPullStallState state = PartyPullStalls.GetOrCreateValue(session);
        long now = GameLoop.GameLoopTime;
        long gap = state.LastObservedTick == 0 ? 0 : now - state.LastObservedTick;
        state.LastObservedTick = now;
        bool progress = groupCombatActive;
        if (now >= state.XpSampleTick)
        {
            // live experience, as the stuck watchdog reads it (the record only refreshes on save)
            long xp = 0;
            foreach (GameBot member in members)
                xp += member?.Experience ?? 0L;
            if (state.XpSampleTick != 0 && xp != state.Xp) progress = true;
            state.Xp = xp;
            state.XpSampleTick = now + AutonomousPartyPullStall.ExperienceSampleMilliseconds;
        }
        string campId = session.Camp?.Id ?? string.Empty;
        bool sameCamp = campId.Length > 0 && string.Equals(campId, state.StallCampId, StringComparison.Ordinal);
        long since = AutonomousPartyPullStall.NextStallClock(state.StallSince, campId.Length == 0 ? string.Empty : session.Phase,
            sameCamp, progress, gap, now);
        if (since != state.StallSince)
        {
            if (since == 0 || since == now) state.StallLogged = false;
            state.StallSince = since;
            state.StallCampId = campId;
        }
        if (since == 0) return true;
        PullStallAction action = AutonomousPartyPullStall.Decide(AutonomousPartyPullStall.PullStallStep(now - since),
            state.StallLogged, AutonomousPartyPullStall.StallLog, AutonomousPartyPullStall.StallEnd, mayEnd: true);
        if (action == PullStallAction.None) return true;

        GameBot puller = ChoosePuller(session, members);
        string gate = puller == null ? AutonomousPartyPullStall.GateNoPuller
            : CanInitiateNewPull(puller, false, false, out string named) ? AutonomousPartyPullStall.GateOpen : named;
        string pullerActivity = puller?.PersistentRecord?.Activity ?? string.Empty;
        (string holder, string detail) = PullGateDetail(gate, members, ChooseLeader(session, members), puller);
        int minutes = (int)((now - since) / 60_000);
        var fields = new
        {
            group = session.Id,
            phase = session.Phase,
            camp = campId,
            target = session.Camp?.MonsterName ?? string.Empty,
            zone = session.Camp?.ZoneName ?? string.Empty,
            // bot names under the keys the log pipeline drops (row-policy.json json_name_keys), free text under "text"
            bot = puller?.Name ?? string.Empty,
            text = pullerActivity,
            gate,
            name = holder,
            detail,
            minutes,
            ending = action == PullStallAction.End
        };
        if (action == PullStallAction.Log)
        {
            state.StallLogged = true;
            // A grinding gamebot party had no group fight and no experience for 6 minutes, and gate names what holds its pull.
            Log.Info("AUTONOMOUS_GROUP_PULL_STALL " + JsonSerializer.Serialize(fields));
            return true;
        }
        // The stalled party reached 11 minutes, so its task ends cleanly here before the 15-minute stuck watchdog teleport.
        Log.Warn("AUTONOMOUS_GROUP_PULL_STALL_END " + JsonSerializer.Serialize(fields));
        FinishGroupTask(session, $"Pull stalled {minutes} minutes at {campId}: {gate}");
        return false;
    }

    /// <summary>The member a resources or placement gate stops on and what holds it, for the log line only (never on the
    /// pull path).</summary>
    private static (string Name, string Detail) PullGateDetail(string gate, GameBot[] members, GameBot leader, GameBot puller)
    {
        if (gate == AutonomousPartyPullStall.GateResources)
            foreach (GameBot member in members)
            {
                string resources = AutonomousPartyPullStall.ResourceGate(member.HealthPercent, member.ManaPercent,
                    member.EndurancePercent, member.MaxMana > 0);
                if (resources != null) return (member.Name, resources);
            }
        if (gate == AutonomousPartyPullStall.GatePlacement && leader != null)
        {
            int radius = PullCohesionRadius(false, puller?.CurrentZone?.IsDungeon == true);
            foreach (GameBot member in members)
            {
                bool sameRegion = member.CurrentRegionID == leader.CurrentRegionID;
                string placement = AutonomousPartyPullStall.PlacementGate(member.IsOnStableMasterRoute, sameRegion,
                    sameRegion ? member.GetDistanceTo(leader) : int.MaxValue, radius);
                if (placement != null) return (member.Name, placement);
            }
        }
        return (string.Empty, string.Empty);
    }
}
