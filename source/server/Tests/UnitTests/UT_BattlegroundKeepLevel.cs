using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;
using Props = DOL.GS.ServerProperties.Properties;

namespace DOL.GS.Tests;

/// <summary>
/// Battleground keeps can't be claimed, so they never upgrade: they stay at level 1 (owner's PC, build 3b808b4,
/// 2026-11-15: Thidranki Faste was at level 4 after a capture, guards 33 and lord 38 against levels 20-24).
/// </summary>
[TestFixture]
public class UT_BattlegroundKeepLevel
{
    private int _startingKeepLevel;
    private double _guardMultiplier;

    [SetUp]
    public void SetUp()
    {
        _startingKeepLevel = Props.STARTING_KEEP_LEVEL;
        _guardMultiplier = Props.KEEP_GUARD_LEVEL_MULTIPLIER;
        Props.STARTING_KEEP_LEVEL = 4; // the shipped value
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = 1.6; // DOL's value (hard-coded before it became a property in 2009)
    }

    [TearDown]
    public void TearDown()
    {
        Props.STARTING_KEEP_LEVEL = _startingKeepLevel;
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = _guardMultiplier;
    }

    private static GameKeep Keep(int baseLevel, int level, int keepId = 11)
    {
        var keep = (GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(GameKeep));
        keep.DBKeep = new DbKeep { KeepID = (ushort)keepId, Name = "Thidranki Faste", BaseLevel = (byte)baseLevel, Level = (byte)level, Realm = 1 };
        return keep;
    }

    [TestCase(26, false, 4, 1)]   // Thidranki Faste
    [TestCase(21, false, 4, 1)]   // Dun Abermenai
    [TestCase(46, false, 5, 1)]   // Caer Caledon, whatever starting_keep_level says
    [TestCase(50, false, 4, 4)]   // a frontier keep keeps starting_keep_level
    [TestCase(100, true, 4, 4)]   // portal keeps
    [TestCase(255, true, 4, 4)]
    public void UnclaimedLevel_IsOneForBattlegroundKeeps_StartingLevelOtherwise(int baseLevel, bool portal, int starting, int expected)
    {
        Assert.That(AbstractGameKeep.UnclaimedLevelFor(baseLevel, portal, starting), Is.EqualTo((byte)expected));
    }

    [Test]
    public void UnclaimedLevel_OfAKeep()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Keep(26, 4).IsBattlegroundKeep, Is.True);
            Assert.That(Keep(26, 4).UnclaimedLevel, Is.EqualTo(AbstractGameKeep.BattlegroundKeepLevel));
            Assert.That(Keep(50, 4).IsBattlegroundKeep, Is.False);
            Assert.That(Keep(50, 4).UnclaimedLevel, Is.EqualTo((byte)4));
            Assert.That(Keep(100, 1).IsBattlegroundKeep, Is.False, "battleground portal keep");
            Assert.That(Keep(255, 1).UnclaimedLevel, Is.EqualTo((byte)4));
        });
    }

    [Test]
    public void Load_PutsAPlayedBattlegroundKeepBackToLevelOne()
    {
        GameKeep thidranki = Keep(26, 4);
        Assert.That(thidranki.RestoreBattlegroundKeepLevel(), Is.EqualTo((byte?)4));
        Assert.That(thidranki.Level, Is.EqualTo((byte)1));
        Assert.That(thidranki.DBKeep.Level, Is.EqualTo((byte)1), "written to the row at the next save");
        Assert.That(thidranki.RestoreBattlegroundKeepLevel(), Is.Null, "already level 1");
    }

    [Test]
    public void Load_LeavesFrontierPortalAndClaimedKeepsAlone()
    {
        GameKeep frontier = Keep(50, 4, 50);
        GameKeep portal = Keep(100, 10, 1);
        GameKeep claimed = Keep(26, 5); // only if allow_bg_claim is turned on
        claimed.Guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        Assert.Multiple(() =>
        {
            Assert.That(frontier.RestoreBattlegroundKeepLevel(), Is.Null);
            Assert.That(frontier.Level, Is.EqualTo((byte)4));
            Assert.That(portal.RestoreBattlegroundKeepLevel(), Is.Null);
            Assert.That(portal.Level, Is.EqualTo((byte)10));
            Assert.That(claimed.RestoreBattlegroundKeepLevel(), Is.Null);
            Assert.That(claimed.Level, Is.EqualTo((byte)5));
        });
    }

    [Test]
    public void ReleaseWorldDefaults_GiveEveryBattlegroundKeepTheBattlegroundLevel()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        JsonElement keeps = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            .RootElement.GetProperty("Keep");
        // The battleground keeps of the shipped world (base level below 50).
        string[] names = { "Dun Orseo", "Braemar Middle Camp", "Thidranki Faste", "Caer Caledon", "Dun Murdaigean", "Dun Abermenai", "Molvik Faste", "Dun Killaloe" };
        foreach (string name in names)
        {
            JsonElement row = keeps.EnumerateObject().Select(p => p.Value).Single(v => v.GetProperty("Name").GetString() == name);
            Assert.That(row.GetProperty("Level").GetInt32(), Is.EqualTo(AbstractGameKeep.BattlegroundKeepLevel), name);
        }
    }

    [Test]
    public void KeepStatus_GuardLevelRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDashboard.GuardLevelRange(Array.Empty<byte>()), Is.EqualTo("none"));
            Assert.That(AutonomousRvrDashboard.GuardLevelRange(new byte[] { 28, 28 }), Is.EqualTo("28"));
            Assert.That(AutonomousRvrDashboard.GuardLevelRange(new byte[] { 33, 28, 30 }), Is.EqualTo("28-33"));
        });
    }
}
