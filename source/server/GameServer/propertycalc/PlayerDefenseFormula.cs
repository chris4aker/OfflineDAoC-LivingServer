namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The player parry, block and evade formulas (per-mille, before buffs).
    /// Real players and GameBots (autonomous gamebots and /spawn companions)
    /// both use these, so a bot's defense comes from its class abilities, specs,
    /// level and equipment exactly like a player's. Ordinary NPCs keep their
    /// template ParryChance / BlockChance / EvadeChance.
    /// </summary>
    public static class PlayerDefenseFormula
    {
        /// <summary>Living objects that use the player defense rules.</summary>
        public static bool UsesPlayerDefense(GameObject living) => living is GamePlayer or GameBot;

        /// <summary>Parry: only with the Parry specialization. 5% base, +0.5% per spec level above 1, dexterity bonus.</summary>
        public static int Parry(int dexterity, bool hasParrySpec, int modifiedParrySpec) =>
            hasParrySpec ? (dexterity * 2 - 100) / 4 + (modifiedParrySpec - 1) * (10 / 2) + 50 : 0;

        /// <summary>Block: 5% base, +0.5% per Shields level above 1, dexterity bonus (the Shield ability and a shield are checked in TryBlock).</summary>
        public static int Block(int dexterity, int modifiedShieldSpec) =>
            (dexterity * 2 - 100) / 4 + (modifiedShieldSpec - 1) * (10 / 2) + 50;

        /// <summary>Evade: only with the Evade ability; grows with its level, quickness and dexterity.</summary>
        public static int Evade(int quickness, int dexterity, int evadeAbilityLevel) =>
            evadeAbilityLevel > 0 ? (900 + quickness + dexterity) * evadeAbilityLevel * 5 / 100 : 0;

        /// <summary>
        /// Frontal evade arc of a player or gamebot: 120 degrees (OpenDAoC-Core 50027c4f7, after
        /// chadwickgjohnson.com/data/20080715114516). Ordinary NPCs keep 180: narrowing it would weaken monsters,
        /// which is held for the owner.
        /// </summary>
        public const double PlayerEvadeArc = 120;
        public const double NpcEvadeArc = 180;

        /// <summary>
        /// Evade chance divisor for several melee attackers (players and gamebots only): each attacker beyond the first
        /// counts a third, so 4 attackers halve it (OpenDAoC-Core a36db6f43; grab bags and live tests show a smaller
        /// reduction than parry's). Upstream later dropped it for NPCs (ab90dbf12); ours never applies it to NPCs.
        /// </summary>
        public static double EvadeAttackerDivisor(int meleeAttackerCount) =>
            meleeAttackerCount > 1 ? 1 + (meleeAttackerCount - 1) / 3.0 : 1;
    }
}
