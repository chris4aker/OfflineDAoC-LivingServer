using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using NUnit.Framework;
using static DOL.UnitTests.GearTestBots;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #540 (1.2.2 PC log: blocking gen-2 collections froze the server 1.1-2.4 s, and handles rose 2,134 to 5,260 in
    /// 4.5 hours). The gear checks' allocations (the log's largest: ValueTuple`2[Int32,Int32][] from the bonus pairs, the
    /// copied loadouts and the sorted spec lists) are gone without changing a single score, and the stat tables no longer
    /// hold a Lock that keeps a Windows event. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_Issue540GcHandles
    {
        private GameServer _previous;
        private bool _crafters;

        private static readonly eInventorySlot[] Slots = BotGearSet.TemplateSlots.Concat(
        [
            eInventorySlot.Cloak, eInventorySlot.Neck, eInventorySlot.Waist, eInventorySlot.Jewelry,
            eInventorySlot.LeftBracer, eInventorySlot.RightBracer, eInventorySlot.LeftRing, eInventorySlot.RightRing,
        ]).ToArray();

        private static readonly eProperty[] Types =
        [
            eProperty.Strength, eProperty.Constitution, eProperty.Dexterity, eProperty.Quickness, eProperty.Intelligence,
            eProperty.Piety, eProperty.MaxHealth, eProperty.MaxMana, eProperty.Acuity, eProperty.Resist_Body,
            eProperty.Resist_Heat, eProperty.ArmorFactor, eProperty.PowerPool, eProperty.Skill_Slashing,
        ];

        [SetUp]
        public void SetUp()
        {
            _previous = UseServer();
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            CraftingEconomyProperties.BOT_CRAFTERS = true;
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            GameServer.LoadTestDouble(_previous);
        }

        private static GearBot RandomBot(Random random)
        {
            GearBot bot = Bot(random.Next(1, 51), random.Next(2) == 0 ? new ClassHero() : new ClassEnchanter());
            foreach (eInventorySlot slot in Slots)
                if (random.NextDouble() < 0.6)
                    Wear(bot, slot, UT_TemplateValuation.RandomPiece(random, slot, bot.Level, Types, negatives: true));
            return bot;
        }

        [Test]
        public void Delta_IsTheScoreOfTheCopiedLoadout_ForNewPiecesMovedPiecesAndEmptySlots()
        {
            var random = new Random(540);
            for (int trial = 0; trial < 3_000; trial++)
            {
                GearBot bot = RandomBot(random);
                BotGearSet.Context context = BotGearSet.For(bot);
                DbInventoryItem[] worn = BotGearSet.Worn(bot);
                eInventorySlot slot = Slots[random.Next(Slots.Length)];
                DbInventoryItem item = random.Next(4) switch
                {
                    0 => null,                                                         // taking the slot's piece off
                    1 => worn.FirstOrDefault(piece => piece != null),                  // moving a worn piece
                    _ => UT_TemplateValuation.RandomPiece(random, slot, bot.Level, Types, negatives: true),
                };
                long expected = BotGearSet.Score(context, BotGearSet.With(worn, slot, item)) - BotGearSet.Score(context, worn);
                Assert.That(BotGearSet.Delta(context, worn, slot, item), Is.EqualTo(expected), $"trial {trial}");
                Assert.That(BotGearSet.Delta(context, null, slot, item),
                    Is.EqualTo(BotGearSet.Score(context, BotGearSet.With(null, slot, item))), $"trial {trial}, nothing worn");
            }
        }

        [Test]
        public void TheScanScope_GivesTheSameGainsAsOneContextPerCandidate_AndClosesBehindItself()
        {
            var random = new Random(5401);
            for (int trial = 0; trial < 500; trial++)
            {
                GearBot bot = RandomBot(random);
                var candidates = Enumerable.Range(0, 12).Select(_ =>
                {
                    eInventorySlot slot = Slots[random.Next(Slots.Length)];
                    return (slot, UT_TemplateValuation.RandomPiece(random, slot, bot.Level, Types, negatives: true));
                }).ToArray();
                long[] outside = candidates.Select(c => AutonomousBotEconomy.GearImprovement(bot, c.Item2, c.slot)).ToArray();
                long[] inside;
                using (BotGearSet.BeginScan(bot))
                {
                    using (BotGearSet.BeginScan(bot)) { }   // a nested scope does not close the outer one
                    Assert.That(BotGearSet.ScanOpenFor(bot), Is.True);
                    inside = candidates.Select(c => AutonomousBotEconomy.GearImprovement(bot, c.Item2, c.slot)).ToArray();
                }
                Assert.That(inside, Is.EqualTo(outside), $"trial {trial}");
                Assert.That(BotGearSet.ScanOpenFor(bot), Is.False);
            }
        }

        [Test]
        public void AScanForOneBot_DoesNotLendItsLoadoutToAnother()
        {
            var random = new Random(5402);
            GearBot first = RandomBot(random), second = RandomBot(random);
            DbInventoryItem piece = UT_TemplateValuation.RandomPiece(random, eInventorySlot.Neck, second.Level, Types);
            long expected = AutonomousBotEconomy.GearImprovement(second, piece, eInventorySlot.Neck);
            using (BotGearSet.BeginScan(first))
            {
                AutonomousBotEconomy.GearImprovement(first, piece, eInventorySlot.Neck);
                Assert.That(AutonomousBotEconomy.GearImprovement(second, piece, eInventorySlot.Neck), Is.EqualTo(expected));
            }
        }

        [Test]
        public void ScoringAndDelta_AllocateNothingOnceTheWeightsAreKnown()
        {
            var random = new Random(5403);
            GearBot bot = RandomBot(random);
            BotGearSet.Context context = BotGearSet.For(bot);
            DbInventoryItem[] worn = BotGearSet.Worn(bot);
            DbInventoryItem piece = UT_TemplateValuation.RandomPiece(random, eInventorySlot.Cloak, bot.Level, Types);
            long warm = 0;
            for (eProperty type = (eProperty)1; (int)type < 256; type++)
                warm += context.Weight((int)type);
            warm += BotGearSet.Delta(context, worn, eInventorySlot.Cloak, piece);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            for (int i = 0; i < 1_000; i++)
            {
                sum += BotGearSet.Score(context, worn);
                sum += BotGearSet.Delta(context, worn, eInventorySlot.Cloak, piece);
                foreach ((int type, int value) in BotGearValue165.Bonuses(piece))
                    sum += type + value;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"bytes allocated by 1,000 scores (sum {sum + warm})");
        }

        [Test]
        public void TheBonusPairs_ReadTheItemsOwnElevenSlots()
        {
            DbInventoryItem item = Piece(eInventorySlot.Neck, 30, bonuses: [(eProperty.Strength, 7), (eProperty.MaxHealth, 20)]);
            SetBonus(item, 10, (int)eProperty.Piety, 3);
            item.ExtraBonusType = (int)eProperty.Resist_Body;
            item.ExtraBonus = 2;
            (int Type, int Value)[] expected =
            [
                (item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3),
                (item.Bonus4Type, item.Bonus4), (item.Bonus5Type, item.Bonus5), (item.Bonus6Type, item.Bonus6),
                (item.Bonus7Type, item.Bonus7), (item.Bonus8Type, item.Bonus8), (item.Bonus9Type, item.Bonus9),
                (item.Bonus10Type, item.Bonus10), (item.ExtraBonusType, item.ExtraBonus),
            ];
            Assert.That(BotGearValue165.Bonuses(item).ToArray(), Is.EqualTo(expected));
            var listed = new List<(int, int)>();
            foreach ((int type, int value) in BotGearValue165.Bonuses(item))
                listed.Add((type, value));
            Assert.That(listed, Is.EqualTo(expected));
            Assert.That(BotGearValue165.Bonuses(item.Template).ToArray()[0], Is.EqualTo((item.Template.Bonus1Type, item.Template.Bonus1)));
            Assert.That(BotGearValue165.Bonuses((DbInventoryItem)null).IsEmpty, Is.True);
            Assert.That(BotGearValue165.Bonuses((DbInventoryItem)null).Length, Is.Zero);
            BotGearValue165.ItemBonuses own = new (int Type, int Value)[] { (1, 2) };
            Assert.That(own.Length, Is.EqualTo(1));
            Assert.That(own[0], Is.EqualTo((1, 2)));
        }

        [Test]
        public void TheTrainedLineCheck_MatchesTheSpecListWhereTheTestWorldCanAnswer()
        {
            // SkillBase's spec-to-skill table needs the language tables, which the unit-test world lacks, so this pins the
            // cases that don't reach it: no lines at all, and a line below level 1, are not trained; a bot whose spec
            // table never loaded fails like GetSpecList does, so HasSkillLine still values the bonus as before.
            GearBot bot = Bot(40);
            FieldInfo field = typeof(GameBot).GetField("m_specialization", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(() => bot.GetSpecList(), Throws.InstanceOf<NullReferenceException>());
            Assert.That(() => bot.HasTrainedSpecRaising(eProperty.Skill_Slashing), Throws.InstanceOf<NullReferenceException>());
            Assert.That(BotGearValue165.Weight(bot, eProperty.Skill_Slashing), Is.EqualTo(6), "no spec table: valued as today");

            field.SetValue(bot, new Dictionary<string, Specialization>());
            Assert.That(bot.HasTrainedSpecRaising(eProperty.Skill_Slashing), Is.False);
            Assert.That(BotGearValue165.Weight(bot, eProperty.Skill_Slashing), Is.Zero);

            field.SetValue(bot, new Dictionary<string, Specialization> { ["x"] = new Specialization("x", "x", 0) { Level = 0 } });
            Assert.That(bot.HasTrainedSpecRaising(eProperty.Skill_Slashing), Is.False, "a line at 0 is not trained");
        }

        [Test]
        public void TheStatTable_HoldsNoLock_SoItCanNeverKeepAWindowsEvent()
        {
            FieldInfo[] fields = typeof(PropertyIndexer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(fields.Where(field => field.FieldType == typeof(Lock) || typeof(WaitHandle).IsAssignableFrom(field.FieldType)), Is.Empty);
        }

        [Test]
        public void TheStatTable_StaysConsistentUnderParallelWriters()
        {
            var table = new PropertyIndexer();
            const int Writers = 8, Keys = 40, Rounds = 2_000;
            Parallel.For(0, Writers, new ParallelOptions { MaxDegreeOfParallelism = Writers }, writer =>
            {
                for (int round = 1; round <= Rounds; round++)
                    for (int key = 0; key < Keys; key++)
                    {
                        var property = (eProperty)(1 + writer * Keys + key);
                        table[property] = round;
                        Assert.That(table[property], Is.EqualTo(round));
                    }
            });
            for (int writer = 0; writer < Writers; writer++)
                for (int key = 0; key < Keys; key++)
                    Assert.That(table[(eProperty)(1 + writer * Keys + key)], Is.EqualTo(Rounds));
            table[(eProperty)1] = 0;
            Assert.That(table[(eProperty)1], Is.Zero);
            table.Clear();
            Assert.That(table[(eProperty)2], Is.Zero);
        }

        [Test]
        public void TheHandleRow_NamesTheLargestTypesAndWhatGrew()
        {
            string line = ServerHandleCensus.Line(3_100,
                [("Event", 2_000, 350), ("File", 600, 0), ("Thread", 70, -2), ("Key", 430, 12)], 41.6);
            Assert.That(line, Is.EqualTo(
                "SERVER_HANDLE_CENSUS total=3100 byType=\"Event=2000,File=600,Key=430,Thread=70\" grew=\"Event+350,Key+12\" ms=42"));
        }

        [Test]
        public void TheBlockingCollectionSummary_IsNoneOrNamesCompactionAndLoad()
        {
            Assert.That(ServerMemoryDiagnostics.BlockingGen2Summary(),
                Does.Match(@"^(none|gc\d+/compacted=(True|False)/loadPct=-?\d+/ms=\d+)$"));
        }
    }
}
