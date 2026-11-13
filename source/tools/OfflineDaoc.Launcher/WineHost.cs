using Microsoft.Win32;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Living Server on Linux runs this same launcher, its server and the game under Wine (docs/LINUX.md §0, from Ryan's
/// setup, 2026-11-13). Wine has no Windows Firewall and no Windows PowerShell, so the two places that use them ask here
/// first: Host for Friends skips its firewall step, and an update is installed by the Linux script instead of
/// apply-update.ps1. Everything else runs as on Windows.
/// </summary>
public static class WineHost
{
    /// <summary>The script, beside the game's runtime folder, that does what Windows-only steps do on Linux.</summary>
    public const string LinuxScript = "linux/offlinedaoc";

    private static readonly Lazy<bool> Detected = new(Detect);

    /// <summary>Whether this launcher runs under Wine. Wine keeps its own key in every prefix's registry.</summary>
    public static bool IsWine => Override ?? Detected.Value;

    /// <summary>For tests.</summary>
    internal static bool? Override { get; set; }

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"Software\Wine");
            return key != null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>What a Linux host does instead of the Windows Firewall step: most desktops have no firewall on; if one is on,
    /// these open the game's ports (shown, never run: they need the player's own sudo).</summary>
    public static string FirewallNote(IEnumerable<(string Protocol, int Port)> ports)
    {
        var list = ports.ToList();
        string ufw = string.Join(" && ", list.Select(p => $"sudo ufw allow {p.Port}/{p.Protocol.ToLowerInvariant()}"));
        string firewalld = string.Join(" && ", list.Select(p => $"sudo firewall-cmd --add-port={p.Port}/{p.Protocol.ToLowerInvariant()}"));
        return "On Linux there is no Windows Firewall step. If your computer runs a firewall, open the game's ports yourself: " +
               $"{ufw}  (Ubuntu), or {firewalld}  (Fedora).";
    }

    /// <summary>A Wine path as Linux shows it: Wine's Z: drive is the Linux root.</summary>
    public static string LinuxPath(string windowsPath)
    {
        string path = windowsPath.Replace('\\', '/');
        return path.StartsWith("Z:/", StringComparison.OrdinalIgnoreCase) ? path[2..] : path;
    }

    /// <summary>How a Linux player installs an update the launcher found.</summary>
    public static string UpdateNote(string gameFolder) =>
        "On Linux, updates are installed from a terminal, with the server and the game closed:" + Environment.NewLine +
        Environment.NewLine + $"  {LinuxPath(Path.Combine(gameFolder, LinuxScript))} update" + Environment.NewLine +
        Environment.NewLine + "It downloads the same package, checks it the same way and keeps the same backups (rollback undoes it).";
}
