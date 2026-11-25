using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// #521: the server's mez and stun refusals, read once, for the spell handlers (MesmerizeSpellHandler and
    /// StunSpellHandler, CCSpellHandler.cs) and for the bots' crowd-control planner (RvrCrowdControlPlan), which used to
    /// keep its own copy. The target takes the player's branch when <see cref="PvpRules.TakesPlayerRules(GameLiving, GameLiving)"/>
    /// says so: the player always, an autonomous gamebot against an enemy-realm player character. Not pure (reads effects).
    /// </summary>
    public static class CrowdControlImmunity
    {
        /// <summary>Whether a mez of <paramref name="duration"/> ms from <paramref name="caster"/> is refused; <paramref name="enraged"/>
        /// when the refusal is the monster rule "enraged" (below 75% health). <paramref name="stillMezzedCounts"/>: a target
        /// under the player's rule that is already mezzed is refused (the handler); the planner leaves held subjects alone.</summary>
        public static bool MezRefused(GameLiving caster, GameLiving target, long duration, out bool enraged, bool stillMezzedCounts = true)
        {
            enraged = false;
            if (target?.effectListComponent == null)
                return false;
            if (target.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) || target.HasAbility(Abilities.MezzImmunity))
                return true;
            bool playerRules = PvpRules.TakesPlayerRules(target, caster);
            if (playerRules)
            {
                if (stillMezzedCounts && target.effectListComponent.ContainsEffectForEffectType(eEffect.Mez))
                    return true;
            }
            else if (EffectListService.GetEffectOnTarget(target, eEffect.NPCMezImmunity) is NpcMezImmunityEffect immunity &&
                     !immunity.CanApplyNewEffect(System.Math.Max(1, duration)))
                return true;
            if (PvpRules.EnragedAgainstMez(playerRules, target is GameNPC, target.HealthPercent))
            {
                enraged = true;
                return true;
            }
            return false;
        }

        /// <summary>Whether a stun of <paramref name="duration"/> ms from <paramref name="caster"/> is refused.</summary>
        public static bool StunRefused(GameLiving caster, GameLiving target, long duration, bool ignoreStunImmunity = false,
            bool stillStunnedCounts = true)
        {
            if (target?.effectListComponent == null)
                return false;
            if (!ignoreStunImmunity && target.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) ||
                target.HasAbility(Abilities.StunImmunity))
                return true;
            if (PvpRules.TakesPlayerRules(target, caster))
                return stillStunnedCounts && target.effectListComponent.ContainsEffectForEffectType(eEffect.Stun);
            return EffectListService.GetEffectOnTarget(target, eEffect.NPCStunImmunity) is NpcStunImmunityEffect immunity &&
                !immunity.CanApplyNewEffect(System.Math.Max(1, duration));
        }
    }
}
