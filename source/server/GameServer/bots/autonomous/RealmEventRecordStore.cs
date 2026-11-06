using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace DOL.GS;

// Shared with the launcher. This is a separate ledger, never the game database.
public sealed record RealmEventRecord(string Id, string EventId, string Name, string Kind, string Realm,
    string StartedUtc, string EndedUtc, string Phase, string Outcome, string Details, int Assigned, int Present);

public static class RealmEventRecordStore
{
    public const string FileName="realm-event-records.sqlite3";
    /// <summary>
    /// Living Server 1.1.2: the ledger lives beside the world database in runtime\data, so backups of that folder and
    /// progress transfers carry it. Earlier builds kept it in runtime\server (audit 2026-11-03).
    /// </summary>
    public static string PathIn(string dataFolder)=>Path.Combine(dataFolder,FileName);
    /// <summary>The ledger of a runtime folder: runtime\data, or runtime\server until the server has moved it there.</summary>
    public static string Locate(string runtime)
    {
        string data=PathIn(Path.Combine(runtime,"data")),legacy=Path.Combine(runtime,"server",FileName);
        return File.Exists(data)||!File.Exists(legacy)?data:legacy;
    }
    /// <summary>
    /// Moves the ledger an earlier build left at <paramref name="legacyPath"/> to <paramref name="path"/> (the server does
    /// it at start, before it writes). If both exist (an older build ran again after a rollback) the old file's events are
    /// added to the new one, none twice. Returns what it did: "none", "moved" or "merged N".
    /// </summary>
    public static string MoveLegacy(string legacyPath, string path)
    {
        if(!File.Exists(legacyPath)||string.Equals(Path.GetFullPath(legacyPath),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return "none";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Opening it once finishes or rolls back a write an unclean stop left in its journal, so the file moves whole.
        using(var settle=Open(legacyPath,false)){using var command=settle.CreateCommand();command.CommandText="PRAGMA journal_mode=DELETE";command.ExecuteScalar();}
        if(!File.Exists(path))
        {
            File.Move(legacyPath,path);
            return "moved";
        }
        long added;
        using(var connection=Open(path,false))
        {
            using(var schema=connection.CreateCommand()){schema.CommandText=Schema;schema.ExecuteNonQuery();}
            using(var attach=connection.CreateCommand()){attach.CommandText="ATTACH DATABASE @old AS old";attach.Parameters.AddWithValue("@old",legacyPath);attach.ExecuteNonQuery();}
            using(var command=connection.CreateCommand())
            {
                command.CommandText="SELECT count(*) FROM old.sqlite_master WHERE type='table' AND name='Events'";
                added=(long)command.ExecuteScalar()==0?0:-1;
                if(added<0){command.CommandText="INSERT OR IGNORE INTO main.Events SELECT * FROM old.Events";added=command.ExecuteNonQuery();}
                command.CommandText="DETACH DATABASE old";command.ExecuteNonQuery();
            }
        }
        foreach(string file in new[]{legacyPath,legacyPath+"-journal"})if(File.Exists(file))File.Delete(file);
        return $"merged {added}";
    }
    private const string Schema="CREATE TABLE IF NOT EXISTS Events(Id TEXT PRIMARY KEY, EventId TEXT NOT NULL, Name TEXT NOT NULL, Kind TEXT NOT NULL, Realm TEXT NOT NULL, StartedUtc TEXT NOT NULL, EndedUtc TEXT NOT NULL, Phase TEXT NOT NULL, Outcome TEXT NOT NULL, Details TEXT NOT NULL, Assigned INTEGER NOT NULL, Present INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS EventTime ON Events(StartedUtc DESC);";
    private static SQLiteConnection Open(string path, bool readOnly)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource=path, ReadOnly=readOnly,
            FailIfMissing=readOnly, Pooling=false, DefaultTimeout=5 }.ConnectionString);
        connection.Open();
        return connection;
    }
    public static void Save(string path, IEnumerable<RealmEventRecord> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var connection=Open(path,false);
        using (var schema=connection.CreateCommand())
        {
            schema.CommandText=Schema;
            schema.ExecuteNonQuery();
        }
        using var tx=connection.BeginTransaction();
        foreach(var row in rows)
        {
            using var command=connection.CreateCommand();command.Transaction=tx;
            command.CommandText="INSERT INTO Events VALUES(@id,@event,@name,@kind,@realm,@start,@end,@phase,@outcome,@details,@assigned,@present) ON CONFLICT(Id) DO UPDATE SET EndedUtc=excluded.EndedUtc,Phase=excluded.Phase,Outcome=excluded.Outcome,Details=excluded.Details,Assigned=excluded.Assigned,Present=excluded.Present";
            foreach(var pair in new (string,object)[]{("id",row.Id),("event",row.EventId),("name",row.Name),("kind",row.Kind),("realm",row.Realm),("start",row.StartedUtc),("end",row.EndedUtc),("phase",row.Phase),("outcome",row.Outcome),("details",row.Details),("assigned",row.Assigned),("present",row.Present)}) command.Parameters.AddWithValue("@"+pair.Item1,pair.Item2);
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public static void CloseInterrupted(string path)
    {
        if(!File.Exists(path))return;
        using var connection=Open(path,false);using var command=connection.CreateCommand();
        // The exact end time is unknown after an unclean stop; do not invent it.
        command.CommandText="UPDATE Events SET Outcome='Interrupted',Phase='Stopped',Details=Details || ' | Previous server session ended without a final outcome; end time unknown.' WHERE Outcome='In progress'";
        command.ExecuteNonQuery();
    }
    // Launcher "Delete all records": empties this ledger file only. No game data lives here.
    // An event still running is written again by the server at its next save.
    public static int DeleteAll(string path)
    {
        if(!File.Exists(path))return 0;
        using var connection=Open(path,false);using var command=connection.CreateCommand();
        command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Events'";
        if((long)command.ExecuteScalar()==0)return 0;
        command.CommandText="DELETE FROM Events";
        return command.ExecuteNonQuery();
    }
    /// <summary>
    /// Details of a raid the player started from the launcher (a forced raid). Its result is not part of the encounter's
    /// record for raid sizing or the Epic Special Forces (owner, 2026-11-04). Rows written before this marker existed
    /// have none and count as automatic.
    /// </summary>
    public const string ForcedRaidPrefix="Forced raid: ";
    /// <summary>
    /// The end of a raid's details when the Epic Special Forces joined it (owner, 2026-11-04: a win they helped makes their
    /// next call-up at that encounter a party smaller). A row without it (every row before this marker) counts as a raid
    /// they did not join.
    /// </summary>
    public const string SpecialForcesMarker=" Epic Special Forces joined: ";
    public static string WithSpecialForces(string details,int joined)=>joined>0?$"{details}{SpecialForcesMarker}{joined}.":details;
    /// <summary>How many Epic Special Forces joined the raid these details describe (0 when the details don't say).</summary>
    public static int SpecialForcesIn(string details)
    {
        int at=details?.LastIndexOf(SpecialForcesMarker,StringComparison.Ordinal)??-1;
        if(at<0)return 0;
        int start=at+SpecialForcesMarker.Length,end=start;
        while(end<details.Length&&char.IsDigit(details[end]))end++;
        return end>start&&int.TryParse(details.AsSpan(start,end-start),out int joined)?joined:0;
    }
    /// <summary>The end of a sized raid's details: the size it ran at, as a percentage of its period size (2026-11-05). The
    /// next raid at that encounter carries on from it; a row without it (every row before) is replayed as before.</summary>
    public const string RaidSizeMarker=" Raid size: ";
    public static string WithRaidSize(string details,double? percent)=>percent is double p?$"{details}{RaidSizeMarker}{p.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)}%.":details;
    /// <summary>The size these details say the raid ran at, or null.</summary>
    public static double? RaidSizeIn(string details)
    {
        int at=details?.LastIndexOf(RaidSizeMarker,StringComparison.Ordinal)??-1;
        if(at<0)return null;
        int start=at+RaidSizeMarker.Length,end=start;
        while(end<details.Length&&(char.IsDigit(details[end])||details[end]=='.'))end++;
        return end>start&&end<details.Length&&details[end]=='%'&&double.TryParse(details.AsSpan(start,end-start),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double p)?p:null;
    }
    /// <summary>The end of a raid's details: how many wipes it took (2026-11-05). A row without it (every row before) reads
    /// as before: its time-out counts as a loss.</summary>
    public const string WipesMarker=" Wipes: ";
    public static string WithWipes(string details,int wipes)=>$"{details}{WipesMarker}{Math.Max(0,wipes)}.";
    /// <summary>The wipes these details say the raid took, or null when they don't say.</summary>
    public static int? WipesIn(string details)
    {
        int at=details?.LastIndexOf(WipesMarker,StringComparison.Ordinal)??-1;
        if(at<0)return null;
        int start=at+WipesMarker.Length,end=start;
        while(end<details.Length&&char.IsDigit(details[end]))end++;
        return end>start&&int.TryParse(details.AsSpan(start,end-start),out int wipes)?wipes:null;
    }
    /// <summary>The outcome a row gives the raid record: a time-out its details say took no wipe is
    /// <see cref="TimedOutWithoutWipe"/> (no result); anything else as written.</summary>
    public static string RecordOutcome(string outcome,string details)=>WipesIn(details) is int wipes?RecordOutcome(outcome,wipes):outcome;
    /// <summary>
    /// The raid record's name for a time-out with no wipe (owner, 2026-11-05): neither a win nor a loss, for the raid's size
    /// and the Special Forces alike. Here, not in AutonomousEpicSpecialForces: the launcher links this file alone.
    /// </summary>
    public const string TimedOutWithoutWipe="Timed out without a wipe";
    /// <summary>The outcome a raid's record keeps: a time-out after one or more wipes is a loss; without one it is no result.</summary>
    public static string RecordOutcome(string outcome,int wipes)=>outcome=="Timed out"&&wipes<=0?TimedOutWithoutWipe:outcome;
    /// <summary>An event's last <paramref name="limit"/> finished runs, oldest first: each outcome as the raid record reads it (<see cref="RecordOutcome"/>), how many Epic Special
    /// Forces joined it (<see cref="SpecialForcesIn"/>) and the size it ran at (<see cref="RaidSizeIn"/>);
    /// <paramref name="automaticOnly"/> leaves out forced raids.</summary>
    public static List<(string Outcome,int SpecialForces,double? SizePercent)> Results(string path,string eventId,int limit,bool automaticOnly=false)
    {
        var results=new List<(string,int,double?)>();
        if(!File.Exists(path))return results;
        using var connection=Open(path,true);using var command=connection.CreateCommand();
        command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Events'";
        if((long)command.ExecuteScalar()==0)return results;
        command.CommandText="SELECT Outcome,Details FROM Events WHERE EventId=@event AND EndedUtc<>''"+(automaticOnly?" AND substr(Details,1,length(@forced))<>@forced":"")+" ORDER BY StartedUtc DESC,Id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@event",eventId);command.Parameters.AddWithValue("@limit",limit);command.Parameters.AddWithValue("@forced",ForcedRaidPrefix);
        using var reader=command.ExecuteReader();
        while(reader.Read())results.Add((RecordOutcome(reader.GetString(0),reader.GetString(1)),SpecialForcesIn(reader.GetString(1)),RaidSizeIn(reader.GetString(1))));
        results.Reverse();
        return results;
    }
    /// <summary>The outcomes of an event's last <paramref name="limit"/> finished runs, oldest first;
    /// <paramref name="automaticOnly"/> leaves out forced raids (<see cref="ForcedRaidPrefix"/>).</summary>
    public static List<string> Outcomes(string path,string eventId,int limit,bool automaticOnly=false)
    {
        var outcomes=new List<string>();
        if(!File.Exists(path))return outcomes;
        using var connection=Open(path,true);using var command=connection.CreateCommand();
        command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Events'";
        if((long)command.ExecuteScalar()==0)return outcomes;
        command.CommandText="SELECT Outcome FROM Events WHERE EventId=@event AND EndedUtc<>''"+(automaticOnly?" AND substr(Details,1,length(@forced))<>@forced":"")+" ORDER BY StartedUtc DESC,Id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@event",eventId);command.Parameters.AddWithValue("@limit",limit);command.Parameters.AddWithValue("@forced",ForcedRaidPrefix);
        using var reader=command.ExecuteReader();
        while(reader.Read())outcomes.Add(reader.GetString(0));
        outcomes.Reverse();
        return outcomes;
    }
    /// <summary>Details of a keep siege the bots answered because a player attacked (not a bot siege; SiegeDefenseBalance skips it).</summary>
    public const string PlayerAttackPrefix="Player attack: ";
    /// <summary>
    /// The last <paramref name="limit"/> finished bot keep sieges, oldest first: true when the keep was captured, false when
    /// it was defended. Rally failures, superseded rallies and sieges answering a player's attack are not results.
    /// </summary>
    public static List<bool> SiegeResults(string path,int limit)
    {
        var results=new List<bool>();
        if(!File.Exists(path))return results;
        using var connection=Open(path,true);using var command=connection.CreateCommand();
        command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Events'";
        if((long)command.ExecuteScalar()==0)return results;
        command.CommandText="SELECT Outcome FROM Events WHERE Kind IN ('Keep','Relic keep') AND Outcome IN ('Captured','Defended','Defended (timeout)') AND substr(Details,1,length(@player))<>@player AND EndedUtc<>'' ORDER BY EndedUtc DESC,Id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@player",PlayerAttackPrefix);command.Parameters.AddWithValue("@limit",limit);
        using var reader=command.ExecuteReader();
        while(reader.Read())results.Add(reader.GetString(0)=="Captured");
        results.Reverse();
        return results;
    }
    public static (List<RealmEventRecord> Rows,long Total) Read(string path,string realm,string kind,string outcome,string search,int page,int pageSize=100)
    {
        if(!File.Exists(path))return (new(),0);
        if(page<0 || pageSize<1 || pageSize>500)throw new ArgumentOutOfRangeException(nameof(page));
        using var connection=Open(path,true);
        const string where=" WHERE (@realm='' OR Realm=@realm) AND (@kind='' OR Kind=@kind) AND (@outcome='' OR Outcome=@outcome) AND (@search='' OR instr(lower(Name || ' ' || Details || ' ' || EventId),lower(@search))>0)";
        using var command=connection.CreateCommand();
        command.Parameters.AddWithValue("@realm",realm);command.Parameters.AddWithValue("@kind",kind);command.Parameters.AddWithValue("@outcome",outcome);command.Parameters.AddWithValue("@search",search);
        command.CommandText="SELECT count(*) FROM Events"+where;
        long count=(long)command.ExecuteScalar();
        command.CommandText="SELECT * FROM Events"+where+" ORDER BY COALESCE(NULLIF(StartedUtc,''),EndedUtc) DESC,Id DESC LIMIT @limit OFFSET @offset";
        command.Parameters.AddWithValue("@limit",pageSize);command.Parameters.AddWithValue("@offset",(long)page*pageSize);
        using var reader=command.ExecuteReader();var rows=new List<RealmEventRecord>();
        while(reader.Read())rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetInt32(10),reader.GetInt32(11)));
        return(rows,count);
    }
}
