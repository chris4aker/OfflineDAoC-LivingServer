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
    }

    public sealed record Beat(string Role, string[] Variants, bool Optional, int PauseSeconds)
    {
        public bool IsPause => Role == "~";
    }

    /// <summary>Who may play a scene role: personality types, classes and, for lines like "i am not 50 yet", levels.</summary>
    public sealed record RoleSpec(string[] Types, string[] Classes, int LevelLow = 1, int LevelHigh = 50)
    {
        public bool LevelFits(int level) => level >= LevelLow && level <= LevelHigh;
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
                roles[role.Name] = new RoleSpec(Strs(role.Value, "type"), Strs(role.Value, "class"), roleLow, roleHigh);
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

    public bool TryPick(Request request, out Picked result)
    {
        result = null;
        if (TryPickFrom(request, line => request.Recent == null || !request.Recent.WasUsed(request.Speaker.Realm, line.Id, request.Now),
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
            if (!TryFill(line.Text, SlotsForLine(line, request.Slots, request.NeedSlots), out string filled))
                continue;
            if (request.LocalHour is int hour)
                filled = ForTimeOfDay(filled, hour);
            string text = Style(filled, VoiceFor(speaker.Id), random, out string correction);
            request.Recent?.Use(speaker.Realm, line.Id, request.Now);
            result = new Picked(text, line, correction);
            return true;
        }
        return false;
    }

    #endregion

    #region Time of day

    /// <summary>"day" 6-17, "evening" 17-23, "night" 23-6, on the PC's local clock.</summary>
    public static string TimeFlag(int localHour) => localHour is >= 6 and < 17 ? "day" : localHour is >= 17 and < 23 ? "evening" : "night";

    /// <summary>
    /// By day, "tonight" reads "today" and "all night" reads "all day", so the many lines that say
    /// "tonight" fit a noon session too (0.36-se.4; lines that only fit the evening say so with @needs).
    /// </summary>
    public static string ForTimeOfDay(string text, int localHour)
    {
        if (string.IsNullOrEmpty(text) || TimeFlag(localHour) != "day")
            return text;
        string result = Regex.Replace(text, @"\b(tonight|tonite)\b", "today", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(result, @"\ball night\b", "all day", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    /// <summary>Optional openers for {q}; empty half the time.</summary>
    public static readonly string[] Openers = ["", "", "", "hey", "so", "random question but", "quick question,", "ok so"];

    /// <summary>Fills every {slot}; false if any slot has no value (a line is never sent half-filled).</summary>
    public static bool TryFill(string template, IReadOnlyDictionary<string, string> slots, out string filled)
    {
        bool ok = true;
        string result = SlotPattern.Replace(template ?? string.Empty, match =>
        {
            if (slots != null && slots.TryGetValue(match.Groups[1].Value, out string value) && value != null &&
                (value.Length > 0 || match.Groups[1].Value == "q"))
                return value;
            ok = false;
            return match.Value;
        });
        filled = ok ? Regex.Replace(result, @"\s+", " ").Trim().TrimStart(',', ' ') : null;
        return ok && !string.IsNullOrEmpty(filled);
    }

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
    public static string Style(string text, Voice voice, Random random, out string correction)
    {
        correction = null;
        if (string.IsNullOrEmpty(text) || voice == null)
            return text;
        string result = text;
        // A "proper" writer still types chat words as chat words: "lol" and "ty ty", not "Lol." or "Thanks thanks."
        // (audit 2026-10-18).
        bool chatToken = IsChatToken(text);
        foreach ((string from, string to) in voice.Abbreviate)
            result = Regex.Replace(result, $@"(?<=^|\s){Regex.Escape(from)}(?=$|[\s,.?!])", to, RegexOptions.CultureInvariant);
        if (voice.Expand.Count > 0 && !chatToken)
            result = string.Join(' ', result.Split(' ').Select(word => voice.Expand.TryGetValue(word, out string full) ? full : word));
        if (voice.StripPunctuation)
            result = result.TrimEnd('.', '!').Replace(",", string.Empty);
        if (voice.TypoChance > 0 && random.NextDouble() < voice.TypoChance)
        {
            string typed = SwapTypo(result, random, out string fixedWord);
            if (typed != result && fixedWord != null && random.NextDouble() < CorrectionChance)
                correction = "*" + fixedWord;
            result = typed;
        }
        if (voice.Emotes.Length > 0 && voice.EmoteChance > 0 && random.NextDouble() < voice.EmoteChance)
            result += " " + voice.Emotes[random.Next(voice.Emotes.Length)];
        if (voice.Capitalize && result.Length > 0 && !chatToken)
            result = char.ToUpperInvariant(result[0]) + result[1..];
        if (voice.CapitalizeSentences)
            result = Regex.Replace(result, @"([.!?] )([a-z])", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
        if (voice.EndPeriod && result.Length > 0 && !chatToken)
        {
            string[] after = voice.NoPeriodAfter.Length > 0 ? voice.NoPeriodAfter : new[] { ".", "!", "?" };
            if (!after.Any(end => result.EndsWith(end, StringComparison.Ordinal)))
                result += ".";
        }
        return result.Length <= MaxLength ? result : result[..MaxLength];
    }

    /// <summary>Swaps two neighboring letters in one word of four or more letters.</summary>
    public static string SwapTypo(string text, Random random) => SwapTypo(text, random, out _);

    /// <summary>Swaps two neighboring letters in one word of four or more letters; <paramref name="original"/> is that word as it should be.</summary>
    public static string SwapTypo(string text, Random random, out string original)
    {
        original = null;
        string[] words = text.Split(' ');
        int[] eligible = words.Select((word, index) => (word, index))
            .Where(x => x.word.Length >= 4 && x.word.All(char.IsLetter)).Select(x => x.index).ToArray();
        if (eligible.Length == 0)
            return text;
        int pick = eligible[random.Next(eligible.Length)];
        char[] letters = words[pick].ToCharArray();
        int at = random.Next(letters.Length - 1);
        if (letters[at] == letters[at + 1])
            return text;
        original = words[pick];
        (letters[at], letters[at + 1]) = (letters[at + 1], letters[at]);
        words[pick] = new string(letters);
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> ChatTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "lol", "heh", "hehe", "haha", "ha", "rofl", "lmao", "gz", "grats", "gratz", "ty", "thx", "np", "k", "kk", "ok", "brb",
        "afk", "wb", "gn", "gl", "omw", "inc", "re", "hm", "hmm", "huh", "yw", "sry", "nm", "nvm", "pst", "ding", "lfg", "lfm",
        "oom", "med", "rez", "r", "y", "n", "yep", "nope", "ikr", "bah", "ugh", "doh", "w00t", "woot",
    };

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
