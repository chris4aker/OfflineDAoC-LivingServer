using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // Automatic expeditions may recruit waiting level-50 Group PvE applicants,
    // never interrupt an existing ordinary party, solo goal or RvR assignment.
    public static GameBot[] PlanWaitingRaidParty(eRealm realm, int size)
    {
        var candidates = AutonomousBotRegistry.Snapshot().Where(b => AutonomousRealmRaid.IsEligible(b) &&
            b.Realm == realm && b.Group == null && b.IsAlive && b.PersistentRecord != null &&
            b.CurrentRegion != null && !b.InCombat && !b.IsAttacking && !b.IsOnStableMasterRoute &&
            !AutonomousRealmRaid.IsReserved(b) &&
            AutonomousRealmRaid.MayAnswerAutomaticRaid(b))
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        if (size == 4) return candidates.Length >= 4 ? candidates.Take(4).ToArray() : null;
        if (size != 8) return null;
        foreach (GameBot leader in candidates.Take(8))
            if (TryBuildPveRoster(leader, candidates.Where(b => b != leader).ToArray(), out var others, out _))
                return new[] { leader }.Concat(others).ToArray();
        return null;
    }

    // Pure roster planning: no objective, group or position is changed here.
    // Never break a player-led or mixed-level party to obtain a level-50 member.
    // Forced raids recruit the level-50 bots closest to the rally hub first (same region,
    // straight-line distance), then bots in other regions, so the 300 arrive sooner and more
    // consistently than a random pick from the whole realm.
    public const int ForcedRecruitWindow = 64;

    public static double ForcedRecruitDistance(ushort botRegion, float x, float y, ushort hubRegion, Vector3 hub) =>
        botRegion == hubRegion ? Math.Sqrt((x - hub.X) * (double)(x - hub.X) + (y - hub.Y) * (double)(y - hub.Y)) : double.MaxValue;

    public static GameBot[][] PlanForcedRaid(eRealm realm, ushort hubRegion, Vector3 hubCenter)
    {
        var candidates = AutonomousBotRegistry.Snapshot().Where(b => AutonomousRealmRaid.IsEligible(b) &&
            b.Realm == realm && b.PersistentRecord != null && b.CurrentRegion != null &&
            !AutonomousRealmRaid.IsReserved(b) && AutonomousRealmRaid.GetView(b.Group) == null &&
            !AutonomousRvrEventLayer.IsForceCommitted(RvrForceId(b), GameLoop.GameLoopTime) &&
            (b.Group == null || b.Group.GetMembersInTheGroup().All(m => m is GameBot other && AutonomousRealmRaid.IsEligible(other))))
            .OrderBy(b => ForcedRecruitDistance(b.CurrentRegionID, b.X, b.Y, hubRegion, hubCenter))
            .ThenBy(b => b.Group == null ? 0 : 1).ThenBy(_ => Random.Shared.Next()).ToList();
        var parties = new List<GameBot[]>();
        while (candidates.Count >= 8 && parties.Sum(p => p.Length) + 8 <= RealmRaidRecruitmentPolicy.MaximumBots)
        {
            GameBot[] chosen = null;
            // The roster builder shuffles its pool, so offer it the closest bots first
            // and the whole realm only if they cannot fill the eight roles.
            foreach (GameBot[] pool in new[] { candidates.Take(ForcedRecruitWindow).ToArray(), candidates.ToArray() })
            {
                foreach (GameBot leader in pool.Take(8))
                    if (TryBuildPveRoster(leader, pool.Where(b => b != leader).ToArray(), out var others, out _))
                    { chosen = new[] { leader }.Concat(others).ToArray(); break; }
                if (chosen != null) break;
            }
            if (chosen == null) break;
            parties.Add(chosen);
            foreach (var bot in chosen) candidates.Remove(bot);
        }
        int remaining = RealmRaidRecruitmentPolicy.MaximumBots - parties.Sum(p => p.Length);
        if (remaining == 4 && candidates.Count >= 4) parties.Add(candidates.Take(4).ToArray());
        return parties.ToArray();
    }

    /// <summary>
    /// The next forced-raid party from the free reserved bots (already in closest-first order):
    /// a full eight with the usual roles, or the final four when only four reserved bots remain.
    /// </summary>
    public static GameBot[] ComposeForcedParty(GameBot[] free, int reservedRemaining)
    {
        if (free == null || free.Length == 0) return null;
        if (free.Length >= 8)
        {
            GameBot[] window = free.Take(ForcedRecruitWindow).ToArray();
            foreach (GameBot[] pool in new[] { window, free })
                foreach (GameBot leader in pool.Take(8))
                    if (TryBuildPveRoster(leader, pool.Where(b => b != leader).ToArray(), out var others, out _))
                        return new[] { leader }.Concat(others).ToArray();
        }
        return ForcedPartyCanUseFinalFour(free.Length, reservedRemaining) ? free.Take(4).ToArray() : null;
    }

    public static bool ForcedPartyCanUseFinalFour(int free, int reservedRemaining) => reservedRemaining == 4 && free == 4;

    private static bool TryAssignExpeditionRoles(GameBot[] members, GameBot leader, out Dictionary<long, BotPveGroupRole> roles)
    {
        if (TryAssignExactPveRoles(members, leader, out roles)) return true;
        roles = new();
        // Only the final four reinforcement slots use a smaller party, with
        // cross-party support. Ordinary PvE still requires all eight roles.
        if (members.Length != 4 || members.Any(b => !AutonomousRealmRaid.IsEligible(b))) return false;
        foreach (var b in members)
            roles[MemberKey(b)] = Enum.GetValues<BotPveGroupRole>().First(r => BotPartyRoles.CanFill((eCharacterClass)b.CharacterClass.ID, r));
        return true;
    }

    /// <summary>One would-be member of a forced or automatic raid party, as the pure rule below sees it.</summary>
    public readonly record struct ForcedRaidMember(bool Eligible, bool Alive, bool Reserved, bool InCombat, bool InGroup,
        bool MayAnswerAutomaticRaid, bool SpecialForce);

    /// <summary>
    /// Why a set of bots cannot be formed into a raid party, or null when it can (before the raid's post is checked).
    /// The Epic Special Forces answer a call: they are made for the raid and need no waiting-applicant objective.
    /// </summary>
    public static string ForcedRaidPartyRefusal(IReadOnlyList<ForcedRaidMember> members, bool rolesAssigned, bool forcedRaid)
    {
        if (members == null || members.Count is not (4 or 8)) return $"the party has {members?.Count ?? 0} bots, not 4 or 8";
        if (members.Any(m => !m.Eligible)) return "a member is not an eligible level 50";
        if (members.Any(m => !m.Alive)) return "a member is dead";
        if (members.Any(m => !m.Reserved)) return "a member is not reserved for this raid";
        if (members.Any(m => m.InCombat)) return "a member is in combat";
        if (!rolesAssigned) return "the classes do not fill an expedition party's roles";
        if (!forcedRaid && members.Any(m => m.InGroup)) return "a member already has a party";
        if (!forcedRaid && members.Any(m => !m.SpecialForce && !m.MayAnswerAutomaticRaid)) return "a member may not answer an automatic raid";
        return null;
    }

    public static bool FormForcedRaidParty(string eventId, GameBot[] members) => FormForcedRaidParty(eventId, members, out _);

    /// <summary>Forms a reserved roster into a raid party, or says why not (REALM_RAID_SPECIAL_FORCES_FAILED names it).</summary>
    public static bool FormForcedRaidParty(string eventId, GameBot[] members, out string refusal)
    {
        members ??= [];
        bool forced = AutonomousRealmRaid.IsForcedExpedition(eventId);
        bool specialForces = members.Length > 0 && members.All(b => b.IsEpicSpecialForce);
        ForcedRaidMember Describe(GameBot b) => new(AutonomousRealmRaid.IsEligible(b), b.IsAlive,
            AutonomousRealmRaid.IsReservedFor(b, eventId), b.InCombat, b.Group != null,
            AutonomousRealmRaid.MayAnswerAutomaticRaid(b), b.IsEpicSpecialForce);
        bool roles = members.Length > 0 && TryAssignExpeditionRoles(members, members[0], out _);
        if ((refusal = ForcedRaidPartyRefusal(members.Select(Describe).ToArray(), roles, forced)) != null) return false;
        // Validate the raid post before cancelling anybody's work.
        if (!AutonomousRealmRaid.TryGetForcedStaging(eventId, specialForces, members.Length, out SharedCamp staging, out refusal)) return false;
        lock (Sync)
        {
            // Reservations normally exclude these applicants from matchmaking.
            // Still fail closed if one acquired a party before this commit.
            if ((refusal = ForcedRaidPartyRefusal(members.Select(Describe).ToArray(), true, forced)) != null) return false;
            foreach (Group previous in members.Select(b => b.Group).Where(g => g != null).Distinct().ToArray())
            {
                if (AutonomousRealmRaid.GetView(previous) != null) { refusal = "a member's party is already in a raid"; return false; }
                if (previous.GetMembersInTheGroup().OfType<GameBot>().Any(b =>
                    AutonomousRvrEventLayer.IsForceCommitted(RvrForceId(b), GameLoop.GameLoopTime)))
                { refusal = "a member's party is committed to an RvR force"; return false; }
                if (previous.GetMembersInTheGroup().Any(m => m is not GameBot bot || !AutonomousRealmRaid.IsEligible(bot) || bot.InCombat))
                { refusal = "a member's party has a player, an ineligible member or a member in combat"; return false; }
            }
            foreach (Group previous in members.Select(b => b.Group).Where(g => g != null).Distinct().ToArray())
            {
                if (Sessions.TryGetValue(previous, out var old)) FinishGroupTask(old, "Reassigned by a forced realm expedition");
                else previous.DisbandGroup();
            }
            var group = new Group(members[0]);
            GroupMgr.AddGroup(group);
            foreach (GameBot bot in members) group.AddMember(bot);
            Session session = group.MemberCount == members.Length ? NewSession(group, members[0], members.Length, eAutonomousObjectiveKind.GroupPve, staging) : null;
            if (session == null) refusal = $"the group took {group.MemberCount} of {members.Length} members";
            if (session == null || !AutonomousRealmRaid.CommitForcedParty(eventId, group, out refusal))
            {
                group.DisbandGroup();
                Log.Warn($"REALM_RAID_FORCED_PARTY_RETRY event={eventId} reason=staging-or-group-validation detail=\"{refusal}\" members={string.Join(',', members.Select(b => b.Name))}");
                return false;
            }
            Sessions[group] = session;
            foreach (GameBot bot in members)
            {
                AutonomousRvrEventLayer.RemoveForce($"rvr-{bot.DatabaseID}");
                AutonomousObjectiveAssignments.AssignForcedRaid(bot, eventId, forced);
            }
            WriteSessionMetadata(session, members);
            Log.Info($"REALM_RAID_FORCED_PARTY event={eventId} source={(forced ? "forced" : "automatic-waiting-applicants")} group={session.Id} size={members.Length} members={string.Join(',', members.Select(b => b.Name))}");
            return true;
        }
    }

    /// <summary>
    /// Fills a gap in a raid party with one free level-50 bot of the realm, closest to the rally
    /// hub first, preferring a class that can take the released member's role. Forced raids
    /// may take any free bot that is not in a group; automatic raids only take waiting group
    /// applicants, as when they recruit. A released bot is never taken back. No teleport: the
    /// newcomer walks to the party like a late member.
    /// </summary>
    public static bool TryRefillRaidParty(string eventId, Group group, ushort hubRegion, Vector3 hubCenter,
        bool forced, long[] released)
    {
        if (group == null) return false;
        lock (Sync)
        {
            if (!Sessions.TryGetValue(group, out Session session) || session.Ending ||
                session.RaidMusterEvent == null && AutonomousRealmRaid.GetView(group) == null) return false;
            GameBot[] members = BotMembers(group);
            if (members.Length == 0 || members.Length >= 8) return false;
            eRealm realm = members[0].Realm;
            var releasedIds = released.ToHashSet();
            GameBot[] candidates = AutonomousBotRegistry.Snapshot().Where(b => AutonomousRealmRaid.IsEligible(b) &&
                    b.Realm == realm && b.Group == null && b.IsAlive && b.PersistentRecord != null &&
                    b.CurrentRegion != null && !b.InCombat && !b.IsAttacking && !releasedIds.Contains(b.DatabaseID) &&
                    !AutonomousRealmRaid.IsReserved(b) &&
                    !AutonomousRvrEventLayer.IsForceCommitted(RvrForceId(b), GameLoop.GameLoopTime) &&
                    (forced || AutonomousRealmRaid.MayAnswerAutomaticRaid(b)))
                .OrderBy(b => ForcedRecruitDistance(b.CurrentRegionID, b.X, b.Y, hubRegion, hubCenter))
                .Take(ForcedRecruitWindow).ToArray();
            if (candidates.Length == 0) return false;
            BotPveGroupRole? wanted = session.ReleasedRaidRoles.Count > 0 ? session.ReleasedRaidRoles[0] : null;
            GameBot chosen = wanted is BotPveGroupRole role
                ? candidates.FirstOrDefault(b => BotPartyRoles.CanFill((eCharacterClass)b.CharacterClass.ID, role)) ?? candidates[0]
                : candidates[0];
            BotPveGroupRole chosenRole = wanted is BotPveGroupRole w && BotPartyRoles.CanFill((eCharacterClass)chosen.CharacterClass.ID, w)
                ? w : Enum.GetValues<BotPveGroupRole>().First(r => BotPartyRoles.CanFill((eCharacterClass)chosen.CharacterClass.ID, r));
            if (!group.AddMember(chosen)) return false;
            if (!AutonomousRealmRaid.AddReplacement(group, chosen))
            {
                session.ProcessingAttendanceRemovals = true;
                try { group.RemoveMember(chosen, retainSingleRemainingMember: true); }
                finally { session.ProcessingAttendanceRemovals = false; }
                return false;
            }
            if (wanted is BotPveGroupRole filled && filled == chosenRole) session.ReleasedRaidRoles.RemoveAt(0);
            session.PveRoles[MemberKey(chosen)] = chosenRole;
            session.LockedSize = BotMembers(group).Length;
            AutonomousRvrEventLayer.RemoveForce($"rvr-{chosen.DatabaseID}");
            AutonomousObjectiveAssignments.AssignForcedRaid(chosen, eventId, forced);
            WriteSessionMetadata(session, BotMembers(group));
            Log.Info($"REALM_RAID_REPLACEMENT event={eventId} group={session.Id} bot={chosen.Name} role={chosenRole} " +
                $"region={chosen.CurrentRegionID} position={chosen.X},{chosen.Y},{chosen.Z} size={BotMembers(group).Length}");
            return true;
        }
    }
}
