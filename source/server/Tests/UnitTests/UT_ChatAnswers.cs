using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Market = DOL.GS.AutonomousChatMarket;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, after RC5 (owner's PC, RC3 and RC5 2026-11-09): most questions bots asked in /faction got no reply (71 of 106 in RC3,
/// 15 of 21 in RC5), a player's "where do i get my spells trained?" got "no idea", and /lfg and /trade were monologues. Every
/// question now has answers that fit it, is answered whenever a bot can answer, and an answer heard this evening comes back
/// only after a rest; /lfg and /trade posts get a share of true replies.
/// </summary>
[TestFixture]
public class UT_ChatAnswers
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Boldiam"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static readonly (eRealm Realm, string[] Classes)[] Realms =
    [
        (eRealm.Albion, ["Armsman", "Paladin", "Mercenary", "Reaver", "Cleric", "Friar", "Minstrel", "Scout", "Infiltrator", "Sorcerer", "Wizard", "Theurgist",
            "Cabalist", "Necromancer"]),
        (eRealm.Midgard, ["Warrior", "Thane", "Skald", "Berserker", "Savage", "Hunter", "Shadowblade", "Healer", "Shaman", "Runemaster", "Spiritmaster",
            "Bonedancer"]),
        (eRealm.Hibernia, ["Hero", "Champion", "Blademaster", "Ranger", "Nightshade", "Druid", "Warden", "Bard", "Eldritch", "Enchanter", "Mentalist",
            "Animist", "Valewalker"]),
    ];

    private static readonly string[] Zones = ["Camelot Hills", "Salisbury Plains", "Lyonesse", "Forest Sauvage", "Cornwall"];

    private static Field.Facts Facts(int i, eRealm realm = eRealm.Albion, string cls = null, int level = 0, bool inRvR = false)
    {
        string[] classes = Realms.First(r => r.Realm == realm).Classes;
        cls ??= classes[i % classes.Length];
        int lvl = level > 0 ? level : 5 + i * 7 % 46;
        bool healer = cls is "Cleric" or "Friar" or "Healer" or "Shaman" or "Druid" or "Warden";
        return new Field.Facts(20_000 + i, realm, cls, lvl, Zones[i % Zones.Length], AutonomousBotVoices.DungeonFor(realm, lvl, new Random(i)), inRvR, false,
            i % 5 == 0, i % 4 == 0, i % 4 == 0 ? 3 : 1, false, false, false, i % 2 == 0, "large skeleton", i % 3 == 0 ? null : "Ghostly Cloak", 96, i % 6 == 0,
            i % 7 == 0 ? "Camelot" : null, 1 + i % 9, 20, healer, Frontier);
    }

    private static bool Rhetorical(string text)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " "), @"\s+", " ").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:\S+|(?:lol|heh|haha|ugh|bleh|argh|ok|so|well)\b.*|only \d+ \w+|.*\bnot bad|.*\bkeeps not bad)$");
    }

    // ---- 1. every question has answers and gets one ------------------------------------------------------------------------

    [Test]
    public void EveryQuestionABotAsksHasAnswers()
    {
        var unanswerable = new List<string>();
        int questions = 0;
        for (int i = 0; i < 6_000; i++)
        {
            eRealm realm = Realms[i % 3].Realm;
            string channel = i % 4 == 0 ? "say" : "faction";
            Field.Said said = Field.Line(channel, Facts(i, realm, inRvR: i % 9 == 0), null, new Random(i));
            if (said == null || !said.Asks || said.Kind == "social" || Rhetorical(said.Text))
                continue;
            questions++;
            if (said.Answers == null || said.Answers.Length == 0)
                unanswerable.Add($"{said.Family}: {said.Text}");
        }
        Assert.That(questions, Is.GreaterThan(1_000));
        Assert.That(unanswerable.Distinct(), Is.Empty, "questions written with no answers (RC5: \"what spec for a 50 inf?\", \"any stealthers out?\")");
    }

    [Test]
    public void AQuestionIsAnsweredWhenABotCanAnswerItAllEvening()
    {
        // A realm's evening: one memory, a question every half minute for three hours (more than the budget lets through).
        var memory = new Field.Memory();
        int asked = 0, answered = 0;
        var unanswered = new List<string>();
        for (int i = 0; i < 360; i++)
        {
            long now = i * 30_000L;
            var random = new Random(1_000 + i);
            Field.Said said = Field.Line("faction", Facts(i), memory, random, now);
            if (said == null || !Field.WantsAnswer(said))
                continue;
            Field.Facts answerer = Enumerable.Range(1, 200).Select(k => Facts(i + k * 13)).FirstOrDefault(f =>
                Field.CanGiveAnswer(said, f.ClassName, f.Level, f.IsHealer));
            if (answerer == null)
                continue;
            asked++;
            if (Field.Thread(said, Facts(i), answerer, null, memory, random, now).Any(step => step.Who == 1))
                answered++;
            else
                unanswered.Add(said.Text);
        }
        Assert.That(asked, Is.GreaterThan(150));
        Assert.That(answered, Is.GreaterThanOrEqualTo(0.95 * asked), string.Join(" | ", unanswered.Take(20)));
    }

    [Test]
    public void AQuestionForOneClassGoesToABotOfThatClass()
    {
        var said = new Field.Said("any reavers on? quick q", "class", ["{same}me, whats up", "{same}me"]) { SameClass = "Reaver", Asks = true };
        Assert.That(Field.CanGiveAnswer(said, "Reaver", 40), Is.True);
        Assert.That(Field.CanGiveAnswer(said, "Cabalist", 40), Is.False, "RC5: a cabalist was picked and had nothing to say");
        var heal = new Field.Said("any healers here?", "camp", ["{healer}me"]) { Asks = true };
        Assert.That(Field.CanGiveAnswer(heal, "Cleric", 20, true), Is.True);
        Assert.That(Field.CanGiveAnswer(heal, "Wizard", 20), Is.False);
    }

    [Test]
    public void AnAnswerHeardThisEveningComesBackOnlyAfterARest()
    {
        var memory = new Field.Memory();
        var said = new Field.Said("what do i need to start crafting?", "help", ["go see the crafting trainer", "tools from the merchant"]) { Asks = true };
        var random = new Random(3);
        Field.Facts answerer = Facts(2);
        Assert.That(Field.Answer(said, answerer, memory, random, 0), Is.Not.Null);
        Assert.That(Field.Answer(said, answerer, memory, random, 60_000), Is.Not.Null);
        // Both heard: nothing fresh, nothing rested yet.
        Assert.That(Field.Answer(said, answerer, memory, random, 5 * 60_000), Is.Null, "no repeat within minutes");
        // An hour on, a heard answer may come back (RC5: a question's fourth asking had no answer left all evening).
        Assert.That(Field.Answer(said, answerer, memory, random, 61 * 60_000), Is.Not.Null);
    }

    [Test]
    public void ALiveAnswerIsSaidInsteadOfAWrittenOne()
    {
        var said = new Field.Said("where's the pally trainer", "ask", ["in camelot"]) { Family = "ask_trainer", Asks = true };
        List<Field.Step> steps = Field.Thread(said, Facts(1), Facts(2), null, null, new Random(1), 0, "Lady Fridwulf in Camelot City");
        Assert.That(steps.First(step => step.Who == 1).Text, Does.StartWith("lady fridwulf in camelot city"));
    }

    // ---- 2. answers fit the question ---------------------------------------------------------------------------------------

    private static IEnumerable<string> AnswersTo(string family, Func<Field.Facts> facts = null, int draws = 4_000)
    {
        var found = new List<string>();
        for (int i = 0; i < draws && found.Count < 40; i++)
        {
            Field.Facts f = facts?.Invoke() ?? Facts(i, inRvR: true, level: 50);
            Field.Said said = Field.Line("faction", f, null, new Random(i));
            if (said?.Family != family || said.Answers == null)
                continue;
            for (int k = 0; k < 5; k++)
                if (Field.Answer(said, Facts(i + 1 + k, level: f.Level), null, new Random(i * 7 + k)) is { } answer)
                    found.Add(answer);
        }
        return found;
    }

    [Test]
    public void ATrainerQuestionIsAnsweredWithTheCapital()
    {
        int n = 0;
        var random = new Random(5);
        foreach ((eRealm realm, string[] classes) in Realms)
            foreach (string cls in classes)
                foreach (string answer in Field.TrainerAnswers(Facts(n++, realm, cls), random))
                    Assert.That(answer, Does.Contain(Field.CapitalWord(realm)), $"{cls}: {answer}");
        Assert.That(AutonomousChatKnowledge.GenericTrainerAnswer(eRealm.Albion, "Paladin", new Random(1)), Does.Contain("paladin").And.Contain("camelot"));
    }

    [TestCase("Infiltrator", "cs|critical|envenom|stealth|main line")]
    [TestCase("Paladin", "chant|2h|slash|main line")]
    [TestCase("Healer", "mending|pac|main line")]
    [TestCase("Eldritch", "light|void|main line")]
    [TestCase("Valewalker", "scythe|arboreal|main line")]
    public void SpecAdviceIsTheClasssOwn(string cls, string words)
    {
        foreach (string advice in Field.SpecAdvice(cls))
            Assert.That(advice, Does.Match(words), $"{cls}: {advice}");
        Assert.That(Field.WeaponAdvice("Infiltrator"), Has.Some.Contains("dagger").Or.Some.Contains("thrust"));
        Assert.That(Field.WeaponAdvice("Wizard"), Has.Some.Contains("staff"));
    }

    [Test]
    public void ScOrDropsDependsOnTheLevel()
    {
        Assert.That(Field.GearAdvice(27), Has.All.Contains("drops"));
        Assert.That(Field.GearAdvice(50), Has.All.Contains("sc"));
    }

    [Test]
    public void HelpAnswersFitTheirQuestion()
    {
        Assert.That(AnswersTo("q_handin"), Is.Not.Empty.And.All.Match("town|gave it|journal|whoever"));
        Assert.That(AnswersTo("q_craft"), Is.Not.Empty.And.All.Match("crafting trainer|tools|money"));
        Assert.That(AnswersTo("q_rps"), Is.Not.Empty.And.All.Match("kill|keep|rps|tower"));
        Assert.That(AnswersTo("q_epic"), Is.Not.Empty.And.All.Match("not yet|did|grp|guild|chain"));
        Assert.That(AnswersTo("ask_spec"), Is.Not.Empty.And.None.Match("^(me|x|ty)$"));
    }

    [Test]
    public void APriceQuestionGetsAPrice()
    {
        var said = new Field.Said("how much for a 98% bladeblocker small shield?", "crafter", ["3p", "3p maybe", "dunno, try the broker", "not much"]) { Asks = true };
        for (int i = 0; i < 50; i++)
            Assert.That(Field.Fits(Field.Answer(said, Facts(i), null, new Random(i)), "price"), Is.True);
    }

    [Test]
    public void AnAnswerAboutItsSpeakerIsOnlyItsOwn()
    {
        Field.Facts thane = Facts(1, eRealm.Midgard, "Thane", 31);
        Assert.That(Field.MentionsSelf("me, thane 31", thane), Is.True);
        Assert.That(Field.MentionsSelf("in lyonesse", Facts(2) with { Zone = "Lyonesse" }), Is.True);
        Assert.That(Field.MentionsSelf("back to whoever gave it", thane), Is.False);
        Assert.That(Field.MentionsSelf("kill enemies, get rps", thane), Is.False);
    }

    // ---- 3. a player's question --------------------------------------------------------------------------------------------

    [TestCase("where do i get my spells trained?", true)]
    [TestCase("where do i learn new spells", true)]
    [TestCase("how do i get trained?", true)]
    [TestCase("where is the paladin trainer", true)]
    [TestCase("just trained, heading out", false)]
    [TestCase("my spells are so weak lol", false)]
    public void ATrainingQuestionIsATrainerQuestion(string text, bool trainer) =>
        Assert.That(AutonomousChatKnowledge.LooksLikeTrainerQuestion(text), Is.EqualTo(trainer), text);

    [Test]
    public void APlayersTrainingQuestionNamesTheTrainerNotNoIdea()
    {
        // No world loaded here: the capital, where every class trains.
        string answer = AutonomousChatKnowledge.TrainerAnswer(eRealm.Albion, (int)eCharacterClass.Paladin, null, new Random(2));
        Assert.That(answer, Does.Contain("trainer").And.Contain("camelot"));
        Assert.That(answer, Does.Not.Contain("no idea").And.Not.Contain("dunno"));
    }

    [TestCase("what spec for a 50 inf?", "Cleric", "cs|critical|envenom|stealth|main line")]
    [TestCase("what should i put my points in?", "Paladin", "chant|2h|slash|main line")]
    [TestCase("sc or drops at 27?", "Cleric", "drops")]
    [TestCase("what do i need to start crafting?", "Cleric", "crafting trainer")]
    [TestCase("where do i hand in this quest?", "Cleric", "gave it|quest giver")]
    [TestCase("how do realm points work?", "Cleric", "kill|keep")]
    [TestCase("anyone done the cleric epic?", "Cleric", "chain")]
    public void APlayersCommonQuestionGetsAFittingAnswer(string question, string cls, string words)
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon(question, eRealm.Albion, cls, 30, new Random(1), out string answer), Is.True, question);
        Assert.That(answer, Does.Match(words), question);
    }

    [TestCase("lol")]
    [TestCase("going crafting later")]
    public void NotEveryLineIsACommonQuestion(string line) =>
        Assert.That(AutonomousChatKnowledge.TryAnswerCommon(line, eRealm.Albion, "Cleric", 30, new Random(1), out _), Is.False, line);

    // ---- 4. /lfg and /trade ------------------------------------------------------------------------------------------------

    [Test]
    public void AGroupPostIsAnsweredOnlyByASoloBotOfTheRoleAndLevels()
    {
        Market.Need need = Market.NeedOf("lf1m healer barrows 31-37", 34);
        Assert.That(need, Is.EqualTo(new Market.Need("healer", 31, 37)));
        Assert.That(Market.NeedOf("lfm keltoi", 25), Is.EqualTo(new Market.Need(null, 22, 28)));
        Assert.That(Market.CanReply(Market.Post.Lfm, "lf1m healer barrows 31-37", 34, Facts(1, cls: "Cleric", level: 33), solo: true), Is.True);
        Assert.That(Market.CanReply(Market.Post.Lfm, "lf1m healer barrows 31-37", 34, Facts(1, cls: "Cleric", level: 33), solo: false), Is.False, "already grouped");
        Assert.That(Market.CanReply(Market.Post.Lfm, "lf1m healer barrows 31-37", 34, Facts(1, cls: "Wizard", level: 33), solo: true), Is.False, "not a healer");
        Assert.That(Market.CanReply(Market.Post.Lfm, "lf1m healer barrows 31-37", 34, Facts(1, cls: "Cleric", level: 45), solo: true), Is.False, "out of the levels");
    }

    [Test]
    public void ASaleIsAskedAboutByABotThatWearsItsArmour()
    {
        Assert.That(Market.CanReply(Market.Post.Wts, "wts 99% chain legs 2p", 40, Facts(1, cls: "Mercenary", level: 40), true), Is.True);
        Assert.That(Market.CanReply(Market.Post.Wts, "wts 99% chain legs 2p", 40, Facts(1, cls: "Wizard", level: 40), true), Is.False);
        Assert.That(Market.CanReply(Market.Post.Wts, "wts ghostly cloak /send", 40, Facts(1, cls: "Wizard", level: 15), true), Is.False, "far below its level");
        Assert.That(Market.WantedInName("wtb chain helm pst"), Is.EqualTo("helm"));
        Assert.That(Market.WantedInName("wtb pots"), Is.EqualTo("potion"));
    }

    [Test]
    public void APartyHasASpotOnlyWhileItIsStillRecruitingThatRole()
    {
        // Bot parties are formed at their full size and never backfilled: once a party stops recruiting it has no spot, whatever
        // its size (1.1.3, after RC5: any party under eight answered "ya, 1 spot").
        (string, bool)[] noHealer = [("Armsman", false), ("Wizard", false), ("Minstrel", false)];
        (string, bool)[] withHealer = [("Armsman", false), ("Cleric", true), ("Wizard", false)];
        Assert.That(Market.SpotFor(null, recruiting: false, noHealer), Is.False, "a party that set off has no seat");
        Assert.That(Market.SpotFor("healer", recruiting: false, noHealer), Is.False);
        Assert.That(Market.SpotFor("healer", recruiting: true, noHealer), Is.True, "still recruiting and no healer yet");
        Assert.That(Market.SpotFor("healer", recruiting: true, withHealer), Is.False, "it already has its healer");
        Assert.That(Market.SpotFor("tank", recruiting: true, withHealer), Is.False, "an armsman is its tank");
        Assert.That(Market.SpotFor("dps", recruiting: true, withHealer), Is.True, "a free seat takes dps");
        Assert.That(Market.SpotFor(null, recruiting: true, withHealer), Is.True);
        // The reply says which role it asks about, so the poster's answer can be checked against it.
        var random = new Random(3);
        for (int i = 0; i < 60; i++)
        {
            Market.Reply reply = Market.ReplyTo(Market.Post.Lfm, "lf1m healer barrows 31-37", Facts(i, cls: "Cleric", level: 33), true, false, random);
            if (reply.Text.StartsWith("still need a", StringComparison.Ordinal))
                Assert.That(reply.Role, Is.EqualTo("healer"), reply.Text);
            else if (reply.Asks == "room")
                Assert.That(reply.Role, Is.Null, reply.Text);
        }
        string filled = Market.PosterAnswer("room", Market.Post.Lfm, "lf1m healer", Facts(3, level: 40), null,
            Market.SpotFor("healer", true, withHealer), true, random, roleFilled: "healer");
        Assert.That(filled, Does.Contain("healer").And.Not.StartWith("ya"));
        string gone = Market.PosterAnswer("room", Market.Post.Lfm, "lf1m healer", Facts(3, level: 40), null,
            Market.SpotFor(null, false, noHealer), true, random);
        Assert.That(gone, Does.Contain("full").Or.Contain("filled"));
    }

    [Test]
    public void ThePostersAnswersAreTrueOfIt()
    {
        var random = new Random(4);
        Field.Facts poster = Facts(3, level: 40);
        for (int i = 0; i < 50; i++)
        {
            Assert.That(Market.PosterAnswer("room", Market.Post.Lfm, "lf1m healer", poster, null, groupHasRoom: false, true, random), Does.Contain("full").Or.Contain("filled"));
            Assert.That(Market.PosterAnswer("room", Market.Post.Lfm, "lf1m healer", poster, null, groupHasRoom: true, true, random), Does.StartWith("ya"));
            Assert.That(Market.PosterAnswer("price", Market.Post.Wts, "wts 99% chain legs 2p", poster, null, true, stillHas: true, random), Does.Contain("2p"));
            Assert.That(Market.PosterAnswer("have", Market.Post.Wts, "wts 99% chain legs 2p", poster, null, true, stillHas: false, random), Does.Contain("sold"));
            Assert.That(Market.PosterAnswer("spec", Market.Post.Lfg, "lfg 40 cleric", poster, "40 rejuvenation, 20 enhancement", true, true, random),
                Is.EqualTo("40 rejuvenation, 20 enhancement"));
        }
    }

    [Test]
    public void APosterSaysFullOrSoldOnlyWhenItIs()
    {
        var random = new Random(5);
        Assert.That(Market.Closing(Market.Post.Lfm, groupFull: false, grouped: true, true, random), Is.Null);
        Assert.That(Market.Closing(Market.Post.Lfm, groupFull: true, grouped: true, true, random), Does.Contain("full"));
        Assert.That(Market.Closing(Market.Post.Lfg, false, grouped: false, true, random), Is.Null);
        Assert.That(Market.Closing(Market.Post.Lfg, false, grouped: true, true, random), Does.Contain("grp").Or.Contain("found"));
        Assert.That(Market.Closing(Market.Post.Wts, false, false, stillHas: true, random), Is.Null);
        Assert.That(Market.Closing(Market.Post.Wts, false, false, stillHas: false, random), Does.Contain("sold"));
    }

    [Test]
    public void AShareOfPostsGetsAReply()
    {
        foreach (Market.Post post in new[] { Market.Post.Lfm, Market.Post.Lfg, Market.Post.Wts, Market.Post.Wtb })
            Assert.That(Market.ReplyShare(post), Is.InRange(0.25, 0.5), post.ToString());
        Assert.That(Market.ReplyTo(Market.Post.Wtb, "wtb chain helm", Facts(1), true, hasWanted: true, new Random(1)).Text, Does.Contain("have one"));
        Assert.That(Market.ReplyTo(Market.Post.Wtb, "wtb chain helm", Facts(1), true, hasWanted: false, new Random(1)).Text, Does.Not.Contain("have one"));
    }
}
