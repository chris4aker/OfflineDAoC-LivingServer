using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 2026-11-15, crafting M0: RecipeDB.FindBy cached an invalid recipe as null (reloaded and logged on every click) or
    /// as a product-less recipe (KeyNotFoundException on the second click), and with crafting_adjust_product_price on it
    /// called the price update on the null (NullReferenceException on the first click, and the GM "adjustprices" loop
    /// stopped at the first broken row). An invalid recipe is now loaded once, logged once and hidden (null) after.
    /// FindProblem is the server's whole rule for a broken recipe; the audit in docs/CRAFTING-M0-FINDINGS.md applies it.
    /// </summary>
    [TestFixture]
    public class UT_RecipeDB
    {
        private bool _adjustPrice;
        private int _sellback;

        [SetUp]
        public void SetUp()
        {
            _adjustPrice = Properties.CRAFTING_ADJUST_PRODUCT_PRICE;
            _sellback = Properties.CRAFTING_SELLBACK_PERCENT;
            RecipeDB.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = _adjustPrice;
            Properties.CRAFTING_SELLBACK_PERCENT = _sellback;
            RecipeDB.ClearCache();
        }

        private static DbItemTemplate Template(string id, string name, long price = 10, int objectType = (int)eObjectType.Plate, int itemType = (int)eInventorySlot.TorsoArmor) =>
            new() { Id_nb = id, Name = name, Price = price, Object_Type = objectType, Item_Type = itemType, PackageID = null };

        private static Recipe Valid(long productPrice)
        {
            var bars = Template("bars", "bronze metal bars", 10, 0, 40);
            var product = Template("hauberk", "bronze hauberk", productPrice);
            return new Recipe(product, new List<Ingredient> { new(5, bars) }, eCraftingSkill.ArmorCrafting, 15, false);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AnInvalidRecipeIsLoadedOnceAndHiddenAfter(bool adjustPrice)
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = adjustPrice;
            int loads = 0;
            Func<ushort, Recipe> load = _ => { loads++; return null; };

            Assert.Multiple(() =>
            {
                Assert.That(RecipeDB.FindBy(60001, load), Is.Null);
                Assert.That(RecipeDB.FindBy(60001, load), Is.Null);
                Assert.That(RecipeDB.FindBy(60001, load), Is.Null);
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(RecipeDB.InvalidRecipeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void ALoaderExceptionHidesTheRecipeInsteadOfThrowingOnTheNextClick()
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = true;
            int loads = 0;
            Func<ushort, Recipe> load = _ => { loads++; throw new InvalidOperationException("broken row"); };

            Assert.Multiple(() =>
            {
                Assert.That(() => RecipeDB.FindBy(60002, load), Throws.Nothing);
                Assert.That(RecipeDB.FindBy(60002, load), Is.Null);
                Assert.That(loads, Is.EqualTo(1));
            });
        }

        [Test]
        public void AProductlessRecipeIsInvalidWithThePriceSettingOn()
        {
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = true;
            Assert.That(RecipeDB.FindBy(60003, _ => new Recipe(null, new List<Ingredient>())), Is.Null);
        }

        [Test]
        public void AValidRecipeIsCachedAndAPriceAlreadyRightNeedsNoDatabase()
        {
            // 5 bars at 10 = 50; 50 * 2 * 95% = 95, so a product priced 95 is left alone and nothing is saved.
            Properties.CRAFTING_ADJUST_PRODUCT_PRICE = true;
            Properties.CRAFTING_SELLBACK_PERCENT = 95;
            int loads = 0;
            var recipe = Valid(95);
            Func<ushort, Recipe> load = _ => { loads++; return recipe; };

            Assert.Multiple(() =>
            {
                Assert.That(RecipeDB.FindBy(60004, load), Is.SameAs(recipe));
                Assert.That(RecipeDB.FindBy(60004, load), Is.SameAs(recipe));
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(RecipeDB.InvalidRecipeCount, Is.Zero);
            });
        }

        [Test]
        public void APriceCorrectionWithANullPackageIdDoesNotThrow()
        {
            Properties.CRAFTING_SELLBACK_PERCENT = 95;
            var recipe = Valid(95); // PackageID null, price already right: the PackageID check used to throw first
            Assert.That(() => recipe.SetRecommendedProductPriceInDB(), Throws.Nothing);
        }

        private static eRecipeProblem Problem(DbItemTemplate product, int skill, params (string, int, DbItemTemplate)[] ingredients) =>
            RecipeDB.FindProblem(product, skill, ingredients, out _);

        [Test]
        public void FindProblemNamesEachWayARecipeBreaks()
        {
            var bars = Template("bars", "bronze metal bars", 10, 0, 40);
            var leather = Template("squares", "leather square", 10, 0, 40);
            var hauberk = Template("hauberk", "bronze hauberk");
            const int armor = (int)eCraftingSkill.ArmorCrafting;

            Assert.Multiple(() =>
            {
                Assert.That(Problem(hauberk, armor, ("bars", 5, bars), ("squares", 2, leather)), Is.EqualTo(eRecipeProblem.None));
                Assert.That(Problem(null, armor, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.ProductMissing));
                Assert.That(Problem(hauberk, armor), Is.EqualTo(eRecipeProblem.NoIngredients));
                Assert.That(Problem(hauberk, armor, ("bars", 5, bars), ("gone", 1, null)), Is.EqualTo(eRecipeProblem.IngredientMissing));
                Assert.That(Problem(hauberk, armor, ("bars", 0, bars)), Is.EqualTo(eRecipeProblem.IngredientCount));
                Assert.That(Problem(hauberk, armor, ("bars", -3, bars)), Is.EqualTo(eRecipeProblem.IngredientCount));
                Assert.That(Problem(hauberk, armor, ("bars", 5, bars), ("bars", 2, bars)), Is.EqualTo(eRecipeProblem.DuplicateIngredientName));
                Assert.That(Problem(hauberk, 0, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.SkillType));
                Assert.That(Problem(hauberk, 5, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.SkillType));
                Assert.That(Problem(hauberk, 16, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.SkillType));
            });
        }

        [Test]
        public void AGenericObjectInAnArmorOrWeaponSlotIsUnusableButMaterialsAndDyesAreFine()
        {
            var bars = Template("bars", "bronze metal bars", 10, 0, 40);
            const int armor = (int)eCraftingSkill.ArmorCrafting;
            // The 0.37 world has 38 of these: "adamantium svarkedja hauberk" with Object_Type 0, AF 0 and ABS 0.
            var shell = Template("shell", "adamantium svarkedja hauberk", 10, (int)eObjectType.GenericItem, (int)eInventorySlot.TorsoArmor);
            var sword = Template("sword", "fine alloy dwarven great sword", 10, (int)eObjectType.GenericItem, (int)eInventorySlot.TwoHandWeapon);
            var dye = Template("dye", "blue cloth dye", 10, (int)eObjectType.GenericItem, 40);
            var cloak = Template("cloak", "woolen cloak", 10, (int)eObjectType.GenericItem, (int)eInventorySlot.Cloak);

            Assert.Multiple(() =>
            {
                Assert.That(Problem(shell, armor, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.ProductUnusable));
                Assert.That(Problem(sword, 1, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.ProductUnusable));
                Assert.That(Problem(dye, (int)eCraftingSkill.Alchemy, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.None));
                Assert.That(Problem(cloak, (int)eCraftingSkill.Tailoring, ("bars", 5, bars)), Is.EqualTo(eRecipeProblem.None));
            });
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [TestCase(5, false)]   // unused in eCraftingSkill
        [TestCase(15, true)]
        [TestCase(16, false)]
        [TestCase(-1, false)]
        public void OnlyRealCraftingSkillTypesAreAccepted(int value, bool accepted) =>
            Assert.That(RecipeDB.IsCraftingSkillType(value), Is.EqualTo(accepted));

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(16)]
        [TestCase(-3)]
        [TestCase(200)]
        public void ASkillValueOutsideTheTableIsNoSkill(int value) =>
            Assert.That(CraftingMgr.getSkillbyEnum((eCraftingSkill)value), Is.Null);
    }
}
