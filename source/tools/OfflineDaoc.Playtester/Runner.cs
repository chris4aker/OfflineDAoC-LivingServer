// Runs the chosen scenarios one after the other against one server, and collects the report.
using System.Diagnostics;
using OfflineDaoc.Playtester.Scenarios;

namespace OfflineDaoc.Playtester;

internal static class Runner
{
    public static IReadOnlyList<Scenario> All { get; } = new Scenario[]
    {
        new LoginSaveRestart(), new ChatBetweenPlayers(), new GroupInvite(),
        new TellGamebot(), new FactionRate(), new KillQuestInBotGroup(), new DeliveryQuest(), new GuildDues(),
    };

    /// <summary>The hook scenarios: what they cannot do yet is reported as Incomplete, never as a pass or a failure.</summary>
    private static readonly HashSet<string> Hooks = new();

    public static async Task<RunReport> RunAsync(IReadOnlyList<Scenario> scenarios, PlaytestOptions options, TestWorld world,
        string? serverDir, string workDir, TextWriter progress, bool keepServerCopy = false)
    {
        var clock = Stopwatch.StartNew();
        var report = new RunReport
        {
            StartedUtc = DateTimeOffset.UtcNow,
            World = world.Path,
            WorldMarker = new Dictionary<string, string>(world.Marker),
            Server = serverDir != null ? $"started by the playtester from {Path.GetFullPath(serverDir)}" : $"already running at {options.Host}:{options.Port}",
        };
        await using ServerHost? server = serverDir != null ? new ServerHost(serverDir, world, workDir, options.Port) : null;
        try
        {
            if (server != null)
            {
                progress.WriteLine($"Starting the server on {world.Path} ...");
                await server.StartAsync(options.ServerStartTimeout);
                progress.WriteLine("Server started.");
            }
            else if (!ServerHost.PortInUse(options.Port))
                throw new PlaytestException($"no server answers on {options.Host}:{options.Port} (start one, or pass --server-dir)");

            foreach (var scenario in scenarios)
            {
                progress.WriteLine($"== {scenario.Id}: {scenario.Title}");
                var ctx = new ScenarioContext(scenario, options, world, server, clock);
                try
                {
                    if (server != null && !server.Running)
                        await server.StartAsync(options.ServerStartTimeout);   // a failed restart scenario may leave it stopped
                    await scenario.RunAsync(ctx);
                    ctx.Result.Outcome = ctx.Result.Todo.Count > 0 ? Outcome.Incomplete : Outcome.Passed;
                    ctx.Result.Reason = ctx.Result.Todo.Count > 0 ? $"{ctx.Result.Todo.Count} part(s) still TODO" : "";
                }
                catch (SkipScenario skip)
                {
                    ctx.Result.Outcome = Outcome.Skipped;
                    ctx.Result.Reason = skip.Message;
                }
                catch (Exception e)
                {
                    bool hook = Hooks.Contains(scenario.Id);
                    ctx.Result.Outcome = hook ? Outcome.Incomplete : Outcome.Failed;
                    ctx.Result.Reason = (hook ? "hook stopped at: " : "") + (e is StepFailed or PlaytestException ? e.Message : e.ToString());
                }
                try { await ctx.DisposeAsync(); }
                catch (Exception e) { ctx.Result.Reason += $" (cleaning up: {e.Message})"; }
                report.Scenarios.Add(ctx.Result);
                progress.WriteLine($"   {ctx.Result.Outcome} ({ctx.Result.Seconds:0.0} s){(ctx.Result.Reason.Length > 0 ? ": " + ctx.Result.Reason : "")}");
                foreach (var step in ctx.Result.Steps)
                    progress.WriteLine($"     - {step.Outcome,-9} {step.Seconds,6:0.00} s  {step.Name}{(step.Detail.Length > 0 ? " -> " + Short(step.Detail) : "")}");
                foreach (var (key, value) in ctx.Result.Measured)
                    progress.WriteLine($"     * {key}: {value}");
                foreach (var flag in ctx.Result.Flags)
                    progress.WriteLine($"     ! FLAG: {flag}");
            }
        }
        finally
        {
            if (server != null)
            {
                progress.WriteLine("Stopping the server ...");
                await server.StopAsync(options.ServerStopTimeout);
                report.ServerLogs.AddRange(server.Logs);
                report.ServerErrorLines = server.ErrorLines;
                report.ServerFatalLines.AddRange(server.FatalLines);
                if (!keepServerCopy)
                    try { server.DeleteCopy(); } catch (IOException e) { progress.WriteLine($"Could not remove the server copy: {e.Message}"); }
            }
            report.Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
        }
        return report;
    }

    private static string Short(string text) => text.Length > 160 ? text[..160] + "..." : text;
}
