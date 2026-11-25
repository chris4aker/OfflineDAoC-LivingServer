using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Gate = DOL.GS.BotSayGate;
using Kind = DOL.GS.BotSayGate.NonAnswerKind;
using Path = DOL.GS.AutonomousChatReplyPath;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Realism 2a (issue #478): no non-answer as a whole answer, in any channel. The say gate's one class (BotSayGate.NonAnswer.cs):
/// a dunno, a "try elsewhere", an echo word, a bare laugh and stock agreement are never the whole reply to a line said to the bot
/// or asked of its channel. The reply planner settles its pick with it (the bot's own facts, or quiet), the gate holds what
/// still comes through any exit, and the fallbacks that spoke are gone. Tests only.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_NoNonAnswers478
{
    // ---- the class ---------------------------------------------------------------------------------------------------------

    [TestCase("dunno", "where do animists train", Kind.Dunno)]
    [TestCase("no clue sry", "what lvl for df", Kind.Dunno)]
    [TestCase("Not sure, try /advice.", "whats your class", Kind.Dunno)]
    [TestCase("hmm dunno", "is it hard?", Kind.Dunno)]
    [TestCase("dunno havent camped them", "puny skeletons drop anything good?", Kind.Dunno)]
    [TestCase("the name escapes me lol", "who?", Kind.Dunno)]
    [TestCase("try asking a guard", "where is the bridge?", Kind.Elsewhere)]
    [TestCase("ask around maybe", "who buys loot?", Kind.Elsewhere)]
    [TestCase("where", "where do animists train", Kind.Echo)]
    [TestCase("Where?", "how do i add more bots", Kind.Echo)]
    [TestCase("?", "Brannoc", Kind.Echo)]
    [TestCase("ya?", "u there?", Kind.Echo)]
    [TestCase("lol", "im by camp station", Kind.Laugh)]
    [TestCase("lol", "pull", Kind.Laugh)]
    [TestCase("heh", "whats your spec?", Kind.Laugh)]
    [TestCase("true", "is df ours?", Kind.Agreement)]
    [TestCase("lol same", "what do you do at 50?", Kind.Agreement)]
    [TestCase("k", "where is the bridge?", Kind.Agreement)]
    [TestCase("gotcha", "how do i get to thid", Kind.Agreement)]
    public void TheClassKnowsANonAnswer(string reply, string answered, Kind kind) =>
        Assert.That(Gate.KindOf(reply, answered), Is.EqualTo(kind));

    [TestCase("ask your trainer at 50, long chain", "when can i do my epic quest?")]
    [TestCase("not sure, maybe 20ish", "what lvl for thid?")]
    [TestCase("no idea, im only 24", "what lvl for df")]
    [TestCase("rr1 lol, havent done much rvr", "whats ur rr")]
    [TestCase("ya", "is df ours?")]
    [TestCase("k", "follow me")]
    [TestCase("lol", "that pull was a mess lol")]
    [TestCase("where?", "adds!!")]
    [TestCase("gz", "dinged 30!")]
    [TestCase("here, whats up?", "u there?")]
    [TestCase("Camelot, by the trainers", "where is the armsman trainer?")]
    [TestCase("/anon", "how do i go anon?")]
    public void AnAnswerIsNoNonAnswer(string reply, string answered) =>
        Assert.That(Gate.KindOf(reply, answered), Is.EqualTo(Kind.None));

    [Test]
    public void ARemarkSaidToNobodyIsOwedNoAnswer()
    {
        Assert.That(Gate.Owed("nobody has died yet, lets keep it that way", saidToBot: false), Is.False);
        Assert.That(Gate.Owed("nobody has died yet, lets keep it that way", saidToBot: true), Is.True);
        Assert.That(Gate.Owed("where is the bridge?", saidToBot: false), Is.True, "asked of the channel");
        Assert.That(Gate.Settle("lol", "nobody has died yet, lets keep it that way", false), Is.EqualTo("lol"), "a reaction to a remark is fine");
    }

    [Test]
    public void SettleAnswersFromWhatItKnowsOrStaysQuiet()
    {
        Assert.That(Gate.Settle("dunno sry", "whats your class", true, () => "24 armsman"), Is.EqualTo("24 armsman"));
        Assert.That(Gate.Settle("dunno sry", "what lvl for df", true, () => null), Is.Null);
        Assert.That(Gate.Settle("dunno sry", "what lvl for df", true, () => "no idea"), Is.Null, "a known line that is a non-answer too is no answer");
        Assert.That(Gate.Settle("in Camelot", "where is the armsman trainer", true, () => "x"), Is.EqualTo("in Camelot"));
    }

    // ---- the gate ----------------------------------------------------------------------------------------------------------

    [SetUp]
    public void Reset() => Gate.ResetForTests();

    [TearDown]
    public void Release() => Gate.ResetForTests();

    [Test]
    public void TheGateHoldsANonAnswerToALineSaidToTheBot()
    {
        string sent = null;
        string shown = Gate.Say(new Gate.Utterance("tell", "Brannoc", eRealm.Albion, "no clue sry", "Wynn")
            { Answers = "where do animists train", SaidToSpeaker = true }, text => sent = text);
        Assert.That(shown, Is.Null);
        Assert.That(sent, Is.Null, "the send is never called");
        Assert.That(Gate.Counts.Held, Is.EqualTo(1));
        shown = Gate.Say(new Gate.Utterance("tell", "Brannoc", eRealm.Albion, "24 armsman", "Wynn")
            { Answers = "whats your class", SaidToSpeaker = true }, text => sent = text);
        Assert.That(shown, Is.EqualTo("24 armsman"));
    }

    [Test]
    public void TheGateHoldsABotsDunnoToAnotherBotsQuestionInFaction()
    {
        // The owner's 1.2.2 game: 13 bot-to-bot "dunno" lines in /faction ("dunno havent camped them").
        Assert.That(Gate.Say(new Gate.Utterance("faction", "Oldfluffy", eRealm.Albion, "puny skeletons drop anything good?"), text => text),
            Is.EqualTo("puny skeletons drop anything good?"));
        Assert.That(Gate.Say(new Gate.Utterance("faction", "Brannoc", eRealm.Albion, "dunno havent camped them"), text => text), Is.Null);
        Assert.That(Gate.Say(new Gate.Utterance("faction", "Brannoc", eRealm.Albion, "bones mostly, and a few coins"), text => text),
            Is.EqualTo("bones mostly, and a few coins"));
        // A laugh after a remark said to nobody is a reaction, not a non-answer.
        Assert.That(Gate.Say(new Gate.Utterance("faction", "Oldfluffy", eRealm.Albion, "my horse just threw me in the lake"), text => text), Is.Not.Null);
        Assert.That(Gate.Say(new Gate.Utterance("faction", "Brannoc", eRealm.Albion, "lol"), text => text), Is.EqualTo("lol"));
    }

    // ---- the planner -------------------------------------------------------------------------------------------------------

    private static VC.Picked Line(string text) =>
        new(text, new VC.Line("t1", "reply", null, [], "*", [], 1, 50, [], null, null, 1, text), null);

    private static readonly string[] Deflections =
        ["dunno", "no clue sry", "not sure, try /advice", "lol", "true", "where?", "?", "k", "hmm no clue", "ask around"];

    private static Path.Ports PortsSaying(string text, AutonomousChatQuestions.BotFacts facts = null) => new()
    {
        Random = new Random(478),
        Line = (kind, intent, answers, flags, relax, filter) => Line(text),
        Style = line => line,
        Facts = facts == null ? null : () => facts,
        Moment = (message, channel, addressed) => new AutonomousChatReplyBudget.Moment(message, channel, addressed,
            AutonomousChatIntentModel.Predict(message), "Armsman", false, false, false, null, facts),
    };

    private static readonly AutonomousChatQuestions.BotFacts Brannoc =
        new(7, 24, "Armsman", eRealm.Albion, 3, BotPersonalityType.Leveler, false, 2, null, Name: "Brannoc", Zone: "Camelot Hills", Target: "large skeleton");

    [Test]
    public void NoChannelGetsANonAnswerAsTheWholeAnswer(
        [Values("faction", "say", "group", "tell", "guild", "broadcast", "chatgroup")] string channel, [Values(true, false)] bool addressed)
    {
        foreach (string message in new[] { "where do animists train", "what lvl for df", "how do we take a keep", "pull", "im by camp station",
                     "is df ours?", "whats the bg for 26", "really?", "how much" })
            foreach (string deflection in Deflections)
            {
                var choice = Path.Budget(PortsSaying(deflection, Brannoc), new AutonomousBotChatCoordinator.ReplyChoice(deflection, "intent banter"),
                    message, addressed, channel);
                bool owed = Gate.Owed(message, addressed || channel is "tell" or "group");
                if (owed && choice.Text != null)
                    Assert.That(Gate.IsNonAnswer(choice.Text, message), Is.False, $"{channel} \"{message}\" -> \"{choice.Text}\" ({choice.Route})");
            }
    }

    [TestCase("whats your class", "armsman")]
    [TestCase("what lvl are you?", "24")]
    [TestCase("can you pull?", "armsman")]
    [TestCase("who is pulling?", "tank")]
    [TestCase("need that drop?", null)]
    [TestCase("what now?", "skeletons")]
    [TestCase("is this camp ok for us?", null)]
    [TestCase("how much longer can you stay?", null)]
    [TestCase("Aldwyn what lvl were you when you picked your class", "5")]
    public void AQuestionAboutTheBotIsAnsweredFromWhatItKnows(string message, string holds)
    {
        var choice = Path.Budget(PortsSaying("dunno sry", Brannoc), new AutonomousBotChatCoordinator.ReplyChoice("dunno sry", "none factual"), message, true, "tell");
        Assert.That(choice.Text, Is.Not.Null, choice.Route);
        Assert.That(Gate.IsNonAnswer(choice.Text, message), Is.False, choice.Text);
        if (holds != null)
            Assert.That(choice.Text.ToLowerInvariant(), Does.Contain(holds));
    }

    [Test]
    public void AnOpenerWithNothingInItGetsTheBotHereNeverABareQuestionMark()
    {
        for (int seed = 0; seed < 50; seed++)
            foreach (bool busy in new[] { false, true })
            {
                string ack = AutonomousChatReplyBudget.Ack(seed, busy, new Random(seed));
                Assert.That(Gate.IsNonAnswer(ack, "u there?"), Is.False, ack);
            }
    }

    [Test]
    public void AQuestionToTheBotThatNothingAnswersGetsQuietNotNoClue()
    {
        var moment = new AutonomousChatReplyBudget.Moment("what lvl for df", "tell", true, AutonomousChatIntentModel.Predict("what lvl for df"), "Armsman",
            false, false, false);
        Assert.That(AutonomousChatReplyBudget.ToContent(moment, new Random(1)), Is.Null);
        var choice = Path.Budget(PortsSaying("dunno", Brannoc), new AutonomousBotChatCoordinator.ReplyChoice(null, "none factual"), "what lvl for df", true, "tell");
        Assert.That(choice.Text, Is.Null, choice.Route);
    }

    [TestCase("where do animists train")]
    [TestCase("how do i add more bots")]
    [TestCase("does it cost money to train")]
    public void AQuestionIsNoWarning(string message) =>
        Assert.That(AutonomousChatReplyBudget.IsAlarm(message, AutonomousChatIntentModel.Predict(message)), Is.False);

    [Test]
    public void TheFieldTalkNonAnswerIsTheGatesClass()
    {
        foreach (string text in new[] { "dunno havent camped them", "idk", "no idea", "not sure tbh", "bones mostly", "the skellies in the hills" })
            Assert.That(AutonomousChatFieldTalk.IsNonAnswer(text), Is.EqualTo(Gate.IsNonAnswer(text)), text);
    }

    [Test]
    public void FieldTalkDoesNotOpenOnADunno()
    {
        // PR #614 (the evening ratchet): "dunno, check lfg" holds something past its hedge, so the gate lets it through, but a bot
        // answering another bot from its pool doesn't open on a dunno (field talk's rule before #478).
        foreach (string text in new[] { "dunno, check lfg", "dunno, ask in guild", "idk, not me", "Dunno, quiet tonight" })
        {
            Assert.That(Gate.IsNonAnswer(text), Is.False, text);
            Assert.That(AutonomousChatFieldTalk.IsNonAnswer(text), Is.True, text);
        }
        Assert.That(AutonomousChatFieldTalk.IsNonAnswer("check lfg, was a couple"), Is.False);
        Assert.That(AutonomousChatFieldTalk.IsNonAnswer("not that i know"), Is.False);
    }

    [Test]
    public void TheCorpusHasNoDunnoSetLeft()
    {
        IEnumerable<VC.Line> replies = AutonomousBotVoices.Corpus.Lines.Where(line => line.Kind == "reply" && line.Answers != null && line.Answers.Contains("ask"));
        Assert.That(replies.Select(line => line.Text), Is.Empty, "the @answers ask set (no idea sorry, dunno, try asking a guard) is gone");
    }
}
