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
/// already sells and buys upgrades, a bot with coin above its reserve dyes what it wears in its colours and has a
/// weapon made to glow. A new piece of gear comes in its plain colour, so it pays to dye it again: coin keeps leaving the
/// economy. Guild emblems on cloaks and shields, companions and players' gear are never touched.
/// The style and the prices are pure (tests); <see cref="TryRestyle"/> is the game glue.
/// </summary>
public static class AutonomousBotCosmetics
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public enum Vanity { Plain, Tidy, Flashy, Peacock }

    public enum Hue { White, Red, Blue, Green, Yellow, Purple, Violet, Orange, Turquoise, Black, Brown, Gray }

    /// <summary>What a dye can colour: cloth (and cloaks), leather (leather, studded, reinforced), metal (chain, plate, scale).</summary>
    public enum Material { Cloth, Leather, Metal }

    /// <summary>A bot's look: how much it cares, its two colours, and a seed that picks the exact shade and glow.</summary>
    public readonly record struct Style(Vanity Vanity, Hue Primary, Hue Secondary, int Seed);

    #region The dye colours (the client's colour ids, as the game's own Effect Master lists them)
    // Ids 0-7 and 51-87 are general dyes for any material; 27-44 cloth only; 8-15 leather only; 16-26 and 45-50 metal
    // only. The crafter-only and ship colours are left out.
    private static readonly (int Id, Hue Hue)[] Colours =
    [
        (0, Hue.White), (1, Hue.Red), (2, Hue.Green), (3, Hue.Blue), (4, Hue.Yellow), (5, Hue.Purple), (6, Hue.Gray),
        (7, Hue.Turquoise),
        (8, Hue.Yellow), (9, Hue.Red), (10, Hue.Green), (11, Hue.Orange), (12, Hue.Violet), (13, Hue.Green), (14, Hue.Blue),
        (15, Hue.Purple),
        (16, Hue.Brown), (17, Hue.Gray), (18, Hue.Gray), (19, Hue.Gray), (20, Hue.Gray), (21, Hue.White), (22, Hue.Gray),
        (23, Hue.Black), (24, Hue.Gray), (25, Hue.Black), (26, Hue.Gray),
        (27, Hue.Red), (28, Hue.Orange), (29, Hue.Orange), (30, Hue.Yellow), (31, Hue.Green), (32, Hue.Green),
        (33, Hue.Turquoise), (34, Hue.Turquoise), (35, Hue.Blue), (36, Hue.Blue), (37, Hue.Violet), (38, Hue.Violet),
        (39, Hue.Violet), (40, Hue.Purple), (41, Hue.Purple), (42, Hue.Red), (43, Hue.Black), (44, Hue.Brown),
        (45, Hue.Blue), (46, Hue.Green), (47, Hue.Yellow), (48, Hue.Yellow), (49, Hue.Red), (50, Hue.Purple),
        (51, Hue.Blue), (52, Hue.Blue), (53, Hue.Blue), (54, Hue.Blue), (55, Hue.Turquoise), (56, Hue.Turquoise),
        (57, Hue.Turquoise), (58, Hue.Turquoise), (59, Hue.Turquoise), (60, Hue.Turquoise), (61, Hue.Brown),
        (62, Hue.Brown), (63, Hue.Brown), (64, Hue.Red), (65, Hue.Red), (66, Hue.Red), (67, Hue.Red), (68, Hue.Green),
        (69, Hue.Green), (70, Hue.Green), (71, Hue.Green), (72, Hue.Gray), (73, Hue.Gray), (74, Hue.Gray),
        (75, Hue.Orange), (76, Hue.Orange), (77, Hue.Orange), (78, Hue.Purple), (79, Hue.Purple), (80, Hue.Purple),
        (81, Hue.Yellow), (82, Hue.Yellow), (83, Hue.Yellow), (84, Hue.Violet), (85, Hue.Violet), (86, Hue.Blue),
        (87, Hue.Purple),
    ];

    public static bool IsGeneral(int id) => id is >= 0 and <= 7 or >= 51 and <= 87;

    public static bool Fits(int id, Material material) => IsGeneral(id) || material switch
    {
        Material.Cloth => id is >= 27 and <= 44,
        Material.Leather => id is >= 8 and <= 15,
        _ => id is >= 16 and <= 26 or >= 45 and <= 50,
    };

    /// <summary>The dye colours of one hue a material takes (never empty for a hue the general dyes cover).</summary>
    public static int[] ColoursOf(Hue hue, Material material) =>
        Colours.Where(c => c.Hue == hue && Fits(c.Id, material)).Select(c => c.Id).ToArray();

    public static Hue? HueOf(int colour)
    {
        foreach ((int id, Hue hue) in Colours)
            if (id == colour) return hue;
        return null;
    }
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
        return new Style(vanity, primary, secondary, (int)(Next(ref state, int.MaxValue)));
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

    /// <summary>The material of a piece a dye can colour, or null (jewellery, weapons, shields, instruments).</summary>
    public static Material? MaterialOf(int objectType, int slot)
    {
        if (slot == (int)eInventorySlot.Cloak) return Material.Cloth;
        return (eObjectType)objectType switch
        {
            eObjectType.Cloth => Material.Cloth,
            eObjectType.Leather or eObjectType.Studded or eObjectType.Reinforced => Material.Leather,
            eObjectType.Chain or eObjectType.Plate or eObjectType.Scale => Material.Metal,
            _ => null,
        };
    }

    public static readonly eInventorySlot[] DyedSlots =
    [
        eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.Cloak, eInventorySlot.ArmsArmor,
        eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.HeadArmor,
    ];

    /// <summary>
    /// The colour a bot wants on a piece: its main colour on the chest, legs and cloak; its second colour on the arms,
    /// hands, feet and helm when it is flashy or a peacock (a tidy bot wears one colour). Null: it doesn't dye.
    /// </summary>
    public static int? WantedColour(Style style, eInventorySlot slot, Material material)
    {
        if (style.Vanity == Vanity.Plain) return null;
        bool main = slot is eInventorySlot.TorsoArmor or eInventorySlot.LegsArmor or eInventorySlot.Cloak;
        Hue hue = main || style.Vanity == Vanity.Tidy ? style.Primary : style.Secondary;
        int[] shades = ColoursOf(hue, material);
        if (shades.Length == 0) shades = ColoursOf(Hue.Gray, material);
        // One shade per bot and hue, so its pieces match.
        return shades[(int)((uint)(style.Seed ^ (int)hue * 7919) % (uint)shades.Length)];
    }

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
    /// <summary>A dye for one piece, in copper: cloth 50 silver, leather 1 gold, metal (enamel) 2 gold at level 50, less
    /// for lower levels (never under a tenth).</summary>
    public static long DyePrice(Material material, int level, int pricePercent = 100) =>
        Scale(material switch { Material.Cloth => 5_000, Material.Leather => 10_000, _ => 20_000 }, level, pricePercent);

    /// <summary>A weapon glow, in copper: 50 gold at level 50.</summary>
    public static long GlowPrice(int level, int pricePercent = 100) => Scale(500_000, level, pricePercent);

    private static long Scale(long levelFifty, int level, int pricePercent) =>
        Math.Max(1, levelFifty * Math.Max(10, Math.Clamp(level, 1, 50) * 2) / 100 * Math.Clamp(pricePercent, 1, 1000) / 100);

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

    /// <summary>How many pieces a bot has done at one visit.</summary>
    public static int PiecesPerVisit(Vanity vanity) => vanity switch { Vanity.Peacock => 5, Vanity.Flashy => 3, Vanity.Tidy => 2, _ => 0 };
    #endregion

    #region The game glue
    public static bool On => Properties.BOT_COSMETICS;

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
    /// At a merchant, between tasks: dyes and glows what the bot wears in its style, as far as its budget goes. True when
    /// it bought something (<paramref name="done"/> says what).
    /// </summary>
    public static bool TryRestyle(GameBot bot, GameMerchant merchant, out string done, out long spent)
    {
        done = null;
        spent = 0;
        if (!On || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null || merchant == null ||
            !merchant.IsWithinRadius(bot, Properties.WORLD_PICKUP_DISTANCE))
            return false;
        Style style = StyleOf(bot);
        if (style.Vanity == Vanity.Plain) return false;
        int pricePercent = Properties.BOT_COSMETICS_PRICE_PERCENT;
        long budget = Budget(style.Vanity, AutonomousBotEconomy.GetMoney(bot.DatabaseID), bot.Level);
        int pieces = PiecesPerVisit(style.Vanity);
        var bought = new List<string>();
        // Weapons first for a show-off (the glow is what it saves for), then the armour, chest first.
        IEnumerable<eInventorySlot> order = style.Vanity == Vanity.Tidy ? DyedSlots :
            new[] { eInventorySlot.RightHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.LeftHandWeapon }.Concat(DyedSlots);
        foreach (eInventorySlot slot in order)
        {
            if (bought.Count >= pieces) break;
            DbInventoryItem item = bot.Inventory.GetItem(slot);
            if (item?.Template == null || item.IsStackable) continue;
            if (slot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon or eInventorySlot.LeftHandWeapon)
            {
                if (item.Effect != 0 || WantedGlow(style, slot, item.Object_Type, bot.Level) is not int glow) continue;
                long price = GlowPrice(bot.Level, pricePercent);
                if (price > budget || !TryGlow(bot, item, slot, glow, price)) continue;
                budget -= price;
                spent += price;
                bought.Add($"{item.Name}: glow {glow}");
                LogRestyle(bot, style, slot, item.Name, "glow", glow, price);
                continue;
            }
            // A guild emblem stays: the cloak and shield show it (AutonomousBotGuilds).
            if (AutonomousBotGuilds.CarriesEmblem(item) && AutonomousBotGuilds.ShownGuild(bot)?.Emblem is > 0) continue;
            if (item.Emblem != 0 || MaterialOf(item.Object_Type, (int)slot) is not Material material) continue;
            if (Quests.Hibernia.SluaghbinderEpicArmor.IsSetItem(item)) continue;
            if (WantedColour(style, slot, material) is not int colour || item.Color == colour) continue;
            long dye = DyePrice(material, bot.Level, pricePercent);
            if (dye > budget || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, dye)) continue;
            item.Color = colour;
            budget -= dye;
            spent += dye;
            bought.Add($"{item.Name}: dye {colour}");
            LogRestyle(bot, style, slot, item.Name, "dye", colour, dye);
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

    private static void LogRestyle(GameBot bot, Style style, eInventorySlot slot, string item, string kind, int value, long copper)
    {
        if (!Log.IsInfoEnabled) return;
        Log.Info($"BOT_COSMETIC bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} realm={bot.Realm} vanity={style.Vanity} " +
                 $"colours={style.Primary}/{style.Secondary} slot={slot} item=\"{item}\" kind={kind} value={value} copper={copper} " +
                 $"moneyLeft={AutonomousBotEconomy.GetMoney(bot.DatabaseID)}");
    }
    #endregion
}
