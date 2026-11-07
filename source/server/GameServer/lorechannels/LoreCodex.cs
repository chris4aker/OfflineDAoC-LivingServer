using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.Commands;

namespace DOL.GS.LoreChannels
{
    /// <summary>A page of the codex: the text window's caption and lines.</summary>
    public sealed record LoreCodexPage(string Caption, List<string> Lines);

    /// <summary>
    /// The lore codex (Living Server 1.2): /lore in either client opens the realm's places and bosses as text-window
    /// pages, with each boss's first-kill status for the player's realm. The new client shows the same pages in its
    /// LORE window (journal button), where every [name] is a link that asks for that page; the classic client shows them
    /// in its information window (type /lore and the name). Captions start "Lore: " so the new client knows them. Boss
    /// pages follow the bestiary's rules (LoreBestiary): legend always, story once the realm has killed it.
    /// </summary>
    public static class LoreCodex
    {
        public const string CaptionPrefix = "Lore: ";
        /// <summary>Characters per window, as /stables pages (the packet holds 2,048 bytes).</summary>
        public const int PageCharacters = 850;

        private static readonly Regex TrailingPage = new(@"^(.*?)\s+(\d{1,2})$", RegexOptions.Compiled);

        public static List<LoreCodexPage> Pages(ILoreSource source, int realm, IEnumerable<LoreFirstKill> kills, string query)
        {
            query = (query ?? "").Trim();
            source = LoreRealmView.For(source, realm);   // the reader's realm's take on neutral places (realmTakes)
            List<LoreBestiaryEntry> bestiary = LoreBestiary.For(source, realm, kills, []);
            if (query.Length == 0 || query.Equals("index", StringComparison.OrdinalIgnoreCase))
                return Paginate("Lore of " + LoreRealm.Name(realm), "index", Index(source, realm, bestiary));
            string key = LoreSourceExtensions.Key(query);
            if (key == "history" && HistoriesFor(source, realm).Any())
                return Paginate("History", "history", History(source, realm));
            foreach (LoreHistoryText history in HistoriesFor(source, realm))
            {
                if (history.Eras.FirstOrDefault(e => LoreSourceExtensions.Key(e.Name) == key || LoreSourceExtensions.Key(e.Id) == key) is { } era)
                    return Paginate(era.Name, era.Name, Era(source, era));
                if (history.Factions.FirstOrDefault(f => LoreSourceExtensions.Key(f.Name) == key || LoreSourceExtensions.Key(f.Id) == key) is { } faction)
                    return Paginate(faction.Name, faction.Name, Faction(source, faction));
            }
            LoreLocation place = source.AllLocations.FirstOrDefault(l => LoreRealm.Sees(realm, l.RealmId) &&
                (LoreSourceExtensions.Key(l.Name) == key || LoreSourceExtensions.Key(l.Id) == key));
            if (place != null)
                return Paginate(place.Name, place.Name, Location(source, place, bestiary));
            LoreBestiaryEntry entry = bestiary.FirstOrDefault(b => LoreSourceExtensions.Key(b.Boss.Name) == key || LoreSourceExtensions.Key(b.Boss.Id) == key);
            if (entry != null)
                return Paginate(entry.Boss.Name, entry.Boss.Name, BossLines(entry, realm));
            return [];
        }

        /// <summary>The page a query asks for ("krondon 2" is Krondon's second page), or null.</summary>
        public static LoreCodexPage Find(ILoreSource source, int realm, IEnumerable<LoreFirstKill> kills, string query)
        {
            query = (query ?? "").Trim();
            int page = 1;
            List<LoreCodexPage> pages = Pages(source, realm, kills, query);
            if (pages.Count == 0 && TrailingPage.Match(query) is { Success: true } m)
            {
                page = int.Parse(m.Groups[2].Value);
                pages = Pages(source, realm, kills, m.Groups[1].Value);
            }
            return pages.Count == 0 ? null : pages[Math.Clamp(page, 1, pages.Count) - 1];
        }

        private static List<string> Index(ILoreSource source, int realm, List<LoreBestiaryEntry> bestiary)
        {
            var lines = new List<string>();
            foreach (var group in source.AllLocations.Where(l => LoreRealm.Sees(realm, l.RealmId))
                         .GroupBy(l => l.RealmId == LoreRealm.Neutral).OrderBy(g => g.Key))
            {
                lines.Add(group.Key ? "The places no realm holds:" : $"The places of {LoreRealm.Name(realm)}:");
                foreach (LoreLocation place in group.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var bosses = bestiary.Where(b => b.Location.Id == place.Id).ToList();
                    lines.Add($"[{place.Name}]" + (bosses.Count == 0 ? "" : $" - {bosses.Count(b => b.Unlocked)} of {bosses.Count} slain"));
                }
                lines.Add(" ");
            }
            if (lines.Count == 0) lines.Add("No lore has been written down yet.");
            else
            {
                if (HistoriesFor(source, realm).Any()) lines.Add("[history] of the realms");
                lines.Add("Choose a place, or type /lore and its name.");
            }
            return lines;
        }

        /// <summary>The reader's realm's history and the neutral one.</summary>
        private static IEnumerable<LoreHistoryText> HistoriesFor(ILoreSource source, int realm) =>
            source.Histories().Where(h => h.RealmId == realm || h.RealmId == LoreRealm.Neutral);

        private static List<string> History(ILoreSource source, int realm)
        {
            var lines = new List<string>();
            foreach (LoreHistoryText history in HistoriesFor(source, realm))
            {
                lines.Add(history.Title);
                if (!string.IsNullOrWhiteSpace(history.Intro)) lines.AddRange(LoreBooks.Wrap(history.Intro, 200));
                lines.Add("The ages, oldest first:");
                foreach (LoreEraText era in history.Eras) lines.Add($"[{era.Name}]" + (string.IsNullOrWhiteSpace(era.Summary) ? "" : " - " + era.Summary));
                if (history.Factions.Length > 0)
                {
                    lines.Add("The powers:");
                    foreach (LoreFactionText faction in history.Factions) lines.Add($"[{faction.Name}]" + (string.IsNullOrWhiteSpace(faction.Summary) ? "" : " - " + faction.Summary));
                }
                lines.Add(" ");
            }
            lines.Add("[index]");
            return lines;
        }

        private static List<string> Era(ILoreSource source, LoreEraText era)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(era.Text ?? era.Summary)) { lines.AddRange(LoreBooks.Wrap(era.Text ?? era.Summary, 200)); lines.Add(" "); }
            if (!string.IsNullOrWhiteSpace(era.When)) lines.Insert(0, era.When);
            string own = string.Join(", ", era.Places.Concat(era.Bosses).Select(source.NameOf).Where(n => n != null).Select(n => $"[{n}]"));
            if (own.Length > 0) { lines.Add("Of that age:"); lines.Add(own); lines.Add(" "); }
            foreach (LoreEventText e in era.Events)
            {
                if (!string.IsNullOrWhiteSpace(e.Name)) lines.Add(e.Name);
                if (!string.IsNullOrWhiteSpace(e.Text)) lines.AddRange(LoreBooks.Wrap(e.Text, 200));
                string links = string.Join(", ", e.Places.Concat(e.Bosses).Select(source.NameOf).Where(n => n != null).Select(n => $"[{n}]"));
                if (links.Length > 0) lines.Add(links);
                lines.Add(" ");
            }
            lines.Add("[history]");
            return lines;
        }

        private static List<string> Faction(ILoreSource source, LoreFactionText faction)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(faction.Text ?? faction.Summary)) { lines.AddRange(LoreBooks.Wrap(faction.Text ?? faction.Summary, 200)); lines.Add(" "); }
            string links = string.Join(", ", faction.Places.Concat(faction.Bosses).Select(source.NameOf).Where(n => n != null).Select(n => $"[{n}]"));
            if (links.Length > 0) { lines.Add("Its places and its own:"); lines.Add(links); lines.Add(" "); }
            lines.Add("[history]");
            return lines;
        }

        /// <summary>A place's era, powers and the places or bosses it knows, as links.</summary>
        private static List<string> Ties(ILoreSource source, string era, string[] factions, LoreRelated[] related)
        {
            var lines = new List<string>();
            if (source.FindEra(era) is { } e) lines.Add($"Its age: [{e.Name}]");
            var powers = (factions ?? []).Select(source.FindFaction).Where(f => f != null).Select(f => $"[{f.Name}]").ToList();
            if (powers.Count > 0) lines.Add("Behind it: " + string.Join(", ", powers));
            var known = (related ?? []).Select(r => source.NameOf(r.Id) is { } n ? $"[{n}]" + (string.IsNullOrWhiteSpace(r.Relation) ? "" : $" ({r.Relation})") : null)
                .Where(x => x != null).ToList();
            if (known.Count > 0) lines.Add("Bound up with: " + string.Join(", ", known));
            if (lines.Count > 0) lines.Add(" ");
            return lines;
        }

        private static List<string> Location(ILoreSource source, LoreLocation place, List<LoreBestiaryEntry> bestiary)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(place.Summary)) { lines.AddRange(LoreBooks.Wrap(place.Summary, 200)); lines.Add(" "); }
            if (!string.IsNullOrWhiteSpace(place.Story)) { lines.Add("The story"); lines.AddRange(LoreBooks.Wrap(place.Story, 200)); lines.Add(" "); }
            // A long story's chapters follow in order; the codex pages them (Paginate), so no page is a wall of text.
            foreach (LoreChapter chapter in place.Chapters)
            {
                lines.Add(chapter.Title);
                foreach (string piece in LorePieces.Split(chapter.Text, LorePieces.PageChars))
                    lines.AddRange(LoreBooks.Wrap(piece, 200));
                lines.Add(" ");
            }
            if (!string.IsNullOrWhiteSpace(place.Legend)) { lines.Add("What players say"); lines.AddRange(LoreBooks.Wrap(place.Legend, 200)); lines.Add(" "); }
            lines.AddRange(Ties(source, place.Era, place.Factions, place.Related));
            var bosses = bestiary.Where(b => b.Location.Id == place.Id).ToList();
            if (bosses.Count > 0)
            {
                lines.Add("Who waits there");
                foreach (LoreBestiaryEntry b in bosses)
                    lines.Add($"[{b.Boss.Name}] - " + (b.Unlocked ? "first slain " + LoreBestiary.When(b.FirstKillUtc) : "not yet slain"));
                lines.Add(" ");
            }
            lines.Add("[index]");
            return lines;
        }

        private static List<string> BossLines(LoreBestiaryEntry entry, int realm)
        {
            var lines = new List<string>();
            foreach (string line in LoreBestiary.Page(entry, realm).Skip(1))
                lines.AddRange(line.Length == 0 ? [" "] : LoreBooks.Wrap(line, 200));
            lines[0] = $"[{entry.Location.Name}]" + lines[0][entry.Location.Name.Length..];
            lines.Add(" ");
            lines.Add("[index]");
            return lines;
        }

        /// <summary>Lines into windows of at most <see cref="PageCharacters"/>, each ending with a link to the next.</summary>
        public static List<LoreCodexPage> Paginate(string title, string link, List<string> lines)
        {
            var chunks = new List<List<string>>();
            var current = new List<string>();
            int size = 0;
            foreach (string line in lines)
            {
                if (current.Count > 0 && size + line.Length + 2 > PageCharacters - 40)
                {
                    chunks.Add(current);
                    current = new();
                    size = 0;
                }
                if (current.Count == 0 && line == " ") continue;
                current.Add(line);
                size += line.Length + 2;
            }
            if (current.Count > 0) chunks.Add(current);
            var pages = new List<LoreCodexPage>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i + 1 < chunks.Count) chunks[i].Add($"More: [{link} {i + 2}]");
                string caption = CaptionPrefix + title + (chunks.Count > 1 ? $" ({i + 1}/{chunks.Count})" : "");
                pages.Add(new(caption.Length > 250 ? caption[..250] : caption, chunks[i]));
            }
            return pages;
        }
    }

    [CmdAttribute("&lore", ePrivLevel.Player,
        "The lore of your realm's places and bosses, and whether your realm has slain them",
        "/lore", "/lore <place or boss>", "/lore <place or boss> <page>")]
    public sealed class LoreCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null) return;
            string query = string.Join(' ', args.Skip(1));
            int realm = (int)player.Realm;
            LoreCodexPage page = LoreCodex.Find(LoreChannelHub.Source, realm, LoreFirstKills.All, query);
            if (page == null)
            {
                DisplayMessage(client, $"No lore of {LoreRealm.Name(realm)} by that name. Type /lore for the list.");
                return;
            }
            client.Out.SendCustomTextWindow(page.Caption, page.Lines);
        }
    }
}
