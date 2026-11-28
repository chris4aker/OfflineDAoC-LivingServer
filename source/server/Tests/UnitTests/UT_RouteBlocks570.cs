using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 2b: shared route blocks (3 solo gamebots giving a camp up at one spot make it a block for 30 minutes) and
    /// near-miss dead ends (a stall on a cliff edge just above the walk target teaches the spot; later walks there take the
    /// zone roads). Pure rules; tests only until a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RouteBlocks570
    {
        private const long Minute = 60_000;

        [SetUp]
        public void Reset()
        {
            AutonomousRouteBlockMemory.Clear();
            AutonomousNearMissDeadEnds.Clear();
        }

        [TearDown]
        public void Clean()
        {
            AutonomousRouteBlockMemory.Clear();
            AutonomousNearMissDeadEnds.Clear();
        }

        // ---- route-block memory ----

        [Test]
        public void TwoBotsMakeNoBlockTheThirdDoes()
        {
            Assert.That(AutonomousRouteBlockMemory.AnyActive(0), Is.False);
            Assert.That(AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, 1, 10, 1_000, out _, out _), Is.Null);
            Assert.That(AutonomousRouteBlockMemory.RecordGiveUp(200, 345_300, 505_000, 2, 11, 2_000, out _, out _), Is.Null);
            var block = AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_400, 3, 9, 3_000, out bool created, out int bots);
            Assert.That(block, Is.Not.Null);
            Assert.That(created, Is.True);
            Assert.That(bots, Is.EqualTo(3));
            Assert.That(block.MaxBotLevel, Is.EqualTo(11), "the highest level that gave up");
            Assert.That(AutonomousRouteBlockMemory.AnyActive(4_000), Is.True);
        }

        [Test]
        public void TheSameBotThreeTimesMakesNoBlock()
        {
            for (int i = 0; i < 3; i++)
                Assert.That(AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, 7, 10, 1_000 + i, out _, out _), Is.Null);
        }

        [Test]
        public void BotsTooFarApartMakeNoBlock()
        {
            for (int bot = 0; bot < 3; bot++)
                Assert.That(AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000 + bot * 1_000, 505_000, bot, 10, 1_000, out _, out _), Is.Null);
        }

        [Test]
        public void AGiveUpOlderThanThirtyMinutesDoesNotCount()
        {
            AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, 1, 10, 0, out _, out _);
            AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, 2, 10, 20 * Minute, out _, out _);
            Assert.That(AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, 3, 10, 31 * Minute, out _, out _), Is.Null);
        }

        [Test]
        public void ABlockExpiresIsLevelGatedAndARenewalRaisesTheLevel()
        {
            for (int bot = 1; bot <= 3; bot++)
                AutonomousRouteBlockMemory.RecordGiveUp(200, 345_000, 505_000, bot, 10, 1_000, out _, out _);
            long until = 1_000 + AutonomousRouteBlockMemory.ActiveMilliseconds;
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 200, 2_000), Has.Length.EqualTo(1));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(5, 200, 2_000), Has.Length.EqualTo(1));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(11, 200, 2_000), Is.Empty, "a bot above every level that gave up");
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 1, 2_000), Is.Empty, "another region");
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 200, until), Is.Empty, "after 30 minutes");
            var renewed = AutonomousRouteBlockMemory.RecordGiveUp(200, 345_100, 505_000, 4, 13, 5_000, out bool created, out _);
            Assert.That(created, Is.False);
            Assert.That(renewed.MaxBotLevel, Is.EqualTo(13));
            Assert.That(renewed.Until, Is.EqualTo(5_000 + AutonomousRouteBlockMemory.ActiveMilliseconds));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(13, 200, 6_000), Has.Length.EqualTo(1));
        }

        [Test]
        public void BlocksPerRegionAreCapped()
        {
            for (int spot = 0; spot < AutonomousRouteBlockMemory.MaxBlocksPerRegion + 5; spot++)
            for (int bot = 0; bot < 3; bot++)
                AutonomousRouteBlockMemory.RecordGiveUp(200, 100_000 + spot * 5_000, 505_000, spot * 10 + bot, 10, 1_000 + spot, out _, out _);
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(1, 200, 2_000).Length, Is.EqualTo(AutonomousRouteBlockMemory.MaxBlocksPerRegion));
        }

        private static readonly AutonomousRouteBlockMemory.Block Block = new(200, 345_000, 505_000, 12, long.MaxValue);

        [Test]
        public void ACampNearTheBlockIsBlocked() =>
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_000, 560_000, 200, 345_000, 506_000), Is.True);

        [Test]
        public void AWayPassingAt500IsBlockedAt1000IsNot()
        {
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 340_000, 505_500, 200, 350_000, 505_500), Is.True);
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 340_000, 506_000, 200, 350_000, 506_000), Is.False);
        }

        [Test]
        public void ABotAlreadyAtTheBlockOnlyChecksTheDestination()
        {
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_200, 505_000, 200, 350_000, 505_000), Is.False);
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_200, 505_000, 200, 345_500, 505_500), Is.True);
        }

        [Test]
        public void AnotherRegionIsNeverBlocked()
        {
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 1, 345_000, 505_000, 1, 345_000, 505_000), Is.False);
            // from another region only the destination counts (no straight way across regions)
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 1, 340_000, 505_000, 200, 350_000, 505_000), Is.False);
        }

        [Test]
        public void ThePickerDropsBlockedCampsOnlyWhileAnUnblockedOneIsLeft()
        {
            var camps = new List<string> { "near-block", "past-block", "safe" };
            bool Blocked(string id) => id != "safe";
            List<string> kept = AutonomousRouteBlockMemory.KeepUnblocked(camps, Blocked, out int skipped);
            Assert.That(kept, Is.EqualTo(new[] { "safe" }));
            Assert.That(skipped, Is.EqualTo(2));

            var allBlocked = new List<string> { "near-block", "past-block" };
            Assert.That(AutonomousRouteBlockMemory.KeepUnblocked(allBlocked, Blocked, out skipped), Is.SameAs(allBlocked),
                "never left without a camp");
            Assert.That(skipped, Is.Zero);

            var single = new List<string> { "near-block" };
            Assert.That(AutonomousRouteBlockMemory.KeepUnblocked(single, Blocked, out skipped), Is.SameAs(single));
            var none = new List<string> { "safe", "safe" };
            Assert.That(AutonomousRouteBlockMemory.KeepUnblocked(none, Blocked, out skipped), Is.SameAs(none));
            Assert.That(skipped, Is.Zero);
        }

        [Test]
        public void ParallelUseDoesNotThrow()
        {
            Assert.DoesNotThrow(() => Parallel.For(0, 2_000, i =>
            {
                AutonomousRouteBlockMemory.RecordGiveUp((ushort)(i % 3), 345_000 + i % 7 * 100, 505_000, i % 50, 10, i, out _, out _);
                AutonomousRouteBlockMemory.ActiveFor(10, (ushort)(i % 3), i);
                AutonomousRouteBlockMemory.AnyActive(i);
            }));
        }

        // ---- near-miss dead ends ----

        // Black Mtns. South: the crossing into Camelot Hills on the low hillside, and the cliff edge 51 units above it
        private static readonly Vector3 Crossing = new(548_800, 495_328, 3_260);
        private static readonly Vector3 CliffEdge = new(548_762, 495_343, 3_290);

        [Test]
        public void TheCliffEdgeAboveTheCrossingIsANearMissStall()
        {
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound, CliffEdge, Crossing, true), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound, CliffEdge, Crossing, false), Is.False,
                "another zone");
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PathFound, CliffEdge, Crossing, true), Is.False,
                "a full path");
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.NoPathFound, CliffEdge, Crossing, true), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound,
                new Vector3(314_846, 471_535, 5_263), new Vector3(317_100, 470_673, 6_417), true), Is.False, "far from the target");
        }

        [Test]
        public void ASpotIsLearnedOnceRenewedAndForgottenAfterSixHours()
        {
            long now = 1_000_000;
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, now), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing, now), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing + new Vector3(20, -10, 0), now + 1_000), Is.False, "same crossing");
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing + new Vector3(30, 30, 0), now), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing + new Vector3(200, 0, 0), now), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(0, Crossing, now), Is.False, "another zone");
            long renewed = now + 1_000 + AutonomousNearMissDeadEnds.MemoryMilliseconds;
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, renewed - 1), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, renewed), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing, renewed), Is.True, "learned again after expiry");
        }

        [Test]
        public void TheMemoryIsBounded()
        {
            for (int i = 0; i < AutonomousNearMissDeadEnds.Capacity; i++)
                Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(i * 1_000, 0, 0), 0), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(-5_000, 0, 0), 0), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, new Vector3(-5_000, 0, 0), 0), Is.False);
            // expired spots free their room
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(-5_000, 0, 0), AutonomousNearMissDeadEnds.MemoryMilliseconds), Is.True);
        }

        [Test]
        public void AKnownNearMissAsksTheRoadsOnlyForGamebots()
        {
            long now = 5_000;
            Assert.That(AutonomousNearMissDeadEnds.KeepsNearMiss(true, true, 2, Crossing, now), Is.True, "not learned yet");
            AutonomousNearMissDeadEnds.Remember(2, Crossing, now);
            Assert.That(AutonomousNearMissDeadEnds.KeepsNearMiss(true, true, 2, Crossing, now), Is.False, "a gamebot asks the roads");
            Assert.That(AutonomousNearMissDeadEnds.KeepsNearMiss(true, false, 2, Crossing, now), Is.True,
                "companions and other walkers keep today's shortcut");
            Assert.That(AutonomousNearMissDeadEnds.KeepsNearMiss(false, true, 2, Crossing, now), Is.False, "not a near miss");
            Assert.That(AutonomousNearMissDeadEnds.KeepsNearMiss(true, true, 2, Crossing + new Vector3(500, 0, 0), now), Is.True,
                "another target keeps the shortcut");
        }

        [Test]
        public void AnUnknownNearMissKeepsItsPartialPathWithoutAskingTheRoads()
        {
            var zone = new Zone(null, 2, "zone2", 483_328, 434_176, 65_536, 65_536, 2, false, 0, false, 0, 0, 0, 0, 0);
            var nodes = new WrappedPathfindingNode[8];
            nodes[0] = new WrappedPathfindingNode(new Vector3(516_852, 497_979, 3_370), default);
            nodes[1] = new WrappedPathfindingNode(CliffEdge, default);
            var direct = new PathfindingResult(PathfindingStatus.PartialPathFound, 2);
            // nav null: had the roads been asked they would also say no, so only the gate is under test here
            foreach (bool gamebot in new[] { false, true })
            {
                Assert.That(Pathfinder.TryRoadPath(null, zone, nodes[0].Position, Crossing, null, direct, nodes, nodes.Length,
                    out var result, nearMissMemory: gamebot), Is.False);
                Assert.That(result.Status, Is.EqualTo(PathfindingStatus.PartialPathFound));
                Assert.That(result.NodeCount, Is.EqualTo(2));
            }
        }
    }
}
