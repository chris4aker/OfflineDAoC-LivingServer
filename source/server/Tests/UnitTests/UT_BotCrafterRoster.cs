using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP4: who is a crafter. The legacy 12% types stay for every existing bot; bots created after the stored
    /// types began get the 7% table; a stored type never changes (not with a new share, not after a restart); beggars and
    /// non-Crafters never craft; with bot_crafters off nothing is stored and every type is the legacy one. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotCrafterRoster
    {
        private bool _crafters, _beggars;
        private int _share;

        [SetUp]
        public void SetUp()
        {
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _share = CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT;
            _beggars = Properties.BOT_LIVING_BEGGARS;
            BotCrafterTypes.ResetForTests();
            BotCrafterRoster.Instance.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT = _share;
            Properties.BOT_LIVING_BEGGARS = _beggars;
            BotCrafterTypes.ResetForTests();
            BotCrafterRoster.Instance.ResetForTests();
        }

        private static double CrafterShare(IEnumerable<long> ids) =>
            ids.Average(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Crafter ? 100.0 : 0.0);

        [Test]
        public void TheLegacyTableStaysAtTwelvePercent()
        {
            Assert.That(AutonomousBotPersonality.TypeSharePercent[(int)BotPersonalityType.Crafter], Is.EqualTo(12));
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            Assert.That(CrafterShare(Enumerable.Range(1, 50_000).Select(i => (long)i)), Is.EqualTo(12).Within(0.6),
                "before the stored types begin, every bot keeps its 12%-era type");
            Assert.That(Enumerable.Range(1, 5_000).All(id => AutonomousBotPersonality.TypeFor(id) == AutonomousBotPersonality.LegacyTypeFor(id)), Is.True);
            Assert.That(BotCrafterTypes.Count, Is.Zero, "nothing stored before the types began");
        }

        [Test]
        public void TheNewBotTableHasTheShareAndSumsToOneHundred()
        {
            foreach (int share in new[] { 0, 7, 12, 20, 100 })
            {
                int[] shares = BotCraftingRules.NewBotShares(AutonomousBotPersonality.TypeSharePercent, share);
                Assert.That(shares.Sum(), Is.EqualTo(100), $"share {share}");
                Assert.That(shares[(int)BotPersonalityType.Crafter], Is.EqualTo(share));
            }
            Assert.That(BotCraftingRules.NewBotShares(AutonomousBotPersonality.TypeSharePercent, 12), Is.EqualTo(AutonomousBotPersonality.TypeSharePercent),
                "at 12 the new table is the legacy one");
        }

        [Test]
        public void ExistingBotsKeepTheirType_NewBotsGetSevenPercent()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT = 7;
            long[] existing = Enumerable.Range(1, 20_000).Select(i => (long)i).ToArray();
            Dictionary<long, BotPersonalityType> before = existing.ToDictionary(id => id, AutonomousBotPersonality.TypeFor);

            Assert.That(BotCrafterTypes.Begin(existing), Is.EqualTo(existing.Length));
            Assert.That(BotCrafterTypes.Began, Is.True);
            Assert.That(existing.All(id => AutonomousBotPersonality.TypeFor(id) == before[id]), Is.True, "no existing bot is re-typed");
            Assert.That(CrafterShare(existing), Is.EqualTo(12).Within(0.7));

            long[] newcomers = Enumerable.Range(1_000_000, 50_000).Select(i => (long)i).ToArray();
            Assert.That(CrafterShare(newcomers), Is.EqualTo(7).Within(0.5), "a bot created after the types began: 7%");
            Assert.That(BotCrafterTypes.Count, Is.EqualTo(existing.Length + newcomers.Length), "each newcomer is stored when first seen");
            Assert.That(BotCrafterTypes.Begin(existing), Is.Zero, "the types begin once");
        }

        [Test]
        public void AStoredTypeNeverChanges_NotWithANewShare_NorAfterARestart()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT = 7;
            BotCrafterTypes.Begin(Enumerable.Range(1, 1_000).Select(i => (long)i));
            long[] newcomers = Enumerable.Range(2_000_000, 2_000).Select(i => (long)i).ToArray();
            Dictionary<long, BotPersonalityType> typed = newcomers.Concat(Enumerable.Range(1, 1_000).Select(i => (long)i))
                .ToDictionary(id => id, AutonomousBotPersonality.TypeFor);

            CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT = 40;
            Assert.That(typed.All(pair => AutonomousBotPersonality.TypeFor(pair.Key) == pair.Value), Is.True, "a new share re-types nobody");

            // A restart: the rows come back from the table (their saved copies), the marker row with them.
            var rows = typed.Select(pair => new OfflineBotTypeRecord { BotId = pair.Key, PersonalityType = (int)pair.Value })
                .Append(new OfflineBotTypeRecord { BotId = BotCrafterTypes.MarkerId, PersonalityType = -1 }).ToArray();
            BotCrafterTypes.ResetForTests();
            BotCrafterTypes.Load(rows);
            Assert.That(BotCrafterTypes.Began, Is.True);
            Assert.That(typed.All(pair => AutonomousBotPersonality.TypeFor(pair.Key) == pair.Value), Is.True);

            CraftingEconomyProperties.BOT_CRAFTERS = false;
            Assert.That(typed.All(pair => AutonomousBotPersonality.TypeFor(pair.Key) == pair.Value), Is.True,
                "switched off, a stored type still holds");
        }

        [Test]
        public void SwitchedOff_NothingIsStored_AndANewBotHasItsLegacyType()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            BotCrafterTypes.Load([new OfflineBotTypeRecord { BotId = BotCrafterTypes.MarkerId, PersonalityType = -1 }]);
            for (long id = 3_000_000; id < 3_002_000; id++)
                Assert.That(AutonomousBotPersonality.TypeFor(id), Is.EqualTo(AutonomousBotPersonality.LegacyTypeFor(id)));
            Assert.That(BotCrafterTypes.Count, Is.Zero);
            Assert.That(BotCrafters.Roster.IsCrafter(1), Is.False, "nothing registered and off: nobody is a crafter");
            Assert.That(BotCrafterRoster.Instance.IsCrafter(Enumerable.Range(1, 1000).First(id => AutonomousBotPersonality.LegacyTypeFor(id) == BotPersonalityType.Crafter)),
                Is.False, "off: not even a Crafter");
        }

        [Test]
        public void BeggarsAndOtherTypesAreNeverCrafters()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            long beggar = Enumerable.Range(1, 200_000).First(id => AutonomousBeggars.IsBeggarByRoll(id));
            long crafter = Enumerable.Range(1, 200_000).First(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Crafter &&
                                                                    !AutonomousBeggars.IsBeggarByRoll(id));
            long leveler = Enumerable.Range(1, 200_000).First(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Leveler);
            Properties.BOT_LIVING_BEGGARS = true;
            Assert.That(BotCrafterRoster.Instance.IsCrafter(beggar), Is.False, "a beggar lives on its corner");
            Assert.That(BotCrafterRoster.Instance.IsCrafter(crafter), Is.True);
            Assert.That(BotCrafterRoster.Instance.IsCrafter(leveler), Is.False, "only the Crafter personality crafts");
        }

        [Test]
        public void TheRosterCountsEachRealmsOrders()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            // The rows table is internal to the server: reached by reflection so the test loads rows without a database.
            var table = (LivingServerTable<long, OfflineBotCrafterRecord>)typeof(BotCrafterRoster)
                .GetField("Rows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .GetValue(BotCrafterRoster.Instance);
            table.Load([
                new OfflineBotCrafterRecord { BotId = 1, Realm = (int)eRealm.Albion, PrimaryCraft = (int)eCraftingSkill.ArmorCrafting },
                new OfflineBotCrafterRecord { BotId = 2, Realm = (int)eRealm.Albion, PrimaryCraft = (int)eCraftingSkill.ArmorCrafting },
                new OfflineBotCrafterRecord { BotId = 3, Realm = (int)eRealm.Albion, PrimaryCraft = (int)eCraftingSkill.Tailoring },
                new OfflineBotCrafterRecord { BotId = 4, Realm = (int)eRealm.Midgard, PrimaryCraft = (int)eCraftingSkill.Alchemy },
            ]);
            IReadOnlyDictionary<eCraftingSkill, int> albion = BotCrafterRoster.Instance.OrderCounts(eRealm.Albion);
            Assert.That(albion[eCraftingSkill.ArmorCrafting], Is.EqualTo(2));
            Assert.That(albion[eCraftingSkill.Tailoring], Is.EqualTo(1));
            Assert.That(albion.ContainsKey(eCraftingSkill.Alchemy), Is.False);
            Assert.That(BotCrafterRoster.Instance.OrderCounts(eRealm.Midgard)[eCraftingSkill.Alchemy], Is.EqualTo(1));
            BotCrafterRoster.Instance.NoteMade(1, masterpiece: true);
            BotCrafterRoster.Instance.NoteMade(99, masterpiece: false);
            Assert.That(BotCrafterRoster.Instance.TryGetRow(1, out var row) && row.CraftsMade == 1 && row.Masterpieces == 1, Is.True);
            Assert.That(BotCrafterRoster.Instance.TryGetRow(99, out _), Is.False, "counters never make a row for a bot that never enrolled");
        }
    }
}
