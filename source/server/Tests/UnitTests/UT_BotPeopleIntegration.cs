using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots as people, work package 7 (integration; docs/BOTS-AS-PEOPLE-1.2.md): one tier per bot in every package, the RvR
    /// call-follow delay inside the group tier's band, the 5-minute summaries (one line per package, none missing, none
    /// twice), the AI_PROFILE scopes in use, and the review nits fixed here. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotPeopleIntegration
    {
        private const int Bots = 10_000;

        // ---- one tier per bot ------------------------------------------------------------------------------------------

        [Test]
        public void Every_package_rolls_a_bot_the_same_start_tier() => Assert.Multiple(() =>
        {
            foreach (RvrBotAwareness awareness in new[] { RvrBotAwareness.Easy, RvrBotAwareness.Normal, RvrBotAwareness.Veteran })
                for (long id = 1; id <= Bots; id++)
                {
                    BotPersonKey person = BotPersonKey.Of(id);
                    RvrSkillTier start = BotSkillProfile.For(person, null, awareness).StartTier;
                    Assert.That(BotSkillProfile.StartTierOf(person, awareness), Is.EqualTo(start), $"profile {id}");
                    Assert.That(BotWayFinding.TierOf(person, awareness), Is.EqualTo(start), $"way finding {id}");
                    Assert.That(BotMovementHabits.TierOf(id, awareness), Is.EqualTo(start), $"movement {id}");
                    if (id > 200)
                        break; // a sample per awareness for the equality; the shares below use all 10,000
                }
            var tiers = Enumerable.Range(1, Bots).Select(id => BotMovementHabits.TierOf(id, RvrBotAwareness.Normal)).ToArray();
            Assert.That(tiers.Count(t => t == RvrSkillTier.Casual) * 100.0 / Bots, Is.EqualTo(30).Within(2));
            Assert.That(tiers.Count(t => t == RvrSkillTier.Average) * 100.0 / Bots, Is.EqualTo(50).Within(2));
            Assert.That(tiers.Count(t => t == RvrSkillTier.Veteran) * 100.0 / Bots, Is.EqualTo(20).Within(2));
        });

        [Test]
        public void A_profile_tier_wins_over_the_start_roll_in_movement() => Assert.Multiple(() =>
        {
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
                Assert.That(BotMovementHabits.TierFor(tier, 77, RvrBotAwareness.Normal), Is.EqualTo(tier));
            Assert.That(BotMovementHabits.TierFor(null, 77, RvrBotAwareness.Normal),
                Is.EqualTo(BotSkillProfile.StartTierOf(BotPersonKey.Of(77), RvrBotAwareness.Normal)));
        });

        // ---- the RvR call-follow delay (WP2's open point) --------------------------------------------------------------

        [Test]
        public void A_members_follow_delay_stays_in_its_group_tiers_band_and_keeps_the_mean() => Assert.Multiple(() =>
        {
            var random = new Random(11);
            foreach (RvrSkillTier tier in Enum.GetValues<RvrSkillTier>())
            {
                var (min, max) = RvrAssist.FollowDelayRange(tier);
                double today = 0, moved = 0, sharp = 0, slow = 0;
                const int n = 20_000;
                for (int i = 0; i < n; i++)
                {
                    long draw = RvrAssist.FollowDelay(tier, i + 1, 1_000 + i * 37);
                    double position = random.NextDouble();
                    long delay = BotSkillMistakes.FollowDelayMilliseconds(tier, position, draw);
                    Assert.That(delay, Is.InRange(min, max), $"{tier} in the group tier's band (D5)");
                    today += draw;
                    moved += delay;
                    sharp += BotSkillMistakes.FollowDelayMilliseconds(tier, 0.95, draw);
                    slow += BotSkillMistakes.FollowDelayMilliseconds(tier, 0.05, draw);
                }
                Assert.That(moved / n, Is.EqualTo(today / n).Within((max - min) * 0.03), $"{tier}: the group's mean stays today's");
                Assert.That(sharp / n, Is.LessThan(slow / n - (max - min) * 0.3), $"{tier}: quick reactions follow sooner");
            }
            Assert.That(BotSkillMistakes.FollowDelayMilliseconds(RvrSkillTier.Casual, 0.5, -5), Is.InRange(1_000, 4_000), "a draw out of band is clamped");
            Assert.That(BotSkillMistakes.FollowDelayMilliseconds(RvrSkillTier.Veteran, double.NaN, 99_999), Is.InRange(300, 1_500));
        });

        // ---- review nits fixed in WP7 ----------------------------------------------------------------------------------

        [Test]
        public void After_login_a_first_death_teaches_and_a_fight_credits_only_the_time_online() => Assert.Multiple(() =>
        {
            const long login = 5_000_000;
            long death = BotSkillGrowth.ClockStart(BotSkillEvent.Death, login);
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.Death, login + 60_000 - death).Positioning,
                Is.EqualTo(BotSkillGrowth.DeathLessonMinutes), "a death one minute after login is a lesson");
            foreach (BotSkillEvent kind in new[] { BotSkillEvent.PullMade, BotSkillEvent.BuffCast, BotSkillEvent.RestedForPower })
                Assert.That(BotSkillGrowth.Gain(kind, login - BotSkillGrowth.ClockStart(kind, login)).Total, Is.GreaterThan(0), kind.ToString());
            Assert.That(BotSkillGrowth.ClockStart(BotSkillEvent.FightWon, login), Is.EqualTo(login), "a fight credits the time since login");
            Assert.That(BotSkillGrowth.Gain(BotSkillEvent.FightWon, 30_000).Reactions, Is.EqualTo(0.5).Within(1e-9));
        });

        // ---- the 5-minute summaries ------------------------------------------------------------------------------------

        private static readonly string[] SummaryTags =
            ["BOT_MOVE_HABITS", "BOT_PLACE_BUDGET", "BOT_SKILL_BUDGET", "BOT_SKILL_MISTAKES", "BOT_WAY_SUMMARY"];

        [Test]
        public void Each_built_package_logs_one_summary_line_after_BOT_PEOPLE_SUMMARY()
        {
            string[] names = ["skill", "lost", "ask", "wall"];
            bool[] built = names.Select(BotPeopleTest.IsBuilt).ToArray();
            try
            {
                BotSkill.Wire();
                BotSkillHands.Wire();
                BotWayFinding.Register(); // and BotPlaceKnowledge.Register
                BotMovementHabitsRuntime.RegisterSeams();
                BotMovementHabitsRuntime.RegisterSeams(); // twice: a tag is replaced, never doubled
            }
            finally
            {
                // The /lstest and /lswhy stubs are other fixtures' subject: leave them as they were.
                for (int i = 0; i < names.Length; i++)
                    if (!built[i])
                        BotPeopleTest.Unregister(names[i]);
                BotPeopleWhy.Unregister(BotPeopleWhy.Topic.Skill);
                BotPeopleWhy.Unregister(BotPeopleWhy.Topic.Way);
            }
            Assert.That(BotPeopleSummary.Tags, Is.SupersetOf(SummaryTags));
            var lines = BotPeopleSummary.TakeLines(300);
            Assert.Multiple(() =>
            {
                foreach (string tag in SummaryTags)
                    Assert.That(lines.Count(line => line.StartsWith(tag + " ", StringComparison.Ordinal)), Is.EqualTo(1), tag);
                foreach (string line in lines.Where(line => SummaryTags.Any(tag => line.StartsWith(tag + " ", StringComparison.Ordinal))))
                    Assert.That(line, Does.Contain(" seconds=300"), "the window is named");
                Assert.That(lines, Has.None.StartsWith("BOT_PEOPLE_SUMMARY"), "the store logs its own line first, once");
                Assert.That(BotPeopleSummary.Tags, Has.None.StartsWith("BOT_MILESTONE"), "work package 6 is not built");
            });
        }

        [Test]
        public void Each_summary_tag_is_logged_only_through_the_summary_and_each_package_uses_its_profiler_scope()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("the server source is not beside the tests.");
            string bots = Path.Combine(dir, "source", "server", "GameServer", "bots");
            string source = string.Join("\n", Directory.GetFiles(bots, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Multiple(() =>
            {
                foreach (string tag in SummaryTags)
                    Assert.That(Regex.Matches(source, $@"(Log\.Info|Info)\(\$?""{tag} ").Count, Is.EqualTo(0),
                        $"{tag} is written by BotPeopleStore's timer only");
                foreach (string system in new[] { "BotSkill", "PlaceKnowledge", "WayFinding", "MovementHabits" })
                    Assert.That(source, Does.Contain($"AiProfile.Scope(AiSystem.{system})"), system);
            });
        }
    }
}
