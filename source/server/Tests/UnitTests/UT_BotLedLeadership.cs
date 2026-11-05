using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups, B2 (docs/BOT-LED-GROUPS.md, "Taking the lead"): /lead and its refusals, /lead bot and /makeleader
    /// (who takes the lead, and when the bots can't), the join mode, the "fewer than two bots" box, /invites off, the
    /// duty lease's handover between the player's two kinds, and the party's pull gate counting a hosted healer.
    /// Automated only; nothing here is a played check, and the handover's packets and chat are not exercised.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotLedLeadership : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type SessionType = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic)!;

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

        #region The pure rules

        [TestCase("lead", BotLedGroups.JoinMode.Lead)]
        [TestCase(" LEAD ", BotLedGroups.JoinMode.Lead)]
        [TestCase("follow", BotLedGroups.JoinMode.Follow)]
        [TestCase("", BotLedGroups.JoinMode.Follow)]
        [TestCase(null, BotLedGroups.JoinMode.Follow)]
        [TestCase("leader", BotLedGroups.JoinMode.Follow)]
        public void TheJoinModeFollowsUnlessItSaysLead(string value, BotLedGroups.JoinMode mode) =>
            Assert.That(BotLedGroups.ParseJoinMode(value), Is.EqualTo(mode));

        [Test]
        public void TheJoinModeSettingDefaultsToFollow()
        {
            FieldInfo field = typeof(DOL.GS.ServerProperties.Properties).GetField(nameof(DOL.GS.ServerProperties.Properties.BOT_LED_GROUP_JOIN_MODE))!;
            var attribute = field.GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>()!;
            Assert.Multiple(() =>
            {
                Assert.That(attribute.Key, Is.EqualTo("bot_led_group_join_mode"));
                Assert.That(attribute.DefaultValue, Is.EqualTo("follow"));
            });
        }

        [Test]
        public void LeadTakesOverOnlyAGroupAGamebotLeadsThatHostsYou()
        {
            var free = new BotLedGroups.TakeLeadState(Grouped: true, LeadsAlready: false, RaidParty: false, HostedHere: true);
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.TakeLeadBlocks(free), Is.Null);
                Assert.That(BotLedGroups.TakeLeadBlocks(free with { Grouped = false }), Is.EqualTo("not_grouped"));
                Assert.That(BotLedGroups.TakeLeadBlocks(free with { LeadsAlready = true }), Is.EqualTo("already_leading"));
                Assert.That(BotLedGroups.TakeLeadBlocks(free with { RaidParty = true }), Is.EqualTo("raid_party"), "the raid leader runs a raid party");
                Assert.That(BotLedGroups.TakeLeadBlocks(free with { HostedHere = false }), Is.EqualTo("not_bot_led"));
                foreach (string reason in new[] { "not_grouped", "already_leading", "raid_party", "not_bot_led", "party_gone" })
                    Assert.That(BotLedGroups.TakeLeadMessage(reason), Is.Not.Empty.And.Not.Contains("{"), reason);
            });
        }

        [Test]
        public void HandingTheLeadBackNeedsTwoGamebotsACoreRosterAndNoCompanions()
        {
            var ready = new BotLedGroups.HandBackState(Grouped: true, Leads: true, HasCompanions: false, Gamebots: 2, OtherPlayers: 0,
                InCombat: false, PlaceReachable: true, ViableRoster: true);
            var cases = new Dictionary<string, BotLedGroups.HandBackState>
            {
                ["not_grouped"] = ready with { Grouped = false },
                ["not_leader"] = ready with { Leads = false },
                ["companions_out"] = ready with { HasCompanions = true },
                ["other_players"] = ready with { OtherPlayers = 1 },
                ["too_few_bots"] = ready with { Gamebots = 1 },
                ["in_combat"] = ready with { InCombat = true },
                ["unreachable"] = ready with { PlaceReachable = false },
                ["no_core_roster"] = ready with { ViableRoster = false },
            };
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.HandBackBlocks(ready), Is.Null);
                foreach ((string reason, BotLedGroups.HandBackState state) in cases)
                {
                    Assert.That(BotLedGroups.HandBackBlocks(state), Is.EqualTo(reason), reason);
                    Assert.That(BotLedGroups.HandBackMessage(reason, "Brannoc"), Is.Not.Empty.And.Not.Contains("{"), reason);
                }
                Assert.That(BotLedGroups.HandBackBlocks(ready with { HasCompanions = true, Gamebots = 0 }), Is.EqualTo("companions_out"),
                    "companions are named first: they are why it can't happen");
            });
        }

        [Test]
        public void TheNamedGamebotLeadsElseTheHighestLevelLivingOne()
        {
            static BotLedGroups.LeaderCandidate C(bool bot = true, bool alive = true, int level = 20, bool named = false) => new(bot, alive, level, named);
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.PickNewLeader([C(level: 20), C(level: 22), C(level: 22)], out string why), Is.EqualTo(1));
                Assert.That(why, Is.Null);
                Assert.That(BotLedGroups.PickNewLeader([C(level: 30, alive: false), C(level: 20)], out _), Is.EqualTo(1), "the dead don't lead");
                Assert.That(BotLedGroups.PickNewLeader([C(level: 30, bot: false), C(level: 20)], out _), Is.EqualTo(1), "a companion never leads");
                Assert.That(BotLedGroups.PickNewLeader([C(level: 30), C(level: 20, named: true)], out _), Is.EqualTo(1), "the one named or targeted");
                Assert.That(BotLedGroups.PickNewLeader([C(level: 30), C(bot: false, named: true)], out why), Is.EqualTo(-1));
                Assert.That(why, Is.EqualTo("not_a_gamebot"));
                Assert.That(BotLedGroups.PickNewLeader([C(level: 30), C(alive: false, named: true)], out why), Is.EqualTo(-1));
                Assert.That(why, Is.EqualTo("dead"));
                Assert.That(BotLedGroups.PickNewLeader([C(bot: false), C(alive: false)], out why), Is.EqualTo(-1));
                Assert.That(why, Is.EqualTo("no_leader"));
            });
        }

        [TestCase(true, false, 1, true)]
        [TestCase(true, false, 2, false)]
        [TestCase(true, false, 0, false)]
        [TestCase(true, true, 1, false)]
        [TestCase(false, false, 1, false)]
        public void TheBoxComesOnlyWithOneGamebotLeftOutsideARaid(bool hosted, bool raid, int bots, bool offered) =>
            Assert.That(BotLedGroups.OffersLeadOnEnd(hosted, raid, bots), Is.EqualTo(offered));

        [TestCase("on", true)]
        [TestCase(" OFF", false)]
        [TestCase("maybe", null)]
        [TestCase(null, null)]
        public void InvitesTakeOnOrOff(string value, bool? on) => Assert.That(BotLedGroups.ParseToggle(value), Is.EqualTo(on));

        [Test]
        public void InvitesOffStopsUnaskedInvitesButNotAsking()
        {
            const long Minute = 60_000;
            var memory = new BotLedGroups.InviteMemory(0) { PlayerInvitesOff = true };
            long later = 2 * BotLedGroups.UnaskedGapMs;
            Assert.That(BotLedGroups.CadenceBlocks(memory, later, true, long.MaxValue), Is.EqualTo("invites_off_by_player"));
            memory.Asked(later);
            Assert.That(BotLedGroups.CadenceBlocks(memory, later + 1_000, true, long.MaxValue), Is.Null, "/lfg still gets one");
            memory.PlayerInvitesOff = false;
            Assert.That(BotLedGroups.CadenceBlocks(memory, later + 10 * Minute, true, long.MaxValue), Is.Null);
            Assert.That(BotLedGroups.CadenceBlocks(memory, later + 10 * Minute, false, long.MaxValue), Is.EqualTo("invites_off"),
                "the server setting is read first");
        }

        [Test]
        public void ABotLeaderAnswersPullInviteAndMakeleaderWithLead()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.BotLeadsMessage("Brannoc"), Is.EqualTo("Brannoc is leading this group. /lead to take over."));
                Assert.That(BotLedGroups.BotLeadsMessage(null), Does.StartWith("A gamebot is leading"));
                Assert.That(BotLedGroups.BotLeadsMessageFor(null), Is.Null);
                Assert.That(BotLedGroups.BotLeadsMessageFor(RealPlayer()), Is.Null, "a player in no group");
            });
        }

        [Test]
        public void TheCommandsAreThere()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(LeadCommandHandler).GetCustomAttribute<CmdAttribute>()!.Cmd, Is.EqualTo("&lead"));
                Assert.That(typeof(InvitesCommandHandler).GetCustomAttribute<CmdAttribute>()!.Cmd, Is.EqualTo("&invites"));
            });
        }

        #endregion

        #region The duty lease's handover

        [Test]
        public void TheLeaseMovesOnlyBetweenThePlayersTwoKinds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeaseRules.MayHandOver(Kind.PlayerGroup, Kind.BotLedGroupWithPlayer), Is.True);
                Assert.That(BotDutyLeaseRules.MayHandOver(Kind.BotLedGroupWithPlayer, Kind.PlayerGroup), Is.True);
                Assert.That(BotDutyLeaseRules.MayHandOver(Kind.PlayerGroup, Kind.PlayerGroup), Is.False);
                foreach (Kind other in new[] { Kind.RealmRaid, Kind.RvrEvent, Kind.BattlegroundWave, Kind.BotGroupSession, Kind.None })
                {
                    Assert.That(BotDutyLeaseRules.MayHandOver(other, Kind.BotLedGroupWithPlayer), Is.False, other.ToString());
                    Assert.That(BotDutyLeaseRules.MayHandOver(Kind.PlayerGroup, other), Is.False, other.ToString());
                }
            });
        }

        [Test]
        public void LeadBotMovesTheBotsToTheBotLedLeaseInOneStepAndNothingAutomaticTakesThem()
        {
            Bot bot = Gamebot(20);
            Player player = RealPlayer();
            JoinPlayersGroup(bot, player);
            Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.PlayerGroup));
            Assert.That(BotDutyLeases.HandOver(bot, Kind.PlayerGroup, null, Kind.BotLedGroupWithPlayer, "g-1", DutyLeaseReason.HostingPlayer,
                "Chris", bot.Group), Is.True);
            bot.LeavePlayerLedGroup();
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.BotLedGroupWithPlayer), "leaving the player's follow keeps the new lease");
                Assert.That(BotDutyLeases.Holds(bot, Kind.BotLedGroupWithPlayer, "g-1"), Is.True);
                Assert.That(BotDutyLeases.IsSpokenForByPlayer(bot), Is.True);
                Assert.That(bot.IsPlayerLedGroup, Is.False, "the bot leads now");
                foreach (Kind automatic in new[] { Kind.RealmRaid, Kind.RvrEvent, Kind.BattlegroundWave, Kind.BotGroupSession })
                    Assert.That(BotDutyLeases.Claim(bot, automatic, "other", "test").Granted, Is.False, automatic.ToString());
                Assert.That(AutonomousRealmRaid.IsEligible(bot), Is.False, "a raid can't take a bot from the player's group");
            });
            Assert.That(BotDutyLeases.WhyLines(bot, bot.Name, 0, 8), Has.Some.Contains("handed over leading a group with a player (g-1)"));
        }

        [Test]
        public void LeadMovesTheBotsBackToThePlayersGroup()
        {
            Bot bot = Gamebot(20);
            Player player = RealPlayer();
            bot.Group = Token();
            player.Group = bot.Group;
            BotDutyLeases.Claim(bot, Kind.BotLedGroupWithPlayer, "g-2", DutyLeaseReason.HostingPlayer, "Chris", bot.Group);
            Assert.That(bot.EnterPlayerLedGroup(player), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.PlayerGroup));
                Assert.That(bot.PlayerGroupLeader, Is.SameAs(player), "the bot follows the player, and its kills credit the player");
                Assert.That(BotDutyLeases.ReleaseOwner(Kind.BotLedGroupWithPlayer, "g-2"), Is.EqualTo(0), "nothing of the old lease is left over");
            });
        }

        [Test]
        public void AHandOverNeedsTheLeaseItSaysAndAGamebot()
        {
            Bot bot = Gamebot(20), companion = Companion();
            BotDutyLeases.Claim(bot, Kind.BotGroupSession, "g-3", DutyLeaseReason.GroupSession);
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.HandOver(bot, Kind.PlayerGroup, null, Kind.BotLedGroupWithPlayer, "g-3", "x"), Is.False, "it holds another kind");
                Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.BotGroupSession));
                Assert.That(BotDutyLeases.HandOver(companion, Kind.PlayerGroup, null, Kind.BotLedGroupWithPlayer, "g-3", "x"), Is.False);
                Assert.That(BotDutyLeases.HandOver(bot, Kind.BotGroupSession, null, Kind.RealmRaid, "r", "x"), Is.False, "not a player kind");
            });
            BotDutyLeases.Claim(bot, Kind.PlayerGroup, "Chris", DutyLeaseReason.PlayerGroup);
            Assert.That(BotDutyLeases.HandOver(bot, Kind.PlayerGroup, "Someone", Kind.BotLedGroupWithPlayer, "g-3", "x"), Is.False, "another player's");
        }

        #endregion

        #region The coordinator's objects

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

        private static object Session(Group group, string id, GameBot leader, int lockedSize)
        {
            object session = RuntimeHelpers.GetUninitializedObject(SessionType);
            SessionType.GetProperty("Group")!.SetValue(session, group);
            SessionType.GetProperty("Id")!.SetValue(session, id);
            SessionType.GetProperty("Leader")!.SetValue(session, leader);
            SessionType.GetProperty("Phase")!.SetValue(session, "Grinding");
            SessionType.GetProperty("LockedSize")!.SetValue(session, lockedSize);
            SessionType.GetProperty("ObjectiveKind")!.SetValue(session, eAutonomousObjectiveKind.GroupPve);
            SessionType.GetField("<PveRoles>k__BackingField", Hidden)!.SetValue(session, new Dictionary<long, BotPveGroupRole>());
            return session;
        }

        private static Dictionary<long, BotPveGroupRole> Roles(object session) =>
            (Dictionary<long, BotPveGroupRole>)SessionType.GetProperty("PveRoles")!.GetValue(session)!;

        private static bool Call(string method, params object[] arguments) =>
            (bool)typeof(AutonomousBotGroupCoordinator).GetMethod(method, HiddenStatic)!.Invoke(null, arguments)!;

        [Test]
        public void AHostedHealerLetsThePartyPullAsWellAsCountForTheRoster()
        {
            Bot tank = Gamebot(20), hitter = Gamebot(20);
            Player player = RealPlayer();
            Group group = GroupOf(tank, hitter, player);
            object session = Session(group, "g-4", tank, 2);
            Roles(session)[tank.DatabaseID] = BotPveGroupRole.Tank;
            Roles(session)[hitter.DatabaseID] = BotPveGroupRole.Attacker;
            GameBot[] bots = [tank, hitter];
            Assert.That(Call("HasActivePveComposition", session, bots), Is.False, "no healer, no player");
            SessionType.GetProperty("HostedPlayer")!.SetValue(session, player);
            SessionType.GetProperty("PlayerRole")!.SetValue(session, BotPveGroupRole.Healer);
            Assert.Multiple(() =>
            {
                Assert.That(Call("HasViablePveComposition", session, bots), Is.True);
                Assert.That(Call("HasActivePveComposition", session, bots), Is.True, "the player is the healer: the party pulls");
            });
            SessionType.GetProperty("PlayerRole")!.SetValue(session, BotPveGroupRole.Attacker);
            Assert.That(Call("HasActivePveComposition", session, bots), Is.False);
        }

        [Test]
        public void ABotOnlyPartysPullGateIsUnchanged()
        {
            Bot tank = Gamebot(20), healer = Gamebot(20), hitter = Gamebot(20);
            Group group = GroupOf(tank, healer, hitter);
            object session = Session(group, "g-5", tank, 3);
            Roles(session)[tank.DatabaseID] = BotPveGroupRole.Tank;
            Roles(session)[healer.DatabaseID] = BotPveGroupRole.Healer;
            Roles(session)[hitter.DatabaseID] = BotPveGroupRole.Attacker;
            Assert.That(Call("HasActivePveComposition", session, new GameBot[] { tank, healer, hitter }), Is.True);
            SessionType.GetProperty("LockedSize")!.SetValue(session, 4);
            Assert.That(Call("HasActivePveComposition", session, new GameBot[] { tank, healer, hitter }), Is.False, "a member short");
        }

        [Test]
        public void AHandoverEndsTheSessionWithoutBreakingTheGroupUp()
        {
            Bot leader = Gamebot(20), member = Gamebot(20);
            Player player = RealPlayer();
            Group group = GroupOf(leader, member, player);
            object session = Session(group, "g-6", leader, 2);
            SessionType.GetProperty("HostedPlayer")!.SetValue(session, player);
            var sessions = (System.Collections.IDictionary)typeof(AutonomousBotGroupCoordinator).GetField("Sessions", HiddenStatic)!.GetValue(null)!;
            sessions[group] = session;
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, "g-6"));
            try
            {
                foreach (Bot bot in new[] { leader, member })
                    BotDutyLeases.Claim(bot, Kind.BotLedGroupWithPlayer, "g-6", DutyLeaseReason.HostingPlayer, "Chris", group);
                Assert.That(Call("TryOfferLeadToLastBot", session, "test"), Is.False, "two gamebots left: no box");
                typeof(AutonomousBotGroupCoordinator).GetMethod("DetachSession", HiddenStatic)!.Invoke(null, [session, "handed over"]);
                Assert.Multiple(() =>
                {
                    Assert.That(sessions.Contains(group), Is.False, "the coordinator forgets the party");
                    Assert.That(SessionType.GetProperty("Ending")!.GetValue(session), Is.EqualTo(true));
                    Assert.That(BotLedGroups.IsHostedGroup(group), Is.False);
                    Assert.That(leader.Group, Is.SameAs(group), "the group is kept (and with it the loot ledger)");
                    Assert.That(member.Group, Is.SameAs(group));
                    Assert.That(player.Group, Is.SameAs(group));
                    Assert.That(BotDutyLeases.KindOf(leader), Is.EqualTo(Kind.BotLedGroupWithPlayer), "the caller moves the lease, not the detach");
                });
            }
            finally
            {
                sessions.Remove(group);
            }
        }

        [Test]
        public void ThePlayersGroupRolesComeFromEachBotsClassWhenNoRosterFits()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.NaturalRole(eCharacterClass.Cleric), Is.EqualTo(BotPveGroupRole.Healer));
                Assert.That(BotLedGroups.NaturalRole(eCharacterClass.Armsman), Is.EqualTo(BotPveGroupRole.Tank));
                Assert.That(BotLedGroups.NaturalRole(eCharacterClass.Wizard), Is.EqualTo(BotPveGroupRole.Attacker));
                Assert.That(BotLedGroups.NaturalRole(eCharacterClass.Healer), Is.EqualTo(BotPveGroupRole.Healer));
                Assert.That(BotLedGroups.NaturalRole(eCharacterClass.Hero), Is.EqualTo(BotPveGroupRole.Tank));
            });
        }

        #endregion
    }
}
