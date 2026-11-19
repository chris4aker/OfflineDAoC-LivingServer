using System.Linq;
using DOL.GS.LoreChannels;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>The capital loremasters' keyword dialogue (Living Server 1.2, LoremasterDialogue).</summary>
[TestFixture]
public class UT_LoreChannelDialogue
{
    private static LoremasterDialogue Albion() => new(LoreChannelFixtures.Library(), LoreRealm.Albion, "Loremaster Aldwin");

    [Test]
    public void GreetingOffersPlacesAndBooksAsLinks()
    {
        string text = Albion().Greet("Tester").Text;
        Assert.That(text, Does.Contain("[places]").And.Contain("[books]").And.Contain("Tester"));
    }

    [Test]
    public void PlacesListTheRealmsOwnAndTheNeutralOnesOnly()
    {
        string text = Albion().Resolve("places").Text;
        Assert.That(text, Does.Contain("[Krondon]").And.Contain("[Caer Sidi]").And.Contain("[Darkness Falls]"));
        Assert.That(text, Does.Not.Contain("Trollheim").And.Not.Contain("Tur Suil"));
        Assert.That(Albion().Resolve("Trollheim"), Is.Null, "an Albion loremaster knows no Midgard place");
        Assert.That(new LoremasterDialogue(LoreChannelFixtures.Library(), LoreRealm.Midgard, "S").Resolve("places").Text,
            Does.Contain("[Trollheim]").And.Contain("[Darkness Falls]").And.Not.Contain("Krondon"));
    }

    [TestCase("Krondon")] [TestCase("krondon")] [TestCase("  KRONDON ")]
    public void APlaceAnswersWithItsIntroItsBossesAndItsBook(string keyword)
    {
        LoremasterReply reply = Albion().Resolve(keyword);
        Assert.That(reply.LocationId, Is.EqualTo("krondon"));
        Assert.That(reply.Text, Does.StartWith("Krondon? The [names] are old"));
        Assert.That(reply.Text, Does.Contain("[storm-keeper]"));
        Assert.That(reply.Text, Does.Contain("[Orylle]"), "a boss the intro does not link is offered after it");
        Assert.That(reply.Text, Does.Not.Contain("[Scurceol Hyrde]"), "the intro already links him by his topic keyword");
        Assert.That(reply.Text, Does.Contain("[book of Krondon]"));
    }

    [Test]
    public void ALinkThatLeadsNowhereLosesItsBrackets()
    {
        string text = Albion().Resolve("Krondon").Text;
        Assert.That(text, Does.Contain(" nowhere.").And.Not.Contain("[nowhere]"));
        var dialogue = Albion();
        foreach (string link in LoremasterDialogue.Links(text))
            Assert.That(dialogue.Knows(link), Is.True, link);
    }

    [Test]
    public void BossTopicsResolveByKeywordAndByName()
    {
        LoremasterReply byKeyword = Albion().Resolve("storm-keeper");
        LoremasterReply byName = Albion().Resolve("Scurceol Hyrde");
        Assert.That(byKeyword.Text, Does.StartWith("Of Scurceol Hyrde: the storm-keeper."));
        Assert.That(byName.Text, Is.EqualTo(byKeyword.Text));
        Assert.That(byKeyword.LocationId, Is.EqualTo("krondon"));
        Assert.That(Albion().Resolve("Orylle").Text, Is.EqualTo("Kill Orylle's adds first."), "no topic: the boss's legend");
    }

    [Test]
    public void ASharedTopicAnswersForThePlaceTheTalkIsAbout()
    {
        var dialogue = Albion();
        Assert.That(dialogue.Resolve("names", "caer-sidi").Text, Is.EqualTo("Caer Sidi's names are Welsh."));
        Assert.That(dialogue.Resolve("names", "krondon").Text, Does.StartWith("Old words."));
        Assert.That(dialogue.Resolve("names").Text, Is.Not.Null);
    }

    [Test]
    public void BooksListEachPlaceAndABookKeywordOffersIt()
    {
        var dialogue = Albion();
        string list = dialogue.Resolve("books").Text;
        Assert.That(list, Does.Contain("[book of Krondon]").And.Contain("[book of Darkness Falls]").And.Not.Contain("Trollheim"));
        LoremasterReply offer = dialogue.Resolve("book of Krondon");
        Assert.That(offer.BookLocationId, Is.EqualTo("krondon"));
        Assert.That(offer.Text, Does.Contain("Of the Hall under the Hill").And.Contain("3 pages"));
        Assert.That(dialogue.Resolve("book of Trollheim"), Is.Null);
    }

    [Test]
    public void UnknownWordsAndAnEmptyBibleAreHandled()
    {
        Assert.That(Albion().Resolve("dragons of the moon"), Is.Null);
        Assert.That(Albion().Resolve(""), Is.Null);
        var empty = new LoremasterDialogue(LoreLibrary.Empty, LoreRealm.Hibernia, "Loremaster Eithne");
        Assert.That(empty.Resolve("places").Text, Does.Contain("bare"));
        Assert.That(empty.Greet("x").Text, Does.Contain("Hibernia"));
    }

    [Test]
    public void LongAnswersAreSaidInPiecesAtSentenceEnds()
    {
        string text = string.Join(" ", Enumerable.Repeat("A sentence of some length that goes on.", 40));
        var pieces = LoremasterNPC.Pieces(text).ToList();
        Assert.That(pieces.Count, Is.GreaterThan(1));
        Assert.That(pieces.All(p => p.Length <= 480), Is.True);
        Assert.That(pieces.Take(pieces.Count - 1).All(p => p.EndsWith(".")), Is.True);
        Assert.That(string.Join(" ", pieces), Is.EqualTo(text));
    }

    [Test]
    public void EachRealmHasOneLoremasterInItsCapital()
    {
        Assert.That(Loremasters.Definitions.Select(d => d.Realm), Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(Loremasters.Definitions.Select(d => (int)d.RegionId), Is.EquivalentTo(new[] { 10, 101, 201 }));
        Assert.That(Loremasters.Definitions.Select(d => d.Name).Distinct().Count(), Is.EqualTo(3));
    }
}
