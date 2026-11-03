#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

/// <summary>Why a package was refused, in words for the player and the log.</summary>
public sealed class PackageRefusedException : Exception
{
    public PackageRefusedException(string message) : base(message) { }
}

/// <summary>What a checked package holds: its one top folder and its BUILD-INFO.txt.</summary>
public sealed class PackageContents
{
    public PackageContents(string topFolder, IReadOnlyDictionary<string, string> buildInfo)
    {
        TopFolder = topFolder;
        BuildInfo = buildInfo;
    }

    /// <summary>"LivingServer-1.1.2-beta": the folder a player puts in the game folder.</summary>
    public string TopFolder { get; }

    public IReadOnlyDictionary<string, string> BuildInfo { get; }

    public string BuildName => BuildInfo.TryGetValue("BuildName", out string? v) ? v : "";

    public string BaseRelease => BuildInfo.TryGetValue("BaseRelease", out string? v) ? v : "";
}

// Checks a Living Server release package before anything of it is used, the same things tools/installer/build.py
// checks before Setup embeds one: everything sits in one top folder named LivingServer-<build>, BUILD-INFO.txt there
// names the build the release's tag names, the install and rollback scripts are there, and GameServer.dll is the file
// BUILD-INFO.txt names. The package's own INSTALL.cmd then checks every other file against BUILD-INFO.txt.
public static class PackageCheck
{
    public static readonly string[] RequiredFiles =
    {
        "BUILD-INFO.txt", "GameServer.dll", "GameServer.pdb", "INSTALL.cmd", "ROLLBACK.cmd",
        "install-server-build.ps1", "rollback-server-build.ps1", "fork-build-common.ps1",
    };

    public static readonly string[] RequiredBuildInfo = { "BuildName", "BaseRelease", "GameServer.dll", "GameServer.pdb" };

    public static string TopFolderFor(BuildVersion build) => "LivingServer-" + build.Name;

    /// <summary>Checks the package at path for the build expected. Throws PackageRefusedException with the reason.</summary>
    public static PackageContents Inspect(string path, BuildVersion expected)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return Inspect(zip, expected);
        }
        catch (InvalidDataException e)
        {
            throw new PackageRefusedException("The package isn't a readable zip file (" + e.Message + ").");
        }
    }

    public static PackageContents Inspect(ZipArchive zip, BuildVersion expected)
    {
        string top = TopFolderFor(expected);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = Normalize(entry.FullName);
            if (!IsSafe(name))
                throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
            string first = name.Split('/')[0];
            if (!string.Equals(first, top, StringComparison.Ordinal))
                throw new PackageRefusedException("Everything in the package should be in its " + top + " folder, but it has " + entry.FullName + ".");
            names.Add(name);
        }
        foreach (string file in RequiredFiles)
        {
            if (!names.Contains(top + "/" + file))
                throw new PackageRefusedException("The package has no " + file + " in its " + top + " folder.");
        }

        Dictionary<string, string> info;
        using (var reader = new StreamReader(Find(zip, top + "/BUILD-INFO.txt").Open()))
            info = ReadBuildInfo(reader.ReadToEnd());
        foreach (string key in RequiredBuildInfo)
        {
            if (!info.TryGetValue(key, out string? value) || value.Length == 0)
                throw new PackageRefusedException("The package's BUILD-INFO.txt has no " + key + ".");
        }
        if (!string.Equals(info["BuildName"], expected.Name, StringComparison.Ordinal))
            throw new PackageRefusedException("The package's BUILD-INFO.txt names build " + info["BuildName"] + ", but its release is " + expected.Tag + ".");

        string dll;
        using (var sha = SHA256.Create())
        using (Stream s = Find(zip, top + "/GameServer.dll").Open())
            dll = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(dll, info["GameServer.dll"].ToLowerInvariant(), StringComparison.Ordinal))
            throw new PackageRefusedException("The package's GameServer.dll doesn't match its BUILD-INFO.txt.");
        return new PackageContents(top, info);
    }

    /// <summary>
    /// Unpacks a checked package into parent\&lt;top folder&gt; (made empty first) and returns that folder. Every entry
    /// must land inside it; each file is read back and compared with the zip.
    /// </summary>
    public static string Extract(string path, PackageContents contents, string parent)
    {
        string target = Path.Combine(parent, contents.TopFolder);
        if (Directory.Exists(target))
            Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        string root = Path.GetFullPath(target).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string prefix = contents.TopFolder + "/";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = Normalize(entry.FullName);
            if (!IsSafe(name) || !(name + "/").StartsWith(prefix, StringComparison.Ordinal))
                throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
            string rel = name.Length > prefix.Length ? name.Substring(prefix.Length) : "";
            if (rel.Length == 0)
                continue;
            string dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new PackageRefusedException("The package has an unsafe entry: " + entry.FullName);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            byte[] data;
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            File.WriteAllBytes(dest, data);
            if (!File.ReadAllBytes(dest).SequenceEqual(data))
                throw new IOException("Writing " + dest + " failed verification.");
        }
        return target;
    }

    public static Dictionary<string, string> ReadBuildInfo(string text)
    {
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'))
        {
            Match m = Regex.Match(line, @"^\s*([^=#]+?)\s*=\s*(.*?)\s*$");
            if (m.Success)
                info[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return info;
    }

    private static ZipArchiveEntry Find(ZipArchive zip, string name) =>
        zip.Entries.First(e => string.Equals(Normalize(e.FullName), name, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string name) => name.Replace('\\', '/').TrimEnd('/');

    // No absolute paths, drive letters or ".." steps: a package entry may only name a place inside its folder.
    private static bool IsSafe(string name) =>
        name.Length > 0 && !name.StartsWith("/", StringComparison.Ordinal) && name.IndexOf(':') < 0 &&
        name.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}
