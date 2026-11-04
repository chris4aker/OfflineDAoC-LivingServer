using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Regulars = DOL.GS.AutonomousChatRegulars;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, the fifth pass after the stream test: regulars who carry the channel, ideas that rest, no canned agreement
/// after a musing, answers that fit the line before, and bursty pacing. Written from the judges' categories.
/// </summary>
[TestFixture]
public class UT_ChatRegulars
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    // ---- 1. regulars ----------------------------------------------------------------------------------------------------------

    [Test]
    public void ARealmHasAboutAPercentOfItsBotsAsRegularsMostlySocialites()
    {
        int Count(int bots, long now, Func<long, BotPersonalityType> type) =>
            Enumerable.Range(1, bots).Count(id => Regulars.IsRegular(id, type(id), now, bots));
        // Averaged over ten evenings: about 12 at 1,000 bots (8-15 at a busy peak), about 5 at 300.
        double Mean(int bots) => Enumerable.Range(0, 10).Average(night => Count(bots, night * 24 * 60 * 60_000L, id => (BotPersonalityType)(id % 6)));
        Assert.That(Mean(1_000), Is.InRange(8.0, 18.0), "8-15 at a busy peak");
        Assert.That(Mean(300), Is.InRange(3.0, 8.0));
        int socialites = Enumerable.Range(1, 6_000).Count(id => id % 6 == (int)BotPersonalityType.Socialite &&
            Regulars.IsRegular(id, BotPersonalityType.Socialite, 0, 6_000));
        int levelers = Enumerable.Range(1, 6_000).Count(id => id % 6 == (int)BotPersonalityType.Leveler &&
            Regulars.IsRegular(id, BotPersonalityType.Leveler, 0, 6_000));
        Assert.That(socialites, Is.GreaterThan(levelers * 2), "Socialites three times as likely");
    }

    [Test]
    public void TheSetRotatesOverAnEveningButHoldsForAnHour()
    {
        var first = Enumerable.Range(1, 1_000).Where(id => Regulars.IsRegular(id, (BotPersonalityType)(id % 6), 10 * 60_000, 1_000)).ToHashSet();
        var hourLater = Enumerable.Range(1, 1_000).Where(id => Regulars.IsRegular(id, (BotPersonalityType)(id % 6), 70 * 60_000, 1_000)).ToHashSet();
        var nextNight = Enumerable.Range(1, 1_000).Where(id => Regulars.IsRegular(id, (BotPersonalityType)(id % 6), 24 * 60 * 60_000L, 1_000)).ToHashSet();
        Assert.That(first.Intersect(hourLater).Count(), Is.GreaterThanOrEqualTo(first.Count / 2), "most of an hour's regulars are still there");
        Assert.That(first.SetEquals(nextNight), Is.False, "a different set another night");
    }

    [Test]
    public void ARegularRestsBrieflyAndTalksOftenEveryoneElseRarely()
    {
        var now = new DateTime(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddSeconds(-50), false, new FixedRandom(0.3), 1.0, 1.0, regular: true), Is.True);
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddSeconds(-50), false, new FixedRandom(0.0), 1.0, 1.0, regular: false), Is.False,
            "others rest 2-4 minutes");
        Assert.That(AutonomousBotChat.ShouldSpeak(now, now.AddMinutes(-5), false, new FixedRandom(0.3), 1.0, 1.0, regular: false), Is.False,
            "and talk about 1% of the time");
    }

    [Test]
    public void ALineIsFollowedUpByItsSpeaker()
    {
        var said = new Field.Said("anyone up for keltoi?", "ask_group", ["me"]) { Unanswered = ["still lf keltoi", "nm"], Thanks = ["ty, inv"] };
        var memory = new Field.Memory();
        Assert.That(Field.FollowUp(said, true, new Random(1), memory, 0), Is.EqualTo("ty, inv"));
        Assert.That(Field.FollowUp(said, false, new Random(1), memory, 0), Is.AnyOf("still lf keltoi", "nm"));
        // The same follow-up isn't heard again in the realm for a while.
        Assert.That(Field.FollowUp(said, true, new Random(1), memory, 60_000), Is.Null);
    }

    [Test]
    public void AFollowUpNamesItsSubjectNotItsKey()
    {
        var random = new Random(3);
        for (int i = 0; i < 400; i++)
            if (Field.Line("faction", Facts(i, 10 + i % 40), null, random) is { Unanswered: { } pool })
                Assert.That(pool.Where(line => line.Contains(':')), Is.Empty, string.Join(" | ", pool));
    }

    // ---- 2. ideas rest --------------------------------------------------------------------------------------------------------

    [Test]
    public void ReworkdingsOfOneWrittenIdeaShareAKey()
    {
        string a = AutonomousChatFiller.IdeaOf("loot", "still look at every drop twice, you never know");
        string b = AutonomousChatFiller.IdeaOf("loot", "always check each drop twice before you vendor it");
        Assert.That(a, Is.EqualTo(b));
        Assert.That(AutonomousChatFiller.IdeaOf("camp", "this camp is full again"), Is.Not.EqualTo(a));
    }

    [Test]
    public void FamiliesRestMinutesAndIdeasHalfAnHourOrMore()
    {
        Assert.That(Field.Memory.FamilyRestMs("camp_free"), Is.InRange(3 * 60_000, 6 * 60_000));
        Assert.That(Field.Memory.IdeaRestMs("camp:large skeleton"), Is.InRange(30 * 60_000, 60 * 60_000));
        var memory = new Field.Memory();
        memory.Use("camp_free", "camp:large skeleton", 0);
        Assert.That(memory.Rested("camp_free", null, 2 * 60_000), Is.True);
        Assert.That(memory.Rested("camp_free", null, 7 * 60_000), Is.False);
        Assert.That(memory.Rested(null, "camp:large skeleton", 20 * 60_000), Is.True);
        Assert.That(memory.Rested(null, "camp:large skeleton", 61 * 60_000), Is.False);
    }

    private static readonly Field.WorldView Frontier = new(["Caer Benowyc"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static Field.Facts Facts(long id, int level, bool camp = true) =>
        new(id, eRealm.Albion, "Cleric", level, "Lyonesse", "Keltoi Fogou", false, false, false, false, 1, false, false, false, camp, "large skeleton",
            "Ghostly Cloak", 95, true, "Camelot", 3, 20, true, Frontier);

    [Test]
    public void ARealmDoesNotHearOneIdeaReworded()
    {
        var memory = new Field.Memory();
        var random = new Random(7);
        var heard = new List<(long At, Field.Said Said)>();
        for (int i = 0; i < 600; i++)
        {
            long now = i * 6_000L;
            if (Field.Line("faction", Facts(1_000 + i, 20 + i % 3), memory, random, now) is { } said)
                heard.Add((now, said));
        }
        foreach (var group in heard.Where(h => h.Said.Idea != null).GroupBy(h => h.Said.Idea))
        {
            long[] times = group.Select(h => h.At).ToArray();
            for (int i = 1; i < times.Length; i++)
                Assert.That(times[i] - times[i - 1], Is.GreaterThanOrEqualTo(30 * 60_000), group.Key);
        }
        foreach (var group in heard.Where(h => h.Said.Family != null).GroupBy(h => h.Said.Family))
        {
            long[] times = group.Select(h => h.At).ToArray();
            for (int i = 1; i < times.Length; i++)
                Assert.That(times[i] - times[i - 1], Is.GreaterThanOrEqualTo(3 * 60_000), group.Key);
        }
    }

    // ---- 3. musings and canned agreement --------------------------------------------------------------------------------------

    [Test]
    public void AWrittenLineAtMostOnceInTwentyMinutes()
    {
        var memory = new Field.Memory();
        Assert.That(memory.WrittenAllowed(0), Is.True);
        memory.NoteWritten(0);
        Assert.That(memory.WrittenAllowed(19 * 60_000), Is.False);
        Assert.That(memory.WrittenAllowed(20 * 60_000), Is.True);
    }

    [Test]
    public void LoreIsNeverRecitedUnprompted()
    {
        var lore = new VC.Line("l", "ambient", "lore", [], "*", [], 1, 50, [], "", "chat", 1, "merlin sleeps under the stones, or so they say");
        Assert.That(AutonomousChatFiller.AmbientFilter(true, new FixedRandom(0.0))(lore), Is.False);
    }

    [TestCase("i know right")]
    [TestCase("yeah its always like that")]
    [TestCase("yep that is how it goes")]
    [TestCase("story of my life")]
    public void CannedAgreementIsNeverSaid(string text) => Assert.That(AutonomousChatFiller.IsGeneric(text), Is.True, text);

    [Test]
    public void AMusingDrawsAFittingReactionOrNothing()
    {
        var musing = new VC.Line("m", "ambient", "general", [], "*", [], 1, 50, [], "", "chat", 1, "some nights the mobs carry nothing but bones");
        var joke = musing with { Invites = "joke", Text = "my pet has better gear than me" };
        var random = new Random(5);
        string[] reactions = Enumerable.Range(0, 400).Select(_ => AutonomousBotChatCoordinator.WrittenReaction(musing, random)).ToArray();
        Assert.That(reactions.Count(r => r == null), Is.GreaterThan(240), "most of the time nothing");
        Assert.That(reactions.Where(r => r != null), Is.All.AnyOf("true", "heh", "lol", "ha", "lol true", "true true"));
        Assert.That(Enumerable.Range(0, 200).Select(_ => AutonomousBotChatCoordinator.WrittenReaction(joke, random)).Where(r => r != null),
            Is.All.AnyOf("lol", "haha", "heh", "lol!"));
        Assert.That(AutonomousBotChatCoordinator.WrittenReaction(musing with { Topic = "lore" }, new FixedRandom(0.0)), Is.Null);
    }

    // ---- 4. answers fit the line before ---------------------------------------------------------------------------------------

    private static readonly Regex Recruits = new(
        @"\b(anyone else|lf |need |grp for|anyone$|later\??$|anyone around|anyone up for|up for|any1 lf grp|lf duo|wanna duo|wanna xp|duo|grp\??$|grp anyone|forming|if anyone wants|want to group|lf guildies|need 1 more|lf grp|lfg|lf\d?m|anyone grouping|who's on for|who's up|who's around|anyone want|keep take|take .* back|lets take|who wants|free for guild|up for grabs|spare|free to|selling my old|any .*s on\??|any1 at|anyone at|anyone out|anyone in|who's on|anyone heading|anyone on|hit a keep|lf rvr|need a |lf (heal|tank|mezzer|bard|cleric|druid|shaman|puller)|anyone\?)",
        RegexOptions.CultureInvariant);

    [Test]
    public void MeAndXOnlyAnswerALineThatRecruits()
    {
        var random = new Random(11);
        var lines = new List<Field.Said>();
        for (int i = 0; i < 3_000; i++)
        {
            Field.Facts facts = Facts(i, 10 + i % 40, camp: i % 2 == 0) with { GuildKeep = i % 3 == 0 ? "Caer Benowyc" : null };
            foreach (string channel in new[] { "faction", "guild", "say" })
                if (Field.Line(channel, facts, null, random) is { } said)
                    lines.Add(said);
        }
        foreach (Field.Said said in lines.Where(said => said.Answers != null && said.Answers.Any(a => a is "me" or "x" or "x me" or "me pls")))
            Assert.That(Recruits.IsMatch(said.Text), Is.True, $"\"{said.Text}\" draws a \"me\"");
        Assert.That(lines.Where(said => said.Text.Contains("doors ok")).All(said => said.Answers.All(a => a is not ("me" or "x"))), Is.True,
            "a question gets an answer, not \"me\"");
    }

    [Test]
    public void AHealerCallIsAnsweredOnlyByAHealer()
    {
        var said = new Field.Said("any healers on?", "ask_group", ["{healer}me", "{healer}me, {lvl}"]);
        Field.Facts cleric = Facts(1, 30), thane = Facts(2, 30) with { ClassName = "Thane", IsHealer = false };
        Assert.That(Field.Answer(said, cleric, null, new Random(1)), Does.StartWith("me"));
        Assert.That(Field.Answer(said, thane, null, new Random(1)), Is.Null);
    }
}
