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
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP5: craft orders between gamebots, the crafters' extra Realm Exchange posts and the sale line. The
    /// orders half of rambodolphin13's DividedRealms UT_AutonomousBotCraftingOrders (ceac061a, e37f8810, 24f8a2e7;
    /// GPL-3.0) adapted to CraftRecipeInfo, the board's snapshot and the switches (bot_crafters and bot_craft_orders,
    /// both off by default); the recipe-choice half went with WP4 (UT_BotCrafting). Plus ours: the coin of every move is
    /// classified, the quartermaster's work goes through the same board, the tax rides on the sale line, the reclaim.
    /// Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public partial class UT_AutonomousBotCraftingOrders
    {
        internal sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
            public override void RefreshItemBonuses() { }
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => TemplateDatabase;
        }

        internal static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IObjectDatabase TemplateDatabase = DispatchProxy.Create<IObjectDatabase, TemplateReads>();

        public class TemplateReads : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && args[0] is string key)
                    return Templates.GetValueOrDefault(key);
                return Default(method);
            }
        }

        /// <summary>A database that keeps what is saved, for the sale ledger.</summary>
        public class SavedRows : DispatchProxy
        {
            public readonly List<DataObject> Saved = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "SaveObject")
                {
                    if (args[0] is IEnumerable<DataObject> rows)
                        Saved.AddRange(rows);
                    else if (args[0] is DataObject row)
                        Saved.Add(row);
                }
                return Default(method);
            }
        }

        private static object Default(MethodInfo method)
        {
            Type result = method.ReturnType;
            if (result == typeof(bool))
                return true;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
        }

        /// <summary>The catalog WP1 registers, as a test double.</summary>
        internal sealed class FakeCatalog : IRecipeCatalog, ICraftWorldIndex
        {
            public readonly List<CraftRecipeInfo> Recipes = new();
            public bool IsLoaded => true;
            public IReadOnlyList<CraftRecipeInfo> ForSkill(eCraftingSkill skill) => Recipes.Where(r => r.Skill == skill).ToList();
            public IEnumerable<CraftRecipeInfo> Find(eCraftingSkill skill, int minLevel, int maxLevel, eRealm realm) =>
                ForSkill(skill).Where(r => r.Level >= minLevel && r.Level <= maxLevel);
            public CraftRecipeInfo Get(string recipeId) => Recipes.FirstOrDefault(r => r.RecipeId == recipeId);
            public CraftRecipeInfo BestFor(int objectType, int itemType, int itemLevel, eRealm realm) => null;
            public IEnumerable<CraftNPC> MastersOf(eCraftingSkill skill, eRealm realm) => [];
            public IEnumerable<GameStaticItem> Stations(CraftStation station) => [];
            public IEnumerable<GameMerchant> SellersOf(string materialTemplateId, eRealm realm) => [];
        }

        /// <summary>Every coin move the board makes, with its kind.</summary>
        private readonly List<(long Bot, long Signed, EconomyFlowKind Kind)> _moves = new();
        private readonly Dictionary<long, long> _purses = new();
        private readonly Dictionary<long, GameBot> _live = new();
        private readonly List<DbInventoryItem> _marketItems = new();
        private GameServer _previousServer;
        private MarketSearchEngine _previousMarket;
        private MarketSearchEngine _testMarket;
        private FakeCatalog _catalog;
        private bool _crafters, _orders, _beggars;
        private int _listings, _tax;
        private string _commissions;
        private static long _nextId = 960000;

        private static FieldInfo MarketField => typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic);

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            _previousMarket = (MarketSearchEngine)MarketField.GetValue(null);
            _testMarket = new MarketSearchEngine();
            MarketField.SetValue(null, _testMarket);
            Templates.Clear();
            _purses.Clear();
            _live.Clear();
            _moves.Clear();
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _orders = CraftingEconomyProperties.BOT_CRAFT_ORDERS;
            _listings = CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS;
            _tax = CraftingEconomyProperties.EXCHANGE_TAX_PERCENT;
            _commissions = CraftingEconomyProperties.REALM_COMMISSIONS;
            _beggars = Properties.BOT_LIVING_BEGGARS;
            Properties.BOT_LIVING_BEGGARS = false;
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = true;
            CraftingEconomyProperties.REALM_COMMISSIONS = "doubled";
            CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS = 6;
            _catalog = new FakeCatalog();
            RecipeCatalogs.Current = _catalog;
            RecipeCatalogs.World = _catalog;
            AutonomousBotCrafting.ResetForTests();
            AutonomousBotCrafting.AllOnSaleForTests = true;
            BotCrafterRoster.Instance.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            MarketSeams.ResetForTests();
            MarketSeams.RegisterForTests();
            MarketSeams.FindLive = id => _live.GetValueOrDefault(id);
            AutonomousBotCraftOrders.SpendCoin = (bot, copper, kind) =>
            {
                if (_purses.GetValueOrDefault(bot) < copper)
                    return false;
                _purses[bot] -= copper;
                _moves.Add((bot, -copper, kind));
                return true;
            };
            AutonomousBotCraftOrders.PayCoin = (bot, copper, kind) =>
            {
                _purses[bot] = _purses.GetValueOrDefault(bot) + copper;
                _moves.Add((bot, copper, kind));
                return true;
            };
            QuartermasterCommissions.PayCrafter = AutonomousBotCraftOrders.PayCoin;
            AutonomousBotCraftOrders.FindLive = id => _live.GetValueOrDefault(id);
            AutonomousBotCraftOrders.CraftersOf = realm => _live.Values.Where(bot => bot.Realm == realm);
            AutonomousBotCraftOrders.OnSale = (_, _) => true;
            AutonomousBotCraftOrders.CoinOf = id => _purses.GetValueOrDefault(id);
            QuartermasterCommissions.CraftersOf = realm => _live.Values.Where(bot => bot.Realm == realm);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (DbInventoryItem item in _marketItems)
                MarketCache.RemoveItem(item);
            _marketItems.Clear();
            _testMarket.Dispose();
            MarketField.SetValue(null, _previousMarket);
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = _orders;
            CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS = _listings;
            CraftingEconomyProperties.EXCHANGE_TAX_PERCENT = _tax;
            CraftingEconomyProperties.REALM_COMMISSIONS = _commissions;
            Properties.BOT_LIVING_BEGGARS = _beggars;
            MarketSeams.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            AutonomousBotCrafting.ResetForTests();
            BotCrafterRoster.Instance.ResetForTests();
            RecipeCatalogs.Current = null;
            RecipeCatalogs.World = null;
            GameServer.LoadTestDouble(_previousServer);
        }

        #region Helpers

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        internal static long CrafterId(long after) =>
            Enumerable.Range(1, 100_000).Select(i => after + i).First(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Crafter);

        private TestBot Bot(string name, string skills = "", int level = 20, bool crafter = false)
        {
            long id = Interlocked.Increment(ref _nextId);
            if (crafter)
                _nextId = id = CrafterId(id);
            TestBot bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = (byte)level;
            bot.Realm = eRealm.Albion;
            bot.Name = name;
            bot.DatabaseID = id;
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, SerializedCraftingSkills = skills });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>());
            _live[bot.DatabaseID] = bot;
            return bot;
        }

        internal static DbItemTemplate Item(string id, int price = 10, int pack = 1, int maxCount = 1, int model = 1) =>
            Templates[id] = new DbItemTemplate { Id_nb = id, Name = id, Level = 1, Quality = 95, MaxCount = maxCount, PackSize = pack,
                MaxCondition = 10000, MaxDurability = 10000, Price = price, Model = model, IsTradable = true, IsDropable = true };

        internal static CraftRecipeInfo Info(Recipe recipe) =>
            new(recipe.Product.Id_nb, recipe.RequiredCraftingSkill, recipe.Level, recipe.Product.Id_nb, recipe.Product.Name,
                recipe.Product.Object_Type, recipe.Product.Item_Type, recipe.Product.Level, recipe.Product.Realm, recipe.IsForUniqueProduct,
                recipe.Product.IsStackable,
                recipe.Ingredients.Select(i => new CraftMaterial(i.Material.Id_nb, i.Material.Name, i.Material.Model, i.Count, i.Material.Price,
                    i.Material.PackSize)).ToList())
            { Recipe = recipe };

        internal static CraftRecipeInfo MakeRecipe(int level, long price = 10, int productLevel = 1, string id = null, bool unique = true,
            eObjectType type = eObjectType.SlashingWeapon, eCraftingSkill skill = eCraftingSkill.WeaponCrafting)
        {
            DbItemTemplate product = Item(id ?? $"product_{level}_{Guid.NewGuid():N}", 100);
            product.Level = productLevel;
            product.Object_Type = (int)type;
            product.Realm = (int)eRealm.Albion;
            DbItemTemplate material = Item("bars_" + price, (int)price, 1, 100, 519);
            return Info(new Recipe(product, [new Ingredient(2, material)], skill, level, !unique));
        }

        private static DbInventoryItem OwnPiece(GameBot maker, string name)
        {
            DbInventoryItem piece = GameInventoryItem.Create(Item(name));
            piece.IsCrafted = true;
            piece.Creator = maker.Name;
            Assert.That(maker.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, piece), Is.True);
            if (string.IsNullOrEmpty(piece.ObjectId))
                typeof(DataObject).GetProperty(nameof(DataObject.ObjectId)).SetValue(piece, Guid.NewGuid().ToString("N"));
            return piece;
        }

        private static CraftOrder OrderOf(string orderId) => AutonomousBotCraftOrders.All().Single(order => order.OrderId == orderId);

        #endregion

        #region Orders between bots (DR, adapted)

        [Test]
        public void APiecesPriceIsItsMaterialsAndAFee()
        {
            Assert.That(AutonomousBotCraftOrders.Quote(MakeRecipe(10, price: 10)), Is.EqualTo(20 + AutonomousBotCraftOrders.MinimumFee));
            Assert.That(AutonomousBotCraftOrders.Quote(MakeRecipe(10, price: 5_000)), Is.EqualTo(10_000 + 2_500));
        }

        [Test]
        public void ACrafterFillsOnlyWhatItMakesAtOrangeOrBetter()
        {
            CraftRecipeInfo recipe = MakeRecipe(110);
            Assert.That(AutonomousBotCraftOrders.CanFill("1|100;6|200", recipe), Is.True, "Orange: 84%");
            Assert.That(AutonomousBotCraftOrders.CanFill("1|85;6|200", recipe), Is.False, "Red: 68%");
            Assert.That(AutonomousBotCraftOrders.CanFill("2|300", MakeRecipe(1)), Is.False, "Not a weaponcrafter");
        }

        [Test]
        public void TheLeastBusyAbleCrafterOfTheRealmIsAsked()
        {
            TestBot buyer = Bot("Buyer");
            TestBot weak = Bot("Weak", "1|50;6|200");
            TestBot strong = Bot("Strong", "1|300;6|300");
            TestBot able = Bot("Able", "1|120;6|200");
            TestBot enemy = Bot("Enemy", "1|500;6|500");
            enemy.Realm = eRealm.Midgard;
            CraftRecipeInfo recipe = MakeRecipe(110);

            Assert.That(AutonomousBotCraftOrders.PickCrafter([buyer, weak, strong, able, enemy], recipe, buyer), Is.SameAs(strong),
                "The most skilled among the idle");

            _purses[buyer.DatabaseID] = 1_000_000;
            Assert.That(AutonomousBotCraftOrders.Place(buyer, strong, recipe), Is.Not.Null);
            Assert.That(AutonomousBotCraftOrders.PickCrafter([weak, strong, able, enemy], recipe, buyer), Is.SameAs(able),
                "A crafter with an order waits behind one without");
        }

        [Test]
        public void OnlyACrafterWhoseTaskEndsSoonIsAsked()
        {
            TestBot buyer = Bot("Buyer");
            TestBot late = Bot("Late", "1|300;6|300");
            TestBot soon = Bot("Soon", "1|120;6|200");
            TestBot later = Bot("Later", "1|200;6|200");
            TestBot resting = Bot("Resting", "1|500;6|500");
            late.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(100).ToString("O");
            soon.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(10).ToString("O");
            later.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(30).ToString("O");
            resting.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-ID-" + resting.DatabaseID + "-1";
            CraftRecipeInfo recipe = MakeRecipe(110);

            Assert.That(AutonomousBotCraftOrders.PickCrafter([late, soon, later, resting], recipe, buyer), Is.SameAs(soon),
                "The soonest break; not one 100 minutes away, nor one on a break without a craft trip");

            resting.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-CI-" + resting.DatabaseID + "-1";
            Assert.That(AutonomousBotCraftOrders.PickCrafter([late, soon, later, resting], recipe, buyer), Is.SameAs(resting),
                "A crafter on its craft trip now");
            Assert.That(AutonomousBotCraftOrders.PickCrafter([late], recipe, buyer), Is.Null);
        }

        [Test]
        public void TheBiggestAffordableUpgradeWithACrafterIsOrdered()
        {
            CraftRecipeInfo small = MakeRecipe(10, price: 10, productLevel: 18);
            CraftRecipeInfo big = MakeRecipe(20, price: 10, productLevel: 20);
            CraftRecipeInfo tooDear = MakeRecipe(30, price: 10_000, productLevel: 20);
            CraftRecipeInfo tooHigh = MakeRecipe(40, price: 10, productLevel: 25);
            CraftRecipeInfo noCrafter = MakeRecipe(50, price: 10, productLevel: 19);
            var gains = new Dictionary<CraftRecipeInfo, int> { [small] = 20, [big] = 50, [tooDear] = 500, [tooHigh] = 500, [noCrafter] = 400 };

            CraftRecipeInfo chosen = AutonomousBotCraftOrders.ChooseCommission([small, big, tooDear, tooHigh, noCrafter], eRealm.Albion, 20, 1_000,
                recipe => gains[recipe], recipe => recipe != noCrafter);

            Assert.That(chosen, Is.SameAs(big));
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([small], eRealm.Albion, 20, 1_000, _ => 0, _ => true), Is.Null,
                "No upgrade, no order");
        }

        [Test]
        public void ALowPieceIsStillOrderedAsAnUpgradeForALevel20Buyer()
        {
            // DR's night of 2026-11-04: crafters topped out at skill 28, so only low pieces could be made.
            CraftRecipeInfo low = MakeRecipe(10, price: 10, productLevel: 3);
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([low], eRealm.Albion, 22, 1_000, _ => 30, _ => true), Is.SameAs(low));
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([low], eRealm.Albion, 24, 1_000, _ => 30, _ => true), Is.Null,
                "More than the commission window below the buyer");
            Assert.That(AutonomousBotCraftOrders.FitsCommissionLevel(25, 20), Is.False, "Never above the buyer");
        }

        [Test]
        public void PlacingAnOrderTakesTheBuyersCoinIntoTheOrder()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            CraftRecipeInfo recipe = MakeRecipe(110, price: 100);
            long price = AutonomousBotCraftOrders.Quote(recipe);
            _purses[buyer.DatabaseID] = price - 1;

            Assert.That(AutonomousBotCraftOrders.Place(buyer, smith, recipe), Is.Null, "Too little coin");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);

            _purses[buyer.DatabaseID] = price + 5;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, recipe);

            Assert.That(order, Is.Not.Null);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(5));
            Assert.That(order.EscrowCopper, Is.EqualTo(price));
            Assert.That(order.State, Is.EqualTo(CraftOrderState.Open));
            Assert.That(order.BuyerId, Is.EqualTo(buyer.InternalID));
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.True);
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(buyer), Is.False);
            Assert.That(CraftOrders.Board.ForCrafter(smith.DatabaseID), Has.Count.EqualTo(1), "WP0's board answers");
            Assert.That(CraftOrders.Board.ForBuyer(buyer.InternalID).Single().OrderId, Is.EqualTo(order.OrderId));
            Assert.That(_moves, Is.EqualTo(new[] { (buyer.DatabaseID, -price, EconomyFlowKind.CraftOrder) }), "The escrow is a named move");
        }

        [Test]
        public void AnOrderIsMadeFirstWithRealMaterialsAndDeliveredIntoTheBuyersBag()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200", crafter: true);
            CraftRecipeInfo recipe = MakeRecipe(110, price: 100, id: "ordered_sword");
            CraftRecipeInfo other = MakeRecipe(125, price: 1);
            _catalog.Recipes.AddRange([recipe, other]);
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, recipe);

            BotCraftingRules.Session session = AutonomousBotCraftOrders.PlanOrder(smith, 1_000);
            Assert.That(session.Recipe, Is.SameAs(recipe));
            Assert.That(session.Copies, Is.EqualTo(1));
            Assert.That(session.OrderId, Is.EqualTo(order.OrderId));
            Assert.That(AutonomousBotCraftOrders.PlanOrder(smith, 199), Is.Null, "The crafter pays for the materials");

            DbItemTemplate bars = recipe.Recipe.Ingredients[0].Material;
            DbInventoryItem piece = null;
            for (int i = 0; i < 50 && piece == null; i++)
            {
                smith.Inventory.AddTemplate(GameInventoryItem.Create(bars), 2, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                piece = AutonomousBotCrafting.Attempt(smith, session, out _, new Random(i));
                if (piece != null && string.IsNullOrEmpty(piece.ObjectId))
                    Assert.Inconclusive("The test inventory gave the piece no id");
            }
            Assert.That(piece, Is.Not.Null);
            Assert.That(OrderOf(order.OrderId).State, Is.EqualTo(CraftOrderState.Made));
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.True);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.Zero, "The buyer's piece is not stock to sell");
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(smith, piece, out _), Is.False, "Never worn by its maker");
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.False);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.EqualTo(1));

            Assert.That(smith.Inventory.AllItems, Does.Not.Contain(piece));
            Assert.That(buyer.Inventory.AllItems, Does.Contain(piece), "The real item, not a copy");
            Assert.That(piece.OwnerID, Is.EqualTo(buyer.InternalID));
            Assert.That(_purses[smith.DatabaseID], Is.EqualTo(order.EscrowCopper), "The order's coin goes to the crafter");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.False);
            Assert.That(_moves.Sum(move => move.Signed), Is.Zero, "Escrow in, payout out: the order made and destroyed no coin");
            Assert.That(_moves.All(move => move.Kind == EconomyFlowKind.CraftOrder), Is.True);
        }

        [Test]
        public void AtTheTaskBreakTheCrafterGoesOnATripForItsOrder()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200", crafter: true);
            _purses[buyer.DatabaseID] = 10_000;
            AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            var random = new CountingRandom();
            Assert.That(BotCrafterSeams.TripActivity.Instance.Wants(smith, random), Is.True);
            Assert.That(random.Draws, Is.Zero, "An order always goes: no roll");
        }

        [Test]
        public void ABuyerOutOfTheWorldWaitsForTheNextBreak()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            Assert.That(AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece), Is.True);
            _live.Remove(buyer.DatabaseID);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.Zero);
            Assert.That(smith.Inventory.AllItems, Does.Contain(piece));
            Assert.That(AutonomousBotCraftOrders.Count, Is.EqualTo(1));
        }

        [Test]
        public void AnExpiredOrderRefundsTheBuyerAndLeavesThePieceWithItsMaker()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200", crafter: true);
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);

            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow), Is.Zero, "Not yet");
            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + AutonomousBotCraftOrders.Lifetime + TimeSpan.FromMinutes(1)), Is.EqualTo(1));

            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(10_000), "Every copper back");
            Assert.That(_purses.GetValueOrDefault(smith.DatabaseID), Is.Zero);
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.False);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.EqualTo(1), "Now the maker's own work to sell");
            Assert.That(_moves.Sum(move => move.Signed), Is.Zero);
        }

        [Test]
        public void APieceTheCrafterLostRefundsTheBuyer()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);
            smith.Inventory.RemoveItem(piece);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.Zero);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(10_000));
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
        }

        [Test]
        public void SavedOrdersComeBackAfterARestart()
        {
            var open = new OfflineBotCraftOrderRecord { OrderId = "a", BuyerBotId = 1, CrafterBotId = 2, Copper = 300, ExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O") };
            var made = new OfflineBotCraftOrderRecord { OrderId = "b", BuyerBotId = 3, CrafterBotId = 2, Copper = 400, State = AutonomousBotCraftOrders.MadeState,
                ItemObjectId = "item-b", ExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O") };

            AutonomousBotCraftOrders.Load([open, made]);

            Assert.That(AutonomousBotCraftOrders.Count, Is.EqualTo(2));
            Assert.That(AutonomousBotCraftOrders.OrdersOf(2), Has.Length.EqualTo(2));
            Assert.That(AutonomousBotCraftOrders.OfBuyer(AutonomousBotEconomy.GetOwnerId(1)).Single().OrderId, Is.EqualTo("a"),
                "A DR row (no BuyerId) is the bot's by its BuyerBotId");
            DbInventoryItem piece = GameInventoryItem.Create(Item("held"));
            typeof(DataObject).GetProperty(nameof(DataObject.ObjectId)).SetValue(piece, "item-b");
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.True, "A made piece stays held across the restart");
            Assert.That(AutonomousBotCraftOrders.IsDue("not a time", DateTime.UtcNow), Is.True, "A damaged row is refunded, not kept");
            CraftOrder damaged = AutonomousBotCraftOrders.ToOrder(new OfflineBotCraftOrderRecord { OrderId = "c", ExpiresUtc = "garbage" });
            Assert.That(damaged.ExpiresUtc, Is.EqualTo(DateTime.MinValue), "...and expires at the next pass");
        }

        [Test]
        public void TheSwitchesStopOrders()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));

            CraftingEconomyProperties.BOT_CRAFT_ORDERS = false;
            Assert.That(AutonomousBotCraftOrders.TryCommission(buyer), Is.Null, "bot_craft_orders off: no bot orders");
            Assert.That(MilestoneCraftSets.TryPost(Bot("Milestone", level: 20)), Is.Zero);
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.True, "an order already placed is still made");

            CraftingEconomyProperties.BOT_CRAFTERS = false;
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.False);
            Assert.That(AutonomousBotCraftOrders.PlanOrder(smith, 10_000), Is.Null);
            int moves = _moves.Count;
            AutonomousBotCraftOrders.AtTaskBreak(buyer);
            Assert.That(_moves, Has.Count.EqualTo(moves), "bot_crafters off: a task break does nothing");
        }

        [Test]
        public void TheSwitchesAreOnByDefault()
        {
            // Owner 2026-11-16: every Living Server feature on by default (built off in WP5).
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.BOT_CRAFT_ORDERS))
                .GetCustomAttribute<ServerPropertyAttribute>().DefaultValue, Is.EqualTo(true));
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.BOT_CRAFTERS))
                .GetCustomAttribute<ServerPropertyAttribute>().DefaultValue, Is.EqualTo(true));
        }

        [Test]
        public void ACompanionNeverOrdersOrFills()
        {
            TestBot buyer = Bot("Buyer");
            TestBot companion = Bot("Companion", "1|300;6|300");
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(companion, false);
            _purses[companion.DatabaseID] = 1_000_000;
            Assert.That(AutonomousBotCraftOrders.PickCrafter([companion], MakeRecipe(110), buyer), Is.Null);
            Assert.That(AutonomousBotCraftOrders.TryCommission(companion), Is.Null);
            AutonomousBotCraftOrders.AtTaskBreak(companion);
            Assert.That(_moves, Is.Empty);
        }

        [Test]
        public void TheBoardIsWP0sBoardOnceRegistered()
        {
            Assert.That(CraftOrders.Board, Is.SameAs(AutonomousBotCraftOrders.Board.Instance));
            Assert.That(EconomyLifecycle.Count, Is.GreaterThanOrEqualTo(3), "orders, index, commissions");
            Assert.That(BotServiceSeams.RegisteredCount, Is.EqualTo(1), "the task-break hook");
            MarketSeams.ResetForTests();
            Assert.That(CraftOrders.Board, Is.Not.SameAs(AutonomousBotCraftOrders.Board.Instance), "reset: WP0's empty board");
        }

        #endregion

        #region Listing cap (DR's own-work posts)

        private void List(GameBot bot, int count)
        {
            for (int i = 0; i < count; i++)
            {
                DbInventoryItem listed = GameInventoryItem.Create(Item($"listing {bot.DatabaseID} {i}"));
                listed.OwnerID = bot.InternalID;
                listed.OwnerLot = RealmExchangeBroker.AlbionOwnerLot;
                listed.SlotPosition = (int)eInventorySlot.Consignment_First + i;
                listed.SellPrice = 100;
                listed.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
                Assert.That(MarketCache.AddItem(listed), Is.True);
                _marketItems.Add(listed);
            }
        }

        [Test]
        public void OwnWorkHasItsOwnExtraPosts()
        {
            TestBot smith = Bot("Smith", "1|50", crafter: true);
            List(smith, AutonomousBotEconomy.MaxBotRealmExchangeListings);
            DbInventoryItem own = OwnPiece(smith, "own sword");
            DbInventoryItem loot = GameInventoryItem.Create(Item("found sword"));

            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(smith.InternalID), Is.EqualTo(-1), "Loot stops at the cap");
            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(smith.InternalID, own), Is.GreaterThanOrEqualTo(0));
            Assert.That(AutonomousBotEconomy.CanListNow(smith, own), Is.True);
            Assert.That(AutonomousBotEconomy.CanListNow(smith, loot), Is.False);
            Assert.That(EconomySeams.Listings.MaxListings(smith.InternalID), Is.EqualTo(6), "bot_crafter_exchange_listings: DR's 3 + 3");

            TestBot full = Bot("Full", "1|50", crafter: true);
            List(full, 6);
            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(full.InternalID, OwnPiece(full, "more")), Is.EqualTo(-1), "The extra posts have an end too");

            CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS = 4;
            Assert.That(EconomySeams.Listings.ListingCap(smith.InternalID, own), Is.EqualTo(4), "The setting sizes them");
            CraftingEconomyProperties.BOT_CRAFTER_EXCHANGE_LISTINGS = 1;
            Assert.That(EconomySeams.Listings.ListingCap(smith.InternalID, own), Is.EqualTo(3), "Never below every bot's 3");

            CraftingEconomyProperties.BOT_CRAFTERS = false;
            Assert.That(EconomySeams.Listings.ListingCap(smith.InternalID, own), Is.EqualTo(3), "Off: today's cap");
            Assert.That(EconomySeams.Listings.MaxListings(smith.InternalID), Is.EqualTo(3));
        }

        [Test]
        public void AtTheCapACrafterStillPostsItsOwnWork()
        {
            TestBot smith = Bot("Smith", "1|50", crafter: true);
            smith.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-CI-" + smith.DatabaseID + "-1";
            List(smith, AutonomousBotEconomy.MaxBotRealmExchangeListings);
            DbInventoryItem loot = GameInventoryItem.Create(Item("dear loot", 100_000));
            loot.Level = 30;
            loot.Quality = 100;
            Assert.That(smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, loot), Is.True);
            DbInventoryItem own = OwnPiece(smith, "own sword");
            BotCrafterSeams.RegisterForTests();

            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(smith)?.Item, Is.SameAs(own));
        }

        [Test]
        public void APieceHeldForAnOrderIsNeverListedOrVendored()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200", crafter: true);
            smith.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-CI-" + smith.DatabaseID + "-1";
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "held sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);

            Assert.That(EconomySeams.IsReserved(smith, piece), Is.True);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(smith)?.Item, Is.Not.SameAs(piece));
            Assert.That(AutonomousBotEconomy.FindVendorTrashCandidate(smith), Is.Not.SameAs(piece));
        }

        #endregion

        #region The sale line and the tax

        [Test]
        public void TheSaleLineIsDividedRealmsWithTheTax()
        {
            var sale = new RealmExchangeSaleLedger.Sale(1, "Bronze sword", 1, 500, "offlinebot:1", "Smith", "offlinebot:2", "Buyer",
                DateTime.UtcNow, TaxCopper: 25, Crafted: true);
            Assert.That(MarketSeams.SaleLine(sale), Is.EqualTo(
                "AUTONOMOUS_EXCHANGE_SOLD realm=1 item=\"Bronze sword\" quantity=1 price=500 seller=\"Smith\" sellerId=\"offlinebot:1\" " +
                "buyer=\"Buyer\" buyerId=\"offlinebot:2\" crafted=1 tax=25"));
            Assert.That(MarketSeams.SaleLine(sale with { Crafted = false }), Does.Contain(" crafted=0 "));
        }

        [Test]
        public void ACommittedSaleReachesTheMarketAndTheCraftersBook()
        {
            TestBot smith = Bot("Smith", "1|50", crafter: true);
            BotCrafterRoster.Instance.Enroll(smith, eCraftingSkill.WeaponCrafting);
            var database = DispatchProxy.Create<IObjectDatabase, SavedRows>();
            var sale = new RealmExchangeSaleLedger.Sale(1, "Bronze sword", 1, 500, smith.InternalID, "Smith", "offlinebot:2", "Buyer",
                DateTime.UtcNow, 25, true);

            Assert.That(RealmExchangeSaleLedger.CommitSale(database, sale, new OfflineWorldBotRecord()), Is.True);

            Assert.That(BotCrafterRoster.Instance.TryGetRow(smith.DatabaseID, out OfflineBotCrafterRecord row), Is.True);
            Assert.That(row.CoinEarned, Is.EqualTo(475), "the proceeds after the tax");
        }

        #endregion

        #region The reclaim of unsold work

        [Test]
        public void UnsoldWorkComesBackToItsCrafterAndGoesToAVendorNext()
        {
            TestBot smith = Bot("Smith", "1|50", crafter: true);
            BotCrafterSeams.RegisterForTests();
            DbInventoryItem own = OwnPiece(smith, "unsold sword");
            Assert.That(smith.Inventory.RemoveItemWithoutDbDeletion(own), Is.True);
            own.OwnerID = smith.InternalID;
            own.OwnerLot = RealmExchangeBroker.AlbionOwnerLot;
            own.SlotPosition = (int)eInventorySlot.Consignment_First;
            own.SellPrice = 300;
            own.RealmExchangeListedUtc = DateTime.UtcNow.AddHours(-30).ToString("O");
            MarketCache.AddItem(own);
            _marketItems.Add(own);

            Assert.That(MarketSeams.ReclaimExpired(own, smith.DatabaseID), Is.True);
            Assert.That(smith.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), Does.Contain(own));
            Assert.That(own.OwnerLot, Is.Zero);
            Assert.That(own.SellPrice, Is.Zero);
            Assert.That(EconomySeams.BlocksListing(smith, own), Is.True, "not posted again");
            Assert.That(EconomySeams.KeepFromVendor(smith, own), Is.False, "a vendor takes it at the next break");

            DbInventoryItem loot = GameInventoryItem.Create(Item("loot"));
            Assert.That(MarketSeams.ReclaimExpired(loot, smith.DatabaseID), Is.False, "Loot is deleted as before");
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            Assert.That(MarketSeams.ReclaimExpired(own, smith.DatabaseID), Is.False, "Off: deleted as before");
        }

        #endregion

        /// <summary>Counts every draw, so a test can prove a seam drew none.</summary>
        private sealed class CountingRandom : Random
        {
            public int Draws;
            public CountingRandom() : base(1) { }
            public override int Next() { Draws++; return base.Next(); }
            public override int Next(int maxValue) { Draws++; return base.Next(maxValue); }
            public override int Next(int minValue, int maxValue) { Draws++; return base.Next(minValue, maxValue); }
            public override double NextDouble() { Draws++; return base.NextDouble(); }
            protected override double Sample() { Draws++; return base.Sample(); }
        }
    }
}
