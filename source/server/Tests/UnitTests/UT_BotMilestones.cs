using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people for 1.2, work package 6 (docs/BOTS-AS-PEOPLE-1.2.md, item 6): personal milestones. The arcs (the ding to
    /// 50, realm rank 5, a dragon trophy; no epic armour), progress from the bot's real numbers, the talk points and their
    /// pacing, the channel order, the goal tilt inside the exact mix, the arc's life in the store (a real SQLite round trip of
    /// the new columns), the dragon trophy only from an item really received at a high-end raid kill, and the corpus lines
    /// for every speaker. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotMilestones
    {
        private static BotPersonality Personality(BotPersonalityType type, int risk = 5, int sociability = 5, int patience = 5,
            int curiosity = 5, int chattiness = 5) =>
            new(type, new BotPersonalityTraits(curiosity, risk, sociability, patience, chattiness));

        private static BotMilestoneSubject Subject(int level, long experience = 0, long realmPoints = 0, BotPersonality? personality = null,
            long id = 1234) => new(BotPersonKey.Character(id), id, "Aldwin", "Armsman", eRealm.Albion, level, experience, realmPoints,
            AutonomousBotRealmPointRewards.RealmLevelFor(realmPoints), personality);

        private static long XpAt(int level, double into = 0) =>
            BotMilestones.ExperienceAt(level) + (long)((BotMilestones.ExperienceAt(level + 1) - BotMilestones.ExperienceAt(level)) * into);

        // ---- the numbers -------------------------------------------------------------------------------------------------

        [Test]
        public void The_numbers_are_the_servers_own_tables()
        {
            Assert.That(BotMilestones.ExperienceAt(45), Is.EqualTo(53_999_999_950L));
            Assert.That(BotMilestones.ExperienceAt(50), Is.EqualTo(169_999_999_950L));
            Assert.That(BotMilestones.RealmRank5Points, Is.EqualTo(513_500));
            Assert.That(AutonomousBotRealmPointRewards.RealmLevelFor(BotMilestones.RealmRank5Points), Is.EqualTo(BotMilestones.RealmRank5Level));
            Assert.That(BotMilestones.Arcs.Select(BotMilestones.Id), Is.EqualTo(new[] { "ding50", "rr5", "dragon_trophy" }));
            foreach (BotMilestoneArc arc in BotMilestones.Arcs)
                Assert.That(BotMilestones.Parse(BotMilestones.Id(arc)), Is.EqualTo(arc));
            Assert.That(BotMilestones.Parse("epic_armour"), Is.EqualTo(BotMilestoneArc.None), "no epic armour arc in 1.2 (owner decision 2)");
        }

        [Test]
        public void Progress_comes_from_experience_and_realm_points()
        {
            Assert.That(BotMilestones.DingProgress(45, XpAt(45)), Is.EqualTo(0).Within(1e-9));
            Assert.That(BotMilestones.DingProgress(50, 0), Is.EqualTo(1));
            Assert.That(BotMilestones.DingProgress(48, XpAt(48)), Is.LessThan(0.5), "48 is just under halfway in experience");
            Assert.That(BotMilestones.DingProgress(48, XpAt(48, 0.1)), Is.GreaterThan(0.5));
            Assert.That(BotMilestones.RankProgress(256_750), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(BotMilestones.RankProgress(9_999_999), Is.EqualTo(1));
            Assert.That(BotMilestones.RankThousandsLeft(500_000), Is.EqualTo(14));
            Assert.That(BotMilestones.RankThousandsLeft(513_499), Is.EqualTo(1));
            Assert.That(BotMilestones.RankThousandsLeft(513_500), Is.EqualTo(0));
        }

        [TestCase(0.0, 10)]
        [TestCase(0.05, 10)]
        [TestCase(0.1, 9)]
        [TestCase(0.64, 4)]
        [TestCase(0.95, 1)]
        public void Bubbles_left_are_tenths_of_level_49(double into, int bubbles)
        {
            Assert.That(BotMilestones.BubblesLeft(49, XpAt(49, into)), Is.EqualTo(bubbles));
            Assert.That(BotMilestones.BubblesLeft(48, XpAt(48, into)), Is.Zero, "only a level 49 counts bubbles to 50");
        }

        [Test]
        public void Talk_points_are_half_and_nine_tenths_and_the_trophy_counts_raid_kills()
        {
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.Ding50, 0.49, 0), Is.Zero);
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.Ding50, 0.5, 0), Is.EqualTo(50));
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.RealmRank5, 0.89, 0), Is.EqualTo(50));
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.RealmRank5, 0.95, 0), Is.EqualTo(90));
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.DragonTrophy, 1, 1), Is.Zero, "a trophy has no fraction, only kills without a drop");
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.DragonTrophy, 0, 2), Is.EqualTo(50));
            Assert.That(BotMilestones.PointOf(BotMilestoneArc.DragonTrophy, 0, 5), Is.EqualTo(90));
        }

        // ---- choosing ------------------------------------------------------------------------------------------------------

        [Test]
        public void An_arc_is_chosen_by_level_and_leaning_and_never_twice_once_reached()
        {
            string None(BotMilestoneArc _) => null;
            var daredevil = Personality(BotPersonalityType.Daredevil, risk: 8, sociability: 3);
            var socialite = Personality(BotPersonalityType.Socialite, risk: 3);
            var crafter = Personality(BotPersonalityType.Crafter, risk: 1);
            Assert.That(BotMilestones.Choose(44, 0, daredevil, 1, None), Is.EqualTo(BotMilestoneArc.None));
            Assert.That(BotMilestones.Choose(45, 0, crafter, 1, None), Is.EqualTo(BotMilestoneArc.Ding50), "every bot chases 50");
            Assert.That(BotMilestones.Choose(49, 0, crafter, 1, a => a == BotMilestoneArc.Ding50 ? OfflineBotMilestoneRecord.Reached : null),
                Is.EqualTo(BotMilestoneArc.None));
            Assert.That(BotMilestones.Choose(50, 0, daredevil, 1, None), Is.EqualTo(BotMilestoneArc.RealmRank5));
            Assert.That(BotMilestones.Choose(50, 40, daredevil, 1, None), Is.EqualTo(BotMilestoneArc.None), "already realm rank 5");
            Assert.That(BotMilestones.Choose(50, 0, socialite, 1, None), Is.EqualTo(BotMilestoneArc.DragonTrophy));
            Assert.That(BotMilestones.Choose(50, 0, crafter, 1, None), Is.EqualTo(BotMilestoneArc.None), "a Crafter chases neither");
            Assert.That(BotMilestones.Choose(50, 0, socialite, 1, a => a == BotMilestoneArc.DragonTrophy ? OfflineBotMilestoneRecord.Reached : null),
                Is.EqualTo(BotMilestoneArc.None));
            Assert.That(BotMilestones.Choose(50, 0, socialite, 1, a => a == BotMilestoneArc.DragonTrophy ? OfflineBotMilestoneRecord.Dropped : null),
                Is.EqualTo(BotMilestoneArc.DragonTrophy), "a dropped arc may be taken again");

            // Both leanings: one by a fixed roll, the other once that one is reached.
            var nolifer = Personality(BotPersonalityType.NoLifer, risk: 8);
            var picks = Enumerable.Range(1, 400).Select(id => BotMilestones.Choose(50, 0, nolifer, id, None)).ToArray();
            Assert.That(picks.Count(p => p == BotMilestoneArc.RealmRank5), Is.InRange(140, 260));
            Assert.That(picks.Count(p => p == BotMilestoneArc.DragonTrophy), Is.InRange(140, 260));
            for (int id = 1; id <= 20; id++)
            {
                BotMilestoneArc first = BotMilestones.Choose(50, 0, nolifer, id, None);
                Assert.That(BotMilestones.Choose(50, 0, nolifer, id, None), Is.EqualTo(first), "fixed by the id");
                Assert.That(BotMilestones.Choose(50, 0, nolifer, id, a => a == first ? OfflineBotMilestoneRecord.Reached : null),
                    Is.Not.EqualTo(first).And.Not.EqualTo(BotMilestoneArc.None));
            }
        }

        [Test]
        public void About_two_level_50s_in_three_chase_something_with_real_personalities()
        {
            int rvr = 0, raid = 0, any = 0, n = 10_000;
            for (long id = 1; id <= n; id++)
            {
                BotPersonality p = AutonomousBotPersonality.For(id);
                bool r = BotMilestones.LeansRvr(p, id), d = BotMilestones.LeansRaid(p, id);
                rvr += r ? 1 : 0;
                raid += d ? 1 : 0;
                any += r || d ? 1 : 0;
            }
            Assert.That(rvr * 100.0 / n, Is.InRange(20, 40));
            Assert.That(raid * 100.0 / n, Is.InRange(40, 70));
            Assert.That(any * 100.0 / n, Is.InRange(55, 85));
            int offRvr = Enumerable.Range(1, n).Count(id => BotMilestones.LeansRvr(null, id));
            Assert.That(offRvr * 100.0 / n, Is.EqualTo(30).Within(2), "with personalities off, a fixed roll");
        }

        [Test]
        public void An_arc_stays_possible_and_is_reached_only_by_its_own_numbers()
        {
            Assert.That(BotMilestones.StillPossible(BotMilestoneArc.Ding50, 44), Is.False);
            Assert.That(BotMilestones.StillPossible(BotMilestoneArc.Ding50, 49), Is.True);
            Assert.That(BotMilestones.StillPossible(BotMilestoneArc.RealmRank5, 49), Is.False);
            Assert.That(BotMilestones.StillPossible(BotMilestoneArc.DragonTrophy, 50), Is.True);
            Assert.That(BotMilestones.Reached(BotMilestoneArc.Ding50, 50, 0, false), Is.True);
            Assert.That(BotMilestones.Reached(BotMilestoneArc.RealmRank5, 50, 39, true), Is.False);
            Assert.That(BotMilestones.Reached(BotMilestoneArc.RealmRank5, 50, 40, false), Is.True);
            Assert.That(BotMilestones.Reached(BotMilestoneArc.DragonTrophy, 50, 99, false), Is.False, "only a real item");
            Assert.That(BotMilestones.Reached(BotMilestoneArc.DragonTrophy, 50, 0, true), Is.True);
        }

        // ---- the small push ------------------------------------------------------------------------------------------------

        [Test]
        public void The_push_takes_fewer_town_breaks_near_50_and_leans_to_rvr_near_rank_5()
        {
            for (long id = 1; id <= 200; id++)
            {
                Assert.That(BotMilestones.TownBreakFactor(BotMilestoneArc.Ding50, 48, id), Is.InRange(0.4, 0.6));
                Assert.That(BotMilestones.TownBreakFactor(BotMilestoneArc.Ding50, 47, id), Is.EqualTo(1.0), "only within 2 levels of 50");
                Assert.That(BotMilestones.TownBreakFactor(BotMilestoneArc.RealmRank5, 50, id), Is.EqualTo(1.0));
            }
            Assert.That(BotMilestones.RvrLean(BotMilestoneArc.RealmRank5, 0.3), Is.EqualTo(1.0));
            Assert.That(BotMilestones.RvrLean(BotMilestoneArc.RealmRank5, 0.6), Is.EqualTo(1.5));
            Assert.That(BotMilestones.RvrLean(BotMilestoneArc.RealmRank5, 0.95), Is.EqualTo(2.0));
            Assert.That(BotMilestones.RvrLean(BotMilestoneArc.DragonTrophy, 0.95), Is.EqualTo(1.0), "raid recruiting is the raids' own (frozen)");
        }

        [Test]
        public void The_rvr_lean_changes_who_takes_the_slots_never_how_many()
        {
            var random = new Random(7);
            int n = 100, leanedRvr = 0, plainRvr = 0;
            var types = new BotPersonalityType?[n];
            double[] lean = Enumerable.Range(0, n).Select(i => i < 20 ? 2.0 : 1.0).ToArray();
            for (int trial = 0; trial < 200; trial++)
            {
                var plan = AutonomousBotPersonality.PlanGoals(types, new bool[n], 40, 40, 20, 0, random, lean);
                Assert.That(plan.Count(p => p == eAutonomousObjectiveKind.RvR), Is.EqualTo(20), "the mix stays exact");
                Assert.That(plan.Count(p => p == eAutonomousObjectiveKind.SoloPve), Is.EqualTo(40));
                leanedRvr += Enumerable.Range(0, 20).Count(i => plan[i] == eAutonomousObjectiveKind.RvR);
                plainRvr += Enumerable.Range(20, 80).Count(i => plan[i] == eAutonomousObjectiveKind.RvR);
            }
            Assert.That(leanedRvr / (200.0 * 20), Is.GreaterThan(1.3 * plainRvr / (200.0 * 80)), "a leaned bot takes RvR more often");
            var same = AutonomousBotPersonality.PlanGoals(types, new bool[n], 40, 40, 20, 0, new Random(3));
            var unleaned = AutonomousBotPersonality.PlanGoals(types, new bool[n], 40, 40, 20, 0, new Random(3), Enumerable.Repeat(1.0, n).ToArray());
            Assert.That(unleaned, Is.EqualTo(same), "a lean of 1 everywhere is today's plan, draw for draw");
        }

        // ---- the talk ------------------------------------------------------------------------------------------------------

        [Test]
        public void A_bot_says_some_points_by_its_chattiness_and_never_twice()
        {
            Assert.That(BotMilestoneTalk.TalkChance(0), Is.EqualTo(0.35).Within(1e-9));
            Assert.That(BotMilestoneTalk.TalkChance(9), Is.EqualTo(0.80).Within(1e-9));
            int quiet = Enumerable.Range(1, 4000).Count(id => BotMilestoneTalk.WillTalk(id, BotMilestoneArc.Ding50, 50, 0));
            int chatty = Enumerable.Range(1, 4000).Count(id => BotMilestoneTalk.WillTalk(id, BotMilestoneArc.Ding50, 50, 9));
            Assert.That(quiet / 4000.0, Is.EqualTo(0.35).Within(0.03));
            Assert.That(chatty / 4000.0, Is.EqualTo(0.80).Within(0.03));
            Assert.That(BotMilestoneTalk.WillTalk(77, BotMilestoneArc.Ding50, 90, 4), Is.EqualTo(BotMilestoneTalk.WillTalk(77, BotMilestoneArc.Ding50, 90, 4)),
                "a fixed roll: a restart decides the same");
            Assert.That(BotMilestoneTalk.Due(50, 0, 1_000, 0), Is.True);
            Assert.That(BotMilestoneTalk.Due(50, 50, 1_000, 0), Is.False, "a point handled is never said again");
            Assert.That(BotMilestoneTalk.Due(90, 50, 1_000, 2_000), Is.False, "waiting for its retry");
        }

        [Test]
        public void Lines_go_where_a_real_player_hears_them_and_faction_waits_its_turn()
        {
            Assert.That(BotMilestoneTalk.Pick(false, true, true, true, true, true), Is.EqualTo(BotMilestoneChannel.Group));
            Assert.That(BotMilestoneTalk.Pick(false, false, true, true, true, true), Is.EqualTo(BotMilestoneChannel.Say));
            Assert.That(BotMilestoneTalk.Pick(false, false, false, true, true, true), Is.EqualTo(BotMilestoneChannel.Guild));
            Assert.That(BotMilestoneTalk.Pick(false, false, false, false, true, true), Is.EqualTo(BotMilestoneChannel.Faction));
            Assert.That(BotMilestoneTalk.Pick(false, false, false, false, true, false), Is.EqualTo(BotMilestoneChannel.None), "the realm gap");
            Assert.That(BotMilestoneTalk.Pick(false, false, false, false, false, true), Is.EqualTo(BotMilestoneChannel.None), "nobody hears");
            Assert.That(BotMilestoneTalk.Pick(true, true, true, true, true, true), Is.EqualTo(BotMilestoneChannel.Faction), "the moment is realm news");
            Assert.That(BotMilestoneTalk.Pick(true, false, true, true, true, false), Is.EqualTo(BotMilestoneChannel.Guild));
            Assert.That(BotMilestoneTalk.FactionGapOpen(BotMilestoneTalk.FactionGapMs, 0), Is.True);
            Assert.That(BotMilestoneTalk.FactionGapOpen(BotMilestoneTalk.FactionGapMs - 1, 0), Is.False);
            for (int seed = 0; seed < 50; seed++)
                Assert.That(BotMilestoneTalk.TypingDelayMs(new Random(seed)), Is.InRange(2_000, 5_999));
        }

        [Test]
        public void Topics_name_what_the_numbers_say()
        {
            Assert.That(BotMilestones.Topic(BotMilestoneArc.Ding50, 50, 0), Is.EqualTo("ding50_half"));
            Assert.That(BotMilestones.Topic(BotMilestoneArc.Ding50, 90, 4), Is.EqualTo("ding50_bubbles"));
            Assert.That(BotMilestones.Topic(BotMilestoneArc.Ding50, 90, 1), Is.EqualTo("ding50_close"), "no \"1 bubbles\"");
            Assert.That(BotMilestones.Topic(BotMilestoneArc.Ding50, 90, 10), Is.EqualTo("ding50_close"));
            Assert.That(BotMilestones.Topic(BotMilestoneArc.RealmRank5, 90, 0), Is.EqualTo("rr5_close"));
            Assert.That(BotMilestones.Topic(BotMilestoneArc.DragonTrophy, 50, 0), Is.EqualTo("trophy_try"));
            Assert.That(BotMilestones.ReachedTopic(BotMilestoneArc.Ding50), Is.Null, "the ding's own line and the grats chain are its moment");
            Assert.That(BotMilestones.ReachedTopic(BotMilestoneArc.DragonTrophy), Is.EqualTo("trophy_reached"));
        }

        [Test]
        public void The_summary_line_names_every_count()
        {
            string line = BotMilestoneTalk.SummaryLine(300, new Dictionary<string, long> { ["chosen"] = 3, ["talk.faction"] = 1, ["reached.rr5"] = 2 },
                [4, 1, 2]);
            Assert.That(line, Does.StartWith("BOT_MILESTONE_SUMMARY seconds=300 active=ding50:4,rr5:1,dragon_trophy:2 chosen=3 "));
            Assert.That(line, Does.Contain("talks=group:0,say:0,guild:0,faction:1").And.Contain("reached=ding50:0,rr5:2,dragon_trophy:0"));
        }

        // ---- the arc's life in the store -------------------------------------------------------------------------------------

        [Test]
        public void The_ding_to_50_is_chosen_passes_its_points_once_and_is_reached_then_the_next_arc_follows()
        {
            var store = new BotPeopleStore(() => null);
            var daredevil = Personality(BotPersonalityType.Daredevil, risk: 8, sociability: 2);
            var now = new DateTime(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc);

            var events = BotMilestoneLife.Advance(store, Subject(44, XpAt(44), personality: daredevil), now);
            Assert.That(events, Is.Empty, "below 45 nothing");
            events = BotMilestoneLife.Advance(store, Subject(46, XpAt(46), personality: daredevil), now);
            Assert.That(events.Select(e => e.Step), Is.EqualTo(new[] { BotMilestoneStep.Chosen }));
            Assert.That(events[0].Arc, Is.EqualTo(BotMilestoneArc.Ding50));
            Assert.That(events[0].Row.State, Is.EqualTo(OfflineBotMilestoneRecord.Active));
            Assert.That(events[0].Row.Name, Is.EqualTo("Aldwin"));
            Assert.That(BotMilestoneLife.Advance(store, Subject(47, XpAt(47), personality: daredevil), now), Is.Empty, "under halfway");

            events = BotMilestoneLife.Advance(store, Subject(48, XpAt(48, 0.2), personality: daredevil), now);
            Assert.That(events.Single().Step, Is.EqualTo(BotMilestoneStep.Point));
            Assert.That(events.Single().Point, Is.EqualTo(50));
            BotMilestoneLife.MarkPoint(store, events.Single().Row, 50, now);
            Assert.That(BotMilestoneLife.Advance(store, Subject(48, XpAt(48, 0.3), personality: daredevil), now), Is.Empty, "said once");

            events = BotMilestoneLife.Advance(store, Subject(49, XpAt(49, 0.7), personality: daredevil), now);
            Assert.That(events.Single().Point, Is.EqualTo(90));
            BotMilestoneLife.MarkPoint(store, events.Single().Row, 90, null);
            Assert.That(events.Single().Row.TalkPoint, Is.EqualTo(90));
            Assert.That(events.Single().Row.LastTalkUtc, Is.Not.Empty, "the 50 talk's time stays; a silent point keeps it");

            events = BotMilestoneLife.Advance(store, Subject(50, XpAt(50), personality: daredevil), now);
            Assert.That(events.Select(e => (e.Step, e.Arc)), Is.EqualTo(new[]
                { (BotMilestoneStep.Reached, BotMilestoneArc.Ding50), (BotMilestoneStep.Chosen, BotMilestoneArc.RealmRank5) }));
            Assert.That(events[0].Row.ReachedUtc, Is.Not.Empty);
            Assert.That(BotMilestoneLife.ActiveRow(store, BotPersonKey.Character(1234), 1234).Arc, Is.EqualTo("rr5"));

            events = BotMilestoneLife.Advance(store, Subject(50, realmPoints: 300_000, personality: daredevil), now);
            Assert.That(events.Single().Point, Is.EqualTo(50));
            events = BotMilestoneLife.Advance(store, Subject(50, realmPoints: 513_500, personality: daredevil), now);
            Assert.That(events.Select(e => (e.Step, e.Arc)), Is.EqualTo(new[] { (BotMilestoneStep.Reached, BotMilestoneArc.RealmRank5) }),
                "a Daredevil with little sociability has no trophy arc to take next");
            Assert.That(BotMilestoneLife.ActiveRow(store, BotPersonKey.Character(1234), 1234), Is.Null);
            Assert.That(store.Milestones.Where(r => r.State == OfflineBotMilestoneRecord.Reached).Select(r => r.Arc), Is.EquivalentTo(new[] { "ding50", "rr5" }));
            Assert.That(BotMilestoneLife.ActiveCounts(store), Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void A_dragon_trophy_is_only_an_item_really_received_at_a_high_end_raid_kill()
        {
            var store = new BotPeopleStore(() => null);
            var socialite = Personality(BotPersonalityType.Socialite, risk: 2);
            var now = DateTime.UtcNow;
            BotMilestoneSubject at50 = Subject(50, personality: socialite);
            Assert.That(BotMilestoneLife.Advance(store, at50, now).Single().Arc, Is.EqualTo(BotMilestoneArc.DragonTrophy));
            Assert.That(BotMilestoneLife.Advance(store, at50, now, new BotMilestoneRaidKill("Cuuldurach the Glimmer King", null)), Is.Empty);
            var events = BotMilestoneLife.Advance(store, at50, now, new BotMilestoneRaidKill("Gjalpinulva", null));
            Assert.That(events.Single().Point, Is.EqualTo(50), "the second kill without a drop");
            Assert.That(events.Single().Row.Count, Is.EqualTo(2));
            Assert.That(events.Single().Row.Source, Is.EqualTo("Gjalpinulva"), "the last boss it was at");
            Assert.That(BotMilestoneLife.Advance(store, at50, now), Is.Not.Empty.And.None.Matches<BotMilestoneEvent>(e => e.Step == BotMilestoneStep.Reached),
                "no raid kill, no trophy");

            events = BotMilestoneLife.Advance(store, at50, now, new BotMilestoneRaidKill("Gjalpinulva", "Gjalpinulva's Scale"));
            BotMilestoneEvent reached = events.First();
            Assert.That(reached.Step, Is.EqualTo(BotMilestoneStep.Reached));
            Assert.That(reached.Row.Trophy, Is.EqualTo("Gjalpinulva's Scale"));
            Assert.That(reached.Row.Source, Is.EqualTo("Gjalpinulva"));
            Assert.That(reached.Row.Count, Is.EqualTo(2), "the winning kill is not counted as a miss");
            Assert.That(BotMilestoneLife.Advance(store, at50, now, new BotMilestoneRaidKill("Gjalpinulva", "Another Scale")).Where(e => e.Step == BotMilestoneStep.Reached),
                Is.Empty, "one trophy arc per character");

            // A raid kill means nothing to a bot chasing something else.
            var otherStore = new BotPeopleStore(() => null);
            var daredevil = Personality(BotPersonalityType.Daredevil, risk: 9, sociability: 1);
            BotMilestoneSubject rvrBot = Subject(50, realmPoints: 10, personality: daredevil, id: 99);
            BotMilestoneLife.Advance(otherStore, rvrBot, now);
            Assert.That(BotMilestoneLife.Advance(otherStore, rvrBot, now, new BotMilestoneRaidKill("Gjalpinulva", "Scale")), Is.Empty);
            Assert.That(BotMilestoneLife.ActiveRow(otherStore, rvrBot.Person, 99).Count, Is.Zero);
        }

        [Test]
        public void An_arc_no_longer_possible_is_dropped_and_nobody_without_a_person_gets_one()
        {
            var store = new BotPeopleStore(() => null);
            var now = DateTime.UtcNow;
            BotMilestoneLife.Advance(store, Subject(46, XpAt(46)), now);
            var events = BotMilestoneLife.Advance(store, Subject(44, XpAt(44)), now);
            Assert.That(events.Single().Step, Is.EqualTo(BotMilestoneStep.Dropped));
            Assert.That(events.Single().Row.State, Is.EqualTo(OfflineBotMilestoneRecord.Dropped));
            Assert.That(BotMilestoneLife.Advance(store, Subject(45, XpAt(45)), now).Single().Step, Is.EqualTo(BotMilestoneStep.Chosen),
                "taken up again when possible again");

            // A player's character, a companion, a hired companion or a helper has no person (BotPersonKey.Of): never an arc.
            var nobody = Subject(49, XpAt(49, 0.9)) with { Person = BotPersonKey.None };
            Assert.That(BotMilestoneLife.Advance(store, nobody, now), Is.Empty);
            Assert.That(BotPersonKey.Of((GameBot)null).IsNone, Is.True);
            Assert.DoesNotThrow(() => BotMilestoneLife.OnExperience(null));
            Assert.DoesNotThrow(() => BotMilestoneLife.OnItemReceived(null, null));
            Assert.That(BotMilestoneLife.TownBreakFactor(null), Is.EqualTo(1.0));
            Assert.That(BotMilestoneLife.RvrLean(null), Is.EqualTo(1.0));
        }

        [Test]
        public void The_arc_and_its_trophy_survive_a_save_and_a_merge()
        {
            string folder = TemporarySqlite.NewFolder("milestones");
            try
            {
                var database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                    $"Data Source={Path.Combine(folder, "people.db")};Pooling=False");
                foreach (Type type in BotPeopleTables.RecordTypes)
                    database.RegisterDataObject(type);
                var store = new BotPeopleStore(() => database);
                var now = DateTime.UtcNow;
                BotMilestoneSubject s = Subject(50, personality: Personality(BotPersonalityType.Socialite));
                BotMilestoneLife.Advance(store, s, now);
                BotMilestoneLife.Advance(store, s, now, new BotMilestoneRaidKill("Gjalpinulva", null));
                BotMilestoneLife.Advance(store, s, now, new BotMilestoneRaidKill("Gjalpinulva", "Gjalpinulva's Scale"));
                store.Flush(wait: true);

                var reloaded = new BotPeopleStore(() => database);
                reloaded.LoadFrom(database);
                Assert.That(reloaded.Milestones.TryGet((s.Person, 1234, "dragon_trophy"), out var row), Is.True);
                Assert.That((row.State, row.Trophy, row.Source, row.Count), Is.EqualTo((OfflineBotMilestoneRecord.Reached, "Gjalpinulva's Scale", "Gjalpinulva", 1)));

                OfflineBotMilestoneRecord merged = BotPeopleMerge.Milestones(BotPersonKey.Player(7), [row]).Single();
                Assert.That((merged.PersonKey, merged.Trophy, merged.Source, merged.Count, merged.TalkPoint),
                    Is.EqualTo(("p:7", row.Trophy, row.Source, row.Count, row.TalkPoint)), "a merge keeps the trophy, re-keyed");
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        // ---- the corpus -------------------------------------------------------------------------------------------------------

        private static VC _corpus;

        private static VC Corpus => _corpus ??= Load();

        private static VC Load()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        private static readonly (string Topic, int Level)[] Topics =
        [
            ("ding50_half", 45), ("ding50_half", 49), ("ding50_bubbles", 49), ("ding50_close", 47), ("rr5_half", 50), ("rr5_close", 50),
            ("rr5_reached", 50), ("trophy_try", 50), ("trophy_long", 50), ("trophy_reached", 50),
        ];

        [Test]
        public void Every_milestone_topic_has_a_line_for_every_speaker_with_its_slots_filled()
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["me"] = "Aldwin", ["class"] = "armsman", ["level"] = "49", ["realm"] = "Albion", ["bubbles"] = "4", ["count"] = "3",
                ["boss"] = "Cuuldurach the Glimmer King", ["item"] = "Glimmering Ring",
            };
            var random = new Random(11);
            foreach (BotPersonalityType type in Enum.GetValues<BotPersonalityType>())
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            foreach ((string topic, int level) in Topics)
            {
                var speaker = new VC.Speaker(31, "Aldwin", realm, "Armsman", level, type, new HashSet<string>(StringComparer.Ordinal));
                Assert.That(Corpus.TryPick("milestone", speaker, slots, null, DateTime.UtcNow, random, out string text, out _, topic: topic), Is.True,
                    $"{type} {realm} level {level} has a {topic} line");
                Assert.That(text, Does.Not.Contain("{"), "every slot filled");
            }
            foreach (string answers in new[] { "milestone_rr5", "milestone_trophy" })
                Assert.That(Corpus.Lines.Count(line => line.Kind == "reply" && line.Answers.Contains(answers) && line.Who.Length == 0), Is.GreaterThanOrEqualTo(5),
                    answers + ": the realm's cheer");
        }

        [Test]
        public void Milestone_lines_claim_only_the_numbers_their_slots_give()
        {
            VC.Line[] lines = Corpus.Lines.Where(line => line.Kind == "milestone").ToArray();
            Assert.That(lines.Length, Is.GreaterThan(100));
            foreach (VC.Line line in lines)
            {
                // The only literal numbers: 50 (the goal), rr5 (the rank), "the 40s" (levels 45-49) and, in the level 49 lines, 49.
                string bare = Regex.Replace(line.Text, @"\{\w+\}|\brr5\b|\b50s?\b|\b40s\b", "");
                if (line.Topic == "ding50_bubbles")
                    bare = Regex.Replace(bare, @"\b49\b", "");
                Assert.That(Regex.IsMatch(bare, @"\d"), Is.False, line.Text);
                Assert.That(Regex.IsMatch(line.Text, @"\b(epic|artifact|armou?r)\b", RegexOptions.IgnoreCase), Is.False, "decisions 2 and 3: " + line.Text);
            }
            Assert.That(lines.Where(l => l.Topic == "ding50_bubbles").All(l => l.Text.Contains("{bubbles}")), Is.True);
            Assert.That(lines.Where(l => l.Topic == "trophy_reached").All(l => l.Text.Contains("{item}") && l.Text.Contains("{boss}")), Is.True);
            Assert.That(lines.Where(l => l.Topic == "trophy_long").All(l => l.Text.Contains("{count}")), Is.True);
            Assert.That(lines.Where(l => l.Topic.StartsWith("ding50")).All(l => l.LevelLow >= 45 && l.LevelHigh <= 49), Is.True);
            Assert.That(lines.Where(l => !l.Topic.StartsWith("ding50")).All(l => l.LevelLow == 50), Is.True);
        }
    }
}
