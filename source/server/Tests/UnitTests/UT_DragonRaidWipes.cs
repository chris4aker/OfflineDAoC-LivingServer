using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 2026-10-30: a dragon raid that wipes regroups at its staging ring and calls the Epic Special Forces, as a dungeon
    /// raid has since 0.37-se.2 (before, the fallen trickled back to a dragon at full health until the four hours ran
    /// out, and "Dragon raids don't call them yet"). The rules run on plain facts (RealmRaidDragonFight.Raider and
    /// .Dragon), so these tests play whole fights without a game world.
    /// </summary>
    [TestFixture]
    public class UT_DragonRaidWipes
    {
        private const long Minute = 60_000;
        private static readonly RealmRaidDragonFight.Dragon Idle = new(false, true, false, 100, 100_000);
        private static readonly RealmRaidDragonFight.Dragon Fighting = new(true, true, false, 90, 90_000);

        private static RealmRaidDragonFight.Raider AtDragon(long id) => new(id, true, true, false);
        private static RealmRaidDragonFight.Raider Fallen(long id) => new(id, false, false, false);
        private static RealmRaidDragonFight.Raider Staged(long id) => new(id, true, false, true);
        private static RealmRaidDragonFight.Raider Away(long id) => new(id, true, false, false);
        private static long[] Roster(int size) => Enumerable.Range(1, size).Select(i => (long)i).ToArray();
        private static List<RealmRaidDragonFight.Raider> All(long[] roster, System.Func<long, RealmRaidDragonFight.Raider> shape) =>
            roster.Select(shape).ToList();

        [SetUp]
        public void Reset() => AutonomousEpicSpecialForces.ResetForTests();

        [Test]
        public void TheDragonRaidKeepsTheDungeonRaidsWipeAndRegroupRules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDragonFight.MaximumFailedAttempts, Is.EqualTo(RealmRaidDungeonRoute.MaximumFailedAttempts));
                Assert.That(RealmRaidDragonFight.RegroupMinimumMilliseconds, Is.EqualTo(5 * Minute));
                Assert.That(RealmRaidDragonFight.IsWipe(60, 30, 100, 45), Is.True, "half the attempt fell");
                Assert.That(RealmRaidDragonFight.IsWipe(60, 5, 100, 45), Is.True, "reset to full after real damage");
                Assert.That(RealmRaidDragonFight.IsWipe(60, 29, 60, 45), Is.False, "most standing and the dragon hurt: they broke off");
                Assert.That(RealmRaidDragonFight.IsWipe(7, 7, 100, 10), Is.False, "a handful at the dragon is not the raid");
                Assert.That(RealmRaidDragonFight.RegroupNeeded(80, 0), Is.EqualTo(56));
                Assert.That(RealmRaidDragonFight.RegroupNeeded(80, 20 * Minute), Is.EqualTo(32), "after 20 minutes 40% will do");
            });
        }

        // in combat, time since the raid went in, time since the dragon last took damage
        [TestCase(false, 3 * Minute - 1, 0L, false)]  // still crossing from the staging ring
        [TestCase(false, 3 * Minute, 0L, true)]       // out of the fight after the approach: judge the attempt
        [TestCase(true, 60 * Minute, 5 * Minute - 1, false)]
        [TestCase(true, 10_000L, 5 * Minute, true)]    // in the fight but untouched for five minutes: only a trickle is left on it
        public void AnAttemptEndsWhenTheDragonLeavesTheFightAfterTheApproachOrTakesNoDamageForFiveMinutes(
            bool inCombat, long attempt, long sinceProgress, bool over) =>
            Assert.That(RealmRaidDragonFight.AttemptOver(inCombat, attempt, sinceProgress), Is.EqualTo(over));

        [TestCase(28, 40, 5 * Minute, true, true)]
        [TestCase(28, 40, 5 * Minute - 1, true, false)]  // not rested
        [TestCase(27, 40, 5 * Minute, true, false)]      // under 70%
        [TestCase(16, 40, 20 * Minute, true, true)]      // 40% after 20 minutes
        [TestCase(40, 40, 10 * Minute, false, false)]    // the dragon is in the air: the first attempt waited for it to land too
        public void TheRegroupGoesInAgainRestedWithMostOfTheRosterAndTheDragonLanded(int staged, int roster, long regroup, bool landed, bool goes) =>
            Assert.That(RealmRaidDragonFight.GoesInAgain(staged, roster, regroup, landed), Is.EqualTo(goes));

        [Test]
        public void AWipeRegroupsTheRaidAtTheRingAndASecondWipeDefeatsIt()
        {
            var fight = new RealmRaidDragonFight("Golestandt");
            long[] roster = Roster(40);
            long now = 0;
            fight.Engage(roster, Idle, now);
            Assert.That(fight.Engaged, Is.EqualTo(40), "everyone at the ring is in the first attempt");

            // A minute later the dragon is in the fight with the raid around it.
            now += Minute;
            fight.Advance(All(roster, AtDragon), Fighting, now);
            // Ten minutes in: 25 of 40 fell, the dragon is at 60% and still fighting the rest. No verdict yet.
            now += 9 * Minute;
            var afterLosses = All(roster, id => id <= 25 ? Fallen(id) : AtDragon(id));
            fight.Advance(afterLosses, new(true, true, false, 60, 60_000), now);
            Assert.That(fight.Regrouping, Is.False, "the fight is still on");

            // The dragon leaves combat: half the attempt fell, so it was a wipe.
            now += 10_000;
            fight.Advance(afterLosses, new(false, true, false, 60, 60_000), now);
            Assert.Multiple(() =>
            {
                Assert.That(fight.Regrouping, Is.True);
                Assert.That(fight.FailedAttempts, Is.EqualTo(1));
                Assert.That(fight.Defeated, Is.False);
                Assert.That(fight.TryTakeWipe(out int failures), Is.True);
                Assert.That(failures, Is.EqualTo(1));
                Assert.That(fight.TryTakeWipe(out _), Is.False, "one reaction per wipe");
                Assert.That(fight.Status, Does.Contain("regrouping at the staging ring").And.Contain("attempt 2 of 2"));
            });

            // Four minutes later 30 of 40 stand at the ring: enough, but not rested.
            now += 4 * Minute;
            var gathered = All(roster, id => id <= 30 ? Staged(id) : Away(id));
            fight.Advance(gathered, Idle, now);
            Assert.That(fight.Regrouping, Is.True, "the survivors rest and rebuff for five minutes");
            Assert.That(fight.Status, Does.Contain("(30/28"));

            // At five minutes the raid goes in again, the thirty at the ring in the attempt.
            now += Minute + 10_000;
            fight.Advance(gathered, Idle, now);
            Assert.Multiple(() =>
            {
                Assert.That(fight.Regrouping, Is.False);
                Assert.That(fight.Engaged, Is.EqualTo(30));
                Assert.That(fight.Status, Is.Null, "the launcher shows the usual fight state again");
            });

            // The second attempt: 20 of the 30 fall and the dragon resets. Defeated.
            now += Minute;
            fight.Advance(All(roster, id => id <= 30 ? AtDragon(id) : Away(id)), new(true, true, false, 80, 80_000), now);
            now += 6 * Minute;
            fight.Advance(All(roster, id => id <= 20 ? Fallen(id) : id <= 30 ? AtDragon(id) : Away(id)), Idle, now);
            Assert.Multiple(() =>
            {
                Assert.That(fight.Defeated, Is.True);
                Assert.That(fight.DefeatedAt, Is.EqualTo("Golestandt"));
                Assert.That(fight.FailedAttempts, Is.EqualTo(2));
                Assert.That(fight.TryTakeWipe(out int failures), Is.True);
                Assert.That(failures, Is.EqualTo(2));
                Assert.That(fight.Regrouping, Is.False);
                Assert.That(fight.Status, Is.EqualTo("defeated at Golestandt"));
            });
        }

        [Test]
        public void ADragonResetToFullAfterRealDamageIsAWipeEvenWithFewFallen()
        {
            var fight = new RealmRaidDragonFight("Cuuldurach");
            long[] roster = Roster(60);
            fight.Engage(roster, Idle, 0);
            fight.Advance(All(roster, AtDragon), new(true, true, false, 45, 45_000), Minute);
            // Everybody who lived ran, the dragon went home and healed to full.
            fight.Advance(All(roster, id => id <= 5 ? Fallen(id) : Away(id)), Idle, 4 * Minute);
            Assert.That(fight.Regrouping, Is.True);
            Assert.That(fight.TryTakeWipe(out int failures) && failures == 1, Is.True);
        }

        [Test]
        public void ARaidThatBreaksOffWithMostStandingHasNotWipedAndFightsOn()
        {
            var fight = new RealmRaidDragonFight("Gjalpinulva");
            long[] roster = Roster(60);
            fight.Engage(roster, Idle, 0);
            fight.Advance(All(roster, AtDragon), new(true, true, false, 45, 45_000), Minute);
            fight.Advance(All(roster, id => id <= 29 ? Fallen(id) : Away(id)), new(false, true, false, 60, 60_000), 4 * Minute);
            Assert.Multiple(() =>
            {
                Assert.That(fight.Regrouping, Is.False);
                Assert.That(fight.FailedAttempts, Is.EqualTo(0));
                Assert.That(fight.TryTakeWipe(out _), Is.False);
                Assert.That(fight.Engaged, Is.EqualTo(0), "the attempt is over");
            });
            // The next pull is a new attempt, from the raiders around the dragon.
            fight.Advance(All(roster, id => id <= 29 ? Fallen(id) : AtDragon(id)), Fighting, 5 * Minute);
            Assert.That(fight.Engaged, Is.EqualTo(31));
        }

        [Test]
        public void TheApproachFromTheRingIsNotAWipeAndLateArrivalsJoinTheAttempt()
        {
            var fight = new RealmRaidDragonFight("Golestandt");
            long[] roster = Roster(40);
            fight.Engage(roster, Idle, 0);
            // Two minutes of walking with the dragon still asleep: nothing to judge.
            fight.Advance(All(roster, Away), Idle, 2 * Minute);
            Assert.That(fight.Engaged, Is.EqualTo(40));
            // Three minutes and still nobody reached it: the attempt is closed quietly, nobody fell.
            fight.Advance(All(roster, Away), Idle, 3 * Minute);
            Assert.That(fight.Engaged, Is.EqualTo(0));
            Assert.That(fight.FailedAttempts, Is.EqualTo(0));
            // The first ten pull it; the rest arrive during the fight and count too.
            fight.Advance(All(roster, id => id <= 10 ? AtDragon(id) : Away(id)), Fighting, 4 * Minute);
            Assert.That(fight.Engaged, Is.EqualTo(10));
            fight.Advance(All(roster, AtDragon), Fighting, 5 * Minute);
            Assert.That(fight.Engaged, Is.EqualTo(40));
        }

        [Test]
        public void ADragonKeptBusyByATrickleWhileTheRaidLiesDeadIsAWipeAfterFiveMinutesWithoutDamage()
        {
            var fight = new RealmRaidDragonFight("Golestandt");
            long[] roster = Roster(40);
            fight.Engage(roster, Idle, 0);
            fight.Advance(All(roster, AtDragon), new(true, true, false, 70, 70_000), Minute);
            // From the first minute on the dragon takes no damage: 30 are dead, a few keep running in.
            var trickle = All(roster, id => id <= 30 ? Fallen(id) : AtDragon(id));
            fight.Advance(trickle, new(true, true, false, 70, 70_000), 2 * Minute);
            fight.Advance(trickle, new(true, true, false, 70, 70_000), 5 * Minute + 59_000);
            Assert.That(fight.Regrouping, Is.False, "not five minutes since the last damage yet");
            fight.Advance(trickle, new(true, true, false, 70, 70_000), 6 * Minute);
            Assert.That(fight.Regrouping, Is.True);
        }

        [Test]
        public void LeadingADragonHomeFromOutsideItsLairIsNotAStall()
        {
            var fight = new RealmRaidDragonFight("Gjalpinulva");
            long[] roster = Roster(40);
            fight.Engage(roster, Idle, 0);
            fight.Advance(All(roster, AtDragon), new(true, true, false, 70, 70_000), Minute);
            // She takes no damage outside her lair; the raid is leading her back for ten minutes.
            var standing = All(roster, id => id <= 20 ? Fallen(id) : AtDragon(id));
            for (long now = 2 * Minute; now <= 12 * Minute; now += Minute)
                fight.Advance(standing, new(true, true, true, 70, 70_000), now);
            Assert.That(fight.Regrouping, Is.False, "no verdict while she is out of her lair");
            // Home again and still untouched for five minutes: now it is a wipe.
            for (long now = 13 * Minute; now <= 18 * Minute; now += Minute)
                fight.Advance(standing, new(true, true, false, 70, 70_000), now);
            Assert.That(fight.Regrouping, Is.True);
        }

        [Test]
        public void ADragonFightingSomebodyElseIsNotTheRaidsAttempt()
        {
            var fight = new RealmRaidDragonFight("Golestandt");
            long[] roster = Roster(40);
            for (long now = 0; now <= 20 * Minute; now += Minute)
                fight.Advance(All(roster, Away), new(true, true, false, 80, 80_000), now);
            Assert.Multiple(() =>
            {
                Assert.That(fight.Engaged, Is.EqualTo(0));
                Assert.That(fight.FailedAttempts, Is.EqualTo(0));
                Assert.That(fight.Regrouping, Is.False);
            });
        }

        [Test]
        public void TheRegroupWaitsForTheDragonToLand()
        {
            var fight = new RealmRaidDragonFight("Cuuldurach");
            long[] roster = Roster(40);
            fight.Engage(roster, Idle, 0);
            fight.Advance(All(roster, AtDragon), Fighting, Minute);
            fight.Advance(All(roster, id => id <= 30 ? Fallen(id) : Away(id)), Idle, 4 * Minute);
            Assert.That(fight.Regrouping, Is.True);
            var airborne = new RealmRaidDragonFight.Dragon(false, false, false, 100, 100_000);
            fight.Advance(All(roster, Staged), airborne, 10 * Minute);
            Assert.That(fight.Regrouping, Is.True);
            Assert.That(fight.Status, Does.Contain("dragon airborne"));
            fight.Advance(All(roster, Staged), Idle, 11 * Minute);
            Assert.That(fight.Regrouping, Is.False);
        }

        [Test]
        public void TheRealmHearsOfADragonWipeAndADragonDefeatWithoutADoubledName()
        {
            Assert.Multiple(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    string wipe = RealmEventBanter.RaidWipe("Golestandt", "Golestandt");
                    Assert.That(wipe, Does.Contain("Golestandt").And.Not.Contain("in Golestandt").And.Not.Contain("on Golestandt"));
                }
                Assert.That(RealmEventBanter.RaidWipe("Galladoria", "Conservator"), Does.Contain("Conservator"), "dungeon lines unchanged");
                Assert.That(GameWideAnnouncements.RaidDefeated(eRealm.Albion, "Golestandt", "Golestandt"),
                    Is.EqualTo("Albion's expedition has been driven off by Golestandt!"));
                Assert.That(GameWideAnnouncements.RaidDefeated(eRealm.Hibernia, "Conservator", "Galladoria"),
                    Is.EqualTo("Hibernia's expedition has been driven out of Galladoria by Conservator!"));
            });
        }

        [Test]
        public void ADragonWipeAsksForTheSpecialForcesSizedByTheDragonsOwnRecord()
        {
            AutonomousEpicSpecialForces.SetHistoryForTests("dragon-albion");
            AutonomousEpicSpecialForces.Request("dragon-albion", "Golestandt", eRealm.Albion, "a wipe at Golestandt");
            Assert.That(AutonomousEpicSpecialForces.PendingCalls, Is.EqualTo(1), "no record: the full guild answers");

            AutonomousEpicSpecialForces.SetHistoryForTests("dragon-hibernia", "Boss defeated", "Boss defeated", "Boss defeated");
            AutonomousEpicSpecialForces.Request("dragon-hibernia", "Cuuldurach", eRealm.Hibernia, "a wipe at Cuuldurach");
            Assert.That(AutonomousEpicSpecialForces.PendingCalls, Is.EqualTo(1), "a realm that keeps killing its dragon gets nobody");

            AutonomousEpicSpecialForces.SetHistoryForTests("dragon-midgard", "Boss defeated", "Defeated", "Timed out");
            Assert.That(AutonomousEpicSpecialForces.ComesFromStart("dragon-midgard"), Is.True, "two losses in a row at the dragon: from the start");
            Assert.That(AutonomousEpicSpecialForces.CallUpSize(new[] { "Boss defeated", "Defeated", "Timed out" }), Is.EqualTo(24));
        }

        [Test]
        public void EveryDragonRaidHasAMusterHubInTheDragonsRegionForTheSpecialForcesToArriveAt()
        {
            Assert.Multiple(() =>
            {
                foreach (var dragon in AutonomousRealmRaid.Definitions.Where(d => !d.IsDungeon))
                {
                    Assert.That(dragon.Id, Does.StartWith("dragon-"));
                    Assert.That(RealmRaidMuster.Hubs.Any(h => h.Event == dragon.Id && h.Region == dragon.Region), Is.True,
                        $"{dragon.Name}: the Special Forces arrive at the raid's hub and walk to the staging ring");
                }
            });
        }
    }
}
