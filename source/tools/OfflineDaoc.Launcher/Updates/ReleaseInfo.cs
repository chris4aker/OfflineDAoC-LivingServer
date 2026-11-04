#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

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

    /// <summary>A published, final release whose tag is a build: the only kind an update is offered from.</summary>
    public bool IsUsable => !Draft && !Prerelease && Version != null;

    /// <summary>The package a release of this build carries: OfflineDAoC-LivingServer-&lt;build&gt;.zip.</summary>
    public static string PackageName(BuildVersion build) => "OfflineDAoC-LivingServer-" + build.Name + ".zip";

    /// <summary>The release's package asset, or null when it has none.</summary>
    public ReleaseAsset? Package
    {
        get
        {
            BuildVersion? build = Version;
            if (build == null || Assets == null)
                return null;
            string name = PackageName(build);
            return Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
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
