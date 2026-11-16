using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Gamebots craft for real (Crafting 2.0 WP4, M2). A Crafter gamebot joins an order at a real craft master of its realm,
/// buys the recipe's materials from a real merchant with its own coin, works at a real forge, lathe or alchemy table,
/// rolls every attempt by the player's rules (<see cref="CraftRules"/>: chance to make, quality, chance to gain, time) and
/// ends up with real items (<c>IsCrafted</c>, <c>Creator</c>), which it wears when they are upgrades and otherwise posts on
/// the Realm Exchange at the inventory stop that ends the trip. Skill lives in
/// <see cref="OfflineWorldBotRecord.SerializedCraftingSkills"/> in <see cref="BotCraftingSkills"/>' format.
/// <para>Ported from rambodolphin13's DividedRealms (AutonomousBotCrafting, 93c25b69 → ceac061a → e37f8810), GPL-3.0.
/// Changes in the port: only Crafter-type gamebots craft, and only while bot_crafters is on (DR: any bot of level 5+,
/// bot_crafting on); the recipe catalog and the world index are the registered contracts (RecipeCatalogs, WP1) instead
/// of DR's own catalog; the rules are CraftRules; a gain gives the bot pace's points; titles, the Legendary first,
/// the CRAFT_* lines, and a bounded number of plans and attempts per tick are ours.</para>
/// Companions (/bot) and real players never reach this code.
/// </summary>
public static class AutonomousBotCrafting
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotCrafting));

    /// <summary>bot_crafters (default off).</summary>
    public static bool Enabled => CraftingEconomyProperties.BOT_CRAFTERS;

    private static readonly BotCraftingRules.TickBudget PlanBudget = new(BotCraftingRules.SessionPlansPerTick);
    private static readonly BotCraftingRules.TickBudget AttemptBudget = new(BotCraftingRules.AttemptsPerTick);
    /// <summary>The between-task assignment whose craft trip is over, per bot (DR).</summary>
    private static readonly ConcurrentDictionary<long, string> FinishedTrips = new();
    /// <summary>Pieces made on a trip that are not its best (True165: only the best of a session is listed).</summary>
    private static readonly ConcurrentDictionary<long, HashSet<string>> Seconds = new();
    /// <summary>A missing master logged once per realm and order (M0: Albion alchemy in Camelot, Midgard spellcraft in
    /// Aegir's Landing, Hibernian tailoring in Domnann, siegecraft nowhere).</summary>
    private static readonly ConcurrentDictionary<(eRealm, eCraftingSkill), bool> MissingMasterLogged = new();
    /// <summary>Striped locks for a bot's skill gains (see <see cref="Gain"/>); a power of two.</summary>
    private static readonly object[] SkillLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();

    #region Who

    /// <summary>Whether this live gamebot crafts: bot_crafters on, an autonomous gamebot (never a companion, an Epic
    /// Special Force visitor or a beggar), of the Crafter personality.</summary>
    public static bool IsCrafter(GameBot bot) =>
        Enabled && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && !bot.IsEpicSpecialForce &&
        bot.DatabaseID > 0 && bot.PersistentRecord != null &&
        BotCraftingRules.IsCrafterType(AutonomousBotPersonality.TypeFor(bot.DatabaseID), AutonomousBeggars.Is(bot));

    public static bool IsTrained(GameBot bot) => !string.IsNullOrWhiteSpace(bot?.PersistentRecord?.SerializedCraftingSkills);

    public static int SkillValue(GameBot bot, eCraftingSkill skill) =>
        BotCraftingSkills.Value(bot?.PersistentRecord?.SerializedCraftingSkills, skill);

    public static void ForgetTrip(GameBot bot) => FinishedTrips.TryRemove(bot.DatabaseID, out _);

    public static void MarkTripDone(GameBot bot) => FinishedTrips[bot.DatabaseID] = bot.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;

    public static bool TripDone(GameBot bot) =>
        bot != null && FinishedTrips.TryGetValue(bot.DatabaseID, out string assignment) && assignment == bot.PersistentRecord?.ObjectiveAssignmentId;

    #endregion

    #region Catalog (the registered contracts)

    private static IRecipeCatalog Catalog => RecipeCatalogs.Current;
    private static ICraftWorldIndex World => RecipeCatalogs.World;

    private sealed class CatalogCache
    {
        public IRecipeCatalog Catalog;
        public readonly ConcurrentDictionary<eCraftingSkill, HashSet<string>> MaterialsBySkill = new();
        public readonly ConcurrentDictionary<(eRealm, string), bool> Sold = new();
    }

    private static volatile CatalogCache _cache = new();

    /// <summary>Tests only: every material counts as on sale.</summary>
    public static bool AllOnSaleForTests;

    private static CatalogCache Cache()
    {
        CatalogCache cache = _cache;
        IRecipeCatalog current = Catalog;
        if (!ReferenceEquals(cache.Catalog, current))
            _cache = cache = new CatalogCache { Catalog = current };
        return cache;
    }

    public static IReadOnlyList<CraftRecipeInfo> RecipesOf(eCraftingSkill skill) =>
        Catalog.IsLoaded ? Catalog.ForSkill(skill) ?? [] : [];

    private static HashSet<string> MaterialsOf(eCraftingSkill skill) =>
        Cache().MaterialsBySkill.GetOrAdd(skill, s => new HashSet<string>(
            RecipesOf(s).SelectMany(recipe => recipe.Materials ?? []).Select(material => material.TemplateId)
                .Where(id => !string.IsNullOrEmpty(id)), StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether a merchant of this realm sells this material for coin (cached per catalog).</summary>
    public static bool MaterialOnSale(string templateId, eRealm realm) =>
        AllOnSaleForTests || !string.IsNullOrEmpty(templateId) && Cache().Sold.GetOrAdd((realm, templateId), key =>
            World.IsLoaded && World.SellersOf(key.Item2, key.Item1).Any(merchant => IsUsableMerchant(merchant, key.Item1)));

    /// <summary>Whether every material of a recipe is sold for coin by a merchant of this realm (DR's OnSale).</summary>
    public static bool OnSale(CraftRecipeInfo recipe, eRealm realm) =>
        recipe?.Materials != null && recipe.Materials.All(material => MaterialOnSale(material.TemplateId, realm));

    private static bool IsUsableMerchant(GameMerchant merchant, eRealm realm) =>
        merchant != null && merchant.ObjectState == GameObject.eObjectState.Active && AutonomousBotEconomy.SellsForCoin(merchant) &&
        (merchant.Realm == eRealm.None || merchant.Realm == realm);

    public static IEnumerable<CraftNPC> MastersOf(eCraftingSkill skill, eRealm realm) =>
        World.IsLoaded
            ? World.MastersOf(skill, realm).Where(master => master != null && master.TheCraftingSkill == skill &&
                master.ObjectState == GameObject.eObjectState.Active && (master.Realm == eRealm.None || master.Realm == realm))
            : [];

    public static IEnumerable<GameStaticItem> Stations(CraftStation station) =>
        World.IsLoaded && station != CraftStation.None ? World.Stations(station).Where(item => item != null) : [];

    public static IEnumerable<GameMerchant> Sellers(CraftMaterial material, eRealm realm) =>
        World.IsLoaded ? World.SellersOf(material.TemplateId, realm).Where(merchant => IsUsableMerchant(merchant, realm)) : [];

    /// <summary>
    /// Whether this stack is material for a craft the bot knows (DR: only its own crafts count, so a trained crafter does
    /// not hoard every stack). Null for a bot with no craft (today's rule decides). While the catalog loads every stack
    /// counts, so nothing is sold by mistake on a bot's first stop after a start-up (DR).
    /// </summary>
    public static bool? IsMaterialFor(GameBot bot, DbInventoryItem item)
    {
        string saved = bot?.PersistentRecord?.SerializedCraftingSkills;
        if (string.IsNullOrWhiteSpace(saved) || item == null || !item.IsStackable)
            return null;
        if (!Catalog.IsLoaded)
            return true;
        foreach (KeyValuePair<eCraftingSkill, int> skill in BotCraftingSkills.Parse(saved))
            if (MaterialsOf(skill.Key).Contains(item.Id_nb))
                return true;
        return false;
    }

    /// <summary>A finished piece this bot made (DR).</summary>
    public static bool IsOwnWork(GameBot bot, DbInventoryItem item) =>
        item != null && item.IsCrafted && !item.IsStackable && bot != null && item.Creator == bot.Name;

    /// <summary>A piece of a trip that was not its best: it goes to a vendor at the inventory stop, never to the Exchange.</summary>
    public static bool IsSecond(GameBot bot, DbInventoryItem item)
    {
        if (bot == null || item?.ObjectId == null || !Seconds.TryGetValue(bot.DatabaseID, out HashSet<string> seconds))
            return false;
        lock (seconds)
            return seconds.Contains(item.ObjectId);
    }

    /// <summary>Own work that waits for a Realm Exchange post: the best of its trip.</summary>
    public static bool IsListableWork(GameBot bot, DbInventoryItem item) => IsOwnWork(bot, item) && !IsSecond(bot, item);

    private static IEnumerable<DbInventoryItem> Backpack(GameBot bot) =>
        bot?.Inventory?.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)?.Where(item => item != null) ?? [];

    /// <summary>Pieces of its own work the bot still carries to sell (DR's UnsoldWork; a reserved piece, WP5's order, is not).</summary>
    public static int UnsoldWork(GameBot bot) =>
        Backpack(bot).Count(item => IsListableWork(bot, item) && !EconomySeams.IsReserved(bot, item));

    /// <summary>Material stacks the bot carries, for the decision to craft (DR).</summary>
    public static int MaterialStacks(GameBot bot) =>
        IsTrained(bot) ? Backpack(bot).Count(item => IsMaterialFor(bot, item) == true) : 0;

    #endregion

    #region Orders

    /// <summary>
    /// The order a Crafter joins (realm weights, DR's class rule as the tie-break), among the orders that have a master
    /// in its realm anywhere in the world. An order with no master in the realm is skipped and logged once (M0: no
    /// siegecraft master exists); one whose master is only in another town is taken, and the trip walks there.
    /// </summary>
    public static eCraftingSkill ChooseOrder(GameBot bot, Func<eCraftingSkill, bool> reachable = null)
    {
        IReadOnlyDictionary<eCraftingSkill, int> counts = BotCrafterRoster.Instance.OrderCounts(bot.Realm);
        eCraftingSkill classOrder = BotCraftingRules.ClassOrder((eCharacterClass)(bot.CharacterClass?.ID ?? 0),
            bot.CharacterClass?.ClassType ?? eClassType.PureTank, bot.DatabaseID);
        return BotCraftingRules.ChooseOrder(counts, order =>
        {
            if (!MastersOf(order, bot.Realm).Any())
            {
                NoteMissingMaster(bot.Realm, order, "no master of the order in the realm");
                return false;
            }
            return reachable == null || reachable(order);
        }, classOrder);
    }

    public static void NoteMissingMaster(eRealm realm, eCraftingSkill order, string reason)
    {
        if (!MissingMasterLogged.TryAdd((realm, order), true) || !Log.IsInfoEnabled)
            return;
        Log.Info(EconomyLog.Line(EconomyLog.CraftEnroll, ("outcome", "skipped"), ("realm", GlobalConstants.RealmToName(realm)),
            ("order", order.ToString()), ("reason", reason)));
    }

    /// <summary>Joins the master's order (DR's Train): the master's craft first, then what it teaches, each at 1.</summary>
    public static void Train(GameBot bot, CraftNPC master)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        record.SerializedCraftingSkills = BotCraftingSkills.Join(master.TheCraftingSkill, master.TrainedSkills);
        BotCrafterRoster.Instance.Enroll(bot, master.TheCraftingSkill);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Crafting);
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftEnroll, ("bot", bot.Name), ("id", bot.DatabaseID), ("level", (int)bot.Level),
                ("realm", GlobalConstants.RealmToName(bot.Realm)), ("outcome", "joined"), ("order", master.TheCraftingSkill.ToString()),
                ("master", master.Name), ("zone", master.CurrentZone?.Description ?? string.Empty)));
    }

    #endregion

    #region Planning

    /// <summary>
    /// What the bot sets out to make now, or null when nothing fits its skill, coin, bag, the trip's time left
    /// (<paramref name="msLeft"/>) or the market (DR's PlanSession, with the copies cut by BotCraftingRules.CopiesThatFit).
    /// At most <see cref="BotCraftingRules.SessionPlansPerTick"/> plans start a tick server-wide: <paramref name="deferred"/>
    /// is true when this one must wait for the next turn.
    /// </summary>
    public static BotCraftingRules.Session PlanSession(GameBot bot, out bool deferred) =>
        PlanSession(bot, BotCraftingRules.TripWorkMilliseconds, out deferred);

    public static BotCraftingRules.Session PlanSession(GameBot bot, long msLeft, out bool deferred)
    {
        deferred = false;
        string saved = bot?.PersistentRecord?.SerializedCraftingSkills;
        if (!Catalog.IsLoaded || string.IsNullOrWhiteSpace(saved))
            return null;
        if (!PlanBudget.TryTake(GameLoop.GameLoopTime))
        {
            deferred = true;
            return null;
        }
        using var aiProfile = AiProfile.Scope(AiSystem.CraftSession); // AI_PROFILE: measures only
        long coin = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
        // A piece another bot (or the quartermaster) ordered is made first, with the crafter's own materials (DR; WP5's board).
        if (AutonomousBotCraftOrders.PlanOrder(bot, coin) is BotCraftingRules.Session order)
            return order;
        // A session's worth still waiting for the Realm Exchange: selling it comes first (DR).
        if (UnsoldWork(bot) >= BotCraftingRules.CraftsPerSession)
            return null;
        Func<CraftRecipeInfo, bool> useful = UsefulTo(bot);
        foreach (KeyValuePair<eCraftingSkill, int> skill in BotCraftingSkills.Parse(saved))
        {
            // A primary craft held back by its secondary skills finds no recipe here, and the next skill in the list trains them (DR).
            BotCraftingRules.Choice choice = BotCraftingRules.ChooseRecipe(RecipesOf(skill.Key), skill.Key, saved, bot.Realm,
                (long)(coin * BotCraftingRules.ShareOfCoin), recipe => OnSale(recipe, bot.Realm), useful, Properties.ALLOW_CRAFT_NOREALM_ITEMS);
            if (choice == null)
                continue;
            int copies = BotCraftingRules.CopiesFor(choice.Recipe, coin);
            if (copies <= 0)
                continue;
            var session = new BotCraftingRules.Session(choice.Recipe, skill.Key, copies);
            int freeSlots = bot.Inventory?.CountSlots(false, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) ?? 0;
            copies = BotCraftingRules.CopiesThatFit(copies, choice.Recipe.Recipe?.Product?.IsStackable ?? choice.Recipe.Stackable,
                freeSlots, WorkSeconds(bot, session), choice.Make, msLeft);
            if (copies > 0)
                return session with { Copies = copies };
        }
        return null;
    }

    /// <summary>
    /// Whether a wearer could put this product on or wield it now (DR's CouldUse): armor and weapons of its level window
    /// that its class may use (a gamebot's melee weapons by its configured weapon skills, the rest by the item rules).
    /// </summary>
    public static bool CouldUse(GameLiving wearer, DbItemTemplate product)
    {
        if (wearer == null || product == null || product.IsStackable || !BotCraftingRules.FitsLevel(product.Level, wearer.Level))
            return false;
        var type = (eObjectType)product.Object_Type;
        bool armor = type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor || type == eObjectType.Shield;
        bool weapon = type is >= eObjectType._FirstWeapon and <= eObjectType._LastWeapon;
        if (!armor && !weapon)
            return false;
        try
        {
            if (wearer is GameBot bot && BotWeaponStats.IsMeleeWeapon(type))
                return BotWeaponStats.IsConfiguredMeleeWeapon(bot, product);
            return GameServer.ServerRules.CheckAbilityToUseItem(wearer, product);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The bot and its group members, the people a crafter's work is useful to (DR).</summary>
    public static Func<CraftRecipeInfo, bool> UsefulTo(GameBot bot)
    {
        GameLiving[] wearers = bot?.Group?.GetMembersInTheGroup().Where(member => member != null).ToArray() ?? [];
        if (bot != null && !wearers.Contains(bot))
            wearers = wearers.Append(bot).ToArray();
        return recipe => recipe?.Recipe?.Product is DbItemTemplate product && wearers.Any(wearer => CouldUse(wearer, product));
    }

    private static bool SameMaterial(DbInventoryItem item, CraftMaterial material) =>
        item != null && (string.Equals(item.Id_nb, material.TemplateId, StringComparison.OrdinalIgnoreCase) ||
                         !string.IsNullOrEmpty(material.Name) && item.Name == material.Name);

    private static int CarriedMaterial(GameBot bot, CraftMaterial material) =>
        Backpack(bot).Where(item => SameMaterial(item, material)).Sum(item => item.Count);

    public static List<BotCraftingRules.Purchase> ShoppingFor(GameBot bot, BotCraftingRules.Session session) =>
        BotCraftingRules.Shopping(session.Recipe, session.Copies, material => CarriedMaterial(bot, material));

    /// <summary>The server's template of a material: the recipe's own ingredient when it has one, else the database's.</summary>
    public static DbItemTemplate TemplateOf(CraftRecipeInfo recipe, CraftMaterial material)
    {
        foreach (Ingredient ingredient in recipe?.Recipe?.Ingredients ?? [])
            if (string.Equals(ingredient.Material?.Id_nb, material.TemplateId, StringComparison.OrdinalIgnoreCase))
                return ingredient.Material;
        return string.IsNullOrEmpty(material.TemplateId) ? null : GameServer.Database?.FindObjectByKey<DbItemTemplate>(material.TemplateId);
    }

    #endregion

    #region Working

    /// <summary>Buys one material from a real merchant the bot stands beside, at the merchant's price, as GameMerchant does (DR).</summary>
    public static bool TryBuy(GameBot bot, GameMerchant merchant, BotCraftingRules.Session session, BotCraftingRules.Purchase purchase)
    {
        if (bot?.Inventory == null || merchant == null || purchase == null ||
            !merchant.IsWithinRadius(bot, Properties.WORLD_PICKUP_DISTANCE))
            return false;
        DbItemTemplate template = TemplateOf(session.Recipe, purchase.Material);
        if (template == null || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, purchase.Copper, EconomyFlowKind.CraftMaterials))
            return false;
        if (!bot.Inventory.AddTemplate(GameInventoryItem.Create(template), purchase.Amount, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
        {
            AutonomousBotEconomy.AddMoney(bot.DatabaseID, purchase.Copper, EconomyFlowKind.Refund);
            return false;
        }
        BotCrafterRoster.Instance.NoteSpent(bot.DatabaseID, purchase.Copper);
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Money);
        return true;
    }

    /// <summary>The craft's clock for this bot as a player's (AbstractCraftingSkill.GetCraftingTime through CraftRules):
    /// crafting_speed and the capital bonus, the realm's relics and keep bonuses, max_craft_time.</summary>
    public static CraftContext ContextFor(GameBot bot, BotCraftingRules.Session session)
    {
        int skill = SkillValue(bot, session.Skill);
        bool capital = bot.CurrentRegion?.IsCapitalCity == true;
        double speed = Properties.CRAFTING_SPEED <= 0 ? 1.0 : Properties.CRAFTING_SPEED;
        if (capital && Properties.CAPITAL_CITY_CRAFTING_SPEED_BONUS > 0)
            speed *= Properties.CAPITAL_CITY_CRAFTING_SPEED_BONUS;
        int relics = 0;
        double keep = 1.0;
        try
        {
            relics = RelicMgr.GetRelicCount(bot.Realm);
            keep = CraftRules.KeepTimerDivisor(Keeps.KeepBonusMgr.RealmHasBonus(Keeps.eKeepBonusType.Craft_Timers_5, bot.Realm),
                Keeps.KeepBonusMgr.RealmHasBonus(Keeps.eKeepBonusType.Craft_Timers_3, bot.Realm));
        }
        catch { } // tests and a world without keeps: no bonus
        return new CraftContext(skill, session.Recipe.Level, capital, Properties.CAPITAL_CITY_CRAFTING_SKILL_GAIN_BONUS, speed,
            relics, keep, Properties.MAX_CRAFT_TIME);
    }

    /// <summary>Seconds one attempt takes, by the player's clock.</summary>
    public static int WorkSeconds(GameBot bot, BotCraftingRules.Session session) =>
        Math.Max(1, CraftRules.CraftingTimeSeconds(ContextFor(bot, session), session.Recipe.IngredientCounts));

    /// <summary>Whether an attempt may be resolved this tick (at most <see cref="BotCraftingRules.AttemptsPerTick"/> server-wide).</summary>
    public static bool TryTakeAttempt() => AttemptBudget.TryTake(GameLoop.GameLoopTime);

    /// <summary>
    /// One attempt, by CraftAction's rules: the chance to make (CraftRules); a failure costs nothing (today's player rule);
    /// a success takes the materials, makes the item (a unique piece rolls its quality as a player's does) and may teach
    /// points (the chance to gain, with the capital bonus, then the bot pace's points). Returns the item made, or null;
    /// <paramref name="stop"/> when the session cannot go on (materials, room, recipe).
    /// </summary>
    public static DbInventoryItem Attempt(GameBot bot, BotCraftingRules.Session session, out bool stop, Random random = null)
    {
        stop = false;
        random ??= Random.Shared;
        OfflineWorldBotRecord record = bot?.PersistentRecord;
        CraftRecipeInfo recipe = session?.Recipe;
        DbItemTemplate product = recipe?.Recipe?.Product;
        if (record == null || bot.Inventory == null || product == null ||
            BotCraftingRules.Shopping(recipe, 1, material => CarriedMaterial(bot, material)).Count > 0 ||
            !product.IsStackable && bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
        {
            stop = true;
            return null;
        }
        using var aiProfile = AiProfile.Scope(AiSystem.CraftSession); // AI_PROFILE: measures only
        CraftContext context = ContextFor(bot, session);
        // Util.Chance(p) is p > Next(100) (CraftRules.RollQuality draws the same way).
        if (!(CraftRules.ChanceToMake(context.Skill, recipe.Level) > random.Next(100)))
            return null;

        foreach (CraftMaterial material in recipe.Materials)
            TakeMaterial(bot, material);
        DbInventoryItem item = MakeProduct(bot, recipe, context.Skill, random);
        if (item == null)
        {
            stop = true;
            return null;
        }
        int gained = CraftRules.ChanceToGainPoint(context) > random.Next(100)
            ? Gain(bot, session.Skill, recipe.Materials.Select(material => material.Model))
            : 0;
        BotCrafterRoster.Instance.NoteMade(bot.DatabaseID, item.Quality == 100 && !item.IsStackable);
        if (EconomyDiagnostics.CraftMadeLines && Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftMade, ("bot", bot.Name), ("id", bot.DatabaseID), ("level", (int)bot.Level),
                ("craft", session.Skill.ToString()), ("skill", context.Skill), ("recipeLevel", recipe.Level), ("item", item.Name),
                ("itemId", item.Id_nb), ("quality", item.Quality), ("gained", gained)));
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Crafting);
        // A commissioned piece (WP5) is the buyer's: it waits in the bag for delivery and is never worn by its maker (DR).
        if (session.OrderId != null && !item.IsStackable)
            CraftOrders.Board.TryMarkMade(session.OrderId, item);
        else if (!item.IsStackable)
            AutonomousBotEconomy.TryEquipOwnedUpgrade(bot, item, "crafted");
        return item;
    }

    private static void TakeMaterial(GameBot bot, CraftMaterial material)
    {
        int count = material.Count;
        foreach (DbInventoryItem item in Backpack(bot).ToList())
        {
            if (count <= 0)
                break;
            if (!SameMaterial(item, material))
                continue;
            int take = Math.Min(count, item.Count);
            if (item.Count > take ? bot.Inventory.RemoveCountFromStack(item, take) : bot.Inventory.RemoveItem(item))
                count -= take;
        }
    }

    /// <summary>The product as AbstractCraftingSkill.BuildCraftedItem makes it (DR's MakeProduct): a unique piece with its
    /// rolled quality, or a templated item or stack; crafted by the bot.</summary>
    private static DbInventoryItem MakeProduct(GameBot bot, CraftRecipeInfo recipe, int skill, Random random)
    {
        DbItemTemplate product = recipe.Recipe.Product;
        int count = Math.Max(1, product.PackSize);
        if (product.IsStackable)
        {
            DbInventoryItem stack = GameInventoryItem.Create(product);
            stack.IsCrafted = true;
            stack.Creator = bot.Name;
            return bot.Inventory.AddTemplate(stack, count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) ? stack : null;
        }
        DbInventoryItem item;
        if (recipe.Recipe.IsForUniqueProduct)
        {
            item = GameInventoryItem.Create(new DbItemUnique(product));
            item.Quality = CraftRules.RollQuality(skill, recipe.Level, random);
        }
        else
            item = GameInventoryItem.Create(product);
        item.IsCrafted = true;
        item.Creator = bot.Name;
        item.Count = count;
        return bot.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item) ? item : null;
    }

    /// <summary>
    /// One won gain: each skill the craft raises (BotCraftingRules.GainedSkills) rises by the bot pace's points, a
    /// secondary never past 1300. Title rungs reached are logged, said and, at Legendary, claimed as a server first.
    /// Returns the points the craft itself gained.
    /// </summary>
    public static int Gain(GameBot bot, eCraftingSkill craft, IEnumerable<int> materialModels)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        double rate = BotRate(bot.Level);
        int[] models = materialModels?.ToArray() ?? [];
        string saved;
        int before;
        // An enchanter gains from another crafter's brain turn (BotEnchanting), and brain turns run in parallel: the
        // read-modify-write of the skills string holds the bot's skill lock (no database work, no other lock inside).
        lock (SkillLocks[(int)(bot.DatabaseID & (SkillLocks.Length - 1))])
        {
            saved = record.SerializedCraftingSkills;
            before = BotCraftingSkills.Value(saved, craft);
            foreach ((eCraftingSkill skill, bool primary) in BotCraftingRules.GainedSkills(craft, models))
            {
                int current = BotCraftingSkills.Value(saved, skill);
                int points = BotCraftingRules.PointsPerGain(CraftingEconomyProperties.BOT_CRAFTING_PACE, rate, current, primary);
                saved = BotCraftingSkills.Gain(saved, skill, primary, points);
            }
            record.SerializedCraftingSkills = saved;
        }
        int after = BotCraftingSkills.Value(saved, craft);
        if (craft == BotCraftingSkills.Primary(saved) && BotCraftingRules.IsOrder(craft))
            foreach (int rung in BotCraftingRules.TitlesReached(before, after))
                OnTitle(bot, craft, rung);
        return after - before;
    }

    private static double BotRate(int level)
    {
        try { return LevelRangeXpRates.BotRate(level); }
        catch { return Properties.BOT_XP_RATE; }
    }

    private static void OnTitle(GameBot bot, eCraftingSkill order, int rung)
    {
        if (rung <= 0)
            return; // Helper is where everyone starts
        string title = BotCraftingRules.Title(order, rung);
        BotCrafterRoster.Instance.NoteTitle(bot.DatabaseID, title);
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftTitle, ("bot", bot.Name), ("id", bot.DatabaseID), ("level", (int)bot.Level),
                ("realm", GlobalConstants.RealmToName(bot.Realm)), ("order", order.ToString()), ("skill", rung), ("title", title)));
        BotCrafterVoice.OnTitle(bot, order, rung, title);
        if (rung >= 1000 && AutonomousLivingServer.FirstsOn)
        {
            string first = BotCraftingRules.LegendaryFirstTitle(order, bot.Realm);
            AutonomousLivingServer.Later(bot, 2_000, () => bot.IsAlive, () =>
            {
                if (AutonomousLivingServer.TryClaimFirst(first, bot, $"Legendary {BotCraftingRules.Profession(order)} in {GlobalConstants.RealmToName(bot.Realm)}") &&
                    Log.IsInfoEnabled)
                    Log.Info(EconomyLog.Line(EconomyLog.CraftFirst, ("bot", bot.Name), ("id", bot.DatabaseID),
                        ("realm", GlobalConstants.RealmToName(bot.Realm)), ("order", order.ToString()), ("title", first)));
            });
        }
    }

    #endregion

    #region Session start and end

    public static void LogSessionStart(GameBot bot, BotCraftingRules.Session session, int stationModel)
    {
        BotCrafterRoster.Instance.NoteSession(bot.DatabaseID);
        if (Log.IsInfoEnabled)
            Log.Info(EconomyLog.Line(EconomyLog.CraftSessionStart, ("bot", bot.Name), ("id", bot.DatabaseID), ("level", (int)bot.Level),
                ("realm", GlobalConstants.RealmToName(bot.Realm)), ("craft", session.Skill.ToString()), ("skill", SkillValue(bot, session.Skill)),
                ("recipe", session.Recipe.ProductName), ("recipeLevel", session.Recipe.Level), ("copies", session.Copies),
                ("materials", session.Recipe.MaterialCostCopper * session.Copies), ("station", stationModel),
                ("zone", bot.CurrentZone?.Description ?? string.Empty), ("order", session.OrderId != null)));
    }

    /// <summary>
    /// The end of a trip that made something (True165's FinishSession, on real items): the best piece (BestProductIndex)
    /// is the one listed, enchanted first when the realm's spellcrafter and alchemist gamebots can (BotEnchanting); the
    /// others become seconds, sold to a vendor at the inventory stop. Then the CRAFT_SESSION_END line and, now and then,
    /// a true /trade advert.
    /// </summary>
    public static void FinishSession(GameBot bot, BotCraftingRules.Session session, IReadOnlyList<DbInventoryItem> made,
        int attempts, string reason)
    {
        made ??= [];
        int skill = session == null ? 0 : SkillValue(bot, session.Skill);
        DbInventoryItem best = null;
        BotEnchanting.Result enchant = null;
        // Only what is still in the bag: a piece the bot put on (an upgrade) is worn, not sold.
        var carried = Backpack(bot).ToHashSet();
        var pieces = made.Where(item => item != null && !item.IsStackable && carried.Contains(item)).ToList();
        if (pieces.Count > 0 && session?.OrderId == null)
        {
            int index = BotCraftingRules.BestProductIndex(pieces.Select(item => (item.IsStackable, item.Quality)).ToList());
            best = index >= 0 ? pieces[index] : null;
            var seconds = Seconds.GetOrAdd(bot.DatabaseID, _ => new HashSet<string>(StringComparer.Ordinal));
            lock (seconds)
            {
                seconds.RemoveWhere(id => Backpack(bot).All(item => item.ObjectId != id));
                foreach (DbInventoryItem piece in pieces)
                    if (!ReferenceEquals(piece, best) && piece.ObjectId != null)
                        seconds.Add(piece.ObjectId);
            }
            if (best != null)
                enchant = BotEnchanting.TryEnchant(bot, best);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
        }
        int masterpieces = made.Count(item => item != null && !item.IsStackable && item.Quality == 100);
        if (Log.IsInfoEnabled && session != null)
            Log.Info(EconomyLog.Line(EconomyLog.CraftSessionEnd, ("bot", bot.Name), ("id", bot.DatabaseID), ("level", (int)bot.Level),
                ("realm", GlobalConstants.RealmToName(bot.Realm)), ("craft", session.Skill.ToString()), ("skill", skill),
                ("recipe", session.Recipe.ProductName), ("attempts", attempts), ("made", made.Count(item => item != null)),
                ("masterpieces", masterpieces), ("best", best?.Name ?? string.Empty), ("bestQuality", best?.Quality ?? 0),
                ("seconds", Math.Max(0, pieces.Count - (best == null ? 0 : 1))), ("enchant", enchant?.Describe() ?? "none"), ("reason", reason)));
        if (best != null)
            BotCrafterVoice.AfterSession(bot, best);
    }

    #endregion

    /// <summary>Tests only: forget the trips, the seconds and the logged gaps.</summary>
    public static void ResetForTests()
    {
        FinishedTrips.Clear();
        Seconds.Clear();
        MissingMasterLogged.Clear();
        _cache = new CatalogCache();
        AllOnSaleForTests = false;
    }
}
