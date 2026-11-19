using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Where a raid musters, regroups and fights relative to a boss's tether (Living Server 1.2, pure rules). Past its tether a
/// tethered boss takes no damage (the bots also stop hitting it, AutonomousTetherReset) and its script heals it to full, so
/// a raid standing out there, or close enough to its edge that the boss follows a raider out, can't hurt it.
/// <para>
/// Owner's PC, 2026-11-16 (build ccba364):
/// <list type="bullet">
/// <item>Summoner's Hall, Hibernia: the final muster stood 1,400 from Summoner Roesia's spawn (every final muster stood
/// <see cref="RealmRaidDungeonRoute.FinalMusterStandOff"/> off) with her tether at 1,300 and her fight-at-home radius at
/// 1,000. The raid walked in from past her home, she met it at 1,159 and nobody hit her there (the fight-at-home rule:
/// targeting=0); attempt 1's early pull pulled the raid back to the muster, where she can't be hurt.</item>
/// <item>Tuscaran Glacier: the muster for Queen Kula stood 1,395 from her and 1,381 from King Tuscar (tether 1,550), in her
/// frost circle; the twins went after the raiders there and were taken 2,455 and 2,935 out, and Kula reset to full twice.</item>
/// </list>
/// Now a muster stands inside the reach each boss of the fight can be hurt in (<see cref="DamageReach"/>), outside its aggro
/// where that fits (<see cref="MusterStandOff"/>), and a muster point that lies past a reach is refused like one in another
/// boss's aggro (#213's check, RealmRaidDungeonRoute.PlanMuster).
/// </para>
/// </summary>
public static class RealmRaidTetherReach
{
    /// <summary>A muster keeps this far inside a boss's tether: raiders spread around the point, and the boss walks a
    /// little past the raider it goes after.</summary>
    public const int TetherMargin = 300;
    /// <summary>The muster stands this far inside the reach when the aggro allows it (its parties spread around it).</summary>
    public const float MusterInset = 100f;
    /// <summary>Outside a boss's aggro range by this much (the margin #213 keeps from another boss's aggro).</summary>
    public const float AggroMargin = RealmRaidDungeonRoute.UntargetedBossMargin;

    /// <summary>
    /// How far from its spawn a boss can be fought (pure; 0: no limit): its tether less <see cref="TetherMargin"/>. Past its
    /// tether it takes no damage. (A catalog fight-at-home radius is a tactic inside that reach: past it the raid leads the
    /// boss home, and it is not a limit on where the raid may stand.)
    /// </summary>
    public static int DamageReach(int tetherRange) => tetherRange > 0 ? Math.Max(1, tetherRange - TetherMargin) : 0;

    /// <summary>A raider of a fight with a fight-at-home boss stands this far inside its home radius (and its muster's reach
    /// is this far inside it): EpicEncounterTactics' stand-inside-home rule walks raiders back to it.</summary>
    public const int HomeInset = 200;

    /// <summary>The home cap of <see cref="MusterReach"/> for a boss whose home radius is not inside its damage reach applies only
    /// when it leaves the muster at least this far outside the boss's aggro range.</summary>
    public const int HomeCapAggroClearance = 150;

    /// <summary>
    /// How far from its spawn a boss's muster may stand (pure; 0: no limit): its <see cref="DamageReach"/>, and for a boss
    /// with a catalog fight-at-home radius never past <see cref="HomeInset"/> inside that radius, when that radius lies inside
    /// the damage reach, or otherwise when the capped muster still stands <see cref="HomeCapAggroClearance"/> or more outside
    /// <paramref name="aggroRange"/>.
    /// <para>
    /// Owner's PC, build b1b6846, 2026-11-18 (Summoner's Hall, Albion; S-1): Aidon the Archwizard (tether 1,500, fight at home
    /// 1,100, aggro 600) had his muster at 1,100 from his spawn, his fight-at-home edge; healers and regroupers stood at
    /// 1,100-2,200, he followed them past 1,500 and his script healed him to full eight times, the last one at 1% (14:18:56).
    /// His script's out-of-tether heal is the original anti-kite rule and is unchanged; the raid now stands in his room: his
    /// muster's reach is 900 and the muster stands about 800 from his spawn (<see cref="MusterStandOff"/>).
    /// </para>
    /// <para>
    /// Sweep 6f (same build, Summoner's Hall, Midgard): the summoners' home radius (1,000) is their damage reach (tether 1,300
    /// less 300), so the cap only moved their musters in, from 900 to 700 from the spawn: 100 outside their 600 aggro instead
    /// of 300, which invites the early pull that cost Cunovinda's attempt 1 (raiders arriving into her aggro). Their muster
    /// stays at 900 (as on b1b6846); the stand-inside-home rule still walks their raiders inside 800 in the fight.
    /// </para>
    /// </summary>
    public static int MusterReach(int tetherRange, int fightAtHomeRadius, int aggroRange)
    {
        int reach = DamageReach(tetherRange);
        if (fightAtHomeRadius <= 0) return reach;
        int home = Math.Max(1, fightAtHomeRadius - HomeInset);
        if (reach <= 0) return home;
        if (fightAtHomeRadius < reach) return Math.Min(reach, home);
        float cappedMuster = home > MusterInset ? home - MusterInset : home;
        return cappedMuster >= Math.Max(0, aggroRange) + HomeCapAggroClearance ? Math.Min(reach, home) : reach;
    }

    /// <summary>As <see cref="MusterStandOff(float, int, int)"/> with the reach of <see cref="MusterReach"/>: a boss with a
    /// fight-at-home radius is mustered for inside its home (Aidon: about 800 from his spawn, 200 outside his aggro; the summoners,
    /// whose home is their damage reach: 900, 300 outside it).</summary>
    public static float MusterStandOff(float preferred, int aggroRange, int tetherRange, int fightAtHomeRadius) =>
        MusterStandOff(preferred, aggroRange, MusterReach(tetherRange, fightAtHomeRadius, aggroRange));

    /// <summary>
    /// How far from a boss its muster stands (pure): <paramref name="preferred"/> (the 1,400 every muster stood at), but
    /// never past <paramref name="reach"/> (<see cref="MusterInset"/> inside it where that still clears the aggro), and at
    /// least <paramref name="aggroRange"/> plus <see cref="AggroMargin"/> when the reach leaves room for it. When it doesn't,
    /// the reach (less the inset) wins: a boss that comes to its muster is fought where it can be hurt. No reach: unchanged.
    /// </summary>
    public static float MusterStandOff(float preferred, int aggroRange, int reach)
    {
        if (reach <= 0) return preferred;
        float clear = aggroRange > 0 ? aggroRange + AggroMargin : 0;
        float inside = reach > MusterInset ? reach - MusterInset : reach;
        float standOff = Math.Min(preferred, inside);
        standOff = Math.Max(standOff, clear);
        // Never at the reach's very edge: the walk's point is snapped to the floor, and a floor a little lower or further
        // round lies past it (Galladoria's Earth primal on the PC's zone191.nav: 590.3 of 590, refused).
        return Math.Min(standOff, inside);
    }

    /// <summary>One boss of the fight mustered for: its spawn and the reach it can be hurt in (0: no limit).</summary>
    public readonly record struct BossReach(string Name, Vector3 Spawn, float Reach);

    /// <summary>The bosses whose reach <paramref name="point"/> lies past.</summary>
    public static IEnumerable<BossReach> OutOfReachOf(Vector3 point, IEnumerable<BossReach> reaches) =>
        (reaches ?? []).Where(r => r.Reach > 0 && Vector3.DistanceSquared(point, r.Spawn) > r.Reach * r.Reach);

    /// <summary>A muster at <paramref name="point"/> is refused: it lies past the reach of a boss of the fight.</summary>
    public static bool OutOfReach(Vector3 point, IEnumerable<BossReach> reaches) => OutOfReachOf(point, reaches).Any();

    /// <summary>
    /// A pull-back from a boss that engaged the raid early leaves it behind only past its tether, where it can't be hurt:
    /// owner's PC, 2026-11-16, Summoner Roesia came to the raid at 14 of 44 mustered, the raid pulled back to its muster 1,400
    /// out (tether 1,300) and she killed 50 of it there in a minute. So a raid never pulls back to a rally past a boss's reach
    /// (pure: true when it would); it holds or commits instead (EpicEncounterTactics.ChooseEarlyPull).
    /// </summary>
    public static bool PullBackLeavesReach(int reach, double rallyFromSpawn) => reach > 0 && rallyFromSpawn > reach;

    /// <summary>
    /// A boss of the fight walks into the raid's regroup (pure): the raid is regrouping, the boss is in combat with raiders
    /// and it is within <paramref name="threatRadius"/> (its aggro plus <see cref="AggroMargin"/>) of the regroup point.
    /// Owner's PC, 2026-11-16, Tuscaran Glacier: the twins came to the regroup at 23 of 79 and killed the raid there while it
    /// held (no attempt was open). Now that is answered as an early pull: the raid commits, or the raiders it caught fight it
    /// as an attempt while the rest gather; never fought piecemeal by a raid that waits.
    /// </summary>
    public static bool RegroupThreatened(bool regrouping, bool bossInCombat, int raidersFightingIt, float bossToRegroup, float threatRadius) =>
        regrouping && bossInCombat && raidersFightingIt > 0 && bossToRegroup <= threatRadius;

    /// <summary>Raw points on rings around <paramref name="centre"/> from <paramref name="inner"/> to <paramref name="outer"/>
    /// (every <paramref name="step"/> units, <paramref name="angles"/> to a ring), for a muster when no point of the walk to
    /// the raid is both clear and in reach (Queen Kula's: her frost circle covers the walk at the reach).</summary>
    public static IEnumerable<Vector3> RingCandidates(Vector3 centre, float inner, float outer, float step = 100f, int angles = 24)
    {
        if (outer < inner || step <= 0 || angles <= 0) yield break;
        for (float r = inner; r <= outer + 0.5f; r += step)
            for (int a = 0; a < angles; a++)
            {
                double angle = a * 2 * Math.PI / angles;
                yield return centre + new Vector3((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r, 0);
            }
    }

    /// <summary>The first of <paramref name="candidates"/> nearest <paramref name="preferNear"/> first (the walk's own muster)
    /// that is on the floor (<paramref name="floor"/>) and <paramref name="accept"/>ed; null when none.</summary>
    public static Vector3? NearestAccepted(IEnumerable<Vector3> candidates, Vector3 preferNear, Func<Vector3, Vector3?> floor,
        Func<Vector3, bool> accept)
    {
        foreach (Vector3 raw in (candidates ?? []).OrderBy(c => Vector3.DistanceSquared(c, preferNear)))
            if ((floor == null ? raw : floor(raw)) is Vector3 p && accept(p))
                return p;
        return null;
    }

    // ---- open-world bosses: the staging ring the raid regroups on ----------------------------------------------------

    /// <summary>The staging ring's inner radius for a dragon, and for an open-world boss whose reach it already clears.</summary>
    public const float DefaultStagingInner = 3_600f;
    /// <summary>An open-world boss whose tether, aggro and roam reach further than this (the Green Knight's 10,000) keeps the
    /// default ring: no ring around it is out of its reach.</summary>
    public const float MaximumStagingInner = 6_500f;

    /// <summary>
    /// Where an open-world boss's staging ring (its regroup point) starts (pure): past its tether, aggro and roam plus
    /// <see cref="AggroMargin"/>, so the boss can't follow a raider there without leaving its tether. Owner's PC, 2026-11-16:
    /// Marfoirwort (tether 4,500, aggro 800) came to the raid regrouping on the 3,600 ring and killed 103 of it there in 36
    /// minutes, 8,564 from his spawn. The default ring when that is already clear of it or when no ring is (see
    /// <see cref="MaximumStagingInner"/>).
    /// </summary>
    public static float OpenWorldStagingInner(int tetherRange, int aggroRange, int roamingRange)
    {
        if (tetherRange <= 0) return DefaultStagingInner;
        float reach = tetherRange + Math.Max(0, aggroRange) + Math.Max(0, roamingRange) + AggroMargin;
        return reach <= DefaultStagingInner || reach > MaximumStagingInner ? DefaultStagingInner : reach;
    }

    /// <summary>A monster near the ring (spawn, aggro, roam): a staging post is not placed within its aggro and roam plus
    /// <see cref="AggroMargin"/> (Marfoirwort's roaming neighbours).</summary>
    public readonly record struct RingMonster(Vector3 Spawn, int AggroRange, int RoamingRange);

    /// <summary>A staging post at <paramref name="post"/> is clear of every monster's reach (pure).</summary>
    public static bool ClearOfMonsters(Vector3 post, IEnumerable<RingMonster> monsters) =>
        !(monsters ?? []).Any(m => m.AggroRange > 0 &&
            Vector3.DistanceSquared(post, m.Spawn) < Sq(m.AggroRange + Math.Max(0, m.RoamingRange) + AggroMargin));

    private static float Sq(float v) => v * v;

    /// <summary>
    /// An open-world boss's attempt does not end on "no damage for five minutes" while it is in combat with raiders (pure):
    /// owner's PC, 2026-11-16, Marfoirwort's attempt 1 ended so (lowest 79%) with raiders still on him, the raid regrouped,
    /// and he went on killing them with no attempt open (103 deaths). True: the attempt stays open.
    /// </summary>
    public static bool HoldsAttemptWhileFought(bool openWorld, bool bossInCombat, int raidersAtBoss) =>
        openWorld && bossInCombat && raidersAtBoss > 0;
}
