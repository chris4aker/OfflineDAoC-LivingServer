using System;

namespace OfflineDaoc.Shared
{
    /// <summary>
    /// The Activity text the server writes on a retired buffbot copy (BuffBotCopyRetirement, owner decision 2026-11-18:
    /// retire, never delete), and how the launcher tells such a row from a deletion the player asked for. Pure; linked into
    /// the launcher. The launcher's own delete writes "Deletion requested" over the Activity, so a copy the player chooses
    /// to delete is shown as a deletion again.
    /// </summary>
    public static class BuffBotCopyMarker
    {
        public const string ActivityPrefix = "Retired: copy of ";

        /// <summary>The Activity of a retired copy of <paramref name="mainName"/>'s buffbot.</summary>
        public static string ActivityFor(string mainName) =>
            $"{ActivityPrefix}{(string.IsNullOrWhiteSpace(mainName) ? "a main" : mainName.Trim())}'s buffbot (kept, never logs in)";

        /// <summary>A retired copy: IsRetired, no class name (only a saved buffbot record has none) and the server's
        /// Activity marker. Any other IsRetired row is a deletion request.</summary>
        public static bool IsRetiredCopy(bool isRetired, string className, string activity) =>
            isRetired && string.IsNullOrWhiteSpace(className) &&
            activity != null && activity.StartsWith(ActivityPrefix, StringComparison.Ordinal);
    }
}
