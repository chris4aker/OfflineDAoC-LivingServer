#nullable enable
using System;
using System.IO;
using System.Threading;

namespace OfflineDaoc.Updates;

/// <summary>
/// The internet, as far as updates need it: one GET for the releases API and one file download. Kept behind this
/// interface so tests never touch the network (they give a fake with recorded answers).
/// </summary>
public interface IReleaseWeb
{
    /// <summary>The text at url (the API's JSON). Throws on any failure, including the timeout.</summary>
    string GetText(string url, TimeSpan timeout);

    /// <summary>Downloads url into path, reporting (bytes so far, total or -1). Throws on any failure.</summary>
    void Download(string url, string path, Action<long, long>? progress, CancellationToken cancel);
}

// Living Server's releases on GitHub: the newest one, and its package downloaded and verified. Used by the launcher's
// update check and by Setup. Only one request is made for the check, with a generic User-Agent and nothing about the
// player; a failure is reported to the caller, who stays quiet about it unless the player asked.
public static class ReleaseFeed
{
    public const string Repository = "chris4aker/OfflineDAoC-LivingServer";
    /// <summary>The newest final release (GitHub leaves drafts and prereleases out of /latest).</summary>
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    /// <summary>Packages are downloaded only from this repository's own release files.</summary>
    public const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The newest release, or null with the reason (offline, rate limit, not a release). Never throws.</summary>
    public static ReleaseInfo? TryGetLatest(IReleaseWeb web, out string? problem)
    {
        try
        {
            ReleaseInfo? release = ReleaseInfo.Parse(web.GetText(LatestUrl, CheckTimeout));
            problem = release == null ? "GitHub's answer wasn't a release" : null;
            return release;
        }
        catch (Exception e)
        {
            problem = Innermost(e).Message;
            return null;
        }
    }

    /// <summary>
    /// Downloads the release's package into folder and checks it: the SHA-256 digest GitHub publishes for the file,
    /// then the package itself (PackageCheck). A file that fails either is deleted. A package already there that
    /// matches the digest is used again. Throws PackageRefusedException with the reason; download errors pass through.
    /// </summary>
    public static (string Path, PackageContents Contents) DownloadPackage(IReleaseWeb web, ReleaseInfo release, string folder,
        Action<long, long>? progress, CancellationToken cancel)
    {
        BuildVersion build = release.Version ?? throw new PackageRefusedException("The release's tag " + release.TagName + " isn't a Living Server build.");
        ReleaseAsset asset = release.Package ?? throw new PackageRefusedException("The " + build.Tag + " release has no " + ReleaseInfo.PackageName(build) + ".");
        // Fail closed before spending the download: without GitHub's digest the file couldn't be checked.
        if (PackageDigest.Sha256Of(asset.Digest) == null)
            throw new PackageRefusedException("GitHub gave no SHA-256 digest for " + asset.Name + ", so it can't be checked. Nothing was downloaded.");
        string url = asset.DownloadUrl ?? "";
        if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            throw new PackageRefusedException("The package's address isn't one of this project's release files: " + url);

        Directory.CreateDirectory(folder);
        string final = Path.Combine(folder, ReleaseInfo.PackageName(build));
        if (!File.Exists(final) || PackageDigest.Check(final, asset.Digest) != DigestCheck.Match)
        {
            string part = final + ".part";
            foreach (string stale in new[] { part, final + ".sha256" })
            {
                if (File.Exists(stale))
                    File.Delete(stale);
            }
            web.Download(url, part, progress, cancel);
            string? refused = PackageDigest.VerifyOrDelete(part, asset.Digest);
            if (refused != null)
                throw new PackageRefusedException("The download was refused and deleted: " + refused + ".");
            if (File.Exists(final))
                File.Delete(final);
            File.Move(part, final);
        }
        PackageContents contents;
        try
        {
            contents = PackageCheck.Inspect(final, build);
        }
        catch (PackageRefusedException)
        {
            File.Delete(final);
            throw;
        }
        // What it was checked against, so a later reinstall of the kept file can check it again offline.
        File.WriteAllText(final + ".sha256", PackageDigest.Sha256Of(asset.Digest) + "  " + Path.GetFileName(final) + Environment.NewLine);
        return (final, contents);
    }

    public static Exception Innermost(Exception e)
    {
        while (e is AggregateException && e.InnerException != null)
            e = e.InnerException;
        // HttpRequestException wraps the socket or TLS error that says what really happened.
        return e.InnerException != null && e is System.Net.Http.HttpRequestException ? e.InnerException : e;
    }
}
