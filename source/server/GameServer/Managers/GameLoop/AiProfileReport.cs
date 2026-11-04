using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    // Living Server 1.2, AI_PROFILE: the minute report (audit 2026-11-03 §8.4 and §9). The once-a-minute summary line
    // follows stefanrows's BOT_THINK_PROFILE line (refs/forks/stefanrows/main, GPL-3.0); the tick percentiles, the
    // serial/parallel split and the CSV file are new here.

    /// <summary>One system's numbers for one window. Times are summed over every thread, in ms.</summary>
    /// <param name="Name">The dotted name, for example <c>route.zone_step</c>.</param>
    /// <param name="System">The slot number.</param>
    /// <param name="Calls">Scopes that ended in the window (for a lock: waits, not acquisitions).</param>
    /// <param name="TotalMs">Time inside the scopes, including scopes nested in them.</param>
    /// <param name="SelfMs">Time inside the scopes less the nested scopes: these add up without counting twice.</param>
    /// <param name="MaxMs">The longest single call.</param>
    public readonly record struct AiProfileSystemStat(string Name, int System, long Calls, double TotalMs, double SelfMs, double MaxMs)
    {
        /// <summary>The average call in microseconds.</summary>
        public double AverageMicroseconds => Calls == 0 ? 0 : TotalMs * 1000.0 / Calls;
    }

    /// <summary>The game-loop ticks of one window. Ms per tick; the averages count every tick, the percentiles the
    /// recorded ones (see <see cref="Dropped"/>).</summary>
    public readonly record struct AiProfileTickSummary(
        int Ticks, int Dropped,
        double TickAverage, double TickP50, double TickP95, double TickP99, double TickMax,
        double SerialAverage, double SerialP99, double SerialMax,
        double ParallelAverage, double ParallelP99, double ParallelMax)
    {
        /// <summary>The rest of the tick: the other services (movement, attacks, casting, effects, clients...).</summary>
        public double OtherAverage => Math.Max(0, TickAverage - SerialAverage - ParallelAverage);
    }

    /// <summary>
    /// The tick samples of the current window, kept by the game-loop thread only: each tick's length and its serial and
    /// parallel parts. Fixed arrays, so recording allocates nothing; past <see cref="Capacity"/> ticks in one window
    /// (over 136 ticks a second) the extra ticks still count in the averages and the maximum but not in the percentiles.
    /// </summary>
    public sealed class AiProfileTicks
    {
        public const int DefaultCapacity = 8192;

        private readonly double[] _tick;
        private readonly double[] _serial;
        private readonly double[] _parallel;
        private readonly double[] _scratch;
        private int _recorded;
        private int _seen;
        private double _tickSum, _serialSum, _parallelSum;
        private double _tickMax, _serialMax, _parallelMax;

        public AiProfileTicks(int capacity)
        {
            _tick = new double[capacity];
            _serial = new double[capacity];
            _parallel = new double[capacity];
            _scratch = new double[capacity];
        }

        public int Capacity => _tick.Length;

        public void Add(double tickMs, double serialMs, double parallelMs)
        {
            _seen++;
            _tickSum += tickMs;
            _serialSum += serialMs;
            _parallelSum += parallelMs;
            _tickMax = Math.Max(_tickMax, tickMs);
            _serialMax = Math.Max(_serialMax, serialMs);
            _parallelMax = Math.Max(_parallelMax, parallelMs);
            if (_recorded == _tick.Length)
                return;
            _tick[_recorded] = tickMs;
            _serial[_recorded] = serialMs;
            _parallel[_recorded] = parallelMs;
            _recorded++;
        }

        public void Clear()
        {
            _recorded = _seen = 0;
            _tickSum = _serialSum = _parallelSum = 0;
            _tickMax = _serialMax = _parallelMax = 0;
        }

        public AiProfileTickSummary Summarize()
        {
            if (_seen == 0)
                return default;
            double tickP50 = Percentiles(_tick, 50, 95, 99, out double tickP95, out double tickP99);
            Percentiles(_serial, 99, 99, 99, out _, out double serialP99);
            Percentiles(_parallel, 99, 99, 99, out _, out double parallelP99);
            return new AiProfileTickSummary(_seen, _seen - _recorded,
                _tickSum / _seen, tickP50, tickP95, tickP99, _tickMax,
                _serialSum / _seen, serialP99, _serialMax,
                _parallelSum / _seen, parallelP99, _parallelMax);
        }

        // Sorts a copy of the recorded samples once and reads three percentiles from it; returns the first.
        private double Percentiles(double[] samples, double first, double second, double third, out double secondValue,
            out double thirdValue)
        {
            Array.Copy(samples, _scratch, _recorded);
            Array.Sort(_scratch, 0, _recorded);
            secondValue = Percentile(_scratch, _recorded, second);
            thirdValue = Percentile(_scratch, _recorded, third);
            return Percentile(_scratch, _recorded, first);
        }

        /// <summary>
        /// The nearest-rank percentile of the first <paramref name="count"/> values of <paramref name="sorted"/> (ascending):
        /// the smallest value with at least <paramref name="percent"/>% of the values at or below it. With 100 values
        /// 1..100, p50 is 50, p95 is 95 and p99 is 99. No values give 0.
        /// </summary>
        public static double Percentile(double[] sorted, int count, double percent)
        {
            if (count <= 0)
                return 0;
            int rank = (int)Math.Ceiling(percent / 100.0 * count);
            return sorted[Math.Clamp(rank, 1, count) - 1];
        }
    }

    /// <summary>One finished window (normally a minute) of the profiler: what the AI_PROFILE line, the CSV file and
    /// <c>/lsprofile</c> show.</summary>
    public sealed class AiProfileMinute
    {
        /// <summary>The audit's serial budget (§9): all serial planning at most 5 ms a tick, each pulse at most 2 ms.</summary>
        public const double SerialBudgetMs = 5;
        public const double PulseBudgetMs = 2;

        public const int TopCount = 10;

        public AiProfileMinute(DateTime utc, double windowSeconds, AiProfileTickSummary ticks, AiProfileSystemStat[] systems)
        {
            Utc = utc;
            WindowSeconds = windowSeconds;
            Ticks = ticks;
            Systems = systems ?? [];
            LocksMs = Systems.Where(stat => AiSystemNames.IsLockWait(stat.System)).Sum(stat => stat.TotalMs);
            double allSelf = Systems.Sum(stat => stat.SelfMs);
            TotalSelfMs = allSelf;
        }

        public DateTime Utc { get; }
        public double WindowSeconds { get; }
        public AiProfileTickSummary Ticks { get; }

        /// <summary>Every system that ran, biggest self time first.</summary>
        public AiProfileSystemStat[] Systems { get; }

        /// <summary>Time spent waiting for the coordinator, raid, RvR and database locks, all threads.</summary>
        public double LocksMs { get; }

        /// <summary>The self time of every system together: what <see cref="SharePercent"/> divides by.</summary>
        public double TotalSelfMs { get; }

        /// <summary>A system's share of all profiled time (self time over all self time).</summary>
        public double SharePercent(AiProfileSystemStat stat) => TotalSelfMs <= 0 ? 0 : stat.SelfMs * 100.0 / TotalSelfMs;

        private static string F(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);

        /// <summary>
        /// The AI_PROFILE line. The first fields are the ones the audit asked for, in its order:
        /// <c>tick_p50 p95 p99 max</c> (ms per tick), <c>serial_ms parallel_ms</c> (average ms per tick),
        /// <c>locks_ms</c> (ms waited on locks in the window, all threads) and <c>top</c> (the ten biggest systems as
        /// name:calls/self-ms, all threads). Extra fields follow.
        /// </summary>
        public string SummaryLine()
        {
            var line = new StringBuilder(512);
            line.Append("AI_PROFILE tick_p50=").Append(F(Ticks.TickP50, "0.00"))
                .Append(" p95=").Append(F(Ticks.TickP95, "0.00"))
                .Append(" p99=").Append(F(Ticks.TickP99, "0.00"))
                .Append(" max=").Append(F(Ticks.TickMax, "0.00"))
                .Append(" serial_ms=").Append(F(Ticks.SerialAverage, "0.000"))
                .Append(" parallel_ms=").Append(F(Ticks.ParallelAverage, "0.000"))
                .Append(" locks_ms=").Append(F(LocksMs, "0.0"))
                .Append(" top=").Append(TopList(TopCount))
                .Append(" ticks=").Append(Ticks.Ticks.ToString(CultureInfo.InvariantCulture))
                .Append(" window_s=").Append(F(WindowSeconds, "0"))
                .Append(" tick_avg=").Append(F(Ticks.TickAverage, "0.00"))
                .Append(" serial_p99=").Append(F(Ticks.SerialP99, "0.00"))
                .Append(" serial_max=").Append(F(Ticks.SerialMax, "0.00"))
                .Append(" parallel_p99=").Append(F(Ticks.ParallelP99, "0.00"))
                .Append(" parallel_max=").Append(F(Ticks.ParallelMax, "0.00"))
                .Append(" other_ms=").Append(F(Ticks.OtherAverage, "0.000"));
            if (Ticks.Dropped > 0)
                line.Append(" unsampled_ticks=").Append(Ticks.Dropped.ToString(CultureInfo.InvariantCulture));
            string over = OverBudget();
            if (over.Length > 0)
                line.Append(" over_budget=").Append(over);
            line.Append(" utc=").Append(Utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            return line.ToString();
        }

        /// <summary>The biggest systems by self time as <c>name:calls/ms</c>, comma separated; <c>none</c> when idle.</summary>
        public string TopList(int count)
        {
            if (Systems.Length == 0)
                return "none";
            return string.Join(",", Systems.Take(count).Select(stat =>
                $"{stat.Name}:{stat.Calls.ToString(CultureInfo.InvariantCulture)}/{F(stat.SelfMs)}"));
        }

        /// <summary>
        /// What is over the audit's serial budget (§9), comma separated, or empty: <c>serial_p99:6.2</c> when the serial
        /// planning took more than 5 ms in the 99th-percentile tick, and <c>pulse.x:3.1</c> for a coordinator pulse whose
        /// longest run passed 2 ms. Only reported, never enforced.
        /// </summary>
        public string OverBudget()
        {
            var over = new List<string>();
            if (Ticks.SerialP99 > SerialBudgetMs)
                over.Add($"serial_p99:{F(Ticks.SerialP99)}");
            foreach (AiProfileSystemStat stat in Systems)
            {
                if (stat.Name.StartsWith("pulse.", StringComparison.Ordinal) && stat.MaxMs > PulseBudgetMs)
                    over.Add($"{stat.Name}:{F(stat.MaxMs)}");
            }
            return string.Join(",", over);
        }

        public const string CsvHeader = "utc,system,calls,total_ms,self_ms,max_ms,avg_us,share_pct";

        /// <summary>One CSV row per system that ran (no header), biggest self time first.</summary>
        public IEnumerable<string> CsvRows()
        {
            string utc = Utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            foreach (AiProfileSystemStat stat in Systems)
                yield return string.Join(",", utc, stat.Name, stat.Calls.ToString(CultureInfo.InvariantCulture),
                    F(stat.TotalMs, "0.###"), F(stat.SelfMs, "0.###"), F(stat.MaxMs, "0.###"),
                    F(stat.AverageMicroseconds, "0.##"), F(SharePercent(stat), "0.##"));
        }

        /// <summary>The lines <c>/lsprofile</c> shows: the tick numbers, then the biggest systems.</summary>
        public IEnumerable<string> ChatLines(int count)
        {
            yield return $"AI profile, the minute to {Utc.ToLocalTime():HH:mm:ss} ({Ticks.Ticks} ticks):";
            yield return $"Tick ms: p50 {F(Ticks.TickP50, "0.0")}, p95 {F(Ticks.TickP95, "0.0")}, p99 {F(Ticks.TickP99, "0.0")}, max {F(Ticks.TickMax, "0.0")}";
            yield return $"Per tick: serial {F(Ticks.SerialAverage, "0.00")} ms (p99 {F(Ticks.SerialP99, "0.0")}), " +
                         $"brains {F(Ticks.ParallelAverage, "0.00")} ms (p99 {F(Ticks.ParallelP99, "0.0")}), " +
                         $"other services {F(Ticks.OtherAverage, "0.00")} ms";
            yield return $"Lock waits: {F(LocksMs)} ms in the minute";
            string over = OverBudget();
            if (over.Length > 0)
                yield return $"Over the serial budget: {over}";
            yield return "System: calls, self ms (share), total ms, longest ms";
            foreach (AiProfileSystemStat stat in Systems.Take(count))
                yield return $"{stat.Name}: {stat.Calls}, {F(stat.SelfMs)} ({F(SharePercent(stat), "0")}%), {F(stat.TotalMs)}, {F(stat.MaxMs, "0.00")}";
            if (Systems.Length > count)
                yield return $"... and {Systems.Length - count} more (/lsprofile all)";
        }
    }

    /// <summary>
    /// Appends each minute's per-system rows to <c>logs/ai-profile.csv</c> beside the server (one header line when the
    /// file is new). Written on a thread-pool thread, so a slow disk never holds a tick. Past 50 MB the file is renamed to
    /// <c>ai-profile.1.csv</c> (replacing an older one) and a new one starts.
    /// </summary>
    public static class AiProfileCsv
    {
        public const string FileName = "ai-profile.csv";
        public const long RollBytes = 50L * 1024 * 1024;

        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Lock WriteLock = new();
        private static int _warned;

        /// <summary>The file the server writes: <c>logs/ai-profile.csv</c> under the server folder, where NLog's
        /// <c>./logs</c> also resolves.</summary>
        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "logs", FileName);

        public static void Append(AiProfileMinute minute) =>
            ThreadPool.QueueUserWorkItem(static state => WriteSafely((AiProfileMinute)state, DefaultPath), minute);

        private static void WriteSafely(AiProfileMinute minute, string path)
        {
            // A thread-pool work item that throws ends the process, so nothing may leave here (review of PR #67): any
            // failure is logged once and the next minute tries again.
            try { Write(minute, path); }
            catch (Exception exception)
            {
                try
                {
                    if (Interlocked.Exchange(ref _warned, 1) == 0)
                        Log.Warn($"AI_PROFILE could not write {path}; the log line still has the top ten", exception);
                }
                catch (Exception) { /* the logger itself failed: still never throw from the pool */ }
            }
        }

        /// <summary>Appends the minute's rows to <paramref name="path"/>, with the header first when the file is new.</summary>
        public static void Write(AiProfileMinute minute, string path)
        {
            if (minute.Systems.Length == 0)
                return;
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > RollBytes)
                {
                    string rolled = Path.Combine(info.DirectoryName!, Path.GetFileNameWithoutExtension(path) + ".1.csv");
                    File.Move(path, rolled, overwrite: true);
                    info.Refresh();
                }
                var text = new StringBuilder();
                if (!info.Exists || info.Length == 0)
                    text.Append(AiProfileMinute.CsvHeader).Append('\n');
                foreach (string row in minute.CsvRows())
                    text.Append(row).Append('\n');
                File.AppendAllText(path, text.ToString());
            }
        }
    }
}
