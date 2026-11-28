using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #523 step 2, honest pacing numbers. The response reserve was a hash of the warband's lowest member id alone, so the
    /// same 30% of warbands sat out every defense (defense.md finding 1); it is now drawn per event. The siege balance kept
    /// one server-wide window, so one realm's lost keeps raised every realm's defenders; each defending realm's own window now
    /// sets its defenders' share and the cap on armies attacking it once it has three sieges, the server-wide window before
    /// that (owner's answer to question 52, #523 issuecomment-7227740738; applied=own or applied=server).
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
        public void MidgardsLossesDoNotMoveHiberniasShareOrCap()
        {
            // 52A: Hibernia holds three keeps, then Midgard loses six. Only Midgard's own record moves Midgard's numbers.
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(false, eRealm.Hibernia);
            double hiberniaShare = SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia);
            int hiberniaCap = SiegeDefenseBalance.AttackerCap(128, eRealm.Hibernia);
            Assert.That(hiberniaShare, Is.EqualTo(0.625));
            Assert.That(hiberniaCap, Is.EqualTo(128));

            foreach (int _ in Enumerable.Range(0, 6)) SiegeDefenseBalance.Record(true, eRealm.Midgard);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia), Is.EqualTo(hiberniaShare), "Hibernia's share is its own");
                Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Hibernia), Is.EqualTo(hiberniaCap), "Hibernia's attackers' cap is its own");
                Assert.That(SiegeDefenseBalance.AttackerCap(240, eRealm.Hibernia), Is.EqualTo(240));
                Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Albion), Is.EqualTo(SiegeDefenseBalance.RatioAt(0.75, SiegeDefenseBalance.Shift)),
                    "Albion has no sieges yet: the server-wide record");
                Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Albion), Is.EqualTo(128));
                Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Midgard), Is.EqualTo(1.0), "Midgard's share at its limit");
                Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Midgard), Is.EqualTo(96), "then the armies attacking Midgard are trimmed");
                Assert.That(SiegeDefenseBalance.AttackerCap(240, eRealm.Midgard), Is.EqualTo(180));
                // The server-wide window (DDD then CCCCCC) is still kept and logged, and differs from both realms'.
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter([false, false, false, true, true, true, true, true, true])));
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step));
            });
        }

        [Test]
        public void EachRealmsOwnResultsMoveItsOwnShareAndCap()
        {
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Albion);
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Albion), Is.EqualTo(0.875), "three Albion keeps lost: one step more defenders");
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Albion);
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Albion), Is.EqualTo(SiegeDefenseBalance.MaximumShift + SiegeDefenseBalance.MaximumAttackerTrim));
            Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Albion), Is.EqualTo(96));

            // Albion holds again: the trim comes off first, then the share falls, by its own window.
            foreach (int _ in Enumerable.Range(0, 6)) SiegeDefenseBalance.Record(false, eRealm.Albion);
            Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Albion), Is.EqualTo(128), "the trim is off");
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Albion), Is.EqualTo(0.875), "the share is coming down");
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(false, eRealm.Albion);
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Albion), Is.EqualTo(SiegeDefenseBalance.ShiftAfter(
                [true, true, true, true, true, true, false, false, false, false, false, false, false, false, false])));
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Albion), Is.EqualTo(-SiegeDefenseBalance.MaximumShift), "the floor");
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Albion), Is.EqualTo(0.5));
            Assert.That(SiegeDefenseBalance.AttackerCap(128, eRealm.Albion), Is.EqualTo(128));

            // The policy's share, defender cap and attackers' cap in use follow the defending realm's own shift.
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Hibernia);
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                // Albion and Hibernia have their own three; Midgard has none, so it follows the server-wide record.
                double shift = realm == eRealm.Midgard ? SiegeDefenseBalance.Shift : SiegeDefenseBalance.RealmShift(realm);
                Assert.That(SiegeDefenseBalance.AppliedShift(realm), Is.EqualTo(shift), realm.ToString());
                Assert.That(RealmEventPolicy.EffectiveDefenseRatioFor(realm),
                    Is.EqualTo(SiegeDefenseBalance.RatioAt(RealmEventPolicy.DefenseRatio, shift)), realm.ToString());
                Assert.That(RealmEventPolicy.EffectiveAttackerCap(false, realm),
                    Is.EqualTo(SiegeDefenseBalance.AttackerCapAt(RealmEventPolicy.KeepAttackers, shift)), realm.ToString());
                Assert.That(RealmEventPolicy.DefenderCap(true, realm), Is.EqualTo(Math.Max(8, (int)Math.Round(
                    RealmEventPolicy.RelicAttackers * SiegeDefenseBalance.RatioAt(RealmEventPolicy.DefenseRatio, shift)))), realm.ToString());
            }
            Assert.That(RealmEventPolicy.EffectiveDefenseRatioFor(eRealm.Hibernia), Is.Not.EqualTo(RealmEventPolicy.EffectiveDefenseRatioFor(eRealm.Albion)));
        }

        [Test]
        public void ASiegeWithNoKnownDefenderUsesTheServerWideRecord()
        {
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true, eRealm.Midgard);
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true);
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.None), Is.EqualTo(SiegeDefenseBalance.Shift));
            Assert.That(SiegeDefenseBalance.Ratio(0.75), Is.EqualTo(SiegeDefenseBalance.RatioAt(0.75, SiegeDefenseBalance.Shift)));
            Assert.That(SiegeDefenseBalance.AttackerCap(128), Is.EqualTo(SiegeDefenseBalance.AttackerCapAt(128, SiegeDefenseBalance.Shift)));
            Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.Zero, "the server-wide record never moves a realm's own");
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.EqualTo(SiegeDefenseBalance.Shift),
                "a realm with no sieges of its own uses the server-wide record");
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Midgard), Is.EqualTo(SiegeDefenseBalance.RealmShift(eRealm.Midgard)),
                "Midgard has three of its own");
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
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia), Is.EqualTo(0.875), "the rebuilt window is applied");
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Albion), Is.EqualTo(SiegeDefenseBalance.RatioAt(0.75, SiegeDefenseBalance.Shift)),
                "one result of its own: Albion uses the server-wide record");

            SiegeDefenseBalance.Load([true, true, true]);
            Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia), Is.EqualTo(0.875), "no record of its own: the server-wide one");
            Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Hibernia), Is.Empty, "a new ledger replaces every record");
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.Step));
        }

        [Test]
        public void TheRealmLineIsPinned()
        {
            Assert.That(SiegeDefenseBalance.RealmLine(eRealm.Midgard, "captured", 0.125, 0.25, [true, true, false, true]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE_REALM realm=Midgard event=\"captured\" shift=+0.125->+0.25 share=1 " +
                           "attackerCap=128 window=\"CCDC\" applied=own"));
            Assert.That(SiegeDefenseBalance.RealmLine(eRealm.Albion, "defended", 0, -0.125, [false, false, false]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE_REALM realm=Albion event=\"defended\" shift=0->-0.125 share=0.625 " +
                           "attackerCap=128 window=\"DDD\" applied=own"));
            Assert.That(SiegeDefenseBalance.RealmLine(eRealm.Hibernia, "defended", 0, 0, [false, false]),
                Does.EndWith("window=\"DD\" applied=server"), "two of its own: the server-wide record is applied");
        }

        [TestCase("Automatic continuous siege; defender: Midgard", eRealm.Midgard)]
        [TestCase("Forced continuous siege; defender: Albion", eRealm.Albion)]
        [TestCase("Automatic continuous siege; defender: Hibernia", eRealm.Hibernia)]
        [TestCase("Retake attempt after a player captured the keep; one try", eRealm.None)]
        [TestCase("Keep captured: ownership changed", eRealm.None)]
        [TestCase("Keep captured: ownership changed; defender: Midgard", eRealm.Midgard)]
        [TestCase("Siege defended: no attacker reached the keep for 10 minutes; defender: Hibernia", eRealm.Hibernia)]
        [TestCase("", eRealm.None)]
        [TestCase(null, eRealm.None)]
        public void TheLedgerNamesTheDefender(string details, eRealm expected)
        {
            Assert.That(SiegeDefenseBalance.DefenderOf(details), Is.EqualTo(expected));
        }

        // ---- Owner's answer to question 52 (#523 issuecomment-7227740738): the server-wide record until three of its own ----

        [Test]
        public void ARealmFollowsTheServerWideRecordUntilItHasThreeSiegesOfItsOwn()
        {
            foreach (int _ in Enumerable.Range(0, 3)) SiegeDefenseBalance.Record(true);
            SiegeDefenseBalance.Record(false, eRealm.Hibernia);
            SiegeDefenseBalance.Record(false, eRealm.Hibernia);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.UsesOwnRecord(eRealm.Hibernia), Is.False, "two of its own");
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter([true, true, true, false, false])));
                Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.EqualTo(SiegeDefenseBalance.Shift));
                Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.Not.EqualTo(SiegeDefenseBalance.RealmShift(eRealm.Hibernia)));
                Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia), Is.EqualTo(SiegeDefenseBalance.RatioAt(0.75, SiegeDefenseBalance.Shift)));
            });

            SiegeDefenseBalance.Record(false, eRealm.Hibernia);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.UsesOwnRecord(eRealm.Hibernia), Is.True, "the third of its own");
                Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.EqualTo(-SiegeDefenseBalance.Step));
                Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.EqualTo(-SiegeDefenseBalance.Step), "its own record now");
                Assert.That(SiegeDefenseBalance.Ratio(0.75, eRealm.Hibernia), Is.EqualTo(0.625));
                Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Albion), Is.EqualTo(SiegeDefenseBalance.Shift), "Albion has none: server-wide");
            });
        }

        [Test]
        public void TheSwitchToItsOwnRecordCarriesOverTheLedgerReplay()
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.LoadResults([
                new(true, eRealm.None), new(true, eRealm.None), new(true, eRealm.None),
                new(false, eRealm.Hibernia), new(false, eRealm.Hibernia),
            ]);
            Assert.That(SiegeDefenseBalance.UsesOwnRecord(eRealm.Hibernia), Is.False, "two of its own after the replay");
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.EqualTo(SiegeDefenseBalance.Shift));

            SiegeDefenseBalance.LoadResults([
                new(true, eRealm.None), new(true, eRealm.None), new(true, eRealm.None),
                new(false, eRealm.Hibernia), new(false, eRealm.Hibernia), new(false, eRealm.Hibernia),
            ]);
            Assert.That(SiegeDefenseBalance.UsesOwnRecord(eRealm.Hibernia), Is.True, "three of its own after the replay");
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Hibernia), Is.EqualTo(-SiegeDefenseBalance.Step));
        }

        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(6, true)]
        public void TheOwnRecordAppliesFromTheThirdSiege(int own, bool applies) =>
            Assert.That(SiegeDefenseBalance.OwnRecordApplies(own), Is.EqualTo(applies));

        // ---- The caps of a siege use the defending realm's record (review of #799, re-review point 1) ----

        /// <summary>
        /// Twelve Midgard captures, then twelve Albion defenses: Midgard's own record sits at its trim (+0.5: share 1.0,
        /// attackers 96, defenders 128) while the server-wide record has fallen to its floor (-0.25: share 0.5, attackers
        /// 128, defenders 64). A siege on a Midgard keep must take Midgard's numbers in DefenderCapFor (its cap and its
        /// share) and AttackerCapFor; passing no realm there gives the server-wide numbers and fails this.
        /// </summary>
        private static object MidgardSiege(int marched)
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.LoadResults([
                .. Enumerable.Repeat(new SiegeDefenseBalance.SiegeResult(true, eRealm.Midgard), 12),
                .. Enumerable.Repeat(new SiegeDefenseBalance.SiegeResult(false, eRealm.Albion), 12),
            ]);
            AutonomousRvrEventLayer.SiegeScheduling = true;
            Type type = typeof(AutonomousRvrEventLayer).GetNestedType("ActiveEvent", BindingFlags.NonPublic);
            object active = Activator.CreateInstance(type, nonPublic: true);
            void Set(string field, object value) => type.GetField(field).SetValue(active, value);
            Set("TargetId", "rvr-keep-799cap");
            Set("RelicKeep", false);
            Set("AttackerRealm", eRealm.Albion);
            Set("DefenderRealm", eRealm.Midgard);
            Set("MarchedAttackers", marched);
            return active;
        }

        private static int Cap(string method, object active) =>
            (int)typeof(AutonomousRvrEventLayer).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [active]);

        [Test]
        public void TheTwoRecordsDifferInTheCapTest() => Assert.Multiple(() =>
        {
            MidgardSiege(0);
            Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Midgard), Is.EqualTo(0.5), "Midgard at its trim");
            Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(-0.25), "server-wide at its floor");
            Assert.That(RealmEventPolicy.EffectiveAttackerCap(false), Is.EqualTo(128), "server-wide attackers");
            Assert.That(RealmEventPolicy.DefenderCap(false), Is.EqualTo(64), "server-wide defenders");
        });

        [Test]
        public void TheAttackersCapIsTheDefendingRealmsOwn()
        {
            object active = MidgardSiege(0);
            Assert.That(Cap("AttackerCapFor", active), Is.EqualTo(96), "Midgard's trim, not the server-wide 128");
        }

        [TestCase(0, 128)]    // no march yet: the cap from Midgard's share (server-wide: 64)
        [TestCase(40, 40)]    // Midgard's share of 1.0 of the army that marched (server-wide: 20)
        [TestCase(200, 128)]  // a big army meets Midgard's cap (server-wide: 64)
        public void TheDefendersCapIsTheDefendingRealmsOwn(int marched, int expected)
        {
            object active = MidgardSiege(marched);
            Assert.That(Cap("DefenderCapFor", active), Is.EqualTo(expected));
        }

        // ---- The finished row names the defending realm (review of #799, point 1) ----

        [Test]
        public void TheFinishedRowNamesTheDefender()
        {
            string bot = AutonomousRvrEventLayer.FinishedSiegeDetails(false, "Keep captured: ownership changed", eRealm.Midgard);
            string player = AutonomousRvrEventLayer.FinishedSiegeDetails(true, "Siege defended: the attackers left", eRealm.Albion);
            Assert.Multiple(() =>
            {
                Assert.That(bot, Is.EqualTo("Keep captured: ownership changed; defender: Midgard"));
                Assert.That(SiegeDefenseBalance.DefenderOf(bot), Is.EqualTo(eRealm.Midgard));
                Assert.That(player, Does.StartWith(RealmEventRecordStore.PlayerAttackPrefix), "a player attack is still left out of the balance");
                Assert.That(AutonomousRvrEventLayer.FinishedSiegeDetails(false, "Ended", eRealm.None), Is.EqualTo("Ended"));
            });
        }

        private static AutonomousRvrEventLayer.LiveObjective MidKeep(int n) =>
            new("rvr-keep-799" + n, "Midgard keep " + n, AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100,
                400000 + n * 20000, 400000, 3000, false, 0, 0, 2, 2);

        private static Dictionary<string, RealmEventRecord> PendingRows() =>
            (Dictionary<string, RealmEventRecord>)typeof(RealmEventRecords).GetField("Pending", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>
        /// Points 1 and 3 of the review of #799: Albion takes three Midgard keeps through the event layer (ChooseOrJoin, a
        /// Progress that replaces the row's details as the recruit pass does, EndTarget). Only Midgard's record moves. The
        /// ledger rows EndEvent wrote are saved to a ledger file and read back as a restart reads them
        /// (SiegeResultsWithDetails, FromLedger, LoadResults): each names Midgard, so the rebuilt record is Midgard's.
        /// </summary>
        [Test]
        public void ACaptureThroughTheEventLayerMovesOnlyTheDefendersRecordAndSurvivesARestart()
        {
            DOL.GS.Tests.RvrEventTestState.Clear();
            string folder = Path.Combine(Path.GetTempPath(), "ut-siege-ledger-799-" + Guid.NewGuid().ToString("N"));
            try
            {
                var keeps = Enumerable.Range(0, 3).Select(MidKeep).ToArray();
                for (int n = 0; n < keeps.Length; n++)
                {
                    long t = 1_000 + n * 120_000L;
                    var force = new AutonomousRvrEventLayer.Force("alb-799-" + n, eRealm.Albion, 8, 50, 2);
                    AutonomousRvrEventLayer.Plan plan = AutonomousRvrEventLayer.ChooseOrJoin(force, new[] { keeps[n] }, t, 0);
                    Assert.That(plan?.TargetId, Is.EqualTo(keeps[n].Id), plan?.Reason);
                    RealmEventRecords.Progress(keeps[n].Id, "Battle", "Reinforcements converging: attackers=8, defenders=0, third realm=0", 8, 8);
                    AutonomousRvrEventLayer.EndTarget(keeps[n].Id, t + 60_000, eRealm.Albion);
                    if (n == 0)
                    {
                        Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Midgard), Is.EqualTo(new[] { true }), "the capture is Midgard's");
                        Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Albion), Is.Empty);
                        Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Hibernia), Is.Empty);
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Midgard), Is.EqualTo(new[] { true, true, true }));
                    Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Midgard), Is.EqualTo(SiegeDefenseBalance.Step), "Midgard's record moved");
                    Assert.That(SiegeDefenseBalance.UsesOwnRecord(eRealm.Midgard), Is.True);
                    Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Albion), Is.Zero, "Albion's did not");
                    Assert.That(SiegeDefenseBalance.RealmShift(eRealm.Hibernia), Is.Zero, "Hibernia's did not");
                    Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Albion), Is.Empty);
                    Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Hibernia), Is.Empty);
                });

                // The rows EndEvent finished, through the real ledger file.
                var ids = keeps.Select(k => k.Id).ToHashSet();
                var rows = PendingRows().Values.Where(r => ids.Contains(r.EventId) && r.Phase == "Ended").ToArray();
                Assert.That(rows, Has.Length.EqualTo(3));
                Assert.That(rows.Select(r => r.Details), Has.All.EndWith("; defender: Midgard"));
                string path = RealmEventRecordStore.PathIn(folder);
                RealmEventRecordStore.Save(path, rows);
                var results = SiegeDefenseBalance.FromLedger(RealmEventRecordStore.SiegeResultsWithDetails(path, SiegeDefenseBalance.LedgerResults));
                Assert.That(results, Is.EqualTo(Enumerable.Repeat(new SiegeDefenseBalance.SiegeResult(true, eRealm.Midgard), 3)));

                SiegeDefenseBalance.ResetUnloadedForTests();
                SiegeDefenseBalance.LoadResults(results);
                Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Midgard), Is.EqualTo(new[] { true, true, true }), "rebuilt after a restart");
                Assert.That(SiegeDefenseBalance.AppliedShift(eRealm.Midgard), Is.EqualTo(SiegeDefenseBalance.Step));
                Assert.That(SiegeDefenseBalance.RealmWindow(eRealm.Albion), Is.Empty);
            }
            finally
            {
                DOL.GS.Tests.RvrEventTestState.Clear();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
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
                Assert.That(SiegeDefenseBalance.FromLedger(RealmEventRecordStore.SiegeResultsWithDetails(path, 24)), Is.EqualTo(new SiegeDefenseBalance.SiegeResult[]
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
