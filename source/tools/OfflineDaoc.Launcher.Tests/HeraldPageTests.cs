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

    [Test] public void AnEmptyOrOldDatabaseStillMakesAPage()
    {
        Exec("CREATE TABLE ServerProperty (Key TEXT)");
        string html = HeraldPage.Build(_database, Path.Combine(_folder, "none.sqlite3"), DateTime.Now);
        Assert.That(html, Does.Contain("No firsts recorded yet").And.Contain("No one has earned realm points yet"));
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
