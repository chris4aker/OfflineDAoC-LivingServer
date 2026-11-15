namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The player upkeep setting (design facet I, decision 5), pure. Read by Repair.cs (WP1, durability) and
    /// Blacksmith.cs (WP3, cost); nothing reads it in WP0. Players only: gamebots' upkeep is bot_upkeep (WP7).
    /// </summary>
    public static class UpkeepPolicy
    {
        public enum Mode { Classic, Light, Free }

        /// <summary>The mode the server is set to (player_upkeep).</summary>
        public static Mode Current => CraftingEconomyProperties.ParseUpkeep(CraftingEconomyProperties.PLAYER_UPKEEP);

        /// <summary>What a repair costs under the mode: classic the smith's price, light half of it (rounded up, so a
        /// 1-copper repair still costs 1), free nothing.</summary>
        public static long RepairCost(long classicCost, Mode mode) => classicCost <= 0 ? 0 : mode switch
        {
            Mode.Free => 0,
            Mode.Light => (classicCost + 1) / 2,
            _ => classicCost,
        };

        /// <summary>Whether a repair spends durability (classic only).</summary>
        public static bool ConsumesDurability(Mode mode) => mode == Mode.Classic;
    }
}
