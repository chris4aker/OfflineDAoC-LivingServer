#nullable enable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace OfflineDaoc.Passport;

/// <summary>Thin SQLite access. Values are read by storage class, so a row comes back byte for byte.</summary>
sealed class PassportDatabase : IDisposable
{
    readonly SQLiteConnection _connection;
    readonly Dictionary<string, TableShape> _shapes = new(StringComparer.OrdinalIgnoreCase);

    sealed record TableShape(List<string> Columns, string? AutoKey);

    public PassportDatabase(string path, bool readOnly = false)
    {
        var builder = new SQLiteConnectionStringBuilder { DataSource = path, ReadOnly = readOnly, FailIfMissing = true, Pooling = false, DefaultTimeout = 60 };
        _connection = new SQLiteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    public static void Backup(string source, string destination)
    {
        using var from = new PassportDatabase(source, readOnly: true);
        using var to = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = destination, Pooling = false }.ConnectionString);
        to.Open();
        from._connection.BackupDatabase(to, "main", "main", -1, null, 0);
    }

    public void Transaction(Action body)
    {
        Exec("BEGIN IMMEDIATE");
        try
        {
            body();
            Exec("COMMIT");
        }
        catch
        {
            Exec("ROLLBACK");
            throw;
        }
    }

    public int Exec(string sql, params (string Name, object? Value)[] values)
    {
        using SQLiteCommand command = Command(sql, values);
        return command.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] values)
    {
        using SQLiteCommand command = Command(sql, values);
        object? value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public long Count(string sql, params (string Name, object? Value)[] values) => Convert.ToInt64(Scalar(sql, values));

    public bool HasTable(string table) =>
        Count("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@t COLLATE NOCASE", ("@t", table)) > 0;

    public IReadOnlyList<string> Columns(string table) => Shape(table).Columns;

    /// <summary>Every column except an autoincrement key, which the receiving world assigns itself.</summary>
    public IEnumerable<string> PortableColumns(string table)
    {
        TableShape shape = Shape(table);
        return shape.Columns.Where(c => !c.Equals(shape.AutoKey, StringComparison.OrdinalIgnoreCase));
    }

    public List<Row> Rows(string table, string where, params (string Name, object? Value)[] values)
    {
        // A unary plus drops the declared type, so DATETIME text stays the text that is stored.
        string columns = string.Join(",", PortableColumns(table).Select(c => $"+{Q(c)} AS {Q(c)}"));
        using SQLiteCommand command = Command($"SELECT {columns} FROM {Q(table)} WHERE {where}", values);
        using SQLiteDataReader reader = command.ExecuteReader();
        var rows = new List<Row>();
        while (reader.Read())
        {
            var row = new Row();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Inserts the columns both sides have; returns the ones this world lacks.</summary>
    public List<string> Insert(string table, Row row, bool orIgnore = false)
    {
        var here = new HashSet<string>(Columns(table), StringComparer.OrdinalIgnoreCase);
        List<string> shared = row.Keys.Where(here.Contains).ToList();
        string names = string.Join(",", shared.Select(Q));
        string slots = string.Join(",", shared.Select((_, i) => $"@p{i}"));
        Exec($"INSERT {(orIgnore ? "OR IGNORE " : "")}INTO {Q(table)} ({names}) VALUES ({slots})",
            shared.Select((c, i) => ($"@p{i}", row[c])).ToArray());
        return row.Keys.Except(shared, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    TableShape Shape(string table)
    {
        if (_shapes.TryGetValue(table, out TableShape? shape))
            return shape;

        var columns = new List<string>();
        string? autoKey = null;
        using (SQLiteCommand command = Command($"PRAGMA table_info({Q(table)})"))
        using (SQLiteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string name = reader.GetString(1);
                columns.Add(name);
                if (reader.GetInt32(5) == 1 && reader.GetString(2).Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                    autoKey = name;
            }
        }
        return _shapes[table] = new TableShape(columns, autoKey);
    }

    SQLiteCommand Command(string sql, params (string Name, object? Value)[] values)
    {
        SQLiteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
