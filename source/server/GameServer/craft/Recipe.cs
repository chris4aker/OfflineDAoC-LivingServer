using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    public class Recipe
    {
        protected static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public readonly Lock Lock = new();

        private Ingredient[] ingredients;

        public DbItemTemplate Product { get; }
        public eCraftingSkill RequiredCraftingSkill { get; }
        public int Level { get; }
        public List<Ingredient> Ingredients => new List<Ingredient>(ingredients);
        public bool IsForUniqueProduct { get; } = false;

        public Recipe(DbItemTemplate product, List<Ingredient> ingredients)
        {
            this.ingredients = ingredients.ToArray();
            Product = product;
        }

        public Recipe(DbItemTemplate product, List<Ingredient> ingredients, eCraftingSkill requiredSkill, int level, bool makeTemplated)
            : this(product, ingredients)
        {
            RequiredCraftingSkill = requiredSkill;
            Level = level;
            IsForUniqueProduct = !makeTemplated;
        }

        public long CostToCraft
        {
            get
            {
                long result = 0;
                foreach (var ingredient in ingredients)
                {
                    result += ingredient.Cost;
                }
                return result;
            }
        }

        public void SetRecommendedProductPriceInDB()
        {
            var product = Product;
            var totalPrice = CostToCraft;
            bool updatePrice = !(product.Name.EndsWith("metal bars") ||
                                 product.Name.EndsWith("leather square") ||
                                 product.Name.EndsWith("cloth square") ||
                                 product.Name.EndsWith("wooden boards"));

            if (product.PackageID?.Contains("NoPriceUpdate") == true) // PackageID is nullable (11 recipe products in the 0.37 world)
                updatePrice = false;

            if (updatePrice)
            {
                long pricetoset;
                var secondaryCraftingSkills = new List<eCraftingSkill>() { 
                    eCraftingSkill.MetalWorking, eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking, eCraftingSkill.WoodWorking
                };

                if (secondaryCraftingSkills.Contains(RequiredCraftingSkill))
                    pricetoset = Math.Abs((long)(totalPrice * 2 * Properties.CRAFTING_SECONDARYCRAFT_SELLBACK_PERCENT) / 100);
                else
                    pricetoset = Math.Abs(totalPrice * 2 * Properties.CRAFTING_SELLBACK_PERCENT / 100);

                if (pricetoset > 0 && product.Price != pricetoset)
                {
                    long currentPrice = product.Price;
                    product.Price = pricetoset;
                    product.AllowUpdate = true;
                    product.Dirty = true;
                    product.Id_nb = product.Id_nb.ToLower();
                    if (GameServer.Database.SaveObject(product))
                        log.Warn("Craft Price Correction: " + product.Id_nb + " rawmaterials price= " + totalPrice + " Current Price= " + currentPrice + ". Corrected price to= " + pricetoset);
                    else
                        log.Warn("Craft Price Correction Not SAVED: " + product.Id_nb + " rawmaterials price= " + totalPrice + " Current Price= " + currentPrice + ". Corrected price to= " + pricetoset);
                    GameServer.Database.UpdateInCache<DbItemTemplate>(product.Id_nb);
                    product.Dirty = false;
                    product.AllowUpdate = false;
                }
            }
        }
    }

    public class Ingredient
    {
        public int Count { get; }
        public DbItemTemplate Material { get; }

        public Ingredient(int count, DbItemTemplate ingredient)
        {
            Count = count;
            Material = ingredient;
        }

        public long Cost => Count * Material.Price;
    }

    /// <summary>
    /// Why the server will not let a recipe be crafted. 2026-11-15 (crafting M0 audit): every reason is a way a
    /// CraftedItem row would fail or misbehave further down the craft (see docs/CRAFTING-M0-FINDINGS.md).
    /// </summary>
    public enum eRecipeProblem
    {
        None = 0,
        /// <summary>The CraftedItem row is missing.</summary>
        RecipeMissing,
        /// <summary>No ItemTemplate for the product's Id_nb (lookups ignore case).</summary>
        ProductMissing,
        /// <summary>No CraftedXItem rows for the product.</summary>
        NoIngredients,
        /// <summary>An ingredient's ItemTemplate is missing.</summary>
        IngredientMissing,
        /// <summary>An ingredient count of 0 or less: free materials, and a negative count wraps the craft time.</summary>
        IngredientCount,
        /// <summary>Two ingredients share a name: RemoveUsedMaterials would add the same bag slot twice and throw.</summary>
        DuplicateIngredientName,
        /// <summary>CraftingSkillType names no crafting skill: getSkillbyEnum gives null or reads past its array.</summary>
        SkillType,
        /// <summary>A weapon or armor slot product with the generic object type: a statless shell nobody can repair.</summary>
        ProductUnusable,
    }

    public class RecipeDB
    {
        protected static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        // 2026-11-15: valid recipes are cached; an invalid one is remembered as invalid, logged once and returned as null
        // every time (it used to be cached as null and reloaded and logged on every click, or cached as a product-less
        // recipe that threw KeyNotFoundException on the second click, and crafting_adjust_product_price called the price
        // update on the null and threw on the first).
        private static readonly ConcurrentDictionary<ushort, Recipe> recipeCache = new();
        private static readonly ConcurrentDictionary<ushort, eRecipeProblem> invalidRecipes = new();

        /// <summary>The recipe with this client recipe id, or null if the server cannot make it (logged once).</summary>
        public static Recipe FindBy(ushort recipeDatabaseID) => FindBy(recipeDatabaseID, LoadFromDB);

        /// <summary>FindBy with the loader given, so the caching rules can be tested without a database.</summary>
        public static Recipe FindBy(ushort recipeDatabaseID, Func<ushort, Recipe> load)
        {
            if (recipeCache.TryGetValue(recipeDatabaseID, out Recipe cached))
                return cached;

            if (invalidRecipes.ContainsKey(recipeDatabaseID))
                return null;

            Recipe recipe = null;

            try
            {
                recipe = load(recipeDatabaseID);
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"[CRAFTING] Recipe {recipeDatabaseID} could not be loaded; it is skipped until the server restarts.", e);
            }

            if (recipe?.Product == null)
            {
                invalidRecipes.TryAdd(recipeDatabaseID, eRecipeProblem.RecipeMissing);
                return null;
            }

            if (Properties.CRAFTING_ADJUST_PRODUCT_PRICE)
            {
                try
                {
                    recipe.SetRecommendedProductPriceInDB();
                }
                catch (Exception e)
                {
                    if (log.IsErrorEnabled)
                        log.Error($"[CRAFTING] Price correction failed for recipe {recipeDatabaseID} ({recipe.Product.Id_nb}).", e);
                }
            }

            recipeCache[recipeDatabaseID] = recipe;
            return recipe;
        }

        /// <summary>How many recipes this run has found invalid (for the GM command and tests).</summary>
        public static int InvalidRecipeCount => invalidRecipes.Count;

        /// <summary>Forgets every cached recipe, valid or not (a GM reload or a test).</summary>
        public static void ClearCache()
        {
            recipeCache.Clear();
            invalidRecipes.Clear();
        }

        /// <summary>
        /// The first reason a recipe cannot be crafted, in the order the loader meets them, or None. Pure, so the audit
        /// and the tests apply exactly the server's rule. A missing ingredient template is passed as a null template.
        /// </summary>
        public static eRecipeProblem FindProblem(DbItemTemplate product, int craftingSkillType, IReadOnlyList<(string Id, int Count, DbItemTemplate Template)> ingredients, out string detail)
        {
            detail = string.Empty;

            if (product == null)
                return eRecipeProblem.ProductMissing;

            if (ingredients == null || ingredients.Count == 0)
                return eRecipeProblem.NoIngredients;

            var names = new HashSet<string>(StringComparer.Ordinal); // CheckRawMaterials matches bag items by Name, ordinal

            foreach (var ingredient in ingredients)
            {
                if (ingredient.Template == null)
                {
                    detail = ingredient.Id;
                    return eRecipeProblem.IngredientMissing;
                }

                if (ingredient.Count <= 0)
                {
                    detail = $"{ingredient.Id} x{ingredient.Count}";
                    return eRecipeProblem.IngredientCount;
                }

                if (!names.Add(ingredient.Template.Name ?? string.Empty))
                {
                    detail = ingredient.Template.Name;
                    return eRecipeProblem.DuplicateIngredientName;
                }
            }

            if (!IsCraftingSkillType(craftingSkillType))
            {
                detail = craftingSkillType.ToString();
                return eRecipeProblem.SkillType;
            }

            if (product.Object_Type == (int)eObjectType.GenericItem && IsWeaponOrArmorSlot(product.Item_Type))
            {
                detail = $"item type {product.Item_Type}";
                return eRecipeProblem.ProductUnusable;
            }

            return eRecipeProblem.None;
        }

        /// <summary>A value CraftingMgr.getSkillbyEnum maps to a crafting skill (1-15 without the unused 5).</summary>
        public static bool IsCraftingSkillType(int craftingSkillType) =>
            craftingSkillType >= (int)eCraftingSkill.WeaponCrafting && craftingSkillType <= (int)eCraftingSkill._Last && craftingSkillType != 5;

        private static bool IsWeaponOrArmorSlot(int itemType) =>
            (itemType >= (int)eInventorySlot.RightHandWeapon && itemType <= (int)eInventorySlot.DistanceWeapon) ||
            (itemType >= (int)eInventorySlot.HeadArmor && itemType <= (int)eInventorySlot.ArmsArmor && itemType != (int)eInventorySlot.Jewelry && itemType != (int)eInventorySlot.Cloak);

        private static Recipe LoadFromDB(ushort recipeDatabaseID)
        {
            var dbRecipe = GameServer.Database.FindObjectByKey<DbCraftedItem>(recipeDatabaseID.ToString());
            if (dbRecipe == null)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"[CRAFTING] No CraftedItem row with ID {recipeDatabaseID}; the recipe is hidden.");
                return null;
            }

            DbItemTemplate product = GameServer.Database.FindObjectByKey<DbItemTemplate>(dbRecipe.Id_nb);
            var rawMaterials = product == null
                ? new List<DbCraftedXItem>()
                : DOLDB<DbCraftedXItem>.SelectObjects(DB.Column("CraftedItemId_nb").IsEqualTo(dbRecipe.Id_nb));

            var lookedUp = new List<(string Id, int Count, DbItemTemplate Template)>(rawMaterials.Count);
            foreach (DbCraftedXItem material in rawMaterials)
                lookedUp.Add((material.IngredientId_nb, material.Count, GameServer.Database.FindObjectByKey<DbItemTemplate>(material.IngredientId_nb)));

            eRecipeProblem problem = FindProblem(product, dbRecipe.CraftingSkillType, lookedUp, out string detail);
            if (problem != eRecipeProblem.None)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"[CRAFTING] Recipe {dbRecipe.CraftedItemID} ({dbRecipe.Id_nb}) is hidden: {problem}{(detail.Length > 0 ? " " + detail : string.Empty)}.");
                return null;
            }

            var ingredients = new List<Ingredient>(lookedUp.Count);
            foreach (var material in lookedUp)
                ingredients.Add(new Ingredient(material.Count, material.Template));

            bool makeTemplated = dbRecipe.MakeTemplated;

            if (!makeTemplated && product.IsStackable)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"Recipe '{dbRecipe.Id_nb}' produces stackable product '{product.Name}' but MakeTemplated is false. Forcing to true to prevent inventory issues.");

                makeTemplated = true;
            }

            var recipe = new Recipe(product, ingredients, (eCraftingSkill)dbRecipe.CraftingSkillType, dbRecipe.CraftingLevel, makeTemplated);
            return recipe;
        }
    }
}
