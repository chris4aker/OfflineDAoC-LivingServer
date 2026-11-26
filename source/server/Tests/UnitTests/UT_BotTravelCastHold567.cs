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
    public void AHeldUpkeepLooksAgainWithinASecond()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotTravelCastHold.NextScanAfter(true, 8_000), Is.EqualTo(1_000), "held: within a second");
            Assert.That(BotTravelCastHold.NextScanAfter(true, 500), Is.EqualTo(500), "never later than it would be");
            Assert.That(BotTravelCastHold.NextScanAfter(false, 8_000), Is.EqualTo(8_000), "not held: as before");
            Assert.That(BotTravelCastHold.HeldRecheckMilliseconds, Is.LessThan(BotTravelCastHold.SettleMilliseconds),
                "the walk is sampled inside the settle");
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
        string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")).Replace("\r\n", "\n");
        int upkeep = brain.IndexOf("private bool TryMaintainTravelAndClassBuffs()", StringComparison.Ordinal);
        Assert.That(upkeep, Is.GreaterThanOrEqualTo(0), "the upkeep method");
        int end = brain.IndexOf("private int _pendingMaintenancePetBuffId;", upkeep, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(upkeep), "the end of the upkeep method");
        string body = brain.Substring(upkeep, end - upkeep);
        Assert.That(body, Does.Contain("bool partyTravelHold = HoldsUpkeepForGroupTravel();"));
        Assert.That(body, Does.Contain("(!partyTravelHold || !CompanionFollowPolicy.IsOrdinaryBuff(bot, spell))"),
            "only the buffs the formation follow refuses anyway (no heals, songs or travel speed)");
        Assert.That(body, Does.Contain("!waitingForLeader && !partyTravelHold && ApproachSupportTarget(outOfRangeBuffTarget)"));
        Assert.That(body, Does.Contain("_nextMaintenanceBuffTick = GameLoop.GameLoopTime + BotTravelCastHold.HeldRecheckMilliseconds;"),
            "a held pass with nothing left waits a second, not one think");
        Assert.That(body, Does.Contain("BotTravelCastHold.NextScanAfter(partyTravelHold, 8_000)"), "a held complete scan rechecks within a second");
    }
}
