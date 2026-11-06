using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Overnight run 2026-11-04/05 (build 616190a): the Marfach Caverns raid timed out after 91 pulls on an Ancient Black
    /// Oak that never entered combat, and the night's sieges were logged without the army they could draw on
    /// (docs/NIGHT-SIEGES-AND-MARFACH.md).
    /// </summary>
    [TestFixture]
    public class UT_MarfachAndNightSieges
    {
        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(91, true)]
        public void ThreePullsThatReachNobodySetTheEncounterAside(int pulls, bool setAside) =>
            Assert.That(RealmRaidDungeonRoute.PullsNeverReached(pulls), Is.EqualTo(setAside));

        [Test]
        public void TheMarfachLoopEndsOnTheThirdEmptyPull()
        {
            // The run: every pull went out, waited the two-minute grace, and ended with the Oak out of combat and no wipe.
            var pulls = new RealmRaidDungeonRoute.PullContact();
            int setAsideAt = 0;
            for (int pull = 1; pull <= 91 && setAsideAt == 0; pull++)
            {
                pulls.Begin();
                if (pulls.Ended(wiped: false)) setAsideAt = pull;
            }
            Assert.That(setAsideAt, Is.EqualTo(RealmRaidDungeonRoute.MaximumPullsWithoutContact));
        }

        [Test]
        public void APullThatReachesTheEncounterStartsTheCountAgain() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(2));
            // The third pull gets a fight going and the raid breaks off: an ordinary attempt, not an unreachable encounter.
            pulls.Begin();
            pulls.SawCombat();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False, "one empty pull after a fight is not three in a row");
        });

        [Test]
        public void AWipeIsAFailedAttemptNotAnUnreachableEncounter() => Assert.Multiple(() =>
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            for (int i = 0; i < 5; i++)
            {
                pulls.Begin();
                Assert.That(pulls.Ended(wiped: true), Is.False);
            }
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(0));
        });

        [Test]
        public void ANewEncounterStartsWithNoEmptyPulls()
        {
            var pulls = new RealmRaidDungeonRoute.PullContact();
            pulls.Begin(); pulls.Ended(false);
            pulls.Begin(); pulls.Ended(false);
            pulls.Reset();
            pulls.Begin();
            Assert.That(pulls.Ended(false), Is.False);
            Assert.That(pulls.PullsWithoutContact, Is.EqualTo(1));
        }

        private static long At(string hhmmss) => (long)TimeSpan.Parse(hhmmss).TotalMilliseconds;

        [Test]
        public void KrondonsBeranMusterIsSetAsideWhenItStopsGaining()
        {
            // Build 8d7baa3, 2026-11-05: REALM_EXPEDITION_FINAL_MUSTER for Beran the Supply Master (needed 50), with the raid
            // turning on Scurceol Hyrde from 14:42:32 to 14:48:07 in between (the muster clock restarted at each return).
            var muster = new RealmRaidDungeonRoute.MusterProgress();
            (string At, int Mustered)[] samples =
            [
                ("14:27:55", 0), ("14:29:56", 2), ("14:31:57", 2), ("14:33:58", 2), ("14:35:59", 2), ("14:38:00", 2),
                ("14:40:01", 3), ("14:42:02", 5),
                ("14:48:07", 2), ("14:50:08", 2), ("14:52:09", 2), ("14:54:10", 2), ("14:56:12", 2), ("14:58:14", 2),
                ("15:00:15", 2), ("15:02:16", 2),
            ];
            string stalledAt = null;
            foreach (var (time, mustered) in samples)
                if (muster.Observe(mustered, 50, At(time))) { stalledAt = time; break; }
            Assert.Multiple(() =>
            {
                Assert.That(stalledAt, Is.EqualTo("15:02:16"), "20 minutes after its best (5 at 14:42), not never");
                Assert.That(muster.Best, Is.EqualTo(5));
            });
        }

        [Test]
        public void AMusterThatKeepsGainingOrIsFullIsNeverStalled() => Assert.Multiple(() =>
        {
            var slow = new RealmRaidDungeonRoute.MusterProgress();
            // A long walk in: one more raider every five minutes for an hour.
            bool stalled = false;
            for (int minute = 0, count = 1; minute <= 60; minute += 5, count++)
                stalled |= slow.Observe(count, 50, minute * 60_000L);
            Assert.That(stalled, Is.False, "still gaining");
            var full = new RealmRaidDungeonRoute.MusterProgress();
            full.Observe(60, 50, 0);
            Assert.That(full.Observe(55, 50, 30 * 60_000L), Is.False, "enough there: the raid goes in, it is not stalled");
            var empty = new RealmRaidDungeonRoute.MusterProgress();
            Assert.That(empty.Observe(0, 50, 0), Is.False);
            Assert.That(empty.Observe(0, 50, RealmRaidDungeonRoute.MusterStalledMilliseconds - 1), Is.False);
            Assert.That(empty.Observe(0, 50, RealmRaidDungeonRoute.MusterStalledMilliseconds), Is.True,
                "an empty muster stalls too (MUSTER_UNREACHABLE usually takes it first)");
        });

        // Owner, 2026-11-05: a time-out with no wipe is neither a win nor a loss; after a wipe it is still a loss.
        [TestCase("Timed out", 0, false)]
        [TestCase("Timed out", 1, true)]
        [TestCase("Timed out", 2, true)]
        [TestCase("Defeated", 2, true)]
        [TestCase("Boss defeated", 0, true)]
        public void ATimeOutWithoutAWipeIsNoResult(string outcome, int wipes, bool counted) => Assert.Multiple(() =>
        {
            string recorded = AutonomousEpicSpecialForces.RecordOutcome(outcome, wipes);
            Assert.That(RealmRaidRecord.Counted(recorded), Is.EqualTo(counted));
            if (counted) Assert.That(recorded, Is.EqualTo(outcome), "a counted result keeps its name");
        });

        [Test]
        public void ATimeOutWithoutAWipeMovesNeitherTheSizeNorTheSpecialForces()
        {
            AutonomousEpicSpecialForces.ResetForTests();
            RealmRaidRecord.ResetForTests();
            try
            {
                const string id = "dungeon-hibernia-marfach";
                AutonomousEpicSpecialForces.RaidEnded(id, "Boss defeated", sizePercent: 100);
                var before = RealmRaidRecord.GetResults(id);
                AutonomousEpicSpecialForces.RaidEnded(id, AutonomousEpicSpecialForces.RecordOutcome("Timed out", 0), sizePercent: 90);
                var rules = RealmRaidRecruitmentPolicy.SizingRules.Default;
                Assert.Multiple(() =>
                {
                    Assert.That(RealmRaidRecord.GetResults(id), Is.EqualTo(before), "not added to the record");
                    Assert.That(RealmRaidRecruitmentPolicy.Size(64, RealmRaidRecord.GetResults(id), rules).Percent,
                        Is.EqualTo(RealmRaidRecruitmentPolicy.Size(64, before, rules).Percent), "the size stays where it was");
                    Assert.That(AutonomousEpicSpecialForces.CallUpParties(RealmRaidRecord.GetResults(id)),
                        Is.EqualTo(AutonomousEpicSpecialForces.CallUpParties(before)), "the Special Forces call-up too");
                });
                AutonomousEpicSpecialForces.RaidEnded(id, AutonomousEpicSpecialForces.RecordOutcome("Timed out", 1));
                Assert.That(RealmRaidRecord.Get(id), Is.EqualTo(new[] { "Boss defeated", "Timed out" }), "a time-out after a wipe is a loss");
            }
            finally
            {
                AutonomousEpicSpecialForces.ResetForTests();
                RealmRaidRecord.ResetForTests();
            }
        }

        [Test]
        public void TheLedgerSaysHowManyWipesAndARestartReadsItTheSameWay()
        {
            Assert.Multiple(() =>
            {
                string details = RealmEventRecordStore.WithWipes("The four-hour expedition window ended.", 0);
                Assert.That(details, Is.EqualTo("The four-hour expedition window ended. Wipes: 0."));
                Assert.That(RealmEventRecordStore.WipesIn(details), Is.EqualTo(0));
                Assert.That(RealmEventRecordStore.WipesIn(RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithSpecialForces(
                    RealmEventRecordStore.WithWipes("x", 2), 24), 110.2)), Is.EqualTo(2), "read past the other markers");
                Assert.That(RealmEventRecordStore.WipesIn("an older row"), Is.Null);
                Assert.That(RealmEventRecordStore.WipesIn(null), Is.Null);
            });
            string folder = Path.Combine(Path.GetTempPath(), "ut-wipes-ledger-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "dungeon-hibernia-marfach", "Marfach Caverns", "Epic dungeon", "Hibernia",
                        $"2026-11-05T10:{n:00}:00.0000000Z", $"2026-11-05T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 77, 64);
                RealmEventRecordStore.Save(path, [
                    Row("Timed out", "The four-hour expedition window ended."),                                       // older row: a loss, as before
                    Row("Timed out", RealmEventRecordStore.WithWipes("The four-hour expedition window ended.", 0)),     // no result
                    Row("Timed out", RealmEventRecordStore.WithWipes("The four-hour expedition window ended.", 1)),     // a loss
                    Row("Boss defeated", RealmEventRecordStore.WithWipes("The final dungeon encounter has been defeated.", 0)),
                ]);
                var rows = RealmEventRecordStore.Results(path, "dungeon-hibernia-marfach", 10, automaticOnly: true);
                Assert.That(RealmRaidRecord.FromLedger(rows).Select(r => r.Outcome),
                    Is.EqualTo(new[] { "Timed out", "Timed out", "Boss defeated" }));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void TheSiegePoolCountsTheAttackersLevelFiftiesAndTheirRvrDuty()
        {
            var bots = new (eRealm, int, bool)[]
            {
                (eRealm.Albion, 50, true), (eRealm.Albion, 50, false), (eRealm.Albion, 49, true),
                (eRealm.Midgard, 50, true), (eRealm.Albion, 50, true),
            };
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Albion), Is.EqualTo((3, 2)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(bots, eRealm.Hibernia), Is.EqualTo((0, 0)));
            Assert.That(AutonomousRvrEventLayer.SiegePool(null, eRealm.Albion), Is.EqualTo((0, 0)));
        }

        [Test]
        public void TheMarchLineNamesTheArmyAndItsPool() => Assert.Multiple(() =>
        {
            // Dun Bolg, 21:49: a keep siege that marched with 9 of the 64 its muster waited for.
            Assert.That(AutonomousRvrEventLayer.SiegeArmyFields("keep-siege", 9, 64, 72, 594, 180, 16, 0.5),
                Is.EqualTo("kind=keep-siege needed=64 thin=true assigned=72 realmLevel50=594 realmRvr=180 inOtherSieges=16 defenseShare=0.5"));
            Assert.That(AutonomousRvrEventLayer.SiegeArmyFields("keep-siege", 69, 64, 80, 590, 200, 0, 1.0),
                Does.StartWith("kind=keep-siege needed=64 thin=false "));
        });

        // The owner's follow-up (2026-11-05): deliberate small strikes. Only a full siege that fell short of its muster is
        // "thin"; any other kind, now or later, is told apart by its kind.
        [TestCase(false, false, false, "keep-siege")]
        [TestCase(true, false, false, "relic-siege")]
        [TestCase(false, true, false, "retake")]
        [TestCase(false, false, true, "player-attack-defense")]
        public void EachAttackLogsItsKind(bool relic, bool retake, bool defense, string kind) =>
            Assert.That(AutonomousRvrEventLayer.SiegeKind(relic, retake, defense), Is.EqualTo(kind));

        [TestCase("keep-siege", 45, 64, true)]
        [TestCase("keep-siege", 64, 64, false)]
        [TestCase("relic-siege", 51, 120, true)]
        [TestCase("keep-siege", 0, 64, false)]      // never marched (a failed rally)
        [TestCase("retake", 10, 64, false)]
        [TestCase("tower-strike", 16, 64, false)]  // a planned small strike is not a thin siege
        public void OnlyASiegeThatFellShortOfItsMusterIsThin(string kind, int marched, int needed, bool thin) =>
            Assert.That(AutonomousRvrEventLayer.MarchedThin(kind, marched, needed), Is.EqualTo(thin));

        [Test]
        public void TheMusterWaitsForSixtyFourOrAHundredAndTwenty() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.MusterNeeded(false), Is.EqualTo(64));
            Assert.That(AutonomousRvrEventLayer.MusterNeeded(true), Is.EqualTo(120));
        });
    }
}
