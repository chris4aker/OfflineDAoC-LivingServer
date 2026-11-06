using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS.LoreChannels
{
    /// <summary>What a loremaster answers: the words, the place the talk is now about, and a book offered for sale.</summary>
    public sealed record LoremasterReply(string Text, string LocationId = null, string BookLocationId = null);

    /// <summary>
    /// A capital's loremaster's keyword dialogue (Living Server 1.2), built from the lore bible: an intro, the realm's
    /// places (its own and the neutral ones), each place's loremaster intro and topics, each boss's loremaster topic
    /// (by its keyword or its name), and the books. Keywords are matched ignoring case, spaces and punctuation, as the
    /// clients whisper them back. A topic keyword two places share answers for the place the talk is about, then the
    /// realm's own place. A [keyword] in the text that leads nowhere loses its brackets, so no link is dead.
    /// </summary>
    public sealed class LoremasterDialogue
    {
        public const string PlacesKeyword = "places", BooksKeyword = "books", BookPrefix = "book of ";

        private readonly ILoreSource _source;
        private readonly int _realm;
        private readonly Dictionary<string, LoreLocation> _places = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(string LocationId, string Text)>> _topics = new(StringComparer.Ordinal);
        private readonly List<LoreLocation> _ordered;

        public LoremasterDialogue(ILoreSource source, int realm, string speaker)
        {
            // Each realm's loremaster tells a neutral place its realm's way (realmTakes).
            _source = LoreRealmView.For(source ?? LoreLibrary.Empty, realm);
            _realm = realm;
            Speaker = speaker;
            _ordered = _source.AllLocations.Where(l => LoreRealm.Sees(realm, l.RealmId))
                .OrderBy(l => l.RealmId == LoreRealm.Neutral ? 1 : 0).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (LoreLocation location in _ordered)
            {
                _places.TryAdd(LoreSourceExtensions.Key(location.Name), location);
                _places.TryAdd(LoreSourceExtensions.Key(location.Id), location);
                foreach (var (keyword, text) in location.Loremaster?.Topics ?? [])
                    AddTopic(keyword, location.Id, text);
                foreach (LoreBoss boss in _source.BossesOf(location))
                {
                    string text = boss.LoremasterTopic?.Text ?? boss.Legend;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (boss.LoremasterTopic != null) AddTopic(boss.LoremasterTopic.Keyword, location.Id, text);
                    AddTopic(boss.Name, location.Id, text);
                }
            }
        }

        public string Speaker { get; }
        public IReadOnlyList<LoreLocation> Places => _ordered;

        private void AddTopic(string keyword, string locationId, string text)
        {
            string key = LoreSourceExtensions.Key(keyword);
            if (key.Length == 0 || string.IsNullOrWhiteSpace(text)) return;
            if (!_topics.TryGetValue(key, out var list)) _topics[key] = list = new();
            if (!list.Any(e => e.LocationId == locationId)) list.Add((locationId, text));
        }

        /// <summary>The words when a player first speaks to the loremaster.</summary>
        public LoremasterReply Greet(string playerName)
        {
            string realmName = LoreRealm.Name(_realm);
            string opening = _realm switch
            {
                LoreRealm.Albion => $"Well met, {playerName}. Every hall under every hill in Albion has a story, and most of them end badly for someone. I keep them, and I copy them out for those who mean to go.",
                LoreRealm.Midgard => $"Hail, {playerName}. The skalds sing the deeds; I keep what the songs leave out. Every hole in Midgard's mountains has a name, and every name a reason.",
                _ => $"Be welcome, {playerName}. The filidh remember the old places of Hibernia, the ones under the hills and the ones beyond the Veil. Sit, if you have the time; some of them are long in the telling.",
            };
            return new(Clean($"{opening} Ask me of the [{PlacesKeyword}] of {realmName}, or of the [{BooksKeyword}] I copy."));
        }

        /// <summary>The answer to a whispered keyword, or null when the loremaster knows nothing of it.</summary>
        public LoremasterReply Resolve(string keyword, string contextLocationId = null)
        {
            string key = LoreSourceExtensions.Key(keyword);
            if (key.Length == 0) return null;
            if (key == PlacesKeyword || key == LoreSourceExtensions.Key("places of " + LoreRealm.Name(_realm)))
                return PlacesReply();
            if (key == BooksKeyword) return BooksReply();
            string bookPrefix = LoreSourceExtensions.Key(BookPrefix);
            if (key.StartsWith(bookPrefix, StringComparison.Ordinal) && _places.TryGetValue(key[bookPrefix.Length..], out LoreLocation forBook) &&
                HasBook(forBook))
                return new(Clean($"\"{forBook.Book.Title}\", {LoreBooks.Pages(_source, forBook).Count} pages on {forBook.Name}. I will copy it out for you for {LoreBooks.PriceText}."),
                    forBook.Id, forBook.Id);
            if (_places.TryGetValue(key, out LoreLocation place))
                return LocationReply(place);
            if (_topics.TryGetValue(key, out var entries))
            {
                var pick = entries.FirstOrDefault(e => string.Equals(e.LocationId, contextLocationId, StringComparison.OrdinalIgnoreCase));
                if (pick.Text == null) pick = entries.FirstOrDefault(e => _source.GetLocation(e.LocationId)?.RealmId == _realm);
                if (pick.Text == null) pick = entries[0];
                return new(Clean(pick.Text), pick.LocationId);
            }
            return null;
        }

        /// <summary>Whether a keyword leads anywhere (in any place's context).</summary>
        public bool Knows(string keyword)
        {
            string key = LoreSourceExtensions.Key(keyword);
            string bookPrefix = LoreSourceExtensions.Key(BookPrefix);
            return key == PlacesKeyword || key == BooksKeyword || _places.ContainsKey(key) || _topics.ContainsKey(key) ||
                   key.StartsWith(bookPrefix, StringComparison.Ordinal) && _places.TryGetValue(key[bookPrefix.Length..], out var l) && HasBook(l);
        }

        public static bool HasBook(LoreLocation location) => location?.Book != null && !string.IsNullOrWhiteSpace(location.Book.Title) && location.Book.Pages.Length > 0;

        private LoremasterReply PlacesReply()
        {
            var own = _ordered.Where(l => l.RealmId == _realm).Select(l => $"[{l.Name}]").ToList();
            var neutral = _ordered.Where(l => l.RealmId == LoreRealm.Neutral).Select(l => $"[{l.Name}]").ToList();
            if (own.Count + neutral.Count == 0)
                return new("My shelves are bare today. Come back when the scribes have caught up.");
            string text = own.Count > 0 ? $"These are the places of {LoreRealm.Name(_realm)} I keep tales of: {string.Join(", ", own)}." : "";
            if (neutral.Count > 0)
                text += (text.Length > 0 ? " " : "") + $"And the places no realm holds, where all three go to bleed: {string.Join(", ", neutral)}.";
            return new(Clean(text));
        }

        private LoremasterReply BooksReply()
        {
            var books = _ordered.Where(HasBook).Select(l => $"[{BookPrefix}{l.Name}]").ToList();
            return books.Count == 0
                ? new("I have nothing copied out yet. The scribes are slow this season.")
                : new(Clean($"I copy a book for each place, {LoreBooks.PriceText} a copy: {string.Join(", ", books)}."));
        }

        private LoremasterReply LocationReply(LoreLocation place)
        {
            string intro = !string.IsNullOrWhiteSpace(place.Loremaster?.Intro) ? place.Loremaster.Intro
                : !string.IsNullOrWhiteSpace(place.Summary) ? place.Summary : $"{place.Name}. I know little of it yet.";
            var linked = Links(intro).Select(LoreSourceExtensions.Key).ToHashSet(StringComparer.Ordinal);
            var others = _source.BossesOf(place)
                .Where(b => !linked.Contains(LoreSourceExtensions.Key(b.Name)) &&
                            !(b.LoremasterTopic != null && linked.Contains(LoreSourceExtensions.Key(b.LoremasterTopic.Keyword))) &&
                            !string.IsNullOrWhiteSpace(b.LoremasterTopic?.Text ?? b.Legend))
                .Select(b => $"[{b.Name}]").ToList();
            string text = intro;
            if (others.Count > 0) text += $" Ask me too of {Join(others)}.";
            if (HasBook(place)) text += $" I have it written down, if you want the [{BookPrefix}{place.Name}].";
            return new(Clean(text), place.Id);
        }

        private static string Join(List<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

        private static readonly Regex Link = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);

        public static IEnumerable<string> Links(string text) => Link.Matches(text ?? "").Select(m => m.Groups[1].Value);

        /// <summary>The text with every [keyword] that leads nowhere unbracketed.</summary>
        public string Clean(string text) => Link.Replace(text ?? "", m => Knows(m.Groups[1].Value) ? m.Value : m.Groups[1].Value);
    }
}
