using System.Globalization;
using System.Text;
using DAoCLivingServer.Configuration;

namespace DOL.GS;

/// <summary>
/// The goal rows (#669, #670). Logging only, like the other Living rows: no game state is written and no random number is
/// drawn. No bot or player names, and no file path (a path can hold the Windows user name).
/// <list type="bullet">
/// <item><c>BOT_GOALS source= reason= l1_19=S/G/R/B l20_49=S/G/R/B l50=S/G/R/B</c>: once at start-up (reason=start), and once
/// more if the 1.2.3 settings upgrade rewrote an untouched bot-goals.json (reason=upgrade). <c>source</c> is file (the
/// server's bot-goals.json), override (the test override, OFFLINE_DAOC_BOT_GOALS) or defaults (no file: the launcher's
/// default shares). The shares are the ones every task break and pass then use.</item>
/// <item><c>GOAL_PASS t= realm= band= members= pool= bgEligible= target=S/G/R/B held=S/G/R/B open=S/G/R/B
/// planned=S/G/R/B personalities=</c>: the one-minute goal pass, one row per realm and level band (0: 1-19, 20: 20-49,
/// 50) on at most one pass every five minutes (at most 9 rows per five minutes, 108 an hour). <c>members</c> are the
/// band's bots in the pass; <c>pool</c> the ones the pass gave a goal to choose (no live task, no group); <c>bgEligible</c>
/// those of the pool whose level fits a battleground bracket. <c>target</c> is the band's goal shares as headcounts,
/// <c>held</c> the live tasks it counted, <c>open</c> = target - held, and <c>planned</c> the goals the pass gave the pool
/// (as stored after the goal rules).</item>
/// </list>
/// source/tools/living_report_sections/bot_goals.py reads them into the morning report.
/// </summary>
public static class BotGoalRows
{
    public const string StartupMarker = "BOT_GOALS";
    public const string PassMarker = "GOAL_PASS";
    public const long PassIntervalMilliseconds = 5 * 60_000;
    public const int PassRowsPerMinute = 12;

    private static long _nextPassTick;

    /// <summary>The start-up row. Pure.</summary>
    public static string Startup(string source, BotGoalSettings settings, string reason)
    {
        settings ??= BotGoalSettings.Defaults;
        return $"{StartupMarker} source={source} reason={reason} l1_19={Weights(settings.Levels1To19)} " +
               $"l20_49={Weights(settings.Levels20To49)} l50={Weights(settings.Level50)}";
    }

    /// <summary>Whether this pass writes its rows (one pass in five minutes). Not pure: it moves the clock.</summary>
    public static bool PassDue(long now)
    {
        if (!LivingRows.Enabled || now < _nextPassTick)
            return false;
        _nextPassTick = now + PassIntervalMilliseconds;
        return true;
    }

    /// <summary>Tests: the next pass writes its rows again.</summary>
    public static void ResetForTest() => _nextPassTick = 0;

    /// <summary>One pass row. Pure.</summary>
    public static string Pass(long now, eRealm realm, int band, int members, int pool, int battlegroundEligible,
        AutonomousObjectiveAssignments.Allocation target, AutonomousObjectiveAssignments.Allocation held,
        AutonomousObjectiveAssignments.Allocation planned, bool personalities)
    {
        var open = new AutonomousObjectiveAssignments.Allocation(System.Math.Max(0, target.SoloPve - held.SoloPve),
            System.Math.Max(0, target.GroupPve - held.GroupPve), System.Math.Max(0, target.RvR - held.RvR),
            System.Math.Max(0, target.Battlegrounds - held.Battlegrounds));
        var row = new StringBuilder(PassMarker, 220);
        row.Append(" t=").Append(now.ToString(CultureInfo.InvariantCulture))
            .Append(" realm=").Append(RvrRecordText.Realm(realm))
            .Append(" band=").Append(band.ToString(CultureInfo.InvariantCulture))
            .Append(" members=").Append(members.ToString(CultureInfo.InvariantCulture))
            .Append(" pool=").Append(pool.ToString(CultureInfo.InvariantCulture))
            .Append(" bgEligible=").Append(battlegroundEligible.ToString(CultureInfo.InvariantCulture))
            .Append(" target=").Append(Counts(target))
            .Append(" held=").Append(Counts(held))
            .Append(" open=").Append(Counts(open))
            .Append(" planned=").Append(Counts(planned))
            .Append(" personalities=").Append(personalities ? "on" : "off");
        return row.ToString();
    }

    /// <summary>Writes a pass row within the row's minute budget. Never throws.</summary>
    public static void EmitPass(string row)
    {
        if (LivingRows.Take(PassMarker, PassRowsPerMinute))
            LivingRows.Emit(row);
    }

    private static string Weights(BotGoalWeights w) =>
        w == null ? "-" : string.Create(CultureInfo.InvariantCulture, $"{w.SoloPve}/{w.GroupPve}/{w.RvR}/{w.Battlegrounds}");

    private static string Counts(AutonomousObjectiveAssignments.Allocation a) =>
        string.Create(CultureInfo.InvariantCulture, $"{a.SoloPve}/{a.GroupPve}/{a.RvR}/{a.Battlegrounds}");
}
