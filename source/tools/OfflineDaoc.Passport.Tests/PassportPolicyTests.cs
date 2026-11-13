// Living Server 1.2: the passport must decide about every saved-progress table. A table added to
// source/tools/OfflineDaoc.ProgressImport/progress-policy.json fails here until PassportTables.Decisions says whether it
// travels, moves by a rule of its own, or stays in its world, and the player guide names it.
using System.Text.Json;
using NUnit.Framework;

namespace OfflineDaoc.Passport.Tests;

[TestFixture]
public class PassportPolicyTests
{
    static string[] PolicyTables()
    {
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progress-policy.json")));
        IEnumerable<string> Read(string name) => policy.RootElement.TryGetProperty(name, out JsonElement list)
            ? list.EnumerateArray().Select(t => t.GetString()!)
            : [];
        return Read("ProgressTables").Concat(Read("OptionalProgressTables")).ToArray();
    }

    [Test]
    public void EveryProgressTableHasAPassportDecision()
    {
        string[] undecided = PolicyTables().Where(t => !PassportTables.Decisions.ContainsKey(t)).ToArray();

        Assert.That(undecided, Is.Empty,
            "progress-policy.json lists saved-progress tables the passport has no decision for. Add each one to PassportTables.Decisions " +
            "(Travels needs a PassportTables.Travelling entry and a row in the round-trip fixture), and to docs/CHARACTER-PASSPORT.md.");
    }

    [Test]
    public void EveryDecisionIsAProgressTable()
    {
        Assert.That(PassportTables.Decisions.Keys.Except(PolicyTables(), StringComparer.OrdinalIgnoreCase), Is.Empty,
            "A passport decision names a table progress-policy.json doesn't list.");
    }

    [Test]
    public void TheTravellingTablesAreExactlyTheOnesDecidedToTravel()
    {
        string[] travels = PassportTables.Decisions.Where(d => d.Value.Decision == PassportDecision.Travels).Select(d => d.Key)
            .Where(t => !t.Equals("ItemUnique", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.That(PassportTables.Travelling.Select(t => t.Table), Is.EquivalentTo(travels));
    }

    [Test]
    public void OnlyMoneyAndCraftingMoveByRule()
    {
        Assert.That(PassportTables.Decisions.Where(d => d.Value.Decision == PassportDecision.MovedByRule).Select(d => d.Key),
            Is.EquivalentTo(new[] { "AccountXMoney", "AccountXCrafting" }));
    }

    [Test]
    public void EveryDecisionSaysWhy()
    {
        Assert.That(PassportTables.Decisions.Where(d => string.IsNullOrWhiteSpace(d.Value.Why)).Select(d => d.Key), Is.Empty);
    }

    [Test]
    public void ThePlayerGuideNamesEveryTable()
    {
        string guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHARACTER-PASSPORT.md"));

        Assert.That(PolicyTables().Where(t => !guide.Contains($"`{t}`", StringComparison.Ordinal)), Is.Empty,
            "docs/CHARACTER-PASSPORT.md should say what happens to each saved-progress table.");
    }
}
