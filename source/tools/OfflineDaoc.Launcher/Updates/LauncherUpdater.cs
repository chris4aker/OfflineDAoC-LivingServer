using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

// The Living Server build this launcher belongs to. The build stamps it from the server's ForkBuildInfo.BuildName
// (OfflineDaoc.Launcher.csproj), so the launcher and the server it ships with can't name different builds.
public static class LauncherBuild
{
    public static string Name { get; } = typeof(LauncherBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "LivingServerBuild")?.Value ?? "";

    public static BuildVersion? Version => BuildVersion.Parse(Name);
}

/// <summary>The launcher's update log, runtime\logs\launcher-updates.log: every step of a check and an update.</summary>
public sealed class UpdateLog
{
    private readonly object _gate = new();

    public UpdateLog(string path) { Path = path; }

    public string Path { get; }

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The log is a help, never a reason to stop.
            }
        }
    }
}

/// <summary>A checked package, unpacked into the game folder, ready for its INSTALL.cmd.</summary>
public sealed record PreparedUpdate(BuildVersion Build, string ZipPath, string PackageFolder);

// Checks for and applies Living Server updates for one game folder. Used by the launcher's window (MainForm.Updates)
// and by its headless test switch (--apply-update). The steps:
// - Check: one request to GitHub's releases API, at most once a day unless the player asks (UpdatePolicy).
// - Prepare: the package downloaded to runtime\updates, checked against GitHub's SHA-256 digest and PackageCheck,
//   unpacked to runtime\updates\staging, then moved into the game folder as LivingServer-<build> (where a player
//   puts it by hand, so its ROLLBACK.cmd is where the docs say).
// - Hand off: apply-update.ps1 waits for the launcher to close, runs the package's INSTALL.cmd and opens the launcher.
// Nothing runs while the server, the game or another launcher does, and the player's saves are never touched.
public sealed class LauncherUpdater
{
    public const string UserAgent = "OfflineDAoC-LivingServer-Launcher";
    public const string ResultFile = "last-update.txt";

    /// <summary>The programs whose running blocks an update: the server, both clients, and another launcher.</summary>
    public static readonly string[] BlockingProcesses =
        ["CoreServer", "DOLServer", "game", "camelot", "connect", "Godot_v4.7.2-stable_mono_win64", "OfflineDAoC"];

    private readonly IReleaseWeb _web;

    public LauncherUpdater(string gameFolder, IReleaseWeb web, UpdateLog? log = null)
    {
        GameFolder = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
        _web = web;
        Log = log ?? new UpdateLog(Path.Combine(GameFolder, "runtime", "logs", "launcher-updates.log"));
        Settings = UpdateSettings.Load(UpdatesFolder);
    }

    public string GameFolder { get; }

    public string UpdatesFolder => Path.Combine(GameFolder, "runtime", "updates");

    public UpdateLog Log { get; }

    public UpdateSettings Settings { get; }

    /// <summary>The game folder a launcher belongs to: the parent of the runtime folder its server lives in.</summary>
    public static string GameFolderFor(string serverRoot) =>
        Path.GetDirectoryName(Path.GetFullPath(serverRoot).TrimEnd('\\', '/')) ?? serverRoot;

    public void SaveSettings()
    {
        try { Settings.Save(UpdatesFolder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Couldn't save the update settings: " + e.Message); }
    }

    /// <summary>
    /// Asks GitHub for the newest release when the rules allow (always when asked). Returns the release to offer, or
    /// null. problem says why nothing could be checked; an automatic check stays silent about it.
    /// </summary>
    public ReleaseInfo? Check(BuildVersion? installed, bool asked, DateTime nowUtc, out string? problem)
    {
        problem = null;
        if (!UpdatePolicy.ShouldCheck(Settings, nowUtc, asked))
            return null;
        if (!asked)
        {
            Settings.LastCheckUtc = nowUtc;
            SaveSettings();
        }
        Log.Write($"Checking for a newer Living Server than {installed?.Name ?? "(unknown build)"}{(asked ? " (asked)" : "")}: GET {ReleaseFeed.LatestUrl}");
        ReleaseInfo? latest = ReleaseFeed.TryGetLatest(_web, out problem);
        if (latest == null)
        {
            Log.Write("No answer: " + problem);
            return null;
        }
        bool offer = UpdatePolicy.ShouldOffer(latest, installed, Settings, asked);
        Log.Write($"Newest release {latest.TagName}{(latest.Package == null ? " (no package)" : "")}: {(offer ? "newer, offered" : "nothing to offer")}.");
        return offer ? latest : null;
    }

    public void Skip(BuildVersion build)
    {
        Settings.SkippedVersion = build.Name;
        SaveSettings();
        Log.Write("The player skipped " + build.Name + ".");
    }

    /// <summary>What blocks an update now ("the server", "game.exe"...); empty when it can go ahead.</summary>
    public static List<string> RunningParts(int ownProcessId, bool serverListening)
    {
        var running = new List<string>();
        if (serverListening)
            running.Add("the server");
        foreach (string name in BlockingProcesses)
        {
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch (InvalidOperationException) { continue; }
            int others = found.Count(p => p.Id != ownProcessId);
            foreach (Process p in found) p.Dispose();
            if (others > 0)
                running.Add(name == "OfflineDAoC" ? "another launcher" : name == "CoreServer" || name == "DOLServer" ? "the server" : name + ".exe");
        }
        return running.Distinct().ToList();
    }

    /// <summary>Downloads, checks and unpacks a release's package. Throws PackageRefusedException with the reason.</summary>
    public PreparedUpdate PrepareFromRelease(ReleaseInfo release, Action<long, long>? progress, CancellationToken cancel)
    {
        BuildVersion build = release.Version ?? throw new PackageRefusedException("Not a Living Server release: " + release.TagName);
        Log.Write($"Downloading {release.Package?.Name} ({release.Package?.Size:N0} bytes) to {UpdatesFolder}");
        try
        {
            var (zip, contents) = ReleaseFeed.DownloadPackage(_web, release, UpdatesFolder, progress, cancel);
            Log.Write($"Downloaded and checked: SHA-256 matches GitHub's digest {release.Package?.Digest}; BUILD-INFO.txt names {contents.BuildName}.");
            return Stage(build, zip, contents);
        }
        catch (PackageRefusedException e)
        {
            Log.Write("REFUSED: " + e.Message);
            throw;
        }
    }

    /// <summary>
    /// Checks and unpacks a package already on disk (the --apply-update switch, or a kept package to reinstall). Its
    /// SHA-256 must be given, or be in the .sha256 file written beside it when it was downloaded and verified.
    /// </summary>
    public PreparedUpdate PrepareFromFile(string zipPath, string? sha256, string? tag)
    {
        string zip = Path.GetFullPath(zipPath);
        Match fromName = Regex.Match(Path.GetFileName(zip), @"^OfflineDAoC-LivingServer-(.+)\.zip$", RegexOptions.IgnoreCase);
        string? name = tag ?? (fromName.Success ? fromName.Groups[1].Value : null);
        BuildVersion build = BuildVersion.Parse(name) ?? throw Refuse("Can't tell which build " + Path.GetFileName(zip) + " is: name it OfflineDAoC-LivingServer-<build>.zip or give --tag.");
        if (!File.Exists(zip))
            throw Refuse("There's no " + zip + ".");
        string? expected = (sha256 ?? ReadSidecar(zip + ".sha256"))?.Trim();
        if (expected != null && expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expected = expected.Substring("sha256:".Length);
        string digest = "sha256:" + expected;
        DigestCheck check = PackageDigest.Check(zip, digest);
        if (check != DigestCheck.Match)
            throw Refuse(check == DigestCheck.Missing
                ? "No SHA-256 to check " + Path.GetFileName(zip) + " against (give --sha256, as GitHub lists it for the release file)."
                : Path.GetFileName(zip) + " doesn't match the SHA-256 it should have.");
        Log.Write($"{zip}: SHA-256 matches {expected}");
        PackageContents contents;
        try { contents = PackageCheck.Inspect(zip, build); }
        catch (PackageRefusedException e) { Log.Write("REFUSED: " + e.Message); throw; }
        // Kept with the others, so it can be installed again later.
        Directory.CreateDirectory(UpdatesFolder);
        string kept = Path.Combine(UpdatesFolder, ReleaseInfo.PackageName(build));
        if (!string.Equals(Path.GetFullPath(kept), zip, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(zip, kept, true);
            File.WriteAllText(kept + ".sha256", PackageDigest.Sha256Of(digest) + "  " + Path.GetFileName(kept) + Environment.NewLine);
        }
        return Stage(build, kept, contents);
    }

    private PackageRefusedException Refuse(string why)
    {
        Log.Write("REFUSED: " + why);
        return new PackageRefusedException(why);
    }

    private static string? ReadSidecar(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim().Split(' ')[0] : null; }
        catch (IOException) { return null; }
    }

    // Unpacks into runtime\updates\staging first (every entry checked and read back), then moves the folder into the
    // game folder, replacing an earlier copy of the same build's folder.
    private PreparedUpdate Stage(BuildVersion build, string zip, PackageContents contents)
    {
        string staging = Path.Combine(UpdatesFolder, "staging");
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        string unpacked = PackageCheck.Extract(zip, contents, staging);
        Log.Write("Unpacked to " + unpacked);
        string target = Path.Combine(GameFolder, contents.TopFolder);
        if (Directory.Exists(target))
        {
            Log.Write("Replacing the earlier " + contents.TopFolder + " folder in the game folder.");
            Directory.Delete(target, true);
        }
        Directory.Move(unpacked, target);
        Directory.Delete(staging, true);
        Log.Write("Package folder ready: " + target);
        KeepNewestPackages(build);
        return new PreparedUpdate(build, zip, target);
    }

    /// <summary>Keeps this package and the newest one before it in runtime\updates (to reinstall), deletes older ones.</summary>
    public void KeepNewestPackages(BuildVersion current)
    {
        var older = new List<(BuildVersion Build, string Path)>();
        foreach (string file in Directory.GetFiles(UpdatesFolder, "OfflineDAoC-LivingServer-*.zip"))
        {
            var m = Regex.Match(Path.GetFileName(file), @"^OfflineDAoC-LivingServer-(.+)\.zip$", RegexOptions.IgnoreCase);
            BuildVersion? b = m.Success ? BuildVersion.Parse(m.Groups[1].Value) : null;
            if (b != null && !b.SameBuildAs(current))
                older.Add((b, file));
        }
        foreach (var (b, file) in older.OrderByDescending(o => o.Build).Skip(1))
        {
            try
            {
                File.Delete(file);
                if (File.Exists(file + ".sha256")) File.Delete(file + ".sha256");
                Log.Write("Removed the older package " + Path.GetFileName(file) + " (the one before this update is kept).");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Couldn't remove " + file + ": " + e.Message); }
        }
    }

    /// <summary>
    /// Writes apply-update.ps1 and starts it in its own window. The caller closes the launcher right after: the
    /// script waits for it, runs INSTALL.cmd and (with restart) opens the launcher again.
    /// </summary>
    public Process StartHandoff(PreparedUpdate update, int launcherProcessId, string? restart, bool noPause)
    {
        Directory.CreateDirectory(UpdatesFolder);
        string script = Path.Combine(UpdatesFolder, "apply-update.ps1");
        using (Stream s = typeof(LauncherUpdater).Assembly.GetManifestResourceStream("OfflineDaoc.Updates.apply-update.ps1")
                          ?? throw new InvalidOperationException("The launcher has no apply-update.ps1 inside it."))
        using (var f = File.Create(script))
            s.CopyTo(f);
        string result = Path.Combine(UpdatesFolder, ResultFile);
        if (File.Exists(result))
            File.Delete(result);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = UpdatesFolder,
        };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-LauncherPid", launcherProcessId.ToString(), "-PackageFolder", update.PackageFolder, "-GameFolder", GameFolder,
                     "-Build", update.Build.Name, "-Log", Log.Path, "-Result", result })
            start.ArgumentList.Add(arg);
        // Only when there is one: Windows PowerShell can drop an empty "" argument and read the next switch as its value.
        if (!string.IsNullOrEmpty(restart))
        {
            start.ArgumentList.Add("-Restart");
            start.ArgumentList.Add(restart);
        }
        if (noPause)
            start.ArgumentList.Add("-NoPause");
        Log.Write($"Handing over to {script}: it waits for the launcher (process {launcherProcessId}) to close, runs {Path.Combine(update.PackageFolder, "INSTALL.cmd")}" +
                  (string.IsNullOrEmpty(restart) ? "" : " and opens " + restart));
        return Process.Start(start) ?? throw new InvalidOperationException("Windows didn't start PowerShell.");
    }

    /// <summary>The last update's result as apply-update.ps1 wrote it (once; the file is then marked as shown).</summary>
    public IReadOnlyDictionary<string, string>? TakeLastResult()
    {
        string path = Path.Combine(UpdatesFolder, ResultFile);
        try
        {
            if (!File.Exists(path))
                return null;
            var info = PackageCheck.ReadBuildInfo(File.ReadAllText(path));
            File.Move(path, path + ".shown", true);
            return info;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
