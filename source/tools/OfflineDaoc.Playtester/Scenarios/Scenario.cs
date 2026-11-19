// The frame every scenario runs in: its players, its steps with timings, skips and the result it reports.
using System.Diagnostics;
using OfflineDaoc.Playtester.Protocol;

namespace OfflineDaoc.Playtester.Scenarios;

internal sealed class PlaytestOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 10300;
    public TimeSpan NetTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ServerStartTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ServerStopTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan TellReplyTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan BotsOnlineTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan FactionListen { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>A fixed /faction pass mark (--min-faction-per-minute); null: half the rate the server's chat budget predicts.</summary>
    public double? MinFactionPerMinute { get; set; }
    /// <summary>faction-rate above the server's ceiling (ambient plus replies) fails instead of being flagged (--fail-over-ceiling).</summary>
    public bool FailOverCeiling { get; set; }
    /// <summary>The share of the predicted /faction rate that passes.</summary>
    public double FactionPassShare { get; set; } = 0.5;
    /// <summary>Listen until this many /faction lines are expected at the predicted rate (but at least FactionListen, at most FactionListenMax).</summary>
    public double FactionExpectedLines { get; set; } = 10;
    public TimeSpan FactionListenMax { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>The share of the realm's roster that must be online before the /faction listen starts.</summary>
    public double FactionRosterOnline { get; set; } = 0.8;
}

internal abstract class Scenario
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    /// <summary>What it checks, for `list` and docs/PLAYTESTER.md.</summary>
    public abstract string Checks { get; }
    /// <summary>True when the scenario stops and starts the server itself (it needs --server-dir).</summary>
    public virtual bool RestartsServer => false;

    public abstract Task RunAsync(ScenarioContext ctx);
}

internal sealed class SkipScenario(string reason) : Exception(reason);

internal sealed class ScenarioContext : IAsyncDisposable
{
    private readonly List<ScriptedPlayer> _players = new();
    private readonly Stopwatch _scenarioClock = Stopwatch.StartNew();

    public PlaytestOptions Options { get; }
    public TestWorld World { get; }
    public ServerHost? Server { get; }
    public Stopwatch Clock { get; }
    public ScenarioResult Result { get; }

    public ScenarioContext(Scenario scenario, PlaytestOptions options, TestWorld world, ServerHost? server, Stopwatch clock)
    {
        Options = options;
        World = world;
        Server = server;
        Clock = clock;
        Result = new ScenarioResult { Id = scenario.Id, Title = scenario.Title, StartedUtc = DateTimeOffset.UtcNow };
    }

    /// <summary>A new scripted player (disposed with the scenario; its chat goes into the report).</summary>
    public ScriptedPlayer NewPlayer(string label)
    {
        var player = new ScriptedPlayer(label, Clock, World.ZoneOf, World.ZoneOrigin);
        _players.Add(player);
        return player;
    }

    /// <summary>Connects and logs in a scripted player on a playtester account.</summary>
    public async Task<ScriptedPlayer> LoginAsync(string label, string account)
    {
        ScriptedPlayer player = NewPlayer(label);
        await Step($"{label}: connect and log in as {account}", async () =>
        {
            var watch = Stopwatch.StartNew();
            for (int attempt = 1; ; attempt++)
            {
                await player.ConnectAsync(Options.Host, Options.Port, Options.NetTimeout);
                try
                {
                    await player.LoginAsync(account, account, Options.NetTimeout);
                    return $"plvl {World.AccountPrivLevel(account)}" + (attempt > 1 ? $", after {attempt} tries" : "");
                }
                catch (PlaytestException e) when (e.Message.Contains("eLoginError 11") || e.Message.Contains("eLoginError 17"))
                {
                    // the account's last connection (the scenario before) is still being logged out
                    if (watch.Elapsed > TimeSpan.FromSeconds(90))
                        throw;
                    _players.Remove(player);
                    await player.DisposeAsync();
                    await Task.Delay(3000);
                    player = NewPlayer(label);
                }
            }
        });
        return player;
    }

    /// <summary>Creates a character with a fresh name (or picks the first one with this prefix) and enters the world.</summary>
    public async Task<CharacterSlot> PlayAsync(ScriptedPlayer player, string namePrefix, int realm = 1, int classId = 14, int race = 1)
    {
        CharacterSlot? character = null;
        await Step($"{player.Label}: create a character", async () =>
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    character = await player.CreateCharacterAsync(realm, Names.Fresh(namePrefix), classId, race, false, Options.NetTimeout);
                    break;
                }
                catch (PlaytestException e) when (attempt < 3 && e.Message.Contains("was refused"))
                {
                    // a random name the server's invalid-names list rejects: try another
                }
            }
            return $"{character.Name}, {character.ClassName} {character.RaceName}, level {character.Level}, slot {character.Slot}";
        });
        await Step($"{player.Label}: enter the world", async () =>
        {
            await player.EnterWorldAsync(character!, Options.NetTimeout);
            return $"region {player.Region} zone {player.Zone} at {player.X:0},{player.Y:0},{player.Z:0} (object {player.ObjectId})";
        });
        return character!;
    }

    /// <summary>Runs one step; its exception fails the step and ends the scenario.</summary>
    public async Task Step(string name, Func<Task<string>> body)
    {
        var watch = Stopwatch.StartNew();
        var step = new StepResult { Name = name };
        Result.Steps.Add(step);
        try
        {
            step.Detail = await body();
            step.Outcome = Outcome.Passed;
        }
        catch (SkipScenario skip)
        {
            step.Outcome = Outcome.Skipped;
            step.Detail = skip.Message;
            throw;
        }
        catch (Exception e)
        {
            step.Outcome = Outcome.Failed;
            step.Detail = e is PlaytestException ? e.Message : e.ToString();
            throw new StepFailed(name, step.Detail);
        }
        finally
        {
            step.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 2);
        }
    }

    /// <summary>
    /// A step the scenario may give up on (one monster of several): a failure is recorded as Skipped with its reason and
    /// the scenario goes on. Returns whether it passed.
    /// </summary>
    public async Task<bool> TryStep(string name, Func<Task<string>> body)
    {
        try
        {
            await Step(name, body);
            return true;
        }
        catch (StepFailed)
        {
            var step = Result.Steps[^1];
            step.Outcome = Outcome.Skipped;
            step.Detail = "gave up: " + step.Detail;
            return false;
        }
    }

    public void Measure(string key, object value) => Result.Measured[key] = value;
    public void Todo(string what) => Result.Todo.Add(what);
    public void Flag(string what) => Result.Flags.Add(what);

    public static void Check(bool condition, string failure)
    {
        if (!condition)
            throw new PlaytestException(failure);
    }

    public async ValueTask DisposeAsync()
    {
        // Quit cleanly (all at once), so the next scenario's login is not refused as "already logged in". In the first
        // minute after a server start the quit timer runs up to 60 s (GamePlayer.QuitTimer counts from the server's start).
        await Task.WhenAll(_players.Where(p => p.InWorld && p.Connected).Select(async p =>
        {
            try { await p.QuitAsync(TimeSpan.FromSeconds(75)); } catch (Exception) { }
        }));
        foreach (var p in _players)
        {
            await p.DisposeAsync();
            Result.Chat.AddRange(p.ChatSnapshot());
        }
        Result.Chat.Sort((a, b) => a.At.CompareTo(b.At));
        Result.Seconds = Math.Round(_scenarioClock.Elapsed.TotalSeconds, 2);
    }
}

internal sealed class StepFailed(string step, string detail) : Exception($"{step}: {detail}");

/// <summary>Character names the server accepts: a capital, then letters only (CharacterCreateRequestHandler).</summary>
internal static class Names
{
    public static string Fresh(string prefix)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        var tail = new char[6];
        for (int i = 0; i < tail.Length; i++)
            tail[i] = letters[Random.Shared.Next(letters.Length)];
        return prefix + new string(tail);
    }

    public static bool Valid(string name) =>
        name.Length is >= 3 and <= 23 && char.IsAsciiLetterUpper(name[0]) && name.All(char.IsAsciiLetter);
}
