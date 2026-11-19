namespace DOL.GS
{
    public static class BotCasterPriority
    {
        public static bool IsDamage(Spell spell) => spell != null &&
            (spell.Damage > 0 || spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
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

        public static bool AllowInstant(Spell spell, bool rangedCaster, bool meleePressure) =>
            !rangedCaster || IsDamage(spell) || meleePressure;
    }
}
