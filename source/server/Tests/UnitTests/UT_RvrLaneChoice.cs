using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 5 (docs/RVR-FIELD-1.2.md, "Feet"): which lanes a march takes and how its groups keep
    /// level. On a made-up frontier (portal keep, milegate, target keep 50 to the north, three lanes from the gate): trunk
    /// then fan, the 50/25/25 split over many forces, a choice that never flips, live-ownership keep and hazard rejection,
    /// today's route as the fallback, the leader-relative pace, and a column that re-merges at the siege camp ring with its
    /// arrival spread inside the stated margin. Pure. Tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture]
    public class UT_RvrLaneChoice
    {
        private const ushort Region = 1;
        private static readonly Vector2 Goal = new(0, 80_000);

        // Lane 1 bends 15,000 east (+16%), lane 2 bends 25,000 west (+32%): inside the catalog's 35% limit.
        private static string Json(string extraLanes = "") => """
            {"version":1,"tool":"test","objectives":[
              {"ref":"keep:22","kind":"portal keep","region":1,"keep_id":22,"name":"Hibernia Portal Keep","at":[0,0,0]},
              {"ref":"milegate:m","kind":"milegate","region":1,"name":"m","at":[0,20000,0],"portal_keep":22},
              {"ref":"keep:50","kind":"keep","region":1,"keep_id":50,"name":"Target","at":[0,80000,0]},
              {"ref":"keep:51","kind":"keep","region":1,"keep_id":51,"name":"Other","at":[40000,100000,0]},
              {"ref":"entry:e","kind":"entry","region":1,"name":"Border","at":[-40000,90000,0]}],
            "lanes":[
              {"region":1,"kind":"trunk","from":{"ref":"keep:22"},"to":{"ref":"milegate:m"},"start":[0,0,0],"goal":[0,20000,0],"lane":0,"via":[],"length":{"forward":20000,"back":20000}},
              {"region":1,"kind":"gate","from":{"ref":"milegate:m"},"to":{"ref":"keep:50"},"start":[0,20000,0],"goal":[0,78000,0],"lane":0,"via":[],"length":{"forward":58000,"back":58000}},
              {"region":1,"kind":"gate","from":{"ref":"milegate:m"},"to":{"ref":"keep:50"},"start":[0,20000,0],"goal":[0,78000,0],"lane":1,"via":[[15000,49000,0]],"length":{"forward":67082,"back":67082}},
              {"region":1,"kind":"gate","from":{"ref":"milegate:m"},"to":{"ref":"keep:50"},"start":[0,20000,0],"goal":[0,78000,0],"lane":2,"via":[[-25000,49000,0]],"length":{"forward":76637,"back":76637}},
              {"region":1,"kind":"hop","from":{"ref":"keep:50"},"to":{"ref":"keep:51"},"start":[0,78000,0],"goal":[40000,100000,0],"lane":0,"via":[],"length":{"forward":45650,"back":45650}}
              EXTRA]}
            """.Replace("EXTRA", extraLanes);

        private static RvrLaneData Data(string extra = "") => RvrLaneCatalog.Parse(Json(extra));

        /// <summary>Every lane walks as straight legs, both ways.</summary>
        private static Func<RvrLane, bool, RvrLaneProof> Proofs(Func<RvrLane, bool, bool> fails = null) =>
            (lane, reverse) => fails?.Invoke(lane, reverse) == true
                ? RvrLaneProof.Rejected("blocked")
                : RvrLaneCatalog.Prove(lane, reverse, UT_RvrLaneCatalog.Straight);

        private static RvrLaneOptions March(RvrLaneData data = null, Func<RvrLane, bool, RvrLaneProof> proofs = null,
            IReadOnlyList<RvrLiveKeep> keeps = null, IReadOnlyList<AutonomousArmyDetour.Hazard> hazards = null,
            Vector3? origin = null, eRealm realm = eRealm.Hibernia) =>
            RvrLaneChoice.Options(data ?? Data(), proofs ?? Proofs(), Region, realm, origin ?? new Vector3(300, 400, 0), "keep:50",
                Goal, 50, keeps ?? [], hazards ?? []);

        [Test]
        public void Trunk_then_fan_from_a_portal_keep()
        {
            RvrLaneOptions options = March();
            Assert.That(options.UsesLanes, Is.True, options.Detail);
            Assert.That(options.Routes.Select(route => route.LaneIndex), Is.EqualTo(new[] { 0, 1, 2 }));
            foreach (RvrLaneRoute route in options.Routes)
                Assert.Multiple(() =>
                {
                    // Everyone walks the trunk to the milegate first, then their own lane.
                    Assert.That(route.Walk[0], Is.EqualTo(new Vector3(0, 0, 0)));
                    Assert.That(route.Walk[1], Is.EqualTo(new Vector3(0, 20_000, 0)), "the milegate");
                    Assert.That(route.Trunk?.Kind, Is.EqualTo(RvrLaneKind.Trunk));
                    Assert.That(route.TrunkLength, Is.EqualTo(20_000).Within(1));
                    Assert.That(route.Walk[^1], Is.EqualTo(new Vector3(0, 78_000, 0)), "every lane ends at the same goal");
                    Assert.That(route.FromRef, Is.EqualTo("keep:22"));
                    Assert.That(route.ToRef, Is.EqualTo("keep:50"));
                    Assert.That(route.WalkProgress, Is.Ordered);
                    Assert.That(route.Slot, Is.EqualTo(options.Routes.IndexOf(route)));
                });
            Assert.That(options.Routes[1].Walk.Skip(2).First().X, Is.EqualTo(15_000), "lane 1 fans east after the gate");
            Assert.That(options.Routes[2].Walk.Skip(2).First().X, Is.EqualTo(-25_000), "lane 2 fans west after the gate");
            // From the milegate itself the same three lanes, with no trunk.
            RvrLaneOptions fromGate = March(origin: new Vector3(0, 20_500, 0));
            Assert.That(fromGate.Routes.Length, Is.EqualTo(3));
            Assert.That(fromGate.Routes.All(route => route.Trunk == null && route.TrunkLength == 0), Is.True);
        }

        [Test]
        public void Lanes_end_where_the_column_re_merges_at_the_siege_camp()
        {
            foreach (RvrLaneRoute route in March().Routes)
            {
                Vector3 merge = RvrLaneGeometry.PointAt(route.Shape, route.Along, route.MergeLength);
                Assert.Multiple(() =>
                {
                    Assert.That(Vector2.Distance(new(merge.X, merge.Y), Goal), Is.EqualTo(RvrLaneChoice.MergeRadius).Within(2),
                        "the march ends on the camp ring, as TryMarchWithArmy's own CampRadius + 2,500");
                    Assert.That(route.MergeLength, Is.LessThan(route.Length));
                    Assert.That(route.MergeLength, Is.GreaterThan(route.TrunkLength));
                });
            }
            Assert.That(RvrLaneChoice.MergeRadius, Is.EqualTo(AutonomousRvrRally.CampRadius + 2_500));
        }

        [Test]
        public void A_lane_near_a_keep_an_enemy_holds_now_is_left_out()
        {
            // Keep 61 stands 3,000 east of lane 1's bend.
            RvrLiveKeep Keep61(eRealm realm) => new(61, realm, new(18_000, 49_000));
            RvrLaneOptions enemy = March(keeps: [Keep61(eRealm.Midgard)]);
            RvrLaneOptions friendly = March(keeps: [Keep61(eRealm.Hibernia)]);
            RvrLaneOptions neutral = March(keeps: [Keep61(eRealm.None)]);
            RvrLaneOptions goalHeld = March(keeps: [new(50, eRealm.Midgard, Goal)]);
            Assert.Multiple(() =>
            {
                Assert.That(enemy.Routes.Select(route => route.LaneIndex), Is.EqualTo(new[] { 0, 2 }));
                Assert.That(enemy.Detail, Does.Contain("lane 1: passes 3000 from keep 61, held by Midgard"));
                Assert.That(enemy.Routes.Select(route => route.Slot), Is.EqualTo(new[] { 0, 1 }), "slots are renumbered");
                Assert.That(friendly.Routes.Length, Is.EqualTo(3), "a keep the marching realm holds is no danger");
                Assert.That(neutral.Routes.Length, Is.EqualTo(3));
                Assert.That(goalHeld.Routes.Length, Is.EqualTo(3), "the goal itself is held by the enemy, always");
            });
            // The same keep held by the enemy of whoever marches: Midgard marching is not stopped by its own keep.
            Assert.That(March(keeps: [Keep61(eRealm.Midgard)], realm: eRealm.Midgard).Routes.Length, Is.EqualTo(3));
        }

        [Test]
        public void A_lane_the_army_detour_hazards_reject_is_left_out()
        {
            var westKeep = new AutonomousArmyDetour.Hazard(new(-30_000, 49_000), AutonomousArmyDetour.KeepClearance);
            var nearGoal = new AutonomousArmyDetour.Hazard(new(0, 76_000), AutonomousArmyDetour.RallyClearance);
            var nearStart = new AutonomousArmyDetour.Hazard(new(0, 1_000), AutonomousArmyDetour.KeepClearance);
            RvrLaneOptions options = March(hazards: [westKeep, nearGoal, nearStart]);
            Assert.That(options.Routes.Select(route => route.LaneIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(options.Detail, Does.Contain("lane 2: crosses a march hazard at -30000,49000"));
            Assert.That(options.Fallback, Is.EqualTo(RvrLaneFallback.None));
        }

        [Test]
        public void With_no_proven_lane_the_march_takes_todays_route()
        {
            var everyLane = new AutonomousArmyDetour.Hazard(new(0, 40_000), 18_000);
            Assert.Multiple(() =>
            {
                Assert.That(March(proofs: (_, _) => RvrLaneProof.Unchecked).Fallback, Is.EqualTo(RvrLaneFallback.Unproved),
                    "validation not finished yet: today's route");
                Assert.That(March(proofs: Proofs((lane, _) => lane.Kind == RvrLaneKind.Trunk)).Detail, Does.StartWith("trunk:"));
                Assert.That(March(proofs: Proofs((lane, _) => lane.Kind == RvrLaneKind.Gate)).UsesLanes, Is.False);
                Assert.That(March(hazards: [everyLane]).Fallback, Is.EqualTo(RvrLaneFallback.Hazard));
                Assert.That(March(origin: new Vector3(90_000, 0, 0)).Fallback, Is.EqualTo(RvrLaneFallback.NoOrigin));
                Assert.That(RvrLaneChoice.Options(Data(), Proofs(), Region, eRealm.Hibernia, new(300, 400, 0), "keep:51",
                    new(40_000, 100_000), 51, [], []).Fallback, Is.EqualTo(RvrLaneFallback.NoTrip), "the catalog has no entry");
                Assert.That(RvrLaneChoice.Options(Data(), Proofs(), Region, eRealm.Hibernia, new(300, 400, 0), "keep:50",
                    Goal, 50, [], [], _ => false).Fallback, Is.EqualTo(RvrLaneFallback.NotAllowed));
            });
            // One proven alternate: two lanes.
            RvrLaneOptions two = March(proofs: Proofs((lane, _) => lane.Index == 2));
            Assert.That(two.Routes.Select(route => route.LaneIndex), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void A_hop_is_walked_back_only_on_its_own_back_proof()
        {
            RvrLaneOptions back = RvrLaneChoice.Between(Data(), Proofs(), Region, eRealm.Hibernia, "keep:51", "keep:50", Goal, 50, [], []);
            RvrLaneOptions unproved = RvrLaneChoice.Between(Data(), Proofs((_, reverse) => reverse), Region, eRealm.Hibernia,
                "keep:51", "keep:50", Goal, 50, [], []);
            Assert.That(back.Routes.Single().Reversed, Is.True);
            Assert.That(back.Routes.Single().Walk[0], Is.EqualTo(new Vector3(40_000, 100_000, 0)));
            Assert.That(unproved.Fallback, Is.EqualTo(RvrLaneFallback.Unproved));
        }

        [Test]
        public void Groups_split_fifty_twenty_five_twenty_five_over_many_forces()
        {
            var random = new Random(1116);
            int[] heads = new int[3], twoHeads = new int[2];
            for (int march = 0; march < 2_000; march++)
            {
                var plan = new RvrMarchLanePlan($"rvr-keep-{march}", Region, eRealm.Hibernia, March(), 1);
                var plan2 = new RvrMarchLanePlan($"rvr-keep-{march}", Region, eRealm.Hibernia, March(proofs: Proofs((lane, _) => lane.Index == 2)), 1);
                int forces = random.Next(4, 30);
                for (int force = 0; force < forces; force++)
                {
                    int size = random.Next(1, 9);
                    // Veterans: the tier's tag-along failures are measured on their own below.
                    var lane = plan.Assign($"force-{march}-{force}", size, force == 0, RvrSkillTier.Veteran, out _);
                    heads[lane.Intended] += size;
                    twoHeads[plan2.Assign($"force-{march}-{force}", size, force == 0, RvrSkillTier.Veteran, out _).Intended] += size;
                    if (force == 0) Assert.That(lane.Slot, Is.EqualTo(0), "the march leader's group walks lane 0");
                }
                if (forces >= 8) Assert.That(plan.DistinctLanes, Is.EqualTo(3).Or.EqualTo(2), "today: 1");
            }
            double total = heads.Sum(), total2 = twoHeads.Sum();
            Assert.Multiple(() =>
            {
                Assert.That(heads[0] / total * 100, Is.EqualTo(50).Within(2));
                Assert.That(heads[1] / total * 100, Is.EqualTo(25).Within(2));
                Assert.That(heads[2] / total * 100, Is.EqualTo(25).Within(2));
                Assert.That(twoHeads[0] / total2 * 100, Is.EqualTo(66.7).Within(2), "two lanes: 67/33");
                Assert.That(RvrLaneChoice.Weights(1), Is.EqualTo(new[] { 1 }));
            });
        }

        [Test]
        public void A_groups_lane_is_fixed_for_the_whole_march()
        {
            int builds = 0;
            RvrLaneOptions Build() { builds++; return March(); }
            RvrMarchLanePlan plan = RvrMarchLanes.Get("rvr-keep-test-stable", Region, eRealm.Hibernia, 1_000, Build);
            Assert.That(RvrMarchLanes.Get("rvr-keep-test-stable", Region, eRealm.Hibernia, 2_000, Build), Is.SameAs(plan));
            Assert.That(builds, Is.EqualTo(1), "the lanes are chosen once per march, not per marcher");
            var first = plan.Assign("a", 8, true, RvrSkillTier.Average, out bool assigned);
            var second = plan.Assign("b", 8, false, RvrSkillTier.Average, out _);
            for (int i = 0; i < 20; i++) plan.Assign($"other-{i}", 8, false, RvrSkillTier.Average, out _);
            Assert.Multiple(() =>
            {
                Assert.That(assigned, Is.True);
                Assert.That(plan.Assign("a", 8, false, RvrSkillTier.Casual, out bool again), Is.EqualTo(first));
                Assert.That(again, Is.False);
                Assert.That(plan.Assign("b", 1, true, RvrSkillTier.Casual, out _), Is.EqualTo(second), "not even when it later leads");
                Assert.That(plan.TryGetForce("b", out var known) && known == second, Is.True);
            });
            // Another realm's march on the same keep is its own plan; a march idle for five minutes is forgotten.
            Assert.That(RvrMarchLanes.Get("rvr-keep-test-stable", Region, eRealm.Midgard, 3_000, Build), Is.Not.SameAs(plan));
            Assert.That(RvrMarchLanes.Get("rvr-keep-test-stable", Region, eRealm.Hibernia, 3_000 + RvrMarchLanes.IdleMilliseconds + 1, Build),
                Is.Not.SameAs(plan));
            RvrMarchLanes.Forget("rvr-keep-test-stable");
        }

        [Test]
        public void Some_groups_ignore_their_flank_lane_by_skill_tier()
        {
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                int tagging = Enumerable.Range(0, 20_000).Count(i => RvrLaneChoice.TagsAlong(tier, $"force-{i}"));
                Assert.That(tagging / 200.0, Is.EqualTo(RvrLaneChoice.TagAlongPercent(tier)).Within(1), tier.ToString());
            }
            var plan = new RvrMarchLanePlan("t", Region, eRealm.Hibernia, March(), 1);
            string tagger = Enumerable.Range(0, 1_000).Select(i => $"g{i}").First(key => RvrLaneChoice.TagsAlong(RvrSkillTier.Casual, key));
            plan.Assign("leader", 8, true, RvrSkillTier.Casual, out _);
            var lane = plan.Assign(tagger, 8, false, RvrSkillTier.Casual, out _);
            Assert.That((lane.Intended, lane.Slot, lane.TagAlong), Is.EqualTo((1, 0, true)), "sent east, stays with the main body");
        }

        [Test]
        public void Pace_holds_ahead_and_hurries_behind_the_leader()
        {
            ImmutableArray<RvrLaneRoute> routes = March().Routes;
            RvrLaneRoute main = routes[0], east = routes[1];
            RvrLaneMargins average = RvrLanePacing.Margins(RvrSkillTier.Average);
            float leaderAtHalfFan = main.TrunkLength + (main.MergeLength - main.TrunkLength) / 2;
            float eastAtHalfFan = east.TrunkLength + (east.MergeLength - east.TrunkLength) / 2;
            Assert.Multiple(() =>
            {
                // On the shared trunk the gap is the plain distance.
                Assert.That(RvrLanePacing.Gap(12_000, east, 10_000, main), Is.EqualTo(2_000).Within(1));
                // Past the gate, the share of each lane walked: level at half of each fan.
                Assert.That(RvrLanePacing.Gap(eastAtHalfFan, east, leaderAtHalfFan, main), Is.EqualTo(0).Within(1));
                Assert.That(RvrLanePacing.Gap(eastAtHalfFan + 2_000, east, leaderAtHalfFan, main), Is.EqualTo(2_000).Within(1));
                Assert.That(RvrLanePacing.Pace(2_000, average), Is.EqualTo(RvrLanePace.Hold), "ahead: wait");
                Assert.That(RvrLanePacing.Pace(-2_000, average), Is.EqualTo(RvrLanePace.Hurry), "behind: walk on");
                Assert.That(RvrLanePacing.Pace(500, average), Is.EqualTo(RvrLanePace.Level));
                Assert.That(RvrLanePacing.Pace(2_000, RvrLanePacing.Margins(RvrSkillTier.Casual)), Is.EqualTo(RvrLanePace.Level),
                    "a casual group keeps a looser pace");
                Assert.That(RvrLanePacing.Waits(RvrLanePace.Level, -800, leaderMoving: false), Is.False,
                    "a little behind a waiting leader: close up");
                Assert.That(RvrLanePacing.Waits(RvrLanePace.Level, 300, leaderMoving: false), Is.True);
                Assert.That(RvrLanePacing.Waits(RvrLanePace.Level, 300, leaderMoving: true), Is.False);
                Assert.That(RvrLanePacing.Waits(RvrLanePace.Hold, 3_000, leaderMoving: true), Is.True);
                Assert.That(RvrLanePacing.Waits(RvrLanePace.Hurry, -3_000, leaderMoving: false), Is.False);
                Assert.That(RvrLanePacing.Level(2_400), Is.True);
                Assert.That(RvrLanePacing.Level(-2_600), Is.False);
            });
            // The tier's reaction delay: a change of pace waits 2 s for an average group.
            RvrLanePace pace = RvrLanePacing.Next(RvrLanePace.Level, 0, 2_000, average, 10_000, out long pending);
            Assert.That((pace, pending), Is.EqualTo((RvrLanePace.Level, 10_000L)));
            pace = RvrLanePacing.Next(pace, pending, 2_000, average, 11_000, out pending);
            Assert.That(pace, Is.EqualTo(RvrLanePace.Level));
            pace = RvrLanePacing.Next(pace, pending, 2_000, average, 12_000, out pending);
            Assert.That((pace, pending), Is.EqualTo((RvrLanePace.Hold, 0L)));
            Assert.That(RvrLanePacing.Next(pace, 0, 0, average, 12_500, out _), Is.EqualTo(RvrLanePace.Hold), "and back, after its delay");
        }

        [Test]
        public void The_leader_waits_for_a_flank_lane_that_fell_behind()
        {
            long until = 0, next = 0;
            Assert.That(RvrLanePacing.LeaderWaits(-2_000, 1_000, ref until, ref next), Is.False, "within the margin");
            Assert.That(RvrLanePacing.LeaderWaits(-4_000, 1_000, ref until, ref next), Is.True);
            Assert.That(RvrLanePacing.LeaderWaits(-3_500, 30_000, ref until, ref next), Is.True, "still behind");
            Assert.That(RvrLanePacing.LeaderWaits(-4_000, 47_000, ref until, ref next), Is.False, "45 seconds at most");
            Assert.That(RvrLanePacing.LeaderWaits(-4_000, 80_000, ref until, ref next), Is.False, "then 45 seconds of marching");
            Assert.That(RvrLanePacing.LeaderWaits(-4_000, 92_000, ref until, ref next), Is.True);
            Assert.That(RvrLanePacing.LeaderWaits(-500, 95_000, ref until, ref next), Is.False, "caught up: the leader walks on");
        }

        [Test]
        public void A_bot_joins_its_lane_at_the_next_point_ahead_or_takes_todays_route()
        {
            RvrLaneRoute east = March().Routes[1];
            Assert.Multiple(() =>
            {
                Assert.That(RvrLaneChoice.JoinIndex(east, new(100, 100, 0), -1, out _), Is.EqualTo(1), "at the start: on to the milegate");
                Assert.That(east.Walk[RvrLaneChoice.JoinIndex(east, new(7_000, 33_000, 0), -1, out _)], Is.EqualTo(new Vector3(15_000, 49_000, 0)));
                Assert.That(RvrLaneChoice.JoinIndex(east, new(40_000, 33_000, 0), -1, out float lateral), Is.EqualTo(-1));
                Assert.That(lateral, Is.GreaterThan(RvrLaneChoice.JoinDistance));
            });
        }

        /// <summary>
        /// The march simulated at running speed from the portal keep to the camp ring with the pace rules above: the leader
        /// on lane 0 (78,000 to the ring after the 20,000 trunk), flank groups on lanes 16% and 32% longer. One column on
        /// one route arrives together (spread 0 in this model); lanes may spread the arrival by at most
        /// <see cref="RvrLanePacing.ArrivalSpreadMarginMilliseconds"/>.
        /// </summary>
        [TestCase(RvrSkillTier.Casual)]
        [TestCase(RvrSkillTier.Average)]
        [TestCase(RvrSkillTier.Veteran)]
        public void The_column_arrives_at_the_siege_camp_within_the_stated_spread(RvrSkillTier tier)
        {
            ImmutableArray<RvrLaneRoute> routes = March().Routes;
            const float speed = 191, step = 0.5f;
            RvrLaneMargins margins = RvrLanePacing.Margins(tier);
            float[] walked = new float[routes.Length];
            double[] arrived = Enumerable.Repeat(-1.0, routes.Length).ToArray();
            var pace = new RvrLanePace[routes.Length];
            var pending = new long[routes.Length];
            long waitUntil = 0, nextWait = 0;
            double leaderWaited = 0;
            for (double t = 0; t < 3_600 && arrived.Any(a => a < 0); t += step)
            {
                long now = (long)(t * 1000) + 1;
                float worst = 0;
                for (int i = 1; i < routes.Length; i++)
                    if (arrived[i] < 0) worst = MathF.Min(worst, RvrLanePacing.Gap(walked[i], routes[i], walked[0], routes[0]));
                bool leaderMoves = arrived[0] < 0 && !RvrLanePacing.LeaderWaits(worst, now, ref waitUntil, ref nextWait);
                if (arrived[0] < 0 && !leaderMoves) leaderWaited += step;
                for (int i = 1; i < routes.Length; i++)
                {
                    if (arrived[i] >= 0) continue;
                    float gap = RvrLanePacing.Gap(walked[i], routes[i], walked[0], routes[0]);
                    pace[i] = RvrLanePacing.Next(pace[i], pending[i], gap, margins, now, out pending[i]);
                    bool holds = RvrLanePacing.Waits(pace[i], gap, leaderMoves || arrived[0] >= 0);
                    if (!holds) walked[i] += speed * step;
                }
                if (leaderMoves) walked[0] += speed * step;
                for (int i = 0; i < routes.Length; i++)
                    if (arrived[i] < 0 && walked[i] >= routes[i].MergeLength) arrived[i] = t;
            }
            double spread = (arrived.Max() - arrived.Min()) * 1000;
            double alone = routes[0].MergeLength / speed, longest = routes[^1].MergeLength / speed;
            TestContext.Progress.WriteLine(FormattableString.Invariant(
                $"lanes {tier}: arrivals {string.Join("/", arrived.Select(a => ((int)a).ToString()))} s, spread {spread / 1000:0} s, leader waited {leaderWaited:0} s; one route alone {alone:0} s, longest lane alone {longest:0} s"));
            Assert.Multiple(() =>
            {
                Assert.That(arrived.All(a => a >= 0), Is.True, "every lane reaches the camp ring");
                Assert.That(spread, Is.LessThanOrEqualTo(RvrLanePacing.ArrivalSpreadMarginMilliseconds));
                Assert.That(arrived[0], Is.GreaterThanOrEqualTo(alone - 1), "the leader is not faster than today");
                Assert.That(arrived.Max(), Is.LessThanOrEqualTo(longest + 120), "no lane is held long past its own walking time");
            });
        }

        [Test]
        public void Roaming_groups_take_lanes_between_catalog_objectives_only()
        {
            RvrLaneRoute Roam(string key, RvrTacticsSwitches switches, string to = "keep:50") =>
                RvrLaneChoice.ForRoam(switches, Data(), Proofs(), Region, eRealm.Hibernia, new(0, 20_300, 0), to, key, [], [], null, out _);
            int[] counts = new int[3];
            for (int i = 0; i < 8_000; i++) counts[Roam($"roam-{i}", RvrTacticsSwitches.AllOn).Slot]++;
            Assert.Multiple(() =>
            {
                Assert.That(Roam("g", RvrTacticsSwitches.AllOn with { RoamLanes = false }), Is.Null, "rvr_roam_lanes off");
                Assert.That(Roam("g", RvrTacticsSwitches.AllOn, "camp:somewhere"), Is.Null, "not a catalog objective");
                Assert.That(Roam("g", RvrTacticsSwitches.AllOn).Slot, Is.EqualTo(Roam("g", RvrTacticsSwitches.AllOn).Slot), "fixed per group");
                Assert.That(counts[0] / 80.0, Is.EqualTo(50).Within(2.5));
                Assert.That(counts[1] / 80.0, Is.EqualTo(25).Within(2.5));
                Assert.That(counts[2] / 80.0, Is.EqualTo(25).Within(2.5));
            });
        }
    }
}
