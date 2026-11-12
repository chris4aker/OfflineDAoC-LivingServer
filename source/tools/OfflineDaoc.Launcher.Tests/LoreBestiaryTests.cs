using System.Data.SQLite;
using DOL.GS;
using DOL.GS.LoreChannels;
using NUnit.Framework;
using OfflineDaoc.Launcher;

/// <summary>Living Server 1.2: the Realm Records tab's Bestiary page, the records' chronicle line and the Herald's bestiary.</summary>
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class LoreBestiaryTests
{
    private string _folder = null!, _ledger = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "lore-bestiary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _ledger = Path.Combine(_folder, RealmEventRecordStore.FileName);
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static LoreLibrary Lore() => new(
        [new LoreLocation("krondon", "Krondon", "Albion", [61], "A hall.", "The hall's story.", "Bring fifty.", null, null, ["scurceol-hyrde", "orylle"], [])],
        [
            new LoreBoss("scurceol-hyrde", "Scurceol Hyrde", "krondon/scurceolhyrde", "krondon", "Albion", "The storm-keeper's story.", "Kill the four lights first.", null, null, null),
            new LoreBoss("orylle", "Orylle", "krondon/orylle", "krondon", "Albion", "Orylle's story.", "Tank and spank.", null, null,
                new LoreChronicleText("{realm} felled Orylle in {minutes} minutes, led by {raidLeader}.", "Orylle held against {realm}.")),
        ]);

    private static RealmEventRecord Raid(string outcome, string details, string realm = "Albion") =>
        new(Guid.NewGuid().ToString("N"), "dungeon-albion-krondon", "Krondon", "Epic dungeon", realm, "2026-11-03T20:00:00Z",
            "2026-11-03T20:47:00Z", "Ended", outcome, details, 50, 48);

    [Test]
    public void TheLauncherShipsTheSameLoreFilesAsTheServer()
    {
        Assert.That(LauncherLore.Source.GetLocation("krondon"), Is.Not.Null);
        Assert.That(LauncherLore.Source.GetBoss("scurceol-hyrde")?.Chronicle?.Win, Does.Contain("{realm}"));
    }

    [Test]
    public void ARecordsDetailsStartWithItsChronicle()
    {
        var won = Raid("Boss defeated", LoreChronicle.WithRaidLeader("Final defeated.", "Ector"));
        Assert.That(LauncherLore.Describe(won, Lore()), Does.StartWith("Albion felled Orylle in 47 minutes, led by Ector."));
        Assert.That(LauncherLore.Describe(won, Lore()), Does.EndWith(won.Details));
        var interrupted = Raid("Interrupted", "Server stopped.");
        Assert.That(LauncherLore.Describe(interrupted, Lore()), Is.EqualTo("Server stopped."));
    }

    [Test]
    public async Task TheBestiaryPageListsBossesAndOpensTheSlainOnes()
    {
        RealmEventRecordStore.Save(_ledger, [Raid("Boss defeated", "x"), Raid("Defeated", "y")]);
        LoreFirstKillStore.Add(_ledger, [new LoreFirstKill(2, "scurceol-hyrde", "2026-11-01T10:00:00Z", "Bjorn", "")]);
        using var page = new BestiaryControl(() => _ledger, Lore());
        using var host = new Form { ClientSize = new Size(1000, 520), ShowInTaskbar = false, Opacity = 0, Location = new Point(-20000, -20000), StartPosition = FormStartPosition.Manual };
        host.Controls.Add(page); host.Show();
        await page.RefreshAsync();
        Assert.That(page.List.Items.Cast<string>(), Is.EqualTo(new[] { "Scurceol Hyrde - Krondon (not yet slain)", "Orylle - Krondon (slain 3 Nov 2026)" }));
        page.List.SelectedIndex = 0;
        Assert.That(page.Page.Text, Does.Contain(LoreBestiaryEntry.LockedStory).And.Contain("Kill the four lights first."),
            "Midgard's kill opens nothing for Albion; the legend is always shown");
        page.List.SelectedIndex = 1;
        Assert.That(page.Page.Text, Does.Contain("Orylle's story.").And.Contain("Raid record: 1 won, 1 lost"));
        Assert.That(page.Status.Text, Does.StartWith("1 of 2 slain by Albion"));
        using var image = new Bitmap(page.Width, page.Height);
        page.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        string preview = Path.Combine(TestContext.CurrentContext.WorkDirectory, "realm-records-bestiary.png");
        image.Save(preview); TestContext.AddTestAttachment(preview);
    }

    [Test]
    public async Task AMissingLedgerShowsEveryBossLocked()
    {
        using var page = new BestiaryControl(() => Path.Combine(_folder, "missing.sqlite3"), Lore());
        await page.RefreshAsync();
        Assert.That(page.List.Items.Count, Is.EqualTo(2));
        Assert.That(page.Status.Text, Does.StartWith("0 of 2 slain"));
        Assert.That(File.Exists(Path.Combine(_folder, "missing.sqlite3")), Is.False, "reading creates nothing");
    }

    [Test]
    public void TheHeraldTellsSlainBossesStories()
    {
        string database = Path.Combine(_folder, "opendaoc.sqlite3.db");
        using (var c = new SQLiteConnection($"Data Source={database};Pooling=False")) { c.Open(); }
        LoreFirstKillStore.Add(_ledger, [new LoreFirstKill(1, "scurceol-hyrde", "2026-11-04T10:00:00Z", "Ector", "")]);
        string html = HeraldPage.Build(database, _ledger, new DateTime(2026, 11, 6, 12, 0, 0));
        Assert.That(html, Does.Contain("Bestiary of the Realms").And.Contain("Scurceol Hyrde"));
        string empty = Path.Combine(_folder, "none.sqlite3");
        Assert.That(HeraldPage.Build(database, empty, new DateTime(2026, 11, 6, 12, 0, 0)), Does.Not.Contain("Bestiary of the Realms"),
            "nothing slain, no section");
    }
}
