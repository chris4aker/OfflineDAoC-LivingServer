using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Temporary folders for tests that open SQLite files.
/// Each test gets its own folder, so nothing it leaves behind can collide with another test. Cleanup clears the
/// SQLite pools, then retries the delete: on Windows a connection that is still closing (a pooled one, or one waiting
/// for its finalizer) keeps the file open for a moment, and File.Delete fails with "being used by another process"
/// (PR #56, job 124920974517). A folder that still can't be deleted is left with a warning: these tests check
/// behaviour, not deletion.
/// </summary>
internal static class TemporarySqlite
{
    private const int Attempts = 10;

    /// <summary>Creates %TEMP%\&lt;prefix&gt;-&lt;guid&gt; and returns it.</summary>
    public static string NewFolder(string prefix)
    {
        string folder = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A database path in a new folder of its own: %TEMP%\&lt;prefix&gt;-&lt;guid&gt;\&lt;prefix&gt;.sqlite3.</summary>
    public static string NewDatabasePath(string prefix) => Path.Combine(NewFolder(prefix), prefix + ".sqlite3");

    /// <summary>Deletes the folder <see cref="NewDatabasePath"/> made for this database, with its -wal and -shm files.</summary>
    public static void DeleteDatabase(string databasePath) => DeleteFolder(Path.GetDirectoryName(databasePath));

    /// <summary>Deletes a test folder directly under %TEMP% (never anything else), retrying while SQLite lets go of it.</summary>
    public static void DeleteFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
            return;
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(full), temp, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Not a test folder directly under {temp}: {full}", nameof(folder));

        Exception last = null;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(full))
                    Directory.Delete(full, true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                last = e;
            }
            // Let connections that were never disposed finalize, then wait a little longer each time (about 2.75 s in all).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(50 * (attempt + 1));
        }
        TestContext.Progress.WriteLine($"Warning: left the test folder {full} after {Attempts} tries: {last?.Message}");
    }
}
