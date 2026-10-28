using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// A goal standing inside the courtyard of a keep whose closed outer gates the bot may not pass is not offered (in
/// stefanrows' log, 440 of 1,307 give-ups were bots walking at a closed gate). Living Server rule, classic RvR: a bot
/// passes the gates of keeps its own realm owns (GameKeepDoor.TryTraverse), never another realm's. A keep with a
/// broken outer gate is open, and the keep the bot's own force is sieging keeps its targets. Only the courtyard
/// counts, so fighters outside the walls stay targets. Ported from stefanrows/OfflineDAoC (0.314, 0.320).
/// </summary>
public static class AutonomousKeepGateGoals
{
    /// <summary>Used for a keep with no doors yet.</summary>
    public const float FallbackFootprintRadius = 1_300f;
    /// <summary>The courtyard is the farthest door's ring less this, so ground just outside the walls is not "behind a gate".</summary>
    public const float WallMargin = 300f;
    public const float MinimumCourtyardRadius = 600f;
    /// <summary>Gates this close to the farthest gate's ring are outer gates; inner doors don't hold a breached keep.</summary>
    public const float OuterGateBand = 150f;

    private static readonly ConcurrentDictionary<int, float> FootprintByKeep = new();
    private static readonly ConcurrentDictionary<int, float> OuterGateRingByKeep = new();

    /// <summary>Living Server's gate rule: only a keep of the bot's own realm.</summary>
    public static bool CanPass(eRealm botRealm, eRealm keepRealm) => botRealm != eRealm.None && keepRealm == botRealm;

    /// <summary>The goal is held back: a keep the bot may not pass, its outer gate standing, and not the keep it is sieging.</summary>
    public static bool GateBlocks(bool canPassKeep, bool anyGateIntact, bool isSiegedKeep) =>
        !canPassKeep && anyGateIntact && !isSiegedKeep;

    public static bool IsInsideFootprint(float dx, float dy, float radius) => dx * dx + dy * dy <= radius * radius;

    public static float CourtyardRadius(float outerRadius) => Math.Max(MinimumCourtyardRadius, outerRadius - WallMargin);

    public static bool IsOuterGate(float doorDistance, float outerRadius) => doorDistance >= outerRadius - OuterGateBand;

    /// <summary>One door's share of the outer gate ring: its distance if it is a gate (never a postern), else 0.</summary>
    public static float GateRingContribution(float distance, bool isGate) => isGate ? distance : 0f;

    /// <summary>One door holds the courtyard: a closed, standing gate in the outer gate ring.</summary>
    public static bool HoldsCourtyard(float distance, bool isGate, bool closedAndStanding, float outerGateRing) =>
        outerGateRing > 0 && isGate && closedAndStanding && IsOuterGate(distance, outerGateRing);

    /// <summary>The ring of the outer gates: the farthest gate (an attackable door, never a postern), or 0.</summary>
    public static float OuterGateRing(IEnumerable<(float Distance, bool IsGate)> doors)
    {
        float ring = 0;
        foreach ((float distance, bool isGate) in doors)
            ring = Math.Max(ring, GateRingContribution(distance, isGate));
        return ring;
    }

    public static bool AnyOuterGateIntact(IEnumerable<(float Distance, bool IsGate, bool ClosedAndStanding)> doors,
        float outerGateRing)
    {
        foreach ((float distance, bool isGate, bool closed) in doors)
            if (HoldsCourtyard(distance, isGate, closed, outerGateRing))
                return true;
        return false;
    }

    /// <summary>The keep's walls: its farthest door from the centre (cached; doors never move).</summary>
    public static float FootprintRadius(AbstractGameKeep keep)
    {
        if (FootprintByKeep.TryGetValue(keep.KeepID, out float known))
            return known;
        float farthest = 0;
        foreach (GameKeepDoor door in keep.Doors.Values)
            farthest = Math.Max(farthest, DoorDistance(keep, door));
        if (farthest <= 0)
            return FallbackFootprintRadius;
        FootprintByKeep[keep.KeepID] = farthest;
        return farthest;
    }

    private static float OuterGateRing(AbstractGameKeep keep)
    {
        if (OuterGateRingByKeep.TryGetValue(keep.KeepID, out float known))
            return known;
        float ring = 0;
        foreach (GameKeepDoor door in keep.Doors.Values)
            ring = Math.Max(ring, GateRingContribution(DoorDistance(keep, door), door.IsAttackableDoor));
        if (ring > 0)
            OuterGateRingByKeep[keep.KeepID] = ring;
        return ring;
    }

    private static float DoorDistance(AbstractGameKeep keep, GameKeepDoor door) =>
        Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y));

    /// <summary>Iterates the doors only; allocates nothing.</summary>
    private static bool AnyGateIntact(AbstractGameKeep keep)
    {
        float ring = OuterGateRing(keep);
        if (ring <= 0)
            return false;
        foreach (GameKeepDoor door in keep.Doors.Values)
            if (HoldsCourtyard(DoorDistance(keep, door), door.IsAttackableDoor,
                    door.IsAlive && door.State == eDoorState.Closed, ring))
                return true;
        return false;
    }

    /// <summary>
    /// The keep whose courtyard holds the point and whose closed gates a bot of <paramref name="realm"/> may not pass,
    /// or null. <paramref name="keeps"/> are the keeps of the point's region; <paramref name="siegedKeepId"/> is the
    /// "rvr-keep-N" objective the bot's force is attacking, if any.
    /// </summary>
    public static AbstractGameKeep FindBlockingKeep(eRealm realm, IEnumerable<AbstractGameKeep> keeps, float x, float y,
        string siegedKeepId = null)
    {
        if (keeps == null)
            return null;
        foreach (AbstractGameKeep keep in keeps)
        {
            if (keep == null || keep.IsPortalKeep || CanPass(realm, keep.Realm) ||
                !IsInsideFootprint(x - keep.X, y - keep.Y, CourtyardRadius(FootprintRadius(keep))))
                continue;
            bool sieged = siegedKeepId != null && siegedKeepId == $"rvr-keep-{keep.KeepID}";
            if (GateBlocks(false, AnyGateIntact(keep), sieged))
                return keep;
        }
        return null;
    }
}
