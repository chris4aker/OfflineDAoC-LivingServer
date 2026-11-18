using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Persistence bridge for independent world bots. Copper is always read from and
/// written to offline_world_bots; exchange inventory is always a real Inventory row.
/// </summary>
    public static class AutonomousBotEconomy
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private const string OwnerPrefix = "offlinebot:";
        private const int MinimumEquipmentUpgrade = 8;
        // Owner 2026-10-29: 3 posts per bot (was 1; Codex's 10 made a launcher refresh take minutes before the launcher split
        // bot snapshots from the exchange). Players stay unlimited.
        public const int MaxBotRealmExchangeListings = 3;
        private const int ServiceDecisionSafetyRefreshMilliseconds = 5 * 60_000;
        private sealed class ServiceDecisionState
        {
            public readonly object Gate = new();
            public int Revision;
            public int EvaluatedRevision = -1;
            public long NextSafetyRefreshTick;
            public eWorldServiceKind? CachedService;
        }
        private static readonly ConcurrentDictionary<long, ServiceDecisionState> ServiceDecisions = new();
        public sealed record ListingCandidate(DbInventoryItem Item, int PriceCopper);
    public sealed record PurchaseCandidate(DbInventoryItem Item, int Utility, eInventorySlot EquipSlot, bool IsEquipmentUpgrade, bool IsPlayerListing);

    public static string GetOwnerId(long botId) => $"{OwnerPrefix}{botId}";

    /// <summary>
    /// Invalidates only this bot's cached appraisal. Loot, purchases, sales and
    /// equipment moves call this at the mutation site, so ordinary AI turns do
    /// not rescan the entire backpack and Realm Exchange every 1.5 seconds.
    /// </summary>
    public static void MarkInventoryChanged(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.DatabaseID <= 0)
            return;
        Interlocked.Increment(ref ServiceDecisions.GetOrAdd(bot.DatabaseID, _ => new ServiceDecisionState()).Revision);
    }

    public static void MarkEconomyChanged(long botId)
    {
        if (botId <= 0)
            return;
        Interlocked.Increment(ref ServiceDecisions.GetOrAdd(botId, _ => new ServiceDecisionState()).Revision);
    }

    public static void ForgetCachedDecision(long botId)
    {
        if (botId > 0)
            ServiceDecisions.TryRemove(botId, out _);
    }

    public static bool TryParseOwnerId(string ownerId, out long botId)
    {
        botId = 0;
        return ownerId != null && ownerId.StartsWith(OwnerPrefix, StringComparison.OrdinalIgnoreCase) &&
               long.TryParse(ownerId.AsSpan(OwnerPrefix.Length), out botId) && botId > 0;
    }

    public static long GetMoney(long botId) => FindLiveOrStored(botId)?.MoneyCopper ?? 0;

    /// <summary>Takes coin from a gamebot's saved purse. The optional kind and the compiler-filled caller let the
    /// economy ledger (Crafting 2.0 WP2) attribute the move without any call-site edit; no amount depends on them.</summary>
    public static bool TrySpend(long botId, long copper, EconomyFlowKind kind = EconomyFlowKind.Unattributed,
        [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "")
    {
        if (copper < 0)
            return false;

        bool spent = TrySpendCore(botId, copper);
        if (spent)
            EconomyFlows.Record(botId, -copper, kind, callerFile, callerMember);
        return spent;
    }

    private static bool TrySpendCore(long botId, long copper)
    {
        lock (typeof(AutonomousBotEconomy))
        {
            bool isLive = AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.PersistentRecord != null;
            OfflineWorldBotRecord bot = isLive ? live.PersistentRecord : Find(botId);
            if (bot == null || bot.MoneyCopper < copper)
                return false;

            bot.MoneyCopper -= copper;
            bot.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            bot.Dirty = true;
            if (isLive)
                AutonomousBotStatusPersistence.Queue(live);
            else
            {
                using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                    GameServer.Database.SaveObject(bot);
            }
            return true;
        }
    }

    /// <summary>Adds coin to a gamebot's saved purse (see <see cref="TrySpend"/> for the ledger parameters).</summary>
    public static bool AddMoney(long botId, long copper, EconomyFlowKind kind = EconomyFlowKind.Unattributed,
        [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "")
    {
        if (copper < 0)
            return false;

        bool added = AddMoneyCore(botId, copper);
        if (added)
            EconomyFlows.Record(botId, copper, kind, callerFile, callerMember);
        return added;
    }

    private static bool AddMoneyCore(long botId, long copper)
    {
        lock (typeof(AutonomousBotEconomy))
        {
            bool isLive = AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.PersistentRecord != null;
            OfflineWorldBotRecord bot = isLive ? live.PersistentRecord : Find(botId);
            if (bot == null || bot.MoneyCopper > long.MaxValue - copper)
                return false;

            bot.MoneyCopper += copper;
            bot.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            bot.Dirty = true;
            if (isLive)
                AutonomousBotStatusPersistence.Queue(live);
            else
            {
                using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                    GameServer.Database.SaveObject(bot);
            }
            return true;
        }
    }

    public static DbInventoryItem[] FindAffordableExchangeItems(GameBot bot, int maximumResults = 24)
    {
        if (bot == null || bot.DatabaseID <= 0 || maximumResults <= 0)
            return Array.Empty<DbInventoryItem>();

        ushort lot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
        long money = GetMoney(bot.DatabaseID);
        ItemQuery query = new() { Realm = bot.Realm };
        return MarketCache.SearchItems(query)
            .Where(item => item.OwnerLot == lot && item.IsTradable && item.SellPrice > 0 && item.SellPrice <= money &&
                item.OwnerID != GetOwnerId(bot.DatabaseID) && !RealmExchangeExpiry.IsExpired(item, DateTime.UtcNow))
            .OrderBy(item => item.SellPrice)
            .Take(maximumResults)
            .ToArray();
    }

    public static PurchaseCandidate FindBestUsefulExchangePurchase(GameBot bot, int maximumResults = 48)
    {
        using var aiProfile = AiProfile.Scope(AiSystem.EconomyValuation); // AI_PROFILE: measures only
        if (bot?.Inventory == null || bot.DatabaseID <= 0)
            return null;

        bool hasCraft = !string.IsNullOrWhiteSpace(bot.PersistentRecord?.SerializedCraftingSkills);
        // Appraise matching goods before limiting results. Cheap unusable bot
        // wares must not hide a useful player listing beyond the first 48 rows.
        return FindAffordableExchangeItems(bot, int.MaxValue)
            .Select(item => ScorePurchase(bot, item, hasCraft))
            .Where(candidate => candidate != null && candidate.Utility > 0)
            .OrderByDescending(candidate => candidate.Utility)
            .ThenBy(candidate => candidate.Item.SellPrice)
            .FirstOrDefault();
    }

        private static PurchaseCandidate ScorePurchase(GameBot bot, DbInventoryItem item, bool hasCraft)
        {
            if (item == null || item.Template == null || item.LevelRequirement > bot.Level ||
                !BotWeaponStats.HasFunctionalMeleeStats(item) ||
                (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 !BotWeaponStats.IsConfiguredMeleeWeapon(bot, item.Template)) ||
                !GameServer.ServerRules.CheckAbilityToUseItem(bot, item.Template))
                return null;

        eInventorySlot equipSlot = ResolveEquipmentSlot(bot, item);
        bool isPlayerListing = IsPlayerListing(item.OwnerID);
        int playerPreference = isPlayerListing ? 650 : 0;
        if (equipSlot != eInventorySlot.Invalid)
        {
            DbInventoryItem equipped = bot.Inventory.GetItem(equipSlot);
            int improvement = EquipmentValue(item) - EquipmentValue(equipped);
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
            {
                double gain = SavageBotWeaponPolicy.UpgradeGain(bot, item, equipSlot);
                bool gearValuation = UsesGearValuation(bot);
                if (SavageWeaponUpgrade(gain, gearValuation, BotGearSet.IsScoredSlot(equipSlot),
                        gearValuation && BotGearSet.IsScoredSlot(equipSlot) ? GearImprovement(bot, item, equipSlot) : 0))
                    return new PurchaseCandidate(item,
                        1_000 + (int)Math.Clamp(Math.Ceiling(gain * 100), 1, int.MaxValue - 1_000),
                        equipSlot, true, isPlayerListing);
                return null;
            }
            // Crafting 2.0 WP6 (bot_crafters on): the template-aware set score decides, and an empty slot is filled only by
            // a piece that raises it, so nothing bought here is refused by the no-undress guard when it is put on.
            if (UsesGearValuation(bot))
            {
                long gain = GearImprovement(bot, item, equipSlot);
                if (gain > 0 && (equipped == null || gain > MinimumEquipmentUpgrade))
                    return new PurchaseCandidate(item,
                        1_000 + (int)Math.Clamp(gain, 1, 1_000_000) + (equipped == null ? 250 : 0) + playerPreference,
                        equipSlot, true, isPlayerListing);
            }
            else
            {
                if (equipped == null)
                    improvement += 250;
                if (improvement > 8)
                    return new PurchaseCandidate(item, 1_000 + improvement + playerPreference, equipSlot, true, isPlayerListing);
            }
        }

        // Stackable world goods are the server's practical crafting-material
        // signal. Only trained crafters buy them, and they remain real backpack
        // inventory until an actual recipe consumes them.
        if (item.IsStackable && item.Count > 0 && EconomySeams.IsMaterialFor(bot, item, hasCraft))
            return new PurchaseCandidate(item, 300 + Math.Min(150, item.Count * 3) + playerPreference, eInventorySlot.Invalid, false, isPlayerListing);

        return null;
    }

        public static int EquipmentValue(DbInventoryItem item)
    {
        if (item == null || !BotWeaponStats.HasFunctionalMeleeStats(item))
            return 0;
        int utility = Math.Abs(item.Bonus) + Math.Abs(item.ExtraBonus) + Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) +
                      Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) + Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) +
                      Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) + Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10);
        return Math.Max(1, item.Level) * 10 + Math.Max(1, item.Quality) + Math.Max(0, item.DPS_AF) * 2 + utility * 3;
    }

        /// <summary>
        /// From shadowofze (1edbcf5), via True165, GPL-3.0: EquipmentValue with each typed bonus weighed for the bot's class
        /// and counted only up to its cap, and level and DPS/AF only up to what the bot can use (BotGearValue165). True165
        /// also added WeaponSpecBonus and WastedFocus (earlier True165 work, not ported). EquipmentValue itself is unchanged
        /// (logs, listing and the vendor-trash order still read it). Read only while <see cref="UsesGearValuation"/>.
        /// </summary>
        public static int FitValue(GameBot bot, DbInventoryItem item, eInventorySlot slot)
        {
            if (item == null)
                return 0;
            int equipment = EquipmentValue(item);
            // EquipmentValue is 0 for a melee weapon without working stats: nothing to adjust
            if (equipment == 0 || bot?.Inventory == null)
                return equipment;
            DbInventoryItem current = slot == eInventorySlot.Invalid ? null : bot.Inventory.GetItem(slot);
            return equipment + BotGearValue165.Adjustment(bot, item.Level, item.DPS_AF, item.Object_Type, BotGearValue165.Bonuses(item), current);
        }

        /// <summary>
        /// Crafting 2.0 WP6: whether this gamebot's gear decisions use the template-aware set score (BotGearSet) and the
        /// no-undress guard. Only with bot_crafters on, and only for an autonomous gamebot that is saved (not a companion,
        /// an Epic Special Forces bot or a No Lifer buffbot) and has a class. Off: today's EquipmentValue rules, unchanged.
        /// </summary>
        public static bool UsesGearValuation(GameBot bot) =>
            CraftingEconomyProperties.BOT_CRAFTERS && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
            !bot.IsInMemoryOnly && bot.CharacterClass != null && bot.Inventory != null;

        /// <summary>How much wearing the item in that slot raises the bot's set score (BotGearSet.Delta; negative when it
        /// lowers it).</summary>
        public static long GearImprovement(GameBot bot, DbInventoryItem item, eInventorySlot slot) =>
            item == null || !BotGearSet.IsScoredSlot(slot) ? 0
                : BotGearSet.Delta(BotGearSet.For(bot), BotGearSet.Worn(bot), slot, item);

        /// <summary>
        /// A Savage's melee weapon is an upgrade (pure): its weapon policy gains (SavageBotWeaponPolicy.UpgradeGain > 0) and,
        /// with the template-aware set score on and a scored slot, wearing it does not lower that score (the no-undress
        /// guard's own rule, BotGearGuard). The one rule of the loot, merchant and Realm Exchange paths. Owner's PC, build
        /// b1b6846, 2026-11-18: the Savage "Fre" bought a "Lifetaker Greatsword" for 39,863 copper that its weapon policy
        /// rated higher than its "Sturdy Greatsword" while the set score fell 11 points (14,934 to 14,923); the Exchange's
        /// buy side asked the weapon policy only, and the guard refused to put it on (ECON_UNDRESS_GUARD event="blocked").
        /// </summary>
        public static bool SavageWeaponUpgrade(double weaponGain, bool gearValuation, bool scoredSlot, long setGain) =>
            weaponGain > 0 && (!gearValuation || !scoredSlot || setGain >= 0);

        public static bool IsPlayerListing(string ownerId) =>
            !string.IsNullOrWhiteSpace(ownerId) && !TryParseOwnerId(ownerId, out _);

        /// <summary>
        /// Returns the real equipment destination only when this bot can use the item and it is
        /// materially better than the item presently worn there.  This is shared by loot,
        /// merchant, and Realm Exchange paths so they cannot accidentally vendor a usable upgrade.
        /// </summary>
        public static bool TryGetEquipmentUpgrade(GameBot bot, DbInventoryItem item, out eInventorySlot equipSlot)
        {
            equipSlot = eInventorySlot.Invalid;
            // A seam (Crafting 2.0): an item a package holds back (a piece made for another bot's order) is never worn.
            if (bot?.Inventory == null || !CanUseEquipment(bot, item) || EconomySeams.IsReserved(bot, item))
                return false;

            equipSlot = ResolveEquipmentSlot(bot, item);
            if (equipSlot == eInventorySlot.Invalid)
                return false;

            DbInventoryItem equipped = bot.Inventory.GetItem(equipSlot);
            if (DisplacesOffhandWeapon(item, equipped))
                return false;
            bool gearValuation = UsesGearValuation(bot);
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                return SavageWeaponUpgrade(SavageBotWeaponPolicy.UpgradeGain(bot, item, equipSlot), gearValuation,
                    BotGearSet.IsScoredSlot(equipSlot),
                    gearValuation && BotGearSet.IsScoredSlot(equipSlot) ? GearImprovement(bot, item, equipSlot) : 0);
            // Crafting 2.0 WP6 (bot_crafters on): the template-aware set score decides. A piece must raise it (an empty
            // slot) or raise it by more than the usual margin (a worn slot), so the no-undress guard never has to refuse.
            if (gearValuation && BotGearSet.IsScoredSlot(equipSlot))
            {
                long gain = GearImprovement(bot, item, equipSlot);
                return equipped == null ? gain > 0 : gain > MinimumEquipmentUpgrade;
            }
            int improvement = EquipmentValue(item) - EquipmentValue(equipped);
            return equipped == null || improvement > MinimumEquipmentUpgrade;
        }

        /// <summary>
        /// Whether this bot may wear or wield the item at all: its level requirement, real weapon stats, the build's
        /// configured weapon and proficiency, a usable ranged weapon, and the server's own class/realm/armour rules.
        /// Shared by every upgrade path and by the starter gear (AutonomousStarterGear), so none can give a bot an item
        /// the others would refuse.
        /// </summary>
        public static bool CanUseEquipment(GameBot bot, DbInventoryItem item) =>
            bot != null && item?.Template != null &&
            (eObjectType)item.Object_Type != eObjectType.GenericItem && item.LevelRequirement <= bot.Level &&
            BotWeaponStats.HasFunctionalMeleeStats(item) &&
            !(BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
              (!BotWeaponStats.IsConfiguredMeleeWeapon(bot, item.Template) ||
               !BotWeaponStats.HasConfiguredWeaponProficiency(bot, item.Template))) &&
            !(BotRangedCombat.IsRangedWeaponType((eObjectType)item.Object_Type) && !BotRangedCombat.IsUsableWeapon(item)) &&
            !(!BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
              !GameServer.ServerRules.CheckAbilityToUseItem(bot, item.Template));

        /// <summary>
        /// Whether the bot can wear this item at all (class, level, weapon proficiency and its slots), and where. From
        /// shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0: hired companions wear what their player hands them,
        /// upgrade or not. A player can put on an item above his level (it only works fully once he reaches it), so a
        /// hired companion can too; a gamebot buying for itself still waits.
        /// </summary>
        public static bool CanWear(GameBot bot, DbInventoryItem item, out eInventorySlot equipSlot)
        {
            equipSlot = eInventorySlot.Invalid;
            if (bot?.Inventory == null || item?.Template == null ||
                (eObjectType)item.Object_Type == eObjectType.GenericItem ||
                item.LevelRequirement > bot.Level && !bot.IsHiredCompanion ||
                (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 (!BotWeaponStats.IsConfiguredMeleeWeapon(bot, item.Template) ||
                  !BotWeaponStats.HasConfiguredWeaponProficiency(bot, item.Template))) ||
                (BotRangedCombat.IsRangedWeaponType((eObjectType)item.Object_Type) && !BotRangedCombat.IsUsableWeapon(item)) ||
                (!BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 !GameServer.ServerRules.CheckAbilityToUseItem(bot, item.Template)))
                return false;
            equipSlot = ResolveEquipmentSlot(bot, item);
            return equipSlot != eInventorySlot.Invalid;
        }

        /// <summary>
        /// Whether an item the bot can wear (CanWear passed) may go in the slot the player dropped it on (True 1.65). The
        /// same slot rules as a player's own equipment (GamePlayerInventory): the main hand takes a one-handed weapon of
        /// either hand, never a shield; the left hand only an either-hand item - a shield, or a weapon for a class that
        /// fights with a left-hand weapon; the two-hand slot a two-hander or an instrument; the ranged slot a ranged weapon
        /// or an instrument; rings and bracers either side; armour only its own slot.
        /// </summary>
        public static bool FitsChosenSlot(GameBot bot, DbInventoryItem item, int wanted)
        {
            if (bot == null || item == null || wanted < (int)eInventorySlot.MinEquipable || wanted > (int)eInventorySlot.MaxEquipable)
                return false;
            var slot = (eInventorySlot)wanted;
            var own = (eInventorySlot)item.Item_Type;
            var type = (eObjectType)item.Object_Type;
            bool fits = slot switch
            {
                eInventorySlot.RightHandWeapon => type != eObjectType.Shield && own is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon,
                eInventorySlot.LeftHandWeapon => own == eInventorySlot.LeftHandWeapon && (type == eObjectType.Shield || LeftHandWeaponAllowed(bot, item)),
                eInventorySlot.TwoHandWeapon => type != eObjectType.Shield && (own == eInventorySlot.TwoHandWeapon || type == eObjectType.Instrument),
                eInventorySlot.DistanceWeapon => own == eInventorySlot.DistanceWeapon || type == eObjectType.Instrument,
                eInventorySlot.LeftRing or eInventorySlot.RightRing => own is eInventorySlot.LeftRing or eInventorySlot.RightRing,
                eInventorySlot.LeftBracer or eInventorySlot.RightBracer => own is eInventorySlot.LeftBracer or eInventorySlot.RightBracer,
                _ => own == slot,
            };
            if (!fits)
                return false;
            // a melee weapon also has to be one the bot can fight with in that hand (skill, level, condition)
            return !BotWeaponStats.IsMeleeWeapon(type) || BotWeaponStats.FitsConfiguredSlot(bot, item, slot);
        }

        /// <summary>Whether the bot's class fights with a weapon in its left hand (dual wield, celtic dual, left axe,
        /// hand to hand): the same class flag that decides it for a player (True 1.65).</summary>
        public static bool CanUseOffhandWeapon(GameBot bot) => bot?.CharacterClass?.CanUseLefthandedWeapon == true;

        /// <summary>Whether this weapon can be the bot's left-hand weapon (True 1.65): its class fights left-handed, with the
        /// left-hand line's weapons - Left Axe (Berserker, Shadowblade) only axes, Hand to Hand (Savage) only hand-to-hand
        /// weapons; Dual Wield and Celtic Dual any either-hand weapon.</summary>
        public static bool LeftHandWeaponAllowed(GameBot bot, DbInventoryItem item)
        {
            if (item == null || !CanUseOffhandWeapon(bot) || (eInventorySlot)item.Item_Type != eInventorySlot.LeftHandWeapon)
                return false;
            var type = (eObjectType)item.Object_Type;
            return bot.CharacterClass.ID switch
            {
                (int)eCharacterClass.Berserker or (int)eCharacterClass.Shadowblade => type is eObjectType.Axe or eObjectType.LeftAxe,
                (int)eCharacterClass.Savage => type == eObjectType.HandToHand,
                _ => true,
            };
        }

        /// <summary>
        /// A hired companion's melee weapon left in the left hand of a class that can't use it moves to the empty main hand,
        /// so it fights with it (True 1.65). True when it moved. Only hired companions: a gamebot's hands are unchanged.
        /// </summary>
        public static bool MoveStrayOffhandWeapon(GameBot bot)
        {
            if (bot?.Inventory == null || !bot.IsHiredCompanion || CanUseOffhandWeapon(bot))
                return false;
            DbInventoryItem left = bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            if (left == null || !BotWeaponStats.IsMeleeWeapon((eObjectType)left.Object_Type) ||
                left.Item_Type == Slot.TWOHAND || left.Hand == 1 ||
                bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) != null)
                return false;
            if (!bot.Inventory.MoveItem(eInventorySlot.LeftHandWeapon, eInventorySlot.RightHandWeapon, 1))
                return false;
            bot.RefreshItemBonuses();
            if (!bot.IsAttacking && bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard)
                bot.SwitchWeapon(eActiveWeaponSlot.Standard);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

        /// <summary>
        /// Compares against what the move actually displaces (the idea behind the
        /// stefanrows/OfflineDAoC fork's shield/two-hander loop fix, 0b76744). A shield is
        /// never an upgrade over a weapon the build wields in its left hand: Savages swapped a
        /// shield and their hand-to-hand offhand back and forth every 11 seconds (up to 177
        /// times per bot in one log), saving inventory each time.
        /// </summary>
        public static bool DisplacesOffhandWeapon(DbInventoryItem item, DbInventoryItem equipped) =>
            item != null && equipped != null && (eObjectType)item.Object_Type == eObjectType.Shield &&
            BotWeaponStats.IsMeleeWeapon((eObjectType)equipped.Object_Type);

        /// <summary>
        /// Equips an already-owned backpack item. MoveItem swaps an existing equipped item back
        /// into the source backpack slot, so an upgrade is safe even when the backpack is full.
        /// </summary>
        public static bool TryEquipOwnedUpgrade(GameBot bot, DbInventoryItem item, string source = "inventory")
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                item == null || !bot.Inventory.AllItems.Contains(item) ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
                !TryGetEquipmentUpgrade(bot, item, out eInventorySlot equipSlot))
                return false;

            eInventorySlot backpackSlot = (eInventorySlot)item.SlotPosition;
            DbInventoryItem replaced = bot.Inventory.GetItem(equipSlot);
            // Crafting 2.0 WP6: the no-undress guard (bot_crafters on; always passes otherwise).
            if (!BotGearGuard.Allows(bot, item, equipSlot, source))
                return false;
            if (!bot.Inventory.MoveItem(backpackSlot, equipSlot, Math.Max(1, item.Count)))
                return false;

            bot.RefreshItemBonuses();
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                bot.EnsureBotWeaponReady();
            MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            LogEquipmentUpgrade(bot, item, replaced, equipSlot, source);
            return true;
        }

        /// <summary>
        /// Repairs an older, full backpack before any disposal decision. Equipping the best
        /// owned upgrade swaps the previous worn item into its backpack slot, where normal
        /// merchant appraisal can then decide whether it is actually surplus.
        /// </summary>
        public static bool TryEquipBestOwnedUpgrade(GameBot bot, out DbInventoryItem equipped)
        {
            equipped = null;
            if (bot?.IsAutonomousWorldBot != true || bot.Inventory == null)
                return false;

            DbInventoryItem candidate = bot.Inventory.AllItems
                .Where(item => item != null && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack &&
                               TryGetEquipmentUpgrade(bot, item, out _))
                .OrderByDescending(EquipmentValue)
                .ThenBy(item => item.SlotPosition)
                .FirstOrDefault();
            if (candidate == null || !TryEquipOwnedUpgrade(bot, candidate, "merchant-service"))
                return false;

            equipped = candidate;
            return true;
        }

        private static void LogEquipmentUpgrade(GameBot bot, DbInventoryItem item,
            DbInventoryItem replaced, eInventorySlot slot, string source)
        {
            if (!AutonomousDiagnosticsProperties.EquipmentUpgrades || bot?.IsAutonomousWorldBot != true ||
                bot.IsTemporaryGroupHelper) return;
            // The move has already succeeded. This is one outcome record, not
            // a scan or a second inventory transaction.
            try
            {
                Log.Info($"AUTONOMOUS_EQUIPMENT_UPGRADE bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                    $"class=\"{bot.ClassName}\" source={source} slot={slot} item=\"{item.Name}\" " +
                    $"itemId=\"{item.ObjectId}\" replaced=\"{replaced?.Name ?? "empty"}\" " +
                    $"oldValue={EquipmentValue(replaced)} newValue={EquipmentValue(item)}");
            }
            catch { }
        }

        /// <summary>
        /// Decides which real-world service should be routed to. This has no movement side effect;
        /// the world controller supplies the reachable NPC destination and BotBrain performs the
        /// transaction only after it is physically nearby.
        /// </summary>
        public static eWorldServiceKind? GetNeededService(GameBot bot)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.EconomyValuation); // AI_PROFILE: measures only
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                !AutonomousObjectiveAssignments.WantsBetweenTaskInventory(bot))
                return null;

            ServiceDecisionState state = ServiceDecisions.GetOrAdd(bot.DatabaseID, _ => new ServiceDecisionState());
            long now = GameLoop.GameLoopTime;
            int revision = Volatile.Read(ref state.Revision);
            if (Volatile.Read(ref state.EvaluatedRevision) == revision && now < Volatile.Read(ref state.NextSafetyRefreshTick))
                return state.CachedService;

            lock (state.Gate)
            {
                revision = Volatile.Read(ref state.Revision);
                if (state.EvaluatedRevision == revision && now < state.NextSafetyRefreshTick)
                    return state.CachedService;

                state.CachedService = EvaluateNeededService(bot);
                state.EvaluatedRevision = revision;
                // Deterministic staggering prevents a server-wide safety sweep.
                state.NextSafetyRefreshTick = now + ServiceDecisionSafetyRefreshMilliseconds + bot.DatabaseID % 90_000;
                return state.CachedService;
            }
        }

        private static eWorldServiceKind? EvaluateNeededService(GameBot bot)
        {
            // Choose the single listing from the complete backpack before any
            // vendor sale changes that candidate pool. Once the listing slot is
            // occupied, finish clearing every disposable item before shopping.
            // The candidate search applies each item's own listing cap (EconomySeams.Listings; 3 for every item by
            // default, so this is the old HasListingSpace check).
            if (FindValuableListingCandidate(bot) != null)
                return eWorldServiceKind.RealmExchange;

            if (FindVendorTrashCandidate(bot) != null)
                return eWorldServiceKind.Vendor;

            // A backpack filled entirely with quest/reputation tokens or other
            // non-vendorable objects still needs a real merchant visit. The
            // arrived-service path may clear that blocked backpack only after
            // confirming that no ordinary sale is possible.
            if (IsBackpackFull(bot))
                return eWorldServiceKind.Vendor;

            // The vendor phase has now made room. If the bot arrived with its
            // one listing slot already occupied, it naturally skipped the first
            // branch and reaches this realm-local upgrade appraisal directly.
            if (!IsBackpackFull(bot) && FindBestUsefulExchangePurchase(bot) != null)
                return eWorldServiceKind.RealmExchange;

            // In Darkness Falls, seals the bot has looted buy its realm's seal gear, or seal items to sell (owner 2026-10-30).
            if (!IsBackpackFull(bot) && HasSealPurchase(bot))
                return eWorldServiceKind.SealMerchant;

            // A seam (Crafting 2.0): a registered town-visit need (stock to sell, a gear gap, a repair); none by default.
            return BotServiceSeams.NeededService(bot);
        }

        public const int NearlyFullFreeSlots = 6;

        /// <summary>
        /// Used only at a natural task boundary (never mid-grind): with this few
        /// free slots left the next task would fill the bag and leave loot on
        /// the ground, so the bot unloads now. Selling still never interrupts a
        /// grind and is not triggered by ordinary partially filled bags.
        /// </summary>
        public static bool IsBackpackNearlyFull(GameBot bot)
        {
            if (bot?.Inventory == null)
                return false;
            int free = 0;
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
                if (bot.Inventory.GetItem(slot) == null && ++free > NearlyFullFreeSlots)
                    return false;
            return true;
        }

        public static bool IsBackpackFull(GameBot bot) =>
            bot?.Inventory != null &&
            bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid;

        /// <summary>
        /// Last-resort recovery for a persistent world bot that has physically
        /// reached a merchant with all 40 backpack slots occupied and nothing
        /// the merchant phase can sell. Only expendable backpack items are
        /// removed: worn and spare combat equipment (including bard/minstrel
        /// instruments), vaults, exchange listings, and saved copper remain.
        /// A caller must establish the live merchant/range and between-task
        /// service guards before passing arrivedAtMerchant=true.
        /// </summary>
        public static bool TryClearBlockedBackpackAtMerchant(GameBot bot, bool arrivedAtMerchant, out int removed)
        {
            removed = 0;
            if (!arrivedAtMerchant || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
                BotDutyLeases.IsSpokenForByPlayer(bot) || bot.Inventory == null || bot.DatabaseID <= 0 ||
                !IsBackpackFull(bot) || FindVendorTrashCandidate(bot) != null ||
                HasListingSpace(bot) && FindValuableListingCandidate(bot) != null)
                return false;

            DbInventoryItem[] backpack = bot.Inventory.AllItems
                .Where(item => item != null && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack)
                .ToArray();
            int capacity = (int)eInventorySlot.LastBackpack - (int)eInventorySlot.FirstBackpack + 1;
            if (backpack.Length != capacity || !IsBackpackFull(bot))
                return false;

            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            foreach (DbInventoryItem item in backpack.Where(item =>
                         !operationalLoadout.Contains(item) && !TryGetEquipmentUpgrade(bot, item, out _) &&
                         !EconomySeams.IsReserved(bot, item)))
            {
                if (bot.Inventory.RemoveItem(item))
                    removed++;
            }
            if (removed == 0)
                return false;

            bot.MarkAutonomousStateDirty();
            MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot, true);
            try
            {
                Log.Warn($"AUTONOMOUS_BLOCKED_BACKPACK_CLEARED bot=\"{bot.Name}\" id={bot.DatabaseID} removed={removed} capacity={capacity}");
            }
            catch { } // A diagnostics failure must not interrupt the persisted cleanup.
            return true;
        }

        /// <summary>
        /// Keep one best spare per usable weapon shape and one per instrument kind, not every
        /// usable weapon ever looted. A caster can otherwise retain forty obsolete focus
        /// staves, making both merchant sale and the blocked-backpack failsafe impossible.
        /// Equipped gear is outside the backpack and is never considered here.
        /// </summary>
        private static HashSet<DbInventoryItem> FindOperationalLoadoutItems(GameBot bot)
        {
            HashSet<DbInventoryItem> retained = new();
            if (bot?.Inventory == null)
                return retained;

            Dictionary<(int Kind, int Type, int Shape), DbInventoryItem> bestByRole = new();
            foreach (DbInventoryItem item in bot.Inventory.AllItems)
            {
                if (item == null || item.OwnerLot != 0 ||
                    item.SlotPosition < (int)eInventorySlot.FirstBackpack ||
                    item.SlotPosition > (int)eInventorySlot.LastBackpack)
                    continue;
                if (item is GameInventoryRelic || BotSiegeRuntime.IsSupply(item.Id_nb))
                {
                    retained.Add(item);
                    continue;
                }

                eObjectType type = (eObjectType)item.Object_Type;
                (int Kind, int Type, int Shape) role;
                if (type == eObjectType.Instrument)
                    // Lute/drum/flute use DPS_AF as the instrument kind; songs may
                    // pull these from a backpack even when another is equipped.
                    role = (1, item.DPS_AF, 0);
                else if (type == eObjectType.Shield)
                    role = (2, item.Item_Type, item.Hand);
                else if (BotWeaponStats.IsMeleeWeapon(type) && BotWeaponStats.CanUseMelee(bot, item))
                    role = (3, item.Object_Type, item.Item_Type);
                else if (BotRangedCombat.IsRangedWeaponType(type) && BotRangedCombat.CanUse(bot, item))
                    role = (4, item.Object_Type, item.Item_Type);
                else
                    continue;

                if (!bestByRole.TryGetValue(role, out DbInventoryItem incumbent) ||
                    EquipmentValue(item) > EquipmentValue(incumbent) ||
                    EquipmentValue(item) == EquipmentValue(incumbent) && item.SlotPosition < incumbent.SlotPosition)
                    bestByRole[role] = item;
            }

            foreach (DbInventoryItem item in bestByRole.Values)
                retained.Add(item);
            return retained;
        }

    private static eInventorySlot ResolveEquipmentSlot(GameBot bot, DbInventoryItem item)
    {
        eInventorySlot requested = (eInventorySlot)item.Item_Type;
        if (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
        {
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                bot.BotSpec?.WeaponOneType != eObjectType.HandToHand &&
                requested == eInventorySlot.LeftHandWeapon)
                requested = eInventorySlot.RightHandWeapon;
            // A left-axe build may use ordinary one-handed axes in its offhand.
            // Don't buy/equip one as an upgrade to a trained sword/hammer mainhand.
            if (bot.BotSpec?.SpecType == eSpecType.LeftAxe &&
                item.Object_Type is (int)eObjectType.Axe or (int)eObjectType.LeftAxe &&
                item.Object_Type != (int)bot.BotSpec.WeaponOneType && item.Item_Type != Slot.TWOHAND && item.Hand != 1)
                requested = eInventorySlot.LeftHandWeapon;
            // A hired companion is placed like a player's auto-equip (True 1.65): an either-hand weapon goes to the main
            // hand when that hand is empty, or when its class can't fight with it in the left hand.
            if (requested == eInventorySlot.LeftHandWeapon && bot.IsHiredCompanion &&
                (bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) == null || !LeftHandWeaponAllowed(bot, item)))
                requested = eInventorySlot.RightHandWeapon;
            if (!BotWeaponStats.FitsConfiguredSlot(bot, item, requested)) return eInventorySlot.Invalid;
        }
        if (requested is eInventorySlot.LeftRing or eInventorySlot.RightRing)
            return WorseSlot(bot, eInventorySlot.LeftRing, eInventorySlot.RightRing);
        if (requested is eInventorySlot.LeftBracer or eInventorySlot.RightBracer)
            return WorseSlot(bot, eInventorySlot.LeftBracer, eInventorySlot.RightBracer);
        return requested is >= eInventorySlot.MinEquipable and <= eInventorySlot.MaxEquipable
            ? requested
            : eInventorySlot.Invalid;
    }

        private static eInventorySlot WorseSlot(GameBot bot, eInventorySlot first, eInventorySlot second)
    {
        DbInventoryItem firstItem = bot.Inventory.GetItem(first);
        DbInventoryItem secondItem = bot.Inventory.GetItem(second);
        if (firstItem == null)
            return first;
        if (secondItem == null)
            return second;
        return EquipmentValue(firstItem) <= EquipmentValue(secondItem) ? first : second;
    }

        public static ListingCandidate FindValuableListingCandidate(GameBot bot)
    {
        using var aiProfile = AiProfile.Scope(AiSystem.EconomyValuation); // AI_PROFILE: measures only
        if (bot?.Inventory == null || bot.DatabaseID <= 0 ||
            (!IsBackpackFull(bot) && !AutonomousObjectiveAssignments.IsBetweenPveTasks(bot)))
            return null;

            // Only what fits under its listing cap now (EconomySeams.Listings: 3 posts for every item by default).
            string ownerId = GetOwnerId(bot.DatabaseID);
            int used = CountListings(ownerId);
            IExchangeListingPolicy listings = EconomySeams.Listings;
            if (used >= listings.MaxListings(ownerId))
                return null;

            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            return bot.Inventory.AllItems
            .Where(item => item != null && item.IsTradable && !string.IsNullOrWhiteSpace(item.Name) &&
                           item.OwnerLot == 0 && item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                           item.SlotPosition <= (int)eInventorySlot.LastBackpack &&
                           !operationalLoadout.Contains(item) &&
                           !TryGetEquipmentUpgrade(bot, item, out _) &&
                           used < listings.ListingCap(ownerId, item) &&
                           !EconomySeams.IsReserved(bot, item) && !EconomySeams.BlocksListing(bot, item))
            .Select(item => new ListingCandidate(item, RecommendListingPrice(item)))
            .Where(candidate => WorthListing(bot, candidate.Item, candidate.PriceCopper))
            // Stable: with every priority 0 (the default) the order is the old one, dearest first.
            .OrderByDescending(candidate => EconomySeams.ListingPriority(bot, candidate.Item))
            .ThenByDescending(candidate => candidate.PriceCopper)
            .FirstOrDefault();
        }

        /// <summary>
        /// Crafting 2.0 WP8, the full bot trade (BotTrades): the backpack items a gamebot would part with in a trade window
        /// with a real player. Its tradable, unlisted backpack items, less what it keeps for itself as at a merchant: its
        /// operational loadout (a backup weapon, an instrument, a relic, siege supply), an upgrade it would wear, its
        /// crafting materials, and what a package holds back (a piece made for an order, a kept draught). Worn gear is
        /// never in the backpack, so a bot never sells what it wears. Dearest first.
        /// </summary>
        public static IReadOnlyList<DbInventoryItem> FindPlayerTradeGoods(GameBot bot)
        {
            if (bot?.Inventory == null || bot.DatabaseID <= 0)
                return [];
            bool trainedCrafter = !string.IsNullOrWhiteSpace(bot.PersistentRecord?.SerializedCraftingSkills);
            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            return bot.Inventory.AllItems
                .Where(item => item != null && item.IsTradable && item.IsDropable && !string.IsNullOrWhiteSpace(item.Name) &&
                               item.OwnerLot == 0 && item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack &&
                               item is not GameInventoryRelic && !BotSiegeRuntime.IsSupply(item.Id_nb) &&
                               !operationalLoadout.Contains(item) && !TryGetEquipmentUpgrade(bot, item, out _) &&
                               !(item.IsStackable && item.Count > 0 && EconomySeams.IsMaterialFor(bot, item, trainedCrafter)) &&
                               !EconomySeams.IsReserved(bot, item) && !EconomySeams.KeepFromVendor(bot, item))
                .OrderByDescending(item => RecommendListingPrice(item))
                .ThenBy(item => item.SlotPosition)
                .ToArray();
        }

        public static DbInventoryItem FindVendorTrashCandidate(GameBot bot)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.EconomyValuation); // AI_PROFILE: measures only
            if (bot?.Inventory == null)
                return null;

            bool trainedCrafter = !string.IsNullOrWhiteSpace(bot.PersistentRecord?.SerializedCraftingSkills);
            bool canList = HasListingSpace(bot);
            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            return bot.Inventory.AllItems
                .Where(item => item != null && item.IsDropable && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack)
                .Where(item => !IsProtectedFromVendor(bot, item, trainedCrafter, canList, operationalLoadout))
                .Where(item => CalculateStandardVendorSaleCopper(item) > 0)
                .OrderBy(item => EquipmentValue(item))
                .ThenBy(item => item.Price)
                .FirstOrDefault();
        }

        private static bool IsProtectedFromVendor(GameBot bot, DbInventoryItem item, bool trainedCrafter,
            bool canList, HashSet<DbInventoryItem> operationalLoadout)
        {
            if (item is GameInventoryRelic || BotSiegeRuntime.IsSupply(item.Id_nb) ||
                operationalLoadout.Contains(item))
                return true;
            if (TryGetEquipmentUpgrade(bot, item, out _))
                return true;
            if (item.IsStackable && item.Count > 0 && EconomySeams.IsMaterialFor(bot, item, trainedCrafter))
                return true;
            // Seams (Crafting 2.0): a held-back item or one a package keeps from vendors; none by default.
            if (EconomySeams.IsReserved(bot, item) || EconomySeams.KeepFromVendor(bot, item))
                return true;
            // If the bot's single listing slot is occupied, unwanted gear can
            // still be sold normally. Never vendor an equippable upgrade.
            return canList && item.IsTradable && !EconomySeams.BlocksListing(bot, item) &&
                WorthListing(bot, item, RecommendListingPrice(item));
        }

        /// <summary>Worth a Realm Exchange post: dear enough for its level (today's rule), or an item a package says is
        /// always worth posting (EconomySeams; DividedRealms: a crafter's own work).</summary>
        public static bool WorthListing(GameBot bot, DbInventoryItem item, int priceCopper) =>
            item != null && (priceCopper >= Math.Max(50, item.Level * item.Level * 4) || EconomySeams.AlwaysWorthListing(bot, item));

        /// <summary>
        /// Performs the same appraisal and range checks as a real merchant, removes the exact
        /// persisted inventory row, then queues the bot's real copper ledger and inventory
        /// in the shared crash-safe persistence batch.
        /// It deliberately never sells equipped, usable upgrades, material stacks, or exchange goods.
        /// </summary>
        public static bool TrySellToVendor(GameBot bot, GameMerchant merchant, DbInventoryItem item, out long copper)
        {
            copper = 0;
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                merchant == null || item == null || !item.IsDropable || item.OwnerLot != 0 ||
                !bot.Inventory.AllItems.Contains(item) ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
                !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
                return false;

            bool trainedCrafter = !string.IsNullOrWhiteSpace(bot.PersistentRecord?.SerializedCraftingSkills);
            if (IsProtectedFromVendor(bot, item, trainedCrafter, HasListingSpace(bot),
                    FindOperationalLoadoutItems(bot)))
                return false;

            copper = CalculateStandardVendorSaleCopper(item);
            int originalSlot = item.SlotPosition;
            if (copper <= 0 || !bot.Inventory.RemoveItem(item))
            {
                copper = 0;
                return false;
            }

            if (!AddMoney(bot.DatabaseID, copper, EconomyFlowKind.VendorSale))
            {
                // A failed durable credit must not destroy the item; restoring the exact slot
                // preserves the finite inventory invariant and makes the sale retryable.
                bot.Inventory.AddItem((eInventorySlot)originalSlot, item);
                AutonomousBotStatusPersistence.Queue(bot, true);
                copper = 0;
                return false;
            }

            bot.MarkAutonomousStateDirty();
            MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

        /// <summary>
        /// The ordinary GameMerchant appraisal formula, factored for GameBot because the legacy
        /// merchant packet entry point accepts GamePlayer rather than the server's NPC-backed
        /// autonomous IGamePlayer implementation.
        /// </summary>
        public static long CalculateStandardVendorSaleCopper(DbInventoryItem item)
        {
            if (item == null || !item.IsDropable)
                return 0;

            int itemCount = Math.Max(1, item.Count);
            int packSize = Math.Max(1, item.PackSize);
            long copper = item.Price * (long)itemCount / packSize * GS.ServerProperties.Properties.ITEM_SELL_RATIO / 100;
            return item.Price == 1 && copper == 0
                ? item.Price * (long)itemCount / packSize
                : copper;
        }

        /// <summary>
        /// Buys one legal, material equipment upgrade from the nearby merchant's real trade list.
        /// The item is first inserted into the finite backpack, charged from saved copper, and
        /// immediately equipped through the same owned-upgrade path used by dropped equipment.
        /// </summary>
        /// <summary>
        /// A merchant whose prices are in coin. Seal, glass, aurulite and other item-currency merchants, and the bounty
        /// point merchants, price in their own currency, but the vendor upgrade paid their price in copper: a bot next
        /// to a Darkness Falls seals merchant bought seal gear for 15 to 51 copper (owner 2026-10-30).
        /// </summary>
        public static bool SellsForCoin(GameMerchant merchant) =>
            merchant != null && merchant is not GameItemCurrencyMerchant && merchant is not GameBountyMerchant;

        public static bool TryBuyUsefulVendorUpgrade(GameBot bot, GameMerchant merchant, out DbInventoryItem purchased, out long copper)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.EconomyValuation); // AI_PROFILE: measures only
            purchased = null;
            copper = 0;
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                !SellsForCoin(merchant) ||
                merchant?.TradeItems == null || !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) ||
                bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
                return false;

            // Only what the bot can pay for: the best upgrade overall was chosen first and, when it cost more than the
            // bot had, nothing was bought although cheaper upgrades were on the same list (run 1c, 2026-10-30).
            long coin = GetMoney(bot.DatabaseID);
            DbInventoryItem[] upgrades = merchant.TradeItems.GetAllItems().Values
                .OfType<DbItemTemplate>()
                .Where(template => template.Price > 0 && template.Price <= coin)
                .Select(GameInventoryItem.Create)
                .Where(item => item != null && TryGetEquipmentUpgrade(bot, item, out _))
                .OrderByDescending(EquipmentValue)
                .ToArray();
            DbInventoryItem selected = upgrades.FirstOrDefault();
            if (selected != null && bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)selected.Object_Type))
                selected = upgrades
                    .Where(item => BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                    .OrderByDescending(item => SavageBotWeaponPolicy.UpgradeGain(bot, item,
                        ResolveEquipmentSlot(bot, item)))
                    .ThenByDescending(EquipmentValue)
                    .First();
            DbItemTemplate template = selected?.Template;
            if (template == null || template.Price <= 0)
                return false;

            copper = template.Price;
            eInventorySlot backpackSlot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            purchased = GameInventoryItem.Create(template);
            bool spent = purchased != null && TrySpend(bot.DatabaseID, copper, EconomyFlowKind.VendorGear);
            if (!spent || !bot.Inventory.AddItem(backpackSlot, purchased))
            {
                if (spent && bot.Inventory.GetItem(backpackSlot) != purchased)
                    AddMoney(bot.DatabaseID, copper, EconomyFlowKind.VendorGear);
                purchased = null;
                copper = 0;
                return false;
            }

            TryEquipOwnedUpgrade(bot, purchased, "vendor");
            MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

    /// <summary>
    /// Seal gear (owner 2026-10-30): a bot grinding Darkness Falls spends the seals it has looted at its own realm's seal
    /// merchants there, as a player would. Each merchant prices its stock in its own seal (Emerald, Sapphire or Diamond).
    /// An affordable upgrade is bought and worn. With no upgrade left to save that seal for, the seals buy the most
    /// valuable item they can, which the bot then sells or posts on the Realm Exchange like any other loot; while a
    /// dearer upgrade exists, it keeps saving for it.
    /// </summary>
    public readonly record struct SealPurchase(DbItemTemplate Template, bool Upgrade);

    public static int SealCount(GameBot bot, string sealId) =>
        bot?.Inventory == null || string.IsNullOrEmpty(sealId) ? 0 :
            bot.Inventory.CountItemTemplate(sealId, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

    private static IEnumerable<GameDarknessFallsSealsMerchant> OwnSealMerchants(GameBot bot, string sealId) =>
        (bot?.CurrentRegion?.Merchants ?? []).OfType<GameDarknessFallsSealsMerchant>()
            .Where(merchant => merchant.ObjectState == GameObject.eObjectState.Active && merchant.MoneyKey == sealId &&
                               GameDarknessFallsSealsMerchant.CanTrade(merchant.Realm, bot.Realm));

    public static SealPurchase? FindSealPurchase(GameBot bot, GameDarknessFallsSealsMerchant merchant)
    {
        if (bot?.Inventory == null || merchant?.TradeItems == null ||
            !GameDarknessFallsSealsMerchant.CanTrade(merchant.Realm, bot.Realm))
            return null;
        int seals = SealCount(bot, merchant.MoneyKey);
        if (seals <= 0)
            return null;

        DbItemTemplate upgrade = null, resale = null;
        int upgradeValue = int.MinValue, resaleValue = int.MinValue;
        foreach (DbItemTemplate template in merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
        {
            if (template.Price <= 0 || template.Price > seals)
                continue;
            DbInventoryItem item = GameInventoryItem.Create(template);
            if (item == null)
                continue;
            if (TryGetEquipmentUpgrade(bot, item, out _))
            {
                int value = EquipmentValue(item);
                if (value > upgradeValue)
                {
                    upgrade = template;
                    upgradeValue = value;
                }
            }
            else if (item.IsTradable)
            {
                int value = RecommendListingPrice(item);
                if (value > resaleValue)
                {
                    resale = template;
                    resaleValue = value;
                }
            }
        }
        if (upgrade != null)
            return new SealPurchase(upgrade, true);
        if (resale == null || IsBackpackNearlyFull(bot) || SavingSealsForUpgrade(bot, merchant.MoneyKey, seals))
            return null;
        return new SealPurchase(resale, false);
    }

    /// <summary>Some merchant of this seal sells an upgrade the bot can't afford yet.</summary>
    private static bool SavingSealsForUpgrade(GameBot bot, string sealId, int seals)
    {
        foreach (GameDarknessFallsSealsMerchant merchant in OwnSealMerchants(bot, sealId))
            foreach (DbItemTemplate template in merchant.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>() ?? [])
                if (template.Price > seals && GameInventoryItem.Create(template) is DbInventoryItem item &&
                    TryGetEquipmentUpgrade(bot, item, out _))
                    return true;
        return false;
    }

    /// <summary>The bot is in Darkness Falls and one of its realm's seal merchants there has something to spend seals on.</summary>
    public static bool HasSealPurchase(GameBot bot)
    {
        if (bot?.Inventory == null || bot.CurrentRegionID != AutonomousDarknessFallsPolicy.RegionId || bot.CurrentRegion == null)
            return false;
        foreach (GameMerchant merchant in bot.CurrentRegion.Merchants)
            if (merchant is GameDarknessFallsSealsMerchant seals && seals.ObjectState == GameObject.eObjectState.Active &&
                FindSealPurchase(bot, seals) != null)
                return true;
        return false;
    }

    /// <summary>Buys the seal item chosen above at this merchant and pays in seals; an upgrade is worn at once.</summary>
    public static bool TryBuySealItem(GameBot bot, GameDarknessFallsSealsMerchant merchant, out DbInventoryItem purchased,
        out int paid, out bool upgrade)
    {
        purchased = null;
        paid = 0;
        upgrade = false;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
            merchant == null || !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
            return false;
        SealPurchase? choice = FindSealPurchase(bot, merchant);
        eInventorySlot backpackSlot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
        if (choice == null || backpackSlot == eInventorySlot.Invalid)
            return false;
        DbItemTemplate template = choice.Value.Template;
        upgrade = choice.Value.Upgrade;

        lock (bot.Inventory.Lock)
        {
            int price = (int)template.Price;
            if (SealCount(bot, merchant.MoneyKey) < price)
                return false;
            DbInventoryItem item = GameInventoryItem.Create(template);
            if (item == null || !bot.Inventory.AddItem(backpackSlot, item))
                return false;
            int removed = 0;
            foreach (DbInventoryItem stack in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToList())
            {
                if (stack == null || stack.Id_nb != merchant.MoneyKey)
                    continue;
                int take = Math.Min(stack.Count, price - removed);
                if (take > 0 && bot.Inventory.RemoveCountFromStack(stack, take))
                    removed += take;
                if (removed >= price)
                    break;
            }
            purchased = item;
            paid = removed;
        }

        Log.Info($"AUTONOMOUS_SEAL_PURCHASE bot={bot.Name} id={bot.DatabaseID} item=\"{purchased.Name}\" " +
                 $"seals={paid} seal={merchant.MoneyKey} use={(upgrade ? "upgrade" : "resale")} merchant=\"{merchant.Name}\"");
        if (upgrade)
            TryEquipOwnedUpgrade(bot, purchased, "seal merchant");
        MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        return true;
    }

    public static int RollListingPrice(DbInventoryItem item) => RecommendListingPrice(item, -0.35 + Random.Shared.NextDouble() * 0.85);

        public static int RecommendListingPrice(DbInventoryItem item, double sellerDisposition = 0)
    {
        if (item == null)
            return 0;
        int utility = Math.Abs(item.Bonus) + Math.Abs(item.ExtraBonus) + Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) +
                      Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) + Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) +
                      Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) + Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10);
        double rarity = Math.Clamp((item.Quality - 85) / 3d + (item.IsROG ? 2 : 0) + (item.IsCrafted ? 1 : 0) +
                                   Math.Min(3, utility / 20d), 0, 10);
        var facts = new AutonomousAuctionValuation.ItemFacts(
            Math.Max(1, item.Level), Math.Max(1, item.Count), Math.Max(1, item.Quality),
            Math.Max(1, (int)item.ConditionPercent), rarity, utility, item.MaxCount > 1 || item.Count > 1);
        long price = AutonomousAuctionValuation.Recommend(facts, sellerDisposition).BuyoutCopper;
        return (int)Math.Clamp(price, 1, int.MaxValue);
    }

    public static bool TryList(GameBot bot, DbInventoryItem item, int priceCopper) =>
        TryList(bot, item, priceCopper, out _);

    /// <summary>
    /// <paramref name="deferred"/> is true when nothing was changed because the database writer was busy for longer than
    /// <see cref="AutonomousBotStatusPersistence.AiTurnWriteWaitMs"/>: the caller keeps the item and tries again later.
    /// </summary>
    public static bool TryList(GameBot bot, DbInventoryItem item, int priceCopper, out bool deferred)
    {
        deferred = false;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0 ||
            item == null || !item.IsTradable || priceCopper <= 0 || item.OwnerLot != 0 || BotSiegeRuntime.IsSupply(item.Id_nb) ||
            item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
            EconomySeams.IsReserved(bot, item) || EconomySeams.BlocksListing(bot, item))
            return false;

        lock (RealmExchangeBroker.TransactionLock)
        // 2026-11-15 02:38 (build 1dc66b6): Waffleironer's turn waited 5,582 ms here while the status flush held this
        // lock through a 6,062 ms Inventory INSERT, and the whole game loop waited with it. A bot turn now waits briefly
        // and lists on a later turn instead.
        using (AutonomousBotStatusPersistence.TryEnterForAiTurn(bot, "list", out bool entered))
        {
        if (!entered)
        {
            deferred = true;
            return false;
        }
        string ownerId = GetOwnerId(bot.DatabaseID);
        if (!bot.Inventory.AllItems.Contains(item))
            return false;

        // A retained inventory instance can carry a stale persistence flag.
        // Never INSERT its existing primary key again, and never adopt another
        // owner's row or an item already on sale. This runs only at listing,
        // not in the ordinary bot tick or inventory appraisal.
        if (!item.IsPersisted && !string.IsNullOrWhiteSpace(item.ObjectId))
        {
            DbInventoryItem stored = GameServer.Database.SelectObject<DbInventoryItem>(
                DB.Column("Inventory_ID").IsEqualTo(item.ObjectId));
            if (stored != null)
            {
                if (!CanReconcileListingItem(item, stored, ownerId))
                    return false;
                item.IsPersisted = true;
                item.Dirty = true;
            }
        }

        int slot = FindFreeListingSlot(ownerId, item);
        int originalSlot = item.SlotPosition;
        if (slot < 0 || !bot.Inventory.RemoveItemWithoutDbDeletion(item))
            return false;

        item.OwnerID = ownerId;
        item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
        item.SlotPosition = slot;
        item.SellPrice = priceCopper;
        item.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
        bool saved = item.IsPersisted ? GameServer.Database.SaveObject(item) : GameServer.Database.AddObject(item);
        if (!saved)
        {
            item.OwnerLot = 0;
            item.SellPrice = 0;
            item.RealmExchangeListedUtc = string.Empty;
            bot.Inventory.AddItem((eInventorySlot)originalSlot, item);
            return false;
        }
        MarketCache.AddItem(item);
        MarkInventoryChanged(bot);
        EconomySeams.NotifyListed(bot, item);
        return true;
        }
    }

    public static bool CanReconcileListingItem(DbInventoryItem item, DbInventoryItem stored, string ownerId) =>
        item != null && stored != null && !string.IsNullOrWhiteSpace(item.ObjectId) &&
        item.ObjectId == stored.ObjectId && stored.OwnerID == ownerId && stored.OwnerLot == 0 &&
        stored.SlotPosition >= (int)eInventorySlot.FirstBackpack && stored.SlotPosition <= (int)eInventorySlot.LastBackpack &&
        item.ITemplate_Id == stored.ITemplate_Id && item.UTemplate_Id == stored.UTemplate_Id && item.Count == stored.Count;

    /// <summary>A free post under the owner's cap for this item (EconomySeams.Listings; 3 for every item by default).
    /// <paramref name="listedItem"/> may be null: the cap of an unspecified item.</summary>
    public static int FindFreeListingSlot(string ownerId, DbInventoryItem listedItem = null)
    {
        var used = MarketCache.SearchItems(new ItemQuery { Owner = ownerId })
            .Where(item => RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot)).Select(item => item.SlotPosition).ToHashSet();
        if (used.Count >= EconomySeams.Listings.ListingCap(ownerId, listedItem)) return -1;
        for (int slot = (int)eInventorySlot.Consignment_First; slot <= (int)eInventorySlot.Consignment_Last; slot++)
            if (!used.Contains(slot)) return slot;
        return -1;
    }

    private static bool HasListingSpace(GameBot bot) => FindFreeListingSlot(GetOwnerId(bot.DatabaseID)) >= 0;

    /// <summary>The owner's posts on the Realm Exchange now (distinct posts, as <see cref="FindFreeListingSlot"/> counts them).</summary>
    public static int CountListings(string ownerId) =>
        MarketCache.SearchItems(new ItemQuery { Owner = ownerId })
            .Where(item => RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot)).Select(item => item.SlotPosition).Distinct().Count();

    /// <summary>Whether this item can be posted now, under the cap that applies to it (DividedRealms' CanListNow).</summary>
    public static bool CanListNow(GameBot bot, DbInventoryItem item) =>
        bot != null && FindFreeListingSlot(GetOwnerId(bot.DatabaseID), item) >= 0;

    public static bool TryBuy(GameBot bot, DbInventoryItem item) => TryBuy(bot, item, out _);

    /// <summary><paramref name="deferred"/>: as for <see cref="TryList(GameBot, DbInventoryItem, int, out bool)"/>.</summary>
    public static bool TryBuy(GameBot bot, DbInventoryItem item, out bool deferred)
    {
        deferred = false;
        lock (RealmExchangeBroker.TransactionLock)
        lock (typeof(AutonomousBotEconomy))
        using (AutonomousBotStatusPersistence.TryEnterForAiTurn(bot, "buy", out bool entered))
        {
            if (!entered)
            {
                deferred = true;
                return false;
            }
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.PersistentRecord == null ||
                bot.DatabaseID <= 0 || !RealmExchangeBroker.IsAvailableListing(item, bot.Realm) ||
                item.OwnerID == GetOwnerId(bot.DatabaseID))
                return false;

            eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid || bot.PersistentRecord.MoneyCopper < item.SellPrice)
                return false;

            int price = item.SellPrice;
            string sellerId = item.OwnerID;
            int sellerSlot = item.SlotPosition;
            var payment = RealmExchangeBroker.PrepareSellerPayment(sellerId, price, bot.Realm, GetOwnerId(bot.DatabaseID));
            if (payment == null) return false;
            long originalMoney = bot.PersistentRecord.MoneyCopper;
            bot.PersistentRecord.MoneyCopper -= price;
            bot.PersistentRecord.Dirty = true;
            MarketCache.RemoveItem(item);
            item.OwnerLot = 0;
            item.SellPrice = 0;

            if (!bot.Inventory.AddItem(slot, item))
            {
                item.OwnerID = sellerId;
                item.SlotPosition = sellerSlot;
                item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
                item.SellPrice = price;
                MarketCache.AddItem(item);
                bot.PersistentRecord.MoneyCopper = originalMoney;
                return false;
            }

            item.OwnerID = GetOwnerId(bot.DatabaseID);
            payment.Apply();
            var sale = new RealmExchangeSaleLedger.Sale((int)bot.Realm, item.Name, item.Count, price,
                sellerId, payment.SellerName, GetOwnerId(bot.DatabaseID), bot.Name, DateTime.UtcNow, payment.TaxCopper, item.IsCrafted);
            if (!RealmExchangeSaleLedger.CommitSale(GameServer.Database, sale, item, bot.PersistentRecord, payment.Row))
            {
                payment.Restore();
                bot.Inventory.RemoveItemWithoutDbDeletion(item);
                item.OwnerID = sellerId;
                item.SlotPosition = sellerSlot;
                item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
                item.SellPrice = price;
                MarketCache.AddItem(item);
                bot.PersistentRecord.MoneyCopper = originalMoney;
                bot.PersistentRecord.Dirty = true;
                return false;
            }
            MarkInventoryChanged(bot);
            return true;
        }
    }

    public static bool TryEquipPurchasedUpgrade(GameBot bot, PurchaseCandidate purchase)
    {
        if (bot?.Inventory == null || purchase?.IsEquipmentUpgrade != true ||
            purchase.EquipSlot == eInventorySlot.Invalid || purchase.Item == null)
            return false;

        // Revalidate at the moment of the move.  A listing can remain in the
        // candidate cache while specs, level, or another equipment action
        // changes.  Without this guard a stale purchase could put a staff in
        // a Shadowblade/Hunter melee slot (or a non-bow in a distance slot),
        // leaving the bot looking armed but unable to deal damage.  Invalid
        // purchases stay in the real backpack and are never deleted.
        bool legal = purchase.EquipSlot switch
        {
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon =>
                BotWeaponStats.CanUseMelee(bot, purchase.Item),
            eInventorySlot.DistanceWeapon => BotRangedCombat.CanUse(bot, purchase.Item),
            _ => true,
        };
        if (!legal)
            return false;

        eInventorySlot backpackSlot = (eInventorySlot)purchase.Item.SlotPosition;
        DbInventoryItem replaced = bot.Inventory.GetItem(purchase.EquipSlot);
        // Crafting 2.0 WP6: the no-undress guard (bot_crafters on; always passes otherwise). A refused piece stays in
        // the backpack, bought and real, as an invalid purchase does above.
        if (backpackSlot is < eInventorySlot.FirstBackpack or > eInventorySlot.LastBackpack ||
            !BotGearGuard.Allows(bot, purchase.Item, purchase.EquipSlot, "realm_exchange") ||
            !bot.Inventory.MoveItem(backpackSlot, purchase.EquipSlot, purchase.Item.Count))
            return false;

        bot.RefreshItemBonuses();
        if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
            BotWeaponStats.IsMeleeWeapon((eObjectType)purchase.Item.Object_Type))
            bot.EnsureBotWeaponReady();
        MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        LogEquipmentUpgrade(bot, purchase.Item, replaced, purchase.EquipSlot, "realm_exchange");
        return true;
    }

    private static OfflineWorldBotRecord Find(long botId) =>
        DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));

    internal static OfflineWorldBotRecord FindLiveOrStored(long botId) =>
        AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.PersistentRecord != null
            ? live.PersistentRecord
            : Find(botId);
}

[DataTable(TableName = "offline_world_bots")]
public sealed class OfflineWorldBotRecord : DataObject
{
    [PrimaryKey(AutoIncrement = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public string Name { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false)] public int ClassId { get; set; }
    [DataElement(AllowDbNull = false)] public string ClassName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RaceId { get; set; }
    [DataElement(AllowDbNull = false)] public string RaceName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Gender { get; set; }
    [DataElement(AllowDbNull = false)] public int Level { get; set; }
    [DataElement(AllowDbNull = false)] public long Experience { get; set; }
    [DataElement(AllowDbNull = false)] public long RealmPoints { get; set; }
    [DataElement(AllowDbNull = false)] public int ZoneId { get; set; }
    [DataElement(AllowDbNull = true)] public string ZoneName { get; set; }
    [DataElement(AllowDbNull = false)] public string Activity { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool IsOnline { get; set; }
    [DataElement(AllowDbNull = false)] public bool IsAlive { get; set; }
    [DataElement(AllowDbNull = false)] public string LastUpdateUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public long MoneyCopper { get; set; }
    [DataElement(AllowDbNull = false)] public int InventoryRevision { get; set; }
    [DataElement(AllowDbNull = false)] public int X { get; set; }
    [DataElement(AllowDbNull = false)] public int Y { get; set; }
    [DataElement(AllowDbNull = false)] public int Z { get; set; }
    [DataElement(AllowDbNull = false)] public int RegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int Health { get; set; }
    [DataElement(AllowDbNull = false)] public int Mana { get; set; }
    [DataElement(AllowDbNull = false)] public int Endurance { get; set; }
    [DataElement(AllowDbNull = false)] public int BindRegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int BindX { get; set; }
    [DataElement(AllowDbNull = false)] public int BindY { get; set; }
    [DataElement(AllowDbNull = false)] public int BindZ { get; set; }
    [DataElement(AllowDbNull = false)] public string SerializedSpecs { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SerializedBuildPlan { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int UnspentSpecPoints { get; set; }
    [DataElement(AllowDbNull = false)] public string SerializedAbilities { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SerializedCraftingSkills { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string CurrentCampId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ItineraryJson { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int DeathCount { get; set; }
    [DataElement(AllowDbNull = false)] public bool IsRetired { get; set; }
    [DataElement(AllowDbNull = false)] public string LastSavedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int PersistedStateVersion { get; set; } = 1;
    [DataElement(AllowDbNull = false)] public string CurrentGoal { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TravelDestination { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveProgress { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastMeaningfulProgressUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RecoveryCount { get; set; }
    // Durable autonomous population allocation.  These are deliberately separate
    // from the human-readable Activity/CurrentGoal fields so restart/reload does
    // not turn a presentation string into control state.
    [DataElement(AllowDbNull = false)] public string ObjectiveKind { get; set; } = "SoloPve";
    [DataElement(AllowDbNull = false)] public string ObjectiveAssignmentId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveAssignedUtc { get; set; } = string.Empty;
    // The object database schema reconciler adds these columns to existing
    // offline_world_bots tables during startup, just as it does every
    // DataElement on this durable record.
    [DataElement(AllowDbNull = false)] public string ObjectiveExpiresUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveRvrEligibleUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectivePhase { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectivePveMode { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int ObjectivePveKillTarget { get; set; }
    [DataElement(AllowDbNull = false)] public int ObjectivePveKills { get; set; }
}
