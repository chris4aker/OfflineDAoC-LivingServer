using System.Text.Json;

namespace DAoCLivingServer.Updates;

// The launcher's update choices, in runtime\updates\update-settings.json (beside the downloaded packages, never in the
// game's database). Both launchers of a 0.37c game read the same file, as they share the runtime folder.
public sealed class UpdateSettings
{
    public const string FileName = "update-settings.json";

    /// <summary>Look for a new Living Server when the launcher starts (at most once a day).</summary>
    public bool CheckAutomatically { get; set; } = true;

    /// <summary>
    /// Also offer test builds: release candidates published as GitHub pre-releases (v1.2.3-rc.1, issue #637). Off by
    /// default: it's a test switch for contributors, not a player feature (AGENTS.md, "Living Server features are on by
    /// default": test-only switches stay off).
    /// </summary>
    public bool OfferTestBuilds { get; set; }

    /// <summary>The build the player chose "Skip this version" for; it isn't offered again until a newer one.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>When the launcher last asked GitHub on its own.</summary>
    public DateTime? LastCheckUtc { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The saved settings, or the defaults when there are none or they can't be read.</summary>
    public static UpdateSettings Load(string updatesFolder)
    {
        try
        {
            string path = Path.Combine(updatesFolder, FileName);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(path), Options) ?? new UpdateSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file: start again from the defaults.
        }
        return new UpdateSettings();
    }

    public void Save(string updatesFolder)
    {
        Directory.CreateDirectory(updatesFolder);
        string path = Path.Combine(updatesFolder, FileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Options));
        File.Move(path + ".tmp", path, true);
    }
}

// The rules for when the launcher looks for an update and when it offers one. Pure, so they can be tested.
public static class UpdatePolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// Whether to ask GitHub now: always when the player clicks "Check for updates"; otherwise only when automatic
    /// checks are on and the last one was a day or more ago (or the clock went back since).
    /// </summary>
    public static bool ShouldCheck(UpdateSettings settings, DateTime nowUtc, bool asked)
    {
        if (asked)
            return true;
        if (!settings.CheckAutomatically)
            return false;
        if (settings.LastCheckUtc is not { } last)
            return true;
        TimeSpan since = nowUtc - DateTime.SpecifyKind(last, DateTimeKind.Utc);
        return since >= CheckInterval || since < TimeSpan.Zero;
    }

    /// <summary>
    /// Whether this release is an update to offer: a final release of a newer build, with a package to install, that
    /// the player didn't skip. A skipped build is still shown when the player asked for a check.
    /// </summary>
    public static bool ShouldOffer(ReleaseInfo? latest, BuildVersion? installed, UpdateSettings settings, bool asked)
    {
        if (latest is not { IsUsable: true } || installed == null || latest.Package == null)
            return false;
        BuildVersion version = latest.Version!;
        if (!version.IsNewerThan(installed))
            return false;
        return asked || !version.SameBuildAs(BuildVersion.Parse(settings.SkippedVersion));
    }

    /// <summary>
    /// Whether this pre-release is a test build to offer: only with "Offer test builds" on, a release candidate marked
    /// pre-release on GitHub, newer than the installed build, with a package, and not skipped (unless asked).
    /// </summary>
    public static bool ShouldOfferTest(ReleaseInfo? release, BuildVersion? installed, UpdateSettings settings, bool asked)
    {
        if (!settings.OfferTestBuilds || release is not { IsTestBuild: true } || installed == null || release.Package == null)
            return false;
        BuildVersion version = release.Version!;
        if (!version.IsNewerThan(installed))
            return false;
        return asked || !version.SameBuildAs(BuildVersion.Parse(settings.SkippedVersion));
    }

    /// <summary>
    /// The release to offer: the newest final (ShouldOffer) and, with "Offer test builds" on, the newest test build
    /// among releases (ShouldOfferTest); whichever is the newer build. A final outranks the candidates of its numbers
    /// (1.2.3 over 1.2.3-rc.2), so a tester moves on to the final when it is out. Null when there is nothing to offer.
    /// </summary>
    public static ReleaseInfo? Choose(ReleaseInfo? latest, IEnumerable<ReleaseInfo>? releases, BuildVersion? installed,
        UpdateSettings settings, bool asked)
    {
        ReleaseInfo? final = ShouldOffer(latest, installed, settings, asked) ? latest : null;
        ReleaseInfo? test = !settings.OfferTestBuilds || releases == null ? null : releases
            .Where(r => ShouldOfferTest(r, installed, settings, asked))
            .OrderByDescending(r => r.Version!)
            .FirstOrDefault();
        if (final == null)
            return test;
        if (test == null)
            return final;
        return test.Version!.IsNewerThan(final.Version) ? test : final;
    }
}
