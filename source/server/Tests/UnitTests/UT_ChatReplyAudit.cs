using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// An audit tool, not a regular test: runs a file of realistic player lines (TALK_AUDIT_IN, a JSON array of
    /// {text, channel, context, expect, note}) through the pure parts of the reply path (thread rules, intent, question shape,
    /// join and where checks, and the corpus reply pick with the server's filters) and writes what each would draw to
    /// TALK_AUDIT_OUT. The world parts (knowledge lookups in the database, live answers, who is eligible) are marked, not run.
    /// It mirrors AutonomousBotChatCoordinator.VoicedReply; keep the two in step.
    /// </summary>
    [TestFixture, Explicit("audit tool: set TALK_AUDIT_IN and TALK_AUDIT_OUT")]
    public class UT_ChatReplyAudit
    {
        private static VC Load()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        private sealed record Item(string text, string channel, string context, string expect, string note, string to = null);

        private static VC Corpus;
        private static VC.RecentLines Recent;
        private static Random Rng;
        private static DateTime Now;

        private static VC.Speaker Speaker(int i) => new(500 + i, "Brannoc", eRealm.Midgard, "Thane", 30, (BotPersonalityType)(i % 6),
            new HashSet<string>(["solo", "outdoors", "evening", "at:myrkwood"], StringComparer.Ordinal));

        private static string Pick(int i, string intent = null, string answers = null, Func<VC.Line, bool> filter = null)
        {
            Now = Now.AddMinutes(1);
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player"] = "Wynn", ["me"] = "Brannoc", ["class"] = "thane", ["level"] = "30", ["realm"] = "Midgard", ["zone"] = "Myrkwood Forest",
            };
            return Corpus.TryPick(new VC.Request("reply", Speaker(i), slots, Recent, Now, Rng)
            {
                Intent = intent, Answers = answers, Filter = filter, RelaxTo = TimeSpan.FromMinutes(40), LocalHour = 20,
            }, out VC.Picked picked) ? picked.Text : null;
        }

        /// <summary>What VoicedReply would draw for the line, as (path, reply).</summary>
        private static readonly AutonomousChatQuestions.BotFacts Me = new(501, 30, "Thane", eRealm.Midgard, 13, BotPersonalityType.Leveler, false, 2, null);

        private static AutonomousChatQuestions.WorldFacts World => new(eRealm.Hibernia,
            [("Hibernia's Relic of Lugh", eRealm.Hibernia, eRealm.Hibernia, true), ("Midgard's Relic of Thor", eRealm.Albion, eRealm.Midgard, true),
             ("Midgard's Relic of Odin", eRealm.Midgard, eRealm.Midgard, true)],
            [("Bledmeer Faste", eRealm.Midgard), ("Caer Benowyc", eRealm.Albion), ("Dun Crauchon", eRealm.Hibernia)], AutonomousChatPlaces.All);

        private static (string Path, string Reply) Voiced(string incoming, int i, bool addressed = false)
        {
            eAutonomousChatIntent value = AutonomousChatIntentModel.Predict(incoming);
            string intent = AutonomousBotVoices.IntentName(value);
            if (value is not (eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse))
            {
                string self = addressed ? AutonomousChatQuestions.SelfQuestion(incoming) : null;
                string fact = self != null ? AutonomousChatQuestions.AnswerSelf(self, incoming, Me, Rng)
                    : AutonomousChatQuestions.AnswerWorld(incoming, eRealm.Midgard, World, Rng);
                if (fact != null)
                    return ("fact " + (self ?? "world"), fact);
            }
            string shape = AutonomousChatShortTalk.QuestionShape(incoming);
            if (shape == "ask_howareyou")
                return ("shape howareyou", Pick(i, answers: "ask_howareyou") ?? Pick(i, intent: "greeting"));
            if (shape is "ask_whereyou" or "ask_inplace" or "ask_hunt" or "ask_enemy")
                return ($"live {shape}", "[from the world]");
            if (shape is "ack")
                return ("ack", null);
            if (shape == "ask_help")
                return ("shape help", Pick(i, intent: "help", filter: l => l.Text.TrimEnd().EndsWith('?') || l.Text.Contains("whats up") || l.Text.Contains("what do")));
            if (shape != null)
            {
                string answers = shape == "ask_price" ? "trade" : shape;
                string shaped = Pick(i, answers: answers, filter: l => AutonomousBotVoices.ShapedFits(shape, incoming, l.Text));
                if (shaped != null)
                    return ($"shape {shape}", shaped);
            }
            if (AutonomousChatShortTalk.IsFollowUp(incoming))
                return ("followup(no thread)", null);
            if (AutonomousChatQuestions.IsFactual(incoming) && value is not (eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Thanks or
                    eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Grouping or eAutonomousChatIntent.BotCheck or
                    eAutonomousChatIntent.Abuse or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Ding or eAutonomousChatIntent.Congrats))
                return ("factual -> not sure (half the time)", Pick(i, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard")));
            if (value == eAutonomousChatIntent.Unknown)
                return incoming.Contains('?') ? ("unknown?", Pick(i, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard"))) : ("unknown", null);
            if (value == eAutonomousChatIntent.ClassTalk)
            {
                string[] asked = AutonomousChatShortTalk.ClassWordsIn(incoming);
                string about = asked.Length == 0 ? null : Pick(i, intent: intent, filter: l => AutonomousChatShortTalk.ClassWordsIn(l.Text)
                    .Any(w => asked.Any(a => AutonomousChatShortTalk.SameClassWord(w, a))));
                return ("classtalk", about ?? Pick(i, intent: intent, filter: l => !AutonomousChatShortTalk.NamesAClass(l.Text)));
            }
            string reply = value == eAutonomousChatIntent.Greeting
                ? Pick(i, intent: "greeting", filter: l => AutonomousChatShortTalk.AsksHowAreYou(incoming) || !l.Answers.Contains("ask_howareyou"))
                : Pick(i, intent: intent, filter: l => AutonomousChatShortTalk.ReplyFits(incoming, l.Text));
            if (reply == null && incoming.Contains('?'))
                return ($"intent {intent} -> not sure", Pick(i, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard")));
            return ($"intent {intent}", reply);
        }

        private static string Closing(int n, IEnumerable<string> sets)
        {
            foreach (string answers in sets)
            {
                Now = Now.AddMinutes(1);
                if (Corpus.TryPick(new VC.Request("closing", Speaker(n), new Dictionary<string, string> { ["player"] = "Wynn" }, Recent, Now, Rng)
                    { Answers = answers, RelaxTo = TimeSpan.FromMinutes(40) }, out VC.Picked closing))
                    return closing.Text;
            }
            return null;
        }

        /// <summary>The read rate of any labelled line file (TALK_AUDIT_IN), by the same route and labels as UT_TalkingAudit. Used to
        /// measure a holdout set that the fixes were not made against; misses go to TALK_AUDIT_OUT.</summary>
        [Test]
        public void MeasureReadRate()
        {
            string input = Environment.GetEnvironmentVariable("TALK_AUDIT_IN");
            string output = Environment.GetEnvironmentVariable("TALK_AUDIT_OUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                Assert.Ignore("set TALK_AUDIT_IN and TALK_AUDIT_OUT");
            ChatAuditRoute.Item[] items = JsonSerializer.Deserialize<ChatAuditRoute.Item[]>(File.ReadAllText(input));
            var sb = new StringBuilder();
            int hits = 0;
            foreach (ChatAuditRoute.Item item in items)
            {
                (string path, _) = ChatAuditRoute.Route(item, new Random(1));
                if (ChatAuditRoute.Fits(item.expect, path))
                    hits++;
                else
                    sb.AppendLine($"[{item.channel}] {item.text} | ctx {item.context} | expect {item.expect} | got {path}");
            }
            sb.Insert(0, $"read as expected: {hits}/{items.Length} ({(double)hits / items.Length:P1})\n");
            File.WriteAllText(output, sb.ToString());
        }

        [Test]
        public void RunAudit()
        {
            string input = Environment.GetEnvironmentVariable("TALK_AUDIT_IN");
            string output = Environment.GetEnvironmentVariable("TALK_AUDIT_OUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                Assert.Ignore("set TALK_AUDIT_IN and TALK_AUDIT_OUT");
            Corpus = Load();
            Recent = new VC.RecentLines();
            Rng = new Random(7);
            Now = new DateTime(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
            Item[] items = JsonSerializer.Deserialize<Item[]>(File.ReadAllText(input));
            var sb = new StringBuilder();
            int n = 0;
            foreach (Item item in items)
            {
                n++;
                string text = item.text;
                if (!string.IsNullOrEmpty(item.to) && AutonomousChatThreads.WithoutName(text, item.to) is { Length: > 0 } unnamed)
                    text = unnamed;
                string path;
                var replies = new List<string>();
                eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(text);
                string channel = "audit:" + n;
                var log = new ChatThreadLog();
                if (!string.IsNullOrEmpty(item.context))
                    log.Record(channel, 9, "Brannoc", item.context, item.context.TrimEnd().EndsWith('?') ? "ask" : "chat", 1_000, "Wynn");
                string word = AutonomousChatThreads.FollowUpWord(text);
                ChatThreadLog.Said last = log.Last(channel, 5_000, AutonomousChatThreads.FollowUpWindowMs);
                ChatThreadLog.Said open = log.OpenQuestion(channel, 5_000, AutonomousChatThreads.AnswerWindowMs);
                if (word == "y")
                    word = last is { Asked: true } ? null : "why";
                if (word == "confirm" && (last == null || !AutonomousChatThreads.ConfirmFits(text, last.Text)))
                    word = null;
                bool tell = item.channel == "tell";
                bool addressed = tell || item.channel == "group" || item.expect == "directed" || !string.IsNullOrEmpty(item.to);
                if (word != null && last != null)
                {
                    path = "thread followup_" + word;
                    if (word == "where")
                        replies.Add("[where the bot is]");
                    else
                        replies.Add(Pick(n, answers: "followup_" + word, filter: l => l.Topic is "general" or "chat" or ""));
                }
                else if (open != null && AutonomousChatThreads.SaysYes(text) == false)
                {
                    path = "thread decline->closing";
                    replies.Add(Closing(n, AutonomousChatThreads.ClosingSets(open, text)));
                }
                else if (last != null && intent == eAutonomousChatIntent.Thanks && !text.Contains('?'))
                {
                    path = AutonomousChatThreads.EndsAnExchange(last.Text) ? "thread thanks, exchange done" : "thread thanks->np";
                    replies.Add(AutonomousChatThreads.EndsAnExchange(last.Text) ? "(nothing)" : Pick(n, intent: "thanks"));
                }
                else if (last != null && intent == eAutonomousChatIntent.Laugh)
                {
                    path = "thread laugh->joker";
                    replies.Add(Pick(n, intent: "laugh", filter: l => AutonomousChatThreads.FitsOwnJoke(l.Text)));
                }
                else if (open != null && AutonomousChatThreads.ReadsAsAnswer(text, intent))
                {
                    path = "thread answer->closing";
                    replies.Add(AutonomousChatShortTalk.QuestionShape(text) == "player_wait"
                        ? Pick(n, answers: "player_wait")
                        : Closing(n, AutonomousChatThreads.ClosingSets(open, text)));
                }
                else if (tell && AutonomousBotChatCoordinator.SaysInvited(text))
                {
                    path = "tell invited";
                    replies.Add("[got it, omw / didnt get it]");
                }
                else if (item.channel == "group" && intent == eAutonomousChatIntent.Alarm)
                {
                    path = "group warning";
                    replies.Add(Pick(n, answers: "player_headsup"));
                }
                else if (tell && AutonomousBotChatCoordinator.AsksToJoinGroup(text))
                {
                    path = "tell join";
                    replies.Add("[JoinReply]");
                }
                else if (tell && AutonomousBotChatCoordinator.AsksWhereYouAre(text))
                {
                    path = "tell where";
                    replies.Add("[WhereText]");
                }
                else if (AutonomousChatKnowledge.TryExplainTerm(text, out string term))
                {
                    path = "knowledge term";
                    replies.Add(term);
                }
                else
                {
                    bool shortTalk = !text.Contains('?') && intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or
                        eAutonomousChatIntent.Ding or eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or
                        eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.BotCheck;
                    string shape = AutonomousChatShortTalk.QuestionShape(text);
                    string who = "";
                    if (!tell && item.channel == "faction" && intent == eAutonomousChatIntent.Thanks)
                        who = " [faction ty: groupmates only]";
                    if (!tell && intent == eAutonomousChatIntent.Congrats)
                        who = " [gz: only a bot that just levelled]";
                    if (shape == null && !shortTalk && !tell && AutonomousChatKnowledge.LooksLikeTrainerQuestion(text))
                        who += " [db knowledge may answer]";
                    if (!tell && AutonomousBotChatCoordinator.AsksToJoinGroup(text) && (item.expect == "directed" || item.channel == "say" &&
                            !AutonomousBotChatCoordinator.BroadcastsLfg(text)))
                        who += " [JoinReply by the bot asked]";
                    (string p, string r) = Voiced(text, n, addressed);
                    (string p2, string r2) = Voiced(text, n + 1000, addressed);
                    path = p + who;
                    replies.Add(r);
                    if (!tell && r2 != r)
                        replies.Add(r2);
                }
                sb.AppendLine($"{n,3}. [{item.channel}] {text}");
                if (!string.IsNullOrEmpty(item.context))
                    sb.AppendLine($"     bot before: {item.context}");
                sb.AppendLine($"     expect {item.expect}: {item.note}");
                sb.AppendLine($"     -> {path} | intent={intent} shape={AutonomousChatShortTalk.QuestionShape(text) ?? "-"}");
                foreach (string r in replies)
                    sb.AppendLine($"        \"{r ?? "(nothing)"}\"");
            }
            File.WriteAllText(output, sb.ToString());
        }
    }
}
