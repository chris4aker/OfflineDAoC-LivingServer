using System;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2, C0: every chat decision records why (AutonomousChatWhy, /lswhy), with a rule score.</summary>
[TestFixture, NonParallelizable]
public class UT_ChatWhy
{
    [SetUp]
    public void Clear() => AutonomousChatWhy.Clear();

    private static AutonomousChatWhy.Decision Said(long bot, string name, long at, string text = "hi", eRealm realm = eRealm.Albion, string to = null) =>
        new(at, bot, name, realm, "faction", "ambient", "said", "ambient/general", 2, "abc", "its turn", text, to);

    [Test]
    public void TheNewestDecisionsComeFirstAndOneBotsCanBeFound()
    {
        AutonomousChatWhy.Record(Said(1, "Aldwin", 1_000, "first"));
        AutonomousChatWhy.Record(Said(2, "Brannoc", 2_000, "second"));
        AutonomousChatWhy.Record(Said(1, "Aldwin", 3_000, "third"));
        Assert.That(AutonomousChatWhy.Recent(10).Select(d => d.Text), Is.EqualTo(new[] { "third", "second", "first" }));
        Assert.That(AutonomousChatWhy.ForBot(1, null, 10).Select(d => d.Text), Is.EqualTo(new[] { "third", "first" }));
        Assert.That(AutonomousChatWhy.ForBot(0, "brannoc", 10).Single().Text, Is.EqualTo("second"), "by name when the id is unknown");
        Assert.That(AutonomousChatWhy.LastSpeaker(eRealm.Albion), Is.EqualTo(1));
        Assert.That(AutonomousChatWhy.LastSpeaker(eRealm.Midgard), Is.EqualTo(0));
    }

    [Test]
    public void TheRingKeepsOnlyTheNewestAndSilencesAreKeptToo()
    {
        for (int i = 0; i < AutonomousChatWhy.Capacity + 50; i++)
            AutonomousChatWhy.Record(Said(i + 1, "Bot" + i, i));
        AutonomousChatWhy.RecordSilence(eRealm.Albion, "say", "player Wynn", "none ack", 0, "an acknowledgement draws no answer", "Wynn");
        Assert.That(AutonomousChatWhy.Recent(int.MaxValue).Count, Is.EqualTo(AutonomousChatWhy.Capacity));
        AutonomousChatWhy.Decision newest = AutonomousChatWhy.Recent(1)[0];
        Assert.That(newest.Outcome, Is.EqualTo("silent"));
        Assert.That(AutonomousChatWhy.ForBot(1, null, 5), Is.Empty, "the oldest decisions are gone");
    }

    [Test]
    public void ADecisionReadsAsOneLine()
    {
        string line = AutonomousChatWhy.Describe(Said(7, "Aldwin", 1_000, "lol same", to: "Wynn"), 6_000);
        Assert.That(line, Does.StartWith("5s ago [faction] Aldwin said (ambient)"));
        Assert.That(line, Does.Contain("rule ambient/general score 2"));
        Assert.That(line, Does.Contain(": its turn"));
        Assert.That(line, Does.EndWith("-> \"lol same\""));
        Assert.That(AutonomousChatWhy.Describe(Said(7, "Aldwin", 0), 600_000), Does.StartWith("10m ago"));
    }

    [Test]
    public void ALinesScoreCountsTheCriteriaItNames()
    {
        var vague = new VC.Line("a", "reply", "general", [], "*", [], 1, 50, [], "", "", 1, "lol");
        var social = vague with { Intent = "thanks" };
        var specific = vague with { Answers = ["ask_where"], Realm = "alb", Needs = ["in_capital"] };
        Assert.That(VC.Specificity(null), Is.EqualTo(0));
        Assert.That(VC.Specificity(vague), Is.EqualTo(0));
        Assert.That(VC.Specificity(social), Is.EqualTo(1));
        Assert.That(VC.Specificity(specific), Is.EqualTo(4));
    }

    [TestCase("fact level", AutonomousChatReplyFit.Known)]
    [TestCase("knowledge", AutonomousChatReplyFit.Known)]
    [TestCase("live ask_hunt", AutonomousChatReplyFit.Known)]
    [TestCase("join", AutonomousChatReplyFit.Known)]
    [TestCase("thread thanks", AutonomousChatReplyFit.Specific)]
    [TestCase("thread followup_which", AutonomousChatReplyFit.Weak)]
    [TestCase("shape ask_on", AutonomousChatReplyFit.Specific)]
    [TestCase("shape ask_price", AutonomousChatReplyFit.Weak)]
    [TestCase("intent greeting", AutonomousChatReplyFit.Fits)]
    [TestCase("intent travel", AutonomousChatReplyFit.Weak)]
    [TestCase("intent banter", AutonomousChatReplyFit.Weak)]
    [TestCase("not sure", AutonomousChatReplyFit.Weak)]
    [TestCase("intent rvr -> not sure", AutonomousChatReplyFit.Weak)]
    [TestCase("none unknown", AutonomousChatReplyFit.None)]
    [TestCase(null, AutonomousChatReplyFit.None)]
    public void ARouteHasAFitScore(string route, int fit) => Assert.That(AutonomousChatReplyFit.Of(route), Is.EqualTo(fit));

    /// <summary>RC6 on the owner's PC: a friend's answer to "hey! what u up to?" was swapped for "not sure, try /advice".</summary>
    [TestCase("friend chat")]
    [TestCase("friend chat invite")]
    public void AFriendsSmallTalkAnswerPassesTheReplyBudget(string route)
    {
        Assert.That(AutonomousChatReplyFit.Of(route, "hey! what u up to?"), Is.EqualTo(AutonomousChatReplyFit.Known));
        Assert.That(AutonomousChatReplyBudget.Decide(AutonomousChatReplyFit.Of(route, "hey! what u up to?"), true, AutonomousChatBudget.MinReplyFit),
            Is.EqualTo(AutonomousChatReplyBudget.Outcome.Answer));
    }

    [Test]
    public void AGroupRequestFitsOnlyWhenAGroupIsForming()
    {
        Assert.That(AutonomousChatReplyFit.Of("intent grouping", "lfm vendo need healer pst"), Is.EqualTo(AutonomousChatReplyFit.Fits));
        Assert.That(AutonomousChatReplyFit.Of("intent grouping", "lfg 34 bm"), Is.EqualTo(AutonomousChatReplyFit.Weak));
        Assert.That(AutonomousChatReplyFit.Of("intent grouping", "inv pls"), Is.EqualTo(AutonomousChatReplyFit.Weak));
    }
}
