// Consistent online backups of the world database, with rotation and the skip-if-recent rule.
// Ported from lometur's HearthDAoC (deploy/bin/backup.py, GPL-3.0, the same license as this project): the backup API
// into a temporary file, the copy made a standalone file with no -wal/-shm, a quick_check of the copy, an atomic rename,
// and only then the rotation, so a failed backup never deletes an older one and leaves no partial file.
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.Database
{
    public static class SqliteBackups
    {
        /// <summary>Label of the server's automatic backup (the only label rotation and the daily rule look at).</summary>
        public const string DailyLabel = "daily";

        private const string StampFormat = "yyyyMMdd-HHmmss";

        /// <summary>A backup file found in the backups folder.</summary>
        public sealed record BackupFile(string Path, DateTime CreatedUtc, string Label);

        /// <summary>What <see cref="Create"/> wrote.</summary>
        public sealed record Created(string Path, long Bytes, long Milliseconds);

        /// <summary>"opendaoc" for runtime\data\opendaoc.sqlite3.db: the start of every backup's name.</summary>
        public static string Prefix(string databasePath)
        {
            string name = System.IO.Path.GetFileName(databasePath);
            int dot = name.IndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }

        /// <summary>
        /// opendaoc-20261103-191500-daily.sqlite3.db: the time is UTC, so the names sort in time order through
        /// daylight-saving changes. Labels are lower-case letters and digits.
        /// </summary>
        public static string FileName(string prefix, DateTime createdUtc, string label)
        {
            if (string.IsNullOrEmpty(label) || !Regex.IsMatch(label, "^[a-z0-9]+$"))
                throw new ArgumentException("A backup label is lower-case letters and digits.", nameof(label));

            return $"{prefix}-{createdUtc.ToString(StampFormat, CultureInfo.InvariantCulture)}-{label}.sqlite3.db";
        }

        /// <summary>The backups of this database in the folder, oldest first; other files are ignored.</summary>
        public static IReadOnlyList<BackupFile> List(string folder, string prefix, string label = null)
        {
            if (!Directory.Exists(folder))
                return Array.Empty<BackupFile>();

            var pattern = new Regex("^" + Regex.Escape(prefix) + @"-(\d{8}-\d{6})-([a-z0-9]+)\.sqlite3\.db$", RegexOptions.IgnoreCase);
            var found = new List<BackupFile>();
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                Match match = pattern.Match(System.IO.Path.GetFileName(path));
                if (!match.Success)
                    continue;
                if (!DateTime.TryParseExact(match.Groups[1].Value, StampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime created))
                    continue;

                string foundLabel = match.Groups[2].Value.ToLowerInvariant();
                if (label == null || foundLabel == label)
                    found.Add(new BackupFile(path, created, foundLabel));
            }

            return found.OrderBy(file => file.CreatedUtc).ThenBy(file => file.Path, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Whether a new backup is due: none yet, or the newest is at least <paramref name="interval"/> old. A backup dated
        /// in the future (the PC's clock was set back) doesn't count, so a wrong clock can't stop backups for good.
        /// </summary>
        public static bool IsDue(IReadOnlyList<BackupFile> backups, DateTime nowUtc, TimeSpan interval, out TimeSpan? newestAge)
        {
            newestAge = null;
            var valid = backups.Where(file => file.CreatedUtc <= nowUtc.AddMinutes(5)).ToArray();
            if (valid.Length == 0)
                return true;

            newestAge = nowUtc - valid.Max(file => file.CreatedUtc);
            return newestAge >= interval;
        }

        /// <summary>
        /// Deletes the oldest backups with this label beyond the newest <paramref name="keep"/>. Other labels and other
        /// files are never touched. Returns the deleted paths. <paramref name="keep"/> below 1 deletes nothing.
        /// </summary>
        public static IReadOnlyList<string> Rotate(string folder, string prefix, string label, int keep)
        {
            if (keep < 1)
                return Array.Empty<string>();

            var backups = List(folder, prefix, label);
            var deleted = new List<string>();
            foreach (BackupFile old in backups.Take(Math.Max(0, backups.Count - keep)))
            {
                File.Delete(old.Path);
                deleted.Add(old.Path);
            }

            return deleted;
        }

        /// <summary>
        /// A consistent copy of the database while the server runs (SQLite's online backup API, in one step, so the copy is
        /// one moment of the world; in WAL mode the server's saves go on meanwhile). The copy is written as
        /// &lt;name&gt;.part, switched to an ordinary rollback journal (a standalone file: no -wal or -shm to carry),
        /// checked with quick_check, and only then renamed to its final name. On any failure no file is left.
        /// </summary>
        public static Created Create(string databasePath, string folder, DateTime nowUtc, string label)
        {
            var stopwatch = Stopwatch.StartNew();
            Directory.CreateDirectory(folder);
            string prefix = Prefix(databasePath);
            string destination = System.IO.Path.Combine(folder, FileName(prefix, nowUtc, label));
            if (File.Exists(destination))
                throw new IOException($"{destination} already exists.");

            string temporary = destination + ".part";
            RemoveLeftovers(temporary);
            try
            {
                using (SQLiteConnection source = SqliteSafety.OpenSide(databasePath, readOnly: true))
                using (var target = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ConnectionString))
                {
                    target.Open();
                    source.BackupDatabase(target, "main", "main", -1, null, 0);
                    using var journal = target.CreateCommand();
                    journal.CommandText = "PRAGMA journal_mode=DELETE";
                    journal.ExecuteNonQuery();
                }

                SqliteSafety.IntegrityResult check = SqliteSafety.CheckIntegrity(temporary, full: false);
                if (!check.Ok)
                    throw new InvalidDataException("The new backup failed its integrity check: " + string.Join("; ", check.Problems));

                File.Move(temporary, destination);
                return new Created(destination, new FileInfo(destination).Length, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                RemoveLeftovers(temporary);
            }
        }

        /// <summary>Deletes unfinished copies (*.part) a stopped server left in the folder.</summary>
        public static int RemoveUnfinished(string folder, string prefix)
        {
            if (!Directory.Exists(folder))
                return 0;

            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(folder, prefix + "-*.sqlite3.db.part*"))
            {
                File.Delete(path);
                removed++;
            }

            return removed;
        }

        private static void RemoveLeftovers(string temporary)
        {
            foreach (string path in new[] { temporary, temporary + "-journal", temporary + "-wal", temporary + "-shm" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
