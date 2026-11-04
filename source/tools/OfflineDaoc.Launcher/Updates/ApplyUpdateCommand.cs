namespace OfflineDaoc.Updates;

// OfflineDAoC.exe --apply-update <package.zip> [--sha256 <hex>] [--tag v<build>] [--game <folder>] [--no-restart] [--no-pause]
//
// The update path without the window or the dialog, for tests and support: the same checks and hand-over as the
// GET UPDATE button, from a package already on disk. Its SHA-256 must be given (as GitHub lists it for the release
// file) or be in the .sha256 file the launcher wrote beside a package it downloaded. The game folder is the one this
// launcher belongs to unless --game names another. Exit codes: 0 handed over to apply-update.ps1 (its result goes to
// runtime\updates\last-update.txt), 2 refused (nothing changed), 3 the game runs, 4 bad arguments, 1 failed.
public static class ApplyUpdateCommand
{
    public static int Run(string[] args, string serverRoot)
    {
        string? Get(string key)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string key) => args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));

        string? zip = Get("--apply-update");
        if (zip == null)
        {
            Console.WriteLine("Usage: OfflineDAoC.exe --apply-update <package.zip> [--sha256 <hex>] [--tag v<build>] [--game <folder>] [--no-restart] [--no-pause]");
            return 4;
        }
        string game = Get("--game") ?? LauncherUpdater.GameFolderFor(serverRoot);
        using var web = new HttpReleaseWeb(LauncherUpdater.UserAgent);
        var updater = new LauncherUpdater(game, web);
        void Say(string line)
        {
            Console.WriteLine(line);
            updater.Log.Write(line);
        }
        Say($"--apply-update {zip} for the game in {updater.GameFolder} (launcher build {LauncherBuild.Name})");
        try
        {
            List<string> running = LauncherUpdater.RunningParts(Environment.ProcessId, Launcher.ServerPortProbe.IsListening());
            if (running.Count > 0)
            {
                Say("Not updating while running: " + string.Join(", ", running));
                return 3;
            }
            PreparedUpdate prepared = updater.PrepareFromFile(zip, Get("--sha256"), Get("--tag"));
            string? restart = Has("--no-restart") ? null : Environment.ProcessPath;
            var handoff = updater.StartHandoff(prepared, Environment.ProcessId, restart, Has("--no-pause"));
            Say($"Handed over to apply-update.ps1 (process {handoff.Id}); its result goes to {Path.Combine(updater.UpdatesFolder, LauncherUpdater.ResultFile)}");
            return 0;
        }
        catch (PackageRefusedException e)
        {
            Say("REFUSED, nothing changed: " + e.Message);
            return 2;
        }
        catch (Exception e)
        {
            Say("FAILED: " + e.Message);
            return 1;
        }
    }
}
