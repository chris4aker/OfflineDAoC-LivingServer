using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.ServerRules;

/// <summary>
/// The eight Darkness Falls exit IDs (70-77) each have one destination row per realm.
/// Even a GM must use the row for their character's realm; an ID-only lookup
/// otherwise picks whichever realm happens to be first in the database.
/// The client's zonejump.csv also has 71 (MidBoss) and 77 (LordExit, by Legion's hall), which the released world has no
/// rows for, so clicking them did nothing. Their rows come from <see cref="BuiltInExits"/>, in memory, when the world
/// has none: nothing is written to the world. Found and fixed by rambodolphin13 (DividedRealms 428ccd7d, 2026-11-03, as
/// a world edit); ported 2026-11-08 with his rows, whose targets are the realm homes every other DF exit uses.
/// </summary>
public static class DarknessFallsExitPolicy
{
    public const ushort RegionId = 249;

    public static bool IsExitRequest(ushort currentRegion, ushort zonePointId) =>
        currentRegion == RegionId && zonePointId is >= 70 and <= 77;

    /// <summary>The rows for 71 and 77 the released world lacks (rambodolphin13's, 2026-11-03): one per realm, to its home.</summary>
    public static readonly IReadOnlyList<DbZonePoint> BuiltInExits =
    [
        Exit(71, eRealm.Albion, 16127, 16077, 17772, "DF MIDBOSS EXIT ALB"),
        Exit(71, eRealm.Midgard, 16127, 16077, 17772, "DF MIDBOSS EXIT MID"),
        Exit(71, eRealm.Hibernia, 16127, 16077, 17772, "DF MIDBOSS EXIT HIB"),
        Exit(77, eRealm.Albion, 40446, 44337, 15469, "DF LORD EXIT ALB"),
        Exit(77, eRealm.Midgard, 40446, 44337, 15469, "DF LORD EXIT MID"),
        Exit(77, eRealm.Hibernia, 40446, 44337, 15469, "DF LORD EXIT HIB"),
    ];

    /// <summary>Where each realm's DF exits lead (the same for every exit in the released world).</summary>
    private static DbZonePoint Exit(ushort id, eRealm realm, int sx, int sy, int sz, string name)
    {
        var (region, x, y, z, heading) = realm switch
        {
            eRealm.Albion => ((ushort)1, 599195, 536170, 2960, (ushort)259),
            eRealm.Midgard => ((ushort)100, 760782, 700723, 6292, (ushort)7727),
            _ => ((ushort)200, 324957, 433427, 6317, (ushort)180),
        };
        return new DbZonePoint
        {
            Id = id, Realm = (ushort)realm, SourceRegion = RegionId, SourceX = sx, SourceY = sy, SourceZ = sz,
            TargetRegion = region, TargetX = x, TargetY = y, TargetZ = z, TargetHeading = heading, ClassType = "",
            ObjectId = name,
        };
    }

    /// <summary>The world's row for this exit and realm, or the built-in one when the world has none.</summary>
    public static DbZonePoint ResolveRealmExit(IEnumerable<DbZonePoint> worldRows, eRealm realm, ushort zonePointId) =>
        SelectRealmExit(worldRows, realm, zonePointId) ?? SelectRealmExit(BuiltInExits, realm, zonePointId);

    public static ushort HomeRegion(eRealm realm) => realm switch
    {
        eRealm.Albion => 1,
        eRealm.Midgard => 100,
        eRealm.Hibernia => 200,
        _ => 0,
    };

    public static DbZonePoint SelectRealmExit(IEnumerable<DbZonePoint> rows, eRealm realm, ushort zonePointId)
    {
        ushort home = HomeRegion(realm);
        if (home == 0 || rows == null)
            return null;

        return rows.FirstOrDefault(point => point != null && point.Id == zonePointId &&
            IsExitRequest(point.SourceRegion, point.Id) &&
            point.Realm == (ushort)realm && point.TargetRegion == home);
    }
}
