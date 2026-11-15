using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.DailyQuest.Albion;
using DOL.GS.Quests;
using DOL.UnitTests.QuestHarness;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// RvR and battleground kill quests count enemy-realm gamebots (1.2, 2026-11-15). Offline DAoC's enemy players are
    /// autonomous gamebots; Pazz's battleground dailies and the Atlas realm quests counted only a GamePlayer victim,
    /// so none of them could be finished. RvrQuestTargets.IsEnemyPlayer is the one rule all 27 quests now read.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RvrQuestTargets
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Dying = typeof(GameLiving).GetField("_isBeingHandledByReaperService", Hidden);

        private QuestHarness.QuestHarness _harness;

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
        }

        private QuestHarness.QuestHarness Harness() => _harness = new QuestHarness.QuestHarness(new QuestWorld());

        /// <summary>A gamebot standing by the player, as GameLiving.ProcessDeath sees it: dying unless told otherwise.</summary>
        private sealed class EnemyBot : GameBot
        {
            private EnemyBot() : base((OfflineWorldBotRecord)null) { }
            private eRealm _realm;
            private ushort _region;
            private int _x, _y;
            private byte _level;

            public static EnemyBot Make(GamePlayer near, eRealm realm, byte level = 22, bool dying = true)
            {
                var bot = (EnemyBot)RuntimeHelpers.GetUninitializedObject(typeof(EnemyBot));
                Fields.Fill(bot);
                bot._realm = realm;
                bot._region = near.CurrentRegionID;
                bot._x = near.X + 100;
                bot._y = near.Y;
                bot._level = level;
                bot.ObjectState = eObjectState.Active;
                // GamePlayer.EnemyKilled reads the victim's attacker tracker for the killer's groupmates.
                var attack = (AttackComponent)RuntimeHelpers.GetUninitializedObject(typeof(AttackComponent));
                var tracker = (AttackerTracker)RuntimeHelpers.GetUninitializedObject(typeof(AttackerTracker));
                Fields.Fill(tracker);
                typeof(AttackComponent).GetField("<AttackerTracker>k__BackingField", Hidden).SetValue(attack, tracker);
                bot.attackComponent = attack;
                Dying.SetValue(bot, dying);
                return bot;
            }

            public override string Name { get => "Enemy gamebot"; set { } }
            public override ushort CurrentRegionID { get => _region; set => _region = value; }
            public override int X { get => _x; set => _x = value; }
            public override int Y { get => _y; set => _y = value; }
            public override int Z { get => 0; set { } }
            public override eRealm Realm { get => _realm; set => _realm = value; }
            public override byte Level { get => _level; set => _level = value; }
            public override int EffectiveLevel => _level;
        }

        // The helper

        [Test]
        public void AnEnemyRealmPlayerCountsAsBefore()
        {
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            HarnessPlayer enemy = harness.NewPlayer(QuestWalker.DefaultClass, 22, realm: eRealm.Midgard, name: "Enemy", primary: false);
            HarnessPlayer friend = harness.NewPlayer(QuestWalker.DefaultClass, 22, name: "Friend", primary: false);
            Assert.Multiple(() =>
            {
                Assert.That(RvrQuestTargets.IsEnemyPlayer(enemy, player), Is.True);
                Assert.That(RvrQuestTargets.IsEnemyPlayer(friend, player), Is.False, "a player of the quest player's realm (a duel)");
            });
        }

        [Test]
        public void AnEnemyRealmGamebotCounts()
        {
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            Assert.Multiple(() =>
            {
                Assert.That(RvrQuestTargets.IsEnemyPlayer(EnemyBot.Make(player, eRealm.Midgard), player), Is.True);
                Assert.That(RvrQuestTargets.IsEnemyPlayer(EnemyBot.Make(player, eRealm.Hibernia), player), Is.True);
            });
        }

        [Test]
        public void ASameRealmGamebotDoesNotCount()
        {
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            Assert.That(RvrQuestTargets.IsEnemyPlayer(EnemyBot.Make(player, eRealm.Albion), player), Is.False);
        }

        [Test]
        public void AMonsterOrAnEnemyRealmNpcDoesNotCount()
        {
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            Assert.Multiple(() =>
            {
                Assert.That(RvrQuestTargets.IsEnemyPlayer(harness.Monster("rabid badger", level: 22), player), Is.False, "a monster (realm none)");
                Assert.That(RvrQuestTargets.IsEnemyPlayer(HarnessNpc.Make("Midgard guard", player.CurrentRegionID, eRealm.Midgard), player), Is.False,
                    "an enemy-realm NPC (a guard) never counted");
                Assert.That(RvrQuestTargets.IsEnemyPlayer(null, player), Is.False);
            });
        }

        [Test]
        public void AGamebotsPetDoesNotCount()
        {
            // A bot's pet is a GameNPC of the bot's realm, as a player's pet is, and the originals never counted a player's pet.
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            EnemyBot bot = EnemyBot.Make(player, eRealm.Midgard);
            HarnessNpc pet = HarnessNpc.Make("spirit warrior", player.CurrentRegionID, eRealm.Midgard);
            var brain = new DOL.AI.Brain.ControlledMobBrain(bot) { Body = pet };
            typeof(GameNPC).GetField("m_ownBrain", Hidden).SetValue(pet, brain);
            Assert.Multiple(() =>
            {
                Assert.That(((DOL.AI.Brain.IControlledBrain)pet.Brain).Owner, Is.SameAs(bot), "the pet is the bot's");
                Assert.That(RvrQuestTargets.IsEnemyPlayer(pet, player), Is.False);
            });
        }

        [Test]
        public void AGamebotThatLeftTheWorldWithoutDyingDoesNotCount()
        {
            // GameLiving.RemoveFromWorld tells every attacker "EnemyKilled": a bot riding off on a stablemaster route,
            // porting home or despawned mid-fight is not a kill.
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            Assert.That(RvrQuestTargets.IsEnemyPlayer(EnemyBot.Make(player, eRealm.Midgard, dying: false), player), Is.False);
        }

        // One quest's Notify path: Pazz's "[Daily] Fen's New Friends" in Thidranki (ThidKillQuestAlb)

        private static int PlayersKilled(ThidKillQuestAlb quest) =>
            (int)typeof(ThidKillQuestAlb).GetField("PlayersKilled", Hidden).GetValue(quest);

        /// <summary>The kill as GameLiving.ProcessDeath gives it out: the credit rule, then each credited player's EnemyKilled.</summary>
        private static List<GamePlayer> Kill(GameLiving victim, params GameLiving[] attackers)
        {
            List<GamePlayer> credited = QuestKillCredit.PlayersCredited(victim, attackers,
                attacker => System.Array.IndexOf(attackers, attacker) >= 0, new List<GameLiving>());
            foreach (GamePlayer credit in credited)
                credit.EnemyKilled(victim);
            return credited;
        }

        private static ThidKillQuestAlb TakeQuest(HarnessPlayer player)
        {
            var quest = new ThidKillQuestAlb(player, 1);
            Assert.That(player.AddQuest(quest), Is.True);
            return quest;
        }

        [Test]
        public void TenEnemyGamebotKillsFinishThidrankisKillQuest()
        {
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            ThidKillQuestAlb quest = TakeQuest(player);

            Kill(EnemyBot.Make(player, eRealm.Albion), player);
            Kill(EnemyBot.Make(player, eRealm.Midgard, dying: false), player);
            Kill(harness.Monster("rabid badger", level: 22), player);
            Assert.That(PlayersKilled(quest), Is.Zero, "a same-realm bot, a bot that left the world and a monster");

            for (int kill = 1; kill <= 9; kill++)
                Kill(EnemyBot.Make(player, kill % 2 == 0 ? eRealm.Hibernia : eRealm.Midgard), player);
            Assert.Multiple(() =>
            {
                Assert.That(PlayersKilled(quest), Is.EqualTo(9));
                Assert.That(quest.Step, Is.EqualTo(1));
            });

            Kill(EnemyBot.Make(player, eRealm.Midgard), player);
            Assert.Multiple(() =>
            {
                Assert.That(PlayersKilled(quest), Is.EqualTo(10));
                Assert.That(quest.Step, Is.EqualTo(2), "back to Pazz for the reward");
            });
        }

        [Test]
        public void AGroupmatesGamebotKillCountsOnceAsAPlayerKillWould()
        {
            // The credit rule is the victim-blind ProcessDeath one: a groupmate's kill in range counts for the quest
            // player, once, whether the victim is a player or a gamebot.
            QuestHarness.QuestHarness harness = Harness();
            HarnessPlayer player = harness.NewPlayer(QuestWalker.DefaultClass, 22);
            HarnessPlayer friend = harness.NewPlayer(QuestWalker.DefaultClass, 22, name: "Friend", primary: false);
            HarnessGroups.Of(player, friend);
            ThidKillQuestAlb quest = TakeQuest(player);

            List<GamePlayer> credited = Kill(EnemyBot.Make(player, eRealm.Midgard), friend);
            Assert.Multiple(() =>
            {
                Assert.That(credited, Does.Contain(player));
                Assert.That(PlayersKilled(quest), Is.EqualTo(1));
            });
        }
    }
}
