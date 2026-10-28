using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// What bots said lately, by channel, so a player's next line can be read as part of a conversation (the talking update).
/// Before it every line stood alone: a player who answered a bot's question got no "ty", a "why?" after a bot's line drew
/// nothing, and a follow-up by tell got "sup". Pure apart from the clock the caller passes in; unit-tested.
/// </summary>
public sealed class ChatThreadLog
{
    public sealed record Said(string Channel, long BotId, string BotName, string Text, string Invites, long At)
    {
        /// <summary>The player the line answered, if it was a reply to one (so their "ty" goes to this bot).</summary>
        public string To { get; init; }

        /// <summary>The kind of line it was ("ask", "boast", "news", "chat"...), for follow-ups that fit it.</summary>
        public string Family => AutonomousBotVoiceCorpus.InviteFamily(Invites);
        public bool Asked => Invites != "none" && (Family == "ask" || Text.TrimEnd().EndsWith('?'));

        /// <summary>A line nothing follows on from: a combat call ("add, add"), a join hello.</summary>
        public bool Closed => Invites == "none";
    }

    public const int PerChannel = 8;
    private readonly Dictionary<string, List<Said>> _byChannel = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Said> _byBot = new();
    private readonly HashSet<Said> _closed = new();

    public void Record(string channel, long botId, string botName, string text, string invites, long now, string to = null)
    {
        if (string.IsNullOrEmpty(channel) || string.IsNullOrWhiteSpace(text))
            return;
        var said = new Said(channel, botId, botName, text.Trim(), invites ?? string.Empty, now) { To = to };
        lock (_byChannel)
        {
            if (!_byChannel.TryGetValue(channel, out var list))
                _byChannel[channel] = list = new List<Said>();
            list.Add(said);
            if (list.Count > PerChannel)
                list.RemoveAt(0);
            if (botId > 0)
                _byBot[botId] = said;
            if (_closed.Count > 200)
                _closed.Clear();
        }
    }

    /// <summary>The last bot line in the channel within the time, or null.</summary>
    public Said Last(string channel, long now, long withinMs)
    {
        lock (_byChannel)
            return _byChannel.TryGetValue(channel, out var list) && list.Count > 0 && now - list[^1].At <= withinMs ? list[^1] : null;
    }

    /// <summary>The last line this bot said anywhere within the time, or null.</summary>
    public Said LastBy(long botId, long now, long withinMs)
    {
        lock (_byChannel)
            return _byBot.TryGetValue(botId, out Said said) && now - said.At <= withinMs ? said : null;
    }

    /// <summary>
    /// The question a player's line may be answering: the last line in the channel, if it asked something within the time
    /// and nobody has answered it yet (an answer closes it).
    /// </summary>
    public Said OpenQuestion(string channel, long now, long withinMs)
    {
        Said last = Last(channel, now, withinMs);
        lock (_byChannel)
            return last != null && last.Asked && !_closed.Contains(last) ? last : null;
    }

    /// <summary>The last line in the channel within the time that answered this player, or null.</summary>
    public Said LastTo(string channel, string player, long now, long withinMs)
    {
        lock (_byChannel)
        {
            if (!_byChannel.TryGetValue(channel, out var list))
                return null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (now - list[i].At > withinMs)
                    return null;
                if (string.Equals(list[i].To, player, StringComparison.OrdinalIgnoreCase))
                    return list[i];
            }
            return null;
        }
    }

    public void Close(Said said)
    {
        if (said == null)
            return;
        lock (_byChannel)
            _closed.Add(said);
    }
}

public static class AutonomousChatThreads
{
    public static readonly ChatThreadLog Log = new();

    /// <summary>How long a bot's line stays the one a player's "why?" or answer is about.</summary>
    public const long FollowUpWindowMs = 90_000;
    public const long AnswerWindowMs = 75_000;
    public const long TellMemoryMs = 4 * 60_000;

    public static string Faction(eRealm realm) => "faction:" + realm;
    public static string Say(ushort region) => "say:" + region;
    public static string Tell(string player, string bot) => "tell:" + player + "|" + bot;
    public static string GroupChannel(object group) => group == null ? null : "group:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(group);

    // The talking update audit (2026-10-26) widened these: "which bridge", "what kind of adds", "what for?", "crazy how",
    // "nice how long did that take u", "the one by the inn?", "for u too?" were read as new lines and got nothing.
    private static readonly (Regex Pattern, string Word)[] FollowUps =
    [
        (new Regex(@"^(why|why\s?not|how come|whys that|why is that|why tho|why though|how so|what for|for what|y what happened|y tho|y not|(y|why) (there|that|that one|him|her|them|now|so|then|that way))$"), "why"),
        (new Regex(@"^(really|rly|srsly|seriously|for real|no way|are you sure|u sure|you sure|honestly|truly)$"), "really"),
        (new Regex(@"^(how long|how long did (that|it) take( (u|you))?|how long did u take|how much longer|how long was that|how long (u|you|have you|have u) (been )?(here|there|at it|doing that|camping|at this))$"), "howlong"),
        (new Regex(@"^(how|how do you mean|how do u mean|how do you do that|how did you|how'd you|hows that work|how does that work|\w+ how|how so)$"), "how"),
        // "how many?" after "zerg at the mile gate" is a count, not a time (review 2026-10-26).
        (new Regex(@"^(how many|how many of them|how many were there|how big|how high|how many mobs|how many adds)$"), "howmany"),
        // "o ya what stats", "any good?" about something the bot found (the holdout check).
        (new Regex(@"^((find|get|got|see) (anything|any|something) (good|nice|cool|decent)|what did (it|u|you) (get|drop)|what dropped|what drop|(what stats|what bonuses|what are the stats|whats it do|what does it do|any good|good stats|how good|is it good|what con|what color|what kind of stats|stats))$"), "detail"),
        // "into what" after "just respecced", "what was it" after "had a nice drop": the bot says a little more, not "nvm".
        (new Regex(@"^((into|from|with|to|about|on) (what|which)|what was it|what was that|what did u (do|get|pick)|what did you (do|get|pick))$"), "specific"),
        (new Regex(@"^(which|which one|which ones|what one|which \w+|what kind|what kind of \w+|what sort of \w+|what \w+ exactly)$"), "which"),
        // "whered they go", "which way did they head": where something went, not where the bot is.
        (new Regex(@"^(where(d| did| have)? (they|he|she|it|them|the \w+|those \w+) (go|went|gone|head|headed|run|ran|move|moved)( to)?|where (are|r) they( now| headed| heading| going)?|which way (did )?(they|he|she|it) (go|went|head|headed))$"), "whereto"),
        (new Regex(@"^(when|what time|when tho|when is that|when though|when is it)$"), "when"),
        (new Regex(@"^(what|wut|wat|huh|what do you mean|what do u mean|wdym|meaning|what about it|what is that|whats that|what happened|better than what|than what)$"), "what"),
        (new Regex(@"^(who|who was it|who did|who said|who is that|whos that|who was that|who made it|who made that|who has it|who \w+ (it|that|them|him|her))$"), "who"),
        (new Regex(@"^(where|where at|where is that|wheres that|where exactly|where is it|wheres it|where abouts|whereabouts)$"), "where"),
    ];

    private static readonly Regex Interjection = new(@"^(nice|oh|ooh|wow|lol|ok|okay|so|hmm|ah|ahh|wait|heh|haha|cool|damn|dang|geez|o ya|oh ya|oh yeah|o yeah|o rly|o|ya|yeah|really|rly|only|seriously|srsly) ");
    private static readonly Regex QuestionWord = new(@"^(is|are|was|were|can|could|do|does|did|will|would|should|how|what|whats|where|when|which|who|why|r|any|anyone|anybody|hi|hey)\b");

    /// <summary>
    /// The message with a name taken out ("Aldwin why?" -> "why?", "why tho aldwin" -> "why tho"), for a follow-up said to a
    /// bot by name. Pure.
    /// </summary>
    public static string WithoutName(string message, string name)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(name))
            return message;
        string result = Regex.Replace(message, $@"(?<![A-Za-z]){Regex.Escape(name)}(?![A-Za-z])[,:]?", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    /// <summary>A laugh reply the joker itself can say ("heh, it happens", ":)"), not one that praises a joke ("good one"). Pure.</summary>
    public static bool FitsOwnJoke(string reply) =>
        !Regex.IsMatch(reply ?? string.Empty, @"good one|got me|\bnice\b|oh no|rofl|lolol|hahaha|ikr|lol right", RegexOptions.IgnoreCase);

    /// <summary>How long after a bot answered a player their "ty" is still for that bot, and a laugh for a bot's joke.</summary>
    public const long ThanksWindowMs = 90_000;
    public const long LaughWindowMs = 45_000;

    /// <summary>
    /// "why?", "rly?", "how so", "which bridge", "what for?", "the one by the inn?": a short follow-up to the last thing said, or
    /// null. "y" is "y": why after a remark, yes after a question (the caller knows which). A short line ending in "?" that is
    /// no question of its own ("second gate?", "for u too?", "now?") is "confirm".
    /// </summary>
    public static string FollowUpWord(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        string text = Regex.Replace(message.ToLowerInvariant().Replace("'", ""), @"[^a-z ]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text is "y" or "y y")
            return "y";
        // "only 3? lol where", "o ya what stats", "really? whered they go" (the holdout check): several in a row.
        for (int i = 0; i < 3 && Interjection.IsMatch(text); i++)
            text = Interjection.Replace(text, "");
        text = Regex.Replace(text, @" (lol|heh|haha|lmao|tho|though)$", "");
        if (text.Length == 0 || text.Split(' ').Length > 6)
            return null;
        foreach ((Regex pattern, string word) in FollowUps)
            if (pattern.IsMatch(text))
                return word;
        // "lfg?", "inv?", "rez?" are requests of their own, not a check on what was said.
        if (message.TrimEnd().EndsWith('?') && text.Split(' ').Length <= 5 && !QuestionWord.IsMatch(text) &&
            AutonomousChatIntentModel.Predict(message) is eAutonomousChatIntent.Unknown or eAutonomousChatIntent.Dungeon or
                eAutonomousChatIntent.RvR or eAutonomousChatIntent.Travel or eAutonomousChatIntent.Grinding or eAutonomousChatIntent.Banter)
            return "confirm";
        return null;
    }

    /// <summary>
    /// Whether a player's line reads as an answer to a bot's question: not a question itself, not a greeting, laugh or
    /// goodbye, and with some words in it ("try spindel", "no idea sorry", "yeah im in").
    /// </summary>
    public static bool ReadsAsAnswer(string message, eAutonomousChatIntent intent)
    {
        if (string.IsNullOrWhiteSpace(message) || FollowUpWord(message) is { } word && word != "y")
            return false;
        // "i might, what lvl" answers with a question of its own; a plain question is not an answer.
        if (message.Contains('?') && !Regex.IsMatch(message, @"^\s*(yes|yeah|yep|ya|no|nope|nah|maybe|i might|sure|prob|probably)\b", RegexOptions.IgnoreCase))
            return false;
        if (intent is eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Laugh or
            eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or eAutonomousChatIntent.Abuse or eAutonomousChatIntent.IgnoreOutOfWorld or
            eAutonomousChatIntent.Ding or eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Rez)
            return false;
        // A player's own lfg, sale or alarm is not an answer (review 2026-10-26: "lfg healer for tom" drew "ty"); "yep forming
        // at the portal keep" is (the audit), so the words decide, not the intent.
        if (Regex.IsMatch(message, @"\b(lfg|lfm|lf\dm|wts|wtb|inc)\b", RegexOptions.IgnoreCase))
            return false;
        // "32" to "what level are you?" is an answer (the audit).
        return Regex.Matches(message, @"[A-Za-z]{2,}|\d+|\b[ykn]\b", RegexOptions.IgnoreCase).Count >= 1;
    }

    private static readonly HashSet<string> ConfirmFiller = new(StringComparer.Ordinal)
    {
        "the", "that", "this", "those", "one", "you", "for", "too", "now", "then", "there", "here", "and", "with", "are", "was", "yet",
        "lol", "still", "just", "really",
    };

    /// <summary>
    /// Whether a short "...?" checks the bot's own line ("second gate?" after "meet at the gate", "the one by the inn?" after
    /// "try the horse from cotswold"): it shares a word with that line or points at something in it. Not "50 yet?" or "mids?"
    /// after an unrelated line, which a "yep" would answer falsely (review 2026-10-26). Pure.
    /// </summary>
    public static bool ConfirmFits(string message, string botLine)
    {
        string said = AutonomousChatQuestions.Normalize(message);
        if (AutonomousChatQuestions.SelfQuestion(said) != null || Regex.IsMatch(said, @"\d|\b(lvl|level|rr|rank|yet)\b"))
            return false;
        if (Regex.IsMatch(said, @"^(the|that|this|those|which|second|first|third|next|last|other|by|near|at|in|on|past|behind|left|right|north|south|east|west) "))
            return true;
        var theirs = new HashSet<string>(AutonomousChatQuestions.Normalize(botLine).Split(' ').Where(word => word.Length >= 3 && !ConfirmFiller.Contains(word)),
            StringComparer.Ordinal);
        return said.Split(' ').Any(word => word.Length >= 3 && !ConfirmFiller.Contains(word) && (theirs.Contains(word) ||
            theirs.Any(other => other.Length >= 4 && word.Length >= 4 && (other.StartsWith(word, StringComparison.Ordinal) || word.StartsWith(other, StringComparison.Ordinal)))));
    }

    /// <summary>"gz on the ding!", "np", "yw anytime": a bot line that a "ty" closes, with nothing more to say. Pure.</summary>
    public static bool EndsAnExchange(string line) =>
        AutonomousChatIntentModel.Predict(line) is eAutonomousChatIntent.Congrats or eAutonomousChatIntent.Thanks ||
        Regex.IsMatch((line ?? string.Empty).ToLowerInvariant(), @"^\W*(np|yw|no problem|no prob|anytime|any time|you re welcome|youre welcome|your welcome|no worries)\b");

    /// <summary>"lfm vendo need a tank pst", "1 more for spindel": a bot's call for players to join.</summary>
    public static bool CallsForGroup(string line) =>
        Regex.IsMatch((line ?? string.Empty).ToLowerInvariant(),
            @"\b(lfm|lf\dm|lf \d ?more|need (a |one |1 )?(tank|healer|puller|cc|mezzer|caster|nuker|dps|more)|room for (one|1|2|two|a)|spots? (left|open)|(one|1) more for|need (one|1) more|anyone want to (group|join|come|go|run|do|duo)|anyone wanna (group|join|come|go|run|do|duo)|want to join|want to come|wanna come|come with)\b");

    private static readonly Regex Joins = new(@"^(x|xx|me|me pls|me plz|me please|x me|x pls|inv|inv pls|inv plz|inv me|inv me pls|im in|i m in|count me in|ill come|i ll come|i can come|i can|me me|i ll join|ill join|i want in|sign me up)$");

    /// <summary>"x", "me", "inv pls" (or "/x"): a player answering a call for a group. Pure.</summary>
    public static bool JoinsCall(string message)
    {
        string text = Regex.Replace((message ?? string.Empty).ToLowerInvariant().Replace("'", " "), @"[^a-z ]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 0 && Joins.IsMatch(text);
    }

    private static readonly Regex Yes = new(@"^(sure|yes|yeah|yea|ya|yep|yup|ok|okay|k|kk|ok sure|sure thing|why not|sounds good|lets go|let's go|lets do it|let's do it|definitely|absolutely|of course|ofc|inv|invite|inv me|np|sure why not|go for it|im in|i'm in|deal|sweet|cool)\b");
    private static readonly Regex No = new(@"^(no|nah|nope|no thanks|no thx|no ty|not now|not right now|maybe later|later|im good|i'm good|im ok|i'm ok|pass|cant|can't|sorry|busy|already grouped|im grouped|i'm grouped|nvm|never mind|nevermind|all good|figured it out|got it now|dont need|don't need|do not need|already (released|got|have|did|done))\b");

    /// <summary>"sure", "yeah inv me", "ok": a yes to an offer. "nah", "maybe later": a no. Null when neither.</summary>
    public static bool? SaysYes(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        string text = Regex.Replace(message.ToLowerInvariant(), @"[^a-z' ]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (Regex.IsMatch(text, @"^(no problem|no prob|no worries)\b"))
            return true;
        if (No.IsMatch(text))
            return false;
        return Yes.IsMatch(text) ? true : null;
    }

    /// <summary>
    /// The closing sets for a player's answer to a bot's question, best first (the talking update audit: "ya go" to "ready to
    /// pull?" drew "good to know", "since beta lol" to "how long you been playing?" drew "will do"): a yes gets "ask_yes"
    /// ("cool", "k, on it"), a no "ask_no" ("fair enough"), an answer about the player "ask_personal" ("oh cool"), then the sets
    /// the question's kind closes ("ask_where", "ask").
    /// </summary>
    public static string[] ClosingSets(ChatThreadLog.Said question, string answer)
    {
        var sets = new List<string>();
        bool? yes = SaysYes(answer);
        if (yes == true)
            sets.Add("ask_yes");
        else if (yes == false)
            sets.Add("ask_no");
        string[] kind = ClosingAnswers(question?.Invites);
        if (kind.Length > 1)
            sets.Add(kind[0]);
        if (yes == null && Regex.IsMatch(AutonomousChatQuestions.Normalize(question?.Text), @"\b(you|u|ur|your|ya|youre)\b"))
            sets.Add("ask_personal");
        sets.AddRange(kind);
        if (yes == false)
            sets.Add("lfg");
        return sets.Distinct().ToArray();
    }

    /// <summary>The closing sets that fit the question asked, most specific first ("ask_where" then "ask").</summary>
    public static string[] ClosingAnswers(string invites)
    {
        string family = AutonomousBotVoiceCorpus.InviteFamily(invites);
        if (string.IsNullOrEmpty(invites))
            return ["ask"];
        return invites == family ? [invites] : [invites, family];
    }
}
