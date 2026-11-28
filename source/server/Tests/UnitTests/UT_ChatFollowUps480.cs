using NUnit.Framework;
using Convo = DOL.GS.ChatConversations;

namespace DOL.GS.Tests;

/// <summary>
/// Realism 2c (#480): follow-ups, nudges and corrections keep their subject. ChatConversations.Resolve
/// runs in every channel before the line is read (/faction, /say, tells, /group, a companion's /say), so these pure cases hold in
/// every channel. Tests only: not seen in a PC log.
/// </summary>
[TestFixture]
public class UT_ChatFollowUps480
{
    private const string Room = "faction:Albion";

    private static Convo.Conversation After(string setup, long at = 1_000)
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, setup, at);
        return c.In("Wynn", Room, at + 1_000);
    }

    // ---- the place in play ----------------------------------------------------------------------------------------------------------

    [TestCase("what lvl is it?", "what lvl is stonehenge barrows?")]
    [TestCase("what level is that?", "what lvl is stonehenge barrows?")]
    [TestCase("what lvl do i need for it", "what lvl is stonehenge barrows")]
    [TestCase("where?", "where is stonehenge barrows?")]
    [TestCase("never heard of it, where?", "where is stonehenge barrows?")]
    [TestCase("where exactly", "where is stonehenge barrows")]
    [TestCase("what zone is that in?", "where is stonehenge barrows?")]
    [TestCase("how far is that?", "how far is stonehenge barrows?")]
    [TestCase("ok how do i find it", "how do i get to stonehenge barrows")]
    [TestCase("whats the best way there", "how do i get to stonehenge barrows")]
    [TestCase("is it good xp?", "stonehenge barrows good for 24s?")]
    [TestCase("any good?", "stonehenge barrows good for 24s?")]
    [TestCase("is it hard?", "stonehenge barrows good for 24s?")]
    [TestCase("can i solo it?", "can a 24 solo in stonehenge barrows?")]
    [TestCase("can i solo there?", "can a 24 solo in stonehenge barrows?")]
    public void APlaceFollowUpAsksAboutThePlaceInPlay(string line, string full) =>
        Assert.That(Convo.Resolve(line, After("anyone been to the barrows?"), 24), Is.EqualTo(full), line);

    [Test]
    public void WithoutTheAskersLevelAFitQuestionAsksTheLevels()
    {
        Assert.That(Convo.Resolve("can i solo it?", After("anyone been to the barrows?")), Is.EqualTo("what lvl is stonehenge barrows?"));
        Assert.That(Convo.Resolve("is it hard?", After("anyone been to the barrows?")), Is.EqualTo("what lvl is stonehenge barrows?"));
    }

    [Test]
    public void TheOldPlaceFormsStayAsTheyWere()
    {
        Convo.Conversation c = After("anyone been to the barrows?");
        Assert.That(Convo.Resolve("how do i get there", c, 24), Is.EqualTo("how do i get to stonehenge barrows"));
        Assert.That(Convo.Resolve("is it far?", c, 24), Is.EqualTo("is stonehenge barrows far?"));
        Assert.That(Convo.Resolve("ty, where is it", c, 24), Is.EqualTo("where is stonehenge barrows"));
    }

    [TestCase("good")]
    [TestCase("good xp")]
    [TestCase("hard")]
    [TestCase("what lvl are you?")]
    [TestCase("what lvl is spindelhalla?")]
    [TestCase("where is the cleric trainer?")]
    [TestCase("lol")]
    public void ALineThatAsksNothingOrNamesItsOwnSubjectIsLeftAlone(string line) =>
        Assert.That(Convo.Resolve(line, After("anyone been to the barrows?"), 24), Is.EqualTo(line), line);

    [Test]
    public void NothingInPlayNothingCompleted()
    {
        Convo.Conversation c = After("hi all");
        foreach (string line in new[] { "what lvl is it?", "who trains them?", "who sells them?", "where?" })
            Assert.That(Convo.Resolve(line, c, 24), Is.EqualTo(line), line);
    }

    // ---- the class in play ----------------------------------------------------------------------------------------------------------

    [TestCase("who trains them?", "who trains minstrels?")]
    [TestCase("what spec for one?", "what spec for minstrel?")]
    [TestCase("what armor do they wear?", "what armor does a minstrel wear?")]
    [TestCase("are they good solo?", "are minstrels good solo?")]
    public void AClassFollowUpAsksAboutTheClassInPlay(string line, string full) =>
        Assert.That(Convo.Resolve(line, After("thinking of rolling a minstrel"), 24), Is.EqualTo(full), line);

    [Test]
    public void ARaceQuestionIsLeftAlone() =>
        // Nothing answers "what race is best for a minstrel" yet; completing it only turned silence into an unrelated class line.
        Assert.That(Convo.Resolve("what race is best for one?", After("thinking of rolling a minstrel"), 24), Is.EqualTo("what race is best for one?"));

    [Test]
    public void TheClassIsTheOneNamedNotTheAskers()
    {
        // The audit: "who trains them?" after a minstrel drew "your armsman trainer" (the asker's class).
        Assert.That(Convo.Resolve("what armor do they wear?", After("thinking of an infiltrator alt"), 24), Is.EqualTo("what armor does an infiltrator wear?"));
        Assert.That(Convo.Resolve("who trains them", After("anyone play armsman?"), 24), Is.EqualTo("who trains armsmen"));
    }

    [TestCase("minstrel", "minstrels")]
    [TestCase("armsman", "armsmen")]
    [TestCase("Mercenary", "mercenaries")]
    [TestCase("hero", "heroes")]
    [TestCase("eldritch", "eldritches")]
    public void ClassPlurals(string cls, string plural) => Assert.That(Convo.ClassPlural(cls), Is.EqualTo(plural));

    // ---- the item in play -----------------------------------------------------------------------------------------------------------

    [TestCase("who sells them?", "who sells studded helm?")]
    [TestCase("where do i get one?", "where do i get studded helm?")]
    [TestCase("who makes those?", "who makes studded helm?")]
    [TestCase("how much for it?", "how much for studded helm?")]
    public void AnItemFollowUpAsksAboutTheItemInPlay(string line, string full) =>
        Assert.That(Convo.Resolve(line, After("wtb a studded helm"), 24), Is.EqualTo(full), line);

    [Test]
    public void WhoMakesAnItemIsAnsweredLikeWhoSellsIt()
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerBuyGear(eRealm.Albion, "who makes studded helm?", out string answer), Is.True);
        Assert.That(answer, Does.Contain("armorcrafter"));
    }

    // ---- nudges ---------------------------------------------------------------------------------------------------------------------

    [TestCase("anyone?", true)]
    [TestCase("hello??", true)]
    [TestCase("nobody knows?", true)]
    [TestCase("so no one can help", true)]
    [TestCase("bump", true)]
    [TestCase("?", true)]
    [TestCase("??", true)]
    [TestCase("hello", false)]
    [TestCase("anyone selling dyes?", false)]
    [TestCase("anyone know where the cleric trainer is?", false)]
    public void WhatANudgeIs(string line, bool nudge) => Assert.That(Convo.IsNudge(line), Is.EqualTo(nudge), line);

    [Test]
    public void ANudgeReAsksTheQuestionNobodyAnswered()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "where is the cleric trainer?", 0);
        c.NotePlayer("Wynn", Room, "anyone?", 40_000);
        Assert.That(Convo.Resolve("anyone?", c.In("Wynn", Room, 40_500), 24), Is.EqualTo("where is the cleric trainer?"));
    }

    [Test]
    public void ANudgeReAsksTheCompletedQuestion()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "anyone been to the barrows?", 0);
        c.NotePlayer("Wynn", Room, "what lvl is it?", 5_000);
        c.NotePlayer("Wynn", Room, "nobody knows?", 60_000);
        Assert.That(Convo.Resolve("nobody knows?", c.In("Wynn", Room, 60_500), 24), Is.EqualTo("what lvl is stonehenge barrows?"));
    }

    [Test]
    public void ASecondNudgeStillReAsksIt()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "where is the cleric trainer?", 0);
        c.NotePlayer("Wynn", Room, "anyone?", 30_000);
        c.NotePlayer("Wynn", Room, "hello??", 70_000);
        Assert.That(Convo.Resolve("hello??", c.In("Wynn", Room, 70_500), 24), Is.EqualTo("where is the cleric trainer?"));
    }

    [Test]
    public void AnAnsweredQuestionIsNotReAsked()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "where is the cleric trainer?", 0);
        c.NoteBot("Wynn", 7, "Aldric", Room, "camelot, in the church", 8_000);
        c.NotePlayer("Wynn", Room, "anyone?", 40_000);
        Assert.That(Convo.Resolve("anyone?", c.In("Wynn", Room, 40_500), 24), Is.EqualTo("anyone?"));
    }

    [Test]
    public void ANudgeTooSoonOrTooLateIsLeftAlone()
    {
        var soon = new Convo();
        soon.NotePlayer("Wynn", Room, "where is the cleric trainer?", 0);
        soon.NotePlayer("Wynn", Room, "anyone?", 5_000);
        Assert.That(Convo.Resolve("anyone?", soon.In("Wynn", Room, 5_500), 24), Is.EqualTo("anyone?"), "an answer may still be on its way");
        var late = new Convo();
        late.NotePlayer("Wynn", Room, "where is the cleric trainer?", 0);
        late.NotePlayer("Wynn", Room, "anyone?", Convo.NudgeWindowMs + 1_000);
        Assert.That(Convo.Resolve("anyone?", late.In("Wynn", Room, Convo.NudgeWindowMs + 1_500), 24), Is.EqualTo("anyone?"));
    }

    [Test]
    public void ANudgeAfterAStatementIsLeftAlone()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "lfg 24 armsman", 0);
        c.NotePlayer("Wynn", Room, "anyone?", 40_000);
        Assert.That(Convo.Resolve("anyone?", c.In("Wynn", Room, 40_500), 24), Is.EqualTo("anyone?"));
    }

    // ---- corrections ----------------------------------------------------------------------------------------------------------------

    [Test]
    public void ACorrectionCarryingItsOwnQuestionAsksAboutWhatWasMeant()
    {
        // questions.md finding 8: this drew "lol yeah" / "heh".
        Convo.Conversation c = After("is varulvhamn good at 38?");
        Assert.That(Convo.Resolve("sorry i meant spindelhalla not varulvhamn. what lvl", c, 38), Is.EqualTo("what lvl is spindelhalla"));
    }

    [Test]
    public void ACorrectionReAsksTheLastQuestion()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "what lvl is varulvhamn?", 0);
        c.NotePlayer("Wynn", Room, "i meant spindelhalla", 6_000);
        Assert.That(Convo.Resolve("i meant spindelhalla", c.In("Wynn", Room, 6_500), 38), Is.EqualTo("what lvl is spindelhalla?"));
    }

    [Test]
    public void ACorrectedNumberReplacesTheOneTakenBack()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "where can a 21 friar solo?", 0);
        c.NotePlayer("Wynn", Room, "i said 12 not 21", 6_000);
        Assert.That(Convo.Resolve("i said 12 not 21", c.In("Wynn", Room, 6_500), 12), Is.EqualTo("where can a 12 friar solo?"));
    }

    [Test]
    public void ACorrectedClassReplacesTheOneTakenBack()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "who trains clerics?", 0);
        c.NotePlayer("Wynn", Room, "sorry i meant friar", 6_000);
        Assert.That(Convo.Resolve("sorry i meant friar", c.In("Wynn", Room, 6_500), 12), Is.EqualTo("who trains friars?"));
    }

    [Test]
    public void TheConversationLearnsWhatWasMeant()
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "sorry i meant spindelhalla not varulvhamn", 0);
        Assert.That(c.In("Wynn", Room, 500).Place, Is.EqualTo("spindelhalla"), "not the place taken back");
    }

    [TestCase("i meant to say ty")]
    [TestCase("i mean it")]
    [TestCase("i meant the hib one")]
    public void ACorrectionWithNothingToCorrectIsLeftAlone(string line)
    {
        var c = new Convo();
        c.NotePlayer("Wynn", Room, "what lvl is varulvhamn?", 0);
        c.NotePlayer("Wynn", Room, line, 6_000);
        Assert.That(Convo.Resolve(line, c.In("Wynn", Room, 6_500), 38), Is.EqualTo(line), line);
    }

    [Test]
    public void WhatACorrectionHolds()
    {
        Convo.Correction fix = Convo.CorrectionIn("sorry i meant spindelhalla not varulvhamn. what lvl");
        Assert.That(fix, Is.Not.Null);
        Assert.That(fix.Meant, Is.EqualTo("spindelhalla"));
        Assert.That(fix.Instead, Is.EqualTo("varulvhamn"));
        Assert.That(fix.Rest, Is.EqualTo("what lvl"));
        Assert.That(Convo.CorrectionIn("where is the cleric trainer?"), Is.Null);
    }
}
