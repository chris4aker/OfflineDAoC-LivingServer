using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.LoreChannels;

namespace DOL.GS;

/// <summary>
/// The lore engine's bible as the lore channels' <see cref="ILoreSource"/> (loremasters, books, the /lore codex, the
/// Herald and first kills; LoreChannelHub.UseSource at start-up). One loader and one validator for every channel: what
/// UT_LoreBible passes is what the loremasters tell. Pure.
/// </summary>
public sealed class LoreBibleSource : ILoreSource, ILoreHistorySource
{
    private readonly LoreLibrary _library;

    public LoreBibleSource(LoreBible bible)
    {
        bible ??= LoreBible.Empty;
        _library = new LoreLibrary(bible.Locations.Select(ToChannel), bible.Bosses.Select(ToChannel), bible.Histories.Select(ToChannel));
    }

    public IReadOnlyList<LoreHistoryText> Histories => _library.Histories;

    public static LoreHistoryText ToChannel(LoreBible.History h) =>
        new(h.Id, h.Realm, h.Title, h.Intro,
            h.Eras.Select(e => new LoreEraText(e.Id, e.Name, e.Summary, e.Text,
                    e.Events.Select(v => new LoreEventText(v.Name, v.Text, v.Places, v.Bosses))
                        .Concat(e.Facts.Select(x => new LoreEventText("", x.Text, [], []))).ToArray())
                { When = e.When ?? "", Places = e.Places, Bosses = e.Bosses }).ToArray(),
            h.Factions.Select(f => new LoreFactionText(f.Id, f.Name, f.Summary, f.Text, f.Places, f.Bosses) { Related = ToChannel(f.RelatedTo) }).ToArray());

    private static LoreRelated[] ToChannel(LoreBible.Related[] related) => related.Select(r => new LoreRelated(r.Id, r.Relation)).ToArray();

    public IReadOnlyList<LoreLocation> AllLocations => _library.AllLocations;
    public IReadOnlyList<LoreBoss> AllBosses => _library.AllBosses;
    public LoreLocation GetLocation(string id) => _library.GetLocation(id);
    public LoreBoss GetBoss(string id) => _library.GetBoss(id);

    private static LoreLoremasterText ToChannel(LoreBible.Loremaster master) =>
        master == null ? null : new LoreLoremasterText(master.Intro, master.Topics.Select(t => new KeyValuePair<string, string>(t.Key, t.Value)).ToList());

    private static LoreTopic Topic(string keyword, string text) =>
        string.IsNullOrWhiteSpace(keyword) ? null : new LoreTopic(keyword.Trim(), text);

    private static IReadOnlyDictionary<int, LoreRealmTake> Takes(IReadOnlyDictionary<string, LoreBible.RealmTake> takes) =>
        takes.Where(t => LoreRealm.Id(t.Key) != LoreRealm.Neutral).ToDictionary(t => LoreRealm.Id(t.Key),
            t => new LoreRealmTake(t.Value.Story, t.Value.Legend, ToChannel(t.Value.Loremaster), Topic(t.Value.LoremasterKeyword, t.Value.LoremasterText)));

    public static LoreLocation ToChannel(LoreBible.Location l) =>
        new(l.Id, l.Name, l.Realm, l.Regions.Select(r => (int)r).ToArray(), l.Summary, l.Story, l.Legend, ToChannel(l.Loremaster),
            l.Book == null ? null : new LoreBookText(l.Book.Title, l.Book.Pages), l.Bosses,
            l.Areas.Select(a => new LoreArea(a.Name, a.X, a.Y, a.Z, a.Radius)).ToArray())
        {
            RealmTakes = Takes(l.RealmTakes),
            Chapters = l.Chapters.Select(c => new LoreChapter(c.Title, c.Text)).ToArray(),
            Era = l.Era ?? "",
            Factions = l.Factions,
            Related = ToChannel(l.RelatedTo),
        };

    public static LoreBoss ToChannel(LoreBible.Boss b) =>
        new(b.Id, b.Name, b.CatalogKey, b.LocationId, b.Realm, b.Story, b.Legend, Topic(b.LoremasterKeyword, b.LoremasterText),
            string.IsNullOrWhiteSpace(b.BookPage) ? null : b.BookPage,
            string.IsNullOrWhiteSpace(b.ChronicleWin) && string.IsNullOrWhiteSpace(b.ChronicleLoss) ? null : new LoreChronicleText(b.ChronicleWin, b.ChronicleLoss))
        {
            RealmTakes = Takes(b.RealmTakes),
            NpcNames = b.NpcNames,
            Chapters = b.Chapters.Select(c => new LoreChapter(c.Title, c.Text)).ToArray(),
            BookPages = b.BookPages,
            Era = b.Era ?? "",
            Factions = b.Factions,
            Related = ToChannel(b.RelatedTo),
        };
}
