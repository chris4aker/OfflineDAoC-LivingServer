using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Raid learning (Bots as people WP2R): which party members the learning counts, read from real GameBots. A saved
    /// gamebot remembers; an Epic Special Force counts as having seen everything and remembers nothing; a hired companion and
    /// a No Lifer's buffbot are not counted; a real player keeps the raid at the record's familiarity. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidLearningSeats
    {
        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, UT_HiredCompanionParkAlive.OneRecordDatabase>();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => Db;
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
        }

        private static void Hidden(string method, params object[] args) =>
            typeof(HiredCompanions165).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);

        private GameServer _previous;
        private PetTestLanguageScope _language;
        private IPropertyCalculator[] _originalCalculators;
        private readonly List<GameBot> _constructed = new();
        private Logging.Logger _logger;
        private object _previousQueue;
        private BlockingCollection<Logging.LogEntry> _entries;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _originalCalculators = calculators.ToArray();
            GameLiving.LoadCalculators();
            // GameBot's logger writes into a test queue (no logging system here), as UT_HiredCompanionParkAlive does.
            _logger = (Logging.Logger)typeof(GameBot).GetField("log", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FieldInfo field = typeof(Logging.Logger).GetField("_queueProcessor", BindingFlags.Instance | BindingFlags.NonPublic);
            _previousQueue = field.GetValue(_logger);
            var queue = new Logging.LogEntryQueueProcessor();
            _entries = new BlockingCollection<Logging.LogEntry>();
            typeof(Logging.LogEntryQueueProcessor).GetField("_loggingQueue", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(queue, _entries);
            field.SetValue(_logger, queue);
            HiredCompanionsMode.Override = true;
            Hidden("ClearForTests");
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameBot bot in _constructed)
            {
                if (bot.castingComponent != null) ServiceObjectStore.Remove(bot.castingComponent);
                if (bot.effectListComponent != null) ServiceObjectStore.Remove(bot.effectListComponent);
                if (bot.movementComponent != null) ServiceObjectStore.Remove(bot.movementComponent);
            }
            _constructed.Clear();
            typeof(Logging.Logger).GetField("_queueProcessor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_logger, _previousQueue);
            _entries.Dispose();
            Hidden("ClearForTests");
            HiredCompanionsMode.Override = null;
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Array.Copy(_originalCalculators, calculators, calculators.Length);
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        private static OfflineWorldBotRecord Record(long id) => new()
        {
            BotId = id, Name = "Raider" + id, Realm = (int)eRealm.Albion, ClassId = (int)eCharacterClass.Armsman, ClassName = "Armsman",
            RaceId = (int)eRace.Briton, RaceName = "Briton", Level = 50, Health = 1200, IsAlive = true,
        };

        private GameBot Keep(GameBot bot)
        {
            _constructed.Add(bot);
            return bot;
        }

        [Test]
        public void TheLearningCountsGamebotsAndTheSpecialForcesOnly()
        {
            GameBot raider = Keep(new GameBot(Record(4101)));
            GameBot esf = Keep(GameBot.CreateEpicSpecialForce(Record(AutonomousEpicSpecialForces.FirstBotId + 7)));
            GameBot buffbot = Keep(GameBot.CreateNoLiferBuffBot(Record(4102), 4101));
            Hidden("RegisterForTests", new OfflineHiredCompanionRecord { BotId = 4103, OwnerCharacterID = "owner", OwnerName = "Owner", Row = 0, WithOwner = true });
            GameBot hired = Keep(new GameBot(Record(4103)));
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

            Assert.Multiple(() =>
            {
                Assert.That(RaidLearning.SeatOf(raider, out var learner), Is.EqualTo(RaidLearning.Seat.Raider));
                Assert.That(learner, Is.EqualTo(new RaidLearning.Learner(4101, BotPersonKey.Of(4101L), false)));
                Assert.That(RaidLearning.SeatOf(esf, out var force), Is.EqualTo(RaidLearning.Seat.SpecialForce));
                Assert.That(force.SpecialForce && force.Person.IsNone, Is.True, "a Special Force has no person: no row");
                Assert.That(RaidLearning.SeatOf(buffbot, out _), Is.EqualTo(RaidLearning.Seat.Skip), "a buffbot");
                Assert.That(hired.IsHiredCompanion, Is.True);
                Assert.That(RaidLearning.SeatOf(hired, out _), Is.EqualTo(RaidLearning.Seat.Skip), "a hired companion");
                Assert.That(RaidLearning.SeatOf(player, out _), Is.EqualTo(RaidLearning.Seat.Player), "a real player");
                Assert.That(RaidLearning.SeatOf(null, out _), Is.EqualTo(RaidLearning.Seat.Skip));
            });

            // The call's raider loop with the seeding done: a raider the call counted gets its own familiarity; any other
            // bot keeps the raid's.
            RaidLearning.ResetForTests();
            try
            {
                const string raidId = "dungeon-albion-crystal", mechanic = "Xanxicar/blast";
                var store = new BotPeopleStore(() => null);
                RaidLearning.SeatOf(raider, out var counted);
                RaidLearning.SeatOf(esf, out var veteran);
                double raid = RaidLearning.AtCall(store, true, raidId, mechanic, 1, 2, 0.8, false, [counted, veteran], DateTime.UtcNow);
                Assert.That(raid, Is.EqualTo(RaidDiscipline.RosterFamiliarity(0.8, 0.5)).Within(1e-12), "one of two has seen it (the Special Force)");
                Assert.That(store.MechanicsSeen.Count, Is.EqualTo(1), "only the gamebot remembers");
                Assert.That(RaidLearning.FamiliarityOf(raider, raidId, mechanic, 2, raid),
                    Is.LessThan(RaidLearning.FamiliarityOf(esf, raidId, mechanic, 2, raid)), "the newcomer below the veteran");
                Assert.That(RaidLearning.FamiliarityOf(hired, raidId, mechanic, 2, raid), Is.EqualTo(raid), "not counted: the raid's");
                Assert.That(RaidLearning.FamiliarityOf(null, raidId, mechanic, 2, raid), Is.EqualTo(raid));
            }
            finally
            {
                RaidLearning.ResetForTests(RaidLearning.SeedingState.Pending);
            }
        }
    }
}
