using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>#831 (owner decision 64A): the #612 door cap's race. rc.2, rvr-keep-53: two bots passed the cap check while
    /// walking to their spots and both started a build (standing=2 building=2 cap=3). The admission and the building mark
    /// are now one step, and a builder holds its place until its engine stands. The cap itself is unchanged (3).</summary>
    [TestFixture, NonParallelizable]
    public class UT_DoorCapRace831
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        private const ushort Region = 252;

        [SetUp] public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            AutonomousSiegeJobs.Reset();
        }
        [TearDown] public void Cleanup()
        {
            AutonomousSiegeJobs.Reset();
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

        private static Bot[] WithJobs(string keep, int count, eRealm realm = eRealm.Albion)
        {
            var bots = Enumerable.Range(0, count).Select(_ => MakeBot(realm)).ToArray();
            foreach (var bot in bots)
                Assert.That(AutonomousSiegeJobs.TryAcquire(bot, keep, 64, true, true, out _, out _, Region), Is.True, "a job place");
            return bots;
        }

        [Test]
        public void TwoBuildsStartingAtOnceCantBothTakeTheLastPlace()
        {
            string keep = "race-" + Guid.NewGuid();
            var bots = WithJobs(keep, 2);
            // rc.2, rvr-keep-53: two standing engines and one place left. Both bots passed the turn's check while walking
            // (neither was building yet); at their spots only one may start.
            Assert.That(AutonomousSiegeJobs.DoorCapReached(2, AutonomousSiegeJobs.OthersBuilding(bots[0], Region, keep)), Is.False);
            Assert.That(AutonomousSiegeJobs.DoorCapReached(2, AutonomousSiegeJobs.OthersBuilding(bots[1], Region, keep)), Is.False);
            bool first = AutonomousSiegeJobs.TryStartBuilding(bots[0], Region, keep, 2, out int before);
            bool second = AutonomousSiegeJobs.TryStartBuilding(bots[1], Region, keep, 2, out int seen);
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.True, "the first takes the third place");
                Assert.That(before, Is.Zero);
                Assert.That(second, Is.False, "the second is refused: 2 standing and 1 building is the cap");
                Assert.That(seen, Is.EqualTo(1), "the refusal's row shows the other build");
                Assert.That(AutonomousSiegeJobs.OthersBuilding(bots[1], Region, keep), Is.EqualTo(1), "nothing marked for the refused bot");
                Assert.That(AutonomousSiegeJobs.OthersBuilding(bots[0], Region, keep), Is.Zero);
            });
        }

        [Test]
        public void ConcurrentStartsNeverAdmitMoreThanTheCap()
        {
            for (int round = 0; round < 25; round++)
            {
                AutonomousSiegeJobs.Reset();
                string keep = "race-" + Guid.NewGuid();
                // Six job places for an army at a door: more bots than the door takes.
                var bots = WithJobs(keep, 6);
                using var start = new Barrier(bots.Length);
                bool[] admitted = new bool[bots.Length];
                // Dedicated threads (a thread-pool loop may not run them all at once, and the barrier would then wait forever).
                var threads = Enumerable.Range(0, bots.Length).Select(i => new Thread(() =>
                {
                    start.SignalAndWait();
                    admitted[i] = AutonomousSiegeJobs.TryStartBuilding(bots[i], Region, keep, 0, out _);
                })).ToArray();
                foreach (var thread in threads) thread.Start();
                foreach (var thread in threads) Assert.That(thread.Join(TimeSpan.FromSeconds(30)), Is.True, "thread finished");
                Assert.That(admitted.Count(a => a), Is.EqualTo(AutonomousSiegeJobs.MaxEnginesPerDoor), $"round {round}");
            }
        }

        [Test]
        public void TheStandingEnginesAndTheBuildsShareTheCap()
        {
            string keep = "race-" + Guid.NewGuid();
            var bots = WithJobs(keep, 4);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[0], Region, keep, 1, out _), Is.True);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[1], Region, keep, 1, out _), Is.True);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[2], Region, keep, 1, out int building), Is.False, "1 + 2 = 3");
            Assert.That(building, Is.EqualTo(2));
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[0], Region, keep, 1, out _), Is.True, "an admitted build keeps its place");
            // A build's place passes to its engine: the mark clears and the engine stands.
            AutonomousSiegeJobs.MarkBuilding(bots[0], false);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[3], Region, keep, 2, out _), Is.False, "2 standing + 1 building");
            // A released job frees its place.
            AutonomousSiegeJobs.Release(bots[1]);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(bots[3], Region, keep, 2, out _), Is.True);
        }

        [Test]
        public void ARealmsBuildsAreItsOwnAndABotWithoutAJobIsNeverAdmitted()
        {
            string keep = "race-" + Guid.NewGuid();
            var albion = WithJobs(keep, 3);
            var midgard = WithJobs(keep, 1, eRealm.Midgard);
            foreach (var bot in albion)
                Assert.That(AutonomousSiegeJobs.TryStartBuilding(bot, Region, keep, 0, out _), Is.True);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(midgard[0], Region, keep, 0, out int others), Is.True, "Midgard's door count is its own");
            Assert.That(others, Is.Zero);
            Assert.That(AutonomousSiegeJobs.TryStartBuilding(MakeBot(), Region, keep, 0, out _), Is.False, "no job here, no build");
        }

        [Test]
        public void TheControllerAdmitsAtTheBuildsStartAndHoldsThePlaceUntilTheSpawn()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repository root");
            string siege = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous",
                "AutonomousWorldBotController.Siege.cs")).Replace("\r\n", "\n");
            int admit = siege.IndexOf("AutonomousSiegeJobs.TryStartBuilding(bot, bot.CurrentRegionID, _siegeJobKeep, standing, out int building)", StringComparison.Ordinal);
            int started = siege.IndexOf("_siegeBuildStarted = now; _siegeBuildKind = _siegeKind;", StringComparison.Ordinal);
            int spawn = siege.IndexOf("owned = CreateSiegeWeapon(_siegeKind);", StringComparison.Ordinal);
            int cleared = siege.IndexOf("AutonomousSiegeJobs.MarkBuilding(bot, false);\n                }", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(admit, Is.GreaterThanOrEqualTo(0), "the build's start goes through the one admission");
                Assert.That(started, Is.GreaterThan(admit), "admitted before the build starts");
                Assert.That(siege, Does.Contain("{ _siegeBuildStarted = 0; _siegeBuildLastWorked = 0; _siegeBuildSpot = null; return 1; }"),
                    "a finished build keeps its mark until the spawn");
                Assert.That(cleared, Is.GreaterThan(spawn), "the mark clears after the spawn, in the spawn's finally");
                Assert.That(siege, Does.Contain("lock (AutonomousSiegeOwnership.ChangeGate)\n                try"), "the spawn holds the lock");
                Assert.That(siege.Split("DoorCapReachedNow(bot, door, out int standingNow, out int buildingNow)").Length - 1, Is.EqualTo(2),
                    "the spawn and the reclaim count the other builds");
                Assert.That(siege, Does.Contain("building = AutonomousSiegeJobs.OthersBuilding(bot, bot.CurrentRegionID, _siegeJobKeep);"));
                Assert.That(AutonomousSiegeJobs.MaxEnginesPerDoor, Is.EqualTo(3), "the cap is unchanged");
            });
        }
    }
}
