using System.Data.SQLite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace OfflineDaoc.Patcher;

/// <summary>
/// Brings an unmodified Offline DAoC folder up to a newer version (from 0.37 on, versions ship as patches on the 0.36 base).
///
///   dotnet OfflineDaoc.Patcher.dll --game &lt;game folder&gt; --patch &lt;unpacked patch folder&gt; [--check] [--overwrite-changed]
///
/// patch.json lists every file to add or replace (with the fingerprints the current file may have) and every file to
/// remove; files\ holds the new files; db-&lt;edition&gt;.json holds the world-data update for that edition. The patcher
///  1. finds out what the folder is (edition from EDITION.txt, version from the patch's identity file),
///  2. checks that the game, its server and both launchers are closed,
///  3. checks every file it will replace or remove still has a released fingerprint (a changed file stops the patch,
///     unless --overwrite-changed),
///  4. backs up everything it replaces (and the database) to patch-backups\&lt;version&gt;-&lt;time&gt;,
///  5. updates the world data in one transaction - a world row is changed or deleted only if it still has its released
///     values, so nothing the player's game wrote is overwritten, and save tables are never in the update - and records
///     it in offline_patch_log, so it runs once,
///  6. writes the files, the identity file last,
/// and puts everything back from the backup if any step fails.
///
/// Exit codes: 0 patched, 2 already up to date, 3 files changed by the player (listed), 4 not a folder this patch knows,
/// 5 the game or launcher is running, 1 failed (everything restored).
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        string? game = Arg(args, "--game"), patch = Arg(args, "--patch");
        if (game == null || patch == null)
        {
            Console.Error.WriteLine("usage: OfflineDaoc.Patcher --game <game folder> --patch <unpacked patch folder> [--check] [--overwrite-changed]");
            return 4;
        }
        try
        {
            return new PatchRun(Path.GetFullPath(game), Path.GetFullPath(patch),
                args.Contains("--check"), args.Contains("--overwrite-changed")).Run();
        }
        catch (PatchStop stop)
        {
            Console.Error.WriteLine(stop.Message);
            return stop.Code;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("The update failed and everything was put back as it was: " + e.Message);
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

internal sealed class PatchStop(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

internal sealed record PatchFile(string Path, string Sha256, long Bytes, string[] From);
internal sealed record RemoveFile(string Path, string[] From);
internal sealed record Identity(string Path, string To, string[] From);
internal sealed record PatchManifest(string Version, string Kind, string Title, Identity Identity, PatchFile[] Files,
    RemoveFile[] Remove, Dictionary<string, string>? Database, Dictionary<string, string>? EditionText);

internal sealed class PatchRun(string game, string patchDir, bool checkOnly, bool overwriteChanged)
{
    const string Absent = "absent";
    readonly List<string> _log = [];
    PatchManifest _patch = null!;

    public int Run()
    {
        _patch = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(Path.Combine(patchDir, "patch.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new PatchStop(4, "The patch has no patch.json.");
        if (!File.Exists(Path.Combine(game, "runtime", "OfflineDAoC.exe")))
            throw new PatchStop(4, $"No Offline DAoC game was found in {game}. Choose the folder that holds START OFFLINE DAOC.cmd.");
        string edition = File.Exists(Path.Combine(game, "EDITION.txt")) ? "plain" : "b";
        if (_patch.Kind == "new-client" && !Directory.Exists(Path.Combine(game, "new-client")))
            throw new PatchStop(4, $"{game} has no new client to update.");

        string identity = Hash(Full(_patch.Identity.Path));
        if (identity == _patch.Identity.To)
        {
            Say($"{_patch.Title}: already up to date in {game}.");
            return 2;
        }
        if (!_patch.Identity.From.Contains(identity))
            throw new PatchStop(4, $"The game in {game} is not a released version this update can bring up to date. " +
                                   "Choose 2 (New install) instead: it installs a fresh copy, and your progress can be brought over.");
        CheckClosed();

        // 3. every file this update replaces or removes must still be as released (or already updated)
        var changed = new List<string>();
        foreach (var f in _patch.Files)
        {
            string current = Hash(Full(f.Path));
            if (current != f.Sha256 && !f.From.Contains(current)) changed.Add(f.Path);
        }
        foreach (var r in _patch.Remove)
        {
            string current = Hash(Full(r.Path));
            if (current != Absent && !r.From.Contains(current)) changed.Add(r.Path);
        }
        if (changed.Count > 0 && !overwriteChanged)
        {
            string list = Path.Combine(game, $"UPDATE-{_patch.Version}-CHANGED-FILES.txt");
            File.WriteAllLines(list, changed);
            throw new PatchStop(3, $"{changed.Count} file(s) in {game} were changed since they were installed, so this update " +
                                   $"cannot be applied safely. They are listed in {list}.");
        }
        if (checkOnly)
        {
            Say($"{_patch.Title} can be applied to {game} ({edition} edition).");
            return 0;
        }

        // 4. back up
        string backup = Path.Combine(game, "patch-backups", $"{_patch.Version}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        var backedUp = new List<(string rel, bool existed)>();
        foreach (string rel in _patch.Files.Select(f => f.Path).Concat(_patch.Remove.Select(r => r.Path)).Append(_patch.Identity.Path).Distinct())
        {
            string src = Full(rel);
            bool existed = File.Exists(src);
            if (existed)
            {
                string dst = Path.Combine(backup, "files", rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, true);
            }
            backedUp.Add((rel, existed));
        }
        string database = Path.Combine(game, "runtime", "data", "opendaoc.sqlite3.db");
        string? dbUpdate = _patch.Database != null && _patch.Database.TryGetValue(edition, out var name) ? Path.Combine(patchDir, name) : null;
        string dbBackup = Path.Combine(backup, "opendaoc.sqlite3.db");
        if (dbUpdate != null)
            BackupDatabase(database, dbBackup);
        File.WriteAllLines(Path.Combine(backup, "BACKUP.txt"), [
            $"Backup made before {_patch.Title} on {DateTime.Now:yyyy-MM-dd HH:mm}.",
            "files\\ holds every file the update replaced or removed; opendaoc.sqlite3.db is the database before the update.",
            "To undo the update by hand: close the game, copy files\\ back over the game folder and the database back to runtime\\data."]);

        try
        {
            // 5. world data (once per version)
            if (dbUpdate != null)
                new WorldUpdate(database, dbUpdate, _patch.Version, Say).Apply();
            // 6. files - the identity file last, so an interrupted update is never taken for a finished one
            var ordered = _patch.Files.Where(f => f.Path != _patch.Identity.Path).Concat(_patch.Files.Where(f => f.Path == _patch.Identity.Path));
            foreach (var f in ordered)
            {
                string target = Full(f.Path);
                if (Hash(target) == f.Sha256) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(patchDir, "files", f.Path.Replace('/', Path.DirectorySeparatorChar)), target, true);
                if (Hash(target) != f.Sha256) throw new IOException($"{f.Path} did not copy correctly.");
                if (Environment.GetEnvironmentVariable("OFFLINEDAOC_PATCH_FAIL_AFTER") == f.Path)
                    throw new IOException("test failure requested after " + f.Path);
            }
            foreach (var r in _patch.Remove)
            {
                string target = Full(r.Path);
                if (File.Exists(target)) File.Delete(target);
                RemoveEmptyFolders(Path.GetDirectoryName(target)!);
            }
            if (edition == "plain" && _patch.EditionText != null && _patch.EditionText.TryGetValue("plain", out var text))
                File.WriteAllText(Path.Combine(game, "EDITION.txt"), text + Environment.NewLine);
        }
        catch
        {
            Say("Something went wrong - putting everything back as it was...");
            foreach (var (rel, existed) in backedUp)
            {
                string target = Full(rel);
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(backup, "files", rel.Replace('/', Path.DirectorySeparatorChar)), target, true);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
            if (dbUpdate != null && File.Exists(dbBackup))
            {
                SQLiteConnection.ClearAllPools();
                File.Copy(dbBackup, database, true);
                foreach (string side in new[] { database + "-wal", database + "-shm" })
                    if (File.Exists(side)) File.Delete(side);
            }
            throw;
        }
        File.WriteAllLines(Path.Combine(backup, "UPDATE-REPORT.txt"), _log);
        Say($"{_patch.Title} installed in {game}. The files it replaced are backed up in {backup}.");
        return 0;
    }

    string Full(string rel) => Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar));

    void Say(string line)
    {
        _log.Add(line);
        Console.WriteLine(line);
    }

    static string Hash(string path)
    {
        if (!File.Exists(path)) return Absent;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    void RemoveEmptyFolders(string folder)
    {
        string root = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        while (folder.Length > root.Length && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
            folder = Path.GetDirectoryName(folder)!;
        }
    }

    static void BackupDatabase(string source, string destination)
    {
        using var from = new SQLiteConnection($"Data Source={source};Pooling=False");
        from.Open();
        using var to = new SQLiteConnection($"Data Source={destination};Pooling=False");
        to.Open();
        from.BackupDatabase(to, "main", "main", -1, null, 0);
    }

    void CheckClosed()
    {
        if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == 10300))
            throw new PatchStop(5, "A local DAoC server is running. Click STOP SERVER, close the launcher and the game, then run the update again.");
        string root = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                if (process.ProcessName is not ("CoreServer" or "OfflineDAoC" or "game" or "camelot" or "connect" or "Godot_v4.7.2-stable_mono_win64"))
                    continue;
                string? file = null;
                try { file = process.MainModule?.FileName; } catch { }
                if (file == null || file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || process.ProcessName == "CoreServer")
                    throw new PatchStop(5, "Close the Offline DAoC launcher, the server and the game, then run the update again.");
            }
    }
}

/// <summary>
/// Applies db-&lt;edition&gt;.json: new tables and columns, then per table the released rows that were added, changed or
/// removed. A change or removal only happens where the player's row still has its released values (volatile columns
/// such as LastTimeRowUpdated are ignored), so rows the running game has rewritten are left as they are.
/// </summary>
internal sealed class WorldUpdate(string database, string updateFile, string version, Action<string> say)
{
    public void Apply()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(updateFile));
        var root = doc.RootElement;
        using var c = new SQLiteConnection($"Data Source={database};Pooling=False");
        c.Open();
        Exec(c, "CREATE TABLE IF NOT EXISTS offline_patch_log (Version TEXT PRIMARY KEY, AppliedUtc TEXT, Notes TEXT)");
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT count(*) FROM offline_patch_log WHERE Version=@v";
            check.Parameters.AddWithValue("@v", version);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            {
                say($"World data: already updated to {version}.");
                return;
            }
        }
        using var tx = c.BeginTransaction();
        foreach (var t in root.GetProperty("NewTables").EnumerateArray())
        {
            Exec(c, t.GetProperty("Create").GetString()!, tx, ignoreExists: true);
            if (t.TryGetProperty("Indexes", out var indexes))
                foreach (var index in indexes.EnumerateArray())
                    Exec(c, index.GetString()!, tx, ignoreExists: true);
        }
        foreach (var col in root.GetProperty("NewColumns").EnumerateArray())
        {
            string table = col.GetProperty("Table").GetString()!, name = col.GetProperty("Column").GetString()!;
            if (!Columns(c, table, tx).Contains(name, StringComparer.OrdinalIgnoreCase))
                Exec(c, $"ALTER TABLE {Q(table)} ADD COLUMN {Q(name)} {col.GetProperty("Decl").GetString()}", tx);
        }
        var ignored = root.GetProperty("IgnoreColumns").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long added = 0, changed = 0, removed = 0, kept = 0, already = 0;
        var notes = new List<string>();
        foreach (var t in root.GetProperty("Tables").EnumerateArray())
        {
            string table = t.GetProperty("Table").GetString()!;
            string[] cols = t.GetProperty("Columns").EnumerateArray().Select(e => e.GetString()!).ToArray();
            string[] keys = t.GetProperty("Key").EnumerateArray().Select(e => e.GetString()!).ToArray();
            int[] keyIdx = keys.Select(k => Array.IndexOf(cols, k)).ToArray();
            var tableIgnore = t.TryGetProperty("IgnoreCompare", out var ic) ? ic.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            int[] cmpIdx = Enumerable.Range(0, cols.Length).Where(i => !ignored.Contains(cols[i]) && !tableIgnore.Contains(cols[i])).ToArray();
            string where(int[] idx, string prefix) => string.Join(" AND ", idx.Select(i => $"{Q(cols[i])} IS @{prefix}{i}"));
            long tableKept = 0;
            // a row that already has this version's values (an earlier build of the same update ran here) is up to date
            bool has(int[] idx, JsonElement row)
            {
                using var q = Cmd(c, tx, $"SELECT count(*) FROM {Q(table)} WHERE {where(idx, "h")}");
                Bind(q, "h", row, idx);
                return Convert.ToInt64(q.ExecuteScalar()) > 0;
            }

            foreach (var row in t.GetProperty("Insert").EnumerateArray())
            {
                using var cmd = Cmd(c, tx, $"INSERT OR IGNORE INTO {Q(table)} ({string.Join(",", cols.Select(Q))}) VALUES ({string.Join(",", cols.Select((_, i) => "@n" + i))})");
                Bind(cmd, "n", row);
                if (cmd.ExecuteNonQuery() == 1) added++;
                else if (keyIdx.Length > 0 && keyIdx.All(i => i >= 0) && has(keyIdx, row)) already++;
                else tableKept++;
            }
            foreach (var row in t.GetProperty("Update").EnumerateArray())
            {
                var oldRow = row.GetProperty("Old");
                var newRow = row.GetProperty("New");
                SQLiteCommand cmd;
                if (row.TryGetProperty("Set", out var set))
                {
                    // field update: only the fields this version changes, where they still have their released values
                    // (the running server rewrites other fields itself, e.g. monster size, level and stats)
                    int[] setIdx = set.EnumerateArray().Select(e => Array.IndexOf(cols, e.GetString()!)).ToArray();
                    if (setIdx.Length == 0) continue;
                    int[] matchIdx = keyIdx.Concat(setIdx).Distinct().ToArray();
                    cmd = Cmd(c, tx, $"UPDATE {Q(table)} SET {string.Join(",", setIdx.Select(i => $"{Q(cols[i])}=@n{i}"))} WHERE {where(matchIdx, "o")}");
                    Bind(cmd, "n", newRow, setIdx);
                    Bind(cmd, "o", oldRow, matchIdx);
                }
                else
                {
                    cmd = Cmd(c, tx, $"UPDATE {Q(table)} SET {string.Join(",", cols.Select((n, i) => $"{Q(n)}=@n{i}"))} WHERE {where(cmpIdx, "o")}");
                    Bind(cmd, "n", newRow);
                    Bind(cmd, "o", oldRow, cmpIdx);
                }
                using (cmd)
                {
                    if (cmd.ExecuteNonQuery() > 0) changed++;
                    else if (row.TryGetProperty("Set", out var done) &&
                             has(keyIdx.Concat(done.EnumerateArray().Select(e => Array.IndexOf(cols, e.GetString()!))).Distinct().ToArray(), newRow)) already++;
                    else tableKept++;
                }
            }
            foreach (var row in t.GetProperty("Delete").EnumerateArray())
            {
                using var cmd = Cmd(c, tx, $"DELETE FROM {Q(table)} WHERE {where(cmpIdx, "o")}");
                Bind(cmd, "o", row, cmpIdx);
                if (cmd.ExecuteNonQuery() > 0) removed++; else tableKept++;
            }
            if (tableKept > 0) notes.Add($"{table}: {tableKept} row(s) left as the game had them");
            kept += tableKept;
        }
        using (var log = Cmd(c, tx, "INSERT INTO offline_patch_log (Version, AppliedUtc, Notes) VALUES (@v, @t, @n)"))
        {
            log.Parameters.AddWithValue("@v", version);
            log.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
            log.Parameters.AddWithValue("@n", string.Join("; ", notes));
            log.ExecuteNonQuery();
        }
        tx.Commit();
        say($"World data updated to {version}: {added} added, {changed} changed, {removed} removed, {already} already up to date, " +
            $"{kept} left as the game had them.");
        foreach (string note in notes) say("  " + note);
    }

    static string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    static SQLiteCommand Cmd(SQLiteConnection c, SQLiteTransaction tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    static void Exec(SQLiteConnection c, string sql, SQLiteTransaction? tx = null, bool ignoreExists = false)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = !ignoreExists ? sql : sql
            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
            .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
            .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase);
        cmd.ExecuteNonQuery();
    }

    static List<string> Columns(SQLiteConnection c, string table, SQLiteTransaction tx)
    {
        using var cmd = Cmd(c, tx, $"PRAGMA table_info({Q(table)})");
        using var r = cmd.ExecuteReader();
        var result = new List<string>();
        while (r.Read()) result.Add(r.GetString(1));
        return result;
    }

    static void Bind(SQLiteCommand cmd, string prefix, JsonElement row, int[]? only = null)
    {
        var values = row.EnumerateArray().ToArray();
        foreach (int i in only ?? Enumerable.Range(0, values.Length).ToArray())
            cmd.Parameters.AddWithValue($"@{prefix}{i}", Value(values[i]));
    }

    // JSON values from make_patch: numbers as written by SQLite (integers stay integers), text, null, {"$b64": blob}
    static object Value(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => DBNull.Value,
        JsonValueKind.String => v.GetString()!,
        JsonValueKind.Number => v.TryGetInt64(out long l) && !v.GetRawText().Contains('.') && !v.GetRawText().Contains('e') && !v.GetRawText().Contains('E')
            ? l : v.GetDouble(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Object => Convert.FromBase64String(v.GetProperty("$b64").GetString()!),
        _ => throw new InvalidDataException("Unexpected value in the world update: " + v.GetRawText()),
    };
}
