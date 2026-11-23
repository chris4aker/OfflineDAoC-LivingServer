using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// The knowledge holes of the 1.2.2 chat audit (docs/bot-voices/CHAT-AUDIT-1.2.2.md, C8 and fix F2): where the realm sells
/// potions, dyes, poison, spellcraft gems, crafting tools and materials, bags, where loot sells, where the Realm Exchange is;
/// the way into Darkness Falls, the frontier and the battlegrounds; the armour and the stats of a class; crafting past the
/// first step; NPC factions; kill tasks; and "who sells X" from the live merchants. Every fact comes from the server's own
/// code or the world data it ships (each table names its source), said the way a 1.65 player types it.
/// <see cref="TryAnswer"/> is pure; <see cref="TryAnswerWhoSells"/> reads the world's merchants.
/// </summary>
public static class AutonomousChatGuide
{
    /// <summary>
    /// One realm's people and places for these answers. NPC names and their trades: tools/lore/npc_roster.json (built from the
    /// OpenDAoC database at 7068aef, the era pass) and the public OpenDAoC database's mob and merchantitem tables (what each
    /// capital merchant's list holds). Porters: AutonomousTownTeleporters; border keeps and frontier medallions:
    /// AutonomousFrontierTransport and the 1.2 changelog; Darkness Falls entrances: AutonomousDarknessFallsPolicy's home
    /// entrance zone points (81, 84, 87); emissaries: FactionEmissaryRuntime.Definitions.
    /// </summary>
    public sealed record Realm(string Capital, string Porter, string BorderKeep, string DfZone, string Vault, string Smith,
        string ClothDye, string LeatherDye, string Enamel, string Poison, string Tools, string Herbs, string Gems, string Materials,
        string WeaponMaster, string ArmorMaster, string TailorMaster, string FletchMaster, string AlchemyMaster, string SpellMaster,
        string Emissary, string EmissaryFaction, string EmissaryTown);

    public static readonly IReadOnlyDictionary<eRealm, Realm> Realms = new Dictionary<eRealm, Realm>
    {
        [eRealm.Albion] = new("Camelot", "Master Visur", "Castle Sauvage", "Camelot Hills", "Lord Urqhart", "Judan Hammerfel",
            "Dyemaster Edare", "Dyemaster Lendand", "Dyemaster Mara", "Velmis", "Marna", "Godfreid", "Godfreid", "Hector Darian",
            "Hephas Elgen", "Loraine Elgen", "Arliss Eadig", "Acey Dalston", "Adelaide Dinsmore", "Clayton Gage",
            "Ysslith", "the lammia", "Caer Diogel"),
        [eRealm.Midgard] = new("Jordheim", "Stor Gothi Annark", "Svasud Faste", "Vale of Mularn", "Jarl Yuliwyf", "Morlin Caan",
            "Hyndla Gunderson", "Ozur", "Solveig", "Ander", "Om", "Holsvi Hallgrim", "Freydis", "Ottar",
            "Aase", "Gest", "Eskil", "Gils", "Armod Dag", "Hradi Thorleif",
            "Hrodvar Deepvow", "the remnants", "Hagall"),
        [eRealm.Hibernia] = new("Tir na Nog", "Channeler Glasny", "Druim Ligen", "Connacht", "Bhreagar Hylvian", "Banon",
            "Dierdra", "Jeanna", "Cristolia", "Nona", "Cedric", "Darcy", "Darcy", "Baran",
            "Hendrika", "Dunstan", "Armin", "Arziqua", "Aslander", "Shannen",
            "Kzzirrak", "the krrzck hive", "Necht"),
    };

    /// <summary>
    /// A few factions players ask about, and whose deaths raise them: the public OpenDAoC database's linkedfaction rows (an
    /// enemy link from F to B: killing a B lowers F's aggro, Faction.OnMemberKilled via FactionMgr.Init). The live answer reads
    /// FactionMgr instead (<see cref="LiveFactions"/>); this table is the fallback and what the bench sees.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string[] Aliases, string[] RaisedBy)> KnownFactions =
    [
        ("the svartalfar", new[] { "svartalf", "svartalfs", "svartalfar", "svart" }, new[] { "pack drakulv", "arachite", "varulv" }),
        ("the remnants", new[] { "remnants", "remnant" }, new[] { "morvalt", "hrimthursa", "servants of iarnvidiur" }),
        ("dellingstad", new[] { "dellingstad" }, new[] { "cats of aegir", "elements of the caldera" }),
        ("the lammia", new[] { "cryptos", "mythicos", "lammia" },   // chat-check: input (the aliases; Cryptos Mythicos are the lammia)
            new[] { "kulaclan", "undead servants of morgana", "children of orylie" }),
        ("caldey outpost", new[] { "caldey" }, new[] { "undead servants of morgana", "drakoran legion" }),
        ("stonecrush clan", new[] { "stonecrush" }, new[] { "dragonsworn", "granite giants" }),
        ("cornwall farmers", new[] { "farmers" }, new[] { "boogys" }),
        ("krrzck", new[] { "krrzck", "mantid", "mantids", "hive" }, new[] { "olcasgean", "botonids" }),
        ("the watcher", new[] { "watcher" }, new[] { "olcasgean", "essence reflection" }),
    ];

    private static readonly Regex TradeAd = new(@"\b(wts|wtb|wtt|pst|for sale|fs)\b", RegexOptions.CultureInvariant);

    private static string Pad(string text) => " " + AutonomousChatQuestions.Normalize(text) + " ";

    private static bool Has(string padded, string pattern) => Regex.IsMatch(padded, pattern, RegexOptions.CultureInvariant);

    /// <summary>A question, not a trade post: a "?", a question opening, or "anyone ..." / "i need ...". Pure.</summary>
    public static bool AsksQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = AutonomousChatQuestions.Normalize(text);
        if (TradeAd.IsMatch(t))
            return false;
        return text.Contains('?') || AutonomousChatQuestions.StartsLikeQuestion(text) ||
               Regex.IsMatch(t, @"^(anyone|any1|anybody|someone|where|who|how|i need|need to know|cant find|can t find)\b");
    }

    /// <summary>
    /// Every answer of this guide, in order: buying, the way into Darkness Falls, the frontier and the battlegrounds, armour,
    /// stats, crafting, factions, tasks. <paramref name="factions"/> is the world's factions (null: <see cref="KnownFactions"/>).
    /// Pure but for the draw.
    /// </summary>
    public static bool TryAnswer(string text, eRealm realm, string askerClass, int askerLevel, eRealm dfOwner, bool dfOpenToAll,
        Random random, out string answer, IReadOnlyList<(string Name, string[] Aliases, string[] RaisedBy)> factions = null)
    {
        answer = null;
        random ??= Random.Shared;
        if (!AsksQuestion(text) || !Realms.TryGetValue(realm, out Realm r))
            return false;
        string t = Pad(text);
        answer = Shopping(t, r, random)
                 ?? Travel(t, r, realm, askerLevel, dfOwner, dfOpenToAll, random)
                 ?? ArmorAnswer(t, text, askerClass, random)
                 ?? StatAnswer(t, text, askerClass, random)
                 ?? CraftAnswer(t, r, random)
                 ?? FactionAnswer(t, r, factions ?? KnownFactions, random)
                 ?? TaskAnswer(t, random);
        return answer != null;
    }

    private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];

    // ---- buying ------------------------------------------------------------------------------------------------------------

    private const string BuyWords = @" (buy|buys|sell|sells|selling|get|find|vendor|vendors|merchant|merchants|shop|shops|store|has|have|got) ";
    private const string AskWords = @" (where|wheres|who|whos|how|anyone|any1|anybody|which|can i|do i|is there|are there) ";

    /// <summary>
    /// Potions, dyes, poison, gems, crafting tools and materials, bags, selling loot, the Realm Exchange, repairs, horse food.
    /// Prices are the capital merchants' lists (public OpenDAoC database, merchantitem with itemtemplate.Price, copper): dyes 50c
    /// to 16g 50s, the poison merchants 11c to 86s, the craft kits 30s, a sewing kit or planing tool 2s, gem bases 60c up to
    /// 29g. No merchant of the classic or Shrouded Isles lands sells a potion or a bag in that database (only the tutorial, Cathal
    /// Valley and Atlantis have potion merchants), so those answers say so.
    /// </summary>
    private static string Shopping(string t, Realm r, Random random)
    {
        bool buying = Has(t, BuyWords) && Has(t, AskWords);
        string cap = r.Capital;
        if (Has(t, @" (food|feed|hay|oats|carrots?) ") && Has(t, @" horses? "))
            return Pick(random, "lol horses dont eat here, its a ticket from the stable master", "no horse food lol, you just buy a ticket at the stable");
        if (Has(t, @" repair(ed|ing|s)? ") && Has(t, @" (how|where|who|can|anyone) ") && !Has(t, @" (keep|keeps|door|doors|wall|walls|gate|ram|rams|siege|tower|towers) "))   // chat-check: input
            return Pick(random, $"hand it to a smith, theres {r.Smith} in {cap}. or a crafter with the skill",
                $"any smith fixes it for coin, {r.Smith} in {cap}", "smith in town repairs it, or an armorcrafter");
        // The exchange of the capital asked about; another realm's has nothing for us.
        bool otherCapital = Realms.Values.Any(other => other.Capital != cap && t.Contains(" " + other.Capital.ToLowerInvariant() + " ", StringComparison.Ordinal));
        if (Has(t, @" (realm exchange|the exchange|exchange) ") && Has(t, @" (where|wheres|how do i get|how to get|find) ") && !otherCapital)
            return Pick(random, $"in {cap}, type /tc and it ports you right to it", $"/tc takes you straight to the exchange in {cap}",
                $"the broker in {cap}, /tc gets you there");
        if (!buying)
            return null;
        if (Has(t, @" (pots?|potions?|draughts?|elixirs?) "))
            return Pick(random, $"no vendor sells pots, alchemists make them. check the realm exchange in {cap}",
                $"alchemists make them, nobody vendors them. try the exchange in {cap}",
                $"crafted only, ask an alchemist or look on the exchange in {cap}");
        if (Has(t, @" (dyes?|enamels?) "))
            return Pick(random, $"dye merchants in {cap}, {r.ClothDye} for cloth, {r.LeatherDye} leather, {r.Enamel} enamel for metal",
                $"{cap} has dye merchants, 50c for the plain ones up to 16g for the fancy ones",
                $"{r.ClothDye} in {cap} does cloth dye, {r.Enamel} the enamel for chain and plate");
        if (Has(t, @" (poisons?|venoms?|envenom) "))
            return Pick(random, $"poison merchant in {cap}, {r.Poison}. only the assassin classes can use them",
                $"{r.Poison} in {cap} sells poison, cheap ones a few copper, the good ones near a gold",
                $"{cap}, theres a poison merchant, {r.Poison}");
        if (Has(t, @" (gems?|sc gems?|spellcraft gems?) "))
            return Pick(random, $"spellcrafters make them. the gem bases are at {r.Gems} in {cap}",
                $"get a spellcrafter to make them, or buy bases from {r.Gems} in {cap} and learn sc",
                $"crafted by spellcrafters. check the exchange in {cap} too");
        if (Has(t, @" (tools?|kits?|sewing kit|planing tool|smiths hammer) "))
            return Pick(random, $"merchant in {cap}, {r.Tools} has the kits. about 30s, a sewing kit is 2s",
                $"{r.Tools}, a merchant in {cap}, sells the craft kits and hammers", $"the craft supply merchant in {cap}, {r.Tools}");
        if (Has(t, @" (materials?|mats|supplies|metal bars|bars|thread|boards|leather squares?|cloth squares?) "))
            return Pick(random, $"supply merchants in {cap}, {r.Materials} or {r.Tools} have bars, boards, leather and cloth",
                $"{r.Tools}, the supply merchant in {cap}, sells the basic mats", $"craft merchants in {cap}, {r.Materials} has the metal and wood");
        if (Has(t, @" (bags?|backpacks?|sacks?|pouch|pouches) "))
            return Pick(random, $"no bags in this game lol. bank the rest with {r.Vault} in {cap}",
                $"there are no bags, the bank in {cap} holds your overflow", "no such thing here, use the bank in town");
        if (Has(t, @" sell (my |the |all |this |some )?(loot|junk|drops|stuff|items|trash) ") || Has(t, @" (where|how) (do i|can i|to) sell "))
            return Pick(random, $"any merchant buys it. the good stuff goes on the realm exchange in {cap}",
                $"right click any merchant and sell, or post it on the exchange in {cap}", "any vendor in town takes loot");
        return null;
    }

    // ---- the way in --------------------------------------------------------------------------------------------------------

    private const string WayWords = @" (how do i get|how do you get|how to get|how can i get|how do i go|how to go|how do i enter|how to enter|get in|get into|go in|way in|way to|entrance|where is|wheres|where s|where do i go) ";

    /// <summary>
    /// Darkness Falls (DFEnterJumpPoint: open to the realm with the most keeps, plus a 15-minute grace for the realm that lost
    /// it, unless allow_all_realms_df; each realm's home entrance is AutonomousDarknessFallsPolicy.HomeEntranceZonePointId), the
    /// frontier (the porter to the first border keep, where the frontier medallions are sold, then the keep's teleporter) and the
    /// battlegrounds (every realm porter's [Battlegrounds], by level: AutonomousChatLiveAnswers.BattlegroundLevels). Pure.
    /// </summary>
    private static string Travel(string t, Realm r, eRealm realm, int level, eRealm dfOwner, bool dfOpenToAll, Random random)
    {
        if (!Has(t, WayWords))
            return null;
        if (Has(t, @" (df|darkness falls) "))
        {
            string where = $"the entrance is in {r.DfZone}";
            if (dfOpenToAll)
                return Pick(random, $"{where}, its open to every realm here", $"{where}, anyone can go in on this server");
            if (dfOwner == realm)
                return Pick(random, $"{where}, and we hold it right now so just walk in", $"{where}. its ours atm, go for it");
            string holder = dfOwner switch
            {
                eRealm.Albion => "the albs have it", eRealm.Midgard => "the mids have it", eRealm.Hibernia => "the hibs have it",
                _ => "nobody holds it",
            };
            return Pick(random, $"{where}, but {holder} right now. the realm with the most keeps gets df",
                $"{where}. only opens for us when we hold the most keeps, {holder} atm");
        }
        if (Has(t, @" (bgs?|battlegrounds?|thid|thidranki|abermenai|murdaigean|caledonia) "))
        {
            string bracket = level switch
            {
                >= 15 and <= 19 => "Abermenai", >= 20 and <= 24 => "Thidranki", >= 25 and <= 29 => "Murdaigean",
                >= 30 and <= 35 => "Caledonia", _ => null,
            };
            string tail = bracket != null ? $", at your level its {bracket}" : ", 15 to 35 only";
            return Pick(random, $"talk to the porter, {r.Porter} in {r.Capital}, and say battlegrounds{tail}",
                $"any porter, {r.Porter} in {r.Capital}, say battlegrounds{tail}",
                $"the porter sends you, click {r.Porter} and pick battlegrounds{tail}");
        }
        if (Has(t, @" (frontier|frontiers|rvr|the front) ") && !Has(t, @" (rps?|realm points?|realm rank|rr\d*|kills?) "))
            return Pick(random, $"port to {r.BorderKeep} with {r.Porter}, buy a frontier medallion there and the teleporter sends you to a keep",
                $"{r.Porter} in {r.Capital} to {r.BorderKeep}, then a medallion and the keep teleporter",
                $"{r.Porter} ports you to {r.BorderKeep}, the border keep. thats the way out");
        return null;
    }

    // ---- a class's armour and stats ----------------------------------------------------------------------------------------

    /// <summary>
    /// "what armor does a thane wear?": the heaviest armour the class wears, by the game's item rule
    /// (<see cref="AutonomousChatGear.ArmorWord"/>, GeneratedUniqueItem.Get*ArmorType), with the level it starts at. Pure.
    /// </summary>
    private static string ArmorAnswer(string t, string text, string askerClass, Random random)
    {
        if (!Has(t, @" (armor|armour|armors) ") || !Has(t, @" (wear|wears|wearing|use|uses|can|for|does|do|should|type|kind) ") ||
            Has(t, BuyWords) && Has(t, @" (buy|sell|sells|vendor|merchant|get) "))
            return null;
        string cls = AutonomousChatFieldTalk.ClassNamed(text) ?? (Has(t, @" (i|my|me) ") ? askerClass : null);
        return ArmorLine(cls);
    }

    /// <summary>"thanes wear chain, studded until 10": the class's top armour and the level it starts at, or null. Pure.</summary>
    public static string ArmorLine(string className)
    {
        string top = AutonomousChatGear.ArmorWord(className, 50);
        if (top == null)
            return null;
        string name = className.Trim().ToLowerInvariant();
        int from = 50;
        while (from > 1 && AutonomousChatGear.ArmorWord(className, from - 1) == top)
            from--;
        string plural = name.EndsWith("man") ? name[..^3] + "men" : name.EndsWith("s") ? name : name + "s";
        if (from <= 1)
            return $"{plural} wear {top}";
        string before = AutonomousChatGear.ArmorWord(className, from - 1);
        return $"{plural} wear {top} from {from}, {before} before that";
    }

    private static readonly (eStat Stat, string Word, string Pattern)[] StatWords =
    [
        (eStat.STR, "str", @"str|strength"), (eStat.CON, "con", @"con|constitution"), (eStat.DEX, "dex", @"dex|dexterity"),
        (eStat.QUI, "qui", @"qui|quick|quickness"), (eStat.INT, "int", @"int|intelligence"), (eStat.PIE, "pie", @"pie|piety"),
        (eStat.EMP, "emp", @"emp|empathy"), (eStat.CHR, "cha", @"cha|chr|charisma"),
    ];

    private static readonly Dictionary<eStat, string> StatMeaning = new()
    {
        [eStat.STR] = "strength, melee damage", [eStat.CON] = "constitution, more hit points", [eStat.DEX] = "dexterity, weapon skill and cast speed",
        [eStat.QUI] = "quickness, swing speed", [eStat.INT] = "intelligence, power and damage for the int casters",
        [eStat.PIE] = "piety, power and spell strength for the piety casters", [eStat.EMP] = "empathy, power for the empathy casters",
        [eStat.CHR] = "charisma, power for the charisma casters",
    };

    /// <summary>
    /// "what stats for a sorcerer?", "does strength matter for a healer?", "what does con mean on items?": the class's own
    /// stats from its class script (PrimaryStat, SecondaryStat, TertiaryStat; ManaStat is the power stat), or what a stat
    /// does. Pure.
    /// </summary>
    private static string StatAnswer(string t, string text, string askerClass, Random random)
    {
        var named = StatWords.Where(s => Has(t, $" ({s.Pattern}) ")).ToArray();
        if (named.Length == 1 && Has(t, @" (mean|means|do|does) ") && Has(t, @" (items?|gear|armor|armour|stats?|on my) "))
            return Pick(random, StatMeaning[named[0].Stat], $"{named[0].Word} is {StatMeaning[named[0].Stat]}");
        bool asksStats = Has(t, @" (stats?|attributes?) ");
        bool asksOne = named.Length == 1 && Has(t, @" (matter|matters|need|needs|worth|important|good for|use|useful) ");
        if (!asksStats && !asksOne)
            return null;
        string cls = AutonomousChatFieldTalk.ClassNamed(text) ?? (Has(t, @" (i|my) ") ? askerClass : null);
        if (StatsOf(cls) is not { } stats)
            return null;
        string main = WordOf(stats.Primary);
        if (asksOne)
        {
            eStat asked = named[0].Stat;
            if (asked == stats.Primary || asked == stats.Mana)
                return Pick(random, $"yes, {main} is the main one for a {Lower(cls)}", $"yeah thats your main stat");
            if (asked == stats.Secondary || asked == stats.Tertiary || asked == eStat.CON)
                return Pick(random, $"a bit, but {main} first", $"some, {main} matters more");
            return Pick(random, $"not really, {main} is what counts", $"nope, get {main}");
        }
        var rest = new[] { stats.Secondary, stats.Tertiary }.Where(s => s != eStat.UNDEFINED && s != stats.Primary).Distinct().Select(WordOf).ToArray();
        string then = rest.Length == 0 ? "" : $", then {string.Join(" and ", rest)}";
        string con = stats.Primary == eStat.CON || stats.Secondary == eStat.CON || stats.Tertiary == eStat.CON ? "" : ", con for hp";
        return $"{main} first{then}{con}";
    }

    private static string Lower(string cls) => (cls ?? "class").Trim().ToLowerInvariant();

    private static string WordOf(eStat stat) => StatWords.FirstOrDefault(s => s.Stat == stat).Word ?? stat.ToString().ToLowerInvariant();

    /// <summary>A class's stats from its class script (ClassSorcerer: INT, DEX, QUI, power from INT), or null. Pure but for the class lookup.</summary>
    public static (eStat Primary, eStat Secondary, eStat Tertiary, eStat Mana)? StatsOf(string className)
    {
        if (AutonomousChatGear.ClassOf(className) is not { } cls)
            return null;
        ICharacterClass found;
        try
        {
            found = ScriptMgr.FindCharacterClass((int)cls);
        }
        catch
        {
            return null;
        }
        if (found == null || found.PrimaryStat == eStat.UNDEFINED)
            return null;
        return (found.PrimaryStat, found.SecondaryStat, found.TertiaryStat, found.ManaStat);
    }

    // ---- crafting ----------------------------------------------------------------------------------------------------------

    private static readonly (string Craft, string Pattern)[] Crafts =
    [
        ("weaponcrafting", @"weaponcraft|weaponcrafting|weapon crafting|weaponsmith|weaponsmithing"),
        ("armorcrafting", @"armorcraft|armorcrafting|armourcraft|armourcrafting|armor crafting|armorsmith|armorsmithing"),
        ("tailoring", @"tailoring|tailor"), ("fletching", @"fletching|fletch|fletcher"), ("alchemy", @"alchemy|alch|alchemist"),
        ("spellcrafting", @"spellcraft|spellcrafting|spellcrafter"),
    ];

    private static string MasterOf(Realm r, string craft) => craft switch
    {
        "weaponcrafting" => r.WeaponMaster, "armorcrafting" => r.ArmorMaster, "tailoring" => r.TailorMaster,
        "fletching" => r.FletchMaster, "alchemy" => r.AlchemyMaster, _ => r.SpellMaster,
    };

    /// <summary>
    /// Crafting past "how do i start": who teaches a craft in the capital (the masters of tools/lore/npc_roster.json), how the
    /// skill rises (CraftRules.Con and BaseChanceToGainPoint: orange 55%, red 65%, grey nothing; ChanceToMake: red fails more),
    /// what alchemy needs, how spellcraft works (SpellcraftRules: four bonuses, points from the item's level and quality, up to six
    /// over with a chance to blow), the secondary skills (AbstractCraftingSkill.GainCraftingSkillPoints: they rise with the
    /// materials used; Alchemy raises herbcraft, SpellCrafting gemcutting). Pure.
    /// </summary>
    private static string CraftAnswer(string t, Realm r, Random random)
    {
        string craft = Crafts.FirstOrDefault(c => Has(t, $" ({c.Pattern}) ")).Craft;
        if (craft == "spellcrafting" || Has(t, @" sc ") && Has(t, @" (work|works|how does|how do) "))
        {
            if (Has(t, @" (how does|how do|whats|what is|explain) ") && Has(t, @" (work|works|spellcraft|spellcrafting|sc) ") && !Has(t, @" (learn|start|trainer) "))
                return Pick(random, "a spellcrafter puts up to four gems on crafted gear, the item level and quality decide the points",
                    "gems on crafted armor or weapons, max four. you can overcharge a little but it might blow up");
            craft ??= "spellcrafting";
        }
        if (craft != null && Has(t, @" (learn|start|starting|begin|get into|pick up|take up|become|trainer|trains|teaches|teach|master) "))
            return Pick(random, $"{MasterOf(r, craft)} in {r.Capital} teaches {craft}", $"see {MasterOf(r, craft)} in {r.Capital}, the {craft} master",
                $"the {craft} master in {r.Capital}, {MasterOf(r, craft)}");
        if (craft == "alchemy" && Has(t, @" (need|needs|required|use|uses) "))
            return Pick(random, $"an alchemy kit, herbs and vials, {r.Herbs} in {r.Capital} sells them",
                $"herbs and crystal flasks from {r.Herbs} in {r.Capital}, plus the kit");
        if (Has(t, @" (skill|skills|level|levels|raise|grind|get) ") && Has(t, @" (up|raise|level|grind|faster|skill up|skillup) ") &&
            (craft != null || Has(t, @" (craft|crafting|tradeskill) ")))
            return Pick(random, "make stuff that cons orange to you, red gains more but fails more. grey gives nothing",
                "orange and red recipes give the points, yellow barely, grey none", "keep making whatever cons orange, thats the grind");
        if (Has(t, @" (secondary|secondaries|second) (craft|crafts|skill|skills) "))
            return Pick(random, "the secondaries go up by themselves as you craft, metalwork, leather, cloth, wood. alch raises herbcraft",
                "they come with your main craft, you skill them just by making stuff");
        return null;
    }

    // ---- NPC factions ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// NPC faction as the server keeps it (Faction, FactionMgr): an NPC camp that is no realm's remembers you, 100 down to -100;
    /// above 75 it attacks on sight, above 50 it won't deal with you (GameNPC.Interact's dirty look), 25 or less is friendly
    /// (Faction.GetStanding); a kill angers the dead one's friends and pleases its enemies by a point (Faction.OnMemberKilled);
    /// the examine line says where you stand (GameNPC.GetAggroLevelString); the realm's emissary gives a hunt, ten kills for ten
    /// points (FactionReputationQuest). Your own realm's guards and towns are realm NPCs, not a faction. Pure.
    /// </summary>
    private static string FactionAnswer(string t, Realm r, IReadOnlyList<(string Name, string[] Aliases, string[] RaisedBy)> factions, Random random)
    {
        bool faction = Has(t, @" (faction|factions|kos|reputation|rep|standing) ") || Has(t, @" (get|make) (the )?\w+( \w+)? to (like|trust) (me|us) ");
        var named = factions.FirstOrDefault(f => f.Aliases.Any(a => Has(t, $" {Regex.Escape(a)} ")));
        string emissary = $"{r.Emissary} in {r.EmissaryTown} gives a hunt for {r.EmissaryFaction}";
        if (Has(t, @" kos ") || faction && Has(t, @" (town|camp|village) "))
            return Pick(random, "with a neutral camp yeah, kill enough of their friends and they attack on sight. our own towns never",
                "only the neutral camps, not our realm's towns");
        if (faction && Has(t, @" what (faction|factions) (is|are|s) "))
            return named.Name != null
                ? Pick(random, $"{named.Name}, their own faction", $"theyre {named.Name}")
                : Pick(random, "every camp is its own faction, target one and the examine line says how they see you",
                    "their own faction, click one and it says if theyre friendly or not");
        if (Has(t, @" (raise|fix|improve|repair|better|up|like me|friendly|gain|get) ") && (faction || named.Name != null && Has(t, @" (like me|friendly|trust) ")))
        {
            if (named.Name != null && named.RaisedBy.Length > 0)
                return Pick(random, $"kill their enemies, {string.Join(", ", named.RaisedBy.Take(3))}. every kill helps a bit",
                    $"every {named.RaisedBy[0]} you kill helps with {named.Name}");
            return Pick(random, $"kill their enemies, every kill helps a little. {emissary}",
                "kill whoever they hate, a point a kill. takes a while");
        }
        if (faction && Has(t, @" (check|see|know|tell|find out) "))
            return Pick(random, "target one of their npcs, the examine line says aggressive, hostile, neutral or friendly to you",
                "click one of their npcs, it tells you if they look at you friendly or hostile");
        if (Has(t, @" (guards?|town|npcs?|mobs?|they) ") && Has(t, @" (attack|attacking|attacked|aggro|aggroing|hate|kos) ") && Has(t, @" (why|how come) "))
            return Pick(random, "if its a neutral camp thats faction, you killed their friends. our own guards never turn on you",
                "faction, they attack on sight once you kill enough of their friends");
        if (Has(t, @" (merchant|vendor|npc|he|she) ") && Has(t, @" (talk|trade|deal|sell) ") && Has(t, @" (wont|won t|doesnt|will not|not|why) "))
            return Pick(random, "bad faction, they give you a dirty look when it drops. kill their enemies to fix it",
                "faction, theyre hostile to you. kill whoever they hate for a while");
        if (Has(t, @" (kill|killing|kills) ") && (faction || Has(t, @" (hurt|lower|ruin|bad) ")))
            return Pick(random, "not with them, no. it angers their friends, and their enemies like you more for it",
                "yeah, with whoever is friends with them");
        if (faction && Has(t, @" (what is|whats|what s|what are|for|how does|how do|work|works|mean) "))
            return Pick(random, "npc camps outside the realm remember what you kill. too low and they attack, high enough and they trade",
                $"neutral npcs keep score of you. {emissary}");
        return null;
    }

    // ---- tasks -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// "how do i get kill tasks?": target a named guard of the realm and /task (KillTask.CheckAvailability, the /task command),
    /// another townsperson for a delivery task; tasks stop after 19 (AbstractTask.MaxTaskLevel). Pure.
    /// </summary>
    private static string TaskAnswer(string t, Random random)
    {
        if (!Has(t, @" (task|tasks) ") || !Has(t, @" (how|where|get|who|any|do i|can i) "))
            return null;
        if (Has(t, @" (kill task|kill tasks) ") || !Has(t, @" (delivery|money|craft) "))
            return Pick(random, "target a named guard in town and type /task, they hand out kill tasks till 19",
                "/task on a named guard, only up to level 19", "guards with a name give them till 19, target and /task");
        return Pick(random, "/task on a townsperson for a delivery, till level 19", "target a merchant or townsperson and /task");
    }

    // ---- who sells X (live) ------------------------------------------------------------------------------------------------

    private static readonly Regex WantedRe = new(
        @"^(?:so |hey |um |anyone know |any1 know |does anyone know |quick question )?(?:who sells|who has|who s got|anyone sell|anyone selling|any1 sell|where (?:can|do|would|should) (?:i|you|u|we|ya) (?:buy|get)|where to (?:buy|get)|where do they sell|where are)(?: a| an| some| the| any| new| more| better| good| cheap)? (?<thing>[a-z][a-z ]{1,30}?)(?: (?:at|in|for|around|near|on) [a-z ]+| lol| pls| please| plz| here| now)?$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> TooVague = new(StringComparer.Ordinal)
    {
        "stuff", "gear", "it", "one", "them", "things", "items", "equipment", "that", "this", "those", "these", "better", "good stuff",
    };

    /// <summary>The thing a "who sells X" question asks for ("who sells feathers" -> "feathers"), or null. Pure.</summary>
    public static string WantedThing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match m = WantedRe.Match(AutonomousChatQuestions.Normalize(text));
        if (!m.Success)
            return null;
        string thing = m.Groups["thing"].Value.Trim();
        return thing.Length < 3 || TooVague.Contains(thing) ? null : thing;
    }

    /// <summary>Copper as chat says it: "40c", "2s", "3g 50s", "16g". Pure.</summary>
    public static string PriceWord(long copper)
    {
        if (copper < 100)
            return $"{Math.Max(0, copper)}c";
        if (copper < 10_000)
            return $"{copper / 100}s";
        long gold = copper / 10_000, silver = copper % 10_000 / 100;
        return gold < 10 && silver > 0 ? $"{gold}g {silver}s" : $"{gold}g";
    }

    /// <summary>"Gilbert in Camelot sells hemlock, about 40c". Pure but for the draw.</summary>
    public static string WhoSellsLine(string thing, string merchant, string zone, long minPrice, long maxPrice, Random random)
    {
        random ??= Random.Shared;
        string price = minPrice <= 0 ? "" : maxPrice > minPrice * 2 ? $", {PriceWord(minPrice)} to {PriceWord(maxPrice)}" : $", about {PriceWord(minPrice)}";
        return random.Next(2) == 0 ? $"{merchant} in {zone} has {thing}{price}" : $"{merchant} sells {thing} in {zone}{price}";
    }

    private sealed record Ware(string Merchant, string Zone, ushort Region, int X, int Y, string Item, long Price);

    private static readonly object WareSync = new();
    private static Dictionary<eRealm, List<Ware>> _wares;

    /// <summary>What the world's coin merchants sell, by the realm that can reach them (classic and Shrouded Isles lands), once.</summary>
    private static List<Ware> WaresOf(eRealm realm)
    {
        if (_wares == null)
        {
            lock (WareSync)
            {
                if (_wares == null)
                {
                    var index = new Dictionary<eRealm, List<Ware>>();
                    foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                                 AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
                    {
                        eRealm home = AutonomousChatKnowledge.HomeRealmOfRegion(region.ID);
                        // Only plain coin merchants: bounty, seal and medallion vendors price in something else.
                        foreach (GameMerchant merchant in region.Objects.OfType<GameMerchant>().Where(m => m.GetType() == typeof(GameMerchant)))
                        {
                            if (merchant.ObjectState is not GameObject.eObjectState.Active || merchant.TradeItems == null || merchant.CurrentZone == null)
                                continue;
                            eRealm owner = merchant.Realm != eRealm.None ? merchant.Realm : home;
                            if (owner == eRealm.None)
                                continue;
                            if (!index.TryGetValue(owner, out var list))
                                index[owner] = list = new();
                            foreach (DictionaryEntry entry in merchant.TradeItems.GetAllItems())
                                if (entry.Value is DbItemTemplate item && !string.IsNullOrWhiteSpace(item.Name))
                                    list.Add(new(merchant.Name, merchant.CurrentZone.Description, region.ID, merchant.X, merchant.Y,
                                        item.Name.ToLowerInvariant(), item.Price));
                        }
                    }
                    _wares = index;
                }
            }
        }
        return _wares.TryGetValue(realm, out var found) ? found : new();
    }

    /// <summary>
    /// "who sells feathers?", "where can i buy hemlock": the asker's realm's merchant that has it, the nearest in the asker's
    /// region first, then the capital's (the world's merchant lists; 1.2.2 chat audit C8: "who sells" went unanswered).
    /// </summary>
    public static bool TryAnswerWhoSells(GamePlayer player, string query, out string answer)
    {
        answer = null;
        if (player == null || WantedThing(query) is not { } thing)
            return false;
        string single = thing.EndsWith("s") && thing.Length > 3 ? thing[..^1] : thing;
        List<Ware> hits;
        try
        {
            hits = WaresOf(player.Realm).Where(w => AutonomousChatKnowledge.ContainsPhrase(w.Item, thing) ||
                                                    AutonomousChatKnowledge.ContainsPhrase(w.Item, single)).ToList();
        }
        catch
        {
            return false;
        }
        if (hits.Count == 0)
            return false;
        Ware best = hits.OrderByDescending(w => w.Region == player.CurrentRegionID)
            .ThenBy(w => w.Region == player.CurrentRegionID ? Math.Pow(w.X - player.X, 2) + Math.Pow(w.Y - player.Y, 2) : 0)
            .ThenByDescending(w => AutonomousBotVoices.IsCapitalRegion(w.Region)).First();
        var his = hits.Where(w => w.Merchant == best.Merchant && w.Region == best.Region).Select(w => w.Price).Where(p => p > 0).DefaultIfEmpty(0).ToArray();
        answer = WhoSellsLine(thing, best.Merchant, best.Zone, his.Min(), his.Max(), Random.Shared);
        return true;
    }

    /// <summary>The world's factions as <see cref="TryAnswer"/> reads them (FactionMgr), or null when none are loaded.</summary>
    public static IReadOnlyList<(string Name, string[] Aliases, string[] RaisedBy)> LiveFactions()
    {
        try
        {
            if (FactionMgr.Factions.Count == 0)
                return null;
            var all = FactionMgr.Factions.Values.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Name)).ToArray();
            var list = new List<(string, string[], string[])>();
            foreach (Faction f in all)
            {
                string name = f.Name.Trim().ToLowerInvariant();
                string bare = name.StartsWith("the ") ? name[4..] : name;
                // Killing a member of b lowers the aggro of every faction in b.EnemyFactions (Faction.OnMemberKilled).
                string[] raisedBy = all.Where(b => b.EnemyFactions.Contains(f)).Select(b => b.Name.Trim().ToLowerInvariant()).Take(3).ToArray();
                var known = KnownFactions.FirstOrDefault(k => k.Name == name || k.Name == "the " + bare || k.Name == bare);
                string[] aliases = (known.Aliases ?? []).Append(bare).Where(a => a.Length >= 4).Distinct().ToArray();
                list.Add((name, aliases, raisedBy));
            }
            return list;
        }
        catch
        {
            return null;
        }
    }
}
