using System;
using System.IO;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>#567 A1 (port of True165 03dce76ee): an autonomous party casts no ordinary buffs until it has stopped.</summary>
[TestFixture]
public class UT_BotTravelCastHold567
{
    [Test]
    public void AWalkingAutonomousPartyHoldsItsBuffs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotTravelCastHold.Holds(true, false, true, false, 0), Is.True, "walking");
            Assert.That(BotTravelCastHold.Holds(true, false, false, false, 2_000), Is.True, "a pause between path legs");
            Assert.That(BotTravelCastHold.Holds(true, false, false, false, BotTravelCastHold.SettleMilliseconds - 1), Is.True);
            Assert.That(BotTravelCastHold.Holds(true, false, false, false, BotTravelCastHold.SettleMilliseconds), Is.False, "settled");
            Assert.That(BotTravelCastHold.Holds(true, false, false, false, -1), Is.False, "never seen walking");
        });
    }

    [Test]
    public void FightsRestsAndOtherPartiesAreNeverHeld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotTravelCastHold.Holds(true, true, true, false, 0), Is.False, "anyone in the party fighting");
            Assert.That(BotTravelCastHold.Holds(true, false, false, true, 1_000), Is.False, "sitting or resting: a known stop");
            Assert.That(BotTravelCastHold.Holds(true, false, true, true, 0), Is.True, "but not while still walking");
            Assert.That(BotTravelCastHold.Holds(false, false, true, false, 0), Is.False, "a player-led party or a solo bot");
        });
    }

    [Test]
    public void TheUpkeepHoldsOnlyOrdinaryBuffsAndTheWalkToAMember()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs"));
        int upkeep = brain.IndexOf("private bool TryMaintainTravelAndClassBuffs()", StringComparison.Ordinal);
        string body = brain.Substring(upkeep, brain.IndexOf("CompleteCompanionPerformerBuffBatch(bot);\n            return false;\n        }",
            upkeep, StringComparison.Ordinal) - upkeep);
        Assert.That(body, Does.Contain("bool partyTravelHold = HoldsUpkeepForGroupTravel();"));
        Assert.That(body, Does.Contain("(!partyTravelHold || !CompanionFollowPolicy.IsOrdinaryBuff(bot, spell))"),
            "only the buffs the formation follow refuses anyway (no heals, songs or travel speed)");
        Assert.That(body, Does.Contain("!waitingForLeader && !partyTravelHold && ApproachSupportTarget(outOfRangeBuffTarget)"));
    }
}
