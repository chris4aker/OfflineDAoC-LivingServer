using System.Collections.Generic;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #571 step 1, leader focus, through the real BotBrain.AttackMostWanted (the #751 review): real gamebots in the fight
    /// harness (RvrFightWorld) with a stand-in player, in a battleground (Thidranki, region 252) and in a plain PvE region.
    /// The companion is a befriended gamebot in the player's group (one of the bot types leader focus covers). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CompanionLeaderFocus571Harness
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort Battleground = 252, PveRegion = 571;
        private const int X = 30_000, Y = 30_000;

        /// <summary>Behemoth's own rule (EpicEncounterTactics.Legion: MustNotHit always); no template, so not put in the world.</summary>
        private sealed class TestBehemoth : Behemoth
        {
            public override bool IsAlive => true;
        }

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        private sealed class Scene
        {
            public RvrFightWorld World;
            public Region Region;
            public StandInPlayer Player;
            public GameBot Subject;
            public GameNPC Attacker;
            public GameNPC LeadersTarget;
        }

        private static GameNPC Monster(RvrFightWorld world, Region region, string name, int x, int y)
        {
            var npc = new GameNPC { Name = name, Level = 30, Realm = eRealm.None };
            Assert.That(world.Place(npc, region, x, y), Is.True, name + " placed");
            return npc;
        }

        /// <summary>
        /// A damage dealer (Mercenary) hit by monster A a moment ago, while the player fights B. With
        /// <paramref name="playerLed"/> it is in the player's group; otherwise in a bot-only group with another gamebot.
        /// </summary>
        private static Scene Make(ushort regionId, bool playerLed, GameNPC leadersTarget = null)
        {
            var world = new RvrFightWorld(571, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(regionId, regionId, "#571 flat field (rehearsal)");
            var scene = new Scene { World = world, Region = region };
            scene.Subject = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 30, "Subject", "GroupPve");
            Assert.That(world.Place(scene.Subject, region, X, Y), Is.True, "subject placed");
            scene.Attacker = Monster(world, region, "its attacker", X + 150, Y);
            if (leadersTarget != null)
            {
                leadersTarget.CurrentRegion = region;
                leadersTarget.X = X + 400;
                leadersTarget.Y = Y;
                leadersTarget.Z = 0;
                leadersTarget.Realm = eRealm.None;
                leadersTarget.Level = 30;
                leadersTarget.ObjectState = GameObject.eObjectState.Active;
                scene.LeadersTarget = leadersTarget;
            }
            else
                scene.LeadersTarget = Monster(world, region, "the leader's target", X + 400, Y);

            if (playerLed)
            {
                scene.Player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 30, "Leader");
                Assert.That(scene.Player.Enter(region, X - 100, Y), Is.True, "the player entered");
                world.Track(scene.Player);
                var group = new Group(scene.Player);
                var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
                members.Add(scene.Player);
                scene.Player.Group = group;
                members.Add(scene.Subject);
                scene.Subject.Group = group;
                Assert.That(scene.Subject.EnterPlayerLedGroup(scene.Player), Is.True, "the gamebot joins the player's group");
            }
            else
            {
                GameBot mate = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 30, "Mate", "GroupPve");
                Assert.That(world.Place(mate, region, X + 50, Y), Is.True, "mate placed");
                world.Form(new List<GameLiving> { mate, scene.Subject });
            }
            world.Tick(); // the zones file what was placed

            var brain = (BotBrain)scene.Subject.Brain;
            // A hit the subject a moment ago (what OnAttackedByEnemy records), and is on its list.
            typeof(BotBrain).GetField("_recentDirectAttacker", Hidden).SetValue(brain, scene.Attacker);
            typeof(BotBrain).GetField("_recentDirectAttackTick", Hidden).SetValue(brain, world.Now);
            brain.ForceAddToAggroList(scene.Attacker, 500);
            if (scene.Player != null)
            {
                // the player swings at B
                scene.Player.TargetObject = scene.LeadersTarget;
                scene.Player.attackComponent.AttackState = true;
            }
            return scene;
        }

        private static GameObject Choice(Scene scene)
        {
            ((BotBrain)scene.Subject.Brain).AttackMostWanted();
            return scene.Subject.TargetObject;
        }

        [Test]
        public void InABattlegroundAPlayerLedCompanionHitByAEndsOnThePlayersTargetB()
        {
            Scene scene = Make(Battleground, playerLed: true);
            using (scene.World)
            {
                Assert.That(PlayerLedPullCoordinator.FindLeaderTarget(scene.Player), Is.SameAs(scene.LeadersTarget), "the player fights B");
                Assert.That(Choice(scene), Is.SameAs(scene.LeadersTarget));
            }
        }

        [Test]
        public void AnAutonomousGroupHitByAEndsOnA()
        {
            Scene scene = Make(Battleground, playerLed: false);
            using (scene.World)
                Assert.That(Choice(scene), Is.SameAs(scene.Attacker), "no player: the choice is as before");
        }

        [Test]
        public void WithAMustNotHitTargetTheCompanionStaysOnA()
        {
            Scene scene = Make(Battleground, playerLed: true, new TestBehemoth { Name = "Behemoth" });
            using (scene.World)
            {
                Assert.That(EpicEncounterTactics.MustNotHit(scene.Subject, scene.LeadersTarget), Is.True, "Behemoth's rule");
                Assert.That(Choice(scene), Is.SameAs(scene.Attacker), "it does not idle on Behemoth while A hits it");
            }
        }

        [Test]
        public void InPveThePlayerLedCompanionStillEndsOnAUntilTheOwnerDecides()
        {
            Assert.That(CompanionLeaderFocus.InPve, Is.False);
            Scene scene = Make(PveRegion, playerLed: true);
            using (scene.World)
            {
                Assert.That(PlayerLedPullCoordinator.FindLeaderTarget(scene.Player), Is.SameAs(scene.LeadersTarget), "the player fights B");
                Assert.That(Choice(scene), Is.SameAs(scene.Attacker), "PvE stays as it is (#663)");
            }
        }
    }
}
