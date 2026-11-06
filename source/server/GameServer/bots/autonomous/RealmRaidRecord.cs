using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DOL.GS;

/// <summary>One counted result at an encounter: its outcome, whether the Epic Special Forces took part in it, and the size
/// the raid ran at (a percentage of its period size; null on rows from before 2026-11-05 and unsized raids).</summary>
public readonly record struct RaidResult(string Outcome, bool WithSpecialForces = false, double? SizePercent = null)
{
    /// <summary>An outcome alone: a raid the Special Forces did not join (and every ledger row from before 2026-11-04).</summary>
    public static implicit operator RaidResult(string outcome) => new(outcome);
    /// <summary>A win the Special Forces helped.</summary>
    public bool AssistedWin => WithSpecialForces && AutonomousEpicSpecialForces.IsWin(Outcome);
    public override string ToString() => WithSpecialForces ? Outcome + " (Special Forces)" : Outcome;
}

/// <summary>
/// Each raid encounter's record: its last <see cref="Depth"/> counted results (wins and losses, oldest first) from the
/// event ledger (the launcher's Herald), kept in memory and updated as raids end. One record serves both halves of the
/// raid balance (owner 2026-10-19, "about 2 in 3"): the raid's own size (RealmRaidRecruitmentPolicy.Size) and the Epic
/// Special Forces call-up (AutonomousEpicSpecialForces.CallUpParties, which also reads whether they took part in each
/// win: the ledger details say so, RealmEventRecordStore.SpecialForcesMarker, and an older row counts as without them).
/// Interrupted, beaten-to-it, failed and stopped raids are not results: logistics and restarts are fixed, not balanced.
/// Nor are raids the player started from the launcher (owner, 2026-11-04): their ledger rows are marked
/// (RealmEventRecordStore.ForcedRaidPrefix) and left out, and an unmarked older row counts as automatic. Loaded once off
/// the game loop; until it has loaded, <see cref="Loaded"/> is false and raids recruit their full size.
/// </summary>
public static class RealmRaidRecord
{
    /// <summary>
    /// Counted results kept per encounter: in practice its whole history (each encounter is raided about once a day of
    /// play, so 400 is more than a year). The size is a running total replayed from the full size, so a record cut too
    /// short would forget where it had got to: in the simulation (UT_RealmRaidWinBalance) a 100-result record let the
    /// size jump 13 points between raids as old results dropped out. Once 400 are kept, a raid moves by at most one step
    /// plus the oldest result dropping out (15 points: a new loss and an old win gone).
    /// </summary>
    public const int Depth = 400;
    /// <summary>Ledger rows read per encounter at load: restarts leave Interrupted rows between the results.</summary>
    public const int LedgerRows = 1600;

    private static readonly object Sync = new();
    private static Dictionary<string, List<RaidResult>> _record;
    private static readonly List<(string Id, RaidResult Result)> EndedBeforeLoad = new();
    private static int _loading;

    public static bool Counted(string outcome) => AutonomousEpicSpecialForces.IsWin(outcome) || AutonomousEpicSpecialForces.IsLoss(outcome);

    /// <summary>The counted results of a ledger's rows (oldest first), at most <see cref="Depth"/>.</summary>
    public static List<string> CountedOf(IEnumerable<string> outcomesOldestFirst) =>
        CountedResults((outcomesOldestFirst ?? []).Select(o => (RaidResult)o)).Select(r => r.Outcome).ToList();

    /// <summary>The counted results (oldest first), at most <see cref="Depth"/>.</summary>
    public static List<RaidResult> CountedResults(IEnumerable<RaidResult> resultsOldestFirst)
    {
        var counted = (resultsOldestFirst ?? []).Where(r => Counted(r.Outcome)).ToList();
        if (counted.Count > Depth) counted.RemoveRange(0, counted.Count - Depth);
        return counted;
    }

    public static bool Loaded
    {
        get { lock (Sync) return _record != null; }
    }

    /// <summary>The encounter's counted outcomes, oldest first (empty until loaded).</summary>
    public static List<string> Get(string raidId) => GetResults(raidId).Select(r => r.Outcome).ToList();

    /// <summary>The encounter's counted results with the Special Forces' part in them, oldest first (empty until loaded).</summary>
    public static List<RaidResult> GetResults(string raidId)
    {
        lock (Sync) return _record?.GetValueOrDefault(raidId)?.ToList() ?? [];
    }

    /// <summary>A raid ended: a win or a loss of an automatic raid joins its record (anything else is ignored), with
    /// whether the Special Forces joined it.</summary>
    public static void Add(string raidId, string outcome, bool forced = false, bool withSpecialForces = false, double? sizePercent = null)
    {
        if (forced || !Counted(outcome)) return;
        var result = new RaidResult(outcome, withSpecialForces, sizePercent);
        lock (Sync)
        {
            if (_record == null) EndedBeforeLoad.Add((raidId, result));
            else Append(_record, raidId, result);
        }
    }

    private static void Append(Dictionary<string, List<RaidResult>> record, string raidId, RaidResult result)
    {
        if (!record.TryGetValue(raidId, out var list)) record[raidId] = list = new();
        list.Add(result);
        if (list.Count > Depth) list.RemoveRange(0, list.Count - Depth);
    }

    /// <summary>A ledger's rows as results: the Special Forces took part when its details name any who joined.</summary>
    public static List<RaidResult> FromLedger(IEnumerable<(string Outcome, int SpecialForces)> rowsOldestFirst) =>
        CountedResults((rowsOldestFirst ?? []).Select(row => new RaidResult(row.Outcome, row.SpecialForces > 0)));

    /// <summary>A ledger's rows as results, with the size each raid ran at when its details say.</summary>
    public static List<RaidResult> FromLedger(IEnumerable<(string Outcome, int SpecialForces, double? SizePercent)> rowsOldestFirst) =>
        CountedResults((rowsOldestFirst ?? []).Select(row => new RaidResult(row.Outcome, row.SpecialForces > 0, row.SizePercent)));

    /// <summary>Starts loading the record from the ledger, once, off the game loop.</summary>
    public static void EnsureLoading()
    {
        if (Interlocked.Exchange(ref _loading, 1) != 0) return;
        string[] ids = AutonomousRealmRaid.Definitions.Select(d => d.Id).Distinct().ToArray();
        Task.Run(() =>
        {
            var loaded = new Dictionary<string, List<RaidResult>>(StringComparer.Ordinal);
            foreach (string id in ids)
                loaded[id] = FromLedger(RealmEventRecords.FinishedResults(id, LedgerRows, automaticOnly: true));
            lock (Sync)
            {
                foreach (var (id, result) in EndedBeforeLoad) Append(loaded, id, result);
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
    public static void SetForTests(string raidId, params string[] outcomes) =>
        SetResultsForTests(raidId, (outcomes ?? []).Select(o => (RaidResult)o).ToArray());

    /// <summary>Tests only: the encounter's results with the Special Forces' part, oldest first.</summary>
    public static void SetResultsForTests(string raidId, params RaidResult[] results)
    {
        lock (Sync)
        {
            _record ??= new(StringComparer.Ordinal);
            _record[raidId] = CountedResults(results);
        }
    }

    /// <summary>Tests only: not loaded yet (results that end now wait for the load).</summary>
    public static void UnloadForTests()
    {
        lock (Sync) { _record = null; EndedBeforeLoad.Clear(); }
        Interlocked.Exchange(ref _loading, 1);
    }
}
