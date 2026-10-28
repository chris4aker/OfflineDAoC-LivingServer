using System.Collections.Generic;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

// A data quest's QuestDependency entries: a quest name, quest IDs ("#20188", any one of "#20157/20469") and closing
// entries ("!#20478": not while that quest is active or finished). From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_DataQuestDependency
{
    private static readonly List<string> Names = new() { "Regal Nobility", "Entry Into Tomorrow" };
    private static readonly HashSet<int> Finished = new() { 21306, 20157 };
    private static readonly HashSet<int> Active = new() { 20469 };

    private static bool Met(string entry) => QuestDependencies.IsMet(entry, Names, Finished, Active);

    [TestCase("Regal Nobility", true)]
    [TestCase("regal nobility", true)]          // names compare without case, as upstream did
    [TestCase("Hidden Insurrection", false)]
    [TestCase("#21306", true)]
    [TestCase("#21491", false)]
    [TestCase("#21491/21306", true)]            // any one of them
    [TestCase("#20469", false)]                 // active is not finished
    [TestCase("!#21324", true)]                 // neither active nor finished
    [TestCase("!#20157", false)]                // finished closes
    [TestCase("!#20469", false)]                // active closes too
    [TestCase("!#21324/20469", false)]
    [TestCase(" #21306 ", true)]                // spaces around an ID entry don't matter
    public void OneEntry(string entry, bool met) => Assert.That(Met(entry), Is.EqualTo(met));

    [TestCase("#")]
    [TestCase("#x")]
    [TestCase("!#")]
    [TestCase("#21306/")]
    [TestCase("#0")]
    [TestCase("#-5")]
    [TestCase("!#abc")]
    public void AMalformedIdEntryIsNeverMetAndNotValid(string entry)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Met(entry), Is.False);
            Assert.That(QuestDependencies.IsValid(entry), Is.False);
        });
    }

    [Test]
    public void EveryEntryMustBeMet()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDependencies.AreMet(new[] { "#21306", "!#21324" }, Names, Finished, Active), Is.True);
            Assert.That(QuestDependencies.AreMet(new[] { "#21306", "!#20469" }, Names, Finished, Active), Is.False);
            Assert.That(QuestDependencies.AreMet(new[] { "Regal Nobility", "#21491" }, Names, Finished, Active), Is.False);
            Assert.That(QuestDependencies.AreMet(new string[0], Names, Finished, Active), Is.True);
        });
    }

    [Test]
    public void ParsingGivesTheIdsAndTheForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestDependencies.TryParseIds("#20157/20469", out int[] ids, out bool closes), Is.True);
            Assert.That(ids, Is.EqualTo(new[] { 20157, 20469 }));
            Assert.That(closes, Is.False);
            Assert.That(QuestDependencies.TryParseIds("!#20478", out ids, out closes), Is.True);
            Assert.That(ids, Is.EqualTo(new[] { 20478 }));
            Assert.That(closes, Is.True);
            Assert.That(QuestDependencies.TryParseIds("Lord of Deceit", out _, out _), Is.False);
            Assert.That(QuestDependencies.IsIdEntry("Lord of Deceit"), Is.False);
            Assert.That(QuestDependencies.IsValid("Lord of Deceit"), Is.True);
        });
    }
}
