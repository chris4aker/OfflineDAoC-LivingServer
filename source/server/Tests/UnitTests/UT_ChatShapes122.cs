using System;
using System.Linq;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Knowledge = DOL.GS.AutonomousChatKnowledge;
using Live = DOL.GS.AutonomousChatLiveAnswers;
using Questions = DOL.GS.AutonomousChatQuestions;
using Short = DOL.GS.AutonomousChatShortTalk;

namespace DOL.GS.Tests;

/// <summary>
/// The 1.2.2 chat audit's shape fixes (docs/bot-voices/CHAT-AUDIT-1.2.2.md): F3 where-to-level read as a pattern and answered
/// from the place data, F5 trainer answers that fit the asker's class and realm, F7 (first half) no generic line in a known
/// class's advice, F11 what the bot is doing now, its own potions, and short facts typed faster. Tests only.
/// </summary>
[TestFixture]
public class UT_ChatShapes122
{
    // ---- F3: where to level ----------------------------------------------------------------------------------------------

    [TestCase("where should i level at 3?")]
    [TestCase("good place to lvl at 6?")]
    [TestCase("where do 9s hunt?")]
    [TestCase("any good spots for a 12 paladin?")]
    [TestCase("where can a 15 minstrel solo?")]
    [TestCase("lvl 18 where to xp?")]
    [TestCase("best xp for 21?")]
    [TestCase("best xp for 21")]
    [TestCase("im 24, where should i go now?")]
    [TestCase("where to level 27-32?")]
    [TestCase("good camp for 30?")]
    [TestCase("where do i go after 33?")]
    [TestCase("anyone know a good spot for 36?")]
    [TestCase("where do 24s xp?")]
    [TestCase("where do mids level at 20?")]
    [TestCase("where to level mid 20s?")]
    [TestCase("lvl 15->20 where?")]
    [TestCase("where is everyone getting xp in the 30s")]
    [TestCase("good place to lvl at 18")]
    public void AWhereToLevelQuestionIsAHunt(string text)
    {
        Assert.That(Short.AsksWhereToLevel(text), Is.True, text);
        Assert.That(Short.QuestionShape(text), Is.EqualTo("ask_hunt"), text);
    }

    [TestCase("what lvl is gotar?")]
    [TestCase("is vendo caverns ok at 24?")]
    [TestCase("whats the level range for devwy?")]
    [TestCase("where is the cleric trainer?")]
    [TestCase("best class to level?")]
    [TestCase("good luck leveling all")]
    [TestCase("lfg 24 any good xp")]
    [TestCase("where do i level weaponcrafting?")]
    [TestCase("best place to buy a sword?")]
    [TestCase("good place to bind?")]
    [TestCase("should i level in vendo or myrk at 24?")]
    [TestCase("any good music to grind to?")]
    [TestCase("how do i level?")]
    [TestCase("where to next?")]
    [TestCase("where should we go")]
    [TestCase("mind if i hunt near cotswold?")]
    public void OtherQuestionsAreNotAHunt(string text) => Assert.That(Short.AsksWhereToLevel(text), Is.False, text);

    [Test]
    public void AClassInTheQuestionDoesNotMakeItClassTalk()
    {
        // The shape is read before class talk (VoicedChoiceCore: TryShapedReply first): "any good spots for a 12 paladin?" drew
        // "Paladin chants carry a whole group."
        foreach (string text in new[] { "any good spots for a 12 paladin?", "any good spots for a 36 healer?", "where can a 27 champion solo?" })
            Assert.That(Short.QuestionShape(text), Is.EqualTo("ask_hunt"), text);
    }

    [Test]
    public void TheRealmAndSoloHintsAreRead()
    {
        Assert.That(Short.RealmNamed("where do mids level at 20?"), Is.EqualTo(eRealm.Midgard));
        Assert.That(Short.RealmNamed("any albs know a spot for 30"), Is.EqualTo(eRealm.Albion));
        Assert.That(Short.RealmNamed("where do hibs hunt at 40"), Is.EqualTo(eRealm.Hibernia));
        Assert.That(Short.RealmNamed("where to level mid 20s?"), Is.EqualTo(eRealm.None), "mid 20s is a level");
        Assert.That(Short.AsksSolo("where can a 15 minstrel solo?"), Is.True);
        Assert.That(Short.AsksSolo("good camp for 30?"), Is.False);
    }

    [Test]
    public void TheCatalogZoneHoldsTheLevelAndIsOfTheRealm()
    {
        foreach ((eRealm realm, string code) in new[] { (eRealm.Albion, "alb"), (eRealm.Midgard, "mid"), (eRealm.Hibernia, "hib") })
        {
            int found = 0;
            for (int level = 1; level <= 50; level++)
            {
                string zone = Live.CatalogZone(realm, level);
                if (zone == null)
                    continue;
                found++;
                var place = AutonomousChatPlaces.All.First(p => p.Type == "zone" && p.Realm == code && (p.Zones.FirstOrDefault() ?? p.Key) == zone);
                Assert.That(level, Is.InRange(place.Levels[0], place.Levels[1]), $"{realm} {level}: {zone}");
            }
            Assert.That(found, Is.GreaterThanOrEqualTo(40), $"{realm}: a zone for most levels");
        }
        Assert.That(Live.CatalogZone(eRealm.None, 20), Is.Null);
    }

    [Test]
    public void AWhereToLevelAnswerNamesARealPlaceAndClaimsNoCrowdItDidNotSee()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var random = new Random(seed);
            string quiet = Live.HuntText(eRealm.Midgard, 24, "Muspelheim", "Vendo Caverns", busy: false, random);
            Assert.That(quiet, Does.Contain("Muspelheim"));
            Assert.That(quiet, Does.Not.Match("camping|busy|saw a bunch|right now"), "no bots seen there: no crowd claimed");
            string solo = Live.HuntText(eRealm.Albion, 15, "Isle of Glass", null, busy: false, random);
            Assert.That(solo, Does.Contain("Isle of Glass").And.Not.Contain("group"));
            Assert.That(solo.Length, Is.LessThanOrEqualTo(140));
        }
        Assert.That(Live.HuntText(eRealm.Hibernia, 3, null, null, false, new Random(1)), Does.Contain("Connacht"), "the starter zones below 10");
    }

    // ---- F5: trainers --------------------------------------------------------------------------------------------------

    [Test]
    public void ATrainerAnswerSaysNoSpellsAndYourOnlyForTheAskersClass()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var random = new Random(seed);
            foreach (string cls in new[] { "Armsman", "Warrior", "Hero", "Blademaster", "Wizard" })
            {
                eRealm realm = AutonomousChatGear.RealmOf(cls);
                string own = Knowledge.GenericTrainerAnswer(realm, cls, random, own: true);
                Assert.That(own, Does.Not.Match(@"\bspells?\b"), own);
                Assert.That(own, Does.Contain(cls.ToLowerInvariant()).And.Contain(Field.CapitalWord(realm)));
                string other = Knowledge.GenericTrainerAnswer(realm, cls, random, own: false);
                Assert.That(other, Does.Not.Match(@"\b(your|ur)\b|\bspells?\b"), other);
                Assert.That(other, Does.Contain(Field.CapitalWord(realm)));
            }
            // "who trains minstrels?" from a sorcerer: the minstrel trainer, not "your minstrel trainer".
            string minstrel = Knowledge.TrainerAnswerFor(eRealm.Albion, "Minstrel", "Sorcerer", random);
            Assert.That(minstrel, Does.Not.Match(@"\b(your|ur)\b"), minstrel);
            Assert.That(minstrel, Does.Contain("camelot"));
            // "where is the armsman trainer?" from an armsman: his own.
            Assert.That(Knowledge.TrainerAnswerFor(eRealm.Albion, "Armsman", "Armsman", random), Does.Contain("armsman").And.Not.Match(@"\bspells?\b"));
            // "where is the blademaster trainer?" in Albion: a Hibernian class, said as such.
            string bm = Knowledge.TrainerAnswerFor(eRealm.Albion, "Blademaster", "Wizard", random);
            Assert.That(bm, Does.Contain("hib"), bm);
            Assert.That(bm, Does.Not.Contain("camelot"));
            Assert.That(Knowledge.TrainerAnswerFor(eRealm.Hibernia, "Skald", "Ranger", random), Does.Contain("mid"));
            Assert.That(Knowledge.TrainerAnswerFor(eRealm.Midgard, null, "Healer", random), Does.Contain("healer").And.Contain("jordheim"));
        }
        Assert.That(Knowledge.TrainerAnswerFor(eRealm.Albion, null, null, new Random(1)), Is.Null);
    }

    [TestCase("who trains mercenaries?", "Mercenary")]
    [TestCase("where do armsmen train?", "Armsman")]
    [TestCase("where are the heroes trained", "Hero")]
    [TestCase("who trains blademasters?", "Blademaster")]
    [TestCase("who trains minstrels?", "Minstrel")]
    public void AClassIsReadInItsPluralToo(string text, string cls) => Assert.That(Field.ClassNamed(text), Is.EqualTo(cls));

    // ---- F7 (first half): advice -----------------------------------------------------------------------------------------

    [Test]
    public void AKnownClasssAdviceIsItsOwn()
    {
        foreach (eCharacterClass cls in AutonomousChatGear.Classes.Where(c => c != eCharacterClass.Sluaghbinder))
        {
            string name = cls.ToString();
            Assert.That(Field.KnowsClass(name), Is.True, name);
            Assert.That(Field.SpecAdvice(name), Is.Not.Empty.And.None.Match("main line first|rest wherever|ask ur trainer"), name);
            Assert.That(Field.WeaponAdvice(name), Is.Not.Empty.And.None.Match("whatever ur spec|that drops lol"), name);
        }
        // A class the tips don't know: no player-facing advice at all (TryAnswerCommon), the general rule only between bots.
        Assert.That(Knowledge.TryAnswerCommon("what weapon should i use?", eRealm.Albion, null, 20, new Random(1), out _), Is.False);
        Assert.That(Knowledge.TryAnswerCommon("what spec should i go?", eRealm.Albion, null, 20, new Random(1), out _), Is.False);
        Assert.That(Knowledge.TryAnswerCommon("what weapon should a thane use?", eRealm.Midgard, "Healer", 20, new Random(1), out string thane), Is.True);
        Assert.That(thane, Does.Contain("hammer"));
        Assert.That(Field.SpecAdvice("nobody"), Is.Not.Empty, "bot-to-bot class talk keeps a general rule for a class it doesn't know");
    }

    // ---- F11: what the bot is doing, its pots, quick facts ----------------------------------------------------------------

    private static readonly Questions.BotFacts Me = new(501, 30, "Thane", eRealm.Midgard, 13, BotPersonalityType.Leveler, false, 2, null,
        Name: "Brannoc", Zone: "Myrkwood Forest", GroupSize: 1, Target: "Large Frost Giant");

    [TestCase("what are you up to?", "doing")]
    [TestCase("what r u doing", "doing")]
    [TestCase("whatcha up to", "doing")]
    [TestCase("wyd", "doing")]
    [TestCase("what are you up to tonight?", "plans")]
    [TestCase("what are you doing later", "plans")]
    [TestCase("got any pots?", "pots")]
    [TestCase("have any spare potions?", "pots")]
    [TestCase("you got any pots?", "pots")]
    public void WhatTheBotIsDoingAndItsPotsAreAboutItself(string text, string kind) => Assert.That(Questions.SelfQuestion(text), Is.EqualTo(kind));

    [Test]
    public void WhatTheBotIsDoingComesFromItsHunt()
    {
        for (int seed = 0; seed < 12; seed++)
        {
            string doing = Questions.AnswerSelf("doing", "what are you up to?", Me, new Random(seed));
            Assert.That(doing, Does.Contain("giants").And.Not.Contain("the usual"), doing);
            string grouped = Questions.AnswerSelf("doing", "what are you up to?", Me with { GroupSize = 3 }, new Random(seed));
            Assert.That(grouped, Does.Contain("group").Or.Contain("grouped"));
            Assert.That(Questions.AnswerSelf("pots", "got any pots?", Me, new Random(seed)), Does.Match("couple|2"));
            Assert.That(Questions.AnswerSelf("pots", "got any pots?", Me with { Potions = 0 }, new Random(seed)), Does.Match("out|none"));
        }
        // Nothing known of its hunt: the corpus answers (no fact).
        Assert.That(Questions.AnswerSelf("doing", "what are you up to?", Me with { Target = null }, new Random(1)), Is.Null);
    }

    [Test]
    public void AShortKnownFactTypesFaster()
    {
        Assert.That(AutonomousChatArbiter.IsQuickFact("fact world", "mostly 20-28"), Is.True);
        Assert.That(AutonomousChatArbiter.IsQuickFact("knowledge", "jordheim, ur healer trainer is there"), Is.True);
        Assert.That(AutonomousChatArbiter.IsQuickFact("live ask_hunt", "try Muspelheim, about right for 24"), Is.True);
        Assert.That(AutonomousChatArbiter.IsQuickFact("intent banter", "lol same"), Is.False, "talk is not a known fact");
        Assert.That(AutonomousChatArbiter.IsQuickFact("knowledge",
            "The fortress of caer sidi sits right on the road to annwn, and lost souls walk in"), Is.False, "a long answer keeps its pace");
        Assert.That(AutonomousChatArbiter.QuickWpm(40), Is.EqualTo(60));
        const string answer = "jordheim, ur healer trainer is there";
        int slow = AutonomousChatArbiter.DelayMs("where is the healer trainer?", answer, 40, 0, new Random(3));
        int quick = AutonomousChatArbiter.DelayMs("where is the healer trainer?", answer, AutonomousChatArbiter.QuickWpm(40), 0, new Random(3));
        Assert.That(quick, Is.LessThan(slow));
        Assert.That(quick, Is.GreaterThanOrEqualTo(2_000), "never faster than a person can read and answer");
    }
}
