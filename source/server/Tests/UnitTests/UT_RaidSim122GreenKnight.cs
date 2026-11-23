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
    /// The Green Knight (#412, Living Server 1.2.2 rework; tests only, nothing here has been seen in a PC log). The owner
    /// (2026-11-23): every raider fights from where its attacks reach, no benched roles; the audits (2026-11-23): model his
    /// walks, the game's loss, the buffs the bots really cast, and count who contributed.
    /// <para>His side, from his script and the server: 100,000 health; his hit on his target every 3.616 s (338 a second
    /// on plate unbuffed, 292 with a capped spec AF buff: UT_RaidRealStats122's formulas; x 1.106 on mail); Might of the
    /// Forest (200 after resists, assumed) on 30% of his swings on every raider and pet within 500 of his target; every
    /// 40-60 s a random healer within 2,500 (he walks to it at 400 and fights it 6 s, assumed); every 25-35 s a tree at
    /// every living raider within 4,000 (never at a pet), at most 70 a wave; a tree takes one living thing within 800
    /// (CheckNpcAggro), walks at 225, turns to the highest threat counted less past 250 (<see cref="T.GreenKnightEffectiveThreat"/>),
    /// silences a caster or healer it hits; at every 10% he walks at 400 to one of his four points (GkTeleport, from his
    /// spawn), attacks no one on the way, heals 400 there and 4 s later goes back for his target.</para>
    /// <para>The raid: parties of 8 (tank, three melee, a Wizard / a pet owner / a Theurgist, a caster without an area spell
    /// or an archer, two healers); damage and heals count only in reach (melee 200, spells 1,500, bows 1,700, heals 2,000,
    /// summons 2,000); 400 a raider-minute on him (b1b6846 ledger); dps a second on a tree (assumed); pets as described in
    /// the mirror. Lost when no raider is left fighting, or after 45 minutes. The Python mirror
    /// (tools/raid-sims/gk_mirror.py; sweeps: gk_sweep.py) runs the same model; this test pins its 8-seed grid exactly.</para>
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidSim122GreenKnight
    {
        public enum GkRule { Before, Round2, TagBurn }
        private const int Tank = 0, Melee = 1, Caster = 2, Healer = 3, Wizard = 4, Archer = 5, Owner = 6, Theurg = 7, Pet = 8, TPet = 9;
        private static readonly double[] MaxHp = [2600, 2000, 1400, 1600, 1400, 1700, 1400, 1400, 2600, 900];
        // a tree's hit a second by armour (UT_RaidRealStats122's formulas): plate, mail, cloth, leather; unbuffed / buffed
        private static readonly (double, double) Plate = (32.5, 28.1), Mail = (35.9, 31.1), Cloth = (77.7, 66.6), Leather = (44.3, 38.3);
        private static readonly (double Unbuffed, double Buffed)[] Armour = [Plate, Mail, Cloth, Mail, Cloth, Leather, Cloth, Cloth, Mail, Mail];
        private const double GkUnbuffed = 338, GkBuffed = 292.5;
        private const double Step = 0.25, TreeHp = 800, TreeRun = 225, Run = 191, TreeAggro = 800, Grow = 4000, PickRadius = 2500,
            HeatRadius = 500, HealRange = 2000, SpellRange = 1500, BowRange = 1700, SummonRange = 2000, AreaRadius = 350, Reach = 150,
            GkReach = 220, OnGk = 400.0 / 60.0, AreaHit = 134, HeatHit = 200, Heal = 400, Cast = 3, Swing = 3.616, PickS = 6,
            PetBackS = 20, TPetLife = 30, TPetShare = 0.5, GkRun = 400, Arrive = 40, AfterHealS = 4, SpreadRing = 1000;
        private const int WaveCap = 70, TPets = 10, TankHealBelow = 98, HealBelow = 85, TreedHealBelow = 35;
        private static readonly (double X, double Y)[] Points = [(203, -2206), (266, 2093), (3063, 1484), (-2114, -635)];
        private static readonly (double X, double Y) TankAt = (-180, 0), FightAt = (180, 0), StackAt = (750, 0),
            HealSpot = (T.GreenKnightHealDistance, 0), BurnSpot = (T.GreenKnightBurnDistance, 0), OutSpot = (-T.GreenKnightOutDistance, 0),
            ShootersAt = (T.GreenKnightShootDistance, 0), TheurgAt = (T.GreenKnightTheurgistDistance, 0);
        public static readonly string[] RoleNames = ["tank", "melee", "caster", "healer", "wizard", "archer", "petowner", "theurgist", "pet", "elemental"];

        private static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool Soft(int r) => r >= Caster && r <= Theurg;
        private static bool Fighter(int r) => r <= Melee || r >= Pet;
        private static double ShootRange(int r) => r == Archer ? BowRange : SpellRange;

        private sealed class Tree
        {
            public int Id, Target = -1;
            public double Hp = TreeHp, X, Y, Hx, Hy;
            public readonly SortedDictionary<int, double> Threat = new();
            public double ThreatOf(int i) => Threat.TryGetValue(i, out double v) ? v : 0.0;
        }

        private static int Pct(Tree t) => (int)Math.Floor(t.Hp * 100 / TreeHp);

        public static int[] Layout(int n) => Enumerable.Range(0, n).Select(i =>
        {
            int p = i / 8, s = i % 8;
            return s == 0 ? Tank : s <= 3 ? Melee : s == 4 ? (p % 2 == 0 ? Wizard : p % 4 == 1 ? Owner : Theurg)
                : s == 5 ? (p % 2 == 0 ? Caster : Archer) : Healer;
        }).ToArray();

        /// <summary>What each role did in one fight: damage on him, on trees, trees killed, heals; and how many raiders did
        /// anything (a pet's work counts for its owner).</summary>
        public sealed class Stats
        {
            public readonly double[] OnHim = new double[10], OnTrees = new double[10], Heals = new double[10];
            public readonly int[] Kills = new int[10];
            public int Contributed, Walks;
        }

        /// <summary>The fight (gk_mirror.fight). Returns whether he died, the minutes, the most trees alive, the fallen raiders
        /// and his health left in percent.</summary>
        public static (bool Won, double Minutes, int MaxTrees, int Fallen, double Left) GreenKnightFight(int seed, GkRule rule, int n,
            double dps, bool buffed, int minutes = 45, Stats stats = null)
        {
            double tankHit = buffed ? GkBuffed : GkUnbuffed;
            var rng = new Rng(seed);
            var role = Layout(n).ToList();
            var owner = Enumerable.Repeat(-1, n).ToList();
            for (int i = 0; i < n; i++)
                if (role[i] == Owner) { role.Add(Pet); owner.Add(i); }
            for (int i = 0; i < n; i++)
                if (role[i] == Theurg)
                    for (int k2 = 0; k2 < TPets; k2++) { role.Add(TPet); owner.Add(i); }
            int N = role.Count;
            int[] party = Enumerable.Range(0, N).Select(i => i < n ? i / 8 : owner[i] / 8).ToArray();
            double[] mx = role.Select(r => MaxHp[r]).ToArray();
            double[] hp = (double[])mx.Clone();
            bool[] dead = Enumerable.Range(0, N).Select(i => role[i] == TPet).ToArray();
            bool[] inside = Enumerable.Repeat(true, N).ToArray();
            bool[] guard = new bool[N];
            var home = Enumerable.Repeat(FightAt, N).ToArray();
            int tank = 0, k = 0, softCount = Enumerable.Range(0, n).Count(i => Soft(role[i]));
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
                else if (r == Theurg) home[i] = TheurgAt;
                else if (r == Wizard) home[i] = BurnSpot;
                else if (r == Owner) { home[i] = OutSpot; inside[i] = false; }
                else home[i] = ShootersAt;
            }
            if (rule == GkRule.TagBurn)
            {
                for (int p = 0; p <= party[n - 1]; p++)
                {
                    if (!Enumerable.Range(0, n).Any(i => role[i] == Wizard && party[i] == p)) continue;
                    int g = 0;
                    for (int i = 0; i < n; i++)
                        if (party[i] == p && role[i] == Melee && g < T.GreenKnightGuardsPerParty)
                        {
                            guard[i] = true;
                            home[i] = BurnSpot;
                            g++;
                        }
                }
                for (int i = n; i < N; i++)
                    if (role[i] == Pet) { home[i] = BurnSpot; guard[i] = true; }
            }
            double[] px = home.Select(h => h.X).ToArray(), py = home.Select(h => h.Y).ToArray();
            for (int i = n; i < N; i++)
                if (role[i] == TPet) { px[i] = px[owner[i]]; py[i] = py[owner[i]]; }
            int[] on = Enumerable.Repeat(-1, N).ToArray(), castOn = Enumerable.Repeat(-1, N).ToArray();
            double[] castEnd = Enumerable.Repeat(-1.0, N).ToArray(), expire = new double[N], backAt = Enumerable.Repeat(-1.0, N).ToArray();
            var trees = new List<Tree>();
            var byId = new Dictionary<int, Tree>();
            int nid = 0, walks = 0, fallen = 0, maxTrees = 0, picked = -1, lastPoint = -1;
            double now = 0, gk = 100_000, gkx = 0, gky = 0, pickUntil = -1, nextSwing = Swing, nextChoice = 0, walkEnd = -1;
            double ox = 0, oy = 0, wx = 0, wy = 0;
            bool walking = false;
            double nextWave = 25 + rng.Below(10_001) / 1000.0, nextPick = 40 + rng.Below(20_001) / 1000.0;
            double end = minutes * 60.0;
            double[] contrib = new double[n];
            int[] credit = Enumerable.Range(0, N).Select(i => i < n ? i : owner[i]).ToArray();
            var lastHit = new Dictionary<int, int>();
            stats ??= new Stats();
            void HitTree(Tree t, int i, double d)
            {
                contrib[credit[i]] += d;
                t.Hp -= d;
                t.Threat[i] = t.ThreatOf(i) + d;
                stats.OnTrees[role[i]] += d;
                lastHit[t.Id] = role[i];
            }
            (bool, double, int, int, double)? result = null;
            while (now < end)
            {
                bool paused = walking;
                if (now >= nextWave)
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
                if (now >= nextPick && !walking)
                {
                    var hs = Enumerable.Range(0, n).Where(i => role[i] == Healer && !dead[i] && Dist(px[i], py[i], gkx, gky) <= PickRadius).ToList();
                    if (hs.Count > 0)
                    {
                        picked = hs[rng.Below(hs.Count)];
                        pickUntil = now + PickS;
                    }
                    nextPick = now + 40 + rng.Below(20_001) / 1000.0;
                }
                if ((int)Math.Floor((100_000 - gk) / 10_000) > walks && walks < 9 && !walking)
                {
                    walks++;
                    var choices = Enumerable.Range(0, 4).Where(q => q != lastPoint).ToList();
                    lastPoint = choices[rng.Below(choices.Count)];
                    (wx, wy) = Points[lastPoint];
                    if (rule != GkRule.TagBurn) (ox, oy) = (wx, wy);
                    walking = true;
                    walkEnd = -1;
                    pickUntil = -1;
                    paused = true;
                }
                if (walking)
                {
                    double d = Dist(gkx, gky, wx, wy);
                    if (d > Arrive)
                    {
                        double s = Math.Min(GkRun * Step, d);
                        gkx += (wx - gkx) / d * s;
                        gky += (wy - gky) / d * s;
                    }
                    else if (walkEnd < 0)
                    {
                        walkEnd = now + AfterHealS;
                        gk = Math.Min(100_000.0, gk + 400);
                    }
                    else if (now >= walkEnd)
                        walking = false;
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
                    guard[tank] = false;
                }
                int target = now < pickUntil && picked >= 0 && !dead[picked] ? picked : tank;
                if (!walking)
                {
                    (double ax, double ay) = target != tank ? (px[target], py[target]) : (ox, oy);
                    double d = Dist(gkx, gky, ax, ay), lim = target != tank ? 180.0 : 0.0;
                    if (d > lim)
                    {
                        double s = Math.Min(400 * Step, d - lim);
                        gkx += (ax - gkx) / d * s;
                        gky += (ay - gky) / d * s;
                    }
                }
                // ---- pets come and go ----
                for (int i = n; i < N; i++)
                {
                    if (role[i] == Pet && dead[i] && backAt[i] >= 0 && now >= backAt[i] && !dead[owner[i]])
                    {
                        dead[i] = false;
                        hp[i] = mx[i];
                        px[i] = px[owner[i]]; py[i] = py[owner[i]];
                        backAt[i] = -1.0;
                    }
                    else if (role[i] == TPet && !dead[i])
                    {
                        Tree t = on[i] >= 0 && byId.TryGetValue(on[i], out Tree o) ? o : null;
                        if (now >= expire[i] || on[i] >= 0 && (t == null || t.Hp <= 0)) dead[i] = true;
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
                            var near = Enumerable.Range(0, N).Where(i => !dead[i] && inside[i] && Dist(px[i], py[i], t.X, t.Y) <= TreeAggro).ToList();
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
                    Choose(rule, n, role, party, guard, dead, inside, tank, px, py, trees, on, castOn, dps);
                }
                // ---- the raid acts ----
                for (int i = 0; i < N; i++)
                {
                    if (dead[i] || !inside[i]) continue;
                    int r = role[i];
                    Tree t = on[i] >= 0 && byId.TryGetValue(on[i], out Tree o) ? o : null;
                    if (t != null && t.Hp <= 0) t = null;
                    bool meleeLike = r <= Melee || r >= Pet;
                    double gx, gy, lim;
                    if (meleeLike && t != null) (gx, gy, lim) = (t.X, t.Y, Reach * 0.6);
                    else if (r == TPet || r == Pet) (gx, gy, lim) = (gkx, gky, GkReach * 0.8);
                    else (gx, gy, lim) = (ox + home[i].X, oy + home[i].Y, 20.0);
                    double d = Dist(px[i], py[i], gx, gy);
                    bool moving = d > lim;
                    if (moving)
                    {
                        double s = Math.Min(Run * Step, d - lim);
                        px[i] += (gx - px[i]) / d * s;
                        py[i] += (gy - py[i]) / d * s;
                    }
                    if (meleeLike)
                    {
                        double share = r == TPet ? TPetShare : 1.0;
                        if (t != null)
                        {
                            if (Dist(px[i], py[i], t.X, t.Y) <= Reach) HitTree(t, i, dps * share * Step);
                        }
                        else if (on[i] < 0 && Dist(px[i], py[i], gkx, gky) <= GkReach)
                        {
                            gk -= OnGk * share * Step;
                            stats.OnHim[r] += OnGk * share * Step;
                            contrib[credit[i]] += OnGk * share * Step;
                        }
                        continue;
                    }
                    if (hitting.ContainsKey(i) || moving)
                    {
                        castEnd[i] = -1.0;
                        continue;
                    }
                    if (r == Wizard && on[i] == -2)
                    {
                        if (castEnd[i] < 0) castEnd[i] = now + Cast;
                        else if (now >= castEnd[i])
                        {
                            if (byId.TryGetValue(castOn[i], out Tree c) && c.Hp > 0 && Dist(px[i], py[i], c.X, c.Y) <= SpellRange)
                            {
                                double cx = c.X, cy = c.Y;
                                foreach (Tree u in trees)
                                    if (u.Hp > 0 && Dist(u.X, u.Y, cx, cy) <= AreaRadius) HitTree(u, i, AreaHit);
                            }
                            castEnd[i] = -1.0;
                        }
                        continue;
                    }
                    if (r == Theurg)
                    {
                        if (castEnd[i] < 0) castEnd[i] = now + Cast;
                        else if (now >= castEnd[i])
                        {
                            castEnd[i] = -1.0;
                            Tree c = castOn[i] >= 0 && byId.TryGetValue(castOn[i], out Tree cc) ? cc : null;
                            double reach = c != null && c.Hp > 0 ? Dist(px[i], py[i], c.X, c.Y) : Dist(px[i], py[i], gkx, gky);
                            if ((castOn[i] < 0 || c != null && c.Hp > 0) && reach <= SummonRange)
                                for (int e = n; e < N; e++)
                                    if (role[e] == TPet && owner[e] == i && dead[e])
                                    {
                                        dead[e] = false;
                                        hp[e] = mx[e];
                                        px[e] = px[i]; py[e] = py[i];
                                        on[e] = castOn[i];
                                        expire[e] = now + TPetLife;
                                        break;
                                    }
                        }
                        continue;
                    }
                    if (r != Healer)
                    {
                        castEnd[i] = -1.0;
                        if (t != null)
                        {
                            if (Dist(px[i], py[i], t.X, t.Y) <= ShootRange(r)) HitTree(t, i, dps * Step);
                        }
                        else if (Dist(px[i], py[i], gkx, gky) <= ShootRange(r))
                        {
                            gk -= OnGk * Step;
                            stats.OnHim[r] += OnGk * Step;
                            contrib[i] += OnGk * Step;
                        }
                        continue;
                    }
                    if (castEnd[i] >= 0 && now >= castEnd[i])
                    {
                        int m = castOn[i];
                        if (!dead[m] && Dist(px[i], py[i], px[m], py[m]) <= HealRange)
                        {
                            double amount = Math.Min(Heal, mx[m] - hp[m]);
                            hp[m] += amount;
                            stats.Heals[Healer] += amount;
                            contrib[i] += amount;
                            if (hitting.TryGetValue(m, out var onM))
                                foreach (Tree u in onM) u.Threat[i] = u.ThreatOf(i) + 0.5 * amount;
                        }
                        castEnd[i] = -1.0;
                    }
                    if (castEnd[i] < 0)
                    {
                        int m = Patient(rule, i, n, role, party, dead, hp, mx, px, py, target, hitting);
                        if (m >= 0)
                        {
                            castOn[i] = m;
                            castEnd[i] = now + Cast;
                        }
                    }
                }
                foreach (Tree t in trees)
                    if (t.Hp <= 0) stats.Kills[lastHit[t.Id]]++;
                trees.RemoveAll(t => t.Hp <= 0);
                maxTrees = Math.Max(maxTrees, trees.Count);
                // ---- damage on the raid and its pets ----
                foreach (int v in hitting.Keys.OrderBy(v => v))
                    if (!dead[v])
                        hp[v] -= (buffed ? Armour[role[v]].Buffed : Armour[role[v]].Unbuffed) * Step * hitting[v].Count(t => t.Hp > 0);
                if (!paused && Dist(gkx, gky, px[target], py[target]) <= GkReach + 20)
                {
                    if (now >= nextSwing)
                    {
                        nextSwing = now + Swing;
                        hp[target] -= tankHit * Swing * (role[target] == Tank ? 1.0 : 1.106);
                        if (rng.Next() < 0.3)
                        {
                            double cx = px[target], cy = py[target];
                            for (int i = 0; i < N; i++)
                                if (!dead[i] && inside[i] && Dist(px[i], py[i], cx, cy) <= HeatRadius) hp[i] -= HeatHit;
                        }
                    }
                }
                else if (now >= nextSwing)
                    nextSwing = now;
                for (int i = 0; i < N; i++)
                    if (!dead[i] && hp[i] <= 0)
                    {
                        dead[i] = true;
                        if (i < n) fallen++;
                        else if (role[i] == Pet) backAt[i] = now + PetBackS;
                    }
                if (gk <= 0) { result = (true, now / 60, maxTrees, fallen, 0.0); break; }
                if (!Enumerable.Range(0, n).Any(i => !dead[i] && inside[i])) { result = (false, now / 60, maxTrees, fallen, gk / 1000); break; }
                now += Step;
            }
            result ??= (false, now / 60, maxTrees, fallen, gk / 1000);
            stats.Contributed = contrib.Count(c => c > 0);
            stats.Walks = walks;
            return result.Value;
        }

        /// <summary>Whom a free healer heals (gk_mirror.patient). Tag and burn: his target first under 98% for the
        /// <see cref="T.GreenKnightTankHealerCount"/> healers nearest it (<see cref="T.GreenKnightTankHealers"/>); then the most
        /// hurt under 85%, leaving a raider a tree hits until under <see cref="T.GreenKnightTreedHealBelow"/>%.</summary>
        private static int Patient(GkRule rule, int i, int n, List<int> role, int[] party, bool[] dead, double[] hp, double[] mx,
            double[] px, double[] py, int target, Dictionary<int, List<Tree>> hitting)
        {
            bool tagBurn = rule == GkRule.TagBurn;
            if (tagBurn && !dead[target] && hp[target] * 100 / mx[target] < TankHealBelow &&
                Dist(px[i], py[i], px[target], py[target]) <= HealRange &&
                T.GreenKnightTankHealers(Enumerable.Range(0, n).Where(m => role[m] == Healer && !dead[m] && m != target)
                    .Select(m => ((long)m, Dist(px[m], py[m], px[target], py[target]))), T.GreenKnightTankHealerCount).Contains(i))
                return target;
            double below = tagBurn ? HealBelow : 80;
            foreach (bool own in new[] { true, false })
            {
                int best = -1;
                for (int m = 0; m < n; m++)
                {
                    if (dead[m] || own && party[m] != party[i] || hp[m] * 100 / mx[m] >= below) continue;
                    if (Dist(px[i], py[i], px[m], py[m]) > HealRange) continue;
                    if (tagBurn && hitting.ContainsKey(m) && hp[m] * 100 / mx[m] >= TreedHealBelow) continue;
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

        private static double Holder(Tree t, List<int> role) => t.Target >= 0 && Fighter(role[t.Target]) ? t.ThreatOf(t.Target) : 0.0;

        private static void Choose(GkRule rule, int n, List<int> role, int[] party, bool[] guard, bool[] dead, bool[] inside, int tank,
            double[] px, double[] py, List<Tree> trees, int[] on, int[] castOn, double dps)
        {
            List<Tree> live = trees.Where(t => t.Hp > 0).ToList();
            bool areaLeft = Enumerable.Range(0, n).Any(i => role[i] == Wizard && !dead[i]);
            for (int i = 0; i < role.Count; i++)
            {
                if (i >= n && !(role[i] == Pet && guard[i])) continue;
                if (dead[i] || !inside[i] || i == tank) continue;
                int r = role[i];
                if (r <= Melee || r == Pet)
                {
                    if (rule == GkRule.Before) on[i] = -1;
                    else if (rule == GkRule.Round2) on[i] = Round2Tree(i, live, role, px, py);
                    else
                    {
                        // The bots' own choice (EpicEncounterTactics.GreenKnightTagTree), on the trees in the order they grew.
                        var views = live.Select(t => new T.TagView(t.Id, Dist(px[i], py[i], t.X, t.Y), t.Target == i, t.ThreatOf(i),
                            Holder(t, role), t.Target >= 0 && t.Target != i && Soft(role[t.Target]), Pct(t))).ToArray();
                        int pick = T.GreenKnightTagTree(views, i, guard[i], areaLeft);
                        on[i] = pick >= 0 ? live[pick].Id : -1;
                    }
                    continue;
                }
                if (r == Healer) continue;
                on[i] = -1;
                if (r == Theurg) castOn[i] = -1;
                if (rule == GkRule.Before)
                {
                    if (r == Wizard && Clump(live, px[i], py[i], 2) is Tree c)
                    {
                        on[i] = -2;
                        castOn[i] = c.Id;
                        continue;
                    }
                    if (live.Count > 0)
                    {
                        int pick = live[party[i] % live.Count].Id;
                        if (r == Theurg) castOn[i] = pick;
                        else on[i] = pick;
                    }
                }
                else if (rule == GkRule.Round2)
                {
                    if (r == Wizard && Clump(live.Where(t => Dist(t.X, t.Y, 0.0, 0.0) <= 400).ToList(), px[i], py[i], 2) is Tree c)
                    {
                        on[i] = -2;
                        castOn[i] = c.Id;
                    }
                }
                else if (r == Wizard)
                {
                    var views = live.Select(t => new T.BurnView(t.Id, t.X, t.Y, Dist(px[i], py[i], t.X, t.Y), t.ThreatOf(i), Holder(t, role))).ToArray();
                    int aim = T.GreenKnightBurnTree(views, AreaHit, AreaRadius);
                    if (aim >= 0)
                    {
                        on[i] = -2;
                        castOn[i] = live[aim].Id;
                    }
                }
                else if (r == Theurg)
                {
                    int aim = T.GreenKnightSummonTree(live.Select(t => (Dist(px[i], py[i], t.X, t.Y), t.Target >= 0 && Soft(role[t.Target]))).ToArray());
                    castOn[i] = aim >= 0 ? live[aim].Id : -1;
                }
                else
                {
                    var views = live.Select(t => new T.ShotView(t.Id, Dist(px[i], py[i], t.X, t.Y), t.ThreatOf(i), Holder(t, role), Pct(t))).ToArray();
                    int shot = T.GreenKnightShotTree(views, dps * 2, ShootRange(r));
                    on[i] = shot >= 0 ? live[shot].Id : -1;
                }
            }
        }

        /// <summary>1.2.1 round 2's peel in this model (gk_mirror.round2_tree).</summary>
        private static int Round2Tree(int i, List<Tree> live, List<int> role, double[] px, double[] py)
        {
            int Rank(int v) => role[v] == Wizard ? 0 : role[v] == Healer ? 1 : Soft(role[v]) ? 2 : 3;
            int best = 99;
            foreach (Tree t in live)
            {
                int v = t.Target;
                if (v < 0 || v == i || v >= px.Length || Dist(px[i], py[i], t.X, t.Y) > 1200) continue;
                best = Math.Min(best, Rank(v));
            }
            if (best <= 2)
            {
                var cands = live.Where(t => t.Target >= 0 && t.Target != i && Dist(px[i], py[i], t.X, t.Y) <= 1200 && Rank(t.Target) == best).ToList();
                return cands[i % cands.Count].Id;
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
                Assert.That(T.GreenKnightOutDistance, Is.GreaterThan(T.GreenKnightGrowRadius + 500), "pet owners out of his trees' reach");
                Assert.That(T.GreenKnightHealDistance - TankAt.X, Is.GreaterThan(T.GreenKnightHeatRadius), "healers clear of Might of the Forest");
                Assert.That(T.GreenKnightHealDistance - FightAt.X, Is.LessThanOrEqualTo(T.GreenKnightPeelSoftReach), "in the melee's peel reach");
                Assert.That(T.GreenKnightShootDistance, Is.LessThan((int)SpellRange), "casters reach him from their spot");
                Assert.That(T.GreenKnightTheurgistDistance, Is.LessThan(T.GreenKnightSummonReach), "Theurgists summon at him from theirs");
                Assert.That(T.GreenKnightEffectiveThreat(300, 1_500), Is.EqualTo(100).Within(1e-9), "a third at 1,500 (StandardMobBrain)");
                // His four walk points, from his spawn (OFGreenKnight.GkTeleport and his ScriptLoaded spawn).
                (int X, int Y) spawn = (592990, 418687);
                (int X, int Y)[] script = [(593193, 416481), (593256, 420780), (596053, 420171), (590876, 418052)];
                Assert.That(script.Select(p => (double)(p.X - spawn.X)).Zip(script.Select(p => (double)(p.Y - spawn.Y))),
                    Is.EqualTo(Points.Select(p => (p.X, p.Y))));
            });
        }

        [Test]
        public void AMeleeRaiderFinishesItsTagThenPeelsThenTagsThenGoesBackToHim()
        {
            T.TagView V(long id, double d, bool onMe = false, double mine = 0, double holder = 0, bool soft = false, int pct = 100) =>
                new(id, d, onMe, mine, holder, soft, pct);
            Assert.Multiple(() =>
            {
                Assert.That(T.GreenKnightTagTree([V(1, 200, soft: true), V(2, 100, true, 120)], 7, false, true), Is.EqualTo(1));
                var soft = new[] { V(1, 400, soft: true), V(2, 450, soft: true), V(3, 50) };
                Assert.That(T.GreenKnightTagTree(soft, 6, false, true), Is.EqualTo(0));
                Assert.That(T.GreenKnightTagTree(soft, 7, false, true), Is.EqualTo(1), "the next melee takes the other");
                Assert.That(T.GreenKnightTagTree([V(1, 600, soft: true)], 1, false, true), Is.EqualTo(-1), "out of reach: on him");
                Assert.That(T.GreenKnightTagTree([V(1, 100), V(2, 250, onMe: true)], 1, false, true), Is.EqualTo(1));
                Assert.That(T.GreenKnightTagTree([V(1, 200), V(2, 100), V(3, 50, holder: 240)], 1, false, true), Is.EqualTo(1));
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240)], 1, false, true), Is.EqualTo(-1), "tagged, unburned: on him");
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240, pct: 50), V(2, 100, true, 240, 240, pct: 30)], 1, false, true),
                    Is.EqualTo(1), "burned under half: finish the most hurt");
                Assert.That(T.GreenKnightTagTree([V(1, 100, true, 240, 240, pct: 70)], 1, false, false), Is.EqualTo(0), "no area caster: kill my own");
                Assert.That(T.GreenKnightTagTree([V(1, 280, holder: 300, pct: 90)], 1, true, true), Is.EqualTo(0), "a guard kills those near it");
                Assert.That(T.GreenKnightTagTree(Array.Empty<T.TagView>(), 1, false, true), Is.EqualTo(-1));
            });
        }

        [Test]
        public void AnAreaCasterBurnsAndAShooterShootsOnlyHeldTreesTheyWouldNotPull()
        {
            T.BurnView B(long id, double x, double d, double mine, double holder) => new(id, x, 0, d, mine, holder);
            Assert.Multiple(() =>
            {
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 240), B(2, 100, 320, 0, 240)], 134, 350), Is.EqualTo(0));
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 240)], 134, 350), Is.EqualTo(-1), "one alone: not worth it");
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 300, 0, 100), B(2, 100, 320, 0, 100)], 134, 350), Is.EqualTo(-1), "untagged");
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 200, 134, 240), B(2, 100, 200, 134, 240)], 134, 350), Is.EqualTo(-1), "would pull them");
                Assert.That(T.GreenKnightBurnTree([B(1, 0, 1_400, 268, 240), B(2, 100, 1_400, 268, 240)], 134, 350), Is.EqualTo(0));
                // A shooter: the most hurt tagged tree in reach its shots would not pull.
                T.ShotView S(long id, double d, double mine, double holder, int pct) => new(id, d, mine, holder, pct);
                Assert.That(T.GreenKnightShotTree([S(1, 1_300, 0, 400, 80), S(2, 1_300, 0, 400, 40)], 300, 1_500), Is.EqualTo(1));
                Assert.That(T.GreenKnightShotTree([S(1, 1_300, 0, 100, 40)], 300, 1_500), Is.EqualTo(-1), "untagged: it would pull it");
                Assert.That(T.GreenKnightShotTree([S(1, 200, 0, 250, 40)], 300, 1_500), Is.EqualTo(-1), "close: its shots would pull it");
                Assert.That(T.GreenKnightShotTree([S(1, 1_600, 0, 400, 40)], 300, 1_500), Is.EqualTo(-1), "out of reach");
                // A Theurgist: the nearest tree on a soft raider within 2,000.
                Assert.That(T.GreenKnightSummonTree([(900, true), (500, false), (700, true)]), Is.EqualTo(2));
                Assert.That(T.GreenKnightSummonTree([(2_100, true)]), Is.EqualTo(-1));
            });
        }

        [Test]
        public void HisSpotsStayWhileHeWalksOrFightsAHealerAndSnapToTheNavmesh()
        {
            Assert.Multiple(() =>
            {
                Assert.That(T.GreenKnightKeepsPlan(false, true, true, 0), Is.False, "no plan yet");
                Assert.That(T.GreenKnightKeepsPlan(true, true, false, 3_000), Is.True, "he walks: the raid holds its ground");
                Assert.That(T.GreenKnightKeepsPlan(true, false, true, 900), Is.True, "he fights a healer he picked");
                Assert.That(T.GreenKnightKeepsPlan(true, false, false, 600), Is.True);
                Assert.That(T.GreenKnightKeepsPlan(true, false, false, 601), Is.False, "he came back somewhere else: new spots");
                Vector3[] c = T.GreenKnightSpotCandidates(new(0, 0, 0), new(1, 0), 650);
                Assert.That(c[0].X, Is.EqualTo(650).Within(0.01));
                Assert.That(c.Select(v => Math.Round(Math.Sqrt(v.X * v.X + v.Y * v.Y))), Is.All.EqualTo(650), "all on the circle");
                Vector3 picked = T.GreenKnightPickSpot(c, v => v.Y > 1 ? v with { Z = 12 } : null, v => true);
                Assert.That((picked.Y, picked.Z), Is.EqualTo((c[1].Y, 12f)), "the first with a floor, snapped to it");
                Assert.That(T.GreenKnightPickSpot(c, v => v, v => v.Y < -1), Is.EqualTo(c[2]), "the first that sees him");
                Assert.That(T.GreenKnightPickSpot(c, v => null, v => true), Is.EqualTo(c[0]), "no mesh: as it is");
                Assert.That(T.GreenKnightSlot(new(0, 0, 0), 7), Is.EqualTo(new Vector3(0, 0, 0)));
                Assert.That(Vector3.Distance(T.GreenKnightSlot(new(0, 0, 0), 3), Vector3.Zero), Is.EqualTo(T.GreenKnightSlotRing).Within(0.5));
                Assert.That(T.GreenKnightTankHealers([(5, 300.0), (2, 100.0), (9, 100.0), (4, 900.0), (7, 50.0)], 3),
                    Is.EquivalentTo(new long[] { 7, 2, 9 }), "the nearest four (here three), the lower id on a tie");
                Assert.That(T.GreenKnightHoldsHeal(T.GreenKnightTreedHealBelow, true), Is.True);
                Assert.That(T.GreenKnightHoldsHeal(T.GreenKnightTreedHealBelow - 1, true), Is.False);
            });
        }

        // ---- the fight --------------------------------------------------------------------------------------------------

        [Test]
        public void WithHisWalksAndTheBuffsTheBotsCastTagAndBurnStillLosesAtFortyAndWinsFromFiftySix()
        {
            // The Python mirror's 8-seed grid, exact (its 32-seed sweep is in the PR, #412 and docs/RAID-SIZES.md). 80 a second
            // on a tree; buffed = a Cleric's or Paladin's capped spec AF, the only buff Albion bots cast against his slashes.
            int[] sizes = [40, 56, 80];
            var won = new Dictionary<(GkRule, bool, int), int>();
            foreach (GkRule rule in Enum.GetValues<GkRule>())
                foreach (bool buffed in new[] { false, true })
                    foreach (int n in sizes)
                    {
                        var all = new List<(bool Won, double Minutes, int MaxTrees, int Fallen, double Left)>();
                        int least = int.MaxValue;
                        for (int s = 0; s < 8; s++)
                        {
                            var st = new Stats();
                            all.Add(GreenKnightFight(s, rule, n, 80.0, buffed, stats: st));
                            least = Math.Min(least, st.Contributed);
                        }
                        won[(rule, buffed, n)] = all.Count(x => x.Won);
                        TestContext.WriteLine($"{rule} {(buffed ? "buffed" : "unbuffed")} {n}: won {all.Count(x => x.Won)}/8, " +
                                              $"{all.Min(x => x.Minutes):0.0}-{all.Max(x => x.Minutes):0.0} min, his health left {all.Average(x => x.Left):0.0}%, " +
                                              $"at least {least} of {n} raiders did something");
                        if (rule == GkRule.TagBurn)
                            Assert.That(least, Is.EqualTo(n), $"no benched role at {n} ({(buffed ? "buffed" : "unbuffed")}): every raider (a pet's owner by its pet) contributes");
                    }
            string mirror = string.Join("; ", Enum.GetValues<GkRule>().SelectMany(rule => new[] { false, true }.Select(buffed =>
                $"{rule} {(buffed ? "buffed" : "unbuffed")}: " + string.Join(" ", sizes.Select(n => won[(rule, buffed, n)])))));
            TestContext.WriteLine(mirror);
            Assert.That(mirror, Is.EqualTo(ExpectedGrid));
            Assert.Multiple(() =>
            {
                foreach (int n in sizes)
                    foreach (bool buffed in new[] { false, true })
                        Assert.That(won[(GkRule.Before, buffed, n)] + won[(GkRule.Round2, buffed, n)], Is.Zero,
                            $"the 1.2 catalog and 1.2.1 round 2 lose at {n}: more bots alone don't win");
                Assert.That(won[(GkRule.TagBurn, true, 40)], Is.Zero, "honest: not yet at his period size");
            });
        }

        /// <summary>gk_mirror.py's grid (8 seeds a cell), pinned so the PR's and the issue's tables are this test's.</summary>
        public const string ExpectedGrid =
            "Before unbuffed: 0 0 0; Before buffed: 0 0 0; Round2 unbuffed: 0 0 0; Round2 buffed: 0 0 0; " +
            "TagBurn unbuffed: 0 0 8; TagBurn buffed: 0 7 8";
    }
}
