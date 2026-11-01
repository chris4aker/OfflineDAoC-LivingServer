using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Relic audit 2026-10-31: the rules behind the relic raid fixes (B1-B5, L1, L5, L6).</summary>
[TestFixture, NonParallelizable]
public sealed class UT_RelicRaidFixes
{
    [SetUp] public void ResetEvents() => RvrEventTestState.Clear();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.LiveObjective DunDagda() =>
        new("rvr-keep-110", "Dun Dagda", AutonomousRvrEventLayer.Intent.AssaultRelicKeep, eRealm.Hibernia, 200,
            401036, 465147, 2888, true, 0, 0, 12, 9);

    private sealed class StraightMesh : PathfindingMgrBase
    {
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters,
            Span<WrappedPathfindingNode> destination)
        {
            destination[0] = new(start, EDtPolyFlags.Walk);
            destination[1] = new(end, EDtPolyFlags.Walk);
            return new(PathfindingStatus.PathFound, 2);
        }
    }

    [Test]
    public void AWalkPassesADoorOnlyWhenItRunsThroughItOnTheSameFloor()
    {
        Vector3[] walk = [new(0, 0, 0), new(1000, 0, 0), new(1000, 1000, 0)];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRelicShrine.WalkPassesDoor(walk, new(500, 60, 0)), Is.True, "on the first leg");
            Assert.That(AutonomousRelicShrine.WalkPassesDoor(walk, new(1040, 600, 30)), Is.True, "on the second leg");
            Assert.That(AutonomousRelicShrine.WalkPassesDoor(walk, new(500, 400, 0)), Is.False, "beside the walk");
            Assert.That(AutonomousRelicShrine.WalkPassesDoor(walk, new(500, 0, 832)), Is.False, "a floor above (the shrine)");
            Assert.That(AutonomousRelicShrine.WalkPassesDoor([], new(0, 0, 0)), Is.False);
        });
    }

    [Test]
    public void APlannedWalkThroughAShutDoorTheNavmeshDoesNotKnowIsNoWalk()
    {
        // The six relic gates (door 02 of each relic keep) are not registered in the navmesh: a shut one lay on the open
        // walk to every shrine (UT_RelicShrineReach). The attackers' planning view now checks them itself (B3/B2).
        var mesh = new StraightMesh();
        Vector3 from = new(0, 0, 0), shrine = new(2000, 0, 0);
        var blocked = new AutonomousKeepApproachNavigation(mesh, [], [new(1000, 20, 0)]);
        var aside = new AutonomousKeepApproachNavigation(mesh, [], [new(1000, 900, 0)]);
        var unaware = new AutonomousKeepApproachNavigation(mesh, []);
        var nodes = new WrappedPathfindingNode[8];
        Assert.Multiple(() =>
        {
            Assert.That(blocked.GetPathStraight(null, from, shrine, mesh.DefaultFilters, nodes).Status, Is.EqualTo(PathfindingStatus.NoPathFound));
            Assert.That(aside.GetPathStraight(null, from, shrine, mesh.DefaultFilters, nodes).Status, Is.EqualTo(PathfindingStatus.PathFound));
            Assert.That(unaware.GetPathStraight(null, from, shrine, mesh.DefaultFilters, nodes).Status, Is.EqualTo(PathfindingStatus.PathFound));
        });
    }

    [Test]
    public void CampaignRetriesBackOffToHalfAnHour()
    {
        Assert.That(new[] { 1, 2, 3, 4, 9 }.Select(AutonomousRvrEventLayer.CampaignRetryDelay).Select(ms => ms / 60_000),
            Is.EqualTo(new long[] { 5, 10, 20, 30, 30 }));
    }

    [Test]
    public void CampaignTimeCountsFromProgress()
    {
        // L1: six hours without a sentinel keep taken, not six hours from the start.
        Assert.That(AutonomousRvrEventLayer.CampaignIdleMilliseconds, Is.EqualTo(6 * 60 * 60_000L));
        Assert.That(AutonomousRvrEventLayer.CampaignIdleMilliseconds, Is.GreaterThanOrEqualTo(6 * 60 * 60_000L));
        Assert.That(AutonomousRvrEventLayer.CampaignMaxStepsWithoutProgress, Is.EqualTo(3));
    }

    [Test]
    public void ARelicSiegeThatCannotStartKeepsItsCampaign()
    {
        // B4: the campaign was removed before the siege was tried; a keep already under siege ended it for good.
        var relic = DunDagda();
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Albion, 1, out _), Is.True, "Albion's own relic siege is in the way");
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic, eRealm.Midgard, 2, 60_000, 60 * 60_000, 0, "test"), Is.True);

        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 3, out string reason), Is.False, reason);
        Assert.That(AutonomousRvrEventLayer.HasRelicCampaign(relic.Id), Is.True, "the campaign waits for its retry");
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Hibernia, 4, out _), Is.False, "the owner cannot attack itself");
    }

    [Test]
    public void ACampaignEndsOnlyOnceItsRelicSiegeIsUnderWay()
    {
        var relic = DunDagda();
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic, eRealm.Midgard, 1, 60_000, 60 * 60_000, 0, "test"), Is.True);
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Albion, 2, out string other), Is.False,
            "one campaign per relic keep: " + other);
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 3, out string reason), Is.True, reason);
        Assert.That(AutonomousRvrEventLayer.HasRelicCampaign(relic.Id), Is.False);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(relic.Id, 4), Is.True);
    }

    [Test]
    public void ACampaignWhoseSiegeIsAlreadyRunningIsDone()
    {
        var relic = DunDagda();
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 1, out _), Is.True);
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic, eRealm.Midgard, 2, 60_000, 60 * 60_000, 0, "test"), Is.True);
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 3, out _), Is.True);
        Assert.That(AutonomousRvrEventLayer.HasRelicCampaign(relic.Id), Is.False);
    }

    [Test]
    public void TheCarrierEventHasItsOwnWindowAndEndsWhenTheRelicGoesBack()
    {
        // L6: the carrier event kept the siege's leftover time. L5: a relic back on its pad by itself never ended it.
        var relic = DunDagda();
        const string carrier = "rvr-relic-carrier-3-1";
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 1_000, out _), Is.True);
        AutonomousRvrEventLayer.TransferToRelicCarrier(relic.Id, carrier, 2_000);
        Assert.That(AutonomousRvrEventLayer.CarrierRemainingMilliseconds(carrier, 2_000),
            Is.EqualTo(AutonomousRvrEventLayer.CarrierEventMilliseconds));
        Assert.That(AutonomousRvrEventLayer.RelicStateSnapshot(2_000).Select(s => (s.RelicKeepId, s.Attacker, s.Source)),
            Does.Contain((relic.Id, eRealm.Midgard, "carrier")), "a carried relic's raid is saved for a restart");

        AutonomousRvrEventLayer.EndTarget(carrier, 3_000, eRealm.None, "returned");
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(carrier, 3_000), Is.False);
        Assert.That(AutonomousRvrEventLayer.RelicStateSnapshot(3_000), Is.Empty);
    }

    [Test]
    public void RelicRaidsAreSavedAndResumedAsCampaigns()
    {
        // B5: events, campaigns and carrier events were memory only; every restart wiped them.
        var relic = DunDagda();
        Assert.That(AutonomousRvrEventLayer.ForceStart(relic, eRealm.Midgard, 1, out _), Is.True);
        var saved = AutonomousRvrEventLayer.RelicStateSnapshot(2);
        Assert.That(saved.Select(s => (s.RelicKeepId, s.Attacker, s.Source)), Is.EqualTo(new[] { (relic.Id, eRealm.Midgard, "siege") }));

        RvrEventTestState.Clear();   // the restart
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic, saved[0].Attacker, 10, 5 * 60_000,
            saved[0].IdleRemainingMilliseconds, saved[0].StepsWithoutProgress, saved[0].Source), Is.True);
        Assert.That(AutonomousRvrEventLayer.HasRelicCampaign(relic.Id), Is.True);
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic, eRealm.Midgard, 11, 1, 1, 0, "again"), Is.False, "once");
        Assert.That(AutonomousRvrEventLayer.RelicStateSnapshot(12).Single().Source, Is.EqualTo("campaign"));
        Assert.That(AutonomousRvrEventLayer.RestoreRelicCampaign(relic with { Id = "rvr-keep-82" }, eRealm.Hibernia, 13, 1, 1, 0, "own"),
            Is.False, "never against the realm's own relic keep");
    }
}
