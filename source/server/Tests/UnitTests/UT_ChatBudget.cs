using System;
using System.Linq;
using NUnit.Framework;
using Budget = DOL.GS.AutonomousChatBudget;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2: the two chat budgets (chat-budget.json, AutonomousChatBudget, AutonomousChatReplyBudget).</summary>
[TestFixture]
public class UT_ChatBudget
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => (int)(value * maxValue);
        public override int Next(int minValue, int maxValue) => minValue + (int)(value * (maxValue - minValue));
    }

    [Test]
    public void TheBuiltInBudgetReadsWithItsComments()
    {
        Budget.Settings settings = Budget.Parse(Budget.BuiltIn());
        Assert.That(settings.DesignBots, Is.EqualTo(1000));
        foreach (Budget.Channel channel in Enum.GetValues<Budget.Channel>().Where(c => c != Budget.Channel.Count))
        {
            Budget.ChannelBand band = settings.Band(channel);
            Assert.That(band.Target, Is.GreaterThan(0), channel + " has a target");
            Assert.That(band.Floor, Is.LessThanOrEqualTo(band.Target), channel.ToString());
            Assert.That(band.Ceiling, Is.GreaterThanOrEqualTo(band.Target), channel.ToString());
        }
        Assert.That(settings.Hours.Length, Is.EqualTo(24));
        Assert.That(settings.Hours.Max(), Is.EqualTo(1.0), "prime time is 1");
        Assert.That(settings.Hours[4], Is.LessThan(settings.Hours[21]), "night is quieter than the evening");
        Assert.That(settings.MinReplyFit, Is.EqualTo(AutonomousChatReplyFit.Fits));
    }

    [Test]
    public void ABrokenOrPartialFileStillGivesSafeValues()
    {
        Budget.Settings settings = Budget.Parse("{ /* nothing */ \"channels\": { \"faction\": { \"target\": 2, \"floor\": 5 } }, }");
        Budget.ChannelBand faction = settings.Band(Budget.Channel.Faction);
        Assert.That(faction.Target, Is.GreaterThanOrEqualTo(faction.Floor), "a target under the floor is raised to it");
        Assert.That(faction.Ceiling, Is.GreaterThanOrEqualTo(faction.Target));
        Assert.That(settings.Band(Budget.Channel.Guild).Target, Is.EqualTo(0), "a channel left out is silent, not an error");
        Assert.That(settings.Hours.All(h => h == 1.0));
    }

    [Test]
    public void VolumeRisesWithBotsWithoutACapAndWithTheHour()
    {
        Budget.Settings settings = Budget.Current;
        double At(int bots, int hour = 21) => Budget.Band(settings, Budget.Channel.Faction, bots, hour, false, 1.0).Target;
        Assert.That(At(390), Is.LessThan(At(1_000)));
        Assert.That(At(1_000), Is.LessThan(At(3_000)));
        Assert.That(At(3_000), Is.LessThan(At(10_000)));
        Assert.That(At(1_000) / At(300), Is.LessThan(1_000 / 300.0), "sublinear");
        Assert.That(At(1_000, 4), Is.LessThan(At(1_000, 21)));
        Assert.That(Budget.Band(settings, Budget.Channel.Faction, 1_000, 21, false, 0).Target, Is.EqualTo(0), "bot_voices_chat_rate 0");
        Assert.That(Budget.Band(settings, Budget.Channel.Faction, 1_000, 4, true, 1.0).Target, Is.GreaterThan(At(1_000, 4)),
            "with the clock schedule the night's thinner crowd already lowers it");
        // A guild grows with its members online, sublinearly (2003: 2.5 a minute at 50 online, 6 at 150).
        double Guild(int online) => Budget.Band(settings, Budget.Channel.Guild, online, 21, false, 1.0).Target;
        Assert.That(Guild(150) / Guild(50), Is.InRange(2.0, 3.0));
        Assert.That(Guild(5), Is.LessThan(Guild(50)));
    }

    [Test]
    public void TheGateHoldsAboveTheCeilingSpeaksUnderTheFloorAndOtherwiseWaitsItsGap()
    {
        var band = (Floor: 1.0, Target: 2.5, Ceiling: 6.0);
        Assert.That(Budget.Decide(6.0, band, 10_000, 0), Is.EqualTo(Budget.Verdict.HoldCeiling));
        Assert.That(Budget.Decide(0.5, band, 10_000, 99_000), Is.EqualTo(Budget.Verdict.SpeakUnderFloor));
        Assert.That(Budget.Decide(2.0, band, 10_000, 99_000), Is.EqualTo(Budget.Verdict.HoldGap));
        Assert.That(Budget.Decide(2.0, band, 99_000, 99_000), Is.EqualTo(Budget.Verdict.Speak));
        Assert.That(Budget.Decide(0, (0, 0, 0), 99_000, 0), Is.EqualTo(Budget.Verdict.HoldGap), "a channel with no target never starts");
    }

    [Test]
    public void GapsVaryAroundTheTargetAndNeverFlood()
    {
        // Round 5: bursty. Most gaps short, some ordinary, one in ten a lull; about the target on average.
        Assert.That(Budget.GapMs(2.0, new FixedRandom(0.0)), Is.EqualTo(7_500), "a short gap: a quarter of the mean");
        Assert.That(Budget.GapMs(2.0, new FixedRandom(0.9999)), Is.EqualTo(135_000).Within(10), "a lull: up to 4.5 times the mean");
        var random = new Random(3);
        long[] gaps = Enumerable.Range(0, 20_000).Select(_ => Budget.GapMs(2.0, random)).ToArray();
        Assert.That(gaps.Average(), Is.InRange(0.85 * 30_000, 1.0 * 30_000), "the per-minute target holds on average");
        Assert.That(gaps.Count(gap => gap <= 18_000) / (double)gaps.Length, Is.GreaterThan(0.55), "bursts");
        Assert.That(gaps.Count(gap => gap >= 75_000) / (double)gaps.Length, Is.InRange(0.08, 0.12), "lulls");
        Assert.That(Budget.GapMs(1_000, new FixedRandom(0.0)), Is.EqualTo(4_000), "never under 4 s");
        Assert.That(Budget.GapMs(0, new FixedRandom(0.5)), Is.GreaterThan(TimeSpan.FromDays(365).TotalMilliseconds));
    }

    [Test]
    public void TheMeterCountsTheLastTwoMinutesAndPacesItsChannel()
    {
        var meter = new Budget.ChannelMeter();
        for (int i = 0; i < 6; i++)
            meter.Count(Budget.Channel.Faction, 1, 10_000 + i * 1_000);
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 1, 20_000), Is.EqualTo(3.0));
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 2, 20_000), Is.EqualTo(0), "another realm's channel");
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 1, 10_000 + Budget.ChannelMeter.WindowMs + 6_000), Is.EqualTo(0), "old lines drop out");
        var band = (Floor: 0.5, Target: 2.0, Ceiling: 5.0);
        var random = new FixedRandom(0.5);
        Assert.That(meter.TryStart(Budget.Channel.Faction, 1, 20_000, band, random), Is.EqualTo(Budget.Verdict.Speak));
        Assert.That(meter.TryStart(Budget.Channel.Faction, 1, 21_000, band, random), Is.EqualTo(Budget.Verdict.HoldGap), "the gap is booked");
        Assert.That(meter.TryStart(Budget.Channel.Faction, 1, 20_000 + 30_000, band, random), Is.EqualTo(Budget.Verdict.Speak));
        meter.Delay(Budget.Channel.Say, 7, 90_000);
        Assert.That(meter.TryStart(Budget.Channel.Say, 7, 60_000, (0, 1, 3), random), Is.EqualTo(Budget.Verdict.HoldGap));
    }

    [Test]
    public void PressureRisesOnlyWhileAChannelRunsUnderTarget()
    {
        Assert.That(Budget.Pressure(3, 2.5), Is.EqualTo(1.0));
        Assert.That(Budget.Pressure(1.25, 2.5), Is.EqualTo(2.0));
        Assert.That(Budget.Pressure(0, 2.5), Is.EqualTo(4.0), "at most four times");
        Assert.That(Budget.ChoiceWeight(2.5, 0), Is.EqualTo(1.0));
        Assert.That(Budget.ChoiceWeight(2.5, 5), Is.EqualTo(0.05), "a full channel can still be picked, rarely");
        // A pressed bot rests 2 minutes between lines instead of 4, and tries more often.
        var now = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddMinutes(-3), false, new FixedRandom(0.0)), Is.False);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddMinutes(-3), false, new FixedRandom(0.0), 1.0, 2.0), Is.True);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddMinutes(-5), false, new FixedRandom(0.03), 1.0, 1.0), Is.False);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddMinutes(-5), false, new FixedRandom(0.03), 1.0, 4.0), Is.True);
    }

    [Test]
    public void TypingSpeedsSpreadOverTheRangeAndStayFixedPerBot()
    {
        Budget.Settings settings = Budget.Current;
        int[] speeds = Enumerable.Range(1, 500).Select(id => Budget.TypingWpm(settings, id, BotPersonalityType.Leveler)).ToArray();
        Assert.That(speeds.Min(), Is.GreaterThanOrEqualTo(settings.WpmMin));
        Assert.That(speeds.Max(), Is.LessThanOrEqualTo(settings.WpmMax));
        Assert.That(speeds.Distinct().Count(), Is.GreaterThan(15));
        Assert.That(Budget.TypingWpm(settings, 42, BotPersonalityType.Leveler), Is.EqualTo(Budget.TypingWpm(settings, 42, BotPersonalityType.Leveler)));
    }

    [Test]
    public void AWeakReplyIsSilenceUnlessTheBotWasAddressed()
    {
        Assert.That(AutonomousChatReplyBudget.Decide(AutonomousChatReplyFit.Fits, false, 2), Is.EqualTo(AutonomousChatReplyBudget.Outcome.Answer));
        Assert.That(AutonomousChatReplyBudget.Decide(AutonomousChatReplyFit.Weak, false, 2), Is.EqualTo(AutonomousChatReplyBudget.Outcome.Silent));
        Assert.That(AutonomousChatReplyBudget.Decide(AutonomousChatReplyFit.Weak, true, 2), Is.EqualTo(AutonomousChatReplyBudget.Outcome.Ack));
        Assert.That(AutonomousChatReplyBudget.Decide(AutonomousChatReplyFit.None, true, 2), Is.EqualTo(AutonomousChatReplyBudget.Outcome.Ack));
        var random = new Random(3);
        string[] acks = Enumerable.Range(0, 200).Select(i => AutonomousChatReplyBudget.Ack(i, false, random)).ToArray();
        Assert.That(acks.All(a => a.Length <= 9 && a == a.ToLowerInvariant()), "short and casual");
        Assert.That(acks.Distinct().Count(), Is.GreaterThan(4));
        Assert.That(AutonomousChatReplyBudget.Ack(1, true, random), Does.Contain("sec").Or.EqualTo("brt"), "a busy bot says sec");
    }

    [TestCase("ask around maybe", true)]
    [TestCase("beats me :D", true)]
    [TestCase("i think so?", true)]
    [TestCase("good question", true)]
    [TestCase("someone said something about that earlier", true)]
    [TestCase("spindel is in the vale of mularn", false)]
    [TestCase("not sure, never been past the gate", false)]
    [TestCase("gz!", false)]
    public void StockBrushOffsAreKnown(string text, bool brushOff) => Assert.That(AutonomousChatReplyBudget.IsBrushOff(text), Is.EqualTo(brushOff));

    [Test]
    public void NearCopiesShareASkeletonAndAChannelWontRepeatOne()
    {
        Assert.That(AutonomousChatFiller.Skeleton("Ha yes, this exactly."), Is.EqualTo(AutonomousChatFiller.Skeleton("Heh yep, this exactly")));
        Assert.That(AutonomousChatFiller.Skeleton("lfg 24 ranger"), Is.EqualTo(AutonomousChatFiller.Skeleton("LFG 31 ranger!")));
        Assert.That(AutonomousChatFiller.Skeleton("lol"), Is.EqualTo("lol"), "a reaction keeps its words");
        var recent = new AutonomousChatFiller.RecentTexts();
        Assert.That(recent.TryAdd(1, "anyone know a good camp near prydwen?", 0), Is.True);
        Assert.That(recent.TryAdd(1, "Anyone know a good camp near Prydwen", 60_000), Is.False);
        Assert.That(recent.TryAdd(2, "anyone know a good camp near prydwen?", 60_000), Is.True, "another channel");
        Assert.That(recent.TryAdd(1, "anyone know a good camp near prydwen?", AutonomousChatFiller.RecentTexts.KeepMs + 1), Is.True, "after half an hour");
        Assert.That(recent.TryAdd(1, "lol", 0) && recent.TryAdd(1, "lol", 1), Is.True, "short reactions repeat");
    }
}
