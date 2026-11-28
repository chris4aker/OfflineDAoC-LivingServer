using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.RvrFieldTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// #599 step 2: one target call across a battlegroup. A follower group's main assist takes the lead group's called
    /// target when it is on the follower's own board (RvrAssist.BattlegroupCandidate, RvrAssist.Plan), and its first pick
    /// opens on it (AutonomousWorldBotController.PlanFirstPick). Pure rules on boards of plain records, plus source pins for
    /// the wiring. The rehearsal row is in UT_RvrFightRehearsal (scenario 5). Tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrBattlegroupCall599 : DutyLeaseFixture
    {
        private static readonly RvrTacticsSwitches On = RvrTacticsSwitches.AllOn;

        private static List<RvrCombatant> Us() =>
            [Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Cleric, -200)];

        private static RvrFightBoard Board(long now, List<RvrCombatant> enemies, RvrCombatant leadCall) =>
            RvrFieldTestKit.Board(now, Us(), enemies) with { BattlegroupCall = leadCall?.Living };

        private static RvrFightPlan Plan(RvrFightBoard board, long now, RvrFightPlan previous = null, RvrFieldFacts facts = null,
            RvrSkillTier tier = RvrSkillTier.Average) =>
            RvrAssist.Plan(previous ?? Start(now, tier), board, On, now, facts ?? RvrFieldFacts.Empty);

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- the plan's call ------------------------------------------------------------------------------------------

        [Test]
        public void A_follower_takes_the_lead_call_over_its_own_better_target([Values] RvrSkillTier tier)
        {
            for (int seed = 0; seed < 200; seed++)
            {
                long now = 10_000 + seed * 7_919L;
                RvrCombatant tank = Foe(eRealm.Midgard, eCharacterClass.Warrior, 500);
                RvrCombatant caster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
                RvrFightPlan plan = Plan(Board(now, [tank, caster], tank), now, tier: tier);
                Assert.That(plan.CalledTarget, Is.SameAs(tank.Living), $"seed {seed}");
                Assert.That(plan.CallFromBattlegroup, Is.True);
                Assert.That(plan.CalledAt, Is.EqualTo(now));
                Assert.That(plan.MainAssist, Is.Not.Null);
            }
        }

        [Test]
        public void The_lead_call_comes_before_a_rising_enemy_and_a_held_call()
        {
            long now = 50_000;
            RvrCombatant tank = Foe(eRealm.Midgard, eCharacterClass.Warrior, 500);
            RvrCombatant caster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
            RvrFieldFacts facts = Facts().Enemy(caster, new RvrEnemyFacts { BigSpell = true }).Build();
            RvrFightPlan held = Start(now, RvrSkillTier.Veteran) with { CalledTarget = caster.Living, CalledAt = now - 1_000 };
            RvrFightPlan plan = Plan(Board(now, [tank, caster], tank), now, held, facts, RvrSkillTier.Veteran);
            Assert.That(plan.CalledTarget, Is.SameAs(tank.Living));
            Assert.That(plan.CallFromBattlegroup, Is.True);
        }

        [Test]
        public void The_same_call_keeps_its_time()
        {
            long now = 40_000;
            RvrCombatant caster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
            RvrFightPlan before = Start(now, RvrSkillTier.Average) with { CalledTarget = caster.Living, CalledAt = now - 9_000 };
            RvrFightPlan plan = Plan(Board(now, [caster, Foe(eRealm.Midgard, eCharacterClass.Healer, 400)], caster), now, before);
            Assert.That(plan.CalledTarget, Is.SameAs(caster.Living));
            Assert.That(plan.CalledAt, Is.EqualTo(now - 9_000), "the members' follow delays do not start again");
            Assert.That(plan.CallFromBattlegroup, Is.True);
        }

        [Test]
        public void A_follower_with_no_contact_of_its_own_still_takes_the_lead_call()
        {
            long now = 30_000;
            // Neither enemy fights the follower nor is targeted by it: its own call has nothing to call.
            RvrCombatant healer = Head(eRealm.Midgard, eCharacterClass.Healer, 1_800);
            RvrCombatant tank = Head(eRealm.Midgard, eCharacterClass.Warrior, 1_900);
            RvrFightBoard board = Board(now, [healer, tank], healer);
            Assert.That(RvrAssist.Candidates(board, RvrFieldFacts.Empty), Is.Empty, "no contact of its own");
            RvrFightPlan plan = Plan(board, now);
            Assert.That(plan.CalledTarget, Is.SameAs(healer.Living));
            Assert.That(plan.CallFromBattlegroup, Is.True);
            Assert.That(Plan(board with { BattlegroupCall = null }, now).CalledTarget, Is.Null, "without the lead's call: no call");
        }

        [Test]
        public void A_lead_call_off_the_board_or_out_of_reach_is_not_taken()
        {
            long now = 20_000;
            RvrCombatant caster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
            RvrCombatant far = Foe(eRealm.Midgard, eCharacterClass.Healer, RvrAssist.CallRadius + 400);
            RvrCombatant wall = Foe(eRealm.Midgard, eCharacterClass.Healer, 600);
            RvrCombatant unreachable = Foe(eRealm.Midgard, eCharacterClass.Healer, 700);
            RvrCombatant mezzed = Foe(eRealm.Midgard, eCharacterClass.Healer, 800, control: RvrControlState.Mezzed);
            RvrCombatant elsewhere = Foe(eRealm.Midgard, eCharacterClass.Shaman, 900); // the lead's call, not on this board
            RvrFieldFacts facts = Facts().Enemy(wall, new RvrEnemyFacts { OnWall = true })
                .Enemy(unreachable, new RvrEnemyFacts { Unreachable = true }).Build();
            var enemies = new List<RvrCombatant> { caster, far, wall, unreachable, mezzed };
            foreach ((string why, RvrCombatant call) in new[] { ("beyond the call radius", far), ("on a wall", wall),
                         ("unreachable", unreachable), ("mezzed", mezzed), ("not on the board", elsewhere) })
            {
                RvrFightBoard board = Board(now, enemies, call);
                Assert.That(RvrAssist.BattlegroupCandidate(board, facts), Is.Null, why);
                RvrFightPlan plan = Plan(board, now, facts: facts);
                Assert.That(plan.CallFromBattlegroup, Is.False, why);
                Assert.That(plan.CalledTarget, Is.Not.SameAs(call.Living), why);
            }
        }

        [Test]
        public void A_group_with_no_battlegroup_call_calls_as_before()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                long now = 5_000 + seed * 3_571L;
                var enemies = new List<RvrCombatant>
                {
                    Foe(eRealm.Midgard, eCharacterClass.Warrior, 500), Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450),
                    Foe(eRealm.Midgard, eCharacterClass.Healer, 600),
                };
                RvrFightBoard without = RvrFieldTestKit.Board(now, Us(), enemies);
                RvrFightPlan expected = RvrAssist.Plan(Start(now, RvrSkillTier.Average), without, On, now, RvrFieldFacts.Empty);
                RvrFightPlan plan = Plan(without with { BattlegroupCall = null }, now);
                Assert.That(plan.CalledTarget, Is.SameAs(expected.CalledTarget));
                Assert.That(plan.CallFromBattlegroup, Is.False);
            }
        }

        [Test]
        public void Clearing_the_plan_drops_the_battlegroup_mark()
        {
            long now = 9_000;
            RvrCombatant caster = Foe(eRealm.Midgard, eCharacterClass.Runemaster, 450);
            RvrFightPlan marked = Start(now, RvrSkillTier.Average) with { CalledTarget = caster.Living, CalledAt = now, CallFromBattlegroup = true };
            // a lone member: no assist train
            RvrFightBoard lone = RvrFieldTestKit.Board(now, [Head(eRealm.Albion, eCharacterClass.Armsman)], [caster]) with
            {
                BattlegroupCall = caster.Living,
            };
            RvrFightPlan plan = RvrAssist.Plan(marked, lone, On, now, RvrFieldFacts.Empty);
            Assert.That(plan.CalledTarget, Is.Null);
            Assert.That(plan.CallFromBattlegroup, Is.False);
            Assert.That(RvrAssist.Cleared(marked).CallFromBattlegroup, Is.False);
        }

        // ---- the first pick -------------------------------------------------------------------------------------------

        [Test]
        public void The_first_pick_takes_the_lead_call_only_within_the_call_radius()
        {
            Assert.That(AutonomousWorldBotController.BattlegroupCallTakes(0), Is.True);
            Assert.That(AutonomousWorldBotController.BattlegroupCallTakes(RvrFirstPick.CallRadius), Is.True);
            Assert.That(AutonomousWorldBotController.BattlegroupCallTakes(RvrFirstPick.CallRadius + 1), Is.False);
            Assert.That(RvrFirstPick.Label(RvrFirstPickReason.Battlegroup), Is.EqualTo("battlegroup"));
        }

        [Test]
        public void A_called_lead_target_wins_the_first_pick_over_the_kill_order()
        {
            // The scan marks the lead's call as the group's call; the pure pick then takes it over a better kill-order target.
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var candidates = new[]
                {
                    new RvrFirstPickCandidate(new RvrCombatant { Key = 1, Class = eCharacterClass.Warrior, Realm = eRealm.Midgard,
                        Role = RvrRole.Tank }, false, false, 600, 0, Called: true),
                    new RvrFirstPickCandidate(new RvrCombatant { Key = 2, Class = eCharacterClass.Runemaster, Realm = eRealm.Midgard,
                        Role = RvrRole.Caster }, false, false, 300, 0, Called: false),
                };
                Assert.That(RvrFirstPick.Pick(candidates, RvrSkillTier.Average, seed, out RvrFirstPickReason reason), Is.EqualTo(0));
                Assert.That(reason, Is.EqualTo(RvrFirstPickReason.Called));
            }
        }

        // ---- the wiring (source pins) ---------------------------------------------------------------------------------

        [Test]
        public void Only_a_battlegroup_follower_gets_a_lead_and_the_board_carries_its_call()
        {
            string support = Code("bots", "autonomous", "RealmWarbandSupport.cs");
            int battlegroups = support.IndexOf("AutonomousRvrEventLayer.BattlegroupsNow()", StringComparison.Ordinal);
            int leads = support.IndexOf("leads[group] = lead", StringComparison.Ordinal);
            Assert.That(battlegroups, Is.GreaterThan(0));
            Assert.That(leads, Is.GreaterThan(battlegroups), "leads are written only from the battlegroup rosters");
            Assert.That(support, Does.Contain("Volatile.Write(ref _leads, leads)"));

            string boards = Code("bots", "autonomous", "RvrFightBoards.cs");
            Assert.That(boards, Does.Contain("BattlegroupCall = BattlegroupCallOf(asker, now)"));
            Assert.That(boards, Does.Contain("holder.Peek(now)?.Plan?.CalledTarget"), "the lead's board is peeked, never built");

            string pick = Code("bots", "autonomous", "AutonomousWorldBotController.RvrFirstPick.cs");
            Assert.That(pick, Does.Contain("RvrFightBoards.BattlegroupCallOf(bot, now)"));
            Assert.That(pick, Does.Contain("reason = RvrFirstPickReason.Battlegroup"));
        }

        [Test]
        public void A_group_hosting_a_player_never_links_so_never_has_a_lead()
        {
            // Companions and a player's own group stay out: the battlegroup layer refuses a group that hosts a player.
            Assert.That(AutonomousRvrEventLayer.CanLink(new AutonomousRvrEventLayer.BattlegroupCandidate("rvr-1", eRealm.Albion, 1,
                0, 0, 8, false, HostsPlayer: true)), Is.False);
            Assert.That(RealmWarbandSupport.BattlegroupLeadOf(null), Is.Null);
        }
    }
}
