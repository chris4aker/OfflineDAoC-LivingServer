using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using DOL.GS.Database;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.UnitTests
{
    /// <summary>
    /// Review of era builds WP0-2 (2026-11-17), should-fix 2: the custom-class rule ("bots never play the Sluaghbinder",
    /// owner 2026-11-17) tested where it is enforced, not only in its pure helpers: a saved /bot companion, hiring and
    /// summoning a hired companion, and the population's retirement and login roster.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CustomClassRuleWiring
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly IObjectDatabase RowsDatabase = DispatchProxy.Create<IObjectDatabase, Rows>();

        public class Rows : DispatchProxy
        {
            public BotProfile Profile;
            public OfflineWorldBotRecord Record;
            public readonly List<object> Saved = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "SaveObject")
                {
                    if (args[0] is IEnumerable many)
                        Saved.AddRange(many.Cast<object>());
                    else
                        Saved.Add(args[0]);
                    return method.ReturnType == typeof(bool) ? true : null;
                }
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("Unexpected database call: " + method.Name);
                Type type = method.ReturnType;
                if (type == typeof(BotProfile)) return Profile;
                if (type == typeof(OfflineWorldBotRecord)) return Record;
                if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                {
                    Type element = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                    if (element == typeof(BotProfile) && Profile != null) list.Add(Profile);
                    return list;
                }
                return null;
            }
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => RowsDatabase;
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override byte Level { get; set; }
        }

        private GameServer _previous;
        private PetTestLanguageScope _language;
        private bool? _hiredOverride;
        private TestLogCapture _logs;
        private readonly List<long> _hiredRows = new();
        private static Rows Db => (Rows)RowsDatabase;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            _hiredOverride = HiredCompanionsMode.Override;
            _logs = new TestLogCapture("AutonomousPopulationController", "BotDatabase", "HiredCompanions165", "GameBot");
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            Db.Profile = null;
            Db.Record = null;
            Db.Saved.Clear();
        }

        [TearDown]
        public void Cleanup()
        {
            var hired = (ConcurrentDictionary<long, OfflineHiredCompanionRecord>)typeof(HiredCompanions165)
                .GetField("Hired", HiddenStatic).GetValue(null);
            foreach (long id in _hiredRows)
                hired.TryRemove(id, out _);
            _hiredRows.Clear();
            HiredCompanionsMode.Override = _hiredOverride;
            _logs.Dispose();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        private static Player NewPlayer(string id, byte level = 50)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.Level = level;
            typeof(GameObject).GetField("m_InternalID", Hidden).SetValue(player, id);
            return player;
        }

        // ---- a saved /bot companion

        [Test]
        public void ASavedSluaghbinderBotIsNotLoadedAndThePlayerIsToldWhy()
        {
            Player owner = NewPlayer("wiring-owner");
            Db.Profile = new BotProfile { BotId = 990001, OwnerCharacterID = owner.ObjectId, Name = "Keptsluagh",
                ClassId = (byte)eCharacterClass.Sluaghbinder, RaceId = (byte)eRace.Celt, Level = 20 };
            Assert.That(BotDatabase.LoadBot(owner, 990001), Is.Null);
            Assert.That(BotDatabase.LoadBotByName(owner, "Keptsluagh"), Is.Null);
            Assert.That(BotDatabase.FindProfileByName(owner, "Keptsluagh"), Is.SameAs(Db.Profile), "kept, not deleted");
            Assert.That(BotDatabase.RefusalFor(Db.Profile), Is.EqualTo(BotCustomClassRule.RefusedMessage),
                "/bot spawn says why instead of \"No bot named ... found\"");
            Assert.That(Db.Saved, Is.Empty, "nothing about it is written");
        }

        [Test]
        public void EveryOtherSavedBotHasNoRefusal()
        {
            foreach (eCharacterClass cls in Enum.GetValues<eCharacterClass>().Where(c => c != eCharacterClass.Sluaghbinder))
                Assert.That(BotDatabase.RefusalFor(new BotProfile { ClassId = (byte)cls }), Is.Null, cls.ToString());
            Assert.That(BotDatabase.RefusalFor(null), Is.Null);
        }

        [Test]
        public void BotSpawnTellsThePlayerTheRefusal()
        {
            string source = File.ReadAllText(Path.Combine(ServerRoot(), "GameServer", "bots", "BotCommand.cs"));
            Assert.That(source, Does.Contain("BotDatabase.RefusalFor(BotDatabase.FindProfileByName(client.Player, botName))"));
        }

        // ---- hired companions

        [Test]
        public void ACompanionCannotBeHiredAsASluaghbinder()
        {
            HiredCompanionsMode.Override = true;
            Player player = NewPlayer("wiring-hirer");
            var design = new Hire165.Design { Race = eRace.Celt, Class = eCharacterClass.Sluaghbinder, Name = "Sluaghy" };
            Assert.That(HiredCompanions165.Hire(player, design), Is.EqualTo(BotCustomClassRule.RefusedMessage));
            design.Class = eCharacterClass.Druid;
            design.Name = "x";
            Assert.That(HiredCompanions165.Hire(player, design), Is.Not.EqualTo(BotCustomClassRule.RefusedMessage),
                "another class goes on to the next check (here the name)");
        }

        [Test]
        public void ACompanionHiredAsASluaghbinderStaysAtTheInn()
        {
            HiredCompanionsMode.Override = true;
            Player player = NewPlayer("wiring-summoner");
            const long botId = 990002;
            var hired = (ConcurrentDictionary<long, OfflineHiredCompanionRecord>)typeof(HiredCompanions165)
                .GetField("Hired", HiddenStatic).GetValue(null);
            hired[botId] = new OfflineHiredCompanionRecord { BotId = botId, OwnerCharacterID = "wiring-summoner", Row = 1 };
            _hiredRows.Add(botId);
            Db.Record = new OfflineWorldBotRecord { BotId = botId, Name = "Innsluagh", ClassId = (int)eCharacterClass.Sluaghbinder,
                Realm = (int)eRealm.Hibernia, Level = 30, IsAlive = true };
            string answer = HiredCompanions165.Summon(player, 1);
            Assert.That(answer, Does.StartWith("Innsluagh stays behind").And.Contains(BotCustomClassRule.RefusedMessage));
            Assert.That(HiredCompanions165.LiveBot(botId), Is.Null, "never comes into the world");
        }

        // ---- the population

        [Test]
        public void RetirementMarksAndSavesTheKeptCustomClassBotsOnly()
        {
            var sluagh = new OfflineWorldBotRecord { BotId = 990003, Name = "Popsluagh", ClassId = (int)eCharacterClass.Sluaghbinder,
                Realm = (int)eRealm.Hibernia, Level = 50, IsOnline = true, Activity = "Hunting" };
            var druid = new OfflineWorldBotRecord { BotId = 990004, Name = "Popdruid", ClassId = (int)eCharacterClass.Druid,
                Realm = (int)eRealm.Hibernia, Level = 50, Activity = "Hunting" };
            typeof(AutonomousPopulationController).GetMethod("RetireCustomClassBots", HiddenStatic)
                .Invoke(null, [new[] { sluagh, druid }]);
            Assert.That(sluagh.Activity, Is.EqualTo(BotCustomClassRule.RetiredActivity));
            Assert.That(sluagh.IsOnline, Is.False);
            Assert.That(sluagh.IsRetired, Is.False, "reversible: not the launcher's delete request");
            Assert.That(druid.Activity, Is.EqualTo("Hunting"));
            Assert.That(Db.Saved, Is.EqualTo(new object[] { sluagh }));
            Assert.That(_logs.Lines, Has.One.StartsWith("BOT_CUSTOM_CLASS_RETIRED bot=\"Popsluagh\" id=990003 class=Sluaghbinder"));
        }

        [Test]
        public void ThePopulationLogsInOnlyFromTheRosterWithoutTheCustomClass()
        {
            // The login paths (the poll's desired count and pick, the spawn on the game loop) read _loginRoster; _cachedRoster,
            // which keeps the custom-class bots for their guilds, is only read where the rule allows.
            string source = File.ReadAllText(Path.Combine(ServerRoot(), "GameServer", "bots", "autonomous",
                "AutonomousPopulationController.cs"));
            Assert.That(source, Does.Contain("_loginRoster = CustomClassBotRetirement.LoginRoster(_cachedRoster);"));
            Assert.That(Regex.IsMatch(source, @"RefreshControlPlane\(force: true\);\s*RetireCustomClassBots\(_cachedRoster\);"), Is.True,
                "the start retires them once, after the first roster read");
            Assert.That(source, Does.Contain("OfflineWorldBotRecord[] roster = _loginRoster;"));
            Assert.That(source, Does.Contain("_loginRoster.FirstOrDefault(candidate => candidate.BotId == botId)"));
            string[] allowed =
            [
                "private static volatile OfflineWorldBotRecord[] _cachedRoster",
                "RetireCustomClassBots(_cachedRoster);",
                "LogEraBuildCensus(_cachedRoster);",
                "_cachedRoster = Array.Empty<OfflineWorldBotRecord>();",
                "_cachedRoster = DOLDB<OfflineWorldBotRecord>",
                "_loginRoster = CustomClassBotRetirement.LoginRoster(_cachedRoster);",
                "long[] rosterIds = _cachedRoster.Select(record => record.BotId).ToArray();",
                "internal static OfflineWorldBotRecord[] CachedRoster => _cachedRoster;",
            ];
            foreach (string line in source.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("_cachedRoster") && !l.StartsWith("//")))
                Assert.That(allowed.Any(line.StartsWith), Is.True, $"a new read of the full roster: {line}");
            Assert.That(CustomClassBotRetirement.LoginRoster(
                [new OfflineWorldBotRecord { BotId = 1, ClassId = (int)eCharacterClass.Sluaghbinder },
                 new OfflineWorldBotRecord { BotId = 2, ClassId = (int)eCharacterClass.Druid }]).Select(r => r.BotId),
                Is.EqualTo(new long[] { 2 }));
        }

        private static string ServerRoot()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "source", "server", "Dawn of Light.sln")))
                    return Path.Combine(dir.FullName, "source", "server");
            Assert.Ignore("Server source not found beside the tests.");
            return null;
        }
    }
}
