using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-10-19: epic encounters stay at full strength; a wipe brings a regroup and one more attempt, and the
    /// realm's Epic Special Forces (up to 24 extra level 50s, needed less as the realm wins) join the retry.
    /// </summary>
    [TestFixture]
    public class UT_EpicSpecialForces
    {
        [SetUp]
        public void Reset() => AutonomousEpicSpecialForces.ResetForTests();

        // engaged, fallen, health now, lowest health during the attempt
        [TestCase(60, 30, 100, 45, true)]   // half the raid fell
        [TestCase(60, 29, 60, 45, false)]   // most still standing, the encounter still hurt: they broke off
        [TestCase(60, 5, 100, 45, true)]    // it reset to full after being taken to 45%: nobody was left on it
        [TestCase(60, 5, 100, 90, false)]   // barely touched and back to full: not a real attempt
        [TestCase(95, 4, 95, 84, false)]    // play test 2026-11-07: a named trash monster nicked to 84% and left: no wipe
        [TestCase(124, 17, 100, 42, true)]  // play test 2026-11-07: Spindler Broodmother worn to 42% and lost: a wipe
        [TestCase(7, 7, 100, 10, false)]    // a handful who wandered in is not the raid
        [TestCase(8, 4, 50, 40, true)]
        public void AWipeIsHalfTheAttemptFallenOrAResetAfterRealDamage(int engaged, int fallen, int health, int lowest, bool wipe) =>
            Assert.That(RealmRaidDungeonRoute.IsWipe(engaged, fallen, health, lowest), Is.EqualTo(wipe));

        [TestCase(80, 0L, 56)]
        [TestCase(80, 19 * 60_000L, 56)]
        [TestCase(80, 20 * 60_000L, 32)]   // after 20 minutes 40% will do
        [TestCase(10, 0L, 8)]              // never fewer than eight
        [TestCase(300, 0L, 150)]           // never more than 150
        public void ARegroupWaitsForMostOfTheRoster(int roster, long milliseconds, int needed) =>
            Assert.That(RealmRaidDungeonRoute.RegroupNeeded(roster, milliseconds), Is.EqualTo(needed));

        [Test]
        public void ARegroupRestsAtLeastFiveMinutes() =>
            Assert.That(RealmRaidDungeonRoute.RegroupMinimumMilliseconds, Is.EqualTo(5 * 60_000L));

        // An out-of-time rally goes in with half of what it needs (at least 24), a full 200 raid too (2026-10-21).
        [TestCase(false, 103, 150, true)]   // Caer Sidi, 2026-10-20: 103 staged, about 60 more on the road
        [TestCase(false, 90, 132, true)]
        [TestCase(false, 74, 149, true)]    // under half: who came goes (2026-11-11)
        [TestCase(false, 29, 57, true)]     // half (2026-11-04: the minimum is three parties, 24, no longer 40)
        [TestCase(false, 23, 40, false)]    // half, but under three parties
        [TestCase(false, 28, 57, true)]     // the Hibernian dragon rally that failed: it goes now
        [TestCase(false, 170, 200, true)]   // Darkness Falls, 2026-10-21: a full raid, 170 staged at its deadline
        [TestCase(false, 99, 200, true)]    // under half of a full raid: it goes now
        [TestCase(true, 150, 150, false)]   // forced raids never go in short
        public void AnOutOfTimeRallyGoesInWithWhoCame(bool forced, int present, int required, bool goes) =>
            Assert.That(RealmRaidRecruitmentPolicy.GoesInShortAtDeadline(forced, present, required), Is.EqualTo(goes));

        // Defenders follow the attacking army's size when it marched or now, whichever is larger.
        [TestCase(25, 89, 0.75, 96, 67)]    // Dun Bolg, 2026-10-20: was capped at 25 against 89
        [TestCase(60, 40, 0.75, 96, 45)]    // the marched size still counts after losses
        [TestCase(10, 5, 0.75, 96, 8)]      // never fewer than 8
        [TestCase(10, 200, 1.0, 96, 96)]    // never more than the cap
        public void DefendersFollowTheLargerAttackingArmy(int marched, int now, double ratio, int cap, int defenders) =>
            Assert.That(RealmEventPolicy.DefendersFor(marched, now, ratio, cap), Is.EqualTo(defenders));

        [Test]
        public void TwoFailedAttemptsEndTheRaid() =>
            Assert.That(RealmRaidDungeonRoute.MaximumFailedAttempts, Is.EqualTo(2));

        [Test]
        public void TheCallUpShrinksWithEveryWinTheyHelpedAndGrowsWithEveryLoss()
        {
            // Owner 2026-11-04: "keep 24 and if they are called in on the same raid type where 24 made the second run win,
            // it's 16 now".
            RaidResult helped = new("Boss defeated", WithSpecialForces: true);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([]), Is.EqualTo(24), "no record: the full guild");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped]), Is.EqualTo(16));
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, helped]), Is.EqualTo(8));
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, helped, helped, helped]), Is.EqualTo(8), "they always answer a wipe");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize(["Boss defeated", "Boss defeated", "Boss defeated"]), Is.EqualTo(24),
                    "wins without them change nothing");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, helped, "Boss defeated", "Boss defeated"]), Is.EqualTo(8));
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, helped, "Defeated"]), Is.EqualTo(16), "a defeat adds a party back");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, helped, "Timed out"]), Is.EqualTo(16), "a time-out too");
                Assert.That(AutonomousEpicSpecialForces.CallUpSize(["Defeated", "Timed out", "Defeated"]), Is.EqualTo(24), "never more than 24");
                // A defeat they took part in counts as a defeat.
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, new RaidResult("Defeated", true)]), Is.EqualTo(24));
                // Rallies that never fought, blocked routes and player stops say nothing about the fight.
                Assert.That(AutonomousEpicSpecialForces.CallUpSize([helped, "Failed rally", "Route blocked", "Stopped by player",
                    new RaidResult("Interrupted", true)]), Is.EqualTo(16));
            });
        }

        [Test]
        public void TheyComeFromTheStartOnlyAfterTwoDefeatsInARow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousEpicSpecialForces.ComesFromStart(new[] { "Defeated", "Timed out" }), Is.True);
                Assert.That(AutonomousEpicSpecialForces.ComesFromStart(new[] { "Defeated", "Failed rally", "Defeated" }), Is.True);
                Assert.That(AutonomousEpicSpecialForces.ComesFromStart(new[] { "Defeated", "Boss defeated" }), Is.False);
                Assert.That(AutonomousEpicSpecialForces.ComesFromStart(new[] { "Defeated" }), Is.False);
                Assert.That(AutonomousEpicSpecialForces.ComesFromStart(Array.Empty<string>()), Is.False);
            });
        }

        [Test]
        public void ARaidIsAnsweredOnceAndAlwaysByAtLeastOneParty()
        {
            AutonomousEpicSpecialForces.SetHistoryForTests("epic-hibernia");
            AutonomousEpicSpecialForces.Request("epic-hibernia", "Galladoria", eRealm.Hibernia, "a wipe at Conservator");
            AutonomousEpicSpecialForces.Request("epic-hibernia", "Galladoria", eRealm.Hibernia, "a wipe at Conservator");
            Assert.That(AutonomousEpicSpecialForces.PendingCalls, Is.EqualTo(1));

            RaidResult helped = new("Boss defeated", WithSpecialForces: true);
            RealmRaidRecord.SetResultsForTests("epic-albion", helped, helped, helped);
            AutonomousEpicSpecialForces.Request("epic-albion", "Caer Sidi", eRealm.Albion, "a wipe at Apocalypse");
            Assert.That(AutonomousEpicSpecialForces.PendingCalls, Is.EqualTo(2), "three wins they helped: one party still comes");
        }

        [Test]
        public void AFinishedRaidJoinsTheRecordAndCanBeAnsweredAgainNextTime()
        {
            AutonomousEpicSpecialForces.SetHistoryForTests("epic-midgard", "Defeated");
            AutonomousEpicSpecialForces.Request("epic-midgard", "Tuscaran Glacier", eRealm.Midgard, "a wipe");
            AutonomousEpicSpecialForces.RaidEnded("epic-midgard", "Defeated");
            Assert.That(AutonomousEpicSpecialForces.ComesFromStart("epic-midgard"), Is.True, "two defeats in a row");
            AutonomousEpicSpecialForces.Request("epic-midgard", "Tuscaran Glacier", eRealm.Midgard, "start");
            Assert.That(AutonomousEpicSpecialForces.PendingCalls, Is.EqualTo(2));
        }

        [Test]
        public void EachRealmsGuildPartyIsAFullExpeditionParty()
        {
            Assert.Multiple(() =>
            {
                foreach (var (realm, classes) in AutonomousEpicSpecialForces.PartyClasses)
                {
                    Assert.That(classes, Has.Length.EqualTo(AutonomousEpicSpecialForces.PartySize), realm.ToString());
                    Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(classes[0], classes.Skip(1).ToArray()), Is.EqualTo(8), realm.ToString());
                    foreach (eCharacterClass characterClass in classes)
                        Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(realm), Does.Contain(characterClass), $"{realm} {characterClass}");
                }
            });
        }

        [Test]
        public void TheirIdsNeverMeetARosterId() =>
            Assert.That(AutonomousEpicSpecialForces.FirstBotId, Is.GreaterThan(1_000_000_000L).And.LessThan(int.MaxValue));

        [Test]
        public void TheLedgerGivesADungeonsFinishedOutcomesOldestFirst()
        {
            string path = TemporarySqlite.NewDatabasePath("ledger");
            try
            {
                Assert.That(RealmEventRecordStore.Outcomes(path, "epic-hibernia", 6), Is.Empty, "no ledger yet");
                RealmEventRecordStore.Save(path, new[]
                {
                    new RealmEventRecord("a", "epic-hibernia", "Galladoria", "Raid", "Hibernia", "2026-10-18T10:00:00Z", "2026-10-18T12:00:00Z", "Ended", "Timed out", "", 60, 50),
                    new RealmEventRecord("b", "epic-hibernia", "Galladoria", "Raid", "Hibernia", "2026-10-19T10:00:00Z", "2026-10-19T12:00:00Z", "Ended", "Defeated", "", 60, 50),
                    new RealmEventRecord("c", "epic-hibernia", "Galladoria", "Raid", "Hibernia", "2026-10-19T15:00:00Z", "", "Battle", "In progress", "", 60, 50),
                    new RealmEventRecord("d", "epic-albion", "Caer Sidi", "Raid", "Albion", "2026-10-19T11:00:00Z", "2026-10-19T13:00:00Z", "Ended", "Boss defeated", "", 60, 50),
                });
                Assert.That(RealmEventRecordStore.Outcomes(path, "epic-hibernia", 6), Is.EqualTo(new[] { "Timed out", "Defeated" }));
                Assert.That(RealmEventRecordStore.Outcomes(path, "epic-hibernia", 1), Is.EqualTo(new[] { "Defeated" }));
            }
            finally
            {
                TemporarySqlite.DeleteDatabase(path);
            }
        }

        [Test]
        public void TheRealmHearsOfADefeatAndOfAWipe()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmEventBanter.RaidOutcome("Galladoria", "Defeated"), Does.Contain("Galladoria"));
                Assert.That(RealmEventBanter.RaidWipe("Galladoria", "Conservator"), Does.Contain("Conservator"));
                Assert.That(GameWideAnnouncements.RaidDefeated(eRealm.Hibernia, "Conservator", "Galladoria"),
                    Is.EqualTo("Hibernia's expedition has been driven out of Galladoria by Conservator!"));
            });
        }
    }
}
