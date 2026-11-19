using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal.Adapters
{
    /// <summary>
    /// Crafting 2.0 WP8's part of the rehearsal, on WP8's pure code: a real player in each realm (the rehearsal has no
    /// players, so a stand-in that never runs short of coin) commissions crafter agents and buys their stock. At a
    /// crafter's natural task break the player now and then asks for a piece at the crafter's level band
    /// (<see cref="CrafterQuotes.Parse"/>, <see cref="CrafterQuotes.Makeable"/>, <see cref="CrafterQuotes.Best"/>), priced
    /// as the crafter quotes it (<see cref="CrafterQuotes.Price"/>, the customer's standing from <see cref="CrafterCustomers"/>),
    /// paid up front or on delivery by <see cref="CrafterCustomers.WantsUpFront"/>; the piece is made at the crafter's next
    /// break (it pays the materials, CraftMaterials) and handed over, the player's coin reaching the crafter as CraftOrder
    /// (from escrow) or PlayerTrade (in the trade window). A crafted piece in a crafter's bag is sometimes bought outright
    /// (PlayerTrade). The player's purse is outside the bots' money, so each payment is coin into the bots' supply: every
    /// move goes through the world's Earn and Pay, and the gate "flows equal the change in supply" holds.
    /// </summary>
    /// <remarks>
    /// Simplifications: one stand-in player per realm, at most one open order per crafter, no haggle (the quote is paid),
    /// no guild crafters (the rehearsal has no guilds), every order is collected (no stiffing).
    /// </remarks>
    public sealed class WP8Commerce : RehearsalModule
    {
        public const string ModuleId = "WP8.Commerce";
        /// <summary>Share of a crafter's task breaks at which the realm's player asks it for a piece (a player is rarely on).</summary>
        public const double AskChance = 0.03;
        /// <summary>Share of breaks at which the player buys a crafted piece the crafter holds.</summary>
        public const double StockChance = 0.02;
        /// <summary>The full bot trade: share of any gamebot's task breaks at which the player sells it a piece for a gap (empty, or a band behind).</summary>
        public const double SellToBotChance = 0.02;
        /// <summary>Share of any gamebot's task breaks at which the player buys a piece of loot from its bag.</summary>
        public const double BuyFromBagChance = 0.02;

        private sealed record Pending(CraftRecipeInfo Recipe, long Price, bool UpFront, int PlacedMinute);

        private readonly Dictionary<long, Pending> _open = new();
        private readonly Dictionary<long, (int Deals, long Spent)> _customer = new();
        private int _placed, _delivered, _stock, _cannot, _upFront, _soldToBots, _boughtFromBags, _capped;
        private long _paid, _materials, _paidToPlayer, _paidForLoot;
        private bool _on;

        public override string Id => ModuleId;
        public override string Package => "WP8";
        public override string Describe =>
            "WP8 commerce: a stand-in player per realm commissions crafter agents by their real skill (quote, standing discount, up front or on delivery) and buys their crafted stock; in the trade window it sells any agent a piece for a gap (an empty slot or one a band behind) (index bid within the purse share) and buys loot from its bag (index ask)";
        public override IReadOnlyCollection<string> ReadsSettings => ["bot_crafter_commerce"];

        public override bool AppliesTo(RehearsalScenario scenario) =>
            scenario.Flag("bot_crafters") && scenario.Flag("bot_crafter_commerce");

        public override void Attach(RehearsalWorld world) => _on = true;

        public override void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent)
        {
            if (!_on)
                return;
            Random random = world.RandomFor(agent.Realm);
            // The full bot trade (any gamebot): the player sells it a piece it lacks, or buys loot from its bag.
            TradeWindow(world, agent, random);
            if (!agent.IsCrafter || string.IsNullOrEmpty(agent.CraftingSkills))
                return;
            if (_open.TryGetValue(agent.Id, out Pending pending))
            {
                Deliver(world, agent, pending, random);
                return;
            }
            DbInventoryItem stock = agent.Bag.FirstOrDefault(item => item.IsCrafted && !item.IsStackable && item.Creator == agent.Name);
            if (stock != null && random.NextDouble() < StockChance)
            {
                long ask = Math.Max(1, world.PriceIndex.Ask(stock, 0));
                agent.Bag.Remove(stock);
                world.Earn(agent, ask, EconomyFlowKind.PlayerTrade);
                _stock++;
                _paid += ask;
                Note(agent.Id, ask);
                return;
            }
            if (random.NextDouble() >= AskChance)
                return;
            eCraftingSkill primary = BotCraftingSkills.Primary(agent.CraftingSkills);
            IReadOnlyList<CraftRecipeInfo> makeable = CrafterQuotes.Makeable(world.Recipes.For(primary, agent.Realm).Select(r => r.Info),
                agent.CraftingSkills, agent.Realm, allowNoRealm: true);
            (int _, int highest) = CrafterQuotes.Range(makeable);
            CrafterQuotes.Request request = CrafterQuotes.Parse($"can you make something lvl {Math.Max(1, highest - random.Next(4))}");
            CraftRecipeInfo recipe = CrafterQuotes.Best(request with { Craft = primary }, makeable);
            if (recipe == null)
            {
                _cannot++;
                return;
            }
            (int deals, long spent) = _customer.GetValueOrDefault(agent.Id);
            CrafterCustomers.Standing standing = CrafterCustomers.StandingOf(deals, 0, false, false);
            CrafterQuotes.Offer offer = CrafterQuotes.Price(recipe, agent.CraftingSkills, CrafterCustomers.DiscountPercent(standing, spent), false);
            bool upFront = CrafterCustomers.WantsUpFront(standing, agent.Personality.Traits.Patience, false);
            _open[agent.Id] = new Pending(recipe, offer.Price, upFront, world.Minute);
            _placed++;
            if (upFront)
                _upFront++;
            world.OrdersPosted++;
            if (world.EconomyDiagnosticsLines)
                world.Log(EconomyLog.Line(EconomyLog.EconPlayerOrder, ("hour", world.NowUtc), ("realm", agent.Realm), ("crafter", agent.Name),
                    ("item", recipe.ProductName), ("copper", offer.Price), ("pay", upFront ? "upfront" : "delivery"), ("source", "rehearsal")));
        }

        private void Deliver(RehearsalWorld world, RehearsalAgent agent, Pending pending, Random random)
        {
            int skill = BotCraftingSkills.Value(agent.CraftingSkills, pending.Recipe.Skill);
            if (random.Next(100) >= CraftRules.ChanceToMake(skill, pending.Recipe.Level))
                return; // a failure costs nothing (today's rule); tried again at the next break
            if (!world.Pay(agent, pending.Recipe.MaterialCostCopper, EconomyFlowKind.CraftMaterials))
                return;
            _open.Remove(agent.Id);
            // Up front: the player's coin waited in escrow (outside the bots' money) and reaches the crafter now.
            world.Earn(agent, pending.Price, pending.UpFront ? EconomyFlowKind.CraftOrder : EconomyFlowKind.PlayerTrade);
            world.OrderWaitMinutes.Add(world.Minute - pending.PlacedMinute);
            _delivered++;
            _paid += pending.Price;
            _materials += pending.Recipe.MaterialCostCopper;
            Note(agent.Id, pending.Price);
        }

        /// <summary>
        /// The trade window on the pure terms (<see cref="BotTradeTerms.Evaluate"/>): a piece for an empty slot sold to the bot at
        /// the index's eager bid within its purse share (80%), or a loot piece of its bag bought at the index's ask. Both are
        /// PlayerTrade: the player's coin is outside the bots' money here.
        /// </summary>
        private void TradeWindow(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            // A gap, as WP6's gear check sees it: an empty slot, or a piece a full level band behind the bot.
            int empty = Array.FindIndex(agent.Worn, item => item == null || BotGearPipeline.IsBelowBand(agent.Level, item.Level));
            if (empty >= 0 && random.NextDouble() < SellToBotChance)
            {
                DbInventoryItem piece = RehearsalItems.Merchant(agent, empty, Math.Max(1, agent.Level - random.Next(3)), 1);
                long bid = Math.Max(0, world.PriceIndex.Bid(piece, ItemValue165.MoodHigh));
                BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new BotTradeTerms.Offer(agent.Name, [],
                    [new BotTradeTerms.PlayerPiece(piece.ObjectId, piece.Name, piece.Quality, piece.Level, bid, BotTradeTerms.Verdict.Wanted)],
                    0, BotTradeTerms.PurseCapOf(agent.Purse, BotTrades.PurseShare)));
                if (quote.Accepts && quote.BotPays > 0 && world.Pay(agent, quote.BotPays, EconomyFlowKind.PlayerTrade))
                {
                    agent.Worn[empty] = piece;
                    _soldToBots++;
                    _paidToPlayer += quote.BotPays;
                    if (quote.CappedByPurse)
                        _capped++;
                }
                return;
            }
            DbInventoryItem loot = agent.Bag.FirstOrDefault(item => !item.IsStackable && !item.IsCrafted);
            if (loot != null && random.NextDouble() < BuyFromBagChance)
            {
                long ask = Math.Max(1, world.PriceIndex.Ask(loot, 0));
                BotTradeTerms.Quote quote = BotTradeTerms.Evaluate(new BotTradeTerms.Offer(agent.Name,
                    [new BotTradeTerms.BotPiece(loot.ObjectId, loot.Name, loot.Quality, loot.Level, ask, BotTradeTerms.Source.Bag)], [], ask, 0));
                if (!quote.Accepts)
                    return;
                agent.Bag.Remove(loot);
                world.Earn(agent, quote.Owed, EconomyFlowKind.PlayerTrade);
                _boughtFromBags++;
                _paidForLoot += quote.Owed;
            }
        }

        private void Note(long crafterId, long copper)
        {
            (int deals, long spent) = _customer.GetValueOrDefault(crafterId);
            _customer[crafterId] = (deals + 1, spent + copper);
        }

        public override void OnHour(RehearsalWorld world)
        {
            foreach (eRealm realm in world.Realms)
                world.HourMetric(realm, "wp8.playerOrdersOpen", world.InRealm(realm).Count(agent => _open.ContainsKey(agent.Id)));
        }

        public override void Finish(RehearsalWorld world, RehearsalReport report)
        {
            report.Metric("wp8.playerOrdersPlaced", _placed);
            report.Metric("wp8.playerOrdersUpFront", _upFront);
            report.Metric("wp8.playerOrdersDelivered", _delivered);
            report.Metric("wp8.playerStockBought", _stock);
            report.Metric("wp8.playerPaidCopper", _paid);
            report.Metric("wp8.crafterMaterialsCopper", _materials);
            report.Metric("wp8.requestsNoRecipe", _cannot);
            report.Metric("wp8.tradeSoldToBots", _soldToBots);
            report.Metric("wp8.tradeSoldCappedByPurse", _capped);
            report.Metric("wp8.tradeBotsPaidCopper", _paidToPlayer);
            report.Metric("wp8.tradeBoughtFromBags", _boughtFromBags);
            report.Metric("wp8.tradePlayerPaidForLootCopper", _paidForLoot);
        }
    }
}
