using System;
using System.Reflection;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// The rules that decide whether a gamebot is busy or may be taken, pinned as they stand before they move onto the
    /// duty lease record (docs/DUTY-LEASE.md, Living Server 1.2 phase A). They are written against the public entry
    /// points the game calls, so they stay unchanged and green through the migration: the lease must not change who
    /// a raid, a siege, a battleground rally or the watchdog may take.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DutyLeaseCurrentRules : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void TheRaidRecruitmentRuleTakesOnlyFreeLevelFiftyGamebots()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.Eligible(50, true, false, false), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.Eligible(49, true, false, false), Is.False, "level 50 only");
                Assert.That(RealmRaidRecruitmentPolicy.Eligible(50, false, false, false), Is.False, "autonomous gamebots only");
                Assert.That(RealmRaidRecruitmentPolicy.Eligible(50, true, true, false), Is.False, "never a /spawn companion");
                Assert.That(RealmRaidRecruitmentPolicy.Eligible(50, true, false, true), Is.False, "never a bot in a player's group");
            });
        }

        [Test]
        public void ARaidNeverTakesABotFromAPlayersGroupAndMayOnceItLeaves()
        {
            Bot bot = Gamebot();
            Player player = RealPlayer();
            Assert.That(AutonomousRealmRaid.IsEligible(bot), Is.True, "a free level-50 gamebot");
            JoinPlayersGroup(bot, player);
            Assert.That(AutonomousRealmRaid.IsEligible(bot), Is.False, "in the player's group");
            bot.LeavePlayerLedGroup();
            Assert.That(AutonomousRealmRaid.IsEligible(bot), Is.True, "free again once it leaves");
            Assert.That(AutonomousRealmRaid.IsEligible(Gamebot(level: 49)), Is.False);
            Assert.That(AutonomousRealmRaid.IsEligible(Companion()), Is.False);
        }

        [Test]
        public void AReservedRaiderIsReservedForItsOwnRaidOnly()
        {
            Bot raider = SpecialForce();
            Bot ordinary = Gamebot();
            string raid = "duty-lease-test-raid-" + raider.DatabaseID;
            Assert.That(AutonomousRealmRaid.IsReserved(raider), Is.False);
            AutonomousRealmRaid.ReserveSpecialForce(raider, raid);
            AutonomousRealmRaid.ReserveSpecialForce(ordinary, raid);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.IsReserved(raider), Is.True);
                Assert.That(AutonomousRealmRaid.IsReservedFor(raider, raid), Is.True);
                Assert.That(AutonomousRealmRaid.IsReservedFor(raider, raid + "-other"), Is.False);
                Assert.That(AutonomousRealmRaid.IsReserved(ordinary), Is.False, "only the Special Forces are reserved this way");
                Assert.That(AutonomousRealmRaid.IsReserved(null), Is.False);
            });
        }

        [Test]
        public void APlayersInviteIsRefusedByTheBotsDutyInAFixedOrder()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotGroupInvite.DutyOf(false, true, true, true, true, true, true), Is.EqualTo(BotGroupInvite.Duty.Dead));
                Assert.That(BotGroupInvite.DutyOf(true, true, true, true, true, true, true), Is.EqualTo(BotGroupInvite.Duty.Siege));
                Assert.That(BotGroupInvite.DutyOf(true, false, true, true, true, true, true), Is.EqualTo(BotGroupInvite.Duty.Relic));
                Assert.That(BotGroupInvite.DutyOf(true, false, false, true, true, true, true), Is.EqualTo(BotGroupInvite.Duty.RvrEvent));
                Assert.That(BotGroupInvite.DutyOf(true, false, false, false, true, true, true), Is.EqualTo(BotGroupInvite.Duty.RealmRaid));
                Assert.That(BotGroupInvite.DutyOf(true, false, false, false, false, true, true), Is.EqualTo(BotGroupInvite.Duty.StableRoute));
                Assert.That(BotGroupInvite.DutyOf(true, false, false, false, false, false, true), Is.EqualTo(BotGroupInvite.Duty.Helper));
                Assert.That(BotGroupInvite.DutyOf(true, false, false, false, false, false, false), Is.EqualTo(BotGroupInvite.Duty.None));
                Assert.That(BotGroupInvite.BusyMessage("Brannoc", BotGroupInvite.Duty.RealmRaid),
                    Is.EqualTo("Brannoc is committed to a realm raid right now."));
                Assert.That(BotGroupInvite.BusyMessage("Brannoc", BotGroupInvite.Duty.None), Is.Null);
            });
        }

        [Test]
        public void ABotsLiveDutyCountsARaidReservationButNotAPlayersGroup()
        {
            Bot free = Gamebot();
            Bot reserved = SpecialForce();
            AutonomousRealmRaid.ReserveSpecialForce(reserved, "duty-lease-test-invite-" + reserved.DatabaseID);
            Bot followed = Gamebot();
            JoinPlayersGroup(followed, RealPlayer());
            Bot dead = Gamebot();
            dead.Alive = false;
            Assert.Multiple(() =>
            {
                Assert.That(BotGroupInvite.DutyOf(free), Is.EqualTo(BotGroupInvite.Duty.None));
                Assert.That(BotGroupInvite.DutyOf(reserved), Is.EqualTo(BotGroupInvite.Duty.RealmRaid));
                // A bot already in a player's group is refused later, by TryInvite ("already following another player-led group").
                Assert.That(BotGroupInvite.DutyOf(followed), Is.EqualTo(BotGroupInvite.Duty.None));
                Assert.That(BotGroupInvite.DutyOf(dead), Is.EqualTo(BotGroupInvite.Duty.Dead));
                Assert.That(BotGroupInvite.DutyOf(Companion()), Is.EqualTo(BotGroupInvite.Duty.None), "companions are never on duty here");
            });
        }

        [Test]
        public void SiegeCrewsAreNeverTakenFromAPlayersGroup()
        {
            Bot armsman = Gamebot();
            typeof(GameBot).GetField("m_characterClass", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(armsman, new ClassArmsman());
            Assert.That(AutonomousSiegeJobs.Eligible(armsman), Is.True);
            JoinPlayersGroup(armsman, RealPlayer());
            Assert.That(AutonomousSiegeJobs.Eligible(armsman), Is.False);
            armsman.LeavePlayerLedGroup();
            Assert.That(AutonomousSiegeJobs.Eligible(armsman), Is.True);
        }

        [Test]
        public void TheWatchdogNeverRelocatesABotInAPlayersGroup()
        {
            Bot bot = Gamebot();
            DateTime now = DateTime.UtcNow;
            DateTime longAgo = now - TimeSpan.FromHours(2);
            Assert.That(AutonomousStuckWatchdog.HasHumanCompany(bot), Is.False);
            JoinPlayersGroup(bot, RealPlayer());
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousStuckWatchdog.HasHumanCompany(bot), Is.True);
                Assert.That(AutonomousStuckWatchdog.ShouldRecoverMovement(longAgo, now, true, true, false), Is.False);
                Assert.That(AutonomousStuckWatchdog.ShouldRecoverGoal(longAgo, now, true, true, false), Is.False);
                Assert.That(AutonomousStuckWatchdog.ShouldRecoverMovement(longAgo, now, true, false, false), Is.True);
                Assert.That(AutonomousStuckWatchdog.ShouldRecoverGoal(longAgo, now, true, false, false), Is.True);
            });
            bot.LeavePlayerLedGroup();
            bot.Group = null;
            Assert.That(AutonomousStuckWatchdog.HasHumanCompany(bot), Is.False);
        }

        [Test]
        public void ABattlegroundRallyCountsOnlyItsOwnTourersAndNeverAPlayersGroup()
        {
            MethodInfo isMember = typeof(AutonomousBattlegroundKeepRally).GetMethod("IsMember", Hidden)!;
            bool Member(Bot bot) => (bool)isMember.Invoke(null, new object[] { bot, eRealm.Albion, (ushort)1 })!;
            Bot tourer = Gamebot(level: 22, objective: "Battleground");
            Bot grinder = Gamebot(level: 22);
            Bot enemy = Gamebot(level: 22, realm: eRealm.Midgard, objective: "Battleground");
            Bot dead = Gamebot(level: 22, objective: "Battleground");
            dead.Alive = false;
            Assert.Multiple(() =>
            {
                Assert.That(Member(tourer), Is.True);
                Assert.That(Member(grinder), Is.False, "not on a battleground tour");
                Assert.That(Member(enemy), Is.False);
                Assert.That(Member(dead), Is.False);
            });
            JoinPlayersGroup(tourer, RealPlayer());
            Assert.That(Member(tourer), Is.False, "a player's group follows the player");
            tourer.LeavePlayerLedGroup();
            Assert.That(Member(tourer), Is.True);
        }

        [Test]
        public void TheMerchantFallbackNeverEmptiesABagInAPlayersGroup()
        {
            Bot bot = Gamebot();
            JoinPlayersGroup(bot, RealPlayer());
            Assert.That(AutonomousBotEconomy.TryClearBlockedBackpackAtMerchant(bot, true, out int removed), Is.False);
            Assert.That(removed, Is.EqualTo(0));
        }

        [Test]
        public void TheGroupCoordinatorLeavesAPlayersGroupToThePlayer()
        {
            Bot bot = Gamebot();
            JoinPlayersGroup(bot, RealPlayer());
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGroupCoordinator.ProtectsFromIndividualWatchdog(bot), Is.False);
                Assert.That(AutonomousBotGroupCoordinator.CanInitiateNewPull(bot), Is.True, "the player decides the pulls");
                Assert.That(AutonomousBotGroupCoordinator.IsRecovering(bot), Is.False);
            });
        }
    }
}
