using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS.LoreChannels
{
    /// <summary>What the books' world update does to one item template.</summary>
    public enum LoreBookAction { Add, Update, Keep, Foreign, Remove, InUse }

    public sealed record LoreBookChange(string TemplateId, LoreBookAction Action, string Title);

    /// <summary>The item templates the books' world update reads and writes (the world database, or a test's).</summary>
    public interface ILoreItemStore
    {
        DbItemTemplate Find(string id);
        IEnumerable<DbItemTemplate> Owned(string packageId);
        void Add(DbItemTemplate template);
        void Save(DbItemTemplate template);
        void Delete(DbItemTemplate template);
        /// <summary>Whether any row of the Inventory table (packs, vaults) holds an item of this template.</summary>
        bool InUse(string templateId);
    }

    /// <summary>
    /// Living Server 1.2 lore books: one readable book per location, sold by the capital loremasters and now and then
    /// dropped near the place it tells of. Each is a new item template (id ls_lore_book_&lt;location&gt;, the client's
    /// stock "Book" model 500, PackageID LivingServerLore, ClassType LoreBookItem); no stock item is changed.
    ///
    /// The world update: at every start the server compares the templates with the lore and adds or updates its own rows
    /// (only rows with its PackageID; a row of that id from anything else is left alone and that book is not offered),
    /// and removes its own rows for books the lore no longer has unless someone holds a copy. <see cref="Plan"/> is the
    /// dry run, <see cref="Apply"/> writes, <see cref="Rollback"/> removes every own row nobody holds. Offline, without
    /// the server: tools/claude-version/lore_books.py (dry run, rollback --apply).
    /// </summary>
    public static class LoreBooks
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string PackageId = "LivingServerLore";
        public const string IdPrefix = "ls_lore_book_";
        public const string ClassType = "DOL.GS.LoreChannels.LoreBookItem";
        public const int BookModel = 500;
        public const long Price = 100;
        public static string PriceText => "a silver";

        /// <summary>
        /// The pages one book item holds (LoreBible.MaxBookPages). A longer book is never cut: it is bound in volumes of
        /// this many pages, each its own item (ls_lore_book_&lt;location&gt;, then ..._vol2, ..._vol3), sold together.
        /// </summary>
        public const int VolumePages = 16;
        public const int MaxNameLength = 60;
        private const string VolumeTag = "_vol";

        /// <summary>One bound volume of a location's book: its item id, its name and its pages.</summary>
        public sealed record LoreBookVolume(string LocationId, int Number, int Count, string TemplateId, string Title, List<string> Pages)
        {
            public bool IsLast => Number >= Count;
        }

        public static string TemplateId(string locationId, int volume = 1) =>
            IdPrefix + (locationId ?? "").ToLowerInvariant() + (volume > 1 ? VolumeTag + volume : "");

        public static string LocationIdOf(string templateId)
        {
            if (templateId == null || !templateId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = templateId[IdPrefix.Length..];
            int tag = rest.LastIndexOf(VolumeTag, StringComparison.Ordinal);
            // Location ids are kebab case (no "_"), so "_vol<n>" can only be the volume.
            return tag > 0 && int.TryParse(rest[(tag + VolumeTag.Length)..], out int n) && n > 1 ? rest[..tag] : rest;
        }

        /// <summary>The volume a book item is (1 for the first, or a one-volume book).</summary>
        public static int VolumeOf(string templateId)
        {
            string location = LocationIdOf(templateId);
            if (location == null) return 0;
            string rest = templateId[(IdPrefix.Length + location.Length)..];
            return rest.StartsWith(VolumeTag, StringComparison.Ordinal) && int.TryParse(rest[VolumeTag.Length..], out int n) ? n : 1;
        }

        private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

        public static string Numeral(int n) => n >= 1 && n <= Numerals.Length ? Numerals[n - 1] : n.ToString();

        /// <summary>
        /// The item name: the title, and for a book in volumes ", Volume II". The name stays within
        /// <see cref="MaxNameLength"/>; a title too long loses words at its end, never the volume.
        /// </summary>
        public static string VolumeTitle(string title, int number, int count)
        {
            title = (title ?? "").Trim();
            string suffix = count > 1 ? $", Volume {Numeral(number)}" : "";
            int room = MaxNameLength - suffix.Length;
            if (title.Length > room)
            {
                int cut = title.LastIndexOf(' ', Math.Max(0, room - 1));
                title = (cut > room / 2 ? title[..cut] : title[..room]).TrimEnd(' ', ',', ';', ':');
            }
            return title + suffix;
        }

        /// <summary>
        /// The book in volumes: its pages (<see cref="Pages"/>) bound <see cref="VolumePages"/> to a volume, in order, so
        /// every page of the lore is in one of them. One volume for most books. Pure.
        /// </summary>
        public static List<LoreBookVolume> Volumes(ILoreSource source, LoreLocation location)
        {
            var volumes = new List<LoreBookVolume>();
            List<string> pages = Pages(source, location);
            if (pages.Count == 0) return volumes;
            int count = (pages.Count + VolumePages - 1) / VolumePages;
            for (int i = 0; i < count; i++)
                volumes.Add(new(location.Id, i + 1, count, TemplateId(location.Id, i + 1), VolumeTitle(location.Book.Title, i + 1, count),
                    pages.Skip(i * VolumePages).Take(VolumePages).ToList()));
            return volumes;
        }

        /// <summary>The volume a book item's id names, or null (no such place, or the book has fewer volumes now).</summary>
        public static LoreBookVolume VolumeFor(ILoreSource source, string templateId)
        {
            LoreLocation location = source.GetLocation(LocationIdOf(templateId));
            if (location?.Book == null) return null;
            int number = VolumeOf(templateId);
            List<LoreBookVolume> volumes = Volumes(source, location);
            return number >= 1 && number <= volumes.Count ? volumes[number - 1] : null;
        }

        /// <summary>The book's examine text (its delve): what it tells of and how to read it.</summary>
        public static string Summary(LoreLocation location, int pages, int volume = 1, int volumes = 1) =>
            $"{(string.IsNullOrWhiteSpace(location.Summary) ? $"A book about {location.Name}." : location.Summary.Trim())} " +
            (volumes > 1 ? $"Volume {Numeral(volume)} of {Numeral(volumes)}. " : "") +
            $"Use the book to read it ({pages} page{(pages == 1 ? "" : "s")}).";

        /// <summary>The template of a location's book (its first volume).</summary>
        public static DbItemTemplate Template(ILoreSource source, LoreLocation location) =>
            Volumes(source, location) is { Count: > 0 } volumes ? Template(location, volumes[0]) : Template(location, new(location.Id, 1, 1, TemplateId(location.Id),
                VolumeTitle(location.Book.Title, 1, 1), []));

        /// <summary>The template a volume of a location's book should have.</summary>
        public static DbItemTemplate Template(LoreLocation location, LoreBookVolume volume) => new()
        {
            Id_nb = volume.TemplateId,
            Name = volume.Title,
            Description = Summary(location, volume.Pages.Count, volume.Number, volume.Count),
            Model = BookModel,
            Item_Type = 40,
            Object_Type = (int)eObjectType.GenericItem,
            Level = 1,
            Price = Price,
            Weight = 5,
            PackSize = 1,
            MaxCount = 1,
            Quality = 100,
            Condition = 50000,
            MaxCondition = 50000,
            Durability = 50000,
            MaxDurability = 50000,
            IsPickable = true,
            IsDropable = true,
            IsTradable = true,
            CanDropAsLoot = false,
            Realm = 0,
            ClassType = ClassType,
            PackageID = PackageId,
        };

        /// <summary>The book's pages: the location's pages, then each of its bosses' book pages under the boss's name.</summary>
        public static List<string> Pages(ILoreSource source, LoreLocation location)
        {
            var pages = new List<string>();
            if (location?.Book == null) return pages;
            var written = new List<string>();
            written.AddRange(location.Book.Pages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            foreach (LoreBoss boss in source.BossesOf(location))
            {
                string[] bossPages = boss.BookPages.Length > 0 ? boss.BookPages : string.IsNullOrWhiteSpace(boss.BookPage) ? [] : [boss.BookPage];
                for (int i = 0; i < bossPages.Length; i++)
                    if (!string.IsNullOrWhiteSpace(bossPages[i]))
                        written.Add(i == 0 ? $"Of {boss.Name}. {bossPages[i].Trim()}" : bossPages[i].Trim());
            }
            // A page longer than one text window holds (2,048 bytes, LorePieces.PageChars) goes on over more pages; nothing is cut.
            foreach (string page in written)
                pages.AddRange(LorePieces.Split(page, LorePieces.PageChars));
            return pages;
        }

        private static bool Same(DbItemTemplate a, DbItemTemplate b) =>
            a.Name == b.Name && a.Description == b.Description && a.Model == b.Model && a.Price == b.Price &&
            a.ClassType == b.ClassType && a.Item_Type == b.Item_Type && a.Object_Type == b.Object_Type && a.CanDropAsLoot == b.CanDropAsLoot;

        /// <summary>The dry run: what <see cref="Apply"/> would do. Nothing is written.</summary>
        public static List<LoreBookChange> Plan(ILoreItemStore store, ILoreSource source)
        {
            var changes = new List<LoreBookChange>();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LoreLocation location in source.AllLocations.Where(LoremasterDialogue.HasBook))
                foreach (LoreBookVolume volume in Volumes(source, location))
                {
                    DbItemTemplate desired = Template(location, volume);
                    wanted.Add(desired.Id_nb);
                    DbItemTemplate existing = store.Find(desired.Id_nb);
                    LoreBookAction action = existing == null ? LoreBookAction.Add
                        : !string.Equals(existing.PackageID, PackageId, StringComparison.Ordinal) ? LoreBookAction.Foreign
                        : Same(existing, desired) ? LoreBookAction.Keep : LoreBookAction.Update;
                    changes.Add(new(desired.Id_nb, action, volume.Title));
                }
            foreach (DbItemTemplate own in store.Owned(PackageId).Where(t => !wanted.Contains(t.Id_nb)).ToList())
                changes.Add(new(own.Id_nb, store.InUse(own.Id_nb) ? LoreBookAction.InUse : LoreBookAction.Remove, own.Name));
            return changes;
        }

        /// <summary>Writes the plan; returns it. A template another package owns is never touched.</summary>
        public static List<LoreBookChange> Apply(ILoreItemStore store, ILoreSource source)
        {
            List<LoreBookChange> plan = Plan(store, source);
            foreach (LoreBookChange change in plan)
            {
                LoreLocation location = source.GetLocation(LocationIdOf(change.TemplateId));
                LoreBookVolume volume = VolumeFor(source, change.TemplateId);
                switch (change.Action)
                {
                    case LoreBookAction.Add:
                        store.Add(Template(location, volume));
                        break;
                    case LoreBookAction.Update:
                        DbItemTemplate row = store.Find(change.TemplateId), desired = Template(location, volume);
                        row.Name = desired.Name; row.Description = desired.Description; row.Model = desired.Model; row.Price = desired.Price;
                        row.ClassType = desired.ClassType; row.Item_Type = desired.Item_Type; row.Object_Type = desired.Object_Type;
                        row.CanDropAsLoot = desired.CanDropAsLoot;
                        store.Save(row);
                        break;
                    case LoreBookAction.Remove:
                        store.Delete(store.Find(change.TemplateId));
                        break;
                }
            }
            return plan;
        }

        /// <summary>Removes every book template this wrote that nobody holds; returns what it did (InUse rows stay).</summary>
        public static List<LoreBookChange> Rollback(ILoreItemStore store)
        {
            var changes = new List<LoreBookChange>();
            foreach (DbItemTemplate own in store.Owned(PackageId).ToList())
            {
                bool held = store.InUse(own.Id_nb);
                if (!held) store.Delete(own);
                changes.Add(new(own.Id_nb, held ? LoreBookAction.InUse : LoreBookAction.Remove, own.Name));
            }
            return changes;
        }

        // --- the running server

        private static readonly ConcurrentDictionary<string, DbItemTemplate> Offered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The book template of a location the server offers (added by the world update), or null. Its first volume.</summary>
        public static DbItemTemplate Offer(string locationId) =>
            locationId != null && Offered.TryGetValue(TemplateId(locationId), out DbItemTemplate t) ? t : null;

        /// <summary>Every volume of a location's book, in order, or empty unless the server offers them all.</summary>
        public static List<DbItemTemplate> OfferAll(string locationId)
        {
            var all = new List<DbItemTemplate>();
            LoreLocation location = LoreChannelHub.Source.GetLocation(locationId);
            if (location?.Book == null) return all;
            foreach (LoreBookVolume volume in Volumes(LoreChannelHub.Source, location))
            {
                if (!Offered.TryGetValue(volume.TemplateId, out DbItemTemplate t)) return [];
                all.Add(t);
            }
            return all;
        }

        /// <summary>The price of a book in <paramref name="volumes"/> volumes, in words ("a silver", "3 silver").</summary>
        public static string PriceFor(int volumes) => volumes <= 1 ? PriceText : $"{volumes} silver";

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            Refresh();
            LoreChannelHub.SourceChanged -= Refresh;
            LoreChannelHub.SourceChanged += Refresh;
        }

        public static void Refresh()
        {
            try
            {
                var store = new WorldLoreItemStore();
                ILoreSource source = LoreChannelHub.Source;
                List<LoreBookChange> plan = Apply(store, source);
                Offered.Clear();
                foreach (LoreBookChange change in plan.Where(c => c.Action is LoreBookAction.Add or LoreBookAction.Update or LoreBookAction.Keep))
                    if (store.Find(change.TemplateId) is { } template)
                        Offered[template.Id_nb] = template;
                Log.Info("LORE_BOOKS " + string.Join(" ", Enum.GetValues<LoreBookAction>().Select(a => $"{a.ToString().ToLowerInvariant()}={plan.Count(c => c.Action == a)}")));
                foreach (LoreBookChange foreign in plan.Where(c => c.Action == LoreBookAction.Foreign))
                    Log.Warn($"LORE_BOOK_SKIPPED {foreign.TemplateId}: an item template of that id belongs to something else; the book is not offered");
                LoreBookDrops.Register(source);
            }
            catch (Exception exception)
            {
                Log.Error("Lore books: the book templates could not be checked; no books are offered", exception);
            }
        }

        /// <summary>
        /// Sells a player a location's book, every volume of it: checks room for all and coin for all, then takes the coin.
        /// False with a reason.
        /// </summary>
        public static bool TryBuy(GamePlayer player, string locationId, out string reason)
        {
            reason = null;
            List<DbItemTemplate> volumes = OfferAll(locationId);
            if (player == null || volumes.Count == 0)
            {
                reason = "I have no copy of that one to give.";
                return false;
            }
            long price = volumes.Sum(v => v.Price);
            lock (player.Inventory.Lock)
            {
                if (player.GetCurrentMoney() < price)
                {
                    reason = $"A copy is {Money.GetString(price)}, and your purse says otherwise.";
                    return false;
                }
                if (!player.Inventory.IsSlotsFree(volumes.Count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                {
                    reason = volumes.Count == 1 ? "Your packs are full. Make room and come back."
                        : $"It is {volumes.Count} volumes, and your packs have not the room. Make room and come back.";
                    return false;
                }
                foreach (DbItemTemplate template in volumes)
                {
                    if (!player.Inventory.AddTemplate(GameInventoryItem.Create(template), 1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    {
                        reason = "Your packs are full. Make room and come back.";
                        return false;
                    }
                    InventoryLogging.LogInventoryAction("(LORE;" + template.Id_nb + ")", player, eInventoryActionType.Merchant, template, 1);
                    player.RemoveMoney(template.Price, $"You pay {{0}} for {template.Name}.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                }
            }
            return true;
        }

        // --- reading

        private static readonly ConcurrentDictionary<(string Player, string Book), int> Reading = new();

        /// <summary>
        /// The text window of one page: the caption and its lines (each under the packet's 255-character line). The last
        /// page of a volume that is not the book's last names the volume that goes on (<paramref name="next"/>).
        /// </summary>
        public static (string Caption, List<string> Lines) PageWindow(string title, IReadOnlyList<string> pages, int page, string next = null)
        {
            int count = Math.Max(1, pages.Count);
            page = Math.Clamp(page, 0, count - 1);
            var lines = new List<string>();
            lines.AddRange(Wrap(pages.Count == 0 ? "The pages are blank." : pages[page], 200));
            lines.Add(" ");
            bool last = page + 1 >= count;
            lines.Add(!last ? "(Use the book again to turn the page.)"
                : next != null ? $"(The tale goes on in {next}.{(count == 1 ? "" : " Use the book again to start over.")})"
                : count == 1 ? "(The end.)" : "(The end. Use the book again to start over.)");
            return ($"{title} ({page + 1}/{count})", lines);
        }

        /// <summary>The page a player reads next (each use turns one page; after the last, the first again).</summary>
        public static int NextPage(string player, string book, int pages)
        {
            int page = Reading.AddOrUpdate((player ?? "", book ?? ""), 0, (_, last) => pages <= 0 ? 0 : (last + 1) % pages);
            return page;
        }

        public static IEnumerable<string> Wrap(string text, int width)
        {
            var line = new StringBuilder();
            foreach (string word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word.Length > width ? word[..width] : word);
            }
            if (line.Length > 0) yield return line.ToString();
        }

        public static void Read(GamePlayer player, DbInventoryItem item)
        {
            if (player?.Client == null) return;
            ILoreSource source = LoreChannelHub.Source;
            LoreBookVolume volume = VolumeFor(source, item?.Id_nb);
            if (volume == null)
            {
                player.Out.SendMessage("The ink has faded past reading.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            string next = volume.IsLast ? null : VolumeTitle(source.GetLocation(volume.LocationId).Book.Title, volume.Number + 1, volume.Count);
            var (caption, lines) = PageWindow(volume.Title, volume.Pages, NextPage(player.InternalID ?? player.Name, volume.TemplateId, volume.Pages.Count), next);
            player.Out.SendCustomTextWindow(caption, lines);
        }

        /// <summary>The killer a book could drop for: a real player, or the player who owns the pet that struck last.</summary>
        public static GamePlayer PlayerOf(GameObject killer) =>
            killer as GamePlayer ?? (killer is GameNPC { Brain: IControlledBrain controlled } ? controlled.GetPlayerOwner() : null);
    }

    /// <summary>A lore book in an inventory: use reads it a page at a time, the delve shows its summary.</summary>
    public class LoreBookItem : GameInventoryItem
    {
        public LoreBookItem() { }
        public LoreBookItem(DbItemTemplate template) : base(template) { }
        public LoreBookItem(DbItemUnique template) : base(template) { }
        public LoreBookItem(DbInventoryItem item) : base(item) { }

        public override bool Use(GamePlayer player)
        {
            LoreBooks.Read(player, this);
            return true;
        }
    }

    /// <summary>The world database's item templates.</summary>
    public sealed class WorldLoreItemStore : ILoreItemStore
    {
        public DbItemTemplate Find(string id) => GameServer.Database.FindObjectByKey<DbItemTemplate>(id);
        public IEnumerable<DbItemTemplate> Owned(string packageId) => DOLDB<DbItemTemplate>.SelectObjects(DB.Column("PackageID").IsEqualTo(packageId));
        public void Add(DbItemTemplate template) => GameServer.Database.AddObject(template);
        public void Save(DbItemTemplate template) => GameServer.Database.SaveObject(template);
        public void Delete(DbItemTemplate template) => GameServer.Database.DeleteObject(template);
        public bool InUse(string templateId) => DOLDB<DbInventoryItem>.SelectObjects(DB.Column("ITemplate_Id").IsEqualTo(templateId)).Count > 0;
    }

    /// <summary>
    /// Books found near their place: rarely, to real players only, never loot-table bloat. A kill by a player (or the
    /// player's pet) of a monster not grey to them, in a place's region (a region only that place uses, or within
    /// twice an atmosphere area's radius of it), drops that place's book with a 1-in-<see cref="OneIn"/> chance, at
    /// most once per <see cref="CooldownMinutes"/> minutes per player, and never while the player already has a copy.
    /// </summary>
    public static class LoreBookDrops
    {
        public const int OneIn = 250;
        public const int CooldownMinutes = 20;
        private static readonly ConcurrentDictionary<string, long> LastDrop = new(StringComparer.Ordinal);
        private static readonly LootGeneratorLoreBook Generator = new();
        private static readonly HashSet<int> Registered = new();

        /// <summary>The place a kill at this spot is near (null when none, or the region is shared and no area is near).</summary>
        public static LoreLocation Near(ILoreSource source, int region, int x, int y, int z)
        {
            var inRegion = source.AllLocations.Where(l => LoremasterDialogue.HasBook(l) && (l.Regions ?? []).Contains(region)).ToList();
            foreach (LoreLocation location in inRegion)
                foreach (LoreArea area in location.Areas ?? [])
                {
                    long dx = x - area.X, dy = y - area.Y, r = Math.Max(300, area.Radius) * 2L;
                    if (dx * dx + dy * dy <= r * r && Math.Abs(z - area.Z) <= Math.Max(600, r)) return location;
                }
            var owners = source.AllLocations.Where(l => (l.Regions ?? []).Contains(region)).ToList();
            return owners.Count == 1 && inRegion.Count == 1 ? inRegion[0] : null;
        }

        /// <summary>The rule after the place: not grey, no copy held, out of cooldown, and the roll (0 drops).</summary>
        public static bool Allowed(int conLevel, bool holdsCopy, long lastDropMs, long nowMs, int roll) =>
            conLevel > -3 && !holdsCopy && (lastDropMs <= 0 || nowMs - lastDropMs >= CooldownMinutes * 60_000L) && roll == 0;

        internal static void Register(ILoreSource source)
        {
            lock (Registered)
                foreach (int region in source.AllLocations.Where(LoremasterDialogue.HasBook).SelectMany(l => l.Regions ?? []).Distinct())
                    if (Registered.Add(region))
                        LootMgr.RegisterLootGenerator(Generator, null, null, null, region);
        }

        private sealed class LootGeneratorLoreBook : LootGeneratorBase
        {
            public override LootList GenerateLoot(GameNPC mob, GameObject killer)
            {
                LootList loot = base.GenerateLoot(mob, killer);
                GamePlayer player = LoreBooks.PlayerOf(killer);
                if (player?.Client == null || mob == null || mob is GameBot) return loot;
                LoreLocation location = Near(LoreChannelHub.Source, mob.CurrentRegionID, mob.X, mob.Y, mob.Z);
                if (location == null) return loot;
                // A book in volumes drops one at a time: the first volume the player does not hold yet.
                DbItemTemplate template = LoreBooks.OfferAll(location.Id)
                    .FirstOrDefault(t => player.Inventory.CountItemTemplate(t.Id_nb, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == 0);
                bool holds = template == null;
                template ??= LoreBooks.Offer(location.Id);
                if (template == null) return loot;
                string key = player.InternalID ?? player.Name;
                long now = GameLoop.GameLoopTime;
                if (!Allowed(player.GetConLevel(mob), holds, LastDrop.GetValueOrDefault(key), now, Random.Shared.Next(OneIn)))
                    return loot;
                LastDrop[key] = now;
                loot.AddFixed(template, 1);
                return loot;
            }
        }
    }
}
