using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP0: every seam's default leaves today's behaviour as it was (listing cap 3, proceeds equal to the
    /// price, no reclaim, no steps, no activities, no random number drawn), the registries behave, and the contracts
    /// (settings, skill format, upkeep, log line) say what the build plan says. The existing UT_RealmExchange* tests
    /// run through the seamed code unchanged; UT_RealmExchangeEconomy's "Seam" tests check the seams when filled.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EconomySeamsDefaults
    {
        [SetUp]
        [TearDown]
        public void Reset()
        {
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            BotCommerceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            EconomyFlows.Sink = null;
        }

        /// <summary>Counts every draw, so a test can prove a seam drew none.</summary>
        private sealed class CountingRandom : Random
        {
            public int Draws;
            public CountingRandom() : base(1) { }
            public override int Next() { Draws++; return base.Next(); }
            public override int Next(int maxValue) { Draws++; return base.Next(maxValue); }
            public override int Next(int minValue, int maxValue) { Draws++; return base.Next(minValue, maxValue); }
            public override double NextDouble() { Draws++; return base.NextDouble(); }
            protected override double Sample() { Draws++; return base.Sample(); }
        }

        [Test]
        public void ListingCap_IsTodaysThreeForEveryItem()
        {
            Assert.That(EconomySeams.Listings, Is.SameAs(EconomySeams.DefaultListingPolicy.Instance));
            Assert.That(EconomySeams.Listings.MaxListings("offlinebot:7"), Is.EqualTo(3));
            Assert.That(EconomySeams.Listings.ListingCap("offlinebot:7", null), Is.EqualTo(3));
            Assert.That(EconomySeams.Listings.ListingCap("offlinebot:7", new DbInventoryItem { IsCrafted = true }), Is.EqualTo(3));
            Assert.That(AutonomousBotEconomy.MaxBotRealmExchangeListings, Is.EqualTo(3));
            EconomySeams.Listings = null;
            Assert.That(EconomySeams.Listings, Is.SameAs(EconomySeams.DefaultListingPolicy.Instance), "null restores the default");
        }

        [Test]
        public void SellerProceeds_AreTheWholePrice_AndASeamIsKeptWithinThePrice()
        {
            foreach (long price in new long[] { 1, 99, 400, 1_000_000 })
                Assert.That(EconomySeams.ProceedsOf(price, eRealm.Albion, "offlinebot:1", "offlinebot:2"), Is.EqualTo(price));

            EconomySeams.SellerProceeds = (price, _, _, _) => price * 2;
            Assert.That(EconomySeams.ProceedsOf(400, eRealm.Albion, "a", "b"), Is.EqualTo(400), "never more than the price");
            EconomySeams.SellerProceeds = (_, _, _, _) => -5;
            Assert.That(EconomySeams.ProceedsOf(400, eRealm.Albion, "a", "b"), Is.Zero, "never negative");
            EconomySeams.SellerProceeds = (_, _, _, _) => throw new InvalidOperationException();
            Assert.That(EconomySeams.ProceedsOf(400, eRealm.Albion, "a", "b"), Is.EqualTo(400), "a failing seam pays the price");
        }

        [Test]
        public void Reclaim_DefaultTakesNothing_AndAFailingSeamTakesNothing()
        {
            Assert.That(EconomySeams.Reclaim(new DbInventoryItem(), 5), Is.False);
            EconomySeams.TryReclaimExpiredListing = (_, _) => throw new InvalidOperationException();
            Assert.That(EconomySeams.Reclaim(new DbInventoryItem(), 5), Is.False);
        }

        [Test]
        public void ItemPolicies_DefaultToTodaysRules()
        {
            var item = new DbInventoryItem { IsCrafted = true };
            Assert.That(EconomySeams.IsReserved(null, item), Is.False);
            Assert.That(EconomySeams.KeepFromVendor(null, item), Is.False);
            Assert.That(EconomySeams.BlocksListing(null, item), Is.False);
            Assert.That(EconomySeams.AlwaysWorthListing(null, item), Is.False);
            Assert.That(EconomySeams.ListingPriority(null, item), Is.Zero);
            Assert.That(EconomySeams.IsMaterialFor(null, item, true), Is.True, "today: a trained crafter keeps every stack");
            Assert.That(EconomySeams.IsMaterialFor(null, item, false), Is.False);
            Assert.DoesNotThrow(() => EconomySeams.NotifyListed(null, item));
        }

        private sealed class MaterialPolicy : IBotItemPolicy
        {
            public string Name => "test materials";
            public bool? Answer;
            public bool? IsMaterialFor(GameBot bot, DbInventoryItem item) => Answer;
            public bool IsReserved(GameBot bot, DbInventoryItem item) => item.Name == "reserved";
            public int ListingPriority(GameBot bot, DbInventoryItem item) => item.Name == "first" ? 5 : 0;
            public void OnListed(GameBot bot, DbInventoryItem item) => throw new InvalidOperationException();
        }

        [Test]
        public void ItemPolicies_DecideOnlyWhatTheyImplement()
        {
            var policy = new MaterialPolicy();
            EconomySeams.Register(policy);
            EconomySeams.Register(policy);
            Assert.That(EconomySeams.ItemPolicyCount, Is.EqualTo(1), "registered once");
            Assert.That(EconomySeams.IsMaterialFor(null, new DbInventoryItem(), true), Is.True, "null defers to today's rule");
            policy.Answer = false;
            Assert.That(EconomySeams.IsMaterialFor(null, new DbInventoryItem(), true), Is.False);
            Assert.That(EconomySeams.IsReserved(null, new DbInventoryItem { Name = "reserved" }), Is.True);
            Assert.That(EconomySeams.IsReserved(null, new DbInventoryItem { Name = "other" }), Is.False);
            Assert.That(EconomySeams.ListingPriority(null, new DbInventoryItem { Name = "first" }), Is.EqualTo(5));
            Assert.That(EconomySeams.KeepFromVendor(null, new DbInventoryItem()), Is.False, "an unimplemented member keeps its default");
            Assert.DoesNotThrow(() => EconomySeams.NotifyListed(null, new DbInventoryItem()), "a policy's failure never undoes a listing");
        }

        [Test]
        public void SaleCommitted_ReachesEverySubscriber_EvenWhenOneFails()
        {
            var heard = new List<RealmExchangeSaleLedger.Sale>();
            EconomySeams.SaleCommitted += _ => throw new InvalidOperationException();
            EconomySeams.SaleCommitted += heard.Add;
            var sale = new RealmExchangeSaleLedger.Sale(1, "tunic", 1, 400, "offlinebot:1", "Seller", "offlinebot:2", "Buyer", DateTime.UtcNow);
            Assert.That(sale.TaxCopper, Is.Zero, "no tax by default");
            Assert.That(sale.Crafted, Is.False);
            EconomySeams.RaiseSaleCommitted(sale);
            Assert.That(heard, Is.EqualTo(new[] { sale }));
        }

        [Test]
        public void ServiceSeams_WithNothingRegistered_DoNothingAndDrawNoRandomNumber()
        {
            Assert.That(BotServiceSeams.RegisteredCount, Is.Zero);
            Assert.That(BotServiceSeams.WantsVisit(null), Is.False);
            Assert.That(BotServiceSeams.NeededService(null), Is.Null);
            Assert.That(BotServiceSeams.TryRunSteps(eWorldServiceKind.Vendor, null, null, 0, out BotServiceResult result), Is.False);
            Assert.That(result, Is.EqualTo(default(BotServiceResult)));
            Assert.That(BotServiceSeams.TryRunSteps(eWorldServiceKind.RealmExchange, null, null, 0, out _), Is.False);
            var random = new CountingRandom();
            Assert.That(BotServiceSeams.RollBreakActivities(null, random, out bool inventoryStop, out string text), Is.Empty);
            Assert.That(inventoryStop, Is.False);
            Assert.That(text, Is.Empty);
            Assert.That(random.Draws, Is.Zero, "the between-task plan's random sequence is unchanged");
            Assert.That(BotServiceSeams.BreakActivitiesDone(null), Is.True);
            Assert.DoesNotThrow(() => BotServiceSeams.AtTaskBreak(null, TaskBreakPoint.Begin));
            Assert.That(BotCommerceSeams.ReceiveItem(null, null, new DbInventoryItem()), Is.False);
            Assert.That(BotCommerceSeams.ReceiveMoney(null, null, 10), Is.False);
            Assert.That(EconomyLifecycle.Count, Is.Zero);
            Assert.DoesNotThrow(() => EconomyLifecycle.PulseAll(0));
            Assert.DoesNotThrow(() => EconomyLifecycle.FlushAll(true));
        }

        private sealed class Activity(char flag, bool wants, bool inventory) : IBotBreakActivity
        {
            public string Name => $"activity {Flag}";
            public char Flag => flag;
            public bool ImpliesInventoryStop => inventory;
            public string Describe => $"do {Flag}; ";
            public bool Wants(GameBot bot, Random random) => wants;
            public bool IsDone(GameBot bot) => true;
        }

        [Test]
        public void BreakActivities_TakeAFreeCapitalLetter_AndAddTheirFlags()
        {
            Assert.Throws<ArgumentException>(() => BotServiceSeams.Register(new Activity('T', true, false)));
            Assert.Throws<ArgumentException>(() => BotServiceSeams.Register(new Activity('I', true, false)));
            Assert.Throws<ArgumentException>(() => BotServiceSeams.Register(new Activity('D', true, false)));
            Assert.Throws<ArgumentException>(() => BotServiceSeams.Register(new Activity('c', true, false)));
            BotServiceSeams.Register(new Activity('C', true, true));
            Assert.Throws<ArgumentException>(() => BotServiceSeams.Register(new Activity('C', true, false)), "one letter, one activity");
            BotServiceSeams.Register(new Activity('Q', false, false));
            Assert.That(BotServiceSeams.RollBreakActivities(null, new Random(1), out bool inventoryStop, out string text), Is.EqualTo("C"));
            Assert.That(inventoryStop, Is.True, "DividedRealms: a craft trip ends at the inventory stop");
            Assert.That(text, Is.EqualTo("do C; "));
        }

        private sealed class Step(string name, int order, eWorldServiceKind kind, bool acts, List<string> ran) : IBotServiceStep
        {
            public string Name => name;
            public int Order => order;
            public eWorldServiceKind Kind => kind;
            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                ran.Add(name);
                result = new BotServiceResult(name, "detail", 1_000, eAutonomousProgressKind.Money);
                return acts;
            }
        }

        [Test]
        public void ServiceSteps_RunInOrderOfTheirKind_UntilOneActs()
        {
            var ran = new List<string>();
            BotServiceSeams.Register(new Step("late", 20, eWorldServiceKind.Vendor, true, ran));
            BotServiceSeams.Register(new Step("early", 10, eWorldServiceKind.Vendor, false, ran));
            BotServiceSeams.Register(new Step("exchange", 0, eWorldServiceKind.RealmExchange, true, ran));
            Assert.That(BotServiceSeams.TryRunSteps(eWorldServiceKind.Vendor, null, null, 0, out BotServiceResult result), Is.True);
            Assert.That(ran, Is.EqualTo(new[] { "early", "late" }));
            Assert.That(result.Status, Is.EqualTo("late"));
        }

        [Test]
        public void Settings_DefaultBotSideSwitchesOn_AndTheTaxToZero()
        {
            Dictionary<string, object> defaults = typeof(CraftingEconomyProperties)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>())
                .Where(attribute => attribute != null)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.DefaultValue);
            // Owner 2026-11-16: every Living Server feature on by default (WP0 and WP8 built them off); diagnostics and the
            // classic rules stay off.
            foreach (string key in new[] { "bot_crafters", "bot_craft_orders", "bot_upkeep", "bot_luxury_spending", "bot_siege_supply",
                         "bot_crafter_commerce", "bot_crafting_social" })
                Assert.That(defaults[key], Is.EqualTo(true), key);
            foreach (string key in new[] { "crafting_secondary_caps", "crafting_classic_quality", "crafting_classic_losses" })
                Assert.That(defaults[key], Is.EqualTo(false), key);
            Assert.That(defaults["exchange_tax_percent"], Is.EqualTo(0), "owner 2026-11-15: 0 until the ledger shows a need");
            Assert.That(defaults["bot_crafter_share_percent"], Is.EqualTo(7));
            Assert.That(defaults["crafting_skill_rate"], Is.EqualTo(1.0), "classic player pace");
            Assert.That(defaults["player_upkeep"], Is.EqualTo("light"));
            Assert.That(defaults["launcher_level50_kit"], Is.EqualTo(true), "today's launcher behaviour");
            Assert.That(defaults, Has.Count.EqualTo(22), "WP0's 20 and WP8's two switches");

            // The static fields start at their attribute defaults, so nothing differs before the database is read.
            foreach (FieldInfo field in typeof(CraftingEconomyProperties).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetCustomAttribute<ServerPropertyAttribute>() is { } attribute)
                    Assert.That(field.GetValue(null), Is.EqualTo(attribute.DefaultValue), attribute.Key);

            // No key is declared twice anywhere in the server.
            string[] keys = typeof(GameServer).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>()?.Key)
                .Where(key => key != null).ToArray();
            Assert.That(keys.GroupBy(key => key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key),
                Is.Empty);
        }

        [Test]
        public void Upkeep_LightIsHalfWithoutDurability_FreeIsNothing()
        {
            Assert.That(UpkeepPolicy.RepairCost(1000, UpkeepPolicy.Mode.Classic), Is.EqualTo(1000));
            Assert.That(UpkeepPolicy.RepairCost(1000, UpkeepPolicy.Mode.Light), Is.EqualTo(500));
            Assert.That(UpkeepPolicy.RepairCost(1, UpkeepPolicy.Mode.Light), Is.EqualTo(1));
            Assert.That(UpkeepPolicy.RepairCost(1000, UpkeepPolicy.Mode.Free), Is.Zero);
            Assert.That(UpkeepPolicy.RepairCost(-3, UpkeepPolicy.Mode.Classic), Is.Zero);
            Assert.That(UpkeepPolicy.ConsumesDurability(UpkeepPolicy.Mode.Classic), Is.True);
            Assert.That(UpkeepPolicy.ConsumesDurability(UpkeepPolicy.Mode.Light), Is.False);
            Assert.That(CraftingEconomyProperties.ParseUpkeep("CLASSIC"), Is.EqualTo(UpkeepPolicy.Mode.Classic));
            Assert.That(CraftingEconomyProperties.ParseUpkeep("free"), Is.EqualTo(UpkeepPolicy.Mode.Free));
            Assert.That(CraftingEconomyProperties.ParseUpkeep("nonsense"), Is.EqualTo(UpkeepPolicy.Mode.Light));
            Assert.That(CraftingEconomyProperties.SanitizeTaxPercent(-1), Is.Zero);
            Assert.That(CraftingEconomyProperties.SanitizeTaxPercent(5), Is.EqualTo(5));
            Assert.That(CraftingEconomyProperties.SanitizeTaxPercent(90), Is.EqualTo(50));
        }

        [Test]
        public void CraftingSkills_UseDividedRealmsFormat_PrimaryFirst()
        {
            string saved = BotCraftingSkills.Join(eCraftingSkill.ArmorCrafting, [eCraftingSkill.MetalWorking, eCraftingSkill.ArmorCrafting]);
            Assert.That(saved, Is.EqualTo($"{(int)eCraftingSkill.ArmorCrafting}|1;{(int)eCraftingSkill.MetalWorking}|1"));
            Assert.That(BotCraftingSkills.Primary(saved), Is.EqualTo(eCraftingSkill.ArmorCrafting));
            saved = BotCraftingSkills.Gain(saved, eCraftingSkill.ArmorCrafting, true, 3);
            Assert.That(BotCraftingSkills.Value(saved, eCraftingSkill.ArmorCrafting), Is.EqualTo(4));
            string capped = BotCraftingSkills.Serialize([new(eCraftingSkill.ArmorCrafting, 5), new(eCraftingSkill.MetalWorking, 1299)]);
            capped = BotCraftingSkills.Gain(capped, eCraftingSkill.MetalWorking, false, 5);
            Assert.That(BotCraftingSkills.Value(capped, eCraftingSkill.MetalWorking), Is.EqualTo(1300), "a secondary stops at 1300");
            Assert.That(BotCraftingSkills.Gain(capped, eCraftingSkill.MetalWorking, false), Is.EqualTo(capped));
            Assert.That(BotCraftingSkills.Gain(capped, eCraftingSkill.Alchemy, true), Is.EqualTo(capped), "an unknown skill changes nothing");
            Assert.That(BotCraftingSkills.Parse("garbage;0|5;9999|1;2|x"), Is.Empty);
            Assert.That(BotCraftingSkills.Parse(string.Empty), Is.Empty, "the column nothing writes yet");
        }

        [Test]
        public void LogLines_FollowTheAutonomousFormat()
        {
            string line = EconomyLog.Line(EconomyLog.EconSale, ("price", 400L), ("tax", 0), ("seller", "Bran \"the\"\nSmith"),
                ("crafted", true), ("rate", 1.5), ("band", new MarketBand(eRealm.Midgard, 34, 25, 4, 1).Key));
            Assert.That(line, Is.EqualTo("ECON_SALE price=400 tax=0 seller=\"Bran 'the' Smith\" crafted=1 rate=1.5 band=\"2|34|25|4|1\""));
            Assert.That(EconomyLog.AllTags, Is.Unique);
            Assert.That(EconomyLog.AllTags.All(tag => tag.StartsWith("CRAFT_") || tag.StartsWith("ECON_")), Is.True);
            Assert.That(EconomyFlows.FileName(@"C:\src\bots\AutonomousBotEconomy.cs"), Is.EqualTo("AutonomousBotEconomy.cs"));
            Assert.That(EconomyFlows.FileName("/home/x/GameBot.cs"), Is.EqualTo("GameBot.cs"));
        }

        [Test]
        public void Holders_StartEmpty()
        {
            Assert.That(DOL.GS.Crafting.RecipeCatalogs.Current.IsLoaded, Is.False);
            Assert.That(DOL.GS.Crafting.RecipeCatalogs.Current.ForSkill(eCraftingSkill.ArmorCrafting), Is.Empty);
            Assert.That(DOL.GS.Crafting.RecipeCatalogs.World.IsLoaded, Is.False);
            Assert.That(BotCrafters.Roster.IsCrafter(1), Is.False);
            Assert.That(CraftOrders.Board.Post(null), Is.False);
            Assert.That(CraftOrders.Board.IsHeldForOrder(new DbInventoryItem()), Is.False);
            Assert.That(CraftOrders.StateText(CraftOrderState.Made), Is.EqualTo(OfflineBotCraftOrderRecord.MadeState));
            Assert.That(CraftOrders.StateText(CraftOrderState.Open), Is.EqualTo(OfflineBotCraftOrderRecord.OpenState));
            Assert.That(CraftOrders.ParseState("made"), Is.EqualTo(CraftOrderState.Made));
            Assert.That(MarketBand.TierOf(98), Is.Zero);
            Assert.That(MarketBand.TierOf(99), Is.EqualTo(1));
            Assert.That(MarketBand.TierOf(100), Is.EqualTo(2));
            Assert.That(MarketBand.BandOf(24), Is.EqualTo(4));
        }

        // ---- tables ----

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source", "tools", "DAoCLivingServer.ProgressImport", "progress-policy.json")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "the repository root above the test directory");
            return dir;
        }

        [Test]
        public void EconomyTables_AreRegisteredInTheProgressPolicyAndThePassport()
        {
            string root = RepoRoot();
            using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "source", "tools", "DAoCLivingServer.ProgressImport", "progress-policy.json")));
            string[] optional = policy.RootElement.GetProperty("OptionalProgressTables").EnumerateArray().Select(entry => entry.GetString()).ToArray();
            string passport = File.ReadAllText(Path.Combine(root, "source", "tools", "DAoCLivingServer.Passport", "PassportEngine.cs"));

            Assert.That(EconomyTables.All, Has.Length.EqualTo(EconomyTables.RecordTypes.Length));
            for (int i = 0; i < EconomyTables.All.Length; i++)
            {
                string table = EconomyTables.All[i];
                Assert.That(EconomyTables.RecordTypes[i].GetCustomAttribute<DataTable>()?.TableName, Is.EqualTo(table), "record type order");
                Assert.That(optional, Does.Contain(table), $"{table} in progress-policy.json (build_release_037.py empties it)");
                Assert.That(passport, Does.Contain($"[\"{table}\"] = new(PassportDecision.StaysInWorld"), $"{table} in PassportTables.Decisions");
            }
            // Every saved-progress row of the new tables is a DataObject the server registers by reflection.
            foreach (Type type in EconomyTables.RecordTypes)
                Assert.That(typeof(DataObject).IsAssignableFrom(type) && typeof(ILivingServerRow).IsAssignableFrom(type), Is.True, type.Name);
            // No new column on offline_world_bots (the launcher's explicit INSERT).
            Assert.That(typeof(OfflineWorldBotRecord).GetProperties().Count(property => property.GetCustomAttribute<DataElement>() != null),
                Is.EqualTo(CountWorldBotColumnsAtBase()), "offline_world_bots columns unchanged");
        }

        /// <summary>offline_world_bots' DataElement count at integration/2.0 c58ea66 (before Crafting 2.0).</summary>
        private static int CountWorldBotColumnsAtBase() => 56;

        [Test]
        public void CraftOrderRecord_KeepsDividedRealmsColumnsFirst()
        {
            string[] columns = typeof(OfflineBotCraftOrderRecord).GetProperties()
                .Where(property => property.GetCustomAttribute<DataElement>() != null).Select(property => property.Name).ToArray();
            string[] dividedRealms = ["OrderId", "BuyerBotId", "CrafterBotId", "Realm", "CraftingSkill", "ProductId", "ProductName",
                "RecipeLevel", "Copper", "State", "ItemObjectId", "CreatedUtc", "ExpiresUtc"];
            Assert.That(columns.Take(dividedRealms.Length), Is.EqualTo(dividedRealms));
            Assert.That(new OfflineBotCraftOrderRecord().State, Is.EqualTo("open"));
        }
    }
}
