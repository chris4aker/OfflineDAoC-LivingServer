using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #572 step 4: how an autonomous gamebot leaves a battleground,
    /// the backstops inside one (20 minutes without progress, 3 deaths without a gain) and the 30-minute re-entry block
    /// after a forced exit. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_BattlegroundExit572
    {
        private const int Radius = 400;
        private const long Minute = 60_000;

        [SetUp, TearDown]
        public void Forget() => AutonomousBattlegroundExit.Clear();

        [Test]
        public void ExitStep_WalksFirst_ThenExitsFromHereAfterFourMinutes()
        {
            Assert.That(AutonomousBattlegroundExit.ExitStep(3_000, Radius, false, 0), Is.EqualTo(eBattlegroundExitStep.WalkToExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(3_000, Radius, false, 4 * Minute - 1), Is.EqualTo(eBattlegroundExitStep.WalkToExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(3_000, Radius, false, 4 * Minute), Is.EqualTo(eBattlegroundExitStep.ExitFromHere));
        }

        [Test]
        public void ExitStep_AFightOnTheWayKeepsTheWalkUntilTheHardLimit()
        {
            Assert.That(AutonomousBattlegroundExit.ExitStep(3_000, Radius, true, 5 * Minute), Is.EqualTo(eBattlegroundExitStep.WalkToExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(3_000, Radius, true, 10 * Minute), Is.EqualTo(eBattlegroundExitStep.ExitFromHere));
        }

        [Test]
        public void ExitStep_AtTheExit_FinishesTheFightThenTakesTheExit()
        {
            Assert.That(AutonomousBattlegroundExit.ExitStep(Radius, Radius, true, Minute), Is.EqualTo(eBattlegroundExitStep.FinishFight));
            Assert.That(AutonomousBattlegroundExit.ExitStep(Radius, Radius, false, Minute), Is.EqualTo(eBattlegroundExitStep.TakeExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(100, Radius, true, 10 * Minute), Is.EqualTo(eBattlegroundExitStep.ExitFromHere),
                "the hard limit holds even at the exit");
        }

        [Test]
        public void LeaveStarted_KeepsARunningLeave_RestartsAStaleOne()
        {
            Assert.That(AutonomousBattlegroundExit.LeaveStarted(0, 0, 50_000), Is.EqualTo(50_000));
            Assert.That(AutonomousBattlegroundExit.LeaveStarted(10_000, 45_000, 50_000), Is.EqualTo(10_000));
            Assert.That(AutonomousBattlegroundExit.LeaveStarted(10_000, 15_000, 50_000), Is.EqualTo(50_000), "more than 30 s since the last call");
        }

        [Test]
        public void IsStuck_AtTwentyMinutes()
        {
            Assert.That(AutonomousBattlegroundExit.IsStuck(0, 100 * Minute), Is.False, "0 = not measured");
            Assert.That(AutonomousBattlegroundExit.IsStuck(Minute, 20 * Minute), Is.False);
            Assert.That(AutonomousBattlegroundExit.IsStuck(Minute, 21 * Minute), Is.True);
        }

        [Test]
        public void IsProgress_GainsCount_ADeathsLossDoesNot_MovingCountsOnlyOnATour()
        {
            Assert.That(AutonomousBattlegroundExit.IsProgress(110, 100, 0, 0, false, false), Is.True, "XP gained");
            Assert.That(AutonomousBattlegroundExit.IsProgress(90, 100, 0, 0, false, false), Is.False, "XP lost to a death");
            Assert.That(AutonomousBattlegroundExit.IsProgress(100, 100, 5, 0, false, false), Is.True, "realm points gained");
            Assert.That(AutonomousBattlegroundExit.IsProgress(100, 100, 0, 0, true, false), Is.False, "a grinder walking is not progress");
            Assert.That(AutonomousBattlegroundExit.IsProgress(100, 100, 0, 0, true, true), Is.True, "a tour roamer crossing the field is");
        }

        [Test]
        public void Watch_NoProgressForTwentyMinutes_ForcesTheExit()
        {
            const long id = 572_001;
            long t = 1_000 * Minute;
            Assert.That(AutonomousBattlegroundExit.Observe(id, 252, true, 100, 0, 1_000, 1_000, false, t), Is.EqualTo(eBattlegroundForcedExit.None));
            Assert.That(AutonomousBattlegroundExit.Observe(id, 252, true, 100, 0, 1_100, 1_000, false, t + 19 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.None));
            Assert.That(AutonomousBattlegroundExit.Observe(id, 252, true, 100, 0, 1_100, 1_000, false, t + 20 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.NoProgress));
        }

        [Test]
        public void Watch_AGainRestartsTheClock_AndATourRoamerMovingFarIsProgress()
        {
            const long grinder = 572_002, roamer = 572_003;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.Observe(grinder, 252, true, 100, 0, 0, 0, false, t);
            AutonomousBattlegroundExit.Observe(grinder, 252, true, 150, 0, 0, 0, false, t + 15 * Minute);
            Assert.That(AutonomousBattlegroundExit.Observe(grinder, 252, true, 150, 0, 0, 0, false, t + 30 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.None), "the gain at 15 min restarted the 20-min clock");

            AutonomousBattlegroundExit.Observe(roamer, 252, true, 100, 0, 0, 0, true, t);
            AutonomousBattlegroundExit.Observe(roamer, 252, true, 100, 0, 2_000, 0, true, t + 15 * Minute);
            Assert.That(AutonomousBattlegroundExit.Observe(roamer, 252, true, 100, 0, 2_000, 0, true, t + 30 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.None));
            Assert.That(AutonomousBattlegroundExit.Observe(roamer, 252, true, 100, 0, 2_100, 0, true, t + 35 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.NoProgress), "a short shuffle is not progress");
        }

        [Test]
        public void Watch_ThreeDeathsWithoutAGain_ForceTheExit_AGainBetweenResets()
        {
            const long id = 572_004;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.Observe(id, 251, true, 100, 0, 0, 0, false, t);
            AutonomousBattlegroundExit.NoteDeath(id, 251);
            AutonomousBattlegroundExit.NoteDeath(id, 251);
            Assert.That(AutonomousBattlegroundExit.Observe(id, 251, true, 90, 0, 0, 0, false, t + Minute),
                Is.EqualTo(eBattlegroundForcedExit.None), "two deaths");
            AutonomousBattlegroundExit.Observe(id, 251, true, 95, 0, 0, 0, false, t + 2 * Minute); // a gain after the loss
            Assert.That(AutonomousBattlegroundExit.DeathsWithoutGain(id), Is.EqualTo(0));
            AutonomousBattlegroundExit.NoteDeath(id, 251);
            AutonomousBattlegroundExit.NoteDeath(id, 251);
            AutonomousBattlegroundExit.NoteDeath(id, 251);
            Assert.That(AutonomousBattlegroundExit.Observe(id, 251, true, 80, 0, 0, 0, false, t + 3 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.DeathStrikes));
        }

        [Test]
        public void Watch_LeavingTheBattlegroundDropsIt_AndADeathOutsideIsNotCounted()
        {
            const long id = 572_005;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.Observe(id, 253, true, 100, 0, 0, 0, false, t);
            AutonomousBattlegroundExit.NoteDeath(id, 253);
            AutonomousBattlegroundExit.Observe(id, 1, false, 100, 0, 0, 0, false, t + Minute);
            AutonomousBattlegroundExit.NoteDeath(id, 1);
            Assert.That(AutonomousBattlegroundExit.DeathsWithoutGain(id), Is.EqualTo(0));
            Assert.That(AutonomousBattlegroundExit.Observe(id, 253, true, 100, 0, 0, 0, false, t + 30 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.None), "a new visit starts a fresh watch");
        }

        [Test]
        public void Touch_AProtectedHoldRestartsTheClock()
        {
            const long id = 572_006;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.Observe(id, 252, true, 100, 0, 0, 0, false, t);
            AutonomousBattlegroundExit.Touch(id, t + 18 * Minute);
            Assert.That(AutonomousBattlegroundExit.Observe(id, 252, true, 100, 0, 0, 0, false, t + 25 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.None));
        }

        [Test]
        public void ReentryBlock_ThirtyMinutes_PerBot()
        {
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.BlockReentry(572_007, t);
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(572_007, t + Minute), Is.True);
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(572_007, t + 30 * Minute - 1), Is.True);
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(572_007, t + 30 * Minute), Is.False);
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(572_008, t + Minute), Is.False, "another bot");
        }

        [Test]
        public void Watchdog_ABattlegroundTourInsideIsExemptFromTheFortyFiveMinuteClock()
        {
            Assert.That(AutonomousStuckWatchdog.RoamsLikeRvr(false, true, true), Is.True);
            Assert.That(AutonomousStuckWatchdog.RoamsLikeRvr(false, true, false), Is.False, "on the way in the clock stays");
            Assert.That(AutonomousStuckWatchdog.RoamsLikeRvr(true, false, false), Is.True, "frontier RvR as before");
            Assert.That(AutonomousStuckWatchdog.RoamsLikeRvr(false, false, true), Is.False, "a grinder in a battleground keeps it");
        }
    }
}
