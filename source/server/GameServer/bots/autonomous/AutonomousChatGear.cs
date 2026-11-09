using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Which items a class can use, for chat that pairs an item with a class (Beta 1.1.3, after RC5: "99% adroit focus staff any
/// good for pally?", "95% adroit focus staff any good for friar?", "99% earthshaker chain boots any good for sorc?"). A live
/// bot's own item is checked with the server's rule (<see cref="AbstractServerRules.CheckAbilityToUseItem"/>, from the class's
/// real abilities), see <see cref="AutonomousChatFieldTalk.Of"/>; this is the same rule without a live character: the armour a
/// class wears at a level is the game's own (<see cref="GeneratedUniqueItem.GetAlbionArmorType"/> and its Midgard and Hibernia
/// twins), the weapon lines and shield sizes follow the classes' weapon skills and the random item generator's lists. A focus
/// staff is a caster's weapon: a friar or a cleric can hold a staff, but nobody asks whether a focus staff suits one. Pure.
/// </summary>
public static class AutonomousChatGear
{
    private readonly record struct Kit(eRealm Realm, eObjectType[] Weapons, int Shield, bool Focus);

    private static readonly eObjectType[] Staff = [eObjectType.Staff];

    private static readonly Dictionary<eCharacterClass, Kit> Kits = new()
    {
        // Albion
        [eCharacterClass.Armsman] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.CrushingWeapon,
            eObjectType.TwoHandedWeapon, eObjectType.PolearmWeapon, eObjectType.Crossbow], 3, false),
        [eCharacterClass.Paladin] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.CrushingWeapon,
            eObjectType.TwoHandedWeapon], 3, false),
        [eCharacterClass.Mercenary] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.CrushingWeapon,
            eObjectType.Fired], 2, false),
        [eCharacterClass.Reaver] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.CrushingWeapon,
            eObjectType.Flexible], 3, false),
        [eCharacterClass.Cleric] = new(eRealm.Albion, [eObjectType.CrushingWeapon, eObjectType.Staff], 2, false),
        [eCharacterClass.Friar] = new(eRealm.Albion, [eObjectType.Staff, eObjectType.CrushingWeapon], 1, false),
        [eCharacterClass.Minstrel] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.Instrument], 1, false),
        [eCharacterClass.Scout] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.Longbow], 1, false),
        [eCharacterClass.Infiltrator] = new(eRealm.Albion, [eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.Crossbow], 1, false),
        [eCharacterClass.Cabalist] = new(eRealm.Albion, Staff, 0, true),
        [eCharacterClass.Necromancer] = new(eRealm.Albion, Staff, 0, true),
        [eCharacterClass.Sorcerer] = new(eRealm.Albion, Staff, 0, true),
        [eCharacterClass.Theurgist] = new(eRealm.Albion, Staff, 0, true),
        [eCharacterClass.Wizard] = new(eRealm.Albion, Staff, 0, true),
        // Midgard
        [eCharacterClass.Warrior] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer, eObjectType.Thrown], 3, false),
        [eCharacterClass.Thane] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer, eObjectType.Thrown], 2, false),
        [eCharacterClass.Skald] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer, eObjectType.Thrown], 1, false),
        [eCharacterClass.Berserker] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer, eObjectType.LeftAxe,
            eObjectType.Thrown], 1, false),
        [eCharacterClass.Savage] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer, eObjectType.HandToHand,
            eObjectType.Thrown], 1, false),
        [eCharacterClass.Shadowblade] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Axe, eObjectType.LeftAxe, eObjectType.Thrown], 1, false),
        [eCharacterClass.Hunter] = new(eRealm.Midgard, [eObjectType.Sword, eObjectType.Spear, eObjectType.CompositeBow, eObjectType.Thrown], 0, false),
        [eCharacterClass.Healer] = new(eRealm.Midgard, [eObjectType.Hammer, eObjectType.Staff], 1, false),
        [eCharacterClass.Shaman] = new(eRealm.Midgard, [eObjectType.Hammer, eObjectType.Staff], 1, false),
        [eCharacterClass.Runemaster] = new(eRealm.Midgard, Staff, 0, true),
        [eCharacterClass.Spiritmaster] = new(eRealm.Midgard, Staff, 0, true),
        [eCharacterClass.Bonedancer] = new(eRealm.Midgard, Staff, 0, true),
        // Hibernia
        [eCharacterClass.Hero] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Piercing, eObjectType.LargeWeapons,
            eObjectType.CelticSpear, eObjectType.Fired], 3, false),
        [eCharacterClass.Champion] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Piercing, eObjectType.LargeWeapons], 2, false),
        [eCharacterClass.Blademaster] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Piercing, eObjectType.Fired], 2, false),
        [eCharacterClass.Warden] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Fired], 2, false),
        [eCharacterClass.Druid] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Staff], 1, false),
        [eCharacterClass.Bard] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Blunt, eObjectType.Instrument], 1, false),
        [eCharacterClass.Ranger] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Piercing, eObjectType.RecurvedBow], 1, false),
        [eCharacterClass.Nightshade] = new(eRealm.Hibernia, [eObjectType.Blades, eObjectType.Piercing], 1, false),
        [eCharacterClass.Valewalker] = new(eRealm.Hibernia, [eObjectType.Scythe], 0, false),
        [eCharacterClass.Sluaghbinder] = new(eRealm.Hibernia, [eObjectType.Blunt, eObjectType.Scythe], 3, false),
        [eCharacterClass.Eldritch] = new(eRealm.Hibernia, Staff, 0, true),
        [eCharacterClass.Enchanter] = new(eRealm.Hibernia, Staff, 0, true),
        [eCharacterClass.Mentalist] = new(eRealm.Hibernia, Staff, 0, true),
        [eCharacterClass.Animist] = new(eRealm.Hibernia, Staff, 0, true),
    };

    /// <summary>The classes this table knows (the base classes of every realm).</summary>
    public static IEnumerable<eCharacterClass> Classes => Kits.Keys;

    /// <summary>The class a name stands for ("Paladin", "paladin"), or null.</summary>
    public static eCharacterClass? ClassOf(string className) =>
        !string.IsNullOrWhiteSpace(className) && Enum.TryParse(className.Replace(" ", ""), true, out eCharacterClass cls) && Kits.ContainsKey(cls)
            ? cls
            : null;

    /// <summary>The realm a class belongs to (None for one this table doesn't know).</summary>
    public static eRealm RealmOf(string className) => ClassOf(className) is { } cls ? Kits[cls].Realm : eRealm.None;

    /// <summary>The heaviest armour a class wears at a level, as the game's item generator gives it ("cabalist" is cloth).</summary>
    public static eObjectType ArmorFor(string className, int level)
    {
        if (ClassOf(className) is not { } cls)
            return eObjectType.GenericArmor;
        byte lvl = (byte)Math.Clamp(level, 1, 50);
        return Kits[cls].Realm switch
        {
            eRealm.Midgard => GeneratedUniqueItem.GetMidgardArmorType(cls, lvl),
            eRealm.Hibernia => GeneratedUniqueItem.GetHiberniaArmorType(cls, lvl),
            _ => GeneratedUniqueItem.GetAlbionArmorType(cls, lvl),
        };
    }

    /// <summary>The armour word chat uses for a class at a level ("plate", "chain", "cloth", "reinforced"), or null.</summary>
    public static string ArmorWord(string className, int level) =>
        ClassOf(className) == null ? null : ArmorFor(className, level) switch
        {
            eObjectType.Cloth => "cloth",
            eObjectType.Leather => "leather",
            eObjectType.Studded => "studded",
            eObjectType.Chain => "chain",
            eObjectType.Plate => "plate",
            eObjectType.Reinforced => "reinforced",
            eObjectType.Scale => "scale",
            _ => null,
        };

    // The armour ladder of the server's rule (CheckAbilityToUseItem): reinforced counts as studded, scale as chain.
    private static int ArmorRank(eObjectType type) => type switch
    {
        eObjectType.Cloth => 1,
        eObjectType.Leather => 2,
        eObjectType.Studded or eObjectType.Reinforced => 3,
        eObjectType.Chain or eObjectType.Scale => 4,
        eObjectType.Plate => 5,
        _ => 0,
    };

    private static readonly Regex Focus = new(@"\bfocus\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether an item is a caster's focus staff ("adroit focus staff").</summary>
    public static bool IsFocusStaff(string itemName) => itemName != null && Focus.IsMatch(itemName);

    /// <summary>Whether a class casts from focus lines (the only class a focus staff is for).</summary>
    public static bool IsFocusCaster(string className) => ClassOf(className) is { } cls && Kits[cls].Focus;

    /// <summary>Whether a class shoots a bow (arrows are for it).</summary>
    public static bool UsesBow(string className) => ClassOf(className) is { } cls &&
        Kits[cls].Weapons.Any(type => type is eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Fired);

    /// <summary>
    /// Whether a class can use an item at a level: its armour, weapon line, shield size or instrument. <paramref name="objectType"/>
    /// 0 reads the type from the name (<see cref="TypeFromName"/>). An item of no class's kind (jewelry, a cloak, loot) and an
    /// unknown class are always fine. Pure.
    /// </summary>
    public static bool CanUse(string className, int level, int objectType, string itemName)
    {
        if (ClassOf(className) is not { } cls)
            return true;
        Kit kit = Kits[cls];
        if (IsFocusStaff(itemName))
            return kit.Focus;
        eObjectType type = objectType > 0 ? (eObjectType)objectType : TypeFromName(itemName, kit.Realm);
        if (ArmorRank(type) > 0)
        {
            // Hibernia's ladder is reinforced and scale; the other realms' studded, chain and plate.
            bool hibArmor = type is eObjectType.Reinforced or eObjectType.Scale;
            bool otherArmor = type is eObjectType.Studded or eObjectType.Chain or eObjectType.Plate;
            if (kit.Realm == eRealm.Hibernia ? otherArmor : hibArmor)
                return false;
            return ArmorRank(type) <= ArmorRank(ArmorFor(className, level));
        }
        return type switch
        {
            eObjectType.Shield => kit.Shield > 0 && ShieldSize(itemName) <= kit.Shield,
            eObjectType.Instrument => kit.Weapons.Contains(eObjectType.Instrument),
            >= eObjectType._FirstWeapon and <= eObjectType._LastWeapon => kit.Weapons.Contains(type),
            _ => true,
        };
    }

    private static readonly Regex ItemForClass = new(
        @"\b(?:wtb|wts|selling|buying|looking for|anyone got|anyone selling|anyone have|need|lf) (?<item>[a-z' ]+?) for (?:a |an |my )?(?:lvl |level )?(?<lvl>\d{1,2} )?(?<cls>[a-z]+)\b|" +
        @"^(?:\d+% )?(?<item>[a-z' ]+?) any good for (?:a |an |my )?(?<lvl>\d{1,2} )?(?<cls>[a-z]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Whether a written line pairs an item with a class that can't use it ("wtb a shield for a 30 cabalist" on the owner's PC,
    /// RC3): "X for a 30 cabalist", "X any good for a wizard", the class by its name. Pure.
    /// </summary>
    public static bool PairsUnusable(string text, int level)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (Match match in ItemForClass.Matches(text))
        {
            string cls = match.Groups["cls"].Value;
            if (ClassOf(cls) == null)
                continue;
            int at = int.TryParse(match.Groups["lvl"].Value.Trim(), out int named) ? named : level;
            if (!CanUse(cls, at, 0, match.Groups["item"].Value))
                return true;
        }
        return false;
    }

    /// <summary>A shield's size from its name: 1 small (or unnamed), 2 medium, 3 large or tower.</summary>
    public static int ShieldSize(string itemName)
    {
        string name = itemName?.ToLowerInvariant() ?? string.Empty;
        return name.Contains("large") || name.Contains("tower") ? 3 : name.Contains("medium") ? 2 : 1;
    }

    private static readonly (Regex Words, eObjectType Albion, eObjectType Midgard, eObjectType Hibernia)[] NameTypes =
    [
        (Words(@"focus staff|quarterstaff|staff"), eObjectType.Staff, eObjectType.Staff, eObjectType.Staff),
        (Words("shield|buckler|kite|heater|tower"), eObjectType.Shield, eObjectType.Shield, eObjectType.Shield),
        (Words("lute|drum|flute|harp|lyre"), eObjectType.Instrument, eObjectType.Instrument, eObjectType.Instrument),
        (Words("cloth|quilted|padded|robe"), eObjectType.Cloth, eObjectType.Cloth, eObjectType.Cloth),
        (Words("leather"), eObjectType.Leather, eObjectType.Leather, eObjectType.Leather),
        (Words("studded"), eObjectType.Studded, eObjectType.Studded, eObjectType.Studded),
        (Words("chain|chainmail|mail"), eObjectType.Chain, eObjectType.Chain, eObjectType.Chain),
        (Words("plate"), eObjectType.Plate, eObjectType.Plate, eObjectType.Plate),
        (Words("reinforced"), eObjectType.Reinforced, eObjectType.Reinforced, eObjectType.Reinforced),
        (Words("scale"), eObjectType.Scale, eObjectType.Scale, eObjectType.Scale),
        (Words("longbow"), eObjectType.Longbow, eObjectType.Longbow, eObjectType.Longbow),
        (Words("crossbow"), eObjectType.Crossbow, eObjectType.Crossbow, eObjectType.Crossbow),
        (Words("composite bow|compound bow"), eObjectType.CompositeBow, eObjectType.CompositeBow, eObjectType.CompositeBow),
        (Words("recurve bow|recurved bow"), eObjectType.RecurvedBow, eObjectType.RecurvedBow, eObjectType.RecurvedBow),
        (Words("short bow|shortbow"), eObjectType.Fired, eObjectType.CompositeBow, eObjectType.Fired),
        (Words("scythe"), eObjectType.Scythe, eObjectType.Scythe, eObjectType.Scythe),
        (Words("flail|whip"), eObjectType.Flexible, eObjectType.Flexible, eObjectType.Flexible),
        (Words("claw|claws|fist|knuckles|katar"), eObjectType.HandToHand, eObjectType.HandToHand, eObjectType.HandToHand),
        (Words("halberd|lucerne|lochaber|partisan|bill|glaive|pike|polearm"), eObjectType.PolearmWeapon, eObjectType.Spear, eObjectType.CelticSpear),
        (Words("spear"), eObjectType.PolearmWeapon, eObjectType.Spear, eObjectType.CelticSpear),
        (Words("great sword|greatsword|great axe|great hammer|war mattock|claymore|two handed|two-handed|2h"),
            eObjectType.TwoHandedWeapon, eObjectType.Sword, eObjectType.LargeWeapons),
        (Words("dagger|dirk|rapier|stiletto|gladius|main gauche|kris"), eObjectType.ThrustWeapon, eObjectType.Sword, eObjectType.Piercing),
        (Words("mace|hammer|club|morning star|flanged mace|maul|cudgel|war pick"), eObjectType.CrushingWeapon, eObjectType.Hammer, eObjectType.Blunt),
        (Words("axe|hatchet|cleaver"), eObjectType.SlashingWeapon, eObjectType.Axe, eObjectType.Blades),
        (Words("sword|broadsword|longsword|short sword|scimitar|sabre|saber|falchion|cutlass|katana|blade"),
            eObjectType.SlashingWeapon, eObjectType.Sword, eObjectType.Blades),
    ];

    private static Regex Words(string alternatives) =>
        new($@"\b(?:{alternatives})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// An item's kind from its name, the way a player reads it ("earthshaker chain boots" is chain, "bladeblocker dagger" a
    /// thrust weapon in Albion), in the realm of whoever would use it; <see cref="eObjectType.GenericItem"/> when the name says
    /// nothing. Pure.
    /// </summary>
    public static eObjectType TypeFromName(string itemName, eRealm realm)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return eObjectType.GenericItem;
        foreach ((Regex words, eObjectType albion, eObjectType midgard, eObjectType hibernia) in NameTypes)
            if (words.IsMatch(itemName))
                return realm switch { eRealm.Midgard => midgard, eRealm.Hibernia => hibernia, _ => albion };
        return eObjectType.GenericItem;
    }
}
