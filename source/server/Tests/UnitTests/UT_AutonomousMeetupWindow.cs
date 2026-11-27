using DOL.GS;
using NUnit.Framework;

// 1.2.3 (#569 step 3): ported from zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze).
namespace DOL.UnitTests
{
    /// <summary>The meetup wait sized from the farthest member, at most 20 minutes (owner 2026-11-10).</summary>
    [TestFixture]
    public class UT_AutonomousMeetupWindow
    {
        private const long Start = 1_000_000;
        private const long Minute = 60_000;

        [Test]
        public void ACustomWindowTimesOutAtItsOwnLength()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1 }, Start, 20 * Minute);
            Assert.That(attendance.WindowMilliseconds, Is.EqualTo(20 * Minute));
            Assert.That(attendance.Observe(1, Start + 20 * Minute - 1, false), Is.False);
            Assert.That(attendance.Observe(1, Start + 20 * Minute, false), Is.True);
            Assert.That(attendance.WaitedMilliseconds(1, Start + 20 * Minute), Is.EqualTo(20 * Minute));
        }

        [Test]
        public void TheDefaultRebaseIsStillFifteenMinutes()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1 }, Start, 20 * Minute);
            attendance.Rebase(new long[] { 1 }, Start);
            Assert.That(attendance.Observe(1, Start + AutonomousRendezvousAttendance.TimeoutMilliseconds - 1, false), Is.False);
            Assert.That(attendance.Observe(1, Start + AutonomousRendezvousAttendance.TimeoutMilliseconds, false), Is.True);
        }

        [Test]
        public void ARosterReductionKeepsTheCustomDeadline()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1, 2 }, Start, 20 * Minute);
            attendance.RebaseAfterRosterReduction(new long[] { 1 }, Start + Minute);
            Assert.That(attendance.Observe(1, Start + 16 * Minute, false), Is.False, "A 15-minute rebase would end at 16.");
            Assert.That(attendance.Observe(1, Start + 20 * Minute - 1, false), Is.False);
            Assert.That(attendance.Observe(1, Start + 20 * Minute, false), Is.True);
        }

        [Test]
        public void ARosterReductionKeepsTheTwoMinuteFloor()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1, 2 }, Start, 20 * Minute);
            long reducedAt = Start + 19 * Minute + 30_000;
            attendance.RebaseAfterRosterReduction(new long[] { 1 }, reducedAt);
            Assert.That(attendance.Observe(1, reducedAt + 2 * Minute - 1, false), Is.False);
            Assert.That(attendance.Observe(1, reducedAt + 2 * Minute, false), Is.True);
        }

        [Test]
        public void ResetReturnsToFifteenMinutes()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1 }, Start, 20 * Minute);
            attendance.Reset();
            Assert.That(attendance.WindowMilliseconds, Is.EqualTo(AutonomousRendezvousAttendance.TimeoutMilliseconds));
        }

        [Test]
        public void ANonPositiveWindowFallsBackToFifteenMinutes()
        {
            var attendance = new AutonomousRendezvousAttendance();
            attendance.RebaseWithWindow(new long[] { 1 }, Start, 0);
            Assert.That(attendance.WindowMilliseconds, Is.EqualTo(AutonomousRendezvousAttendance.TimeoutMilliseconds));
        }
    }
}
