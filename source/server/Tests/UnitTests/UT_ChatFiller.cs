using System;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2, cutting the filler: musings, unasked Exchange plugs, generic acks, tidy capitals.</summary>
[TestFixture]
public class UT_ChatFiller
{
    private static VC.Line Line(string kind, string topic, string text, params string[] needs) =>
        new("x", kind, topic, [], "*", [], 1, 50, needs, "", "", 1, text);

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    [Test]
    public void LoreMusingsAreOnlyARareFactionOpener()
    {
        VC.Line lore = Line("ambient", "lore", "heard lady nimue still walks the marsh");
        VC.Line musing = Line("ambient", "banter", "the bind stone and i are on first name terms");
        // Round 3: a "general" topic line counts as crafted; a short grouping line is plain.
        VC.Line plain = Line("ambient", "grouping", "anyone want to duo near prydwen");
        Assert.That(AutonomousChatFiller.IsMusing(lore) && AutonomousChatFiller.IsMusing(musing));
        Assert.That(AutonomousChatFiller.IsMusing(plain), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(lore, true, new FixedRandom(0.01)), Is.False, "round 5: no lore recitation unprompted");
        Assert.That(AutonomousChatFiller.AmbientAllowed(musing, true, new FixedRandom(0.01)), Is.True, "another musing now and then in /faction");
        Assert.That(AutonomousChatFiller.AmbientAllowed(musing, true, new FixedRandom(0.1)), Is.False, "rarely: 5% (round 2)");
        Assert.That(AutonomousChatFiller.AmbientAllowed(lore, true, new FixedRandom(0.5)), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(lore, false, new FixedRandom(0.0)), Is.False, "never in /say or /broadcast");
        Assert.That(AutonomousChatFiller.AmbientAllowed(plain, false, new FixedRandom(0.9)), Is.True);
        Assert.That(AutonomousChatFiller.ReplyAllowed(Line("reply", "lore", "the old stories say so"), null), Is.False, "never as a reply");
    }

    [Test]
    public void TheExchangeIsNamedOnlyToSomeoneAskingWhereToBuyOrSell()
    {
        VC.Line plug = Line("reply", "general", "try the exchange, prices are fair");
        using (var asked = AutonomousChatFiller.Replying("ty for rez, how much xp did i lose?", "c", 0))
            Assert.That(AutonomousChatFiller.ReplyAllowed(plug, asked), Is.False);
        using (var asked = AutonomousChatFiller.Replying("where can i sell this sword?", "c", 0))
            Assert.That(AutonomousChatFiller.ReplyAllowed(plug, asked), Is.True);
        Assert.That(AutonomousChatFiller.AmbientAllowed(Line("ambient", "money", "the exchange prices tonight are wild"), true, new FixedRandom(0)), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(Line("trade", "", "selling {item} on the exchange, fair price", "has_listing"), true, new FixedRandom(0)),
            Is.True, "a seller's own listing (the owner's Exchange feature) stays");
        Assert.That(AutonomousChatFiller.AsksWhereToBuyOrSell("anyone buying?"), Is.True);
        Assert.That(AutonomousChatFiller.AsksWhereToBuyOrSell("selling mc chain 3p"), Is.False);
    }

    [Test]
    public void GenericFillerIsNeverSaid()
    {
        // Beta 1.1.2, round 2: the spot-the-bot judge's most common reason; removed from replies and openers.
        foreach (string text in new[] { "good question", "Happy to help.", "sounds familiar :)", "Ha yes, this exactly.", "could be, tell me more",
                     "yeah that is about right :D", "just thinking out loud", "sure thing", "hey!! how is everyone doing tonight" })
            Assert.That(AutonomousChatFiller.IsGeneric(text), Is.True, text);
        Assert.That(AutonomousChatFiller.IsGeneric("try the spiders by prydwen"), Is.False);
        VC.Line generic = Line("reply", "general", "good question");
        using (var scope = AutonomousChatFiller.Replying("is df open?", "faction:Albion:filler-test", 1_000))
            Assert.That(AutonomousChatFiller.ReplyAllowed(generic, scope), Is.False);
        Assert.That(AutonomousChatFiller.ReplyAllowed(generic, null), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(Line("ambient", "general", "just thinking out loud"), true, new FixedRandom(0)), Is.False);
    }

    [Test]
    public void AReplyScopeEndsWhenDisposed()
    {
        Assert.That(AutonomousChatFiller.Current, Is.Null);
        using (AutonomousChatFiller.Replying("a", "k", 0))
        {
            using (AutonomousChatFiller.Replying("b", "k", 0))
                Assert.That(AutonomousChatFiller.Current.Incoming, Is.EqualTo("b"));
            Assert.That(AutonomousChatFiller.Current.Incoming, Is.EqualTo("a"));
        }
        Assert.That(AutonomousChatFiller.Current, Is.Null);
    }

    [Test]
    public void NearCopiesCountAsOneAnswer()
    {
        var scheduled = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.That(AutonomousBotChatCoordinator.TryAddDistinctResponse(scheduled, "Ha yes, this exactly.", out _), Is.True);
        Assert.That(AutonomousBotChatCoordinator.TryAddDistinctResponse(scheduled, "Heh yep, this exactly", out _), Is.False);
    }

    [Test]
    public void NoTemplateSaysASingularSubjectAre()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using var reader = new System.IO.StreamReader(assembly.GetManifestResourceStream(name));
            return reader.ReadToEnd();
        }
        VC corpus = VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        Assert.That(corpus.Lines.Where(line => line.Text.Contains("a kobold with a grudge are") || line.Text.Contains(", any alb who")), Is.Empty);
        Assert.That(corpus.Voices.Single(voice => voice.Name == "proper").Share, Is.LessThan(0.1), "tidy writers are few");
    }
}
