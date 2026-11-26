// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0. Living Server adds Blocks: one reader at the cast
// itself (GameBot.CastSpell) for every AI Options row it offers, since 1.2's spell choice has no True 1.65 hooks to read
// them in.

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
        /// Whether a hired companion's AI Options forbid this cast now (False for every other bot, always: their choices read
        /// 0, Automatic). Read at the cast itself (GameBot.CastSpell), after the bot's own AI chose the spell:
        /// Role "Melee and heal", Attack spells, Save power Strict, Bolts "Nukes only", Debuff before nuking "Never" and
        /// the per-debuff Use / Skip, Life transfer and Amnesia Off, the Hunter's pet Off, charming Off and Crowd control Off.
        /// </summary>
        public static bool Blocks(GameBot bot, Spell spell)
        {
            if (spell == null || bot?.CharacterClass == null || !bot.IsHiredCompanion)
                return false;
            var cls = (eCharacterClass)bot.CharacterClass.ID;
            if (MeleeHealRole(bot) && MeleeHealBlocks(spell))
                return true;
            if (IsCasterClass(cls) && AttackChoiceBlocks(CompanionOptions165.Choice(bot, "attackspells"), spell))
                return true;
            int savePower = CompanionOptions165.Choice(bot, "savepower");
            if (savePower == SavePowerStrict)
            {
                int targetHealth = bot.TargetObject is GameLiving { MaxHealth: > 0 } target ? target.HealthPercent : 100;
                if (StrictBlocks(spell, StrictSavesPower(savePower, bot.ManaPercent, targetHealth)))
                    return true;
            }
            if (CompanionOptions165.Choice(bot, "bolts") == 1 && spell.SpellType == eSpellType.Bolt)
                return true;
            if (BotCasterPriority.IsDebuff(spell) || spell.SpellType == eSpellType.PetSpell && spell.SubSpellID > 0)
            {
                int master = CompanionOptions165.Choice(bot, "debuff");
                int line = CompanionOptions165.DebuffLineChoice(bot, spell);
                bool isDebuff = BotCasterPriority.IsDebuff(spell) || line != 0 || master != 0 && CompanionOptions165.IsChoosableDebuffSpell(spell);
                if (isDebuff && !CompanionOptions165.DebuffAllowed(master, line, CompanionOptions165.PickedDebuffCount(bot) > 0))
                    return true;
            }
            if (spell.SpellType == eSpellType.LifeTransfer && CompanionOptions165.Choice(bot, "lifetransfer") == 1 ||
                spell.SpellType == eSpellType.Amnesia && CompanionOptions165.Choice(bot, "amnesia") == 1 ||
                spell.SpellType == eSpellType.SummonHunterPet && CompanionOptions165.Choice(bot, "hunterpet") == 1 ||
                spell.SpellType == eSpellType.Charm && !CompanionOptions165.CharmAllowed(bot) ||
                spell.SpellType is eSpellType.Mesmerize or eSpellType.Mez or eSpellType.Stun && CompanionOptions165.Choice(bot, "cc") == 3)
                return true;
            return false;
        }

        /// <summary>Pure (#568 C8): "Attack spells: None" keeps the caster's self bladeturn up in the fight as well.</summary>
        public static bool KeepsBladeturnInFight(bool hired, int attackChoice) => hired && attackChoice == AttackNone;

        /// <summary>A hired caster whose player chose "Attack spells: None" (always false for gamebots).</summary>
        public static bool KeepsBladeturnInFight(GameBot bot) =>
            bot?.CharacterClass != null && bot.IsHiredCompanion && IsCasterClass((eCharacterClass)bot.CharacterClass.ID) &&
            KeepsBladeturnInFight(true, CompanionOptions165.Choice(bot, "attackspells"));

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
