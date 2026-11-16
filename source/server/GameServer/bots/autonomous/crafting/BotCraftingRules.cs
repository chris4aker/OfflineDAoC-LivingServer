using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;

namespace DOL.GS;

/// <summary>
/// The pure rules of gamebot crafting (Crafting 2.0 WP4, docs/CRAFTING-2.0-BUILD-PLAN.md §1 and §3). No server state: every
/// decision takes records and numbers, so the tests and the rehearsal (WP10) run them as they are.
/// <para>Ported: recipe choice, shopping, copies per session, trip roll, stations and the secondary-skill check from
/// rambodolphin13's DividedRealms (AutonomousBotCrafting, 93c25b69 → ceac061a → e37f8810), GPL-3.0, moved from the
/// server's <c>Recipe</c> onto <see cref="CraftRecipeInfo"/> and from <c>AbstractCraftingSkill</c>'s instance tables onto
/// <see cref="CraftRules"/>; "list only the best of a session" and the siege kit tiers from shadowofze's True165
/// (CrafterBots165.BestProductIndex and SiegeKits, b097caf → 75267dd), and the points per gain from True165's
/// CraftingRate165.Points (00f9fbf), GPL-3.0.</para>
/// <para>Ours: only the Crafter personality crafts (DR: any bot of level 5+); the 7% share for new bots; orders by realm
/// weights with DR's class rule as the tie-break; the bot pace following the bot XP multiplier; the order and title names.</para>
/// </summary>
public static class BotCraftingRules
{
    // ---- DividedRealms' numbers ----

    public const int MinimumBotLevel = 5;
    /// <summary>Pieces one session makes (True165's 10 combines, b097caf; DR had 6). A failed attempt is tried again, as a
    /// player's queue does, so a session takes more attempts than this on a hard recipe.</summary>
    public const int CraftsPerSession = 10;
    /// <summary>A session spends at most this share of the bot's coin (DR).</summary>
    public const double ShareOfCoin = 0.25;
    /// <summary>Recipes whose expected points per attempt are within this share of the best count as a tie (DR).</summary>
    public const double GainTieShare = 0.95;
    /// <summary>A piece is useful to a wearer up to this many levels below the wearer's own level (DR).</summary>
    public const int UsefulLevelWindow = 10;
    /// <summary>DR's engine rule: a trained crafter with three or more stacks of its materials takes a craft break 16% of the time.</summary>
    public const int MaterialStacksForCraftBreak = 3;
    public const double MaterialCraftBreakChance = 0.16;

    // ---- Ours: Crafter gamebots craft more often than DR's every-bot crafters did ----

    /// <summary>A Crafter with no order goes to join one at this share of its task breaks (DR: every bot, 10%).</summary>
    public const double JoinChance = 0.5;
    /// <summary>A trained Crafter makes a craft trip at this share of its task breaks (DR: 15% of every bot's). The
    /// design's model gave crafters 55% of their time at the station; a task is 45-120 minutes and a trip up to
    /// <see cref="TripWorkMilliseconds"/>, so this is still far less, and the pace (<see cref="PointsPerGain"/>) adds the
    /// bot XP multiplier on top.</summary>
    public const double SessionChance = 0.5;

    /// <summary>Sessions one craft trip may work in a row (ours): after a session the trip plans the next at the same
    /// station while coin, bag room and <see cref="TripWorkMilliseconds"/> allow. DR's trips made one session, and its
    /// night of 2026-11-04 left the median crafter at skill 8 (top 28): a Crafter spent about 1% of its time crafting.</summary>
    public const int SessionsPerTrip = 3;

    /// <summary>No session starts after a trip has run this long, and a session's copies are cut to what fits before it
    /// (a task break lasts at most 30 minutes, and the inventory stop that posts the work comes after the trip).</summary>
    public const long TripWorkMilliseconds = 15 * 60_000;

    /// <summary>Backpack slots a session leaves free for loot (a piece made is a slot until the inventory stop).</summary>
    public const int ReservedBagSlots = 4;

    /// <summary>The share tie window for an order: orders whose shortfall is within this many crafters of the largest
    /// count as equally needed, and DR's class rule picks among them.</summary>
    public const double OrderTieWindow = 0.5;

    /// <summary>The orders a Crafter gamebot can join (DR had the first four; True165 added spellcraft, alchemy and siegecraft).</summary>
    public static readonly eCraftingSkill[] Orders =
    [
        eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring, eCraftingSkill.WeaponCrafting, eCraftingSkill.SpellCrafting,
        eCraftingSkill.Alchemy, eCraftingSkill.Fletching, eCraftingSkill.SiegeCrafting,
    ];

    /// <summary>
    /// The share of a realm's crafters each order should have (design doc facet A, proposed and taken by the plan:
    /// armorcraft 24%, tailoring 22%, weaponcraft 18%, spellcraft 14%, alchemy 12%, fletching 8%, siegecraft 2%). The same
    /// weights in each realm, balanced per realm by <see cref="ChooseOrder"/>.
    /// </summary>
    public static double OrderWeight(eCraftingSkill order) => order switch
    {
        eCraftingSkill.ArmorCrafting => 0.24,
        eCraftingSkill.Tailoring => 0.22,
        eCraftingSkill.WeaponCrafting => 0.18,
        eCraftingSkill.SpellCrafting => 0.14,
        eCraftingSkill.Alchemy => 0.12,
        eCraftingSkill.Fletching => 0.08,
        eCraftingSkill.SiegeCrafting => 0.02,
        _ => 0,
    };

    public static bool IsOrder(eCraftingSkill skill) => Array.IndexOf(Orders, skill) >= 0;

    // ---- Who crafts ----

    /// <summary>Only our Crafter personality crafts, and never a beggar (a beggar lives on its corner).</summary>
    public static bool IsCrafterType(BotPersonalityType type, bool beggar) => type == BotPersonalityType.Crafter && !beggar;

    /// <summary>
    /// The personality shares for a gamebot created after the stored types began: the legacy shares with the Crafter share
    /// set to <paramref name="crafterPercent"/> (bot_crafter_share_percent, 7 by default) and the difference spread over
    /// the other types in proportion to their legacy shares, so they still sum to 100.
    /// </summary>
    public static int[] NewBotShares(IReadOnlyList<int> legacy, int crafterPercent)
    {
        int crafter = (int)BotPersonalityType.Crafter;
        crafterPercent = Math.Clamp(crafterPercent, 0, 100);
        var shares = new int[legacy.Count];
        int otherLegacy = 0;
        for (int i = 0; i < legacy.Count; i++)
            if (i != crafter)
                otherLegacy += legacy[i];
        int room = 100 - crafterPercent;
        int given = 0;
        var remainders = new List<(double Remainder, int Index)>();
        for (int i = 0; i < legacy.Count; i++)
        {
            if (i == crafter)
                continue;
            double exact = otherLegacy == 0 ? 0 : room * (double)legacy[i] / otherLegacy;
            shares[i] = (int)Math.Floor(exact);
            given += shares[i];
            remainders.Add((exact - shares[i], i));
        }
        // Largest remainders take the points rounding left, so the table always sums to 100.
        foreach ((double _, int index) in remainders.OrderByDescending(r => r.Remainder).ThenBy(r => r.Index).Take(room - given))
            shares[index]++;
        shares[crafter] = crafterPercent;
        return shares;
    }

    /// <summary>A type from a roll in [0, 100) on a share table (the same walk as AutonomousBotPersonality.TypeFor).</summary>
    public static BotPersonalityType TypeFromRoll(int roll, IReadOnlyList<int> shares)
    {
        for (int index = 0; index < shares.Count; index++)
        {
            if (roll < shares[index])
                return (BotPersonalityType)index;
            roll -= shares[index];
        }
        return BotPersonalityType.Leveler;
    }

    // ---- Orders ----

    /// <summary>DR's class rule: archers fletch, list casters tailor, the rest split between weapons and armor by id.</summary>
    public static eCraftingSkill ClassOrder(eCharacterClass characterClass, eClassType classType, long botId)
    {
        if (characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger)
            return eCraftingSkill.Fletching;
        if (classType == eClassType.ListCaster)
            return eCraftingSkill.Tailoring;
        return botId % 2 == 0 ? eCraftingSkill.WeaponCrafting : eCraftingSkill.ArmorCrafting;
    }

    /// <summary>
    /// The order a new crafter of a realm joins (owner, plan §1: realm weights first, DR's class rule as the tie-break).
    /// Among the orders <paramref name="available"/> accepts (a master of the realm exists), the one furthest below its
    /// weighted share of the realm's crafters once this one is counted; orders within <see cref="OrderTieWindow"/> of the
    /// largest shortfall are a tie, won by <paramref name="classOrder"/>, else the largest shortfall. NoCrafting when no order
    /// is available (the caller logs it and the bot stays untrained).
    /// </summary>
    public static eCraftingSkill ChooseOrder(IReadOnlyDictionary<eCraftingSkill, int> realmCounts, Func<eCraftingSkill, bool> available,
        eCraftingSkill classOrder)
    {
        eCraftingSkill[] open = Orders.Where(order => available == null || available(order)).ToArray();
        if (open.Length == 0)
            return eCraftingSkill.NoCrafting;
        double weights = open.Sum(OrderWeight);
        int crafters = open.Sum(order => realmCounts?.GetValueOrDefault(order) ?? 0) + 1;
        double Shortfall(eCraftingSkill order) => OrderWeight(order) / weights * crafters - (realmCounts?.GetValueOrDefault(order) ?? 0);
        double best = open.Max(Shortfall);
        return open
            .Where(order => Shortfall(order) >= best - OrderTieWindow)
            .OrderByDescending(order => order == classOrder)
            .ThenByDescending(Shortfall)
            .ThenByDescending(OrderWeight)
            .ThenBy(order => (int)order)
            .First();
    }

    // ---- Recipes (DR) ----

    public sealed record Choice(CraftRecipeInfo Recipe, int Skill, int Make, int Gain);

    /// <summary>What one trip makes. <paramref name="OrderId"/> is set for a piece another bot paid for (WP5).</summary>
    public sealed record Session(CraftRecipeInfo Recipe, eCraftingSkill Skill, int Copies, string OrderId = null);

    /// <summary>One material to buy, in whole packs at the merchant's price.</summary>
    public sealed record Purchase(CraftMaterial Material, int Packs, long Copper)
    {
        public int Amount => Packs * Math.Max(1, Material.PackSize);
    }

    /// <summary>The player's realm rule (NormalServerRules.IsAllowedToCraft): the crafter's realm, or no realm when allowed.</summary>
    public static bool RealmAllows(int productRealm, eRealm realm, bool allowNoRealm) =>
        productRealm == (int)realm || productRealm == 0 && allowNoRealm;

    /// <summary>The secondary skills a recipe asks for are high enough (AbstractCraftingSkill.CheckSecondCraftingSkillRequirement).</summary>
    public static bool MeetsSecondaryRequirement(CraftRecipeInfo recipe, string saved)
    {
        int minimum = recipe.SecondaryMinimum;
        if (minimum <= 0)
            return true;
        foreach (CraftMaterial material in recipe.Materials ?? [])
        {
            eCraftingSkill secondary = material.SecondarySkill;
            if (secondary != eCraftingSkill.NoCrafting && BotCraftingSkills.Value(saved, secondary) < minimum)
                return false;
        }
        return true;
    }

    /// <summary>
    /// DR's ChooseRecipe on the player's tables (CraftRules): only recipes that can still teach a point and be made, that
    /// the bot can pay for, of a product its realm may craft and with the materials on sale. Among those that teach
    /// about the best (<see cref="GainTieShare"/>), a piece the bot or its group could use first, then the hardest recipe,
    /// then the cheapest. DR's run of 2026-11-01: the cheapest-first rule kept level 45 crafters on level 1 gear.
    /// </summary>
    public static Choice ChooseRecipe(IEnumerable<CraftRecipeInfo> recipes, eCraftingSkill craft, string saved, eRealm realm,
        long coin, Func<CraftRecipeInfo, bool> onSale, Func<CraftRecipeInfo, bool> useful = null, bool allowNoRealm = false)
    {
        int skill = BotCraftingSkills.Value(saved, craft);
        var teaching = new List<Choice>();
        foreach (CraftRecipeInfo recipe in recipes ?? [])
        {
            if (recipe == null || recipe.Skill != craft || !RealmAllows(recipe.Realm, realm, allowNoRealm))
                continue;
            int make = CraftRules.ChanceToMake(skill, recipe.Level);
            int gain = CraftRules.BaseChanceToGainPoint(skill, recipe.Level);
            if (make <= 0 || gain <= 0 || recipe.MaterialCostCopper > coin || !MeetsSecondaryRequirement(recipe, saved) ||
                onSale != null && !onSale(recipe))
                continue;
            teaching.Add(new Choice(recipe, skill, make, gain));
        }
        if (teaching.Count == 0)
            return null;
        int best = teaching.Max(choice => choice.Make * choice.Gain);
        return teaching
            .Where(choice => choice.Make * choice.Gain >= best * GainTieShare)
            .OrderByDescending(choice => useful?.Invoke(choice.Recipe) == true)
            .ThenByDescending(choice => choice.Recipe.Level)
            .ThenBy(choice => choice.Recipe.MaterialCostCopper)
            .ThenBy(choice => choice.Recipe.RecipeId, StringComparer.Ordinal)
            .First();
    }

    /// <summary>A piece fits a wearer of this level: no higher than the level, and not more than the window below it (DR).</summary>
    public static bool FitsLevel(int productLevel, int wearerLevel) =>
        productLevel <= wearerLevel && productLevel + UsefulLevelWindow >= wearerLevel;

    /// <summary>What must be bought for <paramref name="copies"/> of a recipe, given what the bot carries (DR).</summary>
    public static List<Purchase> Shopping(CraftRecipeInfo recipe, int copies, Func<CraftMaterial, int> carried)
    {
        var list = new List<Purchase>();
        foreach (CraftMaterial material in recipe?.Materials ?? [])
        {
            int missing = material.Count * copies - (carried?.Invoke(material) ?? 0);
            if (missing <= 0)
                continue;
            int pack = Math.Max(1, material.PackSize);
            int packs = (missing + pack - 1) / pack;
            list.Add(new Purchase(material, packs, packs * material.UnitPriceCopper));
        }
        return list;
    }

    /// <summary>How many copies the bot's coin share pays for, within a session (DR).</summary>
    public static int CopiesFor(CraftRecipeInfo recipe, long coin)
    {
        long budget = (long)(coin * ShareOfCoin);
        long cost = Math.Max(1, recipe?.MaterialCostCopper ?? 1);
        return (int)Math.Clamp(budget / cost, 0, CraftsPerSession);
    }

    /// <summary>
    /// The copies a session plans after the coin (<see cref="CopiesFor"/>), cut to the bag's room for a piece that does not
    /// stack (<paramref name="freeSlots"/> less <see cref="ReservedBagSlots"/>) and to what the trip's time left can make:
    /// each piece takes <paramref name="secondsPerAttempt"/> per attempt over the chance to make. 0 when none fits.
    /// </summary>
    public static int CopiesThatFit(int copies, bool stackable, int freeSlots, int secondsPerAttempt, int makePercent, long msLeft)
    {
        if (copies <= 0 || makePercent <= 0 || msLeft <= 0)
            return 0;
        if (!stackable)
            copies = Math.Min(copies, freeSlots - ReservedBagSlots);
        double msPerPiece = Math.Max(1, secondsPerAttempt) * 1000.0 * 100 / makePercent;
        int byTime = (int)Math.Floor(msLeft / msPerPiece);
        return Math.Max(0, Math.Min(copies, byTime));
    }

    /// <summary>Whether a trip that has worked <paramref name="sessionsDone"/> sessions in <paramref name="tripMs"/> plans
    /// another (<see cref="SessionsPerTrip"/>, <see cref="TripWorkMilliseconds"/>).</summary>
    public static bool AnotherSession(int sessionsDone, long tripMs) =>
        sessionsDone < SessionsPerTrip && tripMs < TripWorkMilliseconds;

    /// <summary>
    /// Whether a Crafter's task break becomes a craft trip (DR's WantsTrip with our chances): never below level 5; an
    /// untrained Crafter goes to join an order at <see cref="JoinChance"/>; a trained one with three stacks of its
    /// materials by DR's engine rule, otherwise at <see cref="SessionChance"/>. One roll in [0, 1).
    /// </summary>
    public static bool WantsTrip(bool trained, int level, int materialStacks, double roll)
    {
        if (level < MinimumBotLevel)
            return false;
        if (!trained)
            return roll < JoinChance;
        return materialStacks >= MaterialStacksForCraftBreak && roll < MaterialCraftBreakChance || roll < SessionChance;
    }

    /// <summary>The station a recipe needs, by the crafts' CheckForTools (DR's StationFor, with the alchemy table).</summary>
    public static CraftStation StationFor(eCraftingSkill skill, int productObjectType, IEnumerable<int> materialModels) => skill switch
    {
        eCraftingSkill.WeaponCrafting or eCraftingSkill.ArmorCrafting or eCraftingSkill.MetalWorking => CraftStation.Forge,
        eCraftingSkill.Tailoring => materialModels?.Contains(519) == true ? CraftStation.Forge : CraftStation.None,
        eCraftingSkill.Fletching => productObjectType is (int)eObjectType.Arrow or (int)eObjectType.Bolt ? CraftStation.None : CraftStation.Lathe,
        eCraftingSkill.Alchemy or eCraftingSkill.SpellCrafting => CraftStation.AlchemyTable,
        _ => CraftStation.None,
    };

    /// <summary>The station a crafter of an order stands at on a town break (AutonomousTownHangouts' station hangout): a
    /// forge for weapons, armor and tailoring (tailors work metal at the forge too), a lathe for fletching, the alchemy
    /// table for alchemy and spellcraft; none for siegecraft.</summary>
    public static CraftStation HangoutStationFor(eCraftingSkill order) => order switch
    {
        eCraftingSkill.WeaponCrafting or eCraftingSkill.ArmorCrafting or eCraftingSkill.Tailoring => CraftStation.Forge,
        eCraftingSkill.Fletching => CraftStation.Lathe,
        eCraftingSkill.Alchemy or eCraftingSkill.SpellCrafting => CraftStation.AlchemyTable,
        _ => CraftStation.None,
    };

    /// <summary>A station's static item: forge (model 478 or named "forge"), lathe (481), alchemy table (820), as the crafts check.</summary>
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

    // ---- Skill gains (the player's crafts' GainCraftingSkillPoints) ----

    /// <summary>
    /// The skills one gain raises, as each craft's GainCraftingSkillPoints does: the professions raise themselves and,
    /// per ingredient, the secondary skill of its material; alchemy raises herbalism and spellcraft gem cutting with
    /// them; a secondary craft and siegecraft raise only themselves. Primary is false for a skill capped at 1300.
    /// </summary>
    public static List<(eCraftingSkill Skill, bool Primary)> GainedSkills(eCraftingSkill craft, IEnumerable<int> materialModels)
    {
        var gains = new List<(eCraftingSkill, bool)>();
        switch (craft)
        {
            case eCraftingSkill.WeaponCrafting:
            case eCraftingSkill.ArmorCrafting:
            case eCraftingSkill.Tailoring:
            case eCraftingSkill.Fletching:
                gains.Add((craft, true));
                foreach (int model in materialModels ?? [])
                {
                    eCraftingSkill secondary = CraftRules.SecondarySkillForMaterial(model);
                    if (secondary != eCraftingSkill.NoCrafting)
                        gains.Add((secondary, false));
                }
                break;
            case eCraftingSkill.Alchemy:
                gains.Add((craft, true));
                gains.Add((eCraftingSkill.HerbalCrafting, false));
                break;
            case eCraftingSkill.SpellCrafting:
                gains.Add((craft, true));
                gains.Add((eCraftingSkill.GemCutting, false));
                break;
            case eCraftingSkill.SiegeCrafting:
                gains.Add((craft, true));
                break;
            case eCraftingSkill.NoCrafting:
            case eCraftingSkill.BasicCrafting:
                break;
            default:
                gains.Add((craft, false)); // metalworking, leathercraft, clothworking, woodworking, gem cutting, herbalism
                break;
        }
        return gains;
    }

    // ---- Pace (True165's CraftingRate165.Points with the bot XP rate) ----

    /// <summary>No cap (the main trades).</summary>
    public const int NoCap = int.MaxValue;
    /// <summary>A hand-edited huge rate is held here.</summary>
    public const int MaxRate = 100;

    /// <summary>A rate as whole points per gain: not a number, infinite or below 1 counts as 1; fractions drop (True165).</summary>
    public static int SanitizeRate(double rate) =>
        double.IsFinite(rate) && rate >= 1 ? (int)Math.Min(MaxRate, Math.Floor(rate)) : 1;

    /// <summary>The points one gain adds to a skill at <paramref name="current"/>: the rate, never past the cap (True165's Points).</summary>
    public static int Points(int current, double rate, int cap = NoCap)
    {
        if (current >= cap)
            return 0;
        long room = (long)cap - current;
        return (int)Math.Min(SanitizeRate(rate), room);
    }

    /// <summary>
    /// The bot crafting pace (bot_crafting_pace): "xp" (the default) gives a gain as many points as the bot XP multiplier
    /// of the crafter's level band (bot_xp_rate, or bot_xp_rate_by_level), whole points, at least one; "classic" one point.
    /// DR's overnight run left the median crafter at skill 8 at player pace; bots level at the bot XP multiplier, so their
    /// crafts keep up the same way (decision 2 of the design doc).
    /// </summary>
    public static double PaceRate(string pace, double botXpRate) =>
        string.Equals(pace?.Trim(), "classic", StringComparison.OrdinalIgnoreCase) ? 1.0 : botXpRate;

    /// <summary>The points one gain gives at this pace: <see cref="Points"/> of the rate, capped for a secondary skill.</summary>
    public static int PointsPerGain(string pace, double botXpRate, int current, bool primary) =>
        Points(current, PaceRate(pace, botXpRate), primary ? NoCap : CraftRules.SubSkillCap);

    // ---- Titles (GamePlayer's title rungs, True165's TitlesReached) ----

    /// <summary>The title rungs (each 100 up to 1000) a gain from <paramref name="before"/> to <paramref name="after"/> reached, lowest first (True165).</summary>
    public static List<int> TitlesReached(int before, int after)
    {
        var titles = new List<int>();
        int top = Math.Min(after, 1000);
        if (top <= before)
            return titles;
        int first = before < 0 ? 0 : (before / 100 + 1) * 100;
        for (int level = first; level <= top; level += 100)
            titles.Add(level);
        return titles;
    }

    /// <summary>The profession word of an order (language/EN/Crafting.txt, CraftersProfession.*).</summary>
    public static string Profession(eCraftingSkill order) => order switch
    {
        eCraftingSkill.WeaponCrafting => "Weaponcrafter",
        eCraftingSkill.ArmorCrafting => "Armorer",
        eCraftingSkill.Tailoring => "Tailor",
        eCraftingSkill.Fletching => "Fletcher",
        eCraftingSkill.SpellCrafting => "Spellcrafter",
        eCraftingSkill.Alchemy => "Alchemist",
        eCraftingSkill.SiegeCrafting => "Siegecrafter",
        _ => "Crafter",
    };

    /// <summary>
    /// The crafting title at a skill (AbstractProfession.GetTitleFormat with the English Crafting.txt): Helper below 100,
    /// then Junior Apprentice ... Grandmaster, Legendary from 1000. The 1100 rung (Legendary Grandmaster) is post-era and
    /// never reached as a title event (TitlesReached stops at 1000), so it is not named here.
    /// </summary>
    public static string Title(eCraftingSkill order, int skill)
    {
        string profession = Profession(order);
        return (Math.Max(0, skill) / 100) switch
        {
            0 => $"{profession}'s Helper",
            1 => $"Junior Apprentice {profession}",
            2 => $"Apprentice {profession}",
            3 => $"Neophyte {profession}",
            4 => $"Assistant {profession}",
            5 => $"Junior {profession}",
            6 => $"Journeyman {profession}",
            7 => $"Senior {profession}",
            8 => $"Master {profession}",
            9 => $"Grandmaster {profession}",
            _ => $"Legendary {profession}",
        };
    }

    /// <summary>The server-first title of the first Legendary of an order in a realm (design doc facet E). Players earn the
    /// same title (WP3 may reuse it), so a bot and a player compete for one first.</summary>
    public static string LegendaryFirstTitle(eCraftingSkill order, eRealm realm) =>
        $"First Legendary {Profession(order)} of {GlobalConstants.RealmToName(realm)}";

    // ---- True165: list only the best of a session; siege kit tiers ----

    /// <summary>
    /// The product of a session that is listed (True165's BestProductIndex): only one per session, the best - a
    /// masterpiece, else the highest quality, gear before stackables. -1 for none.
    /// </summary>
    public static int BestProductIndex(IReadOnlyList<(bool Stackable, int Quality)> products)
    {
        int best = -1;
        for (int i = 0; i < (products?.Count ?? 0); i++)
        {
            if (best < 0 || products[best].Stackable && !products[i].Stackable ||
                products[best].Stackable == products[i].Stackable && products[i].Quality > products[best].Quality)
                best = i;
        }
        return best;
    }

    /// <summary>
    /// True165's siege kit tiers (CrafterBots165.SiegeKits): a ram from skill 1, a catapult from 300, a trebuchet from 600.
    /// The highest tier a siegecrafter of this skill builds. Our world has no siegecraft master and no siegecraft recipe
    /// (M0 findings §2), so no gamebot joins siegecraft today; the tiers are kept for bot_siege_supply (WP7).
    /// </summary>
    public static BotSiegeKind SiegeKitTier(int skill) =>
        skill >= 600 ? BotSiegeKind.Trebuchet : skill >= 300 ? BotSiegeKind.Catapult : BotSiegeKind.Ram;

    // ---- Bounded work per tick ----

    /// <summary>Session plans (a recipe scan) the whole server starts in one game-loop tick.</summary>
    public const int SessionPlansPerTick = 4;
    /// <summary>Craft attempts the whole server resolves in one game-loop tick.</summary>
    public const int AttemptsPerTick = 8;

    /// <summary>
    /// A per-tick budget shared by parallel brain turns: <see cref="TryTake"/> is true for the first <c>limit</c> calls
    /// of a tick and false after, lock-free. A bot that is refused simply tries again on its next turn.
    /// </summary>
    public sealed class TickBudget
    {
        private sealed record State(long Tick, int Used);

        private readonly int _limit;
        private State _state = new(long.MinValue, 0);

        public TickBudget(int limit) => _limit = Math.Max(1, limit);

        public bool TryTake(long tick)
        {
            while (true)
            {
                State current = System.Threading.Volatile.Read(ref _state);
                int used = current.Tick == tick ? current.Used : 0;
                if (used >= _limit)
                    return false;
                if (System.Threading.Interlocked.CompareExchange(ref _state, new State(tick, used + 1), current) == current)
                    return true;
            }
        }
    }
}
