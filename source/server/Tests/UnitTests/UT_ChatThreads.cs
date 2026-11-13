using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, the seventh pass after R3d: help frames rest and are capped, lines that invite a reply get one and some run on,
/// stories from real events, and life lines at most once a guild evening. Written from the judges' categories.
/// </summary>
[TestFixture]
public class UT_ChatThreads
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], ["Caer Benowyc"], ["Caer Renaris"], ["Midgard"], 0, 1,
        "Forest Sauvage");

    private static readonly string[] Classes = ["Cleric", "Armsman", "Wizard", "Friar", "Scout", "Minstrel", "Paladin", "Theurgist"];
    private static readonly string[] Zones = ["Camelot Hills", "Salisbury Plains", "Lyonesse", "Dartmoor", "Avalon Marsh", "Cornwall"];

    private static Field.Facts Facts(int i) =>
        new(10_000 + i, eRealm.Albion, Classes[i % Classes.Length], 8 + i % 42, Zones[i % Zones.Length],
            AutonomousBotVoices.DungeonFor(eRealm.Albion, 8 + i % 42, new Random(i)), i % 9 == 0, false, false, i % 4 == 0, i % 4 == 0 ? 3 : 1,
            i % 50 == 0, i % 60 == 0, false, i % 2 == 0, "large skeleton", i % 3 == 0 ? null : "Ghostly Cloak", 95, true, i % 7 == 0 ? "Camelot" : null,
            1 + i % 9, 20, Classes[i % Classes.Length] is "Cleric" or "Friar", Frontier);

    // ---- 1. help frames -----------------------------------------------------------------------------------------------------

    [Test]
    public void AHelpFrameRestsTenMinutesAndIsHeardAtMostFourTimesAnHour()
    {
        Assert.That(Field.Memory.FrameRestMs("ask_xp"), Is.InRange(10 * 60_000, 15 * 60_000));
        var memory = new Field.Memory();
        var random = new Random(4);
        var heard = new List<(long At, Field.Said Said)>();
        for (int i = 0; i < 2_400; i++)
        {
            long now = i * 4_500L;   // three hours, a line every 4.5 s
            if (Field.Line("faction", Facts(i), memory, random, now) is { } said)
                heard.Add((now, said));
        }
        foreach (var frame in heard.Where(h => h.Said.Frame != null).GroupBy(h => h.Said.Frame))
        {
            long[] times = frame.Select(h => h.At).ToArray();
            for (int i = 1; i < times.Length; i++)
                Assert.That(times[i] - times[i - 1], Is.GreaterThanOrEqualTo(10 * 60_000), frame.Key);
            for (int hour = 0; hour < 3; hour++)
                Assert.That(times.Count(t => t >= hour * 3_600_000L && t < (hour + 1) * 3_600_000L), Is.LessThanOrEqualTo(4), frame.Key);
        }
        // XP and spec no longer dominate the questions.
        Field.Said[] questions = heard.Select(h => h.Said).Where(said => said.Frame != null).ToArray();
        int xpAndSpec = questions.Count(said => said.Kind is "ask_xp" or "class" || said.Frame is "ask_spec");
        Assert.That(xpAndSpec / (double)questions.Length, Is.LessThan(0.3), $"{xpAndSpec} of {questions.Length}");
        Assert.That(questions.Select(said => said.Frame).Distinct().Count(), Is.GreaterThan(20), "many frames: gear, quests, travel, crafting, rvr...");
    }

    [Test]
    public void FollowUpFramesAreCappedToo()
    {
        var memory = new Field.Memory();
        var said = new Field.Said("anyone up for keltoi?", "ask_group", ["me"]) { Unanswered = ["still lf keltoi", "still looking for keltoi"] };
        int heard = 0;
        for (int minute = 0; minute < 60; minute++)
            if (Field.FollowUp(said, false, new Random(minute), memory, minute * 60_000L) != null)
                heard++;
        Assert.That(heard, Is.LessThanOrEqualTo(4), "\"still lf...\" at most four times an hour");
    }

    // ---- 2. threads ---------------------------------------------------------------------------------------------------------

    [Test]
    public void ALineThatInvitesAReplyUsuallyGetsOneAndSomeRunOn()
    {
        var random = new Random(9);
        // Not a question: about seven in ten get a reply (a question always does when a bot can answer, 1.1.3 after RC5).
        var said = new Field.Said("camping skeletons if anyone wants to join", "ask_group", ["me", "x", "me pls", "{lvl} {cls} here"]);
        Assert.That(Field.ReplyChance(said), Is.GreaterThanOrEqualTo(0.7));
        Assert.That(Field.ReplyChance(new Field.Said("anyone up for keltoi?", "ask_group", ["me", "x"])), Is.EqualTo(1.0), "a question");
        int answered = 0, longer = 0, three = 0;
        for (int i = 0; i < 2_000; i++)
        {
            List<Field.Step> steps = Field.Thread(said, Facts(i), Facts(i + 1), Facts(i + 2), null, random);
            if (steps.Any(step => step.Who == 1))
                answered++;
            if (steps.Count >= 3)
                longer++;
            if (steps.Any(step => step.Who == 2))
                three++;
            Assert.That(steps.Select(step => step.AtMs), Is.Ordered, "in order");
        }
        Assert.That(answered / 2_000.0, Is.InRange(0.62, 0.8));
        Assert.That(longer / (double)answered, Is.InRange(0.15, 0.4), "about a fifth run on to four lines or more");
        Assert.That(three, Is.GreaterThan(0), "a third bot chimes in now and then");
        Assert.That(Field.Thread(said, Facts(1), null, null, null, random).Any(step => step.Who == 1), Is.False, "no answerer, no answer");
    }

    [Test]
    public void AThreadSpeaksFromEachSpeakersOwnState()
    {
        var random = new Random(2);
        var said = new Field.Said("anyone up for keltoi?", "ask_group", ["{lvl} {cls} here"]);
        Field.Facts answerer = Facts(3) with { ClassName = "Thane", Level = 27 };
        for (int i = 0; i < 200; i++)
            foreach (Field.Step step in Field.Thread(said, Facts(1), answerer, null, null, random).Where(step => step.Who == 1))
                Assert.That(step.Text, Does.Not.Contain("{"), step.Text);
        Assert.That(Field.Answer(said, answerer, null, random), Is.EqualTo("27 thane here"));
    }

    [Test]
    public void StoriesFromRealEventsRunThreeToSixLinesBetweenTwoOrThreeSpeakers()
    {
        var random = new Random(5);
        foreach (string kind in new[] { "ding50", "death", "wipe" })
        {
            List<Field.Step> story = Field.Story(kind, Facts(1) with { Level = kind == "ding50" ? 50 : 30 }, Facts(2), Facts(3), random);
            Assert.That(story.Count, Is.InRange(3, 6), kind);
            Assert.That(story[0].Who, Is.EqualTo(0), "the bot it happened to tells it");
            Assert.That(story.Select(step => step.Who).Distinct().Count(), Is.InRange(2, 3), kind);
        }
        Assert.That(Field.Story("ding50", Facts(1) with { Level = 50 }, Facts(2), null, random)[0].Text, Does.Contain("50"));
        // The same story isn't told twice an evening in the same words.
        var memory = new Field.Memory();
        var told = new List<string>();
        for (int i = 0; i < 30; i++)
            told.AddRange(Field.Story("wipe", Facts(i), Facts(i + 1), Facts(i + 2), random, memory).Select(step => step.Text)
                .Where(text => AutonomousChatFiller.Skeleton(text).Split(' ').Length > 2));
        Assert.That(told.Distinct().Count(), Is.EqualTo(told.Count));
    }

    // ---- round 8: typing time, frames for every family, answers that fit the question ----------------------------------------

    [Test]
    public void EveryBotAnswerTakesAtLeastTwoSecondsAndAWordMore()
    {
        var random = new Random(1);
        foreach (string reply in new[] { "me", "x", "omw", "in lyonesse", "keltoi with a grp, back rooms" })
            for (int i = 0; i < 200; i++)
            {
                int words = reply.Split(' ').Length;
                Assert.That(AutonomousChatArbiter.DelayMs("where at?", reply, 200, 0, random), Is.GreaterThanOrEqualTo(2_000 + 300 * words), reply);
            }
        // Every line of a thread after the opener, too.
        var said = new Field.Said("anyone up for keltoi?", "ask_group", ["me", "x"]);
        for (int i = 0; i < 300; i++)
            foreach (Field.Step step in Field.Thread(said, Facts(i), Facts(i + 1), Facts(i + 2), null, random).Where(step => step.Who != 0 || step.AtMs > 0))
                Assert.That(step.AtMs, Is.GreaterThanOrEqualTo(2_300));
    }

    [Test]
    public void LfgCrafterAndLevelFamiliesAreFramesToo()
    {
        var memory = new Field.Memory();
        var random = new Random(12);
        var heard = new List<(long At, Field.Said Said)>();
        for (int i = 0; i < 2_400; i++)
        {
            long now = i * 4_500L;
            foreach (string channel in new[] { "faction", "lfg", "trade", "guild" })
                if (Field.Line(channel, Facts(i), memory, random, now) is { } said)
                    heard.Add((now, said));
        }
        string[] named = ["g_lfgrp", "class_later", "wtb_any1", "g_craft_any", "g_more", "ask_xp_good", "ask_xp_place", "status_grind"];
        foreach (var frame in heard.Where(h => named.Contains(h.Said.Family)).GroupBy(h => h.Said.Family))
        {
            Assert.That(frame.All(h => h.Said.Frame == frame.Key), Is.True, frame.Key);
            for (int hour = 0; hour < 3; hour++)
                Assert.That(frame.Count(h => h.At >= hour * 3_600_000L && h.At < (hour + 1) * 3_600_000L), Is.LessThanOrEqualTo(Field.Memory.FramesPerHour), frame.Key);
        }
        Assert.That(heard.Where(h => h.Said.Kind == "lfg").Any(h => h.Said.Frame == null), Is.True, "terse lfg posts may repeat as on any server");
    }

    [TestCase("where at??", "place")]
    [TestCase("where do 21s xp?", "place")]
    [TestCase("which camp there?", "place")]
    [TestCase("how much for a 99% sword?", "price")]
    [TestCase("pc ghostly cloak?", "price")]
    [TestCase("how many?", "count")]
    [TestCase("when did we lose rena?", "time")]
    [TestCase("raid tonight, what time?", "time")]
    [TestCase("who's leading?", "who")]
    [TestCase("what class?", "class")]
    [TestCase("what lvl r u?", "level")]
    [TestCase("anyone up for keltoi?", null)]
    public void AQuestionAsksForAKindOfAnswer(string question, string kind) => Assert.That(Field.QuestionKind(question), Is.EqualTo(kind), question);

    [TestCase("place", "in lyonesse", true)]
    [TestCase("place", "keltoi", true)]
    [TestCase("place", "43", false)]
    [TestCase("place", "43 thane", false)]
    [TestCase("place", "me", false)]
    [TestCase("place", "few", false)]
    [TestCase("price", "2p", true)]
    [TestCase("price", "50g obo", true)]
    [TestCase("price", "keltoi", false)]
    [TestCase("time", "9ish", true)]
    [TestCase("time", "dunno", true)]
    [TestCase("time", "big grp", false)]
    [TestCase("who", "thane", true)]
    [TestCase("who", "43", false)]
    [TestCase("count", "10+ at the door", true)]
    [TestCase("count", "at the gate", false)]
    [TestCase("level", "27", true)]
    [TestCase("level", "in lyonesse", false)]
    public void AnAnswerGivesWhatTheQuestionAsks(string kind, string answer, bool fits) => Assert.That(Field.Fits(answer, kind), Is.EqualTo(fits), $"{kind}: {answer}");

    [Test]
    public void AWhereNeverGetsALevelInAThread()
    {
        var random = new Random(8);
        var said = new Field.Said("anyone up for keltoi?", "ask_group", ["me", "x", "{lvl} {cls} here"]);
        int checkedAsks = 0;
        for (int i = 0; i < 3_000; i++)
        {
            List<Field.Step> steps = Field.Thread(said, Facts(i), Facts(i + 1), Facts(i + 2), null, random);
            for (int k = 0; k + 1 < steps.Count; k++)
                if (steps[k].Who == 0 && Field.QuestionKind(steps[k].Text) is { } kind && steps[k + 1].Who == 1)
                {
                    checkedAsks++;
                    Assert.That(Field.Fits(steps[k + 1].Text, kind), Is.True, $"{steps[k].Text} -> {steps[k + 1].Text}");
                }
        }
        Assert.That(checkedAsks, Is.GreaterThan(50));
        Assert.That(Field.Answer(new Field.Said("where at??", "ask_group", ["43", "{lvl} {cls}"]), Facts(5), null, random), Does.Not.Match(@"^\d"),
            "no written answer fits, so a place from the answerer's own state");
    }

    [Test]
    public void NoLevelPastFifty()
    {
        var random = new Random(3);
        for (int i = 0; i < 2_000; i++)
        {
            Field.Facts top = Facts(i) with { Level = 50, JustLeveled = i % 2 == 0 };
            foreach (string channel in new[] { "faction", "lfg", "guild" })
                if (Field.Line(channel, top, null, random) is { } said)
                {
                    foreach (string text in new[] { said.Text }.Concat(said.Unanswered ?? []).Concat(said.Thanks ?? []))
                        foreach (System.Text.RegularExpressions.Match number in System.Text.RegularExpressions.Regex.Matches(text, @"\b(\d{2})\b(?!%)"))
                            Assert.That(int.Parse(number.Value), Is.LessThanOrEqualTo(50), text);
                }
        }
    }

    // ---- 3. life lines in guild ------------------------------------------------------------------------------------------------

    [TestCase("freezing outside, my hands are numb")]
    [TestCase("the library closed early so here i am")]
    [TestCase("aced my test, celebrating with xp")]
    [TestCase("hi all, just checking in to say hi")]
    [TestCase("rented a movie for later, after this level")]
    [TestCase("payday today, which means new armor tomorrow")]
    [TestCase("the dog heard thunder and now hes under my desk")]
    public void LifeLinesAreKnown(string text) => Assert.That(AutonomousChatFiller.IsLife(text), Is.True, text);

    [TestCase("anyone up for keltoi")]
    [TestCase("lf healer for tepoks")]
    [TestCase("who's on for df tonight")]
    public void GameLinesAreNotLife(string text) => Assert.That(AutonomousChatFiller.IsLife(text), Is.False, text);

    private static VC Corpus()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
            return reader.ReadToEnd();
        }
        return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
    }

    [Test]
    public void AGuildEveningHearsOneLifeLineAtMostAndNoneTwice()
    {
        VC.Line[] written = Corpus().Lines.Where(line => line.Kind == "guild").ToArray();
        var random = new Random(6);
        var memory = new Field.Memory(512);
        var heard = new List<VC.Line>();
        // Six hours of a busy guild, a written line tried every 30 s (far more often than the 10% the guild uses).
        for (long now = 0; now < 6 * 60 * 60_000L; now += 30_000)
        {
            VC.Line line = written[random.Next(written.Length)];
            if (!AutonomousChatFiller.GuildCorpusFits(line, memory, now))
                continue;
            AutonomousChatFiller.NoteGuildCorpus(line, memory, now);
            heard.Add(line);
        }
        Assert.That(heard, Is.Not.Empty);
        Assert.That(heard.Count(AutonomousChatFiller.IsLife), Is.LessThanOrEqualTo(1), string.Join(" | ", heard.Where(AutonomousChatFiller.IsLife).Select(l => l.Text)));
        Assert.That(heard.Select(line => line.Text).Distinct().Count(), Is.EqualTo(heard.Count), "no written guild line twice an evening");
    }
}
