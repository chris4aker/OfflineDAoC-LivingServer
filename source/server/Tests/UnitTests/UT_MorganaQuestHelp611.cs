using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #611 (owner decision 2026-11-25): bots help a player through the Albion level 50 epic quest fight. When the quest
    /// player's event starts (MorganaBrain spawns Bechard and Silcharde), the bots in that player's own group, companions and
    /// grouped gamebots alike, engage a demon; solo gamebots and other groups don't; bots never take Morgana as a target.
    /// Switch bot_morgana_quest_help, on by default. Tests only until seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_MorganaQuestHelp611
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private bool _previousSwitch;
        private readonly List<GameLiving> _actors = new();

        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
                attacker.Realm != defender.Realm;
        }
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new Rules();
        }
        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public bool Attacking;
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => true;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override GameObject TargetObject { get; set; }
            public override IControlledBrain ControlledBrain { get; set; }
            public override IPacketLib Out => new BotDummyPacketLib();
        }
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public int TestX;
            public byte TestLevel = 50;
            public override bool IsAlive => true;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override short MaxSpeed => 200;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override int EffectiveLevel => TestLevel;
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
        /// <summary>Bechard as MorganaBrain spawns him, without a world.</summary>
        private sealed class TestBechard : Bechard
        {
            public int TestX = 400;
            public bool Alive = true;
            public override bool IsAlive => Alive;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.None; set { } }
            public override GameObject TargetObject { get; set; }
            public override byte Level { get => 65; set { } }
            public override int EffectiveLevel => 65;
        }
        private sealed class TestSilcharde : Silcharde
        {
            public int TestX = 900;
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.None; set { } }
            public override GameObject TargetObject { get; set; }
            public override byte Level { get => 65; set { } }
            public override int EffectiveLevel => 65;
        }
        private sealed class TestMorgana : Morgana
        {
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => 200;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.None; set { } }
            public override GameObject TargetObject { get; set; }
            public override byte Level { get => 90; set { } }
            public override int EffectiveLevel => 90;
        }
        private sealed class Brain : BotBrain
        {
            // The pull itself is real (OrderPull, the aggro list); only the swing is left out (no world to swing in).
            public override void AttackMostWanted() { }
        }

        [SetUp] public void Setup()
        {
            _previous = GameServer.Instance;
            _previousSwitch = Properties.BOT_MORGANA_QUEST_HELP;
            Properties.BOT_MORGANA_QUEST_HELP = true;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }
        [TearDown] public void Cleanup()
        {
            foreach (GameLiving actor in _actors) ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            Properties.BOT_MORGANA_QUEST_HELP = _previousSwitch;
            GameServer.LoadTestDouble(_previous);
        }

        [Test] public void SwitchIsOnByDefault()
        {
            FieldInfo field = typeof(Properties).GetField(nameof(Properties.BOT_MORGANA_QUEST_HELP));
            var attribute = field.GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That(attribute.Key, Is.EqualTo("bot_morgana_quest_help"));
            Assert.That(attribute.DefaultValue, Is.EqualTo(true));
        }

        [Test] public void QuestPlayersGroupBotsEngageTheDemonsWhenTheEventStarts()
        {
            var (player, companion, gamebot) = Party();
            Bot solo = MakeBot(new ClassArmsman());
            var (_, otherGroupBot, _) = Party();
            TestBechard bechard = Actor<TestBechard>();
            TestSilcharde silcharde = Actor<TestSilcharde>();

            int asked = MorganaQuestHelp.OnEventStarted(player, new GameNPC[] { bechard, silcharde });

            Assert.That(asked, Is.EqualTo(2), "the companion and the grouped gamebot");
            Assert.That(B(companion).HasAggro, Is.True, "the /spawn companion fights");
            Assert.That(B(gamebot).HasAggro, Is.True, "the gamebot grouped with the player fights");
            Assert.That(B(companion).GetBaseAggroAmount(bechard), Is.GreaterThan(0), "the demon nearest the player first");
            Assert.That(B(gamebot).GetBaseAggroAmount(bechard), Is.GreaterThan(0));
            Assert.That(B(companion).GetBaseAggroAmount(silcharde) + B(gamebot).GetBaseAggroAmount(silcharde), Is.Zero,
                "the second demon comes in through its link, not a second order");
            Assert.That(B(solo).HasAggro, Is.False, "a solo gamebot is no part of it");
            Assert.That(B(otherGroupBot).HasAggro, Is.False, "nor a bot in another player's group");
        }

        [Test] public void TheNearerLiveDemonIsTheFirst()
        {
            Player player = Actor<Player>();
            TestBechard bechard = Actor<TestBechard>();
            TestSilcharde silcharde = Actor<TestSilcharde>();
            silcharde.TestX = 100;
            Assert.That(MorganaQuestHelp.FirstDemon(player, new GameNPC[] { bechard, silcharde }), Is.SameAs(silcharde));
            silcharde.TestX = 900;
            Assert.That(MorganaQuestHelp.FirstDemon(player, new GameNPC[] { bechard, silcharde }), Is.SameAs(bechard));
            bechard.Alive = false;
            Assert.That(MorganaQuestHelp.FirstDemon(player, new GameNPC[] { bechard, silcharde }), Is.SameAs(silcharde));
            Assert.That(MorganaQuestHelp.FirstDemon(player, new GameNPC[] { Actor<TestMorgana>(), null }), Is.Null,
                "Morgana is never one of the demons");
        }

        [Test] public void NothingWithTheSwitchOffOrWithoutAQuestPlayer()
        {
            var (player, companion, gamebot) = Party();
            TestBechard bechard = Actor<TestBechard>();
            Assert.That(MorganaQuestHelp.OnEventStarted(null, new GameNPC[] { bechard }), Is.Zero);
            Properties.BOT_MORGANA_QUEST_HELP = false;
            Assert.That(MorganaQuestHelp.OnEventStarted(player, new GameNPC[] { bechard }), Is.Zero);
            Assert.That(B(companion).HasAggro || B(gamebot).HasAggro, Is.False);
        }

        [Test] public void AFarBotStaysOut()
        {
            var (player, companion, gamebot) = Party();
            gamebot.TestX = MorganaQuestHelp.HelpRadius + 500;
            MorganaQuestHelp.OnEventStarted(player, new GameNPC[] { Actor<TestBechard>() });
            Assert.That(B(companion).HasAggro, Is.True);
            Assert.That(B(gamebot).HasAggro, Is.False, "beyond the group-defense radius");
        }

        [Test] public void BotsNeverAttackMorgana()
        {
            var (player, companion, gamebot) = Party();
            TestMorgana morgana = Actor<TestMorgana>();
            Assert.That(MorganaQuestHelp.IsMorgana(morgana), Is.True);

            // not even when the player attacks her: no assist, no order, no defense, no aggro
            player.TargetObject = morgana;
            player.Attacking = true;
            foreach (Bot bot in new[] { companion, gamebot })
            {
                Assert.That(B(bot).CanAggroTarget(morgana), Is.False);
                B(bot).AssistPlayerAttack(morgana);
                Assert.That(B(bot).OrderPull(morgana), Is.False);
                B(bot).AddToAggroList(morgana, 1000);
                B(bot).ForceAddToAggroList(morgana, 1000);
                Assert.That(B(bot).HasAggro, Is.False, bot.Name);
            }
            // and the event's own call never hands her over
            MorganaQuestHelp.OnEventStarted(player, new GameNPC[] { morgana });
            Assert.That(B(companion).HasAggro || B(gamebot).HasAggro, Is.False);
        }

        /// <summary>The quest player, a /spawn companion and an autonomous gamebot, grouped and player-led.</summary>
        private (Player, Bot, Bot) Party()
        {
            Player player = Actor<Player>();
            var group = new Group(player);
            Add(group, player);
            Bot companion = MakeBot(new ClassPaladin()), gamebot = MakeBot(new ClassArmsman());
            gamebot.TestX = 50;
            foreach (Bot bot in new[] { companion, gamebot }) { Add(group, bot); bot.EnterPlayerLedGroup(player); }
            Field(typeof(GameBot), companion, "<IsTemporaryGroupHelper>k__BackingField", true);
            Field(typeof(GameBot), companion, "<Owner>k__BackingField", player);
            Field(typeof(GameBot), gamebot, "<IsAutonomousWorldBot>k__BackingField", true);
            return (player, companion, gamebot);
        }
        private Bot MakeBot(ICharacterClass characterClass)
        {
            Bot bot = Actor<Bot>();
            bot.TestLevel = 50;
            bot.Name = characterClass.Name;
            bot.TestClass = characterClass;
            var brain = new Brain { Body = bot };
            Field(typeof(GameNPC), bot, "m_ownBrain", brain);
            return bot;
        }
        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }
        private static Brain B(Bot bot) => (Brain)bot.Brain;
        private static List<GameLiving> Members(Group group) => (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
        private static void Add(Group group, GameLiving member) { Members(group).Add(member); member.Group = group; }
        private static void Field(Type type, object owner, string name, object value) => type.GetField(name, Hidden).SetValue(owner, value);
    }
}
