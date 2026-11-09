using System;
using System.Linq;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Recall = DOL.GS.AutonomousChatRecall;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, chat after RC10 on the owner's PC (2026-11-09, 14:51-15:40), read line by line: "which stable for master visur?" from
/// a bot taking the porter, "grinding city of camelot at 50" from a bot back at its bind, "got one ty" and later "nah, ended up
/// soloing" from the same asker, and "how do i get to pennine mountains from here" answered "pennine mountains? thats in albion".
/// </summary>
[TestFixture]
public class UT_ChatAfterRC10
{
    private static readonly Field.WorldView Quiet = new([], [], [], [], 0, 0, "Forest Sauvage");

    private static Field.Facts Facts(string zone, bool inTown) =>
        new(1, eRealm.Albion, "Necromancer", 50, zone, null, false, false, inTown, false, 1, false, false, false, false, null, null, 0, false,
            null, 3, 15, Field.Fills("Necromancer", "healer"), Quiet);

    [TestCase("Master Visur", false)]
    [TestCase("Channeler Glasny", false)]
    [TestCase("Stor Gothi Annark", false)]
    [TestCase("Glasny", false)]
    [TestCase("Odin Hib", false)]
    [TestCase("Emain Alb", false)]
    [TestCase("Home Mid", false)]
    [TestCase("Wearyall Village", true)]
    [TestCase("Snowdonia Fortress", true)]
    [TestCase("Caer Gothwaite", true)]
    [TestCase("Mularn", true)]
    public void APorterIsNoPlace(string destination, bool place) =>
        Assert.That(AutonomousBotVoices.IsPlaceName(destination), Is.EqualTo(place));

    [Test]
    public void NobodyGrindsATown()
    {
        var random = new Random(5);
        for (int i = 0; i < 3000; i++)
        {
            Field.Said said = Field.Line("faction", Facts("City of Camelot", inTown: true) with { Id = i }, null, random);
            Assert.That(said?.Text ?? "", Does.Not.Contain("grinding city of camelot").And.Not.Contain("grinding camelot"), said?.Text);
        }
        bool heard = false;
        for (int i = 0; i < 3000 && !heard; i++)
            heard = Field.Line("faction", Facts("Pennine Mountains", inTown: false) with { Id = i }, null, random)?.Text?.StartsWith("grinding pennine") == true;
        Assert.That(heard, Is.True, "out in a zone the line is still said");
    }

    [Test]
    public void ABotInTownAsksForAGroupToHeadOutWith()
    {
        var random = new Random(9);
        Field.Facts town = Facts("City of Camelot", inTown: true) with { Level = 41 };
        for (int i = 0; i < 8000; i++)
        {
            string text = Field.Line(i % 2 == 0 ? "faction" : "guild", town with { Id = i }, null, random)?.Text ?? "";
            Assert.That(text, Does.Not.Match(@"(camp|camps|xp|xping|duo|group|grp|quest|port|guildies|more)\b[^,?]* (in|near|to|for|at) (city of )?camelot|" +
                                             @"^(city of )?camelot (anyone|camp)|around \d+\? (city of )?camelot"), text);
        }
    }

    [TestCase("lf 2 more for city of camelot", "City of Camelot", false)]
    [TestCase("where do 41s camp in city of camelot?", "City of Camelot", false)]
    [TestCase("city of camelot anyone", "City of Camelot", false)]
    [TestCase("anyone around 41? camelot", "City of Camelot", false)]
    [TestCase("grinding jordheim at 50", "Jordheim", false)]
    [TestCase("where do i bind in city of camelot?", "City of Camelot", true)]
    [TestCase("back in camelot", "City of Camelot", true)]
    [TestCase("lf 2 more for camelot hills", "Camelot Hills", true)]
    [TestCase("anyone selling dye in tir na nog?", "Tir na Nog", true)]
    public void ACapitalIsNoPlaceToHunt(string text, string zone, bool fits) =>
        Assert.That(Field.FitsCapital(text, zone), Is.EqualTo(fits));

    [TestCase("got one ty", true)]
    [TestCase("ty, inv", true)]
    [TestCase("kk inv", true)]
    [TestCase("inv sent", true)]
    [TestCase("nobody? ok lol", false)]
    [TestCase("ty", false)]
    [TestCase("still looking", false)]
    public void AnAskerThatFoundOneSaysSo(string text, bool found) => Assert.That(Recall.SaysFound(text), Is.EqualTo(found));

    [Test]
    public void AFoundGroupIsNotAskedAfter()
    {
        var store = new Recall.Store();
        store.Add(new Recall.Note(Recall.Kind.AskedGroup, 7, "Ishimissa", "healers", 1_000));
        store.Add(new Recall.Note(Recall.Kind.AskedCamp, 7, "Ishimissa", "camp", 1_000));
        store.Add(new Recall.Note(Recall.Kind.AskedGroup, 8, "Other", "grp", 1_000));
        store.Settle(7, Recall.Kind.AskedGroup);
        Assert.That(store.Notes.Select(note => (note.BotId, note.Kind)),
            Is.EquivalentTo(new[] { (7L, Recall.Kind.AskedCamp), (8L, Recall.Kind.AskedGroup) }));
    }

    [TestCase("hey, im {level}, you?", "50 lf xp grp", false)]
    [TestCase("hey, im {level}, you?", "lfg", true)]
    [TestCase("hey, want to duo here? im {level} {class}", "50 lf xp grp", true)]
    [TestCase("your level looks close to mine, want to group up?", "lf grp 50 theurg", true)]
    public void AnOfferDoesntAskTheLevelThePostGave(string line, string post, bool fits) =>
        Assert.That(AutonomousLivingServer.OfferFitsPost(line, post), Is.EqualTo(fits));

    [TestCase("we keep going after the same mobs lol, want to group?", true, false, false)]
    [TestCase("we keep going after the same mobs lol, want to group?", true, true, true)]
    [TestCase("hey, want to duo here? im {level} {class}", true, false, false)]
    [TestCase("you looking for a group? im {level}, same area", true, false, true)]
    [TestCase("you looking for a group? im {level}, same area", false, false, false)]
    [TestCase("want to group? xp is better with two", false, false, true)]
    public void AFarOfferDoesntSayItIsNear(string line, bool sameZone, bool close, bool fits) =>
        Assert.That(AutonomousLivingServer.OfferFits(line, sameZone, close), Is.EqualTo(fits));

    [TestCase("Pennine Mountains", "Albion", true)]
    [TestCase("Cornwall", "midgard", true)]
    [TestCase("Camelot Hills", "Camelot Hills", true)]
    [TestCase("Caer Diogel", "Aldland", false)]
    [TestCase("Wearyall Village", "Avalon Marsh", false)]
    public void TheRealmIsNoAnswerToWhereAZoneIs(string name, string area, bool saysNothing) =>
        Assert.That(AutonomousChatKnowledge.AreaSaysNothing(name, area), Is.EqualTo(saysNothing));
}
