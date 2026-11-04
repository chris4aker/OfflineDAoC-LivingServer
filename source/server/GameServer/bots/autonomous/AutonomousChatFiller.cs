using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Keeps filler out of the bots' chat (Beta 1.1.2): near-copies of a line a channel just carried, and the kinds of line the
/// first blind evaluation marked as the bot tell. Pure rules plus a small ring of recent lines per channel moment.
/// </summary>
public static class AutonomousChatFiller
{
    /// <summary>The share of lore musings a /faction opener may still use (the rest of the time they are passed over).</summary>
    public const double MusingShare = 0.05;

    /// <summary>
    /// Whether an ambient corpus line may be said in this channel now: a lore musing (the blind spot-the-bot judge's top tell,
    /// "the bind stone and i are on first name terms") only now and then as a /faction opener, never in /say or /broadcast;
    /// a crafted line (a joke, a tip, life filler, a long remark) as rarely; the Realm Exchange only in a seller's own listing
    /// (the owner's feature stays; bots don't plug it unasked). Pure but for the random draws.
    /// </summary>
    public static bool AmbientAllowed(VC.Line line, bool faction, Random random) => AmbientFilter(faction, random)(line);

    /// <summary>
    /// The ambient line filter for one opener: whether this opener may be a musing or a crafted line is drawn once, so the
    /// shares hold however many lines the picker looks at (Beta 1.1.2, round 3). Pure but for the draws.
    /// </summary>
    public static Func<VC.Line, bool> AmbientFilter(bool faction, Random random)
    {
        bool crafted = faction && random.NextDouble() < CraftedShare;
        bool musing = faction && random.NextDouble() < MusingShare;
        return line => AmbientFits(line, crafted, musing);
    }

    private static bool AmbientFits(VC.Line line, bool crafted, bool musing)
    {
        if (line == null)
            return false;
        if (MentionsExchange(line.Text) && !line.Needs.Contains("has_listing", StringComparer.Ordinal))
            return false;
        // Beta 1.1.2, round 2: generic agreement is never an opener either.
        if (IsGeneric(line.Text))
            return false;
        // Round 5: no lore recitation unprompted at all; a lore line answers a real question only (the knowledge route).
        if (line.Topic == "lore")
            return false;
        if (IsMusing(line))
            return musing;
        return crafted || !IsCrafted(line);
    }

    private static readonly string[] LifeMarkers =
    [
        // weather
        "freezing", "cold outside", "snow", "snowing", "raining", "rain outside", "thunder", "storm", "hot outside", "heat wave", "weather",
        // school and work
        "school", "test", "exam", "homework", "library", "teacher", "class tomorrow", "college", "boss", "shift", "payday", "office", "commute",
        // home, pets, media, food, family
        "dog", "cat", "puppy", "kitten", "movie", "tv", "show tonight", "pizza", "dinner", "cooking", "coffee", "tea", "wife", "husband", "kids",
        "baby", "mom", "dad", "roommate", "neighbor", "blanket", "couch", "my hands", "checking in", "just saying hi", "say hi", "rough day",
        "long day", "week off", "vacation", "birthday",
    ];

    /// <summary>
    /// A life line: weather, school, work, pets, films, food, family, a check-in ("freezing outside", "aced my test", "just
    /// checking in to say hi"). A guild hears at most one an evening (round 7). Pure.
    /// </summary>
    public static bool IsLife(VC.Line line) =>
        line != null && (line.Kind == "guild" && line.Topic == "social" || (line.Invites ?? string.Empty).StartsWith("player_rl", StringComparison.Ordinal) ||
                         line.Topic == "reallife" || IsLife(line.Text));

    /// <inheritdoc cref="IsLife(VC.Line)"/>
    public static bool IsLife(string text)
    {
        string padded = " " + string.Join(' ', AutonomousChatIntentModel.Words(text ?? string.Empty)) + " ";
        return LifeMarkers.Any(marker => padded.Contains(" " + marker + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a written guild line may be said in this guild now (round 7: life filler came back through the written guild
    /// lines): short and plain, not generic, its idea rested, a life line only when the guild heard none this evening, and a
    /// written line at most once in 20 minutes. Pure but for the memory read.
    /// </summary>
    public static bool GuildCorpusFits(VC.Line line, AutonomousChatFieldTalk.Memory memory, long now) =>
        line != null && !IsCrafted(line) && !IsGeneric(line.Text) && memory.WrittenAllowed(now) && !memory.Seen(Skeleton(line.Text)) &&
        !memory.Rested(null, IdeaOf(line), now) && (!IsLife(line) || memory.LifeAllowed(now));

    /// <summary>A written guild line was said: its idea rests, the guild's written gap starts, and a life line uses up the evening's one.</summary>
    public static void NoteGuildCorpus(VC.Line line, AutonomousChatFieldTalk.Memory memory, long now)
    {
        if (line == null)
            return;
        memory.Use(null, IdeaOf(line), now);
        memory.Add(Skeleton(line.Text));
        memory.NoteWritten(now);
        if (IsLife(line))
            memory.NoteLife(now);
    }

    private static readonly (string Concept, string[] Words)[] Concepts =
    [
        ("loot", ["drop", "drops", "dropped", "loot", "looted", "looting", "roll", "rolled", "ninja"]),
        ("camp", ["camp", "camped", "camping", "spot", "spawn", "spawns", "respawn"]),
        ("pull", ["pull", "pulls", "pulling", "puller", "train", "trained", "aggro", "add", "adds"]),
        ("heal", ["heal", "heals", "healer", "healers", "cleric", "druid", "rez", "rezz", "res"]),
        ("death", ["died", "die", "dead", "death", "rip", "corpse", "gravestone"]),
        ("xp", ["xp", "exp", "level", "levels", "lvl", "ding", "dinged", "bubble", "bubbles", "grind"]),
        ("craft", ["craft", "crafting", "crafter", "smith", "armorcraft", "spellcraft", "tailor", "fletch", "alchemy", "materials", "skill"]),
        ("money", ["gold", "plat", "money", "coin", "coins", "copper", "silver", "broke", "cost", "price", "prices"]),
        ("gear", ["armor", "armour", "sword", "weapon", "shield", "cloak", "gear", "helm", "boots", "gloves", "ring"]),
        ("pet", ["pet", "pets"]),
        ("bags", ["bag", "bags", "inventory", "full"]),
        ("rvr", ["keep", "keeps", "relic", "relics", "frontier", "rvr", "rps", "siege", "door", "zerg"]),
        ("travel", ["horse", "stable", "route", "walk", "port", "ride"]),
        ("mana", ["mana", "power", "pow", "oom"]),
        ("group", ["group", "grp", "duo", "solo", "soloing", "groups"]),
        ("lag", ["lag", "laggy", "crash", "crashed", "server"]),
        ("food", ["food", "dinner", "eat", "lunch", "snack"]),
        ("night", ["night", "late", "bed", "sleep", "tired"]),
    ];

    private static readonly HashSet<string> IdeaStop = new(StringComparer.Ordinal)
    {
        "the", "and", "that", "this", "with", "from", "have", "just", "your", "they", "them", "then", "than", "when", "what", "were", "will",
        "would", "could", "should", "about", "always", "never", "every", "still", "really", "there", "here", "some", "more", "into", "like",
        "only", "been", "being", "dont", "cant", "wont", "thats", "its", "youre", "anyone", "everyone", "again", "twice", "today", "tonight",
    };

    /// <summary>
    /// What a written line is about, so its rewordings rest with it (round 5: "check every drop twice" three times in four
    /// minutes): its topic and up to two concepts it names (loot, camp, pull, heal...), else its longest content word. Pure.
    /// </summary>
    public static string IdeaOf(VC.Line line) => line == null ? null : IdeaOf(line.Topic, line.Text);

    /// <inheritdoc cref="IdeaOf(VC.Line)"/>
    public static string IdeaOf(string topic, string text)
    {
        string[] words = AutonomousChatIntentModel.Words(text ?? string.Empty);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string concept, string[] list) in Concepts)
            if (words.Any(word => list.Contains(word)))
                found.Add(concept);
        string subject = found.Count > 0 ? string.Join("+", found.Take(2))
            : words.Where(word => word.Length >= 4 && !IdeaStop.Contains(word) && !word.StartsWith('{')).OrderByDescending(word => word.Length)
                .FirstOrDefault() ?? "misc";
        return (topic ?? "line") + ":" + subject;
    }

    /// <summary>The share of /faction openers that may still be a crafted line (round 3; the rest come from the field, see AutonomousChatFieldTalk).</summary>
    public const double CraftedShare = 0.12;

    /// <summary>The share of joke one-liners a /faction opener may still use (round 2; round 3 folds them into the crafted share).</summary>
    public const double QuipShare = CraftedShare;

    private static readonly HashSet<string> CraftedTopics = new(StringComparer.Ordinal)
    {
        "lore", "general", "class", "classes", "realm_pride", "places", "places_here", "veteran", "latenight", "reallife", "explorer",
        "daredevil", "leveler", "socialite", "nolifer", "crafter", "boast", "banter", "roles", "newbie", "lowlevel", "capital", "town",
        "solo", "evening", "night", "waiting", "mobs", "leveling", "crafting", "money", "gripe", "dungeon", "travel",
        "frontier", "frontier_alb", "frontier_mid", "frontier_hib", "battlegrounds",
    };

    private static readonly string[] CraftedMarkers =
    [
        // unprompted tips
        "always ", "never pull", "remember to", "dont forget", "don't forget", "make sure", "worth every", "is worth it", "the key is", "tip:",
        "trick is", "golden rule", "rule:", "you should", "u should", "if you ", "if u ", "best way to", "learned the hard way", "the hard way",
        // life filler
        " work", " job", "my boss", " kids", "my wife", "my husband", "girlfriend", "boyfriend", "school", "homework", "coffee", "my cat", "my dog",
        "weekend", "monday", "the wife", "my mom", "my dad",
        // sentimental check-ins and life filler in guild chat (round 4)
        "best part of my", "here for you", "rough week", "rough day", "missed the", "regret nothing", "just saying", "reminder:", "reminder ",
        "love this guild", "love you guys", "you guys are", "family", "proud of", "grateful", "means a lot", "soda", "snack", "pizza", " beer",
        " tea ", "baby", " kid ", "asleep", "typing quietly", "typing very", "finals", "exams", "my brain", "how is everyone", "hows everyone", "how's everyone", "everyones night", "everyone's night",
        "hope everyone", "hope you all", "hope yall", "took a week off", "a week off", "my week", "long week", "hugs", "<3", "not a dare", "pro move", "life hack", "fun fact",
        // narration
        "feels like", "nothing like", "best kind of", "smells like", "i miss", "makes the time", "is louder than", "like a ", "like geese",
    ];

    /// <summary>
    /// A written line that reads as written: a joke, a tip, life filler, narration, or anything over nine words (round 3: real
    /// ambient lines were short and about the game in front of you). Pure.
    /// </summary>
    public static bool IsCrafted(VC.Line line)
    {
        if (line == null)
            return false;
        if (line.Invites == "joke")
            return true;
        if (line.Kind is "ambient" or "rvr" or "camp" or "group" && line.Topic != null && CraftedTopics.Contains(line.Topic))
            return true;
        string text = line.Text ?? string.Empty;
        // A written guild line counts as plain only when it is short and has no trailing "..." (round 4).
        if (line.Kind == "guild" && (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8 || text.Contains("...") || text.Contains('…')))
            return true;
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 9)
            return true;
        string padded = " " + text.ToLowerInvariant() + " ";
        return CraftedMarkers.Any(marker => padded.Contains(marker, StringComparison.Ordinal));
    }

    private static readonly string[] MusingMarkers =
    [
        "makes everyone", "on first name terms", "is where most", "haunts", "nothing says", "there is something about", "they say the",
        "legend says", "the old stories", "an old skald", "an old guildmate swore", "heard lady", "is older than", "built over the bones",
        "people forget that", "every realm has", "you know youre", "you know you are",
        // Beta 1.1.2, round 2: preachy etiquette and guidebook observations.
        "basic manners", "is basic", "etiquette", "people should", "nobody should", "you should always", "always ask before",
        "or so they say", "or so sylvans claim", "or so the", "sees the whole", "they claim", "is said to", "the bards say",
        "folk say", "old timers say", "rule of thumb", "the secret to", "the trick is", "pro tip",
    ];

    /// <summary>A line that narrates the game instead of playing it: a lore line, or an observation in the voice of a guidebook. Pure.</summary>
    public static bool IsMusing(VC.Line line)
    {
        if (line == null)
            return false;
        if (line.Kind is "ambient" or "reply" && line.Topic == "lore")
            return true;
        string text = line.Text.ToLowerInvariant();
        return MusingMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>The Realm Exchange named in a line. Pure.</summary>
    public static bool MentionsExchange(string text) =>
        !string.IsNullOrEmpty(text) && System.Text.RegularExpressions.Regex.IsMatch(text, @"\bexchange\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"where can i sell this", "where do i buy armor", "anyone buying?": a question about where to buy or sell. Pure.</summary>
    public static bool AsksWhereToBuyOrSell(string incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(incoming) + " ";
        return System.Text.RegularExpressions.Regex.IsMatch(t,
            @" (where|how) (can|do|should|would) (i|you|u|we) (buy|sell|get rid of|offload|find|trade) | where to (buy|sell) | (best|good) (place|way) to (buy|sell) | (exchange|auction|consignment) | whos buying | who is buying | anyone buying | where (is|are) (the )?(merchant|vendor|shop)s? ");
    }

    private static readonly HashSet<string> GenericLines = new(StringComparer.Ordinal)
    {
        "good question", "happy to help", "sounds familiar", "good observation", "you and me both", "just thinking out loud", "mhm anyway back to it", "thinking out loud",
        "this exactly", "cant argue with that", "fair enough", "interesting", "true story", "so true", "same here honestly", "i hear that",
        "i hear you", "glad to help", "tell me more", "could be", "if you say so", "uh huh", "mhm", "well said", "makes sense", "fair point",
        "i was wondering that too", "good to know", "noted", "right", "indeed",
        // Beta 1.1.2, round 2 (the spot-the-bot judge's most common reason).
        "could be tell me more", "shout someone might be close", "hey how is everyone doing tonight", "how is everyone doing tonight",
        "sure thing", "yeah that is about right", "that is about right", "yeah thats about right", "anyway back to it", "mhm anyway",
        "interesting thought", "good point", "very true", "so true", "lol so true", "true that", "cant disagree", "cant argue there",
        "fair", "i suppose", "i guess so", "maybe maybe not", "if you say so", "you never know", "who knows",
        // Beta 1.1.2, round 5: canned agreement after a musing ("i know right", "yeah its always like that").
        "i know right", "ikr", "yeah its always like that", "its always like that", "always like that", "yep that is how it goes",
        "thats how it goes", "that is how it goes", "yep thats how it goes", "story of my life", "tell me about it", "you said it",
        "isnt that the truth", "aint that the truth", "so true lol", "too true", "yep yep", "right there with you", "same here",
        // Beta 1.1.2, round 3: agreement filler between bots in /faction and /say.
        "cant say youre wrong", "cant say you are wrong", "that is pretty accurate", "pretty accurate", "thats accurate", "so accurate", "accurate",
        "this is true", "thats true", "that is true", "true enough", "fair enough i guess", "cant argue", "couldnt agree more",
        "you got that right", "ya got that right", "spot on", "exactly", "exactly this", "pretty much", "pretty much yeah", "basically",
    };

    /// <summary>
    /// Filler that fits any line ("good question", "happy to help", "sounds familiar", "this exactly"): allowed at most once per
    /// channel per hour (the blind round 1 judge's generic-ack tell). Pure.
    /// </summary>
    public static bool IsGeneric(string text)
    {
        string skeleton = Skeleton(text);
        return skeleton.Length > 0 && GenericLines.Contains(skeleton);
    }

    public const long GenericRestMs = 60 * 60_000;

    private static readonly Dictionary<string, long> GenericUsed = new(StringComparer.Ordinal);

    /// <summary>Whether a generic line may be said in this channel now (none in the last hour).</summary>
    public static bool GenericAllowed(string channelKey, long now)
    {
        if (string.IsNullOrEmpty(channelKey))
            return true;
        lock (GenericUsed)
            return !GenericUsed.TryGetValue(channelKey, out long at) || now - at >= GenericRestMs || at > now;
    }

    /// <summary>A generic line was said in this channel.</summary>
    public static void NoteGeneric(string channelKey, long now)
    {
        if (string.IsNullOrEmpty(channelKey))
            return;
        lock (GenericUsed)
        {
            if (GenericUsed.Count > 4096)
                GenericUsed.Clear();
            GenericUsed[channelKey] = now;
        }
    }

    /// <summary>What a reply is answering, while it is picked (set by the reply path, read by the line filter).</summary>
    public sealed class ReplyScope : IDisposable
    {
        public string Incoming { get; }
        public string ChannelKey { get; }
        public long Now { get; }
        private readonly ReplyScope _outer;

        internal ReplyScope(string incoming, string channelKey, long now, ReplyScope outer) =>
            (Incoming, ChannelKey, Now, _outer) = (incoming, channelKey, now, outer);

        public void Dispose() => _current = _outer;
    }

    [ThreadStatic]
    private static ReplyScope _current;

    /// <summary>The reply being picked on this thread, or null.</summary>
    public static ReplyScope Current => _current;

    /// <summary>Marks the reply being picked on this thread: what it answers and in which channel (dispose to end).</summary>
    public static ReplyScope Replying(string incoming, string channelKey, long now) => _current = new ReplyScope(incoming, channelKey, now, _current);

    /// <summary>
    /// Whether a reply or closing line may answer now: no lore musing, the Realm Exchange only to someone asking where to buy or
    /// sell, a generic line at most once per channel per hour. Pure but for the generic memory.
    /// </summary>
    public static bool ReplyAllowed(VC.Line line, ReplyScope scope)
    {
        if (line == null)
            return false;
        if (IsMusing(line))
            return false;
        if (MentionsExchange(line.Text) && !AsksWhereToBuyOrSell(scope?.Incoming))
            return false;
        // Beta 1.1.2, round 2: generic agreement is removed from replies altogether.
        if (IsGeneric(line.Text))
            return false;
        return true;
    }

    #region Near-duplicates

    private static readonly HashSet<string> Interjections = new(StringComparer.Ordinal)
    {
        "ha", "haha", "hah", "heh", "hehe", "lol", "lmao", "rofl", "yes", "yep", "yeah", "ya", "yea", "yup", "oh", "hmm", "hm", "ah", "ok", "k", "kk",
        "so", "well", "wow", "man", "dude", "honestly", "tbh", "imo", "hey", "hi", "yo", "and", "but", "just", "really", "pretty", "very",
        // what is left of a smiley (":D", ":P", "XD")
        "d", "p", "xd",
    };

    /// <summary>
    /// A line's skeleton for spotting near-copies: lower case, letters and digits only, numbers as "#", smileys and
    /// leading or trailing interjections dropped ("Ha yes, this exactly." and "Heh yep, this exactly." are both "this
    /// exactly"). Pure.
    /// </summary>
    public static string Skeleton(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var words = new List<string>(12);
        var word = new StringBuilder(12);
        void Flush()
        {
            if (word.Length > 0)
                words.Add(word.ToString());
            word.Clear();
        }
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetter(c))
                word.Append(c);
            else if (char.IsDigit(c))
            {
                if (word.Length == 0 || word[^1] != '#')
                    word.Append('#');
            }
            else if (c != '\'')
                Flush();
        }
        Flush();
        int start = 0, end = words.Count;
        while (start < end && Interjections.Contains(words[start]))
            start++;
        while (end > start && Interjections.Contains(words[end - 1]))
            end--;
        // A line made only of interjections ("lol", "heh yeah") keeps its words: short reactions may repeat.
        return start >= end ? string.Join(' ', words) : string.Join(' ', words.GetRange(start, end - start));
    }

    /// <summary>
    /// The last lines each channel moment carried, by skeleton, for half an hour: a near-copy of one of them is not said
    /// again there. Reactions of one or two words ("lol", "gz", "omw") are exempt, as players repeat those all the time.
    /// </summary>
    public sealed class RecentTexts
    {
        public const int PerChannel = 48;
        public const long KeepMs = 30 * 60_000;

        private sealed class Ring
        {
            public readonly string[] Skeletons = new string[PerChannel];
            public readonly long[] At = new long[PerChannel];
            public int Next;
        }

        private readonly Dictionary<long, Ring> _rings = new();

        /// <summary>True (and remembered) unless a near-copy was said in this channel moment in the last half hour.</summary>
        public bool TryAdd(long channel, string text, long now)
        {
            string skeleton = Skeleton(text);
            if (skeleton.Length == 0)
                return true;
            bool reaction = skeleton.IndexOf(' ') < 0 || skeleton.Split(' ').Length <= 2 && skeleton.Length <= 10;
            lock (_rings)
            {
                if (!_rings.TryGetValue(channel, out Ring ring))
                {
                    if (_rings.Count > 4096)
                        _rings.Clear();
                    _rings[channel] = ring = new Ring();
                }
                if (!reaction)
                    for (int i = 0; i < PerChannel; i++)
                        if (ring.Skeletons[i] == skeleton && now - ring.At[i] < KeepMs && ring.At[i] <= now)
                            return false;
                ring.Skeletons[ring.Next] = skeleton;
                ring.At[ring.Next] = now;
                ring.Next = (ring.Next + 1) % PerChannel;
                return true;
            }
        }

        public void Clear()
        {
            lock (_rings)
                _rings.Clear();
        }
    }

    /// <summary>The server's recent lines.</summary>
    public static readonly RecentTexts Recent = new();

    #endregion
}
