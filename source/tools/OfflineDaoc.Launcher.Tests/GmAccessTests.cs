using System.Data.SQLite;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Living Server 1.2, play with friends: Make Me GM never reaches a friend's account (co-op audit 2026-11-03 §3).
public sealed class GmAccessTests
{
    private string _database = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _database = Path.Combine(Path.GetTempPath(), "odc-gm-" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Account (Name TEXT PRIMARY KEY, PrivLevel INTEGER NOT NULL);" +
                              "INSERT INTO Account VALUES ('offline', 1), ('Aaron', 1), ('Bea', 1);";
        command.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        if (File.Exists(_database))
            File.Delete(_database);
    }

    private SQLiteConnection Open()
    {
        var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False");
        connection.Open();
        return connection;
    }

    private Dictionary<string, long> Levels()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, PrivLevel FROM Account";
        using var reader = command.ExecuteReader();
        var levels = new Dictionary<string, long>();
        while (reader.Read())
            levels[reader.GetString(0)] = reader.GetInt64(1);
        return levels;
    }

    [Test]
    public void MakeMeGmChangesOnlyTheHostsOwnAccount()
    {
        using (var connection = Open())
            GmAccess.Persist(connection, "offline", enabled: true);

        Assert.That(Levels(), Is.EquivalentTo(new Dictionary<string, long> { ["offline"] = 2, ["Aaron"] = 1, ["Bea"] = 1 }));

        using (var connection = Open())
            GmAccess.Persist(connection, "offline", enabled: false);

        Assert.That(Levels().Values, Is.All.EqualTo(1));
    }

    [Test]
    public void NoAccountNameChangesNothing()
    {
        using var connection = Open();
        Assert.Throws<ArgumentException>(() => GmAccess.Persist(connection, "", enabled: true));
        Assert.That(Levels().Values, Is.All.EqualTo(1));
    }
}
