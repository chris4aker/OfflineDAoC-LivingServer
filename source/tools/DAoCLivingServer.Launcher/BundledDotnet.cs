namespace DAoCLivingServer.Launcher;

/// <summary>
/// The .NET the game ships in <c>tools\dotnet</c> (issue #732). <c>START OFFLINE DAOC.cmd</c> and its shortcut set
/// DOTNET_ROOT to that folder before opening the launcher, and CoreServer inherits the launcher's environment. A
/// launcher opened straight from its exe skips that, so CoreServer looked for a system-wide .NET 10 and, on a PC without
/// one, closed at once while START SERVER seemed to do nothing. The launcher now points CoreServer at the bundled
/// runtime itself, and says so when the server closes within seconds of START SERVER.
/// </summary>
public static class BundledDotnet
{
    /// <summary>A server that stops by itself this soon after START SERVER never got going.</summary>
    public static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The game folder's bundled runtime, <c>&lt;runtime&gt;\..\tools\dotnet</c> (the runtime folder is where
    /// <c>START OFFLINE DAOC.cmd</c> opens the launcher from), or null when that folder holds no runtime.
    /// </summary>
    public static string? Find(string runtimeRoot)
    {
        string folder = Path.GetFullPath(Path.Combine(runtimeRoot, "..", "tools", "dotnet"));
        bool holdsRuntime = Directory.Exists(Path.Combine(folder, "host", "fxr"))
                            && Directory.Exists(Path.Combine(folder, "shared", "Microsoft.NETCore.App"));
        return holdsRuntime ? folder : null;
    }

    /// <summary>
    /// Sets the three variables <c>START OFFLINE DAOC.cmd</c> sets, so CoreServer uses the bundled runtime whatever the
    /// launcher's own environment. Without a bundled runtime the environment stays as inherited. Returns the folder used.
    /// </summary>
    public static string? Apply(IDictionary<string, string?> environment, string runtimeRoot)
    {
        string? folder = Find(runtimeRoot);
        if (folder is null)
            return null;
        environment["DOTNET_ROOT"] = folder;
        environment["DOTNET_ROOT_X64"] = folder;
        environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        return folder;
    }

    /// <summary>
    /// The message for a server that closed by itself within <see cref="EarlyExitWindow"/> of START SERVER, or null
    /// when it was asked to stop or ran longer (the footer's "stopped by itself" line covers those).
    /// </summary>
    public static string? EarlyExitMessage(TimeSpan ranFor, int? exitCode, bool askedToStop, bool usedBundled)
    {
        if (askedToStop || ranFor >= EarlyExitWindow)
            return null;
        string code = exitCode is int c ? $" (exit code {c})" : "";
        int seconds = (int)Math.Round(Math.Max(0, ranFor.TotalSeconds));
        string when = seconds < 1 ? "right after START SERVER"
            : seconds == 1 ? "1 second after START SERVER"
            : $"{seconds} seconds after START SERVER";
        string advice = usedBundled
            ? "The end of runtime\\logs\\server-console.log says why."
            : "This game's own .NET (the tools\\dotnet folder in the game folder) is missing, so the server looked for " +
              "one installed on this PC and found none it could use. Reinstall the game, or extract the whole download " +
              "again, then click START SERVER again.";
        return $"The server closed {when}{code}.\n\n" + advice;
    }
}
