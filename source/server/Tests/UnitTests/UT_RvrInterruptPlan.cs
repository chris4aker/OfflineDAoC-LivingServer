using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using static DOL.UnitTests.RvrFieldTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 2: interrupt duty (our design, not a sourced 2003 rotation) and the scope guard of
    /// every work package 2 hook. Pure, apart from the hooks, which are asked on gamebots without a world. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrInterruptPlan : DutyLeaseFixture
    {
        private static readonly RvrTacticsSwitches On = RvrTacticsSwitches.AllOn;

        private static RvrFightPlan Interrupts(RvrFightPlan plan, RvrFightBoard board, RvrFieldFacts facts, long now = 1_000) =>
            RvrInterruptPlan.Plan(plan, board, On, now, facts);

        [Test]
        public void Each_enemy_caster_that_is_not_the_kill_target_gets_one_interrupter()
        {
            List<RvrCombatant> members =
            [
                Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Scout, -300),
                Head(eRealm.Albion, eCharacterClass.Wizard, -350), Head(eRealm.Albion, eCharacterClass.Theurgist, -400),
                Head(eRealm.Albion, eCharacterClass.Mercenary, 380), Head(eRealm.Albion, eCharacterClass.Cleric, -500),
                Head(eRealm.Albion, eCharacterClass.Cleric, -520), Head(eRealm.Albion, eCharacterClass.Paladin, 100),
            ];
            RvrCombatant armsman = members[0], scout = members[1], wizard = members[2], theurgist = members[3], merc = members[4];
            RvrCombatant druid = Foe(eRealm.Hibernia, eCharacterClass.Druid, 400), eldritch = Foe(eRealm.Hibernia, eCharacterClass.Eldritch, 450);
            RvrCombatant bard = Foe(eRealm.Hibernia, eCharacterClass.Bard, 500), hero = Foe(eRealm.Hibernia, eCharacterClass.Hero, 420);
            RvrCombatant enchanter = Foe(eRealm.Hibernia, eCharacterClass.Enchanter, 900);
            RvrFieldFacts facts = Facts()
                .Member(scout, new RvrMemberFacts { Ranged = true })
                .Member(wizard, new RvrMemberFacts { FastSpell = true })
                .Member(theurgist, new RvrMemberFacts { HasPet = true })
                .Member(members[5], new RvrMemberFacts { InstantSpell = true, SupportOnly = true })
                .Build();
            RvrFightBoard board = Board(1_000, members, [druid, eldritch, bard, hero, enchanter]);
            RvrFightPlan plan = Start(1_000, RvrSkillTier.Average) with { MainAssist = armsman.Living, CalledTarget = bard.Living, CalledAt = 1_000 };
            RvrFightPlan result = Interrupts(plan, board, facts);
            Assert.Multiple(() =>
            {
                Assert.That(result.Interrupts.Select(j => j.Subject), Has.None.SameAs(bard.Living), "not the kill target");
                Assert.That(result.Interrupts.Select(j => j.Subject), Has.None.SameAs(hero.Living), "only casters and healers");
                Assert.That(result.Interrupts.Select(j => j.Subject).Distinct().Count(), Is.EqualTo(result.Interrupts.Length), "one per caster");
                Assert.That(result.Interrupts.Select(j => j.Member).Distinct().Count(), Is.EqualTo(result.Interrupts.Length), "one caster per bot");
                Assert.That(result.Interrupts, Has.Length.EqualTo(3), "druid, eldritch, enchanter");
                Assert.That(RvrFightPlan.SubjectFor(result.Interrupts, scout.Living), Is.SameAs(druid.Living), "the healer first, by the best means (arrows)");
                Assert.That(result.Interrupts.Select(j => j.Member), Has.None.SameAs(armsman.Living), "not the main assist");
                Assert.That(result.Interrupts.Select(j => j.Member), Has.None.SameAs(members[5].Living), "not a support-only healer");
                Assert.That(result.Interrupts.Select(j => j.Member), Has.None.SameAs(members[7].Living), "a melee fighter out of reach");
                Assert.That(RvrFightPlan.SubjectFor(result.Interrupts, theurgist.Living), Is.SameAs(eldritch.Living), "pets before slow spells");
                Assert.That(RvrFightPlan.SubjectFor(result.Interrupts, wizard.Living), Is.SameAs(enchanter.Living));
                Assert.That(RvrFightPlan.SubjectFor(result.Interrupts, merc.Living), Is.Null, "the means ran out before the melee in reach was needed");
            });

            // the jobs stand on the next plan, and a crowd-control job takes a caster off the interrupt list
            RvrFightPlan next = Interrupts(result with { CrowdControl = [new RvrAssignment(members[6].Living, enchanter.Living, 1_000)] }, board, facts, 3_000);
            Assert.Multiple(() =>
            {
                Assert.That(next.Interrupts.Where(j => !ReferenceEquals(j.Subject, enchanter.Living)), Is.EqualTo(result.Interrupts.Where(j => !ReferenceEquals(j.Subject, enchanter.Living))));
                Assert.That(next.Interrupts.Select(j => j.Subject), Has.None.SameAs(enchanter.Living), "held by crowd control");
            });
        }

        [Test]
        public void At_most_half_the_group_interrupts_and_melee_only_in_reach()
        {
            List<RvrCombatant> members = Enumerable.Range(0, 4).Select(i => Head(eRealm.Albion, eCharacterClass.Mercenary, 0, i * 10)).ToList();
            List<RvrCombatant> casters = Enumerable.Range(0, 4).Select(i => Foe(eRealm.Midgard, eCharacterClass.Runemaster, 300, i * 10)).ToList();
            RvrCombatant kill = Foe(eRealm.Midgard, eCharacterClass.Warrior, 300);
            RvrFightPlan plan = Start(1_000, RvrSkillTier.Veteran) with { CalledTarget = kill.Living };
            RvrFightPlan result = Interrupts(plan, Board(1_000, members, casters.Append(kill)), RvrFieldFacts.Empty);
            Assert.That(result.Interrupts, Has.Length.EqualTo(RvrInterruptPlan.Cap(4)));
            Assert.That(RvrInterruptPlan.Cap(8), Is.EqualTo(4));
            Assert.That(RvrInterruptPlan.Means(members[0], default, casters[0]), Is.EqualTo(RvrInterruptMeans.Melee));
            Assert.That(RvrInterruptPlan.Means(members[0], default, casters[0] with { X = 300 + RvrInterruptPlan.MeleeReach }), Is.EqualTo(RvrInterruptMeans.None));
            RvrFightPlan farAway = Interrupts(plan, Board(1_000, members, casters.Select(c => c with { X = 1_200 }).Append(kill)), RvrFieldFacts.Empty);
            Assert.That(farAway.Interrupts, Is.Empty, "melee out of reach do not leave the train");
        }

        [Test]
        public void A_held_or_unseen_caster_needs_no_interrupter_and_the_switch_turns_it_off()
        {
            List<RvrCombatant> members = [Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Scout)];
            RvrFieldFacts facts = Facts().Member(members[1], new RvrMemberFacts { Ranged = true }).Build();
            RvrCombatant healer = Foe(eRealm.Midgard, eCharacterClass.Healer);
            RvrFightPlan plan = Start(1_000, RvrSkillTier.Average);
            Assert.Multiple(() =>
            {
                Assert.That(Interrupts(plan, Board(1_000, members, [healer]), facts).Interrupts, Has.Length.EqualTo(1));
                Assert.That(Interrupts(plan, Board(1_000, members, [healer with { Control = RvrControlState.Stunned }]), facts).Interrupts, Is.Empty);
                Assert.That(Interrupts(plan, Board(1_000, members, [healer with { TargetsGroup = false }]), facts).Interrupts, Is.Empty);
                Assert.That(RvrInterruptPlan.Plan(plan, Board(1_000, members, [healer]), On with { Interrupts = false }, 1_000, facts).Interrupts, Is.Empty);
                Assert.That(RvrInterruptPlan.Plan(plan with { Stance = RvrStance.Observe }, Board(1_000, members, [healer]), On, 1_000, facts).Interrupts, Is.Empty);
            });
        }

        [TestCase(RvrSkillTier.Veteran)]
        [TestCase(RvrSkillTier.Average)]
        [TestCase(RvrSkillTier.Casual)]
        public void Interrupters_look_again_every_one_and_a_half_to_three_seconds(RvrSkillTier tier)
        {
            (long min, long max) = RvrInterruptPlan.LookRange(tier);
            Assert.That(min, Is.GreaterThanOrEqualTo(1_500));
            Assert.That(max, Is.LessThanOrEqualTo(3_000));
            for (long now = 0; now < 500_000; now += 1_237)
                Assert.That(RvrInterruptPlan.LookMilliseconds(tier, 7_310_000_001, now), Is.InRange(min, max));
            Assert.That(RvrInterruptPlan.LookRange(RvrSkillTier.Veteran).Max, Is.LessThanOrEqualTo(RvrInterruptPlan.LookRange(RvrSkillTier.Casual).Min));
        }

        // ---- the scope guard ------------------------------------------------------------------------------------------

        [Test]
        public void Nothing_happens_outside_the_tactics_scope()
        {
            string[] fields = ["RVR_BOT_FIELD_TACTICS", "RVR_BOT_ASSIST", "RVR_BOT_CC_PLAN", "RVR_BOT_INTERRUPTS"];
            var before = fields.ToDictionary(f => f, f => (bool)typeof(Properties).GetField(f)!.GetValue(null)!);
            try
            {
                foreach (string field in fields)
                    typeof(Properties).GetField(field)!.SetValue(null, true);
                BotDutyLeases.Clear();
                Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
                enemy.Region = 250;
                long builds = RvrFightBoards.Builds;
                void Nothing(BotBrain brain, string why) => Assert.Multiple(() =>
                {
                    Assert.That(brain.RvrTacticsTarget(enemy, null, enemy), Is.Null, why);
                    Assert.That(brain.RvrTacticsCrowdControl(), Is.Null, why);
                    Assert.That(brain.RvrTacticsBlastIgnoresMez(enemy), Is.False, why);
                    Assert.That(RvrFightBoards.Builds, Is.EqualTo(builds), why + ": no board built");
                });

                Bot raider = Gamebot(objective: "RvR");
                raider.Region = 250;
                BotDutyLeases.Claim(raider, Kind.RealmRaid, "df-raid", DutyLeaseReason.RaidReserved);
                Nothing(new BotBrain { Body = raider }, "a raider");

                Bot assault = Gamebot(objective: "RvR");
                assault.Region = 250;
                BotDutyLeases.Claim(assault, Kind.BattlegroundWave, "bg-wave", DutyLeaseReason.BattlegroundRally);
                Nothing(new BotBrain { Body = assault }, "a battleground assault");

                Bot companion = Companion();
                companion.Region = 250;
                Nothing(new BotBrain { Body = companion }, "a companion");

                Bot invited = Gamebot(objective: "RvR");
                invited.Region = 250;
                JoinPlayersGroup(invited, RealPlayer());
                Nothing(new BotBrain { Body = invited }, "a player's group");

                Bot home = Gamebot(objective: "RvR");
                Nothing(new BotBrain { Body = home }, "not a battle region");

                Bot free = Gamebot(objective: "RvR");
                free.Region = 250;
                var freeBrain = new BotBrain { Body = free };
                Assert.That(freeBrain.RvrTacticsActive(), Is.True, "in scope");
                Nothing(freeBrain, "in scope, but no world: no board can be read");

                foreach (string field in fields)
                {
                    typeof(Properties).GetField(field)!.SetValue(null, field == "RVR_BOT_FIELD_TACTICS");
                }
                Nothing(new BotBrain { Body = free }, "all three switches off");
                Properties.RVR_BOT_FIELD_TACTICS = false;
                Properties.RVR_BOT_ASSIST = Properties.RVR_BOT_CC_PLAN = Properties.RVR_BOT_INTERRUPTS = true;
                Nothing(new BotBrain { Body = free }, "the master switch off");
            }
            finally
            {
                foreach ((string field, bool value) in before)
                    typeof(Properties).GetField(field)!.SetValue(null, value);
                BotDutyLeases.Clear();
            }
        }
    }
}
