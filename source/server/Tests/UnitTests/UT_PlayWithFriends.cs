using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Play with friends (1.2, co-op audit 2026-11-03): two real players in one bot world. Companions follow their owner,
    /// invited gamebots follow the group's leader, /pull and the pilot move only the bots a player owns or leads, /travel
    /// and the pilot's quickbars are host-PC only, companions have a server-wide budget, reply cooldowns are per player,
    /// a gamebot's kill credits every eligible human once, and a bot leader invites one human at a time. Each rule is also
    /// checked with one human, where nothing may change.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PlayWithFriends
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private readonly List<GameLiving> _actors = new();

        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) => attacker.Realm != defender.Realm;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new Rules();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public string TestName;
            public eRealm TestRealm;
            public bool Dead;
            public int TestX;
            public GameClient Connection;
            public override string Name { get => TestName; set => TestName = value; }
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => !Dead;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => TestRealm; set { } }
            public override GameObject TargetObject { get; set; }
            public override IControlledBrain ControlledBrain { get; set; }
            public override IPacketLib Out => new BotDummyPacketLib();
            public override GameClient Client => Connection;
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public eRealm TestRealm = eRealm.Albion;
            public override bool IsAlive => true;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override short MaxSpeed => 200;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }
            public override byte Level { get => 50; set { } }
            public override int EffectiveLevel => 50;
            public override int MeleeAttackRange => 200;
            public override ICharacterClass CharacterClass => TestClass;
            public override IControlledBrain ControlledBrain { get; set; }
            public override void WalkTo(Vector3 target, short speed) { }
            public override void StopAttack() { }
            public override void StartAttack(GameObject target) { }
            public override void Follow(GameObject target, int minDistance, int maxDistance) { }
            public override int Mana { get; set; }
            public override byte EndurancePercent => 0;
            public override int GetModified(eProperty property) => property == eProperty.SpellRange ? 100 : 0;
        }

        private sealed class Enemy : GameNPC
        {
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Midgard; set { } }
            public override GameObject TargetObject { get; set; }
            public override byte Level { get => 50; set { } }
            public override int EffectiveLevel => 50;
        }

        private sealed class Brain : BotBrain
        {
            public int Pulls;
            public override void AttackMostWanted() { }
            public override bool CheckSpells(eCheckSpellType type) => false;
            public override bool OrderPull(GameLiving target) { Pulls++; return true; }
            public override void CancelOrderedPull(GameLiving target) { }
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            BotDutyLeases.Clear();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameLiving actor in _actors)
                ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            BotDutyLeases.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        #region Kit

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            actor.ObjectState = GameObject.eObjectState.Active;
            _actors.Add(actor);
            return actor;
        }

        private Player Human(string name, eRealm realm = eRealm.Albion, bool host = true)
        {
            Player player = Actor<Player>();
            player.TestName = name;
            player.TestRealm = realm;
            player.Connection = (GameClient)RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
            Field(typeof(DOL.Network.BaseClient), player.Connection, "<IsLoopbackConnection>k__BackingField", host);
            return player;
        }

        /// <summary>A bot: a companion of <paramref name="owner"/> (a /spawn helper unless <paramref name="temporary"/> is false), or with no owner an autonomous gamebot.</summary>
        private Bot MakeBot(ICharacterClass characterClass, GamePlayer owner = null, bool temporary = true, bool brain = true)
        {
            Bot bot = Actor<Bot>();
            bot.Name = characterClass.Name;
            bot.TestClass = characterClass;
            if (owner != null)
            {
                Field(typeof(GameBot), bot, "<Owner>k__BackingField", owner);
                Field(typeof(GameBot), bot, "<IsTemporaryGroupHelper>k__BackingField", temporary);
            }
            else
                Field(typeof(GameBot), bot, "<IsAutonomousWorldBot>k__BackingField", true);
            if (brain)
                Field(typeof(GameNPC), bot, "m_ownBrain", new Brain { Body = bot });
            return bot;
        }

        private static Group GroupLedBy(Player leader, params GameLiving[] others)
        {
            var group = new Group(leader);
            Add(group, leader);
            foreach (GameLiving member in others)
                Add(group, member);
            return group;
        }

        private static void Join(Bot bot, Player follows) => Assert.That(bot.EnterPlayerLedGroup(follows), Is.True, bot.Name);
        private static List<GameLiving> Members(Group group) => (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
        private static void Add(Group group, GameLiving member) { Members(group).Add(member); member.Group = group; member.GroupIndex = (byte)(Members(group).Count - 1); }
        private static void Remove(GameLiving member) { Members(member.Group).Remove(member); member.Group = null; }
        private static void SetLeader(Group group, GameLiving leader) => Field(typeof(Group), group, "<LivingLeader>k__BackingField", leader);
        private static void Field(Type type, object owner, string name, object value) => type.GetField(name, Hidden).SetValue(owner, value);
        private static Brain B(Bot bot) => (Brain)bot.Brain;

        /// <summary>Chris leads; Alys is in the group. Each has a /spawn companion; one gamebot was invited by Chris.</summary>
        private (Player chris, Player alys, Bot chrisTank, Bot chrisCaster, Bot alysCaster, Bot gamebot, Group group, Enemy enemy) TwoHumans()
        {
            Player chris = Human("Chris"), alys = Human("Alys", host: false);
            Bot chrisTank = MakeBot(new ClassPaladin(), chris), chrisCaster = MakeBot(new ClassCabalist(), chris);
            Bot alysCaster = MakeBot(new ClassWizard(), alys), gamebot = MakeBot(new ClassMercenary());
            Group group = GroupLedBy(chris, alys, chrisTank, chrisCaster, alysCaster, gamebot);
            Join(chrisTank, chris);
            Join(chrisCaster, chris);
            Join(alysCaster, alys);
            Join(gamebot, chris);
            return (chris, alys, chrisTank, chrisCaster, alysCaster, gamebot, group, Actor<Enemy>());
        }

        #endregion

        #region 1. Who a bot follows

        [Test]
        public void ACompanionNeverFollowsAnotherHuman()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot companion = MakeBot(new ClassCleric(), chris);
            Assert.Multiple(() =>
            {
                Assert.That(companion.EnterPlayerLedGroup(alys), Is.False, "Alys can't take Chris's companion");
                Assert.That(companion.PlayerGroupLeader, Is.Null);
                Assert.That(companion.EnterPlayerLedGroup(chris), Is.True, "its owner can");
                Assert.That(companion.EnterPlayerLedGroup(alys), Is.False);
                Assert.That(companion.PlayerGroupLeader, Is.SameAs(chris));
            });
        }

        [Test]
        public void MakeLeaderHandsTheGamebotsToTheNewLeaderAndLeavesEachCompanionWithItsOwner()
        {
            var (chris, alys, chrisTank, _, alysCaster, gamebot, group, _) = TwoHumans();
            Assert.That(group.MakeLeader(alys), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(group.Leader, Is.SameAs(alys));
                Assert.That(gamebot.PlayerGroupLeader, Is.SameAs(alys), "an invited gamebot follows the group's leader");
                Assert.That(chrisTank.PlayerGroupLeader, Is.SameAs(chris), "Chris's companion stays with Chris");
                Assert.That(alysCaster.PlayerGroupLeader, Is.SameAs(alys));
                Assert.That(BotHumanBinding.RightfulLeader(gamebot), Is.SameAs(alys));
                Assert.That(BotHumanBinding.RightfulLeader(chrisTank), Is.SameAs(chris));
            });
        }

        [Test]
        public void WhenTheLeaderLeavesTheGamebotsFollowTheNewLeaderNotTheFirstOtherPlayer()
        {
            Player chris = Human("Chris"), dana = Human("Dana", host: false), alys = Human("Alys", host: false);
            Bot gamebot = MakeBot(new ClassMercenary(), brain: false);
            Group group = GroupLedBy(chris, dana, alys, gamebot);
            Join(gamebot, chris);
            Remove(chris);
            SetLeader(group, alys); // RemoveMember elected Alys (Dana is first in the list)
            group.ReleaseGamebotsLedBy(chris);
            Assert.That(gamebot.PlayerGroupLeader, Is.SameAs(alys));
            Assert.That(gamebot.Group, Is.SameAs(group));
        }

        [Test]
        public void ALeavingPlayersCompanionsLeaveWithThemOnlyWhenAnotherHumanStays()
        {
            Player chris = Human("Chris"), alys = Human("Alys", host: false);
            Bot alysHealer = MakeBot(new ClassCleric(), alys, temporary: false, brain: false);
            Bot chrisTank = MakeBot(new ClassPaladin(), chris, temporary: false, brain: false);
            Bot chrisCaster = MakeBot(new ClassCabalist(), chris, temporary: false, brain: false);
            Group group = GroupLedBy(chris, alys, alysHealer, chrisTank, chrisCaster);
            Join(alysHealer, alys);
            Join(chrisTank, chris);
            Join(chrisCaster, chris);
            Remove(alys);
            group.ReleaseCompanionsOf(alys);
            Assert.Multiple(() =>
            {
                Assert.That(alysHealer.Group, Is.Null, "Alys's companion left with her");
                Assert.That(chrisTank.Group, Is.SameAs(group), "Chris's companions stay");
                Assert.That(chrisCaster.Group, Is.SameAs(group));
            });

            // One human: nothing changes (the group ends by the old rules).
            Remove(chris);
            group.ReleaseCompanionsOf(chris);
            Assert.That(chrisTank.Group, Is.SameAs(group), "no other human: the companions are left as before");
        }

        [Test]
        public void AnotherPlayersCompanionCantBeInvited()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot companion = MakeBot(new ClassCleric(), chris, temporary: false, brain: false);
            Assert.That(BotGroupInvite.TryInvite(alys, companion), Is.False);
            Assert.That(alys.Group, Is.Null, "no group was made for the refused invite");
        }

        [Test]
        public void WhoCommandsWhichBot()
        {
            Assert.Multiple(() =>
            {
                // Companions: only their owner, whoever leads.
                Assert.That(BotHumanBinding.CommandsRule(true, ownedByPlayer: true, true, false, true, false), Is.True);
                Assert.That(BotHumanBinding.CommandsRule(true, ownedByPlayer: false, true, true, true, false), Is.False);
                // Gamebots: the leader, or the only human.
                Assert.That(BotHumanBinding.CommandsRule(false, false, otherHumanInGroup: true, playerLeads: true, true, false), Is.True);
                Assert.That(BotHumanBinding.CommandsRule(false, false, otherHumanInGroup: true, playerLeads: false, true, true), Is.False);
                Assert.That(BotHumanBinding.CommandsRule(false, false, otherHumanInGroup: false, playerLeads: false, true, false), Is.True);
            });
        }

        #endregion

        #region 2. /pull and attacks

        [Test]
        public void APullByTheOtherHumanOrdersOnlyTheirOwnCompanions()
        {
            var (chris, alys, chrisTank, chrisCaster, alysCaster, gamebot, _, enemy) = TwoHumans();
            string answer = PlayerLedPullCoordinator.Begin(alys, enemy);
            Assert.Multiple(() =>
            {
                Assert.That(answer, Does.Contain("Pull ordered"));
                Assert.That(B(alysCaster).Pulls, Is.EqualTo(1), "Alys's companion answers her pull");
                Assert.That(B(chrisTank).Pulls, Is.Zero, "Chris's tank is not hers to order");
                Assert.That(B(chrisCaster).Pulls, Is.Zero);
                Assert.That(B(gamebot).Pulls, Is.Zero, "the gamebots follow the leader");
                Assert.That(chrisTank.PlayerGroupLeader, Is.SameAs(chris), "nothing was rebound to Alys");
                Assert.That(chrisCaster.PlayerGroupLeader, Is.SameAs(chris));
                Assert.That(gamebot.PlayerGroupLeader, Is.SameAs(chris));
            });
        }

        [Test]
        public void TheLeadersTankGatedPullWaitsOnlyForTheirOwnBotsAndTheOtherHumanStillFights()
        {
            var (chris, alys, chrisTank, chrisCaster, alysCaster, gamebot, _, enemy) = TwoHumans();
            Assert.That(PlayerLedPullCoordinator.Begin(chris, enemy), Does.Contain("leads the pull"));
            Assert.Multiple(() =>
            {
                Assert.That(B(chrisTank).Pulls, Is.EqualTo(1));
                Assert.That(PlayerLedPullCoordinator.IsWaiting(chrisCaster), Is.True);
                Assert.That(PlayerLedPullCoordinator.IsWaiting(gamebot), Is.True, "the leader's gamebot waits for the tank");
                Assert.That(PlayerLedPullCoordinator.IsWaiting(alysCaster), Is.False, "Alys's companion is not held by Chris's pull");
                Assert.That(alysCaster.PlayerGroupLeader, Is.SameAs(alys));
            });

            // Alys attacks: her companion joins her, Chris's pull keeps its tank gate.
            PlayerLedPullCoordinator.LeaderEngaged(alys, enemy);
            Assert.Multiple(() =>
            {
                Assert.That(B(alysCaster).Pulls, Is.EqualTo(1));
                Assert.That(PlayerLedPullCoordinator.IsWaiting(chrisCaster), Is.True);
                Assert.That(B(chrisCaster).Pulls, Is.Zero);
            });

            // Alys's own /pull while Chris's is waiting doesn't cancel it.
            PlayerLedPullCoordinator.Begin(alys, enemy);
            Assert.That(PlayerLedPullCoordinator.IsWaiting(chrisCaster), Is.True);
        }

        [Test]
        public void ANonLeaderWithNoCompanionsIsToldWhoTheGamebotsFollow()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot gamebot = MakeBot(new ClassMercenary());
            GroupLedBy(chris, alys, gamebot);
            Join(gamebot, chris);
            Assert.That(PlayerLedPullCoordinator.Begin(alys, Actor<Enemy>()), Does.Contain("follow Chris"));
            Assert.That(B(gamebot).Pulls, Is.Zero);
        }

        [Test]
        public void WithOneHumanEveryBotInTheGroupAnswersThePull()
        {
            Player chris = Human("Chris");
            Bot caster = MakeBot(new ClassCabalist(), chris), gamebot = MakeBot(new ClassMercenary());
            GroupLedBy(chris, caster, gamebot);
            Join(caster, chris);
            Join(gamebot, chris);
            PlayerLedPullCoordinator.Begin(chris, Actor<Enemy>());
            Assert.That(B(caster).Pulls, Is.EqualTo(1));
            Assert.That(B(gamebot).Pulls, Is.EqualTo(1));
        }

        #endregion

        #region 3. The /bot pilot

        private static void PreserveParty(GamePlayer player)
        {
            Type stateType = typeof(AutonomousPlayerPilot).GetNestedType("PilotState", BindingFlags.NonPublic);
            object state = Activator.CreateInstance(stateType, player);
            typeof(AutonomousPlayerPilot).GetMethod("PreserveSpawnedParty", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [state]);
        }

        [Test]
        public void ThePilotOfTheOtherHumanKeepsOnlyTheirOwnBots()
        {
            var (chris, alys, chrisTank, chrisCaster, alysCaster, gamebot, _, _) = TwoHumans();
            PreserveParty(alys);
            Assert.Multiple(() =>
            {
                Assert.That(chrisTank.PlayerGroupLeader, Is.SameAs(chris), "Alys's pilot doesn't take Chris's companions");
                Assert.That(chrisCaster.PlayerGroupLeader, Is.SameAs(chris));
                Assert.That(gamebot.PlayerGroupLeader, Is.SameAs(chris), "nor the gamebots, which follow the leader");
                Assert.That(alysCaster.PlayerGroupLeader, Is.SameAs(alys));
            });
        }

        [Test]
        public void TheLeadersPilotKeepsTheGamebotsButNotTheOtherHumansCompanion()
        {
            var (chris, alys, _, _, alysCaster, gamebot, _, _) = TwoHumans();
            gamebot.LeavePlayerLedGroup();
            PreserveParty(chris);
            Assert.That(gamebot.PlayerGroupLeader, Is.SameAs(chris));
            Assert.That(alysCaster.PlayerGroupLeader, Is.SameAs(alys));
        }

        #endregion

        #region 4. Host-PC only

        [Test]
        public void HostPcFeaturesFailClosedForAFriendOnAnotherPc()
        {
            Player host = Human("Chris", host: true), friend = Human("Alys", host: false);
            Assert.Multiple(() =>
            {
                Assert.That(HostPcFeatures.IsOnHostPc(host), Is.True);
                Assert.That(HostPcFeatures.IsOnHostPc(friend), Is.False);
                Assert.That(HostPcFeatures.IsOnHostPc(Human("Mira", eRealm.Midgard, host: false)), Is.False, "another realm's friend");
                Assert.That(HostPcFeatures.IsOnHostPc(null), Is.False);
                Assert.That(AutonomousPlayerQuickbarProfile.TryLoad(friend, out var profile, out string error), Is.False);
                Assert.That(profile, Is.Null);
                Assert.That(error, Is.EqualTo(HostPcFeatures.PilotHostOnly));
                Assert.That(HostPcFeatures.TravelHostOnly, Does.Contain("PC that runs the server"));
            });
        }

        #endregion

        #region 5. The companion budget

        [Test]
        public void TheBudgetAppliesOnlyWithMoreThanOneHumanOnline()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CompanionBudget.Blocks(new(HumansOnline: 1, Own: 79, ServerTotal: 400), 160, 79), Is.Null,
                    "one human: a /raid 80 works as always");
                Assert.That(CompanionBudget.Blocks(new(2, 78, 100), 160, 79), Is.Null, "a friend online: a full raid still fits");
                Assert.That(CompanionBudget.Blocks(new(2, 79, 100), 160, 79), Is.EqualTo("per_player"));
                Assert.That(CompanionBudget.Blocks(new(3, 10, 160), 160, 79), Is.EqualTo("server_total"));
                Assert.That(CompanionBudget.Blocks(new(4, 200, 500), 0, 0), Is.Null, "0 turns both off");
            });
        }

        [Test]
        public void TheDefaultsLetOneFullRaidAndASecondOneOut()
        {
            ServerPropertyAttribute Attribute(string field) =>
                typeof(Properties).GetField(field).GetCustomAttribute<ServerPropertyAttribute>();
            int total = (int)Attribute(nameof(Properties.COMPANION_BUDGET_TOTAL)).DefaultValue;
            int perPlayer = (int)Attribute(nameof(Properties.COMPANION_BUDGET_PER_PLAYER)).DefaultValue;
            Assert.Multiple(() =>
            {
                Assert.That(perPlayer, Is.GreaterThanOrEqualTo(79), "a /raid 80 has 79 companions");
                Assert.That(total, Is.GreaterThanOrEqualTo(2 * 79));
                Assert.That(Attribute(nameof(Properties.COMPANION_BUDGET_TOTAL)).Key, Is.EqualTo("companion_budget_total"));
            });
        }

        [Test]
        public void TheBudgetCountsEveryHumansCompanionsInEveryRealmOnce()
        {
            Player chris = Human("Chris"), alys = Human("Alys"), mira = Human("Mira", eRealm.Midgard);
            Bot chrisOne = MakeBot(new ClassCleric(), chris, brain: false), chrisTwo = MakeBot(new ClassPaladin(), chris, brain: false);
            Bot alysOne = MakeBot(new ClassWizard(), alys, brain: false);
            Bot miraOne = MakeBot(new ClassHealer(), mira, brain: false);
            miraOne.TestRealm = eRealm.Midgard;
            GroupLedBy(chris, alys, chrisOne, chrisTwo, alysOne, MakeBot(new ClassMercenary(), brain: false));
            GroupLedBy(mira, miraOne);
            CompanionBudget.Counts counts = CompanionBudget.CountsFor(alys, [chris, alys, mira]);
            Assert.Multiple(() =>
            {
                Assert.That(counts.HumansOnline, Is.EqualTo(3));
                Assert.That(counts.Own, Is.EqualTo(1), "Alys's own companion");
                Assert.That(counts.ServerTotal, Is.EqualTo(4), "two of Chris's, Alys's and Mira's; the gamebot is no companion");
                Assert.That(CompanionBudget.CountsFor(chris, [chris]).HumansOnline, Is.EqualTo(1));
                Assert.That(CompanionBudget.Blocks(counts, total: 4, perPlayer: 79), Is.EqualTo("server_total"));
                Assert.That(CompanionBudget.Message("server_total", counts, 4, 79), Does.Contain("companion_budget_total"));
            });
        }

        #endregion

        #region 6. Reply cooldowns

        [Test]
        public void OneHumansLineNeverSilencesTheRepliesToAnother()
        {
            var faction = new Dictionary<(eRealm, string), long>();
            const int gap = AutonomousBotChatCoordinator.FactionReplyGapMs;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Albion, "chris", 1_000, gap, false), Is.True);
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Albion, "alys", 1_500, gap, false), Is.True,
                    "same realm, another player: answered");
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Albion, "chris", 2_000, gap, false), Is.False,
                    "the same player within the gap: as before");
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Albion, "chris", 2_000, gap, true), Is.True,
                    "a line that needs an answer always gets one");
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Midgard, "mira", 2_100, gap, false), Is.True,
                    "another realm");
                Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(faction, eRealm.Albion, "chris", 2_000 + gap, gap, false), Is.True,
                    "after the gap");
            });
            var say = new Dictionary<(ushort, string), long>();
            Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(say, (ushort)1, "chris", 0, AutonomousBotChatCoordinator.LocalReplyGapMs, false), Is.True);
            Assert.That(AutonomousBotChatCoordinator.TryTakeReplyTurn(say, (ushort)1, "alys", 10, AutonomousBotChatCoordinator.LocalReplyGapMs, false), Is.True);
        }

        [Test]
        public void TheReplyKeyIsTheCharacterNotTheRealm()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            chris.InternalID = "char-1";
            Assert.That(AutonomousBotChatCoordinator.ReplyKey(chris), Is.EqualTo("char-1"));
            Assert.That(AutonomousBotChatCoordinator.ReplyKey(alys), Is.EqualTo("Alys"), "a character with no id yet: its name");
        }

        #endregion

        #region 7. Quest credit

        private static List<GamePlayer> Credited(GameLiving killed, params GameLiving[] attackers) =>
            QuestKillCredit.PlayersCredited(killed, attackers, attacker => Array.IndexOf(attackers, attacker) >= 0, new List<GameLiving>());

        [Test]
        public void AGamebotsKillCreditsBothHumansOnce()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot gamebot = MakeBot(new ClassMercenary(), brain: false);
            GroupLedBy(chris, alys, gamebot);
            Join(gamebot, chris);
            List<GamePlayer> credited = Credited(Actor<Enemy>(), gamebot);
            Assert.That(credited.Count(player => player == chris), Is.EqualTo(1));
            Assert.That(credited.Count(player => player == alys), Is.EqualTo(1));
            Assert.That(credited, Has.Count.EqualTo(2));
        }

        [Test]
        public void ADeadLeaderStillLetsTheOtherHumanBeCredited()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot gamebot = MakeBot(new ClassMercenary(), brain: false);
            GroupLedBy(chris, alys, gamebot);
            Join(gamebot, chris);
            chris.Dead = true;
            Enemy kill = Actor<Enemy>();
            Assert.Multiple(() =>
            {
                Assert.That(QuestKillCredit.GroupLeaderFor(gamebot, kill), Is.Null, "the dead leader isn't the one credited");
                Assert.That(QuestKillCredit.GroupmateFor(gamebot, kill), Is.SameAs(alys));
                List<GamePlayer> credited = Credited(kill, gamebot);
                Assert.That(credited.Count(player => player == alys), Is.EqualTo(1));
                Assert.That(credited.Distinct().Count(), Is.EqualTo(credited.Count), "nobody twice");
            });

            alys.TestX = QuestKillCredit.Range + 1;
            Assert.That(QuestKillCredit.GroupmateFor(gamebot, kill), Is.Null, "a friend out of range gets nothing either");
        }

        [Test]
        public void WithOneDeadHumanAGamebotsKillStillCreditsNobody()
        {
            Player chris = Human("Chris");
            Bot gamebot = MakeBot(new ClassMercenary(), brain: false);
            GroupLedBy(chris, gamebot);
            Join(gamebot, chris);
            chris.Dead = true;
            Assert.That(Credited(Actor<Enemy>(), gamebot), Is.Empty);
        }

        [Test]
        public void ACompanionsKillCreditsItsOwnerAndTheFriendOnce()
        {
            Player chris = Human("Chris"), alys = Human("Alys");
            Bot companion = MakeBot(new ClassWizard(), alys);
            GroupLedBy(chris, alys, companion);
            Join(companion, alys);
            List<GamePlayer> credited = Credited(Actor<Enemy>(), companion);
            Assert.That(credited, Is.EquivalentTo(new GamePlayer[] { alys, chris }));
        }

        #endregion

        #region 8. Bot leaders' invites

        [Test]
        public void ALeaderInvitesOneHumanAtATime()
        {
            var memories = new Dictionary<string, BotLedGroups.InviteMemory>
            {
                ["Chris"] = new(0) { Pending = true, PendingLeaderId = 7 },
                ["Alys"] = new(0),
                ["Mira"] = new(0) { Pending = false, PendingLeaderId = 9 },
            };
            HashSet<long> forAlys = BotLedGroups.LeadersInvitingOthers(memories, "Alys");
            HashSet<long> forChris = BotLedGroups.LeadersInvitingOthers(memories, "Chris");
            BotLedGroups.Candidate Party(long id) => new(id, "Leader" + id, eRealm.Albion, 20, 6, 8, true, Hunting: true, OnRealmDuty: false,
                Reachable: true, HasPlayer: false, Distance: 1000 + id);
            Assert.Multiple(() =>
            {
                Assert.That(forAlys, Is.EquivalentTo(new long[] { 7 }), "Chris's box is waiting on leader 7; Mira answered hers");
                Assert.That(forChris, Is.Empty, "a player's own waiting box doesn't count against them");
                Assert.That(BotLedGroups.PickInvitingLeader([Party(7), Party(8)], eRealm.Albion, 20, false, memories["Alys"], 0, forAlys)?.LeaderId,
                    Is.EqualTo(8), "Alys gets another leader's invite");
                Assert.That(BotLedGroups.PickInvitingLeader([Party(7)], eRealm.Albion, 20, false, memories["Alys"], 0, forAlys), Is.Null);
                Assert.That(BotLedGroups.PickInvitingLeader([Party(7)], eRealm.Albion, 20, false, memories["Alys"], 0)?.LeaderId, Is.EqualTo(7),
                    "with nobody else waiting, as before");
                Assert.That(BotLedGroups.CadenceBlocks(memories["Chris"], 0, true, long.MaxValue), Is.EqualTo("box_waiting"),
                    "one waiting invite per human");
            });
        }

        #endregion
    }
}
