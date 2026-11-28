using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;
using F = DOL.GS.CompanionRvrFormation;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #665: the player's group spreads into the period backline once it fights enemy bots in RvR (CompanionRvrFormation,
    /// BotBrain.CompanionFormation): the pure rules, then the fight harness (RvrFightWorld, #520 / #605, with its flat
    /// stand-in navmesh) with every brain running. Tests only until seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CompanionRvrFormation665
    {
        // ---- pure ---------------------------------------------------------------------------------------------------------

        [Test]
        public void OneSettingOnByDefaultAndShippedOn()
        {
            ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_COMPANION_RVR_FORMATION))
                .GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", F.PropertyKey, (object)true)));
            string root = TestContext.CurrentContext.TestDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "source", "tools", "release-server-settings.json")))
                root = Path.GetDirectoryName(root);
            Assume.That(root, Is.Not.Null, "the repository's release settings");
            string shipped = File.ReadAllText(Path.Combine(root, "source", "tools", "release-server-settings.json"));
            Assert.That(shipped, Does.Contain($"\"{F.PropertyKey}\": \"True\""));
        }

        [Test]
        public void OnlyWithTheSwitchInAWarZoneForABotThatJoins()
        {
            Assert.Multiple(() =>
            {
                Assert.That(F.Applies(on: true, warZone: true, joins: true), Is.True);
                Assert.That(F.Applies(false, true, true), Is.False, "switch off");
                Assert.That(F.Applies(true, warZone: false, true), Is.False, "PvE is unchanged");
                Assert.That(F.Applies(true, true, joins: false), Is.False, "passive, /bot stay or /bot hold");
                Assert.That(F.Places(RvrStand.Back), Is.True, "casters and healers walk for a place");
                Assert.That(F.Places(RvrStand.Front) || F.Places(RvrStand.Side) || F.Places(RvrStand.Escort), Is.False,
                    "tanks and melee fight as now; no guard escort yet");
            });
        }

        [Test]
        public void ASpotIsPulledToWithinTheBoundOfThePlayer()
        {
            var player = new Vector2(1_000, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(F.Clamp(new Vector2(1_500, 1_000), player, F.PlayerBound), Is.EqualTo(new Vector2(1_500, 1_000)), "inside: kept");
                Vector2 far = F.Clamp(new Vector2(4_000, 1_000), player, F.PlayerBound);
                Assert.That(Vector2.Distance(far, player), Is.EqualTo(F.PlayerBound).Within(0.5f), "outside: onto the bound");
                Assert.That(far.Y, Is.EqualTo(1_000).Within(0.01f), "in the same direction");
                Assert.That(F.PlayerBound, Is.EqualTo(CompanionRvrEngage.DefensiveBound), "the /defensive bound");
            });
        }

        [Test]
        public void IncomingUntilSomeoneIsInContact()
        {
            var group = new[] { new Vector2(0, 0), new Vector2(100, 0) };
            Assert.Multiple(() =>
            {
                Assert.That(F.Incoming(group, new[] { new Vector2(1_500, 0) }), Is.True, "seen, nobody in contact: scatter wider");
                Assert.That(F.Incoming(group, new[] { new Vector2(1_500, 0), new Vector2(400, 0) }), Is.False, "in contact");
                Assert.That(F.Incoming(group, Array.Empty<Vector2>()), Is.False, "no enemy");
            });
        }

        private static RvrPositioning.Placed Member(long key, eCharacterClass characterClass, int x, int y) =>
            new(new RvrCombatant
            {
                Key = key, Class = characterClass, Role = RvrRoles.For(characterClass), X = x, Y = y, Realm = eRealm.Albion,
            }, RvrPositioning.StandFor(RvrRoles.For(characterClass), characterClass));

        [Test]
        public void TheBacklineStandsBehindTheTanksApartAndNearThePlayer()
        {
            // Owner answer 1: tanks in front; casters behind; healers behind and more than 350 apart. Answer 2: near the player.
            var members = new List<RvrPositioning.Placed>
            {
                Member(1, eCharacterClass.Armsman, 900, 0), Member(2, eCharacterClass.Mercenary, 900, 80),
                Member(3, eCharacterClass.Cleric, 0, 0), Member(4, eCharacterClass.Cleric, 20, 20),
                Member(5, eCharacterClass.Wizard, 40, 40), Member(6, eCharacterClass.Theurgist, 60, 60),
            };
            var enemy = new Vector2(1_600, 40);
            var player = new Vector2(0, 0);
            var slots = F.Layout(members, enemy, player, incoming: false, now: 0);
            Vector2 line = new(900, 40);
            double front = RvrPositioning.Bearing(line, enemy);
            var backs = slots.Where(s => s.Stand == RvrStand.Back).ToList();
            var healers = backs.Where(s => s.Healer).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(backs, Has.Count.EqualTo(4));
                foreach (RvrSlot slot in backs)
                {
                    Assert.That(RvrPositioning.Ahead(slot.Spot, line, front), Is.LessThan(0), "behind the tanks' line");
                    Assert.That(Vector2.Distance(slot.Spot, player), Is.LessThanOrEqualTo(F.PlayerBound + 0.5f), "near the player");
                    Assert.That(slot.Error, Is.EqualTo(RvrSlotError.None), "the player's group makes no deliberate mistakes");
                }
                Assert.That(healers, Has.Count.EqualTo(2));
                Assert.That(Vector2.Distance(healers[0].Spot, healers[1].Spot), Is.GreaterThan(RvrPositioning.MezSpread),
                    "one mez can't catch both healers");
            });
        }

        [Test]
        public void TheHooksAreWired()
        {
            string root = TestContext.CurrentContext.TestDirectory;
            while (root != null && !Directory.Exists(Path.Combine(root, "source", "server", "GameServer", "bots")))
                root = Path.GetDirectoryName(root);
            Assume.That(root, Is.Not.Null, "the repository's server source");
            string brain = File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "BotBrain.cs"));
            Assert.Multiple(() =>
            {
                Assert.That(brain, Does.Contain("TryCompanionFormationMove(formationTarget)) return;"), "the attack turn");
                Assert.That(brain, Does.Contain("if (TryCompanionFormationSupport()) return;"), "the support turn");
                Assert.That(brain, Does.Contain("if (_brain.TryCompanionFormationSupport())"), "the follow state (a healer not on the attack)");
                Assert.That(brain, Does.Contain("CompanionHealerCombatHold.Holds(BotBody, leader) || CompanionFormationHolds()"),
                    "the travel formation doesn't pull a placed member back mid-fight");
            });
            string formation = File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "BotBrain.CompanionFormation.cs"));
            Assert.Multiple(() =>
            {
                Assert.That(formation, Does.Contain(": StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now);"), "the game's walk");
                Assert.That(formation, Does.Contain("if (bot.IsCasting || bot.IsIncapacitated || bot.IsCrowdControlled"),
                    "no walk starts out of a cast");
                Assert.That(formation, Does.Contain("&& !RvrTacticsActive() ? leader : null;"),
                    "the autonomous gamebots' own RvR tactics keep their groups");
                Assert.That(formation.IndexOf("CompanionFormationHealDue(bot))", StringComparison.Ordinal),
                    Is.LessThan(formation.IndexOf("if (ContinueRvrPlaceWalk(bot, now))", StringComparison.Ordinal)),
                    "heals come before a walk under way");
            });
        }

        // ---- the fight harness ------------------------------------------------------------------------------------------

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort Thidranki = 252, PveRegion = 665;
        private const int X = 30_000, Y = 30_000;
        private static readonly eCharacterClass[] Classes =
        [
            eCharacterClass.Armsman, eCharacterClass.Mercenary, eCharacterClass.Cleric, eCharacterClass.Cleric,
            eCharacterClass.Wizard, eCharacterClass.Theurgist, eCharacterClass.Cabalist,
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
        public void RestoreRolls()
        {
            CompanionRvrEngage.Roll = _roll;
            F.ProveWalk = null;
        }

        /// <summary>The stand-in navmesh has no grounded surface walk (AutonomousNavigationSurface.MoveAlongDryGround needs the
        /// real mesh manager), so the walk's proof is the straight line on its flat floor, capped at the longest place walk.</summary>
        private static void FlatProof() => F.ProveWalk = (bot, spot) =>
        {
            var from = new Vector2(bot.X, bot.Y);
            float length = Vector2.Distance(from, spot);
            if (length < RvrPositioning.SlotTolerance) return null;
            Vector2 to = length > RvrPositioning.MaxWalk ? from + (spot - from) / length * RvrPositioning.MaxWalk : spot;
            return new Vector3(to.X, to.Y, 0);
        };

        private sealed class Scene : IDisposable
        {
            public RvrFightWorld World;
            public StandInPlayer Player;
            public readonly List<GameBot> Group = new();
            public readonly List<GameBot> Enemies = new();
            public void Dispose() => World.Dispose();
        }

        private static Scene Make(ushort regionId, bool switchOn = true)
        {
            var settings = switchOn ? null : new Dictionary<string, object> { [F.PropertyKey] = false };
            var scene = new Scene { World = new RvrFightWorld(665, settings) };
            RvrFightWorld world = scene.World;
            Region region = world.AddRegion(regionId, regionId, "#665 flat field (rehearsal)");
            scene.Player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 22, "Leader");
            Assert.That(scene.Player.Enter(region, X, Y), Is.True, "the player entered");
            world.Track(scene.Player);
            var group = new Group(scene.Player);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
            members.Add(scene.Player);
            scene.Player.Group = group;
            for (int i = 0; i < Classes.Length; i++)
            {
                // Bunched on the player, 40 apart: the travel picture the report describes.
                GameBot bot = world.NewBot(eRealm.Albion, Classes[i], 22, $"Companion{i}", "GroupPve");
                Assert.That(world.Place(bot, region, X - 100 - 40 * i, Y + 40 * i), Is.True, "companion placed");
                members.Add(bot);
                bot.Group = group;
                Assert.That(bot.EnterPlayerLedGroup(scene.Player), Is.True, "the gamebot joins the player's group");
                scene.Group.Add(bot);
            }
            for (int j = 0; j < EnemyClasses.Length; j++)
            {
                GameBot enemy = world.NewBot(eRealm.Midgard, EnemyClasses[j], 22, $"Enemy{j}", "RvR");
                Assert.That(world.Place(enemy, region, X + 1_200, Y - 210 + 60 * j), Is.True, "enemy placed");
                scene.Enemies.Add(enemy);
            }
            world.Tick();
            // The enemies only stand there: the group's own pick (#664) opens the fight, and nothing hurts the group.
            foreach (GameBot enemy in scene.Enemies)
                enemy.Brain?.Stop();
            return scene;
        }

        private List<GameBot> Healers(Scene scene) => scene.Group.Where(b => b.CharacterClass?.ID == (int)eCharacterClass.Cleric).ToList();

        [Test]
        public void OnceEngagedTheBacklineSpreadsOutBehindTheTanksNearThePlayer()
        {
            using Scene scene = Make(Thidranki);
            FlatProof();
            List<GameBot> healers = Healers(scene);
            Assert.That(healers[0].GetDistanceTo(healers[1]), Is.LessThan((int)RvrPositioning.MezSpread), "bunched at the start");
            bool fought = false;
            scene.World.Run(15, _ => fought |= F.Read(scene.Player, GameLoop.GameLoopTime) != null);
            Assert.That(fought, Is.True, "the group got into the fight (#664's pick)");
            var backs = scene.Group.Where(b => RvrPositioning.StandFor(RvrRoles.For((eCharacterClass)b.CharacterClass.ID),
                (eCharacterClass)b.CharacterClass.ID) == RvrStand.Back).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(healers[0].GetDistanceTo(healers[1]), Is.GreaterThan((int)RvrPositioning.MezSpread - 60),
                    "the two healers stand more than a mez apart\n" + Picture(scene));
                foreach (GameBot bot in backs)
                {
                    Assert.That(bot.GetDistanceTo(scene.Player), Is.LessThanOrEqualTo(F.PlayerBound + (int)RvrPositioning.SlotTolerance),
                        $"{bot.Name} stays near the player");
                    Assert.That(bot.X, Is.LessThan(X + 1_200), $"{bot.Name} stands on the group's side of the enemy");
                }
                Assert.That(scene.World.ServiceErrors, Is.Zero, string.Join("\n", scene.World.Errors));
            });
        }

        /// <summary>What the formation saw of each back member (for a failure message).</summary>
        private static string Picture(Scene scene)
        {
            var fight = F.Read(scene.Player, GameLoop.GameLoopTime);
            var lines = new List<string> { fight == null ? "no fight read" :
                $"fight: enemies={fight.Enemies} incoming={fight.Incoming} line={fight.Line} enemy={fight.Enemy} backs={fight.Backs.Count}" };
            foreach (GameBot bot in scene.Group)
            {
                RvrSlot slot = fight?.SlotOf(bot) ?? default;
                lines.Add($"{bot.Name} {(eCharacterClass)bot.CharacterClass.ID} at=({bot.X},{bot.Y}) state={(bot.Brain as BotBrain)?.FSM?.GetCurrentState()?.StateType} " +
                          $"applies={F.Applies(bot, scene.Player)} stand={slot.Stand} spot={slot.Spot} healer={slot.Healer} casting={bot.IsCasting} " +
                          $"moving={bot.IsMoving} aggro={(bot.Brain as BotBrain)?.HasAggro} hp={bot.HealthPercent} autonomous={bot.IsAutonomousWorldBot}");
            }
            return string.Join("\n", lines);
        }

        [Test]
        public void TheFormationTurnSpreadsTheHealersOnItsOwn()
        {
            // The formation turn alone (every brain stopped; the group pick through Observe, then each back member's turn),
            // so the rule and the walk are tested apart from the FSM that calls them.
            using Scene scene = Make(Thidranki);
            FlatProof();
            foreach (GameBot bot in scene.Group)
                bot.Brain?.Stop();
            List<GameBot> healers = Healers(scene);
            int ticks = 15 * RvrFightWorld.TicksPerSecond;
            for (int i = 0; i < ticks; i++)
            {
                scene.World.Tick();
                foreach (GameBot bot in scene.Group)
                {
                    CompanionPvpEngagement.Observe(bot);
                    (bot.Brain as BotBrain)?.TryCompanionFormationSupport();
                }
            }
            string picture = Picture(scene);
            Assert.Multiple(() =>
            {
                Assert.That(healers[0].GetDistanceTo(healers[1]), Is.GreaterThan((int)RvrPositioning.MezSpread - 60),
                    "the two healers stand more than a mez apart\n" + picture);
                foreach (GameBot bot in healers)
                    Assert.That(bot.GetDistanceTo(scene.Player), Is.LessThanOrEqualTo(F.PlayerBound + (int)RvrPositioning.SlotTolerance),
                        $"{bot.Name} stays near the player\n" + picture);
            });
        }

        [Test]
        public void AHealerStopsWalkingForItsPlaceOnceAGroupmateNeedsAHeal()
        {
            // Review of #815: heals win at every hook. A healer already walking stops as soon as a groupmate drops below 70%,
            // and while one is hurt no healer sets off.
            using Scene scene = Make(Thidranki);
            FlatProof();
            foreach (GameBot bot in scene.Group)
                bot.Brain?.Stop();
            List<GameBot> healers = Healers(scene);
            GameBot hurt = scene.Group.First(b => !healers.Contains(b));
            GameBot walker = null;
            int ticks = 15 * RvrFightWorld.TicksPerSecond;
            for (int i = 0; i < ticks && walker == null; i++)
            {
                scene.World.Tick();
                foreach (GameBot bot in scene.Group)
                {
                    CompanionPvpEngagement.Observe(bot);
                    if ((bot.Brain as BotBrain)?.TryCompanionFormationSupport() == true && healers.Contains(bot) && walker == null)
                        walker = bot;
                }
            }
            Assert.That(walker, Is.Not.Null, "a healer set off for its place\n" + Picture(scene));
            var brain = (BotBrain)walker.Brain;
            scene.World.Tick();
            Assert.That(brain.TryCompanionFormationSupport(), Is.True, "its walk is under way and owns the turn");
            hurt.Health = hurt.MaxHealth / 2;
            Assert.That(hurt.HealthPercent, Is.LessThan(RvrPositioning.HealFirstPercent), "a groupmate needs a heal");
            Assert.Multiple(() =>
            {
                Assert.That(brain.TryCompanionFormationSupport(), Is.False, "the turn goes to the heal, not the walk");
                Assert.That(walker.IsMoving, Is.False, "the healer stopped walking\n" + Picture(scene));
            });
            var stoppedAt = new Vector2(walker.X, walker.Y);
            // While the groupmate stays hurt, no healer sets off again.
            for (int i = 0; i < 5 * RvrFightWorld.TicksPerSecond; i++)
            {
                scene.World.Tick();
                hurt.Health = hurt.MaxHealth / 2;
                foreach (GameBot healer in healers)
                {
                    CompanionPvpEngagement.Observe(healer);
                    Assert.That((healer.Brain as BotBrain)?.TryCompanionFormationSupport(), Is.False,
                        $"{healer.Name} stays to heal");
                }
            }
            Assert.That(Vector2.Distance(stoppedAt, new Vector2(walker.X, walker.Y)), Is.LessThan(RvrPositioning.SlotTolerance),
                "it stayed where it stopped\n" + Picture(scene));
        }

        [Test]
        public void WithTheSwitchOffTheHealersStayWhereTheyAre()
        {
            using Scene scene = Make(Thidranki, switchOn: false);
            FlatProof();
            List<GameBot> healers = Healers(scene);
            scene.World.Run(15);
            Assert.That(healers[0].GetDistanceTo(healers[1]), Is.LessThan((int)RvrPositioning.MezSpread),
                "the rules before #665: still bunched");
        }

        [Test]
        public void InPveItNeverApplies()
        {
            using Scene scene = Make(PveRegion);
            Assert.Multiple(() =>
            {
                foreach (GameBot bot in scene.Group)
                    Assert.That(F.Applies(bot, scene.Player), Is.False, $"{bot.Name}: PvE is unchanged");
            });
        }
    }
}
