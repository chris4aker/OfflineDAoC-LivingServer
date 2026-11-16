// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0.

namespace DOL.GS
{
    /// <summary>
    /// AI Options that choose WHICH attack spells a hired companion casts (owner 2026-11-11; combat numbers untouched):
    /// a healer's fourth Role "Melee and heal, no attack spells" (a Shaman: "melee and conserve power for heals"), a
    /// caster's "Save power: Strict" and a caster's "Attack spells" row (a Runemaster: "afk personal bladeturn, no nukes;
    /// he blows his entire load every pull"). Choice 0 (Automatic) never blocks anything, so gamebots - whose choices
    /// always read 0 - are unchanged.
    /// </summary>
    public static class CompanionSpellOptions165
    {
        /// <summary>Role choice 3 (Shaman, Cleric, Druid, Healer, Friar): melee, heal, buff, crowd control - no attack spells.</summary>
        public const int RoleMeleeHeal = 3;

        /// <summary>Save power choice 2 (casters): under half power whoever holds the monster, area damage included.</summary>
        public const int SavePowerStrict = 2;

        /// <summary>Attack spells choices (casters): 1 area damage only, 2 none (buffs and crowd control only).</summary>
        public const int AttackAreaOnly = 1, AttackNone = 2;

        /// <summary>The pure casters offered "Save power: Strict" and "Attack spells".</summary>
        public static bool IsCasterClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Runemaster or eCharacterClass.Wizard or eCharacterClass.Sorcerer or eCharacterClass.Eldritch or
            eCharacterClass.Spiritmaster or eCharacterClass.Enchanter or eCharacterClass.Theurgist or
            eCharacterClass.Cabalist or eCharacterClass.Animist or eCharacterClass.Bonedancer or
            // Q8 (veteran review 2026-11-11): the Mentalist is a caster too (it was left without these rows)
            eCharacterClass.Mentalist;

        /// <summary>The healers with a Role row (Cleric, Druid, Healer, Shaman, Friar, and - Q8 - the Mentalist, which
        /// reuses the Cleric/Druid/Healer row).</summary>
        public static bool HasRoleRow(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer or eCharacterClass.Shaman or
            eCharacterClass.Friar or eCharacterClass.Mentalist;

        /// <summary>Q8: the classes on the healer Role row ("Heal first", "Attack, heal only emergencies", ...).</summary>
        public static bool UsesHealerRoleRow(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer or eCharacterClass.Mentalist;

        /// <summary>Pure: a healer whose player chose "Melee and heal, no attack spells".</summary>
        public static bool MeleeHealRole(eCharacterClass characterClass, int roleChoice) =>
            roleChoice == RoleMeleeHeal && HasRoleRow(characterClass);

        /// <summary>A hired healer set to "Melee and heal, no attack spells" (always false for gamebots).</summary>
        public static bool MeleeHealRole(GameBot bot) =>
            bot?.CharacterClass != null && HasRoleRow((eCharacterClass)bot.CharacterClass.ID) &&
            MeleeHealRole((eCharacterClass)bot.CharacterClass.ID, CompanionOptions165.Choice(bot, "role"));

        /// <summary>
        /// Pure: the spells a "Melee and heal" healer never casts - nukes, bolts, damage over time, snare-nukes, area
        /// damage and every debuff (disease too). Roots, mezzes, heals and buffs stay.
        /// </summary>
        public static bool MeleeHealBlocks(Spell spell) =>
            spell != null && (BotCasterPriority.IsDamage(spell) || BotCasterPriority.IsDebuff(spell) ||
                              spell.SpellType is eSpellType.DamageOverTime or eSpellType.Disease);

        /// <summary>
        /// Pure: "Save power: Strict" - under half power no damage spell, whoever holds the monster (the Automatic rule
        /// needs a tank holding it); a monster under 30% is still finished, as with Automatic.
        /// </summary>
        public static bool StrictSavesPower(int savePowerChoice, int casterPowerPercent, int targetHealthPercent) =>
            savePowerChoice == SavePowerStrict &&
            BotCasterPriority.SavePowerInGroup(casterPowerPercent, targetHealthPercent, true);

        /// <summary>Pure: while Strict saves power every damage spell waits - point blank and area damage too. Crowd
        /// control, debuffs, buffs and the pet focus shield are no damage spells.</summary>
        public static bool StrictBlocks(Spell spell, bool strictSaving) => strictSaving && BotCasterPriority.IsDamage(spell);

        /// <summary>An area damage spell: point blank, targeted area or cone.</summary>
        public static bool IsAreaSpell(Spell spell) => spell != null && (spell.Radius > 0 || spell.Target == eSpellTarget.CONE);

        /// <summary>
        /// Pure: the "Attack spells" row. Area spells only: no single-target damage (instant, bolt or cast), area damage,
        /// debuffs and crowd control stay. None: no damage spell and no debuff; buffs, crowd control and utility stay.
        /// </summary>
        public static bool AttackChoiceBlocks(int attackChoice, Spell spell) => spell != null && attackChoice switch
        {
            AttackAreaOnly => BotCasterPriority.IsDamage(spell) && !IsAreaSpell(spell),
            AttackNone => BotCasterPriority.IsDamage(spell) || BotCasterPriority.IsDebuff(spell),
            _ => false,
        };
    }
}
