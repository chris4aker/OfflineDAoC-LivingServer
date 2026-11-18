using System;
using System.Linq;
using NUnit.Framework;
using Convo = DOL.GS.ChatConversations;

namespace DOL.GS.Tests;

/// <summary>
/// Conversation memory (1.2, owner 2026-11-18: DESIGN-040 is in 1.2). Multi-turn exchanges, with and without unrelated lines
/// between turns: a second line goes to the same bot, "how do i get there" completes from the last place, and a bot never asks
/// what the conversation already holds. Tests only: not seen on the owner's PC.
/// </summary>
[TestFixture]
public class UT_ChatConversation
{
    private const string Room = "faction:Midgard";

    [Test]
    public void ASecondLineGoesToTheSameBot()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "where is a good camp at 24?", 0);
        c.NoteBot("Wynn", 7, "Aldric", Room, "try the bone camp in Muspelheim", 2_000);
        Assert.That(c.FloorHolder("Wynn", Room, 5_000), Is.EqualTo(7));
        Assert.That(c.FloorHolder("Wynn", Room, 2_000 + Convo.FloorMs + 1), Is.Zero, "the floor goes after a while");
        Assert.That(c.FloorHolder("Brynn", Room, 5_000), Is.Zero, "only for the player it talked to");
    }

    [Test]
    public void TheFloorHoldsWithTwoUnrelatedLinesBetween()
    {
        var c = new Convo();
        c.NoteBot("Wynn", 7, "Aldric", Room, "try the bone camp in Muspelheim", 0);
        // Two other exchanges land in the channel: other bots to other players.
        c.NoteBot("Brynn", 8, "Cael", Room, "gz on 30", 1_000);
        c.NoteBot("Dorn", 9, "Eira", Room, "the cleric trainer is in camelot", 2_000);
        Assert.That(c.FloorHolder("Wynn", Room, 4_000), Is.EqualTo(7));
        Assert.That(Convo.Resolve("how do i get there?", c.In("Wynn", Room, 4_000)), Does.Contain("muspelheim"));
    }

    [TestCase("how do i get there", "how do i get to muspelheim")]
    [TestCase("what about there?", "what about muspelheim?")]
    [TestCase("is it far", "is muspelheim far")]
    [TestCase("ok where is it?", "where is muspelheim?")]
    public void ThereIsTheLastPlace(string line, string resolved)
    {
        var c = new Convo();
        c.NoteBot("Wynn", 7, "Aldric", Room, "try the bone camp in Muspelheim", 0);
        Assert.That(Convo.Resolve(line, c.In("Wynn", Room, 1_000)), Is.EqualTo(resolved));
    }

    [Test]
    public void ALineWithNothingToCompleteIsLeftAlone()
    {
        var c = new Convo();
        c.NoteBot("Wynn", 7, "Aldric", Room, "lol same", 0);
        Assert.That(Convo.Resolve("how do i get there", c.In("Wynn", Room, 1_000)), Is.EqualTo("how do i get there"), "no place in play");
        Assert.That(Convo.Resolve("anyone selling dyes?", c.In("Wynn", Room, 1_000)), Is.EqualTo("anyone selling dyes?"));
    }

    [Test]
    public void ABotNeverAsksWhatTheConversationHolds()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "lfg 30 cleric", 0);
        Convo.Conversation held = c.In("Wynn", Room, 1_000);
        Assert.That(held.Level, Is.EqualTo(30));
        Assert.That(held.ClassName, Is.EqualTo("Cleric"));
        Assert.That(Convo.AsksWhatItHolds("what level are you?", held), Is.True);
        Assert.That(Convo.AsksWhatItHolds("what class r u?", held), Is.True);
        Assert.That(Convo.AsksWhatItHolds("where are you?", held), Is.False, "the place was never given");
        Assert.That(Convo.AsksWhatItHolds("nice, im 31", held), Is.False);
    }

    [Test]
    public void AConversationEndsAfterFiveMinutes()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "lfg 30 cleric", 0);
        Assert.That(c.In("Wynn", Room, Convo.KeepMs), Is.Not.Null);
        Assert.That(c.In("Wynn", Room, Convo.KeepMs + 1), Is.Null);
    }

    [Test]
    public void AFiveTurnTellWithACrafter()
    {
        var c = new Convo();
        string tell = AutonomousChatThreads.Tell("Wynn", "Smithy");
        c.NotePlayer("Wynn", tell, "can you make me a studded helm?", 0, 42);
        c.NoteBot("Wynn", 42, "Smithy", tell, "sure, what level?", 3_000);
        Assert.That(c.With("Wynn", 42, 3_500).OpenQuestion, Is.EqualTo("sure, what level?"));
        c.NotePlayer("Wynn", tell, "lvl 30", 6_000, 42);
        Assert.That(c.With("Wynn", 42, 6_500).OpenQuestion, Is.Null, "the player answered it");
        c.NoteBot("Wynn", 42, "Smithy", tell, "can do, 30 is fine", 9_000);
        Convo.Conversation held = c.For("Wynn", 42, tell, 12_000);
        Assert.That(Convo.Resolve("how much for it?", held), Is.EqualTo("how much for studded helm?"), "\"it\" is the helm");
        Assert.That(Convo.AsksWhatItHolds("what level?", held), Is.True, "it was told the level two turns ago");
        c.NotePlayer("Wynn", tell, "how much for it?", 12_000, 42);
        c.NoteBot("Wynn", 42, "Smithy", tell, "3g", 14_000);
        c.NotePlayer("Wynn", tell, "deal ty", 16_000, 42);
        Assert.That(c.With("Wynn", 42, 17_000).Turns.Count, Is.EqualTo(7));
        Assert.That(c.With("Wynn", 42, 17_000).Item, Is.EqualTo("studded helm"));
    }

    [Test]
    public void AFiveTurnTellWithAFriend()
    {
        var c = new Convo();
        string tell = AutonomousChatThreads.Tell("Wynn", "Aldric");
        c.NotePlayer("Wynn", tell, "hey", 0, 7);
        c.NoteBot("Wynn", 7, "Aldric", tell, "hey, been a while", 2_000);
        c.NotePlayer("Wynn", tell, "u still hunting in muspelheim?", 5_000, 7);
        c.NoteBot("Wynn", 7, "Aldric", tell, "ya, bone camp, good xp", 8_000);
        Convo.Conversation held = c.With("Wynn", 7, 9_000);
        Assert.That(held.Place, Is.EqualTo("muspelheim"));
        Assert.That(Convo.Resolve("is it far", held), Is.EqualTo("is muspelheim far"));
        Assert.That(Convo.AsksWhatItHolds("where at?", held), Is.True, "the player named the place");
        c.NotePlayer("Wynn", tell, "is it far", 10_000, 7);
        Assert.That(c.With("Wynn", 7, 11_000).Turns.Count(turn => turn.ByPlayer), Is.EqualTo(3));
    }

    [Test]
    public void AGroupPlanningExchange()
    {
        var c = new Convo();
        string group = "group:1";
        c.NotePlayer("Wynn", group, "where we headed?", 0);
        c.NoteBot("Wynn", 7, "Aldric", group, "back to the bone camp in Muspelheim?", 3_000);
        Assert.That(Convo.Resolve("how do i get there", c.In("Wynn", group, 5_000)), Is.EqualTo("how do i get to muspelheim"));
        c.NotePlayer("Wynn", group, "ok", 6_000);
        c.NoteBot("Wynn", 8, "Brynn", group, "rdy when u are", 8_000);
        Assert.That(c.FloorHolder("Wynn", group, 9_000), Is.EqualTo(8), "the floor goes to whoever spoke last");
        c.NotePlayer("Wynn", group, "ready?", 10_000);
        Assert.That(c.With("Wynn", 8, 11_000).Turns.Last().Text, Is.EqualTo("ready?"), "the line went on with the bot that had the floor");
    }

    [TestCase("hi all", eAutonomousChatIntent.Greeting, false)]
    [TestCase("lfg 30 cleric", eAutonomousChatIntent.Grouping, false)]
    [TestCase("inc 3 albs", eAutonomousChatIntent.Alarm, false)]
    [TestCase("what about there?", eAutonomousChatIntent.Unknown, true)]
    [TestCase("why that one", eAutonomousChatIntent.Unknown, true)]
    public void SomeLinesAreForEveryone(string line, eAutonomousChatIntent intent, bool keeps) =>
        Assert.That(AutonomousBotChatCoordinator.KeepsTheFloor(line, intent), Is.EqualTo(keeps), line);

    [TestCase("lvl 30", 30)]
    [TestCase("32 now", 32)]
    [TestCase("32", 32)]
    [TestCase("lfg 24 druid", 24)]
    [TestCase("inc 3 albs", null)]
    [TestCase("relic raid at 9", null)]
    public void TheLevelALineGives(string line, int? level) => Assert.That(Convo.LevelIn(line), Is.EqualTo(level), line);
}
