using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.LoreChannels;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Lore books (Living Server 1.2): the item templates, their world update (dry run, apply, rollback), reading, drops.</summary>
[TestFixture]
public class UT_LoreChannelBooks
{
    private sealed class MemoryStore : ILoreItemStore
    {
        public readonly Dictionary<string, DbItemTemplate> Rows = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Held = new(StringComparer.OrdinalIgnoreCase);
        public int Writes;
        public DbItemTemplate Find(string id) => id != null && Rows.TryGetValue(id, out var t) ? t : null;
        public IEnumerable<DbItemTemplate> Owned(string packageId) => Rows.Values.Where(t => t.PackageID == packageId);
        public void Add(DbItemTemplate template) { Rows.Add(template.Id_nb, template); Writes++; }
        public void Save(DbItemTemplate template) { Rows[template.Id_nb] = template; Writes++; }
        public void Delete(DbItemTemplate template) { Rows.Remove(template.Id_nb); Writes++; }
        public bool InUse(string templateId) => Held.Contains(templateId);
    }

    [Test]
    public void ABookIsANewTemplateOnTheStockBookModel()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        DbItemTemplate t = LoreBooks.Template(lore, lore.GetLocation("krondon"));
        Assert.That(t.Id_nb, Is.EqualTo("ls_lore_book_krondon"));
        Assert.That(t.Name, Is.EqualTo("Of the Hall under the Hill"));
        Assert.That(t.Model, Is.EqualTo(500), "objects.csv 500 = Book (stock model, no client change)");
        Assert.That(t.PackageID, Is.EqualTo(LoreBooks.PackageId));
        Assert.That(t.ClassType, Is.EqualTo(typeof(LoreBookItem).FullName));
        Assert.That(t.CanDropAsLoot, Is.False, "never in the random loot tables");
        Assert.That(t.Price, Is.EqualTo(100));
        Assert.That(t.Description, Is.EqualTo("Krondon in a sentence. Use the book to read it (3 pages)."));
        Assert.That(LoreBooks.LocationIdOf(t.Id_nb), Is.EqualTo("krondon"));
    }

    [Test]
    public void CreatingTheItemGivesAReadableLoreBook()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        GameInventoryItem item = GameInventoryItem.Create(LoreBooks.Template(lore, lore.GetLocation("krondon")));
        Assert.That(item, Is.InstanceOf<LoreBookItem>());
        Assert.That(item.Id_nb, Is.EqualTo("ls_lore_book_krondon"));
        var delve = new List<string>();
        Assert.That(item.Description, Does.Contain("Use the book to read it"));
    }

    [Test]
    public void TheBookHoldsThePlacesPagesThenItsBossesPages()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        List<string> pages = LoreBooks.Pages(lore, lore.GetLocation("krondon"));
        Assert.That(pages, Is.EqualTo(new[] { "Page one of Krondon.", "Page two of Krondon.", "Of Scurceol Hyrde. The lights are winds." }));
    }

    [Test]
    public void DryRunWritesNothingAndApplyAddsEveryBookOnce()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var store = new MemoryStore();
        List<LoreBookChange> plan = LoreBooks.Plan(store, lore);
        Assert.That(store.Writes, Is.Zero, "the dry run writes nothing");
        Assert.That(plan.All(c => c.Action == LoreBookAction.Add), Is.True);
        Assert.That(plan.Count, Is.EqualTo(lore.AllLocations.Count));
        LoreBooks.Apply(store, lore);
        Assert.That(store.Rows.Count, Is.EqualTo(plan.Count));
        int writes = store.Writes;
        Assert.That(LoreBooks.Apply(store, lore).All(c => c.Action == LoreBookAction.Keep), Is.True, "running again changes nothing");
        Assert.That(store.Writes, Is.EqualTo(writes));
    }

    [Test]
    public void ARowOfAnotherPackageIsNeverTouched()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var store = new MemoryStore();
        var stock = new DbItemTemplate { Id_nb = "ls_lore_book_krondon", Name = "someone else's", PackageID = "Other", Model = 1 };
        store.Rows[stock.Id_nb] = stock;
        List<LoreBookChange> plan = LoreBooks.Apply(store, lore);
        Assert.That(plan.Single(c => c.TemplateId == stock.Id_nb).Action, Is.EqualTo(LoreBookAction.Foreign));
        Assert.That(store.Rows[stock.Id_nb], Is.SameAs(stock));
        Assert.That(stock.Name, Is.EqualTo("someone else's"));
        Assert.That(LoreBooks.Rollback(store).Any(c => c.TemplateId == stock.Id_nb), Is.False, "rollback leaves it too");
        Assert.That(store.Rows.ContainsKey(stock.Id_nb), Is.True);
    }

    [Test]
    public void AChangedTitleUpdatesTheOwnRowAndARemovedBookGoesUnlessHeld()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var store = new MemoryStore();
        LoreBooks.Apply(store, lore);
        store.Rows["ls_lore_book_krondon"].Name = "an older title";
        var smaller = new LoreLibrary(lore.AllLocations.Where(l => l.Id is not ("tur-suil" or "trollheim")), lore.AllBosses);
        store.Held.Add("ls_lore_book_trollheim");
        List<LoreBookChange> plan = LoreBooks.Apply(store, smaller);
        Assert.That(plan.Single(c => c.TemplateId == "ls_lore_book_krondon").Action, Is.EqualTo(LoreBookAction.Update));
        Assert.That(store.Rows["ls_lore_book_krondon"].Name, Is.EqualTo("Of the Hall under the Hill"));
        Assert.That(plan.Single(c => c.TemplateId == "ls_lore_book_tur-suil").Action, Is.EqualTo(LoreBookAction.Remove));
        Assert.That(store.Rows.ContainsKey("ls_lore_book_tur-suil"), Is.False);
        Assert.That(plan.Single(c => c.TemplateId == "ls_lore_book_trollheim").Action, Is.EqualTo(LoreBookAction.InUse));
        Assert.That(store.Rows.ContainsKey("ls_lore_book_trollheim"), Is.True, "a copy someone holds keeps its template");
    }

    [Test]
    public void RollbackRemovesEveryOwnRowNobodyHolds()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var store = new MemoryStore();
        LoreBooks.Apply(store, lore);
        store.Held.Add("ls_lore_book_krondon");
        List<LoreBookChange> done = LoreBooks.Rollback(store);
        Assert.That(store.Rows.Keys, Is.EquivalentTo(new[] { "ls_lore_book_krondon" }));
        Assert.That(done.Count(c => c.Action == LoreBookAction.Remove), Is.EqualTo(lore.AllLocations.Count - 1));
    }

    [Test]
    public void APageFitsTheTextWindowAndUseTurnsThePages()
    {
        string longPage = string.Join(" ", Enumerable.Repeat("word", 300));
        var (caption, lines) = LoreBooks.PageWindow("A Title", [longPage, "two"], 0);
        Assert.That(caption, Is.EqualTo("A Title (1/2)"));
        Assert.That(lines.All(l => l.Length <= 200), Is.True, "no line is split by the packet's 255 limit");
        Assert.That(string.Join(" ", lines.Take(lines.Count - 2)), Is.EqualTo(longPage));
        Assert.That(lines[^1], Does.Contain("turn the page"));
        Assert.That(string.Join("", lines).Length, Is.LessThan(1900), "one page stays under the 2,048-byte packet");
        Assert.That(LoreBooks.PageWindow("A Title", [longPage, "two"], 1).Lines[^1], Does.Contain("start over"));
        string reader = Guid.NewGuid().ToString();
        Assert.That(new[] { LoreBooks.NextPage(reader, "k", 3), LoreBooks.NextPage(reader, "k", 3), LoreBooks.NextPage(reader, "k", 3), LoreBooks.NextPage(reader, "k", 3) },
            Is.EqualTo(new[] { 0, 1, 2, 0 }));
    }

    [Test]
    public void ABookOverOneBindingComesInVolumesWithEveryPage()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        LoreLocation krondon = lore.GetLocation("krondon");
        string[] written = Enumerable.Range(1, 38).Select(i => $"Page {i} of the long book.").ToArray();
        string title = "Of the Hall under the Hill, as the Old Keepers Told It to Brother Aldric";
        var big = krondon with { Book = new LoreBookText(title, written) };
        var library = new LoreLibrary(lore.AllLocations.Select(l => l.Id == "krondon" ? big : l), lore.AllBosses);
        List<string> pages = LoreBooks.Pages(library, big);
        Assert.That(pages.Count, Is.EqualTo(39), "38 pages and the boss's page");
        List<LoreBooks.LoreBookVolume> volumes = LoreBooks.Volumes(library, big);
        Assert.That(volumes.Select(v => v.Pages.Count), Is.EqualTo(new[] { 16, 16, 7 }));
        Assert.That(volumes.SelectMany(v => v.Pages), Is.EqualTo(pages), "every page, in order, nothing cut");
        Assert.That(volumes.Select(v => v.TemplateId), Is.EqualTo(new[] { "ls_lore_book_krondon", "ls_lore_book_krondon_vol2", "ls_lore_book_krondon_vol3" }),
            "the first volume keeps the one-volume id, so copies already held still read");
        Assert.That(volumes.All(v => v.Title.Length <= LoreBooks.MaxNameLength), Is.True);
        Assert.That(volumes[1].Title, Does.EndWith(", Volume II").And.StartWith("Of the Hall under the Hill"));
        Assert.That(volumes.Select(v => (LoreBooks.LocationIdOf(v.TemplateId), LoreBooks.VolumeOf(v.TemplateId))),
            Is.EqualTo(new[] { ("krondon", 1), ("krondon", 2), ("krondon", 3) }));
        Assert.That(LoreBooks.VolumeFor(library, "ls_lore_book_krondon_vol3").Pages[^1], Is.EqualTo(pages[^1]));
        Assert.That(LoreBooks.VolumeFor(library, "ls_lore_book_krondon_vol4"), Is.Null);
        Assert.That(LoreBooks.LocationIdOf("ls_lore_book_tur-suil"), Is.EqualTo("tur-suil"));

        DbItemTemplate second = LoreBooks.Template(big, volumes[1]);
        Assert.That(second.Description, Does.Contain("Volume II of III").And.Contain("(16 pages)"));
        var store = new MemoryStore();
        List<LoreBookChange> plan = LoreBooks.Apply(store, library);
        Assert.That(plan.Count(c => c.TemplateId.StartsWith("ls_lore_book_krondon")), Is.EqualTo(3));
        Assert.That(store.Rows["ls_lore_book_krondon_vol3"].Name, Does.EndWith("Volume III"));
        Assert.That(LoreBooks.Apply(store, library).All(c => c.Action == LoreBookAction.Keep), Is.True);

        var (_, lines) = LoreBooks.PageWindow(volumes[0].Title, volumes[0].Pages, 15, LoreBooks.VolumeTitle(title, 2, 3));
        Assert.That(lines[^1], Does.Contain("goes on in").And.Contain("Volume II"));
        Assert.That(LoreBooks.PageWindow(volumes[2].Title, volumes[2].Pages, 6).Lines[^1], Does.Contain("The end"));

        var dialogue = new LoremasterDialogue(library, LoreRealm.Albion, "Loremaster");
        Assert.That(dialogue.Resolve("book of Krondon").Text, Does.Contain("3 volumes").And.Contain("3 silver"));
        Assert.That(LoreBooks.Volumes(lore, krondon).Count, Is.EqualTo(1), "a short book is one volume");
        Assert.That(LoreBooks.Template(lore, krondon).Name, Is.EqualTo("Of the Hall under the Hill"), "and keeps its plain title");
    }

    [Test]
    public void BooksDropOnlyNearTheirPlace()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        Assert.That(LoreBookDrops.Near(lore, 61, 10000, 10000, 0)?.Id, Is.EqualTo("krondon"), "a region only Krondon uses");
        Assert.That(LoreBookDrops.Near(lore, 1, 400500, 600400, 2100)?.Id, Is.EqualTo("golestandts-lair"), "a shared region: near the lair's area");
        Assert.That(LoreBookDrops.Near(lore, 1, 100000, 100000, 2000), Is.Null, "a shared region far from any area");
        Assert.That(LoreBookDrops.Near(lore, 999, 0, 0, 0), Is.Null);
    }

    [TestCase(0, false, 0, 0, true)]
    [TestCase(-3, false, 0, 0, false)]  // grey: no book
    [TestCase(0, true, 0, 0, false)]    // already has a copy
    [TestCase(0, false, 1, 0, false)]   // the roll missed
    public void TheDropRule(int con, bool holds, int roll, int sinceMinutes, bool drops)
    {
        Assert.That(LoreBookDrops.Allowed(con, holds, 0, 10_000_000, roll), Is.EqualTo(drops));
    }

    [Test]
    public void TheDropCooldown()
    {
        long now = 100 * 60_000L;
        Assert.That(LoreBookDrops.Allowed(0, false, now - 5 * 60_000, now, 0), Is.False);
        Assert.That(LoreBookDrops.Allowed(0, false, now - LoreBookDrops.CooldownMinutes * 60_000, now, 0), Is.True);
    }
}
