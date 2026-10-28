using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Logging;

namespace DOL.GS;

/// <summary>A dunce that rage quit (AutonomousBotSchedule.RageQuit): when it may come back, kept until it does.</summary>
[DataTable(TableName = "offline_bot_ragequits")]
public sealed class OfflineBotRageQuitRecord : DataObject
{
    [PrimaryKey] public string BotKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public string UntilUtc { get; set; } = string.Empty;
}

/// <summary>
/// Saves rage quits so a server restart does not cut them short (2026-10-26: the 2-6 hours and the sheepish return lived
/// in memory only, so after a restart the dunce logged straight back in and said nothing). A row is written when the dunce
/// logs off and deleted when it logs back in; rows a week old are dropped at start-up.
/// </summary>
public static class AutonomousBotRageQuits
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
    private static readonly object Sync = new();
    private static bool _loaded;

    /// <summary>The server's database, or null before the server (and in unit tests).</summary>
    private static IObjectDatabase Database() => GameServer.Instance == null ? null : GameServer.Database;

    private static string KeyOf(long botId) => botId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads saved rage quits into the schedule, once, the first time the schedule asks.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        lock (Sync)
        {
            if (_loaded)
                return;
            IObjectDatabase database = Database();
            if (database == null)
                return;
            _loaded = true;
            try
            {
                DateTime now = DateTime.UtcNow;
                var stale = new List<DataObject>();
                int restored = 0;
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    foreach (OfflineBotRageQuitRecord row in database.SelectAllObjects<OfflineBotRageQuitRecord>())
                    {
                        if (!DateTime.TryParse(row.UntilUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime until) ||
                            now - until > KeepFor)
                        {
                            stale.Add(row);
                            continue;
                        }
                        AutonomousBotSchedule.RestoreRageQuit(row.BotId, until, now);
                        restored++;
                    }
                    if (stale.Count > 0)
                        database.DeleteObject(stale);
                }
                if (restored > 0)
                    Log.Info($"LIVING_SERVER_RAGEQUITS restored={restored} dropped={stale.Count}");
            }
            catch (Exception exception)
            {
                Log.Warn("Living Server: could not read saved rage quits", exception);
            }
        }
    }

    /// <summary>Writes a rage quit (off the game loop).</summary>
    public static void Save(long botId, DateTime untilUtc)
    {
        IObjectDatabase database = Database();
        if (database == null || botId <= 0)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    var row = database.FindObjectByKey<OfflineBotRageQuitRecord>(KeyOf(botId));
                    if (row == null)
                        database.AddObject(new OfflineBotRageQuitRecord { BotKey = KeyOf(botId), BotId = botId, UntilUtc = untilUtc.ToString("O") });
                    else
                    {
                        row.UntilUtc = untilUtc.ToString("O");
                        database.SaveObject(row);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Living Server: could not save the rage quit of bot {botId}", exception);
            }
        });
    }

    /// <summary>Deletes a rage quit once the dunce is back (off the game loop).</summary>
    public static void Forget(long botId)
    {
        IObjectDatabase database = Database();
        if (database == null || botId <= 0)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    var row = database.FindObjectByKey<OfflineBotRageQuitRecord>(KeyOf(botId));
                    if (row != null)
                        database.DeleteObject(row);
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"Living Server: could not clear the rage quit of bot {botId}", exception);
            }
        });
    }
}
