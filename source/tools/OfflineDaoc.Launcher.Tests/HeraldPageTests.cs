using System.Data.SQLite;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable]
public class HeraldPageTests
{
    private string _folder = null!, _database = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "herald-" + Guid.NewGuid().ToString("N"));
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

    private void Seed()
    {
        Exec("""
            CREATE TABLE DOLCharacters (Name TEXT, Realm INT, Class INT, Level INT, RealmPoints INT, GuildID TEXT);
            CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY, Name TEXT, Realm INT, ClassName TEXT, Level INT, RealmPoints INT, IsRetired INT);
            CREATE TABLE Guild (GuildID TEXT, GuildName TEXT, Realm INT, RealmPoints INT, MeritPoints INT, IsStartingGuild INT);
            CREATE TABLE offline_bot_guild_members (BotId INT, GuildId TEXT);
            CREATE TABLE Keep (Name TEXT, Realm INT, OriginalRealm INT, Level INT, ClaimedGuildName TEXT);
            CREATE TABLE Relic (Realm INT, OriginalRealm INT, relicType INT, LastCaptureDate TEXT);
            CREATE TABLE offline_server_firsts (Title TEXT, Realm INT, Name TEXT, ClassName TEXT, AchievedUtc TEXT);
            CREATE TABLE offline_bot_nemesis (KillerName TEXT, KillerRealm INT, Kills INT);
            INSERT INTO DOLCharacters VALUES ('Chrisplays', 1, 1, 50, 7200, 'g1');
            INSERT INTO offline_world_bots VALUES (1, 'Brannoc', 3, 'Champion', 50, 30000, 0);
            INSERT INTO offline_world_bots VALUES (2, 'Retiredguy', 2, 'Warrior', 50, 999999, 1);
            INSERT INTO offline_world_bots VALUES (3, '<script>x</script>', 2, 'Healer', 20, 10, 0);
            INSERT INTO Guild VALUES ('g1', 'Knights of the Dawn', 1, 5000, 120, 0);
            INSERT INTO Guild VALUES ('g2', 'Fianna', 3, 9000, 300, 0);
            INSERT INTO Guild VALUES ('s1', 'Starting Guild', 1, 0, 0, 1);
            INSERT INTO offline_bot_guild_members VALUES (1, 'g2');
            INSERT INTO Keep VALUES ('Caer Benowyc', 3, 1, 5, 'Fianna');
            INSERT INTO Keep VALUES ('Dun Crauchon', 3, 3, 5, '');
            INSERT INTO Relic VALUES (3, 1, 0, '2026-10-16 20:00:00');
            INSERT INTO Relic VALUES (3, 3, 1, '');
            INSERT INTO offline_server_firsts VALUES ('First Champion to level 50', 3, 'Brannoc', 'Champion', '2026-10-16T20:00:00Z');
            INSERT INTO offline_server_firsts VALUES ('First to realm rank 9', 0, '', '', '');
            INSERT INTO offline_bot_nemesis VALUES ('Chrisplays', 1, 7);
            """);
    }

    [Test] public void RealmRanksFollowTheServersTable()
    {
        Assert.That(HeraldPage.RealmRank(0), Is.EqualTo("RR1L0"));
        Assert.That(HeraldPage.RealmRank(25), Is.EqualTo("RR1L2"));
        Assert.That(HeraldPage.RealmRank(7_124), Is.EqualTo("RR1L9"));
        Assert.That(HeraldPage.RealmRank(7_125), Is.EqualTo("RR2L0"));
        Assert.That(HeraldPage.RealmRank(61_750), Is.EqualTo("RR3L0"));
    }

    [Test] public void BuildsTheNewsFromTheDatabaseAndEscapesNames()
    {
        Seed();
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), new DateTime(2026, 10, 16, 21, 0, 0));
        Assert.That(html, Does.Contain("The Camelot Herald"));
        Assert.That(html, Does.Contain("Brannoc").And.Contain("Chrisplays"));
        Assert.That(html, Does.Not.Contain("Retiredguy"), "retired bots are not news");
        Assert.That(html, Does.Not.Contain("<script>x"), "names are escaped");
        Assert.That(html, Does.Contain("&lt;script&gt;x"));
        Assert.That(html, Does.Contain("Scabbard of Excalibur").And.Contain("captured from Albion"));
        Assert.That(html, Does.Contain("Caer Benowyc"), "a keep in enemy hands is listed");
        Assert.That(html, Does.Contain("First Champion to level 50"));
        Assert.That(html, Does.Not.Contain("First to realm rank 9"), "firsts reached before records began are not named");
        Assert.That(html, Does.Contain("Most Wanted"));
        Assert.That(html, Does.Contain("Fianna").And.Not.Contain("Starting Guild"));
        Assert.That(html, Does.Contain("RR2L0"), "Chrisplays has 7,200 realm points");
    }

    [Test] public void APlayersGuildCountsTheGamebotsHeInvited()
    {
        // r/daoc report (1.1.1): a player's guild with 30 invited gamebots showed 1 member, because the Herald read only
        // the bot guilds' table. Invited gamebots are in offline_world_bot_guild (AutonomousBotGuilds).
        Seed();
        Exec("""
            CREATE TABLE offline_world_bot_guild (BotId INTEGER PRIMARY KEY, GuildID TEXT, BotName TEXT, JoinedUtc TEXT, InvitedBy TEXT);
            INSERT INTO offline_world_bots VALUES (10, 'Aldwin', 1, 'Cleric', 40, 100, 0);
            INSERT INTO offline_world_bots VALUES (11, 'Berwyn', 1, 'Armsman', 41, 200, 0);
            INSERT INTO offline_world_bots VALUES (12, 'Cedric', 1, 'Wizard', 42, 300, 0);
            INSERT INTO offline_world_bots VALUES (13, 'Oldbones', 1, 'Friar', 45, 400, 1);
            INSERT INTO offline_world_bot_guild VALUES (10, 'g1', 'Aldwin', '2026-11-01T00:00:00Z', 'Chrisplays');
            INSERT INTO offline_world_bot_guild VALUES (11, 'g1', 'Berwyn', '2026-11-01T00:00:00Z', 'Chrisplays');
            INSERT INTO offline_world_bot_guild VALUES (12, 'g1', 'Cedric', '2026-11-01T00:00:00Z', 'Chrisplays');
            INSERT INTO offline_world_bot_guild VALUES (13, 'g1', 'Oldbones', '2026-11-01T00:00:00Z', 'Chrisplays');
            INSERT INTO offline_bot_guild_members VALUES (12, 'g1');
            """);
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), DateTime.Now);
        // Chrisplays and his three active gamebots; Cedric, in both tables, once; the retired Oldbones not at all.
        Assert.That(html, Does.Match(@"<td>Knights of the Dawn</td><td><span class=""realm [a-z]+"">Albion</span></td><td class=""num"">4</td>"));
        Assert.That(html, Does.Match(@"<td>Fianna</td><td><span class=""realm [a-z]+"">Hibernia</span></td><td class=""num"">1</td>"),
            "a bot guild is counted as before");
    }

    /// <summary>
    /// Crafting 2.0 (WP9, design facet E): the Herald lists the top crafters of each order, players (per account and realm,
    /// named by the account's highest-level character there) and gamebots alike, with the title their skill gives.
    /// </summary>
    [Test] public void TheHeraldListsTheTopCraftersOfEachOrder()
    {
        Exec("""
            CREATE TABLE DOLCharacters (Name TEXT, AccountName TEXT, Realm INT, Class INT, Level INT, RealmPoints INT, GuildID TEXT);
            CREATE TABLE AccountXCrafting (AccountId TEXT, Realm INT, SerializedCraftingSkills TEXT, CraftingPrimarySkill INT);
            CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY, Name TEXT, Realm INT, ClassName TEXT, Level INT, RealmPoints INT,
                IsRetired INT, SerializedCraftingSkills TEXT);
            CREATE TABLE offline_bot_crafters (BotId INT, Realm INT, PrimaryCraft INT);
            INSERT INTO DOLCharacters VALUES ('Lowalt', 'chris', 1, 1, 10, 0, '');
            INSERT INTO DOLCharacters VALUES ('Chrisplays', 'CHRIS', 1, 1, 50, 0, '');
            INSERT INTO AccountXCrafting VALUES ('chris', 1, '11|1012;12|640;1|300', 11);
            INSERT INTO AccountXCrafting VALUES ('chris', 2, '13|500', 13);
            INSERT INTO AccountXCrafting VALUES ('basic', 1, '15|200', 15);
            INSERT INTO offline_world_bots VALUES (1, 'Needles', 1, 'Cleric', 40, 0, 0, '11|850;12|400');
            INSERT INTO offline_world_bots VALUES (2, 'Gone', 1, 'Cleric', 40, 0, 1, '11|999');
            INSERT INTO offline_world_bots VALUES (3, 'Forgeheart', 2, 'Warrior', 30, 0, 0, '2|120;1|90');
            INSERT INTO offline_world_bots VALUES (4, 'Plain', 3, 'Hero', 30, 0, 0, '');
            INSERT INTO offline_bot_crafters VALUES (3, 2, 1);
            """);
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), DateTime.Now);
        Assert.That(html, Does.Contain("Master Crafters"));
        Assert.That(html, Does.Contain("Chrisplays <span class=\"muted\">Legendary Tailor</span>"), "named by the account's highest character");
        Assert.That(html, Does.Contain("Needles <span class=\"muted\">Master Tailor</span>"));
        Assert.That(html.IndexOf("Chrisplays <span", StringComparison.Ordinal), Is.LessThan(html.IndexOf("Needles <span", StringComparison.Ordinal)), "highest skill first");
        Assert.That(html, Does.Contain("Forgeheart <span class=\"muted\">Weaponcrafter&#39;s Helper</span>"), "the crafter row's order wins");
        Assert.That(html, Does.Not.Contain("Gone <span"), "retired bots are not listed");
        Assert.That(html, Does.Not.Contain("Lowalt"));
        Assert.That(html, Does.Contain("economy.html"), "the Herald links to the economy page");

        Assert.That(HeraldPage.CraftTitle(0, "Tailor"), Is.EqualTo("Tailor's Helper"));
        Assert.That(HeraldPage.CraftTitle(999, "Fletcher"), Is.EqualTo("Grandmaster Fletcher"));
        Assert.That(HeraldPage.CraftTitle(1100, "Alchemist"), Is.EqualTo("Legendary Alchemist"), "no Legendary Grandmaster in the era");
        Assert.That(HeraldPage.CraftSkills("11|5;x;11|9;0|4;12|0;13|7"), Is.EqualTo(new[] { (11, 5), (13, 7) }));
    }

    /// <summary>
    /// Bots as people (1.2, WP6): Milestones of the Realm lists the last 40 reached arcs of gamebots still playing, newest
    /// first, with the trophy and its boss; active and dropped arcs and retired bots are not news, and names are escaped.
    /// </summary>
    [Test] public void TheHeraldListsTheMilestonesOfTheRealm()
    {
        Seed();
        Exec("""
            CREATE TABLE offline_bot_milestones (PersonKey TEXT, CharacterId INT, Realm INT, Name TEXT, ClassName TEXT, Arc TEXT, State TEXT,
                StartedUtc TEXT, ReachedUtc TEXT, LastTalkUtc TEXT, TalkPoint INT, Count INT, Trophy TEXT, Source TEXT);
            INSERT INTO offline_bot_milestones VALUES ('c:1', 1, 3, 'Brannoc', 'Champion', 'ding50', 'reached', '', '2026-11-17T10:00:00Z', '', 90, 0, '', '');
            INSERT INTO offline_bot_milestones VALUES ('c:1', 1, 3, 'Brannoc', 'Champion', 'dragon_trophy', 'reached', '', '2026-11-17T12:00:00Z', '', 50, 3,
                'Glimmering Ring', 'Cuuldurach the Glimmer King');
            INSERT INTO offline_bot_milestones VALUES ('c:4', 4, 1, 'Aldwin', 'Cleric', 'rr5', 'reached', '', '2026-11-17T11:00:00Z', '', 90, 0, '', '');
            INSERT INTO offline_bot_milestones VALUES ('c:5', 5, 1, 'Waiting', 'Cleric', 'rr5', 'active', '', '', '', 50, 0, '', '');
            INSERT INTO offline_bot_milestones VALUES ('c:2', 2, 2, 'Retiredguy', 'Warrior', 'rr5', 'reached', '', '2026-11-17T13:00:00Z', '', 90, 0, '', '');
            INSERT INTO offline_bot_milestones VALUES ('c:3', 3, 2, '<b>x</b>', 'Healer', 'ding50', 'reached', '', '2026-11-17T09:00:00Z', '', 90, 0, '', '');
            INSERT INTO offline_world_bots VALUES (4, 'Aldwin', 1, 'Cleric', 50, 600000, 0);
            """);
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), DateTime.Now);
        Assert.That(html, Does.Contain("Milestones of the Realm"));
        Assert.That(html, Does.Contain("Won Glimmering Ring from Cuuldurach the Glimmer King"));
        Assert.That(html, Does.Contain("Earned realm rank 5").And.Contain("Reached level 50"));
        Assert.That(html, Does.Not.Contain("Waiting"), "an arc still being chased is not news");
        Assert.That(html, Does.Not.Contain("Retiredguy"), "a retired bot is not news");
        Assert.That(html, Does.Contain("&lt;b&gt;x&lt;/b&gt;").And.Not.Contain("<b>x</b>"), "names are escaped");
        int trophy = html.IndexOf("Won Glimmering Ring", StringComparison.Ordinal), rank = html.IndexOf("Earned realm rank 5", StringComparison.Ordinal);
        Assert.That(trophy, Is.LessThan(rank), "newest first");
        Assert.That(HeraldPage.MilestoneText("dragon_trophy", "", ""), Is.EqualTo("Won a trophy in a great raid"));
        Assert.That(HeraldPage.MilestoneText("epic_armour", "", ""), Is.Empty, "no epic armour in 1.2 (owner decision 2)");
    }

    [Test] public void AnEmptyOrOldDatabaseStillMakesAPage()
    {
        Exec("CREATE TABLE ServerProperty (Key TEXT)");
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), DateTime.Now);
        Assert.That(html, Does.Contain("No firsts recorded yet").And.Contain("No one has earned realm points yet"));
        Assert.That(html, Does.Contain("No milestones told yet"));
    }

    [Test] public void WritesOneSelfContainedFileAndLeavesTheDatabaseUnchanged()
    {
        Seed();
        long before = new FileInfo(_database).Length;
        DateTime written = File.GetLastWriteTimeUtc(_database);
        string path = HeraldPage.Write(_database, Path.Combine(_folder, "none.sqlite3"), Path.Combine(_folder, "herald"), DateTime.Now);
        Assert.That(File.Exists(path), Is.True);
        string html = File.ReadAllText(path);
        Assert.That(html, Does.Not.Contain("http://").And.Not.Contain("https://"), "nothing is loaded from the internet");
        Assert.That(new FileInfo(_database).Length, Is.EqualTo(before));
        Assert.That(File.GetLastWriteTimeUtc(_database), Is.EqualTo(written));

        // A copy for the CI preview artifact.
        string preview = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-preview");
        Directory.CreateDirectory(preview);
        File.Copy(path, Path.Combine(preview, "herald.html"), overwrite: true);
    }
}
