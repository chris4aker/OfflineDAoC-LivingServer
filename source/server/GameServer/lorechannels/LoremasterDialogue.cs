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
        public const string PlacesKeyword = "places", BooksKeyword = "books", BookPrefix = "book of ", HistoryKeyword = "history";

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
                // A long story: the summary as "the tale of X", each chapter a topic of its own (sent in chat-sized pieces).
                AddChapters(location.Name, location.Story, location.Chapters, location.Id);
                foreach (LoreBoss boss in _source.BossesOf(location))
                {
                    string text = boss.LoremasterTopic?.Text ?? boss.Legend;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    text += RelatedSentence(boss.Related);
                    text += ChapterSentence(boss.Name, boss.Chapters);
                    if (boss.LoremasterTopic != null) AddTopic(boss.LoremasterTopic.Keyword, location.Id, text);
                    AddTopic(boss.Name, location.Id, text);
                    AddChapters(boss.Name, boss.Story, boss.Chapters, location.Id);
                }
            }

            // The realm's history, and the neutral one: each era and each power is a topic, and "history" lists them.
            _histories = new[] { _source.HistoryOf(realm), realm == LoreRealm.Neutral ? null : _source.HistoryOf(LoreRealm.Neutral) }
                .Where(h => h != null).ToList();
            foreach (LoreHistoryText history in _histories)
            {
                foreach (LoreEraText era in history.Eras)
                {
                    string text = EraText(era);
                    AddTopic(era.Name, null, text);
                    AddTopic(era.Id, null, text);
                }
                foreach (LoreFactionText faction in history.Factions)
                {
                    string text = FactionText(faction);
                    AddTopic(faction.Name, null, text);
                    AddTopic(faction.Id, null, text);
                }
            }
        }

        private readonly List<LoreHistoryText> _histories;

        public const string TalePrefix = "the tale of ";

        /// <summary>" The whole tale of Krondon is long: ask of [The Making], [The First Raid]..." for a story told in chapters.</summary>
        private static string ChapterSentence(string name, LoreChapter[] chapters) =>
            chapters is { Length: > 0 }
                ? $" [{TalePrefix}{name}] is long in the telling; ask of it a part at a time: {Join(chapters.Select(c => $"[{c.Title}]").ToList())}."
                : "";

        private void AddChapters(string name, string summary, LoreChapter[] chapters, string locationId)
        {
            if (chapters is not { Length: > 0 }) return;
            AddTopic(TalePrefix + name, locationId, $"{(summary ?? "").Trim()} It is long in the telling; ask of it a part at a time: " +
                Join(chapters.Select(c => $"[{c.Title}]").ToList()) + ".");
            for (int i = 0; i < chapters.Length; i++)
            {
                string next = i + 1 < chapters.Length ? $" Then ask of [{chapters[i + 1].Title}]." : "";
                AddTopic(chapters[i].Title, locationId, chapters[i].Text + next);
            }
        }

        private string LinkTo(string id) => _source.NameOf(id) is { } name ? $"[{name}]" : null;

        private string LinksTo(IEnumerable<string> ids) => Join(ids.Select(LinkTo).Where(l => l != null).Distinct().ToList());

        private string EraText(LoreEraText era)
        {
            string text = !string.IsNullOrWhiteSpace(era.Text) ? era.Text : era.Summary ?? "";
            if (!string.IsNullOrWhiteSpace(era.When)) text = $"{era.When}. {text}";
            foreach (LoreEventText e in era.Events)
            {
                text += string.IsNullOrWhiteSpace(e.Name) ? $" {e.Text}" : $" {e.Name}: {e.Text}";
                string places = LinksTo(e.Places.Concat(e.Bosses));
                if (places.Length > 0) text += $" (Ask of {places}.)";
            }
            string own = LinksTo(era.Places.Concat(era.Bosses));
            if (own.Length > 0) text += $" Of that age: {own}.";
            return text;
        }

        private string FactionText(LoreFactionText faction)
        {
            string text = !string.IsNullOrWhiteSpace(faction.Text) ? faction.Text : faction.Summary ?? "";
            string places = LinksTo(faction.Places);
            string bosses = LinksTo(faction.Bosses);
            if (places.Length > 0) text += $" Its places: {places}.";
            if (bosses.Length > 0) text += $" Its own: {bosses}.";
            return text;
        }

        /// <summary>" It is bound up with [Behemoth] (its keeper)." for the places and bosses a lore file says it knows.</summary>
        private string RelatedSentence(IEnumerable<LoreRelated> related)
        {
            var parts = (related ?? []).Select(r => LinkTo(r.Id) is { } link ? (string.IsNullOrWhiteSpace(r.Relation) ? link : $"{link} ({r.Relation})") : null)
                .Where(p => p != null).ToList();
            return parts.Count == 0 ? "" : $" Ask me too of {Join(parts)}.";
        }

        /// <summary>" Its age is [the War of the Isles]; behind it stand [Morgana's host]." from a place's era and factions.</summary>
        private string HistorySentence(string eraId, IEnumerable<string> factionIds)
        {
            string text = "";
            if (_source.FindEra(eraId) is { } era && !string.IsNullOrWhiteSpace(era.Name)) text += $" Its tale belongs to [{era.Name}].";
            var factions = (factionIds ?? []).Select(id => _source.FindFaction(id)?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => $"[{n}]").ToList();
            if (factions.Count > 0) text += $" Behind it stand {Join(factions)}.";
            return text;
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
            if (key == HistoryKeyword && _histories.Count > 0) return HistoryReply();
            string bookPrefix = LoreSourceExtensions.Key(BookPrefix);
            if (key.StartsWith(bookPrefix, StringComparison.Ordinal) && _places.TryGetValue(key[bookPrefix.Length..], out LoreLocation forBook) &&
                HasBook(forBook))
            {
                int pages = LoreBooks.Pages(_source, forBook).Count, volumes = LoreBooks.Volumes(_source, forBook).Count;
                string bound = volumes > 1 ? $", too many for one binding: it comes in {volumes} volumes" : "";
                return new(Clean($"\"{forBook.Book.Title}\", {pages} pages on {forBook.Name}{bound}. I will copy it out for you for {LoreBooks.PriceFor(volumes)}."),
                    forBook.Id, forBook.Id);
            }
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
            return key == PlacesKeyword || key == BooksKeyword || key == HistoryKeyword && _histories.Count > 0 || _places.ContainsKey(key) || _topics.ContainsKey(key) ||
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

        private LoremasterReply HistoryReply()
        {
            var parts = new List<string>();
            foreach (LoreHistoryText history in _histories)
            {
                string eras = Join(history.Eras.Where(e => !string.IsNullOrWhiteSpace(e.Name)).Select(e => $"[{e.Name}]").ToList());
                string powers = Join(history.Factions.Where(f => !string.IsNullOrWhiteSpace(f.Name)).Select(f => $"[{f.Name}]").ToList());
                string text = !string.IsNullOrWhiteSpace(history.Intro) ? history.Intro : history.Title;
                if (eras.Length > 0) text += $" The ages, oldest first: {eras}.";
                if (powers.Length > 0) text += $" The powers behind the dark places: {powers}.";
                parts.Add(text);
            }
            return new(Clean(string.Join(" ", parts)));
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
            string text = intro + HistorySentence(place.Era, place.Factions) + RelatedSentence(place.Related) + ChapterSentence(place.Name, place.Chapters);
            if (others.Count > 0) text += $" Ask me too of {Join(others)}.";
            if (HasBook(place)) text += $" I have it written down, if you want the [{BookPrefix}{place.Name}].";
            return new(Clean(text), place.Id);
        }

        private static string Join(List<string> items) =>
            items.Count == 0 ? "" : items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

        private static readonly Regex Link = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);

        public static IEnumerable<string> Links(string text) => Link.Matches(text ?? "").Select(m => m.Groups[1].Value);

        /// <summary>The text with every [keyword] that leads nowhere unbracketed.</summary>
        public string Clean(string text) => Link.Replace(text ?? "", m => Knows(m.Groups[1].Value) ? m.Value : m.Groups[1].Value);
    }
}
