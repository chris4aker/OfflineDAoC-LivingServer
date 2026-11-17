using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 2 (docs/BOTS-AS-PEOPLE-1.2.md, item 2, "Skill in the hands"): the mistake rates and
    /// delays by tier, the caps (no named pull, no monster more than 2 levels above the group, one add at most, no add
    /// for a duo or in a dungeon, no buff lapse at a boss, a caster's reserve), the quarter rate in a group with a real
    /// player (owner decision 4), RvR groups keeping their group tier with a member only spreading inside its band
    /// (decision 5), and the scope: no hook acts in a raid, a battleground assault, at a keep or for a companion. Pure rules:
    /// tests only, nothing here is seen in game.
    /// </summary>
    [TestFixture]
    public class UT_BotSkillMistakes
    {
        private const int Ids = 10_000;

        private static readonly BotSkillMistakeKind[] ChanceKinds =
            [BotSkillMistakeKind.Position, BotSkillMistakeKind.Pull, BotSkillMistakeKind.Buff, BotSkillMistakeKind.Power];

        private static IEnumerable<BotSkillProfile> Profiles(int count = Ids) =>
            Enumerable.Range(1, count).Select(id => BotSkillProfile.For(BotPersonKey.Of(id), null, RvrBotAwareness.Normal));

        private static (double Casual, double Average, double Veteran) Design(BotSkillMistakeKind kind) => kind switch
        {
            BotSkillMistakeKind.Position => BotSkillMistakes.PositionChances,
            BotSkillMistakeKind.Pull => BotSkillMistakes.PullChances,
            BotSkillMistakeKind.Buff => BotSkillMistakes.BuffChances,
            _ => BotSkillMistakes.PowerChances,
        };

        private static double ByTier((double Casual, double Average, double Veteran) values, RvrSkillTier tier) => tier switch
        {
            RvrSkillTier.Casual => values.Casual,
            RvrSkillTier.Average => values.Average,
            _ => values.Veteran,
        };

        // ---- rates by tier --------------------------------------------------------------------------------------------

        [Test]
        public void The_middle_of_each_band_is_the_design_value()
        {
            foreach (BotSkillMistakeKind kind in ChanceKinds)
            {
                var design = Design(kind);
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                    Assert.That(BotSkillProfile.Blend(tier, 0.5, design.Casual, design.Average, design.Veteran),
                        Is.EqualTo(ByTier(design, tier)).Within(1e-9), $"{kind} {tier}");
            }
            Assert.That(BotSkillMistakes.ReactionFactor(RvrSkillTier.Casual, 0.5), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(BotSkillMistakes.ReactionFactor(RvrSkillTier.Average, 0.5), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(BotSkillMistakes.ReactionFactor(RvrSkillTier.Veteran, 0.5), Is.EqualTo(0.75).Within(1e-9));
        }

        [Test]
        public void Rates_by_tier_over_10000_bots_are_the_design_rates_and_fall_with_the_tier()
        {
            var profiles = Profiles().ToArray();
            foreach (BotSkillMistakeKind kind in ChanceKinds)
            {
                var design = Design(kind);
                var means = new double[3];
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                {
                    var rates = profiles.Where(p => p.Tier == tier).Select(p => BotSkillMistakes.Chance(kind, p, false, true)).ToArray();
                    Assert.That(rates, Is.Not.Empty);
                    means[(int)tier] = rates.Average();
                    // The facets spread a bot inside its band: the tier's mean stays near the design value.
                    Assert.That(means[(int)tier], Is.EqualTo(ByTier(design, tier)).Within(ByTier(design, tier) * 0.3), $"{kind} {tier}");
                }
                Assert.That(means[0], Is.GreaterThan(means[1]), kind.ToString());
                Assert.That(means[1], Is.GreaterThan(means[2]), kind.ToString());
            }
        }

        [Test]
        public void Rolled_mistakes_follow_the_chance()
        {
            var random = new Random(7);
            foreach (double chance in new[] { 0.1, 0.05, 0.015 })
            {
                int made = 0;
                for (int i = 0; i < 100_000; i++)
                    made += BotSkillMistakes.Rolls(chance, random.NextDouble()) ? 1 : 0;
                Assert.That(made / 100_000.0, Is.EqualTo(chance).Within(chance * 0.1 + 0.002));
            }
            Assert.That(BotSkillMistakes.Rolls(0, 0), Is.False, "a chance of 0 never rolls");
        }

        [Test]
        public void Delays_by_tier_are_the_design_delays()
        {
            var profiles = Profiles().ToArray();
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                var tierProfiles = profiles.Where(p => p.Tier == tier).ToArray();
                double factor = tierProfiles.Average(p => BotSkillMistakes.ReactionFactor(p));
                Assert.That(factor, Is.EqualTo(ByTier(BotSkillMistakes.ReactionFactors, tier)).Within(0.06), $"reactions {tier}");
                var (fireMin, fireMax) = BotSkillMistakes.FireBand(tier);
                double fire = tierProfiles.Average(p => BotSkillMistakes.FireDelayMilliseconds(p, 0.5));
                Assert.That(fire, Is.EqualTo((fireMin + fireMax) / 2.0).Within((fireMax - fireMin) * 0.2), $"fire {tier}");
                (double lapseMin, double lapseMax) = tier switch
                {
                    RvrSkillTier.Casual => BotSkillMistakes.BuffLapseCasual,
                    RvrSkillTier.Average => BotSkillMistakes.BuffLapseAverage,
                    _ => BotSkillMistakes.BuffLapseVeteran,
                };
                double lapse = tierProfiles.Average(p => BotSkillMistakes.BuffLapseMilliseconds(p, 0.5)) / 1000;
                Assert.That(lapse, Is.EqualTo((lapseMin + lapseMax) / 2).Within((lapseMax - lapseMin) * 0.2), $"buff lapse {tier}");
                double floor = tierProfiles.Average(p => BotSkillMistakes.PowerRestFloor(p, false, true, false));
                Assert.That(floor, Is.EqualTo(ByTier(BotSkillMistakes.PowerRestFloors, tier)).Within(3), $"power floor {tier}");
            }
            // Nobody waits forever or reacts instantly: every delay keeps a floor and a ceiling.
            foreach (BotSkillProfile p in profiles)
            {
                Assert.That(BotSkillMistakes.FireDelayMilliseconds(p, 0), Is.GreaterThanOrEqualTo(250));
                Assert.That(BotSkillMistakes.FireDelayMilliseconds(p, 1), Is.LessThanOrEqualTo(5_750));
                Assert.That(BotSkillMistakes.BuffLapseMilliseconds(p, 1), Is.LessThanOrEqualTo(110_000));
                Assert.That(BotSkillMistakes.ReactionFactor(p), Is.InRange(0.6, 1.45));
            }
        }

        [Test]
        public void No_profile_means_todays_rules()
        {
            BotSkillProfile none = default;
            foreach (BotSkillMistakeKind kind in Enum.GetValues<BotSkillMistakeKind>())
                Assert.That(BotSkillMistakes.Chance(kind, none, false, true), Is.EqualTo(0));
            Assert.That(BotSkillMistakes.ReactionFactor(none), Is.EqualTo(1));
            Assert.That(BotSkillMistakes.BuffLapseMilliseconds(none, 0.9), Is.EqualTo(0));
            Assert.That(BotSkillMistakes.PowerRestFloor(none, false, true, false), Is.EqualTo(AutonomousRestPolicy.PowerRestThreshold));
            Assert.That(BotSkillMistakes.TodayPowerRestFloor, Is.EqualTo(AutonomousRestPolicy.PowerRestThreshold));
        }

        [Test]
        public void The_live_hooks_keep_todays_rule_for_no_gamebot()
        {
            // A null bot stands for anything with no person (a companion, a helper, a player's pet): every hook gives today's answer.
            Assert.That(BotSkillHands.ReactionFactor(null, out BotSkillProfile profile, out bool spared), Is.EqualTo(1));
            Assert.That(profile.IsNone, Is.True);
            Assert.That(spared, Is.False);
            Assert.That(BotSkillHands.PullMistake(null, Array.Empty<GameNPC>()), Is.EqualTo(-1));
            Assert.That(BotSkillHands.BuffLapse(null, null, null, false), Is.EqualTo(0));
            Assert.That(BotSkillHands.PowerRestFloor(null), Is.EqualTo(AutonomousRestPolicy.PowerRestThreshold));
            Assert.That(BotSkillHands.ChooseSpell(null, Array.Empty<Spell>(), null), Is.Null);
            Assert.That(BotSkillHands.FireDelay(null, RvrSkillTier.Casual), Is.EqualTo(-1), "out of scope: it does not step out (today)");
            Assert.That(BotSkillHands.RvrPlaceDelay(null, RvrSkillTier.Average, 1_234), Is.EqualTo(1_234));
            Assert.That(BotSkillHands.PositionMistake(null, null, false), Is.False);
        }

        // ---- the quarter rate in the player's group (owner decision 4) ------------------------------------------------

        [Test]
        public void In_a_group_with_a_real_player_a_chance_is_a_quarter()
        {
            foreach (BotSkillProfile p in Profiles(2_000))
                foreach (BotSkillMistakeKind kind in ChanceKinds)
                {
                    double own = BotSkillMistakes.Chance(kind, p, false, true);
                    Assert.That(BotSkillMistakes.Chance(kind, p, true, true), Is.EqualTo(own * 0.25).Within(1e-12), kind.ToString());
                    Assert.That(BotSkillMistakes.Chance(kind, p, true, false), Is.EqualTo(own).Within(1e-12),
                        "bot_skill_spare_player_group off: the bot's own rate");
                }
        }

        [Test]
        public void The_quarter_shows_in_rolled_counts()
        {
            BotSkillProfile casual = Profiles(500).First(p => p.Tier == RvrSkillTier.Casual);
            var random = new Random(11);
            int strangers = 0, player = 0;
            for (int i = 0; i < 200_000; i++)
            {
                strangers += BotSkillMistakes.Rolls(BotSkillMistakes.Chance(BotSkillMistakeKind.Pull, casual, false, true), random.NextDouble()) ? 1 : 0;
                player += BotSkillMistakes.Rolls(BotSkillMistakes.Chance(BotSkillMistakeKind.Pull, casual, true, true), random.NextDouble()) ? 1 : 0;
            }
            Assert.That((double)player / strangers, Is.EqualTo(0.25).Within(0.03));
        }

        [Test]
        public void Delays_are_not_shortened_for_the_players_group_and_the_power_floor_moves_back_three_quarters()
        {
            foreach (BotSkillProfile p in Profiles(2_000))
            {
                Assert.That(BotSkillMistakes.IsChance(BotSkillMistakeKind.Reaction), Is.False);
                Assert.That(BotSkillMistakes.IsChance(BotSkillMistakeKind.Fire), Is.False);
                int own = BotSkillMistakes.PowerRestFloor(p, false, true, false);
                int spared = BotSkillMistakes.PowerRestFloor(p, true, true, false);
                double expected = 45 - (45 - p.Blend(BotSkillFacet.Power, 25, 35, 45)) * 0.25;
                Assert.That(spared, Is.EqualTo((int)Math.Round(Math.Min(45, expected))));
                Assert.That(spared, Is.GreaterThanOrEqualTo(own));
                Assert.That(BotSkillMistakes.PowerRestFloor(p, true, false, false), Is.EqualTo(own), "switch off: its own floor");
            }
        }

        // ---- the caps: never a death sentence ---------------------------------------------------------------------------

        private static readonly BotPullGroup Party = new(Size: 5, TopLevel: 30, AllAlive: true, InDungeon: false);

        private static BotPullView Mob(float x, int level = 30, int con = 0, bool named = false, int aggro = 300) =>
            new(new Vector3(x, 0, 0), aggro, level, con, named);

        [Test]
        public void A_careless_pull_takes_a_monster_with_exactly_one_safe_add()
        {
            // 0: alone (the clean pull); 1 and 2: a pair 200 apart; 3: far away.
            var pool = new[] { Mob(-5_000), Mob(1_000), Mob(1_200), Mob(9_000) };
            Assert.That(BotSkillMistakes.ChooseAddPull(pool, Vector3.Zero, Party), Is.EqualTo(1), "the nearer of the pair");
            Assert.That(BotSkillMistakes.ChooseAddPull(pool, new Vector3(1_300, 0, 0), Party), Is.EqualTo(2));
            // Three together: each has two neighbours; never two adds.
            var crowd = new[] { Mob(0), Mob(100), Mob(200) };
            Assert.That(BotSkillMistakes.ChooseAddPull(crowd, Vector3.Zero, Party), Is.EqualTo(-1));
            // Nobody near anyone: no add to take, the clean pull stands.
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0), Mob(5_000)], Vector3.Zero, Party), Is.EqualTo(-1));
        }

        [Test]
        public void A_careless_pull_never_takes_a_named_or_a_monster_over_2_levels_above_the_group()
        {
            // The named is never pulled by mistake, and a plain monster whose only neighbour is a named is not either.
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, named: true), Mob(100)], Vector3.Zero, Party), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0), Mob(100, named: true)], Vector3.Zero, Party), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, named: true), Mob(100, named: true)], Vector3.Zero, Party), Is.EqualTo(-1));

            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, level: 32), Mob(100, level: 32)], Vector3.Zero, Party), Is.Not.EqualTo(-1),
                "two levels above the group's top is allowed");
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, level: 33), Mob(100, level: 33)], Vector3.Zero, Party), Is.EqualTo(-1),
                "three above: never");
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, level: 30), Mob(100, level: 33)], Vector3.Zero, Party), Is.EqualTo(-1),
                "an add three above: never, from either side");
        }

        [Test]
        public void A_named_is_never_part_of_a_careless_pull()
        {
            var random = new Random(3);
            for (int run = 0; run < 2_000; run++)
            {
                var pool = Enumerable.Range(0, random.Next(2, 8))
                    .Select(_ => Mob(random.Next(0, 2_000), level: random.Next(25, 36), con: random.Next(-2, 3), named: random.Next(4) == 0))
                    .ToArray();
                int pick = BotSkillMistakes.ChooseAddPull(pool, Vector3.Zero, Party);
                if (pick < 0)
                    continue;
                var adds = pool.Where((m, i) => i != pick && Vector3.Distance(m.Position, pool[pick].Position) <= pool[pick].AggroRange).ToArray();
                Assert.That(adds, Has.Length.EqualTo(1), "exactly one add");
                Assert.That(pool[pick].Named || adds[0].Named, Is.False, "no named");
                Assert.That(Math.Max(pool[pick].Level, adds[0].Level), Is.LessThanOrEqualTo(Party.TopLevel + 2), "no monster over +2");
                Assert.That(adds[0].Con, Is.LessThanOrEqualTo(pool[pick].Con), "the add is the same con or easier");
            }
        }

        [Test]
        public void An_add_never_comes_to_a_duo_a_party_with_a_dead_member_or_in_a_dungeon()
        {
            var pair = new[] { Mob(0), Mob(100) };
            Assert.That(BotSkillMistakes.ChooseAddPull(pair, Vector3.Zero, Party), Is.Not.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull(pair, Vector3.Zero, Party with { Size = 2 }), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull(pair, Vector3.Zero, Party with { AllAlive = false }), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull(pair, Vector3.Zero, Party with { InDungeon = true }), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.ChooseAddPull([Mob(0, con: 0), Mob(100, con: 1)], Vector3.Zero, Party), Is.EqualTo(1),
                "the orange one with a yellow add may be pulled; the yellow one with an orange add may not");
        }

        [Test]
        public void The_world_scan_around_a_careless_pick_allows_exactly_one_safe_add()
        {
            BotPullView pick = Mob(0, con: 0);
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150)], 30), Is.True);
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150), Mob(-200)], 30), Is.False, "a second monster of another kind beside it");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150, named: true)], 30), Is.False, "a named next to the pick");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150, level: 33)], 30), Is.False, "over +2");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150, con: 1)], 30), Is.False, "a harder con");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [Mob(150), Mob(900)], 30), Is.True, "outside its aggro range: not an add");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, [], 30), Is.False, "no add after all: the clean pull");
            Assert.That(BotSkillMistakes.AddScanAllows(pick, null, 30), Is.False);
            Assert.That(BotSkillMistakes.AddScanAllows(Mob(0, aggro: 0), [Mob(10)], 30), Is.False);
        }

        [Test]
        public void A_held_careless_pick_fires_only_while_the_scope_and_the_party_still_allow_it()
        {
            Assert.That(BotSkillMistakes.HeldPullStands(true, Party), Is.True);
            Assert.That(BotSkillMistakes.HeldPullStands(false, Party), Is.False, "the scope refuses now (a raid muster...)");
            Assert.That(BotSkillMistakes.HeldPullStands(true, Party with { AllAlive = false }), Is.False, "someone died meanwhile");
            Assert.That(BotSkillMistakes.HeldPullStands(true, Party with { Size = 2 }), Is.False);
            Assert.That(BotSkillMistakes.HeldPullStands(true, Party with { InDungeon = true }), Is.False);
            Assert.That(BotSkillMistakes.LapseHolds(true, false, false), Is.True);
            Assert.That(BotSkillMistakes.LapseHolds(false, false, false), Is.False);
            Assert.That(BotSkillMistakes.LapseHolds(true, true, false), Is.False);
            Assert.That(BotSkillMistakes.LapseHolds(true, false, true), Is.False);
        }

        [Test]
        public void Stepping_out_of_fire_leaves_the_circle_and_never_heads_toward_the_enemy()
        {
            var random = new Random(17);
            for (int i = 0; i < 20_000; i++)
            {
                Vector2 center = new((float)random.Next(-500, 500), (float)random.Next(-500, 500));
                int radius = random.Next(150, 500);
                double a = random.NextDouble() * Math.PI * 2;
                float r = (float)(random.NextDouble() * radius);
                Vector2 self = center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
                Vector2 enemy = new((float)random.Next(-3_000, 3_000), (float)random.Next(-3_000, 3_000));
                Vector2 spot = BotSkillMistakes.FireStepSpot(self, center, radius, enemy, center);
                Assert.That(Vector2.Distance(spot, center), Is.GreaterThanOrEqualTo(radius + BotSkillMistakes.FireStepMargin - 0.5f), "out of the circle");
                Assert.That(Vector2.Distance(spot, enemy), Is.GreaterThanOrEqualTo(Vector2.Distance(self, enemy) - 0.5f), "never toward the enemy");
            }
            // An area centred on a groupmate west of the bot, the enemy east: the bot steps out west, behind its mate.
            Vector2 west = BotSkillMistakes.FireStepSpot(new(100, 0), new(0, 0), 300, new(2_000, 0), new(0, 0));
            Assert.That(west.X, Is.LessThan(0));
            // No enemy known: straight out from the centre.
            Assert.That(BotSkillMistakes.FireStepSpot(new(100, 0), new(0, 0), 300, null, new(0, 0)).X, Is.EqualTo(375).Within(0.5));
        }

        [Test]
        public void A_running_lapse_is_dropped_when_it_is_no_longer_allowed()
        {
            var lapses = new BotBuffLapses<string>();
            bool allowed = true;
            Assert.That(lapses.Waits("tank", 7, 1_000, 0, _ => 60_000L, _ => allowed), Is.True);
            Assert.That(lapses.Waits("tank", 7, 3_000, 0, _ => 60_000L, _ => allowed), Is.True);
            allowed = false;
            Assert.That(lapses.Waits("tank", 7, 5_000, 0, _ => 60_000L, _ => allowed), Is.False, "renewed now");
            allowed = true;
            Assert.That(lapses.Waits("tank", 7, 6_000, 0, _ => 60_000L, _ => allowed), Is.False, "no new roll right after");
            int asked = 0;
            Assert.That(lapses.Waits("healer", 7, 1_000, 0, _ => 0L, _ => { asked++; return false; }), Is.False);
            Assert.That(asked, Is.EqualTo(0), "the re-check runs only while a lapse runs");
        }

        [Test]
        public void No_two_careless_pulls_within_3_minutes()
        {
            Assert.That(BotSkillMistakes.PullCooledDown(1_000, 0), Is.True);
            Assert.That(BotSkillMistakes.PullCooledDown(100_000, 10_000), Is.False);
            Assert.That(BotSkillMistakes.PullCooledDown(190_000, 10_000), Is.True);
        }

        [Test]
        public void A_buff_never_lapses_at_a_boss_and_a_position_mistake_never_against_a_named_or_by_the_tank()
        {
            Assert.That(BotSkillMistakes.BuffLapseAllowed(false, false), Is.True);
            Assert.That(BotSkillMistakes.BuffLapseAllowed(true, false), Is.False, "dungeons: where the bosses are");
            Assert.That(BotSkillMistakes.BuffLapseAllowed(false, true), Is.False, "a named target");
            Assert.That(BotSkillMistakes.PositionMistakeAllowed(false, false), Is.True);
            Assert.That(BotSkillMistakes.PositionMistakeAllowed(true, false), Is.False);
            Assert.That(BotSkillMistakes.PositionMistakeAllowed(false, true), Is.False);
        }

        [Test]
        public void A_caster_keeps_its_reserve_and_a_solo_caster_rests_no_lower_than_35()
        {
            Assert.That(BotSkillMistakes.ExpensiveCastAllowed(500, 1000, 300, solo: false), Is.True);
            Assert.That(BotSkillMistakes.ExpensiveCastAllowed(150, 1000, 100, solo: false), Is.False, "under 10% left in a group");
            Assert.That(BotSkillMistakes.ExpensiveCastAllowed(500, 1000, 100, solo: true), Is.True);
            Assert.That(BotSkillMistakes.ExpensiveCastAllowed(450, 1000, 100, solo: true), Is.False, "under 40% left alone");
            Assert.That(BotSkillMistakes.ExpensiveCastAllowed(500, 0, 100, solo: false), Is.False);
            foreach (BotSkillProfile p in Profiles(2_000))
            {
                int solo = BotSkillMistakes.PowerRestFloor(p, false, true, soloCaster: true);
                Assert.That(solo, Is.InRange(35, 45));
                Assert.That(BotSkillMistakes.PowerRestFloor(p, false, true, false), Is.InRange(20, 45), "never above today's floor");
            }
            Assert.That(BotSkillMistakes.MostExpensive([10, 30, 20, 30], 15), Is.EqualTo(1));
            Assert.That(BotSkillMistakes.MostExpensive([10, 30], 30), Is.EqualTo(-1), "the pick is already the dearest");
        }

        [Test]
        public void A_badly_placed_bot_ends_nearer_the_monsters_face()
        {
            var random = new Random(5);
            for (int i = 0; i < 5_000; i++)
            {
                float angle = (float)(random.NextDouble() * 360), sweep = (float)(10 + random.NextDouble() * 50);
                int direction = BotSkillMistakes.TowardFront(angle, sweep);
                float gap = BotSkillMistakes.FrontGap(angle + direction * sweep);
                Assert.That(gap, Is.LessThanOrEqualTo(BotSkillMistakes.FrontGap(angle - direction * sweep) + 1e-3));
            }
            Assert.That(BotSkillMistakes.TowardFront(90, 30), Is.EqualTo(-1));
            Assert.That(BotSkillMistakes.TowardFront(270, 30), Is.EqualTo(1));
            Assert.That(BotSkillMistakes.TowardFront(90, 0), Is.EqualTo(0));
        }

        [Test]
        public void A_late_reaction_is_past_the_slowest_an_average_person_takes()
        {
            var random = new Random(9);
            int casualLate = 0, veteranLate = 0, averageLate = 0;
            for (int i = 0; i < 20_000; i++)
            {
                double roll = random.NextDouble();
                casualLate += BotIncomingFireReaction.IsLate(BotIncomingFireKind.Melee, 1, 1, 1.3, roll) ? 1 : 0;
                averageLate += BotIncomingFireReaction.IsLate(BotIncomingFireKind.Melee, 1, 1, 1.0, roll) ? 1 : 0;
                veteranLate += BotIncomingFireReaction.IsLate(BotIncomingFireKind.Melee, 1, 1, 0.75, roll) ? 1 : 0;
            }
            Assert.That(casualLate / 20_000.0, Is.EqualTo(0.37).Within(0.03));
            Assert.That(averageLate, Is.EqualTo(0));
            Assert.That(veteranLate, Is.EqualTo(0));
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, 1.15 * 1.45, 1.3, 0.999),
                Is.LessThanOrEqualTo(BotIncomingFireReaction.MaxHoldMilliseconds), "the hold stays capped");
        }

        // ---- buffs: one decision per expiry ----------------------------------------------------------------------------

        [Test]
        public void A_buff_lapse_is_decided_once_per_expiry_and_held_for_its_length()
        {
            var lapses = new BotBuffLapses<string>();
            int asked = 0;
            Func<long, long> decide = lapse => { asked++; return lapse; };
            Assert.That(lapses.Waits("tank", 7, 1_000, 20_000L, decide), Is.True);
            Assert.That(lapses.Waits("tank", 7, 2_000, 0L, decide), Is.True, "still lapsed; not asked again");
            Assert.That(lapses.Waits("tank", 7, 20_999, 0L, decide), Is.True);
            Assert.That(lapses.Waits("tank", 7, 21_000, 0L, decide), Is.False, "the lapse ends: renewed");
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(lapses.Waits("tank", 7, 40_000, 5_000L, decide), Is.False, "within the memory: renewed, no new roll");
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(lapses.Waits("tank", 7, 52_000, 5_000L, decide), Is.True, "the next expiry rolls again");
            Assert.That(asked, Is.EqualTo(2));
            Assert.That(lapses.Waits("healer", 7, 52_000, 0L, decide), Is.False, "another target: its own decision");
            Assert.That(lapses.Waits("tank", 8, 52_000, 0L, decide), Is.False, "another spell: its own decision");
            Assert.That(asked, Is.EqualTo(4));
            Assert.That(lapses.Waits(null, 7, 1, 1L, decide), Is.False);
        }

        [Test]
        public void The_buff_memory_stays_small()
        {
            var lapses = new BotBuffLapses<string>();
            for (int i = 0; i < 1_000; i++)
                lapses.Waits("t" + i, i, i * 10_000L, 0L, static _ => 0);
            Assert.That(lapses.Count, Is.LessThanOrEqualTo(BotBuffLapses<string>.MaxKept));
        }

        // ---- RvR groups keep their group tier (owner decision 5) ---------------------------------------------------------

        [Test]
        public void In_an_RvR_group_a_members_facet_only_spreads_its_delay_inside_the_group_tiers_band()
        {
            var profiles = Profiles(3_000).ToArray();
            var random = new Random(13);
            foreach (RvrSkillTier group in Enum.GetValues<RvrSkillTier>())
            {
                var (fireMin, fireMax) = BotSkillMistakes.FireBand(group);
                var (placeMin, placeMax) = RvrPositioning.ReactionBand(group);
                double low = BotSkillMistakes.ReactionFactor(group, 1), high = BotSkillMistakes.ReactionFactor(group, 0);
                double fireSum = 0, placeSum = 0;
                foreach (BotSkillProfile member in profiles)
                {
                    double roll = random.NextDouble();
                    long fire = BotSkillMistakes.FireDelayMilliseconds(group, member.Position(BotSkillFacet.Fire), roll);
                    Assert.That(fire, Is.InRange(fireMin, fireMax), $"fire, group {group}, member {member.Tier}");
                    long place = BotSkillMistakes.SpreadInBand(placeMin, placeMax, roll, member.Position(BotSkillFacet.Positioning));
                    Assert.That(place, Is.InRange(placeMin, placeMax), $"place, group {group}, member {member.Tier}");
                    double factor = BotSkillMistakes.ReactionFactor(group, member.Position(BotSkillFacet.Reactions));
                    Assert.That(factor, Is.InRange(Math.Min(low, high), Math.Max(low, high)), $"reactions, group {group}");
                    fireSum += fire;
                    placeSum += place;
                }
                // The RvR field's measures stay readable: the group's mean stays near its band's middle.
                Assert.That(fireSum / profiles.Length, Is.EqualTo((fireMin + fireMax) / 2.0).Within((fireMax - fireMin) * 0.12));
                Assert.That(placeSum / profiles.Length, Is.EqualTo((placeMin + placeMax) / 2.0).Within((placeMax - placeMin) * 0.12));
            }
            Assert.That(BotSkillMistakes.ReactionFactor(RvrSkillTier.Veteran, 0), Is.LessThan(1.0));
            Assert.That(BotSkillMistakes.ReactionFactor(RvrSkillTier.Casual, 1), Is.GreaterThan(1.0));
        }

        [Test]
        public void A_better_member_steps_out_sooner_on_average_inside_the_band()
        {
            double Mean(double position) =>
                Enumerable.Range(0, 1_000).Average(i => BotSkillMistakes.FireDelayMilliseconds(RvrSkillTier.Average, position, i / 1_000.0));
            Assert.That(Mean(0.9), Is.LessThan(Mean(0.5)));
            Assert.That(Mean(0.5), Is.LessThan(Mean(0.1)));
            Assert.That(BotSkillMistakes.SpreadInBand(1_000, 3_000, 0, 1), Is.EqualTo(1_000));
            Assert.That(BotSkillMistakes.SpreadInBand(1_000, 3_000, 1, 0), Is.EqualTo(3_000));
            Assert.That(BotSkillMistakes.SpreadInBand(1_000, 3_000, double.NaN, double.NaN), Is.EqualTo(2_000));
        }

        [Test]
        public void The_rvr_place_band_is_the_one_RvrPositioning_draws_from()
        {
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                var (min, max) = RvrPositioning.ReactionBand(tier);
                for (long now = 0; now < 200_000; now += 997)
                    Assert.That(RvrPositioning.ReactionMilliseconds(tier, 42, now), Is.InRange(min, max));
            }
        }

        // ---- the scope: no hook acts in a raid ---------------------------------------------------------------------------

        [Test]
        public void No_mistake_in_a_raid_a_battleground_assault_at_a_keep_or_for_a_companion()
        {
            var free = BotPeopleFacts.FreeGamebot;
            Assert.That(BotPeopleScope.Refusal(free, BotPeopleRule.Mistakes), Is.EqualTo(BotPeopleRefusal.None));
            var fenced = new (BotPeopleFacts Facts, BotPeopleRefusal Expected)[]
            {
                (free with { Rvr = free.Rvr with { OnRealmRaid = true } }, BotPeopleRefusal.RealmRaid),
                (free with { Rvr = free.Rvr with { Duty = eDutyLeaseKind.RealmRaid } }, BotPeopleRefusal.RealmRaid),
                (free with { InRaidDungeon = true }, BotPeopleRefusal.DungeonRaid),
                (free with { Rvr = free.Rvr with { BattlegroundAssault = true } }, BotPeopleRefusal.BattlegroundAssault),
                (free with { Rvr = free.Rvr with { AtObjective = true } }, BotPeopleRefusal.AtObjective),
                (free with { Rvr = free.Rvr with { SiegeOperator = true } }, BotPeopleRefusal.SiegeOperator),
                (free with { Rvr = free.Rvr with { RelicCarrier = true } }, BotPeopleRefusal.RelicCarrier),
                (free with { StableRide = true }, BotPeopleRefusal.StableRide),
                (free with { Rvr = free.Rvr with { Companion = true } }, BotPeopleRefusal.NotAGamebot),
                (free with { InMemoryOnly = true }, BotPeopleRefusal.InMemoryOnly),
            };
            foreach (var (facts, expected) in fenced)
                Assert.That(BotPeopleScope.Refusal(facts, BotPeopleRule.Mistakes), Is.EqualTo(expected), expected.ToString());
            // A raid with a real player in it is still a raid.
            Assert.That(BotPeopleScope.Refusal(free with { Rvr = free.Rvr with { OnRealmRaid = true, PlayerInGroup = true } },
                BotPeopleRule.Mistakes), Is.EqualTo(BotPeopleRefusal.RealmRaid));
        }

        [Test]
        public void The_players_group_is_spared_not_refused_and_its_companions_never_get_mistakes()
        {
            var free = BotPeopleFacts.FreeGamebot;
            var withPlayer = free with { Rvr = free.Rvr with { PlayerInGroup = true } };
            Assert.That(BotPeopleScope.Refusal(withPlayer, BotPeopleRule.Mistakes), Is.EqualTo(BotPeopleRefusal.None));
            Assert.That(withPlayer.InPlayerGroup, Is.True);
            Assert.That(BotSkillMistakes.Spared(withPlayer.InPlayerGroup, true), Is.True);
            Assert.That(BotSkillMistakes.Spared(free.InPlayerGroup, true), Is.False);
            var playerLed = free with { Rvr = free.Rvr with { PlayerLedGroup = true } };
            Assert.That(BotPeopleScope.Refusal(playerLed, BotPeopleRule.Mistakes), Is.EqualTo(BotPeopleRefusal.None));
            Assert.That(playerLed.InPlayerGroup, Is.True, "a player-led group is the player's group: spared");
            var companion = withPlayer with { Rvr = withPlayer.Rvr with { Companion = true } };
            Assert.That(BotPeopleScope.Refusal(companion, BotPeopleRule.Mistakes), Is.EqualTo(BotPeopleRefusal.NotAGamebot));
            Assert.That(BotPeopleScope.Refusal(free with { Rvr = free.Rvr with { AutonomousGamebot = false } }, BotPeopleRule.Mistakes),
                Is.EqualTo(BotPeopleRefusal.NotAGamebot), "real players and /spawn companions");
        }

        // ---- the log -----------------------------------------------------------------------------------------------------

        [Test]
        public void The_mistake_line_and_the_summary()
        {
            string line = BotSkillMistakes.MistakeLine(BotSkillMistakeKind.Pull, true, "Aldric", BotPersonKey.Of(12), eRealm.Albion,
                RvrSkillTier.Casual, 0.025, "monster=\"a bog creeper\"");
            Assert.That(line, Is.EqualTo("BOT_SKILL_MISTAKE kind=pull spared=true name=Aldric person=c:12 realm=alb tier=casual chance=0.025 monster=\"a bog creeper\""));

            var mistakes = new long[BotSkillMistakes.CountLength];
            var decisions = new long[BotSkillMistakes.CountLength];
            mistakes[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Pull, RvrSkillTier.Casual, false)] = 4;
            decisions[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Pull, RvrSkillTier.Casual, false)] = 40;
            mistakes[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Pull, RvrSkillTier.Casual, true)] = 1;
            decisions[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Pull, RvrSkillTier.Casual, true)] = 40;
            mistakes[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Reaction, RvrSkillTier.Veteran, false)] = 9;
            decisions[BotSkillMistakes.CountIndex(BotSkillMistakeKind.Reaction, RvrSkillTier.Veteran, false)] = 10;
            string summary = BotSkillMistakes.SummaryLine(300, mistakes, decisions, 5, 2);
            Assert.That(summary, Does.StartWith("BOT_SKILL_MISTAKES seconds=300 out.reaction=c0/0,a0/0,v9/10 in.reaction=c0/0,a0/0,v0/0"));
            Assert.That(summary, Does.Contain(" out.pull=c4/40,a0/0,v0/0 in.pull=c1/40,a0/0,v0/0 "));
            Assert.That(summary, Does.EndWith(" rate_out=0.100 rate_in=0.025 lines=5 suppressed=2"), "reactions are delays: not in the rates");
            Assert.That(BotSkillMistakes.SummaryLine(300, null, null, 0, 0), Does.Contain("rate_out=- rate_in=-"));
            Assert.That(BotSkillMistakes.CountLength, Is.EqualTo(Enum.GetValues<BotSkillMistakeKind>().Length * 6));
        }
    }
}
