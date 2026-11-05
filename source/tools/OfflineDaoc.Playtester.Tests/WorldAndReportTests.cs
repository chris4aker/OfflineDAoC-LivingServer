using System.Data.SQLite;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using OfflineDaoc.Playtester;
using OfflineDaoc.Playtester.Scenarios;

namespace OfflineDaoc.Playtester.Tests;

/// <summary>The guard against played worlds, the server config the playtester writes, the command line and the reports.</summary>
public class WorldAndReportTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "playtester-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string MakeDb(bool marker, string format = "1")
    {
        string path = Path.Combine(_dir, $"world-{Guid.NewGuid():N}.db");
        using var con = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False");
        con.Open();
        void Run(string sql) { using var cmd = new SQLiteCommand(sql, con); cmd.ExecuteNonQuery(); }
        Run("CREATE TABLE Zones (ZoneID INT, RegionID INT, Name TEXT, OffsetX INT, OffsetY INT, Width INT, Height INT)");
        Run("INSERT INTO Zones VALUES (0, 1, 'Camelot Hills', 67, 59, 8, 8), (1, 1, 'Salisbury Plains', 67, 67, 8, 8)");
        Run("CREATE TABLE Account (Name TEXT, PrivLevel INT)");
        Run("INSERT INTO Account VALUES ('playtestergm', 3)");
        if (marker)
        {
            Run($"CREATE TABLE {TestWorld.MarkerTable} (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)");
            Run($"INSERT INTO {TestWorld.MarkerTable} VALUES ('format', '{format}'), ('mode', 'minimal')");
        }
        return path;
    }

    [Test]
    public void APlayedWorldIsRefused()
    {
        var e = Assert.Throws<PlaytestException>(() => TestWorld.Open(MakeDb(marker: false)));
        Assert.That(e!.Message, Does.Contain("not a test world"));
    }

    [Test]
    public void AnUnknownTestWorldFormatIsRefused()
    {
        Assert.Throws<PlaytestException>(() => TestWorld.Open(MakeDb(marker: true, format: "9")));
    }

    [Test]
    public void ATestWorldOpensAndPlacesZones()
    {
        var world = TestWorld.Open(MakeDb(marker: true));
        Assert.That(world.IsMinimal, Is.True);
        Assert.That(world.ZoneOf(1, 574123, 530520), Is.EqualTo(0));
        Assert.That(world.ZoneOf(1, 574123, 67 * 8192 + 10), Is.EqualTo(1));
        Assert.That(world.AccountPrivLevel("playtestergm"), Is.EqualTo(3));
        Assert.That(world.Bots(1), Is.Empty, "no bot table: no bots");
    }

    [Test]
    public void TheServerHostRefusesTheInstallsOwnWorld()
    {
        string server = Path.Combine(_dir, "runtime", "server");
        string data = Path.Combine(_dir, "runtime", "data");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(server, "CoreServer.dll"), "");
        string own = Path.Combine(data, "opendaoc.sqlite3.db");
        File.Copy(MakeDb(marker: true), own);
        var world = TestWorld.Open(own);
        var e = Assert.Throws<PlaytestException>(() => new ServerHost(server, world, Path.Combine(_dir, "work"), 10300));
        Assert.That(e!.Message, Does.Contain("own world"));
    }

    [Test]
    public void TheServerRunsFromACopyWithoutTheInstallsRunState()
    {
        string server = Path.Combine(_dir, "runtime", "server");
        Directory.CreateDirectory(Path.Combine(server, "config"));
        Directory.CreateDirectory(Path.Combine(server, "logs"));
        Directory.CreateDirectory(Path.Combine(server, "navmesh"));
        Directory.CreateDirectory(Path.Combine(server, "lib"));
        File.WriteAllText(Path.Combine(server, "CoreServer.dll"), "server");
        File.WriteAllText(Path.Combine(server, "lib", "GameServer.dll"), "game");
        File.WriteAllText(Path.Combine(server, "config", "serverconfig.xml"), "<root/>");
        File.WriteAllText(Path.Combine(server, "bot-goals.json"), "{}");
        File.WriteAllText(Path.Combine(server, "rvr-relic-campaigns.json"), "[]");
        File.WriteAllText(Path.Combine(server, "bot-world.json"), "{}");
        File.WriteAllText(Path.Combine(server, "logs", "server.log"), "log");
        File.WriteAllText(Path.Combine(server, "navmesh", "zone001.nav"), "mesh");
        string run = ServerHost.PrepareCopy(server, Path.Combine(_dir, "work", "server"));
        Assert.That(File.ReadAllText(Path.Combine(run, "lib", "GameServer.dll")), Is.EqualTo("game"));
        Assert.That(File.Exists(Path.Combine(run, "bot-goals.json")), Is.True, "settings are copied");
        Assert.That(File.Exists(Path.Combine(run, "rvr-relic-campaigns.json")), Is.False, "run state is not");
        Assert.That(File.Exists(Path.Combine(run, "bot-world.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(run, "logs")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(run, "navmesh", "zone001.nav")), Is.EqualTo("mesh"), "the navmeshes are linked");
        // what the server writes into its copy stays there
        File.WriteAllText(Path.Combine(run, "rvr-world.json"), "{}");
        Assert.That(File.Exists(Path.Combine(server, "rvr-world.json")), Is.False);
        // removing the copy removes the link, never the install's meshes
        var world = TestWorld.Open(MakeDb(marker: true));
        new ServerHost(server, world, Path.Combine(_dir, "work"), 10300).DeleteCopy();
        Assert.That(Directory.Exists(run), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(server, "navmesh", "zone001.nav")), Is.EqualTo("mesh"));
    }

    [Test]
    public void TheConfigKeepsTheFoldersSettingsButUsesTheTestWorld()
    {
        string existing = Path.Combine(_dir, "serverconfig.xml");
        new XDocument(new XElement("root", new XElement("Server",
            new XElement("ServerName", "Offline DAoC: Classic SI"),
            new XElement("DBType", "MYSQL"),
            new XElement("DBConnectionString", "Server=localhost"),
            new XElement("IP", "0.0.0.0")))).Save(existing);
        var server = XDocument.Parse(ServerHost.BuildConfig(existing, "/tmp/test.db", 10300)).Root!.Element("Server")!;
        Assert.That(server.Element("ServerName")!.Value, Is.EqualTo("Offline DAoC: Classic SI"));
        Assert.That(server.Element("DBType")!.Value, Is.EqualTo("SQLITE"));
        Assert.That(server.Element("DBConnectionString")!.Value, Does.StartWith("Data Source=/tmp/test.db;"));
        Assert.That(server.Element("IP")!.Value, Is.EqualTo("127.0.0.1"), "this PC only");
        Assert.That(server.Element("AutoAccountCreation")!.Value, Is.EqualTo("True"));
        var fresh = XDocument.Parse(ServerHost.BuildConfig(null, "/tmp/t.db", 10301)).Root!.Element("Server")!;
        Assert.That(fresh.Element("Port")!.Value, Is.EqualTo("10301"));
        Assert.That(fresh.Element("RegionPort")!.Value, Is.EqualTo("10401"), "a second test server's UDP and region ports don't collide");
        Assert.That(fresh.Element("UdpPort")!.Value, Is.EqualTo("10401"));
    }

    [Test]
    public void TheBuiltInChatBudgetIsTheServers()
    {
        var budget = ChatBudget.Load(null);
        string server = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..",
            "server", "GameServer", "bots", "autonomous", "data", "chat", "chat-budget.json"));
        var parsed = ChatBudget.Parse(server, "server");
        Assert.That(budget.DesignBots, Is.EqualTo(parsed.DesignBots));
        Assert.That(budget.Hours, Is.EqualTo(parsed.Hours));
        Assert.That(budget.Channels["faction"], Is.EqualTo(parsed.Channels["faction"]));
    }

    [Test]
    public void TheFactionRateIsPredictedAsTheServerDoes()
    {
        // AutonomousChatBudget.Band: target x (bots / design) ^ exponent x hour factor x clamp(rate, 0, 3).
        var budget = ChatBudget.Parse("""
            { // comments and trailing commas, as the server allows
              "design_bots_per_realm": 1000, "population_exponent": 0.65,
              "hours": [1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,0.86,1,1,1,1,1,1],
              "hours_clock_schedule": [0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.5,0.96,0.5,0.5,0.5,0.5,0.5,0.5],
              "channels": { "faction": { "floor": 2.0, "target": 4.0, "ceiling": 10.0, }, },
            }
            """, "test");
        var p = FactionPrediction.Of(budget, 10, 17, clockSchedule: false, chatRate: 2.0, passShare: 0.5);
        double population = Math.Pow(10 / 1000.0, 0.65);
        Assert.That(p.PopulationFactor, Is.EqualTo(population).Within(1e-9));
        Assert.That(p.Expected, Is.EqualTo(4.0 * population * 0.86 * 2.0).Within(1e-9));
        Assert.That(p.Expected, Is.EqualTo(0.345).Within(0.001));
        Assert.That(p.PassMark, Is.EqualTo(p.Expected / 2).Within(1e-9));
        Assert.That(p.Band.Floor, Is.EqualTo(p.Expected / 2).Within(1e-9));
        Assert.That(FactionPrediction.Of(budget, 10, 17, true, 2.0, 0.5).HourFactor, Is.EqualTo(0.96));
        Assert.That(FactionPrediction.Of(budget, 10, 17, false, 9.0, 0.5).Expected, Is.EqualTo(p.Expected * 1.5).Within(1e-9), "the rate is clamped to 3");
        Assert.That(FactionPrediction.Of(budget, 0, 17, false, 2.0, 0.5).BotsOnline, Is.EqualTo(0));
        Assert.That(FactionPrediction.Of(budget, 0, 17, false, 2.0, 0.5).PopulationFactor, Is.EqualTo(Math.Pow(1 / 1000.0, 0.65)).Within(1e-12), "at least one bot");
    }

    [Test]
    public void TheReplyBudgetIsPartOfThePredictionAndAboveTheCeilingIsFlagged()
    {
        // Living Server 1.2: /faction's replies have a budget of their own (AutonomousChatBudget.ReplyBand), scaled like the
        // ambient band but by bot_voices_chat_rate never below 1.
        var budget = ChatBudget.Parse("""
            { "design_bots_per_realm": 1000, "population_exponent": 0.65,
              "channels": { "faction": { "floor": 2.0, "target": 4.0, "ceiling": 10.0, "reply_target": 4.0, "reply_ceiling": 8.0 },
                            "say": { "floor": 1.0, "target": 2.0, "ceiling": 5.0, "reply_target": 3.0 } } }
            """, "test");
        Assert.That(budget.Channels["say"].ReplyCeiling, Is.EqualTo(6.0), "a reply ceiling left out is twice the reply target, as the server reads it");
        var p = FactionPrediction.Of(budget, 10, 20, clockSchedule: false, chatRate: 2.0, passShare: 0.5);
        double scale = Math.Pow(10 / 1000.0, 0.65) * 2.0;
        Assert.That(p.Replies.Target, Is.EqualTo(4.0 * scale).Within(1e-9));
        Assert.That(p.Expected, Is.EqualTo(8.0 * scale).Within(1e-9), "the ambient target plus the reply target");
        Assert.That(p.Expected, Is.EqualTo(0.80).Within(0.01));
        Assert.That(p.PassMark, Is.EqualTo(p.Expected / 2).Within(1e-9));
        Assert.That(p.HighMark, Is.EqualTo(18.0 * scale).Within(1e-9), "the ambient ceiling plus the reply ceiling");
        var none = FactionPrediction.Of(budget, 10, 20, false, 0.0, 0.5);
        Assert.That(none.Band.Target, Is.EqualTo(0), "bot_voices_chat_rate 0: bots start nothing...");
        Assert.That(none.Replies.Target, Is.EqualTo(4.0 * scale / 2).Within(1e-9), "... and still answer");
        // A file without reply values (Beta 1.1.2's) predicts as before.
        var old = ChatBudget.Parse("""{ "channels": { "faction": { "floor": 2.0, "target": 4.0, "ceiling": 10.0 } } }""", "old");
        var q = FactionPrediction.Of(old, 10, 20, false, 2.0, 0.5);
        Assert.That(q.Replies, Is.EqualTo((0.0, 0.0)));
        Assert.That(q.Expected, Is.EqualTo(q.Band.Target));
        Assert.That(q.HighMark, Is.EqualTo(q.Band.Ceiling));
        // The marks.
        Assert.That(FactionPrediction.Judge(30, 1.0, 0.4, 1.8), Is.EqualTo((true, false)));
        Assert.That(FactionPrediction.Judge(40, 1.33, 0.14, 0.73), Is.EqualTo((true, true)), "the 1.1.2 runs: a pass, flagged");
        Assert.That(FactionPrediction.Judge(2, 0.1, 0.3, 1.8), Is.EqualTo((false, false)));
        Assert.That(FactionPrediction.Judge(0, 0, 0, 1.8), Is.EqualTo((false, false)), "no line at all never passes");
        Assert.That(FactionPrediction.Judge(5, 9.0, 0.1, 0), Is.EqualTo((true, false)), "no ceiling, no flag");
    }

    [Test]
    public void AFlagIsReportedWithoutFailingTheRun()
    {
        var report = new RunReport();
        report.Scenarios.Add(new ScenarioResult { Id = "faction-rate", Outcome = Outcome.Passed, Flags = { "/faction ran at 1.33/min, above 0.73/min" } });
        Assert.That(report.AnyFailed, Is.False);
        Assert.That(report.ToMarkdown(), Does.Contain("- FLAG: /faction ran at 1.33/min"));
        Assert.That(report.ToJson(), Does.Contain("\"flags\""));
        Assert.That(Cli.ParseOptions(["--fail-over-ceiling", "--world", "w.db"])["fail-over-ceiling"], Is.EqualTo("true"));
    }

    [Test]
    public void AnInstallsOwnChatBudgetWinsAndTheWorldsSettingsAreRead()
    {
        File.WriteAllText(Path.Combine(_dir, "chat-budget.json"),
            """{"design_bots_per_realm": 100, "channels": {"faction": {"floor": 1, "target": 2, "ceiling": 3}}}""");
        var budget = ChatBudget.Load(_dir);
        Assert.That(budget.DesignBots, Is.EqualTo(100));
        Assert.That(budget.Source, Does.EndWith("chat-budget.json"));
        string world = MakeDb(marker: true);
        Assert.That(FactionPrediction.SettingsOf(TestWorld.Open(world)), Is.EqualTo((2.0, false)), "the server's defaults before its first start");
        using (var con = new SQLiteConnection($"Data Source={world};Version=3;Pooling=False"))
        {
            con.Open();
            using var cmd = new SQLiteCommand("CREATE TABLE ServerProperty (`Key` TEXT, Value TEXT); " +
                "INSERT INTO ServerProperty VALUES ('bot_voices_chat_rate', '1.5'), ('bot_schedule_mode', 'Clock')", con);
            cmd.ExecuteNonQuery();
        }
        Assert.That(FactionPrediction.SettingsOf(TestWorld.Open(world)), Is.EqualTo((1.5, true)));
    }

    [Test]
    public async Task TheCommandLineWantsOneWayToReachTheServer()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string world = MakeDb(marker: true);
        Assert.That(await Cli.MainAsync(new[] { "run", "--world", world }, output, error), Is.EqualTo(2));
        Assert.That(await Cli.MainAsync(new[] { "run", "--world", world, "--attach", "--server-dir", _dir }, output, error), Is.EqualTo(2));
        Assert.That(await Cli.MainAsync(new[] { "run", "--world", MakeDb(false), "--attach" }, output, error), Is.EqualTo(2));
        Assert.That(error.ToString(), Does.Contain("not a test world"));
        Assert.That(await Cli.MainAsync(new[] { "list" }, output, error), Is.EqualTo(0));
        Assert.That(output.ToString(), Does.Contain("login-save-restart"));
    }

    [Test]
    public void ScenariosArePickedByIdAndUnknownOnesAreRefused()
    {
        Assert.That(Cli.Pick("all"), Has.Count.EqualTo(Runner.All.Count));
        Assert.That(Cli.Pick("chat-players, group-players").Select(s => s.Id), Is.EqualTo(new[] { "chat-players", "group-players" }));
        Assert.Throws<ArgumentException>(() => Cli.Pick("nope"));
        Assert.That(Runner.All.Select(s => s.Id), Is.Unique);
    }

    [Test]
    public async Task AStepTheScenarioMayGiveUpOnIsSkippedNotFailed()
    {
        var world = TestWorld.Open(MakeDb(marker: true));
        var ctx = new ScenarioContext(Runner.All[0], new PlaytestOptions(), world, null, System.Diagnostics.Stopwatch.StartNew());
        bool ok = await ctx.TryStep("fight a flier", () => throw new PlaytestException("still alive after 60 s"));
        Assert.That(ok, Is.False);
        Assert.That(ctx.Result.Steps[0].Outcome, Is.EqualTo(Outcome.Skipped));
        Assert.That(ctx.Result.Steps[0].Detail, Does.StartWith("gave up: still alive"));
        Assert.That(await ctx.TryStep("fight a bandit", () => Task.FromResult("The bandit dies!")), Is.True);
        Assert.ThrowsAsync<StepFailed>(async () => await ctx.Step("must pass", () => throw new PlaytestException("no")));
        await ctx.DisposeAsync();
    }

    [Test]
    public void TheReportSaysWhatRanAndWhatIsStillToDo()
    {
        var report = new RunReport { StartedUtc = DateTimeOffset.Parse("2026-11-04T18:00:00Z"), World = "w.db", Server = "test" };
        report.WorldMarker["mode"] = "minimal";
        report.Scenarios.Add(new ScenarioResult
        {
            Id = "chat-players", Title = "Say and tells", Outcome = Outcome.Passed,
            Steps = { new StepResult { Name = "tell | answer", Outcome = Outcome.Passed, Seconds = 0.04, Detail = "ok" } },
            Chat = { new ChatLine(1.5, ChatTypes.Send, "Aldric sends, \"hi\"", "one") },
            Measured = { ["tell seconds"] = 0.04 },
        });
        report.Scenarios.Add(new ScenarioResult { Id = "kill-quest-bot-group", Outcome = Outcome.Incomplete, Todo = { "kill the target" } });
        string md = report.ToMarkdown();
        Assert.That(md, Does.Contain("| chat-players | **Passed** |"));
        Assert.That(md, Does.Contain("tell \\| answer"), "pipes in cells are escaped");
        Assert.That(md, Does.Contain("TODO: kill the target"));
        Assert.That(md, Does.Contain("[tell] Aldric sends"));
        Assert.That(md, Does.Contain("Nothing here was played by a person"));
        using var json = JsonDocument.Parse(report.ToJson());
        var first = json.RootElement.GetProperty("scenarios")[0];
        Assert.That(first.GetProperty("outcome").GetString(), Is.EqualTo("Passed"));
        Assert.That(first.GetProperty("chat")[0].GetProperty("channel").GetString(), Is.EqualTo("tell"));
        Assert.That(report.AnyFailed, Is.False);
    }
}
