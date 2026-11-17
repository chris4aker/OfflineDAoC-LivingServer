using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, build 453e01a, 2026-11-17 (sweep 4, log to 14:00):
    /// <list type="bullet">
    /// <item>Tur Suil: the return wave (formed 12:37:20, 8 raiders, later 40) stood at its step 4 from 12:57 to past 14:00 and
    /// killed 216 balorians in the hall at 36,900-39,200 x 29,600-33,600 (aonghus 49, conall 50, einion 51: aggro 380/350,
    /// RespawnInterval 180 s, 32 spawns), one every ~20 s, while the hall refilled about ten a minute.</item>
    /// <item>Trollheim: a one-raider wave met the anpassa foranst room by the way in (about 15 spawns, levels 50-55, aggro 500,
    /// 180 s) at step 1 and called the main body (13:54:08); 5 joined from the way in (13:55:29).</item>
    /// </list>
    /// Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidReturnWaveFarm1117
    {
        private static readonly Vector3 TurSuilStep4Middle = new(38554, 30051, 12999);
        private const int BalorianAggro = 380;

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        [Test]
        public void WhatBlocksTheWayWhenTheWaveArrivesIsCleared()
        {
            // balorian conall at its spawn 38283,30479, on the walk: present when the wave reached its step.
            float d = Flat(TurSuilStep4Middle, new(38283, 30479, 12993));
            Assert.That(W.InTheWay(false, false, true, true, d, -6), Is.True);
            Assert.That(W.StillBlocks(false, false, 0, true, d, BalorianAggro), Is.True, "cleared before the wave goes on");
        }

        [Test]
        public void WhatComesBackWhileTheWaveStandsThereIsNotFarmed()
        {
            float d = Flat(TurSuilStep4Middle, new(38283, 30479, 12993));
            Assert.That(W.StillBlocks(false, true, 3, true, d, BalorianAggro), Is.False, "came back (180 s) while it stood there");
            Assert.That(W.StillBlocks(true, true, 3, true, d, BalorianAggro), Is.True, "unless it attacks the wave");
        }

        [Test]
        public void AnAggressiveMonsterOffTheWalkBeyondItsAggroIsNotInTheWay()
        {
            // balorian aonghus at 37826,30473: 841 from the wave, off the walk. The old rule (aggressive within 1,200) fought it.
            Vector3 aonghus = new(37826, 30473, 12993);
            float d = Flat(TurSuilStep4Middle, aonghus);
            Assert.That(d, Is.EqualTo(841).Within(2));
            Assert.That(W.InTheWay(false, false, false, true, d, -6), Is.True, "the old rule");
            Assert.That(W.StillBlocks(false, false, 0, false, d, BalorianAggro), Is.False, "380 + 300 < 841: it never comes to the wave");
            Assert.That(W.StillBlocks(false, false, 0, false, 600, BalorianAggro), Is.True, "within its aggro + 300: it would");
        }

        [Test]
        public void TwelveKillsAtAStepOnlyLetOffWalkMonstersGo()
        {
            Assert.That(W.StillBlocks(false, false, W.StepKillCap - 1, false, 600, BalorianAggro), Is.True, "off the walk, in reach");
            Assert.That(W.StillBlocks(false, false, W.StepKillCap, false, 600, BalorianAggro), Is.False, "the cap: off-walk ones no longer hold it");
            Assert.That(W.StillBlocks(false, false, W.StepKillCap, true, 300, BalorianAggro), Is.True,
                "an original blocker standing on the walk holds it until it is dead");
            Assert.That(W.StillBlocks(true, true, W.StepKillCap + 20, false, 900, BalorianAggro), Is.True,
                "one attacking the wave always holds it: never on mid-fight (re-review of 06b9cb2c0)");
        }

        /// <summary>The Tur Suil hall, replayed with the pure rules: 32 spawns on or by the walk, 180 s respawn, one kill every 20 s
        /// once the wave stands together. Before: always one in the way, never on. Now: on within the cap.</summary>
        [Test]
        public void TheTurSuilHallNoLongerHoldsTheWaveForEver()
        {
            const long Respawn = 180_000, KillEvery = 20_000, Hour = 3_600_000;
            int Run(bool newRule)
            {
                var spawned = Enumerable.Repeat(0L, 32).ToArray();
                var alive = Enumerable.Repeat(true, 32).ToArray();
                var died = new long[32];
                long entered = 1, now = 1;
                int kills = 0;
                for (; now < Hour; now += KillEvery)
                {
                    for (int i = 0; i < 32; i++)
                        if (!alive[i] && now - died[i] >= Respawn) { alive[i] = true; spawned[i] = now; }
                    int blocking = Array.FindIndex(alive, a => a);
                    if (newRule)
                        blocking = Enumerable.Range(0, 32).FirstOrDefault(i => alive[i] &&
                            W.StillBlocks(false, spawned[i] > entered, kills, true, 400, BalorianAggro), -1);
                    if (blocking < 0) return kills;
                    alive[blocking] = false;
                    died[blocking] = now;
                    kills++;
                }
                return -1;
            }
            Assert.That(Run(false), Is.EqualTo(-1), "the old rule: still at the step an hour later (216 kills by 14:00)");
            // The new rule: the 32 that stood there when it arrived, then on (about 11 minutes at one kill every 20 s).
            Assert.That(Run(true), Is.EqualTo(32));
        }

        [Test]
        public void TrollheimsOneRaiderWaveStillCallsTheBodyAndItsJoinersCatchUpAtOnce()
        {
            // Wave 3: 1 raider at step 1 of 28 by the anpassa foranst; a wave under 8 calls the main body (unchanged).
            Assert.That(W.Walk(1, 1, 0, true, false, true, false), Is.EqualTo(W.WaveMove.CallHelp));
            // The 5 who joined at 13:55:29 came from outside: step 1 (the way in), one leg behind the wave's step 2.
            Vector3[] steps = W.WalkSteps([new(28284, 47911, 16000), new(31000, 44000, 14637), new(33000, 40000, 14600)], new(33000, 40000, 14600));
            Assert.That(W.JoinStep(steps, default, false, 1), Is.EqualTo(0));
            Assert.That(W.CatchUp(0, 0, 1), Is.EqualTo(-1), "at the way in they are with the wave at once");
        }
    }
}
