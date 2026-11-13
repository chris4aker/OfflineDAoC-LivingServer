// Hand port from rambodolphin13/DividedRealms 3c6cc9d: the borderless-fullscreen setup moved out of MainForm so Join Friend shares it.
namespace OfflineDaoc.Launcher;

internal static class ClientDisplayPreferences
{
    // Start the original client borderless fullscreen, in this installation's own client profile
    // (paths.dat settings=), never another copy's preferences.
    internal static void EnsureBorderlessFullscreen(string clientDirectory)
    {
        string profile = "OfflineDAoC036";
        string pathsFile = Path.Combine(clientDirectory, "paths.dat");
        if (File.Exists(pathsFile))
        {
            string? value = File.ReadLines(pathsFile).FirstOrDefault(line => line.TrimStart().StartsWith("settings=", StringComparison.OrdinalIgnoreCase));
            if (value != null)
            {
                string candidate = value[(value.IndexOf('=') + 1)..].Trim();
                if (candidate.Length > 0 && candidate.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')) profile = candidate;
            }
        }
        var profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Electronic Arts", "Dark Age of Camelot", profile);
        Directory.CreateDirectory(profileDirectory);
        var path = Path.Combine(profileDirectory, "user.dat");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var mainIndex = lines.FindIndex(line => line.Trim().Equals("[main]", StringComparison.OrdinalIgnoreCase));
        if (mainIndex < 0)
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add(string.Empty);
            lines.Add("[main]");
            lines.Add("fullscreen_windowed=1");
        }
        else
        {
            var sectionEnd = lines.FindIndex(mainIndex + 1, line => line.TrimStart().StartsWith('['));
            if (sectionEnd < 0) sectionEnd = lines.Count;
            var settingIndex = -1;
            for (var index = mainIndex + 1; index < sectionEnd; index++)
            {
                if (lines[index].TrimStart().StartsWith("fullscreen_windowed=", StringComparison.OrdinalIgnoreCase))
                {
                    settingIndex = index;
                    break;
                }
            }
            if (settingIndex >= 0) lines[settingIndex] = "fullscreen_windowed=1";
            else lines.Insert(sectionEnd, "fullscreen_windowed=1");
        }
        File.WriteAllLines(path, lines);
    }
}
