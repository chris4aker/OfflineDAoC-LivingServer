using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// RvR perception (AutonomousRvrPerception, owner 2026-11-01; audit 2026-11-03 challenge 2): the notice chance by
    /// distance and boosts, the always radius and the outer limit, the difficulty levels, the reaction delay, spotter
    /// calls, the noticed-target memory, the observer cap, duty, and that targets other than players and what they bring
    /// are not touched. Pure rules only; nothing here plays the game. The statistical calibration and crowd checks are in
    /// UT_RvrPerceptionCalibration.
    /// </summary>
    [TestFixture]
    public class UT_RvrPerception
    {
        private static readonly RvrAwarenessProfile Normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);
        private static readonly RvrAwarenessProfile Easy = AutonomousRvrPerception.Profile(RvrBotAwareness.Easy);
        private static readonly RvrAwarenessProfile Veteran = AutonomousRvrPerception.Profile(RvrBotAwareness.Veteran);

        private static RvrSighting At(double distance, bool combat = false, bool casting = false, int group = 1,
            bool facing = false, bool still = false) => new(distance, combat, casting, group, facing, still);

        private static double Chance(RvrSighting sighting, RvrAwarenessProfile? profile = null) =>
            AutonomousRvrPerception.NoticeChance(sighting, profile ?? Normal);

        [Test]
        public void Within_the_always_radius_every_scan_notices_even_a_still_target()
        {
            Assert.That(Chance(At(0)), Is.EqualTo(1.0));
            Assert.That(Chance(At(1_000, still: true)), Is.EqualTo(1.0));
            AutonomousRvrPerception.NoticeChance(At(800), Normal, out RvrNoticeFactors factors);
            Assert.That(factors, Is.EqualTo(RvrNoticeFactors.Close));
            Assert.That(Chance(At(1_001)), Is.LessThan(1.0));
        }

        [Test]
        public void The_chance_falls_with_distance_and_is_zero_beyond_the_outer_range()
        {
            double previous = 1.0;
            for (int distance = 1_001; distance <= 3_500; distance += 100)
            {
                double chance = Chance(At(distance));
                Assert.That(chance, Is.LessThanOrEqualTo(previous), $"at {distance}");
                previous = chance;
            }
            Assert.That(Chance(At(1_001)), Is.GreaterThan(0.5), "just past the sure radius: well over half a scan");
            Assert.That(Chance(At(3_500)), Is.InRange(0.02, 0.05), "the outer range: a few percent a scan");
            Assert.That(Chance(At(3_501, combat: true, casting: true, group: 8, facing: true)), Is.Zero, "no boost reaches past the limit");
            Assert.That(Chance(At(5_000, combat: true)), Is.Zero, "the old 5,000-unit radar is gone");
        }

        [Test]
        public void Combat_casting_a_group_of_three_and_the_facing_cone_raise_the_chance_and_standing_still_lowers_it()
        {
            // A boost multiplies the rate of noticing: the chance per scan is the one whose median is the plain median / factor.
            double plainMedian = AutonomousRvrPerception.MedianSecondsToNotice(2_500, Normal);
            double Expected(double factor) => AutonomousRvrPerception.ChancePerScan(plainMedian / factor,
                AutonomousRvrPerception.DefaultScanIntervalMilliseconds);
            double plain = Chance(At(2_500));
            Assert.That(plain, Is.EqualTo(Expected(1)).Within(1e-12));
            Assert.That(Chance(At(2_500, combat: true)), Is.EqualTo(Expected(AutonomousRvrPerception.CombatFactor)).Within(1e-12));
            Assert.That(Chance(At(2_500, casting: true)), Is.EqualTo(Expected(AutonomousRvrPerception.CastingFactor)).Within(1e-12));
            Assert.That(Chance(At(2_500, group: 2)), Is.EqualTo(plain), "a pair is not a group");
            Assert.That(Chance(At(2_500, group: 3)), Is.EqualTo(Expected(AutonomousRvrPerception.GroupFactor)).Within(1e-12));
            Assert.That(Chance(At(2_500, facing: true)), Is.EqualTo(Expected(AutonomousRvrPerception.FacingFactor)).Within(1e-12));
            Assert.That(Chance(At(2_500, still: true)), Is.EqualTo(Expected(AutonomousRvrPerception.StillFactor)).Within(1e-12));
            Assert.That(Chance(At(2_500, still: true, combat: true)), Is.EqualTo(Expected(AutonomousRvrPerception.CombatFactor)).Within(1e-12),
                "a fighter standing still is not hiding");
            Assert.That(Chance(At(2_500, combat: true)), Is.GreaterThan(plain));
            Assert.That(Chance(At(2_500, still: true)), Is.LessThan(plain));
            AutonomousRvrPerception.NoticeChance(At(2_500, combat: true, facing: true), Normal, out RvrNoticeFactors factors);
            Assert.That(factors, Is.EqualTo(RvrNoticeFactors.Combat | RvrNoticeFactors.Facing));
            Assert.That(AutonomousRvrPerception.Describe(factors), Is.EqualTo("Combat+Facing"));
        }

        [Test]
        public void Boosts_never_make_a_scan_beyond_the_always_radius_certain()
        {
            Assert.That(Chance(At(1_100, combat: true, casting: true, group: 8, facing: true)), Is.EqualTo(AutonomousRvrPerception.MaxChance));
        }

        [Test]
        public void The_roll_decides_deterministically()
        {
            Assert.That(AutonomousRvrPerception.Notices(At(2_000), Normal, 0.0, out _), Is.True);
            Assert.That(AutonomousRvrPerception.Notices(At(2_000), Normal, 0.99, out _), Is.False);
            Assert.That(AutonomousRvrPerception.Notices(At(500), Normal, 0.999, out _), Is.True);
            Assert.That(AutonomousRvrPerception.Notices(At(4_000, combat: true), Normal, 0.0, out _), Is.False);
        }

        [Test]
        public void The_facing_cone_is_sixty_degrees_either_side()
        {
            Assert.That(AutonomousRvrPerception.InFacingCone(0), Is.True);
            Assert.That(AutonomousRvrPerception.InFacingCone(60), Is.True);
            Assert.That(AutonomousRvrPerception.InFacingCone(61), Is.False);
            Assert.That(AutonomousRvrPerception.InFacingCone(180), Is.False);
            Assert.That(AutonomousRvrPerception.InFacingCone(300), Is.True);
            Assert.That(AutonomousRvrPerception.InFacingCone(299), Is.False);
            Assert.That(AutonomousRvrPerception.InFacingCone(-30), Is.True);
        }

        [Test]
        public void Easy_normal_and_veteran_scale_the_range_and_the_chances()
        {
            Assert.That(Easy.OuterRange, Is.LessThan(Normal.OuterRange));
            Assert.That(Veteran.OuterRange, Is.GreaterThan(Normal.OuterRange));
            Assert.That(Easy.AlwaysNoticeRadius, Is.LessThan(Normal.AlwaysNoticeRadius));
            Assert.That(Veteran.AlwaysNoticeRadius, Is.GreaterThan(Normal.AlwaysNoticeRadius));
            foreach (int distance in new[] { 1_500, 2_000, 2_400 })
            {
                Assert.That(Chance(At(distance), Easy), Is.LessThan(Chance(At(distance), Normal)), $"easy at {distance}");
                Assert.That(Chance(At(distance), Veteran), Is.GreaterThan(Chance(At(distance), Normal)), $"veteran at {distance}");
            }
            Assert.That(Chance(At(3_000), Easy), Is.Zero);
            Assert.That(Chance(At(4_000), Veteran), Is.GreaterThan(0));
            Assert.That(Easy.SpotterRadius, Is.LessThan(Normal.SpotterRadius));
            Assert.That(Veteran.SpotterRadius, Is.GreaterThan(Normal.SpotterRadius));
        }

        [Test]
        public void The_setting_reads_easy_normal_and_veteran_and_anything_else_is_normal()
        {
            Assert.That(AutonomousRvrPerception.ParseAwareness("easy"), Is.EqualTo(RvrBotAwareness.Easy));
            Assert.That(AutonomousRvrPerception.ParseAwareness(" Veteran "), Is.EqualTo(RvrBotAwareness.Veteran));
            Assert.That(AutonomousRvrPerception.ParseAwareness("normal"), Is.EqualTo(RvrBotAwareness.Normal));
            Assert.That(AutonomousRvrPerception.ParseAwareness("radar"), Is.EqualTo(RvrBotAwareness.Normal));
            Assert.That(AutonomousRvrPerception.ParseAwareness(null), Is.EqualTo(RvrBotAwareness.Normal));
        }

        [Test]
        public void Normal_is_a_2003_player_not_radar_a_warband_rarely_sees_a_far_runner_on_the_same_scan()
        {
            // Eight bots scanning a lone enemy running past at 2,800 units, out of their facing cones.
            var random = new Random(2003);
            int trials = 10_000, wholeWarband = 0, anyone = 0;
            for (int trial = 0; trial < trials; trial++)
            {
                int noticed = Enumerable.Range(0, 8).Count(bot => AutonomousRvrPerception.Notices(At(2_800), Normal, random.NextDouble(), out _));
                if (noticed == 8) wholeWarband++;
                if (noticed > 0) anyone++;
            }
            Assert.That(wholeWarband, Is.Zero, "the old scan: all eight, every time");
            Assert.That(anyone / (double)trials, Is.LessThan(0.6), "most first scans at that range see nothing");
            // At 1,500 the same warband sees a fighting enemy within a scan or two.
            double close = Chance(At(1_500, combat: true));
            Assert.That(1 - Math.Pow(1 - close, 2), Is.GreaterThan(0.85));
        }

        [Test]
        public void Reaction_delays_stay_between_half_a_second_and_two_seconds_at_normal()
        {
            var types = Enum.GetValues<BotPersonalityType>();
            foreach (int level in new[] { 1, 10, 25, 40, 50, 60 })
            foreach (double roll in new[] { 0.0, 0.5, 1.0 })
            foreach (int risk in new[] { 0, 5, 9 })
            foreach (BotPersonalityType type in types)
            {
                int delay = AutonomousRvrPerception.ReactionDelayMilliseconds(level,
                    new BotPersonality(type, new BotPersonalityTraits(5, risk, 5, 5, 5)), roll, Normal);
                Assert.That(delay, Is.InRange(AutonomousRvrPerception.ReactionMinMilliseconds, AutonomousRvrPerception.ReactionMaxMilliseconds));
            }
            Assert.That(AutonomousRvrPerception.ReactionDelayMilliseconds(1, null, 0, Normal), Is.InRange(500, 2_000));
            Assert.That(AutonomousRvrPerception.ReactionDelayMilliseconds(50, Daredevil, 1, Normal), Is.EqualTo(500));
            Assert.That(AutonomousRvrPerception.ReactionDelayMilliseconds(1, Timid, 0, Normal), Is.EqualTo(2_000));
        }

        private static readonly BotPersonality Daredevil = new(BotPersonalityType.Daredevil, new BotPersonalityTraits(5, 8, 5, 5, 5));
        private static readonly BotPersonality Timid = new(BotPersonalityType.Crafter, new BotPersonalityTraits(5, 0, 5, 5, 5));

        [Test]
        public void Higher_level_and_bolder_bots_react_faster_and_difficulty_scales_the_delay()
        {
            int Delay(int level, BotPersonality? personality, RvrAwarenessProfile profile) =>
                AutonomousRvrPerception.ReactionDelayMilliseconds(level, personality, 0.5, profile);
            Assert.That(Delay(50, Timid, Normal), Is.LessThan(Delay(10, Timid, Normal)));
            Assert.That(Delay(30, Daredevil, Normal), Is.LessThan(Delay(30, Timid, Normal)));
            Assert.That(Delay(30, null, Easy), Is.GreaterThan(Delay(30, null, Normal)));
            Assert.That(Delay(30, null, Veteran), Is.LessThan(Delay(30, null, Normal)));
            Assert.That(AutonomousRvrPerception.ReactionDelayMilliseconds(50, Daredevil, 1, Veteran), Is.EqualTo(350));
            Assert.That(AutonomousRvrPerception.ReactionDelayMilliseconds(1, Timid, 0, Easy), Is.EqualTo(2_600));
        }

        [Test]
        public void A_spotter_tells_living_mates_in_its_region_within_the_radius_but_not_itself()
        {
            var mates = new[]
            {
                new RvrSpotterMate(1, 0, 0, 0, true, true),          // the spotter
                new RvrSpotterMate(2, 2_000, 0, 0, true, true),
                new RvrSpotterMate(3, 1_500, 1_500, 0, true, true),  // 2,121 away
                new RvrSpotterMate(4, 2_000, 2_000, 0, true, true),  // 2,828 away
                new RvrSpotterMate(5, 100, 0, 0, false, true),       // dead
                new RvrSpotterMate(6, 100, 0, 0, true, false),       // another region
                new RvrSpotterMate(2, 2_000, 0, 0, true, true),      // in both the group and the warband
            };
            Assert.That(AutonomousRvrPerception.SpotterRecipients(1, 0, 0, 0, mates, Normal.SpotterRadius), Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(AutonomousRvrPerception.SpotterRecipients(1, 0, 0, 0, mates, Veteran.SpotterRadius), Is.EqualTo(new long[] { 2, 3, 4 }));
            Assert.That(AutonomousRvrPerception.SpotterRecipients(1, 0, 0, 0, mates, Easy.SpotterRadius), Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Noticed_targets_wait_for_the_reaction_delay_and_are_forgotten_out_of_sight()
        {
            var memory = new AutonomousRvrPerception.Memory();
            Assert.That(memory.Seen(1, 77, 10_000), Is.EqualTo(AutonomousRvrPerception.Memory.State.Unnoticed));
            memory.Notice(1, 77, 10_000, 1_200);
            Assert.That(memory.Seen(1, 77, 10_500), Is.EqualTo(AutonomousRvrPerception.Memory.State.Reacting));
            Assert.That(memory.Seen(1, 77, 11_200), Is.EqualTo(AutonomousRvrPerception.Memory.State.Ready));
            Assert.That(memory.Seen(2, 77, 11_200), Is.EqualTo(AutonomousRvrPerception.Memory.State.Unnoticed), "per bot");
            Assert.That(memory.Seen(1, 78, 11_200), Is.EqualTo(AutonomousRvrPerception.Memory.State.Unnoticed), "per target");
            Assert.That(memory.Seen(1, 77, 11_200 + AutonomousRvrPerception.ForgetAfterMilliseconds + 1),
                Is.EqualTo(AutonomousRvrPerception.Memory.State.Unnoticed));
        }

        [Test]
        public void A_call_makes_a_mate_notice_after_its_own_delay_and_never_resets_what_it_already_noticed()
        {
            var memory = new AutonomousRvrPerception.Memory();
            Assert.That(memory.Alert(2, 77, 0, 800), Is.True);
            Assert.That(memory.Seen(2, 77, 700), Is.EqualTo(AutonomousRvrPerception.Memory.State.Reacting));
            Assert.That(memory.Seen(2, 77, 800), Is.EqualTo(AutonomousRvrPerception.Memory.State.Ready));
            Assert.That(memory.Alert(2, 77, 900, 2_000), Is.False, "already noticed");
            Assert.That(memory.Seen(2, 77, 1_000), Is.EqualTo(AutonomousRvrPerception.Memory.State.Ready));
        }

        [Test]
        public void Old_entries_are_swept()
        {
            var memory = new AutonomousRvrPerception.Memory();
            for (int target = 0; target < 50; target++)
                memory.Notice(1, target, 0, 500);
            Assert.That(memory.Count, Is.EqualTo(50));
            memory.Seen(9, 9, AutonomousRvrPerception.ForgetAfterMilliseconds * 2 + 1);
            Assert.That(memory.Count, Is.EqualTo(1), "only the new sighting is left");
        }

        [Test]
        public void The_spot_call_names_the_count_and_the_compass_direction()
        {
            Assert.That(AutonomousRvrPerception.SpotCall(3, 0, -2_000), Is.EqualTo("inc 3 north"));
            Assert.That(AutonomousRvrPerception.SpotCall(1, 0, 2_000), Is.EqualTo("inc 1 south"));
            Assert.That(AutonomousRvrPerception.SpotCall(0, 1_500, 0), Is.EqualTo("inc 1 east"));
            Assert.That(AutonomousRvrPerception.SpotCall(8, -1_000, -1_000), Is.EqualTo("inc 8 nw"));
            Assert.That(AutonomousRvrPerception.Direction(1_000, 1_000), Is.EqualTo("se"));
        }

        [Test]
        public void Only_players_companions_and_pets_are_perceived_and_retaliation_stays_immediate()
        {
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.Player, false, false), Is.True);
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.PlayerCompanion, false, false), Is.True);
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.PlayerPet, false, false), Is.True);
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.Other, false, false), Is.False,
                "enemy gamebots, siege, guards, doors and monsters are acquired as before");
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.Player, firingOnRealm: true, attackingGroup: false), Is.False);
            Assert.That(AutonomousRvrPerception.Applies(true, RvrPerceivedKind.Player, firingOnRealm: false, attackingGroup: true), Is.False);
            Assert.That(AutonomousRvrPerception.Applies(false, RvrPerceivedKind.Player, false, false), Is.False, "the setting off");
        }

        // ---- calibration curve (audit 2026-11-03, technical §8.2)

        [Test]
        public void The_normal_curve_passes_through_the_audit_targets_and_each_level_has_its_own()
        {
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(1_500, Normal), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(2_500, Normal), Is.EqualTo(6).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(3_400, Normal), Is.EqualTo(20).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(2_000, Normal), Is.EqualTo(3).Within(1e-9), "log scale: half way is the geometric mean");
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(3_500, Normal), Is.InRange(22, 24), "the last slope carries on");
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(900, Normal), Is.Zero);
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(3_501, Normal), Is.EqualTo(double.PositiveInfinity));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(1_500, Easy), Is.EqualTo(3).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(2_500, Easy), Is.EqualTo(12).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(3_400, Easy), Is.EqualTo(double.PositiveInfinity), "easy never notices at 3,400");
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(1_500, Veteran), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(2_500, Veteran), Is.EqualTo(3).Within(1e-9));
            Assert.That(AutonomousRvrPerception.MedianSecondsToNotice(3_400, Veteran), Is.EqualTo(10).Within(1e-9));
            double previous = 0;
            for (int distance = 1_001; distance <= 3_500; distance += 50)
            {
                double median = AutonomousRvrPerception.MedianSecondsToNotice(distance, Normal);
                Assert.That(median, Is.GreaterThan(previous), $"rises with distance at {distance}");
                previous = median;
            }
        }

        [TestCase(0.75, 1_125)]
        [TestCase(1.5, 1_000)]
        [TestCase(6, 1_125)]
        [TestCase(20, 1_249)]
        [TestCase(20, 2_000)]
        [TestCase(0.9, 1_000)]
        public void The_chance_per_scan_puts_exactly_half_the_sightings_past_the_median(double median, int interval)
        {
            // First sight at a uniformly random moment between two scans: P(nothing by t = kΔ + r) = (1 - (r/Δ)p)(1 - p)^k.
            double p = AutonomousRvrPerception.ChancePerScan(median, interval);
            double m = median * 1000 / interval;
            int k = (int)Math.Floor(m);
            double remaining = (1 - (m - k) * p) * Math.Pow(1 - p, k);
            Assert.That(remaining, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(p, Is.InRange(0.0, 1.0));
        }

        [Test]
        public void A_slower_scan_gets_a_higher_chance_per_scan_so_the_clock_stays_the_same()
        {
            double fast = AutonomousRvrPerception.NoticeChance(At(2_500), Normal, 1_000, out _);
            double slow = AutonomousRvrPerception.NoticeChance(At(2_500), Normal, 2_000, out _);
            Assert.That(slow, Is.GreaterThan(fast));
            Assert.That(AutonomousRvrPerception.NoticeChance(At(2_500), Normal, 60_000, out _),
                Is.EqualTo(AutonomousRvrPerception.NoticeChance(At(2_500), Normal, AutonomousRvrPerception.MaxScanIntervalMilliseconds, out _)),
                "a bot that didn't scan for a minute (it was fighting) gets no burst");
            Assert.That(AutonomousRvrPerception.ChancePerScan(0.4, 1_000), Is.EqualTo(1), "a median under half a scan: every scan");
            Assert.That(AutonomousRvrPerception.ChancePerScan(double.PositiveInfinity, 1_000), Is.Zero);
        }

        // ---- the observer cap

        [Test]
        public void At_most_three_bots_of_a_realm_roll_for_one_enemy_each_second_at_normal()
        {
            Assert.That(Easy.ObserverCap, Is.EqualTo(2));
            Assert.That(Normal.ObserverCap, Is.EqualTo(3));
            Assert.That(Veteran.ObserverCap, Is.EqualTo(5));
            var budget = new AutonomousRvrPerception.ObserverBudget();
            int claimed = Enumerable.Range(0, 10).Count(_ => budget.TryClaim(77, 1, 5_100, 3));
            Assert.That(claimed, Is.EqualTo(3));
            Assert.That(budget.IsFull(77, 1, 5_999, 3), Is.True);
            Assert.That(budget.TryClaim(77, 2, 5_100, 3), Is.True, "each realm has its own slots");
            Assert.That(budget.TryClaim(78, 1, 5_100, 3), Is.True, "each enemy has its own slots");
            Assert.That(budget.IsFull(77, 1, 6_000, 3), Is.False, "a new second");
            Assert.That(Enumerable.Range(0, 10).Count(_ => budget.TryClaim(77, 1, 6_000, 3)), Is.EqualTo(3));
            Assert.That(budget.TryClaim(77, 1, 6_000, 0), Is.True, "cap 0: no cap");
        }

        [Test]
        public void Observer_windows_are_swept()
        {
            var budget = new AutonomousRvrPerception.ObserverBudget();
            for (int target = 0; target < 40; target++)
                budget.TryClaim(target, 1, 0, 3);
            Assert.That(budget.Count, Is.EqualTo(40));
            budget.TryClaim(999, 1, 60_000, 3);
            Assert.That(budget.Count, Is.EqualTo(1));
        }

        private static RvrScanOutcome Scan(AutonomousRvrPerception.Memory memory, AutonomousRvrPerception.ObserverBudget budget, long bot,
            long now, double distance, double roll = 0, bool mayAnswerCall = true, int realm = 1, int delay = 800) =>
            AutonomousRvrPerception.Scan(memory, budget, bot, realm, 77, now, At(distance), Normal, 1_125, roll, () => delay, mayAnswerCall);

        [Test]
        public void A_bot_over_the_cap_does_not_roll_and_learns_only_from_a_call()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            // Three bots look (and miss) at 2,800 in the same second; the fourth doesn't get to look at all.
            for (long bot = 1; bot <= 3; bot++)
                Assert.That(Scan(memory, budget, bot, 10_000, 2_800, roll: 0.99).Result, Is.EqualTo(RvrScanResult.Unseen));
            Assert.That(Scan(memory, budget, 4, 10_100, 2_800, roll: 0).Result, Is.EqualTo(RvrScanResult.Capped), "a sure roll, but no slot");
            // Next second the first of them notices and calls; bot 4 is told and engages after its own delay.
            RvrScanOutcome spotted = Scan(memory, budget, 1, 11_100, 2_800, roll: 0);
            Assert.That(spotted.Result, Is.EqualTo(RvrScanResult.Noticed));
            Assert.That(spotted.SinceFirstSightMilliseconds, Is.EqualTo(1_100), "time since first line of sight, for the log");
            Assert.That(memory.Alert(4, 77, 11_100, 900), Is.True);
            Assert.That(Scan(memory, budget, 4, 11_500, 2_800).Result, Is.EqualTo(RvrScanResult.Reacting));
            Assert.That(Scan(memory, budget, 4, 12_000, 2_800).Result, Is.EqualTo(RvrScanResult.Engage));
            Assert.That(Scan(memory, budget, 1, 12_000, 2_800).Result, Is.EqualTo(RvrScanResult.Engage), "the spotter after its 800 ms");
        }

        [Test]
        public void Close_up_a_bot_always_notices_without_a_slot_and_out_of_range_it_takes_none()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            for (long bot = 1; bot <= 3; bot++)
                Scan(memory, budget, bot, 10_000, 2_000, roll: 0.99);
            Assert.That(budget.IsFull(77, 1, 10_000, Normal.ObserverCap), Is.True);
            Assert.That(Scan(memory, budget, 9, 10_200, 900, roll: 0.999).Result, Is.EqualTo(RvrScanResult.Noticed), "within 1,000: always");
            var fresh = new AutonomousRvrPerception.ObserverBudget();
            Assert.That(Scan(memory, fresh, 10, 10_000, 4_000, roll: 0).Result, Is.EqualTo(RvrScanResult.Unseen));
            Assert.That(fresh.Count, Is.Zero, "no slot spent beyond the outer range");
        }

        [Test]
        public void A_bot_whose_duty_holds_it_drops_a_call_and_looks_for_itself()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            memory.Alert(5, 77, 0, 500);
            RvrScanOutcome held = Scan(memory, budget, 5, 1_000, 2_800, roll: 0.99, mayAnswerCall: false);
            Assert.That(held.Result, Is.EqualTo(RvrScanResult.Unseen), "the call is dropped; its own roll missed");
            Assert.That(memory.Peek(5, 77, 1_000), Is.EqualTo(AutonomousRvrPerception.Memory.State.Unnoticed));
            // What it notices itself it engages, whatever its duty (it fights what comes at it).
            Assert.That(Scan(memory, budget, 5, 2_000, 2_800, roll: 0, mayAnswerCall: false).Result, Is.EqualTo(RvrScanResult.Noticed));
            Assert.That(Scan(memory, budget, 5, 3_000, 2_800, mayAnswerCall: false).Result, Is.EqualTo(RvrScanResult.Engage));
        }

        [Test]
        public void Only_free_rvr_event_and_bot_group_duties_answer_a_call()
        {
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.None), Is.True, "free");
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.RvrEvent), Is.True, "an RvR march");
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.BotGroupSession), Is.True, "its bot group is who the call is for");
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.RealmRaid), Is.False);
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.BattlegroundWave), Is.False);
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.PlayerGroup), Is.False);
            Assert.That(AutonomousRvrPerception.MayAnswerCall(eDutyLeaseKind.BotLedGroupWithPlayer), Is.False);
        }

        [Test]
        public void A_call_reaches_the_group_at_once_and_other_warband_groups_half_the_time_later_and_held_mates_are_not_told()
        {
            var group = new[]
            {
                new RvrSpotterMate(1, 0, 0, 0, true, true),
                new RvrSpotterMate(2, 300, 0, 0, true, true),
                new RvrSpotterMate(3, 400, 0, 0, true, true, MayAnswer: false),   // on a raid
                new RvrSpotterMate(4, 3_000, 0, 0, true, true),                   // too far
            };
            var nearGroup = new[] { new RvrSpotterMate(10, 1_000, 0, 0, true, true), new RvrSpotterMate(11, 1_100, 0, 0, true, true) };
            var otherGroup = new[] { new RvrSpotterMate(20, 0, 1_000, 0, true, true) };
            var rolls = new Queue<double>(new[] { 0.2, 0.7 });   // the first other group hears it, the second doesn't
            List<RvrCallee> callees = AutonomousRvrPerception.CallRecipients(1, 0, 0, 0, group,
                new IReadOnlyCollection<RvrSpotterMate>[] { nearGroup, otherGroup }, Normal.SpotterRadius, rolls.Dequeue, out int held);
            Assert.That(held, Is.EqualTo(1));
            Assert.That(callees.Select(c => c.Id), Is.EqualTo(new long[] { 2, 10, 11 }));
            Assert.That(callees[0], Is.EqualTo(new RvrCallee(2, 0, false)));
            Assert.That(callees[1], Is.EqualTo(new RvrCallee(10, AutonomousRvrPerception.WarbandRelayDelayMilliseconds, true)));
        }

        [Test]
        public void The_line_of_sight_ray_is_skipped_only_when_the_scan_could_not_use_it()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            bool Skips(long bot, double distance, long now = 10_000) =>
                AutonomousRvrPerception.SkipsLineOfSight(memory, budget, bot, 1, 77, now, distance, Normal);
            Assert.That(Skips(1, 2_000), Is.False, "slots free: look");
            Assert.That(Skips(1, 4_000), Is.True, "beyond the outer range a bot can't notice on its own");
            for (int i = 0; i < 3; i++) budget.TryClaim(77, 1, 10_000, 3);
            Assert.That(Skips(1, 2_000), Is.True, "slots taken this second");
            Assert.That(Skips(1, 800), Is.False, "within the sure radius it always looks");
            memory.Alert(2, 77, 10_000, 500);
            Assert.That(Skips(2, 2_000), Is.False, "told of it: it needs to see it to engage");
            Assert.That(Skips(2, 4_500), Is.False, "told of it, even beyond the outer range");
            Assert.That(Skips(1, 2_000, now: 11_000), Is.False, "a new second");
        }
    }
}
