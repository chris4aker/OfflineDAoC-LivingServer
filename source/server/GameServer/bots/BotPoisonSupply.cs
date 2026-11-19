using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.CraftingEconomy;

namespace DOL.GS
{
    /// <summary>
    /// Unlimited supplies, not unlimited procs: a native coat per weapon outside combat.
    /// <para>Crafting 2.0 WP7 (paid poisons, ours): with <c>bot_upkeep</c> on (and <c>bot_crafters</c>), an autonomous
    /// gamebot's coat comes out of a real vial in its bag, one vial a coat, and the vials are bought at a natural task
    /// break from a real merchant near the bot that sells them, at the merchant's price (the pack price per vial), as
    /// <see cref="EconomyFlowKind.Poison"/>. No vial, no coat, as for a player. Companions and everything with the switch
    /// off keep today's free coat.</para>
    /// </summary>
    public static class BotPoisonSupply
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BotPoisonSupply));
        private sealed record Coat(int Level, int SpellId, eSpellType Type);
        private static Coat[] _catalog = Array.Empty<Coat>();
        // WP7: the vial templates of each coat's spell (realm variants), for buying and for matching a carried vial.
        private static Dictionary<int, DbItemTemplate[]> _vials = new();

        /// <summary>WP7: a restock is due below this many vials of a coat...</summary>
        public const int RestockBelow = 20;
        /// <summary>...and buys up to this many (a stack holds 100).</summary>
        public const int RestockTarget = 60;
        /// <summary>A poison merchant is looked for within this radius of the bot (True165's potion merchant radius).</summary>
        public const int MerchantRadius = 2500;

        internal static readonly BotSinks.HourlySums Bought = new();
        internal static readonly BotSinks.HourlySums Coats = new();
        internal static readonly BotSinks.HourlySums Missed = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameBot, string> RestockedAtBreak = new();

        public static bool IsAssassin(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Infiltrator or eCharacterClass.Shadowblade or eCharacterClass.Nightshade;
        public static int EligibleLevel(int level, int envenom) => Math.Clamp(Math.Min(level, envenom), 0, 50);

        // One startup query; never reads SQLite during AI turns.
        public static void Load()
        {
            Load(GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Object_Type").IsEqualTo((int)eObjectType.Poison)),
                SkillBase.GetSpellList(GlobalSpellsLines.Mundane_Poisons));
        }

        public static void Load(IEnumerable<DbItemTemplate> items, IEnumerable<Spell> spellList)
        {
            var spells = spellList.GroupBy(s => s.ID).ToDictionary(g => g.Key, g => g.First());
            DbItemTemplate[] usable = items
                .Where(item => item.Level is > 0 and <= 50 && item.PoisonCharges > 0 &&
                    spells.TryGetValue(item.PoisonSpellID, out Spell spell) &&
                    spell.SpellType is eSpellType.DamageOverTime or eSpellType.StrengthConstitutionDebuff)
                .ToArray();
            _catalog = usable
                .Select(item => new Coat(Math.Max(item.Level, spells[item.PoisonSpellID].Level),
                    item.PoisonSpellID, spells[item.PoisonSpellID].SpellType))
                .Distinct().OrderByDescending(coat => coat.Level).ToArray();
            _vials = usable.GroupBy(item => item.PoisonSpellID).ToDictionary(g => g.Key, g => g.ToArray());
        }

        /// <summary>WP7: whether this bot's coats are paid (bot_upkeep, an autonomous gamebot).</summary>
        public static bool Paid(GameBot bot) => BotSinks.Upkeep(bot);

        public static bool Maintain(GameBot bot)
        {
            if (bot?.CharacterClass == null || !IsAssassin((eCharacterClass)bot.CharacterClass.ID) ||
                !bot.IsAlive || bot.InCombat || bot.IsAttacking || bot.IsCasting || bot.IsCrowdControlled ||
                bot.castingComponent?.HasPendingSkillRequests == true) return false;
            int level = EligibleLevel(bot.Level, bot.GetModifiedSpecLevel(Specs.Envenom));
            bool paid = Paid(bot);
            // Paid: only a coat the bot carries a vial of (the best one it has); the bag is looked at once.
            HashSet<int> carried = paid ? CarriedSpells(bot) : null;
            Coat damage = null, debuff = null;
            foreach (Coat coat in _catalog)
            {
                if (coat.Level > level) continue;
                if (paid && !carried.Contains(coat.SpellId)) continue;
                if (damage == null && coat.Type == eSpellType.DamageOverTime) damage = coat;
                if (debuff == null && coat.Type == eSpellType.StrengthConstitutionDebuff) debuff = coat;
                if (damage != null && debuff != null) break;
            }
            bool changed = Apply(bot, eInventorySlot.RightHandWeapon, damage, paid);
            changed |= Apply(bot, eInventorySlot.TwoHandWeapon, damage, paid);
            changed |= Apply(bot, eInventorySlot.LeftHandWeapon, debuff ?? damage, paid);
            if (changed) Changed(bot);
            return changed;
        }

        private static bool Apply(GameBot bot, eInventorySlot slot, Coat coat, bool paid)
        {
            DbInventoryItem weapon = bot.Inventory?.GetItem(slot);
            if (!BotWeaponStats.FitsConfiguredSlot(bot, weapon, slot) || weapon.PoisonCharges > 0)
                return false;
            if (coat == null)
            {
                if (paid) Missed.Add(bot.Realm, 0);
                return false;
            }
            if (paid && !UseVial(bot, coat.SpellId))
            {
                Missed.Add(bot.Realm, 0);
                return false;
            }
            weapon.PoisonSpellID = coat.SpellId;
            weapon.PoisonMaxCharges = 1;
            weapon.PoisonCharges = 1;
            if (paid) Coats.Add(bot.Realm, 0);
            return true;
        }

        public static void Changed(GameBot bot)
        {
            if (!bot.IsAutonomousWorldBot) return;
            bot.MarkAutonomousStateDirty();
            // A coat changes neither equipment suitability nor backpack space;
            // don't invalidate the expensive vendor/upgrade appraisal cache.
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        #region WP7: vials

        /// <summary>Whether a bag item is a poison vial (any poison item with a coat's spell).</summary>
        public static bool IsVial(DbInventoryItem item) =>
            item != null && item.Object_Type == (int)eObjectType.Poison && item.PoisonSpellID > 0 && item.Count > 0;

        /// <summary>A vial of this spell in the bag, or null.</summary>
        private static DbInventoryItem Vial(GameBot bot, int spellId) => bot.Inventory?.AllItems.FirstOrDefault(item =>
            IsVial(item) && item.PoisonSpellID == spellId &&
            item.SlotPosition >= (int)eInventorySlot.FirstBackpack && item.SlotPosition <= (int)eInventorySlot.LastBackpack);

        private static HashSet<int> CarriedSpells(GameBot bot)
        {
            var spells = new HashSet<int>();
            foreach (DbInventoryItem item in bot.Inventory?.AllItems ?? Enumerable.Empty<DbInventoryItem>())
                if (IsVial(item) && item.SlotPosition >= (int)eInventorySlot.FirstBackpack && item.SlotPosition <= (int)eInventorySlot.LastBackpack)
                    spells.Add(item.PoisonSpellID);
            return spells;
        }

        /// <summary>Vials of a spell the bot carries.</summary>
        public static int Carried(GameBot bot, int spellId) => bot?.Inventory?.AllItems.Where(item =>
                IsVial(item) && item.PoisonSpellID == spellId &&
                item.SlotPosition >= (int)eInventorySlot.FirstBackpack && item.SlotPosition <= (int)eInventorySlot.LastBackpack)
            .Sum(item => item.Count) ?? 0;

        private static bool UseVial(GameBot bot, int spellId)
        {
            lock (bot.Inventory)
            {
                DbInventoryItem vial = Vial(bot, spellId);
                if (vial == null || !bot.Inventory.RemoveCountFromStack(vial, 1))
                    return false;
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            return true;
        }

        /// <summary>Pure: what a number of vials costs at a merchant that sells a pack of <paramref name="packSize"/> for
        /// <paramref name="packPrice"/> (rounded up, at least 1 copper a vial).</summary>
        public static long VialsPrice(long packPrice, int packSize, int count) =>
            count <= 0 || packPrice <= 0 ? 0 : Math.Max(count, (packPrice * count + Math.Max(1, packSize) - 1) / Math.Max(1, packSize));

        /// <summary>Pure: how many vials to buy when holding <paramref name="held"/> (0 unless below the restock point).</summary>
        public static int RestockCount(int held, long coin, long pricePerVial) =>
            held >= RestockBelow || pricePerVial <= 0 ? 0 : (int)Math.Min(RestockTarget - held, Math.Max(0, coin) / pricePerVial);

        /// <summary>
        /// At a task break: an assassin gamebot under upkeep buys vials of the best damage and debuff coats a merchant near
        /// it sells (realm or neutral, at or below its envenom level), up to <see cref="RestockTarget"/>. Returns the vials bought.
        /// </summary>
        public static int Restock(GameBot bot, GameMerchant at, out long spent)
        {
            spent = 0;
            if (!Paid(bot) || bot.CharacterClass == null || !IsAssassin((eCharacterClass)bot.CharacterClass.ID) || bot.Inventory == null)
                return 0;
            int level = EligibleLevel(bot.Level, bot.GetModifiedSpecLevel(Specs.Envenom));
            var onSale = new[] { at }.Concat(bot.GetNPCsInRadius(MerchantRadius).OfType<GameMerchant>())
                .Where(m => m?.TradeItems != null && AutonomousBotEconomy.SellsForCoin(m) && (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .Distinct()
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                .Where(t => t.Price > 0 && t.Object_Type == (int)eObjectType.Poison && _vials.ContainsKey(t.PoisonSpellID))
                .GroupBy(t => t.PoisonSpellID).ToDictionary(g => g.Key, g => g.OrderBy(t => t.Price).First());
            int bought = 0;
            foreach (eSpellType type in new[] { eSpellType.DamageOverTime, eSpellType.StrengthConstitutionDebuff })
            {
                Coat coat = _catalog.FirstOrDefault(c => c.Type == type && c.Level <= level && onSale.ContainsKey(c.SpellId));
                if (coat == null)
                    continue;
                DbItemTemplate template = onSale[coat.SpellId];
                long perVial = VialsPrice(template.Price, template.PackSize, 1);
                int count = RestockCount(Carried(bot, coat.SpellId), BotSinks.CoinOf(bot.DatabaseID), perVial);
                if (count <= 0)
                    continue;
                long price = VialsPrice(template.Price, template.PackSize, count);
                lock (bot.Inventory)
                {
                    DbInventoryItem stack = Vial(bot, coat.SpellId);
                    bool toStack = stack != null && stack.MaxCount - stack.Count >= count;
                    eInventorySlot slot = toStack ? eInventorySlot.Invalid
                        : bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (!toStack && slot == eInventorySlot.Invalid)
                        continue;
                    DbInventoryItem item = toStack ? null : GameInventoryItem.Create(template);
                    if (!toStack && item == null)
                        continue;
                    if (item != null)
                        item.Count = Math.Min(count, Math.Max(1, template.MaxCount));
                    if (!BotSinks.Spend(bot.DatabaseID, price, EconomyFlowKind.Poison))
                        continue;
                    bool added = toStack ? bot.Inventory.AddCountToStack(stack, count) : bot.Inventory.AddItem(slot, item);
                    if (!added)
                    {
                        BotSinks.Pay(bot.DatabaseID, price, EconomyFlowKind.Poison);
                        continue;
                    }
                }
                bought += count;
                spent += price;
            }
            if (bought == 0)
                return 0;
            Bought.Add(bot.Realm, spent, bought);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconConsumable, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("level", bot.Level), ("kind", EconomyFlowKind.Poison), ("count", bought), ("copper", spent)));
            return bought;
        }

        /// <summary>The vial purchase as a merchant step (once a break).</summary>
        public sealed class Step : IBotServiceStep
        {
            public static readonly Step Instance = new();
            public string Name => "poisons (WP7)";
            public int Order => 120;
            public eWorldServiceKind Kind => eWorldServiceKind.Vendor;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                if (!Paid(bot) || service is not GameMerchant merchant)
                    return false;
                string breakId = BotSinks.BreakOf(bot);
                if (RestockedAtBreak.TryGetValue(bot, out string done) && done == breakId)
                    return false;
                RestockedAtBreak.AddOrUpdate(bot, breakId);
                int bought = Restock(bot, merchant, out long spent);
                if (bought == 0)
                    return false;
                result = new BotServiceResult($"Bought {bought} poison vials", $"Paid {spent} saved copper for its weapon coats", 3_000,
                    eAutonomousProgressKind.Money);
                return true;
            }
        }

        /// <summary>Tests: the hour's sums back to 0.</summary>
        public static void ResetForTests()
        {
            Bought.Take();
            Coats.Take();
            Missed.Take();
        }

        #endregion
    }
}
