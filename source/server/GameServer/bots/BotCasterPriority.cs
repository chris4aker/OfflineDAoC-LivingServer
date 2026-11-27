namespace DOL.GS
{
    public static class BotCasterPriority
    {
        // #568 C2 (True165 a95ef07c4): a bomber is damage only when its payload is (root bombers carry Damage 50 too)
        public static bool IsDamage(Spell spell) => spell != null &&
            (spell.Damage > 0 && !BotControlSpells.IsControlBomber(spell) ||
             spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease);

        /// <summary>IsDamage with an explicit bomber payload lookup (tests).</summary>
        public static bool IsDamageWith(Spell spell, System.Func<int, Spell> resolve) => spell != null &&
            (spell.Damage > 0 && !BotControlSpells.IsControlBomberWith(spell, resolve) ||
             spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease);

        /// <summary>
        /// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0; read only by hired companions' AI Options
        /// (CompanionOptions165, CompanionSpellOptions165). A debuff: stat, attack-speed, armor and resist debuffs, and
        /// disease. Pure; no gamebot rule reads it.
        /// </summary>
        public static bool IsDebuff(Spell spell) => spell != null && !IsDamage(spell) && spell.SpellType is
            eSpellType.StrengthDebuff or eSpellType.DexterityDebuff or eSpellType.ConstitutionDebuff or
            eSpellType.StrengthConstitutionDebuff or eSpellType.DexterityQuicknessDebuff or
            eSpellType.DexterityConstitutionDebuff or eSpellType.WeaponSkillConstitutionDebuff or
            eSpellType.CombatSpeedDebuff or eSpellType.MeleeDamageDebuff or eSpellType.ArmorAbsorptionDebuff or
            eSpellType.ArmorFactorDebuff or eSpellType.AllStatsPercentDebuff or eSpellType.CrushSlashThrustDebuff or
            eSpellType.EffectivenessDebuff or eSpellType.Disease or
            eSpellType.SlashResistDebuff or eSpellType.CrushResistDebuff or eSpellType.ThrustResistDebuff or
            eSpellType.BodyResistDebuff or eSpellType.SpiritResistDebuff or eSpellType.EnergyResistDebuff or
            eSpellType.HeatResistDebuff or eSpellType.ColdResistDebuff or eSpellType.MatterResistDebuff;

        public const int SavePowerBelowPercent = 50;
        public const int FinishTargetBelowPercent = 30;

        /// <summary>True 1.65 (period forums: stop casting at about half power): under half power no optional nuke while the
        /// tank holds the monster, unless it is nearly dead. Read only by a hired companion's "Save power: Strict". Pure.</summary>
        public static bool SavePowerInGroup(int casterPowerPercent, int targetHealthPercent, bool tankHoldsTarget) =>
            tankHoldsTarget && casterPowerPercent < SavePowerBelowPercent && targetHealthPercent >= FinishTargetBelowPercent;

        /// <summary>A ranged caster casts an instant only when it is damage, under melee pressure, or (#568 C6) an opening
        /// debuff.</summary>
        public static bool AllowInstant(Spell spell, bool rangedCaster, bool meleePressure, bool debuffOpening = false) =>
            !rangedCaster || IsDamage(spell) || meleePressure || debuffOpening && IsDebuff(spell);

        /// <summary>#568 C6: at most this many debuffs open a fight on one target; then the caster nukes.</summary>
        public const int OpeningDebuffs = 2;

        /// <summary>
        /// Pure (#568 C6, True165 2d1b010 / b9c4dc0; Prima: Runemaster, Spiritmaster, Sorcerer and Cabalist debuff first,
        /// then nuke): debuffing is worth its cast on a fresh target that will last - a player-like enemy, a monster of the
        /// caster's level or above, or any monster when grouped. Not on greys and blues solo, not on a target under 60%
        /// and not below 30% power, where the power belongs to the nukes.
        /// </summary>
        public static bool DebuffOpeningWorthwhile(int casterLevel, int casterPowerPercent, bool grouped,
            int targetLevel, int targetHealthPercent, bool targetIsPlayerLike, int debuffsCastOnTarget,
            int openingDebuffs = OpeningDebuffs) =>
            debuffsCastOnTarget < openingDebuffs && casterPowerPercent >= 30 && targetHealthPercent >= 60 &&
            (targetIsPlayerLike || grouped || targetLevel >= casterLevel);

        /// <summary>
        /// Pure (#568 C6, True165 269870b; Prima, Wizard: "forget bolts in large battles, since they'll often be blocked"): a
        /// bolt is worth casting only at a target nobody else is meleeing.
        /// </summary>
        public static bool BoltWorthwhile(int othersMeleeingTarget) => othersMeleeingTarget <= 0;
    }
}
