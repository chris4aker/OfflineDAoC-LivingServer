using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.GS.Keeps;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #572 step 2, ram crews on purpose (owner 2026-11-27: the seats, without a "second ram only at 16+ bots" rule and
    /// without a woodworking repair). Pure rules, then siege cases in the fight harness (RvrFightWorld) with real rams and
    /// real gamebots, and cases that drive the controller's own turns: the operator's engine turn registers its ram, and a
    /// melee gamebot's siege turn takes a seat, walks to it and holds it. Autonomous gamebots only; the player's ram keeps
    /// the crew #525 gave it. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RamCrews572
    {
        private const int X = 30_000, Y = 30_000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        // ---- pure rules ----

        [Test]
        public void MeleeFightersCrewTheRamAndNobodyElse() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRamCrews.IsCrewCandidate(false, false), Is.True, "melee fighters crew the ram");
            Assert.That(AutonomousRamCrews.IsCrewCandidate(true, false), Is.False, "casters and archers shoot the walls");
            Assert.That(AutonomousRamCrews.IsCrewCandidate(false, true), Is.False, "healers keep the crew alive");
        });

        [Test]
        public void ARamWithEmptySeatsFillsBeforeAFullerOne() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRamCrews.ChooseRam(new[] { (2, 100L), (9, 900L) }), Is.EqualTo(1), "the most empty places, though farther");
            Assert.That(AutonomousRamCrews.ChooseRam(new[] { (5, 900L), (5, 100L) }), Is.EqualTo(1), "the nearest on a tie");
            Assert.That(AutonomousRamCrews.ChooseRam(new[] { (0, 10L), (1, 5_000L) }), Is.EqualTo(1), "a full ram takes nobody");
            Assert.That(AutonomousRamCrews.ChooseRam(new[] { (0, 10L), (0, 20L) }), Is.EqualTo(-1), "every ram full: no seat");
            Assert.That(AutonomousRamCrews.ChooseRam(new (int, long)[0]), Is.EqualTo(-1), "no ram: no seat");
        });

        [Test]
        public void TheLowestFreeSeatIsTaken() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRamCrews.FirstFreeSeat(new HashSet<int>()), Is.EqualTo(0));
            Assert.That(AutonomousRamCrews.FirstFreeSeat(new HashSet<int> { 0, 1, 3 }), Is.EqualTo(2), "a seat given up is taken again");
        });

        [Test]
        public void EveryCrewSpotStandsBehindTheRamWithinItsCrewReach()
        {
            Vector2 ram = new(1000, 0), door = new(700, 0);
            var spots = new HashSet<(int, int)>();
            for (int seat = 0; seat < 12; seat++)
            {
                Vector2 spot = AutonomousRamCrews.CrewSpot(ram, door, seat);
                Assert.That(Vector2.Distance(spot, ram), Is.LessThan(AutonomousRamCrews.SpotReach), $"seat {seat} is counted by the ram (300)");
                Assert.That(spot.X, Is.GreaterThan(ram.X - 60), $"seat {seat} stands on the far side from the door");
                Assert.That(spots.Add(((int)spot.X, (int)spot.Y)), Is.True, $"seat {seat} has its own spot");
            }
        }

        [Test]
        public void TheOwnersRulesAreKept() => Assert.Multiple(() =>
        {
            // owner 2026-11-27: no "second ram only at 16+" rule (the #612 door cap decides the engines) and no woodworking repair
            Assert.That(typeof(AutonomousRamCrews).GetMethod("RamsForDoor"), Is.Null);
            Assert.That(AutonomousRamCrews.SpotReach, Is.LessThan(GameSiegeRam.BotCrewRadius));
        });

        [Test]
        public void OnlyMeleeClassesAreCrewCandidates() => Assert.Multiple(() =>
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            foreach (var (cls, crews) in new[]
                     {
                         (eCharacterClass.Armsman, true), (eCharacterClass.Mercenary, true), (eCharacterClass.Paladin, true),
                         (eCharacterClass.Savage, true), (eCharacterClass.Cleric, false), (eCharacterClass.Healer, false),
                         (eCharacterClass.Shaman, false), (eCharacterClass.Druid, false), (eCharacterClass.Wizard, false),
                         (eCharacterClass.Runemaster, false), (eCharacterClass.Scout, false), (eCharacterClass.Hunter, false),
                     })
                Assert.That(AutonomousWorldBotController.IsRamCrewCandidate(world.NewBot(eRealm.Albion, cls, 50, "c" + cls)),
                    Is.EqualTo(crews), cls.ToString());
        });

        // ---- the siege case in the fight harness ----

        /// <summary>
        /// Past the 500 ms objects-in-radius cache (GameObject.GetObjectsInRadius: taking control arms the ram, which reads
        /// its crew before anyone stands there), then the zone pass alone so the zones file what was placed. No brain
        /// thinks: no bot walks off its spot, and no bot's turn fills a server-wide cache another test reads.
        /// </summary>
        private static void Settle(RvrFightWorld world)
        {
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, GameLoop.GameLoopTime + 1_000);
            ZoneService.Instance.Tick();
        }

        private static GameSiegeRam Ram(RvrFightWorld world, Region region, int x, int y, GameLiving op, GameKeepDoor door = null)
        {
            var ram = (GameSiegeRam)AutonomousWorldBotController.CreateSiegeWeapon(BotSiegeKind.Ram);
            ram.Realm = eRealm.Albion;
            Assert.That(world.Place(ram, region, x, y), Is.True, "ram placed");
            Assert.That(ram.TryTakeControl(op), Is.True, "operator in control");
            if (door != null) ram.TargetObject = door; // aimed at the door, as its operator's turn leaves it
            return ram;
        }

        /// <summary>A keep door that stands or has fallen as the case says (no keep behind it: only its state is read).</summary>
        private sealed class StandInDoor : GameKeepDoor
        {
            public bool Standing = true;
            public eDoorState Shown = eDoorState.Closed;
            public override bool IsAlive => Standing;
            public override eDoorState State { get => Shown; set => Shown = value; }
        }

        private static StandInDoor Door(Region region, int x, int y)
        {
            var door = new StandInDoor();
            door.CurrentRegion = region;
            door.X = x; door.Y = y; door.Z = 0;
            return door;
        }

        /// <summary>An enemy that is swinging at its target (the attack itself is not run: only who strikes whom is read).</summary>
        private sealed class Striker : GameNPC
        {
            public override bool IsAttacking => true;
        }

        private static Striker Strike(RvrFightWorld world, Region region, GameLiving victim, int dx, int z)
        {
            var striker = new Striker { Name = "Striker", Realm = eRealm.Midgard, Level = 50 };
            Assert.That(world.Place(striker, region, victim.X + dx, victim.Y, z), Is.True, "striker placed");
            striker.TargetObject = victim;
            return striker;
        }

        // ---- the controller's own turns, through reflection (the turns are private) ----

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type ControllerType = typeof(AutonomousWorldBotController);

        private static void SetField(AutonomousWorldBotController controller, string name, object value)
        {
            FieldInfo field = ControllerType.GetField(name, Hidden);
            Assert.That(field, Is.Not.Null, "AutonomousWorldBotController." + name);
            field.SetValue(controller, value);
        }

        private static T GetField<T>(AutonomousWorldBotController controller, string name) =>
            (T)ControllerType.GetField(name, Hidden)!.GetValue(controller);

        private static bool Call(AutonomousWorldBotController controller, string name, params object[] args)
        {
            MethodInfo method = ControllerType.GetMethod(name, Hidden);
            Assert.That(method, Is.Not.Null, "AutonomousWorldBotController." + name);
            try { return (bool)method.Invoke(controller, args); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        /// <summary>A controller whose force assaults this keep, as its RvR plan leaves it: the keep assault intent and the
        /// keep as its destination (at the door).</summary>
        private static AutonomousWorldBotController Assaulting(string keepId, Region region, GameObject door)
        {
            NoKeeps();
            var controller = new AutonomousWorldBotController();
            Type destination = ControllerType.GetNestedType("CampDestination", BindingFlags.NonPublic);
            Assert.That(destination, Is.Not.Null, "AutonomousWorldBotController.CampDestination");
            ConstructorInfo make = destination.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(c => c.GetParameters().Length > 1);
            object[] values = make.GetParameters().Select(p => (object)(p.Name switch
            {
                "Id" => keepId,
                "MonsterName" => "Keep (rehearsal)",
                "ZoneName" => "#572 ram crews (rehearsal)",
                "RegionId" => (object)region.ID,
                "X" => door.X,
                "Y" => door.Y,
                "Z" => door.Z,
                "LiveMobCount" => 1,
                "IsDungeon" => false,
                "IsFrontier" => true,
                _ => p.HasDefaultValue ? p.DefaultValue : null,
            })).ToArray();
            SetField(controller, "_rvrDestination", make.Invoke(values));
            SetField(controller, "_rvrIntent", AutonomousRvrEventLayer.Intent.AssaultKeep);
            return controller;
        }

        /// <summary>The harness's stand-in server gets a keep manager that knows no keeps (a walk order looks for a friendly
        /// keep door on its leg; the stand-in server has no keep manager of its own). Only this world's server is changed.</summary>
        private static void NoKeeps()
        {
            FieldInfo keeps = typeof(GameServer).GetField("m_keepManager", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(keeps, Is.Not.Null, "GameServer.m_keepManager");
            if (keeps.GetValue(GameServer.Instance) == null)
                keeps.SetValue(GameServer.Instance, DispatchProxy.Create<IKeepManager, RvrFightWorld.NothingStored>());
        }

        /// <summary>The autonomous operator's turn at its engine (the end of TryRunSiegeJob).</summary>
        private static bool OperatorTurn(AutonomousWorldBotController controller, GameBot op, GameSiegeRam ram, GameLiving door, string keepId)
        {
            SetField(controller, "_siegeJobKeep", keepId);
            SetField(controller, "_siegeLastProgress", GameLoop.GameLoopTime);
            ram.SiegeWeaponTimer?.Stop(); // the arming that taking control started is over: the operator aims the ram now
            return Call(controller, "OperateEngine", op, ram, door, true, GameLoop.GameLoopTime);
        }

        /// <summary>A gamebot's siege turn (TryRunSiegeJob): its own job first, else the ram crew.</summary>
        private static bool SiegeTurn(AutonomousWorldBotController controller, GameBot bot) => Call(controller, "TryRunSiegeJob", bot);

        private static void Later(long milliseconds) =>
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, GameLoop.GameLoopTime + milliseconds);

        /// <summary>Crew for the controller cases: Infiltrators, melee who never take a siege job (AutonomousSiegeJobs.CanOperate),
        /// so their siege turn goes straight to the ram crew, as an operator's does once no engine is left for it.</summary>
        private static GameBot CrewBot(RvrFightWorld world, Region region, string name, int x, int y, string objective = "RvR", byte level = 50)
        {
            GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Infiltrator, level, name, objective);
            Assert.That(world.Place(bot, region, x, y), Is.True);
            return bot;
        }

        private static List<GameBot> Melee(RvrFightWorld world, Region region, int count, string tag, int x, int y)
        {
            var bots = new List<GameBot>();
            for (int i = 0; i < count; i++)
            {
                GameBot bot = world.NewBot(eRealm.Albion, i % 2 == 0 ? eCharacterClass.Armsman : eCharacterClass.Mercenary, 50, $"{tag}{i:00}");
                Assert.That(world.Place(bot, region, x + i * 30, y), Is.True);
                bots.Add(bot);
            }
            return bots;
        }

        [Test]
        public void TwoRamsAtTheDoorShareTheCrewAndAFullOneTakesNobody()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            GameBot op1 = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op1"), op2 = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op2");
            Assert.That(world.Place(op1, region, X, Y), Is.True);
            Assert.That(world.Place(op2, region, X, Y + 1_000), Is.True);
            StandInDoor door = Door(region, X - 300, Y + 500);
            GameSiegeRam near = Ram(world, region, X + 50, Y, op1, door), far = Ram(world, region, X + 50, Y + 1_000, op2, door);
            string keep = "rvr-keep-572-" + System.Guid.NewGuid();
            AutonomousRamCrews.Register(keep, near);
            AutonomousRamCrews.Register(keep, far);
            Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(2));

            List<GameBot> army = Melee(world, region, 26, "Crew", X - 2_000, Y);
            var seats = new Dictionary<GameSiegeRam, int> { [near] = 0, [far] = 0 };
            int refused = 0;
            foreach (GameBot bot in army)
            {
                if (AutonomousRamCrews.TryTakeSeat(bot, keep, out GameSiegeRam ram, out int seat))
                {
                    seats[ram]++;
                    Assert.That(seat, Is.LessThan(ram.MAX_PASSENGERS));
                    // after every seat the two rams differ by one at most: the emptier ram always fills first
                    Assert.That(System.Math.Abs(AutonomousRamCrews.SeatsTaken(near) - AutonomousRamCrews.SeatsTaken(far)), Is.LessThanOrEqualTo(1));
                }
                else refused++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(seats[near], Is.EqualTo(12), "the nearer ram is full");
                Assert.That(seats[far], Is.EqualTo(12), "the farther ram is full");
                Assert.That(refused, Is.EqualTo(2), "two rams of 12 places seat 24 of 26; the rest batter the door by hand");
            });

            // a seat given up is taken by the next comer
            GameBot leaver = army.First(b => AutonomousRamCrews.KeepSeat(b, out _) == near);
            AutonomousRamCrews.Leave(leaver);
            Assert.That(AutonomousRamCrews.SeatsTaken(near), Is.EqualTo(11));
            Assert.That(AutonomousRamCrews.TryTakeSeat(army[^1], keep, out GameSiegeRam again, out _), Is.True);
            Assert.That(again, Is.SameAs(near));
            foreach (GameBot bot in army) AutonomousRamCrews.Leave(bot);
        }

        [Test]
        public void AFullCrewOnItsSpotsDrivesTheRamAsTwelvePlaces()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            StandInDoor keepDoor = Door(region, X - 400, Y);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op, keepDoor);
            string keep = "rvr-keep-572-" + System.Guid.NewGuid();
            AutonomousRamCrews.Register(keep, ram);
            Vector2 door = new(keepDoor.X, keepDoor.Y);
            Assert.That(ram.Crew, Is.EqualTo(0), "the operator alone is no crew");

            // each crew member takes its seat, then stands on its spot behind the ram (as TryCrewRam walks it there)
            var crew = new List<GameBot>();
            for (int i = 0; i < 12; i++)
            {
                // seats are handed out lowest first, so the i-th crew member gets seat i: it stands on that spot
                GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, $"Seat{i:00}");
                Vector2 spot = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), door, i);
                Assert.That(world.Place(bot, region, (int)spot.X, (int)spot.Y), Is.True);
                Assert.That(AutonomousRamCrews.TryTakeSeat(bot, keep, out GameSiegeRam taken, out int seat), Is.True);
                Assert.That((taken, seat), Is.EqualTo((ram, i)));
                crew.Add(bot);
            }
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(crew.All(AutonomousRamCrews.AtSeat), Is.True, "every crew member stands at its seat");
                Assert.That(ram.Crew, Is.EqualTo(12), "the crew on its spots is counted by the ram (GameSiegeRam.Crew)");
                Assert.That(GameSiegeRam.CrewReloadMilliseconds(ram.Level, ram.Crew, ram.MAX_PASSENGERS),
                    Is.LessThan(GameSiegeRam.CrewReloadMilliseconds(ram.Level, 0, ram.MAX_PASSENGERS)), "a crewed ram reloads faster");
                Assert.That(GameSiegeRam.CrewDamage(200, ram.Crew), Is.EqualTo(1400), "and hits as twelve places");
            });
            foreach (GameBot bot in crew) AutonomousRamCrews.Leave(bot);
        }

        [Test]
        public void ADownedRamLosesItsCrew()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op, Door(region, X - 300, Y));
            string keep = "rvr-keep-572-" + System.Guid.NewGuid();
            AutonomousRamCrews.Register(keep, ram);
            GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, "Crew");
            Assert.That(world.Place(bot, region, X + 150, Y), Is.True);
            Assert.That(AutonomousRamCrews.TryTakeSeat(bot, keep, out _, out _), Is.True);
            Assert.That(AutonomousRamCrews.AtSeat(bot), Is.True);
            ram.ReleaseControl();
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRamCrews.Working(ram, eRealm.Albion), Is.False, "no operator: the ram does not work");
                Assert.That(AutonomousRamCrews.AtSeat(bot), Is.False, "the crew member no longer holds a post");
                Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(0));
            });
            AutonomousRamCrews.Leave(bot);
        }

        // ---- the controller's turns ----

        [Test]
        public void TheOperatorsTurnRegistersItsRamAndACrewTurnTakesASeatAndWalksToIt()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            string keep = "rvr-keep-572-" + Guid.NewGuid();
            StandInDoor door = Door(region, X - 300, Y);
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op);
            AutonomousWorldBotController opTurn = Assaulting(keep, region, door);
            Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(0), "no ram before the operator's turn");

            Assert.That(OperatorTurn(opTurn, op, ram, door, keep), Is.True, "the operator works its ram");
            Assert.Multiple(() =>
            {
                Assert.That(ram.TargetObject, Is.SameAs(door), "the operator aims the ram at the door");
                Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(1), "and its turn registers the ram for the crew");
            });

            // a melee gamebot 600 from the ram: its siege turn takes a seat and walks to its spot behind the ram
            GameBot walker = CrewBot(world, region, "Walker", X + 650, Y);
            AutonomousWorldBotController walkerTurn = Assaulting(keep, region, door);
            Assert.That(SiegeTurn(walkerTurn, walker), Is.True, "the crew turn owns the bot's turn");
            var spot = GetField<Vector3?>(walkerTurn, "_crewSpot");
            Vector2 planned = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 0);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRamCrews.KeepSeat(walker, out int seat), Is.SameAs(ram), "a seat at the operator's ram");
                Assert.That(seat, Is.EqualTo(0));
                Assert.That(spot.HasValue, Is.True, "a spot to walk to");
                Assert.That(Vector2.Distance(new(spot!.Value.X, spot.Value.Y), planned), Is.LessThan(60), "the seat's own spot behind the ram");
                Assert.That(walker.PersistentRecord.ObjectivePhase, Is.EqualTo("Joining the ram crew"), "it walks there");
                Assert.That(AutonomousRamCrews.AtSeat(walker), Is.False, "not yet at the ram");
            });

            // a melee gamebot already on its spot stands, faces the door and crews the ram
            Vector2 second = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 1);
            GameBot stander = CrewBot(world, region, "Stander", (int)second.X, (int)second.Y);
            AutonomousWorldBotController standerTurn = Assaulting(keep, region, door);
            Assert.That(SiegeTurn(standerTurn, stander), Is.True);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRamCrews.KeepSeat(stander, out int seat), Is.SameAs(ram));
                Assert.That(seat, Is.EqualTo(1), "the next free seat");
                Assert.That(stander.PersistentRecord.ObjectivePhase, Does.StartWith("Crewing the ram"), "it stands at its seat");
                Assert.That(AutonomousRamCrews.AtSeat(stander), Is.True);
                Assert.That(ram.Crew, Is.EqualTo(1), "the ram counts it (the walker is still on its way)");
            });
            AutonomousRamCrews.Leave(walker);
            AutonomousRamCrews.Leave(stander);
        }

        [Test]
        public void APlayersRamIsNeverRegisteredByAnOperatorTurnButKeepsTheCrewStandingAtIt()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            string keep = "rvr-keep-572-" + Guid.NewGuid();
            StandInDoor door = Door(region, X - 300, Y);
            StandInPlayer player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 50, "Player");
            Assert.That(player.Enter(region, X, Y + 600), Is.True);
            world.Track(player);
            GameSiegeRam playersRam = Ram(world, region, X + 50, Y + 600, player, door);
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam botsRam = Ram(world, region, X + 50, Y, op);
            Assert.That(OperatorTurn(Assaulting(keep, region, door), op, botsRam, door, keep), Is.True);
            Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(1), "the gamebot's ram only");

            // 13 crew turns: the 12 places of the gamebot's ram fill, and nobody is sent to the player's ram
            var crew = new List<GameBot>();
            int seated = 0;
            for (int i = 0; i < 13; i++)
            {
                GameBot bot = CrewBot(world, region, $"Crew{i:00}", X + 1_500 + i * 40, Y - 1_500);
                crew.Add(bot);
                SiegeTurn(Assaulting(keep, region, door), bot);
                GameSiegeRam at = AutonomousRamCrews.KeepSeat(bot, out _);
                Assert.That(at, Is.Not.SameAs(playersRam), $"{bot.Name}: never a seat at the player's ram");
                if (at == botsRam) seated++;
            }
            Assert.That(seated, Is.EqualTo(12), "the gamebot ram's 12 places; the 13th batters the door by hand");

            // #525: a friendly gamebot that stands at the player's ram still crews it
            GameBot beside = CrewBot(world, region, "Beside", X + 150, Y + 600);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRamCrews.AtSeat(beside), Is.False);
                Assert.That(playersRam.Crew, Is.EqualTo(1), "#525: a gamebot standing at the player's ram counts as its crew");
            });
            foreach (GameBot bot in crew) AutonomousRamCrews.Leave(bot);
        }

        [Test]
        public void ADoorThatFellTakesNoCrewAndTheCrewDoesNotComeBack()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            string keep = "rvr-keep-572-" + Guid.NewGuid();
            StandInDoor door = Door(region, X - 300, Y);
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op, door);
            AutonomousRamCrews.Register(keep, ram);
            Vector2 spot = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 0);
            GameBot bot = CrewBot(world, region, "Crew", (int)spot.X, (int)spot.Y);
            AutonomousWorldBotController turn = Assaulting(keep, region, door);
            Assert.That(SiegeTurn(turn, bot), Is.True);
            Assert.That(AutonomousRamCrews.KeepSeat(bot, out _), Is.SameAs(ram), "seated at a closed door");

            // the door breaks; the operator still holds its ram on it (up to a minute), but the crew leaves for good
            door.Standing = false;
            Assert.That(AutonomousRamCrews.AimedAtClosedDoor(ram), Is.False);
            SiegeTurn(turn, bot);
            Assert.That(AutonomousRamCrews.KeepSeat(bot, out _), Is.Null, "the crew leaves the broken door");
            for (int look = 0; look < 12; look++)
            {
                Later(AutonomousRamCrews.LookMilliseconds);
                SiegeTurn(turn, bot);
                Assert.That(AutonomousRamCrews.KeepSeat(bot, out _), Is.Null, $"look {look}: no seat again at a ram on a broken door");
            }
            GameBot late = CrewBot(world, region, "Late", (int)spot.X, (int)spot.Y + 40);
            Assert.That(AutonomousRamCrews.TryTakeSeat(late, keep, out _, out _), Is.False, "nor for a newcomer");

            // an opened door takes none either
            door.Standing = true; door.Shown = eDoorState.Open;
            Assert.That(AutonomousRamCrews.TryTakeSeat(late, keep, out _, out _), Is.False, "an open door takes no crew");
            door.Shown = eDoorState.Closed;
            Assert.That(AutonomousRamCrews.TryTakeSeat(late, keep, out _, out _), Is.True, "a closed, standing door does");
            AutonomousRamCrews.Leave(late);
        }

        [Test]
        public void ASeatedCrewMemberHoldsThroughWallFireButAnswersAMeleeBlow()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            string keep = "rvr-keep-572-" + Guid.NewGuid();
            StandInDoor door = Door(region, X - 300, Y);
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op, door);
            AutonomousRamCrews.Register(keep, ram);
            Vector2 spot = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 0);
            GameBot frontier = CrewBot(world, region, "Frontier", (int)spot.X, (int)spot.Y);
            Vector2 spot2 = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 1);
            // a battleground gamebot is of the bracket's levels (a level 50 is moved to another goal when it loads)
            GameBot battleground = CrewBot(world, region, "Battleground", (int)spot2.X, (int)spot2.Y, "Battleground", 30);
            Assert.That(AutonomousObjectiveAssignments.Is(battleground, eAutonomousObjectiveKind.Battleground), Is.True, "a battleground gamebot");
            GameBot unseated = CrewBot(world, region, "Unseated", X + 2_000, Y + 2_000);
            Assert.That(SiegeTurn(Assaulting(keep, region, door), frontier), Is.True);
            Assert.That(SiegeTurn(Assaulting(keep, region, door), battleground), Is.True);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousWorldBotController.HoldsRamSeat(frontier), Is.True, "a seated frontier crew member holds");
                Assert.That(AutonomousWorldBotController.HoldsRamSeat(battleground), Is.True, "and so does a battleground one");
                Assert.That(AutonomousWorldBotController.HoldsRamSeat(unseated), Is.False);
                // the army defends its crew, and a seated crew member is busy (group invites, rezzing strangers)
                Assert.That(BotSiegeRuntime.HoldingPosition(frontier), Is.True, "a seat is a held siege post");
                Assert.That(BotSiegeRuntime.HoldingPosition(unseated), Is.False);
            });

            // an archer on the wall shoots it: it holds its seat
            Striker archer = Strike(world, region, frontier, 100, 250);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(archer.Z, Is.EqualTo(250), "the archer stands on the wall");
                Assert.That(BotSiegeRuntime.StruckInMeleeReach(frontier), Is.False, "fire from the wall is no melee blow");
                Assert.That(AutonomousWorldBotController.HoldsRamSeat(frontier), Is.True, "it stays on its seat");
            });

            // an enemy at melee reach hits it: it answers the blow (the ordinary fight takes the turn)
            Strike(world, region, frontier, 80, 0);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(BotSiegeRuntime.StruckInMeleeReach(frontier), Is.True);
                Assert.That(AutonomousWorldBotController.HoldsRamSeat(frontier), Is.False, "a melee blow takes it off the seat");
                Assert.That(SiegeTurn(Assaulting(keep, region, door), frontier), Is.False, "its siege turn gives the fight the turn");
            });
            AutonomousRamCrews.Leave(frontier);
            AutonomousRamCrews.Leave(battleground);
        }

        [Test]
        public void TheCrewWaitsForTheArmy() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRamCrews.WaitsForArmy(true, true, false), Is.True, "a battleground wave that gathers holds its crew");
            Assert.That(AutonomousRamCrews.WaitsForArmy(true, false, false), Is.False, "the wave goes in: the crew takes its seats");
            Assert.That(AutonomousRamCrews.WaitsForArmy(false, false, true), Is.True, "a frontier siege rally holds its crew");
            Assert.That(AutonomousRamCrews.WaitsForArmy(false, false, false), Is.False, "the battle is on: the crew takes its seats");
            Assert.That(AutonomousRamCrews.WaitsForArmy(false, true, false), Is.False, "a battleground wave never holds a frontier crew");
        });

        /// <summary>The battleground wave of a realm in a region, as AutonomousBattlegroundWave keeps it (gathering at a
        /// staging point, or going). Removed again by <see cref="NoWave"/>.</summary>
        private static object SetWave(ushort region, eRealm realm, Vector3 staging, bool going)
        {
            Type waves = typeof(AutonomousBattlegroundWave);
            var all = (System.Collections.IDictionary)waves.GetField("Waves", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            Type waveType = waves.GetNestedType("Wave", BindingFlags.NonPublic);
            Assert.That(waveType, Is.Not.Null, "AutonomousBattlegroundWave.Wave");
            object wave = all[(region, realm)] ?? Activator.CreateInstance(waveType, true);
            waveType.GetField("Staging")!.SetValue(wave, (Vector3?)staging);
            waveType.GetField("GatherSince")!.SetValue(wave, GameLoop.GameLoopTime);
            waveType.GetField("Going")!.SetValue(wave, going);
            all[(region, realm)] = wave;
            return wave;
        }

        private static void NoWave(ushort region, eRealm realm) =>
            ((System.Collections.IDictionary)typeof(AutonomousBattlegroundWave)
                .GetField("Waves", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)).Remove((region, realm));

        [Test]
        public void ARamAtAClosedGateSeatsNoCrewWhileTheWaveGathersAndTheCrewOnceItGoes()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            ushort regionId = region.ID;
            string keep = "rvr-keep-572-" + Guid.NewGuid();
            StandInDoor door = Door(region, X - 300, Y);
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op);
            Assert.That(OperatorTurn(Assaulting(keep, region, door), op, ram, door, keep), Is.True, "the operator works its ram meanwhile");
            Assert.That(AutonomousRamCrews.RegisteredRams(keep, eRealm.Albion), Is.EqualTo(1), "a working ram at a closed gate");
            Assert.That(AutonomousRamCrews.AimedAtClosedDoor(ram), Is.True);

            // the realm's battleground gamebots muster 3,500 out (the staging point); one already stood at its seat
            Vector2 seat0 = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), new(door.X, door.Y), 0);
            GameBot seated = CrewBot(world, region, "Seated", (int)seat0.X, (int)seat0.Y, "Battleground", 30);
            var musterers = new List<GameBot>();
            for (int i = 0; i < 6; i++)
                musterers.Add(CrewBot(world, region, $"Muster{i:00}", X + 3_500 + i * 40, Y, "Battleground", 30));
            Assert.That(AutonomousObjectiveAssignments.Is(seated, eAutonomousObjectiveKind.Battleground), Is.True, "battleground gamebots");
            var turns = musterers.ToDictionary(b => b, _ => Assaulting(keep, region, door));
            AutonomousWorldBotController seatedTurn = Assaulting(keep, region, door);
            Assert.That(AutonomousRamCrews.TryTakeSeat(seated, keep, out _, out _), Is.True, "seated before the wave gathered again");
            try
            {
                SetWave(regionId, eRealm.Albion, new Vector3(X + 3_500, Y, 0), going: false);
                Assert.That(AutonomousBattlegroundWave.IsGathering(regionId, eRealm.Albion, out _), Is.True, "the wave gathers");
                for (int look = 0; look < 3; look++)
                {
                    foreach (GameBot bot in musterers)
                        Assert.That(SiegeTurn(turns[bot], bot), Is.False, $"look {look}, {bot.Name}: the wave's turn goes on");
                    Assert.That(SiegeTurn(seatedTurn, seated), Is.False, $"look {look}: the seated one goes back to the muster");
                    Later(AutonomousRamCrews.LookMilliseconds);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(musterers.Concat(new[] { seated }).All(b => AutonomousRamCrews.KeepSeat(b, out _) == null), Is.True,
                        "no crew seated during the muster");
                    Assert.That(AutonomousRamCrews.SeatsTaken(ram), Is.EqualTo(0));
                    Assert.That(musterers.All(b => b.PersistentRecord.ObjectivePhase != "Joining the ram crew"), Is.True,
                        "nobody walks to the gate");
                });

                // the wave goes in: the same ram takes its crew
                SetWave(regionId, eRealm.Albion, new Vector3(X + 3_500, Y, 0), going: true);
                Assert.That(AutonomousBattlegroundWave.IsGathering(regionId, eRealm.Albion, out _), Is.False, "the wave goes");
                foreach (GameBot bot in musterers)
                    Assert.That(SiegeTurn(turns[bot], bot), Is.True, $"{bot.Name}: the crew turn owns the turn");
                Assert.Multiple(() =>
                {
                    Assert.That(musterers.All(b => AutonomousRamCrews.KeepSeat(b, out _) == ram), Is.True, "every musterer crews the ram");
                    Assert.That(AutonomousRamCrews.SeatsTaken(ram), Is.EqualTo(musterers.Count));
                    Assert.That(musterers.All(b => b.PersistentRecord.ObjectivePhase == "Joining the ram crew"), Is.True, "and walks to it");
                });
            }
            finally
            {
                NoWave(regionId, eRealm.Albion);
                foreach (GameBot bot in musterers.Append(seated)) AutonomousRamCrews.Leave(bot);
            }
        }

        [Test]
        public void TheCrewTurnAsksTheArmyBeforeAnySeat()
        {
            string siege = System.Text.RegularExpressions.Regex.Replace(
                Source("bots", "autonomous", "AutonomousWorldBotController.Siege.cs"), @"\s+", " ");
            int crew = siege.IndexOf("private bool TryCrewRam(GameBot bot, long now)", StringComparison.Ordinal);
            int wait = siege.IndexOf("if (RamCrewWaitsForArmy(bot, now)) {", crew, StringComparison.Ordinal);
            int seat = siege.IndexOf("GameSiegeRam ram = AutonomousRamCrews.KeepSeat(bot, out int seat);", crew, StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(crew, Is.GreaterThan(0));
                Assert.That(wait, Is.GreaterThan(crew), "TryCrewRam asks whether the army holds");
                Assert.That(seat, Is.GreaterThan(wait), "before it keeps or takes a seat");
                Assert.That(siege, Does.Contain("LeaveRamCrew(bot); return false; }"), "a held crew leaves its seat and gives the turn back");
                Assert.That(siege, Does.Contain("battleground && AutonomousBattlegroundWave.IsGathering(bot.CurrentRegionID, bot.Realm, out _)"));
                Assert.That(siege, Does.Contain("!battleground && AutonomousRvrEventLayer.GetRallyOrder(RvrForceId(bot), bot.Realm, now) is { } rally && bot.Realm == rally.Attacker"));
            });
        }

        [Test]
        public void TheAiTurnHoldsTheSeatBeforeTheOrdinaryFightInTheFrontierAndTheBattleground()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            int hold = controller.IndexOf("if (HoldsRamSeat(bot))", StringComparison.Ordinal);
            int fight = controller.IndexOf("bool postCombatRecovery = BotRestRecovery.NeedsOrContinuesRest(bot)", StringComparison.Ordinal);
            Assert.That(hold, Is.GreaterThan(0), "the AI turn asks HoldsRamSeat");
            Assert.That(fight, Is.GreaterThan(hold), "before the ordinary fight");
            string branch = controller[hold..fight];
            Assert.That(branch, Does.Contain("ExecuteBattleground(brain,bot)").And.Contain("ExecuteRvr(brain,bot)"),
                "a battleground crew member goes on with its battleground turn, a frontier one with its RvR turn");
        }

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }
    }
}
