using System;
using System.IO;
using System.Net;
using System.Threading;
using OfflineDaoc.Updates;

namespace LivingServerSetup
{
    /// <summary>
    /// Beta 1.1.2: before installing, Setup asks GitHub for the newest Living Server release (the same request and the
    /// same checks as the launcher's update, from the shared source/tools/OfflineDaoc.Launcher/Updates). When its
    /// package is newer than the one inside Setup and made for the same Offline DAoC release, Setup downloads it,
    /// checks it against GitHub's SHA-256 digest and the package rules, and installs it instead. Anything that fails
    /// (offline, a rate limit, a bad hash, a package for another release) leaves the embedded package in place, and the
    /// progress page says so. --no-update (tests, offline installs) skips the question.
    /// </summary>
    internal static class PackageUpdate
    {
        public const string UserAgent = "OfflineDAoC-LivingServer-Setup";

        /// <summary>What was decided, for the progress page's first step and the log. Null until Run.</summary>
        public static string Note { get; private set; }
        public static bool UsedNewer { get; private set; }
        /// <summary>A newer package exists but couldn't be used: the progress page marks the step as skipped.</summary>
        public static bool FellBack { get; private set; }

        /// <summary>The progress page's step title: which Living Server will be installed, and from where.</summary>
        public static string StepTitle => UsedNewer
            ? "Living Server " + Payload.DisplayName + ", the newest release"
            : "Living Server " + Payload.DisplayName + " from inside Setup";

        /// <summary>Decides which package to install. Never throws. status gets short lines while it works.</summary>
        public static void Run(bool enabled, string releaseJsonFile, Action<string> status)
        {
            if (Note != null) return;   // once per run of Setup
            var embedded = BuildVersion.Parse(Payload.EmbeddedBuildName);
            var inside = "the " + Payload.DisplayName + " inside Setup";
            if (!enabled)
            {
                Decide("Checking for a newer Living Server is off (--no-update): installing " + inside + ".");
                return;
            }
            ReleaseInfo release = null;
            string problem = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                status?.Invoke("Looking for a newer Living Server...");
                using (var web = new HttpReleaseWeb(UserAgent))
                {
                    // tests: a recorded or changed answer instead of GitHub's (the download is still real)
                    release = releaseJsonFile != null ? ReleaseInfo.Parse(File.ReadAllText(releaseJsonFile)) : ReleaseFeed.TryGetLatest(web, out problem);
                    if (releaseJsonFile != null) Log.Write("Release answer read from " + releaseJsonFile + " (test).");
                    if (release == null)
                    {
                        Decide("Couldn't ask GitHub for a newer Living Server (" + (problem ?? "no release in the answer") + "): installing " + inside + ".");
                        return;
                    }
                    var newest = release.Version;
                    if (!release.IsUsable || embedded == null || !newest.IsNewerThan(embedded))
                    {
                        Decide("Installing " + inside + ": it is the newest release" + (release.IsUsable ? " (GitHub's newest is " + release.TagName + ")." : "."));
                        return;
                    }
                    Log.Write("Living Server " + newest.Name + " is newer than " + embedded.Name + ": downloading " + release.Package?.Name);
                    string folder = Path.Combine(Path.GetTempPath(), "LivingServer-Setup-download");
                    (string Path, PackageContents Contents) got;
                    try
                    {
                        got = ReleaseFeed.DownloadPackage(web, release, folder, (done, total) =>
                            status?.Invoke("Getting Living Server " + newest.DisplayName + ", newer than the one inside Setup: " +
                                           Theme.Gb(done) + (total > 0 ? " of " + Theme.Gb(total) : "")), CancellationToken.None);
                    }
                    catch (PackageRefusedException e)
                    {
                        Fallback(newest, e.Message, inside);
                        return;
                    }
                    // A package for another Offline DAoC release needs a Setup that downloads and updates to that release.
                    if (Payload.NewestRelease(got.Contents.BaseRelease) != Payload.BaseRelease)
                    {
                        Fallback(newest, "it is made for Offline DAoC " + got.Contents.BaseRelease + ", and this Setup installs " + Payload.BaseRelease +
                                         "; a newer Setup is on the release page", inside);
                        return;
                    }
                    var bytes = File.ReadAllBytes(got.Path);
                    Payload.Use(bytes, "downloaded");
                    UsedNewer = true;
                    Decide("Installing Living Server " + Payload.DisplayName + ", downloaded from its GitHub release and checked against its " +
                           "SHA-256 digest (Setup itself has " + embedded.DisplayName + ").");
                    Log.Write("Living Server package now: " + Payload.BuildName + " for Offline DAoC " + Payload.BaseRelease + ", commit " + Payload.Commit +
                              ", sha256 " + PackageDigest.FileSha256(got.Path));
                }
            }
            catch (Exception e)
            {
                Fallback(release == null ? null : release.Version, ReleaseFeed.Innermost(e).Message, inside);
            }
        }

        static void Fallback(BuildVersion newer, string why, string inside)
        {
            FellBack = true;
            Decide("Couldn't use " + (newer == null ? "the newer Living Server" : "Living Server " + newer.DisplayName) + " (" + why.TrimEnd('.') +
                   "): installing " + inside + " instead.");
        }

        static void Decide(string note)
        {
            Note = note;
            Log.Write("PACKAGE " + note);
        }
    }
}
