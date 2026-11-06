using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, B1) on the coordinator's own objects: the hosted view only while the player
    /// is in the group, the party's pace and cohesion waiting for the player, the roster and the camp level counting the
    /// player, a session whose only player is the one it hosts, the lease the party's gamebots hold (and what it refuses),
    /// and the player leaving while the party carries on. Automated only; nothing here is a played check.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotLedGroupHosting : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type SessionType = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic)!;

        private sealed class Leveled : GamePlayer
        {
            private Leveled() : base(null, null) { }
            public byte TestLevel;
            public override byte Level { get => TestLevel; set { } }
            public override string Name { get => "Chris"; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        [SetUp]
        public void Fresh()
        {
            BotDutyLeases.Clear();
            BotLedGroups.Clear();
        }

        [TearDown]
        public void Forget()
        {
            BotDutyLeases.Clear();
            BotLedGroups.Clear();
        }

        /// <summary>A real group object with these members (no packets are sent: members are put in directly).</summary>
        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden)!.GetValue(group)!;
            foreach (GameLiving member in members)
            {
                list.Add(member);
                member.Group = group;
            }
            return group;
        }

        private static object Session(Group group, string id, GameBot leader)
        {
            object session = RuntimeHelpers.GetUninitializedObject(SessionType);
            SessionType.GetProperty("Group")!.SetValue(session, group);
            SessionType.GetProperty("Id")!.SetValue(session, id);
            SessionType.GetProperty("Leader")!.SetValue(session, leader);
            SessionType.GetProperty("Phase")!.SetValue(session, "Grinding");
            SessionType.GetProperty("ObjectiveKind")!.SetValue(session, eAutonomousObjectiveKind.GroupPve);
            SessionType.GetField("<PveRoles>k__BackingField", Hidden)!.SetValue(session, new Dictionary<long, BotPveGroupRole>());
            return session;
        }

        private static void Set(object session, string property, object value) => SessionType.GetProperty(property)!.SetValue(session, value);

        private static Dictionary<long, BotPveGroupRole> Roles(object session) =>
            (Dictionary<long, BotPveGroupRole>)SessionType.GetProperty("PveRoles")!.GetValue(session)!;

        private static T Call<T>(string method, params object[] arguments) =>
            (T)typeof(AutonomousBotGroupCoordinator).GetMethod(method, HiddenStatic)!.Invoke(null, arguments)!;

        private static AutonomousBotGroupCoordinator.Directive Directive(GameBot leader) =>
            new("g-1", "Traveling", "", "", eAutonomousObjectiveKind.GroupPve, leader, Vector3.Zero, null, 2, 20, 0, 6, true);

        [Test]
        public void TheHostedViewHoldsOnlyWhileThePlayerIsInTheGroup()
        {
            Bot leader = Gamebot(20);
            Player player = RealPlayer();
            Group group = GroupOf(leader, player);
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, "g-1"));
            Assert.That(BotLedGroups.HostedPlayerOf(group), Is.SameAs(player));
            Assert.That(AutonomousBotGroupCoordinator.IsHostedParty(leader), Is.True);
            player.Group = null;
            Assert.That(BotLedGroups.IsHostedGroup(group), Is.False, "a player who left is not hosted");
            Assert.That(BotLedGroups.HostedPlayerOf(null), Is.Null);
        }

        [Test]
        public void TheLeaderStopsForAHostedPlayerFarBehindAndGoesOnOnceClose()
        {
            Bot leader = Gamebot(20), follower = Gamebot(20);
            follower.TestX = 200;
            Player player = RealPlayer();
            Group group = GroupOf(leader, follower, player);
            var view = new BotLedGroups.HostedView(player, "g-1");
            BotLedGroups.SetHosted(group, view);
            AutonomousBotGroupCoordinator.Directive directive = Directive(leader);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGroupCoordinator.FarthestMemberDistance(directive), Is.LessThan(AutonomousGroupPace.TrailRadius));
                Assert.That(AutonomousBotGroupCoordinator.IsCohesive(directive), Is.True);
            });
            view.HoldsPace = true;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupPace.Decide(AutonomousBotGroupCoordinator.FarthestMemberDistance(directive), false),
                    Is.EqualTo(AutonomousGroupPace.Decision.Wait));
                Assert.That(AutonomousBotGroupCoordinator.IsCohesive(directive), Is.False, "the region crossing waits too");
            });
        }

        [Test]
        public void AHostedHealerFillsTheRostersHealerSlot()
        {
            Bot tank = Gamebot(20), hitter = Gamebot(20);
            Leveled player = (Leveled)RuntimeHelpers.GetUninitializedObject(typeof(Leveled));
            Group group = GroupOf(tank, hitter, player);
            object session = Session(group, "g-2", tank);
            Roles(session)[tank.DatabaseID] = BotPveGroupRole.Tank;
            Roles(session)[hitter.DatabaseID] = BotPveGroupRole.Attacker;
            GameBot[] bots = [tank, hitter];
            Assert.That(Call<bool>("HasViablePveComposition", session, bots), Is.False, "two bots, no healer");
            Set(session, "HostedPlayer", player);
            Set(session, "PlayerRole", BotPveGroupRole.Healer);
            Assert.That(Call<bool>("HasViablePveComposition", session, bots), Is.True, "the player is the healer");
            Set(session, "PlayerRole", BotPveGroupRole.Attacker);
            Assert.That(Call<bool>("HasViablePveComposition", session, bots), Is.False);
            player.Group = null;
            Set(session, "PlayerRole", BotPveGroupRole.Healer);
            Assert.That(Call<bool>("HasViablePveComposition", session, bots), Is.False, "a player who left counts for nothing");
        }

        [Test]
        public void TheDirectivesCampLevelCountsThePlayerAndStaysSixAbove()
        {
            Bot first = Gamebot(24), second = Gamebot(24);
            Leveled player = (Leveled)RuntimeHelpers.GetUninitializedObject(typeof(Leveled));
            player.TestLevel = 20;
            Group group = GroupOf(first, second, player);
            object session = Session(group, "g-3", first);
            Set(session, "PreferredLevelBonus", 10);
            GameBot[] bots = [first, second];
            (int level, int bonus) = Call<(int, int)>("HostedLevels", session, bots);
            Assert.That((level, bonus), Is.EqualTo((24, 10)), "a bot-only party keeps its own level and roll");
            Set(session, "HostedPlayer", player);
            (level, bonus) = Call<(int, int)>("HostedLevels", session, bots);
            Assert.Multiple(() =>
            {
                Assert.That(level + bonus, Is.LessThanOrEqualTo(26));
                Assert.That(level, Is.EqualTo(23), "the group's level with the player (23), at most 3 below the cap");
                Assert.That(bonus, Is.EqualTo(3));
            });
        }

        [Test]
        public void ASessionKeepsRunningWithOnlyTheHostedPlayerInIt()
        {
            Bot leader = Gamebot(20);
            Player host = RealPlayer(), stranger = RealPlayer();
            Group group = GroupOf(leader, host);
            object session = Session(group, "g-4", leader);
            Assert.That(Call<bool>("OnlyHostedPlayer", group, session), Is.False, "not hosted yet: the player leads");
            Set(session, "HostedPlayer", host);
            Assert.That(Call<bool>("OnlyHostedPlayer", group, session), Is.True);
            Group other = GroupOf(leader, stranger);
            Assert.That(Call<bool>("OnlyHostedPlayer", other, session), Is.False, "another player in it");
        }

        [Test]
        public void HostingTakesTheBotGroupLeaseAndNothingAutomaticTakesItBack()
        {
            Group group = Token();
            Bot member = Gamebot(20);
            member.Group = group;
            BotDutyLeases.Claim(member, Kind.BotGroupSession, "g-5", DutyLeaseReason.GroupSession, group: group);
            Assert.That(BotDutyLeases.WouldGrant(member, Kind.BotLedGroupWithPlayer, "g-5"), Is.True);
            BotDutyLeases.Claim(member, Kind.BotLedGroupWithPlayer, "g-5", DutyLeaseReason.HostingPlayer, "Chris", group);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.KindOf(member), Is.EqualTo(Kind.BotLedGroupWithPlayer));
                Assert.That(BotDutyLeases.IsSpokenForByPlayer(member), Is.True);
                Assert.That(BotDutyLeaseRules.ExemptsFromWatchdog(BotDutyLeases.KindOf(member)), Is.True);
                foreach (Kind automatic in new[] { Kind.RealmRaid, Kind.RvrEvent, Kind.BattlegroundWave, Kind.BotGroupSession })
                {
                    BotDutyLeases.ClaimResult claim = BotDutyLeases.Claim(member, automatic, "other", "test");
                    Assert.That(claim.Granted, Is.False, automatic.ToString());
                    Assert.That(claim.Reason, Is.EqualTo(DutyLeaseReason.HostingPlayer), automatic.ToString());
                }
                Assert.That(BotDutyLeases.KindOf(member), Is.EqualTo(Kind.BotLedGroupWithPlayer));
            });
            Assert.That(BotDutyLeases.Claim(member, Kind.PlayerGroup, "Chris", DutyLeaseReason.PlayerGroup).Granted, Is.True,
                "only a player's own group (a later /lead) takes it");
        }

        [Test]
        public void APartyOnARaidOrRvrDutyCannotHostThePlayer()
        {
            Bot raider = Gamebot(50), marcher = Gamebot(50);
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            BotDutyLeases.Claim(marcher, Kind.RvrEvent, "siege-1", DutyLeaseReason.RvrForce);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.WouldGrant(raider, Kind.BotLedGroupWithPlayer, "g-6"), Is.False);
                Assert.That(BotDutyLeases.WouldGrant(marcher, Kind.BotLedGroupWithPlayer, "g-6"), Is.False);
            });
        }

        [Test]
        public void OnlyAutonomousGamebotsAreLeasedNeverACompanion()
        {
            Bot companion = Companion();
            BotDutyLeases.ClaimResult claim = BotDutyLeases.Claim(companion, Kind.BotLedGroupWithPlayer, "g-7", DutyLeaseReason.HostingPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(claim.Granted, Is.False);
                Assert.That(claim.Reason, Is.EqualTo(DutyLeaseReason.NotAGamebot));
            });
        }

        [Test]
        public void TheBotsGoAlongWithTheHostedPlayerAsWithAnInvitingOne()
        {
            Bot invited = Gamebot(20), hosted = Gamebot(20), alone = Gamebot(20);
            Player leader = RealPlayer(), guest = RealPlayer();
            JoinPlayersGroup(invited, leader);
            Group party = GroupOf(hosted, guest);
            BotLedGroups.SetHosted(party, new BotLedGroups.HostedView(guest, "g-9"));
            alone.Group = Token();
            Assert.Multiple(() =>
            {
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(invited), Is.SameAs(leader));
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(hosted), Is.SameAs(guest));
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(alone), Is.Null, "a bot-only party");
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(null), Is.Null);
            });
        }

        [Test]
        public void WhenThePlayerLeavesThePartyGoesOnAndItsLeasesComeBack()
        {
            Bot leader = Gamebot(20), member = Gamebot(20);
            Leveled player = (Leveled)RuntimeHelpers.GetUninitializedObject(typeof(Leveled));
            Group group = GroupOf(leader, member, player);
            object session = Session(group, "g-8", leader);
            Set(session, "HostedPlayer", player);
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, "g-8"));
            foreach (Bot bot in new[] { leader, member })
                BotDutyLeases.Claim(bot, Kind.BotLedGroupWithPlayer, "g-8", DutyLeaseReason.HostingPlayer, "Chris", group);
            typeof(AutonomousBotGroupCoordinator).GetMethod("EndHosting", HiddenStatic)!.Invoke(null, [session, player, "left_group"]);
            Assert.Multiple(() =>
            {
                Assert.That(SessionType.GetProperty("HostedPlayer")!.GetValue(session), Is.Null);
                Assert.That(BotLedGroups.IsHostedGroup(group), Is.False);
                Assert.That(BotDutyLeases.Current(leader), Is.Null);
                Assert.That(BotDutyLeases.Current(member), Is.Null);
                Assert.That(leader.Group, Is.SameAs(group), "the party is not disbanded");
                Assert.That(BotDutyLeases.WouldGrant(member, Kind.BotGroupSession, "g-8"), Is.True, "free for its bot-group lease again");
            });
        }
    }
}
