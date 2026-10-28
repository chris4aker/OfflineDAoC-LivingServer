namespace OfflineDaoc.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--prepare-portable-account")
        {
            // the new client's launcher keeps its account with the game it plays (server_root.txt)
            PortableCredentials.ReadOrCreate(MainForm.ServerRootFor(AppContext.BaseDirectory));
            return;
        }
        if (args.Length == 1 && args[0] == "--apply-recommended-settings")
        {
            // Setup, after a first install: every Living Server feature on at the suggested settings (exit code 0 when saved).
            Environment.ExitCode = ApplyRecommendedSettings();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static int ApplyRecommendedSettings()
    {
        string database = Path.Combine(MainForm.ServerRootFor(AppContext.BaseDirectory), "data", "opendaoc.sqlite3.db");
        try
        {
            bool stopped = System.Diagnostics.Process.GetProcessesByName("CoreServer").Length == 0;
            SocialExpansionSettings.Recommended.Save(database, null, () => stopped);
            var problems = new List<string>();
            bool saved = SocialExpansionSettings.Load(database, problems) == SocialExpansionSettings.Recommended && problems.Count == 0;
            Console.WriteLine(saved ? "Recommended Living Server settings saved in " + database
                                    : "Saved, but reading back differs: " + string.Join("; ", problems));
            return saved ? 0 : 2;
        }
        catch (Exception e)
        {
            Console.WriteLine("Recommended settings not saved: " + e.Message);
            return 1;
        }
    }
}
