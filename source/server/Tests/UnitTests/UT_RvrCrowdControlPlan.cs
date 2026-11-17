using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.RvrFieldTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 2: the crowd-control plan (docs/RVR-FIELD-1.2.md "Mind" and "Hands"). The kill
    /// target is never mezzed, the mezzer opens on the charge, a resisted mez is cast again, the server's immunity is
    /// respected, and the human errors by tier. Pure. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrCrowdControlPlan : DutyLeaseFixture
    {
        private static readonly RvrTacticsSwitches On = RvrTacticsSwitches.AllOn;
        private static readonly RvrMemberFacts Mezzer = new() { CanMez = true };
        private static readonly RvrMemberFacts AreaMezzer = new() { CanMez = true, AoeMez = true };

        /// <summary>The planner's order: assist, then crowd control, then interrupts.</summary>
        private static RvrFightPlan Next(RvrFightPlan previous, RvrFightBoard board, long now, RvrFieldFacts facts)
        {
            RvrFightPlan plan = RvrAssist.Plan(previous, board, On, now, facts);
            plan = RvrCrowdControlPlan.Plan(plan, board, On, now, facts);
            return RvrInterruptPlan.Plan(plan, board, On, now, facts);
        }

        [Test]
        public void The_kill_target_is_never_mezzed()
        {
            for (int seed = 0; seed < 400; seed++)
            {
                List<RvrCombatant> members = AlbionEight();
                List<RvrCombatant> enemies =
                [
                    Foe(eRealm.Midgard, eCharacterClass.Healer), Foe(eRealm.Midgard, eCharacterClass.Healer, 450),
                    Foe(eRealm.Midgard, eCharacterClass.Shaman, 500), Foe(eRealm.Midgard, eCharacterClass.Runemaster, 550),
                ];
                var facts = Facts();
                foreach (RvrCombatant member in members.Where(m => m.Class is eCharacterClass.Sorcerer or eCharacterClass.Minstrel))
                    facts.Member(member, AreaMezzer with { CanStun = true });
                long now = 3_000 + seed * 6_007L;
                RvrFightPlan plan = Next(Start(now, (RvrSkillTier)(seed % 3)), Board(now, members, enemies), now, facts.Build());
                Assert.That(plan.CalledTarget, Is.Not.Null);
                Assert.That(plan.CrowdControl.Select(j => j.Subject), Has.None.SameAs(plan.CalledTarget), $"seed {seed}");
                Assert.That(plan.CrowdControl, Has.Length.EqualTo(2), "both mezzers hold someone, from the first contact (the opener)");
                Assert.That(plan.CrowdControl.Select(j => j.Subject).Distinct().Count(), Is.EqualTo(2), "one enemy each");
                Assert.That(plan.CrowdControl.Select(j => j.Since), Is.All.EqualTo(now));
            }
        }

        [Test]
        public void Versus_Midgard_the_healers_are_mezzed_and_the_shaman_killed()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrCombatant healerA = Foe(eRealm.Midgard, eCharacterClass.Healer), healerB = Foe(eRealm.Midgard, eCharacterClass.Healer, 450);
            RvrCombatant shaman = Foe(eRealm.Midgard, eCharacterClass.Shaman, 500), warrior = Foe(eRealm.Midgard, eCharacterClass.Warrior, 520);
            RvrCombatant sorcerer = members.Single(m => m.Class == eCharacterClass.Sorcerer);
            RvrCombatant minstrel = members.Single(m => m.Class == eCharacterClass.Minstrel);
            RvrFieldFacts facts = Facts().Member(sorcerer, AreaMezzer).Member(minstrel, Mezzer).Build();
            int shamanCalls = 0;
            for (int i = 0; i < 200; i++)
            {
                long now = 1_000 + i * 10_007L;
                RvrFightPlan plan = Next(Start(now, RvrSkillTier.Veteran), Board(now, members, [healerA, healerB, shaman, warrior]), now, facts);
                if (!ReferenceEquals(plan.CalledTarget, shaman.Living))
                    continue;
                shamanCalls++;
                Assert.That(plan.CrowdControl.Select(j => j.Subject), Is.EquivalentTo(new[] { healerA.Living, healerB.Living }));
            }
            Assert.That(shamanCalls, Is.GreaterThan(120));
        }

        [Test]
        public void The_servers_immunity_is_respected_kind_by_kind()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrCombatant cleric = Foe(eRealm.Midgard, eCharacterClass.Healer);
            RvrCombatant kill = Foe(eRealm.Midgard, eCharacterClass.Warrior, 520, health: 10);
            RvrCombatant sorcerer = members.Single(m => m.Class == eCharacterClass.Sorcerer);
            RvrCombatant minstrel = members.Single(m => m.Class == eCharacterClass.Minstrel);
            RvrFightPlan Plan(RvrMemberFacts sorc, RvrEnemyFacts healerFacts)
            {
                RvrFightBoard board = Board(1_000, members, [cleric, kill]);
                RvrFieldFacts facts = Facts().Member(sorcerer, sorc).Member(minstrel, Mezzer).Enemy(cleric, healerFacts).Build();
                RvrFightPlan plan = Start(1_000, RvrSkillTier.Average) with { CalledTarget = kill.Living, CalledAt = 1_000 };
                return RvrCrowdControlPlan.Plan(plan, board, On, 1_000, facts);
            }
            Assert.Multiple(() =>
            {
                Assert.That(Plan(Mezzer, default).CrowdControl.Select(j => j.Subject), Has.Exactly(1).SameAs(cleric.Living));
                Assert.That(Plan(Mezzer, new RvrEnemyFacts { MezImmune = true }).CrowdControl, Is.Empty,
                    "mez immune (the minute after a mez, or a bot's NPC immunity): no mezzer takes it");
                RvrFightPlan stun = Plan(Mezzer with { CanStun = true }, new RvrEnemyFacts { MezImmune = true });
                Assert.That(RvrFightPlan.SubjectFor(stun.CrowdControl, sorcerer.Living), Is.SameAs(cleric.Living), "a stun still lands");
                Assert.That(RvrCrowdControlPlan.BestKind(Mezzer with { CanStun = true }, new RvrEnemyFacts { MezImmune = true }), Is.EqualTo(RvrControlKind.Stun));
                Assert.That(RvrCrowdControlPlan.BestKind(new RvrMemberFacts { CanRoot = true }, new RvrEnemyFacts { RootImmune = true }), Is.EqualTo(RvrControlKind.None));
                Assert.That(RvrCrowdControlPlan.BestKind(new RvrMemberFacts { CanMez = true, CanStun = true, CanRoot = true },
                    new RvrEnemyFacts { MezImmune = true, StunImmune = true }), Is.EqualTo(RvrControlKind.Root));
                // the kill target is never a subject, even when it is the only enemy left
                RvrFightBoard alone = Board(1_000, members, [kill]);
                RvrFightPlan onlyKill = RvrCrowdControlPlan.Plan(Start(1_000, RvrSkillTier.Average) with { CalledTarget = kill.Living },
                    alone, On, 1_000, Facts().Member(sorcerer, AreaMezzer).Build());
                Assert.That(onlyKill.CrowdControl, Is.Empty);
            });
        }

        [Test]
        public void A_resisted_mez_is_cast_again_and_a_held_subject_keeps_its_mezzer()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrCombatant healer = Foe(eRealm.Midgard, eCharacterClass.Healer);
            RvrCombatant kill = Foe(eRealm.Midgard, eCharacterClass.Shaman, 520);
            RvrCombatant sorcerer = members.Single(m => m.Class == eCharacterClass.Sorcerer);
            RvrFieldFacts facts = Facts().Member(sorcerer, Mezzer).Build();
            RvrFightPlan plan = Start(1_000, RvrSkillTier.Casual) with { CalledTarget = kill.Living, CalledAt = 1_000 };
            RvrFightPlan first = RvrCrowdControlPlan.Plan(plan, Board(1_000, members, [healer, kill]), On, 1_000, facts);
            Assert.That(RvrFightPlan.SubjectFor(first.CrowdControl, sorcerer.Living), Is.SameAs(healer.Living));
            // resisted: still free, not immune: the job stands, so the mezzer casts again
            RvrFightPlan resisted = RvrCrowdControlPlan.Plan(first, Board(3_000, members, [healer, kill]), On, 3_000, facts);
            Assert.That(resisted.CrowdControl.Single(), Is.EqualTo(first.CrowdControl.Single()), "the same job, the same start");
            // held by this job
            RvrFightPlan held = RvrCrowdControlPlan.Plan(resisted,
                Board(5_000, members, [healer with { Control = RvrControlState.Mezzed }, kill]), On, 5_000, facts);
            Assert.That(held.CrowdControl.Single(), Is.EqualTo(first.CrowdControl.Single()));
            // the mez ends and the minute of immunity starts: the job goes
            RvrFightPlan immune = RvrCrowdControlPlan.Plan(held, Board(9_000, members, [healer, kill]), On, 9_000,
                Facts().Member(sorcerer, Mezzer).Enemy(healer, new RvrEnemyFacts { MezImmune = true }).Build());
            Assert.That(immune.CrowdControl, Is.Empty);
            // an enemy someone else mezzed is left alone
            RvrFightPlan other = RvrCrowdControlPlan.Plan(plan, Board(1_000, members, [healer with { Control = RvrControlState.Mezzed }, kill]),
                On, 1_000, facts);
            Assert.That(other.CrowdControl, Is.Empty);
        }

        [TestCase(RvrSkillTier.Veteran)]
        [TestCase(RvrSkillTier.Average)]
        [TestCase(RvrSkillTier.Casual)]
        public void A_mezzed_mezzer_is_not_replaced_for_two_to_four_seconds(RvrSkillTier tier)
        {
            List<RvrCombatant> members = AlbionEight();
            RvrCombatant healer = Foe(eRealm.Midgard, eCharacterClass.Healer);
            RvrCombatant runemaster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
            RvrCombatant kill = Foe(eRealm.Midgard, eCharacterClass.Shaman, 520);
            RvrCombatant sorcerer = members.Single(m => m.Class == eCharacterClass.Sorcerer);
            RvrCombatant minstrel = members.Single(m => m.Class == eCharacterClass.Minstrel);
            RvrFightPlan plan = Start(1_000, tier) with { CalledTarget = kill.Living, CalledAt = 1_000 };
            RvrFieldFacts both = Facts().Member(sorcerer, AreaMezzer).Member(minstrel, Mezzer).Build();
            RvrFightPlan first = RvrCrowdControlPlan.Plan(plan, Board(1_000, members, [healer, runemaster, kill]), On, 1_000, both);
            Assert.That(RvrFightPlan.SubjectFor(first.CrowdControl, sorcerer.Living), Is.SameAs(healer.Living), "the area mezzer takes the healer");
            Assert.That(RvrFightPlan.SubjectFor(first.CrowdControl, minstrel.Living), Is.SameAs(runemaster.Living));

            // the Sorcerer is mezzed at 2,000; the healer it held goes free
            long delay = RvrCrowdControlPlan.ReplaceDelay(tier, sorcerer.Key, 2_000);
            (long min, long max) = RvrCrowdControlPlan.ReplaceDelayRange(tier);
            Assert.That(delay, Is.InRange(min, max));
            Assert.That(min, Is.GreaterThanOrEqualTo(2_000));
            Assert.That(max, Is.LessThanOrEqualTo(4_000));
            RvrFieldFacts mezzed = Facts().Member(sorcerer, AreaMezzer with { ControlledSince = 2_000 }).Member(minstrel, Mezzer).Build();
            List<RvrCombatant> mezzedMembers = members.Select(m => ReferenceEquals(m, sorcerer) ? m with { Control = RvrControlState.Mezzed } : m).ToList();
            RvrCombatant minstrelNow = mezzedMembers.Single(m => m.Class == eCharacterClass.Minstrel);
            // the Minstrel's runemaster is now held, so it is free for the healer once the delay is over
            List<RvrCombatant> enemies = [healer, runemaster with { Control = RvrControlState.Mezzed }, kill];
            RvrFightPlan during = RvrCrowdControlPlan.Plan(first, Board(2_000 + delay - 1, mezzedMembers, enemies), On, 2_000 + delay - 1, mezzed);
            Assert.That(RvrFightPlan.SubjectFor(during.CrowdControl, sorcerer.Living), Is.SameAs(healer.Living), "kept, uncovered");
            RvrFightPlan after = RvrCrowdControlPlan.Plan(during with
                {
                    CrowdControl = during.CrowdControl.RemoveAll(j => ReferenceEquals(j.Member, minstrel.Living)),
                }, Board(2_000 + delay, mezzedMembers, enemies), On, 2_000 + delay, mezzed);
            Assert.That(RvrFightPlan.SubjectFor(after.CrowdControl, sorcerer.Living), Is.Null, "a controlled mezzer gets no job");
            Assert.That(RvrFightPlan.SubjectFor(after.CrowdControl, minstrelNow.Living), Is.SameAs(healer.Living), "covered after the delay");
        }

        [TestCase(RvrSkillTier.Casual, 0.25)]
        [TestCase(RvrSkillTier.Average, 0.12)]
        [TestCase(RvrSkillTier.Veteran, 0.04)]
        public void Area_damage_breaks_a_mez_at_the_tiers_share(RvrSkillTier tier, double share)
        {
            int breaks = 0;
            const int rolls = 20_000;
            for (int i = 0; i < rolls; i++)
                if (RvrCrowdControlPlan.AreaDamageBreaksMez(tier, 7_310_000_000 + i % 400, 9_000_000 + i / 400, 50_000 + i * 13L))
                    breaks++;
            Assert.That(breaks / (double)rolls, Is.EqualTo(share).Within(0.012));
            Assert.That(RvrCrowdControlPlan.AreaDamageBreaksMez(tier, 1, 2, 10_000), Is.EqualTo(RvrCrowdControlPlan.AreaDamageBreaksMez(tier, 1, 2, 19_999)),
                "one roll per ten seconds");
        }

        [Test]
        public void An_area_mez_is_not_cast_where_it_would_catch_the_kill_target()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrCrowdControlPlan.AreaSpareKillTarget(new Vector3(0, 0, 0), 350, new Vector3(300, 0, 0)), Is.False);
                Assert.That(RvrCrowdControlPlan.AreaSpareKillTarget(new Vector3(0, 0, 0), 350, new Vector3(500, 0, 0)), Is.True);
                Assert.That(RvrCrowdControlPlan.AreaSpareKillTarget(new Vector3(0, 0, 0), 350, null), Is.True);
            });
        }

        [Test]
        public void No_crowd_control_plan_without_the_switch_contact_or_a_fighting_stance()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrCombatant healer = Foe(eRealm.Midgard, eCharacterClass.Healer);
            RvrCombatant sorcerer = members.Single(m => m.Class == eCharacterClass.Sorcerer);
            RvrFieldFacts facts = Facts().Member(sorcerer, Mezzer).Build();
            RvrFightBoard board = Board(1_000, members, [healer]);
            RvrFightPlan plan = Start(1_000, RvrSkillTier.Average);
            Assert.Multiple(() =>
            {
                Assert.That(RvrCrowdControlPlan.Plan(plan, board, On, 1_000, facts).CrowdControl, Has.Length.EqualTo(1));
                Assert.That(RvrCrowdControlPlan.Plan(plan, board, On with { CrowdControlPlan = false }, 1_000, facts).CrowdControl, Is.Empty);
                Assert.That(RvrCrowdControlPlan.Plan(plan, board, RvrTacticsSwitches.AllOff, 1_000, facts).CrowdControl, Is.Empty);
                Assert.That(RvrCrowdControlPlan.Plan(plan with { Stance = RvrStance.Retreat }, board, On, 1_000, facts).CrowdControl, Is.Empty);
                Assert.That(RvrCrowdControlPlan.Plan(plan, Board(1_000, members, [healer with { TargetsGroup = false }]), On, 1_000, facts).CrowdControl,
                    Is.Empty, "an enemy nobody fights is not known");
            });
        }
    }
}
