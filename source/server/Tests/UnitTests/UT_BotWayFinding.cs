using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 4 (docs/BOTS-AS-PEOPLE-1.2.md, item 4, "way finding"): a gamebot on a trip to a place it
    /// does not know knows the way anyway, asks it, tags along or takes a wrong road, by its skill tier; owner decision 7's
    /// limits (once a trip, 3 minutes, no frontier, dungeon, stable ride or monster far above; one ask in /faction per realm
    /// in 10 minutes); answers only from bots that know the place; the Travel scene's questions said only as real ones.
    /// Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotWayFinding
    {
        private BotPeopleStore _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = BotPeopleStore.UseForTests(new BotPeopleStore(() => null));
            BotPlaceKnowledge.ResetForTests();
            BotWayFinding.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            BotPeopleStore.UseForTests(_previous);
            BotPlaceKnowledge.ResetForTests();
            BotWayFinding.ResetForTests();
        }

        private static readonly RvrSkillTier[] Tiers = [RvrSkillTier.Casual, RvrSkillTier.Average, RvrSkillTier.Veteran];

        private static Dictionary<BotWayChoice, double> Draw(BotPlaceKnowing knowing, RvrSkillTier tier, bool ask = true, bool wrong = true,
            BotWayGroup group = default, int draws = 100_000)
        {
            var counts = Enum.GetValues<BotWayChoice>().ToDictionary(choice => choice, _ => 0.0);
            for (int i = 0; i < draws; i++)
                counts[BotWayFinding.Choose(knowing, tier, group, ask, wrong, (i + 0.5) / draws)]++;
            return counts.ToDictionary(pair => pair.Key, pair => 100.0 * pair.Value / draws);
        }

        // ---- the choice by tier ----------------------------------------------------------------------------------------

        [TestCase(RvrSkillTier.Casual, 40, 30, 15, 15)]
        [TestCase(RvrSkillTier.Average, 55, 25, 12, 8)]
        [TestCase(RvrSkillTier.Veteran, 75, 15, 8, 2)]
        public void An_unknown_place_is_known_asked_followed_or_lost_by_the_design_shares_of_the_tier(RvrSkillTier tier, int knows, int ask,
            int follow, int wrong)
        {
            Assert.That(BotWayFinding.Shares(tier, BotPlaceKnowing.Unknown, true, true), Is.EqualTo(new BotWayShares(knows, ask, follow, wrong)));
            Dictionary<BotWayChoice, double> drawn = Draw(BotPlaceKnowing.Unknown, tier);
            Assert.That(drawn[BotWayChoice.Knows], Is.EqualTo(knows).Within(0.1));
            Assert.That(drawn[BotWayChoice.Ask], Is.EqualTo(ask).Within(0.1));
            Assert.That(drawn[BotWayChoice.Follow], Is.EqualTo(follow).Within(0.1));
            Assert.That(drawn[BotWayChoice.WrongRoad], Is.EqualTo(wrong).Within(0.1));
        }

        [Test]
        public void Over_many_bots_and_trips_the_tiers_mix_gives_each_choice_its_share()
        {
            // RVR-FIELD-1.2's 30/50/20 over 10,000 bots (the WP1 roll), one random trip each.
            var random = new Random(7);
            var tiers = new Dictionary<RvrSkillTier, int>();
            int wrong = 0, ask = 0;
            for (long id = 1; id <= 10_000; id++)
            {
                RvrSkillTier tier = BotWayFinding.TierOf(BotPersonKey.Character(id), RvrBotAwareness.Normal);
                tiers[tier] = tiers.GetValueOrDefault(tier) + 1;
                BotWayChoice choice = BotWayFinding.Choose(BotPlaceKnowing.Unknown, tier, BotWayGroup.Solo, true, true, random.NextDouble());
                wrong += choice == BotWayChoice.WrongRoad ? 1 : 0;
                ask += choice == BotWayChoice.Ask ? 1 : 0;
            }
            Assert.That(tiers[RvrSkillTier.Casual] / 100.0, Is.EqualTo(30).Within(2));
            Assert.That(tiers[RvrSkillTier.Average] / 100.0, Is.EqualTo(50).Within(2));
            Assert.That(tiers[RvrSkillTier.Veteran] / 100.0, Is.EqualTo(20).Within(2));
            // 0.3*15 + 0.5*8 + 0.2*2 = 8.9%; 0.3*30 + 0.5*25 + 0.2*15 = 24.5%.
            Assert.That(wrong / 100.0, Is.EqualTo(8.9).Within(1.2));
            Assert.That(ask / 100.0, Is.EqualTo(24.5).Within(1.5));
        }

        [Test]
        public void A_known_place_is_always_known_and_a_half_known_one_asks_no_more_and_halves_the_wrong_road()
        {
            foreach (RvrSkillTier tier in Tiers)
            {
                Assert.That(Draw(BotPlaceKnowing.Known, tier)[BotWayChoice.Knows], Is.EqualTo(100));
                BotWayShares unknown = BotWayFinding.Shares(tier, BotPlaceKnowing.Unknown, true, true);
                BotWayShares half = BotWayFinding.Shares(tier, BotPlaceKnowing.HalfKnown, true, true);
                Assert.That(half.Ask, Is.Zero, "told the way, it does not ask again");
                Assert.That(half.WrongRoad, Is.EqualTo(unknown.WrongRoad / 2));
                Assert.That(half.Follow, Is.EqualTo(unknown.Follow));
                Assert.That(half.Total, Is.EqualTo(100));
                Assert.That(Draw(BotPlaceKnowing.HalfKnown, tier)[BotWayChoice.WrongRoad], Is.EqualTo(half.WrongRoad).Within(0.1));
            }
        }

        [Test]
        public void A_switch_that_is_off_gives_its_shares_to_knowing_the_way()
        {
            foreach (RvrSkillTier tier in Tiers)
            {
                Dictionary<BotWayChoice, double> noAsk = Draw(BotPlaceKnowing.Unknown, tier, ask: false);
                Assert.That(noAsk[BotWayChoice.Ask] + noAsk[BotWayChoice.Follow], Is.Zero, "bot_ask_the_way also covers tagging along");
                Assert.That(noAsk[BotWayChoice.WrongRoad], Is.GreaterThan(0));
                Dictionary<BotWayChoice, double> noWrong = Draw(BotPlaceKnowing.Unknown, tier, wrong: false);
                Assert.That(noWrong[BotWayChoice.WrongRoad], Is.Zero);
                Dictionary<BotWayChoice, double> neither = Draw(BotPlaceKnowing.Unknown, tier, false, false);
                Assert.That(neither[BotWayChoice.Knows], Is.EqualTo(100));
            }
            // The master switch turns both off (WP0's switches).
            BotPeopleSwitches off = BotPeopleSwitches.From(true, true, true, true, false, true, true, true, true, true, true, true);
            Assert.That(off.AskTheWay || off.WrongRoads, Is.False);
        }

        [Test]
        public void A_group_takes_its_leaders_way_and_a_leader_asks_only_when_no_member_knows()
        {
            foreach (RvrSkillTier tier in Tiers)
            {
                Assert.That(Draw(BotPlaceKnowing.Unknown, tier, group: new BotWayGroup(true, false, false))[BotWayChoice.Knows], Is.EqualTo(100),
                    "a member follows its leader");
                Assert.That(Draw(BotPlaceKnowing.Unknown, tier, group: new BotWayGroup(true, true, true))[BotWayChoice.Knows], Is.EqualTo(100),
                    "a member knows the way");
                Assert.That(Draw(BotPlaceKnowing.Unknown, tier, group: new BotWayGroup(true, true, false))[BotWayChoice.Ask], Is.EqualTo(100),
                    "nobody knows: the leader asks");
                Assert.That(Draw(BotPlaceKnowing.Unknown, tier, ask: false, group: new BotWayGroup(true, true, false))[BotWayChoice.Knows],
                    Is.EqualTo(100));
                Assert.That(Draw(BotPlaceKnowing.Unknown, tier, group: new BotWayGroup(true, true, false))[BotWayChoice.WrongRoad], Is.Zero,
                    "no group takes a wrong road");
            }
        }

        // ---- the wrong road: the caps and every exclusion ----------------------------------------------------------------

        private static BotWrongRoadFacts Free => new(true, false, false, false, false, false, false, 100, false, true);

        [Test]
        public void A_free_walking_solo_bot_may_take_a_wrong_road_and_every_exclusion_stops_it()
        {
            Assert.That(BotWayFinding.WrongRoadRefusal(Free), Is.EqualTo(BotWrongRoadRefusal.None));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { SwitchOn = false }), Is.EqualTo(BotWrongRoadRefusal.SwitchedOff));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { UsedThisTrip = true }), Is.EqualTo(BotWrongRoadRefusal.UsedThisTrip), "once a trip");
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { StableRide = true }), Is.EqualTo(BotWrongRoadRefusal.StableRide));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { InGroup = true }), Is.EqualTo(BotWrongRoadRefusal.Grouped));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { InFrontier = true }), Is.EqualTo(BotWrongRoadRefusal.Frontier));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { InDungeon = true }), Is.EqualTo(BotWrongRoadRefusal.Dungeon));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { Threatened = true }), Is.EqualTo(BotWrongRoadRefusal.Threat));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { HealthPercent = 79 }), Is.EqualTo(BotWrongRoadRefusal.Hurt));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { DiedLately = true }), Is.EqualTo(BotWrongRoadRefusal.Hurt));
            Assert.That(BotWayFinding.WrongRoadRefusal(Free with { Walking = false }), Is.EqualTo(BotWrongRoadRefusal.NotWalking));
            // Every exclusion at once still refuses (the first in order).
            Assert.That(BotWayFinding.WrongRoadRefusal(new BotWrongRoadFacts(true, true, true, true, true, true, true, 0, true, false)),
                Is.Not.EqualTo(BotWrongRoadRefusal.None));
        }

        [Test]
        public void Only_an_aggressive_monster_more_than_2_levels_above_within_1500_units_of_the_wrong_leg_stops_it()
        {
            Vector3 from = new(10_000, 10_000, 0), to = new(11_000, 10_000, 0);
            Vector3 beside = new(10_500, 11_400, 0); // 1,400 from the leg
            Assert.That(AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, 30, 33, true, beside), Is.True);
            Assert.That(AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, 30, 32, true, beside), Is.False, "+2 is allowed");
            Assert.That(AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, 30, 40, false, beside), Is.False, "not aggressive");
            Assert.That(AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, 30, 40, true, new(10_500, 11_600, 0)), Is.False, "1,600 away");
            Assert.That(AutonomousRouteThreatPolicy.WrongLegThreatened(from, to, 30, 40, true, new(12_400, 10_000, 0)), Is.True, "past the end, 1,400");
            Assert.That(AutonomousRouteThreatPolicy.WrongRoadThreatRadius, Is.EqualTo(1_500));
        }

        [Test]
        public void A_wrong_leg_is_600_to_1500_units_and_never_costs_more_than_3_minutes_there_and_back()
        {
            for (int i = 0; i <= 100; i++)
            {
                double roll = i / 100.0;
                foreach (float speed in new[] { 1f, 50f, 120f, 191f, 250f, 600f })
                {
                    BotWrongLeg leg = BotWayFinding.LegFor(0, 3_000, roll, 1 - roll, speed);
                    if (leg.IsNone)
                    {
                        Assert.That(speed, Is.LessThan(10), "only a crawl has no wrong leg that fits the cap");
                        continue;
                    }
                    Assert.That(leg.Length, Is.InRange(BotWayFinding.WrongLegMinimum, BotWayFinding.WrongLegMaximum));
                    Assert.That(leg.LookMilliseconds, Is.InRange(BotWayFinding.LookMinMilliseconds, BotWayFinding.LookMaxMilliseconds));
                    Assert.That(BotWayFinding.PlannedSeconds(leg, speed), Is.LessThanOrEqualTo(150), "planned with 30 s to spare in the 180");
                }
            }
            // The leg stays 150 short of the anchor it heads for; a branch too near has none.
            Assert.That(BotWayFinding.LegFor(0, 1_000, 1, 0, 191).Length, Is.EqualTo(850));
            Assert.That(BotWayFinding.LegFor(0, 700, 1, 0, 191).IsNone, Is.True);
            Assert.That(BotWayFinding.LegFor(-1, 3_000, 1, 0, 191).IsNone, Is.True);
            // The hard stop: whatever happens on the way, it ends at 3 minutes.
            Assert.That(BotWayFinding.MustEnd(179_999), Is.False);
            Assert.That(BotWayFinding.MustEnd(180_000), Is.True);
            Assert.That(BotWayFinding.WrongRoadCapMilliseconds, Is.EqualTo(180_000));
        }

        [Test]
        public void A_wrong_branch_is_neither_the_road_to_the_goal_nor_straight_back()
        {
            Vector3 from = new(0, 0, 0), goal = new(20_000, 0, 0);
            var anchors = new List<Vector3>
            {
                new(2_048, 0, 0),       // toward the goal: the right road
                new(-2_048, 0, 0),      // straight back
                new(0, 2_048, 0),       // 90 degrees: a wrong road
                new(-1_448, -1_448, 0), // 135 degrees: a wrong road
                new(1_448, 1_448, 0),   // 45 degrees: too close to the right road
                new(300, 300, 0),       // too near
                new(0, 9_000, 0),       // too far
            };
            var picked = new HashSet<int>();
            for (int i = 0; i < 100; i++)
                picked.Add(BotWayFinding.WrongBranch(from, goal, anchors, i / 100.0));
            Assert.That(picked, Is.EquivalentTo(new[] { 2, 3 }));
            Assert.That(BotWayFinding.WrongBranch(from, goal, anchors.Take(2).ToList(), 0.5), Is.EqualTo(-1), "no other branch: none");
            Assert.That(BotWayFinding.WrongBranch(from, from, anchors, 0.5), Is.EqualTo(-1), "no way to the goal: none");
            Assert.That(BotWayFinding.WrongBranch(from, goal, null, 0.5), Is.EqualTo(-1));
        }

        // ---- asking: answers only from bots that know, the realm cap -------------------------------------------------------

        [Test]
        public void Only_a_bot_that_knows_the_place_answers_the_way()
        {
            Assert.That(BotWayFinding.MayAnswer(BotPlaceKnowing.Known), Is.True);
            Assert.That(BotWayFinding.MayAnswer(BotPlaceKnowing.HalfKnown), Is.False, "told the way is not knowing it");
            Assert.That(BotWayFinding.MayAnswer(BotPlaceKnowing.Unknown), Is.False);
            Assert.That(BotWayFinding.Answer(new[] { ("regular", BotPlaceKnowing.Unknown), ("near", BotPlaceKnowing.HalfKnown),
                ("any", BotPlaceKnowing.Known), ("second", BotPlaceKnowing.Known) }), Is.EqualTo("any"));
            Assert.That(BotWayFinding.Answer(new[] { ("regular", BotPlaceKnowing.Unknown), ("near", BotPlaceKnowing.HalfKnown) }), Is.Null,
                "nobody who knows: nobody answers");
            Assert.That(BotWayFinding.Answer<string>(null), Is.Null);
            // A line that is no way question is answered as before.
            Assert.That(BotWayFinding.MayAnswer((GameBot)null, new Field.Said("lfg 30 cleric", "lfg", null)), Is.True);
        }

        [Test]
        public void Being_told_the_way_makes_the_asker_half_know_the_place_until_it_goes_there()
        {
            BotPlaceKey lyonesse = BotPlaceKey.Of("lyonesse");
            var asker = new BotPlaceSubject(BotPersonKey.Character(4_001), 4_001, "Asker", eRealm.Albion, 20, "Camelot Hills");
            Assert.That(BotPlaceKnowledge.Ensure(asker).Knowing(lyonesse), Is.EqualTo(BotPlaceKnowing.Unknown));
            Assert.That(BotPlaceKnowledge.Learn(asker, lyonesse, BotPlaceLearning.Told), Is.True);
            Assert.That(BotPlaceKnowledge.Ensure(asker).Knowing(lyonesse), Is.EqualTo(BotPlaceKnowing.HalfKnown));
            Assert.That(BotWayFinding.MayAnswer(BotPlaceKnowledge.Ensure(asker).Knowing(lyonesse)), Is.False, "a half-knower does not answer");
            Assert.That(BotWayFinding.Choose(BotPlaceKnowing.HalfKnown, RvrSkillTier.Casual, BotWayGroup.Solo, true, true, 0.2),
                Is.Not.EqualTo(BotWayChoice.Ask));
        }

        [Test]
        public void A_realm_asks_the_way_in_faction_at_most_once_every_10_minutes()
        {
            var cap = new BotWayAskCap(BotWayFinding.FactionAskGapMilliseconds);
            Assert.That(cap.TryTake(eRealm.Albion, 1_000, out _), Is.True);
            Assert.That(cap.TryTake(eRealm.Albion, 1_000 + 599_999, out _), Is.False);
            Assert.That(cap.TryTake(eRealm.Midgard, 2_000, out _), Is.True, "each realm has its own 10 minutes");
            Assert.That(cap.WaitMilliseconds(eRealm.Albion, 301_000), Is.EqualTo(300_000));
            Assert.That(cap.TryTake(eRealm.Albion, 601_000, out long previous), Is.True);
            // A line that was not said gives its turn back.
            cap.Restore(eRealm.Albion, previous);
            Assert.That(cap.WaitMilliseconds(eRealm.Albion, 601_000), Is.LessThanOrEqualTo(0));
            Assert.That(cap.TryTake(eRealm.Hibernia, 5, out long none), Is.True);
            cap.Restore(eRealm.Hibernia, none);
            Assert.That(cap.TryTake(eRealm.Hibernia, 6, out _), Is.True, "a first turn given back leaves the realm free");
            Assert.That(BotWayFinding.FactionAskGapMilliseconds, Is.EqualTo(600_000));
        }

        // ---- the Travel scene's questions become the asker's real ones ---------------------------------------------------

        private static Field.Facts Facts(string destination, bool real) =>
            new Field.Facts(77, eRealm.Albion, "Friar", 22, "Camelot Hills", null, false, false, false, false, 1, false, false, false, false, null,
                null, 0, false, destination, 0, 15, true, Field.WorldView.Empty) { WayAsksAreReal = real };

        [Test]
        public void With_asking_real_the_ambient_faction_never_draws_a_way_question_and_without_it_still_does()
        {
            int Asks(bool real)
            {
                var random = new Random(3);
                int asks = 0;
                for (int i = 0; i < 4_000; i++)
                    if (Field.Line("faction", Facts("Lyonesse", real), null, random) is { } said &&
                        said.Family is "ask_way" or "ask_stable")
                        asks++;
                return asks;
            }
            Assert.That(Asks(true), Is.Zero);
            Assert.That(Asks(false), Is.GreaterThan(0), "switches off: the scene as before 1.2");
        }

        [Test]
        public void A_way_question_is_the_travel_scenes_line_naming_the_destination_and_answered_only_by_knowers()
        {
            var random = new Random(11);
            var families = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                Field.Said said = Field.WayQuestion(Facts(null, true), "Lyonesse", random);
                Assert.That(said.WayTo, Is.EqualTo("Lyonesse"));
                Assert.That(said.Kind, Is.EqualTo("travel"));
                Assert.That(said.Text, Does.Contain("lyonesse"));
                Assert.That(said.Answers, Is.Not.Empty, "the scene's written answers stay the fallback for a live one");
                families.Add(said.Family);
            }
            Assert.That(families, Is.SubsetOf(new[] { "ask_way", "ask_stable" }));
            Assert.That(families, Does.Contain("ask_way"));
            Assert.That(Field.WayQuestion(Facts(null, true), " ", random), Is.Null);
            // The realm's memory notes it (its family rests, so the ambient lines don't repeat it at once).
            var memory = new Field.Memory();
            Field.Said noted = Field.WayQuestion(Facts(null, true), "Lyonesse", random, memory, 1_000);
            Assert.That(memory.Rested(noted.Family, noted.Idea, 2_000), Is.True);
        }

        // ---- tagging along ---------------------------------------------------------------------------------------------

        [Test]
        public void A_bot_tags_along_only_behind_a_free_realm_mate_that_knows_the_place_and_keeps_300_to_600_behind()
        {
            Assert.That(BotWayFinding.FollowFits(2_400, true, BotPlaceKnowing.Known, false, false, false), Is.True);
            Assert.That(BotWayFinding.FollowFits(2_600, true, BotPlaceKnowing.Known, false, false, false), Is.False, "beyond 2,500");
            Assert.That(BotWayFinding.FollowFits(500, false, BotPlaceKnowing.Known, false, false, false), Is.False, "going elsewhere");
            Assert.That(BotWayFinding.FollowFits(500, true, BotPlaceKnowing.HalfKnown, false, false, false), Is.False, "only told the way");
            Assert.That(BotWayFinding.FollowFits(500, true, BotPlaceKnowing.Known, true, false, false), Is.False, "on a stable ride");
            Assert.That(BotWayFinding.FollowFits(500, true, BotPlaceKnowing.Known, false, true, false), Is.False, "fighting");
            Assert.That(BotWayFinding.FollowFits(500, true, BotPlaceKnowing.Known, false, false, true), Is.False, "in the player's group");
            Assert.That(BotWayFinding.FollowStep(250, 450), Is.EqualTo(-1), "too close: waits");
            Assert.That(BotWayFinding.FollowStep(450, 450), Is.Zero);
            Assert.That(BotWayFinding.FollowStep(451, 450), Is.EqualTo(1), "falls behind: catches up");
            Assert.That(BotWayFinding.FollowStep(700, 9_000), Is.EqualTo(1), "never more than 600 behind");
        }

        // ---- the summary --------------------------------------------------------------------------------------------------

        [Test]
        public void The_summary_line_counts_choices_by_tier_minutes_lost_and_deaths_after_a_wrong_road()
        {
            var choices = new long[3, 4];
            choices[0, 0] = 4; choices[0, 1] = 3; choices[0, 2] = 1; choices[0, 3] = 2;
            choices[2, 0] = 7;
            string line = BotWayFinding.SummaryLine(300, 30, 17, choices, 3, 1, 2, 0, 1, 2, 1, 1, 2, 90_000,
                new Dictionary<BotWrongRoadRefusal, long> { [BotWrongRoadRefusal.Threat] = 2, [BotWrongRoadRefusal.Frontier] = 1 }, 0, 9, 0);
            Assert.That(line, Does.StartWith("BOT_WAY_SUMMARY seconds=300 trips=30 unknown=17 "));
            Assert.That(line, Does.Contain("choices=casual:4/3/1/2,average:0/0/0/0,veteran:7/0/0/0"));
            Assert.That(line, Does.Contain("asks=said:3(say:1),capped:2,unheard:0,held:1 told=2 follows=1,none:1"));
            Assert.That(line, Does.Contain("wrong_roads=2 minutes_lost=1.5"));
            Assert.That(line, Does.Contain("refused=frontier:1,threat:2"));
            Assert.That(line, Does.Contain("deaths_60s=0"));
            Assert.That(BotWayFinding.SummaryLine(300, 0, 0, new long[3, 4], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0),
                Does.Contain("refused=none"));
        }

        [Test]
        public void Names_used_in_the_log_lines_are_stable()
        {
            Assert.That(Enum.GetValues<BotWayChoice>().Select(BotWayFinding.ChoiceName), Is.EqualTo(new[] { "knows", "ask", "follow", "wrong_road" }));
            Assert.That(Enum.GetValues<BotWrongRoadRefusal>().Select(BotWayFinding.RefusalName), Is.Unique);
            Assert.That(Enum.GetValues<BotWayAskOutcome>().Select(BotWayFinding.OutcomeName), Is.Unique);
            Assert.That(Tiers.Select(BotWayFinding.TierName), Is.EqualTo(new[] { "casual", "average", "veteran" }));
        }
    }
}
