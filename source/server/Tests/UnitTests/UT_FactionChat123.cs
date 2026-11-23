using System;
using System.Linq;
using NUnit.Framework;
using CC = DOL.GS.AutonomousChatClassChoice;
using Field = DOL.GS.AutonomousChatFieldTalk;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.3, from the owner's game on 1.2.2 ("broken ass chat"), in /faction:
/// <code>
/// Dumbledork: where do I choose advanced class          (no bot answered)
/// (Spellslinger92) puny skeletons drop anything good?
/// (Kludax) dunno havent camped them
/// (a bot) nobody has died yet, lets keep it that way   (party talk in /faction, answered "heh")
/// (Wilenric) nice pulls everyone thats how it should be
/// </code>
/// Tests only.
/// </summary>
[TestFixture]
public class UT_FactionChat123
{
    [TestCase("where do I choose advanced class", true)]
    [TestCase("where do i pick my class?", true)]
    [TestCase("when do i get my advanced class?", true)]
    [TestCase("how do i become a cleric?", true)]
    [TestCase("where do i spec into theurgist", true)]
    [TestCase("how do i get to camelot", false)]
    [TestCase("whats my spec", false)]
    [TestCase("is friar good?", false)]
    [TestCase("how do i become a fighter", false)]
    public void AQuestionOfPickingTheClassIsRead(string line, bool asks) =>
        Assert.That(CC.AsksAboutClassChoice(line), Is.EqualTo(asks));

    [Test]
    public void ALevelFiveElementalistHearsItsOptionsAndWhere()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            string answer = CC.Answer("where do I choose advanced class", eRealm.Albion, "Elementalist", 5, "Camelot", new Random(seed));
            Assert.That(answer, Does.Contain("wizard").Or.Contain("theurgist"));
            Assert.That(answer, Does.Contain("Camelot").Or.Contain("at 5"));
            Assert.That(answer, Does.Not.Contain("dunno").And.Not.Contain("not sure"));
        }
    }

    [Test]
    public void ANamedClassGetsItsTrainerOrWhyNot()
    {
        var random = new Random(1);
        Assert.Multiple(() =>
        {
            Assert.That(CC.Answer("how do i become a cleric?", eRealm.Albion, "Acolyte", 5, "Camelot", random), Does.Contain("cleric trainer"));
            Assert.That(CC.Answer("how do i become a druid?", eRealm.Hibernia, "Guardian", 5, "Tir na Nog", random),
                Does.Contain("naturalist").And.Contain("cant").Or.Contain("not guardian"));
            Assert.That(CC.Answer("how do i become a healer", eRealm.Albion, "Acolyte", 5, "Camelot", random), Does.Contain("cant train").Or.Contain("midgard").Or.Contain("mid"),
                "another realm's class");
            Assert.That(CC.Answer("how do i become a cleric?", eRealm.Albion, "Friar", 12, "Camelot", random), Does.Contain("friar").And.Not.Contain("cleric trainer"));
            Assert.That(CC.Answer("how do i pick my class?", eRealm.Midgard, "Rogue", 5, "Jordheim", random), Does.Contain("hunter").And.Contain("shadowblade"),
                "Midgard's rogue base");
        });
    }

    [Test]
    public void TheClassTreeHasOnlyTheEraClasses()
    {
        string[] classes = CC.Bases.Values.SelectMany(b => b.Classes).ToArray();
        Assert.That(classes, Has.None.AnyOf("Heretic", "Valkyrie", "Warlock", "Bainshee", "Vampiir", "Sluaghbinder", "Mauler"));
        Assert.That(classes.Where(c => AutonomousChatGear.ClassOf(c) == null), Is.Empty, "every class is one the chat's class table knows");
    }

    [TestCase("dunno, havent camped them", true)]
    [TestCase("dunno havent camped them", true)]
    [TestCase("idk", true)]
    [TestCase("no idea sry", true)]
    [TestCase("never camped it", true)]
    [TestCase("mostly junk", false)]
    [TestCase("coin mostly lol", false)]
    [TestCase("didnt see it", false)]
    public void ABotsDunnoIsANonAnswer(string text, bool nonAnswer) =>
        Assert.That(Field.IsNonAnswer(text), Is.EqualTo(nonAnswer));

    [Test]
    public void ABotNeverAnswersAnotherWithDunno()
    {
        var said = new Field.Said("puny skeletons drop anything good?", "camp", ["mostly junk", "coin mostly lol", "now and then", "dunno, havent camped them"])
            { Asks = true };
        var onlyDunno = new Field.Said("puny skeletons drop anything good?", "camp", ["dunno, havent camped them", "dunno"]) { Asks = true };
        for (int seed = 0; seed < 100; seed++)
        {
            string answer = Field.Answer(said, null, null, new Random(seed));
            Assert.That(answer == null || !Field.IsNonAnswer(answer), answer);
            Assert.That(Field.Answer(onlyDunno, null, null, new Random(seed)), Is.Null, "nothing to say: nothing said");
        }
    }

    [Test]
    public void GroupTalkStaysOutOfFaction()
    {
        VC.Line Line(string text, string topic, params string[] needs) => new("t", "ambient", topic, [], "*", [], 1, 50, needs, "", "", 1, text);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousChatFiller.SaidToTheGroup(Line("nobody has died yet, lets keep it that way", "group", "in_group")), Is.True);
            Assert.That(AutonomousChatFiller.SaidToTheGroup(Line("nice pulls everyone, thats how it should be", "group", "in_group")), Is.True);
            Assert.That(AutonomousChatFiller.SaidToTheGroup(Line("sorry all, my bad", "death", "in_group")), Is.True);
            Assert.That(AutonomousChatFiller.SaidToTheGroup(Line("late night groups are the best, nobody rushes", "latenight", "in_group")), Is.False);
            Assert.That(AutonomousChatFiller.SaidToTheGroup(Line("long day, short session, lets make it count", "life")), Is.False, "not a group line");
            Func<VC.Line, bool> faction = AutonomousChatFiller.AmbientFilter(true, new Random(0));
            Assert.That(faction(Line("nobody has died yet, lets keep it that way", "group", "in_group")), Is.False);
        });
    }
}
