using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2, world-driven bursts (AutonomousChatBursts): their size, timing and which events start them.</summary>
[TestFixture]
public class UT_ChatBursts
{
    [Test]
    public void ABurstIsAFewLinesFrontLoadedOverAboutNinetySeconds()
    {
        var random = new Random(3);
        for (int round = 0; round < 200; round++)
        {
            int[] delays = AutonomousChatBursts.Delays(6, 90_000, random);
            Assert.That(delays.Length, Is.EqualTo(6));
            Assert.That(delays[0], Is.InRange(2_000, 60_000));
            Assert.That(delays.Last(), Is.LessThanOrEqualTo(90_000 + 6 * 1_200));
            for (int i = 1; i < delays.Length; i++)
                Assert.That(delays[i] - delays[i - 1], Is.GreaterThanOrEqualTo(1_200), "never two in the same second");
        }
        int early = Enumerable.Range(0, 500).SelectMany(_ => AutonomousChatBursts.Delays(5, 90_000, random)).Count(d => d < 45_000);
        Assert.That(early / 2_500.0, Is.GreaterThan(0.6), "most lines come in the first half");
    }

    [Test]
    public void ABurstGrowsWithTheCrowdWithinTheBudget()
    {
        AutonomousChatBudget.Settings settings = AutonomousChatBudget.Current;
        double Mean(int bots) => Enumerable.Range(0, 400).Average(i => AutonomousChatBursts.Size(settings, bots, new Random(i)));
        Assert.That(Mean(1_000), Is.InRange(settings.BurstMin, settings.BurstMax));
        Assert.That(Mean(100), Is.LessThan(Mean(1_000)));
        Assert.That(Enumerable.Range(0, 400).Max(i => AutonomousChatBursts.Size(settings, 10_000, new Random(i))), Is.LessThanOrEqualTo(settings.BurstMax + 2));
        Assert.That(Enumerable.Range(0, 400).Min(i => AutonomousChatBursts.Size(settings, 5, new Random(i))), Is.GreaterThanOrEqualTo(1));
    }

    [TestCase("keep_lost", true, true)]
    [TestCase("relic_won", true, true)]
    [TestCase("raid_wiped", true, false)]
    [TestCase("keep_won", true, false)]
    [TestCase("siege_defend", false, false)]
    [TestCase("enemy_seen", false, false)]
    [TestCase("raid_forming", false, false)]
    public void BigMomentsBurstAndTheRealmWideOnesReachTheCapital(string topic, bool bursts, bool towns)
    {
        Assert.That(AutonomousChatBursts.BurstsOn(topic), Is.EqualTo(bursts));
        Assert.That(AutonomousChatBursts.ReachesTowns(topic), Is.EqualTo(towns));
    }
}
