using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// A parked hired companion is saved alive (owner's PC, 2026-11-17, build 5ddd003): /hire dismiss and logout parked
    /// Pannax, a status save written after he left the world stored IsAlive=0 with Health=116, and /hire summon and the
    /// next login answered "Pannax is dead" until a revive. Only a real death needs a healer. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_HiredCompanionParkAlive
    {
        private const long BotId = 5836;
        private const string OwnerId = "owner-testrunner";

        /// <summary>A database that keeps one companion record: reads return it, saves are counted and accepted.</summary>
        public class OneRecordDatabase : DispatchProxy
        {
            public OfflineWorldBotRecord Record;
            public int Saves;

            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type type = method.ReturnType;
                if (method.Name.StartsWith("Save") || method.Name.StartsWith("Add"))
                {
                    Saves++;
                    return type == typeof(bool) ? true : type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
                }
                if (type == typeof(OfflineWorldBotRecord))
                    return Record;
                if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
                return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, OneRecordDatabase>();
        private static OneRecordDatabase Store => (OneRecordDatabase)Db;

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
        private GamePlayer _owner;
        private readonly List<(Logging.Logger Logger, object Previous, BlockingCollection<Logging.LogEntry> Entries)> _logs = new();

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _originalCalculators = calculators.ToArray();
            GameLiving.LoadCalculators();
            // the loggers write into a test queue (no logging system here), as UT_BotAttributeProgression does
            foreach ((Type type, string name) in new[] { (typeof(HiredCompanions165), "Log"), (typeof(GameBot), "log") })
            {
                var logger = (Logging.Logger)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                FieldInfo field = typeof(Logging.Logger).GetField("_queueProcessor", BindingFlags.Instance | BindingFlags.NonPublic);
                object previous = field.GetValue(logger);
                var queue = new Logging.LogEntryQueueProcessor();
                var entries = new BlockingCollection<Logging.LogEntry>();
                typeof(Logging.LogEntryQueueProcessor).GetField("_loggingQueue", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(queue, entries);
                field.SetValue(logger, queue);
                _logs.Add((logger, previous, entries));
            }
            HiredCompanionsMode.Override = true;
            Hidden("ClearForTests");
            Hidden("RegisterForTests", new OfflineHiredCompanionRecord
            {
                BotId = BotId, OwnerCharacterID = OwnerId, OwnerName = "Testrunner", Row = 0, WithOwner = true,
            });
            _owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            _owner.InternalID = OwnerId;
            Store.Record = null;
            Store.Saves = 0;
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
            foreach (var entry in _logs)
            {
                typeof(Logging.Logger).GetField("_queueProcessor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(entry.Logger, entry.Previous);
                entry.Entries.Dispose();
            }
            _logs.Clear();
            Hidden("ClearForTests");
            HiredCompanionsMode.Override = null;
            Store.Record = null;
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Array.Copy(_originalCalculators, calculators, calculators.Length);
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        /// <summary>Pannax as the hire made him: a level 5 Briton Armsman with a persistent record.</summary>
        private GameBot Pannax(out OfflineWorldBotRecord record)
        {
            record = new OfflineWorldBotRecord
            {
                BotId = BotId, Name = "Pannax", Realm = (int)eRealm.Albion, ClassId = (int)eCharacterClass.Armsman,
                ClassName = "Armsman", RaceId = (int)eRace.Briton, RaceName = "Briton", Level = 5, Health = 116, IsAlive = true,
            };
            var bot = new GameBot(record);
            _constructed.Add(bot);
            return bot;
        }

        /// <summary>What the database holds after a status save: a copy of the snapshot, as the next Record() read sees it.</summary>
        private static OfflineWorldBotRecord SaveSnapshot(GameBot bot)
        {
            OfflineWorldBotRecord snapshot = bot.PrepareAutonomousStateSnapshot();
            Assert.That(snapshot, Is.Not.Null, "a hired companion is a persistent bot");
            OfflineWorldBotRecord saved = JsonSerializer.Deserialize<OfflineWorldBotRecord>(JsonSerializer.Serialize(snapshot));
            Store.Record = saved;
            return saved;
        }

        private string Summon() => HiredCompanions165.Summon(_owner, 0);

        [Test]
        public void TheSavedAliveRule() => Assert.Multiple(() =>
        {
            Assert.That(GameBot.SavedAlive(inWorld: true, aliveInWorld: true, health: 116), Is.True, "alive in the world");
            Assert.That(GameBot.SavedAlive(inWorld: true, aliveInWorld: false, health: 0), Is.False, "dead in the world");
            Assert.That(GameBot.SavedAlive(inWorld: true, aliveInWorld: false, health: 50), Is.False,
                "in the world the game's own IsAlive decides (respawning, reaper)");
            Assert.That(GameBot.SavedAlive(inWorld: false, aliveInWorld: false, health: 116), Is.True,
                "parked (out of the world) with health: alive - the 5ddd003 bug saved this as dead");
            Assert.That(GameBot.SavedAlive(inWorld: false, aliveInWorld: false, health: 0), Is.False, "a faded body stays dead");
        });

        [Test]
        public void DismissThenSummonFindsHimAlive()
        {
            GameBot bot = Pannax(out _);
            bot.ObjectState = GameObject.eObjectState.Active;   // summoned, following his player
            Assert.That(bot.IsAlive, Is.True);
            Assert.That(SaveSnapshot(bot).IsAlive, Is.True, "the save while he follows");

            // /hire dismiss 1: Park removes him from the world; a status save queued earlier is written afterwards
            Assert.That(HiredCompanions165.Dismiss(_owner, 0), Is.Null);
            Assert.That(HiredCompanions165.HiredRecord(bot).WithOwner, Is.False, "dismissed: waits until summoned");
            bot.ObjectState = GameObject.eObjectState.Inactive;
            Assert.That(bot.IsAlive, Is.False, "GameLiving.IsAlive is false out of the world (the cause)");
            OfflineWorldBotRecord saved = SaveSnapshot(bot);
            Assert.That(saved.IsAlive, Is.True, "parked alive, not dead");
            Assert.That(saved.IsOnline, Is.False);
            Assert.That(saved.Health, Is.GreaterThan(0));

            // /hire summon 1: not refused as dead (here the world itself is absent, so it fails later, at joining)
            string reply = Summon();
            Assert.That(reply, Does.Not.Contain("is dead"));
            Assert.That(HiredCompanions165.Revive(_owner, 0), Is.EqualTo("Pannax is alive."), "no revive needed");
        }

        [Test]
        public void LogoutThenLoginFindsHimAlive()
        {
            GameBot bot = Pannax(out _);
            bot.ObjectState = GameObject.eObjectState.Active;
            SaveSnapshot(bot);

            // logout: OnPlayerQuit parks him to come back at the next login (WithOwner stays true)
            HiredCompanions165.HiredRecord(bot).WithOwner = true;
            bot.ObjectState = GameObject.eObjectState.Inactive;
            OfflineWorldBotRecord saved = SaveSnapshot(bot);
            Assert.That(saved.IsAlive, Is.True, "parked at logout alive");
            Assert.That(HiredCompanions165.HiredRecord(bot).WithOwner, Is.True, "login summons him");

            // login: OnPlayerEntered summons every companion WithOwner; the summon must not stop at "dead"
            Assert.That(Summon(), Does.Not.Contain("is dead"));

            // and the record a later login reads back builds a living companion again
            var again = new GameBot(saved);
            _constructed.Add(again);
            Assert.That(again.Health, Is.GreaterThan(0));
        }

        [Test]
        public void ARealDeathStillNeedsTheHealer()
        {
            GameBot bot = Pannax(out _);
            bot.ObjectState = GameObject.eObjectState.Active;
            bot.Health = 0;   // killed: the body lies in the world
            Assert.That(bot.IsAlive, Is.False);
            Assert.That(SaveSnapshot(bot).IsAlive, Is.False, "dead while the body lies there");

            // the body fades (DeathTick parks it): out of the world with no health, still dead
            bot.ObjectState = GameObject.eObjectState.Inactive;
            OfflineWorldBotRecord saved = SaveSnapshot(bot);
            Assert.That(saved.IsAlive, Is.False, "a faded body stays dead");
            Assert.That(Summon(), Is.EqualTo("Pannax is dead. Any healer of your realm brings them back, free."));

            // /hire revive 1 beside a healer: alive again, saved, then summoned (which fails here only at joining the world)
            string revived = HiredCompanions165.Revive(_owner, 0);
            Assert.That(Store.Record.IsAlive, Is.True, "the revive saves him alive");
            Assert.That(Store.Saves, Is.GreaterThan(0));
            Assert.That(revived, Is.Null.Or.StartsWith("Pannax lives again."));
            Assert.That(Summon(), Does.Not.Contain("is dead"));
        }
    }
}
