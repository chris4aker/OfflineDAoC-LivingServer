using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #667 step 1 through the real gates, in the fight harness (RvrFightWorld, #520 / #605), on #664's scene: a stand-in
    /// player and real gamebots befriended into his group, in Thidranki (region 252), against enemy Midgard gamebots that
    /// only stand there (brains stopped). The perception rolls are certain (CompanionRvrEngage.Roll = 0). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_GroupOrders667Harness
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort Thidranki = 252;
        private const int X = 30_000, Y = 30_000;
        private static readonly eCharacterClass[] Classes =
        [
            eCharacterClass.Mercenary, eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Armsman,
        ];

        private Func<double> _roll;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void CertainRolls()
        {
            _roll = CompanionRvrEngage.Roll;
            CompanionRvrEngage.Roll = () => 0.0;
        }

        [TearDown]
        public void RestoreRolls() => CompanionRvrEngage.Roll = _roll;

        private sealed class Scene : IDisposable
        {
            public RvrFightWorld World;
            public Region Region;
            public StandInPlayer Player;
            public readonly List<GameBot> Group = new();
            public GameBot Enemy;
            public void Dispose() => World.Dispose();
        }

        private static Scene Make(int enemyDistance, bool defensive = false, bool switchOn = true, int companions = 3)
        {
            var settings = switchOn ? null : new Dictionary<string, object> { [PlayerGroupOrders.PropertyKey] = false };
            var scene = new Scene { World = new RvrFightWorld(667, settings) };
            RvrFightWorld world = scene.World;
            Region region = world.AddRegion(Thidranki, Thidranki, "#667 flat field (rehearsal)");
            scene.Region = region;
            scene.Player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 22, "Leader");
            Assert.That(scene.Player.Enter(region, X, Y), Is.True, "the player entered");
            world.Track(scene.Player);
            var group = new Group(scene.Player);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
            members.Add(scene.Player);
            scene.Player.Group = group;
            for (int i = 0; i < companions; i++)
            {
                GameBot bot = world.NewBot(eRealm.Albion, Classes[i % Classes.Length], 22, $"Companion{i}", "GroupPve");
                Assert.That(world.Place(bot, region, X - 100 - 40 * i, Y + 40 * i), Is.True, "companion placed");
                members.Add(bot);
                bot.Group = group;
                Assert.That(bot.EnterPlayerLedGroup(scene.Player), Is.True, "the gamebot joins the player's group");
                scene.Group.Add(bot);
            }
            scene.Enemy = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 22, "Enemy", "RvR");
            Assert.That(world.Place(scene.Enemy, region, X + enemyDistance, Y), Is.True, "enemy placed");
            world.Tick();
            scene.Enemy.Brain?.Stop();
            if (defensive)
                CompanionEngagementMode.Set(scene.Player, true);
            return scene;
        }

        /// <summary>The companions think through Observe only (brains stopped): what the group's rules order, nothing else.</summary>
        private static bool ObserveFor(Scene scene, double seconds)
        {
            foreach (GameBot bot in scene.Group)
                bot.Brain?.Stop();
            bool engaged = false;
            int ticks = (int)(seconds * RvrFightWorld.TicksPerSecond);
            for (int i = 0; i < ticks; i++)
            {
                scene.World.Tick();
                foreach (GameBot bot in scene.Group)
                    CompanionPvpEngagement.Observe(bot);
                engaged |= Engaged(scene);
            }
            return engaged;
        }

        private static bool Engaged(Scene scene) => scene.Group.Any(bot => bot.Brain is BotBrain { HasAggro: true });

        [Test]
        public void AttackSendsADefensiveGroupOntoATargetItWouldNotTakeOnItsOwn()
        {
            // /defensive's bound is 1,000 and its leash 1,250: an enemy 1,500 away is not the group's own pick.
            using Scene scene = Make(1_500, defensive: true);
            Assert.That(ObserveFor(scene, 6), Is.False, "on its own the group leaves it");
            scene.Player.TargetObject = scene.Enemy;
            string reply = PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out bool given);
            Assert.Multiple(() =>
            {
                Assert.That(given, Is.True, reply);
                Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Attack));
                Assert.That(PlayerGroupOrders.AttackTarget(scene.Player), Is.SameAs(scene.Enemy));
                foreach (GameBot bot in scene.Group)
                    Assert.That(CompanionEngagementMode.Allows(bot, scene.Enemy), Is.True, $"{bot.Name}: let through the leash");
                Assert.That(Engaged(scene), Is.True, "the fighters took it at once");
            });
            Assert.That(ObserveFor(scene, 2), Is.True, "and keep it");
        }

        [Test]
        public void AttackNeedsATarget()
        {
            using Scene scene = Make(1_500);
            scene.Player.TargetObject = null;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out bool given);
            Assert.That(given, Is.False);
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None));
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "attack"), Is.False, "the word without a target stays talk");
        }

        [Test]
        public void AssistLeavesTheGroupsOwnPickAndTakesThePlayersFight()
        {
            // Without an order the group opens on an enemy 800 away by itself (#664); under assist it waits for the player.
            using (Scene free = Make(800))
                Assert.That(ObserveFor(free, 6), Is.True, "control: the group's own pick engages");
            using Scene scene = Make(800);
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "assist me"), Is.True, "the /group word gives the order");
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Assist));
            Assert.That(ObserveFor(scene, 6), Is.False, "no pick of its own under assist");
            Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null);
            scene.Player.TargetObject = scene.Enemy;
            scene.Player.attackComponent.AttackState = true; // the player swings at it
            Assert.That(ObserveFor(scene, 1.5), Is.True, "the group takes the player's fight");
            GameBot fighter = scene.Group.First(bot => !BotPartyRoles.IsSupport(bot));
            Assert.That(PlayerGroupOrders.FocusFor(fighter, out bool applies), Is.SameAs(scene.Enemy));
            Assert.That(applies, Is.True);
        }

        [Test]
        public void RegroupDropsTheFightAndFightsNothingOnTheWayBack()
        {
            using Scene scene = Make(1_500);
            scene.Player.TargetObject = scene.Enemy;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out _);
            Assert.That(Engaged(scene), Is.True, "in the fight first");
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "regroup"), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Regroup), "the next order ends attack");
                foreach (GameBot bot in scene.Group)
                {
                    Assert.That(CompanionEngagementMode.Allows(bot, scene.Enemy), Is.False, $"{bot.Name}: breaks off");
                    Assert.That(CompanionEngagementMode.Restricts(bot), Is.True, $"{bot.Name}: its brain drops it");
                    Assert.That((bot.Brain as BotBrain)?.HasAggro, Is.False, $"{bot.Name}: nothing on its aggro list");
                    Assert.That(bot.TargetObject, Is.Not.SameAs(scene.Enemy), $"{bot.Name}: no longer targets it");
                    Assert.That(CompanionEngagementMode.Allows(bot, scene.Player), Is.True, $"{bot.Name}: a friend is never refused");
                }
            });
            // Everyone already stands beside the player: after the minimum the order ends and the stance rules are back.
            ObserveFor(scene, PlayerGroupOrders.RegroupMinimumMilliseconds / 1000.0 + 1);
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None), "back: regroup ended");
            Assert.That(CompanionEngagementMode.Allows(scene.Group[0], scene.Enemy), Is.True);
        }

        [Test]
        public void HoldIsHoldAndTheNextOrderEndsIt()
        {
            using Scene scene = Make(800);
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Hold, "command", out bool given);
            Assert.That(given, Is.True);
            Assert.That(CompanionEngagementMode.PlayerHolding(scene.Player), Is.True, "the /hold stance");
            Assert.That(ObserveFor(scene, 6), Is.False, "nobody opens on the enemy 800 away");
            scene.Player.TargetObject = scene.Enemy;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "chat", out given);
            Assert.That(given, Is.True);
            Assert.That(CompanionEngagementMode.PlayerHolding(scene.Player), Is.False, "attack ends hold");
            Assert.That(Engaged(scene), Is.True);
            CompanionEngagementMode.Set(scene.Player, defensive: false);
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None), "/aggressive ends the order");
        }

        [Test]
        public void AnAttackOrderEndsWhenItsTargetIsGone()
        {
            using Scene scene = Make(1_500);
            scene.Player.TargetObject = scene.Enemy;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out _);
            scene.Enemy.RemoveFromWorld();
            ObserveFor(scene, 1);
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None));
        }

        [Test]
        public void WithTheSwitchOffNothingIsAnOrder()
        {
            using Scene scene = Make(800);
            string reply = PlayerGroupOrders.Give(scene.Player, GroupOrder.Assist, "command", out bool given);
            Assert.Multiple(() =>
            {
                Assert.That(given, Is.False);
                Assert.That(reply, Does.Contain("turned off"));
                Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "assist me"), Is.False, "ordinary chat");
                Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None));
            });
        }

        [Test]
        public void AGamebotOutsideThePlayersGroupIsUntouched()
        {
            using Scene scene = Make(1_500);
            GameBot outsider = scene.World.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 22, "Outsider", "RvR");
            Assert.That(scene.World.Place(outsider, scene.Region, X + 200, Y + 300), Is.True);
            scene.World.Tick();
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Regroup, "command", out bool given);
            Assert.That(given, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(PlayerGroupOrders.Obeys(outsider, scene.Player), Is.False);
                Assert.That(PlayerGroupOrders.Restricts(outsider), Is.False);
                Assert.That(PlayerGroupOrders.MayFight(outsider, scene.Enemy), Is.True);
                Assert.That(PlayerGroupOrders.Obeying(scene.Player), Has.Count.EqualTo(scene.Group.Count));
            });
        }
    }
}
