using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Keeps bot sieges contested (owner, 2026-10-19: endgame fights should be won about two times in three, never a
/// sure thing). The last sieges' results nudge the defenders' share: attackers winning nearly every one adds up to
/// 0.25 to rvr_siege_defense_share, attackers losing most of them takes up to 0.25 away.
///
/// 2026-11-04: the record was in memory only, so every restart put the share back to the setting and it needed three
/// sieges in one session before it moved at all; on the owner's test copy, restarted many times that day, 6 of 7 keep
/// sieges were captured. The record is now rebuilt from the event ledger at start (<see cref="Load"/>): the last
/// <see cref="LedgerResults"/> bot sieges, replayed. Every change is logged (SIEGE_DEFENSE_BALANCE).
///
/// 2026-11-06 (build 168f55d, 18:42-07:30): the shift sat at +0.25 all night (share 1.0) while attackers took 13 of 17
/// keeps. The share only caps the defenders a siege may draw, and it was rarely the limit: in 11 of the 13 captures the
/// realm committed 35-90 defenders, what it had free, while attacker reinforcements filled their 128 in 10 of 17
/// battles. So past the share's limit the balance now trims the attackers' cap instead, by up to
/// <see cref="MaximumAttackerTrim"/> (128 to 112 to 96 for a keep). When defenders win again the trim comes off first,
/// then the share (docs/NIGHT-SIEGES-AND-MARFACH.md, section 4).
///
/// 2026-11-27 (#523 step 2): the window is server-wide, so one realm's lost keeps raised every realm's defenders
/// (defense.md finding 1). A window per defending realm is now kept beside it, moved by the same rule and logged
/// (SIEGE_DEFENSE_BALANCE_REALM, applied=false). The share and the attackers' cap still follow the server-wide shift
/// until the owner picks the lever (#523, owner question 49).
/// </summary>
public static class SiegeDefenseBalance
{
    public const int Window = 6;
    public const int MinimumResults = 3;
    public const double Step = 0.125;
    /// <summary>How far the defenders' share moves from the setting, either way.</summary>
    public const double MaximumShift = 0.25;
    /// <summary>How far past <see cref="MaximumShift"/> the shift goes on the defenders' side: the share of the attackers'
    /// cap that it takes away (two steps: 128, 112, 96 for a keep; 240, 210, 180 for a relic keep).</summary>
    public const double MaximumAttackerTrim = 0.25;
    /// <summary>
    /// Attacker win share above which defenders get more, and below which they get fewer. Was 0.8 / 0.45 (owner 2026-10-19:
    /// sieges won about two times in three). Owner 2026-11-09: about half. With the defenders now called when the army marches
    /// (AutonomousRvrEventLayer.CallDefendersAtMarch), 0.65 / 0.35 is a band around one half: of the last six, four or more
    /// captured adds defenders, two or fewer takes them away, three holds. In the balance simulation (UT_SiegeBalanceNov06,
    /// a model of the attackers' chance by the shift, ten curves) it settles at 0.44-0.56, 0.50 on average; 0.8 / 0.45 settled
    /// at 0.53-0.61 and 0.5 / 0.2 (the first 1.1.3 draft) at 0.39-0.47.
    /// </summary>
    public const double HighWinShare = 0.65, LowWinShare = 0.35;
    /// <summary>Sieges replayed from the ledger at start: four windows, twice the steps from one limit to the other.</summary>
    public const int LedgerResults = 24;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(SiegeDefenseBalance));
    private static readonly object Sync = new();
    private static readonly Queue<bool> Results = new();
    private static double _shift;
    private static bool _loaded;
    private static readonly List<SiegeResult> EndedBeforeLoad = new();
    private static readonly Dictionary<eRealm, Queue<bool>> RealmResults = new();
    private static readonly Dictionary<eRealm, double> RealmShifts = new();

    /// <summary>One finished bot siege: captured or not, and the realm that held the keep (None when unknown).</summary>
    public readonly record struct SiegeResult(bool Captured, eRealm Defender);

    /// <summary>The realms with a record of their own: the three that hold keeps.</summary>
    public static bool HasRealmRecord(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

    /// <summary>Records a finished siege and moves the shift one step when the window says so; the defending realm's
    /// own window moves by the same rule.</summary>
    public static void Record(bool attackerWon, eRealm defender = eRealm.None)
    {
        string line, realmLine = null;
        lock (Sync)
        {
            if (!_loaded && EndedBeforeLoad.Count < LedgerResults) EndedBeforeLoad.Add(new(attackerWon, defender));
            double before = _shift;
            double realmBefore = RealmShiftLocked(defender);
            Add(new(attackerWon, defender));
            string what = attackerWon ? "captured" : "defended";
            line = Line(what, before, _shift, Results.ToArray());
            if (HasRealmRecord(defender))
                realmLine = RealmLine(defender, what, realmBefore, RealmShifts[defender], RealmResults[defender].ToArray());
        }
        if (Log.IsInfoEnabled)
        {
            Log.Info(line);
            if (realmLine != null) Log.Info(realmLine);
        }
    }

    private static void Add(SiegeResult result)
    {
        Results.Enqueue(result.Captured);
        while (Results.Count > Window) Results.Dequeue();
        _shift = NextShift(_shift, Results.ToArray());
        if (!HasRealmRecord(result.Defender)) return;
        if (!RealmResults.TryGetValue(result.Defender, out var window)) RealmResults[result.Defender] = window = new();
        window.Enqueue(result.Captured);
        while (window.Count > Window) window.Dequeue();
        RealmShifts[result.Defender] = NextShift(RealmShiftLocked(result.Defender), window.ToArray());
    }

    private static double RealmShiftLocked(eRealm realm) => RealmShifts.GetValueOrDefault(realm);

    /// <summary>
    /// The ledger's bot sieges (oldest first) replace the record. Read once at server start (RealmEventRecords.Start),
    /// before any siege of the session can end; a result recorded before it anyway is replayed after the ledger's.
    /// </summary>
    public static void Load(IReadOnlyList<bool> ledgerOldestFirst) =>
        LoadResults((ledgerOldestFirst ?? []).Select(captured => new SiegeResult(captured, eRealm.None)).ToList());

    /// <summary>As <see cref="Load"/>, with each siege's defending realm, so the per-realm windows are rebuilt too.</summary>
    public static void LoadResults(IReadOnlyList<SiegeResult> ledgerOldestFirst)
    {
        var lines = new List<string>();
        lock (Sync)
        {
            double before = _shift;
            var realmsBefore = RealmShifts.ToDictionary(p => p.Key, p => p.Value);
            Results.Clear();
            _shift = 0;
            RealmResults.Clear();
            RealmShifts.Clear();
            var ledger = (ledgerOldestFirst ?? []).TakeLast(LedgerResults).ToList();
            foreach (SiegeResult result in ledger.Concat(EndedBeforeLoad)) Add(result);
            EndedBeforeLoad.Clear();
            _loaded = true;
            string what = $"loaded {ledger.Count} from the ledger";
            lines.Add(Line(what, before, _shift, Results.ToArray()));
            foreach (eRealm realm in RealmResults.Keys.OrderBy(r => r))
                lines.Add(RealmLine(realm, what, realmsBefore.GetValueOrDefault(realm), RealmShifts[realm], RealmResults[realm].ToArray()));
        }
        if (Log.IsInfoEnabled) foreach (string line in lines) Log.Info(line);
    }

    /// <summary>The shift after these results (oldest first), from none: what a restart rebuilds.</summary>
    public static double ShiftAfter(IEnumerable<bool> resultsOldestFirst)
    {
        var window = new Queue<bool>();
        double shift = 0;
        foreach (bool result in resultsOldestFirst ?? [])
        {
            window.Enqueue(result);
            while (window.Count > Window) window.Dequeue();
            shift = NextShift(shift, window.ToArray());
        }
        return shift;
    }

    public static double NextShift(double shift, IReadOnlyCollection<bool> results)
    {
        if (results == null || results.Count < MinimumResults) return shift;
        double share = results.Count(won => won) / (double)results.Count;
        if (share > HighWinShare) shift += Step;
        else if (share < LowWinShare) shift -= Step;
        return Math.Clamp(shift, -MaximumShift, MaximumShift + MaximumAttackerTrim);
    }

    /// <summary>The defenders' share at this shift: the setting moved by at most <see cref="MaximumShift"/>.</summary>
    public static double RatioAt(double baseRatio, double shift) =>
        Math.Clamp(baseRatio + Math.Min(shift, MaximumShift), 0.25, 2.0);

    /// <summary>The share of the attackers' cap taken away at this shift: whatever lies past <see cref="MaximumShift"/>.</summary>
    public static double AttackerTrimAt(double shift) => Math.Clamp(shift - MaximumShift, 0, MaximumAttackerTrim);

    /// <summary>The attackers' cap at this shift (at least 8).</summary>
    public static int AttackerCapAt(int fullCap, double shift) =>
        Math.Max(8, (int)Math.Round(fullCap * (1 - AttackerTrimAt(shift))));

    public static double Ratio(double baseRatio)
    {
        lock (Sync) return RatioAt(baseRatio, _shift);
    }

    /// <summary>The attackers' cap in use: the full cap, trimmed once the defenders' share is at its limit.</summary>
    public static int AttackerCap(int fullCap)
    {
        lock (Sync) return AttackerCapAt(fullCap, _shift);
    }

    public static double Shift
    {
        get { lock (Sync) return _shift; }
    }

    /// <summary>The shift of one defending realm's own window (logged only: #523 step 2, owner question 49).</summary>
    public static double RealmShift(eRealm realm)
    {
        lock (Sync) return RealmShiftLocked(realm);
    }

    /// <summary>The defending realm's own window, oldest first (true = captured).</summary>
    public static bool[] RealmWindow(eRealm realm)
    {
        lock (Sync) return RealmResults.TryGetValue(realm, out var window) ? window.ToArray() : [];
    }

    /// <summary>The log line of a change: what happened, the shift before and after, the share and the keep attackers' cap
    /// (setting -> in use), the window (C = captured, D = defended).</summary>
    public static string Line(string what, double before, double after, IEnumerable<bool> window) =>
        $"SIEGE_DEFENSE_BALANCE event=\"{what}\" shift={before:+0.###;-0.###;0}->{after:+0.###;-0.###;0} " +
        $"share={RealmEventPolicy.DefenseRatio:0.###}->{RatioAt(RealmEventPolicy.DefenseRatio, after):0.###} " +
        $"attackerCap={RealmEventPolicy.KeepAttackers}->{AttackerCapAt(RealmEventPolicy.KeepAttackers, after)} " +
        $"window=\"{string.Concat((window ?? []).Select(won => won ? 'C' : 'D'))}\"";

    /// <summary>The log line of a defending realm's own window: what happened, its shift before and after, the share
    /// and keep attackers' cap that shift would give, the window (C = captured, D = defended), and whether it is applied
    /// (not yet: the share and the cap follow the server-wide shift, #523 owner question 49).</summary>
    public static string RealmLine(eRealm realm, string what, double before, double after, IEnumerable<bool> window) =>
        $"SIEGE_DEFENSE_BALANCE_REALM realm={GlobalConstants.RealmToName(realm)} event=\"{what}\" " +
        $"shift={before:+0.###;-0.###;0}->{after:+0.###;-0.###;0} " +
        $"share={RatioAt(RealmEventPolicy.DefenseRatio, after):0.###} " +
        $"attackerCap={AttackerCapAt(RealmEventPolicy.KeepAttackers, after)} " +
        $"window=\"{string.Concat((window ?? []).Select(won => won ? 'C' : 'D'))}\" applied=false";

    /// <summary>Tests only.</summary>
    public static void Reset()
    {
        lock (Sync) { ClearLocked(); _loaded = true; }
    }

    /// <summary>Tests only: as after a restart, before the ledger is read.</summary>
    public static void ResetUnloadedForTests()
    {
        lock (Sync) { ClearLocked(); _loaded = false; }
    }

    private static void ClearLocked()
    {
        Results.Clear(); _shift = 0; EndedBeforeLoad.Clear(); RealmResults.Clear(); RealmShifts.Clear();
    }
}
