using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Connection;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Save safety for the world database (Living Server 1.1.2, technical audit 2026-11-03 section 2):
    /// <list type="bullet">
    /// <item>the database always opens in WAL with Synchronous=Normal, pooled, with a busy timeout, whatever
    /// serverconfig.xml says (the file itself is never rewritten);</item>
    /// <item>a quick integrity check at each start, timed and logged (DB_INTEGRITY);</item>
    /// <item>a daily online backup into runtime\data\backups, at most one a day, the last 7 kept (DB_BACKUP);</item>
    /// <item>the WAL is emptied into the database file when the server stops, so a copy of the file alone is complete.</item>
    /// </list>
    /// All of the file work runs on a timer thread or at start and stop, never on the game loop.
    /// </summary>
    public static class WorldDatabaseSafety
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        [ServerProperty("system", "db_backup_keep",
            "Daily backups of the world database kept in runtime\\data\\backups (the oldest beyond this are deleted). 0 = no daily backups.", 7)]
        public static int BackupKeep = 7;

        [ServerProperty("system", "db_integrity_check",
            "World database check at each server start: quick (PRAGMA quick_check, the default), full (PRAGMA integrity_check, slower) or off.", "quick")]
        public static string IntegrityCheckMode = "quick";

        /// <summary>At most one automatic backup in this time.</summary>
        public static readonly TimeSpan BackupInterval = TimeSpan.FromHours(24);

        /// <summary>The first look after a start: after the world has loaded and the first logins are done.</summary>
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(5);

        /// <summary>How often a running server looks whether a backup is due.</summary>
        public static readonly TimeSpan CheckPeriod = TimeSpan.FromHours(1);

        /// <summary>Free disk space a backup leaves at least, beyond the copy itself.</summary>
        public const long FreeSpaceMarginBytes = 256L * 1024 * 1024;

        private static void Info(string message)
        {
            if (Log.IsInfoEnabled)
                Log.Info(message);
        }

        private static void Warn(string message, Exception e = null)
        {
            if (!Log.IsWarnEnabled)
                return;
            if (e == null)
                Log.Warn(message);
            else
                Log.Warn(message, e);
        }

        private static void Error(string message)
        {
            if (Log.IsErrorEnabled)
                Log.Error(message);
        }

        private static Timer _timer;
        private static int _backupRunning;

        /// <summary>The world database file (null when the server doesn't use SQLite).</summary>
        public static string DatabasePath { get; private set; }

        /// <summary>runtime\data: the world database's folder.</summary>
        public static string DataDirectory => DatabasePath == null ? null : Path.GetDirectoryName(DatabasePath);

        /// <summary>runtime\data\backups.</summary>
        public static string BackupDirectory => DataDirectory == null ? null : Path.Combine(DataDirectory, "backups");

        /// <summary>The start's integrity check found a problem: no automatic backup replaces a good one until it is fixed.</summary>
        public static bool IntegrityFailed { get; private set; }

        /// <summary>The connection string the server opens the world with (logs what it changed).</summary>
        public static string PrepareConnectionString(EConnectionType type, string configured)
        {
            if (type != EConnectionType.DATABASE_SQLITE)
                return configured;

            SqliteSafety.Result safe = SqliteSafety.Normalize(configured);
            DatabasePath = safe.DatabasePath;
            string changed = safe.Changes.Count == 0 ? "none" : string.Join("; ", safe.Changes);
            Info($"DB_SAFETY database=\"{safe.DatabasePath}\" journal=WAL synchronous=Normal pooling=on " +
                     $"busy_timeout_ms>={SqliteSafety.MinimumBusyTimeoutMs} changed=\"{changed}\" (serverconfig.xml is left as it is)");
            return safe.ConnectionString;
        }

        /// <summary>After the first open: the file really is in WAL.</summary>
        public static void ConfirmJournal()
        {
            if (DatabasePath == null || !File.Exists(DatabasePath))
                return;

            try
            {
                string mode = SqliteSafety.JournalMode(DatabasePath);
                if (mode == "wal")
                    Info("DB_SAFETY journal_mode=wal confirmed");
                else
                    Warn($"DB_SAFETY journal_mode={mode}: the database did not switch to WAL (another program holding it open?). Saves are less safe until the next start.");
            }
            catch (Exception e)
            {
                Warn("DB_SAFETY could not read the journal mode", e);
            }
        }

        /// <summary>The integrity check at start. Never stops the server: a problem is logged with what to do.</summary>
        public static bool CheckAtStart()
        {
            if (DatabasePath == null || !File.Exists(DatabasePath))
                return true;

            string mode = (IntegrityCheckMode ?? "quick").Trim().ToLowerInvariant();
            if (mode == "off")
            {
                Info("DB_INTEGRITY mode=off (db_integrity_check)");
                return true;
            }

            try
            {
                long bytes = new FileInfo(DatabasePath).Length;
                SqliteSafety.IntegrityResult result = SqliteSafety.CheckIntegrity(DatabasePath, full: mode == "full");
                IntegrityFailed = !result.Ok;
                if (result.Ok)
                {
                    Info($"DB_INTEGRITY mode={result.Mode} result=ok ms={result.Milliseconds} size_mb={bytes / (1024 * 1024)}");
                }
                else
                {
                    Error($"DB_INTEGRITY mode={result.Mode} result=FAILED ms={result.Milliseconds} problems=\"{string.Join(" | ", result.Problems)}\". " +
                              "Your world database is damaged. Stop the server and restore the newest backup from " +
                              $"{BackupDirectory} (README: Backups). Automatic backups are paused so they can't replace a good one.");
                }
            }
            catch (Exception e)
            {
                Warn("DB_INTEGRITY could not run the check", e);
            }

            return true;
        }

        /// <summary>Whether the daily backup runs now, and why not.</summary>
        public sealed record DailyPlan(bool Run, string Reason);

        public static DailyPlan PlanDaily(int keep, bool integrityFailed, IReadOnlyList<SqliteBackups.BackupFile> daily, DateTime nowUtc,
            long? freeBytes, long databaseBytes)
        {
            if (keep < 1)
                return new DailyPlan(false, "daily backups are off (db_backup_keep = 0)");
            if (integrityFailed)
                return new DailyPlan(false, "the start's integrity check failed; restore a backup first");
            if (!SqliteBackups.IsDue(daily, nowUtc, BackupInterval, out TimeSpan? age))
                return new DailyPlan(false, $"the last backup is {age.Value.TotalHours:0.0} h old (one a day)");
            if (freeBytes.HasValue && freeBytes.Value < databaseBytes + FreeSpaceMarginBytes)
                return new DailyPlan(false, $"not enough free disk space ({freeBytes.Value / (1024 * 1024)} MB free, the copy needs {databaseBytes / (1024 * 1024)} MB and a margin)");
            return new DailyPlan(true, age.HasValue ? $"the last backup is {age.Value.TotalHours:0.0} h old" : "no backup yet");
        }

        /// <summary>One daily-backup turn: plan, back up, rotate. Returns the DB_BACKUP line it logged.</summary>
        public static string RunDaily(string databasePath, string backupDirectory, int keep, bool integrityFailed, DateTime nowUtc)
        {
            string prefix = SqliteBackups.Prefix(databasePath);
            string line;
            try
            {
                int unfinished = SqliteBackups.RemoveUnfinished(backupDirectory, prefix);
                if (unfinished > 0)
                    Info($"DB_BACKUP removed {unfinished} unfinished copy file(s) from an earlier stop");

                var daily = SqliteBackups.List(backupDirectory, prefix, SqliteBackups.DailyLabel);
                long databaseBytes = new FileInfo(databasePath).Length;
                DailyPlan plan = PlanDaily(keep, integrityFailed, daily, nowUtc, FreeBytes(backupDirectory), databaseBytes);
                if (!plan.Run)
                {
                    line = $"DB_BACKUP result=skipped reason=\"{plan.Reason}\"";
                    Info(line);
                    return line;
                }

                SqliteBackups.Created created = SqliteBackups.Create(databasePath, backupDirectory, nowUtc, SqliteBackups.DailyLabel);
                IReadOnlyList<string> removed = SqliteBackups.Rotate(backupDirectory, prefix, SqliteBackups.DailyLabel, keep);
                line = $"DB_BACKUP result=written path=\"{created.Path}\" size_mb={created.Bytes / (1024 * 1024)} ms={created.Milliseconds} " +
                       $"keep={keep} removed={removed.Count} why=\"{plan.Reason}\"";
                Info(line);
                return line;
            }
            catch (Exception e)
            {
                line = $"DB_BACKUP result=failed error=\"{e.Message}\" (older backups were kept; the next try is in an hour)";
                Warn(line, e);
                return line;
            }
        }

        private static long? FreeBytes(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            if (DatabasePath == null)
                return;

            _timer = new Timer(_ => BackupTurn(), null, FirstCheckDelay, CheckPeriod);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = null;
        }

        private static void BackupTurn()
        {
            // A slow disk may still be writing the last one: never two at once.
            if (Interlocked.CompareExchange(ref _backupRunning, 1, 0) != 0)
                return;

            try
            {
                if (DatabasePath != null && File.Exists(DatabasePath))
                    RunDaily(DatabasePath, BackupDirectory, BackupKeep, IntegrityFailed, DateTime.UtcNow);
            }
            finally
            {
                Interlocked.Exchange(ref _backupRunning, 0);
            }
        }

        /// <summary>At server stop, after the last save: the WAL goes into the database file (best effort).</summary>
        public static void CheckpointAtStop()
        {
            if (DatabasePath == null || !File.Exists(DatabasePath))
                return;

            try
            {
                Info($"DB_SAFETY checkpoint at stop: {SqliteSafety.Checkpoint(DatabasePath)}");
            }
            catch (Exception e)
            {
                Warn("DB_SAFETY checkpoint at stop failed; SQLite applies the WAL at the next start, nothing is lost", e);
            }
        }
    }
}
