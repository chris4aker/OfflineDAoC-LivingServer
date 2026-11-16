using System.Linq;
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
                Assert.That(Rally.AssistsAgainstGuards(false, Rally.Step.Storm, inLordFight: true), Is.True,
                    "beside the lord, stormers fight the guard on one of their own");
                Assert.That(Rally.AssistsAgainstGuards(false, Rally.Step.Storm), Is.False,
                    "on the way up they don't turn to help (ef76db3: 106 of 110 deaths 'Fighting ...')");
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

        // Owner's PC, build ef76db3 (Thidranki 2026-11-15, 16:02-16:24): eight attempts, all "half the stormers fell" after
        // 0.8-2.6 minutes, lord 100%; 106 of the attacking realms' 110 deaths during them "Fighting ...", 53 at their own rally.
        [TestCase(false, false, true, 200, 0, true)]     // a melee swinging at this stormer: hit back
        [TestCase(false, false, true, 500, 100, true)]   // a caster in its face, on its floor
        [TestCase(false, false, true, 600, 0, false)]    // on it but 600 off: run on
        [TestCase(false, false, true, 300, 400, false)]  // a wizard on the wall above the gate: run past
        [TestCase(false, false, false, 150, 0, false)]   // an enemy beside it fighting someone else: run on
        [TestCase(false, false, false, 1_400, 0, false)] // firing on a realm-mate near it (the old rule took this): run on
        [TestCase(true, false, false, 3_000, 900, true)] // the lord, always
        [TestCase(false, true, false, 700, 0, true)]     // beside the lord: everything in his fight
        public void AStormerFightsOnlyWhatIsOnIt(bool isLord, bool inLordFight, bool onThisBot, float distance, float height, bool engages) =>
            Assert.That(Rally.StormerMayEngage(isLord, inLordFight, onThisBot, distance, height), Is.EqualTo(engages));

        [Test]
        public void TheLordsFightIsHisFloorBesideHim()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.InLordFight(300, 50), Is.True);
                Assert.That(Rally.InLordFight(Rally.InTheFightRadius, Rally.LordFloorHeight), Is.True);
                Assert.That(Rally.InLordFight(300, 872), Is.False, "in the courtyard under his tower (z 3,720 vs 4,592)");
                Assert.That(Rally.InLordFight(1_000, 0), Is.False);
            });
        }

        [Test]
        public void WhereAStormerFell()
        {
            float gateToLord = Vector2.Distance(new(OuterGate.X, OuterGate.Y), new(Lord.X, Lord.Y)); // 2,037
            Assert.Multiple(() =>
            {
                // ef76db3 deaths, Thidranki Faste.
                Assert.That(Rally.DeathPlace(1_800, 3_705, gateToLord, 1_181), Is.EqualTo("outside"), "Speedyfirbolg at Hibernia's rally");
                Assert.That(Rally.DeathPlace(91, 1_946, gateToLord, 870), Is.EqualTo("gate"), "Eogaedan, to a Wizard");
                Assert.That(Rally.DeathPlace(718, 1_319, gateToLord, 870), Is.EqualTo("inside"), "Maez, 'Storming the keep'");
                Assert.That(Rally.DeathPlace(2_058, 78, gateToLord, 308), Is.EqualTo("inside"), "Stormarrow on the stairs, 308 below him");
                Assert.That(Rally.DeathPlace(2_189, 154, gateToLord, 120), Is.EqualTo("lordFloor"));
            });
        }

        [TestCase(true, false, eRealm.Albion, Rally.KillerKind.Guard)]
        [TestCase(true, true, eRealm.Albion, Rally.KillerKind.Lord)]
        [TestCase(false, false, eRealm.Albion, Rally.KillerKind.Holder)]
        [TestCase(false, false, eRealm.Midgard, Rally.KillerKind.OtherAttacker)]
        [TestCase(false, false, eRealm.None, Rally.KillerKind.Other)]
        [TestCase(false, false, eRealm.Hibernia, Rally.KillerKind.Other)]
        public void WhatKilledAHibernianStormerAtAnAlbionKeep(bool guard, bool lord, eRealm killer, Rally.KillerKind kind) =>
            Assert.That(Rally.KillerOf(guard, lord, killer, eRealm.Albion, eRealm.Hibernia), Is.EqualTo(kind));

        [Test]
        public void TheRegroupSaysWhereTheFallenDied()
        {
            var fallen = new[]
            {
                new Rally.FallenStormer("outside", Rally.KillerKind.Holder, 1_800, 3_700),
                new Rally.FallenStormer("outside", Rally.KillerKind.OtherAttacker, 1_790, 3_720),
                new Rally.FallenStormer("gate", Rally.KillerKind.Guard, 90, 1_950),
            };
            Assert.Multiple(() =>
            {
                Assert.That(Rally.SummariseFallen(fallen), Is.EqualTo(
                    "fell=outside:2,gate:1,inside:0,lordFloor:0 killers=guard:1,lord:0,holder:1,otherAttacker:1,other:0 " +
                    "fellMedianToGate=1790 fellMedianToLord=3700"));
                Assert.That(Rally.SummariseFallen(System.Array.Empty<Rally.FallenStormer>()), Does.EndWith("fellMedianToGate=-1 fellMedianToLord=-1"));
            });
        }

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

        // c165c90 (Thidranki 2026-11-15 17:37): Midgard placed 4 s before Hibernia (BG_RALLY_PLACED lines).
        private static readonly Vector3 MidC165 = new(35892, 37375, 3411); // -30
        private static readonly Vector3 HibC165 = new(35233, 39834, 3353); // +60, gate 252000301

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        // Build 0acc99b (Thidranki 2026-11-15 20:43-21:02) gave both realms' plans as a pair Midgard +60 and Hibernia -30, the
        // only two spots of the near ring 2,250 apart: -60 at 1,800 stands by the wall's south-east corner. A post on that corner
        // (the walls are 1,244 round the centre, as the gate) reproduces it.
        private static readonly Vector2 Centre = new(33089, 38271);
        private static readonly Vector2 SouthEastCorner = new(34333, 37027);
        private static float PcClearance(Vector3 at) =>
            System.Math.Min(PostClearance(at), Vector2.Distance(SouthEastCorner, new(at.X, at.Y)));
        private static bool NearRingOnly(Vector3 at) => Flat(at, OuterGate) <= Rally.BreachStandOffs[^1] + 1;

        private static float MidGap(Rally.RallyPlan plan) =>
            Rally.BearingGap(plan.Bearing, Rally.ArrivalBearing(OuterGate, East, MidArrival));
        private static float HibGap(Rally.RallyPlan plan) =>
            Rally.BearingGap(plan.Bearing, Rally.ArrivalBearing(OuterGate, East, HibArrival));

        [Test]
        public void InTheNearRingOnlyThePairPutsOneRealmAcrossTheGateFront()
        {
            // As on the PC at 20:43 (and c165c90's +60 for Hibernia): one realm 93-187 degrees off its arrival.
            Rally.RallyPlan mid = Rally.PlanRally(OuterGate, East, MidArrival, new Vector3[0], Anywhere, NearRingOnly, PcClearance,
                partnerArrival: HibArrival, centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(mid.Bearing, Is.EqualTo(60), "Midgard +60, the PC's 20:43 placement");
                Assert.That(Rally.ArrivalBearing(OuterGate, East, mid.Partner!.Value), Is.EqualTo(-30).Within(0.5f), "Hibernia -30");
                Assert.That(MidGap(mid), Is.GreaterThan(Rally.FarSide));
                Assert.That(mid.Candidates, Does.Contain("-60/1800=952x"), "BG_RALLY_CANDIDATES names the corner post");
            });
        }

        [Test]
        public void WithTheFarRingBothRealmsRallyOnTheirOwnSideWhoeverPlacesFirst()
        {
            Rally.RallyPlan mid = Rally.PlanRally(OuterGate, East, MidArrival, new Vector3[0], Anywhere, _ => true, PcClearance,
                partnerArrival: HibArrival, partnerFirst: false, centre: Centre);
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new[] { mid.Point!.Value }, Anywhere, _ => true, PcClearance,
                partnerArrival: MidArrival, partnerFirst: true, centre: Centre);
            Rally.RallyPlan hibFirst = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, _ => true, PcClearance,
                partnerArrival: MidArrival, partnerFirst: true, centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(Flat(mid.Point.Value, MidC165), Is.LessThan(1), "Midgard -30 at 1,800 (35892,37375), its PC spot");
                Assert.That(hib.Bearing, Is.EqualTo(-90), "Hibernia round the corner, past the south wall's line");
                Assert.That(Flat(hib.Point!.Value, OuterGate), Is.EqualTo(2_700).Within(1));
                Assert.That(MidGap(mid), Is.LessThan(5));
                Assert.That(HibGap(hib), Is.LessThan(40), "37 degrees off its arrival (c165c90/0acc99b: 97-187)");
                Assert.That(Flat(mid.Partner!.Value, hib.Point.Value), Is.LessThan(1), "Midgard left Hibernia the spot Hibernia takes");
                Assert.That(Flat(hib.Partner!.Value, mid.Point.Value), Is.LessThan(1));
                Assert.That(Flat(hibFirst.Point!.Value, hib.Point.Value), Is.LessThan(1), "the order makes no difference");
                Assert.That(mid.Apart && hib.Apart, Is.True);
                Assert.That(Flat(mid.Point.Value, hib.Point.Value), Is.GreaterThanOrEqualTo(Rally.RallySeparation));
                Assert.That(PcClearance(hib.Point.Value), Is.GreaterThanOrEqualTo(Rally.RallyGuardClearance));
                Assert.That(Vector2.Distance(new(hib.Point.Value.X, hib.Point.Value.Y), Centre),
                    Is.GreaterThan(Vector2.Distance(new(OuterGate.X, OuterGate.Y), Centre) + Rally.FarWallClearance), "outside the walls");
                Assert.That(mid.UsedBarren || hib.UsedBarren, Is.False);
            });
        }

        [Test]
        public void TheFarRingStandsOnlyWellOutsideTheWalls()
        {
            // Without the centre every far candidate is tried; with it, the walls' line near the corner is not.
            Rally.RallyPlan with = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, _ => true, _ => float.MaxValue,
                centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(with.Candidates, Does.Contain("-90/2700"));
                Assert.That(with.Candidates, Does.Contain("-90/2100"), "2,100 round the wall: 2,437 from the centre");
                Assert.That(Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, _ => true, _ => float.MaxValue,
                    centre: new(OuterGate.X - 4_000, OuterGate.Y)).Candidates, Does.Not.Contain("-90/2100"), "a bigger keep's wall line");
                Assert.That(with.Bearing, Is.EqualTo(-60), "alone, the near ring comes first while it is on the realm's side");
                Assert.That(with.Candidates, Does.Not.Contain("90/900"), "the near ring stops at 60 degrees");
            });
        }

        [Test]
        public void AloneWithMidgardOnItsSideHiberniaNoLongerTakesTheFarSide()
        {
            // c165c90: Midgard at -30 first, Hibernia alone got +60, 187 degrees off its arrival.
            Rally.RallyPlan near = Rally.PlanRally(OuterGate, East, HibArrival, new[] { MidC165 }, Anywhere, NearRingOnly, PcClearance,
                centre: Centre);
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new[] { MidC165 }, Anywhere, _ => true, PcClearance,
                centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(near.Bearing, Is.EqualTo(60), "the near ring alone: as on c165c90");
                Assert.That(hib.Bearing, Is.EqualTo(-90));
                Assert.That(HibGap(hib), Is.LessThan(40));
                Assert.That(hib.Apart, Is.True);
            });
        }

        [Test]
        public void APairIsOnlyTakenWhenBothHalvesCanWalkToTheGate()
        {
            // Hibernia's -90 spots have no walk to the gate: the next pair down.
            bool Reaches(Vector3 at) => System.Math.Abs(Rally.ArrivalBearing(OuterGate, East, at) + 90) > 1;
            Rally.RallyPlan mid = Rally.PlanRally(OuterGate, East, MidArrival, new Vector3[0], Anywhere, Reaches, PcClearance,
                partnerArrival: HibArrival, centre: Centre);
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, Reaches, PcClearance,
                partnerArrival: MidArrival, partnerFirst: true, centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(hib.Bearing, Is.Not.EqualTo(-90));
                Assert.That(Flat(mid.Partner!.Value, hib.Point!.Value), Is.LessThan(1));
                Assert.That(Flat(hib.Partner!.Value, mid.Point!.Value), Is.LessThan(1));
                Assert.That(Flat(mid.Point.Value, hib.Point.Value), Is.GreaterThanOrEqualTo(Rally.RallySeparation));
                Assert.That(System.Math.Max(MidGap(mid), HibGap(hib)), Is.LessThanOrEqualTo(Rally.FarSide), "still both on their own side");
            });
        }

        [Test]
        public void ABarrenSpotIsPassedOverByBothRealmsPlans()
        {
            Rally.RallyPlan first = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, _ => true, PostClearance,
                partnerArrival: MidArrival, partnerFirst: true, centre: Centre);
            Vector3[] barren = { first.Point!.Value };
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new Vector3[0], Anywhere, _ => true, PostClearance,
                partnerArrival: MidArrival, partnerFirst: true, barren: barren, centre: Centre);
            Rally.RallyPlan mid = Rally.PlanRally(OuterGate, East, MidArrival, new Vector3[0], Anywhere, _ => true, PostClearance,
                partnerArrival: HibArrival, partnerFirst: false, partnerBarren: barren, centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(Flat(hib.Point!.Value, first.Point.Value), Is.GreaterThan(Rally.BarrenRadius), "moved off the barren spot");
                Assert.That(hib.UsedBarren, Is.False);
                Assert.That(Flat(mid.Partner!.Value, hib.Point.Value), Is.LessThan(1), "Midgard's plan knows Hibernia's barren spot");
                Assert.That(Flat(hib.Partner!.Value, mid.Point!.Value), Is.LessThan(1));
            });
        }

        [Test]
        public void WhenEverySeparatedSpotIsBarrenTheRallyKeepsApartInsteadOfSharingOne()
        {
            // 0acc99b with the near ring only: Midgard +60 barren (20:48), the swap, then Hibernia +60 barren too (20:53). The
            // re-plan then gave Hibernia 36133,38275 (apart=932) and Midgard the same spot (apart=0).
            Vector3[] farSide = { OuterGate + new Vector3(750, 1_299, 0), OuterGate + new Vector3(900, 1_559, 0) }; // +60 at 1,500 and 1,800
            Vector3[] midBarren = farSide, hibBarren = farSide;
            Rally.RallyPlan hib = Rally.PlanRally(OuterGate, East, HibArrival, new[] { MidC165 }, Anywhere, NearRingOnly, PcClearance,
                partnerArrival: MidArrival, partnerFirst: true, barren: hibBarren, partnerBarren: midBarren, centre: Centre);
            Rally.RallyPlan alone = Rally.PlanRally(OuterGate, East, HibArrival, new[] { MidC165 }, Anywhere, NearRingOnly, PcClearance,
                barren: hibBarren, centre: Centre);
            Assert.Multiple(() =>
            {
                Assert.That(hib.Partner, Is.Not.Null, "a pair is still found, barren spots and all");
                Assert.That(hib.Apart, Is.True);
                Assert.That(hib.UsedBarren, Is.True);
                Assert.That(Flat(hib.Point!.Value, hib.Partner!.Value), Is.GreaterThanOrEqualTo(Rally.RallySeparation));
                Assert.That(alone.Apart, Is.True, "alone: the barren spot apart, not the straight-out spot on Midgard's");
                Assert.That(alone.UsedBarren, Is.True);
                Assert.That(Flat(alone.Point!.Value, SharedPoint), Is.GreaterThan(1));
                // ...and the gathering rally keeps the spot it has rather than take either.
                Assert.That(Rally.KeepsSpot(2_546, hib.Apart, hib.UsedBarren), Is.True);
            });
        }

        [TestCase(2_546f, false, false, true)]  // 0acc99b 20:53: apart=932 planned, the rally stood 2,546 from Midgard's
        [TestCase(2_546f, true, true, true)]    // only a barren spot left: no trading spots every five minutes
        [TestCase(2_546f, true, false, false)]  // a better spot apart: it moves
        [TestCase(932f, false, false, false)]   // already on top of the other rally: nothing to keep
        [TestCase(932f, true, true, false)]
        public void AReplanKeepsASeparatedSpotRatherThanTakeAWorseOne(float fromOthers, bool apart, bool barren, bool keeps) =>
            Assert.That(Rally.KeepsSpot(fromOthers, apart, barren), Is.EqualTo(keeps));

        [Test]
        public void RallyPairsAreTheSameFromBothSides()
        {
            var candidates = new Rally.RallyCandidate[]
            {
                new(0, 900, 1_500, float.MaxValue), new(30, 1_800, 1_500, float.MaxValue),
                new(-60, 1_800, 1_500, float.MaxValue), new(60, 1_800, 1_200, float.MaxValue), // under a guard
            };
            var points = new Vector3[]
            {
                OuterGate + new Vector3(900, 0, 0), OuterGate + new Vector3(1_559, 900, 0),
                OuterGate + new Vector3(900, -1_559, 0), OuterGate + new Vector3(900, 1_559, 0),
            };
            var mine = Rally.RallyPairs(candidates, points, -33, -127, partnerFirst: false);
            var theirs = Rally.RallyPairs(candidates, points, -127, -33, partnerFirst: true);
            var barren = Rally.RallyPairs(candidates, points, -33, -127, false, new[] { false, true, false, false });
            Assert.Multiple(() =>
            {
                Assert.That(mine[0], Is.EqualTo((1, 2)));
                Assert.That(theirs[0], Is.EqualTo((2, 1)));
                Assert.That(mine, Has.None.Matches<(int Mine, int Partner)>(pair => pair.Mine == 3 || pair.Partner == 3), "never under a guard");
                Assert.That(mine, Has.None.Matches<(int Mine, int Partner)>(pair => pair == (0, 2) || pair == (2, 0)), "1,800 apart is too near");
                Assert.That(barren[0], Is.EqualTo((2, 1)), "a barren spot is taken only after every pair without one");
                Assert.That(barren, Has.Some.Matches<(int Mine, int Partner)>(pair => pair.Mine == 1), "...but it is not dropped");
            });
        }

        [Test]
        public void TheNearRingComesFirstForARealmAlone()
        {
            var candidates = new Rally.RallyCandidate[]
            {
                new(-60, 1_800, 1_500, float.MaxValue),
                new(-90, 2_700, 1_500, float.MaxValue, Far: true),
                new(60, 1_800, 1_500, float.MaxValue),
                new(-90, 2_100, 1_500, 1_000, Far: true), // on the other realm's rally
            };
            Assert.Multiple(() =>
            {
                Assert.That(Rally.RallyPreference(candidates, -127), Is.EqualTo(new[] { 0, 1, 2 }), "on its side: near, then far; then the far side");
                Assert.That(Rally.RallyPreference(candidates.Skip(1).ToArray(), -127), Is.EqualTo(new[] { 0, 1 }),
                    "the far ring on its side before the near ring across the gate");
            });
        }

        [TestCase(5 * 60_000L, 4, 33, true)]   // c165c90 Hibernia: peak 4 of needed 19, 33 inside
        [TestCase(5 * 60_000L - 1, 4, 33, false)]
        [TestCase(10 * 60_000L, 8, 33, false)] // drew a wave's minimum: not barren
        [TestCase(10 * 60_000L, 0, 5, false)]  // too few of the realm here to gather anyway
        public void ARallyThatDrawsNobodyIsBarren(long gathering, int peak, int inside, bool barren) =>
            Assert.That(Rally.IsBarren(gathering, peak, inside), Is.EqualTo(barren));

        [TestCase(true, 541f, 539f, false)]  // c165c90 Conyff on the stair, 539 under Lady Thidranki
        [TestCase(true, 1_274f, 867f, false)] // the ground floor inside the keep door, under her
        [TestCase(true, 160f, 68f, true)]    // the last flight (4524) and her floor (4592)
        [TestCase(true, 1_000f, 0f, false)]  // her floor, beyond the strike radius: walk on
        [TestCase(false, 120f, 100f, true)]  // a scout on the next flight, on it
        [TestCase(false, 300f, 308f, false)] // a guard a floor up
        public void AStormerStrikesOnlyOnItsTargetsFloor(bool isLord, float distance, float height, bool strikes) =>
            Assert.That(Rally.StrikesFromHere(isLord, distance, height), Is.EqualTo(strikes));

        [Test]
        public void ANearbyPointIsFoundWithinItsRadius()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.IsNearAny(HibC165, new[] { HibC165 + new Vector3(299, 0, 500) }, Rally.BarrenRadius), Is.True, "flat");
                Assert.That(Rally.IsNearAny(HibC165, new[] { HibC165 + new Vector3(301, 0, 0) }, Rally.BarrenRadius), Is.False);
                Assert.That(Rally.IsNearAny(HibC165, null, Rally.BarrenRadius), Is.False);
            });
        }

        // Owner's PC, build 95af6fd (Thidranki 2026-11-15 22:37-23:00): a walk from the gate's front through keep door 252000302 and
        // up Lady Thidranki's stair, as BG_LORD_PATH and the storm traces placed it (ground 3,720-3,728, her floor 4,592).
        private static readonly Vector3 LadyThidranki = new(32296, 38267, 4592);
        private static readonly Vector3[] TowerWalk =
        {
            new(34563, 38275, 3720), OuterGate, new(32654, 38180, 3720), new(32420, 38150, 3728),
            new(32285, 38215, 4053), new(32250, 38300, 4380), LadyThidranki,
        };

        [Test]
        public void TheFootOfTheTowerIsTheWalksLastPointOnTheGround()
        {
            float NoGuards(Vector3 _) => float.MaxValue;
            float GuardAtTheStair(Vector3 at) => Vector3.Distance(at, new Vector3(32420, 38150, 3728));
            float GuardsEverywhere(Vector3 _) => 100;
            Assert.Multiple(() =>
            {
                Assert.That(Rally.TowerFootIndex(TowerWalk, LadyThidranki, NoGuards), Is.EqualTo(3), "where the stair starts, 864 under her");
                Assert.That(Rally.TowerFootIndex(TowerWalk, LadyThidranki, GuardAtTheStair), Is.EqualTo(1),
                    "a guard at the stair: back along the walk to the first point clear of him (the keep door is 235 from him)");
                Assert.That(Rally.TowerFootIndex(TowerWalk, LadyThidranki, GuardsEverywhere), Is.EqualTo(3), "nothing clear: the first choice");
                Assert.That(Rally.TowerFootIndex(TowerWalk, LadyThidranki + new Vector3(0, 0, -400), NoGuards), Is.EqualTo(-1),
                    "a lord less than 600 up: no foot, the stormers walk straight at him");
                Assert.That(Rally.TowerFootIndex(new[] { TowerWalk[0], TowerWalk[1] }, LadyThidranki, NoGuards), Is.EqualTo(0),
                    "a walk cut short on the ground: its start, never its end");
                Assert.That(Rally.TowerFootIndex(new[] { TowerWalk[3] }, LadyThidranki, NoGuards), Is.EqualTo(-1));
            });
        }

        [TestCase(0, 12, 0L, false)]        // nobody there yet
        [TestCase(5, 0, 0L, true)]          // everyone still standing is there
        [TestCase(14, 6, 0L, true)]         // 70%
        [TestCase(13, 7, 0L, false)]        // 65%: wait for the rest
        [TestCase(13, 7, 25_000L, true)]    // ...but not longer than 25 s
        [TestCase(1, 19, 24_999L, false)]
        public void TheClimbGoesWhenTheWaveIsAtTheFoot(int atFoot, int coming, long waited, bool goes) =>
            Assert.That(Rally.ClimbGoes(atFoot, coming, waited), Is.EqualTo(goes));

        [TestCase(true, false, true)]   // a caster, archer or healer: the lord
        [TestCase(true, true, true)]    // ...even with a guard on it
        [TestCase(false, true, false)]  // a melee bot with a guard on it: the guard
        [TestCase(false, false, true)]  // a melee bot with nothing on it: the lord
        public void OnHerFloorCastersStrikeTheLordAndMeleeTakeTheGuardOnThem(bool rangedOrHealer, bool enemyOnMe, bool lord) =>
            Assert.That(Rally.StrikesLordFirst(rangedOrHealer, enemyOnMe), Is.EqualTo(lord));

        [Test]
        public void AStormIsJoinedOnlyInItsPush()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.JoinsOtherStorm(null), Is.False);
                Assert.That(Rally.JoinsOtherStorm(5_000), Is.True, "22:46:45/50 and 22:51:53/54: 1-5 s apart");
                Assert.That(Rally.JoinsOtherStorm(Rally.JoinWindowMilliseconds), Is.True);
                // 22:53:25: Midgard went with 12 of 16, 0.0 minutes after its regroup, on Hibernia's storm of 22:51:54.
                Assert.That(Rally.JoinsOtherStorm(91_000), Is.False, "the other realm's tail, 91 s in");
                Assert.That(Rally.ShouldStorm(12, 16, 0, Rally.JoinsOtherStorm(91_000)), Is.False, "it gathers on");
                Assert.That(Rally.ShouldStorm(12, 16, 0, Rally.JoinsOtherStorm(20_000)), Is.True);
            });
        }

        [TestCase(0L, true, 8, 18, true)]       // the other realm has 44% of its 18: wait for it
        [TestCase(44_999L, true, 8, 18, true)]
        [TestCase(45_000L, true, 8, 18, false)] // not longer than 45 s
        [TestCase(0L, false, 8, 18, false)]     // it storms already: go (and join it)
        [TestCase(0L, true, 18, 18, false)]     // it is ready itself: both go
        [TestCase(0L, true, 7, 18, false)]      // 38%: too far off
        [TestCase(0L, true, 0, 0, false)]
        public void AReadyRallyHoldsBrieflyForTheOtherRealm(long waited, bool otherGathering, int otherRallied, int otherNeeded, bool holds) =>
            Assert.That(Rally.HoldsForOtherRealm(waited, otherGathering, otherRallied, otherNeeded), Is.EqualTo(holds));

        [Test]
        public void TheRegroupSaysHowTogetherTheyReachedHerFloor()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.SummariseArrivals(new[] { 40, 35, 40 }, new[] { 30 }, 14),
                    Is.EqualTo("floorArrivals=3 arrivalSeconds=35,40,40 arrivalSpread=5 climbs=30 footPeak=14"));
                Assert.That(Rally.SummariseArrivals(new int[0], new int[0], 0),
                    Is.EqualTo("floorArrivals=0 arrivalSeconds=- arrivalSpread=-1 climbs=- footPeak=0"));
            });
        }
    }
}
