using System.Reflection;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public sealed class UT_BotXpRateSeparation
    {
        private double _playerRate;
        private double _botRate;
        private double _rvrRate;
        private string _playerByLevel;
        private string _botByLevel;

        [SetUp]
        public void SaveRates()
        {
            _playerRate = Properties.XP_RATE;
            _botRate = Properties.BOT_XP_RATE;
            _rvrRate = Properties.RvR_XP_RATE;
            _playerByLevel = Properties.XP_RATE_BY_LEVEL;
            _botByLevel = Properties.BOT_XP_RATE_BY_LEVEL;
            Properties.XP_RATE_BY_LEVEL = "";
            Properties.BOT_XP_RATE_BY_LEVEL = "";
        }

        [TearDown]
        public void RestoreRates()
        {
            Properties.XP_RATE = _playerRate;
            Properties.BOT_XP_RATE = _botRate;
            Properties.RvR_XP_RATE = _rvrRate;
            Properties.XP_RATE_BY_LEVEL = _playerByLevel;
            Properties.BOT_XP_RATE_BY_LEVEL = _botByLevel;
        }

        private static long Scale(long experience, bool isRvR, int level)
        {
            MethodInfo scale = typeof(GameBot).GetMethod("ScaleAutonomousExperience",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return (long)scale.Invoke(null, new object[] { experience, isRvR, level })!;
        }

        [Test]
        public void AutonomousRateDoesNotReadOrModifyPlayerRate()
        {
            Properties.XP_RATE = 10;
            Properties.BOT_XP_RATE = 3;
            Properties.RvR_XP_RATE = 2;

            Assert.That(Scale(100L, false, 20), Is.EqualTo(300L));
            Assert.That(Scale(100L, true, 20), Is.EqualTo(600L));
            Assert.That(Properties.XP_RATE, Is.EqualTo(10));
        }

        [TestCase(1, 100L)]
        [TestCase(2, 200L)]
        [TestCase(3, 300L)]
        [TestCase(5, 500L)]
        [TestCase(10, 1000L)]
        public void LauncherPresetsScaleAutonomousExperience(double rate, long expected)
        {
            Properties.BOT_XP_RATE = rate;
            Properties.RvR_XP_RATE = 1;
            Assert.That(Scale(100L, false, 20), Is.EqualTo(expected));
        }

        [TestCase(5, 150L)]
        [TestCase(19, 150L)]
        [TestCase(20, 250L)]
        [TestCase(34, 250L)]
        [TestCase(35, 600L)]
        [TestCase(49, 600L)]
        public void BotRateByLevelReplacesTheFlatRateInsideEachRange(int level, long expected)
        {
            Properties.BOT_XP_RATE = 4;
            Properties.RvR_XP_RATE = 1;
            Properties.BOT_XP_RATE_BY_LEVEL = "1-19:1.5;20-34:2.5;35-50:6";
            Assert.That(Scale(100L, false, level), Is.EqualTo(expected));
        }

        [Test]
        public void BotRateByLevelStillUsesTheRvRZoneMultiplier()
        {
            Properties.BOT_XP_RATE = 1;
            Properties.RvR_XP_RATE = 2;
            Properties.BOT_XP_RATE_BY_LEVEL = "35-50:6";
            Assert.That(Scale(100L, true, 40), Is.EqualTo(1200L));
            Assert.That(Scale(100L, true, 30), Is.EqualTo(200L), "a level outside every range keeps bot_xp_rate");
        }

        [Test]
        public void BotRateByLevelNeverChangesThePlayerRate()
        {
            Properties.XP_RATE = 7;
            Properties.BOT_XP_RATE_BY_LEVEL = "1-50:2";
            Assert.That(LevelRangeXpRates.PlayerRate(10), Is.EqualTo(7));
        }

        [Test]
        public void InvalidBotSettingKeepsTheFlatRate()
        {
            Properties.BOT_XP_RATE = 3;
            Properties.RvR_XP_RATE = 1;
            Properties.BOT_XP_RATE_BY_LEVEL = "1-30:2;20-50:4";
            Assert.That(Scale(100L, false, 25), Is.EqualTo(300L));
        }
    }
}
