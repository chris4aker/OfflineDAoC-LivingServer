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

    private static readonly Regex ThereQuestion = new(
        @"^(?:(?:ok|k|so|and|hmm|um|ty|thanks|thx)\W*\s*)?(how (?:do|can|would) (?:i|we|you|u) get (?:there|to it|to that)|how (?:far|long) (?:is it|to get there)|what about there|is it far|where is (?:it|that)|wheres (?:it|that)|which way(?: is it)?|any good|is it good)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ItItem = new(@"\b(how much(?: for)?|price|pc|what stats|can you make|can u make|got any)\s+(it|that|one|them)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A player's line completed from the conversation (DESIGN-040): "how do i get there" -> "how do i get to muspelheim", "what
    /// about there" -> "what about muspelheim", "is it far" -> "is muspelheim far", "how much for it" -> "how much for studded helm".
    /// The line itself when there is nothing to complete. Pure.
    /// </summary>
    public static string Resolve(string text, Conversation c)
    {
        if (string.IsNullOrWhiteSpace(text) || c == null)
            return text;
        string t = AutonomousChatQuestions.Normalize(text);
        if (c.Place != null && ThereQuestion.Match(t) is { Success: true } there)
        {
            string asked = there.Groups[1].Value;
            string done = Regex.IsMatch(asked, @"^how (do|can|would)") ? Regex.Replace(asked, @"get (there|to it|to that)", "get to " + c.Place)
                : Regex.IsMatch(asked, @"^how (far|long)") ? "how far is " + c.Place
                : asked.StartsWith("what about", StringComparison.Ordinal) ? "what about " + c.Place
                : asked == "is it far" ? "is " + c.Place + " far"
                : asked.StartsWith("where", StringComparison.Ordinal) || asked.StartsWith("which way", StringComparison.Ordinal) ? "where is " + c.Place
                : "is " + c.Place + " good";
            return done + (text.TrimEnd().EndsWith('?') ? "?" : "");
        }
        if (c.Item != null && ItItem.IsMatch(t))
            return ItItem.Replace(t, match => match.Groups[1].Value + " " + c.Item) + (text.TrimEnd().EndsWith('?') ? "?" : "");
        return text;
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
