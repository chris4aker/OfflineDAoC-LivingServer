using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;
using R = DOL.GS.RealmRaidPlayerInvite;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups, B3 (docs/BOT-LED-GROUPS.md, "Raid leaders"): who leads a realm raid, when the raid leader invites
    /// the player (level 50, the raid's realm, a dragon or epic dungeon raid before its battle, one invite per raid), which
    /// party seats the player and which gamebot steps out, the seat's target, and the duty lease around a raid party with
    /// the player in it. Automated only; nothing here is a played check.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RaidLeaderInvite : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type SessionType = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic)!;

        private static readonly R.Check Open = new(SettingOn: true, PlayerInvitesOff: false, PlayerLevel: 50, PlayerRealm: eRealm.Midgard,
            RaidRealm: eRealm.Midgard, Neutral: false, Started: false, AlreadyInvited: false, JustLoggedIn: false, BoxWaiting: false,
            StateBlocks: null);

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

        [Test]
        public void ALevelFiftyOfTheRealmGetsOneInviteToAFormingRaid()
        {
            var cases = new Dictionary<string, R.Check>
            {
                ["raid_invites_off"] = Open with { SettingOn = false },
                ["invites_off_by_player"] = Open with { PlayerInvitesOff = true },
                ["level"] = Open with { PlayerLevel = 49 },
                ["other_realm"] = Open with { RaidRealm = eRealm.Albion },
                ["neutral_raid"] = Open with { Neutral = true },
                ["raid_started"] = Open with { Started = true },
                ["invited_this_raid"] = Open with { AlreadyInvited = true },
                ["box_waiting"] = Open with { BoxWaiting = true },
                ["in_combat"] = Open with { StateBlocks = "in_combat" },
                ["grouped"] = Open with { StateBlocks = "grouped" },
                ["just_logged_in"] = Open with { JustLoggedIn = true },
            };
            Assert.Multiple(() =>
            {
                Assert.That(R.InviteBlocks(Open), Is.Null);
                foreach ((string reason, R.Check check) in cases)
                    Assert.That(R.InviteBlocks(check), Is.EqualTo(reason), reason);
            });
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void EveryRealmIsInvitedTheSameWay(eRealm realm) =>
            Assert.That(R.InviteBlocks(Open with { PlayerRealm = realm, RaidRealm = realm }), Is.Null);

        [Test]
        public void TheRaidLeaderIsARaidingGuildsLevelFiftyOfficerElseTheFirstPartysLeader()
        {
            static R.LeaderCandidate C(long id, int level = 50, bool raiding = true, int rank = 1, int party = 0, bool leads = false) =>
                new(id, level, raiding, rank, party, leads);
            Assert.Multiple(() =>
            {
                Assert.That(R.PickRaidLeader([C(1, rank: 2), C(2, rank: 0, party: 3), C(3, rank: 1)]), Is.EqualTo(1), "the guild leader first");
                Assert.That(R.PickRaidLeader([C(1, rank: 1, party: 2), C(2, rank: 1, party: 1)]), Is.EqualTo(1), "then the earliest party");
                Assert.That(R.PickRaidLeader([C(1, raiding: false, rank: 0, party: 1, leads: true), C(2, rank: 5, party: 0, leads: true),
                    C(3, level: 49, rank: 0)]), Is.EqualTo(1), "no officer of a raiding guild: the first party's leader");
                Assert.That(R.PickRaidLeader([C(1, raiding: false, party: 1, leads: true), C(2, raiding: false, party: 0)]), Is.EqualTo(0));
                Assert.That(R.PickRaidLeader([]), Is.EqualTo(-1));
            });
        }

        [Test]
        public void ThePlayerSitsInThePartyThatNeedsTheirClassMost()
        {
            static R.PartyOption P(int members = 8, int same = 1, bool player = false, bool forces = false, bool departed = false) =>
                new(members, same, player, forces, departed);
            Assert.Multiple(() =>
            {
                Assert.That(R.PickParty([P(same: 2), P(same: 1), P(same: 3)], out bool stepOut), Is.EqualTo(1), "the fewest of your role");
                Assert.That(stepOut, Is.True, "full: one of your role steps out");
                Assert.That(R.PickParty([P(same: 0), P(members: 7, same: 2)], out stepOut), Is.EqualTo(1),
                    "a full party with none of your role can't swap; one with room takes you");
                Assert.That(stepOut, Is.False);
                Assert.That(R.PickParty([P(same: 1), P(members: 6, same: 3)], out _), Is.EqualTo(1), "room before a swap");
                Assert.That(R.PickParty([P(departed: true, members: 6, same: 0), P(same: 2)], out _), Is.EqualTo(1), "a party still at the hub first");
                Assert.That(R.PickParty([P(player: true, members: 5), P(forces: true, members: 4)], out _), Is.EqualTo(-1),
                    "never a party with a player already, never the Epic Special Forces");
            });
        }

        [Test]
        public void TheGamebotThatStepsOutIsOfYourRoleAndLeadsNothing()
        {
            static R.Seat S(bool same = true, bool party = false, bool raid = false, bool alive = true) => new(same, party, raid, alive);
            Assert.Multiple(() =>
            {
                Assert.That(R.PickStepOut([S(party: true), S(), S(same: false), S()]), Is.EqualTo(3), "the last of your role");
                Assert.That(R.PickStepOut([S(), S(alive: false), S()]), Is.EqualTo(1), "a dead one first");
                Assert.That(R.PickStepOut([S(raid: true), S(party: true), S(same: false)]), Is.EqualTo(-1), "the leaders stay");
            });
        }

        [Test]
        public void ThePlayersSeatIsNotRefilledWhileTheyAreThere()
        {
            Assert.Multiple(() =>
            {
                Assert.That(R.TargetWithPlayer(8), Is.EqualTo(7));
                Assert.That(R.TargetWithPlayer(6), Is.EqualTo(6), "a party with room keeps its target");
                Assert.That(R.TargetWithPlayer(0), Is.EqualTo(1));
                Assert.That(R.TargetWithoutPlayer(7, 8), Is.EqualTo(8), "the seat goes back to a gamebot when the player leaves");
                Assert.That(R.TargetWithoutPlayer(6, 6), Is.EqualTo(6));
            });
        }

        [Test]
        public void TheTellAndTheBoxNameTheRaidAndTheMuster()
        {
            var slots = new Dictionary<string, string> { ["raid"] = "Gjalpinulva", ["destination"] = "Svasud Faste" };
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.Fill(R.FallbackTell, slots), Is.EqualTo("Forming for Gjalpinulva at Svasud Faste. Want a spot?"));
                Assert.That(R.BoxText("Haldor", "Gjalpinulva"), Is.EqualTo("Haldor invites you to join the Gjalpinulva raid. Do you wish to join?"));
            });
        }

        [Test]
        public void TheSettingIsOnByDefault()
        {
            FieldInfo field = typeof(DOL.GS.ServerProperties.Properties).GetField(nameof(DOL.GS.ServerProperties.Properties.BOT_RAID_INVITE_PLAYER))!;
            var attribute = field.GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>()!;
            Assert.That((attribute.Key, attribute.DefaultValue), Is.EqualTo(("bot_raid_invite_player", (object)true)));
        }

        [Test]
        public void ARaidKeepsItsBotsAndCantTakeOneFromThePlayersGroup()
        {
            Bot raider = Gamebot(50), hosted = Gamebot(50), invited = Gamebot(50);
            Player player = RealPlayer();
            raider.Group = Token();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon-mid", DutyLeaseReason.RaidParty, "Gjalpinulva", raider.Group);
            hosted.Group = Token();
            BotDutyLeases.Claim(hosted, Kind.BotLedGroupWithPlayer, "g-1", DutyLeaseReason.HostingPlayer, "Chris", hosted.Group);
            JoinPlayersGroup(invited, player);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.IsEligible(raider), Is.True, "a raid party with the player in it keeps its gamebots");
                Assert.That(BotDutyLeases.Claim(raider, Kind.BotLedGroupWithPlayer, "g-2", DutyLeaseReason.HostingPlayer).Granted, Is.False,
                    "no bot-led group takes a raider");
                Assert.That(AutonomousRealmRaid.IsEligible(hosted), Is.False, "a raid can't take a bot from a party hosting the player");
                Assert.That(AutonomousRealmRaid.IsEligible(invited), Is.False, "nor from the player's own group");
                Assert.That(BotDutyLeases.Claim(hosted, Kind.RealmRaid, "dragon-mid", DutyLeaseReason.RaidReserved).Granted, Is.False);
                Assert.That(BotDutyLeases.Claim(invited, Kind.RealmRaid, "dragon-mid", DutyLeaseReason.RaidReserved).Granted, Is.False);
            });
        }

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

        [Test]
        public void ARaidGuestDoesntBringThePartyAlongOrTakeTheLead()
        {
            Bot raider = Gamebot(50), follower = Gamebot(50);
            Player guest = RealPlayer(), host = RealPlayer();
            Group raid = GroupOf(raider, guest);
            BotLedGroups.SetHosted(raid, new BotLedGroups.HostedView(guest, "g-3") { RaidGuest = true });
            Group party = GroupOf(follower, host);
            BotLedGroups.SetHosted(party, new BotLedGroups.HostedView(host, "g-4"));
            Assert.Multiple(() =>
            {
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(raider), Is.Null, "the raid party musters at its hub");
                Assert.That(TemporaryGroupStableTravel.PlayerTheBotTravelsWith(follower), Is.SameAs(host));
                Assert.That(BotLedGroups.HostedPlayerOf(raid), Is.SameAs(guest), "quest credit and the gamebot leader still apply");
                Assert.That(BotLedGroups.TakeLead(guest), Is.EqualTo(BotLedGroups.TakeLeadMessage("raid_party")));
                Assert.That(BotLedGroups.BotLeadsMessageFor(guest), Does.Contain("/lead"));
            });
        }

        [Test]
        public void AFullRaidPartyWithoutYourRoleHasNoSeat()
        {
            Bot tank = Gamebot(50), healer = Gamebot(50);
            Player player = RealPlayer();
            Group group = GroupOf(tank, healer);
            object session = RuntimeHelpers.GetUninitializedObject(SessionType);
            SessionType.GetProperty("Group")!.SetValue(session, group);
            SessionType.GetProperty("Id")!.SetValue(session, "g-5");
            SessionType.GetField("<PveRoles>k__BackingField", Hidden)!.SetValue(session, new Dictionary<long, BotPveGroupRole>
            {
                [tank.DatabaseID] = BotPveGroupRole.Tank,
                [healer.DatabaseID] = BotPveGroupRole.Healer,
            });
            var sessions = (System.Collections.IDictionary)typeof(AutonomousBotGroupCoordinator).GetField("Sessions", HiddenStatic)!.GetValue(null)!;
            sessions[group] = session;
            try
            {
                var raid = new AutonomousRealmRaid.PlayerRaid("dragon-alb", 1, "Golestandt", eRealm.Albion, false, false, "Caer Ulfwych",
                    [group], [false], [false], tank);
                MethodInfo options = typeof(AutonomousBotGroupCoordinator).GetMethod("SeatOptions", HiddenStatic)!;
                object[] args = [raid, BotPveGroupRole.Healer, null];
                var seats = (R.PartyOption[])options.Invoke(null, args)!;
                Assert.Multiple(() =>
                {
                    Assert.That(seats.Single().Members, Is.EqualTo(2));
                    Assert.That(seats.Single().SameRole, Is.EqualTo(1), "one healer already");
                    Assert.That(seats.Single().HasPlayer, Is.False);
                    Assert.That(R.PickParty(seats, out bool stepOut), Is.EqualTo(0));
                    Assert.That(stepOut, Is.False, "room: nobody steps out");
                });
                group.GetMembersInTheGroup();
                var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden)!.GetValue(group)!;
                list.Add(player);
                player.Group = group;
                seats = (R.PartyOption[])options.Invoke(null, [raid, BotPveGroupRole.Healer, null])!;
                Assert.That(seats.Single().HasPlayer, Is.True, "a party with a player in it already");
            }
            finally
            {
                sessions.Remove(group);
            }
        }
    }
}
