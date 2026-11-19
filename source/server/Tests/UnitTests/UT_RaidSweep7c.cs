using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, build 9a173f4, 2026-11-18/19 (sweep 7c, c9a17d.log.all): T-1 (a stranded raider teleported into the resting
    /// Suttung's aggro), P-1 (Steinvor's Pillar of Ice outlived him), H-1 (twelve healers cured one mesmerized raider), C-1
    /// (Curengkur's first staging post had no corridor from the hub's side), the dragon-hibernia hub post and the formation
    /// pass after two raids ended together. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RaidSweep7c
    {
        // ---- T-1: the raid's own resting target is a danger for the stranded teleport ----------------------------------------

        private static readonly Vector3 Suttung = new(32074, 54243, 11883);       // 21 from his spawn, resting (04:03:24)
        private static readonly Vector3 SuttungMuster = new(32787, 53968, 11895); // STRANDED_REGROUP Pwnage9 to= (04:03:29)

        [Test]
        public void AStrandedRaiderIsNotSentToAMusterInsideTheRestingTargetsAggro()
        {
            Assert.That(Vector3.Distance(SuttungMuster, Suttung), Is.EqualTo(764).Within(1), "the log's distance");
            // 9a173f4: the guard leaves the raid's own target out, so the muster 764 from him was "clear".
            List<R.BossDanger> old = R.RestingDangersOf(
                [new("Elder Icelord Suttung", Suttung, Suttung, 600, false, false, true, false, OfTheTarget: true)]);
            Assert.That(R.StrandedRegroupPoint(true, Suttung, SuttungMuster, SuttungMuster, old), Is.EqualTo(SuttungMuster),
                "the teleport that pulled him with nobody mustered");

            List<R.BossDanger> own = R.RestingTargetDangers("Elder Icelord Suttung", Suttung, Suttung, 600, resting: true,
                musteredEncounter: true, musterStandOff: R.FinalMusterStandOff);
            Assert.That(own, Has.Count.EqualTo(1));
            Assert.That(own[0].Radius, Is.EqualTo(600 + R.UntargetedBossMargin), "aggro 600 + 300");
            List<R.BossDanger> now = [.. old, .. own];
            Assert.That(R.StrandedRegroupPoint(true, Suttung, SuttungMuster, SuttungMuster, now), Is.Null,
                "now nobody is moved there (REGROUP_REFUSED names him)");
            Assert.That(R.RefusalText(SuttungMuster, now), Does.StartWith("Elder Icelord Suttung@32074,54243,11883/distance=764"));
            // A muster clear of his aggro + 300 still takes stranded raiders.
            var clear = new Vector3(Suttung.X + 1400, Suttung.Y, Suttung.Z);
            Assert.That(R.StrandedRegroupPoint(true, Suttung, clear, clear, now), Is.EqualTo(clear));
        }

        [Test]
        public void TheTargetIsOnlyADangerWhileItRestsAndIsMustered()
        {
            Assert.That(R.RestingTargetDangers("Suttung", Suttung, Suttung, 600, resting: false, musteredEncounter: true, 1400), Is.Empty,
                "in his fight the raid goes to him");
            Assert.That(R.RestingTargetDangers("trash", Suttung, Suttung, 600, resting: true, musteredEncounter: false, 1400), Is.Empty,
                "trash the route clears is not mustered for");
            Assert.That(R.RestingTargetDangers("Suttung", Suttung, Suttung, 0, true, true, 1400), Is.Empty, "no aggro");
            // Walking home away from his spawn: both spots.
            var away = new Vector3(Suttung.X - 500, Suttung.Y, Suttung.Z);
            Assert.That(R.RestingTargetDangers("Suttung", Suttung, away, 600, true, true, 1400), Has.Count.EqualTo(2));
        }

        [Test]
        public void AMusterAShortTetherPutsCloserStillTakesStrandedRaiders()
        {
            // A reach-limited muster 700 from a boss of aggro 600 (RealmRaidTetherReach.MusterStandOff): the clearance is the
            // stand-off, not aggro + 300, so his stranded raiders are still moved to it.
            Assert.That(R.TargetClearance(700, 600), Is.EqualTo(700));
            var muster = new Vector3(Suttung.X + 700, Suttung.Y, Suttung.Z);
            List<R.BossDanger> own = R.RestingTargetDangers("Aidon", Suttung, Suttung, 600, true, true, 700);
            Assert.That(R.StrandedRegroupPoint(true, Suttung, muster, muster, own), Is.EqualTo(muster));
            Assert.That(R.InTargetClearance(muster, Suttung, 700, 600), Is.False);
        }

        [Test]
        public void AMusterInsideTheTargetsOwnAggroIsMovedOut()
        {
            // The walk's "the raid is already close" choice put Suttung's muster 764 from him with a 1,400 stand-off.
            Assert.That(R.InTargetClearance(SuttungMuster, Suttung, R.FinalMusterStandOff, 600), Is.True);
            Assert.That(R.InTargetClearance(new(Suttung.X + 900, Suttung.Y, Suttung.Z), Suttung, R.FinalMusterStandOff, 600), Is.False,
                "at aggro + 300");
            Assert.That(R.InTargetClearance(new(Suttung.X + 1400, Suttung.Y, Suttung.Z), Suttung, R.FinalMusterStandOff, 600), Is.False,
                "at the stand-off");
            Assert.That(R.InTargetClearance(SuttungMuster, Suttung, R.FinalMusterStandOff, 0), Is.False, "no aggro, no clearance");
        }

        // ---- P-1: the Pillar of Ice leaves on its own timer ----------------------------------------------------------------

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the scripts are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray()));
        }

        [Test]
        public void ThePillarOfIceLeavesOnItsOwnTimerWhetherSteinvorLivesOrNot()
        {
            string script = Source("scripts", "namedmobs", "TuscarianGlacier", "IcelordSteinvorAndSkuf .cs").Replace("\r\n", "\n");
            int mob = script.IndexOf("public class EffectMob : GameEpicNPC", StringComparison.Ordinal);
            Assert.That(mob, Is.GreaterThan(0));
            string pillar = script[mob..];
            int add = pillar.IndexOf("public override bool AddToWorld()", StringComparison.Ordinal);
            string addToWorld = pillar[add..pillar.IndexOf("return success;", add, StringComparison.Ordinal)];
            Assert.That(addToWorld, Does.Contain("new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(Expire), LifetimeMilliseconds)"),
                "9a173f4: only Steinvor's EffectTimer removed it, and only if (Body.IsAlive)");
            Assert.That(addToWorld, Does.Contain("ECSTimerCallback(Show_Effect), 3000"), "its one Pillar of Frost is unchanged");
            Assert.That(EffectMob.LifetimeMilliseconds, Is.EqualTo(8000), "the moment Steinvor removed it at");
            Assert.That(EffectMob.LifetimeMilliseconds, Is.GreaterThan(3000 + 500), "the pillar casts before it leaves");
            Assert.That(script, Does.Contain("ECSTimerCallback(EffectTimer), EffectMob.LifetimeMilliseconds"),
                "while he lives he removes it at the same moment, as before");
            Assert.That(pillar, Does.Contain("if (ObjectState == eObjectState.Active)\n                RemoveFromWorld();"));
        }

        // ---- H-1: a raider one curer is curing is left to it -----------------------------------------------------------------

        [SetUp]
        public void ClearClaims() => RaidCureClaims.Clear();

        [Test]
        public void TwelveCurersNoLongerAllCureTheOneMesmerizedRaider()
        {
            var captainfluffy = new List<RaidCureMezz.Mezzed> { new(9001, false, false, false) };
            // 9a173f4: every curer picks him (02:21:52, 12 Cleanse Mind on Captainfluffy).
            Assert.That(Enumerable.Range(1, 12).Select(c => RaidCureMezz.Choose(captainfluffy, c)), Is.All.EqualTo(0));

            const long now = 1_000_000;
            int taking = 0;
            foreach (long curer in Enumerable.Range(1, 12))
            {
                int pick = RaidCureMezz.Choose(captainfluffy, curer, id => RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, id, curer, now));
                if (pick >= 0 && RaidCureClaims.TryClaim(RaidCureClaims.Kind.Mezz, captainfluffy[pick].Id, curer, now, 3000)) taking++;
            }
            Assert.That(taking, Is.EqualTo(1), "one curer casts; the other eleven heal or cure someone else");
        }

        [Test]
        public void AClaimLastsTheCastTimeAndHalfASecond()
        {
            Assert.That(RaidCureClaims.Duration(3000), Is.EqualTo(3500));
            Assert.That(RaidCureClaims.TryClaim(RaidCureClaims.Kind.Mezz, 7, curerId: 1, now: 0, castTimeMs: 3000), Is.True);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, 7, curerId: 2, now: 3499), Is.True);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, 7, curerId: 1, now: 3499), Is.False, "its own claim never blocks it");
            Assert.That(RaidCureClaims.TryClaim(RaidCureClaims.Kind.Mezz, 7, 2, 1000, 3000), Is.False);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, 7, 2, 3500), Is.False, "it ends on its own");
            Assert.That(RaidCureClaims.TryClaim(RaidCureClaims.Kind.Mezz, 7, 2, 3500, 3000), Is.True);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Poison, 7, 1, 3600), Is.False, "the poison cure has its own claims");
            // A cast that did not start gives the raider back.
            RaidCureClaims.Release(RaidCureClaims.Kind.Mezz, 7, curerId: 1);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, 7, 1, 3600), Is.True, "only its own claim is released");
            RaidCureClaims.Release(RaidCureClaims.Kind.Mezz, 7, curerId: 2);
            Assert.That(RaidCureClaims.IsClaimed(RaidCureClaims.Kind.Mezz, 7, 1, 3600), Is.False);
        }

        [Test]
        public void AClaimedRaiderIsSkippedForTheNextMostUrgent()
        {
            // A curer first (rank 0), then a healer (rank 1): the claimed curer is left out, the healer is taken.
            var mezzed = new List<RaidCureMezz.Mezzed> { new(1, true, false, false), new(2, false, true, false) };
            Assert.That(RaidCureMezz.Choose(mezzed, 5, id => id == 1), Is.EqualTo(1));
            Assert.That(RaidCureMezz.Choose(mezzed, 5, _ => true), Is.EqualTo(-1), "all claimed: the bot heals");
            Assert.That(RaidCureMezz.Choose(mezzed, 5, null), Is.EqualTo(0), "no claims: as before");
        }

        [Test]
        public void TheSingleCurePoisonSkipsAClaimedRaiderToo()
        {
            // Two raiders of other parties carry Summoner Pain: 9a173f4 split twelve curers six and six on them.
            var dotted = new List<RaidCurePoison.Dotted> { new(11, false, 60), new(12, false, 70) };
            Assert.That(Enumerable.Range(1, 12).Count(c => RaidCurePoison.Choose(dotted, c) == 0), Is.EqualTo(6));
            var (cure, pick) = RaidCurePoison.Decide(true, true, false, true, dotted, 0, false, 2, id => id == 11);
            Assert.That((cure, pick), Is.EqualTo((RaidCurePoison.Cure.Single, 1)), "the other dotted raider");
            (cure, pick) = RaidCurePoison.Decide(true, true, false, true, dotted, 0, false, 2, _ => true);
            Assert.That((cure, pick), Is.EqualTo((RaidCurePoison.Cure.None, -1)), "both are being cured: heal");
            // The own party's lowest still comes first when it is free; the group cure is not claimed.
            var own = new List<RaidCurePoison.Dotted> { new(21, true, 40), new(22, true, 50), new(11, false, 30) };
            Assert.That(RaidCurePoison.Decide(true, true, true, true, own, 0, false, 2, _ => true).Cure, Is.EqualTo(RaidCurePoison.Cure.Group));
        }

        // ---- C-1: the next staging posts when the hub's route reaches no corridor to the first --------------------------------

        [Test]
        public void TheStartTriesTheNextStagingPostsOnlyForAnInZoneCorridorFailure()
        {
            Assert.That(AutonomousRealmRaid.RerouteSlots("no corridor inside zone 187 to the staging post"),
                Is.EqualTo(Enumerable.Range(1, AutonomousRealmRaid.StagingRouteRetries)), "Curengkur, 04:32:18");
            Assert.That(AutonomousRealmRaid.RerouteSlots("no seam from zone 185 toward zone 187 at 1,2,3"), Is.Empty,
                "a seam failure is the same for every post");
            Assert.That(AutonomousRealmRaid.RerouteSlots("stopped in zone 186 after 3 zones"), Is.Empty);
            Assert.That(AutonomousRealmRaid.RerouteSlots(null), Is.Empty);
        }

        // ---- dragon-hibernia: the walk from the hub starts on the hub's floor; the rejecting check is logged -------------------

        /// <summary>Flat connected ground everywhere but a hole right under the hub's centre (an NPC's spot off the mesh).</summary>
        private sealed class HubMesh : PathfindingMgrBase
        {
            public Vector3 Hole;
            public bool Empty;
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters)
            {
                if (Empty) return null;
                if (Vector2.Distance(new(position.X, position.Y), new(Hole.X, Hole.Y)) < 10)
                    return x <= 2 ? null : new Vector3(Hole.X + 40, Hole.Y, 0);
                return position with { Z = 0 };
            }
            public override bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) => true;
            public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
            {
                nodes[0] = new(to, EDtPolyFlags.Walk);
                return new(PathfindingStatus.PathFound, 1);
            }
        }

        private static readonly Zone LoughGur = new(null, 204, "Lough Gur", 0, 0, 65536, 65536, 204, false, 0, false, 0, 0, 0, 0, 0);
        private static readonly Vector3 HubCentre = new(30000, 30000, 0);

        [Test]
        public void TheWalkFromAHubStartsOnItsFloorWhenItsCentreIsOffTheMesh()
        {
            var nav = new HubMesh { Hole = HubCentre };
            var post = new Vector3(31000, 30000, 0);
            Assert.That(RealmRaidMuster.HubReachesPost(nav, LoughGur, HubCentre, post), Is.False,
                "from the centre itself (#224) every post fails");
            Vector3 floor = RealmRaidMuster.HubFloor(nav, LoughGur, HubCentre, out string how);
            Assert.That(how, Is.EqualTo("nearest"));
            Assert.That(Vector3.Distance(floor, HubCentre), Is.LessThanOrEqualTo(RealmRaidMuster.HubFloorReach));
            Assert.That(RealmRaidMuster.HubReachesPost(nav, LoughGur, floor, post), Is.True);
            var hub = new RealmRaidMuster.Hub("dragon-hibernia", "Innis Carthaig", 200, 204, 0, 0, HubCentre);
            Assert.That(RealmRaidMuster.TryPost(nav, LoughGur, hub, 0, [], out Vector3 placed), Is.True, "a muster post is placed");
            Assert.That(Vector3.Distance(placed, HubCentre), Is.GreaterThanOrEqualTo(400));
            // A centre on the floor is used as it is.
            Assert.That(RealmRaidMuster.HubFloor(new HubMesh { Hole = new(1, 1, 0) }, LoughGur, HubCentre, out how), Is.EqualTo(HubCentre));
            Assert.That(how, Is.EqualTo("centre"));
        }

        [Test]
        public void AFailedHubPostNamesTheCheckThatRefusedIt()
        {
            var hub = new RealmRaidMuster.Hub("dragon-hibernia", "Innis Carthaig", 200, 204, 0, 0, HubCentre);
            Assert.That(RealmRaidMuster.TryPost(new HubMesh { Empty = true }, LoughGur, hub, 0, [], out _), Is.False);
            Assert.That(RealmRaidMuster.LastPostRejections, Does.StartWith($"rejectedBy=NoFloor NoFloor={RealmRaidMuster.MusterPostAttempts},Height=0"));
            Assert.That(RealmRaidMuster.TryPost(new HubMesh { Hole = HubCentre }, LoughGur, hub, 0, [], out _, _ => false), Is.False);
            Assert.That(RealmRaidMuster.LastPostRejections, Does.StartWith("rejectedBy=Unsafe ").And.Contain("hubFloor=30040,30000,0(nearest)"));
            Assert.That(RealmRaidMuster.PostRejectionText([0, 0, 0, 0, 0, 0, 5, 1]),
                Is.EqualTo("rejectedBy=NoWalkFromHub NoFloor=0,Height=0,Spacing=0,Unsafe=0,NoExit=0,NoWalkToHub=0,NoWalkFromHub=5,NoPartySpace=1"));
            Assert.That(RealmRaidMuster.PostRejectionText([0, 0, 0, 0, 0, 0, 0, 0]), Does.StartWith("rejectedBy=none "));
        }

        // ---- the formation pass after two raids ended together ---------------------------------------------------------------

        [Test]
        public void AFormationPassStopsItsRendezvousChecksPastItsHardCap()
        {
            Assert.That(AutonomousBotGroupCoordinator.FormationPassHardCapMilliseconds,
                Is.EqualTo(2 * AutonomousBotGroupCoordinator.FormationPassBudgetMilliseconds));
            Assert.That(AutonomousBotGroupCoordinator.DefersRendezvous(2817, AutonomousBotGroupCoordinator.FormationPassHardCapMilliseconds), Is.True,
                "04:07:46: one pass held the coordinator lock 2,817 ms");
            Assert.That(AutonomousBotGroupCoordinator.DefersRendezvous(120, AutonomousBotGroupCoordinator.FormationPassHardCapMilliseconds), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.DefersRendezvous(5000, 0), Is.False, "no cap outside a formation pass");
        }
    }
}
