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
/// </summary>
public static class SiegeDefenseBalance
{
    public const int Window = 6;
    public const int MinimumResults = 3;
    public const double Step = 0.125;
    public const double MaximumShift = 0.25;
    /// <summary>Attacker win share above which defenders get more, and below which they get fewer.</summary>
    public const double HighWinShare = 0.8, LowWinShare = 0.45;
    /// <summary>Sieges replayed from the ledger at start: four windows, twice the steps from one limit to the other.</summary>
    public const int LedgerResults = 24;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(SiegeDefenseBalance));
    private static readonly object Sync = new();
    private static readonly Queue<bool> Results = new();
    private static double _shift;
    private static bool _loaded;
    private static readonly List<bool> EndedBeforeLoad = new();

    /// <summary>Records a finished siege and moves the shift one step when the window says so.</summary>
    public static void Record(bool attackerWon)
    {
        string line;
        lock (Sync)
        {
            if (!_loaded && EndedBeforeLoad.Count < LedgerResults) EndedBeforeLoad.Add(attackerWon);
            double before = _shift;
            Add(attackerWon);
            line = Line(attackerWon ? "captured" : "defended", before, _shift, Results.ToArray());
        }
        if (Log.IsInfoEnabled) Log.Info(line);
    }

    private static void Add(bool attackerWon)
    {
        Results.Enqueue(attackerWon);
        while (Results.Count > Window) Results.Dequeue();
        _shift = NextShift(_shift, Results.ToArray());
    }

    /// <summary>
    /// The ledger's bot sieges (oldest first) replace the record. Read once at server start (RealmEventRecords.Start),
    /// before any siege of the session can end; a result recorded before it anyway is replayed after the ledger's.
    /// </summary>
    public static void Load(IReadOnlyList<bool> ledgerOldestFirst)
    {
        string line;
        lock (Sync)
        {
            double before = _shift;
            Results.Clear();
            _shift = 0;
            var ledger = (ledgerOldestFirst ?? []).TakeLast(LedgerResults).ToList();
            foreach (bool result in ledger.Concat(EndedBeforeLoad)) Add(result);
            EndedBeforeLoad.Clear();
            _loaded = true;
            line = Line($"loaded {ledger.Count} from the ledger", before, _shift, Results.ToArray());
        }
        if (Log.IsInfoEnabled) Log.Info(line);
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
        return Math.Clamp(shift, -MaximumShift, MaximumShift);
    }

    public static double Ratio(double baseRatio)
    {
        lock (Sync) return Math.Clamp(baseRatio + _shift, 0.25, 2.0);
    }

    public static double Shift
    {
        get { lock (Sync) return _shift; }
    }

    /// <summary>The log line of a change: what happened, the shift before and after, the window (C = captured, D = defended).</summary>
    public static string Line(string what, double before, double after, IEnumerable<bool> window) =>
        $"SIEGE_DEFENSE_BALANCE event=\"{what}\" shift={before:+0.###;-0.###;0}->{after:+0.###;-0.###;0} " +
        $"share={RealmEventPolicy.DefenseRatio:0.###}->{Math.Clamp(RealmEventPolicy.DefenseRatio + after, 0.25, 2.0):0.###} " +
        $"window=\"{string.Concat((window ?? []).Select(won => won ? 'C' : 'D'))}\"";

    /// <summary>Tests only.</summary>
    public static void Reset()
    {
        lock (Sync) { Results.Clear(); _shift = 0; _loaded = true; EndedBeforeLoad.Clear(); }
    }

    /// <summary>Tests only: as after a restart, before the ledger is read.</summary>
    public static void ResetUnloadedForTests()
    {
        lock (Sync) { Results.Clear(); _shift = 0; _loaded = false; EndedBeforeLoad.Clear(); }
    }
}
