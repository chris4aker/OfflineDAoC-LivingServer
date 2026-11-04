using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DOL.GS;

/// <summary>
/// Each raid encounter's record: its last <see cref="Depth"/> counted results (wins and losses, oldest first) from the
/// event ledger (the launcher's Herald), kept in memory and updated as raids end. One record serves both halves of the
/// raid balance (owner 2026-10-19, "about 2 in 3"): the Epic Special Forces call-up (its last six results) and the
/// raid's own size (RealmRaidRecruitmentPolicy.Size, all of it). Interrupted, beaten-to-it, failed and stopped raids are
/// not results: logistics and restarts are fixed, not balanced. Loaded once off the game loop; until it has loaded,
/// <see cref="Loaded"/> is false and raids recruit today's full size.
/// </summary>
public static class RealmRaidRecord
{
    /// <summary>
    /// Counted results kept per encounter: in practice its whole history (each encounter is raided about once a day of
    /// play, so 400 is more than a year). The size is a running total replayed from today's size, so a record cut too
    /// short would forget where it had got to: in the simulation (UT_RealmRaidWinBalance) a 100-result record let the
    /// size jump 13 points between raids as old results dropped out; at 400 it never moves more than one step.
    /// </summary>
    public const int Depth = 400;
    /// <summary>Ledger rows read per encounter at load: restarts leave Interrupted rows between the results.</summary>
    public const int LedgerRows = 1600;

    private static readonly object Sync = new();
    private static Dictionary<string, List<string>> _record;
    private static readonly List<(string Id, string Outcome)> EndedBeforeLoad = new();
    private static int _loading;

    public static bool Counted(string outcome) => AutonomousEpicSpecialForces.IsWin(outcome) || AutonomousEpicSpecialForces.IsLoss(outcome);

    /// <summary>The counted results of a ledger's rows (oldest first), at most <see cref="Depth"/>.</summary>
    public static List<string> CountedOf(IEnumerable<string> outcomesOldestFirst)
    {
        var counted = (outcomesOldestFirst ?? []).Where(Counted).ToList();
        if (counted.Count > Depth) counted.RemoveRange(0, counted.Count - Depth);
        return counted;
    }

    public static bool Loaded
    {
        get { lock (Sync) return _record != null; }
    }

    /// <summary>The encounter's counted results, oldest first (empty until loaded).</summary>
    public static List<string> Get(string raidId)
    {
        lock (Sync) return _record?.GetValueOrDefault(raidId)?.ToList() ?? [];
    }

    /// <summary>A raid ended: a win or a loss joins its record (anything else is ignored).</summary>
    public static void Add(string raidId, string outcome)
    {
        if (!Counted(outcome)) return;
        lock (Sync)
        {
            if (_record == null) EndedBeforeLoad.Add((raidId, outcome));
            else Append(_record, raidId, outcome);
        }
    }

    private static void Append(Dictionary<string, List<string>> record, string raidId, string outcome)
    {
        if (!record.TryGetValue(raidId, out var list)) record[raidId] = list = new();
        list.Add(outcome);
        if (list.Count > Depth) list.RemoveRange(0, list.Count - Depth);
    }

    /// <summary>Starts loading the record from the ledger, once, off the game loop.</summary>
    public static void EnsureLoading()
    {
        if (Interlocked.Exchange(ref _loading, 1) != 0) return;
        string[] ids = AutonomousRealmRaid.Definitions.Select(d => d.Id).Distinct().ToArray();
        Task.Run(() =>
        {
            var loaded = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string id in ids)
                loaded[id] = CountedOf(RealmEventRecords.FinishedOutcomes(id, LedgerRows));
            lock (Sync)
            {
                foreach (var (id, outcome) in EndedBeforeLoad) Append(loaded, id, outcome);
                EndedBeforeLoad.Clear();
                _record = loaded;
            }
        });
    }

    /// <summary>Tests only: an empty, loaded record.</summary>
    public static void ResetForTests()
    {
        lock (Sync)
        {
            _record = new(StringComparer.Ordinal);
            EndedBeforeLoad.Clear();
        }
        Interlocked.Exchange(ref _loading, 1);
    }

    /// <summary>Tests only: the encounter's ledger rows, oldest first (uncounted rows are dropped as at load).</summary>
    public static void SetForTests(string raidId, params string[] outcomes)
    {
        lock (Sync)
        {
            _record ??= new(StringComparer.Ordinal);
            _record[raidId] = CountedOf(outcomes);
        }
    }

    /// <summary>Tests only: not loaded yet (results that end now wait for the load).</summary>
    public static void UnloadForTests()
    {
        lock (Sync) { _record = null; EndedBeforeLoad.Clear(); }
        Interlocked.Exchange(ref _loading, 1);
    }
}
