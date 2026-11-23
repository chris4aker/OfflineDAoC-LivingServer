using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// #415 (1.2.1, tests only: nothing here was played in game). Dragon fights fall apart partway: Golestandt lost after two
    /// wipes on 9a173f4 and c81eb5d (the second: 72 of 91 fell), Cuuldurach on c81eb5d and 43c43aa, while Gjalpinulva won on
    /// 88b6ab3, 9a173f4, c81eb5d and 43c43aa. The three scripts are the same code (only coordinates, names and damage types
    /// differ), so the difference is where things land: the wing-gust throw's spots (DragonEncounterThrow.Spots). Four of
    /// Golestandt's five spots and three of Cuuldurach's lie past the return wave's 6,700 line
    /// (RealmRaidReturnWave.DragonReturnDistance); one of Gjalpinulva's does. The return wave took a thrown raider there for
    /// one walking back from a release and, while a wave gathered, held it at the staging ring; a raider resurrected where
    /// it fell was held the same way. The raid then fought on short of the healers it needed and fell apart.
    /// <para>
    /// The simulation drives the real code for everything that decides who is held and when an attempt is lost:
    /// RealmRaidReturnWave.ClassifyDragonRaider (the 1.2 overload as "current", the #415 overload as "fixed") and .Next,
    /// RealmRaidDragonFight (attempts, wipes, regroups, defeat), DragonEncounterThrow's spots, picks and interval,
    /// DragonLairPlacement's lairs and EncounterDisplacementRecovery.AssumedSpeed. The fight itself is a model: a 72-raider
    /// raid (16 healers), the dragon's 300,000 health, a fixed damage per raider, raiders lost at a rate that grows as the
    /// healers at the fight fall under ten (twice the base rate from 50%, for the messengers' adds), resurrections where a
    /// raider fell, and releases walking back from a bind. Its death rate was set where Gjalpinulva's geometry on the
    /// current code wins most fights and Golestandt's and Cuuldurach's about half, the owner's PC pattern (Gjalpinulva 4 of 4
    /// on the candidate line, Golestandt 2 of 4, Cuuldurach 1 of 3); 15% less pressure and every geometry wins nearly always. What the model can't show:
    /// the dragons' real damage and the bots' real heals, the terrain and monsters on a thrown raider's walk, the messengers'
    /// own paths, and whether a held raider with the dragon still on its own aggro list stays to fight (here only healers
    /// and resurrected raiders, who have none, walk to the ring).
    /// </para>
    /// </summary>
    [TestFixture]
    public class UT_DragonThrowReturnWave415
    {
        private static readonly string[] Dragons = ["MidGjalpinulva", "AlbGolestandt", "HibCuuldurach"];

        private static eRealm RealmOf(string dragon) => dragon switch
        {
            "AlbGolestandt" => eRealm.Albion,
            "MidGjalpinulva" => eRealm.Midgard,
            _ => eRealm.Hibernia,
        };

        private static float FromLair(string dragon, DragonEncounterThrow.Spot s)
        {
            Point3D home = DragonLairPlacement.Home(RealmOf(dragon));
            return (float)Math.Sqrt((double)(s.X - home.X) * (s.X - home.X) + (double)(s.Y - home.Y) * (s.Y - home.Y));
        }

        // ---- 1.2.2 regression audit, items 4 and 5 ---------------------------------------------------------------------

        [TestCase(3_600f)]
        [TestCase(4_800f)]
        [TestCase(6_100f)]
        public void ARaiderWaitingAtTheStagingRingStaysReturning(float ring)
        {
            // Golestandt's and Cuuldurach's wins held a released raider at the ring until its wave went: still so.
            Assert.That(ring, Is.GreaterThan(W.DragonBackInFightDistance));
            Assert.That(W.ClassifyDragonRaider(true, true, true, ring), Is.EqualTo(W.DragonRaider.Returning), "1.2");
            Assert.That(W.ClassifyDragonRaider(true, true, true, ring, displaced: false), Is.EqualTo(W.DragonRaider.Returning));
            Assert.That(W.ClassifyDragonRaider(true, true, true, ring, displaced: true), Is.EqualTo(W.DragonRaider.Returning),
                "a raider already on its way back stays so, thrown or not");
        }

        [Test]
        public void TheDragonReturnWaveNeverRunsForAnOpenWorldRaid()
        {
            // PulseDragonReturnWave measured an open-world raid's raiders to its realm's dragon lair: none is ever Returning
            // there now, as in v1.2 (nobody was ever within that line).
            Assert.That(W.DragonWaveApplies(isOpenWorld: true), Is.False);
            Assert.That(W.DragonWaveApplies(isOpenWorld: false), Is.True);
        }

        [Test]
        public void TheThrowSpotsPastTheReturnLineAreWhereTheRaidsLose()
        {
            int Past(string dragon) => DragonEncounterThrow.Spots[dragon].Count(s => FromLair(dragon, s) > W.DragonReturnDistance);
            Assert.Multiple(() =>
            {
                Assert.That(Past("MidGjalpinulva"), Is.EqualTo(1), "Gjalpinulva: one spot (7,181) past 6,700");
                Assert.That(Past("AlbGolestandt"), Is.EqualTo(4), "Golestandt: 7,334, 8,140, 10,106 and 12,227");
                Assert.That(Past("HibCuuldurach"), Is.EqualTo(3), "Cuuldurach: 7,903, 8,403 and 10,855");
            });
        }

        [Test]
        public void AThrownOrResurrectedRaiderIsWithTheRaidNotHeldForAWave()
        {
            Assert.Multiple(() =>
            {
                // 1.2: a raider thrown 10,106 out (Golestandt's fifth spot) was "on its way back" and held for the wave.
                Assert.That(W.ClassifyDragonRaider(true, true, false, 10_106), Is.EqualTo(W.DragonRaider.Returning), "1.2: held");
                Assert.That(W.ClassifyDragonRaider(true, true, false, 10_106, displaced: true), Is.EqualTo(W.DragonRaider.WithTheRaid));
                // Resurrected where it fell (on the list as fallen): 1.2 held it at the ring; now it is back in the fight.
                Assert.That(W.ClassifyDragonRaider(true, true, true, 400), Is.EqualTo(W.DragonRaider.Returning), "1.2: held");
                Assert.That(W.ClassifyDragonRaider(true, true, true, 400, displaced: false), Is.EqualTo(W.DragonRaider.WithTheRaid));
                // Unchanged: released and walking back, waiting at the ring, down, never with the raid.
                Assert.That(W.ClassifyDragonRaider(true, true, true, 15_000, false), Is.EqualTo(W.DragonRaider.Returning));
                Assert.That(W.ClassifyDragonRaider(true, true, true, 4_500, false), Is.EqualTo(W.DragonRaider.Returning), "at its staging post");
                Assert.That(W.ClassifyDragonRaider(true, true, false, float.MaxValue, false), Is.EqualTo(W.DragonRaider.Returning));
                Assert.That(W.ClassifyDragonRaider(false, true, false, 400, true), Is.EqualTo(W.DragonRaider.Fallen));
                Assert.That(W.ClassifyDragonRaider(true, false, false, 20_000, true), Is.EqualTo(W.DragonRaider.NotYet));
                Assert.That(W.DragonBackInFightDistance, Is.LessThan(3_600), "inside the staging ring");
            });
        }

        [Test]
        public void TheSimulatedFightsReproduceThePcAndTheFixWinsThem()
        {
            const int Runs = 300;
            var current = Dragons.ToDictionary(d => d, d => Sim.Many(d, fixedCode: false, Runs));
            var fixedRuns = Dragons.ToDictionary(d => d, d => Sim.Many(d, fixedCode: true, Runs));
            foreach (string d in Dragons)
                TestContext.Out.WriteLine($"{d}: current win {current[d].WinPercent:0}% (held raider-minutes {current[d].HeldMinutes:0.0} a fight, " +
                                          $"mean {current[d].MeanMinutes:0.0} min) -> fixed win {fixedRuns[d].WinPercent:0}% " +
                                          $"(held {fixedRuns[d].HeldMinutes:0.0}, mean {fixedRuns[d].MeanMinutes:0.0} min)");
            Assert.Multiple(() =>
            {
                // The failure: on the current code thrown and resurrected raiders who never released are held at the ring,
                // and Golestandt's fight (four spots past the line) is lost far more often than Gjalpinulva's.
                Assert.That(current["AlbGolestandt"].HeldMinutes, Is.GreaterThan(current["MidGjalpinulva"].HeldMinutes));
                Assert.That(current["AlbGolestandt"].WinPercent, Is.LessThanOrEqualTo(current["MidGjalpinulva"].WinPercent - 15),
                    "current code: Golestandt is lost more often than Gjalpinulva (the PC's pattern)");
                foreach (string d in Dragons)
                {
                    Assert.That(fixedRuns[d].HeldMinutes, Is.LessThan(1), $"{d}: next to nobody alive and unreleased is held");
                    Assert.That(fixedRuns[d].HeldMinutes, Is.LessThan(current[d].HeldMinutes / 20), $"{d}: held far less than before");
                    Assert.That(fixedRuns[d].WinPercent, Is.GreaterThanOrEqualTo(current[d].WinPercent - 5), $"{d}: fixed no worse");
                    Assert.That(fixedRuns[d].WinPercent, Is.GreaterThanOrEqualTo(60), $"{d}: fixed");
                    Assert.That(fixedRuns[d].MeanMinutes, Is.LessThan(60), $"{d}: a won fight is well inside the 4-hour cap");
                }
                Assert.That(fixedRuns["AlbGolestandt"].WinPercent, Is.GreaterThanOrEqualTo(current["AlbGolestandt"].WinPercent + 10));
                Assert.That(fixedRuns["HibCuuldurach"].WinPercent, Is.GreaterThanOrEqualTo(current["HibCuuldurach"].WinPercent + 8));
            });
        }

        /// <summary>The fight model (see the class summary). One tick a second; the raid's rules every ten seconds.</summary>
        private sealed class Sim
        {
            public readonly record struct Result(double WinPercent, double MeanMinutes, double HeldMinutes);

            private const int Raiders = 72, Healers = 16, HealersNeeded = 10;
            private const double BasePerMinute = 3.8, NoHealersPerMinute = 10.0, DamagePerRaider = 4.0, RezChance = 0.4;
            private const int MaxHealth = 300_000, Ring = 4_850, FightDistance = 300;
            private const long CapSeconds = 4 * 3600;

            private enum S { Fight, Thrown, Dead, BindWalk, ToStage, Staged, WalkIn }

            public static Result Many(string dragon, bool fixedCode, int runs)
            {
                int wins = 0; double minutes = 0, held = 0;
                for (int seed = 0; seed < runs; seed++)
                {
                    var sim = new Sim(dragon, fixedCode, seed);
                    bool won = sim.Run(out long seconds);
                    held += sim._heldSeconds / 60.0;
                    if (won) { wins++; minutes += seconds / 60.0; }
                }
                return new(100.0 * wins / runs, wins == 0 ? 0 : minutes / wins, held / runs);
            }

            private readonly bool _fixed;
            private readonly Random _rnd;
            private readonly float[] _spots;
            private readonly S[] _st = new S[Raiders];
            private readonly double[] _t = new double[Raiders];
            private readonly double[] _dist = new double[Raiders];
            private readonly bool[] _returning = new bool[Raiders], _rezzed = new bool[Raiders], _rezDue = new bool[Raiders],
                _released = new bool[Raiders];
            private readonly long[] _displacedUntil = new long[Raiders];
            private double _heldSeconds;

            private Sim(string dragon, bool fixedCode, int seed)
            {
                _fixed = fixedCode;
                _rnd = new Random(seed * 7919 + dragon.Length);
                _spots = DragonEncounterThrow.Spots[dragon].Select(s => FromLair(dragon, s)).ToArray();
                for (int i = 0; i < Raiders; i++) { _dist[i] = FightDistance; _displacedUntil[i] = -1; }
            }

            private static bool IsHealer(int i) => i < Healers;

            private bool Run(out long now)
            {
                var fight = new RealmRaidDragonFight("dragon");
                double health = MaxHealth;
                bool combat = true;
                long emptySince = -1, nextThrow = _rnd.Next(DragonEncounterThrow.MinimumIntervalMilliseconds,
                    DragonEncounterThrow.MaximumIntervalMilliseconds + 1) / 1000;
                bool gathering = false, wasRegrouping = false;
                long waitSince = 0, openUntil = -1;
                fight.Engage(Enumerable.Range(0, Raiders).Select(i => (long)i), Dragon(combat, health), 0);
                for (now = 1; now < CapSeconds; now++)
                {
                    bool regrouping = fight.Regrouping;
                    if (regrouping)
                        for (int i = 0; i < Raiders; i++)
                            if (_st[i] is S.Fight or S.Thrown or S.WalkIn) { _st[i] = S.ToStage; _t[i] = Ring / (double)EncounterDisplacementRecovery.AssumedSpeed; }
                    Move(now, gathering, regrouping);
                    int[] at = Enumerable.Range(0, Raiders).Where(i => _st[i] == S.Fight).ToArray();
                    int healers = at.Count(IsHealer);
                    if (!combat && !regrouping && at.Length > 0) combat = true;
                    if (combat && !regrouping)
                    {
                        health -= DamagePerRaider * at.Count(i => !IsHealer(i));
                        if (health <= 0) return true;
                        double perMinute = BasePerMinute * (health < MaxHealth / 2.0 ? 2 : 1) +
                                           NoHealersPerMinute * Math.Max(0, HealersNeeded - healers) / HealersNeeded;
                        var alive = at.ToList();
                        for (int k = Poisson(perMinute / 60.0); k > 0 && alive.Count > 0; k--)
                        {
                            int v = alive[_rnd.Next(alive.Count)];
                            alive.Remove(v);
                            _st[v] = S.Dead;
                            _rezDue[v] = healers >= 4 && _rnd.NextDouble() < RezChance;
                            _t[v] = _rezDue[v] ? 40 : 60;
                        }
                        if (now >= nextThrow)
                        {
                            nextThrow = now + _rnd.Next(DragonEncounterThrow.MinimumIntervalMilliseconds,
                                DragonEncounterThrow.MaximumIntervalMilliseconds + 1) / 1000;
                            // Never its target (the first at the fight here).
                            var pool = Enumerable.Range(0, Raiders).Where(i => _st[i] == S.Fight).Skip(1).OrderBy(_ => _rnd.Next()).ToList();
                            int picks = _rnd.Next(DragonEncounterThrow.MinimumPicks, DragonEncounterThrow.MaximumPicks + 1);
                            foreach (int v in pool.Take(picks))
                            {
                                float d = _spots[_rnd.Next(_spots.Length)];
                                _st[v] = S.Thrown;
                                _dist[v] = d;
                                _t[v] = 2 + Math.Max(0, d - EncounterDisplacementRecovery.ArriveRadius) / EncounterDisplacementRecovery.AssumedSpeed;
                                _displacedUntil[v] = now + EncounterDisplacementRecovery.HealPriorityMilliseconds / 1000;
                                _released[v] = false;
                            }
                        }
                        if (!_st.Contains(S.Fight))
                        {
                            if (emptySince < 0) emptySince = now;
                            else if (now - emptySince >= 8) { combat = false; health = MaxHealth; }
                        }
                        else emptySince = -1;
                    }
                    if (now % 10 != 0) continue;
                    fight.Advance(Enumerable.Range(0, Raiders).Select(i => new RealmRaidDragonFight.Raider(i, _st[i] != S.Dead,
                        _st[i] == S.Fight, _st[i] == S.Staged)).ToList(), Dragon(combat, health), now * 1000);
                    if (fight.Defeated) return false;
                    if (fight.Regrouping) { wasRegrouping = true; gathering = false; continue; }
                    if (wasRegrouping)
                    {
                        // The raid goes in again from the ring (RealmRaidDragonFight.GoesInAgain).
                        wasRegrouping = false;
                        health = MaxHealth;
                        for (int i = 0; i < Raiders; i++)
                        {
                            _returning[i] = _rezzed[i] = _released[i] = false;
                            if (_st[i] == S.Staged) { _st[i] = S.WalkIn; _t[i] = Ring / (double)EncounterDisplacementRecovery.AssumedSpeed; }
                        }
                    }
                    gathering = ReturnWavePulse(now, ref waitSince, ref openUntil);
                }
                return false;
            }

            /// <summary>AutonomousRealmRaid.PulseDragonReturnWave, on the model's raiders. True while a wave gathers.</summary>
            private bool ReturnWavePulse(long now, ref long waitSince, ref long openUntil)
            {
                int outside = 0, fallen = 0, waiting = 0;
                for (int i = 0; i < Raiders; i++)
                {
                    bool alive = _st[i] != S.Dead;
                    bool displaced = _st[i] == S.Thrown || now <= _displacedUntil[i];
                    W.DragonRaider c = _fixed
                        ? W.ClassifyDragonRaider(alive, true, _returning[i], (float)_dist[i], displaced)
                        : W.ClassifyDragonRaider(alive, true, _returning[i], (float)_dist[i]);
                    if (c == W.DragonRaider.WithTheRaid) { _returning[i] = _rezzed[i] = _released[i] = false; }
                    else if (c == W.DragonRaider.Fallen) { _returning[i] = true; fallen++; }
                    else if (c == W.DragonRaider.Returning)
                    {
                        _returning[i] = true; outside++;
                        if (_st[i] == S.Staged) waiting++;
                    }
                }
                if (waiting == 0) waitSince = 0;
                else if (waitSince == 0) waitSince = now;
                long waited = waitSince == 0 ? 0 : (now - waitSince) * 1000;
                W.Step step = W.Next(outside, fallen, waiting, waited, now < openUntil);
                if (step == W.Step.Gather)
                {
                    // Held raiders with no aggro of their own walk to their staging post: healers, and the resurrected.
                    for (int i = 0; i < Raiders; i++)
                        if (_returning[i] && _st[i] is S.Fight or S.WalkIn && (IsHealer(i) || _rezzed[i]))
                        {
                            _st[i] = S.ToStage;
                            _t[i] = Math.Max(5, Ring - _dist[i]) / EncounterDisplacementRecovery.AssumedSpeed;
                        }
                    return true;
                }
                if (step == W.Step.Go) { openUntil = now + W.OpenMilliseconds / 1000; waitSince = 0; }
                for (int i = 0; i < Raiders; i++)
                    if (_returning[i] && _st[i] != S.Dead)
                    {
                        _returning[i] = _rezzed[i] = _released[i] = false;
                        if (_st[i] is S.Staged or S.ToStage) { _st[i] = S.WalkIn; _t[i] = Ring / (double)EncounterDisplacementRecovery.AssumedSpeed; }
                    }
                return false;
            }

            private void Move(long now, bool gathering, bool regrouping)
            {
                double step = EncounterDisplacementRecovery.AssumedSpeed;
                for (int i = 0; i < Raiders; i++)
                {
                    // Held: alive, never released (thrown or resurrected), standing at or walking to the ring for a wave.
                    if (!regrouping && _returning[i] && !_released[i] && _st[i] is S.ToStage or S.Staged) _heldSeconds++;
                    switch (_st[i])
                    {
                        case S.Thrown:
                        case S.WalkIn:
                            _dist[i] = Math.Max(FightDistance, _dist[i] - step);
                            if (--_t[i] <= 0) { _st[i] = S.Fight; _dist[i] = FightDistance; }
                            break;
                        case S.ToStage:
                            if (--_t[i] <= 0) { _st[i] = S.Staged; _dist[i] = Ring; }
                            break;
                        case S.BindWalk:
                            _dist[i] = Math.Max(Ring, _dist[i] - step);
                            if (--_t[i] <= 0)
                            {
                                if (regrouping || gathering && _returning[i] && IsHealer(i)) { _st[i] = S.Staged; _dist[i] = Ring; }
                                else { _st[i] = S.WalkIn; _t[i] = Ring / step; _dist[i] = Ring; }
                            }
                            break;
                        case S.Dead:
                            if (--_t[i] <= 0)
                            {
                                if (_rezDue[i]) { _st[i] = S.Fight; _dist[i] = FightDistance; _rezzed[i] = true; }
                                else { _st[i] = S.BindWalk; _t[i] = 120 + _rnd.NextDouble() * 120; _dist[i] = 9_000; _rezzed[i] = false; _released[i] = true; }
                                _rezDue[i] = false;
                            }
                            break;
                    }
                }
            }

            private int Poisson(double lambda)
            {
                double l = Math.Exp(-lambda), p = 1;
                int k = 0;
                do { k++; p *= _rnd.NextDouble(); } while (p > l);
                return k - 1;
            }

            private static RealmRaidDragonFight.Dragon Dragon(bool combat, double health) =>
                new(combat, true, false, (int)Math.Ceiling(100 * Math.Max(0, health) / MaxHealth), (int)Math.Max(0, health));
        }
    }
}
