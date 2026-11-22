using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.1 raid simulation pass (owner 2026-11-22: "Do the simulation"), issues #410 (Summoner Roesia, Grand Summoner
    /// Govannon) and #412 (the Green Knight, Marfoirwort). Deterministic models of the fights, built on the bosses' own
    /// numbers (read from their scripts here) and the bots' own decision functions (RaidCurePoison.Decide and InDanger,
    /// EpicEncounterTactics.LeechMeleeSpot, LeechRangeSpot, SpreadSpot and GreenKnightMeleeTree), calibrated where the
    /// owner's PC logs give a number. Everything else is an assumption, named where it is used. Tests only: nothing here
    /// has been seen in a PC log. Each model uses its own seeded generator (<see cref="Rng"/>) so the numbers in the PR and
    /// the issues are the numbers these tests compute.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidSim121SummonersGreenKnight
    {
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp() => _server = new EpicTestServerScope();

        [TearDown]
        public void TearDown() => _server.Dispose();

        /// <summary>A small seeded generator (64-bit LCG), the same in every run and on every machine.</summary>
        public sealed class Rng
        {
            private ulong _x;
            public Rng(int seed) => _x = unchecked((ulong)seed * 2654435761UL + 12345UL);
            public double Next()
            {
                _x = unchecked(_x * 6364136223846793005UL + 1442695040888963407UL);
                return (_x >> 11) * (1.0 / (1UL << 53));
            }
            public int Below(int n) => Math.Min(n - 1, (int)(Next() * n));
        }

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static Spell RoesiaSpell(string property)
        {
            var brain = (SummonerRoesiaBrain)RuntimeHelpers.GetUninitializedObject(typeof(SummonerRoesiaBrain));
            return (Spell)typeof(SummonerRoesiaBrain).GetProperty(property, Hidden)!.GetValue(brain);
        }

        private static int MaxHealthOf<TNpc>() where TNpc : GameNPC => ((TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc))).MaxHealth;

        // =============================================================================================================
        // #410 Summoner Roesia
        // =============================================================================================================

        [Test]
        public void TheRoesiaModelUsesHerOwnSpellsAndHealth()
        {
            Spell pain = RoesiaSpell("RoesiaDot"), hot = RoesiaSpell("RoesiaHOT"), ds = RoesiaSpell("RoesiaDS");
            Assert.That((pain.Name, pain.Damage, pain.Frequency, pain.Duration, pain.RecastDelay, pain.CastTime, pain.Radius, pain.Range),
                Is.EqualTo(("Summoner Pain", 150.0, 3_000, 36_000, 20_000, 3_000, 1_000, 1_800)));
            Assert.That((hot.Value, hot.Frequency, hot.Duration, hot.RecastDelay, hot.CastTime), Is.EqualTo((1_250.0, 2_000, 10_000, 45_000, 3_000)));
            Assert.That(ds.Damage, Is.EqualTo(80.0), "her damage shield");
            Assert.That(MaxHealthOf<SummonerRoesia>(), Is.EqualTo(100_000));
        }

        [Test]
        public void ADottedRaiderHoldsTheRaidCureBackOnlyUnderAQuarter()
        {
            Assert.That(RaidCurePoison.InDanger(49, false), Is.True, "not dotted: under half, as before");
            Assert.That(RaidCurePoison.InDanger(50, false), Is.False);
            Assert.That(RaidCurePoison.InDanger(49, true), Is.False, "dotted: the cure is its help");
            Assert.That(RaidCurePoison.InDanger(24, true), Is.True, "dotted and under a quarter: the heal comes first");
            Assert.That(RaidCurePoison.CureDelayMilliseconds, Is.EqualTo(2_500L));
        }

        /// <summary>
        /// One attempt at Summoner Roesia, the raid fighting within 900 of her spawn (b1b6846's rule, back since sweep 7b).
        /// <para>Her side, from her script: her think every 2 s casts Summoner Pain with a 35% chance once its 20 s recast is
        /// over (3 s cast, uninterruptible) on a random living raider; each other raider is caught with
        /// <see cref="Cover"/> (b1b6846: 71 of 77 in her first minute); Pain ticks every 3 s for 36 s and a new cast renews it.
        /// Under 25% she casts Summoner Heal (35% a think, 45 s recast): 5 x 1,250. Her damage shield (80 heat) hits each
        /// melee raider on every swing (<see cref="DsHit"/> after resists, assumed; a swing every 3.5 s).</para>
        /// <para>Calibrated: <see cref="Tick"/> (150 less resists) so that the first minute's Pain matches b1b6846's 124,913
        /// on 71 raiders; <see cref="Heal"/> (a healer's effective heal a cast, overheal and travel counted) so that a raid
        /// that never cures wipes in about 1.5 minutes with her at about 76%, as b1b6846 did; the raid's damage, 17,000 a
        /// minute for 77 raiders (b1b6846's first minute), scales with the living. Assumed: parties of 8 (a tank, three
        /// melee, two casters, two healers), her melee 200 a second on the tank, 3 s casts for heals and cures, no rez in
        /// the attempt, the wipe rule's half fallen.</para>
        /// <para>The bots' side is the real code: RaidCurePoison.Decide (who cures, which cure, whom) with the claims, the
        /// danger count by <see cref="RaidCurePoison.DangerPercent"/> (sweep 7b) or <see cref="RaidCurePoison.InDanger"/> (the
        /// fix), and the cure delay of 5 s (sweep 7b) or <see cref="RaidCurePoison.CureDelayMilliseconds"/> (the fix).</para>
        /// </summary>
        public sealed class RoesiaFight
        {
            public const int Tick = 125, DsHit = 64, Swing = 3_500, TankHitPerHalfSecond = 100, Heal = 400, HealMs = 3_000, CureMs = 3_000;
            public const double Cover = 0.92, PainChance = 0.35, DealtPerRaiderMs = 17_000.0 / 77 / 60_000;
            public const int Step = 250, Think = 2_000;
            private static readonly string[] Layout = ["tank", "melee", "melee", "melee", "caster", "caster", "healer", "healer"];

            public bool Won;
            public double Minutes;
            public int Lowest = 100, Fallen, Cures, FirstMinutePain;

            public static RoesiaFight Run(int seed, int n, bool cures, bool fix, bool groupCure, int minutes = 15)
            {
                Spell pain = RoesiaSpell("RoesiaDot"), hot = RoesiaSpell("RoesiaHOT");
                int freq = pain.Frequency, dur = pain.Duration, recast = pain.RecastDelay, cast = pain.CastTime;
                int hotTicks = hot.Duration / hot.Frequency;
                double hotValue = hot.Value;
                int bossMax = MaxHealthOf<SummonerRoesia>();
                var f = new RoesiaFight();
                var rng = new Rng(seed);
                string[] role = Enumerable.Range(0, n).Select(i => Layout[i % 8]).ToArray();
                int[] party = Enumerable.Range(0, n).Select(i => i / 8).ToArray();
                int[] max = role.Select(r => r switch { "tank" => 2_500, "melee" => 2_000, "caster" => 1_400, _ => 1_600 }).ToArray();
                int[] hp = (int[])max.Clone();
                long[] painUntil = new long[n], nextTick = new long[n], pendingAt = new long[n], nextCure = new long[n];
                string[] pending = new string[n];
                int[] pendingTarget = new int[n];
                bool[] dead = new bool[n];
                int[] healers = Enumerable.Range(0, n).Where(i => role[i] == "healer").ToArray();
                long delay = fix ? RaidCurePoison.CureDelayMilliseconds : 5_000;
                double boss = bossMax;
                long now = 0, ready = 0, castAt = -1, hotReady = 0;
                var hotAt = new List<long>();
                int tank = 0;
                while (now < minutes * 60_000L)
                {
                    if (now % Think == 0)
                    {
                        if (castAt < 0 && now >= ready && rng.Next() < PainChance) castAt = now + cast;
                        if ((int)Math.Floor(boss * 100 / bossMax) < 25 && now >= hotReady && rng.Next() < PainChance)
                        {
                            hotReady = now + hot.RecastDelay + hot.CastTime;
                            for (int k = 0; k < hotTicks; k++) hotAt.Add(now + hot.CastTime + hot.Frequency * k);
                        }
                    }
                    if (castAt >= 0 && now >= castAt)
                    {
                        castAt = -1;
                        ready = now + recast;
                        int[] living = Enumerable.Range(0, n).Where(i => !dead[i]).ToArray();
                        if (living.Length > 0)
                        {
                            int centre = living[rng.Below(living.Length)];
                            foreach (int i in living)
                                if (i == centre || rng.Next() < Cover)
                                {
                                    if (painUntil[i] <= now) nextTick[i] = now + freq;
                                    painUntil[i] = now + dur;
                                }
                        }
                    }
                    for (int k = hotAt.Count - 1; k >= 0; k--)
                        if (hotAt[k] <= now)
                        {
                            boss = Math.Min(bossMax, boss + hotValue);
                            hotAt.RemoveAt(k);
                        }
                    for (int i = 0; i < n; i++)
                        if (!dead[i] && painUntil[i] > now && now >= nextTick[i])
                        {
                            nextTick[i] += freq;
                            hp[i] -= Tick;
                            if (now < 60_000) f.FirstMinutePain += Tick;
                        }
                    if (dead[tank])
                    {
                        int next = Enumerable.Range(0, n).Where(i => !dead[i] && role[i] == "tank").DefaultIfEmpty(-1).First();
                        if (next < 0) next = Enumerable.Range(0, n).Where(i => !dead[i]).DefaultIfEmpty(-1).First();
                        if (next < 0) break;
                        tank = next;
                    }
                    if (now % 500 == 0)
                    {
                        hp[tank] -= TankHitPerHalfSecond;
                        for (int i = 0; i < n; i++)
                            if (!dead[i] && role[i] is "tank" or "melee" && (now + i * 137L) % Swing < 500) hp[i] -= DsHit;
                    }
                    for (int i = 0; i < n; i++)
                        if (!dead[i] && hp[i] <= 0)
                        {
                            dead[i] = true;
                            hp[i] = 0;
                            f.Fallen++;
                        }
                    foreach (int h in healers)
                    {
                        if (dead[h]) { pending[h] = null; continue; }
                        if (pending[h] != null && now >= pendingAt[h])
                        {
                            string kind = pending[h];
                            int t = pendingTarget[h];
                            pending[h] = null;
                            if (kind == "heal") { if (!dead[t]) hp[t] = Math.Min(max[t], hp[t] + Heal); }
                            else if (kind == "group")
                            {
                                for (int m = 0; m < n; m++)
                                    if (party[m] == party[h] && !dead[m]) painUntil[m] = 0;
                                f.Cures++;
                            }
                            else
                            {
                                if (!dead[t]) painUntil[t] = 0;
                                f.Cures++;
                            }
                        }
                        if (pending[h] != null || now % 500 != 0) continue;
                        if (cures)
                        {
                            int danger = 0;
                            for (int m = 0; m < n; m++)
                            {
                                if (party[m] != party[h] || dead[m]) continue;
                                int pct = hp[m] * 100 / max[m];
                                bool dotted = painUntil[m] > now;
                                if (fix ? RaidCurePoison.InDanger(pct, dotted) : pct < RaidCurePoison.DangerPercent) danger++;
                            }
                            var dottedList = new List<RaidCurePoison.Dotted>();
                            for (int m = 0; m < n; m++)
                            {
                                if (dead[m] || painUntil[m] <= now) continue;
                                bool own = party[m] == party[h];
                                if (groupCure && !own) continue; // a group-cure class scans its own party (BotBrain.TryRaidCurePoison)
                                dottedList.Add(new(m, own, hp[m] * 100 / max[m]));
                            }
                            var claimed = new HashSet<long>(healers.Where(x => pending[x] == "single").Select(x => (long)pendingTarget[x]));
                            var (cure, pick) = RaidCurePoison.Decide(true, now >= nextCure[h], groupCure, !groupCure, dottedList, danger, false, h,
                                id => claimed.Contains(id));
                            if (cure != RaidCurePoison.Cure.None && pick >= 0)
                            {
                                pending[h] = cure == RaidCurePoison.Cure.Group ? "group" : "single";
                                pendingTarget[h] = (int)dottedList[pick].Id;
                                pendingAt[h] = now + CureMs;
                                nextCure[h] = now + delay;
                                continue;
                            }
                        }
                        int best = -1;
                        for (int m = 0; m < n; m++)
                        {
                            if (dead[m] || party[m] != party[h] || hp[m] * 100 / max[m] >= 80) continue;
                            if (best < 0 || (long)hp[m] * max[best] < (long)hp[best] * max[m]) best = m;
                        }
                        if (best < 0)
                            for (int m = 0; m < n; m++)
                            {
                                if (dead[m] || hp[m] * 100 / max[m] >= 80) continue;
                                if (best < 0 || (long)hp[m] * max[best] < (long)hp[best] * max[m]) best = m;
                            }
                        if (best >= 0)
                        {
                            pending[h] = "heal";
                            pendingTarget[h] = best;
                            pendingAt[h] = now + HealMs;
                        }
                    }
                    boss -= DealtPerRaiderMs * (n - f.Fallen) * Step;
                    f.Lowest = Math.Min(f.Lowest, Math.Max(0, (int)Math.Floor(boss * 100 / bossMax)));
                    f.Minutes = now / 60_000.0;
                    if (boss <= 0) { f.Won = true; return f; }
                    if (f.Fallen * 2 >= n) return f;
                    now += Step;
                }
                f.Minutes = now / 60_000.0;
                return f;
            }
        }

        private static RoesiaFight[] Roesia(int n, bool cures, bool fix, bool groupCure, int seeds = 16) =>
            Enumerable.Range(0, seeds).Select(s => RoesiaFight.Run(s, n, cures, fix, groupCure)).ToArray();

        private static string Show(string what, RoesiaFight[] r) =>
            $"{what}: won {r.Count(x => x.Won)}/{r.Length}, minutes {r.Min(x => x.Minutes):0.0}-{r.Max(x => x.Minutes):0.0} " +
            $"(mean {r.Average(x => x.Minutes):0.0}), her lowest {r.Min(x => x.Lowest)}-{r.Max(x => x.Lowest)}%, " +
            $"first-minute Pain {r.Min(x => x.FirstMinutePain):N0}-{r.Max(x => x.FirstMinutePain):N0}";

        [Test]
        public void WithoutCuresTheModelLosesAsB1b6846Did()
        {
            RoesiaFight[] none = Roesia(77, cures: false, fix: false, groupCure: true);
            TestContext.WriteLine(Show("no cure (b1b6846 / 9a173f4), 77", none));
            Assert.That(none.Count(x => x.Won), Is.Zero);
            Assert.That(none.Max(x => x.Minutes), Is.LessThan(3.0), "b1b6846: the raid fell in about 1.5 minutes");
            Assert.That(none.Average(x => x.Lowest), Is.InRange(65, 85), "b1b6846: lowest 76%");
            // b1b6846: 124,913 on 71 raiders in the first minute; the model's 77 raiders: about 135,000.
            Assert.That(none.Average(x => x.FirstMinutePain), Is.InRange(105_000, 165_000));
        }

        [Test]
        public void WithAGroupCureInEveryPartySweep7bsRuleAlreadyWinsAndTheFixWinsSooner()
        {
            RoesiaFight[] old = Roesia(77, true, false, true), fix = Roesia(77, true, true, true);
            TestContext.WriteLine(Show("group cure, sweep 7b's rule, 77", old));
            TestContext.WriteLine(Show("group cure, the fix, 77", fix));
            Assert.That(old.Count(x => x.Won), Is.GreaterThanOrEqualTo(14));
            Assert.That(fix.Count(x => x.Won), Is.GreaterThanOrEqualTo(15));
            Assert.That(fix.Where(x => x.Won).Max(x => x.Minutes), Is.LessThanOrEqualTo(old.Where(x => x.Won).Max(x => x.Minutes) + 0.25));
        }

        [Test]
        public void WithSingleCuresOnlySweep7bsRuleNeverWinsAndTheFixWinsOnceTheRaidGrows()
        {
            // Honest result: at 77 the fix still loses (it lasts longer); the raid's own size, which the win-balance policy
            // grows after a loss (RealmRaidRecruitmentPolicy, up to 200% of the period 64), is what turns it.
            RoesiaFight[] old77 = Roesia(77, true, false, false), fix77 = Roesia(77, true, true, false);
            RoesiaFight[] old100 = Roesia(100, true, false, false), fix100 = Roesia(100, true, true, false);
            RoesiaFight[] old128 = Roesia(128, true, false, false), fix128 = Roesia(128, true, true, false);
            foreach (var (what, r) in new[] { ("single cure, sweep 7b, 77", old77), ("single cure, fix, 77", fix77),
                         ("single cure, sweep 7b, 100", old100), ("single cure, fix, 100", fix100),
                         ("single cure, sweep 7b, 128", old128), ("single cure, fix, 128", fix128) })
                TestContext.WriteLine(Show(what, r));
            Assert.That(old77.Count(x => x.Won) + old100.Count(x => x.Won) + old128.Count(x => x.Won), Is.LessThanOrEqualTo(1),
                "sweep 7b's rule loses at every size");
            Assert.That(fix77.Average(x => x.Minutes), Is.GreaterThan(old77.Average(x => x.Minutes)), "the fix lasts longer");
            Assert.That(fix100.Count(x => x.Won), Is.GreaterThanOrEqualTo(2));
            Assert.That(fix128.Count(x => x.Won), Is.GreaterThanOrEqualTo(12));
        }

        // =============================================================================================================
        // #412 Marfoirwort
        // =============================================================================================================

        [Test]
        public void TheLeechModelUsesHisOwnSpellAndHealth()
        {
            var marf = (Marfoirwort)RuntimeHelpers.GetUninitializedObject(typeof(Marfoirwort));
            Spell leech = marf.MarfoirwortDD;
            Assert.That((leech.Name, leech.Damage, leech.Radius, leech.CastTime), Is.EqualTo(("Marfoirwort Life-leech", 450.0, 350, 0)));
            Assert.That(T.LeechRadius, Is.EqualTo(leech.Radius));
            Assert.That(marf.MaxHealth, Is.EqualTo(40_000));
            Assert.That(T.For("Marfoirwort").SpreadRadius, Is.EqualTo(leech.Radius), "the catalog's spread is unchanged");
        }

        [Test]
        public void TheMeleeBehindHimStandClearOfTheLeechAroundHisTarget()
        {
            Vector3 boss = new(0, 0, 0);
            // His target at the 185 it holds: a melee raider anywhere within reach of him is sent behind him, 370-380 from it.
            Vector3 anchor = new(T.BehindDistance, 0, 0);
            for (int deg = 0; deg < 360; deg += 15)
            {
                double a = deg * Math.PI / 180;
                Vector3 at = new((float)(190 * Math.Cos(a)), (float)(190 * Math.Sin(a)), 0);
                if (T.LeechMeleeSpot(at, boss, anchor) is Vector3 spot)
                {
                    Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(anchor.X, anchor.Y)), Is.GreaterThan(T.LeechRadius + 15), $"from {deg} deg");
                    Assert.That(Vector2.Distance(new(spot.X, spot.Y), Vector2.Zero), Is.LessThanOrEqualTo(T.BehindReach), "within bot melee reach");
                }
            }
            // At 150 (his own close-in, before the hold) the spot behind him reaches only 345: the hold is what clears it.
            Assert.That(150 + T.LeechBehindFor(150), Is.LessThan(T.LeechRadius));
            Assert.That(T.LeechMeleeSpot(new(-400, 0, 0), boss, anchor), Is.Null, "already clear");
            Assert.That(T.LeechRangeSpot(new(300, 0, 0), boss, anchor), Is.Not.Null, "at range: out of it");
            Assert.That(T.LeechExposure(new(0, 0, 0), new[] { (new Vector3(100, 0, 0), false), (new Vector3(0, 340, 0), true),
                (new Vector3(400, 0, 0), false) }), Is.EqualTo((1, 1)));
        }

        [Test]
        public void HisRuleKeepsEveryBotsPetOffHim()
        {
            T.DungeonRule rule = T.AllDungeonRules.Single(r => r.Encounter == "MarfoirwortLeech");
            Assert.That(rule.Targets, Is.EqualTo(new[] { nameof(Marfoirwort) }));
            Assert.That(rule.NoPets?.Invoke(null), Is.True);
            Assert.That(rule.Summary, Is.Not.Empty);
        }

        /// <summary>
        /// Marfoirwort against a 40-raider Hibernia raid (the period size). His side, from his script: a Life-leech on 85% x 25%
        /// of his swings (85% a hit: assumed), 450 body (his spell, read here) less 15% resists (assumed) to everyone within
        /// 350 of his target, healing him by all of it; 40,000 health. Swing intervals of 3.0-4.5 s are tried (his template's
        /// speed is not in the repository). The raid: 400 damage a raider a minute (the owner's PC ledger on build b1b6846,
        /// 47 EPIC_FIGHT_LEDGER lines at the Earthmenders, pets counted in), 13 melee, 11 pets (druids, enchanters, animists),
        /// 3 at range in his target's party and 23 at range in the others. Positions are drawn again for each leech: before,
        /// the melee 110-200 from him all round, the pets 90-170, his target 140 from him, the catalog's SpreadSpot for the
        /// others at range; after, his target at 185, the melee at LeechMeleeSpot (20 of slack either way), no pets (their
        /// share of the damage, 11 of 51, is taken off), everyone at range at LeechRangeSpot.
        /// </summary>
        public static (bool Killed, double Minutes, int Lowest, double Inside) MarfoirwortFight(int seed, bool fix, double swing, double perMinute = 400)
        {
            var marf = (Marfoirwort)RuntimeHelpers.GetUninitializedObject(typeof(Marfoirwort));
            double leech = marf.MarfoirwortDD.Damage * (1 - 0.15);
            int radius = marf.MarfoirwortDD.Radius, max = marf.MaxHealth;
            var rng = new Rng(seed);
            double hp = max, t = 0, dealt = perMinute * 40 * (fix ? 40.0 / 51 : 1);
            int lowest = 100;
            long inside = 0, leeches = 0;
            while (t < 30 * 60)
            {
                t += swing;
                hp -= dealt * swing / 60;
                if (rng.Next() < 0.85 * 0.25)
                {
                    int k = LeechInside(rng, fix, radius);
                    inside += k;
                    leeches++;
                    hp = Math.Min(max, hp + k * leech);
                }
                lowest = Math.Min(lowest, (int)Math.Floor(hp * 100 / max));
                if (hp <= 0) return (true, t / 60, 0, inside / (double)Math.Max(1, leeches));
            }
            return (false, t / 60, lowest, inside / (double)Math.Max(1, leeches));
        }

        private static Vector3 Around(Rng rng, double lo, double hi)
        {
            double a = rng.Next() * 2 * Math.PI, d = lo + rng.Next() * (hi - lo);
            return new((float)(d * Math.Cos(a)), (float)(d * Math.Sin(a)), 0);
        }

        /// <summary>Raiders and pets one leech lands on, his target included.</summary>
        private static int LeechInside(Rng rng, bool fix, int radius)
        {
            Vector3 boss = Vector3.Zero, anchor = new(fix ? T.BehindDistance : 140, 0, 0);
            int n = 1;
            void Count(Vector3 p)
            {
                if (Vector2.Distance(new(p.X, p.Y), new(anchor.X, anchor.Y)) <= radius) n++;
            }
            for (int i = 0; i < 13; i++)
            {
                Vector3 p = Around(rng, 110, 200);
                if (fix && T.LeechMeleeSpot(p, boss, anchor) is Vector3 s)
                    p = new(s.X + (float)(rng.Next() * 40 - 20), s.Y + (float)(rng.Next() * 40 - 20), 0);
                Count(p);
            }
            for (int i = 0; i < 11; i++)
            {
                Vector3 p = Around(rng, 90, 170);
                if (!fix) Count(p);
            }
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = Around(rng, 300, 1200);
                if (fix && T.LeechRangeSpot(p, boss, anchor) is Vector3 s) p = s;
                Count(p);
            }
            for (int i = 0; i < 23; i++)
            {
                Vector3 p = Around(rng, 300, 1500);
                Vector3? s = fix ? T.LeechRangeSpot(p, boss, anchor) : T.SpreadSpot(p, boss, anchor, radius, true);
                Count(s ?? p);
            }
            return n;
        }

        [Test]
        public void TheMeleeAndPetsAtHisTargetHeldHimAndTheFixKillsHim()
        {
            foreach (double perMinute in new[] { 400.0, 200.0 })
                foreach (double swing in new[] { 3.0, 3.5, 4.0, 4.5 })
                {
                    var old = Enumerable.Range(0, 16).Select(s => MarfoirwortFight(s, false, swing, perMinute)).ToArray();
                    var fix = Enumerable.Range(0, 16).Select(s => MarfoirwortFight(s, true, swing, perMinute)).ToArray();
                    TestContext.WriteLine($"damage {perMinute}/raider-minute, swing {swing:0.0} s: before {old.Count(x => x.Killed)}/16 killed in 30 min " +
                                          $"(leech lands on {old.Average(x => x.Inside):0.0}); fix {fix.Count(x => x.Killed)}/16, " +
                                          $"{fix.Min(x => x.Minutes):0.0}-{fix.Max(x => x.Minutes):0.0} min (leech lands on {fix.Average(x => x.Inside):0.00})");
                    Assert.That(old.Count(x => x.Killed), Is.LessThanOrEqualTo(3), "ccba364: held at 79% and above, no damage for five minutes");
                    Assert.That(old.Average(x => x.Inside), Is.GreaterThan(15));
                    Assert.That(fix.Count(x => x.Killed), Is.EqualTo(16));
                    Assert.That(fix.Max(x => x.Minutes), Is.LessThan(perMinute >= 400 ? 6.0 : 12.0));
                    Assert.That(fix.Average(x => x.Inside), Is.LessThan(2.0), "his target, and now and then one more");
                }
        }

        // =============================================================================================================
        // #412 the Green Knight
        // =============================================================================================================

        [Test]
        public void TheMeleeTakeTheTreeOnThemThenTheNearest()
        {
            var trees = new[]
            {
                new T.TreeView(5, 150, false, 100), new T.TreeView(6, 300, true, 100), new T.TreeView(7, 900, true, 10),
            };
            Assert.That(T.GreenKnightMeleeTree(trees), Is.EqualTo(1), "the one on me");
            Assert.That(T.GreenKnightMeleeTree(new[] { new T.TreeView(5, 150, false, 100), new T.TreeView(4, 150, false, 40) }), Is.EqualTo(1),
                "a tie: the most hurt");
            Assert.That(T.GreenKnightMeleeTree(new[] { new T.TreeView(7, 900, true, 10) }), Is.EqualTo(-1), "out of reach: stay on him");
            Assert.That(T.GreenKnightMeleeTree(Array.Empty<T.TreeView>()), Is.EqualTo(-1));
            Assert.That(MaxHealthOf<GKTrees>(), Is.EqualTo(800));
            Assert.That(MaxHealthOf<OFGreenKnight>(), Is.EqualTo(100_000));
            Assert.That(T.AllDungeonRules.Count(r => r.Encounter == "GreenKnightTrees" && r.Targets.SequenceEqual(new[] { nameof(OFGreenKnight) })),
                Is.EqualTo(1));
        }

        /// <summary>
        /// The Green Knight against a 40-raider Albion raid (the period size), five parties of three melee, three at range
        /// and two healers; his target's party keeps him. His side, from his script: a tree (800 health) at every living
        /// raider every 25-35 s, and it stays; Might of the Forest, 200 after resists (assumed) on every melee raider on 30%
        /// of his swings (every 3.5 s, assumed); 150 a second on his target (assumed). Each tree hits its raider for
        /// <paramref name="treeHit"/> a second (assumed: no number in the logs). Raid: <paramref name="killerDps"/> a second
        /// from each raider on a tree (assumed), 400 a raider-minute on him (the b1b6846 ledger), 400 a heal every 3 s from
        /// each healer. Before: the casters and archers take the trees by their party (the catalog), the melee stay on him.
        /// After: the melee outside his target's party take GreenKnightMeleeTree's pick (its own tree at 0, another melee's at
        /// 200, one at range at 1,500).
        /// </summary>
        public static (bool Won, double Minutes, int MaxTrees) GreenKnight(int seed, bool fix, double killerDps, double treeHit, int n = 40,
            int minutes = 45)
        {
            string[] layout = ["melee", "melee", "melee", "ranged", "ranged", "ranged", "healer", "healer"];
            var rng = new Rng(seed);
            string[] role = Enumerable.Range(0, n).Select(i => layout[i % 8]).ToArray();
            int[] party = Enumerable.Range(0, n).Select(i => i / 8).ToArray();
            double[] max = role.Select(r => r == "melee" ? 2_000.0 : r == "ranged" ? 1_400.0 : 1_600.0).ToArray();
            double[] hp = (double[])max.Clone();
            bool[] dead = new bool[n];
            int treeMax = MaxHealthOf<GKTrees>();
            double gk = MaxHealthOf<OFGreenKnight>();
            const int step = 250;
            long now = 0, nextWave = 25_000 + rng.Below(10_001);
            var trees = new List<(long Id, double Hp, int Victim)>();
            long nextId = 0;
            long[] on = Enumerable.Repeat(-1L, n).ToArray();
            int[] pending = Enumerable.Repeat(-1, n).ToArray();
            long[] pendingAt = new long[n];
            int fallen = 0, maxTrees = 0, tank = 0;
            while (now < minutes * 60_000L)
            {
                if (now >= nextWave)
                {
                    for (int i = 0; i < n; i++)
                        if (!dead[i]) trees.Add((nextId++, treeMax, i));
                    nextWave = now + 25_000 + rng.Below(10_001);
                }
                if (now % 2_000 == 0)
                    for (int i = 0; i < n; i++)
                    {
                        if (dead[i]) continue;
                        if (role[i] == "ranged") on[i] = trees.Count > 0 ? trees[party[i] % trees.Count].Id : -1;
                        else if (role[i] == "melee")
                        {
                            if (!fix || party[i] == party[tank] || trees.Count == 0) { on[i] = -1; continue; }
                            var views = trees.Select(t => new T.TreeView(t.Id, t.Victim == i ? 0 : role[t.Victim] == "melee" ? 200 : 1_500,
                                t.Victim == i, (int)Math.Floor(t.Hp * 100 / treeMax))).ToArray();
                            int pick = T.GreenKnightMeleeTree(views);
                            on[i] = pick >= 0 ? views[pick].Id : -1;
                        }
                    }
                var byId = new Dictionary<long, int>();
                for (int k = 0; k < trees.Count; k++) byId[trees[k].Id] = k;
                for (int i = 0; i < n; i++)
                {
                    if (dead[i] || role[i] == "healer") continue;
                    if (on[i] >= 0 && byId.TryGetValue(on[i], out int k))
                        trees[k] = (trees[k].Id, trees[k].Hp - killerDps * step / 1000, trees[k].Victim);
                    else if (role[i] == "melee" && on[i] < 0) gk -= 400.0 * step / 60_000;
                }
                trees.RemoveAll(t => t.Hp <= 0);
                maxTrees = Math.Max(maxTrees, trees.Count);
                bool anyone = true;
                for (int k = 0; k < trees.Count; k++)
                {
                    var t = trees[k];
                    if (dead[t.Victim])
                    {
                        int[] living = Enumerable.Range(0, n).Where(i => !dead[i]).ToArray();
                        if (living.Length == 0) { anyone = false; break; }
                        t = (t.Id, t.Hp, living[(int)(t.Id % living.Length)]);
                        trees[k] = t;
                    }
                    hp[t.Victim] -= treeHit * step / 1000;
                }
                if (!anyone) break;
                if (dead[tank])
                {
                    int next = Enumerable.Range(0, n).Where(i => !dead[i] && role[i] == "melee").DefaultIfEmpty(-1).First();
                    if (next < 0) next = Enumerable.Range(0, n).Where(i => !dead[i]).DefaultIfEmpty(-1).First();
                    if (next < 0) break;
                    tank = next;
                }
                hp[tank] -= 150.0 * step / 1000;
                if (now % 3_500 < step && rng.Next() < 0.3)
                    for (int i = 0; i < n; i++)
                        if (!dead[i] && role[i] == "melee") hp[i] -= 200;
                for (int i = 0; i < n; i++)
                    if (!dead[i] && hp[i] <= 0)
                    {
                        dead[i] = true;
                        fallen++;
                    }
                for (int h = 0; h < n; h++)
                {
                    if (dead[h] || role[h] != "healer") continue;
                    if (pending[h] >= 0 && now >= pendingAt[h])
                    {
                        if (!dead[pending[h]]) hp[pending[h]] = Math.Min(max[pending[h]], hp[pending[h]] + 400);
                        pending[h] = -1;
                    }
                    if (pending[h] < 0 && now % 500 == 0)
                    {
                        int best = -1;
                        for (int m = 0; m < n; m++)
                        {
                            if (dead[m] || hp[m] * 100 / max[m] >= 80) continue;
                            if (best < 0 || hp[m] * max[best] < hp[best] * max[m]) best = m;
                        }
                        if (best >= 0)
                        {
                            pending[h] = best;
                            pendingAt[h] = now + 3_000;
                        }
                    }
                }
                if (gk <= 0) return (true, now / 60_000.0, maxTrees);
                if (fallen * 2 >= n) return (false, now / 60_000.0, maxTrees);
                now += step;
            }
            return (false, now / 60_000.0, maxTrees);
        }

        [Test]
        public void TheTreesOutgrowTheRaidAndTheMeleeOnThemOnlyDelayTheWipe()
        {
            // Honest result: with these assumptions both lose. Every raider brings 800 of trees each 25-35 s (about 27 a
            // second), so the casters and archers alone (15 of 40) fall behind at any tree damage under about 70 a second;
            // with the melee on them too the trees stay fewer and the raid lasts longer, but in the model the attrition
            // still wins. If this starts failing on the "no wins" line, the model now wins: update the docs and the report.
            foreach (double treeHit in new[] { 5.0, 8.0, 12.0 })
                foreach (double dps in new[] { 40.0, 50.0, 60.0 })
                {
                    var old = Enumerable.Range(0, 8).Select(s => GreenKnight(s, false, dps, treeHit)).ToArray();
                    var fix = Enumerable.Range(0, 8).Select(s => GreenKnight(s, true, dps, treeHit)).ToArray();
                    TestContext.WriteLine($"tree hit {treeHit}/s, tree damage {dps}/s a raider: before won {old.Count(x => x.Won)}/8, wiped after " +
                                          $"{old.Average(x => x.Minutes):0.0} min, at most {old.Max(x => x.MaxTrees)} trees; fix won {fix.Count(x => x.Won)}/8, " +
                                          $"{fix.Average(x => x.Minutes):0.0} min, at most {fix.Max(x => x.MaxTrees)} trees");
                    Assert.That(old.Count(x => x.Won), Is.Zero, "ccba364: two wipes in 29 minutes");
                    Assert.That(fix.Average(x => x.Minutes), Is.GreaterThanOrEqualTo(old.Average(x => x.Minutes)));
                    Assert.That(fix.Max(x => x.MaxTrees), Is.LessThan(old.Max(x => x.MaxTrees)));
                    Assert.That(fix.Count(x => x.Won), Is.Zero, "if this fails the model wins: update the docs and the report");
                }
        }
    }
}
