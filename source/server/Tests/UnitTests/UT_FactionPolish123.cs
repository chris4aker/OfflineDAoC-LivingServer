using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ex = DOL.GS.AutonomousChatExchange;
using Field = DOL.GS.AutonomousChatFieldTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.3, from the owner's game on 1.2.2, in the chat window (/trade and /faction together):
/// <code>
/// [Trade] Aldae: any1 selling studded legs
/// (Zhoxum) &lt;Faction&gt; not atm sry                          (a thread answer, read as the answer to /trade)
/// (Zhoxum) &lt;Faction&gt; this group is solid, glad i stuck around lol
/// (Eadric) &lt;Faction&gt; almost 6                             (an opener that reads as an answer to nothing)
/// (Roseiselle) &lt;Faction&gt; nothing humbles you like dying to something green heh
/// (Zhoxum) &lt;Faction&gt; camelot hills camp for 4? heh       (Zhoxum's third line of nine)
/// </code>
/// Tests only.
/// </summary>
[TestFixture]
public class UT_FactionPolish123
{
    [Test]
    public void AShortAnswerWithLinesBetweenNamesItsAsker()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Field.NamedWhenApart("not atm sry", "Spellslinger92", 2), Is.EqualTo("Spellslinger92, not atm sry"));
            Assert.That(Field.NamedWhenApart("not atm sry", "Spellslinger92", 1), Is.EqualTo("not atm sry"), "right under its question");
            Assert.That(Field.NamedWhenApart("Spellslinger92 not atm", "Spellslinger92", 3), Is.EqualTo("Spellslinger92 not atm"), "already named");
            Assert.That(Field.NamedWhenApart("try camelot hills, a bunch of 5s camp there", "Eadwynn", 3),
                Is.EqualTo("try camelot hills, a bunch of 5s camp there"), "a long answer says what it answers");
        });
    }

    [Test]
    public void GroupTalkFromTheCorpusStaysOutOfFaction()
    {
        VC corpus = AutonomousBotVoices.Corpus;
        VC.Line[] lines = corpus.Lines.Where(line => line.Text.Contains("glad i stuck around", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.That(lines, Is.Not.Empty);
        Assert.That(lines.Where(line => !AutonomousChatFiller.SaidToTheGroup(line)).Select(line => line.Text), Is.Empty);
        for (int seed = 0; seed < 20; seed++)
        {
            Func<VC.Line, bool> faction = AutonomousChatFiller.AmbientFilter(true, new Random(seed));
            Assert.That(lines.Where(faction).Select(line => line.Text), Is.Empty);
        }
    }

    [TestCase("camelot hills camp for 4?", false)]
    [TestCase("anyone xping in camelot hills?", false)]
    [TestCase("nothing humbles you like dying to something green", false)]
    [TestCase("this group is a good one, glad i stuck around", true)]
    public void AQuestionAndADeathTakeNoLaugh(string text, bool takes) =>
        Assert.That(VC.TakesEmote(null, text), Is.EqualTo(takes));

    [Test]
    public void NoStyleEndsAQuestionOnALaugh()
    {
        var laughing = new VC.Voice("laugher", 1, false, false, false, [], true, [], new Dictionary<string, string>(), ["heh", "lol", ":)"], 1.0, 0);
        for (int seed = 0; seed < 200; seed++)
        {
            string styled = VC.Style("camelot hills camp for 4?", laughing, new Random(seed), out _, null);
            Assert.That(styled.TrimEnd(), Does.EndWith("?"), styled);
            string idiolect = AutonomousChatIdiolect.Apply("camelot hills camp for 4?", seed, false, new Random(seed));
            Assert.That(idiolect, Does.Not.EndWith("lol"), idiolect);
            string dying = AutonomousChatIdiolect.Apply("nothing humbles you like dying to something green", seed, false, new Random(seed));
            Assert.That(dying, Does.Not.EndWith("lol"), dying);
        }
    }

    [Test]
    public void ABotThatJustSpokeStartsNothingNew()
    {
        long now = 1_000_000;
        var opener = new Ex.Source("field", 9, 0, "asker");
        var answer = new Ex.Source("thread", 9, 1, "answerer");
        Assert.Multiple(() =>
        {
            Assert.That(Ex.JustSpoke(laneSeq: 9, speakerSeq: 7, Ex.SpeakerShareWindow, now, now - 5_000), Is.True, "two lines ago");
            Assert.That(Ex.JustSpoke(9, 5, Ex.SpeakerShareWindow, now, now - 5_000), Is.False, "four lines ago");
            Assert.That(Ex.JustSpoke(9, 0, Ex.SpeakerShareWindow, now, now - 5_000), Is.False, "never spoke");
            Assert.That(Ex.JustSpoke(9, 8, Ex.SpeakerShareWindow, now, now - Ex.SpeakerShareQuietMs - 1), Is.False, "a quiet channel");
            Assert.That(Ex.SinkHold(opener, false, true, now, now - 20_000, long.MinValue / 2, speakerJustSpoke: true), Is.EqualTo("hog"));
            Assert.That(Ex.SinkHold(answer, false, true, now, now - 20_000, long.MinValue / 2, speakerJustSpoke: true), Is.Null,
                "an answer in a thread still goes");
            Assert.That(Ex.SinkHold(new Ex.Source("player", 9, 1, "answerer"), false, true, now, now - 20_000, long.MinValue / 2, speakerJustSpoke: true),
                Is.Null, "a line to the player always goes");
        });
    }

    [Test]
    public void ALevelOpenerSaysWhatItCounts()
    {
        Field.Facts Facts(int bubbles) => new(1, eRealm.Albion, "Wizard", 5, "Camelot Hills", null, false, false, false, false, 1, false, false,
            false, true, null, null, 0, false, null, bubbles, 20, false, Field.WorldView.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(Field.LevelProgressTexts(Facts(2)), Has.None.EqualTo("almost 6"));
            Assert.That(Field.LevelProgressTexts(Facts(2)), Has.Some.EqualTo("almost 6, 2 bubs left"));
            Assert.That(Field.LevelProgressTexts(Facts(8)).Where(text => text.StartsWith("almost")), Is.Empty, "8 bubbles is not almost");
            Assert.That(Field.LevelProgressTexts(Facts(8)).All(text => text.Contains("bub")), Is.True);
        });
    }
}
