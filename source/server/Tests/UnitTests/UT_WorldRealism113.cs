using System;
using System.Numerics;
using DOL.GS;
using DOL.UnitTests;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.1.3 world fixes from the owner's RC nights (2026-11-08/09): gamebots waiting in their capital for a frontier
    /// teleporter that isn't there, stale camp searches, and the AI turns and coordinator passes that stalled the server.
    /// </summary>
    [TestFixture]
    public class UT_WorldRealism113
    {
        // ---- Frontier teleporters: a capital has none --------------------------------------------------------------------

        [TestCase(eRealm.Albion, (ushort)1)]
        [TestCase(eRealm.Midgard, (ushort)100)]
        [TestCase(eRealm.Hibernia, (ushort)200)]
        [TestCase(eRealm.None, (ushort)0)]
        public void EachRealmHasItsOwnFrontier(eRealm realm, ushort region) =>
            Assert.That(AutonomousFrontierTransport.HomeFrontier(realm), Is.EqualTo(region));

        [TestCase((ushort)201, (ushort)100, (ushort)200, ExpectedResult = true, TestName = "From Tir na Nog to Midgard's frontier: Hibernia's own first")]
        [TestCase((ushort)101, (ushort)200, (ushort)100, ExpectedResult = true, TestName = "From Jordheim to Dun Bolg: Midgard's own first")]
        [TestCase((ushort)10, (ushort)100, (ushort)1, ExpectedResult = true, TestName = "From Camelot to Glenlock Faste: Albion's own first")]
        [TestCase((ushort)201, (ushort)200, (ushort)200, ExpectedResult = false, TestName = "To its own frontier it walks as before")]
        [TestCase((ushort)200, (ushort)100, (ushort)200, ExpectedResult = false, TestName = "In its own frontier the porter takes it")]
        [TestCase((ushort)1, (ushort)100, (ushort)200, ExpectedResult = false, TestName = "In another frontier the porters there take over")]
        [TestCase((ushort)201, (ushort)249, (ushort)200, ExpectedResult = false, TestName = "Not for a destination outside the frontiers")]
        public bool ABotOutsideTheFrontiersGoesToItsOwnFirst(ushort current, ushort destination, ushort home) =>
            AutonomousFrontierTransport.NeedsHomeFrontierFirst(current, destination, home);

        [Test]
        public void TheHomeLegEndsWhereTheMedallionsAreSold() => Assert.Multiple(() =>
        {
            // The home necklace's landing at each realm's first border keep (Castle Sauvage, Svasud Faste, Druim Ligen).
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                ushort home = AutonomousFrontierTransport.HomeFrontier(realm);
                var passage = AutonomousFrontierTransport.Destination(realm, home);
                Assert.That(passage?.Medallion, Is.EqualTo("home_necklace"), realm.ToString());
                Assert.That(passage?.Location.RegionID, Is.EqualTo(home), realm.ToString());
            }
        });

        // ---- A camp search that holds a bot for minutes ------------------------------------------------------------------

        [Test]
        public void AStaleCampSearchIsResetAfterThreeMinutes() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousWorldBotController.CampSearchIsStale(0, 10_000_000), Is.False, "not searching");
            Assert.That(AutonomousWorldBotController.CampSearchIsStale(1_000, 1_000 + 179_999), Is.False);
            Assert.That(AutonomousWorldBotController.CampSearchIsStale(1_000, 1_000 + 180_000), Is.True);
            Assert.That(AutonomousWorldBotController.StaleCampSearchMilliseconds, Is.LessThan(15 * 60_000),
                "well before the fifteen-minute watchdog");
        });

        // ---- Camp target searches: a bounded number of route proofs ------------------------------------------------------

        /// <summary>A mesh with floor everywhere and no route anywhere, counting every query (the worst case for a proof).</summary>
        private sealed class NoRouteMesh : PathfindingMgrBase
        {
            public int Queries;
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override Vector3? GetClosestPoint(Zone zone, Vector3 p, EDtPolyFlags[] filters) { Queries++; return p; }
            public override Vector3? GetClosestPoint(Zone zone, Vector3 p, float x, float y, float z, EDtPolyFlags[] filters) { Queries++; return p; }
            public override Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 min, Vector3 max, EDtPolyFlags[] filters) { Queries++; return origin; }
            public override Vector3? GetNearestPoly(Zone zone, Vector3 point, EDtPolyFlags[] filters) { Queries++; return point; }
            public override bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) { Queries++; return true; }
            public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
            { Queries++; return new PathfindingResult(PathfindingStatus.NoPathFound, 0); }
            public override Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters) { Queries++; return position; }
        }

        [Test]
        public void ACampSearchProvesAFewRoutesNotEveryCandidate()
        {
            var nav = new NoRouteMesh();
            Vector3 bot = new(1000, 1000, 100);
            Assert.That(AutonomousDungeonTargetRoute.CanReach(nav, UT_RealmRaidFormation.Zone(), bot, new(1400, 1000, 100)), Is.False);
            int perProof = nav.Queries;

            // 2026-11-09 05:17 (Furyspike, dampwood mites): one search proved candidate after candidate. Each failed proof
            // costs the same, so the queries of one search grow with the candidates; now they stop at the budget.
            int candidates = 200, proofs = 0;
            nav.Queries = 0;
            for (int i = 0; i < candidates; i++)
            {
                if (!AutonomousCampTargetProof.MayProveRoute(proofs)) break;
                proofs++;
                AutonomousDungeonTargetRoute.CanReach(nav, UT_RealmRaidFormation.Zone(), bot, new(1400 + i * 10, 1000, 100));
            }
            TestContext.WriteLine($"queries per failed proof={perProof}; one search over {candidates} unreachable candidates: " +
                $"before={perProof * candidates} now={nav.Queries}");
            Assert.Multiple(() =>
            {
                Assert.That(perProof, Is.GreaterThan(0));
                Assert.That(proofs, Is.EqualTo(AutonomousCampTargetProof.RouteProofsPerSearch));
                Assert.That(nav.Queries, Is.EqualTo(perProof * AutonomousCampTargetProof.RouteProofsPerSearch));
                Assert.That(AutonomousCampTargetProof.MayCheckSight(AutonomousCampTargetProof.SightChecksPerSearch - 1), Is.True);
                Assert.That(AutonomousCampTargetProof.MayCheckSight(AutonomousCampTargetProof.SightChecksPerSearch), Is.False);
                Assert.That(AutonomousCampTargetProof.RetryMilliseconds, Is.EqualTo(60_000), "a failed target waits a minute");
            });
        }

        // ---- Group formation: a time budget under the coordinator's lock -------------------------------------------------

        [Test]
        public void AFormationPassStopsOnceItHasSpentItsBudget() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.FormationPassSpent(0, 5_000), Is.False, "the first leader is always tried");
            Assert.That(AutonomousBotGroupCoordinator.FormationPassSpent(1, 149), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.FormationPassSpent(1, 150), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.FormationPassBudgetMilliseconds, Is.LessThan(700),
                "the profile's slowest passes took 0.7-1.3 s");
        });
    }
}
