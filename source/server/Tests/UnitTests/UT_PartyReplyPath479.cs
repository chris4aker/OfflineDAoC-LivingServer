using System;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Realism 2b (#479), step 1: hired companions' /say and the player's /group take one party reply path
    /// (AutonomousChatReplyPath.Party), with the group meet-up and pause answers, and every channel types at one delay
    /// (AutonomousChatArbiter.AnswerDelayMs). Pure rules here; UT_ChatQuestionBench (PartyRows) runs the same path on the bench's
    /// stub world. Tests only: no world, no live bots.
    /// </summary>
    [TestFixture]
    public class UT_PartyReplyPath479
    {
        private static AutonomousChatReplyPath.Ports Ports(int seed = 1) => new() { Random = new Random(seed), Voices = true };

        private static AutonomousBotChatCoordinator.ReplyChoice Party(string message, bool addressed = false, Func<string> knowledge = null,
            Func<string> alarm = null, Func<string> come = null, Func<string> meetup = null, Func<string> pause = null,
            Func<AutonomousBotChatCoordinator.ReplyChoice> voiced = null) =>
            AutonomousChatReplyPath.Party(Ports(), message, AutonomousChatIntentModel.Predict(message), addressed, "group",
                knowledge, alarm, come, meetup, pause, voiced);

        // ------------------------------------------------------------------------------------------------- the order

        private static Func<string> Meetup(string line) => () => AutonomousChatMeetup.PartyReply(line, false, true, "Camelot Hills", new Random(2));

        private static Func<string> Pause(string line) => () => AutonomousChatMeetup.PauseReply(line, 100, 100, new Random(3));

        [Test]
        public void A_question_the_knowledge_lookup_answers_is_answered_from_it_first()
        {
            const string line = "where is the cleric trainer?";
            var choice = Party(line, knowledge: () => "camelot, the church", meetup: Meetup(line), pause: Pause(line),
                voiced: () => new AutonomousBotChatCoordinator.ReplyChoice("sure", "intent help"));
            Assert.That(choice.Route, Is.EqualTo("knowledge"));
            Assert.That(choice.Text, Is.EqualTo("camelot, the church"));
        }

        [Test]
        public void Short_talk_and_a_question_to_the_bot_about_itself_skip_the_knowledge_lookup()
        {
            bool asked = false;
            string Lookup()
            {
                asked = true;
                return "a general answer";
            }
            Party("ty", knowledge: Lookup, voiced: () => new AutonomousBotChatCoordinator.ReplyChoice(null, "none thanks"));
            Assert.That(asked, Is.False, "thanks is short talk, as in /faction");
            Party("what lvl are you?", addressed: true, knowledge: Lookup, voiced: () => new AutonomousBotChatCoordinator.ReplyChoice(null, "none"));
            Assert.That(asked, Is.False, "a groupmate named answers about itself from its own facts");
        }

        [Test]
        public void A_warning_comes_before_come_here()
        {
            var choice = Party("adds!!", alarm: () => "on it", come: () => "omw", meetup: Meetup("adds!!"), pause: Pause("adds!!"));
            Assert.That(choice.Route, Is.EqualTo("context alarm"));
            Assert.That(choice.Text, Is.EqualTo("on it"));
            Assert.That(Party("INC", alarm: () => null).Route, Is.EqualTo("none alarm"), "a warning nothing fits draws nothing");
        }

        [Test]
        public void Come_here_is_answered_honestly()
        {
            var choice = Party("can u come to mularn?", come: () => "omw, give me a few");
            Assert.That(choice.Route, Is.EqualTo("context come"));
        }

        [Test]
        public void Where_the_player_is_and_a_pause_have_their_own_answers_before_anything_else()
        {
            bool looked = false;
            string Lookup()
            {
                looked = true;
                return "oom means out of mana";
            }
            bool voicedAsked = false;
            AutonomousBotChatCoordinator.ReplyChoice Voiced()
            {
                voicedAsked = true;
                return new AutonomousBotChatCoordinator.ReplyChoice("lol", "intent laugh");
            }
            var meet = Party("im at the bridge", meetup: () => AutonomousChatMeetup.PartyReply("im at the bridge", false, true, "Camelot Hills", new Random(2)),
                voiced: Voiced);
            Assert.That(meet.Route, Is.EqualTo("live meetup"));
            Assert.That(meet.Text, Is.Not.Null);
            var oom = Party("oom", knowledge: Lookup, meetup: Meetup("oom"), pause: Pause("oom"), voiced: Voiced);
            Assert.That(oom.Route, Is.EqualTo("live pause"));
            Assert.That(voicedAsked, Is.False);
            Assert.That(looked, Is.False, "\"oom\" in a group is a pause, not a question about the word");
        }

        [Test]
        public void Without_a_voiced_reply_the_line_draws_nothing_and_with_one_the_budget_has_it()
        {
            Assert.That(Party("nice weather", voiced: null).Text, Is.Null);
            Assert.That(Party("nice weather", voiced: null).Route, Is.EqualTo("none count"));
            var fact = Party("what lvl is vendo?", voiced: () => new AutonomousBotChatCoordinator.ReplyChoice("mostly 20-28", "fact world"));
            Assert.That(fact.Text, Is.EqualTo("mostly 20-28"));
            Assert.That(fact.Route, Is.EqualTo("fact world"));
            Assert.That(Party("buy cheap gold at www dot goldsite dot com", voiced: () => new AutonomousBotChatCoordinator.ReplyChoice("ok", "intent trading")).Text,
                Is.Null, "the budget still holds spam");
        }

        [Test]
        public void The_steps_text_is_styled_once_by_the_ports()
        {
            var ports = new AutonomousChatReplyPath.Ports { Random = new Random(1), Voices = true, Style = text => text + " :)" };
            var choice = AutonomousChatReplyPath.Party(ports, "oom", AutonomousChatIntentModel.Predict("oom"), false, "group",
                null, null, null, null, () => "np take ur time", null);
            Assert.That(choice.Text, Is.EqualTo("np take ur time :)"));
        }

        // ------------------------------------------------------------------------------------------------- the meet-up

        [Test]
        public void The_party_meetup_beside_in_the_region_and_far_away()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                Assert.That(AutonomousChatMeetup.PartyReply("im by camp station", true, true, "Camelot Hills", new Random(seed)), Is.AnyOf("k", "kk", "ok"));
                string coming = AutonomousChatMeetup.PartyReply("im at the bridge", false, true, "Camelot Hills", new Random(seed));
                Assert.That(coming, Does.Match(@"\b(coming|omw|heading)\b"), "it follows its group in the region");
                string far = AutonomousChatMeetup.PartyReply("im at the bridge", false, false, "Lyonesse", new Random(seed));
                Assert.That(far, Does.Not.Match(@"\b(coming|omw|heading)\b"), "it can't follow into another region: no promise");
                Assert.That(far, Does.Contain("lyonesse"), "where it is, as the tell meet-up says it");
                Assert.That(AutonomousChatMeetup.PartyReply("omw", false, false, "Lyonesse", new Random(seed)), Does.Match(@"\b(k|cool)\b"));
                Assert.That(AutonomousChatMeetup.PartyReply("be there in 5", false, true, "Camelot Hills", new Random(seed)), Is.Not.Null);
            }
            Assert.That(AutonomousChatMeetup.PartyReply("where is the bridge?", false, true, "Camelot Hills", new Random(1)), Is.Null);
            Assert.That(AutonomousChatMeetup.PartyReply("im in combat", false, true, "Camelot Hills", new Random(1)), Is.Null);
            Assert.That(AutonomousChatMeetup.PartyReply("nice pull", false, true, "Camelot Hills", new Random(1)), Is.Null);
        }

        [Test]
        public void Be_there_in_a_time_is_coming()
        {
            Assert.That(AutonomousChatMeetup.ComesSoon("be there in 5"), Is.True);
            Assert.That(AutonomousChatMeetup.ComesSoon("ill be there in a min"), Is.True);
            Assert.That(AutonomousChatMeetup.ComesSoon("be there in 5?"), Is.False);
            Assert.That(AutonomousChatMeetup.ComesSoon("be back in 5"), Is.False, "afk, not coming to the bot");
        }

        // ------------------------------------------------------------------------------------------------- a pause, ready

        [TestCase("oom", true)]
        [TestCase("oom, med", true)]
        [TestCase("need to rest", true)]
        [TestCase("med break", true)]
        [TestCase("need pow", true)]
        [TestCase("im medding", true)]
        [TestCase("low on power", true)]
        [TestCase("oom?", false)]
        [TestCase("where do i med?", false)]
        [TestCase("medieval stuff is cool", false)]
        [TestCase("i need to rest my eyes after this game", false)]
        public void A_pause_is_read(string line, bool pause) =>
            Assert.That(AutonomousChatMeetup.SaysPause(line), Is.EqualTo(pause), line);

        [TestCase("rdy", true)]
        [TestCase("ready", true)]
        [TestCase("im ready", true)]
        [TestCase("r", true)]
        [TestCase("ready?", false)]
        [TestCase("ready to go to vendo tonight?", false)]
        [TestCase("already did that", false)]
        public void Ready_is_read(string line, bool ready) =>
            Assert.That(AutonomousChatMeetup.SaysReady(line), Is.EqualTo(ready), line);

        [Test]
        public void The_pause_answer_is_true_to_the_bot()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                Assert.That(AutonomousChatMeetup.PauseReply("oom", 100, 100, new Random(seed)), Does.Not.Contain("med").And.Not.Contain("pow"),
                    "a bot at full power doesn't say it meds too");
                Assert.That(AutonomousChatMeetup.PauseReply("oom", 100, null, new Random(seed)), Does.Not.Contain("med").And.Not.Contain("pow"),
                    "a bot with no power doesn't med");
                Assert.That(AutonomousChatMeetup.PauseReply("need to rest", 100, 30, new Random(seed)), Does.Match(@"\b(med|pow)\b"));
                Assert.That(AutonomousChatMeetup.PauseReply("rdy", 100, 100, new Random(seed)), Is.AnyOf("k go", "rdy", "k"));
                Assert.That(AutonomousChatMeetup.PauseReply("rdy", 100, 20, new Random(seed)), Does.StartWith("sec"));
                Assert.That(AutonomousChatMeetup.PauseReply("rdy", 30, null, new Random(seed)), Does.StartWith("sec"));
            }
            Assert.That(AutonomousChatMeetup.PauseReply("where is camelot", 100, 100, new Random(1)), Is.Null);
            Assert.That(AutonomousChatMeetup.PauseReply("ready?", 100, 100, new Random(1)), Is.Null, "\"ready?\" is a question the situation answers");
        }

        // ------------------------------------------------------------------------------------------------- one typing delay

        [Test]
        public void One_delay_reads_thinks_and_types_at_the_bots_speed()
        {
            const string answer = "camelot, the church by the west gate, ask for the cleric trainer there";
            Assert.That(AutonomousChatArbiter.AnswerDelayMs("where is the cleric trainer?", answer, 40, "intent help", false, false, 0, new Random(5)),
                Is.EqualTo(AutonomousChatArbiter.DelayMs("where is the cleric trainer?", answer, 40, 0, new Random(5))), "uncapped, not quick: DelayMs as it was");
            int slow = AutonomousChatArbiter.AnswerDelayMs("where?", answer, 15, "intent help", false, false, 0, new Random(5));
            Assert.That(slow, Is.GreaterThan(AutonomousChatArbiter.TypingCapMs));
            Assert.That(AutonomousChatArbiter.AnswerDelayMs("where?", answer, 15, "intent help", false, true, 0, new Random(5)),
                Is.EqualTo(AutonomousChatArbiter.TypingCapMs), "said to the bot or in its group: within 12 s");
            Assert.That(AutonomousChatArbiter.AnswerDelayMs("where?", answer, 15, "intent help", false, true, 9_000, new Random(5)),
                Is.LessThanOrEqualTo(9_000 + AutonomousChatArbiter.TypingCapMs), "the cap counts from the reply before");
            Assert.That(AutonomousBotChatCoordinator.DirectTypingCapMs, Is.EqualTo(AutonomousChatArbiter.TypingCapMs));
        }

        [Test]
        public void A_short_known_fact_types_faster_only_as_an_answer_to_a_question()
        {
            const string fact = "mostly 20-28 there";
            int quick = AutonomousChatArbiter.AnswerDelayMs("what lvl is vendo?", fact, 30, "knowledge", true, false, 0, new Random(9));
            Assert.That(quick, Is.EqualTo(AutonomousChatArbiter.DelayMs("what lvl is vendo?", fact, AutonomousChatArbiter.QuickWpm(30), 0, new Random(9))));
            Assert.That(AutonomousChatArbiter.AnswerDelayMs("what lvl is vendo?", fact, 30, "knowledge", false, false, 0, new Random(9)),
                Is.EqualTo(AutonomousChatArbiter.DelayMs("what lvl is vendo?", fact, 30, 0, new Random(9))));
        }

        // ------------------------------------------------------------------------------------------------- /group's own rules

        [Test]
        public void The_group_cooldown_never_drops_a_question_or_a_named_line()
        {
            Assert.That(AutonomousBotGroupChatter.CooledDown(10_000, 12_000, needsAnswer: false), Is.True, "a remark a moment after an answer");
            Assert.That(AutonomousBotGroupChatter.CooledDown(10_000, 12_000, needsAnswer: true), Is.False, "a question is always heard");
            Assert.That(AutonomousBotGroupChatter.CooledDown(10_000, 10_000 + AutonomousBotGroupChatter.PlayerReplyCooldownMs, false), Is.False);
            Assert.That(AutonomousBotGroupChatter.CooledDown(0, 1_000, false), Is.False, "nobody answered yet");
        }

        [Test]
        public void Only_the_voiced_reply_may_draw_a_second_groupmate()
        {
            foreach (string route in new[] { "knowledge", "knowledge spec", "context alarm", "context come", "live meetup", "live pause" })
                Assert.That(AutonomousBotGroupChatter.IsVoicedRoute(route), Is.False, route);
            foreach (string route in new[] { "intent greeting", "shape ask_howareyou", "short (intent ding)", "fact world" })
                Assert.That(AutonomousBotGroupChatter.IsVoicedRoute(route), Is.True, route);
        }

        // ------------------------------------------------------------------------------------------------- a companion's own line

        [Test]
        public void A_companion_keeps_its_own_line_only_for_a_statement_it_can_make_true()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                Assert.That(CompanionChat165.StatementLine("lets go", eAutonomousChatIntent.Travel, "Wynn", new Random(seed)),
                    Is.AnyOf("lead on, im behind u", "wherever u go"));
                Assert.That(CompanionChat165.StatementLine("hand me that", eAutonomousChatIntent.Trading, "Wynn", new Random(seed)), Does.Contain("wear"));
            }
            Assert.That(CompanionChat165.StatementLine("where should i level at 3?", eAutonomousChatIntent.Grinding, "Wynn", new Random(1)), Is.Null,
                "a question gets the reply path's answer or nothing, never \"point me at the next one\"");
            foreach (eAutonomousChatIntent intent in new[] { eAutonomousChatIntent.Unknown, eAutonomousChatIntent.Help, eAutonomousChatIntent.Banter,
                         eAutonomousChatIntent.Grouping, eAutonomousChatIntent.IgnoreOutOfWorld })
                for (int seed = 0; seed < 10; seed++)
                    Assert.That(CompanionChat165.StatementLine("add!", intent, "Wynn", new Random(seed)), Is.Null, intent.ToString());
        }

        [Test]
        public void The_formal_companion_and_its_commands_are_unchanged()
        {
            // Reply is Command ?? ReplyTo, as before #479 (the Formal companion's tells and the tests of its lines use it).
            for (int seed = 0; seed < 10; seed++)
                Assert.That(CompanionChat165.ReplyTo(eAutonomousChatIntent.Unknown, "Wynn", new Random(seed), true), Is.AnyOf("Aye.", "Understood.", "As you say, Wynn."));
        }
    }
}
