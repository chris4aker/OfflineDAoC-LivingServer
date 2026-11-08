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
