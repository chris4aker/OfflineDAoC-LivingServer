#nullable enable
using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using DAoCLivingServer.Updates;

namespace DAoCLivingServer.Launcher.Tests;

// Test builds (issue #637): release candidates published as GitHub pre-releases (v1.2.3-rc.1), the launcher's
// "Offer test builds (pre-releases)" channel, and "Install into a new test folder". No test touches the network.
[TestFixture]
public class TestBuildVersionTests
{
    [TestCase("1.2.2", "1.2.3-rc.1")]
    [TestCase("1.2.3-beta", "1.2.3-rc.1")]
    [TestCase("1.2.3-beta.4", "1.2.3-rc.1")]
    [TestCase("1.2.3-rc.1", "1.2.3-rc.2")]
    [TestCase("1.2.3-rc.2", "1.2.3-rc.10")]
    [TestCase("1.2.3-rc.9", "1.2.3")]
    [TestCase("v1.2.3-rc.1", "v1.2.3")]
    [TestCase("1.2.3", "1.2.4-rc.1")]
    [TestCase("1.2.3-rc.5", "1.2.4-rc.1")]
    [TestCase("1.2.3-rc.1", "1.3")]
    public void CandidatesComeAfterTheBetasAndBeforeTheFinal(string older, string newer)
    {
        BuildVersion a = BuildVersion.Parse(older)!, b = BuildVersion.Parse(newer)!;
        Assert.That(b.IsNewerThan(a), Is.True, $"{newer} > {older}");
        Assert.That(a.IsNewerThan(b), Is.False, $"{older} < {newer}");
    }

    [Test]
    public void ACandidatesPackageIsItsFinalBuild()
    {
        BuildVersion rc = BuildVersion.Parse("v1.2.3-rc.2")!;
        Assert.That(rc.IsCandidate, Is.True);
        Assert.That(rc.Candidate, Is.EqualTo(2));
        Assert.That(rc.Name, Is.EqualTo("1.2.3-rc.2"));
        Assert.That(rc.Tag, Is.EqualTo("v1.2.3-rc.2"));
        Assert.That(rc.DisplayName, Is.EqualTo("1.2.3 test build 2"));
        Assert.That(rc.PackageBuild.Name, Is.EqualTo("1.2.3"));
        Assert.That(rc.PackageBuild.IsCandidate, Is.False);
        Assert.That(rc.SameBuildAs(BuildVersion.Parse("1.2.3")), Is.False);
        BuildVersion final = BuildVersion.Parse("1.2.3")!;
        Assert.That(final.PackageBuild, Is.SameAs(final));
        Assert.That(ReleaseInfo.PackageName(rc), Is.EqualTo("DAoC-LivingServer-1.2.3-rc.2.zip"));
    }

    [TestCase("1.2.3-rc.0")]
    [TestCase("1.2.3-rc1")]
    [TestCase("1.2.3-rc")]
    [TestCase("1.2.3-beta-rc.1")]
    [TestCase("1.2.3-rc.1-beta")]
    public void MalformedCandidateTagsAreNotBuilds(string tag) => Assert.That(BuildVersion.Parse(tag), Is.Null);
}

[TestFixture]
public class TestBuildChannelTests
{
    private string _game = "";

    [SetUp]
    public void SetUp() => _game = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_game, true);

    private static string Releases(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private static string Rc(string tag, byte[]? package = null) => UpdateFixtures.ReleaseJson(tag, package ?? [1], prerelease: true);

    [Test]
    public void ThePreReleaseChannelIsOffByDefault()
    {
        Assert.That(new UpdateSettings().OfferTestBuilds, Is.False);
        Assert.That(UpdateSettings.Load(_game).OfferTestBuilds, Is.False, "no settings file");
    }

    [Test]
    public void APreReleaseIsIgnoredWithTheSwitchOff()
    {
        var web = new FakeReleaseWeb { Json = UpdateFixtures.ReleaseJson("v1.2.2", [1]), ReleasesJson = Releases(Rc("v1.2.3-rc.1")) };
        var updater = new LauncherUpdater(_game, web);
        Assert.That(updater.Check(BuildVersion.Parse("1.2.2"), true, DateTime.UtcNow, out _), Is.Null);
        Assert.That(web.ReleasesCalls, Is.EqualTo(0), "the list of releases isn't even asked for");
        ReleaseInfo rc = ReleaseInfo.Parse(Rc("v1.2.3-rc.1"))!;
        Assert.That(UpdatePolicy.ShouldOfferTest(rc, BuildVersion.Parse("1.2.2"), new UpdateSettings(), asked: true), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(rc, BuildVersion.Parse("1.2.2"), new UpdateSettings { OfferTestBuilds = true }, asked: true), Is.False,
            "the normal channel never offers a pre-release");
    }

    [Test]
    public void APreReleaseIsOfferedWithTheSwitchOn()
    {
        var web = new FakeReleaseWeb
        {
            Json = UpdateFixtures.ReleaseJson("v1.2.2", [1]),
            ReleasesJson = Releases(Rc("v1.2.3-rc.2"), Rc("v1.2.3-rc.1"), UpdateFixtures.ReleaseJson("v1.2.2", [1])),
        };
        var updater = new LauncherUpdater(_game, web);
        updater.Settings.OfferTestBuilds = true;
        ReleaseInfo? offered = updater.Check(BuildVersion.Parse("1.2.2"), true, DateTime.UtcNow, out string? problem);
        Assert.That(problem, Is.Null);
        Assert.That(offered?.TagName, Is.EqualTo("v1.2.3-rc.2"), "the newest test build");
        Assert.That(offered!.IsTestBuild, Is.True);
        Assert.That(web.ReleasesCalls, Is.EqualTo(1));
        Assert.That(File.ReadAllText(updater.Log.Path), Does.Contain("(test build)"));
    }

    [Test]
    public void TheFinalWinsOverItsCandidatesAndOlderCandidatesAreNotOffered()
    {
        var on = new UpdateSettings { OfferTestBuilds = true };
        ReleaseInfo final = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3", [1]))!;
        var list = new[] { ReleaseInfo.Parse(Rc("v1.2.3-rc.2"))!, ReleaseInfo.Parse(Rc("v1.2.3-rc.1"))! };
        Assert.That(UpdatePolicy.Choose(final, list, BuildVersion.Parse("1.2.3-rc.2"), on, false)?.TagName, Is.EqualTo("v1.2.3"),
            "a tester on rc.2 moves on to the final");
        Assert.That(UpdatePolicy.Choose(final, list, BuildVersion.Parse("1.2.2"), on, false)?.TagName, Is.EqualTo("v1.2.3"));
        Assert.That(UpdatePolicy.Choose(null, list, BuildVersion.Parse("1.2.3-rc.2"), on, true), Is.Null, "nothing newer than rc.2");
        Assert.That(UpdatePolicy.Choose(null, list, BuildVersion.Parse("1.2.3-rc.1"), on, true)?.TagName, Is.EqualTo("v1.2.3-rc.2"));
        Assert.That(UpdatePolicy.Choose(null, list, BuildVersion.Parse("1.2.3"), on, true), Is.Null, "the final is past its candidates");
        Assert.That(UpdatePolicy.Choose(null, list, BuildVersion.Parse("1.2.3-rc.1"), new UpdateSettings { OfferTestBuilds = true, SkippedVersion = "1.2.3-rc.2" }, false),
            Is.Null, "a skipped test build isn't offered again unless asked");
    }

    [Test]
    public void AMislabelledCandidateIsOfferedByNeitherChannel()
    {
        // An rc tag published as a full release, or a final tag published as a pre-release.
        ReleaseInfo rcAsFinal = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3-rc.1", [1]))!;
        ReleaseInfo finalAsPre = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3", [1], prerelease: true))!;
        var on = new UpdateSettings { OfferTestBuilds = true };
        BuildVersion installed = BuildVersion.Parse("1.2.2")!;
        Assert.That(UpdatePolicy.Choose(rcAsFinal, [rcAsFinal, finalAsPre], installed, on, true), Is.Null);
        Assert.That(() => ReleaseFeed.DownloadPackage(new FakeReleaseWeb(), rcAsFinal, _game, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("pre-release"));
    }

    [Test]
    public void AListThatIsntReleasesOffersNothing()
    {
        Assert.That(ReleaseInfo.ParseList("{\"message\":\"API rate limit exceeded\"}"), Is.Empty);
        Assert.That(ReleaseInfo.ParseList(""), Is.Empty);
        var web = new FakeReleaseWeb { GetFails = null, Json = "{\"message\":\"Not Found\"}", ReleasesJson = "{\"message\":\"API rate limit exceeded\"}" };
        Assert.That(ReleaseFeed.TryGetReleases(web, out string? problem), Is.Null);
        Assert.That(problem, Is.Not.Null);
    }

    [Test]
    public void ATestBuildIsDownloadedCheckedAndStagedAsItsFinalBuild()
    {
        byte[] package = UpdateFixtures.Package("1.2.3");
        ReleaseInfo release = ReleaseInfo.Parse(Rc("v1.2.3-rc.1", package))!;
        Assert.That(release.Package?.Name, Is.EqualTo("OfflineDAoC-LivingServer-1.2.3-rc.1.zip"));
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb { File = package });
        PreparedUpdate prepared = updater.PrepareFromRelease(release, null, CancellationToken.None);
        Assert.That(prepared.Build.Name, Is.EqualTo("1.2.3"), "INSTALL.cmd is handed the package's build");
        Assert.That(prepared.ReleaseBuild.Name, Is.EqualTo("1.2.3-rc.1"));
        Assert.That(prepared.PackageFolder, Is.EqualTo(Path.Combine(_game, "LivingServer-1.2.3")));
        Assert.That(prepared.ZipPath, Is.EqualTo(Path.Combine(_game, "runtime", "updates", "DAoC-LivingServer-1.2.3-rc.1.zip")));
        Assert.That(prepared.GameServerSha256, Is.EqualTo(UpdateFixtures.Sha("a server build"u8.ToArray())));
    }

    [Test]
    public void ATestBuildThatDoesntMatchItsDigestIsRefused()
    {
        byte[] published = UpdateFixtures.Package("1.2.3");
        byte[] changed = UpdateFixtures.Package("1.2.3", files => files["README.txt"] = "changed"u8.ToArray());
        ReleaseInfo release = ReleaseInfo.Parse(Rc("v1.2.3-rc.1", published))!;
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb { File = changed });
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("doesn't match the SHA-256"));
        Assert.That(Directory.Exists(Path.Combine(_game, "LivingServer-1.2.3")), Is.False);
        Assert.That(Directory.GetFiles(Path.Combine(_game, "runtime", "updates")), Has.None.Contains("LivingServer-"));
    }

    [Test]
    public void TheInstalledTestBuildIsKnownOnlyWhileItsServerIsInstalled()
    {
        byte[] package = UpdateFixtures.Package("1.2.3");
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb { File = package });
        PreparedUpdate prepared = updater.PrepareFromRelease(ReleaseInfo.Parse(Rc("v1.2.3-rc.1", package))!, null, CancellationToken.None);
        BuildVersion launcher = BuildVersion.Parse("1.2.3")!;
        string dll = Path.Combine(_game, "runtime", "server", "GameServer.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        updater.RecordTestBuild(prepared);
        Assert.That(updater.InstalledBuild(launcher), Is.SameAs(launcher), "no server installed yet");
        File.WriteAllBytes(dll, "a server build"u8.ToArray());
        Assert.That(updater.InstalledBuild(launcher)?.Name, Is.EqualTo("1.2.3-rc.1"));
        Assert.That(updater.InstalledBuild(BuildVersion.Parse("1.2.2"))?.Name, Is.EqualTo("1.2.2"), "another launcher build");
        File.WriteAllBytes(dll, "the final 1.2.3"u8.ToArray());
        Assert.That(updater.InstalledBuild(launcher)?.Name, Is.EqualTo("1.2.3"), "a later install makes the marker stale");
        updater.RecordTestBuild(new PreparedUpdate(launcher, "z", "p"));
        Assert.That(File.Exists(Path.Combine(updater.UpdatesFolder, LauncherUpdater.TestBuildMarker)), Is.False, "a final clears it");
    }
}

[TestFixture]
public class TestFolderInstallTests
{
    private string _root = "";
    private string _game = "";

    [SetUp]
    public void SetUp()
    {
        _root = UpdateFixtures.TempFolder();
        _game = Path.Combine(_root, "OfflineDAoC");
        Directory.CreateDirectory(_game);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private void Put(string relative, string text = "x")
    {
        string path = Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static readonly string[] Kept =
    [
        "runtime/OfflineDAoC.exe", "runtime/OfflineDAoC.dll", "runtime/server/GameServer.dll", "runtime/server/CoreServer.exe",
        "runtime/server/config/serverconfig.xml", "runtime/server/navmesh/roads.json", "runtime/client-opendaoc/app/game.dll",
        "tools/dotnet/dotnet.exe", "START OFFLINE DAOC.cmd", "new-client/godot/project.godot",
    ];

    private static readonly string[] Left =
    [
        "runtime/data/opendaoc.sqlite3.db", "runtime/data/opendaoc.sqlite3.db-wal", "runtime/data/realm-event-records.sqlite3",
        "runtime/account.txt", "runtime/logs/server-console.log", "runtime/logs/launcher-updates.log", "runtime/server/errorlog.txt",
        "runtime/server/bot-goals.json", "runtime/server/bot-world.json", "runtime/server/deployment-backups/fork-build-1/GameServer.dll",
        "runtime/backups/world.db", "runtime/progress-backups/a.db", "runtime/updates/OfflineDAoC-LivingServer-1.2.2.zip",
        "runtime/updates/update-settings.json", "runtime/server/chat.log", "runtime/server/old.bak", "runtime/server/memory-census.on",
        "runtime/client-opendaoc/app/d3d9.dll", "runtime/client-opendaoc/app/dxvk.conf", "LivingServer-1.2.2/INSTALL.cmd",
        "new-client/save/settings.cfg", "new-client/godot/dev_mode.txt", "runtime/host-for-friends.json",
        "runtime/server/before-1.2.2/GameServer.dll", "runtime/join-friend.json",
    ];

    [Test]
    public void TheCopyPlanLeavesOutThePlayedWorldAccountLogsBackupsAndRunState()
    {
        foreach (string f in Kept.Concat(Left))
            Put(f);
        List<string> plan = TestFolderInstall.CopyPlan(_game);
        Assert.That(plan, Is.EquivalentTo(Kept));
        foreach (string f in Left)
            Assert.That(TestFolderInstall.Include(f), Is.False, f);
        Assert.That(TestFolderInstall.Include(@"runtime\data\opendaoc.sqlite3.db"), Is.False, "either slash");
        Assert.That(TestFolderInstall.Include("RUNTIME/Account.txt"), Is.False, "any case");
    }

    [Test]
    public void TheTestFolderStaysBesideTheInstall()
    {
        Assert.That(TestFolderInstall.DefaultFolderName(_game), Is.EqualTo("OfflineDAoC-test"));
        Assert.That(TestFolderInstall.CheckTarget(_game, "OfflineDAoC-test", out string target), Is.Null);
        Assert.That(target, Is.EqualTo(Path.Combine(_root, "OfflineDAoC-test")));
        Assert.That(TestFolderInstall.CheckTarget(_game, Path.Combine(_root, "Other"), out _), Is.Null, "a full path in the same parent");
        Assert.That(TestFolderInstall.CheckTarget(_game, "OfflineDAoC", out _), Does.Contain("current install"));
        Assert.That(TestFolderInstall.CheckTarget(_game, "", out _), Is.Not.Null);
        Assert.That(TestFolderInstall.CheckTarget(_game, Path.Combine("..", "escape"), out _), Does.Contain("directly in"));
        Assert.That(TestFolderInstall.CheckTarget(_game, Path.Combine("OfflineDAoC", "inside"), out _), Does.Contain("directly in"));
        Assert.That(TestFolderInstall.CheckTarget(_game, Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid().ToString("N")), out _),
            Does.Contain("directly in"));
        Directory.CreateDirectory(Path.Combine(_root, "Used"));
        File.WriteAllText(Path.Combine(_root, "Used", "a.txt"), "x");
        Assert.That(TestFolderInstall.CheckTarget(_game, "Used", out _), Does.Contain("already has files"));
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Assert.That(TestFolderInstall.CheckTarget(_game, "Empty", out _), Is.Null, "an empty folder may be used");
    }

    [Test]
    public void NothingIsMadeWhileAServerAnswersOnItsPort()
    {
        Assert.That(TestFolderInstall.RunningRefusal(() => true), Does.Contain("port 10300"));
        Assert.That(TestFolderInstall.RunningRefusal(() => false), Is.Null);
        Assert.That(TestFolderInstall.RunningRefusal(() => false, ["game.exe"]), Does.Contain("game.exe"));
        // The real probe against a real listener.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.That(TestFolderInstall.RunningRefusal(() => ServerPortProbe.IsListening(port)), Is.Not.Null);
        }
        finally
        {
            listener.Stop();
        }
        Put("runtime/data/opendaoc.sqlite3.db");
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3-rc.1", [1], prerelease: true))!;
        Assert.That(() => TestFolderInstall.Create(_game, "OfflineDAoC-test", release, new FakeReleaseWeb(), () => true, null, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("port 10300"));
        Assert.That(Directory.Exists(Path.Combine(_root, "OfflineDAoC-test")), Is.False);
    }

    private void MakeWorld(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var con = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False");
        con.Open();
        foreach (string sql in new[]
                 {
                     "CREATE TABLE Account (Name TEXT)", "INSERT INTO Account VALUES ('chris')",
                     "CREATE TABLE DOLCharacters (Name TEXT)", "INSERT INTO DOLCharacters VALUES ('Hero')",
                     "CREATE TABLE Inventory (Item TEXT)", "INSERT INTO Inventory VALUES ('sword')",
                     "CREATE TABLE offline_world_bots (Name TEXT)", "INSERT INTO offline_world_bots VALUES ('Bot')",
                     "CREATE TABLE offline_bot_memory (Line TEXT)", "INSERT INTO offline_bot_memory VALUES ('remembers chris')",
                     "CREATE TABLE Guild (GuildID TEXT, RealmPoints INT, BountyPoints INT, Bank INT, MeritPoints INT, Webpage TEXT, Email TEXT, Motd TEXT, oMotd TEXT, HaveGuildHouse INT, GuildHouseNumber INT)",
                     "INSERT INTO Guild VALUES ('62d4d73a-bb6b-4c5d-9d5e-2664c3fe0284', 99, 1, 5, 1, 'w', 'e', 'm', 'o', 1, 3)",
                     "INSERT INTO Guild VALUES ('a-player-guild', 1, 1, 1, 1, '', '', '', '', 0, 0)",
                     "CREATE TABLE GuildRank (GuildID TEXT)", "INSERT INTO GuildRank VALUES ('a-player-guild')",
                     "CREATE TABLE ServerProperty (`Key` TEXT, Value TEXT, DefaultValue TEXT)",
                     "INSERT INTO ServerProperty VALUES ('xp_rate', '5', '1')",
                     "INSERT INTO ServerProperty VALUES ('allow_auto_account_creation', 'False', 'True')",
                     "CREATE TABLE offline_population_settings (Key TEXT, Value TEXT)",
                     "INSERT INTO offline_population_settings VALUES ('ActiveTarget', '400')",
                     "CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT)",
                     "INSERT INTO offline_local_options VALUES ('MakeMeGM', 'true')",
                     "CREATE TABLE Mob (Name TEXT)", "INSERT INTO Mob VALUES ('a world mob')",
                 })
        {
            using var cmd = new SQLiteCommand(sql, con);
            cmd.ExecuteNonQuery();
        }
    }

    private static object? One(string db, string sql)
    {
        using var con = new SQLiteConnection($"Data Source={db};Version=3;Pooling=False;Read Only=True");
        con.Open();
        using var cmd = new SQLiteCommand(sql, con);
        return cmd.ExecuteScalar();
    }

    [Test]
    public void TheCleanWorldKeepsTheWorldAndLosesEverySave()
    {
        string db = Path.Combine(_game, "w.db");
        MakeWorld(db);
        TestFolderInstall.CleanWorld(db);
        foreach (string table in new[] { "Account", "DOLCharacters", "Inventory", "offline_world_bots", "offline_bot_memory", "GuildRank" })
            Assert.That(One(db, $"SELECT COUNT(*) FROM {table}"), Is.EqualTo(0L), table);
        Assert.That(One(db, "SELECT COUNT(*) FROM Guild"), Is.EqualTo(1L), "only the starting guild");
        Assert.That(One(db, "SELECT RealmPoints FROM Guild"), Is.EqualTo(0L), "its earned state cleared");
        Assert.That(One(db, "SELECT COUNT(*) FROM Mob"), Is.EqualTo(1L), "world data stays");
        Assert.That(One(db, "SELECT Value FROM ServerProperty WHERE `Key`='xp_rate'"), Is.EqualTo("1"));
        Assert.That(One(db, "SELECT Value FROM ServerProperty WHERE `Key`='allow_auto_account_creation'"), Is.EqualTo("True"));
        Assert.That(One(db, "SELECT Value FROM offline_population_settings WHERE Key='ActiveTarget'"), Is.EqualTo("0"));
        Assert.That(One(db, "SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'"), Is.EqualTo("false"));
    }

    [Test]
    public void ANewTestFolderIsAFreshInstallBesideTheCurrentOne()
    {
        foreach (string f in Kept.Concat(Left).Where(f => !f.EndsWith(".db") && !f.EndsWith(".db-wal")))
            Put(f);
        string world = Path.Combine(_game, "runtime", "data", "opendaoc.sqlite3.db");
        MakeWorld(world);
        string before = PackageDigest.FileSha256(world);
        byte[] package = UpdateFixtures.Package("1.2.3");
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3-rc.1", package, prerelease: true))!;
        var (updater, prepared) = TestFolderInstall.Create(_game, "OfflineDAoC-test", release, new FakeReleaseWeb { File = package },
            () => false, null, null, CancellationToken.None);
        string test = Path.Combine(_root, "OfflineDAoC-test");
        Assert.That(updater.GameFolder, Is.EqualTo(test));
        foreach (string f in Kept)
            Assert.That(File.Exists(Path.Combine(test, f)), Is.True, f);
        // The test folder's own update log is new (it logs the test build's install); the rest is never there.
        foreach (string f in Left.Where(f => f != "runtime/data/opendaoc.sqlite3.db" && f != "runtime/logs/launcher-updates.log" && !f.StartsWith("runtime/updates/")))
            Assert.That(File.Exists(Path.Combine(test, f)), Is.False, f);
        Assert.That(File.ReadAllText(Path.Combine(test, "runtime", "logs", "launcher-updates.log")), Does.Not.StartWith("x"), "not the current log");
        Assert.That(File.Exists(Path.Combine(test, "runtime", "updates", "OfflineDAoC-LivingServer-1.2.2.zip")), Is.False);
        string testWorld = Path.Combine(test, "runtime", "data", "opendaoc.sqlite3.db");
        Assert.That(One(testWorld, "SELECT COUNT(*) FROM Account"), Is.EqualTo(0L));
        Assert.That(One(testWorld, "SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(0L));
        Assert.That(One(world, "SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(1L), "the current world is untouched");
        Assert.That(PackageDigest.FileSha256(world), Is.EqualTo(before));
        Assert.That(updater.Settings.OfferTestBuilds, Is.True);
        Assert.That(UpdateSettings.Load(Path.Combine(test, "runtime", "updates")).OfferTestBuilds, Is.True);
        Assert.That(prepared.PackageFolder, Is.EqualTo(Path.Combine(test, "LivingServer-1.2.3")));
        Assert.That(Directory.Exists(Path.Combine(_game, "LivingServer-1.2.3")), Is.False, "nothing staged in the current install");
    }

    [Test]
    public void AFailedTestFolderIsDeleted()
    {
        Put("runtime/OfflineDAoC.exe");
        MakeWorld(Path.Combine(_game, "runtime", "data", "opendaoc.sqlite3.db"));
        byte[] package = UpdateFixtures.Package("1.2.3");
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3-rc.1", package, prerelease: true))!;
        var web = new FakeReleaseWeb { File = UpdateFixtures.Package("1.2.3", f => f["README.txt"] = "changed"u8.ToArray()) };
        Assert.That(() => TestFolderInstall.Create(_game, "OfflineDAoC-test", release, web, () => false, null, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>());
        Assert.That(Directory.Exists(Path.Combine(_root, "OfflineDAoC-test")), Is.False);
    }
}
