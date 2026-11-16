using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal.Adapters
{
    /// <summary>
    /// Crafting 2.0 WP6 in the economy rehearsal: the package's pure code against the in-memory world. Runs only in
    /// scenarios with <c>bot_crafters</c> on.
    /// <list type="bullet">
    /// <item>The world's upgrade rule becomes the template-aware set score (<see cref="BotGearSet"/>: True165's caps, the
    /// quality term at 50), as AutonomousBotEconomy.TryGetEquipmentUpgrade decides with the switch on. A rehearsal agent
    /// has no class, so every typed bonus weighs 3 (the rehearsal's items carry untyped bonuses anyway).</item>
    /// <item>At each task break the gear check (<see cref="BotGearPipeline.FindGaps"/>) and, for an ungrouped agent, the
    /// gap's purchase from its realm's listings within 80% of its purse, ranked as the server ranks it.</item>
    /// <item>A gap with nothing listed is a want-to-buy the agent would post: counted, with whether DividedRealms' quote
    /// for the nearest fixture recipe fits half its purse. No order is placed (the board is WP5's; its adapter places
    /// them).</item>
    /// <item>The no-undress check on the set: an agent's set score never falls between its task breaks (the score only
    /// rises with level for the same gear), reported as wp6.setScoreDrops, which must be 0.</item>
    /// </list>
    /// The rehearsal's listing prices stay WP0's index (as in the server, WP6 does not change listing prices), and the
    /// purchase skips the server's ItemValue165 bid cap, which reads the merchant stock from the database.
    /// </summary>
    public sealed class WP6Demand : RehearsalModule
    {
        public const string ModuleId = "WP6.Demand";
        private const string ScoreKey = "wp6.score";

        private long _checks, _withGaps, _empty, _template, _below, _bought, _templateBought, _boughtCopper, _wtbWanted,
            _wtbAffordable, _scoreDrops;
        private Dictionary<(eRealm, int, int), RehearsalRecipe[]> _recipes;

        public override string Id => ModuleId;
        public override string Package => "WP6";
        public override string Describe =>
            "WP6 demand: template-aware set score as the upgrade rule, the gear check at task breaks, gap purchases on the Exchange, want-to-buy demand counted";
        public override IReadOnlyCollection<string> ReadsSettings => ["bot_crafters", "bot_craft_orders"];

        public override bool AppliesTo(RehearsalScenario scenario) => scenario.Flag("bot_crafters");

        /// <summary>The agent's worn gear as a slot-indexed loadout.</summary>
        public static DbInventoryItem[] Loadout(RehearsalAgent agent)
        {
            var worn = new DbInventoryItem[BotGearSet.LoadoutLength];
            for (int i = 0; i < RehearsalAgent.Slots.Length; i++)
                worn[(int)RehearsalAgent.Slots[i]] = agent.Worn[i];
            return worn;
        }

        public static BotGearSet.Context ContextFor(RehearsalAgent agent) => new(agent.Level, _ => BotGearValue165.BaseWeight);

        /// <summary>The server's rule (TryGetEquipmentUpgrade with bot_crafters on) on the set score.</summary>
        public static bool IsUpgrade(RehearsalAgent agent, DbInventoryItem worn, DbInventoryItem candidate)
        {
            if (candidate == null)
                return false;
            var slot = (eInventorySlot)candidate.Item_Type;
            if (!BotGearSet.IsScoredSlot(slot))
                return false;
            long gain = BotGearSet.Delta(ContextFor(agent), Loadout(agent), slot, candidate);
            return worn == null ? gain > 0 : gain > 8;
        }

        public override void Attach(RehearsalWorld world) => world.IsUpgrade = IsUpgrade;

        public override void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent)
        {
            BotGearSet.Context context = ContextFor(agent);
            long score = BotGearSet.Score(context, Loadout(agent));
            if (agent.State.TryGetValue(ScoreKey, out object last) && score < (long)last)
            {
                _scoreDrops++;
                world.Log(EconomyLog.Line(EconomyLog.EconUndressGuard, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)),
                    ("bot", agent.Name), ("event", "set"), ("before", (long)last), ("after", score), ("source", "rehearsal")));
            }

            _checks++;
            BotGearPipeline.Gap[] gaps = BotGearPipeline.FindGaps(agent.Level, Loadout(agent));
            if (gaps.Length > 0)
            {
                _withGaps++;
                foreach (BotGearPipeline.Gap gap in gaps)
                {
                    if (gap.Reason == BotGearPipeline.GapReason.Empty) _empty++;
                    else if (gap.Reason == BotGearPipeline.GapReason.Template) _template++;
                    else _below++;
                }
                if (!agent.Grouped && !TryBuyGapFill(world, agent, gaps))
                    WantToBuy(world, agent, gaps);
            }
            agent.State[ScoreKey] = BotGearSet.Score(context, Loadout(agent));
        }

        private bool TryBuyGapFill(RehearsalWorld world, RehearsalAgent agent, BotGearPipeline.Gap[] gaps)
        {
            long budget = (long)(agent.Purse * BotGearPipeline.ExchangeBudgetShare);
            BotGearSet.Context context = ContextFor(agent);
            DbInventoryItem[] worn = Loadout(agent);
            RehearsalListing best = null;
            (bool Template, bool Crafted, long Gain, long Price) bestKey = default;
            foreach (RehearsalListing listing in world.Listings(agent.Realm))
            {
                if (listing.Gone || listing.Seller == agent || listing.Price > budget || listing.Item.Level > agent.Level)
                    continue;
                var slot = (eInventorySlot)listing.Item.Item_Type;
                int index = Array.FindIndex(gaps, gap => gap.Slot == slot);
                if (index < 0)
                    continue;
                bool templateGap = gaps[index].Reason == BotGearPipeline.GapReason.Template;
                if (templateGap && !BotGearSet.IsTemplatePiece(listing.Item))
                    continue;
                long gain = BotGearSet.Delta(context, worn, slot, listing.Item);
                if (gain <= 0 || worn[(int)slot] != null && gain <= 8)
                    continue;
                var key = (templateGap && BotGearSet.IsTemplatePiece(listing.Item), listing.Item.IsCrafted, gain, listing.Price);
                if (best == null || Better(key, bestKey))
                {
                    best = listing;
                    bestKey = key;
                }
            }
            if (best == null || !world.TryBuy(agent, best))
                return false;
            world.Equip(agent, RehearsalAgent.SlotIndexOf(best.Item.Item_Type), best.Item);
            _bought++;
            _boughtCopper += best.Price;
            world.Count("wp6_gap_buys");
            if (agent.Level >= BotGearSet.TemplateLevel && BotGearSet.IsTemplatePiece(best.Item))
            {
                _templateBought++;
                world.Log(EconomyLog.Line(EconomyLog.EconTemplateBuy, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)),
                    ("bot", agent.Name), ("level", agent.Level), ("slot", (eInventorySlot)best.Item.Item_Type), ("item", best.Item.Name),
                    ("quality", best.Item.Quality), ("copper", best.Price), ("source", "rehearsal")));
            }
            return true;
        }

        /// <summary>BotGearPipeline.Best's order: the template piece, then crafted, then the bigger rise, then the cheaper.</summary>
        private static bool Better((bool Template, bool Crafted, long Gain, long Price) a, (bool Template, bool Crafted, long Gain, long Price) b) =>
            a.Template != b.Template ? a.Template : a.Crafted != b.Crafted ? a.Crafted : a.Gain != b.Gain ? a.Gain > b.Gain : a.Price < b.Price;

        private void WantToBuy(RehearsalWorld world, RehearsalAgent agent, BotGearPipeline.Gap[] gaps)
        {
            _wtbWanted++;
            _recipes ??= world.Recipes.All
                .Where(recipe => recipe.Craftable && !recipe.Info.Stackable)
                .SelectMany(recipe => (recipe.Info.Realm == 0 ? new[] { 1, 2, 3 } : new[] { recipe.Info.Realm })
                    .Select(realm => (Key: ((eRealm)realm, recipe.Info.ObjectType, recipe.Info.ItemType), Recipe: recipe)))
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Recipe)
                    .OrderBy(recipe => recipe.Info.ProductLevel).ThenBy(recipe => recipe.Info.RecipeId, StringComparer.Ordinal).ToArray());
            foreach (BotGearPipeline.Gap gap in gaps)
            {
                if (!_recipes.TryGetValue((agent.Realm, agent.ArmorObjectType, (int)gap.Slot), out RehearsalRecipe[] recipes))
                    continue;
                RehearsalRecipe nearest = recipes.LastOrDefault(recipe => recipe.Info.ProductLevel <= agent.Level &&
                    recipe.Info.ProductLevel + BotGearPipeline.OrderLevelWindow >= agent.Level);
                if (nearest == null)
                    continue;
                if (BotGearPipeline.CanAffordOrder(BotGearPipeline.Quote(nearest.Info.MaterialCostCopper), agent.Purse))
                    _wtbAffordable++;
                return;
            }
        }

        public override void OnHour(RehearsalWorld world)
        {
            foreach (eRealm realm in world.Realms)
            {
                RehearsalAgent[] agents = world.InRealm(realm).ToArray();
                int worn = agents.Sum(agent => agent.Worn.Count(item => item != null));
                int crafted = agents.Sum(agent => agent.Worn.Count(item => item?.IsCrafted == true));
                world.HourMetric(realm, "craftedWornShare", worn == 0 ? 0 : Math.Round(crafted * 100.0 / worn, 2));
                world.HourMetric(realm, "level50Templates", agents.Count(agent => agent.Level >= 50 &&
                    BotGearSet.TemplatePieces(Loadout(agent)) == BotGearSet.TemplateSlots.Length));
            }
        }

        public override void Finish(RehearsalWorld world, RehearsalReport report)
        {
            report.Metric("wp6.checks", _checks);
            report.Metric("wp6.withGaps", _withGaps);
            report.Metric("wp6.gapsEmpty", _empty);
            report.Metric("wp6.gapsTemplate", _template);
            report.Metric("wp6.gapsBelowBand", _below);
            report.Metric("wp6.gapBuys", _bought);
            report.Metric("wp6.templateBuys", _templateBought);
            report.Metric("wp6.gapBuyCopper", _boughtCopper);
            report.Metric("wp6.wtbWanted", _wtbWanted);
            report.Metric("wp6.wtbAffordable", _wtbAffordable);
            report.Metric("wp6.setScoreDrops", _scoreDrops);
            int worn = world.Agents.Sum(agent => agent.Worn.Count(item => item != null));
            int crafted = world.Agents.Sum(agent => agent.Worn.Count(item => item?.IsCrafted == true));
            report.Metric("wp6.craftedWornPercent", worn == 0 ? 0 : Math.Round(crafted * 100.0 / worn, 2));
        }
    }
}
