using System.Numerics;
using System.Reflection;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>#438 (1.2.1): GM "/jump &lt;bot&gt; to me" finds bots by name. 1.2.2: a dead bot is refused with a reason, and a
/// jumped gamebot forgets the trip it planned from the old spot. Tests only.</summary>
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
    [Test]
    public void Refusal_ADeadBotOrOneOutOfTheWorldIsNotMoved()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JumpBotTargets.Refusal(inWorld: true, alive: true), Is.EqualTo(JumpBotTargets.MoveRefusal.None));
            Assert.That(JumpBotTargets.Refusal(inWorld: true, alive: false), Is.EqualTo(JumpBotTargets.MoveRefusal.Dead),
                "a corpse stays where it fell (GameBot.RefuseCorpseMove)");
            Assert.That(JumpBotTargets.Refusal(inWorld: false, alive: true), Is.EqualTo(JumpBotTargets.MoveRefusal.NotInWorld));
            Assert.That(JumpBotTargets.Refusal(inWorld: false, alive: false), Is.EqualTo(JumpBotTargets.MoveRefusal.NotInWorld));
            Assert.That(JumpBotTargets.Refusal((GameBot)null), Is.EqualTo(JumpBotTargets.MoveRefusal.NotInWorld));
            Assert.That(JumpBotTargets.TryMove(null, 1, 0, 0, 0, 0, null), Is.False);
        });
    }

    [Test]
    public void FailureText_SaysWhyTheJumpFailed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JumpBotTargets.FailureText("Brannoc", JumpBotTargets.MoveRefusal.Dead),
                Does.StartWith("Brannoc is dead").And.Contains("resurrect it there"));
            Assert.That(JumpBotTargets.FailureText("Brannoc", JumpBotTargets.MoveRefusal.NotInWorld),
                Is.EqualTo("Brannoc is not in the world any more."));
            // The text the playtester's bot-jump scenario and the 1.2.1 command already used for any other failure.
            Assert.That(JumpBotTargets.FailureText("Brannoc", JumpBotTargets.MoveRefusal.None),
                Is.EqualTo("Brannoc could not be moved there."));
        });
    }

    [Test]
    public void AJumpedGamebotForgetsTheTripItPlannedFromTheOldSpot()
    {
        // The controller's in-flight trip (a camp, a patrol point, a stable ride it meant to take, its next plan time) was
        // planned from where the bot stood; after a GM's /jump it plans again (AutonomousWorldBotController.ForgetTripAfterGmMove).
        var controller = new AutonomousWorldBotController();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo Field(string name) => typeof(AutonomousWorldBotController).GetField(name, hidden)
            ?? throw new AssertionException("AutonomousWorldBotController has no field " + name);
        Field("_patrolDestination").SetValue(controller, (Vector3?)new Vector3(100, 200, 300));
        Field("_nextPlanTick").SetValue(controller, 123_456L);
        Field("_campStartedTick").SetValue(controller, 42L);
        Field("_nextPorterSearch").SetValue(controller, 99L);
        Field("_keepTravelKey").SetValue(controller, "old-keep-route");

        MethodInfo forget = typeof(AutonomousWorldBotController).GetMethod("ForgetTripAfterGmMove", hidden)!;
        forget.Invoke(controller, [null]);

        Assert.Multiple(() =>
        {
            Assert.That(Field("_patrolDestination").GetValue(controller), Is.Null);
            Assert.That(Field("_nextPlanTick").GetValue(controller), Is.EqualTo(0L), "it plans again on its next turn");
            Assert.That(Field("_campStartedTick").GetValue(controller), Is.EqualTo(0L));
            Assert.That(Field("_nextPorterSearch").GetValue(controller), Is.EqualTo(0L));
            Assert.That(Field("_keepTravelKey").GetValue(controller), Is.Null, "a keep route is planned again from the new spot");
            Assert.That(Field("_camp").GetValue(controller), Is.Null);
            Assert.That(Field("_pendingStableChoice").GetValue(controller), Is.Null);
        });
    }
}
