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

        // This checks the fitted formula (NoticeChance, whose chance per scan is solved from these same targets) against
        // its own targets, for a still target at a fixed distance in open sight: it is not evidence that the perception
        // table holds in play (RvR audit 3, evidence.md F1, #519). The played measure is played_check_log.py's notice
        // row: RVR_NOTICE time_since_first_los_ms by distance band.
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
        public void The_fitted_formula_gives_its_own_target_median_for_each_level_and_band(RvrBotAwareness level, int distance, double target)
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
            Func<Random, double> groupDistance, int trials = 400, int seed = Seed, bool lookers = false)
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
                // 1.2 work package 1 (rvr_bot_group_lookers): two lookers per group; the others scan once woken by a
                // mate's call or a group mate fighting (RvrScanRoster), as the frontier scan decides.
                var groups = crowd.GroupBy(b => b.Group).ToDictionary(g => g.Key, g => g.ToArray());
                var groupIds = groups.ToDictionary(g => g.Key, g => g.Value.Select(b => b.Id).ToArray());
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
                    if (lookers)
                    {
                        long[] ids = groupIds[bot.Group];
                        bool isLooker = ids.Length > RvrScanRoster.LookersPerGroup && RvrScanRoster.IsLooker(bot.Id, ids, now);
                        bool fighting = groups[bot.Group].Any(b => b != bot && b.EngagedAt <= at);
                        RvrScanRole role = RvrScanRoster.Decide(true, ids.Length, isLooker, !isLooker && memory.HasNews(bot.Id, now),
                            fighting, false);
                        if (!RvrScanRoster.Scans(role))
                            continue;
                    }
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

        // ---- 1.2 work package 1: lookers and the per-group cap also apply to the player (owner decision 7)

        /// <summary>
        /// The time from first sight until the first of a group's bots notices a solo player, through the real rules (Scan,
        /// its memory and observer budget, the looker roster), with no calls: every bot looks (as before) or only the
        /// group's two lookers (rvr_bot_group_lookers). A group of one is a soloer.
        /// </summary>
        private static double[] GroupTimesToNotice(RvrAwarenessProfile profile, double distance, int groupSize, bool lookers,
            int samples, Random random, double horizonSeconds = 300)
        {
            var times = new double[samples];
            for (int s = 0; s < samples; s++)
            {
                var memory = new AutonomousRvrPerception.Memory();
                var budget = new AutonomousRvrPerception.ObserverBudget();
                long[] ids = Enumerable.Range(0, groupSize).Select(i => 5_000L + s * 16 + i).ToArray();
                // Real time: the group has been in the field a while, so the looker turn is anywhere in its rotation.
                long start = 1_000_000 + random.Next(RvrScanRoster.RotationMilliseconds * 7);
                var scans = new List<(long At, long Bot, int Interval)>();
                foreach (long id in ids)
                {
                    int interval = AutonomousFrontierThreatPolicy.IntervalMilliseconds(random.NextInt64(1, 1_000_000));
                    for (double t = random.NextDouble() * interval; t <= horizonSeconds * 1000; t += interval)
                        scans.Add((start + (long)t, id, interval));
                }
                scans.Sort((a, b) => a.At.CompareTo(b.At));
                times[s] = horizonSeconds;
                foreach ((long at, long id, int interval) in scans)
                {
                    if (lookers && RvrScanRoster.Decide(true, ids.Length, ids.Length > RvrScanRoster.LookersPerGroup &&
                            RvrScanRoster.IsLooker(id, ids, at), false, false, false) == RvrScanRole.Idle)
                        continue;
                    RvrScanOutcome outcome = AutonomousRvrPerception.Scan(memory, budget, id, 1, 77, at, Plain(distance), profile,
                        interval, random.NextDouble(), () => 1_000, true);
                    if (outcome.Result == RvrScanResult.Noticed)
                    {
                        times[s] = (at - start) / 1000.0;
                        break;
                    }
                }
            }
            return times;
        }

        [TestCase(1_500, 1.5)]
        [TestCase(2_500, 6.0)]
        [TestCase(3_400, 20.0)]
        public void With_lookers_the_player_table_still_holds_for_one_bot_and_a_group_is_not_a_radar(int distance, double target)
        {
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            var random = new Random(Seed + distance);
            double solo = Median(GroupTimesToNotice(normal, distance, 1, lookers: true, 4_000, random));
            double groupBefore = Median(GroupTimesToNotice(normal, distance, 8, lookers: false, 4_000, random));
            double groupAfter = Median(GroupTimesToNotice(normal, distance, 8, lookers: true, 4_000, random));
            TestContext.Out.WriteLine($"normal at {distance}: one bot {solo:0.00} s (table {target} s); a group of 8 first notices " +
                                      $"after {groupBefore:0.00} s with every bot looking (cap 3), {groupAfter:0.00} s with two lookers");
            // The table is one bot looking on its own: a soloer, or each of the two lookers.
            Assert.That(solo, Is.EqualTo(target).Within(target * 0.10));
            // Two lookers with a memoryless chance: about half the table; never faster than everyone looking under the cap.
            Assert.That(groupAfter, Is.EqualTo(target / 2).Within(target * 0.10));
            Assert.That(groupAfter, Is.GreaterThanOrEqualTo(groupBefore * 0.95));
        }

        [Test]
        public void With_lookers_a_crowd_still_sees_a_solo_player_soon_and_drips_in_no_faster_than_before()
        {
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            var cases = new (string Name, int Bots, bool Warband, Func<Random, double> Distance)[]
            {
                ("50 bots, groups of 8, spread 1,000-3,500", 50, false, Spread),
                ("50 bots, groups of 8 in one warband", 50, true, Spread),
                ("camp of 3 groups at 3,000, separate", 24, false, _ => 3_000),
                ("camp of 3 groups at 3,000, one warband", 24, true, _ => 3_000),
            };
            foreach ((string name, int bots, bool warband, Func<Random, double> distance) in cases)
            {
                var before = Crowd(bots, 8, warband, normal, distance);
                var after = Crowd(bots, 8, warband, normal, distance, lookers: true);
                TestContext.Out.WriteLine(Row(name + ", every bot looks (cap 3 per enemy)", before));
                TestContext.Out.WriteLine(Row(name + ", two lookers per group (cap 3 per enemy group)", after));
                Assert.That(after.Engaged[1], Is.LessThan(0.5), name + ": at 5 s fewer than half have engaged");
                Assert.That(after.Engaged[1], Is.LessThanOrEqualTo(before.Engaged[1] + 0.05), name + ": no faster than before");
                Assert.That(after.Engaged[3], Is.GreaterThan(after.Engaged[1]), name + ": the rest keep coming");
            }
            var spread = Crowd(50, 8, false, normal, Spread, lookers: true);
            Assert.That(spread.FirstBy5, Is.GreaterThan(0.75), "someone still notices you within 5 s");
        }

        // ---- 1.2 work package 1: the scan budget in the architecture's scenario (ARCHITECTURE.md section 4)

        private sealed class BudgetBot
        {
            public int Index, Realm, Group, X, Y;
            public long Id, Key, Party;
            public AutonomousFrontierThreatPolicy Policy = new();
            public long Phase;
        }

        /// <summary>
        /// 300 gamebots per realm, three realms, out of combat in one square of <paramref name="side"/> units: 80% in groups
        /// of five (members within 150 of the group's centre), the rest solo. 10 s of game time from a cold start. Each
        /// scan takes the enemies within its radius nearest first and casts at most VisibilityBudget rays through the real
        /// AutonomousFrontierThreatPolicy (a ray "sees" by a fixed hash, half the pairs). Before: every bot scans 5,000 and
        /// casts a ray at every enemy bot in its window (bots were acquired at once). After (work package 1): lookers
        /// (RvrScanRoster; a called non-looker wakes and scans), the radius of ScanRadius, the pre-ray skip, one roll per
        /// enemy party (ScanParty) and calls to the group. Nobody engages, so woken bots keep scanning: an upper bound.
        /// </summary>
        private static (long Scans, long Rays, long Rolls, long Capped, long Idle, int MaxPerScan) PerceptionBudget(int side, bool wp1)
        {
            const int perRealm = 300, groupSize = 5, seconds = 10, legacyRadius = 5_000;
            var random = new Random(Seed);
            RvrAwarenessProfile normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
            var bots = new List<BudgetBot>();
            int groupId = 0;
            for (int realm = 1; realm <= 3; realm++)
            {
                for (int g = 0; g < perRealm * 4 / 5 / groupSize; g++)
                {
                    int cx = random.Next(side), cy = random.Next(side);
                    groupId++;
                    for (int m = 0; m < groupSize; m++)
                        bots.Add(new BudgetBot { Realm = realm, Group = groupId, X = cx + random.Next(-150, 151), Y = cy + random.Next(-150, 151) });
                }
                while (bots.Count(b => b.Realm == realm) < perRealm)
                    bots.Add(new BudgetBot { Realm = realm, Group = -(++groupId), X = random.Next(side), Y = random.Next(side) });
            }
            for (int i = 0; i < bots.Count; i++)
            {
                BudgetBot b = bots[i];
                b.Index = i;
                b.Id = 100_000 + i;
                b.Key = 200_000 + i;
                b.Party = RvrPartyKey.Compose(b.Key, b.Group > 0 ? b.Group : null);
                b.Phase = i * 7919 % 1_250;
            }
            var members = bots.Where(b => b.Group > 0).GroupBy(b => b.Group).ToDictionary(g => g.Key, g => g.ToArray());
            var memberIds = members.ToDictionary(g => g.Key, g => g.Value.Select(b => b.Id).ToArray());
            var byId = bots.ToDictionary(b => b.Id);
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            int radius = wp1 ? AutonomousRvrPerception.ScanRadius(normal, true, legacyRadius) : legacyRadius;
            long scans = 0, rays = 0, rolls = 0, capped = 0, idle = 0;
            int maxPerScan = 0;
            var results = new List<RvrMemberOutcome>();
            for (long now = 0; now < seconds * 1000; now += 50)
                foreach (BudgetBot me in bots)
                {
                    if (now < me.Phase || !me.Policy.Due(now, me.Index))
                        continue;
                    if (wp1)
                    {
                        long[] ids = me.Group > 0 ? memberIds[me.Group] : new[] { me.Id };
                        bool isLooker = ids.Length > RvrScanRoster.LookersPerGroup && RvrScanRoster.IsLooker(me.Id, ids, now);
                        RvrScanRole role = RvrScanRoster.Decide(true, ids.Length, isLooker, !isLooker && memory.HasNews(me.Id, now), false, false);
                        if (!RvrScanRoster.Scans(role))
                        {
                            idle++;
                            continue;
                        }
                    }
                    scans++;
                    long DistanceSquared(BudgetBot other) => (long)(other.X - me.X) * (other.X - me.X) + (long)(other.Y - me.Y) * (other.Y - me.Y);
                    BudgetBot[] nearby = bots.Where(other => other.Realm != me.Realm && DistanceSquared(other) <= (long)radius * radius)
                        .OrderBy(DistanceSquared).ToArray();
                    int scanRays = 0;
                    IReadOnlyList<BudgetBot> visible = me.Policy.Visible(nearby, target =>
                    {
                        if (wp1 && AutonomousRvrPerception.SkipsLineOfSight(memory, budget, me.Id, me.Realm, target.Key, target.Party, now,
                                Math.Sqrt(DistanceSquared(target)), normal))
                            return false;
                        scanRays++;
                        return ((uint)(me.Index * 31 + target.Index * 17) * 2654435761u) >> 31 == 0;
                    });
                    rays += scanRays;
                    maxPerScan = Math.Max(maxPerScan, scanRays);
                    if (!wp1)
                        continue;
                    foreach (var party in visible.GroupBy(target => target.Party))
                    {
                        RvrPartyMember[] seen = party.Select(t => new RvrPartyMember(t.Key, Plain(Math.Sqrt(DistanceSquared(t))), t.X, t.Y, 0)).ToArray();
                        results.Clear();
                        AutonomousRvrPerception.ScanParty(memory, budget, me.Id, me.Realm, party.Key, seen, now, normal,
                            me.Policy.LastIntervalMilliseconds, random.NextDouble(), () => 1_000, true, results);
                        if (results.Any(r => r.Result == RvrScanResult.Capped))
                            capped++;
                        if (results.Any(r => r.Result == RvrScanResult.Unseen && r.Chance > 0 ||
                                             r.Result == RvrScanResult.Noticed && (r.Factors & RvrNoticeFactors.Party) == 0))
                            rolls++;
                        long[] noticed = results.Where(r => r.Result == RvrScanResult.Noticed).Select(r => r.Key).ToArray();
                        if (noticed.Length == 0 || me.Group <= 0)
                            continue;
                        foreach (RvrCallee callee in AutonomousRvrPerception.CallRecipients(me.Id, me.X, me.Y, 0,
                                     members[me.Group].Select(b => new RvrSpotterMate(b.Id, b.X, b.Y, 0, true, true)),
                                     Array.Empty<IReadOnlyCollection<RvrSpotterMate>>(), normal.SpotterRadius, random.NextDouble, out _))
                            foreach (long key in noticed)
                                memory.Alert(callee.Id, key, now, callee.ExtraDelayMilliseconds + 1_000);
                    }
                }
            return (scans / seconds, rays / seconds, rolls / seconds, capped / seconds, idle / seconds, maxPerScan);
        }

        [Test]
        public void In_the_architecture_scenario_the_scan_casts_no_more_rays_and_runs_no_more_scans_than_before()
        {
            foreach (int side in new[] { 24_000, 48_000 })
            {
                var before = PerceptionBudget(side, wp1: false);
                var after = PerceptionBudget(side, wp1: true);
                foreach ((string name, var r) in new[] { ("before", before), ("after (work package 1)", after) })
                    TestContext.Out.WriteLine($"900 bots in {side:N0}², {name}: {r.Scans} scans/s, {r.Rays} rays/s, {r.Rolls} rolls/s, " +
                                              $"{r.Capped} capped/s, {r.Idle} idle non-lookers/s, at most {r.MaxPerScan} rays per scan");
                Assert.That(after.Scans, Is.LessThanOrEqualTo(before.Scans), $"{side}: scans");
                Assert.That(after.Rays, Is.LessThanOrEqualTo(before.Rays), $"{side}: rays");
                Assert.That(after.MaxPerScan, Is.LessThanOrEqualTo(AutonomousFrontierThreatPolicy.VisibilityBudget));
                Assert.That(after.Rays, Is.LessThanOrEqualTo(after.Scans * AutonomousFrontierThreatPolicy.VisibilityBudget + 1));
            }
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
