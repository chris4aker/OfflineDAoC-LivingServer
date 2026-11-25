using System.Data.SQLite;
using System.Reflection;
using System.Text.Json;

namespace OfflineDaoc.Updates;

// "Install into a new test folder" (issue #637): a fresh Living Server beside the current install, for a test build,
// that never touches the current install's world or saves.
//
// Setup's own fresh install downloads the multi-gigabyte Offline DAoC release and runs its official update scripts,
// which is too much to repeat from the launcher, so this takes the smallest safe route the release builder already
// uses (source/tools/build_release_037.py):
// 1. the game files are copied from the current install, leaving out everything personal, run-state or saved
//    (Include: logs, backups, account.txt, bot-goals and bot-world files, the downloaded updates, package folders,
//    diagnostics switches, and the whole runtime\data folder);
// 2. the world database is copied with SQLite's backup and cleaned as build_release_037.py's clean_world does: every
//    table of progress-policy.json emptied, the starting guilds kept, houses unowned, keeps, doors and relics back to
//    release-world-defaults.json, every Living Server switch at release-server-settings.json, then checked: no
//    account, character, bot or saved row is left, or the new folder is deleted;
// 3. the test build is downloaded and checked there (the same SHA-256 digest check as every update) and installed by
//    the usual hand-over (apply-update.ps1 runs that folder's INSTALL.cmd), which then opens that folder's launcher.
// It refuses while the server answers on port 10300, and the new folder must be a new (or empty) folder directly in
// the current install's parent folder.
public static class TestFolderInstall
{
    /// <summary>Folders never copied, wherever they are (build_release_037.py EXCLUDED_DIRS).</summary>
    public static readonly string[] ExcludedFolders =
    [
        "logs", "backups", "dxvk-staging", "deployment-backups", "keep-relic-reset-backups", "progress-backups",
        "screenshots", ".git", "testresults", "dumps",
    ];

    /// <summary>Files never copied (build_release_037.py EXCLUDED_NAMES and FORBIDDEN_NAMES, and the launcher's own).</summary>
    public static readonly string[] ExcludedNames =
    [
        "account.txt", "claude_test_account.txt", "dev_mode.txt", "rvr-world.json", "bot-goals.json", "bot-world.json",
        "bot-world.request", "bot-ai-delay.json", "realm-events.request.json", "realm-events.result.json",
        "realm-event-records.sqlite3", "realm-event-records.sqlite3-wal", "realm-event-records.sqlite3-shm",
        "errorlog.txt", "debug.log", "chat.log", "user.dat", "unins000.exe", "unins000.dat", "uninstdaoc.exe",
        "d3d9.dll", "dxvk.conf", "game.dll_d3d9.log", "memory-census.on", "gc-snapshots.on", "gc-diagnostics.off",
        "host-for-friends.json", "host-for-friends-firewall.cmd", "host-for-friends-firewall-remove.cmd",
        "join-friend.json",
    ];

    /// <summary>Endings never copied: logs, dumps, backups, databases and their journals, diagnostics switches.</summary>
    public static readonly string[] ExcludedEndings =
    [
        ".log", ".dmp", ".gcdump", ".bak", ".sqlite3.db", ".sqlite3.db-wal", ".sqlite3.db-shm", ".sqlite3.db-journal",
        ".on", ".off",
    ];

    public const string WorldDatabase = "runtime/data/opendaoc.sqlite3.db";

    /// <summary>The default name of the new folder: the current one's with "-test" after it.</summary>
    public static string DefaultFolderName(string gameFolder) =>
        Path.GetFileName(Path.GetFullPath(gameFolder).TrimEnd('\\', '/')) + "-test";

    /// <summary>
    /// Whether a file of the game folder (path relative to it, either slash) is copied to the test folder. The world
    /// database is never copied this way: a cleaned copy is made instead (CleanWorld).
    /// </summary>
    public static bool Include(string relative)
    {
        string[] parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant()).ToArray();
        if (parts.Length == 0)
            return false;
        string name = parts[^1];
        foreach (string folder in parts[..^1])
        {
            if (ExcludedFolders.Contains(folder) || folder.Contains("before-") || folder.Contains("pre-route-"))
                return false;
        }
        if (parts.Length > 1)
        {
            // The packages a player or the launcher put in the game folder (LivingServer-<build>), with their backups.
            if (parts[0].StartsWith("livingserver-", StringComparison.Ordinal))
                return false;
            if (parts[0] == "runtime" && parts[1] is "data" or "updates")
                return false; // the world and saves (rebuilt below); downloaded updates and update settings
            if (parts[0] == "new-client" && parts[1] == "save")
                return false; // the new client's own settings
        }
        return !ExcludedNames.Contains(name) && !name.Contains("before-") && !ExcludedEndings.Any(name.EndsWith);
    }

    /// <summary>The files to copy, relative to the game folder ('/' separated), in name order.</summary>
    public static List<string> CopyPlan(string gameFolder)
    {
        string root = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        var files = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (Include(relative))
                files.Add(relative);
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>
    /// The new folder's full path, or why it can't be used: a plain folder name (or a path) whose parent is the current
    /// install's parent folder, not the install itself, and not a folder that has anything in it.
    /// </summary>
    public static string? CheckTarget(string gameFolder, string nameOrPath, out string target)
    {
        string game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        string parent = Path.GetDirectoryName(game) ?? "";
        target = "";
        string text = (nameOrPath ?? "").Trim();
        if (text.Length == 0)
            return "Type a name for the new folder.";
        if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || text.Contains('*') || text.Contains('?'))
            return "That isn't a folder name Windows can use.";
        string full;
        try { full = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(parent, text)).TrimEnd('\\', '/'); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That isn't a folder name Windows can use.";
        }
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase))
            return $"The test folder must be directly in {parent}, beside the current install.";
        if (string.Equals(full, game, StringComparison.OrdinalIgnoreCase))
            return "That is the current install. Choose a new folder name.";
        if (File.Exists(full))
            return $"{Path.GetFileName(full)} is a file. Choose a new folder name.";
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            return $"{Path.GetFileName(full)} already has files in it. Choose a new folder name.";
        target = full;
        return null;
    }

    /// <summary>Why nothing may start now, or null: the server answering on port 10300 (ServerPortProbe), or other parts running.</summary>
    public static string? RunningRefusal(Func<bool> serverListening, IReadOnlyCollection<string>? running = null)
    {
        if (serverListening())
            return "A server is running on port " + Launcher.ServerPortProbe.GameServerPort + ". Stop it first: a test folder is never made while a server runs.";
        if (running is { Count: > 0 })
            return $"Close {string.Join(", ", running)} first: a test folder is never made while the game runs.";
        return null;
    }

    /// <summary>
    /// Makes the test folder: the game files copied (CopyPlan), a cleaned world, update settings with test builds on,
    /// then the test build downloaded, checked and unpacked there. Returns that folder's updater and the prepared
    /// update, for the caller to hand over (StartHandoff). On any failure the new folder is deleted and the exception
    /// passes through. The current install is only read.
    /// </summary>
    public static (LauncherUpdater Updater, PreparedUpdate Update) Create(string gameFolder, string target, ReleaseInfo release,
        IReleaseWeb web, Func<bool> serverListening, Action<string>? status, Action<long, long>? progress, CancellationToken cancel)
    {
        string game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        string? refused = RunningRefusal(serverListening) ?? CheckTarget(game, target, out target);
        if (refused != null)
            throw new PackageRefusedException(refused);
        if (release.Version == null || release.Package == null)
            throw new PackageRefusedException("Not a Living Server release with a package: " + release.TagName);
        string sourceDb = Path.Combine(game, WorldDatabase.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourceDb))
            throw new PackageRefusedException("The current install has no world database (" + WorldDatabase + ") to start the test world from.");

        List<string> plan = CopyPlan(game);
        long bytes = plan.Sum(f => new FileInfo(Path.Combine(game, f)).Length) + new FileInfo(sourceDb).Length + (release.Package.Size * 3);
        string? drive = Path.GetPathRoot(target);
        if (drive != null)
        {
            long free = new DriveInfo(drive).AvailableFreeSpace;
            if (free < bytes + (1L << 30))
                throw new PackageRefusedException($"The test folder needs about {bytes / 1073741824.0:0.0} GB and {drive} has {free / 1073741824.0:0.0} GB free.");
        }

        bool existed = Directory.Exists(target);
        Directory.CreateDirectory(target);
        try
        {
            long done = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                string from = Path.Combine(game, plan[i]);
                string to = Path.Combine(target, plan[i].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, false);
                long length = new FileInfo(from).Length;
                if (new FileInfo(to).Length != length)
                    throw new IOException("Copying " + plan[i] + " failed verification.");
                done += length;
                if (i % 50 == 0)
                {
                    status?.Invoke($"Copying the game files: {i + 1:N0} of {plan.Count:N0}");
                    progress?.Invoke(done, bytes);
                }
            }

            status?.Invoke("Making a clean world database");
            string db = Path.Combine(target, WorldDatabase.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(db)!);
            CopyDatabase(sourceDb, db);
            CleanWorld(db);

            var updater = new LauncherUpdater(target, web);
            UpdateSettings current = UpdateSettings.Load(Path.Combine(game, "runtime", "updates"));
            updater.Settings.CheckAutomatically = current.CheckAutomatically;
            updater.Settings.OfferTestBuilds = true;
            updater.SaveSettings();
            updater.Log.Write($"Test folder made from {game}: {plan.Count:N0} game files copied, a clean world database; installing {release.TagName}.");

            status?.Invoke("Downloading " + release.Version.DisplayName);
            PreparedUpdate prepared = updater.PrepareFromRelease(release, progress, cancel);
            return (updater, prepared);
        }
        catch
        {
            TryDelete(target, existed);
            throw;
        }
    }

    private static void TryDelete(string target, bool keepFolder)
    {
        try
        {
            if (!Directory.Exists(target))
                return;
            if (keepFolder)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(target))
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, true);
                    else File.Delete(entry);
                }
            }
            else
                Directory.Delete(target, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the player to delete; it holds no saves.
        }
    }

    /// <summary>A consistent copy of the world database through SQLite's backup (the source is opened read-only).</summary>
    public static void CopyDatabase(string source, string target)
    {
        using var from = new SQLiteConnection($"Data Source={source};Version=3;Read Only=True;Pooling=False;Default Timeout=30");
        using var to = new SQLiteConnection($"Data Source={target};Version=3;Pooling=False;Default Timeout=30");
        from.Open();
        to.Open();
        from.BackupDatabase(to, "main", "main", -1, null, 0);
    }

    // The world's own starting guilds; any other guild was made by a player (build_release_037.py STARTER_GUILDS).
    public static readonly string[] StarterGuilds =
        ["62d4d73a-bb6b-4c5d-9d5e-2664c3fe0284", "1631acab-4626-496f-a96e-48da87d75a89", "c9b97783-7041-4fd5-9919-52f73189c0a8"];

    /// <summary>
    /// Cleans a copied world database as build_release_037.py's clean_world does, then checks it. Throws
    /// PackageRefusedException when anything saved is left.
    /// </summary>
    public static void CleanWorld(string database)
    {
        ProgressPolicy policy = ProgressPolicy.Embedded();
        using var con = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=30");
        con.Open();
        Exec(con, "PRAGMA foreign_keys=OFF");
        var tables = new HashSet<string>(Strings(con, "SELECT name FROM sqlite_master WHERE type='table'"), StringComparer.OrdinalIgnoreCase);
        var cleared = new List<string>();
        using (SQLiteTransaction tx = con.BeginTransaction())
        {
            foreach (string table in policy.AllCleared)
            {
                if (tables.Contains(table) && table is not ("Guild" or "GuildRank"))
                {
                    Exec(con, $"DELETE FROM \"{table}\"");
                    cleared.Add(table);
                }
            }
            string marks = string.Join(",", StarterGuilds.Select(g => "'" + g + "'"));
            if (tables.Contains("Guild"))
            {
                Exec(con, "UPDATE Guild SET RealmPoints=0,BountyPoints=0,Bank=0,MeritPoints=0,Webpage='',Email='',Motd='',oMotd='',HaveGuildHouse=0,GuildHouseNumber=0");
                Exec(con, $"DELETE FROM Guild WHERE GuildID NOT IN ({marks})");
            }
            if (tables.Contains("GuildRank"))
                Exec(con, $"DELETE FROM GuildRank WHERE GuildID NOT IN ({marks})");
            if (tables.Contains("DBHouse"))
                Exec(con, "UPDATE DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' WHERE COALESCE(OwnerID,'')<>''");
            using (JsonDocument defaults = Resource("release-world-defaults.json"))
            {
                if (tables.Contains("Keep") && defaults.RootElement.TryGetProperty("Keep", out JsonElement keeps))
                {
                    foreach (JsonProperty keep in keeps.EnumerateObject())
                    {
                        Exec(con, "UPDATE Keep SET Realm=@r,Level=@l,ClaimedGuildName='' WHERE KeepID=@id",
                            ("@r", keep.Value.GetProperty("Realm").GetInt32()), ("@l", keep.Value.GetProperty("Level").GetInt32()), ("@id", int.Parse(keep.Name)));
                        if (keep.Value.TryGetProperty("BaseLevel", out JsonElement baseLevel))
                            Exec(con, "UPDATE Keep SET BaseLevel=@b WHERE KeepID=@id", ("@b", baseLevel.GetInt32()), ("@id", int.Parse(keep.Name)));
                    }
                }
                if (tables.Contains("Door") && defaults.RootElement.TryGetProperty("Door", out JsonElement doors))
                {
                    foreach (JsonProperty door in doors.EnumerateObject())
                        Exec(con, "UPDATE Door SET Health=@h,State=@s WHERE Door_ID=@id",
                            ("@h", door.Value.GetProperty("Health").GetInt32()), ("@s", door.Value.GetProperty("State").GetInt32()), ("@id", door.Name));
                }
            }
            if (tables.Contains("Relic"))
                Exec(con, "UPDATE Relic SET Realm=OriginalRealm,LastRealm=OriginalRealm WHERE OriginalRealm IN (1,2,3)");
            if (tables.Contains("ServerProperty"))
            {
                Exec(con, "UPDATE ServerProperty SET Value='1' WHERE lower(`Key`) IN ('xp_rate','bot_xp_rate')");
                Exec(con, "UPDATE ServerProperty SET Value='True' WHERE lower(`Key`)='allow_auto_account_creation'");
                using JsonDocument settings = Resource("release-server-settings.json");
                foreach (JsonProperty setting in settings.RootElement.GetProperty("Settings").EnumerateObject())
                    Exec(con, "UPDATE ServerProperty SET Value=@v, DefaultValue=@v WHERE lower(`Key`)=lower(@k)",
                        ("@v", setting.Value.GetString()), ("@k", setting.Name));
            }
            if (tables.Contains("offline_local_options"))
                Exec(con, "INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false')");
            if (tables.Contains("offline_population_settings"))
            {
                Exec(con, "UPDATE offline_population_settings SET Value='0' WHERE Key='ActiveTarget'");
                Exec(con, "UPDATE offline_population_settings SET Value='true' WHERE Key='PopulationEnabled'");
                Exec(con, "UPDATE offline_population_settings SET Value='15' WHERE Key='StartupRampMinutes'");
            }
            if (tables.Contains("sqlite_sequence"))
            {
                foreach (string table in cleared)
                    Exec(con, "DELETE FROM sqlite_sequence WHERE name=@n", ("@n", table));
            }
            tx.Commit();
        }
        Exec(con, "PRAGMA journal_mode=DELETE");
        Exec(con, "VACUUM");

        // The check: nothing saved is left.
        string check = Strings(con, "PRAGMA quick_check").FirstOrDefault() ?? "";
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            throw new PackageRefusedException("The cleaned world database failed SQLite's check: " + check);
        var left = new List<string>();
        foreach (string table in policy.AllCleared)
        {
            if (table is "Guild" or "GuildRank" or "offline_local_options" || !tables.Contains(table))
                continue;
            if (Count(con, $"SELECT COUNT(*) FROM \"{table}\"") != 0)
                left.Add(table);
        }
        if (tables.Contains("Guild") && Count(con, $"SELECT COUNT(*) FROM Guild WHERE GuildID NOT IN ({string.Join(",", StarterGuilds.Select(g => "'" + g + "'"))})") != 0)
            left.Add("Guild");
        if (left.Count > 0)
            throw new PackageRefusedException("Saved rows were left in the test world (" + string.Join(", ", left) + "), so it was deleted.");
    }

    /// <summary>The saved-progress tables of source/tools/OfflineDaoc.ProgressImport/progress-policy.json.</summary>
    public sealed class ProgressPolicy
    {
        public List<string> ProgressTables { get; set; } = [];
        public List<string> OptionalProgressTables { get; set; } = [];
        public List<string> ProgressFiles { get; set; } = [];
        public List<string> ClearTables { get; set; } = [];

        /// <summary>Every table clean_world empties: progress, optional progress, the clear tables, and FactionAggroLevel.</summary>
        public IEnumerable<string> AllCleared =>
            ProgressTables.Concat(OptionalProgressTables).Concat(ClearTables).Append("FactionAggroLevel").Distinct(StringComparer.OrdinalIgnoreCase);

        public static ProgressPolicy Embedded()
        {
            using Stream s = Open("progress-policy.json");
            return JsonSerializer.Deserialize<ProgressPolicy>(s) ?? throw new InvalidDataException("progress-policy.json is empty.");
        }
    }

    private static Stream Open(string name) =>
        typeof(TestFolderInstall).Assembly.GetManifestResourceStream("OfflineDaoc.Updates." + name)
        ?? throw new InvalidOperationException("The launcher has no " + name + " inside it.");

    private static JsonDocument Resource(string name)
    {
        using Stream s = Open(name);
        return JsonDocument.Parse(s);
    }

    private static void Exec(SQLiteConnection con, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = new SQLiteCommand(sql, con);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static long Count(SQLiteConnection con, string sql)
    {
        using var cmd = new SQLiteCommand(sql, con);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static List<string> Strings(SQLiteConnection con, string sql)
    {
        using var cmd = new SQLiteCommand(sql, con);
        using SQLiteDataReader reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetValue(0)?.ToString() ?? "");
        return result;
    }
}
