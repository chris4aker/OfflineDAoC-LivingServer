using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Conversation memory (1.2, owner 2026-11-18 09:14: "It should be in 1.2"; the chat audit's DESIGN-040). Before it the only state
/// across turns was each channel's last bot lines, so a player's second question went to whichever bot spoke, "how do i get
/// there" had no "there", and a bot asked the level the player had just given. A <see cref="Conversation"/> is kept per (player,
/// bot) and per (player, channel) for <see cref="KeepMs"/>: its turns, the bot's open question, the things in play (place, item,
/// class, level) and which bot has the floor. Pure apart from the clock the caller passes in and a lock; unit-tested
/// (UT_ChatConversation). Every kind of bot that talks to a player uses it: gamebots, hired companions and buffbots.
/// </summary>
public sealed class ChatConversations
{
    /// <summary>How long a conversation is kept after its last turn.</summary>
    public const long KeepMs = 5 * 60_000;

    /// <summary>How long after a bot's line to the player it keeps the floor: the player's next line goes to it.</summary>
    public const long FloorMs = 45_000;

    /// <summary>Turns kept per conversation.</summary>
    public const int MaxTurns = 12;

    public sealed record Turn(bool ByPlayer, long BotId, string Who, string Text, long At);

    /// <summary>One conversation: with one bot, or in one channel.</summary>
    public sealed class Conversation
    {
        public readonly List<Turn> Turns = new();
        public string Place { get; internal set; }
        public string Item { get; internal set; }
        public string ClassName { get; internal set; }
        public int? Level { get; internal set; }
        /// <summary>The bot that spoke to the player last, and when.</summary>
        public long FloorBot { get; internal set; }
        public string FloorName { get; internal set; }
        public long FloorAt { get; internal set; }
        /// <summary>The bot's question the player has not answered yet, or null.</summary>
        public string OpenQuestion { get; internal set; }
        public long OpenQuestionBot { get; internal set; }
        public long LastAt { get; internal set; }
    }

    private readonly Dictionary<string, Conversation> _byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The store the server uses.</summary>
    public static readonly ChatConversations Live = new();

    private static string BotKey(string player, long botId) => "bot:" + player + "|" + botId;
    private static string ChannelKey(string player, string channel) => "ch:" + player + "|" + channel;

    /// <summary>Forgets everything (tests).</summary>
    public void Clear()
    {
        lock (_byKey)
            _byKey.Clear();
    }

    private Conversation Get(string key, long now, bool create)
    {
        if (_byKey.TryGetValue(key, out Conversation found) && now - found.LastAt <= KeepMs && now >= found.LastAt)
            return found;
        if (found != null)
            _byKey.Remove(key);
        if (!create)
            return null;
        if (_byKey.Count > 4_000)
            foreach (string old in _byKey.Where(pair => now - pair.Value.LastAt > KeepMs).Select(pair => pair.Key).ToList())
                _byKey.Remove(old);
        return _byKey[key] = new Conversation { LastAt = now };
    }

    /// <summary>The conversation between a player and a bot, or null when there is none.</summary>
    public Conversation With(string player, long botId, long now)
    {
        if (string.IsNullOrEmpty(player) || botId <= 0)
            return null;
        lock (_byKey)
            return Get(BotKey(player, botId), now, false);
    }

    /// <summary>The player's conversation in a channel ("faction:Albion", "say:12", "group", "tell:..."), or null.</summary>
    public Conversation In(string player, string channel, long now)
    {
        if (string.IsNullOrEmpty(player) || string.IsNullOrEmpty(channel))
            return null;
        lock (_byKey)
            return Get(ChannelKey(player, channel), now, false);
    }

    /// <summary>
    /// A player's line: a turn in the channel's conversation (and, when <paramref name="toBot"/> is given or a bot has the floor,
    /// in that bot's), with the things it names. An answer to the bot's open question closes it.
    /// </summary>
    public void NotePlayer(string player, string channel, string text, long now, long toBot = 0)
    {
        if (string.IsNullOrEmpty(player) || string.IsNullOrEmpty(channel) || string.IsNullOrWhiteSpace(text))
            return;
        lock (_byKey)
        {
            Conversation room = Get(ChannelKey(player, channel), now, true);
            long bot = toBot > 0 ? toBot : room.FloorBot > 0 && now - room.FloorAt <= FloorMs ? room.FloorBot : 0;
            foreach (Conversation c in new[] { room, bot > 0 ? Get(BotKey(player, bot), now, true) : null })
            {
                if (c == null)
                    continue;
                Add(c, new Turn(true, 0, player, text.Trim(), now));
                Learn(c, text);
                if (c.OpenQuestion != null && !AutonomousChatQuestions.IsQuestion(text))
                    c.OpenQuestion = null;
            }
        }
    }

    /// <summary>A bot's line to the player: a turn, the floor, and its question if it asked one.</summary>
    public void NoteBot(string player, long botId, string botName, string channel, string text, long now)
    {
        if (string.IsNullOrEmpty(player) || botId <= 0 || string.IsNullOrWhiteSpace(text))
            return;
        lock (_byKey)
        {
            bool asked = text.TrimEnd().EndsWith('?');
            foreach (Conversation c in new[] { Get(BotKey(player, botId), now, true), string.IsNullOrEmpty(channel) ? null : Get(ChannelKey(player, channel), now, true) })
            {
                if (c == null)
                    continue;
                Add(c, new Turn(false, botId, botName, text.Trim(), now));
                c.FloorBot = botId;
                c.FloorName = botName;
                c.FloorAt = now;
                if (asked)
                {
                    c.OpenQuestion = text.Trim();
                    c.OpenQuestionBot = botId;
                }
                // The bot's own line can name the place in play ("try the bone camp in muspelheim"): "how do i get there" means it.
                if (PlaceIn(text) is { } place)
                    c.Place = place;
            }
        }
    }

    /// <summary>
    /// The place in play in a channel's conversation, set by name (1.2.3: a bot-led group's "welcome! meet up at Humberton
    /// Castle" names a town the places catalog doesn't hold, so "where is that" had nothing to complete and drew "not sure tbh").
    /// </summary>
    public void NotePlace(string player, string channel, string place, long now)
    {
        if (string.IsNullOrEmpty(player) || string.IsNullOrEmpty(channel) || string.IsNullOrWhiteSpace(place))
            return;
        lock (_byKey)
            Get(ChannelKey(player, channel), now, true).Place = place.Trim();
    }

    private static void Add(Conversation c, Turn turn)
    {
        c.Turns.Add(turn);
        if (c.Turns.Count > MaxTurns)
            c.Turns.RemoveAt(0);
        c.LastAt = turn.At;
    }

    /// <summary>
    /// The bot that has the floor with this player in this channel: the last bot that spoke to them, within
    /// <see cref="FloorMs"/> (a second line goes to the same bot), or 0.
    /// </summary>
    public long FloorHolder(string player, string channel, long now)
    {
        Conversation c = In(player, channel, now);
        return c != null && c.FloorBot > 0 && now - c.FloorAt <= FloorMs ? c.FloorBot : 0;
    }

    // ---- what a line names -----------------------------------------------------------------------------------------------------

    private static readonly Regex LevelPattern = new(
        @"\b(?:lvl|level|lv|im|i m|i am)\s*(\d{1,2})\b|\b(\d{1,2})\s*(?:now|here)\b|^\s*(\d{1,2})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ItemPattern = new(
        @"\b(?:wtb|wts|make|craft|buy|sell|need|want|selling|buying|pc on|price on|for)\s+(?:a |an |some |the |me a |me an |me some )?((?:[a-z]+ ){0,3}(?:helm|helmet|gloves|boots|hauberk|jerkin|vest|leggings|legs|sleeves|arms|robe|sword|axe|hammer|mace|bow|staff|shield|cloak|ring|necklace|bracer|belt|dagger|spear|polearm|scythe|set|armor|armour|pots|potions|dye|dyes|arrows))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string PlaceIn(string text)
    {
        try
        {
            AutonomousChatPlaces.Place place = AutonomousChatQuestions.PlaceNamed(text, AutonomousChatPlaces.All);
            return place == null ? null : place.Aliases.FirstOrDefault() ?? place.Key;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The level a line gives ("lvl 30", "32 now", a bare "32", "30 cleric"), or null. Pure.</summary>
    public static int? LevelIn(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string t = AutonomousChatQuestions.Normalize(text);
        Match match = LevelPattern.Match(t);
        for (int g = 1; match.Success && g < match.Groups.Count; g++)
            if (match.Groups[g].Success && int.TryParse(match.Groups[g].Value, out int level) && level is >= 1 and <= 50)
                return level;
        // "30 cleric", "lfg 24 druid": a number right before a class word.
        string[] words = t.Split(' ');
        for (int i = 0; i + 1 < words.Length; i++)
            if (int.TryParse(words[i], out int before) && before is >= 1 and <= 50 && AutonomousChatShortTalk.NamesAClass(words[i + 1]))
                return before;
        return null;
    }

    /// <summary>The item a line asks about or offers ("wtb a studded helm" -> "studded helm"), or null. Pure.</summary>
    public static string ItemIn(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match match = ItemPattern.Match(AutonomousChatQuestions.Normalize(text));
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void Learn(Conversation c, string text)
    {
        // Realism 2c (#480): "sorry i meant spindelhalla not varulvhamn" puts spindelhalla in play, not the place it took back.
        if (CorrectionIn(text) is { } fix)
            text = fix.Meant + (fix.Rest != null ? " " + fix.Rest : "");
        if (PlaceIn(text) is { } place)
            c.Place = place;
        if (ItemIn(text) is { } item)
            c.Item = item;
        if (AutonomousChatFieldTalk.ClassNamed(text) is { } cls)
            c.ClassName = cls;
        if (LevelIn(text) is { } level)
            c.Level = level;
    }

    // ---- using what the conversation holds -------------------------------------------------------------------------------------

    /// <summary>
    /// A player's line completed from the conversation (DESIGN-040): "how do i get there" -> "how do i get to muspelheim", "what
    /// about there" -> "what about muspelheim", "is it far" -> "is muspelheim far", "how much for it" -> "how much for studded helm".
    /// The line itself when there is nothing to complete. Pure.
    /// </summary>
    public static string Resolve(string text, Conversation c) => Resolve(text, c, 0);

    /// <summary>
    /// <see cref="Resolve(string, Conversation)"/> for an asker of <paramref name="askerLevel"/> (0: not known). Realism 2c (#480;
    /// channels.md finding 2, questions.md finding 8): every channel runs this before it reads the line, so a follow-up is completed
    /// from the conversation before it is classified, in /faction, /say, tells, /group and to a companion alike:
    /// <list type="bullet">
    /// <item>a nudge ("anyone?", "nobody knows?", "hello??") re-asks the player's question nobody answered;</item>
    /// <item>a correction ("i meant spindelhalla not varulvhamn. what lvl", "i said 12 not 21") asks again about what was meant;</item>
    /// <item>"it / that / there / one / them / they" goes to the thing in play of the kind the question asks about (a place, a
    /// class, an item), and only when the line names nothing of that kind itself.</item>
    /// </list>
    /// Pure.
    /// </summary>
    public static string Resolve(string text, Conversation c, int askerLevel)
    {
        if (string.IsNullOrWhiteSpace(text) || c == null)
            return text;
        if (IsNudge(text))
            return UnansweredQuestion(c, text) is { } open ? Complete(open, c, askerLevel) ?? open : text;
        if (CorrectionIn(text) is { } fix)
            return Corrected(fix, text, c, askerLevel) ?? text;
        return Complete(text, c, askerLevel) ?? text;
    }

    // ---- follow-ups: the thing in play, by kind ----------------------------------------------------------------------------------

    /// <summary>Words a follow-up opens with that change nothing ("ok", "ty", "never heard of it").</summary>
    private const string LeadIn = @"^(?:(?:ok|k|kk|so|and|hmm|um|ty|thanks|thx|oh|ah|cool|nice|never heard of it|sorry|sry)\s+)*";

    private static readonly Regex ThereQuestion = new(
        LeadIn + @"(how (?:do|can|would) (?:i|we|you|u) get (?:there|to it|to that)|how (?:far|long) (?:is it|to get there)|what about there|is it far|where is (?:it|that)|wheres (?:it|that)|which way(?: is it)?|any good|is it good)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ItItem = new(@"\b(how much(?: for)?|price|pc|what stats|can you make|can u make|got any)\s+(it|that|one|them)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string It = @"(?:it|that|there|that place|the place|in there|the mobs there|the mobs)";
    private const string They = @"(?:they|them|those|one|em|that class|that one|a one)";
    private const string Thing = @"(?:it|that|one|them|those|em|that one)";

    /// <summary>Place follow-ups the <see cref="ThereQuestion"/> forms don't cover, and what each one asks in full.</summary>
    private static readonly (Regex Asks, string Kind)[] PlaceFollowUps =
    [
        (new Regex(LeadIn + @"(?:what|wat|whats|what s) (?:lvl|level|lvls|levels)(?: range)? (?:is|are|r) " + It + @"(?: for)?$"), "level"),
        (new Regex(LeadIn + @"(?:what|whats) (?:lvl|level) (?:do|should|would) (?:i|u|you|we) (?:need|be) (?:for|at|to go|to go to|in) " + It + "$"), "level"),
        (new Regex(LeadIn + @"(?:what|whats) (?:lvl|level) (?:for|is good for) " + It + "$"), "level"),
        (new Regex(LeadIn + @"where(?: exactly| at| is it exactly| is that exactly)?$"), "where"),
        (new Regex(LeadIn + @"(?:wheres|where is) (?:it|that) at$"), "where"),
        (new Regex(LeadIn + @"(?:what|which) zone(?: is)? (?:it|that)(?: in)?$"), "where"),
        (new Regex(LeadIn + @"how far(?: away)? is (?:that|there|that place)$"), "far"),
        (new Regex(LeadIn + @"(?:how (?:do|can|would) (?:i|we|you|u) find (?:it|that|the place)|(?:whats )?(?:the )?best way (?:there|to get there)|how to get there|way there)$"), "way"),
        (new Regex(LeadIn + @"is (?:it|that|there) (?:any good|good|good xp|worth it|hard|tough|too hard|easy|ok for me|good for me)(?: for xp| for me)?$"), "fit"),
        (new Regex(LeadIn + @"(?:any good for xp|good xp|worth it)$"), "fit?"),
        (new Regex(LeadIn + @"(?:can|could) (?:i|u|you|we) (?:solo|duo) (?:it|that|there|that place|in there)$"), "solo"),
    ];

    /// <summary>Class follow-ups ("who trains them?" after "thinking of rolling a minstrel"), and what each asks in full.</summary>
    private static readonly (Regex Asks, string Kind)[] ClassFollowUps =
    [
        (new Regex(LeadIn + @"(?:who|where) (?:trains|teaches|do i train|can i train) " + They + "$"), "trains"),
        (new Regex(LeadIn + @"(?:what|which|whats the|whats a good|what is the best|whats the best) spec(?:s)? (?:for|on|is best for|should i go for|do i go for) " + They + "$"), "spec"),
        (new Regex(LeadIn + @"how (?:do|should|would) (?:i|u|you) spec (?:" + They + "|it)$"), "spec"),
        (new Regex(LeadIn + @"what (?:armor|armour) (?:do|does|can|will) " + They + @" (?:wear|use|get)$"), "armor"),
        (new Regex(LeadIn + @"(?:are|r) (?:they|them|those) (?<how>any good|good|ok|decent|fun|strong)(?<rest> solo| for solo| in groups| in a group| in rvr| at solo)?$"), "good"),
        (new Regex(LeadIn + @"(?:can|do) (?:they|them|those|one) solo(?: well| ok)?$"), "solo"),
    ];

    /// <summary>Item follow-ups ("who sells them?" after "wtb a studded helm"), and what each asks in full.</summary>
    private static readonly (Regex Asks, string Kind)[] ItemFollowUps =
    [
        (new Regex(LeadIn + @"(?:who|where|anyone) (?:sells|sell|has|have) " + Thing + "$"), "sells"),
        (new Regex(LeadIn + @"where (?:do|can|would|should) (?:i|u|you|we) (?:get|buy|find) " + Thing + "$"), "get"),
        (new Regex(LeadIn + @"(?:who|anyone) (?:makes|make|can make|crafts|can craft) " + Thing + "$"), "makes"),
    ];

    /// <summary>The line completed from the thing in play of the kind it asks about, or null when there is nothing to complete.</summary>
    private static string Complete(string text, Conversation c, int askerLevel)
    {
        string t = AutonomousChatQuestions.Normalize(text);
        string q = text.TrimEnd().EndsWith('?') ? "?" : "";
        bool namesPlace = PlaceIn(text) != null;
        if (c.Place != null && ThereQuestion.Match(t) is { Success: true } there)
        {
            string asked = there.Groups[1].Value;
            // #480: "any good?", "is it good xp?" from an asker whose level is known ask whether it fits them ("barrows good for 24s?"
            // -> "fine at 24, mobs are 37-47"); "is barrows good" went to the lore ("the shade of Uther walks the barrows").
            if (asked is "any good" or "is it good" && askerLevel is >= 1 and <= 50 && !namesPlace && !Regex.IsMatch(t, @"\d"))
                return $"{c.Place} good for {askerLevel}s" + q;
            string done = Regex.IsMatch(asked, @"^how (do|can|would)") ? Regex.Replace(asked, @"get (there|to it|to that)", "get to " + c.Place)
                : Regex.IsMatch(asked, @"^how (far|long)") ? "how far is " + c.Place
                : asked.StartsWith("what about", StringComparison.Ordinal) ? "what about " + c.Place
                : asked == "is it far" ? "is " + c.Place + " far"
                : asked.StartsWith("where", StringComparison.Ordinal) || asked.StartsWith("which way", StringComparison.Ordinal) ? "where is " + c.Place
                : "is " + c.Place + " good";
            return done + q;
        }
        if (c.Item != null && ItItem.IsMatch(t))
            return ItItem.Replace(t, match => match.Groups[1].Value + " " + c.Item) + q;
        if (c.Place != null && !namesPlace)
            foreach ((Regex asks, string kind) in PlaceFollowUps)
                // A bare "good xp" or "worth it" asks only with its "?" (without one it is a remark).
                if (asks.IsMatch(t) && (kind != "fit?" || q.Length > 0))
                    return PlaceQuestion(kind.TrimEnd('?'), c.Place, askerLevel) + q;
        if (c.ClassName != null && AutonomousChatFieldTalk.ClassNamed(text) == null)
            foreach ((Regex asks, string kind) in ClassFollowUps)
                if (asks.Match(t) is { Success: true } m)
                    return ClassQuestion(kind, c.ClassName.ToLowerInvariant(), m) + q;
        if (c.Item != null && ItemIn(text) == null)
            foreach ((Regex asks, string kind) in ItemFollowUps)
                if (asks.IsMatch(t))
                    return (kind switch { "sells" => "who sells ", "get" => "where do i get ", _ => "who makes " }) + c.Item + q;
        return null;
    }

    /// <summary>A place follow-up asked in full, the way the same question is answered when typed out. Pure.</summary>
    private static string PlaceQuestion(string kind, string place, int askerLevel) => kind switch
    {
        "level" => "what lvl is " + place,
        "where" => "where is " + place,
        "far" => "how far is " + place,
        "way" => "how do i get to " + place,
        "fit" => askerLevel is >= 1 and <= 50 ? $"{place} good for {askerLevel}s" : "what lvl is " + place,
        "solo" => askerLevel is >= 1 and <= 50 ? $"can a {askerLevel} solo in {place}" : "what lvl is " + place,
        _ => place,
    };

    /// <summary>A class follow-up asked in full ("who trains minstrels"). Pure.</summary>
    private static string ClassQuestion(string kind, string cls, Match m) => kind switch
    {
        "trains" => "who trains " + ClassPlural(cls),
        "spec" => "what spec for " + cls,
        "armor" => $"what armor does {AutonomousBotVoiceCorpus.AOrAn(cls)} {cls} wear",
        "good" => $"are {ClassPlural(cls)} {m.Groups["how"].Value}{m.Groups["rest"].Value}",
        _ => $"can {ClassPlural(cls)} solo",
    };

    /// <summary>A class name's plural as chat writes it ("minstrels", "armsmen", "mercenaries", "heroes"). Pure.</summary>
    public static string ClassPlural(string cls)
    {
        string c = (cls ?? string.Empty).Trim().ToLowerInvariant();
        return c switch
        {
            "" => c,
            "armsman" => "armsmen",
            "hero" => "heroes",
            "eldritch" => "eldritches",
            _ when c.EndsWith('y') => c[..^1] + "ies",
            _ => c + "s",
        };
    }

    // ---- nudges ------------------------------------------------------------------------------------------------------------------

    /// <summary>How long a question waits for a nudge to re-ask it, and how soon one may (a reply may still be on its way).</summary>
    public const long NudgeWindowMs = 3 * 60_000, NudgeAfterMs = 15_000;

    private static readonly Regex Nudge = new(
        @"^(?:(?:so|well|um|hmm|uh|lol|really)\s+)*(?:anyone|any1|ne1|anybody|anyone at all|anyone home|anyone there|anybody there|hello|helo|hellooo*|bump|bumping|nobody|noone|no one|nobody knows|no one knows|noone knows|nobody know|nobody can help|no one can help|noone can help|no one can help me|nobody can help me|no one knows this|nobody knows this|no help|no ideas|still looking|still need help|guys)$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a line only nudges an earlier question: "anyone?", "hello??", "nobody knows?", "so no one can help", "bump", a bare
    /// "?" (questions.md finding 8: all silent before #480). "hello" with no "?" is a greeting, not a nudge. Pure.
    /// </summary>
    public static bool IsNudge(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string raw = text.Trim();
        string t = AutonomousChatQuestions.Normalize(raw);
        if (t.Length == 0)
            return raw.All(ch => ch == '?');
        if (!Nudge.IsMatch(t))
            return false;
        return !Regex.IsMatch(t, @"^(?:hello|helo|hellooo*|guys)$") || raw.EndsWith('?');
    }

    /// <summary>
    /// The player's last question in the conversation that nothing answered: no bot line came after it, it is not itself a nudge,
    /// and it was asked between <see cref="NudgeAfterMs"/> and <see cref="NudgeWindowMs"/> before the nudge. Null otherwise. Pure.
    /// </summary>
    public static string UnansweredQuestion(Conversation c, string nudge)
    {
        if (c == null || c.Turns.Count == 0)
            return null;
        Turn last = c.Turns[^1];
        long at = last.ByPlayer && last.Text == (nudge ?? string.Empty).Trim() ? last.At : c.LastAt;
        for (int i = c.Turns.Count - 1; i >= 0; i--)
        {
            Turn turn = c.Turns[i];
            if (!turn.ByPlayer)
                return null;
            if (IsNudge(turn.Text))
                continue;
            if (!AutonomousChatQuestions.IsQuestion(turn.Text) || CorrectionIn(turn.Text) != null)
                return null;
            long waited = at - turn.At;
            return waited >= NudgeAfterMs && waited <= NudgeWindowMs ? turn.Text : null;
        }
        return null;
    }

    // ---- corrections -------------------------------------------------------------------------------------------------------------

    /// <summary>A correction: what the player meant, what it replaces (or null) and a question it carries (or null).</summary>
    public sealed record Correction(string Meant, string Instead, string Rest);

    private static readonly Regex CorrectionPattern = new(
        @"^(?:(?:sorry|sry|soz|oops|no|nah|err|er|um|lol|wait|ah|oh)\s+)*(?:i meant|i mean|meant|i said)\s+(?<x>.+?)(?:\s+not\s+(?<y>.+?))?(?:\s+(?<rest>(?:what|whats|wat|where|wheres|how|is|are|can|who|which|when|any)\b.*))?$",
        RegexOptions.CultureInvariant);

    /// <summary>"sorry i meant spindelhalla not varulvhamn. what lvl" -> (spindelhalla, varulvhamn, "what lvl"); null for any other line. Pure.</summary>
    public static Correction CorrectionIn(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match m = CorrectionPattern.Match(AutonomousChatQuestions.Normalize(text));
        if (!m.Success)
            return null;
        string x = m.Groups["x"].Value.Trim();
        // "i meant to say ty", "i mean it": not a correction of a question.
        if (x.Length == 0 || Regex.IsMatch(x, @"^(to|it|that|this|what i said|well|nothing|no offense|no harm)\b"))
            return null;
        return new Correction(x, m.Groups["y"].Success ? m.Groups["y"].Value.Trim() : null, m.Groups["rest"].Success ? m.Groups["rest"].Value.Trim() : null);
    }

    /// <summary>A correction asked again: its own question about what was meant, or the last question with the meant thing in it.</summary>
    private static string Corrected(Correction fix, string text, Conversation c, int askerLevel)
    {
        string q = text.TrimEnd().EndsWith('?') ? "?" : "";
        if (fix.Rest != null)
        {
            // "i meant spindelhalla. what lvl": the question is about what was meant.
            var meant = new Conversation
            {
                Place = PlaceIn(fix.Meant) ?? c.Place, Item = ItemIn("wtb " + fix.Meant) ?? c.Item,
                ClassName = AutonomousChatFieldTalk.ClassNamed(fix.Meant) ?? c.ClassName, Level = c.Level,
            };
            if (PlaceIn(fix.Meant) is { } place && Regex.IsMatch(fix.Rest, @"^(?:what|wat|whats) (?:lvl|level|lvls|levels)$"))
                return "what lvl is " + place + q;
            return Complete(fix.Rest + q, meant, askerLevel);
        }
        string before = LastPlayerQuestion(c, text);
        if (before == null)
            return null;
        string b = AutonomousChatQuestions.Normalize(before);
        string bq = before.TrimEnd().EndsWith('?') ? "?" : q;
        // "i said 12 not 21", "i meant mids not hibs": the word it takes back is in the question.
        if (fix.Instead != null && Regex.IsMatch(b, @"\b" + Regex.Escape(fix.Instead) + @"\b"))
            return Regex.Replace(b, @"\b" + Regex.Escape(fix.Instead) + @"\b", fix.Meant) + bq;
        // "i meant spindelhalla": the place in the question becomes the one meant.
        if (PlaceIn(fix.Meant) != null)
        {
            AutonomousChatPlaces.Place old = SafePlace(before);
            string said = old?.Aliases.Append(old.Key).Select(AutonomousChatQuestions.Normalize).Where(a => a.Length >= 3)
                .OrderByDescending(a => a.Length).FirstOrDefault(a => Regex.IsMatch(b, @"\b" + Regex.Escape(a) + @"\b"));
            if (said != null)
                return Regex.Replace(b, @"\b" + Regex.Escape(said) + @"\b", fix.Meant) + bq;
        }
        // "i meant minstrel": the class in the question becomes the one meant.
        if (AutonomousChatFieldTalk.ClassNamed(fix.Meant) is { } cls && AutonomousChatFieldTalk.ClassNamed(before) is { } was &&
            !string.Equals(cls, was, StringComparison.OrdinalIgnoreCase))
        {
            string w = was.ToLowerInvariant();
            foreach (string form in new[] { ClassPlural(w), w })
                if (Regex.IsMatch(b, @"\b" + Regex.Escape(form) + @"\b"))
                    return Regex.Replace(b, @"\b" + Regex.Escape(form) + @"\b", form == w ? cls.ToLowerInvariant() : ClassPlural(cls)) + bq;
        }
        return null;
    }

    /// <summary>The player's last question before this line, within the conversation (a correction re-asks it). Pure.</summary>
    private static string LastPlayerQuestion(Conversation c, string text)
    {
        string self = (text ?? string.Empty).Trim();
        bool skipped = false;
        for (int i = c.Turns.Count - 1; i >= 0; i--)
        {
            Turn turn = c.Turns[i];
            if (!turn.ByPlayer)
                continue;
            if (!skipped && turn.Text == self)
            {
                skipped = true;
                continue;
            }
            if (IsNudge(turn.Text) || CorrectionIn(turn.Text) != null)
                continue;
            return AutonomousChatQuestions.IsQuestion(turn.Text) ? turn.Text : null;
        }
        return null;
    }

    private static AutonomousChatPlaces.Place SafePlace(string text)
    {
        try
        {
            return AutonomousChatQuestions.PlaceNamed(text, AutonomousChatPlaces.All);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly Regex AsksLevel = new(@"\b(what (lvl|level)|level are (you|u)|lvl are (you|u)|how high are (you|u)|what level r u|whats (ur|your) (lvl|level))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AsksClass = new(@"\b(what class|which class|what are (you|u) playing|what r u playing|what do (you|u) play)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AsksPlace = new(@"\b(where (are|r) (you|u)|where (you|u) at|what zone|which zone|where at|where is that|where\?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AsksItem = new(@"\b(what (item|do you need|do u need|are you looking for|r u looking for)|what (kind|piece) of (armor|armour|weapon))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when a reply asks what the conversation already holds: the level, class, place or item the player gave (DESIGN-040:
    /// a bot never asks what the conversation holds). Pure.
    /// </summary>
    public static bool AsksWhatItHolds(string reply, Conversation c)
    {
        if (string.IsNullOrWhiteSpace(reply) || c == null)
            return false;
        return c.Level != null && AsksLevel.IsMatch(reply) || c.ClassName != null && AsksClass.IsMatch(reply) ||
               c.Place != null && AsksPlace.IsMatch(reply) && c.Turns.Any(turn => turn.ByPlayer && PlaceIn(turn.Text) != null) ||
               c.Item != null && AsksItem.IsMatch(reply);
    }

    /// <summary>The conversation's two views merged for one reply: what the player told this bot, and what they said in the channel.</summary>
    public Conversation For(string player, long botId, string channel, long now)
    {
        Conversation withBot = With(player, botId, now), room = In(player, channel, now);
        if (withBot == null || room == null)
            return withBot ?? room;
        lock (_byKey)
        {
            var merged = new Conversation
            {
                Place = withBot.Place ?? room.Place, Item = withBot.Item ?? room.Item, ClassName = withBot.ClassName ?? room.ClassName,
                Level = withBot.Level ?? room.Level, FloorBot = room.FloorBot, FloorName = room.FloorName, FloorAt = room.FloorAt,
                OpenQuestion = withBot.OpenQuestion, OpenQuestionBot = withBot.OpenQuestionBot, LastAt = Math.Max(withBot.LastAt, room.LastAt),
            };
            merged.Turns.AddRange(withBot.Turns.Concat(room.Turns).Distinct().OrderBy(turn => turn.At));
            return merged;
        }
    }
}
