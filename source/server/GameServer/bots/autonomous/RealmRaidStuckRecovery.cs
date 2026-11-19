using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Where the 15-minute watchdog puts a raid member that stood still inside its raid's dungeon (Living Server 1.2, build 4;
/// AutonomousRealmRaid.TryRecoverInsideRaid). Owner's PC, 2026-11-11 (build e90bf21, Krondon): 26 such recoveries in the
/// entrance ogre camp sent raiders to Camelot; each walked back for half an hour into the same spot. Now it joins the
/// raid: where the living raider nearest the route's front stands (ground the raid has walked), or the front when
/// nobody stands within <see cref="FrontReach"/> of it.
/// </summary>
public static class RealmRaidStuckRecovery
{
    /// <summary>A raider this close to the front stands with the raid.</summary>
    public const float FrontReach = 2_000f;
    /// <summary>A stuck bot already this close to where it would go is not moved (it is not lost; the watchdog's capital
    /// recovery decides as before).</summary>
    public const float AlreadyThere = 300f;
    /// <summary>The spot (pure): where the raider nearest the front within <see cref="FrontReach"/> stands, else the front;
    /// <paramref name="index"/> is that raider's index, or -1 for the front. False when the bot is already there.</summary>
    public static bool Choose(Vector3 stuck, Vector3 front, IReadOnlyList<Vector3> raiders, out Vector3 spot, out int index) =>
        Choose(stuck, front, raiders, null, out spot, out index);

    /// <summary>As above, from the raid's regroup point (RealmRaidDungeonRoute.TryStrandedRegroupPoint: the hold point while it
    /// holds, the front while it goes in); a raider standing in a resting boss's aggro (<paramref name="inDanger"/>) is not
    /// where the stuck one goes (re-review of 06b9cb2c0).</summary>
    public static bool Choose(Vector3 stuck, Vector3 front, IReadOnlyList<Vector3> raiders, Func<Vector3, bool> inDanger,
        out Vector3 spot, out int index)
    {
        index = -1;
        float best = FrontReach;
        for (int i = 0; i < (raiders?.Count ?? 0); i++)
        {
            float d = Vector3.Distance(raiders[i], front);
            if (d <= best && inDanger?.Invoke(raiders[i]) != true) { best = d; index = i; }
        }
        spot = index >= 0 ? raiders[index] : front;
        if (!float.IsFinite(spot.X) || !float.IsFinite(spot.Y) || !float.IsFinite(spot.Z) || spot == default) return false;
        return Vector3.Distance(stuck, spot) > AlreadyThere;
    }

    /// <summary>A raid member inside its started expedition's dungeon whose regroup is refused (every point by a resting boss)
    /// stays where it is: neither the pocket escape nor the 15-minute watchdog sends it to its capital (pure; review of
    /// 27abe6e78, M1; run 9's Darkness Falls ride-back loop).</summary>
    public static bool StaysInside(bool inExpeditionDungeon, bool regroupClear) => inExpeditionDungeon && !regroupClear;
}
