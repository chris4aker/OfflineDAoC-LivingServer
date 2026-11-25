// Living Server's own downloader for the base game files (issue #641, owner 2026-11-25: "download the parts in
// parallel, plus HTTP Range splits within each large part, about 4-8 connections in total; resume a broken part; check
// each part's SHA-256 as today; report the combined speed").
//
// The original downloader script fetched the parts one at a time over one connection each (about 1.7 MB/s on the
// owner's PC, GitHub's per-connection limit). This one:
// - splits every part larger than SplitAbove into ranges and works through all ranges of all parts with at most
//   Connections requests at once;
// - writes each part to "<part>.partial" and its finished ranges to "<part>.partial.state", so a cut connection or a
//   stopped Setup continues where it stopped (a retry continues the range, a new run continues the part);
// - checks each finished part against its SHA-256 and refuses it on a mismatch (the bytes are deleted, nothing is
//   kept under the part's name);
// - keeps a part already in place with the right size and SHA-256.
// A finished part lands under the same name and folder the original script uses (.downloads\v<release>\<part>), so
// that script finds it verified and only joins and unpacks.
//
// Shared by Setup (.NET Framework 4.7.2) and its tests (.NET 10), so it uses HttpWebRequest, which both have.
#pragma warning disable SYSLIB0014 // HttpWebRequest: the one HTTP client .NET Framework 4.7.2 and .NET 10 share here
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LivingServerSetup
{
    /// <summary>One file to download: where from, where to, its size and its SHA-256 (64 hex digits).</summary>
    public sealed class DownloadPart
    {
        public string Url;
        public string Path;
        public long Bytes;
        public string Sha256;
        public string Name => System.IO.Path.GetFileName(Path);
    }

    /// <summary>A part came down whole but its SHA-256 isn't the expected one: it was refused and deleted.</summary>
    public sealed class BadHashException : Exception
    {
        public BadHashException(string part, string got, string want)
            : base(part + " failed its SHA-256 check (got " + got + ", expected " + want + "); it was deleted.") { Part = part; }
        public string Part { get; }
    }

    /// <summary>The server answered a byte-range request with the whole file: that part is fetched in one piece.</summary>
    sealed class NoRangesException : IOException
    {
        public NoRangesException(DownloadPart part) : base("the server doesn't send byte ranges for " + part.Name) { Part = part; }
        public DownloadPart Part { get; }
    }

    public sealed class FastDownload
    {
        /// <summary>Requests at once, over all parts (polite to GitHub: 6 by default, at most 8).</summary>
        public int Connections = 6;
        /// <summary>Parts larger than this are split into ranges.</summary>
        public long SplitAbove = 64L * 1024 * 1024;
        /// <summary>Retries of one range after an error, counted again from zero whenever it makes progress.</summary>
        public int Retries = 6;
        public TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
        public int TimeoutMs = 30000;
        public string UserAgent = "DAoC-LivingServer-Setup";
        public Action<string> Log = _ => { };

        long done, total;
        volatile Exception failure;

        /// <summary>Bytes of every part in place so far (resumed bytes and kept parts included).</summary>
        public long BytesDone => Interlocked.Read(ref done);
        /// <summary>Bytes of every part.</summary>
        public long BytesTotal => Interlocked.Read(ref total);
        /// <summary>The most requests that were open at once (for the tests and the log).</summary>
        public int PeakConnections => peak;
        int open, peak;

        sealed class Range
        {
            public long Start, End;   // End exclusive
            public long Done;         // bytes of this range written and flushed
            public long Left => End - Start - Done;
        }

        sealed class PartJob
        {
            public DownloadPart Part;
            public List<Range> Ranges;
            public int Remaining;
            public string Partial => Part.Path + ".partial";
            public string State => Part.Path + ".partial.state";
            public readonly object Gate = new object();
        }

        /// <summary>Downloads every part that isn't already in place. Throws on the first failure that retries can't fix
        /// (a BadHashException for a part that doesn't match its SHA-256); the finished ranges stay for the next run.</summary>
        public void Run(IList<DownloadPart> parts)
        {
            for (int pass = 0; ; pass++)
            {
                try { RunOnce(parts); return; }
                catch (NoRangesException e) when (pass < parts.Count)
                {
                    // Planned again as one range from its first byte (what it had is thrown away: it can't be trusted
                    // to line up with a whole-file answer).
                    Log(e.Message + "; downloading it in one piece");
                    whole.Add(e.Part.Path);
                    TryDelete(e.Part.Path + ".partial"); TryDelete(e.Part.Path + ".partial.state");
                }
            }
        }

        readonly HashSet<string> whole = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void RunOnce(IList<DownloadPart> parts)
        {
            if (Connections < 1) Connections = 1;
            if (Connections > 8) Connections = 8;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            if (ServicePointManager.DefaultConnectionLimit < Connections + 2) ServicePointManager.DefaultConnectionLimit = Connections + 2;
            failure = null;
            done = 0; total = parts.Sum(p => p.Bytes); open = peak = 0;

            var jobs = new List<PartJob>();
            foreach (var part in parts)
            {
                if (IsInPlace(part)) { Interlocked.Add(ref done, part.Bytes); Log(part.Name + ": already downloaded and checked"); continue; }
                var job = Plan(part);
                Interlocked.Add(ref done, job.Ranges.Sum(r => r.Done));
                jobs.Add(job);
            }
            if (jobs.Count == 0) return;

            var queue = new ConcurrentQueue<(PartJob job, Range range)>();
            foreach (var job in jobs)
            {
                job.Remaining = job.Ranges.Count(r => r.Left > 0);
                foreach (var r in job.Ranges.Where(r => r.Left > 0)) queue.Enqueue((job, r));
                if (job.Remaining == 0) Finish(job);
            }
            Log("Downloading " + jobs.Count + " part(s) over " + Math.Min(Connections, Math.Max(1, queue.Count)) + " connection(s)");

            var workers = Enumerable.Range(0, Math.Min(Connections, Math.Max(1, queue.Count))).Select(_ => new Thread(() =>
            {
                while (failure == null && queue.TryDequeue(out var item))
                {
                    try
                    {
                        Fetch(item.job, item.range);
                        bool last;
                        lock (item.job.Gate) last = --item.job.Remaining == 0;
                        if (last) Finish(item.job);
                    }
                    catch (Exception e) { if (failure == null) failure = e; }
                }
            }) { IsBackground = true }).ToList();
            foreach (var w in workers) w.Start();
            foreach (var w in workers) w.Join();
            if (failure != null) throw failure;
        }

        static bool IsInPlace(DownloadPart part)
        {
            var f = new FileInfo(part.Path);
            return f.Exists && f.Length == part.Bytes && string.Equals(Sha256Of(part.Path), part.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The ranges of a part: those of an unfinished earlier run when its state file fits, else new ones.</summary>
        PartJob Plan(DownloadPart part)
        {
            var job = new PartJob { Part = part };
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(part.Path)));
            job.Ranges = ReadState(job);
            if (job.Ranges != null)
            {
                Log(part.Name + ": continuing, " + job.Ranges.Sum(r => r.Done) + " of " + part.Bytes + " bytes already here");
                return job;
            }
            int count = whole.Contains(part.Path) ? 1 : part.Bytes > SplitAbove ? (int)Math.Min(Connections, (part.Bytes + SplitAbove - 1) / SplitAbove) : 1;
            job.Ranges = Split(part.Bytes, Math.Max(1, count));
            using (var f = new FileStream(job.Partial, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) f.SetLength(part.Bytes);
            WriteState(job);
            return job;
        }

        static List<Range> Split(long bytes, int count)
        {
            var list = new List<Range>();
            long size = bytes / count;
            for (int i = 0; i < count; i++)
                list.Add(new Range { Start = i * size, End = i == count - 1 ? bytes : (i + 1) * size });
            return list;
        }

        List<Range> ReadState(PartJob job)
        {
            try
            {
                if (!File.Exists(job.State) || !File.Exists(job.Partial) || new FileInfo(job.Partial).Length != job.Part.Bytes) return null;
                var lines = File.ReadAllLines(job.State);
                if (lines.Length < 2 || lines[0] != "bytes=" + job.Part.Bytes + " sha256=" + job.Part.Sha256.ToLowerInvariant()) return null;
                var ranges = new List<Range>();
                foreach (var line in lines.Skip(1).Where(l => l.Trim().Length > 0))
                {
                    var f = line.Split(' ').Select(s => long.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    if (f.Length != 3 || f[0] < 0 || f[1] > job.Part.Bytes || f[0] >= f[1] || f[2] < 0 || f[2] > f[1] - f[0]) return null;
                    ranges.Add(new Range { Start = f[0], End = f[1], Done = f[2] });
                }
                // The ranges must cover the part exactly, in order.
                long at = 0;
                foreach (var r in ranges) { if (r.Start != at) return null; at = r.End; }
                return at == job.Part.Bytes ? ranges : null;
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is OverflowException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        void WriteState(PartJob job)
        {
            lock (job.Gate)
            {
                var text = new StringBuilder("bytes=" + job.Part.Bytes + " sha256=" + job.Part.Sha256.ToLowerInvariant() + "\n");
                foreach (var r in job.Ranges)
                    text.Append(r.Start.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(r.End.ToString(CultureInfo.InvariantCulture))
                        .Append(' ').Append(r.Done.ToString(CultureInfo.InvariantCulture)).Append('\n');
                var tmp = job.State + ".tmp";
                File.WriteAllText(tmp, text.ToString(), Encoding.ASCII);
                if (File.Exists(job.State)) File.Delete(job.State);
                File.Move(tmp, job.State);
            }
        }

        /// <summary>One range, with retries; each retry continues from the last flushed byte.</summary>
        void Fetch(PartJob job, Range range)
        {
            int tries = 0;
            while (range.Left > 0 && failure == null)
            {
                long before = range.Done;
                try
                {
                    FetchOnce(job, range);
                }
                catch (Exception e) when ((e is WebException || e is IOException) && !(e is NoRangesException))
                {
                    if (range.Done > before) tries = 0;
                    if (++tries > Retries) throw new IOException(job.Part.Name + ": the download kept failing (" + e.Message + "); run Setup again to continue.", e);
                    Log(job.Part.Name + ": " + e.Message + "; continuing from byte " + (range.Start + range.Done) + " (try " + tries + " of " + Retries + ")");
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        void FetchOnce(PartJob job, Range range)
        {
            long from = range.Start + range.Done;
            var request = (HttpWebRequest)WebRequest.Create(job.Part.Url);
            request.UserAgent = UserAgent;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.None;
            bool ranged = true;
            request.AddRange(from, range.End - 1);
            int now = Interlocked.Increment(ref open);
            int seen;
            while (now > (seen = peak) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (ranged && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        // The server sends the whole file: fine for a part in one range from its first byte, else the
                        // part is planned again as one range.
                        if (job.Ranges.Count == 1 && from == 0) ranged = false;
                        else throw new NoRangesException(job.Part);
                    }
                    if (ranged)
                    {
                        string cr = response.Headers["Content-Range"] ?? "";
                        if (!cr.StartsWith("bytes " + from.ToString(CultureInfo.InvariantCulture) + "-", StringComparison.OrdinalIgnoreCase))
                            throw new IOException("the server sent another byte range (" + cr + ")");
                    }
                    using (var body = response.GetResponseStream())
                    using (var file = new FileStream(job.Partial, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    {
                        file.Position = from;
                        var buffer = new byte[81920];
                        long sinceFlush = 0;
                        void Commit()
                        {
                            if (sinceFlush == 0) return;
                            file.Flush(true);
                            lock (job.Gate) range.Done += sinceFlush;
                            Interlocked.Add(ref done, sinceFlush);
                            sinceFlush = 0;
                            WriteState(job);
                        }
                        try
                        {
                        while (range.Left - sinceFlush > 0 && failure == null)
                        {
                            int want = (int)Math.Min(buffer.Length, range.Left - sinceFlush);
                            int n = body.Read(buffer, 0, want);
                            if (n <= 0) throw new IOException("the connection closed early");
                            file.Write(buffer, 0, n);
                            sinceFlush += n;
                            // Counted as done only once flushed, so a resumed range never trusts bytes that weren't written.
                            if (sinceFlush >= 4 * 1024 * 1024) Commit();
                        }
                        }
                        finally
                        {
                            // A cut connection keeps every byte that arrived before it: the retry asks for the rest.
                            try { Commit(); } catch (IOException) { }
                        }
                    }
                }
            }
            catch (IOException) { FlushAfterError(job); throw; }
            catch (WebException) { FlushAfterError(job); throw; }
            finally { Interlocked.Decrement(ref open); }
        }

        void FlushAfterError(PartJob job) { try { WriteState(job); } catch (IOException) { } }

        /// <summary>Checks a part whose ranges are all in, and puts it under its own name or refuses it.</summary>
        void Finish(PartJob job)
        {
            string got = Sha256Of(job.Partial);
            if (!string.Equals(got, job.Part.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(job.Partial); TryDelete(job.State);
                Interlocked.Add(ref done, -job.Part.Bytes);
                throw new BadHashException(job.Part.Name, got, job.Part.Sha256.ToLowerInvariant());
            }
            TryDelete(job.Part.Path);
            File.Move(job.Partial, job.Part.Path);
            TryDelete(job.State);
            Log(job.Part.Name + ": downloaded and checked (SHA-256 " + got.Substring(0, 12) + "...)");
        }

        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }

        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }
    }
}
