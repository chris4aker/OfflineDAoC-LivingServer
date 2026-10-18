using NUnit.Framework;
using Economy = DOL.GS.AutonomousBotGuildEconomy;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotGuildEconomy
    {
        [TestCase(1000, true, 5, 50, 950)]
        [TestCase(1000, false, 5, 0, 1000)]
        [TestCase(1000, true, 0, 0, 1000)]
        [TestCase(99, true, 5, 4, 95)]
        [TestCase(1000, true, 250, 1000, 0)]
        [TestCase(0, true, 5, 0, 0)]
        [TestCase(-5, true, 5, 0, 0)]
        public void SplitDues_IsThePlayerFormulaAndConservesCoin(long copper, bool duesOn, long percent, long toGuild, long toBot)
        {
            (long guild, long bot) = Economy.SplitDues(copper, duesOn, percent);
            Assert.That((guild, bot), Is.EqualTo((toGuild, toBot)));
            if (copper > 0)
                Assert.That(guild + bot, Is.EqualTo(copper), "Dues never create or destroy coin.");
        }

        [TestCase(2, 6)]
        [TestCase(25, 100)]
        [TestCase(50, 250)]
        [TestCase(0, 0)]
        public void MeritForLevel_MatchesGuildEvents(int level, int expected)
        {
            Assert.That(Economy.MeritForLevel(level), Is.EqualTo(expected));
        }

        [Test]
        public void MeritForLevels_OneAwardPerLevelGained()
        {
            Assert.That(Economy.MeritForLevels(9, 10), Is.EqualTo(Economy.MeritForLevel(10)));
            Assert.That(Economy.MeritForLevels(9, 11), Is.EqualTo(Economy.MeritForLevel(10) + Economy.MeritForLevel(11)));
            Assert.That(Economy.MeritForLevels(10, 10), Is.Zero);
            Assert.That(Economy.MeritForLevels(12, 10), Is.Zero);
        }

        [Test]
        public void DefaultDues_SocialGuildsChargeNone()
        {
            Assert.That(Economy.DefaultDuesPercent(BotGuildCharter.Social), Is.Zero);
            Assert.That(Economy.DefaultDuesPercent(BotGuildCharter.Rvr), Is.GreaterThan(Economy.DefaultDuesPercent(BotGuildCharter.Leveling)));
        }

        [Test]
        public void BuffFor_FollowsTheCharterAndWhatTheServerOffers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Economy.BuffFor(BotGuildCharter.Rvr, true, true, true), Is.EqualTo(Guild.eBonusType.RealmPoints));
                Assert.That(Economy.BuffFor(BotGuildCharter.Rvr, true, false, true), Is.EqualTo(Guild.eBonusType.Experience));
                Assert.That(Economy.BuffFor(BotGuildCharter.Crafting, true, true, true), Is.EqualTo(Guild.eBonusType.CraftingHaste));
                Assert.That(Economy.BuffFor(BotGuildCharter.Leveling, true, true, true), Is.EqualTo(Guild.eBonusType.Experience));
                Assert.That(Economy.BuffFor(BotGuildCharter.Leveling, false, true, false), Is.EqualTo(Guild.eBonusType.RealmPoints));
                Assert.That(Economy.BuffFor(BotGuildCharter.Social, false, false, false), Is.EqualTo(Guild.eBonusType.None));
            });
        }
    }
}
