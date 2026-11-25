using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Port of OpenDAoC-Core c955cc64c: the game loop paces against absolute deadlines, so small overshoots no longer add up
    /// to drift, and a tick more than a whole tick late resets the schedule instead of bursting through the missed ticks.
    /// </summary>
    [TestFixture]
    public class UT_GameLoopTickPacer
    {
        private const long Tick = 1000;

        [Test]
        public void SmallOvershootsDoNotAccumulate()
        {
            long deadline = Tick;
            // Every tick ends 30% late; with a restarted stopwatch that was 30% of drift per tick.
            for (int i = 1; i <= 100; i++)
            {
                long finished = deadline + Tick * 3 / 10;
                deadline = GameLoopTickPacer.NextDeadline(deadline, finished, Tick);
            }

            Assert.That(deadline, Is.EqualTo(101 * Tick), "the 101st deadline is still on the original schedule");
        }

        [Test]
        public void ATickMoreThanOneTickLateResetsTheSchedule()
        {
            long deadline = 5 * Tick;
            long finished = deadline + 3 * Tick + 10;
            Assert.That(GameLoopTickPacer.NextDeadline(deadline, finished, Tick), Is.EqualTo(finished + Tick),
                "catch-up is capped at one tick: no burst of missed ticks");
        }

        [Test]
        public void AnOnTimeTickKeepsTheSchedule()
        {
            Assert.That(GameLoopTickPacer.NextDeadline(2 * Tick, 2 * Tick, Tick), Is.EqualTo(3 * Tick));
        }
    }
}
