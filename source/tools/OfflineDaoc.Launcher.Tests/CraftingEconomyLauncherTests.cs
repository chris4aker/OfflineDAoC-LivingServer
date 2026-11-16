using System.Data.SQLite;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OfflineDaoc.Launcher;

/// <summary>
/// Crafting 2.0 WP9 in the launcher: the economy page, the bot purge of the Crafting 2.0 tables and the level-50 kit
/// setting. No window: these run beside the Living Server tab's tests in CI. Tests only.
/// </summary>
[TestFixture, NonParallelizable]
public class CraftingEconomyLauncherTests
{
    private string _folder = null!, _database = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "craft-economy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "opendaoc.sqlite3.db");
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private void Exec(string sql)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Count(string sql)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? RepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source/server/GameServer/craft/economy/EconomyTables.cs")))
            dir = Path.GetDirectoryName(dir);
        return dir;
    }

    // ---- the economy page ----

    private const string LedgerSchema = """
        CREATE TABLE offline_economy_ledger (HourUtc TEXT, Realm INT, BotCoin INT, PlayerCoin INT, GuildBanks INT, ListedValue INT,
            FaucetsJson TEXT, SinksJson TEXT, ResidualCopper INT, Bots INT, Crafters INT);
        CREATE TABLE offline_exchange_index (BandKey TEXT, Realm INT, Count INT, Demand INT, IndexCopper INT, FloorCopper INT,
            CeilingCopper INT, DailyPlayerSales INT, DailyDate TEXT, UpdatedUtc TEXT);
        CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT PRIMARY KEY, Description TEXT, DefaultValue TEXT, Value TEXT,
            LastTimeRowUpdated TEXT, ServerProperty_ID TEXT);
        """;

    [Test] public void TheEconomyPageShowsTheLedgerAsRecorded()
    {
        Exec(LedgerSchema + """
            INSERT INTO offline_economy_ledger VALUES ('2026-11-16T20:00:00Z', 1, 5000000, 2000000, 300000, 900000,
                '{"lootcoin":120000,"vendorsale":30000}', '{"stableticket":8000,"exchangetax":5000,"repairkit":2000}', 150, 600, 12);
            INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 1, 5100000, 2000000, 300000, 950000,
                '{"lootcoin":100000}', '{"stableticket":6000,"exchangetax":4000}', -50, 601, 12);
            INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 2, 4000000, 0, 0, 0,
                '{"lootcoin":90000}', '{"medallion":7000}', 0, 590, 9);
            INSERT INTO offline_economy_ledger VALUES ('not an hour', 3, 1, 1, 1, 1, '{}', '{}', 0, 1, 0);
            INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 3, 3000000, 0, 0, 0, 'broken', '{}', 0, 580, 7);
            INSERT INTO offline_exchange_index VALUES ('1|36|25|9|2', 1, 14, 3, 250000, 40000, 900000, 2, '2026-11-16', '2026-11-16T21:00:00Z');
            INSERT INTO offline_exchange_index VALUES ('bad', 1, 1, 1, 1, 1, 1, 0, '', '');
            INSERT INTO ServerProperty VALUES ('bots', 'exchange_tax_percent', '', '0', '5', '', 'exchange_tax_percent');
            """);
        var snapshot = EconomyPage.Read(_database);
        Assert.That(snapshot.Hours, Has.Count.EqualTo(4), "the unreadable hour is skipped");
        Assert.That(snapshot.Unreadable, Has.Count.EqualTo(3), "an hour, a flows value and a band key");
        Assert.That(snapshot.TaxPercent, Is.EqualTo(5));
        Assert.That(snapshot.LedgerEnabled, Is.True, "a missing row is the server's default, on");

        var totals = EconomyPage.Totals(snapshot.Hours);
        Assert.That(totals.Select(t => t.Supply), Is.EqualTo(new[] { 7_300_000L, 7_400_000L + 4_000_000L + 3_000_000L }));
        Assert.That(totals.Select(t => t.Tax), Is.EqualTo(new[] { 5000L, 4000L }), "the tax column is the exchangetax sink");
        Assert.That(EconomyPage.TopSinks(snapshot.Hours, 2), Is.EqualTo(new[] { ("stableticket", 14000L), ("exchangetax", 9000L) }));
        Assert.That(EconomyPage.SumKinds(snapshot.Hours, sinks: false, realm: 1)["lootcoin"], Is.EqualTo(220_000));
        Assert.That(EconomyPage.SumKinds(snapshot.Hours, sinks: false, realm: 1, count: 1)["lootcoin"], Is.EqualTo(100_000), "the window is the latest hours");

        string html = EconomyPage.Render(snapshot, new DateTime(2026, 11, 16, 17, 0, 0));
        Assert.That(html, Does.Contain("Money Supply").And.Contain("Hour by Hour").And.Contain("Exchange tax").And.Contain("Where the Coin Goes"));
        Assert.That(html, Does.Contain("Horse routes").And.Contain("Realm Exchange tax").And.Contain("Monster coin"));
        Assert.That(html, Does.Contain("Plate vest").And.Contain("45–49").And.Contain("100%"), "the price index band");
        Assert.That(html, Does.Contain("Realm Exchange tax: 5%"));
        Assert.That(html, Does.Contain("740g 0s"), "Albion's supply at the latest hour in gold");

        // A copy for the CI preview artifact, beside the Herald's.
        string preview = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-preview");
        Directory.CreateDirectory(preview);
        File.WriteAllText(Path.Combine(preview, "economy.html"), html);
    }

    [Test] public void TheEconomyPageSaysWhenThereIsNothingYet()
    {
        Exec("CREATE TABLE ServerProperty (`Key` TEXT PRIMARY KEY, Value TEXT); INSERT INTO ServerProperty VALUES ('economy_ledger_enabled', 'False');");
        string path = EconomyPage.Write(_database, Path.Combine(_folder, "herald"), DateTime.Now);
        string html = File.ReadAllText(path);
        Assert.That(Path.GetFileName(path), Is.EqualTo("economy.html"));
        Assert.That(html, Does.Contain("Economy ledger: off").And.Contain("has no economy ledger yet").And.Contain("No price index yet"));
    }

    [Test] public void CoinReadsAsGold()
    {
        Assert.That(EconomyPage.Gold(0), Is.EqualTo("0c"));
        Assert.That(EconomyPage.Gold(1234), Is.EqualTo("12s 34c"));
        Assert.That(EconomyPage.Gold(123_456_789), Is.EqualTo("12,345g 67s"));
        Assert.That(EconomyPage.Gold(-20_000), Is.EqualTo("−2g 0s"));
        Assert.That(EconomyPage.Signed(50), Is.EqualTo("+50c"));
        Assert.That(EconomyPage.KindName("exchangetax"), Is.EqualTo("Realm Exchange tax"));
        Assert.That(EconomyPage.KindName("somethingnew"), Is.EqualTo("somethingnew"));
    }

    /// <summary>Every kind the server can write has a reader's name (EconomyFlows.cs' EconomyFlowKind, lower case).</summary>
    [Test] public void EveryFlowKindHasAName()
    {
        string? root = RepoRoot();
        if (root == null) Assert.Ignore("Server source not found beside the tests.");
        string source = File.ReadAllText(Path.Combine(root!, "source/server/GameServer/craft/economy/EconomyFlows.cs"));
        string body = Regex.Match(source, @"enum EconomyFlowKind\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        var kinds = Regex.Matches(body, @"^\s*(\w+)\s*(=\s*\d+)?,", RegexOptions.Multiline).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToList();
        Assert.That(kinds, Has.Count.GreaterThan(20));
        Assert.That(kinds.Where(k => EconomyPage.KindName(k) == k), Is.Empty);
    }

    // ---- the bot purge ----

    private const string BotSchema = """
        CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);
        CREATE TABLE Inventory (Inventory_ID TEXT, OwnerID TEXT);
        CREATE TABLE offline_auction_escrow (BidderBotId INT);
        CREATE TABLE offline_auction_ledger (SellerBotId INT, BuyerBotId INT);
        CREATE TABLE offline_auction_listings (SellerBotId INT, HighBidderBotId INT);
        CREATE TABLE offline_bot_commands (BotId INT);
        INSERT INTO offline_world_bots (BotId, Name) VALUES (1, 'Crafty'), (2, 'Buyer'), (3, 'Other');
        INSERT INTO Inventory VALUES ('a', 'offlinebot:1'), ('b', 'offlinebot:2'), ('c', 'player');
        """;

    private const string EconomySchema = """
        CREATE TABLE offline_bot_types (BotId INT, PersonalityType INT, Cohort INT, StoredUtc TEXT);
        CREATE TABLE offline_bot_crafters (BotId INT, Realm INT, PrimaryCraft INT);
        CREATE TABLE offline_bot_craft_orders (OrderId TEXT, BuyerBotId INT, CrafterBotId INT, Copper INT, Source INT, BuyerId TEXT);
        CREATE TABLE offline_exchange_index (BandKey TEXT);
        CREATE TABLE offline_economy_ledger (HourUtc TEXT, Realm INT);
        CREATE TABLE offline_crafter_customers (CrafterBotId INT, CustomerId TEXT);
        INSERT INTO offline_bot_types VALUES (0, -1, 0, ''), (1, 4, 1, ''), (2, 0, 0, ''), (3, 1, 0, '');
        INSERT INTO offline_bot_crafters VALUES (1, 1, 11), (3, 1, 1);
        INSERT INTO offline_bot_craft_orders VALUES ('o1', 2, 1, 500, 0, 'offlinebot:2'), ('o2', 1, 3, 400, 0, 'offlinebot:1'),
            ('o3', 0, 1, 900, 2, 'player-char'), ('o4', 0, 3, 0, 1, 'quartermaster:1');
        INSERT INTO offline_exchange_index VALUES ('1|36|25|9|2');
        INSERT INTO offline_economy_ledger VALUES ('2026-11-16T21:00:00Z', 1);
        INSERT INTO offline_crafter_customers VALUES (1, 'offlinebot:2'), (3, 'offlinebot:1'), (3, 'offlinebot:2');
        """;

    private void Purge(Action<SQLiteConnection, SQLiteTransaction> purge)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var transaction = c.BeginTransaction();
        purge(c, transaction);
        transaction.Commit();
    }

    /// <summary>Every table of EconomyTables.All has a purge decision, read from the server's own list.</summary>
    [Test] public void EveryEconomyTableHasAPurgeDecision()
    {
        string? root = RepoRoot();
        if (root == null) Assert.Ignore("Server source not found beside the tests.");
        string source = File.ReadAllText(Path.Combine(root!, "source/server/GameServer/craft/economy/EconomyTables.cs"));
        var constants = Regex.Matches(source, @"public const string (\w+) = ""(\w+)"";").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        string all = Regex.Match(source, @"string\[\] All = \[(?<names>[^\]]*)\]").Groups["names"].Value;
        var tables = all.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => constants[name]).ToList();
        Assert.That(tables, Has.Count.GreaterThanOrEqualTo(6));
        Assert.That(BotPurge.EconomyDecisions.Select(d => d.Table), Is.EquivalentTo(tables));
        foreach (var d in BotPurge.EconomyDecisions)
            Assert.That(d.Decision == BotPurge.Decision.KeepWorldRows ? d.OneBot == null && d.AllBots == null : d.OneBot != null && d.AllBots != null,
                Is.True, d.Table);
    }

    [Test] public void DeletingABotRemovesItsCraftingRowsAndKeepsEveryoneElsesCoin()
    {
        Exec(BotSchema + EconomySchema);
        Purge((c, t) => BotPurge.DeleteBot(c, t, 1));
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=1"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:1'"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_types WHERE BotId=1"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_types WHERE BotId=0"), Is.EqualTo(1), "the stored-types marker stays");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_crafters WHERE BotId=1"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_crafters"), Is.EqualTo(1));
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders WHERE OrderId='o2'"), Is.Zero, "its own order goes with it");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders WHERE OrderId IN ('o1','o3','o4')"), Is.EqualTo(3),
            "orders it was to make stay, so their buyers are refunded when they expire");
        Assert.That(Count("SELECT COUNT(*) FROM offline_crafter_customers"), Is.EqualTo(1), "its memories and the memories of it go");
        Assert.That(Count("SELECT COUNT(*) FROM offline_exchange_index") + Count("SELECT COUNT(*) FROM offline_economy_ledger"), Is.EqualTo(2),
            "the world's prices and ledger stay");
    }

    [Test] public void DeletingEveryBotKeepsPlayersOrdersAndTheWorldsLedger()
    {
        Exec(BotSchema + EconomySchema);
        Purge(BotPurge.DeleteAllBots);
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots") + Count("SELECT COUNT(*) FROM Inventory WHERE OwnerID LIKE 'offlinebot:%'"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM Inventory"), Is.EqualTo(1), "the player's item stays");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_types"), Is.EqualTo(1), "only the marker stays");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_crafters") + Count("SELECT COUNT(*) FROM offline_crafter_customers"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders WHERE BuyerBotId<>0"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders"), Is.EqualTo(2), "the player's and the quartermaster's orders expire as usual");
        Assert.That(Count("SELECT COUNT(*) FROM offline_exchange_index") + Count("SELECT COUNT(*) FROM offline_economy_ledger"), Is.EqualTo(2));
    }

    [Test] public void AWorldWithoutTheCraftingTablesIsPurgedAsBefore()
    {
        Exec(BotSchema);
        Purge((c, t) => BotPurge.DeleteBot(c, t, 2));
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots"), Is.EqualTo(2));
        Purge(BotPurge.DeleteAllBots);
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots"), Is.Zero);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.That(BotPurge.OneBotStatements(tables).Last(), Is.EqualTo("DELETE FROM offline_world_bots WHERE BotId=@id"), "the character record last");
        Assert.That(BotPurge.AllBotsStatements(tables).Last(), Is.EqualTo("DELETE FROM offline_world_bots"));
    }

    // ---- hired companions (from True 1.65): a player's companions are never deleted with the gamebots ----

    private const string HiredSchema = """
        CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT, Name TEXT);
        CREATE TABLE offline_hired_companions (BotId INTEGER PRIMARY KEY, OwnerCharacterID TEXT);
        INSERT INTO DOLCharacters VALUES ('char-1', 'Chris');
        INSERT INTO offline_hired_companions VALUES (2, 'char-1'), (3, 'deleted-character');
        """;

    [Test] public void DeletingEveryBotKeepsPlayersHiredCompanionsAndDeletesOrphans()
    {
        Exec(BotSchema + EconomySchema + HiredSchema);
        Purge(BotPurge.DeleteAllBots);
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots"), Is.EqualTo(1), "Buyer is Chris's companion");
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=2"), Is.EqualTo(1));
        Assert.That(Count("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:2'"), Is.EqualTo(1), "its items stay with it");
        Assert.That(Count("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:1'"), Is.Zero);
        Assert.That(Count("SELECT COUNT(*) FROM offline_hired_companions"), Is.EqualTo(1), "the orphan's hire row goes with it");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders WHERE OrderId='o1'"), Is.EqualTo(1), "the companion's own order stays");
        Assert.That(Count("SELECT COUNT(*) FROM offline_bot_craft_orders WHERE OrderId='o2'"), Is.Zero, "a gamebot's order goes");
    }

    [Test] public void DeletingOnePlayersHiredCompanionIsRefused()
    {
        Exec(BotSchema + EconomySchema + HiredSchema);
        Assert.That(() => Purge((c, t) => BotPurge.DeleteBot(c, t, 2)),
            Throws.InvalidOperationException.With.Message.Contains("hired companion"));
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=2"), Is.EqualTo(1));
        Purge((c, t) => BotPurge.DeleteBot(c, t, 3));   // an orphan: deleted like any bot, with its hire row
        Assert.That(Count("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=3") + Count("SELECT COUNT(*) FROM offline_hired_companions WHERE BotId=3"), Is.Zero);
    }

    [Test] public void TheCompanionHireKindHasAName() =>
        Assert.That(EconomyPage.KindName("companionhire"), Is.EqualTo("Companion hires"));

    // ---- the level-50 kit ----

    [Test] public void TheLevel50KitIsOnUnlessSwitchedOff()
    {
        Assert.That(Level50Kit.IsOn((string?)null) && Level50Kit.IsOn("") && Level50Kit.IsOn("True") && Level50Kit.IsOn(" true ") && Level50Kit.IsOn("maybe"), Is.True);
        Assert.That(Level50Kit.IsOn("False") || Level50Kit.IsOn(" false "), Is.False);
        Assert.That(Level50Kit.PurseCopper(true), Is.EqualTo(100_000_000L), "10,000 gold, as before 1.2");
        Assert.That(Level50Kit.PurseCopper(false), Is.EqualTo(250_000L), "the level squared in silver");

        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        Assert.That(Level50Kit.IsOn(c, null), Is.True, "no ServerProperty table: the default");
        using (var command = c.CreateCommand())
        {
            command.CommandText = "CREATE TABLE ServerProperty (`Key` TEXT PRIMARY KEY, Value TEXT)";
            command.ExecuteNonQuery();
        }
        Assert.That(Level50Kit.IsOn(c, null), Is.True, "no row: the default");
        using (var command = c.CreateCommand())
        {
            command.CommandText = "INSERT INTO ServerProperty VALUES ('launcher_level50_kit', 'False')";
            command.ExecuteNonQuery();
        }
        Assert.That(Level50Kit.IsOn(c, null), Is.False);
    }

    /// <summary>The tab's key is the server's declared key (CraftingEconomyProperties.LAUNCHER_LEVEL50_KIT).</summary>
    [Test] public void TheLevel50KitKeyIsTheServers()
    {
        string? root = RepoRoot();
        if (root == null) Assert.Ignore("Server source not found beside the tests.");
        string source = File.ReadAllText(Path.Combine(root!, "source/server/GameServer/craft/economy/CraftingEconomyProperties.cs"));
        Assert.That(source, Does.Contain($"[ServerProperty(\"bots\", \"{Level50Kit.Key}\","));
        Assert.That(SocialExpansionSettings.Defaults.ToStoredValues()[Level50Kit.Key], Is.EqualTo("True"));
    }
}
