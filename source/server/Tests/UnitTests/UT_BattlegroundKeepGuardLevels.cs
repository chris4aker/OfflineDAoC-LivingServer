using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;
using Props = DOL.GS.ServerProperties.Properties;

namespace DOL.GS.Tests;

/// <summary>
/// Owner, 2026-11-19: "I want the guards and lord levels appropriate for their battleground". Owner's PC, build c81eb5d
/// (BG_KEEP_STATUS 09:07-09:40): Caer Caledon's lord 57 and guards up to 48 in Caledonia (30-35), from its base level 46;
/// Thidranki's lord 33 (base 26), Murdaigean's 40 (base 31), Abermenai's 28 (base 21).
/// </summary>
[TestFixture]
[NonParallelizable]
public class UT_BattlegroundKeepGuardLevels
{
    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
    private sealed class Server : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private GameServer _previous;
    private double _guardMultiplier;
    private double _towerMultiplier;

    [SetUp]
    public void SetUp()
    {
        // The brackets are read once; read them before the test server (which has no keep manager) is in place.
        _ = BattlegroundBrackets.Get(0);
        _previous = GameServer.Instance;
        // Guards are game objects, whose type setup reads the database.
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        _guardMultiplier = Props.KEEP_GUARD_LEVEL_MULTIPLIER;
        _towerMultiplier = Props.TOWER_GUARD_LEVEL_MULTIPLIER;
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = 1.6; // the shipped values
        Props.TOWER_GUARD_LEVEL_MULTIPLIER = 1.0;
    }

    [TearDown]
    public void TearDown()
    {
        GameServer.LoadTestDouble(_previous);
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = _guardMultiplier;
        Props.TOWER_GUARD_LEVEL_MULTIPLIER = _towerMultiplier;
    }

    private static GameKeep Keep(string name, ushort region, int baseLevel, int level = 1, int keepId = 31)
    {
        var keep = (GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(GameKeep));
        keep.DBKeep = new DbKeep { KeepID = (ushort)keepId, Name = name, Region = region, BaseLevel = (byte)baseLevel, Level = (byte)level };
        return keep;
    }

    private static T Guard<T>(AbstractGameKeep keep) where T : GameKeepGuard
    {
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        var guard = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        guard.Component = component;
        return guard;
    }

    private static byte GuardLevel(AbstractGameKeep keep) => keep.GuardLevelFor(Guard<GuardFighter>(keep));
    private static byte LordLevel(AbstractGameKeep keep) => keep.GuardLevelFor(Guard<GuardLord>(keep));

    // battleground, region, bracket top, base level, guards and lord at keep level 1
    [TestCase("Abermenai", (ushort)253, 19, 21, 23, 28)]
    [TestCase("Thidranki", (ushort)252, 24, 26, 28, 33)]
    [TestCase("Murdaigean", (ushort)251, 29, 31, 33, 40)]
    [TestCase("Caledonia", (ushort)250, 35, 37, 39, 46)]
    public void Rule_EachBattleground(string name, ushort region, int top, int baseLevel, int guards, int lord)
    {
        BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForRegion(region);
        Assert.Multiple(() =>
        {
            Assert.That(bracket.Name, Is.EqualTo(name));
            Assert.That(bracket.MaxLevel, Is.EqualTo((byte)top));
            Assert.That(BattlegroundKeepLevels.BaseLevelFor(bracket), Is.EqualTo((byte)baseLevel));
            Assert.That(BattlegroundKeepLevels.Cap(bracket, false), Is.EqualTo((byte)guards));
            Assert.That(BattlegroundKeepLevels.Cap(bracket, true), Is.EqualTo((byte)lord));
            // The keep itself, through DOL's formula, once at its bracket's base level.
            GameKeep keep = Keep(bracket.CentralKeep, region, baseLevel);
            Assert.That(GuardLevel(keep), Is.EqualTo((byte)guards), "guards");
            Assert.That(LordLevel(keep), Is.EqualTo((byte)lord), "lord");
            Assert.That(guards, Is.EqualTo(top + 4), "guards at the bracket top + 4");
        });
    }

    [Test]
    public void Caledon_TheShippedBaseLevelGaveTheLogsLevels_AndTheRepairFixesIt()
    {
        // Without the repair the unchanged formula reproduces the log exactly (base 46: guards 48, lord 57).
        Assert.That(BattlegroundKeepLevels.KeepGuardLevel(46, 1, 1.6, lord: false), Is.EqualTo((byte)48));
        Assert.That(BattlegroundKeepLevels.KeepGuardLevel(46, 1, 1.6, lord: true), Is.EqualTo((byte)57));

        GameKeep caledon = Keep("Caer Caledon", 250, 46);
        Assert.That(BattlegroundKeepLevels.Repair(caledon), Is.EqualTo((byte?)46));
        Assert.Multiple(() =>
        {
            Assert.That(caledon.DBKeep.BaseLevel, Is.EqualTo((byte)37), "the row");
            Assert.That(caledon.BaseLevel, Is.EqualTo((byte)37), "in memory");
            Assert.That(caledon.IsBattlegroundKeep, Is.True);
            Assert.That(GuardLevel(caledon), Is.EqualTo((byte)39));
            Assert.That(LordLevel(caledon), Is.EqualTo((byte)46));
        });
    }

    [Test]
    public void Caledon_EvenUnrepaired_IsCappedAtItsBattlegroundLevel()
    {
        GameKeep caledon = Keep("Caer Caledon", 250, 46);
        Assert.That(GuardLevel(caledon), Is.EqualTo((byte)39));
        Assert.That(LordLevel(caledon), Is.EqualTo((byte)46));
    }

    [Test]
    public void Repair_IsIdempotent()
    {
        GameKeep caledon = Keep("Caer Caledon", 250, 46);
        Assert.That(BattlegroundKeepLevels.Repair(caledon), Is.EqualTo((byte?)46));
        Assert.That(BattlegroundKeepLevels.Repair(caledon), Is.Null, "second start");
        Assert.That(caledon.DBKeep.BaseLevel, Is.EqualTo((byte)37));
        foreach (GameKeep keep in new[] { Keep("Dun Abermenai", 253, 21), Keep("Thidranki Faste", 252, 26), Keep("Dun Murdaigean", 251, 31) })
        {
            byte before = keep.DBKeep.BaseLevel;
            Assert.That(BattlegroundKeepLevels.Repair(keep), Is.Null, keep.Name + " is already right");
            Assert.That(keep.DBKeep.BaseLevel, Is.EqualTo(before));
        }
    }

    [TestCase(4)]
    [TestCase(10)]
    public void Cap_AfterUpgradesOrAClaim(int keepLevel)
    {
        foreach ((ushort region, int guards, int lord) in new[] { ((ushort)253, 23, 28), ((ushort)252, 28, 33), ((ushort)251, 33, 40), ((ushort)250, 39, 46) })
        {
            BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForRegion(region);
            GameKeep keep = Keep(bracket.CentralKeep, region, BattlegroundKeepLevels.BaseLevelFor(bracket), keepLevel);
            Assert.That(BattlegroundKeepLevels.KeepGuardLevel(keep.BaseLevel, keepLevel, 1.6, false), Is.GreaterThan((byte)guards),
                "the upgrade would raise them");
            Assert.That(GuardLevel(keep), Is.EqualTo((byte)guards), bracket.Name + " guards");
            Assert.That(LordLevel(keep), Is.EqualTo((byte)lord), bracket.Name + " lord");
        }
    }

    [Test]
    public void Cap_HoldsWithABiggerMultiplierOrARaisedBaseLevel()
    {
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = 5;
        GameKeep thidranki = Keep("Thidranki Faste", 252, 26, 1, 11);
        Assert.That(GuardLevel(thidranki), Is.EqualTo((byte)28));
        Assert.That(LordLevel(thidranki), Is.EqualTo((byte)33));
        Props.KEEP_GUARD_LEVEL_MULTIPLIER = 1.6;
        thidranki.BaseLevel = 40; // keep balancing or a GM
        Assert.That(GuardLevel(thidranki), Is.EqualTo((byte)28));
        Assert.That(LordLevel(thidranki), Is.EqualTo((byte)33));
    }

    [Test]
    public void Cap_NeverRaisesALowerLevel()
    {
        BattlegroundBrackets.Bracket caledonia = BattlegroundBrackets.ForRegion(250);
        Assert.That(BattlegroundKeepLevels.Capped(caledonia, false, 20), Is.EqualTo((byte)20));
        Assert.That(BattlegroundKeepLevels.Capped(null, true, 77), Is.EqualTo((byte)77));
    }

    [Test]
    public void FrontierPortalAndOtherKeeps_Unchanged()
    {
        // A frontier keep at level 4 and 10, a battleground portal keep, and the old battleground regions without a
        // bracket (Cathal Valley's Dun Orseo, Molvik Faste, Dun Killaloe, Braemar Middle Camp).
        GameKeep frontier = Keep("Caer Benowyc", 1, 50, 4, 50);
        GameKeep upgraded = Keep("Caer Benowyc", 1, 50, 10, 50);
        GameKeep portal = Keep("Albion Portal Keep", 250, 255, 1, 40);
        GameKeep[] others = { Keep("Dun Orseo", 165, 39, 1, 5), Keep("Molvik Faste", 241, 39, 1, 132),
            Keep("Dun Killaloe", 237, 19, 1, 138), Keep("Braemar Middle Camp", 239, 1, 1, 10) };
        Assert.Multiple(() =>
        {
            Assert.That(GuardLevel(frontier), Is.EqualTo((byte)57), "frontier guards: 51 + 4 x 1.6");
            Assert.That(LordLevel(frontier), Is.EqualTo((byte)68), "frontier lord: 62 + 4 x 1.6");
            Assert.That(GuardLevel(upgraded), Is.EqualTo((byte)67));
            Assert.That(LordLevel(upgraded), Is.EqualTo((byte)78));
            Assert.That(BattlegroundKeepLevels.Repair(frontier), Is.Null);
            Assert.That(frontier.DBKeep.BaseLevel, Is.EqualTo((byte)50));
            Assert.That(BattlegroundKeepLevels.BracketOf(portal), Is.Null);
            Assert.That(BattlegroundKeepLevels.Repair(portal), Is.Null);
            Assert.That(portal.DBKeep.BaseLevel, Is.EqualTo((byte)255));
            foreach (GameKeep keep in others)
            {
                byte guards = BattlegroundKeepLevels.KeepGuardLevel(keep.BaseLevel, 1, 1.6, false);
                byte lord = BattlegroundKeepLevels.KeepGuardLevel(keep.BaseLevel, 1, 1.6, true);
                Assert.That(BattlegroundKeepLevels.BracketOf(keep), Is.Null, keep.Name);
                Assert.That(BattlegroundKeepLevels.Repair(keep), Is.Null, keep.Name);
                Assert.That(GuardLevel(keep), Is.EqualTo(guards), keep.Name);
                Assert.That(LordLevel(keep), Is.EqualTo(lord), keep.Name);
            }
        });
    }

    [Test]
    public void Tower_Unchanged()
    {
        var tower = (GameKeepTower)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepTower));
        tower.DBKeep = new DbKeep { KeepID = 1280, Name = "Benowyc tower", Region = 1, BaseLevel = 50, Level = 4 };
        Assert.That(tower.GuardLevelFor(Guard<GuardFighter>(tower)), Is.EqualTo((byte)54), "50 + 4 x 1.0");
        Assert.That(tower.GuardLevelFor(Guard<GuardLord>(tower)), Is.EqualTo((byte)56), "captain: 52 + 4 x 1.0");
    }

    [Test]
    public void ReleaseWorldDefaults_GiveEachBattlegroundKeepItsBracketBaseLevel()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        JsonElement keeps = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "source", "tools", "release-world-defaults.json")))
            .RootElement.GetProperty("Keep");
        for (int index = 0; index < BattlegroundBrackets.Count; index++)
        {
            BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.Get(index);
            JsonElement row = keeps.EnumerateObject().Select(p => p.Value).Single(v => v.GetProperty("Name").GetString() == bracket.CentralKeep);
            Assert.That(row.GetProperty("BaseLevel").GetInt32(), Is.EqualTo(BattlegroundKeepLevels.BaseLevelFor(bracket)), bracket.CentralKeep);
        }
    }
}
