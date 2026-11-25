using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The verified part of OpenDAoC-Core's NPC defense package (5090fa4fc): a monster's second attack per round comes
    /// from its LeftHandSwingChance, not from an offhand item (Prima, monster tables: "Secondary Attack Chance. This is
    /// the percent chance (per round) that a Monster will make two attacks"). Gamebots, companions and pets keep the
    /// item rule. The rest of the package (ecaf945d0, 8721f20a1, 9c161ae20, ab90dbf12 and the rest of 5090fa4fc) is
    /// not ported; see the PR's verdict table.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_NpcSecondaryAttack
    {
        private sealed class Server : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Empty;
        }

        private static readonly IObjectDatabase Empty =
            DispatchProxy.Create<IObjectDatabase, UT_AutonomousBotRealmPointRewards.EmptyReadDatabase>();

        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void TearDown() => GameServer.LoadTestDouble(_previous);

        private static void SetField(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        private static GameNPC Npc(ABrain brain, byte leftHandSwingChance)
        {
            GameNPC npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            SetField(typeof(GameNPC), npc, "m_brains", new ArrayList());
            brain.Body = npc;
            SetField(typeof(GameNPC), npc, "m_ownBrain", brain);
            npc.LeftHandSwingChance = leftHandSwingChance;
            return npc;
        }

        private static AttackComponent Attack(GameLiving owner)
        {
            AttackComponent component = (AttackComponent)RuntimeHelpers.GetUninitializedObject(typeof(AttackComponent));
            component.owner = owner;
            return component;
        }

        private static DbInventoryItem Item(eObjectType type, int slot)
        {
            DbInventoryItem item = GameInventoryItem.Create(new DbItemTemplate { Name = "test", Object_Type = (int)type, Item_Type = slot });
            item.SlotPosition = slot;
            return item;
        }

        [Test]
        public void MonsterWithSecondaryAttackChanceSwingsTwiceWithoutAnOffhandItem()
        {
            GameNPC monster = Npc(new StandardMobBrain(), 100);

            Assert.That(AttackComponent.MakesSecondaryAttackWithoutOffhandItem(monster), Is.True);
            Assert.That(Attack(monster).CalculateLeftHandSwingCount(null, null), Is.EqualTo(1), "an animal: no items at all");
            Assert.That(Attack(monster).CalculateLeftHandSwingCount(Item(eObjectType.TwoHandedWeapon, Slot.TWOHAND), null),
                Is.EqualTo(1), "a two-handed main weapon no longer blocks the monster's second attack");
            Assert.That(Attack(monster).CalculateLeftHandSwingCount(Item(eObjectType.Sword, Slot.RIGHTHAND), Item(eObjectType.Shield, Slot.LEFTHAND)),
                Is.EqualTo(1), "the second attack is the monster's stat, whatever it holds");
        }

        [Test]
        public void MonsterWithoutSecondaryAttackChanceNeverSwingsTwice()
        {
            GameNPC monster = Npc(new StandardMobBrain(), 0);

            Assert.That(Attack(monster).CalculateLeftHandSwingCount(null, null), Is.EqualTo(0));
            Assert.That(Attack(monster).CalculateLeftHandSwingCount(Item(eObjectType.Sword, Slot.RIGHTHAND), Item(eObjectType.Sword, Slot.LEFTHAND)),
                Is.EqualTo(0), "an offhand item alone gives no second attack");
        }

        [Test]
        public void PetsKeepTheOffhandItemRule()
        {
            GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            GameNPC pet = Npc(new ControlledMobBrain(owner), 100);

            Assert.That(AttackComponent.MakesSecondaryAttackWithoutOffhandItem(pet), Is.False);
            Assert.That(Attack(pet).CalculateLeftHandSwingCount(null, null), Is.EqualTo(0), "no offhand item, no second swing");
            Assert.That(Attack(pet).CalculateLeftHandSwingCount(Item(eObjectType.Sword, Slot.RIGHTHAND), Item(eObjectType.Sword, Slot.LEFTHAND)),
                Is.EqualTo(1), "a pet with a real offhand weapon swings as before");
        }

        [Test]
        public void GamebotsAndCompanionsAreNotMonsters()
        {
            GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());

            Assert.That(AttackComponent.MakesSecondaryAttackWithoutOffhandItem(bot), Is.False);
            Assert.That(AttackComponent.MakesSecondaryAttackWithoutOffhandItem(
                (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer))), Is.False);
        }
    }
}
