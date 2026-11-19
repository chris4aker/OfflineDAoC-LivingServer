using System;
using System.Linq;
using NUnit.Framework;
using Budget = DOL.GS.AutonomousChatBudget;
using Why = DOL.GS.AutonomousChatWhy;

namespace DOL.GS.Tests;

/// <summary>
/// Living Server 1.2, roadmap item 6, "replies that don't give bots away": the reply budget counted apart from the ambient
/// budget (tells and a bot's name exempt), the meter's window, reason codes, self-knowledge (where the bot is) and the context
/// fits that keep a reply from saying something untrue of its bot or pasting an ad into another conversation.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_RepliesDontGiveBotsAway
{
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => (int)(value * maxValue);
        public override int Next(int minValue, int maxValue) => minValue + (int)(value * (maxValue - minValue));
    }

    [SetUp]
    public void Clear() => Why.Clear();

    #region The reply budget

    [Test]
    public void ATellOrABotsNameIsAlwaysAnswered()
    {
        var band = (Target: 1.0, Ceiling: 2.0);
        foreach (double measured in new[] { 0.0, 1.5, 2.0, 50.0 })
            Assert.That(Budget.DecideReply(Budget.ReplyKind.Direct, measured, band, 0.99), Is.EqualTo(Budget.ReplyVerdict.Exempt), $"at {measured}/min");
        Assert.That(Budget.Says(Budget.ReplyVerdict.Exempt));
    }

    [Test]
    public void ThePlayersFirstAnswerComesUpToTheCeilingAndBotsLeaveTheRoomAboveTheTargetToIt()
    {
        var band = (Target: 1.0, Ceiling: 2.0);
        // Under the target: everyone speaks.
        foreach (Budget.ReplyKind kind in new[] { Budget.ReplyKind.ToPlayer, Budget.ReplyKind.PileOn, Budget.ReplyKind.BotThread, Budget.ReplyKind.Burst })
            Assert.That(Budget.DecideReply(kind, 0.5, band, 0.99), Is.EqualTo(Budget.ReplyVerdict.Speak), kind.ToString());
        // Between the target and the ceiling: the player's answer and a burst still come; bots answering bots fade out.
        Assert.That(Budget.DecideReply(Budget.ReplyKind.ToPlayer, 1.9, band, 0.99), Is.EqualTo(Budget.ReplyVerdict.SpeakOverTarget));
        Assert.That(Budget.DecideReply(Budget.ReplyKind.Burst, 1.9, band, 0.99), Is.EqualTo(Budget.ReplyVerdict.SpeakOverTarget));
        Assert.That(Budget.DecideReply(Budget.ReplyKind.BotThread, 1.25, band, 0.7), Is.EqualTo(Budget.ReplyVerdict.SpeakOverTarget), "75% room left");
        Assert.That(Budget.DecideReply(Budget.ReplyKind.BotThread, 1.25, band, 0.8), Is.EqualTo(Budget.ReplyVerdict.HoldTarget));
        Assert.That(Budget.DecideReply(Budget.ReplyKind.PileOn, 1.9, band, 0.2), Is.EqualTo(Budget.ReplyVerdict.HoldTarget), "10% room left");
        // At the ceiling nothing but a direct reply.
        foreach (Budget.ReplyKind kind in new[] { Budget.ReplyKind.ToPlayer, Budget.ReplyKind.PileOn, Budget.ReplyKind.BotThread, Budget.ReplyKind.Burst })
            Assert.That(Budget.DecideReply(kind, 2.0, band, 0.0), Is.EqualTo(Budget.ReplyVerdict.HoldCeiling), kind.ToString());
        // A channel that doesn't budget its replies holds none.
        Assert.That(Budget.DecideReply(Budget.ReplyKind.BotThread, 99, (0, 0), 0.99), Is.EqualTo(Budget.ReplyVerdict.Speak));
    }

    [Test]
    public void RepliesAndAmbientLinesAreCountedApart()
    {
        var meter = new Budget.ChannelMeter();
        for (int i = 0; i < 6; i++)
            meter.Count(Budget.Channel.Faction, 1, 10_000 + i * 1_000, Budget.LineKind.Reply);
        meter.Count(Budget.Channel.Faction, 1, 17_000);
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 1, 20_000), Is.EqualTo(3.5), "every line, two minutes");
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 1, 20_000, Budget.LineKind.Reply, 120_000), Is.EqualTo(3.0));
        Assert.That(meter.PerMinute(Budget.Channel.Faction, 1, 20_000, Budget.LineKind.Ambient, 120_000), Is.EqualTo(0.5));
        // The ambient gate doesn't see the replies: they no longer take the openers' turns.
        var band = (Floor: 0.5, Target: 2.0, Ceiling: 3.0);
        Assert.That(meter.TryStart(Budget.Channel.Faction, 1, 21_000, band, new FixedRandom(0.5)), Is.EqualTo(Budget.Verdict.Speak));
        // The 1.1.2 gate counted them all: at 3.5 a minute this channel was over its ceiling.
        var old = new Budget.ChannelMeter { CountsEveryLineOverTwoMinutes = true };
        for (int i = 0; i < 6; i++)
            old.Count(Budget.Channel.Faction, 1, 10_000 + i * 1_000, Budget.LineKind.Reply);
        old.Count(Budget.Channel.Faction, 1, 17_000);
        Assert.That(old.TryStart(Budget.Channel.Faction, 1, 21_000, band, new FixedRandom(0.5)), Is.EqualTo(Budget.Verdict.HoldCeiling));
        // The reply budget reads the replies, over the window its target needs (eight replies at 1 a minute: eight minutes).
        Assert.That(meter.RepliesPerMinute(Budget.Channel.Faction, 1, 21_000, 1.0), Is.EqualTo(0.75));
        for (int i = 0; i < 14; i++)
            meter.Count(Budget.Channel.Faction, 1, 18_000 + i * 100, Budget.LineKind.Reply);
        Assert.That(meter.RepliesPerMinute(Budget.Channel.Faction, 1, 21_000, 1.0), Is.EqualTo(2.5));
        Assert.That(meter.TryReply(Budget.Channel.Faction, 1, 21_000, (Target: 1.0, Ceiling: 2.0), Budget.ReplyKind.BotThread, new FixedRandom(0)),
            Is.EqualTo(Budget.ReplyVerdict.HoldCeiling));
        Assert.That(meter.TryReply(Budget.Channel.Faction, 1, 21_000, (Target: 1.0, Ceiling: 2.0), Budget.ReplyKind.Direct, new FixedRandom(0)),
            Is.EqualTo(Budget.ReplyVerdict.Exempt));
    }

    [Test]
    public void ASlowChannelsFloorIsReadOverALongEnoughWindow()
    {
        // The playtester's faction-rate runs (PR #72): 10 gamebots a realm at 5 pm, bot_voices_chat_rate 2. /faction's band was
        // about 0.17 / 0.34 / 0.85 a minute; it ran at 1.3-1.4.
        Budget.Settings settings = Budget.Current;
        var band = Budget.Band(settings, Budget.Channel.Faction, 10, 17, false, 2.0);
        Assert.That(band.Target, Is.InRange(0.3, 0.4));
        Assert.That(Budget.WindowMs(band.Target), Is.InRange(600_000, 900_000), "about 11 minutes: four lines at the target");
        Assert.That(Budget.WindowMs(Budget.Band(settings, Budget.Channel.Faction, 1_000, 20, false, 2.0).Target), Is.EqualTo(Budget.MinWindowMs),
            "a full server keeps the two minutes");
        Assert.That(Budget.WindowMs(0.01), Is.EqualTo(Budget.MaxWindowMs));
        Assert.That(Budget.WindowMs(0), Is.EqualTo(Budget.MaxWindowMs));
        // Two openers in the last three minutes. 1.1.2 read the last two minutes, saw one line (0.5 a minute, over the floor) and,
        // a minute later, none, so "under the floor" fired and a bot spoke at once, every quiet two minutes: about 0.5 a minute of
        // openers alone, half again over the target, before any answer.
        var random = new FixedRandom(0.5);
        var old = new Budget.ChannelMeter { CountsEveryLineOverTwoMinutes = true };
        var meter = new Budget.ChannelMeter();
        foreach (Budget.ChannelMeter m in new[] { old, meter })
        {
            m.Count(Budget.Channel.Faction, 1, 1_000);
            m.Count(Budget.Channel.Faction, 1, 61_000);
            m.Delay(Budget.Channel.Faction, 1, 400_000);   // the gap isn't over
        }
        Assert.That(old.TryStart(Budget.Channel.Faction, 1, 190_000, band, random), Is.EqualTo(Budget.Verdict.SpeakUnderFloor));
        Assert.That(meter.TryStart(Budget.Channel.Faction, 1, 190_000, band, random), Is.EqualTo(Budget.Verdict.HoldGap),
            "two lines in eleven minutes is the floor: the bot waits its gap");
        // Real silence still lets a bot speak at once.
        var quiet = new Budget.ChannelMeter();
        quiet.Count(Budget.Channel.Faction, 1, 1_000);
        quiet.Delay(Budget.Channel.Faction, 1, 900_000);
        Assert.That(quiet.TryStart(Budget.Channel.Faction, 1, 600_000, band, random), Is.EqualTo(Budget.Verdict.SpeakUnderFloor));
    }

    [Test]
    public void TheBuiltInBudgetHasReplyBudgetsForFactionAndSay()
    {
        Budget.Settings settings = Budget.Parse(Budget.BuiltIn());
        foreach (Budget.Channel channel in new[] { Budget.Channel.Faction, Budget.Channel.Say })
        {
            Budget.ChannelBand band = settings.Band(channel);
            Assert.That(band.BudgetsReplies, channel.ToString());
            Assert.That(band.ReplyCeiling, Is.GreaterThan(band.ReplyTarget), channel.ToString());
            // The owner's rule: re-balance, don't cut. Openers keep 1.1.2's target and replies get a band of their own on top.
            Assert.That(band.Target + band.ReplyTarget, Is.GreaterThan(channel == Budget.Channel.Faction ? 4.0 : 2.0), channel.ToString());
        }
        Assert.That(settings.Band(Budget.Channel.Faction).Target, Is.EqualTo(4.0), "the ambient /faction target is unchanged");
        Assert.That(settings.Band(Budget.Channel.Guild).BudgetsReplies, Is.False, "guild talk keeps one budget");
        // The reply band scales like the ambient one, and "0 = bots only answer" still answers.
        var full = Budget.ReplyBand(settings, Budget.Channel.Faction, 1_000, 20, false, 1.0);
        Assert.That(full.Target, Is.EqualTo(settings.Band(Budget.Channel.Faction).ReplyTarget).Within(1e-9));
        Assert.That(Budget.ReplyBand(settings, Budget.Channel.Faction, 10, 20, false, 1.0).Target, Is.LessThan(full.Target));
        Assert.That(Budget.ReplyBand(settings, Budget.Channel.Faction, 1_000, 20, false, 0).Target, Is.EqualTo(full.Target).Within(1e-9));
        Assert.That(Budget.ReplyBand(settings, Budget.Channel.Faction, 1_000, 20, false, 2.0).Target, Is.EqualTo(2 * full.Target).Within(1e-9));
        Assert.That(Budget.ReplyBand(settings, Budget.Channel.Lfg, 1_000, 20, false, 1.0), Is.EqualTo((0.0, 0.0)));
    }

    [Test]
    public void AFileWithoutReplyValuesKeepsOneBudgetPerChannel()
    {
        Budget.Settings settings = Budget.Parse("{ \"channels\": { \"faction\": { \"floor\": 1, \"target\": 2, \"ceiling\": 4 }, " +
                                                "\"say\": { \"target\": 1, \"reply_target\": 3, \"reply_ceiling\": 1 } } }");
        Assert.That(settings.Band(Budget.Channel.Faction).BudgetsReplies, Is.False, "a 1.1.2 copy of the file reads as before");
        Assert.That(settings.Band(Budget.Channel.Say).ReplyCeiling, Is.EqualTo(3), "a reply ceiling under its target is raised to it");
    }

    [Test]
    public void ScenesComeAtAFullServersPaceFrom300BotsAndLessOftenBelow()
    {
        foreach (int bots in new[] { 300, 900, 1_000, 3_000 })
            Assert.That(Budget.SceneScale(bots), Is.EqualTo(1.0), $"{bots} bots a realm keep today's pace");
        Assert.That(Budget.SceneScale(10), Is.EqualTo(Math.Sqrt(10 / 300.0)).Within(1e-9), "about a fifth as often at 10");
        Assert.That(Budget.SceneScale(0), Is.EqualTo(Budget.MinSceneScale), "never stopped");
        Assert.That(Budget.SceneScale(30), Is.GreaterThan(Budget.SceneScale(10)));
        Assert.That(Budget.SceneScale(150), Is.LessThan(1.0));
        // A debate planned 30 minutes out: due at 30 minutes on a full server, about 2.7 hours at 10 bots a realm.
        long planned = 1_000, due = planned + 30 * 60_000;
        Assert.That(Budget.SceneDue(planned, due, due, 900), Is.True);
        Assert.That(Budget.SceneDue(planned, due, due - 1, 900), Is.False);
        Assert.That(Budget.SceneDue(planned, due, due, 10), Is.False);
        Assert.That(Budget.SceneDue(planned, due, planned + 165 * 60_000, 10), Is.True);
        Assert.That(Budget.SceneDue(planned, due, planned + 160 * 60_000, 10), Is.False);
        // Scaled by the bots online when it comes due, not when it was planned (a server start has few bots on).
        Assert.That(Budget.SceneDue(planned, due, due, 0), Is.False);
        Assert.That(Budget.SceneDue(planned, due, due, 1_000), Is.True);
        // A GM's "start soon" (due at 0) is due at once on any roster.
        Assert.That(Budget.SceneDue(0, 0, 5, 1), Is.True);
        // A spammer's wave: 5-10 ads on a full server, two on a tiny one.
        Assert.That(Budget.SceneLines(10, 900), Is.EqualTo(10));
        Assert.That(Budget.SceneLines(5, 300), Is.EqualTo(5));
        Assert.That(Budget.SceneLines(10, 10), Is.EqualTo(2));
        Assert.That(Budget.SceneLines(10, 100), Is.EqualTo(6));
        Assert.That(Budget.SceneLines(1, 10), Is.EqualTo(1));
    }

    #endregion

    #region Reason codes

    private static Why.Decision Decision(string outcome, string rule, string channel = "faction", string trigger = "player Wynn", string code = null) =>
        new(1_000, 7, "Aldwin", eRealm.Albion, channel, trigger, outcome, rule, 2, null, "why", "text", "Wynn") { Code = code };

    [TestCase("silent", "silent (none unknown)", Why.Codes.BelowMinimum)]
    [TestCase("silent", "silent (none spam)", Why.Codes.Spam)]
    [TestCase("silent", "none other", Why.Codes.OtherAddressee)]
    [TestCase("silent", "none ack", Why.Codes.AckLine)]
    [TestCase("silent", "none undirected", Why.Codes.Undirected)]
    [TestCase("said", "short (none factual)", Why.Codes.ShortBelowMinimum)]
    [TestCase("ack", "ack (none unknown)", Why.Codes.ShortBelowMinimum)]
    [TestCase("said", "rescue (intent banter)", Why.Codes.RescueBelowMinimum)]
    [TestCase("said", "fact level", Why.Codes.SelfKnowledge)]
    [TestCase("said", "live where", Why.Codes.SelfKnowledge)]
    [TestCase("said", "silent (none contradicts (intent classtalk))", Why.Codes.ContradictsSelf)]
    [TestCase("pileon", "pileon laugh", Why.Codes.PileOn)]
    [TestCase("held", "reply budget botthread", Why.Codes.Held)]
    [TestCase("said", "intent greeting", Why.Codes.Sent)]
    public void EveryDecisionHasAReasonCode(string outcome, string rule, string code) =>
        Assert.That(Why.CodeOf(Decision(outcome, rule)), Is.EqualTo(code));

    [Test]
    public void AGivenCodeWinsAndTellsAndAmbientLinesReadAsSuch()
    {
        Assert.That(Why.CodeOf(Decision("held", "reply budget botthread", code: Why.Codes.HoldReplyTarget)), Is.EqualTo(Why.Codes.HoldReplyTarget));
        Assert.That(Why.CodeOf(Decision("said", "intent greeting", channel: "tell")), Is.EqualTo(Why.Codes.ExemptTell));
        Assert.That(Why.CodeOf(Decision("said", "field lfg", trigger: "ambient")), Is.EqualTo(Why.Codes.Ambient));
        Assert.That(Why.Codes.Of(Budget.ReplyVerdict.HoldCeiling, Budget.ReplyKind.ToPlayer), Is.EqualTo(Why.Codes.HoldReplyCeiling));
        Assert.That(Why.Codes.Of(Budget.ReplyVerdict.SpeakOverTarget, Budget.ReplyKind.ToPlayer), Is.EqualTo(Why.Codes.ReplyOverTarget));
        Assert.That(Why.Codes.Of(Budget.ReplyVerdict.Speak, Budget.ReplyKind.PileOn), Is.EqualTo(Why.Codes.PileOn));
        Assert.That(Why.Codes.Of(Budget.ReplyVerdict.Speak, Budget.ReplyKind.BotThread), Is.EqualTo(Why.Codes.ReplyInBudget));
    }

    [Test]
    public void AReplyToAPlayerKeepsTheMinimumFitsCodeOverTheBudgets()
    {
        Assert.That(AutonomousBotChatCoordinator.ReplyCode("short (none factual)", Why.Codes.ExemptNamed), Is.EqualTo(Why.Codes.ShortBelowMinimum));
        Assert.That(AutonomousBotChatCoordinator.ReplyCode("rescue (intent banter)", Why.Codes.ReplyInBudget), Is.EqualTo(Why.Codes.RescueBelowMinimum));
        Assert.That(AutonomousBotChatCoordinator.ReplyCode("pileon laugh", Why.Codes.ReplyInBudget), Is.EqualTo(Why.Codes.PileOn));
        Assert.That(AutonomousBotChatCoordinator.ReplyCode("intent greeting", Why.Codes.ExemptNamed), Is.EqualTo(Why.Codes.ExemptNamed));
        Assert.That(AutonomousBotChatCoordinator.ReplyCode("knowledge", Why.Codes.ReplyOverTarget), Is.EqualTo(Why.Codes.ReplyOverTarget));
    }

    [Test]
    public void LswhyMeFindsAPlayersOwnLinesAndShowsTheCode()
    {
        Why.Record(Decision("said", "intent greeting", code: Why.Codes.ExemptNamed));
        Why.Record(new Why.Decision(2_000, 8, "Brannoc", eRealm.Albion, "faction", "bot Aldwin", "said", "reply", 2, null, "answers", "lol", "Aldwin"));
        Why.RecordSilence(eRealm.Albion, "faction", "player Wynn", "none ack", 0, "an acknowledgement draws no answer", "Wynn", at: 3_000);
        var mine = Why.ForPlayer("wynn", 10);
        Assert.That(mine.Count, Is.EqualTo(2));
        Assert.That(Why.Describe(mine[0], 4_000), Does.Contain("silent [ack-line]"));
        Assert.That(Why.Describe(mine[1], 4_000), Does.Contain("said [exempt-named]"));
    }

    [Test]
    public void HeldRepliesAreCountedForTheBudgetReport()
    {
        Why.CountReply(Budget.Channel.Faction, Budget.ReplyVerdict.HoldTarget);
        Why.CountReply(Budget.Channel.Faction, Budget.ReplyVerdict.HoldCeiling);
        Why.CountReply(Budget.Channel.Faction, Budget.ReplyVerdict.Speak);
        Why.CountReply(Budget.Channel.Faction, Budget.ReplyVerdict.Exempt);
        Assert.That(Why.HeldCount(Budget.Channel.Faction, Why.Held.ReplyTarget), Is.EqualTo(1));
        Assert.That(Why.HeldCount(Budget.Channel.Faction, Why.Held.ReplyCeiling), Is.EqualTo(1));
        Assert.That(Why.RepliesAllowed(Budget.Channel.Faction), Is.EqualTo(2));
        Assert.That(Why.RepliesAllowed(Budget.Channel.Say), Is.EqualTo(0));
    }

    #endregion

    #region Self-knowledge and context fits

    private static AutonomousChatQuestions.BotFacts Facts(string cls, int level, string zone = "Camelot Hills") =>
        new(42, level, cls, eRealm.Albion, 0, BotPersonalityType.Leveler, false, 0, null, Zone: zone, Name: "Aldwin");

    [TestCase("im a cleric lol", "Thane", 30, true)]
    [TestCase("i am a healer, sec", "Wizard", 30, true)]
    [TestCase("healer here, inv?", "Wizard", 30, true)]
    [TestCase("thane here, inv?", "Thane", 30, false)]
    [TestCase("38 zerk here", "Berserker", 41, true)]
    [TestCase("41 zerk here", "Berserker", 41, false)]
    [TestCase("sm here", "Spiritmaster", 41, false)]
    [TestCase("me, 30 necro", "Necromancer", 30, false)]
    [TestCase("me, 30 necro", "Necromancer", 31, true)]
    [TestCase("im lvl 30 so cant help", "Cleric", 30, false)]
    [TestCase("im lvl 30 so cant help", "Cleric", 31, true)]
    [TestCase("im only 22 now", "Cleric", 23, true)]
    [TestCase("as a friar u want staff spec", "Armsman", 30, false)]
    [TestCase("any healer here?", "Wizard", 30, false)]
    [TestCase("im a healer", "Druid", 30, false)]
    [TestCase("im in camelot", "Cleric", 30, false)]
    [TestCase("lol same", "Cleric", 30, false)]
    public void AReplyNeverSaysSomethingUntrueOfItsBot(string reply, string cls, int level, bool untrue) =>
        Assert.That(AutonomousChatQuestions.ContradictsSelf(reply, Facts(cls, level)), Is.EqualTo(untrue));

    [TestCase("what zone r u in?", "zone")]
    [TestCase("which zone are you", "zone")]
    [TestCase("are you in camelot hills?", "zone")]
    [TestCase("r u in a guild?", "guild")]
    [TestCase("are you in a group?", "group")]
    [TestCase("are you busy?", "busy")]
    public void WhereTheBotIsIsAQuestionAboutItself(string question, string kind) =>
        Assert.That(AutonomousChatQuestions.SelfQuestion(question), Is.EqualTo(kind));

    [Test]
    public void ABotSaysWhereItIsFromItsOwnState()
    {
        var random = new Random(1);
        var bot = Facts("Cleric", 12, "Camelot Hills");
        for (int i = 0; i < 20; i++)
        {
            Assert.That(AutonomousChatQuestions.AnswerSelf("zone", "what zone r u in?", bot, random), Does.Contain("Camelot Hills"));
            Assert.That(AutonomousChatQuestions.AnswerSelf("zone", "are you in camelot hills?", bot, random), Does.StartWith("y"));
            string elsewhere = AutonomousChatQuestions.AnswerSelf("zone", "are you in black mountains south?", bot, random);
            Assert.That(elsewhere, Does.StartWith("no"));
            Assert.That(elsewhere, Does.Contain("Camelot Hills"));
        }
        Assert.That(AutonomousChatQuestions.AnswerSelf("zone", "are you at the party later", bot, random), Is.Null, "no place named: no answer");
    }

    [TestCase("gn all", "need a healer still?", true)]
    [TestCase("where is the armor trainer?", "wts 97% chain hauberk /send", true)]
    [TestCase("lol that mob", "x me", true)]
    [TestCase("inv pls", "need a healer still?", false)]
    [TestCase("anyone selling chain?", "wts 97% chain hauberk /send", false)]
    [TestCase("lfg 38 thane", "x me", false)]
    [TestCase("gn all", "gn", false)]
    public void AnAdFromAnotherSituationDoesntFit(string incoming, string reply, bool wrong) =>
        Assert.That(AutonomousChatReplyFit.WrongSituation(incoming, reply), Is.EqualTo(wrong));

    #endregion
}
