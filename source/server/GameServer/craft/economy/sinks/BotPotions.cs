using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting 2.0 WP7: gamebots drink the draughts they carry and rich ones keep a heal draught, bought at a natural
    /// task break from a real merchant.
    /// <para>Ported from shadowofze's True165 (<c>mode165/BotPotions165.cs</c>, e7f6f8d → 2e4212e), GPL-3.0. Its pure rules
    /// and constants are kept as they were (and its unit tests run unchanged in UT_BotSinks): a heal draught at 35% health
    /// or less when hit while neither casting nor swinging; after a kill with an add still on the bot, a heal draught at
    /// 50% or less, a power draught at 20% power or less (casters) or an endurance draught at 20% or less (fighters); the
    /// item's shared reuse timer and charges as a player's; a bot with no draught does not look in its bag again for 30
    /// seconds or until it buys one; a gamebot of level 20+ keeps one heal draught when its coin is five times its reserve
    /// after paying, two at twenty times (True165's reserve, max(50, 4 x level²) copper). Changes in the port: gated by
    /// <c>bot_upkeep</c> (with <c>bot_crafters</c>), autonomous gamebots only (True165 also lets hired companions drink);
    /// the purchase is a merchant <see cref="IBotServiceStep"/> at a task break; the per-bot marks live on the bot
    /// (a weak table, so nothing to forget when it leaves); the bag revision is this package's own count of its purchases
    /// (True165 used an inventory revision added to AutonomousBotEconomy, which WP7 does not own); every coin move names
    /// <see cref="EconomyFlowKind.Potion"/> and is summed in the hourly ECON_CONSUMABLE line (True165's BOT_POTION_165
    /// keys in the per-purchase line).</para>
    /// </summary>
    public static class BotPotions
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotPotions));

        public const int HealWhenHitPercent = 35;
        public const int HealAfterKillPercent = 50;
        public const int PowerPercent = 20;
        public const int EndurancePercent = 20;
        public const int MinBuyLevel = 20;
        public const int RichReserveMultiple = 5;
        public const int VeryRichReserveMultiple = 20;
        /// <summary>A potion merchant is looked for within this radius of the bot's merchant (True165).</summary>
        public const int MerchantRadius = 2500;

        public enum Kind { None, Heal, Power, Endurance }

        /// <summary>A bag found with no potion of a kind is not looked at again for this long (unless it changes).</summary>
        public const long NoPotionRecheckMilliseconds = 30_000;

        private sealed class DrinkState
        {
            // timer group -> tick the next drink of that group is allowed
            public readonly ConcurrentDictionary<int, long> Timers = new();
            // per kind: no bag look before this tick while the revision is unchanged
            public readonly long[] SkipUntil = new long[4];
            public readonly int[] SkipRevision = new int[4];
            // this package's purchases for the bot (the "bag changed" revision)
            public int Revision;
            public string BoughtAtBreak = string.Empty;
        }

        private static readonly ConditionalWeakTable<GameBot, DrinkState> States = new();

        private static DrinkState StateOf(GameBot bot) => States.GetValue(bot, _ => new DrinkState());

        internal static readonly BotSinks.HourlySums Bought = new();
        internal static readonly BotSinks.HourlySums Drunk = new();

        #region True165's pure rules

        /// <summary>Which drink a spell type is ("Heal", "PowerHeal", "EnduranceHeal"; anything else is not drunk).</summary>
        public static Kind KindOf(string spellType) => spellType switch
        {
            "Heal" => Kind.Heal,
            "PowerHeal" => Kind.Power,
            "EnduranceHeal" => Kind.Endurance,
            _ => Kind.None,
        };

        /// <summary>Which drink a spell type is, without a string per potion.</summary>
        public static Kind KindOf(eSpellType spellType) => spellType switch
        {
            eSpellType.Heal => Kind.Heal,
            eSpellType.PowerHeal => Kind.Power,
            eSpellType.EnduranceHeal => Kind.Endurance,
            _ => Kind.None,
        };

        /// <summary>Pure: the bag is looked at for a drink of this kind (not while a mark runs for the same revision).</summary>
        public static bool ShouldLook(long now, long skipUntil, int skipRevision, int revision) =>
            now >= skipUntil || revision != skipRevision;

        /// <summary>A drinkable potion: a backpack magical item with a spell and charges (GamePlayer.UseSlot's potion test).</summary>
        public static bool IsPotion(DbInventoryItem item) =>
            item != null && item.Object_Type == (int)eObjectType.Magical && item.Item_Type == (int)eInventorySlot.FirstBackpack &&
            item.SpellID > 0 && item.Charges > 0;

        /// <summary>When hit: a heal draught at 35% or less while neither casting nor swinging.</summary>
        public static bool WantsHealWhenHit(int healthPercent, bool casting, bool attacking) =>
            healthPercent > 0 && healthPercent <= HealWhenHitPercent && !casting && !attacking;

        /// <summary>After a kill with an add still on it: which drink, if any (heal first).</summary>
        public static Kind WantsAfterKill(int healthPercent, int powerPercent, bool usesPower, int endurancePercent, bool addsLeft)
        {
            if (!addsLeft)
                return Kind.None;
            if (healthPercent > 0 && healthPercent <= HealAfterKillPercent)
                return Kind.Heal;
            if (usesPower && powerPercent <= PowerPercent)
                return Kind.Power;
            if (!usesPower && endurancePercent <= EndurancePercent)
                return Kind.Endurance;
            return Kind.None;
        }

        /// <summary>How many heal draughts a gamebot wants to own at this level and coin: 0, 1 or 2.</summary>
        public static int HealDraughtsWanted(int level, long coin, long reserve, long price)
        {
            if (level < MinBuyLevel || price <= 0)
                return 0;
            if (coin - 2 * price >= VeryRichReserveMultiple * reserve)
                return 2;
            return coin - price >= RichReserveMultiple * reserve ? 1 : 0;
        }

        /// <summary>True165's shopping reserve (AutonomousWorldBotController.ShoppingReserve): max(50, 4 x level²) copper.</summary>
        public static long Reserve(int level) => Math.Max(50L, level * level * 4L);

        #endregion

        #region Drinking (glue)

        private static bool Eligible(GameBot bot) => BotSinks.Upkeep(bot) && bot.Inventory != null && bot.IsAlive;

        /// <summary>GameBot.OnAttackedByEnemy: an emergency heal draught.</summary>
        public static void OnAttacked(GameBot bot)
        {
            if (bot == null || bot.HealthPercent > HealWhenHitPercent || !Eligible(bot))
                return;
            if (WantsHealWhenHit(bot.HealthPercent, bot.IsCasting, bot.attackComponent?.AttackState == true))
                TryDrink(bot, Kind.Heal);
        }

        /// <summary>GameBot.EnemyKilled: a draught between a kill and the add still on the bot.</summary>
        public static void AfterKill(GameBot bot)
        {
            if (bot == null || !Eligible(bot) || bot.IsCasting)
                return;
            bool addsLeft = (bot.attackComponent?.AttackerTracker?.Count ?? 0) > 0;
            if (!addsLeft)
                return;
            bool usesPower = bot.MaxMana > 0 && bot.CharacterClass?.ManaStat is not null and not eStat.UNDEFINED;
            Kind kind = WantsAfterKill(bot.HealthPercent, bot.ManaPercent, usesPower, bot.EndurancePercent, addsLeft);
            if (kind != Kind.None)
                TryDrink(bot, kind);
        }

        /// <summary>Drinks the first potion of this kind in the bag when its reuse timer is free. True when drunk.</summary>
        public static bool TryDrink(GameBot bot, Kind kind)
        {
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            if (line == null)
                return false;
            long now = GameLoop.GameLoopTime;
            DrinkState state = StateOf(bot);
            int k = (int)kind;
            int revision = Volatile.Read(ref state.Revision);
            if (!ShouldLook(now, Volatile.Read(ref state.SkipUntil[k]), Volatile.Read(ref state.SkipRevision[k]), revision))
                return false;
            ConcurrentDictionary<int, long> timers = state.Timers;
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
            {
                DbInventoryItem item = bot.Inventory.GetItem(slot);
                if (!IsPotion(item))
                    continue;
                int required = item.Template?.LevelRequirement > 0 ? item.Template.LevelRequirement : Math.Min(50, item.Level);
                if (required > bot.Level)
                    continue;
                Spell spell = SkillBase.FindSpell(item.SpellID, line);
                if (spell == null || KindOf(spell.SpellType) != kind)
                    continue;
                if (timers.TryGetValue(spell.SharedTimerGroup, out long next) && now < next)
                {
                    // the timer is shared: no other potion of this group either, and no bag look until it is free
                    MarkSkip(state, k, next, revision);
                    return false;
                }
                if (!bot.CastSpell(spell, line))
                    return false;
                timers[spell.SharedTimerGroup] = now + Math.Max(1, item.CanUseEvery) * 1000L;
                UseCharge(bot, item);
                Drunk.Add(bot.Realm, 0);
                return true;
            }
            MarkSkip(state, k, now + NoPotionRecheckMilliseconds, revision);
            return false;
        }

        private static void MarkSkip(DrinkState state, int kind, long until, int revision)
        {
            Volatile.Write(ref state.SkipRevision[kind], revision);
            Volatile.Write(ref state.SkipUntil[kind], until);
        }

        /// <summary>A charge used, as a player's: a stack loses one, the last charge uses the item up.</summary>
        private static void UseCharge(GameBot bot, DbInventoryItem item)
        {
            if (item.Count > 1)
                bot.Inventory.RemoveCountFromStack(item, 1);
            else if (--item.Charges < 1)
                bot.Inventory.RemoveCountFromStack(item, 1);
            else
                item.Dirty = true;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        #endregion

        #region Buying (glue)

        /// <summary>Whether a bag item is a draught this package keeps (heal, power or endurance) for a gamebot under upkeep.</summary>
        public static bool IsKeptDraught(GameBot bot, DbInventoryItem item)
        {
            if (!BotSinks.Upkeep(bot) || !IsPotion(item))
                return false;
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            return line != null && SkillBase.FindSpell(item.SpellID, line) is { } spell && KindOf(spell.SpellType) != Kind.None;
        }

        /// <summary>The draught templates of a kind the merchants near the bot sell for coin, cheapest first.</summary>
        public static DbItemTemplate[] DraughtsOnSale(GameBot bot, GameMerchant at, Kind kind)
        {
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            if (line == null)
                return Array.Empty<DbItemTemplate>();
            return new[] { at }.Concat(bot.GetNPCsInRadius(MerchantRadius).OfType<GameMerchant>())
                .Where(m => m != null && AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null &&
                            (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .Distinct()
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                .Where(t => t.Price > 0 && t.Object_Type == (int)eObjectType.Magical && t.Item_Type == (int)eInventorySlot.FirstBackpack &&
                            t.SpellID > 0 && t.MaxCharges > 0 && (t.LevelRequirement > 0 ? t.LevelRequirement : t.Level) <= bot.Level &&
                            SkillBase.FindSpell(t.SpellID, line) is { } s && KindOf(s.SpellType) == kind)
                .GroupBy(t => t.Id_nb).Select(g => g.First())
                .OrderBy(t => t.Price).ToArray();
        }

        /// <summary>How many draughts of a kind the bot carries.</summary>
        public static int Carried(GameBot bot, Kind kind)
        {
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            if (line == null || bot?.Inventory == null)
                return 0;
            int owned = 0;
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
                if (bot.Inventory.GetItem(slot) is { } item && IsPotion(item) &&
                    SkillBase.FindSpell(item.SpellID, line) is { } owns && KindOf(owns.SpellType) == kind)
                    owned += Math.Max(1, item.Count);
            return owned;
        }

        /// <summary>
        /// Buys draughts of a template into the bag, one at a time, paying each from the bot's coin with the kind given.
        /// Returns how many it bought. A failed bag add is refunded.
        /// </summary>
        public static int Buy(GameBot bot, DbItemTemplate draught, int count, EconomyFlowKind kind)
        {
            int bought = 0;
            for (; bought < count; bought++)
            {
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid || !BotSinks.Spend(bot.DatabaseID, draught.Price, kind))
                    break;
                DbInventoryItem item = GameInventoryItem.Create(draught);
                if (item == null || !bot.Inventory.AddItem(slot, item))
                {
                    BotSinks.Pay(bot.DatabaseID, draught.Price, kind);
                    break;
                }
            }
            if (bought == 0)
                return 0;
            DrinkState state = StateOf(bot);
            Interlocked.Increment(ref state.Revision);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            if (kind == EconomyFlowKind.Potion)
                Bought.Add(bot.Realm, draught.Price * bought, bought);
            if (EconomyDiagnostics.TradeLines && Log.IsInfoEnabled)
                Log.Info(EconomyLog.Line(EconomyLog.EconConsumable, ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm),
                    ("level", bot.Level), ("kind", kind), ("item", draught.Name), ("count", bought), ("copper", draught.Price * bought)));
            return bought;
        }

        /// <summary>
        /// True165's RestockHealDraughts at a task break: a rich gamebot of level 20+ buys heal draughts from a potion
        /// merchant near its merchant, up to what <see cref="HealDraughtsWanted"/> says. Returns how many it bought.
        /// </summary>
        public static int RestockHealDraughts(GameBot bot, GameMerchant at, out DbItemTemplate draught)
        {
            draught = null;
            if (!BotSinks.Upkeep(bot) || bot.Inventory == null || bot.Level < MinBuyLevel)
                return 0;
            long reserve = Reserve(bot.Level);
            long coin = BotSinks.CoinOf(bot.DatabaseID);
            // the cheapest a draught can be: nothing to look at while the bot is far from rich
            if (HealDraughtsWanted(bot.Level, coin, reserve, 1) == 0)
                return 0;
            int owned = Carried(bot, Kind.Heal);
            if (owned >= 2)
                return 0;
            draught = DraughtsOnSale(bot, at, Kind.Heal).FirstOrDefault();
            if (draught == null)
                return 0;
            int wanted = HealDraughtsWanted(bot.Level, coin, reserve, draught.Price);
            return wanted <= owned ? 0 : Buy(bot, draught, wanted - owned, EconomyFlowKind.Potion);
        }

        /// <summary>The potion purchase as a merchant step (once a break).</summary>
        public sealed class Step : IBotServiceStep
        {
            public static readonly Step Instance = new();
            public string Name => "potions (WP7)";
            public int Order => 110;
            public eWorldServiceKind Kind => eWorldServiceKind.Vendor;

            public bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result)
            {
                result = default;
                if (!BotSinks.Upkeep(bot) || service is not GameMerchant merchant)
                    return false;
                DrinkState state = StateOf(bot);
                string breakId = BotSinks.BreakOf(bot);
                if (state.BoughtAtBreak == breakId)
                    return false;
                state.BoughtAtBreak = breakId;
                int bought = RestockHealDraughts(bot, merchant, out DbItemTemplate draught);
                if (bought == 0)
                    return false;
                result = new BotServiceResult($"Bought {bought} {draught.Name}", $"Paid {draught.Price * bought} saved copper for draughts to drink in a fight",
                    3_000, eAutonomousProgressKind.Money);
                return true;
            }
        }

        #endregion

        /// <summary>Tests: the hour's sums back to 0.</summary>
        public static void ResetForTests()
        {
            Bought.Take();
            Drunk.Take();
        }
    }
}
