using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 3 (docs/RVR-FIELD-1.2.md "Hands"): the protect plan and the peel trigger
    /// (RvrPeelPlan). Pure: board records with world-less gamebots and players as the living objects (DutyLeaseTestKit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrPeelPlan : DutyLeaseFixture
    {
        private static long _key = 9_000;

        internal static RvrCombatant C(GameLiving living, eCharacterClass characterClass, int x, int y = 0,
            eRealm realm = eRealm.Albion, GameLiving target = null, bool fighting = false,
            RvrControlState control = RvrControlState.None, bool pet = false, int health = 100) => new()
        {
            Living = living, Key = ++_key, Realm = realm, Class = characterClass,
            Role = pet ? RvrRole.Pet : RvrRoles.For(characterClass), IsPet = pet, IsPlayer = living is GamePlayer,
            Level = 50, HealthPercent = health, X = x, Y = y, Target = target, TargetRealm = target?.Realm ?? eRealm.None,
            Fighting = fighting, Control = control,
        };

        internal static RvrFightBoard Board(IEnumerable<RvrCombatant> members, IEnumerable<RvrCombatant> enemies) =>
            RvrFightBoard.Build(1_000, 163, eRealm.Albion, members, enemies);

        private static RvrTacticsSwitches PeelOn => RvrTacticsSwitches.AllOn;

        // ---- the trigger --------------------------------------------------------------------------------------------------

        [Test]
        public void Peel_triggers_on_player_and_bot_attackers_and_not_on_monsters_attacking_someone_else()
        {
            Bot healer = Gamebot();
            Bot tank = Gamebot();
            Player player = RealPlayer(eRealm.Midgard);
            Bot enemyBot = Gamebot(realm: eRealm.Hibernia, objective: "RvR");
            Bot monster = Gamebot(realm: eRealm.None);

            var byPlayer = ImmutableArray.Create(C(player, eCharacterClass.Berserker, 150, realm: eRealm.Midgard, target: healer, fighting: true));
            var byBot = ImmutableArray.Create(C(enemyBot, eCharacterClass.Blademaster, 0, 180, eRealm.Hibernia, healer, true));
            var monsterOnTank = ImmutableArray.Create(C(monster, eCharacterClass.Unknown, 100, realm: eRealm.None, target: tank, fighting: true));
            Assert.Multiple(() =>
            {
                Assert.That(byPlayer[0].IsPlayer, Is.True);
                Assert.That(RvrPeelPlan.HitInMelee(healer, 0, 0, byPlayer), Is.True, "an enemy player hitting the healer");
                Assert.That(RvrPeelPlan.HitInMelee(healer, 0, 0, byBot), Is.True, "an enemy bot hitting the healer");
                Assert.That(RvrPeelPlan.HitInMelee(healer, 0, 0, monsterOnTank), Is.False, "a monster hitting someone else");
                Assert.That(RvrPeelPlan.HitInMelee(healer, 900, 0, byPlayer), Is.False, "out of melee reach");
                Assert.That(RvrPeelPlan.HitInMelee(healer, 0, 0,
                    ImmutableArray.Create(byPlayer[0] with { Control = RvrControlState.Mezzed })), Is.False, "a mezzed attacker swings at nobody");
                Assert.That(RvrPeelPlan.HitInMelee(healer, 0, 0,
                    ImmutableArray.Create(byPlayer[0] with { Fighting = false })), Is.False, "only looking at it");
                Assert.That(RvrPeelPlan.Threatened(healer, 0, 0, monsterOnTank, new HashSet<GameLiving> { healer }), Is.True,
                    "the group's fight board (attacker lists) says it is hit in melee");
                Assert.That(RvrPeelPlan.Threatened(healer, 0, 0, monsterOnTank, new HashSet<GameLiving> { tank }), Is.False);
                Assert.That(RvrPeelPlan.Threatened(healer, 0, 0, byBot, null), Is.True);
            });
        }

        [Test]
        public void The_attacker_peeled_is_the_one_on_the_charge_never_a_mezzed_one()
        {
            Bot healer = Gamebot();
            Bot far = Gamebot(realm: eRealm.Midgard), near = Gamebot(realm: eRealm.Midgard), mezzed = Gamebot(realm: eRealm.Midgard);
            Bot passer = Gamebot(realm: eRealm.Midgard), elsewhere = Gamebot(realm: eRealm.Midgard);
            RvrCombatant charge = C(healer, eCharacterClass.Cleric, 0);
            var enemies = ImmutableArray.Create(
                C(far, eCharacterClass.Runemaster, 1500, realm: eRealm.Midgard, target: healer, fighting: true),
                C(mezzed, eCharacterClass.Savage, 50, realm: eRealm.Midgard, target: healer, fighting: true, control: RvrControlState.Mezzed),
                C(near, eCharacterClass.Savage, 200, realm: eRealm.Midgard, target: healer, fighting: true),
                C(elsewhere, eCharacterClass.Warrior, 2000, realm: eRealm.Midgard));
            Assert.That(RvrPeelPlan.AttackerOf(charge, enemies)?.Living, Is.SameAs(near), "in melee on the charge first");
            Assert.That(RvrPeelPlan.AttackerOf(charge, enemies.RemoveAt(2))?.Living, Is.SameAs(far), "then anyone targeting it");
            var closeMelee = ImmutableArray.Create(C(passer, eCharacterClass.Berserker, 250, realm: eRealm.Midgard));
            Assert.That(RvrPeelPlan.AttackerOf(charge, closeMelee)?.Living, Is.SameAs(passer), "slam meleers that get too close");
            Assert.That(RvrPeelPlan.AttackerOf(charge, ImmutableArray.Create(enemies[1], enemies[3])), Is.Null);
        }

        // ---- assignments --------------------------------------------------------------------------------------------------

        [Test]
        public void One_protector_per_healer_where_possible_then_casters()
        {
            Bot t1 = Gamebot(), t2 = Gamebot(), t3 = Gamebot(), h1 = Gamebot(), h2 = Gamebot(), caster = Gamebot(), merc = Gamebot();
            RvrCombatant T1 = C(t1, eCharacterClass.Armsman, 0), T2 = C(t2, eCharacterClass.Paladin, 1000),
                T3 = C(t3, eCharacterClass.Armsman, 500, 300), H1 = C(h1, eCharacterClass.Cleric, 50), H2 = C(h2, eCharacterClass.Cleric, 950),
                Cs = C(caster, eCharacterClass.Wizard, 500), M = C(merc, eCharacterClass.Mercenary, 500, -300);

            ImmutableArray<RvrAssignment> two = RvrPeelPlan.Assign(ImmutableArray.Create(T1, T2, H1, H2, Cs, M), default, 5);
            ImmutableArray<RvrAssignment> three = RvrPeelPlan.Assign(ImmutableArray.Create(T1, T2, T3, H1, H2, Cs, M), default, 5);
            ImmutableArray<RvrAssignment> one = RvrPeelPlan.Assign(ImmutableArray.Create(T2, H1, H2, Cs), default, 5);
            Assert.Multiple(() =>
            {
                Assert.That(two.Select(a => (a.Member, a.Subject)), Is.EquivalentTo(new[] { ((GameLiving)t1, (GameLiving)h1), (t2, h2) }),
                    "each healer its nearest tank; no tank left for the caster");
                Assert.That(three, Has.Length.EqualTo(3));
                Assert.That(three.Single(a => a.Subject == caster).Member, Is.SameAs(t3), "a third tank guards the caster");
                Assert.That(one, Has.Length.EqualTo(1));
                Assert.That(one[0].Subject, Is.SameAs(h1), "one tank, two healers: the first healer");
                Assert.That(two.All(a => a.Since == 5));
                foreach (ImmutableArray<RvrAssignment> set in new[] { two, three, one })
                {
                    Assert.That(set.Select(a => a.Member).Distinct().Count(), Is.EqualTo(set.Length), "a tank guards one member");
                    Assert.That(set.Select(a => a.Subject).Distinct().Count(), Is.EqualTo(set.Length), "one protector per member");
                }
                Assert.That(three.Any(a => a.Subject == merc || a.Member == merc), Is.False, "melee damage neither guards nor is guarded");
                Assert.That(RvrPeelPlan.Assign(ImmutableArray.Create(H1, Cs), default, 5), Is.Empty, "no tank");
                Assert.That(RvrPeelPlan.Assign(ImmutableArray.Create(T1, M), default, 5), Is.Empty, "nobody to guard");
            });
        }

        [Test]
        public void Assignments_are_kept_and_an_unguarded_healer_takes_a_casters_tank()
        {
            Bot t1 = Gamebot(), t2 = Gamebot(), h1 = Gamebot(), caster = Gamebot();
            RvrCombatant T1 = C(t1, eCharacterClass.Armsman, 0), T2 = C(t2, eCharacterClass.Armsman, 2000),
                H1 = C(h1, eCharacterClass.Cleric, 10), Cs = C(caster, eCharacterClass.Wizard, 1990);
            var kept = RvrPeelPlan.Assign(ImmutableArray.Create(T1, T2, H1, Cs),
                ImmutableArray.Create(new RvrAssignment(t2, h1, 1), new RvrAssignment(t1, caster, 1)), 9);
            var upgraded = RvrPeelPlan.Assign(ImmutableArray.Create(T1, H1, Cs), ImmutableArray.Create(new RvrAssignment(t1, caster, 1)), 9);
            var gone = RvrPeelPlan.Assign(ImmutableArray.Create(T1, Cs), ImmutableArray.Create(new RvrAssignment(t1, h1, 1)), 9);
            Assert.Multiple(() =>
            {
                Assert.That(kept.Single(a => a.Subject == h1).Member, Is.SameAs(t2), "a valid assignment is kept, even if another tank is nearer");
                Assert.That(kept.Single(a => a.Subject == h1).Since, Is.EqualTo(1), "and keeps its start");
                Assert.That(upgraded.Single().Subject, Is.SameAs(h1), "the healer outranks the caster");
                Assert.That(upgraded.Single().Since, Is.EqualTo(9));
                Assert.That(gone.Single().Subject, Is.SameAs(caster), "a charge that left the board frees its tank");
            });
        }

        [Test]
        public void The_plan_step_fills_protect_only_with_the_switch_on_and_enemies_in_view()
        {
            Bot t1 = Gamebot(), h1 = Gamebot(), enemy = Gamebot(realm: eRealm.Midgard);
            var members = new[] { C(t1, eCharacterClass.Armsman, 0), C(h1, eCharacterClass.Cleric, 100) };
            var foes = new[] { C(enemy, eCharacterClass.Savage, 2000, realm: eRealm.Midgard) };
            RvrFightPlan neutral = RvrFightPlan.Neutral(1_000, RvrGroupState.Empty);
            RvrFightPlan planned = RvrPeelPlan.Protect(neutral, Board(members, foes), PeelOn, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(planned.Protect.Single().Member, Is.SameAs(t1));
                Assert.That(planned.IsNeutral, Is.False);
                Assert.That(RvrPeelPlan.Protect(neutral, Board(members, foes), PeelOn with { Peel = false }, 1_000),
                    Is.SameAs(neutral), "rvr_bot_peel off: unchanged");
                Assert.That(RvrPeelPlan.Protect(neutral, Board(members, []), PeelOn, 1_000), Is.SameAs(neutral), "no enemy in view");
                Assert.That(RvrPeelPlan.Protect(planned, Board(members, []), PeelOn, 2_000).Protect, Is.Empty, "the enemy left: cleared");
                Assert.That(RvrFightPlanner.PlanProtect(neutral, Board(members, foes), RvrGroupState.Empty, PeelOn, 1_000).Protect,
                    Has.Length.EqualTo(1), "the planner's step is this rule");
            });
        }

        [Test]
        public void Protection_in_an_open_field_fight_is_limited_to_the_assignments()
        {
            Bot t1 = Gamebot(), t2 = Gamebot(), t3 = Gamebot(), h1 = Gamebot(), h2 = Gamebot(), merc = Gamebot();
            Bot e1 = Gamebot(realm: eRealm.Midgard), e2 = Gamebot(realm: eRealm.Midgard), e3 = Gamebot(realm: eRealm.Midgard);
            RvrCombatant[] members =
            [
                C(t1, eCharacterClass.Armsman, 0), C(t2, eCharacterClass.Paladin, 1000), C(t3, eCharacterClass.Armsman, 500, 800),
                C(h1, eCharacterClass.Cleric, 50), C(h2, eCharacterClass.Cleric, 950), C(merc, eCharacterClass.Mercenary, 500, 900),
            ];
            RvrCombatant[] enemies =
            [
                C(e1, eCharacterClass.Savage, 150, realm: eRealm.Midgard, target: h1, fighting: true),
                C(e2, eCharacterClass.Berserker, 1100, realm: eRealm.Midgard, target: h2, fighting: true),
                C(e3, eCharacterClass.Warrior, 500, 1000, eRealm.Midgard, merc, true),
            ];
            RvrFightBoard board = Board(members, enemies);
            RvrFightPlan plan = RvrPeelPlan.Protect(RvrFightPlan.Neutral(1_000, RvrGroupState.Empty), board, PeelOn, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(RvrPeelPlan.ChargeOf(plan, t1), Is.SameAs(h1));
                Assert.That(RvrPeelPlan.GuardOf(plan, h2), Is.SameAs(t2));
                Assert.That(RvrPeelPlan.ProtectionTargetFor(plan, board, t1), Is.SameAs(e1), "the attacker on its own charge");
                Assert.That(RvrPeelPlan.ProtectionTargetFor(plan, board, t2), Is.SameAs(e2), "a different attacker: not all on one");
                Assert.That(RvrPeelPlan.ChargeOf(plan, t3), Is.Null, "two healers, no caster: the third tank has no charge");
                Assert.That(RvrPeelPlan.ProtectionTargetFor(plan, board, t3), Is.Null,
                    "no charge, no protection target, though an enemy hits the mercenary");
                Assert.That(RvrPeelPlan.ProtectionTargetFor(plan, board, merc), Is.Null);
            });
        }

        // ---- the tier's error ---------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 0.35)]
        [TestCase(RvrSkillTier.Average, 0.15)]
        [TestCase(RvrSkillTier.Veteran, 0.05)]
        public void Tanks_over_extend_at_the_tiers_share(RvrSkillTier tier, double share)
        {
            const int Tanks = 20_000;
            int over = 0;
            for (long id = 1; id <= Tanks; id++)
                if (RvrPeelPlan.OverExtends(tier, 0.5, 7_000_000 + id, 123_456))
                    over++;
            Assert.Multiple(() =>
            {
                Assert.That(RvrPeelPlan.OverExtendShare(tier, 0.5), Is.EqualTo(share).Within(1e-9));
                Assert.That(over / (double)Tanks, Is.EqualTo(share).Within(0.015), "the share over 20,000 tanks");
                Assert.That(RvrPeelPlan.OverExtendShare(tier, 1.0), Is.GreaterThan(share), "a daredevil over-extends more");
                Assert.That(RvrPeelPlan.OverExtendShare(tier, 0.0), Is.LessThan(share));
                Assert.That(RvrPeelPlan.OverExtends(tier, 0.5, 7_000_001, 123_456),
                    Is.EqualTo(RvrPeelPlan.OverExtends(tier, 0.5, 7_000_001, 125_000)), "one roll per 20 s window");
            });
        }

        [Test]
        public void A_style_without_a_stun_proc_is_no_slam() => Assert.Multiple(() =>
        {
            Assert.That(RvrPeelPlan.IsStunStyle(null), Is.False);
            Assert.That(RvrPeelPlan.IsMeleeKind(RvrRole.Caster), Is.False);
            Assert.That(RvrPeelPlan.IsMeleeKind(RvrRole.Pet), Is.True);
        });
    }
}
