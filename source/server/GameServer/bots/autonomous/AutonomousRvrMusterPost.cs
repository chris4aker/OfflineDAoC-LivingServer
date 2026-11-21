using System;
using System.Collections.Concurrent;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The post an RvR siege or defense muster slot holds around its muster centre (portal outpost or keep).
/// Slots are spread on a sunflower around the centre, as before; a slot whose spot has no walk from the centre now takes
/// the nearest spot round the same ring (then a tighter ring) that has one, else the centre's own floor.
/// Owner's PC, 2026-11-19 (build 1.2-43c43aa): at the Hibernian muster in Odin's Gate (100:596055,581400) Miadur 4598,
/// Yvik 4824 and Blotiogg 4677 stood 90-220 short of posts 596917,581210 / 595687,580379 / 595422,580642, gave up three
/// local recoveries each and were sent home to Tir na Nog at 13:58, a minute before the march.
/// Posts are cached per centre and slot (and the zone's mesh revision): every bot of every muster shares one proof.
/// </summary>
public static class AutonomousRvrMusterPost
{
    /// <summary>Alternative spots tried round the ring (then as many on a ring 60% as wide) before the centre's floor.</summary>
    public const int RingAlternatives = 12;

    private static readonly ConcurrentDictionary<(ushort Zone, int X, int Y, int Z, int Slot, long Revision), Vector3> Posts = new();

    /// <summary>The slot's spot as HandleSiegeMuster always placed it: 450 + 120 * sqrt(slot) out, golden angle apart.</summary>
    public static Vector3 RawPost(Vector3 home, int slot, int alternative = 0)
    {
        double angle = slot * 2.39996323 + (alternative % RingAlternatives) * (2 * Math.PI / RingAlternatives);
        float radius = (450 + 120 * MathF.Sqrt(slot)) * (alternative < RingAlternatives ? 1f : 0.6f);
        return home + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
    }

    /// <summary>
    /// True when a walk from the muster centre's floor reaches the post (same zone, a complete Detour corridor).
    /// No navmesh, or a centre with no floor to judge from, passes as before.
    /// </summary>
    public static bool Reachable(IPathfindingMgr nav, Zone zone, Vector3 home, Vector3 post)
    {
        if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return true;
        if (home.X < zone.XOffset || home.X >= zone.XOffset + zone.Width || home.Y < zone.YOffset || home.Y >= zone.YOffset + zone.Height)
            return false;
        Vector3? from = nav.GetClosestPoint(zone, home, 160, 160, 800, nav.DefaultFilters);
        if (!from.HasValue) return true;
        return Vector3.DistanceSquared(from.Value, post) <= 48 * 48 ||
               AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from.Value, post);
    }

    /// <summary>
    /// The slot's post: its usual spot when a walk from the centre reaches it, else the first alternative round the ring
    /// that does, else the centre's own floor (or the centre). Without a navmesh, the usual spot as before.
    /// </summary>
    public static Vector3 Choose(IPathfindingMgr nav, Func<Vector3, Zone> zoneAt, Vector3 home, int slot)
    {
        Zone homeZone = zoneAt(home);
        if (nav == null || homeZone == null || !nav.IsAvailable || !nav.HasNavmesh(homeZone))
        {
            Vector3 raw = RawPost(home, slot);
            Zone zone = zoneAt(raw);
            return zone != null && nav != null && nav.IsAvailable && nav.HasNavmesh(zone)
                ? nav.GetClosestPoint(zone, raw, 160, 160, 800, nav.DefaultFilters) ?? home : raw;
        }
        var key = (homeZone.ID, (int)home.X, (int)home.Y, (int)home.Z, slot, NavigationGeometryRevision.Read(homeZone));
        if (Posts.TryGetValue(key, out Vector3 known)) return known;
        Vector3 chosen = Find(nav, zoneAt, homeZone, home, slot);
        if (Posts.Count > 4_096) Posts.Clear();
        Posts[key] = chosen;
        return chosen;
    }

    private static Vector3 Find(IPathfindingMgr nav, Func<Vector3, Zone> zoneAt, Zone homeZone, Vector3 home, int slot)
    {
        for (int alternative = 0; alternative < RingAlternatives * 2; alternative++)
        {
            Vector3 raw = RawPost(home, slot, alternative);
            Zone zone = zoneAt(raw);
            if (zone != homeZone) continue;
            Vector3? floor = nav.GetClosestPoint(zone, raw, 160, 160, 800, nav.DefaultFilters);
            if (floor.HasValue && Reachable(nav, zone, home, floor.Value)) return floor.Value;
        }
        return nav.GetClosestPoint(homeZone, home, 160, 160, 800, nav.DefaultFilters) ?? home;
    }

    /// <summary>Keep-route failures from one spot before a bot by its realm's arrival point starts again from there.</summary>
    public const int FailuresBeforeRestart = 2;
    /// <summary>How near its realm's arrival point (flat) a failing bot must stand: the muster ring and its surroundings.</summary>
    public const float RestartReach = 4_000;
    /// <summary>A bot this near the arrival point already stands on it: moving it would change nothing.</summary>
    public const float RestartMinimum = 150;

    /// <summary>
    /// Where a bot whose keep route keeps failing ("No connected exterior route") starts again: its realm's arrival point in
    /// this frontier (where the frontier porter puts every bot of the realm, the muster centre, AutonomousFrontierTransport.
    /// Destination), when the bot stands within <see cref="RestartReach"/> of it and has failed
    /// <see cref="FailuresBeforeRestart"/> times. Owner's PC, 2026-11-19 (build 1.2-43c43aa): Poshozois 4911 and
    /// Clumsychampion 5149 (100:596048,582909, 1,500 from the Hibernian arrival in Odin's Gate) and Linaelorbel 3236
    /// (200:476630,343539, 800 from the Albion arrival in Emain Macha) walked toward muster posts no walk reached, ended on
    /// a few square units of mesh with no way off and failed their keep route for 10-17 minutes; the first two were then
    /// sent to Tir na Nog by the 15-minute watchdog. From the arrival point the same keep routes are found. The move is the
    /// porter's own drop, not a new way in: the bot is already in that frontier, by that outpost.
    /// </summary>
    public static bool TryRestartPoint(eRealm realm, ushort region, Vector3 current, int failures, out Vector3 restart)
    {
        restart = default;
        if (failures < FailuresBeforeRestart) return false;
        AutonomousFrontierTransport.Passage passage = AutonomousFrontierTransport.Destination(realm, region);
        if (passage?.Location == null || passage.Region != region) return false;
        var arrival = new Vector3(passage.Location.X, passage.Location.Y, passage.Location.Z);
        // In the realm's own frontier the army musters at its border keep (AutonomousRvrStaging.TryGetSiegeMuster), not at
        // the home porter's drop: restart from the keep's muster centre, the point every muster post is proved from and
        // the army marched from. Owner's PC, 2026-11-19 (build 1.2-aa537ce, 16:37-16:53): Kanira3 and Krudrum, on the
        // walk from Castle Sauvage to Caer Benowyc, were restarted at the home drop 584285,477200 (850 from the keep's
        // centre, near the walled-off floor of run 21) and failed "No connected exterior route" twice again from there,
        // while the army of 81 walked from the muster round 585085,477504.
        if (passage.Medallion == "home_necklace")
        {
            if (!AutonomousRvrStaging.TryGetBorderKeepNear(realm, region, new(current.X, current.Y), out var keep) ||
                keep.RegionId != region) return false;
            arrival = keep.Position;
        }
        float flat = Vector2.Distance(new(current.X, current.Y), new(arrival.X, arrival.Y));
        if (flat > RestartReach || flat <= RestartMinimum) return false;
        restart = arrival;
        return true;
    }

    public static void ResetForTests() => Posts.Clear();
}
