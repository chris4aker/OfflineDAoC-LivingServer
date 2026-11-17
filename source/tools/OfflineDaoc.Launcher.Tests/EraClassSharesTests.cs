using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Era builds 1.2, WP2 (docs/ERA-BUILDS-1.2.md): the launcher's batches draw each new gamebot's class from its realm's 2003
/// share (bot_era_class_shares, on by default), from the same embedded catalog the server reads; off, evenly. Never class 63.
/// </summary>
public sealed class EraClassSharesTests
{
    private static readonly Assembly Launcher = typeof(EraClassShares).Assembly;
    private static readonly Type Generator = Launcher.GetType("OfflineDaoc.Launcher.BotCharacterGenerator")!;
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;

    private static int Draw(int realm, bool era, Random random)
    {
        object choice = Generator.GetMethod("DrawClass", Hidden)!.Invoke(null, [realm, era, random])!;
        return (int)choice.GetType().GetProperty("ClassId")!.GetValue(choice)!;
    }

    private static IReadOnlyList<int> ClassIds(int realm) =>
        (IReadOnlyList<int>)Generator.GetMethod("ClassIds", Hidden)!.Invoke(null, [realm])!;

    [Test]
    public void TheLauncherEmbedsTheServersCatalog()
    {
        EraBuildCatalog catalog = EraBuildCatalog.Default;
        Assert.That(catalog.IsLoaded, Is.True, string.Join("; ", catalog.Problems));
        Assert.That(catalog.Problems, Is.Empty);
        Assert.That(catalog.Classes, Has.Count.EqualTo(39));
        Assert.That(catalog.BuildCount, Is.EqualTo(106));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TenThousandBotsComeWithinOneAndAHalfPointsOfEachShare(int realm)
    {
        var random = new Random(1665 + realm);
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < 10_000; i++)
        {
            int id = Draw(realm, true, random);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        foreach (EraClassShare share in EraBuildCatalog.Default.Shares(realm))
            Assert.That(counts.GetValueOrDefault(share.ClassId) / 100.0, Is.EqualTo(share.Share * 100).Within(1.5), share.ClassName);
        Assert.That(counts.Keys, Is.SubsetOf(ClassIds(realm)));
        Assert.That(counts.ContainsKey(63), Is.False);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void OffEveryClassIsEquallyLikely(int realm)
    {
        var random = new Random(42 + realm);
        IReadOnlyList<int> ids = ClassIds(realm);
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < 10_000; i++)
        {
            int id = Draw(realm, false, random);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        foreach (int id in ids)
            Assert.That(counts.GetValueOrDefault(id) / 100.0, Is.EqualTo(100.0 / ids.Count).Within(1.5), id.ToString());
        Assert.That(ids, Does.Not.Contain(63));
    }

    [TestCase(null, true)]
    [TestCase("True", true)]
    [TestCase("False", false)]
    [TestCase(" false ", false)]
    [TestCase("maybe", true)]
    public void TheSwitchIsReadFromTheWorldAndAMissingRowIsOn(string? value, bool expected)
    {
        using var connection = new SQLiteConnection("Data Source=:memory:;Version=3;");
        connection.Open();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE ServerProperty (Category TEXT, `Key` TEXT, Value TEXT)";
            setup.ExecuteNonQuery();
            if (value != null)
            {
                setup.CommandText = "INSERT INTO ServerProperty VALUES ('bots','bot_era_class_shares',@value)";
                setup.Parameters.AddWithValue("@value", value);
                setup.ExecuteNonQuery();
            }
        }
        using var transaction = connection.BeginTransaction();
        Assert.That(EraClassShares.IsOn(connection, transaction), Is.EqualTo(expected));
    }

    [Test]
    public void TheSwitchIsOnInDefaultsAndRecommendedAndWritten()
    {
        Assert.That(SocialExpansionSettings.Defaults.BotEraClassShares, Is.True);
        Assert.That(SocialExpansionSettings.Recommended.BotEraClassShares, Is.True);
        Assert.That(SocialExpansionSettings.Defaults.ToStoredValues()[EraClassShares.Key], Is.EqualTo("True"));
        Assert.That((SocialExpansionSettings.Defaults with { BotEraClassShares = false }).ToStoredValues()[EraClassShares.Key],
            Is.EqualTo("False"));
    }
}
