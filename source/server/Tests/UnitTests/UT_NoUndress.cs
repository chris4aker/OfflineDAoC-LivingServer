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
    /// Crafting 2.0 WP6, the plan's risk 6: with the template-aware valuation on, a gamebot never equips anything that
    /// lowers its set score and never leaves a worn slot empty, so ECON_UNDRESS_GUARD's blocked count stays 0. The
    /// property test runs 10,000 random loadouts through the real equip paths (loot's TryEquipOwnedUpgrade and the
    /// merchant's TryEquipBestOwnedUpgrade). Tests only: the overnight check on the owner's PC is still to do.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_NoUndress
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
            eProperty.Piety, eProperty.Empathy, eProperty.Charisma, eProperty.MaxHealth, eProperty.MaxMana, eProperty.Acuity,
            eProperty.Resist_Body, eProperty.Resist_Cold, eProperty.Resist_Heat, eProperty.Resist_Slash, eProperty.Resist_Crush,
            eProperty.Resist_Thrust, eProperty.ArmorFactor, eProperty.PowerPool,
        ];

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
        public void TenThousandRandomLoadouts_NoEquipLowersTheSetOrEmptiesASlot()
        {
            var random = new Random(20261115);
            int equips = 0, refusedUpgrades = 0;
            for (int trial = 0; trial < 10_000; trial++)
            {
                GearBot bot = Bot(random.Next(1, 51), random.Next(2) == 0 ? new ClassHero() : new ClassEnchanter());
                foreach (eInventorySlot slot in Slots)
                    if (random.NextDouble() < 0.6)
                        Wear(bot, slot, UT_TemplateValuation.RandomPiece(random, slot, bot.Level, Types, negatives: true));
                int carried = random.Next(1, 7);
                for (int i = 0; i < carried; i++)
                    Carry(bot, UT_TemplateValuation.RandomPiece(random, Slots[random.Next(Slots.Length)], bot.Level, Types, negatives: true));

                BotGearSet.Context context = BotGearSet.For(bot);
                long score = BotGearSet.Score(context, BotGearSet.Worn(bot));
                int filled = BotGearSet.Filled(BotGearSet.Worn(bot));

                void AfterEquip(string how)
                {
                    DbInventoryItem[] worn = BotGearSet.Worn(bot);
                    long next = BotGearSet.Score(context, worn);
                    int nextFilled = BotGearSet.Filled(worn);
                    Assert.That(next, Is.GreaterThanOrEqualTo(score), $"trial {trial} ({how}): the set score fell");
                    Assert.That(nextFilled, Is.GreaterThanOrEqualTo(filled), $"trial {trial} ({how}): a worn slot was left empty");
                    score = next;
                    filled = nextFilled;
                    equips++;
                }

                // The merchant stop: the best owned upgrade, again and again, as long as there is one.
                for (int i = 0; i < 8 && AutonomousBotEconomy.TryEquipBestOwnedUpgrade(bot, out _); i++)
                    AfterEquip("best owned");
                // Loot: each carried piece as if just picked up.
                foreach (DbInventoryItem item in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToArray())
                {
                    if (AutonomousBotEconomy.TryEquipOwnedUpgrade(bot, item, "loot"))
                        AfterEquip("loot");
                    else if (BotGearSet.IsScoredSlot((eInventorySlot)item.Item_Type) && item.LevelRequirement <= bot.Level)
                        refusedUpgrades++;
                }
            }

            Assert.That(BotGearGuard.BlockedSinceStart, Is.Zero, "ECON_UNDRESS_GUARD blocked must be 0");
            Assert.That(BotGearGuard.Checked, Is.EqualTo(equips), "every equip went through the guard");
            Assert.That(equips, Is.GreaterThan(5_000), "the test equips real upgrades, not nothing");
            Assert.That(refusedUpgrades, Is.GreaterThan(1_000), "and refuses real downgrades");
        }

        [Test]
        public void TheGuardRefusesAMoveThatLowersTheScore_AndSaysSo()
        {
            GearBot hero = Bot(50);
            DbInventoryItem good = Piece(eInventorySlot.TorsoArmor, 50, 99, true, (eProperty.Strength, 20), (eProperty.Constitution, 20));
            Wear(hero, eInventorySlot.TorsoArmor, good);
            DbInventoryItem bad = Piece(eInventorySlot.TorsoArmor, 10, 90);
            Carry(hero, bad);
            Assert.That(BotGearGuard.Allows(hero, bad, eInventorySlot.TorsoArmor, "test"), Is.False);
            Assert.That(BotGearGuard.Blocked, Is.EqualTo(1));
            Assert.That(BotGearPipeline.GuardLine(BotGearGuard.Checked, BotGearGuard.Blocked),
                Does.StartWith("ECON_UNDRESS_GUARD event=\"hour\" checked=1 blocked=1"));
            Assert.That(AutonomousBotEconomy.TryEquipOwnedUpgrade(hero, bad), Is.False);
            Assert.That(hero.Inventory.GetItem(eInventorySlot.TorsoArmor), Is.SameAs(good));
        }

        [Test]
        public void MovingAWornPieceToAnotherSlotLeavesItsSlotEmpty_AndIsRefused()
        {
            var context = new BotGearSet.Context(50, _ => 3);
            DbInventoryItem ring = Piece(eInventorySlot.LeftRing, 50, 99, true, (eProperty.Strength, 5));
            var before = new DbInventoryItem[BotGearSet.LoadoutLength];
            before[(int)eInventorySlot.LeftRing] = ring;
            DbInventoryItem[] after = BotGearSet.With(before, eInventorySlot.RightRing, ring);
            Assert.That(after[(int)eInventorySlot.LeftRing], Is.Null);
            Assert.That(BotGearGuard.Allows(context, before, after, out long b, out long a), Is.False);
            Assert.That(a, Is.EqualTo(b), "the same score, but a slot left empty");
        }

        [Test]
        public void WithBotCraftersOff_TheGuardChecksNothing()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            GearBot hero = Bot(50);
            Wear(hero, eInventorySlot.TorsoArmor, Piece(eInventorySlot.TorsoArmor, 50, 99, true, (eProperty.Strength, 20)));
            Assert.That(BotGearGuard.Allows(hero, Piece(eInventorySlot.TorsoArmor, 1, 85), eInventorySlot.TorsoArmor, "test"), Is.True);
            Assert.That(BotGearGuard.Checked, Is.Zero);
        }
    }
}
