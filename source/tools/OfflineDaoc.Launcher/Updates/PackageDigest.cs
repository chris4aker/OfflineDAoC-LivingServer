#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

public enum DigestCheck
{
    /// <summary>The file's SHA-256 is the one GitHub gave.</summary>
    Match,
    /// <summary>The file differs from what was published (a damaged or changed download).</summary>
    Mismatch,
    /// <summary>GitHub gave no SHA-256 for the file (or something that isn't one), so it can't be checked.</summary>
    Missing,
}

// Checks a downloaded package against the SHA-256 digest GitHub's releases API reports for the asset
// ("digest": "sha256:..."). An update is used only on Match: a missing digest is refused the same as a wrong one.
public static class PackageDigest
{
    private static readonly Regex Shape = new(@"^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

    /// <summary>The 64 lower-case hex digits of a "sha256:..." digest, or null when it isn't one.</summary>
    public static string? Sha256Of(string? digest)
    {
        if (digest == null)
            return null;
        Match m = Shape.Match(digest.Trim());
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static string FileSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    public static DigestCheck Check(string path, string? digest)
    {
        string? expected = Sha256Of(digest);
        if (expected == null)
            return DigestCheck.Missing;
        return string.Equals(FileSha256(path), expected, StringComparison.Ordinal) ? DigestCheck.Match : DigestCheck.Mismatch;
    }

    /// <summary>
    /// Checks the file and deletes it unless it matches, so a damaged or unverifiable download never stays behind
    /// looking like a good one. Returns why it was refused, or null when it matches.
    /// </summary>
    public static string? VerifyOrDelete(string path, string? digest)
    {
        DigestCheck result;
        try
        {
            result = Check(path, digest);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            TryDelete(path);
            return "the download couldn't be read (" + e.Message + ")";
        }
        if (result == DigestCheck.Match)
            return null;
        TryDelete(path);
        return result == DigestCheck.Missing
            ? "GitHub gave no SHA-256 digest for the package, so it can't be checked"
            : "the download doesn't match the SHA-256 digest GitHub published for it";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Left behind; the next download replaces it, and it is never used unverified.
        }
    }
}
