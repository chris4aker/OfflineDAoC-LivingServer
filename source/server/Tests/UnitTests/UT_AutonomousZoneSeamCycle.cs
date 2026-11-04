using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AutonomousZoneSeamCycle
{
    private static AutonomousZoneBoundaryRouting.Step Step(float ix, float iy, float ox, float oy) =>
        new(new Vector3(ix, iy, 5706), new Vector3(ox, oy, 5706));

    [Test]
    public void TheVindsaulFasteStepsAreEachOthersReversal()
    {
        // 2026-10-28: West Svealand (102) to Yggdra Forest (112), then back.
        var toSouth = Step(704544, 737344, 704544, 737216);
        var toNorth = Step(704288, 737216, 704288, 737354);
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(toSouth, toNorth), Is.True);
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(toNorth, toSouth), Is.True);
    }

    [Test]
    public void AStepTakenBackExactlyIsAReversal()
    {
        var step = Step(1000, 2000, 1000, 2140);
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(step, Step(1000, 2140, 1000, 2000)), Is.True);
    }

    [Test]
    public void ARouteThatGoesOnIsNotAReversal()
    {
        var first = Step(1000, 2000, 1000, 2140);
        // The next border of the chain, far from the first one.
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, Step(1000, 9000, 1000, 9140)), Is.False);
        // The same step again (not taken back).
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, first), Is.False);
        // Same border, inside and outside not swapped.
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, Step(1000, 2000, 1000, 2140)), Is.False);
    }

    [Test]
    public void ANeighbouringStepInTheSameDirectionIsNotAReversal()
    {
        // The next crossing of the same border, 100 units along it, going the same way.
        var first = Step(1000, 2000, 1000, 2140);
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, Step(1100, 2000, 1100, 2140)), Is.False);
        // A step across the border at right angles to the first (a corner), however near.
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, Step(1000, 2140, 1140, 2140)), Is.False);
    }

    [Test]
    public void TheToleranceDecidesHowFarApartTheTwoStepsMayBe()
    {
        var first = Step(0, 0, 0, 140);
        var farBack = Step(500, 140, 500, 0);
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, farBack), Is.False, "500 units apart: a different crossing");
        Assert.That(AutonomousZoneBoundaryRouting.IsReversal(first, farBack, 600), Is.True);
    }

    [Test]
    public void ThreeReversalsInARowBreakTheCycle() =>
        Assert.That(AutonomousWorldBotController.MaximumSeamReversals, Is.EqualTo(3));
}
