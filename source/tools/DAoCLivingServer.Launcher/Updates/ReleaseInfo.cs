#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace DAoCLivingServer.Updates;

// What GitHub's releases API says about one release (GET /repos/{owner}/{repo}/releases/latest), reduced to the fields
// an update needs. Read with DataContractJsonSerializer because Setup runs on .NET Framework 4.7.2, where
// System.Text.Json isn't part of the framework; the launcher uses the same reader so one tested parser serves both.
[DataContract]
public sealed class ReleaseInfo
{
    [DataMember(Name = "tag_name")] public string? TagName { get; set; }
    [DataMember(Name = "name")] public string? Title { get; set; }
    [DataMember(Name = "body")] public string? Body { get; set; }
    [DataMember(Name = "html_url")] public string? PageUrl { get; set; }
    [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
    [DataMember(Name = "draft")] public bool Draft { get; set; }
    [DataMember(Name = "assets")] public List<ReleaseAsset>? Assets { get; set; }

    /// <summary>The build the tag names ("v1.1.2-beta" is 1.1.2-beta), or null for a tag that isn't a build.</summary>
    public BuildVersion? Version => BuildVersion.Parse(TagName);

    /// <summary>A published, final release whose tag is a final build: the only kind the normal channel offers.</summary>
    public bool IsUsable => !Draft && !Prerelease && Version is { IsCandidate: false };

    /// <summary>
    /// A published pre-release whose tag is a release candidate (v1.2.3-rc.1): what the test-build channel offers, only
    /// with "Offer test builds" on (issue #637). GitHub never lists one as /releases/latest.
    /// </summary>
    public bool IsTestBuild => !Draft && Prerelease && Version is { IsCandidate: true };

    /// <summary>
    /// The package a release of this build carries: DAoC-LivingServer-&lt;build&gt;.zip, named by the tag's build
    /// (DAoC-LivingServer-1.2.3-rc.1.zip for a candidate, whose package inside is build 1.2.3). Issue #641 renamed it.
    /// </summary>
    public static string PackageName(BuildVersion build) => PackagePrefix + build.Name + ".zip";

    public const string PackagePrefix = "DAoC-LivingServer-";
    /// <summary>
    /// The package's name up to 1.2.2 (OfflineDAoC-LivingServer-&lt;build&gt;.zip). Releases up to 1.2.2 carry only it, and from
    /// 1.2.3 on every release carries a copy under it too, because 1.2.0-1.2.2 launchers look for exactly that name.
    /// </summary>
    public const string LegacyPackagePrefix = "OfflineDAoC-LivingServer-";
    public static string LegacyPackageName(BuildVersion build) => LegacyPackagePrefix + build.Name + ".zip";

    /// <summary>The build a package file's name gives (either name), or null for another file.</summary>
    public static string? BuildFromPackageFileName(string fileName)
    {
        Match m = Regex.Match(fileName, @"^(?:DAoC-LivingServer|OfflineDAoC-LivingServer)-(.+)\.zip$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The release's package asset, or null when it has none.</summary>
    public ReleaseAsset? Package
    {
        get
        {
            BuildVersion? build = Version;
            if (build == null || Assets == null)
                return null;
            string name = PackageName(build), legacy = LegacyPackageName(build);
            return Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) ??
                   Assets.FirstOrDefault(a => string.Equals(a.Name, legacy, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Reads the API's answer. Null when it isn't a release (an error message, a rate limit, not JSON).</summary>
    public static ReleaseInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var serializer = new DataContractJsonSerializer(typeof(ReleaseInfo));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json!.TrimStart('\uFEFF')));
            var release = serializer.ReadObject(stream) as ReleaseInfo;
            return release?.TagName == null ? null : release;
        }
        catch (Exception e) when (e is SerializationException || e is InvalidCastException || e is ArgumentException || e is FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the API's list of releases (GET /repos/{owner}/{repo}/releases). Entries that aren't releases are left out;
    /// an answer that isn't a list gives an empty one.
    /// </summary>
    public static List<ReleaseInfo> ParseList(string? json)
    {
        var result = new List<ReleaseInfo>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            var serializer = new DataContractJsonSerializer(typeof(List<ReleaseInfo>));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json!.TrimStart('\uFEFF')));
            if (serializer.ReadObject(stream) is List<ReleaseInfo> list)
                result.AddRange(list.Where(r => r != null && r.TagName != null));
        }
        catch (Exception e) when (e is SerializationException || e is InvalidCastException || e is ArgumentException || e is FormatException)
        {
            // Not a list of releases (an error message, a rate limit): nothing to offer.
        }
        return result;
    }

    /// <summary>
    /// The first lines of the release notes as plain text, for a one-glance summary: headings, list marks, bold and
    /// links are reduced to their words; empty lines are skipped.
    /// </summary>
    public IReadOnlyList<string> NotesPreview(int lines)
    {
        var result = new List<string>();
        foreach (string raw in (Body ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (result.Count >= lines)
                break;
            string line = raw.Trim();
            line = Regex.Replace(line, @"^(#{1,6}\s+|[-*+]\s+|\d+\.\s+|>\s*)", "");
            line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            line = line.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
            if (line.Length == 0 || Regex.IsMatch(line, @"^[-=_*|: ]+$"))
                continue;
            result.Add(line);
        }
        return result;
    }
}

[DataContract]
public sealed class ReleaseAsset
{
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "size")] public long Size { get; set; }
    /// <summary>"sha256:&lt;64 hex digits&gt;", computed by GitHub when the file was uploaded. May be missing.</summary>
    [DataMember(Name = "digest")] public string? Digest { get; set; }
    [DataMember(Name = "browser_download_url")] public string? DownloadUrl { get; set; }
}
