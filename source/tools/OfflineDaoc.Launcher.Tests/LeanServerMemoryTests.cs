using System.Data.SQLite;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>The launcher's "Lean server memory" option (Living Server 1.2): stored per world, off unless saved on, and
/// the server's start environment follows it either way.</summary>
public sealed class LeanServerMemoryTests
{
    private static SQLiteConnection Open()
    {
        var connection = new SQLiteConnection("Data Source=:memory:;Version=3");
        connection.Open();
        return connection;
    }

    [Test]
    public void Off_WithoutTheTableOrRow()
    {
        using SQLiteConnection connection = Open();
        Assert.That(LeanServerMemory.Read(connection), Is.False);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        Assert.That(LeanServerMemory.Read(connection), Is.False);
    }

    [Test]
    public void Persist_RoundTrips()
    {
        using SQLiteConnection connection = Open();
        LeanServerMemory.Persist(connection, true);
        Assert.That(LeanServerMemory.Read(connection), Is.True);
        LeanServerMemory.Persist(connection, false);
        Assert.That(LeanServerMemory.Read(connection), Is.False);
    }

    [Test]
    public void Apply_On_SetsBothVariables()
    {
        var environment = new Dictionary<string, string?>();
        LeanServerMemory.Apply(environment, true);
        Assert.That(environment["DOTNET_GCHeapCount"], Is.EqualTo("4"));
        Assert.That(environment["DOTNET_GCConserveMemory"], Is.EqualTo("5"));
    }

    [Test]
    public void Apply_Off_RemovesInheritedValues_AndLeavesOthers()
    {
        var environment = new Dictionary<string, string?> { ["DOTNET_GCHeapCount"] = "4", ["DOTNET_GCConserveMemory"] = "5", ["PATH"] = "x" };
        LeanServerMemory.Apply(environment, false);
        Assert.That(environment.ContainsKey("DOTNET_GCHeapCount"), Is.False);
        Assert.That(environment.ContainsKey("DOTNET_GCConserveMemory"), Is.False);
        Assert.That(environment["PATH"], Is.EqualTo("x"));
    }
}
