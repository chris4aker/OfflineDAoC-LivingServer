using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP1: RecipeCatalog (ported from DividedRealms' BuildCatalog, 93c25b69) loads every recipe at once,
    /// matching ids without case as the server does (the M0 audit: 2 broken recipes ignoring case, 1,608 counting case),
    /// and hides a recipe by the server's own rule (RecipeDB.FindProblem), so a bot's plan and a player's click agree.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RecipeCatalog
    {
        private bool _adjustPrice;
        private bool _allowNoRealm;

        [SetUp]
        public void SetUp()
        {
            _adjustPrice = Properties.CRAFTING_ADJUST_PRODUCT_PRICE;
            _allowNoRealm = Properties.ALLOW_CRAFT_NOREALM_ITEMS;
            Properties.ALLOW_CRAFT_NOREALM_ITEMS = false;
            _templates.Clear();
            _rows.Clear();
            _parts.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = _adjustPrice;
            Properties.ALLOW_CRAFT_NOREALM_ITEMS = _allowNoRealm;
            RecipeCatalogs.Current = null;
            RecipeCatalogs.World = null;
        }

        // ---- a small world: templates keyed in lower case, as the server's ItemTemplate cache is ----

        private readonly Dictionary<string, DbItemTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DbCraftedItem> _rows = new();
        private readonly List<DbCraftedXItem> _parts = new();

        private DbItemTemplate Template(string id, string name, int objectType = (int)eObjectType.Plate, int itemType = (int)eInventorySlot.TorsoArmor,
            int level = 10, int realm = 1, int model = 0, long price = 10, int maxCount = 1)
        {
            var template = new DbItemTemplate
            {
                Id_nb = id, Name = name, Object_Type = objectType, Item_Type = itemType, Level = level, Realm = realm,
                Model = model, Price = price, MaxCount = maxCount, PackSize = 1, PackageID = null,
            };
            _templates[id] = template;
            return template;
        }

        private void Row(string recipeId, string productId, eCraftingSkill skill, int level, bool makeTemplated = false) =>
            _rows.Add(new DbCraftedItem { CraftedItemID = recipeId, Id_nb = productId, CraftingSkillType = (int)skill, CraftingLevel = level, MakeTemplated = makeTemplated });

        private void Part(string productId, string ingredientId, int count) =>
            _parts.Add(new DbCraftedXItem { CraftedItemId_nb = productId, IngredientId_nb = ingredientId, Count = count });

        private RecipeCatalog Build() => RecipeCatalog.Build(_rows, _parts, id => _templates.TryGetValue(id, out DbItemTemplate t) ? t : null);

        private void StandardWorld()
        {
            Template("bronze_metal_bars", "bronze metal bars", 0, 40, model: 519, price: 30, maxCount: 99);
            Template("leather_square", "leather square", 0, 40, model: 521, price: 20, maxCount: 99);
            Template("bronze_hauberk", "bronze hauberk", level: 5);
            Template("iron_hauberk", "iron hauberk", level: 15);
            Template("mid_hauberk", "midgard hauberk", level: 12, realm: 2);
            Template("norealm_hauberk", "plain hauberk", level: 10, realm: 0);

            // Case differs between the tables, as in 1,606 rows of the 0.35 world: the server still finds them.
            Row("101", "Bronze_Hauberk", eCraftingSkill.ArmorCrafting, 15);
            Part("BRONZE_HAUBERK", "Bronze_Metal_Bars", 12);
            Part("bronze_hauberk", "LEATHER_SQUARE", 4);

            Row("102", "iron_hauberk", eCraftingSkill.ArmorCrafting, 215);
            Part("iron_hauberk", "bronze_metal_bars", 20);

            Row("7001", "mid_hauberk", eCraftingSkill.ArmorCrafting, 115);
            Part("mid_hauberk", "bronze_metal_bars", 15);

            Row("103", "norealm_hauberk", eCraftingSkill.ArmorCrafting, 100);
            Part("norealm_hauberk", "bronze_metal_bars", 10);
        }

        [Test]
        public void IdsMatchWithoutCase_AsTheServerMatchesThem()
        {
            StandardWorld();
            RecipeCatalog catalog = Build();
            CraftRecipeInfo hauberk = catalog.Get("101");

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Broken, Is.Empty);
                Assert.That(hauberk, Is.Not.Null);
                Assert.That(hauberk.ProductId, Is.EqualTo("bronze_hauberk"), "the template's own id");
                Assert.That(hauberk.Materials.Select(m => m.TemplateId), Is.EquivalentTo(new[] { "bronze_metal_bars", "leather_square" }));
                Assert.That(hauberk.MaterialCostCopper, Is.EqualTo(12 * 30 + 4 * 20));
                Assert.That(hauberk.Recipe, Is.Not.Null);
                Assert.That(hauberk.Recipe.Ingredients.Count, Is.EqualTo(2));
                Assert.That(hauberk.Recipe.RequiredCraftingSkill, Is.EqualTo(eCraftingSkill.ArmorCrafting));
                Assert.That(hauberk.Unique, Is.True);
                Assert.That(catalog.Materials, Is.EquivalentTo(new[] { "bronze_metal_bars", "leather_square" }));
            });
        }

        [Test]
        public void BrokenRecipesAreHiddenByTheServersOwnRule()
        {
            Template("bars", "bars", 0, 40, model: 519, maxCount: 99);
            Template("bars_again", "bars", 0, 40, model: 519, maxCount: 99);   // the same name as "bars"
            Template("good", "good hauberk");
            Template("lonely", "lonely hauberk");
            Template("orphan", "orphan hauberk");
            Template("shell", "statless shell", (int)eObjectType.GenericItem, (int)eInventorySlot.TorsoArmor);
            Template("twins", "twin hauberk");
            Template("zero", "zero hauberk");
            Template("badskill", "bad skill hauberk");

            Row("1", "good", eCraftingSkill.ArmorCrafting, 1); Part("good", "bars", 1);
            Row("2", "missing_product", eCraftingSkill.ArmorCrafting, 1); Part("missing_product", "bars", 1);
            Row("3", "lonely", eCraftingSkill.ArmorCrafting, 1);                                    // no ingredients
            Row("4", "orphan", eCraftingSkill.ArmorCrafting, 1); Part("orphan", "no_such_bars", 1);
            Row("5", "shell", eCraftingSkill.ArmorCrafting, 1); Part("shell", "bars", 1);
            Row("6", "twins", eCraftingSkill.ArmorCrafting, 1); Part("twins", "bars", 1); Part("twins", "bars_again", 1);
            Row("7", "zero", eCraftingSkill.ArmorCrafting, 1); Part("zero", "bars", 0);
            Row("8", "badskill", (eCraftingSkill)5, 1); Part("badskill", "bars", 1);
            Row("70000", "good", eCraftingSkill.ArmorCrafting, 1);                                  // not a client id

            RecipeCatalog catalog = Build();
            Dictionary<string, eRecipeProblem> problems = catalog.Broken.ToDictionary(b => b.RecipeId, b => b.Problem);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.RowCount, Is.EqualTo(9));
                Assert.That(catalog.All.Select(r => r.RecipeId), Is.EquivalentTo(new[] { "1" }));
                Assert.That(problems["2"], Is.EqualTo(eRecipeProblem.ProductMissing));
                Assert.That(problems["3"], Is.EqualTo(eRecipeProblem.NoIngredients));
                Assert.That(problems["4"], Is.EqualTo(eRecipeProblem.IngredientMissing));
                Assert.That(problems["5"], Is.EqualTo(eRecipeProblem.ProductUnusable));
                Assert.That(problems["6"], Is.EqualTo(eRecipeProblem.DuplicateIngredientName));
                Assert.That(problems["7"], Is.EqualTo(eRecipeProblem.IngredientCount));
                Assert.That(problems["8"], Is.EqualTo(eRecipeProblem.SkillType));
                Assert.That(problems["70000"], Is.EqualTo(eRecipeProblem.RecipeIdInvalid));
                Assert.That(catalog.Get("4"), Is.Null);
            });
        }

        [Test]
        public void ForSkillFindAndBestFor()
        {
            StandardWorld();
            RecipeCatalog catalog = Build();

            Assert.Multiple(() =>
            {
                Assert.That(catalog.ForSkill(eCraftingSkill.ArmorCrafting).Select(r => r.Level), Is.Ordered, "by level");
                Assert.That(catalog.ForSkill(eCraftingSkill.Tailoring), Is.Empty);
                Assert.That(catalog.Find(eCraftingSkill.ArmorCrafting, 0, 1000, eRealm.Albion).Select(r => r.RecipeId),
                    Is.EquivalentTo(new[] { "101", "102" }), "Albion's own; no-realm products only with allow_craft_norealm_items");
                Assert.That(catalog.Find(eCraftingSkill.ArmorCrafting, 100, 200, eRealm.Midgard).Select(r => r.RecipeId), Is.EquivalentTo(new[] { "7001" }));
                Assert.That(catalog.Find(eCraftingSkill.ArmorCrafting, 0, 1000, eRealm.None).Count(), Is.EqualTo(4), "None asks for every realm");
                Assert.That(catalog.BestFor((int)eObjectType.Plate, (int)eInventorySlot.TorsoArmor, 13, eRealm.Albion).RecipeId, Is.EqualTo("102"));
                Assert.That(catalog.BestFor((int)eObjectType.Plate, (int)eInventorySlot.TorsoArmor, 10, eRealm.Albion).RecipeId, Is.EqualTo("101"), "a tie goes to the lower level");
                Assert.That(catalog.BestFor((int)eObjectType.Cloth, (int)eInventorySlot.TorsoArmor, 10, eRealm.Albion), Is.Null);
                Assert.That(catalog.Get(" 101 "), Is.Not.Null);
                Assert.That(catalog.Get(null), Is.Null);
            });

            Properties.ALLOW_CRAFT_NOREALM_ITEMS = true;
            Assert.That(catalog.Find(eCraftingSkill.ArmorCrafting, 0, 1000, eRealm.Albion).Select(r => r.RecipeId), Is.EquivalentTo(new[] { "101", "102", "103" }));
        }

        [Test]
        public void AStackableProductIsMadeTemplated_AsTheLoaderForcesIt()
        {
            Template("ore", "ore", 0, 40, model: 1, maxCount: 99);
            Template("bars", "bars", 0, 40, model: 519, maxCount: 99);
            Row("1", "bars", eCraftingSkill.MetalWorking, 1, makeTemplated: false);
            Part("bars", "ore", 2);

            CraftRecipeInfo bars = Build().Get("1");
            Assert.That(bars.Unique, Is.False);
            Assert.That(bars.Stackable, Is.True);
            Assert.That(bars.Recipe.IsForUniqueProduct, Is.False);
        }

        [Test]
        public void BuildingNeverPricesOrWrites_EvenWithAdjustPriceOn()
        {
            // crafting_adjust_product_price threw on a null recipe before M0; the catalog never prices at all (RecipeDB does,
            // on a player's click), so turning the setting on cannot make the start-up load fail.
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = true;
            StandardWorld();
            long before = _templates["bronze_hauberk"].Price;
            RecipeCatalog catalog = null;
            Assert.DoesNotThrow(() => catalog = Build());
            Assert.That(_templates["bronze_hauberk"].Price, Is.EqualTo(before));
            Assert.That(catalog.All.Count(), Is.EqualTo(4));
        }

        [Test]
        public void StationsAreKnownByModelOrName()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RecipeCatalog.StationOf(478, "anything"), Is.EqualTo(CraftStation.Forge));
                Assert.That(RecipeCatalog.StationOf(1, "Forge"), Is.EqualTo(CraftStation.Forge));
                Assert.That(RecipeCatalog.StationOf(481, null), Is.EqualTo(CraftStation.Lathe));
                Assert.That(RecipeCatalog.StationOf(820, null), Is.EqualTo(CraftStation.AlchemyTable));
                Assert.That(RecipeCatalog.StationOf(2, "Alchemy Table"), Is.EqualTo(CraftStation.AlchemyTable));
                Assert.That(RecipeCatalog.StationOf(2, "barrel"), Is.EqualTo(CraftStation.None));
            });
        }

        [Test]
        public void AuditLinesUseTheEconomyLogFormat()
        {
            StandardWorld();
            Row("3", "lonely", eCraftingSkill.Alchemy, 659); // product missing
            RecipeCatalog catalog = Build();

            string[] audit = catalog.RecipeAuditLines().ToArray();
            string[] broken = catalog.BrokenRecipeLines().ToArray();
            string[] world = catalog.WorldAuditLines().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(audit, Does.Contain("CRAFT_RECIPE_AUDIT skill=ArmorCrafting recipes=4 valid=4 hidden=0"));
                Assert.That(audit, Does.Contain("CRAFT_RECIPE_AUDIT skill=Alchemy recipes=1 valid=0 hidden=1"));
                Assert.That(audit.Last(), Is.EqualTo("CRAFT_RECIPE_AUDIT skill=\"all\" recipes=5 valid=4 hidden=1 albion=2 midgard=1 hibernia=0 norealm=1 materials=2 match=\"nocase\""));
                Assert.That(broken, Is.EqualTo(new[] { "CRAFT_RECIPE_BROKEN recipe=\"3\" product=\"lonely\" skill=Alchemy level=659 problem=ProductMissing detail=\"\"" }));
                Assert.That(world, Has.Length.EqualTo(4));
                Assert.That(world[0], Does.StartWith("CRAFT_WORLD_AUDIT realm=\"Albion\" masters=0"));
                Assert.That(world[3], Is.EqualTo("CRAFT_WORLD_AUDIT realm=\"all\" masters=0 forges=0 lathes=0 tables=0 onSale=0 materials=2"));
                Assert.That(catalog.DividedRealmsCatalogLine(), Is.EqualTo("AUTONOMOUS_CRAFT_CATALOG recipes=4 materials=2 onSale=0 masters=0 forges=0 lathes=0 tables=0"));
            });
        }

        [Test]
        public void TheCatalogRegistersAndIsSafeToReadInParallel()
        {
            StandardWorld();
            RecipeCatalog catalog = Build();
            RecipeCatalogs.Current = catalog;
            RecipeCatalogs.World = catalog;

            Assert.That(RecipeCatalogs.Current.IsLoaded, Is.True);
            Assert.That(RecipeCatalogs.World.IsLoaded, Is.False, "the world index loads with the world, not in a recipes-only build");
            int found = 0;
            Parallel.For(0, 2000, i =>
            {
                if (RecipeCatalogs.Current.Get("101") != null && RecipeCatalogs.Current.Find(eCraftingSkill.ArmorCrafting, 0, 300, eRealm.Albion).Any())
                    System.Threading.Interlocked.Increment(ref found);
            });
            Assert.That(found, Is.EqualTo(2000));

            RecipeCatalogs.Current = null;
            Assert.That(RecipeCatalogs.Current.IsLoaded, Is.False, "null puts the empty catalog back");
        }
    }
}
