using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DOL.GS;

/// <summary>
/// 1.2.3 (#569 step 2). Port of zomboden/OfflineDAoC-True165 fb7487a (GPL-3.0, shadowofze): a party that keeps losing
/// members to monsters far above them near one spot ends its task there instead of regrouping beside the killers again
/// (AutonomousGroupDangerRetreat has the rule and the evidence). Autonomous gamebot PvE parties only: realm expeditions,
/// parties hosting a real player, parties formed from a player's group and temporary helpers are never retreated.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private sealed class DangerDeaths
    {
        public readonly List<AutonomousGroupDangerRetreat.Death> Deaths = new();
        public AutonomousGroupDangerRetreat.Death? Pending;
        public string PendingKiller = string.Empty;
        public int PendingBand;
    }
    private static readonly ConditionalWeakTable<Session, DangerDeaths> PartyDangerDeaths = new();

    /// <summary>Whether a death in this party is counted at all (pure; the coordinator passes the facts).</summary>
    public static bool CountsPartyDangerDeath(bool groupPve, bool ending, bool taskStarted, bool expedition, bool hostsPlayer,
        bool fromPlayersGroup) =>
        groupPve && !ending && taskStarted && !expedition && !hostsPlayer && !fromPlayersGroup;

    /// <summary>
    /// Called once from AutonomousDeathDiagnostics.Record for a grouped autonomous bot killed by a monster. Records the death
    /// when the killer is far above the member and flags the party for a retreat when the rule says so; the party acts on
    /// its next pulse (never inside the death itself).
    /// </summary>
    public static void NoteMemberDeath(GameBot bot, GameLiving killer)
    {
        Group group = bot?.Group;
        if (group == null || killer == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
            !AutonomousGroupDangerRetreat.IsFarAbove(killer.Level, bot.Level))
            return;   // cheap pre-check before the party lock
        long now = GameLoop.GameLoopTime;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            if (!TryGetSession(group, out Session session) ||
                !CountsPartyDangerDeath(session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve, session.Ending,
                    session.TaskClock.HasStarted, AutonomousRealmRaid.GetView(group) != null,
                    session.HostedPlayer != null || BotLedGroups.IsHostedGroup(group), session.FromPlayersGroup))
                return;
            GameBot[] members = BotMembers(group);
            int average = members.Length == 0 ? bot.Level : (int)Math.Round(members.Average(member => member.Level));
            if (!AutonomousGroupDangerRetreat.IsFarAboveParty(killer.Level, bot.Level, average, session.PreferredLevelBonus))
                return;
            DangerDeaths state = PartyDangerDeaths.GetOrCreateValue(session);
            AutonomousGroupDangerRetreat.Prune(state.Deaths, now);
            state.Deaths.Add(new(MemberKey(bot), bot.CurrentRegionID, bot.X, bot.Y, bot.Level, killer.Level, now));
            bool wipe = members.Length > 1 && members.All(member => !member.IsAlive || member == bot);
            if (state.Pending == null &&
                AutonomousGroupDangerRetreat.ShouldRetreat(state.Deaths, now, wipe, out AutonomousGroupDangerRetreat.Death anchor))
            {
                state.Pending = anchor;
                state.PendingKiller = killer.Name ?? string.Empty;
                state.PendingBand = AutonomousGroupDangerRetreat.BlockedBand(average,
                    AutonomousGroupDangerRetreat.HighestKiller(state.Deaths, anchor, now));
            }
        }
    }

    /// <summary>Caller holds Sync (UpdateSession). Ends the task, benches the camp for the band and remembers the spot.</summary>
    private static bool TryRetreatFromDanger(Session session, GameBot[] members)
    {
        if (session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || !PartyDangerDeaths.TryGetValue(session, out DangerDeaths state) ||
            state.Pending is not { } anchor)
            return false;
        long now = GameLoop.GameLoopTime;
        state.Pending = null;
        int deaths = state.Deaths.Count(death => now - death.Tick <= AutonomousGroupDangerRetreat.WindowMilliseconds);
        AutonomousGroupDangerRetreat.MarkSpot(anchor.Region, anchor.X, anchor.Y, state.PendingBand, now);
        SharedCamp camp = session.Camp;
        bool blameCamp = camp != null && (string.Equals(session.ArrivalLoggedCampId, camp.Id, StringComparison.Ordinal) ||
            camp.RegionId == anchor.Region && Math.Pow(camp.X - anchor.X, 2) + Math.Pow(camp.Y - anchor.Y, 2) <=
            Math.Pow(AutonomousGroupDangerRetreat.CampBlameRadius, 2));
        if (blameCamp)
            AutonomousGroupDangerRetreat.BenchCamp(camp.Id, state.PendingBand, now);
        Log.Warn($"AUTONOMOUS_GROUP_DANGER_RETREAT group={session.Id} phase=\"{session.Phase}\" killer=\"{state.PendingKiller}\" " +
                 $"killerLevel={anchor.KillerLevel} victimLevel={anchor.BotLevel} deaths={deaths} " +
                 $"region={anchor.Region} x={anchor.X} y={anchor.Y} band={state.PendingBand} " +
                 $"camp=\"{(blameCamp ? camp.Id : string.Empty)}\" benchMinutes={AutonomousGroupDangerRetreat.BenchMilliseconds / 60_000} " +
                 $"size={members.Length} alive={members.Count(member => member.IsAlive)}");
        state.Deaths.Clear();
        // The released dead go to their own binds as solo bots (GameBot release), not back past the killers to the party.
        FinishGroupTask(session, $"Lost members to {state.PendingKiller} (level {anchor.KillerLevel}) far above them; " +
            $"the place is avoided by parties up to level {state.PendingBand}", "danger/far_above");
        return true;
    }
}
