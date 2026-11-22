using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>#438 (1.2.1): GM "/jump &lt;bot&gt; to me" finds bots by name. Tests only.</summary>
[TestFixture]
public class UT_JumpBotTargets
{
    private sealed record Bot(string Name, int Rank);

    private static Bot Pick(Bot[] bots, string name, out JumpBotTargets.NameMatch match) =>
        JumpBotTargets.Pick(bots, name, bot => bot.Name, bot => bot.Rank, out match);

    [Test]
    public void ExactName_AnyCase_WinsOverPartialAndOverRank()
    {
        Bot[] bots = [new("Aldwin", 0), new("Ald", 3), new("Aldric", 0)];
        Bot found = Pick(bots, "ald", out var match);
        Assert.Multiple(() =>
        {
            Assert.That(found?.Name, Is.EqualTo("Ald"), "an exact name beats a GM-owned partial match");
            Assert.That(match, Is.EqualTo(JumpBotTargets.NameMatch.Exact));
        });
    }

    [Test]
    public void PartialName_PrefersTheGmsOwnBots_AndRefusesATieInTheBestRank()
    {
        Bot[] bots = [new("Brannoc", 3), new("Branwen", 1)];
        Assert.That(Pick(bots, "bran", out var match)?.Name, Is.EqualTo("Branwen"), "the GM's group first");
        Assert.That(match, Is.EqualTo(JumpBotTargets.NameMatch.Partial));

        Bot[] tie = [new("Brannoc", 1), new("Branwen", 1)];
        Assert.Multiple(() =>
        {
            Assert.That(Pick(tie, "bran", out var tied), Is.Null);
            Assert.That(tied, Is.EqualTo(JumpBotTargets.NameMatch.Ambiguous));
            Assert.That(Pick(tie, "branw", out _)?.Name, Is.EqualTo("Branwen"));
        });
    }

    [Test]
    public void NoMatch_BlankName_AndNullCandidates()
    {
        Bot[] bots = [new("Cadoc", 0), null];
        Assert.Multiple(() =>
        {
            Assert.That(Pick(bots, "zz", out var none), Is.Null);
            Assert.That(none, Is.EqualTo(JumpBotTargets.NameMatch.None));
            Assert.That(Pick(bots, "  ", out _), Is.Null);
            Assert.That(JumpBotTargets.Pick<Bot>(null, "x", b => b.Name, b => b.Rank, out _), Is.Null);
            Assert.That(Pick(bots, " cadoc ", out _)?.Name, Is.EqualTo("Cadoc"), "the name is trimmed");
        });
    }

    [Test]
    public void Rank_OwnThenGroupThenRegionThenElsewhere()
    {
        Assert.That(new[]
        {
            JumpBotTargets.Rank(true, true, true), JumpBotTargets.Rank(false, true, false),
            JumpBotTargets.Rank(false, false, true), JumpBotTargets.Rank(false, false, false),
        }, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Choose_ExactPlayerThenExactBotThenPartialPlayerThenPartialBot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JumpBotTargets.Choose(true, true, JumpBotTargets.NameMatch.Exact), Is.EqualTo(1));
            Assert.That(JumpBotTargets.Choose(true, false, JumpBotTargets.NameMatch.Exact), Is.EqualTo(2),
                "a bot named exactly beats a player whose name only starts with the text");
            Assert.That(JumpBotTargets.Choose(true, false, JumpBotTargets.NameMatch.Partial), Is.EqualTo(1));
            Assert.That(JumpBotTargets.Choose(false, false, JumpBotTargets.NameMatch.Partial), Is.EqualTo(2));
            Assert.That(JumpBotTargets.Choose(false, false, JumpBotTargets.NameMatch.Exact), Is.EqualTo(2),
                "the report: \"/jump <bot> to me\" with no player of that name");
            Assert.That(JumpBotTargets.Choose(false, false, JumpBotTargets.NameMatch.Ambiguous), Is.EqualTo(0));
            Assert.That(JumpBotTargets.Choose(false, false, JumpBotTargets.NameMatch.None), Is.EqualTo(0));
        });
    }
}
