using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// What a rehearsal run plays: the world (realms, bots per realm, fresh or aged), the clock (1-minute steps; 10,080 is a
    /// week), the seed and the settings, by their CraftingEconomyProperties keys. The scenario never reads or writes the
    /// server's static settings, so runs are independent of each other and of the test process.
    /// </summary>
    public sealed record RehearsalScenario(string Name, int Realms = 3, int BotsPerRealm = 1000, int Minutes = RehearsalScenario.Week,
        int Seed = 1, bool Aged = false, IReadOnlyDictionary<string, string> Settings = null)
    {
        public const int Day = 1_440;
        public const int Week = 10_080;

        /// <summary>
        /// The shipped defaults (CRAFTING-2.0-BUILD-PLAN 2.4; every gamebot switch on since the owner's 2026-11-16 rule), and the
        /// package that first reads each one.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string Default, string Package)> Known =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["crafting_era_start"] = ("True", "WP3"),
                ["crafting_secondary_caps"] = ("False", "WP3"),
                ["crafting_skill_rate"] = ("1.0", "WP1"),
                ["player_upkeep"] = ("light", "WP1"),
                ["player_coin_by_level"] = ("True", "WP3"),
                ["crafting_classic_quality"] = ("False", "WP1"),
                ["crafting_classic_losses"] = ("False", "WP1"),
                ["bot_crafters"] = ("True", "WP4"),
                ["bot_craft_orders"] = ("True", "WP5"),
                ["bot_crafter_share_percent"] = ("7", "WP4"),
                ["bot_crafting_pace"] = ("xp", "WP4"),
                ["realm_commissions"] = ("doubled", "WP5"),
                ["exchange_tax_percent"] = ("0", "WP5"),
                ["bot_upkeep"] = ("True", "WP7"),
                ["bot_luxury_spending"] = ("True", "WP7"),
                ["bot_siege_supply"] = ("True", "WP7"),
                ["bot_crafter_chat_rate"] = ("1.0", "WP4"),
                ["bot_crafter_exchange_listings"] = ("6", "WP5"),
                ["launcher_level50_kit"] = ("True", "WP9"),
                ["economy_ledger_enabled"] = ("True", "WP2"),
                ["bot_crafter_commerce"] = ("True", "WP8"),
                ["bot_crafting_social"] = ("True", "WP8"),
            };

        public string Text(string key) =>
            Settings != null && Settings.TryGetValue(key, out string value) ? value
            : Known.TryGetValue(key, out var known) ? known.Default : string.Empty;

        public bool Flag(string key) => bool.TryParse(Text(key), out bool on) && on;

        public int Int(string key) => int.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        /// <summary>The settings that differ from the plan's defaults.</summary>
        public IEnumerable<string> ChangedSettings() => (Settings ?? new Dictionary<string, string>())
            .Where(pair => !Known.TryGetValue(pair.Key, out var known) ||
                           !string.Equals(known.Default, pair.Value, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal);

        public RehearsalScenario With(string key, string value)
        {
            var settings = new Dictionary<string, string>(Settings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            {
                [key] = value,
            };
            return this with { Settings = settings };
        }

        public RehearsalScenario Named(string name) => this with { Name = name };

        /// <summary>The gamebot switches "today" turns off (the economy before Crafting 2.0).</summary>
        public static readonly string[] BotSideSwitches =
            ["bot_crafters", "bot_craft_orders", "bot_upkeep", "bot_luxury_spending", "bot_siege_supply", "bot_crafter_commerce",
             "bot_crafting_social"];

        // The plan's scenarios (3.2). "Today" is the economy before Crafting 2.0, every bot-side switch off (set here, since the
        // defaults are on from 2026-11-16); the others start from the shipped defaults, so they rehearse everything on.
        public static RehearsalScenario Today(int realms = 3, int bots = 1000, int minutes = Week, int seed = 1) =>
            BotSideSwitches.Aggregate(new RehearsalScenario("today-aged", realms, bots, minutes, seed, Aged: true),
                (scenario, key) => scenario.With(key, "False"));

        /// <summary>Fresh start: everyone level 1, every switch at its default (all on), the 7% share at creation.</summary>
        public static RehearsalScenario FreshStart(int realms = 3, int bots = 1000, int minutes = Week, int seed = 1) =>
            new RehearsalScenario("fresh-7pct", realms, bots, minutes, seed, Aged: false).With("bot_crafters", "True");

        /// <summary>An aged world whose bots keep their legacy 12% types, crafters on.</summary>
        public static RehearsalScenario AgedLegacy(int realms = 3, int bots = 1000, int minutes = Week, int seed = 1) =>
            new RehearsalScenario("aged-legacy-12pct", realms, bots, minutes, seed, Aged: true).With("bot_crafters", "True");

        public static RehearsalScenario Commissions(string mode, int realms = 3, int bots = 1000, int minutes = Week, int seed = 1) =>
            AgedLegacy(realms, bots, minutes, seed).With("realm_commissions", mode).Named($"commissions-{mode}");

        public static RehearsalScenario Tax(int percent, int realms = 3, int bots = 1000, int minutes = Week, int seed = 1) =>
            AgedLegacy(realms, bots, minutes, seed).With("exchange_tax_percent", percent.ToString(CultureInfo.InvariantCulture))
                .Named($"tax-{percent}");

        /// <summary>upkeep-on: the aged world with every WP7 switch on (the defaults); upkeep-off: every WP7 switch off.</summary>
        public static RehearsalScenario Upkeep(bool on, int realms = 3, int bots = 1000, int minutes = Week, int seed = 1)
        {
            string value = on ? "True" : "False";
            return AgedLegacy(realms, bots, minutes, seed).With("bot_upkeep", value).With("bot_luxury_spending", value)
                .With("bot_siege_supply", value).Named(on ? "upkeep-on" : "upkeep-off");
        }

        public static IEnumerable<RehearsalScenario> PlanScenarios(int realms = 3, int bots = 1000, int minutes = Week, int seed = 1)
        {
            yield return Today(realms, bots, minutes, seed);
            yield return FreshStart(realms, bots, minutes, seed);
            yield return AgedLegacy(realms, bots, minutes, seed);
            foreach (string mode in new[] { "classic", "doubled", "off" })
                yield return Commissions(mode, realms, bots, minutes, seed);
            yield return Tax(0, realms, bots, minutes, seed);
            yield return Tax(5, realms, bots, minutes, seed);
            yield return Upkeep(true, realms, bots, minutes, seed);
            yield return Upkeep(false, realms, bots, minutes, seed);
        }
    }

    #region Fixtures

    /// <summary>fixtures/calibration.json (M0's measured numbers and the marked assumptions).</summary>
    public sealed class RehearsalCalibration
    {
        public sealed class Band
        {
            public string Name { get; set; }
            public int MinLevel { get; set; }
            public int MaxLevel { get; set; }
            public double SoloKillsPerHour { get; set; }
            public double GroupKillsPerHour { get; set; }
            public bool Measured { get; set; }
        }

        public sealed class CoinPart
        {
            public double Base { get; set; } = 2;
            public double CubeDivisor { get; set; } = 8;
            public double ExtraUpTo { get; set; } = 0.5;
            public double MoneyDrop { get; set; } = 1;
            public int MonsterLevelOverBot { get; set; }
            public int MonsterLevelAt50 { get; set; } = 52;
            public Dictionary<string, double> GrossGoldPerHourSolo { get; set; } = new();
        }

        public sealed class GroupPart
        {
            public double ShareOfTasksGrouped { get; set; } = 0.45;
            public int GroupSize { get; set; } = 3;
        }

        public sealed class LootPart
        {
            public double RogSoloChance { get; set; } = 0.24;
            public Dictionary<string, double> RogGroupChance { get; set; } = new();
            public int RogQualityMin { get; set; } = 85;
            public int RogQualityMax { get; set; } = 100;
            public double RogUtilityPerLevel { get; set; } = 0.9;
            public int RogUtilityFloor { get; set; } = 15;
            public double TrashVendorShareOfCoin { get; set; } = 0.1;
            public int BagSlots { get; set; } = 40;
        }

        public sealed class ShoppingPart
        {
            public Dictionary<string, double> ExchangeAttemptsPerBotHour { get; set; } = new();
            public double VendorAttemptsPerBotHour { get; set; }
            public double ServicePurchasesPerBotHour { get; set; }
            public long ServicePriceCopper { get; set; }
            public double ListingDispositionMin { get; set; } = -0.35;
            public double ListingDispositionMax { get; set; } = 0.5;
            public int ListingHours { get; set; } = 24;
        }

        public sealed class TravelPart
        {
            public double TicketsPerTaskBreak { get; set; }
            public long TicketPriceCopper { get; set; }
        }

        public sealed class TimePart
        {
            public int TaskMinutesMin { get; set; } = 30;
            public int TaskMinutesMax { get; set; } = 90;
            public int TownBreakMinutesMin { get; set; } = 15;
            public int TownBreakMinutesMax { get; set; } = 30;
        }

        public sealed class LevelingPart
        {
            public double HoursToLevelBase { get; set; } = 0.045;
            public double HoursToLevelGrowth { get; set; } = 1.135;
            public double GroupXpFactor { get; set; } = 1.15;
        }

        public sealed class AgedShare
        {
            public int MinLevel { get; set; }
            public int MaxLevel { get; set; }
            public double Share { get; set; }
            public double MedianPurseGold { get; set; }
            public bool Measured { get; set; }
        }

        public sealed class AgedPart
        {
            public List<AgedShare> LevelShares { get; set; } = new();
            public double PurseSpread { get; set; } = 0.6;
        }

        public sealed class ObservedPart
        {
            public double LootUpgradesPerBotHour { get; set; }
            public Dictionary<string, double> NetPurseGoldPer56h { get; set; } = new();
        }

        public sealed class CraftingPart
        {
            public int CapitalGainBonus { get; set; }
            public int SellRatioPercent { get; set; } = 50;
            public double ReinvestShareOfPurse { get; set; } = 0.5;
            public int LegendarySkill { get; set; } = 1000;
            public int TitleEvery { get; set; } = 100;
        }

        public sealed class GatesPart
        {
            public double SupplyGrowthWeekPercentMax { get; set; } = 400;
            public double SupplyGrowthPerBotHourGoldMax { get; set; } = 100;
            public double OrderWaitMedianMinutesMax { get; set; } = 120;
        }

        public string Source { get; set; }
        public List<Band> Bands { get; set; } = new();
        public CoinPart Coin { get; set; } = new();
        public GroupPart Groups { get; set; } = new();
        public LootPart Loot { get; set; } = new();
        public ShoppingPart Shopping { get; set; } = new();
        public TravelPart Travel { get; set; } = new();
        public TimePart Time { get; set; } = new();
        public LevelingPart Leveling { get; set; } = new();
        public AgedPart AgedWorld { get; set; } = new();
        public ObservedPart Observed { get; set; } = new();
        public CraftingPart Crafting { get; set; } = new();
        public GatesPart Gates { get; set; } = new();

        public Band BandOf(int level) =>
            Bands.FirstOrDefault(band => level >= band.MinLevel && level <= band.MaxLevel) ?? Bands.Last();

        public static string BandName(int level) => level switch
        {
            <= 20 => "1-20",
            <= 30 => "21-30",
            <= 40 => "31-40",
            <= 49 => "41-49",
            _ => "50",
        };

        public static readonly string[] BandNames = ["1-20", "21-30", "31-40", "41-49", "50"];

        public static int BandIndex(int level) => level switch { <= 20 => 0, <= 30 => 1, <= 40 => 2, <= 49 => 3, _ => 4 };

        public double KillsPerHour(int level, bool grouped)
        {
            Band band = BandOf(level);
            return grouped ? band.GroupKillsPerHour : band.SoloKillsPerHour;
        }

        public int MonsterLevel(int botLevel) => botLevel >= 50 ? Coin.MonsterLevelAt50 : Math.Max(1, botLevel + Coin.MonsterLevelOverBot);

        /// <summary>LootGeneratorMoney's coin for one kill before the "up to half again" roll, in copper.</summary>
        public double CoinPerKillBase(int monsterLevel) =>
            (Coin.Base + Math.Pow(monsterLevel + 1, 3) / Coin.CubeDivisor) * Coin.MoneyDrop;

        public double RogChance(int level, bool grouped) => !grouped ? Loot.RogSoloChance
            : level <= 27 ? Loot.RogGroupChance.GetValueOrDefault("upTo27", 0.14)
            : level <= 40 ? Loot.RogGroupChance.GetValueOrDefault("upTo40", 0.11)
            : Loot.RogGroupChance.GetValueOrDefault("over40", 0.08);

        public double ExchangeAttemptsPerHour(int level) =>
            Shopping.ExchangeAttemptsPerBotHour.GetValueOrDefault(level >= 50 ? "50" : "1-49", 0);

        public double HoursToLevel(int level) => Leveling.HoursToLevelBase * Math.Pow(Leveling.HoursToLevelGrowth, level);
    }

    /// <summary>A recipe of fixtures/recipes.json: the contract's pure record plus the product's stats.</summary>
    public sealed record RehearsalRecipe(CraftRecipeInfo Info, int Quality, int DpsAf, int SpdAbs, int Hand, int MaxCondition,
        long ProductPrice, string Hidden, bool AllMaterialsSold)
    {
        public bool Craftable => Hidden == null && AllMaterialsSold && Info.Materials.Count > 0;

        /// <summary>A crafted copy of the product (an in-memory item; never saved).</summary>
        public DbInventoryItem Make(int quality, string creator)
        {
            var template = new DbItemTemplate
            {
                Id_nb = Info.ProductId,
                Name = Info.ProductName,
                Level = Info.ProductLevel,
                Object_Type = Info.ObjectType,
                Item_Type = Info.ItemType,
                Quality = quality,
                DPS_AF = DpsAf,
                SPD_ABS = SpdAbs,
                Hand = Hand,
                MaxCondition = MaxCondition,
                Condition = MaxCondition,
                MaxDurability = MaxCondition,
                Durability = MaxCondition,
                Price = ProductPrice,
                Realm = Info.Realm,
                IsDropable = true,
                IsPickable = true,
                IsTradable = true,
                PackSize = 1,
                MaxCount = Info.Stackable ? 50 : 1,
            };
            var item = GameInventoryItem.Create(template);
            item.IsCrafted = true;
            item.Creator = creator;
            item.Quality = quality;
            return item;
        }
    }

    /// <summary>fixtures/recipes.json: the world's recipes (source/tools/export_recipe_fixture.py; world tables only).</summary>
    public sealed class RehearsalRecipes
    {
        public string Source { get; init; }
        public IReadOnlyList<RehearsalRecipe> All { get; init; }
        public int HiddenCount { get; init; }
        /// <summary>The median coin-merchant price of weapon and armor by item level (levels with no merchant gear absent).</summary>
        public IReadOnlyDictionary<int, long> MerchantGearPriceByLevel { get; init; }

        private Dictionary<(eCraftingSkill, int), RehearsalRecipe[]> _byRealm;

        /// <summary>The craftable recipes of a skill a realm can make (product realm 0 or the realm's), by level.</summary>
        public RehearsalRecipe[] For(eCraftingSkill skill, eRealm realm)
        {
            _byRealm ??= All.Where(recipe => recipe.Craftable)
                .SelectMany(recipe => recipe.Info.Realm == 0
                    ? new[] { 1, 2, 3 }.Select(r => (Key: (recipe.Info.Skill, r), Recipe: recipe))
                    : new[] { (Key: (recipe.Info.Skill, recipe.Info.Realm), Recipe: recipe) })
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Recipe).OrderBy(r => r.Info.Level).ThenBy(r => r.Info.RecipeId, StringComparer.Ordinal).ToArray());
            return _byRealm.TryGetValue((skill, (int)realm), out RehearsalRecipe[] list) ? list : Array.Empty<RehearsalRecipe>();
        }

        /// <summary>The merchant gear price at the highest level at or below this one that merchants sell, or 0 (none).</summary>
        public (int Level, long Price) MerchantGearAtOrBelow(int level)
        {
            for (int at = Math.Min(50, level); at >= 1; at--)
                if (MerchantGearPriceByLevel.TryGetValue(at, out long price))
                    return (at, price);
            return (0, 0);
        }
    }

    public static class RehearsalFixtures
    {
        private static readonly Lazy<RehearsalCalibration> _calibration = new(() =>
            JsonConvert.DeserializeObject<RehearsalCalibration>(Read("calibration.json")));

        private static readonly Lazy<RehearsalRecipes> _recipes = new(() => ParseRecipes(Read("recipes.json")));

        public static RehearsalCalibration Calibration => _calibration.Value;
        public static RehearsalRecipes Recipes => _recipes.Value;

        /// <summary>A fixture embedded from Tests/Rehearsal/fixtures (Tests.csproj embeds the folder).</summary>
        public static string Read(string fileName)
        {
            var assembly = typeof(RehearsalFixtures).Assembly;
            string name = assembly.GetManifestResourceNames()
                .Single(n => n.EndsWith("fixtures." + fileName, StringComparison.OrdinalIgnoreCase));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static RehearsalRecipes ParseRecipes(string json)
        {
            JObject root = JObject.Parse(json);
            var materials = ((JObject)root["materials"]).Properties().ToDictionary(p => p.Name, p => (JObject)p.Value, StringComparer.OrdinalIgnoreCase);
            var recipes = new List<RehearsalRecipe>();
            foreach (JObject row in (JArray)root["recipes"])
            {
                var parts = new List<CraftMaterial>();
                bool allSold = true;
                foreach (JArray part in (JArray)row["materials"])
                {
                    string id = (string)part[0];
                    int count = (int)part[1];
                    if (materials.TryGetValue(id, out JObject material))
                    {
                        parts.Add(new CraftMaterial(id, (string)material["name"], (int)material["model"], count,
                            (long)material["price"], (int)material["packSize"]));
                        allSold &= (bool)material["sold"];
                    }
                    else
                        allSold = false;
                }
                int Int(string key, int usual = 0) => row[key] is JToken token ? (int)token : usual;
                bool stackable = row["stackable"] is JToken s && (bool)s;
                var info = new CraftRecipeInfo(((int)row["id"]).ToString(CultureInfo.InvariantCulture), (eCraftingSkill)(int)row["skill"],
                    (int)row["level"], (string)row["product"], (string)row["name"] ?? string.Empty, Int("objectType"), Int("itemType"),
                    Int("productLevel"), Int("realm"), Unique: !(row["templated"] is JToken t && (bool)t) && !stackable, stackable, parts);
                recipes.Add(new RehearsalRecipe(info, Int("quality", 85), Int("dpsAf"), Int("spdAbs"), Int("hand"), Int("maxCondition", 50000),
                    Int("price"), (string)row["hidden"], allSold));
            }
            var ladder = ((JObject)root["merchantGearPriceByLevel"]).Properties()
                .ToDictionary(p => int.Parse(p.Name, CultureInfo.InvariantCulture), p => (long)p.Value);
            return new RehearsalRecipes
            {
                Source = (string)root["source"],
                All = recipes,
                HiddenCount = (int)root["hiddenCount"],
                MerchantGearPriceByLevel = ladder,
            };
        }
    }

    #endregion

    /// <summary>A listing on a realm's Exchange in the rehearsal (in memory; the real broker is never touched).</summary>
    public sealed class RehearsalListing
    {
        public RehearsalAgent Seller { get; init; }
        public DbInventoryItem Item { get; init; }
        public long Price { get; init; }
        public long Bid { get; init; }
        public int ListedMinute { get; init; }
        public MarketBand Band { get; init; }
        public bool Gone { get; set; }
    }

    /// <summary>Coin in and out of one kind in one hour of one realm.</summary>
    public sealed class FlowTotals
    {
        public long In;
        public long Out;
        public int Count;
    }

    /// <summary>One hour of one realm: the ledger row (the server's ECON_SUPPLY, plus what the rehearsal can see).</summary>
    public sealed class RehearsalHourRow
    {
        public int Hour { get; init; }
        public DateTime HourUtc { get; init; }
        public eRealm Realm { get; init; }
        public long SupplyCopper { get; init; }
        public long DeltaCopper { get; init; }
        public long FlowsCopper { get; init; }
        public long ResidualCopper => DeltaCopper - FlowsCopper;
        public long FaucetsCopper { get; init; }
        public long SinksCopper { get; init; }
        public long TransfersCopper { get; init; }
        public IReadOnlyDictionary<EconomyFlowKind, FlowTotals> Flows { get; init; }
        public int Bots { get; init; }
        public int Crafters { get; init; }
        public long CrafterPurseMedian { get; init; }
        public long HunterPurseMedian { get; init; }
        public long CrafterPurseTopFifth { get; init; }
        public long HunterPurseTopFifth { get; init; }
        public double MeanLevel { get; init; }
        public int[] LevelBands { get; init; }
        public int Listings { get; init; }
        public long ListedValue { get; init; }
        public double[] CraftedWornShareByBand { get; init; }
        public double? OrderWaitMedianMinutes { get; init; }
        public IReadOnlyDictionary<string, double> Metrics { get; init; }
    }

    /// <summary>
    /// The rehearsal's world: agents, the clock, per-realm random streams, the Exchange listings and the ledger. Every coin
    /// move goes through <see cref="Earn"/>, <see cref="Pay"/> or <see cref="Transfer"/>, which keep the hourly flows and
    /// forward each move to the flow observers (WP2's ledger sink plugs in there), so the supply's change and the sum of
    /// the flows can be checked against each other exactly. Hooks default to today's code (the seams' defaults, the
    /// registered price index, today's equipment value); modules replace them in Attach.
    /// </summary>
    public sealed class RehearsalWorld
    {
        public static readonly DateTime DefaultStartUtc = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        public RehearsalScenario Scenario { get; }
        public RehearsalCalibration Calibration { get; }
        public RehearsalRecipes Recipes { get; }
        public IReadOnlyList<IRehearsalModule> Modules { get; internal set; } = Array.Empty<IRehearsalModule>();
        public IReadOnlyList<eRealm> Realms { get; }
        public DateTime StartUtc { get; } = DefaultStartUtc;
        public int Minute { get; internal set; }
        public DateTime NowUtc => StartUtc.AddMinutes(Minute);
        public int Hour => Minute / 60;

        public List<RehearsalAgent> Agents { get; } = new();
        private readonly Random[] _random;
        private readonly List<RehearsalListing>[] _listings;
        private readonly Dictionary<EconomyFlowKind, FlowTotals>[] _hourFlows;
        private readonly long[] _hourFlowSum;
        private readonly long[] _lastSupply;
        private readonly Dictionary<string, double>[] _hourMetrics;

        // ---- hooks (today's code by default) ----

        /// <summary>The personality type of a new agent (id, aged world). Today: AutonomousBotPersonality.TypeFor (12% Crafters).</summary>
        public Func<long, bool, BotPersonalityType> TypeOf { get; set; } = (id, _) => AutonomousBotPersonality.TypeFor(id);

        /// <summary>Whether to wear a candidate over what is worn (agent, worn or null, candidate). Today's rule:
        /// AutonomousBotEconomy.EquipmentValue, better by more than 8 or the slot empty (TryGetEquipmentUpgrade).</summary>
        public Func<RehearsalAgent, DbInventoryItem, DbInventoryItem, bool> IsUpgrade { get; set; } = (_, worn, candidate) =>
            worn == null ? AutonomousBotEconomy.EquipmentValue(candidate) > 0
                : AutonomousBotEconomy.EquipmentValue(candidate) - AutonomousBotEconomy.EquipmentValue(worn) > 8;

        /// <summary>The no-undress rule an equip is checked against (agent, slot index, item; the value of the gear before
        /// and after the move, on the rule's own scale). True when the move undresses the agent.</summary>
        public delegate bool UndressRule(RehearsalAgent agent, int slotIndex, DbInventoryItem item, out long before, out long after);

        /// <summary>
        /// The no-undress rule of the server's active upgrade rule. Today's (bot_crafters off): the slot's
        /// AutonomousBotEconomy.EquipmentValue falls, or the slot is emptied; TryGetEquipmentUpgrade only takes a piece
        /// better by more than 8, so no equip may lower it. WP6's adapter replaces it with the server's own guard
        /// (BotGearGuard.Allows on the set score), the rule the server enforces with bot_crafters on.
        /// </summary>
        public UndressRule IsUndress { get; set; } = SlotValueUndress;

        /// <summary>The scale <see cref="IsUndress"/> measures on, for the gate and the log ("slot value" or "set score").</summary>
        public string UndressScale { get; set; } = "slot value";

        public static bool SlotValueUndress(RehearsalAgent agent, int slotIndex, DbInventoryItem item, out long before, out long after)
        {
            DbInventoryItem worn = agent.Worn[slotIndex];
            before = AutonomousBotEconomy.EquipmentValue(worn);
            after = AutonomousBotEconomy.EquipmentValue(item);
            return item == null || worn != null && after < before;
        }

        /// <summary>The price index (PriceIndexes.Current: today's vendor appraisal and listing valuation until WP5/WP6).</summary>
        public IPriceIndex PriceIndex { get; set; } = PriceIndexes.Current;

        /// <summary>What the seller of an Exchange sale receives (EconomySeams.ProceedsOf; the whole price until WP5's tax).</summary>
        public Func<long, eRealm, string, string, long> SellerProceeds { get; set; } = EconomySeams.ProceedsOf;

        /// <summary>Whether an expired listing goes back to its seller (EconomySeams.Reclaim; today it is deleted after 24 h).</summary>
        public Func<DbInventoryItem, long, bool> ReclaimExpired { get; set; } = EconomySeams.Reclaim;

        /// <summary>Every coin move, as the server's EconomyFlows sink would see it (WP2's ledger registers here).</summary>
        public List<IEconomyFlowSink> FlowObservers { get; } = new();

        // ---- gate inputs and counters ----

        public int NegativePurseEvents { get; private set; }
        public int UndressEvents { get; private set; }
        /// <summary>Equips that the undress rule allowed although the slot's own EquipmentValue fell (only possible when
        /// the rule is the set score: a piece of higher quality at 50, or one whose bonuses the set needs). Not undresses:
        /// counted and logged so that every such trade is visible.</summary>
        public int SlotValueTradeoffs { get; private set; }
        public int BidAskViolations { get; private set; }
        public Dictionary<string, (int Quotes, long MaxBid, long MinAsk)> BandQuotes { get; } = new(StringComparer.Ordinal);
        /// <summary>Minutes from posting to collection of every collected order (WP5 adds them).</summary>
        public List<double> OrderWaitMinutes { get; } = new();
        public int OrdersPosted { get; set; }
        public int OrdersNeverCollected { get; set; }
        public Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);
        public List<string> Lines { get; } = new();
        public int LineCap { get; set; } = 400_000;
        public List<RehearsalHourRow> Rows { get; } = new();
        public long[] StartSupply { get; private set; }

        public RehearsalWorld(RehearsalScenario scenario, RehearsalCalibration calibration = null, RehearsalRecipes recipes = null)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            if (scenario.Realms is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(scenario), "1-3 realms");
            Calibration = calibration ?? RehearsalFixtures.Calibration;
            Recipes = recipes ?? RehearsalFixtures.Recipes;
            Realms = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Take(scenario.Realms).ToArray();
            int count = Realms.Count;
            _random = Enumerable.Range(0, count).Select(i => new Random(unchecked(scenario.Seed * 7919 + (i + 1) * 104_729))).ToArray();
            _listings = Enumerable.Range(0, count).Select(_ => new List<RehearsalListing>()).ToArray();
            _hourFlows = Enumerable.Range(0, count).Select(_ => new Dictionary<EconomyFlowKind, FlowTotals>()).ToArray();
            _hourFlowSum = new long[count];
            _lastSupply = new long[count];
            _hourMetrics = Enumerable.Range(0, count).Select(_ => new Dictionary<string, double>(StringComparer.Ordinal)).ToArray();
        }

        public int RealmIndex(eRealm realm) => realm switch { eRealm.Albion => 0, eRealm.Midgard => 1, _ => 2 };

        /// <summary>The realm's name for lines and tables. Not eRealm.ToString(): Albion shares its value with
        /// _FirstPlayerRealm, and ToString() returns that alias.</summary>
        public static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            _ => realm.ToString(),
        };

        /// <summary>The realm's random stream: every draw for the realm's agents comes from it, in agent order, so a seed repeats.</summary>
        public Random RandomFor(eRealm realm) => _random[RealmIndex(realm)];

        public IEnumerable<RehearsalAgent> InRealm(eRealm realm) => Agents.Where(agent => agent.Realm == realm);

        public void Count(string key, long by = 1) => Counters[key] = Counters.GetValueOrDefault(key) + by;

        public void Log(string line)
        {
            if (Lines.Count < LineCap)
                Lines.Add(line);
            else
                Count("lines_dropped");
        }

        /// <summary>A module's value for the hour's row (the last value set in the hour wins).</summary>
        public void HourMetric(eRealm realm, string key, double value) => _hourMetrics[RealmIndex(realm)][key] = value;

        // ---- coin ----

        /// <summary>Coin into a purse from outside the bots' economy (a faucet: loot coin, vendor sales, commissions).</summary>
        public void Earn(RehearsalAgent agent, long copper, EconomyFlowKind kind)
        {
            if (copper <= 0)
                return;
            agent.Purse += copper;
            Record(agent, copper, kind);
        }

        /// <summary>Coin out of a purse to outside (a sink). False, and nothing moves, when the purse is short.</summary>
        public bool Pay(RehearsalAgent agent, long copper, EconomyFlowKind kind)
        {
            if (copper < 0)
                return false;
            if (copper == 0)
                return true;
            if (agent.Purse < copper)
                return false;
            agent.Purse -= copper;
            Record(agent, -copper, kind);
            if (agent.Purse < 0)
                NegativePurseEvents++;
            return true;
        }

        /// <summary>
        /// Coin from one bot to another (an Exchange sale, an order): the buyer pays the price, the seller receives
        /// <see cref="SellerProceeds"/>, and any difference is the tax, a sink. Both sides are flows of <paramref name="kind"/>.
        /// </summary>
        public bool Transfer(RehearsalAgent buyer, RehearsalAgent seller, long price, EconomyFlowKind kind, out long tax)
        {
            tax = 0;
            if (price <= 0 || buyer.Purse < price)
                return false;
            long proceeds = Math.Clamp(SellerProceeds(price, seller.Realm, seller.OwnerId, buyer.OwnerId), 0, price);
            tax = price - proceeds;
            // The buyer's payment is two flows: the proceeds, a transfer to the seller, and the tax, a sink. The transfer
            // kind then nets to zero within the realm and the tax shows as what it is.
            buyer.Purse -= price;
            Record(buyer, -proceeds, kind);
            if (tax > 0)
                Record(buyer, -tax, EconomyFlowKind.ExchangeTax);
            seller.Purse += proceeds;
            Record(seller, proceeds, kind);
            if (buyer.Purse < 0)
                NegativePurseEvents++;
            return true;
        }

        private FlowTotals Totals(int realm, EconomyFlowKind kind)
        {
            if (!_hourFlows[realm].TryGetValue(kind, out FlowTotals totals))
                _hourFlows[realm][kind] = totals = new FlowTotals();
            return totals;
        }

        private void Record(RehearsalAgent agent, long signed, EconomyFlowKind kind)
        {
            int realm = RealmIndex(agent.Realm);
            FlowTotals totals = Totals(realm, kind);
            if (signed > 0)
                totals.In += signed;
            else
                totals.Out -= signed;
            totals.Count++;
            _hourFlowSum[realm] += signed;
            foreach (IEconomyFlowSink observer in FlowObservers)
                observer.Record(agent.Id, signed, kind, "Rehearsal.cs", kind.ToString());
        }

        // ---- gear ----

        /// <summary>Wears an item in its slot (what was worn goes to the bag). Counts an undress when the move breaks
        /// <see cref="IsUndress"/>, which no policy may do (the no-undress gate); <paramref name="source"/> names the path
        /// that made the move (loot, bag, exchange, vendor, gap-buy, crafted).</summary>
        public void Equip(RehearsalAgent agent, int slotIndex, DbInventoryItem item, string source = "other")
        {
            DbInventoryItem worn = agent.Worn[slotIndex];
            bool undress = IsUndress(agent, slotIndex, item, out long before, out long after);
            int oldValue = AutonomousBotEconomy.EquipmentValue(worn), newValue = AutonomousBotEconomy.EquipmentValue(item);
            bool tradeoff = !undress && worn != null && newValue < oldValue;
            if (undress)
                UndressEvents++;
            else if (tradeoff)
                SlotValueTradeoffs++;
            if (undress || tradeoff)
                Log(EconomyLog.Line(EconomyLog.EconUndressGuard, ("hour", NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                    ("event", undress ? "undress" : "tradeoff"), ("level", agent.Level), ("slot", RehearsalAgent.SlotOf(slotIndex)),
                    ("source", source), ("rule", UndressScale), ("before", before), ("after", after),
                    ("oldValue", oldValue), ("newValue", newValue), ("worn", Describe(worn)), ("item", Describe(item))));
            agent.Worn[slotIndex] = item;
            if (worn != null)
                agent.Bag.Add(worn);
            Count(item?.IsCrafted == true ? "equipped_crafted" : "equipped");
        }

        /// <summary>A piece for an undress line: level, quality, DPS/AF, untyped utility, crafted or a drop.</summary>
        private static string Describe(DbInventoryItem item) => item == null ? "empty"
            : $"L{item.Level} q{item.Quality} af{item.DPS_AF} u{Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) + Math.Abs(item.Bonus3)}" +
              (item.IsCrafted ? " crafted" : item.IsROG ? " drop" : " merchant");

        // ---- the Exchange ----

        public IReadOnlyList<RehearsalListing> Listings(eRealm realm) => _listings[RealmIndex(realm)];

        public int ListingsOf(RehearsalAgent agent) => agent.Listings;

        /// <summary>
        /// Lists an item at a price, within the listing caps (EconomySeams.Listings: 3 a bot today). Records the band's
        /// quote: the index's Bid for the item against the asking price (the gate "Bid &lt; Ask in every band").
        /// </summary>
        public bool TryList(RehearsalAgent agent, DbInventoryItem item, long price)
        {
            if (item == null || price <= 0)
                return false;
            IExchangeListingPolicy policy = EconomySeams.Listings;
            int cap = Math.Min(policy.MaxListings(agent.OwnerId), policy.ListingCap(agent.OwnerId, item));
            if (agent.Listings >= cap)
                return false;
            var band = MarketBand.Of(agent.Realm, item);
            long bid = PriceIndex.Bid(item, 0);
            string key = band.Key;
            (int quotes, long maxBid, long minAsk) = BandQuotes.GetValueOrDefault(key, (0, long.MinValue, long.MaxValue));
            BandQuotes[key] = (quotes + 1, Math.Max(maxBid, bid), Math.Min(minAsk, price));
            if (bid >= price)
                BidAskViolations++;
            PriceIndex.ObserveListing(band, price);
            _listings[RealmIndex(agent.Realm)].Add(new RehearsalListing
            {
                Seller = agent, Item = item, Price = price, Bid = bid, ListedMinute = Minute, Band = band,
            });
            agent.Listings++;
            Count("listed");
            if (EconomyDiagnosticsLines)
                Log(EconomyLog.Line(EconomyLog.EconList, ("hour", NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                    ("item", item.Name), ("band", key), ("askCopper", price), ("bidCopper", bid), ("crafted", item.IsCrafted)));
            return true;
        }

        /// <summary>Per-trade ECON_LIST / ECON_SALE lines (the server's econ_trade_lines; off for week runs).</summary>
        public bool EconomyDiagnosticsLines { get; set; }

        /// <summary>A buyer takes a listing: the coin moves as an Exchange sale and the item goes to the buyer's bag.</summary>
        public bool TryBuy(RehearsalAgent buyer, RehearsalListing listing)
        {
            if (listing == null || listing.Gone || listing.Seller == buyer || listing.Seller.Realm != buyer.Realm)
                return false;
            if (!Transfer(buyer, listing.Seller, listing.Price, EconomyFlowKind.ExchangeSale, out long tax))
                return false;
            listing.Gone = true;
            listing.Seller.Listings--;
            PriceIndex.ObserveSale(listing.Band, listing.Price);
            Count("exchange_sales");
            if (listing.Item.IsCrafted)
                Count("exchange_sales_crafted");
            if (EconomyDiagnosticsLines)
                Log(EconomyLog.Line(EconomyLog.EconSale, ("hour", NowUtc), ("realm", RehearsalWorld.RealmName(buyer.Realm)), ("seller", listing.Seller.Name),
                    ("buyer", buyer.Name), ("item", listing.Item.Name), ("priceCopper", listing.Price), ("taxCopper", tax),
                    ("crafted", listing.Item.IsCrafted)));
            return true;
        }

        /// <summary>Takes down listings older than the listing time: back to the seller if a reclaim seam takes it, else deleted (today).</summary>
        public void ExpireListings()
        {
            int maxMinutes = Calibration.Shopping.ListingHours * 60;
            for (int realm = 0; realm < _listings.Length; realm++)
            {
                List<RehearsalListing> list = _listings[realm];
                foreach (RehearsalListing listing in list)
                {
                    if (listing.Gone || Minute - listing.ListedMinute < maxMinutes)
                        continue;
                    listing.Gone = true;
                    listing.Seller.Listings--;
                    if (ReclaimExpired(listing.Item, listing.Seller.Id))
                    {
                        listing.Seller.Bag.Add(listing.Item);
                        Count("listings_reclaimed");
                    }
                    else
                        Count("listings_expired_deleted");
                }
                list.RemoveAll(listing => listing.Gone);
            }
        }

        // ---- the hourly ledger ----

        internal void OpenLedger()
        {
            StartSupply = Realms.Select(realm => InRealm(realm).Sum(agent => agent.Purse)).ToArray();
            Array.Copy(StartSupply, _lastSupply, StartSupply.Length);
        }

        /// <summary>Closes the hour: one row and one ECON_SUPPLY line per realm, one ECON_FLOWS line per kind that moved.</summary>
        internal void CloseHour()
        {
            DateTime hourUtc = StartUtc.AddHours(Hour);
            foreach (eRealm realm in Realms)
            {
                int index = RealmIndex(realm);
                RehearsalAgent[] agents = InRealm(realm).ToArray();
                long supply = agents.Sum(agent => agent.Purse);
                long delta = supply - _lastSupply[index];
                _lastSupply[index] = supply;
                var flows = _hourFlows[index].ToDictionary(pair => pair.Key, pair => pair.Value);
                long transfers = flows.Where(pair => IsTransfer(pair.Key)).Sum(pair => pair.Value.In - pair.Value.Out);
                long faucets = flows.Where(pair => !IsTransfer(pair.Key)).Sum(pair => pair.Value.In);
                long sinks = flows.Where(pair => !IsTransfer(pair.Key)).Sum(pair => pair.Value.Out);
                long[] crafters = agents.Where(a => a.IsCrafter).Select(a => a.Purse).OrderBy(p => p).ToArray();
                long[] hunters = agents.Where(a => !a.IsCrafter).Select(a => a.Purse).OrderBy(p => p).ToArray();
                var bands = new int[5];
                var worn = new int[5];
                var crafted = new int[5];
                foreach (RehearsalAgent agent in agents)
                {
                    int band = RehearsalCalibration.BandIndex(agent.Level);
                    bands[band]++;
                    foreach (DbInventoryItem item in agent.Worn)
                        if (item != null)
                        {
                            worn[band]++;
                            if (item.IsCrafted)
                                crafted[band]++;
                        }
                }
                List<RehearsalListing> listings = _listings[index];
                var row = new RehearsalHourRow
                {
                    Hour = Hour,
                    HourUtc = hourUtc,
                    Realm = realm,
                    SupplyCopper = supply,
                    DeltaCopper = delta,
                    FlowsCopper = _hourFlowSum[index],
                    FaucetsCopper = faucets,
                    SinksCopper = sinks,
                    TransfersCopper = transfers,
                    Flows = flows,
                    Bots = agents.Length,
                    Crafters = crafters.Length,
                    CrafterPurseMedian = Median(crafters),
                    HunterPurseMedian = Median(hunters),
                    CrafterPurseTopFifth = TopFifth(crafters),
                    HunterPurseTopFifth = TopFifth(hunters),
                    MeanLevel = agents.Length == 0 ? 0 : agents.Average(a => a.Level),
                    LevelBands = bands,
                    Listings = listings.Count(l => !l.Gone),
                    ListedValue = listings.Where(l => !l.Gone).Sum(l => l.Price),
                    CraftedWornShareByBand = Enumerable.Range(0, 5).Select(b => worn[b] == 0 ? 0 : (double)crafted[b] / worn[b]).ToArray(),
                    OrderWaitMedianMinutes = OrderWaitMinutes.Count == 0 ? null : MedianOf(OrderWaitMinutes),
                    Metrics = new Dictionary<string, double>(_hourMetrics[index], StringComparer.Ordinal),
                };
                Rows.Add(row);
                Log(EconomyLog.Line(EconomyLog.EconSupply, ("hour", hourUtc), ("realm", RehearsalWorld.RealmName(realm)), ("botCoin", supply),
                    ("deltaCopper", delta), ("flowsCopper", row.FlowsCopper), ("residualCopper", row.ResidualCopper),
                    ("faucetsCopper", faucets), ("sinksCopper", sinks), ("transfersCopper", transfers), ("listedValue", row.ListedValue),
                    ("bots", row.Bots), ("crafters", row.Crafters), ("source", "rehearsal"), ("scenario", Scenario.Name)));
                foreach ((EconomyFlowKind kind, FlowTotals totals) in flows.OrderBy(pair => pair.Key))
                    Log(EconomyLog.Line(EconomyLog.EconFlows, ("hour", hourUtc), ("realm", RehearsalWorld.RealmName(realm)), ("kind", kind),
                        ("inCopper", totals.In), ("outCopper", totals.Out), ("count", totals.Count),
                        ("transfer", IsTransfer(kind)), ("source", "rehearsal"), ("scenario", Scenario.Name)));
                _hourFlows[index].Clear();
                _hourFlowSum[index] = 0;
                _hourMetrics[index].Clear();
            }
        }

        /// <summary>Kinds that move coin between bots (their in and out cancel within the realm, less any tax).</summary>
        public static bool IsTransfer(EconomyFlowKind kind) =>
            kind is EconomyFlowKind.ExchangeSale or EconomyFlowKind.CraftOrder or EconomyFlowKind.CraftSale or EconomyFlowKind.LootShare
                or EconomyFlowKind.BeggarGift;

        public static long Median(IReadOnlyList<long> sorted) => sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];

        public static long TopFifth(IReadOnlyList<long> sorted) =>
            sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.8))];

        public static double MedianOf(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }
    }
}
