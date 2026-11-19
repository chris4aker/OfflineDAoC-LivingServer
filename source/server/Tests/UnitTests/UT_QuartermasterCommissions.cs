using System;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP5: realm quartermaster commissions (written new) and True165's milestone sets (CraftOrders165,
    /// f6dbe48, GPL-3.0, adapted to the board), on the same fixture as the board. Tests only.
    /// </summary>
    public partial class UT_AutonomousBotCraftingOrders
    {
        #region Quartermaster commissions

        [Test]
        public void CommissionsAreDoubledByDefaultAndTheGovernorScalesThem()
        {
            Assert.That(typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.REALM_COMMISSIONS))
                .GetCustomAttribute<ServerPropertyAttribute>().DefaultValue, Is.EqualTo("doubled"));
            Assert.That(QuartermasterCommissions.ParseMode("doubled"), Is.EqualTo(QuartermasterCommissions.Mode.Doubled));
            Assert.That(QuartermasterCommissions.ParseMode(" Classic "), Is.EqualTo(QuartermasterCommissions.Mode.Classic));
            Assert.That(QuartermasterCommissions.ParseMode("off"), Is.EqualTo(QuartermasterCommissions.Mode.Off));
            Assert.That(QuartermasterCommissions.ParseMode("garbage"), Is.EqualTo(QuartermasterCommissions.Mode.Doubled), "the default");
            Assert.That(QuartermasterCommissions.PerHour(QuartermasterCommissions.Mode.Classic, 1), Is.EqualTo(30));
            Assert.That(QuartermasterCommissions.PerHour(QuartermasterCommissions.Mode.Doubled, 1), Is.EqualTo(60));
            Assert.That(QuartermasterCommissions.PerHour(QuartermasterCommissions.Mode.Off, 2), Is.Zero);
            Assert.That(QuartermasterCommissions.PerHour(QuartermasterCommissions.Mode.Doubled, 0.5), Is.EqualTo(30));
            QuartermasterCommissions.GovernorMultiplier = 7;
            Assert.That(QuartermasterCommissions.GovernorMultiplier, Is.EqualTo(2), "WP7's governor is kept within 0-2");
            QuartermasterCommissions.GovernorMultiplier = double.NaN;
            Assert.That(QuartermasterCommissions.GovernorMultiplier, Is.EqualTo(1));
        }

        [Test]
        public void ThePayCoversTheMaterialsAndTheTime()
        {
            Assert.That(QuartermasterCommissions.Pay(1_000, 10, 100), Is.EqualTo(3_000), "at least three times the materials (the simulator)");
            Assert.That(QuartermasterCommissions.Pay(10, 10, 100), Is.EqualTo(10 + AutonomousBotCraftOrders.MinimumFee), "the board's minimum fee");
            Assert.That(QuartermasterCommissions.Pay(100, 60, 1_000), Is.EqualTo(100 + 60 * 11), "a Legendary's hour is worth more");
            Assert.That(QuartermasterCommissions.WagePerSecond(0), Is.EqualTo(1));
            Assert.That(QuartermasterCommissions.WagePerSecond(5_000), Is.EqualTo(12), "capped at 1100");
        }

        [Test]
        public void TheWorkIsSizedToTheCraftersSkill()
        {
            CraftRecipeInfo easy = MakeRecipe(50, price: 10);
            CraftRecipeInfo apprentice = MakeRecipe(200, price: 30);
            CraftRecipeInfo journeyman = MakeRecipe(600, price: 80);
            CraftRecipeInfo legendary = MakeRecipe(1_000, price: 500);
            CraftRecipeInfo stack = MakeRecipe(190, price: 1, unique: false);
            Templates[stack.ProductId].MaxCount = 50;
            stack = stack with { Stackable = true };
            CraftRecipeInfo armor = MakeRecipe(200, price: 30, skill: eCraftingSkill.ArmorCrafting);
            CraftRecipeInfo[] all = [easy, apprentice, journeyman, legendary, stack, armor];

            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|210;6|300", eRealm.Albion, 10_000, null), Is.SameAs(apprentice),
                "an Apprentice gets apprentice work");
            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|1000;6|1000", eRealm.Albion, 10_000, null), Is.SameAs(legendary));
            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|1000;6|1000", eRealm.Albion, 200, null), Is.SameAs(journeyman),
                "the crafter buys the materials: within its purse");
            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|40;6|300", eRealm.Albion, 10_000, null), Is.Null, "skill 50 and up only");
            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|210;6|300", eRealm.Albion, 10_000, r => r != apprentice), Is.SameAs(easy),
                "materials not on sale");
            Assert.That(QuartermasterCommissions.ChooseWork(all, "1|210;6|300", eRealm.Midgard, 10_000, null), Is.Null, "the realm's own products");
        }

        [Test]
        public void TheQuartermasterPostsItsMinutesShareToTheLongestUnaskedCrafter()
        {
            _catalog.Recipes.Add(MakeRecipe(200, price: 30));
            TestBot first = Bot("First", "1|210;6|300", crafter: true);
            TestBot second = Bot("Second", "1|210;6|300", crafter: true);
            Bot("Novice", "1|20;6|20", crafter: true);
            _purses[first.DatabaseID] = _purses[second.DatabaseID] = 10_000;
            DateTime now = DateTime.UtcNow;

            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 0.5, now), Is.Zero, "half an order owed");
            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 0.5, now), Is.EqualTo(1));
            CraftOrder posted = AutonomousBotCraftOrders.All().Single();
            Assert.That(posted.Source, Is.EqualTo(CraftOrderSource.Quartermaster));
            Assert.That(posted.BuyerId, Is.EqualTo("quartermaster:1"));
            Assert.That(posted.BuyerName, Is.EqualTo("Quartermaster of Albion"));
            Assert.That(posted.PayUpFront, Is.False, "nobody's coin is held");
            Assert.That(_moves, Is.Empty, "posting moves no coin");

            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 1, now), Is.EqualTo(1));
            Assert.That(AutonomousBotCraftOrders.All().Select(order => order.CrafterBotId), Is.EquivalentTo(new[] { first.DatabaseID, second.DatabaseID }),
                "the work goes round");
            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 100, now), Is.EqualTo(2),
                "a quiet spell owes at most five, and each crafter takes two");
            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Midgard, 100, now), Is.Zero, "no Midgard crafter");
        }

        [Test]
        public void ACommissionIsMadeHandedToTheRealmAndPaidAsAFaucet()
        {
            CraftRecipeInfo recipe = MakeRecipe(200, price: 30);
            _catalog.Recipes.Add(recipe);
            TestBot smith = Bot("Smith", "1|210;6|300", crafter: true);
            _purses[smith.DatabaseID] = 10_000;
            CraftOrder order = QuartermasterCommissions.Post(smith, DateTime.UtcNow);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.EscrowCopper, Is.GreaterThanOrEqualTo(3 * recipe.MaterialCostCopper));

            BotCraftingRules.Session session = AutonomousBotCraftOrders.PlanOrder(smith, 10_000);
            Assert.That(session.OrderId, Is.EqualTo(order.OrderId));
            DbInventoryItem piece = OwnPiece(smith, "guard's sword");
            Assert.That(AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece), Is.True);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.EqualTo(1));

            Assert.That(smith.Inventory.AllItems, Does.Not.Contain(piece), "the guards have it");
            Assert.That(_moves, Is.EqualTo(new[] { (smith.DatabaseID, order.EscrowCopper, EconomyFlowKind.Commission) }),
                "the realm's pay is new coin, recorded as a Commission faucet");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
            var lines = QuartermasterCommissions.HourLines(DateTime.UtcNow, reset: false);
            Assert.That(lines.Single(), Does.StartWith("ECON_COMMISSION outcome=\"hour\" realm=Albion mode=\"doubled\" governor=1 posted=1 delivered=1 expired=0 paid="));
        }

        [Test]
        public void ALapsedCommissionRefundsNobody()
        {
            _catalog.Recipes.Add(MakeRecipe(200, price: 30));
            TestBot smith = Bot("Smith", "1|210;6|300", crafter: true);
            _purses[smith.DatabaseID] = 10_000;
            QuartermasterCommissions.Post(smith, DateTime.UtcNow);
            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + AutonomousBotCraftOrders.Lifetime + TimeSpan.FromMinutes(1)), Is.EqualTo(1));
            Assert.That(_moves, Is.Empty);
        }

        [Test]
        public void NoCommissionsWhileTheSwitchesSayNo()
        {
            _catalog.Recipes.Add(MakeRecipe(200, price: 30));
            TestBot smith = Bot("Smith", "1|210;6|300", crafter: true);
            _purses[smith.DatabaseID] = 10_000;
            CraftingEconomyProperties.REALM_COMMISSIONS = "off";
            Assert.That(QuartermasterCommissions.PostIfDue(1, DateTime.UtcNow), Is.Zero);
            CraftingEconomyProperties.REALM_COMMISSIONS = "doubled";
            CraftingEconomyProperties.BOT_CRAFTERS = false;
            QuartermasterCommissions.ResetForTests();
            QuartermasterCommissions.CraftersOf = realm => _live.Values.Where(bot => bot.Realm == realm);
            QuartermasterCommissions.ScheduledOnline = (_, _) => true;
            Assert.That(QuartermasterCommissions.PostIfDue(1, DateTime.UtcNow), Is.Zero, "bot_crafters off: the mode does nothing");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
        }

        [Test]
        public void ACrafterTheScheduleSendsOffSoonIsNotAsked()
        {
            Assert.That(QuartermasterCommissions.StaysOnline(true, true), Is.True);
            Assert.That(QuartermasterCommissions.StaysOnline(true, false), Is.False, "gone before its trip would end");
            Assert.That(QuartermasterCommissions.StaysOnline(false, true), Is.False, "leaving now");
            Assert.That(QuartermasterCommissions.OnlineLookahead, Is.EqualTo(TimeSpan.FromMinutes(75)), "the 45-minute window and a trip");

            _catalog.Recipes.Add(MakeRecipe(200, price: 30));
            TestBot leaver = Bot("Leaver", "1|210;6|300", crafter: true);
            TestBot stayer = Bot("Stayer", "1|210;6|300", crafter: true);
            _purses[leaver.DatabaseID] = _purses[stayer.DatabaseID] = 10_000;
            DateTime now = DateTime.UtcNow;
            QuartermasterCommissions.ScheduledOnline = (bot, at) => bot != leaver || at < now + TimeSpan.FromMinutes(30);

            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 100, now), Is.EqualTo(1), "one a crafter a pass");
            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 0, now), Is.EqualTo(1));
            Assert.That(AutonomousBotCraftOrders.All().Select(order => order.CrafterBotId).Distinct(), Is.EqualTo(new[] { stayer.DatabaseID }),
                "the crafter logging off in half an hour gets none");
        }

        [Test]
        public void AnOpenCommissionGoesBackWhenItsCrafterCannotComeForIt()
        {
            DateTime now = DateTime.UtcNow;
            var order = new CraftOrder("q", eRealm.Albion, CraftOrderSource.Quartermaster, 0, "quartermaster:1", "Quartermaster of Albion",
                7, eCraftingSkill.WeaponCrafting, "sword", "sword", 200, 0, 1, 300, false, CraftOrderState.Open, string.Empty,
                now - TimeSpan.FromMinutes(20), now + TimeSpan.FromHours(2));
            DateTime soon = now + TimeSpan.FromMinutes(30), late = now + TimeSpan.FromHours(2);

            Assert.That(QuartermasterCommissions.WithdrawReason(order, true, false, soon, true, now), Is.Null, "it can still come");
            Assert.That(QuartermasterCommissions.WithdrawReason(order, false, false, DateTime.MaxValue, false, now), Is.EqualTo("the crafter logged out"));
            Assert.That(QuartermasterCommissions.WithdrawReason(order, true, true, DateTime.MaxValue, true, now), Is.EqualTo("the crafter is in a group"));
            Assert.That(QuartermasterCommissions.WithdrawReason(order, true, false, late, true, now), Is.EqualTo("the crafter has no craft break soon"));
            Assert.That(QuartermasterCommissions.WithdrawReason(order, true, false, soon, false, now), Is.EqualTo("the crafter is logging out"));
            Assert.That(QuartermasterCommissions.WithdrawReason(order, true, false, DateTime.MinValue, false, now), Is.Null,
                "on its craft trip now: it is making it");
            Assert.That(QuartermasterCommissions.WithdrawReason(order with { CreatedUtc = now - TimeSpan.FromMinutes(5) }, false, false,
                DateTime.MaxValue, false, now), Is.Null, "never in its first ten minutes");
            Assert.That(QuartermasterCommissions.WithdrawReason(order with { State = CraftOrderState.Made }, false, false, DateTime.MaxValue,
                false, now), Is.Null, "a made piece waits for its hand-over");
            Assert.That(QuartermasterCommissions.WithdrawReason(order with { Source = CraftOrderSource.Bot }, false, false, DateTime.MaxValue,
                false, now), Is.Null, "a bot's paid order keeps DR's rules");
        }

        [Test]
        public void AWithdrawnCommissionIsPostedAgainToACrafterThatCan()
        {
            _catalog.Recipes.Add(MakeRecipe(200, price: 30));
            TestBot gone = Bot("Gone", "1|210;6|300", crafter: true);
            _purses[gone.DatabaseID] = 10_000;
            DateTime now = DateTime.UtcNow;
            CraftOrder order = QuartermasterCommissions.Post(gone, now);
            _live.Remove(gone.DatabaseID); // logged out
            TestBot here = Bot("Here", "1|210;6|300", crafter: true);
            _purses[here.DatabaseID] = 10_000;

            Assert.That(QuartermasterCommissions.WithdrawUnavailable(now + TimeSpan.FromMinutes(5)), Is.Zero, "the grace");
            Assert.That(QuartermasterCommissions.WithdrawUnavailable(now + TimeSpan.FromMinutes(11)), Is.EqualTo(1));
            Assert.That(AutonomousBotCraftOrders.All().Any(each => each.OrderId == order.OrderId), Is.False);
            Assert.That(_moves, Is.Empty, "nobody paid, nothing moves");

            Assert.That(QuartermasterCommissions.PostForRealm(eRealm.Albion, 0, now + TimeSpan.FromMinutes(11)), Is.EqualTo(1),
                "the realm has its order back to post");
            Assert.That(AutonomousBotCraftOrders.All().Single().CrafterBotId, Is.EqualTo(here.DatabaseID));
            string line = QuartermasterCommissions.HourLines(DateTime.UtcNow, reset: false).Single();
            Assert.That(line, Does.Contain("posted=2 delivered=0 expired=0"));
            Assert.That(line, Does.EndWith("withdrawn=1"));
        }

        [Test]
        public void ACommissionIsHandedOverAtTheStationAsSoonAsItIsMade()
        {
            CraftRecipeInfo recipe = MakeRecipe(200, price: 30);
            _catalog.Recipes.Add(recipe);
            TestBot smith = Bot("Smith", "1|210;6|300", crafter: true);
            TestBot buyer = Bot("Buyer");
            _purses[smith.DatabaseID] = 10_000;
            _purses[buyer.DatabaseID] = 10_000;
            CraftOrder commission = QuartermasterCommissions.Post(smith, DateTime.UtcNow);
            CraftOrder paid = AutonomousBotCraftOrders.Place(buyer, smith, recipe);
            DbInventoryItem forRealm = OwnPiece(smith, "guard's sword");
            DbInventoryItem forBuyer = OwnPiece(smith, "buyer's sword");
            Assert.That(AutonomousBotCraftOrders.OnMade(smith, paid.OrderId, forBuyer), Is.True);
            Assert.That(AutonomousBotCraftOrders.OnMade(smith, commission.OrderId, forRealm), Is.True);
            _moves.Clear();

            AutonomousBotCrafting.FinishSession(smith, new BotCraftingRules.Session(recipe, recipe.Skill, 1, commission.OrderId), [forRealm], 1,
                "Finished at the station; made 1");

            Assert.That(smith.Inventory.AllItems, Does.Not.Contain(forRealm), "the guards have it");
            Assert.That(_moves, Is.EqualTo(new[] { (smith.DatabaseID, commission.EscrowCopper, EconomyFlowKind.Commission) }));
            Assert.That(AutonomousBotCraftOrders.All().Single().OrderId, Is.EqualTo(paid.OrderId),
                "a bot's order still waits for the end of the break, where its buyer is met");
            Assert.That(smith.Inventory.AllItems, Does.Contain(forBuyer));
        }

        #endregion

        #region Milestone sets (True165)

        [Test]
        public void MilestonesAreTwentyThirtyAndForty()
        {
            Assert.That(MilestoneCraftSets.MilestoneFor(19), Is.Zero);
            Assert.That(MilestoneCraftSets.MilestoneFor(20), Is.EqualTo(20));
            Assert.That(MilestoneCraftSets.MilestoneFor(29), Is.EqualTo(20));
            Assert.That(MilestoneCraftSets.MilestoneFor(35), Is.EqualTo(30));
            Assert.That(MilestoneCraftSets.MilestoneFor(50), Is.EqualTo(40));
            Assert.That(MilestoneCraftSets.FitsMilestone(18, 20, 20), Is.True);
            Assert.That(MilestoneCraftSets.FitsMilestone(17, 20, 20), Is.False);
            Assert.That(MilestoneCraftSets.FitsMilestone(21, 20, 22), Is.True);
            Assert.That(MilestoneCraftSets.FitsMilestone(21, 20, 20), Is.False, "never above the buyer");
        }

        [Test]
        public void OnlyAWholeSetTheBuyerCanAffordIsOrdered()
        {
            CraftRecipeInfo recipe = MakeRecipe(300, price: 1_000);
            var plan = Enumerable.Range(0, 6).Select(_ => new MilestoneCraftSets.Piece(recipe, eInventorySlot.TorsoArmor, 1_750)).ToArray();
            Assert.That(MilestoneCraftSets.CanAfford(10_500 + 400, 400, plan), Is.True);
            Assert.That(MilestoneCraftSets.CanAfford(10_499 + 400, 400, plan), Is.False, "the reserve stays");
        }

        [Test]
        public void ThePlanTakesTheHeaviestArmourPerSlotNearestTheMilestone()
        {
            DbItemTemplate Armor(string id, int level, int absorb, int af)
            {
                DbItemTemplate template = Item(id);
                template.Level = level;
                template.Object_Type = (int)eObjectType.Chain;
                template.SPD_ABS = absorb;
                template.DPS_AF = af;
                return template;
            }
            CraftRecipeInfo R(DbItemTemplate product, int level) =>
                Info(new Recipe(product, [new Ingredient(1, Item("ore"))], eCraftingSkill.ArmorCrafting, level, false));
            var studded = (R(Armor("studded", 20, 19, 40), 300), Templates["studded"], eInventorySlot.TorsoArmor);
            var chain = (R(Armor("chain", 19, 27, 38), 320), Templates["chain"], eInventorySlot.TorsoArmor);
            var chainLow = (R(Armor("chain low", 18, 27, 36), 300), Templates["chain low"], eInventorySlot.TorsoArmor);
            var helm = (R(Armor("helm", 20, 27, 40), 300), Templates["helm"], eInventorySlot.HeadArmor);

            MilestoneCraftSets.Piece[] plan = MilestoneCraftSets.PickPlan([studded, chain, chainLow, helm], 20);

            Assert.That(plan.Select(piece => piece.Slot), Is.EqualTo(new[] { eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor }));
            Assert.That(plan[1].Recipe.ProductId, Is.EqualTo("chain"), "the heaviest armour, then the level nearest the milestone");
            Assert.That(plan[1].Price, Is.EqualTo(AutonomousBotCraftOrders.Quote(plan[1].Recipe)), "the board's price (DR), not True165's x 1.75");
        }

        [Test]
        public void AMilestoneBuyerWithNoCrafterOrdersNothing()
        {
            TestBot buyer = Bot("Buyer", level: 20);
            _purses[buyer.DatabaseID] = 1_000_000;
            Assert.That(MilestoneCraftSets.TryPost(buyer), Is.Zero);
            Assert.That(_moves, Is.Empty);
            Assert.That(MilestoneCraftSets.TryPost(Bot("Young", level: 19)), Is.Zero, "below the first milestone");
        }

        #endregion
    }
}
