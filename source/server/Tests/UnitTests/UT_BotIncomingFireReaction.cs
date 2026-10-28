using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// How an autonomous gamebot reacts to a player-shaped attacker (BotIncomingFireReaction, ported from
    /// stefanrows/OfflineDAoC 0.271): the kind of a hit, the delay by kind, class, personality and facing, its cap,
    /// the hold, who it applies to, and when a direct attacker overrides the kill order. Pure rules only.
    /// </summary>
    [TestFixture]
    public class UT_BotIncomingFireReaction
    {
        private static double Role(eCharacterClass characterClass) => BotIncomingFireReaction.RoleFactor(characterClass);

        [Test]
        public void KindOf_reads_the_attack_type_the_area_flag_and_the_bomber_reach()
        {
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, true), Is.EqualTo(BotIncomingFireKind.Area), "an AoE DoT tick from range");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, true, areaFromReach: true), Is.EqualTo(BotIncomingFireKind.Direct),
                "a bomber standing in the group is a personal call");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, false), Is.EqualTo(BotIncomingFireKind.Direct), "a single-target nuke");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Ranged, false), Is.EqualTo(BotIncomingFireKind.Direct), "an arrow");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.MeleeTwoHand, true), Is.EqualTo(BotIncomingFireKind.Melee),
                "a weapon proc with a radius is still a melee swing");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Unknown, false), Is.EqualTo(BotIncomingFireKind.Melee));
        }

        [Test]
        public void An_area_hit_is_from_reach_for_a_bomber_in_its_radius_or_any_caster_in_melee_reach()
        {
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: true, distance: 400, radius: 350, meleeReach: 400), Is.True);
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: true, distance: 460, radius: 350, meleeReach: 400), Is.False);
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: false, distance: 1_400, radius: 350, meleeReach: 400), Is.False);
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: false, distance: 300, radius: 350, meleeReach: 400), Is.True);
        }

        [Test]
        public void Area_hits_are_never_a_personal_call_and_a_mates_area_hit_is_a_splash()
        {
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Area), Is.False);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Direct), Is.True);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Melee), Is.True);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.None), Is.True, "a bot outside the rules keeps the old reaction");
            Assert.That(BotIncomingFireReaction.MateKindOf(true), Is.EqualTo(BotIncomingFireKind.Area));
            Assert.That(BotIncomingFireReaction.MateKindOf(false), Is.EqualTo(BotIncomingFireKind.Mate));
        }

        [Test]
        public void Delays_order_melee_direct_mate_area_stay_between_point_three_and_two_and_a_half_seconds()
        {
            var melee = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Melee);
            var direct = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Direct);
            var mate = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Mate);
            var area = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Area);
            Assert.That(melee.Min, Is.EqualTo(300));
            Assert.That(melee.Min, Is.LessThan(direct.Min));
            Assert.That(direct.Min, Is.LessThan(mate.Min));
            Assert.That(mate.Min, Is.LessThan(area.Min));
            Assert.That(BotIncomingFireReaction.MaxHoldMilliseconds, Is.EqualTo(2_500));
            Assert.That(BotIncomingFireReaction.DelayRange(BotIncomingFireKind.None), Is.EqualTo((0, 0)));
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.None, 1, 1, 0.5), Is.Zero);
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, 1.15, 1.3, 0.99, unseen: true),
                Is.EqualTo(BotIncomingFireReaction.MaxHoldMilliseconds), "the slowest draw is capped");
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, 1, 1, 0.0),
                Is.LessThan(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, 1, 1, 0.99)));
        }

        [Test]
        public void An_unseen_attacker_takes_half_a_second_to_a_second_longer_to_find()
        {
            int seen = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, 1, 1, 0.5);
            int unseen = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, 1, 1, 0.5, unseen: true);
            Assert.That(unseen - seen, Is.InRange(BotIncomingFireReaction.UnseenMinMilliseconds, BotIncomingFireReaction.UnseenMaxMilliseconds + 1));
        }

        [Test]
        public void A_tank_turns_first_a_pure_caster_last_and_a_healer_is_not_slowed()
        {
            Assert.That(Role(eCharacterClass.Armsman), Is.EqualTo(0.8));
            Assert.That(Role(eCharacterClass.Warrior), Is.EqualTo(0.8));
            Assert.That(Role(eCharacterClass.Cabalist), Is.EqualTo(1.15));
            Assert.That(Role(eCharacterClass.Eldritch), Is.EqualTo(1.15));
            Assert.That(Role(eCharacterClass.Cleric), Is.EqualTo(1.0), "a healer heals during the hold anyway");
            Assert.That(Role(eCharacterClass.Mercenary), Is.EqualTo(1.0));
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, Role(eCharacterClass.Armsman), 1, 0.5),
                Is.LessThan(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, Role(eCharacterClass.Cabalist), 1, 0.5)));
        }

        [Test]
        public void Personality_risk_scales_the_delay_and_no_personality_is_neutral()
        {
            Assert.That(BotIncomingFireReaction.PersonalityFactor(0), Is.EqualTo(1.3).Within(1e-9), "a cautious bot is slower");
            Assert.That(BotIncomingFireReaction.PersonalityFactor(9), Is.EqualTo(0.75).Within(1e-9), "a daredevil is quicker");
            Assert.That(BotIncomingFireReaction.PersonalityFactor(-3), Is.EqualTo(BotIncomingFireReaction.PersonalityFactor(0)), "clamped");
            Assert.That(BotIncomingFireReaction.PersonalityFactor(12), Is.EqualTo(BotIncomingFireReaction.PersonalityFactor(9)), "clamped");
            Assert.That(BotIncomingFireReaction.PersonalityFactor(null), Is.EqualTo(1.0), "personalities off: neutral");
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, 1, BotIncomingFireReaction.PersonalityFactor(9), 0.5),
                Is.LessThan(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, 1, BotIncomingFireReaction.PersonalityFactor(1), 0.5)));
            // Daredevil risk band 7-9 against Crafter 0-3 (AutonomousBotPersonality): a Daredevil reacts faster.
            Assert.That(BotIncomingFireReaction.PersonalityFactor(7), Is.LessThan(BotIncomingFireReaction.PersonalityFactor(3)));
        }

        [TestCase(true, true, false, false, false, true, TestName = "Applies: an autonomous gamebot on its own")]
        [TestCase(false, true, false, false, false, false, TestName = "Applies: not when the switch is off")]
        [TestCase(true, false, false, false, false, false, TestName = "Applies: not to a companion bot")]
        [TestCase(true, true, true, false, false, false, TestName = "Applies: not to a bot in a player's group")]
        [TestCase(true, true, false, true, false, false, TestName = "Applies: not to a temporary helper")]
        [TestCase(true, true, false, false, true, false, TestName = "Applies: not to an epic special force escort")]
        public void Applies_only_to_autonomous_gamebots_no_player_leads(bool enabled, bool autonomous, bool playerLed,
            bool helper, bool epic, bool applies) =>
            Assert.That(BotIncomingFireReaction.AppliesTo(enabled, autonomous, playerLed, helper, epic), Is.EqualTo(applies));

        [Test]
        public void The_hold_runs_until_its_end_unless_an_attacker_stands_in_melee_reach()
        {
            Assert.That(BotIncomingFireReaction.Holds(1_000, 2_000, false), Is.True);
            Assert.That(BotIncomingFireReaction.Holds(2_000, 2_000, false), Is.False);
            Assert.That(BotIncomingFireReaction.Holds(1_000, 2_000, true), Is.False);
            Assert.That(BotIncomingFireReaction.Holds(1_000, 0, false), Is.False);
        }

        [Test]
        public void A_direct_attacker_overrides_the_kill_order_only_in_melee_reach_or_without_another_player_target()
        {
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(false, false), Is.True, "a mob is dropped for the ganker");
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(true, true), Is.True, "someone is on the bot");
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(true, false), Is.False);
        }

        [Test]
        public void A_healer_casts_the_group_cure_with_three_dotted_members()
        {
            Assert.That(BotIncomingFireReaction.PrefersGroupCure(2), Is.False);
            Assert.That(BotIncomingFireReaction.PrefersGroupCure(3), Is.True);
        }

        [Test]
        public void A_null_bot_draws_nothing_and_a_mob_is_no_fighter()
        {
            Assert.That(BotIncomingFireReaction.Applies(null), Is.False);
            Assert.That(BotIncomingFireReaction.Draw(null, BotIncomingFireKind.Area, null), Is.Zero);
            Assert.That(BotIncomingFireReaction.KindOf(null, null), Is.EqualTo(BotIncomingFireKind.None));
            Assert.That(BotIncomingFireReaction.MateKindOf(null, null, true), Is.EqualTo(BotIncomingFireKind.None));
            Assert.That(BotIncomingFireReaction.Fighter(null), Is.Null);
            Assert.That(BotIncomingFireReaction.IsAreaEffect(null), Is.False);
            Assert.That(BotIncomingFireReaction.InMeleeReach(null, null), Is.False);
        }
    }
}
