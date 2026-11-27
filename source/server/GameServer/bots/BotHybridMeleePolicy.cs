namespace DOL.GS
{
    /// <summary>
    /// A melee bot that throws an instant spell keeps swinging (issue #571 step 5; ported from DetShaft
    /// OfflineDAoC-LivingServer 09baa0375 and 12b401e4f, GPL-3.0). The engine already works this way: SpellHandler stops
    /// melee only for a spell with a cast time (OpenDAoC-Core, "Stop our melee attack"). But the bot's combat turn
    /// answered every spell action with StopAttack. So a Reaver casting its instant lifedrain every turn, or a Paladin
    /// using an instant chant taunt, ran at its enemy and stood beside it without landing a swing.
    /// The Savage (SavageBotCombatPolicy) and the autonomous Minstrel (MinstrelBotCombatPolicy) had their own narrower
    /// versions of this rule; they stay as they are.
    /// </summary>
    public static class BotHybridMeleePolicy
    {
        /// <summary>
        /// Instant and played without an instrument. A song needs the instrument in hand, so it keeps its own rule
        /// (BotBrain.HasActiveCombatSongInstrument, True165 48b20439).
        /// </summary>
        public static bool IsSwingSafeInstant(Spell spell) => spell != null && spell.IsInstantCast && !spell.NeedInstrument;

        /// <summary>
        /// True when a melee bot's spell action this turn was instant spells only, so its swing and chase go on.
        /// A spell with a cast time (running or queued), a song, or an unknown queued request still stops the swing.
        /// </summary>
        public static bool ContinueMeleeAfterInstant(bool meleeBot, bool spellAction,
            bool isCasting, Spell activeSpell, bool hasPendingCast, Spell pendingSpell)
        {
            if (!meleeBot || !spellAction)
                return false;
            if (isCasting && !IsSwingSafeInstant(activeSpell))
                return false;
            if (hasPendingCast && !IsSwingSafeInstant(pendingSpell))
                return false;
            return true;
        }
    }
}
