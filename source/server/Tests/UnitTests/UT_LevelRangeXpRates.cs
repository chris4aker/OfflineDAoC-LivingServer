using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The optional xp_rate_by_level and bot_xp_rate_by_level settings.</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_LevelRangeXpRates
    {
        private double _playerRate;
        private double _rvrRate;
        private string _playerByLevel;

        [SetUp]
        public void SaveRates()
        {
            _playerRate = Properties.XP_RATE;
            _rvrRate = Properties.RvR_XP_RATE;
            _playerByLevel = Properties.XP_RATE_BY_LEVEL;
        }

        [TearDown]
        public void RestoreRates()
        {
            Properties.XP_RATE = _playerRate;
            Properties.RvR_XP_RATE = _rvrRate;
            Properties.XP_RATE_BY_LEVEL = _playerByLevel;
        }

        [Test]
        public void TheOwnersPlanParses()
        {
            Assert.That(LevelRangeXpRates.TryParse("1-19:3;20-34:5;35-50:12", out var ranges, out string error), Is.True, error);
            Assert.That(ranges, Has.Length.EqualTo(3));
            Assert.That(ranges[0], Is.EqualTo(new LevelRangeXpRates.LevelRange(1, 19, 3)));
            Assert.That(ranges[1], Is.EqualTo(new LevelRangeXpRates.LevelRange(20, 34, 5)));
            Assert.That(ranges[2], Is.EqualTo(new LevelRangeXpRates.LevelRange(35, 50, 12)));
        }

        [TestCase(" 1 - 19 : 1.5 ; 20-34:2.5 ;", TestName = "Spaces and a trailing semicolon are allowed")]
        [TestCase("50:2", TestName = "A single level needs no dash")]
        [TestCase("35-50:0.5", TestName = "Rates below 1 slow leveling down")]
        [TestCase("1-50:1000", TestName = "The maximum rate is allowed")]
        public void ValidSettingsParse(string spec)
        {
            Assert.That(LevelRangeXpRates.TryParse(spec, out _, out string error), Is.True, error);
        }

        [TestCase("", TestName = "Empty")]
        [TestCase("   ", TestName = "Only spaces")]
        [TestCase(";;", TestName = "Only separators")]
        [TestCase("1-19", TestName = "No rate")]
        [TestCase("1-19:", TestName = "Empty rate")]
        [TestCase("1-19:abc", TestName = "Rate is not a number")]
        [TestCase("1-19:0", TestName = "Zero rate")]
        [TestCase("1-19:-2", TestName = "Negative rate")]
        [TestCase("1-19:1001", TestName = "Rate above the maximum")]
        [TestCase("1-19:NaN", TestName = "Rate is NaN")]
        [TestCase("19-1:3", TestName = "Range ends before it starts")]
        [TestCase("0-19:3", TestName = "Level 0")]
        [TestCase("a-19:3", TestName = "Level is not a number")]
        [TestCase("1-19:3:4", TestName = "Two colons")]
        [TestCase("1-30:2;20-50:4", TestName = "Overlapping ranges")]
        [TestCase("1-19:3,5", TestName = "Decimal comma")]
        public void InvalidSettingsAreRejected(string spec)
        {
            Assert.That(LevelRangeXpRates.TryParse(spec, out var ranges, out string error), Is.False);
            Assert.That(ranges, Is.Empty);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [TestCase(1, 3.0)]
        [TestCase(19, 3.0)]
        [TestCase(20, 5.0)]
        [TestCase(34, 5.0)]
        [TestCase(35, 12.0)]
        [TestCase(50, 12.0)]
        public void EachLevelGetsItsRangesRate(int level, double expected)
        {
            Assert.That(LevelRangeXpRates.EffectiveRate("1-19:3;20-34:5;35-50:12", level, 1.0), Is.EqualTo(expected));
        }

        [Test]
        public void LevelsOutsideEveryRangeKeepTheFlatRate()
        {
            Assert.That(LevelRangeXpRates.EffectiveRate("35-50:12", 10, 2.0), Is.EqualTo(2.0));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("not a setting")]
        public void EmptyOrInvalidSettingsKeepTheFlatRate(string spec)
        {
            Assert.That(LevelRangeXpRates.EffectiveRate(spec, 25, 4.0), Is.EqualTo(4.0));
        }

        [Test]
        public void PlayerRateFollowsTheSetting()
        {
            Properties.XP_RATE = 1;
            Properties.XP_RATE_BY_LEVEL = "";
            Assert.That(LevelRangeXpRates.PlayerRate(40), Is.EqualTo(1), "off by default");

            Properties.XP_RATE_BY_LEVEL = "1-19:3;20-34:5;35-50:12";
            Assert.That(LevelRangeXpRates.PlayerRate(10), Is.EqualTo(3));
            Assert.That(LevelRangeXpRates.PlayerRate(40), Is.EqualTo(12));

            Properties.XP_RATE_BY_LEVEL = "1-19:2";
            Assert.That(LevelRangeXpRates.PlayerRate(10), Is.EqualTo(2), "a changed setting is picked up");
            Assert.That(LevelRangeXpRates.PlayerRate(40), Is.EqualTo(1), "levels outside the ranges keep xp_rate");
        }

        [Test]
        public void RvRZonesKeepThePlayerRate()
        {
            // A 10x world with the default rvr_zones_xp_rate: battlegrounds and DF are 10x too.
            Properties.XP_RATE = 10;
            Properties.RvR_XP_RATE = 1;
            Properties.XP_RATE_BY_LEVEL = "";
            Assert.That(LevelRangeXpRates.PlayerRate(20, true), Is.EqualTo(10));
            Assert.That(LevelRangeXpRates.PlayerRate(20, false), Is.EqualTo(10));
        }

        [Test]
        public void RvRZoneRateMultipliesThePlayerRate()
        {
            Properties.XP_RATE = 3;
            Properties.RvR_XP_RATE = 2;
            Properties.XP_RATE_BY_LEVEL = "35-50:12";
            Assert.That(LevelRangeXpRates.PlayerRate(20, true), Is.EqualTo(6));
            Assert.That(LevelRangeXpRates.PlayerRate(40, true), Is.EqualTo(24), "the level range rate is used in RvR too");
            Assert.That(LevelRangeXpRates.PlayerRate(40, false), Is.EqualTo(12), "outside RvR the zone rate is not applied");
        }
    }
}
