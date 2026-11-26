#nullable enable
using NUnit.Framework;
using DAoCLivingServer.Updates;

namespace DAoCLivingServer.Launcher.Tests;

// Issue #684: CI's navmesh set is published as the assets of a navmesh-vN pre-release (zip parts and a manifest, no game
// package). Neither the launcher's update check, its test-build channel nor Setup (which shares ReleaseInfo, BuildVersion
// and ReleaseFeed) may ever offer it: only a tag v<major>.<minor>... that carries the game package is a game release.
// Setup's own run on such an answer is pinned in tools/installer/test-installer.ps1 ("newest-navmesh-release").
[TestFixture]
public class NavmeshReleaseTests
{
    private string _game = "";

    [SetUp]
    public void SetUp() => _game = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_game, true);

    private const string Download = "https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/";

    /// <summary>A release as GitHub's API gives it, with these asset names (each with a digest and a download address).</summary>
    private static string Release(string tag, bool prerelease, params string[] assets) =>
        "{\"tag_name\":\"" + tag + "\",\"name\":\"" + tag + "\",\"prerelease\":" + (prerelease ? "true" : "false") +
        ",\"draft\":false,\"html_url\":\"https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/tag/" + tag +
        "\",\"body\":\"CI only\",\"assets\":[" + string.Join(",", assets.Select(a =>
            "{\"name\":\"" + a + "\",\"size\":1900000000,\"digest\":\"sha256:" + new string('1', 64) +
            "\",\"browser_download_url\":\"" + Download + tag + "/" + a + "\"}")) + "]}";

    private static string Navmesh(int version = 1, bool prerelease = true) =>
        Release("navmesh-v" + version, prerelease, $"navmesh-v{version}.part01.zip", $"navmesh-v{version}.part02.zip", "navmesh-manifest.json");

    [TestCase("navmesh-v1")]
    [TestCase("navmesh-v2")]
    [TestCase("navmesh-v12")]
    [TestCase("v1")]
    [TestCase("1")]
    [TestCase("navmesh-v1.2.3")]
    [TestCase("v1.2.3-navmesh")]
    [TestCase("v1.2.3-navmesh.1")]
    public void ANavmeshTagIsNotABuild(string tag) => Assert.That(BuildVersion.Parse(tag), Is.Null);

    [Test]
    public void TheNavmeshReleaseIsNeitherAFinalNorATestBuildAndHasNoPackage()
    {
        foreach (bool prerelease in new[] { true, false })
        {
            ReleaseInfo release = ReleaseInfo.Parse(Navmesh(prerelease: prerelease))!;
            Assert.That(release, Is.Not.Null);
            Assert.That(release.Version, Is.Null);
            Assert.That(release.IsUsable, Is.False, "the normal channel");
            Assert.That(release.IsTestBuild, Is.False, "the test-build channel");
            Assert.That(release.Package, Is.Null, "no asset is a game package");
        }
    }

    [Test]
    public void TheTestChannelNeverOffersTheNavmeshRelease()
    {
        var on = new UpdateSettings { OfferTestBuilds = true };
        BuildVersion installed = BuildVersion.Parse("1.2.2")!;
        ReleaseInfo final = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.2", [1]))!;
        // GitHub lists releases newest first: the navmesh set would be at the top of the list.
        var list = new[] { ReleaseInfo.Parse(Navmesh(2))!, ReleaseInfo.Parse(Navmesh(1))!, final };
        Assert.That(UpdatePolicy.Choose(final, list, installed, on, asked: true), Is.Null);
        Assert.That(list.Any(r => UpdatePolicy.ShouldOfferTest(r, installed, on, asked: true)), Is.False);
        Assert.That(list.Any(r => UpdatePolicy.ShouldOffer(r, installed, on, asked: true)), Is.False);

        // With a real test build beside it, the test build is offered, never the navmesh set.
        ReleaseInfo rc = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.3-rc.1", [1], prerelease: true))!;
        Assert.That(UpdatePolicy.Choose(final, [ReleaseInfo.Parse(Navmesh())!, rc], installed, on, true)?.TagName, Is.EqualTo("v1.2.3-rc.1"));
    }

    [Test]
    public void TheUpdateCheckOffersNothingWhenTheNavmeshReleaseIsInTheList()
    {
        var web = new FakeReleaseWeb
        {
            Json = UpdateFixtures.ReleaseJson("v1.2.2", [1]),
            ReleasesJson = "[" + Navmesh() + "," + UpdateFixtures.ReleaseJson("v1.2.2", [1]) + "]",
        };
        var updater = new LauncherUpdater(_game, web);
        updater.Settings.OfferTestBuilds = true;
        Assert.That(updater.Check(BuildVersion.Parse("1.2.2"), true, DateTime.UtcNow, out string? problem), Is.Null);
        Assert.That(problem, Is.Null);
        Assert.That(web.ReleasesCalls, Is.EqualTo(1));
        Assert.That(web.Downloads, Is.EqualTo(0));
        Assert.That(File.ReadAllText(updater.Log.Path), Does.Contain("Newest test build: none."));
    }

    [Test]
    public void ANavmeshReleaseMistakenlyMadeTheLatestIsNotOffered()
    {
        // Published by mistake as a full release and "latest": both channels offer nothing, nothing is downloaded.
        foreach (bool testBuilds in new[] { false, true })
        {
            var web = new FakeReleaseWeb { Json = Navmesh(prerelease: false), ReleasesJson = "[" + Navmesh(prerelease: false) + "]" };
            var updater = new LauncherUpdater(_game, web);
            updater.Settings.OfferTestBuilds = testBuilds;
            Assert.That(updater.Check(BuildVersion.Parse("1.2.2"), true, DateTime.UtcNow, out _), Is.Null, $"test builds {testBuilds}");
            Assert.That(web.Downloads, Is.EqualTo(0));
            Assert.That(File.ReadAllText(updater.Log.Path), Does.Contain("Newest release navmesh-v1 (no package)."));
        }
    }

    [Test]
    public void TheNavmeshReleaseIsNeverDownloadedAsAPackage()
    {
        var web = new FakeReleaseWeb();
        foreach (bool prerelease in new[] { true, false })
        {
            ReleaseInfo release = ReleaseInfo.Parse(Navmesh(prerelease: prerelease))!;
            Assert.That(() => ReleaseFeed.DownloadPackage(web, release, _game, null, CancellationToken.None),
                Throws.TypeOf<PackageRefusedException>().With.Message.Contains("isn't a Living Server build"));
            Assert.That(() => new LauncherUpdater(_game, web).PrepareFromRelease(release, null, CancellationToken.None),
                Throws.TypeOf<PackageRefusedException>());
        }
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void AGameTagThatCarriesOnlyNavmeshFilesIsNotOffered()
    {
        // A build tag with the navmesh parts attached but no game package: no package, so neither channel offers it.
        var on = new UpdateSettings { OfferTestBuilds = true };
        ReleaseInfo final = ReleaseInfo.Parse(Release("v1.2.4", false, "navmesh-v1.part01.zip", "navmesh-manifest.json"))!;
        ReleaseInfo rc = ReleaseInfo.Parse(Release("v1.2.4-rc.1", true, "navmesh-v1.part01.zip", "navmesh-manifest.json"))!;
        Assert.That(final.Package, Is.Null);
        Assert.That(rc.Package, Is.Null);
        Assert.That(UpdatePolicy.Choose(final, [rc], BuildVersion.Parse("1.2.2"), on, true), Is.Null);
        Assert.That(() => ReleaseFeed.DownloadPackage(new FakeReleaseWeb(), final, _game, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("has no"));
    }
}
