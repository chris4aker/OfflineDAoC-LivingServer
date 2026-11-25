using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ReplyChoice = DOL.GS.AutonomousBotChatCoordinator.ReplyChoice;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// The reply path a player's line takes, as pure functions (1.2.2 chat fixes, F0): what the line draws (<see cref="Plan"/>), the
/// first answer's choice (<see cref="Choose"/>), the voiced reply (<see cref="Voiced"/>), the tell's own steps
/// (<see cref="Tell"/>) and the reply budget (<see cref="Budget"/>). The coordinator calls them with ports filled from a live
/// bot and the live world (AutonomousBotChatCoordinator.PortsFor); the question bench (UT_ChatQuestionBench) with ports filled
/// from its stub world, so the bench measures the routing the server runs, not a copy of it. Whatever needs the world or the
/// bot (a corpus pick, the bot's facts, its memory, the promise gate) is a port; the order and the rules are here.
/// </summary>
public static class AutonomousChatReplyPath
{
    /// <summary>A corpus line for the bot: kind ("reply", "closing"), intent, answers, extra flags, the short-reply rest, a filter.</summary>
    public delegate VC.Picked LinePick(string kind, string intent, string answers, IEnumerable<string> flags, bool relax, Func<VC.Line, bool> filter);

    /// <summary>What the path asks of the responding bot and the world. A port left null answers nothing.</summary>
    public sealed class Ports
    {
        public Random Random { get; init; } = Random.Shared;
        /// <summary>AutonomousBotVoices.Enabled.</summary>
        public bool Voices { get; init; } = true;
        /// <summary>AutonomousLivingServer.MemoryOn.</summary>
        public bool Memory { get; init; }
        /// <summary>The responder's id (0: none) and class name.</summary>
        public long BotId { get; init; }
        public string ClassName { get; init; }
        /// <summary>The responder's level, or null without a bot (the band reply needs it).</summary>
        public int? Level { get; init; }
        /// <summary>A true answer about the bot or the world (AutonomousChatLiveAnswers.AnswerQuestion), styled: (line, addressed).</summary>
        public Func<string, bool, string> Fact { get; init; }
        /// <summary>The situation's facts for AutonomousChatContext.Reply: (channel, line).</summary>
        public Func<string, string, AutonomousChatContext.Facts> Context { get; init; }
        /// <summary>A reply by name from a bot that remembers the player: (intent, line), or null.</summary>
        public Func<string, string, string> Known { get; init; }
        /// <summary>Whether the bot would really come for a rez.</summary>
        public Func<bool> WillComeRez { get; init; }
        /// <summary>The bot's own facts (class, level, speed...).</summary>
        public Func<AutonomousChatQuestions.BotFacts> Facts { get; init; }
        /// <summary>The live answer to a question shape: (shape, line), unstyled, or null (where the bot is, where to hunt, the enemy).</summary>
        public Func<string, string, string> Live { get; init; }
        public LinePick Line { get; init; }
        /// <summary>The bot's writing style on a text built in code.</summary>
        public Func<string, string> Style { get; init; }
        /// <summary>What the reply budget knows of the moment: (line, channel, addressed).</summary>
        public Func<string, string, bool, AutonomousChatReplyBudget.Moment> Moment { get; init; }
        /// <summary>Whether the conversation already holds what a reply asks: (reply, channel, addressed).</summary>
        public Func<string, string, bool, bool> Holds { get; init; }
        /// <summary>Whether a reply says something untrue of the bot (another class, role or level).</summary>
        public Func<string, bool> Untrue { get; init; }

        internal string Styled(string text) => text == null || Style == null ? text : Style(text);

        internal VC.Picked Pick(string intent = null, string answers = null, IEnumerable<string> flags = null, bool relax = false,
            Func<VC.Line, bool> filter = null, string kind = "reply") =>
            Line?.Invoke(kind, intent, answers, flags, relax, filter);
    }

    // ------------------------------------------------------------------------------------------------------------- the line

    /// <summary>Thanks, a laugh, a hello and the like said without a question: short talk, not a knowledge lookup. Pure.</summary>
    public static bool IsShortTalk(string message, eAutonomousChatIntent intent, bool tell = false) =>
        !string.IsNullOrEmpty(message) && !message.Contains('?') && (tell
            ? intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or
                eAutonomousChatIntent.Congrats or eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back
            : intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or eAutonomousChatIntent.Ding or eAutonomousChatIntent.Laugh or
                eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or
                eAutonomousChatIntent.BotCheck);

    /// <summary>
    /// A question about grouping, not a group post (F6, the bench's C3): "what does lfg mean?", "is a bard needed in groups?",
    /// "do i need a group for my epic?", "how do i invite someone to a group?". It is answered as a question (the knowledge lookup,
    /// the corpus), never with "nobody around your level". "anyone lfg?", "lfg 30 cleric", "is anyone looking for a group?" are
    /// group posts. Pure.
    /// </summary>
    public static bool AsksAboutGrouping(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !AutonomousChatQuestions.IsQuestion(message))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(message) + " ";
        if (Regex.IsMatch(t, @" (anyone|any1|anybody|who wants|who s up|whos up|wanna|want to|lf\d|lfm|pst|room for|inv me|invite me) "))
            return false;
        return Regex.IsMatch(t, @"^ (what|whats|wat|how|why|is (a|an|the|it)|are|do i|does|should i|can i|which|whats the) ") ||
               Regex.IsMatch(t, @" (mean|means|stand for|stands for) ") ||
               // "23 friar looking for a spot to grind, ideas?": a place to level, not a group.
               Regex.IsMatch(t, @" (spot|spots|place|zone|camp) to (grind|level|lvl|xp|hunt) | (looking for|lf) a (spot|place|zone|camp) ");
    }

    /// <summary>
    /// Whether the knowledge lookup answers a line before the bot's own reply: not short talk, not a call for help, and not a
    /// question to a bot named in the line about itself ("aldwyn did you finish your epic?": its own facts answer, not the
    /// general "long chain"). Pure.
    /// </summary>
    public static bool KnowledgeFirst(string message, eAutonomousChatIntent intent, bool helpCall, bool directed) =>
        (!IsShortTalk(message, intent) || AsksWhatNext(message)) && !helpCall && !(directed && AutonomousChatQuestions.SelfQuestion(message) != null);

    /// <summary>"just dinged 50, now what": a ding that asks what comes next is a question (Realism 2e, #482: it drew "Ooh nice"). Pure.</summary>
    public static bool AsksWhatNext(string message) =>
        !string.IsNullOrWhiteSpace(message) &&
        System.Text.RegularExpressions.Regex.IsMatch(" " + AutonomousChatQuestions.Normalize(message) + " ", @" (now what|what now|whats next|what next|then what|what do i do now) ");

    /// <summary>What a player's line draws, decided once for the line (<see cref="Plan"/>).</summary>
    public enum Step
    {
        /// <summary>Nobody answers (Route says why).</summary>
        Silent,
        /// <summary>A bot named while busy answers when free (the caller waited for it).</summary>
        Waited,
        /// <summary>The bot asked says whether it will join.</summary>
        Join,
        /// <summary>The first answer is chosen per bot (<see cref="Choose"/>).</summary>
        Reply,
    }

    /// <summary>The line's facts as the coordinator knows them when it plans the answers.</summary>
    public sealed record LineIn(string Message, bool Faction, bool Directed, bool Voices, eAutonomousChatIntent Intent, string Shape, bool HelpCall,
        string Knowledge, int AskerLevel);

    /// <summary>
    /// The plan for a line: the step; the silence's route and reason; whether only bots that fit a group post answer (Grouping,
    /// with the level and role), only a bot that just levelled (JustLevelled) or only the player's groupmates (Groupmates); the
    /// honest "nobody near your level" reply.
    /// </summary>
    public sealed record LinePlan(Step Step, string Route = null, string Why = null, bool Grouping = false, int GroupLevel = 0, string Role = null,
        bool JustLevelled = false, bool Groupmates = false, string NoneNearLevel = null);

    /// <summary>
    /// What a player's /faction or /say line draws (SchedulePlayerReplies' order), pure but for the ports:
    /// <paramref name="waitForBusyNamed"/> waits for a busy bot the line names (true: it took the line), <paramref name="anyFits"/>
    /// says whether a bot that heard a group post fits its level and role, <paramref name="anyEligible"/> whether any bot heard it.
    /// </summary>
    public static LinePlan Plan(LineIn line, Func<bool> waitForBusyNamed, Func<int, string, bool> anyFits, bool anyEligible, Random random)
    {
        random ??= Random.Shared;
        string message = line.Message ?? string.Empty;
        bool voices = line.Voices;
        // "ok", "nice", "me too" draw no answer (chat audit 2026-10-21).
        if (line.Shape == "ack")
            return new LinePlan(Step.Silent, "none ack", "an acknowledgement draws no answer");
        // "wb Sigrun" and "Aldric pull the next one" are for the bot named; said to nobody we know, nobody answers.
        if (voices && !line.Directed && line.Shape is "player_wb" or "player_order")
            return new LinePlan(Step.Silent, "none undirected", "said to nobody a bot knows");
        if (voices && !line.Directed && waitForBusyNamed != null && waitForBusyNamed())
            return new LinePlan(Step.Waited, "named busy");
        // A line said to someone else by name is theirs to answer.
        if (voices && !line.Directed && AutonomousChatScreen.OtherAddressee(message, null) is { } other)
            return new LinePlan(Step.Silent, "none other", $"said to {other}, not to a bot that heard it");
        if (voices && anyEligible && AutonomousBotChatCoordinator.AsksToJoinGroup(message) &&
            (line.Directed || !line.Faction && !AutonomousBotChatCoordinator.BroadcastsLfg(message)))
            return new LinePlan(Step.Join, "join");
        // A group post hears from bots of the role and level asked; a question about grouping, or one the knowledge lookup
        // answers ("what does lfg mean?"), is answered as a question (F6).
        if (voices && !line.Directed && line.Intent == eAutonomousChatIntent.Grouping && line.Knowledge == null && !AsksAboutGrouping(message))
        {
            string role = AutonomousChatShortTalk.NeededRole(message);
            int level = AutonomousChatLiveAnswers.LevelAsked(message, line.AskerLevel);
            bool fits = anyFits?.Invoke(level, role) == true;
            string none = !fits && anyEligible && random.NextDouble() < AutonomousBotChatCoordinator.NoneNearLevelReplyChance
                ? AutonomousBotChatCoordinator.NoneNearLevelReply(level, role, random)
                : null;
            return new LinePlan(Step.Reply, Grouping: true, GroupLevel: level, Role: role, NoneNearLevel: none);
        }
        if (voices && line.Intent == eAutonomousChatIntent.Congrats)
            return new LinePlan(Step.Reply, JustLevelled: true);
        if (voices && line.Intent == eAutonomousChatIntent.Thanks && line.Faction)
            return new LinePlan(Step.Reply, Groupmates: true);
        return new LinePlan(Step.Reply);
    }

    /// <summary>How many bots answer a line: one for knowledge, a factual question or a live answer; the arbiter does the rest. Pure.</summary>
    public static int ReplyCount(LineIn line, LinePlan plan, int eligible, bool gratsSaidRecently, Random random)
    {
        random ??= Random.Shared;
        bool hasKnowledge = line.Knowledge != null;
        int count = hasKnowledge ? Math.Min(1, eligible)
            : line.Voices ? AutonomousBotChatCoordinator.PlayerReplyCount(line.Intent, line.Faction, line.Directed, eligible, random)
            : line.Directed || line.Intent is eAutonomousChatIntent.IgnoreOutOfWorld or eAutonomousChatIntent.Abuse ? Math.Min(1, eligible)
            : AutonomousBotChatCoordinator.RollResponseCount(eligible, random);
        if (line.Shape is "ask_hunt" or "ask_enemy" or "ask_price" or "player_offer" or "player_wait")
            count = Math.Min(count, 1);
        if (plan.NoneNearLevel != null)
            count = Math.Min(1, eligible);
        if (line.Voices && AutonomousChatQuestions.IsFactual(line.Message))
            count = Math.Min(count, 1);
        if (line.Voices && line.Intent == eAutonomousChatIntent.Ding && !line.Directed && gratsSaidRecently)
            count = Math.Min(count, random.NextDouble() < 0.5 ? 1 : 0);
        return count;
    }

    /// <summary>
    /// The first answer of one bot to the line: the honest "nobody near your level", the help path's reply, the knowledge
    /// answer, else the voiced reply (<paramref name="voiced"/>, given whether the bot was addressed), through the reply budget
    /// unless it is knowledge or a live none-near-level. <paramref name="help"/> is the help path's line (null: none);
    /// <paramref name="known"/> wraps a reply to a bot named in the line with its memory of the player.
    /// </summary>
    public static ReplyChoice Choose(Ports ports, LineIn line, LinePlan plan, string channel, Func<string> help, Func<bool, ReplyChoice> voiced,
        Func<Func<ReplyChoice>, ReplyChoice> known = null)
    {
        if (plan.NoneNearLevel != null)
            return new ReplyChoice(ports.Styled(plan.NoneNearLevel), "live none_near_level");
        if (line.Knowledge != null)
            return new ReplyChoice(ports.Styled(line.Knowledge), "knowledge");
        ReplyChoice choice;
        if (line.HelpCall && help?.Invoke() is { } helpLine)
            choice = new ReplyChoice(ports.Styled(helpLine), "context help");
        else if (line.Directed)
            choice = known != null ? known(() => voiced(true)) : voiced(true);
        else
            // In /say a "u" is the bot nearby ("u selling that sword?"); in /faction it is nobody in particular.
            choice = voiced(!line.Faction);
        // The reply budget: under the minimum fit, a bot named in the line acknowledges it and any other stays quiet.
        return Budget(ports, choice, line.Message, line.Directed, channel);
    }

    // --------------------------------------------------------------------------------------------------------------- a tell

    /// <summary>
    /// A tell's own steps after its threads (TellReply): an invite said, a group ask, "where are you?", a friend's small talk, a
    /// true answer, the knowledge lookup, else the voiced reply. The world's parts are delegates: <paramref name="invited"/>,
    /// <paramref name="join"/>, <paramref name="where"/>, <paramref name="friend"/> (text and whether it asks the player along) and
    /// <paramref name="knowledge"/>. Not budgeted here (the caller does it).
    /// </summary>
    public static ReplyChoice Tell(Ports ports, string message, eAutonomousChatIntent intent, Func<string> invited, Func<string> join, Func<string> where,
        Func<(string Text, bool Offers)?> friend, Func<string> knowledge, Func<ReplyChoice> voiced, Func<string> meetup = null)
    {
        bool shortTalk = ports.Voices && IsShortTalk(message, intent, tell: true);
        if (AutonomousBotChatCoordinator.SaysInvited(message) && invited?.Invoke() is { } got)
            return new ReplyChoice(ports.Styled(got), "tell invited");
        if (AutonomousBotChatCoordinator.AsksToJoinGroup(message) && join?.Invoke() is { } joining)
            return new ReplyChoice(ports.Styled(joining), "join");
        if (AutonomousBotChatCoordinator.AsksWhereYouAre(message) && where?.Invoke() is { } here)
            return new ReplyChoice(ports.Styled(here), "live where");
        // 1.2.3 (the owner's game: "I'm by camp station" by tell drew "lol"): where the player says they are, or that they are
        // coming, is answered with where the bot is and how to meet (AutonomousChatMeetup).
        if (ports.Voices && meetup?.Invoke() is { } meet)
            return new ReplyChoice(ports.Styled(meet), "live meetup");
        if (ports.Voices && friend?.Invoke() is { } friendly && friendly.Text != null)
            return new ReplyChoice(ports.Styled(friendly.Text), friendly.Offers ? AutonomousBotChatCoordinator.FriendInviteRoute : "friend chat");
        if (ports.Voices && ports.Fact?.Invoke(message, true) is { } fact)
            return new ReplyChoice(fact, "fact " + (AutonomousChatQuestions.SelfQuestion(message) ?? "world"));
        if (!shortTalk && knowledge?.Invoke() is { } k)
            return new ReplyChoice(ports.Styled(k), "knowledge");
        return voiced();
    }

    // -------------------------------------------------------------------------------------------------------- the voiced reply

    /// <summary>
    /// A reply to a greeting: "how is everyone?" gets "not bad, you?", a plain "hi all" a hello and never an answer to a question
    /// nobody asked; a holiday wish its holiday's answer.
    /// </summary>
    public static VC.Picked Greeting(LinePick line, string incoming, bool relax)
    {
        if (line == null)
            return null;
        bool asked = AutonomousChatShortTalk.AsksHowAreYou(incoming);
        string greeting = AutonomousBotVoices.IntentName(eAutonomousChatIntent.Greeting);
        if (AutonomousChatIntentModel.IsHolidayWish(incoming) &&
            line("reply", greeting, null, null, true, l => l.Needs.Any(need => need.StartsWith("holiday_", StringComparison.Ordinal))) is { } wish)
            return wish;
        if (asked && line("reply", null, "ask_howareyou", null, relax, null) is { } fine)
            return fine;
        return line("reply", greeting, null, null, relax, l => AutonomousBotVoices.PlainGreetingFits(l, asked));
    }

    /// <summary>A reply for the kind of question a player asked ("anyone on?" -> "here"), before replies by topic.</summary>
    public static VC.Picked Shaped(Ports ports, string incoming, bool relax)
    {
        string shape = AutonomousChatShortTalk.QuestionShape(incoming);
        if (shape == "ask_howareyou")
            return Greeting(ports.Line, incoming, relax);
        // Answers from the live world: where the bot is, whether it is in the place asked about, where people of the level hunt
        // now, where the enemy is.
        if (shape is "ask_whereyou" or "ask_inplace" or "ask_hunt" or "ask_enemy" && ports.Live?.Invoke(shape, incoming) is { } live)
            return new VC.Picked(ports.Styled(live), null, null);
        if (shape is null or "ack" or "ask_whereyou" or "ask_inplace" or "ask_hunt" or "ask_enemy")
            return null;
        // "can you help me?" is answered "sure, whats up?", not a random tip.
        if (shape == "ask_help")
            return ports.Pick(AutonomousBotVoices.IntentName(eAutonomousChatIntent.Help), relax: relax,
                filter: line => line.Text.TrimEnd().EndsWith('?') || line.Text.Contains("whats up", StringComparison.OrdinalIgnoreCase) ||
                                line.Text.Contains("what do", StringComparison.OrdinalIgnoreCase));
        string answers = shape == "ask_price" ? "trade" : shape;
        return ports.Pick(answers: answers, relax: relax, filter: line => AutonomousBotVoices.ShapedFits(shape, incoming, line.Text));
    }

    /// <summary>
    /// The voiced reply (VoicedChoiceCore): a true answer, the situation, a holiday wish, the bot's memory, an honest no to an
    /// order, the question's shape, then the corpus by intent; a factual question no answer knew gets the caller's
    /// <paramref name="fallback"/> (#478: the "not sure" set, <c>@answers ask</c>, no longer answers in any channel; 1.2.2's F4
    /// took it out of /faction only).
    /// </summary>
    public static ReplyChoice Voiced(Ports ports, string incoming, Func<string> fallback, eAutonomousChatIntent? knownIntent, bool addressed, string channel,
        bool relax)
    {
        fallback ??= () => null;
        Random random = ports.Random ?? Random.Shared;
        if (!ports.Voices && !ports.Memory)
            return new ReplyChoice(fallback(), "legacy");
        eAutonomousChatIntent value = knownIntent ?? AutonomousChatIntentModel.Predict(incoming);
        string intent = AutonomousBotVoices.IntentName(value);
        string shape = AutonomousChatShortTalk.QuestionShape(incoming);
        // A question the bot can answer truly is answered first, even by a bot that remembers the player.
        if (ports.Voices && ports.Fact?.Invoke(incoming, addressed) is { } knownFact)
            return new ReplyChoice(knownFact, "fact " + (addressed ? AutonomousChatQuestions.SelfQuestion(incoming) ?? "world" : "world"));
        // The situation decides before the words' topic (gn, /stuck, inc, a call for help, a sale, a counter-offer).
        if (ports.Voices && ports.Context != null &&
            AutonomousChatContext.Reply(incoming, value, shape, ports.Context(channel, incoming), random) is { } situation)
            return new ReplyChoice(ports.Styled(situation.Text), situation.Route);
        // A holiday wish is answered in kind before anything else.
        if (ports.Voices && value == eAutonomousChatIntent.Greeting && AutonomousChatIntentModel.IsHolidayWish(incoming) &&
            Greeting(ports.Line, incoming, true) is { } wish && wish.Line?.Needs?.Any(need => need.StartsWith("holiday_", StringComparison.Ordinal)) == true)
            return new ReplyChoice(wish.Text, "intent greeting", wish.Line);
        // A bot that remembers this player answers by name (Living Server memory); not to a question with answers of its own.
        if (ports.Known != null && shape == null && random.NextDouble() < 0.6 && ports.Known(intent, incoming) is { } known)
            return new ReplyChoice(known, "memory " + intent);
        if (!ports.Voices)
            return new ReplyChoice(fallback(), "legacy");
        // Only a bot that will really come says "omw" to "rez pls".
        string[] with = value == eAutonomousChatIntent.Rez && ports.WillComeRez?.Invoke() == true ? ["will_rez"] : null;
        // An order the bot can't carry out gets an honest no ("heal me" to a thane), not "on it".
        if (shape == "player_order" && ports.Facts != null && AutonomousChatQuestions.CantDo(incoming, ports.Facts()) is { } cant)
            return new ReplyChoice(ports.Styled(cant), "fact cant");
        if (Shaped(ports, incoming, relax) is { } shaped)
            return new ReplyChoice(shaped.Text, (shaped.Line == null ? "live " : "shape ") + shape, shaped.Line);
        // An acknowledgement gets no answer; "anyone in camelot?" only from a bot that is there.
        if (shape is "ack" or "ask_inplace")
            return new ReplyChoice(null, "none " + (shape == "ack" ? "ack" : "not_there"));
        // "really?", "why?": a follow-up only the one it was said to can answer.
        if (AutonomousChatShortTalk.IsFollowUp(incoming))
            return new ReplyChoice(fallback(), "none followup");
        // A factual question no answer above knew gets no remark on its topic, and no "not sure" (#478).
        if (AutonomousChatQuestions.IsFactual(incoming) && value is not (eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Thanks or
                eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Grouping or eAutonomousChatIntent.BotCheck or
                eAutonomousChatIntent.Abuse or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Ding or eAutonomousChatIntent.Congrats or
                eAutonomousChatIntent.Back or eAutonomousChatIntent.Away or eAutonomousChatIntent.Roleplay))
            return new ReplyChoice(fallback(), "none factual");
        // An unread line gets no reply (#478: and a question no "not sure").
        if (value == eAutonomousChatIntent.Unknown)
            return new ReplyChoice(fallback(), "none unknown");
        // An in-character line is answered in kind for what it does, else with an emote.
        if (value == eAutonomousChatIntent.Roleplay)
        {
            string act = AutonomousChatShortTalk.RoleplayAct(incoming);
            foreach (string answers in new[] { "player_rp_" + act, "player_rp_emote" })
                if (ports.Pick(answers: answers, relax: true) is { } inKind)
                    return new ReplyChoice(inKind.Text, "intent roleplay", inKind.Line);
            return new ReplyChoice("*nods*", "intent roleplay");
        }
        // Class talk is about the class asked about, or about no class in particular.
        if (value == eAutonomousChatIntent.ClassTalk)
        {
            // A spec question gets the spec answers the bots give each other, for the class named, else the bot's own.
            if (AutonomousChatShortTalk.AsksSpec(incoming) && SpecAnswer(incoming, ports.ClassName, random) is { } spec)
                return new ReplyChoice(ports.Styled(spec), "knowledge spec");
            string[] asked = AutonomousChatShortTalk.ClassWordsIn(incoming);
            bool AboutAsked(VC.Line line) => AutonomousChatShortTalk.ClassWordsIn(line.Text).Any(w => asked.Any(a => AutonomousChatShortTalk.SameClassWord(w, a)));
            if (asked.Length > 0 && ports.Pick(intent, relax: relax, filter: AboutAsked) is { } about)
                return new ReplyChoice(about.Text, "intent classtalk asked", about.Line);
            if (ports.Pick(intent, relax: relax, filter: line => !AutonomousChatShortTalk.NamesAClass(line.Text)) is { } general)
                return new ReplyChoice(general.Text, "intent classtalk", general.Line);
        }
        else if (value == eAutonomousChatIntent.Greeting)
        {
            if (Greeting(ports.Line, incoming, relax) is { } hello)
                return new ReplyChoice(hello.Text, "intent greeting", hello.Line);
        }
        // A remark about a dungeon or the frontier that plans nothing is answered in kind ("lol same"), not with advice.
        string topicIntent = value is eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR && !AutonomousChatQuestions.IsQuestion(incoming) &&
                             !AutonomousBotChatCoordinator.PlansSomething(incoming)
            ? AutonomousBotVoices.IntentName(eAutonomousChatIntent.Banter)
            : intent;
        if (value is not (eAutonomousChatIntent.ClassTalk or eAutonomousChatIntent.Greeting) &&
            ports.Pick(topicIntent, flags: with, relax: relax,
                filter: line => AutonomousChatShortTalk.ReplyFits(incoming, line.Text) &&
                                AutonomousChatShortTalk.TradeReplyFits(incoming, line.Text) &&
                                // "lurikeens are the cutest" drew "bet you cant solo it" (replies to a boast).
                                !(value is eAutonomousChatIntent.Banter or eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR &&
                                  Regex.IsMatch(line.Text, @"sure (u|you) did|bet (u|you) cant|bet you can't|pics or|show off", RegexOptions.IgnoreCase))) is { } voiced)
            return new ReplyChoice(voiced.Text, "intent " + topicIntent, voiced.Line);
        // No reply of the line's kind fits: the fallback (#478: never a "not sure").
        return new ReplyChoice(fallback(), "none " + intent);
    }

    /// <summary>The spec advice for the class a line names, else for the bot's own class, or null (CORPUS-C-003). Pure.</summary>
    public static string SpecAnswer(string incoming, string ownClass, Random random)
    {
        string cls = AutonomousChatFieldTalk.ClassNamed(incoming) ?? ownClass;
        if (string.IsNullOrWhiteSpace(cls))
            return null;
        // 1.2.2 (C5, fix F7): the class's own lines only, no general rule.
        string[] pool = AutonomousChatFieldTalk.ClassSpecAdvice(cls.ToLowerInvariant());
        return pool.Length == 0 ? null : pool[(random ?? Random.Shared).Next(pool.Length)];
    }

    // ---------------------------------------------------------------------------------------------------------- the budget

    /// <summary>
    /// The reply budget on a chosen reply (Budgeted): spam gets silence; no credit for heals the bot can't give; nothing the
    /// conversation already holds; nothing untrue of the bot or of the wrong situation; under the minimum fit a short reply when
    /// the line was said to the bot, else the rescue (a reaction or nothing).
    /// </summary>
    public static ReplyChoice Budget(Ports ports, ReplyChoice choice, string message, bool addressed, string channel)
    {
        if (!ports.Voices || choice.Route == "legacy")
            return choice;
        return Settled(ports, Budgeted(ports, choice, message, addressed, channel), message, addressed || channel is "tell" or "group");
    }

    /// <summary>
    /// The gate's rule where the reply is chosen (#478, <see cref="BotSayGate.Settle"/>): a non-answer is never the whole answer to
    /// a line said to the bot or asked of the channel. In its place the bot answers from what it knows of itself (its class, level,
    /// place and plans: <see cref="FromWhatItKnows"/>), or it stays quiet.
    /// </summary>
    public static ReplyChoice Settled(Ports ports, ReplyChoice choice, string message, bool saidToBot)
    {
        if (choice.Text == null)
            return choice;
        BotSayGate.NonAnswerKind kind = BotSayGate.Owed(message, saidToBot) ? BotSayGate.KindOf(choice.Text, message) : BotSayGate.NonAnswerKind.None;
        if (kind == BotSayGate.NonAnswerKind.None)
            return choice;
        string known = BotSayGate.Settle(choice.Text, message, saidToBot, () => FromWhatItKnows(ports, message));
        return known != null
            ? new ReplyChoice(known, "known (" + choice.Route + ")")
            : new ReplyChoice(null, "silent (non-answer " + kind.ToString().ToLowerInvariant() + ": " + choice.Route + ")");
    }

    /// <summary>
    /// What the bot knows of itself that answers the line: a question about it (its level, class, spec, group, plans: SelfQuestion
    /// and AnswerSelf on its facts), or where it is. Null when nothing it knows answers it. Pure but for the ports.
    /// </summary>
    public static string FromWhatItKnows(Ports ports, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || ports == null)
            return null;
        AutonomousChatQuestions.BotFacts facts = ports.Facts?.Invoke();
        Random random = ports.Random ?? Random.Shared;
        if (facts != null && AutonomousChatQuestions.AddressedSelfQuestion(message) is { } self &&
            AutonomousChatQuestions.AnswerSelf(self, message, facts, random) is { Length: > 0 } known)
            return ports.Styled(known);
        if (AutonomousBotChatCoordinator.AsksWhereYouAre(message) && ports.Live?.Invoke("ask_whereyou", message) is { Length: > 0 } here)
            return ports.Styled(here);
        return null;
    }

    private static ReplyChoice Budgeted(Ports ports, ReplyChoice choice, string message, bool addressed, string channel)
    {
        Random random = ports.Random ?? Random.Shared;
        string moments = channel ?? (addressed ? "tell" : "faction");
        if (AutonomousChatScreen.IsSpam(message))
            return new ReplyChoice(null, "silent (none spam)");
        AutonomousChatReplyBudget.Moment moment = ports.Moment?.Invoke(message, moments, addressed) ??
            new AutonomousChatReplyBudget.Moment(message, moments, addressed, AutonomousChatIntentModel.Predict(message), ports.ClassName, false, false, false);
        // Never take thanks for heals or a rez the bot can't give.
        if (AutonomousChatReplyBudget.CreditFor(message) != null && AutonomousChatReplyBudget.ClaimsCredit(message, choice.Text, moment.CanHeal, moment.CanRez))
            choice = new ReplyChoice(null, "none credit");
        // Conversation memory (DESIGN-040): a bot never asks what the conversation already holds.
        if (ports.Holds != null && ports.Holds(choice.Text, channel ?? (addressed ? "tell" : "faction"), addressed))
            choice = new ReplyChoice(null, "none known (" + choice.Route + ")");
        if (choice.Text != null && ports.Untrue?.Invoke(choice.Text) == true)
            choice = new ReplyChoice(null, "none contradicts (" + choice.Route + ")");
        else if (AutonomousChatReplyFit.Of(choice.Route, message) < AutonomousChatReplyFit.Known && AutonomousChatReplyFit.WrongSituation(message, choice.Text))
            choice = new ReplyChoice(null, "none situation (" + choice.Route + ")");
        // 1.2.3 (the owner's game: "I'm by camp station" by tell drew "lol"): a bare laugh is never the whole answer to a line said to
        // the bot that doesn't laugh itself; the short answer for the line takes its place.
        if (addressed && AutonomousChatMeetup.IsBareLaugh(choice.Text) && !AutonomousChatMeetup.Laughs(message) &&
            moment.Intent != eAutonomousChatIntent.Laugh)
            choice = new ReplyChoice(null, "none laugh (" + choice.Route + ")");
        int fit = AutonomousChatReplyFit.Of(choice.Route, message);
        if (string.IsNullOrWhiteSpace(choice.Text) || AutonomousChatReplyBudget.IsBrushOff(choice.Text))
            fit = Math.Min(fit, AutonomousChatReplyFit.Weak);
        var outcome = AutonomousChatReplyBudget.Decide(fit, addressed, AutonomousChatBudget.MinReplyFit);
        if (outcome == AutonomousChatReplyBudget.Outcome.Answer)
            return choice;
        // "k", "ok", "nice": an acknowledgement draws nothing, even by tell.
        if (choice.Route is "none ack" or "none spam" or "none other")
            return new ReplyChoice(null, "silent (" + choice.Route + ")");
        // A raid or group outside the bot's level band: it says so rather than "sure".
        if (addressed && ports.Level is { } level && AutonomousBotChatCoordinator.BandReply(level, message, random) is { } band &&
            AutonomousBotChatCoordinator.AsksToJoinGroup(message))
            return new ReplyChoice(ports.Styled(band), "short (band)");
        if (outcome == AutonomousChatReplyBudget.Outcome.Ack)
        {
            if (AutonomousChatReplyBudget.ToContent(moment, random) is { } content)
                return new ReplyChoice(ports.Styled(content), "short (" + choice.Route + ")");
            // #478: "here, whats up?" answers an opener with nothing in it; a line that says or asks something gets what the bot
            // knows of itself, or silence, never a bare "?".
            if (AutonomousChatReplyBudget.ContentFree(message))
                return new ReplyChoice(AutonomousChatReplyBudget.Ack(ports.BotId, moment.Busy, random), "ack (" + choice.Route + ")");
            return FromWhatItKnows(ports, message) is { } known
                ? new ReplyChoice(known, "known (" + choice.Route + ")")
                : new ReplyChoice(null, "silent (nothing true to say: " + choice.Route + ")");
        }
        return AutonomousChatReplyBudget.Rescue(moment, choice.Route, random) is { } rescue
            ? new ReplyChoice(ports.Styled(rescue), "rescue (" + choice.Route + ")")
            : new ReplyChoice(null, "silent (" + choice.Route + ")");
    }
}
