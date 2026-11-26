using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DAoCLivingServer.UpdateContract;

// Issue #685: would a RELEASED launcher take the candidate as its update?
//
//   dotnet DAoCLivingServer.UpdateContract.dll --launcher <game>\runtime\<launcher>.dll --game <game> --feed <feed.json> [--apply] [--as-build <build>]
//
// --as-build: the launcher checks as if it were that build (its own LauncherBuild is the candidate's when the launcher
// under test is this commit's own, #689: the candidate under each repository path it must accept or refuse).
//
// The released launchers have no command that checks GitHub without the window (1.2.2's only headless switch,
// --apply-update, starts from a file already on disk and skips the release feed and the download address check). So this
// loads the released launcher's own dll by reflection and runs, unchanged, the code its START SERVER / GET UPDATE path
// runs:
//   1. LauncherUpdater.Check (the automatic check at start-up): ReleaseFeed.TryGetLatest, ReleaseInfo.Parse, the package
//      asset by name, UpdatePolicy (newer, not skipped) and, where the launcher has it, the test channel's list;
//   2. LauncherUpdater.PrepareFromRelease: ReleaseFeed.DownloadPackage (the tag/pre-release match, the digest's shape,
//      the DownloadPrefix check, the download, the SHA-256, PackageCheck), then the package unpacked into the game folder;
//   3. with --apply, LauncherUpdater.StartHandoff: the launcher's own embedded apply-update.ps1 runs the candidate's
//      INSTALL.cmd (Windows only), and its result file must say the candidate is installed.
// Only the transport is replaced: the launcher's IReleaseWeb is a fake GitHub (a DispatchProxy) that answers the API's
// releases/latest and releases list with the candidate's release JSON (tools/release/update_contract.py candidate, the
// same names, URLs and digest publish_release.py makes and checks), and serves a file only at the exact URL the release
// lists. Like GitHub after a repository rename, it answers the API for any repository path (GitHub redirects it), so the
// launcher's own address check decides, as in #645.
//
// Exit codes: 0 the released launcher accepts (and with --apply, installed) the candidate; 1 it refuses (a contract
// break; the reason is printed); 2 this tool couldn't run (bad arguments, an unreadable launcher).
public static class Program
{
    public static int Main(string[] args)
    {
        string? Get(string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        string? launcher = Get("--launcher"), game = Get("--game"), feedPath = Get("--feed"), asBuild = Get("--as-build");
        if (launcher == null || game == null || feedPath == null)
        {
            Console.WriteLine("Usage: DAoCLivingServer.UpdateContract --launcher <dll> --game <folder> --feed <feed.json> [--apply] [--as-build <build>]");
            return 2;
        }
        Feed feed;
        Assembly old;
        try
        {
            feed = Feed.Load(feedPath);
            old = Assembly.LoadFrom(Path.GetFullPath(launcher));
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e.Message);
            return 2;
        }
        var check = new OldLauncher(old, feed, Path.GetFullPath(game), asBuild);
        try
        {
            return check.Run(args.Contains("--apply"));
        }
        catch (Refused e)
        {
            Fail(e.Message);
            return 1;
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            Fail("the released launcher threw " + e.InnerException.GetType().Name + ": " + e.InnerException.Message);
            return 1;
        }
    }

    public static void Fail(string why)
    {
        Console.WriteLine("REFUSED: " + why);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Console.WriteLine("::error title=Update contract::The released launcher would refuse this candidate: " + why.Replace("\r", "").Replace("\n", " | "));
    }
}

public sealed class Refused(string message) : Exception(message);

/// <summary>The candidate release as tools/release/update_contract.py candidate wrote it.</summary>
public sealed class Feed
{
    public string Repository { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Build { get; init; } = "";
    public string LatestJson { get; init; } = "";
    public string ReleasesJson { get; init; } = "";
    /// <summary>Release file URL (exactly as browser_download_url gives it) to the local file served there.</summary>
    public Dictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);

    public static Feed Load(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement r = doc.RootElement;
        string Abs(string p) => Path.IsPathRooted(p) ? p : Path.Combine(dir, p);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty f in r.GetProperty("files").EnumerateObject())
            files[f.Name] = Abs(f.Value.GetString()!);
        return new Feed
        {
            Repository = r.GetProperty("repository").GetString()!,
            Tag = r.GetProperty("tag").GetString()!,
            Build = r.GetProperty("build").GetString()!,
            LatestJson = File.ReadAllText(Abs(r.GetProperty("latest").GetString()!)),
            ReleasesJson = File.ReadAllText(Abs(r.GetProperty("releases").GetString()!)),
            Files = files,
        };
    }
}

/// <summary>GitHub as the released launcher sees it: the IReleaseWeb it is given (implemented with DispatchProxy).</summary>
public class FakeGitHub : DispatchProxy
{
    private static readonly Regex Latest = new(@"^https://api\.github\.com/repos/([^/]+/[^/]+)/releases/latest$", RegexOptions.IgnoreCase);
    private static readonly Regex List = new(@"^https://api\.github\.com/repos/([^/]+/[^/]+)/releases(\?.*)?$", RegexOptions.IgnoreCase);

    public Feed Feed { get; set; } = null!;
    public List<string> Requests { get; } = new();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string url = (string)args![0]!;
        Requests.Add(method!.Name + " " + url);
        Console.WriteLine($"  fake GitHub: {method.Name} {url}");
        switch (method.Name)
        {
            case "GetText":
            {
                Match m = Latest.Match(url);
                Match l = m.Success ? m : List.Match(url);
                if (!l.Success)
                    throw new HttpRequestException("GitHub answered 404 Not Found");
                if (!string.Equals(l.Groups[1].Value, Feed.Repository, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  fake GitHub: the launcher asks {l.Groups[1].Value}, the release is in {Feed.Repository} (answered, as GitHub's rename redirect does)");
                return m.Success ? Feed.LatestJson : Feed.ReleasesJson;
            }
            case "Download":
            {
                if (!Feed.Files.TryGetValue(url, out string? file))
                    throw new HttpRequestException("GitHub answered 404 Not Found for " + url);
                string path = (string)args[1]!;
                File.Copy(file, path, true);
                long size = new FileInfo(path).Length;
                (args[2] as Delegate)?.DynamicInvoke(size, size);
                return null;
            }
            default:
                throw new NotSupportedException("The released launcher's IReleaseWeb has a member this fake doesn't know: " + method.Name);
        }
    }
}

/// <summary>The released launcher's update types, found by name in its Updates namespace (it was renamed in 1.2.3).</summary>
public sealed class OldLauncher
{
    private readonly Assembly _asm;
    private readonly Feed _feed;
    private readonly string _game;
    private readonly string _ns;
    private readonly string? _asBuild;

    public OldLauncher(Assembly asm, Feed feed, string game, string? asBuild = null)
    {
        _asBuild = asBuild;
        _asm = asm;
        _feed = feed;
        _game = game;
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
        Type updater = types.FirstOrDefault(t => t.Name == "LauncherUpdater" && t.Namespace?.EndsWith(".Updates") == true)
                       ?? throw new Refused(asm.GetName().Name + " has no Updates.LauncherUpdater: not a Living Server launcher with updates");
        _ns = updater.Namespace!;
    }

    private Type T(string name) => _asm.GetType(_ns + "." + name) ?? throw new Refused("the released launcher has no " + _ns + "." + name);

    private static object? Static(Type t, string name) =>
        (object?)t.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
        t.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);

    public int Run(bool apply)
    {
        Type feedType = T("ReleaseFeed"), updaterType = T("LauncherUpdater"), web = T("IReleaseWeb");
        object? installed = Static(T("LauncherBuild"), "Version");
        string? installedName = installed == null ? null : (string?)Prop(installed, "Name");
        Console.WriteLine($"Released launcher: {_asm.Location}");
        Console.WriteLine($"  build {installedName ?? "(none)"}; LatestUrl {Static(feedType, "LatestUrl")}; DownloadPrefix {Static(feedType, "DownloadPrefix")}");
        Console.WriteLine($"Candidate: {_feed.Tag} (package build {_feed.Build}) in {_feed.Repository}");
        if (_asBuild != null)
        {
            installed = T("BuildVersion").GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [_asBuild])
                        ?? throw new Refused("--as-build " + _asBuild + " isn't a build the launcher reads");
            Console.WriteLine($"  checking as build {_asBuild} (--as-build)");
        }
        if (installed == null)
            throw new Refused("the released launcher names no build of its own (LauncherBuild.Version), so it offers nothing");

        var proxy = DispatchProxy.Create(web, typeof(FakeGitHub));
        ((FakeGitHub)proxy).Feed = _feed;
        object updater = Activator.CreateInstance(updaterType, _game, proxy, null)!;
        // What the window passes: its own build, or (1.2.3 on) the test build it installed.
        MethodInfo? installedBuild = updaterType.GetMethod("InstalledBuild");
        if (installedBuild != null)
            installed = installedBuild.Invoke(updater, [installed]) ?? installed;

        // 1. The automatic check at start-up (asked: false), as a player who opens the launcher gets it.
        object?[] checkArgs = [installed, false, DateTime.UtcNow, null];
        object? offer = updaterType.GetMethod("Check")!.Invoke(updater, checkArgs);
        if (offer == null)
            throw new Refused("its update check offers nothing" + (checkArgs[3] is string p ? " (" + p + ")" : "") +
                              ": the release feed, the package's asset name or the version order doesn't fit the released launcher. Its log: " +
                              LogTail());
        string? tag = (string?)Prop(offer, "TagName");
        object? asset = Prop(offer, "Package");
        Console.WriteLine($"PASS  check: offered {tag}, package {(asset == null ? "(none)" : Prop(asset, "Name"))}");
        if (!string.Equals(tag, _feed.Tag, StringComparison.Ordinal))
            throw new Refused($"it offers {tag}, not the candidate {_feed.Tag}");

        // 2. GET UPDATE: download, digest, address, package check, unpack into the game folder.
        object prepared;
        try
        {
            prepared = updaterType.GetMethod("PrepareFromRelease")!.Invoke(updater, [offer, null, CancellationToken.None])!;
        }
        catch (TargetInvocationException e) when (e.InnerException?.GetType().Name == "PackageRefusedException")
        {
            throw new Refused("GET UPDATE refuses the package: " + e.InnerException.Message);
        }
        string folder = (string)Prop(prepared, "PackageFolder")!;
        Console.WriteLine($"PASS  prepare: downloaded, SHA-256 and package checked, unpacked to {folder}");
        if (!File.Exists(Path.Combine(folder, "INSTALL.cmd")))
            throw new Refused("the unpacked package has no INSTALL.cmd");
        if (!apply)
            return 0;

        // 3. The hand-over: the released launcher's apply-update.ps1 runs the candidate's INSTALL.cmd.
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("ERROR: --apply runs Windows PowerShell; run it on Windows");
            return 2;
        }
        var handoff = (Process)updaterType.GetMethod("StartHandoff")!.Invoke(updater, [prepared, 0, null, true])!;
        if (!handoff.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            handoff.Kill(true);
            throw new Refused("apply-update.ps1 didn't finish within 10 minutes");
        }
        string resultFile = Path.Combine(_game, "runtime", "updates", (string)Static(updaterType, "ResultFile")!);
        if (!File.Exists(resultFile))
            throw new Refused("apply-update.ps1 wrote no " + resultFile + ". Its log: " + LogTail());
        Dictionary<string, string> result = File.ReadAllLines(resultFile)
            .Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim().TrimStart('﻿'), p => p[1].Trim());
        Console.WriteLine("  apply-update.ps1: " + string.Join("; ", result.Select(kv => kv.Key + "=" + kv.Value)));
        if (result.GetValueOrDefault("Result") != "installed" || result.GetValueOrDefault("Build") != _feed.Build)
            throw new Refused($"the hand-over didn't install {_feed.Build}: {result.GetValueOrDefault("Result")}: {result.GetValueOrDefault("Detail")}. Its log: " + LogTail());
        Console.WriteLine($"PASS  apply: the released launcher's apply-update.ps1 installed {_feed.Build}");
        return 0;
    }

    private string LogTail()
    {
        string log = Path.Combine(_game, "runtime", "logs", "launcher-updates.log");
        return File.Exists(log) ? string.Join(" | ", File.ReadAllLines(log).TakeLast(8)) : "(no launcher-updates.log)";
    }
}
