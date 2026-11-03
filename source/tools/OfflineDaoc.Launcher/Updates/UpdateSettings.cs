using System.Text.Json;

namespace OfflineDaoc.Updates;

// The launcher's update choices, in runtime\updates\update-settings.json (beside the downloaded packages, never in the
// game's database). Both launchers of a 0.37c game read the same file, as they share the runtime folder.
public sealed class UpdateSettings
{
    public const string FileName = "update-settings.json";

    /// <summary>Look for a new Living Server when the launcher starts (at most once a day).</summary>
    public bool CheckAutomatically { get; set; } = true;

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
}
