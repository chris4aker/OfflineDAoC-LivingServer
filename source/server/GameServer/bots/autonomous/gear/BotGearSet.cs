using System;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Crafting 2.0 WP6 (M3): the template-aware valuation. A gamebot's worn gear is scored as one set on True165's
    /// class weights and item caps (<see cref="BotGearValue165"/>, 1edbcf5): each typed bonus counts at its class weight
    /// only up to the cap of the whole set (stats 1.5 x level, hit points 4 x level, resists level/2 + 1, skills
    /// level/5 + 1, power level/2 + 1), so a second +15 Strength piece over a capped set adds nothing and a piece that
    /// fills a missing resist adds its full weight. Each piece adds its EquipmentValue without its typed bonuses, its
    /// level counted only up to the bot's and its DPS/AF only up to the bot's cap (BotGearValue165.CapAdjustment). At
    /// level 50 the six armour slots also count their quality above 94% (<see cref="QualityPointWeight"/> a point): that
    /// is the template (a capped spellcrafted set of 99-100% crafted pieces), so a 96% drop with odd lines loses to a
    /// 99% crafted piece with the right gems. Below 50 the score's change for one slot is exactly True165's FitValue
    /// difference for bonuses that are not negative (UT_TemplateValuation).
    ///
    /// The NO-UNDRESS rule is stated on this score: with bot_crafters on, a gamebot never equips anything that lowers it
    /// and never leaves a worn slot empty (<see cref="BotGearGuard"/>). Pure arithmetic over at most 22 worn pieces and
    /// their 11 bonus slots; no database, no lock beyond the inventory's own reads, safe from parallel brain turns.
    /// </summary>
    public static class BotGearSet
    {
        public const int TemplateLevel = 50;
        /// <summary>A template piece: crafted, of this quality or better.</summary>
        public const int TemplateQuality = 99;
        /// <summary>At level 50, each quality point above <see cref="QualityReference"/> on an armour slot.</summary>
        public const int QualityPointWeight = 6;
        public const int QualityReference = 94;

        /// <summary>The six armour slots of a template (also the slots the gear check looks at).</summary>
        public static readonly eInventorySlot[] TemplateSlots =
        [
            eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor,
            eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor,
        ];

        /// <summary>The worn slots whose piece is scored: weapons, armour, cloak and jewellery (not quivers or mythical,
        /// which is post-era).</summary>
        public static readonly eInventorySlot[] ScoredSlots =
        [
            eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon, eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon,
            eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.Jewelry,
            eInventorySlot.TorsoArmor, eInventorySlot.Cloak, eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor,
            eInventorySlot.Neck, eInventorySlot.Waist, eInventorySlot.LeftBracer, eInventorySlot.RightBracer,
            eInventorySlot.LeftRing, eInventorySlot.RightRing,
        ];

        /// <summary>Slot-indexed loadout arrays have this length (eInventorySlot.MaxEquipable + 1).</summary>
        public const int LoadoutLength = (int)eInventorySlot.MaxEquipable + 1;

        private const int PropertyCount = 256;

        public static bool IsScoredSlot(eInventorySlot slot) => Array.IndexOf(ScoredSlots, slot) >= 0;

        public static bool IsTemplateSlot(eInventorySlot slot) => Array.IndexOf(TemplateSlots, slot) >= 0;

        /// <summary>A template piece: crafted and 99% or better.</summary>
        public static bool IsTemplatePiece(DbInventoryItem item) => item != null && item.IsCrafted && item.Quality >= TemplateQuality;

        /// <summary>
        /// Whom the set is scored for: the bot's level and its class weight per bonus type. The weights are worked out
        /// on first use and kept for the life of the context (one decision), so a trained-skill check runs once a type.
        /// </summary>
        public sealed class Context
        {
            private readonly Func<eProperty, int> _weight;
            private readonly int[] _weights = new int[PropertyCount];

            public int Level { get; }

            public Context(int level, Func<eProperty, int> weight)
            {
                Level = Math.Clamp(level, 1, 50);
                _weight = weight ?? (_ => BotGearValue165.BaseWeight);
                Array.Fill(_weights, int.MinValue);
            }

            public int Weight(int propertyType)
            {
                if (propertyType is <= 0 or >= PropertyCount)
                    return BotGearValue165.BaseWeight;
                int weight = Volatile.Read(ref _weights[propertyType]);
                if (weight == int.MinValue)
                    Volatile.Write(ref _weights[propertyType], weight = _weight((eProperty)propertyType));
                return weight;
            }
        }

        /// <summary>The bot's context: its level and True165's class weights (BotGearValue165.Weight).</summary>
        public static Context For(GameBot bot) => new(bot?.Level ?? 1, property => BotGearValue165.Weight(bot, property));

        /// <summary>The bot's worn pieces, indexed by slot (<see cref="LoadoutLength"/> entries; unscored slots stay null).</summary>
        public static DbInventoryItem[] Worn(GameBot bot)
        {
            var worn = new DbInventoryItem[LoadoutLength];
            if (bot?.Inventory == null)
                return worn;
            foreach (eInventorySlot slot in ScoredSlots)
                worn[(int)slot] = bot.Inventory.GetItem(slot);
            return worn;
        }

        /// <summary>How many scored slots hold a piece.</summary>
        public static int Filled(DbInventoryItem[] worn)
        {
            int filled = 0;
            if (worn != null)
                foreach (eInventorySlot slot in ScoredSlots)
                    if (worn[(int)slot] != null)
                        filled++;
            return filled;
        }

        /// <summary>How many of the six armour slots hold a template piece.</summary>
        public static int TemplatePieces(DbInventoryItem[] worn)
        {
            int pieces = 0;
            if (worn != null)
                foreach (eInventorySlot slot in TemplateSlots)
                    if (IsTemplatePiece(worn[(int)slot]))
                        pieces++;
            return pieces;
        }

        /// <summary>A piece's own value in a slot, without its typed bonuses (those are scored for the whole set).</summary>
        public static long PieceValue(Context context, DbInventoryItem item, eInventorySlot slot)
        {
            int equipment = AutonomousBotEconomy.EquipmentValue(item);
            // EquipmentValue is 0 for a melee weapon without working stats: it adds nothing, nor do its bonuses.
            if (equipment == 0)
                return 0;
            long value = equipment + BotGearValue165.CapAdjustment(context.Level, item.Level, item.DPS_AF, item.Object_Type);
            foreach ((int type, int bonus) in BotGearValue165.Bonuses(item))
                if (type > 0 && bonus != 0)
                    value -= BotGearValue165.BaseWeight * (long)Math.Abs(bonus);
            if (context.Level >= TemplateLevel && IsTemplateSlot(slot))
                value += QualityPointWeight * (long)Math.Max(0, item.Quality - QualityReference);
            return value;
        }

        /// <summary>The set score of a slot-indexed loadout (see the class summary).</summary>
        public static long Score(Context context, DbInventoryItem[] worn) =>
            Score(context, worn, eInventorySlot.Invalid, null);

        /// <summary>
        /// The set score of <paramref name="worn"/> as <see cref="With"/> would change it (<paramref name="wearSlot"/> =
        /// Invalid: unchanged), read in place without the copy (issue #540: the copies were the Exchange scan's
        /// DbInventoryItem[] allocations).
        /// </summary>
        private static long Score(Context context, DbInventoryItem[] worn, eInventorySlot wearSlot, DbInventoryItem wear)
        {
            if (context == null || worn == null)
                return 0;
            bool wearing = (int)wearSlot is >= 0 and < LoadoutLength;
            Span<int> totals = stackalloc int[PropertyCount];
            totals.Clear();
            long score = 0;
            foreach (eInventorySlot slot in ScoredSlots)
            {
                DbInventoryItem item = (int)slot < worn.Length ? worn[(int)slot] : null;
                if (wearing)
                {
                    // With: a piece moved from another worn slot leaves that slot, and the slot worn takes it.
                    if (slot == wearSlot)
                        item = wear;
                    else if (wear != null && ReferenceEquals(item, wear))
                        item = null;
                }
                if (item == null)
                    continue;
                long piece = PieceValue(context, item, slot);
                if (piece == 0 && AutonomousBotEconomy.EquipmentValue(item) == 0)
                    continue;
                score += piece;
                foreach ((int type, int bonus) in BotGearValue165.Bonuses(item))
                    if (type is > 0 and < PropertyCount && bonus != 0)
                        totals[type] += bonus;
            }
            for (int type = 1; type < PropertyCount; type++)
            {
                int total = totals[type];
                if (total == 0)
                    continue;
                int cap = BotGearValue165.Cap(context.Level, (eProperty)type);
                score += context.Weight(type) * (long)(cap == int.MaxValue ? total : Math.Min(cap, total));
            }
            return score;
        }

        /// <summary>The loadout with <paramref name="item"/> worn in <paramref name="slot"/> (a copy; the input is unchanged).</summary>
        public static DbInventoryItem[] With(DbInventoryItem[] worn, eInventorySlot slot, DbInventoryItem item)
        {
            var next = new DbInventoryItem[LoadoutLength];
            if (worn != null)
                Array.Copy(worn, next, Math.Min(worn.Length, LoadoutLength));
            if ((int)slot is >= 0 and < LoadoutLength)
            {
                // A piece moved from another worn slot leaves that slot (the swap the guard must see).
                for (int i = 0; i < next.Length; i++)
                    if (item != null && ReferenceEquals(next[i], item))
                        next[i] = null;
                next[(int)slot] = item;
            }
            return next;
        }

        /// <summary>How much wearing <paramref name="item"/> in <paramref name="slot"/> changes the set score.</summary>
        public static long Delta(Context context, DbInventoryItem[] worn, eInventorySlot slot, DbInventoryItem item) =>
            worn == null ? Score(context, With(null, slot, item)) : Score(context, worn, slot, item) - Score(context, worn);

        [ThreadStatic] private static GameBot _scanBot;
        [ThreadStatic] private static Context _scanContext;
        [ThreadStatic] private static DbInventoryItem[] _scanWorn;
        [ThreadStatic] private static long _scanWornScore;
        [ThreadStatic] private static int _scanDepth;

        /// <summary>
        /// Issue #540: one context, one worn loadout and one score of it for a whole read-only scan of candidates (the
        /// Exchange purchase and gap-fill scans score every affordable listing, and each <see cref="ScanDelta"/> used to
        /// build its own context (a 256-entry weight table), copy the worn set twice and score it again). Use it only
        /// where nothing is worn or taken off before the scope ends. Scopes nest; only the outermost one sets the bot.
        /// </summary>
        public static ScanScope BeginScan(GameBot bot)
        {
            if (_scanDepth++ == 0)
            {
                _scanBot = bot;
                _scanContext = null;
                _scanWorn = null;
            }
            return new ScanScope();
        }

        public readonly struct ScanScope : IDisposable
        {
            public void Dispose()
            {
                if (_scanDepth > 0 && --_scanDepth == 0)
                {
                    _scanBot = null;
                    _scanContext = null;
                    _scanWorn = null;
                }
            }
        }

        /// <summary>Whether a scan for this bot is open on this thread (tests).</summary>
        public static bool ScanOpenFor(GameBot bot) => _scanDepth > 0 && bot != null && ReferenceEquals(_scanBot, bot);

        /// <summary>
        /// <see cref="Delta"/> for the bot's own worn set: inside a <see cref="BeginScan"/> for this bot, its context,
        /// worn set and worn score are made once; outside, exactly as before.
        /// </summary>
        public static long ScanDelta(GameBot bot, eInventorySlot slot, DbInventoryItem item)
        {
            if (!ScanOpenFor(bot))
                return Delta(For(bot), Worn(bot), slot, item);
            if (_scanContext == null)
            {
                _scanContext = For(bot);
                _scanWorn = Worn(bot);
                _scanWornScore = Score(_scanContext, _scanWorn);
            }
            return Score(_scanContext, _scanWorn, slot, item) - _scanWornScore;
        }
    }

    /// <summary>
    /// The NO-UNDRESS guard (Crafting 2.0 WP6, the plan's risk 6): with bot_crafters on, the last check before a gamebot
    /// puts a piece on. The move is refused when it would lower the set score (<see cref="BotGearSet"/>) or leave a worn
    /// slot empty. The upgrade rules already decide on the same score, so the guard should never refuse anything: every
    /// refusal is an ECON_UNDRESS_GUARD event="blocked" line, and the hourly ECON_UNDRESS_GUARD line must read blocked=0
    /// (UT_NoUndress checks 10,000 random loadouts for that).
    /// </summary>
    public static class BotGearGuard
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BotGearGuard));
        private static long _checked, _blocked, _blockedTotal;

        /// <summary>Equips checked since the last hourly line.</summary>
        public static long Checked => Interlocked.Read(ref _checked);
        /// <summary>Equips refused since the last hourly line.</summary>
        public static long Blocked => Interlocked.Read(ref _blocked);
        /// <summary>Equips refused since start (never reset by the hourly line).</summary>
        public static long BlockedSinceStart => Interlocked.Read(ref _blockedTotal);

        /// <summary>The counts for the hourly line, then zero.</summary>
        public static (long Checked, long Blocked) TakeHour() => (Interlocked.Exchange(ref _checked, 0), Interlocked.Exchange(ref _blocked, 0));

        public static void ResetForTests()
        {
            Interlocked.Exchange(ref _checked, 0);
            Interlocked.Exchange(ref _blocked, 0);
            Interlocked.Exchange(ref _blockedTotal, 0);
        }

        /// <summary>The pure rule: the score does not fall and no worn slot is left empty.</summary>
        public static bool Allows(BotGearSet.Context context, DbInventoryItem[] before, DbInventoryItem[] after,
            out long scoreBefore, out long scoreAfter)
        {
            scoreBefore = BotGearSet.Score(context, before);
            scoreAfter = BotGearSet.Score(context, after);
            if (scoreAfter < scoreBefore)
                return false;
            if (before != null && after != null)
                foreach (eInventorySlot slot in BotGearSet.ScoredSlots)
                    if (before[(int)slot] != null && after[(int)slot] == null)
                        return false;
            return true;
        }

        /// <summary>
        /// Whether this gamebot may put <paramref name="item"/> on in <paramref name="slot"/> now. Always true while the
        /// template-aware valuation is off for the bot (bot_crafters off, a companion, an in-memory bot).
        /// </summary>
        public static bool Allows(GameBot bot, DbInventoryItem item, eInventorySlot slot, string source)
        {
            if (!AutonomousBotEconomy.UsesGearValuation(bot) || item == null || !BotGearSet.IsScoredSlot(slot))
                return true;
            BotGearSet.Context context = BotGearSet.For(bot);
            DbInventoryItem[] before = BotGearSet.Worn(bot);
            DbInventoryItem[] after = BotGearSet.With(before, slot, item);
            Interlocked.Increment(ref _checked);
            if (Allows(context, before, after, out long scoreBefore, out long scoreAfter))
                return true;
            Interlocked.Increment(ref _blocked);
            Interlocked.Increment(ref _blockedTotal);
            try
            {
                if (Log.IsWarnEnabled)
                    Log.Warn(CraftingEconomy.EconomyLog.Line(CraftingEconomy.EconomyLog.EconUndressGuard,
                        ("event", "blocked"), ("bot", bot.Name), ("id", bot.DatabaseID), ("realm", bot.Realm), ("level", bot.Level),
                        ("slot", slot), ("item", item.Name), ("worn", before[(int)slot]?.Name ?? "empty"), ("source", source),
                        ("before", scoreBefore), ("after", scoreAfter),
                        ("filledBefore", BotGearSet.Filled(before)), ("filledAfter", BotGearSet.Filled(after))));
            }
            catch { } // a diagnostics failure never changes what the bot wears
            return false;
        }
    }
}
