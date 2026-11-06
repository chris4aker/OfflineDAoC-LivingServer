using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// RvR perception (AutonomousRvrPerception, owner 2026-11-01): the notice chance by distance and boosts, the always
    /// radius and the outer limit, the difficulty levels, the reaction delay, spotter calls, the noticed-target memory, and
    /// that targets other than players and what they bring are not touched. Pure rules only; nothing here plays the game.
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
        public void The_chance_falls_with_distance_to_the_far_chance_and_is_zero_beyond_the_outer_range()
        {
            double previous = 1.0;
            for (int distance = 1_001; distance <= 3_500; distance += 100)
            {
                double chance = Chance(At(distance));
                Assert.That(chance, Is.LessThanOrEqualTo(previous), $"at {distance}");
                previous = chance;
            }
            Assert.That(Chance(At(1_001)), Is.EqualTo(AutonomousRvrPerception.NearChance).Within(0.001));
            Assert.That(Chance(At(3_500)), Is.EqualTo(AutonomousRvrPerception.FarChance).Within(1e-9));
            Assert.That(Chance(At(3_501, combat: true, casting: true, group: 8, facing: true)), Is.Zero, "no boost reaches past the limit");
            Assert.That(Chance(At(5_000, combat: true)), Is.Zero, "the old 5,000-unit radar is gone");
        }

        [Test]
        public void Combat_casting_a_group_of_three_and_the_facing_cone_raise_the_chance_and_standing_still_lowers_it()
        {
            double plain = Chance(At(2_500));
            Assert.That(Chance(At(2_500, combat: true)), Is.EqualTo(plain * AutonomousRvrPerception.CombatFactor).Within(1e-9));
            Assert.That(Chance(At(2_500, casting: true)), Is.EqualTo(plain * AutonomousRvrPerception.CastingFactor).Within(1e-9));
            Assert.That(Chance(At(2_500, group: 2)), Is.EqualTo(plain), "a pair is not a group");
            Assert.That(Chance(At(2_500, group: 3)), Is.EqualTo(plain * AutonomousRvrPerception.GroupFactor).Within(1e-9));
            Assert.That(Chance(At(2_500, facing: true)), Is.EqualTo(plain * AutonomousRvrPerception.FacingFactor).Within(1e-9));
            Assert.That(Chance(At(2_500, still: true)), Is.EqualTo(plain * AutonomousRvrPerception.StillFactor).Within(1e-9));
            Assert.That(Chance(At(2_500, still: true, combat: true)), Is.EqualTo(plain * AutonomousRvrPerception.CombatFactor).Within(1e-9),
                "a fighter standing still is not hiding");
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
            Assert.That(memory.Count, Is.Zero);
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
    }
}
