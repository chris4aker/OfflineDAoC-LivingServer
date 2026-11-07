using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The lore bible (Living Server 1.2, docs/LORE.md): one JSON file per location and per named boss under
/// <c>bots/autonomous/data/lore/</c>, embedded in GameServer.dll like the chat corpus. This class parses, validates and
/// indexes them; the channels (screen-centre atmosphere lines, boss yells, bot briefings, tales and answers, the
/// loremaster and books) read from it. Parsing and validation are pure and unit-tested (UT_LoreBible), and every
/// writer's file must pass <see cref="Validate"/>.
/// </summary>
public sealed class LoreBible
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    #region Model

    public sealed record Fact(string Text, string Provenance, string Source);

    /// <summary>A named spot inside a location (a boss room, a hall): its lines show once per session on stepping in.</summary>
    public sealed record Area(string Name, ushort Region, int X, int Y, int Z, int Radius, string[] Lines);

    /// <summary>A line tied to a fight trigger ("health&lt;=50", "addsDead", "orbDeath", "addSpawn").</summary>
    public sealed record PhaseLine(string Trigger, string Text);

    public sealed record Loremaster(string Intro, IReadOnlyDictionary<string, string> Topics);

    /// <summary>One chapter of a long (epic tier) story, in order.</summary>
    public sealed record Chapter(string Title, string Text);

    /// <summary>Another place or boss this one knows ("related": rival, servant, kin, master...).</summary>
    public sealed record Related(string Id, string Relation);

    /// <summary>
    /// One named member of an order file (tier "order": the Darkness Falls court's knights, inquisitors, mutilators): its own
    /// post, story, yells, lair line and loremaster topic. Its name is one of the file's npcNames, which is how the engine
    /// finds the order; the channels speak the order-wide fields, and these are kept and checked for later use.
    /// </summary>
    public sealed record Member(string Name, int Level, string Post, string Story, string[] Aggro, string[] Death, string[] Slay,
        string[] LairEntry, string LoremasterKeyword, string LoremasterText);

    /// <summary>An event in a realm's history.</summary>
    public sealed record HistoryEvent(string Name, string Text, string[] Places, string[] Bosses);

    /// <summary>An era of a realm's history, in order (data/lore/history/&lt;realm&gt;.json "eras", sorted by "order").</summary>
    public sealed record Era(string Id, string Name, string Summary, string Text, HistoryEvent[] Events)
    {
        public int Order { get; init; }
        public string When { get; init; } = string.Empty;
        public Fact[] Facts { get; init; } = [];
        public string[] Places { get; init; } = [];
        public string[] Bosses { get; init; } = [];
    }

    /// <summary>
    /// A power behind the dungeons (Morgana, the Fomorians, the Summoners) and the places and bosses that are its. One id
    /// may appear in several realms' files (summoners, sidhe-of-the-hall): each realm tells it its own way.
    /// </summary>
    public sealed record Faction(string Id, string Name, string Summary, string Text, string[] Places, string[] Bosses)
    {
        public string Realm { get; init; } = string.Empty;
        public Related[] RelatedTo { get; init; } = [];
    }

    /// <summary>A realm's history (data/lore/history/&lt;realm&gt;.json): its eras in order, and its factions.</summary>
    public sealed record History(string Id, string Realm, string Title, string Intro, Era[] Eras, Faction[] Factions)
    {
        public string File { get; init; } = string.Empty;
    }

    public sealed record Book(string Title, string[] Pages);

    /// <summary>
    /// One realm's take on a neutral place or boss ("realmTakes": {"albion": {...}}): its own story, legend, bot lines,
    /// loremaster words or loremaster topic. A field left out falls back to the shared one.
    /// </summary>
    public sealed record RealmTake(string Story, string Legend, string[] Lore, string[] Rumour, string[] Tell, string[] Briefing,
        string[] AftermathWin, string[] AftermathLoss, Loremaster Loremaster, string LoremasterKeyword, string LoremasterText)
    {
        public IEnumerable<(string Field, string[] Lines)> BotLines =>
            [("lore", Lore), ("rumour", Rumour), ("tell", Tell), ("briefing", Briefing), ("aftermathWin", AftermathWin), ("aftermathLoss", AftermathLoss)];
    }

    /// <summary>The realm keys of "realmTakes", as written ("albion") and as the realm is named.</summary>
    public static readonly (string Key, string Realm)[] TakeRealms = [("albion", "Albion"), ("midgard", "Midgard"), ("hibernia", "Hibernia")];

    public static string TakeKey(eRealm realm) => realm switch
    {
        eRealm.Albion => "albion",
        eRealm.Midgard => "midgard",
        eRealm.Hibernia => "hibernia",
        _ => string.Empty,
    };

    public sealed record Location(string Id, string Name, string Realm, ushort[] Regions, string Summary, Fact[] Facts,
        string Story, string Legend, string[] FirstEntry, Area[] Areas, string[] Lore, string[] Rumour, string[] Tell,
        Loremaster Loremaster, Book Book, string[] Bosses)
    {
        /// <summary>Zones that make up the location, for a lair in an open-world region (Sheeroe Hills in region 200). Optional.</summary>
        public ushort[] Zones { get; init; } = [];
        /// <summary>A realm-only line when a raid on this location forms ("atmosphere.raidForming"). Optional.</summary>
        public string[] RaidForming { get; init; } = [];
        /// <summary>Other names players use ("krondon", "the glacier"); the name and id always count. Optional ("aliases").</summary>
        public string[] Aliases { get; init; } = [];
        /// <summary>The era it belongs to, the powers behind it and the places or bosses it knows (history files). Optional.</summary>
        public string Era { get; init; } = string.Empty;
        public string[] Factions { get; init; } = [];
        public Related[] RelatedTo { get; init; } = [];
        /// <summary>The long story in order ("chapters", epic tier); "story" is then its summary. Optional.</summary>
        public Chapter[] Chapters { get; init; } = [];
        /// <summary>"epic" (the epic tier: chapters, a long book), "major", "minor" or empty (the full location floor).</summary>
        public string Tier { get; init; } = string.Empty;
        public bool IsEpic => string.Equals(Tier, "epic", StringComparison.OrdinalIgnoreCase);
        public bool IsMinor => string.Equals(Tier, "minor", StringComparison.OrdinalIgnoreCase);
        /// <summary>Each realm's own take (a neutral place), by "albion", "midgard", "hibernia". Optional ("realmTakes").</summary>
        public IReadOnlyDictionary<string, RealmTake> RealmTakes { get; init; } = new Dictionary<string, RealmTake>();
        public string File { get; init; } = string.Empty;

        public RealmTake TakeFor(eRealm realm) => RealmTakes.TryGetValue(TakeKey(realm), out RealmTake take) ? take : null;
        /// <summary>The "tell" lines a bot of this realm answers with: its realm's take first.</summary>
        public string[] TellFor(eRealm realm) => TakeFor(realm)?.Tell is { Length: > 0 } t ? t : Tell;
        public string[] RumourFor(eRealm realm) => TakeFor(realm)?.Rumour is { Length: > 0 } r ? r : Rumour;
        public string[] LoreFor(eRealm realm) => TakeFor(realm)?.Lore is { Length: > 0 } l ? l : Lore;
    }

    public sealed record Boss(string Id, string Name, string CatalogKey, string LocationId, string Realm, Fact[] Facts,
        string Story, string Legend, string[] AggroYells, PhaseLine[] PhaseYells, string[] DeathYells, string[] SlayYells,
        string[] LairEntry, PhaseLine[] AtmospherePhases, string RealmFirstKill, string Wake, string[] Briefing,
        string[] AftermathWin, string[] AftermathLoss, string[] Rumour, string LoremasterKeyword, string LoremasterText,
        string BookPage, string ChronicleWin, string ChronicleLoss)
    {
        /// <summary>Other names players use ("cuuldurach", "the glimmer king"). Optional ("aliases").</summary>
        public string[] Aliases { get; init; } = [];
        public string Era { get; init; } = string.Empty;
        public string[] Factions { get; init; } = [];
        public Related[] RelatedTo { get; init; } = [];
        /// <summary>The long story in order ("chapters", epic tier); "story" is then its summary. Optional.</summary>
        public Chapter[] Chapters { get; init; } = [];
        /// <summary>"bookPage" as one page or a list of pages (epic bosses: 2-4).</summary>
        public string[] BookPages { get; init; } = [];
        /// <summary>Exact spawn names, for a plain world spawn with no catalog entry or script class (coverage audit). Optional.</summary>
        public string[] NpcNames { get; init; } = [];
        /// <summary>"minor" (level 35-49, the minor depth floor), "epic", "order" (a file of named members) or empty (a full boss).</summary>
        public string Tier { get; init; } = string.Empty;
        public bool IsMinor => string.Equals(Tier, "minor", StringComparison.OrdinalIgnoreCase);
        public bool IsEpic => string.Equals(Tier, "epic", StringComparison.OrdinalIgnoreCase);
        public bool IsOrder => string.Equals(Tier, "order", StringComparison.OrdinalIgnoreCase);
        /// <summary>The named members of an order file ("members"). Optional.</summary>
        public Member[] Members { get; init; } = [];
        /// <summary>Each realm's own take (a neutral boss: Govannon). Optional ("realmTakes").</summary>
        public IReadOnlyDictionary<string, RealmTake> RealmTakes { get; init; } = new Dictionary<string, RealmTake>();
        public string File { get; init; } = string.Empty;

        public RealmTake TakeFor(eRealm realm) => RealmTakes.TryGetValue(TakeKey(realm), out RealmTake take) ? take : null;
        public string[] BriefingFor(eRealm realm) => TakeFor(realm)?.Briefing is { Length: > 0 } b ? b : Briefing;
        public string[] AftermathFor(eRealm realm, bool won) =>
            (won ? TakeFor(realm)?.AftermathWin : TakeFor(realm)?.AftermathLoss) is { Length: > 0 } a ? a : won ? AftermathWin : AftermathLoss;
        public string[] RumourFor(eRealm realm) => TakeFor(realm)?.Rumour is { Length: > 0 } r ? r : Rumour;

        /// <summary>The boss's script class in lower case ("scurceolhyrde"), from a catalog id or a class name.</summary>
        public string ClassKey => ClassKeyOf(CatalogKey);
    }

    public static string ClassKeyOf(string catalogKey)
    {
        if (string.IsNullOrWhiteSpace(catalogKey))
            return string.Empty;
        int slash = catalogKey.LastIndexOf('/');
        return (slash >= 0 ? catalogKey[(slash + 1)..] : catalogKey).Trim().ToLowerInvariant();
    }

    public Location[] Locations { get; }
    public Boss[] Bosses { get; }
    public History[] Histories { get; }
    /// <summary>What went wrong reading the files (a file that is not JSON, a missing id). Validation adds the rest.</summary>
    public IReadOnlyList<string> ParseProblems { get; }

    private readonly Dictionary<string, Location> _locationById;
    private readonly Dictionary<string, Boss> _bossById;
    private readonly Dictionary<ushort, Location[]> _locationsByRegion;
    private readonly Dictionary<ushort, (Location, Area)[]> _areasByRegion;
    private readonly Dictionary<string, Boss[]> _bossesByClass;
    private readonly Dictionary<string, Boss[]> _bossesByLocation;
    private readonly Dictionary<string, Boss> _bossesByName;
    private readonly Dictionary<string, (History, Era)> _eras;
    private readonly Dictionary<string, (History, Faction)> _factions;

    public Era EraById(string id) => id != null && _eras.TryGetValue(id, out var e) ? e.Item2 : null;
    public Faction FactionById(string id) => id != null && _factions.TryGetValue(id, out var f) ? f.Item2 : null;
    public History HistoryFor(string realm) => Histories.FirstOrDefault(h => string.Equals(h.Realm, realm, StringComparison.OrdinalIgnoreCase));

    private LoreBible(Location[] locations, Boss[] bosses, List<string> problems, History[] histories = null)
    {
        Locations = locations;
        Bosses = bosses;
        Histories = histories ?? [];
        _eras = new Dictionary<string, (History, Era)>(StringComparer.OrdinalIgnoreCase);
        _factions = new Dictionary<string, (History, Faction)>(StringComparer.OrdinalIgnoreCase);
        foreach (History history in Histories)
        {
            foreach (Era era in history.Eras)
                if (!string.IsNullOrEmpty(era.Id))
                    _eras.TryAdd(era.Id, (history, era));
            foreach (Faction faction in history.Factions)
                if (!string.IsNullOrEmpty(faction.Id))
                    _factions.TryAdd(faction.Id, (history, faction));
        }
        ParseProblems = problems;
        _locationById = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        foreach (Location location in locations)
            _locationById.TryAdd(location.Id, location);
        _bossById = new Dictionary<string, Boss>(StringComparer.OrdinalIgnoreCase);
        foreach (Boss boss in bosses)
            _bossById.TryAdd(boss.Id, boss);
        _locationsByRegion = locations.SelectMany(l => l.Regions.Distinct().Select(r => (Region: r, Location: l)))
            .GroupBy(p => p.Region).ToDictionary(g => g.Key, g => g.Select(p => p.Location).ToArray());
        _areasByRegion = locations.SelectMany(l => l.Areas.Select(a => (Location: l, Area: a))).GroupBy(p => p.Area.Region)
            .ToDictionary(g => g.Key, g => g.Select(p => (p.Location, p.Area)).ToArray());
        _bossesByClass = bosses.Where(b => b.ClassKey.Length > 0).GroupBy(b => b.ClassKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        _bossesByLocation = bosses.GroupBy(b => b.LocationId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        _bossesByName = new Dictionary<string, Boss>(StringComparer.Ordinal);
        foreach (Boss boss in bosses)
            foreach (string name in boss.NpcNames)
                if (!string.IsNullOrWhiteSpace(name))
                    _bossesByName.TryAdd(name.Trim(), boss);
    }

    public static LoreBible Empty { get; } = new([], [], [], []);

    public Location LocationById(string id) => id != null && _locationById.TryGetValue(id, out Location l) ? l : null;
    public Boss BossById(string id) => id != null && _bossById.TryGetValue(id, out Boss b) ? b : null;
    public Location[] LocationsInRegion(ushort region) => _locationsByRegion.TryGetValue(region, out Location[] l) ? l : [];

    /// <summary>Every location's areas in a region (an area names its own region, so an umbrella's lairs are found too).</summary>
    public (Location Location, Area Area)[] AreasInRegion(ushort region) => _areasByRegion.TryGetValue(region, out var a) ? a : [];
    public Boss[] BossesAt(string locationId) => locationId != null && _bossesByLocation.TryGetValue(locationId, out Boss[] b) ? b : [];

    /// <summary>The boss file for a script class ("ScurceolHyrde"), or null. A class with two files (never valid) gives the first.</summary>
    public Boss BossForClass(string className) =>
        className != null && _bossesByClass.TryGetValue(className.ToLowerInvariant(), out Boss[] b) ? b[0] : null;

    /// <summary>The boss file whose npcNames has this exact spawn name, or null.</summary>
    public Boss BossForName(string npcName) => npcName != null && _bossesByName.TryGetValue(npcName.Trim(), out Boss b) ? b : null;

    /// <summary>A live NPC's boss file: by its script class first, then by its exact name (a plain world spawn).</summary>
    public Boss BossFor(string className, string npcName) => BossForClass(className) ?? BossForName(npcName);

    public bool HasBossClass(string className) => className != null && _bossesByClass.ContainsKey(className.ToLowerInvariant());

    /// <summary>The location a boss lives in, or null.</summary>
    public Location LocationOf(Boss boss) => boss == null ? null : LocationById(boss.LocationId);

    #endregion

    #region Loading

    private static readonly Lazy<LoreBible> Loaded = new(LoadEmbedded);

    /// <summary>The bible built into the server (empty if it could not be read; every channel then stays quiet).</summary>
    public static LoreBible Current => Loaded.Value;

    /// <summary>The embedded lore files: (path below data/lore, JSON).</summary>
    public static IEnumerable<(string Path, string Json)> EmbeddedFiles()
    {
        Assembly assembly = typeof(LoreBible).Assembly;
        const string marker = ".data.lore.";
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(marker, StringComparison.Ordinal) &&
                                                                               n.EndsWith(".json", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
                continue;
            using var reader = new StreamReader(stream);
            // GameServer.bots.autonomous.data.lore.bosses.scurceol-hyrde.json -> bosses/scurceol-hyrde.json
            string tail = name[(name.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            int dot = tail.IndexOf('.');
            string path = dot > 0 && tail.Count(c => c == '.') >= 2 ? tail[..dot] + "/" + tail[(dot + 1)..] : tail;
            yield return (path, reader.ReadToEnd());
        }
    }

    private static LoreBible LoadEmbedded()
    {
        try
        {
            LoreBible bible = Parse(EmbeddedFiles());
            foreach (string problem in bible.ParseProblems)
                Log.Warn("Lore: " + problem);
            if (Log.IsInfoEnabled)
                Log.Info($"Lore: loaded {bible.Locations.Length} locations and {bible.Bosses.Length} bosses");
            return bible;
        }
        catch (Exception exception)
        {
            Log.Error("Lore: could not load the lore bible; lore channels stay quiet", exception);
            return Empty;
        }
    }

    /// <summary>Reads lore files ("locations/krondon.json", JSON). A file in neither folder or not JSON is a problem, not a crash.</summary>
    public static LoreBible Parse(IEnumerable<(string Path, string Json)> files)
    {
        var problems = new List<string>();
        var locations = new List<Location>();
        var bosses = new List<Boss>();
        var histories = new List<History>();
        foreach ((string path, string json) in files)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"{normalized}: not a JSON object");
                    continue;
                }
                if (normalized.StartsWith("locations/", StringComparison.Ordinal))
                    locations.Add(ParseLocation(root, normalized, problems));
                else if (normalized.StartsWith("bosses/", StringComparison.Ordinal))
                    bosses.Add(ParseBoss(root, normalized, problems));
                else if (normalized.StartsWith("history/", StringComparison.Ordinal))
                    histories.Add(ParseHistory(root, normalized, problems));
                else
                    problems.Add($"{normalized}: lore files go in locations/, bosses/ or history/");
            }
            catch (JsonException exception)
            {
                problems.Add($"{normalized}: not valid JSON ({exception.Message})");
            }
        }
        return new LoreBible(locations.ToArray(), bosses.ToArray(), problems, histories.ToArray());
    }

    private static readonly string[] HistoryKeys = ["id", "realm", "title", "intro", "summary", "eras", "factions"];

    private static History ParseHistory(JsonElement root, string file, List<string> problems)
    {
        Unknown(root, HistoryKeys, file, problems);
        var eras = new List<Era>();
        foreach (JsonElement e in Objects(root, "eras", file, problems))
        {
            string where = $"{file} era \"{Str(e, "id")}\"";
            Unknown(e, ["id", "order", "name", "when", "summary", "text", "events", "facts", "places", "bosses"], where, problems);
            var events = new List<HistoryEvent>();
            var facts = new List<Fact>();
            // "events" holds named events ({name, text, places, bosses}) or facts ({text, provenance, source}).
            foreach (JsonElement v in Objects(e, "events", where, problems).Concat(Objects(e, "facts", where, problems)))
            {
                if (v.TryGetProperty("provenance", out _))
                {
                    Unknown(v, ["text", "provenance", "source"], where + " fact", problems);
                    facts.Add(new Fact(Str(v, "text"), Str(v, "provenance"), Str(v, "source")));
                    continue;
                }
                Unknown(v, ["name", "text", "places", "bosses"], where + " event", problems);
                events.Add(new HistoryEvent(Str(v, "name"), Str(v, "text"), Strs(v, "places", where, problems), Strs(v, "bosses", where, problems)));
            }
            eras.Add(new Era(Str(e, "id"), Str(e, "name"), Str(e, "summary"), Str(e, "text"), events.ToArray())
            {
                Order = Int(e, "order", where, problems, 1000 + eras.Count),
                When = Str(e, "when"),
                Facts = facts.ToArray(),
                Places = Strs(e, "places", where, problems),
                Bosses = Strs(e, "bosses", where, problems),
            });
        }
        eras = eras.OrderBy(era => era.Order).ToList();
        var factions = new List<Faction>();
        foreach (JsonElement e in Objects(root, "factions", file, problems))
        {
            string where = $"{file} faction \"{Str(e, "id")}\"";
            Unknown(e, ["id", "name", "realm", "summary", "text", "places", "bosses", "related"], where, problems);
            factions.Add(new Faction(Str(e, "id"), Str(e, "name"), Str(e, "summary"), Str(e, "text"), Strs(e, "places", where, problems), Strs(e, "bosses", where, problems))
            {
                Realm = Str(e, "realm"),
                RelatedTo = ParseRelated(e, where, problems),
            });
        }
        string realmName = Str(root, "realm");
        string id = Str(root, "id");
        return new History(id.Length > 0 ? id : realmName.ToLowerInvariant(), realmName,
            Str(root, "title").Length > 0 ? Str(root, "title") : $"A History of {realmName}",
            Str(root, "intro").Length > 0 ? Str(root, "intro") : Str(root, "summary"), eras.ToArray(), factions.ToArray()) { File = file };
    }

    private static IEnumerable<JsonElement> Objects(JsonElement e, string name, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v) || v.ValueKind == JsonValueKind.Null)
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: \"{name}\" must be a list");
            return [];
        }
        return v.EnumerateArray().Where(x =>
        {
            if (x.ValueKind != JsonValueKind.Object)
                problems.Add($"{where}: \"{name}\" holds a {x.ValueKind}, not an object");
            return x.ValueKind == JsonValueKind.Object;
        }).ToArray();
    }

    private static Chapter[] ParseChapters(JsonElement root, string file, List<string> problems) =>
        Objects(root, "chapters", file, problems).Select(c =>
        {
            Unknown(c, ["title", "text"], file + " chapter", problems);
            return new Chapter(Str(c, "title"), Str(c, "text"));
        }).ToArray();

    /// <summary>"bookPage": one page as text, or a list of pages; "bookPages" (the Neutral writer's spelling) the same.</summary>
    private static string[] BookPagesOf(JsonElement root, string file, List<string> problems) =>
        PagesIn(root, "bookPage", file, problems).Concat(PagesIn(root, "bookPages", file, problems)).ToArray();

    private static IEnumerable<string> PagesIn(JsonElement root, string name, string file, List<string> problems) =>
        root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? Strs(root, name, file, problems).Where(p => !string.IsNullOrWhiteSpace(p))
            : Str(root, name) is { Length: > 0 } page ? [page] : [];

    private static Related[] ParseRelated(JsonElement root, string file, List<string> problems) =>
        Objects(root, "related", file, problems).Select(r =>
        {
            Unknown(r, ["id", "relation"], file + " related", problems);
            return new Related(Str(r, "id"), Str(r, "relation"));
        }).ToArray();

    private static readonly string[] LocationKeys =
        ["id", "name", "realm", "regions", "zones", "aliases", "summary", "facts", "story", "legend", "atmosphere", "botLines", "loremaster", "book", "bosses", "realmTakes",
         "era", "factions", "related", "chapters", "tier"];
    private static readonly string[] LocationAtmosphereKeys = ["firstEntry", "areas", "raidForming"];
    private static readonly string[] AreaKeys = ["name", "region", "x", "y", "z", "radius", "lines"];
    private static readonly string[] LocationBotKeys = ["lore", "rumour", "tell"];
    private static readonly string[] BossKeys =
        ["id", "name", "catalogKey", "locationId", "realm", "aliases", "facts", "story", "legend", "yells", "atmosphere", "botLines", "loremasterTopic", "bookPage", "bookPages", "chronicle", "realmTakes",
         "era", "factions", "related", "npcNames", "tier", "chapters", "members"];
    private static readonly string[] MemberKeys = ["name", "level", "post", "story", "yells", "atmosphere", "loremasterTopic"];
    private static readonly string[] TakeKeys = ["story", "legend", "botLines", "loremaster", "loremasterTopic"];
    private static readonly string[] YellKeys = ["aggro", "phases", "death", "slay"];
    private static readonly string[] BossAtmosphereKeys = ["lairEntry", "phases", "realmFirstKill", "wake"];
    private static readonly string[] BossBotKeys = ["briefing", "aftermathWin", "aftermathLoss", "rumour"];

    private static void Unknown(JsonElement e, string[] known, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return;
        foreach (JsonProperty p in e.EnumerateObject())
            if (!known.Contains(p.Name, StringComparer.Ordinal))
                problems.Add($"{where}: unknown field \"{p.Name}\" (known: {string.Join(", ", known)})");
    }

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string[] Strs(JsonElement e, string name, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v) || v.ValueKind == JsonValueKind.Null)
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: \"{name}\" must be a list of strings");
            return [];
        }
        var list = new List<string>();
        foreach (JsonElement x in v.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.String)
                list.Add(x.GetString() ?? string.Empty);
            else
                problems.Add($"{where}: \"{name}\" holds a {x.ValueKind}, not a string");
        }
        return list.ToArray();
    }

    private static int Int(JsonElement e, string name, string where, List<string> problems, int fallback = 0)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
            return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i))
            return i;
        problems.Add($"{where}: \"{name}\" must be a whole number");
        return fallback;
    }

    private static ushort[] Ushorts(JsonElement e, string name, string where, List<string> problems, int min = 1)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: \"{name}\" must be a list of numbers");
            return [];
        }
        var list = new List<ushort>();
        foreach (JsonElement x in v.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int i) && i >= min && i <= ushort.MaxValue)
                list.Add((ushort)i);
            else
                problems.Add($"{where}: \"{name}\" holds {x}, not a region or zone number");
        }
        return list.ToArray();
    }

    private static Fact[] Facts(JsonElement e, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("facts", out JsonElement v))
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: \"facts\" must be a list");
            return [];
        }
        var facts = new List<Fact>();
        foreach (JsonElement f in v.EnumerateArray())
        {
            Unknown(f, ["text", "provenance", "source"], where + " facts", problems);
            facts.Add(new Fact(Str(f, "text"), Str(f, "provenance"), Str(f, "source")));
        }
        return facts.ToArray();
    }

    private static PhaseLine[] Phases(JsonElement e, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("phases", out JsonElement v))
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: \"phases\" must be a list of {{\"trigger\", \"text\"}}");
            return [];
        }
        var list = new List<PhaseLine>();
        foreach (JsonElement p in v.EnumerateArray())
        {
            Unknown(p, ["trigger", "text"], where + " phases", problems);
            list.Add(new PhaseLine(Str(p, "trigger"), Str(p, "text")));
        }
        return list.ToArray();
    }

    private static Loremaster ParseLoremaster(JsonElement master, string file, List<string> problems)
    {
        if (master.ValueKind != JsonValueKind.Object)
            return null;
        Unknown(master, ["intro", "topics"], file + " loremaster", problems);
        var topics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (master.TryGetProperty("topics", out JsonElement t))
        {
            if (t.ValueKind != JsonValueKind.Object)
                problems.Add($"{file}: \"loremaster.topics\" must be an object of keyword: text");
            else
                foreach (JsonProperty topic in t.EnumerateObject())
                {
                    if (topic.Value.ValueKind != JsonValueKind.String)
                        problems.Add($"{file}: loremaster topic \"{topic.Name}\" must be text");
                    else if (!topics.TryAdd(topic.Name, topic.Value.GetString() ?? string.Empty))
                        problems.Add($"{file}: loremaster topic \"{topic.Name}\" is there twice");
                }
        }
        return new Loremaster(Str(master, "intro"), topics);
    }

    private static IReadOnlyDictionary<string, RealmTake> ParseTakes(JsonElement root, string file, string[] botKeys, List<string> problems)
    {
        var takes = new Dictionary<string, RealmTake>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("realmTakes", out JsonElement all) || all.ValueKind == JsonValueKind.Null)
            return takes;
        if (all.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{file}: \"realmTakes\" must be an object of albion, midgard, hibernia");
            return takes;
        }
        foreach (JsonProperty realm in all.EnumerateObject())
        {
            // "albion" or "Albion" (the Albion writer, PR #94): kept by its lower-case key.
            string key = realm.Name.Trim().ToLowerInvariant();
            string where = $"{file} realmTakes.{realm.Name}";
            if (!TakeRealms.Any(r => r.Key == key))
            {
                problems.Add($"{file}: realmTakes \"{realm.Name}\" is not albion, midgard or hibernia");
                continue;
            }
            if (takes.ContainsKey(key))
            {
                problems.Add($"{file}: realmTakes \"{realm.Name}\" is there twice");
                continue;
            }
            JsonElement e = realm.Value;
            if (e.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object");
                continue;
            }
            Unknown(e, TakeKeys, where, problems);
            JsonElement bot = Obj(e, "botLines");
            Unknown(bot, botKeys, where + " botLines", problems);
            JsonElement topic = Obj(e, "loremasterTopic");
            Unknown(topic, ["keyword", "text"], where + " loremasterTopic", problems);
            takes[key] = new RealmTake(Str(e, "story"), Str(e, "legend"), Strs(bot, "lore", where, problems), Strs(bot, "rumour", where, problems),
                Strs(bot, "tell", where, problems), Strs(bot, "briefing", where, problems), Strs(bot, "aftermathWin", where, problems),
                Strs(bot, "aftermathLoss", where, problems), ParseLoremaster(Obj(e, "loremaster"), where, problems), Str(topic, "keyword"), Str(topic, "text"));
        }
        return takes;
    }

    private static Location ParseLocation(JsonElement root, string file, List<string> problems)
    {
        Unknown(root, LocationKeys, file, problems);
        JsonElement atmosphere = Obj(root, "atmosphere");
        Unknown(atmosphere, LocationAtmosphereKeys, file + " atmosphere", problems);
        ushort[] regions = Ushorts(root, "regions", file, problems);
        var areas = new List<Area>();
        if (atmosphere.ValueKind == JsonValueKind.Object && atmosphere.TryGetProperty("areas", out JsonElement a))
        {
            if (a.ValueKind != JsonValueKind.Array)
                problems.Add($"{file}: \"atmosphere.areas\" must be a list");
            else
                foreach (JsonElement area in a.EnumerateArray())
                {
                    string where = $"{file} area \"{Str(area, "name")}\"";
                    Unknown(area, AreaKeys, where, problems);
                    int region = Int(area, "region", where, problems, regions.Length > 0 ? regions[0] : 0);
                    areas.Add(new Area(Str(area, "name"), (ushort)Math.Clamp(region, 0, ushort.MaxValue), Int(area, "x", where, problems),
                        Int(area, "y", where, problems), Int(area, "z", where, problems), Int(area, "radius", where, problems),
                        Strs(area, "lines", where, problems)));
                }
        }
        JsonElement bot = Obj(root, "botLines");
        Unknown(bot, LocationBotKeys, file + " botLines", problems);
        Loremaster master = ParseLoremaster(Obj(root, "loremaster"), file, problems);
        JsonElement book = Obj(root, "book");
        Unknown(book, ["title", "pages"], file + " book", problems);
        return new Location(Str(root, "id"), Str(root, "name"), Str(root, "realm"), regions, Str(root, "summary"),
            Facts(root, file, problems), Str(root, "story"), Str(root, "legend"), Strs(atmosphere, "firstEntry", file, problems),
            areas.ToArray(), Strs(bot, "lore", file, problems), Strs(bot, "rumour", file, problems), Strs(bot, "tell", file, problems),
            master,
            book.ValueKind == JsonValueKind.Object ? new Book(Str(book, "title"), Strs(book, "pages", file, problems)) : null,
            Strs(root, "bosses", file, problems))
        {
            Zones = Ushorts(root, "zones", file, problems, min: 0),
            RaidForming = Strs(atmosphere, "raidForming", file, problems),
            Aliases = Strs(root, "aliases", file, problems),
            RealmTakes = ParseTakes(root, file, LocationBotKeys, problems),
            Chapters = ParseChapters(root, file, problems),
            Tier = Str(root, "tier"),
            Era = Str(root, "era"),
            Factions = Strs(root, "factions", file, problems),
            RelatedTo = ParseRelated(root, file, problems),
            File = file,
        };
    }

    private static Boss ParseBoss(JsonElement root, string file, List<string> problems)
    {
        Unknown(root, BossKeys, file, problems);
        JsonElement yells = Obj(root, "yells");
        Unknown(yells, YellKeys, file + " yells", problems);
        JsonElement atmosphere = Obj(root, "atmosphere");
        Unknown(atmosphere, BossAtmosphereKeys, file + " atmosphere", problems);
        JsonElement bot = Obj(root, "botLines");
        Unknown(bot, BossBotKeys, file + " botLines", problems);
        JsonElement topic = Obj(root, "loremasterTopic");
        Unknown(topic, ["keyword", "text"], file + " loremasterTopic", problems);
        JsonElement chronicle = Obj(root, "chronicle");
        Unknown(chronicle, ["win", "loss"], file + " chronicle", problems);
        return new Boss(Str(root, "id"), Str(root, "name"), Str(root, "catalogKey"), Str(root, "locationId"), Str(root, "realm"),
            Facts(root, file, problems), Str(root, "story"), Str(root, "legend"), Strs(yells, "aggro", file, problems),
            Phases(yells, file + " yells", problems), Strs(yells, "death", file, problems), Strs(yells, "slay", file, problems),
            Strs(atmosphere, "lairEntry", file, problems), Phases(atmosphere, file + " atmosphere", problems),
            Str(atmosphere, "realmFirstKill"), Str(atmosphere, "wake"), Strs(bot, "briefing", file, problems),
            Strs(bot, "aftermathWin", file, problems), Strs(bot, "aftermathLoss", file, problems), Strs(bot, "rumour", file, problems),
            Str(topic, "keyword"), Str(topic, "text"), string.Join(" ", BookPagesOf(root, file, [])), Str(chronicle, "win"), Str(chronicle, "loss"))
        {
            Aliases = Strs(root, "aliases", file, problems),
            RealmTakes = ParseTakes(root, file, BossBotKeys, problems),
            NpcNames = Strs(root, "npcNames", file, problems),
            Chapters = ParseChapters(root, file, problems),
            BookPages = BookPagesOf(root, file, problems),
            Tier = Str(root, "tier"),
            Era = Str(root, "era"),
            Factions = Strs(root, "factions", file, problems),
            RelatedTo = ParseRelated(root, file, problems),
            Members = ParseMembers(root, file, problems),
            File = file,
        };
    }

    private static Member[] ParseMembers(JsonElement root, string file, List<string> problems)
    {
        if (!root.TryGetProperty("members", out JsonElement v))
            return [];
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{file}: \"members\" must be a list");
            return [];
        }
        var members = new List<Member>();
        foreach (JsonElement m in v.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{file}: each member must be an object");
                continue;
            }
            string where = $"{file} member \"{Str(m, "name")}\"";
            Unknown(m, MemberKeys, where, problems);
            JsonElement yells = Obj(m, "yells");
            Unknown(yells, ["aggro", "death", "slay"], where + " yells", problems);
            JsonElement atmosphere = Obj(m, "atmosphere");
            Unknown(atmosphere, ["lairEntry"], where + " atmosphere", problems);
            JsonElement topic = Obj(m, "loremasterTopic");
            Unknown(topic, ["keyword", "text"], where + " loremasterTopic", problems);
            int level = m.TryGetProperty("level", out JsonElement l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out int i) ? i : 0;
            members.Add(new Member(Str(m, "name"), level, Str(m, "post"), Str(m, "story"), Strs(yells, "aggro", where, problems),
                Strs(yells, "death", where, problems), Strs(yells, "slay", where, problems), Strs(atmosphere, "lairEntry", where, problems),
                Str(topic, "keyword"), Str(topic, "text")));
        }
        return members.ToArray();
    }

    #endregion

    #region Validation

    public const int MaxAtmosphereLength = 60;
    /// <summary>As the corpus build (build_corpus.py MAX_RAW): one chat line as written, in every way of saying it.</summary>
    public const int MaxBotLineLength = 140;
    public const int MaxYellLength = 200;
    /// <summary>
    /// The pages one book item holds (epic tier books are 8-16). A longer book is not cut: the books channel binds it in
    /// volumes of this many pages each (LoreBooks.Volumes), and a page too long for one text window turns into more pages.
    /// </summary>
    public const int MaxBookPages = 16;
    public const int MaxAdvisedWords = 1_500;
    public static readonly string[] Realms = ["Albion", "Midgard", "Hibernia", "Neutral"];
    /// <summary>"myth": the real-world myth a name comes from, not game canon (Balor, Hati and Skoll). Needs its source too.</summary>
    public static readonly string[] Provenances = ["mythic", "game", "legend", "myth", "original"];
    public static readonly string[] ChronicleSlots = ["realm", "raidLeader", "minutes"];
    /// <summary>
    /// The powers every realm tells its own way, the faction ids the writers agreed to share across history files (2026-11-06).
    /// Any other faction id in two realms' files is most likely two writers' different powers under one id: a warning.
    /// </summary>
    public static readonly string[] SharedFactionIds = ["summoners", "lords-of-the-undead", "sidhe-of-the-hall", "dark-ones"];

    /// <summary>
    /// Regions that are a whole realm's open world. A location there without "zones" is an umbrella (hibernia-wilds): its
    /// first-entry line shows once a day on coming into that open world, and its areas mark the lairs. A location with zones
    /// (a dragon's lair) wins inside them.
    /// </summary>
    public static readonly ushort[] OpenWorldRegions = [1, 51, 100, 151, 163, 181, 200];
    public const int MaxAreaRadius = 30_000;

    private static readonly Regex IdRe = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex KeywordRe = new(@"\[([^\[\]|]+)\]", RegexOptions.Compiled);
    private static readonly Regex SlotRe = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);
    /// <summary>A Capitalized word ("Krondon", "Camelot"): one capital, then lower case.</summary>
    private static readonly Regex TitleCaseRe = new(@"\b[A-Z][a-z]", RegexOptions.Compiled);

    /// <summary>What the encounter catalog knows, for <see cref="Validate"/> (built from docs/encounters/epic-encounters.json).</summary>
    public sealed record Catalog(IReadOnlySet<string> Ids, IReadOnlyDictionary<string, string[]> AddsByClass,
        IReadOnlyDictionary<string, string[]> PartnersByClass = null)
    {
        public bool Knows(string key) => !string.IsNullOrWhiteSpace(key) && (Ids.Contains(key.Trim()) || AddsByClass.ContainsKey(ClassKeyOf(key)));
    }

    public static int Words(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Everything a writer's files must satisfy (docs/LORE.md, "What the files must pass"). Empty when the bible is good.
    /// <paramref name="catalog"/> null skips the catalog checks (the server has no catalog at run time).
    /// </summary>
    public List<string> Validate(Catalog catalog, List<string> warnings = null)
    {
        var problems = new List<string>(ParseProblems);
        warnings ??= [];
        void Need(bool ok, string message)
        {
            if (!ok)
                problems.Add(message);
        }
        void CheckId(string id, string file, string folder)
        {
            Need(IdRe.IsMatch(id ?? string.Empty), $"{file}: id \"{id}\" must be lower-case kebab (krondon, scurceol-hyrde)");
            Need(string.Equals(file, $"{folder}/{id}.json", StringComparison.Ordinal), $"{file}: the file must be named {folder}/{id}.json");
        }
        // A link to a place or boss (history places/bosses, related, a location's boss list) may name a file another
        // writer's branch carries or one not written yet: a warning, so the branches can land in any order. The engine and
        // channels skip a link that leads nowhere. Structure (a boss's own locationId, ids, fields) stays a failure.
        void Link(bool ok, string message)
        {
            if (!ok)
                warnings.Add(message + " (a link to a file not in this set: another lore branch's, or not written yet)");
        }
        void CheckFacts(Fact[] facts, string file)
        {
            Need(facts.Length > 0, $"{file}: at least one fact");
            foreach (Fact fact in facts)
            {
                Need(!string.IsNullOrWhiteSpace(fact.Text), $"{file}: a fact without text");
                Need(Provenances.Contains(fact.Provenance), $"{file}: provenance \"{fact.Provenance}\" must be one of {string.Join(", ", Provenances)}");
                Need(fact.Provenance == "original" || !string.IsNullOrWhiteSpace(fact.Source),
                    $"{file}: a {fact.Provenance} fact needs its source (URL, document or file): {Short(fact.Text)}");
            }
        }
        // Lengths are advisory (owner 2026-11-06: exhaustive lore; no upper limit, a short text is a warning, not a failure).
        // The depth floor is DepthReport's.
        void CheckWords(string text, int low, int high, string what, string file)
        {
            if (Words(text) < low)
                warnings.Add($"{file}: {what} is {Words(text)} words (at least {low} reads as a story)");
            else if (Words(text) > MaxAdvisedWords)
                warnings.Add($"{file}: {what} is {Words(text)} words (over {MaxAdvisedWords} is long for one sitting)");
        }
        void CheckChapters(Chapter[] chapters, string file)
        {
            foreach (Chapter chapter in chapters)
            {
                Need(!string.IsNullOrWhiteSpace(chapter.Title), $"{file}: a chapter without a title");
                Need(!string.IsNullOrWhiteSpace(chapter.Text), $"{file}: chapter \"{chapter.Title}\" has no text");
                Need((chapter.Title ?? string.Empty).Length <= 60 && !(chapter.Title ?? string.Empty).Contains('['),
                    $"{file}: chapter title \"{chapter.Title}\" is a loremaster keyword: 60 characters at most, no brackets");
                if (Words(chapter.Text) is var words && words > 0 && (words < 250 || words > 700))
                    warnings.Add($"{file}: chapter \"{chapter.Title}\" is {words} words (250-700 reads best)");
            }
            foreach (var same in chapters.GroupBy(c => (c.Title ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"{file}: chapter title \"{same.Key}\" is there twice");
        }
        void CheckAtmosphere(IEnumerable<string> lines, string what, string file)
        {
            foreach (string line in lines)
            {
                Need(!string.IsNullOrWhiteSpace(line), $"{file}: an empty {what} line");
                Need((line ?? string.Empty).Length <= MaxAtmosphereLength,
                    $"{file}: {what} line is {line?.Length} characters (max {MaxAtmosphereLength}): {line}");
                Need(!(line ?? string.Empty).Contains('[') && !(line ?? string.Empty).Contains('{'), $"{file}: {what} lines are said as written (no [a|b], no {{slots}}): {line}");
            }
        }
        void CheckBotLines(IEnumerable<string> lines, string what, string file)
        {
            foreach (string line in lines)
            {
                if (!LoreText.TryParse(line, out string error))
                {
                    problems.Add($"{file}: {what} line: {error}: {line}");
                    continue;
                }
                // 2003 chat voice: lower case, but shouting in caps is the era ("SPREAD", "MT"); a Capitalized word is not.
                Need(!TitleCaseRe.IsMatch(line), $"{file}: {what} lines are in 2003 chat voice, lower case (caps for shouting only): {line}");
                Need(!SlotRe.IsMatch(line), $"{file}: {what} lines take no {{slots}}: {line}");
                foreach (string said in LoreText.Expand(line))
                    Need(said.Length <= MaxBotLineLength, $"{file}: {what} line is {said.Length} characters as said (max {MaxBotLineLength}): {said}");
            }
        }
        void CheckYells(IEnumerable<string> lines, string what, string file)
        {
            foreach (string line in lines)
            {
                Need(!string.IsNullOrWhiteSpace(line), $"{file}: an empty {what} yell");
                Need((line ?? string.Empty).Length <= MaxYellLength, $"{file}: {what} yell is {line?.Length} characters (max {MaxYellLength})");
                Need(LoreText.TryParse(line, out string error), $"{file}: {what} yell: {error}");
            }
        }
        void CheckTriggers(PhaseLine[] phases, Boss boss, string what)
        {
            foreach (PhaseLine phase in phases)
            {
                if (!LoreFightRules.TryParseTrigger(phase.Trigger, out LoreFightRules.Trigger trigger))
                {
                    problems.Add($"{boss.File}: {what} trigger \"{phase.Trigger}\" is not health<=N (1-99), addsSpawn, orbDeath, addsDead, " +
                                 "formChange, partnerDead, death, wake, playerDeath or a mechanic (" + string.Join(", ", LoreFightRules.MechanicTriggers) + ")");
                    continue;
                }
                Need(!string.IsNullOrWhiteSpace(phase.Text), $"{boss.File}: {what} \"{phase.Trigger}\" has no text");
                if (catalog != null && trigger.NeedsAdds)
                    Need(catalog.AddsByClass.TryGetValue(boss.ClassKey, out string[] adds) && adds.Length > 0,
                        $"{boss.File}: {what} trigger \"{phase.Trigger}\" needs adds, and the catalog lists none for {boss.CatalogKey}");
                if (catalog?.PartnersByClass != null && trigger.NeedsPartners)
                    Need(catalog.PartnersByClass.TryGetValue(boss.ClassKey, out string[] mates) && mates.Length > 0,
                        $"{boss.File}: {what} trigger \"{phase.Trigger}\" needs partners, and the catalog lists none for {boss.CatalogKey}");
            }
        }

        // Locations
        foreach (var group in Locations.GroupBy(l => l.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"location id \"{group.Key}\" is used by {string.Join(", ", group.Select(l => l.File))}");
        var bossKeywords = Bosses.GroupBy(b => b.LocationId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(b => new[] { b.LoremasterKeyword }.Concat(b.RealmTakes.Values.Select(t => t.LoremasterKeyword)))
                .Where(k => !string.IsNullOrWhiteSpace(k)).ToArray(), StringComparer.OrdinalIgnoreCase);
        void CheckTakes(IReadOnlyDictionary<string, RealmTake> takes, string f, int storyLow, int storyHigh)
        {
            foreach ((string realm, RealmTake take) in takes)
            {
                string what = $"realmTakes.{realm}";
                if (!string.IsNullOrWhiteSpace(take.Story))
                    CheckWords(take.Story, storyLow, storyHigh, what + ".story", f);
                if (!string.IsNullOrWhiteSpace(take.Legend))
                    CheckWords(take.Legend, 30, 140, what + ".legend", f);
                foreach ((string field, string[] lines) in take.BotLines)
                    CheckBotLines(lines, $"{what}.{field}", f);
                Need(string.IsNullOrWhiteSpace(take.LoremasterKeyword) == string.IsNullOrWhiteSpace(take.LoremasterText),
                    $"{f}: {what}.loremasterTopic needs both keyword and text");
            }
        }
        foreach (Location l in Locations)
        {
            string f = l.File;
            CheckId(l.Id, f, "locations");
            Need(!string.IsNullOrWhiteSpace(l.Name), $"{f}: a name");
            Need(Realms.Contains(l.Realm), $"{f}: realm \"{l.Realm}\" must be one of {string.Join(", ", Realms)}");
            // "regions": [] is allowed (shrouded-isles: Gnat has no known region); such a location has no first entry and its
            // areas name their own region.
            Need(l.Zones.Length == 0 || l.Regions.Length > 0, $"{f}: \"zones\" need the region they are in");
            Need(!string.IsNullOrWhiteSpace(l.Summary), $"{f}: a summary");
            CheckFacts(l.Facts, f);
            CheckWords(l.Story, 130, 460, "story", f);
            CheckWords(l.Legend, 30, 140, "legend", f);
            Need(l.FirstEntry.Length > 0, $"{f}: at least one atmosphere.firstEntry line");
            CheckAtmosphere(l.FirstEntry, "firstEntry", f);
            CheckAtmosphere(l.RaidForming, "raidForming", f);
            foreach (Area area in l.Areas)
            {
                Need(!string.IsNullOrWhiteSpace(area.Name), $"{f}: an area without a name");
                Need(area.Radius >= 100 && area.Radius <= MaxAreaRadius, $"{f}: area \"{area.Name}\" radius {area.Radius} (want 100-{MaxAreaRadius})");
                Need(area.X != 0 || area.Y != 0, $"{f}: area \"{area.Name}\" has no x/y (the room's position in region coordinates)");
                Need(l.Regions.Length == 0 ? area.Region > 0 : l.Regions.Contains(area.Region),
                    l.Regions.Length == 0 ? $"{f}: area \"{area.Name}\" needs its \"region\" (the location lists none)"
                        : $"{f}: area \"{area.Name}\" region {area.Region} is not one of the location's regions");
                Need(area.Lines.Length > 0, $"{f}: area \"{area.Name}\" has no lines");
                CheckAtmosphere(area.Lines, $"area \"{area.Name}\"", f);
            }
            CheckBotLines(l.Lore, "lore", f);
            CheckBotLines(l.Rumour, "rumour", f);
            CheckBotLines(l.Tell, "tell", f);
            Need(l.Tell.Length > 0, $"{f}: at least one botLines.tell line (the answer to \"what's in {l.Name}?\")");
            Need(l.Lore.Length + l.Rumour.Length + l.Tell.Length >= 3, $"{f}: at least 3 bot lines");
            if (l.Loremaster == null)
                problems.Add($"{f}: a loremaster block (intro and topics)");
            else
            {
                Need(!string.IsNullOrWhiteSpace(l.Loremaster.Intro), $"{f}: loremaster.intro");
                var known = new HashSet<string>(l.Loremaster.Topics.Keys, StringComparer.OrdinalIgnoreCase);
                if (bossKeywords.TryGetValue(l.Id ?? string.Empty, out string[] extra))
                    known.UnionWith(extra);
                Loremaster[] takes = l.RealmTakes.Values.Select(t => t.Loremaster).Where(m => m != null).ToArray();
                foreach (Loremaster take in takes)
                    known.UnionWith(take.Topics.Keys);
                foreach (string text in new[] { l.Loremaster }.Concat(takes).SelectMany(m => new[] { m.Intro }.Concat(m.Topics.Values)))
                    foreach (Match m in KeywordRe.Matches(text ?? string.Empty))
                        Need(known.Contains(m.Groups[1].Value.Trim()),
                            $"{f}: loremaster keyword [{m.Groups[1].Value}] has no topic here or in one of its bosses' loremasterTopic");
            }
            CheckTakes(l.RealmTakes, f, 60, 460);   // a take is one realm's angle, shorter than the story
            Need(string.IsNullOrEmpty(l.Tier) || l.IsEpic || l.IsMinor || string.Equals(l.Tier, "major", StringComparison.OrdinalIgnoreCase),
                $"{f}: a location's tier is \"epic\", \"major\" or \"minor\" (or left out), not \"{l.Tier}\"");
            CheckChapters(l.Chapters, f);
            CheckGameTalk(InWorldTexts(l.Story, l.Chapters, l.Book?.Pages ?? [], "book page", l.RealmTakes), f, warnings);
            if (l.Book == null)
                problems.Add($"{f}: a book (title and pages)");
            else
            {
                Need(!string.IsNullOrWhiteSpace(l.Book.Title), $"{f}: book.title");
                Need(l.Book.Pages.Length > 0, $"{f}: at least one book page");
                if (l.Book.Pages.Length > MaxBookPages)
                    warnings.Add($"{f}: {l.Book.Pages.Length} book pages: bound in {(l.Book.Pages.Length + MaxBookPages - 1) / MaxBookPages} volumes of {MaxBookPages}");
                foreach (string page in l.Book.Pages)
                    CheckWords(page, 50, 160, "a book page", f);
            }
            foreach (string id in l.Bosses)
            {
                Boss boss = BossById(id);
                if (boss == null)
                    Link(false, $"{f}: boss \"{id}\" has no file bosses/{id}.json");
                else
                    Need(string.Equals(boss.LocationId, l.Id, StringComparison.OrdinalIgnoreCase),
                        $"{f}: boss \"{id}\" says its locationId is \"{boss.LocationId}\"");
            }
            Need(l.Bosses.Distinct(StringComparer.OrdinalIgnoreCase).Count() == l.Bosses.Length, $"{f}: a boss is listed twice");
        }

        // Bosses
        foreach (var group in Bosses.GroupBy(b => b.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"boss id \"{group.Key}\" is used by {string.Join(", ", group.Select(b => b.File))}");
        foreach (var group in Bosses.Where(b => b.ClassKey.Length > 0).GroupBy(b => b.ClassKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"catalogKey \"{group.First().CatalogKey}\" is used by {string.Join(", ", group.Select(b => b.File))}");
        foreach (var group in Bosses.SelectMany(b => b.NpcNames.Select(n => (Name: n.Trim(), Boss: b))).Where(p => p.Name.Length > 0)
                     .GroupBy(p => p.Name, StringComparer.Ordinal).Where(g => g.Select(p => p.Boss.Id).Distinct().Count() > 1))
            problems.Add($"npcNames \"{group.Key}\" is claimed by {string.Join(", ", group.Select(p => p.Boss.File).Distinct())}");
        foreach (Boss b in Bosses)
        {
            string f = b.File;
            CheckId(b.Id, f, "bosses");
            Need(!string.IsNullOrWhiteSpace(b.Name), $"{f}: a name");
            Need(Realms.Contains(b.Realm), $"{f}: realm \"{b.Realm}\" must be one of {string.Join(", ", Realms)}");
            Need(!string.IsNullOrWhiteSpace(b.CatalogKey) || b.NpcNames.Any(n => !string.IsNullOrWhiteSpace(n)),
                $"{f}: a catalogKey (the epic-encounters.json id or the script class) or npcNames (exact spawn names)");
            foreach (string name in b.NpcNames)
                Need(!string.IsNullOrWhiteSpace(name) && name == name.Trim(), $"{f}: npcNames \"{name}\" must be the exact spawn name");
            if (catalog != null && !string.IsNullOrWhiteSpace(b.CatalogKey))
                Need(catalog.Knows(b.CatalogKey), $"{f}: catalogKey \"{b.CatalogKey}\" is no id or script class in docs/encounters/epic-encounters.json");
            Location home = LocationById(b.LocationId);
            if (home == null)
                problems.Add($"{f}: locationId \"{b.LocationId}\" has no file locations/{b.LocationId}.json");
            else
            {
                Link(home.Bosses.Contains(b.Id, StringComparer.OrdinalIgnoreCase), $"{f}: its location {home.File} does not list it in \"bosses\"");
                Need(home.Realm == "Neutral" || home.Realm == b.Realm, $"{f}: realm {b.Realm}, but its location is {home.Realm}");
            }
            CheckFacts(b.Facts, f);
            CheckWords(b.Story, 70, 345, "story", f);
            CheckWords(b.Legend, 30, 140, "legend", f);
            CheckYells(b.AggroYells, "aggro", f);
            CheckYells(b.DeathYells, "death", f);
            CheckYells(b.SlayYells, "slay", f);
            CheckYells(b.PhaseYells.Select(p => p.Text), "phase", f);
            CheckTriggers(b.PhaseYells, b, "yells.phases");
            CheckTriggers(b.AtmospherePhases, b, "atmosphere.phases");
            CheckAtmosphere(b.LairEntry, "lairEntry", f);
            CheckAtmosphere(b.AtmospherePhases.Select(p => p.Text), "atmosphere phase", f);
            CheckAtmosphere(string.IsNullOrEmpty(b.RealmFirstKill) ? [] : [b.RealmFirstKill], "realmFirstKill", f);
            CheckAtmosphere(string.IsNullOrEmpty(b.Wake) ? [] : [b.Wake], "wake", f);
            Need(b.LairEntry.Length + b.AtmospherePhases.Length + (b.RealmFirstKill.Length > 0 ? 1 : 0) + (b.Wake.Length > 0 ? 1 : 0) > 0,
                $"{f}: at least one atmosphere line");
            CheckBotLines(b.Briefing, "briefing", f);
            CheckBotLines(b.AftermathWin, "aftermathWin", f);
            CheckBotLines(b.AftermathLoss, "aftermathLoss", f);
            CheckBotLines(b.Rumour, "rumour", f);
            CheckTakes(b.RealmTakes, f, 40, 345);
            Need(b.Briefing.Length + b.AftermathWin.Length + b.AftermathLoss.Length + b.Rumour.Length >= 3, $"{f}: at least 3 bot lines");
            Need(string.IsNullOrWhiteSpace(b.LoremasterKeyword) == string.IsNullOrWhiteSpace(b.LoremasterText),
                $"{f}: loremasterTopic needs both keyword and text");
            foreach (string page in b.BookPages)
                CheckWords(page, 50, 160, "bookPage", f);
            Need(string.IsNullOrEmpty(b.Tier) || b.IsMinor || b.IsEpic || b.IsOrder || string.Equals(b.Tier, "major", StringComparison.OrdinalIgnoreCase),
                $"{f}: tier \"{b.Tier}\" must be minor, major, epic or order (or left out)");
            Need(b.Members.Length == 0 || b.IsOrder, $"{f}: \"members\" belong to a tier \"order\" file");
            foreach (Member member in b.Members)
            {
                string mf = $"{f} member \"{member.Name}\"";
                Need(!string.IsNullOrWhiteSpace(member.Name) && b.NpcNames.Contains(member.Name, StringComparer.Ordinal),
                    $"{mf}: a member's name must be one of the file's npcNames");
                CheckYells(member.Aggro, "aggro", mf);
                CheckYells(member.Death, "death", mf);
                CheckYells(member.Slay, "slay", mf);
                CheckAtmosphere(member.LairEntry, "lairEntry", mf);
                Need(string.IsNullOrWhiteSpace(member.LoremasterKeyword) == string.IsNullOrWhiteSpace(member.LoremasterText),
                    $"{mf}: loremasterTopic needs both keyword and text");
            }
            foreach (var group in b.Members.GroupBy(m => m.Name ?? string.Empty, StringComparer.Ordinal).Where(g => g.Count() > 1))
                problems.Add($"{f}: member \"{group.Key}\" is listed {group.Count()} times");
            CheckGameTalk(b.Members.Select(m => ($"member \"{m.Name}\" story", m.Story)), f, warnings);
            CheckChapters(b.Chapters, f);
            CheckGameTalk(InWorldTexts(b.Story, b.Chapters, b.BookPages, "bookPage", b.RealmTakes), f, warnings);
            foreach ((string text, string what) in new[] { (b.ChronicleWin, "chronicle.win"), (b.ChronicleLoss, "chronicle.loss") })
                foreach (Match m in SlotRe.Matches(text ?? string.Empty))
                    Need(ChronicleSlots.Contains(m.Groups[1].Value), $"{f}: {what} slot {{{m.Groups[1].Value}}} (only {{realm}}, {{raidLeader}}, {{minutes}})");
        }

        // History (data/lore/history/<realm>.json) and the links into it: era, factions, related.
        foreach (var group in Histories.GroupBy(h => h.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"history id \"{group.Key}\" is used by {string.Join(", ", group.Select(h => h.File))}");
        var historyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (History h in Histories)
        {
            string f = h.File;
            CheckId(h.Id, f, "history");
            Need(Realms.Contains(h.Realm), $"{f}: realm \"{h.Realm}\" must be one of {string.Join(", ", Realms)}");
            Need(h.Eras.Length > 0, $"{f}: at least one era");
            // Era ids are unique everywhere; a faction id may be shared by several realms' files (summoners), not twice in one.
            foreach ((string id, string name, string what) in h.Eras.Select(e => (e.Id, e.Name, "era")).Concat(h.Factions.Select(x => (x.Id, x.Name, "faction"))))
            {
                Need(IdRe.IsMatch(id ?? string.Empty), $"{f}: {what} id \"{id}\" must be lower-case kebab");
                Need(!string.IsNullOrWhiteSpace(name), $"{f}: {what} \"{id}\" needs a name");
                string key = what == "era" ? "era:" + id : $"faction:{id}@{f}";
                if (!string.IsNullOrEmpty(id) && !historyIds.TryAdd(key, f))
                    problems.Add($"{f}: {what} id \"{id}\" is also used in {historyIds[key]}");
                if (what == "era" && !string.IsNullOrEmpty(id) && historyIds.TryGetValue("faction-any:" + id, out string other))
                    problems.Add($"{f}: era id \"{id}\" is also a faction in {other}");
                if (what == "faction")
                    historyIds.TryAdd("faction-any:" + id, f);
            }
            foreach (Era era in h.Eras)
                foreach (string id in h.Factions.Select(x => x.Id))
                    Need(!string.Equals(era.Id, id, StringComparison.OrdinalIgnoreCase), $"{f}: \"{id}\" is both an era and a faction");
            foreach (Era era in h.Eras)
            {
                Need(!string.IsNullOrWhiteSpace(era.Text) || !string.IsNullOrWhiteSpace(era.Summary), $"{f}: era \"{era.Id}\" has no text");
                foreach (string place in era.Places)
                    Link(LocationById(place) != null, $"{f}: era \"{era.Id}\" place \"{place}\" has no location file");
                foreach (string boss in era.Bosses)
                    Link(BossById(boss) != null, $"{f}: era \"{era.Id}\" boss \"{boss}\" has no boss file");
                foreach (Fact fact in era.Facts)
                {
                    Need(Provenances.Contains(fact.Provenance), $"{f}: provenance \"{fact.Provenance}\" must be one of {string.Join(", ", Provenances)}");
                    Need(fact.Provenance == "original" || !string.IsNullOrWhiteSpace(fact.Source), $"{f}: a {fact.Provenance} fact needs its source: {Short(fact.Text)}");
                }
                foreach (HistoryEvent v in era.Events)
                {
                    Need(!string.IsNullOrWhiteSpace(v.Name) && !string.IsNullOrWhiteSpace(v.Text), $"{f}: an event of era \"{era.Id}\" needs a name and text");
                    foreach (string place in v.Places)
                        Link(LocationById(place) != null, $"{f}: event \"{v.Name}\" place \"{place}\" has no location file");
                    foreach (string boss in v.Bosses)
                        Link(BossById(boss) != null, $"{f}: event \"{v.Name}\" boss \"{boss}\" has no boss file");
                }
            }
            foreach (Faction faction in h.Factions)
            {
                Need(!string.IsNullOrWhiteSpace(faction.Text) || !string.IsNullOrWhiteSpace(faction.Summary), $"{f}: faction \"{faction.Id}\" has no text");
                foreach (string place in faction.Places)
                    Link(LocationById(place) != null, $"{f}: faction \"{faction.Id}\" place \"{place}\" has no location file");
                foreach (string boss in faction.Bosses)
                    Link(BossById(boss) != null, $"{f}: faction \"{faction.Id}\" boss \"{boss}\" has no boss file");
                foreach (Related r in faction.RelatedTo)
                    Link(LocationById(r.Id) != null || BossById(r.Id) != null || FactionById(r.Id) != null,
                        $"{f}: faction \"{faction.Id}\" related \"{r.Id}\" is no location, boss or faction");
            }
        }
        foreach (var shared in Histories.SelectMany(h => h.Factions.Where(x => !string.IsNullOrEmpty(x.Id)).Select(x => (x.Id, h.File)))
                     .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(p => p.File).Distinct().Count() > 1))
            if (!SharedFactionIds.Contains(shared.Key, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"faction id \"{shared.Key}\" is in {string.Join(", ", shared.Select(p => p.File).Distinct())}: only " +
                             $"{string.Join(", ", SharedFactionIds)} are shared by agreement; is it one power or two?");
        // An era or faction must resolve once its realm's history file exists; before that (a writer still drafting it) it
        // is a warning, so the files can land in any order.
        void CheckHistoryLink(bool found, string realm, string message)
        {
            if (found)
                return;
            if (Histories.Any(h => string.Equals(h.Realm, realm, StringComparison.OrdinalIgnoreCase)))
                problems.Add(message);
            else
                warnings.Add(message + $" (no {realm} history file yet)");
        }
        void CheckLinks(string file, string realm, string era, string[] factions, Related[] related, string self)
        {
            if (!string.IsNullOrWhiteSpace(era))
                CheckHistoryLink(EraById(era) != null, realm, $"{file}: era \"{era}\" is in no history file");
            foreach (string faction in factions)
                CheckHistoryLink(FactionById(faction) != null, realm, $"{file}: faction \"{faction}\" is in no history file");
            foreach (Related r in related)
            {
                Need(!string.IsNullOrWhiteSpace(r.Relation), $"{file}: related \"{r.Id}\" needs a relation (rival, servant, kin...)");
                Link(LocationById(r.Id) != null || BossById(r.Id) != null, $"{file}: related \"{r.Id}\" is no location or boss");
                Need(!string.Equals(r.Id, self, StringComparison.OrdinalIgnoreCase), $"{file}: related to itself");
            }
        }
        foreach (Location l in Locations)
            CheckLinks(l.File, l.Realm, l.Era, l.Factions, l.RelatedTo, l.Id);
        foreach (Boss b in Bosses)
            CheckLinks(b.File, LocationOf(b)?.Realm ?? b.Realm, b.Era, b.Factions, b.RelatedTo, b.Id);
        return problems;
    }

    /// <summary>
    /// How many entries sit below the depth floor (owner 2026-11-06, LORE-1.2-DEPTH.md), one line per floor: "locations:
    /// story 500+ words: 12 of 46 below". Advisory: the schema test prints it so the depth can be tracked. Pure.
    /// </summary>
    public List<string> DepthReport()
    {
        static int W(string t) => Words(t);
        var floors = new (string What, Func<Location, bool> Short)[]
        {
            ("story 500+ words", l => W(l.Story) < 500),
            ("legend 120+ words", l => W(l.Legend) < 120),
            ("6+ facts", l => l.Facts.Length < 6),
            ("4-8 firstEntry lines", l => l.FirstEntry.Length < 4),
            ("an area per room (2+ areas)", l => l.Areas.Length < 2),
            ("lore 12+, rumour 8+, tell 6+", l => l.Lore.Length < 12 || l.Rumour.Length < 8 || l.Tell.Length < 6),
            ("loremaster 8+ topics", l => (l.Loremaster?.Topics.Count ?? 0) < 8),
            ("book 4-8 pages of 100+ words", l => l.Book == null || l.Book.Pages.Length < 4 || l.Book.Pages.Any(p => W(p) < 100)),
            ("an era and a faction", l => string.IsNullOrWhiteSpace(l.Era) || l.Factions.Length == 0),
        };
        var minorFloors = new (string What, Func<Boss, bool> Short)[]
        {
            ("story 150+ words", b => W(b.Story) < 150),
            ("legend 60+ words", b => W(b.Legend) < 60),
            ("2+ facts", b => b.Facts.Length < 2),
            ("aggro 2+, death 1+, slay 1+ yells", b => b.AggroYells.Length < 2 || b.DeathYells.Length < 1 || b.SlayYells.Length < 1),
            ("lairEntry 1+", b => b.LairEntry.Length < 1),
            ("briefing, aftermathWin, rumour 2+ each", b => b.Briefing.Length < 2 || b.AftermathWin.Length < 2 || b.Rumour.Length < 2),
            ("loremasterTopic and chronicle win", b => string.IsNullOrWhiteSpace(b.LoremasterKeyword) || string.IsNullOrWhiteSpace(b.ChronicleWin)),
        };
        var bossFloors = new (string What, Func<Boss, bool> Short)[]
        {
            ("story 250+ words", b => W(b.Story) < 250),
            ("legend 100+ words", b => W(b.Legend) < 100),
            ("aggro 3+, death 2+, slay 3+ yells", b => b.AggroYells.Length < 3 || b.DeathYells.Length < 2 || b.SlayYells.Length < 3),
            ("lairEntry 2+ and realmFirstKill", b => b.LairEntry.Length < 2 || string.IsNullOrWhiteSpace(b.RealmFirstKill)),
            ("briefing, aftermathWin, aftermathLoss, rumour 4+ each",
                b => b.Briefing.Length < 4 || b.AftermathWin.Length < 4 || b.AftermathLoss.Length < 4 || b.Rumour.Length < 4),
            ("loremasterTopic and bookPage 100+ words", b => string.IsNullOrWhiteSpace(b.LoremasterKeyword) || b.BookPages.Length == 0 || b.BookPages.Any(p => W(p) < 100)),
            ("chronicle win and loss", b => string.IsNullOrWhiteSpace(b.ChronicleWin) || string.IsNullOrWhiteSpace(b.ChronicleLoss)),
            ("an era, a faction or related", b => string.IsNullOrWhiteSpace(b.Era) && b.Factions.Length == 0 && b.RelatedTo.Length == 0),
        };
        static int ChapterWords(Chapter[] c) => c.Sum(x => W(x.Text));
        var epicPlaces = new (string What, Func<Location, bool> Short)[]
        {
            ("4-10 chapters", l => l.Chapters.Length < 4 || l.Chapters.Length > 10),
            ("chapters 3,000+ words", l => ChapterWords(l.Chapters) < 3000),
            ("story summary 300-500 words", l => W(l.Story) < 300 || W(l.Story) > 500),
            ("book 8-16 pages", l => l.Book == null || l.Book.Pages.Length < 8),
        };
        var epicBosses = new (string What, Func<Boss, bool> Short)[]
        {
            ("4+ chapters", b => b.Chapters.Length < 4),
            ("chapters 1,000+ words (1,800+ for raid finals and dragons)", b => ChapterWords(b.Chapters) < 1000),
            ("story summary 300-500 words", b => W(b.Story) < 300 || W(b.Story) > 500),
            ("bookPage 2-4 pages", b => b.BookPages.Length < 2),
        };
        var report = new List<string>();
        foreach (var (what, isShort) in floors)
            report.Add($"locations: {what}: {Locations.Count(isShort)} of {Locations.Length} below");
        Boss[] major = Bosses.Where(b => !b.IsMinor && !b.IsEpic).ToArray();
        Boss[] minor = Bosses.Where(b => b.IsMinor).ToArray();
        foreach (var (what, isShort) in bossFloors)
            report.Add($"bosses: {what}: {major.Count(isShort)} of {major.Length} below");
        foreach (var (what, isShort) in minorFloors)
            report.Add($"minor bosses: {what}: {minor.Count(isShort)} of {minor.Length} below");
        Location[] epicL = Locations.Where(l => l.IsEpic).ToArray();
        Boss[] epicB = Bosses.Where(b => b.IsEpic).ToArray();
        foreach (var (what, isShort) in epicPlaces)
            report.Add($"epic locations: {what}: {epicL.Count(isShort)} of {epicL.Length} below");
        foreach (var (what, isShort) in epicBosses)
            report.Add($"epic bosses: {what}: {epicB.Count(isShort)} of {epicB.Length} below");
        foreach (string realm in Realms.Take(3))
            report.Add($"history: {realm}: {(HistoryFor(realm) is { } h ? $"{h.Eras.Length} eras, {h.Factions.Length} factions" : "no file yet")}");
        return report;
    }

    private static string Short(string text) => text == null ? string.Empty : text.Length <= 50 ? text : text[..50] + "...";

    /// <summary>
    /// Fight-guide and game talk (owner rule 2026-11-06 19:26, "chapters are not a fight guide"): the patterns of the
    /// writers' self-check, scratchpad lore/chapter_lint.py, kept the same. Chapters, story and books are history told
    /// inside the world; fight advice belongs in legend, bot lines and yells only.
    /// </summary>
    public static readonly string[] GameTalkPatterns =
    [
        @"\braid(s|ers?|ing)?\b", @"\braid[- ]leaders?\b", @"\bhealers?\b", @"\btanks?\b", @"\bpull(s|ed|ing)?\b",
        @"\baggro\b", @"\bdebuffs?\b", @"\bresists?\b", @"\bmechanics?\b", @"\bphases?\b", @"\bexperience\b",
        @"\bxp\b", @"\brespec\b", @"\bloot\b", @"\bdrops?\b", @"\bspawns?\b", @"\bplayers?\b", @"\bserver\b",
        @"\bper fight\b", @"\ba fight\b", @"\bthe fight\b", @"\bevery fight\b", @"\bthe lesson\b", @"\bfirst rule\b",
        @"\bbrace\b", @"\bstack(s|ing)?\b", @"\bspread out\b", @"\badds\b", @"\bimmune\b", @"\bimmunity\b",
        @"\bdamage\b", @"\bhit points?\b", @"\bmindfully\b", @"\bguild(s|halls?)?\b", @"\bherald\b",
        @"\bwhoever (comes|fights)\b", @"\bif you\b", @"\byou (must|should|will need)\b",
    ];

    private static readonly Regex GameTalkRe = new(string.Join("|", GameTalkPatterns), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SentenceEndRe = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    /// <summary>The sentences of a text that read as fight-guide or game talk, with the word that flagged each. Pure.</summary>
    public static IEnumerable<(string Word, string Sentence)> GameTalk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        foreach (string sentence in SentenceEndRe.Split(text))
            if (GameTalkRe.Match(sentence) is { Success: true } m)
                yield return (m.Value, sentence.Trim());
    }

    /// <summary>The in-world texts of a file that must not read as a fight guide: (where, text).</summary>
    private static IEnumerable<(string Where, string Text)> InWorldTexts(string story, Chapter[] chapters, IEnumerable<string> pages, string pageName,
        IReadOnlyDictionary<string, RealmTake> takes)
    {
        foreach (Chapter chapter in chapters)
            yield return ($"chapter \"{chapter.Title}\"", chapter.Text);
        yield return ("story", story);
        int i = 0;
        foreach (string page in pages)
            yield return ($"{pageName} {++i}", page);
        foreach ((string realm, RealmTake take) in takes)
            yield return ($"realmTakes.{realm}.story", take.Story);
    }

    private static void CheckGameTalk(IEnumerable<(string Where, string Text)> texts, string file, List<string> warnings)
    {
        foreach ((string where, string text) in texts)
            foreach ((string word, string sentence) in GameTalk(text))
                warnings.Add($"{file}: game talk in {where} ('{word}'; chapters, story and books are history, not a fight guide): {(sentence.Length <= 160 ? sentence : sentence[..160] + "...")}");
    }

    #endregion

    #region Questions

    private static string Plain(string text) =>
        " " + new string((text ?? string.Empty).ToLowerInvariant().Replace("'", string.Empty).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>The names a player may use for a place or a boss: its name, id words, aliases, and the name without "the"/"'s lair".</summary>
    public static IEnumerable<string> NamesOf(string name, string id, IEnumerable<string> aliases)
    {
        var names = new List<string> { name, (id ?? string.Empty).Replace('-', ' ') };
        names.AddRange(aliases ?? []);
        string plain = Collapse(Plain(name)).Trim();
        // "Cuuldurach the Glimmer King" -> "cuuldurach"; "Aidon the Archwizard" -> "aidon"
        int the = plain.IndexOf(" the ", StringComparison.Ordinal);
        if (the > 2)
            names.Add(plain[..the]);
        if (plain.EndsWith(" s lair", StringComparison.Ordinal))
            names.Add(plain[..^7]);
        return names.Select(n => Collapse(Plain(n)).Trim()).Where(n => n.Length >= 4).Distinct();
    }

    /// <summary>
    /// "whats in krondon", "who is cuuldurach?", "tell me about the storm keeper": the place or boss a question is about, or
    /// nulls. Only a question (a "?", or what/who/tell me/heard of/ever been) counts; the longest name found wins. Pure.
    /// </summary>
    public (Location Location, Boss Boss) Subject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);
        string plain = Collapse(Plain(text));
        bool asks = text.Contains('?') || Regex.IsMatch(plain,
            @" (what s|whats|what is|whats in|what lives|who s|whos|who is|who was|tell me about|tell me of|know about|heard of|ever been|story of|story behind|lore of|lore on) ");
        // "where is krondon?" and "how do i get to krondon" are the directions route's (AutonomousChatKnowledge.TryAnswerLocation).
        if (!asks || Regex.IsMatch(plain, @" (where|wheres|how do i get|how to get|way to|directions|route to) "))
            return (null, null);
        Location bestLocation = null;
        Boss bestBoss = null;
        int best = 0;
        foreach (Boss boss in Bosses)
            foreach (string name in NamesOf(boss.Name, boss.Id, boss.Aliases))
                if (name.Length > best && plain.Contains(" " + name + " ", StringComparison.Ordinal))
                {
                    best = name.Length;
                    bestBoss = boss;
                    bestLocation = null;
                }
        foreach (Location location in Locations)
            foreach (string name in NamesOf(location.Name, location.Id, location.Aliases))
                if (name.Length > best && plain.Contains(" " + name + " ", StringComparison.Ordinal))
                {
                    best = name.Length;
                    bestLocation = location;
                    bestBoss = null;
                }
        return (bestLocation, bestBoss);
    }

    /// <summary>
    /// A bot's answer to a question about a place or boss, in chat voice: a place's "tell" lines, else its rumours; a
    /// boss's rumours, else its briefing (what a raid leader would say). "[a|b]" choices are picked. Pure but for the random.
    /// </summary>
    public bool TryAnswer(string text, Random random, out string answer, eRealm realm = eRealm.None)
    {
        answer = string.Empty;
        (Location location, Boss boss) = Subject(text);
        // The asker's realm's take first (a neutral place or boss told its realm's way).
        string[] pool = location != null
            ? (location.TellFor(realm) is { Length: > 0 } tell ? tell : location.RumourFor(realm) is { Length: > 0 } rumour ? rumour : location.LoreFor(realm))
            : boss != null
                ? (boss.RumourFor(realm) is { Length: > 0 } said ? said : boss.BriefingFor(realm))
                : [];
        if (pool.Length == 0)
            return false;
        answer = LoreText.Pick(pool[random.Next(pool.Length)], random);
        return answer.Length > 0;
    }

    #endregion
}

/// <summary>The "[a|b]" choices of bot lines (the corpus's .lines syntax, tools/bot-voices/build_corpus.py). Pure.</summary>
public static class LoreText
{
    public const int FamilyCap = 24;
    private static readonly Regex ChoiceRe = new(@"\[([^\[\]]*)\]", RegexOptions.Compiled);

    /// <summary>Whether a line's choices are well formed: no nesting, every "[...]" holds a "|", brackets balanced.</summary>
    public static bool TryParse(string text, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty line";
            return false;
        }
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '[' && ++depth > 1)
            {
                error = "a \"[\" inside another (choices do not nest)";
                return false;
            }
            if (c == ']' && --depth < 0)
            {
                error = "a \"]\" without its \"[\"";
                return false;
            }
        }
        if (depth != 0)
        {
            error = "a \"[\" without its \"]\"";
            return false;
        }
        foreach (Match m in ChoiceRe.Matches(text))
            if (!m.Groups[1].Value.Contains('|'))
            {
                error = $"\"[{m.Groups[1].Value}]\" has no \"|\" (a choice needs two ways, \"[a|]\" for a or nothing)";
                return false;
            }
        return true;
    }

    private static string Tidy(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim().Replace(" ,", ",").Replace(" ?", "?").Replace(" !", "!").Replace(" .", ".");

    /// <summary>Every way to say the line, up to <see cref="FamilyCap"/> (then a fixed sample, as build_corpus.py does).</summary>
    public static string[] Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        MatchCollection matches = ChoiceRe.Matches(text);
        if (matches.Count == 0)
            return [Tidy(text)];
        string[][] groups = matches.Select(m => m.Groups[1].Value.Split('|')).ToArray();
        long total = groups.Aggregate(1L, (n, g) => n * g.Length);
        IEnumerable<int[]> combos;
        if (total <= FamilyCap)
        {
            var all = new List<int[]> { new int[groups.Length] };
            for (int g = 0; g < groups.Length; g++)
                all = all.SelectMany(c => Enumerable.Range(0, groups[g].Length).Select(i =>
                {
                    int[] next = (int[])c.Clone();
                    next[g] = i;
                    return next;
                })).ToList();
            combos = all;
        }
        else
        {
            var rng = new Random(StableHash(text));
            var picked = new HashSet<string>();
            var list = new List<int[]>();
            for (int tries = 0; list.Count < FamilyCap && tries < FamilyCap * 20; tries++)
            {
                int[] c = groups.Select(g => rng.Next(g.Length)).ToArray();
                if (picked.Add(string.Join(",", c)))
                    list.Add(c);
            }
            combos = list;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (int[] combo in combos)
        {
            int i = 0;
            string said = Tidy(ChoiceRe.Replace(text, _ => groups[i][combo[i++]]));
            if (said.Length > 0 && seen.Add(said))
                result.Add(said);
        }
        return result.ToArray();
    }

    /// <summary>One way of saying the line, at random.</summary>
    public static string Pick(string text, Random random)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return Tidy(ChoiceRe.Replace(text, m =>
        {
            string[] options = m.Groups[1].Value.Split('|');
            return options[random.Next(options.Length)];
        }));
    }

    /// <summary>A hash that is the same on every run (string.GetHashCode is not).</summary>
    public static int StableHash(string text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in text ?? string.Empty)
                hash = (hash ^ c) * 16777619;
            return hash & 0x7fffffff;
        }
    }
}
