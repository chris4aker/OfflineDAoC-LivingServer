// Living Server 1.2: the passport's guarantees, on synthetic saves. A character is playable in one world at a time, a
// travel file works once, coin is moved and never copied, every write is backed up first, and a step that fails leaves
// the save exactly as it was.
using System.Data.SQLite;
using NUnit.Framework;

namespace OfflineDaoc.Passport.Tests;

public partial class PassportRoundTripTests
{
    static readonly string[] WorldTables = ["offline_player_reputation", "offline_lore_memory", "offline_bot_memory", "bot_profiles"];

    // ---- Every travelling table is proved by the round trip ----

    [Test]
    public void TheFixtureHasRowsInEveryTravellingTable()
    {
        foreach (TravellingTable table in PassportTables.Travelling)
            Assert.That(Value(_home, $"SELECT count(*) FROM \"{table.Table}\" WHERE \"{table.CharacterColumn}\"='{Id}'"), Is.GreaterThan(0L),
                $"{table.Table} needs a row here, so RoundTrip_ComesBackIdenticalExceptWhatWasEarned proves it travels.");
        Assert.That(Value(_home, "SELECT count(*) FROM ItemUnique"), Is.GreaterThan(0L));
    }

    [Test]
    public void RoundTrip_EveryTravellingTableArrivesAndComesBack()
    {
        Dictionary<string, List<string>> home = RealSnapshot(_home, Id);
        PassportEngine.Arrive(_host, Leave(), "friend");
        Dictionary<string, List<string>> host = RealSnapshot(_host, Id);

        foreach (TravellingTable table in PassportTables.Travelling.Where(t => t.Table != "DOLCharacters"))
            Assert.That(host[table.Table], Has.Count.EqualTo(home[table.Table].Count), table.Table);

        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(_home, back);

        Dictionary<string, List<string>> after = RealSnapshot(_home, Id);
        foreach (TravellingTable table in PassportTables.Travelling)
            Assert.That(after[table.Table], Is.EquivalentTo(home[table.Table]), table.Table);
    }

    [Test]
    public void RoundTrip_WorldTablesStayInTheirWorld()
    {
        Dictionary<string, List<string>> homeBefore = Snapshot(_home, WorldTables);

        Trip(onHost: () => Sql(_host, $"""
            INSERT INTO offline_player_reputation VALUES ('hr','2:Traveller','Traveller',2,9,'Host Hero');
            INSERT INTO offline_lore_memory VALUES ('hl','entry:jordheim:{Id}','2026-11-08','2026-11-08T00:00:00Z');
            INSERT INTO offline_bot_memory VALUES ('hm',3,'Traveller',2,1);
            INSERT INTO bot_profiles (OwnerCharacterID,Name) VALUES ('{Id}','Hostsquire');
            """));

        Assert.That(Snapshot(_home, WorldTables), Is.EqualTo(homeBefore), "Home's own world rows are untouched.");
        foreach (string table in WorldTables)
            Assert.That(Value(_host, $"SELECT count(*) FROM \"{table}\""), Is.EqualTo(1L), $"{table} stays at the host for the next visit.");
    }

    // ---- No account data beyond what is needed ----

    [Test]
    public void Passport_CarriesNoAccountData()
    {
        PassportData passport = PassportFile.Read(Leave());
        Row character = passport.Tables["DOLCharacters"].Single();

        Assert.That(character.Text("AccountName"), Is.Empty);
        Assert.That(character["AccountSlot"], Is.EqualTo(0L));
        Assert.That(passport.Manifest.FromWorld, Is.Empty, "No install folder name.");
        Assert.That(passport.Tables.Keys, Has.None.EqualTo("Account").IgnoreCase);
        IEnumerable<object?> values = passport.Tables.Values.SelectMany(rows => rows).Concat(passport.ItemUnique).Concat(passport.ItemTemplate).SelectMany(r => r.Values);
        Assert.That(values.OfType<string>(), Has.None.EqualTo("me").IgnoreCase.And.None.EqualTo("me_2").IgnoreCase.And.None.EqualTo("guid-me"));
    }

    // ---- One world at a time; one use per travel file ----

    long PlayableCopies(string world, string item) =>
        (long)Value(world, $"SELECT count(*) FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId='{Id}' AND KeyName='passport.away'")! > 0
            ? 0
            : (long)Value(world, $"SELECT count(*) FROM Inventory WHERE Inventory_ID='{item}'")!;

    [Test]
    public void AtEveryStepTheCharacterAndItsItemsArePlayableInOneWorldAtMost()
    {
        long Playable() => PlayableCopies(_home, "i1") + PlayableCopies(_host, "i1");
        Assert.That(Playable(), Is.EqualTo(1));

        string outbound = Leave();
        Assert.That(Playable(), Is.EqualTo(0), "On the road: locked at home, not yet at the host.");

        PassportEngine.Arrive(_host, outbound, "friend");
        Assert.That(Playable(), Is.EqualTo(1));

        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        Assert.That(Playable(), Is.EqualTo(0), "Locked at the host once the return passport is made.");
        Assert.That(PassportEngine.AwayTrip(_host, "Traveller"), Is.Not.Null);

        PassportEngine.ComeHome(_home, back);
        Assert.That(Playable(), Is.EqualTo(1), "Home again, while the host's copy stays locked until confirmed.");

        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        Assert.That(Value(_host, "SELECT count(*) FROM Inventory"), Is.EqualTo(0L));
        Assert.That(Playable(), Is.EqualTo(1));
    }

    [Test]
    public void AReturnPassportComesHomeOnce_WithItsPurseOnce()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        string copy = Path.Combine(_folder, "copy.odpassport");
        File.Copy(back, copy);
        PassportEngine.ComeHome(_home, back);
        long wallet = Wallet(_home, "me", 2);
        long items = (long)Value(_home, "SELECT count(*) FROM Inventory")!;
        List<string> before = DumpWorld(_home);

        Assert.That(Refusal(() => PassportEngine.ComeHome(_home, copy)), Is.EqualTo("wrong_trip"));

        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(wallet));
        Assert.That(Value(_home, "SELECT count(*) FROM Inventory"), Is.EqualTo(items));
        Assert.That(DumpWorld(_home), Is.EqualTo(before));
    }

    [Test]
    public void AnOldReturnPassportIsRefusedOnALaterTrip()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        string first = PassportEngine.Depart(_host, "Traveller", null, _out);
        string copy = Path.Combine(_folder, "first.odpassport");
        File.Copy(first, copy);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(first).Manifest.TripId);
        PassportEngine.ComeHome(_home, first);

        Leave();
        List<string> before = DumpWorld(_home);

        Assert.That(Refusal(() => PassportEngine.ComeHome(_home, copy)), Is.EqualTo("wrong_trip"));
        Assert.That(DumpWorld(_home), Is.EqualTo(before), "Still locked on the second trip, nothing changed.");
    }

    [Test]
    public void AnOutboundPassportIsUsedOnceAtTheHost_EvenAfterTheTrip()
    {
        string outbound = Leave();
        string copy = Path.Combine(_folder, "outbound.odpassport");
        File.Copy(outbound, copy);
        PassportEngine.Arrive(_host, outbound, "friend");
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        List<string> host = DumpWorld(_host);

        Assert.That(Refusal(() => PassportEngine.Arrive(_host, copy, "friend")), Is.EqualTo("replayed"));
        Assert.That(DumpWorld(_host), Is.EqualTo(host));
    }

    [Test]
    public void AnOlderOutboundPassportIsRefusedAfterALaterTrip()
    {
        string first = Leave();
        string copy = Path.Combine(_folder, "first-outbound.odpassport");
        File.Copy(first, copy);
        PassportEngine.Arrive(_host, first, "friend");
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(_home, back);
        Trip();  // a second trip, so passport_visits now names that one
        List<string> host = DumpWorld(_host);

        Assert.That(Refusal(() => PassportEngine.Arrive(_host, copy, "friend")), Is.EqualTo("replayed"));
        Assert.That(DumpWorld(_host), Is.EqualTo(host), "The character is not in two worlds.");
    }

    [Test]
    public void ACharacterThatIsAwayCannotLeaveAgain()
    {
        Leave();
        List<string> before = DumpWorld(_home);

        Assert.That(Refusal(() => Leave()), Is.EqualTo("already_away"));
        Assert.That(DumpWorld(_home), Is.EqualTo(before));
    }

    // ---- Rollback: a failed step leaves the save as it was ----

    static void FailOn(string runtime, string table, string when) =>
        Sql(runtime, $"CREATE TRIGGER passport_test_failure BEFORE {when} ON \"{table}\" BEGIN SELECT RAISE(ABORT, 'test failure'); END;");

    static void StopFailing(string runtime) => Sql(runtime, "DROP TRIGGER passport_test_failure");

    [Test]
    public void Leave_AFailureChangesNothingAndLeavesNoFile()
    {
        FailOn(_home, "DOLCharactersXCustomParam", "INSERT");
        List<string> before = DumpWorld(_home);

        Assert.Throws<SQLiteException>(() => Leave());

        Assert.That(DumpWorld(_home), Is.EqualTo(before));
        Assert.That(Directory.Exists(_out) ? Directory.GetFiles(_out) : [], Is.Empty, "No passport for a character that never left.");
        StopFailing(_home);
        Assert.That(PassportFile.Read(Leave()).Manifest.Name, Is.EqualTo("Traveller"));
    }

    [Test]
    public void Arrive_ALateFailureLeavesTheHostExactlyAsItWas()
    {
        string passport = Leave();
        FailOn(_host, "AccountXMoney", "INSERT");  // after every character row is in
        List<string> before = DumpWorld(_host);

        Assert.Throws<SQLiteException>(() => PassportEngine.Arrive(_host, passport, "friend"));

        Assert.That(DumpWorld(_host), Is.EqualTo(before), "Not one row, not even the passport_visits table.");
        StopFailing(_host);
        Assert.That(PassportEngine.Arrive(_host, passport, "friend").Name, Is.EqualTo("Traveller"), "The same passport works once the host is fixed.");
    }

    [Test]
    public void Arrive_AnIdClashIsRefusedAndChangesNothing()
    {
        // A friend's world made from a copy of this save already has the character.
        Sql(_host, $"INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm) VALUES ('{Id}','friend',300,'Copycat',3)");
        string passport = Leave();
        List<string> before = DumpWorld(_host);

        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend")), Is.EqualTo("already_here"));
        Assert.That(DumpWorld(_host), Is.EqualTo(before));
    }

    [Test]
    public void Depart_AFailureLeavesTheVisitorPlayableAndTheWalletWhole()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        FailOn(_host, "AccountXMoney", "UPDATE");
        List<string> before = DumpWorld(_host);

        Assert.Throws<SQLiteException>(() => PassportEngine.Depart(_host, "Traveller", null, _out));

        Assert.That(DumpWorld(_host), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(_out, "*return*"), Is.Empty);
    }

    [Test]
    public void ComeHome_ALateFailureLeavesHomeLockedAndUnchanged()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        Sql(_host, $"UPDATE DOLCharacters SET Experience=999 WHERE DOLCharacters_ID='{Id}'");
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        FailOn(_home, "AccountXMoney", "UPDATE");  // after the frozen rows are replaced
        List<string> before = DumpWorld(_home);

        Assert.Throws<SQLiteException>(() => PassportEngine.ComeHome(_home, back));

        Assert.That(DumpWorld(_home), Is.EqualTo(before));
        Assert.That(PassportEngine.AwayTrip(_home, "Traveller"), Is.Not.Null);
        StopFailing(_home);
        PassportEngine.ComeHome(_home, back);
        Assert.That(Value(_home, "SELECT Experience FROM DOLCharacters"), Is.EqualTo(999L));
    }

    // ---- Backups ----

    static int Backups(string runtime) =>
        Directory.Exists(Path.Combine(runtime, "passport-backups")) ? Directory.GetDirectories(Path.Combine(runtime, "passport-backups")).Length : 0;

    [Test]
    public void EveryStepThatWritesBacksUpFirst()
    {
        string outbound = Leave();
        Assert.That(Backups(_home), Is.EqualTo(1));
        string backup = Directory.GetFiles(Path.Combine(_home, "passport-backups"), "*.db", SearchOption.AllDirectories).Single();
        Assert.That(Value(backup, "SELECT count(*) FROM DOLCharactersXCustomParam WHERE KeyName='passport.away'", file: true), Is.EqualTo(0L),
            "The backup is the save as it was before the step.");
        Assert.That(Value(backup, "PRAGMA journal_mode", file: true), Is.EqualTo("delete"), "A standalone file, no -wal to carry.");

        PassportEngine.Arrive(_host, outbound, "friend");
        Assert.That(Backups(_host), Is.EqualTo(1));
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        Assert.That(Backups(_host), Is.EqualTo(2));
        PassportEngine.Depart(_host, "Traveller", null, _out);
        Assert.That(Backups(_host), Is.EqualTo(2), "Remaking the same return passport writes nothing.");
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        Assert.That(Backups(_host), Is.EqualTo(3));
        PassportEngine.ComeHome(_home, back);
        Assert.That(Backups(_home), Is.EqualTo(2));

        PassportEngine.Arrive(_host, Leave(), "friend");
        PassportEngine.Recall(_home, "Traveller");
        Assert.That(Backups(_home), Is.EqualTo(4));
    }

    [Test]
    public void ARefusalTakesNoBackup()
    {
        Assert.That(Refusal(() => PassportEngine.Recall(_home, "Traveller")), Is.EqualTo("not_away"));
        string passport = Leave();
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "nobody")), Is.EqualTo("no_account"));
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend", "bad name")), Is.EqualTo("bad_name"));

        Assert.That(Backups(_home), Is.EqualTo(1));
        Assert.That(Backups(_host), Is.EqualTo(0));
    }

    [Test]
    public void TheLiveDeskBacksUpBesideItsDataFolder()
    {
        PassportWorld live = PassportWorld.Live(Path.Combine(_host, "data", "opendaoc.sqlite3.db"), "Bob's world");
        PassportEngine.Arrive(live, Leave(), "friend");

        Assert.That(Backups(_host), Is.EqualTo(1));
        Assert.That(live.BackupRoot, Is.EqualTo(Path.Combine(_host, "passport-backups")));
    }

    // ---- Names and classes ----

    [TestCase("wanderer")]
    [TestCase("Wa")]
    [TestCase("Wander3r")]
    [TestCase("Wan Derer")]
    [TestCase("Abcdefghijklmnopqrstu")]
    public void AVisitingNameFollowsTheGamesRule(string name)
    {
        string passport = Leave();
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend", name)), Is.EqualTo("bad_name"));
        Assert.That(OnHostCount(), Is.EqualTo(0L));
    }

    [Test]
    public void ANameUsedByABotOrACompanionIsTaken()
    {
        Sql(_host, "INSERT INTO offline_world_bots (Name,Realm) VALUES ('Traveller',1); INSERT INTO bot_profiles (OwnerCharacterID,Name) VALUES ('x','Wanderer');");
        string passport = Leave();

        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend")), Is.EqualTo("name_taken"));
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend", "Wanderer")), Is.EqualTo("name_taken"));
        Assert.That(PassportEngine.Arrive(_host, passport, "friend", "Rover").Name, Is.EqualTo("Rover"));
    }

    [TestCase(63, 2)]
    [TestCase(16, 3)]
    public void ASluaghbinderVisitsOnlyAWorldWithTheClassOn(int characterClass, int realm)
    {
        Sql(_home, $"UPDATE DOLCharacters SET Class={characterClass}, Realm={realm}, AccountSlot={realm * 100}");
        Sql(_host, "INSERT INTO ServerProperty VALUES ('enable_sluaghbinder','classes','enable_sluaghbinder','False')");
        string passport = Leave();

        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend")), Is.EqualTo("class"));

        Sql(_host, "UPDATE ServerProperty SET Value='True'");
        Assert.That(PassportEngine.Arrive(_host, passport, "friend").Name, Is.EqualTo("Traveller"));
    }

    [Test]
    public void AnAlbionAcolyteIsNotASluaghbinder()
    {
        Sql(_home, "UPDATE DOLCharacters SET Class=16");
        Sql(_host, "INSERT INTO ServerProperty VALUES ('enable_sluaghbinder','classes','enable_sluaghbinder','False')");

        Assert.That(PassportEngine.Arrive(_host, Leave(), "friend").Name, Is.EqualTo("Traveller"));
    }

    // ---- The host's switch ----

    [Test]
    public void TheHostCanTurnVisitorsOffAndOnAgain_WithABackup()
    {
        Assert.That(PassportEngine.VisitorsWelcome(_host), Is.True, "On by default.");

        PassportEngine.SetVisitorsWelcome(_host, false);
        Assert.That(PassportEngine.VisitorsWelcome(_host), Is.False);
        Assert.That(Value(_host, "SELECT Category FROM ServerProperty WHERE \"Key\"='passport_visitors'"), Is.EqualTo("passport"));
        Assert.That(Backups(_host), Is.EqualTo(1));

        PassportEngine.SetVisitorsWelcome(_host, false);
        Assert.That(Backups(_host), Is.EqualTo(1), "No change, no write.");
        PassportEngine.SetVisitorsWelcome(_host, true);
        Assert.That(PassportEngine.VisitorsWelcome(_host), Is.True);
        Assert.That(Value(_host, "SELECT count(*) FROM ServerProperty"), Is.EqualTo(1L));
    }

    long OnHostCount() => (long)Value(_host, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'")!;

    // ---- Helpers ----

    /// <summary>The whole save as text: every table's schema, then every row of every table in rowid order.</summary>
    static List<string> DumpWorld(string runtime)
    {
        var lines = Dump(runtime, "SELECT type||' '||name||' '||COALESCE(sql,'') FROM sqlite_master ORDER BY type, name", raw: true);
        var tables = new List<string>();
        using (SQLiteConnection connection = Open(runtime))
        using (SQLiteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }
        foreach (string table in tables)
            lines.AddRange(Dump(runtime, $"SELECT * FROM \"{table}\" ORDER BY rowid", raw: true).Select(row => table + ": " + row));
        return lines;
    }

    static object? Value(string path, string sql, bool file)
    {
        if (!file)
            return Value(path, sql);
        using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false, ReadOnly = true }.ConnectionString);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
