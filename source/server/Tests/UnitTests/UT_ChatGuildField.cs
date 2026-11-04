using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, the fourth pass after the ambient spot test: guild chat from the field (the bot's and the guild's state), and
/// less repetition across the stream (varied short replies, one item advertised at most once an hour, no trailing "...").
/// Written from the categories, not from the judged lines.
/// </summary>
[TestFixture]
public class UT_ChatGuildField
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], [], ["Caer Renaris"], ["Midgard"], 0, 0, "Forest Sauvage");

    private static Field.Facts Facts(int level = 31, string cls = "Friar", string item = "Ghostly Cloak", bool gear = true, Field.WorldView world = null,
        long id = 900) =>
        new(id, eRealm.Albion, cls, level, "Camelot Hills", "Keltoi Fogou", false, false, false, false, 1, false, false, false, false, null, item, 95, gear,
            null, 0, 20, true, world ?? Frontier);

    private static List<Field.Said> Guild(Field.Facts facts, int count, int seed = 1)
    {
        var random = new Random(seed);
        var lines = new List<Field.Said>();
        for (int i = 0; i < count; i++)
            if (Field.Line("guild", facts, null, random) is { } said)
                lines.Add(said);
        return lines;
    }

    [Test]
    public void GuildLinesComeFromTheFieldAndReadLikeGuildChat()
    {
        Assert.That(Field.Share("guild"), Is.GreaterThanOrEqualTo(0.85));
        List<Field.Said> lines = Guild(Facts(), 400);
        Assert.That(lines.Count, Is.GreaterThan(350));
        foreach (Field.Said said in lines)
        {
            Assert.That(said.Text, Is.EqualTo(said.Text.ToLowerInvariant()), said.Text);
            Assert.That(said.Text, Does.Not.Contain("..."), said.Text);
            Assert.That(said.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.LessThanOrEqualTo(9), said.Text);
        }
        Assert.That(lines.Where(said => said.Kind == "guild_group").All(said => said.Text.Contains("31") || said.Text.Contains("29-33") ||
            said.Text.Contains("camelot hills") || said.Text.Contains("keltoi") || said.Text.Contains("fogou")), Is.True, "a group call names the real level or place");
        Assert.That(lines.Any(said => said.Kind == "guild_craft"), Is.True);
        Assert.That(lines.Where(said => said.Kind == "guild_sell").All(said => said.Text.Contains("ghostly cloak")), Is.True, "the item really in its bags");
    }

    [Test]
    public void AGzGoesOnlyToAGuildmateWhoReallyDinged()
    {
        Assert.That(Guild(Facts(), 300).Any(said => said.Kind == "guild_ding" || said.Text.StartsWith("gz ", StringComparison.Ordinal)), Is.False);
        List<Field.Said> dinged = Guild(Facts() with { GuildmateDinged = "Bran" }, 100);
        Assert.That(dinged.Where(said => said.Kind == "guild_ding").All(said => said.Text.Contains("bran")), Is.True);
        Assert.That(dinged.Count(said => said.Kind == "guild_ding"), Is.GreaterThan(30));
        Assert.That(Guild(Facts() with { GuildmateArrived = "Sigrun" }, 100).Any(said => said.Kind == "guild_hello" && said.Text.Contains("sigrun")), Is.True);
    }

    [Test]
    public void RaidAndKeepPlansUseTheGuildsRealKeep()
    {
        Assert.That(Guild(Facts(level: 20), 300).Any(said => said.Kind == "raid"), Is.False, "raid talk is for 35+");
        List<Field.Said> lines = Guild(Facts(level: 45) with { GuildKeep = "Caer Berkstead" }, 400);
        Assert.That(lines.Any(said => said.Kind == "raid"), Is.True);
        Assert.That(lines.Any(said => said.Kind == "inc"), Is.False, "no call while the guild's keep isn't under attack");
        Assert.That(lines.Where(said => said.Text.Contains("anyone at ")).All(said => said.Text.Contains("berk")), Is.True);
        var attacked = Frontier with { AttackedKeeps = ["Caer Berkstead"] };
        Assert.That(Guild(Facts(level: 45, world: attacked) with { GuildKeep = "Caer Berkstead" }, 300)
            .Where(said => said.Kind == "inc").All(said => said.Text.Contains("berk")), Is.True);
    }

    [Test]
    public void ADropIsNotMyOldAnything()
    {
        List<Field.Said> lines = Guild(Facts(item: "Spider Silk", gear: false), 400).Where(said => said.Kind == "guild_sell").ToList();
        Assert.That(lines, Is.Not.Empty);
        Assert.That(lines.Any(said => said.Text.Contains("my old")), Is.False);
    }

    [TestCase("took a week off the game and missed the chatter")]
    [TestCase("Soda number three, I regret nothing.")]
    [TestCase("this guild is the best part of my week just saying")]
    [TestCase("to whoever had a rough week, guild chat is here for you")]
    [TestCase("reminder: the purple cons are not a dare")]
    [TestCase("how is everyone's night going?")]
    [TestCase("long day at work, glad to be on...")]
    public void SentimentalAndLifeFillerGuildLinesAreCrafted(string text)
    {
        var line = new VC.Line("g", "guild", "general", [], "*", [], 1, 50, [], "", "chat", 1, text);
        Assert.That(AutonomousChatFiller.IsCrafted(line), Is.True, text);
    }

    [TestCase("anyone up for keltoi")]
    [TestCase("who needs a rez")]
    public void ShortPlainGuildLinesMayStay(string text) =>
        Assert.That(AutonomousChatFiller.IsCrafted(new VC.Line("g", "guild", "leveling", [], "*", [], 1, 50, [], "", "chat", 1, text)), Is.False);

    [Test]
    public void ShortRepliesVaryAndSpeakForTheAnswerer()
    {
        var random = new Random(8);
        var ask = new Field.Said("anyone up for keltoi?", "ask_group", ["me", "x", "pst", "me pls", "x me", "can i come", "o/", "me, {cls} {lvl}"]);
        Field.Facts thane = Facts(level: 27, cls: "Thane");
        string[] answers = Enumerable.Range(0, 60).Select(_ => Field.Answer(ask, thane, null, random)).ToArray();
        Assert.That(answers.Distinct().Count(), Is.GreaterThanOrEqualTo(7));
        Assert.That(answers.Where(answer => answer.Contains(',')).All(answer => answer == "me, thane 27"), Is.True, "its own class and level");
        Assert.That(Field.Answer(ask with { Answers = ["me, {cls} {lvl}"] }, null, null, random), Is.Null, "no answerer, no made-up class");
        // A three-word answer isn't said twice while the memory holds it; "me" may be.
        var memory = new Field.Memory();
        var once = new Field.Said("anyone up for keltoi?", "ask_group", ["me, {cls} {lvl}"]);
        Assert.That(Field.Answer(once, thane, memory, random), Is.EqualTo("me, thane 27"));
        Assert.That(Field.Answer(once, thane, memory, random), Is.Null);
        var me = new Field.Said("anyone up for keltoi?", "ask_group", ["me"]);
        Assert.That(Field.Answer(me, thane, memory, random), Is.EqualTo("me"));
        Assert.That(Field.Answer(me, thane, memory, random), Is.EqualTo("me"));
    }

    [Test]
    public void OneItemIsAdvertisedAtMostOnceAnHour()
    {
        var memory = new Field.Memory();
        Assert.That(memory.TryItem("bone-studded ring", 0), Is.True);
        Assert.That(memory.TryItem("Bone-Studded Ring", 30 * 60_000), Is.False, "same item, half an hour later");
        Assert.That(memory.TryItem("bone-studded ring", Field.Memory.ItemRestMs + 1), Is.True, "after an hour");
        var random = new Random(2);
        Assert.That(memory.TryItem("ghostly cloak", 0), Is.True);
        int sales = 0;
        for (int i = 0; i < 400; i++)
            if (Field.Line("trade", Facts(id: 900 + i, level: 20 + i % 20), memory, random, 5_000 + i * 1_000) is { Kind: "wts" } said)
            {
                sales++;
                Assert.That(said.Text, Does.Contain("ghostly cloak"));
            }
        Assert.That(sales, Is.EqualTo(0), "the cloak was advertised within the hour: the bots post a wtb instead");
        Assert.That(Field.Line("trade", Facts() with { BagFull = true }, new Field.Memory(), random, 0)?.Kind, Is.AnyOf("wts", "wtb"));
    }

    [Test]
    public void NoBotTrailsOffWithDots()
    {
        var random = new Random(4);
        for (long id = 1; id < 3_000; id++)
            Assert.That(AutonomousChatIdiolect.Apply("anyone know where the healer trainer is in camelot", id, false, random), Does.Not.EndWith("..."));
    }
}
