using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Events;
using DOL.GS.Quests;
using DOL.UnitTests;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // Whose quests a kill counts for (owner 2026-10-30): the leader of an autonomous gamebot's player-led group is
    // credited like a companion's owner, in the kill's region and within the group's credit range, and a kill counts
    // for a data quest once, not once per notification that carries it (owner 2026-10-31: told apart by the monster,
    // not by the sender, so the named mobs' forced credit with the killer as the sender still counts).
    [TestFixture, NonParallelizable]
    public sealed class UT_QuestKillCredit
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        // GameObject's static constructor reads the data quest cache from the database, so the object tests run with
        // an empty database behind a server test double, as the other object tests do.
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        }

        private GameServer _previous;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup() => GameServer.LoadTestDouble(_previous);

        [Test]
        public void TheLeaderIsCreditedForAnOwnerlessBotInItsGroupNearby()
            => Assert.That(QuestKillCredit.LeaderIsCredited(botHasOwner: false, leaderInBotsGroup: true, leaderInKillsRegion: true, leaderDistance: 1500), Is.True);

        [Test]
        public void ACompanionIsCreditedThroughItsOwnerNotHere()
            => Assert.That(QuestKillCredit.LeaderIsCredited(botHasOwner: true, leaderInBotsGroup: true, leaderInKillsRegion: true, leaderDistance: 0), Is.False);

        [Test]
        public void ALeaderTheBotNoLongerFightsWithIsNotCredited()
            => Assert.That(QuestKillCredit.LeaderIsCredited(botHasOwner: false, leaderInBotsGroup: false, leaderInKillsRegion: true, leaderDistance: 0), Is.False);

        [Test]
        public void ALeaderInAnotherRegionIsNotCredited()
            => Assert.That(QuestKillCredit.LeaderIsCredited(botHasOwner: false, leaderInBotsGroup: true, leaderInKillsRegion: false, leaderDistance: 0), Is.False);

        [Test]
        public void TheCreditRangeIsTheGroupsAndInclusive()
        {
            Assert.Multiple(() =>
            {
                Assert.That(QuestKillCredit.Range, Is.EqualTo(WorldMgr.MAX_EXPFORKILL_DISTANCE));
                Assert.That(QuestKillCredit.LeaderIsCredited(false, true, true, QuestKillCredit.Range), Is.True);
                Assert.That(QuestKillCredit.LeaderIsCredited(false, true, true, QuestKillCredit.Range + 1), Is.False);
            });
        }

        // The wiring on real objects: PlayerGroupLeader (EnterPlayerLedGroup), Owner, Group, region, distance and
        // whether the leader is alive.
        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public int TestX;
            public ushort TestRegion; // set after creation: an uninitialized object runs no field initializer
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => TestRegion; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
        }

        private sealed class Monster : GameNPC
        {
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        private static T Make<T>() where T : GameLiving
        {
            T living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            if (living is GameNPC)
                typeof(GameNPC).GetField("m_brains", Hidden).SetValue(living, new ArrayList());
            return living;
        }

        // The real IsAlive: an active object with health. An uninitialized player has none (no DBCharacter, so
        // GamePlayer.Health reads GameObject.m_health, which is 0), so a living leader is given a point of health.
        private static void SetHealth(GameLiving living, int health) =>
            typeof(GameObject).GetField("m_health", Hidden).SetValue(living, health);

        private static Group Token() => (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));

        private static (Player leader, Bot bot, Monster kill) InvitedBotGroup()
        {
            Player leader = Make<Player>();
            leader.TestRegion = 1;
            SetHealth(leader, 1);
            Bot bot = Make<Bot>();
            Group group = Token();
            leader.Group = group;
            bot.Group = group;
            Assert.That(bot.EnterPlayerLedGroup(leader), Is.True);
            return (leader, bot, Make<Monster>());
        }

        [Test]
        public void AnInvitedGamebotsKillCreditsTheLeaderInRange()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            leader.TestX = QuestKillCredit.Range;
            Assert.Multiple(() =>
            {
                Assert.That(bot.Owner, Is.Null, "an autonomous gamebot has no owner");
                Assert.That(leader.IsAlive, Is.True, "the test leader is alive and active");
                Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.SameAs(leader));
            });
        }

        [Test]
        public void ALeaderBeyondTheCreditRangeGetsNothing()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            leader.TestX = QuestKillCredit.Range + 1;
            Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null);
        }

        [Test]
        public void ALeaderInAnotherRegionGetsNothing()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            leader.TestRegion = 2;
            Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null);
        }

        [Test]
        public void ALeaderWhoLeftTheGroupGetsNothing()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            leader.Group = Token();
            Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null);
        }

        // Owner 2026-10-31: a dead leader lying in range while the bots fight on is not credited, as the other readers
        // of PlayerGroupLeader require the leader alive and active. Health 0 fails IsAlive; an object no longer in the
        // world fails the ObjectState check that IsAlive also makes.
        [Test]
        public void ADeadLeaderInRangeGetsNothing()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            SetHealth(leader, 0);
            Assert.Multiple(() =>
            {
                Assert.That(leader.IsAlive, Is.False);
                Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null);
            });

            SetHealth(leader, 1);
            leader.ObjectState = GameObject.eObjectState.Deleted;
            Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null, "a leader removed from the world");
        }

        [Test]
        public void ABotOnItsOwnCreditsNobody()
        {
            Bot bot = Make<Bot>();
            Assert.Multiple(() =>
            {
                Assert.That(QuestKillCredit.GroupLeaderFor(bot, Make<Monster>()), Is.Null);
                Assert.That(QuestKillCredit.GroupLeaderFor(null, Make<Monster>()), Is.Null);
                Assert.That(QuestKillCredit.GroupLeaderFor(bot, null), Is.Null);
            });
        }

        [Test]
        public void ACompanionWithAnOwnerIsLeftToTheOwnerPath()
        {
            var (leader, bot, kill) = InvitedBotGroup();
            typeof(GameBot).GetField("<Owner>k__BackingField", Hidden).SetValue(bot, leader);
            Assert.That(QuestKillCredit.GroupLeaderFor(bot, kill), Is.Null);
        }

        // The pure once-per-kill rule: the same monster within the window is the same kill, whoever sent the word.
        [Test]
        public void ARepeatIsTheSameMonsterWithinTheWindow()
        {
            Monster wolf = Make<Monster>();
            Monster bear = Make<Monster>();
            Assert.Multiple(() =>
            {
                Assert.That(QuestKillCredit.IsRepeat(wolf, wolf, lastCountedAt: 1000, now: 1000), Is.True, "the synchronous repeat");
                Assert.That(QuestKillCredit.IsRepeat(wolf, wolf, lastCountedAt: 1000, now: 1000 + QuestKillCredit.RepeatWindowMs - 1), Is.True);
                Assert.That(QuestKillCredit.IsRepeat(wolf, wolf, lastCountedAt: 1000, now: 1000 + QuestKillCredit.RepeatWindowMs), Is.False, "a respawn");
                Assert.That(QuestKillCredit.IsRepeat(bear, wolf, lastCountedAt: 1000, now: 1000), Is.False, "another monster");
                Assert.That(QuestKillCredit.IsRepeat(wolf, null, lastCountedAt: 0, now: 0), Is.False, "nothing counted yet");
                Assert.That(QuestKillCredit.IsRepeat(null, wolf, lastCountedAt: 1000, now: 1000), Is.False);
            });
        }

        // DataQuest.Notify: the kill advances the quest once, on whichever notification of it comes first.
        private sealed class CountingQuest : DataQuest
        {
            public int Kills;
            public long Clock;
            public CountingQuest(GamePlayer player) => m_questPlayer = player;
            protected override long KillClock => Clock;
            protected override void OnEnemyKilled(GamePlayer player, GameLiving living) => Kills++;
        }

        [Test]
        public void ADataQuestCountsAKillOnceNotOncePerGroupmate()
        {
            Player questPlayer = Make<Player>();
            Player groupmate = Make<Player>();
            Player anotherGroupmate = Make<Player>();
            var quest = new CountingQuest(questPlayer);
            var killed = new EnemyKilledEventArgs(Make<Monster>());

            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, killed);
            quest.Notify(GamePlayerEvent.EnemyKilled, groupmate, killed);
            quest.Notify(GamePlayerEvent.EnemyKilled, anotherGroupmate, killed);

            Assert.That(quest.Kills, Is.EqualTo(1));
        }

        // The named mobs (the dragons, Legion and the others) tell every player in sight about the kill with the killer
        // as the sender; filtering on the sender silenced them (the first fix of 2026-10-30).
        [Test]
        public void AForcedCreditWithTheKillerAsSenderCounts()
        {
            Player bystander = Make<Player>();
            Bot killer = Make<Bot>();
            var quest = new CountingQuest(bystander);
            var dragon = new EnemyKilledEventArgs(Make<Monster>());

            quest.Notify(GamePlayerEvent.EnemyKilled, killer, dragon);
            Assert.That(quest.Kills, Is.EqualTo(1), "the killer as the sender");

            quest.Notify(GamePlayerEvent.EnemyKilled, bystander, dragon);
            Assert.That(quest.Kills, Is.EqualTo(1), "the player's own notification of the same kill is a repeat");
        }

        [Test]
        public void TheSameMonsterAgainAfterTheWindowIsANewKill()
        {
            Player questPlayer = Make<Player>();
            var quest = new CountingQuest(questPlayer);
            var killed = new EnemyKilledEventArgs(Make<Monster>());

            quest.Clock = 5000;
            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, killed);
            quest.Clock = 5000 + QuestKillCredit.RepeatWindowMs - 1;
            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, killed);
            Assert.That(quest.Kills, Is.EqualTo(1), "still the same kill");

            quest.Clock = 5000 + QuestKillCredit.RepeatWindowMs;
            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, killed);
            Assert.That(quest.Kills, Is.EqualTo(2), "the respawned monster killed again");
        }

        [Test]
        public void ADifferentMonsterWithinTheWindowCounts()
        {
            Player questPlayer = Make<Player>();
            var quest = new CountingQuest(questPlayer);

            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, new EnemyKilledEventArgs(Make<Monster>()));
            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, new EnemyKilledEventArgs(Make<Monster>()));

            Assert.That(quest.Kills, Is.EqualTo(2));
        }

        [Test]
        public void AKillWithoutAMonsterIsIgnored()
        {
            Player questPlayer = Make<Player>();
            var quest = new CountingQuest(questPlayer);

            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, new EnemyKilledEventArgs(null));
            quest.Notify(GamePlayerEvent.EnemyKilled, questPlayer, null);

            Assert.That(quest.Kills, Is.Zero);
        }

        [Test]
        public void ADataQuestWithoutAPlayerIgnoresKills()
        {
            var quest = new CountingQuest(null);
            quest.Notify(GamePlayerEvent.EnemyKilled, Make<Player>(), new EnemyKilledEventArgs(Make<Monster>()));
            Assert.That(quest.Kills, Is.Zero);
        }
    }
}
