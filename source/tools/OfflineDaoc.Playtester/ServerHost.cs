// Starts and stops a game server on a test world, for the scenarios that restart it. The server runs from a COPY of
// the server folder given (runtime/server of an install, or a build's Release folder) in the playtester's work folder,
// because the server writes its run state beside itself (bot-world.json, rvr-world.json, rvr-relic-campaigns.json -
// re-planned at the next start -, bot-ai-delay.json, logs): a test run must leave none of that in the install. The
// navigation meshes are linked, not copied (a junction on Windows). Its config is the folder's serverconfig.xml with
// SQLite on the test world (CoreServer --start -config=...), so the install's own database is never opened; the
// world's ledger and backups go beside the test world. Only one server can use port 10300: the playtester refuses to
// start one while the port is taken.
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OfflineDaoc.Playtester;

internal sealed class ServerHost : IAsyncDisposable
{
    private readonly string _serverDir;                  // the folder given (only read)
    private readonly string _runDir;                     // the copy the server runs from
    private readonly TestWorld _world;
    private readonly string _workDir;
    private readonly int _port;
    private Process? _process;
    private StreamWriter? _log;
    private readonly object _logSync = new();
    private TaskCompletionSource<bool>? _started;
    private int _starts;

    /// <summary>The server folder given (only read): where an install's own chat-budget.json would be.</summary>
    public string ServerDir => _serverDir;
    public string ConfigPath => Path.Combine(_workDir, "serverconfig.xml");
    public string? CurrentLog { get; private set; }
    public List<string> Logs { get; } = new();
    public int ErrorLines { get; private set; }
    public List<string> FatalLines { get; } = new();
    public bool Running => _process is { HasExited: false };

    public ServerHost(string serverDir, TestWorld world, string workDir, int port)
    {
        _serverDir = Path.GetFullPath(serverDir);
        _world = world;
        _workDir = Path.GetFullPath(workDir);
        _port = port;
        _runDir = Path.Combine(_workDir, "server");
        if (!File.Exists(Path.Combine(_serverDir, "CoreServer.dll")) && !File.Exists(Path.Combine(_serverDir, "CoreServer.exe")))
            throw new PlaytestException($"{_serverDir} has no CoreServer: pass the server folder (runtime\\server, or source/server/Release)");
        string own = Path.GetFullPath(Path.Combine(_serverDir, "..", "data", "opendaoc.sqlite3.db"));
        if (string.Equals(own, world.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new PlaytestException($"{world.Path} is this install's own world: put the test world in a file of its own");
    }

    // The server's run state and logs, never copied in (a fresh run starts without them).
    private static readonly HashSet<string> RunState = new(StringComparer.OrdinalIgnoreCase)
    {
        "bot-world.json", "bot-world.request", "rvr-world.json", "rvr-relic-campaigns.json", "bot-ai-delay.json",
        "realm-events.request.json", "realm-events.result.json", "realm-event-records.sqlite3", "account.txt",
    };

    /// <summary>Copies the server folder into the work folder (once), without its logs and run state, navmesh linked.</summary>
    internal static string PrepareCopy(string serverDir, string runDir, TextWriter? note = null)
    {
        if (File.Exists(Path.Combine(runDir, ".playtester-copy")))
            return runDir;
        Directory.CreateDirectory(runDir);
        foreach (string dir in Directory.EnumerateDirectories(serverDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(serverDir, dir);
            string top = rel.Split(Path.DirectorySeparatorChar)[0];
            if (top.Equals("navmesh", StringComparison.OrdinalIgnoreCase) || top.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                top.Equals("backups", StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.Combine(runDir, rel));
        }
        foreach (string file in Directory.EnumerateFiles(serverDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(serverDir, file);
            string top = rel.Split(Path.DirectorySeparatorChar)[0];
            if (top.Equals("navmesh", StringComparison.OrdinalIgnoreCase) || top.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                top.Equals("backups", StringComparison.OrdinalIgnoreCase) || RunState.Contains(Path.GetFileName(file)) ||
                file.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(runDir, rel), overwrite: true);
        }
        string navmesh = Path.Combine(serverDir, "navmesh");
        if (Directory.Exists(navmesh))
        {
            string link = Path.Combine(runDir, "navmesh");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // a directory junction needs no administrator rights (a symbolic link does)
                    using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{navmesh}\"")
                        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
                    mklink.WaitForExit();
                    if (mklink.ExitCode != 0) throw new IOException("mklink /J failed");
                }
                else
                    Directory.CreateSymbolicLink(link, navmesh);
            }
            catch (Exception e)
            {
                note?.WriteLine($"Could not link the navmeshes ({e.Message}); the test server runs without them.");
            }
        }
        File.WriteAllText(Path.Combine(runDir, ".playtester-copy"), $"copied from {serverDir} at {DateTime.UtcNow:O}\n");
        return runDir;
    }

    /// <summary>The config the server runs with: its folder's own settings, SQLite on the test world.</summary>
    public static string BuildConfig(string? existingConfig, string worldPath, int port)
    {
        XDocument doc = existingConfig != null && File.Exists(existingConfig)
            ? XDocument.Load(existingConfig)
            : new XDocument(new XElement("root", new XElement("Server")));
        XElement server = doc.Root!.Element("Server") ?? new XElement("Server");
        if (server.Parent == null)
            doc.Root.Add(server);
        void Set(string name, string value)
        {
            var e = server.Element(name);
            if (e == null) server.Add(new XElement(name, value)); else e.Value = value;
        }
        Set("Port", port.ToString());
        // The region and UDP ports follow (10300 -> 10400, the server's defaults), so two test servers on two ports don't collide.
        Set("RegionPort", (port + 100).ToString());
        Set("UdpPort", (port + 100).ToString());
        Set("IP", "127.0.0.1");
        Set("RegionIP", "127.0.0.1");
        Set("UdpIP", "127.0.0.1");
        Set("EnableUPnP", "False");
        Set("DetectRegionIP", "False");
        Set("AutoAccountCreation", "True");
        Set("DBType", "SQLITE");
        Set("DBConnectionString", $"Data Source={worldPath};Version=3;Pooling=True;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60");
        if (server.Element("LogConfigFile") == null) Set("LogConfigFile", "./config/logconfig.xml");
        return doc.ToString();
    }

    public static bool PortInUse(int port)
    {
        try
        {
            using var probe = new TcpClient();
            var connect = probe.ConnectAsync("127.0.0.1", port);
            return connect.Wait(TimeSpan.FromMilliseconds(500)) && probe.Connected;
        }
        catch (Exception) { return false; }
    }

    public async Task StartAsync(TimeSpan timeout)
    {
        if (Running)
            throw new PlaytestException("the server is already running");
        if (PortInUse(_port))
            throw new PlaytestException($"port {_port} is in use: another server is running (only one may use it). Stop it first.");
        Directory.CreateDirectory(_workDir);
        PrepareCopy(_serverDir, _runDir, System.Console.Out);
        File.WriteAllText(ConfigPath, BuildConfig(Path.Combine(_serverDir, "config", "serverconfig.xml"), _world.Path, _port));
        _starts++;
        CurrentLog = Path.Combine(_workDir, $"server-console-{_starts}.log");
        Logs.Add(CurrentLog);
        _log = new StreamWriter(CurrentLog) { AutoFlush = true };
        string exe = Path.Combine(_runDir, "CoreServer.exe");
        bool native = OperatingSystem.IsWindows() && File.Exists(exe);
        var info = new ProcessStartInfo(native ? exe : DotnetHost());
        if (!native)
            info.ArgumentList.Add(Path.Combine(_runDir, "CoreServer.dll"));
        info.ArgumentList.Add("--start");
        info.ArgumentList.Add($"-config={ConfigPath}");
        info.WorkingDirectory = _runDir;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        // A playable folder bundles its .NET in tools\dotnet (runtime\server -> ..\..\tools\dotnet), as the launcher uses it.
        string bundled = Path.GetFullPath(Path.Combine(_serverDir, "..", "..", "tools", "dotnet"));
        if (Directory.Exists(bundled))
        {
            info.Environment["DOTNET_ROOT"] = bundled;
            info.Environment["DOTNET_ROOT_X64"] = bundled;
            info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }
        _started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        _process.Exited += (_, _) => _started?.TrySetResult(false);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        var done = await Task.WhenAny(_started.Task, Task.Delay(timeout));
        if (done != _started.Task || !_started.Task.Result)
        {
            await StopAsync(TimeSpan.FromSeconds(30));
            throw new PlaytestException($"the server did not start within {timeout.TotalSeconds:0} s (see {CurrentLog})");
        }
        // The listener is up once startup completed; wait until the port answers.
        for (int i = 0; i < 50 && !PortInUse(_port); i++)
            await Task.Delay(200);
    }

    private static string DotnetHost()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return root != null && File.Exists(Path.Combine(root, name)) ? Path.Combine(root, name) : name;
    }

    private static readonly Regex Error = new(@"\| (ERR|FATAL) \|", RegexOptions.Compiled);

    private void OnLine(string? line)
    {
        if (line == null)
            return;
        lock (_logSync)
        {
            _log?.WriteLine(line);
            if (Error.IsMatch(line))
            {
                ErrorLines++;
                if (line.Contains("| FATAL |") || line.Contains("Unhandled exception"))
                    FatalLines.Add(line.Length > 300 ? line[..300] : line);
            }
        }
        if (line.Contains("GameServer startup completed", StringComparison.Ordinal))
            _started?.TrySetResult(true);
    }

    /// <summary>A command typed at the server console ("/..." runs as the console's own admin client).</summary>
    public void Console(string line)
    {
        if (!Running)
            throw new PlaytestException("the server is not running");
        _process!.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    /// <summary>Stops the server the way its console does ("exit": every player and bot saved). Returns the exit code.</summary>
    public async Task<int?> StopAsync(TimeSpan timeout)
    {
        if (_process == null)
            return null;
        if (!_process.HasExited)
        {
            try { Console("exit"); } catch (Exception) { }
            var exited = _process.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(timeout)) != exited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync();
                lock (_logSync) _log?.WriteLine("[playtester] the server did not stop in time and was killed");
            }
        }
        int code = _process.ExitCode;
        _process.Dispose();
        _process = null;
        lock (_logSync)
        {
            _log?.Dispose();
            _log = null;
        }
        // The port is free again once the process is gone; give the OS a moment.
        for (int i = 0; i < 25 && PortInUse(_port); i++)
            await Task.Delay(200);
        return code;
    }

    /// <summary>Removes the server copy (its console logs stay in the work folder). A link is removed, never followed.</summary>
    public void DeleteCopy()
    {
        if (Running || !Directory.Exists(_runDir))
            return;
        var navmesh = new DirectoryInfo(Path.Combine(_runDir, "navmesh"));
        if (navmesh.Exists && (navmesh.LinkTarget != null || navmesh.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            navmesh.Delete();                            // the junction or symlink itself (not recursive): never the meshes
        Directory.Delete(_runDir, true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Running)
            await StopAsync(TimeSpan.FromSeconds(120));
    }
}
