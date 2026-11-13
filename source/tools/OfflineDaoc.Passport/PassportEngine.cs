#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// Character passport. From rambodolphin13/DividedRealms (da78651, da92848; credit: rambodolphin13), with Living Server's
// changes (1.2): every progress-policy table has a passport decision (PassportTables.Decisions, checked by the tests
// against progress-policy.json), every step that writes takes a checked backup first (the live Desk too), the passport
// carries no home account name or slot, a visiting name follows the game's name rule, and the Sluaghbinder may visit only
// a host that has the class on.
namespace OfflineDaoc.Passport;

/// <summary>A refusal the player can read. Nothing has changed when one is thrown.</summary>
public sealed class PassportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record TravellingTable(string Table, string CharacterColumn, string StaysHome = "");

/// <summary>What the passport does with one saved-progress table (progress-policy.json).</summary>
public enum PassportDecision
{
    /// <summary>The character's rows go in the passport and replace the home rows when it comes back.</summary>
    Travels,
    /// <summary>Account-wide per realm, moved by its own rule: the purse (money) or the higher value (crafting).</summary>
    MovedByRule,
    /// <summary>Belongs to one world. The rows stay where they are, at home and at the host.</summary>
    StaysInWorld,
}

/// <param name="PerCharacter">The table has rows for one character (by its id or name) that deliberately do not travel.</param>
public sealed record TableDecision(PassportDecision Decision, string Why, bool PerCharacter = false);

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
        new("Inventory", "OwnerID", TravellingItems),
    ];

    /// <summary>
    /// Living Server: the passport's decision for every table of progress-policy.json (ProgressTables and
    /// OptionalProgressTables). A table added to the policy without a decision here fails PassportPolicyTests.
    /// ItemUnique travels as the rows the character's items use; ItemTemplate is world data, not progress.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TableDecision> Decisions = new Dictionary<string, TableDecision>(StringComparer.OrdinalIgnoreCase)
    {
        ["DOLCharacters"] = new(PassportDecision.Travels, "The character. Account, slot, guild and house bind stay home and are put back."),
        ["DOLCharactersXCustomParam"] = new(PassportDecision.Travels, "The character's settings. passport.* and Realm Exchange sale money stay in their world.", true),
        ["CharacterXDataQuest"] = new(PassportDecision.Travels, "Quest steps."),
        ["Quest"] = new(PassportDecision.Travels, "Quests in progress and done."),
        ["Task"] = new(PassportDecision.Travels, "Tasks."),
        ["CharacterXMasterLevel"] = new(PassportDecision.Travels, "Master levels."),
        ["CharacterXOneTimeDrop"] = new(PassportDecision.Travels, "One-time drops already had."),
        ["FactionAggroLevel"] = new(PassportDecision.Travels, "Faction standing."),
        ["PlayerXEffect"] = new(PassportDecision.Travels, "Effects that last through a logout."),
        ["TimeXLevel"] = new(PassportDecision.Travels, "Level-up times."),
        ["Inventory"] = new(PassportDecision.Travels, "Worn items, backpack and the character's own vault. House vaults, consignment and Realm Exchange listings stay.", true),
        ["ItemUnique"] = new(PassportDecision.Travels, "Only the rows the travelling items use."),
        ["AccountXMoney"] = new(PassportDecision.MovedByRule, "The realm wallet is shared by the account's characters: a purse leaves it and joins the other world's wallet."),
        ["AccountXCrafting"] = new(PassportDecision.MovedByRule, "Crafting is account wide: each skill keeps the higher value of the two worlds."),
        ["Account"] = new(PassportDecision.StaysInWorld, "Each world has its own accounts and passwords; none is in the passport."),
        ["AccountXCustomParam"] = new(PassportDecision.StaysInWorld, "Account settings of that world."),
        ["DOLCharactersBackup"] = new(PassportDecision.StaysInWorld, "Copies of deleted characters."),
        ["DOLCharactersBackupXCustomParam"] = new(PassportDecision.StaysInWorld, "Settings of deleted characters."),
        ["bot_profiles"] = new(PassportDecision.StaysInWorld, "Companion bots stay in the world they were made in and wait for the character there.", true),
        ["bot_settings"] = new(PassportDecision.StaysInWorld, "Settings of those companions."),
        ["offline_world_bots"] = new(PassportDecision.StaysInWorld, "That world's autonomous gamebots."),
        ["offline_world_bot_guild"] = new(PassportDecision.StaysInWorld, "Gamebots' guild memberships."),
        ["offline_auction_escrow"] = new(PassportDecision.StaysInWorld, "The gamebots' own auction."),
        ["offline_auction_ledger"] = new(PassportDecision.StaysInWorld, "The gamebots' own auction."),
        ["offline_auction_listings"] = new(PassportDecision.StaysInWorld, "The gamebots' own auction."),
        ["realm_exchange_sales"] = new(PassportDecision.StaysInWorld, "That world's Realm Exchange sale ledger; its money is paid out there."),
        ["Guild"] = new(PassportDecision.StaysInWorld, "Guilds. The character leaves its guild for the trip and rejoins it at home."),
        ["GuildRank"] = new(PassportDecision.StaysInWorld, "Guild ranks."),
        ["GuildAlliance"] = new(PassportDecision.StaysInWorld, "Guild alliances."),
        ["DBHouseCharsXPerms"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["DBHousePermissions"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["DBIndoorItem"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["DBOutdoorItem"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["HouseConsignmentMerchant"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["househookpointitem"] = new(PassportDecision.StaysInWorld, "Houses belong to their world."),
        ["PlayerBoats"] = new(PassportDecision.StaysInWorld, "Boats belong to their world."),
        ["offline_bot_guilds"] = new(PassportDecision.StaysInWorld, "That world's gamebot guilds."),
        ["offline_bot_guild_members"] = new(PassportDecision.StaysInWorld, "That world's gamebot guilds."),
        ["offline_bot_scenes_heard"] = new(PassportDecision.StaysInWorld, "Per realm, not per character."),
        ["offline_bot_lines_heard"] = new(PassportDecision.StaysInWorld, "Per realm, not per character."),
        ["offline_bot_memory"] = new(PassportDecision.StaysInWorld, "What that world's gamebots remember about the character, by name. They remember it again on its next visit.", true),
        ["offline_bot_nemesis"] = new(PassportDecision.StaysInWorld, "That world's gamebots' grudges, by killer name.", true),
        ["offline_bot_ragequits"] = new(PassportDecision.StaysInWorld, "Gamebots only."),
        ["offline_bot_storylines"] = new(PassportDecision.StaysInWorld, "That world's gamebot stories; a player who took a side is named.", true),
        ["offline_bot_bonds"] = new(PassportDecision.StaysInWorld, "Bonds between that world's gamebots."),
        ["offline_server_firsts"] = new(PassportDecision.StaysInWorld, "Each world's own firsts."),
        ["offline_player_reputation"] = new(PassportDecision.StaysInWorld,
            "Deeds and nickname are what that world's gamebots say about the character, and the running server keeps them in memory, so the Desk could not write them safely.", true),
        ["passport_visits"] = new(PassportDecision.StaysInWorld, "A host's own list of the visitors it has and where each trip stands."),
        ["passport_trips"] = new(PassportDecision.StaysInWorld, "Every trip a host ever took in, so a travel file works only once there."),
        ["offline_lore_memory"] = new(PassportDecision.StaysInWorld,
            "Which lore lines the character heard in that world, by its id; the running server keeps them in memory.", true),
    };

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

/// <summary>A character visiting this world.</summary>
public sealed record VisitorInfo(string CharacterId, string Name, string HomeName, string TripId, string State);

/// <summary>A character of this save, as the launcher lists it.</summary>
public sealed record LocalCharacter(string Id, string Name, int Realm, int Level, string? AwayAt);

/// <summary>
/// The save a step works on. A plain runtime folder means a stopped world, checked and backed up as such. The server's
/// Passport Desk works on its own live database instead: the row changes are one short transaction on characters of an
/// account that is not logged in, and the server reads those at login.
/// </summary>
public sealed class PassportWorld
{
    const int BackupsKept = 6;

    string? _runtime;
    public string Database { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsLive => _runtime == null;

    public static PassportWorld Live(string database, string name) => new() { Database = database, Name = name };

    public static implicit operator PassportWorld(string runtime) => new()
    {
        _runtime = runtime,
        Database = Path.Combine(runtime, "data", "opendaoc.sqlite3.db"),
        // Living Server: no folder name in the passport (it can name the player). A live world gives its server name.
        Name = "",
    };

    internal string Open()
    {
        if (!File.Exists(Database))
            throw new PassportException("no_save", $"No save at {Database}.");
        if (_runtime == null)
            return Database;

        // A running server, however it was started, holds its own assemblies open.
        foreach (string file in new[] { Path.Combine("server", "CoreServer.dll"), Path.Combine("server", "lib", "GameServer.dll") })
        {
            string path = Path.Combine(_runtime, file);
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
        return Database;
    }

    /// <summary>The folder the step's backups go to: runtime\passport-backups (for the live Desk, beside its data folder).</summary>
    public string BackupRoot => Path.Combine(_runtime ?? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(Database)))!, "passport-backups");

    /// <summary>
    /// Living Server: every step that writes a save backs it up first, the live Desk too, and a failed backup stops the
    /// step before anything changes.
    /// </summary>
    internal void BackUp(string character, string step)
    {
        string root = BackupRoot;
        string folder = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{character}-{step}");
        Directory.CreateDirectory(folder);
        try
        {
            PassportDatabase.Backup(Database, Path.Combine(folder, "opendaoc.sqlite3.db"));
        }
        catch
        {
            Directory.Delete(folder, true);
            throw;
        }

        // Each one is a whole save, so only the newest few stay.
        foreach (string old in Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(BackupsKept))
            Directory.Delete(old, true);
    }
}

/// <summary>
/// One character's round trip between two saves. The character is playable in exactly one world:
/// its home rows stay in place, locked by the passport.away custom param, until the return passport
/// of the same trip comes back. Every method needs that world's server stopped.
/// </summary>
public static class PassportEngine
{
    public const string AwayKey = "passport.away", HostKey = "passport.host", GuildKey = "passport.guild";
    public const string PurseKey = "passport.purse", LeftKey = "passport.left_utc", CancelledKey = "passport.cancelled", VisitorKey = "passport.visitor";
    const string ProceedsPrefix = "RealmExchange.Proceeds.";
    const string VisitsTable = "passport_visits";
    /// <summary>
    /// Living Server: every trip this world ever took in. passport_visits keeps only a character's latest trip, so without
    /// this a kept copy of an older outbound passport would be taken in again once a later trip was over.
    /// </summary>
    const string TripsTable = "passport_trips";
    const string Visiting = "visiting", Departing = "departing", Returned = "returned";
    const string ConvertedPrefix = "passport#";
    const int SluaghbinderClass = 63, SluaghbinderNoviceClass = 16, Hibernia = 3;
    /// <summary>The game's own rule (CharacterCreateRequestHandler): a capital letter, then letters, at least 3. DAoC names are 20 at most.</summary>
    static readonly System.Text.RegularExpressions.Regex NameRule = new("^[A-Z][a-zA-Z]{2,19}$");
    const int SlotsPerRealm = 10;
    const int NoGuildRank = 9;

    /// <summary>Home: writes the passport and locks the character. Null purse carries the whole realm wallet.</summary>
    public static string Leave(PassportWorld world, string characterName, long? purseCopper, string hostLabel, string outFolder)
    {
        string database = world.Open();
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

        world.BackUp(character.Text("Name"), "out");
        string tripId = Guid.NewGuid().ToString();
        string path = Path.Combine(outFolder, PassportFile.FileName(character.Text("Name"), tripId, Manifest.Outbound));
        string partial = path + ".partial";
        Directory.CreateDirectory(outFolder);

        try
        {
            db.Transaction(() =>
            {
                PassportFile.Write(Pack(db, character, Manifest.Outbound, tripId, purse, world.Name), partial);
                SetWallet(db, account, realm, wallet - purse);
                SetParam(db, id, AwayKey, tripId);
                SetParam(db, id, HostKey, hostLabel.Length > 200 ? hostLabel[..200] : hostLabel);
                SetParam(db, id, GuildKey, $"{character.Text("GuildID")}|{character.Text("GuildRank")}");
                SetParam(db, id, PurseKey, purse.ToString(CultureInfo.InvariantCulture));
                SetParam(db, id, LeftKey, DateTime.UtcNow.ToString("O"));
                db.Exec("DELETE FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId=@id AND KeyName=@k", ("@id", id), ("@k", CancelledKey));
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
    public static ArrivalReport Arrive(PassportWorld world, string passportPath, string account, string? visitingName = null)
    {
        string database = world.Open();
        PassportData passport = PassportFile.Read(passportPath);
        Manifest manifest = passport.Manifest;
        if (manifest.Direction != Manifest.Outbound)
            throw new PassportException("wrong_direction", "This passport is a return passport. Use it at the character's home.");

        using var db = new PassportDatabase(database);
        if ((manifest.Class == SluaghbinderClass || (manifest.Class == SluaghbinderNoviceClass && manifest.Realm == Hibernia)) && !SluaghbinderOn(db))
            throw new PassportException("class", "This world has the Sluaghbinder class turned off, so a Sluaghbinder can't visit it.");
        Row accountRow = FindAccount(db, account);
        string accountName = accountRow.Text("Name");

        // A locked copy left by a trip whose confirm never got here is out of date: that trip went home.
        Row? stale = db.Rows("DOLCharacters", "DOLCharacters_ID=@id", ("@id", manifest.CharacterId)).SingleOrDefault();
        string? previousTrip = VisitTrip(db, manifest.CharacterId);
        if (stale != null && (VisitState(db, manifest.CharacterId) != Departing || previousTrip == manifest.TripId ||
                              !stale.Text("AccountName").Equals(accountName, StringComparison.OrdinalIgnoreCase)))
            throw new PassportException("already_here", $"{manifest.Name} is already in this world.");
        if (previousTrip == manifest.TripId || TripSeen(db, manifest.TripId))
            throw new PassportException("replayed", "This passport was already used. Start a new trip from home.");

        string name = string.IsNullOrWhiteSpace(visitingName) ? manifest.Name : visitingName.Trim();
        if (!NameRule.IsMatch(name))
            throw new PassportException("bad_name", $"{name} can't be a name here: a capital letter, then letters only, 3 to 20 in all.");
        if (!name.Equals(stale?.Text("Name"), StringComparison.OrdinalIgnoreCase) && NameTaken(db, name))
            throw new PassportException("name_taken", $"The name {name} is taken in this world. Choose a visiting name.");
        int slot = stale != null ? Convert.ToInt32(stale["AccountSlot"]) : FreeSlot(db, accountName, manifest.Realm);

        var notes = new List<string>();
        world.BackUp(name, "arrive");
        db.Transaction(() =>
        {
            // Inside the transaction, so a refused or failed arrival leaves even the schema as it was.
            EnsureVisits(db);
            if (stale != null)
            {
                RemoveCharacter(db, manifest.CharacterId);
                db.Exec("DELETE FROM DOLCharactersXCustomParam WHERE DOLCharactersObjectId=@id", ("@id", manifest.CharacterId));
            }

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
            SetParam(db, manifest.CharacterId, VisitorKey, manifest.TripId);
            if (Convert.ToInt64(accountRow["Realm"]) == 0)
                db.Exec("UPDATE Account SET Realm=@r WHERE Name=@a", ("@r", (long)manifest.Realm), ("@a", accountName));

            db.Exec($"INSERT INTO {TripsTable} (trip_id,character_id,arrived_utc) VALUES (@t,@id,@u)",
                ("@t", manifest.TripId), ("@id", manifest.CharacterId), ("@u", DateTime.UtcNow.ToString("O")));
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
    public static string Depart(PassportWorld world, string characterName, long? purseCopper, string outFolder)
    {
        string database = world.Open();
        using var db = new PassportDatabase(database);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        string account = character.Text("AccountName");
        int realm = Convert.ToInt32(character["Realm"]);

        List<Row> visits = !db.HasTable(VisitsTable) ? [] : db.Rows(VisitsTable, "character_id=@id", ("@id", id));
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
        if (state == Visiting)
            world.BackUp(character.Text("Name"), "depart");

        try
        {
            db.Transaction(() =>
            {
                PassportFile.Write(Pack(db, character, Manifest.Return, tripId, purse, world.Name), partial);
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
    public static void ConfirmDeparture(PassportWorld world, string characterName, string tripId)
    {
        string database = world.Open();
        using var db = new PassportDatabase(database);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        Row? visit = !db.HasTable(VisitsTable) ? null : db.Rows(VisitsTable, "character_id=@id", ("@id", id)).SingleOrDefault();
        if (visit == null || visit.Text("state") != Departing || visit.Text("trip_id") != tripId)
            throw new PassportException("no_departure", $"{character.Text("Name")} has no departure to confirm for trip {tripId}.");

        world.BackUp(character.Text("Name"), "left");
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
    public static IReadOnlyList<string> ComeHome(PassportWorld world, string passportPath)
    {
        string database = world.Open();
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

        world.BackUp(home.Text("Name"), "in");
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
    public static void Recall(PassportWorld world, string characterName)
    {
        string database = world.Open();
        using var db = new PassportDatabase(database);
        Row character = FindCharacter(db, characterName);
        string id = character.Text("DOLCharacters_ID");
        string trip = Param(db, id, AwayKey) ?? throw new PassportException("not_away", $"{character.Text("Name")} is not away.");
        if (VisitState(db, id) == Departing)
            throw new PassportException("is_visitor", $"{character.Text("Name")} is a visitor leaving this world. Recall it from its home save.");

        long purse = long.Parse(Param(db, id, PurseKey) ?? "0", CultureInfo.InvariantCulture);
        world.BackUp(character.Text("Name"), "recall");
        db.Transaction(() =>
        {
            AddToWallet(db, character.Text("AccountName"), Convert.ToInt32(character["Realm"]), purse);
            ClearTripParams(db, id);
            SetParam(db, id, CancelledKey, trip);
        });
    }

    /// <summary>Home or host: this account's characters and where the away ones are.</summary>
    public static IReadOnlyList<LocalCharacter> Characters(PassportWorld world, string account)
    {
        using var db = new PassportDatabase(world.Database, readOnly: true);
        return db.Rows("DOLCharacters", "AccountName=@a COLLATE NOCASE ORDER BY Name", ("@a", account))
            .Select(c =>
            {
                string id = c.Text("DOLCharacters_ID");
                string? away = Param(db, id, AwayKey) == null ? null : Param(db, id, HostKey) ?? "another world";
                return new LocalCharacter(id, c.Text("Name"), Convert.ToInt32(c["Realm"]), Convert.ToInt32(c["Level"]), away);
            })
            .ToList();
    }

    /// <summary>Host: the visitors of an account that have not gone home.</summary>
    public static IReadOnlyList<VisitorInfo> Visitors(PassportWorld world, string account)
    {
        using var db = new PassportDatabase(world.Database, readOnly: true);
        if (!db.HasTable(VisitsTable))
            return [];
        return db.Rows(VisitsTable, "account=@a COLLATE NOCASE AND state<>@s", ("@a", account), ("@s", Returned))
            .Select(v =>
            {
                string id = v.Text("character_id");
                string name = db.Rows("DOLCharacters", "DOLCharacters_ID=@id", ("@id", id)).SingleOrDefault()?.Text("Name") ?? v.Text("home_name");
                return new VisitorInfo(id, name, v.Text("home_name"), v.Text("trip_id"), v.Text("state"));
            })
            .ToList();
    }

    /// <summary>The host's switch, passport / passport_visitors (ServerProperties.PASSPORT_VISITORS): on unless turned off.</summary>
    public const string VisitorsProperty = "passport_visitors";

    public static bool VisitorsWelcome(PassportWorld world)
    {
        using var db = new PassportDatabase(world.Database, readOnly: true);
        if (!db.HasTable("ServerProperty"))
            return true;
        string? value = Convert.ToString(db.Scalar("SELECT Value FROM ServerProperty WHERE \"Key\"=@k COLLATE NOCASE", ("@k", VisitorsProperty)));
        return !bool.TryParse(value, out bool on) || on;
    }

    /// <summary>Host: turns visitors on or off with this world's server stopped, after a backup like every other write.</summary>
    public static void SetVisitorsWelcome(PassportWorld world, bool welcome)
    {
        string database = world.Open();
        using var db = new PassportDatabase(database);
        if (!db.HasTable("ServerProperty"))
            throw new PassportException("no_settings", "This world has no settings table yet. Start its server once first.");
        if (VisitorsWelcome(world) == welcome)
            return;

        string value = welcome ? "True" : "False";
        world.BackUp("host", "visitors");
        db.Transaction(() =>
        {
            if (db.Exec("UPDATE ServerProperty SET Value=@v WHERE \"Key\"=@k COLLATE NOCASE", ("@v", value), ("@k", VisitorsProperty)) == 0)
                db.Insert("ServerProperty", new Row
                {
                    ["ServerProperty_ID"] = VisitorsProperty, ["Category"] = "passport", ["Key"] = VisitorsProperty,
                    ["Description"] = "While hosting, let friends bring their own characters to this world (Character passport).",
                    ["DefaultValue"] = "True", ["Value"] = value, ["LastTimeRowUpdated"] = DateTime.UtcNow.ToString("O"),
                });
        });
    }

    /// <summary>Home: the trip a character is away on, if any.</summary>
    public static string? AwayTrip(PassportWorld world, string characterName)
    {
        using var db = new PassportDatabase(world.Database, readOnly: true);
        return Param(db, FindCharacter(db, characterName).Text("DOLCharacters_ID"), AwayKey);
    }

    static Row FindCharacter(PassportDatabase db, string name) =>
        db.Rows("DOLCharacters", "Name=@n COLLATE NOCASE OR DOLCharacters_ID=@n", ("@n", name)).SingleOrDefault()
        ?? throw new PassportException("no_character", $"No character named {name} in this save.");

    static Row FindAccount(PassportDatabase db, string name) =>
        db.Rows("Account", "Name=@n COLLATE NOCASE", ("@n", name)).SingleOrDefault()
        ?? throw new PassportException("no_account", $"No account {name} in this world. Log in here once first.");

    static PassportData Pack(PassportDatabase db, Row character, string direction, string tripId, long purse, string worldName)
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
                FromWorld = worldName,
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
        // Living Server: no account data beyond what the other world needs. The receiver sets its own account and slot;
        // home puts its own back from the frozen row (HomeOnlyColumns).
        packed["AccountName"] = "";
        packed["AccountSlot"] = 0L;
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
        (db.HasTable("offline_world_bots") && db.Count("SELECT count(*) FROM offline_world_bots WHERE Name=@n COLLATE NOCASE", ("@n", name)) > 0) ||
        (db.HasTable("bot_profiles") && db.Count("SELECT count(*) FROM bot_profiles WHERE Name=@n COLLATE NOCASE", ("@n", name)) > 0);

    /// <summary>classes / enable_sluaghbinder (ServerProperties.ENABLE_SLUAGHBINDER), on unless the world turned it off.</summary>
    static bool SluaghbinderOn(PassportDatabase db)
    {
        if (!db.HasTable("ServerProperty"))
            return true;
        string? value = Convert.ToString(db.Scalar("SELECT Value FROM ServerProperty WHERE \"Key\"='enable_sluaghbinder' COLLATE NOCASE"));
        return !bool.TryParse(value, out bool on) || on;
    }

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

    static void EnsureVisits(PassportDatabase db)
    {
        db.Exec($"CREATE TABLE IF NOT EXISTS {VisitsTable} (character_id TEXT PRIMARY KEY, account TEXT NOT NULL, trip_id TEXT NOT NULL, state TEXT NOT NULL, " +
            "arrived_utc TEXT NOT NULL, host_guild TEXT NOT NULL DEFAULT '', home_name TEXT NOT NULL, purse_copper BIGINT NOT NULL DEFAULT 0)");
        db.Exec($"CREATE TABLE IF NOT EXISTS {TripsTable} (trip_id TEXT PRIMARY KEY, character_id TEXT NOT NULL, arrived_utc TEXT NOT NULL)");
        // A world whose passport_visits predates passport_trips: its trips so far count as seen.
        db.Exec($"INSERT OR IGNORE INTO {TripsTable} (trip_id,character_id,arrived_utc) SELECT trip_id,character_id,arrived_utc FROM {VisitsTable}");
    }

    static bool TripSeen(PassportDatabase db, string tripId) =>
        db.HasTable(TripsTable) && db.Count($"SELECT count(*) FROM {TripsTable} WHERE trip_id=@t", ("@t", tripId)) > 0;

    static string? VisitState(PassportDatabase db, string id) =>
        db.HasTable(VisitsTable) ? Convert.ToString(db.Scalar($"SELECT state FROM {VisitsTable} WHERE character_id=@id", ("@id", id))) : null;

    static string? VisitTrip(PassportDatabase db, string id) => !db.HasTable(VisitsTable) ? null :
        Convert.ToString(db.Scalar($"SELECT trip_id FROM {VisitsTable} WHERE character_id=@id", ("@id", id)));

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
