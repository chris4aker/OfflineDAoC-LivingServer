using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Which groupmates a buff is worth casting on, which buffs a specialist buffer leaves to the group's healers, and which
    /// buffs come first (#491). Used by <see cref="AI.Brain.BotBrain"/> for every bot that buffs its group: companion
    /// <c>/bot</c>s, hired and <c>/spawn</c> companions and autonomous gamebot groups. Targets are real players, companion
    /// bots, autonomous gamebots and their pets alike. No Lifer buffbots plan their own buffs (<c>NoLiferBuffBots.PlanBuffs</c>).
    /// </summary>
    public static class BotBuffTargetPolicy
    {
        /// <summary>What kind of target a buff would land on.</summary>
        public enum TargetKind
        {
            /// <summary>A real player, a companion bot or an autonomous gamebot: judged by its class.</summary>
            Character,
            /// <summary>A pet: summoned, charmed, a necromancer's shade, a Bonedancer's minion, an animist's turret.</summary>
            Pet,
            /// <summary>Anything else (no class known): every buff is allowed, as before.</summary>
            Other,
        }

        /// <summary>The base buffs a group's healers keep on everyone (base STR, CON, DEX and AF).</summary>
        public static bool IsBaseBuff(eSpellType type) => type is
            eSpellType.StrengthBuff or eSpellType.ConstitutionBuff or eSpellType.DexterityBuff or
            eSpellType.BaseArmorFactorBuff;

        /// <summary>The spec / dual-stat buffs a specialist buffer saves its concentration for (STR/CON, DEX/QUI, spec AF, acuity).</summary>
        public static bool IsSpecBuff(eSpellType type) => type is
            eSpellType.StrengthConstitutionBuff or eSpellType.DexterityQuicknessBuff or
            eSpellType.SpecArmorFactorBuff or eSpellType.AcuityBuff;

        /// <summary>
        /// Whether a class has a power pool: a power stat, or the Vampiir (power from strength, real players only; a Vampiir
        /// bot has no pool, <c>GameBot.CalculateMaxMana</c>).
        /// </summary>
        public static bool UsesPower(eCharacterClass characterClass, eStat manaStat, bool realPlayer) =>
            manaStat != eStat.UNDEFINED || realPlayer && characterClass == eCharacterClass.Vampiir;

        /// <summary>
        /// Whether an acuity buff raises the class's casting stat. The stat calculator adds acuity only to a list caster's
        /// power stat (<c>StatCalculator</c>), so a hybrid with power (Paladin, Thane, Friar, Reaver...) gains nothing from it.
        /// </summary>
        public static bool AcuityHelps(eStat manaStat, eClassType classType) =>
            manaStat != eStat.UNDEFINED && classType == eClassType.ListCaster;

        /// <summary>
        /// Whether a buff of this type does anything for this target. Pets never get endurance or power regen (they don't use
        /// either the way characters do) or acuity (a pet's spells read its intelligence, which acuity doesn't raise). A
        /// character with no power gets no power regen; acuity goes only to list casters. Everything else is allowed. Pure.
        /// </summary>
        public static bool Benefits(eSpellType type, TargetKind kind, eCharacterClass characterClass = eCharacterClass.Unknown,
            eStat manaStat = eStat.UNDEFINED, eClassType classType = eClassType.ListCaster, bool realPlayer = false)
        {
            switch (kind)
            {
                case TargetKind.Pet:
                    return type is not (eSpellType.EnduranceRegenBuff or eSpellType.PowerRegenBuff or eSpellType.AcuityBuff);
                case TargetKind.Character:
                    return type switch
                    {
                        eSpellType.PowerRegenBuff => UsesPower(characterClass, manaStat, realPlayer),
                        eSpellType.AcuityBuff => AcuityHelps(manaStat, classType),
                        _ => true,
                    };
                default:
                    return true;
            }
        }

        /// <summary>The kind of a live target: a player or bot with a class is a character; another NPC is a pet.</summary>
        public static TargetKind KindOf(GameLiving target, out ICharacterClass characterClass)
        {
            characterClass = target switch
            {
                GamePlayer player => player.CharacterClass,
                GameBot bot => bot.CharacterClass,
                _ => null,
            };
            if (characterClass != null)
                return TargetKind.Character;
            return target is GameNPC and not GameBot ? TargetKind.Pet : TargetKind.Other;
        }

        /// <summary>Whether this spell does anything for this live target (see <see cref="Benefits(eSpellType, TargetKind, eCharacterClass, eStat, eClassType, bool)"/>).</summary>
        public static bool Benefits(GameLiving target, Spell spell)
        {
            if (target == null || spell == null)
                return false;
            TargetKind kind = KindOf(target, out ICharacterClass characterClass);
            return characterClass == null
                ? Benefits(spell.SpellType, kind)
                : Benefits(spell.SpellType, kind, (eCharacterClass)characterClass.ID, characterClass.ManaStat,
                    characterClass.ClassType, target is GamePlayer);
        }

        /// <summary>
        /// Whether the spec buff is better spent on this target than on a list caster: STR/CON and DEX/QUI go to the tanks and
        /// melee damage dealers first (a caster still gets them once those have theirs).
        /// </summary>
        public static bool PreferredSpecTarget(eSpellType type, TargetKind kind, eClassType classType) =>
            type is eSpellType.StrengthConstitutionBuff or eSpellType.DexterityQuicknessBuff &&
            kind == TargetKind.Character && classType != eClassType.ListCaster;

        /// <summary>
        /// The order a buffer casts in when it can't cast everything: endurance regen, the spec buffs and haste first, then
        /// the other buffs, then the base buffs (which a healer of the group may be covering). Higher comes first. Pure.
        /// </summary>
        public static int Priority(eSpellType type) => type switch
        {
            eSpellType.EnduranceRegenBuff => 3,
            _ when IsSpecBuff(type) => 2,
            eSpellType.CombatSpeedBuff or eSpellType.HasteBuff => 2,
            _ when IsBaseBuff(type) => 0,
            _ => 1,
        };

        /// <summary>One other buffer of the group, as the base/spec split needs it.</summary>
        public readonly record struct Buffer(bool HasSpecBuffs, bool CanCastBase);

        /// <summary>
        /// Whether a caster leaves this base buff to another buffer of its group: the caster is a specialist (it knows spec
        /// buffs) and a groupmate bot that knows no spec buffs (a healer) can cast the same base buff now. A specialist that
        /// is the only buffer, two specialists together, and every healer still cast their base buffs. Pure.
        /// </summary>
        public static bool LeavesBaseBuff(eSpellType type, bool casterHasSpecBuffs, IEnumerable<Buffer> others)
        {
            if (!IsBaseBuff(type) || !casterHasSpecBuffs || others == null)
                return false;
            foreach (Buffer other in others)
                if (!other.HasSpecBuffs && other.CanCastBase)
                    return true;
            return false;
        }

        /// <summary>Whether a bot knows a group or realm spec buff it is level for.</summary>
        public static bool HasSpecBuffs(GameBot bot) => KnowsSharedBuff(bot, IsSpecBuff, out _);

        /// <summary>
        /// The live base/spec split for a caster bot: another bot of its group (alive, in the world, the same region) that
        /// knows a group or realm buff of this base type, is level for it and has the concentration for it, is a
        /// <see cref="Buffer"/> that can cast it. Real players are never counted: a bot doesn't rely on a person to buff.
        /// </summary>
        public static bool LeavesBaseBuff(GameBot caster, Spell spell)
        {
            if (caster?.Group == null || spell == null || !IsBaseBuff(spell.SpellType) || !HasSpecBuffs(caster))
                return false;
            var others = new List<Buffer>();
            foreach (GameLiving member in caster.Group.GetMembersInTheGroup())
            {
                if (member == caster || member is not GameBot bot || !bot.IsAlive ||
                    bot.ObjectState != GameObject.eObjectState.Active || bot.CurrentRegionID != caster.CurrentRegionID)
                    continue;
                bool canCast = KnowsSharedBuff(bot, type => type == spell.SpellType, out Spell own) &&
                    bot.CanAffordConcentration(own);
                others.Add(new Buffer(HasSpecBuffs(bot), canCast));
            }
            return LeavesBaseBuff(spell.SpellType, true, others);
        }

        private static bool KnowsSharedBuff(GameBot bot, System.Func<eSpellType, bool> type, out Spell found)
        {
            found = null;
            if (bot?.Spells == null)
                return false;
            foreach (Spell spell in bot.Spells)
            {
                if (spell == null || spell.IsHarmful || spell.Level > bot.Level || !type(spell.SpellType) ||
                    spell.Target is not (eSpellTarget.REALM or eSpellTarget.GROUP))
                    continue;
                found = spell;
                return true;
            }
            return false;
        }
    }
}
