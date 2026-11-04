#nullable enable
using NUnit.Framework;
using System.IO.Compression;
using System.Security.Cryptography;
using OfflineDaoc.Updates;

namespace OfflineDaoc.Launcher.Tests;

// Shared by the update tests: a fake internet (recorded answers, no network), throwaway folders and small packages
// laid out like the real release zips (LivingServer-<build>/BUILD-INFO.txt, GameServer.dll, the scripts).
internal sealed class FakeReleaseWeb : IReleaseWeb
{
    public string? Json;
    public Exception? GetFails;
    public byte[] File = [];
    public int GetCalls;
    public int Downloads;

    public string GetText(string url, TimeSpan timeout)
    {
        GetCalls++;
        Assert.That(url, Is.EqualTo(ReleaseFeed.LatestUrl));
        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        if (GetFails != null)
            throw GetFails;
        return Json ?? "";
    }

    public void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel)
    {
        Downloads++;
        System.IO.File.WriteAllBytes(path, File);
        progress?.Invoke(File.Length, File.Length);
    }
}

internal static class UpdateFixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

    public static string TempFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ls-update-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>A package zip for build, laid out like a release; change lets a test break one thing in it.</summary>
    public static byte[] Package(string build, Action<Dictionary<string, byte[]>>? change = null, string? top = null)
    {
        byte[] dll = "a server build"u8.ToArray();
        var files = new Dictionary<string, byte[]>
        {
            ["GameServer.dll"] = dll,
            ["GameServer.pdb"] = "symbols"u8.ToArray(),
            ["INSTALL.cmd"] = "@echo off\r\n"u8.ToArray(),
            ["ROLLBACK.cmd"] = "@echo off\r\n"u8.ToArray(),
            ["install-server-build.ps1"] = "# install"u8.ToArray(),
            ["rollback-server-build.ps1"] = "# rollback"u8.ToArray(),
            ["fork-build-common.ps1"] = "# common"u8.ToArray(),
            ["launcher/OfflineDAoC.dll"] = "launcher"u8.ToArray(),
        };
        files["BUILD-INFO.txt"] = System.Text.Encoding.UTF8.GetBytes(
            $"BuildName={build}\r\nBaseRelease=0.37\r\nCommit=abc\r\nGameServer.dll={Sha(dll)}\r\nGameServer.pdb={Sha(files["GameServer.pdb"])}\r\n");
        change?.Invoke(files);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            string root = top ?? "LivingServer-" + build;
            zip.CreateEntry(root + "/");
            foreach (var (name, data) in files)
            {
                using Stream s = zip.CreateEntry(name.StartsWith("..") ? name : root + "/" + name).Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    /// <summary>The recorded v1.1-beta answer, with its package's digest and address replaced for a test package.</summary>
    public static string ReleaseJson(string tag, byte[]? package, bool digest = true, string? url = null, bool prerelease = false)
    {
        string build = tag.TrimStart('v');
        string name = "OfflineDAoC-LivingServer-" + build + ".zip";
        string json = Read("release-latest-v1.1-beta.json")
            .Replace("\"tag_name\": \"v1.1-beta\"", $"\"tag_name\": \"{tag}\"")
            .Replace("\"prerelease\": false", $"\"prerelease\": {(prerelease ? "true" : "false")}")
            .Replace("OfflineDAoC-LivingServer-1.1-beta.zip", name)
            .Replace("releases/download/v1.1-beta/", $"releases/download/{tag}/");
        if (url != null)
            json = json.Replace($"https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/{tag}/{name}", url);
        string recorded = "sha256:67374b96ec6a33a233447682d21499fed581a1ce03e6a5b81c843eb8bb828b97";
        return json.Replace($"\"digest\": \"{recorded}\"", digest && package != null ? $"\"digest\": \"sha256:{Sha(package)}\"" : "\"digest\": null");
    }
}
