using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal.Adapters
{
    /// <summary>
    /// A stand-in for bot crafting until WP4 lands, built only on what WP0 already has: <see cref="CraftRules"/> (the craft
    /// maths, verbatim copies of today's), <see cref="BotCraftingSkills"/> (DividedRealms' skill format) and the recipe
    /// fixture. Runs only in scenarios with <c>bot_crafters</c> on. A Crafter-type gamebot spends each town break at a
    /// station: it buys a recipe's materials from a merchant (only recipes whose materials merchants sell), crafts at
    /// today's player pace (time, chance to make, chance to gain, quality), wears a crafted piece when it is an upgrade,
    /// lists it when worth listing, and vendors the rest. Fresh bots are typed with <c>bot_crafter_share_percent</c>; aged
    /// bots keep their legacy (12%) types.
    /// </summary>
    /// <remarks>
    /// Simplifications, all WP4's to replace: one primary craft chosen from the bot's id, no secondary skills or masters,
    /// a failed craft costs its materials (no classic losses rule; that is WP1's), the recipe is the hardest yellow or
    /// orange one (else the nearest below). WP4's adapter supersedes this one by listing <see cref="StandInId"/> in its
    /// Supersedes; nothing else needs to change.
    /// </remarks>
    public sealed class WP0CraftRulesStandIn : RehearsalModule
    {
        public const string StandInId = "WP0.CraftRulesStandIn";

        private static readonly eCraftingSkill[] Orders =
        [
            eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring, eCraftingSkill.Fletching,
            eCraftingSkill.SpellCrafting, eCraftingSkill.Alchemy,
        ];

        private sealed class Stats
        {
            public int Sessions, Crafts, Made, Points, Worn, Listed, Vendored, NoRecipe, NoCoin;
            public long Spent, Sold;
            public readonly Dictionary<eRealm, int> FirstLegendaryHour = new();
        }

        private readonly Stats _stats = new();

        public override string Id => StandInId;
        public override string Package => "WP0";
        public override string Describe =>
            "Crafter stand-in until WP4: CraftRules at player pace in town breaks, BotCraftingSkills, recipes.json; typing by bot_crafter_share_percent for fresh bots";
        public override IReadOnlyCollection<string> ReadsSettings => ["bot_crafters", "bot_crafter_share_percent"];

        public override bool AppliesTo(RehearsalScenario scenario) => scenario.Flag("bot_crafters");

        public override void Attach(RehearsalWorld world)
        {
            int share = Math.Clamp(world.Scenario.Int("bot_crafter_share_percent"), 0, 100);
            world.TypeOf = (id, aged) =>
            {
                if (aged)
                    return AutonomousBotPersonality.TypeFor(id); // legacy bots keep their 12%-era type
                if ((int)(AutonomousBotPersonality.Mix(id, 7_007) % 100) < share)
                    return BotPersonalityType.Crafter;
                BotPersonalityType type = AutonomousBotPersonality.TypeFor(id);
                return type == BotPersonalityType.Crafter ? BotPersonalityType.Leveler : type;
            };
        }

        public static eCraftingSkill OrderFor(long id) => Orders[(int)(AutonomousBotPersonality.Mix(id, 7_011) % (ulong)Orders.Length)];

        public override void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent)
        {
            if (!agent.IsCrafter || agent.TownMinutes <= 0 || agent.TownActivity != "idle")
                return;
            if (string.IsNullOrEmpty(agent.CraftingSkills))
            {
                eCraftingSkill order = OrderFor(agent.Id);
                agent.CraftingSkills = BotCraftingSkills.Join(order, null);
                world.Log(EconomyLog.Line(EconomyLog.CraftEnroll, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                    ("skill", order), ("source", "rehearsal")));
            }
            agent.TownActivity = "craft";
            Session(world, agent, world.RandomFor(agent.Realm));
        }

        private void Session(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var crafting = world.Calibration.Crafting;
            eCraftingSkill skill = BotCraftingSkills.Primary(agent.CraftingSkills);
            int startValue = BotCraftingSkills.Value(agent.CraftingSkills, skill);
            RehearsalRecipe[] recipes = world.Recipes.For(skill, agent.Realm);
            long budget = (long)(agent.Purse * crafting.ReinvestShareOfPurse);
            int seconds = agent.TownMinutes * 60;
            int crafts = 0, made = 0, points = 0;
            long spent = 0, sold = 0;
            _stats.Sessions++;
            world.Log(EconomyLog.Line(EconomyLog.CraftSessionStart, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                ("skill", skill), ("value", startValue), ("minutes", agent.TownMinutes), ("source", "rehearsal")));
            while (true)
            {
                int value = BotCraftingSkills.Value(agent.CraftingSkills, skill);
                RehearsalRecipe recipe = Choose(recipes, value);
                if (recipe == null)
                {
                    _stats.NoRecipe++;
                    break;
                }
                long cost = recipe.Info.MaterialCostCopper;
                var context = new CraftContext(value, recipe.Info.Level, InCapital: true, CapitalGainBonus: crafting.CapitalGainBonus);
                int time = CraftRules.CraftingTimeSeconds(context, recipe.Info.IngredientCounts);
                if (time > seconds)
                    break;
                if (spent + cost > budget || !world.Pay(agent, cost, EconomyFlowKind.CraftMaterials))
                {
                    _stats.NoCoin++;
                    break;
                }
                seconds -= time;
                spent += cost;
                crafts++;
                if (random.Next(100) >= CraftRules.ChanceToMake(value, recipe.Info.Level))
                    continue;
                made++;
                if (random.Next(100) < CraftRules.ChanceToGainPoint(context))
                {
                    agent.CraftingSkills = BotCraftingSkills.Gain(agent.CraftingSkills, skill, primaryWork: true);
                    points++;
                    Titles(world, agent, skill, value, value + 1);
                }
                int quality = CraftRules.RollQuality(value, recipe.Info.Level, random);
                sold += Dispose(world, agent, recipe.Make(quality, agent.Name), random);
            }
            _stats.Crafts += crafts;
            _stats.Made += made;
            _stats.Points += points;
            _stats.Spent += spent;
            _stats.Sold += sold;
            world.Log(EconomyLog.Line(EconomyLog.CraftSessionEnd, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                ("skill", skill), ("value", BotCraftingSkills.Value(agent.CraftingSkills, skill)), ("crafts", crafts), ("made", made),
                ("points", points), ("spentCopper", spent), ("soldCopper", sold), ("source", "rehearsal")));
        }

        /// <summary>The hardest yellow or orange recipe (the best gain chance), else the nearest one below the skill.</summary>
        public static RehearsalRecipe Choose(IReadOnlyList<RehearsalRecipe> byLevel, int skill)
        {
            RehearsalRecipe best = null;
            foreach (RehearsalRecipe recipe in byLevel)
            {
                if (recipe.Info.Level > skill + 19)
                    break;
                if (CraftRules.ChanceToMake(skill, recipe.Info.Level) > 0)
                    best = recipe;
            }
            return best ?? (byLevel.Count > 0 ? byLevel[0] : null);
        }

        /// <summary>Wear it if an upgrade, else list it if worth listing, else sell it to a merchant. Returns vendor coin.</summary>
        private long Dispose(RehearsalWorld world, RehearsalAgent agent, DbInventoryItem item, Random random)
        {
            int slot = RehearsalAgent.SlotIndexOf(item.Item_Type);
            if (slot >= 0 && item.Level <= agent.Level && item.Object_Type != 0 && world.IsUpgrade(agent, agent.Worn[slot], item))
            {
                world.Equip(agent, slot, item, "crafted");
                _stats.Worn++;
                return 0;
            }
            var shopping = world.Calibration.Shopping;
            double disposition = shopping.ListingDispositionMin + random.NextDouble() * (shopping.ListingDispositionMax - shopping.ListingDispositionMin);
            long ask = world.PriceIndex.Ask(item, disposition);
            if (ask >= Math.Max(50, item.Level * item.Level * 4) && world.TryList(agent, item, ask))
            {
                _stats.Listed++;
                return 0;
            }
            long coin = world.PriceIndex.Floor(item);
            world.Earn(agent, coin, EconomyFlowKind.VendorSale);
            _stats.Vendored++;
            return coin;
        }

        private void Titles(RehearsalWorld world, RehearsalAgent agent, eCraftingSkill skill, int before, int after)
        {
            var crafting = world.Calibration.Crafting;
            if (after / crafting.TitleEvery > before / crafting.TitleEvery)
                world.Log(EconomyLog.Line(EconomyLog.CraftTitle, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                    ("skill", skill), ("value", after), ("source", "rehearsal")));
            if (after >= crafting.LegendarySkill && before < crafting.LegendarySkill && !_stats.FirstLegendaryHour.ContainsKey(agent.Realm))
            {
                _stats.FirstLegendaryHour[agent.Realm] = world.Hour;
                world.Log(EconomyLog.Line(EconomyLog.CraftFirst, ("hour", world.NowUtc), ("realm", RehearsalWorld.RealmName(agent.Realm)), ("bot", agent.Name),
                    ("title", "Legendary"), ("skill", skill), ("source", "rehearsal")));
            }
        }

        public override void OnHour(RehearsalWorld world)
        {
            foreach (eRealm realm in world.Realms)
            {
                int[] values = world.InRealm(realm).Where(a => a.IsCrafter && a.CraftingSkills.Length > 0)
                    .Select(a => BotCraftingSkills.Value(a.CraftingSkills, BotCraftingSkills.Primary(a.CraftingSkills)))
                    .OrderBy(v => v).ToArray();
                world.HourMetric(realm, "crafterSkillMedian", values.Length == 0 ? 0 : values[values.Length / 2]);
                world.HourMetric(realm, "crafterSkillMax", values.Length == 0 ? 0 : values[^1]);
            }
        }

        public override void Finish(RehearsalWorld world, RehearsalReport report)
        {
            int[] values = world.Agents.Where(a => a.IsCrafter && a.CraftingSkills.Length > 0)
                .Select(a => BotCraftingSkills.Value(a.CraftingSkills, BotCraftingSkills.Primary(a.CraftingSkills)))
                .OrderBy(v => v).ToArray();
            report.Metric("wp0.sessions", _stats.Sessions);
            report.Metric("wp0.crafts", _stats.Crafts);
            report.Metric("wp0.made", _stats.Made);
            report.Metric("wp0.points", _stats.Points);
            report.Metric("wp0.worn", _stats.Worn);
            report.Metric("wp0.listed", _stats.Listed);
            report.Metric("wp0.vendored", _stats.Vendored);
            report.Metric("wp0.sessionsOutOfCoin", _stats.NoCoin);
            report.Metric("wp0.materialsCopper", _stats.Spent);
            report.Metric("wp0.vendorCopper", _stats.Sold);
            report.Metric("wp0.enrolled", values.Length);
            report.Metric("wp0.skillMedian", values.Length == 0 ? 0 : values[values.Length / 2]);
            report.Metric("wp0.skillMax", values.Length == 0 ? 0 : values[^1]);
            foreach (eRealm realm in world.Realms)
                report.Metric($"wp0.firstLegendaryHour_{RehearsalWorld.RealmName(realm)}",
                    _stats.FirstLegendaryHour.TryGetValue(realm, out int hour) ? hour.ToString() : "not within the run");
        }
    }
}
