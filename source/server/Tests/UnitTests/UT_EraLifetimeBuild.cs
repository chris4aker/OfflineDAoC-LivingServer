using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.UnitTests
{
    /// <summary>
    /// Era builds 1.2, WP3 (docs/ERA-BUILDS-1.2.md, "The build draw" and "Existing bots keep class and build"): a new
    /// gamebot takes an era build for life at its first load; a saved plan is restored exactly and never re-drawn; a bot
    /// that trained lines before it had a plan keeps every point; companions, hired companions, Epic Special Forces,
    /// buffbots and the custom class keep today's builds. Tests only: none of this has been seen in game yet.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EraLifetimeBuild
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, ReadOnly>();
        private static EraBuildCatalog Catalog => EraBuildCatalog.Default;

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
        private TestLogCapture _logs;
        private TestPropertyCalculators _calculators;
        private bool _eraBuilds;
        private int _offMeta;
        private readonly List<GameBot> _constructed = new();

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            _eraBuilds = Properties.BOT_ERA_BUILDS;
            _offMeta = Properties.BOT_ERA_OFFMETA_PERCENT;
            Properties.BOT_ERA_BUILDS = true;
            Properties.BOT_ERA_OFFMETA_PERCENT = 10;
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
            Properties.BOT_ERA_BUILDS = _eraBuilds;
            Properties.BOT_ERA_OFFMETA_PERCENT = _offMeta;
            _logs.Dispose();
            _calculators.Dispose();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        // ---- the plan made from an era build

        [Test]
        public void EveryEraBuildMakesAPlanOfItsLinesWithItsFamilyAndRole()
        {
            foreach (EraBuild build in Catalog.Classes.SelectMany(c => c.Builds))
            {
                var cls = (eCharacterClass)Catalog.GetClass(build.ClassName).ClassId;
                for (int i = 0; i < 30; i++)
                {
                    BotSpec plan = BotSpec.FromEraBuild(cls, build, null, 1000 + i);
                    Assert.That(plan, Is.Not.Null, build.Id);
                    Assert.That(plan.EraBuildId, Is.EqualTo(build.Id));
                    Assert.That(plan.EraRole, Is.EqualTo(build.Role));
                    Assert.That(plan.SpecLines.Select(l => (l.Spec, (int)l.SpecCap)),
                        Is.EquivalentTo(build.Specs.Select(s => (s.Key, s.Value))), build.Id);
                    foreach (BotSpecLine line in plan.SpecLines)
                        Assert.That(line.levelRatio, Is.EqualTo(EraBuildCatalog.DerivedRatio(build, line.Spec)), $"{build.Id} {line.Spec}");
                    Assert.That(plan.SpecLines.Single(l => l.Spec == build.Specs[0].Key).levelRatio, Is.EqualTo(1f),
                        $"{build.Id}: the top line rises with every level");
                }
            }
        }

        [Test]
        public void ABotAndABuildAlwaysMakeTheSamePlan()
        {
            // Review of WP3-5: the class file's own picks (its variant, an untrained weapon slot) are seeded by the bot id
            // and the build, so a crash before the first save makes the same plan again.
            foreach (EraBuild build in Catalog.Classes.SelectMany(c => c.Builds))
            {
                var cls = (eCharacterClass)Catalog.GetClass(build.ClassName).ClassId;
                for (long id = 1; id <= 5; id++)
                {
                    string first = BotLifetimeBuild.Encode(BotSpec.FromEraBuild(cls, build, null, id * 7919));
                    for (int again = 0; again < 5; again++)
                    {
                        Util.Random(100); // other draws in between must not matter
                        Assert.That(BotLifetimeBuild.Encode(BotSpec.FromEraBuild(cls, build, null, id * 7919)), Is.EqualTo(first),
                            $"{build.Id} bot {id * 7919}");
                    }
                }
            }
            // The seeded scope ends with the plan: the next draws are the server's own again.
            var after = Enumerable.Range(0, 20).Select(_ => Util.Random(1_000_000)).ToArray();
            Assert.That(after.Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void TheClassGuardsKeepTheirOwnPlans()
        {
            BotSpec savageHands = BotSpec.FromEraBuild(eCharacterClass.Savage, Catalog.GetBuild("savage-h2h-sav"));
            Assert.That(savageHands, Is.InstanceOf<SavageBotSpec>());
            Assert.That(savageHands.WeaponOneType, Is.EqualTo(eObjectType.HandToHand));
            Assert.That(savageHands.SpecType, Is.EqualTo(eSpecType.DualWield));
            BotSpec savageSword = BotSpec.FromEraBuild(eCharacterClass.Savage, Catalog.GetBuild("savage-sword-sav"));
            Assert.That(savageSword.WeaponOneType, Is.EqualTo(eObjectType.Sword));
            Assert.That(savageSword.Is2H, Is.True);
            Assert.That(SavageBotSpec.AlignWithPersistedSpecs(savageSword, "Sword|1;Savagery|1"), Is.False,
                "an untrained Savage keeps the era weapon");

            BotSpec bonedancer = BotSpec.FromEraBuild(eCharacterClass.Bonedancer, Catalog.GetBuild("bonedancer-army-supp"), null, 77);
            Assert.That(bonedancer, Is.InstanceOf<BonedancerBotSpec>());
            Assert.That(bonedancer.SpecType, Is.EqualTo(eSpecType.ArmyBone));
            Assert.That(bonedancer.SpecLines.Select(l => l.Spec), Is.EqualTo(new[] { Specs.BoneArmy, Specs.Suppression, Specs.Darkness }));

            // The Necromancer's era plans: UT_NecromancerLifetime.

            // Bots never play the custom class: no era plan for it, whatever build is asked for.
            Assert.That(BotSpec.FromEraBuild(eCharacterClass.Sluaghbinder, Catalog.GetBuild("druid-regrow-nurt")), Is.Null);
            Assert.That(Catalog.GetClass((int)eCharacterClass.Sluaghbinder), Is.Null);
            Assert.That(EraLifetimeBuild.Applies(true, (int)eCharacterClass.Sluaghbinder, false, false, Catalog), Is.False);
        }

        // ---- who takes the draw

        [Test]
        public void OnlyPersistentGamebotsWithoutAPlanTakeTheDraw()
        {
            Assert.That(EraLifetimeBuild.Applies(true, (int)eCharacterClass.Sorcerer, false, false, Catalog), Is.True);
            Assert.That(EraLifetimeBuild.Applies(false, (int)eCharacterClass.Sorcerer, false, false, Catalog), Is.False, "bot_era_builds off");
            Assert.That(EraLifetimeBuild.Applies(true, (int)eCharacterClass.Sorcerer, true, false, Catalog), Is.False,
                "Epic Special Forces and buffbots live in memory only");
            Assert.That(EraLifetimeBuild.Applies(true, (int)eCharacterClass.Sorcerer, false, true, Catalog), Is.False,
                "a hired companion's player trains it");
            Assert.That(EraLifetimeBuild.Applies(true, (int)eCharacterClass.Sorcerer, false, false, EraBuildCatalog.Parse("{")), Is.False,
                "an unreadable data file: today's picker");
        }

        [Test]
        public void ANewBotsDrawIsSeededAndTenPercentAreOffMeta()
        {
            int offMeta = 0, total = 20_000;
            var counts = new Dictionary<string, int>();
            for (long id = 1; id <= total; id++)
            {
                EraBuildDecision first = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Sorcerer, id, [], 10);
                EraBuildDecision again = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Sorcerer, id, [], 10);
                Assert.That(again.Draw, Is.EqualTo(first.Draw));
                Assert.That(again.Build?.Id, Is.EqualTo(first.Build?.Id), "a crash before the first save draws the same build");
                Assert.That(again.Jitter, Is.EqualTo(first.Jitter));
                if (first.Draw == EraBuildDraw.OffMeta)
                {
                    offMeta++;
                    Assert.That(first.Plan, Is.Null, "today's class-file build");
                    continue;
                }
                Assert.That(first.Draw, Is.EqualTo(EraBuildDraw.Era));
                counts[first.Build.Id] = counts.GetValueOrDefault(first.Build.Id) + 1;
            }
            Assert.That(offMeta * 100.0 / total, Is.EqualTo(10).Within(0.6));
            foreach (EraBuild build in Catalog.GetClass("Sorcerer").Builds)
                Assert.That(counts[build.Id] * 1.0 / (total - offMeta), Is.EqualTo(build.Weight).Within(0.015), build.Id);
            Assert.That(Enumerable.Range(1, 2000).Count(id => EraLifetimeBuild.Choose(Catalog, eCharacterClass.Wizard, id, [], 0).Draw ==
                EraBuildDraw.OffMeta), Is.Zero, "0: era builds only");
            Assert.That(Enumerable.Range(1, 200).All(id => EraLifetimeBuild.Choose(Catalog, eCharacterClass.Wizard, id, [], 100).Draw ==
                EraBuildDraw.OffMeta), Is.True);
        }

        [Test]
        public void ABotThatTrainedBeforeItHadAPlanTakesTheNearestBuildOrTodaysPicker()
        {
            var trained = new[] { Kv(Specs.Mind_Magic, 30), Kv(Specs.Body_Magic, 22), Kv(Specs.Matter_Magic, 1) };
            EraBuildDecision nearest = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Sorcerer, 5, trained, 10);
            Assert.That(nearest.Draw, Is.EqualTo(EraBuildDraw.Nearest));
            Assert.That(nearest.Build.Id, Is.EqualTo("sorcerer-mind-body"));
            Assert.That(nearest.Jitter, Is.Empty, "nothing moved: the bot already chose");
            Assert.That(nearest.Plan.EraBuildId, Is.EqualTo("sorcerer-mind-body"));

            var odd = new[] { Kv(Specs.Matter_Magic, 30), Kv(Specs.Body_Magic, 22) };
            EraBuildDecision legacy = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Sorcerer, 5, odd, 10);
            Assert.That(legacy.Draw, Is.EqualTo(EraBuildDraw.Legacy));
            Assert.That(legacy.Plan, Is.Null, "today's picker with AlignWithInvestedWeapons");

            // Its weapons follow its own points: an Armsman that trained Thrust over the build's Slash keeps Thrust.
            var armsman = new[] { Kv(Specs.Polearms, 40), Kv(Specs.Shields, 30), Kv(Specs.Thrust, 20), Kv(Specs.Slash, 1) };
            EraBuildDecision arms = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Armsman, 9, armsman, 10);
            Assert.That(arms.Draw, Is.EqualTo(EraBuildDraw.Nearest));
            Assert.That(arms.Plan.WeaponTwoType, Is.EqualTo(eObjectType.PolearmWeapon));
        }

        [Test]
        public void TrainingAnEraPlanNeverTakesAPointBack()
        {
            GameBot bot = Make(eCharacterClass.Sorcerer, 50, 41);
            BotSpec plan = BotSpec.FromEraBuild(eCharacterClass.Sorcerer, Catalog.GetBuild("sorcerer-mind-body"));
            SetPlan(bot, plan);
            AddLines(bot, [Specs.Mind_Magic, Specs.Body_Magic, Specs.Matter_Magic]);
            bot.GetSpecializationByName(Specs.Mind_Magic).Level = 48;   // above the build's 44
            bot.GetSpecializationByName(Specs.Matter_Magic).Level = 20; // a line the build doesn't train
            Leftover(bot, 600);
            bot.SpendSpecPoints(50, 49);
            Assert.That(bot.GetBaseSpecLevel(Specs.Mind_Magic), Is.EqualTo(48));
            Assert.That(bot.GetBaseSpecLevel(Specs.Matter_Magic), Is.EqualTo(20));
            Assert.That(bot.GetBaseSpecLevel(Specs.Body_Magic), Is.GreaterThan(1));
        }

        // ---- the record constructor

        [Test]
        public void ANewRecordGetsAnEraPlanSavedAtOnceAndOneAssignedLine()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Sorcerer, 1, 424_242);
            GameBot bot = New(record);
            EraBuildDecision expected = EraLifetimeBuild.Choose(Catalog, eCharacterClass.Sorcerer, 424_242, [], 10);
            Assert.That(expected.Draw, Is.EqualTo(EraBuildDraw.Era), "this id draws an era build");
            Assert.That(bot.BotSpec.EraBuildId, Is.EqualTo(expected.Build.Id));
            Assert.That(record.SerializedBuildPlan, Does.Contain($"\"EraBuildId\":\"{expected.Build.Id}\""), "saved at once");
            string[] assigned = _logs.Lines.Where(l => l.StartsWith("ERA_BUILD_ASSIGNED ")).ToArray();
            Assert.That(assigned, Has.Length.EqualTo(1));
            Assert.That(assigned[0], Does.StartWith($"ERA_BUILD_ASSIGNED bot=\"{record.Name}\" id=424242 class=Sorcerer level=1 build={expected.Build.Id} "));
            Assert.That(assigned[0], Does.Contain($"role={expected.Build.Role} draw=era jitter=\"{expected.Jitter}\""));
        }

        [Test]
        public void ASavedPlanIsRestoredExactlyAndNeverReDrawn()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Healer, 1, 515_151);
            New(record);
            string saved = record.SerializedBuildPlan;
            int lines = _logs.Lines.Count(l => l.StartsWith("ERA_BUILD_ASSIGNED "));
            Properties.BOT_ERA_OFFMETA_PERCENT = 100; // a re-draw would now give today's build
            GameBot again = New(record);
            Assert.That(record.SerializedBuildPlan, Is.EqualTo(saved));
            Assert.That(BotLifetimeBuild.Encode(again.BotSpec), Is.EqualTo(saved));
            Assert.That(_logs.Lines.Count(l => l.StartsWith("ERA_BUILD_ASSIGNED ")), Is.EqualTo(lines), "no line for a saved plan");
        }

        [Test]
        public void APlanSavedBeforeOnePointTwoReadsAsTodaysBuildAndIsKept()
        {
            // A 1.1 plan: no EraBuildId or EraRole in its JSON.
            const string old = "{\"WeaponOneType\":8,\"WeaponTwoType\":0,\"DamageType\":0,\"SpecType\":27,\"Is2H\":true," +
                               "\"SpecLines\":[{\"Spec\":\"Mind Magic\",\"SpecCap\":49,\"levelRatio\":1},{\"Spec\":\"Body Magic\",\"SpecCap\":22,\"levelRatio\":0.2}]}";
            var restored = new BotSpec();
            Assert.That(BotLifetimeBuild.Restore(restored, old), Is.True);
            Assert.That(restored.EraBuildId, Is.Empty);
            Assert.That(restored.EraRole, Is.Empty);
            Assert.That(restored.SpecLines.Select(l => (l.Spec, l.SpecCap)), Is.EqualTo(new[] { ("Mind Magic", 49u), ("Body Magic", 22u) }));

            OfflineWorldBotRecord record = Record(eCharacterClass.Sorcerer, 30, 616_161);
            record.SerializedBuildPlan = old;
            GameBot bot = New(record);
            Assert.That(bot.BotSpec.EraBuildId, Is.Empty, "existing bots keep their build (D1)");
            Assert.That(record.SerializedBuildPlan, Is.EqualTo(old), "not even re-encoded");
            Assert.That(_logs.Lines.Any(l => l.StartsWith("ERA_BUILD_ASSIGNED ")), Is.False);

            BotSpec era = BotSpec.FromEraBuild(eCharacterClass.Sorcerer, Catalog.GetBuild("sorcerer-mind-body"));
            var copy = new BotSpec();
            Assert.That(BotLifetimeBuild.Restore(copy, BotLifetimeBuild.Encode(era)), Is.True);
            Assert.That((copy.EraBuildId, copy.EraRole), Is.EqualTo(("sorcerer-mind-body", "cc-mezzer")), "Restore copies the two fields");
        }

        [Test]
        public void WithTheSwitchOffANewBotGetsTodaysBuild()
        {
            Properties.BOT_ERA_BUILDS = false;
            OfflineWorldBotRecord record = Record(eCharacterClass.Sorcerer, 1, 424_242);
            GameBot bot = New(record);
            Assert.That(bot.BotSpec.EraBuildId, Is.Empty);
            Assert.That(record.SerializedBuildPlan, Is.Not.Empty, "today's lifetime build, saved as before");
            Assert.That(_logs.Lines.Any(l => l.StartsWith("ERA_BUILD_ASSIGNED ")), Is.False);
        }

        [Test]
        public void AnOffMetaBotIsLoggedWithTodaysBuild()
        {
            Properties.BOT_ERA_OFFMETA_PERCENT = 100;
            OfflineWorldBotRecord record = Record(eCharacterClass.Wizard, 1, 7);
            GameBot bot = New(record);
            Assert.That(bot.BotSpec.EraBuildId, Is.Empty);
            Assert.That(_logs.Lines.Single(l => l.StartsWith("ERA_BUILD_ASSIGNED ")),
                Does.Contain($"build=today-{bot.BotSpec.SpecType} label=\"today's class-file build\" role= draw=offmeta jitter=\"\""));
        }

        [Test]
        public void EpicSpecialForcesKeepTheirBuilds()
        {
            OfflineWorldBotRecord force = Record(eCharacterClass.Cleric, 50, 900_001);
            GameBot member = GameBot.CreateEpicSpecialForce(force);
            _constructed.Add(member);
            Assert.That(member.BotSpec.EraBuildId, Is.Empty);
            Assert.That(force.SerializedBuildPlan, Is.Empty, "never saved");
            Assert.That(_logs.Lines.Any(l => l.StartsWith("ERA_BUILD_")), Is.False);
        }

        [Test]
        public void ACustomClassGamebotKeepsItsOwnClassFile()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Sluaghbinder, 1, 31_337);
            record.Realm = (int)eRealm.Hibernia;
            record.RaceId = (int)eRace.Celt;
            GameBot bot = New(record);
            Assert.That(bot.BotSpec, Is.InstanceOf<SluaghbinderBotSpec>());
            Assert.That(bot.BotSpec.EraBuildId, Is.Empty);
            Assert.That(_logs.Lines.Any(l => l.StartsWith("ERA_BUILD_")), Is.False);
        }

        // ---- a "+ Lv.50" arrival trains to the build's points exactly

        [Test]
        public void ALevelFiftyArrivalTrainsEveryEraBuildToItsPointsExactly()
        {
            foreach (EraClass eraClass in Catalog.Classes)
            {
                var cls = (eCharacterClass)eraClass.ClassId;
                GameBot bot = Make(cls, 50, 800_000 + eraClass.ClassId);
                foreach (EraBuild build in eraClass.Builds)
                {
                    BotSpec plan = BotSpec.FromEraBuild(cls, build, Catalog.Jitter(build, 3, out _), 3);
                    SetPlan(bot, plan);
                    AddLines(bot, eraClass.Lines);
                    foreach (string line in eraClass.Lines)
                        bot.GetSpecializationByName(line).Level = 1;
                    Leftover(bot, 0);
                    bot.SpendSpecPoints(50, 1); // what the record constructor does for an arrival (ArrivesTrained)
                    foreach (BotSpecLine line in plan.SpecLines)
                        Assert.That(bot.GetBaseSpecLevel(line.Spec), Is.EqualTo((int)line.SpecCap), $"{build.Id} {line.Spec}");
                    Assert.That(eraClass.Lines.Where(l => plan.SpecLines.All(p => p.Spec != l)).Select(bot.GetBaseSpecLevel),
                        Has.All.EqualTo(1), $"{build.Id}: nothing outside the build");
                    // what a level 50 player of the class has left with the same lines (GamePlayer.VerifySpecPoints: every
                    // point earned, less the lines' cost with the class's autotrain levels free)
                    KeyValuePair<string, int>[] trainedLines =
                        plan.SpecLines.Select(l => new KeyValuePair<string, int>(l.Spec, (int)l.SpecCap)).ToArray();
                    Assert.That(Leftover(bot), Is.EqualTo(PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, 50, false, trainedLines)), build.Id);
                    Assert.That(Leftover(bot), Is.EqualTo(eraClass.PointBudget50 -
                        EraBuildCatalog.BuildCost(trainedLines, eraClass.AutoTrainLines)), build.Id);
                    Assert.That(Leftover(bot), Is.Not.Negative, build.Id);
                    // fully trained (owner 2026-11-17 18:59): what is left buys no further level in any line of the build
                    foreach (BotSpecLine line in plan.SpecLines.Where(l => l.SpecCap < 50))
                    {
                        int next = Leftover(bot) - PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, 50, false,
                            trainedLines.Select(l => l.Key == line.Spec ? new KeyValuePair<string, int>(l.Key, l.Value + 1) : l));
                        Assert.That(Leftover(bot), Is.LessThan(next), $"{build.Id}: {line.Spec} {line.SpecCap + 1} is out of reach");
                    }
                    Assert.That(EraBuildCatalog.IsFullyTrained(trainedLines, eraClass.PointBudget50, eraClass.AutoTrainLines), Is.True, build.Id);
                    string trained = EraLifetimeBuild.TrainedLine(bot.Name, bot.DatabaseID, 50, plan, bot.GetBaseSpecLevel, Leftover(bot));
                    Assert.That(trained, Does.EndWith($"match=yes unspent={Leftover(bot)}"), build.Id);
                }
            }
        }

        // ---- levelling: an era-built gamebot is always as trained as its points allow

        [Test]
        public void ALevellingEraBotNeverHoardsPointsItsBuildCanTake()
        {
            // Every trainer visit, one a level or one every third level, leaves less than the next level of any line of the
            // build still below its points and the bot's level: a player following a template (owner 2026-11-17 18:59).
            foreach (EraClass eraClass in Catalog.Classes)
            {
                var cls = (eCharacterClass)eraClass.ClassId;
                foreach (int step in new[] { 1, 3 })
                {
                    GameBot bot = Make(cls, 1, 810_000 + eraClass.ClassId * 10 + step);
                    foreach (EraBuild build in eraClass.Builds)
                    {
                        BotSpec plan = BotSpec.FromEraBuild(cls, build, Catalog.Jitter(build, 5, out _), 5);
                        SetPlan(bot, plan);
                        AddLines(bot, eraClass.Lines);
                        foreach (string line in eraClass.Lines)
                            bot.GetSpecializationByName(line).Level = 1;
                        Leftover(bot, 0);
                        byte trained = 1;
                        for (byte level = 2; level <= 50; level++)
                        {
                            bot.Level = level;
                            if ((level - 1) % step != 0 && level != 50)
                                continue;
                            bot.SpendSpecPoints(level, trained);
                            trained = level;
                            KeyValuePair<string, int>[] now = PlayerSpecPointOracle.Lines(bot).ToArray();
                            int left = PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, level, false, now);
                            Assert.That(Leftover(bot), Is.EqualTo(left), $"{build.Id} level {level}: a player's points");
                            foreach (BotSpecLine line in plan.SpecLines)
                            {
                                int at = bot.GetBaseSpecLevel(line.Spec);
                                Assert.That(at, Is.LessThanOrEqualTo(Math.Min((int)line.SpecCap, level)), $"{build.Id} {line.Spec}");
                                if (at >= Math.Min((int)line.SpecCap, level))
                                    continue;
                                int next = left - PlayerSpecPointOracle.PointsLeft(bot.CharacterClass, level, false,
                                    now.Select(l => l.Key == line.Spec ? new KeyValuePair<string, int>(l.Key, l.Value + 1) : l));
                                Assert.That(left, Is.LessThan(next), $"{build.Id} level {level} step {step}: {line.Spec} {at + 1} was affordable");
                            }
                        }
                        foreach (BotSpecLine line in plan.SpecLines)
                            Assert.That(bot.GetBaseSpecLevel(line.Spec), Is.EqualTo((int)line.SpecCap), $"{build.Id} {line.Spec} at 50");
                        Assert.That(bot.GetSpecList().Where(s => plan.SpecLines.All(p => p.Spec != s.KeyName)).Select(s => s.Level),
                            Has.All.EqualTo(1), $"{build.Id}: nothing outside the build");
                    }
                }
            }
        }

        [Test]
        public void TheTrainedLineIsLoggedOnceWhenAnEraBotFirstStandsAtFifty()
        {
            BotSpec plan = BotSpec.FromEraBuild(eCharacterClass.Sorcerer, Catalog.GetBuild("sorcerer-mind-body"));
            Assert.That(EraLifetimeBuild.FirstTrainedAtFifty(plan, 1, 50), Is.True, "a \"+ Lv.50\" arrival");
            Assert.That(EraLifetimeBuild.FirstTrainedAtFifty(plan, 49, 50), Is.True, "its trainer visit at 50");
            Assert.That(EraLifetimeBuild.FirstTrainedAtFifty(plan, 50, 50), Is.False, "never again");
            Assert.That(EraLifetimeBuild.FirstTrainedAtFifty(plan, 1, 22), Is.False, "a \"+ Lv.20\" arrival waits for 50");
            Assert.That(EraLifetimeBuild.FirstTrainedAtFifty(new BotSpec(), 1, 50), Is.False, "today's builds don't log it");
            var levels = new Dictionary<string, int> { [Specs.Mind_Magic] = 44, [Specs.Body_Magic] = 29 };
            Assert.That(EraLifetimeBuild.TrainedLine("Zap", 12, 50, plan, l => levels.GetValueOrDefault(l)), Is.EqualTo(
                "ERA_BUILD_TRAINED bot=\"Zap\" id=12 build=sorcerer-mind-body level=50 specs=\"Mind Magic:44,Body Magic:29\" " +
                "match=no short=\"Body Magic:29/31\""));
        }

        [Test]
        public void ALevelFiftyArrivalLogsTrainedOnceForLife()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Wizard, 50, 424_243);
            GameBot bot = New(record);
            Assume.That(bot.BotSpec.EraBuildId, Is.Not.Empty, "this id draws an era build");
            // The test world has no skill tables, so the lines can't be trained here: match=no is expected. The exact
            // training is ALevelFiftyArrivalTrainsEveryEraBuildToItsPointsExactly.
            Assert.That(_logs.Lines.Count(l => l.StartsWith($"ERA_BUILD_TRAINED bot=\"{record.Name}\" id=424243 build={bot.BotSpec.EraBuildId} level=50 ")),
                Is.EqualTo(1));
            // the line ends with the points left (here every point a level 50 Wizard has: no line could be trained)
            Assert.That(_logs.Lines.Single(l => l.StartsWith("ERA_BUILD_TRAINED ")),
                Does.EndWith($" unspent={PlayerSpecPointOracle.Earned(bot.CharacterClass, 50)}"));
            OfflineWorldBotRecord saved = JsonSerializer.Deserialize<OfflineWorldBotRecord>(JsonSerializer.Serialize(bot.PrepareAutonomousStateSnapshot()));
            New(saved);
            Assert.That(_logs.Lines.Count(l => l.StartsWith("ERA_BUILD_TRAINED ")), Is.EqualTo(1), "a saved, trained bot never logs it again");
            Assert.That(_logs.Lines.Count(l => l.StartsWith("ERA_BUILD_ASSIGNED ")), Is.EqualTo(1));
        }

        [Test]
        public void ABuffbotKeepsItsBuffSpec()
        {
            OfflineWorldBotRecord record = Record(eCharacterClass.Cleric, 50, 900_002);
            GameBot buffbot = GameBot.CreateNoLiferBuffBot(record, 4242);
            _constructed.Add(buffbot);
            Assert.That(buffbot.BotSpec.EraBuildId, Is.Empty);
            Assert.That(record.SerializedBuildPlan, Is.Empty, "never saved");
            Assert.That(_logs.Lines.Any(l => l.StartsWith("ERA_BUILD_")), Is.False);
        }

        // ---- the census

        [Test]
        public void TheCensusCountsPlansAndClassesAgainstTheirShares()
        {
            string era = BotLifetimeBuild.Encode(BotSpec.FromEraBuild(eCharacterClass.Healer, Catalog.GetBuild("healer-mend-aug")));
            string kept = BotLifetimeBuild.Encode(BotSpec.GetSpec(eCharacterClass.Healer, eSpecType.PacHealer));
            var roster = new List<EraBuildCensus.Entry>();
            for (int i = 0; i < 3; i++) roster.Add(new(2, (int)eCharacterClass.Healer, era));
            roster.Add(new(2, (int)eCharacterClass.Healer, kept));
            roster.Add(new(2, (int)eCharacterClass.Skald, ""));
            roster.Add(new(3, (int)eCharacterClass.Druid, ""));
            IReadOnlyList<string> lines = EraBuildCensus.Lines(roster, Catalog);
            Assert.That(lines, Has.Count.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("ERA_BUILD_CENSUS realm=Albion bots=0 era=0 kept=0 new=0 Cleric=0(0.0%/12%)"));
            Assert.That(lines[1], Does.StartWith("ERA_BUILD_CENSUS realm=Midgard bots=5 era=3 kept=1 new=1 Healer=4(80.0%/15%) Skald=1(20.0%/12%)"));
            Assert.That(lines[2], Does.Contain(" Druid=1(100.0%/12%)"));
            Assert.That(lines[2], Does.Contain(" Sluaghbinder=0(0.0%/0%) eraClasses=\""), "Hibernia always says the custom class is 0");
            // The same count over the era-built bots only (review of WP3-5): the kept Healer and the new Skald are left out.
            Assert.That(lines[1], Does.Contain(" eraClasses=\"Healer=3(100.0%/15%),Skald=0(0.0%/12%),Shadowblade=0(0.0%/10%),"));
            Assert.That(lines[1], Does.EndWith(",Bonedancer=0(0.0%/3%)\""));
            Assert.That(lines[2], Does.EndWith("eraClasses=\"Druid=0(0.0%/12%),Bard=0(0.0%/11%),Nightshade=0(0.0%/11%),Hero=0(0.0%/10%)," +
                "Eldritch=0(0.0%/9%),Ranger=0(0.0%/9%),Blademaster=0(0.0%/8%),Enchanter=0(0.0%/8%),Warden=0(0.0%/8%)," +
                "Champion=0(0.0%/6%),Mentalist=0(0.0%/4%),Animist=0(0.0%/2%),Valewalker=0(0.0%/2%)\""), "no Sluaghbinder among era-built bots");
            Assert.That(EraBuildCensus.PlanKind("{broken"), Is.EqualTo('n'));
        }

        // ---- helpers

        private static KeyValuePair<string, int> Kv(string line, int level) => new(line, level);

        private static OfflineWorldBotRecord Record(eCharacterClass cls, int level, long id)
        {
            eRealm realm = cls switch
            {
                eCharacterClass.Healer or eCharacterClass.Savage or eCharacterClass.Bonedancer => eRealm.Midgard,
                _ => eRealm.Albion,
            };
            return new OfflineWorldBotRecord
            {
                BotId = id, Name = $"Era{cls}{id}", Realm = (int)realm, ClassId = (int)cls,
                RaceId = realm == eRealm.Midgard ? (int)eRace.Norseman : (int)eRace.Briton, Level = level,
                Health = 100, Mana = 10, Endurance = 100,
            };
        }

        private GameBot New(OfflineWorldBotRecord record)
        {
            var bot = new GameBot(record);
            _constructed.Add(bot);
            return bot;
        }

        private GameBot Make(eCharacterClass cls, int level, long id)
        {
            string realmName = Catalog.GetClass((int)cls)?.Realm ?? "Albion";
            OfflineWorldBotRecord record = Record(cls, level, id);
            record.Realm = Array.IndexOf(EraBuildCatalog.RealmNames, realmName) + 1;
            record.RaceId = record.Realm switch { 2 => (int)eRace.Norseman, 3 => (int)eRace.Celt, _ => (int)eRace.Briton };
            return New(record);
        }

        private static void SetPlan(GameBot bot, BotSpec plan) =>
            typeof(GameBot).GetProperty(nameof(GameBot.BotSpec)).SetValue(bot, plan);

        private static void AddLines(GameBot bot, IEnumerable<string> lines)
        {
            MethodInfo add = typeof(GameBot).GetMethod("AddSpecialization", Hidden, [typeof(Specialization), typeof(bool)]);
            foreach (string line in lines)
                if (bot.GetSpecializationByName(line) == null)
                    add.Invoke(bot, [new Specialization(line, line, 0), false]);
        }

        private static FieldInfo LeftoverField => typeof(GameBot).GetField("m_leftOverSpecPoints", Hidden);
        private static int Leftover(GameBot bot) => (int)LeftoverField.GetValue(bot);
        private static void Leftover(GameBot bot, int value) => LeftoverField.SetValue(bot, value);
    }
}
