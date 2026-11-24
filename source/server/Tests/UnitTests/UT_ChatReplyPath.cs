using System;
using System.Collections.Generic;
using NUnit.Framework;
using Path = DOL.GS.AutonomousChatReplyPath;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// The 1.2.2 chat fixes (docs/bot-voices/CHAT-AUDIT-1.2.2.md, issue #452): the shared reply path (F0), world facts before lore
/// (F1), no "dunno" and no "not sure" set in /faction (F4), questions about grouping are questions (F6) and "epic" is not a raid
/// word for the epic quest (F7).
/// </summary>
[TestFixture]
public class UT_ChatReplyPath
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static VC.Picked Line(string text) =>
        new(text, new VC.Line("t1", "reply", null, [], "*", [], 1, 50, [], null, null, 1, text), null);

    /// <summary>Ports whose corpus has only the "not sure" set (answers "ask"); every other pick finds nothing.</summary>
    private static Path.Ports OnlyNotSure(List<string> asked) => new()
    {
        Random = new FixedRandom(0.0),
        Line = (kind, intent, answers, flags, relax, filter) =>
        {
            asked.Add(answers ?? intent ?? kind);
            return answers == "ask" ? Line("not sure tbh") : null;
        },
        Style = text => text,
    };

    // ---- F0: one path -------------------------------------------------------------------------------------------------------

    [Test]
    public void TheKnowledgeLookupAsksItsWorldPortsInOrder()
    {
        var steps = new List<string>();
        var ports = new AutonomousChatKnowledge.KnowledgePorts
        {
            Realm = eRealm.Midgard, Level = 20, Random = new FixedRandom(0.0),
            HowFar = q => { steps.Add("far"); return null; },
            World = q => { steps.Add("world"); return null; },
            Trainer = q => { steps.Add("trainer"); return "the trainer in jordheim"; },
        };
        Assert.That(AutonomousChatKnowledge.TryAnswer(ports, "where is the warrior trainer?", out string answer), Is.True);
        Assert.That(answer, Is.EqualTo("the trainer in jordheim"));
        Assert.That(steps, Is.EqualTo(new[] { "world", "trainer" }), "how far is asked only for a how-far question; the world before the trainer");
    }

    [Test]
    public void ALineSaidToNobodyKnownOrAnAckDrawsSilence()
    {
        Path.LinePlan Plan(string message, bool directed = false) => Path.Plan(
            new Path.LineIn(message, true, directed, true, AutonomousChatIntentModel.Predict(message), AutonomousChatShortTalk.QuestionShape(message), false, null, 20),
            () => false, (_, _) => true, true, new FixedRandom(0.0));
        Assert.That(Plan("ok").Step, Is.EqualTo(Path.Step.Silent));
        Assert.That(Plan("ok").Route, Is.EqualTo("none ack"));
        Assert.That(Plan("where should i level at 20?").Step, Is.EqualTo(Path.Step.Reply));
    }

    [Test]
    public void ABusyBotNamedInTheLineTakesIt()
    {
        string message = "what lvl r u";
        var plan = Path.Plan(new Path.LineIn(message, true, false, true, AutonomousChatIntentModel.Predict(message), AutonomousChatShortTalk.QuestionShape(message),
            false, null, 20), () => true, null, true, new FixedRandom(0.0));
        Assert.That(plan.Step, Is.EqualTo(Path.Step.Waited));
    }

    [Test]
    public void AKnowledgeAnswerIsSaidAsItIsAndNotBudgeted()
    {
        var line = new Path.LineIn("what does lfg mean?", true, false, true, eAutonomousChatIntent.Grouping, null, false, "looking for group", 20);
        var choice = Path.Choose(new Path.Ports { Style = t => t }, line, new Path.LinePlan(Path.Step.Reply), "faction", null,
            _ => throw new AssertionException("no voiced reply for a knowledge answer"));
        Assert.That(choice.Route, Is.EqualTo("knowledge"));
        Assert.That(choice.Text, Is.EqualTo("looking for group"));
    }

    // ---- F1: world facts before lore ----------------------------------------------------------------------------------------

    [TestCase("what lvl is gotar?")]
    [TestCase("is vendo caverns ok at 24?")]
    [TestCase("can a 50 solo in sheeroe hills?")]
    [TestCase("whats the level range for devwy?")]
    [TestCase("who has darkness falls right now?")]
    [TestCase("do we still own our relics?")]
    public void LoreNeverAnswersALevelOrOwnershipQuestion(string question) =>
        Assert.That(AutonomousChatKnowledge.LoreMayAnswer(question), Is.False, question);

    [TestCase("whats in krondon?")]
    [TestCase("who is the storm keeper?")]
    [TestCase("what is atlantis?")]
    [TestCase("is there anything under camelot?")]
    public void LoreStillAnswersLoreQuestions(string question) =>
        Assert.That(AutonomousChatKnowledge.LoreMayAnswer(question), Is.True, question);

    [Test]
    public void APlaceLevelQuestionIsAnsweredFromTheWorldBeforeTheLore()
    {
        var ports = new AutonomousChatKnowledge.KnowledgePorts
        {
            Realm = eRealm.Midgard, Level = 12, Random = new FixedRandom(0.0), Bible = LoreBible.Current, Corpus = AutonomousBotVoices.Corpus,
            World = q => AutonomousChatQuestions.AnswerWorld(q, eRealm.Midgard,
                new AutonomousChatQuestions.WorldFacts(eRealm.None, [], [], AutonomousChatPlaces.All), new FixedRandom(0.0)),
        };
        Assert.That(AutonomousChatKnowledge.TryAnswer(ports, "what lvl is gotar?", out string answer), Is.True);
        Assert.That(answer, Does.Match(@"\d"), "a level range, not a lore line");
    }

    [Test]
    public void TheRealmDragonIsAnsweredBeforeTheLegends()
    {
        var ports = new AutonomousChatKnowledge.KnowledgePorts { Realm = eRealm.Albion, Random = new FixedRandom(0.0), Corpus = AutonomousBotVoices.Corpus };
        Assert.That(AutonomousChatKnowledge.TryAnswer(ports, "is there a dragon in albion?", out string answer), Is.True);
        Assert.That(answer, Does.Contain("Golestandt"));
    }

    // ---- F4: no "dunno" and no "not sure" set to the whole realm -------------------------------------------------------------

    [Test]
    public void TheRescueSaysNoDunnoInAnyChannel()
    {
        // #478: 1.2.2 (F4) took the rescue's "dunno" out of /faction; now it is gone from every channel.
        var moment = new AutonomousChatReplyBudget.Moment("how does spellcrafting work?", "faction", false, eAutonomousChatIntent.Unknown, "Thane", false, false, false);
        foreach (string channel in new[] { "faction", "say", "group", "guild", "broadcast", "chatgroup" })
            Assert.That(AutonomousChatReplyBudget.Rescue(moment with { Channel = channel }, "none factual", new FixedRandom(0.0)), Is.Null, channel);
    }

    [Test]
    public void AFactualQuestionToTheRealmGetsNoNotSure()
    {
        var asked = new List<string>();
        var choice = Path.Voiced(OnlyNotSure(asked), "how does spellcrafting work?", null, null, addressed: false, channel: "faction", relax: true);
        Assert.That(choice.Text, Is.Null, choice.Route);
        Assert.That(asked, Does.Not.Contain("ask"), "the not-sure set is not even drawn");
    }

    [Test]
    public void AFactualQuestionToTheBotGetsNoNotSureEither()
    {
        // #478: the "not sure" set no longer answers a line said to the bot (a tell, its name, /group) either.
        var asked = new List<string>();
        var choice = Path.Voiced(OnlyNotSure(asked), "how does spellcrafting work?", null, null, addressed: true, channel: "tell", relax: true);
        Assert.That(choice.Text, Is.Null, choice.Route);
        Assert.That(asked, Does.Not.Contain("ask"), "the not-sure set is not even drawn");
    }

    // ---- F6: questions about grouping ----------------------------------------------------------------------------------------

    [TestCase("what does lfg mean?")]
    [TestCase("is a bard needed in groups?")]
    [TestCase("do i need a group for my epic?")]
    [TestCase("how do i invite someone to a group?")]
    [TestCase("23 friar looking for a spot to grind, ideas?")]
    [TestCase("is it better to solo or group at 20?")]
    public void AQuestionAboutGroupingIsAQuestion(string text) => Assert.That(Path.AsksAboutGrouping(text), Is.True, text);

    [TestCase("anyone lfg?")]
    [TestCase("lfg 30 cleric")]
    [TestCase("is anyone looking for a group?")]
    [TestCase("need a healer for vendo pst")]
    public void AGroupPostIsAGroupPost(string text) => Assert.That(Path.AsksAboutGrouping(text), Is.False, text);

    [Test]
    public void AGroupingQuestionIsNotToldNobodyIsNearItsLevel()
    {
        string message = "what does lfg mean?";
        var line = new Path.LineIn(message, true, false, true, eAutonomousChatIntent.Grouping, AutonomousChatShortTalk.QuestionShape(message), false, null, 5);
        var plan = Path.Plan(line, () => false, (_, _) => false, true, new FixedRandom(0.0));
        Assert.That(plan.Grouping, Is.False);
        Assert.That(plan.NoneNearLevel, Is.Null);
        var post = Path.Plan(line with { Message = "lfg 5 healer pst" }, () => false, (_, _) => false, true, new FixedRandom(0.0));
        Assert.That(post.Grouping, Is.True, "a group post still hears from bots of its level");
        Assert.That(post.NoneNearLevel, Is.Not.Null);
    }

    [Test]
    public void AGroupPostWithAKnowledgeAnswerGetsTheAnswer()
    {
        var line = new Path.LineIn("lfg?", true, false, true, eAutonomousChatIntent.Grouping, null, false, "looking for group", 5);
        Assert.That(Path.Plan(line, () => false, (_, _) => false, true, new FixedRandom(0.0)).NoneNearLevel, Is.Null);
    }

    // ---- F7: "epic" is not a raid word for the epic quest -------------------------------------------------------------------

    [TestCase("how long is the epic?")]
    [TestCase("where do i get my epic armor?")]
    [TestCase("do i need a group for my epic?")]
    [TestCase("when can i do my epic quest?")]
    public void TheEpicQuestIsNotARaid(string text) => Assert.That(AutonomousChatKnowledge.AsksAboutRealmRaid(text), Is.False, text);

    [TestCase("any epic raids tonight?")]
    [TestCase("is there a raid going")]
    [TestCase("any epic encounters up?")]
    public void ARaidIsStillARaid(string text) => Assert.That(AutonomousChatKnowledge.AsksAboutRealmRaid(text), Is.True, text);

    [Test]
    public void TheEpicIsAnsweredAsAQuest()
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon("how long is the epic?", eRealm.Albion, "Cleric", 30, new FixedRandom(0.0), out string answer), Is.True);
        Assert.That(answer, Does.Contain("chain"));
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon("do i need a group for my epic?", eRealm.Albion, "Cleric", 30, new FixedRandom(0.0), out answer), Is.True);
        Assert.That(answer, Does.Contain("group"));
    }

    [Test]
    public void TheEpicsRewardIsTheArmour()
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon("what do i get from the epic?", eRealm.Hibernia, "Druid", 46, new FixedRandom(0.0), out string answer), Is.True);
        Assert.That(answer, Does.Contain("armor"));
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon("where do i get my epic armor?", eRealm.Hibernia, "Druid", 46, new FixedRandom(0.0), out answer), Is.True);
        Assert.That(answer, Does.Contain("trainer"), "where: the chain and who starts it");
    }

    [TestCase("how do i start my epic?")]
    [TestCase("how long is the epic?")]
    [TestCase("whats the epic like?")]
    public void NoEpicAnswerClaimsTheBotDidIt(string text)
    {
        for (int i = 0; i < 3; i++)
        {
            var random = new Random(i);
            if (AutonomousChatKnowledge.TryAnswerCommon(text, eRealm.Albion, "Cleric", 46, random, out string answer))
                Assert.That(answer, Does.Not.Contain("did mine"), text);
        }
    }

    // ---- a question to a named bot about itself -----------------------------------------------------------------------------

    [Test]
    public void ANamedBotAnswersAboutItselfBeforeTheKnowledgeLookup()
    {
        Assert.That(Path.KnowledgeFirst("did you finish your epic?", eAutonomousChatIntent.Unknown, false, directed: true), Is.False);
        Assert.That(Path.KnowledgeFirst("did you finish your epic?", eAutonomousChatIntent.Unknown, false, directed: false), Is.True);
        Assert.That(Path.KnowledgeFirst("where is the cleric trainer?", eAutonomousChatIntent.Unknown, false, directed: true), Is.True);
        Assert.That(Path.KnowledgeFirst("anyone in camelot?", eAutonomousChatIntent.Unknown, helpCall: true, directed: false), Is.False);
    }
}
