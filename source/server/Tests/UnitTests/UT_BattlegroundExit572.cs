using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #572 step 4: how an autonomous gamebot leaves a battleground,
    /// the backstops inside one (20 minutes without progress, 3 deaths without a gain) and the 30-minute re-entry block
    /// after a forced exit. Owner's answer 51B: a gamebot that out-levels the battleground it is in stays until it dies,
    /// then leaves, and the level cap keeps it out. Tests only.
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
            Assert.That(AutonomousBattlegroundExit.LeaveStarted(10_000, 15_000, 15_000 + 4 * 60_000), Is.EqualTo(10_000),
                "a 4-minute fight on the way out keeps the leave clock running");
            Assert.That(AutonomousBattlegroundExit.LeaveStarted(10_000, 15_000, 15_000 + 5 * 60_000 + 1), Is.EqualTo(15_000 + 5 * 60_000 + 1),
                "more than 5 minutes since the last call");
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

        private const ushort Thidranki = 252; // levels 20-24
        private const ushort Murdaigean = 251; // levels 25-29

        private static AutonomousBattlegroundExit.Sighting Seen(ushort region, long at) => new(region, at);

        [Test]
        public void Sighting_StartsOnlyInBracket_CarriesOverWhileAliveInTheSameBattleground()
        {
            long t = 1_000 * Minute;
            var none = default(AutonomousBattlegroundExit.Sighting);
            Assert.That(AutonomousBattlegroundExit.NextSighting(none, Thidranki, true, true, true, t), Is.EqualTo(Seen(Thidranki, t)),
                "alive and in bracket: seen");
            Assert.That(AutonomousBattlegroundExit.NextSighting(none, Thidranki, true, true, false, t).RegionId, Is.EqualTo(0),
                "out-levelled with no in-bracket sighting (a login there): no stay");
            Assert.That(AutonomousBattlegroundExit.NextSighting(Seen(Thidranki, t), Thidranki, true, true, false, t + Minute),
                Is.EqualTo(Seen(Thidranki, t + Minute)), "out-levelled on the same life: carried over");
            Assert.That(AutonomousBattlegroundExit.NextSighting(Seen(Thidranki, t), Thidranki, true, false, false, t + Minute).RegionId,
                Is.EqualTo(0), "dead: ended");
            Assert.That(AutonomousBattlegroundExit.NextSighting(Seen(Thidranki, t), 1, false, true, false, t + Minute).RegionId,
                Is.EqualTo(0), "out of the battlegrounds: ended");
            Assert.That(AutonomousBattlegroundExit.NextSighting(Seen(Thidranki, t), Murdaigean, true, true, false, t + Minute).RegionId,
                Is.EqualTo(0), "another battleground: ended");
            Assert.That(AutonomousBattlegroundExit.NextSighting(Seen(Thidranki, t), Thidranki, true, true, false,
                t + AutonomousBattlegroundExit.SightingGapMilliseconds + 1).RegionId, Is.EqualTo(0), "a gap (a logout): ended");
        }

        [Test]
        public void StaysOutlevelled_OnlyAliveOverTheCapWithASightingThere_AndNeverInAPlayersCompany()
        {
            long t = 1_000 * Minute;
            BattlegroundBrackets.Bracket here = BattlegroundBrackets.ForRegion(Thidranki);
            var seen = Seen(Thidranki, t);
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(seen, Thidranki, true, false, 25, here), Is.True);
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(seen, Thidranki, true, false, 24, here), Is.False,
                "in bracket: no stay needed");
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(seen, Thidranki, false, false, 25, here), Is.False, "dead");
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(seen, Thidranki, true, true, 25, here), Is.False,
                "in a player's company: today's rules");
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(default, Thidranki, true, false, 25, here), Is.False,
                "no in-bracket sighting");
            Assert.That(AutonomousBattlegroundExit.StaysOutlevelled(Seen(Murdaigean, t), Thidranki, true, false, 25, here), Is.False,
                "seen in another battleground");
        }

        [Test]
        public void ObserveStay_StaysAfterTheLevelUp_UntilItDies_ThenLeavesForGood()
        {
            const long id = 572_101;
            long t = 1_000 * Minute;
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 24, true, false, t), Is.False, "in bracket");
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, false, t + Minute), Is.True,
                "levelled past the cap inside: stays");
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, false, t + 40 * Minute), Is.False,
                "unseen for longer than the gap");
            AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 24, true, false, t + 41 * Minute);
            for (int minute = 42; minute < 100; minute++)
                Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, false, t + minute * Minute), Is.True,
                    "a long stay, looked at every minute");
            AutonomousBattlegroundExit.NoteDeath(id, Thidranki);
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, false, t + 100 * Minute), Is.False,
                "released alive inside after its death: it leaves");
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, false, t + 101 * Minute), Is.False,
                "and the stay does not come back");
        }

        [Test]
        public void ObserveStay_ADeadLookEndsIt_AndLeavingEndsIt()
        {
            const long dies = 572_102, leaves = 572_103;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.ObserveStay(dies, Thidranki, 24, true, false, t);
            Assert.That(AutonomousBattlegroundExit.ObserveStay(dies, Thidranki, 25, false, false, t + Minute), Is.False, "dead");
            Assert.That(AutonomousBattlegroundExit.ObserveStay(dies, Thidranki, 25, true, false, t + 2 * Minute), Is.False,
                "a death seen by the controller alone ends it too");

            AutonomousBattlegroundExit.ObserveStay(leaves, Thidranki, 24, true, false, t);
            AutonomousBattlegroundExit.ObserveStay(leaves, 1, 25, true, false, t + Minute);
            Assert.That(AutonomousBattlegroundExit.ObserveStay(leaves, Thidranki, 25, true, false, t + 2 * Minute), Is.False,
                "back inside after leaving (by any way): no stay");
        }

        [Test]
        public void ObserveStay_InAPlayersCompany_KeepsTodaysRules()
        {
            const long id = 572_104;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 24, true, true, t);
            Assert.That(AutonomousBattlegroundExit.ObserveStay(id, Thidranki, 25, true, true, t + Minute), Is.False);
        }

        [Test]
        public void WorkingBracket_TheOneItStandsInWhileItStays_ItsOwnOtherwise_AndEntryKeepsTheCap()
        {
            Assert.That(AutonomousBattlegroundExit.WorkingBracket(true, Thidranki, 25)?.RegionId, Is.EqualTo(Thidranki));
            Assert.That(AutonomousBattlegroundExit.WorkingBracket(false, Thidranki, 25)?.RegionId, Is.EqualTo(Murdaigean),
                "after its death: its own bracket's battleground");
            Assert.That(AutonomousBattlegroundExit.WorkingBracket(true, 250, 36)?.RegionId, Is.EqualTo((ushort)250),
                "above every bracket, staying in Caledonia");
            Assert.That(AutonomousBattlegroundExit.WorkingBracket(false, 250, 36), Is.Null, "above every bracket: tour ends");
            BattlegroundBrackets.Bracket thidranki = BattlegroundBrackets.ForRegion(Thidranki);
            Assert.That(BattlegroundBrackets.Allows(thidranki, 25), Is.False, "entry still refused to the out-levelled bot");
            Assert.That(AutonomousBattlegroundExit.MayBeIn(thidranki, 25, true), Is.True, "a party member staying there");
            Assert.That(AutonomousBattlegroundExit.MayBeIn(thidranki, 25, false), Is.False, "one outside");
            Assert.That(AutonomousBattlegroundExit.MayBeIn(thidranki, 22, false), Is.True);
        }

        [Test]
        public void ABotThatStays_StillMeetsTheTwentyMinuteAndThreeDeathRules()
        {
            const long idle = 572_105, dying = 572_106;
            long t = 1_000 * Minute;
            AutonomousBattlegroundExit.ObserveStay(idle, Thidranki, 24, true, false, t);
            AutonomousBattlegroundExit.Observe(idle, Thidranki, true, 100, 0, 0, 0, false, t);
            Assert.That(AutonomousBattlegroundExit.ObserveStay(idle, Thidranki, 25, true, false, t + Minute), Is.True);
            Assert.That(AutonomousBattlegroundExit.Observe(idle, Thidranki, true, 100, 0, 0, 0, false, t + 20 * Minute),
                Is.EqualTo(eBattlegroundForcedExit.NoProgress), "the 20-minute rule holds while it stays");

            AutonomousBattlegroundExit.Observe(dying, Thidranki, true, 100, 0, 0, 0, false, t);
            AutonomousBattlegroundExit.NoteDeath(dying, Thidranki);
            AutonomousBattlegroundExit.NoteDeath(dying, Thidranki);
            AutonomousBattlegroundExit.NoteDeath(dying, Thidranki);
            Assert.That(AutonomousBattlegroundExit.Observe(dying, Thidranki, true, 90, 0, 0, 0, false, t + Minute),
                Is.EqualTo(eBattlegroundForcedExit.DeathStrikes), "the 3-death rule is unchanged");
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
