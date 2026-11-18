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
using OfflineDaoc.Shared;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-17: "companions and bots should be the same as a player" for specialization points. Every bot kind's
    /// points at levels 40, 45 and 50 equal a player's of the same class and level with the same lines trained: the points
    /// earned (half levels above 40 included) and what the lines cost (the class's autotrain lines free up to a quarter of
    /// the level). The oracle is the player's own count, GamePlayer.VerifySpecPoints (PlayerSpecPointOracle), run with the
    /// bot's real lines, so an Armsman's Slash and Thrust or an assassin's Stealth are held to the player's cost. The test
    /// world has no skill tables: the lines are added by hand, and a bot made by its constructor alone has none, so every
    /// point it earned is still unspent and is counted.
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

        /// <summary>A hired companion whose test lines stay: without the skill tables the class's career is empty, and
        /// GameBot.LoadClassSpecializations (run after every training) would drop every line not in it.</summary>
        private sealed class LineBot : GameBot
        {
            public LineBot(OfflineWorldBotRecord record) : base(record) { }
            public override void LoadClassSpecializations(bool sendMessage) { }
        }

        /// <summary>A saved gamebot whose lines exist before its saved levels load (the skill tables give them in the game).</summary>
        private sealed class SavedBot : GameBot
        {
            [ThreadStatic] public static string[] Lines;
            public SavedBot(OfflineWorldBotRecord record) : base(record) { }
            public override void LoadClassSpecializations(bool sendMessage)
            {
                foreach (string line in Lines ?? [])
                    if (GetSpecializationByName(line) == null)
                        AddSpecialization(new Specialization(line, line, 0), false);
            }
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
            GameBot.SpecRefundLog.ResetForTests();
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

        // x1.0 and x1.5 without autotrain lines, and every class that has them (review of ce90adcc1): all eleven, x1.5 to
        // x2.5, one to three autotrain lines each.
        private static readonly object[] Classes =
        [
            new object[] { eCharacterClass.Wizard, 10 },
            new object[] { eCharacterClass.Friar, 15 },
            new object[] { eCharacterClass.Minstrel, 15 },
            new object[] { eCharacterClass.Armsman, 20 },
            new object[] { eCharacterClass.Mercenary, 20 },
            new object[] { eCharacterClass.Paladin, 20 },
            new object[] { eCharacterClass.Reaver, 20 },
            new object[] { eCharacterClass.Scout, 20 },
            new object[] { eCharacterClass.Infiltrator, 25 },
            new object[] { eCharacterClass.Warrior, 20 },
            new object[] { eCharacterClass.Shadowblade, 22 },
            new object[] { eCharacterClass.Nightshade, 22 },
            new object[] { eCharacterClass.Ranger, 20 },
        ];

        [Test]
        public void TheCasesCoverEveryClassWithAutotrainLines()
        {
            eCharacterClass[] cases = Classes.Select(c => (eCharacterClass)((object[])c)[0]).ToArray();
            foreach (eCharacterClass cls in Enum.GetValues<eCharacterClass>())
            {
                ICharacterClass characterClass = ScriptMgr.FindCharacterClass((int)cls);
                if (characterClass?.GetAutotrainableSkills() is { Count: > 0 } lines && BotCustomClassRule.BotsMayPlay((int)cls) &&
                    EraBuildCatalog.Default.GetClass((int)cls) != null)
                {
                    Assert.That(cases, Does.Contain(cls), $"{cls} autotrains {string.Join(", ", lines)}");
                    Assert.That(Plans[cls].Select(p => p.Line).Intersect(lines), Is.Not.Empty, $"{cls}: a plan with an autotrain line");
                }
            }
            Assert.That(cases.Count(c => PlayerSpecPointOracle.ClassOf(c).GetAutotrainableSkills().Count > 0), Is.EqualTo(11));
        }

        /// <summary>A training plan per class: line, cap, share of the level (0: trained last, at level 50 only).</summary>
        private static readonly Dictionary<eCharacterClass, (string Line, int Cap, float Ratio)[]> Plans = new()
        {
            [eCharacterClass.Wizard] = [(Specs.Cold_Magic, 50, 1f), (Specs.Earth_Magic, 50, 0.4f), (Specs.Fire_Magic, 20, 0f)],
            [eCharacterClass.Friar] = [(Specs.Enhancement, 50, 1f), (Specs.Staff, 50, 0.8f), (Specs.Rejuvenation, 50, 0.4f), (Specs.Parry, 20, 0f)],
            [eCharacterClass.Minstrel] = [(Specs.Instruments, 50, 1f), (Specs.Slash, 50, 0.8f), (Specs.Stealth, 50, 0.3f)],
            // Thrust is the Armsman's second autotrain line: left at 1 until 50, then its first 12 levels are free
            [eCharacterClass.Armsman] = [(Specs.Slash, 50, 1f), (Specs.Shields, 50, 0.8f), (Specs.Parry, 50, 0.5f), (Specs.Thrust, 20, 0f)],
            [eCharacterClass.Infiltrator] = [(Specs.Thrust, 50, 1f), (Specs.Critical_Strike, 50, 0.8f), (Specs.Stealth, 50, 0.7f),
                (Specs.Envenom, 50, 0.7f), (Specs.Dual_Wield, 50, 0.2f)],
            [eCharacterClass.Mercenary] = [(Specs.Thrust, 50, 1f), (Specs.Dual_Wield, 50, 0.8f), (Specs.Parry, 50, 0.5f), (Specs.Slash, 20, 0f)],
            [eCharacterClass.Paladin] = [(Specs.Chants, 50, 1f), (Specs.Shields, 50, 0.8f), (Specs.Slash, 50, 0.5f), (Specs.Parry, 20, 0f)],
            [eCharacterClass.Reaver] = [(Specs.Flexible, 50, 1f), (Specs.Shields, 50, 0.8f), (Specs.Soulrending, 50, 0.7f), (Specs.Slash, 20, 0f)],
            [eCharacterClass.Scout] = [(Specs.Longbow, 50, 1f), (Specs.Shields, 50, 0.8f), (Specs.Stealth, 50, 0.6f), (Specs.Thrust, 20, 0f)],
            // three autotrain lines: one trained every level, two left at 1 until 50
            [eCharacterClass.Warrior] = [(Specs.Axe, 50, 1f), (Specs.Shields, 50, 0.8f), (Specs.Parry, 50, 0.6f), (Specs.Hammer, 20, 0f),
                (Specs.Sword, 20, 0f)],
            [eCharacterClass.Shadowblade] = [(Specs.Axe, 50, 1f), (Specs.Left_Axe, 50, 0.9f), (Specs.Stealth, 50, 0.7f),
                (Specs.Envenom, 50, 0.7f), (Specs.Critical_Strike, 50, 0.2f)],
            [eCharacterClass.Nightshade] = [(Specs.Piercing, 50, 1f), (Specs.Critical_Strike, 50, 0.8f), (Specs.Stealth, 50, 0.7f),
                (Specs.Envenom, 50, 0.7f)],
            // the server's Ranger autotrains Recurve Bow (the period's did not; a player's is the rule here)
            [eCharacterClass.Ranger] = [(Specs.RecurveBow, 50, 1f), (Specs.Pathfinding, 50, 0.8f), (Specs.Stealth, 50, 0.8f),
                (Specs.Piercing, 50, 0.6f)],
            [eCharacterClass.Cleric] = [(Specs.Enhancement, 50, 1f), (Specs.Rejuvenation, 50, 0.6f), (Specs.Smite, 20, 0f)],
            [eCharacterClass.Shaman] = [(Specs.Augmentation, 50, 1f), (Specs.Mending, 50, 0.6f), (Specs.Subterranean, 20, 0f)],
            [eCharacterClass.Druid] = [(Specs.Nurture, 50, 1f), (Specs.Regrowth, 50, 0.6f), (Specs.Nature, 20, 0f)],
        };

        // 4: before the first free level; 8, 20, 48: free levels just reached; 40, 45: half levels; 50.
        private static readonly int[] Levels = [4, 8, 20, 40, 45, 48, 50];

        // ---- the oracle and the pure helpers

        [Test]
        public void TheOracleIsThePlayersOwnCountAndSeesAutotrain()
        {
            ICharacterClass armsman = PlayerSpecPointOracle.ClassOf(eCharacterClass.Armsman);
            ICharacterClass wizard = PlayerSpecPointOracle.ClassOf(eCharacterClass.Wizard);
            Assert.That(armsman.GetAutotrainableSkills(), Is.EquivalentTo(new[] { Specs.Slash, Specs.Thrust }));
            Assert.That(wizard.GetAutotrainableSkills(), Is.Empty);
            int earned = PlayerSpecPointOracle.Earned(armsman, 50);
            // a level 50 player pays nothing for an autotrain line at 12 (a quarter of 50), and 77 for any other line at 12
            Assert.That(Left(armsman, 50, (Specs.Slash, 12)), Is.EqualTo(earned));
            Assert.That(Left(armsman, 50, (Specs.Slash, 12), (Specs.Thrust, 12)), Is.EqualTo(earned));
            Assert.That(Left(armsman, 50, (Specs.Shields, 12)), Is.EqualTo(earned - 77));
            Assert.That(Left(armsman, 50, (Specs.Slash, 50)) - Left(armsman, 50, (Specs.Shields, 50)), Is.EqualTo(77));
            Assert.That(Left(wizard, 50, (Specs.Cold_Magic, 12)), Is.EqualTo(PlayerSpecPointOracle.Earned(wizard, 50) - 77));
            // and it reports an overspent player instead of resetting it
            Assert.That(Left(wizard, 10, (Specs.Cold_Magic, 10), (Specs.Earth_Magic, 10)), Is.Negative);
        }

        [TestCaseSource(nameof(Classes))]
        public void TheBotsCostAndEarnedHelpersAreThePlayersAtEveryLevel(eCharacterClass cls, int multiplier)
        {
            ICharacterClass characterClass = PlayerSpecPointOracle.ClassOf(cls);
            Assert.That(characterClass.SpecPointsMultiplier, Is.EqualTo(multiplier), "the multiplier this case is about");
            IList<string> autoTrain = characterClass.GetAutotrainableSkills();
            for (int level = 1; level <= 50; level++)
            {
                int earned = PlayerSpecPointOracle.Earned(characterClass, level);
                Assert.That(GameBot.PlayerSpecPointTotal(multiplier, level, false), Is.EqualTo(earned), $"earned at {level}");
                if (level is >= 40 and < 50)
                    Assert.That(GameBot.PlayerSpecPointTotal(multiplier, level, true),
                        Is.EqualTo(PlayerSpecPointOracle.Earned(characterClass, level, true)), $"earned at {level}.5");
                // one autotrain line and one other line of the class
                foreach (string line in Plans[cls].Select(p => p.Line).GroupBy(autoTrain.Contains).Select(g => g.First()))
                {
                    bool free = autoTrain.Contains(line);
                    for (int spec = 1; spec <= level; spec++)
                    {
                        int cost = earned - Left(characterClass, level, (line, spec));
                        Assert.That(GameBot.LineCost(spec, level, free), Is.EqualTo(cost), $"{line} {spec} at level {level}");
                        Assert.That(EraBuildCatalog.LineCost(spec, free, level), Is.EqualTo(cost), $"catalog: {line} {spec} at level {level}");
                        if (spec < level)
                            Assert.That(GameBot.NextLevelCost(spec, level, free),
                                Is.EqualTo(Left(characterClass, level, (line, spec)) - Left(characterClass, level, (line, spec + 1))),
                                $"{line} {spec}->{spec + 1} at level {level}");
                        // what the same line cost the player a level earlier, less what it costs now
                        if (free && spec < level)
                            Assert.That(GameBot.AutoTrainRefundAtLevel(spec, level),
                                Is.EqualTo(PlayerSpecPointOracle.Earned(characterClass, level - 1) -
                                           Left(characterClass, level - 1, (line, spec)) - cost),
                                $"{line} {spec}: back on reaching level {level}");
                    }
                }
            }
        }

        // ---- autonomous gamebots

        [TestCaseSource(nameof(Classes))]
        public void AGamebotTrainedEveryLevelHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            GameBot bare = Gamebot(cls, 1);
            GameBot bot = Gamebot(cls, 1);
            Leftover(bare, 0);
            GivePlan(bot);
            for (byte level = 2; level <= 50; level++)
            {
                bare.Level = bot.Level = level;
                bare.SpendSpecPoints(level, (byte)(level - 1));
                bot.SpendSpecPoints(level, (byte)(level - 1));
                if (!Levels.Contains(level))
                    continue;
                Assert.That(Leftover(bare), Is.EqualTo(PlayerSpecPointOracle.Earned(bare.CharacterClass, level)), $"earned, level {level}");
                AssertAPlayersPoints(bot, Leftover(bot), level, $"x{multiplier / 10.0} level {level}");
            }
            AssertThePlanWasTrained(bot);
        }

        [TestCaseSource(nameof(Classes))]
        public void AGamebotArrivingAtItsLevelHasAPlayersPoints(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bare = Gamebot(cls, level); // "+ Lv.N": never saved, the constructor trains it on arrival
                Assert.That(Leftover(bare), Is.EqualTo(PlayerSpecPointOracle.Earned(bare.CharacterClass, level)), $"earned, level {level}");
                GameBot bot = Gamebot(cls, level);
                GivePlan(bot);
                bot.SpendSpecPoints((byte)level, 1); // what the record constructor does for an arrival (ArrivesTrained)
                AssertAPlayersPoints(bot, Leftover(bot), level, $"x{multiplier / 10.0} level {level}");
                if (level == 50)
                    AssertThePlanWasTrained(bot);
            }
        }

        [TestCase(50, 50)]
        [TestCase(45, 42)]
        public void ASavedGamebotGetsBackWhatAPlayerHasAndKeepsEveryTrainedPoint(int level, int trainedAt)
        {
            // An Armsman saved by a build before 1.2: Slash 40, Shields 42, Parry 20, Thrust 1, every level paid for (no
            // autotrain refund) and no half-level points. At load it is counted as a player is (owner 2026-11-17 19:43:
            // existing gamebots too): the difference is its to spend.
            const string specs = "Slash|40;Shields|42;Parry|20;Thrust|1;Polearm|1;Crush|1;Two Handed|1;Crossbows|1";
            int multiplier = PlayerSpecPointOracle.ClassOf(eCharacterClass.Armsman).SpecPointsMultiplier;
            int oldEarned = Enumerable.Range(2, trainedAt - 1).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false));
            int oldSaved = oldEarned - GameBot.PointsToTrain(40) - GameBot.PointsToTrain(42) - GameBot.PointsToTrain(20);
            Assume.That(oldSaved, Is.Not.Negative);
            OfflineWorldBotRecord record = Record(eCharacterClass.Armsman, level, 9_872_000 + level);
            record.SerializedSpecs = specs;
            record.SerializedAbilities = $"trained-level|{trainedAt}|realm-points|0";
            record.UnspentSpecPoints = oldSaved;
            record.LastSavedUtc = DateTime.UtcNow.ToString("O");
            SavedBot.Lines = specs.Split(';').Select(entry => entry.Split('|')[0]).ToArray();
            GameBot bot;
            try { bot = Track(new SavedBot(record)); }
            finally { SavedBot.Lines = null; }

            foreach ((string line, int at) in new[] { (Specs.Slash, 40), (Specs.Shields, 42), (Specs.Parry, 20) })
                Assert.That(bot.GetBaseSpecLevel(line), Is.GreaterThanOrEqualTo(at), $"{line}: never untrained");
            // a player trained at the same level with the same lines has exactly what the bot has now
            int left = PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, trainedAt, false, PlayerSpecPointOracle.Lines(bot));
            Assert.That(Leftover(bot), Is.EqualTo(left), EraBuild.Format(PlayerSpecPointOracle.Lines(bot)));
            Assert.That(Left(bot.CharacterClass, trainedAt, (Specs.Slash, 40), (Specs.Shields, 42), (Specs.Parry, 20)),
                Is.GreaterThan(oldSaved), "the autotrain refund and the half levels it had not had");
            Assert.That(_logs.Lines.Any(l => l.StartsWith("AUTONOMOUS_SPEC_REFUND ") && l.Contains($"saved={oldSaved} ")), Is.True);
            if (level == trainedAt)
            {
                // at 50 it never trains again: the points are spent at once, along its plan, and Thrust takes its free 12
                Assert.That(bot.GetBaseSpecLevel(Specs.Thrust), Is.EqualTo(12));
                Assert.That(PlayerSpecPointOracle.Lines(bot).Sum(l => l.Value), Is.GreaterThan(40 + 42 + 20 + 5 + 12));
            }
            else
            {
                // levels 43-45 are still to train: the points wait for the trainer, with those levels' own
                Assert.That(bot.GetBaseSpecLevel(Specs.Thrust), Is.EqualTo(1));
                bot.SpendSpecPoints((byte)level, (byte)trainedAt);
                AssertAPlayersPoints(bot, Leftover(bot), level, "trained at the next visit");
            }
            // loaded again, nothing more is given
            GameBot again;
            OfflineWorldBotRecord saved = bot.PrepareAutonomousStateSnapshot();
            SavedBot.Lines = specs.Split(';').Select(entry => entry.Split('|')[0]).ToArray();
            try { again = Track(new SavedBot(saved)); }
            finally { SavedBot.Lines = null; }
            Assert.That(Leftover(again), Is.EqualTo(Leftover(bot)));
            Assert.That(PlayerSpecPointOracle.Lines(again), Is.EquivalentTo(PlayerSpecPointOracle.Lines(bot)));
        }

        [Test]
        public void TheFirstLoadLogsThirtyRefundsAndOneSummaryLine()
        {
            const string specs = "Slash|40;Shields|42;Parry|20;Thrust|1";
            int saved = 0;
            for (int i = 0; i < 40; i++)
            {
                OfflineWorldBotRecord record = Record(eCharacterClass.Armsman, 50, 9_873_000 + i);
                record.SerializedSpecs = specs;
                record.SerializedAbilities = "trained-level|50|realm-points|0";
                record.UnspentSpecPoints = saved;
                record.LastSavedUtc = DateTime.UtcNow.ToString("O");
                SavedBot.Lines = specs.Split(';').Select(entry => entry.Split('|')[0]).ToArray();
                try { Track(new SavedBot(record)); }
                finally { SavedBot.Lines = null; }
            }
            Assert.That(_logs.Lines.Count(l => l.StartsWith("AUTONOMOUS_SPEC_REFUND ")), Is.EqualTo(GameBot.SpecRefundLog.PerBotLines));
            string summary = GameBot.SpecRefundLog.Summary();
            int each = Left(PlayerSpecPointOracle.ClassOf(eCharacterClass.Armsman), 50, (Specs.Slash, 40), (Specs.Shields, 42), (Specs.Parry, 20));
            Assert.That(summary, Is.EqualTo($"AUTONOMOUS_SPEC_REFUND_SUMMARY bots=40 points={40 * each} shown=30 classes=\"Armsman:40/{40 * each}\""));
            Assert.That(GameBot.SpecRefundLog.Summary(), Is.Null, "once");
        }

        [Test]
        public void ASavedGamebotWithAPlayersPointsGetsNothingMore()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Wizard, 30, 9_872_100);
            record.SerializedSpecs = "Cold Magic|20";
            record.SerializedAbilities = "trained-level|30|realm-points|0";
            record.UnspentSpecPoints = PlayerSpecPointOracle.Earned(PlayerSpecPointOracle.ClassOf(eCharacterClass.Wizard), 30) -
                GameBot.PointsToTrain(20) + 5;   // more than a player: kept, never taken away
            record.LastSavedUtc = DateTime.UtcNow.ToString("O");
            SavedBot.Lines = ["Cold Magic", "Earth Magic", "Fire Magic"];
            GameBot bot;
            try { bot = Track(new SavedBot(record)); }
            finally { SavedBot.Lines = null; }
            Assert.That(Leftover(bot), Is.EqualTo(record.UnspentSpecPoints));
            Assert.That(_logs.Lines.Any(l => l.StartsWith("AUTONOMOUS_SPEC_REFUND ")), Is.False);
        }

        // ---- hired companions (True 1.65): their player spends the points in the companion's spec window

        [TestCaseSource(nameof(Classes))]
        public void AHiredCompanionHasAPlayersPointsToSpend(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Hired(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                Assert.That(bot.IsHiredCompanion, Is.True);
                Assert.That(bot.CompanionSpecPoints, Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)), $"earned, level {level}");
                // its player trains every line of the plan a little: the top line to 21, the others to 13 and 5
                (string Line, int Cap, float Ratio)[] plan = Plans[cls];
                for (int i = 0; i < plan.Length; i++)
                {
                    Specialization line = AddLine(bot, plan[i].Line);
                    int target = Math.Min(level, i == 0 ? 21 : i % 2 == 1 ? 13 : 5);
                    while (line.Level < target && bot.CompanionTrainCost(line) <= bot.CompanionSpecPoints)
                    {
                        int before = bot.CompanionSpecPoints, cost = bot.CompanionTrainCost(line);
                        // a free level costs nothing, as /train gives it to a player
                        Assert.That(cost, Is.EqualTo(bot.IsAutoTrainLine(line) && line.Level < level / 4 ? 0 : line.Level + 1));
                        Assert.That(bot.CompanionTrain(line), Is.Null, $"{line.KeyName} {line.Level + 1}");
                        Assert.That(before - bot.CompanionSpecPoints, Is.EqualTo(cost), $"{line.KeyName} {line.Level}: the cost shown");
                    }
                    if (line.Level < target)
                        Assert.That(bot.CompanionTrain(line), Is.Not.Null, "refused, not trained on credit");
                    AssertAPlayersPoints(bot, bot.CompanionSpecPoints, level, $"x{multiplier / 10.0} level {level}, {line.KeyName} trained");
                }
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
                Assert.That(before.CompanionSpecPoints, Is.EqualTo(PlayerSpecPointOracle.Earned(before.CharacterClass, level)), $"{level}, before");
                Assert.That(at.CompanionSpecPoints, Is.EqualTo(PlayerSpecPointOracle.Earned(at.CharacterClass, level, true)), $"{level}.5");
            }
            GameBot fifty = Hired(cls, 50, GamePlayer.GetExperienceAmountForLevel(50) - 1);
            Assert.That(fifty.CompanionSpecPoints, Is.EqualTo(PlayerSpecPointOracle.Earned(fifty.CharacterClass, 50)), "no 50.5");
        }

        [TestCaseSource(nameof(Classes))]
        public void AHiredCompanionsRespecGivesBackAPlayersTotal(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = Hired(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                foreach ((string name, _, _) in Plans[cls])
                    AddLine(bot, name);
                // the first two lines of the plan are trained, the others stay at 1
                foreach ((string name, _, _) in Plans[cls].Take(2))
                {
                    Specialization line = bot.GetSpecializationByName(name);
                    while (line.Level < Math.Min(level, 21) && bot.CompanionTrainCost(line) <= bot.CompanionSpecPoints)
                        Assert.That(bot.CompanionTrain(line), Is.Null, $"train {name} {line.Level + 1}");
                }
                Assert.That(bot.GetBaseSpecLevel(Plans[cls][0].Line), Is.GreaterThan(1));
                AssertAPlayersPoints(bot, bot.CompanionSpecPoints, level, "trained");
                HashSet<string> trained = bot.GetSpecList().Where(spec => spec.Level > 1).Select(spec => spec.KeyName).ToHashSet();

                bot.CompanionRespec();
                // as a player's respec leaves them: a trained autotrain line at a quarter of the level from level 8
                // (GamePlayer.RespecSingleLine), a line never trained where it was (RespecAllLines skips it)
                foreach (Specialization spec in bot.GetSpecList())
                    Assert.That(spec.Level, Is.EqualTo(trained.Contains(spec.KeyName) && bot.IsAutoTrainLine(spec) && level >= 8
                        ? level / 4 : 1), spec.KeyName);
                Assert.That(bot.CompanionSpecPoints, Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)),
                    $"x{multiplier / 10.0} level {level}: every point back");
                AssertAPlayersPoints(bot, bot.CompanionSpecPoints, level, "respecced");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void ASavedCompanionLoadsWithEveryTrainedPointAndAPlayersTotal(eCharacterClass cls, int multiplier)
        {
            // Saved by a build before 1.2: trained at level 42 with no half-level points and every line level paid for
            // (now level 45, 3 levels untrained), or trained at its level.
            string first = Plans[cls][0].Line, second = Plans[cls][1].Line;
            foreach ((int trainedAt, int level) in new[] { (42, 45), (50, 50), (45, 45) })
            {
                string specs = $"{first}|30;{second}|12";
                int spent = GameBot.PointsToTrain(30) + GameBot.PointsToTrain(12);
                int oldEarned = Enumerable.Range(2, trainedAt - 1).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false));
                var record = HiredRecord(cls, level, GamePlayer.GetExperienceAmountForLevel(level - 1));
                record.SerializedSpecs = specs;
                record.SerializedAbilities = $"trained-level|{trainedAt}|realm-points|0";
                record.UnspentSpecPoints = oldEarned - spent;
                record.LastSavedUtc = DateTime.UtcNow.ToString("O");
                GameBot bot = Track(new LineBot(record));
                // The skill tables give the lines; the constructor's own loader then restores the saved levels.
                AddLine(bot, first);
                AddLine(bot, second);
                typeof(GameBot).GetMethod("LoadPersistedSpecs", Hidden).Invoke(bot, [record.SerializedSpecs]);
                int oldToSpend = oldEarned - spent +
                    Enumerable.Range(trainedAt + 1, level - trainedAt).Sum(l => GameBot.SpecPointsForLevel(multiplier, l, false));

                Assert.That(bot.GetBaseSpecLevel(first), Is.EqualTo(30), "no trained point is lost");
                Assert.That(bot.GetBaseSpecLevel(second), Is.EqualTo(12), "no trained point is lost");
                AssertAPlayersPoints(bot, bot.CompanionSpecPoints, level, $"{trainedAt}->{level}");
                Assert.That(bot.CompanionSpecPoints, Is.GreaterThan(oldToSpend), "the half levels, and the autotrain levels, to spend");
                Assert.That(bot.PrepareAutonomousStateSnapshot().SerializedSpecs.Split(';'), Is.EquivalentTo(specs.Split(';')),
                    "saved again unchanged");
            }
        }

        [Test]
        public void ACompanionsTrainedLinesAreNeverTakenAway()
        {
            // More trained than a player could have (a corrupt or hand-edited save): nothing is reset, nothing goes negative.
            GameBot bot = Hired(eCharacterClass.Wizard, 10, GamePlayer.GetExperienceAmountForLevel(9));
            Specialization line = AddLine(bot, Specs.Cold_Magic);
            line.Level = 10;
            AddLine(bot, Specs.Earth_Magic).Level = 10;
            Assert.That(PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, 10, false, PlayerSpecPointOracle.Lines(bot)), Is.Negative);
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
                Assert.That(Leftover(bot), Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)), $"earned, level {level}");
                GivePlan(bot);
                bot.SpendSpecPoints((byte)level, 0); // the constructor's training, now with lines to train
                AssertAPlayersPoints(bot, Leftover(bot), level, $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void ASpawnHelperHasAPlayersPointsAndKeepsThemWhenPromotedToFifty(eCharacterClass cls, int multiplier)
        {
            foreach (int level in Levels)
            {
                GameBot bot = OwnerBot(cls, ownerLevel: (byte)level, botLevel: 0, temporary: true);
                Assert.That(Leftover(bot), Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)), $"earned, level {level}");
                GivePlan(bot);
                bot.SpendSpecPoints((byte)level, 0);
                AssertAPlayersPoints(bot, Leftover(bot), level, $"x{multiplier / 10.0} level {level}");
            }
            foreach (byte from in new byte[] { 40, 47, 49 })
            {
                // TryApplyEndgameCompanionUpgrade's training step: before 1.2 a helper promoted from 49 missed the 49.5 points.
                GameBot helper = OwnerBot(cls, ownerLevel: from, botLevel: 0, temporary: true);
                GivePlan(helper);
                helper.SpendSpecPoints(from, 0);
                helper.Level = 50;
                helper.SpendSpecPoints(50, from);
                AssertAPlayersPoints(helper, Leftover(helper), 50, $"promoted from {from}");
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
                Assert.That(Leftover(bot), Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)), $"earned, level {level}");
                GivePlan(bot);
                bot.SpendSpecPoints((byte)level, 0);
                AssertAPlayersPoints(bot, Leftover(bot), level, $"x{multiplier / 10.0} level {level}");
            }
        }

        [TestCase(eCharacterClass.Cleric)]
        [TestCase(eCharacterClass.Shaman)]
        [TestCase(eCharacterClass.Druid)]
        public void ABuffbotHasAPlayersPoints(eCharacterClass cls)
        {
            // Buffbots are only these classes, all x1.0 and without autotrain lines.
            foreach (int level in Levels)
            {
                OfflineWorldBotRecord record = Record(cls, level, 0);
                record.Realm = (int)(cls == eCharacterClass.Cleric ? eRealm.Albion : cls == eCharacterClass.Shaman ? eRealm.Midgard : eRealm.Hibernia);
                record.RaceId = (int)(cls == eCharacterClass.Cleric ? eRace.Briton : cls == eCharacterClass.Shaman ? eRace.Norseman : eRace.Celt);
                GameBot bot = Track(GameBot.CreateNoLiferBuffBot(record, 1));
                Assert.That(bot.IsNoLiferBuffBot, Is.True);
                Assert.That(bot.CharacterClass.SpecPointsMultiplier, Is.EqualTo(10));
                Assert.That(bot.CharacterClass.GetAutotrainableSkills(), Is.Empty);
                Assert.That(Leftover(bot), Is.EqualTo(PlayerSpecPointOracle.Earned(bot.CharacterClass, level)), $"earned, level {level}");
                GivePlan(bot);
                bot.SpendSpecPoints((byte)level, 0);
                AssertAPlayersPoints(bot, Leftover(bot), level, $"level {level}");
            }
        }

        // ---- helpers

        /// <summary>The bot has, unspent, what a player of its class and level has with the same lines trained.</summary>
        private static void AssertAPlayersPoints(GameBot bot, int unspent, int level, string what)
        {
            Assert.That(bot.Level, Is.EqualTo(level), what);
            Assert.That(unspent, Is.EqualTo(PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, level, false,
                PlayerSpecPointOracle.Lines(bot))), $"{what}: {EraBuild.Format(PlayerSpecPointOracle.Lines(bot))}");
        }

        /// <summary>At 50 the plan is trained as far as the points go, and every autotrain line past its free levels - so the
        /// comparison above did include them.</summary>
        private static void AssertThePlanWasTrained(GameBot bot)
        {
            (string Line, int Cap, float Ratio)[] plan = Plans[(eCharacterClass)bot.CharacterClass.ID];
            Assert.That(bot.GetBaseSpecLevel(plan[0].Line), Is.EqualTo(50), plan[0].Line);
            foreach (Specialization spec in bot.GetSpecList().Where(bot.IsAutoTrainLine))
                Assert.That(spec.Level, Is.GreaterThanOrEqualTo(GameBot.AutoTrainLevel(50)), spec.KeyName);
            int cheapest = bot.GetSpecList().Where(spec => spec.Level < plan.First(p => p.Line == spec.KeyName).Cap)
                .Select(spec => GameBot.NextLevelCost(spec.Level, 50, bot.IsAutoTrainLine(spec))).DefaultIfEmpty(int.MaxValue).Min();
            Assert.That(Leftover(bot), Is.LessThan(cheapest), "nothing more it could have trained");
        }

        private static int Left(ICharacterClass cls, int level, params (string Line, int Level)[] lines) =>
            PlayerSpecPointOracle.PointsLeft(cls, level, false, lines.Select(l => new KeyValuePair<string, int>(l.Line, l.Level)));

        /// <summary>The class's lines at 1, its plan, and nothing unspent: ready for the bot's own training.</summary>
        private static void GivePlan(GameBot bot)
        {
            var plan = new BotSpec();
            foreach ((string line, int cap, float ratio) in Plans[(eCharacterClass)bot.CharacterClass.ID])
            {
                plan.SpecLines.Add(new BotSpecLine(line, (byte)cap, ratio));
                AddLine(bot, line);
            }
            typeof(GameBot).GetProperty(nameof(GameBot.BotSpec)).SetValue(bot, plan);
            Leftover(bot, 0);
        }

        private static OfflineWorldBotRecord Record(eCharacterClass cls, int level, long botId) => new()
        {
            BotId = botId, Name = $"Points{cls}{level}{botId}", Realm = (int)RealmOf(cls), ClassId = (int)cls,
            RaceId = (int)RaceOf(cls), Level = level, Health = 100, Mana = 10, Endurance = 100,
        };

        private static eRealm RealmOf(eCharacterClass cls) =>
            (eRealm)(Array.IndexOf(EraBuildCatalog.RealmNames, EraBuildCatalog.Default.GetClass((int)cls)?.Realm ?? "Albion") + 1);

        private static eRace RaceOf(eCharacterClass cls) =>
            RealmOf(cls) switch { eRealm.Midgard => eRace.Norseman, eRealm.Hibernia => eRace.Celt, _ => eRace.Briton };

        private GameBot Gamebot(eCharacterClass cls, int level) => Track(new GameBot(Record(cls, level, 0)));

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
            return Track(new GameBot(owner, (byte)cls, $"Points{cls}", (byte)RaceOf(cls), temporaryGroupHelper: temporary, botLevel: botLevel));
        }

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
