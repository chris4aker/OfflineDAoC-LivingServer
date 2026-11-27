using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #599 step 1: the battlegroup, one force above groups (a 2-3 group roam outside events). The link plan, the leave
    /// rules, the record's life, the follower's proposal and the trail rule. Unit tests only (the harness case is
    /// UT_RvrFightRehearsal scenario 5); nothing here has been seen in game. Autonomous gamebots only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrBattlegroup599
    {
        [SetUp]
        public void Reset()
        {
            L.ResetForceIntents();
            L.ResetBattlegroups();
        }

        private static L.BattlegroupCandidate C(string id, float x, int heads = 8, eRealm realm = eRealm.Albion, ushort region = 1,
            bool inEvent = false, bool player = false, float y = 0) => new(id, realm, region, x, y, heads, inEvent, player);

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- the link plan --------------------------------------------------------------------------------------------

        [Test]
        public void Two_eights_near_each_other_link_led_by_the_bigger_then_the_lower_id()
        {
            var links = L.PlanBattlegroups(new[] { C("b", 1_800, 6), C("a", 0, 8) }, null);
            Assert.That(links, Has.Count.EqualTo(1));
            Assert.That(links[0].BattlegroupId, Is.Null, "a new battlegroup");
            Assert.That(links[0].LeadForceId, Is.EqualTo("a"), "the bigger group leads");
            Assert.That(links[0].Joining, Is.EqualTo(new[] { "b" }));
            var tie = L.PlanBattlegroups(new[] { C("y", 0, 8), C("x", 1_000, 8) }, null);
            Assert.That(tie[0].LeadForceId, Is.EqualTo("x"), "same size: the lower force id leads");
        }

        [Test]
        public void Never_across_realms_regions_or_beyond_the_link_radius()
        {
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("m", 500, realm: eRealm.Midgard) }, null), Is.Empty, "another realm");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 500, region: 2) }, null), Is.Empty, "another region");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 3_001) }, null), Is.Empty, "beyond 3,000");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 2_200, y: 2_200) }, null), Is.Empty, "3,111 on the diagonal");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 2_000, y: 2_000) }, null), Has.Count.EqualTo(1), "2,828 on the diagonal");
        }

        [Test]
        public void Never_a_duo_an_event_force_or_a_players_group()
        {
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("pair", 500, 2) }, null), Is.Empty, "a duo stays a duo");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("e", 500, inEvent: true) }, null), Is.Empty, "an event force");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("p", 500, player: true) }, null), Is.Empty, "a player's group");
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("t", 500, 3) }, null), Has.Count.EqualTo(1), "three is a group");
        }

        [Test]
        public void At_most_three_groups_and_twenty_four_heads()
        {
            var links = L.PlanBattlegroups(new[] { C("a", 0), C("b", 500), C("c", 1_000), C("d", 1_500) }, null);
            Assert.That(links, Has.Count.EqualTo(1), "the fourth eight has nobody free near it");
            Assert.That(links[0].Joining, Is.EqualTo(new[] { "b", "c" }), "nearest first, three groups");
            var capped = L.PlanBattlegroups(new[] { C("a", 0, 8), C("b", 500, 8), C("c", 1_000, 7), C("d", 1_200, 6) }, null);
            Assert.That(capped[0].Joining.Count + 1, Is.LessThanOrEqualTo(L.BattlegroupMaxGroups));
            int heads = 8 + capped[0].Joining.Sum(id => id switch { "b" => 8, "c" => 7, _ => 6 });
            Assert.That(heads, Is.LessThanOrEqualTo(L.BattlegroupMaxHeads));
            Assert.That(capped.SelectMany(link => link.Joining).Concat(capped.Select(link => link.LeadForceId)).Distinct().Count(),
                Is.EqualTo(capped.Sum(link => link.Joining.Count + 1)), "no group in two battlegroups");
        }

        [Test]
        public void An_existing_battlegroup_is_topped_up_and_its_groups_never_move()
        {
            var existing = new[] { new L.BattlegroupView("bgrp-albion-001", eRealm.Albion, 1, "a", new[] { "a", "b" }) };
            var links = L.PlanBattlegroups(new[] { C("a", 0), C("b", 500), C("c", 2_500), C("d", 2_600, 4) }, existing);
            Assert.That(links[0].BattlegroupId, Is.EqualTo("bgrp-albion-001"));
            Assert.That(links[0].Joining, Is.EqualTo(new[] { "c" }), "the nearest free group tops it up to three");
            Assert.That(links.Any(link => link.Joining.Contains("a") || link.Joining.Contains("b")), Is.False);
            Assert.That(links.Count, Is.EqualTo(1), "d is alone once c is taken");
        }

        // ---- leaving ------------------------------------------------------------------------------------------------

        [Test]
        public void The_leave_rules()
        {
            L.BattlegroupCandidate lead = C("a", 0), near = C("b", 500);
            Assert.That(L.BattlegroupLeaveReason(near, lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.None));
            Assert.That(L.BattlegroupLeaveReason(null, lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.Gone));
            Assert.That(L.BattlegroupLeaveReason(null, null, true, 0, long.MaxValue, 10), Is.EqualTo(L.BattlegroupLeave.LeadGone));
            Assert.That(L.BattlegroupLeaveReason(near, null, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.LeadGone));
            Assert.That(L.BattlegroupLeaveReason(C("b", 500, inEvent: true), lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.Event));
            Assert.That(L.BattlegroupLeaveReason(C("b", 500, player: true), lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.Player));
            Assert.That(L.BattlegroupLeaveReason(C("b", 500, 1), lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.TooSmall));
            Assert.That(L.BattlegroupLeaveReason(C("b", 500, region: 2), lead, false, 0, 1_000, 10), Is.EqualTo(L.BattlegroupLeave.Region));
            Assert.That(L.BattlegroupLeaveReason(near, lead, false, 5, 100_000, 5 + L.BattlegroupLeashMilliseconds - 1), Is.EqualTo(L.BattlegroupLeave.None));
            Assert.That(L.BattlegroupLeaveReason(near, lead, false, 5, 100_000, 5 + L.BattlegroupLeashMilliseconds), Is.EqualTo(L.BattlegroupLeave.Leash));
            Assert.That(L.BattlegroupLeaveReason(near, lead, false, 0, 1_000, 1_000), Is.EqualTo(L.BattlegroupLeave.Stay));
            Assert.That(L.BattlegroupLeaveReason(lead, lead, true, 0, long.MaxValue, 10), Is.EqualTo(L.BattlegroupLeave.None), "the lead has no stay");
        }

        [Test]
        public void The_leash_counts_from_the_first_time_beyond_it()
        {
            Assert.That(L.NextFarSince(0, L.BattlegroupLeashRadius + 1, 100), Is.EqualTo(100));
            Assert.That(L.NextFarSince(100, L.BattlegroupLeashRadius + 1, 200), Is.EqualTo(100));
            Assert.That(L.NextFarSince(100, L.BattlegroupLeashRadius, 300), Is.Zero, "back within it: the count starts over");
        }

        [Test]
        public void A_stay_is_fifteen_to_forty_minutes_and_repeats()
        {
            for (int i = 0; i < 500; i++)
            {
                long stay = L.BattlegroupStay($"albion-x-{i:000}", i * 7_919L);
                Assert.That(stay, Is.InRange(15 * 60_000L, 40 * 60_000L));
                Assert.That(L.BattlegroupStay($"albion-x-{i:000}", i * 7_919L), Is.EqualTo(stay));
            }
        }

        // ---- the record ---------------------------------------------------------------------------------------------

        [Test]
        public void A_battlegroup_forms_takes_a_third_loses_a_straggler_and_ends()
        {
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_800) }, 1_000);
            Assert.That(changes.Select(c => c.What), Is.EqualTo(new[] { "formed" }));
            string id = changes[0].Id;
            Assert.That(id, Does.StartWith("bgrp-albion-"));
            Assert.That(L.BattlegroupLeadOf("b"), Is.EqualTo("a"));
            Assert.That(L.BattlegroupLeadOf("a"), Is.Null, "the lead follows nobody");
            Assert.That(L.BattlegroupOf("a").Members, Is.EqualTo(new[] { "a", "b" }));

            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_800), C("c", 2_900, 5) }, 11_000);
            Assert.That(changes.Single().What, Is.EqualTo("joined"));
            Assert.That(L.BattlegroupOf("c").Id, Is.EqualTo(id));

            // c strays beyond the leash: a minute later it leaves; the other two go on.
            Assert.That(L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_800), C("c", 7_000, 5) }, 21_000), Is.Empty);
            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_800), C("c", 7_000, 5) }, 21_000 + L.BattlegroupLeashMilliseconds);
            Assert.That(changes.Single(), Is.EqualTo(new L.BattlegroupChange("left", id, "c", "leash")));
            Assert.That(L.BattlegroupOf("c"), Is.Null);

            // b joins a keep event: it leaves, and a battlegroup of one ends.
            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_800, inEvent: true) }, 100_000);
            Assert.That(changes.Select(c => (c.What, c.Why)), Is.EqualTo(new[] { ("left", "event"), ("ended", "toosmall") }));
            Assert.That(L.BattlegroupsNow(), Is.Empty);
            Assert.That(L.BattlegroupLeadOf("b"), Is.Null);
        }

        [Test]
        public void The_lead_leaving_ends_the_battlegroup()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 2_000) }, 1_000);
            var changes = L.PulseBattlegroups(new[] { C("b", 1_000), C("c", 2_000) }, 11_000);
            Assert.That(changes[0], Is.EqualTo(new L.BattlegroupChange("ended", changes[0].Id, "a", "leadgone")));
            Assert.That(changes.Skip(1).Single().What, Is.EqualTo("formed"), "the two left may link again, b leading");
            Assert.That(L.BattlegroupLeadOf("c"), Is.EqualTo("b"));
        }

        [Test]
        public void A_follower_leaves_when_its_stay_runs_out()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, 0);
            long stay = L.BattlegroupStay("b", 0);
            Assert.That(L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, stay - 1), Is.Empty);
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, stay);
            Assert.That(changes[0], Is.EqualTo(new L.BattlegroupChange("left", changes[0].Id, "b", "stay")));
        }

        // ---- the follower's proposal and trail ----------------------------------------------------------------------

        [Test]
        public void A_follower_proposes_its_leads_intent_and_it_is_written_once()
        {
            var turn = new L.ForceTurn();
            turn.Begin("b", writer: true);
            L.ForceIntent decided = turn.Propose(new(L.Intent.Roam, false),
                new(L.IntentSource.Battlegroup, L.Intent.HuntEnemy, "rvr-enemy-7", false, null, "battlegroup.follow", "trailing a"), 10);
            Assert.That(decided.Intent, Is.EqualTo(L.Intent.HuntEnemy));
            Assert.That(decided.ObjectiveId, Is.EqualTo("rvr-enemy-7"));
            Assert.That(decided.Source, Is.EqualTo(L.IntentSource.Battlegroup));
            Assert.That(decided.Shared, Is.False, "a battlegroup is not an event");
            Assert.That(turn.End(null, 10), Is.True);
            turn.Begin("b", writer: true);
            turn.Propose(new(L.Intent.HuntEnemy, false),
                new(L.IntentSource.Battlegroup, L.Intent.HuntEnemy, "rvr-enemy-7", false, null, "battlegroup.follow", "trailing a"), 20);
            Assert.That(turn.End(null, 20), Is.False, "the same decision writes nothing");
            Assert.That(L.ForceIntentWrites("b"), Is.EqualTo(1));
        }

        [TestCase(1_000f, false, true)]
        [TestCase(901f, false, true)]
        [TestCase(700f, false, false)]
        [TestCase(700f, true, true)]
        [TestCase(500f, true, false)]
        [TestCase(100f, false, false)]
        public void The_trail_walks_beyond_nine_hundred_and_stops_inside_five_hundred(float distance, bool moving, bool walks) =>
            Assert.That(AutonomousWorldBotController.BattlegroupTrailWalks(distance, moving), Is.EqualTo(walks));

        // ---- wiring pins --------------------------------------------------------------------------------------------

        [Test]
        public void Only_a_follower_groups_leader_trails_and_only_outside_an_event()
        {
            string turn = Code("bots", "autonomous", "AutonomousWorldBotController.cs");
            Match hook = Regex.Match(turn, @"if \(([^;]*?BattlegroupLeadOf\(forceId\)[^;]*?)\)\s*return TrailBattlegroupLead", RegexOptions.Singleline);
            Assert.That(hook.Success, Is.True, "the trail hook");
            foreach (string guard in new[] { "!battleActive", "committedPlan == null", "!carrying", "dynamicWarband", "_groupDirective.Leader == bot" })
                Assert.That(hook.Groups[1].Value, Does.Contain(guard));
            Assert.That(turn.IndexOf("return TrailBattlegroupLead", StringComparison.Ordinal),
                Is.GreaterThan(turn.IndexOf("return FollowDynamicGroupLeader(bot, _groupDirective);", StringComparison.Ordinal)),
                "after the in-group follow and after the target pick");
        }

        [Test]
        public void The_battlegroups_are_linked_before_the_rosters_and_an_event_roster_wins()
        {
            string coordinator = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int link = coordinator.IndexOf("PulseBattlegroups(now);", StringComparison.Ordinal);
            Assert.That(link, Is.GreaterThan(0));
            Assert.That(coordinator.IndexOf("RealmWarbandSupport.Pulse(now);", StringComparison.Ordinal), Is.GreaterThan(link));
            string support = Code("bots", "autonomous", "RealmWarbandSupport.cs");
            Assert.That(support, Does.Contain("!groups.ContainsKey(group)"), "a group already in an event's roster keeps it");
            Assert.That(support.IndexOf("BattlegroupsNow()", StringComparison.Ordinal),
                Is.GreaterThan(support.IndexOf("ForceTargets()", StringComparison.Ordinal)), "the event rosters are built first");
        }
    }
}
