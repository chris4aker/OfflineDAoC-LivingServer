using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// #569 step 4 (ported by hand from True165 78a70c0, d15d72e and 4d888bd, its GitHub #48): autonomous gamebots stood
/// on and around stable masters, merchants, trainers, healers and vault keepers, so the player could not click them.
/// NPCs are not in the navmesh, so nothing kept a bot off them. These rules keep the spots gamebots choose for party
/// meetups, stable-route landings and town breaks clear of the NPCs players use. Every caller decides once (a party
/// forms, a rider lands, a break starts), never per tick. Companions and the player's own movement are untouched.
/// </summary>
public static class AutonomousServiceNpcClearance
{
    /// <summary>A party meetup centre stays at least this far from a service NPC (its formation ring is 64 + 3n
    /// across, so members stand 200+ units clear).</summary>
    public const float MeetClearRadius = 300f;

    /// <summary>The outer edge of the band a meetup centre beside a service NPC is taken from.</summary>
    public const float MeetOuterRadius = 450f;

    /// <summary>A town break spot stays at least this far from a service NPC.</summary>
    public const float TownBreakClearRadius = 250f;

    /// <summary>A landed rider steps off the route end by this much (per bot, from its id).</summary>
    public const float LandingStepMin = 60f;
    public const float LandingStepMax = 140f;

    /// <summary>The landing step stays on the side away from the stable master, within this many degrees.</summary>
    public const int LandingAwayDegrees = 75;

    /// <summary>Service NPCs on another floor (more than this far above or below) do not count.</summary>
    public const float SameFloorHeight = 300f;

    /// <summary>The NPCs players walk up to and click: merchants (stable masters are merchants), trainers,
    /// healers, vault keepers and the Realm Exchange broker. A gamebot is never one.</summary>
    public static bool IsServiceNpc(GameNPC npc) =>
        npc != null && npc is not GameBot &&
        npc is GameMerchant or GameTrainer or GameHealer or GameVaultKeeper or RealmExchangeBroker;

    /// <summary>Pure: true when no position in <paramref name="services"/> lies within <paramref name="radius"/>
    /// (across the ground, ignoring service NPCs on another floor) of <paramref name="point"/>.</summary>
    public static bool IsClearOf(Vector3 point, IEnumerable<Vector3> services, float radius)
    {
        if (services == null)
            return true;
        foreach (Vector3 service in services)
            if (MathF.Abs(service.Z - point.Z) <= SameFloorHeight &&
                Vector2.DistanceSquared(new(point.X, point.Y), new(service.X, service.Y)) < radius * radius)
                return false;
        return true;
    }

    /// <summary>The positions of the active service NPCs among <paramref name="npcs"/> (no radius query of its own).</summary>
    public static List<Vector3> ServicePositions(IEnumerable<GameNPC> npcs)
    {
        var found = new List<Vector3>();
        if (npcs == null)
            return found;
        foreach (GameNPC npc in npcs)
            if (npc?.ObjectState == GameObject.eObjectState.Active && IsServiceNpc(npc))
                found.Add(new(npc.X, npc.Y, npc.Z));
        return found;
    }

    /// <summary>The positions of the service NPCs within <paramref name="radius"/> of <paramref name="near"/>
    /// (one radius query; called once when a party forms or a break spot is chosen).</summary>
    public static List<Vector3> ServicePositionsNear(GameObject near, ushort radius) =>
        near?.CurrentRegion == null ? [] : ServicePositions(near.GetNPCsInRadius(radius));

    /// <summary>
    /// Pure: where a rider who landed at <paramref name="end"/> steps to. Each bot gets its own spot
    /// <see cref="LandingStepMin"/>-<see cref="LandingStepMax"/> from the route end, on the side away from the
    /// stable master (within <see cref="LandingAwayDegrees"/> of straight away), so the riders of one route no longer
    /// share one spot. With no master known the whole circle is used.
    /// </summary>
    public static Vector3 LandingStepOff(Vector3 end, Vector3? master, long botId)
    {
        ulong hash = unchecked((ulong)botId * 2654435761UL);
        float radius = LandingStepMin + (float)(hash % (ulong)(LandingStepMax - LandingStepMin + 1));
        Vector2 away = master.HasValue ? new(end.X - master.Value.X, end.Y - master.Value.Y) : Vector2.Zero;
        float angle = away.LengthSquared() >= 1f
            ? MathF.Atan2(away.Y, away.X) +
              ((float)((hash >> 8) % (ulong)(2 * LandingAwayDegrees + 1)) - LandingAwayDegrees) * MathF.PI / 180f
            : (float)((hash >> 8) % 360UL) * MathF.PI / 180f;
        return new(end.X + MathF.Cos(angle) * radius, end.Y + MathF.Sin(angle) * radius, end.Z);
    }

    /// <summary>
    /// The walkable step-off spot for a landed rider: one floor projection of <see cref="LandingStepOff"/> and one
    /// corridor check back to the landing (no prop tops or islands). False keeps the rider where it landed.
    /// Once per arrival.
    /// </summary>
    public static bool TryResolveLandingStepOff(IPathfindingMgr nav, Zone zone, Vector3 landing, Vector3? master,
        long botId, out Vector3 point)
    {
        point = default;
        if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return false;
        Vector3 desired = LandingStepOff(landing, master, botId);
        if (desired.X < zone.XOffset || desired.X >= zone.XOffset + zone.Width ||
            desired.Y < zone.YOffset || desired.Y >= zone.YOffset + zone.Height)
            return false;
        Vector3? floor = nav.GetClosestPoint(zone, desired, 32, 32, 128, nav.DefaultFilters);
        if (!floor.HasValue || Vector2.Distance(new(floor.Value.X, floor.Value.Y), new(desired.X, desired.Y)) > 48 ||
            MathF.Abs(floor.Value.Z - landing.Z) > 128 ||
            !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, landing, floor.Value))
            return false;
        point = floor.Value;
        return true;
    }

    /// <summary>The kind of service NPC, for log rows (never its name).</summary>
    public static string KindOf(GameNPC npc) => npc switch
    {
        GameStableMaster => "stable",
        GameMerchant => "merchant",
        GameTrainer => "trainer",
        GameHealer => "healer",
        GameVaultKeeper => "vault",
        RealmExchangeBroker => "exchange",
        _ => "other"
    };
}
