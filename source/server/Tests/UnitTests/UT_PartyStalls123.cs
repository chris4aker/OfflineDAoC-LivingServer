using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using Bench = DOL.GS.AutonomousDefensivePull.UnreachableBench;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3: party members that stood at zone borders (#538) and parties that stood on a ranged pull with no firing point
    /// (#539). Evidence: the owner's PC, build 1.2.2 868a82d, server.log 11/24 10:37-15:36.
    /// </summary>
    public class UT_PartyStalls123
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- #538: a leader over a zone border ----------------------------------------------------------------------------

        [Test]
        public void ALeaderOverABorderBeyondAStraightWalkGetsASeamRoute()
        {
            int walk = AutonomousGroupFormationFollow.BorderWalkDistance;
            // The logged case: Kryroul in Gotar (103), Oldwaffle in East Svealand (101), 1,522 units.
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 103, 101, 1522), Is.True);
            // All 1,784 cross-zone stalls of 11/24 were farther than the straight walk (median 1,720, p90 2,381).
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 200, 201, 2381), Is.True);
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 200, 201, walk + 1), Is.True);
            // Within the straight walk the follow keeps walking after a leader that just crossed.
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 200, 201, walk), Is.False);
            // Same zone: the ordinary formation follow; another region: the cross-region travel rules.
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 200, 200, 2000), Is.False);
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(false, 200, 201, 2000), Is.False);
            // No zone (between zones, a load) is never routed.
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 0, 201, 2000), Is.False);
            Assert.That(AutonomousGroupFormationFollow.NeedsSeamRoute(true, 200, 0, 2000), Is.False);
        }

        [Test]
        public void TheFollowCheckHandsAFarBorderToTheAiTurnInsteadOfPausing()
        {
            string tick = Source("ECS-Components", "NpcMovementComponent.cs");
            int start = tick.IndexOf("private int FollowTick()");
            int end = tick.IndexOf("private bool TryRecoverStuckBotPet", start);
            string body = tick[start..end];
            int walk = body.IndexOf("if (crossBorder && distance <= AutonomousGroupFormationFollow.BorderWalkDistance)");
            int handOff = body.IndexOf("AutonomousGroupFormationFollow.NoteBorderHandOff();");
            int path = body.IndexOf("PathToInternal(destination");
            Assert.That(walk, Is.GreaterThan(0));
            Assert.That(handOff, Is.GreaterThan(walk), "the hand-off follows the straight walk");
            Assert.That(path, Is.GreaterThan(handOff),
                "a far border never reaches PathToInternal, which paused the follower on a destination in another zone");
        }

        [Test]
        public void TheAiTurnRoutesTheMemberOverTheBorderBeforeTheFormationFollow()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            int start = controller.IndexOf("private bool FollowDynamicGroupLeader(");
            int end = controller.IndexOf("private static CampDestination FromSharedCamp", start);
            string body = controller[start..end];
            int seam = body.IndexOf("AutonomousGroupFormationFollow.NeedsSeamRoute(");
            int route = body.IndexOf("IssuePath(bot, new Vector3(leader.X, leader.Y, leader.Z), retargetWhileMoving: true);", seam);
            int ensure = body.IndexOf("AutonomousGroupFormationFollow.Ensure(");
            Assert.That(seam, Is.GreaterThan(0));
            Assert.That(route, Is.GreaterThan(seam), "the seam case paths through IssuePath (AutonomousZoneItinerary)");
            Assert.That(ensure, Is.GreaterThan(route), "it is decided before the formation follow");
            // IssuePath still crosses zones only through the itinerary, with its own give-up.
            Assert.That(controller, Does.Contain("AutonomousZoneItinerary.TryNextStep(bot.CurrentRegion, currentZone, destinationZone,"));
            Assert.That(controller, Does.Contain("AbandonCamp(bot, \"No safe connected zone seam on the selected route\");"));
        }

        [Test]
        public void AStalledMemberIsNeverMovedInSightOfAPlayer()
        {
            Assert.That(AutonomousGroupMemberStall.MayMoveUnseen(false, false), Is.True);
            Assert.That(AutonomousGroupMemberStall.MayMoveUnseen(true, false), Is.False, "a player sees the member");
            Assert.That(AutonomousGroupMemberStall.MayMoveUnseen(false, true), Is.False, "a player sees where it would appear");
            string stall = Source("bots", "autonomous", "AutonomousGroupMemberStall.cs");
            int start = stall.IndexOf("public static bool TryRecoverToLeader(");
            string body = stall[start..];
            Assert.That(body.IndexOf("MayMoveUnseen(PlayerSees(member), PlayerSees(leader))"),
                Is.GreaterThan(0).And.LessThan(body.IndexOf("member.MoveTo(")), "the sight check comes before the move");
        }

        // ---- #539: ranged pulls with no firing point ----------------------------------------------------------------------

        [Test]
        public void ATargetWithNoFiringPointIsBenchedOnItsSecondTry()
        {
            var bench = new Bench();
            bench.ForCamp("camp-a");
            Assert.That(bench.RecordUnreachable(7, 1_000), Is.False, "the first try waits for the retry");
            Assert.That(bench.IsBenched(7, 1_000), Is.False);
            Assert.That(bench.RecordUnreachable(7, 35_000), Is.True, "the second benches it");
            Assert.That(bench.IsBenched(7, 35_000), Is.True);
            Assert.That(bench.IsBenched(8, 35_000), Is.False, "another target is not benched");
            Assert.That(bench.RecordUnreachable(7, 36_000), Is.False, "a benched target is not counted again");
            Assert.That(bench.BenchedCount(36_000), Is.EqualTo(1));
            // The bench ends; the target is tried again and counted from one.
            long after = 35_000 + Bench.BenchMilliseconds;
            Assert.That(bench.IsBenched(7, after), Is.False);
            Assert.That(bench.RecordUnreachable(7, after), Is.False);
            Assert.That(bench.RecordUnreachable(7, after + 30_000), Is.True);
        }

        [Test]
        public void ThreeBenchedTargetsGiveTheCampUpAndANewCampStartsClean()
        {
            var bench = new Bench();
            bench.ForCamp("camp-a");
            long now = 0;
            for (int target = 1; target <= Bench.BenchedTargetsBeforeCampGiveUp; target++)
            {
                Assert.That(bench.ShouldGiveUpCamp(now), Is.False);
                bench.RecordUnreachable(target, now += 30_000);
                bench.RecordUnreachable(target, now += 30_000);
            }
            Assert.That(bench.ShouldGiveUpCamp(now), Is.True);
            bench.ForCamp("camp-a");
            Assert.That(bench.ShouldGiveUpCamp(now), Is.True, "the same camp keeps its bench");
            bench.ForCamp("camp-b");
            Assert.That(bench.BenchedCount(now), Is.EqualTo(0), "a new camp starts with a clean bench");
        }

        /// <summary>
        /// The worst run of 11/24: Hendrik's party and a sheevra skirmisher, 126 unreachable tries in 74 minutes, one every
        /// ~34 s (the 30 s retry plus the camp check). Replayed through the bench with every one of the camp's spawns
        /// unreachable, the party stands at most two tries per target and gives the camp up after the third target.
        /// </summary>
        [Test]
        public void TheWorstLoggedRunEndsInMinutesNotAnHourAndAHalf()
        {
            var bench = new Bench();
            bench.ForCamp("silvermine-sheevra-skirmisher");
            const long tryEvery = AutonomousDefensivePull.TimeoutMilliseconds + 4_000;
            long now = 0;
            int tries = 0, target = 100;
            while (!bench.ShouldGiveUpCamp(now) && tries < 126)
            {
                now += tryEvery;
                tries++;
                // A benched target is pulled the ordinary way and the next spawn comes up.
                if (bench.RecordUnreachable(target, now))
                    target++;
            }
            Assert.That(bench.ShouldGiveUpCamp(now), Is.True);
            Assert.That(tries, Is.EqualTo(Bench.TriesBeforeBench * Bench.BenchedTargetsBeforeCampGiveUp));
            Assert.That(now, Is.LessThan(5 * 60_000L), "under five minutes, against 74 (the 10-minute runs: 55 on 11/24)");
        }

        [Test]
        public void TheRangedPullAndTheCampWorkerUseTheBench()
        {
            string pull = Source("bots", "autonomous", "AutonomousDefensivePull.cs");
            int core = pull.IndexOf("private static bool TryBeginCore(");
            string body = pull[core..pull.IndexOf("public static bool TryFiringPoint(", core)];
            int benchedCheck = body.IndexOf("state.Bench.IsBenched(target.ObjectID, GameLoop.GameLoopTime))");
            int retry = body.IndexOf("if (GameLoop.GameLoopTime < state.RetryAfter) return true;");
            Assert.That(benchedCheck, Is.GreaterThan(0).And.LessThan(retry), "a benched target is not waited on");
            Assert.That(body.IndexOf("why = PartyPullStart.Benched;", benchedCheck), Is.GreaterThan(benchedCheck).And.LessThan(retry),
                "the benched target returns (false, Benched) before any wait");
            Assert.That(body, Does.Contain("state.Bench.RecordUnreachable(target.ObjectID, GameLoop.GameLoopTime)"));
            Assert.That(body, Does.Contain("AUTONOMOUS_DEFENSIVE_PULL_BENCHED"));

            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            int work = controller.IndexOf("private bool WorkCamp(");
            string camp = controller[work..controller.IndexOf("private GameNPC FindCampTarget(", work)];
            int giveUp = camp.IndexOf("AutonomousDefensivePull.ShouldGiveUpCamp(bot.Group, _camp.Id, out int benchedPulls)");
            Assert.That(giveUp, Is.GreaterThan(0).And.LessThan(camp.IndexOf("AutonomousDefensivePull.TryBegin(bot, target, out partyPullWhy)")));
            Assert.That(camp.IndexOf("AbandonCamp(bot, $\"{benchedPulls}", giveUp), Is.GreaterThan(giveUp),
                "the party gives the camp up (a new camp), it does not stand");
        }
    }
}
