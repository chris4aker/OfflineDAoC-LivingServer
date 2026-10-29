using System;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>The GM's /lstest switches (owner 2026-10-28): each forces one moment, once.</summary>
    [TestFixture]
    public class UT_LivingServerTestCommand
    {
        [Test]
        public void AForcedLeeroyIsRolledOnceThenTheOddsAreBack()
        {
            Assert.That(AutonomousRaidLeeroy.RollForRaid(0.99), Is.False, "5%: a high draw never rolls");
            AutonomousRaidLeeroy.ForceNextRaid();
            Assert.That(AutonomousRaidLeeroy.RollForRaid(0.99), Is.True, "the GM's next raid has one");
            Assert.That(AutonomousRaidLeeroy.RollForRaid(0.99), Is.False, "only that one");
            Assert.That(AutonomousRaidLeeroy.RollForRaid(0.01), Is.True);
        }

        [Test]
        public void LeeroyNowChargesOnceWithTwoRaidersThere()
        {
            Assert.That(AutonomousRaidLeeroy.GoesNow(false, 30), Is.False);
            AutonomousRaidLeeroy.ChargeNow();
            Assert.That(AutonomousRaidLeeroy.GoesNow(false, 1), Is.False, "one raider alone waits");
            Assert.That(AutonomousRaidLeeroy.GoesNow(false, 2), Is.True);
            Assert.That(AutonomousRaidLeeroy.GoesNow(false, 30), Is.False, "used once");
            Assert.That(AutonomousRaidLeeroy.GoesNow(true, 30), Is.True, "his own rules still apply");
        }

        [Test]
        public void EveryHolidayHasATestDayInsideItsWindow()
        {
            var from = new DateTime(2026, 10, 28);
            foreach (string name in AutonomousHolidays.Names)
            {
                DateTime? day = AutonomousHolidays.DayOf(name, from);
                Assert.That(day, Is.Not.Null, name);
                Assert.That(AutonomousHolidays.Flags(day.Value), Does.Contain("holiday_" + name).And.Contain("holiday_today"), name);
            }
            Assert.That(AutonomousHolidays.DayOf("arbor day", from), Is.Null);
            Assert.That(AutonomousHolidays.DayOf("christmas", from), Is.EqualTo(new DateTime(2026, 12, 24)));
        }

        [Test]
        public void TheTestDateIsWhatHolidayChatterGoesBy()
        {
            try
            {
                AutonomousHolidays.TestDate = new DateTime(2026, 12, 25, 20, 0, 0);
                Assert.That(AutonomousHolidays.Now, Is.EqualTo(new DateTime(2026, 12, 25, 20, 0, 0)));
            }
            finally
            {
                AutonomousHolidays.TestDate = null;
            }
            Assert.That((AutonomousHolidays.Now - DateTime.Now).Duration(), Is.LessThan(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public void ADeedWaitsSecondsForABusyWitnessAndAMinuteForAScene()
        {
            // In-game check 2026-10-28: a mid-pull witness held the deed back two minutes.
            Assert.That(AutonomousPlayerDeeds.RetryDelayMs(false), Is.EqualTo(15_000));
            Assert.That(AutonomousPlayerDeeds.RetryDelayMs(true), Is.EqualTo(60_000));
            Assert.That(AutonomousPlayerDeeds.TellTries * AutonomousPlayerDeeds.RetryDelayMs(false), Is.GreaterThanOrEqualTo(150_000),
                "a witness gets a few pulls' time to finish fighting");
        }

        [Test]
        public void ABotGreetsOnlyFromWhereItsSayIsHeard()
        {
            // It was 700: a bot past the 512 a /say carries used up the greeting and nobody heard it.
            Assert.That(AutonomousLivingServer.GreetRange, Is.LessThanOrEqualTo(WorldMgr.SAY_DISTANCE));
        }

        [Test]
        public void ALeeroyJumpsThePullWhenTheRaidArrivesAllAtOnce()
        {
            // Overnight check 2026-10-29: Crystal Cave went from 1 to 52 at the muster in ten seconds; he never charged.
            Assert.That(AutonomousRaidLeeroy.ShouldCharge(52, 49, 10_000, 0, false), Is.False, "the old rule: all there, so no charge");
            Assert.That(AutonomousRaidLeeroy.JumpsThePull(52, 49, 0, false), Is.True);
            Assert.That(AutonomousRaidLeeroy.JumpsThePull(30, 49, 0, false), Is.False, "not all there: his usual rule decides");
            Assert.That(AutonomousRaidLeeroy.JumpsThePull(52, 49, 1, false), Is.False, "never after a wipe");
            Assert.That(AutonomousRaidLeeroy.JumpsThePull(52, 49, 0, true), Is.False, "never while regrouping");
        }
    }
}
