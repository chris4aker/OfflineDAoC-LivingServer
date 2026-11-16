using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.PlayerClass;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP7 (sinks). shadowofze's True165 tests first, ported unchanged but for the class names
    /// (UT_BotPotions165 and UT_RepairErrand165 at 2e4212e, GPL-3.0), then ours: the switches, wear for autonomous gamebots
    /// only by the player's formula, the repair rules and prices, paid poisons, the luxury budget, the governor, the siege
    /// supply and the registration. Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotSinks
    {
        internal sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override ICharacterClass CharacterClass => new ClassEnchanter();
            public override void RefreshItemBonuses() { }
        }

        private sealed class TestServer : GameServer
        {
            protected override DOL.GS.ServerRules.IServerRules ServerRulesImpl => new DOL.GS.ServerRules.NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        /// <summary>A database that answers every read with nothing (GameObject's static set-up reads the quest cache).</summary>
        public class EmptyRows : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type result = method.ReturnType;
                if (result == typeof(bool))
                    return true;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
            }
        }

        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, EmptyRows>();
        private GameServer _previousServer;
        private bool _crafters, _upkeep, _luxury, _siege;
        private string _playerUpkeep;
        private readonly Dictionary<long, long> _purses = new();
        private readonly List<(long Bot, long Signed, EconomyFlowKind Kind)> _moves = new();
        private static long _nextId = 9_870_000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _upkeep = CraftingEconomyProperties.BOT_UPKEEP;
            _luxury = CraftingEconomyProperties.BOT_LUXURY_SPENDING;
            _siege = CraftingEconomyProperties.BOT_SIEGE_SUPPLY;
            _playerUpkeep = CraftingEconomyProperties.PLAYER_UPKEEP;
            _purses.Clear();
            _moves.Clear();
            SinkSeams.ResetForTests();
            BotSinks.CoinOf = id => _purses.GetValueOrDefault(id);
            BotSinks.Spend = (id, copper, kind) =>
            {
                if (copper < 0 || _purses.GetValueOrDefault(id) < copper)
                    return false;
                _purses[id] = _purses.GetValueOrDefault(id) - copper;
                _moves.Add((id, -copper, kind));
                return true;
            };
            BotSinks.Pay = (id, copper, kind) =>
            {
                _purses[id] = _purses.GetValueOrDefault(id) + copper;
                _moves.Add((id, copper, kind));
                return true;
            };
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_UPKEEP = _upkeep;
            CraftingEconomyProperties.BOT_LUXURY_SPENDING = _luxury;
            CraftingEconomyProperties.BOT_SIEGE_SUPPLY = _siege;
            CraftingEconomyProperties.PLAYER_UPKEEP = _playerUpkeep;
            BotSinks.ResetCoinForTests();
            SinkSeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomySeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            GameServer.LoadTestDouble(_previousServer);
        }

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static TestBot Bot(int level = 50, bool helper = false, bool autonomous = true)
        {
            TestBot bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = (byte)level;
            bot.Realm = eRealm.Albion;
            bot.Name = "Sink test bot";
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, autonomous);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper)).SetValue(bot, helper);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, ObjectiveAssignmentId = "between-pve-services-I-1" });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            return bot;
        }

        private static DbInventoryItem Armor(int level = 50, int maxCondition = 50_000, int conditionPercent = 100, long price = 100_000,
            eInventorySlot slot = eInventorySlot.TorsoArmor)
        {
            var template = new DbItemTemplate
            {
                Id_nb = "sink_test_" + Guid.NewGuid().ToString("N"), Name = "chain hauberk", Level = level,
                Object_Type = (int)eObjectType.Chain, Item_Type = (int)slot, Quality = 95, MaxCount = 1, PackSize = 1,
                MaxCondition = maxCondition, Condition = maxCondition, MaxDurability = maxCondition, Durability = maxCondition,
                DPS_AF = 100, Price = price,
            };
            DbInventoryItem item = GameInventoryItem.Create(template);
            item.Condition = (int)Math.Ceiling(maxCondition * conditionPercent / 100.0);
            item.SlotPosition = (int)slot;
            return item;
        }

        private static void Upkeep(bool on = true, bool crafters = true)
        {
            CraftingEconomyProperties.BOT_CRAFTERS = crafters;
            CraftingEconomyProperties.BOT_UPKEEP = on;
        }

        #region True165's UT_BotPotions165 (2e4212e), on BotPotions

        [Test]
        public void ABagWithNoDraughtIsNotLookedAtOnEveryHit() => Assert.Multiple(() =>
        {
            // found no draught at 1,000: marked until 31,000 for inventory revision 4
            long until = 1_000 + BotPotions.NoPotionRecheckMilliseconds;
            Assert.That(BotPotions.ShouldLook(1_200, until, 4, 4), Is.False, "the next hit does not scan the bag again");
            Assert.That(BotPotions.ShouldLook(until, until, 4, 4), Is.True, "looks again after the recheck time");
            Assert.That(BotPotions.ShouldLook(1_200, until, 4, 5), Is.True, "the inventory changed (loot, a buy): looks at once");
            Assert.That(BotPotions.ShouldLook(0, 0, 0, 0), Is.True, "never marked");
        });

        [Test]
        public void TheSpellTypeEnumMapsLikeItsName() => Assert.Multiple(() =>
        {
            foreach (eSpellType type in new[] { eSpellType.Heal, eSpellType.PowerHeal, eSpellType.EnduranceHeal, eSpellType.HealthRegenBuff })
                Assert.That(BotPotions.KindOf(type), Is.EqualTo(BotPotions.KindOf(type.ToString())), type.ToString());
        });

        [Test]
        public void OnlyHealPowerAndEnduranceDraughtsAreDrunk()
        {
            Assert.That(BotPotions.KindOf("Heal"), Is.EqualTo(BotPotions.Kind.Heal));
            Assert.That(BotPotions.KindOf("PowerHeal"), Is.EqualTo(BotPotions.Kind.Power));
            Assert.That(BotPotions.KindOf("EnduranceHeal"), Is.EqualTo(BotPotions.Kind.Endurance));
            Assert.That(BotPotions.KindOf("HealthRegenBuff"), Is.EqualTo(BotPotions.Kind.None));
        }

        [Test]
        public void APotionIsABackpackMagicalItemWithASpellAndCharges()
        {
            var draught = new DbInventoryItem { Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.FirstBackpack, SpellID = 8053, Charges = 9 };
            Assert.That(BotPotions.IsPotion(draught), Is.True);
            draught.Charges = 0;
            Assert.That(BotPotions.IsPotion(draught), Is.False, "used up");
            var charm = new DbInventoryItem { Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.Neck, SpellID = 8053, Charges = 9 };
            Assert.That(BotPotions.IsPotion(charm), Is.False, "a charged necklace is worn, not drunk");
        }

        [Test]
        public void AHitBotDrinksAHealDraughtAtThirtyFivePercentOnlyWhenNotCastingOrSwinging()
        {
            Assert.That(BotPotions.WantsHealWhenHit(35, false, false), Is.True);
            Assert.That(BotPotions.WantsHealWhenHit(36, false, false), Is.False);
            Assert.That(BotPotions.WantsHealWhenHit(20, true, false), Is.False, "casting");
            Assert.That(BotPotions.WantsHealWhenHit(20, false, true), Is.False, "a cast potion can't be drunk in attack mode");
            Assert.That(BotPotions.WantsHealWhenHit(0, false, false), Is.False, "dead");
        }

        [Test]
        public void AfterAKillWithAnAddLeftTheBotDrinksWhatItIsShortOf()
        {
            Assert.That(BotPotions.WantsAfterKill(40, 90, true, 90, addsLeft: false), Is.EqualTo(BotPotions.Kind.None), "no add: it rests");
            Assert.That(BotPotions.WantsAfterKill(40, 10, true, 90, true), Is.EqualTo(BotPotions.Kind.Heal), "health first");
            Assert.That(BotPotions.WantsAfterKill(80, 15, true, 90, true), Is.EqualTo(BotPotions.Kind.Power));
            Assert.That(BotPotions.WantsAfterKill(80, 0, false, 15, true), Is.EqualTo(BotPotions.Kind.Endurance));
            Assert.That(BotPotions.WantsAfterKill(80, 50, true, 10, true), Is.EqualTo(BotPotions.Kind.None), "a caster drinks no endurance");
        }

        [Test]
        public void OnlyARichGamebotOfLevelTwentyKeepsHealDraughts()
        {
            const long price = 8_000, reserve = 1_600;   // a level-20 bot
            Assert.That(BotPotions.HealDraughtsWanted(19, 1_000_000, reserve, price), Is.Zero);
            Assert.That(BotPotions.HealDraughtsWanted(20, price + 5 * reserve - 1, reserve, price), Is.Zero);
            Assert.That(BotPotions.HealDraughtsWanted(20, price + 5 * reserve, reserve, price), Is.EqualTo(1));
            Assert.That(BotPotions.HealDraughtsWanted(20, 2 * price + 20 * reserve, reserve, price), Is.EqualTo(2));
        }

        #endregion

        #region True165's UT_RepairErrand165 (36358de), on BotGearWear

        [Test]
        public void RepairErrandFollowsThePeriodRepairPoint()
        {
            Assert.That(BotGearWear.RepairErrandBelowPercent, Is.InRange(85, 90));
        }

        #endregion

        [Test]
        public void TheReserveIsTrue165sShoppingReserve() => Assert.Multiple(() =>
        {
            Assert.That(BotPotions.Reserve(20), Is.EqualTo(1_600), "the level-20 reserve of True165's test");
            Assert.That(BotPotions.Reserve(1), Is.EqualTo(50));
            Assert.That(BotPotions.Reserve(50), Is.EqualTo(10_000));
        });

        [Test]
        public void EverySwitchIsOnByDefaultAndNeedsCrafters()
        {
            // Owner 2026-11-16: every Living Server feature on by default (WP7 built them off).
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.BOT_UPKEEP))
                .GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>().DefaultValue, Is.EqualTo(true));
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.BOT_LUXURY_SPENDING))
                .GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>().DefaultValue, Is.EqualTo(true));
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.BOT_SIEGE_SUPPLY))
                .GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>().DefaultValue, Is.EqualTo(true));
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            CraftingEconomyProperties.BOT_UPKEEP = CraftingEconomyProperties.BOT_LUXURY_SPENDING = CraftingEconomyProperties.BOT_SIEGE_SUPPLY = true;
            Assert.That(BotSinks.UpkeepOn || BotSinks.LuxuryOn || BotSinks.SiegeSupplyOn, Is.False, "each needs bot_crafters");
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            Assert.That(BotSinks.UpkeepOn && BotSinks.LuxuryOn && BotSinks.SiegeSupplyOn, Is.True);
        }

        #region Wear

        [Test]
        public void TheWearAmountIsThePlayersFormula()
        {
            for (int wearer = 1; wearer <= 50; wearer += 7)
                for (int itemLevel = 1; itemLevel <= 51; itemLevel += 5)
                    foreach (int percent in new[] { 100, 91, 90, 75 })
                    {
                        int expected = ConLevels.GetConLevel(wearer, itemLevel) + 4; // what GameObject.GetConLevel returns
                        if (percent < 91)
                            expected *= 2;
                        Assert.That(GameInventoryItem.ConditionLoss(wearer, itemLevel, percent), Is.EqualTo(expected), $"{wearer}/{itemLevel}/{percent}");
                    }
        }

        [Test]
        public void OnlyAnAutonomousGamebotsGearWearsAndOnlyUnderUpkeep()
        {
            BotGearWear.WearRoll = () => true;
            TestBot gamebot = Bot();
            DbInventoryItem item = Armor();
            int full = item.Condition;

            Upkeep(on: false);
            GameInventoryItem.OnBotItemUsed(gamebot, item);
            Assert.That(item.Condition, Is.EqualTo(full), "bot_upkeep off: nothing wears (today's behaviour)");

            Upkeep(on: true, crafters: false);
            GameInventoryItem.OnBotItemUsed(gamebot, item);
            Assert.That(item.Condition, Is.EqualTo(full), "bot_upkeep needs bot_crafters");

            Upkeep();
            GameInventoryItem.OnBotItemUsed(gamebot, item);
            Assert.That(item.Condition, Is.EqualTo(full - GameInventoryItem.ConditionLoss(50, 50, 100)), "a gamebot's gear wears by the player rule");

            TestBot companion = Bot(helper: true);
            DbInventoryItem companionItem = Armor();
            GameInventoryItem.OnBotItemUsed(companion, companionItem);
            Assert.That(companionItem.Condition, Is.EqualTo(companionItem.MaxCondition), "a companion's gear never wears here");

            TestBot notAutonomous = Bot(autonomous: false);
            DbInventoryItem other = Armor();
            GameInventoryItem.OnBotItemUsed(notAutonomous, other);
            Assert.That(other.Condition, Is.EqualTo(other.MaxCondition), "a /bot companion (not autonomous) never wears here");
        }

        [Test]
        public void WearStopsAtSeventyPercentAndNeedsTheRoll()
        {
            Upkeep();
            TestBot bot = Bot();
            DbInventoryItem worn = Armor(conditionPercent: 70);
            int at70 = worn.Condition;
            BotGearWear.WearRoll = () => true;
            GameInventoryItem.OnBotItemUsed(bot, worn);
            Assert.That(worn.Condition, Is.EqualTo(at70), "never below 70%");
            DbInventoryItem fresh = Armor();
            BotGearWear.WearRoll = () => false;
            GameInventoryItem.OnBotItemUsed(bot, fresh);
            Assert.That(fresh.Condition, Is.EqualTo(fresh.MaxCondition), "no wear without the 5% roll");
            Assert.That(BotGearWear.CanWear(0, 100), Is.False, "an item without condition never wears");
        }

        #endregion

        #region Repairs

        [Test]
        public void TheErrandWantsTownBelowEightyEightPercentWithTheCoin()
        {
            DbInventoryItem worn = Armor(conditionPercent: 87);
            long price = BotGearWear.PriceOf(worn, UpkeepPolicy.Mode.Light);
            Assert.That(price, Is.GreaterThan(0));
            Assert.That(BotGearWear.WantsErrand([worn], price, UpkeepPolicy.Mode.Light), Is.True);
            Assert.That(BotGearWear.WantsErrand([worn], price - 1, UpkeepPolicy.Mode.Light), Is.False, "it can't pay");
            Assert.That(BotGearWear.WantsErrand([Armor(conditionPercent: 89)], long.MaxValue, UpkeepPolicy.Mode.Light), Is.False, "88% or better waits");
            DbInventoryItem bagged = Armor(conditionPercent: 50);
            bagged.SlotPosition = (int)eInventorySlot.FirstBackpack;
            Assert.That(BotGearWear.WantsErrand([bagged], long.MaxValue, UpkeepPolicy.Mode.Light), Is.False, "only what it wears");
        }

        [Test]
        public void InTownEverythingBelowNinetyIsRepairedWorstFirst()
        {
            DbInventoryItem a = Armor(conditionPercent: 89, slot: eInventorySlot.HeadArmor);
            DbInventoryItem b = Armor(conditionPercent: 80, slot: eInventorySlot.TorsoArmor);
            DbInventoryItem c = Armor(conditionPercent: 95, slot: eInventorySlot.LegsArmor);
            Assert.That(BotGearWear.WornPieces([a, b, c], BotGearWear.RepairBelowPercent), Is.EqualTo(new[] { b, a }));
        }

        [Test]
        public void ABotPaysWhatAPlayerPaysUnderTheUpkeepMode()
        {
            DbInventoryItem worn = Armor(conditionPercent: 80, price: 100_000);
            long classic = worn.RepairCost;
            Assert.That(classic, Is.EqualTo(20_000));
            Assert.That(BotGearWear.PriceOf(worn, UpkeepPolicy.Mode.Classic), Is.EqualTo(classic));
            Assert.That(BotGearWear.PriceOf(worn, UpkeepPolicy.Mode.Light), Is.EqualTo(10_000), "light: half the smith's price");
            Assert.That(BotGearWear.PriceOf(worn, UpkeepPolicy.Mode.Free), Is.Zero);
        }

        [Test]
        public void ACraftersRepairSkillComesFromItsSavedSkills() => Assert.Multiple(() =>
        {
            string armorcrafter = BotCraftingSkills.Serialize([new(eCraftingSkill.ArmorCrafting, 500), new(eCraftingSkill.MetalWorking, 1)]);
            Assert.That(BotGearWear.RepairSkill(armorcrafter, eCraftingSkill.MetalWorking), Is.EqualTo(500), "the secondary grows with the craft");
            Assert.That(BotGearWear.RepairSkill(armorcrafter, eCraftingSkill.WoodWorking), Is.Zero, "armorcraft works no wood");
            string tailor = BotCraftingSkills.Serialize([new(eCraftingSkill.Tailoring, 400), new(eCraftingSkill.MetalWorking, 120)]);
            Assert.That(BotGearWear.RepairSkill(tailor, eCraftingSkill.MetalWorking), Is.EqualTo(120), "its own secondary value");
            Assert.That(BotGearWear.RepairSkill("", eCraftingSkill.MetalWorking), Is.Zero, "no crafting, no own repair");
        });

        [Test]
        public void TheRepairRollIsThePlayers()
        {
            DbInventoryItem chest = Armor(level: 10);
            int craftLevel = CraftingMgr.GetItemCraftLevel(chest);
            Assert.That(BotGearWear.RepairChance(0, chest), Is.Zero);
            Assert.That(BotGearWear.RepairChance(craftLevel, chest), Is.EqualTo(Math.Clamp((int)(90 / (craftLevel * 0.5) * craftLevel) - 80, 0, 100)));
            Assert.That(BotGearWear.RepairChance(10_000, chest), Is.EqualTo(100));
        }

        [Test]
        public void TheErrandAndTheStepDoNothingWithUpkeepOff()
        {
            Upkeep(on: false);
            TestBot bot = Bot();
            Assert.That(BotGearWear.Errand.Instance.WantsVisit(bot), Is.False);
            Assert.That(BotGearWear.Errand.Instance.NeededService(bot), Is.Null);
            Assert.That(BotGearWear.Step.Instance.TryRun(bot, null, 0, out _), Is.False);
            Assert.That(BotPotions.Step.Instance.TryRun(bot, null, 0, out _), Is.False);
            Assert.That(BotPoisonSupply.Step.Instance.TryRun(bot, null, 0, out _), Is.False);
            Assert.That(BotLuxury.Step.Instance.TryRun(bot, null, 0, out _), Is.False);
            Assert.That(SinkSeams.ItemPolicy.Instance.IsReserved(bot, new DbInventoryItem { Object_Type = (int)eObjectType.Poison, PoisonSpellID = 1, Count = 5 }), Is.False);
            Assert.That(_moves, Is.Empty);
        }

        #endregion

        #region Poisons

        [Test]
        public void VialsCostTheMerchantsPackPricePerVial() => Assert.Multiple(() =>
        {
            Assert.That(BotPoisonSupply.VialsPrice(8_643, 20, 1), Is.EqualTo(433), "lifebane: 8,643 a pack of 20, rounded up");
            Assert.That(BotPoisonSupply.VialsPrice(8_643, 20, 20), Is.EqualTo(8_643), "a whole pack is the pack price");
            Assert.That(BotPoisonSupply.VialsPrice(11, 20, 3), Is.EqualTo(3), "at least a copper a vial");
            Assert.That(BotPoisonSupply.VialsPrice(0, 20, 3), Is.Zero);
        });

        [Test]
        public void AnAssassinRestocksBelowTwentyUpToSixtyAsFarAsItsCoinGoes() => Assert.Multiple(() =>
        {
            Assert.That(BotPoisonSupply.RestockCount(20, 1_000_000, 433), Is.Zero, "enough on it");
            Assert.That(BotPoisonSupply.RestockCount(19, 1_000_000, 433), Is.EqualTo(41));
            Assert.That(BotPoisonSupply.RestockCount(0, 433 * 10, 433), Is.EqualTo(10), "what it can pay for");
            Assert.That(BotPoisonSupply.RestockCount(0, 0, 433), Is.Zero);
        });

        internal sealed class PaidAssassin : GameBot
        {
            private PaidAssassin() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override bool InCombat => false;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsCrowdControlled => false;
            public override byte Level { get; set; }
            public override eRealm Realm { get; set; }
            public override ICharacterClass CharacterClass => new ClassShadowblade();
            public override int GetModifiedSpecLevel(string name) => 20;
            public override bool HasAbilityToUseItem(DbItemTemplate item) => item.Object_Type == (int)eObjectType.Sword;
            public override void RefreshItemBonuses() { }
        }

        [Test]
        public void UnderUpkeepAnAssassinsCoatComesOutOfARealVial()
        {
            var spell = new Spell(new DbSpell { SpellID = 30019, Name = "poison", Type = "DamageOverTime", Target = "Enemy" }, 20);
            var vialTemplate = new DbItemTemplate { Id_nb = "greater_lethal_poison", Name = "greater lethal poison", Level = 20, PoisonCharges = 1,
                PoisonMaxCharges = 1, PoisonSpellID = 30019, Object_Type = (int)eObjectType.Poison, MaxCount = 100, PackSize = 20, Price = 770 };
            BotPoisonSupply.Load(new[] { vialTemplate }, new[] { spell });
            try
            {
                var bot = (PaidAssassin)RuntimeHelpers.GetUninitializedObject(typeof(PaidAssassin));
                Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
                bot.Level = 50;
                bot.Realm = eRealm.Midgard;
                bot.DatabaseID = Interlocked.Increment(ref _nextId);
                typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
                typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { BotId = bot.DatabaseID });
                bot.Inventory = new BotInventory();
                var blade = GameInventoryItem.Create(new DbItemTemplate { Name = "earned sword", Object_Type = (int)eObjectType.Sword,
                    Item_Type = Slot.RIGHTHAND, DPS_AF = 150, SPD_ABS = 35, Quality = 99, MaxCondition = 50000, Condition = 50000 });
                bot.Inventory.AddItem(eInventorySlot.RightHandWeapon, blade);

                Upkeep(on: false);
                Assert.That(BotPoisonSupply.Maintain(bot), Is.True, "off: the free coat as today");
                blade.PoisonCharges = 0; blade.PoisonSpellID = 0;

                Upkeep();
                Assert.That(BotPoisonSupply.Maintain(bot), Is.False, "upkeep, no vial: no coat");
                Assert.That(blade.PoisonCharges, Is.Zero);
                DbInventoryItem vials = GameInventoryItem.Create(vialTemplate);
                vials.Count = 2;
                bot.Inventory.AddItem(eInventorySlot.FirstBackpack, vials);
                Assert.That(SinkSeams.ItemPolicy.Instance.KeepFromVendor(bot, vials), Is.True, "its vials are never sold");
                Assert.That(BotPoisonSupply.Maintain(bot), Is.True, "a vial: a coat");
                Assert.That(blade.PoisonSpellID, Is.EqualTo(30019));
                Assert.That(BotPoisonSupply.Carried(bot, 30019), Is.EqualTo(1), "one vial a coat");
                blade.PoisonCharges = 0; blade.PoisonSpellID = 0;
                Assert.That(BotPoisonSupply.Maintain(bot), Is.True);
                Assert.That(BotPoisonSupply.Carried(bot, 30019), Is.Zero);
                blade.PoisonCharges = 0; blade.PoisonSpellID = 0;
                Assert.That(BotPoisonSupply.Maintain(bot), Is.False, "the vials are used up");
            }
            finally
            {
                BotPoisonSupply.Load(Array.Empty<DbItemTemplate>(), Array.Empty<Spell>());
            }
        }

        [Test]
        public void APoisonVialIsAPoisonItemWithACoatSpell()
        {
            Assert.That(BotPoisonSupply.IsVial(new DbInventoryItem { Object_Type = (int)eObjectType.Poison, PoisonSpellID = 30000, Count = 3 }), Is.True);
            Assert.That(BotPoisonSupply.IsVial(new DbInventoryItem { Object_Type = (int)eObjectType.Poison, PoisonSpellID = 0, Count = 3 }), Is.False);
            Assert.That(BotPoisonSupply.IsVial(new DbInventoryItem { Object_Type = (int)eObjectType.Magical, PoisonSpellID = 30000, Count = 3 }), Is.False);
        }

        #endregion

        #region Luxury and the governor

        [Test]
        public void OnlyARichBotSpendsAndAtMostTwoPercentOfItsSurplus() => Assert.Multiple(() =>
        {
            long threshold = BotLuxury.Threshold(50);
            Assert.That(threshold, Is.EqualTo(50 * 50_000L * 20), "100 gold a level");
            Assert.That(BotLuxury.Budget(threshold, 50, 1), Is.Zero, "not above the threshold");
            Assert.That(BotLuxury.Budget(threshold + 10_000_000, 50, 1), Is.EqualTo(200_000), "2% of the surplus at full intensity");
            Assert.That(BotLuxury.Budget(threshold + 10_000_000, 50, 0.5), Is.EqualTo(100_000));
            Assert.That(BotLuxury.Budget(threshold + 10_000_000, 50, 0), Is.Zero, "the governor says no");
            Assert.That(BotLuxury.Budget(threshold + 400_000, 50, 1), Is.Zero, "under a gold is not spent");
            Assert.That(BotLuxury.DraughtsToBuy(1, 2), Is.EqualTo(3));
            Assert.That(BotLuxury.DraughtsToBuy(9, 2), Is.Zero);
        });

        [Test]
        public void TheGovernorsBandIsTheRehearsalsGates()
        {
            RehearsalCalibration.GatesPart gates = RehearsalFixtures.Calibration.Gates;
            Assert.That(SinkGovernor.Gates.WeekPercentMax, Is.EqualTo(gates.SupplyGrowthWeekPercentMax));
            Assert.That(SinkGovernor.Gates.PerBotHourGoldMax, Is.EqualTo(gates.SupplyGrowthPerBotHourGoldMax));
        }

        [Test]
        public void TheRatioIsTheGrowthAsAShareOfTheBandsLimit() => Assert.Multiple(() =>
        {
            var band = new SinkGovernor.Band(400, 100);
            // An aged realm: 1,000 bots with 1,000 gold each; +4% of the supply an hour is 672% a week (1.68 of 400%).
            long supply = 1_000 * 10_000_000L;
            Assert.That(SinkGovernor.Ratio(supply, supply / 25, 1_000, band), Is.EqualTo(1.68).Within(1e-9));
            Assert.That(SinkGovernor.Ratio(supply, -supply / 25, 1_000, band), Is.LessThan(0), "shrinking");
            // A fresh realm (10 gold a bot): judged by gold a bot-hour; 50 gold a bot-hour is half the limit.
            Assert.That(SinkGovernor.Ratio(1_000 * 100_000L, 1_000 * 500_000L, 1_000, band), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(SinkGovernor.Ratio(0, 0, 0, band), Is.Zero);
        });

        [Test]
        public void TheGovernorRaisesLuxuryAboveTheTargetAndDropsItWhenTheSupplyShrinks()
        {
            SinkGovernor.State state = SinkGovernor.State.Start;
            Assert.That(state.Intensity, Is.Zero, "no hour read yet: no luxury");
            state = SinkGovernor.Step(state, 50);
            Assert.That(state.Intensity, Is.EqualTo(SinkGovernor.MaxStepPerHour).Within(1e-9), "one wild hour moves it one step");
            for (int hour = 0; hour < 6; hour++)
                state = SinkGovernor.Step(state, 1.2);
            Assert.That(state.Intensity, Is.EqualTo(1.0), "over the limit: full luxury");
            Assert.That(state.Over, Is.True);
            Assert.That(SinkGovernor.CommissionMultiplier([state]), Is.EqualTo(SinkGovernor.CommissionMultiplierWhenOver), "and fewer commissions");
            for (int hour = 0; hour < 24; hour++)
                state = SinkGovernor.Step(state, 0.1);
            Assert.That(state.Intensity, Is.Zero, "well under the target: no luxury");
            Assert.That(SinkGovernor.CommissionMultiplier([state]), Is.EqualTo(1.0));
            state = SinkGovernor.Step(SinkGovernor.Step(SinkGovernor.State.Start, 0.6), 0.6);
            Assert.That(state.Intensity, Is.GreaterThan(0).And.LessThan(SinkGovernor.MaxStepPerHour), "a little over the target: a little more");
            SinkGovernor.State shrinking = SinkGovernor.Step(new SinkGovernor.State(-0.2, 0.8, 3), -0.5);
            Assert.That(shrinking.Intensity, Is.EqualTo(0.4).Within(1e-9), "shrinking: halved");
            Assert.That(SinkGovernor.Step(state, double.NaN), Is.EqualTo(state), "a bad hour changes nothing");
        }

        [Test]
        public void TheGovernorReadsEachLedgerRowOnceAndSumsEverySinkAndFaucet()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = CraftingEconomyProperties.BOT_LUXURY_SPENDING = true;
            var row = new OfflineEconomyLedgerRecord
            {
                HourUtc = "2026-11-16T10:00:00Z", Realm = 1, BotCoin = 1_000 * 10_000_000L, Bots = 1_000,
                FaucetsJson = "{\"lootcoin\":600000000,\"vendorsale\":100000000}", SinksJson = "{\"dye\":300000000}",
            };
            Assert.That(SinkGovernor.Sum(row.FaucetsJson) - SinkGovernor.Sum(row.SinksJson), Is.EqualTo(400_000_000));
            Assert.That(SinkGovernor.Sum("not json"), Is.Zero);
            Assert.That(SinkGovernor.Instance.Apply(row), Is.True);
            Assert.That(SinkGovernor.Instance.Apply(row), Is.False, "an hour is applied once");
            SinkGovernor.State state = SinkGovernor.Instance.StateOf(eRealm.Albion);
            Assert.That(state.Hours, Is.EqualTo(1));
            Assert.That(state.Smoothed, Is.EqualTo(SinkGovernor.Ratio(row.BotCoin, 400_000_000, 1_000, SinkGovernor.Gates)).Within(1e-9));
            Assert.That(SinkGovernor.Instance.Intensity(eRealm.Albion), Is.EqualTo(state.Intensity));
            CraftingEconomyProperties.BOT_LUXURY_SPENDING = false;
            Assert.That(SinkGovernor.Instance.Intensity(eRealm.Albion), Is.Zero, "off: no luxury");
            SinkGovernor.Instance.Pulse(0);
            Assert.That(QuartermasterCommissions.GovernorMultiplier, Is.EqualTo(1.0), "off: commissions as WP5 built them");
        }

        #endregion

        #region Siege supply

        [Test]
        public void SiegeSupplyIsBelowTheMerchantPriceAndTheCrafterKeepsTheMargin()
        {
            Assert.That(BotSiegeRuntime.SupplyPrice(500_000), Is.EqualTo(400_000));
            Assert.That(BotSiegeRuntime.SupplyMargin(500_000), Is.EqualTo(150_000));
            TestBot buyer = Bot();
            _purses[buyer.DatabaseID] = 1_000_000;
            var crafter = new CrafterState(77, "Siegewright", eRealm.Albion, 50, eCraftingSkill.SiegeCrafting,
                new Dictionary<eCraftingSkill, int> { [eCraftingSkill.SiegeCrafting] = 600 }, 0, 0, CrafterSpendPolicy.Default);
            BotSiegeRuntime.FindSupplier = _ => crafter;

            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_SIEGE_SUPPLY = false;
            Assert.That(BotSiegeRuntime.TryPaySupply(buyer, 500_000, EconomyFlowKind.Siege, out var merchant), Is.True);
            Assert.That(merchant.Paid, Is.EqualTo(500_000), "off: the merchant's price as today");
            Assert.That(merchant.SupplierId, Is.Zero);

            CraftingEconomyProperties.BOT_SIEGE_SUPPLY = true;
            _moves.Clear();
            Assert.That(BotSiegeRuntime.TryPaySupply(buyer, 500_000, EconomyFlowKind.Siege, out var supplied), Is.True);
            Assert.That(supplied.Paid, Is.EqualTo(400_000));
            Assert.That(_purses[77], Is.EqualTo(150_000), "the margin over its materials");
            Assert.That(_moves.All(m => m.Kind == EconomyFlowKind.Siege), Is.True, "every move names its kind");
            Assert.That(_moves.Sum(m => m.Signed), Is.EqualTo(-250_000), "the realm's sink is the materials");

            BotSiegeRuntime.RefundSupply(buyer, supplied);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(500_000), "refunded");
            Assert.That(_purses[77], Is.Zero, "the margin went back");

            BotSiegeRuntime.FindSupplier = _ => null;
            Assert.That(BotSiegeRuntime.TryPaySupply(buyer, 100_000, EconomyFlowKind.RepairKit, out var none), Is.True);
            Assert.That(none.Paid, Is.EqualTo(100_000), "no siegecrafter: the merchant");
            Assert.That(BotSiegeRuntime.TryPaySupply(buyer, 10_000_000, EconomyFlowKind.Siege, out _), Is.False, "can't pay");
        }

        [Test]
        public void ACompanionNeverBuysFromASiegecrafter()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = CraftingEconomyProperties.BOT_SIEGE_SUPPLY = true;
            TestBot companion = Bot(helper: true);
            _purses[companion.DatabaseID] = 1_000_000;
            BotSiegeRuntime.FindSupplier = _ => throw new AssertionException("a companion never asks for a supplier");
            Assert.That(BotSiegeRuntime.TryPaySupply(companion, 500_000, EconomyFlowKind.Siege, out var paid), Is.True);
            Assert.That(paid.Paid, Is.EqualTo(500_000));
        }

        #endregion

        #region Registration and lines

        [Test]
        public void RegistrationAddsTheErrandTheStepsThePolicyAndTheStores()
        {
            BotServiceSeams.ResetForTests();
            EconomySeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            SinkSeams.RegisterForTests();
            Assert.That(BotServiceSeams.RegisteredCount, Is.EqualTo(5), "the errand and four merchant steps");
            SinkSeams.RegisterForTests();
            Assert.That(BotServiceSeams.RegisteredCount, Is.EqualTo(5), "registering twice adds nothing");
            Upkeep(on: false);
            TestBot bot = Bot();
            Assert.That(BotServiceSeams.WantsVisit(bot), Is.False, "off: no town visit");
            Assert.That(BotServiceSeams.NeededService(bot), Is.Null);
        }

        [Test]
        public void TheHourlyLinesSumTheHourPerRealmWhileTheirSwitchIsOn()
        {
            var lines = new List<string>();
            SinkSeams.HourlyLines.Instance.Write = lines.Add;
            Upkeep();
            BotGearWear.WearRoll = () => true;
            TestBot bot = Bot();
            GameInventoryItem.OnBotItemUsed(bot, Armor());
            GameInventoryItem.OnBotItemUsed(bot, Armor());
            SinkSeams.HourlyLines.Instance.WriteHour();
            Assert.That(lines, Has.Exactly(1).StartsWith("ECON_WEAR realm=Albion wearRolls=2 conditionLost=" + 2 * GameInventoryItem.ConditionLoss(50, 50, 100)));
            lines.Clear();
            SinkSeams.HourlyLines.Instance.WriteHour();
            Assert.That(lines, Is.Empty, "a new hour");
        }

        [Test]
        public void TheRepairLineKeepsTrue165sKeys()
        {
            string line = BotGearWear.RepairLine(Bot(), 84, new BotGearWear.Outcome(1, 2, 3_000, "Smith Bob", 0), true);
            Assert.That(line, Does.StartWith("ECON_REPAIR bot=\"Sink test bot\""));
            Assert.That(line, Does.Contain("own_skill=1 paid=2 copper=3000 by=\"Smith Bob\""));
        }

        #endregion
    }
}
