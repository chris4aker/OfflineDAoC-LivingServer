using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Xanxicar's stomp (PC 1.2 test, 2026-11-07, 185 Albion raiders): the withdraw order came 3% above the threshold, the raid
    /// took him 83% -> 80% in 15 s, 158 raiders were still within 1,000 and 110 died in the stomp. Only raiders whose target
    /// was Xanxicar got the order. Now the window opens 10 s of the raid's damage ahead (at least 6%), any raider near him is
    /// sent out whatever it fights, and it is held there (its controller does not walk it back).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_XanxicarStompFix
    {
        [SetUp]
        public void Reset() => EpicEncounterTactics.ResetBlastForTests();

        [TearDown]
        public void Clean() => EpicEncounterTactics.ResetBlastForTests();

        [TestCase(0.0, 6)]
        [TestCase(0.2, 6)]
        [TestCase(1.0, 10)]
        [TestCase(1.5, 15)]
        public void TheWindowOpensTenSecondsOfDamageAhead(double percentPerSecond, int margin) =>
            Assert.That(EpicEncounterTactics.BlastMargin(percentPerSecond), Is.EqualTo(margin));

        [Test]
        public void AFastRaidWithdrawsEarlier()
        {
            // 185 raiders: 3% in 15 s is 0.2%/s at that moment, but bursts reach 1%/s and more.
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 60], 89, -1, percentPerSecond: 1.0), Is.True, "9% above at 1%/s");
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 60], 89, -1, percentPerSecond: 0.2), Is.False);
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 60], 86, -1), Is.True, "the 6% floor");
        }

        // duo6 (container Crystal Cave): raiders told to go stood at a median 763 from him and 71 died in one second; the
        // straight-out spot at 2,700 lies in the cave's rock. They walk out on the floor towards the way in instead.
        [Test]
        public void TheWithdrawSpotIsOnTheFloorPathTowardsTheWayIn()
        {
            Vector3 boss = new(31577, 35305, 14551);
            Vector3[] path = [boss, new(30500, 35500, 15000), new(29600, 33800, 15600), new(28900, 32600, 16000), new(28648, 31962, 16205)];
            Vector3? rally = EpicEncounterTactics.BlastRallyOnPath(path, boss, 2500);
            Assert.That(rally, Is.EqualTo(path[3]), "the first node 2,700 or more from him");
            Assert.That(EpicEncounterTactics.BlastRallyOnPath(path[..3], boss, 2500), Is.Null, "none clear: the old spot");
            Assert.That(EpicEncounterTactics.BlastRallyOnPath(null, boss, 2500), Is.Null);
        }

        // PC 2026-11-08 (the floor-path rally on the owner's copy): he followed the raid out to the path spot, 2,944 from his
        // spawn; at 40% the only clear path spots lay behind him and 54 raiders died walking there. Each raider now takes the
        // clear spot nearest to it, of the path's and of 16 floor directions around him.
        [Test]
        public void EachRaiderTakesTheClearSpotNearestToIt()
        {
            Vector3[] spots = [new(26000, 34000, 15900), new(31800, 35400, 14550), new(29100, 31500, 16000)];
            Assert.That(EpicEncounterTactics.NearestBlastSpot(spots, new(28800, 34300, 15850)), Is.EqualTo(spots[0]));
            Assert.That(EpicEncounterTactics.NearestBlastSpot(spots, new(29500, 34500, 15800)), Is.EqualTo(spots[1]).Or.EqualTo(spots[2]));
            Assert.That(EpicEncounterTactics.NearestBlastSpot([], new(0, 0, 0)), Is.Null);
        }

        // Raid-skill container run duo18: the stomp is centred on his target, so a spot 2,700 from him on the far side from
        // his target is inside the stomp. Only spots clear of the target too are used.
        [Test]
        public void SpotsMustBeClearOfHisTargetToo()
        {
            Vector3 boss = new(30000, 30000, 0), target = new(30300, 30000, 0);   // his target beside him, to the east
            Vector3[] spots = [new(27300, 30000, 0), new(32700, 30000, 0), new(30000, 32700, 0)];  // west, east, north
            Vector3[] clear = EpicEncounterTactics.ClearOf(spots, target, 2500);
            Assert.That(clear, Is.EqualTo(new[] { spots[0], spots[2] }),
                "west is 3,000 from the target and north about 2,717: clear; east is 2,400 from it: inside the stomp");
            Assert.That(EpicEncounterTactics.NearestBlastSpot(clear, new(31000, 30000, 0)), Is.EqualTo(spots[2]),
                "a raider east of him goes north, not east past his target's stomp");
            Assert.That(EpicEncounterTactics.ClearOf(null, target, 2500), Is.Empty);
        }

        [Test]
        public void ARaiderSentOutIsHeldThereUntilTheHoldEnds()
        {
            var bot = (GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            Vector3 spot = new(34277, 35305, 14551);
            Assert.That(EpicEncounterTactics.StandOffHold(bot, 0), Is.Null);
            EpicEncounterTactics.HoldStandOff(bot, spot, 5_000);
            Assert.That(EpicEncounterTactics.StandOffHold(bot, 4_000), Is.EqualTo(spot));
            Assert.That(EpicEncounterTactics.StandOffHold(bot, 5_001), Is.Null, "the hold is renewed while the blast window lasts");
        }
    }
}
