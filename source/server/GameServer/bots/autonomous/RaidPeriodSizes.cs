using System;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Raised period sizes (Living Server 1.2.2; docs/RAID-SIZES.md, "Raising a period size"). Owner, 2026-11-22: "If the
/// mechanics and tactics are working but the period raider size from logs isn't cutting it, and adding more bots without
/// it becoming a Zerg wins, we should bump the suggested raider and period size."
/// <para>An encounter's period size (<see cref="AutonomousRealmRaid.Definition.SuggestedBots"/>) is raised only when its
/// simulation shows that its tactics work (the failure mode the sim found is fixed) but it still loses at the period size
/// and wins with more bots. It goes to the smallest whole-party size that reaches the win balance's target (about two in
/// three), within the no-zerg limit, or to the limit when the target lies beyond it. Not a zerg:</para>
/// <list type="bullet">
/// <item>at most <see cref="NoZergFactor"/> times the period source's own figure (<see cref="Raise.SourceBots"/>: the
/// 2003-2004 guide's group count times 8, or the players it names);</item>
/// <item>never above the realm's 300 (<see cref="RealmRaidRecruitmentPolicy.MaximumBots"/>);</item>
/// <item>the extra bots win because of the tactic: at the raised size the rules from before the tactic (the sim's control)
/// still lose, winning under a third of their attempts (<see cref="ControlMaxWinShare"/>).</item>
/// </list>
/// <para>Win-rate sizing then works from the raised size as for every raid (125% start, 70-200%). The bosses and their
/// flood guards are never touched.</para>
/// </summary>
public static class RaidPeriodSizes
{
    /// <summary>One raise: the encounter, its period size before (the 2026-11-04 table), the period source's own figure,
    /// the raised size and the evidence.</summary>
    public sealed record Raise(string RaidId, int EraBots, int SourceBots, int RaisedBots, string Evidence);

    /// <summary>A raised period size is at most this many times the period source's own figure.</summary>
    public const int NoZergFactor = 2;

    /// <summary>At the raised size the control (the rules before the tactic) must win less often than this share.</summary>
    public const double ControlMaxWinShare = 1.0 / 3;

    /// <summary>The share of attempts the raised size must win in its sim: the win balance's target (67%).</summary>
    public const double TargetWinShare = 0.67;

    /// <summary>The raised period sizes (1.2.2). Every other encounter keeps its 2026-11-04 period size. Tests only: each
    /// rests on a simulation (docs/RAID-SIZES.md), none on a PC log.</summary>
    public static readonly Raise[] Raises =
    [
        // Summoner Roesia (#410), wins of 64 with the 1.2.1 fresh-Pain cure at 64 / 80 / 88 / 96 / 104: Midgard
        // 0 / 14 / 61 / 64 / 64, Albion 0 / 8 / 34 / 56 / 61, Hibernia 0 / 16 / 54 / 61 / 64; the 1.2 cure rule 0-1 of 64
        // at every size to 128. The target lies past the limit (2 x 40 = 80): raised to the limit, so the raid starts at
        // 100 (125%), where she wins 56-64 of 64. 1.2.2 real stats (her melee 200 a second on plate, her damage shield 224 a
        // swing, the heal recalibrated to b1b6846; UT_RaidRealStats122): round 2 wins Albion 15, Hibernia 7, Midgard 0 of 64
        // at 80 and 43-64 from 88-112, the 1.2 rule 0-5 of 64 at 80: still raised to the limit, 80.
        new("summoners-albion", 64, 40, 80, "Roesia: 8/64 at 80, 56/64 at 96 (1.2 rule 0/64); 0/64 at 64; four to five groups (Feb 2003)"),
        new("summoners-midgard", 64, 40, 80, "Roesia: 14/64 at 80, 61/64 at 88 (1.2 rule 0/64); 0/64 at 64; four to five groups (Feb 2003)"),
        new("summoners-hibernia", 64, 40, 80, "Roesia: 16/64 at 80, 54/64 at 88 (1.2 rule 0/64); 0/64 at 64; four to five groups (Feb 2003)"),
        // Spindler Broodmother (#408): from 14 parties the 60% share sends all 8 add parties and matches the one known
        // kill (ccba364: 1.93 newborns alive at her death against its 2.49); 13 parties leave 3.41. The 1.2 share (45%)
        // matches it only from 16 parties (128).
        new("epic-hibernia", 100, 100, 112, "Spindler: the ccba364 kill's newborn share from 14 parties (1.2 share only from 16); 100 ppl (May 2003)"),
        // Queen Kula (#409): with no pet on the King her phase is as short as the ccba364 kill's (20.1 min) from 15
        // parties (20.0 min); the 1.2 rule takes 21.4-27.0 min there. King Tuscar's own fight has no model.
        new("epic-midgard", 100, 100, 120, "Kula: 20.0 min at 120, the ccba364 kill's 20.1 (1.2 rule 21.4-27.0); ~100 ppl (May 2003)"),
        // The Green Knight (#412): no raise. 1.2.2 first raised him to 80 on the real stats; the owner withdrew it (2026-11-22,
        // "option 1": back to his period 40, fix the tactics). His fight is the tactic's (EpicEncounterTactics.GreenKnight.cs,
        // tag and burn; UT_RaidSim122GreenKnight), not the size's.
    ];

    public static Raise Of(string raidId) => raidId == null ? null : Raises.FirstOrDefault(r => r.RaidId == raidId);

    /// <summary>The encounter's period size today (its SuggestedBots), or 0 for an unknown raid.</summary>
    public static int PeriodSize(string raidId) =>
        raidId == null ? 0 : AutonomousRealmRaid.Definitions.FirstOrDefault(d => d.Id == raidId)?.SuggestedBots ?? 0;

    /// <summary>The encounter's period size before any raise (the 2026-11-04 table).</summary>
    public static int EraBots(string raidId) => Of(raidId)?.EraBots ?? PeriodSize(raidId);

    /// <summary>The no-zerg limit of a period source's figure: twice it, never above the realm's 300.</summary>
    public static int NoZergLimit(int sourceBots) =>
        Math.Min(RealmRaidRecruitmentPolicy.MaximumBots, Math.Max(0, sourceBots) * NoZergFactor);

    /// <summary>
    /// A recorded raid size carried over to the encounter's period size today: <paramref name="percent"/> of
    /// <paramref name="sizeOf"/> bots (or, when the row does not say, of the period size before a raise) as a percentage of
    /// today's period size, so the next raid carries on from the same number of bots. Null stays null.
    /// </summary>
    public static double? Rebase(string raidId, double? percent, int? sizeOf)
    {
        if (percent is not double p) return null;
        int now = PeriodSize(raidId);
        int of = sizeOf is int s && s > 0 ? s : Of(raidId)?.EraBots ?? now;
        return now <= 0 || of <= 0 || of == now ? p : p * of / now;
    }
}
