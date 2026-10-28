using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Questions a bot can answer from what is true (the talking update audit, 2026-10-26). The audit ran 320 realistic player
/// lines through the reply path: a factual question ("is thid open for lvl 21", "what lvl is vendo caverns", "is df ours",
/// "r u 50 yet") drew a remark on the topic ("if you see the zerg coming, call it out"), because replies were picked by topic.
/// Now these are answered from the bot itself (level, class, realm rank, potions, guild) and the world (monster levels of a
/// place from the world data, battleground brackets, who holds Darkness Falls, relics and keeps); another factual question
/// gets an honest "not sure" or nothing, never a remark. Pure: the caller passes the facts.
/// </summary>
public static class AutonomousChatQuestions
{
    /// <summary>What a bot knows about itself.</summary>
    /// <param name="Equipped">Worn armor, weapon and jewelry pieces; <paramref name="Crafted"/> of them made by a crafter (IsCrafted).</param>
    /// <param name="Busy">In a fight, or on something it can't leave (a keep, a relic, a raid).</param>
    public sealed record BotFacts(long Id, int Level, string ClassName, eRealm Realm, int RealmLevel, BotPersonalityType Type,
        bool HasSpeed, int Potions, string Guild, int Equipped = 0, int Crafted = 0, bool Busy = false);

    /// <summary>What the bot's realm knows of the world.</summary>
    public sealed record WorldFacts(eRealm DfOwner, IReadOnlyList<(string Name, eRealm Owner, eRealm Home, bool Mounted)> Relics,
        IReadOnlyList<(string Name, eRealm Owner)> Keeps, IReadOnlyList<AutonomousChatPlaces.Place> Places);

    private static readonly Regex Space = new(@"\s+");

    /// <summary>Lower case, letters, digits and spaces only, apostrophes dropped ("what's" -> "whats").</summary>
    public static string Normalize(string text)
    {
        string t = (text ?? string.Empty).ToLowerInvariant().Replace("'", string.Empty);
        t = Regex.Replace(t, @"[^a-z0-9 ]", " ");
        return Space.Replace(t, " ").Trim();
    }

    private static readonly Regex QuestionStart = new(
        @"^(is|are|was|were|can|could|do|does|did|would|should|how|what|whats|wat|wut|where|wheres|when|which|who|whos|why|r u|r we|r they|r (the|our|any|all|those|these|relics|keeps|there|ppl|people)|u got|you got|u have|any1 know|anyone know|anybody know|anyone think|any1 think|anyone else think|does anyone|do you|do u|did u|did you|is there|are there|(will|has|have) (you|u|it|there|they|we|i|he|she|anyone|any1|someone))\b");

    /// <summary>Whether a line opens like a question ("is", "how", "r u"...), whatever its end.</summary>
    public static bool StartsLikeQuestion(string text)
    {
        string t = Normalize(text);
        t = Regex.Replace(t, @"^(hi|hey|hello|yo|ok|so|um|hmm|quick question|question|q|all|guys|everyone|realm)( (all|guys|everyone|so))? ", "");
        return QuestionStart.IsMatch(t);
    }

    /// <summary>"is df ours?", "what lvl for vendo", "r u 50 yet": a question, with or without the question mark.</summary>
    public static bool IsQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.TrimEnd().EndsWith('?'))
            return true;
        string t = Normalize(text);
        // "hi all, anyone know..." / "ok so where is...": the question may follow a greeting or filler.
        t = Regex.Replace(t, @"^(hi|hey|hello|yo|ok|so|um|hmm|quick question|question|q|all|guys|everyone|realm)( (all|guys|everyone|so))? ", "");
        return QuestionStart.IsMatch(t);
    }

    private static readonly Regex OpinionWords = new(
        @"\b(worth|good|better|best|fun|think|thoughts|recommend|fav|favorite|favourite|like|prefer|opinion|should i|would you|viable|op|nerfed|any good|decent)\b");

    /// <summary>A question that wants a fact (where, when, what level, yes or no), not an opinion.</summary>
    public static bool IsFactual(string text) => IsQuestion(text) && !OpinionWords.IsMatch(Normalize(text)) &&
        // "who keeps rolling need on everything lol" is a remark, "hmm where did i put that note" talking to oneself (the holdout check).
        !Regex.IsMatch(Normalize(text), @"\b(lol|lmao|rofl|haha|heh)$|^(hmm |um |uh |ugh )?(where|what) did i (put|leave|drop|do with)\b");

    private static readonly Regex You = new(@"\b(u|you|ur|your|youre|ya|yours|yourself|whatcha|watcha|wyd)\b");

    #region About the bot

    /// <summary>The realm rank as the server writes it (API/Player.cs GetRR): RealmLevel 0 is 1L0, 13 is 2L3.</summary>
    public static string RankText(int realmLevel) => $"{realmLevel / 10 + 1}L{realmLevel % 10}";

    private static readonly (Regex Pattern, string Kind)[] SelfQuestions =
    [
        (new Regex(@"\b(what (lvl|level) (are|r) (you|u)|what (lvl|level) (you|u)|(you|u|r u|are you|are u) (\d+|fifty) yet|how high (are|r) (you|u)|(r|are) (u|you) (\d{2}|[5-9])( yet| already| now)?$|ur level|your level|(you|u) (hit|dinged|reach|reached|made) (\d+|fifty)\b)"), "level"),
        (new Regex(@"\b(what class (are|r) (you|u)|what (are|r) (you|u) playing|what do (you|u) play|(you|u|ur) an? (healer|tank|caster|cleric|druid|bard|shaman|healer))\b"), "class"),
        (new Regex(@"\b(whats (ur|your) (rr|realm rank|rank)|what (rr|realm rank) (are|r) (you|u)|(ur|your) (rr|realm rank))\b"), "rank"),
        (new Regex(@"\b((do|can) (you|u) (have|do|cast|sing) speed|(you|u) got speed|(you|u) have speed|any speed)\b"), "speed"),
        (new Regex(@"\b((you|u) (got|have) (any )?(pots|potions)|any (pots|potions)|spare (pots|potions))\b"), "pots"),
        (new Regex(@"\b(how long (have )?(you|u) (been )?(play|playing|playin|played)|since when (do|have) (you|u)|when did (you|u) start|(you|u) (been )?(play|playing|playin|played) since|since (launch|release|beta)\b|how old is (ur|your) (toon|char|character)|(you|u) (been )?playing (forever|long|a while))"), "playing"),
        (new Regex(@"\b((did|have) (you|u) (ever )?(finish|finished|done|do|get|got) (ur|your|the) epic|(ur|your) epic|done (ur|your|the) epic)\b"), "epic"),
        (new Regex(@"\b(where(d| did| do)? (you|u) (get|got|find|buy) (that|those|this|ur|your|the)|(that|those|ur|your|nice) (\w+ )?(cloak|armor|armour|sword|weapon|shield|staff|bow|helm|helmet|gloves|boots|suit|set|gear|axe|hammer|spear|robe|necklace|ring|bracer|belt)\b.*\b(where|from|drop|drops|crafted|sc)\b|is (that|it) (a |an )?(sc|spellcraft|spellcrafted|crafted|drop|drops)|(sc|crafted) or (drop|drops|dropped))"), "gear"),
        (new Regex(@"\b((how many )?(rps|realm points) (did )?(you|u) (make|get|got|earn|made)|(you|u) (make|get|got|made) (any|many) (rps|realm points))\b"), "rps"),
        (new Regex(@"\b(sent (u|you) the|gave (u|you) the|traded (u|you) the|sent (u|you) some)\b"), "received"),
        (new Regex(@"\b(where (have )?(you|u) been|(havent|haven t|not) seen (you|u)|where u been)\b"), "away"),
        (new Regex(@"\b((are|r|will) (you|u) (be )?on (tonight|later|tomorrow)|(you|u) on (tonight|later|tomorrow))\b"), "online"),
        (new Regex(@"\b(what guild (are |r )?(you|u)|(ur|your) guild|(are|r|you|u) (you |u |still )?in a guild)\b"), "guild"),
        (new Regex(@"\b((is this|this) (ur|your) main|(ur|your) main|(an|ur|your) alt|how many (chars|alts|toons|characters) (do )?(you|u) (got|have)|(you|u) (got|have) (a|an|any) (\w+ )?(alt|alts))\b"), "main"),
        (new Regex(@"\b((are |r )?(you|u) busy|(you|u) free (now|atm|rn)|(are |r )?(you|u) (doing anything|in the middle of))\b"), "busy"),
        (new Regex(@"\b(how (was|is) (ur|your) (weekend|day|week|night)|hows (ur|your) (weekend|day|week|night)|how (was|is) work|hows work|hows life|did (you|u) get (that|the) job)\b"), "life"),
        (new Regex(@"\b((you|u) selling|are (you|u) selling|is (that|it) for sale)\b"), "selling"),
        (new Regex(@"\b(can (you|u) make|(you|u) still (making|crafting|make)|are (you|u) a crafter|do (you|u) craft|(you|u) (a )?crafter)\b"), "craft"),
        (new Regex(@"\b(hows (ur|your) \w+ coming( along)?|how is (ur|your) \w+ (going|coming))\b"), "progress"),
        (new Regex(@"\b((ur|youre|you are|u r) on late|cant sleep)\b"), "late"),
        (new Regex(@"\b((you|u) coming to the raid|(you|u) in for the raid|coming to the raid)\b"), "raid"),
        (new Regex(@"\b(whats (ur|your) spec|how (are|r) (you|u) specced|(ur|your) spec|what spec (are|r) (you|u)|what (are|r) (you|u) specced|(you|u) (specd|specced|spec d) (for|in|into))\b"), "spec"),
        (new Regex(@"\b((you|u) (going|goin|gonna go|heading|headed|coming) (to|out)\b(?! (sell|buy|make|be|need|pull|rez|heal|help|give|trade|craft)\b)|what (are |r )?(you|u) (doing|up to) (tonight|later|tomorrow)|(you|u) (doing|running) (rvr|df|emain|a raid)|(you|u) (going|goin) \w+ (later|tonight)|whatcha (up to|doing)|(you|u) (heading|headed|going|goin) (anywhere|somewhere)|(you|u) still (lvling|leveling|levelling|pve|rvr|grinding))"), "plans"),
    ];

    /// <summary>What a question asks about the bot it is said to ("level", "class", "rank"...), or null.</summary>
    public static string SelfQuestion(string text)
    {
        string t = Normalize(text);
        // "nice armor where from", "is that a sc suit or drops" said to a bot by name ask about its gear without a "you".
        if (!You.IsMatch(t))
            return Regex.IsMatch(t, @"\bis (that|it) (the|ur|your) epic\b") ? "epic"
                : SelfQuestions.First(question => question.Kind == "gear").Pattern.IsMatch(t) ? "gear"
                : Regex.IsMatch(t, @"^((hey|hi|yo|so|oi) )?(what|wat) (class|race) (is that|is this|are those)\b") ? "class"
                : null;
        foreach ((Regex pattern, string kind) in SelfQuestions)
            if (pattern.IsMatch(t))
                return kind;
        // "u still a thane?", "u r a healer?", "ur druid right not bard" (the holdout check): a role or class said of the bot.
        foreach (Match said in Regex.Matches(t, @"\b(u r|you are|youre|are you|are u|r u|you|ur|u)( still)?( an?)? (\w+)\b"))
            if (RoleWords.ContainsKey(said.Groups[4].Value) || AutonomousChatShortTalk.NamesAClass(said.Groups[4].Value))
                return "class";
        return null;
    }

    private static readonly Dictionary<string, string[]> RoleWords = new(StringComparer.Ordinal)
    {
        ["healer"] = ["Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden", "Mentalist"],
        ["tank"] = ["Armsman", "Paladin", "Warrior", "Hero", "Thane", "Champion", "Blademaster", "Mercenary", "Valewalker"],
        ["caster"] = ["Wizard", "Theurgist", "Sorcerer", "Cabalist", "Necromancer", "Runemaster", "Spiritmaster", "Bonedancer",
            "Eldritch", "Enchanter", "Mentalist", "Animist"],
    };

    /// <summary>The bot's answer to a question about itself, from what is true of it. Pure.</summary>
    public static string AnswerSelf(string kind, string text, BotFacts bot, Random random)
    {
        string cls = (bot.ClassName ?? "adventurer").ToLowerInvariant();
        string t = Normalize(text);
        switch (kind)
        {
            case "level":
            {
                // "im 30, what lvl r u": the player's own level is not the one asked about (review 2026-10-26).
                Match asked = Regex.Match(Regex.Replace(t, @"\b(im|i m|i am|im lvl|im level|me) \d{1,2}\b", ""), @"\b(\d{1,2})\b");
                if (bot.Level >= 50)
                    return random.Next(2) == 0 ? "yep, 50" : "50, finally";
                if (asked.Success && int.TryParse(asked.Groups[1].Value, out int level) && level != bot.Level)
                    return random.Next(2) == 0 ? $"not yet, {bot.Level}" : $"nope, only {bot.Level}";
                return random.Next(3) switch { 0 => $"{bot.Level}", 1 => $"{bot.Level}, getting there", _ => $"{bot.Level} {cls}" };
            }
            case "class":
            {
                foreach ((string role, string[] classes) in RoleWords)
                    if (Regex.IsMatch(t, $@"\b{role}\b"))
                        return classes.Contains(bot.ClassName, StringComparer.OrdinalIgnoreCase)
                            ? $"yep, {cls}"
                            : random.Next(2) == 0 ? $"nope, {cls}" : $"no, im a {cls}";
                string named = AutonomousChatShortTalk.ClassWordsIn(text).FirstOrDefault();
                if (named != null)
                    return AutonomousChatShortTalk.SameClassWord(named, cls) || cls.StartsWith(named.TrimEnd('s'), StringComparison.Ordinal)
                        ? $"yep, {cls}"
                        : $"no, {cls}";
                return random.Next(2) == 0 ? $"{cls}, {bot.Level}" : $"{bot.Level} {cls}";
            }
            case "rank":
                return bot.RealmLevel <= 0
                    ? random.Next(2) == 0 ? "rr1 lol, havent done much rvr" : "still rr1, mostly pve"
                    : random.Next(2) == 0 ? $"{RankText(bot.RealmLevel)}" : $"only {RankText(bot.RealmLevel)} lol";
            case "speed":
                return bot.HasSpeed
                    ? random.Next(2) == 0 ? "yep, ill keep it up" : "yeah i have speed"
                    : random.Next(2) == 0 ? $"no speed sorry, {cls}" : "nope, no speed on me";
            case "pots":
                return bot.Potions switch
                {
                    0 => random.Next(2) == 0 ? "nope, out" : "none, sorry",
                    1 => "just one",
                    < 5 => "a couple",
                    _ => "yeah a few",
                };
            case "playing":
                return (bot.Id % 4, bot.Type) switch
                {
                    (_, BotPersonalityType.NoLifer) => "since release, dont ask",
                    (0, _) => "since launch pretty much",
                    (1, _) => "a few months",
                    (2, _) => "since last summer",
                    _ => "a while, on and off",
                };
            case "epic":
                return bot.Level >= 50
                    ? random.Next(2) == 0 ? "yep, finally" : "yeah, took forever"
                    : random.Next(2) == 0 ? "not yet, need 50 first" : $"no, im only {bot.Level}";
            case "away":
                return random.Next(3) switch { 0 => "busy with rl stuff", 1 => "around, just quiet", _ => "work ate my week" };
            case "online":
                return random.Next(3) switch { 0 => "probably for a bit", 1 => "should be", _ => "maybe later on" };
            case "guild":
                return string.IsNullOrWhiteSpace(bot.Guild)
                    ? random.Next(2) == 0 ? "no guild right now" : "guildless atm"
                    : $"{bot.Guild}";
            case "main":
            {
                // One story per bot (by id): an alt whose main is parked, a main with alts, or a single character.
                bool alt = bot.Id % 5 == 0, hasAlts = alt || bot.Id % 3 == 0;
                if (Regex.IsMatch(t, @"\bhow many (chars|alts|toons|characters)\b"))
                    return alt ? "two, this ones my alt" : hasAlts ? "a couple, this ones my main" : "just this one";
                if (Regex.IsMatch(t, @"\b(got|have) (a|an|any) (\w+ )?alts?\b"))
                    return alt ? "this is my alt actually" : hasAlts ? "yeah, a couple parked" : "nah, just this one";
                return alt ? "alt, my main is parked" : "main";
            }
            case "life":
                return random.Next(3) switch { 0 => "not bad, you?", 1 => "pretty good ty", _ => "long, glad to be on" };
            case "received":
                return random.Next(2) == 0 ? "ty, will check" : "oh ty!";
            case "selling":
                return random.Next(2) == 0 ? "nah, not selling" : "not selling, sorry, i use it";
            case "craft":
                return bot.Type == BotPersonalityType.Crafter
                    ? random.Next(2) == 0 ? "yeah, what do you need?" : "sure, send me what you need made"
                    : random.Next(2) == 0 ? "not a crafter, sorry" : "i dont craft, ask around";
            case "progress":
                return bot.Level >= 50 ? "50 now, done leveling" : random.Next(2) == 0 ? $"{bot.Level} now, slow but getting there" : $"ok, {bot.Level} now";
            case "late":
                return random.Next(2) == 0 ? "yeah cant sleep lol" : "one more level, then bed";
            case "raid":
                return random.Next(3) switch { 0 => "maybe, depends when", 1 => "should be", _ => "if im on, yeah" };
            case "gear":
                if (bot.Equipped == 0)
                    return random.Next(2) == 0 ? "what gear lol, still working on it" : "barely have any yet, earning it";
                if (bot.Crafted * 2 >= bot.Equipped)
                    return random.Next(2) == 0 ? "crafted mostly" : "crafter made most of it";
                // Drops and merchant buys look alike here, so a bot with nothing crafted doesn't claim either.
                return bot.Crafted > 0
                    ? random.Next(2) == 0 ? "a mix, a crafted piece or two" : "mostly picked up along the way, a couple crafted"
                    : random.Next(2) == 0 ? "picked it up along the way" : "drops and merchant stuff mostly";
            case "busy":
                return bot.Busy
                    ? random.Next(2) == 0 ? "bit busy right now, whats up?" : "kinda, in the middle of something. whats up?"
                    : random.Next(2) == 0 ? "not really, whats up?" : "nah, whats up?";
            case "rps":
                return bot.RealmLevel <= 0
                    ? random.Next(2) == 0 ? "none lol, wasnt out" : "havent done much rvr"
                    : random.Next(2) == 0 ? "a few, not many" : "not a lot, it was slow";
            case "plans":
                // "u still lvling or just rvr now": a yes or no about what it is doing.
                if (Regex.IsMatch(t, @"\bstill (lvling|leveling|levelling|grinding|pve)\b"))
                    return bot.Level >= 50 ? "done leveling, 50 now" : random.Next(2) == 0 ? "yeah, still leveling" : $"still leveling, {bot.Level} now";
                // "whatcha up to tonight" is open; "u goin to emain later" a yes or no.
                if (Regex.IsMatch(t, @"^(what|wat|whatcha|watcha|where|wyd)\b|\b(what|where) (are |r )?(you|u)\b|whatcha|watcha|anywhere|somewhere"))
                    return bot.Type switch
                    {
                        BotPersonalityType.Daredevil => "probably the frontier if theres a fight",
                        BotPersonalityType.Crafter => "crafting mostly",
                        BotPersonalityType.Explorer => "might wander somewhere new",
                        BotPersonalityType.Socialite => "whatever the guild is doing",
                        _ => bot.Level >= 50 ? "not sure yet" : random.Next(2) == 0 ? "prob just leveling" : "leveling, same as always",
                    };
                return bot.Type switch
                {
                    BotPersonalityType.Daredevil => random.Next(2) == 0 ? "probably, if theres a fight" : "maybe, depends whats going on",
                    BotPersonalityType.Crafter => random.Next(2) == 0 ? "prob just crafting" : "crafting more likely",
                    BotPersonalityType.Explorer => random.Next(2) == 0 ? "might wander somewhere new" : "not sure yet, maybe explore a bit",
                    BotPersonalityType.Socialite => random.Next(2) == 0 ? "whatever the guild is doing" : "if people are going, sure",
                    _ => bot.Level >= 50
                        ? random.Next(2) == 0 ? "maybe, not sure yet" : "probably at some point"
                        : random.Next(2) == 0 ? "nah, leveling" : "prob just leveling",
                };
            case "spec":
                return random.Next(2) == 0 ? "still figuring it out lol" : "mostly my main line, bit of the rest";
        }
        return null;
    }

    #endregion

    #region About the world

    private static readonly Regex LevelWords = new(
        @"\b(lvl|lvls|level|levels|level range|what lvl|for lvl|for level|open for|at \d{1,2}(ish)?|for a \d{1,2}|can a \d{1,2}|a \d{1,2} (solo|go|do|hunt)|\d{1,2}s)\b");

    /// <summary>The place a line names (its longest alias, whole words), or null.</summary>
    public static AutonomousChatPlaces.Place PlaceNamed(string text, IEnumerable<AutonomousChatPlaces.Place> places)
    {
        string t = " " + Normalize(text) + " ";
        AutonomousChatPlaces.Place best = null;
        int bestLength = 0;
        foreach (AutonomousChatPlaces.Place place in places)
            foreach (string alias in place.Aliases.Append(place.Key))
            {
                string a = Normalize(alias);
                if (a.Length >= 3 && a.Length > bestLength && t.Contains(" " + a + " ", StringComparison.Ordinal))
                    (best, bestLength) = (place, a.Length);
            }
        return best;
    }

    private static string RealmWord(string code) => code switch { "alb" => "albion", "mid" => "midgard", "hib" => "hibernia", _ => null };

    private static string RealmCode(eRealm realm) => realm switch { eRealm.Albion => "alb", eRealm.Midgard => "mid", eRealm.Hibernia => "hib", _ => "*" };

    private static string Range(int low, int high) => high > 50 ? $"{Math.Min(low, 50)}+" : $"{low}-{high}";

    /// <summary>"what lvl is vendo caverns", "is thid open for lvl 21", "can a 15 solo in spraggon den". Pure.</summary>
    public static string AnswerPlaceLevel(string text, eRealm realm, IEnumerable<AutonomousChatPlaces.Place> places, Random random)
    {
        string t = Normalize(text);
        if (!LevelWords.IsMatch(t))
            return null;
        AutonomousChatPlaces.Place place = PlaceNamed(text, places);
        if (place?.Levels is not { Length: 2 } levels)
            return null;
        // The frontier's danger is players, not monsters: "is hadrians wall safe at 30" isn't a level question (the audit); only
        // a plain "what level is emain" is answered for a frontier zone.
        if (place.Type == "frontier" && !Regex.IsMatch(t, @"\b(lvl|level|levels)\b"))
            return null;
        // "good spot to hunt at 12 near mag mell?" asks where to hunt (the live answer), not a place's levels.
        if (AutonomousChatShortTalk.QuestionShape(text) is "ask_hunt")
            return null;
        string name = place.Key;
        // A Hibernian asked about Vendo Caverns: say whose it is (the live report from Ardee, the other way round).
        string theirs = place.Realm != "*" && place.Realm != RealmCode(realm) && place.Type is not ("frontier" or "battleground")
            ? $"thats in {RealmWord(place.Realm)} lol, "
            : "";
        int low = levels[0], high = levels[1];
        Match asked = Regex.Match(t, @"\b(?:lvl|level|at|a|for)\s*(\d{1,2})(?:ish)?\b|\b(\d{1,2})(?:s|ish)?\b");
        int level = 0;
        if (asked.Success)
            int.TryParse(asked.Groups[1].Success ? asked.Groups[1].Value : asked.Groups[2].Value, out level);
        if (place.Type == "battleground")
        {
            string bracket = $"{low}-{high}";
            if (level is >= 1 and <= 50)
                return level >= low && level <= high
                    ? random.Next(2) == 0 ? $"yep, {name} is {bracket}" : $"yes, {bracket} for {name}"
                    : $"no, {name} is {bracket}";
            return random.Next(2) == 0 ? $"{name} is {bracket}" : $"{bracket} for {name}";
        }
        if (level is >= 1 and <= 50)
        {
            if (level < low - 2)
                return theirs + $"a bit high for {level}, mobs there are mostly {Range(low, high)}";
            if (level > high + 3)
                return theirs + $"itll be mostly grey for you at {level}, its {Range(low, high)}";
            return theirs + (random.Next(2) == 0 ? $"{level} is fine there, its mostly {Range(low, high)}" : $"should be ok, mobs are {Range(low, high)}");
        }
        return theirs + (random.Next(3) switch
        {
            0 => $"mostly {Range(low, high)}",
            1 => $"{name} is about {Range(low, high)}",
            _ => $"around {Range(low, high)} i think",
        });
    }

    private static string RealmName(eRealm realm) => realm switch
    {
        eRealm.Albion => "albion", eRealm.Midgard => "midgard", eRealm.Hibernia => "hibernia", _ => "nobody",
    };

    private static string Plural(eRealm realm) => realm switch
    {
        eRealm.Albion => "the albs", eRealm.Midgard => "the mids", eRealm.Hibernia => "the hibs", _ => "nobody",
    };

    /// <summary>"is df ours?", "who has df". Pure.</summary>
    public static string AnswerDarknessFalls(string text, eRealm realm, eRealm owner, Random random)
    {
        string t = Normalize(text);
        // Who holds it, not "anyone have a df group?" or "who wants to do df?" (review 2026-10-26).
        if (!Regex.IsMatch(t, @"\b(df|darkness falls)\b") || !Regex.IsMatch(t,
                @"\b((is|s) (df|darkness falls) (ours|open|theirs|still ours)|who (has|owns|holds|got|has got) (df|darkness)|(do|did) we (still )?(have|own|hold|get|lose) (df|darkness)|(df|darkness falls) (open|ours|still ours)|can we get (in|into) (df|darkness)|(we|hibs|mids|albs|they) (still )?(have|own|hold|got|lost|take|took) (df|darkness))\b"))
            return null;
        if (owner == eRealm.None)
            return "not sure who has it right now";
        if (owner == realm)
            return random.Next(2) == 0 ? "yep, df is ours" : "yeah we have it";
        return random.Next(2) == 0 ? $"no, {Plural(owner)} have it" : $"{RealmName(owner)} has df right now";
    }

    /// <summary>"do we still own our relics?", "did we lose a relic". Pure.</summary>
    public static string AnswerRelics(string text, eRealm realm, IReadOnlyList<(string Name, eRealm Owner, eRealm Home, bool Mounted)> relics, Random random)
    {
        string t = Normalize(text);
        if (!Regex.IsMatch(t, @"\brelics?\b") || !Regex.IsMatch(t, @"\b(own|have|still|lost|lose|ours|our|which|how many|who|got|taken|took)\b") ||
            relics == null || relics.Count == 0)
            return null;
        var ours = relics.Where(r => r.Home == realm).ToArray();
        var lost = ours.Where(r => r.Owner != realm).ToArray();
        var taken = relics.Where(r => r.Home != realm && r.Owner == realm).ToArray();
        string extra = taken.Length > 0 ? $", and we hold {taken.Length} of theirs" : "";
        if (lost.Length == 0)
            return (random.Next(2) == 0 ? "yep, both of ours are home" : "we still have ours") + extra;
        if (lost.Length == ours.Length)
            return $"no, {Plural(lost[0].Owner)} have " + (lost.Length == 1 ? lost[0].Name : "both of ours") + extra;
        return $"{Plural(lost[0].Owner)} have {lost[0].Name}" + extra;
    }

    /// <summary>"who owns caer benowyc", "is bledmeer ours". Pure.</summary>
    public static string AnswerKeep(string text, eRealm realm, IReadOnlyList<(string Name, eRealm Owner)> keeps, Random random)
    {
        string t = " " + Normalize(text) + " ";
        // Who holds a keep, not "anyone have a group at benowyc?" or "anyone still at bledmeer?" (review 2026-10-26).
        if (keeps == null || !Regex.IsMatch(t,
                @"\b(who (has|owns|holds|got|took|has got)|ours|theirs|do we (still )?(have|own|hold)|(did|have) we (lose|lost|take|took)|(we|they) (lost|took|take|have taken|took back)|still (have|own|hold))\b"))
            return null;
        foreach ((string name, eRealm owner) in keeps)
        {
            string keyWord = Normalize(name).Split(' ')
                .Where(w => w is not ("caer" or "dun" or "fort" or "faste" or "keep" or "tower" or "of" or "the" or "nged") && w.Length >= 4)
                .OrderByDescending(w => w.Length).FirstOrDefault();
            if (keyWord == null || !t.Contains(" " + keyWord + " ", StringComparison.Ordinal))
                continue;
            string keep = name.ToLowerInvariant();
            if (owner == realm)
                return random.Next(2) == 0 ? $"yep, {keep} is ours" : $"we still have {keep}";
            return random.Next(2) == 0 ? $"no, {Plural(owner)} have {keep}" : $"{RealmName(owner)} has {keep} right now";
        }
        return null;
    }

    /// <summary>
    /// An order the bot can't carry out, said honestly ("heal me" to a thane: "cant heal, im a thane"), or null when it can (it
    /// then says "on it"). Pure.
    /// </summary>
    public static string CantDo(string text, BotFacts bot)
    {
        string t = " " + Normalize(text) + " ";
        string cls = (bot.ClassName ?? "adventurer").ToLowerInvariant();
        bool healer = RoleWords["healer"].Contains(bot.ClassName, StringComparer.OrdinalIgnoreCase);
        if (Regex.IsMatch(t, @" (heal|heals) ") && !healer)
            return $"cant heal, im a {cls}";
        if (Regex.IsMatch(t, @" (rez|res) ") && !healer)
            return $"no rez on me, {cls}";
        if (Regex.IsMatch(t, @" speed ") && !bot.HasSpeed)
            return "no speed on me sorry";
        if (Regex.IsMatch(t, @" (mez|mezz) ") && !(bot.ClassName is "Sorcerer" or "Minstrel" or "Bard" or "Healer" or "Mentalist" or "Skald" or
                "Enchanter" or "Eldritch"))
            return "cant mez, sorry";
        return null;
    }

    /// <summary>"stuck in a rock, help?", "whats the command to go anon": the server's own commands. Pure.</summary>
    public static string AnswerCommand(string text, Random random)
    {
        string t = Normalize(text);
        if (Regex.IsMatch(t, @"\b(stuck in|im stuck|i m stuck|got stuck|stuck on)\b"))
            return random.Next(2) == 0 ? "try /stuck" : "/stuck usually gets you out";
        if (Regex.IsMatch(t, @"\b(go anon|be anon|anonymous|hide from who|hide from \/who)\b"))
            return "/anon";
        if (Regex.IsMatch(t, @"\b(how (do|can) (i|you|u) bind|where (do|can) (i|you|u) bind|how to bind)\b"))
            return "stand by a bindstone and type /bind";
        if (Regex.IsMatch(t, @"\b(how long have i played|see my played time|check my played|how do i see how long)\b"))
            return "/played";
        if (Regex.IsMatch(t, @"\b(how (do|can) i (check|see) (the )?relics|where (do|can) i see (the )?relics)\b"))
            return "/relic shows them";
        return null;
    }

    /// <summary>Any of the world answers for this question, or null.</summary>
    public static string AnswerWorld(string text, eRealm realm, WorldFacts world, Random random) =>
        // Only a question gets a world answer: "zerg heading for our relic keep" is an alarm, not "do we own our relics?".
        // A group request that names a place is not a question about it ("anyone want to do vendo at 25?", "who is at benowyc?").
        world == null || !IsQuestion(text) || AutonomousChatShortTalk.QuestionShape(text) is "ask_anyone" or "ask_inplace" or "ask_on" ||
        AutonomousChatIntentModel.Predict(text) == eAutonomousChatIntent.Grouping ? null
            : AnswerCommand(text, random)
              ?? AnswerPlaceLevel(text, realm, world.Places, random)
              ?? AnswerDarknessFalls(text, realm, world.DfOwner, random)
              ?? AnswerRelics(text, realm, world.Relics, random)
              ?? AnswerKeep(text, realm, world.Keeps, random);

    #endregion
}
