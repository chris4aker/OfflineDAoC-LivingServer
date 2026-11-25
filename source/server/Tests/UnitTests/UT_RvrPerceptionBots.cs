using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 1 ("Eyes", docs/RVR-FIELD-1.2.md): enemy gamebots and their pets are noticed by the
    /// player rule; one look per enemy party and its mates within 1,500 noticed with it; the observer cap per enemy
    /// party; two lookers per group; the scan radius away from an objective; "at objective" by position; minimal
    /// stealth detection; the scan budget line. Pure rules, plus world-less gamebots (DutyLeaseTestKit). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrPerceptionBots : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly RvrAwarenessProfile Normal = AutonomousRvrPerception.Profile(RvrBotAwareness.Normal);

        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        private static RvrSighting At(double distance) => new(distance, false, false, 1, false, false);

        private static RvrPartyMember Member(long key, double distance, int x, int y = 0) => new(key, At(distance), x, y, 0);

        private static List<RvrMemberOutcome> ScanParty(AutonomousRvrPerception.Memory memory,
            AutonomousRvrPerception.ObserverBudget budget, long bot, long party, long now, double roll, int delay,
            bool mayAnswerCall = true, params RvrPartyMember[] members)
        {
            var results = new List<RvrMemberOutcome>();
            AutonomousRvrPerception.ScanParty(memory, budget, bot, 1, party, members, now, Normal, 1_125, roll, () => delay,
                mayAnswerCall, results);
            return results;
        }

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden)!.GetValue(group)!;
            foreach (GameLiving member in members)
            {
                list.Add(member);
                member.Group = group;
            }
            return group;
        }

        // ---- kinds ---------------------------------------------------------------------------------------------------

        [Test]
        public void An_enemy_gamebot_is_its_own_kind_and_counts_as_before_when_its_rule_is_off()
        {
            Bot enemy = Gamebot(realm: eRealm.Midgard, objective: "RvR");
            Assert.That(AutonomousWorldBotController.PerceivedKind(enemy), Is.EqualTo(RvrPerceivedKind.EnemyBot));
            Assert.That(AutonomousWorldBotController.PerceivedKind(Companion()), Is.EqualTo(RvrPerceivedKind.PlayerCompanion));
            Assert.That(AutonomousRvrPerception.IsBotKind(RvrPerceivedKind.EnemyBot), Is.True);
            Assert.That(AutonomousRvrPerception.IsBotKind(RvrPerceivedKind.EnemyBotPet), Is.True);
            Assert.That(AutonomousRvrPerception.IsBotKind(RvrPerceivedKind.Player), Is.False);

            foreach (RvrPerceivedKind kind in new[] { RvrPerceivedKind.EnemyBot, RvrPerceivedKind.EnemyBotPet })
            {
                Assert.That(AutonomousRvrPerception.Effective(kind, perceiveBots: true), Is.EqualTo(kind));
                Assert.That(AutonomousRvrPerception.Effective(kind, perceiveBots: false), Is.EqualTo(RvrPerceivedKind.Other));
                Assert.That(AutonomousRvrPerception.Applies(true, kind, false, false), Is.True, "noticed by chance");
                // The retaliation bypass stays: a bot fired on, or whose group is attacked, fights back at once.
                Assert.That(AutonomousRvrPerception.Applies(true, kind, firingOnRealm: true, attackingGroup: false), Is.False);
                Assert.That(AutonomousRvrPerception.Applies(true, kind, firingOnRealm: false, attackingGroup: true), Is.False);
            }
            // Players are perceived whatever the bots' switch says.
            Assert.That(AutonomousRvrPerception.Effective(RvrPerceivedKind.Player, perceiveBots: false), Is.EqualTo(RvrPerceivedKind.Player));
            Assert.That(AutonomousRvrPerception.Effective(RvrPerceivedKind.Other, perceiveBots: true), Is.EqualTo(RvrPerceivedKind.Other));
        }

        // ---- the party key -------------------------------------------------------------------------------------------

        [Test]
        public void A_party_key_names_the_group_and_never_collides_with_a_target_key()
        {
            long target = (300L << 32) | uint.MaxValue;
            Assert.That(RvrPartyKey.Compose(target, null), Is.EqualTo(target), "a soloer is its own party");
            long group = RvrPartyKey.Compose(target, 1);
            Assert.That(group, Is.Not.EqualTo(target));
            Assert.That(RvrPartyKey.IsGroup(group), Is.True);
            Assert.That(RvrPartyKey.IsGroup(target), Is.False);
            Assert.That(RvrPartyKey.Compose(5, 1), Is.EqualTo(group), "the group decides, not the member");
            Assert.That(RvrPartyKey.Compose(5, 2), Is.Not.EqualTo(group));
            Assert.That(RvrPartyKey.Label(group), Is.EqualTo("group-1"));
            Assert.That(RvrPartyKey.Label(target), Is.EqualTo("solo"));

            Bot a = Gamebot(realm: eRealm.Midgard), b = Gamebot(realm: eRealm.Midgard), c = Gamebot(realm: eRealm.Midgard);
            GroupOf(a, b);
            Assert.That(RvrPartyKey.Of(a), Is.EqualTo(RvrPartyKey.Of(b)));
            Assert.That(RvrPartyKey.Of(c), Is.Not.EqualTo(RvrPartyKey.Of(a)));
            Assert.That(RvrPartyKey.IsGroup(RvrPartyKey.Of(a)), Is.True);
            Assert.That(RvrPartyKey.Of(c), Is.EqualTo(RvrPartyKey.TargetKey(c)));
        }

        // ---- one look per party ----------------------------------------------------------------------------------------

        [Test]
        public void Noticing_one_member_notices_its_mates_within_1500_and_not_the_others()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            // The bot at the origin; the party at 2,000-2,400 along x, one straggler 1,800 beyond the first.
            List<RvrMemberOutcome> r = ScanParty(memory, budget, 1, 900, 10_000, roll: 0, delay: 700, members: new[]
            {
                Member(11, 2_000, 2_000), Member(12, 2_400, 2_400), Member(13, 3_400, 3_800, 0),
            });
            Assert.That(r.Select(o => o.Result), Is.EqualTo(new[] { RvrScanResult.Noticed, RvrScanResult.Noticed, RvrScanResult.Unseen }));
            Assert.That(r[0].Factors & RvrNoticeFactors.Party, Is.EqualTo(RvrNoticeFactors.None), "the one it saw");
            Assert.That(r[1].Factors & RvrNoticeFactors.Party, Is.EqualTo(RvrNoticeFactors.Party), "noticed with it");
            Assert.That(r[0].DelayMilliseconds, Is.EqualTo(700));
            Assert.That(r[1].DelayMilliseconds, Is.EqualTo(700), "one reaction for the group");
            Assert.That(budget.IsFull(900, 1, 10_000, 1), Is.True, "one slot for the party");
            Assert.That(budget.IsFull(900, 1, 10_000, 2), Is.False, "only one");
            Assert.That(memory.WatchingParty(1, 900, 10_500), Is.True);
            Assert.That(memory.HasNews(1, 10_500), Is.True);

            // Next scan: the two engage, and the straggler, now 1,200 from a known mate, is noticed with them.
            r = ScanParty(memory, budget, 1, 900, 11_200, roll: 0.999, delay: 600, members: new[]
            {
                Member(11, 2_000, 2_000), Member(12, 2_400, 2_400), Member(13, 3_400, 3_600, 0),
            });
            Assert.That(r.Select(o => o.Result), Is.EqualTo(new[] { RvrScanResult.Engage, RvrScanResult.Engage, RvrScanResult.Noticed }));
            Assert.That(r[2].Factors, Is.EqualTo(RvrNoticeFactors.Party));
        }

        [Test]
        public void A_party_is_rolled_once_for_its_most_noticeable_member()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            double near = AutonomousRvrPerception.NoticeChance(At(2_000), Normal, 1_125, out _);
            double far = AutonomousRvrPerception.NoticeChance(At(3_000), Normal, 1_125, out _);
            Assert.That(near, Is.GreaterThan(far));
            // A roll between the two chances notices the near one only if it is the one rolled for.
            double roll = (near + far) / 2;
            List<RvrMemberOutcome> r = ScanParty(memory, budget, 1, 901, 5_000, roll, 900, members: new[]
            {
                Member(21, 3_000, 3_000), Member(22, 2_000, 2_000, 1_700),
            });
            Assert.That(r[1].Result, Is.EqualTo(RvrScanResult.Noticed));
            Assert.That(r[1].Chance, Is.EqualTo(near).Within(1e-9));
            Assert.That(r[0].Result, Is.EqualTo(RvrScanResult.Unseen), "more than 1,500 from the one noticed");

            // Eight unnoticed members are not eight rolls: a roll above the best chance sees none of them.
            var memory2 = new AutonomousRvrPerception.Memory();
            var eight = Enumerable.Range(0, 8).Select(i => Member(30 + i, 2_000 + i * 10, 2_000 + i * 10)).ToArray();
            r = ScanParty(memory2, new AutonomousRvrPerception.ObserverBudget(), 1, 902, 5_000, roll: near + 0.01, 900, members: eight);
            Assert.That(r.All(o => o.Result == RvrScanResult.Unseen), Is.True);
        }

        [Test]
        public void The_observer_cap_counts_lookers_per_enemy_party_and_within_1000_needs_no_slot()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            // Three bots look at the party (different members): the fourth is capped for every member of it.
            for (long bot = 1; bot <= 3; bot++)
                ScanParty(memory, budget, bot, 903, 20_000, roll: 0.999, 900, members: new[] { Member(40 + bot, 2_800, 2_800) });
            List<RvrMemberOutcome> r = ScanParty(memory, budget, 4, 903, 20_100, roll: 0, 900, members: new[]
            {
                Member(41, 2_800, 2_800), Member(49, 2_900, 2_900),
            });
            Assert.That(r.All(o => o.Result == RvrScanResult.Capped), Is.True);
            // Another party has its own slots, and a member within 1,000 is always noticed, cap or not.
            Assert.That(ScanParty(memory, budget, 4, 904, 20_100, roll: 0, 900, members: new[] { Member(50, 2_800, 2_800) })[0].Result,
                Is.EqualTo(RvrScanResult.Noticed));
            r = ScanParty(memory, budget, 5, 903, 20_200, roll: 0.999, 900, members: new[] { Member(41, 900, 900), Member(42, 2_300, 2_300) });
            Assert.That(r[0].Result, Is.EqualTo(RvrScanResult.Noticed));
            Assert.That(r[0].Factors & RvrNoticeFactors.Close, Is.EqualTo(RvrNoticeFactors.Close));
            Assert.That(r[1].Result, Is.EqualTo(RvrScanResult.Noticed), "its mate 1,400 away comes with it");
        }

        [Test]
        public void A_bot_watching_a_party_casts_rays_at_its_mates_even_when_the_cap_is_full()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            for (long bot = 1; bot <= 3; bot++)
                budget.TryClaim(905, 1, 30_000, Normal.ObserverCap);
            Assert.That(AutonomousRvrPerception.SkipsLineOfSight(memory, budget, 7, 1, 60, 905, 30_100, 2_500, Normal), Is.True,
                "cap full: no ray");
            memory.WatchParty(7, 905, 30_050);
            Assert.That(AutonomousRvrPerception.SkipsLineOfSight(memory, budget, 7, 1, 60, 905, 30_100, 2_500, Normal), Is.False,
                "it is watching that group");
            Assert.That(AutonomousRvrPerception.SkipsLineOfSight(memory, budget, 7, 1, 60, 905, 30_100, 3_600, Normal), Is.True,
                "beyond the outer range");
            Assert.That(AutonomousRvrPerception.SkipsLineOfSight(memory, budget, 7, 1, 60, 905, 30_050 + 15_001, 2_500, Normal), Is.False,
                "the cap is a new second's");
        }

        [Test]
        public void A_call_held_by_duty_is_dropped_and_the_party_rule_matches_the_single_scan()
        {
            var memory = new AutonomousRvrPerception.Memory();
            var budget = new AutonomousRvrPerception.ObserverBudget();
            memory.Alert(5, 70, 1_000, 300);
            List<RvrMemberOutcome> r = ScanParty(memory, budget, 5, 906, 2_000, roll: 0.999, 900, mayAnswerCall: false,
                members: new[] { Member(70, 2_800, 2_800), Member(71, 2_900, 2_900) });
            Assert.That(r.All(o => o.Result == RvrScanResult.Unseen), Is.True, "a raider looks for itself; no mate noticed with a dropped call");

            // A party of one is the old single-target scan.
            var m1 = new AutonomousRvrPerception.Memory();
            var m2 = new AutonomousRvrPerception.Memory();
            var b1 = new AutonomousRvrPerception.ObserverBudget();
            var b2 = new AutonomousRvrPerception.ObserverBudget();
            var random = new Random(7);
            for (int scan = 0; scan < 200; scan++)
            {
                long now = 1_000 + scan * 1_125L;
                double roll = random.NextDouble(), distance = 1_200 + random.Next(2_400);
                RvrScanOutcome single = AutonomousRvrPerception.Scan(m1, b1, scan % 5, 1, 80, now, At(distance), Normal, 1_125, roll,
                    () => 700, true);
                RvrMemberOutcome party = ScanParty(m2, b2, scan % 5, 80, now, roll, 700, members: new[] { Member(80, distance, (int)distance) })[0];
                Assert.That(party.Result, Is.EqualTo(single.Result), $"scan {scan}");
                Assert.That(party.Chance, Is.EqualTo(single.Chance).Within(1e-12));
            }
        }

        // ---- the scan radius -----------------------------------------------------------------------------------------

        [Test]
        public void Away_from_an_objective_a_bot_scans_to_where_noticing_stops_never_less_than_2600()
        {
            Assert.That(AutonomousRvrPerception.ScanRadius(Normal, true, 5_000), Is.EqualTo(3_500));
            Assert.That(AutonomousRvrPerception.ScanRadius(AutonomousRvrPerception.Profile(RvrBotAwareness.Easy), true, 5_000), Is.EqualTo(2_600));
            Assert.That(AutonomousRvrPerception.ScanRadius(AutonomousRvrPerception.Profile(RvrBotAwareness.Veteran), true, 5_000), Is.EqualTo(4_500));
            Assert.That(AutonomousRvrPerception.ScanRadius(Normal, false, 5_000), Is.EqualTo(5_000), "at an objective or rule off: as before");
            // About half the area of the old 5,000-unit scan on normal.
            Assert.That(3_500.0 * 3_500 / (5_000.0 * 5_000), Is.EqualTo(0.49).Within(0.01));
        }

        [Test]
        public void The_field_rules_need_the_player_rule_the_master_switch_and_the_scope()
        {
            RvrTacticsSwitches on = RvrTacticsSwitches.AllOn;
            Assert.That(RvrFieldRules.For(true, true, on), Is.EqualTo(new RvrFieldRules(true, true, true, true)));
            Assert.That(RvrFieldRules.For(false, true, on), Is.EqualTo(RvrFieldRules.None), "rvr_bot_perception off");
            Assert.That(RvrFieldRules.For(true, false, on), Is.EqualTo(RvrFieldRules.None), "out of scope (raid, keep, player group)");
            Assert.That(RvrFieldRules.For(true, true, RvrTacticsSwitches.AllOff), Is.EqualTo(RvrFieldRules.None));
            Assert.That(RvrFieldRules.For(true, true, on with { PerceptionBots = false }), Is.EqualTo(new RvrFieldRules(false, true, false, false)));
            Assert.That(RvrFieldRules.For(true, true, on with { GroupLookers = false }), Is.EqualTo(new RvrFieldRules(true, false, true, true)));
        }

        // ---- lookers -------------------------------------------------------------------------------------------------

        [Test]
        public void Two_members_of_a_group_look_at_a_time_and_the_turn_goes_round()
        {
            long[] group = { 105, 101, 104, 102, 103 };
            var seen = new HashSet<long>();
            for (long now = 0; now < 60_000; now += 1_000)
            {
                long[] lookers = RvrScanRoster.Lookers(group, now);
                Assert.That(lookers, Has.Length.EqualTo(2));
                Assert.That(lookers.Distinct().Count(), Is.EqualTo(2));
                Assert.That(RvrScanRoster.Lookers(group.OrderByDescending(id => id).ToArray(), now), Is.EquivalentTo(lookers), "every member works out the same pair");
                Assert.That(RvrScanRoster.Lookers(group, now - now % RvrScanRoster.RotationMilliseconds), Is.EquivalentTo(lookers), "held for 10 s");
                Assert.That(group.Count(id => RvrScanRoster.IsLooker(id, group, now)), Is.EqualTo(2));
                seen.UnionWith(lookers);
            }
            Assert.That(seen, Is.EquivalentTo(group), "everyone takes a turn within a minute");
            Assert.That(RvrScanRoster.Lookers(new long[] { 7, 8 }, 0), Is.EquivalentTo(new long[] { 7, 8 }), "a pair: both look");
            Assert.That(RvrScanRoster.Lookers(new long[] { 7, 7, 8, 9 }, 0), Has.Length.EqualTo(2), "duplicates ignored");
        }

        [Test]
        public void A_non_looker_scans_only_when_woken()
        {
            Assert.That(RvrScanRoster.Decide(false, 8, false, false, false, false), Is.EqualTo(RvrScanRole.Everyone), "rule off");
            Assert.That(RvrScanRoster.Decide(true, 1, false, false, false, false), Is.EqualTo(RvrScanRole.Solo));
            Assert.That(RvrScanRoster.Decide(true, 2, false, false, false, false), Is.EqualTo(RvrScanRole.Solo), "a duo looks for itself");
            Assert.That(RvrScanRoster.Decide(true, 8, true, false, false, false), Is.EqualTo(RvrScanRole.Looker));
            Assert.That(RvrScanRoster.Decide(true, 8, false, false, false, false), Is.EqualTo(RvrScanRole.Idle));
            Assert.That(RvrScanRoster.Decide(true, 8, false, true, false, false), Is.EqualTo(RvrScanRole.Woken), "a mate's call");
            Assert.That(RvrScanRoster.Decide(true, 8, false, false, true, false), Is.EqualTo(RvrScanRole.Woken), "a mate is fighting");
            Assert.That(RvrScanRoster.Decide(true, 8, false, false, false, true), Is.EqualTo(RvrScanRole.Woken), "the plan's target");
            Assert.That(RvrScanRoster.Scans(RvrScanRole.Idle), Is.False);
            Assert.That(new[] { RvrScanRole.Everyone, RvrScanRole.Solo, RvrScanRole.Looker, RvrScanRole.Woken }.All(RvrScanRoster.Scans), Is.True);

            // A spotter's call is news to the mate it reaches, for 8 s: long enough for a relayed call and a slow reaction.
            var memory = new AutonomousRvrPerception.Memory();
            Assert.That(memory.HasNews(9, 0), Is.False);
            memory.Alert(9, 77, 1_000, 500);
            Assert.That(memory.HasNews(9, 1_000 + AutonomousRvrPerception.NewsMilliseconds), Is.True);
            Assert.That(memory.HasNews(9, 1_001 + AutonomousRvrPerception.NewsMilliseconds), Is.False, "then it goes back to idle");
        }

        // ---- at objective, by position ----------------------------------------------------------------------------------

        [Test]
        public void At_objective_is_where_the_bot_is_not_what_its_warband_plans()
        {
            var keep = new RvrObjectivePoint(1, 0, 0);
            Assert.That(RvrAtObjective.EngagementRadius, Is.EqualTo(9_500), "the siege's own physically-there radius");
            Assert.That(RvrAtObjective.Within(false, false, 1, 0, 0, keep), Is.False, "not committed");
            Assert.That(RvrAtObjective.Within(true, false, 1, 9_000, 0, keep), Is.True, "at the siege camp ring");
            Assert.That(RvrAtObjective.Within(true, false, 1, 6_000, 7_000, keep), Is.True);
            Assert.That(RvrAtObjective.Within(true, false, 1, 9_600, 0, keep), Is.False, "still marching");
            Assert.That(RvrAtObjective.Within(true, false, 2, 0, 0, keep), Is.False, "another region");
            Assert.That(RvrAtObjective.Within(true, false, 1, 50_000, 0, null), Is.True, "no known point: today's rule");
            Assert.That(RvrAtObjective.Within(false, true, 1, 50_000, 0, keep), Is.True, "a siege engine operator");

            Bot marcher = Gamebot(objective: "RvR");
            marcher.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.AssaultKeep);
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "force-wp1", DutyLeaseReason.SiegeParty);
            Assert.That(RvrTacticsScope.Refusal(marcher), Is.EqualTo(RvrScopeRefusal.AtObjective), "intent and no point: as before");
            RvrAtObjective.Remember(marcher, 1, 80_000, 0, known: true);
            marcher.TestX = 0;
            Assert.That(RvrAtObjective.Of(marcher), Is.False);
            Assert.That(RvrTacticsScope.Allows(marcher), Is.True, "a column 80,000 from its keep is in the open field");
            marcher.TestX = 71_000;
            Assert.That(RvrTacticsScope.Refusal(marcher), Is.EqualTo(RvrScopeRefusal.AtObjective), "9,000 from the keep: at it");
            marcher.Region = 100;
            Assert.That(RvrTacticsScope.Allows(marcher), Is.True, "in another frontier");
            RvrAtObjective.Remember(marcher, 0, 0, 0, known: false);
            Assert.That(RvrTacticsScope.Refusal(marcher), Is.EqualTo(RvrScopeRefusal.AtObjective), "point cleared");

            Bot defender = Gamebot(objective: "RvR");
            defender.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.DefendEvent);
            RvrAtObjective.Remember(defender, 1, 30_000, 0, known: true);
            Assert.That(RvrAtObjective.Of(defender), Is.False, "defenders on the road to their keep too");
            defender.TestX = 25_000;
            Assert.That(RvrAtObjective.Of(defender), Is.True);

            Bot roamer = Gamebot(objective: "RvR");
            roamer.TempProperties.SetProperty("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.Roam);
            RvrAtObjective.Remember(roamer, 1, 0, 0, known: true);
            Assert.That(RvrAtObjective.Of(roamer), Is.False, "a roamer is never at an objective");
        }

        [Test]
        public void A_march_column_is_in_scope_until_it_is_9500_from_its_keep()
        {
            // A column of 40 attackers marching 84,000 units (the lane survey's median straight-line distance of a main
            // route) to its keep, sampled every 1,000 units: the share of bot-steps the new rule brings into scope.
            var keep = new RvrObjectivePoint(1, 0, 0);
            int inScope = 0, total = 0;
            for (int x = 84_000; x >= 0; x -= 1_000)
                for (int bot = 0; bot < 40; bot++)
                {
                    total++;
                    if (!RvrAtObjective.Within(true, false, 1, x + bot * 20, 0, keep))
                        inScope++;
                }
            double share = inScope / (double)total;
            TestContext.Out.WriteLine($"march of 84,000: {share:P0} of bot-steps in the open-field scope (was 0%)");
            Assert.That(share, Is.EqualTo(0.88).Within(0.02));
        }

        // ---- stealth -------------------------------------------------------------------------------------------------

        private static RvrStealthSighting Stealther(double distance, int level = 50, int spec = 50) => new(distance, level, spec);

        [Test]
        public void A_stealther_is_noticed_only_close_up_and_never_beyond_what_a_player_could_see()
        {
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(0), RvrBotAwareness.Normal), Is.EqualTo(0.5));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(0), RvrBotAwareness.Veteran), Is.EqualTo(0.6));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(0), RvrBotAwareness.Easy), Is.EqualTo(0.35));
            // Level 50 against stealth 50: a player sees 125 units, so the bot never beyond that.
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0)), Is.EqualTo(125));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(124), RvrBotAwareness.Normal), Is.GreaterThan(0.14));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(126), RvrBotAwareness.Normal), Is.Zero);
            // Level 50 against stealth 35: a player sees 425; the bot stops at 350.
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0, spec: 35)), Is.EqualTo(425));
            Assert.That(RvrStealthNotice.Limit(Stealther(0, spec: 35)), Is.EqualTo(350));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(237.5, spec: 35), RvrBotAwareness.Normal), Is.EqualTo(0.075).Within(0.001));
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(351, spec: 35), RvrBotAwareness.Normal), Is.Zero);
            // Never in a fight with someone else, never without line of sight, never through Vanish.
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(10) with { ObserverInCombatWithOther = true }, RvrBotAwareness.Normal), Is.Zero);
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(10) with { LineOfSight = false }, RvrBotAwareness.Normal), Is.Zero);
            Assert.That(RvrStealthNotice.ChancePerSecond(Stealther(10) with { Vanish = true }, RvrBotAwareness.Normal), Is.Zero);
            // Detect Hidden widens what a player (and so a bot) could see, unless the stealther has it or Camouflage.
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0) with { ObserverDetectHidden = true }), Is.EqualTo(250));
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0) with { ObserverDetectHidden = true, Camouflage = true }), Is.EqualTo(125));
            // See Hidden at its 1.62 strength (#522, StealthRules.SeeHiddenRange): 1,620 before the cut, 45% of it now.
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0, spec: 30) with { ObserverSeeHidden = true }), Is.EqualTo(729));
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0, spec: 30) with { ObserverSeeHidden = true, TargetAssassin = true }), Is.EqualTo(525));
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0, spec: 5) with { ObserverSeeHidden = true }), Is.EqualTo(1_764));
            Assert.That(RvrStealthNotice.HumanRange(Stealther(0, spec: 5) with { ObserverDetectHidden = true }), Is.EqualTo(1_900), "the server's hard cap");

            // Over many random cases: a chance only where a player in the bot's place sees the stealther, falling with distance.
            var random = new Random(5);
            for (int i = 0; i < 5_000; i++)
            {
                var s = new RvrStealthSighting(random.Next(0, 2_000), random.Next(1, 51), random.Next(0, 51),
                    random.Next(4) == 0, random.Next(4) == 0, random.Next(10) == 0, random.Next(2) == 0, random.Next(10) == 0,
                    random.Next(20) == 0);
                double chance = RvrStealthNotice.ChancePerSecond(s, RvrBotAwareness.Veteran);
                if (chance > 0)
                    Assert.That(s.Distance, Is.LessThanOrEqualTo(Math.Min(RvrStealthNotice.HumanRange(s), RvrStealthNotice.MaxRange)));
                Assert.That(chance, Is.LessThanOrEqualTo(0.6));
                Assert.That(RvrStealthNotice.ChancePerSecond(s with { Distance = s.Distance + 25 }, RvrBotAwareness.Veteran),
                    Is.LessThanOrEqualTo(chance));
            }
        }

        [Test]
        public void The_stealth_roll_follows_the_scan_clock_and_a_noticed_stealther_is_engaged_after_the_delay()
        {
            double perScan = RvrStealthNotice.ChancePerScan(Stealther(0), RvrBotAwareness.Normal, 1_000);
            Assert.That(perScan, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(RvrStealthNotice.ChancePerScan(Stealther(0), RvrBotAwareness.Normal, 2_000), Is.EqualTo(0.75).Within(1e-9));

            var memory = new AutonomousRvrPerception.Memory();
            Assert.That(AutonomousRvrPerception.ScanStealthed(memory, 1, 90, 1_000, perScan, 0.6, () => 800).Result, Is.EqualTo(RvrScanResult.Unseen));
            RvrScanOutcome seen = AutonomousRvrPerception.ScanStealthed(memory, 1, 90, 2_000, perScan, 0.1, () => 800);
            Assert.That(seen.Result, Is.EqualTo(RvrScanResult.Noticed));
            Assert.That(seen.Factors, Is.EqualTo(RvrNoticeFactors.Stealth));
            Assert.That(AutonomousRvrPerception.ScanStealthed(memory, 1, 90, 2_500, perScan, 0.99, () => 800).Result, Is.EqualTo(RvrScanResult.Reacting));
            Assert.That(AutonomousRvrPerception.ScanStealthed(memory, 1, 90, 2_900, perScan, 0.99, () => 800).Result, Is.EqualTo(RvrScanResult.Engage));
            // A mate's call about a stealther is not something it can see.
            memory.Alert(2, 90, 3_000, 0);
            Assert.That(AutonomousRvrPerception.ScanStealthed(memory, 2, 90, 3_100, 0, 0, () => 800).Result, Is.EqualTo(RvrScanResult.Unseen));

            // Median time to notice at point blank, normal, one scan a second: about 1 s (50% a second).
            var random = new Random(11);
            var times = new List<double>();
            for (int trial = 0; trial < 4_000; trial++)
            {
                double t = random.NextDouble();
                while (random.NextDouble() >= perScan)
                    t += 1;
                times.Add(t);
            }
            times.Sort();
            TestContext.Out.WriteLine($"stealther at point blank, normal: median {times[times.Count / 2]:0.00} s");
            Assert.That(times[times.Count / 2], Is.InRange(0.5, 1.6));
        }

        // ---- the scan budget line -------------------------------------------------------------------------------------

        [Test]
        public void The_scan_budget_is_logged_once_a_minute_per_realm_that_scanned()
        {
            RvrScanBudget.Reset();
            try
            {
                Assert.That(RvrScanBudget.TakeLines(1_000), Is.Null, "the first call starts the minute");
                RvrScanBudget.Add(eRealm.Albion, RvrScanBudget.Counter.Scans, 600);
                RvrScanBudget.Add(eRealm.Albion, RvrScanRole.Looker);
                RvrScanBudget.Add(eRealm.Albion, RvrScanRole.Idle);
                RvrScanBudget.Add(eRealm.Albion, RvrScanBudget.Counter.Rays, 1_200);
                RvrScanBudget.Add(eRealm.Albion, RvrScanBudget.Counter.Rolls, 30);
                Assert.That(RvrScanBudget.TakeLines(30_000), Is.Null, "not a minute yet");
                List<string> lines = RvrScanBudget.TakeLines(61_000);
                Assert.That(lines, Has.Count.EqualTo(1), "only Albion scanned");
                Assert.That(lines[0], Does.StartWith("RVR_SCAN_BUDGET realm=Albion seconds=60 scans=600 scansPerSecond=10 lookers=1 "));
                Assert.That(lines[0], Does.Contain(" idle=1 rolls=30 capped=0 rays=1200 raysPerSecond=20 "));
                Assert.That(RvrScanBudget.TakeLines(61_001), Is.Null, "one line a minute");
                Assert.That(RvrScanBudget.Read(eRealm.Albion, RvrScanBudget.Counter.Scans), Is.Zero, "counts restart");
            }
            finally
            {
                RvrScanBudget.Reset();
            }
        }
    }
}
