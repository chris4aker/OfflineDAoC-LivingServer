using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Realism 1b (#477; realism audit 1.2.3, docs/realism-audit-1.2.3/channels.md): the bench in every channel and with the line
    /// before it. Brought in from the audit's branch audit/1.2.3-channels (PR #470) and scored now:
    ///  * every /faction question of the bench, the same words and seed, in each channel's path (bench-channels.json);
    ///  * the 54 follow-ups that lean on the player's line before (a place, an item, a class, a person), in /faction, /group and a
    ///    tell (bench-followups.json);
    ///  * 40 lines of the moment (meeting up, pulling, resting) in eight channels (bench-moments.json);
    ///  * the two-line bench: a bot's line, then the player's follow-up (bench-twoline.json).
    /// /faction, /say, /group and a tell are the bench's own paths (AnswerOne); /group named and a hired companion's /say take the
    /// party reply path the live handlers run (AutonomousChatReplyPath.Party, #479). The other handlers need live bots, so each is read
    /// off its code onto the stub world, as channels.md says: /gu is AutonomousBotGuildSocial.OnPlayerGuildChat, /advice
    /// OnPlayerAdvice, /b and /cg OnPlayerOpenChannel, /trade OnPlayerTradePost's
    /// gate. A mirror can drift from its handler: when a handler moves onto AutonomousChatReplyPath, its case here should call that.
    /// The expectations are in context_lines.json and player_questions.json; score_bench.py scores the rows and the ratchet holds
    /// each channel. Tests only: no world, no live bots.
    /// </summary>
    public partial class UT_ChatQuestionBench
    {
        /// <summary>The channels of the per-channel bench. "faction", "say", "group" and "tell" are the bench's own paths.</summary>
        internal static readonly string[] Channels =
            ["faction", "say", "group", "group_named", "tell", "guild", "guild_named", "advice", "broadcast", "chatgroup", "companion_say", "trade"];

        /// <summary>The channels the lines of the moment are typed in.</summary>
        internal static readonly string[] MomentChannels = ["faction", "say", "group", "group_named", "tell", "guild", "guild_named", "companion_say"];

        /// <summary>The channels a follow-up is asked in.</summary>
        internal static readonly string[] FollowUpChannels = ["faction", "group", "tell"];

        private static readonly DateTime Epoch = new(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);

        /// <summary>One line as the channel's own handler answers it (see the class summary).</summary>
        private static Answer AnswerAs(Run run, string channel)
        {
            Question q = run.Q;
            if (channel is "faction" or "say" or "group" or "tell")
            {
                run.Q = q with { Channel = channel, To = null };
                return AnswerOne(run);
            }
            string message = q.Text.Trim();
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            string shape = AutonomousChatShortTalk.QuestionShape(message);
            using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, channel + ":" + run.Realm,
                (long)(run.Now - Epoch).TotalMilliseconds);
            AutonomousChatReplyPath.Ports ports = PortsOf(run);
            AutonomousBotChatCoordinator.ReplyChoice choice;
            switch (channel)
            {
                case "group_named":
                    // OnPlayerGroupChat with the groupmate named (#479: the party reply path, addressed).
                    choice = PartyOf(run, ports, message, true, "group");
                    break;
                case "guild":
                case "guild_named":
                {
                    bool named = channel == "guild_named";
                    if (shape == "ack")
                        return ChannelDone(run, "none ack", null);
                    if (shape == "ask_inplace")
                        return ChannelDone(run, "none not_there (no guildmate is there)", null);
                    if (!named && AutonomousChatScreen.OtherAddressee(message, null) != null)
                        return ChannelDone(run, "none other", null);
                    string known = Knowledge(run, message);
                    if (known != null)
                    {
                        choice = new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge");
                        break;
                    }
                    AutonomousBotGuildChatLines.ReplyKind kind = AutonomousBotGuildChatLines.Classify(message, intent);
                    int count = named && kind != AutonomousBotGuildChatLines.ReplyKind.None ? 1
                        : AutonomousBotGuildSocial.ResponderCount(kind, 5, run.Rng.NextDouble());
                    if (count == 0)
                        return ChannelDone(run, "none guild kind " + kind, null);
                    choice = AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, true, "guild"), message, named, "guild");
                    break;
                }
                case "advice":
                {
                    string known = Knowledge(run, message);
                    choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                        : VoicedOf(run, ports, message, false, "advice");
                    if (string.IsNullOrWhiteSpace(choice.Text) || AutonomousChatReplyFit.Of(choice.Route, message) < AutonomousChatReplyFit.Specific ||
                        AutonomousChatReplyBudget.IsBrushOff(choice.Text))
                        return ChannelDone(run, "silent advice (" + choice.Route + ")", null);
                    break;
                }
                case "broadcast":
                case "chatgroup":
                {
                    if (!AutonomousBotChatCoordinator.NeedsAnswer(message))
                        return ChannelDone(run, "none open (asks nothing)", null);
                    if (AutonomousChatScreen.OtherAddressee(message, null) != null)
                        return ChannelDone(run, "none other", null);
                    if (AutonomousBotChatCoordinator.AsksToJoinGroup(message) || AutonomousBotChatCoordinator.BroadcastsLfg(message))
                    {
                        run.Stub = true;
                        choice = new AutonomousBotChatCoordinator.ReplyChoice(
                            Style(run, AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? "[world: join reply]"), "join");
                        break;
                    }
                    string known = Knowledge(run, message);
                    choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                        : AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, false, channel), message, false, channel);
                    break;
                }
                case "companion_say":
                    // A hired companion named in /say (or holding the floor), CompanionChat165.Answer after its commands (#479): the party
                    // reply path its groupmates take, addressed, in /say; for a plain statement nothing there answers, its own line where
                    // it says something true of a companion.
                    // CompanionChat165.Answer's own order (AnswerWith), with no command and a non-formal companion.
                    choice = CompanionChat165.AnswerWith(message, "Wynn", false, () => null, _ => PartyOf(run, ports, message, true, "say"),
                        text => Style(run, text), run.Rng);
                    break;
                case "trade":
                {
                    AutonomousChatMarket.Post post = AutonomousChatMarket.PostOf(message);
                    if (post is not (AutonomousChatMarket.Post.Wts or AutonomousChatMarket.Post.Wtb))
                        return ChannelDone(run, "none trade (not a wts or wtb post)", null);
                    run.Stub = true;
                    choice = new AutonomousBotChatCoordinator.ReplyChoice("[world: a market reply at its share]", "market " + post);
                    break;
                }
                default:
                    return ChannelDone(run, "none channel", null);
            }
            return ChannelDone(run, choice.Route, choice.Text);
        }

        private static Answer ChannelDone(Run run, string route, string text)
        {
            string outcome = text == null ? "silent"
                : route.StartsWith("rescue (", StringComparison.Ordinal) ? "rescue"
                : route.StartsWith("short (", StringComparison.Ordinal) ? "short"
                : route.StartsWith("ack (", StringComparison.Ordinal) ? "ack"
                : "answer";
            return new Answer(run.Q.Id, route, text, run.Stub || text != null && text.StartsWith("[world", StringComparison.Ordinal),
                AutonomousChatReplyFit.Of(route, run.Q.Text), outcome, text == null ? 0 : 1, 0, false);
        }

        private static Run RunOf(Bench bench, Question q, VC.RecentLines recent, DateTime now)
        {
            Responder me = bench.Responders[q.Realm];
            int seed = q.Text.Aggregate(17, (h, c) => unchecked(h * 31 + c));
            return new Run
            {
                Q = q, Me = me, Facts = FactsOf(me, q.Realm), Realm = q.Realm, Rng = new Random(seed), Corpus = AutonomousBotVoices.Corpus, Recent = recent,
                Now = now, World = bench.World, Towns = bench.Towns,
            };
        }

        private static string OutPath(string variable, string file) =>
            Environment.GetEnvironmentVariable(variable) is { Length: > 0 } output
                ? output
                : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-bench", file);

        private static void WriteRows(string variable, string file, IEnumerable<object> rows)
        {
            string path = OutPath(variable, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.Progress.WriteLine($"CHAT_BENCH {file} written to {path}");
        }

        private static object Row(string channel, string id, string text, Answer a) =>
            new { ch = channel, id, text, route = a.Route, reply = a.Reply, stub = a.Stub, outcome = a.Outcome };

        /// <summary>Every /faction question of the bench (the code's and the fresh lines), in every channel (bench-channels.json).</summary>
        internal static List<object> ChannelRows(Bench bench)
        {
            Question[] questions = bench.Questions.Where(q => q.Channel == "faction" && string.IsNullOrEmpty(q.To)).ToArray();
            var rows = new List<object>();
            foreach (string channel in Channels)
            {
                var recent = new VC.RecentLines();
                DateTime now = Epoch;
                foreach (Question q in questions)
                {
                    Run run = RunOf(bench, q, recent, now);
                    now = now.AddMinutes(3);
                    rows.Add(Row(channel, q.Id, q.Text, AnswerAs(run, channel)));
                }
            }
            return rows;
        }

        [Test]
        public void AnswersTheBenchInEveryChannel()
        {
            Bench bench = Load();
            List<object> rows = ChannelRows(bench);
            int faction = bench.Questions.Count(q => q.Channel == "faction" && string.IsNullOrEmpty(q.To));
            Assert.That(rows, Has.Count.EqualTo(faction * Channels.Length));
            WriteRows("CHAT_BENCH_CHANNELS_OUT", "bench-channels.json", rows);
        }

        // ------------------------------------------------------------------------------------------- the line before

        private static JsonDocument Context() => JsonDocument.Parse(Resource("context_lines.json"));

        /// <summary>
        /// The 54 follow-ups (bench-followups.json): the player names a place, an item, a class or a person, then asks about it
        /// ("how do i get there", "what lvl is he?"). The conversation memory completes the line as live (ChatConversations.Resolve
        /// on the player's last line), and the completed line takes the channel's path.
        /// </summary>
        [Test]
        public void AnswersTheFollowUpsThatLeanOnTheLineBefore()
        {
            Bench bench = Load();
            using JsonDocument doc = Context();
            var rows = new List<object>();
            var recent = new VC.RecentLines();
            DateTime now = Epoch;
            foreach (JsonElement f in doc.RootElement.GetProperty("followups").EnumerateArray())
            {
                string id = Str(f, "id"), setup = Str(f, "setup"), said = Str(f, "said");
                foreach (string channel in FollowUpChannels)
                {
                    string key = channel == "tell" ? AutonomousChatThreads.Tell("Wynn", bench.Responders[eRealm.Albion].Name)
                        : channel == "group" ? "group:bench" : AutonomousChatThreads.Faction(eRealm.Albion);
                    var conversations = new ChatConversations();
                    conversations.NotePlayer("Wynn", key, setup, 1_000);
                    ChatConversations.Conversation held = conversations.In("Wynn", key, 2_000);
                    string resolved = ChatConversations.Resolve(said, held);
                    var q = new Question(id, resolved, channel, eRealm.Albion, "Armsman", 24, "followup", null);
                    Run run = RunOf(bench, q, recent, now);
                    now = now.AddMinutes(3);
                    Answer a = AnswerAs(run, channel);
                    rows.Add(new
                    {
                        ch = channel, id, text = said, setup, resolved = resolved == said ? null : resolved, route = a.Route, reply = a.Reply, stub = a.Stub,
                        outcome = a.Outcome,
                    });
                }
            }
            Assert.That(rows, Has.Count.EqualTo(54 * FollowUpChannels.Length), "the audit's 54 follow-ups in three channels");
            WriteRows("CHAT_BENCH_FOLLOWUPS_OUT", "bench-followups.json", rows);
        }

        /// <summary>
        /// The 40 lines of the moment (bench-moments.json) in the channels they are typed in. A tell is taken as inside an exchange
        /// (the bot told the player something a moment ago), so the meet-up answer is in reach; /group and a companion's /say take the
        /// party reply path the live handlers run (AutonomousChatReplyPath.Party, #479).
        /// </summary>
        [Test]
        public void AnswersTheLinesOfTheMomentInEveryChannel()
        {
            Bench bench = Load();
            using JsonDocument doc = Context();
            var rows = new List<object>();
            JsonElement[] moments = doc.RootElement.GetProperty("moments").EnumerateArray().ToArray();
            foreach (string channel in MomentChannels)
            {
                var recent = new VC.RecentLines();
                DateTime now = Epoch;
                foreach (JsonElement m in moments)
                {
                    string id = Str(m, "id"), said = Str(m, "said");
                    var q = new Question(id, said, channel, eRealm.Albion, "Armsman", 24, "moment", null);
                    Run run = RunOf(bench, q, recent, now);
                    now = now.AddMinutes(3);
                    eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(said);
                    Answer a;
                    // /group's own steps (a warning, "come here", the meet-up, a pause) are AutonomousChatReplyPath.Party's (#479), in AnswerAs.
                    if (channel == "tell")
                    {
                        AutonomousChatReplyPath.Ports ports = PortsOf(run);
                        using AutonomousChatFiller.ReplyScope scope = AutonomousChatFiller.Replying(said, "tell:" + run.Realm, (long)(run.Now - Epoch).TotalMilliseconds);
                        AutonomousBotChatCoordinator.ReplyChoice choice = AutonomousChatReplyPath.Tell(ports, said, intent,
                            invited: () => Stubbed(run, "[world: got the invite or not]"),
                            join: () => Stubbed(run, "[world: join reply]"),
                            where: () => AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng),
                            friend: null,
                            knowledge: () => Knowledge(run, said),
                            voiced: () => VoicedOf(run, ports, said, true, "tell"),
                            meetup: () => AutonomousChatMeetup.Reply(said, run.Me.Zone, false, false, run.Rng));
                        choice = AutonomousChatReplyPath.Budget(ports, choice, said, true, "tell");
                        a = ChannelDone(run, choice.Route, choice.Text);
                    }
                    else
                        a = AnswerAs(run, channel);
                    rows.Add(Row(channel, id, said, a));
                }
            }
            Assert.That(rows, Has.Count.EqualTo(40 * MomentChannels.Length), "the audit's 40 lines of the moment in eight channels");
            WriteRows("CHAT_BENCH_MOMENTS_OUT", "bench-moments.json", rows);
        }

        // ------------------------------------------------------------------------------------------- the two-line bench

        /// <summary>
        /// The two-line bench (bench-twoline.json): a bot's line, then the player's follow-up. The bot's line goes into a
        /// ChatThreadLog (and, when said to the player, the conversation memory, as Note does live); the player's line then takes
        /// TryThreadReply's pure steps in its order (the follow-up word on the line it is about, the thanks to the bot that
        /// answered, a laugh at its joke, an answer to its open question) and, when none takes it, the channel's own path on the
        /// line ChatConversations.Resolve completes. The steps that draw at random live (0.7, 0.35, 0.85) always try here.
        /// </summary>
        [Test]
        public void AnswersTheTwoLineBench()
        {
            Bench bench = Load();
            using JsonDocument doc = Context();
            var rows = new List<object>();
            var recent = new VC.RecentLines();
            DateTime now = Epoch;
            foreach (JsonElement t in doc.RootElement.GetProperty("twoline").EnumerateArray())
            {
                string id = Str(t, "id"), channel = Str(t, "channel"), bot = Str(t, "bot"), invites = Str(t, "invites"), said = Str(t, "said");
                bool toPlayer = t.GetProperty("to_player").GetBoolean();
                var q = new Question(id, said, channel, eRealm.Albion, "Armsman", 24, "twoline", null);
                Run run = RunOf(bench, q, recent, now);
                now = now.AddMinutes(3);
                Answer a = AnswerAfter(run, channel, bot, invites, toPlayer, out string resolved);
                rows.Add(new { ch = channel, id, text = said, bot, resolved, route = a.Route, reply = a.Reply, stub = a.Stub, outcome = a.Outcome });
            }
            Assert.That(rows, Is.Not.Empty);
            WriteRows("CHAT_BENCH_TWOLINE_OUT", "bench-twoline.json", rows);
        }

        private static Answer AnswerAfter(Run run, string channel, string botLine, string invites, bool toPlayer, out string resolved)
        {
            const string player = "Wynn";
            string key = channel == "tell" ? AutonomousChatThreads.Tell(player, run.Me.Name)
                : channel == "group" ? "group:bench" : AutonomousChatThreads.Faction(run.Realm);
            long said = 60_000, heard = said + 8_000;
            var log = new ChatThreadLog();
            log.Record(key, run.Me.Id, run.Me.Name, botLine, invites, said, toPlayer ? player : null);
            var conversations = new ChatConversations();
            if (toPlayer || channel == "tell")
                conversations.NoteBot(player, run.Me.Id, run.Me.Name, key, botLine, said);
            string message = run.Q.Text.Trim();
            resolved = null;
            using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, key, heard);
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            string word = AutonomousChatThreads.FollowUpWord(message);
            ChatThreadLog.Said about = word == null ? null : log.FollowUpTarget(key, player, message, heard, AutonomousChatThreads.FollowUpWindowMs);
            if (word == "y")
                word = about is { Asked: true } ? null : "why";
            if (word == "confirm" && !(about is { } checkedLine && AutonomousChatThreads.ConfirmFits(message, checkedLine.Text)))
                word = null;
            if (word != null && about is { Closed: false })
            {
                string reply = FollowUp(run, word, about);
                if (reply != null)
                    return ChannelDone(run, "thread followup_" + word, reply);
            }
            if (intent == eAutonomousChatIntent.Thanks && !message.Contains('?') &&
                log.LastTo(key, player, heard, AutonomousChatThreads.ThanksWindowMs) is { } helped)
            {
                if (AutonomousChatThreads.EndsAnExchange(helped.Text))
                    return ChannelDone(run, "thread consumed (thanks for a line that ended the exchange)", null);
                return ChannelDone(run, "thread thanks",
                    Pick(run, "reply", AutonomousBotVoices.IntentName(eAutonomousChatIntent.Thanks), null, null, true, null)?.Text);
            }
            if (intent == eAutonomousChatIntent.Laugh && log.Last(key, heard, AutonomousChatThreads.LaughWindowMs) is { } funny &&
                funny.Family is "joke" or "boast" or "complaint" or "chat")
                return ChannelDone(run, "thread laugh", Pick(run, "reply", AutonomousBotVoices.IntentName(eAutonomousChatIntent.Laugh), null, null, true,
                    line => AutonomousChatThreads.FitsOwnJoke(line.Text))?.Text);
            if (log.OpenQuestion(key, player, message, heard, AutonomousChatThreads.AnswerWindowMs) is { } question &&
                AutonomousChatThreads.ReadsAsAnswer(message, intent))
            {
                log.Close(question);
                if (AutonomousChatShortTalk.QuestionShape(message) == "player_wait" &&
                    Pick(run, "reply", null, "player_wait", null, true, null) is { } wait)
                    return ChannelDone(run, "thread wait", wait.Text);
                foreach (string answers in AutonomousChatThreads.ClosingSets(question, message))
                    if (Pick(run, "closing", null, answers, null, true, null) is { } closing)
                        return ChannelDone(run, "thread answer", closing.Text);
                return ChannelDone(run, "thread consumed (an answer; no closing fits)", null);
            }
            // No thread step took it: the channel's own path, on the line the conversation memory completes.
            ChatConversations.Conversation held = conversations.In(player, key, heard);
            string completed = ChatConversations.Resolve(message, held);
            if (completed != message)
                resolved = completed;
            run.Q = run.Q with { Text = completed };
            return AnswerAs(run, channel);
        }

        /// <summary>AutonomousBotChatCoordinator.FollowUpReply on the stub world: "where" reads the bot's line, the other words draw
        /// their pool (the line's family first).</summary>
        private static string FollowUp(Run run, string word, ChatThreadLog.Said last)
        {
            if (word == "where")
                return Style(run, Town(run, "where is " + last.Text) ??
                                  AutonomousChatThreads.WhereIsIt(last.Text, AutonomousBotVoices.CapitalOf(run.Realm), run.Rng) ??
                                  AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng));
            string family = last.Family;
            // #478: as live, the say gate's non-answer class is left out of the follow-up pools.
            if (!string.IsNullOrEmpty(family) && Pick(run, "reply", null, "followup_" + word, null, true,
                    line => line.Topic == family && !BotSayGate.IsNonAnswer(line.Text)) is { } fit)
                return fit.Text;
            return Pick(run, "reply", null, "followup_" + word, null, true, line => line.Topic is "general" or "" && !BotSayGate.IsNonAnswer(line.Text))?.Text;
        }
    }
}
