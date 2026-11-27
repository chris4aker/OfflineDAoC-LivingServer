using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// #569 step 4: autonomous gamebot party meetups stand BESIDE a merchant, trainer,
/// stable master, healer, vault keeper or the Realm Exchange broker, not on it, so the player can still click the NPC.
/// Every meetup chooser that anchored on such an NPC (the town near the camp, the town where the leader stands, the
/// nearby town NPCs and the recovery regroup) first tries a clear spot 300-450 units off; the old choice stays as the
/// fallback when no clear spot fits. Once per party formation, under the coordinator's lock; nothing per tick.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    // Every member must get a formation slot on the same floor and, when already in the region, a connected way to
    // it: the same test the meetup choosers already make. Checked once while a party forms, never per tick.
    private static bool ServiceMeetupSlotsFit(IPathfindingMgr nav, Region region, Zone zone, Vector3 chosen, GameBot[] members)
    {
        bool tight = zone?.IsDungeon == true;
        foreach (GameBot member in members)
        {
            if (RendezvousOverBudget()) return false;
            Vector3 desired = DesiredFormationPoint(member, chosen, tight);
            if (!AutonomousRendezvousNavigation.TryResolveFormationSlot(nav, zone, chosen, desired, out Vector3 slot) ||
                member.CurrentRegion == region && !AutonomousRendezvousNavigation.CanReachFormationSlot(nav, region,
                    member.CurrentZone, zone, new(member.X, member.Y, member.Z), slot))
                return false;
        }
        return true;
    }

    /// <summary>The start angle of the clear-spot rings, from the leader's id, so two parties at one NPC spread out.</summary>
    public static int ServiceMeetupSeed(long leaderId) => unchecked((int)(leaderId * 2654435761L >> 7));

    /// <summary>
    /// A meetup centre 300-450 units from <paramref name="npc"/>, 300 from any other service NPC close by (one radius
    /// query) and inside the NPC's named town when it has one (a spot outside it is the wilderness).
    /// </summary>
    private static bool TryChooseBesideService(IPathfindingMgr nav, Zone zone, GameNPC npc, int seed, out Vector3 chosen)
    {
        chosen = default;
        if (npc == null || zone == null)
            return false;
        List<Vector3> services = AutonomousServiceNpcClearance.ServicePositionsNear(npc,
            (ushort)(AutonomousServiceNpcClearance.MeetOuterRadius + AutonomousServiceNpcClearance.MeetClearRadius));
        AbstractArea town = NamedTownArea(npc);
        return AutonomousRendezvousNavigation.TryChooseClearPoint(nav, zone, new(npc.X, npc.Y, npc.Z), seed,
            candidate => AutonomousServiceNpcClearance.IsClearOf(candidate, services, AutonomousServiceNpcClearance.MeetClearRadius) &&
                         (town == null || town.IsContaining((int)candidate.X, (int)candidate.Y, (int)candidate.Z, false)),
            out chosen);
    }

    /// <summary>The nearest active service NPC within <paramref name="radius"/> of <paramref name="near"/>, or null.</summary>
    private static GameNPC NearestServiceNpc(GameObject near, ushort radius)
    {
        if (near?.CurrentRegion == null)
            return null;
        GameNPC best = null;
        long bestDistance = long.MaxValue;
        foreach (GameNPC npc in near.GetNPCsInRadius(radius))
        {
            if (npc.ObjectState != GameObject.eObjectState.Active || !AutonomousServiceNpcClearance.IsServiceNpc(npc))
                continue;
            long distance = (long)(npc.X - near.X) * (npc.X - near.X) + (long)(npc.Y - near.Y) * (npc.Y - near.Y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = npc;
        }
        return best;
    }

    /// <summary>
    /// A meetup beside the service NPC <paramref name="npc"/> where every member fits, or false (the caller then keeps
    /// its old choice). <paramref name="where"/> names the chooser in the log row.
    /// </summary>
    private static bool TryChooseMeetupBesideService(IPathfindingMgr nav, Region region, GameNPC npc, GameBot leader,
        GameBot[] members, string where, out Vector3 point)
    {
        point = default;
        if (npc == null || region == null || npc.CurrentRegion != region || !AutonomousServiceNpcClearance.IsServiceNpc(npc))
            return false;
        Zone zone = region.GetZone(npc.X, npc.Y);
        if (!TryChooseBesideService(nav, zone, npc, ServiceMeetupSeed(leader?.DatabaseID ?? 0), out Vector3 chosen) ||
            !ServiceMeetupSlotsFit(nav, region, zone, chosen, members))
            return false;
        point = chosen;
        // Once per party formation (or regroup): where the meetup went and how far it stands from the NPC.
        Log.Info($"AUTONOMOUS_GROUP_MEETUP_CLEAR_OF_SERVICE leader={leader?.DatabaseID ?? 0} where={where} " +
                 $"kind={AutonomousServiceNpcClearance.KindOf(npc)} region={region.ID} " +
                 $"distance={(int)Vector2.Distance(new(npc.X, npc.Y), new(chosen.X, chosen.Y))} members={members?.Length ?? 0}");
        return true;
    }

    /// <summary>
    /// The town-near-camp chooser's point at <paramref name="npc"/>: beside it when a clear spot fits every member, or
    /// the old choice around it.
    /// </summary>
    private static bool TryChooseMeetupAtTownNpc(IPathfindingMgr nav, Region region, Zone zone, GameNPC npc,
        GameBot leader, GameBot[] members, out Vector3 chosen) =>
        TryChooseMeetupBesideService(nav, region, npc, leader, members, "town_near_camp", out chosen) ||
        AutonomousRendezvousNavigation.TryChoosePoint(nav, zone, new(npc.X, npc.Y, npc.Z), out chosen);
}
