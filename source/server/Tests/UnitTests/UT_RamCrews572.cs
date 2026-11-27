using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #572 step 2, ram crews on purpose (port of zomboden/OfflineDAoC-True165 433740c6f; owner 2026-11-27: port the seats,
    /// without the "second ram only at 16+ bots" rule and without the woodworking repair). Pure rules, then a siege case in
    /// the fight harness (RvrFightWorld) with real rams and real gamebots. Autonomous gamebots only; the player's ram keeps
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
        /// its crew before anyone stands there), then one tick so the zones file what was placed. No bot gets a second
        /// to walk off its spot.
        /// </summary>
        private static void Settle(RvrFightWorld world)
        {
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, GameLoop.GameLoopTime + 1_000);
            world.Tick();
        }

        private static GameSiegeRam Ram(RvrFightWorld world, Region region, int x, int y, GameLiving op)
        {
            var ram = (GameSiegeRam)AutonomousWorldBotController.CreateSiegeWeapon(BotSiegeKind.Ram);
            ram.Realm = eRealm.Albion;
            Assert.That(world.Place(ram, region, x, y), Is.True, "ram placed");
            Assert.That(ram.TryTakeControl(op), Is.True, "operator in control");
            return ram;
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
            GameSiegeRam near = Ram(world, region, X + 50, Y, op1), far = Ram(world, region, X + 50, Y + 1_000, op2);
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
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op);
            string keep = "rvr-keep-572-" + System.Guid.NewGuid();
            AutonomousRamCrews.Register(keep, ram);
            Vector2 door = new(X - 400, Y);
            Assert.That(ram.Crew, Is.EqualTo(0), "the operator alone is no crew");

            // each crew member takes its seat, then stands on its spot behind the ram (as TryCrewRam walks it there)
            var crew = new List<GameBot>();
            for (int i = 0; i < 12; i++)
            {
                GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, $"Seat{i:00}");
                Assert.That(world.Place(bot, region, X - 3_000, Y + i * 50), Is.True);
                Assert.That(AutonomousRamCrews.TryTakeSeat(bot, keep, out GameSiegeRam taken, out int seat), Is.True);
                Assert.That(taken, Is.SameAs(ram));
                Assert.That(AutonomousRamCrews.AtSeat(bot), Is.False, "not yet at the ram");
                Vector2 spot = AutonomousRamCrews.CrewSpot(new(ram.X, ram.Y), door, seat);
                bot.RemoveFromWorld();
                Assert.That(world.Place(bot, region, (int)spot.X, (int)spot.Y), Is.True);
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
        public void APlayersRamIsNeverASeatButKeepsTheCrewStandingAtIt()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            StandInPlayer player = StandInPlayer.Make(region, eRealm.Albion, eCharacterClass.Armsman, 50, "Player");
            Assert.That(player.Enter(region, X, Y), Is.True);
            world.Track(player);
            GameSiegeRam playersRam = Ram(world, region, X + 50, Y, player);
            string keep = "rvr-keep-572-" + System.Guid.NewGuid();
            // the player's ram is not registered: only an autonomous gamebot operator registers its ram (TryRunSiegeJob)
            GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Mercenary, 50, "Beside");
            Assert.That(world.Place(bot, region, X + 150, Y), Is.True);
            Settle(world);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRamCrews.TryTakeSeat(bot, keep, out _, out _), Is.False, "no seat at a player's ram");
                Assert.That(AutonomousRamCrews.AtSeat(bot), Is.False);
                Assert.That(playersRam.Crew, Is.EqualTo(1), "#525: a friendly gamebot standing at the player's ram still crews it");
            });
        }

        [Test]
        public void ADownedRamLosesItsCrew()
        {
            using var world = new RvrFightWorld(572, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.FrontierRegion, RvrFightRehearsal.FrontierZone, "#572 ram crews (rehearsal)");
            GameBot op = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 50, "Op");
            Assert.That(world.Place(op, region, X, Y), Is.True);
            GameSiegeRam ram = Ram(world, region, X + 50, Y, op);
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
    }
}
