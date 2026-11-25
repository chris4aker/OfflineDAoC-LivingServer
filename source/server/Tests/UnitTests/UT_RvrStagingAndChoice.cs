using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Border-keep staging, warband size rolls and the event layer's intent choice. The dead RvR director and its
/// tests were deleted (#530); these tests lived in its file and test live code.</summary>
[TestFixture]
public sealed class UT_RvrStagingAndChoice
{
    [Test]
    public void EveryRealmStagesAtItsClassicSafeBorderKeep()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrStaging.TryGetBorderKeep(eRealm.Albion, out var albion), Is.True);
            Assert.That(albion.Name, Is.EqualTo("Castle Sauvage"));
            Assert.That(albion.RegionId, Is.EqualTo(1));
            Assert.That(AutonomousRvrStaging.TryGetBorderKeep(eRealm.Midgard, out var midgard), Is.True);
            Assert.That(midgard.Name, Is.EqualTo("Svasud Faste"));
            Assert.That(midgard.RegionId, Is.EqualTo(100));
            Assert.That(AutonomousRvrStaging.TryGetBorderKeep(eRealm.Hibernia, out var hibernia), Is.True);
            Assert.That(hibernia.Name, Is.EqualTo("Druim Ligen"));
            Assert.That(hibernia.RegionId, Is.EqualTo(200));
        });
    }

    [Test]
    public void RvrSupportsSoloThroughEightAndHasNoSinglePullActor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrStaging.RollWarbandSize(8, 0), Is.EqualTo(1));
            Assert.That(AutonomousRvrStaging.RollWarbandSize(8, .999999), Is.EqualTo(8));
            Assert.That(AutonomousRvrStaging.UsesIndependentCombatActors(eAutonomousObjectiveKind.RvR), Is.True);
            Assert.That(AutonomousRvrStaging.UsesIndependentCombatActors(eAutonomousObjectiveKind.GroupPve), Is.False);
        });
    }

    [Test]
    public void WarbandTargetPressureSpreadsButStaysWithinTheClosestPartySizedSet()
    {
        int[] selections = Enumerable.Range(10, 8)
            .Select(id => AutonomousRvrStaging.TargetIndex(id, 20, 8)).ToArray();

        Assert.That(selections.Distinct().Count(), Is.EqualTo(8));
        Assert.That(selections, Has.All.InRange(0, 7));
        Assert.That(AutonomousRvrStaging.TargetIndex(99, 1, 8), Is.Zero);
    }

    [Test]
    public void HigherLevelLargerWarbandsHaveARealKeepAssaultChance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.MajorAssaultWeight(8, 50), Is.EqualTo(1));
            Assert.That(AutonomousRvrEventLayer.ChooseIntent(
                new AutonomousRvrEventLayer.Force("high", eRealm.Albion, 8, 50, 2),
                false, true, true, .10), Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultRelicKeep));
            Assert.That(AutonomousRvrEventLayer.ChooseIntent(
                new AutonomousRvrEventLayer.Force("low", eRealm.Albion, 3, 30, 1),
                false, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.Roam));
            Assert.That(AutonomousRvrEventLayer.ShouldJoinActiveEvent(
                new AutonomousRvrEventLayer.Force("reserve", eRealm.Albion, 8, 50, 2),
                AutonomousRvrEventLayer.OrdinaryAssaultCap - 2, AutonomousRvrEventLayer.OrdinaryAssaultCap, false, false, 0), Is.False);
        });
    }
}
