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

    [Test]
    public void VersionLabelNamesTheLivingServerBuildFromForkBuildInfo()
    {
        // The 0.3x line is retired (AGENTS.md, "One edition from now on"): the label says "LIVING SERVER 1.2.2", read from
        // the server's ForkBuildInfo.BuildName (the FORK_BUILD name), never a pinned "VERSION 0.37".
        string source = File.ReadAllText(Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "server", "GameServer", "gameutils", "ForkBuildInfo.cs")));
        string buildName = System.Text.RegularExpressions.Regex.Match(source, "BuildName = \"([^\"]+)\"").Groups[1].Value;
        string expected = "LIVING SERVER " + OfflineDaoc.Updates.BuildVersion.Parse(buildName)!.DisplayName.ToUpperInvariant();
        Type form = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        Assert.That(form.GetMethod("VersionLabel", HiddenStatic)!.Invoke(null, null), Is.EqualTo(expected));
        Assert.That(expected, Does.Not.StartWith("VERSION"));
        MethodInfo title = form.GetMethod("WindowTitle", HiddenStatic)!;
        Assert.That(title.Invoke(null, [false]), Is.EqualTo("Offline DAoC Living Server " + BuildDisplay(buildName) + " — Classic + Shrouded Isles"));
        Assert.That(title.Invoke(null, [true]), Is.EqualTo("Offline DAoC Living Server " + BuildDisplay(buildName) + " — New client"));
    }

    [TestCase("1.2.2", "Living Server 1.2.2")]
    [TestCase("v1.2", "Living Server 1.2")]
    [TestCase("1.2.2-beta", "Living Server Beta 1.2.2")]
    [TestCase("", "Living Server")]
    [TestCase(null, "Living Server")]
    [TestCase("0.37-se.1", "Living Server")]
    public void LivingServerVersionReadsABuildName(string? buildName, string expected)
    {
        MethodInfo version = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!.GetMethod("LivingServerVersion", HiddenStatic)!;
        Assert.That(version.Invoke(null, [buildName]), Is.EqualTo(expected));
    }

    private static string BuildDisplay(string buildName) => OfflineDaoc.Updates.BuildVersion.Parse(buildName)!.DisplayName;

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
