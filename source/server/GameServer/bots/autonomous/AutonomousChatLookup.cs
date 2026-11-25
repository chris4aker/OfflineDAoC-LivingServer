using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Realism 2e (#482; docs/realism-audit-1.2.3/structure.md, "E"): the knowledge a new player needs, as one grounded lookup.
/// <see cref="Ask"/> reads a question once (typos and period shorthand folded, <see cref="Fold"/>), decides what it is about
/// (<see cref="TopicOf"/>, the topics of data/chat/game_guide.json in their order) and asks only the providers of that topic,
/// in the topic's own order: precedence by question type, not by call order. Each answer comes back with its source and its
/// topic, and an answer whose topic is not the question's is never said (the audit: "can i salvage dropped armor" drew
/// "blademasters wear reinforced"). A question of no topic of the guide takes the lookup's earlier order
/// (AutonomousChatKnowledge's world, town, lore, trainer and location steps), unchanged.
/// Providers: the written guide (game rules and Living Server's features, each line with its source), and the facts the
/// server's own data gives: the class tree (AutonomousChatClassChoice.Bases), con colours, death rules
/// (ServerProperties pve_exp_loss_level, pve_con_loss_level), realm ranks (GamePlayer.REALMPOINTS_FOR_LEVEL), the first
/// steps of a new character (data/chat/places.json). Pure but for the guide's loading. Tests only until seen in a played log.
/// </summary>
public static class AutonomousChatLookup
{
    /// <summary>One answer: what is said, where the fact comes from, and the question type it answers.</summary>
    public sealed record Fact(string Text, string Source, string Topic);

    /// <summary>A question read once: the line as typed, folded, normalized and padded, its topic, and the asker.</summary>
    public sealed record Asked(string Raw, string Text, string Padded, string Topic, bool AsksSomething,
        AutonomousChatKnowledge.KnowledgePorts Ports, Random Random)
    {
        public eRealm Realm => Ports?.Realm ?? eRealm.None;
        public int Level => Ports?.Level ?? 0;
        public string ClassName => Ports?.ClassName;
        public bool Has(string pattern) => Regex.IsMatch(Padded, pattern, RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------------------------------------------------- typos

    /// <summary>
    /// Period spelling and typos, one word each (the audit's finding 9: "wher do i trian", "whre is jordhiem", "ne1 no where 2
    /// xp @ 20", "any1 kno wher the wepon merchent is", "hwo do i get to tnn"). Only words that are never a word of their own.
    /// </summary>
    private static readonly Dictionary<string, string> Typos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wher"] = "where", ["whre"] = "where", ["wehre"] = "where", ["whr"] = "where", ["wheer"] = "where", ["wherr"] = "where",
        ["whers"] = "wheres", ["whres"] = "wheres", ["wheres's"] = "wheres",
        ["hwo"] = "how", ["hw"] = "how", ["hpw"] = "how", ["waht"] = "what", ["wht"] = "what", ["whta"] = "what", ["wut"] = "what",
        ["whats's"] = "whats", ["wich"] = "which", ["whcih"] = "which", ["shoud"] = "should", ["shuold"] = "should", ["sould"] = "should",
        ["trian"] = "train", ["trane"] = "train", ["triner"] = "trainer", ["trainr"] = "trainer", ["traner"] = "trainer",
        ["trianer"] = "trainer", ["tranier"] = "trainer", ["trainor"] = "trainer",
        ["jordhiem"] = "jordheim", ["jordhem"] = "jordheim", ["jordheym"] = "jordheim", ["jordhim"] = "jordheim", ["jorheim"] = "jordheim",
        ["camalot"] = "camelot", ["camelott"] = "camelot", ["camleot"] = "camelot", ["camlot"] = "camelot",
        ["ne1"] = "anyone", ["any1"] = "anyone", ["ny1"] = "anyone", ["anyon"] = "anyone", ["some1"] = "someone", ["no1"] = "no one",
        ["kno"] = "know", ["knw"] = "know", ["nkow"] = "know",
        ["merchent"] = "merchant", ["merchnt"] = "merchant", ["marchant"] = "merchant", ["merchat"] = "merchant", ["vender"] = "vendor",
        ["wepon"] = "weapon", ["weapn"] = "weapon", ["wpn"] = "weapon", ["weopon"] = "weapon", ["armer"] = "armor", ["armr"] = "armor",
        ["teh"] = "the", ["lvel"] = "level", ["levle"] = "level", ["leval"] = "level", ["mony"] = "money", ["monney"] = "money",
        ["hlep"] = "help", ["halp"] = "help", ["realy"] = "really", ["ressurect"] = "resurrect", ["rezz"] = "rez",
        ["constituion"] = "constitution", ["consitution"] = "constitution", ["constution"] = "constitution",
        ["purp"] = "purple", ["gray"] = "grey", ["grays"] = "greys", ["respecc"] = "respec", ["spellcrafting"] = "spellcrafting",
        ["stealh"] = "stealth", ["steath"] = "stealth", ["guidl"] = "guild", ["gulid"] = "guild", ["gruop"] = "group", ["grp"] = "group",
        ["xperience"] = "experience", ["expierence"] = "experience", ["experiance"] = "experience",
    };

    private static readonly Regex Word = new(@"[A-Za-z0-9']+", RegexOptions.CultureInvariant);

    /// <summary>
    /// The line with its typos folded and period shorthand read: a typo word becomes its word (<see cref="Typos"/>), "@" before
    /// a number is "at", and "2" after a where, how, go or get word is "to" ("where 2 xp", "how 2 get"). Marks are kept, so a
    /// question mark still reads. Pure.
    /// </summary>
    public static string Fold(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;
        string t = Word.Replace(text, m => Typos.TryGetValue(m.Value, out string word) ? word : m.Value);
        t = Regex.Replace(t, @"@\s*(?=\d)", "at ");
        // "what is /faction": the realm channel, not the NPC factions (the audit's wrong-fact table).
        t = Regex.Replace(t, @"(?<![\w/])/(faction|fa)\b", "faction channel", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\b(where|wheres|how|go|get|got|want|need|going|way)\s+2\s+(?=[a-z])", "$1 to ", RegexOptions.IgnoreCase);
        return t;
    }

    // ------------------------------------------------------------------------------------------------------- the guide

    private sealed record Topic(string Id, Regex When, Regex Unless)
    {
        public bool Holds(string padded) => When.IsMatch(padded) && (Unless == null || !Unless.IsMatch(padded));
    }

    private sealed record Entry(string Id, string TopicId, Regex[] When, Regex[] Unless, string[] Lines, string Source, string Realm);

    private sealed record Guide(Topic[] Topics, Entry[] Entries);

    private static Guide _guide;
    private static readonly object GuideSync = new();

    private const string GuideFile = "game_guide.json";

    private static Guide Current
    {
        get
        {
            if (_guide != null)
                return _guide;
            lock (GuideSync)
                return _guide ??= Load();
        }
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Guide Load()
    {
        var assembly = typeof(AutonomousChatLookup).Assembly;
        string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + GuideFile, StringComparison.Ordinal));
        if (name == null)
            return new Guide([], []);
        using Stream stream = assembly.GetManifestResourceStream(name);
        using JsonDocument doc = JsonDocument.Parse(stream);
        return Parse(doc.RootElement);
    }

    private static Guide Parse(JsonElement root)
    {
        static string[] Strings(JsonElement e, string key) =>
            e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()).ToArray() : [];
        Topic[] topics = root.GetProperty("topics").EnumerateArray()
            .Select(t => new Topic(t.GetProperty("id").GetString(), Rx(t.GetProperty("when").GetString()),
                t.TryGetProperty("unless", out JsonElement u) ? Rx(u.GetString()) : null)).ToArray();
        Entry[] entries = root.GetProperty("entries").EnumerateArray().Select(e => new Entry(e.GetProperty("id").GetString(),
            e.GetProperty("topic").GetString(), Strings(e, "when").Select(Rx).ToArray(), Strings(e, "unless").Select(Rx).ToArray(),
            Strings(e, "lines"), e.TryGetProperty("source", out JsonElement s) ? s.GetString() : null,
            e.TryGetProperty("realm", out JsonElement r) ? r.GetString() : null)).ToArray();
        return new Guide(topics, entries);
    }

    /// <summary>The guide's topic ids, in the order they are read (for the tests and the coverage table).</summary>
    public static IReadOnlyList<string> TopicIds => Current.Topics.Select(t => t.Id).ToArray();

    /// <summary>The guide's entries as (id, topic, source), for the tests: every entry names its source.</summary>
    public static IReadOnlyList<(string Id, string Topic, string Source, string[] Lines)> Entries =>
        Current.Entries.Select(e => (e.Id, e.TopicId, e.Source, e.Lines)).ToArray();

    // ------------------------------------------------------------------------------------------------------- reading

    /// <summary>The line normalized as the knowledge lookup reads it, with a space at each end. Pure.</summary>
    public static string Pad(string text) => " " + AutonomousChatQuestions.Normalize(Fold(text ?? string.Empty)) + " ";

    /// <summary>The question type of a line: the first topic of the guide whose pattern it holds, else null. Pure.</summary>
    public static string TopicOf(string text)
    {
        string padded = Pad(text);
        return Current.Topics.FirstOrDefault(t => t.Holds(padded))?.Id;
    }

    private static readonly Regex AskWords = new(
        @"^ (how|what|whats|where|wheres|which|who|whos|why|when|is|are|do|does|did|can|could|should|would|will|any|anyone|anybody|someone|wtf|tips) | (how do|how to|how can|what do|what does|where do|where can|can i|do i|should i|is it|any tips|now what|what now|help|need help|wondering|someone tell|anyone know) ",
        RegexOptions.CultureInvariant);

    /// <summary>The topics a line may be answered in without asking (the audit: "stuck at 5, xp bar wont move", "my pet keeps
    /// running off and pulling stuff" are questions said as statements).</summary>
    private static readonly HashSet<string> StatementTopics = new(StringComparer.Ordinal) { "classtree", "pets", "conloss" };

    /// <summary>
    /// The topics whose question only their own providers answer: no lore line, no place, no other topic's fact (the subject
    /// guard). "terms" is not one: "whats in muire tomb" reads like a word question and is a place question.
    /// </summary>
    private static readonly HashSet<string> OpenTopics = new(StringComparer.Ordinal) { "terms", "ui", "craft", "quest", "classrole", "spec" };

    public static Asked Read(AutonomousChatKnowledge.KnowledgePorts ports, string message, Random random)
    {
        string raw = (message ?? string.Empty).Trim();
        string folded = Fold(raw);
        string padded = " " + AutonomousChatQuestions.Normalize(folded) + " ";
        string topic = Current.Topics.FirstOrDefault(t => t.Holds(padded))?.Id;
        bool asks = raw.Contains('?') || AutonomousChatQuestions.IsQuestion(folded) || AskWords.IsMatch(padded);
        return new Asked(raw, folded, padded, topic, asks, ports, random ?? Random.Shared);
    }

    // ------------------------------------------------------------------------------------------------------- the lookup

    /// <summary>
    /// The precedence of each question type: the providers asked, in order. A name with "guide" is the written guide's entries
    /// of the topic; the others are this lookup's own providers (<see cref="Providers"/>).
    /// </summary>
    private static readonly Dictionary<string, string[]> Precedence = new(StringComparer.Ordinal)
    {
        ["living"] = ["guide"],
        ["classtree"] = ["classtree"],
        ["conloss"] = ["guide"],
        ["respec"] = ["guide"],
        ["salvage"] = ["guide"],
        ["spellcraft"] = ["guide"],
        ["death"] = ["deathrules", "guide"],
        ["pets"] = ["guide"],
        ["stealth"] = ["guide"],
        ["buffs"] = ["guide"],
        ["guild"] = ["guide"],
        ["group"] = ["guide"],
        ["con"] = ["con"],
        ["rr"] = ["howto", "rr"],
        ["relic"] = ["world", "guide"],
        ["keep"] = ["world", "guide"],
        ["money"] = ["guide"],
        ["items"] = ["guide"],
        ["ui"] = ["howto", "guide"],
        ["stats"] = ["stats", "guide"],
        ["endgame"] = ["guide"],
        ["classadvice"] = ["guide"],
        ["start"] = ["guide", "start"],
        ["ra"] = ["guide"],
        ["craft"] = ["guide", "legacyguide", "common"],
        ["quest"] = ["common", "guide"],
        ["spec"] = ["howto", "spec", "common"],
        ["trainer"] = ["guide", "trainer"],
        ["classrole"] = ["guide", "classrole"],
        ["merchant"] = ["whosells", "guide"],
        ["bind"] = ["guide"],
        ["vault"] = ["guide"],
        ["tasks"] = ["guide"],
        ["terms"] = ["terms"],
    };

    private static readonly Dictionary<string, Func<Asked, Fact>> Providers = new(StringComparer.Ordinal)
    {
        ["guide"] = FromGuide,
        ["classtree"] = ClassTree,
        ["deathrules"] = DeathRules,
        ["con"] = ConColour,
        ["rr"] = RealmRank,
        ["start"] = FirstSteps,
        ["terms"] = Term,
        ["howto"] = a => AutonomousChatKnowledge.TryAnswerHowTo(a.Text, out string how) ? new Fact(how, "AutonomousChatKnowledge.TryAnswerHowTo", a.Topic) : null,
        ["world"] = a => a.Ports?.World?.Invoke(a.Text) is { Length: > 0 } world ? new Fact(world, "the world's keeps and relics (AnswerWorld)", a.Topic) : null,
        ["spec"] = a => AutonomousChatReplyPath.SpecAnswer(a.Text, a.ClassName, a.Random) is { } spec ? new Fact(spec, "AutonomousChatFieldTalk.ClassSpecAdvice", a.Topic) : null,
        ["common"] = a => AutonomousChatKnowledge.TryAnswerCommon(a.Text, a.Realm, a.ClassName, a.Level, a.Random, out string common)
            ? new Fact(common, "AutonomousChatKnowledge.TryAnswerCommon", a.Topic) : null,
        ["legacyguide"] = a => AutonomousChatGuide.TryAnswer(a.Text, a.Realm, a.ClassName, a.Level, a.Ports?.DfOwner ?? eRealm.None, a.Ports?.DfOpenToAll ?? false,
            a.Random, out string guide, a.Ports?.Factions?.Invoke()) ? new Fact(guide, "AutonomousChatGuide", a.Topic) : null,
        ["trainer"] = a => a.Ports?.Trainer?.Invoke(a.Text) is { Length: > 0 } trainer ? new Fact(trainer, "the class trainers (world data)", a.Topic) : null,
        ["whosells"] = a => a.Ports?.WhoSells?.Invoke(a.Text) is { Length: > 0 } seller ? new Fact(seller, "the world's merchant lists", a.Topic) : null,
        ["classrole"] = ClassRole,
        ["stats"] = a => AutonomousChatGuide.TryAnswerStats(a.Text, a.ClassName, a.Random, out string stats) ? new Fact(stats, "AutonomousChatGuide stats", a.Topic) : null,
    };

    /// <summary>
    /// The grounded lookup: the answer to a question and its source, or null. <paramref name="handled"/> says whether the
    /// question had a topic of this lookup that holds it (then nothing else may answer it, the subject guard). Pure but for
    /// the ports.
    /// </summary>
    public static Fact Ask(AutonomousChatKnowledge.KnowledgePorts ports, string message, Random random, out bool handled)
    {
        handled = false;
        Asked asked = Read(ports, message, random);
        // The question types read from the world's own data before the guide's words: a dragon by name, a named place's levels.
        if (asked.AsksSomething)
            foreach (Func<Asked, Fact> first in First)
                if (first(asked) is { } known)
                {
                    handled = true;
                    return known;
                }
        if (asked.Topic == null)
        {
            // No topic of the guide: where to hunt at a level, the battlegrounds, from the data; else the earlier order.
            if (asked.AsksSomething)
                foreach (Func<Asked, Fact> after in After)
                    if (after(asked) is { } known)
                    {
                        handled = true;
                        return known;
                    }
            return null;
        }
        if (!Precedence.TryGetValue(asked.Topic, out string[] order))
            return null;
        if (!asked.AsksSomething && !StatementTopics.Contains(asked.Topic))
            return null;
        handled = !OpenTopics.Contains(asked.Topic);
        foreach (string provider in order)
        {
            if (!Providers.TryGetValue(provider, out Func<Asked, Fact> answer))
                continue;
            Fact fact = answer(asked);
            // The subject guard: a fact of another question type is not said.
            if (fact != null && !string.IsNullOrWhiteSpace(fact.Text) && (fact.Topic == null || fact.Topic == asked.Topic))
                return fact;
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------------- providers

    private static string Pick(Random random, IReadOnlyList<string> lines) => lines[random.Next(lines.Count)];

    private static int OrDefault(int value, int fallback) => value > 0 ? value : fallback;

    /// <summary>The server's death settings (their defaults where the properties are not loaded, as in tests).</summary>
    public static int XpLossLevel => OrDefault(Properties.PVE_EXP_LOSS_LEVEL, 6);

    public static int ConLossLevel => OrDefault(Properties.PVE_CON_LOSS_LEVEL, 6);

    public static int RelicBonus => OrDefault(Properties.RELIC_OWNING_BONUS, 10);

    private static string Fill(string line, Asked asked)
    {
        AutonomousChatGuide.Realms.TryGetValue(asked.Realm, out AutonomousChatGuide.Realm r);
        string capital = AutonomousBotVoices.CapitalOf(asked.Realm) ?? r?.Capital ?? "town";
        return line.Replace("{capital}", capital).Replace("{porter}", r?.Porter ?? "the porter")
            .Replace("{border_keep}", r?.BorderKeep ?? "the border keep").Replace("{vault}", r?.Vault ?? "the vault keeper")
            .Replace("{smith}", r?.Smith ?? "the smith").Replace("{xp_loss_level}", XpLossLevel.ToString())
            .Replace("{con_loss_level}", ConLossLevel.ToString()).Replace("{relic_bonus}", RelicBonus.ToString());
    }

    /// <summary>The written guide: the first entry of the question's topic whose patterns the line holds.</summary>
    private static Fact FromGuide(Asked asked)
    {
        foreach (Entry entry in Current.Entries)
        {
            if (entry.TopicId != asked.Topic || entry.Lines.Length == 0)
                continue;
            if (entry.Realm != null && entry.Realm != RealmCode(asked.Realm))
                continue;
            if (!entry.When.All(w => w.IsMatch(asked.Padded)) || entry.Unless.Any(u => u.IsMatch(asked.Padded)))
                continue;
            return new Fact(Fill(Pick(asked.Random, entry.Lines), asked), entry.Source + " [" + entry.Id + "]", entry.TopicId);
        }
        return null;
    }

    private static readonly Regex Lose = new(@" (lose|loose|losing|lost|penalty|happens|happen|cost) ", RegexOptions.CultureInvariant);

    private static readonly Regex RvrWords = new(@" (rvr|frontier|pvp|enemy|keep|emain|odins|hadrian|hadrians|bg|battleground|battlegrounds|thid|thidranki|duel) ",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// "do i lose anything when i die at lvl 3": the server's own rule. Below pve_exp_loss_level a death costs nothing; from it
    /// a PvE death costs experience (a third of the full loss for the first death in a level, two thirds for the second,
    /// GamePlayer.Die) and, from pve_con_loss_level, constitution the healer gives back. Pure.
    /// </summary>
    private static Fact DeathRules(Asked asked)
    {
        if (!Lose.IsMatch(asked.Padded) || RvrWords.IsMatch(asked.Padded))
            return null;
        int level = AutonomousChatLiveAnswers.LevelAsked(asked.Text, asked.Level);
        string source = "ServerProperties pve_exp_loss_level, pve_con_loss_level; GamePlayer.Die";
        if (level > 0 && level < XpLossLevel)
            return new Fact(Pick(asked.Random, [$"nothing til {XpLossLevel}, die all you want lol", $"nope, nothing before {XpLossLevel}. after that some xp and con",
                $"no loss under level {XpLossLevel}, so dont worry yet"]), source, asked.Topic);
        return new Fact(Pick(asked.Random, ["some xp and a point of con. the first death in a level costs less", "xp, and con the healer npc can give back. /pray at your grave for some xp",
            "a chunk of xp and some con, a rez saves part of it"]), source, asked.Topic);
    }

    private static readonly (string Colour, Regex Words, string[] Lines)[] Colours =
    [
        ("grey", Rx(@" (grey|greys) "), ["greys give no xp, theyre way below you", "no xp from greys at all", "nope, greys give nothing"]),
        ("green", Rx(@" (green|greens) "), ["green is easy, only a little xp", "greens are easy kills, not much xp", "easy, low xp"]),
        ("blue", Rx(@" (blue|blues) "), ["blue is a bit below you, easy and ok xp", "blues are safe to solo, decent xp", "a bit under you, fair xp"]),
        ("yellow", Rx(@" (yellow|yellows) "), ["yellow is your level, good xp but a real fight", "even con, good xp", "same level as you, solid xp"]),
        ("orange", Rx(@" (orange|oranges) "), ["orange is above you, hard solo but great xp", "a bit higher than you, careful solo", "tough, great xp if you can take it"]),
        ("red", Rx(@" (red|reds) "), ["red is well above you, group only", "reds will hurt, bring a group", "too high to solo, group it"]),
        ("purple", Rx(@" (purple|purples) "), ["purple is way above you, run lol", "yeah purple will kill you, avoid", "way too high, dont touch purples"]),
    ];

    /// <summary>
    /// "what does blue con mean", "is purple bad", "do greys give xp": the con colours, grey to purple by the level gap
    /// (GameObject.GetConLevel; a grey gives no experience). Pure.
    /// </summary>
    private static Fact ConColour(Asked asked)
    {
        const string source = "GameObject.GetConLevel (grey to purple by the level gap; grey gives no xp)";
        if (asked.Has(@" (should|best|which|what) .*(fight|kill|hunt|solo|go for|xp) | (fight|kill|hunt) .*(colou?r|con) "))
            return new Fact(Pick(asked.Random, ["blues and yellows, oranges if your class can take them", "yellow and blue for solo, oranges in a group",
                "blue to yellow is the sweet spot, greys give nothing"]), source, asked.Topic);
        foreach ((string _, Regex words, string[] lines) in Colours)
            if (words.IsMatch(asked.Padded))
                return new Fact(Pick(asked.Random, lines), source, asked.Topic);
        return new Fact(Pick(asked.Random, ["grey green blue yellow orange red purple, easy to deadly. greys give no xp",
            "its how tough a mob is next to you, grey is nothing, yellow even, purple run", "con color, grey no xp, blue easy, yellow even, red and purple hard"]),
            source, asked.Topic);
    }

    private static readonly Regex Rank = new(@" rr ?(?<rank>\d{1,2}) | realm rank (?<rank>\d{1,2}) ", RegexOptions.CultureInvariant);

    /// <summary>"how many rps to hit rr3?", "how long to rr5": the realm points of a rank, from the server's table
    /// (GamePlayer.REALMPOINTS_FOR_LEVEL, ten levels a rank). Pure.</summary>
    private static Fact RealmRank(Asked asked)
    {
        Match m = Rank.Match(asked.Padded);
        if (!m.Success || !int.TryParse(m.Groups["rank"].Value, out int rr) || rr is < 2 or > 13)
            return null;
        long[] table = GamePlayer.REALMPOINTS_FOR_LEVEL;
        int index = (rr - 1) * 10;
        if (index >= table.Length)
            return null;
        string rps = table[index].ToString("N0");
        bool howLong = asked.Has(@" (how long|how many hours|how many days|how fast) ");
        string line = howLong
            ? Pick(asked.Random, [$"rr{rr} is {rps} rps, a while if you solo. group rvr goes faster", $"{rps} rps for rr{rr}, depends how much you rvr"])
            : Pick(asked.Random, [$"{rps} for rr{rr}", $"{rps} rps for rr{rr}", $"rr{rr} is {rps} rps"]);
        return new Fact(line, "GamePlayer.REALMPOINTS_FOR_LEVEL", asked.Topic);
    }

    private static readonly Dictionary<eRealm, (string Town, string Zone)> StartTowns = new()
    {
        [eRealm.Albion] = ("Cotswold", "Camelot Hills"), [eRealm.Midgard] = ("Mularn", "Vale of Mularn"), [eRealm.Hibernia] = ("Mag Mell", "Lough Derg"),
    };

    /// <summary>
    /// "just made a fighter, where do i go first", "first char ever, any tips for a noob?": the realm's first town and zone
    /// (data/chat/places.json: Cotswold in Camelot Hills, Mularn in the Vale of Mularn, Mag Mell in Lough Derg), the trainer,
    /// kill tasks from the guards and the bindstone. Pure.
    /// </summary>
    private static Fact FirstSteps(Asked asked)
    {
        if (!StartTowns.TryGetValue(asked.Realm, out (string Town, string Zone) start))
            return null;
        string town = start.Town.ToLowerInvariant(), zone = start.Zone.ToLowerInvariant();
        string line = Pick(asked.Random, [
            $"kill stuff around {town} in {zone}, see your trainer every level and do /task with the guards",
            $"{town}, then hunt in {zone}. bind at the stone and visit your trainer when you ding",
            $"start in {zone} near {town}, the guards give kill tasks. train every level",
        ]);
        return new Fact(line, "data/chat/places.json (the realm's first town); task.cs; bind.cs", asked.Topic);
    }

    private static readonly Regex AnyBase = new(@" (fighter|acolyte|mage|elementalist|disciple|rogue|viking|seer|mystic|guardian|naturalist|stalker|magician|forester) ",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The class tree (the audit's finding 4, the class choice in the words players use: "trainer wont train me past 5",
    /// "stuck at 5", "what can a mystic turn into", "can a mage be a necromancer", "whats the diff between a fighter and an
    /// armsman"): AutonomousChatClassChoice's answer for the class named, else for a base class named, else the asker's choice
    /// at 5, from the server's class tree. Pure.
    /// </summary>
    private static Fact ClassTree(Asked asked)
    {
        const string source = "AutonomousChatClassChoice.Bases (the server's class tree, era classes)";
        string cap = AutonomousBotVoices.CapitalOf(asked.Realm) ?? AutonomousChatFieldTalk.CapitalWord(asked.Realm);
        string Choice(string query, string cls) => AutonomousChatClassChoice.Answer(query, asked.Realm, cls, asked.Level, cap, asked.Random);
        string named = AutonomousChatFieldTalk.ClassNamed(asked.Text);
        string baseWord = AnyBase.Match(asked.Padded) is { Success: true } b ? b.Groups[1].Value : null;
        string baseName = baseWord == "rogue" && asked.Realm == eRealm.Midgard ? "Midgard Rogue"
            : baseWord != null ? char.ToUpperInvariant(baseWord[0]) + baseWord[1..] : null;
        // A class named that a base leads to: what the base named (else the asker's) can become.
        if (named != null && AutonomousChatClassChoice.BaseOf(named) is { } namedBase && !string.Equals(namedBase, named, StringComparison.OrdinalIgnoreCase))
        {
            string asker = baseName != null && AutonomousChatClassChoice.Bases.ContainsKey(baseName) ? baseName : asked.ClassName;
            if (Choice("can i become a " + named.ToLowerInvariant(), asker) is { } becomes)
                return new Fact(becomes, source, asked.Topic);
        }
        // A base class named that isn't the asker's: its options.
        if (baseName != null && AutonomousChatClassChoice.Bases.TryGetValue(baseName, out var tree))
        {
            string word = baseWord;
            string options = tree.Classes.Length == 1 ? tree.Classes[0].ToLowerInvariant()
                : string.Join(", ", tree.Classes[..^1].Select(c => c.ToLowerInvariant())) + " or " + tree.Classes[^1].ToLowerInvariant();
            string home = AutonomousBotVoices.CapitalOf(tree.Realm) ?? AutonomousChatFieldTalk.CapitalWord(tree.Realm);
            return new Fact(Pick(asked.Random, [$"{A(word)} picks {options} at {AutonomousChatClassChoice.PromotionLevel}, at their trainers in {home}",
                $"at {AutonomousChatClassChoice.PromotionLevel} {A(word)} becomes {options}", $"{options}, you pick at {AutonomousChatClassChoice.PromotionLevel}"]),
                source, asked.Topic);
        }
        if (Choice("where do i choose advanced class", asked.ClassName) is { } choice)
            return new Fact(choice, source, asked.Topic);
        return null;
    }

    private static string A(string word) =>
        (string.IsNullOrEmpty(word) ? "a" : "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an " : "a ") + word;

    // ------------------------------------------------------------------------------------------------------- words

    /// <summary>Period words the earlier glossary (AutonomousChatKnowledge.TryExplainTerm) doesn't hold (the audit's finding 9).</summary>
    private static readonly Dictionary<string, string> MoreTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bub"] = "a bubble, a tenth of your xp bar", ["bubble"] = "a tenth of the xp bar", ["bubs"] = "bubbles, tenths of the xp bar",
        ["mp"] = "masterpiece, a 100% quality craft", ["af"] = "armor factor, how much armor a piece has", ["conc"] = "concentration, what spec buffs cost",
        ["kiting"] = "running from a mob while you hit it from range", ["kite"] = "keep running and hit it from range", ["twink"] = "a lowbie in gear way above his level",
        ["baf"] = "bring a friend, a mob that pulls its buddies with it", ["otw"] = "on the way", ["omw"] = "on my way", ["uti"] = "utility, how much the bonuses on an item add up to",   // promise: none (what "omw" means, not a promise)
        ["sc"] = "spellcraft, gems on crafted gear", ["aoe"] = "area effect, hits everything around", ["ae"] = "area effect, hits everything around",
        ["dot"] = "damage over time", ["dd"] = "direct damage, a nuke", ["cc"] = "crowd control, mez root stun", ["nuke"] = "a big direct damage spell",
        ["gtg"] = "got to go", ["ttyl"] = "talk to you later", ["wb"] = "welcome back", ["inv"] = "invite", ["rdy"] = "ready", ["debuff"] = "a spell that lowers a stat",
        ["proc"] = "a spell an item fires when you hit", ["respec"] = "redo your spec points, at the trainer", ["bind"] = "where you come back after /release",
        ["port"] = "a teleport", ["bg"] = "battleground, a little rvr zone by level", ["bgs"] = "battlegrounds, little rvr zones by level", ["named"] = "a mob with a name, usually better loot",
        ["train"] = "a mob of mobs chasing someone", ["mini ding"] = "the half level after 40, you get spec points", ["minding"] = "the half level after 40",
        ["tank"] = "the guy in plate who holds the mobs", ["puller"] = "the one who brings the mobs to the group", ["lowbie"] = "a low level char",
        ["alt"] = "a second character", ["toon"] = "a character", ["mez"] = "mesmerize, holds a mob still till hit", ["xp"] = "experience",
        ["wts"] = "want to sell", ["wtb"] = "want to buy", ["pst"] = "please send tell", ["lfm"] = "looking for more, a group wants people",
    };

    private static readonly Regex TermQuestion = new(
        @"^ (?:so |um |hey |sorry |noob question |dumb question |quick q )?(?:what does|what do|whats|what is|wtf is|wtf does|wtfs|wat is|wats)(?: an?)? (?<w>[a-z0-9]+(?: ding)?)(?: mean| means| stand for| stands for)? $|^ (?<w>[a-z0-9]+(?: ding)?) (?:mean|means) $",
        RegexOptions.CultureInvariant);

    /// <summary>"wtf is a bub", "wts mean?", "what does otw mean", "whats a mini ding": a word's meaning. Pure.</summary>
    private static Fact Term(Asked asked)
    {
        if (AutonomousChatKnowledge.TryExplainTerm(asked.Text, out string known))
            return new Fact(known, "AutonomousChatKnowledge terms", asked.Topic);
        Match m = TermQuestion.Match(asked.Padded);
        if (!m.Success)
            return null;
        string word = m.Groups["w"].Value;
        if (!MoreTerms.TryGetValue(word, out string meaning))
            return null;
        return new Fact(Pick(asked.Random, [$"{word} = {meaning}", meaning, $"it means {meaning}"]), "AutonomousChatLookup terms", asked.Topic);
    }

    private static string RealmCode(eRealm realm) => realm switch
    {
        eRealm.Albion => "alb", eRealm.Midgard => "mid", eRealm.Hibernia => "hib", _ => "*",
    };

    // ------------------------------------------------------------------------------------------------------- the world's data

    private static readonly Func<Asked, Fact>[] First = [Dragon, PlaceLevel];

    private static readonly Func<Asked, Fact>[] After = [Battleground, Hunt];

    private static readonly (string Name, Regex Words, eRealm Realm, string Zone)[] Dragons =
    [
        ("Golestandt", Rx(@" (golestandt|golestand|golly) "), eRealm.Albion, "Dartmoor"),
        ("Gjalpinulva", Rx(@" (gjalpinulva|gjalp|gjalpi) "), eRealm.Midgard, "Malmohus"),
        ("Cuuldurach", Rx(@" (cuuldurach|cuuldurach the glimmer king|glimmer king|cuul) "), eRealm.Hibernia, "Sheeroe Hills"),
    ];

    /// <summary>"where is golestandt", "wheres cuuldurach": each realm's dragon by name and its zone (the world data, as
    /// AutonomousChatKnowledge.TryAnswerDragon). Pure.</summary>
    private static Fact Dragon(Asked asked)
    {
        foreach ((string name, Regex words, eRealm _, string zone) in Dragons)
            if (words.IsMatch(asked.Padded))
                return new Fact(Pick(asked.Random, [$"{name} is up in {zone}, thats a full raid", $"{zone}, dont go alone lol", $"{name}? in {zone}, raid only"]),
                    "the realm dragons' zones (world data; AutonomousChatKnowledge.TryAnswerDragon)", "dragon");
        return null;
    }

    /// <summary>"avalon isle what lvl", "am i too low for cardova at 24", "can i do muire tomb at 11": a named place's levels
    /// (data/chat/places.json, AutonomousChatQuestions.AnswerPlaceLevel), however the line opens. Pure.</summary>
    private static Fact PlaceLevel(Asked asked)
    {
        if (!AutonomousChatQuestions.AsksLevels(asked.Text) || AutonomousChatQuestions.PlaceNamed(asked.Text, AutonomousChatPlaces.All) is not { } place)
            return null;
        // "where should i hunt at 15 near mag mell": where to hunt, the live answer's (a zone only; a dungeon or a battleground
        // named with a level is a question about its levels).
        if (place.Type == "zone" && Hunts.IsMatch(asked.Padded) && !asked.Has(@" (good|ok|okay|fine|doable|too (low|high)|what (lvl|level)|lvl is|level is|any good) "))
            return null;
        string text = AutonomousChatQuestions.AnswerPlaceLevel(asked.Text, asked.Realm, AutonomousChatPlaces.All, asked.Random);
        return text == null ? null : new Fact(text, "data/chat/places.json (AutonomousChatQuestions.AnswerPlaceLevel)", "placelevel");
    }

    private static readonly Regex BgWords = Rx(@" (bg|bgs|battleground|battlegrounds|thid|thidranki|abermenai|murdaigean|murd|caledonia) ");

    /// <summary>"which bg can a lvl 17 go to", "whats the bg for 26", "do u get rps in thid": the battlegrounds by level (every
    /// realm porter's [Battlegrounds]; AutonomousChatLiveAnswers.BattlegroundLevels: 15-19 Abermenai, 20-24 Thidranki, 25-29
    /// Murdaigean, 30-35 Caledonia). Pure.</summary>
    private static Fact Battleground(Asked asked)
    {
        if (!BgWords.IsMatch(asked.Padded) || asked.Has(@" (how do i get|how to get|get to|way to|where is|wheres|portal|porter) "))
            return null;
        const string source = "the battleground brackets (AutonomousChatLiveAnswers.BattlegroundLevels; the realm porters)";
        if (asked.Has(@" (rps|rp|realm points) "))
            return new Fact(Pick(asked.Random, ["yeah, kills there give rps like the frontier", "yep, rps count there, good way to start your rr"]), source, "bg");
        if (!asked.Has(@" (which|what|for|lvl|level|can|go) "))
            return null;
        int level = AutonomousChatLiveAnswers.LevelAsked(asked.Text, asked.Level);
        string bracket = level switch
        {
            >= 15 and <= 19 => "Abermenai", >= 20 and <= 24 => "Thidranki", >= 25 and <= 29 => "Murdaigean", >= 30 and <= 35 => "Caledonia", _ => null,
        };
        string line = bracket != null
            ? Pick(asked.Random, [$"{bracket} at {level}", $"at {level} its {bracket}", $"{bracket}, talk to the porter and say battlegrounds"])
            : Pick(asked.Random, ["the bgs are 15 to 35, abermenai first, then thid, murd and caledonia", "15-19 abermenai, 20-24 thid, 25-29 murd, 30-35 caledonia"]);
        return new Fact(line, source, "bg");
    }

    private static readonly Regex Hunts = Rx(@" (hunt|hunting|xp|exp|level|lvl|leveling|levelling|lvling|solo|soloing|kill|grind|grinding|spot|spots|camp|go|next|now) ");

    private static readonly Regex HuntAsks = Rx(@" (where|wheres|what|any|anywhere|good|best|which|spot|spots|place|places|lf|lfg) ");

    private static readonly Regex HuntNot = Rx(
        @" (trainer|trainers|train|buy|sell|merchant|vendor|bind|df|darkness falls|bg|bgs|battleground|battlegrounds|thid|quest|quests|epic|rvr|frontier|how do i get|how to get|get to|guild|class|spec|dragon|raid|keep|relic|horse|stable|portal|port|vault|bank|drop|drops) ");

    private static readonly Regex HuntLevel = Rx(@" \d{1,2}(s|ish)? | (new chars|newbie|newbies|lowbie|lowbies|low lvl|low level|where now|where next|whats next|after that|bored of|done with|sick of|tired of) ");

    private static readonly Regex Bored = Rx(@" (bored of|done with|sick of|tired of|outleveled|outlevelled) (the )?(?<z>[a-z ]+?) (where|now|what|any|so|$)");

    /// <summary>
    /// "what do i kill at lvl 1", "where do ppl go at 15", "40 merc, where is the xp", "16 here, bored of silvermine. where now?":
    /// where to hunt at a level, from the zones of data/chat/places.json (AutonomousChatLiveAnswers.CatalogZone and HuntText),
    /// leaving out a zone the player is done with. A line the live hunt answer takes ("ask_hunt", the camps bots are in) is left
    /// to it. Pure.
    /// </summary>
    private static Fact Hunt(Asked asked)
    {
        if (!Hunts.IsMatch(asked.Padded) || !HuntAsks.IsMatch(asked.Padded) || HuntNot.IsMatch(asked.Padded) || !HuntLevel.IsMatch(asked.Padded))
            return null;
        if (AutonomousChatShortTalk.QuestionShape(asked.Text) == "ask_hunt")
            return null;
        if (asked.Realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia))
            return null;
        AutonomousChatPlaces.Place done = Bored.Match(asked.Padded) is { Success: true } b
            ? AutonomousChatQuestions.PlaceNamed(b.Groups["z"].Value, AutonomousChatPlaces.All) : null;
        // A place named that the player isn't done with is a question about that place.
        if (done == null && AutonomousChatQuestions.PlaceNamed(asked.Text, AutonomousChatPlaces.All) != null)
            return null;
        int level = Math.Clamp(AutonomousChatLiveAnswers.LevelAsked(asked.Text, asked.Level), 1, 50);
        // "high 30s", "mid 20s", "low 40s": the part of the decade asked.
        if (Regex.Match(asked.Padded, @" (?<part>low|mid|high|upper|lower) (?<n>[1-4]0)s ") is { Success: true } part)
            level = Math.Min(50, int.Parse(part.Groups["n"].Value) + part.Groups["part"].Value switch { "low" or "lower" => 2, "mid" => 5, _ => 8 });
        var places = AutonomousChatPlaces.All.Where(p => p != done && (done == null || !p.Zones.Any(z => done.Zones.Contains(z, StringComparer.OrdinalIgnoreCase))));
        string zone = AutonomousChatLiveAnswers.CatalogZone(asked.Realm, level, places);
        if (zone == null)
            return null;
        string dungeon = level >= 7 && !AutonomousChatShortTalk.AsksSolo(asked.Text) ? AutonomousBotVoices.DungeonFor(asked.Realm, level, asked.Random) : null;
        if (dungeon != null && done != null && string.Equals(dungeon, done.Key, StringComparison.OrdinalIgnoreCase))
            dungeon = null;
        string text = AutonomousChatLiveAnswers.HuntText(asked.Realm, level, zone, dungeon, false, asked.Random);
        return string.IsNullOrWhiteSpace(text) ? null : new Fact(text, "data/chat/places.json (AutonomousChatLiveAnswers.CatalogZone)", "hunt");
    }

    /// <summary>"what does a skald do?", "is a bard needed in groups?": the class talk lines the bots say of the class named
    /// (data/chat/replies_classtalk.lines, each names its class), of the asker's realm or of any. Pure but for the corpus.</summary>
    private static Fact ClassRole(Asked asked)
    {
        string[] asked_ = AutonomousChatShortTalk.ClassWordsIn(asked.Text);
        if (asked_.Length == 0 || asked.Ports?.Corpus?.Lines is not { Length: > 0 } lines)
            return null;
        string realm = RealmCode(asked.Realm);
        var fits = lines.Where(l => l.Intent == "classtalk" && (string.IsNullOrEmpty(l.Realm) || l.Realm == "*" || l.Realm == realm) &&
                                    AutonomousChatShortTalk.ClassWordsIn(l.Text).Any(w => asked_.Any(a => AutonomousChatShortTalk.SameClassWord(w, a)))).ToArray();
        return fits.Length == 0 ? null : new Fact(fits[asked.Random.Next(fits.Length)].Text, "data/chat/replies_classtalk.lines", asked.Topic);
    }
}
