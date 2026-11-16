using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal.Adapters
{
    /// <summary>
    /// Crafting 2.0 WP7's part of the rehearsal, on WP7's pure code: gear wear by the player's formula
    /// (<see cref="GameInventoryItem.ConditionLoss"/>, never below 70%), True165's repair errand below 88% and repair of
    /// everything below 90% at the smith's price under the scenario's player_upkeep (<see cref="BotGearWear"/>), True165's
    /// heal-draught rule (<see cref="BotPotions.HealDraughtsWanted"/> with its reserve), paid poison vials for assassins
    /// (<see cref="BotPoisonSupply.RestockCount"/>, <see cref="BotPoisonSupply.VialsPrice"/>), the luxury budget
    /// (<see cref="BotLuxury.Budget"/>) and the money-supply governor (<see cref="SinkGovernor.Ratio"/> and
    /// <see cref="SinkGovernor.Step"/> on the previous hour's row, as the server reads WP2's row). Everything happens at a
    /// natural task break and every coin move is the world's Pay with its kind (Repair, Potion, Poison, Luxury).
    /// </summary>
    /// <remarks>
    /// Assumptions (marked, not measured): 10 weapon swings and 10 hits taken a hunting minute (the hits spread over the six
    /// armor pieces; the cloak and necklace do not wear) at item_condition_loss_chance 5%; a carried heal draught drunk 0.5
    /// times a hunting hour (9 charges a draught); heal draught prices by level from the world's Draught_of_Healing
    /// templates (8,000 / 13,000 / 25,000 copper at 20 / 30 / 40+); 7% of bots are assassins, using one coat a kill, vials at
    /// the world's lethal-poison pack prices (20 a pack) interpolated by level. Simplifications: repairs are all at a smith
    /// (a crafter gamebot's repair is a transfer, which does not change the supply); the luxury's draughts are not
    /// modelled apart from its services; siege supply is not rehearsed (the rehearsal has no sieges); the governor's
    /// commission lever is reported but WP5's adapter posts at the unscaled rate.
    /// </remarks>
    public sealed class WP7Sinks : RehearsalModule
    {
        public const string ModuleId = "WP7.Sinks";

        public const double SwingsPerHuntingMinute = 10;
        public const double HitsTakenPerHuntingMinute = 10;
        public const int ConditionLossChancePercent = 5;
        public const double DrinksPerHuntingHour = 0.5;
        public const int DraughtCharges = 9;
        public const int AssassinPercent = 7;
        public const double CoatsPerKill = 1.0;

        private static readonly (int Level, long PackPrice)[] VialLadder =
            [(1, 11), (5, 39), (10, 175), (15, 366), (20, 770), (35, 3506), (40, 4736), (45, 6399), (50, 8643)];

        /// <summary>The growth band the governor keeps to (the gates by default; a test may tighten it).</summary>
        public SinkGovernor.Band Band { get; init; } = SinkGovernor.Gates;

        private sealed class AgentState
        {
            public int LastMinute;
            public int LastTownMinutes;
            public long LastKills;
            public bool Assassin;
            public int DraughtCharges;
            public int Vials;
            public double DrinkCredit;
        }

        private sealed class Stats
        {
            public long WearEvents, ConditionLost, Errands, PiecesRepaired, RepairCopper, Unaffordable;
            public long DraughtsBought, DraughtCopper, Drinks, VialsBought, VialCopper, Coats, CoatsMissed;
            public long LuxurySpends, LuxuryCopper;
            public double IntensitySum;
            public int IntensityHours;
            public int HoursOver;
        }

        private readonly Stats _stats = new();
        private readonly Dictionary<eRealm, SinkGovernor.State> _governor = new();
        private bool _upkeep, _luxury, _siege;
        private UpkeepPolicy.Mode _mode;

        public override string Id => ModuleId;
        public override string Package => "WP7";
        public override string Describe =>
            "WP7 sinks: gear wear and repairs at task breaks (True165's errand at 88%), heal draughts and paid poison vials, the luxury budget driven by the money-supply governor";
        public override IReadOnlyCollection<string> ReadsSettings => ["bot_upkeep", "bot_luxury_spending", "bot_siege_supply"];

        public override bool AppliesTo(RehearsalScenario scenario) =>
            scenario.Flag("bot_crafters") && (scenario.Flag("bot_upkeep") || scenario.Flag("bot_luxury_spending") || scenario.Flag("bot_siege_supply"));

        public override void Attach(RehearsalWorld world)
        {
            _upkeep = world.Scenario.Flag("bot_upkeep");
            _luxury = world.Scenario.Flag("bot_luxury_spending");
            _siege = world.Scenario.Flag("bot_siege_supply");
            _mode = CraftingEconomyProperties.ParseUpkeep(world.Scenario.Text("player_upkeep"));
            foreach (eRealm realm in world.Realms)
                _governor[realm] = SinkGovernor.State.Start;
        }

        private static AgentState StateOf(RehearsalAgent agent)
        {
            if (!agent.State.TryGetValue(ModuleId, out object state))
                agent.State[ModuleId] = state = new AgentState
                {
                    Assassin = (int)(AutonomousBotPersonality.Mix(agent.Id, 7_007) % 100) < AssassinPercent,
                    LastKills = agent.Kills,
                };
            return (AgentState)state;
        }

        public override void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent)
        {
            AgentState state = StateOf(agent);
            int huntedMinutes = Math.Max(0, world.Minute - state.LastMinute - state.LastTownMinutes);
            long kills = agent.Kills - state.LastKills;
            state.LastMinute = world.Minute;
            state.LastTownMinutes = agent.TownMinutes;
            state.LastKills = agent.Kills;
            Random random = world.RandomFor(agent.Realm);
            if (_upkeep)
            {
                Wear(agent, huntedMinutes, random);
                Repair(world, agent);
                Draughts(world, agent, state, huntedMinutes);
                if (state.Assassin)
                    Poisons(world, agent, state, kills);
            }
            if (_luxury)
            {
                double intensity = _governor.TryGetValue(agent.Realm, out SinkGovernor.State governor) ? governor.Intensity : 0;
                long budget = BotLuxury.Budget(agent.Purse, agent.Level, intensity);
                if (budget > 0 && world.Pay(agent, budget, EconomyFlowKind.Luxury))
                {
                    _stats.LuxurySpends++;
                    _stats.LuxuryCopper += budget;
                }
            }
        }

        private static int Events(double expected, Random random)
        {
            int whole = (int)expected;
            return whole + (random.NextDouble() < expected - whole ? 1 : 0);
        }

        private void Wear(RehearsalAgent agent, int huntedMinutes, Random random)
        {
            for (int slot = 0; slot < RehearsalAgent.Slots.Length; slot++)
            {
                DbInventoryItem item = agent.Worn[slot];
                eInventorySlot where = RehearsalAgent.SlotOf(slot);
                if (item?.Template == null || where is eInventorySlot.Cloak or eInventorySlot.Neck)
                    continue;
                double rolls = where == eInventorySlot.RightHandWeapon ? SwingsPerHuntingMinute * huntedMinutes : HitsTakenPerHuntingMinute * huntedMinutes / 6.0;
                int events = Events(rolls * ConditionLossChancePercent / 100.0, random);
                for (int e = 0; e < events && BotGearWear.CanWear(item.MaxCondition, item.ConditionPercent); e++)
                {
                    int loss = GameInventoryItem.ConditionLoss(agent.Level, item.Level, item.ConditionPercent);
                    item.Condition = Math.Max(0, item.Condition - loss);
                    _stats.WearEvents++;
                    _stats.ConditionLost += loss;
                }
            }
        }

        private static IEnumerable<DbInventoryItem> AsEquipped(RehearsalAgent agent)
        {
            for (int slot = 0; slot < RehearsalAgent.Slots.Length; slot++)
                if (agent.Worn[slot] is DbInventoryItem item)
                {
                    item.SlotPosition = (int)RehearsalAgent.SlotOf(slot);
                    yield return item;
                }
        }

        private void Repair(RehearsalWorld world, RehearsalAgent agent)
        {
            List<DbInventoryItem> equipped = AsEquipped(agent).ToList();
            if (!BotGearWear.WantsErrand(equipped, agent.Purse, _mode))
                return;
            _stats.Errands++;
            foreach (DbInventoryItem item in BotGearWear.WornPieces(equipped, BotGearWear.RepairBelowPercent))
            {
                long price = BotGearWear.PriceOf(item, _mode);
                if (!world.Pay(agent, price, EconomyFlowKind.Repair))
                {
                    _stats.Unaffordable++;
                    break;
                }
                DOL.GS.Repair.ModifyConditionAndDurability(item, UpkeepPolicy.ConsumesDurability(_mode));
                _stats.PiecesRepaired++;
                _stats.RepairCopper += price;
            }
        }

        public static long DraughtPrice(int level) => level >= 40 ? 25_000 : level >= 30 ? 13_000 : 8_000;

        private void Draughts(RehearsalWorld world, RehearsalAgent agent, AgentState state, int huntedMinutes)
        {
            if (state.DraughtCharges > 0)
            {
                state.DrinkCredit += DrinksPerHuntingHour * huntedMinutes / 60.0;
                int drinks = Math.Min(state.DraughtCharges, (int)state.DrinkCredit);
                state.DrinkCredit -= drinks;
                state.DraughtCharges -= drinks;
                _stats.Drinks += drinks;
            }
            long price = DraughtPrice(agent.Level);
            int owned = (state.DraughtCharges + DraughtCharges - 1) / DraughtCharges;
            int wanted = BotPotions.HealDraughtsWanted(agent.Level, agent.Purse, BotPotions.Reserve(agent.Level), price);
            for (; owned < wanted; owned++)
            {
                if (!world.Pay(agent, price, EconomyFlowKind.Potion))
                    break;
                state.DraughtCharges += DraughtCharges;
                _stats.DraughtsBought++;
                _stats.DraughtCopper += price;
            }
        }

        public static long VialPackPrice(int level)
        {
            level = Math.Clamp(level, 1, 50);
            for (int i = 1; i < VialLadder.Length; i++)
                if (level <= VialLadder[i].Level)
                {
                    (int l0, long p0) = VialLadder[i - 1];
                    (int l1, long p1) = VialLadder[i];
                    return p0 + (p1 - p0) * (level - l0) / Math.Max(1, l1 - l0);
                }
            return VialLadder[^1].PackPrice;
        }

        private void Poisons(RehearsalWorld world, RehearsalAgent agent, AgentState state, long kills)
        {
            int used = (int)Math.Min(int.MaxValue, (long)Math.Round(kills * CoatsPerKill));
            int coats = Math.Min(used, state.Vials);
            state.Vials -= coats;
            _stats.Coats += coats;
            _stats.CoatsMissed += used - coats;
            long pack = VialPackPrice(agent.Level);
            long perVial = BotPoisonSupply.VialsPrice(pack, 20, 1);
            int count = BotPoisonSupply.RestockCount(state.Vials, agent.Purse, perVial);
            if (count <= 0)
                return;
            long price = BotPoisonSupply.VialsPrice(pack, 20, count);
            if (!world.Pay(agent, price, EconomyFlowKind.Poison))
                return;
            state.Vials += count;
            _stats.VialsBought += count;
            _stats.VialCopper += price;
        }

        public override void OnHour(RehearsalWorld world)
        {
            if (!_luxury)
                return;
            foreach (eRealm realm in world.Realms)
            {
                // The previous hour's closed row, as the server reads WP2's row for the hour just ended.
                RehearsalHourRow row = world.Rows.LastOrDefault(r => r.Realm == realm);
                if (row == null)
                    continue;
                double ratio = SinkGovernor.Ratio(row.SupplyCopper, row.FaucetsCopper - row.SinksCopper, row.Bots, Band);
                SinkGovernor.State next = _governor[realm] = SinkGovernor.Step(_governor[realm], ratio);
                _stats.IntensitySum += next.Intensity;
                _stats.IntensityHours++;
                if (next.Over)
                    _stats.HoursOver++;
                world.HourMetric(realm, "wp7.luxuryIntensity", next.Intensity);
                world.HourMetric(realm, "wp7.growthRatio", ratio);
                if (world.EconomyDiagnosticsLines)
                    world.Log(SinkGovernor.Line(realm, EconomyLog.Format(world.NowUtc), row.SupplyCopper, row.FaucetsCopper - row.SinksCopper,
                        row.Bots, ratio, next, SinkGovernor.CommissionMultiplier(_governor.Values)));
            }
        }

        public override void Finish(RehearsalWorld world, RehearsalReport report)
        {
            report.Metric("wp7.upkeep", _upkeep);
            report.Metric("wp7.luxury", _luxury);
            report.Metric("wp7.siegeSupply", _siege ? "not rehearsed (no sieges in the rehearsal world)" : "off");
            report.Metric("wp7.upkeepMode", _mode.ToString().ToLowerInvariant());
            report.Metric("wp7.wearEvents", _stats.WearEvents);
            report.Metric("wp7.conditionLost", _stats.ConditionLost);
            report.Metric("wp7.repairErrands", _stats.Errands);
            report.Metric("wp7.piecesRepaired", _stats.PiecesRepaired);
            report.Metric("wp7.repairCopper", _stats.RepairCopper);
            report.Metric("wp7.repairsUnaffordable", _stats.Unaffordable);
            report.Metric("wp7.draughtsBought", _stats.DraughtsBought);
            report.Metric("wp7.draughtCopper", _stats.DraughtCopper);
            report.Metric("wp7.drinks", _stats.Drinks);
            report.Metric("wp7.vialsBought", _stats.VialsBought);
            report.Metric("wp7.vialCopper", _stats.VialCopper);
            report.Metric("wp7.coats", _stats.Coats);
            report.Metric("wp7.coatsWithoutVial", _stats.CoatsMissed);
            report.Metric("wp7.luxurySpends", _stats.LuxurySpends);
            report.Metric("wp7.luxuryCopper", _stats.LuxuryCopper);
            report.Metric("wp7.luxuryIntensityMean", _stats.IntensityHours == 0 ? 0 : Math.Round(_stats.IntensitySum / _stats.IntensityHours, 3));
            report.Metric("wp7.luxuryIntensityEnd", _governor.Count == 0 ? 0 : Math.Round(_governor.Values.Average(s => s.Intensity), 3));
            report.Metric("wp7.governorHoursOver", _stats.HoursOver);
            report.Metric("wp7.governorBand", $"{Band.WeekPercentMax:0.#}% a week / {Band.PerBotHourGoldMax:0.#} gold a bot-hour");
            report.Metric("wp7.commissionMultiplierEnd", SinkGovernor.CommissionMultiplier(_governor.Values));
        }
    }
}
