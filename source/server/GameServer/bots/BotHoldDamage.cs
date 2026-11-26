namespace DOL.GS
{
    /// <summary>
    /// #568 C5 (owner decision 2026-11-26), ported from True165 b3498be ("casters wait for the tank", HoldDamageForTank) and
    /// 5a96d4b (last 7554aca, BotClassAbilities165.PetHoldsDamageAbovePercent). A ranged damage caster lets the tank or its
    /// own pet take the monster before it nukes (period forums: Theurgist, "wait for the tank's hits/taunts, start nuking at
    /// ~50-75% mob health"; Enchanter, "send pet, start nuking at ~85%"; Prima, Spiritmaster: the pet holds the monster to
    /// 75% first). Damage waits (debuffs and an interrupt/peel stun still go) until the monster is on the group's tank (or a
    /// player) under 75% health, or on its own pet under 85% (a Spiritmaster's under 75%). Never when the monster is on the
    /// caster, never solo without a pet, never against a player-like enemy, and at most 8 seconds per target, so a fight
    /// nobody holds is never stalled. Gamebots and companions alike; real players are not affected.
    /// </summary>
    public static class BotHoldDamage
    {
        public const long HoldMilliseconds = 8_000;
        /// <summary>The tank (or a player) holding the monster: nuke once it is under this.</summary>
        public const int TankHoldsAbovePercent = 75;
        public const int PetHoldsAbovePercent = 85;
        public const int SpiritmasterPetHoldsAbovePercent = 75;

        public enum Holder { Nobody, OwnPet, GroupTank, Other }

        /// <summary>
        /// Pure (owner decision 2026-11-26 on #568, from the #727 review): the hold (C5) and the debuff opening (C6) stay out
        /// of realm raids and warbands (shared support: AutonomousRealmRaid.HasSharedSupport, which covers both) and out of
        /// fights against a boss with a tactic (BotHealTriage165.IsBossTarget), the same gate as the 1.65 healer rules
        /// (#567, #703). To be revisited with the raid telemetry (#644) and raid sims writing real rows (#723).
        /// </summary>
        public static bool AppliesHere(bool sharedSupport, bool bossFight) => !sharedSupport && !bossFight;

        /// <summary>The caster waits while its own pet holds the monster above this health.</summary>
        public static int PetHoldsDamageAbovePercent(eCharacterClass characterClass) =>
            characterClass == eCharacterClass.Spiritmaster ? SpiritmasterPetHoldsAbovePercent : PetHoldsAbovePercent;

        /// <summary>Pure: does the caster hold its damage spells now?</summary>
        public static bool Holds(bool rangedCaster, bool monsterTarget, bool monsterOnCaster, bool tankInGroup, bool petAlive,
            long heldMilliseconds, Holder holder, int targetHealthPercent, eCharacterClass characterClass)
        {
            if (!rangedCaster || !monsterTarget || monsterOnCaster || !tankInGroup && !petAlive ||
                heldMilliseconds > HoldMilliseconds)
                return false;
            return holder switch
            {
                // a pet caster opens itself (the pet goes in with it); a group waits for its tank's pull
                Holder.Nobody => tankInGroup,
                Holder.OwnPet => targetHealthPercent > PetHoldsDamageAbovePercent(characterClass),
                Holder.GroupTank => tankInGroup && targetHealthPercent > TankHoldsAbovePercent,
                // a non-tank groupmate has it: wait for the tank to take it back (8 s at most)
                _ => tankInGroup,
            };
        }
    }
}
