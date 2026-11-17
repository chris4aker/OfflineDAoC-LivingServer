using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-17: "companions and bots should be the same as a player" for specialization points. Every bot kind's
    /// points at levels 40, 45 and 50 for the x1.0/x1.5/x2.0 multipliers equal a player's of the same class and level. The
    /// oracle is the player's own count, GamePlayer.VerifySpecPoints (levels 2-5, each level above 5, each half level
    /// 40.5-49.5 passed, and the current half level at the second stage) on a player with no trained lines. Without the skill
    /// tables a bot has no lines, so every point it earned is still unspent and is counted.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_BotSpecPointsMatchAPlayer
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, ReadOnly>();
        public class ReadOnly : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("No database writes allowed: " + method.Name);
                Type type = method.ReturnType;
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => Empty;
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
        }

        /// <summary>The oracle: a player of the class and level, with no trained lines, counted by GamePlayer itself.</summary>
        private sealed class OraclePlayer : GamePlayer
        {
            private OraclePlayer() : base(null, null) { }
            public byte TestLevel;
            public bool TestSecondStage;
            public ICharacterClass TestClass;
            public override byte Level { get => TestLevel; set { } }
            public override bool IsLevelSecondStage { get => TestSecondStage; set { } }
            public override ICharacterClass CharacterClass => TestClass;
            public override IList<Specialization> GetSpecList() => new List<Specialization>();
        }

        /// <summary>A hired companion whose test lines stay: without the skill tables the class's career is empty, and
        /// GameBot.LoadClassSpecializations (run after every training) would drop every line not in it.</summary>
        private sealed class LineBot : GameBot
        {
            public LineBot(OfflineWorldBotRecord record) : base(record) { }
            public override void LoadClassSpecializations(bool sendMessage) { }
        }

        /// <summary>A /bot or /spawn companion's player.</summary>
        private sealed class Owner : GamePlayer
        {
            private Owner() : base(null, null) { }
            public override byte Level { get; set; }
        }

        private GameServer _previous;
        private PetTestLanguageScope _language;
        private bool _eraBuilds;
        private TestLogCapture _logs;
        private TestPropertyCalculators _calculators;
        private readonly List<GameBot> _constructed = new();
        private static long _nextId = 9_871_000;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            _eraBuilds = EraBuildsSwitch;
            EraBuildsSwitch = false; // the points, not the plan
            _calculators = new TestPropertyCalculators();
            _logs = new TestLogCapture("GameBot", "BotEquipment", "BotDatabase", "CreationStartupEquipment", "AutonomousStarterGear",
                "AutonomousBotStatusPersistence", "BotInventory", "HiredCompanions165");
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            ClearHires();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameBot bot in _constructed)
            {
                if (bot.castingComponent != null) ServiceObjectStore.Remove(bot.castingComponent);
                if (bot.effectListComponent != null) ServiceObjectStore.Remove(bot.effectListComponent);
                if (bot.movementComponent != null) ServiceObjectStore.Remove(bot.movementComponent);
                if (bot.Owner != null) DOL.Events.GameEventMgr.RemoveAllHandlersForObject(bot.Owner);
            }
            _constructed.Clear();
            ClearHires();
            EraBuildsSwitch = _eraBuilds;
            _logs.Dispose();
            _calculators.Dispose();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        private static readonly object[] Classes =
        [
            new object[] { eCharacterClass.Wizard, 10 },
            new object[] { eCharacterClass.Friar, 15 },
            new object[] { eCharacterClass.Armsman, 20 },
        ];

        private static readonly int[] Levels = [40, 45, 50];

        [TestCaseSource(nameof(Classes))]
        public void TheOracleIsThePlayersOwnCount(eCharacterClass cls, int multiplier)
        {
            ICharacterClass characterClass = ClassOf(cls);
            Assert.That(characterClass.SpecPointsMultiplier, Is.EqualTo(multiplier), "the multiplier this case is about");
            // The player's own count: at level 50 a player has the half levels 40.5-49.5 that no whole-level sum gives.
            int wholeLevels = Enumerable.Range(2, 49).Sum(level => GameBot.SpecPointsForLevel(multiplier, level, false));
            Assert.That(Player(characterClass, 50) - wholeLevels,
                Is.EqualTo(new Dictionary<int, int> { [10] = 220, [15] = 330, [20] = 445 }[multiplier]));
            foreach (int level in Levels)
                Assert.That(GameBot.PlayerSpecPointTotal(multiplier, level, false), Is.EqualTo(Player(characterClass, level)), $"{level}");
            foreach (int level in new[] { 40, 45, 49 })
                Assert.That(GameBot.PlayerSpecPointTotal(multiplier, level, true), Is.EqualTo(Player(characterClass, level, true)), $"{level}.5");
        }

        // ---- autonomous gamebots (already fixed in #326; kept here so every kind is checked against the player)

        [TestCaseSource(nameof(Classes))]
        public void AGamebotTrainedEveryLevelHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            GameBot bot = Gamebot(cls, 1);
            Leftover(bot, 0);
            for (byte level = 2; level <= 50; level++)
            {
                bot.Level = level;
                bot.SpendSpecPoints(level, (byte)(level - 1));
                if (Levels.Contains(level))
                    Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void AGamebotArrivingAtItsLevelHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Gamebot(cls, level); // "+ Lv.N": never saved, arrives trained
                Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        // ---- hired companions (True 1.65): their player spends the points in the companion's spec window

        [TestCaseSource(nameof(Classes))]
        public void AHiredCompanionHasAPlayersPointsToSpend(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Hired(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                Assert.That(bot.IsHiredCompanion, Is.True);
                Assert.That(bot.CompanionSpecPoints, Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void AHiredCompanionGetsTheHalfLevelAtItsSecondStageAsAPlayerDoes(eCharacterClass cls, int multiplier)
        {
            foreach (int level in new[] { 40, 45 })
            {
                long current = GamePlayer.GetExperienceAmountForLevel(level - 1), next = GamePlayer.GetExperienceAmountForLevel(level);
                long secondStage = 1 + current + (next - current) / 2; // GamePlayer.ExperienceForCurrentLevelSecondStage
                GameBot before = Hired(cls, level, secondStage - 1);
                GameBot at = Hired(cls, level, secondStage);
                Assert.That(before.CompanionSpecPoints, Is.EqualTo(Player(before.CharacterClass, level)), $"{level}, before");
                Assert.That(at.CompanionSpecPoints, Is.EqualTo(Player(at.CharacterClass, level, true)), $"{level}.5");
            }
            GameBot fifty = Hired(cls, 50, GamePlayer.GetExperienceAmountForLevel(50) - 1);
            Assert.That(fifty.CompanionSpecPoints, Is.EqualTo(Player(fifty.CharacterClass, 50)), "no 50.5");
        }

        [TestCaseSource(nameof(Classes))]
        public void AHiredCompanionsRespecGivesBackAPlayersTotal(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Hired(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                Specialization line = AddLine(bot, "TestLineA");
                AddLine(bot, "TestLineB");
                for (int i = 0; i < 20; i++)
                    Assert.That(bot.CompanionTrain(line), Is.Null, $"train {line.Level + 1}");
                Assert.That(line.Level, Is.EqualTo(21));
                Assert.That(bot.CompanionSpecPoints + GameBot.PointsToTrain(21), Is.EqualTo(Player(bot.CharacterClass, level)),
                    "spent plus left is a player's total");
                bot.CompanionRespec();
                Assert.That(bot.GetSpecList().All(spec => spec.Level == 1), Is.True);
                Assert.That(bot.CompanionSpecPoints, Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void ASavedCompanionLoadsWithEveryTrainedPointAndAPlayersTotal(eCharacterClass cls, int multiplier)
        {
            // Saved by the build before 1.2: trained at level 42 (no half-level points), now level 45 with 3 levels untrained.
            foreach ((int trainedAt, int level) in new[] { (42, 45), (50, 50), (45, 45) })
            {
                const string specs = "TestLineA|30;TestLineB|12";
                int spent = GameBot.PointsToTrain(30) + GameBot.PointsToTrain(12);
                int oldEarned = Enumerable.Range(2, trainedAt - 1).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false));
                var record = HiredRecord(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                record.SerializedSpecs = specs;
                record.SerializedAbilities = $"trained-level|{trainedAt}|realm-points|0";
                record.UnspentSpecPoints = oldEarned - spent;
                record.LastSavedUtc = DateTime.UtcNow.ToString("O");
                GameBot bot = Track(new LineBot(record));
                // The skill tables give the lines; the constructor's own loader then restores the saved levels.
                AddLine(bot, "TestLineA");
                AddLine(bot, "TestLineB");
                typeof(GameBot).GetMethod("LoadPersistedSpecs", Hidden).Invoke(bot, [record.SerializedSpecs]);
                int oldToSpend = oldEarned - spent +
                    Enumerable.Range(trainedAt + 1, level - trainedAt).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false));

                Assert.That(bot.GetBaseSpecLevel("TestLineA"), Is.EqualTo(30), "no trained point is lost");
                Assert.That(bot.GetBaseSpecLevel("TestLineB"), Is.EqualTo(12), "no trained point is lost");
                Assert.That(bot.CompanionSpecPoints, Is.EqualTo(Player(bot.CharacterClass, level) - spent), $"{trainedAt}->{level}");
                Assert.That(bot.CompanionSpecPoints, Is.GreaterThanOrEqualTo(oldToSpend), "never fewer to spend than before");
                Assert.That(bot.CompanionSpecPoints - oldToSpend,
                    Is.EqualTo(Player(bot.CharacterClass, level) - Enumerable.Range(2, level - 1).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false))),
                    "the half levels 40.5 to (level-1).5 are added");
                Assert.That(bot.PrepareAutonomousStateSnapshot().SerializedSpecs, Is.EqualTo(specs), "saved again unchanged");
            }
        }

        [Test]
        public void ACompanionsTrainedLinesAreNeverTakenAway()
        {
            // More trained than a player could have (a corrupt or hand-edited save): nothing is reset, nothing goes negative.
            GameBot bot = Hired(eCharacterClass.Wizard, 10, GamePlayer.GetExperienceAmountForLevel(9));
            Specialization line = AddLine(bot, "TestLineA");
            line.Level = 10;
            AddLine(bot, "TestLineB").Level = 10;
            Assert.That(bot.CompanionSpecPoints, Is.Zero);
            Assert.That(line.Level, Is.EqualTo(10));
        }

        // ---- /bot companions (BotManager / BotCommand) and /spawn helpers: made trained at their level

        [TestCaseSource(nameof(Classes))]
        public void ABotCommandCompanionHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = OwnerBot(cls, ownerLevel: 50, botLevel: (byte)level, temporary: false);
                Assert.That(bot.Level, Is.EqualTo(level));
                Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void ASpawnHelperHasAPlayersPointsAndKeepsThemWhenPromotedToFifty(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = OwnerBot(cls, ownerLevel: (byte)level, botLevel: 0, temporary: true);
                Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
            foreach (byte from in new byte[] { 40, 49 })
            {
                // TryApplyEndgameCompanionUpgrade's training step: before 1.2 a helper promoted from 49 missed the 49.5 points.
                GameBot helper = OwnerBot(cls, ownerLevel: from, botLevel: 0, temporary: true);
                helper.Level = 50;
                helper.SpendSpecPoints(50, from);
                Assert.That(Leftover(helper), Is.EqualTo(Player(helper.CharacterClass, 50)), $"promoted from {from}");
            }
        }

        // ---- Epic Special Forces and No Lifer buffbots: made in memory, trained at their level

        [TestCaseSource(nameof(Classes))]
        public void AnEpicSpecialForceHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Track(GameBot.CreateEpicSpecialForce(Record(cls, level, 0)));
                Assert.That(bot.IsEpicSpecialForce, Is.True);
                Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCase(eCharacterClass.Cleric)]
        [TestCase(eCharacterClass.Shaman)]
        [TestCase(eCharacterClass.Druid)]
        public void ABuffbotHasAPlayersPoints(eCharacterClass cls)
        {
            // Buffbots are only these classes, all x1.0.
            foreach (int level in Levels)
            {
                OfflineWorldBotRecord record = Record(cls, level, 0);
                record.Realm = (int)(cls == eCharacterClass.Cleric ? eRealm.Albion : cls == eCharacterClass.Shaman ? eRealm.Midgard : eRealm.Hibernia);
                record.RaceId = (int)(cls == eCharacterClass.Cleric ? eRace.Briton : cls == eCharacterClass.Shaman ? eRace.Norseman : eRace.Celt);
                GameBot bot = Track(GameBot.CreateNoLiferBuffBot(record, 1));
                Assert.That(bot.IsNoLiferBuffBot, Is.True);
                Assert.That(bot.CharacterClass.SpecPointsMultiplier, Is.EqualTo(10));
                Assert.That(Leftover(bot), Is.EqualTo(Player(bot.CharacterClass, level)), $"level {level}");
            }
        }

        // ---- helpers

        private static int Player(ICharacterClass cls, int level, bool secondStage = false)
        {
            var player = (OraclePlayer)RuntimeHelpers.GetUninitializedObject(typeof(OraclePlayer));
            player.TestClass = cls;
            player.TestLevel = (byte)level;
            player.TestSecondStage = secondStage;
            return player.VerifySpecPoints();
        }

        private static ICharacterClass ClassOf(eCharacterClass cls) =>
            ScriptMgr.FindCharacterClass((int)cls) ?? throw new InvalidOperationException(cls.ToString());

        private static OfflineWorldBotRecord Record(eCharacterClass cls, int level, long botId) => new()
        {
            BotId = botId, Name = $"Points{cls}{level}{botId}", Realm = (int)eRealm.Albion, ClassId = (int)cls,
            RaceId = (int)eRace.Briton, Level = level, Health = 100, Mana = 10, Endurance = 100,
        };

        private GameBot Gamebot(eCharacterClass cls, int level) => Construct(Record(cls, level, 0));

        private OfflineWorldBotRecord HiredRecord(eCharacterClass cls, int level, long experience)
        {
            OfflineWorldBotRecord record = Record(cls, level, ++_nextId);
            record.Experience = experience;
            typeof(HiredCompanions165).GetMethod("RegisterForTests", HiddenStatic)!.Invoke(null,
                [new OfflineHiredCompanionRecord { BotId = record.BotId, OwnerCharacterID = "owner-1", OwnerName = "Owner" }]);
            return record;
        }

        private GameBot Hired(eCharacterClass cls, int level, long experience) => Track(new LineBot(HiredRecord(cls, level, experience)));

        private GameBot OwnerBot(eCharacterClass cls, byte ownerLevel, byte botLevel, bool temporary)
        {
            var owner = (Owner)RuntimeHelpers.GetUninitializedObject(typeof(Owner));
            owner.Level = ownerLevel;
            return Track(new GameBot(owner, (byte)cls, $"Points{cls}", (byte)eRace.Briton, temporaryGroupHelper: temporary, botLevel: botLevel));
        }

        private GameBot Construct(OfflineWorldBotRecord record) => Track(new GameBot(record));

        private GameBot Track(GameBot bot)
        {
            _constructed.Add(bot);
            return bot;
        }

        private static Specialization AddLine(GameBot bot, string name)
        {
            var spec = new Specialization(name, name, 0);
            typeof(GameBot).GetMethod("AddSpecialization", Hidden, [typeof(Specialization), typeof(bool)]).Invoke(bot, [spec, false]);
            return bot.GetSpecializationByName(name);
        }

        private static void ClearHires() => typeof(HiredCompanions165).GetMethod("ClearForTests", HiddenStatic)!.Invoke(null, null);

        private static bool EraBuildsSwitch
        {
            get => typeof(Properties).GetField("BOT_ERA_BUILDS")?.GetValue(null) as bool? ?? false;
            set => typeof(Properties).GetField("BOT_ERA_BUILDS")?.SetValue(null, value);
        }

        private static FieldInfo LeftoverField => typeof(GameBot).GetField("m_leftOverSpecPoints", Hidden);
        private static int Leftover(GameBot bot) => (int)LeftoverField.GetValue(bot);
        private static void Leftover(GameBot bot, int value) => LeftoverField.SetValue(bot, value);
    }
}
