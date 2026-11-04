using System;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The reply budget (Beta 1.1.3, docs/bot-voices/CHAT-BUDGETS.md): a bot answers a player only with a reply that fits the
/// line (AutonomousChatReplyFit at least chat-budget.json's "min_fit"). Below it the bot says nothing, unless the line was
/// a tell to it or named it, which gets a short human acknowledgement ("?", "ya?", "sec"), never a stock brush-off. The
/// first blind evaluation found silence read more human than the weak replies (3.99 against 3.30 of 6). This budget is
/// only for replies to the player: the world's own chatter has the ambient budget. Pure.
/// </summary>
public static class AutonomousChatReplyBudget
{
    public enum Outcome { Answer, Ack, Silent }

    /// <summary>What a reply of this fit becomes: said, an acknowledgement (to a tell or a line naming the bot), or silence.</summary>
    public static Outcome Decide(int fit, bool addressed, int minFit) =>
        fit >= minFit ? Outcome.Answer : addressed ? Outcome.Ack : Outcome.Silent;

    private static readonly string[] Acks = ["?", "ya?", "hm?", "yes?", "ya", "whats up?", "yo", "hmm?", "yeah?", "sup?"];
    private static readonly string[] BusyAcks = ["sec", "1 sec", "sec, fighting", "brt", "one sec"];

    /// <summary>
    /// A short acknowledgement. A bot leans on two or three of its own (fixed from its id), as people do; a busy bot says
    /// "sec". Pure.
    /// </summary>
    public static string Ack(long botId, bool busy, Random random)
    {
        if (busy)
            return BusyAcks[random.Next(BusyAcks.Length)];
        int own = (int)(AutonomousBotPersonality.Mix(botId, 53) % (ulong)Acks.Length);
        return random.NextDouble() < 0.7 ? Acks[(own + random.Next(3)) % Acks.Length] : Acks[random.Next(Acks.Length)];
    }

    /// <summary>
    /// A stock brush-off ("ask around maybe", "beats me", "i think so?", "good question"): the blind judge's most common
    /// complaint when said to a question anyone could answer. Pure.
    /// </summary>
    public static bool IsBrushOff(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = AutonomousChatQuestions.Normalize(text);
        // A style's emote at the end (":D", "heh") is no part of what is said.
        foreach (string tail in new[] { " d", " p", " lol", " heh", " haha" })
            if (t.EndsWith(tail, StringComparison.Ordinal))
                t = t[..^tail.Length];
        return t is "beats me" or "no idea" or "no idea sorry" or "not sure" or "dunno" or "no clue" or "good question" or "i think so" or
                   "hmm not sure" or "wish i knew" or "couldnt tell you" or "not a clue honestly" or "pretty sure yes but dont quote me" ||
               t.StartsWith("ask around", StringComparison.Ordinal) || t.Contains("someone said something about that") ||
               t.Contains("i was wondering that too") || t.StartsWith("no idea i just got here", StringComparison.Ordinal) ||
               t.StartsWith("cant help there", StringComparison.Ordinal) || t.StartsWith("maybe ask a", StringComparison.Ordinal) ||
               t.StartsWith("try asking a", StringComparison.Ordinal) || t.StartsWith("someone usually answers", StringComparison.Ordinal);
    }

    #region Second pass (blind round 2): fewer silences, fitting short replies

    /// <summary>What the budget knows of a reply's moment.</summary>
    /// <param name="Channel">faction, say, group, guild, tell, broadcast, chatgroup or advice.</param>
    /// <param name="LastBotLine">The bot's own last line in this conversation, if any (its question the player may be answering).</param>
    public sealed record Moment(string Message, string Channel, bool Addressed, eAutonomousChatIntent Intent, string ClassName, bool CanHeal,
        bool CanRez, bool Busy, string LastBotLine = null);

    /// <summary>The share of weak, readable social lines a channel still answers with a short reaction ("lol", "ugh", "same").</summary>
    public static double ReactionShare(string channel) => channel switch
    {
        "group" => 0.75,
        "guild" => 0.55,
        "say" => 0.45,
        "faction" => 0.3,
        _ => 0.0,
    };

    /// <summary>The share of unanswered questions that one bot still answers "dunno" in a channel.</summary>
    public static double DunnoShare(string channel) => channel switch
    {
        "group" => 0.9,
        "guild" => 0.65,
        "say" => 0.5,
        "faction" => 0.3,
        _ => 0.0,
    };

    /// <summary>A line with nothing in it to answer but "are you there": "hi", "hey u there?", "you around?", "?", "Brannoc". Pure.</summary>
    public static bool ContentFree(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;
        string[] words = AutonomousChatIntentModel.Words(message);
        if (words.Length == 0)
            return true;
        if (AutonomousChatShortTalk.AsksWhoIsOn(message) || AutonomousChatShortTalk.HasPhrase(words, "u there", "you there", "r u there", "are you there",
                "u around", "you around", "r u around", "u busy", "you busy", "got a sec", "got a min", "u on", "you on"))
            return words.Length <= 6;
        return words.Length <= 3 && words.All(word => word is "hi" or "hey" or "heya" or "hiya" or "hello" or "yo" or "sup" or "oi" or "psst" or "hail" or
            "there" or "u" or "you" or "ya" or "man" or "dude" or "buddy" or "again");
    }

    private static string Pick(Random random, params string[] options) => options[random.Next(options.Length)];

    private static readonly System.Text.RegularExpressions.Regex AskedInside = new(
        @" (where is|where s|where are|where r|where do|where can|how do|how does|how can|how much|how many|what is|what s|what are|whats|which|who sells|who has|anyone know|any1 know|does anyone|do u know|do you know|is there) ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A question, also one with no "?" and a lead-in ("sry to bother but where is the healer trainer"). Pure.</summary>
    public static bool AsksSomething(string message) =>
        AutonomousChatQuestions.IsQuestion(message) || AskedInside.IsMatch(" " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ");

    /// <summary>A brb or afk line ("brb dinner, can someone hold my spot?"): its own rules answer it, never with "dunno". Pure.</summary>
    private static bool StepsAway(string message) =>
        AutonomousChatIntentModel.Words(message ?? string.Empty).Any(word => word is "brb" or "afk" or "bbl" or "bio" or "bbiab" or "bbs");

    /// <summary>
    /// A short reply to a tell (or a line naming the bot) that says something, instead of a bare "ya?": a compliment gets thanks,
    /// an offer to buy a no, a request for heals or a rez the truth for the bot's class, an offer of help a polite no, a question
    /// "no clue sry", an answer to the bot's own question "cool". Null for a content-free opener (the "?" stays). Pure.
    /// </summary>
    public static string ToContent(Moment moment, Random random)
    {
        string message = moment.Message ?? string.Empty;
        string t = " " + AutonomousChatQuestions.Normalize(message) + " ";
        string cls = (moment.ClassName ?? "adventurer").ToLowerInvariant();
        bool question = AsksSomething(message);
        // The player answers the bot's own question: no "hm?" (blind round 2).
        if (!string.IsNullOrEmpty(moment.LastBotLine) && AutonomousChatQuestions.IsQuestion(moment.LastBotLine) && !question)
            return Pick(random, "cool", "k cool", "ah ok", "nice", "k", "ok ty", "gotcha");
        if (ContentFree(message))
            return null;
        // A social line in a tell gets its social answer.
        switch (moment.Intent)
        {
            case eAutonomousChatIntent.Greeting:
                return Pick(random, "hey!", "heya", "hey :)", "hi", "hey, been a while");
            case eAutonomousChatIntent.Thanks:
                return Pick(random, "np", "np :)", "anytime", "np, enjoy");
            case eAutonomousChatIntent.Back:
                return Pick(random, "wb", "wb :)", "welcome back");
            case eAutonomousChatIntent.Farewell:
                return Pick(random, "cya", "later", "gn", "take care");
            case eAutonomousChatIntent.Laugh:
                return Pick(random, "lol", "heh", "haha");
            case eAutonomousChatIntent.Congrats or eAutonomousChatIntent.Ding:
                return Pick(random, "gz", "grats", "ty!");
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (nice|good|great|awesome|sweet) (job|work|tanking|heals|healing|pull|pulls|one|hat|armor|cloak|sword|run|fight|kill)| gj | well done | ur (good|great|awesome)| you re (good|great|awesome)| thanks for | ty for | thx for | appreciate "))
            return System.Text.RegularExpressions.Regex.IsMatch(t, @" (thanks|ty|thx) for | appreciate ") ? Pick(random, "np", "anytime", "np :)", "np, gl") : Pick(random, "ty", "thx :)", "heh ty", "ty ty", "thanks");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (will|would) (u|you) take | how much for (ur|your|that|the) | (u|you) selling | sell me | wtb (ur|your) | buy (ur|your|that) | \d+ ?(g|gold|p|plat|s) for (ur|your|that|the) "))
            return Pick(random, "not selling sry", "not for sale sry", "nah, using it", "sry, not selling it");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (want|need|u want|you want) (a )?(res|rez|heal|heals|buff|buffs|help|hand|port|speed) | (want|need) anything | can i help "))
            return Pick(random, "nah im good ty", "im ok ty", "all good ty", "nah ty");
        bool heal = System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|healing|heal me|got heal|have heal) ");
        bool rez = System.Text.RegularExpressions.Regex.IsMatch(t, @" (rez|res|rezz|ress|resurrect) ");
        bool buff = System.Text.RegularExpressions.Regex.IsMatch(t, @" (buff|buffs|speed) ");
        if (rez)
            return moment.CanRez ? Pick(random, "where r u?", "where's ur body?", "where u at?") : Pick(random, $"no rez on a {cls} sry", $"{cls}, cant rez sry");
        if (heal)
            return moment.CanHeal ? Pick(random, "where r u?", "where u at?", "omw, where?") : Pick(random, $"{cls}, no heals sry", $"no heals on a {cls} sry");
        if (buff && question)
            return Pick(random, $"no buffs, im a {cls}", $"{cls}, sry no buffs", "nothing worth having sry");
        if (question)
            return Pick(random, "no clue sry", "dunno sry", "not sure, try /advice", "no idea tbh", "hmm no clue");
        if (moment.Intent == eAutonomousChatIntent.Abuse)
            return Pick(random, "ok", "k", "lol ok", "whatever");
        return Pick(random, "heh", "ya", "lol", "k", "oh ok", "ah");
    }

    /// <summary>The kind of short reaction a weak social line gets: "laugh", "sympathy", "banter", or null for none. Pure.</summary>
    public static string ReactionKind(string message, string route, eAutonomousChatIntent intent)
    {
        string[] words = AutonomousChatIntentModel.Words(message ?? string.Empty);
        bool sad = AutonomousChatShortTalk.HasPhrase(words, "ugh", "sucks", "died", "dead", "hate", "lost", "broke", "broken", "crashed", "crash", "bad day",
                       "wipe", "wiped", "sigh", "argh", "tired", "worst", "nerfed", "ganked", "fml", "lag", "laggy", "sad", "miss", "rip") ||
                   route is "shape complaint" or "shape player_sorry" || (route?.StartsWith("shape player_rl", StringComparison.Ordinal) ?? false);
        if (sad)
            return "sympathy";
        if (intent == eAutonomousChatIntent.Laugh || route is "shape player_mishap" or "intent laugh" || words.Any(word => word is "lol" or "haha" or "rofl" or "lmao" or "heh"))
            return "laugh";
        if (intent is eAutonomousChatIntent.Banter or eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR or eAutonomousChatIntent.ClassTalk ||
            route is "intent banter" or "intent rvr" or "intent dungeon" or "intent classtalk")
            return "banter";
        return null;
    }

    /// <summary>A short reaction of a kind. Pure.</summary>
    public static string Reaction(string kind, Random random) => kind switch
    {
        "sympathy" => Pick(random, "ugh", "ouch", "that sucks", "rip", "oof", "been there", "same here", "ugh, same"),
        "laugh" => Pick(random, "lol", "heh", "haha", "lol nice", "hehe", "rofl"),
        _ => Pick(random, "lol", "heh", "same", "ha", "lol yeah", "heh yeah", "lol same"),
    };

    /// <summary>
    /// What a weak reply to a line not said to the bot becomes (blind round 2: silence went from 38% to 64% of turns, and most
    /// of what was lost was short banter): a short reaction for a social line, at a rate by channel; a "dunno" from one bot for
    /// a question in a group or guild; otherwise nothing. Null means silence. Pure.
    /// </summary>
    public static string Rescue(Moment moment, string route, Random random)
    {
        if (route is "none ack" or "none spam" or "none other" or "none undirected" or "none open" ||
            moment.Intent is eAutonomousChatIntent.Abuse or eAutonomousChatIntent.IgnoreOutOfWorld)
            return null;
        string message = moment.Message ?? string.Empty;
        string t = " " + AutonomousChatQuestions.Normalize(message) + " ";
        // A ding or good news gets a "gz", not a laugh ("dinged 30 and got a respec, now what spec lol").
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (ding|dinged|grats|gz) ") || route == "shape player_goodnews")
            return random.NextDouble() < ReactionShare(moment.Channel) * 1.3 ? Pick(random, "gz", "grats", "gz!", "nice, gz") : null;
        // A request for heals, a rez or buffs: the truth for the bot's class, not "dunno".
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|rez|res|buff|buffs|speed|power|pow|mana) ") &&
            System.Text.RegularExpressions.Regex.IsMatch(t, @" (can|could|pls|plz|please|need|someone|anyone|any1|get) "))
            return random.NextDouble() < DunnoShare(moment.Channel) ? ToContent(moment with { Intent = eAutonomousChatIntent.Unknown, LastBotLine = null }, random) : null;
        bool question = AsksSomething(message) && !StepsAway(message);
        // Group posts, trades, hellos, goodbyes and brbs aren't "dunno" questions; their own rules answer them.
        if (question && moment.Intent is not (eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Trading or eAutonomousChatIntent.Away or
                eAutonomousChatIntent.Back or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Thanks or
                eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm))
            return random.NextDouble() < DunnoShare(moment.Channel) ? Pick(random, "dunno", "no idea", "not sure tbh", "idk sry", "no clue, sry", "hmm dunno") : null;
        if (question)
            return null;
        if (moment.Intent == eAutonomousChatIntent.Unknown && route is "none unknown" &&
            !AutonomousChatIntentModel.Words(message).Any(word => word is "lol" or "haha" or "heh" or "ugh" or "sigh"))
            return null;
        string kind = ReactionKind(message, route, moment.Intent);
        return kind != null && random.NextDouble() < ReactionShare(moment.Channel) ? Reaction(kind, random) : null;
    }

    #endregion
}
