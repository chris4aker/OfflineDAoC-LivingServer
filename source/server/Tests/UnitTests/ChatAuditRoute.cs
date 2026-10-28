using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DOL.GS.Tests
{
    /// <summary>
    /// How the reply path reads one player line, without a world: the thread rules, the question shapes, the intent and the
    /// answers from facts. Mirrors AutonomousBotChatCoordinator (TryThreadReply, SchedulePlayerReplies, VoicedReply, TellReply)
    /// and AutonomousBotGroupChatter; keep the two in step. Used by UT_TalkingAudit (regression on the 320 audit lines and the two holdout sets fixed after them) and the
    /// UT_ChatReplyAudit tool (which also picks the replies).
    /// </summary>
    internal static class ChatAuditRoute
    {
        /// <param name="to">The bot named in the line, if any: the live code knows every bot's name and reads the line without it.</param>
        internal sealed record Item(string text, string channel, string context, string expect, string note, string to = null);

        internal static Item[] Load(string file = "chat_audit_player_lines.json")
        {
            var assembly = typeof(ChatAuditRoute).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(file, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Item[]>(reader.ReadToEnd());
        }

        internal static readonly AutonomousChatQuestions.BotFacts Me =
            new(501, 30, "Thane", eRealm.Midgard, 13, BotPersonalityType.Leveler, false, 2, null);

        internal static AutonomousChatQuestions.WorldFacts World => new(eRealm.Hibernia,
            [("Hibernia's Relic of Lugh", eRealm.Hibernia, eRealm.Hibernia, true), ("Midgard's Relic of Thor", eRealm.Albion, eRealm.Midgard, true),
             ("Midgard's Relic of Odin", eRealm.Midgard, eRealm.Midgard, true)],
            [("Bledmeer Faste", eRealm.Midgard), ("Caer Benowyc", eRealm.Albion), ("Dun Crauchon", eRealm.Hibernia)], AutonomousChatPlaces.All);

        private static readonly eAutonomousChatIntent[] NoNotSure =
        [
            eAutonomousChatIntent.Greeting, eAutonomousChatIntent.Thanks, eAutonomousChatIntent.Rez, eAutonomousChatIntent.Alarm,
            eAutonomousChatIntent.Grouping, eAutonomousChatIntent.BotCheck, eAutonomousChatIntent.Abuse, eAutonomousChatIntent.Farewell,
            eAutonomousChatIntent.Ding, eAutonomousChatIntent.Congrats, eAutonomousChatIntent.Back, eAutonomousChatIntent.Away,
        ];

        /// <summary>The route a line takes ("thread followup_why", "fact world", "shape player_offer", "intent banter", "none"...),
        /// and the fixed answer when it comes from facts.</summary>
        internal static (string Path, string Answer) Route(Item item, Random random)
        {
            string text = item.text;
            if (!string.IsNullOrEmpty(item.to) && AutonomousChatThreads.WithoutName(text, item.to) is { Length: > 0 } unnamed)
                text = unnamed;
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(text);
            bool tell = item.channel == "tell";
            bool group = item.channel == "group";
            bool directed = item.expect == "directed" || !string.IsNullOrEmpty(item.to);
            bool addressed = tell || group || directed || item.channel == "say";
            // The thread: the bot's line before, if any.
            var log = new ChatThreadLog();
            const string channel = "audit";
            if (!string.IsNullOrEmpty(item.context))
                log.Record(channel, 9, "Brannoc", item.context, item.context.TrimEnd().EndsWith('?') ? "ask" : "chat", 1_000, "Wynn");
            ChatThreadLog.Said last = log.Last(channel, 5_000, AutonomousChatThreads.FollowUpWindowMs);
            ChatThreadLog.Said open = log.OpenQuestion(channel, 5_000, AutonomousChatThreads.AnswerWindowMs);
            if (AutonomousChatThreads.JoinsCall(text) && last != null && AutonomousChatThreads.CallsForGroup(last.Text))
                return ("join", null);
            string word = AutonomousChatThreads.FollowUpWord(text);
            if (word == "y")
                word = last is { Asked: true } ? null : "why";
            if (word == "confirm" && (last == null || !AutonomousChatThreads.ConfirmFits(text, last.Text)))
                word = null;
            if (word != null && last != null)
                return ("thread followup_" + word, null);
            if (open != null && AutonomousChatThreads.SaysYes(text) == false)
                return ("thread decline", null);
            if (last != null && intent == eAutonomousChatIntent.Thanks && !text.Contains('?'))
                return AutonomousChatThreads.EndsAnExchange(last.Text) ? ("none thanked", null) : ("thread thanks", null);
            if (last != null && intent == eAutonomousChatIntent.Laugh)
                return ("thread laugh", null);
            if (open != null && AutonomousChatThreads.ReadsAsAnswer(text, intent))
                return (AutonomousChatShortTalk.QuestionShape(text) == "player_wait" ? "thread wait" : "thread answer", null);
            if (group && intent == eAutonomousChatIntent.Alarm)
                return ("group warning", null);
            if (tell)
            {
                if (AutonomousBotChatCoordinator.SaysInvited(text))
                    return ("tell invited", null);
                if (AutonomousBotChatCoordinator.AsksToJoinGroup(text))
                    return ("join", null);
                if (AutonomousBotChatCoordinator.AsksWhereYouAre(text))
                    return ("live where", null);
            }
            string shape = AutonomousChatShortTalk.QuestionShape(text);
            if (shape == "ack")
                return ("none ack", null);
            if (!tell && AutonomousBotChatCoordinator.AsksToJoinGroup(text) &&
                (directed || item.channel == "say" && !AutonomousBotChatCoordinator.BroadcastsLfg(text)))
                return ("join", null);
            if (!directed && !tell && !group && shape is "player_wb" or "player_order")
                return ("none undirected", null);
            if (item.channel == "faction" && intent == eAutonomousChatIntent.Laugh)
                return ("none laugh", null);
            if (item.channel == "faction" && intent == eAutonomousChatIntent.Congrats)
                return ("none gz", null);
            if (AutonomousChatKnowledge.TryExplainTerm(text, out string term))
                return ("knowledge", term);
            if (shape == "player_order" && AutonomousChatQuestions.CantDo(text, Me) is { } cant)
                return ("fact cant", cant);
            if (intent is not (eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse))
            {
                string self = addressed ? AutonomousChatQuestions.SelfQuestion(text) : null;
                string fact = self != null ? AutonomousChatQuestions.AnswerSelf(self, text, Me, random)
                    : AutonomousChatQuestions.AnswerWorld(text, eRealm.Midgard, World, random);
                if (fact != null)
                    return ("fact " + (self ?? "world"), fact);
            }
            if (shape is "ask_whereyou" or "ask_inplace" or "ask_hunt" or "ask_enemy")
                return ("live " + shape, null);
            if (shape != null)
                return ("shape " + shape, null);
            if (AutonomousChatShortTalk.IsFollowUp(text))
                return ("none followup", null);
            if (AutonomousChatQuestions.IsFactual(text) && !NoNotSure.Contains(intent))
                return ("not sure", null);
            if (intent == eAutonomousChatIntent.Unknown)
                return text.Contains('?') ? ("not sure", null) : ("none unknown", null);
            if (intent is eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR && !AutonomousChatQuestions.IsQuestion(text) &&
                !AutonomousBotChatCoordinator.PlansSomething(text))
                return ("intent banter", null);
            return ("intent " + AutonomousBotVoices.IntentName(intent), null);
        }

        /// <summary>Whether a route is what a sensible player expects for the line's label.</summary>
        internal static bool Fits(string expect, string path) => expect switch
        {
            "greeting" => path is "intent greeting" or "shape ask_howareyou",
            "farewell" => path is "intent farewell",
            "thanks_ack" => path is "intent thanks" or "thread thanks",
            "congrats" => path is "intent ding" or "shape player_goodnews",
            "welcome_back" => path is "intent back" or "intent greeting",
            "brb_ack" => path is "intent away" or "shape player_wait",
            "answer_yesno" or "answer_info" => path.StartsWith("fact") || path.StartsWith("live") || path is "knowledge" or "not sure" or
                "shape ask_price" or "shape ask_help",
            "answer_opinion" => path is "shape ask_opinion" or "shape ask_choice" or "intent classtalk" or "intent dungeon" or "fact world",
            "answer_choice" => path is "shape ask_choice",
            "offer_help" => path is "shape player_offer" or "shape ask_help" or "shape ask_anyone" or "shape ask_advice" or "intent help" or "not sure" ||
                path.StartsWith("fact"),
            "join_reply" => path is "join" or "tell invited",
            "lfg_response" => path is "intent grouping",
            "rez_response" => path is "intent rez" or "shape player_wait",
            "alarm_response" => path is "intent alarm" or "group warning" or "shape player_headsup",
            "trade_response" => path is "intent trading" or "shape ask_price" or "shape player_offer" or "shape ask_anyone" || path.StartsWith("fact"),
            "followup" => path.StartsWith("thread followup"),
            "answer_to_bot" => path is "thread answer" or "thread decline" or "thread thanks" or "thread wait",
            "sympathy" => path is "shape complaint" || path.StartsWith("shape player_rl"),
            "banter" => path is "intent banter" or "intent laugh" or "shape player_goodnews",
            "bot_check" => path is "intent botcheck",
            "abuse" => path is "intent abuse" or "none unknown",
            "out_of_world" => path.StartsWith("shape player_rl") || path is "shape ask_choice",
            // Scorer fix after the first holdout run: the shapes about the bot itself ("what lvl r u", "how close r u to 50",
            // "what u doing tonight") answer a directed question too.
            "directed" => path.StartsWith("fact") || path is "intent banter" or "shape ask_level" or "shape ask_xp" or "shape ask_plans" or
                "shape ask_doing" or "live ask_whereyou" or "live where",
            "none" => path.StartsWith("none") || path is "intent away" || path is "intent laugh" || path == "intent thanks",
            _ => false,
        };
    }
}
