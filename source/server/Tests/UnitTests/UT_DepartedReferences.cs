using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Live run 2026-10-22: deleted bots stayed in memory, held by monsters' last interrupter and remembered attack targets
    /// (and deleted monsters by the same fields of bots). <see cref="GameLiving.ForgetDeparted"/> drops those references.
    /// </summary>
    [TestFixture]
    public class UT_DepartedReferences
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Game objects need a server with a database for their static setup (same double as UT_AutonomousBotRealmPointRewards).
        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();
        }

        public class EmptyReadDatabase : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        private GameServer _previousServer;

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
        }

        [TearDown]
        public void TearDown() => GameServer.LoadTestDouble(_previousServer);

        private static T Bare<T>(GameObject.eObjectState state) where T : GameObject
        {
            var obj = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            obj.ObjectState = state;
            if (obj is GameNPC)
                Field(typeof(GameNPC), "m_brains").SetValue(obj, new ArrayList());
            return obj;
        }

        private static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (t.GetField(name, Any | BindingFlags.DeclaredOnly) is FieldInfo field)
                    return field;
            return null;
        }

        [Test]
        public void OnlyAnObjectThatLeftTheWorldHasDeparted() => Assert.Multiple(() =>
        {
            Assert.That(GameLiving.HasDeparted(null), Is.False);
            Assert.That(GameLiving.HasDeparted(Bare<GameNPC>(GameObject.eObjectState.Active)), Is.False);
            Assert.That(GameLiving.HasDeparted(Bare<GameNPC>(GameObject.eObjectState.Deleted)), Is.True);
            Assert.That(GameLiving.HasDeparted(Bare<GameNPC>(GameObject.eObjectState.Inactive)), Is.True);
        });

        [Test]
        public void AMonsterForgetsADeletedBotItInterruptedOrAttacked()
        {
            GameNPC monster = Bare<GameNPC>(GameObject.eObjectState.Active);
            GameBot deleted = Bare<GameBot>(GameObject.eObjectState.Deleted);
            GameBot alive = Bare<GameBot>(GameObject.eObjectState.Active);
            Field(typeof(GameLiving), "XpGainersLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameLiving), "m_xpGainers").SetValue(monster, new Dictionary<GameLiving, double> { [deleted] = 10, [alive] = 5 });
            Field(typeof(GameObject), "_objectInRadiusCachesLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameObject), "_objectsInRadiusCaches").SetValue(monster, Field(typeof(GameObject), "_objectsInRadiusCaches").FieldType
                .GetConstructor(Type.EmptyTypes).Invoke(null));
            typeof(GameLiving).GetProperty("LastInterrupter").SetValue(monster, deleted);
            var action = (NpcAttackAction)RuntimeHelpers.GetUninitializedObject(typeof(NpcAttackAction));
            Field(typeof(AttackAction), "_target").SetValue(action, deleted);
            Field(typeof(NpcAttackAction), "_losCheckTarget").SetValue(action, deleted);

            monster.ForgetDeparted();
            action.ForgetDeparted();

            Assert.Multiple(() =>
            {
                Assert.That(monster.LastInterrupter, Is.Null);
                Assert.That(monster.XPGainers.Keys, Is.EquivalentTo(new GameLiving[] { deleted, alive }),
                    "a monster's damage credit decides XP and loot: it is never pruned");
                Assert.That(Field(typeof(AttackAction), "_target").GetValue(action), Is.Null);
                Assert.That(Field(typeof(NpcAttackAction), "_losCheckTarget").GetValue(action), Is.Null);
            });
        }

        [Test]
        public void ABotForgetsTheDamageOfDeletedMonstersOnly()
        {
            GameBot bot = Bare<GameBot>(GameObject.eObjectState.Active);
            GameNPC deletedMonster = Bare<GameNPC>(GameObject.eObjectState.Deleted);
            GameBot deletedBot = Bare<GameBot>(GameObject.eObjectState.Deleted);
            GameNPC liveMonster = Bare<GameNPC>(GameObject.eObjectState.Active);
            Field(typeof(GameLiving), "XpGainersLock").SetValue(bot, new System.Threading.Lock());
            Field(typeof(GameLiving), "m_xpGainers").SetValue(bot, new Dictionary<GameLiving, double>
                { [deletedMonster] = 10, [deletedBot] = 20, [liveMonster] = 5 });
            Field(typeof(GameObject), "_objectInRadiusCachesLock").SetValue(bot, new System.Threading.Lock());
            Field(typeof(GameObject), "_objectsInRadiusCaches").SetValue(bot, Field(typeof(GameObject), "_objectsInRadiusCaches").FieldType
                .GetConstructor(Type.EmptyTypes).Invoke(null));

            bot.ForgetDeparted();

            Assert.That(bot.XPGainers.Keys, Is.EquivalentTo(new GameLiving[] { deletedBot, liveMonster }),
                "a bot or player that hit it can still be owed realm points");
        }

        [Test]
        public void AMonsterStillInTheWorldIsKept()
        {
            GameNPC monster = Bare<GameNPC>(GameObject.eObjectState.Active);
            GameNPC other = Bare<GameNPC>(GameObject.eObjectState.Active);
            Field(typeof(GameLiving), "XpGainersLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameLiving), "m_xpGainers").SetValue(monster, new Dictionary<GameLiving, double>());
            Field(typeof(GameObject), "_objectInRadiusCachesLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameObject), "_objectsInRadiusCaches").SetValue(monster, Field(typeof(GameObject), "_objectsInRadiusCaches").FieldType
                .GetConstructor(Type.EmptyTypes).Invoke(null));
            typeof(GameLiving).GetProperty("LastInterrupter").SetValue(monster, other);
            typeof(GameLiving).GetProperty("InterruptTime").SetValue(monster, GameLoop.GameLoopTime + 60_000);

            monster.ForgetDeparted();

            Assert.That(monster.LastInterrupter, Is.SameAs(other), "an interrupt still running keeps its interrupter");
        }

        /// <summary>Port of OpenDAoC-Core 08c896ac0: an expired interrupt no longer holds its interrupter.</summary>
        [Test]
        public void AnExpiredInterruptForgetsItsInterrupter()
        {
            GameNPC monster = Bare<GameNPC>(GameObject.eObjectState.Active);
            GameNPC other = Bare<GameNPC>(GameObject.eObjectState.Active);
            Field(typeof(GameLiving), "XpGainersLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameLiving), "m_xpGainers").SetValue(monster, new Dictionary<GameLiving, double>());
            Field(typeof(GameObject), "_objectInRadiusCachesLock").SetValue(monster, new System.Threading.Lock());
            Field(typeof(GameObject), "_objectsInRadiusCaches").SetValue(monster, Field(typeof(GameObject), "_objectsInRadiusCaches").FieldType
                .GetConstructor(Type.EmptyTypes).Invoke(null));
            typeof(GameLiving).GetProperty("LastInterrupter").SetValue(monster, other);
            typeof(GameLiving).GetProperty("InterruptTime").SetValue(monster, GameLoop.GameLoopTime - 1);

            monster.ForgetDeparted();

            Assert.That(monster.LastInterrupter, Is.Null);
        }

        [Test]
        public void APetForgetsTheBrainsOfMonstersThatLeft()
        {
            GameNPC pet = Bare<GameNPC>(GameObject.eObjectState.Active);
            Field(typeof(GameLiving), "XpGainersLock").SetValue(pet, new System.Threading.Lock());
            Field(typeof(GameLiving), "m_xpGainers").SetValue(pet, new Dictionary<GameLiving, double>());
            Field(typeof(GameObject), "_objectInRadiusCachesLock").SetValue(pet, new System.Threading.Lock());
            Field(typeof(GameObject), "_objectsInRadiusCaches").SetValue(pet, Field(typeof(GameObject), "_objectsInRadiusCaches").FieldType
                .GetConstructor(Type.EmptyTypes).Invoke(null));
            StandardMobBrain Brain(GameObject.eObjectState state)
            {
                var brain = (StandardMobBrain)RuntimeHelpers.GetUninitializedObject(typeof(StandardMobBrain));
                brain.Body = Bare<GameNPC>(state);
                return brain;
            }
            StandardMobBrain gone = Brain(GameObject.eObjectState.Deleted), here = Brain(GameObject.eObjectState.Active);
            var holders = new ConcurrentDictionary<StandardMobBrain, byte>();
            holders[gone] = 0;
            holders[here] = 0;
            Field(typeof(GameNPC), "_aggroHolders").SetValue(pet, holders);

            pet.ForgetDeparted();

            Assert.That(holders.Keys, Is.EquivalentTo(new[] { here }));
        }
    }
}
