using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Route give-up fixes ported from stefanrows/OfflineDAoC (0.314-0.328): goal snap boxes and height guard, the floor
/// step budget, foreign failures, the give-up memory and crossing steer, the pocket-escape rule, and the keep gate rule
/// adapted to Living Server's realm ownership.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousRouteGoalFix
{
    /// <summary>A mesh floor at a fixed point; a query finds it only inside its box (like Detour's pick box).</summary>
    private sealed class FloorNavigation : PathfindingMgrBase
    {
        public readonly Vector3 Floor;
        public bool Available = true;
        public readonly List<(float X, float Y, float Z)> Boxes = new();
        public FloorNavigation(Vector3 floor) => Floor = floor;
        public override bool IsAvailable => Available;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters)
        {
            Boxes.Add((x, y, z));
            return MathF.Abs(Floor.X - point.X) <= x && MathF.Abs(Floor.Y - point.Y) <= y && MathF.Abs(Floor.Z - point.Z) <= z
                ? Floor : null;
        }
    }

    private static Zone AZone() => (Zone)RuntimeHelpers.GetUninitializedObject(typeof(Zone));

    [SetUp]
    public void ForgetGiveUps() => AutonomousFailedGoalMemory.Clear();

    [Test]
    public void AGoalOnTheMeshIsLeftByteIdentical()
    {
        var goal = new Vector3(100.5f, 200.25f, 300.125f);
        var nav = new FloorNavigation(new(100, 200, 330));
        var outcome = AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out Vector3 snapped);
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AutonomousGoalSnap.Outcome.OnMesh));
            Assert.That(snapped, Is.EqualTo(goal));
            Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f) }), "only the normal box is asked");
        });
    }

    [Test]
    public void AGoalHighAboveTheFloorSnapsInTheFirstWideBox()
    {
        // stefanrows' Druim Ligen goal hung 210 units above the floor: outside 32/32/64, inside 96/96/512.
        var goal = new Vector3(335449, 421910, 5401);
        var floor = new Vector3(335449, 421910, 5191);
        var nav = new FloorNavigation(floor);
        var outcome = AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out Vector3 snapped);
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
            Assert.That(snapped, Is.EqualTo(floor));
            Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f), (96f, 96f, 512f) }), "widest box not needed");
        });
    }

    [Test]
    public void TheWidestBoxIsTriedLastAndNeverExceeded()
    {
        var goal = new Vector3(0, 0, 5000);
        var nav = new FloorNavigation(new(200, 200, 4200));
        Assert.That(AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out _), Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
        Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f), (96f, 96f, 512f), (256f, 256f, 1024f) }));

        var far = new FloorNavigation(new(5000, 5000, 0));
        Assert.That(AutonomousGoalSnap.TrySnap(far, AZone(), goal, null, out Vector3 same),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
        Assert.That(same, Is.EqualTo(goal));
        foreach (var box in far.Boxes)
            Assert.That(box.X <= 256f && box.Y <= 256f && box.Z <= 1024f, Is.True, "512/512/2048 crashed the Detour host");
    }

    [Test]
    public void AFloorOutsideTheGoalsOwnZoneIsRejected()
    {
        var nav = new FloorNavigation(new(0, 0, 4900));
        Assert.That(AutonomousGoalSnap.TrySnap(nav, AZone(), new(0, 0, 5000), _ => false, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
    }

    [Test]
    public void WithoutANavmeshNothingIsQueried()
    {
        var nav = new FloorNavigation(Vector3.Zero) { Available = false };
        Assert.That(AutonomousGoalSnap.TrySnap(nav, AZone(), Vector3.One, null, out _), Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
        Assert.That(nav.Boxes, Is.Empty);
        Assert.That(AutonomousGoalSnap.TrySnap(new FloorNavigation(Vector3.Zero), null, Vector3.One, null, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
        Assert.That(AutonomousGoalSnap.TrySnap(null, AZone(), Vector3.One, null, out _), Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
    }

    [TestCase(-838f, 400f, true, TestName = "Height guard: dz -838 next to the bot is checked")]
    [TestCase(-325f, 400f, true, TestName = "Height guard: dz -325 next to the bot is checked")]
    [TestCase(256f, 400f, false, TestName = "Height guard: up to 256 units no check")]
    [TestCase(-221f, 100f, false, TestName = "Height guard: a common snap is not checked")]
    [TestCase(1177f, 2_999f, true, TestName = "Height guard: a large snap within 3,000 units is checked")]
    [TestCase(1177f, 3_001f, false, TestName = "Height guard: a far goal keeps its snap")]
    public void TheHeightGuardChecksOnlyLargeSnapsNearTheBot(float dz, float botDistance, bool check)
    {
        var goal = new Vector3(607604, 298904, 5000);
        Assert.That(AutonomousGoalSnap.NeedsConnectionCheck(goal, goal with { Z = goal.Z + dz },
            new Vector3(goal.X + botDistance, goal.Y, 5000)), Is.EqualTo(check));
    }

    [Test]
    public void AWrongFloorNextToTheBotIsRefusedAndAReachableOneKept()
    {
        var goal = new Vector3(607604, 298904, 5200);
        var lower = new Vector3(607604, 298904, 4362);
        var bot = new Vector3(607900, 298904, 5200);
        var nav = new FloorNavigation(lower);
        var refused = AutonomousGoalSnap.TrySnapGuarded(nav, AZone(), goal, null, bot, _ => false, out Vector3 floor);
        var kept = AutonomousGoalSnap.TrySnapGuarded(nav, AZone(), goal, null, bot, _ => true, out Vector3 snapped);
        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.EqualTo(AutonomousGoalSnap.Outcome.WrongFloor));
            Assert.That(floor, Is.EqualTo(lower), "the refused floor is reported for the log line");
            Assert.That(kept, Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
            Assert.That(snapped, Is.EqualTo(lower));
        });
    }

    [Test]
    public void ASmallSnapNeverPaysForTheWalkCheck()
    {
        var goal = new Vector3(335449, 421910, 5401);
        var nav = new FloorNavigation(new(335449, 421910, 5191));
        bool asked = false;
        Assert.That(AutonomousGoalSnap.TrySnapGuarded(nav, AZone(), goal, null, goal, _ => asked = true, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
        Assert.That(asked, Is.False);
        Assert.That(AutonomousGoalSnap.TrySnapGuarded(new FloorNavigation(goal), AZone(), goal, null, goal, _ => false, out Vector3 same),
            Is.EqualTo(AutonomousGoalSnap.Outcome.OnMesh));
        Assert.That(same, Is.EqualTo(goal));
    }

    [Test]
    public void TheGoalSnapTakesOnlyTheGoalOrTheContinuationItself()
    {
        var destination = new Vector3(333134, 420684, 5187);
        var continuation = new Vector3(335995, 412488, 6593);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousForeignRouteFailure.IsRoutePoint(destination + new Vector3(10, 0, 0), destination, null), Is.True);
            Assert.That(AutonomousForeignRouteFailure.IsRoutePoint(continuation, destination, continuation), Is.True);
            Assert.That(AutonomousForeignRouteFailure.IsRoutePoint(destination + new Vector3(17, 0, 0), destination, null), Is.False);
        });
    }

    [Test]
    public void OnlyAFailureWellAwayFromTheRouteIsForeign()
    {
        var destination = new Vector3(333134, 420684, 5187);
        var continuation = new Vector3(335995, 412488, 6593);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousForeignRouteFailure.IsForeign(new(337753, 412697, 6276), destination, continuation), Is.True);
            Assert.That(AutonomousForeignRouteFailure.IsForeign(destination + new Vector3(90, 0, 0), destination, null), Is.False,
                "a drifting formation slot within 96 units still belongs to the route");
            Assert.That(AutonomousForeignRouteFailure.IsForeign(destination + new Vector3(97, 0, 0), destination, null), Is.True);
            Assert.That(AutonomousForeignRouteFailure.IsForeign(continuation + new Vector3(0, 60, 0), destination, continuation), Is.False);
        });
    }

    [TestCase(true, true, true, false, true, TestName = "Foreign failure: an off-mesh NoPathFound is set aside")]
    [TestCase(true, true, true, true, false, TestName = "Foreign failure: the same point again goes to the recovery")]
    [TestCase(true, false, true, false, false, TestName = "Foreign failure: a point of the route is not set aside")]
    [TestCase(true, true, false, false, false, TestName = "Foreign failure: a point on the mesh is a real failure")]
    [TestCase(false, true, true, false, false, TestName = "Foreign failure: a partial path is not set aside")]
    public void ForeignFailureDecision(bool noPath, bool foreign, bool offMesh, bool already, bool setAside) =>
        Assert.That(AutonomousForeignRouteFailure.SetAside(noPath, foreign, offMesh, already), Is.EqualTo(setAside));

    [TestCase(false, false, false, false, false, true, TestName = "Floor step: an idle traveller may be moved")]
    [TestCase(true, false, false, false, false, false, TestName = "Floor step: not while casting")]
    [TestCase(false, true, false, false, false, false, TestName = "Floor step: not while attacking")]
    [TestCase(false, false, true, false, false, false, TestName = "Floor step: not in combat")]
    [TestCase(false, false, false, true, false, false, TestName = "Floor step: not with aggro")]
    [TestCase(false, false, false, false, true, false, TestName = "Floor step: not on a stable route")]
    public void TheFloorStepMovesOnlyAnIdleTraveller(bool casting, bool attacking, bool inCombat, bool aggro, bool riding, bool mayMove) =>
        Assert.That(AutonomousStartSnap.MayMove(casting, attacking, inCombat, aggro, riding), Is.EqualTo(mayMove));

    [Test]
    public void OneFloorStepPerFortyFiveSecondsAndTwoPerDestination()
    {
        var destination = new Vector3(613276, 303964, 2964);
        long cooldown = AutonomousStartSnap.CooldownMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(cooldown, Is.EqualTo(45_000));
            Assert.That(AutonomousStartSnap.BudgetAllows(0, 0, 0), Is.True);
            Assert.That(AutonomousStartSnap.BudgetAllows(10_000 + cooldown - 1, 10_000 + cooldown, 1), Is.False);
            Assert.That(AutonomousStartSnap.BudgetAllows(10_000 + cooldown, 10_000 + cooldown, 1), Is.True);
            Assert.That(AutonomousStartSnap.BudgetAllows(1_000_000, 0, 2), Is.False, "two for one destination");
            Assert.That(AutonomousStartSnap.SnapsFor(destination + new Vector3(90, 0, 0), destination, 2), Is.EqualTo(2));
            Assert.That(AutonomousStartSnap.SnapsFor(destination + new Vector3(500, 0, 0), destination, 2), Is.EqualTo(0));
        });
    }

    [TestCase(false, true, true, TestName = "Floor step: a floor in a gated keep is refused from outside")]
    [TestCase(true, true, false, TestName = "Floor step: a bot already inside such a keep may be put on its floor")]
    [TestCase(false, false, false, TestName = "Floor step: an open floor is fine")]
    public void TheFloorStepNeverPutsABotBehindAGateItMayNotPass(bool startGated, bool floorGated, bool refused) =>
        Assert.That(AutonomousStartSnap.EntersGatedKeep(startGated, floorGated), Is.EqualTo(refused));

    // Living Server's keep gate rule: classic RvR, a bot passes only its own realm's keep gates.

    [TestCase(eRealm.Albion, eRealm.Albion, true, TestName = "Keep gate: an Albion bot passes an Albion keep")]
    [TestCase(eRealm.Albion, eRealm.Midgard, false, TestName = "Keep gate: an Albion bot is held at a Midgard keep")]
    [TestCase(eRealm.Hibernia, eRealm.None, false, TestName = "Keep gate: no realm owns the keep, nobody passes")]
    [TestCase(eRealm.None, eRealm.None, false, TestName = "Keep gate: a realm-less bot passes nothing")]
    public void ABotPassesOnlyTheGatesOfItsOwnRealmsKeeps(eRealm bot, eRealm keep, bool passes) =>
        Assert.That(AutonomousKeepGateGoals.CanPass(bot, keep), Is.EqualTo(passes));

    [TestCase(false, true, false, true, TestName = "Keep gate: a closed enemy gate blocks")]
    [TestCase(true, true, false, false, TestName = "Keep gate: a keep the bot may pass never blocks")]
    [TestCase(false, false, false, false, TestName = "Keep gate: a breached keep is open")]
    [TestCase(false, true, true, false, TestName = "Keep gate: the keep the force is sieging keeps its targets")]
    public void GateDecision(bool canPass, bool gateIntact, bool sieged, bool blocks) =>
        Assert.That(AutonomousKeepGateGoals.GateBlocks(canPass, gateIntact, sieged), Is.EqualTo(blocks));

    [Test]
    public void TheCourtyardCoversTheLoggedGatedGoalsButNotTheGroundOutside()
    {
        float renaris = AutonomousKeepGateGoals.CourtyardRadius(1_415f);
        float smallest = AutonomousKeepGateGoals.CourtyardRadius(1_270f);
        Assert.Multiple(() =>
        {
            Assert.That(renaris, Is.EqualTo(1_115f));
            Assert.That(AutonomousKeepGateGoals.CourtyardRadius(700f), Is.EqualTo(AutonomousKeepGateGoals.MinimumCourtyardRadius));
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(601798 - 601562, 430727 - 430054, smallest), Is.True);
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(579126 - 579877, 429197 - 429821, renaris), Is.True);
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(1_200, 0, renaris), Is.False, "a fight at the back wall stays a target");
            Assert.That(AutonomousKeepGateGoals.IsOuterGate(1_415f, 1_415f), Is.True);
            Assert.That(AutonomousKeepGateGoals.IsOuterGate(500f, 1_415f), Is.False);
        });
    }

    // Castle Excalibur's doors (distance to the centre, gate = attackable, i.e. not a postern).
    private static readonly (float Distance, bool IsGate)[] ExcaliburDoors =
    [
        (156, true), (230, true), (685, true), (738, true), (739, false), (769, true), (826, true), (975, false),
        (1_049, true), (1_116, false), (1_268, false),
    ];

    private static (float, bool, bool)[] Closed((float Distance, bool IsGate)[] doors) =>
        doors.Select(door => (door.Distance, door.IsGate, true)).ToArray();

    [Test]
    public void TheMainGateHoldsTheCourtyardThoughAPosternStandsFartherOut()
    {
        float ring = AutonomousKeepGateGoals.OuterGateRing(ExcaliburDoors);
        Assert.Multiple(() =>
        {
            Assert.That(ring, Is.EqualTo(1_049f), "the main gate is the outer gate, not the farther postern");
            Assert.That(AutonomousKeepGateGoals.AnyOuterGateIntact(Closed(ExcaliburDoors), ring), Is.True);
            Assert.That(AutonomousKeepGateGoals.AnyOuterGateIntact(Closed(ExcaliburDoors), 1_268f), Is.False,
                "reading the ring at the farthest door finds only posterns: the bug stefanrows fixed in 0.320");
        });
    }

    [Test]
    public void ABrokenOuterGateOpensTheCourtyardAndInnerDoorsDoNotHoldIt()
    {
        float ring = AutonomousKeepGateGoals.OuterGateRing(ExcaliburDoors);
        var mainGateDown = ExcaliburDoors.Select(door => (door.Distance, door.IsGate, door.Distance != 1_049f)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousKeepGateGoals.AnyOuterGateIntact(mainGateDown, ring), Is.False);
            Assert.That(AutonomousKeepGateGoals.OuterGateRing([(1_268f, false), (1_116f, false)]), Is.EqualTo(0f));
            Assert.That(AutonomousKeepGateGoals.AnyOuterGateIntact([(1_268f, false, true)], 0f), Is.False,
                "a keep with posterns only has no gate to hold");
        });
    }

    [Test]
    public void NoKeepsMeansNothingBlocks() =>
        Assert.That(AutonomousKeepGateGoals.FindBlockingKeep(eRealm.Albion, null, 0, 0), Is.Null);

    // Give-up memory.

    [Test]
    public void AFailedGoalIsBlockedForTheCooldownWithinTwoHundredUnitsOnly()
    {
        const long owner = 101;
        AutonomousFailedGoalMemory.Note(owner, 200, 335449, 421910, 1_000);
        long inside = 1_000 + AutonomousFailedGoalMemory.CooldownMilliseconds - 1;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFailedGoalMemory.CooldownMilliseconds, Is.EqualTo(6 * 60_000));
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449, 421910, 2_000), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449 + 190, 421910, inside), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449 + 210, 421910, 2_000), Is.False, "too far");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 100, 335449, 421910, 2_000), Is.False, "other region");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner + 1, 200, 335449, 421910, 2_000), Is.False, "other bot");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449, 421910,
                1_000 + AutonomousFailedGoalMemory.CooldownMilliseconds), Is.False, "cooldown over");
        });
    }

    [Test]
    public void HasAnyIsTheCheapPreCheck()
    {
        Assert.That(AutonomousFailedGoalMemory.HasAny(5, 0), Is.False);
        AutonomousFailedGoalMemory.Note(5, 1, 0, 0, 0);
        Assert.That(AutonomousFailedGoalMemory.HasAny(5, 10), Is.True);
        Assert.That(AutonomousFailedGoalMemory.HasAny(5, AutonomousFailedGoalMemory.CooldownMilliseconds), Is.False);
        Assert.That(AutonomousFailedGoalMemory.HasAny(0, 10), Is.False, "owner 0 is no owner");
    }

    [Test]
    public void AWarbandSharesItsMemoryUnderOneForceKey()
    {
        long force = AutonomousFailedGoalMemory.ForceKey("group-17");
        Assert.That(force, Is.Not.EqualTo(0));
        Assert.That(force, Is.EqualTo(AutonomousFailedGoalMemory.ForceKey("group-17")));
        Assert.That(force, Is.GreaterThan(uint.MaxValue), "never collides with a bot id");
        Assert.That(AutonomousFailedGoalMemory.ForceKey(""), Is.EqualTo(0));
        Assert.That(AutonomousFailedGoalMemory.ForceKey(null), Is.EqualTo(0));
        AutonomousFailedGoalMemory.Note(force, 1, 10, 10, 0);
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(force, 1, 50, 50, 10), Is.True);
        AutonomousFailedGoalMemory.Note(0, 1, 10, 10, 0);
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(0, 1, 10, 10, 10), Is.False, "owner 0 is no owner");
    }

    [Test]
    public void TheSameSpotRefreshesAndTheOldestOfFourIsReplaced()
    {
        const long owner = 7;
        for (int i = 0; i < 4; i++)
            AutonomousFailedGoalMemory.Note(owner, 1, i * 1_000, 0, i * 10);
        AutonomousFailedGoalMemory.Note(owner, 1, 9_000, 0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 0, 0, 101), Is.False, "the oldest was replaced");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 1_000, 0, 101), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 9_000, 0, 101), Is.True);
        });
        AutonomousFailedGoalMemory.Note(owner, 1, 1_050, 0, AutonomousFailedGoalMemory.CooldownMilliseconds);
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 1_000, 0, AutonomousFailedGoalMemory.CooldownMilliseconds + 5_000),
            Is.True, "a refreshed spot runs a new cooldown");
    }

    private static DbZonePoint Point(ushort id, int sourceX, int sourceY) =>
        new() { Id = id, SourceRegion = 1, SourceX = sourceX, SourceY = sourceY, TargetRegion = 249 };

    private static DbZonePoint Search(DbZonePoint[] edges, Func<DbZonePoint, bool> avoid) =>
        edges.FirstOrDefault(edge => avoid == null || !avoid(edge));

    [Test]
    public void ACrossingTheBotJustGaveUpReachingYieldsToAnotherRouteUntilTheCooldownEnds()
    {
        const long owner = 25_316;
        DbZonePoint near = Point(79, 601798, 430727), other = Point(81, 599716, 536189);
        DbZonePoint[] edges = [near, other];
        Func<long, Func<DbZonePoint, bool>> givenUp = now => point => AutonomousFailedGoalMemory.IsBlockedCrossing(owner, point, now);
        DbZonePoint before = AutonomousFailedGoalMemory.SteerCrossing(avoid => Search(edges, avoid), givenUp(500), out var s0);
        AutonomousFailedGoalMemory.Note(owner, 1, 601843, 430860, 1_000); // 140 units from the portal source
        DbZonePoint after = AutonomousFailedGoalMemory.SteerCrossing(avoid => Search(edges, avoid), givenUp(2_000), out var s1);
        DbZonePoint later = AutonomousFailedGoalMemory.SteerCrossing(avoid => Search(edges, avoid),
            givenUp(1_000 + AutonomousFailedGoalMemory.CooldownMilliseconds), out var s2);
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.SameAs(near));
            Assert.That(s0, Is.EqualTo(AutonomousFailedGoalMemory.CrossingSteer.Unchanged));
            Assert.That(after, Is.SameAs(other));
            Assert.That(s1, Is.EqualTo(AutonomousFailedGoalMemory.CrossingSteer.Steered));
            Assert.That(later, Is.SameAs(near));
            Assert.That(s2, Is.EqualTo(AutonomousFailedGoalMemory.CrossingSteer.Unchanged));
            Assert.That(AutonomousFailedGoalMemory.IsBlockedCrossing(owner, null, 2_000), Is.False);
        });
    }

    [Test]
    public void TheOnlyRouteIsKeptEvenWhenTheBotJustGaveUpReachingIt()
    {
        DbZonePoint only = Point(79, 601798, 430727);
        int searches = 0;
        DbZonePoint crossing = AutonomousFailedGoalMemory.SteerCrossing(avoid => { searches++; return Search([only], avoid); },
            _ => true, out var steer);
        Assert.Multiple(() =>
        {
            Assert.That(crossing, Is.SameAs(only), "the memory steers among routes, it never strands the bot");
            Assert.That(steer, Is.EqualTo(AutonomousFailedGoalMemory.CrossingSteer.Fallback));
            Assert.That(searches, Is.EqualTo(2));
        });
        searches = 0;
        AutonomousFailedGoalMemory.SteerCrossing(avoid => { searches++; return Search([only], avoid); }, _ => false, out _);
        Assert.That(searches, Is.EqualTo(1), "the second search runs only for a give-up spot");
        Assert.That(AutonomousFailedGoalMemory.SteerCrossing(_ => null, _ => true, out var none), Is.Null);
        Assert.That(none, Is.EqualTo(AutonomousFailedGoalMemory.CrossingSteer.Unchanged));
    }

    [TestCase(true, true, true, TestName = "Pocket escape: connected start and an off-mesh goal, no relocation")]
    [TestCase(true, false, false, TestName = "Pocket escape: a goal on the mesh keeps the relocation rule")]
    [TestCase(false, true, false, TestName = "Pocket escape: a bot in a real pocket still escapes")]
    public void PocketEscapeIsSkippedOnlyWhenTheGoalIsTheFault(bool startConnected, bool goalOffMesh, bool skip) =>
        Assert.That(AutonomousPocketEscapeRule.SkipRelocation(startConnected, goalOffMesh), Is.EqualTo(skip));
}
