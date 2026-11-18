using NUnit.Framework;
using OfflineDaoc.Shared;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Owner decision 2026-11-18: buffbot copies an earlier build saved are retired, never deleted. The bot list shows them as
/// kept ("Retired: copy of Ashfury's buffbot (kept, never logs in)"), not as "Deletion pending"; every other IsRetired row
/// still shows as a deletion, and the Delete button is unchanged (it reads DeletionQueued, which stays IsRetired).
/// </summary>
public sealed class BotDeletionTextTests
{
    private static readonly string Copy = BuffBotCopyMarker.ActivityFor("Ashfury");

    [Test]
    public void ARetiredCopyShowsAsKept()
    {
        Assert.That(Copy, Is.EqualTo("Retired: copy of Ashfury's buffbot (kept, never logs in)"));
        Assert.That(BotDeletionText.IsRetiredCopy(true, "", Copy), Is.True);
        Assert.That(BotDeletionText.Activity("", Copy, serverUp: false), Is.EqualTo(Copy));
        Assert.That(BotDeletionText.Activity("", Copy, serverUp: true), Is.EqualTo(Copy));
        Assert.That(BotDeletionText.State(deletionQueued: true, retiredCopy: true, isOnline: false), Is.EqualTo("Retired"));
    }

    [Test]
    public void EveryOtherRetiredRowStillShowsAsADeletion()
    {
        // the launcher's own delete: Activity "Deletion requested", a class name
        Assert.That(BotDeletionText.Activity("Druid", "Deletion requested", serverUp: false), Is.EqualTo(BotDeletionText.DeletionPending));
        Assert.That(BotDeletionText.Activity("Druid", "Deletion requested", serverUp: true), Is.EqualTo(BotDeletionText.DeletionRequested));
        // the player deleted a copy: the launcher wrote "Deletion requested" over the marker
        Assert.That(BotDeletionText.Activity("", "Deletion requested", serverUp: false), Is.EqualTo(BotDeletionText.DeletionPending));
        // the marker on a row with a class name (not a saved buffbot record), or not retired
        Assert.That(BotDeletionText.IsRetiredCopy(true, "Druid", Copy), Is.False);
        Assert.That(BotDeletionText.Activity("Druid", Copy, serverUp: false), Is.EqualTo(BotDeletionText.DeletionPending));
        Assert.That(BotDeletionText.IsRetiredCopy(false, "", Copy), Is.False);
        Assert.That(BotDeletionText.IsRetiredCopy(true, null, null), Is.False);
        Assert.That(BotDeletionText.State(true, false, false), Is.EqualTo("Deleting…"));
        Assert.That(BotDeletionText.State(false, false, true), Is.EqualTo("Online"));
        Assert.That(BotDeletionText.State(false, false, false), Is.EqualTo("Offline"));
    }
}
