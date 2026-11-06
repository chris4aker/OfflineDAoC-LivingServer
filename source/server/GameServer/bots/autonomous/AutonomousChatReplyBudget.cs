using System;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The reply budget (Beta 1.1.2, docs/bot-voices/CHAT-BUDGETS.md): a bot answers a player only with a reply that fits the
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
    /// <param name="Self">What is true of the bot (level, class, spec, weapon), for a question about it (round 3).</param>
    public sealed record Moment(string Message, string Channel, bool Addressed, eAutonomousChatIntent Intent, string ClassName, bool CanHeal,
        bool CanRez, bool Busy, string LastBotLine = null, AutonomousChatQuestions.BotFacts Self = null);

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
        // A scam is never answered, not even with "oh ok" (round 3).
        if (AutonomousChatScreen.IsSpam(message))
            return null;
        bool question = AsksSomething(message);
        // The player answers the bot's own question: no "hm?" (blind round 2).
        if (!string.IsNullOrEmpty(moment.LastBotLine) && AutonomousChatQuestions.IsQuestion(moment.LastBotLine) && !question)
            return Pick(random, "cool", "k cool", "ah ok", "nice", "k", "ok ty", "gotcha");
        if (ContentFree(message))
            return null;
        // A question about the bot is answered from what is true of it: "spec?", "what weapon is that", "how old r u" (round 3:
        // "hmm no clue" to a question about the bot's own spec was a fail).
        if (moment.Self != null && AutonomousChatQuestions.AddressedSelfQuestion(message) is { } self &&
            AutonomousChatQuestions.AnswerSelf(self, message, moment.Self, random) is { Length: > 0 } known)
            return known;
        // Thanks for heals or a rez the bot can't give isn't its to take (round 3: a thane said "np" to "ty for the heals").
        if (CreditFor(message) is { } credit && !CanGive(credit, moment))
            return Pick(random, $"wasnt me lol, im a {cls}", $"not me, {cls} here", $"lol not me, no {credit} on a {cls}");
        // A raid or group above or below the bot's level band: it says so, it doesn't join (round 3).
        if (moment.Self != null && AutonomousChatShortTalk.LevelBand(message) is { } band && (moment.Self.Level < band.Min || moment.Self.Level > band.Max))
            return moment.Self.Level < band.Min
                ? Pick(random, "too low lol", $"im only {moment.Self.Level}", $"{moment.Self.Level} here, too low", "too low for that sry")
                : Pick(random, $"too high, im {moment.Self.Level}", "im too high for that", $"{moment.Self.Level} lol, too high");
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
        // An offer to sell the bot something gets a price question or a no, never "ah" (round 3).
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (wanna|want to|u want to|you want to|want|interested in|need) (buy|a|my|some|an) .*(\d+ ?%|sword|axe|hammer|staff|bow|armor|armour|chain|plate|cloak|ring|helm|gloves|boots|legs|sleeves|vest|shield|pots|potions|gem|for sale|cheap|\d+ ?(g|p|s|gold|plat)) | wts .* (u|you) (want|interested|need) | (selling|wts) .* (to|for) (u|you) "))
            return Pick(random, "how much?", "what %?", "nah ty", "not buying sry", "maybe, how much", "nah im good");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (nice|good|great|awesome|sweet) (job|work|tanking|heals|healing|pull|pulls|one|hat|armor|cloak|sword|run|fight|kill)| gj | well done | ur (good|great|awesome)| you re (good|great|awesome)| thanks for | ty for | thx for | appreciate "))
            return System.Text.RegularExpressions.Regex.IsMatch(t, @" (thanks|ty|thx) for | appreciate ") ? Pick(random, "np", "anytime", "np :)", "np, gl") : Pick(random, "ty", "thx :)", "heh ty", "ty ty", "thanks");
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (will|would) (u|you) take | how much for (ur|your|that|the) | (u|you) selling | sell me | wtb (ur|your) | buy (ur|your|that) | \d+ ?(g|gold|p|plat|s) for (ur|your|that|the) "))
            return Pick(random, "not selling sry", "not for sale sry", "nah, using it", "sry, not selling it");
        if (OffersHelp(message))
            return Pick(random, "nah im good ty", "im ok ty", "all good ty", "nah ty");
        bool heal = System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|healing|heal me|got heal|have heal|healz) ");
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

    /// <summary>"want a rez?", "u need heals?", "need anything?": the player offers the bot something (not a request). Pure on a padded line.</summary>
    private static bool OffersHelp(string message)
    {
        string t = " " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (u|you|ya|ye) (want|need|wanna) (a |some |any )?(res|rez|heal|heals|buff|buffs|help|hand|port|speed|pot|pots) | (want|need) anything | can i help | i can (rez|res|heal|buff|port) (u|you) "))
            return true;
        // "want a rez?", "need heals?": an offer only with its question mark ("need heals" alone is a request).
        return (message ?? string.Empty).TrimEnd().EndsWith('?') &&
               System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (want|need|wanna) (a |some )?(res|rez|heal|heals|buff|buffs|port|speed|hand) $");
    }

    /// <summary>"ty for the heals", "thx for rez": what the line thanks for (heals, rez, buffs), or null. Pure.</summary>
    public static string CreditFor(string message)
    {
        string t = " " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ";
        if (!System.Text.RegularExpressions.Regex.IsMatch(t, @" (ty|thx|thanks|thank you|tyvm|thank u|cheers) "))
            return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (rez|res|rezz|ress|resurrect|raise) "))
            return "rez";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|healing|heal s) "))
            return "heals";
        return null;
    }

    private static bool CanGive(string credit, Moment moment) => credit switch
    {
        "rez" => moment.CanRez,
        "heals" => moment.CanHeal,
        _ => true,
    };

    /// <summary>
    /// A reply that takes credit the bot can't have: "np" or "anytime" to thanks for heals from a bot with no heals, or for a rez
    /// from one with no rez (round 3). Pure.
    /// </summary>
    public static bool ClaimsCredit(string message, string reply, bool canHeal, bool canRez)
    {
        string credit = CreditFor(message);
        if (credit == null || string.IsNullOrWhiteSpace(reply) || credit == "rez" && canRez || credit == "heals" && canHeal)
            return false;
        string r = " " + AutonomousChatQuestions.Normalize(reply) + " ";
        return System.Text.RegularExpressions.Regex.IsMatch(r, @" (np|yw|anytime|no problem|no prob|my pleasure|welcome|any time|glad to|happy to) ");
    }

    private static readonly System.Text.RegularExpressions.Regex Alarmed = new(
        @" (inc|incoming|zerg|adds|add|train|run|enemy|enemies|stealthers|stealther|ganked|gankers) | \d+ ?\+? ?(albs|mids|hibs|alb|mid|hib|enemies|stealthers) | (albs|mids|hibs) (at|in|near|on|by|hitting|at the) | (under|under) attack | (rams|ram) on ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A warning or a call: "zerg at mmg", "inc 3 albs", "adds!!", "mids hitting bled door". Pure.</summary>
    public static bool IsAlarm(string message, eAutonomousChatIntent intent) =>
        intent == eAutonomousChatIntent.Alarm || Alarmed.IsMatch(" " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ");

    /// <summary>"need heal!!", "heal pls", "rez?", "someone buff me": a request for something. Pure.</summary>
    public static bool IsRequest(string message)
    {
        string t = " " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ";
        return !OffersHelp(message) && System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|healz|rez|res|rezz|buff|buffs|speed|power|pow|mana|help) ") &&
               (System.Text.RegularExpressions.Regex.IsMatch(t, @" (can|could|pls|plz|please|need|needs|someone|anyone|any1|get|me|i) ") || (message ?? "").Contains('!'));
    }

    /// <summary>
    /// The kind of short reaction a weak line gets, by what the line is (round 3: "lol same" to an enemy zerg warning): "alarm"
    /// for a call, "request" for a request, "laugh" for a joke, "sympathy" for bad luck, "banter" for chatter, or null. Pure.
    /// </summary>
    public static string ReactionKind(string message, string route, eAutonomousChatIntent intent)
    {
        if (IsAlarm(message, intent))
            return "alarm";
        if (IsRequest(message))
            return "request";
        string[] words = AutonomousChatIntentModel.Words(message ?? string.Empty);
        bool sad = AutonomousChatShortTalk.HasPhrase(words, "ugh", "sucks", "died", "dead", "hate", "lost", "broke", "broken", "crashed", "crash", "bad day",
                       "wipe", "wiped", "sigh", "argh", "tired", "worst", "nerfed", "ganked", "fml", "lag", "laggy", "sad", "miss", "rip") ||
                   route is "shape complaint" or "shape player_sorry" || (route?.StartsWith("shape player_rl", StringComparison.Ordinal) ?? false);
        if (sad)
            return "sympathy";
        if (intent == eAutonomousChatIntent.Laugh || route is "shape player_mishap" or "intent laugh" || words.Any(word => word is "lol" or "haha" or "rofl" or "lmao" or "heh"))
            return "laugh";
        if (intent is eAutonomousChatIntent.Banter or eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.ClassTalk ||
            route is "intent banter" or "intent dungeon" or "intent classtalk")
            return "banter";
        return null;
    }

    /// <summary>The short answer to a call: "omw" when it says where, "where?" when it doesn't. Pure.</summary>
    public static string AlarmReaction(string message, Random random)
    {
        string t = " " + AutonomousChatQuestions.Normalize(message ?? string.Empty) + " ";
        bool where = System.Text.RegularExpressions.Regex.IsMatch(t, @" (at|in|near|on|by|outside|inside) \w+") ||
                     System.Text.RegularExpressions.Regex.IsMatch(t, @" (amg|hmg|mmg|mg|dock|bridge|gate|keep|relic|pk|portal|caer|dun|faste|fort|tower|milegate) ");
        return where ? Pick(random, "omw", "k", "omw", "on it", "otw") : Pick(random, "where?", "where", "where?!", "which way");
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
        // The screen runs before every reaction too (round 3).
        if (AutonomousChatScreen.IsSpam(message))
            return null;
        // A call gets "where?" or "omw", never "lol same" (round 3).
        if (IsAlarm(message, moment.Intent))
            return random.NextDouble() < DunnoShare(moment.Channel) ? AlarmReaction(message, random) : null;
        // Thanks for heals a bot can't give get nothing from it.
        if (CreditFor(message) is { } credit && !CanGive(credit, moment))
            return null;
        string t = " " + AutonomousChatQuestions.Normalize(message) + " ";
        // A ding or good news gets a "gz", not a laugh ("dinged 30 and got a respec, now what spec lol").
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (ding|dinged|grats|gz) ") || route == "shape player_goodnews")
            return random.NextDouble() < ReactionShare(moment.Channel) * 1.3 ? Pick(random, "gz", "grats", "gz!", "nice, gz") : null;
        // A request for heals, a rez or buffs: the truth for the bot's class ("cant heal sry", "use a pot"), not "dunno", and
        // never "nah im good ty" (round 3: that answered "need heal!!").
        if (IsRequest(message) && System.Text.RegularExpressions.Regex.IsMatch(t, @" (heal|heals|healz|rez|res|rezz|buff|buffs|speed|power|pow|mana) "))
            return random.NextDouble() < DunnoShare(moment.Channel) ? RequestAnswer(moment, t, random) : null;
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

    /// <summary>The truth for the bot's class to a request: a healer "omw, where?", anyone else "cant heal sry" or "use a pot". Pure.</summary>
    public static string RequestAnswer(Moment moment, string padded, Random random)
    {
        string cls = (moment.ClassName ?? "adventurer").ToLowerInvariant();
        bool rez = System.Text.RegularExpressions.Regex.IsMatch(padded, @" (rez|res|rezz) ");
        bool heal = System.Text.RegularExpressions.Regex.IsMatch(padded, @" (heal|heals|healz) ");
        if (rez)
            return moment.CanRez ? Pick(random, "where r u?", "omw, where?", "where's ur body?") : Pick(random, "cant rez sry", $"no rez on a {cls}", $"{cls}, no rez sry");
        if (heal)
            return moment.CanHeal ? Pick(random, "omw, where?", "where r u?", "on it") : Pick(random, "cant heal sry", "use a pot", $"{cls}, no heals", "pot up!");
        return Pick(random, "no buffs sry", $"{cls}, no buffs", "cant sry");
    }

    #endregion
}
