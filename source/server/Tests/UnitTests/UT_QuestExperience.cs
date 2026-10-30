using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Quest XP at the player's pace (2026-10-30): the quest_xp_by_level setting, the xp_rate_by_level rate on quest
    /// payouts, the one-level cap and the percentage the offer window shows (QuestExperience).
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_QuestExperience
    {
        private const string OwnersPlan = "1-19:3;20-34:5;35-50:12";

        // Real level widths from GamePlayer's XP table.
        private static readonly long Level25Span = GamePlayer.GetExperienceAmountForLevel(25) - GamePlayer.GetExperienceAmountForLevel(24);
        private static readonly long Level40Span = GamePlayer.GetExperienceAmountForLevel(40) - GamePlayer.GetExperienceAmountForLevel(39);

        private bool _questByLevel;
        private double _playerRate;
        private string _playerByLevel;

        [SetUp]
        public void SaveSettings()
        {
            _questByLevel = Properties.QUEST_XP_BY_LEVEL;
            _playerRate = Properties.XP_RATE;
            _playerByLevel = Properties.XP_RATE_BY_LEVEL;
        }

        [TearDown]
        public void RestoreSettings()
        {
            Properties.QUEST_XP_BY_LEVEL = _questByLevel;
            Properties.XP_RATE = _playerRate;
            Properties.XP_RATE_BY_LEVEL = _playerByLevel;
        }

        [Test]
        public void TheLevelWidthsAreTheOnesTheOwnerPlaysWith()
        {
            Assert.That(Level25Span, Is.EqualTo(53_000_000L));
            Assert.That(Level40Span, Is.EqualTo(4_300_000_000L));
        }

        // The rate

        [TestCase(1, 3.0)]
        [TestCase(19, 3.0)]
        [TestCase(20, 5.0)]
        [TestCase(34, 5.0)]
        [TestCase(35, 12.0)]
        [TestCase(50, 12.0)]
        public void TheRateIsTheRangesRateForTheLevel(int level, double expected)
        {
            Assert.That(QuestExperience.Rate(level, true, OwnersPlan), Is.EqualTo(expected));
        }

        [Test]
        public void SettingOffLeavesQuestXpAsItWas()
        {
            Assert.That(QuestExperience.Rate(40, false, OwnersPlan), Is.EqualTo(1.0), "a quest never used a rate");
            Assert.That(QuestExperience.Rate(40, false, OwnersPlan, 3.0), Is.EqualTo(3.0), "a bounty keeps its xp_rate");
        }

        [TestCase((string) null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not a setting")]
        [TestCase("1-30:2;20-50:4", TestName = "Overlapping ranges are invalid")]
        public void NoRangeSettingLeavesQuestXpAsItWas(string spec)
        {
            Assert.That(QuestExperience.Rate(25, true, spec), Is.EqualTo(1.0));
            Assert.That(QuestExperience.Rate(25, true, spec, 3.0), Is.EqualTo(3.0), "a bounty keeps its xp_rate");
        }

        [Test]
        public void ALevelOutsideEveryRangeKeepsWhatThePayoutUsedBefore()
        {
            Assert.That(QuestExperience.Rate(10, true, "35-50:12"), Is.EqualTo(1.0), "quests: no rate, as before");
            Assert.That(QuestExperience.Rate(10, true, "35-50:12", 3.0), Is.EqualTo(3.0), "bounties: xp_rate, as before");
            Assert.That(QuestExperience.Rate(40, true, "35-50:12", 3.0), Is.EqualTo(12.0), "inside the range it replaces xp_rate, it does not multiply it");
        }

        [Test]
        public void TheLiveRateFollowsTheServerSettings()
        {
            Properties.XP_RATE = 3;
            Properties.XP_RATE_BY_LEVEL = OwnersPlan;

            Properties.QUEST_XP_BY_LEVEL = true;
            Assert.That(QuestExperience.Rate(25), Is.EqualTo(5.0));
            Assert.That(QuestExperience.Rate(25, Properties.XP_RATE), Is.EqualTo(5.0), "a bounty's xp_rate is replaced inside a range");

            Properties.QUEST_XP_BY_LEVEL = false;
            Assert.That(QuestExperience.Rate(25), Is.EqualTo(1.0));
            Assert.That(QuestExperience.Rate(25, Properties.XP_RATE), Is.EqualTo(3.0));

            Properties.QUEST_XP_BY_LEVEL = true;
            Properties.XP_RATE_BY_LEVEL = "";
            Assert.That(QuestExperience.Rate(25), Is.EqualTo(1.0), "no range set: quests pay as before, xp_rate stays a kill rate");
            Assert.That(LevelRangeXpRates.PlayerRate(25), Is.EqualTo(3.0), "kills still get xp_rate");
        }

        [Test]
        public void PlayerRateWithAFlatRateSharesTheKillRatesRanges()
        {
            Properties.XP_RATE = 3;
            Properties.XP_RATE_BY_LEVEL = "35-50:12";
            Assert.That(LevelRangeXpRates.PlayerRate(10), Is.EqualTo(3.0), "kills outside the range: xp_rate");
            Assert.That(LevelRangeXpRates.PlayerRate(10, 1.0), Is.EqualTo(1.0), "quests outside the range: nothing");
            Assert.That(LevelRangeXpRates.PlayerRate(40, 1.0), Is.EqualTo(12.0));
        }

        // The amount

        [TestCase(1_000L, 1.0, 1_000L)]
        [TestCase(1_000L, 12.0, 12_000L)]
        [TestCase(1_001L, 1.5, 1_501L, TestName = "Fractions are rounded down, as kill XP is")]
        [TestCase(100L, 1.15, 115L, TestName = "Decimal rates multiply exactly")]
        [TestCase(0L, 12.0, 0L)]
        [TestCase(-5L, 12.0, 0L)]
        [TestCase(1_000L, 0.0, 0L)]
        [TestCase(1_000L, -1.0, 0L)]
        [TestCase(1_000L, double.NaN, 0L)]
        [TestCase(1_000L, 5000.0, 1_000_000L, TestName = "A rate above the maximum counts as the maximum")]
        public void ScaledMultipliesTheBaseByTheRate(long baseXp, double rate, long expected)
        {
            Assert.That(QuestExperience.Scaled(baseXp, rate), Is.EqualTo(expected));
        }

        [Test]
        public void OnePaymentPaysAtMostOneLevel()
        {
            Assert.That(QuestExperience.Capped(1_000L, 5_000L), Is.EqualTo(1_000L), "under the level width it is unchanged");
            Assert.That(QuestExperience.Capped(5_000L, 5_000L), Is.EqualTo(5_000L), "exactly one level is allowed");
            Assert.That(QuestExperience.Capped(12_000L, 5_000L), Is.EqualTo(5_000L), "more is cut to one level");
            Assert.That(QuestExperience.Capped(12_000L, 0L), Is.EqualTo(12_000L), "no width known: no cap");
            Assert.That(QuestExperience.Capped(0L, 5_000L), Is.EqualTo(0L));
        }

        [Test]
        public void ATypicalQuestAtTheOwnersRates()
        {
            Properties.QUEST_XP_BY_LEVEL = true;
            Properties.XP_RATE_BY_LEVEL = OwnersPlan;

            // 0.36 pays about a bubble (10% of the level) for a typical quest. At level 25 the plan's 5x makes it half a level.
            long bubble25 = Level25Span / 10;
            Assert.That(QuestExperience.Payout(bubble25, 25, Level25Span), Is.EqualTo(bubble25 * 5));

            // At level 40 the plan's 12x would be 1.2 levels: the cap pays exactly one level.
            long bubble40 = Level40Span / 10;
            Assert.That(QuestExperience.Payout(bubble40, 40, Level40Span), Is.EqualTo(Level40Span));

            // With the setting off the 0.36 amount is paid, as before.
            Properties.QUEST_XP_BY_LEVEL = false;
            Assert.That(QuestExperience.Payout(bubble25, 25, Level25Span), Is.EqualTo(bubble25));
            Assert.That(QuestExperience.Payout(bubble40, 40, Level40Span), Is.EqualTo(bubble40));
        }

        [Test]
        public void ABountyKeepsXpRateOutsideTheRangesAndTakesTheRangeRateInsideThem()
        {
            Properties.QUEST_XP_BY_LEVEL = true;
            Properties.XP_RATE = 2;
            Properties.XP_RATE_BY_LEVEL = "35-50:12";

            // Two bulbs of level 25 (20% of its width), as BountyRewardService computes them at a rate of 1.
            long twoBulbs = Level25Span / 5;
            Assert.That(QuestExperience.Payout(twoBulbs, 25, Level25Span, Properties.XP_RATE), Is.EqualTo(twoBulbs * 2), "xp_rate, as before");

            long twoBulbs40 = Level40Span / 5;
            Assert.That(QuestExperience.Payout(twoBulbs40, 40, Level40Span, Properties.XP_RATE), Is.EqualTo(Level40Span),
                "12 x 20% is more than a level: the cap pays one level, not 2 x 12 x 20%");
            Assert.That(QuestExperience.Payout(Level40Span / 50, 40, Level40Span, Properties.XP_RATE), Is.EqualTo(Level40Span / 50 * 12),
                "under the cap the range rate replaces xp_rate");
        }

        // What the offer window shows

        [Test]
        public void TheOfferWindowShowsThePayoutAsAShareOfTheLevel()
        {
            Assert.That(QuestExperience.Percent(Level25Span / 10, Level25Span), Is.EqualTo(10));
            Assert.That(QuestExperience.Percent(Level25Span / 2, Level25Span), Is.EqualTo(50));
            Assert.That(QuestExperience.Percent(Level25Span, Level25Span), Is.EqualTo(100), "a capped payout shows a whole level");
            Assert.That(QuestExperience.Percent(Level25Span * 3, Level25Span), Is.EqualTo(100), "never above the byte the packet carries");
            Assert.That(QuestExperience.Percent(0, Level25Span), Is.EqualTo(0));
            Assert.That(QuestExperience.Percent(1_000, 0), Is.EqualTo(0));
        }

        [Test]
        public void TheOfferWindowMatchesThePayment()
        {
            Properties.QUEST_XP_BY_LEVEL = true;
            Properties.XP_RATE_BY_LEVEL = OwnersPlan;
            long bubble25 = Level25Span / 10;
            long paid = QuestExperience.Payout(bubble25, 25, Level25Span);
            Assert.That(QuestExperience.Percent(paid, Level25Span), Is.EqualTo(50));

            Properties.QUEST_XP_BY_LEVEL = false;
            paid = QuestExperience.Payout(bubble25, 25, Level25Span);
            Assert.That(QuestExperience.Percent(paid, Level25Span), Is.EqualTo(10));
        }

        [Test]
        public void TheSettingIsOnByDefault()
        {
            var property = typeof(Properties).GetField(nameof(Properties.QUEST_XP_BY_LEVEL))!
                .GetCustomAttributes(typeof(ServerPropertyAttribute), false);
            Assert.That(property, Has.Length.EqualTo(1));
            var attribute = (ServerPropertyAttribute) property[0];
            Assert.That(attribute.Key, Is.EqualTo("quest_xp_by_level"));
            Assert.That(attribute.DefaultValue, Is.EqualTo(true));
        }
    }
}
