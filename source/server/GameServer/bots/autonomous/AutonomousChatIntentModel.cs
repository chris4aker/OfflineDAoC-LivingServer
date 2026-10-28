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
        "politics", "political", "president", "election", "congress", "democrat", "republican", "government",
        "trump", "biden", "bitcoin", "crypto", "stock", "stocks", "football", "basketball", "netflix",
        "twitter", "facebook", "instagram", "tiktok", "reddit", "headline", "headlines", "taxes", "yankees", "nfl", "nba",
        "mlb", "nhl", "superbowl", "senate", "senator", "mortgage",
    };

    /// <summary>Insults; abuse needs one aimed at someone ("you idiot", "ur garbage"), so "dumb question" or "this camp
    /// is garbage" is not an insult (0.36-se.4: insults are remembered by bots).</summary>
    private static readonly HashSet<string> Insults = new(StringComparer.OrdinalIgnoreCase)
    {
        "dumb", "idiot", "idiots", "moron", "morons", "garbage", "worthless", "pathetic", "loser", "losers", "stupid",
        "dumbass", "trash", "ugly", "scrub", "scrubs", "noobs", "terrible", "awful", "useless",
        // The talking update audit: "u suck", "ur a noob", "who invited this clown" (only aimed at someone, below).
        "suck", "noob", "newb", "clown",
    };

    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        "you", "u", "ur", "your", "youre", "you're", "yall", "everyone", "everybody", "guys", "ppl", "people", "all",
    };

    /// <summary>Always abuse, whoever it is aimed at.</summary>
    private static readonly HashSet<string> HardAbuse = new(StringComparer.OrdinalIgnoreCase)
        { "stfu", "shutup", "idiot", "idiots", "moron", "morons", "l2p", "dumbass" };

    /// <summary>Words that are insults only right after "you"/"ur" ("you are terrible", not "terrible lag for everyone").</summary>
    private static readonly HashSet<string> WeakInsults = new(StringComparer.OrdinalIgnoreCase)
        { "trash", "awful", "terrible", "garbage", "ugly", "suck", "noob", "newb", "clown",
          // The holdout check: "ppl say spiritmasters are useless in groups, agree?" is about a class, not an insult.
          "useless" };

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
                "ur guild is terrible", "your guild is terrible", "ur guild is a joke", "your guild is a joke"))
            return true;
        if (!words.Any(Targets.Contains))
            return false;
        for (int i = 0; i < words.Length; i++)
        {
            if (!Insults.Contains(words[i]))
                continue;
            // "are you a noob friendly guild?", "u a newb too? just started" (review 2026-10-26)
            if (i + 1 < words.Length && words[i + 1] is "friendly" or "too" or "like" or "guild" or "here" or "also" or "myself" or "either" or "zone" or "area")
                continue;
            if (!WeakInsults.Contains(words[i]))
                return true;
            // "youre trash", "you are terrible", "ur so awful" (not "where do u sell trash loot")
            static bool Target(string word) => word is "you" or "u" or "ur" or "youre" or "you're" or "yall";
            if (i >= 1 && Target(words[i - 1]) ||
                i >= 2 && Target(words[i - 2]) && words[i - 1] is "are" or "r" or "so" or "such" or "is" or "look" or "smell" or "a" ||
                i >= 3 && Target(words[i - 3]) && words[i - 2] is "are" or "r" && words[i - 1] is "so" or "such" or "really" or "just")
                return true;
        }
        return false;
    }

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
        @"\b((happy|merry|blessed)\s+(halloween|samhain|thanksgiving|turkey day|christmas|xmas|holidays|new year|easter|valentines?( day)?|st\.? ?patricks?( day)?|midsummer|yule|solstice)|god jul|nollaig shona)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static eAutonomousChatIntent Predict(string text)
    {
        string[] tokens = Tokens(text).ToArray();
        string[] words = Words(text);
        if (words.Length == 0)
            return AutonomousChatShortTalk.TryRead(text, words, out eAutonomousChatIntent symbols) ? symbols : eAutonomousChatIntent.Unknown;
        if (tokens.Any(HardOutOfWorld.Contains))
            return eAutonomousChatIntent.IgnoreOutOfWorld;
        if (IsAbuse(words))
            return eAutonomousChatIntent.Abuse;
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
