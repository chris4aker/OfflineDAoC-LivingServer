using System;
using System.Linq;
using NUnit.Framework;
using Intent = DOL.GS.eAutonomousChatIntent;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, responsive replies: the period's short words, roleplay, calls for help, mishaps and apologies are read;
/// replies follow the situation (AutonomousChatContext); bots answer about themselves from their state; "how do i"
/// questions get the game's answer; "where?" after a bot named a place asks where that place is.
/// </summary>
[TestFixture]
public class UT_ChatResponsive
{
    [TestCase("lo all", Intent.Greeting)]
    [TestCase("lo", Intent.Greeting)]
    [TestCase("re", Intent.Back)]
    [TestCase("re all, isp died again", Intent.Back)]
    [TestCase("re, crashed to desktop", Intent.Back)]
    [TestCase("sry was afk, back", Intent.Back)]
    [TestCase("hi! long time no see", Intent.Greeting)]
    [TestCase("cya all next week, going out of town", Intent.Farewell)]
    [TestCase("im out, ty all", Intent.Farewell)]
    [TestCase("i have to go guys, sorry. thx for the group", Intent.Farewell)]
    [TestCase("ks much?", Intent.Abuse)]
    [TestCase("stop ksing my camp jerk", Intent.Abuse)]
    [TestCase("noob", Intent.Abuse)]
    [TestCase("need defenders at dun crim NOW", Intent.Alarm)]
    [TestCase("mids ganking at the dock", Intent.Alarm)]
    [TestCase("Hail, good sir. What news from Camelot?", Intent.Roleplay)]
    [TestCase("*bows deeply* My lady, may I offer my sword in thy service?", Intent.Roleplay)]
    [TestCase("By Odin's beard, these trolls stink worse than a kobold's boots!", Intent.Roleplay)]
    [TestCase("The Morrigan watches over us tonight.", Intent.Roleplay)]
    [TestCase("/me does a happy dance", Intent.Roleplay)]
    [TestCase("how do i go anon", Intent.Help)]
    public void ThePeriodsWordsAreRead(string text, Intent expected) => Assert.That(AutonomousChatIntentModel.Predict(text), Is.EqualTo(expected));

    [TestCase("lol hail")]
    [TestCase("wts thy blade 5g pst")]
    [TestCase("lo pow")]
    public void ChatIsNotMistakenForRoleplayOrAGreeting(string text)
    {
        Assert.That(AutonomousChatIntentModel.Predict(text), Is.Not.EqualTo(Intent.Roleplay));
        if (text == "lo pow")
            Assert.That(AutonomousChatIntentModel.Predict(text), Is.Not.EqualTo(Intent.Greeting));
    }

    [TestCase("hey u there?", "ask_on")]
    [TestCase("ne1 on?", "ask_on")]
    [TestCase("i fell off the bridge in tir na nog. again.", "player_mishap")]
    [TestCase("omg i jsut got 1 shotted by a grey con", "player_mishap")]
    [TestCase("help please, this mob is killing me", "player_help")]
    [TestCase("need heals here!!", "player_help")]
    [TestCase("first char, very confused lol", "player_help")]
    [TestCase("that was close", "player_close")]
    [TestCase("sorry, my fault", "player_sorry")]
    [TestCase("my first solo kill in rvr!!", "player_goodnews")]
    [TestCase("got a 99 quality on the first try!!", "player_goodnews")]
    [TestCase("finally 50 after 4 months :D", "player_goodnews")]
    [TestCase("got my first realm rank lol, rr2 baby", "player_goodnews")]
    [TestCase("low pow, need a sec", "player_wait")]
    [TestCase("anyone need trash loot? giving away", "player_offer")]
    [TestCase("where is the best place to solo at 22 as a hunter?", "ask_hunt")]
    [TestCase("worst day ever, boss yelled at me", "complaint")]
    public void ShapesTheBlindRoundMissedAreRead(string text, string shape) =>
        Assert.That(AutonomousChatShortTalk.QuestionShape(text), Is.EqualTo(shape));

    [TestCase("WTS 2h sword 99% qual, pst")]
    [TestCase("server lag is killing me")]
    [TestCase("re all, isp died again")]
    public void NotEveryLineIsNewsOrACallForHelp(string text) =>
        Assert.That(AutonomousChatShortTalk.QuestionShape(text), Is.Not.EqualTo("player_goodnews").And.Not.EqualTo("player_help"));

    private static AutonomousChatContext.Facts Facts(string channel = "faction", bool near = false, bool heals = false, string last = null, int level = 30) =>
        new("Brannoc", "Wynn", channel, near, heals, false, level, last, "Jordheim");

    private static (string Route, string Text)? Context(string text, AutonomousChatContext.Facts facts, int seed = 1) =>
        AutonomousChatContext.Reply(text, AutonomousChatIntentModel.Predict(text), AutonomousChatShortTalk.QuestionShape(text), facts, new Random(seed));

    [Test]
    public void TheSituationChoosesTheReply()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            Assert.That(Context("bed time for me, gn", Facts("group"), seed)?.Text, Does.Match("^(gn|night|nite|gnight)").IgnoreCase);
            Assert.That(Context("help! im stuck under this rock", Facts("say"), seed)?.Text, Does.Contain("/stuck"));
            Assert.That(Context("cya all next week, going out of town", Facts("guild"), seed)?.Text,
                Does.Contain("trip").Or.Contain("get back").Or.Contain("next week").Or.Contain("enjoy"));
            Assert.That(Context("inc 3 albs", Facts(), seed)?.Text, Does.Contain("where").Or.Contain("how many"), "no place: a question");
            Assert.That(Context("inc 3 albs at amg", Facts(level: 45), seed)?.Text, Does.Contain("omw").Or.Contain("coming").Or.Contain("on my way"));
            Assert.That(Context("help please, this mob is killing me", Facts("say", near: true), seed)?.Route, Is.EqualTo("context help"));
            Assert.That(Context("need heals here!!", Facts("group", near: true, heals: true), seed)?.Text,
                Does.Contain("heal").Or.Contain("got u").Or.Contain("on it"));
            Assert.That(Context("im lost in the forest can someone help", Facts(), seed)?.Text, Does.Contain("where").Or.Contain("zone"));
            Assert.That(Context("DF is ours again", Facts(), seed)?.Route, Is.EqualTo("context rvr"));
            Assert.That(Context("wtb str buffed armor lvl 30, pst", Facts("trade"), seed)?.Route, Is.EqualTo("context wtb"));
            string sale = Context("selling mc chain set lvl 40, 3p obo", Facts("trade"), seed)?.Text;
            Assert.That(sale, Is.Not.Null.And.Not.Contains("how much"), "a sale gets a price or a no, never its price asked back");
        }
        Assert.That(Context("stealthers near the dock watch out", Facts()), Is.Null, "a warning keeps its own thanks");
        Assert.That(Context("hi all", Facts()), Is.Null);
    }

    [Test]
    public void ACounterOfferGetsAPriceOrANo()
    {
        var asked = AutonomousChatContext.Price("60g for the set").Value;
        Assert.That(asked, Is.EqualTo((60.0, "g")));
        Assert.That(AutonomousChatContext.Price("3p obo"), Is.EqualTo((3.0, "p")));
        Assert.That(AutonomousChatContext.Price("lvl 40 chain"), Is.Null);
        var random = new Random(4);
        Assert.That(AutonomousChatContext.CounterOffer("how about 58?", asked, random), Does.Contain("deal").Or.EqualTo("done"));
        Assert.That(AutonomousChatContext.CounterOffer("how about 45?", asked, random), Does.Match(@"\d+g"), "a middle price");
        Assert.That(AutonomousChatContext.CounterOffer("10g?", asked, random), Does.Contain("no").Or.Contain("low"));
        Assert.That(AutonomousChatContext.CounterOffer("too much", asked, random), Is.Not.Null);
        Assert.That(AutonomousChatContext.CounterOffer("lol no", asked, random), Is.Not.Null);
        Assert.That(AutonomousChatContext.CounterOffer("how much for 2?", asked, random), Is.Null, "a question, not an offer");
        Assert.That(AutonomousChatContext.CounterOffer("nice hat", asked, random), Is.Null);
        Assert.That(AutonomousChatContext.Format((3, "p"), 0.25), Is.EqualTo("750g"));
    }

    private static readonly AutonomousChatQuestions.BotFacts Me = new(501, 30, "Thane", eRealm.Midgard, 13, BotPersonalityType.Leveler, false, 2, null,
        Name: "Brannoc", Zone: "Myrkwood Forest", GroupSize: 3, Target: "Large Frost Giant", Spec: "39 hammer, 30 shield");

    [TestCase("whats ur name?", "name", "Brannoc")]
    [TestCase("What be thy name, stranger?", "name", "Brannoc")]
    [TestCase("u in a group?", "group", "3")]
    [TestCase("what are you hunting?", "hunting", "giants")]
    [TestCase("what are we hunting?", "hunting", "giants")]
    [TestCase("whats ur spec?", "spec", "39 hammer")]
    public void ABotAnswersAboutItselfFromItsState(string question, string kind, string contains)
    {
        Assert.That(AutonomousChatQuestions.SelfQuestion(question), Is.EqualTo(kind));
        for (int seed = 0; seed < 6; seed++)
            Assert.That(AutonomousChatQuestions.AnswerSelf(kind, question, Me, new Random(seed)), Does.Contain(contains));
    }

    // Playtest 2026-11-04: "what does your guild do mostly?" by tell drew only the guild's name.
    [TestCase("hi, what does your guild do mostly?")]
    [TestCase("what kind of guild are u in")]
    [TestCase("is your guild rvr?")]
    public void AskedWhatTheGuildDoesABotSaysWhatItDoesNotItsName(string question)
    {
        Assert.That(AutonomousChatQuestions.SelfQuestion(question), Is.EqualTo("guildwhat"));
        var member = Me with { Guild = "Salisbury Brethren" };
        string first = null;
        for (int seed = 0; seed < 8; seed++)
        {
            string answer = AutonomousChatQuestions.AnswerSelf("guildwhat", question, member, new Random(seed));
            Assert.That(answer, Is.Not.EqualTo("Salisbury Brethren"));
            string focus = answer.Replace("Salisbury Brethren? ", "");
            first ??= focus;
            Assert.That(focus, Is.EqualTo(first), "every member of a guild tells the same story");
        }
        Assert.That(AutonomousChatQuestions.SelfQuestion("what guild are you in?"), Is.EqualTo("guild"), "which guild is still its name");
        Assert.That(AutonomousChatQuestions.AnswerSelf("guildwhat", question, Me, new Random(1)), Does.Contain("no").Or.Contain("not"));
    }

    [TestCase("i cant figure out how to equip this", "drag")]
    [TestCase("how do i bind?", "/bind")]
    [TestCase("how do i get unstuck", "/stuck")]
    [TestCase("how do i go anon", "/anon")]
    [TestCase("how many rp for rr2?", "7,125")]
    [TestCase("how many rps for rr3", "61,750")]
    [TestCase("how do i get to hib from alb", "cant")]
    public void HowDoIQuestionsGetTheGamesAnswer(string question, string contains)
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerHowTo(question, out string answer), Is.True, question);
        Assert.That(answer, Does.Contain(contains));
    }

    [Test]
    public void HowToLeavesOtherQuestionsAlone()
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerHowTo("where is the armor trainer?", out _), Is.False);
        Assert.That(AutonomousChatKnowledge.TryAnswerHowTo("how was your weekend", out _), Is.False);
    }

    [Test]
    public void WhereAfterAPlaceAsksWhereThatPlaceIs()
    {
        string answer = AutonomousChatThreads.WhereIsIt("try the bone camp in Muspelheim, good xp at that lvl", "Jordheim", new Random(1));
        Assert.That(answer, Does.Contain("muspelheim"));
        Assert.That(AutonomousChatThreads.WhereIsIt("spindel is great at 40", "Jordheim", new Random(1)), Does.Contain("spindel"));
        Assert.That(AutonomousChatThreads.WhereIsIt("inc 3 albs", "Jordheim", new Random(1)), Is.Null, "no place named: where the bot is");
    }

    [TestCase("farming, u?", true)]
    [TestCase("29, wbu?", true)]
    [TestCase("where are you?", false)]
    public void AnAnswerThatAsksBackIsAnAnswer(string text, bool answer) =>
        Assert.That(AutonomousChatThreads.ReadsAsAnswer(text, AutonomousChatIntentModel.Predict(text)), Is.EqualTo(answer));
}
