using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;
using DOL.Logging;
using DOL.Timing;
using ECS.Debug;

namespace DOL.GS
{
    public sealed class EffectService : GameServiceBase
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private List<ECSGameEffect> _list;
        private int _lastValidIndex;

        public static EffectService Instance { get; }

        static EffectService()
        {
            Instance = new();
        }

        public override void Tick()
        {
            ProcessPostedActionsParallel();

            try
            {
                _list = ServiceObjectStore.UpdateAndGetAll<ECSGameEffect>(ServiceObjectType.Effect, out _lastValidIndex);
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"{nameof(ServiceObjectStore.UpdateAndGetAll)} failed. Skipping this tick.", e);

                _lastValidIndex = -1;
                return;
            }

            GameLoop.ExecuteForEach(_list, _lastValidIndex + 1, TickInternal);

            if (Diagnostics.CheckServiceObjectCount)
                Diagnostics.PrintServiceObjectCount(ServiceName, ref EntityCount, _list.Count);
        }

        private static void TickInternal(ECSGameEffect effect)
        {
            try
            {
                if (Diagnostics.CheckServiceObjectCount)
                    Interlocked.Increment(ref Instance.EntityCount);

                long startTick = MonotonicTime.NowMs;
                TickEffect(effect);
                long stopTick = MonotonicTime.NowMs;

                if (stopTick - startTick > Diagnostics.LongTickThreshold)
                    log.Warn($"Long {Instance.ServiceName}.{nameof(TickInternal)} for {effect.Owner.Name}({effect.Owner.ObjectID}) Effect: {effect.EffectType} Time: {stopTick - startTick}ms");
            }
            catch (Exception e)
            {
                GameServiceUtils.HandleServiceException(e, Instance.ServiceName, effect, effect.Owner);
            }
        }

        /// <summary>
        /// An effect on a monster, pet or bot that was deleted, or a pulse whose caster was, ends. Census of 2026-10-21: 2,755 effects
        /// stayed in the service on deleted NPCs and grew by about 400 an hour (a Sluaghbinder pet's Cairn Boneplate pulse went on
        /// pulsing after the pet was gone, and a pulse's child effects only end when their pulse does), keeping every one of those
        /// NPCs in memory. A monster awaiting respawn is deleted too, but its effects were cancelled when it died.
        /// </summary>
        public static bool OutlivedItsLiving(GameObject.eObjectState ownerState, bool ownerIsNpc, bool isPulse, GameObject.eObjectState? casterState,
            bool casterIsNpc) =>
            ownerIsNpc && ownerState == GameObject.eObjectState.Deleted ||
            isPulse && casterIsNpc && casterState == GameObject.eObjectState.Deleted;

        /// <summary>An ended effect has no business in the service; only a restart (Starting / Enabling) may bring one back.</summary>
        public static bool IsStaleEndedEffect(bool ended, bool starting, bool enabling) => ended && !starting && !enabling;

        private static void TickEffect(ECSGameEffect effect)
        {
            // Last line of defence against any other path that re-adds an ended effect: it can't be ended again, so it would
            // pulse forever (from rambodolphin13/OfflineDAoC 7a8ae69).
            if (IsStaleEndedEffect(effect.IsEnded, effect.IsStarting, effect.IsEnabling))
            {
                ServiceObjectStore.Remove(effect);
                return;
            }

            GameLiving pulseCaster = (effect as ECSPulseEffect)?.SpellHandler?.Caster;
            if (effect.Owner != null && OutlivedItsLiving(effect.Owner.ObjectState, effect.Owner is GameNPC, effect is ECSPulseEffect,
                    pulseCaster?.ObjectState, pulseCaster is GameNPC))
            {
                // An effect whose ending was lost (its owner's list no longer had it) can't be ended again: take it out of the
                // service directly (2026-10-21 live run: orphan effects still grew by about 200 an hour with End() alone).
                if (!effect.End())
                    ServiceObjectStore.Remove(effect);
                return;
            }

            if (effect is ECSGameAbilityEffect abilityEffect)
                TickAbilityEffect(abilityEffect);
            else if (effect is ECSGameSpellEffect spellEffect)
                TickSpellEffect(spellEffect);
        }

        static void TickAbilityEffect(ECSGameAbilityEffect abilityEffect)
        {
            if (abilityEffect.NextTick != 0 && GameServiceUtils.ShouldTick(abilityEffect.NextTick))
            {
                abilityEffect.OnEffectPulse();
                abilityEffect.NextTick += abilityEffect.PulseFreq;
            }

            if (abilityEffect.Duration > 0 && GameServiceUtils.ShouldTick(abilityEffect.ExpireTick))
                abilityEffect.End();
        }

        static void TickSpellEffect(ECSGameSpellEffect spellEffect)
        {
            ISpellHandler spellHandler = spellEffect.SpellHandler;
            Spell spell = spellHandler.Spell;
            GameLiving caster = spellHandler.Caster;
            bool isConcentrationEffect = spellEffect.IsConcentrationEffect() && !spell.IsFocus;

            if (isConcentrationEffect && spellEffect.IsAllowedToPulse)
            {
                TickConcentrationEffect(spellEffect);
                return;
            }

            if (GameServiceUtils.ShouldTick(spellEffect.ExpireTick))
            {
                // A pulse effect cancels its own child effects to prevent them from being cancelled and immediately reapplied.
                // So only cancel them if their source is no longer active.
                if (spellHandler.PulseEffect?.IsActive != true)
                {
                    spellEffect.End();
                    return;
                }
            }

            // Make sure the effect actually has a next tick scheduled since some spells are marked as pulsing but actually don't.
            if (spellEffect.IsAllowedToPulse)
                TickPulsingEffect(spellEffect, spell, spellHandler, caster);
        }

        static void TickConcentrationEffect(ECSGameSpellEffect spellEffect)
        {
            if (!GameServiceUtils.ShouldTick(spellEffect.NextTick))
                return;

            ISpellHandler spellHandler = spellEffect.SpellHandler;
            GameLiving caster = spellHandler.Caster;

            // Only discard concentration from dormant ambient NPCs. GameBots
            // (persistent or temporary) are player-like casters, not scenery:
            // ending their buffs off-screen makes upkeep repeatedly preempt
            // travel until a human arrives. Their owned pets also remain live.
            // Reuse the cached gameplay-vicinity flag for ordinary NPCs; never
            // fabricate client visibility or scan for players on effect pulses.
            if (caster is GameNPC npcCaster &&
                (!npcCaster.IsAlive || npcCaster.ObjectState != GameObject.eObjectState.Active ||
                 (npcCaster is not GameBot && !npcCaster.IsVisibleToPlayersOrBots &&
                  !(npcCaster.Brain is IControlledBrain controlled &&
                    controlled.GetLivingOwner() is GameBot { IsAlive: true, ObjectState: GameObject.eObjectState.Active }))))
            {
                spellEffect.End();
                return;
            }

            spellEffect.Owner.effectListComponent.HandleConcentrationEffectRangeCheck(spellEffect);
            spellEffect.NextTick += spellEffect.PulseFreq;
        }

        static void TickPulsingEffect(ECSGameSpellEffect spellEffect, Spell spell, ISpellHandler spellHandler, GameLiving caster)
        {
            if (!GameServiceUtils.ShouldTick(spellEffect.NextTick))
                return;

            // Not every pulsing effect is a `ECSPulseEffect`. Snares and roots decreasing effect are also handled as pulsing spells for example.
            if (spellEffect is ECSPulseEffect pulseEffect)
            {
                // This should be unreachable.
                if (!caster.ActivePulseSpells.ContainsKey(spell.SpellType))
                {
                    pulseEffect.End();
                    return;
                }

                // Pulsing effects still tick normally but don't cast any spell if the caster is crowd controlled.
                // They also don't buffer, meaning the CC expiring doesn't necessarily make the pulsing effect tick immediately.
                // Accurate 1.65 behavior.
                if (!caster.IsCrowdControlled)
                {
                    if (spell.PulsePower > 0)
                    {
                        int pulsePower = BotSpellPower.PulseCost(caster, spell);
                        if (caster.Mana >= pulsePower)
                        {
                            caster.Mana -= pulsePower;
                            spellHandler.StartSpell(null);
                        }
                        else
                        {
                            if (caster is not GameBot)
                                (spellHandler as SpellHandler).MessageToCaster("You do not have enough power and your spell was canceled.", eChatType.CT_SpellExpires);
                            pulseEffect.End();
                            return;
                        }
                    }
                    else
                        spellHandler.StartSpell(null);

                    if (spell.IsHarmful && spell.SpellType is not eSpellType.SpeedDecrease)
                    {
                        if (!pulseEffect.Owner.IsCrowdControlled)
                            (spellHandler as SpellHandler).SendCastAnimation();
                    }
                }

                foreach (var pair in pulseEffect.ChildEffects)
                {
                    ECSGameSpellEffect childEffect = pair.Value;

                    if (GameServiceUtils.ShouldTick(childEffect.ExpireTick))
                    {
                        // Don't stop effects that were replaced.
                        // `ChildEffects` isn't updated when this happens and still keeps a reference.
                        // Primarily affects speed songs.
                        if (childEffect.IsBeingReplaced)
                        {
                            pulseEffect.RemoveChildIfCurrent(pair.Key, childEffect);
                            continue;
                        }

                        childEffect.End();
                        pulseEffect.RemoveChildIfCurrent(pair.Key, childEffect);
                    }
                }
            }
            else if (spellEffect is not ECSImmunityEffect && spellEffect.EffectType is not eEffect.Pulse && spell.IsSnare)
            {
                double factor = 2.0 - (spellEffect.Duration - spellEffect.GetRemainingTimeForClient()) / (spellEffect.Duration * 0.5);

                if (factor < 0)
                    factor = 0;
                else if (factor > 1)
                    factor = 1;

                factor *= spellEffect.Effectiveness; // Includes critical hit.
                spellEffect.Owner.BuffBonusMultCategory1.Set((int) eProperty.MaxSpeed, spellEffect, 1.0 - spellEffect.SpellHandler.Spell.Value * factor * 0.01);
                spellEffect.Owner.OnMaxSpeedChange();

                if (factor <= 0)
                {
                    spellEffect.End();
                    return;
                }
            }

            spellEffect.OnEffectPulse();
            spellEffect.NextTick += spellEffect.PulseFreq;
        }
    }
}
