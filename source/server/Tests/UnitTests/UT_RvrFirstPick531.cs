using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #531 step 1 (RvR work 13, piece D): the plan's first pick (<see cref="RvrFirstPick"/>), the pure rule. The scan's
    /// wiring and the rehearsal row are in UT_RvrFightRehearsal. Unit tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture]
    public class UT_RvrFirstPick531
    {
        private static RvrFirstPickCandidate Enemy(long key, eCharacterClass characterClass, eRealm realm = eRealm.Midgard,
            float distance = 500, bool pet = false, bool engine = false, bool held = false, int matesOn = 0, bool called = false,
            int health = 100, bool fightsGroup = false) =>
            new(new RvrCombatant
            {
                Key = key, Class = characterClass, Realm = realm, IsPet = pet,
                Role = pet ? RvrRole.Pet : RvrRoles.For(characterClass), HealthPercent = health, TargetsGroup = fightsGroup,
            }, engine, held, distance, matesOn, called);

        private static long Pick(RvrSkillTier tier, ulong seed, out RvrFirstPickReason reason, params RvrFirstPickCandidate[] candidates)
        {
            int index = RvrFirstPick.Pick(candidates, tier, seed, out reason);
            return index < 0 ? -1 : candidates[index].Enemy.Key;
        }

        private static long Pick(RvrSkillTier tier, ulong seed, params RvrFirstPickCandidate[] candidates) =>
            Pick(tier, seed, out _, candidates);

        [Test]
        public void Nothing_to_pick_gives_none()
        {
            Assert.That(RvrFirstPick.Pick(Array.Empty<RvrFirstPickCandidate>(), RvrSkillTier.Average, 1, out RvrFirstPickReason reason), Is.EqualTo(-1));
            Assert.That(reason, Is.EqualTo(RvrFirstPickReason.None));
            Assert.That(RvrFirstPick.Pick(null, RvrSkillTier.Average, 1, out _), Is.EqualTo(-1));
        }

        [Test]
        public void Casters_and_healers_come_before_tanks_for_every_group([Values] RvrSkillTier tier)
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                Assert.That(Pick(tier, seed, Enemy(1, eCharacterClass.Warrior), Enemy(2, eCharacterClass.Runemaster)), Is.EqualTo(2), $"seed {seed}");
                Assert.That(Pick(tier, seed, Enemy(1, eCharacterClass.Warrior), Enemy(3, eCharacterClass.Healer)), Is.EqualTo(3), $"seed {seed}");
            }
            Pick(tier, 7, out RvrFirstPickReason reason, Enemy(1, eCharacterClass.Warrior), Enemy(2, eCharacterClass.Runemaster));
            Assert.That(reason, Is.EqualTo(RvrFirstPickReason.Order));
        }

        [Test]
        public void The_realm_order_puts_the_Shaman_first_against_Midgard()
        {
            // "mezz the healers, kill the shaman and then kill the tanks": the Shaman (130) above the Healer (85).
            for (ulong seed = 1; seed <= 300; seed++)
                Assert.That(Pick(RvrSkillTier.Veteran, seed, Enemy(1, eCharacterClass.Healer), Enemy(2, eCharacterClass.Shaman)), Is.EqualTo(2));
        }

        [Test]
        public void The_tier_wobble_sometimes_turns_a_close_pair_round()
        {
            // a caster (100) and a healer (90) at the same range: the caller's wobble (about 20%) makes either the call
            var picks = new HashSet<long>();
            for (ulong seed = 1; seed <= 500; seed++)
                picks.Add(Pick(RvrSkillTier.Average, seed, Enemy(1, eCharacterClass.Runemaster), Enemy(2, eCharacterClass.Healer)));
            Assert.That(picks, Is.EquivalentTo(new long[] { 1, 2 }));
        }

        [Test]
        public void A_nearer_enemy_of_the_same_kind_wins()
        {
            for (ulong seed = 1; seed <= 300; seed++)
                Assert.That(Pick(RvrSkillTier.Veteran, seed, Enemy(1, eCharacterClass.Runemaster, distance: 2_000),
                    Enemy(2, eCharacterClass.Runemaster, distance: 300)), Is.EqualTo(2));
        }

        [Test]
        public void Within_the_call_radius_comes_before_beyond_it()
        {
            Assert.That(Pick(RvrSkillTier.Average, 3, Enemy(1, eCharacterClass.Runemaster, distance: RvrFirstPick.CallRadius + 100),
                Enemy(2, eCharacterClass.Warrior, distance: 400)), Is.EqualTo(2));
            // beyond it is still picked when nothing is nearer
            Assert.That(Pick(RvrSkillTier.Average, 3, Enemy(1, eCharacterClass.Runemaster, distance: RvrFirstPick.CallRadius + 100)), Is.EqualTo(1));
        }

        [Test]
        public void Free_enemies_before_crowd_controlled_ones_and_held_ones_when_none_is_free()
        {
            // #600: a mezzed healer waits while the free tank is in the fight
            Assert.That(Pick(RvrSkillTier.Average, 5, Enemy(1, eCharacterClass.Healer, held: true), Enemy(2, eCharacterClass.Warrior)), Is.EqualTo(2));
            Assert.That(Pick(RvrSkillTier.Average, 5, Enemy(1, eCharacterClass.Healer, held: true), Enemy(2, eCharacterClass.Warrior, held: true)), Is.EqualTo(1));
        }

        [Test]
        public void People_before_pets_before_siege_engines()
        {
            // #617's battleground ranking, now the plan's: an engine only when no person or pet is proposed
            Assert.That(Pick(RvrSkillTier.Average, 9, Enemy(1, eCharacterClass.Unknown, engine: true, distance: 100),
                Enemy(2, eCharacterClass.Warrior, distance: 3_000)), Is.EqualTo(2));
            Assert.That(Pick(RvrSkillTier.Average, 9, Enemy(1, eCharacterClass.Unknown, pet: true, distance: 100),
                Enemy(2, eCharacterClass.Warrior, distance: 1_500)), Is.EqualTo(2));
            Assert.That(Pick(RvrSkillTier.Average, 9, Enemy(1, eCharacterClass.Unknown, engine: true),
                Enemy(2, eCharacterClass.Unknown, pet: true)), Is.EqualTo(2));
            Assert.That(Pick(RvrSkillTier.Average, 9, Enemy(1, eCharacterClass.Unknown, engine: true)), Is.EqualTo(1));
        }

        [Test]
        public void The_call_wins_then_the_target_mates_already_fight()
        {
            Assert.That(Pick(RvrSkillTier.Average, 11, out RvrFirstPickReason reason, Enemy(1, eCharacterClass.Runemaster),
                Enemy(2, eCharacterClass.Warrior, matesOn: 3), Enemy(3, eCharacterClass.Savage, called: true)), Is.EqualTo(3));
            Assert.That(reason, Is.EqualTo(RvrFirstPickReason.Called));
            Assert.That(Pick(RvrSkillTier.Average, 11, out reason, Enemy(1, eCharacterClass.Runemaster),
                Enemy(2, eCharacterClass.Warrior, matesOn: 1), Enemy(3, eCharacterClass.Savage, matesOn: 2)), Is.EqualTo(3));
            Assert.That(reason, Is.EqualTo(RvrFirstPickReason.Mates));
            // a call on a mezzed enemy does not pull the bot off a free one (free before held comes first)
            Assert.That(Pick(RvrSkillTier.Average, 11, Enemy(1, eCharacterClass.Runemaster, called: true, held: true),
                Enemy(2, eCharacterClass.Warrior)), Is.EqualTo(2));
        }

        [Test]
        public void Every_member_of_a_group_opens_on_the_same_enemy_whatever_order_it_saw_them_in()
        {
            RvrFirstPickCandidate[] eight =
            [
                Enemy(11, eCharacterClass.Warrior, distance: 600), Enemy(12, eCharacterClass.Savage, distance: 650),
                Enemy(13, eCharacterClass.Healer, distance: 1_100), Enemy(14, eCharacterClass.Healer, distance: 1_150),
                Enemy(15, eCharacterClass.Runemaster, distance: 1_200), Enemy(16, eCharacterClass.Spiritmaster, distance: 1_250),
                Enemy(17, eCharacterClass.Skald, distance: 900), Enemy(18, eCharacterClass.Shaman, distance: 1_000),
            ];
            var roles = new Dictionary<RvrRole, int>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var random = new Random((int)seed);
                long first = Pick(RvrSkillTier.Average, seed, eight);
                for (int member = 0; member < 8; member++)
                    Assert.That(Pick(RvrSkillTier.Average, seed, eight.OrderBy(_ => random.Next()).ToArray()), Is.EqualTo(first), $"seed {seed}");
                RvrRole role = eight.First(c => c.Enemy.Key == first).Enemy.Role;
                roles[role] = roles.GetValueOrDefault(role) + 1;
            }
            // the opener is a caster, a healer or a support caster, never one of the two tanks in front
            Assert.That(roles.Keys, Is.SubsetOf(new[] { RvrRole.Caster, RvrRole.Healer, RvrRole.Support }));
        }

        [Test]
        public void Labels_are_the_decision_row_values() => Assert.Multiple(() =>
        {
            Assert.That(RvrFirstPick.Label(RvrFirstPickReason.Called), Is.EqualTo("called"));
            Assert.That(RvrFirstPick.Label(RvrFirstPickReason.Mates), Is.EqualTo("mates"));
            Assert.That(RvrFirstPick.Label(RvrFirstPickReason.Order), Is.EqualTo("order"));
            Assert.That(RvrFirstPick.Label(RvrFirstPickReason.None), Is.EqualTo("none"));
        });
    }
}
