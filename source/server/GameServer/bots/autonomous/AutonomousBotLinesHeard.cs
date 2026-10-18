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
    private static readonly Dictionary<string, DateTime> Saved = new(StringComparer.Ordinal);
    private static Timer _timer;
    private static int _flushing;

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
                    {
                        Known[row.HeardKey] = row;
                        Saved[row.HeardKey] = when;
                    }
                    restored++;
                }
                if (stale.Count > 0)
                    GameServer.Database.DeleteObject(stale);
            }
            if (restored > 0 && Log.IsInfoEnabled)
                Log.Info($"Bot voices: {restored} line(s) heard in the last {VC.RecentLines.Window.TotalHours:0} hours restored");
        }
        catch (Exception exception)
        {
            Log.Warn("Bot voices: could not read the lines heard before the restart", exception);
        }
    }

    /// <summary>
    /// Writes every use newer than what was last saved for that line (timer thread; at shutdown with
    /// <paramref name="wait"/>). Each line is compared with its own saved time, so a use recorded late by a
    /// parallel brain is not skipped, and a row whose write failed is tried again at the next flush.
    /// </summary>
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
            var adds = new List<(OfflineBotLineHeardRecord Row, DateTime When)>();
            var saves = new List<(OfflineBotLineHeardRecord Row, DateTime When)>();
            lock (Known)
            {
                foreach ((eRealm realm, string id, DateTime when) in AutonomousBotVoices.Recent.UsedSince(DateTime.MinValue))
                {
                    string key = KeyOf(realm, id);
                    if (Saved.TryGetValue(key, out DateTime saved) && saved >= when)
                        continue;
                    string utc = when.ToString("O");
                    if (Known.TryGetValue(key, out OfflineBotLineHeardRecord row))
                    {
                        row.UsedUtc = utc;
                        row.Dirty = true;
                        saves.Add((row, when));
                    }
                    else
                    {
                        row = new OfflineBotLineHeardRecord { HeardKey = key, Realm = (int)realm, LineId = id, UsedUtc = utc };
                        adds.Add((row, when));
                    }
                }
            }
            if (adds.Count == 0 && saves.Count == 0)
                return;
            var written = new List<(OfflineBotLineHeardRecord Row, DateTime When)>();
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                if (adds.Count > 0)
                {
                    if (GameServer.Database.AddObject(adds.Select(add => (DataObject)add.Row)))
                        written.AddRange(adds);
                    else
                        // One by one: a row that already exists (written before a failed batch) is updated instead.
                        foreach (var add in adds)
                        {
                            if (GameServer.Database.AddObject(add.Row))
                                written.Add(add);
                            else
                            {
                                add.Row.IsPersisted = true;
                                add.Row.Dirty = true;
                                if (GameServer.Database.SaveObject(add.Row))
                                    written.Add(add);
                            }
                        }
                }
                if (saves.Count > 0)
                {
                    if (GameServer.Database.SaveObject(saves.Select(save => (DataObject)save.Row)))
                        written.AddRange(saves);
                    else
                        foreach (var save in saves)
                            if (GameServer.Database.SaveObject(save.Row))
                                written.Add(save);
                }
            }
            lock (Known)
            {
                foreach ((OfflineBotLineHeardRecord row, DateTime when) in written)
                {
                    Known[row.HeardKey] = row;
                    Saved[row.HeardKey] = when;
                }
            }
            if (written.Count < adds.Count + saves.Count && Log.IsWarnEnabled)
                Log.Warn($"Bot voices: {adds.Count + saves.Count - written.Count} line use(s) not saved; trying again at the next flush");
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
