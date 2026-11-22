using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Issue #418: some gamebots waited at frontier teleporters until the 15-minute watchdog moved them on. A bot picked the
    /// nearest porter by straight distance even when no medallion merchant stood beside it, and then declined the trip every
    /// turn ("Waiting for a frontier teleporter"). It now picks the nearest porter that sells its ticket. Every wait past five
    /// minutes is logged (RVR_FRONTIER_WAIT_LONG) with what holds it. Tests only: not seen on the owner's PC yet.
    /// </summary>
    [TestFixture]
    public class UT_FrontierPorterWait418
    {
        private static readonly (string Porter, double Distance)[] TwoBorderKeeps =
        {
            ("Druim Cain porter", 4_000),     // nearer, nobody sells the ticket beside it
            ("Druim Ligen porter", 95_000),   // farther, the medallion merchant beside it
        };

        [Test]
        public void TheNearestPorterThatSellsTheTicketIsChosen()
        {
            var asked = new List<string>();
            string chosen = AutonomousFrontierTransport.ChoosePorter(TwoBorderKeeps, porter =>
            {
                asked.Add(porter);
                return porter == "Druim Ligen porter";
            });
            Assert.Multiple(() =>
            {
                Assert.That(chosen, Is.EqualTo("Druim Ligen porter"));
                Assert.That(asked, Is.EqualTo(new[] { "Druim Cain porter", "Druim Ligen porter" }), "asked nearest first");
            });
        }

        [Test]
        public void TheNearestIsKeptWhenItSellsTheTicket()
        {
            var asked = new List<string>();
            string chosen = AutonomousFrontierTransport.ChoosePorter(TwoBorderKeeps, porter => { asked.Add(porter); return true; });
            Assert.Multiple(() =>
            {
                Assert.That(chosen, Is.EqualTo("Druim Cain porter"));
                Assert.That(asked, Has.Count.EqualTo(1), "no merchant search past the first porter that sells it");
            });
        }

        [Test]
        public void WithNoMerchantAnywhereTheNearestIsKeptAsBefore() =>
            Assert.That(AutonomousFrontierTransport.ChoosePorter(TwoBorderKeeps, _ => false), Is.EqualTo("Druim Cain porter"));

        [Test]
        public void NoPorterInTheRegionChoosesNone() =>
            Assert.That(AutonomousFrontierTransport.ChoosePorter(new (string, double)[0], _ => true), Is.Null);

        [Test]
        public void TheOrderGivenDoesNotMatterOnlyTheDistance() =>
            Assert.That(AutonomousFrontierTransport.ChoosePorter(new[] { ("far", 9_000.0), ("near", 100.0) }, _ => true),
                Is.EqualTo("near"));

        // ---- RVR_FRONTIER_WAIT_LONG --------------------------------------------------------------------------------------

        [Test]
        public void AWaitContinuesOnlyWhileItsTurnsFollowEachOther() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousFrontierTransport.WaitContinues(0, 10_000), Is.False, "the first turn starts a wait");
            Assert.That(AutonomousFrontierTransport.WaitContinues(10_000, 11_000), Is.True);
            Assert.That(AutonomousFrontierTransport.WaitContinues(10_000, 10_000 + AutonomousFrontierTransport.WaitGapMilliseconds), Is.True);
            Assert.That(AutonomousFrontierTransport.WaitContinues(10_000, 10_001 + AutonomousFrontierTransport.WaitGapMilliseconds), Is.False,
                "a gap (it left, fought, or went elsewhere) starts a new wait");
        });

        [Test]
        public void ALongWaitIsLoggedTwiceBeforeTheWatchdog()
        {
            long since = 1_000, last = 0, reports = 0;
            long watchdog = since + (long)AutonomousStuckWatchdog.StuckThreshold.TotalMilliseconds;
            for (long now = since; now < watchdog; now += 1_000)
            {
                if (!AutonomousFrontierTransport.LongWaitReportDue(since, last, now)) continue;
                last = now;
                reports++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFrontierTransport.LongWaitReportDue(since, 0, since + 299_000), Is.False, "not inside the slowest wait that works");
                Assert.That(AutonomousFrontierTransport.LongWaitReportDue(since, 0, since + 300_000), Is.True);
                Assert.That(reports, Is.EqualTo(2), "at 5 and 10 minutes, before the 15-minute watchdog");
                Assert.That(AutonomousFrontierTransport.LongWaitMilliseconds,
                    Is.GreaterThan(2 * 120_000L + AutonomousFrontierTransport.WarbandWaitMilliseconds).And.LessThan((long)AutonomousStuckWatchdog.StuckThreshold.TotalMilliseconds));
            });
        }
    }
}
