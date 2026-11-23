using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using DOL.GS;
using T = DOL.GS.EpicEncounterTactics;
using Rng = DOL.UnitTests.UT_RaidSim121SummonersGreenKnight.Rng;

namespace DOL.UnitTests
{
    /// <summary>
    /// The Green Knight (#412, Living Server 1.2.2; tests only, nothing here has been seen in a PC log). The owner
    /// (2026-11-22): his period size back to 40 ("option 1") and his tactic fixed. 1.2.1's model (UT_RaidSim121's
    /// GreenKnightFight: places as clusters, every tree on the raider it grew at) lost every attempt at 40-104 with the real
    /// numbers (UT_RaidRealStats122). This model places every raider and tree on the ground and gives the trees the server's
    /// own aggro rules, which decide the fight:
    /// <list type="bullet">
    /// <item>a tree takes one raider within 800 of where it grew (StandardMobBrain.CheckNpcAggro adds the first it finds);</item>
    /// <item>it turns to the highest threat (damage; half of every heal on the raider it hits, HealSpellHandler), counted
    /// less past 250 from it (a third at 1,500: <see cref="T.GreenKnightEffectiveThreat"/>);</item>
    /// <item>while it hits a caster or healer that raider casts nothing (94% a swing).</item>
    /// </list>
    /// His side, from his script: 100,000 health; his hit on his target every 3.616 s (UT_RaidRealStats122: 338 a second on
    /// plate unbuffed, 234 buffed, x 1.106 on mail); Might of the Forest (200 after resists, assumed) on 30% of his swings on
    /// every raider within 500 of his target; every 40-60 s a random healer within 2,500 (he walks to it at 400 and fights it
    /// 6 s, assumed); every 25-35 s a tree (800 health, 225 a second, hits by armour: UT_RaidRealStats122) at every living
    /// raider within 4,000, at most 70 a wave; at every 10% a 12 s walk (assumed) and a 400 heal. Trees never despawn.
    /// The raid: parties of 8 (tank, three melee, two casters, the first a Wizard in every other party, two healers); bots
    /// hit him from within 200 (GameLiving.MeleeAttackRange), so his fighters stand in Might of the Forest; 400 a raider-minute
    /// on him (b1b6846 ledger); <c>dps</c> a second on a tree (assumed); a 400 heal every 3 s, range 2,000; a Wizard's 134 on
    /// every tree within 350 of the one it aims at (Ebullient Blast); 191 a second on foot; a choice every 2 s. Lost when half
    /// the raid has fallen. The Python mirror (tools/raid-sims/gk_mirror.py; sweeps: gk_sweep.py) runs the same model; the numbers in the PR, the
    /// issue and docs/RAID-SIZES.md are its 32-seed sweeps, and this test pins its 8-seed grid.
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidSim122GreenKnight
    {
        public enum GkRule { Before, Round2, TagBurn }
        private const int Tank = 0, Melee = 1, Caster = 2, Healer = 3;
        private static readonly int[] Layout = [Tank, Melee, Melee, Melee, Caster, Caster, Healer, Healer];
        private static readonly double[] MaxHp = [2600, 2000, 1400, 1600];
        private const double Step = 0.25, TreeHp = 800, TreeRun = 225, Run = 191, TreeAggro = 800, Grow = 4000, PickRadius = 2500,
            HeatRadius = 500, HealRange = 2000, SpellRange = 1500, AreaRadius = 350, Reach = 150, GkReach = 220, OnGk = 400.0 / 60.0,
            AreaHit = 134, HeatHit = 200, Heal = 400, Cast = 3, Swing = 3.616, PickS = 6, WalkS = 12, SpreadRing = 1000;
        private const int WaveCap = 70;
        private static readonly (double X, double Y) TankAt = (-180, 0), FightAt = (180, 0), StackAt = (750, 0),
            HealSpot = (T.GreenKnightHealDistance, 0), BurnSpot = (T.GreenKnightBurnDistance, 0), OutSpot = (-T.GreenKnightOutDistance, 0);
        public static readonly double[] TreeHitHigh = [32.5, 35.9, 77.7, 35.9], TreeHitLow = [22.5, 24.9, 53.3, 24.9];
        public const double GkHigh = 338, GkLow = 234;

        private static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class Tree
        {
            public int Id, Target = -1;
            public double Hp = TreeHp, X, Y, Hx, Hy;
            public readonly SortedDictionary<int, double> Threat = new();
            public double ThreatOf(int i) => Threat.TryGetValue(i, out double v) ? v : 0.0;
        }

        private static int Pct(Tree t) => (int)Math.Floor(t.Hp * 100 / TreeHp);

        /// <summary>The fight (gk_mirror.fight). Returns whether he died, the minutes, the most trees alive, the fallen and his
        /// health left in percent.</summary>
        public static (bool Won, double Minutes, int MaxTrees, int Fallen, double Left) GreenKnightFight(int seed, GkRule rule, int n,
            double dps, bool high, int minutes = 45)
        {
            double[] treeHit = high ? TreeHitHigh : TreeHitLow;
            double tankHit = high ? GkHigh : GkLow;
            var rng = new Rng(seed);
            int[] role = Enumerable.Range(0, n).Select(i => Layout[i % 8]).ToArray();
            int[] party = Enumerable.Range(0, n).Select(i => i / 8).ToArray();
            bool[] wiz = Enumerable.Range(0, n).Select(i => i % 8 == 4 && party[i] % 2 == 0).ToArray();
            double[] mx = role.Select(r => MaxHp[r]).ToArray();
            double[] hp = (double[])mx.Clone();
            bool[] dead = new bool[n], guard = new bool[n];
            bool[] inside = Enumerable.Repeat(true, n).ToArray();
            var home = new (double X, double Y)[n];
            int tank = 0, k = 0, softCount = role.Count(r => r >= Caster);
            for (int i = 0; i < n; i++)
            {
                int r = role[i];
                if (i == tank) home[i] = TankAt;
                else if (r <= Melee) home[i] = FightAt;
                else if (rule == GkRule.Before)
                {
                    double a = 2 * Math.PI * k / softCount;
                    k++;
                    home[i] = (SpreadRing * Math.Cos(a), SpreadRing * Math.Sin(a));
                }
                else if (rule == GkRule.Round2) home[i] = StackAt;
                else if (r == Healer) home[i] = HealSpot;
                else if (wiz[i]) home[i] = BurnSpot;
                else { home[i] = OutSpot; inside[i] = false; }
            }
            if (rule == GkRule.TagBurn)
                for (int p = 0; p <= party[n - 1]; p++)
                {
                    if (!Enumerable.Range(0, n).Any(i => wiz[i] && party[i] == p)) continue;
                    int g = 0;
                    for (int i = 0; i < n; i++)
                        if (party[i] == p && role[i] == Melee && g < T.GreenKnightGuardsPerParty)
                        {
                            guard[i] = true;
                            home[i] = BurnSpot;
                            g++;
                        }
                }
            double[] px = home.Select(h => h.X).ToArray(), py = home.Select(h => h.Y).ToArray();
            int[] on = Enumerable.Repeat(-1, n).ToArray(), castOn = Enumerable.Repeat(-1, n).ToArray();
            double[] castEnd = Enumerable.Repeat(-1.0, n).ToArray();
            var trees = new List<Tree>();
            var byId = new Dictionary<int, Tree>();
            int nid = 0, walks = 0, fallen = 0, maxTrees = 0, picked = -1;
            double now = 0, gk = 100_000, gkx = 0, gky = 0, pickUntil = -1, nextSwing = Swing, pauseUntil = -1, nextChoice = 0;
            double nextWave = 25 + rng.Below(10_001) / 1000.0, nextPick = 40 + rng.Below(20_001) / 1000.0;
            double end = minutes * 60.0;
            while (now < end)
            {
                // ---- his waves, picks and walks ----
                bool paused = now < pauseUntil;
                if (now >= nextWave && !paused)
                {
                    var order = Enumerable.Range(0, n).Where(i => !dead[i] && inside[i] && Dist(px[i], py[i], gkx, gky) <= Grow).ToList();
                    for (int j = order.Count - 1; j > 0; j--)
                    {
                        int q = rng.Below(j + 1);
                        (order[j], order[q]) = (order[q], order[j]);
                    }
                    foreach (int i in order.Take(WaveCap))
                    {
                        var t = new Tree { Id = nid++, X = px[i], Y = py[i], Hx = px[i], Hy = py[i] };
                        trees.Add(t);
                        byId[t.Id] = t;
                    }
                    nextWave = now + 25 + rng.Below(10_001) / 1000.0;
                }
                if (now >= nextPick)
                {
                    var hs = Enumerable.Range(0, n).Where(i => role[i] == Healer && !dead[i] && Dist(px[i], py[i], gkx, gky) <= PickRadius).ToList();
                    if (hs.Count > 0)
                    {
                        picked = hs[rng.Below(hs.Count)];
                        pickUntil = now + PickS;
                    }
                    nextPick = now + 40 + rng.Below(20_001) / 1000.0;
                }
                if ((int)Math.Floor((100_000 - gk) / 10_000) > walks && walks < 9 && !paused)
                {
                    walks++;
                    pauseUntil = now + WalkS;
                    gk = Math.Min(100_000.0, gk + 400);
                    paused = true;
                }
                if (dead[tank])
                {
                    int c = -1;
                    foreach (int want in new[] { Tank, Melee, -1 })
                    {
                        for (int i = 0; i < n; i++)
                            if (!dead[i] && inside[i] && (want < 0 || role[i] == want) && !guard[i]) { c = i; break; }
                        if (c >= 0) break;
                    }
                    if (c < 0) break;
                    tank = c;
                    home[tank] = TankAt;
                }
                int target = now < pickUntil && picked >= 0 && !dead[picked] ? picked : tank;
                if (!paused)
                {
                    (double ax, double ay) = target != tank ? (px[target], py[target]) : (0.0, 0.0);
                    double d = Dist(gkx, gky, ax, ay), lim = target != tank ? 180.0 : 0.0;
                    if (d > lim)
                    {
                        double s = Math.Min(400 * Step, d - lim);
                        gkx += (ax - gkx) / d * s;
                        gky += (ay - gky) / d * s;
                    }
                }
                // ---- the trees ----
                int tick = (int)(now * 4);
                foreach (Tree t in trees)
                {
                    if ((tick + t.Id) % 4 == 0)
                    {
                        foreach (int q in t.Threat.Keys.Where(q => dead[q]).ToList()) t.Threat.Remove(q);
                        if (t.Threat.Count == 0)
                        {
                            var near = Enumerable.Range(0, n).Where(i => !dead[i] && Dist(px[i], py[i], t.X, t.Y) <= TreeAggro).ToList();
                            if (near.Count > 0) t.Threat[near[rng.Below(near.Count)]] = 1.0;
                        }
                        int best = -1;
                        double bv = -1.0;
                        foreach (var (q, raw) in t.Threat)
                        {
                            double e = T.GreenKnightEffectiveThreat(raw, Dist(px[q], py[q], t.X, t.Y));
                            if (e > bv) { best = q; bv = e; }
                        }
                        if (t.Target >= 0 && t.Threat.TryGetValue(t.Target, out double cur) && best != t.Target &&
                            T.GreenKnightEffectiveThreat(cur, Dist(px[t.Target], py[t.Target], t.X, t.Y)) >= bv)
                            best = t.Target;
                        t.Target = best;
                    }
                    double gx, gy, tlim;
                    if (t.Target >= 0) (gx, gy, tlim) = (px[t.Target], py[t.Target], Reach * 0.8);
                    else (gx, gy, tlim) = (t.Hx, t.Hy, 0.0);
                    double td = Dist(t.X, t.Y, gx, gy);
                    if (td > tlim)
                    {
                        double s = Math.Min(TreeRun * Step, td - tlim);
                        t.X += (gx - t.X) / td * s;
                        t.Y += (gy - t.Y) / td * s;
                    }
                }
                var hitting = new Dictionary<int, List<Tree>>();
                foreach (Tree t in trees)
                    if (t.Target >= 0 && Dist(t.X, t.Y, px[t.Target], py[t.Target]) <= Reach)
                    {
                        if (!hitting.TryGetValue(t.Target, out var list)) hitting[t.Target] = list = new List<Tree>();
                        list.Add(t);
                    }
                // ---- the raid chooses ----
                if (now >= nextChoice)
                {
                    nextChoice = now + 2;
                    Choose(rule, n, role, party, wiz, guard, dead, inside, tank, px, py, trees, on, castOn);
                }
                // ---- the raid acts ----
                for (int i = 0; i < n; i++)
                {
                    if (dead[i] || !inside[i]) continue;
                    int r = role[i];
                    Tree t = on[i] >= 0 && byId.TryGetValue(on[i], out Tree o) ? o : null;
                    if (t != null && t.Hp <= 0) t = null;
                    double gx, gy, lim;
                    if (r <= Melee && t != null) (gx, gy, lim) = (t.X, t.Y, Reach * 0.6);
                    else (gx, gy, lim) = (home[i].X, home[i].Y, 20.0);
                    double d = Dist(px[i], py[i], gx, gy);
                    bool moving = d > lim;
                    if (moving)
                    {
                        double s = Math.Min(Run * Step, d - lim);
                        px[i] += (gx - px[i]) / d * s;
                        py[i] += (gy - py[i]) / d * s;
                    }
                    if (r <= Melee)
                    {
                        if (t != null)
                        {
                            if (Dist(px[i], py[i], t.X, t.Y) <= Reach)
                            {
                                t.Hp -= dps * Step;
                                t.Threat[i] = t.ThreatOf(i) + dps * Step;
                            }
                        }
                        else if (on[i] < 0 && !paused && Dist(px[i], py[i], gkx, gky) <= GkReach)
                            gk -= OnGk * Step;
                        continue;
                    }
                    if (hitting.ContainsKey(i) || moving)
                    {
                        castEnd[i] = -1.0;
                        continue;
                    }
                    if (r == Caster)
                    {
                        if (on[i] == -2)
                        {
                            if (castEnd[i] < 0) castEnd[i] = now + Cast;
                            else if (now >= castEnd[i])
                            {
                                if (byId.TryGetValue(castOn[i], out Tree c) && c.Hp > 0 && Dist(px[i], py[i], c.X, c.Y) <= SpellRange)
                                {
                                    double cx = c.X, cy = c.Y;
                                    foreach (Tree u in trees)
                                        if (u.Hp > 0 && Dist(u.X, u.Y, cx, cy) <= AreaRadius)
                                        {
                                            u.Hp -= AreaHit;
                                            u.Threat[i] = u.ThreatOf(i) + AreaHit;
                                        }
                                }
                                castEnd[i] = -1.0;
                            }
                            continue;
                        }
                        castEnd[i] = -1.0;
                        if (t != null)
                        {
                            if (Dist(px[i], py[i], t.X, t.Y) <= SpellRange)
                            {
                                t.Hp -= dps * Step;
                                t.Threat[i] = t.ThreatOf(i) + dps * Step;
                            }
                        }
                        else if (!paused && Dist(px[i], py[i], gkx, gky) <= SpellRange)
                            gk -= OnGk * Step;
                        continue;
                    }
                    // healers
                    if (castEnd[i] >= 0 && now >= castEnd[i])
                    {
                        int m = castOn[i];
                        if (!dead[m] && Dist(px[i], py[i], px[m], py[m]) <= HealRange)
                        {
                            double amount = Math.Min(Heal, mx[m] - hp[m]);
                            hp[m] += amount;
                            if (hitting.TryGetValue(m, out var on_m))
                                foreach (Tree u in on_m) u.Threat[i] = u.ThreatOf(i) + 0.5 * amount;
                        }
                        castEnd[i] = -1.0;
                    }
                    if (castEnd[i] < 0)
                    {
                        int m = Patient(rule, i, n, party, dead, hp, mx, px, py, target, hitting);
                        if (m >= 0)
                        {
                            castOn[i] = m;
                            castEnd[i] = now + Cast;
                        }
                    }
                }
                trees.RemoveAll(t => t.Hp <= 0);
                maxTrees = Math.Max(maxTrees, trees.Count);
                // ---- damage on the raid ----
                foreach (int v in hitting.Keys.OrderBy(v => v))
                    if (!dead[v])
                        hp[v] -= treeHit[role[v]] * Step * hitting[v].Count(t => t.Hp > 0);
                if (!paused && Dist(gkx, gky, px[target], py[target]) <= GkReach + 20)
                {
                    if (now >= nextSwing)
                    {
                        nextSwing = now + Swing;
                        hp[target] -= tankHit * Swing * (role[target] == Tank ? 1.0 : 1.106);
                        if (rng.Next() < 0.3)
                        {
                            double cx = px[target], cy = py[target];
                            for (int i = 0; i < n; i++)
                                if (!dead[i] && Dist(px[i], py[i], cx, cy) <= HeatRadius) hp[i] -= HeatHit;
                        }
                    }
                }
                else if (now >= nextSwing)
                    nextSwing = now;
                for (int i = 0; i < n; i++)
                    if (!dead[i] && hp[i] <= 0)
                    {
                        dead[i] = true;
                        fallen++;
                    }
                if (gk <= 0) return (true, now / 60, maxTrees, fallen, 0.0);
                if (fallen * 2 >= n) return (false, now / 60, maxTrees, fallen, gk / 1000);
                now += Step;
            }
            return (false, now / 60, maxTrees, fallen, gk / 1000);
        }

        /// <summary>Whom a free healer heals (gk_mirror.patient). Before and round 2: the most hurt under 80% of its party,
        /// else of the raid. Tag and burn: his target under <see cref="T.GreenKnightTankHealBelow"/>% first
        /// (<see cref="T.GreenKnightTankPatient"/>); then the most hurt under 85%, leaving a raider a tree hits until it is under
        /// <see cref="T.GreenKnightTreedHealBelow"/>% (<see cref="T.GreenKnightHoldsHeal(int, bool)"/>).</summary>
        private static int Patient(GkRule rule, int i, int n, int[] party, bool[] dead, double[] hp, double[] mx, double[] px,
            double[] py, int target, Dictionary<int, List<Tree>> hitting)
        {
            bool tagBurn = rule == GkRule.TagBurn;
            if (tagBurn && !dead[target] && hp[target] * 100 / mx[target] < T.GreenKnightTankHealBelow &&
                Dist(px[i], py[i], px[target], py[target]) <= HealRange)
                return target;
            double below = tagBurn ? 85 : 80;
            foreach (bool own in new[] { true, false })
            {
                int best = -1;
                for (int m = 0; m < n; m++)
                {
                    if (dead[m] || own && party[m] != party[i] || hp[m] * 100 / mx[m] >= below) continue;
                    if (Dist(px[i], py[i], px[m], py[m]) > HealRange) continue;
                    if (tagBurn && hitting.ContainsKey(m) && hp[m] * 100 / mx[m] >= T.GreenKnightTreedHealBelow) continue;
                    if (best < 0 || hp[m] * mx[best] < hp[best] * mx[m]) best = m;
                }
                if (best >= 0) return best;
            }
            return -1;
        }

        private static Tree Clump(List<Tree> ts, double x, double y, int need)
        {
            Tree best = null;
            int bc = 0;
            foreach (Tree t in ts)
            {
                if (Dist(x, y, t.X, t.Y) > SpellRange) continue;
                int c = ts.Count(u => Dist(u.X, u.Y, t.X, t.Y) <= AreaRadius);
                if (c > bc) { best = t; bc = c; }
            }
            return bc >= need ? best : null;
        }

        private static double Holder(Tree t, int[] role) => t.Target >= 0 && role[t.Target] <= Melee ? t.ThreatOf(t.Target) : 0.0;

        private static int Rank(int v, int[] role, bool[] wiz) => wiz[v] ? 0 : role[v] == Healer ? 1 : role[v] == Caster ? 2 : 3;

        private static void Choose(GkRule rule, int n, int[] role, int[] party, bool[] wiz, bool[] guard, bool[] dead, bool[] inside,
            int tank, double[] px, double[] py, List<Tree> trees, int[] on, int[] castOn)
        {
            List<Tree> live = trees.Where(t => t.Hp > 0).ToList();
            bool areaLeft = Enumerable.Range(0, n).Any(i => wiz[i] && !dead[i] && inside[i]);
            for (int i = 0; i < n; i++)
            {
                if (dead[i] || !inside[i] || i == tank) continue;
                int r = role[i];
                if (r <= Melee)
                {
                    if (rule == GkRule.Before) on[i] = -1;
                    else if (rule == GkRule.Round2) on[i] = Round2Tree(i, live, role, wiz, px, py);
                    else
                    {
                        // The bots' own choice (EpicEncounterTactics.GreenKnightTagTree), on the trees in the order they grew.
                        var views = live.Select(t => new T.TagView(t.Id, Dist(px[i], py[i], t.X, t.Y), t.Target == i, t.ThreatOf(i),
                            Holder(t, role), t.Target >= 0 && t.Target != i && role[t.Target] >= Caster, Pct(t))).ToArray();
                        int pick = T.GreenKnightTagTree(views, i, guard[i], areaLeft);
                        on[i] = pick >= 0 ? live[pick].Id : -1;
                    }
                }
                else if (r == Caster)
                {
                    on[i] = -1;
                    if (rule == GkRule.Before)
                    {
                        if (wiz[i] && Clump(live, px[i], py[i], 2) is Tree c)
                        {
                            on[i] = -2;
                            castOn[i] = c.Id;
                            continue;
                        }
                        if (live.Count > 0) on[i] = live[party[i] % live.Count].Id;
                    }
                    else if (rule == GkRule.Round2)
                    {
                        if (wiz[i] && Clump(live.Where(t => Dist(t.X, t.Y, 0.0, 0.0) <= 400).ToList(), px[i], py[i], 2) is Tree c)
                        {
                            on[i] = -2;
                            castOn[i] = c.Id;
                        }
                    }
                    else if (wiz[i])
                    {
                        // The bots' own choice (EpicEncounterTactics.GreenKnightBurnTree).
                        var views = live.Select(t => new T.BurnView(t.Id, t.X, t.Y, Dist(px[i], py[i], t.X, t.Y), t.ThreatOf(i), Holder(t, role)))
                            .ToArray();
                        int aim = T.GreenKnightBurnTree(views, AreaHit, AreaRadius);
                        if (aim >= 0)
                        {
                            on[i] = -2;
                            castOn[i] = live[aim].Id;
                        }
                    }
                }
            }
        }

        /// <summary>1.2.1 round 2's peel in this model (gk_mirror.round2_tree): a tree on an area caster, then a healer, then a
        /// caster within 1,200 (spread by raider), else the most hurt on me, else him.</summary>
        private static int Round2Tree(int i, List<Tree> live, int[] role, bool[] wiz, double[] px, double[] py)
        {
            int best = 99;
            foreach (Tree t in live)
            {
                int v = t.Target;
                if (v < 0 || v == i || Dist(px[i], py[i], t.X, t.Y) > 1200) continue;
                best = Math.Min(best, Rank(v, role, wiz));
            }
            if (best <= 2)
            {
                var soft = live.Where(t => t.Target >= 0 && t.Target != i && Dist(px[i], py[i], t.X, t.Y) <= 1200 &&
                                           Rank(t.Target, role, wiz) == best).ToList();
                return soft[i % soft.Count].Id;
            }
            Tree pick = null;
            foreach (Tree t in live)
                if (t.Target == i && (pick == null || t.Hp < pick.Hp)) pick = t;
            return pick?.Id ?? -1;
        }

        // ---- the bots' own functions ------------------------------------------------------------------------------------

        [Test]
        public void TheModelUsesHisScriptsNumbers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(EncounterFloodGuard.ShippedLimit("OFGreenKnight.Trees"), Is.EqualTo(WaveCap), "trees a wave at most");
                Assert.That(T.GreenKnightGrowRadius, Is.EqualTo((int)Grow), "OFGreenKnight.SpawnTrees: every raider within 4,000");
                Assert.That(T.GreenKnightOutDistance, Is.GreaterThan(T.GreenKnightGrowRadius + 500), "out of his trees' reach, with room");
                Assert.That(T.GreenKnightOutHold, Is.GreaterThan(T.GreenKnightGrowRadius));
                Assert.That(T.GreenKnightHealDistance - TankAt.X, Is.GreaterThan(T.GreenKnightHeatRadius), "healers clear of Might of the Forest");
                Assert.That(T.GreenKnightBurnDistance - TankAt.X, Is.GreaterThan(T.GreenKnightHeatRadius), "area casters too");
                Assert.That(T.GreenKnightHealDistance + T.GreenKnightBurnDistance, Is.LessThan((int)HealRange), "the healers reach everyone");
                Assert.That(T.GreenKnightEffectiveThreat(300, 100), Is.EqualTo(300), "in full within 250");
                Assert.That(T.GreenKnightEffectiveThreat(300, 1_500), Is.EqualTo(100).Within(1e-9), "a third at 1,500 (StandardMobBrain)");
                Assert.That(T.GreenKnightEffectiveThreat(300, 2_750), Is.EqualTo(300 / 9.0).Within(1e-9));
            });
        }

        [Test]
        public void AMeleeRaiderFinishesItsTagThenPeelsThenTagsThenGoesBackToHim()
        {
            T.TagView V(long id, double d, bool onMe = false, double mine = 0, double holder = 0, bool soft = false, int pct = 100) =>
                new(id, d, onMe, mine, holder, soft, pct);
            Assert.Multiple(() =>
            {
                // On me, my tag under 240: finish it, before a healer's tree.
                Assert.That(T.GreenKnightTagTree([V(1, 200, soft: true), V(2, 100, true, 120)], 7, false, true), Is.EqualTo(1));
                // A tree on a healer or caster within 500: spread over the melee by id.
                var soft = new[] { V(1, 400, soft: true), V(2, 450, soft: true), V(3, 50) };
                Assert.That(T.GreenKnightTagTree(soft, 6, false, true), Is.EqualTo(0));
                Assert.That(T.GreenKnightTagTree(soft, 7, false, true), Is.EqualTo(1), "the next melee takes the other");
                Assert.That(T.GreenKnightTagTree([V(1, 600, soft: true)], 1, false, true), Is.EqualTo(-1), "out of reach: on him");
                // Untagged trees within 300: one on me first, then the nearest; a held one (240) is left to the area casters.
                Assert.That(T.GreenKnightTagTree([V(1, 100), V(2, 250, onMe: true)], 1, false, true), Is.EqualTo(1));
                Assert.That(T.GreenKnightTagTree([V(1, 200), V(2, 100), V(3, 50, holder: 240)], 1, false, true), Is.EqualTo(1));
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240)], 1, false, true), Is.EqualTo(-1), "tagged, unburned: on him");
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240, pct: 50), V(2, 100, true, 240, 240, pct: 30)], 1, false, true),
                    Is.EqualTo(1), "burned under half: finish the most hurt");
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240, pct: 70)], 1, false, false), Is.EqualTo(0), "no area caster: kill my own");
                Assert.That(T.GreenKnightTagTree([V(1, 280, holder: 300, pct: 90)], 1, true, true), Is.EqualTo(0), "a guard kills those near it");
                Assert.That(T.GreenKnightTagTree([V(1, 350, holder: 300)], 1, true, true), Is.EqualTo(-1));
                Assert.That(T.GreenKnightTagTree(Array.Empty<T.TagView>(), 1, false, true), Is.EqualTo(-1));
            });
        }

        [Test]
        public void AnAreaCasterBurnsOnlyHeldTreesItsSpellWouldNotPull()
        {
            T.BurnView B(long id, double x, double d, double mine, double holder) => new(id, x, 0, d, mine, holder);
            Assert.Multiple(() =>
            {
                // Two held trees together: aim at them.
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 240), B(2, 100, 320, 0, 240)], 134, 350), Is.EqualTo(0));
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 240)], 134, 350), Is.EqualTo(-1), "one alone: not worth it");
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 100), B(2, 100, 320, 0, 100)], 134, 350), Is.EqualTo(-1), "untagged: it would pull them");
                // My threat after the spell must stay under the holder's as the tree counts it: close, one spell; far, more.
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 200, 134, 240), B(2, 100, 200, 134, 240)], 134, 350), Is.EqualTo(-1));
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 1_400, 268, 240), B(2, 100, 1_400, 268, 240)], 134, 350), Is.EqualTo(0));
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 1_600, 0, 240), B(2, 100, 1_600, 0, 240)], 134, 350), Is.EqualTo(-1), "out of range");
                // The biggest clump.
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 240), B(2, 1_000, 300, 0, 240), B(3, 1_100, 300, 0, 240), B(4, 1_200, 300, 0, 240)],
                    134, 350), Is.EqualTo(1), "the first of the three together");
            });
        }

        [Test]
        public void AHealWaitsOnARaiderATreeHitsUntilItIsUnderHalf()
        {
            Assert.That(T.GreenKnightHoldsHeal(80, true), Is.True);
            Assert.That(T.GreenKnightHoldsHeal(50, true), Is.True);
            Assert.That(T.GreenKnightHoldsHeal(49, true), Is.False, "under half: healed");
            Assert.That(T.GreenKnightHoldsHeal(20, false), Is.False, "no tree on it");
        }

        [Test]
        public void TheSpotsLieAwayFromHisTargetAndTheOutsideOppositeTheHealers()
        {
            Vector3 boss = new(0, 0, 0), target = new(200, 0, 0);
            Vector3 heal = T.GreenKnightAwaySpot(boss, target, T.GreenKnightHealDistance, null);
            Assert.That(heal.X, Is.EqualTo(-T.GreenKnightHealDistance).Within(0.5));
            Assert.That(T.GreenKnightAwaySpot(boss, new(0, 200, 0), T.GreenKnightHealDistance, heal), Is.EqualTo(heal), "his tank moved: it stays");
            Assert.That(T.GreenKnightAwaySpot(boss, heal, T.GreenKnightHealDistance, heal), Is.Not.EqualTo(heal), "he turned to a healer there: away from it");
            Vector3? out1 = T.GreenKnightOutSpot(new(100, 0, 0), boss, heal);
            Assert.That(out1.Value.X, Is.EqualTo(T.GreenKnightOutDistance).Within(0.5), "opposite the healers");
            Assert.That(T.GreenKnightOutSpot(new(4_400, 0, 0), boss, heal), Is.Null, "already out: stays");
        }

        // ---- the fight --------------------------------------------------------------------------------------------------

        [Test]
        public void TagAndBurnWinsAtHisPeriodSizeBuffedWhereTheOldRulesLoseAtEverySize()
        {
            // The Python mirror's 8-seed grid, exact (its 32-seed sweep is in the PR and docs/RAID-SIZES.md): 80 a second on a
            // tree; high = unbuffed and no resists, low = a capped spec AF buff and 20% resists.
            int[] sizes = [40, 56, 80, 104];
            var won = new Dictionary<(GkRule, bool, int), int>();
            foreach (GkRule rule in Enum.GetValues<GkRule>())
                foreach (bool high in new[] { true, false })
                    foreach (int n in sizes)
                    {
                        var r = Enumerable.Range(0, 8).Select(s => GreenKnightFight(s, rule, n, 80.0, high)).ToArray();
                        won[(rule, high, n)] = r.Count(x => x.Won);
                        TestContext.WriteLine($"{rule} {(high ? "high" : "low")} {n}: won {r.Count(x => x.Won)}/8, " +
                                              $"{r.Min(x => x.Minutes):0.0}-{r.Max(x => x.Minutes):0.0} min, at most {r.Max(x => x.MaxTrees)} trees, " +
                                              $"his health left {r.Average(x => x.Left):0.0}%");
                    }
            string mirror = string.Join("; ", Enum.GetValues<GkRule>().SelectMany(rule => new[] { true, false }.Select(high =>
                $"{rule} {(high ? "high" : "low")}: " + string.Join(" ", sizes.Select(n => won[(rule, high, n)])))));
            TestContext.WriteLine(mirror);
            Assert.That(mirror, Is.EqualTo(ExpectedGrid));
            Assert.Multiple(() =>
            {
                foreach (int n in sizes.Where(n => n <= 80))
                    foreach (bool high in new[] { true, false })
                        Assert.That(won[(GkRule.Before, high, n)] + won[(GkRule.Round2, high, n)], Is.Zero,
                            $"the 1.2 catalog and 1.2.1 round 2 lose at {n} ({(high ? "high" : "low")}): no zerg wins without the tactic");
                Assert.That(won[(GkRule.TagBurn, false, 40)], Is.GreaterThanOrEqualTo(4), "tag and burn wins at his period size, buffed");
                Assert.That(won[(GkRule.TagBurn, false, 56)], Is.EqualTo(8));
            });
        }

        /// <summary>gk_mirror.py's grid (8 seeds a cell), pinned so the PR's and the issue's tables are this test's.</summary>
        public const string ExpectedGrid =
            "Before high: 0 0 0 0; Before low: 0 0 0 0; Round2 high: 0 0 0 0; Round2 low: 0 0 0 1; " +
            "TagBurn high: 0 0 6 8; TagBurn low: 5 8 8 8";
    }
}
