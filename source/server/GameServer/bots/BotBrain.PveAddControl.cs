using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Mesmerize and stun on adds for every class that has them (owner 2026-10-28, GitHub issue #17: Healers never used
    /// their mesmerize or stun spells, single or area; only Bards controlled adds, through TryBardPveAddMez). An add is a
    /// monster attacking the group that nobody has chosen to kill. An area mez is used only where it cannot reach the
    /// monster being killed (it would put the group's target to sleep) or a monster not in the fight. A stun protects
    /// whoever an add is hitting while they fight something else (a healer or caster). The instant mesmerize and stun with
    /// a ten-minute reuse are kept for three or more adds. Celerity, the Augmentation Healer's 20-second group attack
    /// speed, was never cast: the buff selector bots use has no case for it (it is a fight buff, not upkeep).
    /// </summary>
    public partial class BotBrain
    {
        private const long PveAddControlRetryMilliseconds = 15_000;
        private const long EmergencyRecastMilliseconds = 60_000;
        private const int EmergencyAddCount = 3;
        /// <summary>A healing class keeps this much power for heals, as a real healer does, before it mezzes or hastes.</summary>
        private const int HealerControlPowerPercent = 25;
        private const int HealerCelerityPowerPercent = 30;
        private readonly Dictionary<GameLiving, long> _pveAddControlRetryUntil = new();
        private long _nextPveAddControlLook;
        private long _nextCelerityLook;

        private static bool IsAddControlSpell(Spell spell) =>
            spell?.SpellType is eSpellType.Mesmerize or eSpellType.Mez or eSpellType.Stun;

        private bool TryPveAddControl()
        {
            GameBot bot = BotBody;
            Group group = bot?.Group;
            long now = GameLoop.GameLoopTime;
            if (bot?.CharacterClass == null || bot.CharacterClass.ID == (int)eCharacterClass.Bard || group == null ||
                group.MemberCount < 2 || bot.IsIncapacitated || bot.IsCasting && !IsChannellingPetShield ||
                bot.castingComponent?.HasPendingSkillRequests == true || now < _nextPveAddControlLook ||
                BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID) && bot.ManaPercent < HealerControlPowerPercent)
                return false;
            _nextPveAddControlLook = now + 1_000;

            Spell[] control = bot.Spells?.Where(spell => spell != null && IsAddControlSpell(spell) &&
                    spell.Target == eSpellTarget.ENEMY && spell.Range > 0 && spell.Level <= bot.Level &&
                    bot.GetSkillDisabledDuration(spell) <= 0 && bot.Mana >= bot.PowerCost(spell))
                .ToArray();
            if (control == null || control.Length == 0)
                return false;

            GameLiving[] members = group.GetMembersInTheGroup()
                .Where(member => member?.IsAlive == true && member.CurrentRegion == bot.CurrentRegion)
                .ToArray();
            HashSet<GameLiving> selectedTargets = new();
            HashSet<GameLiving> activeTargets = new();
            foreach (GameLiving member in members)
            {
                if (member.TargetObject is GameLiving target && target.IsAlive)
                {
                    selectedTargets.Add(target);
                    if ((member.IsAttacking || member.InCombat) && target is GameNPC)
                        activeTargets.Add(target);
                }
                if (member.ControlledBrain?.Body?.TargetObject is GameLiving petTarget && petTarget.IsAlive)
                {
                    selectedTargets.Add(petTarget);
                    if (petTarget is GameNPC)
                        activeTargets.Add(petTarget);
                }
            }
            if (activeTargets.Count == 0)
                return false;

            foreach (GameLiving expired in _pveAddControlRetryUntil
                         .Where(pair => now >= pair.Value || pair.Key?.IsAlive != true)
                         .Select(pair => pair.Key).ToArray())
                _pveAddControlRetryUntil.Remove(expired);

            List<GameNPC> adds = bot.GetNPCsInRadius(1800)
                .Where(npc => npc?.IsAlive == true && npc.Realm == eRealm.None && !BotPvpCrowdControl.PlayerLike(npc) &&
                    !IsUntargetable(npc) && CanDefendAgainst(npc) &&
                    npc.TargetObject is GameLiving victim && members.Contains(victim) &&
                    !selectedTargets.Contains(npc) && npc != Body.TargetObject &&
                    !npc.IsMezzed && !npc.IsStunned && now >= _pveAddControlRetryUntil.GetValueOrDefault(npc))
                .OrderBy(bot.GetDistanceTo).Take(16).ToList();
            if (adds.Count == 0)
                return false;
            bool emergency = adds.Count >= EmergencyAddCount;

            foreach (GameNPC add in adds)
            {
                bool mezImmune = add.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) ||
                    add.effectListComponent.ContainsEffectForEffectType(eEffect.NPCMezImmunity);
                bool stunImmune = add.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) ||
                    add.effectListComponent.ContainsEffectForEffectType(eEffect.NPCStunImmunity);
                // A stun is protection: only for an add hitting someone who is fighting something else.
                bool protects = add.TargetObject is GameLiving victim && victim.TargetObject != add;

                foreach (Spell spell in control
                             .OrderBy(spell => spell.SpellType == eSpellType.Stun ? 1 : 0)   // a mez lasts longer
                             .ThenBy(spell => spell.Radius > 0 ? 1 : 0)                      // a single target first
                             .ThenByDescending(spell => spell.Level))
                {
                    bool isMez = spell.SpellType != eSpellType.Stun;
                    if (isMez ? mezImmune : (stunImmune || !protects))
                        continue;
                    if (spell.HasRecastDelay && spell.RecastDelay >= EmergencyRecastMilliseconds && !emergency)
                        continue;
                    if (spell.CastTime > 0 && !spell.Uninterruptible && bot.IsBeingInterrupted ||
                        !bot.IsWithinRadius(add, spell.CalculateEffectiveRange(bot)) ||
                        !NeedsOffensiveSpellApplication(add, spell) ||
                        !BotGroupSupport.HasCorpseLineOfSight(bot, add))
                        continue;
                    if (spell.Radius > 0)
                    {
                        // An area mez must not reach a monster the group is killing; neither may wake or pull anything.
                        if (isMez && activeTargets.Concat(selectedTargets).Any(target =>
                                target is GameObject killTarget && add.IsWithinRadius(killTarget, spell.Radius + BotAoePolicy.SafetyMargin)))
                            continue;
                        if (EvaluateBlast(add, spell).Vetoed)
                            continue;
                    }

                    if (_pveAddControlRetryUntil.Count >= 32)
                        _pveAddControlRetryUntil.Remove(_pveAddControlRetryUntil.OrderBy(pair => pair.Value).First().Key);
                    _pveAddControlRetryUntil[add] = now + PveAddControlRetryMilliseconds;
                    GameObject previous = bot.TargetObject;
                    bot.TargetObject = add;
                    try
                    {
                        if (spell.CastTime > 0)
                        {
                            bot.StopMovingOnPath();
                            bot.StopMoving();
                        }
                        if (!CheckOffensiveSpells(spell))
                            continue;
                        ReportAddControl(spell, add, adds.Count);
                        return true;
                    }
                    finally
                    {
                        bot.TargetObject = previous;
                    }
                }
            }
            return false;
        }

        private long _nextAddControlReport;

        private void ReportAddControl(Spell spell, GameNPC add, int adds)
        {
            long now = GameLoop.GameLoopTime;
            if (now < _nextAddControlReport)
                return;
            _nextAddControlReport = now + 30_000;
            log.Info($"BOT_ADD_CONTROL bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" spell=\"{spell.Name}\" type={spell.SpellType} " +
                     $"radius={spell.Radius} add=\"{add.Name}\" adds={adds} region={Body.CurrentRegionID} companion={BotBody.IsTemporaryGroupHelper}");
        }

        /// <summary>Celerity and its upgrades: a group attack speed buff of 20 seconds with no concentration cost.</summary>
        public static bool IsShortGroupHaste(Spell spell) =>
            spell?.SpellType == eSpellType.CombatSpeedBuff && spell.Target == eSpellTarget.GROUP &&
            !spell.IsConcentration && spell.Duration > 0 && spell.Duration <= 20_000;

        /// <summary>Celerity while a melee groupmate is fighting in melee and has none: below heals, never while being hit.</summary>
        private bool TryGroupCelerity()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.IsIncapacitated || bot.IsCasting || bot.IsBeingInterrupted ||
                bot.castingComponent?.HasPendingSkillRequests == true || now < _nextCelerityLook ||
                bot.ManaPercent < HealerCelerityPowerPercent)
                return false;
            _nextCelerityLook = now + 1_000;

            Spell celerity = bot.Spells?.Where(spell => IsShortGroupHaste(spell) && spell.Level <= bot.Level &&
                    bot.GetSkillDisabledDuration(spell) <= 0 && bot.Mana >= bot.PowerCost(spell))
                .OrderByDescending(spell => spell.Level).FirstOrDefault();
            if (celerity == null)
                return false;

            int range = Math.Max(1, celerity.CalculateEffectiveRange(bot));
            GameLiving fighter = bot.Group.GetMembersInTheGroup().FirstOrDefault(member =>
                member?.IsAlive == true && member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, range) &&
                member.attackComponent?.AttackState == true && member.ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
                member.TargetObject is GameLiving target && target.IsAlive &&
                member.IsWithinRadius(target, member.MeleeAttackRange + 100) && !LivingHasEffect(member, celerity));
            if (fighter == null)
                return false;

            GameObject previous = bot.TargetObject;
            bot.TargetObject = fighter;
            try
            {
                if (celerity.CastTime > 0)
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                }
                return CastCoordinatedBuff(celerity);
            }
            finally
            {
                bot.TargetObject = previous;
            }
        }
    }
}
