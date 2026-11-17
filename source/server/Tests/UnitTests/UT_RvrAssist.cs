using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.RvrFieldTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 2 (docs/RVR-FIELD-1.2.md "Mind" and "Hands"): the kill order, the main assist, the
    /// called target and its hold, and the members' delay and tunnel by skill tier. Pure rules on boards of plain
    /// records (the live objects are uninitialized gamebots). Tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrAssist : DutyLeaseFixture
    {
        private static readonly RvrTacticsSwitches On = RvrTacticsSwitches.AllOn;

        private static RvrFightPlan Call(RvrFightBoard board, RvrSkillTier tier, long now, RvrFieldFacts facts = null,
            RvrFightPlan previous = null) =>
            RvrAssist.Plan(previous ?? Start(now, tier), board, On, now, facts ?? RvrFieldFacts.Empty);

        // ---- the kill order ------------------------------------------------------------------------------------------

        [Test]
        public void The_general_order_is_casters_healers_bladeturn_crowd_control_and_tanks_last()
        {
            double Of(eCharacterClass c) => RvrTargetPriority.RealmScore(c, RvrTargetPriority.KillRole(c, RvrRoles.For(c)), eRealm.Albion);
            Assert.Multiple(() =>
            {
                Assert.That(Of(eCharacterClass.Wizard), Is.GreaterThan(Of(eCharacterClass.Cleric)), "casters before healers");
                Assert.That(Of(eCharacterClass.Cleric), Is.GreaterThan(RvrTargetPriority.BaseScore(RvrKillRole.Bladeturn)), "healers before bladeturn casters");
                Assert.That(RvrTargetPriority.KillRole(eCharacterClass.Warden, RvrRole.Support), Is.EqualTo(RvrKillRole.Bladeturn));
                Assert.That(RvrTargetPriority.BaseScore(RvrKillRole.Bladeturn), Is.GreaterThan(Of(eCharacterClass.Minstrel)), "bladeturn before crowd control");
                Assert.That(Of(eCharacterClass.Minstrel), Is.GreaterThan(Of(eCharacterClass.Mercenary)));
                foreach (eCharacterClass c in Enum.GetValues<eCharacterClass>())
                    if (c != eCharacterClass.Unknown && RvrRoles.For(c) != RvrRole.Tank)
                        Assert.That(Of(c), Is.GreaterThan(Of(eCharacterClass.Armsman)), $"tanks last: {c}");
            });
        }

        [Test]
        public void Realm_playbooks_change_the_order_versus_Midgard_and_Hibernia()
        {
            double Of(eCharacterClass c, eRealm realm) => RvrTargetPriority.RealmScore(c, RvrTargetPriority.KillRole(c, RvrRoles.For(c)), realm);
            Assert.Multiple(() =>
            {
                // versus Midgard: "mezz the healers, kill the shaman and then kill the tanks"
                Assert.That(Of(eCharacterClass.Shaman, eRealm.Midgard), Is.GreaterThan(Of(eCharacterClass.Runemaster, eRealm.Midgard)));
                Assert.That(Of(eCharacterClass.Runemaster, eRealm.Midgard), Is.GreaterThan(Of(eCharacterClass.Healer, eRealm.Midgard)));
                Assert.That(Of(eCharacterClass.Healer, eRealm.Midgard), Is.GreaterThan(Of(eCharacterClass.Warrior, eRealm.Midgard)));
                Assert.That(RvrTargetPriority.HoldScore(Foe(eRealm.Midgard, eCharacterClass.Healer)),
                    Is.GreaterThan(RvrTargetPriority.HoldScore(Foe(eRealm.Midgard, eCharacterClass.Runemaster))), "the healers are mezzed first");
                // versus Hibernia: the Bard, then the Eldritch, then the Druids
                Assert.That(Of(eCharacterClass.Bard, eRealm.Hibernia), Is.GreaterThan(Of(eCharacterClass.Eldritch, eRealm.Hibernia)));
                Assert.That(Of(eCharacterClass.Eldritch, eRealm.Hibernia), Is.GreaterThan(Of(eCharacterClass.Druid, eRealm.Hibernia)));
                Assert.That(Of(eCharacterClass.Druid, eRealm.Hibernia), Is.GreaterThan(Of(eCharacterClass.Enchanter, eRealm.Hibernia)));
                // versus Albion: the general order
                Assert.That(Of(eCharacterClass.Sorcerer, eRealm.Albion), Is.GreaterThan(Of(eCharacterClass.Cleric, eRealm.Albion)));
                Assert.That(Of(eCharacterClass.Minstrel, eRealm.Albion), Is.EqualTo(RvrTargetPriority.BaseScore(RvrKillRole.CrowdControl)));
            });
        }

        /// <summary>Who a Veteran, an Average and a Casual group call first against each realm's eight, over many calls.</summary>
        [TestCase(eRealm.Midgard, eCharacterClass.Shaman)]
        [TestCase(eRealm.Hibernia, eCharacterClass.Bard)]
        [TestCase(eRealm.Albion, eCharacterClass.Sorcerer)]
        public void Each_realm_eight_is_called_by_its_playbook(eRealm realm, eCharacterClass first)
        {
            eCharacterClass[] eight = realm switch
            {
                eRealm.Midgard => [eCharacterClass.Healer, eCharacterClass.Healer, eCharacterClass.Shaman, eCharacterClass.Runemaster,
                    eCharacterClass.Skald, eCharacterClass.Savage, eCharacterClass.Berserker, eCharacterClass.Warrior],
                eRealm.Hibernia => [eCharacterClass.Druid, eCharacterClass.Druid, eCharacterClass.Bard, eCharacterClass.Warden,
                    eCharacterClass.Eldritch, eCharacterClass.Eldritch, eCharacterClass.Hero, eCharacterClass.Blademaster],
                _ => [eCharacterClass.Cleric, eCharacterClass.Cleric, eCharacterClass.Sorcerer, eCharacterClass.Theurgist,
                    eCharacterClass.Paladin, eCharacterClass.Minstrel, eCharacterClass.Mercenary, eCharacterClass.Mercenary],
            };
            var firstShare = new Dictionary<RvrSkillTier, double>();
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                int firsts = 0, tanks = 0, calls = 0;
                var byEnemy = new Dictionary<int, int>();
                for (int seed = 0; seed < 300; seed++)
                {
                    eRealm us = realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;
                    var members = new List<RvrCombatant> { Head(us, eCharacterClass.Armsman), Head(us, eCharacterClass.Cleric, -200) };
                    // Theurgist and Sorcerer both cast: the Albion eight's casters share the first place by wobble.
                    var enemies = eight.Select((c, i) => Foe(realm, c, 400 + i * 30, i * 40)).ToList();
                    long now = 10_000 + seed * 9_973L;
                    RvrFightPlan plan = Call(Board(now, members, enemies, us), tier, now);
                    RvrCombatant called = enemies.Single(e => ReferenceEquals(e.Living, plan.CalledTarget));
                    calls++;
                    // counted per enemy (two Eldritch are two people); the Albion casters share the first place
                    int index = enemies.IndexOf(called);
                    byEnemy[index] = byEnemy.GetValueOrDefault(index) + 1;
                    if (called.Class == first || realm == eRealm.Albion && called.Role == RvrRole.Caster)
                        firsts++;
                    if (called.Role == RvrRole.Tank)
                        tanks++;
                }
                firstShare[tier] = firsts / (double)calls;
                eCharacterClass mode = eight[byEnemy.OrderByDescending(p => p.Value).First().Key];
                Assert.That(realm == eRealm.Albion ? RvrRoles.For(mode) == RvrRole.Caster : mode == first, Is.True,
                    $"{realm} {tier}: {first} is the enemy called most ({string.Join(", ", byEnemy.Select(p => $"{eight[p.Key]} {p.Value}"))})");
                Assert.That(tanks / (double)calls, Is.LessThan(RvrTargetPriority.WrongCallShare(tier) + 0.06), $"{realm} {tier}: tanks only by the wrong-call share");
            }
            Assert.That(firstShare[RvrSkillTier.Veteran], Is.GreaterThan(firstShare[RvrSkillTier.Casual]), "veterans keep the order better");
        }

        // ---- reachability ----------------------------------------------------------------------------------------------

        [Test]
        public void An_enemy_the_group_cannot_reach_is_never_called()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
                RvrCombatant onWall = Foe(eRealm.Midgard, eCharacterClass.Runemaster);
                RvrCombatant ignored = Foe(eRealm.Midgard, eCharacterClass.Shaman);
                RvrCombatant far = Foe(eRealm.Midgard, eCharacterClass.Spiritmaster, x: RvrAssist.CallRadius + 200);
                RvrCombatant reachable = Foe(eRealm.Midgard, eCharacterClass.Warrior);
                RvrFieldFacts facts = Facts().Enemy(onWall, new RvrEnemyFacts { OnWall = true, BigSpell = true })
                    .Enemy(ignored, new RvrEnemyFacts { Unreachable = true, JustResurrected = true }).Build();
                long now = 5_000 + seed * 7_919L;
                RvrFightPlan plan = Call(Board(now, members, [onWall, ignored, far, reachable]), RvrSkillTier.Casual, now, facts);
                Assert.That(plan.CalledTarget, Is.SameAs(reachable.Living), $"seed {seed}");
            }
            var alone = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
            RvrCombatant wall = Foe(eRealm.Midgard, eCharacterClass.Runemaster);
            RvrFightPlan none = Call(Board(1_000, alone, [wall]), RvrSkillTier.Veteran, 1_000,
                Facts().Enemy(wall, new RvrEnemyFacts { OnWall = true }).Build());
            Assert.That(none.CalledTarget, Is.Null, "no call at all rather than one on a wall");
            Assert.That(none.MainAssist, Is.Null);
        }

        // ---- the hold ----------------------------------------------------------------------------------------------------

        [Test]
        public void A_call_is_held_four_to_eight_seconds_unless_the_target_dies_is_mezzed_or_becomes_unreachable()
        {
            var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
            RvrCombatant warrior = Foe(eRealm.Midgard, eCharacterClass.Warrior);
            RvrCombatant shaman = Foe(eRealm.Midgard, eCharacterClass.Shaman, 500);
            RvrFightPlan first = Call(Board(10_000, members, [warrior]), RvrSkillTier.Veteran, 10_000);
            Assert.That(first.CalledTarget, Is.SameAs(warrior.Living));
            Assert.That(first.CalledAt, Is.EqualTo(10_000));
            ulong seed = RvrAssist.GroupSeed(Board(0, members, []));
            long hold = RvrAssist.HoldMilliseconds(seed, 10_000);
            Assert.That(hold, Is.InRange(4_000, 8_000));

            // a better target arrives: the call stands until the hold ends
            RvrFightPlan held = Call(Board(10_000 + hold - 1, members, [warrior, shaman]), RvrSkillTier.Veteran, 10_000 + hold - 1, previous: first);
            Assert.That(held.CalledTarget, Is.SameAs(warrior.Living), "held");
            Assert.That(held.CalledAt, Is.EqualTo(10_000));
            RvrFightPlan expired = Call(Board(10_000 + hold, members, [warrior, shaman]), RvrSkillTier.Veteran, 10_000 + hold, previous: held);
            Assert.That(expired.CalledAt, Is.EqualTo(10_000 + hold), "a new call when the hold ends");
            Assert.That(expired.CalledTarget, Is.SameAs(RvrAssist.WrongCall(RvrSkillTier.Veteran, seed, 10_000 + hold) ? warrior.Living : shaman.Living),
                "the Shaman, by the Midgard playbook (or the Warrior on one of the tier's wrong calls)");

            // dead (gone from the board), mezzed by someone else, unreachable: dropped at once
            RvrFightPlan dead = Call(Board(10_500, members, [shaman]), RvrSkillTier.Veteran, 10_500, previous: first);
            Assert.That(dead.CalledTarget, Is.SameAs(shaman.Living));
            RvrCombatant mezzedWarrior = warrior with { Control = RvrControlState.Mezzed };
            RvrFightPlan mezzed = Call(Board(10_500, members, [mezzedWarrior, shaman]), RvrSkillTier.Veteran, 10_500, previous: first);
            Assert.That(mezzed.CalledTarget, Is.SameAs(shaman.Living), "mezzed by someone else");
            RvrFightPlan unreachable = Call(Board(10_500, members, [warrior, shaman]), RvrSkillTier.Veteran, 10_500,
                Facts().Enemy(warrior, new RvrEnemyFacts { Unreachable = true }).Build(), first);
            Assert.That(unreachable.CalledTarget, Is.SameAs(shaman.Living), "unreachable");
            Assert.That(unreachable.CalledAt, Is.EqualTo(10_500));

            // over many calls the hold spans 4-8 s
            long min = long.MaxValue, max = long.MinValue;
            for (long at = 0; at < 2_000_000; at += 997)
            {
                long h = RvrAssist.HoldMilliseconds(seed, at);
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
            Assert.That(min, Is.LessThan(4_100));
            Assert.That(max, Is.GreaterThan(7_900));
        }

        [Test]
        public void A_caster_using_Mastery_of_Concentration_or_an_enemy_just_resurrected_takes_the_call()
        {
            var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
            RvrCombatant shaman = Foe(eRealm.Midgard, eCharacterClass.Shaman);
            RvrCombatant runemaster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 500);
            RvrCombatant warrior = Foe(eRealm.Midgard, eCharacterClass.Warrior, 450);
            foreach (RvrEnemyFacts rising in new[] { new RvrEnemyFacts { BigSpell = true }, new RvrEnemyFacts { JustResurrected = true } })
            {
                RvrFightPlan first = Call(Board(10_000, members, [shaman, runemaster, warrior]), RvrSkillTier.Veteran, 10_000);
                Assert.That(first.CalledTarget, Is.SameAs(shaman.Living));
                RvrCombatant risen = rising.BigSpell ? runemaster : warrior;
                RvrFieldFacts facts = Facts().Enemy(risen, rising).Build();
                RvrFightPlan plan = first;
                long at = 11_000;
                // noticed within a board or two (90% a board for a veteran caller), even inside the hold
                while (!ReferenceEquals(plan.CalledTarget, risen.Living) && at < 20_000)
                {
                    plan = Call(Board(at, members, [shaman, runemaster, warrior]), RvrSkillTier.Veteran, at, facts, plan);
                    at += 1_000;
                }
                Assert.That(plan.CalledTarget, Is.SameAs(risen.Living), rising.BigSpell ? "Mastery of Concentration" : "just resurrected");
            }
        }

        // ---- the main assist -----------------------------------------------------------------------------------------------

        [Test]
        public void The_main_assist_is_a_tank_the_leader_first_and_is_kept()
        {
            RvrCombatant cleric = Head(eRealm.Albion, eCharacterClass.Cleric);
            RvrCombatant merc = Head(eRealm.Albion, eCharacterClass.Mercenary);
            RvrCombatant armsman = Head(eRealm.Albion, eCharacterClass.Armsman, level: 48);
            RvrCombatant paladin = Head(eRealm.Albion, eCharacterClass.Paladin);
            RvrFightBoard board = Board(1, [cleric, merc, armsman, paladin], []);
            Assert.Multiple(() =>
            {
                Assert.That(RvrAssist.ChooseMainAssist(board, RvrFieldFacts.Empty, null), Is.SameAs(paladin), "a tank, the highest level");
                Assert.That(RvrAssist.ChooseMainAssist(board, Facts().Member(armsman, new RvrMemberFacts { IsLeader = true }).Build(), null),
                    Is.SameAs(armsman), "the leader first among tanks");
                Assert.That(RvrAssist.ChooseMainAssist(board, RvrFieldFacts.Empty, merc.Living), Is.SameAs(merc), "kept while in the group");
                Assert.That(RvrAssist.ChooseMainAssist(Board(1, [cleric, merc], []), Facts().Member(cleric, new RvrMemberFacts { IsLeader = true }).Build(), null),
                    Is.SameAs(merc), "no tank: a melee fighter before a healing leader");
            });
        }

        [Test]
        public void No_call_without_contact_a_second_member_the_switch_or_a_fighting_stance()
        {
            var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
            RvrCombatant unseen = Head(eRealm.Midgard, eCharacterClass.Shaman, 600);
            Assert.Multiple(() =>
            {
                Assert.That(Call(Board(1_000, members, [unseen]), RvrSkillTier.Veteran, 1_000).CalledTarget, Is.Null,
                    "an enemy in view that nobody fights is not known to the group (one perception rule)");
                RvrCombatant foe = Foe(eRealm.Midgard, eCharacterClass.Shaman);
                RvrCombatant mate = Head(eRealm.Midgard, eCharacterClass.Healer, 400 + RvrAssist.NoticeRadius - 10);
                RvrFightPlan withMate = Call(Board(1_000, members, [foe, mate]), RvrSkillTier.Veteran, 1_000);
                Assert.That(RvrAssist.Noticed(Board(1_000, members, [foe, mate, unseen with { X = 400 + RvrAssist.NoticeRadius + 900 }])),
                    Has.Count.EqualTo(2), "a mate within 1,500 of an enemy in contact is known");
                Assert.That(withMate.CalledTarget, Is.Not.Null);
                Assert.That(Call(Board(1_000, [members[0]], [foe]), RvrSkillTier.Veteran, 1_000).CalledTarget, Is.Null, "solo: no assist train");
                RvrFightBoard board = Board(1_000, members, [foe]);
                Assert.That(RvrAssist.Plan(Start(1_000, RvrSkillTier.Veteran), board, On with { Assist = false }, 1_000, null).CalledTarget, Is.Null);
                Assert.That(RvrAssist.Plan(Start(1_000, RvrSkillTier.Veteran), board, RvrTacticsSwitches.AllOff, 1_000, null).CalledTarget, Is.Null);
                foreach (RvrStance stance in new[] { RvrStance.Observe, RvrStance.Hold, RvrStance.Retreat, RvrStance.Regroup })
                    Assert.That(RvrAssist.Plan(withMate with { Stance = stance }, board, On, 2_000, null).CalledTarget, Is.Null, stance.ToString());
                Assert.That(RvrAssist.Plan(Start(1_000, RvrSkillTier.Veteran) with { Stance = RvrStance.Kite }, board, On, 1_000, null).CalledTarget,
                    Is.SameAs(foe.Living), "a kiting group still calls");
            });
        }

        // ---- the human errors ------------------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual)]
        [TestCase(RvrSkillTier.Average)]
        [TestCase(RvrSkillTier.Veteran)]
        public void A_tank_is_called_at_the_tiers_wrong_call_share(RvrSkillTier tier)
        {
            int tanks = 0;
            const int calls = 6_000;
            for (int i = 0; i < calls; i++)
            {
                var members = new List<RvrCombatant> { Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric) };
                RvrCombatant runemaster = Foe(eRealm.Midgard, eCharacterClass.Runemaster);
                RvrCombatant healer = Foe(eRealm.Midgard, eCharacterClass.Healer, 450);
                RvrCombatant warrior = Foe(eRealm.Midgard, eCharacterClass.Warrior, 500);
                long now = 1_000 + i * 1_009L;
                if (ReferenceEquals(Call(Board(now, members, [runemaster, healer, warrior]), tier, now).CalledTarget, warrior.Living))
                    tanks++;
            }
            Assert.That(tanks / (double)calls, Is.EqualTo(RvrTargetPriority.WrongCallShare(tier)).Within(0.015));
        }

        [Test]
        public void The_score_wobble_is_about_twenty_percent_by_tier()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrTargetPriority.Wobble(RvrSkillTier.Average), Is.EqualTo(0.20));
                Assert.That(RvrTargetPriority.Wobble(RvrSkillTier.Veteran), Is.LessThan(0.20));
                Assert.That(RvrTargetPriority.Wobble(RvrSkillTier.Casual), Is.GreaterThan(0.20));
                foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                {
                    Assert.That(RvrTargetPriority.Jitter(tier, 0), Is.EqualTo(1 - RvrTargetPriority.Wobble(tier)).Within(1e-9));
                    Assert.That(RvrTargetPriority.Jitter(tier, 1), Is.EqualTo(1 + RvrTargetPriority.Wobble(tier)).Within(1e-9));
                }
                // close scores flip now and then (a Runemaster over a Spiritmaster), far ones never (a Warrior over a Runemaster)
                RvrCombatant a = Foe(eRealm.Midgard, eCharacterClass.Runemaster);
                RvrCombatant b = Foe(eRealm.Midgard, eCharacterClass.Spiritmaster, health: 90);
                int flips = 0;
                for (long t = 0; t < 2_000; t++)
                    if (RvrAssist.CallScore(a, default, RvrSkillTier.Average, 7, t) > RvrAssist.CallScore(b, default, RvrSkillTier.Average, 7, t))
                        flips++;
                Assert.That(flips, Is.InRange(400, 1_000));
            });
        }

        [TestCase(RvrSkillTier.Casual, 1_000, 4_000, 0.40)]
        [TestCase(RvrSkillTier.Average, 600, 2_500, 0.25)]
        [TestCase(RvrSkillTier.Veteran, 300, 1_500, 0.10)]
        public void Members_follow_after_the_tiers_delay_and_the_tiers_share_tunnels(RvrSkillTier tier, long min, long max, double share)
        {
            const int members = 20_000;
            long low = long.MaxValue, high = long.MinValue;
            double sum = 0;
            int tunnels = 0;
            for (int id = 1; id <= members; id++)
            {
                long delay = RvrAssist.FollowDelay(tier, 7_310_000_000 + id, 123_456 + id * 31L);
                low = Math.Min(low, delay);
                high = Math.Max(high, delay);
                sum += delay;
                if (RvrAssist.Tunnels(tier, 7_310_000_000 + id, 50_000 + id % 97, 0.5))
                    tunnels++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(low, Is.GreaterThanOrEqualTo(min));
                Assert.That(high, Is.LessThanOrEqualTo(max));
                Assert.That(low, Is.LessThan(min + (max - min) / 50), "the range is used");
                Assert.That(high, Is.GreaterThan(max - (max - min) / 50));
                Assert.That(sum / members, Is.EqualTo((min + max) / 2.0).Within((max - min) * 0.02), "spread evenly");
                Assert.That(tunnels / (double)members, Is.EqualTo(share).Within(0.015), "the tier's share ignores the call");
                Assert.That(RvrAssist.TunnelShare(tier, 1.0), Is.GreaterThan(share), "bold personalities tunnel more");
                Assert.That(RvrAssist.TunnelShare(tier, 0.0), Is.LessThan(share));
            });
        }

        [Test]
        public void Tunnelling_is_decided_once_per_fight()
        {
            Assert.Multiple(() =>
            {
                long fight = RvrAssist.FightId(0, long.MinValue / 2, 10_000);
                Assert.That(fight, Is.EqualTo(10_000), "the first call starts a fight");
                Assert.That(RvrAssist.FightId(fight, 14_000, 16_000), Is.EqualTo(fight), "the next call of the same fight");
                Assert.That(RvrAssist.FightId(fight, 14_000, 14_000 + RvrAssist.NewFightGapMilliseconds + 1), Is.Not.EqualTo(fight),
                    "after twenty seconds without a call, a new fight");
                for (long id = 1; id < 200; id++)
                    Assert.That(RvrAssist.Tunnels(RvrSkillTier.Casual, id, fight, 0.5), Is.EqualTo(RvrAssist.Tunnels(RvrSkillTier.Casual, id, fight, 0.5)));
            });
        }

        [Test]
        public void A_group_logs_a_call_once_per_target_and_at_most_every_two_seconds()
        {
            GameLiving a = DutyLeaseTestKit.Gamebot(), b = DutyLeaseTestKit.Gamebot();
            Assert.Multiple(() =>
            {
                Assert.That(RvrAssist.ShouldLogCall(null, a, 10_000, long.MinValue / 2), Is.True);
                Assert.That(RvrAssist.ShouldLogCall(a, a, 20_000, 10_000), Is.False, "the same call");
                Assert.That(RvrAssist.ShouldLogCall(a, b, 11_000, 10_000), Is.False, "too soon");
                Assert.That(RvrAssist.ShouldLogCall(a, b, 12_000, 10_000), Is.True);
                Assert.That(RvrAssist.ShouldLogCall(a, null, 12_000, 10_000), Is.False);
            });
        }
    }
}
