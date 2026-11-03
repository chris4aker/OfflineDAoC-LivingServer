#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace OfflineDaoc.Updates;

// IReleaseWeb over HttpClient (part of .NET 10 and of .NET Framework 4.7.2, so the launcher and Setup share it).
// Sends a generic User-Agent, which GitHub's API requires, and nothing else about the PC or the player.
public sealed class HttpReleaseWeb : IReleaseWeb, IDisposable
{
    /// <summary>A download that receives nothing for this long is given up.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;

    public HttpReleaseWeb(string userAgent)
    {
        _client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public string GetText(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        // The default completion option reads the whole answer before returning, all inside the timeout.
        using HttpResponseMessage response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase);
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(StallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + url);
        long total = response.Content.Headers.ContentLength ?? -1;
        using Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        // A stalled read on .NET Framework may not notice the token: closing the answer makes it fail.
        using CancellationTokenRegistration closeOnStall = stall.Token.Register(() => response.Dispose());
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var buffer = new byte[1 << 16];
        long done = 0;
        DateTime lastReport = DateTime.MinValue;
        while (true)
        {
            int read;
            try
            {
                read = body.ReadAsync(buffer, 0, buffer.Length, stall.Token).GetAwaiter().GetResult();
            }
            catch (Exception) when (stall.IsCancellationRequested)
            {
                cancel.ThrowIfCancellationRequested();
                throw new TimeoutException("The download stopped receiving data for " + (int)StallTimeout.TotalSeconds + " seconds.");
            }
            if (read == 0)
                break;
            file.Write(buffer, 0, read);
            done += read;
            stall.CancelAfter(StallTimeout);
            if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= 200)
            {
                lastReport = DateTime.UtcNow;
                progress(done, total);
            }
        }
        if (total >= 0 && done != total)
            throw new IOException("The download ended after " + done + " of " + total + " bytes.");
        progress?.Invoke(done, total);
    }

    public void Dispose() => _client.Dispose();
}
