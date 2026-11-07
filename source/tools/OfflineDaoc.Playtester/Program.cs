// OfflineDaoc.Playtester: the automated playtester (docs/PLAYTESTER.md).
//
//   OfflineDaoc.Playtester list
//   OfflineDaoc.Playtester run --world <test world .db> --server-dir <runtime\server> [--scenarios all|id,id] [--out <folder>]
//   OfflineDaoc.Playtester run --world <test world .db> --attach            (a server already running on that world)
//
// Exit codes: 0 nothing failed (skipped and incomplete hooks allowed), 1 a scenario failed, 2 bad arguments or setup.
using OfflineDaoc.Playtester;
using OfflineDaoc.Playtester.Scenarios;

return await Cli.MainAsync(args, Console.Out, Console.Error);

internal static class Cli
{
    public const string Usage = """
        OfflineDaoc.Playtester - a scripted player for Offline DAoC test worlds

          list                                  the scenarios and what they check
          run --world <file> --server-dir <dir> start a server on the test world, run, stop it
          run --world <file> --attach           use a server already running on that test world
                                                (login-save-restart is skipped: it restarts the server)
        Options for run:
          --scenarios all|<id>,<id>   default all
          --out <folder>              report folder (default playtest-reports/<UTC time>)
          --host <ip> --port <n>      default 127.0.0.1 10300
          --reply-seconds <n>         a gamebot's tell reply time limit (default 45)
          --listen-seconds <n>        faction-rate listens at least this long (default 180) and until 10 lines are
                                      expected at the predicted rate
          --max-listen-seconds <n>    ... but at most this long (default 1800)
          --min-faction-per-minute <x> a fixed pass mark for faction-rate (default: half the rate the server's
                                      chat budget predicts for the bots online, the hour and bot_voices_chat_rate:
                                      its ambient target plus its reply target)
          --fail-over-ceiling         faction-rate fails, instead of flagging, a rate above the server's ceiling
                                      (the ambient ceiling plus the reply ceiling)
          --bots-online-seconds <n>   how long to wait for gamebots to log in (default 300)
          --keep-server-copy          keep the copy of the server folder the run used (in the report folder)
        The world must be a test world from source/tools/make_test_world.py: the playtester refuses any other database.
        """;

    public static async Task<int> MainAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            output.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        if (args[0] == "list")
        {
            foreach (var s in Runner.All)
                output.WriteLine($"{s.Id,-22} {s.Title}\n{"",-22} {s.Checks}\n");
            return 0;
        }
        if (args[0] != "run")
        {
            error.WriteLine($"Unknown command {args[0]}.\n\n{Usage}");
            return 2;
        }
        Dictionary<string, string> opts;
        try { opts = ParseOptions(args.Skip(1).ToArray()); }
        catch (ArgumentException e) { error.WriteLine(e.Message); return 2; }

        var options = new PlaytestOptions();
        try
        {
            if (opts.TryGetValue("host", out var host)) options.Host = host;
            if (opts.TryGetValue("port", out var port)) options.Port = int.Parse(port);
            if (opts.TryGetValue("reply-seconds", out var reply)) options.TellReplyTimeout = TimeSpan.FromSeconds(double.Parse(reply));
            if (opts.TryGetValue("listen-seconds", out var listen)) options.FactionListen = TimeSpan.FromSeconds(double.Parse(listen, System.Globalization.CultureInfo.InvariantCulture));
            if (opts.TryGetValue("max-listen-seconds", out var maxListen)) options.FactionListenMax = TimeSpan.FromSeconds(double.Parse(maxListen, System.Globalization.CultureInfo.InvariantCulture));
            if (opts.TryGetValue("min-faction-per-minute", out var min)) options.MinFactionPerMinute = double.Parse(min, System.Globalization.CultureInfo.InvariantCulture);
            if (opts.ContainsKey("fail-over-ceiling")) options.FailOverCeiling = true;
            if (opts.TryGetValue("bots-online-seconds", out var bots)) options.BotsOnlineTimeout = TimeSpan.FromSeconds(double.Parse(bots));
        }
        catch (FormatException e) { error.WriteLine($"Bad number: {e.Message}"); return 2; }

        if (!opts.TryGetValue("world", out var worldPath))
        {
            error.WriteLine("--world <test world .db> is required.");
            return 2;
        }
        bool attach = opts.ContainsKey("attach");
        opts.TryGetValue("server-dir", out var serverDir);
        if (attach == (serverDir != null))
        {
            error.WriteLine("Give either --server-dir <dir> (the playtester starts the server) or --attach (one is running).");
            return 2;
        }
        IReadOnlyList<Scenario> scenarios;
        try { scenarios = Pick(opts.GetValueOrDefault("scenarios", "all")); }
        catch (ArgumentException e) { error.WriteLine(e.Message); return 2; }

        TestWorld world;
        try { world = TestWorld.Open(worldPath); }
        catch (PlaytestException e) { error.WriteLine(e.Message); return 2; }

        string outDir = opts.GetValueOrDefault("out") ?? Path.Combine("playtest-reports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        RunReport report;
        try
        {
            report = await Runner.RunAsync(scenarios, options, world, serverDir, outDir, output, opts.ContainsKey("keep-server-copy"));
        }
        catch (PlaytestException e)
        {
            error.WriteLine(e.Message);
            return 2;
        }
        var (json, md) = report.Write(outDir);
        output.WriteLine($"Report: {md}\n        {json}");
        foreach (var s in report.Scenarios)
            output.WriteLine($"{s.Outcome,-11} {s.Id}");
        return report.AnyFailed ? 1 : 0;
    }

    internal static IReadOnlyList<Scenario> Pick(string spec)
    {
        if (spec == "all")
            return Runner.All;
        var picked = new List<Scenario>();
        foreach (var id in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            picked.Add(Runner.All.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unknown scenario {id} (see `list`)."));
        return picked;
    }

    internal static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
                throw new ArgumentException($"Unexpected argument {args[i]}.");
            string key = args[i][2..];
            if (key is "attach" or "keep-server-copy" or "fail-over-ceiling")
            {
                opts[key] = "true";
                continue;
            }
            if (i + 1 >= args.Length)
                throw new ArgumentException($"--{key} needs a value.");
            opts[key] = args[++i];
        }
        return opts;
    }
}
