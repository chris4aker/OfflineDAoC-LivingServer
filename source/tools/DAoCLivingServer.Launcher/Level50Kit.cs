using System.Data.SQLite;

namespace DAoCLivingServer.Launcher;

/// <summary>
/// The level-50 kit (Crafting 2.0 WP9; design decision 10; the server setting bots / launcher_level50_kit, on by default).
/// On, as every launcher before 1.2: a "+ Lv.50" bot arrives with its class's quality-100 "Offline level 50" kit
/// (offline_level50_loadouts) and 10,000 gold, which is what makes raids possible in a test world. Off, for a fresh-start
/// world: it arrives with no kit and the modest purse the launcher gives every other trained bot (the level squared in
/// silver, 25 gold at 50; BattlegroundBotLevels.PurseCopper's rule), and the server dresses it at its first login in plain
/// merchant gear for its level, as it does a "+ Lv.20" bot (AutonomousStarterGear). The launcher reads the setting; the
/// server only declares it (CraftingEconomyProperties.LAUNCHER_LEVEL50_KIT).
/// </summary>
public static class Level50Kit
{
    public const string Key = "launcher_level50_kit";
    /// <summary>10,000 gold, the kit's purse since the level-50 button was added.</summary>
    public const long KitPurseCopper = 100_000_000L;
    /// <summary>The purse without the kit: the level squared in silver.</summary>
    public const long PlainPurseCopper = 50L * 50L * 100L;

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

    /// <summary>A level-50 bot's purse.</summary>
    public static long PurseCopper(bool kit) => kit ? KitPurseCopper : PlainPurseCopper;
}
