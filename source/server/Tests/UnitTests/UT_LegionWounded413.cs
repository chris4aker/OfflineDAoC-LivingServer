using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// #413 (1.2.1, tests only: nothing here was played in game). Legion's wounded rule sent a raider under 35% straight away
    /// from him to 1,700. His raid's healers stand 1,200 from him on the raid's side (the 1.2 Legion plan's ranged line,
    /// LegionRangedSpot), so a melee raider wounded on his far side stood 2,500-2,900 from every healer, past the 2,000 a
    /// heal reaches: nobody healed it, his graspering souls (aggro 1,500) found it alone, and its death gave him 2.5% back.
    /// That is what beat Hibernia on the owner's PC (e3b8238: two tries, his lowest 92% and 60%, kill heals undoing the
    /// raid's work); the plan since then is tests only, and this hole was in it. Now the wounded step out toward the raid's
    /// side, about 500 behind the healers' line.
    /// <para>
    /// The geometry is the real code (LegionWoundedAdvice and LegionWoundedSpot, LegionRangedSpot, PointAway for the 1.2
    /// rule, LegionRaidSide and his lair). The fight is a model: 64 raiders (16 healers, 32 melee around him at 400, 16 at
    /// range), 9 damage a second each on his 300,000, one melee raider wounded per five minutes each; a wounded raider within
    /// 2,000 of a healer is back in 8 s, one out of reach waits two minutes and meanwhile is found by a soul at 1 in 60 a
    /// second and dies (each death 2.5% back for him, script and flood guard: at most 64 in five minutes); the dead are out
    /// three minutes. What it can't show: his real hits, the souls' real waves, his throw, terrain and the bots' real heals.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UT_LegionWounded413
    {
        private const int HealReach = 2_000;
        private static readonly Vector3 Boss = T.LegionLairCenter;

        private static Vector3[] HealerLine() =>
            Enumerable.Range(0, T.LegionRangedSlots).Select(id => T.LegionRangedSpot(Boss, T.LegionRaidSide, id, Boss.Z)).ToArray();

        private static float ToNearestHealer(Vector3 at, Vector3[] healers) =>
            healers.Min(h => Vector2.Distance(new(at.X, at.Y), new(h.X, h.Y)));

        private static Vector3 Ring(int bearing) =>
            new(Boss.X + 400 * MathF.Cos(bearing * MathF.PI / 180), Boss.Y + 400 * MathF.Sin(bearing * MathF.PI / 180), Boss.Z);

        /// <summary>The 1.2 rule's spot (pure): 1,700 straight away from him.</summary>
        private static Vector3 OldSpot(Vector3 raider) => T.PointAway(raider, Boss, T.LegionWoundedStandOff);

        [Test]
        public void AWoundedRaiderStepsOutWhereTheHealersReachIt()
        {
            Vector3[] healers = HealerLine();
            int[] bearings = Enumerable.Range(0, 72).Select(i => i * 5).ToArray();
            float[] old = bearings.Select(b => ToNearestHealer(OldSpot(Ring(b)), healers)).ToArray();
            float[] now = bearings.Select(b => ToNearestHealer(T.LegionWoundedSpot(Boss, T.LegionRaidSide, b, Boss.Z), healers)).ToArray();
            int oldOut = old.Count(d => d > HealReach);
            TestContext.Out.WriteLine($"1.2 rule: {oldOut} of {bearings.Length} bearings out of heal reach (farthest {old.Max():0}); " +
                                      $"#413: farthest {now.Max():0}");
            Assert.Multiple(() =>
            {
                Assert.That(oldOut, Is.GreaterThanOrEqualTo(bearings.Length / 3), "1.2: his far side was out of every healer's reach");
                Assert.That(old.Max(), Is.GreaterThan(2_500));
                Assert.That(now.Max(), Is.LessThan(700), "#413: about 500 behind the healers' line");
                foreach (int b in bearings)
                {
                    Vector3 spot = T.LegionWoundedSpot(Boss, T.LegionRaidSide, b, Boss.Z);
                    Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(Boss.X, Boss.Y)), Is.EqualTo(T.LegionWoundedStandOff).Within(1),
                        "still past his 1,400 close range");
                }
            });
        }

        [Test]
        public void TheSimulatedFightLosesToKillHealsOnTheOldRuleAndWinsOnTheNew()
        {
            const int Runs = 200;
            var old = Many(fixedRule: false, Runs);
            var now = Many(fixedRule: true, Runs);
            TestContext.Out.WriteLine($"1.2 rule: killed in 20 min {old.Killed20:0}%, mean {old.Minutes:0.0} min, kill heals {old.KillHeals:0.0}; " +
                                      $"#413: killed in 20 min {now.Killed20:0}%, mean {now.Minutes:0.0} min, kill heals {now.KillHeals:0.0}");
            Assert.Multiple(() =>
            {
                Assert.That(old.KillHeals, Is.GreaterThan(10), "1.2: the unreachable wounded die and heal him");
                Assert.That(now.KillHeals, Is.LessThan(1), "#413: the wounded are healed back");
                Assert.That(now.Killed20, Is.GreaterThanOrEqualTo(95), "#413: killed inside 20 minutes");
                Assert.That(now.Minutes, Is.LessThan(old.Minutes - 2), "#413: faster");
            });
        }

        private readonly record struct Outcome(double Killed20, double Minutes, double KillHeals);

        private static Outcome Many(bool fixedRule, int runs)
        {
            Vector3[] healers = HealerLine();
            int killed = 0;
            double minutes = 0, heals = 0;
            for (int seed = 0; seed < runs; seed++)
            {
                var rnd = new Random(seed * 104_729 + (fixedRule ? 1 : 0));
                const int Melee = 32, Others = 32, MaxHealth = 300_000;
                double health = MaxHealth;
                var outUntil = new long[Melee];
                var unreachable = new bool[Melee];
                int killHeals = 0;
                long t;
                for (t = 1; t <= 3_600; t++)
                {
                    int fighting = Others;
                    for (int i = 0; i < Melee; i++)
                    {
                        if (t < outUntil[i])
                        {
                            // Out of reach and wounded: a soul may find it alone.
                            if (unreachable[i] && rnd.NextDouble() < 1 / 60.0)
                            {
                                unreachable[i] = false;
                                outUntil[i] = t + 180;
                                if (killHeals < 64) { health = Math.Min(MaxHealth, health + MaxHealth / 40.0); killHeals++; }
                            }
                            continue;
                        }
                        unreachable[i] = false;
                        fighting++;
                        // The first melee raider is his target (the tank): it never steps out.
                        if (i > 0 && rnd.NextDouble() < 1 / 300.0)
                        {
                            int bearing = rnd.Next(360);
                            Vector3 spot = fixedRule ? T.LegionWoundedSpot(Boss, T.LegionRaidSide, i, Boss.Z) : OldSpot(Ring(bearing));
                            bool reach = ToNearestHealer(spot, healers) <= HealReach;
                            unreachable[i] = !reach;
                            outUntil[i] = t + (reach ? 8 : 120);
                        }
                    }
                    health -= 9.0 * fighting;
                    if (health <= 0) break;
                }
                heals += killHeals;
                if (health <= 0)
                {
                    minutes += t / 60.0;
                    if (t <= 1_200) killed++;
                }
                else minutes += 60;
            }
            return new(100.0 * killed / runs, minutes / runs, heals / runs);
        }
    }
}
