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
        /// either the way characters do), fatigue buffs (#567 A2: StyleProcessor charges style endurance only to players and
        /// bots) or acuity (a pet's spells read its intelligence, which acuity doesn't raise). A
        /// character with no power gets no power regen; acuity goes only to list casters. Everything else is allowed. Pure.
        /// </summary>
        public static bool Benefits(eSpellType type, TargetKind kind, eCharacterClass characterClass = eCharacterClass.Unknown,
            eStat manaStat = eStat.UNDEFINED, eClassType classType = eClassType.ListCaster, bool realPlayer = false)
        {
            switch (kind)
            {
                case TargetKind.Pet:
                    return type is not (eSpellType.EnduranceRegenBuff or eSpellType.PowerRegenBuff or eSpellType.AcuityBuff or
                        eSpellType.FatigueConsumptionBuff);
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

        /// <summary>
        /// Whether this spell does anything for this live target (see <see cref="Benefits(eSpellType, TargetKind, eCharacterClass, eStat, eClassType, bool)"/>),
        /// and the target takes it: a real player who refused the type (<c>/buff refuse</c>, or a right-click cancel of a bot's
        /// buff; #661) gets none from a bot, and neither do that player's pets (<see cref="PlayerBuffRefusals"/>). Every
        /// group, realm and pet buff pick in BotBrain goes through here.
        /// </summary>
        public static bool Benefits(GameLiving target, Spell spell)
        {
            if (target == null || spell == null)
                return false;
            if (PlayerBuffRefusals.Refuses(target, spell.SpellType))
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

        // ---- #571 step 4 (True165 1ed3b24, GPL-3.0, "veteran buffing"): the buff decisions of a companion or a bot in a real
        // player's group. Autonomous gamebots outside one keep the #491 rules above.

        /// <summary>
        /// Whether a buffer uses the player-group buff rules: a companion (hired, <c>/spawn</c>, <c>/bot</c>) or a bot in a
        /// real player's group, with someone to buff besides itself. Pure.
        /// </summary>
        public static bool UsesPlayerGroupRules(bool playerCompanion, bool playerLedGroup, int groupMembers) =>
            (playerCompanion || playerLedGroup) && groupMembers > 1;

        /// <summary>Live form of <see cref="UsesPlayerGroupRules(bool, bool, int)"/>.</summary>
        public static bool UsesPlayerGroupRules(GameBot caster) =>
            caster != null && UsesPlayerGroupRules(caster.IsPlayerCompanion, caster.IsPlayerLedGroup, caster.Group?.MemberCount ?? 0);

        /// <summary>The buffs that only help a class that swings a weapon: base strength, haste, endurance regen, damage
        /// adds and weapon procs (True165 <c>PartyBuffHelps</c> and Q14). Str/con is not one: its constitution helps everyone.</summary>
        public static bool IsWeaponBuff(eSpellType type) => type is
            eSpellType.StrengthBuff or eSpellType.CombatSpeedBuff or eSpellType.HasteBuff or eSpellType.EnduranceRegenBuff or
            eSpellType.DamageAdd or eSpellType.OffensiveProc;

        /// <summary>
        /// Whether a class fights with its weapon: not a pure caster, and not a healer (Cleric, Druid, Healer). The
        /// Valewalker and the Vampiir are list casters by class type but fight in melee. Pure.
        /// </summary>
        public static bool SwingsWeapon(eCharacterClass characterClass, eClassType classType) =>
            (classType != eClassType.ListCaster || characterClass is eCharacterClass.Valewalker or eCharacterClass.Vampiir) &&
            BotPartyRoles.For(characterClass) != BotPartyRole.Support;

        /// <summary>
        /// The player-group rule on top of <see cref="Benefits(eSpellType, TargetKind, eCharacterClass, eStat, eClassType, bool)"/>:
        /// a weapon buff goes only to a character that swings a weapon (a caster's or healer's concentration was spent on
        /// haste it never used). Pets and unknown targets are unchanged. Pure.
        /// </summary>
        public static bool PlayerGroupBenefits(eSpellType type, TargetKind kind, eCharacterClass characterClass, eClassType classType) =>
            kind != TargetKind.Character || !IsWeaponBuff(type) || SwingsWeapon(characterClass, classType);

        /// <summary>
        /// <see cref="Benefits(GameLiving, Spell)"/> as this caster judges it: with <see cref="UsesPlayerGroupRules(GameBot)"/>
        /// also <see cref="PlayerGroupBenefits"/>. Every buff target pick in BotBrain goes through here.
        /// </summary>
        public static bool Benefits(GameBot caster, GameLiving target, Spell spell)
        {
            if (!Benefits(target, spell))
                return false;
            if (!UsesPlayerGroupRules(caster))
                return true;
            TargetKind kind = KindOf(target, out ICharacterClass characterClass);
            return characterClass == null ||
                PlayerGroupBenefits(spell.SpellType, kind, (eCharacterClass)characterClass.ID, characterClass.ClassType);
        }

        /// <summary>
        /// The player-group order a buffer casts in when it can't cast everything (True165 <c>VeteranBuffSpellRank</c>),
        /// higher first: endurance and power regen; the spec buffs (str/con, dex/qui, spec AF); base constitution and
        /// dexterity; acuity and haste; base strength; everything else; base AF last. Spec AF stays with the spec buffs, as
        /// in #491 (True165 ranks it with everything else). Pure.
        /// </summary>
        public static int PlayerGroupPriority(eSpellType type) => type switch
        {
            eSpellType.EnduranceRegenBuff or eSpellType.PowerRegenBuff => 6,
            _ when IsSpecBuff(type) && type != eSpellType.AcuityBuff => 5,
            eSpellType.ConstitutionBuff or eSpellType.DexterityBuff => 4,
            eSpellType.AcuityBuff or eSpellType.CombatSpeedBuff or eSpellType.HasteBuff => 3,
            eSpellType.StrengthBuff => 2,
            eSpellType.BaseArmorFactorBuff => 0,
            _ => 1,
        };

        /// <summary>The order for this caster: <see cref="PlayerGroupPriority"/> in a player's group, else <see cref="Priority"/>.</summary>
        public static int Priority(GameBot caster, eSpellType type) =>
            UsesPlayerGroupRules(caster) ? PlayerGroupPriority(type) : Priority(type);

        /// <summary>The "Buff priority" AI option (hired companions; everyone else is Automatic): stored choices.</summary>
        public const int BuffPriorityAutomatic = 0, BuffPriorityFrontLine = 1, BuffPrioritySpellUsers = 2;

        /// <summary>A pet's rank: after every character (pets after players and bots).</summary>
        public const int PetTargetRank = 9;

        /// <summary>By job: tanks 0, melee 1, healers 2, casters 3 (period groups buffed the tank first). Pure.</summary>
        public static int RoleRank(eCharacterClass characterClass, eClassType classType) => BotPartyRoles.For(characterClass) switch
        {
            BotPartyRole.Tank => 0,
            BotPartyRole.Support => 2,
            _ => SwingsWeapon(characterClass, classType) ? 1 : 3,
        };

        /// <summary>A class that casts for its living: a healer (Cleric, Druid, Healer) or a list caster.</summary>
        public static bool IsSpellUser(eCharacterClass characterClass, eClassType classType) =>
            BotPartyRoles.For(characterClass) == BotPartyRole.Support || classType == eClassType.ListCaster;

        /// <summary>
        /// The buffs Automatic gives spell users first (True165 <c>VeteranBuffTargetRank</c>): base dexterity (it speeds
        /// casting, <c>GameLiving.ScaleCastTime</c>) and power regen. True165 adds dex/qui; here it stays front line first
        /// (#491) until the owner decides (#571).
        /// </summary>
        public static bool SpellUsersFirstOnAutomatic(eSpellType type) => type is eSpellType.DexterityBuff or eSpellType.PowerRegenBuff;

        /// <summary>
        /// Who a player-group buffer buffs first, lower first (True165 <c>VeteranBuffTargetRank</c>): by job (<see cref="RoleRank"/>)
        /// on Front line first, spell users first on Casters and healers first, and on Automatic spell users first only for
        /// <see cref="SpellUsersFirstOnAutomatic"/>. Pets come last; a target of no known class ranks with melee. Pure.
        /// </summary>
        public static int TargetRank(TargetKind kind, eCharacterClass characterClass, eClassType classType, eSpellType type, int choice)
        {
            if (kind == TargetKind.Pet)
                return PetTargetRank;
            if (kind != TargetKind.Character)
                return 1;
            int role = RoleRank(characterClass, classType);
            bool spellUsersFirst = choice == BuffPrioritySpellUsers ||
                choice != BuffPriorityFrontLine && SpellUsersFirstOnAutomatic(type);
            if (!spellUsersFirst)
                return role;
            return IsSpellUser(characterClass, classType) ? 0 : role + 1;
        }

        /// <summary>Live form of <see cref="TargetRank(TargetKind, eCharacterClass, eClassType, eSpellType, int)"/>.</summary>
        public static int TargetRank(GameLiving target, eSpellType type, int choice)
        {
            TargetKind kind = KindOf(target, out ICharacterClass characterClass);
            return characterClass == null
                ? TargetRank(kind, eCharacterClass.Unknown, eClassType.ListCaster, type, choice)
                : TargetRank(kind, (eCharacterClass)characterClass.ID, characterClass.ClassType, type, choice);
        }

        /// <summary>Within this range of the buffer (or the spell's range, if longer) a groupmate keeps a base buff.</summary>
        public const int BaseBuffKeeperMinimumRange = 350;

        /// <summary>
        /// Whether a groupmate bot really keeps a base buff for this buffer (True165 <c>BaseBuffLeftToGroupmate</c>): it can
        /// afford the concentration, is within reach of the buffer, and, in a player's group, isn't fighting (it buffs
        /// only out of a fight). The range part is a shared fix for every bot: a healer left behind kept the buff and nobody
        /// cast it. Pure.
        /// </summary>
        public static bool KeeperCovers(bool canAfford, bool inRange, bool keeperInCombat, bool playerGroupRules) =>
            canAfford && inRange && (!playerGroupRules || !keeperInCombat);

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
            bool playerGroupRules = UsesPlayerGroupRules(caster);
            foreach (GameLiving member in caster.Group.GetMembersInTheGroup())
            {
                if (member == caster || member is not GameBot bot || !bot.IsAlive ||
                    bot.ObjectState != GameObject.eObjectState.Active || bot.CurrentRegionID != caster.CurrentRegionID)
                    continue;
                bool canCast = KnowsSharedBuff(bot, type => type == spell.SpellType, out Spell own) &&
                    KeeperCovers(bot.CanAffordConcentration(own),
                        caster.IsWithinRadius(bot, System.Math.Max(BaseBuffKeeperMinimumRange, own.CalculateEffectiveRange(bot))),
                        bot.InCombat, playerGroupRules);
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
