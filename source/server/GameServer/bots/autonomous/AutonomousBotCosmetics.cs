using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bot cosmetics (owner 2026-11-12): gamebots spend the coin they earn on how they look, not only on gear. Each bot has
/// its own style, fixed by who it is: how much it cares (plain, tidy, flashy, peacock) and two colours leaning on its
/// realm's colours (guild members often wear their guild's colour). At a merchant, at the natural task break where it
/// already sells and buys upgrades, a bot with coin above its reserve dyes what it wears in its colours and colours its
/// weapon with a weapon luster; the show-offs also have a weapon made to glow.
/// <para>Prices are the game's own (owner 2026-11-13: period-accurate, no invented economy): every dye, enamel and weapon
/// luster is a real item template in the world database, and a bot pays that item's price and uses only the colours a
/// real dye of that kind exists in. Light shades cost copper, royal and black ones over 16 gold, a weapon luster about
/// 196 gold. Weapon glows are not a 1.65 purchase (the owner asked for them); they cost what a weapon luster costs.</para>
/// New gear comes in its plain colour, so the bot pays to dye it again. Guild emblems on cloaks and shields, companions
/// and players' gear are never touched. The style and the choices are pure (tests); <see cref="TryRestyle"/> is the glue.
/// </summary>
public static class AutonomousBotCosmetics
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public enum Vanity { Plain, Tidy, Flashy, Peacock }

    public enum Hue { White, Red, Blue, Green, Yellow, Purple, Violet, Orange, Turquoise, Black, Brown, Gray }

    /// <summary>What a dye colours: cloth dye (cloth, cloaks), leather dye (leather, studded, reinforced), enamel (chain,
    /// plate, scale), and weapon luster (weapons).</summary>
    public enum Material { Cloth, Leather, Metal, Weapon }

    /// <summary>A bot's look: how much it cares, its two colours, and a seed that picks the exact shade and glow.</summary>
    public readonly record struct Style(Vanity Vanity, Hue Primary, Hue Secondary, int Seed);

    #region The dyes (the world database's own dye items)
    /// <summary>A dye a bot can buy: its material, the client colour it gives and the item's price in copper.</summary>
    public readonly record struct Dye(Material Material, int Colour, long Price, string Name);

    /// <summary>The dye item models: cloth dye 229, leather dye 494, enamel 495, weapon luster 538.</summary>
    public static Material? MaterialOfDyeModel(int model) => model switch
    {
        229 => Material.Cloth, 494 => Material.Leather, 495 => Material.Metal, 538 => Material.Weapon, _ => null,
    };

    /// <summary>
    /// The dyes from the world's item templates: one per material and colour, the cheapest. Ship paints, bleaches and
    /// removers, the odd colourless ones and the custom "Weapon Enamel" items are left out.
    /// </summary>
    public static Dye[] BuildCatalog(IEnumerable<(int Model, int Colour, long Price, string Name)> templates) =>
        templates
            .Where(t => t.Colour > 0 && t.Price > 0 && t.Name != null && MaterialOfDyeModel(t.Model) is not null &&
                        !t.Name.Contains("paint", StringComparison.OrdinalIgnoreCase) &&
                        !t.Name.Contains("bleach", StringComparison.OrdinalIgnoreCase) &&
                        !t.Name.Contains("remover", StringComparison.OrdinalIgnoreCase) &&
                        (t.Model != 538 || t.Name.Contains("luster", StringComparison.OrdinalIgnoreCase)) &&
                        HueOf(t.Colour) is not null)
            .GroupBy(t => (Material: MaterialOfDyeModel(t.Model)!.Value, t.Colour))
            .Select(g => g.OrderBy(t => t.Price).First() is var cheapest
                ? new Dye(g.Key.Material, g.Key.Colour, cheapest.Price, cheapest.Name) : default)
            .OrderBy(d => d.Material).ThenBy(d => d.Colour)
            .ToArray();

    // The hue of each client colour a dye gives (the Effect Master's colour names, and the dye items' own names).
    private static readonly Dictionary<int, Hue> Hues = new()
    {
        [34] = Hue.Turquoise, [43] = Hue.Black, [49] = Hue.Orange,
        [51] = Hue.Blue, [52] = Hue.Blue, [53] = Hue.Blue, [54] = Hue.Blue, [55] = Hue.Turquoise, [56] = Hue.Turquoise,
        [57] = Hue.Turquoise, [58] = Hue.Turquoise, [59] = Hue.Turquoise, [60] = Hue.Turquoise, [61] = Hue.Brown,
        [62] = Hue.Brown, [63] = Hue.Brown, [64] = Hue.Red, [65] = Hue.Red, [66] = Hue.Red, [67] = Hue.Red,
        [68] = Hue.Green, [69] = Hue.Green, [70] = Hue.Green, [71] = Hue.Green, [72] = Hue.Gray, [73] = Hue.Gray,
        [74] = Hue.Black, [75] = Hue.Orange, [76] = Hue.Orange, [77] = Hue.Orange, [78] = Hue.Purple, [79] = Hue.Purple,
        [80] = Hue.Purple, [81] = Hue.Yellow, [82] = Hue.Yellow, [83] = Hue.Yellow, [84] = Hue.Violet, [85] = Hue.Violet,
        [86] = Hue.Blue, [87] = Hue.Purple, [48] = Hue.Yellow,
        [119] = Hue.Gray, [120] = Hue.Red, [121] = Hue.Purple, [122] = Hue.Violet, [123] = Hue.Violet, [124] = Hue.Violet,
        [125] = Hue.Yellow, [126] = Hue.Yellow, [127] = Hue.Orange, [128] = Hue.Brown, [129] = Hue.Brown, [130] = Hue.Green,
        [131] = Hue.Green, [132] = Hue.Blue, [133] = Hue.Gray, [134] = Hue.Red, [135] = Hue.Red, [136] = Hue.Green,
        [137] = Hue.Green, [138] = Hue.Turquoise, [139] = Hue.Blue, [140] = Hue.Gray, [141] = Hue.Blue, [142] = Hue.Green,
        [143] = Hue.Red,
    };

    public static Hue? HueOf(int colour) => Hues.TryGetValue(colour, out Hue hue) ? hue : null;
    #endregion

    #region The style (pure)
    // How the realms dress: weights of the hues a bot of that realm picks first. One bot in six ignores its realm.
    private static readonly (Hue Hue, int Weight)[] Albion =
        [(Hue.Red, 4), (Hue.Blue, 3), (Hue.White, 2), (Hue.Yellow, 2), (Hue.Gray, 2), (Hue.Black, 1), (Hue.Purple, 1)];
    private static readonly (Hue Hue, int Weight)[] Midgard =
        [(Hue.Blue, 4), (Hue.Gray, 3), (Hue.Brown, 2), (Hue.Red, 2), (Hue.White, 2), (Hue.Turquoise, 1), (Hue.Black, 1)];
    private static readonly (Hue Hue, int Weight)[] Hibernia =
        [(Hue.Green, 4), (Hue.Purple, 3), (Hue.Turquoise, 2), (Hue.Violet, 2), (Hue.Yellow, 1), (Hue.Blue, 1), (Hue.Brown, 1)];

    /// <summary>
    /// A bot's style, the same every time for the same bot: plain 20%, tidy 40%, flashy 30%, peacock 10%. A guild member
    /// wears the guild's colour as its main colour half the time (a guild uniform), else its own.
    /// </summary>
    public static Style StyleOf(long botId, eRealm realm, long guildId = 0)
    {
        ulong state = (ulong)botId * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E5UL;
        int roll = Next(ref state, 100);
        Vanity vanity = roll < 20 ? Vanity.Plain : roll < 60 ? Vanity.Tidy : roll < 90 ? Vanity.Flashy : Vanity.Peacock;
        Hue primary = PickHue(ref state, realm);
        if (guildId != 0 && Next(ref state, 2) == 0)
        {
            ulong guildState = (ulong)guildId * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
            primary = PickHue(ref guildState, realm);
        }
        Hue secondary = PickHue(ref state, realm);
        for (int i = 0; i < 4 && secondary == primary; i++)
            secondary = PickHue(ref state, realm);
        return new Style(vanity, primary, secondary, Next(ref state, int.MaxValue));
    }

    private static Hue PickHue(ref ulong state, eRealm realm)
    {
        if (Next(ref state, 6) == 0)
            return (Hue)Next(ref state, Enum.GetValues<Hue>().Length);
        (Hue Hue, int Weight)[] table = realm switch { eRealm.Midgard => Midgard, eRealm.Hibernia => Hibernia, _ => Albion };
        int pick = Next(ref state, table.Sum(t => t.Weight));
        foreach ((Hue hue, int weight) in table)
        {
            if (pick < weight) return hue;
            pick -= weight;
        }
        return table[0].Hue;
    }

    private static int Next(ref ulong state, int bound)
    {
        // SplitMix64: stable across runs and platforms (unlike string.GetHashCode or Random).
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)Math.Max(1, bound));
    }

    /// <summary>The dye a piece takes, or null (jewellery, shields, instruments).</summary>
    public static Material? MaterialOf(int objectType, int slot)
    {
        if (slot == (int)eInventorySlot.Cloak) return Material.Cloth;
        if (slot is (int)eInventorySlot.RightHandWeapon or (int)eInventorySlot.LeftHandWeapon or
            (int)eInventorySlot.TwoHandWeapon or (int)eInventorySlot.DistanceWeapon)
            return objectType is (int)eObjectType.Shield or (int)eObjectType.Instrument ? null : Material.Weapon;
        return (eObjectType)objectType switch
        {
            eObjectType.Cloth => Material.Cloth,
            eObjectType.Leather or eObjectType.Studded or eObjectType.Reinforced => Material.Leather,
            eObjectType.Chain or eObjectType.Plate or eObjectType.Scale => Material.Metal,
            _ => null,
        };
    }

    public static readonly eInventorySlot[] ArmourSlots =
    [
        eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.Cloak, eInventorySlot.ArmsArmor,
        eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.HeadArmor,
    ];

    public static readonly eInventorySlot[] WeaponSlots =
        [eInventorySlot.RightHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.LeftHandWeapon, eInventorySlot.DistanceWeapon];

    /// <summary>
    /// The dye a bot wants for a piece, from the dyes that exist: its main colour on the chest, legs, cloak and weapons;
    /// its second colour on the arms, hands, feet and helm when it is flashy or a peacock (a tidy bot wears one colour).
    /// A tidy bot takes the cheaper shades of its colour, a peacock the dearer (royal, dark, black), a flashy bot any;
    /// one shade per bot and colour, so its pieces match. Weapon lusters only for flashy bots and peacocks. Null: none.
    /// </summary>
    public static Dye? WantedDye(Style style, eInventorySlot slot, Material material, IReadOnlyList<Dye> catalog)
    {
        if (style.Vanity == Vanity.Plain || catalog == null) return null;
        if (material == Material.Weapon && style.Vanity == Vanity.Tidy) return null;
        bool main = material == Material.Weapon || slot is eInventorySlot.TorsoArmor or eInventorySlot.LegsArmor or eInventorySlot.Cloak;
        Hue hue = main || style.Vanity == Vanity.Tidy ? style.Primary : style.Secondary;
        Dye[] shades = Shades(catalog, material, hue);
        if (shades.Length == 0) shades = Shades(catalog, material, Hue.Gray);
        if (shades.Length == 0) return null;
        int half = (shades.Length + 1) / 2;
        Dye[] range = style.Vanity switch
        {
            Vanity.Tidy => shades[..half],
            Vanity.Peacock => shades[(shades.Length - half)..],
            _ => shades,
        };
        return range[(int)((uint)(style.Seed ^ (int)hue * 7919 ^ (int)material * 104729) % (uint)range.Length)];
    }

    private static Dye[] Shades(IReadOnlyList<Dye> catalog, Material material, Hue hue) =>
        catalog.Where(d => d.Material == material && HueOf(d.Colour) == hue).OrderBy(d => d.Price).ThenBy(d => d.Colour).ToArray();

    /// <summary>The weapon glows a weapon type takes (the game's own Effect Master list), or empty.</summary>
    public static int[] GlowsFor(int objectType) => (eObjectType)objectType switch
    {
        eObjectType.SlashingWeapon or eObjectType.Sword or eObjectType.Blades or eObjectType.ThrustWeapon or
            eObjectType.Piercing or eObjectType.LeftAxe => [.. Enumerable.Range(1, 20), .. Enumerable.Range(67, 7)],
        eObjectType.TwoHandedWeapon or eObjectType.LargeWeapons => [.. Enumerable.Range(21, 11), 33],
        eObjectType.CrushingWeapon or eObjectType.Blunt or eObjectType.Hammer => [.. Enumerable.Range(34, 22)],
        eObjectType.Axe => [.. Enumerable.Range(56, 11)],
        eObjectType.Spear or eObjectType.CelticSpear or eObjectType.PolearmWeapon => [.. Enumerable.Range(74, 16)],
        eObjectType.Staff => [.. Enumerable.Range(90, 5), .. Enumerable.Range(44, 12)],
        _ => [],
    };

    /// <summary>The glow a bot wants on a weapon, or null: a flashy bot glows its main weapon, a peacock every weapon.</summary>
    public static int? WantedGlow(Style style, eInventorySlot slot, int objectType, int level)
    {
        if (level < MinimumGlowLevel || style.Vanity is Vanity.Plain or Vanity.Tidy) return null;
        bool main = slot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon;
        if (!main && style.Vanity != Vanity.Peacock) return null;
        int[] glows = GlowsFor(objectType);
        return glows.Length == 0 ? null : glows[(int)((uint)(style.Seed * 31 + objectType) % (uint)glows.Length)];
    }

    public const int MinimumGlowLevel = 20;
    #endregion

    #region The prices and the budget (pure)
    /// <summary>What a bot pays for a dye: the item's own price (bot_cosmetics_price_percent scales it, 100 = as the game).</summary>
    public static long PriceOf(Dye dye, int pricePercent = 100) =>
        Math.Max(1, dye.Price * Math.Clamp(pricePercent, 1, 1000) / 100);

    /// <summary>A weapon glow (not a 1.65 purchase): what the dearest weapon luster costs.</summary>
    public static long GlowPrice(IReadOnlyList<Dye> catalog, int pricePercent = 100)
    {
        long luster = catalog?.Where(d => d.Material == Material.Weapon).Select(d => d.Price).DefaultIfEmpty(0).Max() ?? 0;
        return luster <= 0 ? 0 : Math.Max(1, luster * Math.Clamp(pricePercent, 1, 1000) / 100);
    }

    /// <summary>The coin a bot keeps for its gear and repairs and never spends on looks: 5 gold a level.</summary>
    public static long Reserve(int level) => Math.Clamp(level, 1, 50) * 50_000L;

    /// <summary>What a bot may spend on looks at one visit: a share of its coin above the reserve (tidy a tenth, flashy a
    /// quarter, a peacock half).</summary>
    public static long Budget(Vanity vanity, long money, int level)
    {
        long surplus = money - Reserve(level);
        if (surplus <= 0) return 0;
        return vanity switch { Vanity.Tidy => surplus / 10, Vanity.Flashy => surplus / 4, Vanity.Peacock => surplus / 2, _ => 0 };
    }

    /// <summary>How many things a bot has done at one visit.</summary>
    public static int PiecesPerVisit(Vanity vanity) => vanity switch { Vanity.Peacock => 5, Vanity.Flashy => 3, Vanity.Tidy => 2, _ => 0 };
    #endregion

    #region The game glue
    public static bool On => Properties.BOT_COSMETICS;

    private static Dye[] _catalog;
    private static readonly object CatalogLock = new();

    /// <summary>The dyes in this world's database, read once.</summary>
    public static IReadOnlyList<Dye> Catalog
    {
        get
        {
            if (_catalog != null) return _catalog;
            lock (CatalogLock)
            {
                if (_catalog != null) return _catalog;
                try
                {
                    _catalog = BuildCatalog(GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Model").IsIn(new[] { 229, 494, 495, 538 }))
                        .Select(t => (t.Model, t.Color, t.Price, t.Name)));
                }
                catch (Exception exception)
                {
                    Log.Warn("BOT_COSMETIC could not read the dye items; bots won't buy dyes", exception);
                    _catalog = [];
                }
                if (Log.IsInfoEnabled)
                    Log.Info($"BOT_COSMETIC_CATALOG dyes={_catalog.Length} cloth={_catalog.Count(d => d.Material == Material.Cloth)} " +
                             $"leather={_catalog.Count(d => d.Material == Material.Leather)} enamel={_catalog.Count(d => d.Material == Material.Metal)} " +
                             $"lusters={_catalog.Count(d => d.Material == Material.Weapon)}");
                return _catalog;
            }
        }
    }

    public static Style StyleOf(GameBot bot) =>
        StyleOf(bot.DatabaseID, bot.Realm, AutonomousBotGuilds.GuildOf(bot.DatabaseID) is { } guild ? StableGuildKey(guild) : 0);

    private static long StableGuildKey(Guild guild)
    {
        // The guild's id is a string; a stable FNV-1a hash of it (string.GetHashCode changes every run).
        ulong hash = 14695981039346656037UL;
        foreach (char c in guild.GuildID ?? guild.Name ?? "")
            hash = (hash ^ c) * 1099511628211UL;
        return (long)(hash & 0x7FFFFFFFFFFFFFFF) | 1;
    }

    /// <summary>
    /// At a merchant, between tasks: dyes what the bot wears and colours and glows its weapons in its style, as far as its
    /// budget goes. True when it bought something (<paramref name="done"/> says what).
    /// </summary>
    public static bool TryRestyle(GameBot bot, GameMerchant merchant, out string done, out long spent)
    {
        done = null;
        spent = 0;
        if (!On || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null || merchant == null ||
            !merchant.IsWithinRadius(bot, Properties.WORLD_PICKUP_DISTANCE))
            return false;
        Style style = StyleOf(bot);
        IReadOnlyList<Dye> catalog = Catalog;
        if (style.Vanity == Vanity.Plain || catalog.Count == 0) return false;
        int pricePercent = Properties.BOT_COSMETICS_PRICE_PERCENT;
        long budget = Budget(style.Vanity, AutonomousBotEconomy.GetMoney(bot.DatabaseID), bot.Level);
        int pieces = PiecesPerVisit(style.Vanity);
        var bought = new List<string>();
        // Weapons first for a show-off, then the armour, chest first.
        IEnumerable<eInventorySlot> order = style.Vanity == Vanity.Tidy ? ArmourSlots : WeaponSlots.Concat(ArmourSlots);
        foreach (eInventorySlot slot in order)
        {
            if (bought.Count >= pieces) break;
            DbInventoryItem item = bot.Inventory.GetItem(slot);
            if (item?.Template == null || item.IsStackable) continue;
            // A guild emblem stays: the cloak and shield show it (AutonomousBotGuilds).
            if (AutonomousBotGuilds.CarriesEmblem(item) && AutonomousBotGuilds.ShownGuild(bot)?.Emblem is > 0) continue;
            if (item.Emblem != 0 || Quests.Hibernia.SluaghbinderEpicArmor.IsSetItem(item)) continue;
            if (MaterialOf(item.Object_Type, (int)slot) is not Material material) continue;

            if (WantedDye(style, slot, material, catalog) is Dye dye && item.Color != dye.Colour)
            {
                long price = PriceOf(dye, pricePercent);
                if (price <= budget && AutonomousBotEconomy.TrySpend(bot.DatabaseID, price))
                {
                    item.Color = dye.Colour;
                    budget -= price;
                    spent += price;
                    bought.Add($"{item.Name}: {dye.Name}");
                    LogRestyle(bot, style, slot, item.Name, material == Material.Weapon ? "luster" : "dye", dye.Colour, dye.Name, price);
                    if (bought.Count >= pieces) break;
                }
            }
            if (material == Material.Weapon && item.Effect == 0 &&
                WantedGlow(style, slot, item.Object_Type, bot.Level) is int glow)
            {
                long price = GlowPrice(catalog, pricePercent);
                if (price > 0 && price <= budget && TryGlow(bot, item, slot, glow, price))
                {
                    budget -= price;
                    spent += price;
                    bought.Add($"{item.Name}: glow {glow}");
                    LogRestyle(bot, style, slot, item.Name, "glow", glow, "weapon glow", price);
                }
            }
        }
        if (bought.Count == 0) return false;
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        bot.BroadcastLivingEquipmentUpdate();
        done = string.Join(", ", bought);
        return true;
    }

    /// <summary>A glow is on the item's template, shared by every copy: the bot's piece becomes its own unique item
    /// (as the Effect Master does for players) and goes back on in the same slot.</summary>
    private static bool TryGlow(GameBot bot, DbInventoryItem item, eInventorySlot slot, int glow, long price)
    {
        DbItemUnique unique;
        try
        {
            unique = new DbItemUnique(item.Template) { Effect = glow };
        }
        catch (ArgumentException)
        {
            return false;
        }
        DbInventoryItem glowing = GameInventoryItem.Create(unique);
        if (glowing == null) return false;
        glowing.Color = item.Color;
        glowing.Emblem = item.Emblem;
        glowing.Extension = item.Extension;
        glowing.Condition = item.Condition;
        glowing.Durability = item.Durability;
        glowing.Creator = item.Creator;
        glowing.IsCrafted = item.IsCrafted;
        glowing.Charges = item.Charges;
        glowing.Count = 1;
        if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID, price)) return false;
        if (!bot.Inventory.RemoveItem(item))
        {
            AutonomousBotEconomy.AddMoney(bot.DatabaseID, price);
            return false;
        }
        if (!bot.Inventory.AddItem(slot, glowing))
        {
            // Put the old one back; the coin goes back too.
            bot.Inventory.AddItem(slot, item);
            AutonomousBotEconomy.AddMoney(bot.DatabaseID, price);
            return false;
        }
        bot.RefreshItemBonuses();
        return true;
    }

    private static void LogRestyle(GameBot bot, Style style, eInventorySlot slot, string item, string kind, int value, string what, long copper)
    {
        if (!Log.IsInfoEnabled) return;
        Log.Info($"BOT_COSMETIC bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                 $"vanity={style.Vanity} colours={style.Primary}/{style.Secondary} slot={(int)slot} item=\"{item}\" kind={kind} " +
                 $"value={value} bought=\"{what}\" copper={copper} moneyLeft={AutonomousBotEconomy.GetMoney(bot.DatabaseID)}");
    }
    #endregion
}
