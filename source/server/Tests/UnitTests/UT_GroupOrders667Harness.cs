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
            public readonly List<GameBot> Enemies = new();
            public void Dispose() => World.Dispose();
        }

        private static readonly eCharacterClass[] EnemyClasses =
        [
            eCharacterClass.Warrior, eCharacterClass.Runemaster, eCharacterClass.Berserker, eCharacterClass.Hunter,
            eCharacterClass.Shaman, eCharacterClass.Thane, eCharacterClass.Spiritmaster,
        ];

        /// <param name="farCompanion">Above 0: the first companion stands this far from the player, toward the enemy.</param>
        /// <param name="moreEnemies">Enemy gamebots standing beside <see cref="Scene.Enemy"/>.</param>
        private static Scene Make(int enemyDistance, bool defensive = false, bool switchOn = true, int companions = 3,
            int farCompanion = 0, int moreEnemies = 0)
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
                bool far = i == 0 && farCompanion > 0;
                Assert.That(world.Place(bot, region, far ? X + farCompanion : X - 100 - 40 * i, Y + 40 * i), Is.True,
                    "companion placed");
                members.Add(bot);
                bot.Group = group;
                Assert.That(bot.EnterPlayerLedGroup(scene.Player), Is.True, "the gamebot joins the player's group");
                scene.Group.Add(bot);
            }
            scene.Enemy = world.NewBot(eRealm.Midgard, eCharacterClass.Healer, 22, "Enemy", "RvR");
            Assert.That(world.Place(scene.Enemy, region, X + enemyDistance, Y), Is.True, "enemy placed");
            for (int j = 0; j < moreEnemies; j++)
            {
                GameBot more = world.NewBot(eRealm.Midgard, EnemyClasses[j % EnemyClasses.Length], 22, $"Enemy{j + 1}", "RvR");
                Assert.That(world.Place(more, region, X + enemyDistance, Y + 30 * (j + 1)), Is.True, "enemy placed");
                scene.Enemies.Add(more);
            }
            world.Tick();
            scene.Enemy.Brain?.Stop();
            foreach (GameBot more in scene.Enemies)
                more.Brain?.Stop();
            if (defensive)
                CompanionEngagementMode.Set(scene.Player, true);
            return scene;
        }

        /// <summary>The companions think through Observe only (brains stopped): what the group's rules order, nothing else.</summary>
        private static bool ObserveFor(Scene scene, double seconds, bool stop = true)
        {
            if (stop)
                StopBrains(scene);
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

        private static bool Engaged(Scene scene) => scene.Group.Any(Engaged);

        private static bool Engaged(GameBot bot) => bot.Brain is BotBrain { HasAggro: true };

        private static void StopBrains(Scene scene)
        {
            foreach (GameBot bot in scene.Group)
                bot.Brain?.Stop();
        }

        /// <summary>The enemy swings at <paramref name="victim"/> (its brain is stopped: only the attack state is set).</summary>
        private static void Swings(GameBot enemy, GameLiving victim)
        {
            enemy.TargetObject = victim;
            enemy.attackComponent.AttackState = true;
        }

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
            using Scene scene = Make(800, switchOn: false);
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
        public void BackFromRegroupTheGroupDefendsThePlayerBeingHit()
        {
            // Review of #824, point 2: once back, a bot defends the player and the group, not only itself.
            using Scene scene = Make(1_500);
            StopBrains(scene);
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "regroup"), Is.True);
            foreach (GameBot bot in scene.Group)
                Assert.That(CompanionEngagementMode.Allows(bot, scene.Enemy), Is.False, $"{bot.Name}: opens nothing");
            Swings(scene.Enemy, scene.Player);
            Assert.Multiple(() =>
            {
                Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Regroup));
                foreach (GameBot bot in scene.Group)
                {
                    Assert.That(bot.IsWithinRadius(scene.Player, PlayerGroupOrders.RegroupRadius), Is.True, $"{bot.Name}: back");
                    Assert.That(CompanionEngagementMode.Restricts(bot), Is.True, $"{bot.Name}: regroup governs it");
                    Assert.That(CompanionEngagementMode.Allows(bot, scene.Enemy), Is.True,
                        $"{bot.Name}: defends the player who is being hit");
                }
            });
        }

        [Test]
        public void OnTheWayBackABotFightsBackOnlyWhenRootedOrHurt()
        {
            // Review of #824, point 2: a bot not back yet breaks off, unless it can't get away.
            using Scene scene = Make(1_500, farCompanion: 900);
            StopBrains(scene);
            GameBot away = scene.Group[0];
            Assert.That(away.IsWithinRadius(scene.Player, PlayerGroupOrders.RegroupRadius), Is.False, "precondition: not back");
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "regroup"), Is.True);
            Swings(scene.Enemy, away);
            Assert.That(CompanionEngagementMode.Allows(away, scene.Enemy), Is.False, "full health and free to move: breaks off");
            Assert.That(CompanionEngagementMode.Allows(scene.Group[1], scene.Enemy), Is.True,
                "a back bot defends a groupmate still on its way");

            short speed = away.MaxSpeedBase;
            away.MaxSpeedBase = 0;
            Assert.That(away.MaxSpeed, Is.LessThanOrEqualTo(0), "precondition: rooted");
            Assert.That(CompanionEngagementMode.Allows(away, scene.Enemy), Is.True, "rooted: fights back");
            Assert.That(CompanionEngagementMode.Allows(away, scene.Group[1]), Is.True, "a friend is never refused");
            away.MaxSpeedBase = speed;
            Assert.That(CompanionEngagementMode.Allows(away, scene.Enemy), Is.False, "free again: breaks off again");

            away.Health = away.MaxHealth * (PlayerGroupOrders.RegroupFightBackHealthPercent - 10) / 100;
            Assert.That(CompanionEngagementMode.Allows(away, scene.Enemy), Is.True, "below half health: fights back");
            scene.Enemy.attackComponent.AttackState = false;
            scene.Enemy.TargetObject = null;
            Assert.That(CompanionEngagementMode.Allows(away, scene.Enemy), Is.False, "hurt, but nothing hits it: opens nothing");
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Regroup), "still regrouping");
        }

        [Test]
        public void APerBotHoldWinsOverAPartyOrder()
        {
            // Review of #824, point 3: passive and /bot hold keep a bot out of the group's orders in the world.
            using Scene scene = Make(1_500, companions: 4);
            StopBrains(scene);
            GameBot free = scene.Group[0], passive = scene.Group[2], suspended = scene.Group[3];
            var brain = (BotBrain)suspended.Brain;
            brain.FSM.SetCurrentState(eFSMStateType.PASSIVE); // /bot hold
            passive.Stance = eBotStance.Passive;
            Assert.That(brain.FSM.GetCurrentState()?.StateType, Is.EqualTo(eFSMStateType.PASSIVE), "precondition: /bot hold");
            List<GameBot> obeying = PlayerGroupOrders.Obeying(scene.Player);
            Assert.That(obeying, Does.Not.Contain(suspended).And.Not.Contain(passive).And.Contain(free));

            scene.Player.TargetObject = scene.Enemy;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out bool given);
            Assert.That(given, Is.True);
            Assert.Multiple(() =>
            {
                foreach (GameBot held in new[] { suspended, passive })
                {
                    Assert.That(PlayerGroupOrders.Obeys(held, scene.Player), Is.False, $"{held.Name}: keeps to its own order");
                    Assert.That(PlayerGroupOrders.LetsThrough(held, scene.Enemy), Is.False, $"{held.Name}: no way through");
                    Assert.That(PlayerGroupOrders.FocusFor(held, out bool applies), Is.Null, $"{held.Name}: no focus");
                    Assert.That(applies, Is.False, $"{held.Name}: the order doesn't set its target");
                    Assert.That(Engaged(held), Is.False, $"{held.Name}: the attack didn't engage it");
                }
                Assert.That(PlayerGroupOrders.LetsThrough(free, scene.Enemy), Is.True, "control: the free bot is let through");
                Assert.That(Engaged(free), Is.True, "control: the free bot took the target");
            });
            ObserveFor(scene, 2, stop: false);
            Assert.That(Engaged(suspended) || Engaged(passive), Is.False, "and they stay out while the group fights");
        }

        [Test]
        public void InAGamebotLedGroupTheOrderIsRefused()
        {
            // Review of #824, point 3: the same rule as /pull when a gamebot leads the player's group.
            using Scene scene = Make(1_500);
            Group group = scene.Player.Group;
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(scene.Player, "g-667"));
            try
            {
                Assert.That(BotLedGroups.HostedPlayerOf(group), Is.SameAs(scene.Player), "precondition: a bot-led group");
                scene.Player.TargetObject = scene.Enemy;
                string reply = PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out bool given);
                Assert.Multiple(() =>
                {
                    Assert.That(given, Is.False);
                    Assert.That(reply, Is.EqualTo(BotLedGroups.BotLeadsMessage(group.LivingLeader?.Name)));
                    Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "attack"), Is.False, "the word stays talk");
                    Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None));
                    Assert.That(Engaged(scene), Is.False, "nobody was sent in");
                });
            }
            finally
            {
                BotLedGroups.ClearHosted(group);
            }
        }

        [Test]
        public void AnAttackIntoALosingFightIsWarnedButCarriedOut()
        {
            // Review of #824, point 3 (owner answer 5): the player and one companion against eight: #664 would back off.
            using (Scene even = Make(900, companions: 1))
            {
                even.Player.TargetObject = even.Enemy;
                PlayerGroupOrders.Give(even.Player, GroupOrder.Attack, "command", out bool given);
                Assert.That(given, Is.True);
                Assert.That(PlayerGroupOrders.Warned(even.Player), Is.False, "control: two against one is no warning");
            }
            using Scene scene = Make(900, companions: 1, moreEnemies: 7);
            Assert.That(CompanionRvrEngage.SizeUpAgainst(scene.Player, scene.Enemy).Call, Is.EqualTo(RvrSightCall.BackOff),
                "precondition: #664's size-up calls it a losing fight");
            scene.Player.TargetObject = scene.Enemy;
            PlayerGroupOrders.Give(scene.Player, GroupOrder.Attack, "command", out bool warnedGiven);
            Assert.Multiple(() =>
            {
                Assert.That(warnedGiven, Is.True, "the order is given");
                Assert.That(PlayerGroupOrders.Warned(scene.Player), Is.True, "with the warning");
                Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Attack));
                Assert.That(Engaged(scene), Is.True, "and the group goes in anyway");
            });
        }

        [Test]
        public void AnOrderEndsFiveSecondsAfterTheFight()
        {
            // Review of #824, point 3: the issue's "an order ends 5 s after the fight", through GroupInCombat and Check.
            using Scene scene = Make(4_000);
            StopBrains(scene);
            Assert.That(PlayerGroupOrders.FromGroupChat(scene.Player, "assist"), Is.True);
            void Run(double seconds, bool fighting)
            {
                for (int i = 0; i < (int)(seconds * RvrFightWorld.TicksPerSecond); i++)
                {
                    scene.World.Tick();
                    if (fighting)
                        scene.Player.LastAttackTickPvP = GameLoop.GameLoopTime;
                    PlayerGroupOrders.Current(scene.Player);
                }
            }
            Run(2, fighting: true);
            Assert.That(scene.Player.InCombat, Is.True, "precondition: the player is fighting");
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Assist), "in the fight");
            long lastSwing = scene.Player.LastAttackTickPvP;
            // In combat until IN_COMBAT_DURATION after the last swing, then 5 s of quiet.
            Run((GameLiving.IN_COMBAT_DURATION + PlayerGroupOrders.FightOverQuietMilliseconds - 1_000) / 1000.0, fighting: false);
            Assert.That(scene.Player.InCombat, Is.False, "precondition: the fight is over");
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.Assist),
                $"under 5 s of quiet: goes on ({GameLoop.GameLoopTime - lastSwing} ms after the last swing)");
            Run(2, fighting: false);
            Assert.That(PlayerGroupOrders.Current(scene.Player), Is.EqualTo(GroupOrder.None), "5 s after the fight: ended");
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
