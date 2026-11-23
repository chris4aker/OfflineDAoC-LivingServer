using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Chat audit 1.2.2 (C11, fix F10): the proper names of the world as a tidy writer types them ("Camelot", "Tir na Nog", "Bog of
/// Cullen", "Legion"), so a writing style that capitalizes its lines capitalizes the names in them too, and a line no longer
/// reads "The armsman trainer in camelot". The names are the realms, the chat catalog's places (data/chat/places.json) and the
/// lore bible's locations and bosses; a boss whose name is only common words ("Host", "Crypt Lord", "Glacier Giant") is left
/// out, as the same words name ordinary things in chat. tools/bot-voices/voice_style.py ports these rules (proper_names,
/// capitalize_names); keep the two the same. Pure apart from loading the data.
/// </summary>
public static class AutonomousChatNames
{
    /// <summary>Words a title keeps in lower case after its first word ("Bog of Cullen", "Tir na Nog").</summary>
    public static readonly HashSet<string> Small = new(StringComparer.Ordinal) { "of", "the", "na", "an", "a", "and", "in", "on", "at" };

    /// <summary>One-word place aliases that are ordinary words too ("the barrows", "the hills").</summary>
    public static readonly HashSet<string> PlaceStop = new(StringComparer.Ordinal)
    {
        "barrows", "coruscating", "hills", "plains", "marsh", "wall", "hall", "keep", "mine", "mines", "tomb", "cave", "caves", "forest",
        "lair", "den", "gate", "city",
    };

    /// <summary>Common words in boss names: a boss named only with these is not capitalized ("Host", "Crypt Lord").</summary>
    public static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "air", "alchemist", "ambassador", "ancient", "annihilator", "apprentice", "archivist", "archwizard", "bane", "bartender", "black",
        "blight", "blue", "bound", "brave", "broodmother", "burr", "cabalist", "cackler", "captain", "chamberlain", "champion", "chancellor", "chief",
        "chieftain", "chosen", "clan", "cluster", "conqueror", "conservator", "cook", "corrupt", "council", "courier", "crafter", "crypt",
        "dagger", "den", "destroyer", "director", "dreaded", "duke", "earl", "earth", "elder", "emperor", "empowered", "energy",
        "enlightened", "eye", "eyed", "eyes", "fairy", "faerie", "fanged", "fester", "field", "fire", "flame", "forgotten", "frat",
        "gatekeeper", "geomancer", "giant", "glacier", "glimmer", "gnat", "grand", "grant", "green", "grim", "gruff", "grunge", "guard",
        "guardian", "hand", "heavy", "herald", "high", "hills", "hope", "host", "household", "i", "inquisitors", "isolationist", "fate's", "king's", "marshal's", "mutilators", "icelord", "jailer", "king", "knight",
        "knights", "lad", "lady", "lair", "lairmaster", "leader", "lecherous", "legendary", "legion", "lich", "lieutenant",
        "lieutenants", "lord", "lords", "magister", "mark", "marquis", "marsh", "marshal", "masher", "master", "matriarch", "matron",
        "maw", "mechanism", "melancholic", "metal", "mighty", "mistress", "monger", "mouth", "necromantic", "oak", "old", "organic",
        "overman", "overseer", "pain", "pathfinder", "patrol", "pet", "priestess", "primal", "prince", "princess", "protector",
        "prototype", "provisioner", "purity", "queen", "quiet", "ranger", "reckoner", "red", "revenant", "rob", "rose", "runes",
        "ruthless", "sacristan", "saffron", "scratch", "seer", "sergeant", "shade", "shaman", "silencer", "silver", "sister", "skeletal",
        "smasher", "soul", "spectral", "spirit", "spiritmaster", "sporite", "stripe", "summoned", "summoner", "supply", "table", "thane",
        "throttler", "tine", "touched", "tower", "traitor", "treefall", "tusker", "twirl", "undead", "underviewer", "ursine", "viewer",
        "warlord", "water", "weaponmaster", "webweaver", "wight", "winter", "wizard", "wraith", "wrath", "wretch",
    };

    /// <summary>Bosses capitalized although their name is a common word.</summary>
    public static readonly HashSet<string> BossAllow = new(StringComparer.Ordinal) { "legion", "green knight" };

    private static readonly Regex NameShape = new(@"^[A-Za-z' ]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new(@"[A-Za-z][A-Za-z']*", RegexOptions.CultureInvariant);

    /// <summary>A phrase in title case, small words lower after the first ("bog of cullen" -> "Bog of Cullen"). Pure.</summary>
    public static string Title(string phrase)
    {
        string[] words = (phrase ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            if (i == 0 || !Small.Contains(words[i]))
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        return string.Join(' ', words);
    }

    private static string StripThe(string phrase) =>
        phrase.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? phrase[4..] : phrase;

    /// <summary>
    /// The name table (pure): lower-case phrase to the name as a tidy writer types it. The first source to name a phrase wins:
    /// the realms, the lore bible's locations, the catalog's places and their aliases, then the bosses and their aliases.
    /// </summary>
    public static Dictionary<string, string> Build(IEnumerable<AutonomousChatPlaces.Place> places, LoreBible bible)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string cased)
        {
            cased = Regex.Replace(StripThe((cased ?? string.Empty).Trim()), @"\s+", " ");
            if (cased.Length < 4 || !NameShape.IsMatch(cased))
                return;
            names.TryAdd(cased.ToLowerInvariant(), cased);
        }
        foreach (string realm in new[] { "Albion", "Midgard", "Hibernia", "Avalon" })
            Add(realm);
        foreach (LoreBible.Location location in bible?.Locations ?? [])
            Add(location.Name);
        foreach (AutonomousChatPlaces.Place place in places ?? [])
            foreach (string phrase in new[] { place.Key }.Concat(place.Aliases ?? []))
            {
                string plain = StripThe((phrase ?? string.Empty).Trim().ToLowerInvariant());
                if (!plain.Contains(' ') && PlaceStop.Contains(plain))
                    continue;
                Add(Title(plain));
            }
        foreach (LoreBible.Boss boss in bible?.Bosses ?? [])
            foreach (string phrase in new[] { boss.Name }.Concat((boss.Aliases ?? []).Where(alias => !alias.StartsWith("the ", StringComparison.OrdinalIgnoreCase))))
            {
                string plain = StripThe((phrase ?? string.Empty).Trim());
                string lower = plain.ToLowerInvariant();
                bool proper = BossAllow.Contains(lower) || lower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => !Small.Contains(word) && !CommonWords.Contains(word));
                if (proper)
                    Add(plain.Any(char.IsUpper) ? plain : Title(plain));
            }
        return names;
    }

    private static readonly Lazy<(Dictionary<string, string> Names, int Longest)> Loaded = new(() =>
    {
        Dictionary<string, string> names;
        try
        {
            names = Build(AutonomousChatPlaces.All, LoreBible.Current);
        }
        catch (Exception)
        {
            names = Build(AutonomousChatPlaces.All, null);
        }
        return (names, Longest(names));
    });

    private static int Longest(Dictionary<string, string> names) =>
        names.Count == 0 ? 0 : names.Keys.Max(key => key.Count(c => c == ' ') + 1);

    /// <summary>The world's names (see <see cref="Build"/>).</summary>
    public static IReadOnlyDictionary<string, string> Names => Loaded.Value.Names;

    /// <summary>A text with the world's names in their capitals: <see cref="Capitalize(string, IReadOnlyDictionary{string, string})"/>.</summary>
    public static string Capitalize(string text) => Capitalize(text, Loaded.Value.Names, Loaded.Value.Longest);

    /// <summary>
    /// A text with every name of <paramref name="names"/> said in its capitals ("the trainer in camelot" -> "the trainer in
    /// Camelot", "bog of cullen" -> "Bog of Cullen"). Words must stand with single spaces between them; the longest name wins; a
    /// word typed with a capital keeps it ("The barrows" stays capital at the start). Pure.
    /// </summary>
    public static string Capitalize(string text, IReadOnlyDictionary<string, string> names, int longest = 0)
    {
        if (string.IsNullOrEmpty(text) || names == null || names.Count == 0)
            return text;
        if (longest <= 0)
            longest = names.Keys.Max(key => key.Count(c => c == ' ') + 1);
        MatchCollection tokens = Token.Matches(text);
        if (tokens.Count == 0)
            return text;
        var output = new StringBuilder(text.Length);
        int written = 0;
        for (int i = 0; i < tokens.Count;)
        {
            int taken = 0;
            string cased = null;
            for (int n = Math.Min(longest, tokens.Count - i); n >= 1 && cased == null; n--)
            {
                bool joined = true;
                for (int k = i; k < i + n - 1 && joined; k++)
                    joined = tokens[k + 1].Index == tokens[k].Index + tokens[k].Length + 1 && text[tokens[k].Index + tokens[k].Length] == ' ';
                if (!joined)
                    continue;
                string phrase = string.Join(' ', Enumerable.Range(i, n).Select(k => tokens[k].Value.ToLowerInvariant()));
                if (names.TryGetValue(phrase, out string found))
                {
                    cased = found;
                    taken = n;
                }
                // "camelot's gates": the name with its possessive.
                else if (phrase.EndsWith("'s", StringComparison.Ordinal) && names.TryGetValue(phrase[..^2], out found))
                {
                    cased = found + "'s";
                    taken = n;
                }
            }
            if (cased == null)
            {
                i++;
                continue;
            }
            string[] casedWords = cased.Split(' ');
            int start = tokens[i].Index, end = tokens[i + taken - 1].Index + tokens[i + taken - 1].Length;
            output.Append(text, written, start - written);
            for (int k = 0; k < taken; k++)
            {
                string typed = tokens[i + k].Value, word = casedWords[k];
                if (char.IsUpper(typed[0]))
                    word = char.ToUpperInvariant(word[0]) + word[1..];
                output.Append(k == 0 ? string.Empty : " ").Append(word);
            }
            written = end;
            i += taken;
        }
        output.Append(text, written, text.Length - written);
        return output.ToString();
    }
}
