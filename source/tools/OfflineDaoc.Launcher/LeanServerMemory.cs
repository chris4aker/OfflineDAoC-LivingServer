using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Lean server memory (Living Server 1.2, owner 2026-11-13; off by default). The server runs .NET's server garbage
/// collector, which keeps one heap per logical CPU and hands little back while the PC has memory to spare. Ryan saw
/// about 6 GB after 48 minutes on a 16-thread Linux desktop, and the owner's 16-thread PC measured 7.0 GB after three
/// hours of build 8 (SERVER_MEMORY: live 1.9 GB, GC heap 3.3 GB, native 3.6 GB). This switch starts the server with
/// fewer heaps and "conserve memory", as Ryan's start script does. It changes only how the server's memory is
/// collected, never what the server does, and it is read at START SERVER. The server log's
/// SERVER_MEMORY_DIAGNOSTICS line names the settings in force, so the two can be compared.
/// </summary>
public static class LeanServerMemory
{
    public const string OptionKey = "LeanServerMemory";

    /// <summary>The environment the server starts with when the switch is on. DOTNET_GCHeapCount is read as hex.</summary>
    public static readonly IReadOnlyDictionary<string, string> Environment = new Dictionary<string, string>
    {
        ["DOTNET_GCHeapCount"] = "4",
        ["DOTNET_GCConserveMemory"] = "5",
    };

    /// <summary>On only with an explicit "true" row; a missing table or row is off.</summary>
    public static bool Read(SQLiteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='offline_local_options'";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
            return false;
        command.CommandText = "SELECT Value FROM offline_local_options WHERE Key=@key";
        command.Parameters.AddWithValue("@key", OptionKey);
        return string.Equals(command.ExecuteScalar()?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Read(string database)
    {
        if (!File.Exists(database))
            return false;
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Read Only=True;Default Timeout=5");
        connection.Open();
        return Read(connection);
    }

    public static void Persist(SQLiteConnection connection, bool enabled)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO offline_local_options(Key,Value) VALUES (@key,@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@key", OptionKey);
        command.Parameters.AddWithValue("@value", enabled ? "true" : "false");
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Sets the server's start environment. Off removes the two variables, so a value the launcher inherited (Ryan's
    /// start script exports them) doesn't apply when the switch says off.
    /// </summary>
    public static void Apply(IDictionary<string, string?> environment, bool enabled)
    {
        foreach ((string name, string value) in Environment)
        {
            if (enabled)
                environment[name] = value;
            else
                environment.Remove(name);
        }
    }
}
