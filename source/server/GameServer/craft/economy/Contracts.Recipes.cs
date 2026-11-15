using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Crafting
{
    /// <summary>The kind of station a recipe needs (each craft's CheckForTools): forge model 478, lathe 481, alchemy table 820.</summary>
    public enum CraftStation { None, Forge, Lathe, AlchemyTable }

    /// <summary>One ingredient of a recipe, as pure data.</summary>
    public readonly record struct CraftMaterial(string TemplateId, string Name, int Model, int Count, long UnitPriceCopper, int PackSize)
    {
        /// <summary>The secondary skill the material is worked with (CraftRules.SecondarySkillForMaterial).</summary>
        public eCraftingSkill SecondarySkill => CraftRules.SecondarySkillForMaterial(Model);
    }

    /// <summary>
    /// A valid recipe as pure data, for planners and the rehearsal. <see cref="Recipe"/> is the server's own object when
    /// the catalog was built from the world (DividedRealms' code works on it), null in the rehearsal.
    /// </summary>
    public sealed record CraftRecipeInfo(string RecipeId, eCraftingSkill Skill, int Level, string ProductId, string ProductName,
        int ObjectType, int ItemType, int ProductLevel, int Realm, bool Unique, bool Stackable, IReadOnlyList<CraftMaterial> Materials)
    {
        public Recipe Recipe { get; init; }

        /// <summary>The materials' merchant price (Recipe.CostToCraft).</summary>
        public long MaterialCostCopper => Materials?.Sum(material => material.Count * material.UnitPriceCopper) ?? 0;

        /// <summary>The ingredient counts in order, for CraftRules.CraftingTimeSeconds.</summary>
        public int[] IngredientCounts => Materials?.Select(material => material.Count).ToArray() ?? Array.Empty<int>();

        /// <summary>The secondary-skill level the recipe asks for (CraftRules.SecondaryMinimumLevel).</summary>
        public int SecondaryMinimum => CraftRules.SecondaryMinimumLevel(Skill, ObjectType, Level);
    }

    /// <summary>
    /// Every valid recipe of the world, indexed (WP1 implements it as RecipeCatalog, built once in the background with
    /// case-insensitive key matching, ported from DividedRealms' AutonomousBotCrafting.BuildCatalog, 93c25b69; the
    /// survey found the "19% broken" recipes were a case-sensitive audit). Thread-safe for readers once loaded.
    /// </summary>
    public interface IRecipeCatalog
    {
        bool IsLoaded { get; }
        IReadOnlyList<CraftRecipeInfo> ForSkill(eCraftingSkill skill);
        IEnumerable<CraftRecipeInfo> Find(eCraftingSkill skill, int minLevel, int maxLevel, eRealm realm);
        CraftRecipeInfo Get(string recipeId);
        /// <summary>The recipe that makes this kind of item nearest the level for the realm, or null.</summary>
        CraftRecipeInfo BestFor(int objectType, int itemType, int itemLevel, eRealm realm);
    }

    /// <summary>
    /// Where crafting happens in the loaded world (DividedRealms' catalog kept these with the recipes): masters, stations
    /// and the merchants who sell each material. WP1's catalog implements it and logs it as CRAFT_WORLD_AUDIT.
    /// </summary>
    public interface ICraftWorldIndex
    {
        bool IsLoaded { get; }
        IEnumerable<CraftNPC> MastersOf(eCraftingSkill skill, eRealm realm);
        IEnumerable<GameStaticItem> Stations(CraftStation station);
        IEnumerable<GameMerchant> SellersOf(string materialTemplateId, eRealm realm);
    }

    /// <summary>The registered catalog and world index; empty until WP1 registers its own.</summary>
    public static class RecipeCatalogs
    {
        private static volatile IRecipeCatalog _current = EmptyCatalog.Instance;
        private static volatile ICraftWorldIndex _world = EmptyCatalog.Instance;

        public static IRecipeCatalog Current
        {
            get => _current;
            set => _current = value ?? EmptyCatalog.Instance;
        }

        public static ICraftWorldIndex World
        {
            get => _world;
            set => _world = value ?? EmptyCatalog.Instance;
        }

        private sealed class EmptyCatalog : IRecipeCatalog, ICraftWorldIndex
        {
            internal static readonly EmptyCatalog Instance = new();
            public bool IsLoaded => false;
            public IReadOnlyList<CraftRecipeInfo> ForSkill(eCraftingSkill skill) => Array.Empty<CraftRecipeInfo>();
            public IEnumerable<CraftRecipeInfo> Find(eCraftingSkill skill, int minLevel, int maxLevel, eRealm realm) => Array.Empty<CraftRecipeInfo>();
            public CraftRecipeInfo Get(string recipeId) => null;
            public CraftRecipeInfo BestFor(int objectType, int itemType, int itemLevel, eRealm realm) => null;
            public IEnumerable<CraftNPC> MastersOf(eCraftingSkill skill, eRealm realm) => Array.Empty<CraftNPC>();
            public IEnumerable<GameStaticItem> Stations(CraftStation station) => Array.Empty<GameStaticItem>();
            public IEnumerable<GameMerchant> SellersOf(string materialTemplateId, eRealm realm) => Array.Empty<GameMerchant>();
        }
    }
}
