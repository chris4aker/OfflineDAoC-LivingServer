using System;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The rule for raising a period size (Living Server 1.2.2; docs/RAID-SIZES.md, "Raising a period size"). Owner, 2026-11-22:
/// "If the mechanics and tactics are working but the period raider size from logs isn't cutting it, and adding more bots
/// without it becoming a Zerg wins, we should bump the suggested raider and period size."
/// <para>No encounter is raised. 1.2.2 first raised Summoner's Hall 64 -> 80, Galladoria 100 -> 112 and Tuscaran Glacier
/// 100 -> 120 on their sims; an independent audit of those sims found none of them sound evidence, and the owner withdrew
/// all three (2026-11-23, "Revert all three"). Every encounter has its 2026-11-04 period size.</para>
/// <para>An encounter's period size (<see cref="AutonomousRealmRaid.Definition.SuggestedBots"/>) may be raised only when its
/// simulation shows that its tactics work but it still loses at the period size and wins with more bots, and only on
/// evidence that meets all of these (tightened after the audit, owner 2026-11-23):</para>
/// <list type="bullet">
/// <item>the sim loses the way the game does: half the engaged raiders fallen, or a reset from
/// <see cref="RealmRaidDungeonRoute.ResetWipeLowestPercent"/> or lower (<see cref="RealmRaidDungeonRoute.IsWipe(int, int, int, int)"/>),
/// with real raider deaths;</item>
/// <item>it reproduces at least one PC kill and one PC loss at the sizes those logs show;</item>
/// <item>the control (the rules before the tactic) still loses at the raid's real start (<see cref="ControlStartPercent"/>,
/// 125%) and with the <see cref="AutonomousEpicSpecialForces.MaximumMembers"/> Special Forces on top, not only at 100%;</item>
/// <item>a raid row shared by several encounters is split before one boss's evidence raises it;</item>
/// <item>the evidence is pinned in a C# test (<see cref="Raise.EvidenceTest"/>), not only in Python outside the repo.</item>
/// </list>
/// <para>It goes to the smallest whole-party size that reaches the win balance's target (about two in three), and is not
/// a zerg: at most <see cref="NoZergFactor"/> times the period source's own figure (<see cref="Raise.SourceBots"/>: the
/// 2003-2004 guide's group count times 8, or the players it names), never above the realm's 300
/// (<see cref="RealmRaidRecruitmentPolicy.MaximumBots"/>), and the control wins under a third of its attempts
/// (<see cref="ControlMaxWinShare"/>). Win-rate sizing then works from the raised size as for every raid (125% start,
/// 70-200%). The bosses and their flood guards are never touched.</para>
/// <para>The Herald's records say what their size is a percentage of ("Raid size: 125% of 80."), so a world that ran a
/// build with the withdrawn raises carries on in bots at the period size (<see cref="Rebase"/>).</para>
/// </summary>
public static class RaidPeriodSizes
{
    /// <summary>One raise: the encounter, its period size before (the 2026-11-04 table), the period source's own figure,
    /// the raised size, the evidence, and the C# test class (in DOL.UnitTests) that pins that evidence.</summary>
    public sealed record Raise(string RaidId, int EraBots, int SourceBots, int RaisedBots, string Evidence, string EvidenceTest);

    /// <summary>A raised period size is at most this many times the period source's own figure.</summary>
    public const int NoZergFactor = 2;

    /// <summary>At the raised size the control (the rules before the tactic) must win less often than this share.</summary>
    public const double ControlMaxWinShare = 1.0 / 3;

    /// <summary>The share of attempts the raised size must win in its sim: the win balance's target (67%).</summary>
    public const double TargetWinShare = 0.67;

    /// <summary>The size, as a percentage of the raised size, the control must still lose at: the raid's real start
    /// (<see cref="RealmRaidRecruitmentPolicy.StartSizePercentSetting"/>), and that again with the Special Forces.</summary>
    public const int ControlStartPercent = 125;

    /// <summary>The raised period sizes: none. 1.2.2's three raises (Summoner's Hall 80, Galladoria 112, Tuscaran Glacier
    /// 120) were withdrawn on audit (owner, 2026-11-23, "Revert all three"); the Green Knight's raise to 80 was withdrawn
    /// before (2026-11-22, "option 1"). docs/RAID-SIZES.md lists each withdrawn raise and why.</summary>
    public static readonly Raise[] Raises = [];

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
    /// today's period size, so the next raid carries on from the same number of bots. A row a 1.2.2 test build wrote at a
    /// withdrawn raise ("125% of 80" at Summoner's Hall, now 64) reads as the same 100 bots: 156.25% of 64. Null stays null.
    /// </summary>
    public static double? Rebase(string raidId, double? percent, int? sizeOf)
    {
        if (percent is not double p) return null;
        int now = PeriodSize(raidId);
        int of = sizeOf is int s && s > 0 ? s : Of(raidId)?.EraBots ?? now;
        return now <= 0 || of <= 0 || of == now ? p : p * of / now;
    }
}
