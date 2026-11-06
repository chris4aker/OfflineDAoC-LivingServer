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
    /// <summary>What went wrong reading the files (a file that is not JSON, a missing id). Validation adds the rest.</summary>
    public IReadOnlyList<string> ParseProblems { get; }

    private readonly Dictionary<string, Location> _locationById;
    private readonly Dictionary<string, Boss> _bossById;
    private readonly Dictionary<ushort, Location[]> _locationsByRegion;
    private readonly Dictionary<ushort, (Location, Area)[]> _areasByRegion;
    private readonly Dictionary<string, Boss[]> _bossesByClass;
    private readonly Dictionary<string, Boss[]> _bossesByLocation;

    private LoreBible(Location[] locations, Boss[] bosses, List<string> problems)
    {
        Locations = locations;
        Bosses = bosses;
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
    }

    public static LoreBible Empty { get; } = new([], [], []);

    public Location LocationById(string id) => id != null && _locationById.TryGetValue(id, out Location l) ? l : null;
    public Boss BossById(string id) => id != null && _bossById.TryGetValue(id, out Boss b) ? b : null;
    public Location[] LocationsInRegion(ushort region) => _locationsByRegion.TryGetValue(region, out Location[] l) ? l : [];

    /// <summary>Every location's areas in a region (an area names its own region, so an umbrella's lairs are found too).</summary>
    public (Location Location, Area Area)[] AreasInRegion(ushort region) => _areasByRegion.TryGetValue(region, out var a) ? a : [];
    public Boss[] BossesAt(string locationId) => locationId != null && _bossesByLocation.TryGetValue(locationId, out Boss[] b) ? b : [];

    /// <summary>The boss file for a script class ("ScurceolHyrde"), or null. A class with two files (never valid) gives the first.</summary>
    public Boss BossForClass(string className) =>
        className != null && _bossesByClass.TryGetValue(className.ToLowerInvariant(), out Boss[] b) ? b[0] : null;

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
                else
                    problems.Add($"{normalized}: lore files go in locations/ or bosses/");
            }
            catch (JsonException exception)
            {
                problems.Add($"{normalized}: not valid JSON ({exception.Message})");
            }
        }
        return new LoreBible(locations.ToArray(), bosses.ToArray(), problems);
    }

    private static readonly string[] LocationKeys =
        ["id", "name", "realm", "regions", "zones", "aliases", "summary", "facts", "story", "legend", "atmosphere", "botLines", "loremaster", "book", "bosses", "realmTakes"];
    private static readonly string[] LocationAtmosphereKeys = ["firstEntry", "areas", "raidForming"];
    private static readonly string[] AreaKeys = ["name", "region", "x", "y", "z", "radius", "lines"];
    private static readonly string[] LocationBotKeys = ["lore", "rumour", "tell"];
    private static readonly string[] BossKeys =
        ["id", "name", "catalogKey", "locationId", "realm", "aliases", "facts", "story", "legend", "yells", "atmosphere", "botLines", "loremasterTopic", "bookPage", "chronicle", "realmTakes"];
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

    private static ushort[] Ushorts(JsonElement e, string name, string where, List<string> problems)
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
            if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int i) && i is > 0 and <= ushort.MaxValue)
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
            Zones = Ushorts(root, "zones", file, problems),
            RaidForming = Strs(atmosphere, "raidForming", file, problems),
            Aliases = Strs(root, "aliases", file, problems),
            RealmTakes = ParseTakes(root, file, LocationBotKeys, problems),
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
            Str(topic, "keyword"), Str(topic, "text"), Str(root, "bookPage"), Str(chronicle, "win"), Str(chronicle, "loss"))
        {
            Aliases = Strs(root, "aliases", file, problems),
            RealmTakes = ParseTakes(root, file, BossBotKeys, problems),
            File = file,
        };
    }

    #endregion

    #region Validation

    public const int MaxAtmosphereLength = 60;
    public const int MaxBotLineLength = AutonomousBotVoiceCorpus.MaxLength;
    public const int MaxYellLength = 200;
    public static readonly string[] Realms = ["Albion", "Midgard", "Hibernia", "Neutral"];
    /// <summary>"myth": the real-world myth a name comes from, not game canon (Balor, Hati and Skoll). Needs its source too.</summary>
    public static readonly string[] Provenances = ["mythic", "game", "legend", "myth", "original"];
    public static readonly string[] ChronicleSlots = ["realm", "raidLeader", "minutes"];

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
    public List<string> Validate(Catalog catalog)
    {
        var problems = new List<string>(ParseProblems);
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
        void CheckWords(string text, int low, int high, string what, string file) =>
            Need(Words(text) >= low && Words(text) <= high, $"{file}: {what} is {Words(text)} words (want {low}-{high})");
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
                Need(line == line.ToLowerInvariant(), $"{file}: {what} lines are in 2003 chat voice, lower case: {line}");
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
                                 "formChange, partnerDead, death, wake, playerDeath, teleport, berserk, breath or glare");
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
            CheckTakes(l.RealmTakes, f, 130, 460);
            if (l.Book == null)
                problems.Add($"{f}: a book (title and pages)");
            else
            {
                Need(!string.IsNullOrWhiteSpace(l.Book.Title), $"{f}: book.title");
                Need(l.Book.Pages.Length > 0, $"{f}: at least one book page");
                foreach (string page in l.Book.Pages)
                    CheckWords(page, 50, 160, "a book page", f);
            }
            foreach (string id in l.Bosses)
            {
                Boss boss = BossById(id);
                if (boss == null)
                    problems.Add($"{f}: boss \"{id}\" has no file bosses/{id}.json");
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
        foreach (Boss b in Bosses)
        {
            string f = b.File;
            CheckId(b.Id, f, "bosses");
            Need(!string.IsNullOrWhiteSpace(b.Name), $"{f}: a name");
            Need(Realms.Contains(b.Realm), $"{f}: realm \"{b.Realm}\" must be one of {string.Join(", ", Realms)}");
            Need(!string.IsNullOrWhiteSpace(b.CatalogKey), $"{f}: a catalogKey (the epic-encounters.json id or the script class)");
            if (catalog != null && !string.IsNullOrWhiteSpace(b.CatalogKey))
                Need(catalog.Knows(b.CatalogKey), $"{f}: catalogKey \"{b.CatalogKey}\" is no id or script class in docs/encounters/epic-encounters.json");
            Location home = LocationById(b.LocationId);
            if (home == null)
                problems.Add($"{f}: locationId \"{b.LocationId}\" has no file locations/{b.LocationId}.json");
            else
            {
                Need(home.Bosses.Contains(b.Id, StringComparer.OrdinalIgnoreCase), $"{f}: its location {home.File} does not list it in \"bosses\"");
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
            CheckTakes(b.RealmTakes, f, 70, 345);
            Need(b.Briefing.Length + b.AftermathWin.Length + b.AftermathLoss.Length + b.Rumour.Length >= 3, $"{f}: at least 3 bot lines");
            Need(string.IsNullOrWhiteSpace(b.LoremasterKeyword) == string.IsNullOrWhiteSpace(b.LoremasterText),
                $"{f}: loremasterTopic needs both keyword and text");
            if (!string.IsNullOrWhiteSpace(b.BookPage))
                CheckWords(b.BookPage, 50, 160, "bookPage", f);
            foreach ((string text, string what) in new[] { (b.ChronicleWin, "chronicle.win"), (b.ChronicleLoss, "chronicle.loss") })
                foreach (Match m in SlotRe.Matches(text ?? string.Empty))
                    Need(ChronicleSlots.Contains(m.Groups[1].Value), $"{f}: {what} slot {{{m.Groups[1].Value}}} (only {{realm}}, {{raidLeader}}, {{minutes}})");
        }
        return problems;
    }

    private static string Short(string text) => text == null ? string.Empty : text.Length <= 50 ? text : text[..50] + "...";

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
