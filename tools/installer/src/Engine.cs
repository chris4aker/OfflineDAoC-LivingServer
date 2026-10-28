using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace LivingServerSetup
{
    internal sealed class InstallOptions
    {
        public bool NewInstall;
        public string Destination;      // new install: the new game folder
        public bool Classic;            // new install: 0.37 instead of 0.37b
        public bool NewClient;          // new install: with the 0.37c new client
        public GameInfo Game;           // existing game
        public bool AddClient;          // existing game: add the new client
        public bool ForceLivingServer;  // replace a server/launcher the package's installer doesn't recognise
        public bool FreshCopyIfChanged; // official update refused (changed files): fresh install + progress import
        public bool Remove;             // take Living Server off (back to the released server and launcher)
        public bool Shortcuts = true;
        public bool EnableNetFx;
        /// <summary>Turn every Living Server feature on at the suggested settings (the launcher's Recommended), after installing.</summary>
        public bool RecommendedSettings;
        public GameInfo ImportFrom;     // new install: bring account, characters and bots from this older game
        public string LocalAssets;      // tests: the downloader reads release files from here instead of GitHub

        public string SetupFilesDir
        {
            get
            {
                var anchor = NewInstall ? Destination : Game.Path;
                var parent = Path.GetDirectoryName(Path.GetFullPath(anchor).TrimEnd('\\', '/')) ?? anchor;
                return Path.Combine(parent, "Offline DAoC setup files");
            }
        }

        public bool NeedsUpstream => NewInstall || (!Remove && Game != null && (!Game.IsBase || Game.ClientBehind || (AddClient && !Game.NewClient)));
    }

    internal enum Outcome { Success, Failed, Cancelled }

    internal sealed class InstallResult
    {
        public Outcome Outcome;
        public string Message;
        public string Hint;
        public bool CanForce;
        public bool CanFreshCopy;
        public bool CanChangeFolder;   // the folder was the problem: offer to go back and pick another
        public string GamePath;
        public string CacheDir;
        public string Note;            // something the player should know on the done page
        public long CacheBytes;
        public bool NetFxNeedsRestart;
        public bool NetFxSkipped;
        /// <summary>Every Living Server feature was turned on at the suggested settings.</summary>
        public bool SettingsApplied;
        public bool NewClient;
    }

    internal enum StepId { Check, Release, DownloadGame, UnpackGame, Client, UpdateGame, UpdateClient, FreshCopy, Import, LivingServer, NetFx, Shortcuts, Remove, Settings }

    /// <summary>Runs an install plan step by step on a worker thread. The UI reads the public state on Changed.</summary>
    internal sealed class Engine
    {
        /// <summary>A download error that means the connection went away, not that something is wrong with the game. Pure.</summary>
        public static bool LooksLikeNetworkDrop(string message) =>
            message != null && (message.IndexOf("could not be resolved", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                message.IndexOf("connection was reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                message.IndexOf("unable to connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                message.IndexOf("connection was closed", StringComparison.OrdinalIgnoreCase) >= 0);

        sealed class Step
        {
            public StepId Id;
            public StepItem Item;
            public double Weight;
            public double Fraction;
            public bool Measured;      // the fraction is real; otherwise the step bar is indeterminate
        }

        readonly InstallOptions o;
        readonly List<Step> steps = new List<Step>();
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly object gate = new object();
        public List<StepItem> Items => steps.Select(s => s.Item).ToList();
        public event Action Changed;

        // state for the UI
        public double Overall { get; private set; }
        public double? StepFraction { get; private set; }
        public string Status { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public bool CanCancel { get; private set; }
        public InstallResult Result { get; private set; }

        string gamePath;
        string setupDir;
        DownloadWatch watch;
        volatile bool upstreamRunning;
        bool rolledBack;
        string previousBuild;

        public Engine(InstallOptions options)
        {
            o = options;
            gamePath = o.NewInstall ? Path.GetFullPath(o.Destination).TrimEnd('\\', '/') : o.Game.Path;
            setupDir = o.SetupFilesDir;
            Plan();
        }

        void Add(StepId id, string title, double weight, string detail = null) =>
            steps.Add(new Step { Id = id, Weight = Math.Max(weight, 1), Item = new StepItem { Title = title, Detail = detail } });

        const double GB = 1073741824.0;

        void Plan()
        {
            var ls = "Living Server " + Payload.DisplayName;
            Add(StepId.Check, "Check your PC", 0.05 * GB);
            if (o.Remove)
            {
                Add(StepId.Remove, "Put the released server and launcher back", 0.3 * GB, "from the backups Living Server made when it was installed");
                return;
            }
            bool client = o.NewInstall ? o.NewClient : (o.Game.NewClient || o.AddClient);
            if (o.NewInstall)
            {
                Add(StepId.DownloadGame, "Download Offline DAoC", Manifests.BaseGame, Theme.Gb(Manifests.BaseGame) + " from shadowofze's official release, checked part by part");
                Add(StepId.UnpackGame, "Unpack the game", Manifests.BaseGame * 0.45);
                if (client)
                    Add(StepId.Client, "Download and unpack the new client", Manifests.NewClient * 1.2, Theme.Gb(Manifests.NewClient) + ", in checked parts");
                Add(StepId.UpdateGame, "Update the game to " + Payload.BaseRelease, Manifests.GamePatch + 0.3 * GB, "the official update");
                if (client) Add(StepId.UpdateClient, "Update the new client", Manifests.ClientPatch * 1.6, Theme.Gb(Manifests.ClientPatch) + " official update");
                if (o.ImportFrom != null)
                    Add(StepId.Import, "Bring your characters over", 0.6 * GB, "account, characters, items, houses and bots from " + o.ImportFrom.Title);
            }
            else
            {
                if (o.NeedsUpstream && o.Game.Ls != LsState.None)
                    Add(StepId.Release, "Put the released server back for the update", 0.2 * GB, "the official update only applies to the released files; your settings stay");
                if (!o.Game.IsBase)
                    Add(StepId.UpdateGame, "Update the game to " + Payload.BaseRelease, Manifests.GamePatch + 0.4 * GB, "the official update: your account, characters and bots stay");
                if (o.AddClient && !o.Game.NewClient)
                    Add(StepId.Client, "Download and unpack the new client", Manifests.NewClient * 1.2, Theme.Gb(Manifests.NewClient) + ", in checked parts");
                if (client && (!o.Game.IsBase || !o.Game.NewClient || o.Game.ClientBehind))
                    Add(StepId.UpdateClient, "Update the new client", Manifests.ClientPatch * 1.6, "the official update");
            }
            if (!o.NewInstall && o.Game.Ready && o.Game.Ls == LsState.ThisBuild && !o.NeedsUpstream)
                Add(StepId.LivingServer, "Check " + ls, 0.3 * GB, "every file compared with the package; anything missing is put back");
            else
                Add(StepId.LivingServer, "Install " + ls, 0.5 * GB, "server, launcher tab and fixes; every file backed up first");
            if (o.RecommendedSettings && !o.Remove)
                Add(StepId.Settings, "Turn on every Living Server feature", 0.01 * GB,
                    "at our suggested settings: bot guilds, personalities, voices, all of server life, XP by level");
            if (o.EnableNetFx) Add(StepId.NetFx, "Turn on .NET Framework 3.5", 0.4 * GB, "the original game client needs it; Windows asks your permission");
            if (o.Shortcuts) Add(StepId.Shortcuts, "Add shortcuts", 0.02 * GB, "on the desktop and in the Start menu");
        }

        Step Find(StepId id) => steps.FirstOrDefault(s => s.Id == id);

        /// <summary>
        /// Runs the game's own launcher (just installed from this package) in its settings mode, which writes the suggested
        /// Living Server settings through the same save as its Living Server tab. Null when saved, else why not. Never fatal.
        /// </summary>
        /// <summary>The launcher's code holds the "--apply-recommended-settings" string (UTF-16, as .NET stores literals).</summary>
        static bool LauncherHasSettingsMode(string dll)
        {
            try
            {
                if (!File.Exists(dll)) return false;
                var needle = System.Text.Encoding.Unicode.GetBytes("--apply-recommended-settings");
                var data = File.ReadAllBytes(dll);
                for (int i = 0; i + needle.Length <= data.Length; i++)
                {
                    int k = 0;
                    while (k < needle.Length && data[i + k] == needle[k]) k++;
                    if (k == needle.Length) return true;
                }
            }
            catch (Exception e) { Log.Write("Recommended settings: " + e.Message); }
            return false;
        }

        static string ApplyRecommendedSettings(string game)
        {
            var launcher = Path.Combine(game, "runtime", "OfflineDAoC.exe");
            if (!File.Exists(launcher)) return "no launcher in runtime";
            // A launcher from before Beta 1.1 has no settings mode and would just open its window: look for the switch in it.
            if (!LauncherHasSettingsMode(Path.Combine(game, "runtime", "OfflineDAoC.dll")))
            {
                Log.Write("Recommended settings: this launcher has no settings mode");
                return "this launcher has no settings mode";
            }
            var dotnet = Path.Combine(game, "tools", "dotnet");
            var psi = new ProcessStartInfo(launcher, "--apply-recommended-settings")
            {
                WorkingDirectory = Path.GetDirectoryName(launcher), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            if (Directory.Exists(dotnet))
            {
                psi.EnvironmentVariables["DOTNET_ROOT"] = dotnet;
                psi.EnvironmentVariables["DOTNET_ROOT_X64"] = dotnet;
                psi.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            }
            try
            {
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEndAsync();
                    var error = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(60_000))
                    {
                        try { p.Kill(); } catch { }
                        return "the launcher didn't finish";
                    }
                    var text = (output.Result + " " + error.Result).Trim();
                    Log.Write("Recommended settings: exit " + p.ExitCode + (text.Length > 0 ? ": " + text : ""));
                    return p.ExitCode == 0 ? null : (text.Length > 0 ? text : "exit code " + p.ExitCode);
                }
            }
            catch (Exception e)
            {
                Log.Write("Recommended settings: " + e.Message);
                return e.Message;
            }
        }

        void Set(StepId id, StepState state, string detail = null)
        {
            var s = Find(id);
            if (s == null) return;
            lock (gate)
            {
                s.Item.State = state;
                if (detail != null) s.Item.Detail = detail;
                if (state == StepState.Done || state == StepState.Skipped) { s.Fraction = 1; }
            }
            Recompute();
        }

        void Activate(StepId id, string status, string detail = null)
        {
            var s = Find(id);
            if (s == null) return;
            // earlier steps of a script run that produced no closing line are done by now
            foreach (var p in steps.TakeWhile(x => x != s))
                if (p.Item.State == StepState.Active || (p.Item.State == StepState.Pending && IsScriptStep(p.Id) && IsScriptStep(id)))
                { p.Item.State = StepState.Done; p.Fraction = 1; }
            lock (gate)
            {
                s.Item.State = StepState.Active;
                if (detail != null) s.Item.Detail = detail;
            }
            Status = status;
            Detail = "";   // the last step's numbers don't belong to this one
            Recompute();
        }

        static bool IsScriptStep(StepId id) => id == StepId.DownloadGame || id == StepId.UnpackGame || id == StepId.Client || id == StepId.UpdateGame || id == StepId.UpdateClient;

        void Progress(StepId id, double fraction, bool measured = true)
        {
            var s = Find(id);
            if (s == null) return;
            lock (gate) { s.Fraction = Math.Max(s.Fraction, Math.Min(1, fraction)); s.Measured = measured; }
            Recompute();
        }

        void Recompute()
        {
            lock (gate)
            {
                double total = steps.Sum(s => s.Weight);
                double done = steps.Sum(s => s.Weight * (s.Item.State == StepState.Done || s.Item.State == StepState.Skipped ? 1 : s.Fraction));
                Overall = total > 0 ? done / total : 0;
                var active = steps.FirstOrDefault(s => s.Item.State == StepState.Active);
                StepFraction = active != null && active.Measured ? active.Fraction : (double?)null;
                // Stopping is safe while parts download (they are kept and checked again) and while the new client
                // unpacks part by part; not while the game unpacks into its new folder or an update applies.
                CanCancel = upstreamRunning && active != null && (active.Id == StepId.DownloadGame || active.Id == StepId.Client);
            }
            Changed?.Invoke();
        }

        void SetStatus(string status, string detail = null)
        {
            Status = status;
            if (detail != null) Detail = detail;
            Changed?.Invoke();
        }

        /// <summary>Screenshot mode: a believable moment part-way through a new install with the new client.</summary>
        public void DemoState()
        {
            foreach (var st in steps)
            {
                if (st.Id == StepId.Check || st.Id == StepId.DownloadGame) { st.Item.State = StepState.Done; st.Fraction = 1; }
                if (st.Id == StepId.UnpackGame) { st.Item.State = StepState.Done; st.Fraction = 1; }
                if (st.Id == StepId.Client) { st.Item.State = StepState.Active; st.Fraction = 0.42; st.Measured = true; }
            }
            Status = "Downloading the new client, part 8 of 19...";
            Detail = "12.4 GB of 27.7 GB  ·  48 MB/s  ·  about 5 min left";
            upstreamRunning = true;
            Recompute();
        }

        public void Cancel()
        {
            if (!CanCancel) return;
            Log.Write("Stop requested.");
            cts.Cancel();
        }

        /// <summary>Runs the whole plan. Call on a worker thread.</summary>
        public InstallResult Run()
        {
            var r = new InstallResult { GamePath = gamePath, CacheDir = Path.Combine(setupDir, ".downloads") };
            try
            {
                Activate(StepId.Check, "Checking your PC...");
                CheckPc();
                Set(StepId.Check, StepState.Done);

                if (o.Remove)
                {
                    Activate(StepId.Remove, "Putting the released server and launcher back...");
                    RunPackageScript("rollback-server-build.ps1", "-GameFolder " + ScriptJob.Quote(gamePath) + " -ToRelease", r);
                    Set(StepId.Remove, StepState.Done);
                    r.Outcome = Outcome.Success;
                    return Result = r;
                }

                if (Find(StepId.Release) != null)
                {
                    Activate(StepId.Release, "Putting the released server back for the official update...");
                    if ((o.Game.Ls == LsState.Modified || o.Game.Ls == LsState.Unknown) && !o.ForceLivingServer)
                        throw new StepFailed("This game's server or launcher files were changed by hand. To update it, Setup has to put the released ones back over them.",
                            "Replace anyway keeps a copy of the current files first, in runtime\\server\\deployment-backups\\setup-before-update-<time>.") { CanForce = true };
                    BackupBeforeRelease(gamePath);
                    previousBuild = o.Game.LsBuild;
                    RunPackageScript("rollback-server-build.ps1", "-GameFolder " + ScriptJob.Quote(gamePath) + " -ToRelease", r);
                    rolledBack = true;
                    Set(StepId.Release, StepState.Done);
                }

                if (o.NeedsUpstream) RunUpstream(r);

                if (Find(StepId.Import) != null)
                {
                    Activate(StepId.Import, "Bringing your characters over...", "from " + o.ImportFrom.Path);
                    ImportProgress(o.ImportFrom.Path, gamePath);
                    Set(StepId.Import, StepState.Done);
                }

                Activate(StepId.LivingServer, "Installing Living Server " + Payload.DisplayName + "...");
                var args = "-GameFolder " + ScriptJob.Quote(gamePath) + (o.ForceLivingServer ? " -Force" : "");
                RunPackageScript("install-server-build.ps1", args, r, extract: true);
                Set(StepId.LivingServer, StepState.Done);

                if (Find(StepId.Settings) != null)
                {
                    Activate(StepId.Settings, "Turning on Living Server at our suggested settings...");
                    string why = ApplyRecommendedSettings(gamePath);
                    r.SettingsApplied = why == null;
                    if (why == null) Set(StepId.Settings, StepState.Done, "all on; change any of them in the launcher's Living Server tab");
                    else Set(StepId.Settings, StepState.Skipped, "not turned on (" + why + "): turn them on in the launcher's Living Server tab");
                }

                if (Find(StepId.NetFx) != null)
                {
                    Activate(StepId.NetFx, "Turning on .NET Framework 3.5 (Windows asks your permission)...");
                    var nf = NetFx35.Enable();
                    if (nf == NetFx35.Result.Skipped) { r.NetFxSkipped = true; Set(StepId.NetFx, StepState.Skipped, "not turned on: you can do it later, see READ ME FIRST.txt"); }
                    else if (nf == NetFx35.Result.Failed) { r.NetFxSkipped = true; Set(StepId.NetFx, StepState.Failed, "Windows couldn't turn it on: see READ ME FIRST.txt to do it by hand"); }
                    else { r.NetFxNeedsRestart = nf == NetFx35.Result.Restart; Set(StepId.NetFx, StepState.Done, nf == NetFx35.Result.Restart ? "on; Windows wants a restart before you play" : "on"); }
                }

                if (Find(StepId.Shortcuts) != null)
                {
                    Activate(StepId.Shortcuts, "Adding shortcuts...");
                    try { Shortcuts.Create(gamePath); Set(StepId.Shortcuts, StepState.Done); }
                    catch (Exception e) { Log.Write("Shortcuts: " + e.Message); Set(StepId.Shortcuts, StepState.Skipped, "couldn't add them: " + e.Message); }
                }

                r.Outcome = Outcome.Success;
                r.GamePath = gamePath;
                r.NewClient = File.Exists(Path.Combine(gamePath, "new-client", "VERSION.txt"));
                r.CacheBytes = DirSize(r.CacheDir);
                r.Note = pendingNote;
                if (o.NewInstall) { try { File.Delete(StartedRecord(gamePath)); } catch { } }
                Overall = 1;
                Status = "Done";
                Changed?.Invoke();
                return Result = r;
            }
            catch (StepFailed f)
            {
                r.Outcome = cts.IsCancellationRequested ? Outcome.Cancelled : Outcome.Failed;
                r.Message = cts.IsCancellationRequested ? "Stopped. Finished downloads are kept, so running Setup again continues where it stopped." : f.Message;
                r.Hint = f.Hint;
                r.CanForce = f.CanForce;
                r.CanFreshCopy = f.CanFreshCopy;
                r.CanChangeFolder = f.CanChangeFolder;
                r.GamePath = gamePath;
                MarkFailed();
                RestoreAfterFailedUpdate(r);
                Log.Write((r.Outcome == Outcome.Cancelled ? "CANCELLED: " : "FAILED: ") + r.Message);
                return Result = r;
            }
            catch (Exception e)
            {
                r.Outcome = Outcome.Failed;
                r.Message = e.Message;
                r.GamePath = gamePath;
                MarkFailed();
                Log.Write("FAILED: " + e);
                return Result = r;
            }
            finally
            {
                upstreamRunning = false;
                CanCancel = false;
                watch?.Dispose();
                Changed?.Invoke();
            }
        }

        /// <summary>Copies the server and launcher files as they are now, before the released ones go back over them.</summary>
        static void BackupBeforeRelease(string game)
        {
            var dest = Path.Combine(game, "runtime", "server", "deployment-backups", "setup-before-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var files = new List<string>();
            foreach (var f in new[] { @"runtime\server", @"runtime\server\lib", @"runtime\server\win-x64" })
                foreach (var n in new[] { "GameServer.dll", "GameServer.pdb" }) files.Add(Path.Combine(f, n));
            foreach (var f in new[] { "runtime", @"new-client\runtime" })
                foreach (var n in new[] { "OfflineDAoC.dll", "OfflineDAoC.exe", "OfflineDAoC.pdb", "OfflineDAoC.deps.json", "OfflineDAoC.runtimeconfig.json" }) files.Add(Path.Combine(f, n));
            foreach (var rel in files)
            {
                var src = Path.Combine(game, rel);
                if (!File.Exists(src)) continue;
                var to = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(src, to, true);
                if (Hash.File(to) != Hash.File(src)) throw new IOException("The copy of " + rel + " before the update failed verification.");
            }
            Log.Write("Kept a copy of the current server and launcher files in " + dest);
        }

        void MarkFailed()
        {
            var a = steps.FirstOrDefault(s => s.Item.State == StepState.Active);
            if (a != null) { a.Item.State = cts.IsCancellationRequested ? StepState.Skipped : StepState.Failed; Recompute(); }
        }

        sealed class StepFailed : Exception
        {
            public string Hint; public bool CanForce; public bool CanFreshCopy; public bool CanChangeFolder;
            public StepFailed(string message, string hint = null) : base(message) { Hint = hint; }
        }

        // ---------------------------------------------------------------- checks

        void CheckPc()
        {
            var running = GameScanner.RunningGameProcesses();
            if (running.Count > 0)
            {
                var details = GameScanner.RunningGameProcessDetails();
                foreach (var d in details) Log.Write("Still running: " + d);
                bool hidden = details.Any(d => d.Contains(", no window"));
                throw new StepFailed("Offline DAoC is still running (" + string.Join(", ", running) + ").",
                    "Quit the game, click STOP SERVER in the launcher and close it, then click Try again." +
                    (hidden ? " One of them has no window: end it in Task Manager (Details: " +
                        string.Join(", ", details.Where(d => d.Contains(", no window"))) + ")." : ""));
            }

            if (o.NewInstall)
            {
                var dest = gamePath;
                if (dest.Length > 100 && o.NewClient)
                    throw new StepFailed("That folder's path is too long for the new client (" + dest.Length + " characters, 100 at most).", "Choose a shorter folder, like C:\\Games\\OfflineDAoC.") { CanChangeFolder = true };
                var pf = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
                if (pf.Any(p => !string.IsNullOrEmpty(p) && dest.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw new StepFailed("The game can't live under Program Files or Windows: it saves your world in its own folder.", "Choose a folder like C:\\Games\\OfflineDAoC.") { CanChangeFolder = true };
                if (GameScanner.IsGame(dest) && GameScanner.Inspect(dest).IsBase && MarkerExists(dest))
                    throw new StepFailed("Offline DAoC " + GameScanner.Inspect(dest).Edition + " is already installed in " + dest + ".",
                        "Go back and choose that game on the first page to add Living Server to it, or choose a new folder for a second game.") { CanChangeFolder = true };
                // A folder Setup itself started unpacking into, stopped by a crash or power cut before the downloader
                // marked it finished: the downloader won't continue it, so move it aside (never delete) and start over
                // from the verified parts, which are still in the cache.
                if (Directory.Exists(dest) && !ResumableNewInstall(dest) && File.Exists(StartedRecord(dest)) &&
                    !File.Exists(Path.Combine(dest, "runtime", "account.txt")))   // never a game someone has played
                {
                    var aside = dest + ".unfinished-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.Move(dest, aside);
                    Log.Write("Moved the unfinished copy in " + dest + " aside to " + aside);
                    pendingNote = "An unfinished copy from an earlier attempt was moved aside to " + aside + ". Delete it when you like.";
                }
                if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any() && !ResumableNewInstall(dest))
                {
                    // A finished game whose download cache was cleaned up has no marker any more (walkthrough 2026-10-28: the
                    // message called a complete 0.37b game "not an unfinished Offline DAoC install").
                    if (GameScanner.IsGame(dest))
                        throw new StepFailed("Offline DAoC " + GameScanner.Inspect(dest).Edition + " is already installed in " + dest + ".",
                            "Go back and choose that game on the first page to add Living Server to it, or choose a new folder for a second game.") { CanChangeFolder = true };
                    throw new StepFailed("The folder " + dest + " already has files in it, and they aren't an unfinished Offline DAoC install.", "Choose a new folder: Setup never installs over files it didn't put there.") { CanChangeFolder = true };
                }
            }
            var need = NeededBytes();
            var root = Path.GetPathRoot(o.NewInstall ? gamePath : setupDir);
            try
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                Log.Write("Free space on " + root + ": " + Theme.Gb(free) + "; needed about " + Theme.Gb(need));
                if (free < need)
                    throw new StepFailed("Not enough free disk space on " + root.TrimEnd('\\') + ": about " + Theme.Gb(need) + " is needed, " + Theme.Gb(free) + " is free.",
                        "Free up space or choose a folder on another drive, then try again.");
            }
            catch (StepFailed) { throw; }
            catch (Exception e) { Log.Write("Free space check skipped: " + e.Message); }
        }

        /// <summary>A folder this Setup (or the official downloader) began and didn't finish: the downloader continues it.</summary>
        string pendingNote;

        /// <summary>Written when Setup starts a new install into a folder that doesn't exist yet: proof the folder is ours.</summary>
        string StartedRecord(string dest)
        {
            var key = System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(dest.ToLowerInvariant()));
            return Path.Combine(setupDir, "started-" + BitConverter.ToString(key).Replace("-", "").Substring(0, 16) + ".txt");
        }

        bool ResumableNewInstall(string dest) => MarkerExists(dest) || File.Exists(Path.Combine(dest, "new-client", ".install", "base.done"));

        /// <summary>The downloader's "base game finished" marker for this folder (written once the base install is done).</summary>
        bool MarkerExists(string dest)
        {
            var cache = Path.Combine(setupDir, ".downloads");
            if (!Directory.Exists(cache)) return false;
            var key = System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(dest.ToLowerInvariant()));
            var marker = "base-installed-" + BitConverter.ToString(key).Replace("-", "").Substring(0, 16) + ".txt";
            return Directory.EnumerateFiles(cache, marker, SearchOption.AllDirectories).Any();
        }

        public long NeededBytes()
        {
            long need = 0;
            if (o.Remove) return 64L << 20;
            if (o.NewInstall)
            {
                need += (long)(Manifests.BaseGame * 3.3);                  // parts + joined archive + unpacked game
                if (o.NewClient) need += Manifests.NewClientUnpacked + (4L << 30);
                need += 2L << 30;
            }
            else
            {
                if (!o.Game.IsBase) need += 3L << 30;
                if (o.FreshCopyIfChanged) need += (long)(Manifests.BaseGame * 3.3) + (o.Game.NewClient ? Manifests.NewClientUnpacked + (4L << 30) : 0);
                if (o.AddClient && !o.Game.NewClient) need += Manifests.NewClientUnpacked + (4L << 30);
                need += 512L << 20;
            }
            bool clientUpdate = o.NewInstall ? o.NewClient : (o.Game.NewClient && (!o.Game.IsBase || o.Game.ClientBehind)) || (o.AddClient && !o.Game.NewClient);
            if (clientUpdate) need += Manifests.ClientPatch * 3;
            return need;
        }

        // ---------------------------------------------------------------- the official downloader

        void RunUpstream(InstallResult r)
        {
            var script = Payload.ExtractDownloader(setupDir);
            bool client = o.NewInstall ? o.NewClient : (o.Game.NewClient || o.AddClient);
            string release = Payload.BaseRelease + (client ? "c" : (o.NewInstall && o.Classic) || (!o.NewInstall && o.Game.Classic) ? "" : "b");
            string local = string.IsNullOrEmpty(o.LocalAssets) ? "" : " -LocalAssets " + ScriptJob.Quote(o.LocalAssets);
            var runs = new List<string>();
            if (o.NewInstall && !Directory.Exists(gamePath))
                File.WriteAllText(StartedRecord(gamePath), gamePath + Environment.NewLine + "Setup began a new install here on " + DateTime.Now.ToString("o") + ".");
            if (o.NewInstall)
            {
                // A new install with the new client downloads the client inside its first step, and the downloader only
                // marks the base game finished after it. If that step was stopped part-way, finish it with the client
                // release itself (it continues part by part), then update the game like an existing one.
                if (client && Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "new-client", ".install", "base.done")) && !MarkerExists(gamePath))
                {
                    Log.Write("Continuing the new client's install in " + gamePath);
                    var baseC = Regex.Replace(Manifests.NewInstallBase, "[bc]$", "") + "c";
                    runs.Add("-ReleaseVersion " + baseC + " -Destination " + ScriptJob.Quote(gamePath) + (o.Classic ? " -Classic" : "") + local);
                    runs.Add("-ReleaseVersion " + release + " -Update " + ScriptJob.Quote(gamePath) + " -IfChanged no -AddClient no" + local);
                }
                else runs.Add("-ReleaseVersion " + release + " -Destination " + ScriptJob.Quote(gamePath) + (client && o.Classic ? " -Classic" : "") + local);
            }
            else
            {
                runs.Add("-ReleaseVersion " + release + " -Update " + ScriptJob.Quote(gamePath) + " -IfChanged " + (o.FreshCopyIfChanged ? "yes" : "no") + " -AddClient " + (o.AddClient ? "yes" : "no") + local);
                if (o.FreshCopyIfChanged)
                {
                    var fresh = Find(StepId.UpdateGame);
                    if (fresh != null) fresh.Item.Title = "Install a fresh copy and bring your characters over";
                }
            }

            watch = new DownloadWatch(Path.Combine(setupDir, ".downloads"), gamePath, this);
            watch.Start();
            upstreamRunning = true;
            string freshFolder = null;
            try
            {
                foreach (var args in runs)
                {
                    var job = new ScriptJob();
                    bool filesChanged = false;
                    job.Line += (line, err) =>
                    {
                        if (err) return;
                        OnUpstreamLine(line);
                        var m = Regex.Match(line, @"^Done\. Your game is now in (.+), with your progress\.");
                        if (m.Success) freshFolder = m.Groups[1].Value.Trim();
                        if (line.StartsWith("Some game files in that folder were changed")) filesChanged = true;
                    };
                    int code = job.Run(script, args, cts.Token);
                    if (cts.IsCancellationRequested) throw new StepFailed("Stopped.");
                    if (code != 0)
                    {
                        var msg = job.LastError ?? "The official downloader stopped with code " + code + ".";
                        var f = new StepFailed(msg, LooksLikeNetworkDrop(msg)
                            ? "The internet connection dropped while downloading. Finished downloads are kept: when you're back online, click Try again and it continues where it stopped."
                            : "Finished downloads are kept: click Try again and it continues where it stopped.");
                        if (filesChanged)
                        {
                            f.CanFreshCopy = true;
                            f.Hint = "Some of the game's own files were changed by hand, so the official update can't apply to this folder. " +
                                     "Setup can install a fresh copy next to it and bring your account, characters, items and bots over.";
                        }
                        throw f;
                    }
                }
            }
            finally { watch.Stop(); upstreamRunning = false; Recompute(); }

            if (freshFolder != null)
            {
                Log.Write("The game now lives in " + freshFolder);
                // the old folder had its build taken off for the update: put it back there, and carry on in the new one
                if (rolledBack)
                {
                    var note = RestorePrevious(gamePath);
                    if (note != null) Log.Write(note);
                }
                rolledBack = false;
                gamePath = freshFolder;
                r.GamePath = freshFolder;
            }
            foreach (var s in steps.Where(s => IsScriptStep(s.Id) && s.Item.State != StepState.Done))
            { s.Item.State = StepState.Done; s.Fraction = 1; }
            Recompute();
        }

        void OnUpstreamLine(string line)
        {
            if (line.StartsWith("Downloading OfflineDAoC-v") && line.Contains(".zip.") || line.StartsWith("Downloading the complete") || line.StartsWith("Step 1 of 2: the Offline DAoC") || line.StartsWith("Step 1 of 2: the complete"))
            {
                if (Find(StepId.DownloadGame)?.Item.State == StepState.Pending) Activate(StepId.DownloadGame, "Downloading Offline DAoC...");
            }
            else if (line.StartsWith("Verified and extracted to") || line.StartsWith("Verified the ") && line.Contains("edition"))
            {
                Set(StepId.DownloadGame, StepState.Done);
                Set(StepId.UnpackGame, StepState.Done);
            }
            else if (line.StartsWith("Step 2 of 2: new client part") || line.StartsWith("Unpacking part") || line.StartsWith("Adding the new client") || line.StartsWith("Continuing the install"))
            {
                if (Find(StepId.Client)?.Item.State != StepState.Active) Activate(StepId.Client, "Downloading the new client...");
                var m = Regex.Match(line, @"part (\d+) of (\d+)");
                if (m.Success) SetStatus((line.StartsWith("Unpacking") ? "Unpacking the new client, part " : "Downloading the new client, part ") + m.Groups[1].Value + " of " + m.Groups[2].Value + "...");
            }
            else if (line.StartsWith("Verified and installed the new client"))
                Set(StepId.Client, StepState.Done);
            else if (line.StartsWith("Step 2 of 2: updating it to") || line.Contains("the game update") || line.StartsWith("The game update is already installed"))
            {
                if (Find(StepId.UpdateGame)?.Item.State != StepState.Active && Find(StepId.UpdateGame)?.Item.State != StepState.Done)
                    Activate(StepId.UpdateGame, "Updating the game to " + Payload.BaseRelease + "...");
                if (line.StartsWith("Applying")) SetStatus("Applying the official update (it backs up every file it replaces)...");
                if (line.Contains("already installed")) Set(StepId.UpdateGame, StepState.Done);
            }
            else if (line.Contains("the new client update"))
            {
                Set(StepId.UpdateGame, StepState.Done);
                if (Find(StepId.UpdateClient)?.Item.State != StepState.Active) Activate(StepId.UpdateClient, "Updating the new client...");
                if (line.StartsWith("Applying")) SetStatus("Applying the new client's official update...");
                if (line.Contains("already installed")) Set(StepId.UpdateClient, StepState.Done);
            }
            else if (line.StartsWith("Installing a fresh Offline DAoC"))
                SetStatus("Installing a fresh copy next to your game...");
            else if (line.StartsWith("Bringing your progress over"))
                SetStatus("Bringing your account, characters and bots over...");
            else if (line.StartsWith("Done:"))
            {
                foreach (var s in steps.Where(s => IsScriptStep(s.Id))) if (s.Item.State != StepState.Done) { s.Item.State = StepState.Done; s.Fraction = 1; }
                Recompute();
            }
        }

        /// <summary>Polls the downloader's cache and the game folder twice a second for real progress numbers.</summary>
        sealed class DownloadWatch : IDisposable
        {
            readonly string cache, game;
            readonly Engine e;
            readonly Dictionary<string, long> seen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Timer timer;
            readonly Queue<(DateTime t, long b)> samples = new Queue<(DateTime, long)>();
            long unpackTotal = -1;
            DateTime lastDirScan;
            long lastDirSize;
            int busy;

            public DownloadWatch(string cache, string game, Engine e) { this.cache = cache; this.game = game; this.e = e; }
            public void Start() => timer = new Timer(_ => Tick(), null, 500, 500);
            public void Stop() { timer?.Dispose(); timer = null; }
            public void Dispose() => Stop();

            long Max(string path, long len)
            {
                seen.TryGetValue(path, out var m);
                if (len > m) seen[path] = m = len;
                return m;
            }

            void Tick()
            {
                if (Interlocked.Exchange(ref busy, 1) == 1) return;
                try { Measure(); }
                catch (Exception ex) { Log.Write("progress: " + ex.Message); }
                finally { Interlocked.Exchange(ref busy, 0); }
            }

            void Measure()
            {
                if (!Directory.Exists(cache)) return;
                long baseBytes = 0, clientBytes = 0, patchBytes = 0, cpatchBytes = 0, joined = 0;
                foreach (var dir in Directory.GetDirectories(cache))
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var name = Path.GetFileName(f);
                        long len;
                        try { len = new FileInfo(f).Length; } catch { continue; }
                        if (Regex.IsMatch(name, @"^OfflineDAoC-v[\d.]+b?\.zip\.\d{3}$")) baseBytes += Max(f, len);
                        else if (Regex.IsMatch(name, @"^OfflineDAoC-v[\d.]+b?\.zip$")) joined = len;
                        else if (name.Contains("client-patch") && name.EndsWith(".zip")) cpatchBytes += Max(f, len);
                        else if (Regex.IsMatch(name, @"^OfflineDAoC-v[\d.]+c-part\d+\.zip$")) clientBytes += Max(f, len);
                        else if (name.Contains("game-patch") && name.EndsWith(".zip")) patchBytes += Max(f, len);
                    }
                }
                // client parts are deleted once unpacked: count what was seen, and parts finished before this run
                var clientSeen = seen.Where(k => Regex.IsMatch(Path.GetFileName(k.Key), @"^OfflineDAoC-v[\d.]+c-part\d+\.zip$"))
                    .ToDictionary(k => Path.GetFileName(k.Key), k => k.Value, StringComparer.OrdinalIgnoreCase);
                clientBytes = clientSeen.Values.Sum();
                var marks = Path.Combine(game, "new-client", ".install");
                if (Directory.Exists(marks))
                    foreach (var m in Directory.GetFiles(marks, "*.done"))
                    {
                        var part = Path.GetFileNameWithoutExtension(m);   // "<part>.zip"
                        if (part == "base" || clientSeen.ContainsKey(part)) continue;
                        clientBytes += Manifests.ClientPartBytes.TryGetValue(part, out var b) ? b : Manifests.NewClient / Math.Max(1, Manifests.NewClientParts);
                    }

                var now = DateTime.UtcNow;
                long all = baseBytes + clientBytes + patchBytes + cpatchBytes;
                samples.Enqueue((now, all));
                while (samples.Count > 2 && (now - samples.Peek().t).TotalSeconds > 8) samples.Dequeue();
                double rate = 0;
                if (samples.Count > 1)
                {
                    var first = samples.Peek();
                    var secs = (now - first.t).TotalSeconds;
                    if (secs > 0.5) rate = Math.Max(0, (all - first.b) / secs);
                }

                var dl = e.Find(StepId.DownloadGame);
                if (dl != null && dl.Item.State == StepState.Active)
                {
                    e.Progress(StepId.DownloadGame, baseBytes / (double)Manifests.BaseGame);
                    e.SetStatus("Downloading Offline DAoC", Line(baseBytes, Manifests.BaseGame, rate));
                    // the joined archive appearing means every part is in: unpacking has begun, even if the sizes were stale
                    if (baseBytes >= Manifests.BaseGame || joined > 0) { e.Set(StepId.DownloadGame, StepState.Done); e.Activate(StepId.UnpackGame, "Unpacking the game..."); }
                }
                var up = e.Find(StepId.UnpackGame);
                if (up != null && up.Item.State == StepState.Active)
                {
                    // joining the parts into one archive, then extracting it into the game folder
                    double frac = 0.25 * Math.Min(1, joined / (double)Manifests.BaseGame);
                    if (joined >= Manifests.BaseGame && unpackTotal < 0) unpackTotal = ZipTotal(Directory.GetFiles(cache, "OfflineDAoC-v*.zip", SearchOption.AllDirectories).FirstOrDefault(p => Regex.IsMatch(Path.GetFileName(p), @"^OfflineDAoC-v[\d.]+b?\.zip$")));
                    if (unpackTotal > 0 && Directory.Exists(game) && (now - lastDirScan).TotalSeconds >= 2)
                    {
                        lastDirScan = now;
                        lastDirSize = DirSize(game);
                    }
                    if (unpackTotal > 0) frac = 0.25 + 0.75 * Math.Min(1, lastDirSize / (double)unpackTotal);
                    e.Progress(StepId.UnpackGame, frac);
                    e.SetStatus("Unpacking the game", joined < Manifests.BaseGame ? "joining the checked parts" : unpackTotal > 0 ? Theme.Gb(lastDirSize) + " of " + Theme.Gb(unpackTotal) : "checking the complete archive");
                }
                var cl = e.Find(StepId.Client);
                if (cl != null && cl.Item.State == StepState.Active)
                {
                    e.Progress(StepId.Client, clientBytes / (double)Manifests.NewClient * 0.97);
                    e.Detail = Line(clientBytes, Manifests.NewClient, rate);
                    e.Changed?.Invoke();
                }
                var ug = e.Find(StepId.UpdateGame);
                if (ug != null && ug.Item.State == StepState.Active && patchBytes < Manifests.GamePatch && patchBytes > 0)
                {
                    e.Progress(StepId.UpdateGame, patchBytes / (double)Manifests.GamePatch * 0.4);
                    e.Detail = Line(patchBytes, Manifests.GamePatch, rate);
                    e.Changed?.Invoke();
                }
                var uc = e.Find(StepId.UpdateClient);
                if (uc != null && uc.Item.State == StepState.Active && cpatchBytes > 0)
                {
                    e.Progress(StepId.UpdateClient, cpatchBytes / (double)Manifests.ClientPatch * 0.6);
                    e.Detail = cpatchBytes < Manifests.ClientPatch ? Line(cpatchBytes, Manifests.ClientPatch, rate) : "applying it";
                    e.Changed?.Invoke();
                }
            }

            static string Line(long done, long total, double rate)
            {
                var s = Theme.Gb(Math.Min(done, total)) + " of " + Theme.Gb(total);
                if (rate > 1024)
                {
                    s += "  ·  " + (rate / 1048576.0).ToString(rate > 10485760 ? "0" : "0.0") + " MB/s";
                    var left = (total - done) / rate;
                    if (left > 1) s += "  ·  " + (left < 90 ? "about " + Math.Max(1, (int)Math.Round(left / 5) * 5) + " s left" : left < 5400 ? "about " + (int)Math.Round(left / 60) + " min left" : "about " + (left / 3600).ToString("0.0") + " h left");
                }
                return s;
            }

            static long ZipTotal(string path)
            {
                if (path == null) return -1;
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                        return z.Entries.Sum(x => x.Length);
                }
                catch { return -1; }
            }
        }

        public static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(dir));
                while (stack.Count > 0)
                {
                    var d = stack.Pop();
                    try
                    {
                        foreach (var f in d.EnumerateFiles()) { try { total += f.Length; } catch { } }
                        foreach (var s in d.EnumerateDirectories()) if ((s.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(s);
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        // ---------------------------------------------------------------- the Living Server package's scripts

        void RunPackageScript(string script, string args, InstallResult r, bool extract = false)
        {
            string dir;
            try { dir = Payload.ExtractPackage(gamePath); }
            catch (Exception e) { throw new StepFailed("Couldn't unpack Living Server into the game folder: " + e.Message, "Check that the folder isn't read-only and that antivirus isn't blocking it, then try again."); }
            Log.Write("Living Server package unpacked to " + dir);
            var job = new ScriptJob();
            string refusal = null, partWay = null, lastGreen = null;
            int extra = 0;
            job.Line += (line, err) =>
            {
                if (refusal != null && extra < 6 && line.StartsWith("  ")) { refusal += "\n" + line.Trim(); extra++; }
                var m = Regex.Match(line, @"NOTHING WAS CHANGED:\s*(.+)$");
                if (m.Success) refusal = m.Groups[1].Value.Trim();
                m = Regex.Match(line, @"(?:ROLLBACK|RESTORE) STOPPED PART-WAY:\s*(.+)$");
                if (m.Success) partWay = m.Groups[1].Value.Trim();
                if (line.StartsWith("Install failed:")) partWay = line;
                if (line.StartsWith("Installed ") || line.StartsWith("Already installed") || line.StartsWith("The released server") || line.StartsWith("Rolled back")) lastGreen = line;
                if (!err && Regex.IsMatch(line, @"^\s+runtime[\\/]")) SetStatus(Status, "checking " + line.Trim());
            };
            var code = job.Run(Path.Combine(dir, script), args, CancellationToken.None);
            if (code != 0)
            {
                if (refusal != null)
                {
                    var f = new StepFailed(refusal);
                    if (refusal.Contains("-Force") && !o.ForceLivingServer)
                    {
                        f.CanForce = true;
                        f.Hint = "Setup can replace them anyway: the files there now are backed up first, and ROLLBACK.cmd in the " + Payload.RootFolder + " folder puts them back.";
                    }
                    else if (refusal.Contains("Close the server")) f.Hint = "Quit the game, stop the server and close the launcher, then click Try again.";
                    throw f;
                }
                throw new StepFailed(partWay ?? job.LastError ?? (script + " stopped with code " + code + "."), "The full output is in the log (Show details).");
            }
            if (lastGreen != null) Log.Write(lastGreen);
        }

        /// <summary>After a failed or stopped official update the game is unchanged (the patcher puts everything back),
        /// but the released server was put back for it: reinstall what was there.</summary>
        void RestoreAfterFailedUpdate(InstallResult r)
        {
            if (!rolledBack) return;
            var note = RestorePrevious(gamePath);
            r.Message += " " + (note ?? "Your game is unchanged, but its released server is in place now: run Setup again to add Living Server.");
        }

        /// <summary>Puts the build that was installed before back into a folder. Returns a sentence for the player, or null.</summary>
        string RestorePrevious(string folder)
        {
            try
            {
                var g = GameScanner.Inspect(folder);
                var dummy = new InstallResult();
                if (g.Ready)
                {
                    Log.Write("Putting Living Server back in " + folder + "...");
                    var saved = gamePath; gamePath = folder;
                    try { RunPackageScript("install-server-build.ps1", "-GameFolder " + ScriptJob.Quote(folder), dummy); }
                    finally { gamePath = saved; }
                    return "Living Server " + Payload.DisplayName + " is installed in " + folder + " again.";
                }
                if (previousBuild == null) return null;
                foreach (var info in Directory.GetDirectories(folder).Select(d => Path.Combine(d, "BUILD-INFO.txt")).Where(File.Exists))
                {
                    if (!Regex.IsMatch(File.ReadAllText(info), @"(?m)^BuildName=" + Regex.Escape(previousBuild) + @"\s*$")) continue;
                    var installer = Path.Combine(Path.GetDirectoryName(info), "install-server-build.ps1");
                    if (!File.Exists(installer)) break;
                    Log.Write("Putting " + previousBuild + " back in " + folder + "...");
                    if (new ScriptJob().Run(installer, "-GameFolder " + ScriptJob.Quote(folder), CancellationToken.None) == 0)
                        return "Your " + Wizard.BuildLabel(previousBuild) + " build is back in " + folder + ", as before.";
                    break;
                }
                return "In " + folder + " the released server is in place now: " + Wizard.BuildLabel(previousBuild) + " is off until it's installed again.";
            }
            catch (Exception e)
            {
                Log.Write("Restoring the earlier build: " + e.Message);
                return "Putting the earlier build back didn't work (" + e.Message + "): run Setup again.";
            }
        }

        // ---------------------------------------------------------------- the rest

        void ImportProgress(string from, string to)
        {
            var exe = Path.Combine(to, "tools", "ProgressImporter", "OfflineDaoc.ProgressImport.exe");
            if (!File.Exists(exe)) throw new StepFailed("The new game has no progress importer (tools\\ProgressImporter).", "Your new game is ready without your old characters. IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd in it can bring them over later.");
            var report = Path.Combine(to, "PROGRESS IMPORT REPORT.txt");
            var psi = new ProcessStartInfo(exe, "--import " + ScriptJob.Quote(from) + " " + ScriptJob.Quote(to) + " --replace-progress " + ScriptJob.Quote(report))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var dotnet = Path.Combine(to, "tools", "dotnet");
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnet;
            psi.EnvironmentVariables["DOTNET_ROOT_X64"] = dotnet;
            psi.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            Log.Write("> " + Path.GetFileName(exe) + " " + psi.Arguments);
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, a) => { if (a.Data != null) Log.Write("  " + a.Data); };
                p.ErrorDataReceived += (s, a) => { if (a.Data != null) Log.Write("! " + a.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new StepFailed("Bringing your characters over didn't finish (see PROGRESS IMPORT REPORT.txt in the new game).", "The new game is installed and your old folder is unchanged. IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd in the new game can try again.");
            }
        }
    }

    internal static class NetFx35
    {
        public enum Result { On, Restart, Skipped, Failed }

        /// <summary>Whether the Windows feature is on. Asks Windows' optional-features list (readable without admin); the
        /// registry's NDP\v3.5 "Install" value is only the fallback, because it stays 1 after the feature is turned off
        /// (found on a Windows runner, 2026-10-28).</summary>
        public static bool IsOn()
        {
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher("SELECT InstallState FROM Win32_OptionalFeature WHERE Name='NetFx3'"))
                    foreach (System.Management.ManagementObject f in q.Get())
                        using (f) return Convert.ToInt32(f["InstallState"]) == 1;   // 1 enabled, 2 disabled, 3 absent
            }
            catch (Exception e) { Log.Write("Optional features query: " + e.Message); }
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5"))
                    return k != null && Convert.ToInt32(k.GetValue("Install", 0)) == 1;
            }
            catch { return true; } // can't tell: don't nag
        }

        /// <summary>Turns the Windows feature on with DISM in its own elevated process (Windows shows the UAC prompt).</summary>
        public static Result Enable()
        {
            if (IsOn()) return Result.On;
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dism.exe"), "/online /enable-feature /featurename:NetFx3 /all /norestart /quiet")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                Log.Write("> dism /online /enable-feature /featurename:NetFx3 /all (elevated)");
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    Log.Write("< dism exit " + p.ExitCode);
                    if (p.ExitCode == 0) return Result.On;
                    if (p.ExitCode == 3010) return Result.Restart;
                    return Result.Failed;
                }
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                Log.Write("The permission prompt was declined.");
                return Result.Skipped;
            }
            catch (Exception e)
            {
                Log.Write("dism: " + e.Message);
                return Result.Failed;
            }
        }
    }

    internal static class Shortcuts
    {
        public static void Create(string game)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Offline DAoC");
            Directory.CreateDirectory(menu);
            // Owner 2026-10-28: the shortcuts carry Living Server's own icon (the splash painting's three figures),
            // written beside the launcher; shadowofze's launcher and its exe icon are left as they are.
            var icon = Path.Combine(game, "runtime", "OfflineDAoC.exe");
            try
            {
                var ours = Path.Combine(game, "runtime", "living-server.ico");
                using (var s = Theme.Resource("shortcut.ico")) using (var f = File.Create(ours)) s.CopyTo(f);
                icon = ours;
            }
            catch (Exception e) { Log.Write("Shortcut icon: " + e.Message + " (the launcher's icon is used)"); }
            var targets = new List<(string name, string file)> { ("Offline DAoC", "START OFFLINE DAOC.cmd") };
            if (File.Exists(Path.Combine(game, "START OFFLINE DAOC - NEW CLIENT.cmd")))
                targets.Add(("Offline DAoC - New Client", "START OFFLINE DAOC - NEW CLIENT.cmd"));
            foreach (var (name, file) in targets)
            {
                var target = Path.Combine(game, file);
                if (!File.Exists(target)) continue;
                foreach (var dir in new[] { desktop, menu })
                    Make(Path.Combine(dir, name + ".lnk"), target, game, File.Exists(icon) ? icon : null, "Play Offline DAoC with Living Server");
            }
            var folder = Path.Combine(menu, "Offline DAoC game folder.lnk");
            Make(folder, game, game, null, "Open the Offline DAoC folder");
            Log.Write("Shortcuts added on the desktop and in Start > Offline DAoC.");
        }

        static void Make(string lnk, string target, string workDir, string icon, string description)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic s = shell.CreateShortcut(lnk);
                s.TargetPath = target;
                s.WorkingDirectory = workDir;
                s.Description = description;
                if (icon != null) s.IconLocation = icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? icon : icon + ",0";
                s.Save();
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(s);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
    }
}
