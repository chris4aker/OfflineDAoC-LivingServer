using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Veteran review M11 (groups G12): a gamebot party walks and stands in a role-aware formation. Once the party has its
/// camp, tanks take the front arc toward the camp, melee damage the flanks, and healers and casters the back arc,
/// 200 units behind (Freddyshouse: tanks in front toward the pull, casters and healers behind and spread). The layout
/// is worked out once per camp, member or role change under the coordinator lock and read without it on every
/// formation step (no extra scans). Parties without a camp or roles keep the even ring.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    [ServerProperty("bots", "bot_role_formation",
        "Veteran review M11 (2026-11-11): a gamebot party's formation is role-aware once it has a camp: tanks in front toward the camp, melee on the flanks, healers and casters behind.", true)]
    public static bool RoleFormationEnabled = true;

    public enum FormationPlace : byte { Front, Side, Back }

    /// <summary>How far behind the leader healers and casters stand outdoors (the review's 150-250).</summary>
    public const int BackRowDistance = 200;

    /// <summary>One member's place: its angle (radians) and whether it stands in the back row.</summary>
    public readonly record struct FormationSlot(double Angle, bool Back);

    private sealed record RoleFormationPlan(long Version, Dictionary<long, FormationSlot> Slots);

    private static readonly ConditionalWeakTable<Group, RoleFormationPlan> RoleFormations = new();

    /// <summary>Pure rule: where a member of this role and class stands.</summary>
    public static FormationPlace PlaceFor(BotPveGroupRole role, eCharacterClass characterClass) => role switch
    {
        BotPveGroupRole.Tank => FormationPlace.Front,
        BotPveGroupRole.Healer => FormationPlace.Back,
        _ => AutonomousCampKnowledge.DamagesWithSpells(characterClass) || characterClass is eCharacterClass.Cleric or
            eCharacterClass.Druid or eCharacterClass.Healer or eCharacterClass.Shaman
            ? FormationPlace.Back : FormationPlace.Side,
    };

    /// <summary>Pure rule: count angles spread evenly over center +- halfSpread (one member stands on the center).</summary>
    public static double[] ArcAngles(int count, double center, double halfSpread)
    {
        if (count <= 0)
            return Array.Empty<double>();
        if (count == 1)
            return new[] { center };
        var angles = new double[count];
        for (int i = 0; i < count; i++)
            angles[i] = center - halfSpread + 2 * halfSpread * i / (count - 1);
        return angles;
    }

    /// <summary>
    /// Pure rule: the slots of members in this order. Front: the front +- 30 degrees; sides: alternately left and right
    /// of the front (90 degrees, spread 20 degrees more each pair); back: behind +- 50 degrees.
    /// </summary>
    public static FormationSlot[] RoleSlots(IReadOnlyList<FormationPlace> places, double front)
    {
        var slots = new FormationSlot[places.Count];
        var fronts = new List<int>();
        var sides = new List<int>();
        var backs = new List<int>();
        for (int i = 0; i < places.Count; i++)
            (places[i] == FormationPlace.Front ? fronts : places[i] == FormationPlace.Side ? sides : backs).Add(i);
        double[] frontAngles = ArcAngles(fronts.Count, front, Math.PI / 6);
        for (int i = 0; i < fronts.Count; i++)
            slots[fronts[i]] = new(frontAngles[i], false);
        for (int i = 0; i < sides.Count; i++)
        {
            double side = (i % 2 == 0 ? 1 : -1) * (Math.PI / 2 + i / 2 * (Math.PI / 9));
            slots[sides[i]] = new(front + side, false);
        }
        double[] backAngles = ArcAngles(backs.Count, front + Math.PI, Math.PI * 5 / 18);
        for (int i = 0; i < backs.Count; i++)
            slots[backs[i]] = new(backAngles[i], true);
        return slots;
    }

    /// <summary>Pure rule: the distance of a slot from the leader.</summary>
    public static int SlotRadius(bool back, bool tight, int memberCount)
    {
        int ring = tight ? 48 + memberCount * 2 : 64 + memberCount * 3;
        return back ? tight ? ring + 40 : BackRowDistance : ring;
    }

    // Caller holds Sync. Rebuilds the party's layout when its camp, members or roles changed.
    private static void RefreshRoleFormation(Session session, GameBot[] members)
    {
        if (!RoleFormationEnabled || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
            session.Camp == null || session.PveRoles.Count == 0 || members.Length < 2)
        {
            RoleFormations.Remove(session.Group);
            return;
        }
        long version = RoleMemberKey(members) * 31 + StringComparer.Ordinal.GetHashCode(session.Camp.Id ?? string.Empty);
        foreach (GameBot member in members)
            version = version * 7 + (session.PveRoles.TryGetValue(MemberKey(member), out BotPveGroupRole role) ? (int)role + 1 : 0);
        if (RoleFormations.TryGetValue(session.Group, out RoleFormationPlan existing) && existing.Version == version)
            return;

        // front: from where the party set out (its rendezvous) toward the camp; the leader's spot when that is unknown
        Vector3 camp = new(session.Camp.X, session.Camp.Y, session.Camp.Z);
        Vector3 from = session.RendezvousRegion == session.Camp.RegionId ? session.Rendezvous
            : session.Leader is { } leader ? new Vector3(leader.X, leader.Y, leader.Z) : camp;
        double front = Vector2.DistanceSquared(new(from.X, from.Y), new(camp.X, camp.Y)) > 300f * 300f
            ? Math.Atan2(camp.Y - from.Y, camp.X - from.X)
            : (RuntimeHelpers.GetHashCode(session.Group) & 31) * (Math.PI / 16);
        var places = new FormationPlace[members.Length];
        for (int i = 0; i < members.Length; i++)
            places[i] = PlaceFor(session.PveRoles.TryGetValue(MemberKey(members[i]), out BotPveGroupRole role)
                ? role : BotPveGroupRole.Attacker, (eCharacterClass)members[i].CharacterClass.ID);
        FormationSlot[] slots = RoleSlots(places, front);
        var byMember = new Dictionary<long, FormationSlot>(members.Length);
        for (int i = 0; i < members.Length; i++)
            byMember[MemberKey(members[i])] = slots[i];
        RoleFormations.AddOrUpdate(session.Group, new RoleFormationPlan(version, byMember));
    }

    // Lock-free read on every formation step: the member's role-aware point, or false for the even ring.
    private static bool TryRoleFormationPoint(GameBot bot, Vector3 center, bool tight, out Vector3 point)
    {
        point = default;
        if (!RoleFormationEnabled || bot?.Group == null || !RoleFormations.TryGetValue(bot.Group, out RoleFormationPlan plan) ||
            !plan.Slots.TryGetValue(MemberKey(bot), out FormationSlot slot))
            return false;
        int radius = SlotRadius(slot.Back, tight, plan.Slots.Count);
        point = new(center.X + (float)(Math.Cos(slot.Angle) * radius), center.Y + (float)(Math.Sin(slot.Angle) * radius), center.Z);
        return true;
    }

    /// <summary>An order-independent key of the members (count, ids, alive), so any join, leave or death re-lays the party.</summary>
    private static long RoleMemberKey(IReadOnlyList<GameBot> members)
    {
        long key = members.Count;
        foreach (GameBot member in members)
            key += unchecked((member?.ObjectID ?? 0) * 1_000_003L) ^ (member?.IsAlive == true ? 1L : 2L);
        return key;
    }
}
