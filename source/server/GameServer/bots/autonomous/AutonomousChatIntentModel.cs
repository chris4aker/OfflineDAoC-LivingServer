using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

public enum eAutonomousChatIntent
{
    Unknown,
    Greeting,
    Banter,
    Abuse,
    Grouping,
    Grinding,
    Travel,
    Dungeon,
    RvR,
    Trading,
    Help,
    Farewell,
    IgnoreOutOfWorld,
    // 0.36-se.4: short chat players type (appended, so existing values keep their numbers)
    Thanks,
    Congrats,
    Ding,
    Laugh,
    Away,
    Back,
    Rez,
    Alarm,
    ClassTalk,
    BotCheck,
    // Beta 1.1.2: an in-character line, answered in kind
    Roleplay,
    // Crafting 2.0 WP8: a request to a crafter ("can you make", "wtb ... set", "lf sc", "what do you make"); only
    // PredictForCrafter returns it, so every other reader of Predict is unchanged
    Crafting,
}

/// <summary>
/// Reads what a chat line is about. Short chat and common requests ("ty", "gz", "brb", "inc", "rez pls",
/// "wts", "lfg", "are you a bot?") are matched by word rules first (0.36-se.4: the audit found "lol", "ty",
/// "gz" and "inc" read as real-world talk, which /say then ignored). Anything else goes to a tiny
/// multinomial Naive Bayes classifier trained from embedded DAoC chat examples, which now needs at least
/// one word it knows. No network, no generative model and no player-data training.
/// </summary>
public static partial class AutonomousChatIntentModel
{
    private sealed record Example(eAutonomousChatIntent Intent, string Text);
    private sealed record Model(Dictionary<eAutonomousChatIntent, Dictionary<string, int>> Counts,
        Dictionary<eAutonomousChatIntent, int> Totals, Dictionary<eAutonomousChatIntent, int> Documents,
        HashSet<string> Vocabulary, int DocumentCount);

    private static readonly Example[] Training =
    [
        new(eAutonomousChatIntent.Greeting, "hello hi hey greetings hail whats up how goes it good evening"),
        new(eAutonomousChatIntent.Greeting, "morning everyone hello realm hey folks how are you"),
        new(eAutonomousChatIntent.Greeting, "yo there hi friends good to see you"),
        new(eAutonomousChatIntent.Banter, "you guys suck terrible group bad pull weaklings"),
        new(eAutonomousChatIntent.Banter, "nice try you hit like a kobold that was rough"),
        new(eAutonomousChatIntent.Banter, "come on slowpokes is that all you have"),
        new(eAutonomousChatIntent.Abuse, "you are shit dumb idiots morons garbage trash"),
        new(eAutonomousChatIntent.Abuse, "stupid loser shut up worthless terrible player"),
        new(eAutonomousChatIntent.Abuse, "everyone here is awful you are all pathetic"),
        new(eAutonomousChatIntent.Grouping, "looking for group need healer tank damage invite join party"),
        new(eAutonomousChatIntent.Grouping, "anybody grouping want to team up group has room"),
        new(eAutonomousChatIntent.Grouping, "forming party need help with harder mobs"),
        new(eAutonomousChatIntent.Grinding, "where are you grinding hunting mobs camp experience xp leveling"),
        new(eAutonomousChatIntent.Grinding, "good monster camp killing creatures gain levels"),
        new(eAutonomousChatIntent.Grinding, "anyone farming skeletons wolves bandits loot drops"),
        new(eAutonomousChatIntent.Travel, "where are you going how do I get there road route stable master"),
        new(eAutonomousChatIntent.Travel, "which way to town trainer capital horse ticket"),
        new(eAutonomousChatIntent.Travel, "help me find npc monster location travel"),
        new(eAutonomousChatIntent.Dungeon, "dungeon entrance cave tomb deeper inside careful pulls"),
        new(eAutonomousChatIntent.Dungeon, "want to run a dungeon clear the corridor"),
        new(eAutonomousChatIntent.Dungeon, "group for spraggon den cursed tomb vendo caves"),
        new(eAutonomousChatIntent.RvR, "rvr frontier keep raid enemies relic defense attack realm points"),
        new(eAutonomousChatIntent.RvR, "albion midgard hibernia fighting at the keep"),
        new(eAutonomousChatIntent.RvR, "any action in emain hadrians odins gate"),
        new(eAutonomousChatIntent.Trading, "buy sell price gear weapon armor auction exchange materials"),
        new(eAutonomousChatIntent.Trading, "does anyone have equipment for sale coins merchant"),
        new(eAutonomousChatIntent.Trading, "realm exchange listing item crafting material"),
        new(eAutonomousChatIntent.Help, "can anyone help me question confused how does this work"),
        new(eAutonomousChatIntent.Help, "need advice can somebody explain please help"),
        new(eAutonomousChatIntent.Help, "what should I do trainer skills equipment"),
        new(eAutonomousChatIntent.Farewell, "bye goodbye farewell safe travels goodnight"),
        new(eAutonomousChatIntent.Farewell, "got to go farewell heading off take care"),
        new(eAutonomousChatIntent.IgnoreOutOfWorld, "politics president election congress democrat republican government"),
        new(eAutonomousChatIntent.IgnoreOutOfWorld, "current news real world country politician vote campaign"),
        new(eAutonomousChatIntent.IgnoreOutOfWorld, "stocks crypto bitcoin market football basketball television movie"),
        new(eAutonomousChatIntent.IgnoreOutOfWorld, "my real job school homework car phone internet social media"),
    ];

    private static readonly HashSet<string> HardOutOfWorld = new(StringComparer.OrdinalIgnoreCase)
    {
        "politics", "political", "president", "presidential", "congress", "democrat", "republican",
        "trump", "biden", "bitcoin", "crypto", "netflix",
        "twitter", "facebook", "instagram", "tiktok", "reddit", "headline", "headlines", "yankees", "nfl", "nba",
        "mlb", "nhl", "superbowl", "senate", "senator", "mortgage",
    };

    /// <summary>
    /// 1.2 chat audit (DESIGN-011, CORPUS-C-017): words that are real-world talk only with a second real-world word ("taxes"
    /// alone is a player's complaint about the bills, "government" a guild's joke). "stock" and "stocks" left the hard list:
    /// "what do you have in stock" and "stock up on arrows" are game talk (crafters say "in stock: ..."), and the sports words
    /// too (a remark about the game last night is real-life talk players had, answered in kind). "stock market" stays.
    /// </summary>
    private static readonly HashSet<string> SoftOutOfWorld = new(StringComparer.OrdinalIgnoreCase)
    {
        "taxes", "government", "election", "football", "basketball", "vote", "voting", "campaign",
    };

    /// <summary>True for a line about the real world the bots stay out of (the hard words, a soft word with a second one, "stock market").</summary>
    internal static bool IsOutOfWorld(string text, IReadOnlyCollection<string> tokens)
    {
        if (tokens.Any(HardOutOfWorld.Contains))
            return true;
        string padded = " " + string.Join(' ', tokens) + " ";
        if (padded.Contains(" stock market ", StringComparison.Ordinal) || padded.Contains(" stocks and ", StringComparison.Ordinal))
            return true;
        return tokens.Where(SoftOutOfWorld.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    /// <summary>Insults; abuse needs one aimed at the listener ("you idiot", "ur garbage"), so "dumb question" or "this camp
    /// is garbage" is not an insult (0.36-se.4: insults are remembered by bots). 1.2 (DESIGN-001): every one of them, not only
    /// the weak ones, must follow "you"/"ur" ("ppl say spiritmasters are useless in groups" is about a class).</summary>
    private static readonly HashSet<string> Insults = new(StringComparer.OrdinalIgnoreCase)
    {
        "dumb", "idiot", "idiots", "moron", "morons", "garbage", "worthless", "pathetic", "loser", "losers", "stupid",
        "dumbass", "trash", "ugly", "scrub", "scrubs", "noobs", "terrible", "awful", "useless",
        // The talking update audit: "u suck", "ur a noob", "who invited this clown" (only aimed at someone, below).
        "suck", "noob", "newb", "clown",
    };

    /// <summary>Always abuse, whoever it is aimed at.</summary>
    private static readonly HashSet<string> HardAbuse = new(StringComparer.OrdinalIgnoreCase) { "stfu", "shutup", "l2p" };

    /// <summary>
    /// Name-calling that is abuse on its own ("idiot", "moron lol", "what an idiot") unless the line says who is meant and it is
    /// not the listener (1.2 chat audit, DESIGN-001: "my guild leader is an idiot but i like him", "that idiot pulled the whole
    /// room, not you" read as abuse of the bot, which then remembered it).
    /// </summary>
    private static readonly HashSet<string> NameCalling = new(StringComparer.OrdinalIgnoreCase)
        { "idiot", "idiots", "moron", "morons", "dumbass" };

    /// <summary>The thing an insult word describes, when it comes next: "dumb question", "stupid lag", "dumb luck" (DESIGN-001).</summary>
    private static readonly HashSet<string> DescribedThings = new(StringComparer.OrdinalIgnoreCase)
    {
        "question", "questions", "idea", "ideas", "luck", "lag", "mob", "mobs", "quest", "quests", "cat", "dog", "game", "patch", "pull",
        "pulls", "thing", "things", "mistake", "move", "bug", "server", "client", "computer", "pc", "keyboard", "mouse", "mez", "spell",
        "zone", "camp", "drop", "drops", "loot", "item", "armor", "armour", "weapon", "pet", "horse", "boat", "door", "wall", "rule",
        "rules", "name", "joke", "of", "for", "to", "about", "in", "at", "on", "with", "named", "boss", "add", "adds", "train",
    };

    /// <summary>
    /// "you idiot", "ur so dumb", "you are all pathetic", "u r such a loser": an insult word aimed at the listener. Not with
    /// "not" between ("ur not stupid"), and not when the next word is what it describes ("can i ask you a stupid question").
    /// </summary>
    private static bool AimedAtListener(string[] words, int i)
    {
        static bool Target(string word) => word is "you" or "u" or "ur" or "youre" or "you're" or "yall";
        return i >= 1 && Target(words[i - 1]) ||
               i >= 2 && Target(words[i - 2]) && words[i - 1] is "are" or "r" or "so" or "such" or "is" or "look" or "smell" or "a" or "an" or "all" ||
               i >= 3 && Target(words[i - 3]) && words[i - 2] is "are" or "r" or "is" or "all" && words[i - 1] is "so" or "such" or "really" or "just" or "all" or "a" or "an" ||
               i >= 4 && Target(words[i - 4]) && words[i - 3] is "are" or "r" && words[i - 2] is "such" or "really" or "so" or "all" or "just" && words[i - 1] is "a" or "an";
    }

    /// <summary>"not", "isnt", "never" within two words before: "ur not stupid", "you are not a loser" (DESIGN-001).</summary>
    private static bool NegatedBefore(string[] words, int i) =>
        i >= 1 && IsNegation(words[i - 1]) || i >= 2 && IsNegation(words[i - 2]) || i >= 3 && IsNegation(words[i - 3]) && words[i - 1] is "a" or "an" or "so" or "that";

    internal static bool IsNegation(string word) => word is "not" or "isnt" or "arent" or "aint" or "never" or "no" or "dont" or "didnt" or "wasnt" or
        "werent" or "doesnt" or "nobody" or "nothing" or "neither" or "nor" or "cant";

    /// <summary>A first- or third-person subject before the word: "my guild leader is an idiot", "that idiot", "im so stupid" (DESIGN-001).</summary>
    private static bool OtherSubjectBefore(string[] words, int i)
    {
        for (int j = 0; j < i; j++)
            if (words[j] is "my" or "his" or "her" or "their" or "our" or "that" or "the" or "this" or "these" or "those" or "im" or "i" or "he" or "she" or
                    "hes" or "shes" or "they" or "theyre" or "him" or "them" or "someone" or "somebody" or "some" or "whoever" or "anyone" or "people" or
                    "ppl" or "everyone" or "everybody" or "says" or "say" or "said" or "called" or "call")
                return true;
        return false;
    }

    private static bool IsAbuse(string[] words)
    {
        if (words.Any(HardAbuse.Contains) || AutonomousChatShortTalk.HasPhrase(words, "shut up", "learn to play", "go away", "get out of my",
                "get lost", "fuck you", "f you", "screw you", "fuck off", "worst healer ever", "worst tank ever", "worst group ever",
                "worst player ever", "who invited this clown", "ur a noob", "youre a noob", "you are a noob", "u r a noob",
                // The holdout check: "ur the worst healer ive ever seen", "thanks for nothing", "wow great pull genius".
                "ur the worst", "youre the worst", "you are the worst", "u r the worst", "u are the worst", "thanks for nothing",
                "thx for nothing", "ty for nothing", "nice one genius", "great pull genius", "good job genius", "way to go genius",
                "nice pull genius", "nice going genius", "great job genius", "real smooth genius",
                // Play test 2026-10-27: "Duggreigan ur guild sucks lol" read as banter, and drew "Howth smells like salt and fresh bread."
                "ur guild sucks", "your guild sucks", "ur guild is trash", "your guild is trash", "ur guild is garbage", "your guild is garbage",
                "ur guild is terrible", "your guild is terrible", "ur guild is a joke", "your guild is a joke",
                // Beta 1.1.2 (blind round 1: read as banter or a question): "ks much?", "stop ksing my camp jerk", "omg ur so bad",
                // "u r teh suck lol", "gg albs, way to zerg", "can u not stand in the fire", a lone "noob" said to someone.
                "ks much", "stop ksing", "quit ksing", "stop ks", "ksing my", "ks er", "kser", "ur so bad", "you re so bad", "youre so bad", "u r so bad",
                "you are so bad", "teh suck", "u suck", "you suck", "way to zerg", "nice zerg", "zerg much", "stand in the fire", "learn 2 play", "l2p noob",
                // 1.2 chat audit: "what a loser", "you are a waste of space", "everyone here is awful".
                "what a loser", "what an idiot", "what a moron", "what a noob", "waste of space", "waste of air", "everyone here is", "everybody here is",
                "all of you are", "you all are", "you guys are", "you people are", "yall are"))
        {
            // "everyone here is" and "you guys are" are abuse only with an insult after them ("everyone here is nice" is not).
            if (!AutonomousChatShortTalk.HasPhrase(words, "everyone here is", "everybody here is", "all of you are", "you all are", "you guys are",
                    "you people are", "yall are") || words.Any(word => Insults.Contains(word) || NameCalling.Contains(word)) ||
                words.Any(HardAbuse.Contains) || AutonomousChatShortTalk.HasPhrase(words, "shut up", "u suck", "you suck", "waste of space", "what a loser",
                    "what an idiot", "what a moron", "what a noob", "ur the worst", "you are the worst", "learn to play"))
                return !NegatedAt(words);
        }
        for (int i = 0; i < words.Length; i++)
        {
            bool nameCalling = NameCalling.Contains(words[i]);
            if (!nameCalling && !Insults.Contains(words[i]))
                continue;
            // "are you a noob friendly guild?", "u a newb too? just started" (review 2026-10-26); "dumb question", "stupid lag" (1.2).
            if (i + 1 < words.Length && (words[i + 1] is "friendly" or "too" or "like" or "guild" or "here" or "also" or "myself" or "either" or "area" ||
                                         DescribedThings.Contains(words[i + 1])))
                continue;
            if (NegatedBefore(words, i))
                continue;
            // 1.2 (DESIGN-001): every insult word must be aimed at the listener ("you idiot", "ur so stupid"); "sorry guys dumb
            // question", "im so stupid, sorry all" and "stupid lag, did you all freeze too" are not abuse.
            if (AimedAtListener(words, i))
                return true;
            // Name-calling said alone ("idiot", "moron lol", "what an idiot") is aimed at whoever heard it, unless the line says who.
            if (nameCalling && !OtherSubjectBefore(words, i) && words.Length <= 4)
                return true;
        }
        return false;
    }

    /// <summary>"ur not the worst", "you dont suck": the abusive phrase negated.</summary>
    private static bool NegatedAt(string[] words) =>
        AutonomousChatShortTalk.HasPhrase(words, "not the worst", "dont suck", "don t suck", "not a waste", "not a loser", "not an idiot", "not so bad",
            "not that bad", "arent so bad", "arent that bad", "not bad");

    /// <summary>Words that carry no topic; the classifier ignores them ("are" made "yellow cons are for warming up" read as abuse).</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "am", "i", "im", "i'm", "you", "u", "your", "ur", "we", "us",
        "our", "they", "them", "their", "it", "its", "it's", "this", "that", "these", "those", "to", "of", "in", "on", "at", "for",
        "with", "and", "or", "but", "so", "if", "then", "just", "too", "very", "do", "does", "did", "have", "has", "had", "my",
        "me", "he", "she", "him", "her", "his", "what", "who", "how", "when", "why", "which", "there", "here", "up", "out", "all",
        "any", "some", "not", "no", "yes", "can", "will", "would", "could", "should", "get", "got", "go", "going", "about", "as",
        "by", "from", "into", "than", "like", "really", "still", "now", "one", "good", "ok", "okay",
    };

    private static readonly IReadOnlyDictionary<eAutonomousChatIntent, HashSet<string>> StrongGameSignals =
        new Dictionary<eAutonomousChatIntent, HashSet<string>>
        {
            [eAutonomousChatIntent.Trading] = new(StringComparer.OrdinalIgnoreCase) { "buy", "sell", "price", "auction", "exchange", "merchant" },
            [eAutonomousChatIntent.Dungeon] = new(StringComparer.OrdinalIgnoreCase) { "dungeon", "tomb", "caves", "cave" },
            [eAutonomousChatIntent.RvR] = new(StringComparer.OrdinalIgnoreCase) { "rvr", "frontier", "relic" },
            // Not a bare "group" or "party": "love this group" talks about one (review 2026-10-26). Requests are read by the word rules.
            [eAutonomousChatIntent.Grouping] = new(StringComparer.OrdinalIgnoreCase) { "invite", "lfg" },
        };

    private static readonly Model Trained = Train();

    /// <summary>True for a holiday wish ("happy halloween", "merry xmas", "god jul").</summary>
    public static bool IsHolidayWish(string text) => !string.IsNullOrEmpty(text) && HolidayWish.IsMatch(text);

    /// <summary>"happy halloween", "merry xmas", "god jul": a holiday wish.</summary>
    private static readonly System.Text.RegularExpressions.Regex HolidayWish = new(
        @"\b((happy|merry|blessed)\s+(halloween|samhain|thanksgiving|turkey day|christmas|xmas|holidays|new ?years?( day| eve)?|easter|valentines?( day)?|st\.? ?patricks?( day)?|midsummer|yule|solstice)|god jul|nollaig shona)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static eAutonomousChatIntent Predict(string text)
    {
        string[] tokens = Tokens(text).ToArray();
        string[] words = Words(text);
        if (words.Length == 0)
            return AutonomousChatShortTalk.TryRead(text, words, out eAutonomousChatIntent symbols) ? symbols : eAutonomousChatIntent.Unknown;
        if (IsOutOfWorld(text, tokens))
            return eAutonomousChatIntent.IgnoreOutOfWorld;
        if (IsAbuse(words) || words.Length == 1 && words[0] is "noob" or "n00b" or "newb" or "nub" or "jerk" or "loser" ||
            words.Length <= 6 && words.Contains("jerk"))
            return eAutonomousChatIntent.Abuse;
        // Beta 1.1.2: an in-character line ("Hail, good sir. What news from Camelot?", "*bows deeply* My lady...", "By Odin's
        // beard...", "Ye be lookin' weary, friend") is answered in kind (blind round 1: 16 of 17 roleplay lines were missed).
        if (AutonomousChatShortTalk.IsRoleplay(text, words))
            return eAutonomousChatIntent.Roleplay;
        // Holiday wishes are greetings (holiday chatter, 2026-10-28): bots answer them with the holiday's own lines.
        if (HolidayWish.IsMatch(text))
            return eAutonomousChatIntent.Greeting;
        if (AutonomousChatShortTalk.TryRead(text, words, out eAutonomousChatIntent shortTalk))
            return shortTalk;
        foreach ((eAutonomousChatIntent intent, HashSet<string> signals) in StrongGameSignals)
        {
            if (tokens.Any(signals.Contains))
                return intent;
        }
        // The classifier decides only from words it was trained on: with none, every intent scores alike and the
        // tie went to the intent with the most examples (real-world talk), which /say ignores (audit 2026-10-18).
        tokens = tokens.Where(token => !StopWords.Contains(token) && Trained.Vocabulary.Contains(token)).ToArray();
        if (tokens.Length == 0)
            return eAutonomousChatIntent.Unknown;

        eAutonomousChatIntent bestIntent = eAutonomousChatIntent.Unknown;
        double best = double.NegativeInfinity;
        double runnerUp = double.NegativeInfinity;
        foreach (eAutonomousChatIntent intent in Trained.Documents.Keys)
        {
            double score = Math.Log((Trained.Documents[intent] + 1d) /
                                    (Trained.DocumentCount + Trained.Documents.Count));
            Dictionary<string, int> counts = Trained.Counts[intent];
            double denominator = Trained.Totals[intent] + Trained.Vocabulary.Count;
            foreach (string token in tokens)
                score += Math.Log((counts.GetValueOrDefault(token) + 1d) / denominator);
            if (score > best)
            {
                runnerUp = best;
                best = score;
                bestIntent = intent;
            }
            else if (score > runnerUp)
            {
                runnerUp = score;
            }
        }
        // Real-world talk is decided by the hard word list alone: /say ignores it, so a guess must not silence a player
        // (0.36-se.4; "lol are you even real" read as real-world talk from the word "real").
        // Abuse too: bots remember an insult, so it must come from the word rules, not a guess ("terrible lag for everyone").
        // The talking update audit (2026-10-26): a guess of a social intent or a group request answered lines that were neither
        // ("whats the command to go anon" read as a greeting, "heading to the bindstone" as goodbye, "do i need SI to get to
        // aegirhamn" as a group request that bots offered to join). Those come from the word rules alone; a guess may only be a
        // topic (dungeon, rvr, travel, grinding, trading, class talk, banter, help).
        return best - runnerUp < 0.18 || bestIntent is eAutonomousChatIntent.IgnoreOutOfWorld or eAutonomousChatIntent.Abuse or
                   eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Grouping or
                   eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or eAutonomousChatIntent.Ding or
                   eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm or
                   eAutonomousChatIntent.Laugh or eAutonomousChatIntent.BotCheck
            ? eAutonomousChatIntent.Unknown : bestIntent;
    }

    private static readonly Regex CraftRequestRegex = new(
        @"\b((can|could|would|will) (you|u|ya|ye) (make|craft|forge|tailor|fletch|sc|spellcraft|imbue)(?! it\b| it to\b| the raid\b| time\b| sure\b)|" +
        @"(make|craft) me (a|an|some|the)\b|(need|lf|looking for|anyone) (a )?(crafter|smith|armorcrafter|weaponcrafter|tailor|fletcher|spellcrafter|alchemist|sc)\b|" +
        @"\blf sc\b|what (do|can) (you|u|ya) (make|craft)|(are|r) (you|u) (a )?crafter|do (you|u) craft|wtb .*\b(set|helm|helmet|gloves|boots|hauberk|jerkin|vest|leggings|sleeves|robe|sword|axe|hammer|bow|staff|shield)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Crafting 2.0 WP8: a line asking a crafter for its work ("can you make a studded helm?", "make me a chain set",
    /// "lf sc", "what can you craft?", "wtb leather gloves lvl 15"). "can you make it to the raid?" is not one. Pure.
    /// </summary>
    public static bool IsCraftRequest(string text) => !string.IsNullOrWhiteSpace(text) && CraftRequestRegex.IsMatch(text);

    /// <summary>
    /// Predict for a line said to a crafter gamebot (WP8's commerce): a craft request is <see cref="eAutonomousChatIntent.Crafting"/>,
    /// anything else what <see cref="Predict"/> says. Predict itself never returns Crafting, so the chat paths keyed on
    /// Trading keep today's behaviour. Pure.
    /// </summary>
    public static eAutonomousChatIntent PredictForCrafter(string text) =>
        IsCraftRequest(text) ? eAutonomousChatIntent.Crafting : Predict(text);

    private static Model Train()
    {
        var counts = new Dictionary<eAutonomousChatIntent, Dictionary<string, int>>();
        var totals = new Dictionary<eAutonomousChatIntent, int>();
        var documents = new Dictionary<eAutonomousChatIntent, int>();
        var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Example example in Training)
        {
            Dictionary<string, int> bag = counts.GetValueOrDefault(example.Intent);
            if (bag == null)
                counts[example.Intent] = bag = new(StringComparer.OrdinalIgnoreCase);
            documents[example.Intent] = documents.GetValueOrDefault(example.Intent) + 1;
            foreach (string token in Tokens(example.Text))
            {
                bag[token] = bag.GetValueOrDefault(token) + 1;
                totals[example.Intent] = totals.GetValueOrDefault(example.Intent) + 1;
                vocabulary.Add(token);
            }
        }
        return new(counts, totals, documents, vocabulary, Training.Length);
    }

    private static IEnumerable<string> Tokens(string text) => TokenRegex().Matches(text?.ToLowerInvariant() ?? string.Empty).Cast<Match>()
        .Select(match => match.Value)
        .Where(token => token.Length > 1);

    /// <summary>Every word, one letter long too ("k", "r", "u"), with digits ("lf2m", "20").</summary>
    internal static string[] Words(string text) => WordRegex().Matches(text?.ToLowerInvariant() ?? string.Empty).Cast<Match>()
        .Select(match => match.Value.Trim('\''))
        .Where(word => word.Length > 0)
        .ToArray();

    [GeneratedRegex("[a-z0-9']+")]
    private static partial Regex WordRegex();

    [GeneratedRegex("[a-z']+")]
    private static partial Regex TokenRegex();
}
