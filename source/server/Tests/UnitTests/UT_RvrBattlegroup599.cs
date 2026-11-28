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

        [Test]
        public void A_group_whose_stay_ran_out_does_not_link_straight_back()
        {
            // Review of #789: b still stands 1,000 from a when its stay ends; before the cooldown the same pulse linked it
            // back under a new id (left, ended, formed), so the group never went its own way.
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, 0);
            long stay = L.BattlegroupStay("b", 0);
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, stay);
            Assert.That(changes.Select(c => (c.What, c.Why)), Is.EqualTo(new[] { ("left", "stay"), ("ended", "toosmall") }),
                "no formed row in the same pulse");
            Assert.That(L.BattlegroupLeadOf("b"), Is.Null);
            Assert.That(L.BattlegroupResting("b", stay), Is.True);
            long end = stay + L.BattlegroupRelinkCooldownMilliseconds;
            for (long now = stay + 10_000; now < end; now += 60_000)
            {
                Assert.That(L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000) }, now)
                    .Where(c => c.What is "formed" or "joined"), Is.Empty, $"no link during the cooldown (t={now})");
                Assert.That(L.BattlegroupLeadOf("b"), Is.Null);
            }
            // a may still link with another group meanwhile; b is not taken.
            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 1_500) }, end - 1);
            Assert.That(changes.Single(), Is.EqualTo(new L.BattlegroupChange("formed", changes[0].Id, "c", "near")));
            Assert.That(L.BattlegroupOf("b"), Is.Null);
            // After the cooldown b may link again.
            changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 1_500) }, end);
            Assert.That(changes.Single().What, Is.EqualTo("joined"));
            Assert.That(L.BattlegroupLeadOf("b"), Is.EqualTo("a"));
            Assert.That(L.BattlegroupResting("b", end), Is.False);
        }

        [Test]
        public void A_stuck_trail_leaves_and_rests_and_only_a_follower_can_report_it()
        {
            L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 2_000) }, 0);
            L.ReportBattlegroupStuck("a");      // the lead: ignored
            L.ReportBattlegroupStuck("zz");     // in no battlegroup: ignored
            L.ReportBattlegroupStuck("b");
            var changes = L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 2_000) }, 10_000);
            Assert.That(changes.Single(), Is.EqualTo(new L.BattlegroupChange("left", changes[0].Id, "b", "stuck")));
            Assert.That(L.BattlegroupOf("a").Members, Is.EqualTo(new[] { "a", "c" }));
            Assert.That(L.BattlegroupResting("b", 10_000), Is.True);
            Assert.That(L.PulseBattlegroups(new[] { C("a", 0), C("b", 1_000), C("c", 2_000) }, 20_000), Is.Empty,
                "a report is used once, and b rests");
        }

        [Test]
        public void Only_a_stay_a_stuck_trail_a_late_or_left_out_zerg_recruit_rests_and_a_resting_group_is_never_planned()
        {
            // Cap, Defense and Level: a zerg follower its event left out (#816 review).
            foreach (L.BattlegroupLeave reason in Enum.GetValues<L.BattlegroupLeave>())
                Assert.That(L.BattlegroupLeaveRests(reason),
                    Is.EqualTo(reason is L.BattlegroupLeave.Stay or L.BattlegroupLeave.Stuck or L.BattlegroupLeave.Late or L.BattlegroupLeave.Cap
                        or L.BattlegroupLeave.Defense or L.BattlegroupLeave.Level),
                    reason.ToString());
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 1_000) }, null, new[] { "b" }), Is.Empty);
            Assert.That(L.PlanBattlegroups(new[] { C("a", 0), C("b", 1_000) }, null, new[] { "a" }), Is.Empty, "nor lead one");
            L.BattlegroupCandidate a = C("a", 0), b = C("b", 1_000);
            Assert.That(L.BattlegroupLeaveReason(b, a, false, 0, long.MaxValue, 0, stuck: true), Is.EqualTo(L.BattlegroupLeave.Stuck));
            Assert.That(L.BattlegroupLeaveReason(a, a, true, 0, long.MaxValue, 0, stuck: true), Is.EqualTo(L.BattlegroupLeave.None),
                "the lead never leaves its own battlegroup as stuck");
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

        [Test]
        public void The_trail_holds_its_goal_until_the_lead_has_gone_fifteen_hundred_or_it_is_reached()
        {
            var held = new System.Numerics.Vector2(0, 0);
            var trailer = new System.Numerics.Vector2(-3_000, 0);
            Assert.That(AutonomousWorldBotController.BattlegroupTrailRetarget(null, new(100, 0), trailer), Is.True, "no goal yet");
            // A running lead moving a few hundred every turn does not move the goal (the route keeps one goal).
            for (float x = 0; x <= 1_500; x += 250)
                Assert.That(AutonomousWorldBotController.BattlegroupTrailRetarget(held, new(x, 0), trailer), Is.False, $"lead at {x}");
            Assert.That(AutonomousWorldBotController.BattlegroupTrailRetarget(held, new(1_501, 0), trailer), Is.True, "past 1,500");
            Assert.That(AutonomousWorldBotController.BattlegroupTrailRetarget(held, new(1_000, 0), new(-400, 0)), Is.True,
                "the trailer reached the goal");
        }

        [Test]
        public void A_trailer_with_no_progress_for_fifty_seconds_is_stuck()
        {
            Assert.That(AutonomousWorldBotController.BattlegroupTrailStuck(0, 1_000_000), Is.False, "no clock yet");
            Assert.That(AutonomousWorldBotController.BattlegroupTrailStuck(10_000, 59_999), Is.False);
            Assert.That(AutonomousWorldBotController.BattlegroupTrailStuck(10_000, 60_000), Is.True);
            Assert.That(AutonomousWorldBotController.BattlegroupTrailStuckMilliseconds, Is.InRange(45_000L, 60_000L));
            Assert.That(AutonomousWorldBotController.BattlegroupTrailStuckMilliseconds,
                Is.LessThan(L.BattlegroupLeashMilliseconds), "stuck is caught before the leash");
        }

        [Test]
        public void A_refused_trail_path_is_asked_again_only_after_the_back_off()
        {
            Assert.That(AutonomousWorldBotController.BattlegroupTrailMayAskPath(0, 1_000_000), Is.True, "nothing refused yet");
            Assert.That(AutonomousWorldBotController.BattlegroupTrailMayAskPath(10_000, 14_999), Is.False);
            Assert.That(AutonomousWorldBotController.BattlegroupTrailMayAskPath(10_000, 15_000), Is.True);
            Assert.That(AutonomousWorldBotController.BattlegroupTrailPathBackoffMilliseconds,
                Is.LessThan(AutonomousWorldBotController.BattlegroupTrailStuckMilliseconds), "several asks before the stuck report");
        }

        [Test]
        public void The_trail_keeps_one_goal_for_the_route_and_reports_a_stuck_trail()
        {
            string trail = Regex.Replace(Code("bots", "autonomous", "AutonomousWorldBotController.Battlegroup.cs"), @"\s+", " ");
            Assert.Multiple(() =>
            {
                Assert.That(trail, Does.Contain("if (BattlegroupTrailRetarget(_battlegroupTrailGoal is { } held ? new Vector2(held.X, held.Y) : null, new(lead.X, lead.Y), new(bot.X, bot.Y))) _battlegroupTrailGoal = new Vector3(lead.X, lead.Y, lead.Z);"));
                Assert.That(trail, Does.Contain("if (BattlegroupTrailMayAskPath(_battlegroupTrailRefusedTick, now)) { if (!IssueVariedRvrPath(bot, _battlegroupTrailGoal.Value)) { _battlegroupTrailGoal = null; _battlegroupTrailRefusedTick = now; } else _battlegroupTrailRefusedTick = 0; }"),
                    "the held goal goes to the route, a refused path is not ignored, and the next ask waits out the back-off");
                Assert.That(Regex.Matches(trail, @"IssueVariedRvrPath\(").Count, Is.EqualTo(1), "never the lead's live spot");
                Assert.That(trail, Does.Contain("if (BattlegroupTrailStuck(_battlegroupTrailProgressTick, now)) { AutonomousRvrEventLayer.ReportBattlegroupStuck(forceId);"));
            });
        }

        // ---- wiring pins --------------------------------------------------------------------------------------------

        [Test]
        public void Only_a_follower_groups_leader_trails_and_only_outside_an_event()
        {
            string turn = Code("bots", "autonomous", "AutonomousWorldBotController.cs");
            // The exact guard, as live code: an added "false &&", a dropped guard or a commented-out hook fails this.
            string live = Regex.Replace(turn, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            Match hook = Regex.Match(live,
                @"(?m)^[ \t]*if \(!battleActive && committedPlan == null && !carrying && dynamicWarband && _groupDirective\.Leader == bot &&\s*" +
                @"AutonomousRvrEventLayer\.BattlegroupLeadOf\(forceId\) is \{ \} battlegroupLead &&\s*" +
                @"AutonomousBotGroupCoordinator\.BattlegroupLeader\(battlegroupLead\) is \{ IsAlive: true \} battlegroupLeader &&\s*" +
                @"battlegroupLeader != bot && battlegroupLeader\.CurrentRegionID == bot\.CurrentRegionID\)\s*" +
                @"return TrailBattlegroupLead\(bot, forceId, battlegroupLead, battlegroupLeader\);");
            Assert.That(hook.Success, Is.True, "the trail hook, with every guard");
            Assert.That(turn.IndexOf("return TrailBattlegroupLead", StringComparison.Ordinal),
                Is.GreaterThan(turn.IndexOf("return FollowDynamicGroupLeader(bot, _groupDirective);", StringComparison.Ordinal)),
                "after the in-group follow and after the target pick");
        }

        [Test]
        public void The_battlegroups_are_linked_before_the_rosters_and_an_event_roster_wins()
        {
            // The live call, as a statement in the coordinator's pulse just before the rosters: commenting it out (// or
            // /* */) fails this (the rehearsal calls PulseBattlegroups(force: true) itself, so it cannot catch that).
            string coordinator = Regex.Replace(Code("bots", "autonomous", "AutonomousBotGroupCoordinator.cs"), @"/\*.*?\*/", string.Empty,
                RegexOptions.Singleline);
            Assert.That(Regex.IsMatch(coordinator,
                @"(?m)^[ \t]*using \(AiProfile\.Scope\(AiSystem\.PulseWarbandSupport\)\)[ \t]*\n[ \t]*\{[ \t]*\n(?:[ \t]*//[^\n]*\n)*" +
                @"[ \t]*PulseBattlegroups\(now\);[ \t]*\n[ \t]*RealmWarbandSupport\.Pulse\(now\);"), Is.True,
                "the battlegroups are linked in the pulse, then the rosters");
            string support = Code("bots", "autonomous", "RealmWarbandSupport.cs");
            Assert.That(support, Does.Contain("!groups.ContainsKey(group)"), "a group already in an event's roster keeps it");
            Assert.That(support.IndexOf("BattlegroupsNow()", StringComparison.Ordinal),
                Is.GreaterThan(support.IndexOf("ForceTargets()", StringComparison.Ordinal)), "the event rosters are built first");
        }
    }
}
