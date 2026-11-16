using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// One autonomous gamebot in the rehearsal: realm, personality (today's TypeFor unless a module types it), level,
    /// purse, worn gear and bag (in-memory DbInventoryItems, never saved), its listings and its crafting skills in
    /// BotCraftingSkills' saved format. Companions and players are not modelled: the rehearsal is the gamebot economy.
    /// </summary>
    public sealed class RehearsalAgent
    {
        /// <summary>The slots the rehearsal dresses: six armor pieces, the cloak, the main weapon and the necklace.</summary>
        public static readonly eInventorySlot[] Slots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.TorsoArmor,
            eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor, eInventorySlot.Cloak, eInventorySlot.RightHandWeapon,
            eInventorySlot.Neck,
        ];

        public enum Phase { Hunting, Town }

        public long Id { get; init; }
        public string Name { get; init; }
        public eRealm Realm { get; init; }
        public BotPersonality Personality { get; init; }
        public bool IsCrafter => Personality.Type == BotPersonalityType.Crafter;
        public string OwnerId => AutonomousBotEconomy.GetOwnerId(Id);

        /// <summary>The armor and weapon types this bot wears (by its id: a stand-in for its class).</summary>
        public int ArmorObjectType { get; init; }
        public int WeaponObjectType { get; init; }

        public int Level { get; set; } = 1;
        public double LevelProgress { get; set; }
        public long Purse { get; set; }
        public DbInventoryItem[] Worn { get; } = new DbInventoryItem[Slots.Length];
        public List<DbInventoryItem> Bag { get; } = new();
        /// <summary>Plain loot waiting for a vendor (its sale value in copper).</summary>
        public long TrashCopper { get; set; }
        public int Listings { get; set; }

        /// <summary>BotCraftingSkills' format ("skill|value;..."); empty until a crafting module enrolls the bot.</summary>
        public string CraftingSkills { get; set; } = string.Empty;

        public Phase Now { get; set; }
        public int PhaseEndsMinute { get; set; }
        public bool Grouped { get; set; }
        public int LastBreakMinute { get; set; }
        /// <summary>The town break planned at this task break (0 = straight to the next task); a module may claim it.</summary>
        public int TownMinutes { get; set; }
        /// <summary>What the town break is spent on ("idle" unless a module claims it, e.g. "craft").</summary>
        public string TownActivity { get; set; } = "idle";

        public long Kills { get; set; }
        public long LootCoin { get; set; }
        /// <summary>Per-module state, keyed by the module's id.</summary>
        public Dictionary<string, object> State { get; } = new(StringComparer.Ordinal);

        public static int SlotIndexOf(int itemType) => Array.IndexOf(Slots, (eInventorySlot)itemType);

        public static eInventorySlot SlotOf(int index) => index >= 0 && index < Slots.Length ? Slots[index] : eInventorySlot.Invalid;

        public bool BagFull(int slots) => Bag.Count >= slots;

        public int WornValue(int slotIndex) => AutonomousBotEconomy.EquipmentValue(Worn[slotIndex]);
    }

    /// <summary>The items of the rehearsal: random magic drops (ROG) and merchant gear, as in-memory items.</summary>
    public static class RehearsalItems
    {
        private static readonly int[] ArmorTypes = [32, 33, 34, 35, 36];
        private static readonly int[] WeaponTypes = [3, 11, 19]; // slashing, sword, blades (one per realm's naming)

        public static int ArmorTypeFor(long id) => ArmorTypes[(int)(AutonomousBotPersonality.Mix(id, 501) % (ulong)ArmorTypes.Length)];

        public static int WeaponTypeFor(eRealm realm) => realm switch { eRealm.Midgard => 11, eRealm.Hibernia => 19, _ => 3 };

        /// <summary>An item of a slot at a level: weapons 1.2 + 0.3 x level DPS, armor 2 AF a level (cloth 1), the
        /// necklace and cloak no AF; utility spread over three bonuses.</summary>
        public static DbInventoryItem Make(string name, int slotIndex, int level, int quality, int utility, long price, int armorType,
            int weaponType, bool rog)
        {
            eInventorySlot slot = RehearsalAgent.SlotOf(slotIndex);
            level = Math.Clamp(level, 1, 51);
            int objectType, dpsAf = 0, spdAbs = 0;
            switch (slot)
            {
                case eInventorySlot.RightHandWeapon:
                    objectType = weaponType;
                    dpsAf = 12 + 3 * level;
                    spdAbs = 37;
                    break;
                case eInventorySlot.Cloak:
                    objectType = (int)eObjectType.Magical;
                    break;
                case eInventorySlot.Neck:
                    objectType = (int)eObjectType.Magical;
                    break;
                default:
                    objectType = armorType;
                    dpsAf = Math.Min(102, armorType == 32 ? level : level * 2);
                    spdAbs = (armorType - 32) * 9;
                    break;
            }
            var template = new DbItemTemplate
            {
                Id_nb = rog ? "rehearsal_rog" : "rehearsal_merchant",
                Name = name,
                Level = level,
                Object_Type = objectType,
                Item_Type = (int)slot,
                Quality = quality,
                DPS_AF = dpsAf,
                SPD_ABS = spdAbs,
                MaxCondition = 50_000,
                Condition = 50_000,
                MaxDurability = 50_000,
                Durability = 50_000,
                Price = price,
                IsDropable = true,
                IsPickable = true,
                IsTradable = true,
                PackSize = 1,
                MaxCount = 1,
                Bonus1 = (utility + 2) / 3,
                Bonus2 = (utility + 1) / 3,
                Bonus3 = utility / 3,
            };
            var item = GameInventoryItem.Create(template);
            item.IsROG = rog;
            item.Quality = quality;
            return item;
        }

        /// <summary>A random magic drop of a level (ROGMobGenerator: price SetAutoPrice / 8; quality and utility from the calibration).</summary>
        public static DbInventoryItem Rog(RehearsalAgent agent, int level, Random random, RehearsalCalibration calibration)
        {
            var loot = calibration.Loot;
            int slotIndex = random.Next(RehearsalAgent.Slots.Length);
            int quality = Math.Clamp(loot.RogQualityMin + random.Next(loot.RogQualityMax - loot.RogQualityMin + 1), 1, 100);
            int utility = Math.Max(loot.RogUtilityFloor, (int)((level - 7) * loot.RogUtilityPerLevel * (0.85 + random.NextDouble() * 0.3)));
            long price = Math.Max(2, Money.SetAutoPrice(level, quality) / 8);
            return Make($"magic {RehearsalAgent.SlotOf(slotIndex)} L{level}", slotIndex, level, quality, utility, price,
                agent.ArmorObjectType, agent.WeaponObjectType, rog: true);
        }

        /// <summary>Merchant gear of a level for a slot (quality 85, no bonuses) at the world's merchant price.</summary>
        public static DbInventoryItem Merchant(RehearsalAgent agent, int slotIndex, int level, long price) =>
            Make($"merchant {RehearsalAgent.SlotOf(slotIndex)} L{level}", slotIndex, level, 85, 0, price,
                agent.ArmorObjectType, agent.WeaponObjectType, rog: false);
    }

    /// <summary>
    /// Today's gamebot economy, pure: hunting (M0's kills and coin per hour by band, ROG drops), and the natural task
    /// break (equip upgrades, list or vendor loot, maybe buy on the Exchange or from a vendor, a service, a ticket, then
    /// maybe a town break). Packages change it only through the world's hooks and their modules.
    /// </summary>
    public static class RehearsalBaseline
    {
        public static void Populate(RehearsalWorld world)
        {
            var calibration = world.Calibration;
            foreach (eRealm realm in world.Realms)
            {
                Random random = world.RandomFor(realm);
                for (int n = 0; n < world.Scenario.BotsPerRealm; n++)
                {
                    long id = (long)world.RealmIndex(realm) * 1_000_000 + n + 1;
                    BotPersonalityType type = world.TypeOf(id, world.Scenario.Aged);
                    BotPersonality traits = AutonomousBotPersonality.For(id);
                    var agent = new RehearsalAgent
                    {
                        Id = id,
                        Name = $"{RehearsalWorld.RealmName(realm)[..3]}{n + 1:0000}",
                        Realm = realm,
                        Personality = new BotPersonality(type, traits.Traits),
                        ArmorObjectType = RehearsalItems.ArmorTypeFor(id),
                        WeaponObjectType = RehearsalItems.WeaponTypeFor(realm),
                    };
                    if (world.Scenario.Aged)
                        Age(world, agent, random);
                    // Stagger the first task ends so the whole realm does not break at once.
                    agent.PhaseEndsMinute = random.Next(calibration.Time.TaskMinutesMin, calibration.Time.TaskMinutesMax + 1);
                    agent.Grouped = random.NextDouble() < calibration.Groups.ShareOfTasksGrouped;
                    world.Agents.Add(agent);
                }
            }
        }

        /// <summary>An aged bot: a level and purse from M0's snapshot, and merchant-quality gear of its level in every slot.</summary>
        private static void Age(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var aged = world.Calibration.AgedWorld;
            double roll = random.NextDouble() * aged.LevelShares.Sum(share => share.Share);
            RehearsalCalibration.AgedShare band = aged.LevelShares.Last();
            foreach (RehearsalCalibration.AgedShare share in aged.LevelShares)
            {
                if (roll < share.Share)
                {
                    band = share;
                    break;
                }
                roll -= share.Share;
            }
            agent.Level = band.MinLevel + random.Next(band.MaxLevel - band.MinLevel + 1);
            double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            agent.Purse = (long)(band.MedianPurseGold * 10_000 * Math.Exp(aged.PurseSpread * normal));
            for (int slot = 0; slot < RehearsalAgent.Slots.Length; slot++)
            {
                int level = Math.Max(1, agent.Level - random.Next(4));
                long price = Math.Max(2, Money.SetAutoPrice(level, 90) / 8);
                agent.Worn[slot] = RehearsalItems.Make($"worn {RehearsalAgent.SlotOf(slot)} L{level}", slot, level, 88 + random.Next(8),
                    Math.Max(0, (level - 10) / 2), price, agent.ArmorObjectType, agent.WeaponObjectType, rog: true);
            }
        }

        /// <summary>One hunting minute: kills (M0's rate for the band), their coin (split in a group), XP, drops.</summary>
        public static void HuntMinute(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var calibration = world.Calibration;
            double perMinute = calibration.KillsPerHour(agent.Level, agent.Grouped) / 60.0;
            int kills = (int)perMinute + (random.NextDouble() < perMinute - Math.Floor(perMinute) ? 1 : 0);
            double xpFactor = agent.Grouped ? calibration.Leveling.GroupXpFactor : 1.0;
            if (agent.Level < 50)
            {
                agent.LevelProgress += xpFactor / 60.0 / calibration.HoursToLevel(agent.Level);
                while (agent.LevelProgress >= 1 && agent.Level < 50)
                {
                    agent.LevelProgress -= 1;
                    agent.Level++;
                    world.Count("level_ups");
                }
            }
            if (kills == 0)
                return;
            int monster = calibration.MonsterLevel(agent.Level);
            int split = agent.Grouped ? Math.Max(1, calibration.Groups.GroupSize) : 1;
            long coin = 0;
            for (int k = 0; k < kills; k++)
            {
                double each = calibration.CoinPerKillBase(monster) * (1 + random.NextDouble() * calibration.Coin.ExtraUpTo);
                coin += (long)(each / split);
                if (random.NextDouble() < calibration.RogChance(agent.Level, agent.Grouped) &&
                    !agent.BagFull(calibration.Loot.BagSlots))
                    agent.Bag.Add(RehearsalItems.Rog(agent, monster, random, calibration));
            }
            agent.Kills += kills;
            agent.LootCoin += coin;
            world.Earn(agent, coin, EconomyFlowKind.LootCoin);
            agent.TrashCopper += (long)(coin * calibration.Loot.TrashVendorShareOfCoin);
            // A full bag is a natural task break (the 'I' flag): the task ends at once.
            if (agent.BagFull(calibration.Loot.BagSlots))
                agent.PhaseEndsMinute = Math.Min(agent.PhaseEndsMinute, world.Minute + 1);
        }

        /// <summary>
        /// The natural task break. Its order is today's between-task plan: decide the town break, wear upgrades, list the
        /// listable and vendor the rest, maybe buy (Exchange, vendor, a service), maybe a ticket; then each module's step.
        /// </summary>
        public static void TaskBreak(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var calibration = world.Calibration;
            double hours = Math.Max(1, world.Minute - agent.LastBreakMinute) / 60.0;
            agent.LastBreakMinute = world.Minute;
            world.Count("task_breaks");

            agent.TownActivity = "idle";
            agent.TownMinutes = random.NextDouble() < AutonomousBotPersonality.TownBreakChance(agent.Personality)
                ? (int)(random.Next(calibration.Time.TownBreakMinutesMin, calibration.Time.TownBreakMinutesMax + 1) *
                        AutonomousBotPersonality.TownBreakLengthFactor(agent.Personality))
                : 0;

            WearUpgrades(world, agent);
            SellAndList(world, agent, random);

            if (random.NextDouble() < calibration.ExchangeAttemptsPerHour(agent.Level) * hours)
                BuyOnExchange(world, agent);
            if (random.NextDouble() < calibration.Shopping.VendorAttemptsPerBotHour * hours)
                BuyFromVendor(world, agent);
            if (random.NextDouble() < calibration.Shopping.ServicePurchasesPerBotHour * hours &&
                world.Pay(agent, calibration.Shopping.ServicePriceCopper, EconomyFlowKind.Dye))
                world.Count("services");
            if (random.NextDouble() < calibration.Travel.TicketsPerTaskBreak &&
                world.Pay(agent, calibration.Travel.TicketPriceCopper, EconomyFlowKind.StableTicket))
                world.Count("tickets");

            foreach (IRehearsalModule module in world.Modules)
                module.OnTaskBreak(world, agent);

            if (agent.TownMinutes > 0)
            {
                agent.Now = RehearsalAgent.Phase.Town;
                agent.PhaseEndsMinute = world.Minute + agent.TownMinutes;
                world.Count(agent.TownActivity == "idle" ? "town_breaks" : "town_breaks_" + agent.TownActivity);
            }
            else
                StartTask(world, agent, random);
        }

        public static void StartTask(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var time = world.Calibration.Time;
            agent.Now = RehearsalAgent.Phase.Hunting;
            agent.PhaseEndsMinute = world.Minute + random.Next(time.TaskMinutesMin, time.TaskMinutesMax + 1);
            agent.Grouped = random.NextDouble() < world.Calibration.Groups.ShareOfTasksGrouped;
        }

        /// <summary>Wears each bag item the world's policy calls an upgrade (best first per slot).</summary>
        public static void WearUpgrades(RehearsalWorld world, RehearsalAgent agent)
        {
            foreach (DbInventoryItem item in agent.Bag.OrderByDescending(AutonomousBotEconomy.EquipmentValue).ToArray())
            {
                int slot = RehearsalAgent.SlotIndexOf(item.Item_Type);
                if (slot < 0 || item.Level > agent.Level || !world.IsUpgrade(agent, agent.Worn[slot], item))
                    continue;
                agent.Bag.Remove(item);
                world.Equip(agent, slot, item);
                world.Count(item.IsROG ? "loot_upgrades" : "bag_upgrades");
            }
        }

        /// <summary>Lists what is worth listing while there is room (best first), vendors the rest and the plain loot.</summary>
        public static void SellAndList(RehearsalWorld world, RehearsalAgent agent, Random random)
        {
            var shopping = world.Calibration.Shopping;
            foreach (DbInventoryItem item in agent.Bag.OrderByDescending(i => AutonomousBotEconomy.RecommendListingPrice(i)).ToArray())
            {
                if (item.IsTradable)
                {
                    double disposition = shopping.ListingDispositionMin +
                                         random.NextDouble() * (shopping.ListingDispositionMax - shopping.ListingDispositionMin);
                    long ask = world.PriceIndex.Ask(item, disposition);
                    // AutonomousBotEconomy.WorthListing's rule, without a GameBot: at least 50 copper and level^2 x 4.
                    if (ask >= Math.Max(50, item.Level * item.Level * 4) && world.TryList(agent, item, ask))
                    {
                        agent.Bag.Remove(item);
                        continue;
                    }
                }
                world.Earn(agent, world.PriceIndex.Floor(item), EconomyFlowKind.VendorSale);
                agent.Bag.Remove(item);
                world.Count("vendor_sales");
            }
            if (agent.TrashCopper > 0)
            {
                world.Earn(agent, agent.TrashCopper, EconomyFlowKind.VendorSale);
                agent.TrashCopper = 0;
            }
        }

        /// <summary>Buys the best affordable upgrade listed by another bot of the realm (today: no bid limit, the upgrade rule).</summary>
        public static void BuyOnExchange(RehearsalWorld world, RehearsalAgent agent)
        {
            world.Count("exchange_attempts");
            RehearsalListing best = null;
            int bestGain = 0;
            foreach (RehearsalListing listing in world.Listings(agent.Realm))
            {
                if (listing.Gone || listing.Seller == agent || listing.Price > agent.Purse || listing.Item.Level > agent.Level)
                    continue;
                int slot = RehearsalAgent.SlotIndexOf(listing.Item.Item_Type);
                if (slot < 0 || !world.IsUpgrade(agent, agent.Worn[slot], listing.Item))
                    continue;
                int gain = AutonomousBotEconomy.EquipmentValue(listing.Item) - agent.WornValue(slot);
                if (gain > bestGain)
                {
                    best = listing;
                    bestGain = gain;
                }
            }
            if (best != null && world.TryBuy(agent, best))
                world.Equip(agent, RehearsalAgent.SlotIndexOf(best.Item.Item_Type), best.Item);
        }

        /// <summary>Buys merchant gear at the highest level merchants sell up to the bot's, for its weakest slot, if an upgrade.</summary>
        public static void BuyFromVendor(RehearsalWorld world, RehearsalAgent agent)
        {
            world.Count("vendor_attempts");
            (int level, long price) = world.Recipes.MerchantGearAtOrBelow(agent.Level);
            if (level == 0 || price > agent.Purse)
                return;
            int weakest = Enumerable.Range(0, RehearsalAgent.Slots.Length).OrderBy(agent.WornValue).First();
            DbInventoryItem item = RehearsalItems.Merchant(agent, weakest, level, price);
            if (!world.IsUpgrade(agent, agent.Worn[weakest], item) || !world.Pay(agent, price, EconomyFlowKind.VendorGear))
                return;
            world.Equip(agent, weakest, item);
            world.Count("vendor_buys");
        }
    }
}
