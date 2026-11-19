using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.UnitTests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// The RvR route traps on the owner's PC, 2026-11-19, build 1.2-43c43aa (log c43.log, 13:20-14:22), each case at its logged
/// position and ids. Tests only: none of these fixes has been seen on the owner's PC yet.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_RvrRouteTraps43c
{
    [TearDown]
    public void TearDown()
    {
        AutonomousZoneSeams.SetForTests(null);
        AutonomousRvrMusterPost.ResetForTests();
        RvrEventTestState.Clear();
    }

    // ---- 1. Cruachan Gorge (211) / Breifine (212) seam at 200:384960,385504 ----------------------------------------------

    private static Zone Z(ushort id, int x, int y) => new(null, id, $"zone{id}", x, y, 65536, 65536, id, false, 0, false, 0, 0, 0, 0, 0);

    private static readonly Zone Connacht = Z(207, 286720, 417792);
    private static readonly Zone MountCollory = Z(210, 385024, 417792);
    private static readonly Zone CruachanGorge = Z(211, 319488, 352256);
    private static readonly Zone Breifine = Z(212, 385024, 352256);
    private static readonly Zone[] Hibernia = [Connacht, MountCollory, CruachanGorge, Breifine];

    /// <summary>The lines of the installed navmesh/seams.json (0.37b fresh runtime) for these borders, near the trap.</summary>
    private const string SeamFile = """
        {"version": 3, "step": 64, "main": {"200": {"207": 69397, "210": 45892, "211": 21922, "212": 62732}},
         "regions": {"200": [
          {"from": 211, "to": 212, "points": [[385440, 6625, 6627, 21922, 62732], [385504, 6699, 6707, 21922, 62732],
                                             [385632, 6795, 6825, 63772, 62631], [385696, 6843, 6874, 63772, 62631]]},
          {"from": 212, "to": 211, "points": [[385440, 6627, 6625, 62732, 21922], [385504, 6707, 6699, 62732, 21922],
                                             [385632, 6825, 6795, 62631, 63772], [385696, 6874, 6843, 62631, 63772]]},
          {"from": 211, "to": 207, "points": [[334880, 6527, 6561, 21922, 69397], [334944, 6530, 6563, 21922, 69397]]},
          {"from": 207, "to": 211, "points": [[334880, 6561, 6527, 69397, 21922], [334944, 6563, 6530, 69397, 21922]]},
          {"from": 212, "to": 210, "points": [[391072, 3344, 3300, 62732, 45892], [419834, 6502, 6500, 62631, 45892]]},
          {"from": 210, "to": 212, "points": [[391072, 3300, 3344, 45892, 62732], [419834, 6500, 6502, 45892, 62631]]}
         ]}}
        """;

    private static readonly Vector3 GorgeSide = new(384960, 385504, 6699);   // AUTONOMOUS_ZONE_SEAM_CYCLE zone=211 position
    private static readonly Vector3 BreifineSide = new(385088, 385504, 6713); // the same line's zone=212 position
    private static readonly Vector3 DruimLigen = new(333231, 420812, 5184);  // goal=333231,420812,5184 (medallion vendor)

    [Test]
    public void ABotOnTheGorgeSideOfTheSeamStandsOnTheGorgesMainGround()
    {
        AutonomousZoneSeams.SetForTests(SeamFile);
        Assert.Multiple(() =>
        {
            // The road anchor nearest this spot (384768,386202,7080, 723 away) lies on the ledge (63772): build 43c43aa
            // took the bot for a ledge bot and routed it 211>212>210>206>200>207, back into Breifine.
            Assert.That(AutonomousZoneItinerary.SeamGroundAt(Hibernia, 200, CruachanGorge, GorgeSide), Is.EqualTo(21922),
                "the seam file lists this very spot as the gorge's main ground");
            Assert.That(AutonomousZoneItinerary.SeamGroundAt(Hibernia, 200, Breifine, BreifineSide), Is.EqualTo(62732));
            Assert.That(AutonomousZoneItinerary.SeamGroundAt(Hibernia, 200, CruachanGorge, new Vector3(384960, 385696, 6843)),
                Is.EqualTo(63772), "the ledge crossing 192 along the border keeps its own ground");
            Assert.That(AutonomousZoneItinerary.SeamGroundAt(Hibernia, 200, CruachanGorge, new Vector3(383900, 385504, 6699)),
                Is.Null, "away from the border the road anchors decide, as before");
            Assert.That(AutonomousZoneItinerary.SeamGroundAt(Hibernia, 200, CruachanGorge, new Vector3(384960, 385504, 7200)),
                Is.Null, "a floor 500 above the listed crossing is not on it");
        });
    }

    [Test]
    public void TheWalkToDruimLigenNoLongerTurnsBackAtTheSeam()
    {
        AutonomousZoneSeams.SetForTests(SeamFile);
        ushort[] fromGorge = AutonomousZoneItinerary.AreaRouteForTests(Hibernia, 200, CruachanGorge, Connacht, GorgeSide, DruimLigen);
        ushort[] fromBreifine = AutonomousZoneItinerary.AreaRouteForTests(Hibernia, 200, Breifine, Connacht, BreifineSide, DruimLigen);
        Assert.Multiple(() =>
        {
            Assert.That(fromGorge, Is.EqualTo(new ushort[] { 211, 207 }), "from the gorge side: on to Connacht, not back into Breifine");
            Assert.That(fromBreifine, Is.EqualTo(new ushort[] { 212, 211, 207 }));
            Assert.That(fromGorge.Skip(1).Contains((ushort)212), Is.False, "the step the cycle took: 211 > 212");
        });
    }

    // ---- 2. The Hibernian muster in Odin's Gate (100:596055,581400) ------------------------------------------------------

    private static readonly Zone OdinsGate = new(null, 115, "Odin's Gate", 589824, 573440, 65536, 65536, 115, false, 0, false, 0, 0, 0, 0, 0);
    private static readonly Vector3 OdinHib = new(596055, 581400, 6031);

    /// <summary>A flat mesh where every point is floor, except small pockets no walk enters (a partial path that stops).</summary>
    private sealed class PocketNav : IPathfindingMgr
    {
        public readonly List<(Vector3 Centre, float Radius)> Pockets = new();
        public bool Available = true;
        public int Paths;
        private bool InPocket(Vector3 p) => Pockets.Any(pocket => Vector2.Distance(new(p.X, p.Y), new(pocket.Centre.X, pocket.Centre.Y)) <= pocket.Radius);
        public bool Init() => true;
        public void Stop() { }
        public bool RegisterDoor(GameDoorBase door) => true;
        public bool UpdateDoorFlags(GameDoorBase door) => true;
        public PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
        {
            Paths++;
            if (InPocket(end) == InPocket(start))
            {
                destination[0] = new WrappedPathfindingNode(end, default);
                return new PathfindingResult(PathfindingStatus.PathFound, 1);
            }
            // The walk stops where it is (Miadur 4598 stood 90 from his post at 596917,581121 for a minute).
            destination[0] = new WrappedPathfindingNode(start, default);
            return new PathfindingResult(PathfindingStatus.PartialPathFound, 1);
        }
        public Vector3? GetMoveAlongSurface(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters) => null;
        public Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters) => null;
        public Vector3? GetClosestPoint(Zone zone, Vector3 position, EDtPolyFlags[] filters) => position;
        public Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange, float zRange, EDtPolyFlags[] filters) => position;
        public Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters) => null;
        public Vector3? GetRoofAbove(Zone zone, Vector3 position, float maxHeight, EDtPolyFlags[] filters) => null;
        public Vector3? GetFloorBeneath(Zone zone, Vector3 position, float maxDepth, EDtPolyFlags[] filters) => position;
        public bool TrySnapToMesh(Zone zone, ref Vector3 position, float range) => true;
        public bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) => true;
        public bool HasNavmesh(Zone zone) => Available;
        public bool IsAvailable => Available;
        public EDtPolyFlags[] DefaultFilters => [];
        public EDtPolyFlags[] BlockingDoorAvoidanceFilters => [];
    }

    // The three posts of the log (AUTONOMOUS_ROUTE_RECOVERY failedDestination) and the slot each one is
    // (UT_RvrRouteTrapProbe on the installed meshes: 29 of 80 slots of this muster have no walk from its centre).
    [TestCase(13, 596917f, 581210f, "Miadur 4598")]
    [TestCase(28, 595687f, 580379f, "Yvik 4824")]
    [TestCase(20, 595422f, 580642f, "Blotiogg 4677")]
    public void AMusterSlotWithNoWalkFromTheCentreTakesAReachableSpot(int slot, float postX, float postY, string bot)
    {
        Vector3 raw = AutonomousRvrMusterPost.RawPost(OdinHib, slot);
        Assert.That(Vector2.Distance(new(raw.X, raw.Y), new(postX, postY)), Is.LessThan(160),
            $"{bot}'s logged post is slot {slot}'s spot (snapped to the floor within 160)");
        var nav = new PocketNav();
        nav.Pockets.Add((new Vector3(postX, postY, 6009), 120));
        Vector3 chosen = AutonomousRvrMusterPost.Choose(nav, _ => OdinsGate, OdinHib, slot);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrMusterPost.Reachable(nav, OdinsGate, OdinHib, raw), Is.False, "build 43c43aa sent the bot here");
            Assert.That(AutonomousRvrMusterPost.Reachable(nav, OdinsGate, OdinHib, chosen), Is.True);
            Assert.That(Vector2.Distance(new(chosen.X, chosen.Y), new(postX, postY)), Is.GreaterThan(120), "not in the pocket");
            Assert.That(Vector2.Distance(new(chosen.X, chosen.Y), new(OdinHib.X, OdinHib.Y)),
                Is.EqualTo(450 + 120 * MathF.Sqrt(slot)).Within(1f), "the same ring round the outpost: the muster keeps its spread");
        });
    }

    [Test]
    public void AReachableSlotKeepsItsUsualSpotAndEverySlotIsProvedOnce()
    {
        var nav = new PocketNav();
        nav.Pockets.Add((new Vector3(596917, 581210, 6009), 120));
        Vector3 usual = AutonomousRvrMusterPost.RawPost(OdinHib, 0);
        Assert.That(AutonomousRvrMusterPost.Choose(nav, _ => OdinsGate, OdinHib, 0), Is.EqualTo(usual));
        int paths = nav.Paths;
        AutonomousRvrMusterPost.Choose(nav, _ => OdinsGate, OdinHib, 0);
        Assert.That(nav.Paths, Is.EqualTo(paths), "the next bot of the slot reuses the proof");
    }

    [Test]
    public void WithoutANavmeshTheSlotKeepsItsUsualSpot()
    {
        var nav = new PocketNav { Available = false };
        Assert.That(AutonomousRvrMusterPost.Choose(nav, _ => OdinsGate, OdinHib, 13), Is.EqualTo(AutonomousRvrMusterPost.RawPost(OdinHib, 13)));
    }

    [Test]
    public void ASlotWithNoReachableSpotAtAllHoldsTheCentre()
    {
        var nav = new PocketNav();
        nav.Pockets.Add((OdinHib + new Vector3(0, 0, 0), 30));
        // The centre is in a pocket of its own here, so every ring spot is unreachable from it.
        Assert.That(AutonomousRvrMusterPost.Choose(nav, _ => OdinsGate, OdinHib, 13), Is.EqualTo(OdinHib));
    }

    // ---- 3. "No connected exterior route" beside the realm's arrival point ---------------------------------------------

    [Test]
    public void AKeepRouteThatKeepsFailingByTheArrivalPointStartsAgainFromIt()
    {
        Assert.Multiple(() =>
        {
            // Poshozois 4911 / Clumsychampion 5149, RVR_KEEP_ROUTE_FAILED target=rvr-keep-77 from=<596048, 582909, 6019>.
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Hibernia, 100, new(596048, 582909, 6019), 2, out Vector3 odin), Is.True);
            Assert.That(odin, Is.EqualTo(OdinHib));
            // Linaelorbel 3236, target=rvr-keep-105 from=<476630, 343539, 4083>.
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Albion, 200, new(476630, 343539, 4083), 2, out Vector3 emain), Is.True);
            Assert.That(emain, Is.EqualTo(new Vector3(475835, 343661, 4080)));
        });
    }

    [Test]
    public void OnlyARepeatedFailureNearTheArrivalPointRestarts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Hibernia, 100, new(596048, 582909, 6019), 1, out _), Is.False,
                "the first failure backs off and retries as before");
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Hibernia, 100, new(655296, 617888, 6002), 4, out _), Is.False,
                "far out on the march (the lane's walk point) the keep route's own recovery stays in charge");
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Hibernia, 100, OdinHib + new Vector3(60, 0, 0), 4, out _), Is.False,
                "a bot on the arrival point already starts from it");
            Assert.That(AutonomousRvrMusterPost.TryRestartPoint(eRealm.Hibernia, 1, new(596048, 582909, 6019), 4, out _), Is.False,
                "another region's point is never used");
        });
    }

    // ---- 4. RVR_RALLY_DELAY for a party holding its muster post ----------------------------------------------------------

    [Test]
    public void APartyHoldingItsMusterPostIsNotAStraggler()
    {
        RvrEventTestState.Clear();
        AutonomousRvrEventLayer.StagedAssault = true;
        // rvr-keep-77 Hlidskialf Faste (Midgard, Jamtland Mountains), attacked by Hibernia.
        string id = "rvr-keep-77-" + Guid.NewGuid().ToString("N");
        long opened = GameLoop.GameLoopTime + 1;
        var objective = new AutonomousRvrEventLayer.LiveObjective(id, "Hlidskialf Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 677000, 646000, 4800, false, 0, 0, 5, 2);
        var attacker = new AutonomousRvrEventLayer.Force(id + "-hibernia", eRealm.Hibernia, 8, 50, 1);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(attacker, [objective], opened, 0)?.TargetId, Is.EqualTo(id));

        const long Qwuxella = 4609;   // flagged 13:51:17 while holding her post at the Odin's Gate muster
        const long Linaelorbel = 3236; // really stuck (here: no step and no post for the whole muster)
        using var logs = new TestLogCapture(nameof(AutonomousRvrEventLayer));
        AutonomousRvrEventLayer.ReportTravel(id, attacker.GroupId, Qwuxella, double.PositiveInfinity, false, opened);
        AutonomousRvrEventLayer.ReportTravel(id, attacker.GroupId, Linaelorbel, double.PositiveInfinity, false, opened);
        // At her post from minute 1 to minute 9, reported every 5 s as HandleSiegeMuster does each turn.
        for (long t = opened + 60_000; t <= opened + 9 * 60_000; t += 5_000)
        {
            AutonomousRvrEventLayer.ReportMusterAttendance(id, attacker.GroupId, Qwuxella, true, t, null, OdinHib);
            AutonomousRvrEventLayer.TryConsumeRelease(attacker.GroupId, t, out _);
        }
        string[] delays = logs.Lines.Where(line => line.StartsWith("RVR_RALLY_DELAY ")).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(delays.Any(line => line.Contains(Linaelorbel.ToString())), Is.True, "a member with no progress is still reported");
            Assert.That(delays.Any(line => line.Contains(Qwuxella.ToString())), Is.False, "build 43c43aa: 39 of 50 lines were this");
        });
    }

    [Test]
    public void TheStragglerRuleAtTheLogsTimes()
    {
        long lastStep = 0, flagged = AutonomousRvrEventLayer.StragglerTimeoutMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(RallyStragglerPolicy.Flags(flagged, lastStep, null), Is.True, "8 minutes with no step and no post");
            Assert.That(RallyStragglerPolicy.Flags(flagged, lastStep, flagged - 2_000), Is.False, "at the post 2 s ago");
            Assert.That(RallyStragglerPolicy.Flags(flagged, lastStep, flagged - AutonomousRvrEventLayer.AttendanceFreshnessMilliseconds - 1),
                Is.True, "left the post (or died there) more than the attendance window ago");
            Assert.That(RallyStragglerPolicy.Flags(flagged - 1, lastStep, null), Is.False);
        });
    }
}
