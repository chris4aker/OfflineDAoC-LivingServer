using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP8: the crafter's side of a /tell conversation with a real player (CrafterTalk, pure). Every fact in
    /// an answer comes from the crafter's real skill, makeable recipes, stock and orders. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_CrafterTalk
    {
        private static readonly DateTime Now = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);

        internal static CrafterTalk.CrafterFacts Crafter(int skill = 310, bool atBreak = false, double toBreak = 30, params CrafterTalk.StockPiece[] stock)
        {
            string skills = $"{UT_CrafterQuotes.Ac}|{skill}";
            return new CrafterTalk.CrafterFacts("Aldwyn", eRealm.Albion, skills, eCraftingSkill.ArmorCrafting, skill,
                BotCraftingRules.Title(eCraftingSkill.ArmorCrafting, skill), CrafterQuotes.Makeable(UT_CrafterQuotes.Book(), skills, eRealm.Albion),
                stock, toBreak, atBreak, 60, "Camelot", 40);
        }

        internal static CrafterTalk.CustomerFacts Stranger(bool near = false) =>
            new("player:p1", "Tamsin", CrafterCustomers.Standing.Stranger, 0, false, near);

        /// <summary>The player's lines and the crafter's answers, the deal carried along as the glue carries it.</summary>
        private static List<(string Player, CrafterTalk.Reply Reply)> Talk(CrafterTalk.CrafterFacts crafter, CrafterTalk.CustomerFacts customer,
            params string[] lines)
        {
            var log = new List<(string, CrafterTalk.Reply)>();
            CrafterTalk.Deal deal = null;
            foreach (string line in lines)
            {
                CrafterTalk.Reply reply = CrafterTalk.Answer(crafter, customer, line, deal, [], Now);
                log.Add((line, reply));
                if (reply?.Deal != null)
                    deal = reply.Deal;
                else if (reply?.Act is CrafterTalk.Act.PlaceOrder or CrafterTalk.Act.Declined)
                    deal = null;
            }
            foreach ((string player, CrafterTalk.Reply reply) in log)
                TestContext.Out.WriteLine($"Tamsin -> Aldwyn: {player}\nAldwyn -> Tamsin: {reply?.Text ?? "(ordinary reply)"}   [{reply?.Act}]");
            return log;
        }

        [Test]
        public void AnExchangeFromQuestionToDeal()
        {
            var log = Talk(Crafter(), Stranger(), "hey what do you make?", "can you make a chain hauberk lvl 20?", "too much, how about 10s",
                "ok deal");
            Assert.That(log[0].Reply.Act, Is.EqualTo(CrafterTalk.Act.Summary));
            Assert.That(log[0].Reply.Text, Does.Contain("310 armorcraft").And.Contain("up to lvl 30").And.Contain("nothing in stock"));
            Assert.That(log[1].Reply.Act, Is.EqualTo(CrafterTalk.Act.Quote));
            CrafterTalk.Deal quoted = log[1].Reply.Deal;
            Assert.That(quoted.Recipes.Single().ProductName, Is.EqualTo("Chain Hauberk"));
            Assert.That(quoted.Ask, Is.EqualTo(AutonomousBotCraftOrders.Quote(quoted.Recipes[0].MaterialCostCopper)), "the board's price");
            Assert.That(log[1].Reply.Text, Does.Contain(CrafterTalk.PriceText(quoted.Ask)).And.Contain("ready in about 35 min"));
            Assert.That(log[2].Reply.Act, Is.EqualTo(CrafterTalk.Act.Haggle));
            Assert.That(log[2].Reply.Topic, Is.EqualTo("craft_haggle_counter"));
            Assert.That(log[2].Reply.Deal.Ask, Is.LessThan(quoted.Ask));
            Assert.That(log[2].Reply.Deal.Ask, Is.GreaterThanOrEqualTo(CrafterHaggle.Floor(quoted.Materials, quoted.Fee, false)));
            Assert.That(log[3].Reply.Act, Is.AnyOf(CrafterTalk.Act.PlaceOrder, CrafterTalk.Act.AwaitDeposit),
                "a patience-40 crafter wants a stranger's coin up front");
            Assert.That(log[3].Reply.Act, Is.EqualTo(CrafterTalk.Act.AwaitDeposit));
            Assert.That(log[3].Reply.Text, Does.Contain("up front"));
        }

        [Test]
        public void ItSaysWhatItCannotMake()
        {
            var log = Talk(Crafter(), Stranger(), "wtb plate breastplate lvl 20", "can you make a chain coif lvl 45", "lf sc for my sword");
            Assert.That(log[0].Reply.Act, Is.EqualTo(CrafterTalk.Act.Cannot));
            Assert.That(log[0].Reply.Text, Does.Contain("cant make that"));
            Assert.That(log[1].Reply.Text, Does.Contain("cant make lvl 45 yet").And.Contain("310").And.Contain("lvl 30 is my best"));
            Assert.That(log[2].Reply.Text, Does.Contain("not my trade, i do armorcraft"));
        }

        [Test]
        public void ALineAboutSomethingElseIsLeftToTheOrdinaryReply()
        {
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "hi there", null, [], Now), Is.Null);
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "where are you hunting?", null, [], Now), Is.Null);
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "can you make it to the raid tonight?", null, [], Now), Is.Null);
        }

        [Test]
        public void StockIsOfferedFirstAndOnlyAtABreakWithThePlayerNear()
        {
            var helm = new CrafterTalk.StockPiece("obj1", "Studded Helm", 98, 20, 4_500, (int)eObjectType.Studded, (int)eInventorySlot.HeadArmor);
            var away = Talk(Crafter(stock: helm), Stranger(), "wtb studded helm", "yes");
            Assert.That(away[0].Reply.Deal.Kind, Is.EqualTo(CrafterTalk.DealKind.Stock));
            Assert.That(away[0].Reply.Text, Does.Contain("studded helm 98% for 45s").And.Contain("catch me at my break"));
            Assert.That(away[1].Reply.Act, Is.Not.EqualTo(CrafterTalk.Act.OpenTrade), "no trade while the bot is hunting");
            var here = Talk(Crafter(atBreak: true, toBreak: 0, stock: helm), Stranger(near: true), "wtb studded helm", "yes");
            Assert.That(here[1].Reply.Act, Is.EqualTo(CrafterTalk.Act.OpenTrade));
        }

        [Test]
        public void ASetIsQuotedPieceByPiece()
        {
            var log = Talk(Crafter(), new CrafterTalk.CustomerFacts("player:p1", "Tamsin", CrafterCustomers.Standing.Regular, 5, false, false),
                "can you make a full studded set lvl 30", "deal");
            CrafterTalk.Deal set = log[0].Reply.Deal;
            Assert.That(set.Kind, Is.EqualTo(CrafterTalk.DealKind.Set));
            Assert.That(set.Recipes, Has.Count.EqualTo(6));
            long full = set.Recipes.Sum(r => AutonomousBotCraftOrders.Quote(r.MaterialCostCopper));
            Assert.That(set.Ask, Is.LessThan(full), "a regular's 5% off the fee");
            Assert.That(set.Ask, Is.GreaterThan(set.Materials));
            Assert.That(log[1].Reply.Act, Is.EqualTo(CrafterTalk.Act.PlaceOrder), "a regular pays on delivery");
        }

        [Test]
        public void ARefusedCustomerGetsNoDeal()
        {
            var refused = new CrafterTalk.CustomerFacts("player:p1", "Tamsin", CrafterCustomers.Standing.Refused, 0, false, true);
            CrafterTalk.Reply reply = CrafterTalk.Answer(Crafter(), refused, "wtb chain hauberk lvl 20", null, [], Now);
            Assert.That(reply.Act, Is.EqualTo(CrafterTalk.Act.Refuse));
            Assert.That(reply.Deal, Is.Null);
        }

        [Test]
        public void TheStatusOfAnOrderIsTrue()
        {
            var open = new CrafterTalk.OrderView("o1", "Chain Hauberk", CraftOrderState.Open, 10_000, false);
            var made = open with { State = CraftOrderState.Made };
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "ready?", null, [open], Now).Text, Does.StartWith("not yet"));
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "ready?", null, [made], Now).Text, Does.Contain("is done"));
            CrafterTalk.Reply here = CrafterTalk.Answer(Crafter(atBreak: true, toBreak: 0), Stranger(near: true), "is it done", null, [made], Now);
            Assert.That(here.Topic, Is.EqualTo("craft_ready_here"));
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "cancel it", null, [open], Now).Act, Is.EqualTo(CrafterTalk.Act.Cancel));
            Assert.That(CrafterTalk.Answer(Crafter(), Stranger(), "cancel it", null, [made], Now).Act, Is.EqualTo(CrafterTalk.Act.Status),
                "a made piece cannot be cancelled");
        }

        [Test]
        public void AQualityItCannotPromiseIsSaidInFixedWords()
        {
            CrafterTalk.Reply reply = CrafterTalk.Answer(Crafter(), Stranger(), "wtb 99% chain hauberk lvl 20", null, [], Now);
            Assert.That(reply.Text, Does.Contain("cant promise 99%"));
            Assert.That(reply.Topic, Is.Null, "no corpus line may drop the caveat");
        }

        [Test]
        public void ADealLapses()
        {
            CrafterTalk.Reply quote = CrafterTalk.Answer(Crafter(), Stranger(), "wtb chain hauberk lvl 20", null, [], Now);
            CrafterTalk.Reply late = CrafterTalk.Answer(Crafter(), Stranger(), "ok deal", quote.Deal, [], Now + CrafterTalk.DealLifetime + TimeSpan.FromMinutes(1));
            Assert.That(late?.Act, Is.Not.EqualTo(CrafterTalk.Act.AwaitDeposit).And.Not.EqualTo(CrafterTalk.Act.PlaceOrder));
        }
    }

    /// <summary>Crafting 2.0 WP8: bots read the player's /trade and the craft intent (pure parts). Tests only.</summary>
    [TestFixture]
    public class UT_PlayerTradeHeard
    {
        [Test]
        public void AWtbIsABuyAndAWtsIsNot()
        {
            Assert.That(CrafterCommerce.BuysInTrade("wtb chain hauberk lvl 23"), Is.True);
            Assert.That(CrafterCommerce.BuysInTrade("anyone make studded? need a set for 20"), Is.True);
            Assert.That(CrafterCommerce.BuysInTrade("wts chain hauberk 99%"), Is.False);
            Assert.That(CrafterCommerce.BuysInTrade("lol nice"), Is.False);
        }

        [Test]
        public void TheCraftIntentIsReadOnlyForCrafters()
        {
            foreach (string line in new[] { "can you make a studded helm?", "make me a chain set", "lf sc", "what can you craft?", "are you a crafter",
                         "wtb leather gloves lvl 15" })
            {
                Assert.That(AutonomousChatIntentModel.IsCraftRequest(line), Is.True, line);
                Assert.That(AutonomousChatIntentModel.PredictForCrafter(line), Is.EqualTo(eAutonomousChatIntent.Crafting), line);
                Assert.That(AutonomousChatIntentModel.Predict(line), Is.Not.EqualTo(eAutonomousChatIntent.Crafting), "Predict is unchanged: " + line);
            }
            foreach (string line in new[] { "can you make it to the raid?", "can u make it tonight", "hi", "where do you hunt" })
                Assert.That(AutonomousChatIntentModel.IsCraftRequest(line), Is.False, line);
        }

        [Test]
        public void CommerceIsOnByDefaultAndNeedsCrafters()
        {
            // Owner 2026-11-16: every Living Server feature on by default (WP8 built both off).
            bool crafters = CraftingEconomyProperties.BOT_CRAFTERS, commerce = CraftingEconomyProperties.BOT_CRAFTER_COMMERCE,
                social = CraftingEconomyProperties.BOT_CRAFTING_SOCIAL;
            try
            {
                foreach (string name in new[] { nameof(CraftingEconomyProperties.BOT_CRAFTER_COMMERCE), nameof(CraftingEconomyProperties.BOT_CRAFTING_SOCIAL) })
                    Assert.That(typeof(CraftingEconomyProperties).GetField(name)
                        .GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false)
                        .Cast<DOL.GS.ServerProperties.ServerPropertyAttribute>().Single().DefaultValue, Is.EqualTo(true), name);
                CraftingEconomyProperties.BOT_CRAFTERS = CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = true;
                Assert.That(CrafterCommerce.Enabled && CraftingDrama.On, Is.True);
                CraftingEconomyProperties.BOT_CRAFTERS = false;
                Assert.That(CrafterCommerce.Enabled || CraftingDrama.On, Is.False, "both need bot_crafters");
            }
            finally
            {
                CraftingEconomyProperties.BOT_CRAFTERS = crafters;
                CraftingEconomyProperties.BOT_CRAFTER_COMMERCE = commerce;
                CraftingEconomyProperties.BOT_CRAFTING_SOCIAL = social;
            }
        }
    }

    /// <summary>Crafting 2.0 WP8b: guild crafters and the price-war storyline (pure parts). Tests only.</summary>
    [TestFixture]
    public class UT_GuildCrafterAccess
    {
        [Test]
        public void AGuildmatePaysMaterialsAndTheCraftingGuildBankPaysTheFee()
        {
            long fee = AutonomousBotCraftOrders.Quote(4_000) - 4_000;
            Assert.That(CraftingDrama.BankFee(4_000, fee * 2.0, craftingBotGuild: true), Is.EqualTo(fee));
            Assert.That(CraftingDrama.BankFee(4_000, fee * 2.0 - 1, craftingBotGuild: true), Is.EqualTo(0), "a short bank pays nothing");
            Assert.That(CraftingDrama.BankFee(4_000, 1e9, craftingBotGuild: false), Is.EqualTo(0), "a player's guild bank is never spent");
            CraftRecipeInfo recipe = UT_CrafterQuotes.Piece("Chain Coif", 200, 20, eObjectType.Chain, eInventorySlot.HeadArmor, 4_000);
            Assert.That(CrafterQuotes.Price(recipe, $"{UT_CrafterQuotes.Ac}|300", 0, guildmate: true).Price, Is.EqualTo(4_000));
            Assert.That(CrafterCustomers.WantsUpFront(CrafterCustomers.Standing.Stranger, 0, guildmate: true), Is.False, "a guildmate pays on delivery");
        }

        [Test]
        public void TheGuildCrafterSaysItsRealCraft()
        {
            var facts = CraftingDrama.FactsOf($"{UT_CrafterQuotes.Ac}|512;{(int)eCraftingSkill.MetalWorking}|300");
            Assert.That(facts.Primary, Is.EqualTo(eCraftingSkill.ArmorCrafting));
            Assert.That(facts.Skill, Is.EqualTo(512));
            for (int seed = 0; seed < 6; seed++)
            {
                string text = CraftingDrama.GuildCrafterAnswer(facts, 30, bankPaysFee: seed % 2 == 0, new Random(seed));
                Assert.That(text, Does.Contain("512").And.Contain("armorcraft").And.Contain("lvl 30"));
            }
            Assert.That(CraftingDrama.AsksWhoCrafts("anyone craft? need a helm"), Is.True);
            Assert.That(CraftingDrama.AsksWhoCrafts("lf armorcrafter"), Is.True);
            Assert.That(CraftingDrama.AsksWhoCrafts("good night all"), Is.False);
        }

        [Test]
        public void TheStandingDecidesDiscountAndTerms()
        {
            Assert.That(CrafterCustomers.StandingOf(0, 0, false, false), Is.EqualTo(CrafterCustomers.Standing.Stranger));
            Assert.That(CrafterCustomers.StandingOf(1, 0, false, false), Is.EqualTo(CrafterCustomers.Standing.Regular));
            Assert.That(CrafterCustomers.StandingOf(3, 0, false, false), Is.EqualTo(CrafterCustomers.Standing.Friend));
            Assert.That(CrafterCustomers.StandingOf(5, 1, false, true), Is.EqualTo(CrafterCustomers.Standing.Stiffed));
            Assert.That(CrafterCustomers.StandingOf(5, 2, false, true), Is.EqualTo(CrafterCustomers.Standing.Refused));
            Assert.That(CrafterCustomers.StandingOf(5, 0, true, true), Is.EqualTo(CrafterCustomers.Standing.Refused), "an insult ends the dealing");
            Assert.That(CrafterCustomers.DiscountPercent(CrafterCustomers.Standing.Friend, 0), Is.EqualTo(10));
            Assert.That(CrafterCustomers.DiscountPercent(CrafterCustomers.Standing.Regular, 200_000), Is.EqualTo(15));
            Assert.That(CrafterCustomers.WantsUpFront(CrafterCustomers.Standing.Stiffed, 100, false), Is.True);
            Assert.That(CrafterCustomers.WantsUpFront(CrafterCustomers.Standing.Stranger, 70, false), Is.False);
        }
    }

    [TestFixture]
    public class UT_CraftingStorylines
    {
        private static AutonomousBotVoiceCorpus Corpus()
        {
            var assembly = typeof(AutonomousBotVoiceCorpus).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using var stream = assembly.GetManifestResourceStream(name);
                using var reader = new System.IO.StreamReader(stream);
                return reader.ReadToEnd();
            }
            return AutonomousBotVoiceCorpus.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        [Test]
        public void ThePriceWarIsAFourEpisodeArcBetweenTwoCrafters()
        {
            var arcs = AutonomousBotStorylines.Arcs(Corpus().Scenes);
            Assert.That(arcs.Keys, Does.Contain("pricewar"));
            var episodes = arcs["pricewar"];
            Assert.That(episodes.Length - 1, Is.EqualTo(4));
            Assert.That(episodes[4].SideA, Is.Not.Null);
            Assert.That(episodes[4].SideB, Is.Not.Null);
            Assert.That(CraftingDrama.IsCraftArc("pricewar"), Is.True);
            Assert.That(CraftingDrama.IsCraftArc("killsteal"), Is.False);
            foreach (var episode in episodes.Skip(1))
            {
                Assert.That(episode.Neutral.Roles["A"].Types, Does.Contain("Crafter"));
                Assert.That(episode.Neutral.Roles["B"].Types, Does.Contain("Crafter"));
            }
        }

        [Test]
        public void OnlyTwoTrainedCraftersOfOneGearOrderFitThePair()
        {
            var a = CraftingDrama.FactsOf($"{UT_CrafterQuotes.Ac}|240");
            var b = CraftingDrama.FactsOf($"{UT_CrafterQuotes.Ac}|410");
            var tailor = CraftingDrama.FactsOf($"{(int)eCraftingSkill.Tailoring}|300");
            var alchemist = CraftingDrama.FactsOf($"{(int)eCraftingSkill.Alchemy}|300");
            Assert.That(CraftingDrama.FitsPriceWar(a, b), Is.True);
            Assert.That(CraftingDrama.FitsPriceWar(a, tailor), Is.False);
            Assert.That(CraftingDrama.FitsPriceWar(alchemist, alchemist), Is.False, "potions have no broker war (stackables)");
            Assert.That(CraftingDrama.FitsPriceWar(a, CraftingDrama.FactsOf("")), Is.False);
            var slots = new Dictionary<string, string>();
            CraftingDrama.AddSlots(slots, a);
            Assert.That(slots["recipe"], Is.EqualTo("armorcraft"));
            Assert.That(slots["title"], Is.EqualTo(BotCraftingRules.Title(eCraftingSkill.ArmorCrafting, 240)));
        }
    }
}
