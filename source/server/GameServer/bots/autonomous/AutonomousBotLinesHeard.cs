using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>When a realm last heard a corpus line (the six-hour no-repeat window, saved across restarts).</summary>
[DataTable(TableName = "offline_bot_lines_heard")]
public sealed class OfflineBotLineHeardRecord : DataObject
{
    [PrimaryKey] public string HeardKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 32)] public string LineId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string UsedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Saves <see cref="AutonomousBotVoices.Recent"/> (0.36-se.4). The window lived in memory only, so every
/// server start began with an empty one; the audit (2026-10-18) simulated 31% of a session's lines as heard
/// the session before, and 47% by the third session. Uses are written once a minute off the game loop
/// under the shared database write lock, and read back at start-up (rows older than the window are dropped).
/// </summary>
public static class AutonomousBotLinesHeard
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly Dictionary<string, OfflineBotLineHeardRecord> Known = new(StringComparer.Ordinal);
    private static Timer _timer;
    private static int _flushing;
    private static DateTime _savedUpTo = DateTime.MinValue;

    public static string KeyOf(eRealm realm, string lineId) => $"{(int)realm}:{lineId}";

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        Load(DateTime.UtcNow);
        _timer = new Timer(_ => Flush(), null, 60_000, 60_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        Flush(wait: true);
    }

    /// <summary>Reads saved uses into the window; deletes rows already outside it.</summary>
    public static void Load(DateTime now)
    {
        try
        {
            var stale = new List<DataObject>();
            int restored = 0;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                foreach (OfflineBotLineHeardRecord row in GameServer.Database.SelectAllObjects<OfflineBotLineHeardRecord>())
                {
                    if (!TryParseUtc(row.UsedUtc, out DateTime when) || now - when >= VC.RecentLines.Window)
                    {
                        stale.Add(row);
                        continue;
                    }
                    AutonomousBotVoices.Recent.Restore((eRealm)row.Realm, row.LineId, when);
                    lock (Known)
                        Known[row.HeardKey] = row;
                    restored++;
                }
                if (stale.Count > 0)
                    GameServer.Database.DeleteObject(stale);
            }
            _savedUpTo = now;
            if (restored > 0 && Log.IsInfoEnabled)
                Log.Info($"Bot voices: {restored} line(s) heard in the last {VC.RecentLines.Window.TotalHours:0} hours restored");
        }
        catch (Exception exception)
        {
            Log.Warn("Bot voices: could not read the lines heard before the restart", exception);
        }
    }

    /// <summary>Writes the uses since the last flush (timer thread; at shutdown with <paramref name="wait"/>).</summary>
    public static void Flush(bool wait = false)
    {
        while (Interlocked.Exchange(ref _flushing, 1) != 0)
        {
            if (!wait)
                return;
            Thread.Sleep(20);
        }
        try
        {
            DateTime since = _savedUpTo;
            (eRealm Realm, string Id, DateTime When)[] uses = AutonomousBotVoices.Recent.UsedSince(since);
            if (uses.Length == 0)
                return;
            var adds = new List<DataObject>();
            var saves = new List<DataObject>();
            lock (Known)
            {
                foreach ((eRealm realm, string id, DateTime when) in uses)
                {
                    string key = KeyOf(realm, id);
                    string utc = when.ToString("O");
                    if (Known.TryGetValue(key, out OfflineBotLineHeardRecord row))
                    {
                        row.UsedUtc = utc;
                        row.Dirty = true;
                        saves.Add(row);
                    }
                    else
                    {
                        row = new OfflineBotLineHeardRecord { HeardKey = key, Realm = (int)realm, LineId = id, UsedUtc = utc };
                        Known[key] = row;
                        adds.Add(row);
                    }
                }
            }
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                if (adds.Count > 0)
                    GameServer.Database.AddObject(adds);
                if (saves.Count > 0)
                    GameServer.Database.SaveObject(saves);
            }
            _savedUpTo = uses.Max(use => use.When);
        }
        catch (Exception exception)
        {
            Log.Warn("Bot voices: could not save the lines heard", exception);
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }

    private static bool TryParseUtc(string value, out DateTime when) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out when);
}
