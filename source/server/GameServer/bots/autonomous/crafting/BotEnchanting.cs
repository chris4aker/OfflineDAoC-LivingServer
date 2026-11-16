using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Crafter gamebots have the best piece of a session enchanted by their realm's spellcrafter and alchemist gamebots,
/// who must have the skill and are paid (Crafting 2.0 WP4). Ported from shadowofze's True165 (BotEnchant165,
/// 5f31c2b → a8c793e), GPL-3.0: the gems and the tincture come from real recipes the enchanters can make, with the real
/// gem bonuses and imbue limits (the item's maximum from its level and quality, the spellcrafter's cap of skill / 20, no
/// overcharge, so nothing ever explodes) and the real tincture effects (a proc, or charges by quality); the crafter pays
/// every recipe's materials plus a 10% fee to each enchanter, and the enchanters gain skill as for the gems or the
/// tincture they made.
/// <para>Changes in the port: the imbue arithmetic is <see cref="SpellcraftRules"/> (not True165's copy); the recipes are
/// the registered catalog (WP1); the enchanters are crafters of the roster (Crafter-type gamebots whose order is
/// spellcraft or alchemy) and gain by the player's chance (CraftRules) at the bot pace; gated by bot_crafters instead of
/// True 1.65 Mode.</para>
/// </summary>
public static class BotEnchanting
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotEnchanting));

    public const int MinItemLevel = 15;
    public const int MinQuality = 94;
    public const int FeePercent = 10;
    public const int MaxGemSlots = 4;
    private const long RefreshMilliseconds = 5 * 60_000;

    public enum TinctureKind : byte { WeaponProc = 1, ArmourProc = 2, Charges = 3 }

    /// <summary>One spellcraft gem recipe: its product's bonus, recipe level and material cost.</summary>
    public sealed record GemOption(string RecipeId, string Name, int BonusType, int BonusValue, int Level, long Cost);

    /// <summary>One alchemy tincture recipe with the fields Alchemy.ApplyMagicalEffect copies onto the item.</summary>
    public sealed record TinctureOption(string RecipeId, string Name, int TypeDamage, int ProcSpellId, byte ProcChance,
        int SpellId, int LevelRequirement, int Level, long Cost)
    {
        public TinctureKind Kind => ProcSpellId != 0 ? (TypeDamage == 1 ? TinctureKind.WeaponProc : TinctureKind.ArmourProc) : TinctureKind.Charges;
    }

    /// <summary>What an enchant did (Cost = materials + fees the crafter paid; 0 when nothing was done).</summary>
    public sealed class Result
    {
        public List<GemOption> Gems = new();
        public TinctureOption Tincture;
        public long Materials;
        public long SpellFee;
        public long AlchemyFee;
        public long Cost => Materials + SpellFee + AlchemyFee;
        public bool Done => Gems.Count > 0 || Tincture != null;
        public int Imbue, MaxImbue, MaxCharges;
        public string Spellcrafter = "none", Alchemist = "none";

        /// <summary>The enchant for the CRAFT_SESSION_END line: bonuses, imbue, proc or charges, and who did it.</summary>
        public string Describe()
        {
            if (!Done)
                return "none";
            string bonuses = Gems.Count == 0 ? "none" : string.Join(",", Gems.Select(g => $"{(eProperty)g.BonusType}+{g.BonusValue}"));
            string proc = Tincture == null ? "none" : $"{Tincture.Kind}:{(Tincture.ProcSpellId != 0 ? Tincture.ProcSpellId : Tincture.SpellId)}" +
                (Tincture.Kind == TinctureKind.Charges ? $"x{MaxCharges}" : string.Empty) + $"(lr{Tincture.LevelRequirement})";
            return $"bonuses={bonuses} imbue={Imbue}/{MaxImbue} proc={proc} spellcrafter={Spellcrafter} alchemist={Alchemist} cost={Cost}";
        }
    }

    #region The player's formulas (SpellcraftRules, Alchemy)

    public static int ItemMaxImbue(DbInventoryItem item) => item == null ? 0 : SpellcraftRules.MaxImbuePoints(item.Level, item.Quality);

    /// <summary>Alchemy.GetItemMaxCharges: 2 below 94%, quality - 92 up to 99%, 10 at 100%.</summary>
    public static int MaxCharges(int quality) => quality < 94 ? 2 : quality >= 100 ? 10 : quality - 92;

    /// <summary>The spellcrafter's cap (SpellCrafting.IsAllowedToCombine: the total imbue may not pass skill / 20).</summary>
    public static int SpellcrafterCap(int skill) => Math.Max(0, skill) / 20;

    private static IEnumerable<(int, int)> Existing(DbInventoryItem item) =>
        [(item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3)];

    public static int TotalImbue(DbInventoryItem item, IEnumerable<(int Type, int Value)> gems) =>
        SpellcraftRules.TotalImbuePoints(Existing(item), gems);

    /// <summary>A recipe the crafter can make: its chance to make is not 0 (con below purple).</summary>
    public static bool CanMake(int skill, int recipeLevel) => CraftRules.ChanceToMake(skill, recipeLevel) > 0;

    #endregion

    #region What fits the item (True165)

    /// <summary>A crafted weapon, armour piece, shield or instrument worth enchanting (level 15+, quality 94%+).</summary>
    public static bool IsWorthEnchanting(DbInventoryItem item) =>
        item?.Template != null && item.IsCrafted && !item.IsStackable && item.Level >= MinItemLevel && item.Quality >= MinQuality &&
        (GlobalConstants.IsArmor(item.Object_Type) || GlobalConstants.IsWeapon(item.Object_Type) ||
         item.Object_Type == (int)eObjectType.Instrument);

    public static int FreeGemSlots(DbInventoryItem item)
    {
        if (item == null)
            return 0;
        int used = (item.Bonus1Type != 0 ? 1 : 0) + (item.Bonus2Type != 0 ? 1 : 0) + (item.Bonus3Type != 0 ? 1 : 0) +
                   (item.Bonus4Type != 0 ? 1 : 0);
        return MaxGemSlots - used;
    }

    /// <summary>AdvancedCraftingSkill.IsAllowedToCombine for a tincture, and Alchemy's rule: nothing on an item with a proc or charge.</summary>
    public static bool TinctureFits(DbInventoryItem item, int typeDamage)
    {
        if (item == null || item.ProcSpellID != 0 || item.SpellID != 0)
            return false;
        int type = item.Object_Type;
        if (type == (int)eObjectType.Instrument)
            return typeDamage == 0;
        return typeDamage switch
        {
            0 => GlobalConstants.IsArmor(type) || type == (int)eObjectType.Shield,
            1 => GlobalConstants.IsWeapon(type),
            _ => false,
        };
    }

    /// <summary>The bonuses a spellcrafter would put on this item, best first: two main ones, then a pair of four extras.</summary>
    public static int[] WantedBonuses(DbInventoryItem item, eRealm realm, int variant)
    {
        if (item == null)
            return [];
        int acuity = realm == eRealm.Midgard ? (int)eProperty.Piety : (int)eProperty.Intelligence;
        const int str = (int)eProperty.Strength, dex = (int)eProperty.Dexterity, con = (int)eProperty.Constitution,
            qui = (int)eProperty.Quickness, hp = (int)eProperty.MaxHealth, cha = (int)eProperty.Charisma;
        const int crush = (int)eProperty.Resist_Crush, slash = (int)eProperty.Resist_Slash, thrust = (int)eProperty.Resist_Thrust;
        int[] list = (eObjectType)item.Object_Type switch
        {
            eObjectType.Cloth or eObjectType.Staff => [acuity, dex, con, hp, slash, crush],
            eObjectType.Instrument => [cha, dex, con, qui, hp, slash],
            eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Crossbow or
                eObjectType.Fired => [dex, qui, con, str, hp, thrust],
            _ when GlobalConstants.IsArmor(item.Object_Type) || item.Object_Type == (int)eObjectType.Shield =>
                [str, con, dex, qui, hp, crush],
            _ => [str, qui, con, dex, hp, slash],
        };
        (int A, int B)[] pairs = [(2, 3), (2, 4), (3, 4), (2, 5), (3, 5), (4, 5)];
        (int a, int b) = pairs[Math.Abs(variant) % pairs.Length];
        return [list[0], list[1], list[a], list[b]];
    }

    #endregion

    #region Planning (pure)

    /// <summary>
    /// The gems to put on the item (True165's PickGems): one per free slot, bonus types not already on it, each from a
    /// recipe the spellcrafter can make, the total imbue within the item's maximum (no overcharge) and the spellcrafter's
    /// cap, the materials plus the fee within the budget; tiers raised evenly. Best bonus first; empty when nothing fits.
    /// </summary>
    public static List<GemOption> PickGems(DbInventoryItem item, IReadOnlyList<int> wanted,
        IReadOnlyDictionary<int, GemOption[]> gemsByType, int spellcraftSkill, long budget)
    {
        var picked = new List<GemOption>();
        int slots = FreeGemSlots(item);
        if (item == null || wanted == null || gemsByType == null || slots <= 0 || budget <= 0)
            return picked;
        int limit = Math.Min(ItemMaxImbue(item), SpellcrafterCap(spellcraftSkill));
        if (limit <= 0)
            return picked;

        var present = new HashSet<int> { item.Bonus1Type, item.Bonus2Type, item.Bonus3Type, item.Bonus4Type };
        var types = new List<GemOption[]>();
        foreach (int type in wanted)
        {
            if (types.Count >= slots)
                break;
            if (type == 0 || present.Contains(type) || !gemsByType.TryGetValue(type, out GemOption[] all))
                continue;
            GemOption[] makeable = all.Where(g => CanMake(spellcraftSkill, g.Level)).OrderBy(g => g.BonusValue).ToArray();
            if (makeable.Length == 0)
                continue;
            present.Add(type);
            types.Add(makeable);
        }
        if (types.Count == 0)
            return picked;

        int[] tier = Enumerable.Repeat(-1, types.Count).ToArray();
        bool raised = true;
        while (raised)
        {
            raised = false;
            foreach (int i in Enumerable.Range(0, types.Count).OrderBy(i => tier[i]).ThenBy(i => i))
            {
                if (tier[i] + 1 >= types[i].Length)
                    continue;
                tier[i]++;
                if (Fits(item, types, tier, limit, budget))
                {
                    raised = true;
                    break;
                }
                tier[i]--;
            }
        }
        for (int i = 0; i < types.Count; i++)
            if (tier[i] >= 0)
                picked.Add(types[i][tier[i]]);
        return picked;
    }

    private static bool Fits(DbInventoryItem item, List<GemOption[]> types, int[] tier, int limit, long budget)
    {
        var gems = new List<GemOption>(types.Count);
        for (int i = 0; i < types.Count; i++)
            if (tier[i] >= 0)
                gems.Add(types[i][tier[i]]);
        long materials = gems.Sum(g => g.Cost);
        return TotalImbue(item, gems.Select(g => (g.BonusType, g.BonusValue))) <= limit && materials + Fee(materials) <= budget;
    }

    /// <summary>
    /// The tincture to put on the item (True165's PickTincture): one that fits it, from a recipe the alchemist can make,
    /// whose level requirement is not above the item's level, within the budget. A proc beats charges; then the highest
    /// level requirement; ties broken by the roll.
    /// </summary>
    public static TinctureOption PickTincture(DbInventoryItem item, IReadOnlyList<TinctureOption> tinctures, int alchemySkill,
        long budget, int roll)
    {
        if (item == null || tinctures == null || budget <= 0)
            return null;
        bool weapon = GlobalConstants.IsWeapon(item.Object_Type) && item.Object_Type != (int)eObjectType.Shield;
        TinctureKind preferred = weapon ? TinctureKind.WeaponProc : TinctureKind.ArmourProc;
        TinctureOption[] fitting = tinctures
            .Where(t => TinctureFits(item, t.TypeDamage) && t.LevelRequirement <= item.Level && CanMake(alchemySkill, t.Level) &&
                        t.Cost + Fee(t.Cost) <= budget)
            .ToArray();
        if (fitting.Length == 0)
            return null;
        int Rank(TinctureOption t) => t.Kind == preferred ? 2 : t.Kind == TinctureKind.Charges ? 0 : 1;
        int bestRank = fitting.Max(Rank);
        TinctureOption[] best = fitting.Where(t => Rank(t) == bestRank).ToArray();
        int bestLevel = best.Max(t => t.LevelRequirement);
        best = best.Where(t => t.LevelRequirement == bestLevel).OrderBy(t => t.RecipeId, StringComparer.Ordinal).ToArray();
        return best[Math.Abs(roll) % best.Length];
    }

    /// <summary>The enchanter's fee: 10% of the materials (at least 1 copper when there are materials).</summary>
    public static long Fee(long materials) => materials <= 0 ? 0 : Math.Max(1, materials * FeePercent / 100);

    /// <summary>Chance (percent) a crafter has a fresh piece enchanted: 15% plus 1% per 5 gold it holds, at most 75%.</summary>
    public static int EnchantChance(long money) => (int)Math.Clamp(15 + Math.Max(0, money) / 50_000, 15, 75);

    #endregion

    #region Applying (exactly as the player combines do)

    /// <summary>SpellCrafting.ApplySpellcraftGems (success): each gem's bonus into the next empty slot.</summary>
    public static void ApplyGems(DbInventoryItem item, IEnumerable<GemOption> gems)
    {
        foreach (GemOption gem in gems)
        {
            if (item.Bonus1Type == 0) { item.Bonus1Type = gem.BonusType; item.Bonus1 = gem.BonusValue; }
            else if (item.Bonus2Type == 0) { item.Bonus2Type = gem.BonusType; item.Bonus2 = gem.BonusValue; }
            else if (item.Bonus3Type == 0) { item.Bonus3Type = gem.BonusType; item.Bonus3 = gem.BonusValue; }
            else if (item.Bonus4Type == 0) { item.Bonus4Type = gem.BonusType; item.Bonus4 = gem.BonusValue; }
        }
    }

    /// <summary>Alchemy.ApplyMagicalEffect: a proc (and its chance), or charges by quality and the spell; the level requirement.</summary>
    public static void ApplyTincture(DbInventoryItem item, TinctureOption tincture)
    {
        if (tincture.ProcSpellId != 0)
        {
            item.ProcSpellID = tincture.ProcSpellId;
            if (tincture.ProcChance != 0)
                item.ProcChance = tincture.ProcChance;
        }
        else
        {
            item.MaxCharges = MaxCharges(item.Quality);
            item.Charges = item.MaxCharges;
            item.SpellID = tincture.SpellId;
        }
        item.LevelRequirement = tincture.LevelRequirement;
    }

    #endregion

    #region Recipes (once per realm and catalog)

    private sealed class Catalog
    {
        public IRecipeCatalog Source;
        public Dictionary<int, GemOption[]> Gems = new();
        public TinctureOption[] Tinctures = [];
    }

    private static readonly ConcurrentDictionary<eRealm, Catalog> Catalogs = new();

    private static Catalog CatalogOf(eRealm realm)
    {
        IRecipeCatalog source = RecipeCatalogs.Current;
        if (Catalogs.TryGetValue(realm, out Catalog catalog) && ReferenceEquals(catalog.Source, source))
            return catalog;
        catalog = Load(realm, source);
        Catalogs[realm] = catalog;
        return catalog;
    }

    private static Catalog Load(eRealm realm, IRecipeCatalog source)
    {
        var catalog = new Catalog { Source = source };
        if (!source.IsLoaded)
            return catalog;
        bool allowNoRealm = ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS;
        var gems = new Dictionary<(int Type, int Value), GemOption>();
        foreach (CraftRecipeInfo info in source.ForSkill(eCraftingSkill.SpellCrafting) ?? [])
        {
            DbItemTemplate product = info.Recipe?.Product;
            if (product == null || product.Object_Type != (int)eObjectType.SpellcraftGem || product.Bonus1Type == 0 || product.Bonus1 <= 0 ||
                !BotCraftingRules.RealmAllows(info.Realm, realm, allowNoRealm) || !AutonomousBotCrafting.OnSale(info, realm))
                continue;
            var gem = new GemOption(info.RecipeId, product.Name, product.Bonus1Type, product.Bonus1, info.Level, info.MaterialCostCopper);
            if (!gems.TryGetValue((gem.BonusType, gem.BonusValue), out GemOption old) || gem.Cost < old.Cost)
                gems[(gem.BonusType, gem.BonusValue)] = gem;
        }
        catalog.Gems = gems.Values.GroupBy(g => g.BonusType).ToDictionary(g => g.Key, g => g.OrderBy(x => x.BonusValue).ToArray());

        var tinctures = new List<TinctureOption>();
        foreach (CraftRecipeInfo info in source.ForSkill(eCraftingSkill.Alchemy) ?? [])
        {
            DbItemTemplate product = info.Recipe?.Product;
            if (product == null || product.Object_Type != (int)eObjectType.AlchemyTincture || product.ProcSpellID == 0 && product.SpellID == 0 ||
                !BotCraftingRules.RealmAllows(info.Realm, realm, allowNoRealm) || !AutonomousBotCrafting.OnSale(info, realm))
                continue;
            tinctures.Add(new TinctureOption(info.RecipeId, product.Name, product.Type_Damage, product.ProcSpellID, product.ProcChance,
                product.SpellID, product.LevelRequirement, info.Level, info.MaterialCostCopper));
        }
        catalog.Tinctures = tinctures.ToArray();
        return catalog;
    }

    #endregion

    #region The realm's best enchanters (refreshed every few minutes, from the live bots)

    private sealed record Enchanters(long Time, long SpellcrafterId, long AlchemistId);

    private static readonly ConcurrentDictionary<eRealm, Enchanters> Best = new();
    private static readonly object RefreshLock = new();

    /// <summary>The order of a crafter gamebot, from its skills (the primary first).</summary>
    private static eCraftingSkill OrderOf(GameBot bot) => BotCraftingSkills.Primary(bot?.PersistentRecord?.SerializedCraftingSkills);

    private static (GameBot Spellcrafter, GameBot Alchemist) FindEnchanters(eRealm realm, long now)
    {
        if (!Best.TryGetValue(realm, out Enchanters best) || now >= best.Time + RefreshMilliseconds)
        {
            lock (RefreshLock)
            {
                if (!Best.TryGetValue(realm, out best) || now >= best.Time + RefreshMilliseconds)
                {
                    Refresh(now);
                    Best.TryGetValue(realm, out best);
                }
            }
        }
        return best == null ? (null, null) : (Live(best.SpellcrafterId, eCraftingSkill.SpellCrafting), Live(best.AlchemistId, eCraftingSkill.Alchemy));
    }

    private static void Refresh(long now)
    {
        var spell = new Dictionary<eRealm, (long Id, int Level)>();
        var alch = new Dictionary<eRealm, (long Id, int Level)>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
        {
            if (!AutonomousBotCrafting.IsCrafter(bot))
                continue;
            eCraftingSkill order = OrderOf(bot);
            var table = order == eCraftingSkill.SpellCrafting ? spell : order == eCraftingSkill.Alchemy ? alch : null;
            int level = AutonomousBotCrafting.SkillValue(bot, order);
            if (table != null && level > 0 && (!table.TryGetValue(bot.Realm, out var top) || level > top.Level))
                table[bot.Realm] = (bot.DatabaseID, level);
        }
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            Best[realm] = new Enchanters(now, spell.TryGetValue(realm, out var s) ? s.Id : 0, alch.TryGetValue(realm, out var a) ? a.Id : 0);
    }

    private static GameBot Live(long botId, eCraftingSkill order) =>
        botId > 0 && AutonomousBotRegistry.TryGet(botId, out GameBot bot) && AutonomousBotCrafting.IsCrafter(bot) && OrderOf(bot) == order ? bot : null;

    public static void ClearCache()
    {
        Catalogs.Clear();
        Best.Clear();
    }

    #endregion

    /// <summary>
    /// A crafter's fresh best piece (before it is listed): maybe have it enchanted by the realm's best spellcrafter and
    /// alchemist gamebots, paying materials and fees (True165's TryEnchant). The item must be a unique copy. Returns what
    /// was done (Done false when nothing).
    /// </summary>
    public static Result TryEnchant(GameBot crafter, DbInventoryItem item, Random random = null)
    {
        var result = new Result();
        random ??= Random.Shared;
        if (!AutonomousBotCrafting.Enabled || crafter?.PersistentRecord == null || item?.Template is not DbItemUnique unique ||
            !IsWorthEnchanting(item))
            return result;
        long money = AutonomousBotEconomy.GetMoney(crafter.DatabaseID);
        if (!(EnchantChance(money) > random.Next(100)) || !AutonomousBotEconomy.CanListNow(crafter, item))
            return result;
        (GameBot spellcrafter, GameBot alchemist) = FindEnchanters(crafter.Realm, GameLoop.GameLoopTime);
        if (spellcrafter == crafter)
            spellcrafter = null; // a crafter's own second order is not an enchanter's service
        if (alchemist == crafter)
            alchemist = null;
        if (spellcrafter == null && alchemist == null)
            return result;

        // At most half of its coin: the rest buys its next materials (True165).
        long budget = money / 2;
        Catalog catalog = CatalogOf(crafter.Realm);
        int spellSkill = AutonomousBotCrafting.SkillValue(spellcrafter, eCraftingSkill.SpellCrafting);
        int alchSkill = AutonomousBotCrafting.SkillValue(alchemist, eCraftingSkill.Alchemy);
        List<GemOption> gems = spellcrafter == null ? new List<GemOption>()
            : PickGems(item, WantedBonuses(item, crafter.Realm, random.Next(6)), catalog.Gems, spellSkill, budget);
        long gemMaterials = gems.Sum(g => g.Cost);
        long left = budget - gemMaterials - Fee(gemMaterials);
        TinctureOption tincture = alchemist == null ? null : PickTincture(item, catalog.Tinctures, alchSkill, left, random.Next(1000));
        if (gems.Count == 0 && tincture == null)
            return result;

        long tinctureMaterials = tincture?.Cost ?? 0;
        long spellFee = gems.Count > 0 ? Fee(gemMaterials) : 0, alchFee = tincture != null ? Fee(tinctureMaterials) : 0;
        long total = gemMaterials + tinctureMaterials + spellFee + alchFee;
        if (!AutonomousBotEconomy.TrySpend(crafter.DatabaseID, total, EconomyFlowKind.Enchant))
            return result;
        BotCrafterRoster.Instance.NoteSpent(crafter.DatabaseID, total);

        int imbue = TotalImbue(item, gems.Select(g => (g.BonusType, g.BonusValue)));
        if (gems.Count > 0)
        {
            ApplyGems(item, gems);
            AutonomousBotEconomy.AddMoney(spellcrafter.DatabaseID, spellFee, EconomyFlowKind.Enchant);
            BotCrafterRoster.Instance.NoteEarned(spellcrafter.DatabaseID, spellFee);
            foreach (GemOption gem in gems)
                if (CraftRules.BaseChanceToGainPoint(AutonomousBotCrafting.SkillValue(spellcrafter, eCraftingSkill.SpellCrafting), gem.Level) > random.Next(100))
                    AutonomousBotCrafting.Gain(spellcrafter, eCraftingSkill.SpellCrafting, []);
            spellcrafter.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(spellcrafter);
        }
        if (tincture != null)
        {
            ApplyTincture(item, tincture);
            AutonomousBotEconomy.AddMoney(alchemist.DatabaseID, alchFee, EconomyFlowKind.Enchant);
            BotCrafterRoster.Instance.NoteEarned(alchemist.DatabaseID, alchFee);
            if (CraftRules.BaseChanceToGainPoint(alchSkill, tincture.Level) > random.Next(100))
                AutonomousBotCrafting.Gain(alchemist, eCraftingSkill.Alchemy, []);
            alchemist.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(alchemist);
        }
        SaveUnique(unique);
        crafter.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(crafter, true);

        result.Gems = gems;
        result.Tincture = tincture;
        result.Materials = gemMaterials + tinctureMaterials;
        result.SpellFee = spellFee;
        result.AlchemyFee = alchFee;
        result.Imbue = imbue;
        result.MaxImbue = ItemMaxImbue(item);
        result.Spellcrafter = gems.Count > 0 ? $"{spellcrafter.Name}({spellSkill})" : "none";
        result.Alchemist = tincture != null ? $"{alchemist.Name}({alchSkill})" : "none";
        result.MaxCharges = item.MaxCharges;
        return result;
    }

    /// <summary>An already saved unique piece is saved again off the game loop (the bag's save writes only new definitions).</summary>
    private static void SaveUnique(DbItemUnique unique)
    {
        if (!unique.IsPersisted || GameServer.Database == null)
            return;
        unique.Dirty = true;
        Task.Run(() =>
        {
            try
            {
                using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                    GameServer.Database.SaveObject(unique);
            }
            catch (Exception exception)
            {
                Log.Warn("Bot enchanting: could not save an enchanted piece", exception);
            }
        });
    }
}
