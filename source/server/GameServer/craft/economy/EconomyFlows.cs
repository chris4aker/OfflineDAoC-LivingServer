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
        // WP8: coin between a real player and a gamebot (a sale in a trade window, a commission paid on delivery, change
        // given back). Both sides are in the money supply, so it is a transfer (EconomyFlowClassifier.IsTransfer).
        PlayerTrade,
        // Hired companions (from True 1.65): the coin a player pays to hire a companion leaves the money supply (a sink).
        // A player-side move (EconomyFlows.RecordPlayer); no bot purse moves.
        CompanionHire,
    }

    /// <summary>Receives every gamebot coin move (WP2's ledger). Called outside the money lock; must be cheap and thread-safe.</summary>
    public interface IEconomyFlowSink
    {
        /// <param name="signedCopper">Positive for coin received, negative for coin paid.</param>
        void Record(long botId, long signedCopper, EconomyFlowKind kind, string callerFile, string callerMember);

        /// <summary>
        /// WP8: coin a real player paid into a craft order's escrow (negative), or got back from it (positive). Escrow is
        /// outside the money supply, as for a gamebot's order; this is the player's side of it, so the realm's flows match
        /// the change in supply. The default records nothing.
        /// </summary>
        void RecordPlayerEscrow(int realm, long signedCopper) { }

        /// <summary>
        /// Hired companions: coin a real player paid to the world (negative) or got from it (positive) for a named reason
        /// (a companion's hire, a companion's repairs). It leaves or enters the money supply but no bot purse, so it is
        /// counted in the realm's flows and its kind's sum, never in the bots' own flows. The default records nothing.
        /// </summary>
        void RecordPlayer(int realm, long signedCopper, EconomyFlowKind kind) { }
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

        /// <summary>WP8: a player's coin into (negative) or out of (positive) a craft order's escrow.</summary>
        public static void RecordPlayerEscrow(eRealm realm, long signedCopper)
        {
            IEconomyFlowSink sink = _sink;
            if (sink == null || signedCopper == 0)
                return;
            try { sink.RecordPlayerEscrow((int)realm, signedCopper); }
            catch { } // diagnostics never break a coin move
        }

        /// <summary>Hired companions: a player's coin paid to (negative) or received from (positive) the world.</summary>
        public static void RecordPlayer(eRealm realm, long signedCopper, EconomyFlowKind kind)
        {
            IEconomyFlowSink sink = _sink;
            if (sink == null || signedCopper == 0)
                return;
            try { sink.RecordPlayer((int)realm, signedCopper, kind); }
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
