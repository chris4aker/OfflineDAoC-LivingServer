using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace DOL.Database
{
    /// <summary>
    /// The connection string the world database is opened with, made safe whatever serverconfig.xml says (Living Server
    /// 1.1.2, technical audit 2026-11-03 section 2). The file is never rewritten: a player's own serverconfig.xml keeps
    /// every other setting, and the safe journal settings are applied each time the server opens the database.
    /// </summary>
    public static class SqliteSafety
    {
        /// <summary>The busy wait the native SQLite handler gets at least (milliseconds), as lometur's HearthDAoC uses.</summary>
        public const int MinimumBusyTimeoutMs = 10000;

        /// <summary>The command timeout (seconds) when the string names none: the server's own 60 s.</summary>
        public const int DefaultTimeoutSeconds = 60;

        /// <summary>What <see cref="Normalize"/> made of a connection string.</summary>
        public sealed record Result(string ConnectionString, string DatabasePath, IReadOnlyList<string> Changes);

        /// <summary>
        /// The journal is write-ahead (WAL) with Synchronous=Normal, connections are pooled and the busy wait is at least
        /// <see cref="MinimumBusyTimeoutMs"/>. Everything else in the string is kept as the player wrote it.
        /// <list type="bullet">
        /// <item>Journal Mode=Off with Synchronous=Off (the old code default) lets a crash or power cut in the middle of a
        /// save corrupt the world. WAL with Synchronous=Normal can lose at most the last moments before a power cut,
        /// never the file.</item>
        /// <item>Pooling: <c>SqliteObjectDatabase</c> opens game-loop reads without the writer gate only on pooled WAL
        /// connections. Unpooled, every query reopens the file and the last close checkpoints the WAL each time: measured
        /// 2026-11-03 (1,000 saves, 1,000 reads in a test): pooled WAL 0.7 s and 0.45 s, unpooled WAL 3.8 s and 1.0 s,
        /// unpooled journal-off (the old default) 1.4 s and 1.2 s.</item>
        /// </list>
        /// </summary>
        public static Result Normalize(string connectionString)
        {
            var builder = new SQLiteConnectionStringBuilder(connectionString ?? string.Empty);
            var changes = new List<string>();

            if (!builder.ContainsKey("Journal Mode") || builder.JournalMode != SQLiteJournalModeEnum.Wal)
            {
                changes.Add($"journal {Describe(builder, "Journal Mode")} -> WAL");
                builder.JournalMode = SQLiteJournalModeEnum.Wal;
            }

            if (!builder.ContainsKey("Synchronous") || builder.SyncMode != SynchronizationModes.Normal)
            {
                // Full is safer still but slower; Normal is SQLite's recommendation for WAL. Keep a player's Full.
                if (!(builder.ContainsKey("Synchronous") && builder.SyncMode == SynchronizationModes.Full))
                {
                    changes.Add($"synchronous {Describe(builder, "Synchronous")} -> Normal");
                    builder.SyncMode = SynchronizationModes.Normal;
                }
            }

            if (!builder.ContainsKey("Pooling") || !builder.Pooling)
            {
                changes.Add($"pooling {Describe(builder, "Pooling")} -> True");
                builder.Pooling = true;
            }

            if (!builder.ContainsKey("BusyTimeout") || builder.BusyTimeout < MinimumBusyTimeoutMs)
            {
                changes.Add($"busy timeout {Describe(builder, "BusyTimeout")} -> {MinimumBusyTimeoutMs} ms");
                builder.BusyTimeout = MinimumBusyTimeoutMs;
            }

            if (!builder.ContainsKey("Default Timeout"))
            {
                changes.Add($"default timeout (none) -> {DefaultTimeoutSeconds} s");
                builder.DefaultTimeout = DefaultTimeoutSeconds;
            }

            string path = string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:"
                ? null
                : Path.GetFullPath(builder.DataSource);
            return new Result(builder.ConnectionString, path, changes);
        }

        /// <summary>
        /// The string with a native busy wait of at least <see cref="MinimumBusyTimeoutMs"/> and nothing else changed
        /// (every SqliteObjectDatabase gets it). Without BusyTimeout, SQLite's own wait is 0: the PRAGMAs a connection
        /// runs while it opens fail at once with "database is locked" when another connection holds a lock for a moment.
        /// </summary>
        public static string WithBusyTimeout(string connectionString)
        {
            var builder = new SQLiteConnectionStringBuilder(connectionString ?? string.Empty);
            if (builder.ContainsKey("BusyTimeout") && builder.BusyTimeout >= MinimumBusyTimeoutMs)
                return connectionString;

            builder.BusyTimeout = MinimumBusyTimeoutMs;
            return builder.ConnectionString;
        }

        private static string Describe(SQLiteConnectionStringBuilder builder, string key) =>
            builder.TryGetValue(key, out object value) && value != null ? value.ToString() : "(none)";

        /// <summary>A short-lived connection beside the server's pool: never pooled, never the journal PRAGMA.</summary>
        public static SQLiteConnection OpenSide(string databasePath, bool readOnly, int busyTimeoutMs = MinimumBusyTimeoutMs)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath, Pooling = false, ReadOnly = readOnly, FailIfMissing = true,
                BusyTimeout = busyTimeoutMs,
                DefaultTimeout = busyTimeoutMs >= MinimumBusyTimeoutMs ? DefaultTimeoutSeconds : Math.Max(1, busyTimeoutMs / 1000)
            };
            var connection = new SQLiteConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>The database's journal mode as SQLite reports it ("wal", "delete", "off"...).</summary>
        public static string JournalMode(string databasePath)
        {
            using var connection = OpenSide(databasePath, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            return Convert.ToString(command.ExecuteScalar())?.ToLowerInvariant() ?? string.Empty;
        }

        /// <summary>What an integrity check found.</summary>
        public sealed record IntegrityResult(bool Ok, string Mode, IReadOnlyList<string> Problems, long Milliseconds);

        /// <summary>
        /// PRAGMA quick_check (or integrity_check with <paramref name="full"/>), read-only. quick_check reads every page and
        /// row but skips matching each index against its table, so it takes a fraction of the time. Up to 10 problems are
        /// returned.
        /// </summary>
        public static IntegrityResult CheckIntegrity(string databasePath, bool full)
        {
            var stopwatch = Stopwatch.StartNew();
            string mode = full ? "integrity_check" : "quick_check";
            var rows = new List<string>();
            try
            {
                using var connection = OpenSide(databasePath, readOnly: true);
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA {mode}(10)";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(Convert.ToString(reader.GetValue(0)));
            }
            catch (SQLiteException e) when (((int)e.ResultCode & 0xFF) is (int)SQLiteErrorCode.Corrupt or (int)SQLiteErrorCode.NotADb)
            {
                // So damaged that the check itself can't read it.
                rows.Clear();
                rows.Add(e.Message.Replace("\r", " ").Replace("\n", " "));
            }

            bool ok = rows.Count == 1 && string.Equals(rows[0], "ok", StringComparison.OrdinalIgnoreCase);
            return new IntegrityResult(ok, mode, ok ? Array.Empty<string>() : rows, stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Moves everything in the WAL into the database file and empties the WAL (best effort, at server stop), so a copy
        /// of the database file alone holds every save.
        /// </summary>
        public static string Checkpoint(string databasePath)
        {
            // A short wait: the server is stopping and the launcher gives it 10 s.
            using var connection = OpenSide(databasePath, readOnly: false, busyTimeoutMs: 2000);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            using var reader = command.ExecuteReader();
            // busy, log pages, checkpointed pages; busy=1 means a reader held it back (nothing is lost, the WAL stays).
            return reader.Read() ? $"busy={reader.GetValue(0)} wal_pages={reader.GetValue(1)} moved={reader.GetValue(2)}" : "no result";
        }
    }
}
