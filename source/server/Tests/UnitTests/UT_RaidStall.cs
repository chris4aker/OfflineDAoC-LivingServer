using DOL.GS;
using NUnit.Framework;
using static DOL.GS.RealmRaidRecruitmentPolicy;

namespace DOL.UnitTests
{
    /// <summary>
    /// A raid in battle has no time limit (owner 2026-11-13): Caer Sidi was cut off by the four-hour window with 12 named
    /// down and still moving. It ends when it stalls: an hour with no named or boss kill, no wipe and no new low on its target.
    /// </summary>
    [TestFixture]
    public class UT_RaidStall
    {
        private const long Hour = 60 * 60_000L;

        [Test]
        public void AnHourWithNothingStallsTheRaid()
        {
            Assert.That(Stalled(false, 10 * Hour, 9 * Hour), Is.True);
            Assert.That(Stalled(false, 10 * Hour - 1, 9 * Hour), Is.False);
            Assert.That(Stalled(true, 50 * Hour, 0), Is.False, "a raid forced from the launcher runs until it ends");
        }

        [Test]
        public void AFiveHourRaidThatKeepsGoingIsNotStopped()
        {
            long last = 0;
            for (long now = 0; now <= 5 * Hour; now += 30 * 60_000L)
            {
                // A named down every half hour.
                Assert.That(Stalled(false, now, last), Is.False, $"at {now / 60_000} minutes");
                last = now;
            }
        }

        [Test]
        public void ProgressIsANamedAWipeOrANewLow()
        {
            Assert.That(MadeProgress(3, 4, 0, 0, "Warlord", "Warlord", 80, 80), Is.True, "a named down");
            Assert.That(MadeProgress(3, 3, 0, 1, "Warlord", "Warlord", 80, 100), Is.True, "a wipe: the fight goes on");
            Assert.That(MadeProgress(3, 3, 1, 1, "Warlord", "Warlord", 80, 72), Is.True, "the boss lower than ever");
            Assert.That(MadeProgress(3, 3, 1, 1, "Warlord", "Warlord", 72, 90), Is.False, "the boss healed: no progress");
            Assert.That(MadeProgress(3, 3, 1, 1, "Warlord", "Vortex", 72, 50), Is.False, "a new target starts its own low");
            Assert.That(MadeProgress(3, 3, 1, 1, null, null, 101, 101), Is.False, "clearing trash only");
        }
    }
}
