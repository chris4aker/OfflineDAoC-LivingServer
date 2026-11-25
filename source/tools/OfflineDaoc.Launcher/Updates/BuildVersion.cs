#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Updates;

// A Living Server build name as the releases use it: "1.1.1-beta", or its tag "v1.1.1-beta". Shared by the launcher
// and Setup (tools/installer links this file), so both read and compare builds by the same rules:
// - numbers compare as numbers, missing ones are 0: 1.1-beta < 1.1.1-beta < 1.1.2-beta < 1.2-beta;
// - a final release outranks the beta of the same numbers: 1.2-beta < 1.2. The releases were betas up to 1.1.4
//   (v1.1.4-beta) and are final from 1.2 on (v1.2, owner 2026-11-19), so 1.1.4-beta < 1.2 < 1.2.1-beta < 1.2.1;
// - a numbered test build of a beta comes after it: 1.1-beta < 1.1-beta.2;
// - a release candidate (a GitHub pre-release, issue #637) comes after the betas and before the final of the same
//   numbers, by its number: 1.2.2 < 1.2.3-beta < 1.2.3-rc.1 < 1.2.3-rc.2 < 1.2.3. Its package is the final's build
//   (BUILD-INFO.txt names 1.2.3, in a LivingServer-1.2.3 folder): PackageBuild.
// Anything else ("latest", "0.37-se.2", "v1.x-beta") is not a build this reads, and Parse returns null.
public sealed class BuildVersion : IComparable<BuildVersion>
{
    private static readonly Regex Shape = new(@"^v?(\d{1,6}(?:\.\d{1,6}){1,3})(?:(-beta(?:\.(\d{1,6}))?)|-rc\.(\d{1,6}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly int[] _numbers;

    private BuildVersion(int[] numbers, bool beta, int betaBuild, int candidate, string name)
    {
        _numbers = numbers;
        IsBeta = beta;
        BetaBuild = betaBuild;
        Candidate = candidate;
        Name = name;
    }

    /// <summary>The build name without the tag's "v", e.g. "1.1.2-beta".</summary>
    public string Name { get; }

    /// <summary>The tag a release of this build has: "v" + Name.</summary>
    public string Tag => "v" + Name;

    public bool IsBeta { get; }

    public int BetaBuild { get; }

    /// <summary>The N of a release candidate "1.2.3-rc.N" (a GitHub pre-release), 0 for any other build.</summary>
    public int Candidate { get; }

    public bool IsCandidate => Candidate > 0;

    /// <summary>
    /// The build a release's package is: the build itself, or for a candidate the final it is a candidate of (the
    /// package of v1.2.3-rc.1 is build 1.2.3, made by the same build-and-test run a final would be published from).
    /// </summary>
    public BuildVersion PackageBuild => IsCandidate ? new BuildVersion(_numbers, false, 0, 0, string.Join(".", _numbers)) : this;

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
        int candidate = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups[4].Success && candidate == 0)
            return null; // "-rc.0" isn't a candidate: they count from 1
        string name = text.Trim();
        if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(1);
        return new BuildVersion(numbers, beta, build, candidate, name.ToLowerInvariant());
    }

    /// <summary>
    /// "Beta 1.1.2" for "1.1.2-beta", "1.2" for a final release (the names the release pages use), "1.2.3 test build 1"
    /// for the candidate "1.2.3-rc.1".
    /// </summary>
    public string DisplayName =>
        IsCandidate ? string.Join(".", _numbers) + " test build " + Candidate :
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
        int rank = Rank.CompareTo(other.Rank);
        if (rank != 0)
            return rank;
        return IsCandidate ? Candidate.CompareTo(other.Candidate) : BetaBuild.CompareTo(other.BetaBuild);
    }

    // Of the same numbers: a beta, then the candidates, then the final.
    private int Rank => IsBeta ? 0 : IsCandidate ? 1 : 2;

    public bool IsNewerThan(BuildVersion? other) => CompareTo(other) > 0;

    public bool SameBuildAs(BuildVersion? other) => other != null && CompareTo(other) == 0;

    public override string ToString() => Name;
}
