using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidTetherReach;
using T = DOL.GS.EpicEncounterTactics;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Sweep 6f (owner's PC log, build b1b6846, 2026-11-18, 15:15-16:46): return waves rebased onto their catching-up raiders
    /// at a step none of them stood at, which then waited silently and were never counted as stuck (W-2: Tur Suil's wave 1
    /// from 15:38:03, Trollheim's wave 4 from 15:10:54), and #358's home-radius muster cap moving the Summoner's Hall summoners'
    /// musters from 900 to 700 from their spawns. Pure rules: no world tick, combat or pathing runs. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidSweep6f
    {
        private const float Preferred = RealmRaidDungeonRoute.FinalMusterStandOff;

        // ---- W-2: the rebase ----------------------------------------------------------------------------------------------

        [Test]
        public void ARebasePlacesTheWaveAtTheFurthestStepACatcherStandsAtSoItsCoreStandsThereAndFights()
        {
            // a and b walk to step 10 and stand at step 9; c walks to 4 and stands at 3.
            var catching = new Dictionary<long, int> { [1] = 10, [2] = 10, [3] = 4 };
            var standing = new Dictionary<long, int> { [1] = 9, [2] = 9, [3] = 3 };
            int step = W.Rebase(catching, standing);
            Assert.Multiple(() =>
            {
                Assert.That(step, Is.EqualTo(9), "the step a and b stand at, not the one they walk to (build b1b6846: 10)");
                Assert.That(catching, Is.EqualTo(new Dictionary<long, int> { [3] = 4 }), "a and b are the wave's core; c still catches up");
                // Both of the core stand at the new step: the wave stands together and fights what is in its way.
                Assert.That(W.Walk(2, 2, 0, monsterAhead: true, lastStep: false, bodyCanCome: false, bodyComing: false),
                    Is.EqualTo(W.WaveMove.Fight));
                // Build b1b6846 put it at 10, where none of its core stood: it waited, before it looked at the monster.
                Assert.That(W.Walk(2, 0, 0, monsterAhead: true, lastStep: false, bodyCanCome: false, bodyComing: false),
                    Is.EqualTo(W.WaveMove.Wait));
            });
        }

        [Test]
        public void ARebaseWithoutAKnownStepUsesTheStepBeforeTheOneEachWalksToAndPullsFurtherCatchersBack()
        {
            // No position known (out of the region): each stands at the step before the one it walks to.
            var catching = new Dictionary<long, int> { [1] = 10, [2] = 10, [3] = 4 };
            Assert.That(W.Rebase(catching), Is.EqualTo(9));
            Assert.That(catching, Is.EqualTo(new Dictionary<long, int> { [3] = 4 }));
            // One walking to 22 but standing at 5, another standing at 15: the wave is at 15, and the one further back walks to
            // at most 15 (it joins the core there).
            catching = new Dictionary<long, int> { [1] = 22, [2] = 16, [3] = 6 };
            var standing = new Dictionary<long, int> { [1] = 5, [2] = 15, [3] = -1 };
            Assert.That(W.Rebase(catching, standing), Is.EqualTo(15));
            Assert.That(catching, Is.EqualTo(new Dictionary<long, int> { [1] = 15, [3] = 6 }));
            Assert.That(W.CatchUp(catching[1], 5, 15), Is.EqualTo(-1), "it walks with the wave from there");
            Assert.That(W.Rebase(new Dictionary<long, int>(), standing), Is.EqualTo(-1), "nobody catching up: the walk ends");
        }

        // ---- W-2: a waiting wave counts as stuck, is helped and is released -------------------------------------------------

        [Test]
        public void ARebasedWaveWhoseCoreStandsAtNoStepIsStuckAfterFourMinutesEvenWhileCatchersMove()
        {
            const long pulse = 5_000;
            long rebased = 1_000_000, lastMove = rebased;
            int helps = 0;
            var lines = new List<string>();
            // 15 minutes: the core never gets closer and kills nothing; one catcher steps on every 30 s.
            for (long now = rebased + pulse; now <= rebased + 15 * 60_000; now += pulse)
            {
                bool catcherStepped = (now - rebased) % 30_000 == 0;
                if (W.Moved(coreCloser: false, targetHit: false, headway: false, catcherStepped)) lastMove = now;
                Assert.That(W.Walk(19, 0, 0, monsterAhead: true, lastStep: false, bodyCanCome: false, bodyComing: false),
                    Is.EqualTo(W.WaveMove.Wait), "its core stands at no step");
                if (!W.Stuck(lastMove, now)) continue;
                W.Help help = W.ChooseHelp(waitingOutside: 3, bodyCanCome: false, helps);
                if (W.ReleasesStuckWave(help)) { lines.Add($"released {(now - rebased) / 60_000}"); break; }
                lines.Add($"stuck {(now - rebased) / 60_000}");
                helps++;
                lastMove = now;
            }
            Assert.That(lines, Is.EqualTo(new[] { "stuck 4", "stuck 8", "released 12" }),
                "build b1b6846: the catchers' steps kept it 'moving' (no stuck line in 27.5 minutes at Tur Suil)");
            Assert.That(W.Moved(true, false, false, false), Is.True, "its own raiders closer is movement");
            Assert.That(W.Moved(false, true, false, false), Is.True, "damage on its monster is movement");
            Assert.That(W.Moved(false, false, true, false), Is.True, "headway is movement");
            // A stuck wave no longer holds the muster it walks to (sweep 6e, T-1).
            var stuck = new RealmRaidDungeonRoute.WaveWalking(new(1000, 1000, 0), rebased, 19, 1);
            Assert.That(RealmRaidDungeonRoute.MusterWaitsForWave(new(1000, 1000, 0), stuck, rebased + W.StuckMilliseconds), Is.False);
        }

        [Test]
        public void AWaitingWaveLogsAWaitingLineAtMostEveryTwoMinutes()
        {
            long since = 500_000;
            Assert.That(W.WaitingLogDue(0, 0, since + 10 * 60_000), Is.False, "not waiting");
            Assert.That(W.WaitingLogDue(since, 0, since + 119_000), Is.False, "a short wait at a step says nothing");
            int lines = 0;
            long logged = 0;
            // Tur Suil's silent 27.5 minutes, 15:38:03 to 16:05:35, pulsed every 5 s.
            for (long now = since; now <= since + 27 * 60_000 + 30_000; now += 5_000)
                if (W.WaitingLogDue(since, logged, now)) { logged = now; lines++; }
            Assert.That(lines, Is.EqualTo(13), "one line every two minutes, never a silent ten");
        }

        // ---- the #358 side effect: the summoners' musters -----------------------------------------------------------------

        /// <summary>#358's muster reach (integration 524cfff7f), for the bosses this fix must leave unchanged.</summary>
        private static int Reach358(int tether, int home)
        {
            int reach = R.DamageReach(tether);
            if (home <= 0) return reach;
            int capped = Math.Max(1, home - R.HomeInset);
            return reach > 0 ? Math.Min(reach, capped) : capped;
        }

        private static int Tether(string boss) => T.Leashes.First(l => l.Boss == boss).TetherRange;
        private static int Home(string boss) => T.For(boss)?.FightAtHomeRadius ?? 0;

        [Test]
        public void TheSummonersMusterStaysAt900OutsideTheirAggroAndAidonKeeps800()
        {
            Assert.Multiple(() =>
            {
                Assert.That(R.MusterStandOff(Preferred, 600, 1300, 1000), Is.EqualTo(900f), "a summoner: home 1,000 is her damage reach");
                Assert.That(R.MusterStandOff(Preferred, 600, 1500, 1100), Is.EqualTo(800f), "Aidon: #358 unchanged");
                Assert.That(Reach358(1300, 1000), Is.EqualTo(800), "#358 capped the summoners at 800...");
                Assert.That(R.MusterStandOff(Preferred, 600, Reach358(1300, 1000)), Is.EqualTo(700f), "...a muster 100 outside their aggro");
                foreach (string boss in new[] { "SummonerCunovinda", "SummonerRoesia", "SummonerLossren" })
                {
                    Assert.That(Tether(boss), Is.EqualTo(1300), boss);
                    Assert.That(Home(boss), Is.EqualTo(1000), boss);
                    Assert.That(R.MusterReach(Tether(boss), Home(boss), 600), Is.EqualTo(1000), $"{boss}: her damage reach");
                    float standOff = R.MusterStandOff(Preferred, 600, Tether(boss), Home(boss));
                    Assert.That(standOff, Is.EqualTo(900f), $"{boss}: as on b1b6846");
                    Assert.That(standOff, Is.GreaterThanOrEqualTo(600 + R.AggroMargin), $"{boss}: aggro + 300 (the guard)");
                }
                Assert.That(R.MusterStandOff(Preferred, 600, Tether("AidonTheArchwizard"), Home("AidonTheArchwizard")), Is.EqualTo(800f));
                Assert.That(R.MusterReach(Tether("AidonTheArchwizard"), Home("AidonTheArchwizard"), 600), Is.EqualTo(900));
                // A home at the damage reach still caps when the capped muster clears a small aggro by 150 or more.
                Assert.That(R.MusterReach(1300, 1000, 400), Is.EqualTo(800));
                Assert.That(R.MusterReach(1300, 1000, 550), Is.EqualTo(800), "700 is exactly 550 + 150");
                Assert.That(R.MusterReach(1300, 1000, 551), Is.EqualTo(1000));
            });
        }

        [Test]
        public void KulaTuscarCaerSidiAndEveryOtherHomeRadiusBossKeepTheirMusterFrom358()
        {
            Assert.Multiple(() =>
            {
                foreach (string boss in new[] { "QueenKula", "KingTuscar", "Fames", "Bellum", "Morbus", "Funus" })
                {
                    Assert.That(Home(boss), Is.GreaterThan(0), boss);
                    Assert.That(R.MusterReach(Tether(boss), Home(boss), 600), Is.EqualTo(Reach358(Tether(boss), Home(boss))), boss);
                    Assert.That(R.MusterStandOff(Preferred, 600, Tether(boss), Home(boss)),
                        Is.EqualTo(R.MusterStandOff(Preferred, 600, Reach358(Tether(boss), Home(boss)))), boss);
                }
                Assert.That(R.MusterStandOff(Preferred, 600, Tether("Fames"), Home("Fames")), Is.EqualTo(Preferred), "Caer Sidi: 1,400");
                // The whole catalog at aggro 600: only the three summoners move (back to their damage reach).
                var changed = T.Leashes.Select(l => l.Boss).Distinct()
                    .Where(b => Home(b) > 0 && R.MusterReach(Tether(b), Home(b), 600) != Reach358(Tether(b), Home(b)))
                    .OrderBy(b => b, StringComparer.Ordinal).ToArray();
                Assert.That(changed, Is.EqualTo(new[] { "SummonerCunovinda", "SummonerLossren", "SummonerRoesia" }));
            });
        }
    }
}

