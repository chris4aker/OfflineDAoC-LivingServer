using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// Stand-in spell kits for the RvR fight rehearsal (#520). CI has no world database, so the classes' real spell lines
    /// (SkillBase) are empty and a gamebot would load no spells; each class gets the few spells its RvR job needs, shaped on
    /// the era's level-50 values (a heal, a nuke, the crowd controller's 60-second mez). The spells run through the
    /// server's own handlers (HealSpellHandler, DirectDamageSpellHandler, MesmerizeSpellHandler) and the bots' own brains
    /// choose them. Melee classes fight with the starter weapon the bot's own loading gives it. Pet classes get no pet.
    /// </summary>
    public static class RvrFightKits
    {
        public const int HealId = 952_001, MezId = 952_002, BoltId = 952_003;

        public static eRace Race(eRealm realm, eCharacterClass characterClass) => realm switch
        {
            eRealm.Albion => eRace.Briton,
            eRealm.Midgard => characterClass is eCharacterClass.Shaman ? eRace.Troll : eRace.Norseman,
            _ => characterClass is eCharacterClass.Bard or eCharacterClass.Druid or eCharacterClass.Warden ? eRace.Celt : eRace.Firbolg,
        };

        public static bool Heals(eCharacterClass c) => c is eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Healer
            or eCharacterClass.Shaman or eCharacterClass.Druid or eCharacterClass.Warden or eCharacterClass.Bard;

        public static bool Mezzes(eCharacterClass c) => c is eCharacterClass.Sorcerer or eCharacterClass.Healer or eCharacterClass.Bard
            or eCharacterClass.Mentalist or eCharacterClass.Minstrel or eCharacterClass.Skald;

        public static bool Nukes(eCharacterClass c) => c is eCharacterClass.Wizard or eCharacterClass.Theurgist or eCharacterClass.Sorcerer
            or eCharacterClass.Cabalist or eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Shaman
            or eCharacterClass.Eldritch or eCharacterClass.Mentalist or eCharacterClass.Enchanter or eCharacterClass.Druid;

        /// <summary>The class's stand-in kit at <paramref name="level"/> (the values scale with level from the level-50 shape).</summary>
        public static List<Spell> For(eCharacterClass c, byte level)
        {
            double scale = level / 50.0;
            var kit = new List<Spell>();
            if (Heals(c))
                kit.Add(Make(HealId, "Stand-in Heal", "Heal", "Realm", range: 2000, power: 30, cast: 3.0, value: 380 * scale, level: level));
            if (Mezzes(c) && c is not eCharacterClass.Skald)
                kit.Add(Make(MezId, "Stand-in Mesmerize", "Mesmerize", "Enemy", range: 1500, power: 25, cast: 2.6, duration: 60, level: level,
                    damageType: eDamageType.Spirit));
            if (Nukes(c))
                kit.Add(Make(BoltId, "Stand-in Bolt", "DirectDamage", "Enemy", range: 1500, power: 20, cast: 2.8, damage: 210 * scale, level: level,
                    damageType: c is eCharacterClass.Runemaster or eCharacterClass.Theurgist ? eDamageType.Energy : eDamageType.Heat));
            return kit;
        }

        private static Spell Make(int id, string name, string type, string target, int range, int power, double cast, int duration = 0,
            double value = 0, double damage = 0, byte level = 50, eDamageType damageType = eDamageType.Natural) =>
            new(new DbSpell
            {
                SpellID = id, Name = name, Description = name, Type = type, Target = target, Range = range, Power = power,
                CastTime = cast, Duration = duration, Value = value, Damage = damage, DamageType = (int)damageType,
            }, level);
    }
}
