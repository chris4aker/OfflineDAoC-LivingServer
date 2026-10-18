using System.Data.SQLite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.ProgressImport;

public sealed record Policy(string[] ProgressTables, string[] ClearTables);

/// <summary>What an old folder contains, shown before anything is changed.</summary>
public sealed record ImportSummary(long Accounts, long Characters, long Bots, long InventoryItems)
{
    public string Version { get; init; } = "unknown";
    public bool SluaghbinderClient { get; init; }
    public long SluaghbinderCharacters { get; init; }
    public long SluaghbinderBots { get; init; }
    public bool HasCredentials { get; init; }
}

/// <summary>
/// Moves saved progress (account, characters, items, money, houses, guild state, bots) from any
/// earlier Offline DAoC folder — v0.3, v0.31, v0.31b, v0.32, v0.32b, v0.33, v0.33b, v0.34, v0.34b, v0.35, v0.35b, the "new class
/// test" builds — into a NEW 0.36 folder. The old folder is only read. The new folder keeps its own
/// world, rules, edition and launcher settings; only the saved-progress tables are replaced, after a
/// backup.
/// </summary>
public static class ImportEngine
{
    /// <summary>This release; the "b" edition has the custom class, the plain one does not.</summary>
    public const string Release = "0.36";
    public static readonly Policy Rules = JsonSerializer.Deserialize<Policy>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"progress-policy.json")))!;
    const int SluaghbinderClass = 63, AcolyteClass = 16, Hibernia = 3;
    // The Sluaghbinder client of 0.33b/0.34b, and of 0.35b / 0.36b (classic war map, red quest markers, QUEST GUIDE button).
    static readonly string[] SluaghbinderClientSha256 = ["01b1848e79b31d2822811effb3d3b098e07015db2d3db1178df5ba31ed805e96",
        "e1d471bb19108610ab9c8ca77dd41af40afe716685b03f4cdeda88c05678463b"];

    public static string LocateRuntime(string folder)
    {
        folder = Path.GetFullPath(folder);
        foreach (string candidate in new[] {Path.Combine(folder,"runtime"),folder})
            if (File.Exists(Path.Combine(candidate,"data","opendaoc.sqlite3.db"))) return candidate;
        throw new InvalidOperationException("Choose the old Offline DAoC folder (or its runtime folder). No data/opendaoc.sqlite3.db was found.");
    }
    static string Q(string value) => "\"" + value.Replace("\"","\"\"") + "\"";
    static SQLiteConnection Open(string path, bool readOnly)
    {
        var builder = new SQLiteConnectionStringBuilder {DataSource=path,ReadOnly=readOnly,FailIfMissing=true,Pooling=false,DefaultTimeout=10};
        var c = new SQLiteConnection(builder.ConnectionString); c.Open(); return c;
    }
    static long Scalar(SQLiteConnection c,string sql)
    { using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar()); }
    static void Exec(SQLiteConnection c,string sql)
    { using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery(); }
    static HashSet<string> Tables(SQLiteConnection c,string schema="main")
    {
        using var q=c.CreateCommand();q.CommandText=$"SELECT name FROM {schema}.sqlite_master WHERE type='table'";
        using var r=q.ExecuteReader();var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(r.Read())result.Add(r.GetString(0));return result;
    }
    static List<string> Columns(SQLiteConnection c,string table,string schema="main")
    {
        using var q=c.CreateCommand();q.CommandText=$"PRAGMA {schema}.table_info({Q(table)})";
        using var r=q.ExecuteReader();var result=new List<string>();while(r.Read())result.Add(r.GetString(1));return result;
    }
    // 0.36c: the new client's launcher (new-client\runtime) shares the game's runtime, so it must be closed too.
    static string[] WithNewClient(params string[] runtimes) =>
        runtimes.SelectMany(r=>new[]{r,Path.GetFullPath(Path.Combine(r,"..","new-client","runtime"))}).ToArray();
    static void CheckClosed(params string[] roots)
    {
        if(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==10300))
            throw new InvalidOperationException("A local DAoC server is listening. Stop it before importing progress.");
        foreach(var process in Process.GetProcesses())
        using(process)
        {
            if (process.Id==Environment.ProcessId)continue;
            string name=process.ProcessName;
            if(name.Equals("CoreServer",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stop the DAoC server before importing progress.");
            if(name is not ("OfflineDAoC" or "game" or "camelot" or "connect" or "Godot_v4.7.2-stable_mono_win64"))continue;
            try
            {
                string? file=process.MainModule?.FileName;
                if(file!=null && roots.Any(root=>file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Close the old and new launchers and game clients before importing.");
            }
            catch(System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Cannot verify that the launcher/client is closed. Close it before importing."); }
        }
    }
    static void Backup(string source,string destination)
    {
        using var from=Open(source,true);
        using var to=new SQLiteConnection(new SQLiteConnectionStringBuilder {DataSource=destination,Pooling=false}.ConnectionString);
        to.Open();from.BackupDatabase(to,"main","main",-1,null,0);
    }
    static void Integrity(SQLiteConnection c)
    {
        using var q=c.CreateCommand();q.CommandText="PRAGMA quick_check";
        if(!string.Equals(Convert.ToString(q.ExecuteScalar()),"ok",StringComparison.Ordinal))throw new InvalidDataException("Database integrity check failed.");
    }

    /// <summary>The launcher's "VERSION x" label, plus "b" style edition detection from the client.</summary>
    public static string DetectVersion(string runtime, out bool sluaghbinderClient)
    {
        string game=Path.Combine(runtime,"client-opendaoc","app","game.dll");
        sluaghbinderClient=File.Exists(game) && SluaghbinderClientSha256.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(game))),StringComparer.OrdinalIgnoreCase);
        string launcher=Path.Combine(runtime,"OfflineDAoC.dll");
        if(!File.Exists(launcher))return "unknown";
        byte[] data=File.ReadAllBytes(launcher);byte[] marker=Encoding.Unicode.GetBytes("VERSION ");
        var labels=new List<string>();
        for(int at=data.AsSpan().IndexOf(marker);at>=0;)
        {
            var text=new StringBuilder();
            for(int i=at+marker.Length;i+1<data.Length && text.Length<8;i+=2)
            {
                char ch=(char)(data[i]|data[i+1]<<8);
                if(!(char.IsDigit(ch)||ch=='.'||char.IsLetter(ch)))break;
                text.Append(ch);
            }
            if(text.Length>0 && !labels.Contains(text.ToString()))labels.Add(text.ToString());
            int next=data.AsSpan(at+marker.Length).IndexOf(marker);
            at=next<0?-1:at+marker.Length+next;
        }
        if(labels.Count==0)return "unknown";
        // Since 0.34 one launcher carries both labels (e.g. "0.35b" and "0.35"); the client decides.
        string plain=labels.FirstOrDefault(l=>labels.Contains(l+"b"))??"";
        if(plain.Length>0)return sluaghbinderClient?plain+"b":plain;
        return labels[0];
    }

    static (string? Account,string? Password) ReadCredentials(string runtime)
    {
        string path=Path.Combine(runtime,"account.txt");
        if(!File.Exists(path))return (null,null);
        var values=File.ReadLines(path).Select(l=>l.Split(':',2)).Where(p=>p.Length==2)
            .ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
        values.TryGetValue("Account",out var account);values.TryGetValue("Password",out var password);
        return string.IsNullOrWhiteSpace(account)||string.IsNullOrEmpty(password)?(null,null):(account,password);
    }

    /// <summary>The server's stored form of a password (DOL "##" + MD5 of the UTF-16BE characters).</summary>
    public static string HashPassword(string password)
    {
        var bytes=new byte[password.Length*2];
        for(int i=0;i<password.Length;i++){bytes[i*2]=(byte)(password[i]>>8);bytes[i*2+1]=(byte)password[i];}
        return "##"+string.Concat(MD5.HashData(bytes).Select(v=>v.ToString("X",System.Globalization.CultureInfo.InvariantCulture)));
    }

    static bool SluaghbinderEnabled(SQLiteConnection c,string schema="main")
    {
        if(!Tables(c,schema).Contains("ServerProperty"))return true;
        using var q=c.CreateCommand();q.CommandText=$"SELECT Value FROM {schema}.ServerProperty WHERE `Key`='enable_sluaghbinder' LIMIT 1";
        return q.ExecuteScalar() is not string text || !text.Trim().Equals("false",StringComparison.OrdinalIgnoreCase);
    }

    public static bool DestinationAllowsSluaghbinder(string newFolder)
    {
        using var c=Open(Path.Combine(LocateRuntime(newFolder),"data","opendaoc.sqlite3.db"),true);
        return SluaghbinderEnabled(c);
    }

    public static ImportSummary Inspect(string folder)
    {
        string runtime=LocateRuntime(folder);
        using var c=Open(Path.Combine(runtime,"data","opendaoc.sqlite3.db"),true);
        var tables=Tables(c);
        foreach(var t in new[]{"Account","DOLCharacters","offline_world_bots","Inventory","ItemUnique","ItemTemplate"})
            if(!tables.Contains(t))throw new InvalidDataException($"This old version is missing {t}. Import has not changed anything; it needs a compatible Offline DAoC database.");
        string version=DetectVersion(runtime,out bool client);
        return new(Scalar(c,"SELECT count(*) FROM Account"),Scalar(c,"SELECT count(*) FROM DOLCharacters"),
            Scalar(c,"SELECT count(*) FROM offline_world_bots"),Scalar(c,"SELECT count(*) FROM Inventory"))
        {
            Version=version,SluaghbinderClient=client,
            SluaghbinderCharacters=Scalar(c,$"SELECT count(*) FROM DOLCharacters WHERE Class={SluaghbinderClass} OR (Class={AcolyteClass} AND Realm={Hibernia})"),
            SluaghbinderBots=Scalar(c,$"SELECT count(*) FROM offline_world_bots WHERE ClassId={SluaghbinderClass}"),
            HasCredentials=ReadCredentials(runtime).Account!=null,
        };
    }

    /// <param name="leaveSluaghbinderBotsBehind">Only for a "no custom class" destination:
    /// autonomous Sluaghbinder bots (and the items they carry) are not transferred.</param>
    public static string Import(string oldFolder,string newFolder,Action<string> progress,bool leaveSluaghbinderBotsBehind=false)
    {
        string old=LocateRuntime(oldFolder),current=LocateRuntime(newFolder);
        if(string.Equals(old,current,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Old and new folders must be different.");
        CheckClosed(WithNewClient(old,current));
        string oldDb=Path.Combine(old,"data","opendaoc.sqlite3.db"),newDb=Path.Combine(current,"data","opendaoc.sqlite3.db");
        var summary=Inspect(old);
        var notes=new List<string>{$"Source: {old} (launcher version {summary.Version}{(summary.SluaghbinderClient?", Sluaghbinder client":"")})."};

        bool customClass;
        using(var destination=Open(newDb,true))customClass=SluaghbinderEnabled(destination);
        if(!customClass && summary.SluaghbinderCharacters>0)
            throw new InvalidDataException($"The old save has {summary.SluaghbinderCharacters} Sluaghbinder character(s). This is the {Release} edition without the custom class. " +
                $"Install {Release}b (with the Sluaghbinder) and import there instead. Nothing has been changed.");
        bool dropBots=!customClass && summary.SluaghbinderBots>0;
        if(dropBots && !leaveSluaghbinderBotsBehind)
            throw new InvalidDataException($"The old save has {summary.SluaghbinderBots} autonomous Sluaghbinder bot(s), but this is the {Release} edition without the custom class. " +
                $"Import into {Release}b to keep them, or confirm that they (and the items they carry) stay behind. Nothing has been changed.");

        // Account: keep the old login when account.txt is present; otherwise keep the old account
        // with a fresh password; a save with no account at all moves bots and world progress only.
        var (account,password)=ReadCredentials(old);
        bool keepNewCredentials=false;
        using(var source=Open(oldDb,true))
        {
            long accounts=Scalar(source,"SELECT count(*) FROM Account");
            if(account!=null)
            {
                using var q=source.CreateCommand();q.CommandText="SELECT count(*) FROM Account WHERE Name=@name";q.Parameters.AddWithValue("@name",account);
                if(Convert.ToInt64(q.ExecuteScalar())!=1)
                {
                    if(accounts!=1)throw new InvalidDataException("The old account.txt does not name an account in the old database, and the old save has several accounts. Restore the matching account.txt first.");
                    account=null;
                }
            }
            if(account==null && accounts==1)
            {
                using var q=source.CreateCommand();q.CommandText="SELECT Name FROM Account LIMIT 1";
                account=Convert.ToString(q.ExecuteScalar());
                password=Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
                notes.Add($"The old account.txt was missing or did not match; account '{account}' was kept with a new password (see account.txt).");
            }
            else if(account==null && accounts==0){keepNewCredentials=true;notes.Add("The old save had no player account; this folder keeps its own new account.");}
            else if(account==null)throw new InvalidDataException("The old runtime/account.txt is missing and the old save has several accounts. Restore account.txt first.");
        }

        using var exclusive=new FileStream(Path.Combine(current,"data","progress-import.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string backup=Path.Combine(current,"progress-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(backup);
        string staged=Path.Combine(backup,"prepared.db"),snapshot=Path.Combine(backup,"source-snapshot.db"),credentialStage=Path.Combine(backup,"prepared-account.txt");
        progress("Backing up the new folder and taking a consistent, read-only snapshot of the old progress…");
        Backup(newDb,Path.Combine(backup,"opendaoc.sqlite3.db"));
        if(File.Exists(Path.Combine(current,"account.txt")))File.Copy(Path.Combine(current,"account.txt"),Path.Combine(backup,"account.txt"));
        Backup(oldDb,snapshot);File.Copy(Path.Combine(backup,"opendaoc.sqlite3.db"),staged);
        bool swapped=false;
        long existingMissing=0;
        try
        {
            using(var c=Open(staged,false))
            {
                Exec(c,"PRAGMA foreign_keys=OFF; PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;");
                using(var attach=c.CreateCommand()){attach.CommandText="ATTACH DATABASE @path AS old";attach.Parameters.AddWithValue("@path",snapshot);attach.ExecuteNonQuery();}
                var sourceTables=Tables(c,"old");var targetTables=Tables(c);
                foreach(string unknown in sourceTables.Except(targetTables,StringComparer.OrdinalIgnoreCase).Where(t=>!t.StartsWith("sqlite_",StringComparison.OrdinalIgnoreCase)))
                    if(Scalar(c,$"SELECT count(*) FROM old.{Q(unknown)}")>0)
                        notes.Add($"Not transferred: old table {unknown} is not used by {Release} ({Scalar(c,$"SELECT count(*) FROM old.{Q(unknown)}")} rows).");
                Exec(c,"BEGIN IMMEDIATE");
                foreach(string table in Rules.ProgressTables)
                {
                    if(!targetTables.Contains(table))throw new InvalidDataException($"New version is missing progress table {table}.");
                    Exec(c,$"DELETE FROM main.{Q(table)}");
                    if(!sourceTables.Contains(table))continue;
                    var oldColumns=Columns(c,table,"old");var newColumns=Columns(c,table);
                    var shared=oldColumns.Where(column=>newColumns.Contains(column,StringComparer.OrdinalIgnoreCase)).ToList();
                    var dropped=oldColumns.Except(shared,StringComparer.OrdinalIgnoreCase).ToList();
                    if(dropped.Count>0)notes.Add($"{table}: old-only column(s) {string.Join(", ",dropped)} are not used by {Release} and were left behind.");
                    string fields=string.Join(",",shared.Select(Q));
                    progress($"Transferring {table}…");
                    Exec(c,$"INSERT INTO main.{Q(table)} ({fields}) SELECT {fields} FROM old.{Q(table)}");
                    if(Scalar(c,$"SELECT count(*) FROM main.{Q(table)}")!=Scalar(c,$"SELECT count(*) FROM old.{Q(table)}"))
                        throw new InvalidDataException($"Row verification failed for {table}.");
                    if(Scalar(c,$"SELECT count(*) FROM (SELECT {fields} FROM old.{Q(table)} EXCEPT SELECT {fields} FROM main.{Q(table)})")!=0)
                        throw new InvalidDataException($"Content verification failed for {table}.");
                }
                if(dropBots)
                {
                    // Only autonomous bots of the disabled class; their carried items go with them.
                    progress($"Leaving the Sluaghbinder bots behind ({Release} edition without the custom class)…");
                    string owners=$"SELECT 'offlinebot:'||BotId FROM main.offline_world_bots WHERE ClassId={SluaghbinderClass}";
                    long items=Scalar(c,$"SELECT count(*) FROM main.Inventory WHERE OwnerID IN ({owners})");
                    Exec(c,$"CREATE TEMP TABLE dropped_unique AS SELECT DISTINCT UTemplate_Id AS Id FROM main.Inventory WHERE OwnerID IN ({owners}) AND COALESCE(UTemplate_Id,'')<>''");
                    Exec(c,$"DELETE FROM main.Inventory WHERE OwnerID IN ({owners})");
                    Exec(c,"DELETE FROM main.ItemUnique WHERE Id_nb IN (SELECT Id FROM dropped_unique) AND Id_nb NOT IN (SELECT UTemplate_Id FROM main.Inventory WHERE COALESCE(UTemplate_Id,'')<>'')");
                    long bots=Scalar(c,$"SELECT count(*) FROM main.offline_world_bots WHERE ClassId={SluaghbinderClass}");
                    Exec(c,$"DELETE FROM main.offline_world_bots WHERE ClassId={SluaghbinderClass}; DROP TABLE dropped_unique;");
                    notes.Add($"Left behind by choice: {bots} autonomous Sluaghbinder bot(s) and the {items} item(s) they carried ({Release} has no custom class).");
                }
                // Keep updated definitions on ID collisions; preserve old custom
                // templates that are absent from the new world for real owned items.
                var templateColumns=Columns(c,"ItemTemplate","old").Where(column=>Columns(c,"ItemTemplate").Contains(column,StringComparer.OrdinalIgnoreCase)).ToList();
                string templateFields=string.Join(",",templateColumns.Select(Q));
                Exec(c,$"INSERT OR IGNORE INTO main.ItemTemplate ({templateFields}) SELECT {templateFields} FROM old.ItemTemplate");
                if(sourceTables.Contains("DBHouse"))
                {
                    Exec(c,"UPDATE main.DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' WHERE COALESCE(OwnerID,'')<>''");
                    var fields=Columns(c,"DBHouse").Intersect(Columns(c,"DBHouse","old"),StringComparer.OrdinalIgnoreCase)
                        .Except(new[]{"HouseNumber","X","Y","Z","RegionID","Heading","DBHouse_ID"},StringComparer.OrdinalIgnoreCase).ToArray();
                    Exec(c,"UPDATE main.DBHouse SET "+string.Join(",",fields.Select(f=>$"{Q(f)}=(SELECT o.{Q(f)} FROM old.DBHouse o WHERE o.HouseNumber=main.DBHouse.HouseNumber)"))+
                        " WHERE HouseNumber IN (SELECT HouseNumber FROM old.DBHouse WHERE COALESCE(OwnerID,'')<>'')");
                }
                foreach(string table in Rules.ClearTables)if(targetTables.Contains(table))Exec(c,$"DELETE FROM {Q(table)}");
                if(!keepNewCredentials && password!=null && ReadCredentials(old).Account==null)
                {
                    using var set=c.CreateCommand();set.CommandText="UPDATE Account SET Password=@hash WHERE Name=@name";
                    set.Parameters.AddWithValue("@hash",HashPassword(password));set.Parameters.AddWithValue("@name",account);
                    if(set.ExecuteNonQuery()!=1)throw new InvalidDataException("Could not set the kept account's new password.");
                }
                Exec(c,"UPDATE Account SET PrivLevel=1; INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false'); UPDATE ServerProperty SET Value='1' WHERE lower(Key) IN ('xp_rate','bot_xp_rate'); UPDATE ServerProperty SET Value='' WHERE lower(Key) IN ('xp_rate_by_level','bot_xp_rate_by_level'); UPDATE offline_world_bots SET IsOnline=0;");
                Exec(c,"UPDATE offline_population_settings SET Value=CAST((SELECT count(*) FROM offline_world_bots WHERE IsRetired=0) AS TEXT) WHERE Key='ActiveTarget'; UPDATE offline_population_settings SET Value='true' WHERE Key='PopulationEnabled';");
                string Missing(string schema) => $"SELECT i.Inventory_ID FROM {schema}.Inventory i WHERE (COALESCE(i.UTemplate_Id,'')<>'' AND NOT EXISTS(SELECT 1 FROM {schema}.ItemUnique u WHERE u.Id_nb=i.UTemplate_Id)) OR (COALESCE(i.UTemplate_Id,'')='' AND COALESCE(i.ITemplate_Id,'')<>'' AND NOT EXISTS(SELECT 1 FROM {schema}.ItemTemplate t WHERE t.Id_nb=i.ITemplate_Id))";
                existingMissing=Scalar(c,$"SELECT count(*) FROM ({Missing("old")})");
                if(Scalar(c,$"SELECT count(*) FROM ({Missing("main")} EXCEPT {Missing("old")})")!=0)
                    throw new InvalidDataException("Import introduced an unresolved inventory template. No progress has been installed.");
                if(existingMissing>0)progress($"Note: preserving {existingMissing} already-unresolved inventory references from the old save; no items are deleted or invented.");
                Exec(c,"COMMIT; DETACH DATABASE old;");Integrity(c);
            }
            if(!keepNewCredentials)File.WriteAllText(credentialStage,$"Account: {account}\r\nPassword: {password}\r\n");
            CheckClosed(WithNewClient(old,current));
            progress("Installing verified progress. Please do not close this window…");
            using(var c=Open(newDb,false))Exec(c,"PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
            File.Replace(staged,newDb,null);swapped=true;
            string targetCredentials=Path.Combine(current,"account.txt");
            if(!keepNewCredentials)
            {
                if(File.Exists(targetCredentials))File.Replace(credentialStage,targetCredentials,null);else File.Move(credentialStage,targetCredentials);
            }
            using(var verified=Open(newDb,true))Integrity(verified);
            File.Delete(snapshot);
            File.WriteAllText(Path.Combine(backup,"IMPORT RESULT.txt"),"Import completed. Previous destination database and account.txt are rollback copies. Original old folder was not changed. XP=1x; GM off.\r\n"+
                string.Join("\r\n",notes)+"\r\n"+
                (existingMissing>0?$"Source-save warning: {existingMissing} inventory references already lacked item definitions in the old database. They were preserved unchanged; this import did not create those missing definitions.\r\n":""));
            return backup;
        }
        catch
        {
            if(swapped)
            {
                string restore=Path.Combine(backup,"restore.db");File.Copy(Path.Combine(backup,"opendaoc.sqlite3.db"),restore);File.Replace(restore,newDb,null);
                if(File.Exists(Path.Combine(backup,"account.txt")))File.Copy(Path.Combine(backup,"account.txt"),Path.Combine(current,"account.txt"),true);
                else if(File.Exists(Path.Combine(current,"account.txt")))File.Delete(Path.Combine(current,"account.txt"));
            }
            throw;
        }
    }
}
