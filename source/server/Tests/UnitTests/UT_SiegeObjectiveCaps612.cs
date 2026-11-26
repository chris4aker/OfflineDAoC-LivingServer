using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using Engine = DOL.GS.AutonomousSiegeJobs.EngineAtDoor;

namespace DOL.UnitTests
{
    /// <summary>#612 and #666 (owner 2026-11-26): battleground siege only for an attacker at a closed door, with rams and
    /// trebuchets only; at most 3 standing engines of a realm per door (patch 1.60) in battlegrounds and the frontier;
    /// the player's ram crew kept; refusals logged.</summary>
    [TestFixture, NonParallelizable]
    public class UT_SiegeObjectiveCaps612
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        [SetUp] public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            AutonomousSiegeJobs.Reset();
            AutonomousSiegeBuild.Reset();
        }
        [TearDown] public void Cleanup()
        {
            AutonomousSiegeJobs.Reset();
            AutonomousSiegeBuild.Reset();
            GameServer.LoadTestDouble(_previous);
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override eRealm Realm { get; set; }
            public override ICharacterClass CharacterClass => new DOL.GS.PlayerClass.ClassArmsman();
        }

        private static Bot MakeBot(eRealm realm = eRealm.Albion)
        {
            var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            bot.Realm = realm;
            bot.ObjectState = GameObject.eObjectState.Active;
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, true);
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new PropertyCollection());
            return bot;
        }

        private static Engine Working(eRealm realm = eRealm.Albion) => new(realm, true, true, true);

        // ---- battlegrounds: an objective for every engine ----

        [TestCase(true, true, true, ExpectedResult = true, TestName = "Battleground_AttackerAtClosedDoor_HasSiege")]
        [TestCase(true, true, false, ExpectedResult = false, TestName = "Battleground_AttackerNoDoorLeft_NoSiege")]
        [TestCase(true, false, false, ExpectedResult = false, TestName = "Battleground_RealmHoldingKeep_NoDefensiveEngines")]
        [TestCase(false, true, true, ExpectedResult = true, TestName = "Frontier_AttackerAtDoor_Unchanged")]
        [TestCase(false, true, false, ExpectedResult = true, TestName = "Frontier_AttackerNoDoor_Unchanged")]
        [TestCase(false, false, false, ExpectedResult = true, TestName = "Frontier_Defender_Unchanged")]
        public bool ASiegeJobNeedsAClosedDoorOnlyInABattleground(bool battleground, bool attacking, bool closedDoor) =>
            AutonomousSiegeJobs.HasObjective(battleground, attacking, closedDoor);

        [Test]
        public void InABattlegroundNoEngineJobIsCreatedWithoutAClosedDoor()
        {
            // The controller passes attacking && door != null; false is the holder, or an attacker with no closed door.
            var bot = MakeBot();
            Assert.That(AutonomousSiegeJobs.TryAcquire(bot, "bg-" + Guid.NewGuid(), 64, false, true, out _, out _, 252, battleground: true), Is.False);
            Assert.That(AutonomousSiegeJobs.SlotCount(64, false, true), Is.Zero);
            Assert.That(AutonomousSiegeJobs.KindsFor(64, false, true), Is.Empty);
            // The frontier keeps its defensive places.
            Assert.That(AutonomousSiegeJobs.TryAcquire(bot, "fr-" + Guid.NewGuid(), 64, false, true, out var kind, out _, 163), Is.True);
            Assert.That(kind, Is.AnyOf(BotSiegeKind.Ballista, BotSiegeKind.Catapult));
        }

        [Test]
        public void OnlyRamsAndTrebuchetsAppearInBattlegrounds()
        {
            Assert.That(AutonomousSiegeJobs.KindsFor(64, true, true), Is.EqualTo(new[] { BotSiegeKind.Ram, BotSiegeKind.Ram, BotSiegeKind.Trebuchet }));
            Assert.That(AutonomousSiegeJobs.KindsFor(16, true, true), Is.EqualTo(new[] { BotSiegeKind.Ram, BotSiegeKind.Ram, BotSiegeKind.Trebuchet }));
            Assert.That(AutonomousSiegeJobs.KindsFor(5, true, true), Is.EqualTo(new[] { BotSiegeKind.Ram }));
            Assert.That(AutonomousSiegeJobs.KindsFor(64, true, false), Has.Length.EqualTo(6), "the frontier attacking set is unchanged");
            string keep = "bg-" + Guid.NewGuid();
            var bots = Enumerable.Range(0, 6).Select(_ => MakeBot()).ToArray();
            var got = bots.Select(b => AutonomousSiegeJobs.TryAcquire(b, keep, 100, true, true, out var k, out _, 252,
                BotSiegeKind.Catapult, battleground: true) ? (BotSiegeKind?)k : null).ToArray();
            Assert.That(got.Count(k => k.HasValue), Is.EqualTo(3), "three places: ram, ram, trebuchet");
            Assert.That(got.Where(k => k.HasValue).Select(k => k.Value), Is.All.Matches<BotSiegeKind>(AutonomousSiegeJobs.AllowedInBattleground));
        }

        [Test]
        public void AFrontierCatapultJobIsNotKeptInABattleground()
        {
            var bot = MakeBot();
            string keep = "bg-" + Guid.NewGuid();
            Assert.That(AutonomousSiegeJobs.TryAcquire(bot, keep, 64, true, true, out var first, out _, 252, BotSiegeKind.Catapult), Is.True);
            Assert.That(first, Is.EqualTo(BotSiegeKind.Catapult));
            Assert.That(AutonomousSiegeJobs.TryAcquire(bot, keep, 64, true, true, out var second, out _, 252, BotSiegeKind.Catapult, battleground: true), Is.True);
            Assert.That(AutonomousSiegeJobs.AllowedInBattleground(second), Is.True);
        }

        // ---- the cap: 3 standing engines of a realm per door ----

        [Test]
        public void ARealmNeverHasMoreThanThreeStandingEnginesPerDoor()
        {
            Assert.That(AutonomousSiegeJobs.MaxEnginesPerDoor, Is.EqualTo(3));
            for (int standing = 0; standing <= 5; standing++)
                Assert.That(AutonomousSiegeJobs.DoorCapReached(standing, 0), Is.EqualTo(standing >= 3), $"standing={standing}");
            Assert.That(AutonomousSiegeJobs.DoorCapReached(2, 1), Is.True, "a bot mid-build holds the third place");
            Assert.That(AutonomousSiegeJobs.DoorCapReached(1, 1), Is.False);
            // Only the realm's operated, working, living engines stand.
            var engines = new[]
            {
                Working(), Working(), // two bot or player engines at work
                new Engine(eRealm.Albion, true, true, false),   // released: frees its place
                new Engine(eRealm.Albion, false, true, true),   // destroyed
                new Engine(eRealm.Albion, true, false, true),   // decayed past use
                Working(eRealm.Midgard), Working(eRealm.Midgard), Working(eRealm.Midgard), // the enemy's own count
            };
            Assert.That(AutonomousSiegeJobs.StandingAtDoor(engines, eRealm.Albion), Is.EqualTo(2));
            Assert.That(AutonomousSiegeJobs.StandingAtDoor(engines, eRealm.Midgard), Is.EqualTo(3));
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(engines, eRealm.Midgard), 0), Is.True);
        }

        [Test]
        public void AnEngineOutsideTheWorldOrUnoperatedDoesNotStand()
        {
            var ram = new GameSiegeRam();
            Assert.That(AutonomousSiegeJobs.Standing(AutonomousWorldBotController.DoorCapFact(ram), eRealm.None), Is.False);
            Assert.That(AutonomousWorldBotController.StandingAtDoor(eRealm.None, new GameSiegeWeapon[] { ram, null }), Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OtherBotsMidBuildCountAgainstTheDoorInBothBattlegroundsAndTheFrontier(bool battleground)
        {
            string keep = "cap-" + Guid.NewGuid();
            ushort region = battleground ? (ushort)252 : (ushort)163;
            var a = MakeBot(); var b = MakeBot(); var c = MakeBot(); var enemy = MakeBot(eRealm.Midgard);
            foreach (var bot in new[] { a, b, c })
                Assert.That(AutonomousSiegeJobs.TryAcquire(bot, keep, 64, true, true, out _, out _, region, battleground: battleground), Is.True);
            Assert.That(AutonomousSiegeJobs.TryAcquire(enemy, keep, 64, true, true, out _, out _, region, battleground: battleground), Is.True);
            // The bots are not in a region in a unit test, so OthersBuilding is asked for the region the jobs were taken in.
            AutonomousSiegeJobs.MarkBuilding(a, true);
            AutonomousSiegeJobs.MarkBuilding(b, true);
            AutonomousSiegeJobs.MarkBuilding(enemy, true);
            Assert.That(AutonomousSiegeJobs.OthersBuilding(c, region, keep), Is.EqualTo(2), "the enemy's build is its own");
            Assert.That(AutonomousSiegeJobs.OthersBuilding(a, region, keep), Is.EqualTo(1), "a bot never counts itself");
            // One standing engine and two builds: the third bot does not start a fourth.
            Assert.That(AutonomousSiegeJobs.DoorCapReached(1, AutonomousSiegeJobs.OthersBuilding(c, region, keep)), Is.True);
            AutonomousSiegeJobs.MarkBuilding(b, false);
            Assert.That(AutonomousSiegeJobs.DoorCapReached(1, AutonomousSiegeJobs.OthersBuilding(c, region, keep)), Is.False);
            AutonomousSiegeJobs.Release(a);
            Assert.That(AutonomousSiegeJobs.OthersBuilding(c, region, keep), Is.Zero, "a released job frees its place");
        }

        [Test]
        public void TheStockDoesNotRefillPastTheCapWithinOneSiege()
        {
            const ushort region = 252; const string keep = "rvr-keep-stock";
            // A large army's stock is far above the cap, and an idle gap gives a fresh stock, yet the door still takes 3.
            int left = AutonomousSiegeBuild.Remaining(region, keep, eRealm.Albion, 100, 0, out int stock);
            Assert.That(stock, Is.GreaterThan(AutonomousSiegeJobs.MaxEnginesPerDoor));
            Assert.That(left, Is.EqualTo(stock));
            var standing = new[] { Working(), Working(), Working() };
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(standing, eRealm.Albion), 0), Is.True);
            long later = AutonomousSiegeBuild.StockIdleResetMilliseconds + 60_000;
            Assert.That(AutonomousSiegeBuild.Remaining(region, keep, eRealm.Albion, 100, later, out _), Is.EqualTo(stock), "fresh stock");
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(standing, eRealm.Albion), 0), Is.True,
                "standing engines are counted, so the stock never puts a 4th on the door");
            // One engine destroyed frees one place, and only one.
            standing[0] = standing[0] with { Alive = false };
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(standing, eRealm.Albion), 0), Is.False);
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(standing, eRealm.Albion), 1), Is.True);
        }

        // ---- kept: the player's ram and its bot crew ----

        [Test]
        public void ThePlayersRamCrewStillWorksAndCountsTowardTheDoor()
        {
            var crew = Enumerable.Range(0, 12).Select(_ => (GameBot)MakeBot()).ToArray();
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            int count = GameSiegeRam.CountCrew(player, eRealm.Albion, 0, crew, 12);
            Assert.That(count, Is.EqualTo(12));
            Assert.That(GameSiegeRam.CrewDamage(200, count), Is.EqualTo(1400));
            Assert.That(GameSiegeRam.CrewReloadMilliseconds(3, count, 12), Is.EqualTo(8000));
            // The player's operated ram stands at the door: with two bot engines the bots stop at 3.
            var atDoor = new[] { Working(), Working(), Working() /* the player's ram */ };
            Assert.That(AutonomousSiegeJobs.DoorCapReached(AutonomousSiegeJobs.StandingAtDoor(atDoor, eRealm.Albion), 0), Is.True);
            // The bots' engine places (#525) still leave a player's engine out.
            Assert.That(AutonomousWorldBotController.CountsAgainstBotEngineCap(new GameSiegeRam()), Is.True);
        }

        // ---- the overnight log ----

        [Test]
        public void ARefusalByTheCapIsLoggedWithTheDoorAndTheCounts()
        {
            string detail = AutonomousSiegeJobs.DoorCapDetail(1101, BotSiegeKind.Trebuchet, 2, 1, true);
            Assert.That(detail, Is.EqualTo("door=1101 kind=Trebuchet standing=2 building=1 cap=3 battleground=true"));
            Assert.That(AutonomousSiegeJobs.DoorCapDetail(7, BotSiegeKind.Ram, 3, 0, false), Does.Contain("battleground=false"));
        }
    }
}
