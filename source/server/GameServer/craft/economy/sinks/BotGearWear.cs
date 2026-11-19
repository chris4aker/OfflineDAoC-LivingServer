using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting 2.0 WP7: gamebots' gear wears like a player's and they pay to have it repaired at a natural task break.
    /// <para>Ported from shadowofze's True165 (<c>mode165/ItemWear165.cs</c> and the repair reason of
    /// <c>mode165/TownErrands165.cs</c>, 36358de; ItemWear165 as of 2e4212e), GPL-3.0. Changes in the port: gated by
    /// <c>bot_upkeep</c> (with <c>bot_crafters</c>) instead of True165's 1.65 Mode; autonomous gamebots only (True165 also
    /// wears hired companions' gear); the errand is an <see cref="IBotTownVisitNeed"/> and the repair a merchant
    /// <see cref="IBotServiceStep"/> (WP0's seams) instead of edits to the brain and the controller; a crafter's repair
    /// skill comes from its saved crafting skills (<see cref="BotCraftingSkills"/>) instead of True165's single craft
    /// level; the price and the durability follow the player's upkeep (<see cref="UpkeepPolicy"/>: light by default, half
    /// the smith's price and no durability lost), so a gamebot repairs exactly as a player does on this server; the
    /// wear amount is the player's own formula (<see cref="GameInventoryItem.ConditionLoss"/>); the ECON_WEAR (hourly)
    /// and ECON_REPAIR lines keep True165's BOT_REPAIRED_165 keys.</para>
    /// Wear: 5% (item_condition_loss_chance) of swings, hits taken and harmful spells, the con-based amount, doubled below
    /// 91%, never below 70%. The errand: below 88% a gamebot with the coin goes to a merchant at its next task break; there
    /// it repairs what is below 90%, worst first, with its own skill first, then at the nearer of a smith or a crafter
    /// gamebot of its realm that repairs the piece every time (the crafter is paid the price), as far as its coin goes.
    /// </summary>
    public static class BotGearWear
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotGearWear));

        /// <summary>Gear never wears below this (the player rule).</summary>
        public const int WearStopsAtPercent = 70;

        /// <summary>True165 (veteran review Q33): worn gear is worth a town errand below 88% (period: repair before 90%).</summary>
        public const int RepairErrandBelowPercent = 88;

        /// <summary>True165: in town everything below 90% is repaired.</summary>
        public const int RepairBelowPercent = 90;

        /// <summary>True165: one repair errand per bot every 30 minutes, so a town without a smith cannot loop it.</summary>
        public const long ErrandCooldownMilliseconds = 30 * 60_000;

        /// <summary>True165: the smith or crafter gamebot is looked for within this radius of the bot, in one scan.</summary>
        public const int ServiceRadius = 3000;

        private static readonly ConcurrentDictionary<long, long> LastErrand = new();
        private static readonly ConcurrentDictionary<long, string> RepairedAtBreak = new();

        /// <summary>The game time (tests replace it).</summary>
        public static Func<long> Now = () => GameLoop.GameLoopTime;

        /// <summary>The wear roll (tests replace it): true on item_condition_loss_chance percent.</summary>
        public static Func<bool> WearRoll = () => Util.Chance(ServerProperties.Properties.ITEM_CONDITION_LOSS_CHANCE);

        // ---- the hour's sums (ECON_WEAR) ----
        internal static readonly BotSinks.HourlySums WearSums = new();
        internal static readonly BotSinks.HourlySums CrossedSums = new();
        internal static readonly BotSinks.HourlySums RepairSums = new();
        internal static readonly BotSinks.HourlySums CrafterRepairSums = new();

        #region Wear

        /// <summary>Pure: whether a piece at this condition can wear (it has condition, above 70%).</summary>
        public static bool CanWear(int maxCondition, int conditionPercent) => maxCondition > 0 && conditionPercent > WearStopsAtPercent;

        /// <summary>
        /// A gamebot's weapon or armor piece was used (GameInventoryItem.OnBotItemUsed): on the wear roll it loses the
        /// player's amount. Cheap: one comparison unless bot_upkeep is on; the save is queued only when the percent drops.
        /// </summary>
        public static void Wear(GameBot bot, DbInventoryItem item)
        {
            if (!BotSinks.Upkeep(bot) || item == null || item.Template == null)
                return;
            int before = item.ConditionPercent;
            if (!CanWear(item.MaxCondition, before) || !WearRoll())
                return;
            int loss = GameInventoryItem.ConditionLoss(bot.Level, item.Level, before);
            item.Condition = Math.Max(0, item.Condition - loss);
            item.Dirty = true;
            WearSums.Add(bot.Realm, loss);
            int after = item.ConditionPercent;
            if (after == before)
                return;
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            if (before >= RepairErrandBelowPercent && after < RepairErrandBelowPercent)
            {
                CrossedSums.Add(bot.Realm, 0);
                // The cached service choice is asked again: the repair errand may now want a merchant.
                AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
            }
        }

        #endregion

        #region Repair rules (pure)

        /// <summary>Whether a piece is worn equipment below a percent that can still be repaired.</summary>
        public static bool IsWornBelow(DbInventoryItem item, int belowPercent) =>
            item != null && item.Template != null && item.MaxCondition > 0 && item.Condition < item.MaxCondition &&
            item.Durability > 0 && item.ConditionPercent < belowPercent &&
            item.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable;

        /// <summary>The equipped pieces below a percent, worst first.</summary>
        public static List<DbInventoryItem> WornPieces(IEnumerable<DbInventoryItem> items, int belowPercent) =>
            (items ?? Enumerable.Empty<DbInventoryItem>()).Where(item => IsWornBelow(item, belowPercent))
                .OrderBy(item => item.ConditionPercent).ThenBy(item => item.SlotPosition).ToList();

        /// <summary>What a repair costs a gamebot: the smith's price under the player's upkeep mode (light: half).</summary>
        public static long PriceOf(DbInventoryItem item, UpkeepPolicy.Mode mode) =>
            item?.Template == null || item.Template.MaxCondition <= 0 ? 0 : UpkeepPolicy.RepairCost(Math.Max(0, item.RepairCost), mode);

        /// <summary>True165's errand rule: the worst piece is below 88% and the bot can pay for it.</summary>
        public static bool WantsErrand(IEnumerable<DbInventoryItem> items, long coin, UpkeepPolicy.Mode mode)
        {
            DbInventoryItem worst = WornPieces(items, RepairErrandBelowPercent).FirstOrDefault();
            return worst != null && coin >= PriceOf(worst, mode);
        }

        /// <summary>True165: the player repair roll (Repair.CalculateSuccessChances) for a skill value, 0-100.</summary>
        public static int RepairChance(int skill, DbInventoryItem item)
        {
            int craftLevel = item == null ? 0 : CraftingMgr.GetItemCraftLevel(item);
            if (skill <= 0 || craftLevel <= 0)
                return 0;
            return Math.Clamp((int)(90 / (craftLevel * 0.5) * skill) - 80, 0, 100);
        }

        /// <summary>True165: whether a craft works the material a repair needs (weaponcraft: metal, wood, leather;
        /// armorcraft: metal, leather, cloth; tailoring: cloth, leather; fletching and siegecraft: wood).</summary>
        public static bool WorksMaterial(eCraftingSkill craft, eCraftingSkill secondary) => craft switch
        {
            eCraftingSkill.WeaponCrafting => secondary is eCraftingSkill.MetalWorking or eCraftingSkill.WoodWorking or eCraftingSkill.LeatherCrafting,
            eCraftingSkill.ArmorCrafting => secondary is eCraftingSkill.MetalWorking or eCraftingSkill.LeatherCrafting or eCraftingSkill.ClothWorking,
            eCraftingSkill.Tailoring => secondary is eCraftingSkill.ClothWorking or eCraftingSkill.LeatherCrafting,
            eCraftingSkill.Fletching or eCraftingSkill.SiegeCrafting => secondary is eCraftingSkill.WoodWorking,
            _ => false,
        };

        /// <summary>
        /// A gamebot's skill in the secondary skill a repair needs, from its saved skills: the secondary's own value, or its
        /// primary craft's value when that craft works the material (True165: secondaries grow with the main craft).
        /// </summary>
        public static int RepairSkill(string savedSkills, eCraftingSkill secondary)
        {
            if (string.IsNullOrWhiteSpace(savedSkills) || secondary == eCraftingSkill.NoCrafting)
                return 0;
            int own = BotCraftingSkills.Value(savedSkills, secondary);
            eCraftingSkill primary = BotCraftingSkills.Primary(savedSkills);
            int fromPrimary = WorksMaterial(primary, secondary) ? BotCraftingSkills.Value(savedSkills, primary) : 0;
            return Math.Max(own, fromPrimary);
        }

        #endregion

        #region The errand and the repair (glue)

        private static IEnumerable<DbInventoryItem> Equipped(GameBot bot) =>
            bot?.Inventory?.AllItems?.Where(item => item != null &&
                item.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable) ??
            Enumerable.Empty<DbInventoryItem>();

        private static bool CoolingDown(GameBot bot) =>
            LastErrand.TryGetValue(bot.DatabaseID, out long last) && Now() - last < ErrandCooldownMilliseconds;

        /// <summary>The errand was run (True165's Visited): no other repair errand for 30 minutes.</summary>
        public static void ErrandDone(GameBot bot)
        {
            if (bot == null)
                return;
            LastErrand[bot.DatabaseID] = Now();
            AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);
        }

        /// <summary>The repair errand (True165's "repair" town reason) as WP0's town-visit need.</summary>
        public sealed class Errand : IBotTownVisitNeed
        {
            public static readonly Errand Instance = new();
            public string Name => "repair errand (WP7)";

            public bool WantsVisit(GameBot bot) =>
                BotSinks.Upkeep(bot) && bot.Inventory != null && !CoolingDown(bot) &&
                WantsErrand(Equipped(bot), BotSinks.CoinOf(bot.DatabaseID), UpkeepPolicy.Current);

            public eWorldServiceKind? NeededService(GameBot bot) => WantsVisit(bot) ? eWorldServiceKind.Vendor : null;
        }

        /// <summary>At the merchant of a task break: the repairs (once a break).</summary>
        public sealed class Step : IBotServiceStep
        {
            public static readonly Step Instance = new();
            public string Name => "repairs (WP7)";
            public int Order => 100;
            public eWorldServiceKind Kind => eWorldServiceKind.Vendor;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                if (!BotSinks.Upkeep(bot) || bot.Inventory == null)
                    return false;
                string breakId = BotSinks.BreakOf(bot);
                if (RepairedAtBreak.TryGetValue(bot.DatabaseID, out string done) && done == breakId)
                    return false;
                List<DbInventoryItem> worn = WornPieces(Equipped(bot), RepairBelowPercent);
                if (worn.Count == 0)
                    return false;
                RepairedAtBreak[bot.DatabaseID] = breakId;
                Outcome outcome = Repair(bot, worn, atTown: true);
                ErrandDone(bot);
                if (outcome.Pieces == 0)
                    return false;
                result = new BotServiceResult($"Had {outcome.Pieces} worn pieces repaired" + (outcome.By.Length > 0 ? $" by {outcome.By}" : string.Empty),
                    $"Paid {outcome.Copper} saved copper for repairs; {outcome.Own} mended with its own skill", 3_000,
                    eAutonomousProgressKind.Money);
                return true;
            }
        }

        public readonly record struct Outcome(int Own, int Paid, long Copper, string By, int Left)
        {
            public int Pieces => Own + Paid;
        }

        /// <summary>
        /// True165's TryBotRepair: own skill first (a failed roll wastes the try), then in town the nearer of a smith or a
        /// crafter gamebot that repairs the piece every time, paying each piece's price worst first as far as the coin goes.
        /// </summary>
        public static Outcome Repair(GameBot bot, List<DbInventoryItem> worn, bool atTown)
        {
            UpkeepPolicy.Mode mode = UpkeepPolicy.Current;
            Blacksmith smith = null;
            List<GameBot> crafters = null; // one NPC scan per errand, not one per piece (True165, veteran review Q33)
            if (atTown)
            {
                foreach (GameNPC npc in bot.GetNPCsInRadius(ServiceRadius))
                {
                    if (npc is Blacksmith b && (smith == null || bot.GetDistanceTo(b) < bot.GetDistanceTo(smith)))
                        smith = b;
                    else if (npc is GameBot c && c != bot && c.IsAlive && c.Realm == bot.Realm && !c.InCombat &&
                             AutonomousBotCrafting.IsCrafter(c) && AutonomousBotCrafting.IsTrained(c))
                        (crafters ??= new List<GameBot>()).Add(c);
                }
                crafters?.Sort((a, b) => bot.GetDistanceTo(a).CompareTo(bot.GetDistanceTo(b)));
            }
            string ownSkills = AutonomousBotCrafting.IsCrafter(bot) ? bot.PersistentRecord?.SerializedCraftingSkills : null;
            int own = 0, paid = 0, left = 0;
            long spent = 0;
            var paidTo = new List<string>();
            foreach (DbInventoryItem item in worn)
            {
                eCraftingSkill secondary = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
                int ownChance = RepairChance(RepairSkill(ownSkills, secondary), item);
                if (ownChance > 0)
                {
                    if (Util.Chance(ownChance))
                    {
                        Repaired(item);
                        own++;
                    }
                    continue;
                }
                if (!atTown)
                    continue;
                GameBot crafter = FindCrafter(crafters, item, secondary);
                bool useCrafter = crafter != null && (smith == null || bot.GetDistanceTo(crafter) < bot.GetDistanceTo(smith));
                if (!useCrafter && smith == null)
                {
                    left++;
                    continue;
                }
                long price = PriceOf(item, mode);
                if (!BotSinks.Spend(bot.DatabaseID, price, EconomyFlowKind.Repair))
                {
                    left++;
                    break;
                }
                if (useCrafter)
                {
                    // A bot-to-bot payment: both sides are Repair, so the realm's net is what the smiths took.
                    BotSinks.Pay(crafter.DatabaseID, price, EconomyFlowKind.Repair);
                    CrafterRepairSums.Add(bot.Realm, price);
                    if (!paidTo.Contains(crafter.Name))
                        paidTo.Add(crafter.Name);
                }
                else if (!paidTo.Contains(smith.Name))
                    paidTo.Add(smith.Name);
                Repaired(item);
                paid++;
                spent += price;
            }
            var outcome = new Outcome(own, paid, spent, string.Join(",", paidTo), left);
            if (outcome.Pieces > 0)
            {
                RepairSums.Add(bot.Realm, spent, outcome.Pieces);
                bot.MarkAutonomousStateDirty();
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                AutonomousBotStatusPersistence.Queue(bot, true);
            }
            if (Log.IsInfoEnabled)
                Log.Info(RepairLine(bot, worn.Count > 0 ? worn[0].ConditionPercent : 100, outcome, smith != null));
            return outcome;
        }

        /// <summary>The ECON_REPAIR line, with True165's BOT_REPAIRED_165 keys (own_skill, paid, copper, by).</summary>
        public static string RepairLine(GameBot bot, int worstPercent, Outcome outcome, bool smithNear) =>
            EconomyLog.Line(EconomyLog.EconRepair, ("bot", bot?.Name), ("id", bot?.DatabaseID ?? 0), ("realm", bot?.Realm ?? eRealm.None),
                ("level", bot?.Level ?? 0), ("worst", worstPercent), ("own_skill", outcome.Own), ("paid", outcome.Paid),
                ("copper", outcome.Copper), ("by", outcome.By), ("left", outcome.Left), ("smith", smithNear),
                ("upkeep", UpkeepPolicy.Current.ToString().ToLowerInvariant()));

        private static void Repaired(DbInventoryItem item)
        {
            // The player's rule: light and free keep durability, classic spends it (WP1's Repair).
            DOL.GS.Repair.ModifyConditionAndDurability(item);
            item.Dirty = true;
        }

        /// <summary>The nearest crafter gamebot (from the one scan) that repairs this piece every time.</summary>
        private static GameBot FindCrafter(List<GameBot> crafters, DbInventoryItem item, eCraftingSkill secondary)
        {
            if (crafters == null || secondary == eCraftingSkill.NoCrafting)
                return null;
            foreach (GameBot c in crafters)
                if (RepairChance(RepairSkill(c.PersistentRecord?.SerializedCraftingSkills, secondary), item) >= 100)
                    return c;
            return null;
        }

        #endregion

        /// <summary>Tests: forget the errand and break marks and the sums.</summary>
        public static void ResetForTests()
        {
            LastErrand.Clear();
            RepairedAtBreak.Clear();
            WearSums.Take();
            CrossedSums.Take();
            RepairSums.Take();
            CrafterRepairSums.Take();
            Now = () => GameLoop.GameLoopTime;
            WearRoll = () => Util.Chance(ServerProperties.Properties.ITEM_CONDITION_LOSS_CHANCE);
        }
    }
}
