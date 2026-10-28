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
    }
}
