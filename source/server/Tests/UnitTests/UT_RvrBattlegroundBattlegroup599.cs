using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #599 step 4: a realm's battleground parties at one keep act as one battlegroup. The wave rule, the plan, the leave
    /// reasons, the record's life and the wiring pins. Unit tests only (the harness case is UT_RvrFightRehearsal scenario 7);
    /// nothing here has been seen in game. Autonomous gamebots only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrBattlegroundBattlegroup599
    {
        private const string Keep = "bg-keep-12", OtherKeep = "bg-keep-13";

        [SetUp]
        public void Reset()
        {
            L.ResetForceIntents();
            L.ResetBattlegroundBattlegroups();
        }

        private static L.BattlegroundPartyCandidate P(string id, int heads = 7, eRealm realm = eRealm.Albion, ushort region = 250,
            string objective = Keep, bool player = false) => new(id, realm, region, objective, heads, player);

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        // ---- the wave rule ------------------------------------------------------------------------------------------------

        [Test]
        public void Mustering_or_attacking_a_battleground_keep_while_fresh_is_the_wave()
        {
            L.IntentView view = new(L.Intent.Roam, false);
            L.ForceIntent Order(bool held, bool tooFew, bool mustering, long part = 1) =>
                L.Decide("bg:p", view, null, L.DecideBattleground(12, held, tooFew, mustering, part).Proposal, 1);
            Assert.Multiple(() =>
            {
                Assert.That(L.InBattlegroundWave(Order(false, false, true), 100_000, 100_000), Is.True, "mustering");
                Assert.That(L.InBattlegroundWave(Order(false, false, false), 100_000, 130_000), Is.True, "attacking, 30 s old");
                Assert.That(L.InBattlegroundWave(Order(false, false, false), 100_000, 130_001), Is.False, "stale");
                Assert.That(L.InBattlegroundWave(Order(true, false, false, 3), 100_000, 100_000), Is.False, "holding the walls");
                Assert.That(L.InBattlegroundWave(Order(true, false, false, 1), 100_000, 100_000), Is.False, "roaming the held keep's field");
                Assert.That(L.InBattlegroundWave(Order(false, true, false), 100_000, 100_000), Is.False, "too few: roaming");
                Assert.That(L.InBattlegroundWave(null, 100_000, 100_000), Is.False);
                var frontier = new L.ForceIntent("bg:p", L.Intent.AssaultKeep, "rvr-keep-5", L.ForceRole.Attacker, "-", 1,
                    L.IntentSource.Plan, "plan", null, false, -1);
                Assert.That(L.InBattlegroundWave(frontier, 100_000, 100_000), Is.False, "a frontier keep is not a battleground wave");
            });
        }

        [Test]
        public void The_wave_objective_is_read_from_the_partys_own_record()
        {
            L.IntentView view = new(L.Intent.Roam, false);
            L.WriteForceIntent("bg:bgmuster-albion-x-001", null,
                L.Decide("bg:bgmuster-albion-x-001", view, null, L.DecideBattleground(12, false, false, true, 1).Proposal, 1), null, 100_000);
            Assert.That(L.BattlegroundWaveObjective("bgmuster-albion-x-001", 110_000), Is.EqualTo(Keep));
            Assert.That(L.BattlegroundWaveObjective("bgmuster-albion-x-001", 140_000), Is.Null, "stale");
            Assert.That(L.BattlegroundWaveObjective("bgmuster-albion-x-002", 110_000), Is.Null, "no record");
            Assert.That(L.BattlegroundWaveObjective(null, 110_000), Is.Null);
        }

        // ---- the plan -----------------------------------------------------------------------------------------------------

        [Test]
        public void Two_parties_at_one_keep_are_one_battlegroup_led_by_the_bigger_then_the_lower_id()
        {
            var plan = L.PlanBattlegroundBattlegroups(new[] { P("b", 5), P("a", 7) });
            Assert.That(plan, Has.Count.EqualTo(1));
            Assert.That(plan[0].Id, Is.EqualTo("bgbg-albion-12"));
            Assert.That(plan[0].LeadPartyId, Is.EqualTo("a"));
            Assert.That(plan[0].Members, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(plan[0].ObjectiveId, Is.EqualTo(Keep));
            var tie = L.PlanBattlegroundBattlegroups(new[] { P("y", 7), P("x", 7), P("z", 8) });
            Assert.That(tie[0].Members, Is.EqualTo(new[] { "z", "x", "y" }), "the biggest leads; then by size, then id");
            Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("y", 7), P("x", 7) })[0].LeadPartyId, Is.EqualTo("x"), "same size: the lower id");
        }

        [Test]
        public void No_cap_the_realms_whole_wave_at_its_keep_is_one_battlegroup()
        {
            var plan = L.PlanBattlegroundBattlegroups(Enumerable.Range(1, 6).Select(i => P($"p{i}", 8)).ToArray());
            Assert.That(plan, Has.Count.EqualTo(1));
            Assert.That(plan[0].Members, Has.Count.EqualTo(6));
        }

        [Test]
        public void One_party_another_realm_keep_or_region_and_parties_out_of_the_wave_are_kept_apart()
        {
            Assert.Multiple(() =>
            {
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a") }), Is.Empty, "one party");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("m", realm: eRealm.Midgard) }), Is.Empty, "another realm");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", objective: OtherKeep) }), Is.Empty, "another keep");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", region: 251) }), Is.Empty, "another region");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", player: true) }), Is.Empty, "a party hosting a player");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", heads: 1) }), Is.Empty, "a lone bot");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", objective: null) }), Is.Empty, "out of the wave");
                Assert.That(L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b", objective: "rvr-keep-5") }), Is.Empty, "a frontier keep");
                var both = L.PlanBattlegroundBattlegroups(new[] { P("a"), P("b"), P("m", realm: eRealm.Midgard), P("n", realm: eRealm.Midgard) });
                Assert.That(both.Select(v => v.Id), Is.EqualTo(new[] { "bgbg-albion-12", "bgbg-midgard-12" }), "one per realm at the keep");
            });
        }

        [Test]
        public void An_existing_lead_is_kept_while_it_is_in()
        {
            var leads = new System.Collections.Generic.Dictionary<string, string> { ["bgbg-albion-12"] = "small" };
            var plan = L.PlanBattlegroundBattlegroups(new[] { P("small", 3), P("big", 8) }, leads);
            Assert.That(plan[0].LeadPartyId, Is.EqualTo("small"));
            Assert.That(plan[0].Members, Is.EqualTo(new[] { "small", "big" }));
            var gone = L.PlanBattlegroundBattlegroups(new[] { P("big", 8), P("other", 4) }, leads);
            Assert.That(gone[0].LeadPartyId, Is.EqualTo("big"), "the held lead left: the biggest takes over");
        }

        [Test]
        public void The_leave_reasons()
        {
            var view = new L.BattlegroundBattlegroupView("bgbg-albion-12", eRealm.Albion, 250, Keep, "a", new[] { "a", "b" });
            Assert.Multiple(() =>
            {
                Assert.That(L.BattlegroundPartyLeaveReason(null, view), Is.EqualTo("gone"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b", player: true), view), Is.EqualTo("player"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b", heads: 1), view), Is.EqualTo("toosmall"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b", region: 251), view), Is.EqualTo("region"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b", objective: null), view), Is.EqualTo("wave"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b", objective: OtherKeep), view), Is.EqualTo("wave"));
                Assert.That(L.BattlegroundPartyLeaveReason(P("b"), view), Is.Null, "still in");
            });
        }

        // ---- the record ---------------------------------------------------------------------------------------------------

        [Test]
        public void The_record_forms_joins_hands_the_lead_over_and_ends()
        {
            var formed = L.PulseBattlegroundBattlegroups(new[] { P("a", 7), P("b", 5) }, 10_000);
            Assert.That(formed.Select(c => (c.What, c.Id, c.ForceId)), Is.EqualTo(new[] { ("formed", "bgbg-albion-12", "a,b") }));
            Assert.That(L.BattlegroundBattlegroupOf("b")?.LeadPartyId, Is.EqualTo("a"));

            var same = L.PulseBattlegroundBattlegroups(new[] { P("a", 7), P("b", 8) }, 20_000);
            Assert.That(same, Is.Empty, "the same parties: no change, and the lead holds though b grew");

            var joined = L.PulseBattlegroundBattlegroups(new[] { P("a", 7), P("b", 8), P("c", 3) }, 30_000);
            Assert.That(joined.Select(c => (c.What, c.ForceId)), Is.EqualTo(new[] { ("joined", "c") }));

            var lead = L.PulseBattlegroundBattlegroups(new[] { P("b", 8), P("c", 3) }, 40_000);
            Assert.That(lead.Select(c => (c.What, c.ForceId, c.Why)), Is.EqualTo(new[] { ("left", "a", "gone"), ("lead", "b", "gone") }));
            Assert.That(L.BattlegroundBattlegroupOf("c")?.LeadPartyId, Is.EqualTo("b"));
            Assert.That(L.BattlegroundBattlegroupOf("a"), Is.Null);

            var wave = L.PulseBattlegroundBattlegroups(new[] { P("b", 8), P("c", 3, objective: null) }, 50_000);
            Assert.That(wave.Select(c => (c.What, c.ForceId, c.Why)),
                Is.EqualTo(new[] { ("left", "c", "wave"), ("ended", "b", "toosmall") }), "down to one party: it ends");
            Assert.That(L.BattlegroundBattlegroupsNow(), Is.Empty);
            Assert.That(L.BattlegroundBattlegroupOf("b"), Is.Null);
        }

        [Test]
        public void A_player_joining_a_party_takes_it_out()
        {
            L.PulseBattlegroundBattlegroups(new[] { P("a", 7), P("b", 5), P("c", 4) }, 10_000);
            var changes = L.PulseBattlegroundBattlegroups(new[] { P("a", 7), P("b", 5, player: true), P("c", 4) }, 20_000);
            Assert.That(changes.Select(c => (c.What, c.ForceId, c.Why)), Is.EqualTo(new[] { ("left", "b", "player") }));
            Assert.That(L.BattlegroundBattlegroupOf("a").Members, Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void The_id_is_stable_per_realm_and_keep()
        {
            L.PulseBattlegroundBattlegroups(new[] { P("a"), P("b") }, 10_000);
            L.PulseBattlegroundBattlegroups(Array.Empty<L.BattlegroundPartyCandidate>(), 20_000);
            L.PulseBattlegroundBattlegroups(new[] { P("c"), P("d") }, 30_000);
            Assert.That(L.BattlegroundBattlegroupOf("c")?.Id, Is.EqualTo("bgbg-albion-12"));
        }

        // ---- the wiring ---------------------------------------------------------------------------------------------------

        [Test]
        public void A_battleground_roster_answers_only_for_battleground_bots_and_every_other_only_for_rvr_ones()
        {
            Assert.That(RealmWarbandSupport.ObjectiveOf(new RealmWarbandSupport.Roster(new object(), Array.Empty<GameLiving>(), true)),
                Is.EqualTo(eAutonomousObjectiveKind.Battleground));
            Assert.That(RealmWarbandSupport.ObjectiveOf(new RealmWarbandSupport.Roster(new object(), Array.Empty<GameLiving>())),
                Is.EqualTo(eAutonomousObjectiveKind.RvR));
            string support = Code("bots", "autonomous", "RealmWarbandSupport.cs");
            Assert.That(support, Does.Contain("AutonomousObjectiveAssignments.Is(bot, ObjectiveOf(roster))"), "Get checks the roster's kind");
            Assert.That(support, Does.Contain("BattlegroundBattlegroupsNow()"));
            Assert.That(support, Does.Contain("new Roster(scope, members, Battleground: true)"));
            Assert.That(support.IndexOf("BattlegroundBattlegroupsNow()", StringComparison.Ordinal),
                Is.GreaterThan(support.IndexOf("AutonomousRvrEventLayer.BattlegroupsNow()", StringComparison.Ordinal)),
                "after the event rosters and the frontier battlegroups, which keep their groups");
        }

        [Test]
        public void The_coordinator_pulses_it_before_the_rosters_from_battleground_trips_only()
        {
            string pulse = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int battleground = pulse.IndexOf("PulseBattlegroundBattlegroups(now);", StringComparison.Ordinal);
            Assert.That(battleground, Is.GreaterThan(0));
            Assert.That(battleground, Is.LessThan(pulse.IndexOf("RealmWarbandSupport.Pulse(now);", StringComparison.Ordinal)));
            string feed = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.BattlegroundBattlegroups.cs");
            Assert.That(feed, Does.Contain("in Trips"));
            Assert.That(feed, Does.Not.Contain("Sessions"), "frontier warbands keep their own battlegroups");
            Assert.That(feed, Does.Contain("member is GamePlayer"), "a party a player joined is passed as such");
        }
    }
}
