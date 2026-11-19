using System;
using System.Reflection;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// The player-group lease (docs/DUTY-LEASE.md, migration 1): "don't take this bot" now reads the lease, and the lease
    /// is held exactly while the gamebot follows a player (GameBot.PlayerGroupLeader), so every decision that used
    /// IsPlayerLedGroup for it decides as before (UT_DutyLeaseCurrentRules). A bot-led group hosting a player (phase B)
    /// is the same kind of claim, so it is spoken for by the same checks.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeasePlayerGroup : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        private static void AssertLeaseMatchesFollowing(Bot bot, string when)
        {
            Assert.That(BotDutyLeases.IsSpokenForByPlayer(bot), Is.EqualTo(bot.IsPlayerLedGroup), when);
            Assert.That(BotDutyLeases.Holds(bot, Kind.PlayerGroup), Is.EqualTo(bot.IsPlayerLedGroup), when);
        }

        [Test]
        public void TheLeaseIsHeldExactlyWhileTheGamebotFollowsAPlayer()
        {
            Bot bot = Gamebot();
            AssertLeaseMatchesFollowing(bot, "before the invite");
            Player chris = RealPlayer(), alys = RealPlayer();
            JoinPlayersGroup(bot, chris);
            AssertLeaseMatchesFollowing(bot, "in the player's group");
            Assert.That(bot.EnterPlayerLedGroup(alys), Is.True, "the leader left and the other player keeps the bot");
            AssertLeaseMatchesFollowing(bot, "after the handover");
            Assert.That(bot.EnterPlayerLedGroup(RealPlayer(eRealm.Midgard)), Is.False, "another realm's player never leads it");
            AssertLeaseMatchesFollowing(bot, "after a refused handover");
            bot.LeavePlayerLedGroup();
            AssertLeaseMatchesFollowing(bot, "after leaving");
            bot.LeavePlayerLedGroup();
            AssertLeaseMatchesFollowing(bot, "leaving twice");
        }

        [Test]
        public void ACompanionFollowsItsPlayerWithoutALease()
        {
            Bot companion = Companion();
            JoinPlayersGroup(companion, RealPlayer());
            Assert.That(companion.IsPlayerLedGroup, Is.True);
            Assert.That(BotDutyLeases.Current(companion), Is.Null, "/spawn companions keep their own owner rules");
        }

        [Test]
        public void APlayersGroupTakesTheBotFromAnyAutomaticClaimAndNothingTakesItBack()
        {
            Bot bot = Gamebot();
            BotDutyLeases.Claim(bot, Kind.BotGroupSession, "g-4", DutyLeaseReason.GroupSession);
            JoinPlayersGroup(bot, RealPlayer());
            Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.PlayerGroup));
            foreach (Kind kind in new[] { Kind.RealmRaid, Kind.RvrEvent, Kind.BattlegroundWave, Kind.BotGroupSession, Kind.BotLedGroupWithPlayer })
                Assert.That(BotDutyLeases.Claim(bot, kind, "x", "x").Granted, Is.False, kind.ToString());
            Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.PlayerGroup));
        }

        [Test]
        public void LoggingOutEndsTheLeaseAndTheBotLogsBackInFree()
        {
            Bot bot = Gamebot();
            JoinPlayersGroup(bot, RealPlayer());
            AutonomousBotRegistry.Unregister(bot);
            Assert.That(BotDutyLeases.Current(bot), Is.Null);
        }

        [Test]
        public void ABotLedGroupHostingAPlayerIsSpokenForByTheSameChecks()
        {
            Bot host = Gamebot(objective: "Battleground");
            typeof(GameBot).GetField("m_characterClass", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new ClassArmsman());
            Assert.That(BotDutyLeases.Claim(host, Kind.BotLedGroupWithPlayer, "g-9", DutyLeaseReason.HostingPlayer).Granted, Is.True);
            MethodInfo isMember = typeof(AutonomousBattlegroundKeepRally).GetMethod("IsMember", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Multiple(() =>
            {
                Assert.That(host.IsPlayerLedGroup, Is.False, "the bot leads; it follows nobody");
                Assert.That(BotDutyLeases.IsSpokenForByPlayer(host), Is.True);
                Assert.That(AutonomousRealmRaid.IsEligible(host), Is.False, "no raid takes it");
                Assert.That(AutonomousSiegeJobs.Eligible(host), Is.False, "no siege crew takes it");
                Assert.That(AutonomousStuckWatchdog.HasHumanCompany(host), Is.True, "the watchdog leaves it to the player");
                Assert.That((bool)isMember.Invoke(null, new object[] { host, eRealm.Albion, (ushort)1 })!, Is.False, "no rally counts it");
                Assert.That(AutonomousBotEconomy.TryClearBlockedBackpackAtMerchant(host, true, out _), Is.False);
            });
            BotDutyLeases.Release(host, Kind.BotLedGroupWithPlayer);
            Assert.That(AutonomousRealmRaid.IsEligible(host), Is.True);
        }
    }
}
