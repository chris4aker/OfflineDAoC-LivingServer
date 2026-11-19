using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's decision, 2026-11-17: "Return waves clear together". Owner's PC (server-1117 log): Trollheim wiped at Nosdoden at
    /// 00:12:12; its return waves went in at the way in and then each raider walked on alone through trash that had come back,
    /// the raid went in again at 00:54 with 34 of 80, and the 60-minute stall clock ended it at 01:12:43 with the next muster at
    /// 0 of 56. Tur Suil's raid (opened by the game itself, build 19e7037) never had 8 on their way back, so they walked in one
    /// by one; Norsob's muster reached 13 of 20 while 13 fought respawned balorians on the walk, and the clock ended it at
    /// 04:33:21. Now a wave walks in as one group, fights what is in its way, is helped when stuck, and its headway counts on
    /// the stall clock. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidReturnWaveWalk
    {
        private const long Minute = 60_000;
        private const long Limit = RealmRaidRecruitmentPolicy.StalledMilliseconds;

        // ---- a wave gathers, then moves -------------------------------------------------------------------------------------

        [Test]
        public void AFewOnTheirWayBackGatherInsteadOfWalkingInOneByOne()
        {
            // Tur Suil: 7 at the way in. Before, too few for a wave: each walked straight in.
            Assert.That(W.Next(7, 0, 7, 0, false), Is.EqualTo(W.Step.Open), "the old rule");
            Assert.That(W.NextWalking(7, 0, 7, 0, false), Is.EqualTo(W.Step.Gather), "they wait for the corpse wait");
            Assert.That(W.NextWalking(7, 0, 7, W.SmallGraceMilliseconds, false), Is.EqualTo(W.Step.Go), "all there: they go together");
            Assert.That(W.NextWalking(2, 3, 2, W.SmallGraceMilliseconds, false), Is.EqualTo(W.Step.Gather), "three still down: wait for them");
            Assert.That(W.NextWalking(2, 3, 2, W.SmallPatienceMilliseconds, false), Is.EqualTo(W.Step.Go), "nobody waits long");
            Assert.That(W.NextWalking(1, 0, 0, 10 * Minute, false), Is.EqualTo(W.Step.Gather), "nobody at the way in yet");
            Assert.That(W.NextWalking(0, 0, 0, 0, false), Is.EqualTo(W.Step.Open), "nobody on the way back");
            Assert.That(W.NextWalking(3, 0, 3, 0, true), Is.EqualTo(W.Step.Open), "a wave just went: they follow it");
        }

        [Test]
        public void ABigReturnGathersAsBefore()
        {
            foreach ((int outside, int fallen, int waiting, long waited) in new[]
                     { (60, 0, 36, 2 * Minute), (60, 0, 35, 2 * Minute), (0, 60, 0, 0L), (60, 0, 8, 4 * Minute), (60, 0, 2, 8 * Minute) })
                Assert.That(W.NextWalking(outside, fallen, waiting, waited, false), Is.EqualTo(W.Next(outside, fallen, waiting, waited, false)));
        }

        [Test]
        public void TheWaveWaitsForMostOfItAtEachStepThenMoves()
        {
            Assert.That(W.Walk(20, 11, 0, false, false, false, false), Is.EqualTo(W.WaveMove.Wait), "11 of 20: not together");
            Assert.That(W.Walk(20, 12, 0, false, false, false, false), Is.EqualTo(W.WaveMove.Advance), "60%: it goes on");
            Assert.That(W.Walk(20, 7, W.StepPatienceMilliseconds - 1, false, false, false, false), Is.EqualTo(W.WaveMove.Wait));
            Assert.That(W.Walk(20, 7, W.StepPatienceMilliseconds, false, false, false, false), Is.EqualTo(W.WaveMove.Advance),
                "after two minutes a third goes on; the rest follow");
            Assert.That(W.Walk(20, 6, 10 * Minute, false, false, false, false), Is.EqualTo(W.WaveMove.Wait), "never a handful alone");
            Assert.That(W.Walk(20, 12, 0, false, true, false, false), Is.EqualTo(W.WaveMove.Arrived), "the last step is the raid");
            Assert.That(W.Walk(0, 0, 0, false, false, true, false), Is.EqualTo(W.WaveMove.Wait), "nobody left");
            Assert.That(W.Walk(1, 1, 0, false, false, false, false), Is.EqualTo(W.WaveMove.Advance), "one is together with itself");
        }

        [Test]
        public void TheWalkIsCutIntoStepsAlongThePathUpToWhereTheRaidStands()
        {
            Vector3[] walk = [new(0, 0, 0), new(3_000, 0, 0), new(3_000, 3_000, 0)];
            Vector3[] steps = W.WalkSteps(walk, new(3_000, 3_000, 0));
            Assert.That(steps[0], Is.EqualTo(walk[0]), "it starts where raiders come in");
            Assert.That(steps[^1], Is.EqualTo(walk[^1]));
            for (int i = 1; i < steps.Length; i++)
                Assert.That(Vector3.Distance(steps[i - 1], steps[i]), Is.LessThanOrEqualTo(W.StepUnits + 1f), $"step {i}");
            Assert.That(steps.Length, Is.EqualTo(6), "6,000 units of path in steps of 1,200");
            // The raid's posts beside the walk, before its end: the wave stops at the walk's nearest point, then goes to them.
            Vector3[] toMuster = W.WalkSteps(walk, new(3_400, 1_000, 0));
            Assert.That(toMuster[^1], Is.EqualTo(new Vector3(3_400, 1_000, 0)));
            Assert.That(toMuster[^2].X, Is.EqualTo(3_000).Within(1f), "it walks the path to the nearest point first");
            Assert.That(W.WalkSteps(null, new(5, 5, 5)), Is.EqualTo(new[] { new Vector3(5, 5, 5) }), "no walk: the goal");
        }

        // ---- it fights what came back on its route ---------------------------------------------------------------------------

        [Test]
        public void ItFightsTheMonstersInItsWayBeforeGoingOn()
        {
            Assert.That(W.Walk(20, 12, 0, true, false, false, false), Is.EqualTo(W.WaveMove.Fight));
            Assert.That(W.Walk(20, 12, 0, true, true, true, false), Is.EqualTo(W.WaveMove.Fight), "also at its last step");
            Assert.That(W.Walk(20, 5, 0, true, false, false, false), Is.EqualTo(W.WaveMove.Wait), "it pulls only once together");
        }

        [Test]
        public void InTheWayMeansOnTheWalkOrCloseAndAggressive()
        {
            Vector3[] leg = [new(0, 0, 0), new(1_200, 0, 0)];
            // A respawned balorian 500 off the walk with 400 aggro covers it (the route's own walk-back rule).
            Assert.That(RealmRaidDungeonRoute.OnWalkBack(leg, new(600, 500, 0), 400), Is.True);
            Assert.That(RealmRaidDungeonRoute.OnWalkBack(leg, new(600, 1_500, 0), 400), Is.False);
            Assert.That(W.InTheWay(false, false, true, true, 2_000, 0), Is.True, "on the walk ahead");
            Assert.That(W.InTheWay(false, false, true, true, W.LookAheadUnits + 1, 0), Is.False, "too far ahead yet");
            Assert.That(W.InTheWay(false, false, false, true, 1_000, 100), Is.True, "aggressive and close on its floor");
            Assert.That(W.InTheWay(false, false, false, true, 1_000, 800), Is.False, "another floor");
            Assert.That(W.InTheWay(false, false, false, false, 1_000, 0), Is.False, "a quiet one off the walk is left alone");
            Assert.That(W.InTheWay(true, false, true, true, 500, 0), Is.False, "never near a named: the raid's fight");
            Assert.That(W.InTheWay(false, true, true, true, 500, 0), Is.False, "one it could not damage is left");
            Assert.That(W.GiveUpTarget(0, W.TargetGiveUpMilliseconds - 1), Is.False);
            Assert.That(W.GiveUpTarget(0, W.TargetGiveUpMilliseconds), Is.True);
        }

        [Test]
        public void ASmallWaveCallsTheMainBodyRatherThanPullAlone()
        {
            Assert.That(W.Walk(5, 5, 0, true, false, true, false), Is.EqualTo(W.WaveMove.CallHelp));
            Assert.That(W.Walk(5, 5, 0, true, false, false, true), Is.EqualTo(W.WaveMove.CallHelp), "it holds while the body comes");
            Assert.That(W.Walk(5, 5, 0, true, false, false, false), Is.EqualTo(W.WaveMove.Fight), "no body to come: it fights");
            Assert.That(W.Walk(8, 8, 0, true, false, true, false), Is.EqualTo(W.WaveMove.Fight), "a party's worth clears it itself");
        }

        // ---- a stuck wave is detected and helped -----------------------------------------------------------------------------

        [Test]
        public void AStuckWaveIsDetectedAndHelped()
        {
            Assert.That(W.Stuck(0, W.StuckMilliseconds - 1), Is.False);
            Assert.That(W.Stuck(0, W.StuckMilliseconds), Is.True);
            Assert.That(W.ChooseHelp(7, true, 0), Is.EqualTo(W.Help.SendWaiting | W.Help.BodyMeets));
            Assert.That(W.ChooseHelp(7, false, 0), Is.EqualTo(W.Help.SendWaiting), "the raid is in an attempt: only the waiting are sent");
            Assert.That(W.ChooseHelp(0, true, 1), Is.EqualTo(W.Help.BodyMeets));
            Assert.That(W.ChooseHelp(0, false, 0), Is.EqualTo(W.Help.None));
            Assert.That(W.BodyCanCome(13, false), Is.True, "Tur Suil: 13 idle at Norsob's muster");
            Assert.That(W.BodyCanCome(13, true), Is.False, "never out of an attempt");
            Assert.That(W.BodyCanCome(W.MinimumWave - 1, false), Is.False);
            Assert.That(W.HelpersWanted(3), Is.EqualTo(W.MinimumWave));
            Assert.That(W.HelpersWanted(20), Is.EqualTo(20));
            Assert.That(W.HelpersWanted(80), Is.EqualTo(W.MaximumHelpers));
            Assert.That(W.Met(8, 16), Is.True);
            Assert.That(W.Met(7, 16), Is.False);
            Assert.That(W.Met(3, 3), Is.True, "all the helpers there");
            Assert.That(W.Met(0, 0), Is.False);
        }

        [Test]
        public void HelpIsLimitedThenTheStallClockDecides()
        {
            Assert.That(W.ChooseHelp(7, true, W.MaximumHelps - 1), Is.Not.EqualTo(W.Help.None));
            Assert.That(W.ChooseHelp(7, true, W.MaximumHelps), Is.EqualTo(W.Help.None));
        }

        // ---- the stall clock is fair ------------------------------------------------------------------------------------------

        [Test]
        public void HeadwayWhileClearingKeepsTheClockFair()
        {
            long progress = 10 * Minute;
            Assert.That(W.Stalled(Limit, progress + Limit, progress, 0), Is.True, "no wave: as before");
            Assert.That(W.Stalled(Limit, progress + Limit, progress, 0),
                Is.EqualTo(RealmRaidRecruitmentPolicy.Stalled(false, progress + Limit, progress)));
            Assert.That(W.Stalled(Limit, progress + 70 * Minute, progress, progress + 30 * Minute), Is.False, "a kill 40 minutes ago");
            Assert.That(W.Stalled(Limit, progress + 90 * Minute, progress, progress + 30 * Minute), Is.True, "60 minutes after the headway");
            Assert.That(W.Stalled(Limit, progress + 2 * Limit, progress, progress + 2 * Limit - Minute), Is.True,
                "never more than one limit past the last real progress");
            Assert.That(W.Stalled(RealmRaidRecruitmentPolicy.ForcedStalledMilliseconds, progress + 4 * 60 * Minute, progress, progress + 3 * 60 * Minute),
                Is.False, "a forced raid's 3 hours the same way");
        }

        [Test]
        public void AStuckWaveWithNoHeadwayStillEndsTheRaid()
        {
            // A wipe at T, the wave makes headway until T+10, then is stuck: helped twice with no headway, the clock ends it
            // 60 minutes after its last headway.
            long wipe = 100 * Minute, lastHeadway = wipe + 10 * Minute;
            long now = lastHeadway;
            int helps = 0;
            while (now < lastHeadway + Limit)
            {
                now += W.StuckMilliseconds;
                if (W.ChooseHelp(5, true, helps) != W.Help.None) helps++;
            }
            Assert.That(helps, Is.EqualTo(W.MaximumHelps));
            Assert.That(W.Stalled(Limit, lastHeadway + Limit - 1, wipe, lastHeadway), Is.False);
            Assert.That(W.Stalled(Limit, lastHeadway + Limit, wipe, lastHeadway), Is.True);
        }

        // ---- the PC log's raids, replayed as rule inputs ---------------------------------------------------------------------

        private static long At(int hour, int minute, int second = 0) => ((hour * 60L + minute) * 60 + second) * 1_000;

        [Test]
        public void TrollheimReplayed()
        {
            long wipe = At(0, 12, 12);
            // 00:13:03: 19 waiting, waited 110 s (REALM_RAID_RETURN_WAVE). The wave goes in, as it did.
            Assert.That(W.NextWalking(19, 0, 19, 110_000, false), Is.EqualTo(W.Step.Go));
            // Before: no headway counted, stalled at 01:12:12 (the log's 01:12:43 was the next pulse after the clock ran out).
            Assert.That(W.Stalled(Limit, At(1, 12, 43), wipe, 0), Is.True, "the log's ending");
            // Now: the wave walks the cleared route and kills what came back: headway through 00:40 (attempt 2 began at 00:54
            // with 34 of 80; the next muster sat at 0 of 56 when the clock ran out).
            long lastKill = At(0, 40);
            Assert.That(W.Stalled(Limit, At(1, 12, 43), wipe, lastKill), Is.False, "the raid is still making headway at 01:12:43");
            Assert.That(W.Stalled(Limit, At(1, 40), wipe, lastKill), Is.True, "with nothing more, it ends 60 minutes after the wave's last kill");
            Assert.That(W.Stalled(Limit, At(2, 12, 12), wipe, At(2, 10)), Is.True, "headway never stretches it past 2 hours after the wipe");
            // The 0/56 muster: a wave of 56 that stood together at its step goes on; one that met a respawn fights it.
            Assert.That(W.Walk(56, 34, 0, false, false, true, false), Is.EqualTo(W.WaveMove.Advance));
            Assert.That(W.Walk(56, 34, 0, true, false, true, false), Is.EqualTo(W.WaveMove.Fight));
            // 00:55:17 and 01:06:31 (2 of 8-12 on their way back, waited 990 and 540 s): they go as before and join the walk.
            Assert.That(W.NextWalking(3, 9, 2, 990_000, false), Is.EqualTo(W.Step.Go));
            Assert.That(W.NextWalking(2, 6, 2, 540_000, false), Is.EqualTo(W.Step.Go));
        }

        [Test]
        public void TurSuilReplayed()
        {
            // Started 02:33; the first-hour clear counted as progress to 03:33; stalled 04:33:21 (no named, no wipe).
            long clearEnd = At(3, 33, 21);
            Assert.That(W.Stalled(Limit, At(4, 33, 21), clearEnd, 0), Is.True, "the log's ending");
            // 7 at the way in: they gather (they walked in one by one before) and go together after the corpse wait.
            Assert.That(W.NextWalking(7, 0, 7, 0, false), Is.EqualTo(W.Step.Gather));
            Assert.That(W.NextWalking(7, 0, 7, W.SmallGraceMilliseconds, false), Is.EqualTo(W.Step.Go));
            // A wave of 7 meets respawned balorians: the 13 idle at Norsob's muster come back to meet it.
            bool bodyCanCome = W.BodyCanCome(13, raidFighting: false);
            Assert.That(W.Walk(7, 7, 0, true, false, bodyCanCome, false), Is.EqualTo(W.WaveMove.CallHelp));
            Assert.That(W.HelpersWanted(7), Is.EqualTo(8), "a party's worth at least");
            Assert.That(W.Met(8, 13), Is.True, "met: they go on to the muster together (20 of 20)");
            // The wave's kills are headway: at 04:33:21 the raid is not ended while it clears its way.
            Assert.That(W.Stalled(Limit, At(4, 33, 21), clearEnd, At(4, 20)), Is.False);
            Assert.That(W.Stalled(Limit, At(5, 20), clearEnd, At(4, 20)), Is.True);
        }
    }
}
