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
    /// <summary>The era, the powers behind it and the places or bosses it knows (the history files). Empty when none.</summary>
    public string Era { get; init; } = "";
    public string[] Factions { get; init; } = [];
    public LoreRelated[] Related { get; init; } = [];
    /// <summary>The long story in order (epic tier); <see cref="Story"/> is then its summary. Empty when none.</summary>
    public LoreChapter[] Chapters { get; init; } = [];
    /// <summary>Each realm's own take on a neutral place, by realm id ("realmTakes", Summoner's Hall). Empty when none.</summary>
    public IReadOnlyDictionary<int, LoreRealmTake> RealmTakes { get; init; } = new Dictionary<int, LoreRealmTake>();

    /// <summary>The place as a reader of this realm meets it: its realm's take where there is one.</summary>
    public LoreLocation ForRealm(int realm) => RealmTakes.TryGetValue(realm, out LoreRealmTake take)
        ? this with
        {
            Story = string.IsNullOrWhiteSpace(take.Story) ? Story : take.Story,
            Legend = string.IsNullOrWhiteSpace(take.Legend) ? Legend : take.Legend,
            Loremaster = Merge(Loremaster, take.Loremaster),
        }
        : this;

    /// <summary>A realm's loremaster words over the shared ones: its intro, and its topics beside (or over) the shared topics.</summary>
    public static LoreLoremasterText Merge(LoreLoremasterText shared, LoreLoremasterText take)
    {
        if (take == null) return shared;
        if (shared == null) return take;
        var topics = take.Topics.ToList();
        topics.AddRange(shared.Topics.Where(s => !take.Topics.Any(t => string.Equals(t.Key, s.Key, StringComparison.OrdinalIgnoreCase))));
        return new LoreLoremasterText(string.IsNullOrWhiteSpace(take.Intro) ? shared.Intro : take.Intro, topics);
    }
}

/// <summary>One realm's take on a neutral place or boss: whatever it gives replaces the shared text for that realm.</summary>
public sealed record LoreRealmTake(string Story, string Legend, LoreLoremasterText Loremaster, LoreTopic LoremasterTopic);

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
    public string Era { get; init; } = "";
    public string[] Factions { get; init; } = [];
    public LoreRelated[] Related { get; init; } = [];
    /// <summary>Exact spawn names, for a plain world spawn with no catalog key ("npcNames"). Empty when none.</summary>
    public string[] NpcNames { get; init; } = [];
    public LoreChapter[] Chapters { get; init; } = [];
    /// <summary>"bookPage" as pages (one, or 2-4 for an epic boss); <see cref="BookPage"/> is their text together.</summary>
    public string[] BookPages { get; init; } = [];
    /// <summary>Each realm's own take on a neutral boss, by realm id ("realmTakes", Govannon). Empty when none.</summary>
    public IReadOnlyDictionary<int, LoreRealmTake> RealmTakes { get; init; } = new Dictionary<int, LoreRealmTake>();

    /// <summary>The boss as a reader of this realm meets it: its realm's take where there is one.</summary>
    public LoreBoss ForRealm(int realm) => RealmTakes.TryGetValue(realm, out LoreRealmTake take)
        ? this with
        {
            Story = string.IsNullOrWhiteSpace(take.Story) ? Story : take.Story,
            Legend = string.IsNullOrWhiteSpace(take.Legend) ? Legend : take.Legend,
            LoremasterTopic = take.LoremasterTopic ?? LoremasterTopic,
        }
        : this;

    public bool IsClass(string className)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(CatalogKey)) return false;
        int slash = CatalogKey.LastIndexOf('/');
        string key = slash >= 0 ? CatalogKey[(slash + 1)..] : CatalogKey;
        return string.Equals(key, className, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LoreTopic(string Keyword, string Text);

/// <summary>Another place or boss this one knows: rival, servant, kin, master.</summary>
public sealed record LoreRelated(string Id, string Relation);

public sealed record LoreEventText(string Name, string Text, string[] Places, string[] Bosses);

/// <summary>An era of a realm's history, in order.</summary>
public sealed record LoreEraText(string Id, string Name, string Summary, string Text, LoreEventText[] Events)
{
    public string When { get; init; } = "";
    public string[] Places { get; init; } = [];
    public string[] Bosses { get; init; } = [];
}

/// <summary>A power behind the dungeons and the places and bosses that are its.</summary>
public sealed record LoreFactionText(string Id, string Name, string Summary, string Text, string[] Places, string[] Bosses)
{
    public LoreRelated[] Related { get; init; } = [];
}

/// <summary>A realm's history (data/lore/history/&lt;realm&gt;.json): eras in order, and factions.</summary>
public sealed record LoreHistoryText(string Id, string Realm, string Title, string Intro, LoreEraText[] Eras, LoreFactionText[] Factions)
{
    public int RealmId => LoreRealm.Id(Realm);
}

/// <summary>One chapter of a long (epic tier) story.</summary>
public sealed record LoreChapter(string Title, string Text);

/// <summary>
/// Long lore in readable pieces, never cut inside a word, in order. The limits are the code's own:
/// <list type="bullet">
/// <item>The hard limit: every TCP packet the server sends is at most 2,048 bytes. PacketProcessor.ValidatePacketSize
/// (GameServer/packets/Server/PacketProcessor.cs, "if (packetSize &lt;= 2048)") discards a bigger one whole, with only a
/// staff message to the player, so an over-long popup or chat message is not shortened but lost.</item>
/// <item>A loremaster's words go out with GameNPC.SayTo, the popup window: one Message packet each
/// (PacketLib1127.SendRawMessage: the packet header, the chat type, "##", then "&lt;Name&gt; says, \"...\"" and a
/// zero; the server itself writes the whole string). <see cref="SayChars"/> keeps each piece far inside the packet
/// (<see cref="SayBytes"/>) and short enough to read in the popup.</item>
/// <item>A text window (a book page, /lore) is one DetailWindow packet: PacketLib168.WriteCustomTextWindowData stops at
/// MAX_PACKET_LENGTH (2,048, PacketLib168.cs) and drops the rest silently, and each line is a Pascal string of at most
/// 255 characters. Text is code page 1252, one byte a character. <see cref="PageChars"/> leaves room for the caption, the
/// line headers and the footer (<see cref="WindowBytes"/>).</item>
/// </list>
/// </summary>
public static class LorePieces
{
    public const int SayChars = 480;
    public const int PageChars = 1_500;
    public const int PacketBytes = 2_048;

    /// <summary>The bytes of the Message packet SayTo sends for <paramref name="piece"/> (PacketLib1127.SendRawMessage).</summary>
    public static int SayBytes(string speaker, string piece) =>
        3 /* size and code */ + 1 /* chat type */ + 2 /* "##" */ + $"{speaker} says, \"{piece}\"".Length + 1 /* zero */;

    /// <summary>
    /// The bytes SendCustomTextWindow writes for a window (PacketLib181.cs, the biggest form): the header, two flag bytes,
    /// the caption as a Pascal string, each line as its number and a Pascal string per 255 characters, and the trailing zero.
    /// </summary>
    public static int WindowBytes(string caption, IEnumerable<string> lines) =>
        3 + 2 + 1 + Math.Min(255, (caption ?? "").Length) + lines.Sum(l => Math.Max(1, (l.Length + 254) / 255) * 2 + l.Length) + 1;

    private static readonly System.Text.RegularExpressions.Regex Sentence = new(@"(?<=[.!?][""')\]]?)\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The text in pieces of at most <paramref name="max"/> characters: whole paragraphs packed together while they fit
    /// (paragraphs are lines of the text), a longer paragraph by sentences, a longer sentence by words. Pure.
    /// </summary>
    public static List<string> Split(string text, int max = SayChars)
    {
        var pieces = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return pieces;
        max = Math.Max(20, max);
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0) pieces.Add(current.ToString());
            current.Clear();
        }
        void Add(string chunk)
        {
            if (current.Length > 0 && current.Length + 1 + chunk.Length > max) Flush();
            if (current.Length > 0) current.Append(' ');
            current.Append(chunk);
        }
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            List<string> chunks = Chunks(paragraph, max);
            // A paragraph that needs more than one piece starts a piece of its own; whole short paragraphs share one.
            if (chunks.Count > 1) Flush();
            foreach (string chunk in chunks) Add(chunk);
        }
        Flush();
        return pieces;
    }

    /// <summary>A paragraph in chunks of at most <paramref name="max"/>: sentences packed, a long sentence by words.</summary>
    private static List<string> Chunks(string paragraph, int max)
    {
        var chunks = new List<string>();
        if (paragraph.Length <= max) { chunks.Add(paragraph); return chunks; }
        var line = new System.Text.StringBuilder();
        void Put(string unit)
        {
            if (line.Length > 0 && line.Length + 1 + unit.Length > max) { chunks.Add(line.ToString()); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(unit);
        }
        foreach (string sentence in Sentence.Split(paragraph).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            if (sentence.Length <= max) { Put(sentence); continue; }
            foreach (string word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word;
                // Only a single word longer than a whole piece is ever cut.
                while (w.Length > max) { if (line.Length > 0) { chunks.Add(line.ToString()); line.Clear(); } chunks.Add(w[..max]); w = w[max..]; }
                Put(w);
            }
        }
        if (line.Length > 0) chunks.Add(line.ToString());
        return chunks;
    }
}

/// <summary>A lore source that also has the realms' histories.</summary>
public interface ILoreHistorySource
{
    IReadOnlyList<LoreHistoryText> Histories { get; }
}

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

/// <summary>A lore source as one realm's readers meet it: every place and boss with that realm's take (LoreRealmTake).</summary>
public sealed class LoreRealmView : ILoreSource, ILoreHistorySource
{
    /// <summary>
    /// The histories with this realm's own first, then the neutral one: a faction id several realms share (summoners,
    /// dark-ones, LoreBible.SharedFactionIds) is found in the reader's realm's telling.
    /// </summary>
    public IReadOnlyList<LoreHistoryText> Histories =>
        _inner.Histories().OrderBy(h => h.RealmId == _realm ? 0 : h.RealmId == LoreRealm.Neutral ? 1 : 2).ToList();
    private readonly ILoreSource _inner;
    private readonly int _realm;

    private LoreRealmView(ILoreSource inner, int realm)
    {
        _inner = inner;
        _realm = realm;
        AllLocations = inner.AllLocations.Select(l => l.ForRealm(realm)).ToArray();
        AllBosses = inner.AllBosses.Select(b => b.ForRealm(realm)).ToArray();
    }

    /// <summary>The view for a realm (a view of a view is re-made from the source underneath).</summary>
    public static ILoreSource For(ILoreSource source, int realm) =>
        source == null ? LoreLibrary.Empty : source is LoreRealmView view ? (view._realm == realm ? view : new LoreRealmView(view._inner, realm)) : new LoreRealmView(source, realm);

    public IReadOnlyList<LoreLocation> AllLocations { get; }
    public IReadOnlyList<LoreBoss> AllBosses { get; }
    public LoreLocation GetLocation(string id) => _inner.GetLocation(id)?.ForRealm(_realm);
    public LoreBoss GetBoss(string id) => _inner.GetBoss(id)?.ForRealm(_realm);
}

/// <summary>An in-memory lore source, from parsed files.</summary>
public sealed class LoreLibrary : ILoreSource, ILoreHistorySource
{
    private readonly Dictionary<string, LoreLocation> _locations;
    private readonly Dictionary<string, LoreBoss> _bosses;

    public IReadOnlyList<LoreHistoryText> Histories { get; }

    public LoreLibrary(IEnumerable<LoreLocation> locations, IEnumerable<LoreBoss> bosses, IEnumerable<LoreHistoryText> histories = null)
    {
        Histories = (histories ?? []).Where(h => h != null).ToArray();
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
        var histories = new List<LoreHistoryText>();
        foreach (var (name, json) in files ?? [])
        {
            try
            {
                string n = (name ?? "").Replace('\\', '/');
                if (n.Contains("/bosses/", StringComparison.OrdinalIgnoreCase) || n.Contains(".bosses.", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("bosses/", StringComparison.OrdinalIgnoreCase))
                    bosses.Add(LoreJson.ParseBoss(json));
                else if (n.Contains("/history/", StringComparison.OrdinalIgnoreCase) || n.Contains(".history.", StringComparison.OrdinalIgnoreCase) ||
                         n.StartsWith("history/", StringComparison.OrdinalIgnoreCase))
                    histories.Add(LoreJson.ParseHistory(json));
                else
                    locations.Add(LoreJson.ParseLocation(json));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                errors?.Add($"{name}: {ex.Message}");
            }
        }
        return new LoreLibrary(locations, bosses, histories);
    }
}

public static class LoreSourceExtensions
{
    /// <summary>The realms' histories, when the source has them.</summary>
    public static IReadOnlyList<LoreHistoryText> Histories(this ILoreSource source) =>
        source is ILoreHistorySource h ? h.Histories : [];

    /// <summary>A realm's history (the neutral one too, when <paramref name="realm"/> is 0).</summary>
    public static LoreHistoryText HistoryOf(this ILoreSource source, int realm) =>
        source.Histories().FirstOrDefault(h => h.RealmId == realm);

    public static LoreEraText FindEra(this ILoreSource source, string id) =>
        source.Histories().SelectMany(h => h.Eras).FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static LoreFactionText FindFaction(this ILoreSource source, string id) =>
        source.Histories().SelectMany(h => h.Factions).FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The name of a place or boss by id (for links), or null.</summary>
    public static string NameOf(this ILoreSource source, string id) => source.GetLocation(id)?.Name ?? source.GetBoss(id)?.Name;

    public static LoreBoss FindBossByClass(this ILoreSource source, string className) =>
        source?.AllBosses.FirstOrDefault(b => b.IsClass(className));

    /// <summary>A live NPC's boss: by its script class, then by its exact spawn name (npcNames).</summary>
    public static LoreBoss FindBossFor(this ILoreSource source, string className, string npcName) =>
        source.FindBossByClass(className) ?? source?.AllBosses.FirstOrDefault(b => b.NpcNames.Contains(npcName ?? "", StringComparer.Ordinal));

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
            Str(e, "legend"), loremaster, book, Strings(e, "bosses"), areas.ToArray())
        { RealmTakes = Takes(e), Era = Str(e, "era") ?? "", Factions = Strings(e, "factions"), Related = RelatedOf(e), Chapters = ChaptersOf(e) };
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
        // "bookPage" as one page or a list; "bookPages" (the Neutral writer's spelling) the same, after it.
        string[] PagesIn(string name) => e.TryGetProperty(name, out JsonElement bp) && bp.ValueKind == JsonValueKind.Array ? Strings(e, name)
            : Str(e, name) is { Length: > 0 } one ? [one] : [];
        string[] pages = PagesIn("bookPage").Concat(PagesIn("bookPages")).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return new LoreBoss(id, Str(e, "name") ?? id, Str(e, "catalogKey"), Str(e, "locationId"), Str(e, "realm") ?? "Neutral",
            Str(e, "story"), Str(e, "legend"), topic, pages.Length == 0 ? null : string.Join(" ", pages), chronicle)
        {
            RealmTakes = Takes(e), Era = Str(e, "era") ?? "", Factions = Strings(e, "factions"), Related = RelatedOf(e), NpcNames = Strings(e, "npcNames"),
            Chapters = ChaptersOf(e), BookPages = pages,
        };
    }

    private static LoreChapter[] ChaptersOf(JsonElement e) =>
        Objects(e, "chapters").Select(c => new LoreChapter(Str(c, "title") ?? "", Str(c, "text") ?? ""))
            .Where(c => c.Title.Length > 0 && c.Text.Length > 0).ToArray();

    private static LoreRelated[] RelatedOf(JsonElement e) =>
        e.TryGetProperty("related", out JsonElement r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "id") is { Length: > 0 })
                .Select(x => new LoreRelated(Str(x, "id"), Str(x, "relation") ?? "")).ToArray()
            : [];

    private static IEnumerable<JsonElement> Objects(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToArray()
            : [];

    /// <summary>A history file (data/lore/history/&lt;realm&gt;.json).</summary>
    public static LoreHistoryText ParseHistory(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement e = document.RootElement;
        string realm = Str(e, "realm") ?? "Neutral";
        string id = Str(e, "id") ?? realm.ToLowerInvariant();
        // An era's "events" are named events or plain facts ({text, provenance, source}); "facts" are facts.
        var eras = Objects(e, "eras").Select((x, i) => (Era: new LoreEraText(Str(x, "id") ?? "", Str(x, "name") ?? "", Str(x, "summary"), Str(x, "text"),
                Objects(x, "events").Concat(Objects(x, "facts")).Select(v => new LoreEventText(Str(v, "name") ?? "", Str(v, "text"), Strings(v, "places"), Strings(v, "bosses"))).ToArray())
            { When = Str(x, "when") ?? "", Places = Strings(x, "places"), Bosses = Strings(x, "bosses") }, Order: Int(x, "order") is var o && o != 0 ? o : 1000 + i))
            .OrderBy(p => p.Order).Select(p => p.Era).ToArray();
        var factions = Objects(e, "factions").Select(x => new LoreFactionText(Str(x, "id") ?? "", Str(x, "name") ?? "", Str(x, "summary"), Str(x, "text"),
            Strings(x, "places"), Strings(x, "bosses")) { Related = RelatedOf(x) }).ToArray();
        return new LoreHistoryText(id, realm, Str(e, "title") ?? $"A History of {realm}", Str(e, "intro") ?? Str(e, "summary"), eras, factions);
    }

    /// <summary>"realmTakes": {"albion": {"story", "legend", "loremaster", "loremasterTopic", ...}}; bot lines are the engine's.</summary>
    private static IReadOnlyDictionary<int, LoreRealmTake> Takes(JsonElement e)
    {
        var takes = new Dictionary<int, LoreRealmTake>();
        if (!e.TryGetProperty("realmTakes", out JsonElement all) || all.ValueKind != JsonValueKind.Object)
            return takes;
        foreach (JsonProperty realm in all.EnumerateObject())
        {
            int id = LoreRealm.Id(realm.Name);
            if (id == LoreRealm.Neutral || realm.Value.ValueKind != JsonValueKind.Object) continue;
            JsonElement t = realm.Value;
            takes[id] = new LoreRealmTake(Str(t, "story"), Str(t, "legend"), Loremaster(t), Topic(t));
        }
        return takes;
    }

    private static LoreLoremasterText Loremaster(JsonElement e)
    {
        if (!e.TryGetProperty("loremaster", out JsonElement lm) || lm.ValueKind != JsonValueKind.Object) return null;
        var topics = new List<KeyValuePair<string, string>>();
        if (lm.TryGetProperty("topics", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty p in t.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Name))
                    topics.Add(new(p.Name.Trim(), p.Value.GetString()));
        return new LoreLoremasterText(Str(lm, "intro"), topics);
    }

    private static LoreTopic Topic(JsonElement e) =>
        e.TryGetProperty("loremasterTopic", out JsonElement t) && t.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(Str(t, "keyword"))
            ? new LoreTopic(Str(t, "keyword").Trim(), Str(t, "text"))
            : null;

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
