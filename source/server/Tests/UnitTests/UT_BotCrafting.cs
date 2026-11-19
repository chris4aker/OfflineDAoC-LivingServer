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
    /// Crafting 2.0 WP4: gamebot crafting. The crafting half of rambodolphin13's DividedRealms UT_AutonomousBotQuestsCrafting
    /// (93c25b69, ceac061a; GPL-3.0) adapted to CraftRecipeInfo and CraftRules, plus our rules: Crafter-only, orders by
    /// realm weights, the bot pace, titles, True165's best-of-session and enchanting (ported from shadowofze's True165,
    /// GPL-3.0), the bounded work per tick, the seams with the switch off, the station hangout, the voice's truth rule.
    /// Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotCrafting
    {
        private sealed class TestBot : GameBot
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

        private static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IObjectDatabase TemplateDatabase = DispatchProxy.Create<IObjectDatabase, TemplateReads>();

        public class TemplateReads : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && args[0] is string key)
                    return Templates.GetValueOrDefault(key);
                Type result = method.ReturnType;
                if (result == typeof(bool))
                    return true;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
            }
        }

        /// <summary>The catalog and world index WP1 registers, as a test double.</summary>
        private sealed class FakeCatalog : IRecipeCatalog, ICraftWorldIndex
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

        private GameServer _previousServer;
        private FakeCatalog _catalog;
        private static long _nextId = 940000;
        private bool _crafters, _beggars;
        private string _pace;
        private double _botXpRate;
        private string _botXpByLevel;
        private double _chatRate;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            Templates.Clear();
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _pace = CraftingEconomyProperties.BOT_CRAFTING_PACE;
            _chatRate = CraftingEconomyProperties.BOT_CRAFTER_CHAT_RATE;
            _botXpRate = Properties.BOT_XP_RATE;
            _botXpByLevel = Properties.BOT_XP_RATE_BY_LEVEL;
            _beggars = Properties.BOT_LIVING_BEGGARS;
            Properties.BOT_XP_RATE = 1.0;
            Properties.BOT_XP_RATE_BY_LEVEL = string.Empty;
            Properties.BOT_LIVING_BEGGARS = false;
            CraftingEconomyProperties.BOT_CRAFTING_PACE = "xp";
            _catalog = new FakeCatalog();
            RecipeCatalogs.Current = _catalog;
            RecipeCatalogs.World = _catalog;
            AutonomousBotCrafting.ResetForTests();
            BotCrafterRoster.Instance.ResetForTests();
            BotCrafterTypes.ResetForTests();
            BotEnchanting.ClearCache();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFTING_PACE = _pace;
            CraftingEconomyProperties.BOT_CRAFTER_CHAT_RATE = _chatRate;
            Properties.BOT_XP_RATE = _botXpRate;
            Properties.BOT_XP_RATE_BY_LEVEL = _botXpByLevel;
            Properties.BOT_LIVING_BEGGARS = _beggars;
            RecipeCatalogs.Current = null;
            RecipeCatalogs.World = null;
            AutonomousBotCrafting.ResetForTests();
            BotCrafterRoster.Instance.ResetForTests();
            BotCrafterTypes.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            BotCrafters.Roster = null;
            GameServer.LoadTestDouble(_previousServer);
        }

        #region Helpers

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static TestBot Bot(string name = "Smith", string skills = "", long? id = null)
        {
            TestBot bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = 20;
            bot.Realm = eRealm.Albion;
            bot.Name = name;
            bot.DatabaseID = id ?? Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, SerializedCraftingSkills = skills });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>());
            return bot;
        }

        private static DbItemTemplate Item(string id, int price = 10, int pack = 1, int maxCount = 1, int model = 1) =>
            Templates[id] = new DbItemTemplate { Id_nb = id, Name = id, Level = 1, Quality = 95, MaxCount = maxCount, PackSize = pack,
                MaxCondition = 10000, MaxDurability = 10000, Price = price, Model = model, IsTradable = true, IsDropable = true };

        private static CraftRecipeInfo Info(Recipe recipe, string id = null) =>
            new(id ?? recipe.Product.Id_nb, recipe.RequiredCraftingSkill, recipe.Level, recipe.Product.Id_nb, recipe.Product.Name,
                recipe.Product.Object_Type, recipe.Product.Item_Type, recipe.Product.Level, recipe.Product.Realm, recipe.IsForUniqueProduct,
                recipe.Product.IsStackable,
                recipe.Ingredients.Select(i => new CraftMaterial(i.Material.Id_nb, i.Material.Name, i.Material.Model, i.Count, i.Material.Price,
                    i.Material.PackSize)).ToList())
            { Recipe = recipe };

        private static CraftRecipeInfo MakeRecipe(eCraftingSkill skill, int level, int realm = (int)eRealm.Albion, long price = 10,
            int model = 519, string id = null, bool unique = false, int productLevel = 1)
        {
            DbItemTemplate product = Item(id ?? $"product_{level}_{Guid.NewGuid():N}", 100);
            product.Realm = realm;
            product.Level = productLevel;
            product.Object_Type = (int)eObjectType.SlashingWeapon;
            DbItemTemplate material = Item("bars_" + price + "_" + model, (int)price, 1, 100, model);
            return Info(new Recipe(product, [new Ingredient(2, material)], skill, level, !unique));
        }

        private static int Count(TestBot bot, string id) =>
            bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Where(i => i?.Id_nb == id).Sum(i => i.Count);

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

        #endregion

        #region DividedRealms' rules, adapted

        [Test]
        public void ClassesFollowCraftsThatSuitThem()
        {
            Assert.That(BotCraftingRules.ClassOrder(eCharacterClass.Ranger, eClassType.Hybrid, 4), Is.EqualTo(eCraftingSkill.Fletching));
            Assert.That(BotCraftingRules.ClassOrder(eCharacterClass.Eldritch, eClassType.ListCaster, 4), Is.EqualTo(eCraftingSkill.Tailoring));
            Assert.That(BotCraftingRules.ClassOrder(eCharacterClass.Hero, eClassType.PureTank, 4), Is.EqualTo(eCraftingSkill.WeaponCrafting));
            Assert.That(BotCraftingRules.ClassOrder(eCharacterClass.Hero, eClassType.PureTank, 5), Is.EqualTo(eCraftingSkill.ArmorCrafting));
        }

        [Test]
        public void StationsFollowTheCraftsToolChecks()
        {
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.WeaponCrafting, (int)eObjectType.Sword, []), Is.EqualTo(CraftStation.Forge));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.Tailoring, (int)eObjectType.Cloth, [522]), Is.EqualTo(CraftStation.None));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.Tailoring, (int)eObjectType.Studded, [521, 519]), Is.EqualTo(CraftStation.Forge));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.Fletching, (int)eObjectType.Longbow, []), Is.EqualTo(CraftStation.Lathe));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.Fletching, (int)eObjectType.Arrow, []), Is.EqualTo(CraftStation.None));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.Alchemy, 0, []), Is.EqualTo(CraftStation.AlchemyTable));
            Assert.That(BotCraftingRules.StationFor(eCraftingSkill.SpellCrafting, 0, []), Is.EqualTo(CraftStation.AlchemyTable));
            Assert.That(BotCraftingRules.StationOf(478, "anvil"), Is.EqualTo(CraftStation.Forge));
            Assert.That(BotCraftingRules.StationOf(1, "Lathe"), Is.EqualTo(CraftStation.Lathe));
            Assert.That(BotCraftingRules.StationOf(820, ""), Is.EqualTo(CraftStation.AlchemyTable));
            Assert.That(BotCraftingRules.StationOf(5, "barrel"), Is.EqualTo(CraftStation.None));
        }

        [Test]
        public void TheRecipeChosenTeachesBestAndCanBeMade()
        {
            string saved = "1|100;6|100";
            CraftRecipeInfo orange = MakeRecipe(eCraftingSkill.WeaponCrafting, 110);
            CraftRecipeInfo grey = MakeRecipe(eCraftingSkill.WeaponCrafting, 15);
            CraftRecipeInfo impossible = MakeRecipe(eCraftingSkill.WeaponCrafting, 200);
            CraftRecipeInfo midgard = MakeRecipe(eCraftingSkill.WeaponCrafting, 105, (int)eRealm.Midgard);
            CraftRecipeInfo notSold = MakeRecipe(eCraftingSkill.WeaponCrafting, 112);

            BotCraftingRules.Choice choice = BotCraftingRules.ChooseRecipe([grey, impossible, midgard, notSold, orange],
                eCraftingSkill.WeaponCrafting, saved, eRealm.Albion, 1_000, recipe => recipe != notSold);

            Assert.That(choice.Recipe, Is.SameAs(orange));
            Assert.That(choice.Make, Is.EqualTo(84));
            Assert.That(choice.Gain, Is.EqualTo(55));
        }

        [Test]
        public void TheHardestOfTheTiesWinsAndAUsefulPieceFirst()
        {
            string saved = "1|100;6|100";
            CraftRecipeInfo orange = MakeRecipe(eCraftingSkill.WeaponCrafting, 110);
            CraftRecipeInfo red = MakeRecipe(eCraftingSkill.WeaponCrafting, 130);
            Assert.That(BotCraftingRules.ChooseRecipe([orange, red], eCraftingSkill.WeaponCrafting, saved, eRealm.Albion, 1_000, _ => true).Recipe,
                Is.SameAs(red), "84 x 55 and 68 x 65 teach about the same: the harder one (DR)");
            Assert.That(BotCraftingRules.ChooseRecipe([orange, red], eCraftingSkill.WeaponCrafting, saved, eRealm.Albion, 1_000, _ => true,
                recipe => recipe == orange).Recipe, Is.SameAs(orange), "a piece the bot can use comes first");
        }

        [Test]
        public void ANoRealmProductFollowsThePlayersRealmRule()
        {
            CraftRecipeInfo none = MakeRecipe(eCraftingSkill.WeaponCrafting, 110, 0);
            Assert.That(BotCraftingRules.ChooseRecipe([none], eCraftingSkill.WeaponCrafting, "1|100;6|100", eRealm.Albion, 1_000, _ => true),
                Is.Null, "allow_craft_norealm_items off: a player cannot make it either");
            Assert.That(BotCraftingRules.ChooseRecipe([none], eCraftingSkill.WeaponCrafting, "1|100;6|100", eRealm.Albion, 1_000, _ => true,
                allowNoRealm: true), Is.Not.Null);
        }

        [Test]
        public void ASecondarySkillHoldsBackARecipe()
        {
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 150);
            Assert.That(BotCraftingRules.MeetsSecondaryRequirement(recipe, "1|140;6|80"), Is.False);
            Assert.That(BotCraftingRules.MeetsSecondaryRequirement(recipe, "1|140;6|90"), Is.True);
        }

        [Test]
        public void ARecipeTheBotCannotPayForIsNotChosen()
        {
            CraftRecipeInfo dear = MakeRecipe(eCraftingSkill.WeaponCrafting, 110, price: 100);
            Assert.That(BotCraftingRules.ChooseRecipe([dear], eCraftingSkill.WeaponCrafting, "1|100;6|100", eRealm.Albion, 199, _ => true), Is.Null);
            Assert.That(BotCraftingRules.ChooseRecipe([dear], eCraftingSkill.WeaponCrafting, "1|100;6|100", eRealm.Albion, 200, _ => true), Is.Not.Null);
        }

        [Test]
        public void ShoppingCountsWhatIsCarriedAndBuysWholePacks()
        {
            DbItemTemplate bars = Item("bars", 7, 5);
            DbItemTemplate thread = Item("thread", 3);
            CraftRecipeInfo recipe = Info(new(Item("sword"), [new Ingredient(4, bars), new Ingredient(1, thread)], eCraftingSkill.WeaponCrafting, 15, true));

            List<BotCraftingRules.Purchase> list = BotCraftingRules.Shopping(recipe, 3, material => 3);

            Assert.That(list, Has.Count.EqualTo(1), "Three threads are in the bag already");
            Assert.That(list[0].Material.TemplateId, Is.EqualTo("bars"));
            Assert.That(list[0].Packs, Is.EqualTo(2), "Nine more bars come in packs of five");
            Assert.That(list[0].Amount, Is.EqualTo(10));
            Assert.That(list[0].Copper, Is.EqualTo(14));
        }

        [Test]
        public void ASessionSpendsAShareOfTheCoinAndHasAnEnd()
        {
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15, price: 100);
            Assert.That(BotCraftingRules.CopiesFor(recipe, 800), Is.EqualTo(1));
            Assert.That(BotCraftingRules.CopiesFor(recipe, 799), Is.Zero);
            Assert.That(BotCraftingRules.CopiesFor(recipe, 1_000_000), Is.EqualTo(BotCraftingRules.CraftsPerSession));
        }

        [Test]
        public void TheTripRollFollowsOurChancesAndTheLevelFloor()
        {
            Assert.That(BotCraftingRules.WantsTrip(false, 4, 0, 0), Is.False, "Too low a level");
            Assert.That(BotCraftingRules.WantsTrip(false, 10, 0, BotCraftingRules.JoinChance - 0.01), Is.True);
            Assert.That(BotCraftingRules.WantsTrip(false, 10, 0, BotCraftingRules.JoinChance + 0.01), Is.False);
            Assert.That(BotCraftingRules.WantsTrip(true, 10, 0, BotCraftingRules.SessionChance - 0.01), Is.True);
            Assert.That(BotCraftingRules.WantsTrip(true, 10, 0, BotCraftingRules.SessionChance + 0.01), Is.False);
            Assert.That(BotCraftingRules.WantsTrip(true, 10, 3, 0.155), Is.True, "Three stacks of materials: DR's engine rule");
        }

        [Test]
        public void ASessionsCopiesFitTheBagAndTheTripsTimeLeft()
        {
            Assert.That(BotCraftingRules.CopiesThatFit(10, false, 40, 5, 100, BotCraftingRules.TripWorkMilliseconds), Is.EqualTo(10));
            Assert.That(BotCraftingRules.CopiesThatFit(10, false, 9, 5, 100, BotCraftingRules.TripWorkMilliseconds), Is.EqualTo(5),
                "nine free slots less the four kept for loot");
            Assert.That(BotCraftingRules.CopiesThatFit(10, true, 0, 5, 100, BotCraftingRules.TripWorkMilliseconds), Is.EqualTo(10),
                "a stack needs no new slot per piece");
            Assert.That(BotCraftingRules.CopiesThatFit(10, false, 40, 30, 50, 5 * 60_000), Is.EqualTo(5),
                "30 s an attempt at 50% to make is a piece a minute: five fit in five minutes");
            Assert.That(BotCraftingRules.CopiesThatFit(10, false, 40, 30, 50, 30_000), Is.Zero);
            Assert.That(BotCraftingRules.CopiesThatFit(10, false, 40, 5, 0, 60_000), Is.Zero, "a recipe that cannot be made");
        }

        [Test]
        public void ATripWorksUpToThreeSessionsWithinItsTime()
        {
            Assert.That(BotCraftingRules.AnotherSession(1, 60_000), Is.True);
            Assert.That(BotCraftingRules.AnotherSession(BotCraftingRules.SessionsPerTrip, 60_000), Is.False);
            Assert.That(BotCraftingRules.AnotherSession(1, BotCraftingRules.TripWorkMilliseconds), Is.False);
            Assert.That(BotCraftingRules.TripWorkMilliseconds, Is.LessThan(AutonomousObjectiveAssignments.MaximumBetweenTaskDuration.TotalMilliseconds),
                "the inventory stop that posts the work still fits in the task break");
        }

        /// <summary>
        /// One night of a crafter at the pure rules (CraftRules, DR's recipe choice, the bot pace): a recipe ladder every ten
        /// levels, each piece tried until made, each made piece rolling the player's chance to gain. Returns the skill.
        /// </summary>
        private static int NightSkill(int pieces, double rate, int seed)
        {
            var ladder = Enumerable.Range(0, 110).Select(i => MakeRecipe(eCraftingSkill.WeaponCrafting, 1 + i * 10)).ToList();
            var random = new Random(seed);
            string saved = "1|1;6|1";
            for (int piece = 0; piece < pieces; piece++)
            {
                BotCraftingRules.Choice choice = BotCraftingRules.ChooseRecipe(ladder, eCraftingSkill.WeaponCrafting, saved, eRealm.Albion,
                    long.MaxValue, _ => true);
                if (choice == null)
                    break;
                while (!(choice.Make > random.Next(100))) { } // a failure costs nothing and is tried again
                if (!(CraftRules.BaseChanceToGainPoint(choice.Skill, choice.Recipe.Level) > random.Next(100)))
                    continue;
                foreach ((eCraftingSkill skill, bool primary) in BotCraftingRules.GainedSkills(eCraftingSkill.WeaponCrafting, [519]))
                    saved = BotCraftingSkills.Gain(saved, skill, primary,
                        BotCraftingRules.PointsPerGain("xp", rate, BotCraftingSkills.Value(saved, skill), primary));
            }
            return BotCraftingSkills.Value(saved, eCraftingSkill.WeaponCrafting);
        }

        private static int Median(IEnumerable<int> values)
        {
            int[] sorted = values.OrderBy(v => v).ToArray();
            return sorted[sorted.Length / 2];
        }

        [Test]
        public void ANightOfCraftingNoLongerEndsAtSkillEight()
        {
            // DR's night of 2026-11-04 left the median crafter at skill 8: a trip at about 15% of some 7 task breaks, one
            // session of at most 6 pieces each. Ours: a Crafter's trip at half its breaks, up to 3 sessions of 10 pieces.
            // The trips per night are an assumption (tasks of 45-120 minutes over 10 hours); the rest is the pure rules.
            int dr = Median(Enumerable.Range(0, 21).Select(seed => NightSkill(1 * 1 * 6, 1.0, seed)));
            int ours = Median(Enumerable.Range(0, 21).Select(seed => NightSkill(3 * BotCraftingRules.SessionsPerTrip * BotCraftingRules.CraftsPerSession, 1.0, seed)));
            int oursFast = Median(Enumerable.Range(0, 21).Select(seed => NightSkill(3 * BotCraftingRules.SessionsPerTrip * BotCraftingRules.CraftsPerSession, 5.0, seed)));
            TestContext.WriteLine($"median skill after a night: DR's trips {dr}; ours at bot_xp_rate 1 {ours}, at 5 {oursFast}");
            Assert.That(dr, Is.LessThanOrEqualTo(10), "the rules reproduce DR's overnight median of 8");
            Assert.That(ours, Is.GreaterThanOrEqualTo(40), "ours at the release's bot_xp_rate 1");
            Assert.That(oursFast, Is.GreaterThanOrEqualTo(200), "ours at bot_xp_rate 5: five points a gain");
            Assert.That(oursFast, Is.LessThan(1000), "but no crafter is Legendary after one night");
        }

        [Test]
        public void AnAttemptUsesRealMaterialsAndMakesARealItem()
        {
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15, id: "bronze_sword", unique: true);
            _catalog.Recipes.Add(recipe);
            TestBot bot = Bot("Smith", "1|30;6|30");
            DbItemTemplate bars = recipe.Recipe.Ingredients[0].Material;
            Assert.That(bot.Inventory.AddTemplate(GameInventoryItem.Create(bars), 4, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), Is.True);
            var session = new BotCraftingRules.Session(recipe, eCraftingSkill.WeaponCrafting, 2);

            DbInventoryItem first = AutonomousBotCrafting.Attempt(bot, session, out bool stop, new Random(3));

            Assert.That(stop, Is.False);
            Assert.That(first, Is.Not.Null, "A blue recipe never fails");
            Assert.That(first.IsCrafted, Is.True);
            Assert.That(first.Creator, Is.EqualTo("Smith"));
            Assert.That(first.Quality, Is.InRange(96, 100), "the 1.87 quality roll (CraftRules)");
            Assert.That(Count(bot, bars.Id_nb), Is.EqualTo(2), "Two bars used, two left");

            Assert.That(AutonomousBotCrafting.Attempt(bot, session, out stop, new Random(4)), Is.Not.Null);
            Assert.That(Count(bot, bars.Id_nb), Is.Zero);
            Assert.That(AutonomousBotCrafting.Attempt(bot, session, out stop), Is.Null);
            Assert.That(stop, Is.True, "Out of materials");
        }

        [Test]
        public void AFailedAttemptCostsNothing_AsAPlayersDoes()
        {
            // Skill 1 on a level 40 recipe: red, 68% to make. Find a seed whose first draw fails.
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 40, id: "red_sword");
            TestBot bot = Bot("Smith", "1|1;6|1");
            bot.Inventory.AddTemplate(GameInventoryItem.Create(recipe.Recipe.Ingredients[0].Material), 2, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            int seed = Enumerable.Range(0, 200).First(s => !(68 > new Random(s).Next(100)));
            DbInventoryItem made = AutonomousBotCrafting.Attempt(bot, new BotCraftingRules.Session(recipe, eCraftingSkill.WeaponCrafting, 1), out bool stop, new Random(seed));
            Assert.That(made, Is.Null);
            Assert.That(stop, Is.False);
            Assert.That(Count(bot, recipe.Recipe.Ingredients[0].Material.Id_nb), Is.EqualTo(2), "a failure loses no materials (today's player rule)");
        }

        [Test]
        public void SkillRisesWithWork_AtTheBotPace()
        {
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 35, id: "copper_sword");
            DbItemTemplate bars = recipe.Recipe.Ingredients[0].Material;
            int Points(string pace, double rate)
            {
                CraftingEconomyProperties.BOT_CRAFTING_PACE = pace;
                Properties.BOT_XP_RATE = rate;
                TestBot bot = Bot("Smith", "1|30;6|30");
                var session = new BotCraftingRules.Session(recipe, eCraftingSkill.WeaponCrafting, 1);
                for (int i = 0; i < 40 && AutonomousBotCrafting.SkillValue(bot, eCraftingSkill.WeaponCrafting) == 30; i++)
                {
                    bot.Inventory.AddTemplate(GameInventoryItem.Create(bars), 2, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    DbInventoryItem made = AutonomousBotCrafting.Attempt(bot, session, out _, new Random(i));
                    if (made != null)
                        bot.Inventory.RemoveItem(made);
                }
                int weapon = AutonomousBotCrafting.SkillValue(bot, eCraftingSkill.WeaponCrafting) - 30;
                Assert.That(AutonomousBotCrafting.SkillValue(bot, eCraftingSkill.MetalWorking) - 30, Is.EqualTo(weapon),
                    "Metal bars train metalworking with it, by the same points");
                return weapon;
            }
            Assert.That(Points("classic", 5.0), Is.EqualTo(1), "classic: one point a gain");
            Assert.That(Points("xp", 1.0), Is.EqualTo(1), "xp at bot_xp_rate 1: one point");
            Assert.That(Points("xp", 5.0), Is.EqualTo(5), "xp at bot_xp_rate 5: five points a gain");
            Assert.That(Points("xp", 2.7), Is.EqualTo(2), "whole points only (True165's rule)");
        }

        [Test]
        public void ASecondaryStopsAtItsCap_AndTheEachCraftsGainsAreThePlayers()
        {
            Assert.That(BotCraftingRules.PointsPerGain("xp", 10, 1295, primary: false), Is.EqualTo(5));
            Assert.That(BotCraftingRules.PointsPerGain("xp", 10, 1300, primary: false), Is.Zero);
            Assert.That(BotCraftingRules.PointsPerGain("xp", 10, 1300, primary: true), Is.EqualTo(10));
            Assert.That(BotCraftingRules.PointsPerGain("xp", double.NaN, 5, true), Is.EqualTo(1));
            Assert.That(BotCraftingRules.GainedSkills(eCraftingSkill.ArmorCrafting, [519, 521, 1]),
                Is.EqualTo(new[] { (eCraftingSkill.ArmorCrafting, true), (eCraftingSkill.MetalWorking, false), (eCraftingSkill.LeatherCrafting, false) }));
            Assert.That(BotCraftingRules.GainedSkills(eCraftingSkill.Alchemy, [1]),
                Is.EqualTo(new[] { (eCraftingSkill.Alchemy, true), (eCraftingSkill.HerbalCrafting, false) }));
            Assert.That(BotCraftingRules.GainedSkills(eCraftingSkill.SpellCrafting, []),
                Is.EqualTo(new[] { (eCraftingSkill.SpellCrafting, true), (eCraftingSkill.GemCutting, false) }));
            Assert.That(BotCraftingRules.GainedSkills(eCraftingSkill.MetalWorking, [519]),
                Is.EqualTo(new[] { (eCraftingSkill.MetalWorking, false) }), "a secondary craft raises only itself, capped");
        }

        [Test]
        public void TitlesComeOnTheHundredsUpToLegendary()
        {
            Assert.That(BotCraftingRules.TitlesReached(98, 103), Is.EqualTo(new[] { 100 }));
            Assert.That(BotCraftingRules.TitlesReached(195, 405), Is.EqualTo(new[] { 200, 300, 400 }), "a big gain never steps over a title");
            Assert.That(BotCraftingRules.TitlesReached(995, 1150), Is.EqualTo(new[] { 1000 }), "no title rung past Legendary");
            Assert.That(BotCraftingRules.TitlesReached(1000, 1200), Is.Empty);
            Assert.That(BotCraftingRules.Title(eCraftingSkill.ArmorCrafting, 50), Is.EqualTo("Armorer's Helper"));
            Assert.That(BotCraftingRules.Title(eCraftingSkill.Tailoring, 650), Is.EqualTo("Journeyman Tailor"));
            Assert.That(BotCraftingRules.Title(eCraftingSkill.WeaponCrafting, 1000), Is.EqualTo("Legendary Weaponcrafter"));
            Assert.That(BotCraftingRules.Title(eCraftingSkill.WeaponCrafting, 1150), Is.EqualTo("Legendary Weaponcrafter"), "LGM is post-era");
            Assert.That(BotCraftingRules.LegendaryFirstTitle(eCraftingSkill.Alchemy, eRealm.Midgard), Is.EqualTo("First Legendary Alchemist of Midgard"));
        }

        [Test]
        public void OnlyTheBotsOwnMaterialsCountAsMaterials()
        {
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15);
            _catalog.Recipes.Add(recipe);
            DbInventoryItem bars = GameInventoryItem.Create(recipe.Recipe.Ingredients[0].Material);
            DbInventoryItem potions = GameInventoryItem.Create(Item("potion", 1, 1, 50));

            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Smith", "1|30"), bars), Is.True);
            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Smith", "1|30"), potions), Is.False);
            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Unskilled"), bars), Is.Null, "no craft: today's rule decides");
            RecipeCatalogs.Current = null;
            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Smith", "1|30"), potions), Is.True, "while the catalog loads every stack is kept (DR)");
        }

        [Test]
        public void OwnWorkIsThePieceTheBotMadeItself()
        {
            TestBot smith = Bot("Smith");
            DbInventoryItem sword = GameInventoryItem.Create(Item("sword"));
            sword.IsCrafted = true;
            sword.Creator = "Smith";
            Assert.That(smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, sword), Is.True);

            Assert.That(AutonomousBotCrafting.IsOwnWork(smith, sword), Is.True);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.EqualTo(1));
            Assert.That(AutonomousBotCrafting.IsOwnWork(Bot("Trader"), sword), Is.False, "Bought from another crafter");
            Assert.That(AutonomousBotCrafting.IsOwnWork(smith, GameInventoryItem.Create(Item("found sword"))), Is.False);
        }

        #endregion

        #region True165: only the best of a session; the siege tiers

        [Test]
        public void TheBestOfASessionIsAMasterpieceElseTheHighestQualityGearFirst()
        {
            Assert.That(BotCraftingRules.BestProductIndex([(false, 96), (false, 99), (false, 97)]), Is.EqualTo(1));
            Assert.That(BotCraftingRules.BestProductIndex([(false, 99), (false, 100)]), Is.EqualTo(1));
            Assert.That(BotCraftingRules.BestProductIndex([(true, 100), (false, 96)]), Is.EqualTo(1), "gear before stackables");
            Assert.That(BotCraftingRules.BestProductIndex([]), Is.EqualTo(-1));
            Assert.That(BotCraftingRules.SiegeKitTier(1), Is.EqualTo(BotSiegeKind.Ram));
            Assert.That(BotCraftingRules.SiegeKitTier(300), Is.EqualTo(BotSiegeKind.Catapult));
            Assert.That(BotCraftingRules.SiegeKitTier(650), Is.EqualTo(BotSiegeKind.Trebuchet));
        }

        [Test]
        public void ASessionsSecondsGoToAVendor_ItsBestWaitsForTheExchange()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFTER_CHAT_RATE = 0;
            TestBot smith = Bot("Smith", "1|30;6|30");
            var made = new List<DbInventoryItem>();
            foreach (int quality in new[] { 96, 99, 97 })
            {
                DbInventoryItem piece = GameInventoryItem.Create(new DbItemUnique(Item("sword_" + quality)));
                piece.Quality = quality;
                piece.IsCrafted = true;
                piece.Creator = "Smith";
                smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, piece);
                made.Add(piece);
            }
            CraftRecipeInfo recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15);
            AutonomousBotCrafting.FinishSession(smith, new BotCraftingRules.Session(recipe, eCraftingSkill.WeaponCrafting, 3), made, 3, "test");

            var policy = BotCrafterSeams.ItemPolicy.Instance;
            Assert.That(policy.AlwaysWorthListing(smith, made[1]), Is.True, "the 99% is the session's best");
            Assert.That(policy.BlocksListing(smith, made[1]), Is.False);
            Assert.That(policy.BlocksListing(smith, made[0]), Is.True, "a second is not posted");
            Assert.That(policy.AlwaysWorthListing(smith, made[2]), Is.False);
            Assert.That(policy.KeepFromVendor(smith, made[0]), Is.False, "a second goes to the vendor at the inventory stop");
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.EqualTo(1));
        }

        #endregion

        #region Orders by realm weights

        [Test]
        public void TheRealmWeightsSumToOneAndEveryOrderHasOne()
        {
            Assert.That(BotCraftingRules.Orders.Sum(BotCraftingRules.OrderWeight), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(BotCraftingRules.Orders.All(order => BotCraftingRules.OrderWeight(order) > 0), Is.True);
        }

        [Test]
        public void OrdersFillTheRealmWeights_AndSkipAnOrderWithNoMaster()
        {
            var counts = new Dictionary<eCraftingSkill, int>();
            bool HasMaster(eCraftingSkill order) => order != eCraftingSkill.SiegeCrafting; // M0: no siegecraft master anywhere
            for (int i = 0; i < 200; i++)
            {
                eCraftingSkill order = BotCraftingRules.ChooseOrder(counts, HasMaster, BotCraftingRules.ClassOrder(eCharacterClass.Hero, eClassType.PureTank, i));
                counts[order] = counts.GetValueOrDefault(order) + 1;
            }
            Assert.That(counts.ContainsKey(eCraftingSkill.SiegeCrafting), Is.False);
            double open = BotCraftingRules.Orders.Where(HasMaster).Sum(BotCraftingRules.OrderWeight);
            foreach (eCraftingSkill order in BotCraftingRules.Orders.Where(HasMaster))
                Assert.That(counts.GetValueOrDefault(order), Is.EqualTo(200 * BotCraftingRules.OrderWeight(order) / open).Within(2), order.ToString());
            Assert.That(BotCraftingRules.ChooseOrder(counts, _ => false, eCraftingSkill.Tailoring), Is.EqualTo(eCraftingSkill.NoCrafting));
        }

        [Test]
        public void DRsClassRuleBreaksATie()
        {
            var empty = new Dictionary<eCraftingSkill, int>();
            Assert.That(BotCraftingRules.ChooseOrder(empty, null, eCraftingSkill.Fletching), Is.EqualTo(eCraftingSkill.Fletching),
                "a realm's first crafter: every order is within half a crafter of its share, so the class rule decides");
            var counts = new Dictionary<eCraftingSkill, int>
            {
                [eCraftingSkill.ArmorCrafting] = 10, [eCraftingSkill.Tailoring] = 9, [eCraftingSkill.WeaponCrafting] = 7,
                [eCraftingSkill.SpellCrafting] = 6, [eCraftingSkill.Alchemy] = 5, [eCraftingSkill.Fletching] = 3, [eCraftingSkill.SiegeCrafting] = 1,
            };
            Assert.That(BotCraftingRules.ChooseOrder(counts, null, eCraftingSkill.Fletching), Is.EqualTo(eCraftingSkill.Fletching),
                "fletching is within the window of the largest shortfall: the archer fletches");
            Assert.That(BotCraftingRules.ChooseOrder(counts, null, eCraftingSkill.Alchemy), Is.EqualTo(eCraftingSkill.WeaponCrafting),
                "alchemy is at its share: the order furthest below its share instead");
        }

        [Test]
        public void AnOrderWhoseMasterIsMissingInTheRealmIsSkippedAndLogged()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            TestBot bot = Bot("Newcomer");
            Assert.That(AutonomousBotCrafting.ChooseOrder(bot), Is.EqualTo(eCraftingSkill.NoCrafting),
                "a world index with no masters: no order to join, the trip ends");
        }

        #endregion

        #region Bounded work, the switch, the seams

        [Test]
        public void ATickBudgetStopsAtItsLimitAndRefillsNextTick()
        {
            var budget = new BotCraftingRules.TickBudget(3);
            Assert.That(Enumerable.Range(0, 5).Count(_ => budget.TryTake(100)), Is.EqualTo(3));
            Assert.That(budget.TryTake(101), Is.True);
            int taken = 0;
            System.Threading.Tasks.Parallel.For(0, 64, _ => { if (budget.TryTake(200)) Interlocked.Increment(ref taken); });
            Assert.That(taken, Is.EqualTo(3), "parallel brain turns share one budget");
        }

        [Test]
        public void SwitchedOff_NoBotCrafts_AndTheTripDrawsNoRandomNumber()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            BotCrafterSeams.RegisterForTests();
            TestBot bot = Bot("Anyone", "1|30");
            var random = new CountingRandom();
            Assert.That(AutonomousBotCrafting.IsCrafter(bot), Is.False);
            Assert.That(BotServiceSeams.RollBreakActivities(bot, random, out bool stop, out string text), Is.Empty);
            Assert.That(stop, Is.False);
            Assert.That(random.Draws, Is.Zero);
            Assert.That(BotServiceSeams.WantsVisit(bot), Is.False);
            Assert.That(BotCrafters.Roster.IsCrafter(bot.DatabaseID), Is.False);
            var own = GameInventoryItem.Create(Item("own sword"));
            own.IsCrafted = true;
            own.Creator = bot.Name;
            Assert.That(EconomySeams.AlwaysWorthListing(bot, own), Is.False);
            Assert.That(EconomySeams.KeepFromVendor(bot, own), Is.False);
            Assert.That(EconomySeams.BlocksListing(bot, own), Is.False);
            Assert.That(EconomySeams.ListingPriority(bot, own), Is.Zero);
            BotCrafters.Roster = null;
        }

        [Test]
        public void OnlyACrafterTypeGamebotCrafts_NeverACompanion()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            long crafterId = Enumerable.Range(1, 1000).First(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Crafter);
            long levelerId = Enumerable.Range(1, 1000).First(id => AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Leveler);
            TestBot crafter = Bot("Crafter", id: crafterId);
            Assert.That(AutonomousBotCrafting.IsCrafter(crafter), Is.True);
            Assert.That(AutonomousBotCrafting.IsCrafter(Bot("Leveler", id: levelerId)), Is.False);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper)).SetValue(crafter, true);
            Assert.That(AutonomousBotCrafting.IsCrafter(crafter), Is.False, "a /bot companion never crafts");
            Assert.That(BotCraftingRules.IsCrafterType(BotPersonalityType.Crafter, beggar: true), Is.False, "nor does a beggar");
        }

        [Test]
        public void TheTripIsActivityC_AndAsksForTheInventoryStop()
        {
            BotCrafterSeams.RegisterForTests();
            Assert.That(BotCrafterSeams.TripActivity.Instance.Flag, Is.EqualTo('C'));
            Assert.That(BotCrafterSeams.TripActivity.Instance.ImpliesInventoryStop, Is.True);
            Assert.That(BotCrafters.Roster, Is.SameAs(BotCrafterRoster.Instance));
            BotCrafters.Roster = null;
        }

        #endregion

        #region Enchanting (True165, on SpellcraftRules)

        private static DbInventoryItem Crafted(int level, int quality, eObjectType type = eObjectType.Plate)
        {
            DbItemTemplate template = Item("piece_" + Guid.NewGuid().ToString("N"));
            template.Level = level;
            template.Object_Type = (int)type;
            DbInventoryItem item = GameInventoryItem.Create(new DbItemUnique(template));
            item.Quality = quality;
            item.IsCrafted = true;
            return item;
        }

        [Test]
        public void GemsStayWithinTheItemsImbueAndTheSpellcraftersCap()
        {
            DbInventoryItem item = Crafted(51, 99);
            var gems = new Dictionary<int, BotEnchanting.GemOption[]>();
            foreach (int type in new[] { (int)eProperty.Strength, (int)eProperty.Constitution, (int)eProperty.Dexterity, (int)eProperty.Quickness })
                gems[type] = Enumerable.Range(1, 5).Select(tier => new BotEnchanting.GemOption($"g{type}_{tier}", "gem", type, tier * 3, tier * 100, tier * 1_000)).ToArray();
            int[] wanted = BotEnchanting.WantedBonuses(item, eRealm.Albion, 0);

            List<BotEnchanting.GemOption> picked = BotEnchanting.PickGems(item, wanted, gems, 1000, 1_000_000);

            Assert.That(picked, Is.Not.Empty);
            int imbue = BotEnchanting.TotalImbue(item, picked.Select(g => (g.BonusType, g.BonusValue)));
            Assert.That(imbue, Is.LessThanOrEqualTo(SpellcraftRules.MaxImbuePoints(51, 99)), "no overcharge: nothing explodes");
            Assert.That(imbue, Is.LessThanOrEqualTo(BotEnchanting.SpellcrafterCap(1000)));
            Assert.That(picked.Select(g => g.BonusType).Distinct().Count(), Is.EqualTo(picked.Count));
            Assert.That(BotEnchanting.PickGems(item, wanted, gems, 40, 1_000_000), Is.Empty, "skill 40: cap 2 points and only level 100 gems blocked by con");
            Assert.That(BotEnchanting.PickGems(item, wanted, gems, 1000, 0), Is.Empty, "nothing without coin");
        }

        [Test]
        public void TincturesFollowTheCombineRules()
        {
            DbInventoryItem plate = Crafted(30, 96);
            DbInventoryItem sword = Crafted(30, 96, eObjectType.SlashingWeapon);
            Assert.That(BotEnchanting.TinctureFits(plate, 0), Is.True);
            Assert.That(BotEnchanting.TinctureFits(plate, 1), Is.False);
            Assert.That(BotEnchanting.TinctureFits(sword, 1), Is.True);
            var tinctures = new[]
            {
                new BotEnchanting.TinctureOption("a", "charges", 0, 0, 0, 77, 20, 200, 500),
                new BotEnchanting.TinctureOption("b", "armour proc", 0, 88, 10, 0, 25, 250, 500),
                new BotEnchanting.TinctureOption("c", "too high", 0, 99, 10, 0, 45, 250, 500),
            };
            Assert.That(BotEnchanting.PickTincture(plate, tinctures, 500, 10_000, 0).RecipeId, Is.EqualTo("b"), "a proc beats charges; never above the item's level");
            BotEnchanting.ApplyTincture(plate, tinctures[0]);
            Assert.That(plate.MaxCharges, Is.EqualTo(BotEnchanting.MaxCharges(96)));
            Assert.That(BotEnchanting.MaxCharges(93), Is.EqualTo(2));
            Assert.That(BotEnchanting.MaxCharges(100), Is.EqualTo(10));
            Assert.That(BotEnchanting.TinctureFits(plate, 0), Is.False, "nothing on an item with a charge");
            Assert.That(BotEnchanting.Fee(1_000), Is.EqualTo(100));
            Assert.That(BotEnchanting.EnchantChance(0), Is.EqualTo(15));
            Assert.That(BotEnchanting.EnchantChance(10_000_000), Is.EqualTo(75));
        }

        #endregion

        #region The voice and the station hangout

        [Test]
        public void AnAdvertNamesTheRealPieceItsQualityAndPrice()
        {
            Assert.That(BotCrafterVoice.PriceText(32_500), Is.EqualTo("3g 25s"));
            Assert.That(BotCrafterVoice.PriceText(150_000), Is.EqualTo("15g"));
            Assert.That(BotCrafterVoice.PriceText(4_000), Is.EqualTo("40s"));
            Assert.That(BotCrafterVoice.PriceText(40), Is.EqualTo("40c"));
            for (int roll = 0; roll < 3; roll++)
            {
                string line = BotCrafterVoice.FixedAdvert("bronze long sword", 98, "2g", roll);
                Assert.That(line, Does.Contain("bronze long sword").And.Contain("98").And.Contain("2g"));
            }
        }

        [Test]
        public void AnAdvertNeedsTheStock()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            TestBot smith = Bot("Smith");
            DbInventoryItem sword = GameInventoryItem.Create(Item("sword"));
            sword.IsCrafted = true;
            sword.Creator = "Smith";
            smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, sword);
            Assert.That(BotCrafterVoice.StillHolds(smith, sword), Is.True);
            smith.Inventory.RemoveItem(sword);
            Assert.That(BotCrafterVoice.StillHolds(smith, sword), Is.False, "sold or gone: no advert");
        }

        [Test]
        public void TheStationHangoutIsOnlyForACrafterNearItsStation()
        {
            for (int i = 0; i < 1000; i++)
            {
                double roll = (i + 0.5) / 1000.0;
                Assert.That(AutonomousTownHangouts.Choose(true, true, true, true, false, roll), Is.EqualTo(AutonomousTownHangouts.Choose(true, true, true, true, roll)),
                    "without a station the choice is the old one");
            }
            int stations = Enumerable.Range(0, 1000).Count(i => AutonomousTownHangouts.Choose(true, true, true, true, true, (i + 0.5) / 1000.0) == AutonomousTownHangouts.Kind.Station);
            Assert.That(stations, Is.InRange(400, 550), "about half a crafter's wanders");
            Assert.That(AutonomousTownHangouts.Choose(false, false, false, false, true, 0.3), Is.EqualTo(AutonomousTownHangouts.Kind.Station));
            Assert.That(BotCraftingRules.HangoutStationFor(eCraftingSkill.Fletching), Is.EqualTo(CraftStation.Lathe));
            Assert.That(BotCraftingRules.HangoutStationFor(eCraftingSkill.SiegeCrafting), Is.EqualTo(CraftStation.None));
        }

        #endregion
    }
}
