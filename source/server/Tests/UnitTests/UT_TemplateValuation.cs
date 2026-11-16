using System;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using static DOL.UnitTests.GearTestBots;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP6: the template-aware valuation (BotGearSet) on True165's class weights and caps. Tests only:
    /// nothing here has been seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_TemplateValuation
    {
        private GameServer _previous;
        private bool _crafters;

        [SetUp]
        public void SetUp()
        {
            _previous = UseServer();
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            BotGearPipeline.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            GameServer.LoadTestDouble(_previous);
        }

        [Test]
        public void AtFifty_A99CraftedPieceWithTheRightLinesBeatsA96DropWithOddOnes()
        {
            GearBot hero = Bot(50);
            // A capped-ish set: other pieces already carry most of the Strength cap (75 at 50).
            Wear(hero, eInventorySlot.LegsArmor, Piece(eInventorySlot.LegsArmor, 50, 99, true, (eProperty.Strength, 40)));
            Wear(hero, eInventorySlot.ArmsArmor, Piece(eInventorySlot.ArmsArmor, 50, 99, true, (eProperty.Strength, 30)));
            // 96% drop: more raw bonus points, but on Strength over the cap and on Piety a Hero never uses.
            DbInventoryItem drop = Piece(eInventorySlot.TorsoArmor, 50, 96, false,
                (eProperty.Strength, 15), (eProperty.Piety, 15), (eProperty.Empathy, 10));
            // 99% crafted: fewer points, on the lines the set lacks.
            DbInventoryItem crafted = Piece(eInventorySlot.TorsoArmor, 50, 99, true,
                (eProperty.Constitution, 15), (eProperty.Resist_Slash, 8), (eProperty.Resist_Crush, 8));

            Assert.That(AutonomousBotEconomy.EquipmentValue(drop), Is.GreaterThan(AutonomousBotEconomy.EquipmentValue(crafted)),
                "today's flat value prefers the drop");
            long dropGain = AutonomousBotEconomy.GearImprovement(hero, drop, eInventorySlot.TorsoArmor);
            long craftedGain = AutonomousBotEconomy.GearImprovement(hero, crafted, eInventorySlot.TorsoArmor);
            Assert.That(craftedGain, Is.GreaterThan(dropGain));

            Wear(hero, eInventorySlot.TorsoArmor, drop);
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(hero, crafted, out eInventorySlot slot), Is.True);
            Assert.That(slot, Is.EqualTo(eInventorySlot.TorsoArmor));
        }

        [Test]
        public void TheSetIsCappedAsAWhole()
        {
            GearBot hero = Bot(50);
            Wear(hero, eInventorySlot.LegsArmor, Piece(eInventorySlot.LegsArmor, 50, 99, true, (eProperty.Strength, 75)));
            DbInventoryItem more = Piece(eInventorySlot.HeadArmor, 50, 99, true, (eProperty.Strength, 15));
            DbInventoryItem plain = Piece(eInventorySlot.HeadArmor, 50, 99, true);
            DbInventoryItem[] worn = BotGearSet.Worn(hero);
            BotGearSet.Context context = BotGearSet.For(hero);
            Assert.That(BotGearSet.Delta(context, worn, eInventorySlot.HeadArmor, more),
                Is.EqualTo(BotGearSet.Delta(context, worn, eInventorySlot.HeadArmor, plain)), "Strength over the set's cap adds nothing");

            DbInventoryItem resist = Piece(eInventorySlot.HeadArmor, 50, 99, true, (eProperty.Resist_Heat, 10));
            Assert.That(BotGearSet.Delta(context, worn, eInventorySlot.HeadArmor, resist) -
                        BotGearSet.Delta(context, worn, eInventorySlot.HeadArmor, plain), Is.EqualTo(10 * 3), "a resist at its class weight 3");
        }

        [Test]
        public void AtFiftyQualityCountsOnTheArmourSlotsOnly()
        {
            var context = new BotGearSet.Context(50, _ => 3);
            DbInventoryItem ring99 = Piece(eInventorySlot.LeftRing, 50, 99, true);
            DbInventoryItem ring94 = Piece(eInventorySlot.LeftRing, 50, 94, true);
            Assert.That(BotGearSet.PieceValue(context, ring99, eInventorySlot.LeftRing) -
                        BotGearSet.PieceValue(context, ring94, eInventorySlot.LeftRing), Is.EqualTo(5), "EquipmentValue's own 1 a point");
            DbInventoryItem torso99 = Piece(eInventorySlot.TorsoArmor, 50, 99, true);
            DbInventoryItem torso94 = Piece(eInventorySlot.TorsoArmor, 50, 94, true);
            Assert.That(BotGearSet.PieceValue(context, torso99, eInventorySlot.TorsoArmor) -
                        BotGearSet.PieceValue(context, torso94, eInventorySlot.TorsoArmor), Is.EqualTo(5 + 5 * BotGearSet.QualityPointWeight));
            var forty = new BotGearSet.Context(40, _ => 3);
            Assert.That(BotGearSet.PieceValue(forty, torso99, eInventorySlot.TorsoArmor) -
                        BotGearSet.PieceValue(forty, torso94, eInventorySlot.TorsoArmor), Is.EqualTo(5), "no template term below 50");
        }

        [Test]
        public void BelowFifty_TheSetDeltaIsTrue165sFitValueDifference()
        {
            var random = new Random(6106);
            eProperty[] types = [eProperty.Strength, eProperty.Constitution, eProperty.Dexterity, eProperty.Piety,
                eProperty.MaxHealth, eProperty.Resist_Body, eProperty.Resist_Slash, eProperty.ArmorFactor];
            for (int trial = 0; trial < 500; trial++)
            {
                GearBot hero = Bot(random.Next(1, 50));
                foreach (eInventorySlot slot in BotGearSet.TemplateSlots.Concat([eInventorySlot.Neck, eInventorySlot.Waist, eInventorySlot.Cloak]))
                    if (random.NextDouble() < 0.7)
                        Wear(hero, slot, RandomPiece(random, slot, hero.Level, types));
                eInventorySlot target = BotGearSet.TemplateSlots[random.Next(BotGearSet.TemplateSlots.Length)];
                DbInventoryItem candidate = RandomPiece(random, target, hero.Level, types);
                DbInventoryItem current = hero.Inventory.GetItem(target);
                long fit = AutonomousBotEconomy.FitValue(hero, candidate, target) - AutonomousBotEconomy.FitValue(hero, current, target);
                Assert.That(AutonomousBotEconomy.GearImprovement(hero, candidate, target), Is.EqualTo(fit), $"trial {trial}");
            }
        }

        [Test]
        public void WithBotCraftersOff_TodaysFlatValueDecides()
        {
            GearBot hero = Bot(50);
            Wear(hero, eInventorySlot.TorsoArmor, Piece(eInventorySlot.TorsoArmor, 50, 99, true, (eProperty.Strength, 20)));
            DbInventoryItem odd = Piece(eInventorySlot.TorsoArmor, 50, 96, false, (eProperty.Piety, 20), (eProperty.Empathy, 10));
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(hero, odd, out _), Is.False, "on: Piety is worth little to a Hero");
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            Assert.That(AutonomousBotEconomy.UsesGearValuation(hero), Is.False);
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(hero, odd, out _), Is.True, "off: more bonus points win, as today");
        }

        [Test]
        public void CompanionsKeepTodaysRules()
        {
            GearBot companion = Bot(50, helper: true);
            Assert.That(AutonomousBotEconomy.UsesGearValuation(companion), Is.False);
            GearBot gamebot = Bot(50);
            Assert.That(AutonomousBotEconomy.UsesGearValuation(gamebot), Is.True);
            gamebot.TestClass = null;
            Assert.That(AutonomousBotEconomy.UsesGearValuation(gamebot), Is.False, "no class: no weights");
        }

        [Test]
        public void TheCasterWeighsItsManaStat()
        {
            GearBot enchanter = Bot(30, new ClassEnchanter());
            Assert.That(BotGearValue165.Weight(enchanter, eProperty.Intelligence), Is.EqualTo(4));
            DbInventoryItem intel = Piece(eInventorySlot.Neck, 30, 96, false, (eProperty.Intelligence, 10));
            DbInventoryItem str = Piece(eInventorySlot.Neck, 30, 96, false, (eProperty.Strength, 10));
            Assert.That(AutonomousBotEconomy.GearImprovement(enchanter, intel, eInventorySlot.Neck),
                Is.GreaterThan(AutonomousBotEconomy.GearImprovement(enchanter, str, eInventorySlot.Neck)));
        }

        internal static DbInventoryItem RandomPiece(Random random, eInventorySlot slot, int botLevel, eProperty[] types, bool negatives = false)
        {
            int level = Math.Clamp(botLevel + random.Next(-12, 6), 1, 51);
            int quality = random.Next(85, 101);
            // Distinct types on one piece, as a crafted or generated item has them.
            var bonuses = types.OrderBy(_ => random.Next()).Take(random.Next(0, 5))
                .Select(type => (type, negatives && random.NextDouble() < 0.15 ? -random.Next(1, 6) : random.Next(1, 25)))
                .ToArray();
            return Piece(slot, level, quality, random.NextDouble() < 0.4, bonuses);
        }
    }
}
