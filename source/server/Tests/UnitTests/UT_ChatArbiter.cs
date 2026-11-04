using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Intent = DOL.GS.eAutonomousChatIntent;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2, arbitration and timing (AutonomousChatArbiter): one primary answer, capped pile-ons, typed replies.</summary>
[TestFixture]
public class UT_ChatArbiter
{
    [Test]
    public void AQuestionGetsOneAnswerAndAHelloAFewMore()
    {
        var random = new Random(1);
        Assert.That(AutonomousChatArbiter.Arrange(3, Intent.Unknown, "fact level", false, true, 50, 2, random).Total, Is.EqualTo(1));
        Assert.That(AutonomousChatArbiter.Arrange(3, Intent.Greeting, "intent greeting", false, false, 50, 2, random),
            Is.EqualTo(new AutonomousChatArbiter.Plan(1, 2, 0)));
        Assert.That(AutonomousChatArbiter.Arrange(3, Intent.Greeting, "intent greeting", true, false, 50, 2, random).Total, Is.EqualTo(1),
            "said to one bot by name");
        Assert.That(AutonomousChatArbiter.Arrange(0, Intent.Laugh, "intent laugh", false, false, 50, 2, random).Total, Is.EqualTo(0),
            "a line nobody answers gets no pile-on either");
        Assert.That(AutonomousChatArbiter.Arrange(5, Intent.Greeting, "intent greeting", false, false, 50, 2, random).Total, Is.LessThanOrEqualTo(3),
            "never more than the cap");
        Assert.That(AutonomousChatArbiter.Arrange(3, Intent.Greeting, "intent greeting", false, false, 2, 2, random).Total, Is.EqualTo(2),
            "no more than can hear it");
        var mishap = AutonomousChatArbiter.Arrange(2, Intent.Banter, "shape player_mishap", false, false, 50, 2, random);
        Assert.That(mishap.Reactions, Is.EqualTo(1));
        Assert.That(AutonomousChatArbiter.ReactionKind("shape player_mishap", Intent.Banter), Is.EqualTo("laugh"));
        Assert.That(AutonomousChatArbiter.ReactionKind("shape ask_opinion", Intent.Unknown), Is.EqualTo("agree"));
        Assert.That(AutonomousChatArbiter.ReactionKind("fact level", Intent.Unknown), Is.Null);
        Assert.That(new[] { "laugh", "cheer", "agree" }.Select(kind => AutonomousChatArbiter.Reaction(kind, random)).All(text => text.Length <= 8));
    }

    [Test]
    public void RepliesAreReadThoughtAndTypedSoLongOnesTakeLonger()
    {
        var random = new Random(2);
        int ack = AutonomousChatArbiter.DelayMs("Brannoc?", "?", 40, 0, random);
        Assert.That(ack, Is.InRange(800, 3_000), "an acknowledgement comes in a couple of seconds");
        const string sentence = "try the bone camp in muspelheim, good xp at 25";
        int slow = 0;
        for (int i = 0; i < 200; i++)
            if (AutonomousChatArbiter.DelayMs("where should i level", sentence, 25, 0, random) > AutonomousChatArbiter.DelayMs("where should i level", sentence, 60, 0, random))
                slow++;
        Assert.That(slow, Is.GreaterThan(190), "a slow typist takes longer");
        int typed = AutonomousChatArbiter.DelayMs("x", sentence, 40, 0, new Random(3));
        Assert.That(typed, Is.InRange(13_500, 18_000), "46 characters at 40 wpm (200 a minute) is about 14 s of typing");
        int next = AutonomousChatArbiter.DelayMs("x", "lol", 60, typed, random);
        Assert.That(next, Is.GreaterThan(typed + 700), "replies stay in order, a moment apart");
        Assert.That(AutonomousChatArbiter.DelayMs(new string('a', 1_000), "k", 40, 0, random), Is.LessThan(7_000), "reading is capped");
    }

    private sealed record Line(string id, string channel, string context, string text, string expect, string note);

    /// <summary>
    /// Responders per player line over the blind round 1 corpus's /faction, /say and /lfg lines (an approximate model: the
    /// audit route and its budget, PlayerReplyCount and the arbiter, with every bot free). Before = Beta 1.1.1 (PlayerReplyCount
    /// with no budget). Set TALK_RESPONDERS_OUT to keep the report.
    /// </summary>
    [Test]
    public void RespondersPerPlayerLineStayFewAndQuestionsGetOneAnswer()
    {
        string name = typeof(UT_ChatArbiter).Assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("chat_audit_player_lines.json", StringComparison.Ordinal));
        Assert.That(name, Is.Not.Null);
        // TALK_AUDIT_IN: another labelled line file (the blind round 1 corpus) instead of the audit set.
        string input = Environment.GetEnvironmentVariable("TALK_AUDIT_IN");
        ChatAuditRoute.Item[] all = string.IsNullOrEmpty(input) ? ChatAuditRoute.Load() : JsonSerializer.Deserialize<ChatAuditRoute.Item[]>(File.ReadAllText(input));
        ChatAuditRoute.Item[] items = all.Where(item => item.channel is "faction" or "say").ToArray();
        var random = new Random(9);
        int lines = 0, beforeTotal = 0, afterTotal = 0, beforeMax = 0, afterMax = 0, questionsMulti = 0;
        foreach (ChatAuditRoute.Item item in items)
        {
            lines++;
            bool faction = item.channel == "faction";
            Intent intent = AutonomousChatIntentModel.Predict(item.text);
            int wanted = AutonomousBotChatCoordinator.PlayerReplyCount(intent, faction, false, 300, random);
            beforeTotal += wanted;
            beforeMax = Math.Max(beforeMax, wanted);
            (string path, _) = ChatAuditRoute.Route(item, new Random(1));
            (string outcome, _, _) = ChatAuditRoute.Budget(item, path, random);
            int after = outcome == "silent" ? 0
                : AutonomousChatArbiter.Arrange(wanted, intent, path, false, AutonomousChatQuestions.IsQuestion(item.text), 300,
                    AutonomousChatBudget.Current.PileOnCap, random).Total;
            if (AutonomousChatQuestions.IsQuestion(item.text) && after > 1)
                questionsMulti++;
            afterTotal += after;
            afterMax = Math.Max(afterMax, after);
        }
        string report = $"Responders per player line ({lines} /faction and /say lines ): before {beforeTotal / (double)lines:0.00} " +
                        $"(max {beforeMax}), after {afterTotal / (double)lines:0.00} (max {afterMax}); questions with more than one answer after: {questionsMulti}";
        string path2 = Environment.GetEnvironmentVariable("TALK_RESPONDERS_OUT");
        if (!string.IsNullOrEmpty(path2))
            File.WriteAllText(path2, report + "\n");
        TestContext.Out.WriteLine(report);
        Assert.That(afterMax, Is.LessThanOrEqualTo(1 + AutonomousChatBudget.Current.PileOnCap));
        Assert.That(questionsMulti, Is.EqualTo(0));
    }
}
