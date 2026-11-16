using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal.Adapters
{
    /// <summary>
    /// Crafting 2.0 WP5's part of the rehearsal, on WP5's pure code: the price index (<see cref="MarketPriceIndex"/>, a
    /// fresh one per run, as the world's index: listings are asked at its Ask and checked against its Bid), the Exchange
    /// tax (<see cref="ExchangeTax.ProceedsOf"/> at the scenario's exchange_tax_percent, kept from the seller's proceeds),
    /// the realm quartermaster's commissions (<see cref="QuartermasterCommissions"/>' rate, work sizing and pay) and the
    /// return of unsold crafted work to its crafter. A commission is posted to a crafter of skill 50 and up and made at
    /// one of its natural task breaks: the crafter pays the materials, the realm pays it (a Commission faucet) and the
    /// piece leaves the world; the wait is the gate "median order wait under the limit". Runs with bot_crafters on, or
    /// with a tax.
    /// </summary>
    /// <remarks>
    /// Simplifications: the bots' own orders between bots (bot_craft_orders, DividedRealms) and the milestone sets are not
    /// rehearsed (they move coin between bots only, a transfer the ledger nets out; the in-game check is theirs); a crafter
    /// fills at most one commission a task break; a failed attempt costs nothing (today's rule) and is tried again at the
    /// next break; the flood cap is not applied to the rehearsal's own listing pass.
    /// </remarks>
    public sealed class WP5Market : RehearsalModule
    {
        public const string ModuleId = "WP5.Market";

        private sealed record Commission(CraftRecipeInfo Recipe, long Pay, int PostedMinute);

        private sealed class Stats
        {
            public int Posted, Delivered, Expired, NoCrafter, Reclaimed;
            public long Paid, Materials;
        }

        private readonly Stats _stats = new();
        private readonly double[] _credit = new double[4];
        private readonly Dictionary<long, int> _lastAsked = new();
        private MarketPriceIndex _index;
        private QuartermasterCommissions.Mode _mode;
        private bool _crafters;
        private int _tax;
        private Dictionary<long, RehearsalAgent> _byId;

        public override string Id => ModuleId;
        public override string Package => "WP5";
        public override string Describe =>
            "WP5 market: price index (stock against demand, sale history, material ceiling), Exchange tax, quartermaster commissions sized to skill and paid for time, unsold crafted work reclaimed";
        public override IReadOnlyCollection<string> ReadsSettings => ["realm_commissions", "exchange_tax_percent"];

        public override bool AppliesTo(RehearsalScenario scenario) =>
            scenario.Flag("bot_crafters") || scenario.Int("exchange_tax_percent") != 0;

        public override void Attach(RehearsalWorld world)
        {
            _crafters = world.Scenario.Flag("bot_crafters");
            _mode = QuartermasterCommissions.ParseMode(world.Scenario.Text("realm_commissions"));
            _tax = CraftingEconomyProperties.SanitizeTaxPercent(world.Scenario.Int("exchange_tax_percent"));
            _index = new MarketPriceIndex();
            world.PriceIndex = _index;
            int tax = _tax;
            world.SellerProceeds = (price, _, _, _) => ExchangeTax.ProceedsOf(price, tax);
            if (_crafters)
                world.ReclaimExpired = (item, sellerId) =>
                {
                    _byId ??= world.Agents.ToDictionary(agent => agent.Id);
                    bool own = item != null && item.IsCrafted && !item.IsStackable && _byId.TryGetValue(sellerId, out RehearsalAgent seller) &&
                               seller.IsCrafter && item.Creator == seller.Name;
                    if (own)
                        _stats.Reclaimed++;
                    return own;
                };
        }

        private static List<Commission> Open(RehearsalAgent agent)
        {
            if (!agent.State.TryGetValue(ModuleId, out object state))
                agent.State[ModuleId] = state = new List<Commission>();
            return (List<Commission>)state;
        }

        public override void OnMinute(RehearsalWorld world)
        {
            if (!_crafters || _mode == QuartermasterCommissions.Mode.Off)
                return;
            double perMinute = QuartermasterCommissions.PerHour(_mode, 1.0) / 60.0;
            foreach (eRealm realm in world.Realms)
            {
                int slot = (int)realm;
                _credit[slot] = Math.Min(QuartermasterCommissions.MaxCredit, _credit[slot] + perMinute);
                if (_credit[slot] < 1)
                    continue;
                foreach (RehearsalAgent crafter in world.InRealm(realm)
                             .Where(agent => agent.IsCrafter && Skill(agent) >= QuartermasterCommissions.MinimumSkill &&
                                             Open(agent).Count < AutonomousBotCraftOrders.MaximumOpenPerCrafter)
                             .OrderBy(agent => _lastAsked.GetValueOrDefault(agent.Id, int.MinValue))
                             .ThenBy(agent => agent.Id).ToArray())
                {
                    if (_credit[slot] < 1)
                        break;
                    eCraftingSkill primary = BotCraftingSkills.Primary(crafter.CraftingSkills);
                    CraftRecipeInfo work = QuartermasterCommissions.ChooseWork(world.Recipes.For(primary, realm).Select(r => r.Info),
                        crafter.CraftingSkills, realm, crafter.Purse, null, allowNoRealm: true);
                    if (work == null)
                        continue;
                    var context = new CraftContext(Skill(crafter), work.Level, InCapital: true,
                        CapitalGainBonus: world.Calibration.Crafting.CapitalGainBonus);
                    int seconds = CraftRules.CraftingTimeSeconds(context, work.IngredientCounts);
                    Open(crafter).Add(new Commission(work, QuartermasterCommissions.Pay(work.MaterialCostCopper, seconds, Skill(crafter)), world.Minute));
                    _lastAsked[crafter.Id] = world.Minute;
                    _credit[slot] -= 1;
                    _stats.Posted++;
                    world.OrdersPosted++;
                }
                if (_credit[slot] >= 1)
                    _stats.NoCrafter++;
            }
        }

        private static int Skill(RehearsalAgent agent) =>
            BotCraftingSkills.Value(agent.CraftingSkills, BotCraftingSkills.Primary(agent.CraftingSkills));

        public override void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent)
        {
            if (!_crafters || !agent.IsCrafter || !agent.State.ContainsKey(ModuleId))
                return;
            List<Commission> open = Open(agent);
            int lifetime = (int)AutonomousBotCraftOrders.Lifetime.TotalMinutes;
            for (int i = open.Count - 1; i >= 0; i--)
                if (world.Minute - open[i].PostedMinute > lifetime)
                {
                    open.RemoveAt(i);
                    _stats.Expired++;
                    world.OrdersNeverCollected++;
                }
            Commission next = open.FirstOrDefault();
            if (next == null)
                return;
            Random random = world.RandomFor(agent.Realm);
            if (random.Next(100) >= CraftRules.ChanceToMake(Skill(agent), next.Recipe.Level))
                return; // a failure costs nothing: tried again at the next break
            if (!world.Pay(agent, next.Recipe.MaterialCostCopper, EconomyFlowKind.CraftMaterials))
                return;
            open.Remove(next);
            world.Earn(agent, next.Pay, EconomyFlowKind.Commission);
            world.OrderWaitMinutes.Add(world.Minute - next.PostedMinute);
            _stats.Delivered++;
            _stats.Paid += next.Pay;
            _stats.Materials += next.Recipe.MaterialCostCopper;
            if (world.EconomyDiagnosticsLines)
                world.Log(EconomyLog.Line(EconomyLog.EconCommission, ("hour", world.NowUtc), ("realm", agent.Realm), ("bot", agent.Name),
                    ("item", next.Recipe.ProductName), ("recipeLevel", next.Recipe.Level), ("pay", next.Pay), ("source", "rehearsal")));
        }

        public override void OnHour(RehearsalWorld world)
        {
            var stock = new Dictionary<MarketBand, int>();
            foreach (eRealm realm in world.Realms)
                foreach (RehearsalListing listing in world.Listings(realm).Where(listing => !listing.Gone))
                {
                    stock[listing.Band] = stock.GetValueOrDefault(listing.Band) + 1;
                    MarketBand all = listing.Band with { Realm = eRealm.None };
                    stock[all] = stock.GetValueOrDefault(all) + 1;
                }
            _index.ApplyHour(stock);
            foreach (eRealm realm in world.Realms)
                world.HourMetric(realm, "wp5.commissionsOpen", world.InRealm(realm).Sum(agent =>
                    agent.State.TryGetValue(ModuleId, out object state) ? ((List<Commission>)state).Count : 0));
        }

        public override void Finish(RehearsalWorld world, RehearsalReport report)
        {
            report.Metric("wp5.commissionMode", _mode.ToString().ToLowerInvariant());
            report.Metric("wp5.taxPercent", _tax);
            report.Metric("wp5.commissionsPosted", _stats.Posted);
            report.Metric("wp5.commissionsDelivered", _stats.Delivered);
            report.Metric("wp5.commissionsExpired", _stats.Expired);
            report.Metric("wp5.commissionPaidCopper", _stats.Paid);
            report.Metric("wp5.commissionMaterialsCopper", _stats.Materials);
            report.Metric("wp5.minutesWithNoAbleCrafter", _stats.NoCrafter);
            report.Metric("wp5.reclaimed", _stats.Reclaimed);
        }
    }
}
