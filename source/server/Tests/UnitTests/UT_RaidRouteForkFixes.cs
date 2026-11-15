using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Raid route fixes from rambodolphin13/DividedRealms (Living Server 1.2, tests only): the next-encounter search has a
    /// time budget per pass and resumes from the same candidate and anchor (an overnight 4,500-bot run stalled a tick for
    /// 7.7 s), and an entrance through an approach dungeon needs a proven walk (146 raiders held 72 minutes in Hall of the
    /// Corrupt).
    /// </summary>
    [TestFixture]
    public class UT_RaidRouteForkFixes
    {
        [Test]
        public void TheSearchStopsWhenItsTimeIsSpent()
        {
            Assert.That(RealmRaidDungeonRoute.ProbeBudgetMilliseconds, Is.EqualTo(100));
            Assert.That(RealmRaidDungeonRoute.ProbeBudgetSpent(99.9), Is.False);
            Assert.That(RealmRaidDungeonRoute.ProbeBudgetSpent(100), Is.True);
            Assert.That(RealmRaidDungeonRoute.ProbeBudgetSpent(7_700), Is.True, "the overnight stall");
        }

        [Test]
        public void TheSameCandidateResumesFromItsNextAnchor()
        {
            Assert.That(RealmRaidDungeonRoute.FirstProbeAnchor(sameCandidate: true, resumeAnchor: 3, anchors: 6), Is.EqualTo(3));
            Assert.That(RealmRaidDungeonRoute.FirstProbeAnchor(sameCandidate: false, resumeAnchor: 3, anchors: 6), Is.Zero,
                "another candidate starts at the first anchor");
            Assert.That(RealmRaidDungeonRoute.FirstProbeAnchor(true, 9, 6), Is.EqualTo(6), "never past the anchors (the loop then ends)");
            Assert.That(RealmRaidDungeonRoute.FirstProbeAnchor(true, -2, 6), Is.Zero);
        }
    }
}
