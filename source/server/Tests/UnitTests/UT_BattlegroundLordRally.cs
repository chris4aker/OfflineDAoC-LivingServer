using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Rally = DOL.GS.AutonomousBattlegroundKeepRally;
using Wave = DOL.GS.AutonomousBattlegroundWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// The lord rally once the doors are down. Owner's PC, build 3b808b4 (Thidranki 2026-11-15, 10:12-10:58): all six doors
    /// of Thidranki Faste down, 30-46 bots of each attacking realm inside, both rallies at "rallying 0/31-34" in every
    /// BG_KEEP_STATUS sample, no BG_RALLY_STORM line, the lord at 100%. The rally point stood inside the keep door under the
    /// lord's floor; 276 Midgard and Hibernian bots died to the keep's guards meanwhile. Only the pure rules are tested here.
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundLordRally
    {
        // Thidranki Faste (BG_STORM_TRACE / BG_KEEP_STATUS of 3b808b4).
        private static readonly Vector3 OuterGate = new(34333, 38275, 3720); // 252000301, east
        private static readonly Vector3 KeepDoor = new(32654, 38180, 3720);  // 252000302, by the lord
        private static readonly Vector3 Lord = new(32296, 38267, 4592);
        private static readonly Vector3 OldRallyPoint = new(32475, 38224, 3728);

        [Test]
        public void TheRealmRalliesAtTheOuterGateNotUnderTheLord()
        {
            var doors = new (int Number, bool PosternLike, bool Down, float FromCentre, float FromArrival)[]
            {
                (2, false, true, 435, 3_000),   // keep door, under the lord: where the old rally stood
                (3, true, true, 900, 2_000),    // a postern: never
                (1, false, true, 1_244, 4_000), // the outer gate
            };
            Assert.Multiple(() =>
            {
                Assert.That(Rally.BreachIndex(doors), Is.EqualTo(2), "the outer gate, the way in from the field");
                doors[2].Down = false;
                Assert.That(Rally.BreachIndex(doors), Is.EqualTo(0), "only the keep door down: there");
                doors[0].Down = false;
                Assert.That(Rally.BreachIndex(doors), Is.EqualTo(-1), "a postern is no breach");
                Assert.That(Rally.BreachIndex(new (int, bool, bool, float, float)[0]), Is.EqualTo(-1));
            });
        }

        [Test]
        public void OfTwoOuterGatesTheRealmTakesTheOneOnItsSide()
        {
            var doors = new (int Number, bool PosternLike, bool Down, float FromCentre, float FromArrival)[]
            {
                (1, false, true, 1_200, 6_000),
                (1, false, true, 1_200, 2_500),
                (1, false, true, 1_100, 1_000), // nearer the centre: an inner arch, not the wall's gate
            };
            Assert.That(Rally.BreachIndex(doors), Is.EqualTo(1));
        }

        [Test]
        public void TheRallyStandsOutsideTheBreachAwayFromTheKeep()
        {
            // The outer gate faces east, away from the keep's centre (1,244 west of it).
            Vector2 centre = new(OuterGate.X - 1_244, OuterGate.Y);
            Vector2 outward = SiegePlacement.OutwardNormal(new(OuterGate.X, OuterGate.Y), 1024, centre);
            Vector3 point = Rally.BreachAnchor(OuterGate, outward, Rally.BreachStandOffs[0]);
            float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
            Assert.Multiple(() =>
            {
                Assert.That(outward.X, Is.EqualTo(1).Within(1e-3), "east, out of the keep");
                Assert.That(Vector3.Distance(point, new Vector3(OuterGate.X + 900, OuterGate.Y, OuterGate.Z)), Is.LessThan(0.5f));
                Assert.That(Vector2.Distance(new(point.X, point.Y), centre), Is.GreaterThan(Flat(OuterGate, new(centre, 0))));
                // The old point: 1,860 inside the outer gate, past the courtyard, 864 under the lord.
                Assert.That(Flat(OldRallyPoint, OuterGate), Is.EqualTo(1_859).Within(2));
                Assert.That(Lord.Z - OldRallyPoint.Z, Is.EqualTo(864));
                Assert.That(Flat(point, KeepDoor), Is.GreaterThan(2_500), "the courtyard and the keep door are the storm's, not the rally's");
                Assert.That(Rally.BreachStandOffs, Is.Ordered, "nearest the breach first");
            });
        }

        [Test]
        public void TheRallyKeepsClearOfTheGuardsPostsWhereItCan()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.RallyGuardClearance, Is.GreaterThan(1_000), "beyond a keep guard's 1,000 aggro range");
                Assert.That(Rally.ChooseRallyCandidate(new[] { 1_400f, 1_700f }), Is.EqualTo(0), "the nearest that is clear");
                Assert.That(Rally.ChooseRallyCandidate(new[] { 800f, 1_350f, 1_800f }), Is.EqualTo(1));
                Assert.That(Rally.ChooseRallyCandidate(new[] { 700f, 1_100f, 900f }), Is.EqualTo(1), "none clear: the clearest");
                Assert.That(Rally.ChooseRallyCandidate(new float[0]), Is.EqualTo(-1), "nothing walkable outside");
            });
        }

        [TestCase(32, 40, 24)] // 3b808b4: the model asked 31-34 of 30-46 inside; a wave of 40 is 24
        [TestCase(33, 30, 18)]
        [TestCase(12, 40, 12)] // the model's own number when it is the smaller
        [TestCase(20, 5, Rally.MinimumRally)]
        [TestCase(20, 0, Rally.MinimumRally)]
        [TestCase(60, 200, Rally.MaximumRally)]
        public void EnoughIsNeverMoreThanAWaveOfTheRealm(int modelled, int inside, int needed) =>
            Assert.That(Rally.RallyNeeded(modelled, inside), Is.EqualTo(needed));

        [Test]
        public void TheRallyWaitsForNoMoreThanTheRealmCanGather()
        {
            // A wave is 60% of the realm inside: the most #247's waves ever asked of it.
            for (int inside = 0; inside <= 120; inside++)
                Assert.That(Rally.RallyNeeded(Rally.MaximumRally, inside), Is.LessThanOrEqualTo(System.Math.Max(Rally.MinimumRally, Wave.WaveSize(inside))));
        }

        [TestCase(12, 24, 0, true, true)]    // the other realm storms: half of enough goes at once
        [TestCase(12, 24, 0, false, false)]  // alone, not yet patient
        [TestCase(11, 24, 0, true, false)]   // half of 24 is 12
        [TestCase(7, 12, 0, true, false)]    // half of 12 is 6, but never fewer than 8
        [TestCase(8, 12, 0, true, true)]
        [TestCase(24, 24, 0, false, true)]
        public void TheTwoAttackingRealmsCueEachOther(int rallied, int needed, long gathering, bool otherStorming, bool goes) =>
            Assert.That(Rally.ShouldStorm(rallied, needed, gathering, otherStorming), Is.EqualTo(goes));

        [Test]
        public void WithTheDoorsDownTheRealmStillFightsLikeAWave()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.DisciplinedKeepFight(true, Rally.Step.None), Is.True, "a door stands: the waves' rule");
                Assert.That(Rally.DisciplinedKeepFight(false, Rally.Step.Rally), Is.True);
                Assert.That(Rally.DisciplinedKeepFight(false, Rally.Step.Storm), Is.True);
                Assert.That(Rally.DisciplinedKeepFight(false, Rally.Step.None), Is.False, "no rally could be placed: as before");
                Assert.That(Rally.AssistsAgainstGuards(false, Rally.Step.Storm), Is.True, "stormers fight the guard on one of their own");
                Assert.That(Rally.AssistsAgainstGuards(false, Rally.Step.Rally), Is.False, "holders don't walk into the keep to help");
                Assert.That(Rally.AssistsAgainstGuards(true, Rally.Step.None), Is.True);
                bool disciplined = Rally.DisciplinedKeepFight(false, Rally.Step.Storm);
                // 3b808b4: "Fighting Armsman" 103 deaths, guards picked up to 5,000 away, alone.
                Assert.That(Wave.WaveTakesGuard(disciplined, false, 3_000, false), Is.False, "a guard far off is left alone");
                Assert.That(Wave.WaveTakesGuard(disciplined, false, 1_000, false), Is.True, "one in the way is fought");
                Assert.That(Wave.WaveTakesGuard(disciplined, false, 3_000, true), Is.True, "one firing on the realm is fought");
                Assert.That(Wave.WaveTakesGuard(disciplined, true, 3_000, false), Is.True, "the lord, always");
            });
        }

        [TestCase(false, 600, true)]   // upon the stormer
        [TestCase(false, 800, false)]  // an enemy in the field: walk on up
        [TestCase(true, 1_400, true)]  // firing on the realm near it
        [TestCase(true, 1_600, false)]
        public void AStormerFightsOnlyWhatAttacksTheGroup(bool firing, float distance, bool engages) =>
            Assert.That(Rally.StormerMayEngage(firing, distance), Is.EqualTo(engages));

        [Test]
        public void ARallyHolderKeepsTheGatherersRule()
        {
            // Holders take what walks into the rally or fires on the realm near it (GathererMayEngage); an Albion
            // defender on the walls 1,600 away, not firing, is left alone.
            Assert.Multiple(() =>
            {
                Assert.That(Wave.GathererMayEngage(false, 600, 900), Is.True);
                Assert.That(Wave.GathererMayEngage(false, 1_600, 1_700), Is.False);
                Assert.That(Wave.GathererMayEngage(true, 1_400, 1_700), Is.True);
                Assert.That(Rally.RallyFloorHeight, Is.GreaterThanOrEqualTo(Rally.LordFloorHeight), "the field outside the gate slopes");
            });
        }

        // Build d64d005 (Thidranki 2026-11-15, 12:55-13:02): Midgard's and Hibernia's rallies both at 36133,38275,3368 by
        // gate 252000301, guardClear=1459. One guard post 341 out from the gate reproduces that: straight out, only the
        // 1,800 stand-off keeps 1,300 from it, and there it is 1,459.
        private static readonly Vector3 SharedPoint = new(36133, 38275, 3368);
        private static readonly Vector2 GuardPost = new(34674, 38275);
        private static readonly Vector3 HibArrival = new(19714, 19085, 4094); // BattlegroundBrackets.BotArrival
        private static readonly Vector3 MidArrival = new(53150, 26093, 4263);
        private static readonly Vector2 East = new(1, 0);

        private static float PostClearance(Vector3 at) => Vector2.Distance(GuardPost, new(at.X, at.Y));
        private static Vector3? Anywhere(Vector3 raw) => raw;

        [Test]
        public void TheTwoRalliesStandFarEnoughApartNotToShareAFight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.RallySeparation, Is.GreaterThanOrEqualTo(2 * Rally.RallyRadius), "no bot is at both rallies");
                Assert.That(Rally.RallySeparation - Rally.RallyHoldRadius, Is.GreaterThan(Wave.GatherDefendRadius),
                    "a holder at its own point is beyond the other realm's fire-back range");
                Assert.That(Rally.RallySeparation - Rally.RallyHoldRadius, Is.GreaterThan(Wave.GatherCloseRadius));
            });
        }

        [Test]
        public void EachRealmSeesItsOwnArrivalFromTheGate()
        {
            Assert.Multiple(() =>
            {
                // Both arrive from the gate's south side (lower Y): Hibernia from far behind the wall, Midgard ahead of it.
                Assert.That(Rally.ArrivalBearing(OuterGate, East, HibArrival), Is.EqualTo(-127.3f).Within(0.5f));
                Assert.That(Rally.ArrivalBearing(OuterGate, East, MidArrival), Is.EqualTo(-32.9f).Within(0.5f));
                Assert.That(Rally.ArrivalBearing(OuterGate, East, null), Is.EqualTo(0));
                Vector2 left = Rally.RallyDirection(East, 90);
                Assert.That(left.X, Is.EqualTo(0).Within(1e-5));
                Assert.That(Rally.ArrivalBearing(OuterGate, East, OuterGate + new Vector3(left * 100, 0)), Is.EqualTo(90).Within(1e-3),
                    "the bearing and the direction turn the same way");
                Assert.That(Rally.BearingGap(170, -170), Is.EqualTo(20).Within(1e-4));
                Assert.That(Rally.BearingGap(-60, -127.3f), Is.EqualTo(67.3f).Within(1e-3));
                Assert.That(Rally.RallyBearings[0], Is.EqualTo(0), "straight out first, as before");
            });
        }

        [Test]
        public void TheRallyPrefersTheRealmsSideAndRefusesTheOtherRealmsSpot()
        {
            var candidates = new Rally.RallyCandidate[]
            {
                new(0, 900, 1_500, float.MaxValue),
                new(-30, 1_200, 1_500, float.MaxValue),
                new(-30, 900, 1_500, 1_000),          // on the other realm's rally
                new(-60, 1_500, 1_100, float.MaxValue), // under a guard
                new(30, 900, 1_500, Rally.RallySeparation),
            };
            Assert.Multiple(() =>
            {
                Assert.That(Rally.RallyPreference(candidates, -40), Is.EqualTo(new[] { 1, 0, 4 }));
                Assert.That(Rally.RallyPreference(candidates, 0), Is.EqualTo(new[] { 0, 4, 1 }), "straight out, then the nearer");
                Assert.That(Rally.RallyPreference(new[] { new Rally.RallyCandidate(0, 900, 1_500, 2_000) }, 0), Is.Empty);
            });
        }

        [Test]
        public void BeforeTheFixBothRealmsGotTheSamePoint()
        {
            // Without the arrival and the other rally (as on d64d005) the plan is the old one: straight out, 1,800.
            Rally.RallyPlan plan = Rally.PlanRally(OuterGate, East, null, new Vector3[0], Anywhere, _ => true, PostClearance);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Point, Is.Not.Null);
                Assert.That(plan.Point!.Value.X, Is.EqualTo(SharedPoint.X).Within(1));
                Assert.That(plan.Point.Value.Y, Is.EqualTo(SharedPoint.Y).Within(1));
                Assert.That(plan.GuardClear, Is.EqualTo(1_459).Within(1), "guardClear=1459");
            });
        }

        [Test]
        public void AtThidrankiFasteMidgardAndHiberniaRallyApart()
        {
            int corridorQueries = 0;
            bool Reaches(Vector3 _)
            {
                corridorQueries++;
                return true;
            }
            // Hibernia placed first (12:55:52), Midgard 14 s later with Hibernia's rally standing.
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, Reaches, PostClearance);
            int hibQueries = corridorQueries;
            Rally.RallyPlan mid = Rally.PlanRally(OuterGate, East, MidArrival, new[] { hib.Point!.Value }, Anywhere, Reaches, PostClearance);
            float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
            Assert.Multiple(() =>
            {
                Assert.That(hib.Bearing, Is.EqualTo(-60), "toward Hibernia's side");
                Assert.That(hib.GuardClear, Is.GreaterThanOrEqualTo(Rally.RallyGuardClearance));
                Assert.That(hib.Apart, Is.True, "nobody else there yet");
                Assert.That(hibQueries, Is.EqualTo(1), "only the chosen candidate's walk is checked");
                Assert.That(mid.Point, Is.Not.Null);
                Assert.That(mid.Apart, Is.True);
                Assert.That(Flat(mid.Point!.Value, hib.Point.Value), Is.GreaterThanOrEqualTo(Rally.RallySeparation));
                Assert.That(mid.GuardClear, Is.GreaterThanOrEqualTo(Rally.RallyGuardClearance));
                Assert.That(mid.Bearing, Is.EqualTo(30), "Midgard's own side (-30) is too near Hibernia's rally");
                Assert.That(Flat(mid.Point.Value, SharedPoint), Is.GreaterThan(0), "no longer the shared point");
                foreach (Vector3 rally in new[] { hib.Point.Value, mid.Point.Value })
                    Assert.That(Flat(rally, OuterGate), Is.GreaterThanOrEqualTo(Rally.BreachStandOffs[0] - 1), "still outside the breach");
            });
        }

        [Test]
        public void WithNothingApartTheRallyStandsWhereItDidBefore()
        {
            // Only the straight-out candidates can walk to the gate: the old choice, even on the other realm's rally.
            bool StraightOnly(Vector3 at) => System.Math.Abs(at.Y - OuterGate.Y) < 1;
            Rally.RallyPlan plan = Rally.PlanRally(OuterGate, East, MidArrival, new[] { SharedPoint }, Anywhere, StraightOnly, PostClearance);
            Rally.RallyPlan nothing = Rally.PlanRally(OuterGate, East, MidArrival, new Vector3[0], _ => null, _ => true, PostClearance);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Point!.Value.X, Is.EqualTo(SharedPoint.X).Within(1));
                Assert.That(plan.Apart, Is.False);
                Assert.That(plan.FromOtherRally, Is.EqualTo(0).Within(1));
                Assert.That(nothing.Point, Is.Null, "nothing walkable outside");
            });
        }

        [TestCase(1_000f, 200L, eRealm.Midgard, 100L, eRealm.Hibernia, false, true)]   // placed later, too near: moves
        [TestCase(1_000f, 100L, eRealm.Hibernia, 200L, eRealm.Midgard, false, false)]  // placed first: keeps its spot
        [TestCase(1_000f, 100L, eRealm.Hibernia, 100L, eRealm.Midgard, false, true)]   // at once: Midgard (2) keeps it
        [TestCase(1_000f, 100L, eRealm.Midgard, 100L, eRealm.Hibernia, false, false)]
        [TestCase(1_000f, 200L, eRealm.Midgard, 100L, eRealm.Hibernia, true, false)]   // already tried: no endless moving
        [TestCase(2_250f, 200L, eRealm.Midgard, 100L, eRealm.Hibernia, false, false)]  // far enough
        public void TheLaterRallyMoves(float apart, long mine, eRealm realm, long other, eRealm otherRealm, bool avoided, bool moves) =>
            Assert.That(Rally.YieldsTo(apart, mine, realm, other, otherRealm, avoided), Is.EqualTo(moves));
    }
}
