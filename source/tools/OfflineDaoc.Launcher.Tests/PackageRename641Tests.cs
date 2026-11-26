#nullable enable
using NUnit.Framework;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher.Tests;

// Issue #641: from 1.2.3 the package is DAoC-LivingServer-<build>.zip. Releases up to 1.2.2 carry only
// OfflineDAoC-LivingServer-<build>.zip, and from 1.2.3 a release carries both (1.2.0-1.2.2 launchers look for the old
// name). The launcher and Setup take the new name when it's there and the old one otherwise.
[TestFixture]
public class PackageRename641Tests
{
    private static ReleaseInfo Release(string tag, params string[] assetNames)
    {
        var release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson(tag, UpdateFixtures.Package(tag.TrimStart('v'))))!;
        ReleaseAsset template = release.Assets!.First(a => a.Name!.EndsWith(".zip"));
        release.Assets = assetNames.Select(name => new ReleaseAsset
        {
            Name = name, Size = template.Size, Digest = name.EndsWith(".zip") ? template.Digest : null,
            DownloadUrl = template.DownloadUrl!.Replace(template.Name!, name),
        }).ToList();
        return release;
    }

    [Test]
    public void AReleaseWithBothNamesGivesTheNewOne()
    {
        ReleaseInfo release = Release("v1.2.3", "OfflineDAoC-LivingServer-1.2.3.zip", "DAoC-LivingServer-1.2.3.zip", "DAoC-LivingServer-Setup-1.2.3.exe");
        Assert.That(release.Package!.Name, Is.EqualTo("DAoC-LivingServer-1.2.3.zip"));
    }

    [Test]
    public void AReleaseWithOnlyTheNewNameIsFound()
    {
        Assert.That(Release("v1.2.4", "DAoC-LivingServer-1.2.4.zip").Package!.Name, Is.EqualTo("DAoC-LivingServer-1.2.4.zip"));
    }

    [Test]
    public void AnOlderReleaseWithOnlyTheOldNameIsStillFound()
    {
        Assert.That(Release("v1.2.2", "OfflineDAoC-LivingServer-1.2.2.zip", "LivingServer-Setup-1.2.2.exe").Package!.Name,
            Is.EqualTo("OfflineDAoC-LivingServer-1.2.2.zip"));
        Assert.That(Release("v1.2.2", "LivingServer-Setup-1.2.2.exe").Package, Is.Null, "Setup's exe is never the package");
    }

    [TestCase("DAoC-LivingServer-1.2.3.zip", "1.2.3")]
    [TestCase("OfflineDAoC-LivingServer-1.2.2.zip", "1.2.2")]
    [TestCase("daoc-livingserver-1.2.3-rc.1.zip", "1.2.3-rc.1")]
    [TestCase("DAoC-LivingServer-Setup-1.2.3.exe", null)]
    [TestCase("Something-1.2.3.zip", null)]
    public void APackageFileNameGivesItsBuild(string file, string? build)
    {
        Assert.That(ReleaseInfo.BuildFromPackageFileName(file), Is.EqualTo(build));
    }

    [Test]
    public void KeptPackagesOfBothNamesAreTidiedTogether()
    {
        string game = Path.Combine(Path.GetTempPath(), "rename641-" + Guid.NewGuid().ToString("N")[..8]);
        string updates = Path.Combine(game, "runtime", "updates");
        Directory.CreateDirectory(updates);
        try
        {
            File.WriteAllText(Path.Combine(updates, "OfflineDAoC-LivingServer-1.2.1.zip"), "a");
            File.WriteAllText(Path.Combine(updates, "OfflineDAoC-LivingServer-1.2.2.zip"), "b");
            File.WriteAllText(Path.Combine(updates, "DAoC-LivingServer-1.2.3.zip"), "c");
            new LauncherUpdater(game, new FakeReleaseWeb()).KeepNewestPackages(BuildVersion.Parse("1.2.3")!);
            Assert.That(File.Exists(Path.Combine(updates, "DAoC-LivingServer-1.2.3.zip")), Is.True, "this build's package");
            Assert.That(File.Exists(Path.Combine(updates, "OfflineDAoC-LivingServer-1.2.2.zip")), Is.True, "the one before it");
            Assert.That(File.Exists(Path.Combine(updates, "OfflineDAoC-LivingServer-1.2.1.zip")), Is.False);
        }
        finally { Directory.Delete(game, true); }
    }
}
