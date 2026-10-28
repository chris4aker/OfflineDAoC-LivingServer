using System.Globalization;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

// A data quest's target and giver names match a spawn's name without case. From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_QuestNames
{
    [TestCase("Arawnite Messenger", "arawnite messenger")]
    [TestCase("Cornwall hunter", "cornwall hunter")]
    [TestCase("witch", "Witch")]
    [TestCase("Albion Runner", "Albion runner")]     // a giver (StartName)
    [TestCase("Arawnite Messenger", "Arawnite Messenger")]
    [TestCase("King's Wight", "KING'S WIGHT")]
    [TestCase("", "")]
    public void NamesThatDifferOnlyInCaseAreTheSame(string questName, string objectName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestNames.Same(questName, objectName), Is.True);
            Assert.That(QuestNames.Same(objectName, questName), Is.True);
        });
    }

    [TestCase("Arawnite Messenger", "Arawnite Messenger ")]   // only case is ignored, not spacing
    [TestCase("Arawnite Messenger", "Arawnite  Messenger")]
    [TestCase("Arawnite Messenger", "Arawnite Messengers")]
    [TestCase("Kings Wight", "King's Wight")]
    [TestCase("Druid", "")]
    public void OtherNamesDiffer(string questName, string objectName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestNames.Same(questName, objectName), Is.False);
            Assert.That(QuestNames.Same(objectName, questName), Is.False);
        });
    }

    [Test]
    public void NullMatchesOnlyNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestNames.Same(null, null), Is.True);
            Assert.That(QuestNames.Same(null, ""), Is.False);
            Assert.That(QuestNames.Same("Arawnite Messenger", null), Is.False);
        });
    }

    [Test]
    public void TheServersCultureDoesNotMatter()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.That(QuestNames.Same("Isolationist Courier", "isolationist courier"), Is.True);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
