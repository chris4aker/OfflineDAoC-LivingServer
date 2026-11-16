using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tur Suil, owner's PC, 2026-11-16 (build 88b6ab3, /epictest sweep): the raid started at 10:47:05 and cleared trash for its
    /// first hour (114 kills, no raider lost), then the 60-minute stall clock, started at the same moment, ended it at 11:47:56
    /// with no named targeted. Owner's decision (2026-11-16): the first-hour trash clear counts as progress, so the stall clock
    /// starts when it ends. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidStallAfterTrash
    {
        private const long RouteStart = 5_000_000;

        [Test]
        public void TheFirstHourOfAnEpicDungeonRouteIsTheTrashClear()
        {
            Assert.That(RealmRaidDungeonRoute.InTrashPhase(RouteStart, RouteStart, hasObjectives: false), Is.True);
            Assert.That(RealmRaidDungeonRoute.InTrashPhase(RouteStart + 59 * 60_000, RouteStart, false), Is.True);
            Assert.That(RealmRaidDungeonRoute.InTrashPhase(RouteStart + RealmRaidDungeonRoute.EpicTrashPhaseMilliseconds, RouteStart, false), Is.False);
        }

        [Test]
        public void NeutralRaidsAndRoutesNotYetBegunHaveNoTrashClear()
        {
            // Summoner's Hall and Darkness Falls (neutral objectives) never run the clear.
            Assert.That(RealmRaidDungeonRoute.InTrashPhase(RouteStart + 60_000, RouteStart, hasObjectives: true), Is.False);
            // A route that hasn't begun (start tick 0) isn't clearing anything.
            Assert.That(RealmRaidDungeonRoute.InTrashPhase(60_000, 0, false), Is.False);
        }

        [Test]
        public void TheStallClockRunsFromTheEndOfTheClear()
        {
            // Tur Suil's shape: the clear ran its full hour with no named progress. With the clear counted, the last
            // progress is the clear's end, so the raid is not stalled at 61 minutes and is stalled only 60 minutes later.
            long clearEnd = RouteStart + RealmRaidDungeonRoute.EpicTrashPhaseMilliseconds;
            Assert.That(RealmRaidRecruitmentPolicy.Stalled(false, RouteStart + 61 * 60_000, clearEnd - 1), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.Stalled(false, clearEnd - 1 + RealmRaidRecruitmentPolicy.StalledMilliseconds + 1, clearEnd - 1), Is.True);
        }
    }
}
