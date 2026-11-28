using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #529 step 3 (piece B): the fight board lists the group's contacts (rvr_board_from_contacts), and a fighting
    /// autonomous gamebot keeps its contacts fresh with a light look around (RvrCombatRefresh). Pure-rule tests, source
    /// checks of the wiring, and two rehearsal cases on the #520 harness (a mid-fight enemy who never hits; an enemy behind
    /// a wall). Tests only: nothing here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrBoardFromContacts529
    {
        private const int Albion = 1, Midgard = 2;
        private const ushort Emain = 163;

        private static RvrContactSighting At(long target, int x = 0, int y = 0) =>
            new(target, target, Midgard, RvrPerceivedKind.EnemyBot, Emain, x, y, 0);

        // ---- the refresh's pick: bounded rays, the nearest first, the rest in turn --------------------------------------

        private static List<RvrRefreshCandidate> Ring(int count, params int[] freeSight) =>
            Enumerable.Range(0, count).Select(i => new RvrRefreshCandidate(i, 300 + i * 200, freeSight.Contains(i))).ToList();

        [Test]
        public void A_look_casts_at_most_four_rays_and_takes_every_enemy_firing_on_the_realm_without_one()
        {
            List<RvrRefreshCandidate> candidates = Ring(12, 5, 9);
            List<int> picked = RvrCombatRefresh.Pick(candidates, 0);
            Assert.That(picked, Does.Contain(5).And.Contain(9), "no ray needed: always taken");
            Assert.That(picked.Count(i => i != 5 && i != 9), Is.EqualTo(RvrCombatRefresh.MaxRays));
            Assert.That(RvrCombatRefresh.RaysAtMost(candidates), Is.EqualTo(RvrCombatRefresh.MaxRays));
            Assert.That(RvrCombatRefresh.RaysAtMost(Ring(3)), Is.EqualTo(3));
            Assert.That(RvrCombatRefresh.Pick(Ring(3), 7), Is.EquivalentTo(new[] { 0, 1, 2 }), "fewer than the cap: all");
            Assert.That(RvrCombatRefresh.Pick(new List<RvrRefreshCandidate>(), 0), Is.Empty);
            Assert.That(RvrCombatRefresh.Pick(null, 0), Is.Empty);
        }

        [Test]
        public void The_two_nearest_get_a_ray_every_look_and_the_others_take_turns()
        {
            List<RvrRefreshCandidate> candidates = Ring(10);
            var seen = new HashSet<int>();
            for (int turn = 0; turn < 4; turn++)
            {
                List<int> picked = RvrCombatRefresh.Pick(candidates, turn);
                Assert.That(picked.Take(RvrCombatRefresh.NearestRays), Is.EqualTo(new[] { 0, 1 }), $"turn {turn}");
                Assert.That(picked.Distinct().Count(), Is.EqualTo(picked.Count));
                seen.UnionWith(picked);
            }
            Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0, 10)), "a caster at the back is looked at within a few looks");
        }

        [Test]
        public void Looks_come_every_two_seconds_spread_by_bot()
        {
            Assert.That(RvrCombatRefresh.NextDue(10_000, 0), Is.EqualTo(12_000));
            Assert.That(RvrCombatRefresh.NextDue(10_000, 249), Is.EqualTo(12_249));
            Assert.That(RvrCombatRefresh.NextDue(10_000, -7), Is.EqualTo(12_007));
            Assert.That(RvrCombatRefresh.Radius, Is.EqualTo(AutonomousRvrPerception.SpotterRadius));
        }

        // ---- the board's list: the group's contacts -----------------------------------------------------------------

        [Test]
        public void The_group_list_is_one_row_per_enemy_the_freshest_member_s_within_fifteen_seconds()
        {
            var table = new RvrContacts();
            table.Note(11, Albion, At(900, 10, 10), RvrContactSource.Noticed, 1_000);
            table.Note(12, Albion, At(900, 50, 50), RvrContactSource.Called, 4_000);
            table.Note(12, Albion, At(800), RvrContactSource.Hit, 2_000);
            table.Note(13, Albion, At(700), RvrContactSource.Noticed, 2_000); // not a member
            List<RvrContact> group = table.Group(new long[] { 11, 12 }, 5_000);
            Assert.That(group.Select(c => c.Target), Is.EqualTo(new long[] { 800, 900 }), "by key, one row each");
            Assert.That(group[1].Owner, Is.EqualTo(12));
            Assert.That(group[1].X, Is.EqualTo(50), "the freshest member's row");
            Assert.That(table.Group(new long[] { 11, 12 }, 2_000 + RvrContacts.BoardMaxAgeMilliseconds + 1).Select(c => c.Target),
                Is.EqualTo(new long[] { 900 }), "older than 15 s: off the board");
            Assert.That(table.Group(null, 5_000), Is.Empty);
            Assert.That(RvrContacts.BoardMaxAgeMilliseconds, Is.EqualTo(AutonomousRvrPerception.ForgetAfterMilliseconds));
        }

        [Test]
        public void Only_a_group_that_keeps_contacts_with_the_switch_on_lists_them()
        {
            Assert.That(RvrFightBoards.UsesContacts(true, new long[] { 1 }), Is.True);
            Assert.That(RvrFightBoards.UsesContacts(false, new long[] { 1 }), Is.False, "switch off: the area search");
            Assert.That(RvrFightBoards.UsesContacts(true, Array.Empty<long>()), Is.False, "nobody keeps contacts");
            Assert.That(RvrFightBoards.UsesContacts(true, null), Is.False, "a player in the group");
        }

        [Test]
        public void The_logs_say_what_the_board_lists_and_what_the_refresh_cost()
        {
            string shadow = RvrContactShadow.Describe(Albion, new RvrShadowTotals(10, 8, 1, 2, 0, 4, 1, 0), 0, 60_000, RvrContactShadow.FromContacts);
            Assert.That(shadow, Does.EndWith("board=contacts"));
            Assert.That(RvrContactShadow.Describe(Albion, default, 0, 60_000), Does.EndWith("board=area"));
            long[] counts = new long[Enum.GetValues<RvrScanBudget.Counter>().Length];
            counts[(int)RvrScanBudget.Counter.CombatRefreshes] = 30;
            counts[(int)RvrScanBudget.Counter.CombatRays] = 90;
            Assert.That(RvrScanBudget.Format("Albion", 60, counts),
                Does.Contain("combatRefreshes=30 combatRays=90 combatRaysPerSecond=1.5"));
        }

        // ---- the wiring (source checks) ---------------------------------------------------------------------------------

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray()));
        }

        [Test]
        public void A_fighting_bot_looks_around_instead_of_scanning()
        {
            string scan = Code("bots", "autonomous", "AutonomousWorldBotController.FrontierThreat.cs");
            int fighting = scan.IndexOf("if ((brain.HasAggro || bot.InCombat || bot.IsAttacking) && previousEngine == null && engineInHand == null)", StringComparison.Ordinal);
            Assert.That(fighting, Is.GreaterThan(0));
            int refresh = scan.IndexOf("RefreshRvrContactsInCombat(brain, bot);", fighting, StringComparison.Ordinal);
            Assert.That(refresh, Is.GreaterThan(fighting).And.LessThan(scan.IndexOf("_frontierThreat.Due(", StringComparison.Ordinal)),
                "in the fighting branch, before the scan's own cadence");
            string perception = Code("bots", "autonomous", "AutonomousWorldBotController.RvrPerception.cs");
            Assert.That(perception, Does.Contain("if (!Properties.RVR_BOARD_FROM_CONTACTS || bot.IsHiredCompanion || bot.DatabaseID <= 0)"));
            Assert.That(perception, Does.Contain("RvrCombatRefresh.Pick(candidates, _combatRefreshTurn++)"));
            Assert.That(perception, Does.Contain("PerceiveRvrTargets(bot, seen.ToArray(), rvr, RvrCombatRefresh.IntervalMilliseconds);"),
                "the scan's own notice rule writes the contacts");
            Assert.That(perception, Does.Contain("using var aiProfile = AiProfile.Scope(RvrCombatRefresh.ProfileName);"));
            // Review of b5effa87: only the board's readers look, through the scan's own ray, and their notices say so.
            Assert.That(perception, Does.Contain("if (!BotBrain.RvrTacticsApplyTo(bot))"));
            Assert.That(perception, Does.Contain("if (RvrSightRay(nav, bot, target))"));
            Assert.That(perception, Does.Contain("RvrScanContext rvr = RvrContext(brain, bot, committed) with { CombatLook = true };"));
            Assert.That(scan, Does.Contain("Ray(RvrSightRay(nav, bot, target))"), "the siege fighter's scan casts the same ray");
            Assert.That(perception, Does.Contain("nav.BlockingDoorAvoidanceFilters"));
            Assert.That(RvrCombatRefresh.Look(true), Is.EqualTo("combat"));
            Assert.That(RvrCombatRefresh.Look(false), Is.EqualTo("scan"));
        }

        [Test]
        public void The_board_lists_contacts_with_the_switch_on_and_searches_the_area_with_it_off()
        {
            string boards = Code("bots", "autonomous", "RvrFightBoards.cs");
            Assert.That(boards, Does.Contain("foreach (GameLiving enemy in ContactEnemies(asker, owners, now))"));
            Assert.That(boards, Does.Contain("area = AreaEnemies(asker);"));
            Assert.That(boards, Does.Contain("FromContacts = fromContacts"));
            Assert.That(boards, Does.Contain("from={(board.FromContacts ? RvrContactShadow.FromContacts : RvrContactShadow.FromArea)}"));
        }

        // ---- the #520 harness ---------------------------------------------------------------------------------------

        private static void Print(RvrFightRehearsal.BoardCaseResult result)
        {
            foreach (string line in result.Detail)
                TestContext.Out.WriteLine("  " + line);
            TestContext.Out.WriteLine(result.Row);
            TestContext.Progress.WriteLine(result.Row);
        }

        [Test, Category("Rehearsal")]
        public void A_mid_fight_enemy_who_never_hits_is_on_the_board_through_the_refresh()
        {
            RvrFightRehearsal.BoardCaseResult on = RvrFightRehearsal.MidFightWatcher(529, combatRefresh: true);
            Print(on);
            RvrFightRehearsal.BoardCaseResult off = RvrFightRehearsal.MidFightWatcher(529, combatRefresh: false);
            Print(off);
            Assert.Multiple(() =>
            {
                Assert.That(on.HitsBy, Is.Zero, "the watcher never hit anyone");
                Assert.That(on.Listed["watcher"], Is.True, "noticed by a fighting bot's look around");
                Assert.That(on.Listed["fighter"], Is.True);
                Assert.That(on.Refreshes, Is.GreaterThan(0));
                Assert.That(on.MaxRaysPerRefresh, Is.LessThanOrEqualTo(RvrCombatRefresh.MaxRays), "bounded cost");
                Assert.That(off.Listed["watcher"], Is.False, "without the refresh nobody knows of it: the step-3 caveat");
                Assert.That(off.Refreshes, Is.Zero);
                Assert.That(on.Errors + off.Errors, Is.Zero, "no service threw");
            });
        }

        [Test, Category("Rehearsal")]
        public void An_enemy_nobody_saw_behind_a_wall_is_not_on_the_board()
        {
            RvrFightRehearsal.BoardCaseResult contacts = RvrFightRehearsal.BehindAWall(529, boardFromContacts: true);
            Print(contacts);
            RvrFightRehearsal.BoardCaseResult area = RvrFightRehearsal.BehindAWall(529, boardFromContacts: false);
            Print(area);
            Assert.Multiple(() =>
            {
                Assert.That(contacts.Listed["hidden"], Is.False, "never seen: not on the board");
                Assert.That(contacts.Listed["open"], Is.True, "noticed in the open: on the board");
                Assert.That(area.Listed["hidden"], Is.True, "the switch off: the area search lists it, as before 1.2.3");
                Assert.That(area.Listed["open"], Is.True);
                Assert.That(contacts.Errors + area.Errors, Is.Zero, "no service threw");
            });
        }

        // ---- the controller's own look (review of b5effa87: the rehearsal's RvrFightScan.Refresh is a copy) -----------

        private static readonly MethodInfo ControllerLook =
            typeof(AutonomousWorldBotController).GetMethod("RefreshRvrContactsInCombat", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>One look by the controller's own RefreshRvrContactsInCombat for this bot (its first: due at once).</summary>
        private static void LookOnce(GameBot bot) =>
            ControllerLook.Invoke(new AutonomousWorldBotController(), new object[] { (BotBrain)bot.Brain, bot });

        private static bool Knows(GameBot bot, GameLiving enemy) =>
            AutonomousWorldBotController.RvrContactTable.Knows(bot.DatabaseID, RvrPartyKey.TargetKey(enemy), GameLoop.GameLoopTime);

        /// <summary>
        /// A fighting Albion armsman, a Midgard warrior 800 east in the open (inside the sure radius) and a Midgard healer
        /// 1,200 west behind a wall. Every brain is stopped, so only the looks called here write contacts.
        /// </summary>
        private static (RvrFightWorld World, Region Region, GameBot Looker, GameBot Open, GameBot Hidden) LookWorld(bool boardFromContacts)
        {
            var world = new RvrFightWorld(5290, new Dictionary<string, object>(RvrFightRehearsal.Repeatable)
            {
                ["rvr_board_from_contacts"] = boardFromContacts,
            });
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "Hadrian's Wall flat stand-in (rehearsal)");
            world.Nav.Walls.Add(new FlatNavmesh.Wall(29_400, 28_000, 29_400, 32_000));
            GameBot looker = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "LLookerArmsman");
            GameBot open = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 50, "LOpenWarrior");
            GameBot hidden = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 50, "LHiddenHealer");
            Assert.That(world.Place(looker, region, 30_000, 30_000, 0, 1024), Is.True);
            Assert.That(world.Place(open, region, 30_800, 30_000, 0, 3072), Is.True);
            Assert.That(world.Place(hidden, region, 28_800, 30_000, 0, 1024), Is.True);
            foreach (GameBot bot in new[] { looker, open, hidden })
                bot.Brain?.Stop();
            world.Tick(); // the zones file what was placed
            AutonomousWorldBotController.RvrContactTable.Clear();
            ((BotBrain)looker.Brain).AddToAggroList(open, 100);
            return (world, region, looker, open, hidden);
        }

        [Test, Category("Rehearsal")]
        public void The_controller_s_own_look_writes_contacts_through_the_scan_s_ray()
        {
            var (world, _, looker, open, hidden) = LookWorld(boardFromContacts: true);
            using (world)
            {
                Assert.That(BotBrain.RvrTacticsApplyTo(looker), Is.True, "a free gamebot in the frontier: its board reads contacts");
                LookOnce(looker);
                Assert.Multiple(() =>
                {
                    Assert.That(Knows(looker, open), Is.True, "the enemy in the open is a contact after one look");
                    Assert.That(Knows(looker, hidden), Is.False, "the wall blocks the scan's ray: no contact");
                    Assert.That(world.ServiceErrors, Is.Zero);
                });
            }
        }

        [Test, Category("Rehearsal")]
        public void The_controller_s_own_look_is_only_for_bots_whose_board_reads_contacts()
        {
            var (world, region, looker, open, _) = LookWorld(boardFromContacts: true);
            using (world)
            {
                GameBot raider = world.NewBot(eRealm.Albion, eCharacterClass.Cleric, 50, "LRaiderCleric");
                Assert.That(world.Place(raider, region, 30_000, 30_100, 0, 1024), Is.True);
                raider.Brain?.Stop();
                world.Tick();
                ((BotBrain)raider.Brain).AddToAggroList(open, 100);
                BotDutyLeases.Claim(raider, eDutyLeaseKind.RealmRaid, "ut-529-raid", DutyLeaseReason.RaidReserved);
                try
                {
                    Assert.That(BotBrain.RvrTacticsApplyTo(raider), Is.False, "a raider's board never reads contacts");
                    LookOnce(raider);
                    Assert.That(Knows(raider, open), Is.False, "a raider does not look (the cost goes only to the board's readers)");
                }
                finally
                {
                    BotDutyLeases.Release(raider, eDutyLeaseKind.RealmRaid, "ut-529-raid");
                }
            }
            var (offWorld, _, offLooker, offOpen, _) = LookWorld(boardFromContacts: false);
            using (offWorld)
            {
                LookOnce(offLooker);
                Assert.That(Knows(offLooker, offOpen), Is.False, "rvr_board_from_contacts off: no look");
            }
        }

        [Test]
        public void A_wall_on_the_stand_in_navmesh_blocks_only_what_crosses_it()
        {
            var wall = new FlatNavmesh.Wall(10, 0, 10, 100);
            Assert.That(FlatNavmesh.Crosses(wall, new(0, 50, 0), new(20, 50, 0)), Is.True);
            Assert.That(FlatNavmesh.Crosses(wall, new(0, 50, 0), new(5, 50, 0)), Is.False);
            Assert.That(FlatNavmesh.Crosses(wall, new(0, 150, 0), new(20, 150, 0)), Is.False, "past its end");
        }
    }
}
