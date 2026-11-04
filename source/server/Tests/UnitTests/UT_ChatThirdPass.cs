using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Intent = DOL.GS.eAutonomousChatIntent;
using Budget = DOL.GS.AutonomousChatReplyBudget;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Screen = DOL.GS.AutonomousChatScreen;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Beta 1.1.2, the third pass after blind round 3, one group of tests per fix category: ambient lines from the field, scams get
/// silence, tell replies with content, reactions that fit the line, state consistency, and /say lines aimed at a bot. Written
/// from the categories; the round 3 lines were not seen.
/// </summary>
[TestFixture]
public class UT_ChatThirdPass
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static readonly Field.WorldView Quiet = new(["Caer Benowyc", "Caer Berkstead"], [], [], [], 0, 0, "Forest Sauvage");
    private static readonly Field.WorldView Busy = new(["Caer Benowyc", "Caer Berkstead"], ["Caer Benowyc"], ["Caer Renaris"], ["Midgard"], 0, 1,
        "Forest Sauvage");

    private static Field.Facts Facts(string cls = "Berserker", int level = 38, Field.WorldView world = null, bool rvr = false, bool dinged = false,
        bool died = false, string item = "Bloodied Chain Hauberk", bool gear = true, bool group = false, string zone = "Vale of Mularn") =>
        new(4242, eRealm.Midgard, cls, level, zone, "Vendo Caverns", rvr, false, false, group, group ? 4 : 1, dinged, died, false, true, "frost wolf",
            item, 97, gear, null, 3, 20, false, world ?? Quiet);

    private static List<Field.Said> Draw(string channel, Field.Facts facts, int count, int seed = 1)
    {
        var random = new Random(seed);
        var lines = new List<Field.Said>();
        for (int i = 0; i < count; i++)
            if (Field.Line(channel, facts, null, random) is { } said)
                lines.Add(said);
        return lines;
    }

    // ---- 1. ambient lines from the field -------------------------------------------------------------------------------------

    [Test]
    public void FieldLinesAreShortLowercaseAndUnstopped()
    {
        foreach (string channel in new[] { "faction", "lfg", "trade", "say", "broadcast" })
            foreach (Field.Said said in Draw(channel, Facts(world: Busy), 300))
            {
                Assert.That(said.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.LessThanOrEqualTo(9), said.Text);
                Assert.That(said.Text, Is.EqualTo(said.Text.ToLowerInvariant()), said.Text);
                Assert.That(said.Text.EndsWith('.'), Is.False, said.Text);
                Assert.That(said.Text, Does.Not.Contain("{").And.Not.Contain("["), said.Text);
            }
    }

    [Test]
    public void FieldLinesNameTheBotsRealLevelClassAndBagItem()
    {
        foreach (Field.Said said in Draw("lfg", Facts(), 200))
            Assert.That(said.Text, Does.Contain("38").Or.Contain("zerk").Or.Contain("berserker"), said.Text);
        Assert.That(Draw("lfg", Facts(), 200).Count(said => said.Text.Contains("38")), Is.GreaterThan(150), "lfg lines name the real level");
        List<Field.Said> sales = Draw("trade", Facts(), 300).Where(said => said.Kind == "wts").ToList();
        Assert.That(sales, Is.Not.Empty);
        Assert.That(sales.All(said => said.Text.Contains("bloodied chain hauberk")), Is.True, "the item really in its bags");
        Assert.That(Draw("trade", Facts(item: "Spider Silk", gear: false), 300).Where(said => said.Kind == "wts").Any(said => said.Text.Contains('%')), Is.False,
            "no quality on loot that isn't gear");
    }

    [Test]
    public void CallsAndKeepTalkFollowTheRealFrontier()
    {
        List<Field.Said> quiet = Draw("faction", Facts(rvr: true, world: Quiet), 400);
        Assert.That(quiet.Any(said => said.Kind is "inc" or "ask_keep" or "relic"), Is.False, "no calls with no keep under attack, none lost, no relic gone");
        List<Field.Said> busy = Draw("faction", Facts(rvr: true, world: Busy), 400);
        Field.Said[] calls = busy.Where(said => said.Kind == "inc").ToArray();
        Assert.That(calls, Is.Not.Empty);
        Assert.That(calls.All(said => said.Text.Contains("beno")), Is.True, "a call names the keep really under attack");
        Assert.That(busy.Where(said => said.Kind == "ask_keep").All(said => said.Text.Contains("rena") && !said.Text.Contains("beno")), Is.True);
        Assert.That(busy.Where(said => said.Family is "keep_who" or "keep_still" or "keep_ours").All(said => said.Answers.All(answer => answer.Contains("mid"))),
            Is.True, "the true owner");
    }

    [Test]
    public void DingsAndDeathsOnlyWhenTheyHappened()
    {
        Assert.That(Draw("faction", Facts(), 400).Any(said => said.Kind is "ding" or "death"), Is.False);
        Assert.That(Draw("faction", Facts(dinged: true), 200).Any(said => said.Kind == "ding"), Is.True);
        Assert.That(Draw("faction", Facts(died: true), 200).Any(said => said.Kind == "death"), Is.True);
    }

    [Test]
    public void ARealmDoesNotHearTheSameFieldLineTwiceInAnEvening()
    {
        var memory = new Field.Memory();
        var random = new Random(5);
        var skeletons = new HashSet<string>();
        for (int i = 0; i < 300; i++)
        {
            Field.Facts facts = Facts(cls: i % 2 == 0 ? "Thane" : "Healer", level: 20 + i % 25, world: Busy);
            if (Field.Line(i % 3 == 0 ? "lfg" : "faction", facts, memory, random) is { } said)
                Assert.That(skeletons.Add(AutonomousChatFiller.Skeleton(said.Text)), Is.True, said.Text);
        }
    }

    [Test]
    public void ChatWritesNamesTheWayPlayersDid()
    {
        var random = new Random(3);
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ShortClass("Berserker", random)), Is.All.AnyOf("zerk", "zerker"));
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ShortKeep("Caer Benowyc", random)), Is.All.AnyOf("benowyc", "beno", "benow"));
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ShortKeep("Bledmeer Faste", random)), Is.All.AnyOf("bledmeer", "bled", "bledm"));
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ShortDungeon("Treibh Caillte", random)), Is.All.AnyOf("tc", "treibh"));
    }

    [Test]
    public void MostUnpromptedLinesComeFromTheFieldAndCraftedLinesAreRare()
    {
        Assert.That(Field.Share("faction"), Is.GreaterThanOrEqualTo(0.85));
        Assert.That(Field.Share("lfg"), Is.GreaterThanOrEqualTo(0.85));
        Assert.That(AutonomousChatFiller.CraftedShare, Is.LessThanOrEqualTo(0.15));
        VC.Line Line(string topic, string text, string invites = "chat") => new("x", "ambient", topic, [], "*", [], 1, 50, [], "", invites, 1, text);
        Assert.That(AutonomousChatFiller.IsCrafted(Line("banter", "the healer npc greets me by name now", "joke")), Is.True, "a crafted joke");
        Assert.That(AutonomousChatFiller.IsCrafted(Line("grouping", "you should always ask before you loot the named")), Is.True, "an unprompted tip");
        Assert.That(AutonomousChatFiller.IsCrafted(Line("grouping", "two levels this week, not bad for someone with a job")), Is.True, "life filler");
        Assert.That(AutonomousChatFiller.IsCrafted(Line("lore", "queen kula built the citadel on the bones of a dragon")), Is.True);
        Assert.That(AutonomousChatFiller.IsCrafted(Line("grouping", "anyone up for vendo")), Is.False);
        // One draw per opener: a crafted line is allowed for the whole opener, or for none of it.
        Func<VC.Line, bool> no = AutonomousChatFiller.AmbientFilter(true, new FixedRandom(0.5));
        Assert.That(no(Line("banter", "the healer npc greets me by name now", "joke")), Is.False);
        Assert.That(no(Line("grouping", "anyone up for vendo")), Is.True);
        Assert.That(AutonomousChatFiller.AmbientFilter(false, new FixedRandom(0.0))(Line("banter", "the healer npc greets me by name now", "joke")), Is.False,
            "never outside /faction");
    }

    // ---- 2. scams get silence, never compliance ------------------------------------------------------------------------------

    [TestCase("hey, i can double your gold, trade me 10g and ill give 20 back")]
    [TestCase("free plat for the first 5 people who send me a tell")]
    [TestCase("hi, im a gm, we need your password to fix your account")]
    [TestCase("whats ur login? ill power level u for free")]
    [TestCase("buying accounts, any level, good prices")]
    [TestCase("wtb your account, pst")]
    [TestCase("GM team notice: your character is under review")]
    [TestCase("Mythic staff here, please confirm your account details")]
    [TestCase("giving away free gold in camelot, just send me 1g first")]
    public void ScamsAreScreened(string text)
    {
        Assert.That(Screen.IsSpam(text), Is.True, text);
        var moment = new Budget.Moment(text, "tell", true, AutonomousChatIntentModel.Predict(text), "Thane", false, false, false);
        Assert.That(Budget.ToContent(moment, new Random(1)), Is.Null, "no short reply");
        Assert.That(Budget.Rescue(moment with { Channel = "group", Addressed = false }, "not sure", new FixedRandom(0.0)), Is.Null, "no reaction");
    }

    [TestCase("free rps at amg, mids are afk")]
    [TestCase("anyone have a spare moment to help me with a quest")]
    [TestCase("ty for the free port")]
    [TestCase("lf2m vendo, need healer")]
    [TestCase("selling mc chain 3p obo")]
    public void ChatAboutTheGameIsNotAScam(string text) => Assert.That(Screen.IsSpam(text), Is.False, text);

    // ---- 3. short tell replies carry content ---------------------------------------------------------------------------------

    private static readonly AutonomousChatQuestions.BotFacts Thane = new(77, 30, "Thane", eRealm.Midgard, 12, BotPersonalityType.Leveler, false, 2,
        null, 6, 1, Spec: "30 stormcalling, 20 hammer", Weapon: "Runed Great Hammer", WeaponQuality: 99);

    private static Budget.Moment Tell(string text, AutonomousChatQuestions.BotFacts self = null, bool heal = false, bool rez = false) =>
        new(text, "tell", true, AutonomousChatIntentModel.Predict(text), "Thane", heal, rez, false, null, self ?? Thane);

    [TestCase("spec?", "stormcalling")]
    [TestCase("whats ur spec", "stormcalling")]
    [TestCase("what lvl r u", "30")]
    [TestCase("lvl?", "30")]
    [TestCase("what weapon is that?", "hammer")]
    [TestCase("is that a 99%?", "99")]
    [TestCase("what class r u", "thane")]
    public void AQuestionAboutTheBotIsAnsweredFromItsState(string text, string expected)
    {
        for (int seed = 0; seed < 6; seed++)
        {
            string reply = Budget.ToContent(Tell(text), new Random(seed));
            Assert.That(reply, Does.Contain(expected), text);
            Assert.That(reply, Does.Not.Contain("no clue").And.Not.Contain("dunno"), text);
        }
    }

    [Test]
    public void AnAgeQuestionGetsTheSameNumberEachTime()
    {
        string[] answers = Enumerable.Range(0, 6).Select(seed => Budget.ToContent(Tell("how old r u?"), new Random(seed))).ToArray();
        Assert.That(answers.All(answer => answer != null && System.Text.RegularExpressions.Regex.IsMatch(answer, @"\b\d{2}\b")), Is.True, string.Join("|", answers));
        Assert.That(answers.Select(answer => System.Text.RegularExpressions.Regex.Match(answer, @"\d{2}").Value).Distinct().Count(), Is.EqualTo(1));
    }

    [TestCase("wanna buy my 99% chain legs? 2p")]
    [TestCase("want a ring? selling it cheap")]
    [TestCase("wts nice sword, u interested?")]
    public void AnOfferGetsAYesANoOrAPrice(string text)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            string reply = Budget.ToContent(Tell(text), new Random(seed));
            Assert.That(reply, Is.AnyOf("how much?", "what %?", "nah ty", "not buying sry", "maybe, how much", "nah im good"), text);
        }
    }

    // ---- 4. reactions fit the line -------------------------------------------------------------------------------------------

    private static Budget.Moment Heard(string text, string channel = "faction", string cls = "Thane", bool heal = false) =>
        new(text, channel, false, AutonomousChatIntentModel.Predict(text), cls, heal, false, false);

    [TestCase("zerg at mmg, 30+ hibs")]
    [TestCase("hibs at bled door")]
    [TestCase("inc 8 albs at amg")]
    public void AWarningWithAPlaceGetsOmwNeverSame(string text)
    {
        Assert.That(Budget.ReactionKind(text, "intent rvr", AutonomousChatIntentModel.Predict(text)), Is.EqualTo("alarm"));
        for (int seed = 0; seed < 10; seed++)
            Assert.That(Budget.Rescue(Heard(text), "intent rvr", new SeededYes(seed)), Is.AnyOf("omw", "k", "on it", "otw"), text);
    }

    [Test]
    public void AWarningWithNoPlaceGetsWhere()
    {
        for (int seed = 0; seed < 10; seed++)
            Assert.That(Budget.Rescue(Heard("INC!!"), "none unknown", new SeededYes(seed)), Is.AnyOf("where?", "where", "where?!", "which way"));
    }

    [Test]
    public void ACallNeverDrawsAPlusOneOrSame()
    {
        Assert.That(AutonomousChatArbiter.ReactionKind("context rvr", Intent.RvR), Is.Null);
        Assert.That(AutonomousChatArbiter.ReactionKind("context inc", Intent.Alarm), Is.Null);
        Assert.That(AutonomousChatArbiter.ReactionKind("shape complaint", Intent.Unknown), Is.EqualTo("agree"), "a gripe still may");
    }

    [TestCase("need heal!!")]
    [TestCase("heal pls im dying")]
    [TestCase("someone heal me")]
    public void ARequestForHealsGetsTheTruthForTheClass(string text)
    {
        Assert.That(Budget.ReactionKind(text, "not sure", AutonomousChatIntentModel.Predict(text)), Is.EqualTo("request"));
        for (int seed = 0; seed < 10; seed++)
        {
            string thane = Budget.Rescue(Heard(text, "group"), "not sure", new SeededYes(seed));
            Assert.That(thane, Is.AnyOf("cant heal sry", "use a pot", "thane, no heals", "pot up!"), text);
            string druid = Budget.Rescue(Heard(text, "group", "Druid", heal: true), "not sure", new SeededYes(seed));
            Assert.That(druid, Is.AnyOf("omw, where?", "where r u?", "on it"), text);
        }
        Assert.That(Budget.ToContent(Tell(text), new Random(1)), Does.Contain("heal").And.Not.Contain("good"), "never 'nah im good ty' to a request");
    }

    [Test]
    public void AnOfferIsNotARequest()
    {
        Assert.That(Budget.IsRequest("want a rez?"), Is.False);
        Assert.That(Budget.IsRequest("u need heals?"), Is.False);
        Assert.That(Budget.IsRequest("need rez pls"), Is.True);
        Assert.That(Budget.ToContent(Tell("want a rez?"), new Random(2)), Is.AnyOf("nah im good ty", "im ok ty", "all good ty", "nah ty"));
    }

    [Test]
    public void JokesAndBadLuckKeepTheirReactions()
    {
        Assert.That(Budget.ReactionKind("lol my pet just ate the quest npc", "intent laugh", Intent.Laugh), Is.EqualTo("laugh"));
        Assert.That(Budget.ReactionKind("ugh died again, lost a bubble", "shape complaint", Intent.Unknown), Is.EqualTo("sympathy"));
    }

    // ---- 5. state consistency -----------------------------------------------------------------------------------------------

    [TestCase("45+ dragon raid tonight, x me", 45, 50)]
    [TestCase("lfm tomb 20-25 need tank", 20, 25)]
    [TestCase("lvl 40 and up for df", 40, 50)]
    [TestCase("need 35 or higher for barrows", 35, 50)]
    public void ALevelBandIsRead(string text, int min, int max) => Assert.That(AutonomousChatShortTalk.LevelBand(text), Is.EqualTo((min, max)));

    [TestCase("wts 99% sword 2p")]
    [TestCase("lf2m for vendo")]
    [TestCase("brb 5 mins")]
    [TestCase("ding 30!")]
    public void NoBandWhereNoneIsAsked(string text) => Assert.That(AutonomousChatShortTalk.LevelBand(text), Is.Null);

    [Test]
    public void ABotOutsideTheBandSaysSoInsteadOfJoining()
    {
        var random = new Random(4);
        Assert.That(AutonomousBotChatCoordinator.BandReply(30, "45+ dragon raid tonight, x me", random), Does.Contain("low").Or.Contain("only 30"));
        Assert.That(AutonomousBotChatCoordinator.BandReply(42, "lfm tomb 20-25 need tank", random), Does.Contain("high"));
        Assert.That(AutonomousBotChatCoordinator.BandReply(47, "45+ dragon raid tonight, x me", random), Is.Null);
        Assert.That(Budget.ToContent(Tell("45+ dragon raid tonight, want in?"), random), Does.Contain("low").Or.Contain("only 30").Or.Contain("30 here"));
    }

    [Test]
    public void ABotNeverTakesThanksForWhatItCantDo()
    {
        Assert.That(Budget.CreditFor("ty for the heals!"), Is.EqualTo("heals"));
        Assert.That(Budget.CreditFor("thx for rez"), Is.EqualTo("rez"));
        Assert.That(Budget.ClaimsCredit("ty for the heals!", "np", canHeal: false, canRez: false), Is.True);
        Assert.That(Budget.ClaimsCredit("ty for the heals!", "np", canHeal: true, canRez: false), Is.False);
        Assert.That(Budget.ClaimsCredit("thx for rez", "anytime", canHeal: true, canRez: false), Is.True);
        Assert.That(Budget.ClaimsCredit("ty all", "np", canHeal: false, canRez: false), Is.False);
        string reply = Budget.ToContent(Tell("ty for the heals!"), new Random(1));
        Assert.That(reply, Does.Contain("not me").Or.Contain("wasnt me"), "a thane says it wasn't it");
        Assert.That(Budget.Rescue(Heard("ty for the rez", "group"), "intent thanks", new FixedRandom(0.0)), Is.Null);
    }

    // ---- 6. /say lines aimed at a bot ----------------------------------------------------------------------------------------

    [Test]
    public void ASayLineGoesToTheBotTargetedFollowedOrFacedUpClose()
    {
        var far = new Screen.Near(1, 400, true, false, false);
        var close = new Screen.Near(2, 120, true, false, false);
        var behind = new Screen.Near(3, 120, false, false, false);
        Assert.That(Screen.SayAddressee([far, close], 0), Is.EqualTo(2), "the one right in front, nobody else close");
        Assert.That(Screen.SayAddressee([far, close], 1), Is.Null, "another player is that close too");
        Assert.That(Screen.SayAddressee([close, behind], 0), Is.Null, "two bots that close: to the room");
        Assert.That(Screen.SayAddressee([behind], 0), Is.Null, "not facing it");
        Assert.That(Screen.SayAddressee([far, close with { Targeted = false }, far with { Id = 9, Targeted = true }], 0), Is.EqualTo(9), "targeted first");
        Assert.That(Screen.SayAddressee([close, far with { Id = 7, SpokeLast = true }], 0), Is.EqualTo(7), "answers the bot's own line");
        Assert.That(Screen.SayAddressee([far], 0), Is.Null);
    }

    [Test]
    public void ALineAimedAtOneBotGetsOneAnswer()
    {
        Assert.That(AutonomousBotChatCoordinator.PlayerReplyCount(Intent.Greeting, false, true, 6, new Random(1)), Is.EqualTo(1));
        Assert.That(AutonomousChatArbiter.Arrange(3, Intent.Greeting, "intent greeting", true, false, 6, 2, new Random(1)).Total, Is.EqualTo(1));
    }

    /// <summary>Always under the share (so the reaction is said), with real picks for the wording.</summary>
    private sealed class SeededYes(int seed) : Random(seed)
    {
        public override double NextDouble() => 0.0;
    }
}
