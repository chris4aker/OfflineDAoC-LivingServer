using System.Globalization;

namespace OfflineDaoc.Passport;

/// <summary>A refusal the player can read. Nothing has changed when one is thrown.</summary>
public sealed class PassportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record TravellingTable(string Table, string CharacterColumn, string StaysHome = "");

public static class PassportTables
{
    /// <summary>Items still in this world's market or a house, not on the character (eInventorySlot.HouseVault_First).</summary>
    public const int FirstWorldBoundSlot = 1000;

    const string TravellingItems = "SlotPosition<1000 AND COALESCE(OwnerLot,0)=0";
    const string TravellingParams = "KeyName NOT LIKE 'passport.%' AND KeyName NOT LIKE 'RealmExchange.Proceeds.%'";

    public static readonly TravellingTable[] Travelling =
    [
        new("DOLCharacters", "DOLCharacters_ID"),
        new("DOLCharactersXCustomParam", "DOLCharactersObjectId", TravellingParams),
        new("CharacterXDataQuest", "Character_ID"),
        new("Quest", "Character_ID"),
        new("Task", "Character_ID"),
        new("CharacterXMasterLevel", "Character_ID"),
        new("CharacterXOneTimeDrop", "CharacterID"),
        new("FactionAggroLevel", "CharacterID"),
        new("PlayerXEffect", "ChardID"),
        new("TimeXLevel", "Character_ID"),
        new("offline_artifact_progress", "CharacterID"),
        new("Inventory", "OwnerID", TravellingItems),
    ];

    /// <summary>
    /// Columns that belong to one world. They are blanked on the way out, and the home values
    /// are put back from the frozen row when the character returns.
    /// </summary>
    public static readonly string[] HomeOnlyColumns =
    [
        "AccountName", "AccountSlot", "Name", "GuildID", "GuildRank", "GuildNote",
        "BindHouseRegion", "BindHouseXpos", "BindHouseYpos", "BindHouseZpos", "BindHouseHeading",
    ];
}

public sealed record ArrivalReport(string Name, int AccountSlot, IReadOnlyList<string> Notes);

/// <summary>
/// One character's round trip between two saves. The character is playable in exactly one world:
/// its home rows stay in place, locked by the passport.away custom param, until the return passport
/// of the same trip comes back. Every method needs that world's server stopped.
/// </summary>
public static class PassportEngine
{
    public const string AwayKey = "passport.away", HostKey = "passport.host", GuildKey = "passport.guild";
    public const string PurseKey = "passport.purse", LeftKey = "passport.left_utc", CancelledKey = "passport.cancelled";
    const string ProceedsPrefix = "RealmExchange.Proceeds.";
    const string VisitsTable = "passport_visits";
    const string Visiting = "visiting", Departing = "departing", Returned = "returned";
    const string ConvertedPrefix = "passport#";
    const int RetiredSluaghbinderClass = 63;
    const int SlotsPerRealm = 10;
    const int NoGuildRank = 9;

    /// <summary>Home: writes the passport and locks the character. Null purse carries the whole realm wallet.</summary>
    public static string Leave(string runtime, string characterName, long? purseCopper, string hostLabel, string outFolder)
    {
        string database = OpenWorld(runtime);
        using var db = new PassportDatabase(database);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        string account = character.Text("AccountName");
        int realm = Convert.ToInt32(character["Realm"]);

        if (Param(db, id, AwayKey) is { } away)
            throw new PassportException("already_away", $"{character.Text("Name")} is already away ({Param(db, id, HostKey) ?? "another world"}, trip {away}).");
        if (VisitState(db, id) is Visiting or Departing)
            throw new PassportException("is_visitor", $"{character.Text("Name")} is a visitor here. Send it home before it travels again.");

        long wallet = WalletCopper(db, account, realm);
        long purse = purseCopper ?? wallet;
        if (purse < 0 || purse > wallet)
            throw new PassportException("purse", $"The realm wallet holds {wallet} copper; {purse} cannot be carried.");

        BackUp(runtime, database, character.Text("Name"), "out");
        string tripId = Guid.NewGuid().ToString();
        string path = Path.Combine(outFolder, PassportFile.FileName(character.Text("Name"), tripId, Manifest.Outbound));
        string partial = path + ".partial";
        Directory.CreateDirectory(outFolder);

        try
        {
            db.Transaction(() =>
            {
                PassportFile.Write(Pack(db, character, Manifest.Outbound, tripId, purse, runtime), partial);
                SetWallet(db, account, realm, wallet - purse);
                SetParam(db, id, AwayKey, tripId);
                SetParam(db, id, HostKey, hostLabel.Length > 200 ? hostLabel[..200] : hostLabel);
                SetParam(db, id, GuildKey, $"{character.Text("GuildID")}|{character.Text("GuildRank")}");
                SetParam(db, id, PurseKey, purse.ToString(CultureInfo.InvariantCulture));
                SetParam(db, id, LeftKey, DateTime.UtcNow.ToString("O"));
            });
        }
        catch
        {
            File.Delete(partial);
            throw;
        }

        File.Move(partial, path);
        return path;
    }

    /// <summary>Host: adds the visitor to an existing account. The account must have logged in here once.</summary>
    public static ArrivalReport Arrive(string runtime, string passportPath, string account, string? visitingName = null)
    {
        string database = OpenWorld(runtime);
        PassportData passport = PassportFile.Read(passportPath);
        Manifest manifest = passport.Manifest;
        if (manifest.Direction != Manifest.Outbound)
            throw new PassportException("wrong_direction", "This passport is a return passport. Use it at the character's home.");
        if (manifest.Class == RetiredSluaghbinderClass)
            throw new PassportException("class", "This world has no Sluaghbinder class.");

        using var db = new PassportDatabase(database);
        EnsureVisits(db);
        Row accountRow = FindAccount(db, account);
        string accountName = accountRow.Text("Name");

        if (db.Count("SELECT count(*) FROM DOLCharacters WHERE DOLCharacters_ID=@id", ("@id", manifest.CharacterId)) > 0)
            throw new PassportException("already_here", $"{manifest.Name} is already in this world.");
        string? previousTrip = Convert.ToString(db.Scalar($"SELECT trip_id FROM {VisitsTable} WHERE character_id=@id", ("@id", manifest.CharacterId)));
        if (previousTrip == manifest.TripId)
            throw new PassportException("replayed", "This passport was already used. Start a new trip from home.");

        string name = string.IsNullOrWhiteSpace(visitingName) ? manifest.Name : visitingName.Trim();
        if (NameTaken(db, name))
            throw new PassportException("name_taken", $"The name {name} is taken in this world. Choose a visiting name.");
        int slot = FreeSlot(db, accountName, manifest.Realm);

        var notes = new List<string>();
        db.Transaction(() =>
        {
            Row row = passport.Tables["DOLCharacters"].Single();
            row["AccountName"] = accountName;
            row["AccountSlot"] = (long)slot;
            row["Name"] = name;
            PlaceInWorld(db, row, notes);

            Unpack(db, passport, notes);
            AddToWallet(db, accountName, manifest.Realm, manifest.PurseCopper);
            MergeCrafting(db, accountName, manifest.Realm, manifest.CraftingSkills, manifest.CraftingPrimarySkill);

            string? hostGuild = Convert.ToString(db.Scalar($"SELECT host_guild FROM {VisitsTable} WHERE character_id=@id", ("@id", manifest.CharacterId)));
            RestoreGuild(db, manifest.CharacterId, hostGuild);
            if (Convert.ToInt64(accountRow["Realm"]) == 0)
                db.Exec("UPDATE Account SET Realm=@r WHERE Name=@a", ("@r", (long)manifest.Realm), ("@a", accountName));

            db.Exec($"DELETE FROM {VisitsTable} WHERE character_id=@id", ("@id", manifest.CharacterId));
            db.Exec($"INSERT INTO {VisitsTable} (character_id,account,trip_id,state,arrived_utc,host_guild,home_name,purse_copper) VALUES (@id,@a,@t,@s,@u,@g,@n,0)",
                ("@id", manifest.CharacterId), ("@a", accountName), ("@t", manifest.TripId), ("@s", Visiting),
                ("@u", DateTime.UtcNow.ToString("O")), ("@g", hostGuild ?? ""), ("@n", manifest.Name));
        });
        return new ArrivalReport(name, slot, notes);
    }

    /// <summary>
    /// Host: writes the return passport and locks the visitor. Calling it again gives the same trip and purse,
    /// so a lost file can be remade until <see cref="ConfirmDeparture"/> runs.
    /// </summary>
    public static string Depart(string runtime, string characterName, long? purseCopper, string outFolder)
    {
        string database = OpenWorld(runtime);
        using var db = new PassportDatabase(database);
        EnsureVisits(db);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        string account = character.Text("AccountName");
        int realm = Convert.ToInt32(character["Realm"]);

        List<Row> visits = db.Rows(VisitsTable, "character_id=@id", ("@id", id));
        Row visit = visits.SingleOrDefault() ?? throw new PassportException("not_visiting", $"{character.Text("Name")} is not visiting this world.");
        string state = visit.Text("state");
        if (state is not (Visiting or Departing))
            throw new PassportException("not_visiting", $"{character.Text("Name")} is not visiting this world.");
        RefuseWorldBoundProgress(db, character);

        long purse;
        if (state == Departing)
            purse = Convert.ToInt64(visit["purse_copper"]);
        else
        {
            long wallet = WalletCopper(db, account, realm);
            long others = db.Count("SELECT count(*) FROM DOLCharacters WHERE AccountName=@a AND Realm=@r AND DOLCharacters_ID<>@id",
                ("@a", account), ("@r", (long)realm), ("@id", id));
            purse = Math.Min(purseCopper ?? (others == 0 ? wallet : 0), wallet);
            if (purse < 0)
                throw new PassportException("purse", "A purse cannot be negative.");
        }

        string tripId = visit.Text("trip_id");
        string path = Path.Combine(outFolder, PassportFile.FileName(visit.Text("home_name"), tripId, Manifest.Return));
        string partial = path + ".partial";
        Directory.CreateDirectory(outFolder);
        File.Delete(partial);

        try
        {
            db.Transaction(() =>
            {
                PassportFile.Write(Pack(db, character, Manifest.Return, tripId, purse, runtime), partial);
                if (state == Departing)
                    return;

                SetWallet(db, account, realm, WalletCopper(db, account, realm) - purse);
                SetParam(db, id, AwayKey, tripId);
                SetParam(db, id, HostKey, "its home world");
                db.Exec($"UPDATE {VisitsTable} SET state=@s,host_guild=@g,purse_copper=@p WHERE character_id=@id",
                    ("@s", Departing), ("@g", $"{character.Text("GuildID")}|{character.Text("GuildRank")}"), ("@p", purse), ("@id", id));
            });
        }
        catch
        {
            File.Delete(partial);
            throw;
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>Host: forgets the visitor once its return passport is safe at home.</summary>
    public static void ConfirmDeparture(string runtime, string characterName, string tripId)
    {
        string database = OpenWorld(runtime);
        using var db = new PassportDatabase(database);
        EnsureVisits(db);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        Row? visit = db.Rows(VisitsTable, "character_id=@id", ("@id", id)).SingleOrDefault();
        if (visit == null || visit.Text("state") != Departing || visit.Text("trip_id") != tripId)
            throw new PassportException("no_departure", $"{character.Text("Name")} has no departure to confirm for trip {tripId}.");

        BackUp(runtime, database, character.Text("Name"), "left");
        db.Transaction(() =>
        {
            RemoveCharacter(db, id);
            db.Exec("DELETE FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId=@id", ("@id", id));
            db.Exec($"UPDATE {VisitsTable} SET state=@s WHERE character_id=@id", ("@s", Returned), ("@id", id));
            string account = character.Text("AccountName");
            if (db.Count("SELECT count(*) FROM DOLCharacters WHERE AccountName=@a", ("@a", account)) == 0)
                db.Exec("UPDATE Account SET Realm=0 WHERE Name=@a", ("@a", account));
        });
    }

    /// <summary>Home: replaces the frozen character with what comes back, and unlocks it.</summary>
    public static IReadOnlyList<string> ComeHome(string runtime, string passportPath)
    {
        string database = OpenWorld(runtime);
        PassportData passport = PassportFile.Read(passportPath);
        Manifest manifest = passport.Manifest;
        if (manifest.Direction != Manifest.Return)
            throw new PassportException("wrong_direction", "This passport is for leaving home, not for coming back.");

        using var db = new PassportDatabase(database);
        Row home = db.Rows("DOLCharacters", "DOLCharacters_ID=@id", ("@id", manifest.CharacterId)).SingleOrDefault()
            ?? throw new PassportException("not_home", $"{manifest.Name} does not live in this save.");
        string id = manifest.CharacterId;
        string? away = Param(db, id, AwayKey);
        if (away != manifest.TripId)
            throw new PassportException("wrong_trip", Param(db, id, CancelledKey) == manifest.TripId
                ? "This trip was recalled, so what it earned cannot come home."
                : "This passport is from another trip. Nothing was changed.");

        BackUp(runtime, database, home.Text("Name"), "in");
        var notes = new List<string>();
        db.Transaction(() =>
        {
            string account = home.Text("AccountName");
            int realm = Convert.ToInt32(home["Realm"]);
            string guild = Param(db, id, GuildKey) ?? "|";

            RemoveCharacter(db, id);
            Row row = passport.Tables["DOLCharacters"].Single();
            foreach (string column in PassportTables.HomeOnlyColumns)
                row[column] = home.GetValueOrDefault(column);
            foreach (Row level in passport.Tables.GetValueOrDefault("TimeXLevel") ?? [])
                level["Character_Name"] = home["Name"];

            Unpack(db, passport, notes);
            AddToWallet(db, account, realm, manifest.PurseCopper);
            MergeCrafting(db, account, realm, manifest.CraftingSkills, manifest.CraftingPrimarySkill);
            RestoreGuild(db, id, guild);
            ClearTripParams(db, id);
        });
        return notes;
    }

    /// <summary>Home: gives up on the trip. The frozen character and its purse come back; everything earned on the trip is lost.</summary>
    public static void Recall(string runtime, string characterName)
    {
        string database = OpenWorld(runtime);
        using var db = new PassportDatabase(database);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        string trip = Param(db, id, AwayKey) ?? throw new PassportException("not_away", $"{character.Text("Name")} is not away.");
        if (VisitState(db, id) == Departing)
            throw new PassportException("is_visitor", $"{character.Text("Name")} is a visitor leaving this world. Recall it from its home save.");

        long purse = long.Parse(Param(db, id, PurseKey) ?? "0", CultureInfo.InvariantCulture);
        BackUp(runtime, database, character.Text("Name"), "recall");
        db.Transaction(() =>
        {
            AddToWallet(db, character.Text("AccountName"), Convert.ToInt32(character["Realm"]), purse);
            ClearTripParams(db, id);
            SetParam(db, id, CancelledKey, trip);
        });
    }

    static string OpenWorld(string runtime)
    {
        string database = Path.Combine(runtime, "data", "opendaoc.sqlite3.db");
        if (!File.Exists(database))
            throw new PassportException("no_save", $"No save at {database}.");

        // A running server, however it was started, holds its own assemblies open.
        foreach (string file in new[] { Path.Combine("server", "CoreServer.dll"), Path.Combine("server", "lib", "GameServer.dll") })
        {
            string path = Path.Combine(runtime, file);
            if (!File.Exists(path))
                continue;
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                throw new PassportException("server_running", "Stop this world's server first. Nothing was changed.");
            }
        }
        return database;
    }

    static void BackUp(string runtime, string database, string name, string step)
    {
        string folder = Path.Combine(runtime, "passport-backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}-{step}");
        Directory.CreateDirectory(folder);
        PassportDatabase.Backup(database, Path.Combine(folder, "opendaoc.sqlite3.db"));
    }

    static Row FindCharacter(PassportDatabase db, string name) =>
        db.Rows("DOLCharacters", "Name=@n COLLATE NOCASE", ("@n", name)).SingleOrDefault()
        ?? throw new PassportException("no_character", $"No character named {name} in this save.");

    static Row FindAccount(PassportDatabase db, string name) =>
        db.Rows("Account", "Name=@n COLLATE NOCASE", ("@n", name)).SingleOrDefault()
        ?? throw new PassportException("no_account", $"No account {name} in this world. Log in here once first.");

    static PassportData Pack(PassportDatabase db, Row character, string direction, string tripId, long purse, string runtime)
    {
        string id = character.Text("DOLCharacters_ID");
        Row? crafting = CraftingRow(db, character.Text("AccountName"), Convert.ToInt32(character["Realm"]));
        var passport = new PassportData
        {
            Manifest = new Manifest
            {
                TripId = tripId,
                CharacterId = id,
                Name = character.Text("Name"),
                Realm = Convert.ToInt32(character["Realm"]),
                Class = Convert.ToInt32(character["Class"]),
                Level = Convert.ToInt32(character["Level"]),
                Direction = direction,
                FromWorld = new DirectoryInfo(runtime).Parent?.Name ?? "",
                PurseCopper = purse,
                CraftingSkills = crafting?.Text("SerializedCraftingSkills") ?? "",
                CraftingPrimarySkill = crafting == null ? 0 : Convert.ToInt32(crafting["CraftingPrimarySkill"]),
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            },
        };

        foreach (TravellingTable table in PassportTables.Travelling)
            if (db.HasTable(table.Table))
                passport.Tables[table.Table] = db.Rows(table.Table, WhereCharacter(table), ("@id", id));

        Row packed = passport.Tables["DOLCharacters"].Single();
        packed["GuildID"] = "";
        packed["GuildRank"] = (long)NoGuildRank;
        packed["GuildNote"] = "";
        foreach (string column in PassportTables.HomeOnlyColumns.Where(c => c.StartsWith("BindHouse", StringComparison.Ordinal)))
            packed[column] = 0L;

        foreach (Row item in passport.Tables["Inventory"])
        {
            string unique = item.Text("UTemplate_Id"), template = item.Text("ITemplate_Id");
            if (unique.Length > 0)
                passport.ItemUnique.AddRange(db.Rows("ItemUnique", "Id_nb=@i", ("@i", unique)));
            else if (template.Length > 0 && !passport.ItemTemplate.Any(t => t.Text("Id_nb") == template))
                passport.ItemTemplate.AddRange(db.Rows("ItemTemplate", "Id_nb=@i", ("@i", template)));
        }
        return passport;
    }

    static string WhereCharacter(TravellingTable table) =>
        $"{PassportDatabase.Q(table.CharacterColumn)}=@id" + (table.StaysHome.Length > 0 ? $" AND {table.StaysHome}" : "");

    static void Unpack(PassportDatabase db, PassportData passport, List<string> notes)
    {
        Dictionary<string, Row> uniques = passport.ItemUnique.GroupBy(u => u.Text("Id_nb")).ToDictionary(g => g.Key, g => g.First());
        Dictionary<string, Row> templates = passport.ItemTemplate.GroupBy(t => t.Text("Id_nb")).ToDictionary(g => g.Key, g => g.First());
        foreach (Row item in passport.Tables["Inventory"])
            MapItem(db, item, uniques, templates, notes);

        foreach ((string table, List<Row> rows) in passport.Tables)
        {
            if (rows.Count == 0)
                continue;
            if (!db.HasTable(table))
                throw new PassportException("missing_table", $"This world has no {table} table. Start its server once, or update it.");

            var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Row row in rows)
                dropped.UnionWith(db.Insert(table, row));
            if (dropped.Count > 0)
                notes.Add($"{table}: this world has no column {string.Join(", ", dropped)}.");
        }

        foreach (TravellingTable table in PassportTables.Travelling.Where(t => passport.Tables.ContainsKey(t.Table) && db.HasTable(t.Table)))
            if (db.Count($"SELECT count(*) FROM {PassportDatabase.Q(table.Table)} WHERE {WhereCharacter(table)}", ("@id", passport.Manifest.CharacterId)) != passport.Tables[table.Table].Count)
                throw new InvalidDataException($"Row check failed for {table.Table}.");
    }

    // The receiver's definition wins when it has the item. One it lacks becomes a unique of its own,
    // named so that a world holding the template turns it back into a plain template item.
    static void MapItem(PassportDatabase db, Row item, Dictionary<string, Row> uniques, Dictionary<string, Row> templates, List<string> notes)
    {
        string unique = item.Text("UTemplate_Id"), template = item.Text("ITemplate_Id");
        if (unique.Length > 0)
        {
            if (unique.StartsWith(ConvertedPrefix, StringComparison.Ordinal))
            {
                string original = unique[ConvertedPrefix.Length..unique.LastIndexOf('#')];
                if (HasTemplate(db, original))
                {
                    item["ITemplate_Id"] = original;
                    item["UTemplate_Id"] = null;
                    return;
                }
            }
            if (uniques.TryGetValue(unique, out Row? row))
                db.Insert("ItemUnique", row, orIgnore: true);
            return;
        }

        if (template.Length == 0 || HasTemplate(db, template))
            return;
        if (!templates.TryGetValue(template, out Row? carried))
        {
            notes.Add($"Item {template} had no definition in the world it came from.");
            return;
        }

        var converted = new Row(carried) { ["Id_nb"] = $"{ConvertedPrefix}{template}#{Guid.NewGuid()}" };
        converted.Remove("ItemTemplate_ID");
        converted["ItemUnique_ID"] = Guid.NewGuid().ToString();
        db.Insert("ItemUnique", converted);
        item["UTemplate_Id"] = converted["Id_nb"];
        item["ITemplate_Id"] = null;
    }

    static bool HasTemplate(PassportDatabase db, string id) =>
        db.Count("SELECT count(*) FROM ItemTemplate WHERE Id_nb=@i", ("@i", id)) > 0;

    // Listings, house vaults and consignment stock are not on the character, and pending sale
    // money is paid in this world, so a visitor settles them here before it can leave.
    static void RefuseWorldBoundProgress(PassportDatabase db, Row character)
    {
        string id = character.Text("DOLCharacters_ID"), name = character.Text("Name");
        if (db.Count($"SELECT count(*) FROM Inventory WHERE OwnerID=@id AND (SlotPosition>={PassportTables.FirstWorldBoundSlot} OR COALESCE(OwnerLot,0)<>0)", ("@id", id)) > 0)
            throw new PassportException("market_items", $"{name} has items for sale or in a house here. Take them back first.");
        if (db.HasTable("DBHouse") && db.Count("SELECT count(*) FROM DBHouse WHERE OwnerID=@id", ("@id", id)) > 0)
            throw new PassportException("house", $"{name} owns a house here. Give it up first.");
        foreach (string value in db.Rows("DOLCharactersXCustomParam", "DOLCharactersObjectId=@id AND KeyName LIKE @k", ("@id", id), ("@k", ProceedsPrefix + "%")).Select(p => p.Text("Value")))
            if (long.TryParse(value.Split('|')[0], out long pending) && pending > 0)
                throw new PassportException("proceeds", $"{name} has Realm Exchange money waiting here. Collect it first.");
    }

    static void RemoveCharacter(PassportDatabase db, string id)
    {
        List<string> uniques = db.Rows("Inventory", WhereCharacter(PassportTables.Travelling[^1]), ("@id", id))
            .Select(i => i.Text("UTemplate_Id")).Where(u => u.Length > 0).ToList();

        foreach (TravellingTable table in PassportTables.Travelling.Where(t => db.HasTable(t.Table)))
            db.Exec($"DELETE FROM {PassportDatabase.Q(table.Table)} WHERE {WhereCharacter(table)}", ("@id", id));
        foreach (string unique in uniques)
            if (db.Count("SELECT count(*) FROM Inventory WHERE UTemplate_Id=@u", ("@u", unique)) == 0)
                db.Exec("DELETE FROM ItemUnique WHERE Id_nb=@u", ("@u", unique));
    }

    static void PlaceInWorld(PassportDatabase db, Row row, List<string> notes)
    {
        if (!db.HasTable("Regions"))
            return;

        bool Known(object? region) => db.Count("SELECT count(*) FROM Regions WHERE RegionID=@r", ("@r", region)) > 0;
        if (Known(row["Region"]))
            return;
        if (!Known(row["BindRegion"]))
            throw new PassportException("no_region", "This world has neither the character's region nor its bind point.");

        foreach ((string to, string from) in new[] { ("Region", "BindRegion"), ("Xpos", "BindXpos"), ("Ypos", "BindYpos"), ("Zpos", "BindZpos"), ("Direction", "BindHeading") })
            row[to] = row[from];
        notes.Add("This world lacks the region the character stood in, so it starts at its bind point.");
    }

    static bool NameTaken(PassportDatabase db, string name) =>
        db.Count("SELECT count(*) FROM DOLCharacters WHERE Name=@n COLLATE NOCASE", ("@n", name)) > 0 ||
        (db.HasTable("offline_world_bots") && db.Count("SELECT count(*) FROM offline_world_bots WHERE Name=@n COLLATE NOCASE", ("@n", name)) > 0);

    static int FreeSlot(PassportDatabase db, string account, int realm)
    {
        for (int slot = realm * 100; slot < realm * 100 + SlotsPerRealm; slot++)
            if (db.Count("SELECT count(*) FROM DOLCharacters WHERE AccountName=@a AND AccountSlot=@s", ("@a", account), ("@s", (long)slot)) == 0)
                return slot;
        throw new PassportException("no_slot", "This account has no free character slot in that realm here.");
    }

    static void RestoreGuild(PassportDatabase db, string id, string? saved)
    {
        string[] parts = (saved ?? "").Split('|');
        if (parts[0].Length == 0 || db.Count("SELECT count(*) FROM Guild WHERE GuildID=@g", ("@g", parts[0])) == 0)
            return;
        db.Exec("UPDATE DOLCharacters SET GuildID=@g,GuildRank=@r WHERE DOLCharacters_ID=@id",
            ("@g", parts[0]), ("@r", long.TryParse(parts.ElementAtOrDefault(1), out long rank) ? rank : NoGuildRank), ("@id", id));
    }

    static void EnsureVisits(PassportDatabase db) => db.Exec(
        $"CREATE TABLE IF NOT EXISTS {VisitsTable} (character_id TEXT PRIMARY KEY, account TEXT NOT NULL, trip_id TEXT NOT NULL, state TEXT NOT NULL, " +
        "arrived_utc TEXT NOT NULL, host_guild TEXT NOT NULL DEFAULT '', home_name TEXT NOT NULL, purse_copper BIGINT NOT NULL DEFAULT 0)");

    static string? VisitState(PassportDatabase db, string id) =>
        db.HasTable(VisitsTable) ? Convert.ToString(db.Scalar($"SELECT state FROM {VisitsTable} WHERE character_id=@id", ("@id", id))) : null;

    static string? Param(PassportDatabase db, string id, string key) =>
        db.Rows("DOLCharactersXCustomParam", "DOLCharactersObjectId=@id AND KeyName=@k", ("@id", id), ("@k", key)).SingleOrDefault()?.Text("Value");

    static void SetParam(PassportDatabase db, string id, string key, string value)
    {
        db.Exec("DELETE FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId=@id AND KeyName=@k", ("@id", id), ("@k", key));
        db.Exec("INSERT INTO DOLCharactersXCustomParam (DOLCharactersObjectId,KeyName,Value) VALUES (@id,@k,@v)", ("@id", id), ("@k", key), ("@v", value));
    }

    static void ClearTripParams(PassportDatabase db, string id)
    {
        foreach (string key in new[] { AwayKey, HostKey, GuildKey, PurseKey, LeftKey })
            db.Exec("DELETE FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId=@id AND KeyName=@k", ("@id", id), ("@k", key));
    }

    // A wallet row is keyed by the account name; one made in the session an account was created
    // carries its Account_ID instead, and the server stops finding that row after a restart.
    static Row? WalletRow(PassportDatabase db, string account, int realm) =>
        db.Rows("AccountXMoney", "AccountId=@a AND Realm=@r", ("@a", account), ("@r", (long)realm)).FirstOrDefault();

    static long WalletCopper(PassportDatabase db, string account, int realm)
    {
        Row? wallet = WalletRow(db, account, realm);
        return wallet == null ? 0 : ToCopper(wallet);
    }

    /// <summary>Money.GetMoney: 100 copper, 100 silver, 1000 gold, 1000 platinum.</summary>
    public static long ToCopper(IReadOnlyDictionary<string, object?> wallet)
    {
        long Part(string column) => Convert.ToInt64(wallet[column]);
        return (((Part("Mithril") * 1000 + Part("Platinum")) * 1000 + Part("Gold")) * 100 + Part("Silver")) * 100 + Part("Copper");
    }

    static void AddToWallet(PassportDatabase db, string account, int realm, long copper) =>
        SetWallet(db, account, realm, WalletCopper(db, account, realm) + copper);

    // The wallet row is always written: without one the server rebuilds a wallet by adding up the
    // money columns on the account's characters, which would pay a visitor's old coin twice.
    static void SetWallet(PassportDatabase db, string account, int realm, long total)
    {
        if (WalletRow(db, account, realm) == null)
            db.Exec("INSERT INTO AccountXMoney (AccountId,Realm) VALUES (@a,@r)", ("@a", account), ("@r", (long)realm));

        long copper = total % 100, silver = total / 100 % 100, gold = total / 10_000 % 1000, platinum = total / 10_000_000 % 1000, mithril = total / 10_000_000_000;
        db.Exec("UPDATE AccountXMoney SET Copper=@c,Silver=@s,Gold=@g,Platinum=@p,Mithril=@m WHERE AccountId=@a AND Realm=@r",
            ("@c", copper), ("@s", silver), ("@g", gold), ("@p", platinum), ("@m", mithril), ("@a", account), ("@r", (long)realm));
    }

    static Row? CraftingRow(PassportDatabase db, string account, int realm) =>
        db.Rows("AccountXCrafting", "AccountId=@a AND Realm=@r", ("@a", account), ("@r", (long)realm)).FirstOrDefault();

    /// <summary>Crafting is account wide, so each skill keeps the higher value of the two worlds.</summary>
    public static string MergeSkills(string first, string second)
    {
        var merged = new SortedDictionary<int, int>();
        foreach (string pair in $"{first};{second}".Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('|');
            if (parts.Length == 2 && int.TryParse(parts[0], out int skill) && int.TryParse(parts[1], out int value))
                merged[skill] = Math.Max(merged.GetValueOrDefault(skill), value);
        }
        return string.Join(";", merged.Select(s => $"{s.Key}|{s.Value}"));
    }

    static void MergeCrafting(PassportDatabase db, string account, int realm, string skills, int primary)
    {
        if (skills.Length == 0)
            return;

        Row? row = CraftingRow(db, account, realm);
        if (row == null)
            db.Exec("INSERT INTO AccountXCrafting (AccountId,Realm,SerializedCraftingSkills,CraftingPrimarySkill) VALUES (@a,@r,@s,@p)",
                ("@a", account), ("@r", (long)realm), ("@s", skills), ("@p", (long)primary));
        else
            db.Exec("UPDATE AccountXCrafting SET SerializedCraftingSkills=@s,CraftingPrimarySkill=@p WHERE AccountId=@a AND Realm=@r",
                ("@s", MergeSkills(row.Text("SerializedCraftingSkills"), skills)),
                ("@p", row.Text("CraftingPrimarySkill") is "" or "0" ? primary : row["CraftingPrimarySkill"]),
                ("@a", account), ("@r", (long)realm));
    }
}
