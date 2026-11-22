using System.Numerics;
using NUnit.Framework;
using P = DOL.GS.AutonomousRouteRecoveryPolicy;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.1 (#420, #421): identical terminal-pocket escapes for one goal are counted, and the third is not made
/// (AutonomousWorldBotController.TryEscapeTerminalRoutePocket logs AUTONOMOUS_ROUTE_POCKET_ESCAPE_LOOP and gives the goal
/// up). The points are the issue's: the Connacht hill camp at 200:314846,471535 and the pocket's escape, the road at
/// 311960,470002. Automated only; the loop's end needs the owner's PC log.
/// </summary>
[TestFixture]
public sealed class UT_PocketEscapeLoop
{
    private static readonly Vector3 ConnachtRoad = new(311960, 470002, 5200);
    private const long Minute = 60_000;

    [Test]
    public void TheThirdIdenticalRescueForOneCampIsALoop()
    {
        var run = new P.PocketEscapeRun();
        string camp = P.PocketEscapeGoal("hib-connacht-hill", null);
        Assert.Multiple(() =>
        {
            Assert.That(run.WouldLoop(camp, ConnachtRoad, 0), Is.False, "the first rescue is made");
            Assert.That(run.Note(camp, ConnachtRoad, 0), Is.EqualTo(1));
            Assert.That(run.WouldLoop(camp, ConnachtRoad + new Vector3(100, -80, 0), 4 * Minute), Is.False, "the second too");
            Assert.That(run.Note(camp, ConnachtRoad + new Vector3(100, -80, 0), 4 * Minute), Is.EqualTo(2));
            Assert.That(run.WouldLoop(camp, ConnachtRoad, 9 * Minute), Is.True, "the third lands where two did not help");
        });
        run.Clear();
        Assert.That(run.WouldLoop(camp, ConnachtRoad, 9 * Minute), Is.False, "given up: a later goal starts again");
    }

    [Test]
    public void AnotherGoalAnotherEscapeOrHalfAnHourLaterIsNoLoop()
    {
        var run = new P.PocketEscapeRun();
        string camp = P.PocketEscapeGoal("hib-connacht-hill", null);
        run.Note(camp, ConnachtRoad, 0);
        run.Note(camp, ConnachtRoad, Minute);
        Assert.Multiple(() =>
        {
            Assert.That(run.WouldLoop(P.PocketEscapeGoal("hib-other-camp", null), ConnachtRoad, 2 * Minute), Is.False, "another camp");
            Assert.That(run.WouldLoop(camp, ConnachtRoad + new Vector3(P.PocketEscapeSameSpotRadius + 50, 0, 0), 2 * Minute), Is.False,
                "another escape point");
            Assert.That(run.WouldLoop(camp, ConnachtRoad, P.PocketEscapeLoopWindowMilliseconds + 1), Is.False, "past the window");
        });
        Assert.That(run.Note(P.PocketEscapeGoal("hib-other-camp", null), ConnachtRoad, 2 * Minute), Is.EqualTo(1),
            "a new goal starts a new run");
    }

    [Test]
    public void AGoalWithoutACampIsItsPointOnAGrid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(P.PocketEscapeGoal(null, new Vector3(314846, 471535, 5600)),
                Is.EqualTo(P.PocketEscapeGoal(null, new Vector3(314700, 471300, 5700))), "the same 512-unit cell");
            Assert.That(P.PocketEscapeGoal(null, new Vector3(314846, 471535, 0)),
                Is.Not.EqualTo(P.PocketEscapeGoal(null, new Vector3(384960, 486688, 0))), "Lough Derg is another goal");
            Assert.That(P.PocketEscapeGoal("", null), Is.EqualTo("none"));
            Assert.That(P.PocketEscapeGoal("c1", new Vector3(1, 2, 3)), Is.EqualTo("camp:c1"));
            Assert.That(P.PocketEscapesBeforeLoop, Is.EqualTo(3));
        });
    }
}
