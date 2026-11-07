using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// RvR perception, measured the way the North Star audit asked (2026-11-03, technical §8.2): seeded Monte Carlo runs
    /// of the real rules (AutonomousRvrPerception.Scan, its memory, the observer budget, CallRecipients, the frontier
    /// scan's cadence and its 8-ray window). Nothing here reads the game world or plays it: positions, line of sight and
    /// facing are drawn, and every bot is a level-50 bot without a personality.
    /// </summary>
    [TestFixture]
    public class UT_RvrPerceptionCalibration
    {
        private const int Seed = 2003;

        private static RvrSighting Plain(double distance, bool facing = false) => new(distance, false, false, 1, facing, false);

        /// <summary>
        /// One bot looking on its own: the first sight comes at a random moment between two of its scans (scans every
        /// 1,000-1,249 ms by id, or a fixed slower interval), and it rolls on each scan until it notices. Returns the seconds.
        /// </summary>
        private static double[] TimesToNotice(RvrAwarenessProfile profile, double distance, int samples, Random random,
            int? fixedInterval = null, double horizonSeconds = 600)
        {
            var times = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                int interval = fixedInterval ?? AutonomousFrontierThreatPolicy.IntervalMilliseconds(random.NextInt64(1, 1_000_000));
                double chance = AutonomousRvrPerception.NoticeChance(Plain(distance), profile, interval, out _);
                double t = random.NextDouble() * interval;
                while (t < horizonSeconds * 1000 && random.NextDouble() >= chance)
                    t += interval;
                times[i] = t / 1000;
            }
            return times;
        }

        private static double Median(double[] values)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        // Targets: the audit's for normal (1.5 s at 1,500, 6 s at 2,500, 20 s at 3,400); easy is twice that and never
        // reaches 3,400; veteran half. 4,000 samples per cell, seed 2003, tolerance ±10% of the target (about four
        // standard errors of a sample median at this size).
        [TestCase(RvrBotAwareness.Normal, 1_500, 1.5)]
        [TestCase(RvrBotAwareness.Normal, 2_500, 6.0)]
        [TestCase(RvrBotAwareness.Normal, 3_400, 20.0)]
        [TestCase(RvrBotAwareness.Easy, 1_500, 3.0)]
        [TestCase(RvrBotAwareness.Easy, 2_500, 12.0)]
        [TestCase(RvrBotAwareness.Veteran, 1_500, 0.75)]
        [TestCase(RvrBotAwareness.Veteran, 2_500, 3.0)]
        [TestCase(RvrBotAwareness.Veteran, 3_400, 10.0)]
        public void The_median_time_to_notice_matches_the_target_for_each_level_and_band(RvrBotAwareness level, int distance, double target)
        {
            RvrAwarenessProfile profile = AutonomousRvrPerception.Profile(level);
            double median = Median(TimesToNotice(profile, distance, 4_000, new Random(Seed + distance)));
            TestContext.Out.WriteLine($"{level} at {distance}: median {median:0.00} s (target {target} s)");
            Assert.That(median, Is.EqualTo(target).Within(target * 0.10));
        }

        [TestCase(1_500, 1.5)]
        [TestCase(3_400, 20.0)]
        public void The_medians_hold_when_a_bot_scans_only_every_two_seconds(int distance, double target)
        {
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            double median = Median(TimesToNotice(normal, distance, 4_000, new Random(Seed), fixedInterval: 2_000));
            Assert.That(median, Is.EqualTo(target).Within(target * 0.10));
        }

        [Test]
        public void Easy_never_notices_at_3400_and_nobody_notices_beyond_the_outer_range()
        {
            var random = new Random(Seed);
            Assert.That(TimesToNotice(AutonomousRvrPerception.Profile(RvrBotAwareness.Easy), 3_400, 200, random, horizonSeconds: 120),
                Has.All.GreaterThanOrEqualTo(120.0));
            Assert.That(TimesToNotice(AutonomousRvrPerception.Profile(RvrBotAwareness.Normal), 3_600, 200, random, horizonSeconds: 120),
                Has.All.GreaterThanOrEqualTo(120.0));
        }

        // ---- crowds: the observer cap and the spotter calls together

        private sealed class SimBot
        {
            public long Id;
            public int Group;
            public int X, Y;
            public int Interval;
            public double EngagedAt = double.PositiveInfinity;
        }

        /// <summary>
        /// A solo enemy player, moving, at the origin. Bots in groups (centres at the drawn distance, members within 200
        /// of it), random facing on each scan (one scan in three in the 120° cone), open line of sight. Each bot scans on
        /// its cadence through the real Scan; a notice calls the group (and, in one warband, relays to the other groups)
        /// through CallRecipients. Returns the share of bots engaging by 1, 5, 10 and 30 s, and P(first notice by 5 s).
        /// </summary>
        private static (double FirstBy5, double[] Engaged) Crowd(int bots, int groupSize, bool oneWarband, RvrAwarenessProfile profile,
            Func<Random, double> groupDistance, int trials = 400, int seed = Seed)
        {
            var random = new Random(seed);
            double[] marks = { 1, 5, 10, 30 };
            var engaged = new double[marks.Length];
            int firstBy5 = 0;
            for (int trial = 0; trial < trials; trial++)
            {
                var memory = new AutonomousRvrPerception.Memory();
                var budget = new AutonomousRvrPerception.ObserverBudget();
                var crowd = new List<SimBot>();
                for (int g = 0; g * groupSize < bots; g++)
                {
                    double r = groupDistance(random), angle = random.NextDouble() * Math.PI * 2;
                    for (int m = 0; m < groupSize && crowd.Count < bots; m++)
                    {
                        double ox = (random.NextDouble() * 2 - 1) * 200, oy = (random.NextDouble() * 2 - 1) * 200;
                        long id = 1_000 + crowd.Count;
                        crowd.Add(new SimBot { Id = id, Group = g, X = (int)(r * Math.Cos(angle) + ox), Y = (int)(r * Math.Sin(angle) + oy),
                            Interval = AutonomousFrontierThreatPolicy.IntervalMilliseconds(random.NextInt64(1, 1_000_000)) });
                    }
                }
                var byId = crowd.ToDictionary(b => b.Id);
                var scans = new List<(double At, SimBot Bot)>();
                foreach (SimBot bot in crowd)
                    for (double t = random.NextDouble() * bot.Interval; t <= 30_000; t += bot.Interval)
                        scans.Add((t, bot));
                scans.Sort((a, b) => a.At.CompareTo(b.At));
                double first = double.PositiveInfinity;
                foreach ((double at, SimBot bot) in scans)
                {
                    if (!double.IsPositiveInfinity(bot.EngagedAt))
                        continue;
                    long now = (long)at;
                    double distance = Math.Sqrt((double)bot.X * bot.X + (double)bot.Y * bot.Y);
                    RvrScanOutcome outcome = AutonomousRvrPerception.Scan(memory, budget, bot.Id, 1, 77, now,
                        Plain(distance, random.NextDouble() < 1 / 3.0), profile, bot.Interval, random.NextDouble(),
                        () => AutonomousRvrPerception.ReactionDelayMilliseconds(50, null, random.NextDouble(), profile), true);
                    if (outcome.Result == RvrScanResult.Engage)
                        bot.EngagedAt = at;
                    if (outcome.Result != RvrScanResult.Noticed)
                        continue;
                    first = Math.Min(first, at);
                    RvrSpotterMate Mate(SimBot b) => new(b.Id, b.X, b.Y, 0, true, true);
                    var mates = crowd.Where(b => b.Group == bot.Group).Select(Mate).ToArray();
                    var others = oneWarband
                        ? crowd.Where(b => b.Group != bot.Group).GroupBy(b => b.Group)
                            .Select(g => (IReadOnlyCollection<RvrSpotterMate>)g.Select(Mate).ToArray()).ToArray()
                        : Array.Empty<IReadOnlyCollection<RvrSpotterMate>>();
                    foreach (RvrCallee callee in AutonomousRvrPerception.CallRecipients(bot.Id, bot.X, bot.Y, 0, mates, others,
                                 profile.SpotterRadius, random.NextDouble, out _))
                        memory.Alert(callee.Id, 77, now, callee.ExtraDelayMilliseconds +
                            AutonomousRvrPerception.ReactionDelayMilliseconds(50, null, random.NextDouble(), profile));
                }
                if (first <= 5_000) firstBy5++;
                for (int i = 0; i < marks.Length; i++)
                    engaged[i] += crowd.Count(b => b.EngagedAt <= marks[i] * 1000) / (double)crowd.Count;
            }
            return (firstBy5 / (double)trials, engaged.Select(e => e / trials).ToArray());
        }

        // Uniform by area between 1,000 and 3,500, as the audit's simulator.
        private static double Spread(Random random) => Math.Sqrt(1_000.0 * 1_000 + random.NextDouble() * (3_500.0 * 3_500 - 1_000.0 * 1_000));

        private static string Row(string name, (double FirstBy5, double[] Engaged) r) =>
            $"{name}: first notice by 5 s {r.FirstBy5:P0}; engaging by 1/5/10/30 s {string.Join(" / ", r.Engaged.Select(e => e.ToString("P0")))}";

        [Test]
        public void With_the_observer_cap_a_crowd_drips_in_instead_of_coming_all_at_once()
        {
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            RvrAwarenessProfile uncapped = normal with { ObserverCap = 0 };
            var capped50 = Crowd(50, 8, oneWarband: false, normal, Spread);
            var open50 = Crowd(50, 8, oneWarband: false, uncapped, Spread);
            var capped10 = Crowd(10, 8, oneWarband: false, normal, Spread);
            var warband50 = Crowd(50, 8, oneWarband: true, normal, Spread);
            TestContext.Out.WriteLine(Row("50 bots, groups of 8, cap 3", capped50));
            TestContext.Out.WriteLine(Row("50 bots, groups of 8, no cap", open50));
            TestContext.Out.WriteLine(Row("10 bots, groups of 8, cap 3", capped10));
            TestContext.Out.WriteLine(Row("50 bots, groups of 8 in one warband, cap 3", warband50));
            // The cap changes who acts, not whether anyone sees you: someone still notices quickly.
            Assert.That(capped50.FirstBy5, Is.GreaterThan(0.9));
            // Without it (PR #53 as reviewed by the audit) nearly the whole crowd engages within 5 s.
            Assert.That(open50.Engaged[1], Is.GreaterThan(0.8));
            Assert.That(capped50.Engaged[1], Is.LessThan(0.5), "at 5 s fewer than half the crowd has engaged");
            Assert.That(capped50.Engaged[1], Is.LessThan(open50.Engaged[1] - 0.3));
            Assert.That(capped50.Engaged[3], Is.GreaterThan(capped50.Engaged[2]), "the rest keep coming");
            // A warband adds the relayed groups later, not at once.
            Assert.That(warband50.Engaged[1], Is.LessThan(0.6));
            Assert.That(warband50.Engaged[2], Is.GreaterThanOrEqualTo(capped50.Engaged[2]));
        }

        [Test]
        public void A_camp_of_three_groups_at_3000_units_sees_a_passing_solo_player_but_does_not_all_come_at_once()
        {
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            var apart = Crowd(24, 8, oneWarband: false, normal, _ => 3_000);
            var warband = Crowd(24, 8, oneWarband: true, normal, _ => 3_000);
            TestContext.Out.WriteLine(Row("camp of 3 groups at 3,000, separate groups", apart));
            TestContext.Out.WriteLine(Row("camp of 3 groups at 3,000, one warband", warband));
            Assert.That(apart.Engaged[1], Is.LessThan(0.5), "at 5 s fewer than half the camp");
            Assert.That(warband.Engaged[1], Is.LessThan(0.5), "at 5 s fewer than half the camp");
            Assert.That(apart.Engaged[3], Is.GreaterThan(apart.Engaged[1]), "then they drip in");
        }

        // ---- cost: line-of-sight rays at 900 bots per realm

        /// <summary>
        /// Gamebots in one 24,000-unit square frontier zone, all scanning (none fighting), and one enemy player (Albion) in
        /// the middle. Each scan takes the enemies within 5,000 nearest first and casts at most VisibilityBudget rays
        /// through the real AutonomousFrontierThreatPolicy; a ray "sees" by a fixed hash (half the pairs). Perception runs
        /// on the player only, as in the game, with or without its pre-ray check (PR #53 had none). 10 s of game time.
        /// </summary>
        private static (long Scans, long Rays, long PlayerRays, int MaxPerScan, double PerceptionMicroseconds) RayCount(
            int[] botsPerRealm, bool preRayCheck)
        {
            const int side = 24_000, radius = 5_000, seconds = 10, playerRealm = 1, playerIndex = -1;
            var random = new Random(Seed);
            var positions = new List<(int X, int Y, int Realm)>();
            for (int realm = 0; realm < botsPerRealm.Length; realm++)
                for (int i = 0; i < botsPerRealm[realm]; i++)
                    positions.Add((random.Next(side), random.Next(side), realm + 1));
            (int X, int Y) player = (side / 2, side / 2);
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            var policies = positions.Select(_ => new AutonomousFrontierThreatPolicy()).ToArray();
            var phase = positions.Select((_, i) => (long)(i * 7919 % 1_250)).ToArray();
            long scans = 0, rays = 0, playerRays = 0;
            int maxPerScan = 0;
            var perception = new Stopwatch();
            for (long now = 0; now < seconds * 1000; now += 50)
                for (int i = 0; i < positions.Count; i++)
                {
                    if (now < phase[i] || !policies[i].Due(now, i))
                        continue;
                    scans++;
                    var me = positions[i];
                    long DistanceSquared(int x, int y) => (long)(x - me.X) * (x - me.X) + (long)(y - me.Y) * (y - me.Y);
                    var nearby = new List<(int Index, long D2)>();
                    for (int j = 0; j < positions.Count; j++)
                        if (positions[j].Realm != me.Realm && DistanceSquared(positions[j].X, positions[j].Y) <= (long)radius * radius)
                            nearby.Add((j, DistanceSquared(positions[j].X, positions[j].Y)));
                    double playerDistance = Math.Sqrt(DistanceSquared(player.X, player.Y));
                    if (me.Realm != playerRealm && playerDistance <= radius)
                        nearby.Add((playerIndex, DistanceSquared(player.X, player.Y)));
                    nearby.Sort((a, b) => a.D2.CompareTo(b.D2));
                    int scanRays = 0;
                    var visible = policies[i].Visible(nearby.Select(n => n.Index).ToArray(), target =>
                    {
                        if (target == playerIndex && preRayCheck)
                        {
                            perception.Start();
                            bool skip = AutonomousRvrPerception.SkipsLineOfSight(memory, budget, i, me.Realm, 77, now, playerDistance, normal);
                            perception.Stop();
                            if (skip) return false;
                        }
                        scanRays++;
                        if (target == playerIndex) playerRays++;
                        return ((uint)(i * 31 + target * 17) * 2654435761u) >> 31 == 0;
                    });
                    rays += scanRays;
                    maxPerScan = Math.Max(maxPerScan, scanRays);
                    if (visible.Contains(playerIndex))
                    {
                        perception.Start();
                        AutonomousRvrPerception.Scan(memory, budget, i, me.Realm, 77, now, Plain(playerDistance), normal,
                            policies[i].LastIntervalMilliseconds, random.NextDouble(), () => 1_000, true);
                        perception.Stop();
                    }
                }
            // Per second of game time; the perception time is the real CPU time of its two calls, per scan.
            return (scans / seconds, rays / seconds, playerRays / seconds, maxPerScan,
                perception.Elapsed.TotalMilliseconds * 1000 / Math.Max(1, scans));
        }

        [Test]
        public void At_900_bots_per_realm_the_scan_casts_no_more_rays_than_before_and_never_more_than_eight_per_scan()
        {
            // All three realms mixed in one zone: rays are almost all bot against bot, and unchanged.
            var mixedBefore = RayCount(new[] { 900, 900, 900 }, preRayCheck: false);
            var mixedAfter = RayCount(new[] { 900, 900, 900 }, preRayCheck: true);
            // One enemy realm's 900 bots around the player and no other enemies: every ray is at the player.
            var aloneBefore = RayCount(new[] { 0, 900 }, preRayCheck: false);
            var aloneAfter = RayCount(new[] { 0, 900 }, preRayCheck: true);
            foreach ((string name, var r) in new[] { ("2,700 mixed, PR #53", mixedBefore), ("2,700 mixed, now", mixedAfter),
                         ("900 around the player, PR #53", aloneBefore), ("900 around the player, now", aloneAfter) })
                TestContext.Out.WriteLine($"{name}: {r.Scans} scans/s, {r.Rays} rays/s ({r.PlayerRays} at the player), " +
                                          $"at most {r.MaxPerScan} per scan; perception {r.PerceptionMicroseconds:0.000} µs per scan");
            foreach (var r in new[] { mixedBefore, mixedAfter, aloneBefore, aloneAfter })
            {
                Assert.That(r.MaxPerScan, Is.LessThanOrEqualTo(AutonomousFrontierThreatPolicy.VisibilityBudget));
                Assert.That(r.Rays, Is.LessThanOrEqualTo(r.Scans * AutonomousFrontierThreatPolicy.VisibilityBudget + 1), "per second, rounded");
            }
            Assert.That(mixedAfter.Scans, Is.EqualTo(mixedBefore.Scans));
            Assert.That(mixedAfter.Rays, Is.LessThanOrEqualTo(mixedBefore.Rays));
            Assert.That(aloneAfter.Rays, Is.LessThan(aloneBefore.Rays / 2), "no rays beyond the outer range or over the cap");
        }
    }
}
