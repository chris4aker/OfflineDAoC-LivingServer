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
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The full bot trade (Crafting 2.0 WP8, built out on the owner's word of 2026-11-16): the terms (pure), the trade window
    /// in both directions with the live trade (coin and pieces moved at once, or nothing), the busy rule and the hold, repairs
    /// and spellcraft, a bot's own open order filled by a player, WP8's commissions through the same window, a scripted
    /// player-and-bot session end to end, the guild side (8b), and the economy ledger: every path leaves the realm's
    /// residual at 0. Only autonomous gamebots trade; a /bot companion never does. Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotTrade
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

        /// <summary>A player's bag for tests: GamePlayerInventory's AddItemWithoutDbAddition (the trade's way in) without a database.</summary>
        private sealed class PlayerBag : BotInventory
        {
            public string Owner;
            public override bool AddItemWithoutDbAddition(eInventorySlot slot, DbInventoryItem item)
            {
                if (!AddItem(slot, item))
                    return false;
                item.OwnerID = Owner;
                return true;
            }
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
        private readonly List<string> _transcript = new();
        private readonly Dictionary<string, BotTradeTerms.PlayerPiece> _verdicts = new();
        private readonly List<DbInventoryItem> _equipped = new();
        private EconomyFlowLedger _ledger;
        private bool _crafters, _orders, _commerce, _social;
        private BotTrades.Busy _busy;
        private static long _nextId = 9_820_000;

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
            _transcript.Clear();
            _verdicts.Clear();
            _equipped.Clear();
            _busy = BotTrades.Busy.Free;
            AutonomousBotCraftOrders.ResetForTests();
            CrafterCustomers.ResetForTests();
            CrafterCommerce.ResetForTests();
            CrafterCommerce.RegisterSeams();
            BotTrades.ResetForTests();
            BotGuildCommerce.ResetForTests();
            _ledger = new EconomyFlowLedger { BotInfo = _ => ((int)eRealm.Albion, 30) };
            EconomyFlows.Sink = _ledger;

            AutonomousBotCraftOrders.SpendCoin = Spend;
            AutonomousBotCraftOrders.PayCoin = Earn;
            CrafterCommerce.CustomerIdOf = player => "player:" + player.Name;
            CrafterCommerce.TakeCoin = (player, copper) =>
            {
                if (_wallets.GetValueOrDefault(player.Name) < copper)
                    return false;
                _wallets[player.Name] -= copper;
                return true;
            };
            CrafterCommerce.GiveCoin = (player, copper) => _wallets[player.Name] = _wallets.GetValueOrDefault(player.Name) + copper;
            CrafterCommerce.SendTell = (bot, player, text) => _transcript.Add($"{bot.Name} sends, \"{text}\"");
            CrafterCommerce.Schedule = (_, _, action) => action();
            CrafterCommerce.AtBreak = _ => true;
            CrafterCommerce.IsNear = (_, _) => true;
            CrafterCommerce.IsGuildmate = (_, _) => false;

            BotTrades.PurseOf = bot => _purses.GetValueOrDefault(bot.DatabaseID);
            BotTrades.InReach = (_, _) => true;
            BotTrades.BusyOf = _ => _busy;
            BotTrades.WalletOf = player => _wallets.GetValueOrDefault(player.Name);
            BotTrades.SayTo = (bot, player, text) => _transcript.Add($"{bot.Name} says, \"{text}\"");
            BotTrades.TellPlayer = (player, text) => _transcript.Add("  [system] " + text);
            BotTrades.BotSpend = Spend;
            BotTrades.BotEarn = Earn;
            BotGuildCommerce.BotEarn = Earn;
            BotGuildCommerce.BotSpend = Spend;
            BotTrades.Equip = (bot, item) =>
            {
                _equipped.Add(item);
                return true;
            };
            BotTrades.Appraise = (bot, item, _) => _verdicts.TryGetValue(item.Name, out BotTradeTerms.PlayerPiece verdict)
                ? verdict with { ObjectId = item.ObjectId }
                : new BotTradeTerms.PlayerPiece(item.ObjectId, item.Name, item.Quality, item.Level, 0, BotTradeTerms.Verdict.Refused, "it cannot use it");
            BotTrades.GoodsOf = bot => bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .Where(item => item != null && !_equipped.Contains(item))
                .Select((item, i) => new BotTrades.Good(i + 1, item, item.Price, BotTradeTerms.Source.Bag)).ToArray();
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.BOT_CRAFTERS = _crafters;
            CraftingEconomyProperties.BOT_CRAFT_ORDERS = _orders;
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = _commerce;
            CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = _social;
            EconomyFlows.Sink = null;
            BotTrades.ResetForTests();
            BotGuildCommerce.ResetForTests();
            CrafterCommerce.ResetForTests();
            CrafterCustomers.ResetForTests();
            AutonomousBotCraftOrders.ResetForTests();
            BotCommerceSeams.ResetForTests();
            GameServer.LoadTestDouble(_previousServer);
            if (_transcript.Count > 0)
                TestContext.Out.WriteLine(string.Join(Environment.NewLine, _transcript));
        }

        #region Helpers

        private bool Spend(long bot, long copper, EconomyFlowKind kind)
        {
            if (_purses.GetValueOrDefault(bot) < copper)
                return false;
            _purses[bot] -= copper;
            EconomyFlows.Record(bot, -copper, kind, "Test.cs", "Spend");
            return true;
        }

        private bool Earn(long bot, long copper, EconomyFlowKind kind)
        {
            _purses[bot] = _purses.GetValueOrDefault(bot) + copper;
            EconomyFlows.Record(bot, copper, kind, "Test.cs", "Earn");
            return true;
        }

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private GameBot Bot(string name = "Aldwyn", long purse = 0, bool companion = false, string skills = null)
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
                new OfflineWorldBotRecord { BotId = id, SerializedCraftingSkills = skills ?? string.Empty });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>());
            _purses[id] = purse;
            return bot;
        }

        private GamePlayer PlayerNamed(string name, long wallet)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.TestName = name;
            player.TestRealm = eRealm.Albion;
            player.InternalID = "char-" + name;
            player.Inventory = new PlayerBag { Owner = player.InternalID };
            _wallets[name] = wallet;
            return player;
        }

        private static DbInventoryItem Piece(string name, int level = 28, int quality = 96, long price = 0, eObjectType type = eObjectType.Studded,
            eInventorySlot slot = eInventorySlot.TorsoArmor, bool crafted = false)
        {
            var item = new DbInventoryItem
            {
                Id_nb = name.ToLowerInvariant().Replace(' ', '_'), Name = name, Level = level, Quality = quality, Price = price,
                Object_Type = (int)type, Item_Type = (int)slot, IsCrafted = crafted, MaxCondition = 100, Condition = 100,
                MaxDurability = 100, Durability = 100, IsDropable = true, IsTradable = true, IsPickable = true, MaxCount = 1, PackSize = 1,
            };
            item.ObjectId = Guid.NewGuid().ToString("N");
            return item;
        }

        private static DbInventoryItem Carry(GameLiving living, DbInventoryItem item)
        {
            eInventorySlot slot = living.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            Assert.That(living.Inventory.AddItem(slot, item), Is.True);
            return item;
        }

        private static bool InBag(GameLiving living, DbInventoryItem item) =>
            living.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).Contains(item);

        private long Supply() => _purses.Values.Sum() + _wallets.Values.Sum();

        /// <summary>supply now - supply before - (flows + the players' escrow), as WP2's snapshot computes it for the realm.</summary>
        private long Residual(long supplyBefore)
        {
            EconomyFlowLedger.Window window = _ledger.TakeWindow();
            int realm = (int)eRealm.Albion;
            long flows = window.ExternalNet(realm) + window.PlayerEscrow(realm);
            return Supply() - supplyBefore - flows;
        }

        private void Wants(string name, long bid, BotTradeTerms.Verdict verdict = BotTradeTerms.Verdict.Wanted, string orderId = null) =>
            _verdicts[name] = new BotTradeTerms.PlayerPiece(string.Empty, name, 96, 28, bid, verdict, null, orderId);

        #endregion

        #region The terms (pure)

        private static BotTradeTerms.BotPiece Mine(string name, long ask, BotTradeTerms.Source source = BotTradeTerms.Source.Bag) =>
            new(name, name, 96, 28, ask, source);

        private static BotTradeTerms.PlayerPiece Theirs(string name, long bid, BotTradeTerms.Verdict verdict = BotTradeTerms.Verdict.Wanted,
            string reason = null) => new(name, name, 96, 28, bid, verdict, reason);

        [Test]
        public void TheBotsAskIsOwedExactlyAndCoinBeyondItStaysWithThePlayer()
        {
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new("Aldwyn", [Mine("Studded Vest", 12_000)], [], 10_000, 50_000));
            Assert.That(quote.Owed, Is.EqualTo(12_000));
            Assert.That(quote.Accepts, Is.False);
            Assert.That(quote.Blocker, Is.EqualTo("Put 20 silver more in the window"));
            quote = BotTradeTerms.Evaluate(new("Aldwyn", [Mine("Studded Vest", 12_000)], [], 15_000, 50_000));
            Assert.That(quote.Accepts, Is.True);
            Assert.That(quote.Lines, Does.Contain("Aldwyn offers Studded Vest (96%, level 28) for 1 gold, 20 silver."));
            Assert.That(quote.Lines, Does.Contain("You pay 1 gold, 20 silver. (30 silver of your coin stays with you.)"));
        }

        [Test]
        public void ABotPaysForWhatItWantsButNeverPastItsPurseShare()
        {
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Chain Coif", 4_500)], 0, 10_000));
            Assert.That((quote.Owed, quote.BotPays, quote.Accepts), Is.EqualTo((0L, 4_500L, true)));
            quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Chain Coif", 4_500)], 0, 3_000));
            Assert.That(quote.BotPays, Is.EqualTo(3_000), "the purse share caps the payment");
            Assert.That(quote.CappedByPurse, Is.True);
            Assert.That(quote.Lines.Last(), Is.EqualTo("Aldwyn pays you 30 silver, all it can spare (worth 45 silver to it)."));
            Assert.That(BotTradeTerms.PurseCapOf(10_000, 0.8), Is.EqualTo(8_000));
        }

        [Test]
        public void ARefusedPieceBlocksTheTradeUntilItComesOut()
        {
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Chain Coif", 4_500),
                Theirs("Rusty Dagger", 0, BotTradeTerms.Verdict.Refused, "it cannot use it")], 0, 10_000));
            Assert.That(quote.Accepts, Is.False, "nothing is ever taken for free");
            Assert.That(quote.Lines, Does.Contain("Aldwyn does not want your Rusty Dagger (96%, level 28): it cannot use it."));
            Assert.That(quote.Blocker, Is.EqualTo("Take your Rusty Dagger out of the trade"));
        }

        [Test]
        public void ASwapSettlesTheDifferenceEitherWayAndAnEvenSwapMovesNoCoin()
        {
            Assert.That(BotTradeTerms.Evaluate(new("Aldwyn", [Mine("Longsword", 3_000)], [Theirs("Chain Coif", 5_000)], 0, 10_000)).BotPays, Is.EqualTo(2_000));
            Assert.That(BotTradeTerms.Evaluate(new("Aldwyn", [Mine("Longsword", 7_000)], [Theirs("Chain Coif", 5_000)], 2_000, 10_000)).Owed, Is.EqualTo(2_000));
            BotTradeTerms.Quote even = BotTradeTerms.Evaluate(new("Aldwyn", [Mine("Longsword", 5_000)], [Theirs("Chain Coif", 5_000)], 0, 0));
            Assert.That((even.Owed, even.BotPays, even.Accepts), Is.EqualTo((0L, 0L, true)));
            Assert.That(even.Lines.Last(), Is.EqualTo("An even swap: no coin changes hands."));
        }

        [Test]
        public void AnOrdersEscrowPaysFirstAndIsNotHeldToThePurseShare()
        {
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Chain Coif", 6_000, BotTradeTerms.Verdict.ForOrder),
                Theirs("Chain Mitts", 3_000)], 0, 1_000));
            Assert.That(quote.FromEscrow, Is.EqualTo(6_000));
            Assert.That(quote.FromPurse, Is.EqualTo(1_000));
            Assert.That(quote.BotPays, Is.EqualTo(7_000));
        }

        [Test]
        public void AJobsFeeIsOwedAndARefusedJobSaysWhy()
        {
            var job = new BotTradeTerms.JobQuote(BotTradeTerms.Job.Repair, "Studded Vest", 250, null, "80% to succeed, no charge if it fails");
            BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Studded Vest", 0, BotTradeTerms.Verdict.JobItem)], 250, 0, job));
            Assert.That((quote.Owed, quote.Accepts), Is.EqualTo((250L, true)));
            Assert.That(quote.Lines[0], Is.EqualTo("Aldwyn will repair your Studded Vest for 2 silver, 50 copper (80% to succeed, no charge if it fails)."));
            var refused = new BotTradeTerms.JobQuote(BotTradeTerms.Job.Spellcraft, "Studded Vest", 0, "it is no spellcrafter");
            quote = BotTradeTerms.Evaluate(new("Aldwyn", [], [Theirs("Studded Vest", 0, BotTradeTerms.Verdict.JobItem)], 0, 0, refused));
            Assert.That(quote.Accepts, Is.False);
            Assert.That(quote.Lines[0], Is.EqualTo("Aldwyn cannot imbue your Studded Vest: it is no spellcrafter."));
        }

        [TestCase(0, "nothing")]
        [TestCase(5, "5 copper")]
        [TestCase(12_005, "1 gold, 20 silver, 5 copper")]
        [TestCase(25_000_000, "2 platinum, 500 gold")]
        public void CoinIsWrittenExactly(long copper, string text) => Assert.That(BotTradeTerms.PriceText(copper), Is.EqualTo(text));

        #endregion

        #region The window and the trade

        [Test]
        public void BuyingFromABotsBagMovesThePieceAndTheCoinAndTheResidualStaysZero()
        {
            GameBot bot = Bot(purse: 1_000);
            DbInventoryItem vest = Carry(bot, Piece("Studded Vest", price: 12_000));
            GamePlayer player = PlayerNamed("Tamsin", 20_000);
            long before = Supply();
            BotTradeWindow window = BotTrades.TryOpen(player, bot, "command", out string why);
            Assert.That(window, Is.Not.Null, why);
            Assert.That(window.Title, Is.EqualTo("Trading with Aldwyn"));
            Assert.That(_transcript, Has.Some.EqualTo("  [system] [Aldwyn sells] 1. Studded Vest (96%, level 28): 1 gold, 20 silver"));
            Assert.That(BotTrades.Buy(player, 1, out why), Is.True, why);
            Assert.That(window.PartnerTradeItems, Is.EquivalentTo(new[] { vest }), "the piece shows on the bot's side");
            Assert.That(window.AcceptTrade(), Is.False, "no coin in yet");
            window.TradeMoney = 12_000;
            window.TradeUpdate();
            Assert.That(_transcript, Has.Some.EqualTo("  [system] Aldwyn has accepted the trade."));
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(InBag(player, vest), Is.True);
            Assert.That(InBag(bot, vest), Is.False);
            Assert.That(vest.OwnerID, Is.Not.EqualTo(bot.InternalID));
            Assert.That(_wallets["Tamsin"], Is.EqualTo(8_000));
            Assert.That(_purses[bot.DatabaseID], Is.EqualTo(13_000));
            Assert.That(player.TradeWindow, Is.Null, "the window closed");
            Assert.That(BotTrades.IsHeld(bot), Is.False, "the bot is free again");
            Assert.That(Residual(before), Is.EqualTo(0), "coin between a wallet and a purse is a transfer");
        }

        [Test]
        public void SellingToABotThatWantsThePiecePaysTheBidAndTheBotPutsItOn()
        {
            GameBot bot = Bot(purse: 10_000);
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 4_500);
            long before = Supply();
            Assert.That(BotTrades.TryReceiveItem(bot, player, coif), Is.True, "dropping the piece on the bot opens the window");
            var window = (BotTradeWindow)player.TradeWindow;
            Assert.That(window.PlayerPieces, Is.EquivalentTo(new[] { coif }));
            Assert.That(window.PartnerTradeMoney, Is.EqualTo(4_500), "the bot's coin shows on its side");
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(InBag(bot, coif), Is.True);
            Assert.That(coif.OwnerID, Is.EqualTo(bot.InternalID));
            Assert.That(_equipped, Is.EquivalentTo(new[] { coif }), "worn through the no-undress guard (TryEquipOwnedUpgrade)");
            Assert.That(_wallets["Tamsin"], Is.EqualTo(4_500));
            Assert.That(_purses[bot.DatabaseID], Is.EqualTo(5_500));
            Assert.That(Residual(before), Is.EqualTo(0));
        }

        [Test]
        public void ABotPaysOnlyItsPurseShareForAPieceWorthMore()
        {
            GameBot bot = Bot(purse: 2_000);
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 4_500);
            BotTrades.TryReceiveItem(bot, player, coif);
            var window = (BotTradeWindow)player.TradeWindow;
            Assert.That(window.PartnerTradeMoney, Is.EqualTo(1_600), "80% of a 2,000 copper purse");
            Assert.That(window.AcceptTrade(), Is.True, "the player may still take the lower offer");
            Assert.That(_purses[bot.DatabaseID], Is.EqualTo(400), "a purse is never emptied by a trade");
        }

        [Test]
        public void ASwapSettlesInCoinAndBothPiecesMove()
        {
            GameBot bot = Bot(purse: 10_000);
            DbInventoryItem sword = Carry(bot, Piece("Longsword", price: 3_000, type: eObjectType.SlashingWeapon, slot: eInventorySlot.RightHandWeapon));
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 5_000);
            long before = Supply();
            BotTradeWindow window = BotTrades.TryOpen(player, bot, "command", out _);
            BotTrades.Buy(player, 1, out _);
            window.AddItemToTrade(coif);
            Assert.That(window.Quote.BotPays, Is.EqualTo(2_000));
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(InBag(player, sword) && InBag(bot, coif), Is.True);
            Assert.That(_wallets["Tamsin"], Is.EqualTo(2_000));
            Assert.That(Residual(before), Is.EqualTo(0));
        }

        [Test]
        public void AnUnwantedPieceKeepsTheBotFromAcceptingAndNothingMoves()
        {
            GameBot bot = Bot(purse: 10_000);
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem dagger = Carry(player, Piece("Rusty Dagger", type: eObjectType.ThrustWeapon, slot: eInventorySlot.RightHandWeapon));
            BotTrades.TryReceiveItem(bot, player, dagger);
            var window = (BotTradeWindow)player.TradeWindow;
            Assert.That(window.AcceptTrade(), Is.False);
            Assert.That(_transcript, Has.Some.Contains("does not want your Rusty Dagger"));
            Assert.That(_transcript, Has.Some.EqualTo("  [system] Aldwyn has not accepted: Take your Rusty Dagger out of the trade."));
            Assert.That(InBag(player, dagger), Is.True);
            window.RemoveItemToTrade(dagger);
            Assert.That(window.Quote.Blocker, Is.EqualTo("Nothing to trade yet"));
        }

        [Test]
        public void ABusyBotSaysWhyAndNoWindowOpens()
        {
            GameBot bot = Bot();
            GamePlayer player = PlayerNamed("Tamsin", 0);
            _busy = BotTrades.Busy.Fighting;
            Assert.That(BotTrades.TryOpen(player, bot, "command", out string why), Is.Null);
            Assert.That(why, Is.EqualTo("Aldwyn is fighting and cannot trade now."));
            Assert.That(_transcript.Last(), Does.StartWith("Aldwyn says,"), "it answers in its own voice");
            _busy = BotTrades.Busy.Duty;
            Assert.That(BotTrades.TryOpen(player, bot, "command", out why), Is.Null);
            Assert.That(why, Is.EqualTo("Aldwyn is on a raid and cannot trade now."));
            Assert.That(player.TradeWindow, Is.Null);
        }

        [Test]
        public void AMidTaskBotHoldsStillForTheTradeAndAFightClosesIt()
        {
            GameBot bot = Bot();
            GamePlayer player = PlayerNamed("Tamsin", 0);
            Assert.That(BotTrades.TryOpen(player, bot, "command", out _), Is.Not.Null);
            Assert.That(BotTrades.HoldsStill(bot), Is.True, "the brain does nothing else while the window is open");
            _busy = BotTrades.Busy.Fighting;
            Assert.That(BotTrades.HoldsStill(bot), Is.False);
            Assert.That(player.TradeWindow, Is.Null);
            Assert.That(_transcript, Has.Some.EqualTo("  [system] Aldwyn is attacked and closes the trade."));
        }

        [Test]
        public void ACompanionNeverTrades()
        {
            GameBot companion = Bot("Pell", companion: true);
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif"));
            Assert.That(BotTrades.TryOpen(player, companion, "command", out string why), Is.Null);
            Assert.That(why, Does.Contain("companion"));
            Assert.That(BotTrades.TryReceiveItem(companion, player, coif), Is.False, "the drop falls through to the companion's own handling");
        }

        [Test]
        public void EverythingIsOffWithTheCommerceSwitchOff()
        {
            CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = false;
            GameBot bot = Bot();
            GamePlayer player = PlayerNamed("Tamsin", 0);
            Assert.That(BotTrades.TryOpen(player, bot, "command", out _), Is.Null);
            Assert.That(BotTrades.TryReceiveItem(bot, player, Carry(player, Piece("Chain Coif"))), Is.False);
            Assert.That(BotTrades.OnTell(player, bot, "what are you selling?"), Is.False);
        }

        [Test]
        public void ARepairIsChargedOnlyWhenItWorks()
        {
            GameBot bot = Bot(skills: $"{(int)eCraftingSkill.ArmorCrafting}|700;{(int)eCraftingSkill.LeatherCrafting}|700");
            GamePlayer player = PlayerNamed("Tamsin", 10_000);
            DbInventoryItem vest = Carry(player, Piece("Studded Vest", level: 20));
            vest.Condition = 60;
            BotTrades.TryReceiveItem(bot, player, vest);
            var window = (BotTradeWindow)player.TradeWindow;
            window.Repairing = true;
            window.TradeUpdate();
            BotTradeTerms.JobQuote job = BotTrades.JobOf(window, bot.PersistentRecord.SerializedCraftingSkills);
            Assert.That(job.Refused, Is.False, job.Refusal);
            Assert.That(window.Quote.Owed, Is.EqualTo(job.Fee));
            window.TradeMoney = job.Fee;
            BotTrades.Roll = _ => true;
            long before = Supply();
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(vest.Condition, Is.EqualTo(vest.MaxCondition), "repaired in the player's own bag");
            Assert.That(InBag(player, vest), Is.True);
            Assert.That(_wallets["Tamsin"], Is.EqualTo(10_000 - job.Fee));
            Assert.That(Residual(before), Is.EqualTo(0));

            // A failure: the fee comes back.
            vest.Condition = 60;
            BotTrades.TryReceiveItem(bot, player, vest);
            window = (BotTradeWindow)player.TradeWindow;
            window.Repairing = true;
            window.TradeMoney = job.Fee;
            BotTrades.Roll = _ => false;
            long wallet = _wallets["Tamsin"];
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(vest.Condition, Is.EqualTo(60));
            Assert.That(_wallets["Tamsin"], Is.EqualTo(wallet), "no charge if it fails");
            Assert.That(_transcript, Has.Some.Contains("fails to improve the condition of your Studded Vest"));
        }

        [Test]
        public void ARepairNeedsTheMaterialSkillAtTheItemsCraftLevel()
        {
            DbInventoryItem vest = Piece("Studded Vest", level: 40);
            vest.Condition = 50;
            BotTradeTerms.JobQuote job = BotTrades.RepairQuote([vest], 0, $"{(int)eCraftingSkill.LeatherCrafting}|100");
            Assert.That(job.Refusal, Is.EqualTo("its leatherworking (100) is too low for it"));
            Assert.That(BotTrades.RepairQuote([vest, vest], 0, "").Refusal, Is.EqualTo("put in exactly one piece to repair"));
            vest.Condition = 100;
            Assert.That(BotTrades.RepairQuote([vest], 0, $"{(int)eCraftingSkill.LeatherCrafting}|1000").Refusal, Is.EqualTo("it is fully repaired already"));
        }

        [Test]
        public void ASpellcrafterImbuesThePlayersGemsWithoutOverchargeAndTakesItsFee()
        {
            GameBot bot = Bot(skills: $"{(int)eCraftingSkill.SpellCrafting}|800");
            GamePlayer player = PlayerNamed("Tamsin", 50_000);
            DbInventoryItem vest = Carry(player, Piece("Studded Vest", level: 50, quality: 99, crafted: true));
            DbInventoryItem gem = Piece("Fiery Fervor Sigil", level: 1, quality: 99, price: 20_000, type: eObjectType.SpellcraftGem,
                slot: eInventorySlot.FirstBackpack);
            gem.Bonus1Type = (int)eProperty.Strength;
            gem.Bonus1 = 10;
            Carry(player, gem);
            BotTrades.TryReceiveItem(bot, player, vest);
            var window = (BotTradeWindow)player.TradeWindow;
            window.AddItemToTrade(gem);
            window.Combine = true;
            window.TradeUpdate();
            BotTradeTerms.JobQuote job = BotTrades.JobOf(window, bot.PersistentRecord.SerializedCraftingSkills);
            Assert.That(job.Refused, Is.False, job.Refusal);
            Assert.That(job.Fee, Is.EqualTo(2_000), "10% of the gem's value");
            window.TradeMoney = job.Fee;
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That((vest.Bonus1Type, vest.Bonus1), Is.EqualTo(((int)eProperty.Strength, 10)));
            Assert.That(InBag(player, gem), Is.False, "the gem is used up");
            Assert.That(InBag(player, vest), Is.True);

            // A few points over the piece's capacity (what a player may risk): refused, never overcharged.
            DbInventoryItem plain = Piece("Leather Jerkin", level: 10, quality: 94, crafted: true);
            DbInventoryItem big = Piece("Big Sigil", type: eObjectType.SpellcraftGem, slot: eInventorySlot.FirstBackpack);
            big.Bonus1Type = (int)eProperty.Dexterity;
            int max = SpellcraftRules.MaxImbuePoints(plain.Level, plain.Quality);
            big.Bonus1 = Enumerable.Range(1, 60).First(v =>
                SpellcraftRules.TotalImbuePoints([], [((int)eProperty.Dexterity, v)]) is int points && points > max && points <= max + 6);
            Assert.That(BotTrades.SpellcraftQuote([plain, big], 0, $"{(int)eCraftingSkill.SpellCrafting}|1000").Refusal,
                Does.Contain("it will not overcharge your piece"));
            Assert.That(BotTrades.SpellcraftQuote([plain, big], 0, string.Empty).Refusal, Is.EqualTo("it is no spellcrafter"));
        }

        [Test]
        public void APieceForABotsOwnOpenOrderCallsTheOrderOffAndItsEscrowPaysThePlayer()
        {
            GameBot buyer = Bot("Brannoc", purse: 1_000);
            GameBot crafter = Bot("Aldwyn");
            AutonomousBotCraftOrders.FindLive = id => id == buyer.DatabaseID ? buyer : id == crafter.DatabaseID ? crafter : null;
            _purses[buyer.DatabaseID] = 7_000;
            CraftRecipeInfo recipe = UT_CrafterQuotes.Piece("Chain Coif", 300, 28, eObjectType.Chain, eInventorySlot.HeadArmor, 4_000);
            CraftOrder order = AutonomousBotCraftOrders.Place(buyer, crafter, recipe, 6_000);
            Assert.That(order, Is.Not.Null);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(1_000), "the order's escrow left the purse");
            GamePlayer player = PlayerNamed("Tamsin", 0);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 6_000, BotTradeTerms.Verdict.ForOrder, order.OrderId);
            long before = Supply();
            _ledger.TakeWindow(); // the order's placement is its own hour
            BotTrades.TryReceiveItem(buyer, player, coif);
            var window = (BotTradeWindow)player.TradeWindow;
            Assert.That(window.Quote.FromEscrow, Is.EqualTo(6_000));
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(AutonomousBotCraftOrders.OfBuyer(order.BuyerId), Is.Empty, "the order is called off");
            Assert.That(_wallets["Tamsin"], Is.EqualTo(6_000));
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(1_000), "its escrow came back and paid the player");
            Assert.That(Residual(before), Is.EqualTo(0), "escrow back (CraftOrder) and paid out (PlayerTrade)");
        }

        [Test]
        public void ACommissionPaidOnDeliveryStillGoesThroughTheWindow()
        {
            GameBot crafter = Bot();
            GamePlayer player = PlayerNamed("Tamsin", 50_000);
            DbInventoryItem hauberk = Carry(crafter, Piece("Chain Hauberk", crafted: true, type: eObjectType.Chain));
            bool completed = false;
            BotTradeWindow window = BotTradeWindow.Open(player, crafter, [hauberk], 9_000, "order-1", _ => completed = true);
            Assert.That(window, Is.Not.Null);
            Assert.That(window.Price, Is.EqualTo(9_000));
            Assert.That(_transcript, Has.Some.EqualTo("  [system] Aldwyn hands over your Chain Hauberk (96%, level 28) for 90 silver."));
            window.TradeMoney = 9_000;
            long before = Supply();
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(completed, Is.True);
            Assert.That(InBag(player, hauberk), Is.True);
            Assert.That(Residual(before), Is.EqualTo(0));
        }

        #endregion

        #region A scripted session, end to end

        [Test]
        public void AScriptedSessionWithAGamebotBuysSellsAndSwaps()
        {
            GameBot bot = Bot("Aldwyn", purse: 20_000);
            DbInventoryItem vest = Carry(bot, Piece("Studded Vest", price: 12_000));
            DbInventoryItem bow = Carry(bot, Piece("Hunting Bow", level: 20, quality: 0, price: 2_500, type: eObjectType.Longbow,
                slot: eInventorySlot.DistanceWeapon));
            GamePlayer player = PlayerNamed("Tamsin", 30_000);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 4_500);
            long before = Supply();

            void Player(string text)
            {
                _transcript.Add($"You send, \"{text}\"");
                BotTrades.OnTell(player, bot, text);
            }

            Player("what are you selling?");
            Assert.That(_transcript, Has.Some.StartsWith("Aldwyn sends,"));
            Assert.That(_transcript, Has.Some.EqualTo("  [system] [Aldwyn sells] 2. Hunting Bow (level 20): 25 silver"));
            Player("trade me");
            var window = player.TradeWindow as BotTradeWindow;
            Assert.That(window, Is.Not.Null);
            Player("buy 1");
            Assert.That(window.Pieces, Is.EquivalentTo(new[] { vest }));
            _transcript.Add("(You drag your Chain Coif into the trade window.)");
            window.AddItemToTrade(coif);
            _transcript.Add("(You put 75 silver in the trade window.)");
            window.TradeMoney = 7_500;
            window.TradeUpdate();
            Assert.That(window.Quote.Owed, Is.EqualTo(7_500), "1g 20s for the vest less 45s for the coif");
            Assert.That(window.BotAccepts, Is.True);
            _transcript.Add("(You press Accept.)");
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(InBag(player, vest) && InBag(bot, coif), Is.True);
            Assert.That(_wallets["Tamsin"], Is.EqualTo(22_500));
            Assert.That(_purses[bot.DatabaseID], Is.EqualTo(27_500));
            Assert.That(_transcript.Last(), Does.StartWith("Aldwyn says,"));

            // A second trade: the bow, paid in full.
            Player("trade me");
            window = (BotTradeWindow)player.TradeWindow;
            Player("buy 1");
            Assert.That(window.Pieces, Is.EquivalentTo(new[] { bow }), "the list was read again: the bow is number 1 now");
            window.AddMoneyToTrade(2_500);
            Assert.That(window.AcceptTrade(), Is.True);
            Assert.That(InBag(player, bow), Is.True);
            Assert.That(Residual(before), Is.EqualTo(0), "the whole session is transfers within the supply");
        }

        #endregion

        #region The command, the voice, WP8's stock mid-task

        [Test]
        public void TheCommandWorksTheWindowLikeTheClientsOwnButtons()
        {
            GameBot bot = Bot(purse: 10_000);
            DbInventoryItem vest = Carry(bot, Piece("Studded Vest", price: 12_000));
            GamePlayer player = PlayerNamed("Tamsin", 20_000);
            DbInventoryItem coif = Carry(player, Piece("Chain Coif", type: eObjectType.Chain, slot: eInventorySlot.HeadArmor));
            Wants("Chain Coif", 4_500);
            BotTradeWindow window = BotTrades.TryOpen(player, bot, "command", out _);
            Assert.That(BotTrades.Command(player, ["&tradewindow", "buy", "1"]), Is.Empty);
            Assert.That(BotTrades.Command(player, ["&tradewindow", "put", coif.SlotPosition.ToString()]), Is.Empty);
            Assert.That(window.PlayerPieces, Is.EquivalentTo(new[] { coif }));
            Assert.That(BotTrades.Command(player, ["&tradewindow", "coin", "999999"]), Is.Empty);
            Assert.That(window.TradeMoney, Is.EqualTo(20_000), "never more than the player has");
            Assert.That(window.Quote.Owed, Is.EqualTo(7_500));
            Assert.That(BotTrades.Command(player, ["&tradewindow", "accept"]), Is.Empty);
            Assert.That(InBag(player, vest) && InBag(bot, coif), Is.True);
            Assert.That(_wallets["Tamsin"], Is.EqualTo(12_500), "only what was owed was taken");
            Assert.That(BotTrades.Command(player, ["&tradewindow", "accept"]), Is.EqualTo(new[] { "No trade with a gamebot is open." }));
            Assert.That(BotTrades.Command(player, ["&tradewindow", "nonsense"])[0], Does.StartWith("Usage: /tradewindow"));
        }

        [Test]
        public void EveryTradeTopicHasCorpusLinesAndTheGapAdvertHasItsOwn()
        {
            var assembly = typeof(AutonomousBotVoiceCorpus).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using var reader = new System.IO.StreamReader(assembly.GetManifestResourceStream(name));
                return reader.ReadToEnd();
            }
            AutonomousBotVoiceCorpus corpus = AutonomousBotVoiceCorpus.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
            string[] topics = ["trade_busy_fight", "trade_busy_duty", "trade_busy_trading", "trade_timeout", "trade_come_closer",
                "trade_nothing_for_sale", "trade_for_sale", "trade_wants_gap", "trade_wants_maybe", "trade_done_swap", "trade_done_bought",
                "trade_done_sold", "trade_done_job", "trade_job_failed"];
            foreach (string topic in topics)
                Assert.That(corpus.Lines.Count(line => line.Kind == "trade_reply" && line.Topic == topic), Is.GreaterThanOrEqualTo(2), topic);
            Assert.That(corpus.Lines.Count(line => line.Kind == "trade" && line.Text.Contains("{item}") && line.Text.Contains("wtb")), Is.GreaterThan(0));
        }

        [Test]
        public void AGapAdvertNamesTheRealPieceTheBotLacks()
        {
            GameBot bot = Bot();
            bot.Level = 25;
            Assert.That(BotTrades.SlotWord(eInventorySlot.TorsoArmor), Is.EqualTo("chest piece"));
            Assert.That(BotTrades.SlotWord(eInventorySlot.HandsArmor), Is.EqualTo("pair of gloves"));
            string text = BotTrades.WantedText(bot, new BotGearPipeline.Gap(eInventorySlot.TorsoArmor, BotGearPipeline.GapReason.Empty));
            Assert.That(text, Is.Null.Or.Matches(@"^(cloth|leather|studded|chain|plate|reinforced|scale) chest piece lvl 25$"));
        }

        [Test]
        public void ACrafterFreeToTradeOffersItsStockMidTask()
        {
            CrafterTalk.CrafterFacts crafter = UT_CrafterTalk.Crafter(310, false, 30,
                new CrafterTalk.StockPiece("s1", "Studded Jerkin", 97, 20, 9_000, (int)eObjectType.Studded, (int)eInventorySlot.TorsoArmor));
            var customer = new CrafterTalk.CustomerFacts("player:Tamsin", "Tamsin", CrafterCustomers.Standing.Stranger, 0, false, true);
            string ask = "wtb studded jerkin";
            CrafterTalk.Reply hunting = CrafterTalk.Answer(crafter, customer, ask, null, [], DateTime.UtcNow);
            Assert.That(hunting.Text, Does.Contain("catch me at my break"), "busy with its task: as WP8");
            CrafterTalk.Reply free = CrafterTalk.Answer(crafter with { FreeToTrade = true }, customer, ask, null, [], DateTime.UtcNow);
            Assert.That(free.Text, Does.Contain("say yes and ill open a trade"), "the player's own trade, mid-task");
        }

        #endregion

        #region The guild side (8b)

        [Test]
        public void CraftersArePlacedFirstAndIntoTheCraftingGuild()
        {
            var crafting = new AutonomousBotGuildPlanner.GuildSlot("craft", BotGuildCharter.Crafting, 5, 3, 0);
            var leveling = new AutonomousBotGuildPlanner.GuildSlot("level", BotGuildCharter.Leveling, 1, 10, 0);
            var candidates = Enumerable.Range(1, 12).Select(i => new AutonomousBotGuildPlanner.Candidate(i, 20, Crafter: i % 4 == 0)).ToArray();
            List<AutonomousBotGuildPlanner.Placement> placed = AutonomousBotGuildPlanner.Place(candidates, [leveling, crafting], 13);
            long[] inCrafting = placed.Where(p => p.GuildId == "craft").Select(p => p.BotId).OrderBy(id => id).ToArray();
            Assert.That(inCrafting, Is.EqualTo(new long[] { 4, 8, 12 }), "the three crafters fill the Crafting guild");
            // Without crafters marked (the switch off) placement is as before.
            List<AutonomousBotGuildPlanner.Placement> plain = AutonomousBotGuildPlanner.Place(
                candidates.Select(c => c with { Crafter = false }), [new("level", BotGuildCharter.Leveling, 1, 10, 0),
                    new("craft", BotGuildCharter.Crafting, 5, 3, 0)], 13);
            Assert.That(plain.Count(p => p.GuildId == "craft"), Is.EqualTo(3));
            Assert.That(plain.Where(p => p.GuildId == "craft").All(p => p.BotId % 4 == 0), Is.False);
        }

        [Test]
        public void AGuildmateCrafterIsAskedFirstAtMaterialCostAndACraftingBankPaysTheFee()
        {
            CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = true;
            BotGuildCommerce.RegisterSeams();
            var guild = new Guild(new DbGuild { GuildName = "Hammer and Tongs", GuildID = "hat", Realm = (byte)eRealm.Albion, Bank = 100_000 });
            GameBot buyer = Bot("Brannoc", purse: 50_000);
            GameBot guildmate = Bot("Aldwyn");
            GameBot stranger = Bot("Cerys");
            BotGuildCommerce.GuildOf = bot => bot == stranger ? null : guild;
            BotGuildCommerce.CharterOf = g => g == guild ? BotGuildCharter.Crafting : null;
            BotGuildCommerce.PaidMaterialsOnly = order => true;
            AutonomousBotCraftOrders.FindLive = id => new[] { buyer, guildmate, stranger }.FirstOrDefault(b => b.DatabaseID == id);
            Assert.That(BotGuildCommerce.IsGuildmate(guildmate, buyer.DatabaseID), Is.True);
            Assert.That(BotGuildCommerce.IsGuildmate(stranger, buyer.DatabaseID), Is.False);
            CraftRecipeInfo recipe = UT_CrafterQuotes.Piece("Chain Coif", 300, 28, eObjectType.Chain, eInventorySlot.HeadArmor, 4_000);
            Assert.That(BotGuildCommerce.GuildPrice(buyer, guildmate, recipe), Is.EqualTo(recipe.MaterialCostCopper), "materials only");
            Assert.That(BotGuildCommerce.GuildPrice(buyer, stranger, recipe), Is.Null, "the board's quote");
            long before = Supply() + (long)guild.GetGuildBank();
            var order = new CraftOrder("o1", eRealm.Albion, CraftOrderSource.Bot, buyer.DatabaseID, buyer.InternalID, buyer.Name, guildmate.DatabaseID,
                recipe.Skill, recipe.ProductId, recipe.ProductName, recipe.Level, 0, 1, recipe.MaterialCostCopper, true, CraftOrderState.Made,
                string.Empty, DateTime.UtcNow, DateTime.UtcNow.AddHours(3));
            long fee = BotGuildCommerce.OnBotOrderDelivered(guildmate, buyer, order);
            Assert.That(fee, Is.EqualTo(AutonomousBotCraftOrders.Quote(recipe.MaterialCostCopper) - recipe.MaterialCostCopper));
            Assert.That(_purses[guildmate.DatabaseID], Is.EqualTo(fee));
            EconomyFlowLedger.Window flows = _ledger.TakeWindow();
            Assert.That(flows.ExternalNet((int)eRealm.Albion), Is.EqualTo(0), "bank to crafter is a Guild transfer");
            Assert.That(Supply() + (long)guild.GetGuildBank(), Is.EqualTo(before));
        }

        [Test]
        public void ACraftingGuildsBankPaysForMaterialsOnlyWhileItHoldsTenTimesThePrice()
        {
            CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = true;
            var guild = new Guild(new DbGuild { GuildName = "Hammer and Tongs", GuildID = "hat", Realm = (byte)eRealm.Albion, Bank = 10_000 });
            GameBot crafter = Bot("Aldwyn");
            BotGuildCommerce.GuildOf = _ => guild;
            BotGuildCommerce.CharterOf = _ => BotGuildCharter.Crafting;
            Assert.That(BotGuildCommerce.FundMaterials(crafter, 1_000), Is.EqualTo(1_000));
            Assert.That(guild.GetGuildBank(), Is.EqualTo(9_000));
            Assert.That(BotGuildCommerce.FundMaterials(crafter, 1_000), Is.EqualTo(0), "9,000 is less than ten times the price");
            BotGuildCommerce.CharterOf = _ => BotGuildCharter.Leveling;
            guild.AddToBank(100_000, false);
            Assert.That(BotGuildCommerce.FundMaterials(crafter, 1_000), Is.EqualTo(0), "only a Crafting-charter guild");
            CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = false;
            BotGuildCommerce.CharterOf = _ => BotGuildCharter.Crafting;
            Assert.That(BotGuildCommerce.FundMaterials(crafter, 1_000), Is.EqualTo(0), "off with bot_crafting_social");
        }

        #endregion
    }
}
