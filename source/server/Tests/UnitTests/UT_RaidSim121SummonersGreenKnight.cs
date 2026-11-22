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
    /// numbers (read from their scripts here) and the bots' own decision functions (RaidCurePoison.Decide, InDanger and
    /// IsFresh, EpicEncounterTactics.LeechMeleeSpot, LeechRangeSpot, SpreadSpot, GreenKnightMeleeTree and
    /// GreenKnightPeelTree), calibrated where the owner's PC logs give a number. Everything else is an assumption, named where
    /// it is used. Tests only: nothing here has been seen in a PC log. Each model uses its own seeded generator
    /// (<see cref="Rng"/>) so the numbers in the PR and the issues are the numbers these tests compute (a Python mirror of
    /// each model gave them before CI ran).
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
        /// Who can cure Summoner Pain, per realm, from the OpenDAoC database (opendaoc-db-core: spell, linexspell, spellline,
        /// classxspecialization). Every Cure Poison is single-target (Target Realm, Radius 0); no realm has a group cure poison:
        /// Albion's Cleric and Friar (Rebirth lines, spell 1371, level 6), Midgard's Healer and Shaman (Mending lines, 3072,
        /// level 7), Hibernia's Druid and Bard (Regrowth lines, 4983, levels 6 and 9). Albion's Paladin and Hibernia's Warden
        /// fill a raid healer's place (BotPartyRoles.IsHealingClass) with no cure poison. The model's parties (two healer
        /// places each, as the calibration) carry the curers in the era shares' proportion (era-builds-165.json, estimates):
        /// Midgard every place (Healer 15%, Shaman 9%); Albion 1.4 of 2 (Cleric 12% + Friar 7% of the 27% with Paladin 8%);
        /// Hibernia 1.5 of 2 (Druid 12% + Bard 11% of the 31% with Warden 8%, rounded down to the nearest pattern).
        /// </summary>
        public static readonly Dictionary<string, string[]> CurerLayouts = new()
        {
            ["midgard"] = ["cc"],
            ["albion"] = ["cc", "ch", "ch", "cc", "ch"],
            ["hibernia"] = ["cc", "ch"],
        };

        [Test]
        public void TheRealmsHealersWithAndWithoutACurePoison()
        {
            foreach (eCharacterClass c in new[] { eCharacterClass.Cleric, eCharacterClass.Friar, eCharacterClass.Healer,
                         eCharacterClass.Shaman, eCharacterClass.Druid, eCharacterClass.Paladin, eCharacterClass.Warden })
                Assert.That(BotPartyRoles.IsHealingClass(c), Is.True, c + " fills a healer's place");
            Assert.That(CurerLayouts["midgard"].SelectMany(p => p).Count(x => x == 'c') / (double)CurerLayouts["midgard"].Length, Is.EqualTo(2.0));
            Assert.That(CurerLayouts["albion"].SelectMany(p => p).Count(x => x == 'c') / (double)CurerLayouts["albion"].Length, Is.EqualTo(1.4));
            Assert.That(CurerLayouts["hibernia"].SelectMany(p => p).Count(x => x == 'c') / (double)CurerLayouts["hibernia"].Length, Is.EqualTo(1.5));
        }

        [Test]
        public void AFreshPainIsCuredEvenWithAPartyMemberInDanger()
        {
            Assert.That(RaidCurePoison.IsFresh(24_000, 36_000), Is.True, "two thirds left");
            Assert.That(RaidCurePoison.IsFresh(23_999, 36_000), Is.False);
            Assert.That(RaidCurePoison.IsFresh(10_000, 0), Is.False, "no duration: never fresh");
            var dotted = new List<RaidCurePoison.Dotted>
            {
                new(1, true, 60, 960, false), new(2, true, 70, 1_400, true), new(3, true, 40, 800, true), new(4, false, 30, 420, true),
            };
            // A member of its party in danger: round 1 healed; now the fresh ones are cured, the own party's fewest health first.
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 1, false, 9), Is.EqualTo((RaidCurePoison.Cure.Single, 2)));
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 1, true, 9), Is.EqualTo((RaidCurePoison.Cure.None, -1)),
                "a boss mechanic's patient still comes first");
            var stale = dotted.Select(d => d with { Fresh = false }).ToList();
            Assert.That(RaidCurePoison.Decide(true, true, false, true, stale, 1, false, 9), Is.EqualTo((RaidCurePoison.Cure.None, -1)),
                "nothing fresh: the heal comes first, as round 1");
            // Nobody in danger: the own party's fewest health points first (800 before 960 before 1,400), not the lowest %.
            Assert.That(RaidCurePoison.Decide(true, true, false, true, stale, 0, false, 9), Is.EqualTo((RaidCurePoison.Cure.Single, 2)));
            // Without health points (older callers) the order is the percentage, as before.
            var old = dotted.Select(d => d with { Health = 0 }).ToList();
            Assert.That(RaidCurePoison.Choose(old, 9), Is.EqualTo(2));
        }

        public enum CureRule { Sweep7b, Round1, Round2 }

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
        /// <para>Round 2: the healers who can cure follow <see cref="CurerLayouts"/> (the real cure spells: single-target only,
        /// in every realm); the others only heal.</para>
        /// <para>The bots' side is the real code: RaidCurePoison.Decide (who cures, whom) with the claims. Sweep 7b: the danger
        /// count by <see cref="RaidCurePoison.DangerPercent"/>, a 5 s cure delay. Round 1 (#432): <see cref="RaidCurePoison.InDanger"/>,
        /// <see cref="RaidCurePoison.CureDelayMilliseconds"/>. Round 2: round 1 plus each raider's health points and whether
        /// its Pain is fresh (<see cref="RaidCurePoison.IsFresh"/>).</para>
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

            public static RoesiaFight Run(int seed, int n, CureRule rule, string realm, bool cures = true, int minutes = 15)
            {
                Spell pain = RoesiaSpell("RoesiaDot"), hot = RoesiaSpell("RoesiaHOT");
                int freq = pain.Frequency, dur = pain.Duration, recast = pain.RecastDelay, cast = pain.CastTime;
                int hotTicks = hot.Duration / hot.Frequency;
                double hotValue = hot.Value;
                int bossMax = MaxHealthOf<SummonerRoesia>();
                string[] layout = CurerLayouts[realm];
                var f = new RoesiaFight();
                var rng = new Rng(seed);
                string[] role = Enumerable.Range(0, n).Select(i => Layout[i % 8]).ToArray();
                bool[] canCure = Enumerable.Range(0, n).Select(i => i % 8 >= 6 && layout[i / 8 % layout.Length][i % 8 - 6] == 'c').ToArray();
                int[] party = Enumerable.Range(0, n).Select(i => i / 8).ToArray();
                int[] max = role.Select(r => r switch { "tank" => 2_500, "melee" => 2_000, "caster" => 1_400, _ => 1_600 }).ToArray();
                int[] hp = (int[])max.Clone();
                long[] painUntil = new long[n], nextTick = new long[n], pendingAt = new long[n], nextCure = new long[n];
                string[] pending = new string[n];
                int[] pendingTarget = new int[n];
                bool[] dead = new bool[n];
                int[] healers = Enumerable.Range(0, n).Where(i => role[i] == "healer").ToArray();
                long delay = rule == CureRule.Sweep7b ? 5_000 : RaidCurePoison.CureDelayMilliseconds;
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
                    var keep = new List<long>();
                    foreach (long at in hotAt)
                        if (at <= now) boss = Math.Min(bossMax, boss + hotValue);
                        else keep.Add(at);
                    hotAt = keep;
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
                            else
                            {
                                if (!dead[t]) painUntil[t] = 0;
                                f.Cures++;
                            }
                        }
                        if (pending[h] != null || now % 500 != 0) continue;
                        if (cures && canCure[h])
                        {
                            int danger = 0;
                            for (int m = 0; m < n; m++)
                            {
                                if (party[m] != party[h] || dead[m]) continue;
                                int pct = hp[m] * 100 / max[m];
                                bool dotted = painUntil[m] > now;
                                if (rule == CureRule.Sweep7b ? pct < RaidCurePoison.DangerPercent : RaidCurePoison.InDanger(pct, dotted)) danger++;
                            }
                            bool round2 = rule == CureRule.Round2;
                            var dottedList = new List<RaidCurePoison.Dotted>();
                            for (int m = 0; m < n; m++)
                            {
                                if (dead[m] || painUntil[m] <= now) continue;
                                dottedList.Add(new(m, party[m] == party[h], hp[m] * 100 / max[m], round2 ? hp[m] : 0,
                                    round2 && RaidCurePoison.IsFresh(painUntil[m] - now, dur)));
                            }
                            var claimed = new HashSet<long>(healers.Where(x => pending[x] == "single").Select(x => (long)pendingTarget[x]));
                            var (cure, pick) = RaidCurePoison.Decide(true, now >= nextCure[h], false, true, dottedList, danger, false, h,
                                id => claimed.Contains(id));
                            if (cure != RaidCurePoison.Cure.None && pick >= 0)
                            {
                                pending[h] = "single";
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

        private static RoesiaFight[] Roesia(int n, CureRule rule, string realm, bool cures = true, int seeds = 16) =>
            Enumerable.Range(0, seeds).Select(s => RoesiaFight.Run(s, n, rule, realm, cures)).ToArray();

        private static string Show(string what, RoesiaFight[] r) =>
            $"{what}: won {r.Count(x => x.Won)}/{r.Length}, minutes {r.Min(x => x.Minutes):0.0}-{r.Max(x => x.Minutes):0.0} " +
            $"(mean {r.Average(x => x.Minutes):0.0}), her lowest {r.Min(x => x.Lowest)}-{r.Max(x => x.Lowest)}%, " +
            $"first-minute Pain {r.Min(x => x.FirstMinutePain):N0}-{r.Max(x => x.FirstMinutePain):N0}";

        [Test]
        public void WithoutCuresTheModelLosesAsB1b6846Did()
        {
            RoesiaFight[] none = Roesia(77, CureRule.Sweep7b, "midgard", cures: false);
            TestContext.WriteLine(Show("no cure (b1b6846 / 9a173f4), 77", none));
            Assert.That(none.Count(x => x.Won), Is.Zero);
            Assert.That(none.Max(x => x.Minutes), Is.LessThan(3.0), "b1b6846: the raid fell in about 1.5 minutes");
            Assert.That(none.Average(x => x.Lowest), Is.InRange(65, 85), "b1b6846: lowest 76%");
            // b1b6846: 124,913 on 71 raiders in the first minute; the model's 77 raiders: about 135,000.
            Assert.That(none.Average(x => x.FirstMinutePain), Is.InRange(105_000, 165_000));
        }

        /// <summary>The raid sizes the win balance gives Summoner's Hall (period 64): 64 (100%), 80 (its 125% start), 93, 106,
        /// 128 (after one, two and four losses: 145%, 166%, 200%).</summary>
        public static readonly int[] RoesiaSizes = [64, 80, 93, 106, 128];

        [Test]
        public void WithTheRealmsSingleCuresTheFreshPainRuleWinsFromTheWinBalancesFirstStep()
        {
            // Python mirror, 16 seeds (round 2): Round 2 wins Midgard 0/1/13/16/16, Albion 0/2/7/15/16, Hibernia 0/3/13/16/16 at
            // 64/80/93/106/128; round 1 0/2/6/9/15, 0/1/0/0/4, 0/0/0/2/4; sweep 7b none. Honest: the period size (64) and the
            // win balance's start (80) still lose.
            var won = new Dictionary<(string, int, CureRule), int>();
            foreach (string realm in new[] { "midgard", "albion", "hibernia" })
                foreach (int n in RoesiaSizes)
                    foreach (CureRule rule in Enum.GetValues<CureRule>())
                    {
                        RoesiaFight[] r = Roesia(n, rule, realm);
                        won[(realm, n, rule)] = r.Count(x => x.Won);
                        TestContext.WriteLine(Show($"{realm} {n} {rule}", r));
                    }
            foreach (string realm in new[] { "midgard", "albion", "hibernia" })
            {
                Assert.That(RoesiaSizes.Sum(n => won[(realm, n, CureRule.Sweep7b)]), Is.LessThanOrEqualTo(1), realm + ": sweep 7b never wins");
                Assert.That(won[(realm, 64, CureRule.Round2)], Is.LessThanOrEqualTo(2), realm + ": the period size still loses");
                Assert.That(won[(realm, 106, CureRule.Round2)], Is.GreaterThanOrEqualTo(13), realm + " at 106");
                Assert.That(won[(realm, 128, CureRule.Round2)], Is.GreaterThanOrEqualTo(14), realm + " at 128");
                Assert.That(RoesiaSizes.Sum(n => won[(realm, n, CureRule.Round2)]),
                    Is.GreaterThan(RoesiaSizes.Sum(n => won[(realm, n, CureRule.Round1)]) + 10), realm + ": round 2 beats round 1");
            }
            Assert.That(won[("midgard", 93, CureRule.Round2)], Is.GreaterThanOrEqualTo(10));
            Assert.That(won[("hibernia", 93, CureRule.Round2)], Is.GreaterThanOrEqualTo(10));
            Assert.That(won[("albion", 93, CureRule.Round2)], Is.GreaterThanOrEqualTo(4));
            Assert.That(won[("albion", 106, CureRule.Round1)] + won[("hibernia", 106, CureRule.Round1)], Is.LessThanOrEqualTo(5),
                "round 1 with the real curers: Albion and Hibernia still lose at 106");
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
        public void TheGreenKnightModelUsesHisOwnNumbers()
        {
            Assert.That(MaxHealthOf<GKTrees>(), Is.EqualTo(GkTreeHealth));
            Assert.That(MaxHealthOf<OFGreenKnight>(), Is.EqualTo(100_000));
            var gk = (OFGreenKnight)RuntimeHelpers.GetUninitializedObject(typeof(OFGreenKnight));
            Spell heat = gk.GreenKnightHeatDD;
            Assert.That((heat.Name, heat.Damage, heat.Radius), Is.EqualTo(("Might of the Forrest", 250.0, T.GreenKnightHeatRadius)));
            Assert.That(EncounterFloodGuard.ShippedLimit("OFGreenKnight.Trees"), Is.EqualTo(GkWaveCap), "trees a wave at most");
            Assert.That(T.AllDungeonRules.Count(r => r.Encounter == "GreenKnightTrees" &&
                                                     r.Targets.SequenceEqual(new[] { nameof(OFGreenKnight), nameof(GKTrees) })), Is.EqualTo(1));
        }

        [Test]
        public void TheMeleeTakeTheTreeOnThemThenTheNearest()
        {
            // #432's choice, kept for the model's before/after.
            var trees = new[]
            {
                new T.TreeView(5, 150, false, 100), new T.TreeView(6, 300, true, 100), new T.TreeView(7, 900, true, 10),
            };
            Assert.That(T.GreenKnightMeleeTree(trees), Is.EqualTo(1), "the one on me");
            Assert.That(T.GreenKnightMeleeTree(new[] { new T.TreeView(5, 150, false, 100), new T.TreeView(4, 150, false, 40) }), Is.EqualTo(1),
                "a tie: the most hurt");
            Assert.That(T.GreenKnightMeleeTree(new[] { new T.TreeView(7, 900, true, 10) }), Is.EqualTo(-1), "out of reach: stay on him");
            Assert.That(T.GreenKnightMeleeTree(Array.Empty<T.TreeView>()), Is.EqualTo(-1));
        }

        [Test]
        public void TheMeleePeelTheTreesOffTheCastersAndHealersFirst()
        {
            var trees = new[]
            {
                new T.TreeView(10, 100, true, 50, T.TreeOnOther), new T.TreeView(11, 700, false, 100, T.TreeOnCaster),
                new T.TreeView(12, 750, false, 100, T.TreeOnHealer), new T.TreeView(13, 760, false, 100, T.TreeOnHealer),
                new T.TreeView(14, 1_500, false, 100, T.TreeOnAreaCaster),
            };
            Assert.That(T.GreenKnightPeelTree(trees, 4), Is.EqualTo(2), "a healer's tree (the area caster's is out of reach)");
            Assert.That(T.GreenKnightPeelTree(trees, 5), Is.EqualTo(3), "the next melee takes the other healer's tree");
            var areaNear = trees.Append(new T.TreeView(15, 800, false, 100, T.TreeOnAreaCaster)).ToArray();
            Assert.That(T.GreenKnightPeelTree(areaNear, 4), Is.EqualTo(5), "an area caster's tree first");
            var mine = new[] { new T.TreeView(20, 0, true, 80), new T.TreeView(21, 0, true, 30), new T.TreeView(22, 300, false, 10) };
            Assert.That(T.GreenKnightPeelTree(mine, 1), Is.EqualTo(1), "none on a caster or healer: the most hurt on me");
            Assert.That(T.GreenKnightPeelTree(new[] { new T.TreeView(22, 300, false, 10) }, 1), Is.EqualTo(-1), "back on him");
            Assert.That(T.GreenKnightPeelTree(Array.Empty<T.TreeView>(), 1), Is.EqualTo(-1));
        }

        [Test]
        public void TheCastersAndHealersStackAwayFromHisTarget()
        {
            Vector3 boss = new(0, 0, 0), target = new(200, 0, 0);
            Vector3 spot = T.GreenKnightStackSpot(boss, target, null);
            Assert.That(spot.X, Is.EqualTo(-T.GreenKnightStackDistance).Within(0.5));
            Assert.That(Vector2.Distance(new(spot.X, spot.Y), new(target.X, target.Y)), Is.GreaterThan(T.GreenKnightHeatRadius));
            // He turns to a healer in the stack for a moment: the stack stays where it is only while it is clear of his heat.
            Vector3 healer = new(spot.X + 50, spot.Y, 0);
            Assert.That(T.GreenKnightStackSpot(boss, healer, spot), Is.Not.EqualTo(spot), "his target is in it: away from it");
            Assert.That(T.GreenKnightStackSpot(boss, new(0, 200, 0), spot), Is.EqualTo(spot), "his tank moved a little: it stays");
            Assert.That(T.GreenKnightStackSpot(new(3_000, 0, 0), new(3_200, 0, 0), spot).X, Is.EqualTo(3_000 - T.GreenKnightStackDistance).Within(0.5),
                "he walked off: a new spot by him");
        }

        public const int GkTreeHealth = 800, GkWaveCap = 70;
        public enum GkRule { Before, Round1, Round2 }

        /// <summary>
        /// The Green Knight against an Albion raid. Parties of 8: a tank, three melee, two casters (the first a Wizard in every
        /// other party: area spells), two healers. Places, as clusters: around him (the tank and the melee), the stack 750 from
        /// him (round 2's casters and healers), or each caster's and healer's own spot (before and round 1: spread, 1,000 from
        /// him, 900 from each other).
        /// <para>His side, from his script and the server: a tree (800 health) at every living raider within 4,000 every
        /// 25-35 s, up to 70 a wave (the flood guard); his heal pick every 40-60 s (he fights a random healer for 6 s,
        /// assumed, till a tank takes him back); Might of the Forest (250 heat, 200 after resists, assumed) on 30% of his
        /// swings (every 3.5 s, assumed) on everyone in his target's place; 150 a second on his target (assumed). A tree goes
        /// for whoever has the most threat on it (the raider it grew at, 1, at first; damage; half of each heal on the raider it
        /// hits: HealSpellHandler), walks at 225 and hits for <paramref name="treeHit"/> a second (assumed: no number in the
        /// logs); while one hits a caster or a healer it casts nothing (GameLiving.StartInterruptTimer: 94% a swing).</para>
        /// <para>The raid: <paramref name="dps"/> a second from a raider on a tree (assumed), 400 a raider-minute on him (the
        /// b1b6846 ledger), 400 a heal every 3 s, the healers' own party first; Wizards' area spells (the database's Ebullient
        /// Blast 158 / 350 / 3 s and Steaming Wind 325 / 300 / 2.5 s at 15% resists: 134 and 276 on every tree in the place);
        /// walking at 250; a choice every 2 s. Before: the casters shoot the trees by party (catalog ranged adds), Wizards the
        /// biggest clump, the melee stay on him. Round 1 (#432): and the melee outside his target's party take
        /// <see cref="T.GreenKnightMeleeTree"/>'s tree. Round 2: the stack; every melee raider but his target takes
        /// <see cref="T.GreenKnightPeelTree"/>'s tree; casters stay on him; Wizards hit the trees around him only. The other
        /// parties' tanks stay on him in every rule.</para>
        /// </summary>
        public static (bool Won, double Minutes, int MaxTrees) GreenKnightFight(int seed, GkRule rule, int n, double dps, double treeHit,
            int minutes = 45)
        {
            const int step = 250;
            const double onGk = 400.0 / 60_000, tankDps = 150, heat = 200, heatChance = 0.3, heal = 400, pbae = 276, tae = 134;
            const double run = 250, treeRun = 225;
            string[] layout = ["tank", "melee", "melee", "melee", "caster", "caster", "healer", "healer"];
            var rng = new Rng(seed);
            string[] role = Enumerable.Range(0, n).Select(i => layout[i % 8]).ToArray();
            int[] party = Enumerable.Range(0, n).Select(i => i / 8).ToArray();
            bool[] wiz = Enumerable.Range(0, n).Select(i => i % 8 == 4 && party[i] % 2 == 0).ToArray();
            double[] max = role.Select(r => r switch { "tank" => 2_600.0, "melee" => 2_000.0, "caster" => 1_400.0, _ => 1_600.0 }).ToArray();
            double[] hp = (double[])max.Clone();
            bool[] dead = new bool[n];
            bool peel = rule == GkRule.Round2;
            int[] where = Enumerable.Range(0, n).Select(i => role[i] is "tank" or "melee" ? 0 : peel ? 1 : 2 + i).ToArray();
            static int Dist(int a, int b) => a == b ? 0 : a == 0 || b == 0 ? (a == 1 || b == 1 ? 750 : 1_000) : 900;
            var trees = new List<Tree>();
            long nid = 0, now = 0;
            double gk = MaxHealthOf<OFGreenKnight>();
            long nextWave = 25_000 + rng.Below(10_001), nextPick = 40_000 + rng.Below(20_001), pickUntil = -1;
            int picked = -1, tank = 0, fallen = 0, maxTrees = 0;
            long[] on = Enumerable.Repeat(-1L, n).ToArray();
            int[] onCl = new int[n];
            long[] busy = new long[n], castAt = Enumerable.Repeat(-1L, n).ToArray(), healAt = new long[n];
            int[] castCl = new int[n], healPend = Enumerable.Repeat(-1, n).ToArray();
            bool[] castPb = new bool[n];
            while (now < minutes * 60_000L)
            {
                if (now >= nextWave)
                {
                    int grown = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (dead[i]) continue;
                        if (grown >= GkWaveCap) break;
                        grown++;
                        trees.Add(new Tree { Id = nid++, Hp = GkTreeHealth, At = where[i], Target = i, Arrive = now, Threat = { [i] = 1.0 } });
                    }
                    nextWave = now + 25_000 + rng.Below(10_001);
                }
                if (now >= nextPick)
                {
                    int[] hs = Enumerable.Range(0, n).Where(i => !dead[i] && role[i] == "healer").ToArray();
                    if (hs.Length > 0) { picked = hs[rng.Below(hs.Length)]; pickUntil = now + 6_000; }
                    nextPick = now + 40_000 + rng.Below(20_001);
                }
                if (dead[tank])
                {
                    int[] c = Enumerable.Range(0, n).Where(i => !dead[i] && role[i] == "tank").ToArray();
                    if (c.Length == 0) c = Enumerable.Range(0, n).Where(i => !dead[i] && role[i] == "melee").ToArray();
                    if (c.Length == 0) c = Enumerable.Range(0, n).Where(i => !dead[i]).ToArray();
                    if (c.Length == 0) break;
                    tank = c[0];
                }
                int target = now < pickUntil && picked >= 0 && !dead[picked] ? picked : tank;
                bool anyone = true;
                foreach (Tree t in trees)
                {
                    foreach (int k in t.Threat.Keys.Where(k => dead[k]).ToArray()) t.Threat.Remove(k);
                    if (t.Threat.Count == 0)
                    {
                        int[] liv = Enumerable.Range(0, n).Where(i => !dead[i] && where[i] == t.At).ToArray();
                        if (liv.Length == 0) liv = Enumerable.Range(0, n).Where(i => !dead[i] && where[i] == 0).ToArray();
                        if (liv.Length == 0) liv = Enumerable.Range(0, n).Where(i => !dead[i]).ToArray();
                        if (liv.Length == 0) { anyone = false; break; }
                        t.Threat[liv[(int)(t.Id % liv.Length)]] = 1.0;
                    }
                    int top = -1;
                    foreach (var (k, v) in t.Threat)
                        if (top < 0 || v > t.Threat[top] || v == t.Threat[top] && k < top) top = k;
                    if (top != t.Target || where[top] != t.At && now >= t.Arrive)
                    {
                        t.Target = top;
                        if (where[top] != t.At)
                        {
                            t.Arrive = now + (int)(Dist(t.At, where[top]) / treeRun * 1000);
                            t.At = where[top];
                        }
                    }
                }
                if (!anyone) break;
                var byId = trees.ToDictionary(t => t.Id);
                var hitting = new Dictionary<int, List<Tree>>();
                foreach (Tree t in trees)
                    if (now >= t.Arrive)
                    {
                        if (!hitting.TryGetValue(t.Target, out var list)) hitting[t.Target] = list = new List<Tree>();
                        list.Add(t);
                    }
                int tpart = party[tank];
                if (now % 2_000 == 0)
                    for (int i = 0; i < n; i++)
                    {
                        if (dead[i] || role[i] is "tank" or "healer") continue;
                        if (role[i] == "melee")
                        {
                            long want = -1;
                            if (trees.Count > 0 && rule == GkRule.Round1 && party[i] != tpart)
                            {
                                var views = trees.Select(t => new T.TreeView(t.Id, Dist(where[i], t.At), t.Target == i,
                                    (int)Math.Floor(t.Hp * 100 / GkTreeHealth))).ToArray();
                                int k = T.GreenKnightMeleeTree(views);
                                want = k >= 0 ? views[k].Id : -1;
                            }
                            else if (trees.Count > 0 && peel && i != tank)
                            {
                                var views = trees.Select(t => new T.TreeView(t.Id, Dist(where[i], t.At), t.Target == i,
                                    (int)Math.Floor(t.Hp * 100 / GkTreeHealth),
                                    wiz[t.Target] ? T.TreeOnAreaCaster : role[t.Target] == "healer" ? T.TreeOnHealer
                                    : role[t.Target] == "caster" ? T.TreeOnCaster : T.TreeOnOther)).ToArray();
                                int k = T.GreenKnightPeelTree(views, i);
                                want = k >= 0 ? views[k].Id : -1;
                            }
                            int dest = want < 0 ? 0 : byId[want].At;
                            if (dest != where[i])
                            {
                                busy[i] = now + (int)(Dist(where[i], dest) / run * 1000);
                                where[i] = dest;
                            }
                            on[i] = want;
                        }
                        else
                        {
                            if (trees.Count == 0) { on[i] = -1; continue; }
                            if (wiz[i])
                            {
                                var cnt = new Dictionary<int, int>();
                                foreach (Tree t in trees)
                                    if (now >= t.Arrive) cnt[t.At] = cnt.GetValueOrDefault(t.At) + 1;
                                int cl = -1;
                                if (peel) { if (cnt.GetValueOrDefault(0) >= 2) cl = 0; }
                                else
                                    foreach (var (c, v) in cnt)
                                        if (v >= 2 && (cl < 0 || v > cnt[cl] || v == cnt[cl] && c < cl)) cl = c;
                                if (cl >= 0) { on[i] = -2; onCl[i] = cl; continue; }
                            }
                            on[i] = peel ? -1 : trees[party[i] % trees.Count].Id;
                        }
                    }
                for (int i = 0; i < n; i++)
                {
                    if (dead[i]) continue;
                    string r = role[i];
                    if (r == "tank") { gk -= onGk * step; continue; }
                    if (r == "melee")
                    {
                        if (now < busy[i]) continue;
                        Tree t = on[i] >= 0 ? byId.GetValueOrDefault(on[i]) : null;
                        if (t != null && t.At == where[i])
                        {
                            double d = dps * step / 1000;
                            t.Hp -= d;
                            t.Threat[i] = t.Threat.GetValueOrDefault(i) + d;
                        }
                        else if (where[i] == 0) gk -= onGk * step;
                        continue;
                    }
                    if (r == "caster")
                    {
                        if (hitting.ContainsKey(i)) { castAt[i] = -1; continue; }
                        if (on[i] == -2)
                        {
                            if (castAt[i] < 0)
                            {
                                castPb[i] = onCl[i] == where[i];
                                castCl[i] = onCl[i];
                                castAt[i] = now + (castPb[i] ? 2_500 : 3_000);
                            }
                            else if (now >= castAt[i])
                            {
                                double d = castPb[i] ? pbae : tae;
                                foreach (Tree t in trees)
                                    if (t.At == castCl[i] && now >= t.Arrive)
                                    {
                                        t.Hp -= d;
                                        t.Threat[i] = t.Threat.GetValueOrDefault(i) + d;
                                    }
                                castAt[i] = -1;
                            }
                            continue;
                        }
                        castAt[i] = -1;
                        Tree shot = on[i] >= 0 ? byId.GetValueOrDefault(on[i]) : null;
                        if (shot != null)
                        {
                            double d = dps * step / 1000;
                            shot.Hp -= d;
                            shot.Threat[i] = shot.Threat.GetValueOrDefault(i) + d;
                        }
                        else gk -= onGk * step;
                        continue;
                    }
                    if (hitting.ContainsKey(i)) { healPend[i] = -1; continue; }
                    if (healPend[i] >= 0 && now >= healAt[i])
                    {
                        int m = healPend[i];
                        if (!dead[m])
                        {
                            double amount = Math.Min(heal, max[m] - hp[m]);
                            hp[m] += amount;
                            if (hitting.TryGetValue(m, out var onPatient))
                                foreach (Tree t in onPatient)
                                    t.Threat[i] = t.Threat.GetValueOrDefault(i) + 0.5 * amount;
                        }
                        healPend[i] = -1;
                    }
                    if (healPend[i] < 0 && now % 500 == 0)
                    {
                        int best = -1;
                        for (int m = 0; m < n; m++)
                        {
                            if (dead[m] || party[m] != party[i] || hp[m] * 100 / max[m] >= 80) continue;
                            if (best < 0 || hp[m] * max[best] < hp[best] * max[m]) best = m;
                        }
                        if (best < 0)
                            for (int m = 0; m < n; m++)
                            {
                                if (dead[m] || hp[m] * 100 / max[m] >= 80) continue;
                                if (best < 0 || hp[m] * max[best] < hp[best] * max[m]) best = m;
                            }
                        if (best >= 0) { healPend[i] = best; healAt[i] = now + 3_000; }
                    }
                }
                trees.RemoveAll(t => t.Hp <= 0);
                maxTrees = Math.Max(maxTrees, trees.Count);
                foreach (var (v, ts) in hitting)
                    if (!dead[v]) hp[v] -= treeHit * step / 1000 * ts.Count(t => t.Hp > 0);
                hp[target] -= tankDps * step / 1000;
                if (now % 3_500 < step && rng.Next() < heatChance)
                {
                    int cl = where[target];
                    for (int i = 0; i < n; i++)
                        if (!dead[i] && where[i] == cl) hp[i] -= heat;
                }
                for (int i = 0; i < n; i++)
                    if (!dead[i] && hp[i] <= 0)
                    {
                        dead[i] = true;
                        fallen++;
                    }
                if (gk <= 0) return (true, now / 60_000.0, maxTrees);
                if (fallen * 2 >= n) return (false, now / 60_000.0, maxTrees);
                now += step;
            }
            return (false, now / 60_000.0, maxTrees);
        }

        private sealed class Tree
        {
            public long Id;
            public double Hp;
            public int At, Target;
            public long Arrive;
            public Dictionary<int, double> Threat { get; } = new();
        }

        [Test]
        public void TheTreesSilenceTheHealersAndOnlyThePeelPastTheFloodGuardWins()
        {
            // Honest result (Python mirror, 8 seeds a case): before and round 1 never win at 50 or 80; round 2 (the stack and
            // the peel) wins at 50 only with the kindest numbers (tree hit 5, 120 on a tree), and at 80 (the win balance's
            // largest size, 200% of 40, past the flood guard's 70 trees a wave) whenever a raider deals 80 or more to a tree
            // and a tree hits for 8 or less. At 104 (80 and the Epic Special Forces' 24 after a wipe) it wins every case but
            // 50 on a tree. Anything lower loses: see the report.
            var won = new Dictionary<(int, double, double, GkRule), int>();
            var mean = new Dictionary<(int, double, double, GkRule), double>();
            foreach (int n in new[] { 50, 80, 104 })
                foreach (double hit in new[] { 5.0, 8.0, 15.0 })
                    foreach (double dps in new[] { 50.0, 80.0, 120.0 })
                        foreach (GkRule rule in Enum.GetValues<GkRule>())
                        {
                            var r = Enumerable.Range(0, 8).Select(s => GreenKnightFight(s, rule, n, dps, hit)).ToArray();
                            won[(n, hit, dps, rule)] = r.Count(x => x.Won);
                            mean[(n, hit, dps, rule)] = r.Average(x => x.Minutes);
                            TestContext.WriteLine($"{n} raiders, tree hit {hit}/s, {dps}/s on a tree, {rule}: won {r.Count(x => x.Won)}/8, " +
                                                  $"{r.Min(x => x.Minutes):0.0}-{r.Max(x => x.Minutes):0.0} min (mean {r.Average(x => x.Minutes):0.0}), " +
                                                  $"at most {r.Max(x => x.MaxTrees)} trees");
                        }
            int Sum(int n, GkRule rule) => won.Where(w => w.Key.Item1 == n && w.Key.Item4 == rule).Sum(w => w.Value);
            Assert.That(Sum(50, GkRule.Before) + Sum(80, GkRule.Before) + Sum(50, GkRule.Round1) + Sum(80, GkRule.Round1), Is.Zero,
                "ccba364: the 1.2 catalog and #432 lose");
            Assert.That(won[(50, 5.0, 120.0, GkRule.Round2)], Is.GreaterThanOrEqualTo(6));
            Assert.That(Sum(50, GkRule.Round2), Is.LessThanOrEqualTo(12), "at 50 only the kindest case wins");
            Assert.That(Sum(80, GkRule.Round2), Is.GreaterThanOrEqualTo(28), "past the flood guard: four cases of nine");
            Assert.That(Sum(104, GkRule.Round2), Is.GreaterThanOrEqualTo(45), "every case but 50 on a tree");
            Assert.That(Sum(104, GkRule.Before), Is.Zero);
            foreach (var key in won.Keys.Where(k => k.Item4 == GkRule.Round2))
            {
                var r1 = (key.Item1, key.Item2, key.Item3, GkRule.Round1);
                Assert.That(won[key], Is.GreaterThanOrEqualTo(won[r1]), $"round 2 wins at least as often ({key})");
                if (won[key] == 0 && won[r1] == 0)
                    Assert.That(mean[key], Is.GreaterThanOrEqualTo(mean[r1]), $"both lose: round 2 lasts longer ({key})");
            }
        }
    }
}
