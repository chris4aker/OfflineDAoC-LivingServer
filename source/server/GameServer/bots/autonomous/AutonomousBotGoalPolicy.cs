using System;
using System.IO;
using DAoCLivingServer.Configuration;

namespace DOL.GS;

public static class AutonomousBotGoalPolicy
{
    public static BotGoalSettings Settings { get; private set; } = BotGoalSettings.Defaults;
    public const string OverrideVariable = "OFFLINE_DAOC_BOT_GOALS";
    /// <summary>
    /// True when a goals file was loaded (the server's bot-goals.json, or the test override). Since 1.2.3 (#669; owner,
    /// 2026-11-26) a world without one still follows <see cref="Settings"/> (then <see cref="BotGoalSettings.Defaults"/>,
    /// as the launcher shows them) at every task break, and its RvR eligibility follows the RvR share (the file-less
    /// "one solo PvE task after an RvR tour" rule was removed; owner, 2026-11-26, #669). This flag only keeps the
    /// level-50 task-break draw a file-less world always had (<see cref="AutonomousObjectiveAssignments.TaskBreakGoal"/>)
    /// and the pass's over-quota draw.
    /// </summary>
    public static bool IsConfigured { get; private set; }
    /// <summary>Where <see cref="Settings"/> came from at the last Initialize: "file", "override" or "defaults".</summary>
    public static string Source { get; private set; } = "defaults";
    /// <summary>The server's own goals file (bot-goals.json), as the last Initialize found it; null before.</summary>
    public static string ServerFilePath { get; private set; }
    private static string _loadedPath;

    // One startup read only. Never poll settings or query the database on AI turns.
    public static void Initialize(string serverDirectory)
    {
        string path = Path.Combine(serverDirectory, BotGoalSettings.FileName);
        ServerFilePath = path;
        // Test runs may point at their own goals file so the owner's saved launcher setting
        // (bot-goals.json) is never edited for a test.
        string overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        bool overridden = false;
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            path = overridePath;
            overridden = true;
            try { DOL.Logging.LoggerManager.Create(typeof(AutonomousBotGoalPolicy)).Warn($"BOT_GOALS_OVERRIDE using {overridePath} instead of {BotGoalSettings.FileName}"); }
            catch (NullReferenceException) { /* no logger yet (unit tests): logging only */ }
        }
        _loadedPath = path;
        Settings = BotGoalSettings.Load(path); // Reject corruption rather than silently ignoring 0% exclusions.
        IsConfigured = File.Exists(path);
        Source = !IsConfigured ? "defaults" : overridden ? "override" : "file";
        LivingRows.Emit(BotGoalRows.Startup(Source, Settings, "start"));
    }

    /// <summary>
    /// 1.2.3 (#598, LivingServerSettingsUpgrade): the server's own bot-goals.json moves to the new default shares when it
    /// still holds exactly the 1.2.2 defaults, then the policy reloads it. A test override file is never touched, nor any
    /// file with other values (the player's own). Runs at start-up before any bot plays. True when the file was rewritten.
    /// </summary>
    public static bool UpgradeUntouchedDefaults()
    {
        if (ServerFilePath == null || !string.Equals(_loadedPath, ServerFilePath, StringComparison.Ordinal) ||
            !BotGoalSettings.UpgradeUntouchedDefaults(ServerFilePath))
            return false;
        Settings = BotGoalSettings.Load(ServerFilePath);
        IsConfigured = true;
        Source = "file";
        LivingRows.Emit(BotGoalRows.Startup(Source, Settings, "upgrade"));
        return true;
    }

    public static eAutonomousObjectiveKind Choose(int level, Random random = null, bool excludeGroup = false,
        bool excludeBattlegrounds = false) =>
        (eAutonomousObjectiveKind)Settings.ForLevel(level).Choose((random ?? Random.Shared).NextDouble(), excludeGroup,
            excludeBattlegrounds || !BattlegroundBrackets.LevelHasBracket(level));

    // A battleground goal also needs a bracket for the level (15-35); level is the only battleground limit.
    public static bool Allows(int level, eAutonomousObjectiveKind kind) =>
        Settings.ForLevel(level).Allows((int)kind) &&
        (kind != eAutonomousObjectiveKind.Battleground || BattlegroundBrackets.LevelHasBracket(level));

    public static eAutonomousObjectiveKind EnsureAllowed(int level, eAutonomousObjectiveKind kind) =>
        Allows(level, kind) ? kind : Choose(level);

    // Called before an autonomous actor can enter the world. Keep inventory,
    // position, XP and valid saved task clocks; discard only a now-disabled task.
    // #669 (owner, 2026-11-26): with or without a file, the same path. Without one the defaults decide what is
    // disabled, and the RvR marker cleared here includes PveCompletionRequired, which a world that ran an earlier build
    // may still hold for a bot just off an RvR tour (nothing reads it any more; clearing it keeps the record clean).
    public static bool ReconcileSavedAssignment(OfflineWorldBotRecord record)
    {
        if (record == null) return false;
        bool changed = false;
        if (!string.IsNullOrEmpty(record.ObjectiveRvrEligibleUtc))
        {
            record.ObjectiveRvrEligibleUtc = string.Empty;
            changed = true;
        }
        if (!AutonomousObjectiveAssignments.IsBetweenPveTasks(record) &&
            !Allows(record.Level, AutonomousObjectiveAssignments.Parse(record.ObjectiveKind)))
        {
            record.ObjectiveKind = Choose(record.Level).ToString();
            record.ObjectiveAssignmentId = string.Empty;
            record.ObjectiveAssignedUtc = record.ObjectiveExpiresUtc = string.Empty;
            record.CurrentCampId = record.TargetName = record.TravelDestination = string.Empty;
            record.ObjectivePhase = "Choosing a goal using Bot Goals Setting";
            changed = true;
        }
        if (changed) record.Dirty = true;
        return changed;
    }
}
