using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

// #569 step 4: autonomous gamebot meetups, stable landings and town breaks stay
// clear of the merchants, trainers, stable masters, healers and vault keepers players click.
[TestFixture]
public class UT_AutonomousServiceNpcClearance
{
    // A flat town floor: every projection lands where asked; a path is complete on the same height.
    private sealed class FlatMesh : PathfindingMgrBase
    {
        public bool Available = true;
        public bool IsolatedAnchor;
        public int Samples;
        public override bool IsAvailable => Available;
        public override bool HasNavmesh(Zone zone) => Available;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters) => position;
        public override Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters)
        {
            Samples++;
            return position;
        }
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
        {
            bool connected = !IsolatedAnchor && to.Z == from.Z;
            nodes[0] = new(connected ? to : from, EDtPolyFlags.Walk);
            return new(connected ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound, 1);
        }
    }

    // The landing mesh: projections can be shifted off the asked spot, paths can be cut.
    private sealed class LandingMesh : PathfindingMgrBase
    {
        public bool Connected = true;
        public float FloorShift;
        public int Projections;
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters)
        {
            Projections++;
            return position + new Vector3(FloorShift, 0, 0);
        }
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
        {
            nodes[0] = new(Connected ? to : from, EDtPolyFlags.Walk);
            return new(Connected ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound, 1);
        }
    }

    private static Zone TestZone => new(null, 2, "Town", 0, 0, 65536, 65536, 2, false, 0, false, 0, 0, 0, 0, 0);

    private static float Across(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

    [Test]
    public void MeetupBesideAServiceNpcIsInTheClearBandNotOnIt()
    {
        var nav = new FlatMesh();
        Vector3 npc = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 17, null, out Vector3 chosen), Is.True);
        float across = Across(chosen, npc);
        Assert.That(across, Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.MeetClearRadius));
        Assert.That(across, Is.LessThanOrEqualTo(AutonomousServiceNpcClearance.MeetOuterRadius));
        Assert.That(across, Is.EqualTo(420f).Within(1f), "the farther ring is preferred");
        Assert.That(nav.Samples, Is.Zero, "no random sampling: fixed rings, once per formation");
    }

    [Test]
    public void ARejectedFarRingFallsBackToTheNearerRingThenFails()
    {
        var nav = new FlatMesh();
        Vector3 npc = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 0,
            p => Across(p, npc) < 400, out Vector3 chosen), Is.True);
        Assert.That(Across(chosen, npc), Is.EqualTo(340f).Within(1f));
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 0, _ => false, out _), Is.False,
            "no clear spot: the caller keeps its old choice");
    }

    [Test]
    public void AnotherServiceNpcCloseByPushesTheMeetupAwayFromItToo()
    {
        var nav = new FlatMesh();
        Vector3 stable = new(1000, 1000, 0);
        Vector3 merchant = new(1420, 1000, 0); // exactly where the first spot of seed 0 would be
        Vector3[] services = [stable, merchant];
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, stable, 0,
            p => AutonomousServiceNpcClearance.IsClearOf(p, services, AutonomousServiceNpcClearance.MeetClearRadius),
            out Vector3 chosen), Is.True);
        Assert.That(Across(chosen, stable), Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.MeetClearRadius));
        Assert.That(Across(chosen, merchant), Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.MeetClearRadius));
    }

    [Test]
    public void TwoPartiesAtOneNpcGetDifferentSpots()
    {
        var nav = new FlatMesh();
        Vector3 npc = new(1000, 1000, 0);
        AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 10, null, out Vector3 a);
        AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 200, null, out Vector3 b);
        Assert.That(Vector3.Distance(a, b), Is.GreaterThan(150f));
        // Leader ids give spread seeds: neighbouring ids do not share a start angle.
        var angles = Enumerable.Range(1, 40)
            .Select(id => (AutonomousBotGroupCoordinator.ServiceMeetupSeed(id) & 0x7fffffff) % 360).Distinct().Count();
        Assert.That(angles, Is.GreaterThan(30));
    }

    [Test]
    public void AClearPointNeedsAConnectedFloor()
    {
        Vector3 npc = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(new FlatMesh { IsolatedAnchor = true }, TestZone,
            npc, 0, null, out _), Is.False);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(new FlatMesh { Available = false }, TestZone,
            npc, 0, null, out _), Is.False);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(new FlatMesh(), null, npc, 0, null, out _), Is.False);
    }

    [Test]
    public void TheOldChoiceStillKeepsItsAnchorForStreetsAndCamps()
    {
        // Only service NPC anchors go through the clear band; the plain choice is unchanged.
        var nav = new FlatMesh();
        Vector3 anchor = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChoosePoint(nav, TestZone, anchor, out Vector3 chosen), Is.True);
        Assert.That(chosen, Is.EqualTo(anchor));
    }

    [Test]
    public void ClearBandAndServiceClearance()
    {
        Vector3 npc = new(0, 0, 0);
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(80, 0, 0)), Is.False, "the old ring on the NPC");
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(350, 0, 0)), Is.True);
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(600, 0, 0)), Is.False, "too far to be beside it");
        Vector3[] services = [new(0, 0, 0), new(1000, 0, 0)];
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(500, 0, 0), services, 300), Is.True);
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(800, 0, 0), services, 300), Is.False);
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(800, 0, 900), services, 300), Is.True, "another floor");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1, 1, 0), null, 300), Is.True);
        Assert.That(AutonomousServiceNpcClearance.ServicePositions(null), Is.Empty);
        Assert.That(AutonomousServiceNpcClearance.IsServiceNpc(null), Is.False);
    }

    [Test]
    public void TownBreakSpotsKeepClearOfServiceNpcs()
    {
        Vector3[] services = [new(1_000, 1_000, 0), new(2_000, 1_000, 0)];
        float clear = AutonomousServiceNpcClearance.TownBreakClearRadius;
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_100, 1_000, 0), services, clear), Is.False,
            "100 units from a merchant: rejected");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_500, 1_000, 0), services, clear), Is.True,
            "between two NPCs, 500 from each");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_000, 1_000 + clear, 0), services, clear), Is.True,
            "exactly at the edge counts as clear");
        Assert.That(AutonomousWorldBotController.TownBreakCandidates, Is.EqualTo(6), "two more samples for the rejected spots");
    }

    [Test]
    public void LandingStepOffIsOwnSpotAwayFromTheStableMaster()
    {
        Vector3 end = new(10_000, 10_000, 0);
        Vector3 master = new(10_000, 9_700, 0); // the master is south of the route end
        Vector2 away = Vector2.Normalize(new(end.X - master.X, end.Y - master.Y));
        var spots = new HashSet<Vector3>();
        for (long id = 1; id < 400; id++)
        {
            Vector3 spot = AutonomousServiceNpcClearance.LandingStepOff(end, master, id);
            Vector2 step = new(spot.X - end.X, spot.Y - end.Y);
            Assert.That(step.Length(), Is.InRange(AutonomousServiceNpcClearance.LandingStepMin - 0.01f,
                AutonomousServiceNpcClearance.LandingStepMax + 0.01f));
            Assert.That(Vector2.Dot(Vector2.Normalize(step), away),
                Is.GreaterThanOrEqualTo(MathF.Cos((AutonomousServiceNpcClearance.LandingAwayDegrees + 0.5f) * MathF.PI / 180f)),
                "never toward the master");
            Assert.That(spot, Is.EqualTo(AutonomousServiceNpcClearance.LandingStepOff(end, master, id)), "the same bot, the same spot");
            spots.Add(spot);
        }
        Assert.That(spots.Count, Is.GreaterThan(300), "riders of one route spread out");
        Vector3 noMaster = AutonomousServiceNpcClearance.LandingStepOff(end, null, 7);
        Assert.That(Across(noMaster, end), Is.InRange(AutonomousServiceNpcClearance.LandingStepMin - 0.01f,
            AutonomousServiceNpcClearance.LandingStepMax + 0.01f));
    }

    [Test]
    public void LandingStepOffNeedsAConnectedFloorNearTheSpot()
    {
        Vector3 end = new(10_000, 10_000, 0);
        var nav = new LandingMesh();
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(nav, TestZone, end, null, 5, out Vector3 spot), Is.True);
        Assert.That(nav.Projections, Is.EqualTo(1), "one floor projection per arrival");
        Assert.That(Vector3.Distance(spot, end), Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.LandingStepMin - 0.01f));
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(new LandingMesh { Connected = false }, TestZone, end, null, 5, out _),
            Is.False, "an island or prop top keeps the rider where it landed");
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(new LandingMesh { FloorShift = 90 }, TestZone, end, null, 5, out _),
            Is.False, "the floor found is far from the chosen spot");
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(nav, null, end, null, 5, out _), Is.False);
        // A landing at the zone's edge whose step would leave the zone stays put.
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(nav, TestZone, new(1, 1, 0), new Vector3(200, 200, 0), 5, out _),
            Is.False);
    }
    // The four wiring sites (review of #763): the pure rules above pass even when a caller stops using them, so the
    // call sites are pinned in the source text. Reverting any of them fails here.
    private static string Source(string relative)
    {
        string root = TestContext.CurrentContext.TestDirectory;
        for (int i = 0; i < 10 && root != null && !File.Exists(Path.Combine(root, relative)); i++)
            root = Path.GetDirectoryName(root);
        if (root == null || !File.Exists(Path.Combine(root, relative)))
            Assert.Fail($"{relative} not found above the test folder.");
        return Regex.Replace(File.ReadAllText(Path.Combine(root, relative)), @"\s+", " ");
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.That(from, Is.GreaterThanOrEqualTo(0), start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.That(to, Is.GreaterThan(from), end);
        return text.Substring(from, to - from);
    }

    [Test]
    public void EveryChooserUsesTheClearance()
    {
        const string Bots = "source/server/GameServer/bots/";
        string coordinator = Source(Bots + "autonomous/AutonomousBotGroupCoordinator.cs");
        string gameBot = Source(Bots + "GameBot.cs");
        string townIdle = Source(Bots + "autonomous/AutonomousTownIdleRouting.cs");
        Assert.Multiple(() =>
        {
            // 1. The town-near-camp chooser stands beside the NPC first.
            Assert.That(coordinator, Does.Contain(
                "if (!TryChooseMeetupAtTownNpc(nav, region, zone, npc, leader, members, out Vector3 chosen)) continue;"));
            // 2. TryPoint's keepClearOf, used where the leader stands and at the nearby town NPCs, and the recovery regroup.
            Assert.That(coordinator, Does.Contain(
                "if (keepClearOf != null && TryChooseMeetupBesideService(nav, targetRegion, keepClearOf, leader, members, clearWhere, out chosen)) return true;"));
            Assert.That(coordinator, Does.Contain(
                "keepClearOf: NearestServiceNpc(leader, (ushort)AutonomousServiceNpcClearance.MeetClearRadius), clearWhere: \"leader_town\""));
            Assert.That(coordinator, Does.Contain("keepClearOf: npc, clearWhere: \"town_npc\""));
            Assert.That(coordinator, Does.Contain("ChooseLeader(session, members), members, \"recovery\", out Vector3 beside"));
            // 3. The landing step runs in the route's arrival, for gamebots only, before the arrival row.
            string arrival = Between(gameBot, "private void CompleteStableMasterRouteCore(", "AUTONOMOUS_STABLE_ROUTE_ARRIVE");
            Assert.That(arrival, Does.Contain(
                "if (IsAlive && !IsHiredCompanion && TryStepOffStableLanding(out Vector3 stepFrom, out Vector3 stepTo)"));
            Assert.That(gameBot, Does.Contain("AutonomousServiceNpcClearance.TryResolveLandingStepOff(PathfindingProvider.Instance, CurrentZone, from, masterAt, DatabaseID, out to)"));
            // 4. The town break rejects a spot near a service NPC, from the one radius scan.
            Assert.That(townIdle, Does.Contain(
                "List<Vector3> services = AutonomousServiceNpcClearance.ServicePositions(nearby);"));
            Assert.That(townIdle, Does.Contain(
                "if (!AutonomousServiceNpcClearance.IsClearOf(candidate.Value, services, AutonomousServiceNpcClearance.TownBreakClearRadius)) continue;"));
            Assert.That(townIdle, Does.Contain("if (InLiveAggroRange(nearby, candidate.Value)) continue;"));
        });
    }
}
