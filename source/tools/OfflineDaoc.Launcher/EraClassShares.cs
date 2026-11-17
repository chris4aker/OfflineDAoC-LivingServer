using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Era builds 1.2 (docs/ERA-BUILDS-1.2.md, WP2; the server setting bots / bot_era_class_shares, on by default): the "+ Lv.1",
/// "+ Lv.20" and "+ Lv.50" batches draw each new gamebot's class from its realm's 2003 share
/// (OfflineDaoc.Shared.EraBuildCatalog, embedded here and in the server) instead of evenly. The launcher reads the row; the
/// server declares it (ServerProperties.BOT_ERA_CLASS_SHARES) and uses the same draw in AutonomousBotIdentityGenerator.
/// Either way no bot is ever the custom class (owner 2026-11-17).
/// </summary>
public static class EraClassShares
{
    public const string Key = "bot_era_class_shares";

    /// <summary>The setting as stored: a missing row or an unreadable value is the default, on.</summary>
    public static bool IsOn(string? stored) =>
        string.IsNullOrWhiteSpace(stored) || !bool.TryParse(stored.Trim(), out bool on) || on;

    /// <summary>Reads the setting in the generator's transaction.</summary>
    public static bool IsOn(SQLiteConnection connection, SQLiteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM ServerProperty WHERE `Key`=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", Key);
        object? value;
        try { value = command.ExecuteScalar(); }
        catch (SQLiteException) { return true; }   // no ServerProperty table: the default
        return IsOn(value as string);
    }
}
