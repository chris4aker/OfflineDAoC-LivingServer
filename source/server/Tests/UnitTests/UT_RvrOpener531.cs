using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.RvrFieldTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// #531 step 2a: the AE mez opener (RvrOpener). At a fight's first contact an area mezzer who is not the main assist
    /// gets the opener, aimed at the free enemy whose radius catches the most free enemies; the opener ends once a mez
    /// lands on the pile or after 8 s, and a new fight opens a new one. Pure. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrOpener531 : DutyLeaseFixture
    {
        private static readonly RvrTacticsSwitches On = RvrTacticsSwitches.AllOn;
        private static readonly RvrMemberFacts Mezzer = new() { CanMez = true };
        private static readonly RvrMemberFacts AreaMezzer = new() { CanMez = true, AoeMez = true };

        /// <summary>The planner's order up to the opener: assist, then opener.</summary>
        private static RvrFightPlan Next(RvrFightPlan previous, RvrFightBoard board, long now, RvrFieldFacts facts)
        {
            RvrFightPlan plan = RvrAssist.Plan(previous, board, On, now, facts);
            return RvrOpener.Plan(plan, board, On, now, facts);
        }

        /// <summary>A Midgard eight's front, clumped at x 1,500 (all within 350 of the Warrior).</summary>
        private static List<RvrCombatant> Pile() =>
        [
            Foe(eRealm.Midgard, eCharacterClass.Warrior, 1_500, 0), Foe(eRealm.Midgard, eCharacterClass.Savage, 1_550, 100),
            Foe(eRealm.Midgard, eCharacterClass.Healer, 1_600, -100), Foe(eRealm.Midgard, eCharacterClass.Shaman, 1_650, 50),
        ];

        private static RvrCombatant Sorcerer(List<RvrCombatant> members) => members.Single(m => m.Class == eCharacterClass.Sorcerer);
        private static RvrCombatant Minstrel(List<RvrCombatant> members) => members.Single(m => m.Class == eCharacterClass.Minstrel);

        [Test]
        public void The_opener_goes_to_an_area_mezzer_never_the_main_assist()
        {
            List<RvrCombatant> members = AlbionEight();
            List<RvrCombatant> pile = Pile();
            RvrFightBoard board = Board(1_000, members, pile);
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Member(Minstrel(members), Mezzer).Build();
            RvrFightPlan plan = Next(Start(1_000, RvrSkillTier.Average), board, 1_000, facts);
            Assert.Multiple(() =>
            {
                Assert.That(plan.OpenerMember, Is.SameAs(Sorcerer(members).Living), "the Sorcerer, the only area mezzer");
                Assert.That(pile.Select(e => e.Living), Has.Member(plan.OpenerCentre));
                Assert.That(plan.FightSince, Is.EqualTo(1_000));
                Assert.That(plan.OpenerSince, Is.EqualTo(1_000));
                Assert.That(plan.IsNeutral, Is.False);
            });

            // the main assist never opens: the other area mezzer does, or nobody
            RvrFightPlan sorcererAssists = Start(1_000, RvrSkillTier.Average) with { MainAssist = Sorcerer(members).Living };
            Assert.That(RvrOpener.Plan(sorcererAssists, board, On, 1_000, facts).OpenerMember, Is.Null);
            RvrFieldFacts two = Facts().Member(Sorcerer(members), AreaMezzer).Member(Minstrel(members), AreaMezzer).Build();
            Assert.That(RvrOpener.Plan(sorcererAssists, board, On, 1_000, two).OpenerMember, Is.SameAs(Minstrel(members).Living));

            // a controlled mezzer does not open
            RvrFieldFacts mezzed = Facts().Member(Sorcerer(members), AreaMezzer with { ControlledSince = 900 }).Build();
            Assert.That(Next(Start(1_000, RvrSkillTier.Average), board, 1_000, mezzed).OpenerMember, Is.Null);

            // no area mezzer, no opener; a single mezzer keeps today's crowd control
            RvrFieldFacts single = Facts().Member(Sorcerer(members), Mezzer).Member(Minstrel(members), Mezzer).Build();
            Assert.That(Next(Start(1_000, RvrSkillTier.Average), board, 1_000, single).OpenerMember, Is.Null);
        }

        [Test]
        public void The_centre_catches_the_most_free_enemies_and_healers_count_extra()
        {
            List<RvrCombatant> members = AlbionEight();
            // three together, two together far off: the three
            List<RvrCombatant> enemies =
            [
                Foe(eRealm.Midgard, eCharacterClass.Warrior, 1_500, 0), Foe(eRealm.Midgard, eCharacterClass.Savage, 1_600, 0),
                Foe(eRealm.Midgard, eCharacterClass.Berserker, 1_700, 0),
                Foe(eRealm.Midgard, eCharacterClass.Runemaster, 1_500, 1_200), Foe(eRealm.Midgard, eCharacterClass.Spiritmaster, 1_600, 1_200),
            ];
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Build();
            RvrFightPlan plan = Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, enemies), 1_000, facts);
            RvrCombatant centre = enemies.Single(e => ReferenceEquals(e.Living, plan.OpenerCentre));
            Assert.Multiple(() =>
            {
                Assert.That(enemies.Take(3), Has.Member(centre), "the three, not the far pair");
                Assert.That(RvrOpener.Catch(centre, enemies).Caught, Is.EqualTo(3), "its radius catches all three");
                Assert.That(centre, Is.SameAs(enemies[0]), "of equal catches, the one nearest the group");
            });

            // two pairs, one with a healer: the healer's pair, though it is farther
            List<RvrCombatant> pairs =
            [
                Foe(eRealm.Midgard, eCharacterClass.Warrior, 1_200, 0), Foe(eRealm.Midgard, eCharacterClass.Savage, 1_300, 0),
                Foe(eRealm.Midgard, eCharacterClass.Healer, 1_800, 1_000), Foe(eRealm.Midgard, eCharacterClass.Thane, 1_900, 1_000),
            ];
            plan = Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, pairs), 1_000, facts);
            Assert.That(new[] { pairs[2].Living, pairs[3].Living }, Has.Member(plan.OpenerCentre));
            Assert.That(RvrOpener.Catch(pairs[2], pairs).Caught, Is.EqualTo(2));
            Assert.That(RvrOpener.Catch(pairs[2], pairs).Score, Is.EqualTo(2.5));
        }

        [Test]
        public void Fewer_than_two_free_enemies_together_give_no_opener()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Build();
            List<RvrCombatant> spread =
            [
                Foe(eRealm.Midgard, eCharacterClass.Warrior, 1_500, 0), Foe(eRealm.Midgard, eCharacterClass.Healer, 1_500, 600),
                Foe(eRealm.Midgard, eCharacterClass.Shaman, 1_500, -600),
            ];
            Assert.That(Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, spread), 1_000, facts).OpenerMember, Is.Null,
                "spread 600 apart: no area mez catches two (reference.md 3.1: keep more than 350 apart)");
            // a mez-immune enemy and a rooted one are not free
            List<RvrCombatant> pile = Pile();
            RvrFieldFacts immune = Facts().Member(Sorcerer(members), AreaMezzer).Enemy(pile[0], new RvrEnemyFacts { MezImmune = true })
                .Enemy(pile[1], new RvrEnemyFacts { MezImmune = true }).Enemy(pile[2], new RvrEnemyFacts { MezImmune = true }).Build();
            Assert.That(Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, pile), 1_000, immune).OpenerMember, Is.Null);
            // nobody in contact: no fight, no opener
            List<RvrCombatant> far = [Head(eRealm.Midgard, eCharacterClass.Warrior, 1_500), Head(eRealm.Midgard, eCharacterClass.Healer, 1_550)];
            RvrFightPlan none = Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, far), 1_000, facts);
            Assert.Multiple(() =>
            {
                Assert.That(none.OpenerMember, Is.Null);
                Assert.That(none.FightSince, Is.Zero);
            });
        }

        [Test]
        public void A_landed_mez_or_the_window_ends_the_opener_and_a_new_fight_opens_a_new_one()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Build();
            RvrFightPlan first = Next(Start(1_000, RvrSkillTier.Average), Board(1_000, members, Pile()), 1_000, facts);
            Assert.That(first.OpenerMember, Is.Not.Null);

            // two of the pile mezzed: the opener landed
            List<RvrCombatant> slept =
            [
                Foe(eRealm.Midgard, eCharacterClass.Warrior, 1_500, 0, RvrControlState.Mezzed),
                Foe(eRealm.Midgard, eCharacterClass.Savage, 1_550, 100, RvrControlState.Mezzed),
                Foe(eRealm.Midgard, eCharacterClass.Healer, 1_600, -100), Foe(eRealm.Midgard, eCharacterClass.Shaman, 1_650, 50),
            ];
            RvrFightPlan landed = Next(first, Board(4_000, members, slept), 4_000, facts);
            Assert.Multiple(() =>
            {
                Assert.That(landed.OpenerDone, Is.True);
                Assert.That(landed.OpenerMember, Is.Null);
                Assert.That(landed.FightSince, Is.EqualTo(1_000));
            });
            // and it stays spent while the fight goes on, even with a fresh pile
            RvrFightPlan later = Next(landed, Board(6_000, members, Pile()), 6_000, facts);
            Assert.That(later.OpenerMember, Is.Null);
            // one mezzed (a single mez) does not end it
            List<RvrCombatant> one = Pile();
            one[3] = one[3] with { Control = RvrControlState.Mezzed };
            Assert.That(Next(first, Board(3_000, members, one), 3_000, facts).OpenerMember, Is.Not.Null);

            // the window: open at 8 s, spent after
            Assert.That(Next(first, Board(1_000 + RvrOpener.WindowMilliseconds, members, Pile()), 1_000 + RvrOpener.WindowMilliseconds, facts)
                .OpenerMember, Is.Not.Null);
            RvrFightPlan lapsed = Next(first, Board(1_001 + RvrOpener.WindowMilliseconds, members, Pile()), 1_001 + RvrOpener.WindowMilliseconds, facts);
            Assert.That(lapsed.OpenerDone, Is.True);

            // a new fight after 20 s without contact: a new opener
            long gap = 6_000 + RvrAssist.NewFightGapMilliseconds + 1;
            RvrFightPlan next = Next(later, Board(gap, members, Pile()), gap, facts);
            Assert.Multiple(() =>
            {
                Assert.That(next.FightSince, Is.EqualTo(gap));
                Assert.That(next.OpenerDone, Is.False);
                Assert.That(next.OpenerMember, Is.SameAs(Sorcerer(members).Living));
            });
        }

        [Test]
        public void The_opener_waits_for_a_stance_that_runs_the_plan()
        {
            List<RvrCombatant> members = AlbionEight();
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Build();
            RvrFightPlan observing = Start(1_000, RvrSkillTier.Average) with { Stance = RvrStance.Observe };
            RvrFightPlan watched = RvrOpener.Plan(observing, Board(1_000, members, Pile()), On, 1_000, facts);
            Assert.Multiple(() =>
            {
                Assert.That(watched.OpenerMember, Is.Null);
                Assert.That(watched.OpenerSince, Is.Zero, "the window has not opened while the group only watches");
                Assert.That(watched.FightSince, Is.EqualTo(1_000));
            });
            // 10 s of watching, then the charge: the window opens at the charge
            RvrFightPlan engage = RvrOpener.Plan(watched with { Stance = RvrStance.Engage }, Board(11_000, members, Pile()), On, 11_000, facts);
            Assert.Multiple(() =>
            {
                Assert.That(engage.OpenerMember, Is.SameAs(Sorcerer(members).Living));
                Assert.That(engage.OpenerSince, Is.EqualTo(11_000));
            });
        }

        [Test]
        public void The_switch_off_gives_no_opener_and_the_order_of_the_board_does_not_matter()
        {
            List<RvrCombatant> members = AlbionEight();
            List<RvrCombatant> pile = Pile();
            RvrFieldFacts facts = Facts().Member(Sorcerer(members), AreaMezzer).Build();
            RvrTacticsSwitches off = On with { CrowdControlPlan = false };
            Assert.That(RvrOpener.Plan(Start(1_000, RvrSkillTier.Average), Board(1_000, members, pile), off, 1_000, facts).OpenerMember, Is.Null);

            GameLiving centre = RvrOpener.Plan(Start(1_000, RvrSkillTier.Average), Board(1_000, members, pile), On, 1_000, facts).OpenerCentre;
            for (int shift = 1; shift < pile.Count; shift++)
            {
                List<RvrCombatant> turned = pile.Skip(shift).Concat(pile.Take(shift)).ToList();
                List<RvrCombatant> crew = members.Skip(shift).Concat(members.Take(shift)).ToList();
                Assert.That(RvrOpener.Plan(Start(1_000, RvrSkillTier.Average), Board(1_000, crew, turned), On, 1_000, facts).OpenerCentre,
                    Is.SameAs(centre), $"shift {shift}");
            }
        }
    }
}
