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
