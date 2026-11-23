using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// The bot-voices corpus (docs/bot-voices/DESIGN.md): pre-written, era-checked lines and scenes,
/// built by tools/bot-voices/build_corpus.py into corpus.json, plus the writing styles in
/// voices.json. Pure: parsing, filtering, slot filling, styles and the no-repeat window, with no
/// game objects, so all of it is unit-tested. Nothing is generated freely.
/// </summary>
public sealed class AutonomousBotVoiceCorpus
{
    public const int MaxLength = 160;

    public sealed record Line(string Id, string Kind, string Topic, string[] Who, string Realm, string[] Classes,
        int LevelLow, int LevelHigh, string[] Needs, string Intent, string Mood, double Weight, string Text)
    {
        /// <summary>An opener's invitation: what kind of answer it asks for (none, chat, joke, boast, ask_where...).</summary>
        public string Invites { get; init; } = string.Empty;

        /// <summary>A reply's or closing's answers: the opener invitations it fits.</summary>
        public string[] Answers { get; init; } = [];

        /// <summary>The line names a place only to talk about it ("@about place"), not to say the speaker is there.</summary>
        public bool About { get; init; }

        /// <summary>The template the line is one way of saying ("[a|b]" choices), or empty (Beta 1.1.2).</summary>
        public string Family { get; init; } = string.Empty;
    }

    public sealed record Beat(string Role, string[] Variants, bool Optional, int PauseSeconds)
    {
        public bool IsPause => Role == "~";
    }

    /// <summary>Who may play a scene role: personality types, classes and, for lines like "i am not 50 yet", levels.</summary>
    public sealed record RoleSpec(string[] Types, string[] Classes, int LevelLow = 1, int LevelHigh = 50, string Task = null)
    {
        public bool LevelFits(int level) => level >= LevelLow && level <= LevelHigh;

        /// <summary>"@role A task=rvr": the role tells what its bot is doing in the frontier, so only a bot out for rvr plays it.</summary>
        public bool TaskFits(bool rvrTask) => Task != "rvr" || rvrTask;
    }

    public sealed record Scene(string Id, string Topic, string Realm, int LevelLow, int LevelHigh,
        IReadOnlyDictionary<string, RoleSpec> Roles, Beat[] Beats)
    {
        /// <summary>Time-of-day flags the scene needs ("night", "!day"); empty for any time.</summary>
        public string[] Needs { get; init; } = [];

        /// <summary>A role that logs off when the scene plays to its end ("@exit A": the dunce who rage quits); empty for none.</summary>
        public string Exit { get; init; } = string.Empty;

        public string[] SpeakingRoles => Beats.Where(beat => !beat.IsPause).Select(beat => beat.Role).Distinct().ToArray();
    }

    public sealed record Voice(string Name, double Share, bool Capitalize, bool CapitalizeSentences, bool EndPeriod,
        string[] NoPeriodAfter, bool StripPunctuation, (string From, string To)[] Abbreviate,
        IReadOnlyDictionary<string, string> Expand, string[] Emotes, double EmoteChance, double TypoChance);

    /// <summary>Who is speaking, as plain values (built from a live bot by AutonomousBotVoices).</summary>
    public sealed record Speaker(long Id, string Name, eRealm Realm, string ClassName, int Level, BotPersonalityType Type,
        IReadOnlySet<string> Flags);

    public Line[] Lines { get; }
    public Scene[] Scenes { get; }
    public Voice[] Voices { get; }
    private readonly Dictionary<string, Line> _byId;

    private AutonomousBotVoiceCorpus(Line[] lines, Scene[] scenes, Voice[] voices)
    {
        Lines = lines;
        Scenes = scenes;
        Voices = voices;
        _byId = new Dictionary<string, Line>(StringComparer.Ordinal);
        foreach (Line line in lines)
            _byId.TryAdd(line.Id, line);
    }

    /// <summary>The corpus with more lines (the lore bible's, Living Server 1.2); a line whose id is already there is left out.</summary>
    public AutonomousBotVoiceCorpus WithLines(IEnumerable<Line> extra)
    {
        Line[] added = (extra ?? []).Where(line => line != null && !_byId.ContainsKey(line.Id)).ToArray();
        return added.Length == 0 ? this : new AutonomousBotVoiceCorpus(Lines.Concat(added).ToArray(), Scenes, Voices);
    }

    /// <summary>The corpus line with this id, or null.</summary>
    public Line LineById(string id) => id != null && _byId.TryGetValue(id, out Line line) ? line : null;

    #region Parsing

    public static AutonomousBotVoiceCorpus Parse(string corpusJson, string voicesJson)
    {
        using JsonDocument corpus = JsonDocument.Parse(corpusJson);
        using JsonDocument voices = JsonDocument.Parse(voicesJson);
        var lines = corpus.RootElement.GetProperty("lines").EnumerateArray().Select(ParseLine).ToArray();
        var scenes = corpus.RootElement.GetProperty("scenes").EnumerateArray().Select(ParseScene).ToArray();
        var styles = voices.RootElement.GetProperty("voices").EnumerateObject().Select(ParseVoice).ToArray();
        if (styles.Length == 0 || styles.Sum(voice => voice.Share) <= 0)
            throw new InvalidOperationException("voices.json has no usable voices");
        return new AutonomousBotVoiceCorpus(lines, scenes, styles);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string[] Strs(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())
                .Where(x => !string.IsNullOrEmpty(x) && x != "*").ToArray()
            : Array.Empty<string>();

    private static (int, int) Levels(JsonElement e)
    {
        if (e.TryGetProperty("levels", out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2)
            return (v[0].GetInt32(), v[1].GetInt32());
        return (1, 50);
    }

    private static double Num(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static Line ParseLine(JsonElement e)
    {
        (int low, int high) = Levels(e);
        string realm = Str(e, "realm");
        return new Line(Str(e, "id"), Str(e, "kind"), Str(e, "topic"), Strs(e, "who"), realm.Length == 0 ? "*" : realm,
            Strs(e, "class"), low, high, Strs(e, "needs"), Str(e, "intent"), Str(e, "mood"), Num(e, "weight", 1.0), Str(e, "text"))
        {
            Invites = Str(e, "invites"),
            Answers = Strs(e, "answers"),
            About = Bool(e, "about"),
            Family = Str(e, "family"),
        };
    }

    private static Scene ParseScene(JsonElement e)
    {
        (int low, int high) = Levels(e);
        var roles = new Dictionary<string, RoleSpec>(StringComparer.Ordinal);
        if (e.TryGetProperty("roles", out JsonElement r) && r.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty role in r.EnumerateObject())
            {
                (int roleLow, int roleHigh) = Levels(role.Value);
                roles[role.Name] = new RoleSpec(Strs(role.Value, "type"), Strs(role.Value, "class"), roleLow, roleHigh,
                    role.Value.TryGetProperty("task", out JsonElement task) ? task.GetString() : null);
            }
        var beats = e.GetProperty("beats").EnumerateArray().Select(b =>
        {
            string role = Str(b, "role");
            string[] variants = Strs(b, "variants");
            if (variants.Length == 0 && Str(b, "text").Length > 0)
                variants = new[] { Str(b, "text") };
            return new Beat(role, variants, Bool(b, "optional"), (int)Num(b, "pause", 0));
        }).ToArray();
        string realm = Str(e, "realm");
        return new Scene(Str(e, "id"), Str(e, "topic"), realm.Length == 0 ? "*" : realm, low, high, roles, beats)
        {
            Needs = Strs(e, "needs"),
            Exit = Str(e, "exit"),
        };
    }

    private static Voice ParseVoice(JsonProperty p)
    {
        JsonElement e = p.Value;
        var abbreviate = e.TryGetProperty("abbreviate", out JsonElement a) && a.ValueKind == JsonValueKind.Object
            ? a.EnumerateObject().Select(x => (x.Name, x.Value.GetString() ?? string.Empty))
                .OrderByDescending(x => x.Name.Length).ToArray()   // longer phrases first
            : Array.Empty<(string, string)>();
        var expand = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty("expand", out JsonElement x2) && x2.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty pair in x2.EnumerateObject())
                expand[pair.Name] = pair.Value.GetString() ?? string.Empty;
        return new Voice(p.Name, Num(e, "share", 0), Bool(e, "capitalize"), Bool(e, "capitalize_sentences"), Bool(e, "end_period"),
            Strs(e, "no_period_after"), Bool(e, "strip_punctuation"), abbreviate, expand, Strs(e, "emotes"),
            Num(e, "emote_chance", 0), Num(e, "typo_chance", 0));
    }

    #endregion

    #region Choosing

    public static string RealmCode(eRealm realm) => realm switch
    {
        eRealm.Albion => "alb",
        eRealm.Midgard => "mid",
        eRealm.Hibernia => "hib",
        _ => "*",
    };

    public static bool RealmFits(string lineRealm, eRealm realm) => lineRealm == "*" || lineRealm == RealmCode(realm);

    public static bool ClassFits(string[] classes, string className) =>
        classes.Length == 0 || classes.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));

    public static bool TypeFits(string[] types, BotPersonalityType type) =>
        types.Length == 0 || types.Any(t => string.Equals(t, type.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the speaker's flags meet every need: "in_group" must be set, "!in_group" must not be
    /// (0.36-se.4: lines could only ask for a flag, so "anyone want to duo?" was said by grouped bots too).
    /// </summary>
    public static bool NeedsMet(string[] needs, IReadOnlySet<string> flags)
    {
        foreach (string need in needs)
        {
            bool negated = need.StartsWith('!');
            string flag = negated ? need[1..] : need;
            // The talking update: "near:myrkwood" is being in Myrkwood or on the way there (flags "at:" and "going:").
            bool has = flag.StartsWith("near:", StringComparison.Ordinal)
                ? flags.Contains("at:" + flag[5..]) || flags.Contains("going:" + flag[5..])
                : flags.Contains(flag);
            if (has == negated)
                return false;
        }
        return true;
    }

    /// <summary>Lines of a kind this speaker may say now (realm, class, level band, needed context, intent, answers).</summary>
    private Dictionary<string, Line[]> _byKind;

    /// <summary>The lines of a kind (built once: lines built from parts made the corpus several times larger).</summary>
    private Line[] OfKind(string kind)
    {
        Dictionary<string, Line[]> byKind = _byKind ??= Lines.GroupBy(line => line.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return kind != null && byKind.TryGetValue(kind, out Line[] lines) ? lines : [];
    }

    public IEnumerable<Line> Candidates(string kind, Speaker speaker, string intent = null, string topic = null, string mood = null,
        string answers = null) =>
        OfKind(kind).Where(line => line.Kind == kind &&
                            (topic == null || string.Equals(line.Topic, topic, StringComparison.Ordinal)) &&
                            (mood == null || string.Equals(line.Mood, mood, StringComparison.Ordinal)) &&
                            RealmFits(line.Realm, speaker.Realm) &&
                            ClassFits(line.Classes, speaker.ClassName) &&
                            speaker.Level >= line.LevelLow && speaker.Level <= line.LevelHigh &&
                            NeedsMet(line.Needs, speaker.Flags) &&
                            (string.IsNullOrEmpty(intent) || string.Equals(line.Intent, intent, StringComparison.OrdinalIgnoreCase)) &&
                            (string.IsNullOrEmpty(answers) || line.Answers.Contains(answers, StringComparer.Ordinal)));

    /// <summary>
    /// The invitations a reply may answer, most specific first: "ask_where" is answered by "ask_where"
    /// replies, then by "ask" replies. "none" (and an empty invitation) asks for no reply.
    /// </summary>
    public static string[] AnswerChain(string invites)
    {
        if (string.IsNullOrEmpty(invites) || invites == "none")
            return [];
        int cut = invites.IndexOf('_');
        return cut > 0 ? [invites, invites[..cut]] : [invites];
    }

    /// <summary>The family of an invitation ("ask_where" -> "ask"), used for closings and reply counts.</summary>
    public static string InviteFamily(string invites)
    {
        if (string.IsNullOrEmpty(invites))
            return string.Empty;
        int cut = invites.IndexOf('_');
        return cut > 0 ? invites[..cut] : invites;
    }

    /// <summary>
    /// How specific a line is (the chat framework's rule score, C0): one point for each criterion it names beyond its kind, so
    /// a line for one intent, topic or question, realm, class, level band or situation outscores a line that fits anything.
    /// 0 for no line. Pure.
    /// </summary>
    public static int Specificity(Line line)
    {
        if (line == null)
            return 0;
        int score = 0;
        if (!string.IsNullOrEmpty(line.Topic) && line.Topic is not ("general" or ""))
            score++;
        if (!string.IsNullOrEmpty(line.Intent))
            score++;
        if (line.Answers.Length > 0)
            score += line.Answers.Any(answer => answer.Contains('_')) ? 2 : 1;
        if (line.Who.Length > 0)
            score++;
        if (line.Realm != "*")
            score++;
        if (line.Classes.Length > 0)
            score++;
        if (line.LevelLow > 1 || line.LevelHigh < 50)
            score++;
        score += line.Needs.Length;
        return score;
    }

    /// <summary>A line's draw weight for this speaker: lines written for its personality type count triple.</summary>
    public static double WeightFor(Line line, Speaker speaker) =>
        Math.Max(0.01, line.Weight) * (line.Who.Length > 0 && TypeFits(line.Who, speaker.Type) ? 3.0 : line.Who.Length > 0 ? 0.0 : 1.0);

    /// <summary>
    /// Picks, fills and styles a line, skipping lines heard in this realm recently and lines whose
    /// slots can't be filled. False when nothing fits (the caller keeps its old line).
    /// </summary>
    public bool TryPick(string kind, Speaker speaker, IReadOnlyDictionary<string, string> slots, RecentLines recent,
        DateTime now, Random random, out string text, out string lineId, string intent = null, string topic = null, string mood = null)
    {
        bool picked = TryPick(new Request(kind, speaker, slots, recent, now, random) { Intent = intent, Topic = topic, Mood = mood },
            out Picked result);
        text = result?.Text;
        lineId = result?.Line.Id;
        return picked;
    }

    /// <summary>What to pick: a kind for a speaker, with optional filters and the no-repeat rules.</summary>
    public sealed record Request(string Kind, Speaker Speaker, IReadOnlyDictionary<string, string> Slots, RecentLines Recent,
        DateTime Now, Random Random)
    {
        public string Intent { get; init; }
        public string Topic { get; init; }
        public string Mood { get; init; }
        public string Answers { get; init; }

        /// <summary>Only lines that need this flag (an advert for a listed item uses only the "has_listing" lines).</summary>
        public string RequireNeed { get; init; }

        /// <summary>Only lines this accepts (for example, class talk about the class that was asked about).</summary>
        public Func<Line, bool> Filter { get; init; }

        /// <summary>
        /// When every fitting line was heard within the six-hour window, a line not heard for this long may
        /// be used again (short, common replies: "np", "lol", "where?"), so a player is never left unanswered
        /// because the pool ran dry. Null keeps the full window.
        /// </summary>
        public TimeSpan? RelaxTo { get; init; }

        /// <summary>The local hour on the PC's clock, for "tonight" by day (null: no time adaptation).</summary>
        public int? LocalHour { get; init; }

        /// <summary>
        /// Slots that come with a flag (the talking update): a line that needs "keep_lost_recent" fills {keep} with the keep
        /// that was lost, and one that needs "defending" with the keep under siege, whatever {keep} is for other lines.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NeedSlots { get; init; }
    }

    /// <summary>The slots a line is filled with: the request's, then those of each flag the line needs.</summary>
    public static IReadOnlyDictionary<string, string> SlotsForLine(Line line, IReadOnlyDictionary<string, string> slots,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> needSlots)
    {
        if (needSlots == null || needSlots.Count == 0 || line.Needs.Length == 0)
            return slots;
        Dictionary<string, string> merged = null;
        foreach (string need in line.Needs)
        {
            if (need.StartsWith('!') || !needSlots.TryGetValue(need, out var extra) || extra == null)
                continue;
            merged ??= slots == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(slots, StringComparer.Ordinal);
            foreach (var pair in extra)
                merged[pair.Key] = pair.Value;
        }
        return merged ?? slots;
    }

    /// <summary>A picked line: its text as sent, the corpus line, and a typo correction the speaker may send after it ("*word").</summary>
    public sealed record Picked(string Text, Line Line, string Correction);

    /// <summary>
    /// How long a template's other ways of saying it rest after one was said (Beta 1.1.2: the blind judge heard "that horse
    /// ride took so long i aged a year" and "... i grew a beard" in one evening). The relaxed pick ignores it.
    /// </summary>
    public static readonly TimeSpan FamilyRest = TimeSpan.FromHours(6);

    public bool TryPick(Request request, out Picked result)
    {
        result = null;
        if (TryPickFrom(request, line => request.Recent == null || !request.Recent.WasUsed(request.Speaker.Realm, line.Id, request.Now) &&
                                         (line.Family.Length == 0 ||
                                          !request.Recent.UsedWithin(request.Speaker.Realm, "f:" + line.Family, request.Now, FamilyRest)),
                out result))
            return true;
        return request.RelaxTo is TimeSpan relax && request.Recent != null &&
               TryPickFrom(request, line => !request.Recent.UsedWithin(request.Speaker.Realm, line.Id, request.Now, relax), out result);
    }

    private bool TryPickFrom(Request request, Func<Line, bool> allowed, out Picked result)
    {
        result = null;
        Speaker speaker = request.Speaker;
        Random random = request.Random;
        var pool = Candidates(request.Kind, speaker, request.Intent, request.Topic, request.Mood, request.Answers)
            .Where(line => request.RequireNeed == null || line.Needs.Contains(request.RequireNeed, StringComparer.Ordinal))
            .Where(line => request.Filter == null || request.Filter(line))
            .Where(allowed)
            .Select(line => (Line: line, Weight: WeightFor(line, speaker)))
            .Where(entry => entry.Weight > 0)
            .ToList();
        while (pool.Count > 0)
        {
            double total = pool.Sum(entry => entry.Weight);
            double draw = random.NextDouble() * total;
            int index = 0;
            for (; index < pool.Count - 1; index++)
            {
                if (draw < pool[index].Weight)
                    break;
                draw -= pool[index].Weight;
            }
            Line line = pool[index].Line;
            pool.RemoveAt(index);
            IReadOnlyDictionary<string, string> lineSlots = SlotsForLine(line, request.Slots, request.NeedSlots);
            if (NamesTheBossTwice(line.Text, lineSlots) || !TryFillMasked(line.Text, lineSlots, out string masked, out List<string> values))
                continue;
            string filled = Unmask(masked, values);
            // 1.1.3, after RC5: no item paired with a class that can't use it ("wtb a shield for a 30 cabalist").
            if (AutonomousChatGear.PairsUnusable(filled, speaker?.Level ?? 50))
                continue;
            if (request.LocalHour is int hour)
                masked = ForTimeOfDay(masked, hour);
            string text;
            string correction = null;
            // 1.2 chat audit (CORPUS-B2-010, LOG-022): a gold spammer's ad is said exactly as written, with no bot's style.
            if (line.Kind == "spam")
                text = Unmask(masked, values);
            else
            {
                Voice voice = VoiceFor(speaker.Id);
                // 1.2 chat audit (DESIGN-015): the style and the typing habits work on the template with its slot values
                // masked, so no typo or "teh" lands in a class, monster, item or place name ("wtb fine aloy great hammer").
                // Chat audit 1.2.2 (F9): a lore line takes no laugh, whatever its kind.
                string styleKind = line.Topic == "lore" ? "lore" : line.Kind;
                text = Style(masked, voice, random, out correction, styleKind);
                // Beta 1.1.2, the style layer: the bot's own typing habits on top of its writing style.
                // In-character lines keep their diction.
                text = AutonomousChatIdiolect.Apply(text, speaker.Id, voice.Capitalize || line.Kind == "roleplay" || line.Intent == "roleplay", random,
                    terse: !TakesEmote(styleKind, masked));
                text = Unmask(text, values);
            }
            // 1.2 chat audit (DESIGN-014): a line too long once filled is not cut in mid-word; the next candidate is tried.
            if (text.Length > MaxLength)
                continue;
            request.Recent?.Use(speaker.Realm, line.Id, request.Now);
            if (line.Family.Length > 0)
                request.Recent?.Use(speaker.Realm, "f:" + line.Family, request.Now);
            result = new Picked(text, line, correction);
            return true;
        }
        return false;
    }

    #endregion

    #region Time of day

    /// <summary>
    /// The season on the PC's calendar, northern hemisphere by month (owner, 2026-11-18 09:14: season-aware weather lines):
    /// "winter" December-February, "spring" March-May, "summer" June-August, "autumn" September-November. Pure.
    /// </summary>
    public static string SeasonFlag(DateTime date) => date.Month switch
    {
        12 or 1 or 2 => "winter",
        3 or 4 or 5 => "spring",
        6 or 7 or 8 => "summer",
        _ => "autumn",
    };

    /// <summary>"day" 6-17, "evening" 17-23, "night" 23-6, on the PC's local clock.</summary>
    public static string TimeFlag(int localHour) => localHour is >= 6 and < 17 ? "day" : localHour is >= 17 and < 23 ? "evening" : "night";

    /// <summary>
    /// By day, "tonight" reads "today" and "all night" reads "all day", so the many lines that say
    /// "tonight" fit a noon session too (0.36-se.4; lines that only fit the evening say so with @needs).
    /// 1.1.3, after RC6 (owner's PC, 10:44 AM: "busy tonight" in /faction): "this evening" reads "today" by day as well, and
    /// "this morning" reads "earlier" from noon on and before 5 AM. Every bot line in /faction, /say, /lfg and /trade passes
    /// here on its way out (AutonomousBotChatCoordinator), and the field lines, threads and stories when they are made.
    /// </summary>
    /// <summary>
    /// <see cref="ForTimeOfDay"/> on the PC's clock now: the one time-of-day pass every bot line takes on its way out, whatever
    /// picked it (corpus, field talk, guild lines, stories, threads, debates, tells), at each channel's sender: /faction, /say,
    /// /lfg and /trade (AutonomousBotChatCoordinator), /broadcast (AutonomousChatBroadcast), /group (AutonomousBotGroupChatter),
    /// /guild (AutonomousBotGuildSocial.SendGuildLine) and tells (AutonomousLivingServer.Tell). 1.1.3, after RC6.
    /// </summary>
    public static string ForNow(string text) => ForTimeOfDay(text, DateTime.Now.Hour);

    public static string ForTimeOfDay(string text, int localHour)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        string result = text;
        if (TimeFlag(localHour) == "day")
        {
            result = Regex.Replace(result, @"\b(tonight|tonite|this evening)\b", "today", options);
            result = Regex.Replace(result, @"\ball night\b", "all day", options);
            // RC10 on the owner's PC: "first night in the frontier" at 15:05.
            result = Regex.Replace(result, @"\bfirst night\b", "first time", options);
        }
        if (localHour >= 12 || localHour < 5)
            result = Regex.Replace(result, @"\bthis morning\b", "earlier", options);
        return result;
    }

    #endregion

    #region Plurals

    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wolf"] = "wolves", ["werewolf"] = "werewolves", ["dwarf"] = "dwarves", ["elf"] = "elves", ["thief"] = "thieves",
        ["man"] = "men", ["woman"] = "women", ["child"] = "children", ["mouse"] = "mice", ["louse"] = "lice",
        ["goose"] = "geese", ["ox"] = "oxen", ["deer"] = "deer", ["sheep"] = "sheep", ["fish"] = "fish",
        ["boar"] = "boars", ["undead"] = "undead", ["kin"] = "kin", ["fungus"] = "fungi", ["cyclops"] = "cyclopes",
        ["leaf"] = "leaves", ["knife"] = "knives", ["staff"] = "staffs", ["djinn"] = "djinn", ["half"] = "halves",
        ["calf"] = "calves", ["scarf"] = "scarves", ["loaf"] = "loaves", ["shelf"] = "shelves", ["wife"] = "wives",
    };

    /// <summary>"-man" words that are not "-men" in the plural.</summary>
    private static readonly string[] ManPluralExceptions = ["shaman", "human", "talisman", "caiman", "ottoman", "german", "brahman"];

    /// <summary>
    /// The plural of a monster name for {monsters}: the last word changes ("hill cat" -> "hill cats",
    /// "orc clanshacker" -> "orc clanshackers", "giant wolf" -> "giant wolves"). A name with "of" changes
    /// the word before it ("bwca of the hills" -> "bwcas of the hills").
    /// </summary>
    public static string Plural(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        string[] words = name.Trim().Split(' ');
        int head = Array.FindIndex(words, w => w is "of" or "from" or "in" or "on");
        int at = head > 0 ? head - 1 : words.Length - 1;
        words[at] = PluralWord(words[at]);
        return string.Join(' ', words);
    }

    private static string PluralWord(string word)
    {
        if (word.Length == 0)
            return word;
        if (IrregularPlurals.TryGetValue(word, out string irregular))
            return char.IsUpper(word[0]) ? char.ToUpperInvariant(irregular[0]) + irregular[1..] : irregular;
        string lower = word.ToLowerInvariant();
        if (lower.EndsWith("man") && lower.Length > 4 && !ManPluralExceptions.Any(lower.EndsWith))
            return word[..^3] + "men";
        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh"))
            return lower.EndsWith("us") || lower.EndsWith("ss") || !lower.EndsWith("s") ? word + "es" : word;
        if (lower.EndsWith("y") && lower.Length > 1 && "aeiou".IndexOf(lower[^2]) < 0)
            return word[..^1] + "ies";
        return word + "s";
    }

    #endregion

    #region Slots

    private static readonly Regex SlotPattern = new(@"\{(\w+)\}", RegexOptions.CultureInvariant);
    private static readonly Regex ArticleBeforeSlot = new(@"\b([Aa]) \{(\w+)\}", RegexOptions.CultureInvariant);

    /// <summary>Optional openers for {q}; empty half the time.</summary>
    public static readonly string[] Openers = ["", "", "", "hey", "so", "random question but", "quick question,", "ok so"];

    /// <summary>
    /// 1.2: a line that names both {boss} and {raid} when the raid is named after its boss (a dragon or an open-world raid):
    /// "the raid killed Golestandt in Golestandt", "Coliath raid got Coliath". Such a line is skipped; the lines that name
    /// one of them are left. Pure.
    /// </summary>
    public static bool NamesTheBossTwice(string template, IReadOnlyDictionary<string, string> slots) =>
        template != null && slots != null && template.Contains("{raid}", StringComparison.Ordinal) &&
        template.Contains("{boss}", StringComparison.Ordinal) && slots.TryGetValue("raid", out string raid) &&
        slots.TryGetValue("boss", out string boss) && RealmEventBanter.NamedForBoss(raid, boss);

    private static readonly Regex ArticleBeforeWord = new(@"\b([Aa]) ([A-Za-z0-9][\w']*)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a word takes "an" as typed in chat (PC check 2026-11-19, build 5154cae: "a armorcrafter"): a word starting
    /// with a, e, i or o ("an elementalist", not "an one-hander"), with u when it is said "uh" ("an undead", "an uber"; not
    /// "a unique", "a useful"), and the numbers said with a vowel first ("an 8", "an 11", "an 18", "an 80"). Pure.
    /// </summary>
    public static bool TakesAn(string next)
    {
        string word = (next ?? string.Empty).TrimStart().ToLowerInvariant();
        if (word.Length == 0)
            return false;
        char first = word[0];
        if (char.IsDigit(first))
        {
            string digits = new(word.TakeWhile(char.IsDigit).ToArray());
            return digits[0] == '8' || digits is "11" or "18";
        }
        if (first is 'a' or 'e' or 'i')
            return !word.StartsWith("eu", StringComparison.Ordinal) && !word.StartsWith("ewe", StringComparison.Ordinal);
        if (first == 'o')
            return !word.StartsWith("one", StringComparison.Ordinal) && !word.StartsWith("once", StringComparison.Ordinal);
        if (first == 'u')
            return !(word.StartsWith("uni", StringComparison.Ordinal) || word.StartsWith("use", StringComparison.Ordinal) ||
                     word.StartsWith("usu", StringComparison.Ordinal) || word.StartsWith("uti", StringComparison.Ordinal) ||
                     word.StartsWith("ure", StringComparison.Ordinal) || word.StartsWith("uri", StringComparison.Ordinal) ||
                     word.StartsWith("ura", StringComparison.Ordinal) || word.StartsWith("ubi", StringComparison.Ordinal) || word.Length == 1);
        return word.StartsWith("hour", StringComparison.Ordinal) || word.StartsWith("honest", StringComparison.Ordinal) ||
               word.StartsWith("honor", StringComparison.Ordinal) || word.StartsWith("honour", StringComparison.Ordinal);
    }

    /// <summary>"a" or "an" for the word after it, by <see cref="TakesAn"/>. Pure.</summary>
    public static string AOrAn(string next) => TakesAn(next) ? "an" : "a";

    /// <summary>
    /// A finished line with "a" made "an" where the next word takes it ("a armorcrafter" -> "an armorcrafter"), for lines built
    /// in code rather than filled from the corpus. Only the article is touched; the rest of the typing stays as it is. Pure.
    /// </summary>
    public static string FixArticles(string text) =>
        string.IsNullOrEmpty(text) || !text.Contains("a ", StringComparison.OrdinalIgnoreCase) ? text
            : ArticleBeforeWord.Replace(text, match => TakesAn(match.Groups[2].Value) && !NotANoun.Contains(match.Groups[2].Value)
                ? match.Groups[1].Value + "n " + match.Groups[2].Value : match.Value);

    // "plan a or b", "rate it a in my book": a word after a lone "a" that is no noun keeps the "a" as typed.
    private static readonly HashSet<string> NotANoun = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "or", "of", "on", "in", "at", "as", "is", "it", "if", "its", "i", "im", "i'm", "are", "am", "also", "all",
        "any", "up", "us", "off", "out", "over", "into", "onto", "else", "ever", "each", "either", "even", "oh", "ok", "okay", "until",
    };

    /// <summary>Fills every {slot}; false if any slot has no value (a line is never sent half-filled).</summary>
    public static bool TryFill(string template, IReadOnlyDictionary<string, string> slots, out string filled)
    {
        bool ok = TryFillMasked(template, slots, out string masked, out List<string> values);
        filled = ok ? Unmask(masked, values) : null;
        return ok;
    }

    /// <summary>The first private-use character a masked slot value is written as (one character per value).</summary>
    private const char MaskBase = '';

    /// <summary>
    /// 1.2 chat audit (DESIGN-015): <see cref="TryFill"/>, but each non-empty slot value is one private-use character in the
    /// text (<paramref name="values"/> holds the values in order), so a writing style and typing habits leave names alone. The
    /// mask is neither a letter nor lower case, so no rule touches it. <see cref="Unmask"/> puts the values back. Pure.
    /// </summary>
    public static bool TryFillMasked(string template, IReadOnlyDictionary<string, string> slots, out string masked, out List<string> values)
    {
        bool ok = true;
        var found = new List<string>();
        // "a {class}" before a vowel reads "an elementalist" (playtest 2026-11-04: "first to 50 as a elementalist"). Not
        // before u: "a unique".
        // PC check 2026-11-19 (build 5154cae): one rule for every filler (TakesAn), numbers too ("an 18 suit").
        template = ArticleBeforeSlot.Replace(template ?? string.Empty, match =>
            slots != null && slots.TryGetValue(match.Groups[2].Value, out string next) && TakesAn(next)
                ? match.Groups[1].Value + "n {" + match.Groups[2].Value + "}"
                : match.Value);
        string result = SlotPattern.Replace(template, match =>
        {
            if (slots != null && slots.TryGetValue(match.Groups[1].Value, out string value) && value != null &&
                (value.Length > 0 || match.Groups[1].Value == "q"))
            {
                // {q} is a chat opener ("hey", "quick question,"), styled like the rest of the line; and past 255 values
                // a value is left as plain text.
                if (value.Length == 0 || match.Groups[1].Value == "q" || found.Count > 255)
                    return value;
                found.Add(value);
                return ((char)(MaskBase + found.Count - 1)).ToString();
            }
            ok = false;
            return match.Value;
        });
        values = found;
        masked = ok ? Regex.Replace(result, @"\s+", " ").Trim().TrimStart(',', ' ') : null;
        return ok && !string.IsNullOrEmpty(masked);
    }

    /// <summary>A masked text (<see cref="TryFillMasked"/>) with its slot values back in. Pure.</summary>
    public static string Unmask(string masked, IReadOnlyList<string> values)
    {
        if (string.IsNullOrEmpty(masked) || values == null || values.Count == 0)
            return masked;
        var output = new StringBuilder(masked.Length + 32);
        foreach (char c in masked)
        {
            int index = c - MaskBase;
            output.Append(index >= 0 && index < values.Count ? values[index] : c.ToString());
        }
        return output.ToString();
    }

    /// <summary>A text cut to <paramref name="max"/> characters at a word, never in mid-word (DESIGN-014): <see cref="Fit"/>. Pure.</summary>
    public static string CutAtWord(string text, int max = MaxLength) => Fit(text, max);

    #endregion

    #region Voices

    /// <summary>A bot's writing style, fixed from its id by the styles' shares.</summary>
    public Voice VoiceFor(long botId)
    {
        double total = Voices.Sum(voice => voice.Share);
        double draw = AutonomousBotPersonality.Mix(botId, 2) % 1_000_000 / 1_000_000.0 * total;
        foreach (Voice voice in Voices)
        {
            if (draw < voice.Share)
                return voice;
            draw -= voice.Share;
        }
        return Voices[^1];
    }

    /// <summary>Applies a writing style (the server version of tools/bot-voices/preview.py's voice_apply).</summary>
    public static string Style(string text, Voice voice, Random random) => Style(text, voice, random, out _);

    /// <summary>Chance that a typo-prone writer sends a "*word" correction after a typo (0.36-se.4).</summary>
    public const double CorrectionChance = 0.45;

    /// <summary>
    /// Applies a writing style. <paramref name="correction"/> is "*word" when the style made a typo and the
    /// writer would fix it in a second line (typo-prone writers often did), else null.
    /// </summary>
    public static string Style(string text, Voice voice, Random random, out string correction) =>
        Style(text, voice, random, out correction, null);

    /// <summary>
    /// 1.2 chat audit (DESIGN-013, CORPUS-B2-032, LOG-022): kinds whose lines take no laugh or emoticon from a writing style or
    /// the "lol" habit: ads, alarms, rez asks, deaths, news, combat calls, in-character lines, beggars' pleas.
    /// </summary>
    public static readonly HashSet<string> NoEmoteKinds = new(StringComparer.Ordinal)
    {
        "spam", "alarm", "rez_ask", "death", "news", "combat", "roleplay", "beggar",
        // Chat audit 1.2.2 (C10, fix F9): a lore line (an answer, a tale, a briefing) is told straight.
        "lore",
    };

    /// <summary>
    /// 1.2 chat audit (DESIGN-012): words that make a line a functional one, a trade, group or call line ("wts ...", "lf healer",
    /// "pc on a staff?", "inc 8 mids"). Such a line is typed the same by every writer: no capital, no stop, no expansion, no
    /// laugh, and the terse style keeps its commas.
    /// </summary>
    private static readonly Regex FunctionalWords = new(
        @"(?<![a-z0-9])(wts|wtb|wtt|lfg|lfm|lf|lf[1-7]m|pc|pst|inc|/send|/tell|/t|/gu|cc'?d|oom|rdy|bio)(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A trade, group or call line (see <see cref="FunctionalWords"/>). Pure.</summary>
    public static bool IsFunctional(string text) => !string.IsNullOrEmpty(text) && FunctionalWords.IsMatch(text);

    private static readonly Regex Laugh = new(@"(?<![a-z])(lol|lmao|rofl|haha+|hehe+|heh|ha|hah)(?![a-z])|[:;]-?[)(PpDdOo/\\|]|\^\^|<g>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The line already laughs or has an emoticon ("lol", "haha", ":)"). Pure.</summary>
    public static bool HasLaugh(string text) => !string.IsNullOrEmpty(text) && Laugh.IsMatch(text);

    /// <summary>A line typed in capitals ("SUPER CHEAP GOLD!!!", a rage quit): most of its letters are upper case. Pure.</summary>
    public static bool IsShouted(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        int letters = text.Count(char.IsLetter), upper = text.Count(char.IsUpper);
        return letters >= 6 && upper >= letters * 0.6;
    }

    /// <summary>Whether a style or habit may add a laugh to a line of this kind (DESIGN-013; chat audit 1.2.2, F9: not to a
    /// serious line, <see cref="IsSerious"/>). Pure.</summary>
    public static bool TakesEmote(string kind, string text) =>
        !(kind != null && NoEmoteKinds.Contains(kind)) && !IsFunctional(text) && !HasLaugh(text) && !IsShouted(text) && !IsSerious(text) &&
        !EndsAsQuestion(text);

    /// <summary>
    /// 1.2.3 (the owner's game on 1.2.2: "camelot hills camp for 4? heh"): a question never ends on a tacked laugh or emote; it
    /// reads as the bot laughing at its own question. Pure.
    /// </summary>
    public static bool EndsAsQuestion(string text) => !string.IsNullOrEmpty(text) && text.TrimEnd().EndsWith('?');

    // Chat audit 1.2.2 (C10, fix F9): "no clue, sry haha", "thats a full raid, dont go alone :)", "trust nothing you meet there heh".
    private static readonly Regex SeriousWords = new(
        @"(?<![a-z])(dunno|idk|not sure|no idea|no clue|beats me|ask around|dont craft|wish i knew|who knows|hard to say|cant say|can't say|couldnt tell|couldn't tell|" +
        @"dont know|don't know|cant heal|cant rez|no rez|careful|watch out|watch ur back|watch your back|beware|stay away|dont go alone|" +
        @"don't go alone|full raid|needs a raid|need a raid|bring a raid|wipe|wipes|wiped|died|dead|die|dies|dying|death|deadly|dangerous|humbles|" +
        @"cant go|can't go|trust nothing|careless|raid|rip|sorry for your loss)(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "bog of cullen? take a horse from a stable master": a line that asks a question and answers it in three words or more.
    private static readonly Regex AsksAndAnswers = new(@"^[^?]{2,60}\?\s+\S+\s+\S+\s+\S", RegexOptions.CultureInvariant);

    /// <summary>
    /// Chat audit 1.2.2 (C10, fix F9): a line no laugh is tacked onto: a "dunno", a warning, a death, or an answer that says the
    /// question back first ("ardee? thats in connacht"). Pure.
    /// </summary>
    public static bool IsSerious(string text) =>
        !string.IsNullOrEmpty(text) && (SeriousWords.IsMatch(text) || AsksAndAnswers.IsMatch(text) || AsksOnly.IsMatch(text));

    // "where's ur body? :)": a laugh after a question that opens with a question word reads as the bot answering itself.
    private static readonly Regex AsksOnly = new(
        @"^\W*(whats|what's|what s|what is|who is|whos|who's|who holds|where is|wheres|where's|where s|is the|is it|how do|how is|why is)\b[^?]{0,48}\?\W*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// "inc 8 mids at Caer Benowyc, healer, warrior, skald": three or more comma parts whose later parts are one or two words
    /// each. The terse style keeps these commas (DESIGN-013). Pure.
    /// </summary>
    public static bool IsCommaList(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        string[] parts = text.TrimEnd('.', '!', '?').Split(',');
        return parts.Length >= 3 && parts.Skip(1).All(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 1 and <= 2);
    }

    /// <summary>Chat tokens that are also plain words a tidy writer starts a sentence with ("This is why.", "No, not yet.").</summary>
    private static readonly HashSet<string> SentenceWords = new(StringComparer.Ordinal)
        { "this", "no", "yes", "true", "same", "sure", "nice", "cool", "later", "night", "agreed", "hey", "hi" };

    private static readonly Regex SentenceStart = new(@"(?<![.!?])([.!?] )([a-z][a-z0-9]*)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Applies a writing style to a line of <paramref name="kind"/> (null: any). 1.2 chat audit: a functional line keeps its
    /// shape (DESIGN-012); no laugh is added to a line that has one, a functional line, a line in capitals or a kind in
    /// <see cref="NoEmoteKinds"/> (DESIGN-013); the terse strip keeps a list's commas, keeps "!" in capitals and keeps a
    /// sentence break as a comma (CORPUS-B1-028); words are expanded at word boundaries ("u?" -> "you?"); no capital after
    /// "..." or "!!". A text over <see cref="MaxLength"/> is cut at a word and ends "..." (<see cref="Fit"/>, DESIGN-014).
    /// <paramref name="maxWords"/>: a debate piece's split limit; no emote that would push the piece past it
    /// (AutonomousChatDebateSpeech.TailFits; 0: no limit).
    /// </summary>
    public static string Style(string text, Voice voice, Random random, out string correction, string kind, int maxWords = 0)
    {
        correction = null;
        if (string.IsNullOrEmpty(text) || voice == null)
            return text;
        string result = text;
        bool functional = IsFunctional(text);
        bool shouted = IsShouted(text);
        bool emote = TakesEmote(kind, text);
        // A "proper" writer still types chat words as chat words: "lol" and "ty ty", not "Lol." or "Thanks thanks."
        // (audit 2026-10-18).
        // Beta 1.1.2: "np, back to it", "lol yeah thats it": a line that starts with a chat word stays as typed, even by a
        // tidy writer.
        bool chatToken = IsChatToken(text) || StartsWithChatToken(text) || functional || shouted;
        foreach ((string from, string to) in voice.Abbreviate)
            result = Regex.Replace(result, $@"(?<=^|\s){Regex.Escape(from)}(?=$|[\s,.?!])", to, RegexOptions.CultureInvariant);
        if (voice.Expand.Count > 0 && !chatToken)
            foreach ((string from, string full) in voice.Expand)
                result = Regex.Replace(result, $@"(?<=^|\s){Regex.Escape(from)}(?=$|[\s,.?!])", full, RegexOptions.CultureInvariant);
        if (voice.StripPunctuation)
        {
            if (!shouted)
                result = result.TrimEnd('.', '!');
            else
                result = result.TrimEnd('.');
            if (!IsCommaList(result))
                result = result.Replace(",", string.Empty);
            // CORPUS-B1-028: "i do both. zerg when i want points" read "i do both zerg when..."; a sentence break stays a break.
            result = Regex.Replace(result, @"(?<!\.)\. (?=\S)", ", ");
        }
        if (voice.TypoChance > 0 && random.NextDouble() < voice.TypoChance)
        {
            string typed = SwapTypo(result, random, out string fixedWord);
            if (typed != result && fixedWord != null && random.NextDouble() < CorrectionChance)
                correction = "*" + fixedWord;
            result = typed;
        }
        if (emote && voice.Emotes.Length > 0 && voice.EmoteChance > 0 && random.NextDouble() < voice.EmoteChance &&
            AutonomousChatDebateSpeech.TailFits(result, maxWords))
            // Chat audit 1.2.2: "good luck out there. :)" read as a laugh stuck on; a single stop goes before the emote.
            result = (result.EndsWith('.') && !result.EndsWith("..") ? result[..^1] : result) + " " + voice.Emotes[random.Next(voice.Emotes.Length)];
        if (voice.Capitalize && result.Length > 0 && !chatToken)
            result = char.ToUpperInvariant(result[0]) + result[1..];
        // Chat audit 1.2.2 (C11, fix F10): a writer who capitalizes capitalizes names too ("The trainer in Camelot").
        if (voice.Capitalize && !chatToken)
            result = AutonomousChatNames.Capitalize(result);
        if (voice.CapitalizeSentences && !chatToken)
            // A chat word starts no sentence with a capital ("...beg? nope", not "beg? Nope"; the builder's round trip found them).
            result = SentenceStart.Replace(result, m => ChatTokens.Contains(m.Groups[2].Value) && !SentenceWords.Contains(m.Groups[2].Value)
                ? m.Value
                : m.Groups[1].Value + char.ToUpperInvariant(m.Groups[2].Value[0]) + m.Groups[2].Value[1..]);
        // Beta 1.1.2: even a tidy writer leaves a short reply unstopped ("Np, have fun", not "Np, have fun.").
        if (voice.EndPeriod && result.Length > 0 && !chatToken && result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3)
        {
            string[] after = voice.NoPeriodAfter.Length > 0 ? voice.NoPeriodAfter : new[] { ".", "!", "?" };
            if (!after.Any(end => result.EndsWith(end, StringComparison.Ordinal)))
                result += ".";
        }
        return Fit(result);
    }

    /// <summary>
    /// A line cut to <paramref name="max"/> characters without cutting a word: it ends at the last whole word that fits,
    /// followed by "..." (plain ASCII: the 1.65 client's chat font is not trusted with the single ellipsis character).
    /// A line that already fits is returned as it is. Seen on the owner's PC (build b1b6846, 2026-11-18): a crafter's
    /// "what do you make?" answer was cut at 160 characters mid-word ("in stock: fine alloy except"). Pure.
    /// </summary>
    public static string Fit(string text, int max = MaxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        const string more = "...";
        int room = Math.Max(1, max - more.Length);
        int cut = text.LastIndexOf(' ', Math.Min(room, text.Length - 1));
        string head = (cut > 0 ? text[..cut] : text[..room]).TrimEnd(' ', ',', ';', ':', '.', '-');
        return head + more;
    }

    /// <summary>Swaps two neighboring letters in one word of four or more letters.</summary>
    public static string SwapTypo(string text, Random random) => SwapTypo(text, random, out _);

    /// <summary>
    /// A typing slip in one word of five or more letters; <paramref name="original"/> is that word as it should be. Beta 1.1.2:
    /// mostly a dropped letter ("realy", "everone"), as fast typists made; a swap of two neighbours now and then, never the
    /// first letter (the blind judge read "repaet" as planted).
    /// </summary>
    public static string SwapTypo(string text, Random random, out string original)
    {
        original = null;
        string[] words = text.Split(' ');
        int[] eligible = words.Select((word, index) => (word, index))
            .Where(x => x.word.Length >= 5 && x.word.All(char.IsLetter) && x.word.All(char.IsLower)).Select(x => x.index).ToArray();
        if (eligible.Length == 0)
            return text;
        int pick = eligible[random.Next(eligible.Length)];
        string word = words[pick];
        int at = 1 + random.Next(word.Length - 2);
        string typed;
        if (random.NextDouble() < 0.75)
            typed = word.Remove(at, 1);
        else
        {
            if (word[at] == word[at + 1])
                return text;
            char[] letters = word.ToCharArray();
            (letters[at], letters[at + 1]) = (letters[at + 1], letters[at]);
            typed = new string(letters);
        }
        if (typed == word)
            return text;
        original = word;
        words[pick] = typed;
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> ChatTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "lol", "heh", "hehe", "haha", "ha", "rofl", "lmao", "gz", "grats", "gratz", "ty", "thx", "np", "k", "kk", "ok", "brb",
        "afk", "wb", "gn", "gl", "omw", "inc", "re", "hm", "hmm", "huh", "yw", "sry", "nm", "nvm", "pst", "ding", "lfg", "lfm",   // chat-check: input
        "oom", "med", "rez", "r", "y", "n", "yep", "nope", "ikr", "bah", "ugh", "doh", "w00t", "woot",   // chat-check: input
        // Beta 1.1.2: "Lol yes." read as a bot (the blind spot-the-bot judge); short acks stay lowercase for every writer.
        "yes", "ya", "yeah", "yea", "no", "nah", "sure", "same", "true", "nice", "cool", "hey", "hi", "yo", "sup", "cya", "later", "night",
        "rip", "oof", "phew", "gg", "x", "this", "agreed", "hehe", "lmao",   // chat-check: input
        // 1.2 chat audit (DESIGN-012): "Wts 97% chain hauberk /send.", "Pc on a runed staff?", "Where r u?" came from a tidy writer.
        "wts", "wtb", "wtt", "lf", "lf1m", "lf2m", "lf3m", "lf4m", "pc", "sm", "zerk", "any1", "ne1", "gtg", "bbl", "bio", "rdy",
        "plz", "pls",
    };

    /// <summary>A line whose first word is a chat word ("np, back to it"). Pure.</summary>
    public static bool StartsWithChatToken(string text)
    {
        string first = Regex.Match(text ?? string.Empty, @"^[a-zA-Z0-9]+").Value;
        return first.Length > 0 && ChatTokens.Contains(first) && !first.Equals("i", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A line made only of chat words ("lol", "ty ty", "gz!"), which no writer capitalizes or ends with a period.</summary>
    public static bool IsChatToken(string text)
    {
        string[] words = Regex.Replace(text ?? string.Empty, @"[^a-zA-Z0-9 ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is > 0 and <= 3 && words.All(ChatTokens.Contains);
    }

    #endregion

    #region No-repeat window

    /// <summary>
    /// When each line was last used in each realm, kept for <see cref="Window"/>. The capacity is far
    /// above the corpus size, so a line really is not repeated in a realm within the window (audit
    /// 2026-10-16: a 400-entry list could let a busy realm repeat lines after an hour or two).
    /// </summary>
    public sealed class RecentLines
    {
        /// <summary>
        /// Three days (0.36-se.4; six hours before): a player who plays each evening heard 17-29% of the previous
        /// evenings' lines in the re-run simulation, because a six-hour window had expired by the next evening.
        /// Callers fall back to six hours (<see cref="Relaxed"/>) when a pool is used up, so nobody goes quiet.
        /// </summary>
        public static readonly TimeSpan Window = TimeSpan.FromDays(3);

        /// <summary>The old six-hour window, still kept when the three-day pool for a kind is used up.</summary>
        public static readonly TimeSpan Relaxed = TimeSpan.FromHours(6);
        public const int Capacity = 5000;
        private readonly Dictionary<eRealm, Dictionary<string, DateTime>> _byRealm = new();

        public bool WasUsed(eRealm realm, string id, DateTime now)
        {
            lock (_byRealm)
                return _byRealm.TryGetValue(realm, out var used) && used.TryGetValue(id, out DateTime when) && now - when < Window;
        }

        public void Use(eRealm realm, string id, DateTime now)
        {
            lock (_byRealm)
            {
                if (!_byRealm.TryGetValue(realm, out var used))
                    _byRealm[realm] = used = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                used[id] = now;
                if (used.Count <= Capacity)
                    return;
                foreach (string old in used.Where(entry => now - entry.Value >= Window).Select(entry => entry.Key).ToList())
                    used.Remove(old);
                foreach (string oldest in used.OrderBy(entry => entry.Value).Take(used.Count - Capacity).Select(entry => entry.Key).ToList())
                    used.Remove(oldest);
            }
        }

        public int Count(eRealm realm)
        {
            lock (_byRealm)
                return _byRealm.TryGetValue(realm, out var used) ? used.Count : 0;
        }

        /// <summary>True when the line was used in this realm less than <paramref name="span"/> ago.</summary>
        public bool UsedWithin(eRealm realm, string id, DateTime now, TimeSpan span)
        {
            lock (_byRealm)
                return _byRealm.TryGetValue(realm, out var used) && used.TryGetValue(id, out DateTime when) && now - when < span;
        }

        /// <summary>
        /// Lines used since <paramref name="since"/> (exclusive), for saving: 0.36-se.4 keeps the window across
        /// server restarts (the audit found 31% of a session's lines had been heard the session before).
        /// </summary>
        public (eRealm Realm, string Id, DateTime When)[] UsedSince(DateTime since)
        {
            lock (_byRealm)
                return _byRealm.SelectMany(realm => realm.Value.Where(entry => entry.Value > since)
                    .Select(entry => (realm.Key, entry.Key, entry.Value))).ToArray();
        }

        /// <summary>Restores a saved use (the later time wins).</summary>
        public void Restore(eRealm realm, string id, DateTime when)
        {
            lock (_byRealm)
            {
                if (!_byRealm.TryGetValue(realm, out var used))
                    _byRealm[realm] = used = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                if (!used.TryGetValue(id, out DateTime known) || known < when)
                    used[id] = when;
            }
        }
    }

    #endregion
}
