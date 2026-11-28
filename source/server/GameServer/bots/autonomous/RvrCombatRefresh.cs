using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>One enemy the in-combat refresh may look at: its place in the caller's list, its distance, and whether it needs no ray.</summary>
public readonly record struct RvrRefreshCandidate(int Index, double Distance, bool FreeSight);

/// <summary>
/// #529 step 3 (piece B): the light in-combat contact refresh. A bot in a fight skips its frontier scan
/// (TryEngageFrontierThreat returns at once), so before this a mid-fight enemy became a contact only by hitting the bot or
/// by a mate's call, and a fight board read from contacts would have refused real targets. Every
/// <see cref="IntervalMilliseconds"/> a fighting autonomous gamebot looks around once: the enemies within
/// <see cref="Radius"/>, at most <see cref="MaxRays"/> line-of-sight rays, through the same notice rule as the scan
/// (AutonomousRvrPerception: within 1,000 for sure, further by chance). It writes contacts only: it never picks a target
/// and never engages. Its cost is counted in RVR_SCAN_BUDGET (combatRefreshes, combatRays) and timed under the AI_PROFILE
/// name <see cref="ProfileName"/>. The rule is pure; the controller and the rehearsal's scan read the world for it.
/// </summary>
public static class RvrCombatRefresh
{
    /// <summary>How often a fighting bot looks around (the out-of-combat scan looks every 1-1.25 s).</summary>
    public const int IntervalMilliseconds = 2_000;
    /// <summary>How far it looks: a spotter's call radius, past the casters standing 250-700 behind their tanks.</summary>
    public const int Radius = AutonomousRvrPerception.SpotterRadius;
    /// <summary>The most rays one look casts (an enemy firing on the realm needs none).</summary>
    public const int MaxRays = 4;
    /// <summary>Of the rays, this many go to the nearest enemies; the rest go round the others, a few each look.</summary>
    public const int NearestRays = 2;
    /// <summary>The AI_PROFILE name the refresh is timed under.</summary>
    public const string ProfileName = "frontier.rvr_combat_refresh";
    /// <summary>The height of the scan's line-of-sight ray above the feet, at both ends (RvrSightRay).</summary>
    public const int EyeHeight = 48;
    /// <summary>RVR_NOTICE's look= value: the scan's notice, or this refresh's (the notice-time table reads scan only).</summary>
    public const string ScanLook = "scan", CombatLook = "combat";

    /// <summary>RVR_NOTICE's look= value for a notice from the in-combat refresh or from the scan. Pure.</summary>
    public static string Look(bool combat) => combat ? CombatLook : ScanLook;

    /// <summary>When this bot looks next: one interval on, spread by its id so a group does not look on one tick. Pure.</summary>
    public static long NextDue(long now, long botId) => now + IntervalMilliseconds + Math.Abs(botId % 250);

    /// <summary>
    /// Which candidates this look takes, by their Index: every one that needs no ray, then up to <paramref name="maxRays"/>
    /// that need one: the <see cref="NearestRays"/> nearest, then the others in turn from <paramref name="turn"/> (the
    /// bot's count of looks), so an enemy caster standing behind the front is looked at every few looks even while
    /// closer enemies fill the first rays. Ties go by Index. Pure.
    /// </summary>
    public static List<int> Pick(IReadOnlyList<RvrRefreshCandidate> candidates, int turn, int maxRays = MaxRays)
    {
        var picked = new List<int>();
        if (candidates == null || candidates.Count == 0)
            return picked;
        var needRay = new List<RvrRefreshCandidate>(candidates.Count);
        foreach (RvrRefreshCandidate candidate in candidates)
        {
            if (candidate.FreeSight)
                picked.Add(candidate.Index);
            else
                needRay.Add(candidate);
        }
        if (maxRays <= 0 || needRay.Count == 0)
            return picked;
        needRay.Sort((a, b) =>
        {
            int c = a.Distance.CompareTo(b.Distance);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        int nearest = Math.Min(Math.Min(NearestRays, maxRays), needRay.Count);
        for (int i = 0; i < nearest; i++)
            picked.Add(needRay[i].Index);
        int rest = needRay.Count - nearest;
        int more = Math.Min(maxRays - nearest, rest);
        if (more <= 0)
            return picked;
        int start = (int)((uint)Math.Max(0, turn) * (uint)more % (uint)rest);
        for (int i = 0; i < more; i++)
            picked.Add(needRay[nearest + (start + i) % rest].Index);
        return picked;
    }

    /// <summary>How many rays a look with these candidates casts at most (for the budget and its test). Pure.</summary>
    public static int RaysAtMost(IReadOnlyList<RvrRefreshCandidate> candidates, int maxRays = MaxRays)
    {
        int needRay = 0;
        if (candidates != null)
            foreach (RvrRefreshCandidate candidate in candidates)
                if (!candidate.FreeSight)
                    needRay++;
        return Math.Min(Math.Max(0, maxRays), needRay);
    }
}
