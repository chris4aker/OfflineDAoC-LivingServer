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
    /// return of unsold crafted work to its crafter. A commission is posted, as <see cref="QuartermasterCommissions.Candidates"/>
    /// posts it, only to an ungrouped crafter of skill 50 and up whose next craft break is within
    /// <see cref="AutonomousBotCraftOrders.BreakWindow"/> (the end of its hunting task, or now while its break is a craft
    /// trip), longest unasked first, then the least busy. At its next ungrouped task break the crafter makes a craft trip
    /// that works its open commissions oldest first, one session each, as the server's trip does
    /// (<see cref="AutonomousBotCraftOrders.PlanOrder"/> first in every session, at most
    /// <see cref="BotCraftingRules.SessionsPerTrip"/> sessions, none started after <see cref="BotCraftingRules.TripWorkMilliseconds"/>;
    /// a failed attempt costs nothing and is tried again at the station); the crafter pays the materials, and at the end of
    /// that break the piece is handed over: the realm pays (a Commission faucet) and the piece leaves the world. The wait,
    /// posting to the end of that break, is the gate "median order wait under the limit". Runs with bot_crafters on, or
    /// with a tax.
    /// </summary>
    /// <remarks>
    /// Simplifications: the bots' own orders between bots (bot_craft_orders, DividedRealms) and the milestone sets are not
    /// rehearsed (they move coin between bots only, a transfer the ledger nets out; the in-game check is theirs); a
    /// commission posted during a craft trip waits for the next break (the server may still make it in that trip, so the
    /// rehearsal's wait is no shorter than the game's); travel to the station is not timed; the realm pays at the break's
    /// start, while the wait is counted to its end; the flood cap is not applied to the rehearsal's own listing pass.
    /// </remarks>
    public sealed class WP5Market : RehearsalModule
    {
        public const string ModuleId = "WP5.Market";

        private sealed record Commission(CraftRecipeInfo Recipe, long Pay, int PostedMinute, int Seconds);

        /// <summary>The town activity of a break that is a craft trip (the stand-in's session, or this module's commissions).</summary>
        public const string CommissionActivity = "commission";

        private sealed class Stats
        {
            public int Posted, Delivered, Expired, NoCrafter, Reclaimed, Trips;
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
                                             Open(agent).Count < AutonomousBotCraftOrders.MaximumOpenPerCrafter &&
                                             CraftBreakSoon(world, agent))
                             .OrderBy(agent => _lastAsked.GetValueOrDefault(agent.Id, int.MinValue))
                             .ThenBy(agent => Open(agent).Count)
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
                    Open(crafter).Add(new Commission(work, QuartermasterCommissions.Pay(work.MaterialCostCopper, seconds, Skill(crafter)), world.Minute,
                        seconds));
                    _lastAsked[crafter.Id] = world.Minute;
                    _credit[slot] -= 1;
                    _stats.Posted++;
                    world.OrdersPosted++;
                }
                if (_credit[slot] >= 1)
                    _stats.NoCrafter++;
            }
        }

        /// <summary>
        /// AutonomousBotCraftOrders.NextCraftBreak within its BreakWindow: never while grouped (a grouped bot gets no
        /// break); the end of the hunting task; now while the break is a craft trip; never on a break that is not one.
        /// </summary>
        public static bool CraftBreakSoon(RehearsalWorld world, RehearsalAgent agent)
        {
            if (agent.Grouped)
                return false;
            if (agent.Now == RehearsalAgent.Phase.Town)
                return agent.TownActivity is "craft" or CommissionActivity;
            return agent.PhaseEndsMinute - world.Minute <= AutonomousBotCraftOrders.BreakWindow.TotalMinutes;
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
            // A grouped bot gets no break and so no craft trip (TryBeginBetweenTaskServices); its orders wait or lapse.
            if (open.Count == 0 || agent.Grouped)
                return;

            // The craft trip (TripActivity: a crafter with open work always goes): each session first takes the oldest open
            // commission it can make and pay for (PlanOrder) and works it until the piece is made.
            Random random = world.RandomFor(agent.Realm);
            var made = new List<Commission>();
            int sessions = 0;
            long tripSeconds = 0;
            while (BotCraftingRules.AnotherSession(sessions, tripSeconds * 1000))
            {
                Commission next = open.FirstOrDefault(order => order.Recipe.MaterialCostCopper <= agent.Purse &&
                                                               CraftRules.ChanceToMake(Skill(agent), order.Recipe.Level) > 0);
                if (next == null)
                    break;
                sessions++;
                int chance = CraftRules.ChanceToMake(Skill(agent), next.Recipe.Level);
                int attempts = 0;
                bool success;
                do
                {
                    attempts++;
                    tripSeconds += Math.Max(1, next.Seconds);
                    success = chance > random.Next(100); // Util.Chance, as AutonomousBotCrafting.Attempt draws it
                } while (!success && attempts < MaximumAttempts);
                if (!success || !world.Pay(agent, next.Recipe.MaterialCostCopper, EconomyFlowKind.CraftMaterials))
                    break;
                open.Remove(next);
                made.Add(next);
                _stats.Materials += next.Recipe.MaterialCostCopper;
            }
            if (made.Count == 0)
                return;
            _stats.Trips++;
            // The break lasts at least the trip's work; the pieces are handed over at its end (TaskBreakPoint.End).
            agent.TownMinutes = Math.Max(agent.TownMinutes, (int)Math.Ceiling(tripSeconds / 60.0));
            if (agent.TownActivity == "idle")
                agent.TownActivity = CommissionActivity;
            int handedOver = world.Minute + agent.TownMinutes;
            foreach (Commission piece in made)
            {
                if (handedOver - piece.PostedMinute > lifetime)
                {
                    // Lapsed before the break ended: nobody pays, and the piece stays with its maker (not modelled further).
                    _stats.Expired++;
                    world.OrdersNeverCollected++;
                    continue;
                }
                world.Earn(agent, piece.Pay, EconomyFlowKind.Commission);
                world.OrderWaitMinutes.Add(handedOver - piece.PostedMinute);
                _stats.Delivered++;
                _stats.Paid += piece.Pay;
                if (world.EconomyDiagnosticsLines)
                    world.Log(EconomyLog.Line(EconomyLog.EconCommission, ("hour", world.NowUtc), ("realm", agent.Realm), ("bot", agent.Name),
                        ("item", piece.Recipe.ProductName), ("recipeLevel", piece.Recipe.Level), ("pay", piece.Pay), ("source", "rehearsal")));
            }
        }

        /// <summary>A guard only: commissions are sized to be made at 84% or better, so a trip never comes near it.</summary>
        private const int MaximumAttempts = 100;

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
            report.Metric("wp5.commissionTrips", _stats.Trips);
            report.Metric("wp5.commissionPaidCopper", _stats.Paid);
            report.Metric("wp5.commissionMaterialsCopper", _stats.Materials);
            report.Metric("wp5.minutesWithNoAbleCrafter", _stats.NoCrafter);
            report.Metric("wp5.reclaimed", _stats.Reclaimed);
        }
    }
}
