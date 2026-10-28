using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LivingServerSetup
{
    /// <summary>The Living Server package and the official downloader embedded in this exe.</summary>
    internal static class Payload
    {
        public static Dictionary<string, string> BuildInfo = new Dictionary<string, string>();
        public static string BuildName => Get("BuildName");
        /// <summary>
        /// The newest Offline DAoC release the package fits, e.g. 0.37 (its b and c editions too): the one Setup downloads
        /// and updates to. BUILD-INFO may list several ("0.37,0.38", docs/UPSTREAM-RELEASES.md) once a build carries a newer
        /// release's server code; a game on any listed release then takes Living Server as it is.
        /// </summary>
        public static string BaseRelease => NewestRelease(Get("BaseRelease"));
        /// <summary>Every release the package fits.</summary>
        public static IReadOnlyList<string> BaseReleases => Releases(Get("BaseRelease"));
        public static bool Fits(string release) => release != null && BaseReleases.Contains(release);
        public static string Commit => Get("Commit");
        /// <summary>"0.37" or "0.37 / 0.38": for labels that name every release the package fits.</summary>
        public static string FitsLabel => string.Join(" / ", BaseReleases.OrderBy(r => new Version(r)));
        public static string ShortCommit => Commit.Length > 7 ? Commit.Substring(0, 7) : Commit;

        public static List<string> Releases(string list) =>
            (list ?? "").Split(',').Select(r => r.Trim()).Where(r => Regex.IsMatch(r, @"^\d+\.\d+$")).Distinct().ToList();

        public static string NewestRelease(string list)
        {
            var all = Releases(list);
            return all.Count == 0 ? "" : all.OrderBy(r => new Version(r)).Last();
        }
        public static string ServerDll => Get("GameServer.dll").ToLowerInvariant();
        public static string RootFolder;
        static byte[] zipBytes;

        static string Get(string key) => BuildInfo.TryGetValue(key, out var v) ? v : "";

        /// <summary>"1.0-beta" reads as "Beta 1.0"; anything else as it is.</summary>
        public static string DisplayName
        {
            get
            {
                var m = Regex.Match(BuildName, @"^(\d+(?:\.\d+)*)-beta(?:\.(\d+))?$");
                if (m.Success) return "Beta " + m.Groups[1].Value + (m.Groups[2].Success ? " (build " + m.Groups[2].Value + ")" : "");
                return BuildName;
            }
        }

        public static void Load()
        {
            using (var s = Theme.Resource("LivingServer.zip"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                zipBytes = ms.ToArray();
            }
            using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                var info = zip.Entries.FirstOrDefault(e => e.FullName.Count(c => c == '/') == 1 && e.Name == "BUILD-INFO.txt")
                    ?? throw new InvalidDataException("The embedded Living Server package has no BUILD-INFO.txt.");
                RootFolder = info.FullName.Substring(0, info.FullName.IndexOf('/'));
                using (var r = new StreamReader(info.Open()))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        var m = Regex.Match(line, @"^\s*([^=#]+?)\s*=\s*(.*?)\s*$");
                        if (m.Success) BuildInfo[m.Groups[1].Value] = m.Groups[2].Value;
                    }
                }
            }
            if (string.IsNullOrEmpty(BuildName) || string.IsNullOrEmpty(BaseRelease) || string.IsNullOrEmpty(ServerDll))
                throw new InvalidDataException("The embedded Living Server package's BUILD-INFO.txt is incomplete.");
        }

        /// <summary>Unpacks the package into &lt;game&gt;\&lt;RootFolder&gt; (as a player would by hand) and returns that folder.
        /// Every file is written fresh and compared byte for byte; the package's own scripts then check BUILD-INFO.</summary>
        public static string ExtractPackage(string game)
        {
            var target = Path.Combine(game, RootFolder);
            using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                var prefix = RootFolder + "/";
                var full = Path.GetFullPath(target).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected entry in the Living Server package: " + e.FullName);
                    var rel = e.FullName.Substring(prefix.Length);
                    if (rel.Length == 0) continue;
                    var dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe entry in the Living Server package: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    byte[] data;
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                    File.WriteAllBytes(dest, data);
                    if (!File.ReadAllBytes(dest).SequenceEqual(data)) throw new IOException("Writing " + dest + " failed verification.");
                }
            }
            return target;
        }

        /// <summary>Writes shadowofze's downloader into dir (its downloads cache, .downloads, goes beside it).</summary>
        public static string ExtractDownloader(string dir)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Get-OfflineDAoC.ps1");
            byte[] data;
            using (var s = Theme.Resource("Get-OfflineDAoC.ps1")) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(data)) File.WriteAllBytes(path, data);
            return path;
        }
    }

    internal static class Hash
    {
        public static string File(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal enum LsState { None, ThisBuild, OtherBuild, Modified, Unknown }

    /// <summary>An Offline DAoC game folder as found on disk.</summary>
    internal sealed class GameInfo
    {
        public string Path;
        public string Release;          // e.g. "0.37", from the newest docs/RELEASE-x.y.md in PACKAGE MANIFEST.sha256
        public bool Classic;            // EDITION.txt: the edition without the Sluaghbinder
        public bool NewClient;          // new-client\VERSION.txt
        public bool Unfinished;         // a download or client install that didn't finish
        public bool ClientBehind;       // the new client hasn't had this release's client update (a refused or failed one)
        public LsState Ls = LsState.Unknown;
        public string LsBuild;          // the fork build installed (Social Expansion / Living Server), if any
        public Dictionary<string, string> Manifest;

        public string Edition => Release == null ? "unknown version" : Release + (NewClient ? "c" : Classic ? "" : "b");
        public string Title => "Offline DAoC " + Edition;

        public int CompareToBase()
        {
            if (Release == null) return -2;
            return new Version(Release).CompareTo(new Version(Payload.BaseRelease));
        }

        public bool IsBase => Payload.Fits(Release);
        /// <summary>The release the official update starts from (0.37's patch updates 0.36 / 0.36b / 0.36c).</summary>
        public bool IsUpdatable => Release != null && Manifests.UpdatableFrom.Contains(Release);
        /// <summary>Living Server fits it as it is: this release, with its new client (if any) updated too.</summary>
        public bool Ready => IsBase && !ClientBehind;
    }

    internal static class GameScanner
    {
        public static bool IsGame(string dir)
        {
            try
            {
                return System.IO.File.Exists(System.IO.Path.Combine(dir, "runtime", "server", "GameServer.dll")) &&
                       (System.IO.File.Exists(System.IO.Path.Combine(dir, "runtime", "OfflineDAoC.exe")) ||
                        System.IO.File.Exists(System.IO.Path.Combine(dir, "START OFFLINE DAOC.cmd")));
            }
            catch { return false; }
        }

        /// <summary>Looks where people put the game: beside this exe, C:\Games, Downloads, Desktop, Documents and the
        /// top of each fixed drive, three folders deep, skipping system folders.</summary>
        public static List<string> FindGames(string exeDir)
        {
            var roots = new List<string>();
            void Add(string p) { try { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) roots.Add(System.IO.Path.GetFullPath(p)); } catch { } }
            Add(exeDir);
            Add(System.IO.Path.GetDirectoryName(exeDir));
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(System.IO.Path.Combine(home, "Downloads"));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Add(System.IO.Path.Combine(home, "Games"));
            foreach (var d in SafeDrives())
            {
                Add(System.IO.Path.Combine(d, "Games"));
                Add(d);
            }
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int budget = 6000;
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool driveRoot = System.IO.Path.GetPathRoot(root).Equals(root, StringComparison.OrdinalIgnoreCase);
                Walk(root, driveRoot ? 2 : 3, found, seen, ref budget, driveRoot);
            }
            return found;
        }

        static IEnumerable<string> SafeDrives()
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok = false;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { }
                if (ok) yield return d.RootDirectory.FullName;
            }
        }

        static readonly string[] Skip = { "windows", "program files", "program files (x86)", "programdata", "$recycle.bin",
            "system volume information", "appdata", "node_modules", ".git", "runtime", "new-client", "source", "msocache", "recovery", "perflogs" };

        static void Walk(string dir, int depth, List<string> found, HashSet<string> seen, ref int budget, bool onlyNamed)
        {
            if (budget-- <= 0 || !seen.Add(dir)) return;
            if (IsGame(dir)) { found.Add(dir); return; }
            if (depth <= 0) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var s in subs)
            {
                var name = System.IO.Path.GetFileName(s).ToLowerInvariant();
                if (Skip.Contains(name) || name.StartsWith(".") && name != ".downloads") continue;
                // at a drive's root, only folders that look like the game's
                if (onlyNamed && !(name.Contains("offlinedaoc") || name.Contains("offline daoc") || name.Contains("daoc") || name.StartsWith("playable"))) continue;
                try { if ((new DirectoryInfo(s).Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue; } catch { continue; }
                Walk(s, depth - 1, found, seen, ref budget, false);
            }
        }

        public static Dictionary<string, string> ReadManifest(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            var m = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                var x = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+(.+)$");
                if (x.Success) m[x.Groups[2].Value.Trim()] = x.Groups[1].Value.ToLowerInvariant();
            }
            return m;
        }

        public static GameInfo Inspect(string dir)
        {
            var g = new GameInfo { Path = System.IO.Path.GetFullPath(dir).TrimEnd('\\', '/') };
            g.Manifest = ReadManifest(System.IO.Path.Combine(g.Path, "PACKAGE MANIFEST.sha256"));
            if (g.Manifest != null)
            {
                var versions = g.Manifest.Keys.Select(k => Regex.Match(k, @"^docs/RELEASE-(\d+\.\d+)\.md$")).Where(m => m.Success)
                    .Select(m => new Version(m.Groups[1].Value)).OrderBy(v => v).ToList();
                if (versions.Count > 0) g.Release = versions.Last().ToString();
                else
                {
                    var ed = g.Manifest.Keys.Select(k => Regex.Match(k, @"^editions/(\d+\.\d+)-no-custom-class/")).FirstOrDefault(m => m.Success);
                    if (ed != null) g.Release = ed.Groups[1].Value;
                }
            }
            g.Classic = System.IO.File.Exists(System.IO.Path.Combine(g.Path, "EDITION.txt"));
            g.NewClient = System.IO.File.Exists(System.IO.Path.Combine(g.Path, "new-client", "VERSION.txt"));
            g.Unfinished = System.IO.File.Exists(System.IO.Path.Combine(g.Path, "new-client", "INSTALLING.txt"));
            if (g.NewClient && g.Release != null && g.Release == Payload.BaseRelease)
            {
                try { g.ClientBehind = Hash.File(System.IO.Path.Combine(g.Path, "new-client", "VERSION.txt")) != Manifests.ClientIdentityTo; } catch { }
            }
            try { InspectLivingServer(g); } catch { g.Ls = LsState.Unknown; }
            return g;
        }

        static void InspectLivingServer(GameInfo g)
        {
            var dll = System.IO.Path.Combine(g.Path, "runtime", "server", "GameServer.dll");
            var hash = Hash.File(dll);
            if (hash == Payload.ServerDll) { g.Ls = LsState.ThisBuild; g.LsBuild = Payload.BuildName; return; }
            if (g.Manifest != null && g.Manifest.TryGetValue("runtime/server/GameServer.dll", out var original) && original == hash)
            { g.Ls = LsState.None; return; }
            foreach (var rec in InstallRecords(g.Path))
            {
                if (rec.TryGetValue("Folders", out var folders) && folders is System.Collections.ArrayList list)
                    foreach (var f in list.OfType<Dictionary<string, object>>())
                        if (f.TryGetValue("NewDll", out var nd) && (nd as string)?.ToLowerInvariant() == hash)
                        { g.Ls = LsState.OtherBuild; g.LsBuild = rec.TryGetValue("BuildName", out var bn) ? bn as string : "an earlier build"; return; }
            }
            g.Ls = g.Manifest == null ? LsState.Unknown : LsState.Modified;
        }

        public static IEnumerable<Dictionary<string, object>> InstallRecords(string game)
        {
            var root = System.IO.Path.Combine(game, "runtime", "server", "deployment-backups");
            if (!Directory.Exists(root)) yield break;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var d in Directory.GetDirectories(root, "fork-build-*").OrderByDescending(x => x, StringComparer.Ordinal))
            {
                var f = System.IO.Path.Combine(d, "install-record.json");
                if (!System.IO.File.Exists(f)) continue;
                Dictionary<string, object> rec = null;
                try { rec = js.Deserialize<Dictionary<string, object>>(System.IO.File.ReadAllText(f).TrimStart('\uFEFF')); } catch { }
                if (rec != null) yield return rec;
            }
        }

        public static readonly string[] GameProcesses = { "CoreServer", "DOLServer", "OfflineDAoC", "game", "camelot", "connect", "Godot_v4.7.2-stable_mono_win64" };

        /// <summary>Names of Offline DAoC programs still running (the same list the package's scripts refuse on).</summary>
        public static List<string> RunningGameProcesses()
        {
            var names = new List<string>();
            foreach (var n in GameProcesses)
            {
                try { if (Process.GetProcessesByName(n).Length > 0) names.Add(n); } catch { }
            }
            return names;
        }

        /// <summary>Where each running Offline DAoC program was started from, so the message can name the game folder
        /// (a launcher can stay behind with no window: play test 2026-10-28). "name (folder)" where Windows lets us read it.</summary>
        public static List<string> RunningGameProcessDetails()
        {
            var details = new List<string>();
            foreach (var n in GameProcesses)
            {
                Process[] found;
                try { found = Process.GetProcessesByName(n); } catch { continue; }
                foreach (var p in found)
                {
                    string where = null;
                    try { where = Path.GetDirectoryName(p.MainModule.FileName); } catch { }
                    bool window = false;
                    try { window = p.MainWindowHandle != IntPtr.Zero; } catch { }
                    details.Add(n + ".exe" + (where != null ? " in " + where : "") + (window ? "" : ", no window") + " (process " + p.Id + ")");
                    p.Dispose();
                }
            }
            return details;
        }
    }

    /// <summary>Sizes of the official downloads, read from shadowofze's release manifests (with fallbacks measured on
    /// the 0.37 releases, used only if GitHub can't be reached before the install starts).</summary>
    internal static class Manifests
    {
        public const string Upstream = "https://github.com/shadowofze/OfflineDAoC/releases/download/v";
        public static readonly HashSet<string> UpdatableFrom = new HashSet<string> { "0.36" };
        public static long BaseGame = 5103650363;          // 0.36b, three parts (0.36 is the same download)
        public static long GamePatch = 38084367;           // 0.37 game update
        public static long NewClient = 29777878306;        // 0.36c, 19 parts
        public static long NewClientUnpacked = 33758711327;
        public static long ClientPatch = 1667048538;       // 0.37c client update, two parts
        public static string NewInstallBase = "0.36b";
        public static string ClientIdentityTo = "230e6013e920273c7a399b892b92ab91cfa1381e61e845cb8f89b1692b283244"; // new-client/VERSION.txt once updated
        public static int NewClientParts = 19;
        public static readonly Dictionary<string, long> ClientPartBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public static bool Live;

        /// <summary>Refreshes the sizes from the live manifests. Quiet on failure.</summary>
        public static void TryRefresh()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> Fetch(string v)
                {
                    using (var wc = new QuickClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "OfflineDAoC-LivingServer-Setup";
                        var text = Encoding.UTF8.GetString(wc.DownloadData(Upstream + v + "/download-manifest.json")).TrimStart('\uFEFF');
                        return js.Deserialize<Dictionary<string, object>>(text);
                    }
                }
                long Sum(object parts, string key = "Bytes") => ((System.Collections.ArrayList)parts).OfType<Dictionary<string, object>>().Sum(p => Convert.ToInt64(p[key]));
                var patch = Fetch(Payload.BaseRelease + "b");
                var gp = (Dictionary<string, object>)patch["GamePatch"];
                var cp = (Dictionary<string, object>)patch["ClientPatch"];
                GamePatch = Sum(gp["Parts"]);
                ClientPatch = Sum(cp["Parts"]);
                var bases = ((System.Collections.ArrayList)patch["BaseVersions"]).OfType<string>().ToList();
                UpdatableFrom.Clear();
                foreach (var b in bases) UpdatableFrom.Add(b);
                var nb = (string)patch["NewInstallBase"];          // e.g. 0.36b
                NewInstallBase = nb;
                if (cp.TryGetValue("Identity", out var idObj) && idObj is Dictionary<string, object> id && id.TryGetValue("To", out var to)) ClientIdentityTo = ((string)to).ToLowerInvariant();
                BaseGame = Sum(Fetch(nb)["Parts"]);
                var client = Fetch(Regex.Replace(nb, "[bc]$", "") + "c");
                NewClient = Sum(client["Parts"]);
                NewClientUnpacked = Convert.ToInt64(client["UnpackedBytes"]);
                var cparts = ((System.Collections.ArrayList)client["Parts"]).OfType<Dictionary<string, object>>().ToList();
                NewClientParts = cparts.Count;
                foreach (var p in cparts) ClientPartBytes[(string)p["Name"]] = Convert.ToInt64(p["Bytes"]);
                Live = true;
            }
            catch (Exception e)
            {
                Log.Write("Could not read the release manifests (" + e.Message + "); using the 0.37 sizes.");
            }
        }
    }

    /// <summary>A WebClient that gives up after 10 seconds, so an offline PC isn't kept waiting.</summary>
    internal sealed class QuickClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var r = base.GetWebRequest(address);
            r.Timeout = 10000;
            if (r is HttpWebRequest h) h.ReadWriteTimeout = 10000;
            return r;
        }
    }

    internal static class Log
    {
        static readonly object gate = new object();
        public static string FilePath;
        public static event Action<string> Written;

        public static void Write(string line)
        {
            var stamped = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
            lock (gate)
            {
                try { if (FilePath != null) System.IO.File.AppendAllText(FilePath, stamped + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            Written?.Invoke(line);
        }
    }
}
