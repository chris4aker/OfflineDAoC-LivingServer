using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using static DOL.UnitTests.GearTestBots;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP6: the gear check at task breaks, the Exchange route, want-to-buy orders on WP0's board contract (a
    /// stand-in board here: WP5 builds the real one), and ItemValue165 as the price index. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotGearPipeline
    {
        private GameServer _previous;
        private bool _crafters, _orders;
        private MarketSearchEngine _previousMarket;
        private readonly List<GameBot> _registered = new();
        private readonly List<DbInventoryItem> _listed = new();

        [SetUp]
        public void SetUp()
        {
            _previous = UseServer();
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _orders = CraftingEconomyProperties.BOT_CRAFT_ORDERS;
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = true;
            FieldInfo market = typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic)!;
            _previousMarket = (MarketSearchEngine)market.GetValue(null);
            market.SetValue(null, new MarketSearchEngine());
            BotGearPipeline.ResetForTests();
            CraftOrders.Board = null;
            RecipeCatalogs.Current = null;
            PriceIndexes.Current = null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (DbInventoryItem item in _listed)
                MarketCache.RemoveItem(item);
            foreach (GameBot bot in _registered)
                AutonomousBotRegistry.Unregister(bot);
            _listed.Clear();
            _registered.Clear();
            typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, _previousMarket);
            CraftOrders.Board = null;
            RecipeCatalogs.Current = null;
            PriceIndexes.Current = null;
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            DOL.GS.Commands.CraftEconomyTestHooks.ResetForTests();
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = _orders;
            GameServer.LoadTestDouble(_previous);
        }

        private GearBot Online(int level, ICharacterClass cls = null, long money = 0)
        {
            GearBot bot = Bot(level, cls ?? new ClassEnchanter(), money: money);
            bot.ObjectState = GameObject.eObjectState.Active;
            AutonomousBotRegistry.Register(bot);
            _registered.Add(bot);
            return bot;
        }

        private void List(DbInventoryItem item, string ownerId, int price)
        {
            item.OwnerID = ownerId;
            item.OwnerLot = RealmExchangeBroker.HiberniaOwnerLot;
            item.SlotPosition = (int)eInventorySlot.Consignment_First + _listed.Count;
            item.SellPrice = price;
            Assert.That(MarketCache.AddItem(item), Is.True);
            _listed.Add(item);
        }

        // ---- the gear check (pure) ----

        [Test]
        public void TheGapsAreEmptySlotsThenTheTemplateThenPiecesAFullBandBehind()
        {
            var worn = new DbInventoryItem[BotGearSet.LoadoutLength];
            worn[(int)eInventorySlot.TorsoArmor] = Piece(eInventorySlot.TorsoArmor, 44, 96);          // a band behind 50, not crafted
            worn[(int)eInventorySlot.LegsArmor] = Piece(eInventorySlot.LegsArmor, 45, 99, true);      // template piece, a band behind
            worn[(int)eInventorySlot.ArmsArmor] = Piece(eInventorySlot.ArmsArmor, 50, 100, true);     // fine
            worn[(int)eInventorySlot.HeadArmor] = Piece(eInventorySlot.HeadArmor, 50, 98, true);      // crafted but 98%
            BotGearPipeline.Gap[] gaps = BotGearPipeline.FindGaps(50, worn);
            Assert.That(gaps.Select(gap => (gap.Slot, gap.Reason)), Is.EqualTo(new[]
            {
                (eInventorySlot.HandsArmor, BotGearPipeline.GapReason.Empty),
                (eInventorySlot.FeetArmor, BotGearPipeline.GapReason.Empty),
                (eInventorySlot.TorsoArmor, BotGearPipeline.GapReason.Template),
                (eInventorySlot.HeadArmor, BotGearPipeline.GapReason.Template),
                (eInventorySlot.LegsArmor, BotGearPipeline.GapReason.BelowBand),
            }));

            Assert.That(BotGearPipeline.FindGaps(30, worn).Select(gap => gap.Reason), Has.No.Member(BotGearPipeline.GapReason.Template),
                "the template is a level-50 gap only");
            Assert.That(BotGearPipeline.IsBelowBand(30, 25), Is.True);
            Assert.That(BotGearPipeline.IsBelowBand(30, 26), Is.False);
            Assert.That(BotGearPipeline.MinQualityFor(new(eInventorySlot.TorsoArmor, BotGearPipeline.GapReason.Template)), Is.EqualTo(99));
            Assert.That(BotGearPipeline.MinQualityFor(new(eInventorySlot.TorsoArmor, BotGearPipeline.GapReason.Empty)), Is.Zero);
        }

        [Test]
        public void AnOrderCostsDividedRealmsQuoteAndAtMostHalfThePurse()
        {
            Assert.That(BotGearPipeline.Quote(0), Is.EqualTo(100));
            Assert.That(BotGearPipeline.Quote(400), Is.EqualTo(500));
            Assert.That(BotGearPipeline.Quote(1_000), Is.EqualTo(1_250));
            Assert.That(BotGearPipeline.CanAffordOrder(500, 1_000), Is.True);
            Assert.That(BotGearPipeline.CanAffordOrder(501, 1_000), Is.False);
        }

        [Test]
        public void ATemplateGapTakesTheTemplatePieceFirstThenCraftedThenTheBiggerRise()
        {
            var gap = new BotGearPipeline.Gap(eInventorySlot.TorsoArmor, BotGearPipeline.GapReason.Template);
            DbInventoryItem drop = Piece(eInventorySlot.TorsoArmor, 50, 100);
            DbInventoryItem crafted96 = Piece(eInventorySlot.TorsoArmor, 50, 96, true);
            DbInventoryItem crafted99 = Piece(eInventorySlot.TorsoArmor, 50, 99, true);
            var fills = new[]
            {
                new BotGearPipeline.Fill(drop, eInventorySlot.TorsoArmor, 900, gap),
                new BotGearPipeline.Fill(crafted96, eInventorySlot.TorsoArmor, 800, gap),
                new BotGearPipeline.Fill(crafted99, eInventorySlot.TorsoArmor, 100, gap),
            };
            Assert.That(BotGearPipeline.Best(fills).Item, Is.SameAs(crafted99));
            Assert.That(BotGearPipeline.Best(fills.Take(2)).Item, Is.SameAs(crafted96));
        }

        // ---- at a task break ----

        [Test]
        public void WithBotCraftersOff_NothingIsCheckedOrPosted()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            var board = new StandInBoard();
            CraftOrders.Board = board;
            GearBot bot = Online(20, money: 100_000);
            BotGearPipeline.TaskBreakHook.Instance.AtTaskBreak(bot, TaskBreakPoint.Begin);
            Assert.That(BotGearPipeline.Peek().Checks, Is.Zero);
            Assert.That(BotGearPipeline.GearNeed.Instance.WantsVisit(bot), Is.False);
            Assert.That(BotGearPipeline.GearNeed.Instance.NeededService(bot), Is.Null);
            Assert.That(BotGearPipeline.ExchangeStep.Instance.TryRun(bot, null, 0, out _), Is.False);
            Assert.That(board.Posted, Is.Empty);
        }

        [Test]
        public void AGapTheExchangeCanFillAsksForTheTownStopAndTheExchange()
        {
            GearBot buyer = Online(20, money: 10_000);
            DbInventoryItem torso = Piece(eInventorySlot.TorsoArmor, 20, 98, true);
            List(torso, AutonomousBotEconomy.GetOwnerId(buyer.DatabaseID + 1), 2_000);
            var board = new StandInBoard();
            CraftOrders.Board = board;

            BotGearPipeline.Check(buyer);
            Assert.That(BotGearPipeline.GapsOf(buyer), Has.Length.EqualTo(6));
            BotGearPipeline.Fill fill = BotGearPipeline.FindGapFill(buyer, BotGearPipeline.GapsOf(buyer));
            Assert.That(fill?.Item, Is.SameAs(torso));
            Assert.That(BotGearPipeline.GearNeed.Instance.WantsVisit(buyer), Is.True);
            Assert.That(BotGearPipeline.GearNeed.Instance.NeededService(buyer), Is.EqualTo(eWorldServiceKind.RealmExchange));
            Assert.That(board.Posted, Is.Empty, "a piece on the Exchange: no order");
            Assert.That(BotGearPipeline.Peek().Routed, Is.EqualTo(1));

            BotGearPipeline.TaskBreakHook.Instance.AtTaskBreak(buyer, TaskBreakPoint.End);
            Assert.That(BotGearPipeline.GearNeed.Instance.WantsVisit(buyer), Is.False, "the next break checks again");
        }

        [Test]
        public void APieceOverEightyPercentOfThePurseIsNotAGapFill()
        {
            GearBot buyer = Online(20, money: 1_000);
            List(Piece(eInventorySlot.TorsoArmor, 20, 98, true), AutonomousBotEconomy.GetOwnerId(buyer.DatabaseID + 1), 900);
            BotGearPipeline.Check(buyer);
            Assert.That(BotGearPipeline.GearNeed.Instance.WantsVisit(buyer), Is.False);
        }

        [Test]
        public void WithNothingOnTheExchange_AWantToBuyOrderIsPostedOnce()
        {
            GearBot buyer = Online(30, money: 10_000);
            var board = new StandInBoard();
            CraftOrders.Board = board;
            RecipeCatalogs.Current = new StandInCatalog(BotGearPipeline.ArmorTypeFor(buyer));

            BotGearPipeline.Check(buyer);
            CraftOrder order = board.Posted.Single();
            Assert.That(order.Source, Is.EqualTo(CraftOrderSource.Bot));
            Assert.That(order.BuyerBotId, Is.EqualTo(buyer.DatabaseID));
            Assert.That(order.BuyerId, Is.EqualTo(AutonomousBotEconomy.GetOwnerId(buyer.DatabaseID)));
            Assert.That(order.Realm, Is.EqualTo(eRealm.Hibernia));
            Assert.That(order.ItemType(), Is.EqualTo((int)eInventorySlot.TorsoArmor), "the body piece first");
            Assert.That(order.EscrowCopper, Is.EqualTo(500), "materials 400 + the fee");
            Assert.That(order.PayUpFront, Is.True, "the board takes the escrow (DividedRealms' Place)");
            Assert.That(order.MinQuality, Is.Zero);
            Assert.That(order.State, Is.EqualTo(CraftOrderState.Open));
            Assert.That(buyer.PersistentRecord.MoneyCopper, Is.EqualTo(10_000), "WP6 moves no coin for an order");
            Assert.That(BotGearPipeline.GearNeed.Instance.WantsVisit(buyer), Is.False);

            BotGearPipeline.Check(buyer);
            Assert.That(board.Posted, Has.Count.EqualTo(1), "one open order a buyer");
            Assert.That(BotGearPipeline.Peek().WtbPosted, Is.EqualTo(1));
            Assert.That(BotGearPipeline.GearCheckLine(BotGearPipeline.Peek()),
                Does.StartWith("ECON_GEAR_CHECK event=\"hour\" checks=2 gaps=2 empty=12 template=0 below=0 routed=0 bought=0"));
        }

        [Test]
        public void AtFifty_TheTemplateOrderAsksFor99Percent()
        {
            GearBot buyer = Online(50, money: 100_000);
            foreach (eInventorySlot slot in BotGearSet.TemplateSlots)
                Wear(buyer, slot, Piece(slot, 50, 96));
            var board = new StandInBoard();
            CraftOrders.Board = board;
            RecipeCatalogs.Current = new StandInCatalog(BotGearPipeline.ArmorTypeFor(buyer));
            BotGearPipeline.Check(buyer);
            Assert.That(board.Posted.Single().MinQuality, Is.EqualTo(99));
            Assert.That(BotGearPipeline.Peek().Template, Is.EqualTo(6));
        }

        [Test]
        public void WithoutABoard_ItDegradesToCountingAndWithoutTheOrderSwitchItPostsNothing()
        {
            GearBot buyer = Online(30, money: 10_000);
            RecipeCatalogs.Current = new StandInCatalog(BotGearPipeline.ArmorTypeFor(buyer));
            BotGearPipeline.Check(buyer);
            Assert.That(BotGearPipeline.Peek().WtbNoBoard, Is.EqualTo(1));

            var board = new StandInBoard();
            CraftOrders.Board = board;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = false;
            BotGearPipeline.Check(buyer);
            Assert.That(board.Posted, Is.Empty);
        }

        [Test]
        public void APoorBotPostsNoOrder()
        {
            GearBot buyer = Online(30, money: 900);
            var board = new StandInBoard();
            CraftOrders.Board = board;
            RecipeCatalogs.Current = new StandInCatalog(BotGearPipeline.ArmorTypeFor(buyer));
            BotGearPipeline.Check(buyer);
            Assert.That(board.Posted, Is.Empty);
            Assert.That(BotGearPipeline.Peek().WtbUnaffordable, Is.EqualTo(1));
        }

        [Test]
        public void ARefusedPostIsCountedAndRetriedLater()
        {
            GearBot buyer = Online(30, money: 10_000);
            var board = new StandInBoard { Accept = false };
            CraftOrders.Board = board;
            RecipeCatalogs.Current = new StandInCatalog(BotGearPipeline.ArmorTypeFor(buyer));
            BotGearPipeline.Check(buyer);
            BotGearPipeline.Check(buyer);
            Assert.That(BotGearPipeline.Peek().WtbRefused, Is.EqualTo(1), "the second break waits out the retry");
        }

        // ---- registration, lines and the index ----

        [Test]
        public void RegistrationAddsAHookANeedAStepAndAStore()
        {
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
            BotGearPipeline.RegisterForTests();
            Assert.That(BotServiceSeams.RegisteredCount, Is.EqualTo(3));
            Assert.That(EconomyLifecycle.Count, Is.EqualTo(1));
            Assert.That(PriceIndexes.Current, Is.SameAs(ItemValuePriceIndex.Instance));
            Assert.That(DOL.GS.Commands.CraftEconomyTestHooks.TryGet("wtb", out DOL.GS.Commands.CraftEconomyTestHooks.Handler wtb), Is.True);
            Assert.That(wtb(null, ["wtb"]).First(), Does.StartWith("Gear pipeline (WP6)"));
        }

        [Test]
        public void TheIndexIsRegisteredOnlyOverWP0sFallback()
        {
            var other = new OtherIndex();
            PriceIndexes.Current = other;
            ItemValuePriceIndex.RegisterIfUnset();
            Assert.That(PriceIndexes.Current, Is.SameAs(other));
            PriceIndexes.Current = null;
            Assert.That(ItemValuePriceIndex.IsFallback(PriceIndexes.Current), Is.True);
            ItemValuePriceIndex.RegisterIfUnset();
            Assert.That(PriceIndexes.Current, Is.SameAs(ItemValuePriceIndex.Instance));
        }

        [Test]
        public void TheIndexIsTrue165sValueWhenOn_AndTodaysNumbersWhenOff()
        {
            ItemValue165.SetMerchantStockForTests([]);
            try
            {
                DbInventoryItem torso = Piece(eInventorySlot.TorsoArmor, 50, 99, true, (eProperty.Strength, 10));
                IPriceIndex index = ItemValuePriceIndex.Instance;
                Assert.That(index.Fair(torso), Is.EqualTo(ItemValue165.Fair(torso)));
                Assert.That(index.Floor(torso), Is.EqualTo(ItemValue165.Floor(torso)));
                Assert.That(index.Ask(torso, 0.1), Is.EqualTo(ItemValue165.Ask(torso, 0.1)));
                Assert.That(index.Bid(torso, 1.0), Is.EqualTo((long)Math.Round(ItemValue165.Fair(torso) * (1 + ItemValue165.MoodHigh))),
                    "the mood is kept to +15%");
                Assert.That(index.Bid(torso, -1.0), Is.GreaterThanOrEqualTo(index.Floor(torso)));
                // 1.65 curve at 50, torso, 99%: 200 gold x 1.6 quality, before utility, at full condition
                Assert.That(ItemValue165.Fair(torso), Is.GreaterThan(200L * 10_000));

                CraftingEconomyProperties.BOT_CRAFTERS = false;
                Assert.That(index.Fair(torso), Is.EqualTo(AutonomousBotEconomy.RecommendListingPrice(torso)));
                Assert.That(index.Floor(torso), Is.EqualTo(AutonomousBotEconomy.CalculateStandardVendorSaleCopper(torso)));
                Assert.That(index.Bid(torso, 0), Is.EqualTo(index.Floor(torso)));
            }
            finally
            {
                ItemValue165.SetMerchantStockForTests(null);
            }
        }

        [Test]
        public void TheLscraftWtbLinesSayWhatIsBuilt()
        {
            string[] lines = BotGearPipeline.WtbLines();
            Assert.That(lines[0], Does.Contain("bot_crafters on").And.Contain("order board not registered (WP5)"));
            Assert.That(lines[3], Does.Contain("must be 0"));
        }

        // ---- stand-ins for WP5's board and WP1's catalog ----

        private sealed class StandInBoard : ICraftOrderBoard
        {
            public readonly List<CraftOrder> Posted = new();
            public bool Accept = true;
            public bool Post(CraftOrder order)
            {
                if (!Accept)
                    return false;
                Posted.Add(order);
                return true;
            }
            public IReadOnlyList<CraftOrder> Open(eRealm realm, eCraftingSkill skill, int maxRecipeLevel) => Posted;
            public IReadOnlyList<CraftOrder> ForCrafter(long crafterBotId) => Array.Empty<CraftOrder>();
            public IReadOnlyList<CraftOrder> ForBuyer(string buyerId) => Posted.Where(order => order.BuyerId == buyerId).ToList();
            public bool IsHeldForOrder(DbInventoryItem item) => false;
            public bool TryMarkMade(string orderId, DbInventoryItem item) => false;
            public bool TryDeliver(string orderId) => false;
            public int ExpireDue(DateTime utcNow) => 0;
        }

        private sealed class StandInCatalog : IRecipeCatalog
        {
            private readonly eObjectType _armor;
            public StandInCatalog(eObjectType armor) => _armor = armor;
            public bool IsLoaded => true;
            public IReadOnlyList<CraftRecipeInfo> ForSkill(eCraftingSkill skill) => Array.Empty<CraftRecipeInfo>();
            public IEnumerable<CraftRecipeInfo> Find(eCraftingSkill skill, int minLevel, int maxLevel, eRealm realm) => Array.Empty<CraftRecipeInfo>();
            public CraftRecipeInfo Get(string recipeId) => null;

            public CraftRecipeInfo BestFor(int objectType, int itemType, int itemLevel, eRealm realm)
            {
                if (objectType != (int)_armor)
                    return null;
                var product = new DbItemTemplate
                {
                    Id_nb = $"stand_in_{itemType}_{itemLevel}", Name = $"stand-in {(eInventorySlot)itemType}", Realm = (int)realm,
                    Level = itemLevel, Item_Type = itemType, Object_Type = objectType, Quality = 100, MaxCount = 1, PackSize = 1,
                    MaxCondition = 10000, MaxDurability = 10000, DPS_AF = itemLevel, Price = 100, IsDropable = true, IsTradable = true,
                };
                var recipe = new Recipe(product, new List<Ingredient>(), eCraftingSkill.Tailoring, itemLevel * 20, false);
                return new CraftRecipeInfo(product.Id_nb, eCraftingSkill.Tailoring, itemLevel * 20, product.Id_nb, product.Name,
                    objectType, itemType, itemLevel, (int)realm, true, false, [new CraftMaterial("cloth", "cloth", 0, 4, 100, 1)])
                { Recipe = recipe };
            }
        }

        private sealed class OtherIndex : IPriceIndex
        {
            public long Floor(DbInventoryItem item) => 1;
            public long Fair(DbInventoryItem item, long materialCostCopper = 0) => 1;
            public long Ask(DbInventoryItem item, double disposition, long materialCostCopper = 0) => 1;
            public long Bid(DbInventoryItem item, double disposition) => 1;
            public void ObserveListing(MarketBand band, long priceCopper) { }
            public void ObserveSale(MarketBand band, long priceCopper) { }
            public void ObserveDemand(MarketBand band) { }
        }
    }

    internal static class CraftOrderTestExtensions
    {
        /// <summary>The slot of the stand-in product the order names ("stand_in_{itemType}_{level}").</summary>
        public static int ItemType(this CraftOrder order) => int.Parse(order.ProductId.Split('_')[2]);
    }
}
