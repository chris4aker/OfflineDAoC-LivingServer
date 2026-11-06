using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DOL.Events;

namespace DOL.GS;

// Observational only: lifecycle hooks update tiny immutable records in memory.
// A separate timer writes the separate ledger; no SQL or file I/O on AI turns.
public static class RealmEventRecords
{
    private static readonly object Sync=new(), WriteSync=new();
    private static readonly Dictionary<string,RealmEventRecord> Active=new(), Pending=new();
    private static Timer _writer;
    private static bool _reconcile;
    private static DateTime _nextWarningUtc;
    // Living Server 1.1.2: beside the world database (runtime\data), where backups and progress transfers find it.
    private static string LegacyPathName=>Path.Combine(AppContext.BaseDirectory,RealmEventRecordStore.FileName);
    private static string PathName=>WorldDatabaseSafety.DataDirectory is { } data?RealmEventRecordStore.PathIn(data):LegacyPathName;
    public static void Begin(string eventId,string name,string kind,string realm,string details)
    {
        lock(Sync)
        {
            if(Active.ContainsKey(eventId))return;
            if(Pending.Count>=4096){DOL.Logging.LoggerManager.Create(typeof(RealmEventRecords)).Warn("Realm event ledger backlog full; new record skipped, gameplay unchanged.");return;}
            var row=new RealmEventRecord(Guid.NewGuid().ToString("N"),eventId,name,kind,realm,DateTime.UtcNow.ToString("O"),"","Rally","In progress",details,0,0);
            Active[eventId]=row;Pending[row.Id]=row;
        }
    }
    public static void Progress(string eventId,string phase,string details,int assigned,int present)
    {
        lock(Sync)if(Active.TryGetValue(eventId,out var row))
        { row=row with {Phase=phase,Details=details,Assigned=assigned,Present=present};Active[eventId]=row;Pending[row.Id]=row; }
    }
    public static void Finish(string eventId,string outcome,string details,int assigned=-1,int present=-1)
    {
        lock(Sync)if(Active.Remove(eventId,out var row))
        { row=row with {EndedUtc=DateTime.UtcNow.ToString("O"),Phase="Ended",Outcome=outcome,Details=details,Assigned=assigned<0?row.Assigned:assigned,Present=present<0?row.Present:present};Pending[row.Id]=row; }
    }
    /// <summary>
    /// The outcomes of an event's last finished runs, oldest first, from this ledger (what the launcher's Herald shows).
    /// File I/O: call it off the game loop. Empty when the ledger cannot be read; nothing depends on it being there.
    /// <paramref name="automaticOnly"/> leaves out raids started from the launcher.
    /// </summary>
    public static List<string> FinishedOutcomes(string eventId,int limit,bool automaticOnly=false)
    {
        try{return RealmEventRecordStore.Outcomes(PathName,eventId,limit,automaticOnly);}
        catch(Exception){return new();}
    }
    /// <summary>As <see cref="FinishedOutcomes"/>, with how many Epic Special Forces joined each run. File I/O: off the game loop.</summary>
    public static List<(string Outcome,int SpecialForces,double? SizePercent)> FinishedResults(string eventId,int limit,bool automaticOnly=false)
    {
        try{return RealmEventRecordStore.Results(PathName,eventId,limit,automaticOnly);}
        catch(Exception){return new();}
    }
    [GameServerStartedEvent]
    public static void Start(DOLEvent e,object sender,EventArgs args)
    {
        _writer?.Dispose();lock(Sync){Active.Clear();Pending.Clear();_reconcile=true;}
        try
        {
            string moved=RealmEventRecordStore.MoveLegacy(LegacyPathName,PathName);
            if(moved!="none")DOL.Logging.LoggerManager.Create(typeof(RealmEventRecords)).Info($"REALM_EVENT_LEDGER {moved}: {LegacyPathName} -> {PathName}");
        }
        catch(Exception ex){DOL.Logging.LoggerManager.Create(typeof(RealmEventRecords)).Warn($"Realm event ledger could not be moved to {PathName}; the old file stays where it is.",ex);}
        // 2026-11-04: the siege balance survives a restart (it was in memory only). One small read at start, before any
        // siege of this session can end; an unreadable ledger leaves the balance where a fresh start puts it.
        try{SiegeDefenseBalance.Load(RealmEventRecordStore.SiegeResults(PathName,SiegeDefenseBalance.LedgerResults));}
        catch(Exception ex){SiegeDefenseBalance.Load([]);DOL.Logging.LoggerManager.Create(typeof(RealmEventRecords)).Warn("Siege balance could not read the realm event ledger; it starts from the setting.",ex);}
        _writer=new Timer(_=>Flush(),null,1000,2000);
    }
    [GameServerStoppedEvent]
    public static void Stop(DOLEvent e,object sender,EventArgs args)
    {
        _writer?.Dispose();_writer=null;
        lock(Sync)foreach(string id in Active.Keys.ToArray())Finish(id,"Interrupted","Server stopped before the event reached a final outcome.");
        Flush(true);
    }
    private static void Flush(bool drain = false)
    {
        if(drain)Monitor.Enter(WriteSync);
        else if(!Monitor.TryEnter(WriteSync))return;
        try
        {
            if(_reconcile){RealmEventRecordStore.CloseInterrupted(PathName);_reconcile=false;}
            do
            {
                KeyValuePair<string,RealmEventRecord>[] batch;
                lock(Sync)batch=Pending.Take(128).ToArray();
                if(batch.Length==0)return;
                RealmEventRecordStore.Save(PathName,batch.Select(p=>p.Value));
                lock(Sync)foreach(var item in batch)
                    if(Pending.TryGetValue(item.Key,out var current)&&ReferenceEquals(current,item.Value))Pending.Remove(item.Key);
            } while(drain);
        }
        catch(Exception ex)
        {
            if(DateTime.UtcNow>=_nextWarningUtc)
            { _nextWarningUtc=DateTime.UtcNow.AddMinutes(1);DOL.Logging.LoggerManager.Create(typeof(RealmEventRecords)).Warn("Realm event ledger write failed; queued records retained for retry.",ex); }
        }
        finally{Monitor.Exit(WriteSync);}
    }
}
