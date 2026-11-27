using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #523 step 2, honest pacing numbers. The response reserve was a hash of the warband's lowest member id alone, so the
    /// same 30% of warbands sat out every defense (defense.md finding 1); it is now drawn per event. The siege balance kept
    /// one server-wide window, so one realm's lost keeps raised every realm's defenders; a window per defending realm is
    /// now kept and logged beside it (applied=false until the owner picks the lever, #523 question 49).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_HonestPacing523
    {
        [SetUp]
        public void SetUp() => SiegeDefenseBalance.Reset();

        [TearDown]
        public void TearDown() => SiegeDefenseBalance.Reset();

        private static ulong Seed(int n) => AutonomousRvrEventLayer.ReserveSeed("keep-" + n, 1_000_000L + n * 60_000L);

        [Test]
        public void EachEventHoldsBackAboutThirtyPercentOfTheWarbands()
        {
            foreach (int n in Enumerable.Range(0, 20))
            {
                ulong seed = Seed(n);
                int held = Enumerable.Range(1, 1000).Count(id => AutonomousRvrEventLayer.ResponseReserve(id, seed));
                Assert.That(held, Is.InRange(250, 350), $"event {n}");
            }
        }

        [Test]
        public void AWarbandSitsOutSomeEventsNotAllOrNone()
        {
            foreach (long id in new long[] { 1, 2, 7, 42, 1000, 123_456_789 })
            {
                int held = Enumerable.Range(0, 1000).Count(n => AutonomousRvrEventLayer.ResponseReserve(id, Seed(n)));
                Assert.That(held, Is.InRange(250, 350), $"warband {id}");
            }
        }

        [Test]
        public void TwoEventsHoldBackDifferentWarbandsAndOneEventIsStable()
        {
            ulong a = Seed(1), b = Seed(2);
            bool[] first = Enumerable.Range(1, 200).Select(id => AutonomousRvrEventLayer.ResponseReserve(id, a)).ToArray();
            bool[] again = Enumerable.Range(1, 200).Select(id => AutonomousRvrEventLayer.ResponseReserve(id, a)).ToArray();
            bool[] other = Enumerable.Range(1, 200).Select(id => AutonomousRvrEventLayer.ResponseReserve(id, b)).ToArray();
            Assert.That(again, Is.EqualTo(first), "the same event always holds back the same warbands");
            Assert.That(other, Is.Not.EqualTo(first), "another event holds back others");
            Assert.That(AutonomousRvrEventLayer.ReserveSeed("keep-1", 5), Is.Not.EqualTo(AutonomousRvrEventLayer.ReserveSeed("keep-1", 6)),
                "the same keep sieged again is a new draw");
            Assert.That(AutonomousRvrEventLayer.ReserveSeed("keep-1", 5), Is.Not.EqualTo(AutonomousRvrEventLayer.ReserveSeed("keep-2", 5)));
            Assert.That(AutonomousRvrEventLayer.ReserveSeed(null, 5), Is.EqualTo(AutonomousRvrEventLayer.ReserveSeed("", 5)));
        }

        [Test]
        public void TheClaimingGuildsWarbandIsNeverHeldBack()
        {
            ulong seed = Seed(3);
            long reserved = Enumerable.Range(1, 1000).First(id => AutonomousRvrEventLayer.ResponseReserve(id, seed));
            long free = Enumerable.Range(1, 1000).First(id => !AutonomousRvrEventLayer.ResponseReserve(id, seed));
            Assert.That(AutonomousRvrEventLayer.HeldInReserve(reserved, seed, claimDefenseRank: 0), Is.False, "the claiming guild answers");
            Assert.That(AutonomousRvrEventLayer.HeldInReserve(reserved, seed, claimDefenseRank: 1), Is.True);
            Assert.That(AutonomousRvrEventLayer.HeldInReserve(free, seed, claimDefenseRank: 1), Is.False);
            Assert.That(AutonomousRvrEventLayer.ResponseReservePercent, Is.EqualTo(30), "the share is unchanged");
        }

        [Test]
        public void ARealmsLossesMoveOnlyItsOwnRecord()
        {
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Midgard);
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Midgard), Is.EqualTo(SiegeDefenseBalance.Step));
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Albion), Is.Zero);
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.Zero);
            Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Midgard), Is.EqualTo(new[] { true, true, true }));
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step), "the server-wide shift moves as before");

            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(false, eRealm.Hibernia);
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.EqualTo(-SiegeDefenseBalance.Step));
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Midgard), Is.EqualTo(SiegeDefenseBalance.Step), "Hibernia's wins leave Midgard alone");
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter([true, true, true, false, false, false])));
        }

        [Test]
        public void TheAppliedLeverStillFollowsTheServerWideShift()
        {
            // Three Albion captures: the server-wide window moves a step, and the share and cap in use follow it alone.
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Albion);
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(false, eRealm.Midgard);
            double shift = SiegeDefenseBalance.Shift;
            Assert.That(shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter([true, true, true, false, false, false])));
            Assert.That(SiegeDefenseBalance.Ratio(0.75), Is.EqualTo(SiegeDefenseBalance.RatioAt(0.75, shift)));
            Assert.That(SiegeDefenseBalance.AttackerCap(128), Is.EqualTo(SiegeDefenseBalance.AttackerCapAt(128, shift)));
        }

        [Test]
        public void ASiegeWithNoKnownDefenderCountsServerWideOnly()
        {
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true);
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step));
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia, eRealm.None })
            {
                Assert.That(SiegeDefenseBalance.RealmShift(realm), Is.Zero, realm.ToString());
                Assert.That(SiegeDefenseBalance.RealmWindow(realm), Is.Empty, realm.ToString());
            }
        }

        [Test]
        public void ARestartRebuildsEachRealmsWindowFromTheLedger()
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Record(true, eRealm.Hibernia);   // ended before the ledger was read: replayed after it
            SiegeDefenseBalance.LoadResults([
                new(true, eRealm.Hibernia), new(true, eRealm.Hibernia), new(false, eRealm.Albion), new(true, eRealm.None),
            ]);
            Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Hibernia), Is.EqualTo(new[] { true, true, true }));
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.EqualTo(SiegeDefenseBalance.Step));
            Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Albion), Is.EqualTo(new[] { false }));
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter([true, true, false, true, true])));

            SiegeDefenseBalance.Load([true, true, true]);
            Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Hibernia), Is.Empty, "a new ledger replaces every record");
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step));
        }

        [Test]
        public void TheRealmLineIsPinned()
        {
            Assert.That(SiegeDefenseBalance.RealmLine(eRealm.Midgard, "captured", 0.125, 0.25, [true, true, false, true]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE_REALM realm=Midgard event=\"captured\" shift=+0.125->+0.25 share=1 " +
                           "attackerCap=128 window=\"CCDC\" applied=false"));
            Assert.That(SiegeDefenseBalance.RealmLine(eRealm.Albion, "defended", 0, -0.125, [false, false, false]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE_REALM realm=Albion event=\"defended\" shift=0->-0.125 share=0.625 " +
                           "attackerCap=128 window=\"DDD\" applied=false"));
        }

        [TestCase("Automatic continuous siege; defender: Midgard", eRealm.Midgard)]
        [TestCase("Forced continuous siege; defender: Albion", eRealm.Albion)]
        [TestCase("Automatic continuous siege; defender: Hibernia", eRealm.Hibernia)]
        [TestCase("Retake attempt after a player captured the keep; one try", eRealm.None)]
        [TestCase("Keep captured: ownership changed", eRealm.None)]
        [TestCase("", eRealm.None)]
        [TestCase(null, eRealm.None)]
        public void TheLedgerNamesTheDefender(string details, eRealm expected)
        {
            Assert.That(RealmEventRecordStore.DefenderOf(details), Is.EqualTo(expected));
        }

        [Test]
        public void TheLedgerGivesBackEachBotSiegesDefender()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ut-siege-ledger-523-" + Guid.NewGuid().ToString("N"));
            string path = RealmEventRecordStore.PathIn(folder);
            try
            {
                int n = 0;
                RealmEventRecord Row(string outcome, string details) =>
                    new(Guid.NewGuid().ToString("N"), "keep-" + n, "Keep " + n, "Keep", "Albion",
                        $"2026-11-04T10:{n:00}:00.0000000Z", $"2026-11-04T11:{n++:00}:00.0000000Z", "Ended", outcome, details, 50, 40);
                RealmEventRecordStore.Save(path, [
                    Row("Captured", "Automatic continuous siege; defender: Midgard"),
                    Row("Defended", RealmEventRecordStore.PlayerAttackPrefix + "Siege defended: the attackers left"),
                    Row("Defended", "Forced continuous siege; defender: Hibernia"),
                    Row("Captured", "Retake attempt after a player captured the keep; one try"),
                ]);
                Assert.That(RealmEventRecordStore.SiegeResultsWithDefenders(path, 24), Is.EqualTo(new SiegeDefenseBalance.SiegeResult[]
                {
                    new(true, eRealm.Midgard), new(false, eRealm.Hibernia), new(true, eRealm.None),
                }));
                Assert.That(RealmEventRecordStore.SiegeResults(path, 24), Is.EqualTo(new[] { true, false, true }));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
