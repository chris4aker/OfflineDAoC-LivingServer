using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using Economy = DOL.GS.AutonomousBotGuildEconomy;

namespace DOL.GS.Tests
{
    [TestFixture, NonParallelizable]
    public class UT_AutonomousBotGuildEconomy
    {
        // Guild loads data from GameServer in its static constructor (UT_AutonomousBotGuilds).
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => EmptyDatabase; }
        private GameServer _previousServer;
        private bool _previousPlayerGuildEconomy, _previousGuilds, _previousBotGuildEconomy;

        [SetUp]
        public void InstallServerDouble()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _previousPlayerGuildEconomy = Properties.BOT_PLAYER_GUILD_ECONOMY;
            _previousGuilds = Properties.BOT_GUILDS_ENABLED;
            _previousBotGuildEconomy = Properties.BOT_GUILD_ECONOMY;
        }

        [TearDown]
        public void RestoreServer()
        {
            Properties.BOT_PLAYER_GUILD_ECONOMY = _previousPlayerGuildEconomy;
            Properties.BOT_GUILDS_ENABLED = _previousGuilds;
            Properties.BOT_GUILD_ECONOMY = _previousBotGuildEconomy;
            GameServer.LoadTestDouble(_previousServer);
        }

        private static Guild TestGuild(string id, bool duesOn, long percent, double bank = 0)
        {
            var guild = new Guild(new DbGuild { GuildName = "Guild " + id, GuildID = id, Realm = (byte)eRealm.Albion, Bank = bank });
            guild.SetGuildDues(duesOn);
            guild.SetGuildDuesPercent(percent);
            return guild;
        }
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
        // ---- Living Server 1.1.2: gamebots in a player's guild, realm rank merit (owner 2026-11-03, a r/daoc report) ----

        [TestCase(1, 0)] [TestCase(9, 0)]
        [TestCase(10, 0)] [TestCase(20, 0)] [TestCase(40, 0)]   // RR2-RR5 L0 earn nothing, as GuildEvents.RealmRankUp
        [TestCase(11, 9)] [TestCase(19, 9)]                      // RR2L1-L9: (3 x 1)^2
        [TestCase(21, 36)] [TestCase(45, 144)]
        [TestCase(50, 225)] [TestCase(51, 225)]                  // RR6L0 is the first round rank that counts
        [TestCase(61, 324)] [TestCase(100, 900)]
        public void MeritForRealmLevel_MatchesGuildEventsRealmRankUp(int realmLevel, int expected)
        {
            Assert.That(Economy.MeritForRealmLevel(realmLevel), Is.EqualTo(expected));
            Assert.That(Economy.MeritForRealmLevel(realmLevel), Is.EqualTo(GuildEventsRealmRankUp(realmLevel)));
        }

        // GuildEvents.RealmRankUp as written, for one realm level reached.
        private static int GuildEventsRealmRankUp(int realmLevel)
        {
            if (realmLevel % 10 == 0)
                return realmLevel > 45 ? (int)System.Math.Pow(3 * (realmLevel / 10 + 1 - 1), 2) : 0;
            if (realmLevel > 60 || realmLevel > 10)
                return (int)System.Math.Pow(3 * ((int)System.Math.Floor(realmLevel * 0.1) + 1 - 1), 2);
            return 0;
        }

        [Test]
        public void MeritForRealmLevel_EveryRealmLevelMatchesGuildEvents()
        {
            for (int realmLevel = 0; realmLevel <= 130; realmLevel++)
                Assert.That(Economy.MeritForRealmLevel(realmLevel), Is.EqualTo(GuildEventsRealmRankUp(realmLevel)), $"realm level {realmLevel}");
        }

        [Test]
        public void MeritForRealmLevels_OneAwardPerRealmLevelGained()
        {
            Assert.That(Economy.MeritForRealmLevels(10, 12), Is.EqualTo(9 + 9));
            Assert.That(Economy.MeritForRealmLevels(49, 51), Is.EqualTo(225 + 225));
            Assert.That(Economy.MeritForRealmLevels(0, 9), Is.Zero);
            Assert.That(Economy.MeritForRealmLevels(30, 30), Is.Zero);
            Assert.That(Economy.MeritForRealmLevels(31, 30), Is.Zero);
        }

        [Test]
        public void EconomyGuild_APlayersGuildFollowsItsOwnSwitch()
        {
            Guild player = TestGuild("player", true, 10), bots = TestGuild("bots", true, 5);
            Assert.Multiple(() =>
            {
                Assert.That(Economy.ResolveEconomyGuild(player, null, true, false), Is.SameAs(player), "works with bot guilds off");
                Assert.That(Economy.ResolveEconomyGuild(player, null, false, true), Is.Null, "bot_player_guild_economy off: nothing");
                Assert.That(Economy.ResolveEconomyGuild(player, bots, true, true), Is.SameAs(player), "the player's guild comes first");
                Assert.That(Economy.ResolveEconomyGuild(player, bots, false, true), Is.Null, "never falls back to a bot guild");
                Assert.That(Economy.ResolveEconomyGuild(null, bots, true, true), Is.SameAs(bots));
                Assert.That(Economy.ResolveEconomyGuild(null, bots, true, false), Is.Null, "bot_guild_economy off");
                Assert.That(Economy.ResolveEconomyGuild(null, null, true, true), Is.Null);
            });
        }

        [Test]
        public void EconomyGuild_ACompanionNeverPaysOrEarns()
        {
            Properties.BOT_PLAYER_GUILD_ECONOMY = true;
            Properties.BOT_GUILDS_ENABLED = true;
            Properties.BOT_GUILD_ECONOMY = true;
            var companion = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(companion, new ArrayList(1));
            companion.Guild = TestGuild("owner", true, 10);
            Assert.That(companion.IsAutonomousWorldBot, Is.False);
            Assert.That(Economy.EconomyGuildOf(companion), Is.Null);
            Assert.That(Economy.ShareAfterDues(companion, 1000, out long dues, out Guild guild), Is.EqualTo(1000));
            Assert.That((dues, guild), Is.EqualTo((0L, (Guild)null)));
        }

        [Test]
        public void KeepAfterDues_PaysTheLeadersRateIntoTheBank()
        {
            Guild guild = TestGuild("player", true, 10);
            Assert.That(Economy.KeepAfterDues(guild, 1000, out long deposited), Is.EqualTo(900));
            Assert.That(deposited, Is.EqualTo(100));
            Assert.That(guild.GetGuildBank(), Is.EqualTo(100));

            Guild noDues = TestGuild("free", false, 0);
            Assert.That(Economy.KeepAfterDues(noDues, 1000, out deposited), Is.EqualTo(1000));
            Assert.That((deposited, noDues.GetGuildBank()), Is.EqualTo((0L, 0.0)));
            Assert.That(Economy.KeepAfterDues(null, 1000, out deposited), Is.EqualTo(1000));
        }

        [Test]
        public void KeepAfterDues_AFullBankLeavesTheWholeCoinWithTheBot()
        {
            Guild full = TestGuild("full", true, 10, bank: 1_000_000_000);
            Assert.That(Economy.KeepAfterDues(full, 1000, out long deposited), Is.EqualTo(1000));
            Assert.That(deposited, Is.Zero);
            Assert.That(full.GetGuildBank(), Is.EqualTo(1_000_000_000), "nothing is created or lost");
            Assert.That(Economy.DepositDues(full, 100), Is.False, "the solo pickup then gives the dues back to the bot");
            Assert.That(Economy.DepositDues(null, 100), Is.False);
            Assert.That(Economy.DepositDues(null, 0), Is.True, "no dues: nothing to give back");
            Assert.That(Economy.DepositDues(TestGuild("room", true, 10), 100), Is.True);
        }

        [Test]
        public void AwardMerit_AddsToTheGuild()
        {
            Guild guild = TestGuild("player", true, 10);
            Assert.That(Economy.AwardMerit(guild, Economy.MeritForLevels(49, 50) + Economy.MeritForRealmLevels(49, 50)), Is.True);
            Assert.That(guild.MeritPoints, Is.EqualTo(250 + 225));
            Assert.That(Economy.AwardMerit(guild, 0), Is.False);
            Assert.That(Economy.AwardMerit(null, 10), Is.False);
            Assert.That(guild.MeritPoints, Is.EqualTo(475));
        }

        [Test]
        public void MeritNotices_OneSummedLinePerGuildEveryFiveMinutes()
        {
            var notices = new Economy.MeritNotices();
            notices.Add("g1", "Seraorith", 69);
            var first = notices.TakeDue(1_000, _ => true);
            Assert.That(first, Is.EqualTo(new[] { ("g1", "Seraorith has earned 69 merit points for your guild!") }));

            notices.Add("g1", "Seraorith", 72);
            notices.Add("g1", "Brannoc", 225);
            Assert.That(notices.TakeDue(1_000 + Economy.MeritNoticeIntervalMs - 1, _ => true), Is.Empty, "too soon after the last line");
            Assert.That(notices.TakeDue(1_000 + Economy.MeritNoticeIntervalMs, _ => true),
                Is.EqualTo(new[] { ("g1", "2 guildmates have earned 297 merit points for your guild!") }));
            Assert.That(notices.TakeDue(10_000_000, _ => true), Is.Empty, "nothing new, no line");
        }

        [Test]
        public void MeritNotices_AGuildNobodyHearsIsCleared()
        {
            var notices = new Economy.MeritNotices();
            notices.Add("g1", "Seraorith", 69);
            notices.Add("g2", "Brannoc", 10);
            Assert.That(notices.TakeDue(1_000, id => id == "g2").Select(n => n.GuildId), Is.EqualTo(new[] { "g2" }));
            Assert.That(notices.TakeDue(2_000, _ => true), Is.Empty, "g1's merit was told to nobody and is not saved up");
            notices.Add("g3", "", 0);
            Assert.That(notices.TakeDue(3_000, _ => true), Is.Empty);
        }
    }
}
