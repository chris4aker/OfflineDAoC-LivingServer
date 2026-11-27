using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.Language;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// The RvR fight rehearsal's world (#520, docs/rvr-audit-1.2.3/evidence.md F2, F6 and section 5.2): one real
    /// <see cref="Region"/> with one flat stand-in zone, real gamebots (<see cref="GameBot"/> built from a record, with
    /// their real class, stats, specializations and property calculators), and the game loop's own services ticked in
    /// the loop's order (<c>GameLoop.BuildTickSequence</c>) on this thread, 30 ticks a simulated second. The brains are
    /// the bots' own (<see cref="BotBrain.Think"/> through <see cref="NpcService"/>), and damage, crowd control and
    /// interrupts go through the server's own spell handlers, attack component and effect list.
    ///
    /// What is stood in, because CI has no world database and no navmesh:
    /// - the database: reads find nothing, writes are accepted and dropped;
    /// - the zone and its navmesh: a flat 65,536-unit square under a flat stand-in navmesh (<see cref="FlatNavmesh"/>): paths
    ///   are straight lines and line of sight is always clear;
    /// - the spells: the world database's spell table is not in CI, so each class gets a short era-shaped kit
    ///   (<see cref="RvrFightKits"/>): heals, a direct-damage nuke, the crowd controllers' mez; the handlers are the server's;
    /// - the server settings: every ServerProperty at its shipped (attribute) default, plus what a scenario sets;
    /// - the frontier scan and the coordinator: see <see cref="RvrFightScan"/> (the controller's scan is private, needs a
    ///   navmesh and rolls Random.Shared, so the rehearsal scans with the same public rule and seeded rolls).
    /// The world never writes to disk and restores every static it changed when disposed.
    /// </summary>
    public sealed class RvrFightWorld : IDisposable
    {
        public const int TicksPerSecond = 30;
        public const long Start = 1_000_000;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => Db;
            protected override IServerRules ServerRulesImpl => Rules;
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, NothingStored>();
        private static readonly IServerRules Rules = new NormalServerRules();

        /// <summary>The stand-in database: reads find nothing, saves and adds succeed and keep nothing.</summary>
        public class NothingStored : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type type = method.ReturnType;
                if (type == typeof(void))
                    return null;
                if (type == typeof(bool))
                    return !method.Name.StartsWith("Select") && !method.Name.StartsWith("Find");
                if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                {
                    Type item = type.GetGenericArguments()[0];
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
                }
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }

        private static readonly PropertyInfo Clock = typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime));
        private static readonly FieldInfo ThreadPoolField = typeof(GameLoop).GetField("_threadPool", HiddenStatic);
        private static readonly FieldInfo TickDurationField = typeof(GameLoop).GetField("<TickDuration>k__BackingField", HiddenStatic);

        private readonly GameServer _previousServer;
        private readonly long _previousClock;
        private readonly object _previousPool;
        private readonly double _previousTickDuration;
        private readonly SynchronizationContext _previousContext;
        private readonly IPropertyCalculator[] _previousCalculators;
        private readonly Dictionary<FieldInfo, object> _previousProperties = new();
        private readonly Dictionary<ushort, Region> _previousRegions = new();
        private readonly List<ushort> _addedRegions = new();
        private readonly object _previousTranslations;
        private readonly string _previousLanguage;
        private readonly Util.SeededRandomScope _random;
        private readonly IPathfindingMgr _previousNav;
        private readonly List<Action> _undo = new();
        private readonly List<GameLiving> _livings = new();
        private readonly ErrorCapture _errors;
        private long _nextBotId;

        public int Seed { get; }
        /// <summary>The flat stand-in navmesh (a scenario may stand walls on it that block line of sight).</summary>
        public FlatNavmesh Nav { get; private set; }
        public Region Region { get; private set; }
        public IReadOnlyList<GameLiving> Livings => _livings;
        public RvrFightRecorder Recorder { get; }
        public long Now => GameLoop.GameLoopTime;
        public long Elapsed => GameLoop.GameLoopTime - Start;

        /// <summary>Every error line logged while the world lived.</summary>
        public IReadOnlyList<string> Errors => _errors.Lines;

        /// <summary>Errors a service caught: the entity that threw was removed from the world (GameServiceUtils).</summary>
        public int ServiceErrors => _errors.Lines.Count(line => line.StartsWith("Critical error encountered", StringComparison.Ordinal));

        public RvrFightWorld(int seed, IReadOnlyDictionary<string, object> settings = null)
        {
            Seed = seed;
            _nextBotId = 9_520_000_000L + seed * 1_000L;
            _previousServer = GameServer.Instance;
            _previousClock = GameLoop.GameLoopTime;
            _previousPool = ThreadPoolField.GetValue(null);
            _previousTickDuration = GameLoop.TickDuration;
            _previousContext = SynchronizationContext.Current;
            var server = (Server)RuntimeHelpers.GetUninitializedObject(typeof(Server));
            typeof(GameServer).GetField("m_database", Hidden)?.SetValue(server, Db);
            try
            {
                typeof(DOL.Network.BaseServer).GetField("<Configuration>k__BackingField", Hidden)?.SetValue(server, new GameServerConfiguration());
            }
            catch (Exception)
            {
                // without a configuration only the code that reads the server type differs (none the fight reaches)
            }
            GameServer.LoadTestDouble(server);
            // the NPC manager's ambient texts, which AddToWorld reads (empty: the database holds none)
            typeof(GameServer).GetProperty(nameof(GameServer.NpcManager)).SetValue(server, new NpcManager(server));

            PropertyInfo translations = typeof(LanguageMgr).GetProperty(nameof(LanguageMgr.Translations));
            _previousTranslations = translations.GetValue(null);
            _previousLanguage = Properties.SERV_LANGUAGE;
            if (_previousTranslations == null)
                translations.SetValue(null, Activator.CreateInstance(translations.PropertyType));
            _undo.Add(() =>
            {
                translations.SetValue(null, _previousTranslations);
                Properties.SERV_LANGUAGE = _previousLanguage;
            });

            ShippedSettings(settings);
            Properties.SERV_LANGUAGE ??= "EN";

            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", HiddenStatic).GetValue(null);
            _previousCalculators = calculators.ToArray();
            GameLiving.LoadCalculators();

            Clock.SetValue(null, Start);
            TickDurationField.SetValue(null, 1000.0 / TicksPerSecond);
            var pool = new GameLoopThreadPoolSingleThreaded();
            pool.Init();
            // Init makes this thread look like the game loop's; loads that wait on a task then wait on a service that
            // is not running. The rehearsal runs the services itself, so the thread keeps its own context.
            SynchronizationContext.SetSynchronizationContext(_previousContext);
            ThreadPoolField.SetValue(null, pool);

            _previousNav = PathfindingProvider.Instance;
            PathfindingProvider.SetPathfindingMgr(Nav = new FlatNavmesh(seed));
            _random = Util.SeededScope(seed);
            _errors = new ErrorCapture();
            Recorder = new RvrFightRecorder(this);
        }

        /// <summary>Every ServerProperty at its shipped default (the attribute's), then the scenario's values.</summary>
        private void ShippedSettings(IReadOnlyDictionary<string, object> settings)
        {
            foreach (FieldInfo field in typeof(Properties).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (field.IsInitOnly || field.GetCustomAttribute<ServerPropertyAttribute>() is not { } attribute)
                    continue;
                _previousProperties[field] = field.GetValue(null);
                object value = attribute.DefaultValue;
                if (settings != null && settings.TryGetValue(attribute.Key, out object chosen))
                    value = chosen;
                try
                {
                    field.SetValue(null, value == null ? null : Convert.ChangeType(value, field.FieldType, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    // a default of another type (none today): keep the field as it was
                }
            }
        }

        /// <summary>A real region with one flat zone covering it (no navmesh), registered with WorldMgr while the world lives.</summary>
        public Region AddRegion(ushort regionId, ushort zoneId, string name)
        {
            var region = new Region(new RegionData { Id = regionId, Name = name, Description = name, Mobs = [] });
            region.Zones.Add(new Zone(region, zoneId, name, 0, 0, 65536, 65536, zoneId, false, 0, false, 0, 0, 0, 0, 0)
            {
                IsPathfindingEnabled = true, // under the flat stand-in navmesh
            });
            if (WorldMgr.Regions.TryGetValue(regionId, out Region previous))
                _previousRegions[regionId] = previous;
            WorldMgr.Regions[regionId] = region;
            _addedRegions.Add(regionId);
            Region ??= region;
            return region;
        }

        /// <summary>
        /// A level-<paramref name="level"/> autonomous gamebot of a real class, built as the server loads one from its
        /// record (stats, specializations, starter gear, property calculators); not yet in the world.
        /// </summary>
        public GameBot NewBot(eRealm realm, eCharacterClass characterClass, byte level, string name, string objective = "RvR")
        {
            eRace race = RvrFightKits.Race(realm, characterClass);
            var record = new OfflineWorldBotRecord
            {
                BotId = ++_nextBotId, Name = name, Realm = (int)realm, ClassId = (int)characterClass,
                ClassName = characterClass.ToString(), RaceId = (int)race, RaceName = race.ToString(), Level = level,
                IsAlive = true, ObjectiveKind = objective, LastSavedUtc = string.Empty,
            };
            var bot = new GameBot(record);
            bot.Spells = RvrFightKits.For(characterClass, level);
            if (bot.CharacterClass.ClassType != eClassType.ListCaster)
                ArmStandIn(bot);
            return bot;
        }

        /// <summary>Puts a living into the region at a spot, as AddToWorld does in game.</summary>
        public bool Place(GameLiving living, Region region, int x, int y, int z = 0, ushort heading = 0)
        {
            living.CurrentRegion = region;
            living.X = x;
            living.Y = y;
            living.Z = z;
            living.Heading = heading;
            if (living is GameNPC npc)
                npc.SpawnPoint = new Point3D(x, y, z);
            bool added = living.AddToWorld();
            if (added)
            {
                living.Health = living.MaxHealth;
                living.Mana = living.MaxMana;
                living.Endurance = living.MaxEndurance;
                _livings.Add(living);
            }
            return added;
        }

        /// <summary>Counts a living the scenario put in the world itself (the recorder watches it; Dispose takes it out).</summary>
        public void Track(GameLiving living)
        {
            if (!_livings.Contains(living))
                _livings.Add(living);
        }

        /// <summary>
        /// A bot-only RvR party, as the coordinator forms one (the leader first), registered with the coordinator as its
        /// own session (AutonomousBotGroupCoordinator.NewSession needs a rendezvous on a navmesh, so the session is made
        /// here: its rendezvous is where the party stands, its task clock is an RvR one, and it is past its assembly).
        /// Without a session the coordinator's pulse disbands the group as it would an orphan.
        /// </summary>
        public Group Form(IReadOnlyList<GameLiving> members)
        {
            var group = new Group(members[0]);
            foreach (GameLiving member in members)
                group.AddMember(member);
            GroupMgr.AddGroup(group);
            if (members[0] is GameBot leader)
                AdoptSession(group, leader, members.Count);
            _undo.Add(() =>
            {
                foreach (GameLiving member in group.GetMembersInTheGroup().ToList())
                    member.Group = null;
                GroupMgr.RemoveGroup(group);
                RvrFightBoards.Forget(group);
            });
            return group;
        }

        private static readonly Type SessionType = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic);
        private static readonly FieldInfo SessionsField = typeof(AutonomousBotGroupCoordinator).GetField("Sessions", HiddenStatic);
        private int _sessions;

        private void AdoptSession(Group group, GameBot leader, int size)
        {
            object session = Activator.CreateInstance(SessionType);
            void Set(string name, object value) => SessionType.GetProperty(name).SetValue(session, value);
            Set("Group", group);
            Set("Leader", leader);
            Set("Id", AutonomousBotGroupCoordinator.SessionId(leader.Realm, 9_000 + Seed % 1_000 * 10 + ++_sessions));
            Set("Rendezvous", new System.Numerics.Vector3(leader.X, leader.Y, leader.Z));
            Set("RendezvousRegion", leader.CurrentRegionID);
            Set("RendezvousName", "rehearsal field");
            Set("TaskClock", new AutonomousGroupTaskClock(eAutonomousObjectiveKind.RvR, new Random(Seed)));
            Set("ObjectiveKind", eAutonomousObjectiveKind.RvR);
            Set("LockedSize", size);
            Set("Phase", "Choosing group target");
            Set("LeaderReadyForAssembly", true);
            var sessions = (IDictionary)SessionsField.GetValue(null);
            sessions[group] = session;
            _undo.Add(() => sessions.Remove(group));
        }

        /// <summary>
        /// Stand-in weapon training for a melee gamebot: the class's spec lines and their weapon abilities come from the world
        /// database (SkillBase), which CI lacks, so the bot is given the weapon abilities and then arms itself through its own
        /// starter-weapon code (GameBot.EnsureAutonomousStarterWeapon: a generated weapon of its class's type and level).
        /// </summary>
        public static void ArmStandIn(GameBot bot)
        {
            foreach (FieldInfo field in typeof(Abilities).GetFields(BindingFlags.Static | BindingFlags.Public))
                if (field.Name.StartsWith("Weapon_", StringComparison.Ordinal) && field.GetValue(null) is string key && !bot.HasAbility(key))
                    bot.AddAbility(SkillBase.GetAbility(key, 1), false);
            typeof(GameBot).GetMethod("EnsureAutonomousStarterWeapon", Hidden)?.Invoke(bot, null);
        }

        /// <summary>The game loop: <paramref name="seconds"/> simulated seconds of the server's services, in their order.</summary>
        public void Run(double seconds, Action<RvrFightWorld> eachTick = null)
        {
            int ticks = (int)Math.Round(seconds * TicksPerSecond);
            for (int i = 0; i < ticks; i++)
            {
                Tick();
                eachTick?.Invoke(this);
            }
        }

        public void Tick()
        {
            long next = GameLoop.GameLoopTime + 1000 / TicksPerSecond + (GameLoop.GameLoopTime % 3 == 0 ? 1 : 0);
            Clock.SetValue(null, next);
            GameLoopService.Instance.Tick();
            TimerService.Instance.Tick();
            NpcService.Instance.Tick();
            AttackService.Instance.Tick();
            CastingService.Instance.Tick();
            EffectService.Instance.Tick();
            EffectListService.Instance.BeginTick();
            EffectListService.Instance.EndTick();
            MovementService.Instance.Tick();
            ReaperService.Instance.Tick();
            ZoneService.Instance.Tick();
            Recorder.Observe();
        }

        public void Dispose()
        {
            Recorder.Dispose();
            foreach (GameLiving living in _livings)
            {
                try
                {
                    if (living.ObjectState == GameObject.eObjectState.Active)
                        living.RemoveFromWorld();
                }
                catch (Exception)
                {
                    // the world is going anyway
                }
                if (living is GameNPC npc && npc.Brain != null)
                    ServiceObjectStore.Remove(npc.Brain);
                if (living.castingComponent != null) ServiceObjectStore.Remove(living.castingComponent);
                if (living.effectListComponent != null) ServiceObjectStore.Remove(living.effectListComponent);
                if (living.movementComponent != null) ServiceObjectStore.Remove(living.movementComponent);
                if (living.attackComponent != null) ServiceObjectStore.Remove(living.attackComponent);
                if (living is GameBot bot)
                {
                    AutonomousBotRegistry.Unregister(bot);
                    AutonomousStuckWatchdog.Unregister(bot);
                    RvrFightBoards.Forget(bot);
                }
            }
            // the services drop what was removed on their next pass
            for (int i = 0; i < 3; i++)
            {
                Clock.SetValue(null, GameLoop.GameLoopTime + 34);
                TimerService.Instance.Tick();
                NpcService.Instance.Tick();
                AttackService.Instance.Tick();
                CastingService.Instance.Tick();
                EffectService.Instance.Tick();
                EffectListService.Instance.BeginTick();
                EffectListService.Instance.EndTick();
                MovementService.Instance.Tick();
                ZoneService.Instance.Tick();
            }
            for (int i = _undo.Count - 1; i >= 0; i--)
                _undo[i]();
            foreach (ushort id in _addedRegions)
                WorldMgr.Regions.Remove(id);
            foreach (var (id, region) in _previousRegions)
                WorldMgr.Regions[id] = region;
            _errors.Dispose();
            _random.Dispose();
            PathfindingProvider.SetPathfindingMgr(_previousNav);
            ThreadPoolField.SetValue(null, _previousPool);
            TickDurationField.SetValue(null, _previousTickDuration);
            SynchronizationContext.SetSynchronizationContext(_previousContext);
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", HiddenStatic).GetValue(null);
            Array.Copy(_previousCalculators, calculators, calculators.Length);
            foreach (var (field, value) in _previousProperties)
                field.SetValue(null, value);
            Clock.SetValue(null, _previousClock);
            GameServer.LoadTestDouble(_previousServer);
        }

        /// <summary>
        /// The warnings and errors every server logger writes while the world lives (the services log an entity that
        /// threw, then remove it from the world), so a rehearsal can say what broke.
        /// </summary>
        private sealed class ErrorCapture : IDisposable
        {
            private static readonly FieldInfo QueueField = typeof(Logger).GetField("_queueProcessor", Hidden);
            private static readonly FieldInfo CollectionField = typeof(LogEntryQueueProcessor).GetField("_loggingQueue", Hidden);
            private static List<Logger> _loggers;
            private readonly System.Collections.Concurrent.BlockingCollection<LogEntry> _entries = new();
            private readonly LogEntryQueueProcessor _processor = new();
            private readonly List<(Logger Logger, object Previous)> _swapped = new();
            private readonly List<string> _lines = new();

            public ErrorCapture()
            {
                CollectionField.SetValue(_processor, _entries);
                foreach (Logger logger in Loggers())
                {
                    _swapped.Add((logger, QueueField.GetValue(logger)));
                    QueueField.SetValue(logger, _processor);
                }
            }

            public IReadOnlyList<string> Lines
            {
                get
                {
                    while (_entries.TryTake(out LogEntry entry))
                        if (entry.Level is ELogLevel.Error or ELogLevel.Fatal)
                            _lines.Add((entry.Message ?? string.Empty) + (entry.Exception != null ? $" ({entry.Exception.GetType().Name}: {entry.Exception.Message} at {entry.Exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim()})" : string.Empty));
                    return _lines;
                }
            }

            private static List<Logger> Loggers()
            {
                if (_loggers != null)
                    return _loggers;
                var found = new HashSet<Logger>(ReferenceEqualityComparer.Instance);
                foreach (Type type in SafeTypes(typeof(GameServer).Assembly))
                {
                    if (type.ContainsGenericParameters)
                        continue;
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(Logger).IsAssignableFrom(field.FieldType))
                            continue;
                        try
                        {
                            if (field.GetValue(null) is Logger logger)
                                found.Add(logger);
                        }
                        catch (Exception)
                        {
                            // a type whose static constructor needs the running server: not one the fight reaches
                        }
                    }
                }
                return _loggers = found.ToList();
            }

            private static IEnumerable<Type> SafeTypes(Assembly assembly)
            {
                try { return assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
            }

            public void Dispose()
            {
                foreach (var (logger, previous) in _swapped)
                    QueueField.SetValue(logger, previous);
            }
        }
    }
}
