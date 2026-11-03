#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

// A Living Server build name as the releases use it: "1.1.1-beta", or its tag "v1.1.1-beta". Shared by the launcher
// and Setup (tools/installer links this file), so both read and compare builds by the same rules:
// - numbers compare as numbers, missing ones are 0: 1.1-beta < 1.1.1-beta < 1.1.2-beta < 1.2-beta;
// - a final release outranks the beta of the same numbers: 1.2-beta < 1.2;
// - a numbered test build of a beta comes after it: 1.1-beta < 1.1-beta.2.
// Anything else ("latest", "0.37-se.2", "v1.x-beta") is not a build this reads, and Parse returns null.
public sealed class BuildVersion : IComparable<BuildVersion>
{
    private static readonly Regex Shape = new(@"^v?(\d{1,6}(?:\.\d{1,6}){1,3})(-beta(?:\.(\d{1,6}))?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly int[] _numbers;

    private BuildVersion(int[] numbers, bool beta, int betaBuild, string name)
    {
        _numbers = numbers;
        IsBeta = beta;
        BetaBuild = betaBuild;
        Name = name;
    }

    /// <summary>The build name without the tag's "v", e.g. "1.1.2-beta".</summary>
    public string Name { get; }

    /// <summary>The tag a release of this build has: "v" + Name.</summary>
    public string Tag => "v" + Name;

    public bool IsBeta { get; }

    public int BetaBuild { get; }

    /// <summary>The build named by a tag or build name, or null when it isn't one.</summary>
    public static BuildVersion? Parse(string? text)
    {
        if (text == null)
            return null;
        Match m = Shape.Match(text.Trim());
        if (!m.Success)
            return null;
        int[] numbers = m.Groups[1].Value.Split('.').Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        bool beta = m.Groups[2].Success;
        int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        string name = text.Trim();
        if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(1);
        return new BuildVersion(numbers, beta, build, name.ToLowerInvariant());
    }

    /// <summary>"Beta 1.1.2" for "1.1.2-beta", "1.2" for a final release (the names the release pages use).</summary>
    public string DisplayName =>
        IsBeta ? "Beta " + string.Join(".", _numbers) + (BetaBuild > 0 ? " (build " + BetaBuild + ")" : "") : string.Join(".", _numbers);

    public int CompareTo(BuildVersion? other)
    {
        if (other == null)
            return 1;
        int length = Math.Max(_numbers.Length, other._numbers.Length);
        for (int i = 0; i < length; i++)
        {
            int mine = i < _numbers.Length ? _numbers[i] : 0;
            int theirs = i < other._numbers.Length ? other._numbers[i] : 0;
            if (mine != theirs)
                return mine.CompareTo(theirs);
        }
        if (IsBeta != other.IsBeta)
            return IsBeta ? -1 : 1;
        return BetaBuild.CompareTo(other.BetaBuild);
    }

    public bool IsNewerThan(BuildVersion? other) => CompareTo(other) > 0;

    public bool SameBuildAs(BuildVersion? other) => other != null && CompareTo(other) == 0;

    public override string ToString() => Name;
}
