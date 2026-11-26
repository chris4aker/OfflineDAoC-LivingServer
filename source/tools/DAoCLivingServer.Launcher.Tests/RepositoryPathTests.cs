#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using DAoCLivingServer.Updates;

namespace DAoCLivingServer.Launcher.Tests;

// Issue #689, step 1 of the two-step rule (docs/RELEASE-TRAIN.md, contract item 1): the launcher and Setup (which
// compiles the same ReleaseFeed.cs) take a package from either repository path, chris4aker/OfflineDAoC-LivingServer or
// chris4aker/DAoC-LivingServer, in any case, and from nowhere else. The API URLs stay on the current name: GitHub
// redirects them after a rename, and HttpReleaseWeb follows the redirect.
[TestFixture]
public class RepositoryPathTests
{
    private const string File = "/releases/download/v1.2.3/DAoC-LivingServer-1.2.3.zip";

    [TestCase("chris4aker/OfflineDAoC-LivingServer")]
    [TestCase("chris4aker/DAoC-LivingServer")]
    [TestCase("chris4aker/offlinedaoc-livingserver")]
    [TestCase("chris4aker/daoc-livingserver")]
    [TestCase("CHRIS4AKER/OFFLINEDAOC-LIVINGSERVER")]
    [TestCase("CHRIS4AKER/DAOC-LIVINGSERVER")]
    [TestCase("Chris4Aker/OFFLINEdaoc-livingSERVER")]
    [TestCase("chris4aker/Daoc-Livingserver")]
    public void EitherRepositoryPathInAnyCaseIsAReleaseFile(string repository)
    {
        Assert.That(ReleaseFeed.IsReleaseFileUrl("https://github.com/" + repository + File), Is.True);
        Assert.That(ReleaseFeed.IsReleaseFileUrl("https://github.com/" + repository + "/releases/download/v1.2.3-rc.1/OfflineDAoC-LivingServer-1.2.3-rc.1.zip"), Is.True);
    }

    [Test]
    public void TheAcceptedPathsAreExactlyTheTwo()
    {
        Assert.That(ReleaseFeed.AcceptedRepositories, Is.EqualTo(new[] { "chris4aker/OfflineDAoC-LivingServer", "chris4aker/DAoC-LivingServer" }));
        // Step 1 only: the launcher still asks the current name (GitHub redirects it after a rename).
        Assert.That(ReleaseFeed.Repository, Is.EqualTo("chris4aker/OfflineDAoC-LivingServer"));
        Assert.That(ReleaseFeed.LatestUrl, Is.EqualTo("https://api.github.com/repos/chris4aker/OfflineDAoC-LivingServer/releases/latest"));
        Assert.That(ReleaseFeed.ReleasesUrl, Is.EqualTo("https://api.github.com/repos/chris4aker/OfflineDAoC-LivingServer/releases?per_page=30"));
        Assert.That(ReleaseFeed.IsReleaseFileUrl(ReleaseFeed.DownloadPrefix + "v1.2.3/DAoC-LivingServer-1.2.3.zip"), Is.True);
    }

    // Another owner or repository, lookalikes of the two names, another host, scheme or port, a user part, a path that
    // leaves the release files, escapes and non-ASCII lookalike letters, and addresses that name no file.
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer-evil" + File)]
    [TestCase("https://github.com/chris4aker/DAoC-LivingServer-evil" + File)]
    [TestCase("https://github.com/chris4aker/evil-OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServe" + File)]
    [TestCase("https://github.com/chris4aker/LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-base-files" + File)]
    [TestCase("https://github.com/someone-else/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/someone-else/DAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker-evil/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/xchris4aker/DAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker" + File)]
    [TestCase("https://objects.githubusercontent.com/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com.evil.example/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://evil.example/https://github.com/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://example.com/DAoC-LivingServer-1.2.3.zip")]
    [TestCase("http://github.com/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com:8443/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com@evil.example/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://GitHub.com/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/../../../evil/x/releases/download/v1.2.3/a.zip")]
    [TestCase("https://github.com/chris4aker/DAoC-LivingServer/releases/download/v1.2.3/../../../../evil/r/releases/download/v1.2.3/a.zip")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/./v1.2.3/a.zip")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/%2e%2e/%2e%2e/%2e%2e/evil/a.zip")]
    [TestCase("https://github.com/chris4aker%2FOfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download\\..\\..\\evil\\a.zip")]
    [TestCase("https://github.com/chriſ4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingKerver" + File)]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/v1.2.3/a zip.zip")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/Download/v1.2.3/a.zip")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/tag/v1.2.3")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download")]
    [TestCase("https://github.com/chris4aker/OfflineDAoC-LivingServer")]
    [TestCase("https://github.com/")]
    [TestCase(" https://github.com/chris4aker/OfflineDAoC-LivingServer" + File)]
    [TestCase("")]
    public void AnythingElseIsNotAReleaseFile(string url)
    {
        Assert.That(ReleaseFeed.IsReleaseFileUrl(url), Is.False, url);
    }

    [Test]
    public void NoAddressIsNotAReleaseFile() => Assert.That(ReleaseFeed.IsReleaseFileUrl(null), Is.False);

    private string _game = "";

    [SetUp]
    public void SetUp() => _game = UpdateFixtures.TempFolder();

    [TearDown]
    public void TearDown() => Directory.Delete(_game, true);

    private static string Url(string repository, string tag) =>
        "https://github.com/" + repository + "/releases/download/" + tag + "/OfflineDAoC-LivingServer-" + tag.TrimStart('v') + ".zip";

    [TestCase("chris4aker/DAoC-LivingServer")]
    [TestCase("chris4aker/daoc-livingserver")]
    [TestCase("chris4aker/offlinedaoc-livingserver")]
    [TestCase("CHRIS4AKER/OFFLINEDAOC-LIVINGSERVER")]
    public void A123LauncherTakesTheNextReleaseUnderEitherPath(string repository)
    {
        // After a rename GitHub answers the old API URL (a redirect) with asset URLs under the new name, as in #645.
        byte[] package = UpdateFixtures.Package("1.2.4");
        var web = new FakeReleaseWeb { Json = UpdateFixtures.ReleaseJson("v1.2.4", package, url: Url(repository, "v1.2.4")), File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo? offered = updater.Check(BuildVersion.Parse("1.2.3"), false, DateTime.UtcNow, out string? problem);
        Assert.That(problem, Is.Null);
        Assert.That(offered?.Package?.DownloadUrl, Is.EqualTo(Url(repository, "v1.2.4")));
        PreparedUpdate prepared = updater.PrepareFromRelease(offered!, null, CancellationToken.None);
        Assert.That(web.Downloads, Is.EqualTo(1));
        Assert.That(prepared.Build.Name, Is.EqualTo("1.2.4"));
        Assert.That(System.IO.File.ReadAllText(Path.Combine(prepared.PackageFolder, "BUILD-INFO.txt")), Does.Contain("BuildName=1.2.4\r\n"));
    }

    [TestCase("chris4aker/OfflineDAoC-LivingServer-evil")]
    [TestCase("chris4aker/DAoC-LivingServer-evil")]
    [TestCase("someone-else/OfflineDAoC-LivingServer")]
    [TestCase("someone-else/DAoC-LivingServer")]
    public void APackageUnderAnotherRepositoryIsRefusedBeforeDownloading(string repository)
    {
        byte[] package = UpdateFixtures.Package("1.2.4");
        var web = new FakeReleaseWeb { File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.4", package, url: Url(repository, "v1.2.4")))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("isn't one of this project's release files"));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [TestCase("https://objects.githubusercontent.com/chris4aker/DAoC-LivingServer/releases/download/v1.2.4/OfflineDAoC-LivingServer-1.2.4.zip")]
    [TestCase("https://github.com.evil.example/chris4aker/DAoC-LivingServer/releases/download/v1.2.4/OfflineDAoC-LivingServer-1.2.4.zip")]
    [TestCase("http://github.com/chris4aker/DAoC-LivingServer/releases/download/v1.2.4/OfflineDAoC-LivingServer-1.2.4.zip")]
    public void APackageOnAnotherHostIsRefusedBeforeDownloading(string url)
    {
        byte[] package = UpdateFixtures.Package("1.2.4");
        var web = new FakeReleaseWeb { File = package };
        var updater = new LauncherUpdater(_game, web);
        ReleaseInfo release = ReleaseInfo.Parse(UpdateFixtures.ReleaseJson("v1.2.4", package, url: url))!;
        Assert.That(() => updater.PrepareFromRelease(release, null, CancellationToken.None),
            Throws.TypeOf<PackageRefusedException>().With.Message.Contains("isn't one of this project's release files"));
        Assert.That(web.Downloads, Is.EqualTo(0));
    }

    [Test]
    public void SetupCompilesTheSameCheck()
    {
        // Setup (tools/installer) links the launcher's ReleaseFeed.cs, so these tests are Setup's too.
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        string csproj = System.IO.File.ReadAllText(Path.Combine(root, "tools", "installer", "src", "LivingServerSetup.csproj"));
        Assert.That(csproj, Does.Contain(@"<Compile Include=""..\..\..\source\tools\DAoCLivingServer.Launcher\Updates\ReleaseFeed.cs"""));
        string update = System.IO.File.ReadAllText(Path.Combine(root, "tools", "installer", "src", "PackageUpdate.cs"));
        Assert.That(update, Does.Contain("ReleaseFeed.DownloadPackage(web, release, folder"));
    }

    // GitHub answers a renamed repository's API URL with 301 Moved Permanently (to api.github.com/repositories/<id>/...),
    // and a release file with 301/302 on to its storage. HttpReleaseWeb (the launcher's and Setup's HttpClient) must
    // follow both, keeping the Accept and User-Agent headers. A loopback server stands in for GitHub.
    [Test]
    public void HttpReleaseWebFollowsGitHubsRedirects()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string root = "http://127.0.0.1:" + port;
        var seen = new List<string>();
        var server = Task.Run(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                using TcpClient client = listener.AcceptTcpClient();
                using NetworkStream stream = client.GetStream();
                string request = ReadHead(stream);
                string first = request.Split("\r\n")[0];
                lock (seen)
                    seen.Add(request);
                string answer = first switch
                {
                    "GET /repos/chris4aker/OfflineDAoC-LivingServer/releases/latest HTTP/1.1" =>
                        "HTTP/1.1 301 Moved Permanently\r\nLocation: " + root + "/repositories/1414596251/releases/latest\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                    "GET /repositories/1414596251/releases/latest HTTP/1.1" =>
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 21\r\nConnection: close\r\n\r\n{\"tag_name\":\"v1.2.4\"}",
                    "GET /chris4aker/DAoC-LivingServer/releases/download/v1.2.4/a.zip HTTP/1.1" =>
                        "HTTP/1.1 302 Found\r\nLocation: " + root + "/storage/a.zip\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                    "GET /storage/a.zip HTTP/1.1" =>
                        "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 7\r\nConnection: close\r\n\r\npackage",
                    _ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                };
                byte[] bytes = Encoding.ASCII.GetBytes(answer);
                stream.Write(bytes, 0, bytes.Length);
            }
        });
        try
        {
            using var web = new HttpReleaseWeb("DAoC-LivingServer-tests");
            string text = web.GetText(root + "/repos/chris4aker/OfflineDAoC-LivingServer/releases/latest", TimeSpan.FromSeconds(10));
            Assert.That(text, Is.EqualTo("{\"tag_name\":\"v1.2.4\"}"));
            string path = Path.Combine(_game, "a.zip");
            web.Download(root + "/chris4aker/DAoC-LivingServer/releases/download/v1.2.4/a.zip", path, null, CancellationToken.None);
            Assert.That(System.IO.File.ReadAllText(path), Is.EqualTo("package"));
            Assert.That(server.Wait(TimeSpan.FromSeconds(10)), Is.True);
            lock (seen)
            {
                Assert.That(seen, Has.Count.EqualTo(4));
                // The redirected API request still asks for GitHub's JSON, with the same User-Agent.
                Assert.That(seen[1], Does.StartWith("GET /repositories/1414596251/releases/latest "));
                Assert.That(seen[1], Does.Contain("application/vnd.github+json").IgnoreCase);
                Assert.That(seen[1], Does.Contain("DAoC-LivingServer-tests"));
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ReadHead(NetworkStream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && stream.Read(one, 0, 1) == 1)
            head.Append((char)one[0]);
        return head.ToString();
    }
}
