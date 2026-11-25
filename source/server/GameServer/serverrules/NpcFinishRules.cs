using DOL.GS.ServerProperties;

namespace DOL.GS.ServerRules
{
    /// <summary>
    /// Port of OpenDAoC-Core cbe604ce1 and 978b991fc (live compliance): a monster, guard or pet that has started a spell or a
    /// bow draw lands it unless it is crowd controlled. Line of sight and range are checked when it starts, not during the
    /// cast or draw nor at its end. Four server properties turn each check back on (all off by default, as upstream).
    /// <para>Autonomous gamebots and companions are GameNPCs but play by the player's rules: they keep every check, as a
    /// player's spell fails when its target steps behind a wall or out of range. Upstream's version had no such exemption
    /// (the harvest's warning on B4).</para>
    /// </summary>
    public static class NpcFinishRules
    {
        /// <summary>Whether this caster or shooter lands what it started (a non-gamebot NPC).</summary>
        public static bool FinishesStarted(GameLiving living) => FinishesStarted(living is GameNPC, living is GameBot);

        /// <summary>Pure: NPCs other than gamebots.</summary>
        public static bool FinishesStarted(bool isNpc, bool isGameBot) => isNpc && !isGameBot;

        /// <summary>Line-of-sight checks while a spell is cast.</summary>
        public static bool ChecksLosDuringCast(GameLiving caster) =>
            FinishesStarted(caster) ? Properties.CHECK_LOS_DURING_NPC_CAST : Properties.CHECK_LOS_DURING_CAST;

        /// <summary>The range check at the end of a cast.</summary>
        public static bool ChecksRangeAtCastEnd(GameLiving caster) =>
            !FinishesStarted(caster) || Properties.CHECK_RANGE_AT_NPC_CAST_END;

        /// <summary>Line-of-sight checks while a bow is drawn on the target it was drawn at.</summary>
        public static bool ChecksLosWhileAiming(GameLiving shooter) =>
            !FinishesStarted(shooter) || Properties.CHECK_LOS_DURING_NPC_RANGED_ATTACK;

        /// <summary>The range check when the draw ends.</summary>
        public static bool ChecksRangeAtShotEnd(GameLiving shooter) =>
            !FinishesStarted(shooter) || Properties.CHECK_RANGE_AT_NPC_RANGED_ATTACK_END;
    }
}
