namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// What a gamebot coin move was for. The union of our plan's kinds and True165's BotMoneyLedger165.Kind (a8c793e,
    /// df86f9f), so WP2's port keeps True165's names in its lines: LootCoin, VendorSale, VendorGear, ExchangeSale,
    /// Repair, Healer, CraftMaterials, CraftSale, CraftOrder, Enchant, Dye, Ammo, Poison, Potion, Travel, Siege, Guild,
    /// KillTask. A caller may name its kind; otherwise WP2's classifier names it from the caller's file and member.
    /// </summary>
    public enum EconomyFlowKind
    {
        Unattributed = 0,
        // True165's kinds, in its order
        LootCoin,
        VendorSale,
        VendorGear,
        ExchangeSale,
        Repair,
        Healer,
        CraftMaterials,
        CraftSale,
        CraftOrder,
        Enchant,
        Dye,
        Ammo,
        Poison,
        Potion,
        Travel,
        Siege,
        Guild,
        KillTask,
        // ours
        LootShare,
        ExchangeTax,
        StableTicket,
        Medallion,
        RepairKit,
        BeggarGift,
        Commission,
        Consumable,
        Luxury,
        Refund,
        LauncherGrant,
    }

    /// <summary>Receives every gamebot coin move (WP2's ledger). Called outside the money lock; must be cheap and thread-safe.</summary>
    public interface IEconomyFlowSink
    {
        /// <param name="signedCopper">Positive for coin received, negative for coin paid.</param>
        void Record(long botId, long signedCopper, EconomyFlowKind kind, string callerFile, string callerMember);
    }

    /// <summary>
    /// The flow seam on AutonomousBotEconomy.AddMoney and TrySpend. Their new optional parameters (a kind, then the
    /// compiler-filled caller file and member) keep every existing call source-compatible, so the ~25 call sites need no
    /// edit for the ledger to attribute them. The default sink is null: nothing is recorded and no amount changes.
    /// </summary>
    public static class EconomyFlows
    {
        private static volatile IEconomyFlowSink _sink;

        public static IEconomyFlowSink Sink
        {
            get => _sink;
            set => _sink = value;
        }

        public static void Record(long botId, long signedCopper, EconomyFlowKind kind, string callerFile, string callerMember)
        {
            IEconomyFlowSink sink = _sink;
            if (sink == null || signedCopper == 0)
                return;
            try { sink.Record(botId, signedCopper, kind, FileName(callerFile), callerMember ?? string.Empty); }
            catch { } // diagnostics never break a coin move
        }

        /// <summary>The bare file name of a [CallerFilePath] (built on Windows or Linux).</summary>
        public static string FileName(string callerFile)
        {
            if (string.IsNullOrEmpty(callerFile))
                return string.Empty;
            int slash = callerFile.LastIndexOfAny(['/', '\\']);
            return slash >= 0 ? callerFile[(slash + 1)..] : callerFile;
        }
    }
}
