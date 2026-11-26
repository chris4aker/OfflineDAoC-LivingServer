using DAoCLivingServer.Shared;

namespace DAoCLivingServer.Launcher;

/// <summary>
/// The bot list's Activity and State for a row with IsRetired set. Such a row is a deletion the player asked for, except a
/// buffbot copy the server retired (owner decision 2026-11-18: retire, never delete; BuffBotCopyMarker) or a gamebot the
/// newcomer stream retired (#630; RetiredFromPlayMarker). A copy shows as
/// kept, and nothing about the Delete button changes.
/// </summary>
public static class BotDeletionText
{
    public const string DeletionRequested = "Deletion requested — waiting for safe server removal";
    public const string DeletionPending = "Deletion pending — Delete is available while the server is stopped";

    /// <summary>True for a row the server retired and kept: a buffbot copy (IsRetired, no class name, its Activity marker)
    /// or a gamebot the newcomer stream retired (#630, RetiredFromPlayMarker).</summary>
    public static bool IsRetiredCopy(bool isRetired, string? className, string? storedActivity) =>
        BuffBotCopyMarker.IsRetiredCopy(isRetired, className ?? string.Empty, storedActivity) ||
        RetiredFromPlayMarker.IsRetiree(isRetired, storedActivity ?? string.Empty);

    /// <summary>The Activity of an IsRetired row: the copy's own text ("Retired: copy of Ashfury's buffbot (kept, never logs
    /// in)"), else the deletion text for a running or stopped server.</summary>
    public static string Activity(string? className, string? storedActivity, bool serverUp) =>
        IsRetiredCopy(true, className, storedActivity) ? storedActivity!
        : serverUp ? DeletionRequested : DeletionPending;

    /// <summary>The State column: "Retired" for a copy, "Deleting…" for a deletion, else Online or Offline.</summary>
    public static string State(bool deletionQueued, bool retiredCopy, bool isOnline) =>
        deletionQueued ? retiredCopy ? "Retired" : "Deleting…" : isOnline ? "Online" : "Offline";
}
