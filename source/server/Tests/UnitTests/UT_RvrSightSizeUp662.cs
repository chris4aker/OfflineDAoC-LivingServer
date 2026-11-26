using System;
using System.IO;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using S = DOL.GS.RvrSightSizeUp;

namespace DOL.GS.Tests;

/// <summary>
/// #662 (owner decision 2026-11-26): a solo or duo autonomous gamebot sizes up a fight when enemies come into its perception
/// range, and opens or backs off. Boldness moves the line, a casual misread is mild, and no solo bot ever opens on three
/// or more enemies.
/// </summary>
[TestFixture]
public class UT_RvrSightSizeUp662
{
    private static RvrSightFacts Fight(int ownLevel, int allies, int enemies, int enemyLevel, int healers = 0, bool attacked = false) => new()
    {
        OwnLevel = ownLevel, Allies = allies, AllyAverageLevel = ownLevel, Enemies = enemies, EnemyHealers = healers,
        EnemyAverageLevel = enemyLevel, EnemyTopLevel = enemyLevel, Attacked = attacked,
    };

    private static readonly RvrSkillTier[] Tiers = [RvrSkillTier.Casual, RvrSkillTier.Average, RvrSkillTier.Veteran];
    private static readonly double[] Boldnesses = [0, 0.2, 0.4, 0.5, 0.8, 1.0];
    private static readonly double[] Rolls = [0, 0.01, 0.05, 0.19, 0.5, 0.99];

    [Test]
    public void OneSettingOnByDefault()
    {
        ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_RVR_SIGHT_SIZE_UP))
            .GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", S.PropertyKey, (object)true)));
    }

    [Test]
    public void ALoneLowBardNeverOpensOnAnEight()
    {
        // The report: a low-level Bard ran up to a full eight. Any tier, any boldness, any roll: it backs off.
        foreach (RvrSkillTier tier in Tiers)
            foreach (double boldness in Boldnesses)
                foreach (double roll in Rolls)
                    Assert.That(S.Decide(Fight(20, 1, 8, 24), boldness, tier, roll).Call, Is.EqualTo(RvrSightCall.BackOff),
                        $"{tier} boldness {boldness} roll {roll}");
    }

    [Test]
    public void NoSoloBotEverOpensOnThreeOrMore()
    {
        // Owner: "no solo bot ever opens on 3 or more enemies", even three far below it.
        foreach (RvrSkillTier tier in Tiers)
            foreach (double boldness in Boldnesses)
                foreach (double roll in Rolls)
                {
                    RvrSightDecision three = S.Decide(Fight(50, 1, 3, 30), boldness, tier, roll);
                    Assert.That(three.Call, Is.EqualTo(RvrSightCall.BackOff), $"{tier} boldness {boldness} roll {roll}");
                    Assert.That(three.Reason, Is.EqualTo("solo_outnumbered"));
                }
    }

    [Test]
    public void AFairOneOnOneOpensAndADuoTakesALoneEnemy()
    {
        Assert.Multiple(() =>
        {
            Assert.That(S.Decide(Fight(30, 1, 1, 30), 0.4, RvrSkillTier.Average, 0.99).Call, Is.EqualTo(RvrSightCall.Engage),
                "a middling bot takes a fair 1v1");
            Assert.That(S.Decide(Fight(30, 1, 1, 30), 0.5, RvrSkillTier.Average, 0.99).Call, Is.EqualTo(RvrSightCall.Engage),
                "and so does one with no personality");
            Assert.That(S.Decide(Fight(30, 2, 1, 32), 0.2, RvrSkillTier.Veteran, 0.99).Call, Is.EqualTo(RvrSightCall.Engage),
                "a careful duo takes a lone enemy a little above it");
        });
    }

    [Test]
    public void BoldnessMovesTheLine()
    {
        RvrSightFacts upHill = Fight(30, 1, 1, 33); // a 1v1 three levels up: odds about 0.79
        Assert.Multiple(() =>
        {
            Assert.That(S.LineFor(1.0), Is.LessThan(S.LineFor(0.4)), "bolder: a lower line");
            Assert.That(S.LineFor(0), Is.GreaterThan(S.LineFor(0.4)));
            Assert.That(S.LineFor(S.BoldnessAnchor), Is.EqualTo(S.EvenLine), "the middle takes even odds");
            Assert.That(S.Decide(upHill, 1.0, RvrSkillTier.Veteran, 0.99).Call, Is.EqualTo(RvrSightCall.Engage), "a Daredevil goes");
            Assert.That(S.Decide(upHill, 0.0, RvrSkillTier.Veteran, 0.99).Call, Is.EqualTo(RvrSightCall.BackOff), "a careful bot doesn't");
        });
    }

    [Test]
    public void AMisreadIsMildAndOnlyNearTheLine()
    {
        RvrSightFacts close = Fight(30, 1, 1, 31); // odds about 0.93, line 1.0: a close call
        RvrSightFacts far = Fight(30, 2, 5, 30);   // a duo against five: odds 0.4, far below
        Assert.Multiple(() =>
        {
            RvrSightDecision misread = S.Decide(close, 0.4, RvrSkillTier.Casual, 0.05);
            Assert.That((misread.Call, misread.Mistake), Is.EqualTo((RvrSightCall.Engage, true)), "a casual bot sometimes takes a close one");
            Assert.That(S.Decide(close, 0.4, RvrSkillTier.Casual, 0.5).Call, Is.EqualTo(RvrSightCall.BackOff), "but mostly reads it");
            Assert.That(S.Decide(close, 0.4, RvrSkillTier.Veteran, 0.05).Call, Is.EqualTo(RvrSightCall.BackOff), "a veteran misreads less");
            foreach (RvrSkillTier tier in Tiers)
                foreach (double roll in Rolls)
                    Assert.That(S.Decide(far, 0.4, tier, roll).Call, Is.EqualTo(RvrSightCall.BackOff), $"never far off: {tier} {roll}");
        });
    }

    [Test]
    public void AHealerCountsTwiceAndAToweringEnemyCountsForItsLevel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(S.Odds(Fight(30, 2, 1, 30, healers: 1)), Is.EqualTo(1.0).Within(1e-9), "two against a lone healer: even");
            Assert.That(S.Odds(Fight(30, 2, 1, 30)), Is.EqualTo(2.0).Within(1e-9));
            RvrSightFacts mixed = Fight(30, 2, 2, 30) with { EnemyAverageLevel = 30, EnemyTopLevel = 45 };
            Assert.That(S.Odds(mixed), Is.LessThan(S.Odds(Fight(30, 2, 2, 30))), "a high-level enemy lifts the group's weight");
        });
    }

    [Test]
    public void FightingBackIsAlwaysAllowed()
    {
        Assert.That(S.Decide(Fight(20, 1, 8, 24, attacked: true), 0, RvrSkillTier.Veteran, 0.99).Call, Is.EqualTo(RvrSightCall.Engage));
    }

    [Test]
    public void OnlySoloAndDuoBots()
    {
        Assert.Multiple(() =>
        {
            Assert.That(S.ForPartySize(1), Is.True);
            Assert.That(S.ForPartySize(2), Is.True);
            Assert.That(S.ForPartySize(3), Is.False, "a group of three or more keeps RvrEngagement");
        });
    }

    [Test]
    public void TheScanSizesUpAndTheBrainHoldsTheTurn()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string bots = Path.Combine(dir, "source", "server", "GameServer", "bots");
        string threat = File.ReadAllText(Path.Combine(bots, "autonomous", "AutonomousWorldBotController.FrontierThreat.cs")).Replace("\r\n", "\n");
        string sight = File.ReadAllText(Path.Combine(bots, "autonomous", "AutonomousWorldBotController.SightSizeUp.cs")).Replace("\r\n", "\n");
        string brain = File.ReadAllText(Path.Combine(bots, "BotBrain.cs")).Replace("\r\n", "\n");
        Assert.Multiple(() =>
        {
            int perceived = threat.IndexOf("visibleTargets = PerceiveRvrTargets(bot, visibleTargets, rvr);", StringComparison.Ordinal);
            int gate = threat.IndexOf("visibleTargets = SightSizeUpGate(bot, visibleTargets, scanNow);", StringComparison.Ordinal);
            Assert.That(perceived, Is.GreaterThanOrEqualTo(0));
            Assert.That(gate, Is.GreaterThan(perceived), "only noticed enemies: after perception");
            Assert.That(threat, Does.Contain("if (!defending && !siegeFighter && !crew)"), "committed defenders and siege crews keep their jobs");
            Assert.That(brain, Does.Contain("if (_autonomousWorldController.HoldsForSightSizeUp(this))"));
            Assert.That(sight, Does.Contain("!bot.IsHiredCompanion && !bot.IsPlayerLedGroup"), "never a companion");
            Assert.That(sight, Does.Contain("bot.CurrentRegionID is 1 or 100 or 200 || BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID)"),
                "the frontier and the battlegrounds only");
            Assert.That(sight, Does.Contain("RVR_SIGHT_SIZE_UP"), "a log row for each decision");
        });
    }
}
