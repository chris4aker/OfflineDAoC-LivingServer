namespace DOL.GS
{
    /// <summary>
    /// How a bot reads the live-like interrupt (1.2.3; owner, 2026-11-24: "Interrupts
    /// should be port it in and not with a switch - it's foundation of the game"). A hit no longer stops a cast at once: it
    /// starts an interrupt timer, and a cast is lost only if that timer is still running at its half-cast point. So a bot
    /// begins a cast when the timer, as it stands, runs out by the half-cast point, and holds it otherwise; it no longer
    /// gives up every cast for the whole timer. Its own melee swing (the self interrupt) still blocks a cast from starting.
    /// Companion bots and autonomous gamebots both use this, and gamebots in RvR get the player's interrupt timer (#521).
    /// </summary>
    public static class BotInterruptPolicy
    {
        /// <summary>
        /// True when casting <paramref name="spell"/> now would be wasted: the self interrupt is running (the cast can't start,
        /// when <paramref name="includeSelfInterrupt"/>), or an attacker's interrupt timer would still run at the half-cast
        /// point. Instant, uninterruptible and Quickcast / Mastery of Concentration casts are never lost.
        /// </summary>
        public static bool CastWouldBeLost(GameLiving caster, Spell spell, bool includeSelfInterrupt = true)
        {
            if (caster == null || spell == null || spell.IsInstantCast || spell.CastTime <= 0 || spell.Uninterruptible)
                return false;

            EffectListComponent effects = caster.effectListComponent;

            if (effects != null && (effects.ContainsEffectForEffectType(eEffect.MasteryOfConcentration) ||
                effects.ContainsEffectForEffectType(eEffect.FacilitatePainworking) ||
                effects.ContainsEffectForEffectType(eEffect.QuickCast)))
                return false;

            if (includeSelfInterrupt && caster.IsBeingSelfInterrupted)
                return true;

            if (!caster.InterruptWouldStopCast(EstimatedCastTime(caster, spell)))
                return false;
            // #531 step 2d: an open-field gamebot's key cast (a mez or its big heal) goes through on Quickcast when it may
            return !(caster is GameBot bot && BotQuickCast.WouldCarry(bot, spell));
        }

        /// <summary>The cast time the caster would get (instrument songs keep their delve time).</summary>
        public static int EstimatedCastTime(GameLiving caster, Spell spell) =>
            spell.InstrumentRequirement != 0
                ? spell.CastTime
                : GameLiving.ScaleCastTime(spell.CastTime, caster.GetModified(eProperty.Dexterity), caster.GetModified(eProperty.CastingSpeed));

        /// <summary>The live rule (pure): a cast begun at <paramref name="now"/> is lost when the attacker's interrupt timer
        /// (<paramref name="interruptEnds"/>) is still running at its half-cast point.</summary>
        public static bool LostAtHalfCast(long now, long interruptEnds, long castTimeMs) =>
            interruptEnds > now && interruptEnds > now + castTimeMs / 2;
    }
}
