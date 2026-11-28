using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// Immutable, realm-separated support rosters for shared keep/relic events, and (#599 step 1) for battlegroups: the
    /// groups of one battlegroup outside an event share a roster, so the inc calls, the stance relay, the size-up and
    /// cross-group support reach across them as they do in an event. An event's roster wins over a battlegroup's.
    /// #599 step 4: a realm's battleground parties at one keep (AutonomousRvrEventLayer.BattlegroundBattlegroupsNow) share
    /// one too, flagged <see cref="Roster.Battleground"/>: it answers only for battleground-objective bots, and every other
    /// roster only for RvR-objective ones, as before.
    /// </summary>
    public static class RealmWarbandSupport
    {
        public sealed record Roster(object Scope, GameLiving[] Members, bool Battleground = false);
        private static Dictionary<Group, Roster> _groups = new();
        private static Dictionary<Group, Group> _leads = new();
        private static Dictionary<string, object> _scopes = new();
        private static long _next;
        public static Roster Get(GameBot bot) => bot?.Group != null &&
            Volatile.Read(ref _groups).TryGetValue(bot.Group, out var roster) &&
            AutonomousObjectiveAssignments.Is(bot, ObjectiveOf(roster)) ? roster : null;

        /// <summary>The goal a bot must hold for a roster to answer for it: the battleground's for a battleground battlegroup, else RvR. Pure.</summary>
        public static eAutonomousObjectiveKind ObjectiveOf(Roster roster) =>
            roster?.Battleground == true ? eAutonomousObjectiveKind.Battleground : eAutonomousObjectiveKind.RvR;

        /// <summary>
        /// #599 step 2: the lead group of a battlegroup's follower group, as of the last pulse; null for a lead group, a
        /// group in no battlegroup, or a group whose roster is an event's. Lock-free (the fight-board builder reads it).
        /// Only autonomous gamebot warbands are ever in a battlegroup: a group that hosts a player never links.
        /// </summary>
        public static Group BattlegroupLeadOf(Group group) =>
            group != null && Volatile.Read(ref _leads).TryGetValue(group, out Group lead) ? lead : null;

        /// <summary>Rebuilds the rosters every ten seconds; <paramref name="immediately"/>: now (the RvR fight rehearsal, #520).</summary>
        public static void Pulse(long now, bool immediately = false)
        {
            if (!immediately && now < _next) return;
            _next = now + 10_000;
            var targets = AutonomousRvrEventLayer.ForceTargets();
            var grouped = new Dictionary<string, List<GameBot>>();
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (bot.Group == null || !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) continue;
                string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
                if (!targets.TryGetValue(force, out string target)) continue;
                string key = $"{target}/{bot.Realm}";
                if (!grouped.TryGetValue(key, out var members)) grouped[key] = members = new();
                members.Add(bot);
            }
            var groups = new Dictionary<Group, Roster>();
            var leads = new Dictionary<Group, Group>();
            var scopes = new Dictionary<string, object>();
            foreach (var pair in grouped)
            {
                object scope = _scopes.GetValueOrDefault(pair.Key) ?? new object();
                scopes[pair.Key] = scope;
                var roster = new Roster(scope, pair.Value.Cast<GameLiving>().ToArray());
                foreach (Group group in pair.Value.Select(b => b.Group).Where(g => g != null).Distinct()) groups[group] = roster;
            }
            foreach (AutonomousRvrEventLayer.BattlegroupView battlegroup in AutonomousRvrEventLayer.BattlegroupsNow())
            {
                Group[] linked = battlegroup.Members.Select(AutonomousBotGroupCoordinator.BattlegroupGroup)
                    .Where(group => group != null && !groups.ContainsKey(group)).Distinct().ToArray();
                if (linked.Length < 2) continue;
                GameLiving[] members = linked.SelectMany(group => group.GetMembersInTheGroup()).OfType<GameBot>()
                    .Where(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)).Cast<GameLiving>().ToArray();
                if (members.Length == 0) continue;
                string key = battlegroup.Id;
                object scope = _scopes.GetValueOrDefault(key) ?? new object();
                scopes[key] = scope;
                var roster = new Roster(scope, members);
                foreach (Group group in linked) groups[group] = roster;
                // #599 step 2: each follower group's lead, for the battlegroup's one target call.
                Group lead = AutonomousBotGroupCoordinator.BattlegroupGroup(battlegroup.LeadForceId);
                if (lead != null && System.Array.IndexOf(linked, lead) >= 0)
                    foreach (Group group in linked)
                        if (group != lead) leads[group] = lead;
            }
            // #599 step 4: each realm's battleground parties at one keep, one roster and one lead party (step 2's call).
            foreach (AutonomousRvrEventLayer.BattlegroundBattlegroupView battlegroup in AutonomousRvrEventLayer.BattlegroundBattlegroupsNow())
            {
                Group[] linked = battlegroup.Members.Select(AutonomousBotGroupCoordinator.BattlegroundBattlegroupGroup)
                    .Where(group => group != null && !groups.ContainsKey(group)).Distinct().ToArray();
                if (linked.Length < 2) continue;
                GameLiving[] members = linked.SelectMany(group => group.GetMembersInTheGroup()).OfType<GameBot>()
                    .Where(bot => bot.IsAutonomousWorldBot && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground))
                    .Cast<GameLiving>().ToArray();
                if (members.Length == 0) continue;
                string key = battlegroup.Id;
                object scope = _scopes.GetValueOrDefault(key) ?? new object();
                scopes[key] = scope;
                var roster = new Roster(scope, members, Battleground: true);
                foreach (Group group in linked) groups[group] = roster;
                Group lead = AutonomousBotGroupCoordinator.BattlegroundBattlegroupGroup(battlegroup.LeadPartyId);
                if (lead != null && System.Array.IndexOf(linked, lead) >= 0)
                    foreach (Group group in linked)
                        if (group != lead) leads[group] = lead;
            }
            _scopes = scopes;
            Volatile.Write(ref _leads, leads);
            Volatile.Write(ref _groups, groups);
        }
    }
}
