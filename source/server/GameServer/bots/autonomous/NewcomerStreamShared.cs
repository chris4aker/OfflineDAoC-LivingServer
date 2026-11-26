using System;

namespace DAoCLivingServer.Shared
{
    /// <summary>
    /// The fields every new gamebot row starts with, whoever makes it: the launcher's "+ Lv.1", "+ Lv.20" and "+ Lv.50"
    /// batches (MainForm.GenerateBotCharacters) and the server's newcomer stream (AutonomousNewcomerStream, issue #630).
    /// One place, linked into the launcher, so a newcomer is queued exactly as a launcher bot is. Pure.
    /// </summary>
    public static class NewBotRow
    {
        /// <summary>The Activity of a new row until its first login.</summary>
        public const string QueuedActivity = "Queued at randomized starting location";

        /// <summary>The CurrentGoal of a new row until its first login.</summary>
        public const string QueuedGoal = "Awaiting staggered login queue";

        /// <summary>The starting health stored on a new row (the server fills it at the first login of a never-saved row).</summary>
        public const int StartingHealth = 1;

        /// <summary>A new bot's gender, 1 (male) or 2 (female), equally likely, as the launcher's batches draw it.</summary>
        public static int DrawGender(Random random) => (random ?? Random.Shared).Next(1, 3);
    }

    /// <summary>
    /// The Activity text the server writes on a gamebot the newcomer stream retired (issue #630: kept with its items and coin,
    /// never deleted, never logged in again), and how the launcher tells such a row from a deletion the player asked for.
    /// Pure; linked into the launcher. The launcher's own delete writes "Deletion requested" over the Activity, so a retiree
    /// the player chooses to delete shows as a deletion again.
    /// </summary>
    public static class RetiredFromPlayMarker
    {
        public const string ActivityPrefix = "Retired from play: ";

        /// <summary>The Activity of a retired gamebot.</summary>
        public static string ActivityFor(int level) =>
            $"{ActivityPrefix}level {Math.Clamp(level, 1, 50)}, made room for a newcomer (kept, never logs in)";

        /// <summary>A retiree: IsRetired and the server's Activity marker. Any other IsRetired row is a deletion request (or a
        /// buffbot copy, BuffBotCopyMarker).</summary>
        public static bool IsRetiree(bool isRetired, string activity) =>
            isRetired && activity != null && activity.StartsWith(ActivityPrefix, StringComparison.Ordinal);
    }
}
