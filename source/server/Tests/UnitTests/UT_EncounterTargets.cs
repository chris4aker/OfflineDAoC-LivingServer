using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The shared target helper of the boss scripts (EncounterTargets): the pool is the raid (real players and bots) with
    /// the script's own filters, random picks never pick the human more often than before, and moves stay player-only
    /// until bots can recover from them.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EncounterTargets
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive = true, Stealth;
            public eRealm TestRealm = eRealm.Midgard;
            public override bool IsAlive => Alive;
            public override bool IsStealthed => Stealth;
            public override eRealm Realm { get => TestRealm; set { } }
            public override ICharacterClass CharacterClass => new ClassWarrior();
        }

        public sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public bool Alive = true, Stealth;
            public override bool IsAlive => Alive;
            public override bool IsStealthed => Stealth;
            public override eRealm Realm { get => eRealm.Midgard; set { } }
        }

        public sealed class Boss : GameNPC
        {
            public eRealm TestRealm = eRealm.None;
            public override eRealm Realm { get => TestRealm; set { } }
        }

        internal static Bot NewBot(eRealm realm = eRealm.Midgard)
        {
            Bot bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            bot.Alive = true;
            bot.TestRealm = realm;
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(bot, new ArrayList());
            bot.ObjectState = GameObject.eObjectState.Active;
            return bot;
        }

        internal static Player NewPlayer(ePrivLevel priv = ePrivLevel.Player)
        {
            Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.Alive = true;
            var client = new BotDummyClient();
            client.Account.PrivLevel = (uint)priv;
            typeof(GamePlayer).GetField("m_client", Hidden)!.SetValue(player, client);
            return player;
        }

        private static Boss NewBoss() => (Boss)RuntimeHelpers.GetUninitializedObject(typeof(Boss));

        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp() => _server = new EpicTestServerScope();

        [TearDown]
        public void TearDown()
        {
            EncounterTargets.BotsCanBeDisplaced = false;
            _server.Dispose();
        }

        [Test]
        public void ThePoolIsEveryPlayerTheScriptSawPlusTheBots()
        {
            var boss = NewBoss();
            var human = NewPlayer();
            var gm = NewPlayer(ePrivLevel.GM);
            var companion = NewBot();
            var gamebot = NewBot(eRealm.Albion);
            var adds = new GameNPC[] { NewBoss(), companion, gamebot };

            var all = EncounterTargets.Pool(boss, [human, gm], adds);
            Assert.That(all, Is.EqualTo(new GameLiving[] { human, gm, companion, gamebot }), "no filter: players first, then bots, no other NPC");
            var notGm = EncounterTargets.Pool(boss, [human, gm], adds, EncounterTargets.NotGm);
            Assert.That(notGm, Is.EqualTo(new GameLiving[] { human, companion, gamebot }), "PrivLevel == 1: GMs out, bots in");
            Assert.That(EncounterTargets.Pool(boss, [human, gm], adds, EncounterTargets.NotGm, bots: false),
                Is.EqualTo(new GameLiving[] { human }), "bots: false is the old player-only pool");
        }

        [Test]
        public void TheScriptsFiltersApplyToBotsAsToPlayers()
        {
            var boss = NewBoss();
            var deadPlayer = NewPlayer(); deadPlayer.Alive = false;
            var hiddenPlayer = NewPlayer(); hiddenPlayer.Stealth = true;
            var deadBot = NewBot(); deadBot.Alive = false;
            var hiddenBot = NewBot(); hiddenBot.Stealth = true;
            var liveBot = NewBot();
            var pool = EncounterTargets.Pool(boss, [deadPlayer, hiddenPlayer], [deadBot, hiddenBot, liveBot], EncounterTargets.AliveVisibleNotGm);
            Assert.That(pool, Is.EqualTo(new GameLiving[] { liveBot }));
            Assert.That(EncounterTargets.Pool(boss, [deadPlayer, hiddenPlayer], [deadBot, hiddenBot, liveBot], EncounterTargets.AliveNotGm),
                Is.EqualTo(new GameLiving[] { hiddenPlayer, hiddenBot, liveBot }), "stealth counts only where the script honoured it");
        }

        [Test]
        public void BotsOutOfTheWorldOnARideOrOfThePickersRealmAreNeverPicked()
        {
            var boss = NewBoss();
            var away = NewBot(); away.ObjectState = GameObject.eObjectState.Inactive;
            var riding = NewBot();
            typeof(GameBot).GetProperty(nameof(GameBot.IsOnStableMasterRoute))!.SetValue(riding, true);
            var ok = NewBot(eRealm.Hibernia);
            Assert.That(EncounterTargets.Pool(boss, [], [away, riding, ok]), Is.EqualTo(new GameLiving[] { ok }));
            var realmBoss = NewBoss(); realmBoss.TestRealm = eRealm.Hibernia;
            Assert.That(EncounterTargets.Pool(realmBoss, [], [ok]), Is.Empty, "a realm-aligned picker never picks its own realm's bot");
        }

        [TestCase(0)] [TestCase(1)] [TestCase(7)] [TestCase(23)]
        public void TheHumanIsNeverPickedMoreOftenThanBefore(int bots)
        {
            var boss = NewBoss();
            var human = NewPlayer();
            var raid = Enumerable.Range(0, bots).Select(_ => (GameNPC)NewBot()).ToList();
            var before = EncounterTargets.Pool(boss, [human], raid, EncounterTargets.AliveNotGm, bots: false);
            var after = EncounterTargets.Pool(boss, [human], raid, EncounterTargets.AliveNotGm);
            Assert.That(before, Is.EqualTo(new GameLiving[] { human }));
            Assert.That(after, Has.Count.EqualTo(bots + 1));
            const int trials = 20_000;
            int humanBefore = Enumerable.Range(0, trials).Count(_ => EncounterTargets.PickRandom(before) == human);
            int humanAfter = Enumerable.Range(0, trials).Count(_ => EncounterTargets.PickRandom(after) == human);
            Assert.That(humanBefore, Is.EqualTo(trials), "before: the human was every pick");
            Assert.That(humanAfter, Is.LessThanOrEqualTo(humanBefore));
            double share = humanAfter / (double)trials, fair = 1.0 / (bots + 1);
            Assert.That(share, Is.EqualTo(fair).Within(0.02), "after: one of many, a fair share");
            // Every bot is picked too, about as often as the human.
            if (bots > 0)
                Assert.That(Enumerable.Range(0, trials).Select(_ => EncounterTargets.PickRandom(after)).Distinct().Count(), Is.EqualTo(bots + 1));
        }

        [Test]
        public void RandomOtherAndRandomSomeKeepTheirRules()
        {
            var boss = NewBoss();
            var human = NewPlayer();
            var bots = Enumerable.Range(0, 5).Select(_ => NewBot()).ToList();
            var pool = EncounterTargets.Pool(boss, [human], bots);
            for (int i = 0; i < 500; i++)
            {
                var some = EncounterTargets.TakeRandom(pool, 3);
                Assert.That(some, Has.Count.EqualTo(3));
                Assert.That(some.Distinct().Count(), Is.EqualTo(3), "different raiders");
            }
            Assert.That(EncounterTargets.TakeRandom(pool, 10), Is.EquivalentTo(pool), "fewer than asked: everyone, once");
            Assert.That(EncounterTargets.TakeRandom([], 2), Is.Empty);
            Assert.That(EncounterTargets.PickRandom([]), Is.Null);
        }

        [Test]
        public void HelpersTellRaidersApart()
        {
            var human = NewPlayer();
            var gm = NewPlayer(ePrivLevel.GM);
            var bot = NewBot();
            var add = NewBoss();
            Assert.That((EncounterTargets.IsRaider(human), EncounterTargets.IsRaider(bot), EncounterTargets.IsRaider(add)), Is.EqualTo((true, true, false)));
            Assert.That((EncounterTargets.IsPlayer(human), EncounterTargets.IsPlayer(bot)), Is.EqualTo((true, false)));
            Assert.That(EncounterTargets.AsRaider(bot), Is.SameAs(bot));
            Assert.That(EncounterTargets.AsRaider(add), Is.Null);
            Assert.That((EncounterTargets.NotGm(human), EncounterTargets.NotGm(gm), EncounterTargets.NotGm(bot), EncounterTargets.NotGm(add)),
                Is.EqualTo((true, false, true, false)));
            Assert.That(EncounterTargets.ClassId(bot), Is.EqualTo((int)eCharacterClass.Warrior), "a bot's class, for class rules");
            Assert.That(EncounterTargets.ClassId(add), Is.Zero);
            Assert.DoesNotThrow(() => EncounterTargets.Tell(bot, "only players get this", DOL.GS.PacketHandler.eChatType.CT_Say,
                DOL.GS.PacketHandler.eChatLoc.CL_ChatWindow));
        }

        [Test]
        public void MovesPickBotsOnlyOnceBotsCanRecover()
        {
            Assert.That(EncounterTargets.BotsCanBeDisplaced, Is.False, "default: throws and teleports pick real players as before");
            Assert.That(EncounterTargets.ForDisplacement, Is.False);
            var boss = NewBoss();
            var human = NewPlayer();
            var bot = NewBot();
            Assert.That(EncounterTargets.Pool(boss, [human], [bot], bots: EncounterTargets.ForDisplacement), Is.EqualTo(new GameLiving[] { human }));
            EncounterTargets.BotsCanBeDisplaced = true;
            Assert.That(EncounterTargets.Pool(boss, [human], [bot], bots: EncounterTargets.ForDisplacement), Is.EqualTo(new GameLiving[] { human, bot }));
        }
    }
}
