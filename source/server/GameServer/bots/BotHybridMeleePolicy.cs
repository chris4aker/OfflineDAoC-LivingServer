namespace DOL.GS
{
    /// <summary>
    /// A melee bot that throws an instant spell keeps swinging (issue #571 step 5). The engine already works this way: SpellHandler stops
    /// melee only for a spell with a cast time (OpenDAoC-Core, "Stop our melee attack"). But the bot's combat turn
    /// answered every spell action with StopAttack. So a Reaver casting its instant lifedrain every turn, or a Paladin
    /// using an instant chant taunt, ran at its enemy and stood beside it without landing a swing.
    /// The Savage (SavageBotCombatPolicy) and the autonomous Minstrel (MinstrelBotCombatPolicy) had their own narrower
    /// versions of this rule; they stay as they are, and the Minstrel is left out of this one.
    /// </summary>
    public static class BotHybridMeleePolicy
    {
        /// <summary>
        /// Instant and played without an instrument. A song needs the instrument in hand, so it keeps its own rule
        /// (BotBrain.HasActiveCombatSongInstrument).
        /// </summary>
        public static bool IsSwingSafeInstant(Spell spell) => spell != null && spell.IsInstantCast && !spell.NeedInstrument;

        /// <summary>
        /// Instant, played without an instrument, and not crowd control on a player-like enemy. PvP crowd control holds
        /// on players, bots and their pets, so a hybrid that mezzes or roots the one it fights and keeps swinging breaks its
        /// own control (review of #777). Against a monster the bot's own control is already limited by
        /// ControlAllowedOnTarget (no mez or root on the kill target), so nothing more is needed there.
        /// </summary>
        public static bool IsSwingSafe(Spell spell, bool playerLikeTarget) => IsSwingSafeInstant(spell) &&
            !(playerLikeTarget && BotControlSpells.Kind(spell) != BotControlSpells.ControlKind.None);

        /// <summary>
        /// True when a melee bot's spell action this turn was instant spells only, so its swing and chase go on.
        /// A spell with a cast time (running or queued), a song, crowd control on a player-like enemy, or an unknown
        /// queued request still stops the swing. The Minstrel is left out: MinstrelBotCombatPolicy keeps its own narrower
        /// rule (only instant damage goes on; charm upkeep and the rest still wait), and grouped or autonomous Minstrels
        /// are melee by PrefersSpellRange, so without this the OR in the combat turn would widen that rule.
        /// </summary>
        public static bool ContinueMeleeAfterInstant(bool meleeBot, bool minstrel, bool playerLikeTarget, bool spellAction,
            bool isCasting, Spell activeSpell, bool hasPendingCast, Spell pendingSpell)
        {
            if (!meleeBot || minstrel || !spellAction)
                return false;
            if (isCasting && !IsSwingSafe(activeSpell, playerLikeTarget))
                return false;
            if (hasPendingCast && !IsSwingSafe(pendingSpell, playerLikeTarget))
                return false;
            return true;
        }
    }
}
