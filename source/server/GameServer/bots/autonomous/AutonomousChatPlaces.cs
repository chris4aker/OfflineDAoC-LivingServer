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
/// The places bots name in chat (data/chat/places.json): every zone, town and dungeon a line may mention, with the zones that
/// count as being there. The talking update: a corpus line that claims the speaker is somewhere needs "at:&lt;place&gt;",
/// "going:&lt;place&gt;" or "near:&lt;place&gt;", and these flags come from here (live 2026-10: "heading into myrkwood" was said
/// from anywhere in Midgard, and Darkness Falls lines from inside Tomb of Mithra). Pure apart from loading the resource.
/// </summary>
public static class AutonomousChatPlaces
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>A place; <paramref name="Levels"/> is its monsters' usual level range (20th-80th percentile, from the world data
    /// by tools/bot-voices/place_levels.py) or a battleground's bracket, or null.</summary>
    public sealed record Place(string Key, string Realm, string Type, string[] Aliases, string[] Zones, int[] Levels = null);

    private static readonly Lazy<Place[]> Loaded = new(Load);

    public static Place[] All => Loaded.Value;

    private static Place[] Load()
    {
        try
        {
            Assembly assembly = typeof(AutonomousChatPlaces).Assembly;
            string name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("chat.places.json", StringComparison.Ordinal));
            if (name == null)
                throw new FileNotFoundException("embedded resource *chat.places.json is missing");
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception exception)
        {
            Log.Error("Bot voices: could not load places.json; lines that name places stay silent", exception);
            return [];
        }
    }

    public static Place[] Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        static string[] Strings(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToArray()
            : [];
        static int[] Levels(JsonElement e) => e.TryGetProperty("levels", out JsonElement v) && v.ValueKind == JsonValueKind.Array &&
                                              v.GetArrayLength() == 2
            ? v.EnumerateArray().Select(x => x.GetInt32()).ToArray()
            : null;
        return document.RootElement.GetProperty("places").EnumerateArray().Select(e => new Place(
            e.GetProperty("key").GetString(), e.TryGetProperty("realm", out JsonElement realm) ? realm.GetString() : "*",
            e.TryGetProperty("type", out JsonElement type) ? type.GetString() : "zone", Strings(e, "aliases"), Strings(e, "zones"),
            Levels(e))).ToArray();
    }

    /// <summary>The places a bot in this zone (or region, for capitals and dungeons) is at: "Vale of Mularn" is the vale and
    /// the towns in it (Mularn, Haggerfel, Fort Veldon).</summary>
    public static IEnumerable<string> KeysAt(string zone, string region = null, IEnumerable<Place> places = null)
    {
        if (string.IsNullOrWhiteSpace(zone) && string.IsNullOrWhiteSpace(region))
            yield break;
        foreach (Place place in places ?? All)
            if (place.Zones.Any(z => string.Equals(z, zone, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(z, region, StringComparison.OrdinalIgnoreCase)))
                yield return place.Key;
    }

    /// <summary>
    /// The places a travel destination is ("Myrkwood Forest", "Spindelhalla", "keep Nottmoor", "Mularn"): a place whose key,
    /// alias or zone is the destination, or whose alias is a whole word of it. A town's zone is not where it is going.
    /// </summary>
    public static IEnumerable<string> KeysFor(string destination, IEnumerable<Place> places = null)
    {
        if (string.IsNullOrWhiteSpace(destination))
            yield break;
        string text = destination.Trim().ToLowerInvariant();
        foreach (Place place in places ?? All)
        {
            bool exact = string.Equals(place.Key, text, StringComparison.OrdinalIgnoreCase) ||
                         place.Aliases.Any(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase)) ||
                         place.Type != "town" && place.Zones.Any(z => string.Equals(z, destination.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact || place.Aliases.Any(a => a.Length >= 5 && WordPattern(a).IsMatch(text)))
                yield return place.Key;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>A whole-word pattern for an alias, built once (they are matched each time a speaker is built).</summary>
    private static Regex WordPattern(string alias) =>
        Patterns.GetOrAdd(alias, a => new Regex($@"(?<![a-z']){Regex.Escape(a)}(?![a-z])", RegexOptions.Compiled));

    /// <summary>The "at:" and "going:" flags for a speaker.</summary>
    public static IEnumerable<string> Flags(string zone, string region, string destination, IEnumerable<Place> places = null)
    {
        Place[] list = (places ?? All).ToArray();
        foreach (string key in KeysAt(zone, region, list))
            yield return "at:" + key;
        foreach (string key in KeysFor(destination, list))
            yield return "going:" + key;
    }
}
