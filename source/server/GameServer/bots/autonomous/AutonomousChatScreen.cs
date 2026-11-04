using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Lines no bot should answer (Beta 1.1.2, the second blind round): spam and scams, and lines said to someone else. Checked
/// before any reply on every channel and in tells. Pure.
/// </summary>
public static class AutonomousChatScreen
{
    private static readonly Regex Url = new(
        @"(https?\s*:\s*/\s*/|www\s*\.|\bw\s+w\s+w\b|\.(com|net|org|biz|info|ru|cn|tk)\b|\b(dot|d0t)\s*(com|c0m|net|org)\b|\[\s*dot\s*\]|\(\s*dot\s*\))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Money = new(@"\$\s*\d|\d\s*\$|\b\d+\s*(usd|dollars|bucks|eur|euro)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Scam = new(
        @"\b(account (has been |was |is )?(flagged|suspended|locked|banned|compromised)|verify (your|ur) account|(your|ur) account will|reply (with|within|in) \d+ ?h|" +
        @"or (you|u) (will )?get banned|gm[_ ]?support|support[_ ]?team|game ?master here|this is (a |the )?gm\b|\[gm\]|<gm>|official (notice|message)|" +
        @"you (have )?won|u (have )?won|u won a|congratulations you won|free (item|mount|gold|plat|platinum) (for|if|when)|click (here|the link)|" +
        @"(wts|selling|sell) (my |\d+ )?(accounts?|accts?|chars? with account)|buying accounts|account for sale|" +
        @"(cheap|cheapest|fast|instant) (plat|platinum|gold|pl4t|g0ld)|(plat|platinum|gold) (for|4) (sale|cash|\$)|power ?level(ing)? service|" +
        @"ascii|" +
        // Beta 1.1.2, round 3: doubling scams, free plat, password and account requests, more fake staff.
        @"(i|ill|i ll|i will|i can) double (ur|your|it|the)|double (ur|your) (gold|plat|platinum|money|coins|cash)|send (me )?\d+ ?(g|p|gold|plat) (and|&) (i|ill|get)|" +
        @"free (plat|platinum|gold|money|coins|cash)\b(?! (for|from) (the )?(quest|drop|mob|mobs))|giving away (plat|gold|platinum|free)|" +
        @"(ur|your|my|the) (password|passwd|pass word|pw|login|log in|acct info|account info|account details|account name)|(password|passwd|login details|account details)\b|" +
        @"(buy|buying|wtb|purchase) (ur|your|my|an?|any|old) (account|acct|accounts|char|character|toon)|sell (me )?(ur|your) (account|acct|char|character|toon)|" +
        @"(i am|im|i m|this is) (a |an |the )?(gm|game master|admin|administrator|moderator|mod|staff|mythic( staff| employee)?|support)\b|" +
        @"(gm|admin|staff|support|mythic) (team|here|speaking|notice|warning)|customer (service|support)|account (review|verification|security)|" +
        @"(credit|debit) card|paypal|western union|gift card)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Words that mix letters and digits as chat always has ("lf2m", "rr4", "w00t", "ne1", "3L1").</summary>
    private static readonly HashSet<string> PeriodMixed = new(StringComparer.OrdinalIgnoreCase)
    {
        "w00t", "l8r", "ne1", "any1", "some1", "no1", "every1", "gr8", "m8", "2day", "2nite", "b4", "pl0x", "n00b", "noob", "l33t", "1337",
        "pwn", "pwned", "g2g", "gtg", "2h", "1h", "x2", "x3", "4u", "u2", "r2", "h8",
    };

    /// <summary>
    /// Spam or a scam: a link (also spaced out), money in dollars, a scam phrase (a flagged account, "GM_Support", a prize, an
    /// account sale, cheap plat), or noise (several leetspeak words like "ch34p pl4t", or a long line in capitals that is no
    /// call). Pure.
    /// </summary>
    public static bool IsSpam(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (Url.IsMatch(text) || Money.IsMatch(text) || Scam.IsMatch(text))
            return true;
        string[] words = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+").Where(word => word.Length > 0).ToArray();
        int leet = words.Count(word => word.Length >= 4 && Regex.IsMatch(word, @"^[a-z]+[0-9]+[a-z]+[a-z0-9]*$") && !PeriodMixed.Contains(word) &&
                                       !Regex.IsMatch(word, @"^(lf\dm|rr\d+|lvl\d+|\dl\d|level\d+)$"));
        if (leet >= 2 || leet >= 1 && words.Any(word => word is "plat" or "pl4t" or "gold" or "g0ld" or "cheap" or "ch34p" or "sale" or "visit"))
            return true;
        // A long line in capitals that is no call ("INC INC INC", "ADDS!!", "RUN" are calls).
        int letters = text.Count(char.IsLetter), capitals = text.Count(char.IsUpper);
        if (letters >= 20 && capitals >= letters * 0.8 && AutonomousChatIntentModel.Predict(text) is not (eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Rez) &&
            words.Length >= 5 && !words.Any(word => word is "inc" or "adds" or "add" or "run" or "help" or "train" or "zerg" or "rez"))
            return true;
        return false;
    }

    /// <summary>Words that address a crowd, not a person ("ty all", "gn guys", "gz everyone").</summary>
    private static readonly HashSet<string> Crowd = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "guys", "everyone", "everybody", "ppl", "people", "folks", "yall", "you", "u", "ya", "man", "dude", "bro", "mate", "m8", "buddy",
        "sir", "group", "grp", "guild", "gang", "team", "realm", "albs", "mids", "hibs", "albion", "midgard", "hibernia", "again", "too", "so",
        "much", "a", "lot", "very", "for", "on", "the", "to", "and", "me", "my", "it", "that", "this", "gl", "lol", "heh", "haha", "again",
        "friend", "friends", "lads", "ladies", "peeps", "crew", "fam", "everyone", "tank", "healer", "puller", "rez", "buffs", "group",
        "back", "there", "here", "now", "tho", "though", "anyway", "both", "yes", "no", "ok", "k", "np", "ty", "thx", "x", "xx",
        // Companies and things: "thx mythic" is sarcasm, not a name.
        "mythic", "goa", "ea", "gm", "gms", "isp", "aol", "comcast", "server", "lag", "patch", "game", "dsl", "windows", "microsoft",
    };

    private static readonly Regex AddressWord = new(
        @"^(?:ty|thx|thanks|thank you|tyvm|gz|grats|gratz|congrats|congratz|gn|night|nite|gnight|hey|hi|hiya|yo|sup|wb|cya|bye|later|hail|gj|good job|well done|sry|sorry)\s+(?<name>[a-z][a-z'\-]{2,15})(?=\s*($|[!.,?:)]|for\b|on\b|its\b|u\b|ur\b|you\b|again\b|:\)|<3))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OpensWithName = new(@"^(?<name>[A-Z][a-z'\-]{2,15})\s*[,:!]", RegexOptions.CultureInvariant);

    private static readonly Regex ToName = new(@"\b(?:congrats|grats|gz|gratz|ty|thanks|thx|props|shout ?out|cheers) (?:to|on) (?<name>[A-Z][a-z'\-]{2,15})\b|@(?<name>[a-z][a-z'\-]{2,15})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "anyone", "anybody", "someone", "somebody", "nobody", "wow", "well", "yes", "yeah", "nope", "okay", "sure", "man", "dude", "guys", "hey",
        "lol", "hmm", "ugh", "aww", "ouch", "oops", "damn", "dang", "heh", "haha", "inc", "help", "omg", "nice", "great", "good", "bad", "ok",
        "hail", "greetings", "pray", "halt", "aye", "nay", "alas", "tis", "behold", "hark", "brothers", "sisters", "friends", "folks", "everyone",
        "albs", "mids", "hibs", "albion", "midgard", "hibernia", "camelot", "jordheim", "emain", "odins", "hadrians", "thid", "seriously", "really",
        "btw", "also", "and", "but", "so", "then", "now", "first", "last", "next", "again", "today", "tonight", "tomorrow", "anyway", "ty", "thx",
        "gz", "grats", "gratz", "congrats", "gn", "night", "wb", "brb", "afk", "cya", "bye", "later", "sry", "sorry", "please", "pls", "plz",
        "quick", "question", "random", "note", "warning", "ps", "edit", "update", "fyi", "guild", "group", "realm", "the", "this", "that",
        "thanks", "thank", "cheers", "hello", "hiya", "yo", "sup", "heya", "welcome", "congratulations", "lmao", "rofl", "jeez", "geez", "whoa",
        "hold", "wait", "stop", "come", "look", "listen", "watch", "careful", "run", "go", "pull", "heal", "mez", "inc", "nvm", "idk", "imo",
        // things thanked for, not people ("ty heals :)", "thx for buffs", "gz ding")
        "heals", "buffs", "buff", "rez", "res", "rezz", "pulls", "grp", "invite", "inv", "port", "ports", "speed", "drop", "drops", "loot", "gold",
        "info", "tip", "tips", "advice", "reply", "answer", "carry", "pl", "ding", "level", "lvl", "epic", "rr", "help", "item", "items", "gear",
        "cash", "coin", "coins", "plat", "mats", "food", "water", "pots", "potions", "dye", "dyes", "taxi", "ride", "lift", "everything", "that",
    };

    /// <summary>
    /// The name a line is said to, when it is someone other than <paramref name="ownName"/> and other than a crowd: "Caoimhe ty for
    /// the gems!!", "Elarwen, wanna group?", "congrats to Tovah on 50!", "ty Bran", "gn sigrun". Null when the line is to
    /// everyone, to the bot, or to nobody in particular. Pure.
    /// </summary>
    public static string OtherAddressee(string text, string ownName, Func<string, bool> isKnownWord = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string trimmed = text.Trim();
        bool IsName(string name) => !string.IsNullOrEmpty(name) && !Crowd.Contains(name) && !NotNames.Contains(name) &&
                                    !AutonomousChatShortTalk.NamesAClass(name) && !(isKnownWord?.Invoke(name) ?? false) &&
                                    AutonomousChatQuestions.PlaceNamed(name, AutonomousChatPlaces.All) == null &&
                                    !string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase);
        foreach (Regex pattern in new[] { ToName, AddressWord })
            foreach (Match match in pattern.Matches(trimmed))
                if (IsName(match.Groups["name"].Value))
                    return match.Groups["name"].Value;
        Match opening = OpensWithName.Match(trimmed);
        if (opening.Success && IsName(opening.Groups["name"].Value))
            return opening.Groups["name"].Value;
        // "Caoimhe ty for the gems!!", "Morwenna wanna duo?": a capitalised name, then chat to it.
        Match lead = Regex.Match(trimmed, @"^(?<name>[A-Z][a-z'\-]{2,15}) (?:ty|thx|thanks|wanna|want|u |you |r u|are you|can u|can you|gz|grats|gn|lol|plz|pls|come|wait|stop|where|did|have|is that)",
            RegexOptions.CultureInvariant);
        return lead.Success && IsName(lead.Groups["name"].Value) ? lead.Groups["name"].Value : null;
    }

    /// <summary>A bot near a player's /say line: how far, whether the player faces it or targets it, whether it spoke last there.</summary>
    public readonly record struct Near(long Id, double Distance, bool InFront, bool Targeted, bool SpokeLast);

    /// <summary>"A few metres" for a /say line to one bot (game units; melee reach is about 200).</summary>
    public const double CloseRadius = 250;

    /// <summary>How soon after a bot's own /say line a player's line is taken as an answer to it.</summary>
    public const long FollowsBotLineMs = 20_000;

    /// <summary>
    /// The bot a /say line is aimed at without naming it (Beta 1.1.2, round 3): the one the player targets; else the one whose
    /// line it follows (within 20 seconds); else the one within a few metres the player faces, when nobody else (bot or player)
    /// is that close. Null when the line is to the room. Pure.
    /// </summary>
    public static long? SayAddressee(IReadOnlyList<Near> bots, int otherPlayersClose)
    {
        if (bots == null || bots.Count == 0)
            return null;
        foreach (Near bot in bots)
            if (bot.Targeted)
                return bot.Id;
        foreach (Near bot in bots)
            if (bot.SpokeLast)
                return bot.Id;
        Near? close = null;
        int closeCount = 0;
        foreach (Near bot in bots)
            if (bot.Distance <= CloseRadius)
            {
                closeCount++;
                close = bot;
            }
        return closeCount == 1 && otherPlayersClose == 0 && close.Value.InFront ? close.Value.Id : null;
    }

    /// <summary>
    /// What to do with a line said to someone else in this channel: in the bot's own group, a light laugh now and then is fine
    /// (teasing a groupmate); anywhere else, nothing. Pure.
    /// </summary>
    public static bool MayReactToOthers(string channel, eAutonomousChatIntent intent) =>
        channel == "group" && intent is eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Banter or eAutonomousChatIntent.Abuse;
}
