using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Runs callbacks when an hour, a day, a week (Monday) or a month (the 1st) turns over, by the server's local clock,
    /// and remembers each rollover in the database so a restart neither repeats nor skips one. Port of OpenDAoC-Core
    /// e688e984e, with 73cd88797 (rows cached, made at start) and 7384fed04 (each callback's errors caught).
    ///
    /// Built to be reused: anything timed subscribes to it instead of keeping its own clock and table. The periodic
    /// quests do (PeriodicQuestService), and the house rent service of #562 (OpenDAoC-Core f33df01e4) is meant to:
    /// <code>RolloverSchedulerService.Instance.Subscribe(RolloverSchedulerService.IntervalKey.Hourly, HouseRentService.CheckRents);</code>
    /// A rollover that came due while the server was off runs once at the first tick after start (single-player servers
    /// are usually off at midnight); <see cref="GetLastRollover"/> tells a player logging in whether one passed since
    /// they last played (PeriodicQuestService.OnPlayerJoin). The schedule itself is <see cref="RolloverSchedule"/>, which
    /// takes its clock and storage as arguments so tests can drive it.
    /// </summary>
    public sealed class RolloverSchedulerService : GameServiceBase
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public static RolloverSchedulerService Instance { get; }

        private readonly RolloverSchedule _schedule;

        static RolloverSchedulerService()
        {
            Instance = new();
        }

        private RolloverSchedulerService()
        {
            _schedule = new(new DatabaseStore(), static () => DateTime.Now, static (message, e) =>
            {
                if (log.IsErrorEnabled)
                    log.Error(message, e);
            });
        }

        public override void Tick()
        {
            ProcessPostedActionsParallel();
            _schedule.Tick();
        }

        /// <summary>Calls <paramref name="callback"/> on the game loop at every rollover of the interval. No unsubscribe (as upstream).</summary>
        public void Subscribe(IntervalKey intervalKey, Action callback) => _schedule.Subscribe(intervalKey, callback);

        /// <summary>When the interval last rolled over (DateTime.MinValue for an unknown key).</summary>
        public DateTime GetLastRollover(IntervalKey intervalKey) => _schedule.GetLastRollover(intervalKey);

        /// <summary>When the interval rolls over next (DateTime.MaxValue for an unknown key).</summary>
        public DateTime GetNextRollover(IntervalKey intervalKey) => _schedule.GetNextRollover(intervalKey);

        public void ForceExecute(IntervalKey intervalKey) => _schedule.ForceExecute(intervalKey);

        public void ForceExecuteAll() => _schedule.ForceExecuteAll();

        public enum IntervalKey : int
        {
            // Database keys.
            Hourly = 1,
            Daily = 2,
            Weekly = 3,
            Monthly = 4
        }

        /// <summary>
        /// The ScheduledRollovers rows, read once and cached (73cd88797). A missing row is made at start; our adaptation:
        /// it starts from the old TaskRefreshIntervals row of the same interval when there is one, so the first start
        /// after the update neither resets the daily, weekly and monthly quests early nor late.
        /// </summary>
        private sealed class DatabaseStore : RolloverSchedule.IStore
        {
            private readonly Dictionary<IntervalKey, DbScheduledRollover> _rows = new();

            public IEnumerable<(IntervalKey Key, DateTime LastRollover)> Load()
            {
                List<(IntervalKey Key, DateTime LastRollover)> saved = new();

                foreach (DbScheduledRollover row in GameServer.Database.SelectAllObjects<DbScheduledRollover>())
                {
                    IntervalKey key = (IntervalKey) row.RolloverIntervalKey;

                    if (_rows.TryAdd(key, row))
                        saved.Add((key, row.LastRollover));
                }

                return saved;
            }

            public DateTime Create(IntervalKey key, DateTime now)
            {
                DateTime lastRollover = RolloverSchedule.SeedFromOldTable(key, OldRows(), now);
                DbScheduledRollover row = new() { RolloverIntervalKey = (int) key, LastRollover = lastRollover };
                GameServer.Database.AddObject(row);
                _rows[key] = row;
                return lastRollover;
            }

            public void Save(IntervalKey key, DateTime lastRollover)
            {
                if (!_rows.TryGetValue(key, out DbScheduledRollover row))
                {
                    Create(key, lastRollover);
                    return;
                }

                row.LastRollover = lastRollover;
                GameServer.Database.SaveObject(row);
            }

            private static List<(string Interval, DateTime LastRollover)> OldRows()
            {
                List<(string Interval, DateTime LastRollover)> oldRows = new();

                try
                {
                    foreach (DbTaskRefreshInterval row in GameServer.Database.SelectAllObjects<DbTaskRefreshInterval>())
                        oldRows.Add((row.RolloverInterval, row.LastRollover));
                }
                catch (Exception e)
                {
                    if (log.IsWarnEnabled)
                        log.Warn("TaskRefreshIntervals could not be read; rollovers start from now.", e);
                }

                return oldRows;
            }
        }
    }

    /// <summary>
    /// RolloverSchedulerService's schedule, apart from the database and the clock (tests drive it with a fake of each).
    /// The earliest rollover is checked first; every interval that is due rolls over once, at "now", and its next
    /// rollover is the next boundary after that. A callback that throws is logged and the others still run (7384fed04).
    /// </summary>
    public sealed class RolloverSchedule
    {
        public interface IStore
        {
            /// <summary>The saved last rollover of each interval.</summary>
            IEnumerable<(RolloverSchedulerService.IntervalKey Key, DateTime LastRollover)> Load();

            /// <summary>Makes the saved row of an interval that has none and returns its last rollover.</summary>
            DateTime Create(RolloverSchedulerService.IntervalKey key, DateTime now);

            void Save(RolloverSchedulerService.IntervalKey key, DateTime lastRollover);
        }

        private readonly IStore _store;
        private readonly Func<DateTime> _now;
        private readonly Action<string, Exception> _logError;
        private readonly List<Tracker> _trackers = new();
        private readonly Lock _lock = new();

        public RolloverSchedule(IStore store, Func<DateTime> now, Action<string, Exception> logError = null)
        {
            _store = store;
            _now = now;
            _logError = logError;

            Dictionary<RolloverSchedulerService.IntervalKey, DateTime> saved = new();

            foreach ((RolloverSchedulerService.IntervalKey key, DateTime lastRollover) in store.Load())
                saved.TryAdd(key, lastRollover);

            foreach (RolloverSchedulerService.IntervalKey key in Enum.GetValues<RolloverSchedulerService.IntervalKey>())
            {
                // A missing row is made now (73cd88797): a server that always stops before midnight still rolls over.
                DateTime lastRollover = saved.TryGetValue(key, out DateTime last) ? last : store.Create(key, now());
                _trackers.Add(new Tracker(key) { LastRollover = lastRollover, NextRollover = NextRollover(key, lastRollover) });
            }

            Sort();
        }

        /// <summary>The first boundary after <paramref name="last"/>: the next hour, midnight, Monday or 1st of the month.</summary>
        public static DateTime NextRollover(RolloverSchedulerService.IntervalKey key, DateTime last)
        {
            switch (key)
            {
                case RolloverSchedulerService.IntervalKey.Hourly:
                    return last.Date.AddHours(last.Hour + 1);
                case RolloverSchedulerService.IntervalKey.Daily:
                    return last.Date.AddDays(1);
                case RolloverSchedulerService.IntervalKey.Weekly:
                {
                    int daysToAdd = ((int) DayOfWeek.Monday - (int) last.DayOfWeek + 7) % 7;
                    return last.Date.AddDays(daysToAdd == 0 ? 7 : daysToAdd);
                }
                case RolloverSchedulerService.IntervalKey.Monthly:
                    return new DateTime(last.Year, last.Month, 1, 0, 0, 0, last.Kind).AddMonths(1);
                default:
                    return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// The first start after the update: the old table's row for the same interval ("DAILY", "WEEKLY", "MONTHLY"),
        /// or <paramref name="now"/> (hourly had no old row).
        /// </summary>
        public static DateTime SeedFromOldTable(RolloverSchedulerService.IntervalKey key, IEnumerable<(string Interval, DateTime LastRollover)> oldRows, DateTime now)
        {
            string oldName = key switch
            {
                RolloverSchedulerService.IntervalKey.Daily => "DAILY",
                RolloverSchedulerService.IntervalKey.Weekly => "WEEKLY",
                RolloverSchedulerService.IntervalKey.Monthly => "MONTHLY",
                _ => null
            };

            if (oldName == null || oldRows == null)
                return now;

            foreach ((string interval, DateTime lastRollover) in oldRows)
            {
                if (string.Equals(interval, oldName, StringComparison.OrdinalIgnoreCase) && lastRollover != default && lastRollover <= now)
                    return lastRollover;
            }

            return now;
        }

        public void Subscribe(RolloverSchedulerService.IntervalKey key, Action callback)
        {
            if (callback == null)
                return;

            lock (_lock)
            {
                Find(key)?.Callbacks.Add(callback);
            }
        }

        public DateTime GetLastRollover(RolloverSchedulerService.IntervalKey key)
        {
            lock (_lock)
            {
                return Find(key)?.LastRollover ?? DateTime.MinValue;
            }
        }

        public DateTime GetNextRollover(RolloverSchedulerService.IntervalKey key)
        {
            lock (_lock)
            {
                return Find(key)?.NextRollover ?? DateTime.MaxValue;
            }
        }

        /// <summary>Rolls over every interval that is due; returns how many did.</summary>
        public int Tick()
        {
            DateTime now = _now();
            List<(RolloverSchedulerService.IntervalKey Key, Action[] Callbacks)> due = null;

            lock (_lock)
            {
                // The earliest rollover is always first.
                foreach (Tracker tracker in _trackers)
                {
                    if (now < tracker.NextRollover)
                        break;

                    (due ??= new()).Add((tracker.Key, MarkRolledOver(tracker, now)));
                }

                if (due != null)
                    Sort();
            }

            if (due == null)
                return 0;

            foreach ((RolloverSchedulerService.IntervalKey key, Action[] callbacks) in due)
                Run(key, callbacks);

            return due.Count;
        }

        public void ForceExecute(RolloverSchedulerService.IntervalKey key)
        {
            DateTime now = _now();
            Action[] callbacks;

            lock (_lock)
            {
                Tracker tracker = Find(key);

                if (tracker == null)
                    return;

                callbacks = MarkRolledOver(tracker, now);
                Sort();
            }

            Run(key, callbacks);
        }

        public void ForceExecuteAll()
        {
            foreach (RolloverSchedulerService.IntervalKey key in Enum.GetValues<RolloverSchedulerService.IntervalKey>())
                ForceExecute(key);
        }

        // Saved before the callbacks run, as upstream: a crash in one never makes the rollover repeat at the next start.
        private Action[] MarkRolledOver(Tracker tracker, DateTime now)
        {
            tracker.LastRollover = now;
            tracker.NextRollover = NextRollover(tracker.Key, now);

            try
            {
                _store.Save(tracker.Key, now);
            }
            catch (Exception e)
            {
                _logError?.Invoke($"Saving the {tracker.Key} rollover failed", e);
            }

            return tracker.Callbacks.ToArray();
        }

        private void Run(RolloverSchedulerService.IntervalKey key, Action[] callbacks)
        {
            foreach (Action callback in callbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    _logError?.Invoke($"Error executing callback for interval {key}", e);
                }
            }
        }

        private Tracker Find(RolloverSchedulerService.IntervalKey key)
        {
            foreach (Tracker tracker in _trackers)
            {
                if (tracker.Key == key)
                    return tracker;
            }

            return null;
        }

        private void Sort() => _trackers.Sort(static (a, b) => a.NextRollover.CompareTo(b.NextRollover));

        private sealed class Tracker
        {
            public Tracker(RolloverSchedulerService.IntervalKey key) => Key = key;

            public RolloverSchedulerService.IntervalKey Key { get; }
            public DateTime LastRollover { get; set; }
            public DateTime NextRollover { get; set; }
            public List<Action> Callbacks { get; } = new();
        }
    }
}
