using System;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 1 (docs/BOTS-AS-PEOPLE-1.2.md, item 2): the skill profile (tier shares, the awareness
    /// shift, facets inside the band, the personality tilt), the growth curve (about 40 hours from the bottom of Casual to
    /// Average), the one-tier cap, skill never falling, the log lines, and the save round trip through the store's SQLite
    /// table. Pure rules and a test database: tests only, nothing here is seen in game.
    /// </summary>
    [TestFixture]
    public class UT_BotSkillProfile
    {
        private const int Ids = 10_000;

        private static (double Casual, double Average, double Veteran) Shares(RvrBotAwareness awareness, Func<long, BotPersonality?> personality = null)
        {
            var counts = new int[3];
            for (long id = 1; id <= Ids; id++)
                counts[(int)BotSkillProfile.For(BotPersonKey.Of(id), personality?.Invoke(id), awareness).Tier]++;
            return (100.0 * counts[0] / Ids, 100.0 * counts[1] / Ids, 100.0 * counts[2] / Ids);
        }

        private static BotPersonality Personality(long id) =>
            new(BotPersonalityType.Leveler, new BotPersonalityTraits((int)(id % 10), (int)(id * 7 % 10), 5, (int)(id * 3 % 10), (int)(id * 11 % 10)));

        // ---- tiers ---------------------------------------------------------------------------------------------------

        [Test]
        public void Shares_are_30_50_20_within_2_points_over_10000_ids()
        {
            var normal = Shares(RvrBotAwareness.Normal);
            Assert.That(normal.Casual, Is.EqualTo(30).Within(2));
            Assert.That(normal.Average, Is.EqualTo(50).Within(2));
            Assert.That(normal.Veteran, Is.EqualTo(20).Within(2));

            var withPersonality = Shares(RvrBotAwareness.Normal, id => Personality(id));
            Assert.That(withPersonality, Is.EqualTo(normal), "a personality moves facets inside the band, never the tier");
        }

        [Test]
        public void Awareness_shifts_the_per_bot_mix_as_it_does_the_groups()
        {
            var easy = Shares(RvrBotAwareness.Easy);
            Assert.That(easy.Casual, Is.EqualTo(45).Within(2));
            Assert.That(easy.Average, Is.EqualTo(45).Within(2));
            Assert.That(easy.Veteran, Is.EqualTo(10).Within(2));
            var veteran = Shares(RvrBotAwareness.Veteran);
            Assert.That(veteran.Casual, Is.EqualTo(15).Within(2));
            Assert.That(veteran.Average, Is.EqualTo(45).Within(2));
            Assert.That(veteran.Veteran, Is.EqualTo(40).Within(2));

            // The knob only moves bots across the cut: no bot gets worse when awareness goes up.
            for (long id = 1; id <= 2_000; id++)
            {
                BotPersonKey person = BotPersonKey.Of(id);
                Assert.That(BotSkillProfile.For(person, null, RvrBotAwareness.Easy).Tier,
                    Is.LessThanOrEqualTo(BotSkillProfile.For(person, null, RvrBotAwareness.Normal).Tier));
                Assert.That(BotSkillProfile.For(person, null, RvrBotAwareness.Normal).Tier,
                    Is.LessThanOrEqualTo(BotSkillProfile.For(person, null, RvrBotAwareness.Veteran).Tier));
            }
        }

        [Test]
        public void A_bot_rolls_apart_from_its_group_and_the_same_on_every_run()
        {
            int same = 0;
            for (long id = 1; id <= Ids; id++)
            {
                RvrSkillTier own = BotSkillProfile.For(BotPersonKey.Of(id), null, RvrBotAwareness.Normal).Tier;
                if (own == RvrGroupSkill.For(id, null, RvrBotAwareness.Normal))
                    same++;
            }
            // Independent rolls agree about 0.3^2 + 0.5^2 + 0.2^2 = 38% of the time; the same salt would agree always.
            Assert.That(100.0 * same / Ids, Is.EqualTo(38).Within(3));
            BotPersonKey key = BotPersonKey.Of(1234);
            Assert.That(BotSkillProfile.For(key, Personality(1234), RvrBotAwareness.Normal),
                Is.EqualTo(BotSkillProfile.For(key, Personality(1234), RvrBotAwareness.Normal)));
            Assert.That(BotSkillProfile.Roll(BotPersonKey.Character(77)), Is.Not.EqualTo(BotSkillProfile.Roll(BotPersonKey.Player(77))));
            Assert.That(BotSkillProfile.For(BotPersonKey.None, null, RvrBotAwareness.Normal).IsNone, Is.True);
        }

        [Test]
        public void Facets_spread_inside_the_tier_band_and_the_personality_tilts_them_only_there()
        {
            double spreadSum = 0;
            int spreadCount = 0;
            for (long id = 1; id <= 3_000; id++)
            {
                BotPersonKey person = BotPersonKey.Of(id);
                BotSkillProfile plain = BotSkillProfile.For(person, null, RvrBotAwareness.Normal);
                BotSkillProfile tilted = BotSkillProfile.For(person, Personality(id), RvrBotAwareness.Normal);
                Assert.That(plain.Tier, Is.EqualTo(plain.StartTier));
                Assert.That(tilted.Tier, Is.EqualTo(plain.Tier));
                foreach (BotSkillFacet facet in BotSkillFacets.All)
                {
                    foreach (BotSkillProfile profile in new[] { plain, tilted })
                    {
                        Assert.That(profile.Skill(facet), Is.InRange((double)(int)profile.Tier, (int)profile.Tier + BotSkillProfile.BandTop));
                        Assert.That(profile.Position(facet), Is.InRange(0.0, 1.0));
                    }
                    Assert.That(Math.Abs(tilted.Skill(facet) - plain.Skill(facet)), Is.LessThanOrEqualTo(BotSkillProfile.MaxTraitTilt + 1e-9));
                    spreadSum += Math.Abs(plain.Skill(facet) - ((int)plain.Tier + plain.BandPosition));
                    spreadCount++;
                }
            }
            Assert.That(spreadSum / spreadCount, Is.InRange(0.05, BotSkillProfile.FacetSpread), "the facets of one bot differ, a little");

            var patient = new BotPersonality(BotPersonalityType.Leveler, new BotPersonalityTraits(5, 0, 5, 9, 0));
            var restless = new BotPersonality(BotPersonalityType.Daredevil, new BotPersonalityTraits(5, 9, 5, 0, 9));
            Assert.That(BotSkillProfile.TraitTilt(patient, BotSkillFacet.Pulls), Is.EqualTo(BotSkillProfile.MaxTraitTilt).Within(1e-9));
            Assert.That(BotSkillProfile.TraitTilt(restless, BotSkillFacet.Pulls), Is.EqualTo(-BotSkillProfile.MaxTraitTilt).Within(1e-9));
            Assert.That(BotSkillProfile.TraitTilt(restless, BotSkillFacet.Fire), Is.LessThan(0), "daring bots stand in it longer");
            Assert.That(BotSkillProfile.TraitTilt(restless, BotSkillFacet.Buffs), Is.LessThan(0), "chatty bots let a buff run out");
            Assert.That(BotSkillProfile.TraitTilt(patient, BotSkillFacet.Reactions), Is.Zero);
            Assert.That(BotSkillProfile.TraitTilt(null, BotSkillFacet.Pulls), Is.Zero);
        }

        [Test]
        public void Blend_gives_the_tier_value_mid_band_and_moves_toward_the_neighbours_at_the_edges()
        {
            // Pulls: Casual 10%, Average 5%, Veteran 1.5% (design table).
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Casual, 0.5, 10, 5, 1.5), Is.EqualTo(10).Within(1e-9));
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Average, 0.5, 10, 5, 1.5), Is.EqualTo(5).Within(1e-9));
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Veteran, 0.5, 10, 5, 1.5), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Casual, 1, 10, 5, 1.5), Is.EqualTo(7.5).Within(1e-9), "half way at the edge");
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Average, 0, 10, 5, 1.5), Is.EqualTo(7.5).Within(1e-9));
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Casual, 0, 10, 5, 1.5), Is.EqualTo(11.25).Within(1e-9), "a quarter of the gap past the bottom");
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Veteran, 1, 10, 5, 1.5), Is.EqualTo(0.625).Within(1e-9));
            Assert.That(BotSkillProfile.Blend(RvrSkillTier.Veteran, 1, 10, 9, 1), Is.EqualTo(0).Within(1e-9), "never below 0");
            // Reactions x1.3 / x1.0 / x0.75 inside an RvR group's tier (owner decision 5): the member's place spreads it.
            double low = BotSkillProfile.Blend(RvrSkillTier.Average, 0, 1.3, 1.0, 0.75);
            double high = BotSkillProfile.Blend(RvrSkillTier.Average, 0.999, 1.3, 1.0, 0.75);
            Assert.That(low, Is.GreaterThan(1.0).And.LessThan(1.3));
            Assert.That(high, Is.LessThan(1.0).And.GreaterThan(0.75));

            BotSkillProfile profile = BotSkillProfile.For(BotPersonKey.Of(42), null, RvrBotAwareness.Normal);
            Assert.That(profile.Blend(BotSkillFacet.Pulls, 10, 5, 1.5),
                Is.EqualTo(BotSkillProfile.Blend(profile.Tier, profile.Position(BotSkillFacet.Pulls), 10, 5, 1.5)));
        }

        // ---- growth --------------------------------------------------------------------------------------------------

        [Test]
        public void The_curve_takes_about_40_hours_from_the_bottom_of_casual_to_average()
        {
            double minutes = BotSkillGrowth.ExperienceToReach(0, BotSkillGrowth.Top(RvrSkillTier.Casual), 1);
            Assert.That(minutes / 60, Is.EqualTo(40).Within(0.01));
            Assert.That(BotSkillGrowth.Grown(0, 2, minutes), Is.EqualTo(1).Within(1e-9));
            Assert.That(BotSkillGrowth.Grown(0, 2, 20 * 60), Is.InRange(0.55, 0.6), "the curve is steep first, as RaidDiscipline's");
            Assert.That(BotSkillGrowth.Grown(0.5, 2, 0), Is.EqualTo(0.5));
            Assert.That(BotSkillGrowth.Grown(0.5, 2, -10), Is.EqualTo(0.5));
            Assert.That(BotSkillGrowth.Grown(0.5, 2, double.NaN), Is.EqualTo(0.5));
            Assert.That(BotSkillGrowth.ExperienceToReach(1, 3, 2) / 60, Is.EqualTo(40).Within(0.01), "the same for Average to Veteran");

            // Played as fights: a fight every minute for 40 hours from the bottom of Casual.
            var experience = BotSkillExperience.None;
            for (int fight = 0; fight < 40 * 60; fight++)
                experience = BotSkillGrowth.Step(experience, BotSkillEvent.FightWon, 60_000);
            Assert.That(experience.Mean, Is.EqualTo(2_400).Within(1e-6));
            double mean = BotSkillFacets.All.Average(f => BotSkillGrowth.Grown(0, 2, experience[f]));
            Assert.That(mean, Is.EqualTo(1).Within(1e-6));
            Assert.That(BotSkillProfile.TierFor(RvrSkillTier.Casual, mean + 1e-9, experience.ReachedTier), Is.EqualTo(RvrSkillTier.Average));
            Assert.That(BotSkillProfile.TierFor(RvrSkillTier.Casual, BotSkillFacets.All.Average(f => BotSkillGrowth.Grown(0, 2, experience[f] * 0.95)),
                -1), Is.EqualTo(RvrSkillTier.Casual), "not before about 40 hours");
        }

        [Test]
        public void Credits_count_play_time_and_lessons_never_a_flood()
        {
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.FightWon, 30_000).Mean, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.FightWon, 3_600_000).Mean, Is.EqualTo(3).Within(1e-9), "a long trip counts 3 minutes");
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.FightWon, 0).Total, Is.Zero, "a groupmate's kill at the same moment adds nothing");
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.FightWon, -5).Total, Is.Zero);
            BotSkillExperience death = BotSkillGrowth.Gain(BotSkillEvent.Death, BotSkillGrowth.DeathGapMilliseconds);
            Assert.That((death.Positioning, death.Fire, death.Reactions, death.Pulls), Is.EqualTo((5.0, 5.0, 2.5, 0.0)));
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.Death, 60_000).Total, Is.Zero, "one lesson in 10 minutes");
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.PullMade, 60_000).Pulls, Is.EqualTo(0.5));
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.PullMade, 10_000).Total, Is.Zero);
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.BuffCast, 60_000).Buffs, Is.EqualTo(0.5));
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.RestedForPower, 300_000).Power, Is.EqualTo(1));
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.RestedForPower, 299_999).Total, Is.Zero);
        }

        [Test]
        public void Skill_grows_one_tier_at_most_and_never_falls()
        {
            var huge = new BotSkillExperience
            {
                Reactions = 1e7, Positioning = 1e7, Pulls = 1e7, Fire = 1e7, Buffs = 1e7, Power = 1e7, ReachedTier = -1,
            };
            for (long id = 1; id <= 3_000; id++)
            {
                BotPersonKey person = BotPersonKey.Of(id);
                BotSkillProfile start = BotSkillProfile.For(person, null, RvrBotAwareness.Normal);
                BotSkillProfile grown = BotSkillProfile.For(person, null, RvrBotAwareness.Normal, huge);
                Assert.That(grown.Tier, Is.EqualTo(BotSkillProfile.MaxTierFor(start.StartTier)), "far past 40 hours: exactly one tier up");
                Assert.That((int)grown.Tier - (int)start.Tier, Is.LessThanOrEqualTo(1));
                foreach (BotSkillFacet facet in BotSkillFacets.All)
                    Assert.That(grown.Skill(facet), Is.GreaterThanOrEqualTo(start.Skill(facet)).And.LessThan(BotSkillGrowth.Top(start.StartTier) + 1e-12));

                // A saved tier above the cap (an old row, a higher awareness setting) is held to one above the start.
                BotSkillProfile capped = BotSkillProfile.For(person, null, RvrBotAwareness.Normal, BotSkillExperience.None with { ReachedTier = 2 });
                Assert.That(capped.Tier, Is.EqualTo(BotSkillProfile.MaxTierFor(start.StartTier)));
            }
            Assert.That(BotSkillProfile.MaxTierFor(RvrSkillTier.Veteran), Is.EqualTo(RvrSkillTier.Veteran));

            // Never falls: each step adds, and a reached tier stays even with no experience behind it.
            var experience = BotSkillExperience.None;
            BotPersonKey casual = Enumerable.Range(1, 1_000).Select(i => BotPersonKey.Of(i))
                .First(p => BotSkillProfile.For(p, null, RvrBotAwareness.Normal).StartTier == RvrSkillTier.Casual);
            RvrSkillTier last = RvrSkillTier.Casual;
            BotSkillEvent[] events = Enum.GetValues<BotSkillEvent>();
            for (int i = 0; i < 6_000; i++)
            {
                BotSkillExperience next = BotSkillGrowth.Step(experience, events[i % events.Length], 1_000_000);
                foreach (BotSkillFacet facet in BotSkillFacets.All)
                    Assert.That(next[facet], Is.GreaterThanOrEqualTo(experience[facet]));
                BotSkillProfile profile = BotSkillProfile.For(casual, null, RvrBotAwareness.Normal, next);
                experience = BotSkillGrowth.Settle(next, profile);
                Assert.That(profile.Tier, Is.GreaterThanOrEqualTo(last));
                last = profile.Tier;
            }
            Assert.That(last, Is.EqualTo(RvrSkillTier.Average));
            Assert.That(BotSkillProfile.For(casual, null, RvrBotAwareness.Normal, BotSkillExperience.None with { ReachedTier = 1 }).Tier,
                Is.EqualTo(RvrSkillTier.Average), "a saved promotion holds");
            Assert.That(BotSkillGrowth.Add(experience, BotSkillExperience.None with { Pulls = -50 }).Pulls, Is.EqualTo(experience.Pulls));
            Assert.That(BotSkillGrowth.Settle(experience with { ReachedTier = 1 },
                BotSkillProfile.For(casual, null, RvrBotAwareness.Normal)).ReachedTier, Is.EqualTo(1), "settling never lowers it");
        }

        [Test]
        public void Hours_to_the_next_tier_follow_the_curve()
        {
            BotPersonKey average = Enumerable.Range(1, 1_000).Select(i => BotPersonKey.Of(i))
                .First(p => BotSkillProfile.For(p, null, RvrBotAwareness.Normal).StartTier == RvrSkillTier.Average);
            BotSkillProfile fresh = BotSkillProfile.For(average, null, RvrBotAwareness.Normal);
            double hours = BotSkillGrowth.HoursToNextTier(fresh);
            Assert.That(hours, Is.GreaterThan(0).And.LessThanOrEqualTo(40.01));
            double minutes = hours * 60;
            var played = new BotSkillExperience
            {
                Reactions = minutes, Positioning = minutes, Pulls = minutes, Fire = minutes, Buffs = minutes, Power = minutes, ReachedTier = -1,
            };
            BotSkillProfile later = BotSkillProfile.For(average, null, RvrBotAwareness.Normal, played);
            Assert.That(later.MeanSkill, Is.EqualTo(2).Within(1e-6));
            Assert.That(BotSkillGrowth.HoursToNextTier(BotSkillProfile.For(average, null, RvrBotAwareness.Normal,
                played with { ReachedTier = 2 })), Is.EqualTo(double.PositiveInfinity), "at its cap");
            Assert.That(BotSkillGrowth.HoursToNextTier(default), Is.EqualTo(double.PositiveInfinity));
        }

        // ---- lines ---------------------------------------------------------------------------------------------------

        [Test]
        public void The_log_lines_carry_tier_facets_and_the_budget_per_realm()
        {
            BotPersonKey person = BotPersonKey.Of(12);
            BotSkillProfile profile = BotSkillProfile.For(person, null, RvrBotAwareness.Normal);
            string line = BotSkill.ProfileLine("Aldwin", person, eRealm.Albion, 34, profile, RvrBotAwareness.Normal);
            Assert.That(line, Does.StartWith("BOT_SKILL_PROFILE name=Aldwin person=c:12 realm=alb level=34 tier="));
            foreach (BotSkillFacet facet in BotSkillFacets.All)
                Assert.That(line, Does.Contain(" " + BotSkillProfile.FacetName(facet) + "="));
            Assert.That(line, Does.EndWith("xp_h=0.0 awareness=normal"));

            var grown = BotSkillProfile.For(person, null, RvrBotAwareness.Normal, BotSkillExperience.None with { ReachedTier = (int)profile.MaxTier });
            Assert.That(BotSkill.GrowthLine("Aldwin", person, eRealm.Albion, profile, grown),
                Does.StartWith($"BOT_SKILL_GROWTH name=Aldwin person=c:12 realm=alb from={BotSkillProfile.TierName(profile.Tier)} to={BotSkillProfile.TierName(grown.Tier)}"));

            var online = Enumerable.Range(1, 100).Select(i => (Realm: 1 + i % 3, Profile: BotSkillProfile.For(BotPersonKey.Of(i), null, RvrBotAwareness.Normal)))
                .Append((Realm: 1, Profile: default(BotSkillProfile))).ToArray();
            string budget = BotSkill.BudgetLine(300, online, [5, 1, 0, 0, 0], 12.5, 1, 30, 70);
            Assert.That(budget, Does.StartWith("BOT_SKILL_BUDGET seconds=300 online=100 alb="));
            Assert.That(budget, Does.Contain("credits=fight:5,death:1,pull:0,buff:0,rest:0 minutes=12.5 promotions=1 profile_lines=30 suppressed=70"));
            int albion = online.Count(o => o.Realm == 1 && !o.Profile.IsNone);
            string alb = budget.Split(' ').Single(part => part.StartsWith("alb=", StringComparison.Ordinal))[4..];
            Assert.That(alb.Split('/').Select(int.Parse).Sum(), Is.EqualTo(albion));
            Assert.That(BotSkill.BudgetLine(300, [], null, 0, 0, 0, 0), Does.Contain("online=0 alb=0/0/0").And.Contain("shares=0/0/0"));

            var why = BotSkill.WhyLines(profile, true).ToArray();
            Assert.That(why[0], Does.StartWith("Skill: " + BotSkillProfile.TierName(profile.Tier)));
            Assert.That(why[2], Does.Contain("hours of fighting"));
            Assert.That(BotSkill.WhyLines(default, false).Single(), Does.Contain("off"));
        }

        // ---- saving --------------------------------------------------------------------------------------------------

        [Test]
        public void Experience_saves_to_sqlite_and_the_profile_comes_back_the_same()
        {
            string folder = TemporarySqlite.NewFolder("botskill");
            try
            {
                var database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                    $"Data Source={Path.Combine(folder, "skill.db")};Pooling=False");
                foreach (Type type in BotPeopleTables.RecordTypes)
                    database.RegisterDataObject(type);
                var store = new BotPeopleStore(() => database);
                BotPersonKey person = Enumerable.Range(1, 1_000).Select(i => BotPersonKey.Of(i))
                    .First(p => BotSkillProfile.For(p, null, RvrBotAwareness.Normal).StartTier == RvrSkillTier.Casual);
                var personality = Personality(person.Id);
                var now = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

                var (before, after) = BotSkill.Learn(store, person, person.Id, eRealm.Midgard, personality, RvrBotAwareness.Normal,
                    BotSkillGrowth.Gain(BotSkillEvent.FightWon, 60_000), now);
                Assert.That(before.Experience.Total, Is.Zero);
                Assert.That(after.Experience.Mean, Is.EqualTo(1).Within(1e-9));
                Assert.That(store.Skill.TryGet(person, out var row) && row.Tier == (int)RvrSkillTier.Casual && row.Realm == 2, Is.True);
                store.Flush(wait: true);

                // Forty-odd hours later (in one lesson, as a test): a promotion, saved with its tier.
                var big = new BotSkillExperience { Reactions = 3_000, Positioning = 3_000, Pulls = 3_000, Fire = 3_000, Buffs = 3_000, Power = 3_000, ReachedTier = -1 };
                (before, after) = BotSkill.Learn(store, person, person.Id, eRealm.Midgard, personality, RvrBotAwareness.Normal, big, now.AddDays(2));
                Assert.That((before.Tier, after.Tier), Is.EqualTo((RvrSkillTier.Casual, RvrSkillTier.Average)));
                store.Flush(wait: true);

                var reloaded = new BotPeopleStore(() => database);
                reloaded.LoadFrom(database);
                Assert.That(reloaded.Skill.TryGet(person, out var saved), Is.True);
                Assert.That(saved.Tier, Is.EqualTo((int)RvrSkillTier.Average));
                Assert.That(saved.PullsXp, Is.EqualTo(3_001).Within(1e-9));
                Assert.That(saved.CharacterId, Is.EqualTo(person.Id));
                Assert.That(saved.UpdatedUtc, Is.EqualTo("2026-11-19T10:00:00.0000000Z"));
                BotSkillProfile back = BotSkill.ProfileOf(person, personality, RvrBotAwareness.Normal, reloaded);
                Assert.That(back, Is.EqualTo(after), "the same profile after a restart");

                // Lowering the knob afterwards keeps the promotion (never falls) as far as one tier above the new start.
                BotSkillProfile easy = BotSkill.ProfileOf(person, personality, RvrBotAwareness.Easy, reloaded);
                Assert.That(easy.Tier, Is.EqualTo(RvrSkillTier.Average));
                Assert.That(BotSkill.ProfileOf(BotPersonKey.Of(999_999), null, RvrBotAwareness.Normal, reloaded).Experience.Total, Is.Zero);
                Assert.That(BotSkill.Learn(reloaded, BotPersonKey.None, 0, eRealm.None, null, RvrBotAwareness.Normal, big, now).After.IsNone, Is.True);
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        [Test]
        public void An_unreadable_row_counts_as_no_experience()
        {
            var row = new OfflineBotSkillRecord { PersonKey = "c:5", Tier = 7, ReactionsXp = double.NaN, PullsXp = -3, PowerXp = 12 };
            BotSkillExperience experience = BotSkillExperience.From(row);
            Assert.That((experience.Reactions, experience.Pulls, experience.Power, experience.ReachedTier), Is.EqualTo((0.0, 0.0, 12.0, -1)));
            Assert.That(BotSkillExperience.From(null), Is.EqualTo(BotSkillExperience.None));
            var copy = new OfflineBotSkillRecord { Tier = 1 };
            BotSkillExperience.None.CopyTo(copy);
            Assert.That(copy.Tier, Is.EqualTo(1), "no tier recorded leaves the row's tier alone");
        }
    }
}
