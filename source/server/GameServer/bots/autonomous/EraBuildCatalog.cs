#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OfflineDaoc.Shared;

/// <summary>
/// The era builds of patch 1.65 (Living Server 1.2, docs/ERA-BUILDS-1.2.md, WP1): each realm's class shares and each
/// class's period builds, read from <c>data/era-builds-165.json</c> (made by source/tools/make_era_builds.py from the
/// research draft and the owner's review). Pure: no game world, no database. Linked into the launcher with the same JSON
/// as an embedded resource, so the launcher's batches and the server draw from one table.
/// <para>
/// Every share and weight is an estimate (2002-2003 forum posts); the data says so per entry (<c>sourced</c>,
/// <c>weightSourced</c>) and <see cref="LoadedLine"/> counts them.
/// </para>
/// <para>
/// Owner rule 2026-11-17: bots never play the custom class (the Sluaghbinder, class 63), in any edition. The catalog
/// never holds it: no share, no builds, whatever the file says (<see cref="BotCustomClassRule"/>).
/// </para>
/// <para>
/// Validation at load: a build with a line its class doesn't train, a line outside 1-50, more points than a level 50 has
/// (<see cref="PointBudget"/>), no family or no weight is dropped and named in <see cref="Problems"/>
/// (<c>ERA_BUILDS_INVALID</c>); a class left with no build is dropped (callers fall back to today's picker for it); a file
/// that doesn't parse gives an empty catalog (<see cref="IsLoaded"/> false). Bots never stop loading over data.
/// </para>
/// </summary>
public sealed class EraBuildCatalog
{
    public const string ResourceName = "OfflineDaoc.era-builds-165.json";
    public const int SupportedSchema = 1;
    public static readonly string[] RealmNames = ["Albion", "Midgard", "Hibernia"];

    // Salts so the class, build, off-meta and jitter draws of one seed are independent of each other.
    private const ulong ClassSalt = 0x5EED_C1A5_5000_0001UL;
    private const ulong BuildSalt = 0x5EED_B111_D000_0002UL;
    private const ulong OffMetaSalt = 0x5EED_0FF0_3E7A_0003UL;
    private const ulong JitterSalt = 0x5EED_1177_E400_0004UL;

    private static readonly Lazy<EraBuildCatalog> DefaultCatalog = new(LoadEmbedded);

    private readonly Dictionary<string, EraClass> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, EraClass> _classesById = new();
    private readonly Dictionary<string, EraBuild> _builds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<EraClassShare>> _shares = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = new();

    private EraBuildCatalog() { }

    /// <summary>The embedded data file, loaded once (an empty catalog, with its problem, if it can't be read).</summary>
    public static EraBuildCatalog Default => DefaultCatalog.Value;

    public bool IsLoaded { get; private set; }
    public int Schema { get; private set; }
    public string Status { get; private set; } = "";
    public string Era { get; private set; } = "";
    /// <summary>Percent of new gamebots that take one of today's class-file builds instead of an era build (D5: 10).</summary>
    public int OffMetaPercent { get; private set; }
    /// <summary>Spec levels a bot's era build may move between its lower lines (D5: 2).</summary>
    public int JitterPoints { get; private set; }
    /// <summary>One line per dropped entry, ready for the log after <c>ERA_BUILDS_INVALID </c>.</summary>
    public IReadOnlyList<string> Problems => _problems;
    public int DroppedBuilds { get; private set; }
    public IReadOnlyCollection<EraClass> Classes => _classes.Values;
    public int BuildCount => _builds.Count;

    // ---- loading

    public static EraBuildCatalog Parse(string json)
    {
        var catalog = new EraBuildCatalog();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            catalog.Read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or
                                              KeyNotFoundException)
        {
            catalog.Clear();
            catalog._problems.Add($"class=* build=* reason=\"the file does not parse: {Clean(exception.Message)}\"");
        }
        return catalog;
    }

    public static EraBuildCatalog LoadEmbedded()
    {
        try
        {
            using Stream? stream = typeof(EraBuildCatalog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
                return Empty($"the resource {ResourceName} is missing");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException exception)
        {
            return Empty($"the resource could not be read: {Clean(exception.Message)}");
        }
    }

    private static EraBuildCatalog Empty(string why)
    {
        var catalog = new EraBuildCatalog();
        catalog._problems.Add($"class=* build=* reason=\"{why}\"");
        return catalog;
    }

    private void Clear()
    {
        _classes.Clear();
        _classesById.Clear();
        _builds.Clear();
        _shares.Clear();
        IsLoaded = false;
    }

    private void Read(JsonElement root)
    {
        Schema = root.GetProperty("schema").GetInt32();
        if (Schema != SupportedSchema)
            throw new FormatException($"schema {Schema}, this build reads {SupportedSchema}");
        Status = root.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? "" : "";
        Era = root.TryGetProperty("era", out JsonElement era) ? era.GetString() ?? "" : "";
        OffMetaPercent = Math.Clamp(root.TryGetProperty("offMetaPercent", out JsonElement off) ? off.GetInt32() : 0, 0, 100);
        JitterPoints = Math.Clamp(root.TryGetProperty("jitterPoints", out JsonElement jitter) ? jitter.GetInt32() : 0, 0, 10);

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BotCustomClassRule.CustomClassName };
        if (root.TryGetProperty("excludedClasses", out JsonElement excludedClasses))
            foreach (JsonProperty entry in excludedClasses.EnumerateObject())
                excluded.Add(entry.Name);

        foreach (JsonProperty entry in root.GetProperty("classes").EnumerateObject())
        {
            string name = entry.Name;
            JsonElement info = entry.Value;
            int classId = info.GetProperty("classId").GetInt32();
            if (excluded.Contains(name) || !BotCustomClassRule.BotsMayPlay(classId))
            {
                _problems.Add($"class={name} build=* reason=\"bots never play the custom class\"");
                continue;
            }
            var lines = new HashSet<string>(info.GetProperty("lines").EnumerateArray().Select(line => line.GetString() ?? ""),
                StringComparer.Ordinal);
            int multiplier = info.GetProperty("specMultiplier").GetInt32();
            int budget = PointBudget(multiplier);
            if (info.TryGetProperty("pointBudget50", out JsonElement statedBudget) && statedBudget.GetInt32() != budget)
                _problems.Add($"class={name} build=* reason=\"pointBudget50 {statedBudget.GetInt32()} is not {budget}, used {budget}\"");
            var builds = new List<EraBuild>();
            foreach (JsonElement buildElement in info.GetProperty("builds").EnumerateArray())
            {
                EraBuild build = ReadBuild(name, buildElement);
                string? problem = Check(build, lines, budget);
                if (problem != null || _builds.ContainsKey(build.Id))
                {
                    _problems.Add($"class={name} build={build.Id} reason=\"{problem ?? "duplicate id"}\"");
                    DroppedBuilds++;
                    continue;
                }
                builds.Add(build);
            }
            if (builds.Count == 0)
            {
                _problems.Add($"class={name} build=* reason=\"no valid build: today's picker for this class\"");
                continue;
            }
            double weights = builds.Sum(build => build.Weight);
            if (Math.Abs(weights - 1) > 0.005)
                _problems.Add($"class={name} build=* reason=\"weights sum to {weights.ToString("0.000", CultureInfo.InvariantCulture)}, drawn in proportion\"");
            var eraClass = new EraClass(name, classId, info.GetProperty("realm").GetString() ?? "", multiplier, budget,
                lines.ToArray(), builds);
            _classes[name] = eraClass;
            _classesById[classId] = eraClass;
            foreach (EraBuild build in builds)
                _builds[build.Id] = build;
        }

        foreach (JsonProperty realm in root.GetProperty("realms").EnumerateObject())
        {
            var shares = new List<EraClassShare>();
            foreach (JsonProperty entry in realm.Value.GetProperty("classShare").EnumerateObject())
            {
                JsonElement value = entry.Value;
                double share = value.ValueKind == JsonValueKind.Number ? value.GetDouble() : value.GetProperty("share").GetDouble();
                bool sourced = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("sourced", out JsonElement s) &&
                               s.GetBoolean();
                string basis = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("basis", out JsonElement b)
                    ? b.GetString() ?? "" : "";
                if (excluded.Contains(entry.Name))
                {
                    _problems.Add($"class={entry.Name} build=* reason=\"bots never play the custom class: its share is left out\"");
                    continue;
                }
                if (!_classes.TryGetValue(entry.Name, out EraClass? eraClass) ||
                    !eraClass.Realm.Equals(realm.Name, StringComparison.OrdinalIgnoreCase) || !(share > 0))
                {
                    _problems.Add($"class={entry.Name} build=* reason=\"share in {realm.Name} left out (unknown, dropped or not of the realm)\"");
                    continue;
                }
                shares.Add(new EraClassShare(eraClass.Name, eraClass.ClassId, share, sourced, basis));
            }
            double total = shares.Sum(entry => entry.Share);
            if (shares.Count > 0 && Math.Abs(total - 1) > 0.005)
                _problems.Add($"class=* build=* reason=\"{realm.Name} shares sum to {total.ToString("0.000", CultureInfo.InvariantCulture)}, drawn in proportion\"");
            _shares[realm.Name] = shares;
        }
        IsLoaded = _classes.Count > 0;
    }

    private static EraBuild ReadBuild(string className, JsonElement element)
    {
        static IReadOnlyList<KeyValuePair<string, int>> Specs(JsonElement specs) =>
            specs.EnumerateObject().Select(line => new KeyValuePair<string, int>(line.Name, line.Value.GetInt32()))
                .OrderByDescending(line => line.Value).ThenBy(line => line.Key, StringComparer.Ordinal).ToArray();

        IReadOnlyList<KeyValuePair<string, int>> specs = Specs(element.GetProperty("specs"));
        return new EraBuild(
            element.GetProperty("id").GetString() ?? "",
            className,
            element.TryGetProperty("label", out JsonElement label) ? label.GetString() ?? "" : "",
            element.TryGetProperty("role", out JsonElement role) ? role.GetString() ?? "" : "",
            element.TryGetProperty("use", out JsonElement use) ? use.GetString() ?? "" : "",
            element.TryGetProperty("family", out JsonElement family) ? family.GetString() ?? "" : "",
            element.TryGetProperty("weight", out JsonElement weight) ? weight.GetDouble() : 0,
            element.TryGetProperty("weightSourced", out JsonElement weightSourced) && weightSourced.GetBoolean(),
            specs,
            element.TryGetProperty("posted", out JsonElement posted) ? Specs(posted) : specs,
            element.TryGetProperty("change", out JsonElement change) && change.ValueKind == JsonValueKind.String
                ? change.GetString() : null,
            element.TryGetProperty("specsSourced", out JsonElement specsSourced) && specsSourced.GetBoolean(),
            element.TryGetProperty("forumMentions", out JsonElement mentions) ? mentions.GetInt32() : 0);
    }

    private static string? Check(EraBuild build, HashSet<string> lines, int budget)
    {
        if (string.IsNullOrWhiteSpace(build.Id))
            return "no id";
        if (string.IsNullOrWhiteSpace(build.Family))
            return "no family";
        if (!(build.Weight > 0))
            return "no weight";
        if (build.Specs.Count == 0)
            return "no spec lines";
        foreach ((string line, int level) in build.Specs)
        {
            if (!lines.Contains(line))
                return $"{line} is not a {build.ClassName} line";
            if (level is < 1 or > 50)
                return $"{line} {level} is outside 1-50";
        }
        int points = BuildCost(build.Specs);
        return points > budget ? $"{points} points, a level 50 has {budget}" : null;
    }

    private static string Clean(string text) => text.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');

    // ---- the point budget (GameBot.GetSpecPointsForLevel with the half levels above 40, as arrival training spends)

    /// <summary>Spec points a character with this multiplier has spent by <paramref name="level"/> (levels 2 to level).</summary>
    public static int PointBudget(int specMultiplier, int level = 50)
    {
        int points = 0;
        for (int l = 2; l <= level; l++)
        {
            if (l > 40)
                points += specMultiplier * (l - 1) / 20;
            points += l > 5 ? specMultiplier * l / 10 : l;
        }
        return points;
    }

    /// <summary>Points to raise a line from 1 to <paramref name="level"/> (2 + 3 + ... + level).</summary>
    public static int LineCost(int level) => level > 1 ? level * (level + 1) / 2 - 1 : 0;

    public static int BuildCost(IEnumerable<KeyValuePair<string, int>> specs) => specs.Sum(line => LineCost(line.Value));

    // ---- lookups

    public EraClass? GetClass(string className) =>
        className != null && _classes.TryGetValue(className, out EraClass? eraClass) ? eraClass : null;

    public EraClass? GetClass(int classId) => _classesById.TryGetValue(classId, out EraClass? eraClass) ? eraClass : null;

    public EraBuild? GetBuild(string buildId) =>
        buildId != null && _builds.TryGetValue(buildId, out EraBuild? build) ? build : null;

    /// <summary>The realm's class shares (Albion, Midgard, Hibernia; empty for anything else).</summary>
    public IReadOnlyList<EraClassShare> Shares(string realm) =>
        realm != null && _shares.TryGetValue(realm, out IReadOnlyList<EraClassShare>? shares) ? shares : Array.Empty<EraClassShare>();

    /// <summary>The realm's class shares by the launcher's realm number (1 Albion, 2 Midgard, 3 Hibernia).</summary>
    public IReadOnlyList<EraClassShare> Shares(int realm) => Shares(RealmName(realm));

    public static string RealmName(int realm) => realm is >= 1 and <= 3 ? RealmNames[realm - 1] : "";

    // ---- draws

    /// <summary>A class for a new gamebot, by the realm's era share; null when the realm has no share table (the caller
    /// then draws evenly, as before). Never the custom class.</summary>
    public EraClassShare? DrawClass(string realm, Random random) =>
        PickShare(Shares(realm), (random ?? Random.Shared).NextDouble());

    public EraClassShare? DrawClass(int realm, Random random) => DrawClass(RealmName(realm), random);

    /// <summary>The same draw from a seed: one seed always gives the same class.</summary>
    public EraClassShare? DrawClass(string realm, ulong seed) => PickShare(Shares(realm), Unit(seed, ClassSalt));

    private static EraClassShare? PickShare(IReadOnlyList<EraClassShare> shares, double roll)
    {
        if (shares.Count == 0)
            return null;
        double total = shares.Sum(entry => entry.Share);
        double target = roll * total;
        foreach (EraClassShare entry in shares)
        {
            target -= entry.Share;
            if (target < 0)
                return entry;
        }
        return shares[^1];
    }

    /// <summary>One of the class's era builds by weight, from a seed (a gamebot's id: a crash before the first save draws
    /// the same build again); null for a class without era builds (the custom class, a dropped class).</summary>
    public EraBuild? DrawBuild(string className, ulong seed)
    {
        EraClass? eraClass = GetClass(className);
        if (eraClass == null)
            return null;
        double target = Unit(seed, BuildSalt) * eraClass.Builds.Sum(build => build.Weight);
        foreach (EraBuild build in eraClass.Builds)
        {
            target -= build.Weight;
            if (target < 0)
                return build;
        }
        return eraClass.Builds[^1];
    }

    /// <summary>True for the share of seeds (<paramref name="percent"/>, default the file's 10) that take one of today's
    /// class-file builds instead of an era build (D5, the odd spec).</summary>
    public bool IsOffMeta(ulong seed, int? percent = null)
    {
        int p = Math.Clamp(percent ?? OffMetaPercent, 0, 100);
        return p > 0 && Unit(seed, OffMetaSalt) * 100 < p;
    }

    /// <summary>
    /// The build's specs with up to <see cref="JitterPoints"/> levels moved from one of its lower lines to another (never
    /// the top line), from a seed; inside 1-50 and the class's level-50 budget. <paramref name="moves"/> says what moved
    /// ("Body Magic+1,Matter Magic-1"; empty when nothing did). A build with fewer than two lower lines never moves.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> Jitter(EraBuild build, ulong seed, out string moves, int? points = null)
    {
        moves = "";
        if (build == null)
            return Array.Empty<KeyValuePair<string, int>>();
        int limit = Math.Clamp(points ?? JitterPoints, 0, 10);
        EraClass? eraClass = GetClass(build.ClassName);
        int budget = eraClass?.PointBudget50 ?? int.MaxValue;
        if (limit == 0 || build.Specs.Count < 3)
            return build.Specs;

        ulong state = Mix(seed ^ JitterSalt ^ StableHash(build.Id));
        int amount = (int)(Next(ref state) % (ulong)(limit + 1));
        if (amount == 0)
            return build.Specs;
        int lower = build.Specs.Count - 1;   // lines 1..Count-1 (0 is the top line)
        int from = 1 + (int)(Next(ref state) % (ulong)lower);
        int to = 1 + (int)(Next(ref state) % (ulong)(lower - 1));
        if (to >= from)
            to++;
        int top = build.Specs[0].Value;
        for (int k = amount; k > 0; k--)
        {
            int fromLevel = build.Specs[from].Value - k, toLevel = build.Specs[to].Value + k;
            if (fromLevel < 1 || toLevel > 50 || toLevel > top)
                continue;
            var result = build.Specs.ToArray();
            result[from] = new KeyValuePair<string, int>(result[from].Key, fromLevel);
            result[to] = new KeyValuePair<string, int>(result[to].Key, toLevel);
            if (BuildCost(result) > budget)
                continue;
            moves = $"{result[to].Key}+{k},{result[from].Key}-{k}";
            // The top line stays first, even when a lower line now ties it (DerivedRatio gives the first line 1.0).
            return result.Take(1).Concat(result.Skip(1).OrderByDescending(line => line.Value)
                .ThenBy(line => line.Key, StringComparer.Ordinal)).ToArray();
        }
        return build.Specs;
    }

    /// <summary>
    /// For a bot that trained lines before it had a lifetime plan: the era build whose two highest lines are the bot's two
    /// highest trained lines (in either order; the heavier build on a tie), or null (the caller keeps today's picker).
    /// Lines at 1 don't count.
    /// </summary>
    public EraBuild? NearestBuild(string className, IEnumerable<KeyValuePair<string, int>> trained)
    {
        EraClass? eraClass = GetClass(className);
        if (eraClass == null || trained == null)
            return null;
        string[] topTrained = trained.Where(line => line.Value > 1)
            .OrderByDescending(line => line.Value).ThenBy(line => line.Key, StringComparer.Ordinal)
            .Take(2).Select(line => line.Key).ToArray();
        if (topTrained.Length < 2)
            return null;
        return eraClass.Builds
            .Where(build => build.Specs.Count >= 2 && build.Specs.Take(2).Select(line => line.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .SequenceEqual(topTrained.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal))
            .OrderByDescending(build => build.Weight)
            .FirstOrDefault();
    }

    /// <summary>
    /// How fast a line rises while levelling (BotSpecLine's ratio; 0 = trained only at 50), the shape of today's class
    /// files: the top line 1.0; lines of 35 or more 0.6 to 0.8 by cap; 11-34 at cap/50 x 0.6; 10 or less 0.
    /// </summary>
    public static float DerivedRatio(EraBuild build, string line)
    {
        if (build == null || build.Specs.Count == 0)
            return 0f;
        if (string.Equals(build.Specs[0].Key, line, StringComparison.Ordinal))
            return 1f;
        int cap = build.Specs.FirstOrDefault(entry => string.Equals(entry.Key, line, StringComparison.Ordinal)).Value;
        if (cap >= 35)
            return 0.6f + 0.2f * (Math.Min(cap, 50) - 35) / 15f;
        if (cap >= 11)
            return cap / 50f * 0.6f;
        return 0f;
    }

    // ---- the log

    /// <summary>The start-up line after <c>ERA_BUILDS_LOADED </c>.</summary>
    public string LoadedLine()
    {
        int builds = _builds.Count;
        int weightsSourced = _builds.Values.Count(build => build.WeightSourced);
        int realms = _shares.Count(entry => entry.Value.Count > 0);
        int sharesSourced = _shares.Count(entry => entry.Value.Count > 0 && entry.Value.All(share => share.Sourced));
        return $"version={Schema} status=\"{Status}\" classes={_classes.Count} builds={builds} dropped={DroppedBuilds} " +
               $"sharesSourced={sharesSourced}/{realms} weightsSourced={weightsSourced}/{builds} offMeta={OffMetaPercent}% " +
               $"jitter={JitterPoints} customClass=never loaded={(IsLoaded ? "yes" : "no")}";
    }

    // ---- deterministic randomness (SplitMix64; the same on every platform and both programs)

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static ulong Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
        }
    }

    private static double Unit(ulong seed, ulong salt) => (Mix(seed ^ salt) >> 11) * (1.0 / (1UL << 53));

    private static ulong StableHash(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
                hash = (hash ^ c) * 1099511628211UL;
            return hash;
        }
    }
}

/// <summary>A class's share of its realm's new gamebots (an estimate unless <see cref="Sourced"/>).</summary>
public sealed record EraClassShare(string ClassName, int ClassId, double Share, bool Sourced, string Basis);

/// <summary>A class and its era builds.</summary>
public sealed record EraClass(string Name, int ClassId, string Realm, int SpecMultiplier, int PointBudget50,
    IReadOnlyList<string> Lines, IReadOnlyList<EraBuild> Builds);

/// <summary>
/// One period build. <see cref="Specs"/> is what bots train, highest line first; <see cref="Posted"/> what the posts said;
/// <see cref="Change"/> why they differ (a trim or fill, never sourced). <see cref="Family"/> is an eSpecType name (the
/// class file's weapon and gear rules); <see cref="Role"/> the research's words (tank, healer-cc, cc-mezzer, pbae...).
/// </summary>
public sealed record EraBuild(string Id, string ClassName, string Label, string Role, string Use, string Family,
    double Weight, bool WeightSourced, IReadOnlyList<KeyValuePair<string, int>> Specs,
    IReadOnlyList<KeyValuePair<string, int>> Posted, string? Change, bool SpecsSourced, int ForumMentions)
{
    public int Points => EraBuildCatalog.BuildCost(Specs);

    public int Level(string line) =>
        Specs.FirstOrDefault(entry => string.Equals(entry.Key, line, StringComparison.Ordinal)).Value;

    /// <summary>"Mind Magic:44,Body Magic:31" (the log's form).</summary>
    public static string Format(IEnumerable<KeyValuePair<string, int>> specs) =>
        string.Join(",", specs.Select(line => $"{line.Key}:{line.Value}"));
}

/// <summary>
/// Owner rule 2026-11-17: "the custom class, that should never be played by bots". No gamebot, companion, helper, hired
/// companion, Epic Special Force or buffbot is ever made as the Sluaghbinder (class 63), in any edition; a gamebot that
/// already is one is kept (character and items) but never logged in again. Players keep the class in the 0.37b edition.
/// Shared by the server and the launcher.
/// </summary>
public static class BotCustomClassRule
{
    public const int CustomClassId = 63;
    public const string CustomClassName = "Sluaghbinder";
    /// <summary>The start-up log tag, one line per kept custom-class gamebot.</summary>
    public const string RetiredLogTag = "BOT_CUSTOM_CLASS_RETIRED";
    /// <summary>What the launcher's bot list shows in the Activity column for such a bot.</summary>
    public const string RetiredActivity = "Not played: bots don't play the Sluaghbinder (kept, not deleted)";
    public const string RefusedMessage = "Bots don't play the Sluaghbinder; it stays a class for players.";

    public static bool BotsMayPlay(int classId) => classId != CustomClassId;
}
