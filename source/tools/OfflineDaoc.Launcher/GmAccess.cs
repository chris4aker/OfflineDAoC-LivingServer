using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Make Me GM: GM (PrivLevel 2) for this PC's own account, the one in account.txt, and for no other. Moved out of MainForm
/// in Living Server 1.2 so a test can show that a friend's account on the same server is never touched (co-op audit
/// 2026-11-03 §3: never grant guests GM). Friends' new accounts are made with PrivLevel 1 by the server.
/// </summary>
public static class GmAccess
{
    public static void Persist(SQLiteConnection connection, string account, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("No local account in account.txt.", nameof(account));

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO offline_local_options VALUES ('MakeMeGM',@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@value", enabled ? "true" : "false");
        command.ExecuteNonQuery();
        command.CommandText = "UPDATE Account SET PrivLevel=@privilege WHERE Name=@account";
        command.Parameters.AddWithValue("@privilege", enabled ? 2 : 1);
        command.Parameters.AddWithValue("@account", account);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Your local account must exist before changing GM access. Enter the game once, then stop the server and retry.");
        transaction.Commit();
    }
}
