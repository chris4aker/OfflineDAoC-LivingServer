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

    [Test]
    public void AGamebotTheNewcomerStreamRetiredShowsAsKept()
    {
        // #630: a level 50 that made room for a newcomer keeps its row, items and coin; its class name stays.
        string retired = RetiredFromPlayMarker.ActivityFor(50);
        Assert.That(BotDeletionText.IsRetiredCopy(true, "Wizard", retired), Is.True);
        Assert.That(BotDeletionText.Activity("Wizard", retired, serverUp: true), Is.EqualTo(retired));
        Assert.That(BotDeletionText.State(deletionQueued: true, retiredCopy: true, isOnline: false), Is.EqualTo("Retired"));
        Assert.That(BotDeletionText.IsRetiredCopy(false, "Wizard", retired), Is.False);
        // the player deleted it: the launcher wrote "Deletion requested" over the marker
        Assert.That(BotDeletionText.Activity("Wizard", "Deletion requested", serverUp: false), Is.EqualTo(BotDeletionText.DeletionPending));
    }

    [Test]
    public void TheNewcomerStreamIsOnByDefault()
    {
        Assert.That((SocialExpansionSettings.Defaults.PopulationNewcomers, SocialExpansionSettings.Defaults.NewcomerFloor,
            SocialExpansionSettings.Defaults.NewcomerPerHour, SocialExpansionSettings.Defaults.PopulationRetire), Is.EqualTo((true, 50, 6, true)));
        Assert.That((SocialExpansionSettings.Recommended.PopulationNewcomers, SocialExpansionSettings.Recommended.PopulationRetire), Is.EqualTo((true, true)));
        var stored = SocialExpansionSettings.Defaults.ToStoredValues();
        Assert.That((stored["population_newcomers"], stored["population_newcomer_floor"], stored["population_newcomer_per_hour"], stored["population_retire"]),
            Is.EqualTo(("True", "50", "6", "True")));
        var off = SocialExpansionSettings.Defaults.AllOff();
        Assert.That((off.PopulationNewcomers, off.PopulationRetire, off.IsAllOff), Is.EqualTo((false, false, true)));
    }
}
