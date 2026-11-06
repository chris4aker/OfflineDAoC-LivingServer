#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DOL.GS.LoreChannels;

// Living Server 1.2: the lore bible as the channels beyond bot chat read it (loremasters, books, the Herald, the codex).
// Shared with the launcher (linked, no server types here). The files are data/lore/locations/*.json and
// data/lore/bosses/*.json (docs/LORE.md); this reads only the fields these channels use and ignores the rest, so the
// lore engine's loader and validator stay the authority on the format. See LoreChannelHub.UseSource to plug that in.

/// <summary>A dungeon, epic zone, lair or raid location.</summary>
public sealed record LoreLocation(string Id, string Name, string Realm, int[] Regions, string Summary, string Story,
    string Legend, LoreLoremasterText Loremaster, LoreBookText Book, string[] Bosses, LoreArea[] Areas)
{
    public int RealmId => LoreRealm.Id(Realm);
}

/// <summary>An atmosphere area: only its circle matters here (books drop near it).</summary>
public sealed record LoreArea(string Name, int X, int Y, int Z, int Radius);

/// <summary>The loremaster's words about a location: an intro and keyword topics ("text with [other keywords]").</summary>
public sealed record LoreLoremasterText(string Intro, IReadOnlyList<KeyValuePair<string, string>> Topics);

/// <summary>A location's book: its title and pages (about 80-120 words each).</summary>
public sealed record LoreBookText(string Title, string[] Pages);

/// <summary>A named boss.</summary>
public sealed record LoreBoss(string Id, string Name, string CatalogKey, string LocationId, string Realm, string Story,
    string Legend, LoreTopic LoremasterTopic, string BookPage, LoreChronicleText Chronicle)
{
    /// <summary>
    /// The script class this boss is (the catalog key's last part is the class name in lower case,
    /// "krondon/scurceolhyrde"; a key may also be the class itself, "ScurceolHyrde").
    /// </summary>
    public bool IsClass(string className)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(CatalogKey)) return false;
        int slash = CatalogKey.LastIndexOf('/');
        string key = slash >= 0 ? CatalogKey[(slash + 1)..] : CatalogKey;
        return string.Equals(key, className, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LoreTopic(string Keyword, string Text);

/// <summary>The Herald's sentence for a raid record, with {realm}, {raidLeader} and {minutes}.</summary>
public sealed record LoreChronicleText(string Win, string Loss);

/// <summary>Where the channels get the lore from. The engine's loader can stand behind it (LoreChannelHub.UseSource).</summary>
public interface ILoreSource
{
    IReadOnlyList<LoreLocation> AllLocations { get; }
    IReadOnlyList<LoreBoss> AllBosses { get; }
    LoreLocation GetLocation(string id);
    LoreBoss GetBoss(string id);
}

public static class LoreRealm
{
    public const int Neutral = 0, Albion = 1, Midgard = 2, Hibernia = 3;
    public static int Id(string realm) => realm?.Trim().ToLowerInvariant() switch
    {
        "albion" or "alb" => Albion,
        "midgard" or "mid" => Midgard,
        "hibernia" or "hib" => Hibernia,
        _ => Neutral,
    };
    public static string Name(int realm) => realm switch { Albion => "Albion", Midgard => "Midgard", Hibernia => "Hibernia", _ => "Neutral" };
    /// <summary>The realm's capital, where its loremaster stands.</summary>
    public static string Capital(int realm) => realm switch { Albion => "Camelot", Midgard => "Jordheim", Hibernia => "Tir na Nog", _ => "" };
    /// <summary>A realm sees its own places and the neutral ones.</summary>
    public static bool Sees(int viewer, int placeRealm) => placeRealm == Neutral || placeRealm == viewer;
}

/// <summary>An in-memory lore source, from parsed files.</summary>
public sealed class LoreLibrary : ILoreSource
{
    private readonly Dictionary<string, LoreLocation> _locations;
    private readonly Dictionary<string, LoreBoss> _bosses;

    public LoreLibrary(IEnumerable<LoreLocation> locations, IEnumerable<LoreBoss> bosses)
    {
        _locations = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoreLocation location in locations ?? []) if (!string.IsNullOrEmpty(location?.Id)) _locations[location.Id] = location;
        _bosses = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoreBoss boss in bosses ?? []) if (!string.IsNullOrEmpty(boss?.Id)) _bosses[boss.Id] = boss;
        AllLocations = _locations.Values.OrderBy(l => l.RealmId == LoreRealm.Neutral ? 9 : l.RealmId).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        AllBosses = _bosses.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static readonly LoreLibrary Empty = new([], []);

    public IReadOnlyList<LoreLocation> AllLocations { get; }
    public IReadOnlyList<LoreBoss> AllBosses { get; }
    public LoreLocation GetLocation(string id) => id != null && _locations.TryGetValue(id, out LoreLocation l) ? l : null;
    public LoreBoss GetBoss(string id) => id != null && _bosses.TryGetValue(id, out LoreBoss b) ? b : null;

    /// <summary>
    /// Parses lore files: (file name, JSON). A path containing "/bosses/" or ".bosses." is a boss, anything else a
    /// location. A file that does not parse is reported in <paramref name="errors"/> and left out.
    /// </summary>
    public static LoreLibrary Parse(IEnumerable<(string Name, string Json)> files, List<string> errors = null)
    {
        var locations = new List<LoreLocation>();
        var bosses = new List<LoreBoss>();
        foreach (var (name, json) in files ?? [])
        {
            try
            {
                string n = (name ?? "").Replace('\\', '/');
                if (n.Contains("/bosses/", StringComparison.OrdinalIgnoreCase) || n.Contains(".bosses.", StringComparison.OrdinalIgnoreCase))
                    bosses.Add(LoreJson.ParseBoss(json));
                else
                    locations.Add(LoreJson.ParseLocation(json));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                errors?.Add($"{name}: {ex.Message}");
            }
        }
        return new LoreLibrary(locations, bosses);
    }
}

public static class LoreSourceExtensions
{
    public static LoreBoss FindBossByClass(this ILoreSource source, string className) =>
        source?.AllBosses.FirstOrDefault(b => b.IsClass(className));

    public static IEnumerable<LoreBoss> BossesOf(this ILoreSource source, LoreLocation location)
    {
        if (source == null || location == null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in location.Bosses ?? [])
            if (source.GetBoss(id) is { } boss && seen.Add(boss.Id)) yield return boss;
        foreach (LoreBoss boss in source.AllBosses)
            if (string.Equals(boss.LocationId, location.Id, StringComparison.OrdinalIgnoreCase) && seen.Add(boss.Id)) yield return boss;
    }

    /// <summary>A location or boss by id or name (case and punctuation ignored).</summary>
    public static string Key(string text) => new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Reads the fields of the lore files these channels use.</summary>
public static class LoreJson
{
    public static LoreLocation ParseLocation(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement e = document.RootElement;
        string id = Required(e, "id");
        LoreLoremasterText loremaster = null;
        if (e.TryGetProperty("loremaster", out JsonElement lm) && lm.ValueKind == JsonValueKind.Object)
        {
            var topics = new List<KeyValuePair<string, string>>();
            if (lm.TryGetProperty("topics", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in t.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Name))
                        topics.Add(new(p.Name.Trim(), p.Value.GetString()));
            loremaster = new LoreLoremasterText(Str(lm, "intro"), topics);
        }
        LoreBookText book = null;
        if (e.TryGetProperty("book", out JsonElement b) && b.ValueKind == JsonValueKind.Object)
            book = new LoreBookText(Str(b, "title"), Strings(b, "pages"));
        var areas = new List<LoreArea>();
        if (e.TryGetProperty("atmosphere", out JsonElement atmosphere) && atmosphere.ValueKind == JsonValueKind.Object &&
            atmosphere.TryGetProperty("areas", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            foreach (JsonElement a in list.EnumerateArray())
                if (a.ValueKind == JsonValueKind.Object)
                    areas.Add(new LoreArea(Str(a, "name"), Int(a, "x"), Int(a, "y"), Int(a, "z"), Int(a, "radius")));
        int[] regions = e.TryGetProperty("regions", out JsonElement r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToArray()
            : [];
        return new LoreLocation(id, Str(e, "name") ?? id, Str(e, "realm") ?? "Neutral", regions, Str(e, "summary"), Str(e, "story"),
            Str(e, "legend"), loremaster, book, Strings(e, "bosses"), areas.ToArray());
    }

    public static LoreBoss ParseBoss(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement e = document.RootElement;
        string id = Required(e, "id");
        LoreTopic topic = null;
        if (e.TryGetProperty("loremasterTopic", out JsonElement t) && t.ValueKind == JsonValueKind.Object &&
            !string.IsNullOrWhiteSpace(Str(t, "keyword")))
            topic = new LoreTopic(Str(t, "keyword").Trim(), Str(t, "text"));
        LoreChronicleText chronicle = null;
        if (e.TryGetProperty("chronicle", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
            chronicle = new LoreChronicleText(Str(c, "win"), Str(c, "loss"));
        return new LoreBoss(id, Str(e, "name") ?? id, Str(e, "catalogKey"), Str(e, "locationId"), Str(e, "realm") ?? "Neutral",
            Str(e, "story"), Str(e, "legend"), topic, Str(e, "bookPage"), chronicle);
    }

    private static string Required(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && Str(e, name) is { Length: > 0 } value ? value
            : throw new FormatException($"the lore file has no \"{name}\"");

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
            : [];
}
