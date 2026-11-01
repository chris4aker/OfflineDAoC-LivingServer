using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>The period 1.65 task rules (owner 2026-10-31; Camelot Warcry task guide).</summary>
    [TestFixture]
    public class UT_PeriodTasks
    {
        [TestCase(0, 1000, "South")]
        [TestCase(0, -1000, "North")]
        [TestCase(1000, 0, "East")]
        [TestCase(-1000, 0, "West")]
        [TestCase(1000, -1000, "North East")]
        [TestCase(-1000, 1000, "South West")]
        public void KillTaskNamesTheWayFromTheGuardToTheMonster(int dx, int dy, string expected)
        {
            // map Y grows to the south; the guard stands at the origin, the monster at (dx, dy)
            var guard = new Point2D(50_000, 50_000);
            Assert.That(KillTask.GetDirectionFromHeading(guard.GetHeading(50_000 + dx, 50_000 + dy)), Is.EqualTo(expected));
        }

        [Test]
        public void TasksStopAfterLevelNineteenAndALevelAllowsItsOwnNumber()
        {
            Assert.That(AbstractTask.MaxTaskLevel, Is.EqualTo(19));
            Assert.That(AbstractTask.MaxTasksDone(1), Is.EqualTo(1));
            Assert.That(AbstractTask.MaxTasksDone(12), Is.EqualTo(12));
            Assert.That(AbstractTask.MaxTasksDone(19), Is.EqualTo(19));
            Assert.That(AbstractTask.MaxTasksDone(20), Is.EqualTo(0));
        }
    }
}
