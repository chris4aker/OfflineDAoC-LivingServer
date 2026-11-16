using System;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// What Crafting 2.0 WP7 (sinks) registers in WP0's seams, so no shared file is edited for it:
    /// <list type="bullet">
    /// <item><see cref="BotGearWear.Errand"/> (IBotTownVisitNeed): True165's repair errand below 88%.</item>
    /// <item>Merchant steps (IBotServiceStep, at a natural task break): <see cref="BotGearWear.Step"/> (repairs, 100),
    /// <see cref="BotPotions.Step"/> (heal draughts, 110), <see cref="BotPoisonSupply.Step"/> (poison vials, 120),
    /// <see cref="BotLuxury.Step"/> (luxury, 200).</item>
    /// <item><see cref="ItemPolicy"/> (IBotItemPolicy): the draughts and vials the upkeep buys are never sold, listed or
    /// cleared from a full bag.</item>
    /// <item><see cref="SinkGovernor.Instance"/> and <see cref="HourlyLines"/> as stores (the governor's hourly read, the
    /// hourly ECON_WEAR, ECON_CONSUMABLE, ECON_LUXURY and ECON_SIEGE_SUPPLY sums per realm).</item>
    /// </list>
    /// The wear itself is called from GameInventoryItem.OnBotItemUsed (armor hit, weapon swing, harmful spell), the
    /// drinking from GameBot, the siege supply from BotSiegeRuntime. With the switches off (the default) every member
    /// answers as today's code: nothing wears, no errand, no step does anything, nothing is kept, no line is written.
    /// </summary>
    public static class SinkSeams
    {
        private static int _registered;

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) => Register();

        /// <summary>Registers every WP7 implementation once.</summary>
        public static void Register()
        {
            if (Interlocked.Exchange(ref _registered, 1) != 0)
                return;
            BotServiceSeams.Register(BotGearWear.Errand.Instance);
            BotServiceSeams.Register(BotGearWear.Step.Instance);
            BotServiceSeams.Register(BotPotions.Step.Instance);
            BotServiceSeams.Register(BotPoisonSupply.Step.Instance);
            BotServiceSeams.Register(BotLuxury.Step.Instance);
            EconomySeams.Register(ItemPolicy.Instance);
            EconomyLifecycle.Register(SinkGovernor.Instance);
            EconomyLifecycle.Register(HourlyLines.Instance);
        }

        /// <summary>Tests only: register again after the seams were reset.</summary>
        public static void RegisterForTests()
        {
            Interlocked.Exchange(ref _registered, 0);
            Register();
        }

        /// <summary>Tests only: every WP7 state back to empty.</summary>
        public static void ResetForTests()
        {
            Interlocked.Exchange(ref _registered, 0);
            BotGearWear.ResetForTests();
            BotPotions.ResetForTests();
            BotPoisonSupply.ResetForTests();
            BotLuxury.ResetForTests();
            SinkGovernor.Instance.ResetForTests();
            BotSiegeRuntime.Supplied.Take();
            BotSiegeRuntime.Margins.Take();
            BotSiegeRuntime.FindSupplier = DefaultSupplier;
            HourlyLines.Instance.ResetForTests();
            BotSinks.ResetCoinForTests();
        }

        private static readonly Func<GameBot, CrafterState> DefaultSupplier = BotSiegeRuntime.FindSupplier;

        /// <summary>The consumables the upkeep buys stay in the bag until used.</summary>
        public sealed class ItemPolicy : IBotItemPolicy
        {
            public static readonly ItemPolicy Instance = new();
            public string Name => "sinks (WP7)";

            public bool IsReserved(GameBot bot, DbInventoryItem item) => Kept(bot, item);
            public bool KeepFromVendor(GameBot bot, DbInventoryItem item) => Kept(bot, item);

            private static bool Kept(GameBot bot, DbInventoryItem item)
            {
                if (item == null || !BotSinks.Upkeep(bot))
                    return false;
                if (item.Object_Type == (int)eObjectType.Poison)
                    return BotPoisonSupply.IsVial(item) && bot.CharacterClass != null &&
                           BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID);
                return BotPotions.IsKeptDraught(bot, item);
            }
        }

        /// <summary>The hourly sums as lines (one per realm and kind that moved), while its switch is on.</summary>
        public sealed class HourlyLines : IEconomyStore
        {
            private static readonly Logger Log = LoggerManager.Create(typeof(HourlyLines));
            public static readonly HourlyLines Instance = new();
            public const long HourMilliseconds = 3_600_000;
            private long _nextHour;

            /// <summary>Where the lines go (tests replace it).</summary>
            public Action<string> Write = line => { if (Log.IsInfoEnabled) Log.Info(line); };

            public string Name => "sinks hourly lines (WP7)";
            public void Load() { }
            public void Flush(bool wait) { }

            public void Pulse(long now)
            {
                long next = Volatile.Read(ref _nextHour);
                if (next == 0)
                {
                    Volatile.Write(ref _nextHour, now + HourMilliseconds);
                    return;
                }
                if (now < next)
                    return;
                Volatile.Write(ref _nextHour, now + HourMilliseconds);
                WriteHour();
            }

            /// <summary>Writes the hour's lines and starts a new hour (public for tests).</summary>
            public void WriteHour()
            {
                var wear = BotGearWear.WearSums.Take();
                var crossed = BotGearWear.CrossedSums.Take();
                var repairs = BotGearWear.RepairSums.Take();
                var crafterRepairs = BotGearWear.CrafterRepairSums.Take();
                var potions = BotPotions.Bought.Take();
                var drunk = BotPotions.Drunk.Take();
                var vials = BotPoisonSupply.Bought.Take();
                var coats = BotPoisonSupply.Coats.Take();
                var missed = BotPoisonSupply.Missed.Take();
                var luxury = BotLuxury.Spent.Take();
                var goods = BotLuxury.Goods.Take();
                var supplied = BotSiegeRuntime.Supplied.Take();
                var margins = BotSiegeRuntime.Margins.Take();
                foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                {
                    int r = (int)realm;
                    if (BotSinks.UpkeepOn)
                    {
                        if (wear.Count[r] + repairs.Count[r] > 0)
                            Write(EconomyLog.Line(EconomyLog.EconWear, ("realm", realm), ("wearRolls", wear.Count[r]),
                                ("conditionLost", wear.Copper[r]), ("below88", crossed.Count[r]), ("repaired", repairs.Count[r]),
                                ("repairCopper", repairs.Copper[r]), ("toCrafters", crafterRepairs.Copper[r])));
                        if (potions.Count[r] + drunk.Count[r] + vials.Count[r] + coats.Count[r] > 0)
                            Write(EconomyLog.Line(EconomyLog.EconConsumable, ("realm", realm), ("potionsBought", potions.Count[r]),
                                ("potionCopper", potions.Copper[r]), ("potionsDrunk", drunk.Count[r]), ("vialsBought", vials.Count[r]),
                                ("vialCopper", vials.Copper[r]), ("coats", coats.Count[r]), ("coatChecksWithoutVial", missed.Count[r])));
                    }
                    if (BotSinks.LuxuryOn && luxury.Count[r] > 0)
                        Write(EconomyLog.Line(EconomyLog.EconLuxury, ("realm", realm), ("spends", luxury.Count[r]),
                            ("copper", luxury.Copper[r]), ("draughts", goods.Count[r]), ("draughtCopper", goods.Copper[r]),
                            ("servicesCopper", luxury.Copper[r] - goods.Copper[r]),
                            ("intensity", Math.Round(SinkGovernor.Instance.Intensity(realm), 3))));
                    if (BotSinks.SiegeSupplyOn && supplied.Count[r] > 0)
                        Write(EconomyLog.Line(EconomyLog.EconSiegeSupply, ("realm", realm), ("supplies", supplied.Count[r]),
                            ("copper", supplied.Copper[r]), ("margin", margins.Copper[r]),
                            ("materials", supplied.Copper[r] - margins.Copper[r])));
                }
            }

            public void ResetForTests()
            {
                Volatile.Write(ref _nextHour, 0);
                Write = line => { if (Log.IsInfoEnabled) Log.Info(line); };
            }
        }
    }
}
