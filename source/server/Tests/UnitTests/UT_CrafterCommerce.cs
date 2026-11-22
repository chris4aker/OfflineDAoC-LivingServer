using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP8: a real player's orders on WP5's craft-order board, the escrow, the hand-over at the crafter's break,
    /// the trade window, refunds (online now, offline at the next login), the customer's memory, and the economy ledger:
    /// over every path the realm's flows equal its change in money supply (residual 0), and the bots' own residual is 0.
    /// Only autonomous gamebots trade; a /bot companion never does. Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CrafterCommerce
    {
        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public string TestName;
            public eRealm TestRealm;
            public override string Name { get => TestName; set { } }
            public override eRealm Realm { get => TestRealm; set { } }
            public override IPacketLib Out => null;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class TestServer : GameServer
        {
            protected override DOL.GS.ServerRules.IServerRules ServerRulesImpl => new DOL.GS.ServerRules.NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Database;
            private static readonly IObjectDatabase Database = DispatchProxy.Create<IObjectDatabase, UT_AutonomousBotCraftingOrders.TemplateReads>();
        }

        private GameServer _previousServer;
        private readonly Dictionary<long, long> _purses = new();
        private readonly Dictionary<string, long> _wallets = new();
        private readonly List<string> _tells = new();
        private readonly List<DbInventoryItem> _received = new();
        private EconomyFlowLedger _ledger;
        private UT_AutonomousBotCraftingOrders.FakeCatalog _catalog;
        private bool _crafters, _orders, _commerce, _social;
        private GamePlayer _online;
        private static long _nextId = 9_810_000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            _crafters = CraftingEconomyProperties.BOT_CRAFTERS;
            _orders = CraftingEconomyProperties.BOT_CRAFT_ORDERS;
            _commerce = CraftingEconomyProperties.BOT_CRAFTER_COMMERCE;
            _social = CraftingEconomyProperties.BOT_CRAFTING_SOCIAL;
            CraftingEconomyProperties.BOT_CRAFTERS = true;
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = true;
            _purses.Clear();
            _wallets.Clear();
            _tells.Clear();
            _received.Clear();
            _catalog = new UT_AutonomousBotCraftingOrders.FakeCatalog();
            _catalog.Recipes.AddRange(UT_CrafterQuotes.Book());
            RecipeCatalogs.Current = _catalog;
            RecipeCatalogs.World = _catalog;
            AutonomousBotCrafting.ResetForTests();
            AutonomousBotCraftOrders.ResetForTests();
            CrafterCustomers.ResetForTests();
            CrafterCommerce.ResetForTests();
            CrafterCommerce.RegisterSeams();
            _ledger = new EconomyFlowLedger { BotInfo = _ => ((int)eRealm.Albion, 30) };
            EconomyFlows.Sink = _ledger;
            AutonomousBotCraftOrders.OnSale = (_, _) => true;
            AutonomousBotCraftOrders.SpendCoin = (bot, copper, kind) =>
            {
                if (_purses.GetValueOrDefault(bot) < copper)
                    return false;
                _purses[bot] -= copper;
                EconomyFlows.Record(bot, -copper, kind, "Test.cs", "Spend");
                return true;
            };
            AutonomousBotCraftOrders.PayCoin = (bot, copper, kind) =>
            {
                _purses[bot] = _purses.GetValueOrDefault(bot) + copper;
                EconomyFlows.Record(bot, copper, kind, "Test.cs", "Pay");
                return true;
            };
            CrafterCommerce.CustomerIdOf = player => "player:" + player.Name;
            CrafterCommerce.FindPlayer = id => _online != null && id == _online.Name ? _online : null;
            CrafterCommerce.OnlinePlayers = () => _online == null ? [] : [_online];
            CrafterCommerce.TakeCoin = (player, copper) =>
            {
                if (_wallets.GetValueOrDefault(player.Name) < copper)
                    return false;
                _wallets[player.Name] -= copper;
                return true;
            };
            CrafterCommerce.GiveCoin = (player, copper) => _wallets[player.Name] = _wallets.GetValueOrDefault(player.Name) + copper;
            CrafterCommerce.SendTell = (bot, player, text) => _tells.Add($"{bot.Name} -> {player.Name}: {text}");
            CrafterCommerce.Schedule = (_, _, action) => action();
            CrafterCommerce.AtBreak = _ => true;
            CrafterCommerce.IsNear = (_, _) => true;
            CrafterCommerce.IsGuildmate = (_, _) => false;
            // The trade itself: the player's coin (when there is a price), the pieces from the bot's bag, the bot's purse.
            BotTradeWindow.Execute = window =>
            {
                if (window.Price > 0)
                {
                    if (_wallets.GetValueOrDefault(window.Owner.Name) < window.Price)
                        return false;
                    _wallets[window.Owner.Name] -= window.Price;
                }
                foreach (DbInventoryItem piece in window.Pieces)
                {
                    window.Bot.Inventory.RemoveItemWithoutDbDeletion(piece);
                    _received.Add(piece);
                }
                if (window.Price > 0)
                {
                    _purses[window.Bot.DatabaseID] = _purses.GetValueOrDefault(window.Bot.DatabaseID) + window.Price;
                    EconomyFlows.Record(window.Bot.DatabaseID, window.Price, EconomyFlowKind.PlayerTrade, "BotTradeWindow.cs", "DefaultExecute");
                }
                return true;
            };
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = _orders;
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = _commerce;
            CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = _social;
            EconomyFlows.Sink = null;
            CrafterCommerce.ResetForTests();
            CrafterCustomers.ResetForTests();
            AutonomousBotCraftOrders.ResetForTests();
            AutonomousBotCrafting.ResetForTests();
            BotCommerceSeams.ResetForTests();
            RecipeCatalogs.Current = null;
            RecipeCatalogs.World = null;
            _online = null;
            GameServer.LoadTestDouble(_previousServer);
        }

        #region Helpers

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private GameBot Crafter(string name = "Aldwyn", int skill = 310, bool companion = false)
        {
            long id = UT_AutonomousBotCraftingOrders.CrafterId(Interlocked.Increment(ref _nextId));
            _nextId = id;
            var bot = (UT_AutonomousBotCraftingOrders.TestBot)RuntimeHelpers.GetUninitializedObject(typeof(UT_AutonomousBotCraftingOrders.TestBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = 30;
            bot.Realm = eRealm.Albion;
            bot.Name = name;
            bot.DatabaseID = id;
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(id);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, !companion);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper)).SetValue(bot, companion);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = id, SerializedCraftingSkills = $"{UT_CrafterQuotes.Ac}|{skill}" });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>());
            return bot;
        }

        private GamePlayer PlayerNamed(string name, long wallet, eRealm realm = eRealm.Albion)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.TestName = name;
            player.TestRealm = realm;
            _wallets[name] = wallet;
            return player;
        }

        private static CrafterTalk.CustomerFacts Customer(GamePlayer player, CrafterCustomers.Standing standing = CrafterCustomers.Standing.Stranger) =>
            new("player:" + player.Name, player.Name, standing, 0, false, true);

        private static CrafterTalk.Deal Quote(string request, CrafterTalk.CustomerFacts customer)
        {
            CrafterTalk.Reply reply = CrafterTalk.Answer(UT_CrafterTalk.Crafter(), customer, request, null, [], DateTime.UtcNow);
            Assert.That(reply.Act, Is.EqualTo(CrafterTalk.Act.Quote), reply.Text);
            return reply.Deal;
        }

        /// <summary>The crafter's trip made the order's piece: a real item in its bag, marked made on the board.</summary>
        private static DbInventoryItem Make(GameBot crafter, CraftOrder order)
        {
            var item = new DbInventoryItem
            {
                Id_nb = order.ProductId, Name = order.ProductName, Quality = 97, IsCrafted = true, Creator = crafter.Name, Level = 20,
                Object_Type = (int)eObjectType.Chain, Item_Type = (int)eInventorySlot.TorsoArmor, MaxCondition = 100, Condition = 100,
                MaxDurability = 100, Durability = 100,
            };
            item.ObjectId = Guid.NewGuid().ToString("N");
            eInventorySlot slot = crafter.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            Assert.That(crafter.Inventory.AddItem(slot, item), Is.True);
            Assert.That(AutonomousBotCraftOrders.OnMade(crafter, order.OrderId, item), Is.True);
            return item;
        }

        /// <summary>supply now - supply before - (flows + the players' escrow), as WP2's snapshot computes it for the realm.</summary>
        private long Residual(long supplyBefore)
        {
            EconomyFlowLedger.Window window = _ledger.TakeWindow();
            int realm = (int)eRealm.Albion;
            long flows = window.ExternalNet(realm) + window.PlayerEscrow(realm);
            long supply = _purses.Values.Sum() + _wallets.Values.Sum();
            return supply - supplyBefore - flows;
        }

        private long Supply() => _purses.Values.Sum() + _wallets.Values.Sum();

        #endregion

        [Test]
        public void AnUpFrontOrderIsHeldInEscrowHandedOverAtTheBreakAndPaysTheCrafter()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 100_000);
            _online = player;
            long before = Supply();
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player));
            IReadOnlyList<CraftOrder> placed = CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true);
            Assert.That(placed, Has.Count.EqualTo(1));
            CraftOrder order = placed[0];
            Assert.That(order.Source, Is.EqualTo(CraftOrderSource.Player));
            Assert.That(order.BuyerBotId, Is.EqualTo(0), "a player is no gamebot buyer");
            Assert.That(order.EscrowCopper, Is.EqualTo(deal.Ask));
            Assert.That(_wallets["Tamsin"], Is.EqualTo(100_000 - deal.Ask), "the price left the purse into escrow");
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(crafter), Is.True, "the crafter makes it on its next trip");
            Assert.That(Residual(before), Is.EqualTo(0), "escrow: the supply fell by the price and the ledger saw it");

            before = Supply();
            DbInventoryItem piece = Make(crafter, order);
            // The board's delivery at the crafter's task break (WP5's AtTaskBreak calls Deliver).
            Assert.That(AutonomousBotCraftOrders.Deliver(crafter), Is.EqualTo(0), "the board leaves a player's piece to the hand-over");
            Assert.That(_received, Is.EquivalentTo(new[] { piece }), "handed over in person at the break");
            Assert.That(AutonomousBotCraftOrders.OfBuyer("player:Tamsin"), Is.Empty, "the order is closed");
            Assert.That(_purses[crafter.DatabaseID], Is.EqualTo(deal.Ask), "the escrow paid the crafter");
            Assert.That(Residual(before), Is.EqualTo(0));
            OfflineCrafterCustomerRecord memory = CrafterCustomers.Get(crafter.DatabaseID, "player:Tamsin");
            Assert.That(memory.Deals, Is.EqualTo(1));
            Assert.That(memory.SpentCopper, Is.EqualTo(deal.Ask));
            Assert.That(_tells.Last(), Does.Contain("here you go"));
        }

        [Test]
        public void APayOnDeliveryOrderTakesNoCoinUntilTheTradeWindow()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 50_000);
            _online = player;
            long before = Supply();
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player, CrafterCustomers.Standing.Regular));
            CraftOrder order = CrafterCommerce.Place(crafter, player, Customer(player, CrafterCustomers.Standing.Regular), deal, upFront: false).Single();
            Assert.That(_wallets["Tamsin"], Is.EqualTo(50_000));
            DbInventoryItem piece = Make(crafter, order);
            AutonomousBotCraftOrders.Deliver(crafter);
            var window = player.TradeWindow as BotTradeWindow;
            Assert.That(window, Is.Not.Null, "the hand-over opens a trade window");
            Assert.That(window.Pieces, Is.EquivalentTo(new[] { piece }));
            Assert.That(window.Price, Is.EqualTo(deal.Ask));
            Assert.That(window.AcceptTrade(), Is.False, "the bot does not accept before the coin is in");
            window.TradeMoney = deal.Ask;
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(player.TradeWindow, Is.Null);
            Assert.That(_wallets["Tamsin"], Is.EqualTo(50_000 - deal.Ask));
            Assert.That(_purses[crafter.DatabaseID], Is.EqualTo(deal.Ask));
            Assert.That(AutonomousBotCraftOrders.OfBuyer("player:Tamsin"), Is.Empty);
            Assert.That(Residual(before), Is.EqualTo(0), "coin between a wallet and a purse is a transfer within the supply");
        }

        [Test]
        public void ALapsedUpFrontOrderRefundsThePlayerNowOrAtTheNextLogin()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 100_000);
            _online = player;
            long before = Supply();
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player));
            CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true);
            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + CrafterCommerce.PlayerOrderLifetime + TimeSpan.FromMinutes(1)), Is.EqualTo(1));
            Assert.That(CrafterCommerce.PayOwedOnline(), Is.EqualTo(deal.Ask), "the next pulse on the game loop pays it");
            Assert.That(_wallets["Tamsin"], Is.EqualTo(100_000), "refunded in the game");
            Assert.That(Residual(before), Is.EqualTo(0));

            // Offline when it lapses: owed (outside the supply, as the escrow was), paid at the next login.
            before = Supply();
            CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true);
            _online = null;
            AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + CrafterCommerce.PlayerOrderLifetime + TimeSpan.FromMinutes(1));
            Assert.That(CrafterCommerce.PayOwedOnline(), Is.EqualTo(0), "not in the game");
            Assert.That(_wallets["Tamsin"], Is.EqualTo(100_000 - deal.Ask));
            Assert.That(CrafterCustomers.Get(crafter.DatabaseID, "player:Tamsin").OwedCopper, Is.EqualTo(deal.Ask));
            Assert.That(CrafterCommerce.PayOwed(player), Is.EqualTo(deal.Ask));
            Assert.That(_wallets["Tamsin"], Is.EqualTo(100_000));
            Assert.That(CrafterCustomers.Get(crafter.DatabaseID, "player:Tamsin").OwedCopper, Is.EqualTo(0));
            Assert.That(CrafterCommerce.PayOwed(player), Is.EqualTo(0), "paid once");
            Assert.That(Residual(before), Is.EqualTo(0));
        }

        [Test]
        public void AnUncollectedPayOnDeliveryPieceCountsAgainstThePlayerAndStaysTheCraftersWork()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 0);
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player, CrafterCustomers.Standing.Regular));
            CraftOrder order = CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: false).Single();
            DbInventoryItem piece = Make(crafter, order);
            AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + CrafterCommerce.PlayerOrderLifetime + TimeSpan.FromMinutes(1));
            Assert.That(CrafterCustomers.Get(crafter.DatabaseID, "player:Tamsin").Stiffed, Is.EqualTo(1));
            Assert.That(crafter.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), Does.Contain(piece));
            Assert.That(AutonomousBotCrafting.IsOwnWork(crafter, piece), Is.True, "its own work again, to sell");
            Assert.That(CrafterCustomers.WantsUpFront(CrafterCustomers.StandingOf(0, 1, false, false), 90, false), Is.True,
                "next time, coin up front");
        }

        [Test]
        public void APlayerCancelsAnOpenOrderAndGetsTheEscrowBack()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 100_000);
            _online = player;
            long before = Supply();
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player));
            CraftOrder order = CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true).Single();
            Assert.That(AutonomousBotCraftOrders.TryCancelOpen(order.OrderId, "test"), Is.True);
            CrafterCommerce.PayOwedOnline();
            Assert.That(_wallets["Tamsin"], Is.EqualTo(100_000));
            Assert.That(Residual(before), Is.EqualTo(0));
            Assert.That(AutonomousBotCraftOrders.TryCancelOpen(order.OrderId, "test"), Is.False, "gone");
        }

        [Test]
        public void ASetIsOneOrderAPieceAndThePriceSplitsWhole()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 1_000_000);
            _online = player;
            CrafterTalk.Deal deal = Quote("can you make a full studded set lvl 30", Customer(player));
            IReadOnlyList<CraftOrder> placed = CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true);
            Assert.That(placed, Has.Count.EqualTo(6));
            Assert.That(placed.Sum(o => o.EscrowCopper), Is.EqualTo(deal.Ask));
            Assert.That(placed.Select(o => o.ProductName).Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void NothingHappensWithCommerceOffOrWithACompanion()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 100_000);
            _online = player;
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = false;
            Assert.That(CrafterCommerce.OnPlayerTell(player, crafter, "can you make a chain hauberk lvl 20"), Is.False);
            Assert.That(CrafterCommerce.TryReceiveMoney(crafter, player, 10_000), Is.False);
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = true;
            Assert.That(CrafterCommerce.IsCommerceBot(Crafter(companion: true), player), Is.False, "a /bot companion never trades");
            Assert.That(CrafterCommerce.IsCommerceBot(crafter, PlayerNamed("Mid", 0, eRealm.Midgard)), Is.False, "nor with another realm");
            Assert.That(CrafterCommerce.IsCommerceBot(crafter, player), Is.True);
        }

        [Test]
        public void TheLedgerSnapshotCountsThePlayersEscrowAndTheResidualIsZero()
        {
            // An hour in Albion: a player pays 2g into escrow, a crafter (5g) is paid 1g from an earlier escrow and sells
            // 50s of stock in a trade window. Supply: wallet 10g -> 7g 50s, purse 5g -> 6g 50s.
            var flows = new EconomyFlowLedger { BotInfo = _ => (1, 30) };
            DateTime start = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
            SupplySnapshot before = new SupplyBuilder().Bot(7, 1, 30, 50_000, online: true).Wallet(1, 100_000).Build(start);
            flows.RecordPlayerEscrow(1, -20_000);
            flows.Record(7, 10_000, EconomyFlowKind.CraftOrder, "CrafterCommerce.cs", "HandOver");
            flows.Record(7, 5_000, EconomyFlowKind.PlayerTrade, "BotTradeWindow.cs", "DefaultExecute");
            SupplySnapshot after = new SupplyBuilder().Bot(7, 1, 30, 65_000, online: true).Wallet(1, 75_000).Build(start.AddHours(1));
            EconomyLedgerSnapshot.RealmHour albion = EconomyLedgerSnapshot.Compute(before, after, flows.TakeWindow(), start.AddHours(1))
                .Single(hour => hour.Realm == 1);
            Assert.That(albion.DeltaSupply, Is.EqualTo(-10_000));
            Assert.That(albion.Residual, Is.EqualTo(0));
            Assert.That(albion.BotResidual, Is.EqualTo(0));
        }

        [Test]
        public void EveryCoinMoveOfThePackageNamesItsKind()
        {
            // The kinds WP8 uses, by the classifier the ledger reads (no Unattributed, and the player's coin a transfer).
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.PlayerTrade), Is.True);
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.Guild), Is.True);
            Assert.That(EconomyFlowClassifier.IsTransfer(EconomyFlowKind.CraftOrder), Is.False, "escrow is outside the supply");
            Assert.That(EconomyFlowClassifier.KeyName(EconomyFlowKind.PlayerTrade), Is.EqualTo("playertrade"));
        }

        #region Stock held for a player (owner 2026-11-18: "Yes fix those issues")

        /// <summary>A piece of the crafter's own work in its bag: stock it would sell.</summary>
        private static DbInventoryItem OwnWork(GameBot crafter, string name = "Chain Hauberk")
        {
            var item = new DbInventoryItem
            {
                Id_nb = "p_" + name.Replace(' ', '_'), Name = name, Quality = 97, IsCrafted = true, Creator = crafter.Name, Level = 20,
                Object_Type = (int)eObjectType.Chain, Item_Type = (int)eInventorySlot.TorsoArmor, MaxCondition = 100, Condition = 100,
                MaxDurability = 100, Durability = 100, Price = 4_000,
            };
            item.ObjectId = Guid.NewGuid().ToString("N");
            eInventorySlot slot = crafter.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            Assert.That(crafter.Inventory.AddItem(slot, item), Is.True);
            return item;
        }

        /// <summary>The player asks for the piece and says "deal" while the crafter hunts, far away: the sale is agreed for its break.</summary>
        private (GameBot Crafter, GamePlayer Player, DbInventoryItem Piece) AgreeStockSale()
        {
            GameBot crafter = Crafter();
            DbInventoryItem piece = OwnWork(crafter);
            GamePlayer player = PlayerNamed("Testrunner", 100_000);
            _online = player;
            CrafterCommerce.AtBreak = _ => false;
            CrafterCommerce.IsNear = (_, _) => false;
            CrafterCommerce.FindBot = id => id == crafter.DatabaseID ? crafter : null;
            CrafterTalk.Reply quote = CrafterCommerce.Handle(player, crafter, "wtb chain hauberk");
            Assert.That(quote.Deal?.Kind, Is.EqualTo(CrafterTalk.DealKind.Stock), quote.Text);
            CrafterTalk.Reply deal = CrafterCommerce.Handle(player, crafter, "deal");
            Assert.That(deal.Topic, Is.EqualTo("craft_stock_meet"), deal.Text);
            return (crafter, player, piece);
        }

        [Test]
        public void AnAgreedStockSaleSetsThePieceAsideFromOtherBuyersAndTheExchange()
        {
            (GameBot crafter, GamePlayer player, DbInventoryItem piece) = AgreeStockSale();
            Assert.That(CrafterCommerce.HoldsNow.Single().ObjectId, Is.EqualTo(piece.ObjectId));
            Assert.That(CrafterCommerce.HoldsNow.Single().UntilUtc, Is.GreaterThan(DateTime.UtcNow.AddMinutes(60)), "until after the break");
            Assert.That(EconomySeams.IsReserved(crafter, piece), Is.True, "never worn, listed on the Exchange or sold to a vendor");
            Assert.That(CrafterCommerce.StockOf(crafter), Is.Empty, "not offered as stock to anyone else");
            Assert.That(BotTrades.DefaultGoods(crafter).Select(g => g.Item), Has.No.Member(piece), "not in its trade-window goods");

            GamePlayer other = PlayerNamed("Othername", 100_000);
            CrafterTalk.Reply asked = CrafterCommerce.Handle(other, crafter, "wtb chain hauberk");
            Assert.That(asked.Deal?.Kind, Is.Not.EqualTo(CrafterTalk.DealKind.Stock), "another buyer is quoted a commission, not the held piece");
            CrafterTalk.Deal sneaky = new(CrafterTalk.DealKind.Stock, [], new CrafterTalk.StockPiece(piece.ObjectId, piece.Name, 97, 20, 4_000,
                piece.Object_Type, piece.Item_Type), 4_000, 3_200, 800, 0, false, DateTime.UtcNow.AddMinutes(10));
            CrafterCommerce.IsNear = (_, _) => true;
            CrafterCommerce.AtBreak = _ => true;
            Assert.That(CrafterCommerce.OpenStockTrade(crafter, other, sneaky), Is.Null, "no trade window for another player");
        }

        [Test]
        public void AHeldPieceIsReleasedWhenTheDealLapsesOrIsCalledOff()
        {
            (GameBot crafter, GamePlayer player, DbInventoryItem piece) = AgreeStockSale();
            CrafterCommerce.UtcNow = () => DateTime.UtcNow.AddHours(3);
            CrafterCommerce.PulseHolds();
            Assert.That(CrafterCommerce.HoldsNow, Is.Empty, "lapsed");
            Assert.That(EconomySeams.IsReserved(crafter, piece), Is.False);
            Assert.That(CrafterCommerce.StockOf(crafter).Select(p => p.ObjectId), Has.Member(piece.ObjectId), "back in stock, nothing lost");

            CrafterCommerce.UtcNow = () => DateTime.UtcNow;
            (crafter, player, piece) = AgreeStockSale();
            CrafterTalk.Reply cancel = CrafterCommerce.Handle(player, crafter, "forget it");
            Assert.That(cancel.Topic, Is.EqualTo("craft_stock_cancelled"), cancel.Text);
            Assert.That(CrafterCommerce.HoldsNow, Is.Empty, "cancelled");
            Assert.That(EconomySeams.IsReserved(crafter, piece), Is.False);
        }

        [Test]
        public void TheBreakTellComesOnceOnlyToAnOnlinePlayerWithAnOpenDeal()
        {
            // No deal: nothing to say at the break.
            CrafterCommerce.AtBreak = _ => true;
            Assert.That(CrafterCommerce.PulseHolds(), Is.EqualTo(0));

            (GameBot crafter, GamePlayer player, DbInventoryItem piece) = AgreeStockSale();
            _tells.Clear();
            Assert.That(CrafterCommerce.PulseHolds(), Is.EqualTo(0), "still hunting");
            CrafterCommerce.AtBreak = _ => true;
            _online = null;
            Assert.That(CrafterCommerce.PulseHolds(), Is.EqualTo(0), "the player is not in the game");
            _online = player;
            Assert.That(CrafterCommerce.PulseHolds(), Is.EqualTo(1));
            Assert.That(_tells.Single(), Does.StartWith("Aldwyn -> Testrunner: ").And.Contain("chain hauberk"));
            Assert.That(CrafterCommerce.PulseHolds(), Is.EqualTo(0), "once per deal");
            Assert.That(_tells, Has.Count.EqualTo(1));
            Assert.That(CrafterCommerce.HoldsNow.Single().Told, Is.True);
        }

        #endregion

        #region Issue #422: a trade that never finished

        /// <summary>
        /// The board calls the hand-over only at the two ends of a break: at its start the crafter is not yet between tasks, and
        /// at its end the next task is assigned before the scheduled hand-over runs. Both see no break, so a made piece was
        /// never handed over or announced. The game-loop pulse hands it over while the break lasts.
        /// </summary>
        [Test]
        public void AMadeOrderIsHandedOverWhileTheBreakLastsNotOnlyAtItsEnds()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 100_000);
            _online = player;
            CrafterCommerce.FindBot = id => id == crafter.DatabaseID ? crafter : null;
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player));
            CraftOrder order = CrafterCommerce.Place(crafter, player, Customer(player), deal, upFront: true).Single();
            DbInventoryItem piece = Make(crafter, order);

            CrafterCommerce.AtBreak = _ => false;
            AutonomousBotCraftOrders.Deliver(crafter);
            Assert.That(_received, Is.Empty, "the board's call at a break's edge finds no break");
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(0), "still hunting");

            CrafterCommerce.AtBreak = _ => true;
            crafter.PersistentRecord.ObjectiveAssignmentId = "break-1";
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(1));
            Assert.That(_received, Is.EquivalentTo(new[] { piece }), "handed over in the break");
            Assert.That(AutonomousBotCraftOrders.OfBuyer("player:Tamsin"), Is.Empty, "the order is closed");
            Assert.That(_purses[crafter.DatabaseID], Is.EqualTo(deal.Ask), "the escrow paid the crafter");
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(0), "nothing left to hand over");
        }

        [Test]
        public void AFarPlayerIsToldOnceABreakAndOneWindowOpensWhenTheyComeNear()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 50_000);
            _online = player;
            CrafterCommerce.FindBot = id => id == crafter.DatabaseID ? crafter : null;
            CrafterTalk.Deal deal = Quote("can you make a chain hauberk lvl 20", Customer(player, CrafterCustomers.Standing.Regular));
            CraftOrder order = CrafterCommerce.Place(crafter, player, Customer(player, CrafterCustomers.Standing.Regular), deal, upFront: false).Single();
            DbInventoryItem piece = Make(crafter, order);
            CrafterCommerce.AtBreak = _ => true;
            CrafterCommerce.IsNear = (_, _) => false;
            crafter.PersistentRecord.ObjectiveAssignmentId = "break-1";
            _tells.Clear();

            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(1));
            Assert.That(_tells.Single(), Does.Contain("chain hauberk"), "told where to collect it");
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(0), "once a break");
            Assert.That(_tells, Has.Count.EqualTo(1));

            CrafterCommerce.IsNear = (_, _) => true;
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(1), "the player came near: the hand-over");
            var window = player.TradeWindow as BotTradeWindow;
            Assert.That(window, Is.Not.Null);
            Assert.That(window.Pieces, Is.EquivalentTo(new[] { piece }));
            window.CloseTrade();
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(0), "one unasked try a break, no window after window");
            Assert.That(player.TradeWindow, Is.Null);

            CrafterTalk.Reply ready = CrafterCommerce.Handle(player, crafter, "ready");
            Assert.That(ready.Topic, Is.EqualTo("craft_ready_here"), ready.Text);
            Assert.That(player.TradeWindow, Is.InstanceOf<BotTradeWindow>(), "\"ready\" opens it again");
            ((BotTradeWindow)player.TradeWindow).CloseTrade();

            CrafterCommerce.IsNear = (_, _) => false;
            crafter.PersistentRecord.ObjectiveAssignmentId = "break-2";
            _tells.Clear();
            Assert.That(CrafterCommerce.PulseDeliveries(), Is.EqualTo(1), "the next break tells again");
            Assert.That(_tells.Single(), Does.Contain("chain hauberk"));
        }

        [Test]
        public void AStockTradeThatCannotOpenSaysWhyAndKeepsTheDeal()
        {
            (GameBot crafter, GamePlayer player, DbInventoryItem piece) = AgreeStockSale();
            CrafterCommerce.AtBreak = _ => true;
            CrafterCommerce.IsNear = (_, _) => true;
            player.TradeWindow = new BotTradeWindow(player, crafter);
            _tells.Clear();

            CrafterTalk.Reply yes = CrafterCommerce.Handle(player, crafter, "yes");
            Assert.That(yes.Act, Is.EqualTo(CrafterTalk.Act.OpenTrade), yes.Text);
            Assert.That(_tells.Last(), Does.Contain("close your trade window"), "the crafter says why");
            Assert.That(CrafterCommerce.HoldsNow.Single().ObjectId, Is.EqualTo(piece.ObjectId), "still set aside");

            player.TradeWindow = null;
            yes = CrafterCommerce.Handle(player, crafter, "yes");
            Assert.That(yes.Act, Is.EqualTo(CrafterTalk.Act.OpenTrade), "the deal stood: " + yes.Text);
            var window = player.TradeWindow as BotTradeWindow;
            Assert.That(window?.Pieces, Is.EquivalentTo(new[] { piece }));

            window.CloseTrade();
            yes = CrafterCommerce.Handle(player, crafter, "yes");
            Assert.That(yes.Act, Is.EqualTo(CrafterTalk.Act.OpenTrade), "a window closed without the trade keeps the deal: " + yes.Text);
            Assert.That(player.TradeWindow, Is.InstanceOf<BotTradeWindow>());
        }

        [Test]
        public void AStockPieceThatIsGoneIsSaidSoAndReleased()
        {
            (GameBot crafter, GamePlayer player, DbInventoryItem piece) = AgreeStockSale();
            CrafterCommerce.AtBreak = _ => true;
            CrafterCommerce.IsNear = (_, _) => true;
            crafter.Inventory.RemoveItemWithoutDbDeletion(piece);
            _tells.Clear();
            CrafterCommerce.Handle(player, crafter, "yes");
            Assert.That(player.TradeWindow, Is.Null);
            Assert.That(_tells.Last(), Does.Contain("is gone"));
            Assert.That(CrafterCommerce.HoldsNow, Is.Empty, "released");
        }

        [Test]
        public void NoTradeWindowIsBuiltForAPlayerAlreadyTrading()
        {
            GameBot crafter = Crafter();
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem piece = OwnWork(crafter);
            var other = new BotTradeWindow(player, crafter);
            player.TradeWindow = other;
            Assert.That(BotTradeWindow.Open(player, crafter, [piece], 100, "stock:" + piece.ObjectId, null), Is.Null);
            Assert.That(BotTrades.IsHeld(crafter), Is.False, "no trade hold for a window never shown");
            Assert.That(player.TradeWindow, Is.SameAs(other));
        }

        #endregion

    }
}
