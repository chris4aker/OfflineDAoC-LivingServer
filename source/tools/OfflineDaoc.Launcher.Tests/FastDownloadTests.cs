using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LivingServerSetup;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Setup's base-game downloader (tools/installer/src/FastDownload.cs, issue #641) against a local HTTP server: ranged
// parallel downloads reassemble byte for byte, a cut connection and a stopped run both continue where they stopped,
// a part whose SHA-256 doesn't match is refused, and a server that ignores byte ranges still works.
[TestFixture]
public sealed class FastDownloadTests
{
    sealed class LocalServer : IDisposable
    {
        readonly HttpListener listener = new();
        readonly Dictionary<string, byte[]> files = new();
        public readonly ConcurrentBag<(string name, long from, long to)> Requests = new();
        public string Prefix { get; }
        public bool Ranges = true;
        /// <summary>Each response stops after this many bytes (0: never), then the connection is cut.</summary>
        public long CutAfter;
        /// <summary>Only this many responses are cut (then the server behaves).</summary>
        public int Cuts = int.MaxValue;
        int open, cutsDone;
        public int Peak;
        public long Served;

        public LocalServer()
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
            Prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Prefix);
            listener.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public string Add(string name, byte[] data) { files[name] = data; return Prefix + name; }

        void Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Serve(ctx));
            }
        }

        void Serve(HttpListenerContext ctx)
        {
            int now = Interlocked.Increment(ref open);
            lock (this) Peak = Math.Max(Peak, now);
            try
            {
                string name = ctx.Request.Url!.AbsolutePath.TrimStart('/');
                if (!files.TryGetValue(name, out var data)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
                long from = 0, to = data.Length - 1;
                string? range = ctx.Request.Headers["Range"];
                bool partial = Ranges && range != null && range.StartsWith("bytes=");
                if (partial)
                {
                    var spec = range!["bytes=".Length..].Split('-');
                    from = long.Parse(spec[0]);
                    if (spec[1].Length > 0) to = Math.Min(long.Parse(spec[1]), data.Length - 1);
                    ctx.Response.StatusCode = 206;
                    ctx.Response.AddHeader("Content-Range", $"bytes {from}-{to}/{data.Length}");
                }
                Requests.Add((name, from, to));
                long length = to - from + 1;
                ctx.Response.ContentLength64 = length;
                bool cut = CutAfter > 0 && CutAfter < length && Interlocked.Increment(ref cutsDone) <= Cuts;
                long send = cut ? CutAfter : length;
                var output = ctx.Response.OutputStream;
                for (long at = 0; at < send;)
                {
                    int n = (int)Math.Min(65536, send - at);
                    output.Write(data, (int)(from + at), n);
                    at += n;
                    Interlocked.Add(ref Served, n);
                }
                if (cut) { output.Flush(); ctx.Response.Abort(); return; }
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
            finally { Interlocked.Decrement(ref open); }
        }

        public void Dispose() { try { listener.Stop(); listener.Close(); } catch { } }
    }

    string dir = "";

    [SetUp]
    public void SetUp() { dir = Path.Combine(Path.GetTempPath(), "fastdl-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir); }

    [TearDown]
    public void TearDown() { try { Directory.Delete(dir, true); } catch { } }

    static byte[] Data(int bytes, int seed) { var b = new byte[bytes]; new Random(seed).NextBytes(b); return b; }
    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    DownloadPart Part(LocalServer server, string name, byte[] data, string? sha = null) =>
        new() { Url = server.Add(name, data), Path = Path.Combine(dir, name), Bytes = data.Length, Sha256 = sha ?? Sha(data) };

    static FastDownload Downloader(int connections = 4) =>
        new() { Connections = connections, SplitAbove = 1 << 20, RetryDelay = TimeSpan.FromMilliseconds(50), TimeoutMs = 10000 };

    [Test]
    public void Ranged_parallel_download_reassembles_every_part_byte_for_byte()
    {
        using var server = new LocalServer();
        var big = Data(5 * 1024 * 1024 + 123, 1);
        var mid = Data(3 * 1024 * 1024 + 7, 2);
        var small = Data(300 * 1024, 3);
        var parts = new[] { Part(server, "game.zip.001", big), Part(server, "game.zip.002", mid), Part(server, "game.zip.003", small) };
        var dl = Downloader(4);
        dl.Run(parts);

        Assert.That(File.ReadAllBytes(parts[0].Path), Is.EqualTo(big));
        Assert.That(File.ReadAllBytes(parts[1].Path), Is.EqualTo(mid));
        Assert.That(File.ReadAllBytes(parts[2].Path), Is.EqualTo(small));
        Assert.That(Directory.GetFiles(dir, "*.partial*"), Is.Empty, "no unfinished files are left");
        Assert.That(server.Requests.Count(r => r.name == "game.zip.001"), Is.EqualTo(4), "the large part is split into ranges");
        Assert.That(dl.PeakConnections, Is.InRange(2, 4), "parts and ranges come down at once, never over the cap");
        Assert.That(dl.BytesDone, Is.EqualTo(dl.BytesTotal));
    }

    [Test]
    public void A_cut_connection_continues_from_where_it_stopped()
    {
        using var server = new LocalServer { CutAfter = 256 * 1024, Cuts = 6 };
        var data = Data(4 * 1024 * 1024, 4);
        var part = Part(server, "game.zip.001", data);
        Downloader(4).Run(new[] { part });

        Assert.That(File.ReadAllBytes(part.Path), Is.EqualTo(data));
        // Every retry asked for the bytes after the cut, not the range again from its start.
        var starts = server.Requests.Select(r => r.from).ToList();
        Assert.That(starts.Count, Is.GreaterThan(4));
        Assert.That(starts.Count(s => s % (1024 * 1024) != 0), Is.GreaterThan(0), "retries continue inside a range");
        Assert.That(server.Served, Is.LessThanOrEqualTo(data.Length + 6 * 256 * 1024), "the bytes before each cut are kept, not fetched again");
    }

    [Test]
    public void A_stopped_download_continues_in_the_next_run()
    {
        var data = Data(4 * 1024 * 1024, 5);
        DownloadPart part;
        using (var broken = new LocalServer { CutAfter = 512 * 1024 })
        {
            part = Part(broken, "game.zip.001", data);
            var first = Downloader(4);
            first.Retries = 0;
            Assert.Throws<IOException>(() => first.Run(new[] { part }));
            Assert.That(File.Exists(part.Path + ".partial"), Is.True, "the unfinished part is kept");
            Assert.That(File.Exists(part.Path), Is.False);
        }
        using var server = new LocalServer();
        part.Url = server.Add("game.zip.001", data);
        var second = Downloader(4);
        second.Run(new[] { part });

        Assert.That(File.ReadAllBytes(part.Path), Is.EqualTo(data));
        Assert.That(server.Served, Is.LessThan(data.Length), "the second run only fetched what the first one didn't get");
        Assert.That(File.Exists(part.Path + ".partial.state"), Is.False);
    }

    [Test]
    public void A_part_with_the_wrong_sha256_is_refused_and_deleted()
    {
        using var server = new LocalServer();
        var data = Data(2 * 1024 * 1024, 6);
        var part = Part(server, "game.zip.001", data, sha: new string('0', 64));
        var e = Assert.Throws<BadHashException>(() => Downloader(4).Run(new[] { part }));

        Assert.That(e!.Part, Is.EqualTo("game.zip.001"));
        Assert.That(File.Exists(part.Path), Is.False, "nothing is kept under the part's name");
        Assert.That(Directory.GetFiles(dir), Is.Empty, "the refused bytes are deleted");
    }

    [Test]
    public void A_part_already_in_place_is_kept_and_a_damaged_one_fetched_again()
    {
        using var server = new LocalServer();
        var data = Data(2 * 1024 * 1024, 7);
        var part = Part(server, "game.zip.001", data);
        File.WriteAllBytes(part.Path, data);
        Downloader().Run(new[] { part });
        Assert.That(server.Requests, Is.Empty, "a checked part is not downloaded again");

        var damaged = (byte[])data.Clone();
        damaged[1000] ^= 0xff;
        File.WriteAllBytes(part.Path, damaged);
        Downloader().Run(new[] { part });
        Assert.That(File.ReadAllBytes(part.Path), Is.EqualTo(data));
    }

    [Test]
    public void A_server_that_ignores_byte_ranges_still_gives_the_whole_part()
    {
        using var server = new LocalServer { Ranges = false };
        var data = Data(3 * 1024 * 1024 + 11, 8);
        var part = Part(server, "game.zip.001", data);
        Downloader(4).Run(new[] { part });
        Assert.That(File.ReadAllBytes(part.Path), Is.EqualTo(data));
    }
}
