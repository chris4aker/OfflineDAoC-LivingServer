using System.Collections.Generic;
using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #572 (port of zomboden/OfflineDAoC-True165 16284ba, UT_RvrHealerSupportCache): an autonomous gamebot healer on an RvR
/// objective holds its support target 2 s and ranks the candidates before any line-of-sight raycast. The choice is the
/// one the old query made: a relic carrier, then a keep lord, then other guards, then the lowest health, first one in sight.
/// </summary>
[TestFixture]
public class UT_RvrHealerSupportCache
{
    private sealed class Candidate
    {
        public string Name;
        public bool Visible = true;
        public override string ToString() => Name;
    }

    [Test]
    public void RankPutsTheRelicCarrierFirstThenTheLord() => Assert.Multiple(() =>
    {
        Assert.That(BotBrain.RvrSupportRank(true, false), Is.EqualTo(0));
        Assert.That(BotBrain.RvrSupportRank(true, true), Is.EqualTo(0), "a lord carrying a relic is a carrier first");
        Assert.That(BotBrain.RvrSupportRank(false, true), Is.EqualTo(1));
        Assert.That(BotBrain.RvrSupportRank(false, false), Is.EqualTo(2));
    });

    [Test]
    public void AHeldTargetIsKeptOnlyWhileItsHoldLastsAndItStillNeedsHealing() => Assert.Multiple(() =>
    {
        Assert.That(BotBrain.KeepsRvrSupport(1_000, 2_000, true, true, 60), Is.True);
        Assert.That(BotBrain.KeepsRvrSupport(2_000, 2_000, true, true, 60), Is.False, "the hold ran out");
        Assert.That(BotBrain.KeepsRvrSupport(1_000, 2_000, false, true, 60), Is.False, "dead, or another realm or region");
        Assert.That(BotBrain.KeepsRvrSupport(1_000, 2_000, true, false, 60), Is.False, "out of heal range");
        Assert.That(BotBrain.KeepsRvrSupport(1_000, 2_000, true, true, BotBrain.RvrSupportBelowPercent), Is.False,
            "healed back to 85%");
        Assert.That(BotBrain.RvrSupportHoldMilliseconds, Is.EqualTo(2_000));
    });

    [Test]
    public void NobodyIsHeldTooUntilTheHoldRunsOut() => Assert.Multiple(() =>
    {
        Assert.That(BotBrain.RvrSupportHoldOver(1_999, 2_000), Is.False);
        Assert.That(BotBrain.RvrSupportHoldOver(2_000, 2_000), Is.True);
        Assert.That(BotBrain.RvrSupportHoldOver(0, long.MinValue), Is.True, "a healer that never looked looks now");
    });

    [Test]
    public void ThePickIsTheOldQuerysChoice()
    {
        Candidate guard = new() { Name = "guard 20%" }, lord = new() { Name = "lord 70%" }, carrier = new() { Name = "carrier 80%" };
        var candidates = new List<(Candidate, int, int)>
        {
            (guard, BotBrain.RvrSupportRank(false, false), 20),
            (lord, BotBrain.RvrSupportRank(false, true), 70),
            (carrier, BotBrain.RvrSupportRank(true, false), 80),
        };
        Assert.That(BotBrain.PickRvrSupport(candidates, c => c.Visible), Is.SameAs(carrier));
    }

    [Test]
    public void LowestHealthWinsWithinARank()
    {
        Candidate a = new() { Name = "guard 70%" }, b = new() { Name = "guard 30%" }, c = new() { Name = "guard 50%" };
        var candidates = new List<(Candidate, int, int)> { (a, 2, 70), (b, 2, 30), (c, 2, 50) };
        Assert.That(BotBrain.PickRvrSupport(candidates, x => x.Visible), Is.SameAs(b));
    }

    [Test]
    public void OnlyCandidatesUpToTheFirstInSightAreRaycast()
    {
        Candidate hidden = new() { Name = "lord behind a wall", Visible = false }, seen = new() { Name = "guard in sight" },
            later = new() { Name = "guard far down the list" };
        var candidates = new List<(Candidate, int, int)> { (later, 2, 80), (seen, 2, 40), (hidden, 1, 50) };
        var raycast = new List<Candidate>();
        Candidate chosen = BotBrain.PickRvrSupport(candidates, x => { raycast.Add(x); return x.Visible; });
        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.SameAs(seen), "the next one is tried when the best has no line of sight");
            Assert.That(raycast, Is.EqualTo(new[] { hidden, seen }), "two raycasts, not one per candidate");
        });
    }

    [Test]
    public void NobodyInSightIsNobody()
    {
        var candidates = new List<(Candidate, int, int)> { (new Candidate { Name = "x", Visible = false }, 2, 10) };
        Assert.That(BotBrain.PickRvrSupport(candidates, x => x.Visible), Is.Null);
        Assert.That(BotBrain.PickRvrSupport(new List<(Candidate, int, int)>(), x => x.Visible), Is.Null);
    }
}
