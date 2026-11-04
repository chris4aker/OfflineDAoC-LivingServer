#nullable enable
using NUnit.Framework;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher.Tests;

// Living Server auto-update (Beta 1.1.2): versions, GitHub's release JSON, the SHA-256 digest check, the
// "skip this version" and once-a-day rules, the package checks and the launcher's preparation of an update. No test
// touches the network: GitHub's answers are the recorded fixtures in Fixtures/ and FakeReleaseWeb.
[TestFixture]
public class UpdateVersionTests
{
    [TestCase("1.1.1-beta", "1.1.2-beta")]
    [TestCase("1.1.2-beta", "1.2-beta")]
    [TestCase("1.1.1-beta", "1.2-beta")]
    [TestCase("1.1-beta", "1.1.1-beta")]
    [TestCase("1.9-beta", "1.10-beta")]
    [TestCase("1.2-beta", "1.2")]
    [TestCase("1.1-beta", "1.1-beta.2")]
    [TestCase("v1.1.2-beta", "v2.0-beta")]
    public void LaterBuildsCompareAsNewer(string older, string newer)
    {
        BuildVersion a = BuildVersion.Parse(older)!, b = BuildVersion.Parse(newer)!;
        Assert.That(b.IsNewerThan(a), Is.True);
        Assert.That(a.IsNewerThan(b), Is.False);
        Assert.That(a.CompareTo(b), Is.LessThan(0));
    }

    [Test]
    public void TheTagsVIsOptionalAndMissingNumbersAreZero()
    {
        Assert.That(BuildVersion.Parse("v1.1.2-beta")!.SameBuildAs(BuildVersion.Parse("1.1.2-beta")), Is.True);
        Assert.That(BuildVersion.Parse("1.2-beta")!.SameBuildAs(BuildVersion.Parse("1.2.0-beta")), Is.True);
        Assert.That(BuildVersion.Parse("v1.1.2-beta")!.Name, Is.EqualTo("1.1.2-beta"));
        Assert.That(BuildVersion.Parse("1.1.2-beta")!.Tag, Is.EqualTo("v1.1.2-beta"));
        Assert.That(BuildVersion.Parse("1.1.2-beta")!.DisplayName, Is.EqualTo("Beta 1.1.2"));
        Assert.That(BuildVersion.Parse("1.1-beta.3")!.DisplayName, Is.EqualTo("Beta 1.1 (build 3)"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("latest")]
    [TestCase("v1.x-beta")]
    [TestCase("1")]
    [TestCase("1.1.1-alpha")]
    [TestCase("0.37-se.2")]
    [TestCase("v1.1.1-beta-hotfix")]
    [TestCase("1.1.1.1.1-beta")]
    [TestCase("v-1.1-beta")]
    public void MalformedTagsAreNotBuilds(string? tag)
    {
        Assert.That(BuildVersion.Parse(tag), Is.Null);
    }

    [Test]
    public void TheLauncherKnowsItsOwnBuildFromTheServersBuildName()
    {
        // The csproj stamps ForkBuildInfo.BuildName; they must never drift apart.
        string source = File.ReadAllText(Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "server", "GameServer", "gameutils", "ForkBuildInfo.cs")));
        string expected = System.Text.RegularExpressions.Regex.Match(source, "BuildName = \"([^\"]+)\"").Groups[1].Value;
        Assert.That(LauncherBuild.Name, Is.EqualTo(expected));
        Assert.That(LauncherBuild.Version, Is.Not.Null);
    }
}

[TestFixture]
public class UpdateReleaseJsonTests
{
    [Test]
    public void TheRecordedLatestReleaseIsRead()
    {
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.Read("release-latest-v1.1-beta.json"))!;
        Assert.That(release.TagName, Is.EqualTo("v1.1-beta"));
        Assert.That(release.Version!.Name, Is.EqualTo("1.1-beta"));
        Assert.That(release.IsUsable, Is.True);
        Assert.That(release.PageUrl, Is.EqualTo("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/tag/v1.1-beta"));
        Assert.That(release.Assets, Has.Count.EqualTo(2));
        ReleaseAsset package = release.Package!;
        Assert.That(package.Name, Is.EqualTo("OfflineDAoC-LivingServer-1.1-beta.zip"), "the zip, not Setup's exe");
        Assert.That(package.Size, Is.EqualTo(30841579));
        Assert.That(package.Digest, Is.EqualTo("sha256:67374b96ec6a33a233447682d21499fed581a1ce03e6a5b81c843eb8bb828b97"));
        Assert.That(package.DownloadUrl, Does.StartWith(ReleaseFeed.DownloadPrefix));
    }

    [Test]
    public void TheNotesPreviewIsPlainText()
    {
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.Read("release-latest-v1.1-beta.json"))!;
        var lines = release.NotesPreview(3);
        Assert.That(lines, Has.Count.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("The realm talks back"));
        Assert.That(lines[1], Does.StartWith("Living Server is a free add-on"));
        Assert.That(string.Join(" ", lines), Does.Not.Contain("**").And.Not.Contain("###"));
    }

    [Test]
    public void APrereleaseIsNeverUsable()
    {
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.Read("release-v1.1.1-beta-prerelease.json"))!;
        Assert.That(release.Prerelease, Is.True);
        Assert.That(release.IsUsable, Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(release, BuildVersion.Parse("1.1-beta"), new UpdateSettings(), asked: true), Is.False);
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{\"message\":\"API rate limit exceeded for 1.2.3.4.\",\"documentation_url\":\"https://docs.github.com/rest\"}")]
    [TestCase("{\"message\":\"Not Found\"}")]
    [TestCase("[1,2,3]")]
    public void AnswersThatArentReleasesAreIgnored(string json)
    {
        Assert.That(ReleaseInfo.Parse(json), Is.Null);
    }

    [Test]
    public void AReleaseWithAMalformedTagIsNotUsable()
    {
        string json = UpdateFixtures.Read("release-latest-v1.1-beta.json").Replace("\"tag_name\": \"v1.1-beta\"", "\"tag_name\": \"nightly\"");
        ReleaseInfo release = ReleaseInfo.Parse(json)!;
        Assert.That(release.Version, Is.Null);
        Assert.That(release.IsUsable, Is.False);
        Assert.That(release.Package, Is.Null);
    }

    [Test]
    public void AMissingDigestReadsAsNull()
    {
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", [1, 2, 3], digest: false))!;
        Assert.That(release.Package!.Name, Is.EqualTo("OfflineDAoC-LivingServer-1.1.2-beta.zip"));
        Assert.That(release.Package.Digest, Is.Null);
    }
}

[TestFixture]
public class UpdateDigestTests
{
    private string _dir = "";
    private string _file = "";
    private readonly byte[] _data = "the published package"u8.ToArray();

    [SetUp]
    public void SetUp()
    {
        _dir = UpdateFixtures.TempFolder();
        _file = Path.Combine(_dir, "package.zip");
        File.WriteAllBytes(_file, _data);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    [Test]
    public void AMatchingDigestPasses()
    {
        string digest = "sha256:" + UpdateFixtures.Sha(_data);
        Assert.That(PackageDigest.Check(_file, digest), Is.EqualTo(DigestCheck.Match));
        Assert.That(PackageDigest.Check(_file, digest.ToUpperInvariant().Replace("SHA256:", "sha256:")), Is.EqualTo(DigestCheck.Match), "hex in either case");
        Assert.That(PackageDigest.VerifyOrDelete(_file, digest), Is.Null);
        Assert.That(File.Exists(_file), Is.True, "a good file is kept");
    }

    [Test]
    public void AMismatchIsRefusedAndTheFileDeleted()
    {
        string digest = "sha256:" + UpdateFixtures.Sha("another file"u8.ToArray());
        Assert.That(PackageDigest.Check(_file, digest), Is.EqualTo(DigestCheck.Mismatch));
        Assert.That(PackageDigest.VerifyOrDelete(_file, digest), Does.Contain("doesn't match"));
        Assert.That(File.Exists(_file), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("sha256:")]
    [TestCase("sha256:1234")]
    [TestCase("sha512:cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e")]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void AMissingOrUnknownDigestIsRefusedAndTheFileDeleted(string? digest)
    {
        Assert.That(PackageDigest.Check(_file, digest), Is.EqualTo(DigestCheck.Missing));
        Assert.That(PackageDigest.VerifyOrDelete(_file, digest), Does.Contain("no SHA-256"));
        Assert.That(File.Exists(_file), Is.False);
    }
}

[TestFixture]
public class UpdatePolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public void AutomaticChecksHappenAtMostOnceADay()
    {
        var settings = new UpdateSettings();
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: false), Is.True, "never checked");
        settings.LastCheckUtc = Now.AddHours(-23);
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: false), Is.False);
        settings.LastCheckUtc = Now.AddHours(-24);
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: false), Is.True);
        settings.LastCheckUtc = Now.AddDays(2);
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: false), Is.True, "the clock went back");
    }

    [Test]
    public void AskingAlwaysChecksEvenWithChecksOff()
    {
        var settings = new UpdateSettings { CheckAutomatically = false, LastCheckUtc = Now.AddMinutes(-1) };
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: false), Is.False);
        Assert.That(UpdatePolicy.ShouldCheck(settings, Now, asked: true), Is.True);
    }

    private static ReleaseInfo Release(string tag) => ReleaseInfo.Parse(UpdateFixtures.ReleaseJson(tag, [1]))!;

    [Test]
    public void ANewerReleaseIsOffered()
    {
        var installed = BuildVersion.Parse("1.1.1-beta");
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.2-beta"), installed, new UpdateSettings(), asked: false), Is.True);
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.1-beta"), installed, new UpdateSettings(), asked: true), Is.False, "the same build");
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1-beta"), installed, new UpdateSettings(), asked: true), Is.False, "an older build");
        Assert.That(UpdatePolicy.ShouldOffer(null, installed, new UpdateSettings(), asked: true), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.2-beta"), null, new UpdateSettings(), asked: true), Is.False, "a launcher that doesn't know its build");
    }

    [Test]
    public void ASkippedVersionIsOfferedAgainOnlyWhenAskedOrWhenANewerOneComes()
    {
        var installed = BuildVersion.Parse("1.1.1-beta");
        var settings = new UpdateSettings { SkippedVersion = "1.1.2-beta" };
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.2-beta"), installed, settings, asked: false), Is.False);
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.2-beta"), installed, settings, asked: true), Is.True);
        Assert.That(UpdatePolicy.ShouldOffer(Release("v1.1.3-beta"), installed, settings, asked: false), Is.True);
    }

    [Test]
    public void AReleaseWithoutAPackageIsNotOffered()
    {
        string json = UpdateFixtures.ReleaseJson("v1.1.2-beta", [1]).Replace("OfflineDAoC-LivingServer-1.1.2-beta.zip", "Something-else.zip");
        Assert.That(UpdatePolicy.ShouldOffer(ReleaseInfo.Parse(json), BuildVersion.Parse("1.1.1-beta"), new UpdateSettings(), asked: true), Is.False);
    }

    [Test]
    public void SettingsAreSavedAndADamagedFileFallsBackToTheDefaults()
    {
        string dir = UpdateFixtures.TempFolder();
        try
        {
            new UpdateSettings { CheckAutomatically = false, SkippedVersion = "1.1.2-beta", LastCheckUtc = Now }.Save(dir);
            UpdateSettings read = UpdateSettings.Load(dir);
            Assert.That(read.CheckAutomatically, Is.False);
            Assert.That(read.SkippedVersion, Is.EqualTo("1.1.2-beta"));
            Assert.That(read.LastCheckUtc, Is.EqualTo(Now));
            File.WriteAllText(Path.Combine(dir, UpdateSettings.FileName), "{ broken");
            Assert.That(UpdateSettings.Load(dir).CheckAutomatically, Is.True);
            Assert.That(UpdateSettings.Load(Path.Combine(dir, "missing")).CheckAutomatically, Is.True);
        }
        finally { Directory.Delete(dir, true); }
    }
}

[TestFixture]
public class UpdateCheckTests
{
    private string _game = "";

    [SetUp]
    public void SetUp() => _game = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_game, true);

    [Test]
    public void TheLauncherAsksOnceADayAndRemembersWhen()
    {
        var web = new FakeReleaseWeb { Json = UpdateFixtures.ReleaseJson("v1.1.2-beta", [1]) };
        var updater = new LauncherUpdater(_game, web);
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var installed = BuildVersion.Parse("1.1.1-beta");
        Assert.That(updater.Check(installed, false, now, out _)?.TagName, Is.EqualTo("v1.1.2-beta"));
        Assert.That(updater.Check(installed, false, now.AddHours(3), out _), Is.Null);
        Assert.That(web.GetCalls, Is.EqualTo(1), "the second automatic check waits a day");
        Assert.That(new LauncherUpdater(_game, web).Settings.LastCheckUtc, Is.EqualTo(now), "remembered in runtime\\updates");
        Assert.That(updater.Check(installed, true, now.AddHours(3), out _)?.TagName, Is.EqualTo("v1.1.2-beta"), "asking checks at once");
        Assert.That(web.GetCalls, Is.EqualTo(2));
        Assert.That(File.ReadAllText(updater.Log.Path), Does.Contain("Newest release v1.1.2-beta"));
    }

    [Test]
    public void AFailedCheckIsQuietAndOffersNothing()
    {
        var web = new FakeReleaseWeb { GetFails = new HttpRequestException("No such host is known.") };
        var updater = new LauncherUpdater(_game, web);
        Assert.That(updater.Check(BuildVersion.Parse("1.1.1-beta"), false, DateTime.UtcNow, out string? problem), Is.Null);
        Assert.That(problem, Is.EqualTo("No such host is known."));
        web.GetFails = null;
        web.Json = "{\"message\":\"API rate limit exceeded\"}";
        Assert.That(updater.Check(BuildVersion.Parse("1.1.1-beta"), true, DateTime.UtcNow, out problem), Is.Null);
        Assert.That(problem, Is.Not.Null);
    }

    [Test]
    public void ChecksOffMeansNoRequest()
    {
        var web = new FakeReleaseWeb { Json = UpdateFixtures.ReleaseJson("v1.1.2-beta", [1]) };
        var updater = new LauncherUpdater(_game, web);
        updater.Settings.CheckAutomatically = false;
        updater.SaveSettings();
        Assert.That(new LauncherUpdater(_game, web).Check(BuildVersion.Parse("1.1.1-beta"), false, DateTime.UtcNow, out _), Is.Null);
        Assert.That(web.GetCalls, Is.EqualTo(0));
    }

    [Test]
    public void SkippingIsRemembered()
    {
        var web = new FakeReleaseWeb { Json = UpdateFixtures.ReleaseJson("v1.1.2-beta", [1]) };
        new LauncherUpdater(_game, web).Skip(BuildVersion.Parse("1.1.2-beta")!);
        Assert.That(new LauncherUpdater(_game, web).Check(BuildVersion.Parse("1.1.1-beta"), false, DateTime.UtcNow, out _), Is.Null);
    }
}

[TestFixture]
public class UpdatePackageTests
{
    private string _dir = "";
    private static readonly BuildVersion Build = BuildVersion.Parse("1.1.2-beta")!;

    [SetUp]
    public void SetUp() => _dir = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private string Write(byte[] zip)
    {
        string path = Path.Combine(_dir, "package.zip");
        File.WriteAllBytes(path, zip);
        return path;
    }

    [Test]
    public void AGoodPackagePassesAndUnpacksIntoItsFolder()
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta"));
        PackageContents contents = PackageCheck.Inspect(zip, Build);
        Assert.That(contents.TopFolder, Is.EqualTo("LivingServer-1.1.2-beta"));
        Assert.That(contents.BuildName, Is.EqualTo("1.1.2-beta"));
        Assert.That(contents.BaseRelease, Is.EqualTo("0.37"));
        string folder = PackageCheck.Extract(zip, contents, Path.Combine(_dir, "out"));
        Assert.That(File.ReadAllText(Path.Combine(folder, "GameServer.dll")), Is.EqualTo("a server build"));
        Assert.That(File.Exists(Path.Combine(folder, "launcher", "OfflineDAoC.dll")), Is.True);
    }

    [Test]
    public void ABuildNameThatIsntTheTagIsRefused()
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta", files => files["BUILD-INFO.txt"] =
            System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(files["BUILD-INFO.txt"]).Replace("BuildName=1.1.2-beta", "BuildName=1.1.3-beta"))));
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("names build 1.1.3-beta"));
    }

    [Test]
    public void TheWrongTopFolderIsRefused()
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta", top: "LivingServer-1.1.1-beta"));
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("LivingServer-1.1.2-beta folder"));
    }

    [TestCase("BUILD-INFO.txt")]
    [TestCase("INSTALL.cmd")]
    [TestCase("ROLLBACK.cmd")]
    [TestCase("install-server-build.ps1")]
    [TestCase("GameServer.dll")]
    public void AMissingFileIsRefused(string missing)
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta", files => files.Remove(missing)));
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>().With.Message.Contains(missing));
    }

    [Test]
    public void AServerThatIsntTheOneBuildInfoNamesIsRefused()
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta", files => files["GameServer.dll"] = "another server"u8.ToArray()));
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("GameServer.dll doesn't match"));
    }

    [Test]
    public void AnEntryOutsideThePackageFolderIsRefused()
    {
        string zip = Write(UpdateFixtures.Package("1.1.2-beta", files => files["../../evil.cmd"] = "x"u8.ToArray()));
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("unsafe"));
    }

    [Test]
    public void NotAZipIsRefused()
    {
        string zip = Write("not a zip"u8.ToArray());
        Assert.That(() => PackageCheck.Inspect(zip, Build), Throws.TypeOf<PackageRefusedException>());
    }
}

[TestFixture]
public class UpdateDownloadTests
{
    private string _game = "";

    [SetUp]
    public void SetUp() => _game = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_game, true);

    private string Updates => Path.Combine(_game, "runtime", "updates");

    [Test]
    public void AVerifiedPackageIsKeptAndMovedIntoTheGameFolder()
    {
        byte[] package = UpdateFixtures.Package("1.1.2-beta");
        var web = new FakeReleaseWeb { File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", package))!;
        long reported = 0;
        PreparedUpdate prepared = updater.PrepareFromRelease(release, (done, _) => reported = done, CancellationToken.None);
        Assert.That(reported, Is.EqualTo(package.Length));
        Assert.That(prepared.ZipPath, Is.EqualTo(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1.2-beta.zip")));
        Assert.That(File.ReadAllText(prepared.ZipPath + ".sha256"), Does.StartWith(UpdateFixtures.Sha(package)));
        Assert.That(prepared.PackageFolder, Is.EqualTo(Path.Combine(_game, "LivingServer-1.1.2-beta")));
        Assert.That(File.Exists(Path.Combine(prepared.PackageFolder, "ROLLBACK.cmd")), Is.True, "ROLLBACK.cmd where a player would put it");
        Assert.That(Directory.Exists(Path.Combine(Updates, "staging")), Is.False);
        Assert.That(File.Exists(prepared.ZipPath + ".part"), Is.False);
        // A second time uses the verified file instead of downloading again.
        updater.PrepareFromRelease(release, null, CancellationToken.None);
        Assert.That(web.Downloads, Is.EqualTo(1));
    }

    [Test]
    public void ADownloadThatDoesntMatchItsDigestIsRefusedAndDeleted()
    {
        byte[] published = UpdateFixtures.Package("1.1.2-beta");
        var web = new FakeReleaseWeb { File = UpdateFixtures.Package("1.1.2-beta", files => files["README.txt"] = "changed"u8.ToArray()) };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", published))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("doesn't match the SHA-256"));
        Assert.That(Directory.GetFiles(Updates), Has.None.Contains("OfflineDAoC-LivingServer"));
        Assert.That(Directory.Exists(Path.Combine(_game, "LivingServer-1.1.2-beta")), Is.False);
        Assert.That(File.ReadAllText(updater.Log.Path), Does.Contain("REFUSED"));
    }

    [Test]
    public void AReleaseWithoutADigestIsRefusedBeforeDownloading()
    {
        var web = new FakeReleaseWeb { File = UpdateFixtures.Package("1.1.2-beta") };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", web.File, digest: false))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("no SHA-256 digest"));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void APackageFromAnotherAddressIsRefused()
    {
        byte[] package = UpdateFixtures.Package("1.1.2-beta");
        var web = new FakeReleaseWeb { File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", package, url: "https://example.com/OfflineDAoC-LivingServer-1.1.2-beta.zip"))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None), Throws.TypeOf<PackageRefusedException>());
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void AMatchingDownloadThatFailsThePackageCheckIsDeleted()
    {
        byte[] package = UpdateFixtures.Package("1.1.2-beta", files => files.Remove("ROLLBACK.cmd"));
        var web = new FakeReleaseWeb { File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.1.2-beta", package))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None), Throws.TypeOf<PackageRefusedException>());
        Assert.That(File.Exists(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1.2-beta.zip")), Is.False);
    }

    [Test]
    public void APackageOnDiskNeedsItsSha256()
    {
        byte[] package = UpdateFixtures.Package("1.1.2-beta");
        string zip = Path.Combine(_game, "OfflineDAoC-LivingServer-1.1.2-beta.zip");
        File.WriteAllBytes(zip, package);
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb());
        Assert.That(() => updater.PrepareFromFile(zip, null, null), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("No SHA-256"));
        Assert.That(() => updater.PrepareFromFile(zip, UpdateFixtures.Sha("x"u8.ToArray()), null), Throws.TypeOf<PackageRefusedException>().With.Message.Contains("doesn't match"));
        Assert.That(File.Exists(zip), Is.True, "a file the player gave is never deleted");
        PreparedUpdate prepared = updater.PrepareFromFile(zip, "sha256:" + UpdateFixtures.Sha(package), null);
        Assert.That(prepared.Build.Name, Is.EqualTo("1.1.2-beta"));
        Assert.That(File.Exists(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1.2-beta.zip.sha256")), Is.True, "kept with its SHA-256 to reinstall");
        // The kept copy is checked again from its .sha256 file.
        Assert.That(updater.PrepareFromFile(prepared.ZipPath, null, null).PackageFolder, Is.EqualTo(Path.Combine(_game, "LivingServer-1.1.2-beta")));
    }

    [Test]
    public void ThePackageBeforeThisUpdateIsKeptAndOlderOnesGo()
    {
        Directory.CreateDirectory(Updates);
        foreach (string old in new[] { "1.1-beta", "1.1.1-beta" })
        {
            File.WriteAllText(Path.Combine(Updates, $"OfflineDAoC-LivingServer-{old}.zip"), old);
            File.WriteAllText(Path.Combine(Updates, $"OfflineDAoC-LivingServer-{old}.zip.sha256"), old);
        }
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb());
        updater.KeepNewestPackages(BuildVersion.Parse("1.1.2-beta")!);
        Assert.That(File.Exists(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1.1-beta.zip")), Is.True);
        Assert.That(File.Exists(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1-beta.zip")), Is.False);
        Assert.That(File.Exists(Path.Combine(Updates, "OfflineDAoC-LivingServer-1.1-beta.zip.sha256")), Is.False);
    }

    [Test]
    public void TheHandoffScriptShipsInsideTheLauncher()
    {
        using Stream? s = typeof(LauncherUpdater).Assembly.GetManifestResourceStream("OfflineDaoc.Updates.apply-update.ps1");
        Assert.That(s, Is.Not.Null);
        string script = new StreamReader(s!).ReadToEnd();
        Assert.That(script, Does.Contain("INSTALL.cmd").And.Contain("$NoPause").And.Contain("$LauncherPid"));
        Assert.That(script, Does.Not.Contain("RunAs").And.Not.Contain("-Verb"), "the install runs without elevation, as a double-click does");
    }

    [Test]
    public void TheLastResultIsShownOnce()
    {
        var updater = new LauncherUpdater(_game, new FakeReleaseWeb());
        Directory.CreateDirectory(Updates);
        File.WriteAllText(Path.Combine(Updates, LauncherUpdater.ResultFile), "Build=1.1.2-beta\r\nResult=installed\r\nDetail=ok\r\n");
        Assert.That(updater.TakeLastResult()!["Result"], Is.EqualTo("installed"));
        Assert.That(updater.TakeLastResult(), Is.Null);
    }

    [Test]
    public void TheGameFolderIsTheParentOfTheServersRuntime()
    {
        Assert.That(LauncherUpdater.GameFolderFor(Path.Combine(_game, "runtime")), Is.EqualTo(_game));
        Assert.That(LauncherUpdater.GameFolderFor(Path.Combine(_game, "runtime") + Path.DirectorySeparatorChar), Is.EqualTo(_game));
    }
}
