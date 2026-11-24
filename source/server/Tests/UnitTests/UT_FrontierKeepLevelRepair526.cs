using System.IO;
using System.Linq;
using System.Text.Json;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #526 (owner decision 2026-11-24): every claimable frontier keep ships at level 5, and worlds from earlier builds are
/// raised once, only where the keep still holds the old shipped level (FrontierKeepLevelRepair).
/// </summary>
[TestFixture]
public class UT_FrontierKeepLevelRepair526
{
    private static JsonElement Keeps()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            .RootElement.GetProperty("Keep");
    }

    /// <summary>The claimable frontier keeps: owned by a realm, not a portal keep, not a relic keep (level 10).</summary>
    [Test]
    public void ReleaseDefaults_EveryClaimableFrontierKeepIsLevel5()
    {
        var frontier = Keeps().EnumerateObject()
            .Select(keep => (Id: int.Parse(keep.Name), Name: keep.Value.GetProperty("Name").GetString(),
                Realm: keep.Value.GetProperty("Realm").GetInt32(), Level: keep.Value.GetProperty("Level").GetInt32(),
                Battleground: keep.Value.TryGetProperty("BaseLevel", out _)))
            .Where(keep => keep.Realm is >= 1 and <= 3 && !keep.Battleground &&
                           !keep.Name.Contains("Portal", System.StringComparison.OrdinalIgnoreCase) && keep.Level < 10)
            .ToArray();
        Assert.That(frontier, Has.Length.EqualTo(21), "seven claimable keeps a realm");
        Assert.Multiple(() =>
        {
            foreach (var keep in frontier)
                Assert.That(keep.Level, Is.EqualTo(FrontierKeepLevelRepair.FrontierKeepLevel), $"{keep.Id} {keep.Name}");
            foreach (int realm in new[] { 1, 2, 3 })
                Assert.That(frontier.Count(keep => keep.Realm == realm), Is.EqualTo(7), $"realm {realm}");
        });
    }

    [Test]
    public void Raises_MatchTheReleaseDefaults_AndOnlyRaise()
    {
        JsonElement keeps = Keeps();
        Assert.Multiple(() =>
        {
            foreach (FrontierKeepLevelRepair.Raise raise in FrontierKeepLevelRepair.Raises)
            {
                JsonElement keep = keeps.GetProperty(raise.KeepId.ToString());
                Assert.That(keep.GetProperty("Name").GetString(), Is.EqualTo(raise.Name));
                Assert.That(keep.GetProperty("Realm").GetInt32(), Is.EqualTo(3), raise.Name);
                Assert.That(keep.GetProperty("Level").GetInt32(), Is.EqualTo(raise.NewLevel), raise.Name);
                Assert.That(raise.NewLevel, Is.GreaterThan(raise.OldLevel), raise.Name);
            }
            Assert.That(FrontierKeepLevelRepair.Raises.Select(raise => raise.KeepId), Is.EquivalentTo(new[] { 100, 103, 104, 106 }));
        });
    }

    [TestCase(1, false, 5)]     // nGed as 1.2.2 shipped it
    [TestCase(1, true, null)]   // a guild holds it: its level is the guild's
    [TestCase(4, false, null)]  // captured since (reset to starting_keep_level): not the old default
    [TestCase(5, false, null)]  // already raised (a 1.2.3 world, or a GM)
    [TestCase(3, false, null)]
    public void TargetLevel_OnlyTheUntouchedOldDefault(int level, bool claimed, int? expected)
    {
        var nGed = FrontierKeepLevelRepair.Raises.Single(raise => raise.KeepId == 103);
        Assert.That(FrontierKeepLevelRepair.TargetLevel(nGed, (byte)level, claimed), Is.EqualTo(expected == null ? null : (byte?)expected));
    }

    [TestCase(4, false, 5)]     // Crauchon as shipped
    [TestCase(1, false, null)]  // a level a GM set below the old default is left alone
    [TestCase(10, true, null)]
    public void TargetLevel_Crauchon(int level, bool claimed, int? expected)
    {
        var crauchon = FrontierKeepLevelRepair.Raises.Single(raise => raise.KeepId == 100);
        Assert.That(FrontierKeepLevelRepair.TargetLevel(crauchon, (byte)level, claimed), Is.EqualTo(expected == null ? null : (byte?)expected));
    }

    [TestCase(1, 10000)]
    [TestCase(4, 40000)]
    [TestCase(5, 50000)]
    public void DoorMaxHealth_IsTheCalculatorsFormula(int level, int expected)
    {
        Assert.That(FrontierKeepLevelRepair.DoorMaxHealth(50, level, 200, 1.0), Is.EqualTo(expected));
    }

    [TestCase(10000, 10000, 50000, false, 50000)]  // a fresh 1.2.3 world: the shipped door row at level 1's full health
    [TestCase(10000, 10000, 50000, true, 50000)]   // an old world, keep raised now, door whole
    [TestCase(40000, 40000, 50000, true, 50000)]   // Crauchon's doors
    [TestCase(5000, 10000, 50000, true, 25000)]    // hurt to half: still half
    [TestCase(5000, 10000, 50000, false, 5000)]    // keep not raised now: a hurt door is left as it is
    [TestCase(30000, 10000, 50000, false, 30000)]  // not the shipped value: left
    [TestCase(0, 10000, 50000, true, 0)]           // broken stays broken (it is repaired by the game)
    [TestCase(10000, 10000, 10000, true, 10000)]   // never lower, nothing to do at the same level
    public void DoorHealthAfter(int health, int oldFull, int newFull, bool raised, int expected)
    {
        Assert.That(FrontierKeepLevelRepair.DoorHealthAfter(health, oldFull, newFull, raised), Is.EqualTo(expected));
    }

    [Test]
    public void RepairList_RecordedOnce_BesideOtherRepairs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FrontierKeepLevelRepair.AlreadyRan(""), Is.False);
            Assert.That(FrontierKeepLevelRepair.AlreadyRan("buffbot-copies"), Is.False);
            Assert.That(FrontierKeepLevelRepair.WithRepair(""), Is.EqualTo(FrontierKeepLevelRepair.RepairName));
            string both = FrontierKeepLevelRepair.WithRepair("buffbot-copies");
            Assert.That(both, Is.EqualTo("buffbot-copies," + FrontierKeepLevelRepair.RepairName));
            Assert.That(FrontierKeepLevelRepair.AlreadyRan(both), Is.True);
            Assert.That(FrontierKeepLevelRepair.WithRepair(both), Is.EqualTo(both));
            Assert.That(BuffBotCopyRetirement.AlreadyRan(both), Is.True, "the buffbot repair still reads its own name");
        });
    }
}
