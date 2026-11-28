using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #599 step 3 (#530 step 5d): the realm's zerg of 16-24 gamebots. The size rule (owner 50A), the recruit plan, the
    /// recruit's leave rules, the gates it holds, the record's life beside the battlegroups, and the controller's wiring.
    /// Unit tests only (the harness case is UT_RvrFightRehearsal scenario 6); nothing here has been seen in game.
    /// Autonomous gamebots only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrZerg599
    {
        [SetUp]
        public void Reset()
        {
            L.ResetForceIntents();
            L.ResetBattlegroups();
        }

        [TearDown]
        public void After() => L.ResetBattlegroups();

        private static L.BattlegroupCandidate C(string id, float x, int heads = 8, eRealm realm = eRealm.Albion, ushort region = 1,
            bool inEvent = false, bool player = false, float y = 0) => new(id, realm, region, x, y, heads, inEvent, player);

        private static Dictionary<eRealm, int> Free(int albion, int midgard = 0) => new() { [eRealm.Albion] = albion, [eRealm.Midgard] = midgard };

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- the size (owner 50A) -----------------------------------------------------------------------------------

        [TestCase(0, false, 0)]
        [TestCase(47, false, 0)]
        [TestCase(48, false, 16)]
        [TestCase(60, false, 20)]
        [TestCase(72, false, 24)]
        [TestCase(300, false, 24)]
        [TestCase(15, true, 0)]
        [TestCase(16, true, 16)]
        [TestCase(20, true, 16)]
        [TestCase(60, true, 20)]
        [TestCase(300, true, 24)]
        public void The_zerg_is_a_third_of_the_free_frontier_bots_with_a_floor_of_sixteen_only_with_the_schedule_on(int free, bool schedule,
            int heads) =>
            Assert.That(L.ZergTargetHeads(free, schedule), Is.EqualTo(heads));

        // ---- the recruit plan -----------------------------------------------------------------------------------------

        [Test]
        public void A_zerg_recruits_from_the_whole_frontier_beyond_the_link_radius_and_from_other_regions()
        {
            var plan = L.PlanZerg(eRealm.Albion, new[] { C("a", 0), C("b", 12_000), C("c", 40_000, region: 2) }, null, 24);
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.ZergId, Is.Null, "a new zerg");
            Assert.That(plan.LeadForceId, Is.EqualTo("a"));
            Assert.That(plan.Region, Is.EqualTo((ushort)1), "the region with the most free heads");
            Assert.That(plan.Joining, Is.EqualTo(new[] { "b", "c" }), "the lead's region first, then the other region");
        }

        [Test]
        public void It_forms_in_the_region_with_the_most_free_heads_led_by_its_biggest_group()
        {
            var plan = L.PlanZerg(eRealm.Albion,
                new[] { C("a", 0, 8, region: 1), C("x", 0, 6, region: 2), C("y", 500, 8, region: 2), C("z", 900, 4, region: 2) }, null, 24);
            Assert.That(plan.Region, Is.EqualTo((ushort)2));
            Assert.That(plan.LeadForceId, Is.EqualTo("y"));
            Assert.That(plan.Joining, Is.EqualTo(new[] { "z", "x" }), "nearest to the lead first (z 400, x 500); a would take 18 to 26");
        }

        [Test]
        public void Never_another_realm_an_event_a_players_group_a_resting_group_or_a_lone_bot()
        {
            var plan = L.PlanZerg(eRealm.Albion, new[]
            {
                C("a", 0), C("b", 100), C("m", 100, realm: eRealm.Midgard), C("e", 100, inEvent: true), C("p", 100, player: true),
                C("r", 100), C("solo", 100, heads: 1), C("pair", 100, heads: 2),
            }, null, 24, new[] { "r" });
            Assert.That(plan.Joining, Is.EquivalentTo(new[] { "b", "pair" }), "a pair joins a zerg; a lone bot does not");
            Assert.That(L.CanJoinZerg(C("pair", 0, heads: 2)), Is.True);
            Assert.That(L.CanJoinZerg(C("solo", 0, heads: 1)), Is.False);
        }

        [Test]
        public void At_most_twenty_four_heads_and_eight_groups_and_never_under_sixteen()
        {
            var big = L.PlanZerg(eRealm.Albion, Enumerable.Range(0, 6).Select(i => C($"g{i}", i * 100)).ToArray(), null, 24);
            Assert.That(big.Joining, Has.Count.EqualTo(2), "three eights: 24");
            var pairs = L.PlanZerg(eRealm.Albion, Enumerable.Range(0, 12).Select(i => C($"p{i:00}", i * 100, heads: 2)).ToArray(), null, 24);
            Assert.That(pairs.Joining, Has.Count.EqualTo(L.ZergMaxGroups - 1), "eight groups at most");
            var mixed = L.PlanZerg(eRealm.Albion, new[] { C("a", 0, 8), C("b", 100, 8), C("c", 200, 7), C("d", 300, 3) }, null, 20);
            Assert.That(mixed.Joining, Is.EqualTo(new[] { "b", "c" }), "8+8 is under 20; +7 is 23, within 24; then the target is met");
            var over = L.PlanZerg(eRealm.Albion, new[] { C("a", 0, 8), C("b", 100, 8), C("c", 200, 7), C("d", 300, 8) }, null, 24);
            Assert.That(over.Joining, Is.EqualTo(new[] { "b", "c" }), "23 + 8 would be 31: skipped");
            Assert.That(L.PlanZerg(eRealm.Albion, new[] { C("a", 0, 8), C("b", 100, 7) }, null, 16), Is.Null, "15 heads: no zerg");
            Assert.That(L.PlanZerg(eRealm.Albion, new[] { C("a", 0), C("b", 100), C("c", 200) }, null, 0), Is.Null, "no target: no zerg");
        }

        [Test]
        public void An_existing_zerg_is_topped_up_to_its_target_and_nobody_else_leads_it()
        {
            var existing = new L.BattlegroupView("zerg-albion-001", eRealm.Albion, 1, "a", new[] { "a", "b" }, true);
            var plan = L.PlanZerg(eRealm.Albion, new[] { C("a", 0), C("b", 100), C("c", 30_000, 4), C("d", 30_100, 8) }, existing, 20);
            Assert.That(plan.ZergId, Is.EqualTo("zerg-albion-001"));
            Assert.That(plan.LeadForceId, Is.EqualTo("a"));
            Assert.That(plan.Joining, Is.EqualTo(new[] { "c" }), "16 + 4 reaches 20");
            Assert.That(L.PlanZerg(eRealm.Albion, new[] { C("a", 0), C("b", 100), C("c", 200) }, existing, 16), Is.Null, "at its target");
            Assert.That(L.PlanZerg(eRealm.Albion, new[] { C("b", 100), C("c", 200) }, existing, 24), Is.Null, "its lead is gone");
        }

        // ---- the recruit's leave rules ------------------------------------------------------------------------------

        [Test]
        public void A_recruit_on_its_way_is_not_leashed_but_leaves_late_after_fifteen_minutes()
        {
            L.BattlegroupCandidate lead = C("a", 0), far = C("b", 60_000, region: 2);
            Assert.That(L.ZergArrived(far, lead), Is.False);
            Assert.That(L.ZergRecruitLeaveReason(far, lead, 0, long.MaxValue, L.ZergJoinMilliseconds - 1), Is.EqualTo(L.BattlegroupLeave.None),
                "another region, 60,000 off: still on its way");
            Assert.That(L.ZergRecruitLeaveReason(far, lead, 0, long.MaxValue, L.ZergJoinMilliseconds), Is.EqualTo(L.BattlegroupLeave.Late));
            Assert.That(L.BattlegroupLeaveRests(L.BattlegroupLeave.Late), Is.True, "a late recruit rests before it joins again");
            Assert.That(L.ZergRecruitLeaveReason(null, lead, 0, long.MaxValue, 0), Is.EqualTo(L.BattlegroupLeave.Gone));
            Assert.That(L.ZergRecruitLeaveReason(C("b", 0, inEvent: true), lead, 0, long.MaxValue, 0), Is.EqualTo(L.BattlegroupLeave.Event));
            Assert.That(L.ZergRecruitLeaveReason(C("b", 0, player: true), lead, 0, long.MaxValue, 0), Is.EqualTo(L.BattlegroupLeave.Player));
            Assert.That(L.ZergRecruitLeaveReason(far, null, 0, long.MaxValue, 0), Is.EqualTo(L.BattlegroupLeave.LeadGone));
            Assert.That(L.ZergRecruitLeaveReason(far, lead, 0, 10, 10), Is.EqualTo(L.BattlegroupLeave.Stay));
            Assert.That(L.ZergArrived(C("b", 2_900), lead), Is.True, "arrived: within the link radius, in the lead's region");
        }

        [Test]
        public void A_recruit_that_arrives_is_then_held_to_the_leash()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 50_000, region: 2) }, 0, Free(72), false);
            Assert.That(L.ZergOrderOf("c"), Is.Not.Null, "recruited from another region");
            // Ten minutes on the way, 50,000 off in another region: no leash, no region rule.
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 50_000, region: 2) }, 10 * 60_000, Free(72), false);
            Assert.That(changes.Where(change => change.What == "left"), Is.Empty);
            // It arrives, then strays 7,000 off for a minute: the leash.
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 2_000) }, 11 * 60_000, Free(72), false);
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 7_000) }, 12 * 60_000, Free(72), false);
            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 7_000) }, 13 * 60_000, Free(72), false);
            Assert.That(changes.Any(change => change.What == "left" && change.ForceId == "c" && change.Why == "leash"), Is.True,
                string.Join("; ", changes));
        }

        [Test]
        public void A_recruit_that_never_arrives_leaves_late_and_rests()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 50_000, region: 2) }, 0, Free(72), false);
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 50_000, region: 2) }, L.ZergJoinMilliseconds,
                Free(72), false);
            Assert.That(changes.Any(change => change.What == "left" && change.ForceId == "c" && change.Why == "late"), Is.True,
                string.Join("; ", changes));
            Assert.That(L.BattlegroupResting("c", L.ZergJoinMilliseconds + 1), Is.True);
            Assert.That(L.ZergOrderOf("c"), Is.Null);
        }

        // ---- the gates ----------------------------------------------------------------------------------------------

        private static RvrFrontierPoi Spot(string id, float x, float y, RvrSpotKind kind = RvrSpotKind.Chokepoint, bool reserve = false) =>
            new() { Id = id, Label = id, Kind = kind, Region = 200, Position = new Vector3(x, y, 0), Stand = new Vector3(x, y, 0), ReserveZone = reserve };

        [Test]
        public void The_gates_are_each_milegate_then_its_road_the_realms_own_gate_first()
        {
            var spots = new[]
            {
                Spot("e-mmg", 0, 0), Spot("e-amg", 0, 40_000), Spot("e-amg-road", 0, 32_000), Spot("e-crossroads", 0, 20_000),
                Spot("e-far", 0, 100_000), Spot("e-reserve", 0, 1_000, reserve: true), Spot("e-camp", 0, 500, RvrSpotKind.Camp),
            };
            Assert.That(L.ZergAnchors(spots, eRealm.Albion).Select(a => a.SpotId),
                Is.EqualTo(new[] { "e-amg", "e-amg-road", "e-mmg", "e-crossroads" }), "AMG first for Albion; a road is used once");
            Assert.That(L.ZergAnchors(spots, eRealm.Midgard).Select(a => a.SpotId),
                Is.EqualTo(new[] { "e-mmg", "e-crossroads", "e-amg", "e-amg-road" }), "MMG first for Midgard");
            Assert.That(L.ZergAnchors(spots, eRealm.Albion, spot => spot.Id != "e-amg").Select(a => a.SpotId),
                Is.EqualTo(new[] { "e-mmg", "e-crossroads" }), "a spot the checks refused is left out");
            Assert.That(L.ZergAnchors(new[] { Spot("e-crossroads", 0, 0) }, eRealm.Albion), Is.Empty, "no milegate: no gate to hold");
            Assert.That(L.MilegateOf(Spot("emain-amg-road", 0, 0)), Is.Null, "the AMG road is a road");
            Assert.That(L.MilegateOf(Spot("hw-hmg", 0, 0)), Is.EqualTo('H'));
        }

        [Test]
        public void The_catalogs_gates_in_every_frontier()
        {
            RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
            Assert.That(catalog, Is.Not.Null);
            string[] Gates(ushort region, eRealm realm) => L.ZergAnchors(catalog.Frontier(region).Spots, realm).Select(a => a.SpotId).ToArray();
            Assert.That(Gates(200, eRealm.Albion), Is.EqualTo(new[] { "emain-amg", "emain-lane-hastener", "emain-mmg", "emain-crossroads" }),
                "Emain for Albion: AMG <-> MMG, and the roads by them");
            Assert.That(Gates(200, eRealm.Midgard), Is.EqualTo(new[] { "emain-mmg", "emain-crossroads", "emain-amg", "emain-lane-hastener" }));
            Assert.That(Gates(100, eRealm.Albion), Is.EqualTo(new[] { "og-amg", "og-road-hastener" }), "Odin's Gate's one milegate");
            Assert.That(Gates(1, eRealm.Midgard), Is.EqualTo(new[] { "hw-mmg", "hw-wall-hastener", "hw-hmg", "hw-pennine-crossing" }));
        }

        [Test]
        public void The_lead_rolls_on_to_the_next_gate_after_its_hold()
        {
            Assert.That(L.ZergAnchorDue(0, long.MaxValue), Is.False, "not at the gate yet");
            Assert.That(L.ZergAnchorDue(1, L.ZergHoldMilliseconds), Is.False);
            Assert.That(L.ZergAnchorDue(1, L.ZergHoldMilliseconds + 1), Is.True);
            // In region 200 (Emain), from the catalog.
            var three = new[] { C("a", 0, region: 200), C("b", 500, region: 200), C("c", 900, region: 200) };
            L.PulseBattlegroups(three, 0, Free(72), false);
            L.ZergOrder order = L.ZergOrderOf("a");
            Assert.That(order.IsLead, Is.True);
            Assert.That(order.Anchor.SpotId, Is.EqualTo("emain-amg"));
            Assert.That(L.ZergOrderOf("b").IsLead, Is.False);
            L.ReportZergAtAnchor("b", "emain-amg", 1_000);
            L.ReportZergAtAnchor("a", "emain-mmg", 1_000);
            Assert.That(L.PulseBattlegroups(three, 1_000 + L.ZergHoldMilliseconds, Free(72), false).Any(c => c.What == "moved"), Is.False,
                "only the lead's report at its own gate starts the hold");
            L.ReportZergAtAnchor("a", "emain-amg", 2_000);
            var changes = L.PulseBattlegroups(three, 2_000 + L.ZergHoldMilliseconds + 1, Free(72), false);
            Assert.That(changes.Any(c => c.What == "moved"), Is.True);
            Assert.That(L.ZergOrderOf("a").Anchor.SpotId, Is.EqualTo("emain-lane-hastener"));
        }

        // ---- the record beside the battlegroups -------------------------------------------------------------------

        [Test]
        public void Below_the_floor_no_zerg_forms_but_the_battlegroup_still_does()
        {
            var groups = new[] { C("a", 0), C("b", 1_000), C("c", 1_500) };
            var changes = L.PulseBattlegroups(groups, 0, Free(26), false);
            Assert.That(changes.Any(c => c.Id.StartsWith("zerg-", StringComparison.Ordinal)), Is.False, "26 free: a third is 8");
            Assert.That(L.BattlegroupOf("a")?.Id, Does.StartWith("bgrp-albion-"));
            Assert.That(L.ZergOrderOf("a"), Is.Null);
        }

        [Test]
        public void With_the_schedule_on_twenty_free_still_make_a_zerg_of_sixteen()
        {
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 20_000, 4) }, 0, Free(20), true);
            L.BattlegroupView zerg = L.BattlegroupOf("a");
            Assert.That(zerg.IsZerg, Is.True, string.Join("; ", changes));
            Assert.That(zerg.Members, Is.EqualTo(new[] { "a", "b" }), "16 heads: the floor, met by two eights");
            Assert.That(L.ZergOrderOf("c"), Is.Null);
        }

        [Test]
        public void The_zerg_takes_groups_out_of_their_battlegroup()
        {
            var groups = new[] { C("a", 0), C("b", 1_000), C("c", 20_000) };
            L.PulseBattlegroups(groups, 0, Free(26), false);
            Assert.That(L.BattlegroupOf("a")?.IsZerg, Is.False, "first a battlegroup of a and b");
            var changes = L.PulseBattlegroups(groups, 10_000, Free(72), false);
            Assert.That(changes.Any(c => c.What == "ended" && c.Id.StartsWith("bgrp-", StringComparison.Ordinal) && c.Why == "zerg"), Is.True,
                string.Join("; ", changes));
            L.BattlegroupView zerg = L.BattlegroupOf("a");
            Assert.That(zerg.IsZerg, Is.True);
            Assert.That(zerg.Id, Does.StartWith("zerg-albion-"));
            Assert.That(zerg.Members, Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(L.BattlegroupsNow().Count(view => !view.IsZerg), Is.Zero);
            // The groups a zerg holds never link into a battlegroup, and one zerg per realm.
            changes = L.PulseBattlegroups(groups.Append(C("d", 500)).Append(C("e", 600)).ToArray(), 20_000, Free(300), false);
            Assert.That(changes.Any(c => c.What == "joined"), Is.False, "the zerg is at 24: " + string.Join("; ", changes));
            Assert.That(L.BattlegroupsNow().Count(view => view.IsZerg), Is.EqualTo(1));
            Assert.That(L.BattlegroupOf("a").Members, Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(L.BattlegroupOf("d")?.Members, Is.EquivalentTo(new[] { "d", "e" }),
                "d and e, beside the zerg, link with each other, not with the zerg's groups");
        }

        [Test]
        public void Each_realm_has_its_own_zerg()
        {
            var groups = new[]
            {
                C("a", 0), C("b", 100), C("m1", 0, realm: eRealm.Midgard), C("m2", 100, realm: eRealm.Midgard),
            };
            L.PulseBattlegroups(groups, 0, Free(48, 48), false);
            Assert.That(L.BattlegroupOf("a").Id, Does.StartWith("zerg-albion-"));
            Assert.That(L.BattlegroupOf("m1").Id, Does.StartWith("zerg-midgard-"));
            Assert.That(L.BattlegroupOf("a").Members, Does.Not.Contain("m1"));
        }

        [Test]
        public void The_lead_leaving_ends_the_zerg()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 100) }, 0, Free(48), false);
            var changes = L.PulseBattlegroups(new[] { C("a", 0, inEvent: true), C("b", 100) }, 10_000, Free(48), false);
            Assert.That(changes.Any(c => c.What == "ended" && c.Why == "event"), Is.True, string.Join("; ", changes));
            Assert.That(L.ZergOrderOf("b"), Is.Null, "its groups answer the call as warbands");
        }

        [Test]
        public void The_old_pulse_plans_no_zerg()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 100), C("c", 200) }, 0);
            Assert.That(L.BattlegroupOf("a").IsZerg, Is.False);
        }

        // ---- wiring pins --------------------------------------------------------------------------------------------

        [Test]
        public void Only_a_zerg_leader_outside_any_event_takes_a_zerg_turn_after_the_trail()
        {
            string turn = Regex.Replace(Code("bots", "autonomous", "AutonomousWorldBotController.cs"), @"/\*.*?\*/", string.Empty,
                RegexOptions.Singleline);
            Match hook = Regex.Match(turn,
                @"(?m)^[ \t]*if \(!battleActive && committedPlan == null && !carrying && dynamicWarband && _groupDirective\.Leader == bot &&\s*" +
                @"!_rvrSharedEvent && AutonomousRvrEventLayer\.ZergOrderOf\(forceId\) is \{ \} zergOrder && TakeZergTurn\(bot, forceId, zergOrder\)\)\s*" +
                @"return true;");
            Assert.That(hook.Success, Is.True, "the zerg hook, with every guard");
            Assert.That(turn.IndexOf("TakeZergTurn(bot, forceId, zergOrder)", StringComparison.Ordinal),
                Is.GreaterThan(turn.IndexOf("return TrailBattlegroupLead", StringComparison.Ordinal)), "after the step 1 trail");
            Assert.That(turn.IndexOf("TakeZergTurn(bot, forceId, zergOrder)", StringComparison.Ordinal),
                Is.LessThan(turn.IndexOf("bool promised = ApplyChatPromiseRvr(bot);", StringComparison.Ordinal)), "before the warband's own pick");
            string zerg = Code("bots", "autonomous", "AutonomousWorldBotController.Zerg.cs");
            Assert.That(zerg, Does.Not.Contain("TryBeginFasterStableRoute"), "the zerg walks");
            Assert.That(zerg, Does.Contain("AutonomousRvrEventLayer.ReportZergAtAnchor(forceId, anchor.SpotId, now);"));
        }

        [TestCase(true, true, 1, 1, true)]
        [TestCase(true, false, 1, 1, false)]
        [TestCase(false, true, 1, 1, false)]
        [TestCase(false, true, 2, 1, true)]
        [TestCase(false, false, 2, 1, false)]
        public void Who_takes_a_zerg_turn(bool lead, bool anchor, int botRegion, int zergRegion, bool takes) =>
            Assert.That(AutonomousWorldBotController.ZergTurnApplies(lead, anchor, (ushort)botRegion, (ushort)zergRegion), Is.EqualTo(takes));

        [Test]
        public void The_coordinator_counts_the_free_frontier_bots_and_the_schedule()
        {
            string coordinator = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.Battlegroups.cs");
            Assert.That(coordinator, Does.Contain("freeFrontierBotsStandIn ?? FreeFrontierBots(candidates)"));
            Assert.That(coordinator, Does.Contain("AutonomousBotSchedule.CurrentMode != AutonomousBotSchedule.Mode.Off"));
            Assert.That(coordinator, Does.Contain("!warband.InEvent && !warband.HostsPlayer"), "a player's group or an event force is not free");
        }
    }
}
