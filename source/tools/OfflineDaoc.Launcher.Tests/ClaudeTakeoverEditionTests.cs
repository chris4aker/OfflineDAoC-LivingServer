using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Since 0.33 "Claude Takeover": every install makes its own local account. Since 1.2 no launcher batch rolls class 63
/// in either edition (owner 2026-11-17: bots never play the custom class).
/// </summary>
public sealed class ClaudeTakeoverEditionTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private static readonly BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void EachInstallCreatesItsOwnAccountOnceAndNeverReplacesIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), "odc-account-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = PortableCredentials.ReadOrCreate(folder);
            var second = PortableCredentials.ReadOrCreate(folder);
            Assert.That(first, Is.EqualTo(second), "an existing account.txt is reused, never regenerated");
            Assert.That(first.Account, Is.EqualTo("offline"));
            Assert.That(first.Password, Has.Length.EqualTo(20), "the legacy login packet allows 20 characters");

            string other = Path.Combine(folder, "other");
            Directory.CreateDirectory(other);
            Assert.That(PortableCredentials.ReadOrCreate(other).Password, Is.Not.EqualTo(first.Password),
                "a second install gets its own password");
        }
        finally { Directory.Delete(folder, true); }
    }

    // Owner 2026-11-17: bots never play the custom class, in either edition; the batches no longer read the edition switch.
    [TestCase(true)]
    [TestCase(false)]
    public void TheCustomClassIsNeverRolled(bool eraClassShares)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MethodInfo generate = Launcher.GetType("OfflineDaoc.Launcher.BotCharacterGenerator")!
            .GetMethod("Generate", BindingFlags.Static | BindingFlags.Public, [typeof(int), typeof(ISet<string>), typeof(bool)])!;
        var rolls = Enumerable.Range(0, 1500).Select(_ =>
        {
            object identity = generate.Invoke(null, [3, reserved, eraClassShares])!;
            return (int)identity.GetType().GetProperty("ClassId")!.GetValue(identity)!;
        }).ToList();
        Assert.That(rolls, Does.Not.Contain(63));
        Assert.That(rolls.Distinct().Count(), Is.EqualTo(13), "every other Hibernian class still comes up");
    }

    [TestCase(true, "VERSION 0.37b")]
    [TestCase(false, "VERSION 0.37")]
    public void VersionLabelNamesTheInstalledEdition(bool customClass, string expected)
    {
        MethodInfo label = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("VersionLabel", HiddenStatic)!;
        Assert.That(label.Invoke(null, [customClass]), Is.EqualTo(expected));
    }

    [TestCase(null, true)]
    [TestCase("True", true)]
    [TestCase("true", true)]
    [TestCase("False", false)]
    [TestCase(" false ", false)]
    public void LauncherReadsTheServerEditionSwitch(string? value, bool expected)
    {
        using var connection = new SQLiteConnection("Data Source=:memory:;Version=3;");
        connection.Open();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT, Value TEXT)";
            setup.ExecuteNonQuery();
            if (value != null)
            {
                setup.CommandText = "INSERT INTO ServerProperty VALUES ('classes','enable_sluaghbinder',@value)";
                setup.Parameters.AddWithValue("@value", value);
                setup.ExecuteNonQuery();
            }
        }
        using var transaction = connection.BeginTransaction();
        MethodInfo read = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("SluaghbinderEnabled", HiddenStatic)!;
        Assert.That(read.Invoke(null, [connection, transaction]), Is.EqualTo(expected));
    }
}
