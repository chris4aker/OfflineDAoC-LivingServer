using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using DOL.Events;

namespace DOL.GS;

/// <summary>
/// Relic raids survive a restart (relic audit 2026-10-31, B5). Events, campaigns and carrier events live in memory only,
/// and a relic attempt (up to 2 h of muster and march plus up to 4 h of battle) never outlived the restarts of
/// 2026-10-30: both relic sieges vanished without an end line. Every 30 seconds and at shutdown the raids in progress are
/// written to rvr-relic-campaigns.json (the mode's state folder); at the next start each one is re-planned as a relic
/// campaign that goes on after a few minutes with a fresh muster: the next sentinel keep, or the relic siege.
///
/// A relic that was being carried at the restart is back on the pad it was taken from (RelicMgr.Init: the saved relic
/// lies at that pad, or is lost and returns to the realm that held it). That is the period rule for a carrier who leaves
/// the game (the relic goes back; it is never handed to the carrier's realm), so the raid that took it is re-planned
/// against the same relic keep.
/// </summary>
public static class AutonomousRvrRelicPersistence
{
    public static string FilePath => System.IO.Path.Combine(AppContext.BaseDirectory, "rvr-relic-campaigns.json");
    /// <summary>Time after a start before resumed raids continue (the bot population logs in first).</summary>
    public static long ResumeDelayMilliseconds => 5 * 60_000L;
    public const int SaveIntervalMilliseconds = 30_000;

    public sealed record SavedState(DateTime SavedUtc, AutonomousRvrEventLayer.RelicState[] Raids);

    private static Timer _timer;
    private static int _saving;
    private static string _lastWritten;
    private static long _lastWriteTick;
    /// <summary>The idle time left is refreshed on disk at least this often while raids are open.</summary>
    public const long RefreshMilliseconds = 10 * 60_000;

    [GameServerStartedEvent]
    public static void Start(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        Load();
        _timer = new Timer(_ => Save(), null, SaveIntervalMilliseconds, SaveIntervalMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void Stop(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        Save(force: true);
    }

    private static void Load()
    {
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrRelicPersistence));
        try
        {
            string path = FilePath;
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return;
            SavedState saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path));
            if (saved?.Raids == null || saved.Raids.Length == 0) return;
            int restored = AutonomousRvrEventLayer.RestoreRelicState(saved.Raids, GameLoop.GameLoopTime, ResumeDelayMilliseconds);
            if (log.IsInfoEnabled)
                log.Info($"RVR_RELIC_STATE_LOADED saved={saved.SavedUtc:u} raids={saved.Raids.Length} resumed={restored} file=\"{path}\"");
        }
        catch (Exception exception)
        {
            if (log.IsWarnEnabled) log.Warn("RVR_RELIC_STATE_LOAD_FAILED: relic raids were not resumed", exception);
        }
    }

    public static void Save(bool force = false)
    {
        if (Interlocked.Exchange(ref _saving, 1) != 0) return;
        try
        {
            var raids = AutonomousRvrEventLayer.RelicStateSnapshot(GameLoop.GameLoopTime);
            // Only a changed set of raids is written (the remaining idle time alone does not count as a change).
            string key = string.Join(";", Array.ConvertAll(raids, r => $"{r.RelicKeepId}/{r.Attacker}/{r.Source}/{r.StepsWithoutProgress}"));
            long now = GameLoop.GameLoopTime;
            if (!force && key == _lastWritten && File.Exists(FilePath) && (raids.Length == 0 || now - _lastWriteTick < RefreshMilliseconds)) return;
            string path = FilePath;
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new SavedState(DateTime.UtcNow, raids)));
            File.Move(path + ".tmp", path, true);
            _lastWritten = key;
            _lastWriteTick = now;
        }
        catch (Exception exception)
        {
            DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrRelicPersistence)).Warn("RVR_RELIC_STATE_SAVE_FAILED", exception);
        }
        finally { Volatile.Write(ref _saving, 0); }
    }
}
