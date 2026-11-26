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
using DAoCLivingServer.Shared;

namespace DOL.UnitTests
{
    /// <summary>
    /// Review of era builds WP0-2 (2026-11-17), should-fix 1: players get the half-level spec points above 40 at each x.5
    /// second stage (GamePlayer.OnLevelSecondStage); gamebots have no second stage, and GameBot.SpendSpecPoints gave them
    /// only when catching up more than one level. A gamebot that trained every level reached 50 with 220/330/445 points
    /// fewer than one the launcher made at 50 (x1.0/x1.5/x2.0). These tests run GameBot's own training, not constants.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_GamebotHalfLevelPoints
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
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

        private GameServer _previous;
        private PetTestLanguageScope _language;
        private bool _eraBuilds;
        private TestLogCapture _logs;
        private TestPropertyCalculators _calculators;
        private readonly List<GameBot> _constructed = new();

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            _eraBuilds = EraBuildsSwitch;
            _calculators = new TestPropertyCalculators();
            _logs = new TestLogCapture("GameBot", "BotEquipment", "BotDatabase", "CreationStartupEquipment", "AutonomousStarterGear",
                "AutonomousBotStatusPersistence", "BotInventory");
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
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
            EraBuildsSwitch = _eraBuilds;
            _logs.Dispose();
            _calculators.Dispose();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        // bots / bot_era_builds arrives with WP3; until then the field is absent and this is a no-op.
        private static bool EraBuildsSwitch
        {
            get => typeof(Properties).GetField("BOT_ERA_BUILDS")?.GetValue(null) as bool? ?? false;
            set => typeof(Properties).GetField("BOT_ERA_BUILDS")?.SetValue(null, value);
        }

        [TestCase(eCharacterClass.Wizard, 10)]
        [TestCase(eCharacterClass.Friar, 15)]
        [TestCase(eCharacterClass.Armsman, 20)]
        public void ABotTrainedEveryLevelEarnsWhatAnArrivingLevelFiftyHas(eCharacterClass cls, int multiplier)
        {
            // No specializations exist without the skill tables, so every earned point stays unspent and is counted.
            GameBot arriving = Make(cls, 50);
            Assert.That(Leftover(arriving), Is.EqualTo(EraBuildCatalog.PointBudget(multiplier)),
                "a \"+ Lv.50\" arrival trains levels 2-50 at once, with the half levels");

            GameBot levelled = Make(cls, 1);
            Leftover(levelled, 0);
            for (byte level = 2; level <= 50; level++)
            {
                levelled.Level = level;
                levelled.SpendSpecPoints(level, (byte)(level - 1));
            }
            Assert.That(Leftover(levelled), Is.EqualTo(Leftover(arriving)), "one trainer visit a level, 2 to 50");
        }

        [Test]
        public void AnEraBuildSizedToTheBudgetIsFinishedByABotTrainedEveryLevel()
        {
            // Wizard Cold/Earth/Fire 50/14/14 costs 1482 points: more than the 1274 a level-by-level bot had before.
            var target = new Dictionary<string, int> { [Specs.Cold_Magic] = 50, [Specs.Earth_Magic] = 14, [Specs.Fire_Magic] = 14 };
            Assert.That(EraBuildCatalog.BuildCost(target), Is.EqualTo(1482));
            foreach (bool everyLevel in new[] { true, false })
            {
                GameBot bot = Make(eCharacterClass.Wizard, 1);
                var plan = new BotSpec { SpecType = eSpecType.IceWiz, WeaponOneType = eObjectType.Staff, Is2H = true };
                plan.SpecLines.Add(new BotSpecLine(Specs.Cold_Magic, 50, 1f));
                plan.SpecLines.Add(new BotSpecLine(Specs.Earth_Magic, 14, 0.17f));
                plan.SpecLines.Add(new BotSpecLine(Specs.Fire_Magic, 14, 0.17f));
                typeof(GameBot).GetProperty(nameof(GameBot.BotSpec)).SetValue(bot, plan);
                foreach (string line in target.Keys)
                    typeof(GameBot).GetMethod("AddSpecialization", Hidden, [typeof(Specialization), typeof(bool)])
                        .Invoke(bot, [new Specialization(line, line, 0), false]);
                Leftover(bot, 0);
                if (everyLevel)
                {
                    for (byte level = 2; level <= 50; level++)
                    {
                        bot.Level = level;
                        bot.SpendSpecPoints(level, (byte)(level - 1));
                    }
                }
                else
                {
                    bot.Level = 50;
                    bot.SpendSpecPoints(50, 1);
                }
                foreach ((string line, int level) in target)
                    Assert.That(bot.GetBaseSpecLevel(line), Is.EqualTo(level), $"{line}, every level: {everyLevel}");
                Assert.That(Leftover(bot), Is.EqualTo(1494 - 1482), $"every level: {everyLevel}");
            }
        }

        [TestCase(40, 10, false, 40)]
        [TestCase(41, 10, false, 41)]
        [TestCase(41, 10, true, 61)]
        [TestCase(50, 20, true, 100 + 49)]
        [TestCase(5, 20, true, 5)]
        public void TheHalfLevelIsTheOneBeforeTheLevel(int level, int multiplier, bool halfLevel, int points) =>
            Assert.That(GameBot.SpecPointsForLevel(multiplier, level, halfLevel), Is.EqualTo(points));

        [Test]
        public void TheSumOverLevelsWithHalfLevelsIsTheEraBudget()
        {
            foreach (int multiplier in new[] { 10, 15, 20, 22, 25 })
                Assert.That(Enumerable.Range(2, 49).Sum(level => GameBot.SpecPointsForLevel(multiplier, level, true)),
                    Is.EqualTo(EraBuildCatalog.PointBudget(multiplier)), multiplier.ToString());
        }

        private GameBot Make(eCharacterClass cls, int level)
        {
            EraBuildsSwitch = false; // today's class-file plan: this test is about the points, not the plan
            var realm = eRealm.Albion;
            var record = new OfflineWorldBotRecord
            {
                BotId = 0, Name = $"HalfLevel{cls}{level}", Realm = (int)realm, ClassId = (int)cls, RaceId = (int)eRace.Briton,
                Level = level, Health = 100, Mana = 10, Endurance = 100,
            };
            var bot = new GameBot(record);
            _constructed.Add(bot);
            return bot;
        }

        private static FieldInfo LeftoverField => typeof(GameBot).GetField("m_leftOverSpecPoints", Hidden);
        private static int Leftover(GameBot bot) => (int)LeftoverField.GetValue(bot);
        private static void Leftover(GameBot bot, int value) => LeftoverField.SetValue(bot, value);
    }
}
