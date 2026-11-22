using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Events;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS.Crafting
{
    /// <summary>A recipe the catalog hid, and why (the server's own rule, RecipeDB.FindProblem).</summary>
    public readonly record struct BrokenRecipe(string RecipeId, string ProductId, int SkillType, int Level, eRecipeProblem Problem, string Detail);

    /// <summary>
    /// Every valid recipe of the world, loaded at once and indexed, plus where crafting happens (masters, stations,
    /// material sellers). Crafting 2.0 WP1. Ported from rambodolphin13's DividedRealms
    /// (AutonomousBotCrafting.BuildCatalog, 93c25b69), GPL-3.0, which matched every key without case, as the server does
    /// (SQLite's NOCASE columns and the lower-cased ItemTemplate cache; docs/CRAFTING-M0-FINDINGS.md 1.1). Changed in the
    /// port: every craft and every valid skill type instead of DR's four plus the secondaries; a recipe is hidden by the
    /// server's whole rule (RecipeDB.FindProblem, so the catalog and a player's click agree); alchemy tables (model 820)
    /// are indexed beside forges and lathes; the catalog is the <see cref="IRecipeCatalog"/> and
    /// <see cref="ICraftWorldIndex"/> registered in <see cref="RecipeCatalogs"/>; and the start-up line
    /// AUTONOMOUS_CRAFT_CATALOG becomes CRAFT_RECIPE_AUDIT, CRAFT_RECIPE_BROKEN and CRAFT_WORLD_AUDIT (the old line is
    /// still written, as an alias). Immutable once built, so any thread may read it (bot brain turns run in parallel).
    /// </summary>
    public sealed class RecipeCatalog : IRecipeCatalog, ICraftWorldIndex
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(RecipeCatalog));

        private readonly Dictionary<string, CraftRecipeInfo> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<eCraftingSkill, CraftRecipeInfo[]> _bySkill = new();
        private readonly List<BrokenRecipe> _broken = new();
        private readonly HashSet<string> _materials = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<CraftNPC> _masters = new();
        private readonly List<GameStaticItem> _forges = new();
        private readonly List<GameStaticItem> _lathes = new();
        private readonly List<GameStaticItem> _tables = new();
        private readonly Dictionary<string, List<GameMerchant>> _sellers = new(StringComparer.OrdinalIgnoreCase);

        private RecipeCatalog() { }

        /// <summary>CraftedItem rows read (valid and hidden).</summary>
        public int RowCount { get; private set; }

        /// <summary>Recipes hidden by the server's rule, in row order.</summary>
        public IReadOnlyList<BrokenRecipe> Broken => _broken;

        /// <summary>Every valid recipe.</summary>
        public IEnumerable<CraftRecipeInfo> All => _byId.Values;

        /// <summary>Base and intermediate materials some valid recipe uses (by template id, without case).</summary>
        public IReadOnlyCollection<string> Materials => _materials;

        /// <summary>Whether the masters, stations and merchants were indexed (false for a recipes-only build).</summary>
        public bool WorldIndexed { get; private set; }

        public bool IsLoaded => true;

        bool ICraftWorldIndex.IsLoaded => WorldIndexed;

        // ---- Building ----

        /// <summary>
        /// Builds the recipes from the world's rows. <paramref name="findTemplate"/> must look an ItemTemplate up by id
        /// without case, as GameServer.Database.FindObjectByKey does; ingredient rows are matched to their recipe's
        /// product id without case. Pure apart from the lookup, so tests and the rehearsal build one from lists.
        /// </summary>
        public static RecipeCatalog Build(IEnumerable<DbCraftedItem> rows, IEnumerable<DbCraftedXItem> ingredientRows, Func<string, DbItemTemplate> findTemplate)
        {
            var catalog = new RecipeCatalog();
            ILookup<string, DbCraftedXItem> ingredients = (ingredientRows ?? Enumerable.Empty<DbCraftedXItem>())
                .Where(row => row?.CraftedItemId_nb != null)
                .ToLookup(row => row.CraftedItemId_nb, StringComparer.OrdinalIgnoreCase);
            var bySkill = new Dictionary<eCraftingSkill, List<CraftRecipeInfo>>();

            foreach (DbCraftedItem row in rows ?? Enumerable.Empty<DbCraftedItem>())
            {
                if (row == null)
                    continue;
                catalog.RowCount++;

                if (!ushort.TryParse(row.CraftedItemID, out _))
                {
                    // the client asks for recipes by a 16-bit id, so this row can never be requested
                    catalog._broken.Add(new BrokenRecipe(row.CraftedItemID, row.Id_nb, row.CraftingSkillType, row.CraftingLevel, eRecipeProblem.RecipeIdInvalid, row.CraftedItemID ?? string.Empty));
                    continue;
                }

                DbItemTemplate product = string.IsNullOrEmpty(row.Id_nb) ? null : findTemplate(row.Id_nb);
                var lookedUp = new List<(string Id, int Count, DbItemTemplate Template)>();
                if (product != null)
                    foreach (DbCraftedXItem part in ingredients[row.Id_nb])
                        lookedUp.Add((part.IngredientId_nb, part.Count, string.IsNullOrEmpty(part.IngredientId_nb) ? null : findTemplate(part.IngredientId_nb)));

                eRecipeProblem problem = RecipeDB.FindProblem(product, row.CraftingSkillType, lookedUp, out string detail);
                if (problem != eRecipeProblem.None)
                {
                    catalog._broken.Add(new BrokenRecipe(row.CraftedItemID, row.Id_nb, row.CraftingSkillType, row.CraftingLevel, problem, detail));
                    continue;
                }

                var skill = (eCraftingSkill)row.CraftingSkillType;
                bool makeTemplated = row.MakeTemplated || product.IsStackable; // as RecipeDB's loader forces it
                var parts = new List<Ingredient>(lookedUp.Count);
                var materials = new List<CraftMaterial>(lookedUp.Count);
                foreach ((string _, int count, DbItemTemplate template) in lookedUp)
                {
                    parts.Add(new Ingredient(count, template));
                    materials.Add(new CraftMaterial(template.Id_nb, template.Name, template.Model, count, template.Price, template.PackSize));
                    catalog._materials.Add(template.Id_nb);
                }

                var info = new CraftRecipeInfo(row.CraftedItemID, skill, row.CraftingLevel, product.Id_nb, product.Name,
                    product.Object_Type, product.Item_Type, product.Level, product.Realm, !makeTemplated, product.IsStackable, materials)
                {
                    Recipe = new Recipe(product, parts, skill, row.CraftingLevel, makeTemplated),
                };

                if (!catalog._byId.TryAdd(info.RecipeId, info))
                    continue; // a duplicate id (keys ignore case): the first row wins, as FindObjectByKey would return one

                if (!bySkill.TryGetValue(skill, out List<CraftRecipeInfo> list))
                    bySkill[skill] = list = new List<CraftRecipeInfo>();
                list.Add(info);
            }

            foreach ((eCraftingSkill skill, List<CraftRecipeInfo> list) in bySkill)
                catalog._bySkill[skill] = list.OrderBy(recipe => recipe.Level).ThenBy(recipe => recipe.RecipeId, StringComparer.OrdinalIgnoreCase).ToArray();

            return catalog;
        }

        /// <summary>The station a static item is, by the crafts' own CheckForTools rules (model or name).</summary>
        public static CraftStation StationOf(int model, string name)
        {
            if (model == 478 || string.Equals(name, "forge", StringComparison.OrdinalIgnoreCase))
                return CraftStation.Forge;
            if (model == 481 || string.Equals(name, "lathe", StringComparison.OrdinalIgnoreCase))
                return CraftStation.Lathe;
            if (model == 820 || string.Equals(name, "alchemy table", StringComparison.OrdinalIgnoreCase))
                return CraftStation.AlchemyTable;
            return CraftStation.None;
        }

        /// <summary>Indexes one object of the world: a crafting master or a station; anything else is ignored.</summary>
        public void AddWorldObject(GameObject item)
        {
            if (item is CraftNPC master)
                _masters.Add(master);
            else if (item is GameStaticItem station)
            {
                switch (StationOf(station.Model, station.Name))
                {
                    case CraftStation.Forge: _forges.Add(station); break;
                    case CraftStation.Lathe: _lathes.Add(station); break;
                    case CraftStation.AlchemyTable: _tables.Add(station); break;
                }
            }
        }

        /// <summary>Indexes a merchant for each recipe material it sells (only merchants who take coin).</summary>
        public void AddMerchant(GameMerchant merchant, IEnumerable<DbItemTemplate> sold)
        {
            if (merchant == null || sold == null || !AutonomousBotEconomy.SellsForCoin(merchant))
                return;
            foreach (DbItemTemplate item in sold)
            {
                if (item?.Id_nb == null || !_materials.Contains(item.Id_nb))
                    continue;
                if (!_sellers.TryGetValue(item.Id_nb, out List<GameMerchant> sellers))
                    _sellers[item.Id_nb] = sellers = new List<GameMerchant>();
                if (!sellers.Contains(merchant))
                    sellers.Add(merchant);
            }
        }

        /// <summary>Indexes the loaded world's masters, stations and material merchants (housing regions left out, as DR did).</summary>
        public void IndexWorld(IEnumerable<Region> regions)
        {
            foreach (Region region in regions ?? Enumerable.Empty<Region>())
            {
                if (region == null || region.IsHousing)
                    continue;
                foreach (GameMerchant merchant in region.Merchants)
                    if (merchant?.TradeItems != null)
                        AddMerchant(merchant, merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>());
                GameObject[] objects = region.Objects;
                if (objects == null)
                    continue;
                foreach (GameObject item in objects)
                    if (item != null)
                        AddWorldObject(item);
            }
            // This walk reads every merchant's list: one warning for all the item templates the world data lacks (1.2.1).
            MerchantTradeItems.ReportMissingTemplates();
            WorldIndexed = true;
        }

        // ---- IRecipeCatalog ----

        public IReadOnlyList<CraftRecipeInfo> ForSkill(eCraftingSkill skill) =>
            _bySkill.TryGetValue(skill, out CraftRecipeInfo[] recipes) ? recipes : Array.Empty<CraftRecipeInfo>();

        public IEnumerable<CraftRecipeInfo> Find(eCraftingSkill skill, int minLevel, int maxLevel, eRealm realm) =>
            ForSkill(skill).Where(recipe => recipe.Level >= minLevel && recipe.Level <= maxLevel && CraftableBy(recipe.Realm, realm));

        public CraftRecipeInfo Get(string recipeId) =>
            recipeId != null && _byId.TryGetValue(recipeId.Trim(), out CraftRecipeInfo recipe) ? recipe : null;

        /// <summary>The recipe whose product has this object and item type nearest the item level, craftable by the realm;
        /// on a tie the lower product level, then the lower recipe level.</summary>
        public CraftRecipeInfo BestFor(int objectType, int itemType, int itemLevel, eRealm realm) =>
            _byId.Values
                .Where(recipe => recipe.ObjectType == objectType && recipe.ItemType == itemType && CraftableBy(recipe.Realm, realm))
                .OrderBy(recipe => Math.Abs(recipe.ProductLevel - itemLevel))
                .ThenBy(recipe => recipe.ProductLevel)
                .ThenBy(recipe => recipe.Level)
                .ThenBy(recipe => recipe.RecipeId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        /// <summary>The server's rule (NormalServerRules.IsAllowedToCraft): the product's realm, or no realm when
        /// allow_craft_norealm_items is on. eRealm.None asks for every realm.</summary>
        public static bool CraftableBy(int productRealm, eRealm realm, bool allowNoRealm) =>
            realm == eRealm.None || productRealm == (int)realm || (productRealm == 0 && allowNoRealm);

        private static bool CraftableBy(int productRealm, eRealm realm) => CraftableBy(productRealm, realm, Properties.ALLOW_CRAFT_NOREALM_ITEMS);

        // ---- ICraftWorldIndex ----

        public IEnumerable<CraftNPC> MastersOf(eCraftingSkill skill, eRealm realm) =>
            _masters.Where(master => master.TheCraftingSkill == skill && master.ObjectState == GameObject.eObjectState.Active &&
                                     (realm == eRealm.None || master.Realm == eRealm.None || master.Realm == realm));

        public IEnumerable<GameStaticItem> Stations(CraftStation station) => station switch
        {
            CraftStation.Forge => _forges,
            CraftStation.Lathe => _lathes,
            CraftStation.AlchemyTable => _tables,
            _ => Array.Empty<GameStaticItem>(),
        };

        public IEnumerable<GameMerchant> SellersOf(string materialTemplateId, eRealm realm) =>
            materialTemplateId != null && _sellers.TryGetValue(materialTemplateId, out List<GameMerchant> sellers)
                ? sellers.Where(merchant => merchant.ObjectState == GameObject.eObjectState.Active &&
                                            (realm == eRealm.None || merchant.Realm == eRealm.None || merchant.Realm == realm))
                : Array.Empty<GameMerchant>();

        // ---- Audit lines ----

        /// <summary>
        /// The CRAFT_RECIPE_AUDIT lines: one per skill that has rows, then the total with the valid recipes per product
        /// realm. "match=nocase" records that ids were matched as the server matches them.
        /// </summary>
        public IEnumerable<string> RecipeAuditLines()
        {
            var rowsBySkill = new SortedDictionary<int, (int Valid, int Hidden)>();
            foreach (CraftRecipeInfo recipe in _byId.Values)
            {
                rowsBySkill.TryGetValue((int)recipe.Skill, out var counts);
                rowsBySkill[(int)recipe.Skill] = (counts.Valid + 1, counts.Hidden);
            }
            foreach (BrokenRecipe broken in _broken)
            {
                rowsBySkill.TryGetValue(broken.SkillType, out var counts);
                rowsBySkill[broken.SkillType] = (counts.Valid, counts.Hidden + 1);
            }
            foreach ((int skill, (int valid, int hidden)) in rowsBySkill)
                yield return EconomyLog.Line(EconomyLog.CraftRecipeAudit, ("skill", SkillName(skill)), ("recipes", valid + hidden), ("valid", valid), ("hidden", hidden));

            int Realm(int realm) => _byId.Values.Count(recipe => recipe.Realm == realm);
            yield return EconomyLog.Line(EconomyLog.CraftRecipeAudit, ("skill", "all"), ("recipes", RowCount), ("valid", _byId.Count),
                ("hidden", _broken.Count), ("albion", Realm(1)), ("midgard", Realm(2)), ("hibernia", Realm(3)), ("norealm", Realm(0)),
                ("materials", _materials.Count), ("match", "nocase"));
        }

        /// <summary>One CRAFT_RECIPE_BROKEN line per hidden recipe.</summary>
        public IEnumerable<string> BrokenRecipeLines() =>
            _broken.Select(broken => EconomyLog.Line(EconomyLog.CraftRecipeBroken, ("recipe", broken.RecipeId ?? string.Empty),
                ("product", broken.ProductId ?? string.Empty), ("skill", SkillName(broken.SkillType)), ("level", broken.Level),
                ("problem", broken.Problem), ("detail", broken.Detail ?? string.Empty)));

        /// <summary>
        /// The CRAFT_WORLD_AUDIT lines: per realm the functional masters (by craft), the merchants selling recipe
        /// materials and how many materials are on sale; then the stations of the whole world.
        /// </summary>
        public IEnumerable<string> WorldAuditLines()
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                CraftNPC[] masters = _masters.Where(master => master.Realm == realm || master.Realm == eRealm.None).ToArray();
                string bySkill = string.Join(",", masters.GroupBy(master => master.TheCraftingSkill).OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}:{group.Count()}"));
                int sellers = _sellers.Values.SelectMany(list => list).Distinct()
                    .Count(merchant => merchant.Realm == realm || merchant.Realm == eRealm.None);
                int onSale = _sellers.Count(pair => pair.Value.Any(merchant => merchant.Realm == realm || merchant.Realm == eRealm.None));
                yield return EconomyLog.Line(EconomyLog.CraftWorldAudit, ("realm", RealmName(realm)), ("masters", masters.Length), ("bySkill", bySkill),
                    ("sellers", sellers), ("materialsOnSale", onSale), ("materials", _materials.Count));
            }
            yield return EconomyLog.Line(EconomyLog.CraftWorldAudit, ("realm", "all"), ("masters", _masters.Count), ("forges", _forges.Count),
                ("lathes", _lathes.Count), ("tables", _tables.Count), ("onSale", _sellers.Count), ("materials", _materials.Count));
        }

        /// <summary>DividedRealms' AUTONOMOUS_CRAFT_CATALOG line, kept as an alias for tools that read it (plus tables=).</summary>
        public string DividedRealmsCatalogLine() =>
            $"{EconomyLog.DividedRealmsCatalog} recipes={_byId.Count} materials={_materials.Count} onSale={_sellers.Count} " +
            $"masters={_masters.Count} forges={_forges.Count} lathes={_lathes.Count} tables={_tables.Count}";

        // eRealm.Albion's ToString is an alias (_FirstPlayerRealm), so realms are named here.
        private static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            _ => "none",
        };

        /// <summary>The skill as the line writes it: its name (unquoted) when it is one, else the stored number.</summary>
        private static object SkillName(int skillType) =>
            Enum.IsDefined(typeof(eCraftingSkill), skillType) ? (eCraftingSkill)skillType : skillType;

        // ---- Start-up ----

        private static int _loading;

        /// <summary>The catalog is built once in the background after start-up, then registered and audited.</summary>
        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => LoadInBackground();

        /// <summary>Builds and registers the world's catalog on a worker thread (once per run).</summary>
        public static void LoadInBackground()
        {
            if (Interlocked.Exchange(ref _loading, 1) != 0)
                return;
            Task.Run(() =>
            {
                try
                {
                    RecipeCatalog catalog = Build(GameServer.Database.SelectAllObjects<DbCraftedItem>(),
                        GameServer.Database.SelectAllObjects<DbCraftedXItem>(),
                        id => GameServer.Database.FindObjectByKey<DbItemTemplate>(id));
                    catalog.IndexWorld(WorldMgr.GetAllRegions());
                    RecipeCatalogs.Current = catalog;
                    RecipeCatalogs.World = catalog;
                    catalog.WriteAudit();
                }
                catch (Exception exception)
                {
                    if (Log.IsErrorEnabled)
                        Log.Error("CRAFT_RECIPE_AUDIT failed: the recipe catalog did not load (players craft as before; nothing reads the catalog yet)", exception);
                }
            });
        }

        private void WriteAudit()
        {
            if (!Log.IsInfoEnabled)
                return;
            foreach (string line in RecipeAuditLines())
                Log.Info(line);
            foreach (string line in BrokenRecipeLines())
                Log.Info(line);
            foreach (string line in WorldAuditLines())
                Log.Info(line);
            Log.Info(DividedRealmsCatalogLine());
        }
    }
}
