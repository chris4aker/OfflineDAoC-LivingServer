using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Intent = DOL.GS.eAutonomousChatIntent;
using Budget = DOL.GS.AutonomousChatReplyBudget;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, the second pass after blind round 2, one test per fix category: spam, other addressees, tells with content,
/// fewer silences, roleplay acts, intent and alarm fixes, ambient realism, polish. Written from the categories, not from the
/// round 2 lines.
/// </summary>
[TestFixture]
public class UT_ChatSecondPass
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    // ---- 2. spam and scams -------------------------------------------------------------------------------------------------

    [TestCase("visit www.cheapgold.com for plat")]
    [TestCase("best prices at w w w . gold4u . net")]
    [TestCase("goldshop dot com 10% off")]
    [TestCase("100p for $5, fast delivery")]
    [TestCase("Your account has been flagged for review, reply within 24h")]
    [TestCase("GM_Support: you have won a free mount, click the link")]
    [TestCase("WTS 2 accounts with level 50s pst")]
    [TestCase("ch34p pl4t h3r3")]
    [TestCase("BEST DEALS ON THE SERVER COME AND SEE MY SHOP TODAY")]
    public void SpamAndScamsAreScreened(string text) => Assert.That(AutonomousChatScreen.IsSpam(text), Is.True, text);

    [TestCase("lf2m for vendo, need healer")]
    [TestCase("w00t rr4 finally")]
    [TestCase("selling mc chain 3p obo")]
    [TestCase("INC INC INC")]
    [TestCase("where is the healer trainer?")]
    [TestCase("ne1 up for df l8r")]
    public void OrdinaryChatIsNotSpam(string text) => Assert.That(AutonomousChatScreen.IsSpam(text), Is.False, text);

    // ---- 3. lines said to someone else ---------------------------------------------------------------------------------------

    [TestCase("Morwenna, wanna duo?", "Morwenna")]
    [TestCase("ty Bran for the rez", "Bran")]
    [TestCase("congrats to Tovah on 50!", "Tovah")]
    [TestCase("gn sigrun", "sigrun")]
    [TestCase("Kelda ty for the port!!", "Kelda")]
    public void ALineToSomeoneElseIsTheirs(string text, string name) => Assert.That(AutonomousChatScreen.OtherAddressee(text, "Brannoc"), Is.EqualTo(name));

    [TestCase("ty all")]
    [TestCase("gn guys")]
    [TestCase("ty heals :)")]
    [TestCase("thx mythic, crashed again")]
    [TestCase("Halt! Who goes there?")]
    [TestCase("Brannoc, wanna duo?")]
    [TestCase("anyone up for emain?")]
    [TestCase("Camelot is packed tonight")]
    public void CrowdsThingsAndTheBotItselfAreNotSomeoneElse(string text) => Assert.That(AutonomousChatScreen.OtherAddressee(text, "Brannoc"), Is.Null, text);

    [Test]
    public void OnlyAGroupMayLaughAtALineToSomeoneElse()
    {
        Assert.That(AutonomousChatScreen.MayReactToOthers("group", Intent.Laugh), Is.True);
        Assert.That(AutonomousChatScreen.MayReactToOthers("faction", Intent.Laugh), Is.False);
        Assert.That(AutonomousChatScreen.MayReactToOthers("group", Intent.Grouping), Is.False);
    }

    // ---- 1. tells with content get a short fitting reply ----------------------------------------------------------------------

    private static Budget.Moment Tell(string text, string cls = "Thane", bool heal = false, bool rez = false, string last = null) =>
        new(text, "tell", true, AutonomousChatIntentModel.Predict(text), cls, heal, rez, false, last);

    [Test]
    public void ATellThatSaysSomethingGetsAShortFittingReply()
    {
        var random = new Random(2);
        Assert.That(Budget.ToContent(Tell("will u take 40g for that cloak instead of 50?"), random), Does.Contain("not").Or.Contain("using"));
        Assert.That(Budget.ToContent(Tell("nice job tanking earlier"), random), Does.Contain("ty").Or.Contain("thx").Or.Contain("thanks"));
        Assert.That(Budget.ToContent(Tell("u got heal? im almost dead"), random), Does.Contain("thane").And.Contain("no heals"));
        Assert.That(Budget.ToContent(Tell("u got heal? im almost dead", "Druid", heal: true), random), Does.Contain("where"));
        Assert.That(Budget.ToContent(Tell("can u rez me pls"), random), Does.Contain("rez"));
        Assert.That(Budget.ToContent(Tell("want a res?"), random), Does.Contain("good").Or.Contain("ok").Or.Contain("ty"));
        Assert.That(Budget.ToContent(Tell("sry to bother but where is the healer trainer here"), random),
            Does.Contain("clue").Or.Contain("dunno").Or.Contain("sure").Or.Contain("idea"));
        Assert.That(Budget.ToContent(Tell("31 friar, at the barrows", last: "what lvl r u, where u at?"), random),
            Is.AnyOf("cool", "k cool", "ah ok", "nice", "k", "ok ty", "gotcha"), "an answer to the bot's own question is never acked");
    }

    [TestCase("hi")]
    [TestCase("hey u there?")]
    [TestCase("you around?")]
    [TestCase("?")]
    public void AContentFreeOpenerKeepsItsAck(string text) => Assert.That(Budget.ToContent(Tell(text), new Random(1)), Is.Null);

    // ---- 4. fewer silences, by intent and channel -----------------------------------------------------------------------------

    private static Budget.Moment Heard(string text, string channel) =>
        new(text, channel, false, AutonomousChatIntentModel.Predict(text), "Thane", false, false, false);

    [Test]
    public void ShortBanterLaughsAndSympathyComeBackAtARateByChannel()
    {
        Assert.That(Budget.ReactionShare("group"), Is.GreaterThan(Budget.ReactionShare("guild")));
        Assert.That(Budget.ReactionShare("guild"), Is.GreaterThan(Budget.ReactionShare("faction")));
        Assert.That(Budget.ReactionShare("faction"), Is.GreaterThan(0));
        Assert.That(Budget.ReactionShare("broadcast"), Is.EqualTo(0));
        var yes = new FixedRandom(0.0);
        Assert.That(Budget.Rescue(Heard("lol the pet ran off and pulled half the zone", "group"), "intent banter", yes), Is.AnyOf("lol", "heh", "haha", "lol nice", "hehe", "rofl"));
        Assert.That(Budget.ReactionKind("ugh died again, lost 2 bubbles", "shape complaint", Intent.Unknown), Is.EqualTo("sympathy"));
        Assert.That(Budget.Rescue(Heard("lol", "faction"), "intent laugh", new FixedRandom(0.99)), Is.Null, "not every time");
    }

    [Test]
    public void AGroupOrGuildQuestionGetsADunnoAndSpamOrOthersNothing()
    {
        var yes = new FixedRandom(0.0);
        Assert.That(Budget.Rescue(Heard("what lvl is the named here?", "group"), "not sure", yes), Does.Contain("dunno").Or.Contain("idea").Or.Contain("sure").Or.Contain("idk").Or.Contain("clue"));
        Assert.That(Budget.Rescue(Heard("who wants a group? im 28", "guild"), "intent grouping", yes), Is.Null, "a group post isn't a dunno question");
        Assert.That(Budget.Rescue(Heard("brb dinner, can someone hold my spot?", "group"), "not sure", yes), Is.Null);
        Assert.That(Budget.Rescue(Heard("x", "group"), "none other", yes), Is.Null);
        Assert.That(Budget.Rescue(Heard("u suck", "group"), "intent abuse", yes), Is.Null);
        Assert.That(Budget.Rescue(Heard("can someone heal me pls", "group"), "not sure", yes), Does.Contain("heal"), "the truth for the bot's class");
        Assert.That(Budget.Rescue(Heard("dinged 30 now what spec lol", "guild"), "shape ask_advice", yes), Does.Contain("gz").Or.Contain("grats"));
    }

    // ---- 6. roleplay in kind --------------------------------------------------------------------------------------------------

    [TestCase("Halt! State thy business at the gate!", "challenge")]
    [TestCase("Knowest thou where the healer dwells?", "question")]
    [TestCase("Hail and well met, friend.", "greeting")]
    [TestCase("*lights a candle for the fallen*", "mourn")]
    [TestCase("*raises a tankard* To Midgard!", "toast")]
    [TestCase("*bows deeply*", "emote")]
    public void ARoleplayLineIsReadForWhatItDoes(string text, string act) => Assert.That(AutonomousChatShortTalk.RoleplayAct(text), Is.EqualTo(act));

    private static VC Corpus()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
            return reader.ReadToEnd();
        }
        return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
    }

    [Test]
    public void EveryRoleplayActHasRepliesInKindAndNoneIsAMusing()
    {
        VC corpus = Corpus();
        foreach (string act in new[] { "greeting", "challenge", "question", "mourn", "toast", "emote", "remark" })
        {
            VC.Line[] lines = corpus.Lines.Where(line => line.Kind == "reply" && line.Answers.Contains("player_rp_" + act)).ToArray();
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(5), act);
            Assert.That(lines.Where(AutonomousChatFiller.IsMusing), Is.Empty, act);
        }
        Assert.That(corpus.Lines.Where(line => line.Answers.Contains("player_rp_mourn")).All(line => !line.Text.Contains("grin")), "no grin at a mourning");
    }

    // ---- 7. intent and alarm fixes --------------------------------------------------------------------------------------------

    [Test]
    public void IntentFixesFromTheSecondRound()
    {
        Assert.That(AutonomousChatShortTalk.QuestionShape("50 cleric lfg rvr, rr4"), Is.Not.EqualTo("player_goodnews"), "an lfg post isn't good news");
        Assert.That(AutonomousChatIntentModel.Predict("game crashed 3 times today, thx mythic"), Is.Not.EqualTo(Intent.Thanks), "sarcasm");
        Assert.That(AutonomousChatShortTalk.QuestionShape("game crashed 3 times today, thx mythic"), Is.EqualTo("complaint"));
        Assert.That(AutonomousChatIntentModel.Predict("free rps at mmg, mids are afk"), Is.EqualTo(Intent.RvR), "a rally, not an away");
    }

    private static (string Route, string Text)? Context(string text, string channel = "faction", int level = 45) =>
        AutonomousChatContext.Reply(text, AutonomousChatIntentModel.Predict(text), AutonomousChatShortTalk.QuestionShape(text),
            new AutonomousChatContext.Facts("Brannoc", "Wynn", channel, false, false, false, level, null, "Jordheim"), new Random(3));

    [Test]
    public void CallsGetTheRightShortAnswer()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var random = new Random(seed);
            (string Route, string Text)? At(string text, string channel = "faction", int level = 45) =>
                AutonomousChatContext.Reply(text, AutonomousChatIntentModel.Predict(text), AutonomousChatShortTalk.QuestionShape(text),
                    new AutonomousChatContext.Facts("Brannoc", "Wynn", channel, false, false, false, level, null, "Jordheim"), random);
            Assert.That(At("zerg at mmg, 40+ hibs")?.Text, Does.Not.Contain("how many").And.Not.Contain("where"), "says both already");
            Assert.That(At("hibs at the amg, 3 grps", level: 10)?.Text, Does.Not.Contain("how many").And.Not.Contain("where"));
            Assert.That(At("inc 3 albs")?.Text, Does.Contain("where"), "no place: where?");
            Assert.That(At("fake inc dont bother")?.Text, Is.AnyOf("k", "kk", "ok", "k ty"));
            Assert.That(At("INC INC INC", "group")?.Text, Is.AnyOf("where", "where?", "which way", "where?!"), "terse in a group");
            Assert.That(At("loot the named", "group")?.Route, Is.EqualTo("context loot"));
            Assert.That(At("free rps at mmg, mids are afk")?.Text, Does.Contain("omw").Or.Contain("way").Or.Contain("coming"));
            Assert.That(At("want a res?", "group")?.Text, Does.Contain("good").Or.Contain("ok").Or.Contain("ty"), "an offer to the bot gets a polite no");
        }
    }

    // ---- 9. polish ------------------------------------------------------------------------------------------------------------

    [Test]
    public void StuckIsForThePlayerNotAPetOrAMob()
    {
        Assert.That(Context("my pet is stuck behind the door")?.Route, Is.Not.EqualTo("context stuck"));
        Assert.That(Context("the mob is stuck in a rock lol")?.Route, Is.Not.EqualTo("context stuck"));
        Assert.That(Context("help im stuck under this rock")?.Route, Is.EqualTo("context stuck"));
    }

    [Test]
    public void ATidyWriterStillTypesNpBackToItInLowercase()
    {
        VC.Voice proper = Corpus().Voices.Single(voice => voice.Name == "proper");
        Assert.That(VC.Style("np, back to it", proper, new Random(1)), Is.EqualTo("np, back to it"));
    }

    // ---- 8. ambient realism ---------------------------------------------------------------------------------------------------

    [Test]
    public void ATemplatesOtherWaysRestAfterOneIsSaid()
    {
        var a = new VC.Line("a1", "ambient", "general", [], "*", [], 1, 50, [], "", "", 1, "that horse ride took so long i aged a year") { Family = "f1" };
        var b = a with { Id = "a2", Text = "that horse ride took so long i grew a beard" };
        var c = a with { Id = "c1", Family = "", Text = "anyone near prydwen want to duo" };
        VC corpus = Corpus();
        var recent = new VC.RecentLines();
        var speaker = new VC.Speaker(7, "Bot", eRealm.Albion, "Armsman", 30, BotPersonalityType.Leveler, new System.Collections.Generic.HashSet<string>());
        var now = new DateTime(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc);
        recent.Use(eRealm.Albion, a.Id, now);
        recent.Use(eRealm.Albion, "f:" + a.Family, now);
        Assert.That(recent.UsedWithin(eRealm.Albion, "f:" + b.Family, now.AddHours(1), VC.FamilyRest), Is.True, "its sibling rests");
        Assert.That(recent.UsedWithin(eRealm.Albion, "f:" + b.Family, now.AddHours(7), VC.FamilyRest), Is.False);
        Assert.That(corpus.Lines.Count(line => line.Family.Length > 0), Is.GreaterThan(10_000), "the corpus marks its families");
    }

    [Test]
    public void NoLineLeaksATemplatesBrackets()
    {
        Assert.That(Corpus().Lines.Where(line => line.Text.Contains('[') || line.Text.Contains(']')).Select(line => line.Text).Take(5), Is.Empty);
    }

    [Test]
    public void QuipsAndPreachyLinesAreRareFactionOpenersOnly()
    {
        var quip = new VC.Line("q", "ambient", "banter", [], "*", [], 1, 50, [], "", "", 1, "the healer npc greets me by name now") { Invites = "joke" };
        var preachy = quip with { Invites = "chat", Text = "asking before u loot is basic manners" };
        Assert.That(AutonomousChatFiller.AmbientAllowed(quip, false, new FixedRandom(0.0)), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(quip, true, new FixedRandom(0.5)), Is.False);
        Assert.That(AutonomousChatFiller.AmbientAllowed(quip, true, new FixedRandom(0.1)), Is.True);
        Assert.That(AutonomousChatFiller.IsMusing(preachy), Is.True);
    }
}
