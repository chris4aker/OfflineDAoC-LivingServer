using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// 1.1.3, an evening that remembers itself (owner's PC, RC3: topics never came back, nobody asked "did you ever find that
/// camp?", a bot that argued in a debate never mentioned it again, a bot that died to something never heard about it). Each
/// realm keeps a light short-term memory of what was really said in its /faction tonight: who asked for a group or a camp,
/// whose death was told and to what, who argued with whom and about what. Now and then, between 12 minutes and three hours
/// later, a line refers back to one of them: a regular asks the bot that asked, and the bot answers from what is true for it
/// now (grouped or not, at a camp or not). Only things the channel really carried are remembered: a note is made when its
/// line was said (<see cref="Heard"/>, from AutonomousBotChatCoordinator.BroadcastFactionAsWritten), never when it was
/// planned. The store and the words are pure; AutonomousLivingServer.Engage.cs plays the follow-ups under the channel's
/// target, as src=followup exchanges. Behind bot voices (bot_voices_enabled), like the lines it refers back to.
/// </summary>
public static class AutonomousChatRecall
{
    /// <summary>What a note remembers.</summary>
    public enum Kind { AskedGroup, AskedCamp, Died, Debated }

    /// <summary>
    /// One thing a realm's /faction carried: <paramref name="BotId"/> said it (the subject of the follow-up), about
    /// <paramref name="Subject"/> (the monster, the debate's topic); <paramref name="OtherId"/> is the other side of a debate.
    /// </summary>
    public sealed record Note(Kind Kind, long BotId, string BotName, string Subject, long At, long OtherId = 0, string OtherName = null)
    {
        public bool Used { get; set; }
    }

    /// <summary>A note is first brought up this long after it happened (it has scrolled away by then)...</summary>
    public const long MinAgeMs = 12 * 60_000;

    /// <summary>...and is forgotten after an evening.</summary>
    public const long EveningMs = 3 * 60 * 60_000L;

    /// <summary>A realm hears a follow-up at most once in 6-12 minutes.</summary>
    public const long RestMinMs = 6 * 60_000;
    public const long RestSpreadMs = 6 * 60_000;

    /// <summary>How many notes a realm keeps.</summary>
    public const int Capacity = 96;

    /// <summary>A realm's notes of the evening. Thread-safe.</summary>
    public sealed class Store
    {
        private readonly List<Note> _notes = new();
        private readonly Dictionary<long, (Kind Kind, string Subject, long Until)> _expected = new();
        private readonly Dictionary<long, (long A, string AName, long B, string BName, string Topic, bool Done)> _debates = new();
        private long _nextAt = long.MinValue / 2;

        public int Count
        {
            get { lock (_notes) return _notes.Count; }
        }

        public IReadOnlyList<Note> Notes
        {
            get { lock (_notes) return _notes.ToArray(); }
        }

        public void Add(Note note)
        {
            if (note == null || note.BotId <= 0)
                return;
            lock (_notes)
            {
                _notes.RemoveAll(old => note.At - old.At > EveningMs || old.At > note.At);
                // One note per bot and kind and subject: the newest.
                _notes.RemoveAll(old => old.BotId == note.BotId && old.Kind == note.Kind && string.Equals(old.Subject, note.Subject, StringComparison.OrdinalIgnoreCase));
                _notes.Add(note);
                if (_notes.Count > Capacity)
                    _notes.RemoveAt(0);
            }
        }

        /// <summary>
        /// A bot said in its thread that it found what it asked for ("got one ty"): nothing is left to ask after (RC10 on the owner's
        /// PC: "any healers on?" "me" "got one ty", and 24 minutes later "u ever find a grp earlier?" "nah, ended up soloing").
        /// </summary>
        public void Settle(long botId, Kind kind)
        {
            lock (_notes)
                _notes.RemoveAll(note => note.BotId == botId && note.Kind == kind);
        }

        /// <summary>A story is about to be told: its teller's first line, once heard, makes a <see cref="Kind.Died"/> note.</summary>
        public void ExpectStory(long tellerId, string monster, long now)
        {
            if (tellerId <= 0 || string.IsNullOrWhiteSpace(monster))
                return;
            lock (_notes)
            {
                foreach (long stale in _expected.Where(pair => pair.Value.Until < now).Select(pair => pair.Key).ToArray())
                    _expected.Remove(stale);
                _expected[tellerId] = (Kind.Died, monster, now + 2 * 60_000);
            }
        }

        /// <summary>
        /// A /faction line was said: confirms an expected story, and collects a debate's two sides (a note once both have
        /// spoken). <paramref name="topic"/> is the running debate's topic (null outside one).
        /// </summary>
        public void Heard(string feature, long exchange, string role, long botId, string botName, string topic, long now)
        {
            if (botId <= 0 || feature == null)
                return;
            if (feature == "story" && role == "teller")
            {
                (Kind Kind, string Subject, long Until) expected;
                lock (_notes)
                {
                    if (!_expected.TryGetValue(botId, out expected) || expected.Until < now)
                        return;
                    _expected.Remove(botId);
                }
                Add(new Note(expected.Kind, botId, botName, expected.Subject, now));
                return;
            }
            if (feature != "debate" || string.IsNullOrWhiteSpace(topic) || role is not ("A" or "B"))
                return;
            Note made = null;
            lock (_notes)
            {
                if (_debates.Count > 32)
                    _debates.Clear();
                _debates.TryGetValue(exchange, out var debate);
                if (debate.Done)
                    return;
                if (role == "A" && debate.A == 0)
                    debate = debate with { A = botId, AName = botName, Topic = topic };
                else if (role == "B" && debate.B == 0)
                    debate = debate with { B = botId, BName = botName, Topic = debate.Topic ?? topic };
                if (debate.A != 0 && debate.B != 0 && debate.A != debate.B)
                {
                    debate = debate with { Done = true };
                    made = new Note(Kind.Debated, debate.A, debate.AName, debate.Topic, now, debate.B, debate.BName);
                }
                _debates[exchange] = debate;
            }
            if (made != null)
                Add(made);
        }

        /// <summary>The notes that may be brought up now: unused, at least <see cref="MinAgeMs"/> old, within the evening.</summary>
        public Note[] Due(long now)
        {
            lock (_notes)
                return _notes.Where(note => !note.Used && now - note.At >= MinAgeMs && now - note.At <= EveningMs).ToArray();
        }

        /// <summary>How many deaths of this bot the channel heard tonight.</summary>
        public int Deaths(long botId, long now)
        {
            lock (_notes)
                return _notes.Count(note => note.Kind == Kind.Died && note.BotId == botId && now - note.At <= EveningMs);
        }

        /// <summary>Whether the realm's rest between follow-ups is over; if so, starts the next rest.</summary>
        public bool TryTurn(long now, Random random)
        {
            lock (_notes)
            {
                if (now < _nextAt)
                    return false;
                _nextAt = now + RestMinMs + (long)(random.NextDouble() * RestSpreadMs);
                return true;
            }
        }

        public void MarkUsed(Note note)
        {
            lock (_notes)
                note.Used = true;
        }
    }

    private static readonly Dictionary<eRealm, Store> Stores = new();

    /// <summary>The live store of a realm.</summary>
    public static Store Of(eRealm realm)
    {
        lock (Stores)
        {
            if (!Stores.TryGetValue(realm, out Store store))
                Stores[realm] = store = new Store();
            return store;
        }
    }

    /// <summary>Whether a line says its speaker found the group or the player it asked for ("got one ty", "ty, inv"). Pure.</summary>
    public static bool SaysFound(string text) => !string.IsNullOrWhiteSpace(text) &&
        System.Text.RegularExpressions.Regex.IsMatch(text.ToLowerInvariant(), @"\b(got one|got a (grp|group)|found one|inv sent|ty,? inv|kk inv)\b");

    /// <summary>What a field line asked about, if it is something a follow-up can ask after ("u ever find a grp?"). Pure.</summary>
    public static Kind? AskedOf(string fieldKind) => fieldKind switch
    {
        "lfg" or "ask_group" => Kind.AskedGroup,
        "camp" or "ask_xp" => Kind.AskedCamp,
        _ => null,
    };

    /// <summary>A debate topic as players say it ("death_penalty" is "the death penalty").</summary>
    public static string TopicWords(string topic) => topic switch
    {
        null or "" => null,
        "housing" => "houses",
        "realm_abilities" => "realm abilities",
        "death_penalty" => "the death penalty",
        "loot_rules" => "loot rules",
        "keep_claims" => "keep claims",
        "named_drops" => "named drops",
        "grey_xp" => "grey con xp",
        "mez_breaks" => "breaking mez",
        "off_hours" => "off hours raids",
        "pets_rvr" => "pets in rvr",
        "bind_points" => "bind points",
        "leveling_route" => "leveling routes",
        "group_setup" => "group setups",
        _ => topic.Replace('_', ' '),
    };

    /// <summary>What a follow-up's subject is like now, for its truthful answer.</summary>
    public sealed record Now(bool Grouped, bool AtCamp, string Zone);

    /// <summary>
    /// The two lines of a follow-up: the asker's (naming the subject) and the subject's answer, true for it now. Null when the
    /// note gives nothing to say. <paramref name="deaths"/>: how many of the subject's deaths the channel heard tonight. Pure.
    /// </summary>
    public static (string Ask, string Answer)? FollowUp(Note note, Now now, int deaths, Random random)
    {
        if (note == null || string.IsNullOrWhiteSpace(note.BotName))
            return null;
        string name = note.BotName.ToLowerInvariant();
        string Pick(params string[] lines) => lines[random.Next(lines.Length)];
        switch (note.Kind)
        {
            case Kind.AskedGroup:
                return (Pick($"{name} u ever find a grp earlier?", $"{name} did u get a grp in the end?", $"hey {name}, find that grp u were after?"),
                    now.Grouped ? Pick("ya, been grouped since, ty", "yep, good grp too", "ya got one not long after")
                        : Pick("nah, ended up soloing", "nope, gave up and went solo", "not yet, still looking tbh"));
            case Kind.AskedCamp:
                return (Pick($"{name} did u ever find a camp?", $"{name} u find a spot in the end?", $"{name} how did the camp hunt go?"),
                    now.AtCamp && !string.IsNullOrWhiteSpace(now.Zone) ? Pick($"found one in {now.Zone}, its decent", $"good, camping in {now.Zone} now")
                        : Pick("not great, still wandering around", "everything was camped, still looking", "no luck yet tbh"));
            case Kind.Died:
                if (string.IsNullOrWhiteSpace(note.Subject))
                    return null;
                string kind = AutonomousChatEngagement.Plural(note.Subject.ToLowerInvariant());
                return (deaths >= 2
                        ? Pick($"{name} how many deaths is that tonight, {deaths}?", $"{name} ur {deaths} deaths in tonight lol")
                        : Pick($"{name} staying away from {kind} now? lol", $"{name} hows the war with the {kind} going", $"{name}, any more trouble with {kind}?"),
                    deaths >= 2 ? Pick("dont count lol", "its not my night ok", "stop keeping score lol")
                        : Pick("dont remind me lol", $"{kind} 1, me 0", "its personal now", "ive made my peace with them lol"));
            case Kind.Debated:
                string topic = TopicWords(note.Subject);
                if (topic == null || string.IsNullOrWhiteSpace(note.OtherName))
                    return null;
                return (Pick($"{name} still think ur wrong about {topic} btw", $"been thinking about {topic} {name}, still disagree with u",
                        $"{name}, still standing by what u said about {topic}?"),
                    Pick("lol agree to disagree", "not changing my mind on that one", "yes. and im still right", "we can argue again tomorrow"));
        }
        return null;
    }

    /// <summary>The note a realm brings up next: the newest due one whose bots are online (by <paramref name="online"/>). Pure.</summary>
    public static Note Choose(IReadOnlyList<Note> due, Func<long, bool> online, Random random)
    {
        if (due == null || due.Count == 0)
            return null;
        Note[] fits = due.Where(note => online(note.BotId) && (note.Kind != Kind.Debated || online(note.OtherId))).ToArray();
        if (fits.Length == 0)
            return null;
        // A little shuffled, newer first: the evening's latest topics come back most.
        return fits.OrderByDescending(note => note.At + (long)(random.NextDouble() * 30 * 60_000)).First();
    }
}
