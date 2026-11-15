using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// The realm cards' "+ Lv.20" button (Living Server 1.2; owner's PC, build 1dc66b6, 2026-11-15: every test-world bot was level
/// 50, so every battleground was empty). Its bots get a level inside the Thidranki bracket (20-24), the server's experience for that
/// level and a small purse; the brackets and the experience table are the server's own.
/// </summary>
public sealed class BattlegroundBotLevelsTests
{
    private static string ServerSource(params string[] path) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            new[] { TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "server", "GameServer" }.Concat(path).ToArray())));

    [Test]
    public void TheBracketsAreTheServersOwn()
    {
        // new("Thidranki", "Thidranki Faste", 252, 20, 24) in DOL.GS.BattlegroundBrackets.Period
        MatchCollection rows = Regex.Matches(ServerSource("world", "BattlegroundBrackets.cs"),
            "new\\(\"(\\w+)\", \"[^\"]+\", (\\d+), (\\d+), (\\d+)\\)");
        var server = rows.Select(row => new BattlegroundBotLevels.Bracket(row.Groups[1].Value, ushort.Parse(row.Groups[2].Value),
            int.Parse(row.Groups[3].Value), int.Parse(row.Groups[4].Value))).ToArray();
        Assert.That(server, Has.Length.EqualTo(4));
        Assert.That(BattlegroundBotLevels.Period, Is.EqualTo(server));
    }

    [Test]
    public void TheDefaultIsThidranki()
    {
        Assert.That(BattlegroundBotLevels.Default, Is.EqualTo(new BattlegroundBotLevels.Bracket("Thidranki", 252, 20, 24)));
        Assert.That(BattlegroundBotLevels.Default.Label, Is.EqualTo("Thidranki 20–24"));
    }

    [Test]
    public void TheBracketsAreInLevelOrderWithoutGapsOrOverlaps()
    {
        IReadOnlyList<BattlegroundBotLevels.Bracket> brackets = BattlegroundBotLevels.Period;
        Assert.That(brackets[0].MinLevel, Is.EqualTo(15));
        Assert.That(brackets[^1].MaxLevel, Is.EqualTo(35));
        for (int index = 1; index < brackets.Count; index++)
            Assert.That(brackets[index].MinLevel, Is.EqualTo(brackets[index - 1].MaxLevel + 1));
    }

    [Test]
    public void EveryRolledLevelIsInsideItsBracketAndBothEndsComeUp()
    {
        var random = new Random(1234);
        foreach (BattlegroundBotLevels.Bracket bracket in BattlegroundBotLevels.Period)
        {
            var seen = new HashSet<int>();
            for (int roll = 0; roll < 2000; roll++)
            {
                int level = BattlegroundBotLevels.RollLevel(bracket, random);
                Assert.That(level, Is.InRange(bracket.MinLevel, bracket.MaxLevel), bracket.Label);
                seen.Add(level);
            }
            Assert.That(seen, Is.EquivalentTo(Enumerable.Range(bracket.MinLevel, bracket.MaxLevel - bracket.MinLevel + 1)), bracket.Label);
        }
    }

    [Test]
    public void ABracketOfOneLevelAlwaysRollsThatLevel()
    {
        var single = BattlegroundBotLevels.Default with { MinLevel = 22, MaxLevel = 22 };
        Assert.That(BattlegroundBotLevels.RollLevel(single, new Random(7)), Is.EqualTo(22));
    }

    [Test]
    public void TheExperienceTableIsTheServersOwn()
    {
        // GamePlayer.XPForLevel: index n is the experience on reaching level n + 1, with a last entry for level 51.
        string source = ServerSource("gameobjects", "GamePlayer.cs");
        string table = Regex.Match(source, "long\\[\\] XPForLevel =\\s*\\{(.*?)\\};", RegexOptions.Singleline).Groups[1].Value;
        long[] server = Regex.Matches(Regex.Replace(table, "//[^\n]*", ""), "\\d+").Select(match => long.Parse(match.Value)).ToArray();
        Assert.That(server, Has.Length.EqualTo(51));
        for (int level = 1; level <= 50; level++)
            Assert.That(BattlegroundBotLevels.ExperienceForLevel(level), Is.EqualTo(server[level - 1]), $"level {level}");
    }

    [Test]
    public void ExperienceMatchesTheLevelsTheOtherButtonsMake()
    {
        Assert.That(BattlegroundBotLevels.ExperienceForLevel(1), Is.EqualTo(0));
        Assert.That(BattlegroundBotLevels.ExperienceForLevel(50), Is.EqualTo(169999999950L), "+ Lv.50");
        Assert.That(BattlegroundBotLevels.ExperienceForLevel(20), Is.EqualTo(25899950L));
        Assert.That(BattlegroundBotLevels.ExperienceForLevel(24), Is.EqualTo(106999950L));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattlegroundBotLevels.ExperienceForLevel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattlegroundBotLevels.ExperienceForLevel(51));
    }

    [Test]
    public void ExperienceRisesWithEveryLevel()
    {
        for (int level = 2; level <= 50; level++)
            Assert.That(BattlegroundBotLevels.ExperienceForLevel(level), Is.GreaterThan(BattlegroundBotLevels.ExperienceForLevel(level - 1)));
    }

    [TestCase(15, 22_500L)]
    [TestCase(20, 40_000L)]
    [TestCase(24, 57_600L)]
    [TestCase(35, 122_500L)]
    public void ThePurseIsTheLevelSquaredInSilver(int level, long copper)
    {
        Assert.That(BattlegroundBotLevels.PurseCopper(level), Is.EqualTo(copper));
        Assert.That(BattlegroundBotLevels.PurseCopper(level), Is.LessThan(100_000_000L), "far below a + Lv.50 bot's 10 platinum");
    }

    [Test]
    public void ABattlegroundTableRowReplacesThePeriodLevelsAsOnTheServer()
    {
        BattlegroundBotLevels.Bracket period = BattlegroundBotLevels.Default;
        Assert.That(BattlegroundBotLevels.WithDatabaseRow(period, null, null), Is.EqualTo(period));
        Assert.That(BattlegroundBotLevels.WithDatabaseRow(period, 21, 26), Is.EqualTo(period with { MinLevel = 21, MaxLevel = 26 }));
    }

    [TestCase(0, 24)]
    [TestCase(20, 51)]
    [TestCase(25, 20)]
    public void ABattlegroundTableRowNoBotCouldUseIsRefused(int min, int max)
    {
        Assert.Throws<InvalidOperationException>(() => BattlegroundBotLevels.WithDatabaseRow(BattlegroundBotLevels.Default, min, max));
    }

    [Test]
    public void TheButtonTipNamesTheBracketTheModifiersAndTheGoalSetting()
    {
        Assert.That(BattlegroundBotLevels.ButtonText, Is.EqualTo("+ Lv.20"));
        string tip = BattlegroundBotLevels.ButtonTip(BattlegroundBotLevels.Default);
        Assert.That(tip, Does.Contain("levels 20–24, the Thidranki battleground bracket"));
        Assert.That(tip, Does.Contain("Shift-click makes 10, Ctrl-click 100"));
        Assert.That(tip, Does.Contain("Battlegrounds %"));
        Assert.That(tip, Does.Not.Contain("no armour"), "they arrive with starter gear (owner 2026-11-15)");
        Assert.That(tip, Does.Contain("plain merchant armour and weapons for its level, free"));
    }
}
