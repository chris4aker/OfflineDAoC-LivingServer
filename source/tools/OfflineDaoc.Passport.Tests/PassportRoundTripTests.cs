using System.Data.SQLite;
using NUnit.Framework;

namespace OfflineDaoc.Passport.Tests;

[TestFixture]
public class PassportRoundTripTests
{
    const string Id = "11111111-1111-1111-1111-111111111111";
    const string Schema = """
        CREATE TABLE Account (Name TEXT PRIMARY KEY COLLATE NOCASE, Realm INT NOT NULL DEFAULT 0, Account_ID TEXT);
        CREATE TABLE AccountXMoney (AccountId TEXT NOT NULL COLLATE NOCASE, Realm INT NOT NULL DEFAULT 0, Copper INT NOT NULL DEFAULT 0, Silver INT NOT NULL DEFAULT 0, Gold INT NOT NULL DEFAULT 0, Platinum INT NOT NULL DEFAULT 0, Mithril INT NOT NULL DEFAULT 0, AccountxMoney_ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        CREATE TABLE AccountXCrafting (AccountId TEXT NOT NULL COLLATE NOCASE, Realm INT NOT NULL DEFAULT 0, SerializedCraftingSkills TEXT, CraftingPrimarySkill INT NOT NULL DEFAULT 0, AccountxCrafting_ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT PRIMARY KEY, AccountName TEXT, AccountSlot INT, Name TEXT COLLATE NOCASE, Realm INT, Class INT, Level INT, Region INT, Xpos INT, Ypos INT, Zpos INT, Direction INT, BindRegion INT, BindXpos INT, BindYpos INT, BindZpos INT, BindHeading INT, BindHouseRegion INT DEFAULT 0, BindHouseXpos INT DEFAULT 0, BindHouseYpos INT DEFAULT 0, BindHouseZpos INT DEFAULT 0, BindHouseHeading INT DEFAULT 0, GuildID TEXT, GuildRank INT DEFAULT 9, GuildNote TEXT, Experience BIGINT DEFAULT 0, CreationDate DATETIME, LastTimeRowUpdated DATETIME);
        CREATE UNIQUE INDEX U_Name ON DOLCharacters (Name);
        CREATE TABLE DOLCharactersXCustomParam (DOLCharactersObjectId TEXT NOT NULL, KeyName TEXT NOT NULL, Value TEXT, CustomParamID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        CREATE TABLE CharacterXDataQuest (ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Character_ID TEXT NOT NULL, DataQuestID INT, Step INT);
        CREATE TABLE Quest (Quest_ID TEXT PRIMARY KEY, Character_ID TEXT NOT NULL, Name TEXT);
        CREATE TABLE Task (Character_ID TEXT PRIMARY KEY, TasksDone INT);
        CREATE TABLE CharacterXMasterLevel (CharacterXMasterLevel_ID TEXT PRIMARY KEY, Character_ID TEXT);
        CREATE TABLE CharacterXOneTimeDrop (CharacterXOneTimeDrop_ID TEXT PRIMARY KEY, CharacterID TEXT);
        CREATE TABLE FactionAggroLevel (FactionAggroLevel_ID TEXT PRIMARY KEY, CharacterID TEXT, FactionID INT, AggroLevel INT);
        CREATE TABLE PlayerXEffect (PlayerXEffect_ID TEXT PRIMARY KEY, ChardID TEXT, Var2 DOUBLE);
        CREATE TABLE TimeXLevel (TimeXLevel_ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Character_ID TEXT, Character_Name TEXT, Character_Level INT);
        CREATE TABLE offline_artifact_progress (offline_artifact_progress_ID TEXT PRIMARY KEY, CharacterID TEXT, ArtifactID TEXT, Experience BIGINT);
        CREATE TABLE Inventory (Inventory_ID TEXT PRIMARY KEY, OwnerID TEXT, OwnerLot INT DEFAULT 0, ITemplate_Id TEXT, UTemplate_Id TEXT, SlotPosition INT, Count INT DEFAULT 1);
        CREATE TABLE ItemUnique (Id_nb TEXT PRIMARY KEY, Name TEXT, ItemUnique_ID TEXT);
        CREATE TABLE ItemTemplate (Id_nb TEXT PRIMARY KEY, Name TEXT, ItemTemplate_ID TEXT);
        CREATE TABLE Guild (GuildID TEXT PRIMARY KEY, GuildName TEXT);
        CREATE TABLE Regions (RegionID INT PRIMARY KEY);
        CREATE TABLE DBHouse (HouseNumber INT PRIMARY KEY, OwnerID TEXT);
        INSERT INTO Regions VALUES (1),(100);
        """;

    string _folder = "", _home = "", _host = "", _out = "";

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "passport-test-" + Guid.NewGuid().ToString("N"));
        _home = NewWorld("home");
        _host = NewWorld("host");
        _out = Path.Combine(_folder, "out");

        Sql(_home, $"""
            INSERT INTO Account VALUES ('me',2,'guid-me');
            INSERT INTO AccountXMoney (AccountId,Realm,Copper,Silver,Gold,Platinum) VALUES ('me',2,5,10,200,3);
            INSERT INTO AccountXCrafting (AccountId,Realm,SerializedCraftingSkills,CraftingPrimarySkill) VALUES ('me',2,'1|500;2|10',1);
            INSERT INTO Guild VALUES ('g1','Home Guild');
            INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm,Class,Level,Region,Xpos,Ypos,Zpos,Direction,BindRegion,BindXpos,BindYpos,BindZpos,BindHeading,BindHouseRegion,BindHouseXpos,GuildID,GuildRank,GuildNote,CreationDate,LastTimeRowUpdated)
                VALUES ('{Id}','me',201,'Traveller',2,64,50,100,1,2,3,4,1,10,20,30,40,7,70,'g1',2,'note','2026-10-27 19:41:41.1071071Z','2026-11-01 01:56:48.7309087Z');
            INSERT INTO DOLCharactersXCustomParam (DOLCharactersObjectId,KeyName,Value) VALUES ('{Id}','title','hero'),('{Id}','RealmExchange.Proceeds.2','500|0|0');
            INSERT INTO CharacterXDataQuest (Character_ID,DataQuestID,Step) VALUES ('{Id}',9,2);
            INSERT INTO Quest VALUES ('q1','{Id}','A quest');
            INSERT INTO Task VALUES ('{Id}',3);
            INSERT INTO FactionAggroLevel VALUES ('f1','{Id}',5,20);
            INSERT INTO PlayerXEffect VALUES ('e1','{Id}',1.5);
            INSERT INTO TimeXLevel (Character_ID,Character_Name,Character_Level) VALUES ('{Id}','Traveller',50);
            INSERT INTO offline_artifact_progress VALUES ('a1','{Id}','1014',76000000000);
            INSERT INTO ItemTemplate VALUES ('shared_sword','Sword','t1'),('home_only_shield','Shield','t2');
            INSERT INTO ItemUnique VALUES ('stone#1','Stone','u1'),('listed#1','Listed','u2');
            INSERT INTO Inventory (Inventory_ID,OwnerID,ITemplate_Id,SlotPosition) VALUES ('i1','{Id}','shared_sword',10),('i2','{Id}','home_only_shield',11);
            INSERT INTO Inventory (Inventory_ID,OwnerID,UTemplate_Id,SlotPosition) VALUES ('i3','{Id}','stone#1',79);
            INSERT INTO Inventory (Inventory_ID,OwnerID,ITemplate_Id,SlotPosition) VALUES ('vault','{Id}','shared_sword',110);
            INSERT INTO Inventory (Inventory_ID,OwnerID,ITemplate_Id,SlotPosition) VALUES ('housed','{Id}','shared_sword',1000);
            INSERT INTO Inventory (Inventory_ID,OwnerID,OwnerLot,UTemplate_Id,SlotPosition) VALUES ('listed','{Id}',7,'listed#1',2000);
            INSERT INTO Inventory (Inventory_ID,OwnerID,ITemplate_Id,SlotPosition) VALUES ('account','me_2','shared_sword',2500);
            """);

        Sql(_host, """
            INSERT INTO Account VALUES ('friend',0,'guid-friend');
            INSERT INTO ItemTemplate VALUES ('shared_sword','Host Sword','t9');
            """);
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        Directory.Delete(_folder, true);
    }

    [Test]
    public void Leave_LocksHomeAndTakesThePurse()
    {
        string passport = Leave(purse: 100_000);

        Assert.That(Value(_home, $"SELECT Value FROM DOLCharactersXCustomParam WHERE KeyName='passport.away'"), Is.EqualTo(PassportFile.Read(passport).Manifest.TripId));
        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(3L * 10_000_000 + 200 * 10_000 + 10 * 100 + 5 - 100_000));
        Assert.That(Value(_home, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'"), Is.EqualTo(1L), "Home rows stay until the character returns.");
        Assert.That(Directory.GetDirectories(Path.Combine(_home, "passport-backups")), Has.Length.EqualTo(1));
    }

    [Test]
    public void Passport_LeavesWorldBoundThingsBehind()
    {
        PassportData passport = PassportFile.Read(Leave());
        Row character = passport.Tables["DOLCharacters"].Single();

        Assert.That(character.Text("GuildID"), Is.Empty);
        Assert.That(character["BindHouseRegion"], Is.EqualTo(0L));
        Assert.That(passport.Tables["Inventory"].Select(i => i.Text("Inventory_ID")), Is.EquivalentTo(new[] { "i1", "i2", "i3", "vault" }));
        Assert.That(passport.Tables["DOLCharactersXCustomParam"].Select(p => p.Text("KeyName")), Is.EqualTo(new[] { "title" }));
        Assert.That(passport.ItemUnique.Select(u => u.Text("Id_nb")), Is.EqualTo(new[] { "stone#1" }));
        Assert.That(passport.Tables["offline_artifact_progress"], Has.Count.EqualTo(1));
    }

    [Test]
    public void Arrive_PutsTheVisitorOnTheHostAccount()
    {
        string passport = Leave();
        ArrivalReport report = PassportEngine.Arrive(_host, passport, "friend");

        Assert.That(report.AccountSlot, Is.EqualTo(200));
        Assert.That(Value(_host, $"SELECT AccountName FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'"), Is.EqualTo("friend"));
        Assert.That(Value(_host, "SELECT Realm FROM Account WHERE Name='friend'"), Is.EqualTo(2L));
        Assert.That(Wallet(_host, "friend", 2), Is.EqualTo(PassportFile.Read(passport).Manifest.PurseCopper));
        Assert.That(Value(_host, "SELECT SerializedCraftingSkills FROM AccountXCrafting WHERE AccountId='friend'"), Is.EqualTo("1|500;2|10"));
    }

    [Test]
    public void Arrive_UsesTheHostDefinitionOrCarriesItsOwn()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");

        Assert.That(Value(_host, "SELECT ITemplate_Id FROM Inventory WHERE Inventory_ID='i1'"), Is.EqualTo("shared_sword"));
        Assert.That(Value(_host, "SELECT Name FROM ItemTemplate WHERE Id_nb='shared_sword'"), Is.EqualTo("Host Sword"));
        string converted = (string)Value(_host, "SELECT UTemplate_Id FROM Inventory WHERE Inventory_ID='i2'")!;
        Assert.That(converted, Does.StartWith("passport#home_only_shield#"));
        Assert.That(Value(_host, $"SELECT Name FROM ItemUnique WHERE Id_nb='{converted}'"), Is.EqualTo("Shield"));
        Assert.That(Value(_host, "SELECT count(*) FROM ItemUnique WHERE Id_nb='stone#1'"), Is.EqualTo(1L));
    }

    [Test]
    public void RoundTrip_ComesBackIdenticalExceptWhatWasEarned()
    {
        var tables = PassportTables.Travelling.Select(t => t.Table).Append("ItemUnique").ToArray();
        Dictionary<string, List<string>> before = Snapshot(_home, tables);
        long wallet = Wallet(_home, "me", 2);

        Trip();

        Dictionary<string, List<string>> after = Snapshot(_home, tables);
        foreach (string table in tables)
            Assert.That(after[table], Is.EquivalentTo(before[table]), table);
        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(wallet));
        Assert.That(Value(_home, "SELECT count(*) FROM DOLCharactersXCustomParam WHERE KeyName LIKE 'passport.%'"), Is.EqualTo(0L));
        Assert.That(Value(_host, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'"), Is.EqualTo(0L));
        Assert.That(Value(_host, "SELECT count(*) FROM Inventory"), Is.EqualTo(0L));
        Assert.That(Value(_host, "SELECT count(*) FROM ItemUnique"), Is.EqualTo(0L), "Carried uniques leave the host with the visitor.");
    }

    [Test]
    public void RoundTrip_BringsBackWhatWasEarned()
    {
        Trip(onHost: () => Sql(_host, $"""
            UPDATE DOLCharacters SET Experience=999 WHERE DOLCharacters_ID='{Id}';
            UPDATE AccountXCrafting SET SerializedCraftingSkills='1|400;2|90;3|7';
            INSERT INTO ItemUnique VALUES ('loot#1','Loot','u9');
            INSERT INTO Inventory (Inventory_ID,OwnerID,UTemplate_Id,SlotPosition) VALUES ('loot','{Id}','loot#1',40);
            INSERT INTO Quest VALUES ('q2','{Id}','Host quest');
            """));

        Assert.That(Value(_home, $"SELECT Experience FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'"), Is.EqualTo(999L));
        Assert.That(Value(_home, "SELECT Name FROM ItemUnique WHERE Id_nb='loot#1'"), Is.EqualTo("Loot"));
        Assert.That(Value(_home, "SELECT count(*) FROM Quest"), Is.EqualTo(2L));
        Assert.That(Value(_home, "SELECT SerializedCraftingSkills FROM AccountXCrafting WHERE AccountId='me'"), Is.EqualTo("1|500;2|90;3|7"));
    }

    [Test]
    public void RoundTrip_PutsHomeOnlyValuesBack()
    {
        Trip();

        Assert.That(Value(_home, "SELECT GuildID FROM DOLCharacters"), Is.EqualTo("g1"));
        Assert.That(Value(_home, "SELECT GuildRank FROM DOLCharacters"), Is.EqualTo(2L));
        Assert.That(Value(_home, "SELECT BindHouseRegion FROM DOLCharacters"), Is.EqualTo(7L));
        Assert.That(Value(_home, "SELECT AccountSlot FROM DOLCharacters"), Is.EqualTo(201L));
        Assert.That(Value(_home, "SELECT count(*) FROM Inventory WHERE Inventory_ID IN ('housed','listed','account')"), Is.EqualTo(3L));
        Assert.That(Value(_home, "SELECT Value FROM DOLCharactersXCustomParam WHERE KeyName='RealmExchange.Proceeds.2'"), Is.EqualTo("500|0|0"));
    }

    [Test]
    public void RoundTrip_VisitingNameStaysOnTheHost()
    {
        Sql(_host, $"INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm) VALUES ('x','friend',300,'traveller',3)");
        string passport = Leave();

        Assert.That(Assert.Throws<PassportException>(() => PassportEngine.Arrive(_host, passport, "friend"))!.Code, Is.EqualTo("name_taken"));
        Assert.That(PassportEngine.Arrive(_host, passport, "friend", "Wanderer").Name, Is.EqualTo("Wanderer"));

        string back = PassportEngine.Depart(_host, "Wanderer", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Wanderer", PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(_home, back);

        Assert.That(Value(_home, "SELECT Name FROM DOLCharacters"), Is.EqualTo("Traveller"));
        Assert.That(Value(_home, "SELECT Character_Name FROM TimeXLevel"), Is.EqualTo("Traveller"));
    }

    [Test]
    public void Purse_IsConservedAcrossTheTrip()
    {
        long total = Wallet(_home, "me", 2);
        string passport = Leave(purse: 50_000);
        PassportEngine.Arrive(_host, passport, "friend");
        Assert.That(Wallet(_home, "me", 2) + Wallet(_host, "friend", 2), Is.EqualTo(total));

        string back = PassportEngine.Depart(_host, "Traveller", 20_000, _out);
        Assert.That(Wallet(_home, "me", 2) + Wallet(_host, "friend", 2) + PassportFile.Read(back).Manifest.PurseCopper, Is.EqualTo(total));

        PassportEngine.ComeHome(_home, back);
        Assert.That(Wallet(_home, "me", 2) + Wallet(_host, "friend", 2), Is.EqualTo(total));
    }

    [Test]
    public void Recall_UnlocksWithThePurseAndRefusesTheLateReturn()
    {
        long wallet = Wallet(_home, "me", 2);
        string passport = Leave();
        PassportEngine.Arrive(_host, passport, "friend");
        string late = PassportEngine.Depart(_host, "Traveller", null, _out);

        PassportEngine.Recall(_home, "Traveller");

        Assert.That(Wallet(_home, "me", 2), Is.EqualTo(wallet));
        Assert.That(Value(_home, "SELECT count(*) FROM DOLCharactersXCustomParam WHERE KeyName='passport.away'"), Is.EqualTo(0L));
        Assert.That(Assert.Throws<PassportException>(() => PassportEngine.ComeHome(_home, late))!.Code, Is.EqualTo("wrong_trip"));
    }

    [Test]
    public void Passport_CannotBeUsedTwice()
    {
        string outbound = Leave();
        PassportEngine.Arrive(_host, outbound, "friend");
        Assert.That(Assert.Throws<PassportException>(() => PassportEngine.Arrive(_host, outbound, "friend"))!.Code, Is.EqualTo("already_here"));

        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        Assert.That(Assert.Throws<PassportException>(() => PassportEngine.Arrive(_host, outbound, "friend"))!.Code, Is.EqualTo("replayed"));

        PassportEngine.ComeHome(_home, back);
        Assert.That(Assert.Throws<PassportException>(() => PassportEngine.ComeHome(_home, back))!.Code, Is.EqualTo("wrong_trip"));
    }

    [Test]
    public void Depart_RemakesTheSamePassportUntilConfirmed()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        Manifest first = PassportFile.Read(PassportEngine.Depart(_host, "Traveller", null, _out)).Manifest;
        Manifest again = PassportFile.Read(PassportEngine.Depart(_host, "Traveller", null, _out)).Manifest;

        Assert.That(again.TripId, Is.EqualTo(first.TripId));
        Assert.That(again.PurseCopper, Is.EqualTo(first.PurseCopper));
        Assert.That(Wallet(_host, "friend", 2), Is.EqualTo(0));
    }

    [Test]
    public void Depart_RefusesWhatIsStillTiedToTheHost()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");

        Sql(_host, $"INSERT INTO Inventory (Inventory_ID,OwnerID,OwnerLot,ITemplate_Id,SlotPosition) VALUES ('sale','{Id}',7,'shared_sword',2000)");
        Assert.That(Refusal(() => PassportEngine.Depart(_host, "Traveller", null, _out)), Is.EqualTo("market_items"));
        Sql(_host, "DELETE FROM Inventory WHERE Inventory_ID='sale'");

        Sql(_host, $"INSERT INTO DOLCharactersXCustomParam (DOLCharactersObjectId,KeyName,Value) VALUES ('{Id}','RealmExchange.Proceeds.2','5|0|0')");
        Assert.That(Refusal(() => PassportEngine.Depart(_host, "Traveller", null, _out)), Is.EqualTo("proceeds"));
        Sql(_host, "DELETE FROM DOLCharactersXCustomParam");

        Sql(_host, $"INSERT INTO DBHouse VALUES (1,'{Id}')");
        Assert.That(Refusal(() => PassportEngine.Depart(_host, "Traveller", null, _out)), Is.EqualTo("house"));
    }

    [Test]
    public void Depart_WithAnotherCharacterHereLeavesTheWalletUnlessAsked()
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        Sql(_host, "INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm) VALUES ('x','friend',201,'Hostalt',2)");

        Assert.That(PassportFile.Read(PassportEngine.Depart(_host, "Traveller", null, _out)).Manifest.PurseCopper, Is.EqualTo(0));
    }

    [Test]
    public void Refusals_ChangeNothing()
    {
        Assert.That(Refusal(() => PassportEngine.Leave(_home, "Nobody", null, "x", _out)), Is.EqualTo("no_character"));
        Assert.That(Refusal(() => PassportEngine.Leave(_home, "Traveller", long.MaxValue, "x", _out)), Is.EqualTo("purse"));
        Assert.That(Refusal(() => PassportEngine.Recall(_home, "Traveller")), Is.EqualTo("not_away"));
        Assert.That(Value(_home, "SELECT count(*) FROM DOLCharactersXCustomParam WHERE KeyName LIKE 'passport.%'"), Is.EqualTo(0L));

        string passport = Leave();
        Assert.That(Refusal(() => PassportEngine.Leave(_home, "Traveller", null, "x", _out)), Is.EqualTo("already_away"));
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "nobody")), Is.EqualTo("no_account"));
        Assert.That(Refusal(() => PassportEngine.ComeHome(_home, passport)), Is.EqualTo("wrong_direction"));
    }

    [Test]
    public void Arrive_FullAccountIsRefused()
    {
        for (int i = 0; i < 10; i++)
            Sql(_host, $"INSERT INTO DOLCharacters (DOLCharacters_ID,AccountName,AccountSlot,Name,Realm) VALUES ('x{i}','friend',{200 + i},'Alt{i}',2)");

        string passport = Leave();
        Assert.That(Refusal(() => PassportEngine.Arrive(_host, passport, "friend")), Is.EqualTo("no_slot"));
        Assert.That(Value(_host, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{Id}'"), Is.EqualTo(0L));
    }

    [Test]
    public void Arrive_UnknownRegionStartsAtTheBindPoint()
    {
        Sql(_host, "DELETE FROM Regions WHERE RegionID=100");
        ArrivalReport report = PassportEngine.Arrive(_host, Leave(), "friend");

        Assert.That(Value(_host, "SELECT Region FROM DOLCharacters"), Is.EqualTo(1L));
        Assert.That(Value(_host, "SELECT Xpos FROM DOLCharacters"), Is.EqualTo(10L));
        Assert.That(report.Notes, Has.Count.EqualTo(1));
    }

    [Test]
    public void Arrive_RestoresTheGuildFromAnEarlierVisit()
    {
        Sql(_host, "INSERT INTO Guild VALUES ('hg','Host Guild')");
        PassportEngine.Arrive(_host, Leave(), "friend");
        Sql(_host, "UPDATE DOLCharacters SET GuildID='hg',GuildRank=3");
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(_home, back);

        PassportEngine.Arrive(_host, Leave(), "friend");

        Assert.That(Value(_host, "SELECT GuildID FROM DOLCharacters"), Is.EqualTo("hg"));
        Assert.That(Value(_host, "SELECT GuildRank FROM DOLCharacters"), Is.EqualTo(3L));
    }

    [Test]
    public void DamagedPassport_IsRefused()
    {
        string passport = Leave();
        byte[] data = File.ReadAllBytes(passport);
        data[data.Length / 2] ^= 0xFF;
        File.WriteAllBytes(passport, data);

        Assert.That(Refusal(() => PassportFile.Read(passport)), Is.EqualTo("damaged"));
    }

    [Test]
    public void RunningServer_IsRefused()
    {
        string dll = Path.Combine(_home, "server", "lib", "GameServer.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        using var running = new FileStream(dll, FileMode.Create, FileAccess.Write, FileShare.Read);

        Assert.That(Refusal(() => PassportEngine.Leave(_home, "Traveller", null, "x", _out)), Is.EqualTo("server_running"));
    }

    [TestCase("1|500;2|10", "1|400;3|7", "1|500;2|10;3|7")]
    [TestCase("", "4|1", "4|1")]
    public void MergeSkills_KeepsTheHigherValue(string first, string second, string expected) =>
        Assert.That(PassportEngine.MergeSkills(first, second), Is.EqualTo(expected));

    /// <summary>A real round trip between copies of two played saves, for a manual run.</summary>
    [Test]
    public void RealSaves_RoundTrip()
    {
        string? homeSave = Environment.GetEnvironmentVariable("PASSPORT_HOME_SAVE"), hostSave = Environment.GetEnvironmentVariable("PASSPORT_HOST_SAVE");
        string? name = Environment.GetEnvironmentVariable("PASSPORT_CHARACTER"), account = Environment.GetEnvironmentVariable("PASSPORT_HOST_ACCOUNT");
        if (homeSave == null || hostSave == null || name == null || account == null)
            Assert.Ignore("Set PASSPORT_HOME_SAVE, PASSPORT_HOST_SAVE, PASSPORT_CHARACTER and PASSPORT_HOST_ACCOUNT to run this.");

        string home = CopyWorld(homeSave, "real-home"), host = CopyWorld(hostSave, "real-host");
        string id = (string)Value(home, $"SELECT DOLCharacters_ID FROM DOLCharacters WHERE Name='{name}'")!;
        string[] tables = PassportTables.Travelling.Select(t => t.Table).ToArray();
        // A copy of the home save can be the host: the character is taken out of that copy first.
        foreach (TravellingTable table in PassportTables.Travelling)
            Sql(host, Value(host, $"SELECT count(*) FROM sqlite_master WHERE name='{table.Table}'") is 0L ? "SELECT 1" : $"DELETE FROM \"{table.Table}\" WHERE \"{table.CharacterColumn}\"='{id}'");
        Dictionary<string, List<string>> before = RealSnapshot(home, id);

        string passport = PassportEngine.Leave(home, name, null, "real test", _out);
        ArrivalReport report = PassportEngine.Arrive(host, passport, account);
        TestContext.Out.WriteLine($"Arrived as {report.Name}, slot {report.AccountSlot}. {string.Join(" ", report.Notes)}");
        foreach (string table in tables)
            TestContext.Out.WriteLine($"{table}: {before[table].Count} rows");

        string back = PassportEngine.Depart(host, report.Name, null, _out);
        PassportEngine.ConfirmDeparture(host, report.Name, PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(home, back).ToList().ForEach(TestContext.Out.WriteLine);

        Dictionary<string, List<string>> after = RealSnapshot(home, id);
        foreach (string table in tables)
            Assert.That(after[table], Is.EquivalentTo(before[table]), table);
        Assert.That(Value(host, $"SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID='{id}'"), Is.EqualTo(0L));
        Assert.That(Value(home, "PRAGMA quick_check"), Is.EqualTo("ok"));
        Assert.That(Value(host, "PRAGMA quick_check"), Is.EqualTo("ok"));
    }

    void Trip(Action? onHost = null)
    {
        PassportEngine.Arrive(_host, Leave(), "friend");
        onHost?.Invoke();
        string back = PassportEngine.Depart(_host, "Traveller", null, _out);
        PassportEngine.ConfirmDeparture(_host, "Traveller", PassportFile.Read(back).Manifest.TripId);
        PassportEngine.ComeHome(_home, back);
    }

    string Leave(long? purse = null) => PassportEngine.Leave(_home, "Traveller", purse, "Friend's world", _out);

    static string Refusal(TestDelegate action) => Assert.Throws<PassportException>(action)!.Code;

    string NewWorld(string name)
    {
        string runtime = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.Combine(runtime, "data"));
        SQLiteConnection.CreateFile(Path.Combine(runtime, "data", "opendaoc.sqlite3.db"));
        Sql(runtime, Schema);
        return runtime;
    }

    string CopyWorld(string save, string name)
    {
        string runtime = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.Combine(runtime, "data"));
        File.Copy(save, Path.Combine(runtime, "data", "opendaoc.sqlite3.db"));
        return runtime;
    }

    static SQLiteConnection Open(string runtime)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = Path.Combine(runtime, "data", "opendaoc.sqlite3.db"), Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    static void Sql(string runtime, string sql)
    {
        using SQLiteConnection connection = Open(runtime);
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static object? Value(string runtime, string sql)
    {
        using SQLiteConnection connection = Open(runtime);
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    static long Wallet(string runtime, string account, int realm)
    {
        using SQLiteConnection connection = Open(runtime);
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT Mithril,Platinum,Gold,Silver,Copper FROM AccountXMoney WHERE AccountId='{account}' AND Realm={realm}";
        using SQLiteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return 0;
        var wallet = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            wallet[reader.GetName(i)] = reader.GetValue(i);
        return PassportEngine.ToCopper(wallet);
    }

    static Dictionary<string, List<string>> Snapshot(string runtime, string[] tables) =>
        tables.ToDictionary(t => t, t => Dump(runtime, $"SELECT * FROM \"{t}\""));

    static Dictionary<string, List<string>> RealSnapshot(string runtime, string id) =>
        PassportTables.Travelling.ToDictionary(t => t.Table, t => Dump(runtime,
            $"SELECT * FROM \"{t.Table}\" WHERE \"{t.CharacterColumn}\"='{id}'" + (t.StaysHome.Length > 0 ? $" AND {t.StaysHome}" : "")));

    // Every value as storage class and text; an autoincrement key is a row's only allowed difference.
    static List<string> Dump(string runtime, string sql)
    {
        using SQLiteConnection connection = Open(runtime);
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SQLiteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var fields = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string column = reader.GetName(i);
                if (column is not ("CustomParamID" or "ID" or "TimeXLevel_ID"))
                    fields.Add($"{column}={(reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i)))}");
            }
            rows.Add(string.Join("|", fields));
        }
        return rows;
    }
}
