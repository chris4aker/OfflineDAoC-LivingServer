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
    /// #664 step 1 through the real CompanionPvpEngagement.Observe, in the fight harness (RvrFightWorld, #520 / #605): a
    /// stand-in player and real gamebots befriended into his group (one of the bot types the group pick covers), in
    /// Thidranki (region 252) and in a plain PvE region, against enemy Midgard gamebots that only stand there (their brains
    /// are stopped, so nothing attacks the group and only the group pick can start the fight). The perception rolls are
    /// made certain (CompanionRvrEngage.Roll = 0) so the notice curve's timing, not its luck, is under test. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CompanionRvrEngage664Harness
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort Thidranki = 252, PveRegion = 664;
        private const int X = 30_000, Y = 30_000;
        private static readonly eCharacterClass[] Classes =
        [
            eCharacterClass.Mercenary, eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Armsman,
            eCharacterClass.Scout, eCharacterClass.Friar, eCharacterClass.Theurgist,
        ];
        private static readonly eCharacterClass[] EnemyClasses =
        [
            eCharacterClass.Warrior, eCharacterClass.Healer, eCharacterClass.Runemaster, eCharacterClass.Berserker,
            eCharacterClass.Hunter, eCharacterClass.Shaman, eCharacterClass.Thane, eCharacterClass.Spiritmaster,
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
            public readonly List<GameBot> Enemies = new();
            public void Dispose() => World.Dispose();
        }

        private static Scene Make(ushort regionId, int enemyDistance, int companions = 2, int enemies = 1, bool stealthed = false,
            bool defensive = false, bool switchOn = true)
        {
            var settings = switchOn ? null : new Dictionary<string, object> { [CompanionRvrEngage.PropertyKey] = false };
            var scene = new Scene { World = new RvrFightWorld(664, settings) };
            RvrFightWorld world = scene.World;
            Region region = world.AddRegion(regionId, regionId, "#664 flat field (rehearsal)");
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
            for (int j = 0; j < enemies; j++)
            {
                GameBot enemy = world.NewBot(eRealm.Midgard, EnemyClasses[j % EnemyClasses.Length], 22, $"Enemy{j}", "RvR");
                Assert.That(world.Place(enemy, region, X + enemyDistance, Y + 30 * j), Is.True, "enemy placed");
                if (stealthed)
                    enemy.Flags |= GameNPC.eFlags.STEALTH;
                scene.Enemies.Add(enemy);
            }
            world.Tick(); // the zones file what was placed
            // The enemies only stand there: nothing attacks the group, so only the group's own pick can start a fight.
            foreach (GameBot enemy in scene.Enemies)
                enemy.Brain?.Stop();
            if (defensive)
                CompanionEngagementMode.Set(scene.Player, true);
            return scene;
        }

        private static bool OnAnEnemy(Scene scene) => scene.Group.Any(bot =>
            bot.Brain is BotBrain brain && brain.HasAggro && scene.Enemies.Contains(bot.TargetObject as GameBot) ||
            scene.Enemies.Contains(bot.TargetObject as GameBot));

        /// <summary>
        /// The companions think through Observe only (their brains stopped too), every tick for <paramref name="seconds"/>:
        /// what the group pick decides and orders, nothing else. True once a companion took an enemy.
        /// </summary>
        private static bool ObserveFor(Scene scene, double seconds, Action eachTick = null)
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
                engaged |= scene.Group.Any(bot => bot.Brain is BotBrain brain && brain.HasAggro);
                eachTick?.Invoke();
            }
            return engaged;
        }

        [Test]
        public void TheGroupDoesNotOpenOnAnEnemyUnderItsRealmsGuards()
        {
            // Review of e0027909: the group charged an enemy bot under its keep's guards, and then the guards joined.
            using Scene scene = Make(Thidranki, 1_500);
            RvrFightRehearsal.RehearsalLord guard = RvrFightRehearsal.RehearsalLord.Make(scene.Region);
            guard.Realm = eRealm.Midgard;
            guard.Level = 30;
            Assert.That(scene.World.Place(guard, scene.Region, X + 1_800, Y), Is.True, "guard placed, 300 past the enemy");
            guard.Brain?.Stop();
            scene.World.Tick();
            bool engaged = ObserveFor(scene, 6);
            Assert.Multiple(() =>
            {
                Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null, "no pick under the guards");
                Assert.That(engaged, Is.False, "nobody charged it");
                Assert.That(scene.World.ServiceErrors, Is.Zero, string.Join("\n", scene.World.Errors));
            });
        }

        [Test]
        public void TheGroupDoesNotOpenOnAnEnemyInASafeArea()
        {
            using Scene scene = Make(Thidranki, 1_500);
            scene.Region.AddArea(new Area.Circle("#664 safe area (rehearsal)", X + 1_500, Y, 0, 400) { IsSafeArea = true });
            bool engaged = ObserveFor(scene, 6);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousWorldBotController.IsSafeArea(scene.Enemies[0]), Is.True, "the enemy stands in it");
                Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null, "no pick in a safe area");
                Assert.That(engaged, Is.False, "nobody charged it");
            });
        }

        [Test]
        public void OnAggressiveTheGroupEngagesAnEnemy1500FromThePlayerWithNoAttackFromHim()
        {
            using Scene scene = Make(Thidranki, 1_500);
            bool engaged = ObserveFor(scene, 6);
            Assert.Multiple(() =>
            {
                Assert.That(scene.Player.IsAttacking, Is.False, "the player never attacked");
                Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.SameAs(scene.Enemies[0]), "the group's pick");
                Assert.That(engaged, Is.True, "a companion took it");
                Assert.That(scene.World.ServiceErrors, Is.Zero, string.Join("\n", scene.World.Errors));
            });
        }

        [Test]
        public void NotAtOnceTheNoticeCurveAndTheReactionDelayComeFirst()
        {
            using Scene scene = Make(Thidranki, 1_500);
            ObserveFor(scene, 0.5);
            Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null, "half a second in: noticed at most, still reacting");
        }

        [Test]
        public void WithTheSwitchOffTheGroupStillWaitsForThePlayer()
        {
            // The report (FarmVibes, Thidranki): the same scene on the rules before #664.
            using Scene scene = Make(Thidranki, 1_500, switchOn: false);
            Assert.That(ObserveFor(scene, 6), Is.False);
            Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null);
        }

        [TestCase(800, true)]
        [TestCase(1_500, false)]
        public void OnDefensiveTheBoundIsTighter(int distance, bool engages)
        {
            using Scene scene = Make(Thidranki, distance, defensive: true);
            Assert.That(CompanionEngagementMode.PlayerDefensive(scene.Player), Is.True);
            Assert.That(ObserveFor(scene, 6), Is.EqualTo(engages));
        }

        [Test]
        public void InPveNothingChanges()
        {
            using Scene scene = Make(PveRegion, 1_500);
            Assert.That(ObserveFor(scene, 6), Is.False);
            Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null);
        }

        [Test]
        public void AStealthedEnemyIsNeverPickedFromAfar()
        {
            using Scene scene = Make(Thidranki, 1_500, stealthed: true);
            Assert.That(scene.Enemies[0].IsStealthed, Is.True);
            Assert.That(ObserveFor(scene, 6), Is.False, "only the close-range stealth rule can find a stealther (within 350)");
        }

        [Test]
        public void ADuoHoldsOffABotEightButAFullGroupTakesIt()
        {
            using (Scene duo = Make(Thidranki, 900, companions: 1, enemies: 8))
            {
                Assert.That(ObserveFor(duo, 6), Is.False, "the player and one companion against eight: clearly losing");
                Assert.That(CompanionRvrEngage.Holding(duo.Player), Is.True);
            }
            using (Scene eight = Make(Thidranki, 900, companions: 7, enemies: 8))
            {
                // The group's first pick: once it is engaged the companions really fight, so the healer may fall and the
                // pick move on within the run.
                GameLiving first = null;
                bool held = false;
                Assert.That(ObserveFor(eight, 6, () =>
                {
                    first ??= CompanionRvrEngage.CurrentPick(eight.Player);
                    held |= CompanionRvrEngage.Holding(eight.Player);
                }), Is.True, "eight against eight: a fair fight");
                Assert.That(held, Is.False, "never held off");
                Assert.That(first?.Name, Is.EqualTo("Enemy1").Or.EqualTo("Enemy5"),
                    "the kill order: a healer first (the Midgard Healer or Shaman)");
            }
        }

        [Test]
        public void AFullGroupWithItsBrainsRunningGoesForTheEnemyEight()
        {
            // The fight harness's own loop (every service, the companions' real brains): the player's eight in Thidranki,
            // an enemy eight 1,500 away that only stands there. The group sees, sizes up, picks and walks in.
            using Scene scene = Make(Thidranki, 1_500, companions: 7, enemies: 8);
            bool onAnEnemy = false;
            scene.World.Run(10, _ => onAnEnemy |= OnAnEnemy(scene));
            Assert.Multiple(() =>
            {
                Assert.That(scene.Player.IsAttacking, Is.False, "the player never attacked");
                Assert.That(onAnEnemy, Is.True, "a companion took an enemy of the eight");
                Assert.That(scene.World.ServiceErrors, Is.Zero, string.Join("\n", scene.World.Errors));
            });
        }

        // ---- #664 step 2: /hold and the drop leash -------------------------------------------------------------------------

        [Test]
        public void UnderHoldTheGroupLetsAnEnemyNearbyPassButOtherwiseGoesForIt()
        {
            // Owner answer 3 on #664: a hold mode for sneaking past or waiting at a keep. Brains running, every service.
            using (Scene held = Make(Thidranki, 800, companions: 3))
            {
                CompanionEngagementMode.SetHold(held.Player);
                bool onAnEnemy = false;
                held.World.Run(10, _ => onAnEnemy |= OnAnEnemy(held));
                Assert.Multiple(() =>
                {
                    Assert.That(CompanionEngagementMode.PlayerHolding(held.Player), Is.True);
                    Assert.That(onAnEnemy, Is.False, "nobody opened on the enemy 800 away");
                    Assert.That(CompanionRvrEngage.CurrentPick(held.Player), Is.Null, "the group made no pick");
                    Assert.That(held.World.ServiceErrors, Is.Zero, string.Join("\n", held.World.Errors));
                });
            }
            using (Scene free = Make(Thidranki, 800, companions: 3))
            {
                bool onAnEnemy = false;
                free.World.Run(10, _ => onAnEnemy |= OnAnEnemy(free));
                Assert.That(onAnEnemy, Is.True, "the same scene without /hold: the group goes for it");
            }
        }

        [Test]
        public void UnderHoldABotThatIsHitFightsBackAndTheOthersLeaveIt()
        {
            using Scene scene = Make(Thidranki, 250, companions: 3);
            CompanionEngagementMode.SetHold(scene.Player);
            GameBot victim = scene.Group[0], other = scene.Group[2];
            GameBot enemy = scene.Enemies[0];
            Assert.That(CompanionEngagementMode.Allows(victim, enemy), Is.False, "before it hits anyone: left alone");
            victim.attackComponent.AddAttacker(new AttackData
            {
                Attacker = enemy, Target = victim, AttackType = AttackData.eAttackType.MeleeOneHand, Interval = 4_000,
            });
            Assert.Multiple(() =>
            {
                Assert.That(CompanionEngagementMode.Allows(victim, enemy), Is.True, "the bot it hits defends itself");
                Assert.That(CompanionEngagementMode.Allows(other, enemy), Is.False, "a groupmate it isn't hitting stays out");
                Assert.That(CompanionEngagementMode.Restricts(other), Is.True, "and its brain drops it");
                Assert.That(CompanionEngagementMode.Allows(scene.Group[1], scene.Player), Is.True,
                    "a friend is never refused: the healer keeps its heal target");
            });
            CompanionEngagementMode.Set(scene.Player, defensive: false);
            Assert.That(CompanionEngagementMode.PlayerHolding(scene.Player), Is.False, "/aggressive ends /hold");
            Assert.That(CompanionEngagementMode.Allows(other, enemy), Is.True, "and the group fights as one again");
        }

        [Test]
        public void UnderHoldTheFirstSpellHitOnABotIsAnsweredThroughTheRealAttackPath()
        {
            // Review of #814: a spell hit reaches the brain (GameLiving.OnAttackedByEnemy's event) before the attacker
            // tracker hears of it, and a bolt in flight or a finished cast leaves the caster targeting someone else. The real
            // path must still let the bot that was hit put its attacker on its aggro list at the first hit.
            using Scene scene = Make(Thidranki, 250, companions: 3);
            CompanionEngagementMode.SetHold(scene.Player);
            GameBot victim = scene.Group[0], other = scene.Group[2];
            GameBot enemy = scene.Enemies[0];
            enemy.TargetObject = scene.Player; // its cast is done and it has turned to someone else
            Assert.That(enemy.IsCasting || enemy.IsAttacking, Is.False, "not casting or swinging at the victim");
            var victimBrain = (BotBrain)victim.Brain;
            Assert.That(victimBrain.HasAggro, Is.False, "nothing on its aggro list before the hit");
            victim.OnAttackedByEnemy(new AttackData
            {
                Attacker = enemy, Target = victim, AttackType = AttackData.eAttackType.Spell,
                AttackResult = eAttackResult.HitUnstyled, Damage = 40, Interval = 4_000,
            });
            Assert.Multiple(() =>
            {
                Assert.That(victimBrain.HasAggro, Is.True, "the bot the spell hit fights back at once");
                Assert.That(((BotBrain)other.Brain).HasAggro, Is.False, "a groupmate it didn't hit stays out under /hold");
            });
        }

        [Test]
        public void APickThatRunsPastTheLeashIsLetGoByEveryBotUnlessItIsHittingThem()
        {
            using Scene scene = Make(Thidranki, 1_500);
            Assert.That(ObserveFor(scene, 6), Is.True, "engaged first");
            GameBot enemy = scene.Enemies[0];
            Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.SameAs(enemy));
            // It breaks off (once hit it may swing back, and that would make it "hitting the group", which is never let go)
            // and runs off, past the leash from the player.
            enemy.StopAttack();
            enemy.TargetObject = null;
            foreach (GameLiving member in scene.Player.Group.GetMembersInTheGroup())
                member.attackComponent?.AttackerTracker?.Clear();
            CompanionPvpEngagement.Reset(scene.Player);
            enemy.X = X + CompanionRvrEngage.Leash(defensive: false) + 150;
            enemy.movementComponent.ForceUpdatePosition(); // an NPC's position is its movement component's
            Assert.That(scene.Player.GetDistanceTo(enemy), Is.GreaterThan(CompanionRvrEngage.Leash(defensive: false)), "past the leash");
            Assert.That(CompanionRvrEngage.HittingGroup(scene.Player, enemy), Is.False, "it is hitting nobody now");
            foreach (GameBot bot in scene.Group)
                Assert.That(CompanionEngagementMode.Allows(bot, enemy), Is.False, $"{bot.Name}: let go");
            foreach (GameBot bot in scene.Group)
                (bot.Brain as BotBrain)?.EnforceCompanionEngagementRange();
            ObserveFor(scene, 1.5);
            Assert.Multiple(() =>
            {
                Assert.That(CompanionRvrEngage.CurrentPick(scene.Player), Is.Null, "the group's pick is let go");
                foreach (GameBot bot in scene.Group)
                {
                    Assert.That(bot.TargetObject, Is.Not.SameAs(enemy), $"{bot.Name} no longer targets it");
                    Assert.That((bot.Brain as BotBrain)?.HasAggro, Is.False, $"{bot.Name} has nothing on its aggro list");
                }
            });
            // An enemy hitting the group is never let go, however far it stands.
            scene.Group[1].attackComponent.AddAttacker(new AttackData
            {
                Attacker = enemy, Target = scene.Group[1], AttackType = AttackData.eAttackType.Spell, Interval = 4_000,
            });
            Assert.That(CompanionEngagementMode.Allows(scene.Group[0], enemy), Is.True, "it is hitting a groupmate: fought");
        }
    }
}
